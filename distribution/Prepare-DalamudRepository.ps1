param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https://')]
    [string]$DownloadUrl,

    [Parameter(Mandatory = $false)]
    [string]$OutputDirectory = "artifacts/dalamud-repository"
)

$ErrorActionPreference = "Stop"
$templatePath = Join-Path $PSScriptRoot "pluginmaster.template.json"
$outputPath = Join-Path $OutputDirectory "pluginmaster.json"

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$manifest = Get-Content -Raw $templatePath
$manifest = $manifest.Replace("__VERSION__", $Version).Replace("__DOWNLOAD_URL__", $DownloadUrl)
$parsed = $manifest | ConvertFrom-Json
if ($parsed.Count -ne 1 -or $parsed[0].InternalName -ne "Splatoon") {
    throw "Generated manifest validation failed."
}

Set-Content -Path $outputPath -Value $manifest -Encoding UTF8
Write-Host "Prepared $outputPath. Nothing was uploaded or published."
