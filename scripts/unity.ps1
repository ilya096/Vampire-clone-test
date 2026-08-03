[CmdletBinding()]
param(
    [ValidateSet('setup-player-combat', 'open')]
    [string]$Task = 'setup-player-combat'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$defaultEditor = 'C:\_Unity\6000.5.6f1\Editor\Unity.exe'
$unityEditor = if ($env:UNITY_EDITOR_PATH) { $env:UNITY_EDITOR_PATH } else { $defaultEditor }

if (-not (Test-Path -LiteralPath $unityEditor)) {
    throw "Unity Editor was not found: $unityEditor. Set UNITY_EDITOR_PATH to Unity.exe."
}

if ($Task -eq 'open') {
    Start-Process -FilePath $unityEditor -ArgumentList @('-projectPath', $projectRoot)
    return
}

$logPath = Join-Path $projectRoot 'Logs\player-combat-unity-batch.log'
$process = Start-Process -FilePath $unityEditor -ArgumentList @(
    '-batchmode',
    '-quit',
    '-projectPath', $projectRoot,
    '-executeMethod', 'PlayerCombatSceneSetup.SetupPlayerCombatInGame',
    '-logFile', $logPath
) -Wait -PassThru
$exitCode = $process.ExitCode

if ($exitCode -ne 0) {
    throw "Unity setup failed with exit code $exitCode. See $logPath."
}

Write-Host "Unity player combat setup completed. Log: $logPath"
