# Decisiones de arquitectura (ADR)

Un ADR (*Architecture Decision Record*) registra una decisión que cambia cómo se escribe el código de la plantilla: qué se decidió, por qué y qué se descartó. Sirve para que una persona o un agente no reabra una discusión cerrada ni invente una variante propia. El contrato vigente se detalla en arquitectura o features y su aplicación, en las guías. Los planes conservan trabajo pendiente y se retiran al consolidarse según la [rutina documental](../guides/auditar-documentacion.md).

## Formato

- Un archivo por decisión: `NNNN-titulo-corto.md`, con número correlativo de cuatro cifras. Un número no se reutiliza.
- Secciones, en este orden:
  1. el título, con su número;
  2. **Estado**: `Propuesta`, `Aceptada` o `Reemplazada por NNNN`, con la fecha;
  3. **Contexto**: qué pasaba y por qué había que decidir;
  4. **Decisión**: qué se hace, en una o dos frases;
  5. **Consecuencias**: lo que cambia, lo que cuesta y lo que obliga a futuro;
  6. **Alternativas descartadas**: cada una con su motivo.
- Un ADR aceptado no se reescribe. Si la decisión cambia, se escribe uno nuevo y el anterior pasa a `Reemplazada por NNNN`.
- En español rioplatense y corto: si hace falta más de una pantalla, el detalle va a la fuente canónica correspondiente.

## Índice

Las siete primeras salen de las decisiones D1 a D7 del plan de estandarización de 2026-09-26, que a su vez sale del code review del 2026-09-26. El usuario las confirmó ese día tal como estaban recomendadas.

| ADR | Decisión | Estado |
|---|---|---|
| [0001](0001-transaccion-explicita-por-caso-de-uso.md) | Una sola forma de guardar: transacción explícita por caso de uso | Aceptada, 2026-09-26; implementada el 2026-09-27 |
| [0002](0002-contratos-http.md) | Todo body y query de entrada tiene un contrato en `Api/Contracts/<Área>` | Aceptada, 2026-09-26 |
| [0003](0003-sin-eventos-de-dominio.md) | Sin eventos de dominio: se quitan `AggregateRoot` e `IDomainEvent` | Aceptada, 2026-09-26 |
| [0004](0004-roles-como-area-de-referencia.md) | Roles es el área de referencia para copiar | Aceptada, 2026-09-26 |
| [0005](0005-sin-versionado-de-api-por-ahora.md) | La API no se versiona por ahora; el primer cambio incompatible introduce `Asp.Versioning` | Aceptada, 2026-09-26 |
| [0006](0006-migraciones-y-seed-fuera-de-development.md) | Seed idempotente en todos los ambientes y migraciones con bundle fuera de Development | Aceptada, 2026-09-26; implementada el 2026-09-28 |
| [0007](0007-whatsapp-como-modulo-opcional.md) | WhatsApp es un módulo opcional dentro del mismo repo | Aceptada, 2026-09-26; implementada el 2026-09-29 |
| [0008](0008-nombres-de-repositorios-y-lectores.md) | Los métodos de repositorios y lectores se nombran por lo que devuelven | Aceptada, 2026-09-27 |
| [0009](0009-redis-como-unico-cache-de-datos.md) | Redis directo es el único caché de datos compartidos | Aceptada, 2026-09-30; enmendada por el 0032 el 2026-11-16 (los permisos sin caché y sin Redis la app sigue) |
| [0010](0010-catalogo-de-permisos-extensible.md) | Catálogo compuesto por el núcleo y fuentes de módulos opcionales | Aceptada e implementada, 2026-10-02; enmendada por el 0032 el 2026-11-16 (el seed no invalida permisos) |
| [0011](0011-separacion-acceso-y-atencion-whatsapp.md) | Acceso explícito independiente de la atención Manual, con eventos y botones históricos | Aceptada, 2026-10-02 |
| [0012](0012-azure-blob-como-primer-proveedor-de-archivos.md) | Azure Blob Storage como primer proveedor, con prueba desde local contra Azure real | Aceptada, 2026-10-02; implementada con su primer consumidor y verificada con Azure real el 2026-10-15 ([archivos privados](../architecture/backend.md#archivos-privados)) |
| [0013](0013-composicion-de-modulos-en-tests.md) | Contribuciones extensibles de módulos en el arnés de tests | Implementada, 2026-10-02 |
| [0014](0014-credenciales-de-ia-fuera-de-la-base.md) | AES-GCM y HMAC con anillo externo por instalación | Implementada, 2026-10-02 |
| [0015](0015-colas-transaccionales-cifradas.md) | Colas transaccionales cifradas | Aceptada, 2026-10-03 |
| [0016](0016-proteccion-de-claves-data-protection.md) | Protección de claves Data Protection | Aceptada, 2026-10-03 |
| [0017](0017-inicio-asistente-y-operaciones-durables.md) | Inicio con assistant-ui y operaciones durables | Aceptada, 2026-10-04 |
| [0018](0018-robustez-proporcional-al-efecto.md) | Robustez proporcional al efecto y verificación por paso | Aceptada, 2026-10-11 |
| [0019](0019-sql-de-configuracion-acotado-a-catalogos.md) | SQL de configuración acotado a catálogos | Aceptada, 2026-10-11; sin SQL configurable por ahora: Query/K5 se retoma si Filters no alcanza |
| [0020](0020-motor-unico-de-ia-con-capacidades.md) | Motor único de IA con capacidades por módulo | Aceptada, 2026-10-16; rige desde la Etapa 5 de formularios; enmendada por el 0034 desde la etapa 1 de su plan (para crear, editar o eliminar, acciones declaradas y una propuesta común) |
| [0021](0021-respuestas-redactadas-por-el-asistente.md) | Respuestas redactadas por el asistente, con límites | Aceptada, 2026-10-21; rige desde Casos del asistente |
| [0022](0022-lectura-de-documentos-con-ia.md) | Lectura de documentos con IA | Aceptada, 2026-10-22; rige desde Documentos por WhatsApp |
| [0023](0023-arca-como-modulo-opcional.md) | ARCA como módulo opcional y sus credenciales | Aceptada, 2026-11-03; rige desde la etapa 1 de ARCA |
| [0024](0024-ia-arma-formularios-y-catalogos.md) | La IA arma formularios y catálogos a pedido | Aceptada, 2026-11-04; rige desde la etapa 1 de [su plan](../features/formularios.md#la-ia-arma-formularios-y-catálogos--etapa-1-2026-11-04) |
| [0025](0025-familias-de-servicios-arca.md) | Familias de servicios dentro del módulo ARCA | Aceptada, 2026-11-04; rige desde la etapa 1 de ARCA |
| [0026](0026-robot-de-delegacion-arca.md) | Robot de delegación sobre la página de ARCA y la clave fiscal en memoria | Aceptada, 2026-11-09; rige desde la etapa 6 de ARCA ([su plan](../features/arca.md#delegación-con-robot-modo-3)) |
| [0027](0027-delegacion-guiada-arca.md) | Delegación guiada en ARCA | Aceptada, 2026-11-09; rige desde la [etapa 5 de ARCA](../features/arca.md#delegación-guiada-etapa-5) |
| [0028](0028-emision-de-comprobantes-arca.md) | Emisión de comprobantes en ARCA: cola, numeración y reconciliación | Aceptada, 2026-11-09; rige desde la [etapa 3 de ARCA](../features/arca.md#emisión-de-comprobantes-etapas-3-y-4); el 0031 reemplaza su decisión 5 en lo que dice del 10016 |
| [0029](0029-biblioteca-de-estandares.md) | La biblioteca de estándares, aportada por los módulos como datos | Aceptada, 2026-11-09; rige desde la etapa 2 de ARCA ([regla](../features/formularios.md#biblioteca-de-estándares--bi1-a-2026-11-09)) |
| [0030](0030-contenido-del-chat-cifrado.md) | El contenido del chat de WhatsApp se guarda cifrado | Aceptada, 2026-11-14; rige desde la tarea 20 del [plan de la bandeja](../plans/2026-10-01-bandeja-whatsapp.md) |
| [0031](0031-fecha-rechazada-y-numero-adelantado-arca.md) | La fecha que ARCA rechaza y el número que ARCA tiene adelantado van a una persona | Aceptada, 2026-11-14; la fecha rige desde el arreglo de H1 (0385b59b) y el número adelantado, desde el de H3 (6ee20368), según la [revisión de ARCA](../reviews/2026-11-14-arquitectura-arca.md#cómo-seguir) |
| [0032](0032-sin-redis-la-app-sigue.md) | Sin Redis la app sigue, y los permisos se leen de la base | Aceptada, 2026-11-14; rige desde el 2026-11-16 (N9 de la [revisión del núcleo](../reviews/2026-11-14-arquitectura-nucleo.md)); enmienda el 0009 y el 0010 |
| [0033](0033-motores-configurables-para-procesos.md) | Los procesos se arman con motores configurables, a mano o con la IA | Propuesta, 2026-11-19; sale de [procesos del campo](../plans/2026-11-19-procesos-del-campo.md); su punto 4 sigue al [0034](0034-acciones-del-asistente.md) |
| [0034](0034-acciones-del-asistente.md) | Acciones del asistente: la IA prepara y la pantalla de siempre confirma | Aceptada, 2026-11-19; rige desde la etapa 1 de [su plan](../plans/2026-11-19-procesos-del-campo.md#acciones-del-asistente); enmienda el 0020 |

Un enlace a un plan dice desde cuándo rige; al retirar ese plan, la auditoría documental lo cambia por la regla que quedó en `features` o en la arquitectura.
