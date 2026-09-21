<#
  Builds the Angular SPA (ng build writes straight into AiLab.Api/wwwroot, per angular.json),
  then publishes the ASP.NET Core host as a self-contained artifact under ./publish.

  Usage: pwsh ./publish.ps1 [-Configuration Release] [-OutputDir ./publish]
#>
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "publish"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

$wwwroot = Join-Path $root "src/AiLab.Api/wwwroot"
Write-Host "==> Clearing $wwwroot"
if (Test-Path $wwwroot) {
    Get-ChildItem $wwwroot -Force | Where-Object { $_.Name -ne ".gitkeep" } | Remove-Item -Recurse -Force
}

Write-Host "==> Building Angular SPA ($Configuration)"
Push-Location (Join-Path $root "src/AiLab.UI")
try {
    npm ci
    npm run build
} finally {
    Pop-Location
}

Write-Host "==> Publishing AiLab.Api ($Configuration)"
$apiProject = Join-Path $root "src/AiLab.Api/AiLab.Api.csproj"
$publishOut = Join-Path $root $OutputDir
dotnet publish $apiProject -c $Configuration -o $publishOut

Write-Host "==> Done. Published to $publishOut"
