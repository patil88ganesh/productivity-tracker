$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$appPublish = Join-Path $artifacts "app"
$installerPublish = Join-Path $artifacts "installer"
$dist = Join-Path $root "dist"
$payload = Join-Path $root "MiniStopwatch.Installer\Payload.zip"

foreach ($path in @($artifacts, $dist)) {
    if (Test-Path $path) {
        Get-ChildItem -Force $path | ForEach-Object {
            if ($_.PSIsContainer -and $_.Name -in @("app", "installer")) {
                Get-ChildItem -Force $_.FullName | Remove-Item -Recurse -Force
            }
            else {
                Remove-Item -Recurse -Force $_.FullName
            }
        }
    }
}

if (Test-Path $payload) {
    Remove-Item -Force $payload
}

dotnet build (Join-Path $root "MiniStopwatch.sln") -c Release
if ($LASTEXITCODE -ne 0) { throw "Solution build failed." }

dotnet run --project (Join-Path $root "MiniStopwatch.Tests") -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

dotnet publish (Join-Path $root "MiniStopwatch.App") `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $appPublish
if ($LASTEXITCODE -ne 0) { throw "Application publish failed." }

Compress-Archive -Path (Join-Path $appPublish "*") -DestinationPath $payload

dotnet build (Join-Path $root "MiniStopwatch.Installer") `
    -c Release `
    -o $installerPublish
if ($LASTEXITCODE -ne 0) { throw "Installer build failed." }

New-Item -ItemType Directory -Path (Join-Path $dist "portable") -Force | Out-Null
Copy-Item (Join-Path $installerPublish "ProductivityTracker-Setup.exe") `
    (Join-Path $dist "ProductivityTracker-Setup.exe")
Copy-Item (Join-Path $appPublish "*") `
    (Join-Path $dist "portable") `
    -Recurse

Write-Host ""
Write-Host "Build complete:"
Write-Host "  Installer: $(Join-Path $dist 'ProductivityTracker-Setup.exe')"
Write-Host "  Portable:  $(Join-Path $dist 'portable\ProductivityTracker.exe')"
