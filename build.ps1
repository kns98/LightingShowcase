$ErrorActionPreference = "Stop"

dotnet restore .\LightingShowcase.Windows.sln
dotnet build .\LightingShowcase.Windows.sln -c Debug --no-restore
