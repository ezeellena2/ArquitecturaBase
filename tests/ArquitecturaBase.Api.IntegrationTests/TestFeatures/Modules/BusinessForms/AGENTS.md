# BusinessForms: fallas exclusivas de prueba

El protector que falla observa rollback ante error técnico, nunca se registra en producción. Leer las reglas de TestFeatures y las de integración del módulo.

El canal listo ficticio permite aceptar sin transporte; nunca se registra en producción y sus efectos no previstos fallan. La composición desactiva workers automáticos para que los escenarios de publicación controlen fases y reloj; las pruebas de transporte usan el proveedor ficticio propio de WhatsApp y los servicios productivos.

`StructuredFormPublicationDriver` adapta el worker real al puerto neutral del arnés para la prueba HTTP de composición con los catálogos reales, sin dependencias entre módulos. Sólo se registra en FormsTestModule.

`export-responses-http.ps1` fija los destinos de las cohortes vigentes antes de una única corrida build/suite completa. Agrupa los productores que comparten FORMS_HTTP_CONTRACT_OUTPUT sin reemplazar su carpeta de captura. Usa una carpeta local nueva por corrida; valida todas las cohortes antes de copiar cuerpos HTTP, XLSX y PDF intactos al front, elimina sólo archivos viejos de cada destino verificado y emite procedencia/SHA256 por cohorte y por fuente. `FormResponseCommandRecorder` captura el SQL ejecutado y clona sus parámetros Npgsql, sin reconstruir la consulta. El EXPLAIN queda en artefactos locales, fuera de Git. No exportar datos de la base de desarrollo como fixtures. El exportador conserva rutas relativas y hashes al copiar capturas anidadas; no edita JSON ni ejecuta otra suite para regenerarlas. test-export-responses-http.ps1 verifica configuración, bytes, faltantes y colisiones con archivos sintéticos locales, sin compilar ni usar Docker o fixtures del front. Qué exige cada cohorte del exportador (cuántos cuerpos o archivos y cuáles) y qué productores fija su procedencia está en [las pruebas de integración de Formularios](../../../../../docs/features/formularios-pruebas-de-integracion.md#exportador-y-cohortes-http): un inventario que cambia se escribe ahí, no en esta guía.

`FormPdfContractCapture` guarda cuerpos HTTP JSON o PDF intactos sólo con FORM_PDF_HTTP_OUTPUT. La emisión real por aprobación también aporta pdf-completed-es a la cohorte Workflow; el exportador incluye FormPdfHttpTests.cs en la procedencia de ambas cohortes.

El capturador PDF agrega companions .http.json con status, Content-Type, Content-Language y Content-Disposition reales; no construye headers. La procedencia de cada cohorte incluye SHA256 de tests y helpers capturadores, además de cada archivo exportado.

El productor E2E de workflow/PDF reutiliza PhotoContent del fixture JPEG, SeedSheetAsync y los helpers de captura/scopes/actor existentes. La procedencia de ambas cohortes incluye esos productores y CapturingFormActionChannel. PhotoContent devuelve una copia; no agrega archivos privados reales ni modifica capturas crudas.

FormMissingRequestScenario recorre Read combinado, Proposal real y sink público con cuentas ficticias sin RecordsCreate; verifica agrupación/recibo/revisión contra PostgreSQL sin invocar un proveedor.

FormAssistantMigrationFixture escribe sólo las columnas del contrato anterior mediante SQL parametrizado. Usa los modelos canónicos para las preguntas/valores y verifica el contenido conservado por consultas explícitas antes y después de cada migración; nunca cambia la base AppHost ni fabrica atribución histórica.

FormMissingRequestScenario usa una base propia por defecto, borrada al disponer el host; sólo el preview opta explícitamente por la base compartida de su UI. El sufijo del tema contiene sólo letras para no activar la protección determinística de datos personales. La reapertura de descartados propone de nuevo el nombre general: ya no fabrica un ExistingTopicId ausente de la lectura.

ControlledFormChannelStateSource aporta sólo evidencia local del puerto neutral por IDs propios para los escenarios HTTP de eliminación. No simula persistencia, crea sesiones ni llama a Meta; la lectura real de sesiones/recibos pertenece a las pruebas del módulo dueño.

FormPublishedVersionFixture sella PublishedCultures desde la misma lista explícita usada en el manifiesto de la publicación ficticia. No infiere idiomas por nombres o esquema; los escenarios de metadata histórica ausente la retiran sólo dentro de su fixture para probar el backfill.

FormApprovalContinuationFixture agrega tareas y recibos propios a la definición de SeedApprovalAsync, con empate de fecha, respuesta propia y rol ajeno; no reescribe versiones selladas.

FormActiveRuleSetMigrationFixture escribe una definición con su revisión de reglas activa por las columnas anteriores a DeferActiveRuleSetKey, lee si la clave existe y si es diferida, y crea en la base migrada a quien agrega de la biblioteca y lee las reglas.

FormCatalogValueMigrationFixture inserta el contrato previo por SQL parametrizado; proyecta columnas históricas de valores/referencias y crea un operador propio con UoW del host migrado.

FormCatalogImportContractCapture guarda bytes HTTP intactos JSON/XLSX en es/en bajo FORM_CATALOG_IMPORT_HTTP_OUTPUT; los XLSX llevan companion con status y headers reales.

FormMultipleChoiceMigrationFixture crea versiones con draft.fields y version.questions separados y sus propios registros/recibos; incluye un borrador sin versión o con activa escalar. Sus tests se detienen en AddFormMultipleChoiceValues para que los Down posteriores no alteren el historial esperado del guard E1. El rechazo compara hechos completos y migraciones aplicadas; Up/Down proyecta sólo columnas anteriores. La prueba de ValueType recorre su frontera y restaura el esquema vigente antes del intercambio HTTP real; su snapshot de ítems usa columnas previas explícitas, sin incorporar metadata de migraciones futuras.

`FormReceivedDocumentScenario` registra documentos por el puerto neutral con un almacenamiento privado en memoria y siembra una copia de «Comprobante de transferencia» (clave, versión y datos de repetidos atados) con respuestas propias. `ScriptedDocumentOperations` es el motor guionado del cambio de formulario: guarda lo que se le pide por quien lo pidió, sólo lo encuentra con el contexto sellado, contesta pendiente hasta que la prueba lo completa y puede fallar el envío; nunca llama a un proveedor. `FormReceivedDocumentContractCapture` guarda los cuerpos sólo con FORM_DOCUMENTS_HTTP_OUTPUT.

FormPersonDataMigrationFixture conserva filas históricas completas, incluidos FieldsJson y snapshots E2-1, y agrega metadata de prueba antes de AddFormPersonData. Usa bases aisladas con limpieza; no altera el AppHost ni ejecuta PostgreSQL fuera del cierre.
