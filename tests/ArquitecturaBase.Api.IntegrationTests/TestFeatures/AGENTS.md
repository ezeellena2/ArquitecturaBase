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
