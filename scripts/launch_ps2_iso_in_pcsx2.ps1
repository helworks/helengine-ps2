param(
    [Parameter(Mandatory = $true)]
    [string]$IsoPath
)

$ErrorActionPreference = 'Stop'

$resolvedIsoPath = [System.IO.Path]::GetFullPath($IsoPath)
if (-not (Test-Path -LiteralPath $resolvedIsoPath)) {
    throw "ISO was not found: $resolvedIsoPath"
}

$pcsx2Path = 'C:\Program Files\PCSX2\pcsx2-qt.exe'
$globalProfileRoot = Join-Path $env:USERPROFILE 'Documents\PCSX2'
$launcherRoot = Join-Path $PSScriptRoot '..\tmp\pcsx2-launcher'
$resolvedLauncherRoot = [System.IO.Path]::GetFullPath($launcherRoot)
$logFilePath = Join-Path $resolvedLauncherRoot 'pcsx2-emulog.txt'

if (-not (Test-Path -LiteralPath $pcsx2Path)) {
    throw "PCSX2 executable was not found: $pcsx2Path"
}

if (-not (Test-Path -LiteralPath $globalProfileRoot)) {
    throw "PCSX2 profile root was not found: $globalProfileRoot"
}

$existingPcsx2Processes = @(Get-Process -Name 'pcsx2-qt' -ErrorAction SilentlyContinue)
foreach ($process in $existingPcsx2Processes) {
    Stop-Process -Id $process.Id -Force
}

if (Test-Path -LiteralPath $resolvedLauncherRoot) {
    Remove-Item -LiteralPath $resolvedLauncherRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $resolvedLauncherRoot | Out-Null

$isoItem = Get-Item -LiteralPath $resolvedIsoPath

Write-Output ("ISO=" + $resolvedIsoPath)
Write-Output ("ISO_LAST_WRITE_TIME=" + $isoItem.LastWriteTime.ToString('O'))
Write-Output ("PCSX2=" + $pcsx2Path)
Write-Output ("PROFILE_ROOT=" + $globalProfileRoot)
Write-Output ("LOGFILE=" + $logFilePath)

$process = Start-Process -FilePath $pcsx2Path -ArgumentList '-fastboot', '-logfile', $logFilePath, '--', $resolvedIsoPath -WorkingDirectory (Split-Path -Path $pcsx2Path -Parent) -PassThru
Write-Output ("PROCESS_ID=" + $process.Id)
