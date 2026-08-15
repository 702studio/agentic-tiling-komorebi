<#
.SYNOPSIS
    Checks for updates and upgrades agentic-tiling-komorebi to the latest release.

.DESCRIPTION
    Queries the official GitHub release repository (702studio/agentic-tiling-komorebi),
    compares the current installation manifest version, and performs an in-place update.

.PARAMETER CheckOnly
    Only check if an update is available without applying it.

.PARAMETER Force
    Force reinstall or upgrade even if already on the latest version.

.PARAMETER NonInteractive
    Runs the upgrade in non-interactive mode without prompting.

.PARAMETER Json
    Outputs structured JSON for agents, scripts, and Tray Hub consumers.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$CheckOnly,
    [switch]$Force,
    [switch]$NonInteractive,
    [switch]$Json,
    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repo = '702studio/agentic-tiling-komorebi'
$apiUrl = "https://api.github.com/repos/$repo/releases/latest"
$bootstrapUrl = "https://raw.githubusercontent.com/$repo/main/bootstrap.ps1"

$configHome = if ($env:KOMOREBI_CONFIG_HOME) {
    $env:KOMOREBI_CONFIG_HOME
} else {
    Join-Path $env:USERPROFILE '.config\komorebi'
}
$manifestPath = Join-Path $configHome 'install-manifest.json'

function Get-CurrentVersion {
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        try {
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            if ($manifest.version) {
                return [string]$manifest.version
            }
        } catch {
            Write-Verbose "Could not parse version from manifest: $_"
        }
    }
    return '0.3.1'
}

function Get-LatestReleaseInfo {
    $headers = @{
        'User-Agent' = 'agentic-tiling-komorebi-updater'
    }
    if ($env:GITHUB_TOKEN) {
        $headers['Authorization'] = "token $env:GITHUB_TOKEN"
    }

    try {
        $release = Invoke-RestMethod -Uri $apiUrl -Headers $headers -TimeoutSec 10
        $version = $release.tag_name.TrimStart('v', 'V')
        return [pscustomobject]@{
            Success = $true
            Version = $version
            TagName = [string]$release.tag_name
            PublishedAt = [string]$release.published_at
            HtmlUrl = [string]$release.html_url
            Body = [string]$release.body
            Error = $null
        }
    } catch {
        return [pscustomobject]@{
            Success = $false
            Version = $null
            TagName = $null
            PublishedAt = $null
            HtmlUrl = $null
            Body = $null
            Error = $_.Exception.Message
        }
    }
}

$currentVersion = Get-CurrentVersion
$latestInfo = Get-LatestReleaseInfo

$updateAvailable = $false
if ($latestInfo.Success -and $latestInfo.Version) {
    try {
        $currVerObj = [version]$currentVersion
        $latestVerObj = [version]$latestInfo.Version
        $updateAvailable = ($latestVerObj -gt $currVerObj)
    } catch {
        $updateAvailable = ($latestInfo.Version -ne $currentVersion)
    }
}

if ($Force) {
    $updateAvailable = $true
}

$resultObj = [ordered]@{
    schemaVersion = 1
    productId = "702studio.agentic-tiling-komorebi"
    currentVersion = $currentVersion
    latestVersion = if ($latestInfo.Success) { $latestInfo.Version } else { "unknown" }
    updateAvailable = $updateAvailable
    releaseUrl = $latestInfo.HtmlUrl
    error = $latestInfo.Error
}

if ($CheckOnly) {
    if ($Json) {
        $resultObj | ConvertTo-Json -Depth 4
    } else {
        if (-not $latestInfo.Success) {
            Write-Host "[!] Failed to check for updates: $($latestInfo.Error)" -ForegroundColor Yellow
        } elseif ($updateAvailable) {
            Write-Host "[+] New update available!" -ForegroundColor Green
            Write-Host "   Current version : v$currentVersion" -ForegroundColor Gray
            Write-Host "   Latest version  : v$($latestInfo.Version)" -ForegroundColor Cyan
            Write-Host "   Release notes   : $($latestInfo.HtmlUrl)" -ForegroundColor DarkGray
            Write-Host "`nRun 'komorebi-update' or 'wm update' to apply the update." -ForegroundColor Yellow
        } else {
            Write-Host "[OK] You are running the latest version (v$currentVersion)." -ForegroundColor Green
        }
    }
    return
}

if (-not $updateAvailable -and -not $Force) {
    if ($Json) {
        $resultObj['status'] = 'already_up_to_date'
        $resultObj | ConvertTo-Json -Depth 4
    } else {
        Write-Host "[OK] You are already on the latest version (v$currentVersion)." -ForegroundColor Green
        Write-Host "   Use 'komorebi-update -Force' to force reinstallation if needed." -ForegroundColor DarkGray
    }
    return
}

Write-Host "`n[*] Updating agentic-tiling-komorebi..." -ForegroundColor Cyan
Write-Host "   Current: v$currentVersion -> Latest: v$($latestInfo.Version)" -ForegroundColor Gray
Write-Host "   Executing installer pipeline via bootstrap.ps1...`n" -ForegroundColor DarkGray

$bootstrapScript = Invoke-RestMethod -Uri $bootstrapUrl -TimeoutSec 15

$params = @{}
if ($NonInteractive) { $params['NonInteractive'] = $true }
if ($Quiet) { $params['Quiet'] = $true }
if ($Force) { $params['Force'] = $true }

$scriptBlock = [ScriptBlock]::Create($bootstrapScript)
& $scriptBlock @params

Write-Host "`n[+] agentic-tiling-komorebi successfully updated!" -ForegroundColor Green
