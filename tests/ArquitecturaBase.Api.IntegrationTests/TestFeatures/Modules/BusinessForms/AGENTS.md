# BusinessForms: fallas exclusivas de prueba

El protector que falla observa rollback ante error técnico, nunca se registra en producción. Leer las reglas de TestFeatures y las de integración del módulo.

El canal listo ficticio permite aceptar sin transporte; nunca se registra en producción y sus efectos no previstos fallan. La composición desactiva workers automáticos para que los escenarios de publicación controlen fases y reloj; las pruebas de transporte usan el proveedor ficticio propio de WhatsApp y los servicios productivos.

`StructuredFormPublicationDriver` adapta el worker real al puerto neutral del arnés para la prueba HTTP de composición con los catálogos reales, sin dependencias entre módulos. Sólo se registra en FormsTestModule.

`export-responses-http.ps1` fija los cinco destinos FORM_RESPONSES_HTTP_OUTPUT, FORM_RULES_HTTP_OUTPUT, FORM_WORKFLOW_HTTP_OUTPUT, FORM_APPROVALS_HTTP_OUTPUT y FORM_PDF_HTTP_OUTPUT antes de una única corrida build/suite completa. Usa una carpeta local nueva por corrida; valida las cinco cohortes antes de copiar cuerpos HTTP, XLSX y PDF intactos al front, elimina sólo archivos viejos de cada destino y emite procedencia/SHA256 por cohorte y por fuente. No ejecutar hasta el cierre de toda la etapa. `FormResponseCommandRecorder` captura el SQL ejecutado y clona sus parámetros Npgsql, sin reconstruir la consulta. El EXPLAIN queda en artefactos locales, fuera de Git. No exportar datos de la base de desarrollo como fixtures.
