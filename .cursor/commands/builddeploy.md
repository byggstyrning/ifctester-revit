# Build and Deploy Command Guide

## Purpose
This command instructs Cursor to run the build and deploy script for the IfcTester Revit Plugin project.

## When to Use
Run this command when you need to:
- Build the web application and Revit plugin together
- Deploy the built components to the Revit addin directory
- Prepare the project for testing in Revit 2025

## How to Execute

### Basic Usage
When the user requests to build and deploy, run the PowerShell script located at:
```
C:\code\ifctester-revit\scripts\deploy.ps1
```

### Command
```powershell
& "C:\code\ifctester-revit\scripts\deploy.ps1"
```

### With Parameters
The script accepts the following parameters:

1. **Configuration** (optional, default: "Debug R25")
   ```powershell
   & "C:\code\ifctester-revit\scripts\deploy.ps1" -Configuration "Release R25"
   ```

2. **SkipWebBuild** (optional flag)
   ```powershell
   & "C:\code\ifctester-revit\scripts\deploy.ps1" -SkipWebBuild
   ```

## What the Script Does

1. Downloads Python packages for Pyodide
2. Builds the web application (unless `-SkipWebBuild` is specified)
3. Builds the Revit plugin using .NET
4. Copies the web app to the plugin publish directory
5. Deploys everything to `%APPDATA%\Autodesk\Revit\Addins\2025\IfcTesterRevit`
6. Creates a `built` folder in the revit directory with all files for easy distribution

## Notes for Cursor

- Always run this from PowerShell (not cmd)
- The script will automatically install npm dependencies if needed
- The script stops on any error (`$ErrorActionPreference = "Stop"`)
- The deployment directory must exist for successful deployment
- After successful deployment, the plugin will be available in Revit 2025

## Example Usage in Cursor

When a user says:
- "Build and deploy the project"
- "Deploy the plugin"
- "Run the build script"
- "Build everything"

Execute:
```powershell
& "C:\code\ifctester-revit\scripts\deploy.ps1"
```

