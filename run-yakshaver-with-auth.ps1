# PowerShell script to run YakShaver.SmartAss with GitHub authentication

# Get the directory where this script is located
$ScriptDir = $PSScriptRoot

# Construct the full paths to the appsettings.json and .csproj file
# Assumes the project is in a subdirectory named 'YakShaver.SmartAss' relative to this script
$appSettingsPath = Join-Path -Path $ScriptDir -ChildPath "YakShaver.SmartAss/appsettings.json"
$projectPath = Join-Path -Path $ScriptDir -ChildPath "YakShaver.SmartAss/YakShaver.SmartAss.csproj"

Write-Host "Script Directory: $ScriptDir"
Write-Host "App Settings Path: $appSettingsPath"
Write-Host "Project Path: $projectPath"

if (-not (Test-Path $appSettingsPath)) {
    Write-Error "Error: appsettings.json not found at '$appSettingsPath'. Please ensure the script is in the correct parent directory of the 'YakShaver.SmartAss' project folder."
    exit 1
}

if (-not (Test-Path $projectPath)) {
    Write-Error "Error: Project file not found at '$projectPath'."
    exit 1
}

# Read GitHub PAT from appsettings.json
$githubPat = $null
try {
    $config = Get-Content -Raw -Path $appSettingsPath | ConvertFrom-Json
    $githubPat = $config.GitHub.PAT
}
catch {
    Write-Error "Error reading or parsing appsettings.json: $_"
    exit 1
}

if ($null -eq $githubPat -or $githubPat -eq "YOUR_GITHUB_PAT_HERE_OR_SET_ENV_VAR" -or $githubPat -eq "") {
    Write-Warning "GitHub PAT is not set, is empty, or is a placeholder in '$appSettingsPath'."
    Write-Warning "The GitHub MCP server might fail to authenticate. Proceeding without setting GITHUB_TOKEN."
} else {
    Write-Host "Setting GITHUB_TOKEN environment variable for this session..."
    $env:GITHUB_TOKEN = $githubPat
    # For verification (optional):
    # Write-Host "GITHUB_TOKEN set to '$($env:GITHUB_TOKEN.Substring(0, [System.Math]::Min($env:GITHUB_TOKEN.Length, 7)))***'"
}

Write-Host "Starting YakShaver.SmartAss application..."

# Run the .NET application
# Ensure your default terminal is PowerShell 7 or later, or run this script using 'pwsh.exe ./run-yakshaver-with-auth.ps1'
dotnet run --project $projectPath

# Note: The GITHUB_TOKEN environment variable set this way is only active
# for the duration of this script and the processes it launches. 