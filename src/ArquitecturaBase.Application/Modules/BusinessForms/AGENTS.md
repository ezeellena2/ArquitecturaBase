# BusinessForms: casos de uso de formularios

Leer [formularios](../../../../docs/features/formularios.md) y los [pendientes del módulo](../../../../docs/plans/formularios/README.md).

Módulo opcional: autoría, publicación, ciclo de vida, catálogos, captura, planilla, reglas, PDF, asistente y sugerencias; persistencia en Infrastructure y rutas en Api.

- `Channels/`: adaptadores de los puertos neutrales del núcleo ([guía](Channels/AGENTS.md)).
- Configuración › Formularios (RC4-A): `FormSettingsService` y el puerto `IFormSettingsReader`, que cada uso lee (tamaño por archivo, relecturas de DOC6, sugerencia de la bandeja y los dos plazos de retención); sin `IOptions` al arrancar.
- `Interfaces/Integrations/`: protectores de comprobantes y manifiestos (propósitos propios), renderer PDF, escritor de Excel y lector de planillas. El lector recibe un stream del llamador y devuelve celdas, errores y SHA-256 de los bytes originales; no guarda archivos ni conoce HTTP. `DetectFormat` dice qué son unos bytes y `Read` los lee con ese formato, para la planilla adjunta al asistente, y `ReadSheets`, todas sus hojas, para cargar sus filas (VC1-A); `IFormDocumentReader` hace lo mismo con un documento (PDF, foto o Word).
- `Interfaces/Persistence/`: repositorios y lectores ([guía](Interfaces/Persistence/AGENTS.md)).
- `Interfaces/Services/`: un contrato por caso de uso, consumido por Api y los workers.
- `Models/`: pedidos, respuestas y filas; el `ToString()` de lo que lleva contenido se redacta.
- `Resources/`: textos es/en (`.resx` y `.en.resx`) con sus accesores estáticos; `Standards/`, los estándares que trae el módulo y la tabla de entidades del BCRA, como JSON incrustado (DOC1).
- `Services/`: casos de uso y helpers, por área ([guía](Services/AGENTS.md)).
- `Validation/`: validadores de los pedidos.

## Al modificar

- No referenciés WhatsApp, IA ni Meta. El cruce va por puertos de `Interfaces/Channels` y DTO de `Models/StructuredData` o `Models/Assistant`, con commits separados e IDs opacos; nunca se llama al escritor de otro módulo dentro de una UoW propia.
- Una UoW `OnSuccess` por método público que escribe; helpers, adaptadores y fuentes de capacidades no guardan. Los efectos (Meta, envíos, almacenamiento, renderer) van fuera del límite; adentro sólo se consultan puertos locales sin efectos.
- Permisos actuales de `BusinessFormsPermissions`, consultados en cada operación; los datos privados se revalidan antes y después de leerlos. Valores, archivos y PDF de respuestas exigen `formrecords.read`; decidir, administrar o publicar no lo conceden. Hay dos excepciones sobre lo propio: «Completar formularios» lee las respuestas que cargó la persona (`FormRecordAccessGuard.CheckReadAsync`, CW6-C) y el asistente lee la propia captura (en Services). Corregir y anular piden «Corregir y anular respuestas» con «Ver respuestas», o «Completar formularios» sobre lo propio sin decidir (`FormRecordAmendmentGuard`, decisión 8).
- La configuración interna (borrador, catálogos, reglas, diseño PDF, sugerencias) se guarda con revisión esperada y 409 ([ADR 0018](../../../../docs/decisions/0018-robustez-proporcional-al-efecto.md)); lo durable queda para efectos externos.
- Un campo opcional nuevo en un modelo que entra en una huella o snapshot sellado lleva `JsonIgnore(WhenWritingNull)`, como `OfferWhen`, `Source` y `Files`: así no cambian las huellas anteriores.
- Al subir la versión de una capacidad, lo que lee su resultado (`FormDocumentAuthoringPolicy`) sigue reconociendo la anterior mientras esté registrada; hoy compara la exacta (I4).
- Todo se registra en `BusinessFormsApplicationRegistration`: servicios con `TryAddScoped` y puertos con `Replace` o `TryAddEnumerable`.

## Referencias

- [Módulos opcionales](../../../../docs/architecture/backend.md#módulos-opcionales) y [formularios estructurados](../../../../docs/architecture/backend.md#formularios-estructurados-módulo-businessforms).
- Otras secciones de [formularios](../../../../docs/features/formularios.md), por su título: «Aceptación durable», «Recarga de publicación», «Verificación de cierre» (catálogos Manual y Filters), «Correcciones de cierre de catálogos», «Reglas con revisiones propias» e «Indicación de ofrecimiento».
- [Pruebas unitarias](../../../../tests/ArquitecturaBase.Application.UnitTests/Modules/BusinessForms/AGENTS.md) e [integración](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/AGENTS.md).
