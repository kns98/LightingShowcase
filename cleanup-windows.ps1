[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$Build,

    [switch]$Deep
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Remove-PathSafely {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (Test-Path -LiteralPath $Path) {
        Write-Host "Removing: $Path"
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

$projectRoot = (Get-Location).Path
$windowsSolution = Join-Path $projectRoot "LightingShowcase.Windows.sln"

if (-not (Test-Path -LiteralPath $windowsSolution)) {
    throw @"
LightingShowcase.Windows.sln was not found.

Run this script from the extracted project root, for example:

    cd C:\Users\YOUR_NAME\Downloads\LightingShowcase-master
    powershell -ExecutionPolicy Bypass -File .\cleanup-windows.ps1
"@
}

Write-Host ""
Write-Host "Project root: $projectRoot"
Write-Host "Configuration: $Configuration"
Write-Host ""

# Retired mixed-platform files. These must not remain after extracting over an
# older copy of the repository.
$obsoleteFiles = @(
    "LightingShowcase.sln",
    "LightingShowcase.csproj",
    "LightingShowcase.CommandLine\LightingShowcase.CommandLine.csproj",
    "validate-platform-split.sh",
    "PORT_VALIDATION.txt"
)

foreach ($relativePath in $obsoleteFiles) {
    Remove-PathSafely -Path (Join-Path $projectRoot $relativePath)
}

# Remove obsolete standalone workflow files that still invoke the retired
# platform-validation script. The primary build workflow in this package is
# already validation-free and must be retained.
$workflowDirectory = Join-Path $projectRoot ".github\workflows"
if (Test-Path -LiteralPath $workflowDirectory) {
    Get-ChildItem -LiteralPath $workflowDirectory -File |
        Where-Object { $_.Name -ne "dotnet-desktop.yml" } |
        ForEach-Object {
            $match = Select-String -LiteralPath $_.FullName -SimpleMatch "validate-platform-split.sh" -Quiet
            if ($match) {
                Remove-PathSafely -Path $_.FullName
            }
        }
}

# Remove Visual Studio state and all compiled/intermediate output.
Remove-PathSafely -Path (Join-Path $projectRoot ".vs")

Get-ChildItem -LiteralPath $projectRoot -Directory -Recurse -Force |
    Where-Object { $_.Name -in @("bin", "obj") } |
    Sort-Object { $_.FullName.Length } -Descending |
    ForEach-Object {
        Remove-PathSafely -Path $_.FullName
    }

# Optional deeper cleanup for stubborn restore/cache issues.
if ($Deep) {
    Write-Host ""
    Write-Host "Clearing local NuGet caches..."
    & dotnet nuget locals all --clear
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet nuget locals all --clear failed with exit code $LASTEXITCODE."
    }
}

# Verify the expected separated Windows projects exist.
$requiredFiles = @(
    "LightingShowcase.Windows.sln",
    "LightingShowcase.Windows.csproj",
    "LightingShowcase.CommandLine\LightingShowcase.CommandLine.Windows.csproj",
    "LightingShowcase.Core\LightingShowcase.Core.csproj"
)

$missing = @(
    foreach ($relativePath in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $projectRoot $relativePath))) {
            $relativePath
        }
    }
)

if ($missing.Count -gt 0) {
    throw "Required platform-split files are missing:`n - $($missing -join "`n - ")"
}

Write-Host ""
Write-Host "Cleanup complete."
Write-Host ""

if ($Build) {
    Write-Host "Restoring Windows solution..."
    & dotnet restore $windowsSolution
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed with exit code $LASTEXITCODE."
    }

    Write-Host ""
    Write-Host "Building Windows solution..."
    & dotnet build $windowsSolution `
        --configuration $Configuration `
        --no-restore `
        -p:ContinuousIntegrationBuild=true

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }

    Write-Host ""
    Write-Host "Windows build succeeded."
}
else {
    Write-Host "Build with:"
    Write-Host "  dotnet restore .\LightingShowcase.Windows.sln"
    Write-Host "  dotnet build .\LightingShowcase.Windows.sln -c $Configuration --no-restore"
    Write-Host ""
    Write-Host "Or run cleanup and build together:"
    Write-Host "  powershell -ExecutionPolicy Bypass -File .\cleanup-windows.ps1 -Build -Configuration $Configuration"
}
