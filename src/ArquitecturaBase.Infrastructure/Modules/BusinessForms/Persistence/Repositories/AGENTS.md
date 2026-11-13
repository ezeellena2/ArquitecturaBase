# Repositories: entidades seguidas de formularios

Los repositorios del módulo cargan entidades seguidas para modificar, agregan o quitan filas y toman los locks de `FormAdvisoryLockKeys`, cuyo orden fija la [guía de Persistence](../AGENTS.md). El servicio abre el límite y guarda. Lo propio de cada repositorio está por tema en el [detalle](../../../../../../docs/features/formularios-persistencia.md): prefijo y borrado (Definición y ciclo de vida), `RemoveItems` y `ListCatalogFieldValuesAsync` (Catálogos), orden de locks y `ListDataWithValueAsync` (Datos de personas) y lo que siguen corregir y anular (Respuestas); `GetByProposalAsync`, en [Configurations](../Configurations/AGENTS.md).

## Al modificar

- `AsNoTracking` sólo en lectores; los repositorios devuelven entidades seguidas, dan de alta dentro del límite del servicio y nunca guardan. Sin `ExecuteUpdate`/`ExecuteDelete`: la purga de comprobantes sigue entidades.
- «Documentos recibidos» (DOC5-A): `FormReceivedDocumentRepository` toma `forms:received-document-source:{canal}:{fuente}` al registrar y `forms:received-document:{id}` en cada cambio, siempre el último lock; su purga relee cada documento bajo su lock y borra la historia en cascada. Las tablas `FormReceivedDocuments` (canal y fuente únicos, un CHECK por estado y la búsqueda GIN `simple` sobre los números) y `FormReceivedDocumentEvents` salen de `AddFormReceivedDocuments`.
- DOC6: `FormDocumentProposalRepository` toma `forms:document-proposal-request:{persona}:{pedido}` al guardar un pedido y `forms:document-proposal:{id}` en cada cambio (el borrador, la relectura), antes del lock de cualquiera de sus documentos; al reservar una relectura, `forms:document-reread-asker:{persona}` antes que el del pedido.

## Referencias

- [Repositorios del núcleo](../../../../Persistence/Repositories/AGENTS.md).
- Pruebas: [concurrencia de respuestas](../../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormRecordConcurrencyTests.cs) y [datos de personas](../../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormPersonDataPersistenceTests.cs).
