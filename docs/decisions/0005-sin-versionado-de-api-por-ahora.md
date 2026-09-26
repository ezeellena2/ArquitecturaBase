# 0005. Sin versionado de la API por ahora

**Estado:** Aceptada, 2026-09-26.

**Origen:** decisión D5 del [plan maestro](../plans/2026-09-26-plantilla-estandar-por-etapas.md), que sale del code review del 2026-09-26.

## Contexto

Las rutas de negocio no llevan versión: son `api/<recurso>` (`api/users`, `api/roles`, `api/me`, `api/settings`, `api/permissions`). Hoy no hay ningún cambio incompatible que sostener, así que versionar sería construir algo que nadie usa. Pero si la decisión no queda escrita, el día que haga falta cada uno puede inventar su propio esquema.

## Decisión

**No se versiona la API por ahora.** Las rutas siguen el formato `api/<recurso>`. El primer cambio incompatible introduce `Asp.Versioning`.

## Consecuencias

- Un recurso nuevo usa `api/<recurso>`, sin prefijo de versión ni header.
- Mientras no haya versiones, un cambio de un contrato que consume el front se revisa antes contra `../ArquitecturaBaseFront`.
- Cuando llegue el primer cambio incompatible, se usa `Asp.Versioning` y no un esquema propio, y se escribe un ADR nuevo que reemplace a este con el esquema elegido.

## Alternativas descartadas

- **Versionar desde ahora.** Es YAGNI: no hay ningún cambio incompatible que justifique el costo.
- **No decidir nada.** Deja la puerta abierta a que alguien invente un esquema propio (prefijo, header o query) el día que lo necesite.
