# BusinessForms: fallas exclusivas de prueba

El protector que falla observa rollback ante error técnico, nunca se registra en producción. Leer las reglas de TestFeatures y las de integración del módulo.

El canal listo ficticio permite aceptar sin transporte; nunca se registra en producción y sus efectos no previstos fallan. La composición desactiva workers automáticos para que los escenarios de publicación controlen fases y reloj; las pruebas de transporte usan el proveedor ficticio propio de WhatsApp y los servicios productivos.

`StructuredFormPublicationDriver` adapta el worker real al puerto neutral del arnés para la prueba HTTP de composición con los catálogos reales, sin dependencias entre módulos. Sólo se registra en FormsTestModule.

`export-responses-http.ps1` corre build/suite completa, copia cuerpos HTTP y XLSX sin modificarlos al front y emite procedencia/SHA256 automáticamente. El EXPLAIN queda en artefactos locales, fuera de Git. No exportar datos de la base de desarrollo como fixtures.
