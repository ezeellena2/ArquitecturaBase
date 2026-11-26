# Support: arnés compartido de integración

Contiene `ApiFactory`, helpers de autenticación y scopes de prueba. Configura proveedores reales y dobles del borde para ejecutar escenarios sin servicios de entrega externos.

## Al modificar

- `ApiTestModules` descubre `IApiTestModule` presentes para settings, servicios e inventario. Cada aporte vive en Modules/<M>; no agregar partial void con dueño único ni nombres de módulos al núcleo. Ver [ADR 0013](../../../docs/decisions/0013-composicion-de-modulos-en-tests.md).

- Conservá las opciones de persistencia registradas por producción; el arnés cambia conexión y tipo de contexto para sus piezas de prueba.
- Una sustitución compartida debe ser explícita y restaurarse al terminar. No filtrar estado entre escenarios.
- `CacheTestHost` permite scopes y procesos lógicos independientes sobre Redis real; no reemplazarlo por un diccionario.
- Un test que mide todo el proceso (`GC.GetTotalAllocatedBytes`) va en `AloneTestGroup`, que corre solo después de las colecciones en paralelo; si lo medido es del hilo propio, alcanza con `GC.GetAllocatedBytesForCurrentThread`.
- `ApiTestCollectionFactory` reparte las clases de `ApiTestGroup` en 32 colecciones, cada una con su `ApiFactory`, y `ApiTestCollectionOrderer` larga primero las que más tardaron en la última medición (`api-test-durations.json`; una clase que no figura pesa sus casos al promedio); corren a la vez la mitad de los hilos de la máquina (`xunit.runner.json`). Si los tiempos cambian mucho, `update-api-test-durations.ps1` regenera la medición desde el informe de una corrida entera (`-result-xml`). Las colecciones no comparten datos, pero sí el proceso: una escucha de trazas o métricas oye a todos los hosts y elige lo suyo por un identificador del escenario (traza, operación) o por el alcance del medidor.
- Los helpers no deben ocultar assertions importantes ni convertir una falla del producto en éxito del test.

## Referencias

- [Arnés y dobles](../../../docs/architecture/backend.md).
- [Configuración de la factory](ApiFactory.cs).
- [Hosts de caché](CacheTestHost.cs).

El proveedor efímero de protección se comparte entre clones de una factory. Un productor capturador de módulo usa una fuente inactiva para no tomar entregas de un host con cola real. Las pruebas de transporte que alimentan la cola concreta activan explícitamente la fuente durable; nunca reemplazar esa fuente por memoria en pruebas transaccionales.
