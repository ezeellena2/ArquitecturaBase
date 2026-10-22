# BusinessForms: casos de uso de formularios

Leer [formularios](../../../../docs/features/formularios.md) y el [resumen de la etapa en curso](../../../../docs/plans/formularios/README.md).

Módulo opcional: autoría, publicación, ciclo de vida, catálogos, captura, planilla, reglas, PDF, asistente y sugerencias; persistencia en Infrastructure y rutas en Api.

- `Channels/`: adaptadores de los puertos neutrales del núcleo ([guía](Channels/AGENTS.md)).
- `Configuration/`: `BusinessFormsOptions`; `BusinessForms:MaxFileSizeMiB` admite 1–25 (25 por defecto), se valida al iniciar y lo sella el manifiesto ([fotos y documentos](../../../../docs/features/formularios.md#fotos-y-documentos--implementación-local-2b-2026-10-15)).
- `Interfaces/Integrations/`: protectores de comprobantes y manifiestos (propósitos propios), renderer PDF, escritor de Excel y lector de planillas. El lector recibe un stream del llamador y devuelve celdas, errores y SHA-256 de los bytes originales; no guarda archivos ni conoce HTTP.
- `Interfaces/Persistence/`: repositorios y lectores ([guía](Interfaces/Persistence/AGENTS.md)).
- `Interfaces/Services/`: un contrato por caso de uso, consumido por Api y los workers.
- `Models/`: pedidos, respuestas y filas; el `ToString()` de lo que lleva contenido se redacta.
- `Resources/`: textos es/en (`.resx` y `.en.resx`) con sus accesores estáticos.
- `Services/`: casos de uso y helpers, por área ([guía](Services/AGENTS.md)).
- `Validation/`: validadores de los pedidos.

## Al modificar

- No referenciés WhatsApp, IA ni Meta. El cruce va por puertos de `Interfaces/Channels` y DTO de `Models/StructuredData` o `Models/Assistant`, con commits separados e IDs opacos; nunca se llama al escritor de otro módulo dentro de una UoW propia.
- Una UoW `OnSuccess` por método público que escribe; helpers, adaptadores y fuentes de capacidades no guardan. Los efectos (Meta, envíos, almacenamiento, renderer) van fuera del límite; adentro sólo se consultan puertos locales sin efectos.
- Permisos actuales de `BusinessFormsPermissions`, consultados en cada operación; los datos privados se revalidan antes y después de leerlos. Valores, archivos y PDF de respuestas exigen `formrecords.read`; decidir, administrar o publicar no lo conceden (la excepción del asistente sobre la propia captura está en Services).
- La configuración interna (borrador, catálogos, reglas, diseño PDF, sugerencias) se guarda con revisión esperada y 409 ([ADR 0018](../../../../docs/decisions/0018-robustez-proporcional-al-efecto.md)); lo durable queda para efectos externos.
- Un campo opcional nuevo en un modelo que entra en una huella o snapshot sellado lleva `JsonIgnore(WhenWritingNull)`, como `OfferWhen`, `Source` y `Files`: así no cambian las huellas anteriores.
- Todo se registra en `BusinessFormsApplicationRegistration`: servicios con `TryAddScoped` y puertos con `Replace` o `TryAddEnumerable`.

## Referencias

- [Módulos opcionales](../../../../docs/architecture/backend.md#módulos-opcionales) y [formularios estructurados](../../../../docs/architecture/backend.md#formularios-estructurados-módulo-businessforms).
- Otras secciones de formularios: [aceptación durable](../../../../docs/features/formularios.md#aceptación-durable--primer-corte-2b-2026-10-10), [recarga de la publicación](../../../../docs/features/formularios.md#recarga-de-publicación-y-cultura-de-preview--mínimo-2), [catálogos Manual y Filters](../../../../docs/features/formularios.md#verificación-de-cierre--2026-10-11), [cierre de catálogos](../../../../docs/features/formularios.md#correcciones-de-cierre-de-catálogos--2026-10-13), [reglas](../../../../docs/features/formularios.md#reglas-con-revisiones-propias--etapa-4-g1) e [indicación de ofrecimiento](../../../../docs/features/formularios.md#indicación-de-ofrecimiento--etapa-5).
- [Pruebas unitarias](../../../../tests/ArquitecturaBase.Application.UnitTests/Modules/BusinessForms/AGENTS.md) e [integración](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/AGENTS.md).
