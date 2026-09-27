# 0002. Contratos HTTP de entrada en `Api/Contracts`

**Estado:** Aceptada, 2026-09-26.

**Origen:** decisión D2 del [plan maestro](../plans/2026-09-26-plantilla-estandar-por-etapas.md), que sale del code review del 2026-09-26. Se aplicó en la Etapa 3, commit `e92d354`: los cuatro controllers que recibían modelos de Application pasaron a contratos propios, y `ControllerInputContractTests` lo hace cumplir. El contexto que sigue describe el estado anterior.

## Contexto

Hoy conviven dos estilos para la entrada de un controller:

- `UsersController` y `RolesController` reciben contratos de `Api/Contracts/<Área>` (por ejemplo `CreateUserHttpRequest` o `CreateRoleHttpRequest`) y los mapean a mano a los modelos de Application;
- `MeController`, `SettingsController`, `LoginCodeController` y `LoginLinkController` reciben directamente modelos de `Application/Models`.

En el segundo estilo, Application carga detalles de MVC. El caso más claro son los `ToString()` que ocultan datos personales: existen porque MVC registra los argumentos de una acción con `ToString()`, y hoy algunos viven en modelos de Application. Con dos estilos, además, no hay una regla que un test pueda verificar.

## Decisión

**Todo body y query de entrada tiene un contrato en `Api/Contracts/<Área>`**, con mapeo manual al modelo de Application. Las respuestas no llevan contrato propio: serializan los `*Response` de Application.

## Consecuencias

- Hay una sola regla, y un test de arquitectura la puede verificar: los parámetros `[FromBody]` y `[FromQuery]` de un controller son tipos de `Api.Contracts`.
- Los `ToString()` que ocultan datos personales se mudan a los contratos, y Application deja de conocer detalles de MVC.
- Cada ruta con entrada suma un contrato y un mapeo escrito a mano, como pide `AGENTS.md` para todos los mapeos.
- Pasar a contratos no tiene que cambiar el JSON que recibe la Api. Si un cambio toca un contrato que consume el front, se revisa antes el cliente en `../ArquitecturaBaseFront`.
- Como las respuestas salen de los `*Response` de Application, renombrar una propiedad de un `*Response` cambia el contrato HTTP.

## Alternativas descartadas

- **Mantener los dos estilos.** No hay una regla única ni un test que la sostenga, y cada área nueva tiene que elegir cuál copiar.
- **Que todos los controllers reciban los modelos de Application.** Application seguiría cargando detalles de MVC, como los `ToString()` que protegen los logs de la acción.
