param([Parameter(Mandatory = $true)][string]$SourceDirectory)
$ErrorActionPreference = "Stop"
$backendRoot = (Resolve-Path (Join-Path $PSScriptRoot "../../../..")).Path
$sourceRoot = (Resolve-Path -LiteralPath $SourceDirectory).Path
$targetRoot = [IO.Path]::GetFullPath((Join-Path $backendRoot "../ArquitecturaBaseFront/src/test/fixtures/form-files-http"))
$names = @("draft-created", "reply-downloading", "reply-completed", "reply-reloaded", "reply-without-read")
foreach ($name in $names) { $null = Get-Content -LiteralPath (Join-Path $sourceRoot "$name.json") -Raw | ConvertFrom-Json }
New-Item -ItemType Directory -Force -Path $targetRoot | Out-Null
$captures = foreach ($name in $names) {
    $file = Get-Item -LiteralPath (Join-Path $sourceRoot "$name.json")
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $targetRoot $file.Name) -Force
    @{name=$file.Name; length=$file.Length; sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$testSource = "tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp/WhatsAppFormFilesIntegrationTests.cs"
$head = git -C $backendRoot rev-parse HEAD
$sourceState = if (git -C $backendRoot status --porcelain -- $testSource) { "working-tree" } else { "committed" }
@{ producer="Real_files_flow_is_atomic_private_reloadable_and_independent_of_message_retention"; provider="Meta HTTP simulated; PostgreSQL, Redis and Azurite real"; repositoryHead=$head; sourceState=$sourceState; source=$testSource; sourceSha256=(Get-FileHash -LiteralPath (Join-Path $backendRoot $testSource) -Algorithm SHA256).Hash.ToLowerInvariant(); captures=@($captures) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $targetRoot "provenance.json") -Encoding utf8NoBOM
Write-Output "Copied five HTTP responses with their producer provenance."
