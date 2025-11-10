using System;
using System.IO;

namespace WebAecRevit;

/// <summary>
/// Configuration for web application URL
/// Supports both development and production modes
/// </summary>
public static class WebAppConfig
{
    private const string DefaultDevUrl = "http://localhost:5173/";
    private const string DefaultProdUrl = "http://localhost:5173/"; // Can be changed to production URL
    
    /// <summary>
    /// Gets the web application folder path (for local file serving)
    /// </summary>
    public static string? GetWebAppFolder()
    {
        // Try to find web app folder relative to plugin location
        var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var pluginDir = Path.GetDirectoryName(assemblyLocation);
        
        if (!string.IsNullOrEmpty(pluginDir))
        {
            var webAppFolder = Path.Combine(pluginDir, "web");
            if (Directory.Exists(webAppFolder))
            {
                return webAppFolder;
            }
        }
        
        // Fallback: check deployment directory
        var deployDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Autodesk",
            "Revit",
            "Addins",
            "2025",
            "WebAecRevit",
            "web"
        );
        
        if (Directory.Exists(deployDir))
        {
            return deployDir;
        }
        
        return null;
    }
    
    /// <summary>
    /// Gets the web application URL based on build configuration
    /// In Debug mode, uses dev server. In Release, uses local files via virtual host.
    /// Can also be overridden via environment variable WEB_APP_URL
    /// </summary>
    public static string GetWebAppUrl()
    {
        // Check for environment variable override
        var envUrl = Environment.GetEnvironmentVariable("WEB_APP_URL");
        if (!string.IsNullOrEmpty(envUrl))
        {
            return envUrl;
        }
        
        // Check for config file
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebAecRevit",
            "webapp.config"
        );
        
        if (File.Exists(configPath))
        {
            try
            {
                var configUrl = File.ReadAllText(configPath).Trim();
                if (!string.IsNullOrEmpty(configUrl))
                {
                    return configUrl;
                }
            }
            catch
            {
                // If config file read fails, fall back to defaults
            }
        }
        
#if DEBUG
        // In debug, prefer dev server if available, otherwise use local files
        var webFolder = GetWebAppFolder();
        if (webFolder != null && Directory.Exists(webFolder))
        {
            return "http://app.localhost/index.html";
        }
        return DefaultDevUrl;
#else
        // In release, use local files via virtual host
        return "http://app.localhost/index.html";
#endif
    }
    
    /// <summary>
    /// Checks if local web app files are available
    /// </summary>
    public static bool HasLocalWebApp()
    {
        var webFolder = GetWebAppFolder();
        return webFolder != null && Directory.Exists(webFolder);
    }
    
    /// <summary>
    /// Gets the Revit API server URL (defaults to localhost:48881)
    /// </summary>
    public static string GetApiUrl()
    {
        var envApiUrl = Environment.GetEnvironmentVariable("REVIT_API_URL");
        if (!string.IsNullOrEmpty(envApiUrl))
        {
            return envApiUrl;
        }
        
        return "http://localhost:48881";
    }
}

