# 0008. Nombres de repositorios y lectores

**Estado:** Aceptada, 2026-09-27.

**Origen:** tarea 5 de la Etapa 2 del [plan maestro](../plans/2026-09-26-plantilla-estandar-por-etapas.md). Se aplica en el [plan de la Etapa 2](../history/plans/2026-09-27-etapa-2-identity-solo-tecnico.md).

## Contexto

Los contratos de `Application/Interfaces/Persistence` usaban `Get` para cosas distintas: una entidad seguida para modificarla (`ILoginCodeRepository.GetLatestAsync`), una colección (`IPermissionReader.GetUserRoleIdsAsync`), un escalar sin seguimiento (`ILoginAuditRepository.GetLastSuccessAtUtcAsync`) y un valor cacheado (`ISystemSettingsReader.GetRegistrationModeAsync`). Los booleanos empezaban con `Is` o `Has`, o terminaban en `Exists`. Por el nombre no se podía saber si lo que volvía estaba seguido por EF, que es lo que decide si se puede modificar y dejar que lo baje el guardado final del límite.

## Decisión

El prefijo de un método de un repositorio o de un lector dice qué devuelve: `Get` una entidad de Domain seguida, solo en repositorios; `Find` una proyección, un registro o un escalar, nunca una entidad; `List` una colección o una página; `Exists` un `bool`; `Count` un número o un registro de conteos; `Lock` un lock de Postgres; `Add` un alta en el contexto; y los verbos de escritura (`Create`, `Update`, `Delete`, `Set`, `Remove`, `Restore`, `Clear`, `Add…Async`). Un lector solo lee y es el único que llama a `AsNoTracking`. La tabla completa está en [backend.md, "Nombres de repositorios y lectores"](../architecture/backend.md#nombres-de-repositorios-y-lectores).

## Consecuencias

- Se renombran nueve métodos en la Etapa 2, sin cambiar nada de HTTP.
- `IUserInvitationRepository.GetLatestAsync` y `GetLatestSentAsync` conservan el nombre y pasan a devolver la entidad seguida, como toda entidad que devuelve un repositorio.
- `PersistenceNamingTests` lo verifica por reflexión (el prefijo y el tipo de retorno) y por el IL (`AsNoTracking` solo en `Infrastructure/Persistence/Readers`): un método nuevo con otro prefijo rompe los tests.
- Excepciones conocidas: `ISystemSettingsReader.InvalidateAsync`, hasta que la Etapa 7 pase la invalidación al servicio, e `IUserReader.CountActiveAdminsAsync`, que sigue entidades desde un lector y pasa a un `COUNT` en SQL en la Etapa 7. `IRoleReader.FindRoleAsync` pasa a `FindByIdAsync` en la Etapa 4.
- Quedan afuera `IUnitOfWork` y los contratos de `Interfaces/Integrations`, que no son de persistencia.

## Alternativas descartadas

- **`FindLatest…` devolviendo una entidad sin seguimiento para las invitaciones.** Se lee bien, pero "`AsNoTracking` solo en lectores" deja de poder verificarse y pasa a depender de la revisión.
- **Escribir la convención sin un test.** Es la regla que más se degrada con el tiempo: cada método nuevo es una oportunidad de romperla sin que nadie lo note.
- **Una tabla sin los verbos de escritura ni `Lock`.** Dejaría afuera la mitad de los métodos, y el test no podría decir cuándo un nombre es desconocido.
