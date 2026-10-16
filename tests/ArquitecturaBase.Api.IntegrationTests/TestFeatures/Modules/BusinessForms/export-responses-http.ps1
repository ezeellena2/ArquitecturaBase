param([string]$Artifacts = ".artifacts/assistant-validation/stage3-final")
$ErrorActionPreference = "Stop"
$backendRoot = (Resolve-Path (Join-Path $PSScriptRoot "../../../../..")).Path
$frontRoot = (Resolve-Path (Join-Path $backendRoot "../ArquitecturaBaseFront")).Path
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $backendRoot $Artifacts))
$exportRoot = Join-Path $artifactRoot "http"
New-Item -ItemType Directory -Force -Path $exportRoot | Out-Null
$previousOutput = $env:FORM_RESPONSES_HTTP_OUTPUT
try {
    $env:FORM_RESPONSES_HTTP_OUTPUT = $exportRoot
    Push-Location $backendRoot
    try {
        $repositoryHead = (git rev-parse HEAD).Trim()
        $sourceState = @(git status --short)
        dotnet build ArquitecturaBase.slnx --no-restore *> (Join-Path $artifactRoot "backend-build.log")
        if ($LASTEXITCODE -ne 0) { throw "Build failed. Inspect backend-build.log." }
        dotnet test --no-build --no-restore *> (Join-Path $artifactRoot "backend-tests.log")
        if ($LASTEXITCODE -ne 0) { throw "Tests failed. Inspect backend-tests.log." }
        $targetRoot = Join-Path $frontRoot "src/test/fixtures/forms-responses-http"
        New-Item -ItemType Directory -Force -Path $targetRoot | Out-Null
        $captures = @()
        foreach ($file in Get-ChildItem -LiteralPath $exportRoot -File) {
            if ($file.Name -eq "explain.json") { continue }
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $targetRoot $file.Name) -Force
            $captures += @{ name = $file.Name; length = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
        }
        $provenance = @{ repositoryHead = $repositoryHead; sourceState = $sourceState;
            command = "dotnet build ArquitecturaBase.slnx --no-restore; dotnet test --no-build --no-restore";
            producer = "FormResponseHttpTests and WhatsAppFormFilesIntegrationTests";
            transport = "Actual HTTP; PostgreSQL/Redis Testcontainers; synthetic records and private files"; captures = $captures }
        $json = ($provenance | ConvertTo-Json -Depth 6).Replace("`r`n", "`n") + "`n"
        [IO.File]::WriteAllText((Join-Path $targetRoot "provenance.json"), $json, [Text.UTF8Encoding]::new($false))
        Write-Output "Backend build and complete tests passed. Raw response fixtures exported."
    } finally { Pop-Location }
} finally { $env:FORM_RESPONSES_HTTP_OUTPUT = $previousOutput }
