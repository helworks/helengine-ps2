param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactPath
)

$ErrorActionPreference = 'Stop'

$resolvedArtifactPath = [System.IO.Path]::GetFullPath($ArtifactPath)
if (-not (Test-Path -LiteralPath $resolvedArtifactPath -PathType Leaf)) {
    throw "Artifact was not found: $resolvedArtifactPath"
}

if ([System.IO.Path]::GetExtension($resolvedArtifactPath) -ine '.iso') {
    throw "Expected a .iso artifact but got '$resolvedArtifactPath'."
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

$pcsx2ProcessNames = @('pcsx2-qt', 'pcsx2')
foreach ($pcsx2ProcessName in $pcsx2ProcessNames) {
    $existingPcsx2Processes = @(Get-Process -Name $pcsx2ProcessName -ErrorAction SilentlyContinue)
    foreach ($process in $existingPcsx2Processes) {
        Stop-Process -Id $process.Id -Force
    }
}

if (Test-Path -LiteralPath $resolvedLauncherRoot) {
    Remove-Item -LiteralPath $resolvedLauncherRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $resolvedLauncherRoot | Out-Null

$artifactItem = Get-Item -LiteralPath $resolvedArtifactPath

Write-Output ("ARTIFACT=" + $resolvedArtifactPath)
Write-Output ("ARTIFACT_LAST_WRITE_TIME=" + $artifactItem.LastWriteTime.ToString('O'))
Write-Output ("PCSX2=" + $pcsx2Path)
Write-Output ("PROFILE_ROOT=" + $globalProfileRoot)
Write-Output ("LOGFILE=" + $logFilePath)

$process = Start-Process -FilePath $pcsx2Path -ArgumentList '-fastboot', '-logfile', $logFilePath, '--', $resolvedArtifactPath -WorkingDirectory (Split-Path -Path $pcsx2Path -Parent) -PassThru
Write-Output ("PROCESS_ID=" + $process.Id)
