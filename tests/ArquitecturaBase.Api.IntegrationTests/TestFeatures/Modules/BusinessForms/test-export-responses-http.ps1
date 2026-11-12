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
$replyNames = @("reply-all-states", "reply-completed", "reply-inbox-read", "reply-inbox-without-read", "reply-latest-page", "reply-multiple-choice", "reply-older-page", "reply-pending", "reply-rejected", "reply-reloaded", "reply-rows", "reply-unrecognized", "reply-without-read")
$privacyNames = @("thread-read-only", "thread-send-with-users-read", "thread-send-without-users-read")
$originalPresentationNames = @("original-only-choices", "original-only-preparation", "original-only-receipt", "original-only-history", "empty-secondary-choices", "empty-secondary-preparation", "empty-secondary-receipt", "empty-secondary-history")
# W1-A: the location asked by WhatsApp before the Flow, captured in Spanish only.
$locationNames = @("thread-location-requested", "thread-location-received")
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
                Assert-Condition ($cohorts.Count -eq 28) "The joint export contains twenty-eight cohorts, including the Home assistant, its proposals, the dashboard, the received documents and the WhatsApp assistant agent."
                $dashboard = @($cohorts | Where-Object { $_.Name -eq "dashboard" })[0]
                Assert-Condition ($null -ne $dashboard -and $dashboard.Environment -eq "FORM_DASHBOARD_HTTP_OUTPUT" -and $dashboard.Destination -eq "forms-dashboard-http") "The dashboard (DOC7-A) requires its own agreed output and destination."
                $expectedDashboard = @("dashboard-day-es.json", "dashboard-week-en.json", "dashboard-month-es.json", "dashboard-filtered-es.json", "dashboard-own-es.json", "dashboard-empty-es.json", "dashboard-catalog-en.json", "dashboard-invalid-period-es.json", "dashboard-overflow-es.json", "dashboard-people-es.json")
                Assert-Condition (@(Compare-Object $expectedDashboard $dashboard.RequiredNames).Count -eq 0) "The dashboard must contain its ten actual HTTP bodies."
                foreach ($producer in @(($sourceRoot + "BusinessForms/FormDashboardHttpTests.cs"), ($captureRoot + "Modules/BusinessForms/FormDashboardScenario.cs"), ($captureRoot + "Modules/BusinessForms/FormDashboardContractCapture.cs"))) {
                    Assert-Condition ($producer -in $dashboard.Sources) "The dashboard producer, scenario and capture SHA must be included."
                }
                $documents = @($cohorts | Where-Object { $_.Name -eq "documents" })[0]
                Assert-Condition ($null -ne $documents -and $documents.Environment -eq "FORM_DOCUMENTS_HTTP_OUTPUT" -and $documents.Destination -eq "forms-documents-http") "The received documents (DOC5-A) require their own agreed output and destination."
                $expectedDocuments = @("documents-list-es.json", "documents-counts-es.json", "documents-invalid-search-es.json", "document-detail-es.json", "document-detail-en.json", "document-discarded-es.json", "document-revision-conflict-es.json", "document-extraction-accepted-es.json", "document-extraction-pending-es.json", "document-extraction-completed-es.json", "document-extraction-failed-es.json", "document-extraction-unavailable-es.json", "document-awaiting-sender-es.json", "document-record-es.json", "document-sender-inactive-es.json")
                Assert-Condition (@(Compare-Object $expectedDocuments $documents.RequiredNames).Count -eq 0) "The received documents must contain their fifteen actual HTTP bodies, including the engine that fails and the person who can still complete it."
                foreach ($producer in @(($sourceRoot + "BusinessForms/FormReceivedDocumentHttpTests.cs"), ($captureRoot + "Modules/BusinessForms/FormReceivedDocumentScenario.cs"), ($captureRoot + "Modules/BusinessForms/FormReceivedDocumentContractCapture.cs"), ($captureRoot + "Modules/BusinessForms/ScriptedDocumentOperations.cs"))) {
                    Assert-Condition ($producer -in $documents.Sources) "The received documents producer, scenario, scripted engine and capture SHA must be included."
                }
                $agent = @($cohorts | Where-Object { $_.Name -eq "whatsapp-assistant-agent" })[0]
                Assert-Condition ($null -ne $agent -and $agent.Environment -eq "WHATSAPP_ASSISTANT_AGENT_HTTP_OUTPUT" -and $agent.Destination -eq "whatsapp-assistant-agent-http") "The WhatsApp assistant agent requires its own agreed output and destination."
                $expectedAgent = @("agent-defaults.json", "agent-saved.json", "agent-conflict.json", "agent-invalid.json", "test-accepted.json", "test-pending.json", "test-completed.json", "test-recipients.json", "chats-needs-person.json", "thread-agent.json", "suggestion-text-ready.json")
                Assert-Condition (@(Compare-Object $expectedAgent $agent.RequiredNames).Count -eq 0) "The WhatsApp assistant agent must contain its eleven actual HTTP bodies, from the screen, Probar and the chat (AS4)."
                foreach ($producer in @(($sourceRoot + "WhatsApp/WhatsAppAssistantAgentEndpointsTests.cs"), ($captureRoot + "Modules/WhatsApp/WhatsAppAssistantAgentContractCapture.cs"))) {
                    Assert-Condition ($producer -in $agent.Sources) "The WhatsApp assistant agent producer and capture SHA must be included."
                }
                $proposals = @($cohorts | Where-Object { $_.Name -eq "assistant-proposals" })[0]
                Assert-Condition ($null -ne $proposals -and $proposals.Environment -eq "FORM_ASSISTANT_PROPOSAL_HTTP_OUTPUT" -and $proposals.RequiredNames.Count -eq 16) "Creating from a Home proposal requires its own output and its sixteen bodies, including the catalog created from a spreadsheet proposal."
                $assistant = @($cohorts | Where-Object { $_.Name -eq "ai-assistant" })[0]
                Assert-Condition ($null -ne $assistant -and $assistant.Environment -eq "AI_ASSISTANT_HTTP_OUTPUT" -and $assistant.Destination -eq "ai-assistant-http") "The Home assistant requires its own agreed output and destination."
                $expectedAssistant = @("bootstrap.json", "submitted.json", "turn-pending.json", "turn-completed.json", "turns.json", "canceled.json", "file-submitted.json", "turn-attachment.json", "document-status.json", "turn-document.json", "turn-word.json")
                Assert-Condition (@(Compare-Object $expectedAssistant $assistant.RequiredNames).Count -eq 0) "The Home assistant must contain its eleven actual HTTP bodies, including the turn with a spreadsheet, the reading of a document and the turns with a document and a Word."
                foreach ($producer in @(($sourceRoot + "ArtificialIntelligence/AiAssistantHttpContractTests.cs"), ($captureRoot + "Modules/ArtificialIntelligence/AiAssistantContractCapture.cs"), ($captureRoot + "Modules/ArtificialIntelligence/AiAssistantExecutionScenario.cs"), ($captureRoot + "Modules/ArtificialIntelligence/AiAssistantExecutionProviderHandler.cs"), ($captureRoot + "Modules/ArtificialIntelligence/HomeAudioFiles.cs"), ($captureRoot + "Modules/ArtificialIntelligence/HomeAttachmentReader.cs"), ($captureRoot + "Modules/ArtificialIntelligence/DocumentReadingProviderFixture.cs"), "src/ArquitecturaBase.Application/Modules/ArtificialIntelligence/Models/AiAssistantResponses.cs", "src/ArquitecturaBase.Application/Modules/ArtificialIntelligence/Models/AiAssistantDocumentModels.cs")) {
                    Assert-Condition ($producer -in $assistant.Sources) "The Home assistant producer, capture, scenario, provider and storage doubles and DTO SHA must be included."
                }
                $responses = @($cohorts | Where-Object { $_.Name -eq "responses" })[0]
                $pdf = @($cohorts | Where-Object { $_.Name -eq "pdf" })[0]
                $definitions = @($cohorts | Where-Object { $_.Name -eq "forms" })[0]
                Assert-Condition ($responses.RequiredNames.Count -eq 66) "Responses must preserve the prior artifacts, ten captured extra-field artifacts, eight rows artifacts, the two of an external result and the detail of a location asked by WhatsApp (W1-A)."
                foreach ($name in @("result-detail-es.json", "result-sheet-es.json")) { Assert-Condition ($name -in $responses.RequiredNames) "An external result capture (RE1-A) is missing." }
                Assert-Condition ("detail-location-whatsapp-es.json" -in $responses.RequiredNames) "The detail of a location asked by WhatsApp (W1-A) is missing."
                Assert-Condition ($pdf.RequiredNames.Count -eq 36) "PDF must preserve twenty-eight artifacts and add four multiple-choice and four rows artifacts."
                Assert-Condition ($definitions.RequiredNames.Count -eq 93) "Definitions must preserve the prior bodies, sixteen recipient filter bodies, four rows bodies, two section title bodies, two library bodies, five folder bodies, six app-only bodies, eight document reading bodies and two bodies with what a question receives (W3)."
                foreach ($directory in @("library", "folders")) { Assert-Condition ($directory -in $definitions.IncludeDirectories) "The definition cohort must retain the library and folder captures." }
                foreach ($name in @("library/list-es.json", "library/copy-created.json", "folders/tree-designs-es.json", "folders/deletion.json", "folders/menu-es.json", "folders/created.json", "folders/item-folders.json")) { Assert-Condition ($name -in $definitions.RequiredNames) "A library or folder capture is missing." }
                foreach ($name in @("created", "accepted", "status-es", "detail-es", "list-es", "suggestion-options-es")) { Assert-Condition ("app-only/$name.json" -in $definitions.RequiredNames) "An app-only capture is missing, including the Published receipt and the suggestion options." }
                Assert-Condition ("app-only" -in $definitions.IncludeDirectories) "The definition cohort must retain the app-only captures."
                Assert-Condition (($sourceRoot + "BusinessForms/FormAppOnlyPublicationTests.cs") -in $definitions.Sources) "The app-only producer SHA must be included."
                Assert-Condition ("document-reading" -in $definitions.IncludeDirectories) "The definition cohort must retain the document reading captures (DOC1)."
                foreach ($name in @("library-es", "copy-transfer-receipt", "draft-saved", "detail-es", "check-lookup-invalid-es", "stats-es", "entry-bank-account-invalid-es", "entry-tax-id-invalid-es")) { Assert-Condition ("document-reading/$name.json" -in $definitions.RequiredNames) "A document reading capture (DOC1) is missing." }
                foreach ($producer in @(($sourceRoot + "BusinessForms/FormDocumentReadingHttpTests.cs"), ($captureRoot + "Modules/BusinessForms/AssistantCopyFiles.cs"))) { Assert-Condition ($producer -in $definitions.Sources) "The document reading producer and storage double SHA must be included." }
                Assert-Condition ("location-contact" -in $definitions.IncludeDirectories) "The definition cohort must retain the captures of what a question receives (W3)."
                foreach ($name in @("draft-saved", "detail-es")) { Assert-Condition ("location-contact/$name.json" -in $definitions.RequiredNames) "A capture of what a question receives (W3) is missing." }
                Assert-Condition (($sourceRoot + "BusinessForms/FormQuestionReceivesHttpTests.cs") -in $definitions.Sources) "The producer SHA of what a question receives (W3) must be included."
                foreach ($name in @("rows/draft", "rows/publication-check", "rows/publication-check-moved", "rows/publication-check-minimum", "sections/draft", "sections/publication-check")) {
                    Assert-Condition ("$name.json" -in $definitions.RequiredNames) "A rows or section title definition capture is missing."
                }
                foreach ($directory in @("rows", "sections")) { Assert-Condition ($directory -in $definitions.IncludeDirectories) "The definition cohort must retain rows and section title captures." }
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
                $expectedImports = @(@("es", "en") | ForEach-Object { $culture = $_; $importNames | ForEach-Object { "$culture/$_" } }) +
                    @("es/en-original-preview.json", "es/en-original-export.xlsx", "es/en-original-export.http.json") +
                    @("es/assistant-pending.json", "es/assistant-preview.json", "es/assistant-applied.json", "es/assistant-none.json", "es/assistant-proposal-unavailable.json", "es/assistant-sheet-unavailable.json")
                Assert-Condition (@(Compare-Object $expectedImports $imports.RequiredNames).Count -eq 0) "Catalog imports must contain thirty-nine actual HTTP artifacts, including the English-original preview and export and the import from the assistant's copy."
                Assert-Condition (($sourceRoot + "BusinessForms/FormCatalogImportHttpTests.cs") -in $imports.Sources) "The import producer SHA must be included."
                Assert-Condition (($sourceRoot + "BusinessForms/FormCatalogImportFieldsHttpTests.cs") -in $imports.Sources) "The source field import producer SHA must be included."
                Assert-Condition (($captureRoot + "Modules/BusinessForms/FormCatalogImportContractCapture.cs") -in $imports.Sources) "The import capture SHA must be included."
                Assert-Condition (($sourceRoot + "BusinessForms/FormCatalogAssistantImportHttpTests.cs") -in $imports.Sources) "The assistant import producer SHA must be included."
                $manual = @($cohorts | Where-Object { $_.Name -eq "manual-catalogs" })[0]
                Assert-Condition ($manual.RequiredNames.Count -eq 50) "Manual catalogs must preserve prior captures, eighteen extra-field artifacts, the optional description, three original-language bodies and the sorted request."
                Assert-Condition ("sorted-request.request.json" -in $manual.RequiredNames) "The «Ordenar por…» request body is missing."
                Assert-Condition (($sourceRoot + "BusinessForms/FormCatalogEndpointsTests.Sort.cs") -in $manual.Sources) "The «Ordenar por…» producer SHA must be included."
                Assert-Condition ("description-optional-created.json" -in $manual.RequiredNames) "The optional catalog description body is missing."
                foreach ($name in @("english-original-created", "english-original-detail-es", "original-missing-error")) { Assert-Condition (("$name.json") -in $manual.RequiredNames) "An original-language catalog body is missing." }
                $typedNames = @("typed-text-created.json", "typed-text-items.json", "typed-integer-created.json", "typed-integer-items.json", "typed-decimal-created.json", "typed-decimal-items.json", "typed-date-created.json", "typed-date-items.json", "typed-invalid-value-es.json", "typed-invalid-value-en.json", "typed-required-value.json", "typed-type-change-rejected.json")
                foreach ($name in $typedNames) { Assert-Condition ($name -in $manual.RequiredNames) "A typed catalog capture is missing from the required inventory." }
                Assert-Condition (($sourceRoot + "BusinessForms/FormCatalogValueHttpTests.cs") -in $manual.Sources) "The typed catalog producer SHA must be included."
                foreach ($name in @("fields-created", "fields-required-error", "fields-values-saved", "fields-conversion-error", "fields-unique-error", "fields-rules-error-es", "fields-rules-error-en", "fields-detail", "fields-items", "fields-removal-confirmation", "fields-removed", "fields-presentation", "fields-source-items", "fields-source-in-use", "fields-in-use", "fields-usage-detail")) {
                    Assert-Condition (("$name.json") -in $manual.RequiredNames) "An extra-field catalog HTTP body is missing."
                }
                foreach ($name in @("fields-create", "fields-update")) { Assert-Condition (("$name.request.json") -in $manual.RequiredNames) "An extra-field request artifact is missing." }
                Assert-Condition (($sourceRoot + "BusinessForms/FormCatalogEndpointsTests.Fields.cs") -in $manual.Sources) "The extra-field catalog producer SHA must be included."
                foreach ($producer in @(($sourceRoot + "BusinessForms/FormCatalogFieldInUseIntegrationTests.cs"), ($captureRoot + "Modules/BusinessForms/FormRecipientScenario.cs"), ($captureRoot + "Modules/BusinessForms/FormCatalogContractCapture.cs"))) {
                    Assert-Condition ($producer -in $manual.Sources) "The field-in-use catalog producer, scenario and capture SHA must be included."
                }
                Assert-Condition ("recipient-filters" -in $definitions.IncludeDirectories) "The definition cohort must retain recipient filter captures."
                foreach ($culture in @("es", "en")) {
                    foreach ($name in @("source-draft", "recipient-samples", "recipient-ready", "recipient-missing", "recipient-unavailable", "source-check-warnings", "recipient-maximum-person", "recipient-maximum-fixed")) {
                        Assert-Condition (("recipient-filters/$name-$culture.json") -in $definitions.RequiredNames) "A recipient filter body is missing."
                    }
                    foreach ($name in @("sheet", "detail", "legacy")) { Assert-Condition (("extra-fields-$name-$culture.json") -in $responses.RequiredNames) "An extra-field response body is missing." }
                    foreach ($name in @("export", "visible-export")) { Assert-Condition (("extra-fields-$name-$culture.xlsx") -in $responses.RequiredNames) "An extra-field workbook is missing." }
                }
                Assert-Condition (($sourceRoot + "BusinessForms/FormRecipientFilterIntegrationTests.cs") -in $definitions.Sources) "The recipient filter producer SHA must be included."
                Assert-Condition (($captureRoot + "Modules/BusinessForms/FormRecipientScenario.cs") -in $definitions.Sources) "The recipient scenario SHA must be included."
                Assert-Condition (($sourceRoot + "BusinessForms/FormCatalogFieldsReadHttpTests.cs") -in $responses.Sources) "The extra-field response producer SHA must be included."
                $rules = @($cohorts | Where-Object { $_.Name -eq "rules" })[0]
                foreach ($producer in @("FormRowsRuleHttpTests.cs", "FormRowsReadHttpTests.cs", "FormRowsCaptureIntegrationTests.cs", "FormRecordCaptureIntegrationTests.cs")) {
                    Assert-Condition (($sourceRoot + "BusinessForms/" + $producer) -in $rules.Sources) "The rules of the block of rows, their seed and its capture SHA must be included."
                }
                $personData = @($cohorts | Where-Object { $_.Name -eq "person-data" })[0]
                Assert-Condition ($null -ne $personData -and $personData.Environment -eq "FORM_PERSON_DATA_HTTP_OUTPUT" -and $personData.Destination -eq "forms-person-data-http") "Person data require their own agreed output and destination."
                $personNames = @("attribute-created", "person-options", "person-data-empty", "person-data-saved", "person-data", "people", "conflict", "invalid-value", "attribute-updated", "attributes", "forbidden", "source-in-use", "attribute-value-removal-confirmation", "attribute-conflict")
                $expectedPersonData = @(@("es", "en") | ForEach-Object { $culture = $_; $personNames | ForEach-Object { "$_-$culture.json" } }) + @("attribute-created-english-original-en.json", "attributes-english-original-es.json")
                Assert-Condition (@(Compare-Object $expectedPersonData $personData.RequiredNames).Count -eq 0) "Person data must contain all thirty actual HTTP bodies, including the English original attribute and its own list."
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
                $expected = @(@("es", "en") | ForEach-Object { $culture = $_; @($baseNames + $replyNames + $privacyNames) | ForEach-Object { "$culture/$_.json" } }) + @($originalPresentationNames | ForEach-Object { "en/$_.json" }) + @($locationNames | ForEach-Object { "es/$_.json" })
                Assert-Condition ($forms.RequiredNames.Count -eq 80) "The canonical WhatsApp form inventory must contain all eighty captures, with the two of the location asked before the Flow (W1-A)."
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
                Assert-Condition (@(Get-ChildItem -LiteralPath $paths.Destination -File -Recurse).Count -eq 78) "Merge must add exactly twenty-six replies and six privacy bodies."
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
