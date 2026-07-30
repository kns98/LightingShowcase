param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot 'publish')
)

$ErrorActionPreference = 'Stop'

$desktop = Join-Path $OutputRoot 'desktop-win-x64'
$commandLine = Join-Path $OutputRoot 'commandline-win-x64'

Remove-Item -Recurse -Force $desktop, $commandLine -ErrorAction SilentlyContinue

dotnet restore (Join-Path $PSScriptRoot 'LightingShowcase.Windows.sln')

dotnet publish (Join-Path $PSScriptRoot 'LightingShowcase.Windows.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    -p:SelfContained=false `
    -p:UseAppHost=true `
    --output $desktop

dotnet publish (Join-Path $PSScriptRoot 'LightingShowcase.CommandLine\LightingShowcase.CommandLine.Windows.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    -p:SelfContained=false `
    -p:UseAppHost=true `
    --output $commandLine

$requiredDesktopPlugins = @(
    'LightingShowcase.ImportExport.Obj.dll',
    'LightingShowcase.ImportExport.Stl.dll',
    'LightingShowcase.ImportExport.Ply.dll',
    'LightingShowcase.ImportExport.ThreeDs.dll',
    'LightingShowcase.ImportExport.Gltf.dll',
    'LightingShowcase.ImportExport.PropXml.dll',
    'LightingShowcase.ImportExport.Fbx.dll'
)

$missingDesktopPlugins = $requiredDesktopPlugins | Where-Object {
    -not (Test-Path (Join-Path $desktop $_))
}
if ($missingDesktopPlugins) {
    throw "Windows desktop publish is missing file-format plugins: $($missingDesktopPlugins -join ', ')"
}

$cli = Join-Path $commandLine 'LightingShowcase.CommandLine.exe'
if (-not (Test-Path $cli)) {
    throw "Windows command-line executable was not published: $cli"
}

& $cli --help | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Windows command-line smoke test failed.'
}

$forbiddenRuntimeFiles = @(
    'coreclr.dll',
    'clrjit.dll',
    'hostfxr.dll',
    'hostpolicy.dll',
    'System.Private.CoreLib.dll',
    'dotnet.exe'
)

$runtimeFiles = Get-ChildItem $desktop, $commandLine -Recurse -File |
    Where-Object { $forbiddenRuntimeFiles -contains $_.Name }

if ($runtimeFiles) {
    $runtimeFiles | ForEach-Object { Write-Error "Bundled .NET runtime file found: $($_.FullName)" }
    throw 'Publish output must not contain the .NET/CLR runtime.'
}

Write-Host "Windows desktop published to $desktop"
Write-Host "Windows CLI published to $commandLine"
