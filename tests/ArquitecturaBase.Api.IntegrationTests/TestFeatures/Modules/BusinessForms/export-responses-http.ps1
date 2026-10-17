param([string]$Artifacts = ".artifacts/assistant-validation/stage4-final")
$ErrorActionPreference = "Stop"
$backendRoot = (Resolve-Path (Join-Path $PSScriptRoot "../../../../..")).Path
$frontRoot = (Resolve-Path (Join-Path $backendRoot "../ArquitecturaBaseFront")).Path
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $frontRoot "src/test/fixtures"))
$fixturePrefix = $fixtureRoot.TrimEnd([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)) + [IO.Path]::DirectorySeparatorChar
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $backendRoot $Artifacts))
$runId = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss") + "-" + [Guid]::NewGuid().ToString("N")
$runRoot = Join-Path $artifactRoot $runId
$sourceRoot = "tests/ArquitecturaBase.Api.IntegrationTests/Modules/"
$captureRoot = "tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/"
$cohorts = @(
    @{ Name = "responses"; Environment = "FORM_RESPONSES_HTTP_OUTPUT"; Destination = "forms-responses-http";
        Sources = @(($sourceRoot + "BusinessForms/FormResponseHttpTests.cs"), ($sourceRoot + "BusinessForms/FormResponseApprovalHttpTests.cs"), ($sourceRoot + "WhatsApp/WhatsAppFormFilesIntegrationTests.cs"), ($captureRoot + "ResponseContractCapture.cs")) },
    @{ Name = "rules"; Environment = "FORM_RULES_HTTP_OUTPUT"; Destination = "forms-rules-http";
        Sources = @(($sourceRoot + "BusinessForms/FormRuleHttpTests.cs"), ($captureRoot + "Modules/BusinessForms/FormRuleContractCapture.cs")) },
    @{ Name = "workflow"; Environment = "FORM_WORKFLOW_HTTP_OUTPUT"; Destination = "forms-workflow-http";
        Sources = @(($sourceRoot + "BusinessForms/FormWorkflowHttpTests.cs"), ($sourceRoot + "BusinessForms/FormPdfHttpTests.cs"), ($sourceRoot + "BusinessForms/FormWorkflowEndToEndHttpTests.cs"), ($sourceRoot + "BusinessForms/FormResponseHttpTests.cs"), ($sourceRoot + "BusinessForms/FormRecordCaptureIntegrationTests.cs"), ($sourceRoot + "BusinessForms/FormActionRecoveryIntegrationTests.cs"), ($sourceRoot + "BusinessForms/FormDraftDurabilityTests.cs"), ($captureRoot + "Modules/BusinessForms/FormPdfRendererFixture.cs"), ($captureRoot + "Modules/BusinessForms/CapturingFormActionChannel.cs"), ($captureRoot + "Modules/BusinessForms/FormWorkflowContractCapture.cs")) },
    @{ Name = "approvals"; Environment = "FORM_APPROVALS_HTTP_OUTPUT"; Destination = "forms-approvals-http";
        Sources = @(($sourceRoot + "BusinessForms/FormApprovalHttpTests.cs"), ($captureRoot + "Modules/BusinessForms/FormApprovalContractCapture.cs")) },
    @{ Name = "pdf"; Environment = "FORM_PDF_HTTP_OUTPUT"; Destination = "forms-pdf-http";
        Sources = @(($sourceRoot + "BusinessForms/FormPdfHttpTests.cs"), ($sourceRoot + "BusinessForms/FormWorkflowEndToEndHttpTests.cs"), ($sourceRoot + "BusinessForms/FormResponseHttpTests.cs"), ($sourceRoot + "BusinessForms/FormRecordCaptureIntegrationTests.cs"), ($sourceRoot + "BusinessForms/FormActionRecoveryIntegrationTests.cs"), ($sourceRoot + "BusinessForms/FormDraftDurabilityTests.cs"), ($captureRoot + "Modules/BusinessForms/FormPdfRendererFixture.cs"), ($captureRoot + "Modules/BusinessForms/CapturingFormActionChannel.cs"), ($captureRoot + "Modules/BusinessForms/FormPdfContractCapture.cs")) },
    @{ Name = "whatsapp-actions"; Environment = "WHATSAPP_FORM_ACTIONS_HTTP_OUTPUT"; Destination = "whatsapp-form-actions-http";
        Sources = @(($sourceRoot + "WhatsApp/WhatsAppFormActionHttpTests.cs"), ($captureRoot + "Modules/WhatsApp/WhatsAppFormActionContractCapture.cs"), ($captureRoot + "Modules/WhatsApp/WhatsAppFormActionScenario.cs")) },
    @{ Name = "whatsapp-forms"; Environment = "WHATSAPP_FORMS_HTTP_CONTRACT_OUTPUT"; Destination = "whatsapp-forms-http";
        Sources = @(($sourceRoot + "WhatsApp/WhatsAppFormHttpContractTests.cs"), ($sourceRoot + "WhatsApp/WhatsAppFormSendingTests.cs"),
            ($captureRoot + "Modules/WhatsApp/CapturingFlowProvider.cs"), ($captureRoot + "Modules/WhatsApp/CapturingStructuredFormSource.cs"),
            ($captureRoot + "Modules/WhatsApp/ControlledWhatsAppSendClient.cs")) }
)
$previousOutputs = @{}
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
try {
    foreach ($cohort in $cohorts) {
        $previousOutputs[$cohort.Environment] = [Environment]::GetEnvironmentVariable($cohort.Environment)
        $cohort.Output = Join-Path (Join-Path $runRoot "http") $cohort.Name
        New-Item -ItemType Directory -Path $cohort.Output -Force | Out-Null
        [Environment]::SetEnvironmentVariable($cohort.Environment, $cohort.Output)
    }
    Push-Location $backendRoot
    try {
        $repositoryHead = (git rev-parse HEAD).Trim()
        $sourceState = @(git status --short)
        foreach ($cohort in $cohorts) {
            $cohort.SourceHashes = @($cohort.Sources | ForEach-Object {
                @{ path = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $backendRoot $_) -Algorithm SHA256).Hash.ToLowerInvariant() }
            })
        }
        dotnet build ArquitecturaBase.slnx --no-restore *> (Join-Path $runRoot "backend-build.log")
        if ($LASTEXITCODE -ne 0) { throw "Build failed. Inspect backend-build.log in $runRoot." }
        dotnet test --no-build --no-restore *> (Join-Path $runRoot "backend-tests.log")
        if ($LASTEXITCODE -ne 0) { throw "Tests failed. Inspect backend-tests.log in $runRoot." }

        # Validate every cohort before replacing any frontend fixture.
        foreach ($cohort in $cohorts) {
            $cohort.Files = @(Get-ChildItem -LiteralPath $cohort.Output -File -Recurse | Where-Object { $_.Name -ne "explain.json" } | Sort-Object FullName)
            if ($cohort.Files.Count -eq 0) { throw "No HTTP captures were produced for $($cohort.Name)." }
            if ($cohort.Name -eq "whatsapp-forms") {
                foreach ($culture in @("es", "en")) {
                    foreach ($name in @("send-pending", "outgoing-pending", "outgoing-accepted", "outgoing-uncertain", "outgoing-failed")) {
                        if (-not (Test-Path -LiteralPath (Join-Path $cohort.Output "$culture/$name.json") -PathType Leaf)) {
                            throw "Missing manual form HTTP capture: $culture/$name.json."
                        }
                    }
                }
            }
            foreach ($source in $cohort.SourceHashes) {
                if ((Get-FileHash -LiteralPath (Join-Path $backendRoot $source.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $source.sha256) {
                    throw "Producer source changed during the test run: $($source.path)."
                }
            }
        }
        $generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        foreach ($cohort in $cohorts) {
            if ([IO.Path]::IsPathRooted($cohort.Destination) -or $cohort.Destination -notmatch "^[a-z0-9-]+$") {
                throw "Invalid fixture destination for cohort $($cohort.Name)."
            }
            $targetRoot = [IO.Path]::GetFullPath((Join-Path $fixtureRoot $cohort.Destination))
            if (-not $targetRoot.StartsWith($fixturePrefix, [StringComparison]::OrdinalIgnoreCase) -or
                [IO.Path]::GetRelativePath($fixtureRoot, $targetRoot) -cne $cohort.Destination) {
                throw "Fixture destination is outside its cohort: $($cohort.Name)."
            }
            $targetPrefix = $targetRoot.TrimEnd([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)) + [IO.Path]::DirectorySeparatorChar
            New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
            $captures = @()
            foreach ($file in $cohort.Files) {
                $sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                $relativePath = [IO.Path]::GetRelativePath($cohort.Output, $file.FullName).Replace([IO.Path]::DirectorySeparatorChar, [char]'/')
                $target = [IO.Path]::GetFullPath((Join-Path $targetRoot $relativePath))
                if (-not $target.StartsWith($targetPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Capture destination is outside its cohort: $($cohort.Name)."
                }
                New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
                Copy-Item -LiteralPath $file.FullName -Destination $target -Force
                if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sha256) {
                    throw "Copied HTTP bytes do not match their source: $($cohort.Name)/$($file.Name)."
                }
                $captures += @{ name = $relativePath; length = $file.Length; sha256 = $sha256 }
            }
            $names = @($captures | ForEach-Object { $_.name }) + "provenance.json"
            foreach ($stale in Get-ChildItem -LiteralPath $targetRoot -File -Recurse) {
                $stalePath = [IO.Path]::GetFullPath($stale.FullName)
                if (-not $stalePath.StartsWith($targetPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Stale fixture is outside its cohort: $($cohort.Name)."
                }
                $relativePath = [IO.Path]::GetRelativePath($targetRoot, $stalePath).Replace([IO.Path]::DirectorySeparatorChar, [char]'/')
                if ($relativePath -notin $names) { Remove-Item -LiteralPath $stalePath }
            }
            $provenance = @{ schemaVersion = 1; artifactSet = $cohort.Name; runId = $runId; generatedAtUtc = $generatedAtUtc;
                repositoryHead = $repositoryHead; sourceState = $sourceState;
                command = "dotnet build ArquitecturaBase.slnx --no-restore; dotnet test --no-build --no-restore";
                producer = ($cohort.Sources | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) }) -join " and "; sources = $cohort.SourceHashes;
                transport = "Actual HTTP; PostgreSQL/Redis Testcontainers; synthetic records and private files"; captures = $captures }
            $json = ($provenance | ConvertTo-Json -Depth 8).Replace([string][char]13, "") + [char]10
            [IO.File]::WriteAllText((Join-Path $targetRoot "provenance.json"), $json, [Text.UTF8Encoding]::new($false))
        }
        Write-Output "Backend build and complete tests passed. $($cohorts.Count) HTTP cohorts exported from $runRoot. EXPLAIN stays in local artifacts."
    } finally { Pop-Location }
} finally {
    foreach ($name in $previousOutputs.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousOutputs[$name])
    }
}
