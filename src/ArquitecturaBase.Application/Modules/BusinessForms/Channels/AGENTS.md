# BusinessForms/Channels: adaptadores de puertos neutrales

Implementa los puertos de `Application/Interfaces/Channels` que el núcleo y otros módulos consumen sin conocer BusinessForms. Cada adaptador valida el contexto y delega en [Services](../Services/AGENTS.md) o en un lector.

- `BusinessFormSource` → `IStructuredFormSource`: selección, preparación, prellenado, aclaración y sugerencias. Exige destinatario, cultura es/en y `formrecords.create`; lee la versión activa sellada, o la publicada de una sesión aceptada. `LockSelectionAsync` es el lock de definiciones.
- `BusinessFormRecordCaptureSink` → `IStructuredRecordSink`: delega en `IBusinessFormRecordCaptureService`; la UoW la abre `BusinessFormRecordRecorder`.
- `BusinessFormRecordSource` → `IStructuredRecordSource`: `formrecords.read` antes y después de leer por lote; proyecta con la cultura de cada versión, sin claves de almacenamiento.
- `BusinessFormEntryOriginSource` → `IStructuredRecordOriginSource`: el origen «App» (`channelId` `app`, quien cargó y cuándo, sin conversación ni dirección) de los registros `EnteredInApp`, leído por lote.
- `BusinessFormRetirementSource` → `IStructuredFormRetirementSource`: lock de definiciones e `IFormUnusedVersionReader`.
- `FormPrivateFileRetentionSource` → `IPrivateFileRetentionSource`: sólo claves que `FormPdfStorageKey.TryParse` reconoce, de runs GeneratePdf existentes, por lote, aun sin `StoredFile` confirmado.
- `FormPermissionCatalogSource` → `IPermissionCatalogSource`: `BusinessFormsPermissions.All` y textos de `Resources/Permissions`.
- `FormClientCapabilitySource` → `IClientCapabilitySource`: `forms`, y `form-files` sólo con almacenamiento privado.
- `IAssistantCapabilitySource`, versión 1: `BusinessFormAssistantCapabilitySource` (`forms.assistant-candidates`, `forms.propose-registration`, `forms.clarify-registration`), `BusinessFormSuggestionAssistantCapabilitySource` (`forms.suggestion-candidates`, `forms.propose-suggestion`), `BusinessFormCatalogAssistantCapabilitySource` (`forms.catalog-filters.metadata`, `forms.catalog-filters.propose`), `BusinessFormMissingRequestAssistantCapabilitySource` (`forms.missing-request.propose`) y `BusinessFormAuthoringAssistantCapabilitySource` (`forms.authoring.forms`, `forms.authoring.catalogs`, `forms.authoring.propose-form`, `forms.authoring.propose-catalog`).

Dos sinks los implementan servicios: `FormCatalogProposalService` (`IStructuredCatalogProposalSink`) y `FormMissingRequestService` (`IStructuredMissingFormRequestSink`).

`BusinessFormAuthoringProposalChannel` → `IStructuredAuthoringProposalChannel`: resume con `FormAuthoringSummaryPolicy` las propuestas de formulario, de catálogo Manual y de personas (H4), y crea delegando en `IFormDraftProposalService` o `IFormCatalogAssistantProposalService` (`CreateFromChannelAsync`), con la ruta `/formularios/{id}` o `/catalogos/{id}`.

## Al modificar

- No importés WhatsApp, IA ni otro módulo: sólo puertos y DTO de `Models/StructuredData` o `Models/Assistant`. Los puertos únicos se registran con `Replace`, idempotente en cualquier orden respecto del fallback del núcleo; las colecciones, con `TryAddEnumerable`.
- Un adaptador no guarda, no abre UoW ni llama a un escritor o transporte.
- Las capacidades sólo leen o proponen; nunca ejecutan ni capturan. Validan Id, versión, turno, Subject y cultura, y toman IDs, ordinal y revisión de la última lectura observada del mismo turno y destinatario. La aclaración no recibe pregunta ni etiquetas; los catálogos se proponen como Filters, nunca SQL.
- Presupuesto de 16 KiB con `FormAssistantCapabilityCodec.ResponseBytes` (envoltorio state/payload; `proposalId` sólo en propuestas). Los candidatos que no entran marcan la lectura parcial sin probar ausencia; la lectura de sugerencias va entera o `ReadUnavailable`; una propuesta excedida es `InvalidResponse`.
- Las descripciones al modelo piden sólo hechos de negocio explícitos, omitir campos sin hecho y el motivo aparte; no copiar instrucciones ni metacomentarios. El Subject del contexto no cambia.
- La autoría asistida lee nombres y campos de lo que existe, cada lectura con su permiso (`formtemplates.read` o `formcatalogs.read`), en 16 KiB y marcada parcial si no entra. Sus propuestas no exigen una lectura observada: el formulario se valida con `SaveFormDraftRequestValidator` y sólo usa catálogos activos que la persona puede leer; el catálogo, con `FormAuthoringProposalPolicy`. Devuelven la propuesta normalizada; un dato inválido vuelve como `rejected` con las rutas de sus argumentos para que el modelo la corrija, sin recortar ni elegirse. El JSON mal formado y los permisos de la capacidad siguen cortando el turno. Las cuatro y las de H4 se marcan `OfferedInHome`: Inicio las ofrece según los permisos.
- La capacidad de sugerencias aplica `FormSuggestionHistoryAccessGuard` antes de entregar origen y valores, salvo el modo neutral `Assistant` con Actor igual a Subject, que lee su propia captura con RecordsCreate.

## Referencias

- [Puertos neutrales](../../../Interfaces/Channels/AGENTS.md) y [módulos opcionales](../../../../../docs/architecture/backend.md#módulos-opcionales).
- [Catálogos propuestos](../../../../../docs/features/formularios.md#catálogos-propuestos--etapa-5-h4).
- [Prueba de composición](../../../../../tests/ArquitecturaBase.Application.UnitTests/Modules/BusinessForms/FormSourceCompositionTests.cs).
