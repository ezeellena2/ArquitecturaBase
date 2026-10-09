# Support: arnés compartido de integración

Contiene `ApiFactory`, helpers de autenticación y scopes de prueba. Configura proveedores reales y dobles del borde para ejecutar escenarios sin servicios de entrega externos.

## Al modificar

- `ApiTestModules` descubre `IApiTestModule` presentes para settings, servicios e inventario. Cada aporte vive en Modules/<M>; no agregar partial void con dueño único ni nombres de módulos al núcleo. Ver [ADR 0013](../../../docs/decisions/0013-composicion-de-modulos-en-tests.md).

- Conservá las opciones de persistencia registradas por producción; el arnés cambia conexión y tipo de contexto para sus piezas de prueba.
- Una sustitución compartida debe ser explícita y restaurarse al terminar. No filtrar estado entre escenarios.
- `CacheTestHost` permite scopes y procesos lógicos independientes sobre Redis real; no reemplazarlo por un diccionario.
- Los helpers no deben ocultar assertions importantes ni convertir una falla del producto en éxito del test.

## Referencias

- [Arnés y dobles](../../../docs/architecture/backend.md).
- [Configuración de la factory](ApiFactory.cs).
- [Hosts de caché](CacheTestHost.cs).

El proveedor efímero de protección se comparte entre clones de una factory. Un productor capturador de módulo usa una fuente inactiva para no tomar entregas de un host con cola real. Las pruebas de transporte que alimentan la cola concreta activan explícitamente la fuente durable; nunca reemplazar esa fuente por memoria en pruebas transaccionales.
