# 0006. Migraciones y seed fuera de Development

**Estado:** Aceptada, 2026-09-26.

**Origen:** decisión D6 del [plan maestro](../plans/2026-09-26-plantilla-estandar-por-etapas.md), que sale del code review del 2026-09-26. Se implementa en la tarea 4 de la Etapa 7.

## Contexto

- `Program.cs` aplica las migraciones y corre el seed al arrancar, **solo en Development**.
- El seed crea los roles con sus permisos, la fila de `SystemSettings` y el scope y el cliente `web` de OpenIddict. Fuera de Development no corre, así que una base nueva de otro ambiente queda sin esos datos.
- El seed ya es idempotente: si la fila existe, manda la base, y un despliegue nunca pisa lo que se configuró desde el panel.
- Aplicar las migraciones al arrancar no sirve con varias réplicas: cada una las correría en paralelo.
- El pipeline de `.github/workflows/deploy.yml` ya genera un bundle de migraciones y lo aplica antes de desplegar la imagen nueva.

## Decisión

**El seed idempotente corre en todos los ambientes.** Fuera de Development, **las migraciones se aplican con un bundle** (`dotnet ef migrations bundle`) como paso del despliegue, no al arrancar. En Development se siguen aplicando al iniciar la Api.

## Consecuencias

- El seed corre al arrancar en cada réplica y en cada reinicio, así que tiene que seguir siendo idempotente y no pisar lo que ya está en la base.
- La Api no migra sola fuera de Development: la base tiene que estar migrada antes de que arranque la versión nueva. Si una migración falla, el despliegue se corta y sigue la versión anterior.
- La Etapa 7 suma una guía de despliegue y un test de integración: arrancar en `Production` contra una base migrada y vacía tiene que dejar los roles, los ajustes y el cliente `web`.

## Alternativas descartadas

- **Aplicar las migraciones al arrancar en todos los ambientes.** Con varias réplicas corren en paralelo.
- **Dejar el seed solo en Development.** Una base nueva de otro ambiente queda sin roles, sin ajustes y sin el cliente `web`.
