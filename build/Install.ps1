<#
.SYNOPSIS
  Installs G923 Mapper for the current user (no administrator rights needed) and checks for the ViGEmBus driver.

.DESCRIPTION
  Copies G923Mapper.exe to %LOCALAPPDATA%\Programs\G923Mapper, adds a Start menu shortcut, and tells you
  if the ViGEmBus driver (which creates the virtual Xbox 360 controller) is missing. The driver itself is
  the one thing that does need administrator rights, so it is installed by its own installer, not by this script.

.EXAMPLE
  .\Install.ps1
  .\Install.ps1 -DesktopShortcut -Launch
#>
param(
    [switch]$DesktopShortcut,
    [switch]$Launch,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\G923Mapper'),
    [switch]$NoShortcuts,  # for testing: copy the files only
    [switch]$NoRegister,   # do not add the entry in Windows Settings > Installed apps
    [switch]$InstallDrivers,  # install missing drivers without asking first
    [switch]$WithHidHide,     # also install HidHide (optional: hides the real wheel from games)
    [switch]$DryRun           # show which driver downloads would be used, change nothing about drivers
)

$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'G923Mapper.exe'
if (-not (Test-Path $source)) { throw "G923Mapper.exe was not found next to this script ($PSScriptRoot)." }

$target = $InstallDir
New-Item -ItemType Directory -Force $target | Out-Null

# Stop a running copy so the file can be replaced.
Get-Process G923Mapper -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$target*" } | Stop-Process -Force
Start-Sleep -Milliseconds 500
Copy-Item $source (Join-Path $target 'G923Mapper.exe') -Force
Copy-Item (Join-Path $PSScriptRoot 'Uninstall.ps1') $target -Force -ErrorAction SilentlyContinue
$exe = Join-Path $target 'G923Mapper.exe'

function New-Shortcut($path) {
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($path)
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $target
    $lnk.Description = 'Use a Logitech G923 as an Xbox 360 controller'
    $lnk.Save()
}
if (-not $NoShortcuts) {
    $startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    New-Shortcut (Join-Path $startMenu 'G923 Mapper.lnk')
    if ($DesktopShortcut) { New-Shortcut (Join-Path ([Environment]::GetFolderPath('Desktop')) 'G923 Mapper.lnk') }
}
"Installed to $target"

# An entry in Windows Settings > Apps > Installed apps, so it can be uninstalled from there. Current user only.
if (-not $NoRegister) {
    $version = (Get-Item $exe).VersionInfo.ProductVersion
    if (-not $version) { $version = '1.0.0' }
    $uninstallCmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $target 'Uninstall.ps1')`" -InstallDir `"$target`" -Pause"
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\G923Mapper'
    New-Item -Path $key -Force | Out-Null
    $values = @{
        DisplayName     = 'G923 Mapper'
        DisplayVersion  = $version
        Publisher       = 'G923 Mapper'
        InstallLocation = $target
        DisplayIcon     = $exe
        UninstallString = $uninstallCmd
        InstallDate     = (Get-Date -Format 'yyyyMMdd')
    }
    foreach ($name in $values.Keys) { New-ItemProperty -Path $key -Name $name -Value $values[$name] -PropertyType String -Force | Out-Null }
    New-ItemProperty -Path $key -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $key -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $key -Name EstimatedSize -Value ([int]((Get-Item $exe).Length / 1KB)) -PropertyType DWord -Force | Out-Null
    "Listed in Windows Settings > Installed apps."
}

# ---------------------------------------------------------------- drivers
# Both are third-party drivers by Nefarius. They need administrator permission, which Windows asks for itself.

function Get-LatestAsset([string]$Repo, [string]$Pattern) {
    $release = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest" -Headers @{ 'User-Agent' = 'G923Mapper-Installer' }
    $asset = $release.assets | Where-Object { $_.name -like $Pattern } | Select-Object -First 1
    if (-not $asset) { throw "No download matching '$Pattern' in $Repo $($release.tag_name)." }
    [pscustomobject]@{ Name = $asset.name; Url = $asset.browser_download_url; Tag = $release.tag_name; SizeMB = [math]::Round($asset.size / 1MB, 1) }
}

function Install-Driver([string]$Title, [string]$Repo, [string]$Pattern, [string]$Service, [bool]$Required) {
    $svc = Get-Service -Name $Service -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -eq 'Running') { "${Title}: already installed and running."; return }

    if ($svc) { Write-Warning "$Title is installed but its service is not running. Reinstalling usually fixes that." }
    elseif ($Required) { Write-Warning "$Title is not installed. G923 Mapper needs it to create the virtual controller." }
    else { "${Title}: not installed (optional)." }

    try { $asset = Get-LatestAsset $Repo $Pattern }
    catch {
        Write-Warning "Could not look up the $Title download: $($_.Exception.Message)"
        "Get it from https://github.com/$Repo/releases and run it, then restart G923 Mapper."
        return
    }
    "${Title} $($asset.Tag): $($asset.Name) ($($asset.SizeMB) MB) from github.com/$Repo"
    if ($DryRun) { "  (dry run: not downloading or installing)"; return }

    if (-not $InstallDrivers) {
        $answer = Read-Host "Download and install $Title now? Windows will ask for administrator permission. (y/n)"
        if ($answer -notlike 'y*') { "Skipped. Get it later from https://github.com/$Repo/releases"; return }
    }

    $file = Join-Path ([IO.Path]::GetTempPath()) $asset.Name
    Invoke-WebRequest $asset.Url -OutFile $file -UseBasicParsing
    $sig = Get-AuthenticodeSignature $file
    if ($sig.Status -ne 'Valid') {
        Remove-Item $file -ErrorAction SilentlyContinue
        Write-Warning "The downloaded installer's digital signature is not valid ($($sig.Status)), so it was not run. Download it yourself from https://github.com/$Repo/releases"
        return
    }
    "Signed by: $($sig.SignerCertificate.Subject)"
    "Running the installer (follow its prompts)..."
    Start-Process -FilePath $file -Verb RunAs -Wait
    Remove-Item $file -ErrorAction SilentlyContinue

    $after = Get-Service -Name $Service -ErrorAction SilentlyContinue
    if ($after -and $after.Status -eq 'Running') { "$Title is installed and running." }
    else { Write-Warning "$Title does not look installed yet. If the installer asked for a restart, restart Windows and run G923 Mapper again." }
}

Install-Driver 'ViGEmBus driver' 'nefarius/ViGEmBus' 'ViGEmBus_*.exe' 'ViGEmBus' $true
if ($WithHidHide) {
    Install-Driver 'HidHide' 'nefarius/HidHide' 'HidHide_*.exe' 'HidHide' $false
} elseif (-not (Get-Service -Name HidHide -ErrorAction SilentlyContinue)) {
    "Optional: run Install.ps1 -WithHidHide to also install HidHide, which hides the real wheel from games so steering cannot fight itself."
}
if ($Launch) { Start-Process $exe }
