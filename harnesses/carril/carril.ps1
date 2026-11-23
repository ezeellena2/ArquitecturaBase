# El carril: corre un comando pesado de a uno por vez en toda la PC. Con varias sesiones a la vez, las compilaciones
# completas y las suites enteras se turnan acá en lugar de competir por el procesador (README.md).
#
#   pwsh -NoProfile -File harnesses/carril/carril.ps1 -- dotnet test --project tests/X/X.csproj
#   pwsh -NoProfile -File harnesses/carril/carril.ps1 -Status
#
# El turno es un mutex con nombre de Windows: si el dueño muere sin soltarlo, el sistema lo libera y entra el siguiente.
# El comando no recibe entrada (una pregunta falla en lugar de esperar para siempre) y se corta si queda quieto, sin
# usar el procesador, o si pasa el tiempo máximo: un comando trabado no puede retener el carril (2026-11-23).
# Sin bloque param a propósito: con -File, PowerShell no respeta `--` en un script con parámetros, y todo lo que va
# después de `--` tiene que llegar tal cual al comando (también otro `--`, como el de `dotnet test`).

$ErrorActionPreference = 'Stop'
$status = $false
$maxWaitMinutes = 240
$stallMinutes = 30
$maxRunMinutes = 240
$command = @()
for ($i = 0; $i -lt $args.Count; $i++) {
    $arg = [string]$args[$i]
    $hasValue = $i + 1 -lt $args.Count
    if ($arg -eq '--') { $command = @($args | Select-Object -Skip ($i + 1)); break }
    elseif ($arg -eq '-Status') { $status = $true }
    elseif ($arg -eq '-MaxWaitMinutes' -and $hasValue -and [int]::TryParse([string]$args[$i + 1], [ref]$maxWaitMinutes)) { $i++ }
    elseif ($arg -eq '-StallMinutes' -and $hasValue -and [int]::TryParse([string]$args[$i + 1], [ref]$stallMinutes)) { $i++ }
    elseif ($arg -eq '-MaxRunMinutes' -and $hasValue -and [int]::TryParse([string]$args[$i + 1], [ref]$maxRunMinutes)) { $i++ }
    else { $command = @($args | Select-Object -Skip $i); break }
}

# Los tests acortan los plazos con segundos; en uso real valen los minutos.
$stall = if ($env:AB_CARRIL_STALL_SECONDS) { [TimeSpan]::FromSeconds([int]$env:AB_CARRIL_STALL_SECONDS) } else { [TimeSpan]::FromMinutes($stallMinutes) }
$maxRun = if ($env:AB_CARRIL_MAX_RUN_SECONDS) { [TimeSpan]::FromSeconds([int]$env:AB_CARRIL_MAX_RUN_SECONDS) } else { [TimeSpan]::FromMinutes($maxRunMinutes) }
$checkEvery = if ($env:AB_CARRIL_CHECK_SECONDS) { [TimeSpan]::FromSeconds([int]$env:AB_CARRIL_CHECK_SECONDS) } else { [TimeSpan]::FromMinutes(1) }

$name = if ($env:AB_CARRIL_NAME) { $env:AB_CARRIL_NAME } else { 'ArquitecturaBase.Carril' }
$stateDir = if ($env:AB_CARRIL_STATE) { $env:AB_CARRIL_STATE } else { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\.artifacts\carril')) }
$stateFile = Join-Path $stateDir "$name.json"
$program = if ($command.Count -gt 0) { [string]$command[0] } else { $null }
$rest = @($command | Select-Object -Skip 1 | ForEach-Object { [string]$_ })

function Get-Holder {
    if (-not (Test-Path $stateFile)) { return 'unknown holder (no state file)' }
    try {
        $state = Get-Content $stateFile -Raw | ConvertFrom-Json
        "pid $($state.pid) since $($state.since) in $($state.directory): $($state.command)"
    }
    catch { 'unknown holder (unreadable state file)' }
}

# El procesador que usó el comando con todos sus hijos, en segundos.
function Get-TreeCpu([int] $rootId) {
    $all = @(Get-CimInstance Win32_Process -Property ProcessId, ParentProcessId)
    $ids = [System.Collections.Generic.HashSet[int]]::new()
    [void]$ids.Add($rootId)
    do {
        $added = $false
        foreach ($p in $all) { if ($ids.Contains([int]$p.ParentProcessId) -and $ids.Add([int]$p.ProcessId)) { $added = $true } }
    } while ($added)
    $seconds = 0.0
    foreach ($id in $ids) {
        $process = Get-Process -Id $id -ErrorAction SilentlyContinue
        if ($process) { try { $seconds += $process.TotalProcessorTime.TotalSeconds } catch { } }
    }
    $seconds
}

# Arranca el comando sin entrada, con la salida directo a la consola, y lo vigila hasta que termina o hay que cortarlo.
function Invoke-Watched {
    $resolved = Get-Command $program -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    $info = [System.Diagnostics.ProcessStartInfo]::new($(if ($resolved) { $resolved.Source } else { $program }))
    foreach ($a in $rest) { $info.ArgumentList.Add($a) }
    $info.UseShellExecute = $false
    $info.RedirectStandardInput = $true
    $info.WorkingDirectory = (Get-Location).Path
    try { $process = [System.Diagnostics.Process]::Start($info) }
    catch {
        Write-Host "carril: could not start '$program': $($_.Exception.Message)"
        return 127
    }
    $process.StandardInput.Close()

    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $lastCpu = 0.0
    $lastActive = [TimeSpan]::Zero
    $reason = $null
    while (-not $process.WaitForExit([int]$checkEvery.TotalMilliseconds)) {
        $cpu = Get-TreeCpu $process.Id
        if ($cpu - $lastCpu -ge 0.25 -or $cpu -lt $lastCpu) { $lastActive = $clock.Elapsed }
        $lastCpu = $cpu
        if ($clock.Elapsed - $lastActive -ge $stall) { $reason = "stalled, without using the processor for $([int]$stall.TotalSeconds) s"; break }
        if ($clock.Elapsed -ge $maxRun) { $reason = "ran longer than $([int]$maxRun.TotalSeconds) s"; break }
    }
    if ($reason) {
        Write-Host "carril: cut the command, it $reason."
        try { $process.Kill($true) } catch { }
        $process.WaitForExit()
        return 125
    }
    $process.WaitForExit()
    $process.ExitCode
}

if (-not $status -and $command.Count -eq 0) {
    Write-Host 'usage: pwsh -NoProfile -File harnesses/carril/carril.ps1 [-MaxWaitMinutes N] [-StallMinutes N] [-MaxRunMinutes N] -- <command> [args...]'
    Write-Host '       pwsh -NoProfile -File harnesses/carril/carril.ps1 -Status'
    exit 2
}

# Un comando que ya corre dentro del carril y lo vuelve a llamar no se espera a sí mismo.
if (-not $status -and $env:AB_CARRIL_OWNER -like "$name|*") {
    exit (Invoke-Watched)
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
                $code = Invoke-Watched
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
