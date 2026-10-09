<#
.SYNOPSIS
Levanta la base de demostración de formularios en Docker (PostgreSQL 18.3, puerto 5440).

.DESCRIPTION
Sin parámetros: crea el contenedor y carga los datos la primera vez; después solo lo arranca.
-Recargar: vuelve a cargar esquema, datos y vistas (borra lo que hayas cambiado).
-Borrar: elimina el contenedor.
No toca la base de desarrollo de Aspire.
#>
param(
    [switch]$Recargar,
    [switch]$Borrar
)

$ErrorActionPreference = 'Stop'
$nombre = 'formularios-demo'
$base = 'formularios_demo'
$archivos = '01-esquema.sql', '02-datos.sql', '03-vistas.sql', '05-recorrido-crear-formulario.sql'

$existe = docker ps -a --filter "name=^$nombre$" --format '{{.Names}}'

if ($Borrar) {
    if ($existe) { docker rm -f $nombre | Out-Null }
    Write-Host 'Base de demostración eliminada.'
    return
}

$cargar = $Recargar.IsPresent
if (-not $existe) {
    docker run -d --name $nombre `
        -e POSTGRES_USER=postgres -e POSTGRES_DB=$base -e POSTGRES_HOST_AUTH_METHOD=trust `
        -p 127.0.0.1:5440:5432 postgres:18.3 | Out-Null
    $cargar = $true
}
else {
    docker start $nombre | Out-Null
}

# El contenedor arranca un servidor temporal antes del definitivo: se espera a que responda dos veces seguidas.
$listo = 0
for ($i = 0; $i -lt 60 -and $listo -lt 2; $i++) {
    docker exec $nombre psql -U postgres -d $base -tAc 'select 1' *> $null
    if ($LASTEXITCODE -eq 0) { $listo++ } else { $listo = 0 }
    Start-Sleep -Seconds 1
}
if ($listo -lt 2) { throw 'PostgreSQL no respondió a tiempo. Revisá «docker logs formularios-demo».' }

if ($cargar) {
    foreach ($archivo in $archivos) {
        Write-Host "Cargando $archivo…"
        docker cp (Join-Path $PSScriptRoot $archivo) "${nombre}:/tmp/$archivo" | Out-Null
        docker exec -e PGOPTIONS=-cclient_min_messages=warning $nombre psql -U postgres -d $base -v ON_ERROR_STOP=1 -q -f "/tmp/$archivo" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Falló la carga de $archivo." }
    }
}

Write-Host ''
Write-Host 'Lista. Conectate desde DBeaver con:'
Write-Host '  Host: localhost   Puerto: 5440   Base: formularios_demo   Usuario: postgres   Contraseña: (vacía)'
Write-Host 'Las consultas guiadas están en 04-consultas.sql.'
