# BusinessForms: fallas exclusivas de prueba

El protector que falla observa rollback ante error técnico, nunca se registra en producción. Leer las reglas de TestFeatures y las de integración del módulo.

El canal listo ficticio permite aceptar sin transporte; nunca se registra en producción y sus efectos no previstos fallan. La composición desactiva workers automáticos para que los escenarios de publicación controlen fases y reloj; las pruebas de transporte usan el proveedor ficticio propio de WhatsApp y los servicios productivos.

`StructuredFormPublicationDriver` adapta el worker real al puerto neutral del arnés para la prueba HTTP de composición con los catálogos reales, sin dependencias entre módulos. Sólo se registra en FormsTestModule.

`export-responses-http.ps1` fija los destinos de las diecinueve cohortes vigentes antes de una única corrida build/suite completa. Agrupa los productores que comparten FORMS_HTTP_CONTRACT_OUTPUT sin reemplazar su carpeta de captura. Usa una carpeta local nueva por corrida; valida todas las cohortes antes de copiar cuerpos HTTP, XLSX y PDF intactos al front, elimina sólo archivos viejos de cada destino verificado y emite procedencia/SHA256 por cohorte y por fuente. Las seis cohortes de formularios, catálogos y archivos heredadas se regeneran con los DTO actuales; catalogs-http/manual histórico permanece como fixture de rechazo y no se atribuye a esta corrida. No ejecutar hasta el cierre de toda la etapa. `FormResponseCommandRecorder` captura el SQL ejecutado y clona sus parámetros Npgsql, sin reconstruir la consulta. El EXPLAIN queda en artefactos locales, fuera de Git. No exportar datos de la base de desarrollo como fixtures.

`FormPdfContractCapture` guarda cuerpos HTTP JSON o PDF intactos sólo con FORM_PDF_HTTP_OUTPUT. La emisión real por aprobación también aporta pdf-completed-es a la cohorte Workflow; el exportador incluye FormPdfHttpTests.cs en la procedencia de ambas cohortes.

El capturador PDF agrega companions .http.json con status, Content-Type, Content-Language y Content-Disposition reales; no construye headers para el consumidor. La procedencia de cada cohorte incluye SHA256 de tests y helpers capturadores, además de cada archivo exportado.

Review17 agrega la cohorte manual whatsapp-forms-http por es/en al mismo cierre, con cuerpos202/200 y estados durables intactos. El exportador conserva rutas relativas y hashes al copiar capturas anidadas; no edita JSON ni ejecuta otra suite para regenerarlas. Conversation identifica recibos manuales y FormAction conserva los avisos automáticos, sin atribuirles un operador.

El productor E2E de workflow/PDF reutiliza PhotoContent del fixture JPEG, SeedSheetAsync y los helpers de captura/scopes/actor existentes. La procedencia de ambas cohortes incluye esos productores y CapturingFormActionChannel. PhotoContent devuelve una copia; no agrega archivos privados reales ni modifica capturas crudas.

FormSuggestionDetailContractCapture exporta dos cuerpos GET íntegros (detail-es y detail-en) bajo FORM_SUGGESTION_DETAILS_HTTP_OUTPUT, en la cohorte forms-suggestion-detail-http. La procedencia incluye FormSuggestionCaptureIntegrationTests y el capturador; se incorpora al exportador único del cierre sin fabricar ni editar JSON.

FormMissingRequestScenario recorre Read combinado, Proposal real y sink público con cuentas ficticias sin RecordsCreate; verifica agrupación/recibo/revisión contra PostgreSQL sin invocar un proveedor. FormMissingRequestContractCapture exporta seis cuerpos íntegros bajo FORM_MISSING_REQUEST_HTTP_OUTPUT para forms-missing-requests-http. Sólo se ejecutan e incorporan al exportador único al cierre de la etapa.
