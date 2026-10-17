# BusinessForms: fallas exclusivas de prueba

El protector que falla observa rollback ante error técnico, nunca se registra en producción. Leer las reglas de TestFeatures y las de integración del módulo.

El canal listo ficticio permite aceptar sin transporte; nunca se registra en producción y sus efectos no previstos fallan. La composición desactiva workers automáticos para que los escenarios de publicación controlen fases y reloj; las pruebas de transporte usan el proveedor ficticio propio de WhatsApp y los servicios productivos.

`StructuredFormPublicationDriver` adapta el worker real al puerto neutral del arnés para la prueba HTTP de composición con los catálogos reales, sin dependencias entre módulos. Sólo se registra en FormsTestModule.

`export-responses-http.ps1` fija los siete destinos FORM_RESPONSES_HTTP_OUTPUT, FORM_RULES_HTTP_OUTPUT, FORM_WORKFLOW_HTTP_OUTPUT, FORM_APPROVALS_HTTP_OUTPUT, FORM_PDF_HTTP_OUTPUT, WHATSAPP_FORM_ACTIONS_HTTP_OUTPUT y WHATSAPP_FORMS_HTTP_CONTRACT_OUTPUT antes de una única corrida build/suite completa. Usa una carpeta local nueva por corrida; valida las siete cohortes antes de copiar cuerpos HTTP, XLSX y PDF intactos al front, elimina sólo archivos viejos de cada destino y emite procedencia/SHA256 por cohorte y por fuente. No ejecutar hasta el cierre de toda la etapa. `FormResponseCommandRecorder` captura el SQL ejecutado y clona sus parámetros Npgsql, sin reconstruir la consulta. El EXPLAIN queda en artefactos locales, fuera de Git. No exportar datos de la base de desarrollo como fixtures.

`FormPdfContractCapture` guarda cuerpos HTTP JSON o PDF intactos sólo con FORM_PDF_HTTP_OUTPUT. La emisión real por aprobación también aporta pdf-completed-es a la cohorte Workflow; el exportador incluye FormPdfHttpTests.cs en la procedencia de ambas cohortes.

El capturador PDF agrega companions .http.json con status, Content-Type, Content-Language y Content-Disposition reales; no construye headers para el consumidor. La procedencia de cada cohorte incluye SHA256 de tests y helpers capturadores, además de cada archivo exportado.

Review17 agrega la cohorte manual whatsapp-forms-http por es/en al mismo cierre, con cuerpos202/200 y estados durables intactos. El exportador conserva rutas relativas y hashes al copiar capturas anidadas; no edita JSON ni ejecuta otra suite para regenerarlas. Conversation identifica recibos manuales y FormAction conserva los avisos automáticos, sin atribuirles un operador.

El productor E2E de workflow/PDF reutiliza PhotoContent del fixture JPEG, SeedSheetAsync y los helpers de captura/scopes/actor existentes. La procedencia de ambas cohortes incluye esos productores y CapturingFormActionChannel. PhotoContent devuelve una copia; no agrega archivos privados reales ni modifica capturas crudas.
