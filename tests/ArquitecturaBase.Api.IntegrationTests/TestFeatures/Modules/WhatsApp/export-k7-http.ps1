param([string]$Artifacts = ".test-artifacts/k7-final")
$ErrorActionPreference = "Stop"
$backendRoot = (Resolve-Path (Join-Path $PSScriptRoot "../../../../..")).Path
$frontRoot = (Resolve-Path (Join-Path $backendRoot "../ArquitecturaBaseFront")).Path
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $backendRoot $Artifacts))
$exportRoot = Join-Path $artifactRoot "http"
New-Item -ItemType Directory -Force -Path $exportRoot | Out-Null
$previousOutput = $env:WHATSAPP_CATALOG_HTTP_OUTPUT
$previousFormsOutput = $env:FORMS_HTTP_CONTRACT_OUTPUT
try {
    $env:WHATSAPP_CATALOG_HTTP_OUTPUT = $exportRoot
    $env:FORMS_HTTP_CONTRACT_OUTPUT = Join-Path $artifactRoot "forms-http"
    Push-Location $backendRoot
    try {
        $repositoryHead = (git rev-parse HEAD).Trim()
        $sourceState = @(git status --short)
        dotnet test --no-restore *> (Join-Path $artifactRoot "backend-tests.log")
        $testExit = $LASTEXITCODE
        if ($testExit -ne 0) { throw "Backend tests failed. Inspect the local backend-tests.log before exporting." }
        $targetRoot = Join-Path $frontRoot "src/test/fixtures/whatsapp-catalogs-http"
        $captures = @()
        foreach ($culture in @("es", "en")) {
            $sourceDirectory = Join-Path $exportRoot $culture
            $targetDirectory = Join-Path $targetRoot $culture
            New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
            foreach ($file in Get-ChildItem -LiteralPath $sourceDirectory -Filter "*.json" -File) {
                Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $targetDirectory $file.Name) -Force
                $captures += @{ name = "$culture/$($file.Name)"; length = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
            }
        }
        $provenance = @{ repositoryHead = $repositoryHead; sourceState = $sourceState; command = "dotnet test --no-restore";
            producer = "Real_http_reply_cards_preserve_exact_values_and_hide_them_without_read_permission (catalog=true)";
            transport = "Actual HTTP; PostgreSQL/Redis Testcontainers; captured Meta and neutral record fixture"; captures = $captures }
        $provenance | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $targetRoot "provenance.json") -Encoding utf8
        $filtersSource = Join-Path $env:FORMS_HTTP_CONTRACT_OUTPUT "catalog-filters"
        $filtersTarget = Join-Path $frontRoot "src/test/fixtures/catalogs-http/filters"
        $filterCaptures = @()
        foreach ($file in Get-ChildItem -LiteralPath $filtersSource -Filter "*.json" -File) {
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $filtersTarget $file.Name) -Force
            $filterCaptures += @{ path = $file.Name; length = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
        }
        $filterSource = "tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormCatalogFiltersEndpointsTests.cs"
        $filterProvenance = @{ schemaVersion = 1; artifactSet = "filters"; generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o");
            producer = @{ project = "ArquitecturaBase.Api.IntegrationTests";
                test = "ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms.FormCatalogFiltersEndpointsTests";
                source = $filterSource; sourceSha256 = (Get-FileHash -LiteralPath (Join-Path $backendRoot $filterSource) -Algorithm SHA256).Hash.ToLowerInvariant();
                repositoryHead = $repositoryHead; sourceState = $sourceState };
            runner = @{ command = "dotnet test --no-restore"; environment = @{ FORMS_HTTP_CONTRACT_OUTPUT = $env:FORMS_HTTP_CONTRACT_OUTPUT };
                exitCode = $testExit; exportFiles = $filterCaptures } }
        $filterProvenance | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $filtersTarget "provenance.json") -Encoding utf8
        Write-Output "Backend tests passed. WhatsApp and Filters HTTP responses copied to the frontend fixtures."
    } finally { Pop-Location }
} finally {
    $env:WHATSAPP_CATALOG_HTTP_OUTPUT = $previousOutput
    $env:FORMS_HTTP_CONTRACT_OUTPUT = $previousFormsOutput
}
