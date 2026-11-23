<#
.SYNOPSIS
  Removes G923 Mapper for the current user. Your profiles and settings in %APPDATA%\G923Mapper are kept unless -RemoveData is given.

.DESCRIPTION
  Shows the wheel to games again if HidHide was hiding it (Windows asks for administrator permission for that),
  stops the app, removes its files, shortcuts, start-with-Windows entry and its Installed apps entry.
#>
param(
    [switch]$RemoveData,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\G923Mapper'),
    [switch]$SkipHidHide,
    [switch]$Pause     # keep the window open at the end (used when started from Windows Settings)
)

Set-Location $env:TEMP     # so this script's own folder is not locked by the working directory
$target = $InstallDir
$exe = Join-Path $target 'G923Mapper.exe'

Get-Process G923Mapper -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$target*" } | Stop-Process -Force
Start-Sleep -Milliseconds 500

# --- give the wheel back to games if HidHide is hiding it
if (-not $SkipHidHide) {
    $cli = @("$env:ProgramFiles\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe", "$env:ProgramFiles\Nefarius Software Solutions\HidHide\HidHideCLI.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($cli) {
        # The wheel's USB ids: the built-in G923 Xbox edition plus anything calibrated into devices.json (not extra devices).
        $ids = @('VID_046D&PID_C26E')
        $devicesJson = Join-Path $env:APPDATA 'G923Mapper\devices.json'
        if (Test-Path $devicesJson) {
            try {
                $devices = Get-Content $devicesJson -Raw | ConvertFrom-Json
                foreach ($d in $devices.PSObject.Properties.Value) {
                    if (-not $d.extra -and $d.vid -and $d.pid) { $ids += ('VID_{0}&PID_{1}' -f $d.vid.ToUpper(), $d.pid.ToUpper()) }
                }
            } catch { }
        }
        $hidden = & $cli --dev-list 2>$null
        $mine = @($hidden | Select-String -Pattern 'HID\\VID_[0-9A-Fa-f]{4}&PID_[0-9A-Fa-f]{4}[^"\r\n,\]\}]*' -AllMatches |
                  ForEach-Object { $_.Matches.Value } | Where-Object { $p = $_; $ids | Where-Object { $p -like "*$_*" } } | Select-Object -Unique)
        if ($mine.Count -gt 0) {
            "The wheel is hidden by HidHide; showing it to games again (Windows will ask for permission)..."
            $commands = @($mine | ForEach-Object { "`"$cli`" --dev-unhide `"$_`"" }) + "`"$cli`" --app-unreg `"$exe`""
            try { Start-Process cmd.exe -ArgumentList ('/c "' + ($commands -join ' && ') + '"') -Verb RunAs -Wait -WindowStyle Hidden }
            catch { Write-Warning "Could not change HidHide (cancelled?). Show the wheel yourself in the HidHide client if games cannot see it." }
        }
    }
}

# --- remove everything else
Remove-Item (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\G923 Mapper.lnk') -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Desktop')) 'G923 Mapper.lnk') -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'G923Mapper' -ErrorAction SilentlyContinue

# The Installed apps entry, only if it belongs to this install.
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\G923Mapper'
if ((Test-Path $key) -and ((Get-ItemProperty $key).InstallLocation -eq $target)) { Remove-Item $key -Recurse -Force }

if (Test-Path $target) { Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue }
"G923 Mapper removed."

if ($RemoveData) {
    Remove-Item (Join-Path $env:APPDATA 'G923Mapper') -Recurse -Force -ErrorAction SilentlyContinue
    "Settings and profiles removed."
} else {
    "Your profiles and settings were kept in $env:APPDATA\G923Mapper."
}
if ($Pause) { Read-Host "Press Enter to close" | Out-Null }