# TestFeatures: código que solo existe para probar

Aloja entidades, contexto, controllers y dobles técnicos de escenarios de integración. No son funcionalidades de la plantilla.

## Al modificar

- Una pieza exclusiva de prueba se queda acá, nunca en `src` ni en la DI productiva.
- El contexto de prueba conserva la configuración real y agrega solo lo que necesita el escenario.
- Registrá controllers y servicios de prueba desde el arnés; no introducir una ruta de producción para facilitar un test.
- Mantené las mismas restricciones relevantes del modelo real para probar auditoría, borrado, errores y límites.

## Referencias

- [Extensiones del arnés](../../../docs/architecture/backend.md).
- [Entidad de prueba](Widget.cs).
- [Composición exclusiva de tests](../Support/ApiFactory.cs).

El puerto neutral `IStructuredFormPublicationDriver` permite que un test HTTP de composición avance una publicación por ID sin importar otro módulo. El adaptador del módulo dueño ejecuta su worker real; sólo Meta se simula. No registra servicios ni rutas productivas.

`ResponseContractCapture` guarda bytes HTTP intactos sólo con FORM_RESPONSES_HTTP_OUTPUT. Es neutral y puede usarlo cualquier módulo; no agrega rutas ni modifica cuerpos.

`IStructuredFormAppPreviewDriver` conecta el productor del navegador con la semilla de conversación del módulo dueño, sin imports entre módulos. Reutiliza ApiFactory, identidad real, almacenamiento privado y un único Hold opt-in; no registra rutas de prueba ni usa datos de Aspire. La metadata de sesión permanece en el archivo privado temporal y se elimina al salir.
