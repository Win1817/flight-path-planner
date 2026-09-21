# Builds the portable Windows executable: dist\win-x64\FlightPathPlanner.exe (single file, icon included, no installer,
# no .NET install required). Run from this folder:  .\publish-windows.ps1
$ErrorActionPreference = "Stop"
dotnet publish FlightPathPlanner -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/win-x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Get-Item dist\win-x64\FlightPathPlanner.exe | Select-Object FullName, @{n = "SizeMB"; e = { [math]::Round($_.Length / 1MB, 1) } }
