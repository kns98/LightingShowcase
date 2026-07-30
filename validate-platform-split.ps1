$ErrorActionPreference = 'Stop'

function Assert-Exists([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Platform split validation failed: missing file: $Path"
    }
}

function Assert-Contains([string]$Path, [string]$Expected) {
    $content = Get-Content -LiteralPath $Path -Raw
    if (-not $content.Contains($Expected)) {
        throw "Platform split validation failed: $Path does not reference $Expected"
    }
}

function Assert-DoesNotContain([string]$Path, [string]$Forbidden) {
    $content = Get-Content -LiteralPath $Path -Raw
    if ($content.Contains($Forbidden)) {
        throw "Platform split validation failed: $Path contains forbidden reference $Forbidden"
    }
}

$windowsFiles = @(
    'LightingShowcase.Windows.sln',
    'LightingShowcase.Windows.csproj',
    'LightingShowcase.CommandLine/LightingShowcase.CommandLine.Windows.csproj',
    'build.ps1',
    'publish-windows.ps1'
)

$linuxFiles = @(
    'LightingShowcase.Linux.sln',
    'LightingShowcase.CommandLine/LightingShowcase.CommandLine.Linux.csproj',
    'build.sh',
    'publish-linux.sh',
    'LightingShowcase.CommandLine/render.sh'
)

$windowsFiles + $linuxFiles | ForEach-Object { Assert-Exists $_ }

Assert-Contains 'LightingShowcase.Windows.sln' 'LightingShowcase.CommandLine\LightingShowcase.CommandLine.Windows.csproj'
Assert-Contains 'LightingShowcase.Windows.csproj' 'LightingShowcase.CommandLine/LightingShowcase.CommandLine.Windows.csproj'

foreach ($file in $windowsFiles) {
    Assert-DoesNotContain $file 'LightingShowcase.CommandLine.Linux.csproj'
    Assert-DoesNotContain $file 'LightingShowcase.Linux.sln'
}

foreach ($file in $linuxFiles) {
    Assert-DoesNotContain $file 'LightingShowcase.CommandLine.Windows.csproj'
    Assert-DoesNotContain $file 'LightingShowcase.Windows.sln'
    Assert-DoesNotContain $file 'LightingShowcase.Windows.csproj'
}


$rendererRequest = Get-Content -LiteralPath 'LightingShowcase.CommandLine/RenderRequest.cs' -Raw
foreach ($renderer in @('raster', 'raster-vulkan', 'vulkan', 'cpu')) {
    if (-not $rendererRequest.Contains($renderer)) {
        throw "Platform split validation failed: command-line renderer option is missing: $renderer"
    }
}
Assert-Contains 'LightingShowcase.CommandLine/Program.cs' '--renderer <name>'

Write-Host 'Platform split validation passed.'
