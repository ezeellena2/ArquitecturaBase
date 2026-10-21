# BusinessForms: persistencia, protección y trabajo durable

Adaptadores del módulo opcional de formularios: implementan los puertos de `Application/Modules/BusinessForms`. Leé [formularios](../../../../docs/features/formularios.md) antes de tocarlo. El núcleo no nombra este módulo y este módulo no nombra a WhatsApp.

- `Persistence/`: configuraciones EF, lectores, repositorios, `FormAdvisoryLockKeys` y el seed de presentación de versiones ([guía](Persistence/AGENTS.md)).
- `Workers/`: publicación en canales, motor de acciones y purga de comprobantes de borrador ([guía](Workers/AGENTS.md)).
- `Pdf/`: renderer de la copia fija y del preview del diseño ([guía](Pdf/AGENTS.md)).
- `Security/`: Data Protection de comprobantes de guardado y de ticket de chequeo y manifiesto de publicación.
- `Export/`: `ClosedXmlFormWorkbookWriter`, el XLSX del export de respuestas.
- `BusinessFormsInfrastructureRegistration`: único lugar que nombra las clases concretas; `Program.cs` lo compone.

## Al modificar

- EF de negocio sólo en repositorios y lectores del módulo; `IQueryable` no sale de Infrastructure. Las migraciones van en la cadena única del núcleo.
- Entre BusinessForms y un canal hay commits separados e IDs opacos: sin FK ni joins a tablas de WhatsApp y nunca un escritor de otro módulo dentro de una transacción de este.
- Cada propósito de Data Protection es propio, versionado (`v1`) y ligado a las identidades de su fila: comprobante por fila/actor/UUID/definición/kind; manifiesto por operación/actor/UUID/definición/versión; el ticket de chequeo tiene propósito aparte. El JSON es cerrado (rechaza miembros desconocidos) y el texto plano se borra de memoria. Renombrar un propósito vuelve ilegible lo sellado.
- La huella SHA-256 del pedido vive sólo dentro del resultado protegido; ningún hash privado, cuerpo o resultado va a columnas o logs. Un manifiesto que no coincide con su dueño falla.
- El restore necesita el anillo de claves protegido y los certificados de la plantilla; el módulo no agrega credenciales de IA ni de Meta.
- La configuración interna (catálogos, reglas, diseño PDF, sugerencias) se guarda con revisión y 409; sólo publicar, ejecutar acciones y emitir el PDF usan trabajo durable ([ADR 0018](../../../../docs/decisions/0018-robustez-proporcional-al-efecto.md)).
- El XLSX usa ClosedXML (MIT) sólo en esta capa: celdas tipadas, texto literal sin fórmulas, sin Office ni procesos externos, sin persistencia.
- Logs con `[LoggerMessage]`: estados, IDs operativos y tipo de excepción; nunca valores de respuestas, nombres de archivos, hashes ni claves de almacenamiento.
- SQL, locks, carreras y workers se prueban con PostgreSQL real; renderer y escritor XLSX, con tests técnicos sin Docker.

## Referencias

- [Formularios estructurados](../../../../docs/architecture/backend.md#formularios-estructurados-módulo-businessforms) y [módulos opcionales](../../../../docs/architecture/backend.md#módulos-opcionales).
- [Un guardado y recuperación exacta](../../../../docs/features/formularios.md#un-guardado-y-recuperación-exacta): propósito, huella y restore.
- [Tests de integración del módulo](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/AGENTS.md), [protección](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormDraftProtectionTests.cs) y [XLSX](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormWorkbookWriterTests.cs).
- [Aportes de arquitectura del módulo](../../../../tests/ArquitecturaBase.ArchitectureTests/Modules/BusinessForms/AGENTS.md).
