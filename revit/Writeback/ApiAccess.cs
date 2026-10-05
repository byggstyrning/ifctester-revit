using System.IO;

namespace IfcTesterRevit.Writeback;

/// <summary>What the server does with a request, given its Origin header.</summary>
public sealed class OriginDecision
{
    private OriginDecision(bool refuse, string? allowOrigin)
    {
        Refuse = refuse;
        AllowOrigin = allowOrigin;
    }

    /// <summary>Answer 403 and do nothing: a page on a foreign origin sent the request.</summary>
    public bool Refuse { get; }

    /// <summary>The value for Access-Control-Allow-Origin, or null to send none.</summary>
    public string? AllowOrigin { get; }

    public static readonly OriginDecision NoOrigin = new(false, null);
    public static readonly OriginDecision Foreign = new(true, null);
    public static OriginDecision Allowed(string origin) => new(false, origin);
}

/// <summary>
/// The web origins whose pages may call the add-in's HTTP API: the page the server serves itself
/// and the Vite dev and preview servers. Every other website the user has open in the same
/// browser can reach localhost too, so a request that carries a foreign Origin is refused outright
/// (a simple POST arrives without a preflight) and gets no CORS header. A request without an
/// Origin (a same-origin GET, curl, the verify scripts) is not a cross-site browser request and is
/// served as before.
/// </summary>
public sealed class OriginPolicy
{
    /// <summary>Extra origins, separated by ';' or ',', e.g. a dev server on another port.</summary>
    public const string ExtraOriginsVariable = "IFCTESTER_ALLOWED_ORIGINS";

    private readonly HashSet<string> _allowed;

    public OriginPolicy(IEnumerable<string?> origins)
    {
        _allowed = new HashSet<string>(origins.Select(Normalize).OfType<string>(), StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> Allowed => _allowed;

    /// <param name="port">The port the server listens on; its own page is served from http://localhost:{port}.</param>
    /// <param name="extra">The value of <see cref="ExtraOriginsVariable"/>, if set.</param>
    public static OriginPolicy ForServer(int port, string? extra)
    {
        var origins = new List<string?> { $"http://localhost:{port}", "http://localhost:5173", "http://localhost:4173" };
        if (!string.IsNullOrWhiteSpace(extra)) origins.AddRange(extra!.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries));
        return new OriginPolicy(origins);
    }

    /// <summary>scheme://host[:port] in lower case, without a default port or a trailing slash; null for anything else ("null", a path, garbage).</summary>
    public static string? Normalize(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return null;
        if (!Uri.TryCreate(origin!.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }

    public OriginDecision Check(string? origin)
    {
        if (origin == null) return OriginDecision.NoOrigin;
        var normalized = Normalize(origin);
        return normalized != null && _allowed.Contains(normalized) ? OriginDecision.Allowed(origin.Trim()) : OriginDecision.Foreign;
    }
}

/// <summary>
/// The files GET /pset-files/read and GET /ids-files/read may return: files the add-in itself has
/// named to the page (an export setup's property set file or mapping table, the overrides of an
/// export, a saved file, a file remembered for the open model) or that the user picked through the
/// add-in's own file dialog, and the files directly in the given folders. Anything else is
/// refused, so the endpoints cannot be used to read arbitrary files off the machine.
/// </summary>
public sealed class ReadablePaths
{
    private readonly object _lock = new();
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _folders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The full path, or null when the path is empty, relative or malformed.</summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var trimmed = path!.Trim();
        if (!Path.IsPathRooted(trimmed)) return null;
        try
        {
            return Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return null;
        }
    }

    public void Allow(string? path)
    {
        var full = Normalize(path);
        if (full == null) return;
        lock (_lock) _files.Add(full);
    }

    public void AllowAll(IEnumerable<string?> paths)
    {
        foreach (var path in paths) Allow(path);
    }

    /// <summary>Every file directly in the folder, not in its subfolders.</summary>
    public void AllowFolder(string? folder)
    {
        var full = Normalize(folder);
        if (full == null) return;
        lock (_lock) _folders.Add(full);
    }

    public bool IsAllowed(string? path)
    {
        var full = Normalize(path);
        if (full == null) return false;
        var folder = Path.GetDirectoryName(full);
        lock (_lock)
        {
            return _files.Contains(full) || (folder != null && _folders.Contains(folder.TrimEnd(Path.DirectorySeparatorChar)));
        }
    }
}
