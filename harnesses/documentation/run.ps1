# Entrada manual: PowerShell inicia Node; Node controla los procesos de los motores.
[CmdletBinding()]
param(
    [ValidateSet('codex', 'claude', 'none')][string]$Engine = 'codex',
    [ValidateSet('audit', 'update')][string]$Mode = 'audit',
    [ValidateSet('structural', 'full')][string]$Checks = 'full',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$runnerPath = Join-Path $PSScriptRoot 'runner.mjs'
$runnerArgs = @($runnerPath, '--engine', $Engine, '--mode', $Mode, '--checks', $Checks)
if ($DryRun) { $runnerArgs += '--dry-run' }
& node @runnerArgs
exit $LASTEXITCODE
