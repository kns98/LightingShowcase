$ErrorActionPreference = "Stop"

Get-ChildItem -Path $PSScriptRoot -Recurse -Force -Directory |
    Where-Object { $_.Name -in @("bin", "obj", ".vs", ".obj") } |
    Sort-Object FullName -Descending |
    Remove-Item -Recurse -Force

dotnet restore "$PSScriptRoot\LightingShowcase.Windows.sln"
dotnet build "$PSScriptRoot\LightingShowcase.Windows.sln" -c Debug --no-restore
