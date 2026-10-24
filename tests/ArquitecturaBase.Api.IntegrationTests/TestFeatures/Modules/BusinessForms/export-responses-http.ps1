param([string]$Artifacts = ".artifacts/assistant-validation/stage5-final")
function Merge-WhatsAppFormCaptures {
    param([string]$RepliesRoot, [string]$PrivacyRoot, [string]$Destination)
    $replies = @("reply-all-states", "reply-completed", "reply-inbox-read", "reply-inbox-without-read", "reply-latest-page", "reply-multiple-choice", "reply-older-page", "reply-pending", "reply-rejected", "reply-reloaded", "reply-unrecognized", "reply-without-read")
    $privacy = @("thread-read-only", "thread-send-with-users-read", "thread-send-without-users-read")
    $files = @()
    foreach ($culture in @("es", "en")) {
        foreach ($group in @(@{ Root = $RepliesRoot; Names = $replies }, @{ Root = $PrivacyRoot; Names = $privacy })) {
            foreach ($name in $group.Names) {
                $relativePath = "$culture/$name.json"
                $source = Join-Path $group.Root $relativePath
                $target = Join-Path $Destination $relativePath
                if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing WhatsApp form capture: $relativePath." }
                if (Test-Path -LiteralPath $target) { throw "Duplicate WhatsApp form capture: $relativePath." }
                $files += @{ Source = $source; Target = $target }
            }
        }
    }
    foreach ($file in $files) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $file.Target) -Force | Out-Null
        Copy-Item -LiteralPath $file.Source -Destination $file.Target
    }
}

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
$whatsappFormExtraRoots = @{
    WHATSAPP_FORM_REPLIES_HTTP_OUTPUT = "whatsapp-form-replies"
    WHATSAPP_FORM_PRIVACY_HTTP_OUTPUT = "whatsapp-form-privacy"
}
$cohorts = @(
    @{ Name = "responses"; Environment = "FORM_RESPONSES_HTTP_OUTPUT"; Destination = "forms-responses-http";
        RequiredNames = @(
            "approval-sheet-en.json", "approval-sheet-es.json", "counts-es.json", "detail-es.json",
            "export-state-approved-d2.xlsx", "export-state-pendingapproval-d2.xlsx", "export-state-received-d2.xlsx", "export-state-rejected-d2.xlsx",
            "export.xlsx", "files-detail-es.json", "files-detail-without-chat-d2-es.json", "files-sheet-es.json",
            "filtered-es.json", "overview-combined-en.json", "overview-combined-es.json", "overview-deactivated-en.json",
            "overview-deactivated-es.json", "overview-decide-en.json", "overview-decide-es.json", "overview-pending-en.json",
            "overview-pending-es.json", "overview-read-en.json", "overview-read-es.json", "record-navigation-first-en.json",
            "record-navigation-first-es.json", "record-navigation-last-en.json", "record-navigation-last-es.json", "record-navigation-middle-en.json",
            "record-navigation-middle-es.json", "record-navigation-singleton-en.json", "record-navigation-singleton-es.json", "sheet-en.json",
            "sheet-es.json", "state-approved-d2-es.json", "state-pendingapproval-d2-es.json", "state-received-d2-es.json",
            "state-rejected-d2-es.json", "multi-sheet-es.json", "multi-detail-es.json", "multi-counts-es.json",
            "multi-sheet-en.json", "multi-detail-en.json", "multi-counts-en.json", "multi-export-es.xlsx",
            "multi-export-en.xlsx", "extra-fields-sheet-es.json", "extra-fields-detail-es.json", "extra-fields-legacy-es.json", "extra-fields-export-es.xlsx", "extra-fields-visible-export-es.xlsx",
            "extra-fields-sheet-en.json", "extra-fields-detail-en.json", "extra-fields-legacy-en.json", "extra-fields-export-en.xlsx", "extra-fields-visible-export-en.xlsx");
        Sources = @(($sourceRoot + "BusinessForms/FormCatalogFieldsReadHttpTests.cs"), ($sourceRoot + "BusinessForms/FormMultipleChoiceReadHttpTests.cs"), ($sourceRoot + "BusinessForms/FormResponseHttpTests.cs"), ($sourceRoot + "BusinessForms/FormOverviewHttpTests.cs"), ($sourceRoot + "BusinessForms/FormRecordNavigationHttpTests.cs"), ($captureRoot + "Modules/BusinessForms/FormPublishedVersionFixture.cs"), ($sourceRoot + "BusinessForms/FormResponseApprovalHttpTests.cs"), ($sourceRoot + "BusinessForms/FormResponseStateHttpTests.cs"), ($sourceRoot + "WhatsApp/WhatsAppFormFilesIntegrationTests.cs"), ($captureRoot + "ResponseContractCapture.cs")) },
    @{ Name = "rules"; Environment = "FORM_RULES_HTTP_OUTPUT"; Destination = "forms-rules-http";
        Sources = @(($sourceRoot + "BusinessForms/FormRuleHttpTests.cs"), ($captureRoot + "Modules/BusinessForms/FormRuleContractCapture.cs")) },
    @{ Name = "workflow"; Environment = "FORM_WORKFLOW_HTTP_OUTPUT"; Destination = "forms-workflow-http";
        Sources = @(($sourceRoot + "BusinessForms/FormWorkflowHttpTests.cs"), ($sourceRoot + "BusinessForms/FormDetailMomentsHttpTests.cs"), ($sourceRoot + "BusinessForms/FormApprovalHttpTests.cs"), ($sourceRoot + "BusinessForms/FormPdfHttpTests.cs"), ($sourceRoot + "BusinessForms/FormWorkflowEndToEndHttpTests.cs"), ($sourceRoot + "BusinessForms/FormResponseHttpTests.cs"), ($captureRoot + "Modules/BusinessForms/FormPublishedVersionFixture.cs"), ($sourceRoot + "BusinessForms/FormRecordCaptureIntegrationTests.cs"), ($sourceRoot + "BusinessForms/FormActionRecoveryIntegrationTests.cs"), ($sourceRoot + "BusinessForms/FormDraftDurabilityTests.cs"), ($captureRoot + "Modules/BusinessForms/FormPdfRendererFixture.cs"), ($captureRoot + "Modules/BusinessForms/CapturingFormActionChannel.cs"), ($captureRoot + "Modules/BusinessForms/FormWorkflowContractCapture.cs")) },
    @{ Name = "approvals"; Environment = "FORM_APPROVALS_HTTP_OUTPUT"; Destination = "forms-approvals-http";
        Sources = @(($sourceRoot + "BusinessForms/FormApprovalHttpTests.cs"), ($sourceRoot + "BusinessForms/FormApprovalContinuationHttpTests.cs"), ($sourceRoot + "BusinessForms/FormResponseHttpTests.cs"), ($captureRoot + "Modules/BusinessForms/FormApprovalContinuationFixture.cs"), ($captureRoot + "Modules/BusinessForms/FormPublishedVersionFixture.cs"), ($captureRoot + "Modules/BusinessForms/FormApprovalContractCapture.cs")) },
    @{ Name = "pdf"; Environment = "FORM_PDF_HTTP_OUTPUT"; Destination = "forms-pdf-http";
        RequiredNames = @(
            "default-es.http.json", "default-es.json", "design-en.http.json", "design-en.json",
            "design-es.http.json", "design-es.json", "document-es.http.json", "document-es.pdf",
            "document-mismatch-en.http.json", "document-mismatch-en.json", "e2e-approved-notice-fallback-es.http.json", "e2e-approved-notice-fallback-es.pdf",
            "e2e-catalog-original-item-en.http.json", "e2e-catalog-original-item-en.pdf", "e2e-english-en.http.json", "e2e-english-en.pdf",
            "e2e-footer-fallback-es.http.json", "e2e-footer-fallback-es.pdf", "issued-es.http.json", "issued-es.json",
            "preview-en-fallback.http.json", "preview-en-fallback.pdf", "preview-es.http.json", "preview-es.pdf",
            "revision-conflict-es.http.json", "revision-conflict-es.json", "saved-es.http.json", "saved-es.json",
            "multi-preview-es.pdf", "multi-preview-es.http.json", "multi-preview-en.pdf", "multi-preview-en.http.json");
        Sources = @(($sourceRoot + "BusinessForms/FormMultipleChoiceReadHttpTests.cs"), ($sourceRoot + "BusinessForms/FormPdfHttpTests.cs"), ($sourceRoot + "BusinessForms/FormWorkflowEndToEndHttpTests.cs"), ($sourceRoot + "BusinessForms/FormResponseHttpTests.cs"), ($captureRoot + "Modules/BusinessForms/FormPublishedVersionFixture.cs"), ($sourceRoot + "BusinessForms/FormRecordCaptureIntegrationTests.cs"), ($sourceRoot + "BusinessForms/FormActionRecoveryIntegrationTests.cs"), ($sourceRoot + "BusinessForms/FormDraftDurabilityTests.cs"), ($captureRoot + "Modules/BusinessForms/FormPdfRendererFixture.cs"), ($captureRoot + "Modules/BusinessForms/CapturingFormActionChannel.cs"), ($captureRoot + "Modules/BusinessForms/FormPdfContractCapture.cs")) },
    @{ Name = "whatsapp-actions"; Environment = "WHATSAPP_FORM_ACTIONS_HTTP_OUTPUT"; Destination = "whatsapp-form-actions-http";
        Sources = @(($sourceRoot + "WhatsApp/WhatsAppFormActionHttpTests.cs"), ($captureRoot + "Modules/WhatsApp/WhatsAppFormActionContractCapture.cs"), ($captureRoot + "Modules/WhatsApp/WhatsAppFormActionScenario.cs")) },
    @{ Name = "whatsapp-forms"; Environment = "WHATSAPP_FORMS_HTTP_CONTRACT_OUTPUT"; Destination = "whatsapp-forms-http";
        RequiredNames = @(
            "es/choices-forbidden.json", "es/choices.json", "es/preparation.json", "es/manifest.json", "es/outgoing-accepted.json",
            "es/outgoing-failed.json", "es/outgoing-pending.json", "es/outgoing-uncertain.json", "es/send-before-failed.json",
            "es/send-before-uncertain.json", "es/send-context-changed.json", "es/send-pending.json", "es/send-version-unavailable.json",
            "es/thread-accepted.json", "es/thread-before-send.json", "es/thread-failed.json", "es/thread-pending.json",
            "es/thread-reloaded.json", "es/thread-uncertain.json", "es/reply-all-states.json", "es/reply-completed.json",
            "es/reply-inbox-read.json", "es/reply-inbox-without-read.json", "es/reply-latest-page.json", "es/reply-multiple-choice.json", "es/reply-older-page.json",
            "es/reply-pending.json", "es/reply-rejected.json", "es/reply-reloaded.json", "es/reply-unrecognized.json",
            "es/reply-without-read.json", "es/thread-read-only.json", "es/thread-send-with-users-read.json", "es/thread-send-without-users-read.json",
            "en/choices-forbidden.json", "en/choices.json", "en/preparation.json", "en/manifest.json", "en/outgoing-accepted.json",
            "en/outgoing-failed.json", "en/outgoing-pending.json", "en/outgoing-uncertain.json", "en/send-before-failed.json",
            "en/send-before-uncertain.json", "en/send-context-changed.json", "en/send-pending.json", "en/send-version-unavailable.json",
            "en/thread-accepted.json", "en/thread-before-send.json", "en/thread-failed.json", "en/thread-pending.json",
            "en/thread-reloaded.json", "en/thread-uncertain.json", "en/reply-all-states.json", "en/reply-completed.json",
            "en/reply-inbox-read.json", "en/reply-inbox-without-read.json", "en/reply-latest-page.json", "en/reply-multiple-choice.json", "en/reply-older-page.json",
            "en/reply-pending.json", "en/reply-rejected.json", "en/reply-reloaded.json", "en/reply-unrecognized.json",
            "en/reply-without-read.json", "en/thread-read-only.json", "en/thread-send-with-users-read.json", "en/thread-send-without-users-read.json",
            "en/original-only-choices.json", "en/original-only-preparation.json", "en/original-only-receipt.json", "en/original-only-history.json",
            "en/empty-secondary-choices.json", "en/empty-secondary-preparation.json", "en/empty-secondary-receipt.json", "en/empty-secondary-history.json");
        Sources = @(($sourceRoot + "WhatsApp/WhatsAppFormHttpContractTests.cs"), ($sourceRoot + "WhatsApp/WhatsAppFormSendingTests.cs"),
            ($sourceRoot + "WhatsApp/WhatsAppFormReplyAppPreviewTests.cs"), ($sourceRoot + "WhatsApp/WhatsAppFormPrivacyTests.cs"),
            ($sourceRoot + "WhatsApp/WhatsAppMultipleChoiceCompositionTests.cs"),
            ($captureRoot + "Modules/WhatsApp/CapturingFlowProvider.cs"), ($captureRoot + "Modules/WhatsApp/CapturingStructuredFormSource.cs"),
            ($captureRoot + "Modules/WhatsApp/ControlledWhatsAppSendClient.cs")) },
    @{ Name = "suggestions"; Environment = "FORM_SUGGESTIONS_HTTP_OUTPUT"; Destination = "forms-suggestions-http";
        Sources = @(($sourceRoot + "BusinessForms/FormSuggestionHttpTests.cs"), ($captureRoot + "Modules/BusinessForms/FormSuggestionContractCapture.cs")) },
    @{ Name = "lifecycle"; Environment = "FORM_LIFECYCLE_HTTP_OUTPUT"; Destination = "forms-lifecycle-http";
        RequiredNames = @("es/preview.json", "es/deactivated.json", "es/detail-deactivated.json", "es/list-deactivated.json", "es/facets.json", "es/options-unavailable.json", "es/detail-reactivation-pending.json", "es/detail-reactivated.json", "es/preview-affected.json", "es/options-deleted.json", "es/delete-responses-conflict.json", "es/status-pending.json", "es/status-uncertain.json", "es/status-needsattention.json", "es/status-deprecated.json",
            "en/preview.json", "en/deactivated.json", "en/detail-deactivated.json", "en/list-deactivated.json", "en/facets.json", "en/options-unavailable.json", "en/detail-reactivation-pending.json", "en/detail-reactivated.json", "en/preview-affected.json", "en/options-deleted.json", "en/delete-responses-conflict.json", "en/status-pending.json", "en/status-uncertain.json", "en/status-needsattention.json", "en/status-deprecated.json");
        Sources = @(($sourceRoot + "BusinessForms/FormDeactivationHttpTests.cs"), ($sourceRoot + "BusinessForms/FormDeletionHttpTests.cs"), ($sourceRoot + "BusinessForms/FormRetirementStatusHttpTests.cs"), ($captureRoot + "Modules/BusinessForms/FormLifecycleContractCapture.cs"), ($captureRoot + "Modules/BusinessForms/ControlledFormChannelStateSource.cs"), ($sourceRoot + "BusinessForms/FormDraftEditingTests.cs"), ($sourceRoot + "BusinessForms/FormDraftDurabilityTests.cs"), ($sourceRoot + "BusinessForms/FormSuggestionHttpTests.cs"), ($sourceRoot + "BusinessForms/FormPublicationAcceptanceTests.cs"), ($sourceRoot + "BusinessForms/FormPublishedSelectionTests.cs")) },
    @{ Name = "catalog-proposals"; Environment = "FORM_CATALOG_PROPOSAL_HTTP_OUTPUT"; Destination = "forms-catalog-proposals-http";
        Sources = @(($sourceRoot + "BusinessForms/FormCatalogProposalHttpTests.cs"), ($captureRoot + "Modules/BusinessForms/FormCatalogProposalScenario.cs"), ($captureRoot + "Modules/BusinessForms/FormCatalogProposalContractCapture.cs")) },
    @{ Name = "suggestion-details"; Environment = "FORM_SUGGESTION_DETAILS_HTTP_OUTPUT"; Destination = "forms-suggestion-detail-http";
        Sources = @(($sourceRoot + "BusinessForms/FormSuggestionCaptureIntegrationTests.cs"), ($captureRoot + "Modules/BusinessForms/FormSuggestionDetailContractCapture.cs")) },
    @{ Name = "whatsapp-audio"; Environment = "WHATSAPP_AUDIO_HTTP_OUTPUT"; Destination = "whatsapp-audio-http";
        Sources = @(($sourceRoot + "WhatsApp/WhatsAppChatTests.Audio.cs"), ($captureRoot + "Modules/WhatsApp/WhatsAppAudioContractCapture.cs")) },
    @{ Name = "missing-requests"; Environment = "FORM_MISSING_REQUEST_HTTP_OUTPUT"; Destination = "forms-missing-requests-http";
        Sources = @(($sourceRoot + "BusinessForms/FormMissingRequestHttpTests.cs"), ($captureRoot + "Modules/BusinessForms/FormMissingRequestScenario.cs"), ($captureRoot + "Modules/BusinessForms/FormMissingRequestContractCapture.cs")) },
    @{ Name = "whatsapp-assistant"; Environment = "WHATSAPP_ASSISTANT_HTTP_OUTPUT"; Destination = "whatsapp-assistant-http";
        Sources = @(($sourceRoot + "WhatsApp/WhatsAppAssistantHttpTests.cs"), ($sourceRoot + "WhatsApp/WhatsAppAssistantHttpTests.Suggestions.cs"), ($captureRoot + "Modules/WhatsApp/WhatsAppAssistantScenario.cs"), ($captureRoot + "Modules/WhatsApp/WhatsAppAssistantContractCapture.cs"), ($captureRoot + "Modules/WhatsApp/WhatsAppAssistantOperationsDouble.cs"), ($captureRoot + "Modules/WhatsApp/WhatsAppAssistantCaptureFixture.cs"), ($captureRoot + "Modules/WhatsApp/CapturingStructuredRecordPort.cs")) },
    @{ Name = "forms"; Environment = "FORMS_HTTP_CONTRACT_OUTPUT"; Destination = "forms-http"; IncludeDirectories = @("published", "failed", "budget-http", "languages", "multiple-choice", "recipient-filters");
        RequiredNames = @("published/accepted-status.json", "published/accepted.json", "published/check-next-unavailable.json", "published/check-unavailable.json", "published/created.json", "published/detail-en.json", "published/detail-es.json", "published/draft-detail.json", "published/draft-list.json", "published/list-en.json", "published/list-es.json", "published/status-en.json", "published/status-es.json", "failed/accepted-status.json", "failed/accepted.json", "failed/check-next-unavailable.json", "failed/check-unavailable.json", "failed/created.json", "failed/detail-en.json", "failed/detail-es.json", "failed/draft-detail.json", "failed/draft-list.json", "failed/list-en.json", "failed/list-es.json", "failed/status-en.json", "failed/status-es.json", "budget-http/es.json", "budget-http/en.json",
            "languages/es/v1-check.json", "languages/es/v1-status.json", "languages/es/v1-detail.json",
            "languages/es/v2-check.json", "languages/es/v2-status.json", "languages/es/v2-detail.json",
            "languages/es/v3-check.json", "languages/es/v3-status.json", "languages/es/v3-detail.json",
            "languages/en/v1-check.json", "languages/en/v1-status.json", "languages/en/v1-detail.json",
            "languages/en/v2-check.json", "languages/en/v2-status.json", "languages/en/v2-detail.json",
            "languages/en/v3-check.json", "languages/en/v3-status.json", "languages/en/v3-detail.json",
            "multiple-choice/draft.json", "multiple-choice/catalog-too-large.json",
            "recipient-filters/source-draft-es.json", "recipient-filters/recipient-samples-es.json", "recipient-filters/recipient-ready-es.json", "recipient-filters/recipient-missing-es.json", "recipient-filters/recipient-unavailable-es.json", "recipient-filters/source-check-warnings-es.json",
            "recipient-filters/source-draft-en.json", "recipient-filters/recipient-samples-en.json", "recipient-filters/recipient-ready-en.json", "recipient-filters/recipient-missing-en.json", "recipient-filters/recipient-unavailable-en.json", "recipient-filters/source-check-warnings-en.json");
        Sources = @(($sourceRoot + "BusinessForms/FormRecipientFilterIntegrationTests.cs"), ($captureRoot + "Modules/BusinessForms/FormRecipientScenario.cs"), ($sourceRoot + "BusinessForms/FormMultipleChoiceAuthoringHttpTests.cs"), ($sourceRoot + "BusinessForms/FormDraftEditingTests.cs"), ($sourceRoot + "BusinessForms/FormDraftDurabilityTests.cs"), ($sourceRoot + "BusinessForms/FormPublicationAcceptanceTests.cs"), ($captureRoot + "Modules/BusinessForms/ReadyStructuredFormChannel.cs"), ($sourceRoot + "BusinessForms/FormHttpContractFixturesTests.cs"), ($sourceRoot + "BusinessForms/FormPublicationCheckTests.cs"), ($sourceRoot + "BusinessForms/FormPublicationLanguageTests.cs")) },
    @{ Name = "catalog-imports"; Environment = "FORM_CATALOG_IMPORT_HTTP_OUTPUT"; Destination = "catalogs-http/imports";
        RequiredNames = @("es/replace-preview.json", "es/append-preview.json", "es/already-applied.json", "es/empty-preview.json", "es/applied.json", "es/conflict.json", "es/confirmation-required.json", "es/round-trip-preview.json", "es/export.xlsx", "es/export.http.json", "es/template.xlsx", "es/template.http.json",
            "en/replace-preview.json", "en/append-preview.json", "en/already-applied.json", "en/empty-preview.json", "en/applied.json", "en/conflict.json", "en/confirmation-required.json", "en/round-trip-preview.json", "en/export.xlsx", "en/export.http.json", "en/template.xlsx", "en/template.http.json",
            "es/extra-source-preview.json", "es/extra-source-applied.json", "es/extra-source-unchanged.json",
            "en/extra-source-preview.json", "en/extra-source-applied.json", "en/extra-source-unchanged.json");
        Sources = @(($sourceRoot + "BusinessForms/FormCatalogImportFieldsHttpTests.cs"), ($sourceRoot + "BusinessForms/FormCatalogImportHttpTests.cs"), ($sourceRoot + "BusinessForms/FormCatalogEndpointsTests.cs"), ($captureRoot + "Modules/BusinessForms/FormCatalogImportContractCapture.cs"),
            "src/ArquitecturaBase.Application/Modules/BusinessForms/Models/FormCatalogImportModels.cs", "src/ArquitecturaBase.Application/Modules/BusinessForms/Services/FormCatalogImportTemplate.cs",
            "src/ArquitecturaBase.Application/Modules/BusinessForms/Resources/Catalogs.resx", "src/ArquitecturaBase.Application/Modules/BusinessForms/Resources/Catalogs.en.resx") },
    @{ Name = "manual-catalogs"; Environment = "FORMS_CATALOG_HTTP_CONTRACT_OUTPUT"; Destination = "catalogs-http/manual-current"; SourceDirectory = "manual-bilingual";
        RequiredNames = @("created.json", "updated.json", "detail.json", "items.json", "list-es.json", "list-en.json", "conflict-es.json", "conflict-en.json", "create-request.request.json", "create-duplicate-positions-request.request.json", "missing-revision-request.request.json", "inactive-request.request.json", "conflict-request.request.json", "queued-moves-request.request.json", "update-request.request.json",
            "typed-text-created.json", "typed-text-items.json", "typed-integer-created.json", "typed-integer-items.json", "typed-decimal-created.json", "typed-decimal-items.json", "typed-date-created.json", "typed-date-items.json", "typed-invalid-value-es.json", "typed-invalid-value-en.json", "typed-required-value.json", "typed-type-change-rejected.json",
            "fields-create.request.json", "fields-update.request.json", "fields-created.json", "fields-required-error.json", "fields-values-saved.json", "fields-conversion-error.json", "fields-unique-error.json", "fields-detail.json", "fields-items.json", "fields-removal-confirmation.json", "fields-removed.json", "fields-presentation.json", "fields-source-items.json", "fields-source-in-use.json", "description-optional-created.json");
        Sources = @(($sourceRoot + "BusinessForms/FormCatalogEndpointsTests.cs"), ($sourceRoot + "BusinessForms/FormCatalogEndpointsTests.Fields.cs"), ($sourceRoot + "BusinessForms/FormCatalogValueHttpTests.cs")) },
    @{ Name = "person-data"; Environment = "FORM_PERSON_DATA_HTTP_OUTPUT"; Destination = "forms-person-data-http";
        RequiredNames = @("attribute-created-es.json", "person-options-es.json", "person-data-empty-es.json", "person-data-saved-es.json", "person-data-es.json", "people-es.json", "conflict-es.json", "invalid-value-es.json", "attribute-updated-es.json", "attributes-es.json", "forbidden-es.json", "source-in-use-es.json", "attribute-value-removal-confirmation-es.json",
            "attribute-created-en.json", "person-options-en.json", "person-data-empty-en.json", "person-data-saved-en.json", "person-data-en.json", "people-en.json", "conflict-en.json", "invalid-value-en.json", "attribute-updated-en.json", "attributes-en.json", "forbidden-en.json", "source-in-use-en.json", "attribute-value-removal-confirmation-en.json");
        Sources = @(($sourceRoot + "BusinessForms/FormPersonDataHttpTests.cs"), ($captureRoot + "Modules/BusinessForms/FormPersonDataContractCapture.cs")) },
    @{ Name = "catalog-filters"; Environment = "FORMS_HTTP_CONTRACT_OUTPUT"; Destination = "catalogs-http/filters"; SourceDirectory = "catalog-filters";
        RequiredNames = @("options.json", "preview-request.json", "preview.json", "create-request.json", "created.json", "detail.json", "update-request.json", "updated.json");
        Sources = @(($sourceRoot + "BusinessForms/FormCatalogFiltersEndpointsTests.cs")) },
    @{ Name = "catalog-source"; Environment = "FORMS_HTTP_CONTRACT_OUTPUT"; Destination = "catalogs-http/k6-source"; SourceDirectory = "k6-source";
        RequiredNames = @("active-catalog-created.json", "active-catalog-list-usage.json", "catalog-choice-check-invalid.json", "catalog-choice-check-valid.json", "catalog-choice-create-receipt.json", "catalog-choice-created.json", "catalog-choice-detail.json", "catalog-choice-invalid-created.json", "catalog-choice-reloaded.json", "catalog-choice-update-receipt.json", "catalog-choice-updated.json");
        Sources = @(($sourceRoot + "BusinessForms/FormPublicationCheckTests.cs")) },
    @{ Name = "form-files"; Environment = "FORM_FILES_HTTP_OUTPUT"; Destination = "form-files-http";
        RequiredNames = @("draft-created.json", "reply-completed.json", "reply-reloaded.json", "reply-downloading.json", "reply-without-read.json");
        Sources = @(($sourceRoot + "WhatsApp/WhatsAppFormFilesIntegrationTests.cs")) },
    @{ Name = "whatsapp-catalogs"; Environment = "WHATSAPP_CATALOG_HTTP_OUTPUT"; Destination = "whatsapp-catalogs-http";
        RequiredNames = @("es/choices.json", "es/preparation.json", "es/reply-completed.json", "es/reply-reloaded.json", "es/reply-without-read.json", "es/reply-rejected.json", "es/reply-pending.json", "es/reply-unrecognized.json", "es/reply-inbox-read.json", "es/reply-inbox-without-read.json", "es/reply-latest-page.json", "es/reply-older-page.json", "es/reply-all-states.json", "en/choices.json", "en/preparation.json", "en/reply-completed.json", "en/reply-reloaded.json", "en/reply-without-read.json", "en/reply-rejected.json", "en/reply-pending.json", "en/reply-unrecognized.json", "en/reply-inbox-read.json", "en/reply-inbox-without-read.json", "en/reply-latest-page.json", "en/reply-older-page.json", "en/reply-all-states.json", "es/preparation-recipient-data-missing.json", "en/preparation-recipient-data-missing.json");
        Sources = @(($sourceRoot + "WhatsApp/WhatsAppFormReplyAppPreviewTests.cs"), ($sourceRoot + "WhatsApp/WhatsAppCatalogRecipientSendingTests.cs"),
            ($sourceRoot + "WhatsApp/WhatsAppFormSendingTests.cs"), ($sourceRoot + "WhatsApp/WhatsAppFormHttpContractTests.cs"),
            ($captureRoot + "Modules/WhatsApp/CapturingFlowProvider.cs"), ($captureRoot + "Modules/WhatsApp/CapturingWhatsAppCloudClient.cs"),
            ($captureRoot + "IStructuredFormPublicationDriver.cs"), ($captureRoot + "Modules/BusinessForms/StructuredFormPublicationDriver.cs")) }
)
$previousOutputs = @{}
$captureRoots = @{}
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
try {
    foreach ($cohort in $cohorts) {
        if (-not $captureRoots.ContainsKey($cohort.Environment)) {
            $previousOutputs[$cohort.Environment] = [Environment]::GetEnvironmentVariable($cohort.Environment)
            $captureRoots[$cohort.Environment] = Join-Path (Join-Path $runRoot "http") $cohort.Name
            New-Item -ItemType Directory -Path $captureRoots[$cohort.Environment] -Force | Out-Null
            [Environment]::SetEnvironmentVariable($cohort.Environment, $captureRoots[$cohort.Environment])
        }
        $cohort.Output = if ($cohort.SourceDirectory) {
            if ($cohort.SourceDirectory -notmatch "^[a-z0-9-]+$") { throw "Invalid capture source directory." }
            Join-Path $captureRoots[$cohort.Environment] $cohort.SourceDirectory
        } else { $captureRoots[$cohort.Environment] }
        New-Item -ItemType Directory -Path $cohort.Output -Force | Out-Null
    }
    foreach ($name in $whatsappFormExtraRoots.Keys) {
        $previousOutputs[$name] = [Environment]::GetEnvironmentVariable($name)
        $captureRoots[$name] = Join-Path (Join-Path $runRoot "http") $whatsappFormExtraRoots[$name]
        New-Item -ItemType Directory -Path $captureRoots[$name] -Force | Out-Null
        [Environment]::SetEnvironmentVariable($name, $captureRoots[$name])
    }
    $measurementName = "WHATSAPP_ROLE_QUEUE_MEASUREMENT_OUTPUT"
    $previousOutputs[$measurementName] = [Environment]::GetEnvironmentVariable($measurementName)
    $measurementPath = Join-Path $runRoot "role-queue-measurement.json"
    [Environment]::SetEnvironmentVariable($measurementName, $measurementPath)
    $technicalPdfName = "FORM_PDF_RENDERER_OUTPUT"
    $previousOutputs[$technicalPdfName] = [Environment]::GetEnvironmentVariable($technicalPdfName)
    [Environment]::SetEnvironmentVariable($technicalPdfName, (Join-Path $runRoot "pdf-technical"))
    $catalogPerformanceName = "FORM_CATALOG_FIELDS_PERFORMANCE_OUTPUT"
    $previousOutputs[$catalogPerformanceName] = [Environment]::GetEnvironmentVariable($catalogPerformanceName)
    [Environment]::SetEnvironmentVariable($catalogPerformanceName, (Join-Path $runRoot "catalog-fields-performance"))
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

        if (-not (Test-Path -LiteralPath $measurementPath -PathType Leaf)) {
            throw "The role/manual queue measurement was not produced."
        }

        $whatsappForms = @($cohorts | Where-Object { $_.Name -eq "whatsapp-forms" })[0]
        Merge-WhatsAppFormCaptures -RepliesRoot $captureRoots["WHATSAPP_FORM_REPLIES_HTTP_OUTPUT"] -PrivacyRoot $captureRoots["WHATSAPP_FORM_PRIVACY_HTTP_OUTPUT"] -Destination $whatsappForms.Output

        # Validate every cohort before replacing any frontend fixture.
        foreach ($cohort in $cohorts) {
            $cohort.Files = @(Get-ChildItem -LiteralPath $cohort.Output -File -Recurse | Where-Object { $_.Name -ne "explain.json" } | Sort-Object FullName)
            if ($cohort.IncludeDirectories) {
                $cohort.Files = @($cohort.Files | Where-Object {
                    $relative = [IO.Path]::GetRelativePath($cohort.Output, $_.FullName).Replace([IO.Path]::DirectorySeparatorChar, [char]'/')
                    ($relative.Split('/')[0]) -in $cohort.IncludeDirectories
                })
            }
            if ($cohort.RequiredNames) {
                $actualNames = @($cohort.Files | ForEach-Object { [IO.Path]::GetRelativePath($cohort.Output, $_.FullName).Replace([IO.Path]::DirectorySeparatorChar, [char]'/') })
                if (@(Compare-Object -ReferenceObject $cohort.RequiredNames -DifferenceObject $actualNames).Count -ne 0) {
                    throw "HTTP capture inventory differs for $($cohort.Name)."
                }
            }
            if ($cohort.Files.Count -eq 0) { throw "No HTTP captures were produced for $($cohort.Name)." }
            foreach ($source in $cohort.SourceHashes) {
                if ((Get-FileHash -LiteralPath (Join-Path $backendRoot $source.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $source.sha256) {
                    throw "Producer source changed during the test run: $($source.path)."
                }
            }
        }
        $generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        foreach ($cohort in $cohorts) {
            if ([IO.Path]::IsPathRooted($cohort.Destination) -or $cohort.Destination -notmatch "^[a-z0-9-]+(/[a-z0-9-]+)*$") {
                throw "Invalid fixture destination for cohort $($cohort.Name)."
            }
            $targetRoot = [IO.Path]::GetFullPath((Join-Path $fixtureRoot $cohort.Destination))
            if (-not $targetRoot.StartsWith($fixturePrefix, [StringComparison]::OrdinalIgnoreCase) -or
                [IO.Path]::GetRelativePath($fixtureRoot, $targetRoot).Replace([IO.Path]::DirectorySeparatorChar, [char]'/') -cne $cohort.Destination) {
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
