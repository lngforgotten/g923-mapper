<#
.SYNOPSIS
  Builds a self-contained single-file G923Mapper.exe (no .NET install needed) plus the installer scripts into .\dist.

.EXAMPLE
  .\build\publish.ps1
#>
param([string]$Output = (Join-Path $PSScriptRoot '..\dist'))

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$Output = [IO.Path]::GetFullPath($Output)

if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }

dotnet publish "$root\src\G923Mapper.App\G923Mapper.App.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:DebugSymbols=false `
    -o $Output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

Copy-Item "$PSScriptRoot\Install.ps1", "$PSScriptRoot\Uninstall.ps1", "$PSScriptRoot\Setup.cmd", "$PSScriptRoot\Uninstall.cmd", "$PSScriptRoot\READ-ME-FIRST.txt" $Output
$exe = Join-Path $Output 'G923Mapper.exe'
"{0}: {1:N1} MB" -f $exe, ((Get-Item $exe).Length / 1MB)
"Double-click Setup.cmd in that folder to install it for the current user (or run Install.ps1)."

# One file to send to someone else.
$zip = Join-Path $root 'G923Mapper-win-x64.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $Output '*') -DestinationPath $zip -CompressionLevel Optimal
"{0}: {1:N1} MB  <- send this file" -f $zip, ((Get-Item $zip).Length / 1MB)

# A much smaller download for people who already have (or will install) the .NET 10 Desktop Runtime. Same app, same installer.
$small = [IO.Path]::GetFullPath((Join-Path $root 'dist-small'))
if (Test-Path $small) { Remove-Item $small -Recurse -Force }
dotnet publish "$root\src\G923Mapper.App\G923Mapper.App.csproj" `
    -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false `
    -o $small
if ($LASTEXITCODE -ne 0) { throw "small dotnet publish failed ($LASTEXITCODE)" }
Copy-Item "$PSScriptRoot\Install.ps1", "$PSScriptRoot\Uninstall.ps1", "$PSScriptRoot\Setup.cmd", "$PSScriptRoot\Uninstall.cmd", "$PSScriptRoot\READ-ME-FIRST.txt" $small
Set-Content -Path (Join-Path $small 'NEEDS-DOTNET.txt') -Encoding UTF8 -Value @"
This is the small build of G923 Mapper. It does not carry .NET with it, so the PC needs the
".NET 10 Desktop Runtime" (x64). If it is missing, Windows offers to download it the first time you start the app,
or get it from https://dotnet.microsoft.com/download/dotnet/10.0 (choose "Desktop Runtime", x64).
If you would rather not install anything, use the larger G923Mapper-win-x64.zip, which includes everything.
"@
$smallZip = Join-Path $root 'G923Mapper-small-win-x64.zip'
if (Test-Path $smallZip) { Remove-Item $smallZip -Force }
Compress-Archive -Path (Join-Path $small '*') -DestinationPath $smallZip -CompressionLevel Optimal
"{0}: {1:N1} MB  <- the small one (needs the .NET 10 Desktop Runtime)" -f $smallZip, ((Get-Item $smallZip).Length / 1MB)
