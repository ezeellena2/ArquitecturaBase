$ErrorActionPreference = "Stop"
$backendRoot = (Resolve-Path (Join-Path $PSScriptRoot "../../../../..")).Path
$exporterPath = Join-Path $PSScriptRoot "export-responses-http.ps1"
$tokens = $null
$parseErrors = $null
$exporterAst = [Management.Automation.Language.Parser]::ParseFile($exporterPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw "The exporter contains PowerShell syntax errors." }
$mergeAst = $exporterAst.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq "Merge-WhatsAppFormCaptures" }, $false)
if ($null -eq $mergeAst) { throw "The exporter does not define its capture merge." }
. ([scriptblock]::Create($mergeAst.Extent.Text))
$testRoot = Join-Path $backendRoot (".artifacts/assistant-validation/exporter-script-tests/" + [Guid]::NewGuid().ToString("N"))
$baseNames = @("choices-forbidden", "choices", "preparation", "manifest", "outgoing-accepted", "outgoing-failed", "outgoing-pending", "outgoing-uncertain", "send-before-failed", "send-before-uncertain", "send-context-changed", "send-pending", "send-version-unavailable", "thread-accepted", "thread-before-send", "thread-failed", "thread-pending", "thread-reloaded", "thread-uncertain")
$replyNames = @("reply-all-states", "reply-completed", "reply-inbox-read", "reply-inbox-without-read", "reply-latest-page", "reply-multiple-choice", "reply-older-page", "reply-pending", "reply-rejected", "reply-reloaded", "reply-unrecognized", "reply-without-read")
$privacyNames = @("thread-read-only", "thread-send-with-users-read", "thread-send-without-users-read")
$originalPresentationNames = @("original-only-choices", "original-only-preparation", "original-only-receipt", "original-only-history", "empty-secondary-choices", "empty-secondary-preparation", "empty-secondary-receipt", "empty-secondary-history")
function Assert-Condition([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Read-ExporterAssignment([string]$Name) {
    $assignment = $exporterAst.Find({ param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and $node.Left.VariablePath.UserPath -eq $Name }, $false)
    Assert-Condition ($null -ne $assignment) "The exporter is missing the $Name configuration."
    & ([scriptblock]::Create($assignment.Right.Extent.Text))
}
function Write-SyntheticCapture([string]$Path, [string]$Marker) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllBytes($Path, ([Text.Encoding]::UTF8.GetBytes($Marker) + [byte[]]@(0, 255, 13, 10)))
}
function New-CaptureCase([string]$Name) {
    $caseRoot = Join-Path $testRoot $Name
    $paths = @{ Replies = (Join-Path $caseRoot "replies"); Privacy = (Join-Path $caseRoot "privacy"); Destination = (Join-Path $caseRoot "canonical") }
    foreach ($culture in @("es", "en")) {
        foreach ($name in $baseNames) { Write-SyntheticCapture (Join-Path $paths.Destination "$culture/$name.json") "C1:$culture/$name" }
        foreach ($name in @($replyNames + @("choices", "preparation", "reply-unknown"))) { Write-SyntheticCapture (Join-Path $paths.Replies "$culture/$name.json") "REPLY:$culture/$name" }
        foreach ($name in @($privacyNames + @("thread-unknown"))) { Write-SyntheticCapture (Join-Path $paths.Privacy "$culture/$name.json") "PRIVACY:$culture/$name" }
    }
    foreach ($name in $originalPresentationNames) { Write-SyntheticCapture (Join-Path $paths.Destination "en/$name.json") "ORIGINAL:en/$name" }
    return $paths
}
$failures = 0
foreach ($case in @("Configuration", "Merge", "Missing", "Collision")) {
    try {
        switch ($case) {
            "Configuration" {
                $sourceRoot = "tests/ArquitecturaBase.Api.IntegrationTests/Modules/"
                $captureRoot = "tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/"
                $extras = Read-ExporterAssignment "whatsappFormExtraRoots"
                Assert-Condition ($extras.Count -eq 2) "Exactly two additional output environments are required."
                Assert-Condition ($extras["WHATSAPP_FORM_REPLIES_HTTP_OUTPUT"] -eq "whatsapp-form-replies") "Replies require their own raw root."
                Assert-Condition ($extras["WHATSAPP_FORM_PRIVACY_HTTP_OUTPUT"] -eq "whatsapp-form-privacy") "Privacy requires its own raw root."
                $cohorts = @(Read-ExporterAssignment "cohorts")
                Assert-Condition ($cohorts.Count -eq 22) "The joint export contains twenty-two cohorts, including person data."
                $responses = @($cohorts | Where-Object { $_.Name -eq "responses" })[0]
                $pdf = @($cohorts | Where-Object { $_.Name -eq "pdf" })[0]
                $definitions = @($cohorts | Where-Object { $_.Name -eq "forms" })[0]
                Assert-Condition ($responses.RequiredNames.Count -eq 55) "Responses must preserve the prior artifacts and add ten captured extra-field artifacts."
                Assert-Condition ($pdf.RequiredNames.Count -eq 32) "PDF must preserve twenty-eight artifacts and add four multiple-choice artifacts."
                Assert-Condition ($definitions.RequiredNames.Count -eq 60) "Definitions must preserve the prior bodies and add twelve recipient filter bodies."
                foreach ($culture in @("es", "en")) {
                    foreach ($name in @("sheet", "detail", "counts")) { Assert-Condition ("multi-$name-$culture.json" -in $responses.RequiredNames) "A multiple-choice response capture is missing." }
                    Assert-Condition ("multi-export-$culture.xlsx" -in $responses.RequiredNames) "A multiple-choice workbook capture is missing."
                    foreach ($extension in @("pdf", "http.json")) { Assert-Condition ("multi-preview-$culture.$extension" -in $pdf.RequiredNames) "A multiple-choice PDF capture is missing." }
                }
                foreach ($name in @("draft", "catalog-too-large")) { Assert-Condition ("multiple-choice/$name.json" -in $definitions.RequiredNames) "A multiple-choice definition capture is missing." }
                Assert-Condition ("multiple-choice" -in $definitions.IncludeDirectories) "The definition cohort must retain multiple-choice captures."
                foreach ($cohort in @($responses, $pdf)) {
                    Assert-Condition (($sourceRoot + "BusinessForms/FormMultipleChoiceReadHttpTests.cs") -in $cohort.Sources) "The multiple-choice read producer SHA must be included."
                    Assert-Condition (@($cohort.RequiredNames | Select-Object -Unique).Count -eq $cohort.RequiredNames.Count) "Capture inventories must not duplicate artifacts."
                }
                Assert-Condition (($sourceRoot + "BusinessForms/FormMultipleChoiceAuthoringHttpTests.cs") -in $definitions.Sources) "The multiple-choice authoring producer SHA must be included."
                foreach ($helper in @("FormDraftEditingTests.cs", "FormDraftDurabilityTests.cs", "FormPublicationAcceptanceTests.cs")) {
                    Assert-Condition (($sourceRoot + "BusinessForms/" + $helper) -in $definitions.Sources) "The multiple-choice authoring helper SHA must be included."
                }
                Assert-Condition (($captureRoot + "Modules/BusinessForms/ReadyStructuredFormChannel.cs") -in $definitions.Sources) "The publication channel double SHA must be included."
                $imports = @($cohorts | Where-Object { $_.Name -eq "catalog-imports" })[0]
                Assert-Condition ($null -ne $imports) "Catalog imports require their own capture cohort."
                Assert-Condition ($imports.Environment -eq "FORM_CATALOG_IMPORT_HTTP_OUTPUT") "Catalog imports require their own raw output."
                Assert-Condition ($imports.Destination -eq "catalogs-http/imports") "Catalog imports require the agreed fixture destination."
                $importNames = @("replace-preview.json", "append-preview.json", "already-applied.json", "empty-preview.json", "applied.json", "conflict.json", "confirmation-required.json", "round-trip-preview.json", "export.xlsx", "export.http.json", "template.xlsx", "template.http.json", "extra-source-preview.json", "extra-source-applied.json", "extra-source-unchanged.json")
                $expectedImports = @(@("es", "en") | ForEach-Object { $culture = $_; $importNames | ForEach-Object { "$culture/$_" } })
                Assert-Condition (@(Compare-Object $expectedImports $imports.RequiredNames).Count -eq 0) "Catalog imports must contain thirty actual HTTP artifacts."
                Assert-Condition (($sourceRoot + "BusinessForms/FormCatalogImportHttpTests.cs") -in $imports.Sources) "The import producer SHA must be included."
                Assert-Condition (($sourceRoot + "BusinessForms/FormCatalogImportFieldsHttpTests.cs") -in $imports.Sources) "The source field import producer SHA must be included."
                Assert-Condition (($captureRoot + "Modules/BusinessForms/FormCatalogImportContractCapture.cs") -in $imports.Sources) "The import capture SHA must be included."
                $manual = @($cohorts | Where-Object { $_.Name -eq "manual-catalogs" })[0]
                Assert-Condition ($manual.RequiredNames.Count -eq 41) "Manual catalogs must preserve prior captures and add fourteen extra-field artifacts."
                $typedNames = @("typed-text-created.json", "typed-text-items.json", "typed-integer-created.json", "typed-integer-items.json", "typed-decimal-created.json", "typed-decimal-items.json", "typed-date-created.json", "typed-date-items.json", "typed-invalid-value-es.json", "typed-invalid-value-en.json", "typed-required-value.json", "typed-type-change-rejected.json")
                foreach ($name in $typedNames) { Assert-Condition ($name -in $manual.RequiredNames) "A typed catalog capture is missing from the required inventory." }
                Assert-Condition (($sourceRoot + "BusinessForms/FormCatalogValueHttpTests.cs") -in $manual.Sources) "The typed catalog producer SHA must be included."
                foreach ($name in @("fields-created", "fields-required-error", "fields-values-saved", "fields-conversion-error", "fields-unique-error", "fields-detail", "fields-items", "fields-removal-confirmation", "fields-removed", "fields-presentation", "fields-source-items", "fields-source-in-use")) {
                    Assert-Condition (("$name.json") -in $manual.RequiredNames) "An extra-field catalog HTTP body is missing."
                }
                foreach ($name in @("fields-create", "fields-update")) { Assert-Condition (("$name.request.json") -in $manual.RequiredNames) "An extra-field request artifact is missing." }
                Assert-Condition (($sourceRoot + "BusinessForms/FormCatalogEndpointsTests.Fields.cs") -in $manual.Sources) "The extra-field catalog producer SHA must be included."
                Assert-Condition ("recipient-filters" -in $definitions.IncludeDirectories) "The definition cohort must retain recipient filter captures."
                foreach ($culture in @("es", "en")) {
                    foreach ($name in @("source-draft", "recipient-samples", "recipient-ready", "recipient-missing", "recipient-unavailable", "source-check-warnings")) {
                        Assert-Condition (("recipient-filters/$name-$culture.json") -in $definitions.RequiredNames) "A recipient filter body is missing."
                    }
                    foreach ($name in @("sheet", "detail", "legacy")) { Assert-Condition (("extra-fields-$name-$culture.json") -in $responses.RequiredNames) "An extra-field response body is missing." }
                    foreach ($name in @("export", "visible-export")) { Assert-Condition (("extra-fields-$name-$culture.xlsx") -in $responses.RequiredNames) "An extra-field workbook is missing." }
                }
                Assert-Condition (($sourceRoot + "BusinessForms/FormRecipientFilterIntegrationTests.cs") -in $definitions.Sources) "The recipient filter producer SHA must be included."
                Assert-Condition (($captureRoot + "Modules/BusinessForms/FormRecipientScenario.cs") -in $definitions.Sources) "The recipient scenario SHA must be included."
                Assert-Condition (($sourceRoot + "BusinessForms/FormCatalogFieldsReadHttpTests.cs") -in $responses.Sources) "The extra-field response producer SHA must be included."
                $personData = @($cohorts | Where-Object { $_.Name -eq "person-data" })[0]
                Assert-Condition ($null -ne $personData -and $personData.Environment -eq "FORM_PERSON_DATA_HTTP_OUTPUT" -and $personData.Destination -eq "forms-person-data-http") "Person data require their own agreed output and destination."
                $personNames = @("attribute-created", "person-options", "person-data-empty", "person-data-saved", "person-data", "people", "conflict", "invalid-value", "attribute-updated", "attributes", "forbidden", "source-in-use", "attribute-value-removal-confirmation")
                $expectedPersonData = @(@("es", "en") | ForEach-Object { $culture = $_; $personNames | ForEach-Object { "$_-$culture.json" } })
                Assert-Condition (@(Compare-Object $expectedPersonData $personData.RequiredNames).Count -eq 0) "Person data must contain all twenty-six actual HTTP bodies."
                Assert-Condition (($sourceRoot + "BusinessForms/FormPersonDataHttpTests.cs") -in $personData.Sources) "The person data producer SHA must be included."
                Assert-Condition (($captureRoot + "Modules/BusinessForms/FormPersonDataContractCapture.cs") -in $personData.Sources) "The person data capture SHA must be included."
                $catalogSending = @($cohorts | Where-Object { $_.Name -eq "whatsapp-catalogs" })[0]
                Assert-Condition ($catalogSending.RequiredNames.Count -eq 28) "WhatsApp catalogs must add two recipient failure bodies to the prior twenty-six."
                foreach ($culture in @("es", "en")) {
                    Assert-Condition (("$culture/preparation-recipient-data-missing.json") -in $catalogSending.RequiredNames) "A blocked recipient preparation body is missing."
                }
                Assert-Condition (($sourceRoot + "WhatsApp/WhatsAppCatalogRecipientSendingTests.cs") -in $catalogSending.Sources) "The real recipient sending producer SHA must be included."
                foreach ($helper in @("WhatsAppFormSendingTests.cs", "WhatsAppFormHttpContractTests.cs")) {
                    Assert-Condition (($sourceRoot + "WhatsApp/" + $helper) -in $catalogSending.Sources) "The actual recipient sending helper SHA must be included."
                }
                Assert-Condition (($captureRoot + "Modules/BusinessForms/FormRecipientScenario.cs") -notin $catalogSending.Sources) "Unrelated recipient preview helpers must not be attributed to sending."
                $approvals = @($cohorts | Where-Object { $_.Name -eq "approvals" })[0]
                Assert-Condition (($sourceRoot + "BusinessForms/FormApprovalContinuationHttpTests.cs") -in $approvals.Sources) "Approval continuation HTTP producer SHA must be included."
                Assert-Condition (($captureRoot + "Modules/BusinessForms/FormApprovalContinuationFixture.cs") -in $approvals.Sources) "Approval continuation fixture SHA must be included."
                $lifecycle = @($cohorts | Where-Object { $_.Name -eq "lifecycle" })[0]
                Assert-Condition ($lifecycle.Environment -eq "FORM_LIFECYCLE_HTTP_OUTPUT") "Lifecycle requires its own raw output."
                Assert-Condition ($lifecycle.Destination -eq "forms-lifecycle-http") "Lifecycle requires its own fixture cohort."
                $lifecycleNames = @("preview", "deactivated", "detail-deactivated", "list-deactivated", "facets", "options-unavailable", "detail-reactivation-pending", "detail-reactivated", "preview-affected", "options-deleted", "delete-responses-conflict", "status-pending", "status-uncertain", "status-needsattention", "status-deprecated")
                $expectedLifecycle = @(@("es", "en") | ForEach-Object { $culture = $_; $lifecycleNames | ForEach-Object { "$culture/$_.json" } })
                Assert-Condition (@(Compare-Object $expectedLifecycle $lifecycle.RequiredNames).Count -eq 0) "Lifecycle must include the thirty actual HTTP bodies."
                Assert-Condition (($sourceRoot + "BusinessForms/FormDeactivationHttpTests.cs") -in $lifecycle.Sources) "Lifecycle producer SHA must be included."
                Assert-Condition (($captureRoot + "Modules/BusinessForms/FormLifecycleContractCapture.cs") -in $lifecycle.Sources) "Lifecycle capture SHA must be included."
                Assert-Condition (($sourceRoot + "BusinessForms/FormDeletionHttpTests.cs") -in $lifecycle.Sources) "Deletion producer SHA must be included."
                Assert-Condition (($sourceRoot + "BusinessForms/FormRetirementStatusHttpTests.cs") -in $lifecycle.Sources) "Local retirement status producer SHA must be included."
                Assert-Condition (($captureRoot + "Modules/BusinessForms/ControlledFormChannelStateSource.cs") -in $lifecycle.Sources) "The controlled local channel source SHA must be included."
                $forms = @($cohorts | Where-Object { $_.Name -eq "whatsapp-forms" })[0]
                $expected = @(@("es", "en") | ForEach-Object { $culture = $_; @($baseNames + $replyNames + $privacyNames) | ForEach-Object { "$culture/$_.json" } }) + @($originalPresentationNames | ForEach-Object { "en/$_.json" })
                Assert-Condition ($forms.RequiredNames.Count -eq 76) "The canonical WhatsApp form inventory must contain all seventy-six captures."
                Assert-Condition (@(Compare-Object $expected $forms.RequiredNames).Count -eq 0) "The canonical capture names differ."
                Assert-Condition (($sourceRoot + "WhatsApp/WhatsAppFormReplyAppPreviewTests.cs") -in $forms.Sources) "Reply producer SHA must be included."
                Assert-Condition (($sourceRoot + "WhatsApp/WhatsAppMultipleChoiceCompositionTests.cs") -in $forms.Sources) "The real multiple-choice composition producer SHA must be included."
                Assert-Condition (($sourceRoot + "WhatsApp/WhatsAppFormPrivacyTests.cs") -in $forms.Sources) "Privacy producer SHA must be included."
                foreach ($name in @("responses", "workflow", "pdf")) {
                    $cohort = @($cohorts | Where-Object { $_.Name -eq $name })[0]
                    Assert-Condition (($captureRoot + "Modules/BusinessForms/FormPublishedVersionFixture.cs") -in $cohort.Sources) "Published version fixture SHA must be included in $name."
                }
            }
            "Merge" {
                $paths = New-CaptureCase "merge"
                $before = @{}
                Get-ChildItem -LiteralPath $paths.Destination -File -Recurse | ForEach-Object { $before[$_.FullName] = (Get-FileHash -LiteralPath $_.FullName).Hash }
                Merge-WhatsAppFormCaptures $paths.Replies $paths.Privacy $paths.Destination
                Assert-Condition (@(Get-ChildItem -LiteralPath $paths.Destination -File -Recurse).Count -eq 76) "Merge must add exactly twenty-four replies and six privacy bodies."
                foreach ($path in $before.Keys) { Assert-Condition ((Get-FileHash -LiteralPath $path).Hash -eq $before[$path]) "Existing C1 bytes, including choices, must remain unchanged." }
                foreach ($culture in @("es", "en")) {
                    foreach ($name in $replyNames) { Assert-Condition ((Get-FileHash -LiteralPath (Join-Path $paths.Replies "$culture/$name.json")).Hash -eq (Get-FileHash -LiteralPath (Join-Path $paths.Destination "$culture/$name.json")).Hash) "Reply bytes changed during merge." }
                    foreach ($name in $privacyNames) { Assert-Condition ((Get-FileHash -LiteralPath (Join-Path $paths.Privacy "$culture/$name.json")).Hash -eq (Get-FileHash -LiteralPath (Join-Path $paths.Destination "$culture/$name.json")).Hash) "Privacy bytes changed during merge." }
                    Assert-Condition ((Get-FileHash -LiteralPath (Join-Path $paths.Destination "$culture/preparation.json")).Hash -eq $before[(Join-Path $paths.Destination "$culture/preparation.json")]) "The manual preparation body must remain unchanged."
                }
            }
            "Missing" {
                $paths = New-CaptureCase "missing"
                $missingPath = Join-Path $paths.Replies "es/reply-completed.json"
                [IO.File]::Move($missingPath, $missingPath + ".withheld")
                $failure = $null
                try { Merge-WhatsAppFormCaptures $paths.Replies $paths.Privacy $paths.Destination } catch { $failure = $_.Exception.Message }
                Assert-Condition ($null -ne $failure -and $failure -like "Missing WhatsApp form capture:*") "A missing required capture must reject the merge."
            }
            "Collision" {
                $paths = New-CaptureCase "collision"
                $existingPath = Join-Path $paths.Destination "es/reply-completed.json"
                Write-SyntheticCapture $existingPath "EXISTING"
                $before = (Get-FileHash -LiteralPath $existingPath).Hash
                $failure = $null
                try { Merge-WhatsAppFormCaptures $paths.Replies $paths.Privacy $paths.Destination } catch { $failure = $_.Exception.Message }
                Assert-Condition ($null -ne $failure -and $failure -like "Duplicate WhatsApp form capture:*") "An existing capture must reject the merge."
                Assert-Condition ((Get-FileHash -LiteralPath $existingPath).Hash -eq $before) "A collision must preserve the original bytes."
            }
        }
        Write-Output "PASS $case"
    } catch {
        $failures++
        Write-Output "FAIL $case -- $($_.Exception.Message)"
    }
}
Write-Output "Exporter script checks: $((4 - $failures))/4 passed. Synthetic test files remain in $testRoot."
if ($failures -ne 0) { exit 1 }
