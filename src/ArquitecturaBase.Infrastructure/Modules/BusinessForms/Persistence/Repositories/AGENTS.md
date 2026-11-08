# Repositories: entidades seguidas de formularios

Los repositorios del módulo cargan entidades seguidas para modificar, agregan o quitan filas y toman los locks de `FormAdvisoryLockKeys`, cuyo orden fija la [guía de Persistence](../AGENTS.md). El servicio abre el límite y guarda. Lo propio de cada repositorio está por tema en el [detalle](../../../../../../docs/features/formularios-persistencia.md): prefijo y borrado (Definición y ciclo de vida), `RemoveItems` y `ListCatalogFieldValuesAsync` (Catálogos), orden de locks y `ListDataWithValueAsync` (Datos de personas) y lo que siguen corregir y anular (Respuestas); `GetByProposalAsync`, en [Configurations](../Configurations/AGENTS.md).

## Al modificar

- `AsNoTracking` sólo en lectores; los repositorios devuelven entidades seguidas, dan de alta dentro del límite del servicio y nunca guardan. Sin `ExecuteUpdate`/`ExecuteDelete`: la purga de comprobantes sigue entidades.

## Referencias

- [Repositorios del núcleo](../../../../Persistence/Repositories/AGENTS.md).
- Pruebas: [concurrencia de respuestas](../../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormRecordConcurrencyTests.cs) y [datos de personas](../../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormPersonDataPersistenceTests.cs).
