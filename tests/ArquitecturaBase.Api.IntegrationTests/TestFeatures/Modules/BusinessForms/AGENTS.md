# BusinessForms: fallas exclusivas de prueba

El protector que falla observa rollback ante error técnico, nunca se registra en producción. Leer las reglas de TestFeatures y las de integración del módulo.

El canal listo ficticio permite aceptar sin transporte; nunca se registra en producción y sus efectos no previstos fallan. La composición desactiva workers automáticos para que los escenarios de publicación controlen fases y reloj; las pruebas de transporte usan el proveedor ficticio propio de WhatsApp y los servicios productivos.

`StructuredFormPublicationDriver` adapta el worker real al puerto neutral del arnés para la prueba HTTP de composición con los catálogos reales, sin dependencias entre módulos. Sólo se registra en FormsTestModule.

`export-responses-http.ps1` fija los destinos de las diecinueve cohortes vigentes antes de una única corrida build/suite completa. Agrupa los productores que comparten FORMS_HTTP_CONTRACT_OUTPUT sin reemplazar su carpeta de captura. La cohorte forms-http exige 46 cuerpos: los 28 anteriores y dieciocho de languages para tres comprobaciones/estados/detalles por original es/en; incluye el productor de idioma en sus fuentes SHA256. Usa una carpeta local nueva por corrida; valida todas las cohortes antes de copiar cuerpos HTTP, XLSX y PDF intactos al front, elimina sólo archivos viejos de cada destino verificado y emite procedencia/SHA256 por cohorte y por fuente. Las seis cohortes de formularios, catálogos y archivos heredadas se regeneran con los DTO actuales; catalogs-http/manual histórico permanece como fixture de rechazo y no se atribuye a esta corrida. El cierre de backend exportó las diecinueve cohortes, incluidos los 46 cuerpos de forms-http; las futuras regeneraciones conservan la corrida conjunta al terminar el desarrollo. `FormResponseCommandRecorder` captura el SQL ejecutado y clona sus parámetros Npgsql, sin reconstruir la consulta. El EXPLAIN queda en artefactos locales, fuera de Git. No exportar datos de la base de desarrollo como fixtures.

`FormPdfContractCapture` guarda cuerpos HTTP JSON o PDF intactos sólo con FORM_PDF_HTTP_OUTPUT. La emisión real por aprobación también aporta pdf-completed-es a la cohorte Workflow; el exportador incluye FormPdfHttpTests.cs en la procedencia de ambas cohortes.

El capturador PDF agrega companions .http.json con status, Content-Type, Content-Language y Content-Disposition reales; no construye headers para el consumidor. La procedencia de cada cohorte incluye SHA256 de tests y helpers capturadores, además de cada archivo exportado.

Review17 agrega la cohorte manual whatsapp-forms-http por es/en al mismo cierre, con cuerpos202/200 y estados durables intactos. El exportador conserva rutas relativas y hashes al copiar capturas anidadas; no edita JSON ni ejecuta otra suite para regenerarlas. Conversation identifica recibos manuales y FormAction conserva los avisos automáticos, sin atribuirles un operador.

El productor E2E de workflow/PDF reutiliza PhotoContent del fixture JPEG, SeedSheetAsync y los helpers de captura/scopes/actor existentes. La procedencia de ambas cohortes incluye esos productores y CapturingFormActionChannel. PhotoContent devuelve una copia; no agrega archivos privados reales ni modifica capturas crudas.

FormSuggestionDetailContractCapture exporta dos cuerpos GET íntegros (detail-es y detail-en) bajo FORM_SUGGESTION_DETAILS_HTTP_OUTPUT, en la cohorte forms-suggestion-detail-http. La procedencia incluye FormSuggestionCaptureIntegrationTests y el capturador; se incorpora al exportador único del cierre sin fabricar ni editar JSON.

FormMissingRequestScenario recorre Read combinado, Proposal real y sink público con cuentas ficticias sin RecordsCreate; verifica agrupación/recibo/revisión contra PostgreSQL sin invocar un proveedor. FormMissingRequestContractCapture exporta seis cuerpos íntegros bajo FORM_MISSING_REQUEST_HTTP_OUTPUT para forms-missing-requests-http. Sólo se ejecutan e incorporan al exportador único al cierre de la etapa.

FormAssistantMigrationFixture escribe sólo las columnas del contrato anterior mediante SQL parametrizado. Usa los modelos canónicos para las preguntas/valores y verifica el contenido conservado por consultas explícitas antes y después de cada migración; nunca cambia la base AppHost ni fabrica atribución histórica.

El exportador captura respuestas y privacidad de WhatsApp en dos raíces adicionales separadas. Une únicamente las 22 respuestas y los seis hilos de privacidad esperados a whatsapp-forms-http, excluye choices/preparation del productor de respuestas y conserva choices/preparation del manual y los ocho cuerpos manuales de original-only/empty-secondary (lista, preparación, recibo e historial para una cuenta inglesa); exige las 74 capturas canónicas e incluye el SHA de los tres productores. test-export-responses-http.ps1 verifica configuración, bytes, faltantes y colisiones con archivos sintéticos locales, sin compilar ni usar Docker o fixtures del front.

FormMissingRequestScenario usa una base propia por defecto, borrada al disponer el host; sólo el preview opta explícitamente por la base compartida de su UI. El sufijo del tema contiene sólo letras para no activar la protección determinística de datos personales. La reapertura de descartados propone de nuevo el nombre general: ya no fabrica un ExistingTopicId ausente de la lectura.

FormLifecycleContractCapture guarda treinta cuerpos HTTP JSON íntegros en es/en mediante FORM_LIFECYCLE_HTTP_OUTPUT para la cohorte forms-lifecycle-http. FormDeactivationHttpTests aporta preview/POST, detalles de baja y reactivación, listado, facetas y opciones de destinos no disponibles; FormDeletionHttpTests agrega opciones con destino eliminado y conflictos por respuestas reales; FormRetirementStatusHttpTests aporta Pending, Uncertain, NeedsAttention y Deprecated con fecha sólo al confirmar. No reconstruye JSON ni cambia datos de la base de desarrollo. La ejecución PostgreSQL y la exportación quedan para el cierre integrado.

ControlledFormChannelStateSource aporta sólo evidencia local del puerto neutral por IDs propios para los escenarios HTTP de eliminación. No simula persistencia, crea sesiones ni llama a Meta; la lectura real de sesiones/recibos pertenece a las pruebas del módulo dueño.

La cohorte forms-responses-http incluye el productor FormRecordNavigationHttpTests y ocho cuerpos íntegros record-navigation-{middle,first,last,singleton}-{es,en}; no se reconstruyen JSON ni se exportan respuestas fuera del cierre conjunto.
