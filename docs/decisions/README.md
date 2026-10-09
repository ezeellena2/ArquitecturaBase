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
| [0009](0009-redis-como-unico-cache-de-datos.md) | Redis directo es el único caché de datos compartidos | Aceptada, 2026-09-30 |
| [0010](0010-catalogo-de-permisos-extensible.md) | Catálogo compuesto por el núcleo y fuentes de módulos opcionales | Aceptada e implementada, 2026-10-02 |
| [0011](0011-separacion-acceso-y-atencion-whatsapp.md) | Acceso explícito independiente de la atención Manual, con eventos y botones históricos | Aceptada, 2026-10-02 |
| [0012](0012-azure-blob-como-primer-proveedor-de-archivos.md) | Azure Blob Storage como primer proveedor, con prueba desde local contra Azure real | Aceptada, 2026-10-02; implementación pendiente |
| [0013](0013-composicion-de-modulos-en-tests.md) | Contribuciones extensibles de módulos en el arnés de tests | Implementada, 2026-10-02 |
| [0014](0014-credenciales-de-ia-fuera-de-la-base.md) | AES-GCM y HMAC con anillo externo por instalación | Implementada, 2026-10-02 |

- [0015 — Colas transaccionales cifradas](0015-colas-transaccionales-cifradas.md).
- [0016 — Protección de claves Data Protection](0016-proteccion-de-claves-data-protection.md).
- [0017 — Inicio con assistant-ui y operaciones durables](0017-inicio-asistente-y-operaciones-durables.md).
