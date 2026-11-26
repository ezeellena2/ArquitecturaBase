param([Parameter(Mandatory)][string]$Report)

# Reescribe api-test-durations.json con los segundos de cada clase de un informe de xUnit de la integración entera: el
# ejecutable de tests con -result-xml, o dotnet test con --report-xunit. ApiTestCollectionFactory pesa con eso cada
# colección y ApiTestCollectionOrderer larga primero las más lentas; una clase que no figura pesa sus casos al promedio.
#
#   pwsh -NoProfile -File tests/ArquitecturaBase.Api.IntegrationTests/Support/update-api-test-durations.ps1 -Report informe.xml
$ErrorActionPreference = 'Stop'
$prefix = 'ArquitecturaBase.Api.IntegrationTests.'
[xml]$xml = Get-Content -LiteralPath $Report -Raw
$seconds = @{}
foreach ($test in $xml.SelectNodes('//test')) {
    $type = [string]$test.type
    if (-not $type.StartsWith($prefix, [StringComparison]::Ordinal)) { continue }
    $name = $type.Substring($prefix.Length)
    # Un cast de PowerShell lee el número sin la cultura de la máquina: «0.01», no «0,01».
    $seconds[$name] = [double]$seconds[$name] + [double]$test.time
}
if ($seconds.Count -eq 0) { throw "El informe no tiene tests de $prefix" }

[string[]]$names = $seconds.Keys
[Array]::Sort($names, [StringComparer]::Ordinal)
$ordered = [ordered]@{}
foreach ($name in $names) { $ordered[$name] = [int][Math]::Round($seconds[$name]) }
$target = Join-Path $PSScriptRoot 'api-test-durations.json'
$ordered | ConvertTo-Json | Set-Content -LiteralPath $target -Encoding utf8NoBOM
"$($names.Count) clases en $target"
