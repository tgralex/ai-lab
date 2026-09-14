<#
  Builds the Angular SPA, copies its output into AiLab.Api/wwwroot, then publishes the
  ASP.NET Core host as a self-contained artifact under ./publish.

  Usage: pwsh ./publish.ps1 [-Configuration Release] [-OutputDir ./publish]
#>
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "publish"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "==> Building Angular SPA ($Configuration)"
Push-Location (Join-Path $root "src/AiLab.UI")
try {
    npm ci
    npm run build
} finally {
    Pop-Location
}

$angularOut = Join-Path $root "src/AiLab.UI/dist/AiLab.UI/browser"
$wwwroot = Join-Path $root "src/AiLab.Api/wwwroot"

Write-Host "==> Copying Angular build into $wwwroot"
if (Test-Path $wwwroot) {
    Get-ChildItem $wwwroot -Force | Where-Object { $_.Name -ne ".gitkeep" } | Remove-Item -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
Copy-Item (Join-Path $angularOut "*") $wwwroot -Recurse -Force

Write-Host "==> Publishing AiLab.Api ($Configuration)"
$apiProject = Join-Path $root "src/AiLab.Api/AiLab.Api.csproj"
$publishOut = Join-Path $root $OutputDir
dotnet publish $apiProject -c $Configuration -o $publishOut

Write-Host "==> Done. Published to $publishOut"
