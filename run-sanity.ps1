$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "Building GWTP Windows Runtime..."
dotnet build (Join-Path $root "GWTP-Windows-POC.csproj")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Building Windows UIA Test Host..."
dotnet build (Join-Path $root "AmbiguityTestHost\AmbiguityTestHost.csproj")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Building Windows GUI sanity runner..."
dotnet build (Join-Path $root "WindowsRuntime.GuiTests\WindowsRuntime.GuiTests.csproj")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Running Windows GUI sanity..."
dotnet run --no-build --project (Join-Path $root "WindowsRuntime.GuiTests\WindowsRuntime.GuiTests.csproj")
exit $LASTEXITCODE
