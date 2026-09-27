# Decisiones de arquitectura (ADR)

Un ADR (*Architecture Decision Record*) registra una decisión que cambia cómo se escribe el código de la plantilla: qué se decidió, por qué y qué se descartó. Sirve para que una persona o un agente no reabra una discusión cerrada ni invente una variante propia. El detalle de implementación no va acá: vive en el plan o en la especificación que el ADR enlaza.

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
- En español rioplatense y corto: si hace falta más de una pantalla, el detalle va al plan.

## Índice

Las siete primeras salen de las decisiones D1 a D7 del [plan maestro](../plans/2026-09-26-plantilla-estandar-por-etapas.md), que a su vez sale del code review del 2026-09-26. El usuario las confirmó ese día tal como estaban recomendadas.

| ADR | Decisión | Estado |
|---|---|---|
| [0001](0001-transaccion-explicita-por-caso-de-uso.md) | Una sola forma de guardar: transacción explícita por caso de uso | Aceptada, 2026-09-26; implementada el 2026-09-27 |
| [0002](0002-contratos-http.md) | Todo body y query de entrada tiene un contrato en `Api/Contracts/<Área>` | Aceptada, 2026-09-26 |
| [0003](0003-sin-eventos-de-dominio.md) | Sin eventos de dominio: se quitan `AggregateRoot` e `IDomainEvent` | Aceptada, 2026-09-26 |
| [0004](0004-roles-como-area-de-referencia.md) | Roles es el área de referencia para copiar | Aceptada, 2026-09-26 |
| [0005](0005-sin-versionado-de-api-por-ahora.md) | La API no se versiona por ahora; el primer cambio incompatible introduce `Asp.Versioning` | Aceptada, 2026-09-26 |
| [0006](0006-migraciones-y-seed-fuera-de-development.md) | Seed idempotente en todos los ambientes y migraciones con bundle fuera de Development | Aceptada, 2026-09-26 |
| [0007](0007-whatsapp-como-modulo-opcional.md) | WhatsApp es un módulo opcional dentro del mismo repo | Aceptada, 2026-09-26 |
| [0008](0008-nombres-de-repositorios-y-lectores.md) | Los métodos de repositorios y lectores se nombran por lo que devuelven | Aceptada, 2026-09-27 |
