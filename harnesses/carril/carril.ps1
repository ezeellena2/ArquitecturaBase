# El carril: corre un comando pesado de a uno por vez en toda la PC. Con varias sesiones a la vez, las compilaciones
# completas y las suites enteras se turnan acá en lugar de competir por el procesador (README.md).
#
#   pwsh -NoProfile -File harnesses/carril/carril.ps1 -- dotnet test --project tests/X/X.csproj
#   pwsh -NoProfile -File harnesses/carril/carril.ps1 -Status
#
# El turno es un mutex con nombre de Windows: si el dueño muere sin soltarlo, el sistema lo libera y entra el siguiente.
# Sin bloque param a propósito: con -File, PowerShell no respeta `--` en un script con parámetros, y todo lo que va
# después de `--` tiene que llegar tal cual al comando (también otro `--`, como el de `dotnet test`).

$ErrorActionPreference = 'Stop'
$status = $false
$maxWaitMinutes = 240
$command = @()
for ($i = 0; $i -lt $args.Count; $i++) {
    $arg = [string]$args[$i]
    if ($arg -eq '--') { $command = @($args | Select-Object -Skip ($i + 1)); break }
    elseif ($arg -eq '-Status') { $status = $true }
    elseif ($arg -eq '-MaxWaitMinutes' -and $i + 1 -lt $args.Count -and [int]::TryParse([string]$args[$i + 1], [ref]$maxWaitMinutes)) { $i++ }
    else { $command = @($args | Select-Object -Skip $i); break }
}

$name = if ($env:AB_CARRIL_NAME) { $env:AB_CARRIL_NAME } else { 'ArquitecturaBase.Carril' }
$stateDir = if ($env:AB_CARRIL_STATE) { $env:AB_CARRIL_STATE } else { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\.artifacts\carril')) }
$stateFile = Join-Path $stateDir "$name.json"

function Get-Holder {
    if (-not (Test-Path $stateFile)) { return 'unknown holder (no state file)' }
    try {
        $state = Get-Content $stateFile -Raw | ConvertFrom-Json
        "pid $($state.pid) since $($state.since) in $($state.directory): $($state.command)"
    }
    catch { 'unknown holder (unreadable state file)' }
}

# El comando corre en el ámbito del script y no en una función, para que su salida vaya directo a la consola.
$program = if ($command.Count -gt 0) { $command[0] } else { $null }
$rest = @($command | Select-Object -Skip 1)

if (-not $status -and $command.Count -eq 0) {
    Write-Host 'usage: pwsh -NoProfile -File harnesses/carril/carril.ps1 [-MaxWaitMinutes N] -- <command> [args...]'
    Write-Host '       pwsh -NoProfile -File harnesses/carril/carril.ps1 -Status'
    exit 2
}

# Un comando que ya corre dentro del carril y lo vuelve a llamar no se espera a sí mismo.
if (-not $status -and $env:AB_CARRIL_OWNER -like "$name|*") {
    $global:LASTEXITCODE = 0
    & $program @rest
    exit $LASTEXITCODE
}

$mutex = [System.Threading.Mutex]::new($false, $name)
$code = 0
try {
    $owned = $false
    try { $owned = $mutex.WaitOne(0) }
    catch [System.Threading.AbandonedMutexException] {
        $owned = $true
        Write-Host 'carril: the previous holder ended without releasing it (abandoned); taking it.'
    }

    if ($status) {
        if ($owned) { $mutex.ReleaseMutex(); Write-Host 'carril: free' }
        else { Write-Host "carril: busy, $(Get-Holder)" }
    }
    else {
        $waited = [System.Diagnostics.Stopwatch]::StartNew()
        if (-not $owned) { Write-Host "carril: waiting, busy with $(Get-Holder)" }
        $nextNotice = [TimeSpan]::FromMinutes(5)
        while (-not $owned) {
            try { $owned = $mutex.WaitOne([TimeSpan]::FromSeconds(15)) }
            catch [System.Threading.AbandonedMutexException] {
                $owned = $true
                Write-Host 'carril: the previous holder ended without releasing it (abandoned); taking it.'
            }
            if ($owned) { break }
            if ($waited.Elapsed -ge $nextNotice) {
                Write-Host "carril: still waiting ($([int]$waited.Elapsed.TotalMinutes) min), busy with $(Get-Holder)"
                $nextNotice += [TimeSpan]::FromMinutes(5)
            }
            if ($waited.Elapsed.TotalMinutes -ge $maxWaitMinutes) {
                Write-Host "carril: gave up after waiting $maxWaitMinutes min."
                $code = 124
                break
            }
        }

        if ($owned) {
            try {
                if ($waited.Elapsed.TotalSeconds -ge 1) { Write-Host "carril: taken after waiting $([int]$waited.Elapsed.TotalSeconds) s." }
                New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
                [ordered]@{ pid = $PID; since = (Get-Date).ToString('dd/MM/yyyy HH:mm:ss'); directory = (Get-Location).Path; command = ($command -join ' ') } |
                    ConvertTo-Json | Set-Content -Path $stateFile -Encoding utf8
                $env:AB_CARRIL_OWNER = "$name|$PID"
                $global:LASTEXITCODE = 0
                & $program @rest
                $code = $LASTEXITCODE
            }
            finally {
                $env:AB_CARRIL_OWNER = $null
                Remove-Item -Path $stateFile -ErrorAction SilentlyContinue
                $mutex.ReleaseMutex()
            }
        }
    }
}
finally {
    $mutex.Dispose()
}
exit $code
