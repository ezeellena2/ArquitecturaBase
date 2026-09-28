# 0001. Transacción explícita por caso de uso

**Estado:** Aceptada, 2026-09-26; implementada el 2026-09-27.

**Origen:** decisión D1 del [plan maestro](../plans/2026-09-26-plantilla-estandar-por-etapas.md), que sale del code review del 2026-09-26. Se aplicó en la Etapa 1, cerrada el 2026-09-27 (commits `40d7669` a `4681d2e`), y el mismo día `0513c8a` sumó las escrituras de cuentas: `IUnitOfWork` quedó con un solo método, `ExecuteInTransactionAsync`, y lo hacen cumplir `TransactionBoundaryTests` (quién recibe la unidad de trabajo, quién abre el límite y quién guarda) y las escrituras que lanzan fuera de un límite (los locks, `IUserRepository`, `IRoleRepository` e `IIdentityService`). El contexto que sigue describe el estado anterior.

## Contexto

El code review encontró **dos formas de guardar**:

- los servicios de Application guardan con `IUnitOfWork.SaveChangesAsync`;
- `UserManager` y `RoleManager` guardan solos en cada llamada, porque los stores de Identity autoguardan (`AutoSaveChanges`).

Además, el límite transaccional de un caso de uso no se ve en el servicio:

- cuando un repositorio toma un lock (`pg_advisory_xact_lock`), abre una transacción, y `UnitOfWork.SaveChangesAsync` la confirma después;
- `RoleRepository` abre y confirma su propia transacción;
- sin un lock previo, un caso de uso que hace dos escrituras de Identity puede quedar a medias: la primera ya está guardada cuando falla la segunda.

## Decisión

Cada caso de uso que escribe tiene **una sola transacción, explícita y abierta desde el servicio**, con `IUnitOfWork.ExecuteInTransactionAsync`. Identity sigue autoguardando, pero adentro de esa transacción.

El diseño concreto (la firma con `CommitPolicy`, por qué una transacción ajena o anidada lanza en lugar de reutilizarse, y cómo un caso de uso pide con `CommitPolicy.OnAnyResult` que un `Result` fallido igual persista un intento) está en el [plan de la Etapa 1](../history/plans/2026-09-26-etapa-1-una-sola-forma-de-guardar.md).

## Consecuencias

- El límite transaccional se lee en el servicio y hay uno solo por caso de uso. Si falla la segunda escritura de Identity, se deshace también la primera.
- La transacción explícita formaliza lo que ya hacían los locks: los repositorios dejan de abrir su propia transacción y exigen la del caso de uso. El orden de los locks no cambia (primero los contactos, después la cuenta), porque de él dependen los tests de concurrencia de WhatsApp.
- `SignInManager` (bloqueo e intentos fallidos), el `SecurityStamp` y el `ConcurrencyStamp` se comportan igual que antes, porque Identity sigue guardando como hasta ahora.
- Sigue vigente la regla de `AGENTS.md`: cada servicio define expresamente cuándo guarda, incluidos los errores que tienen que persistir intentos o el consumo de un código.
- La migración es de riesgo alto: se pasa un servicio por vez y se corre la suite de integración completa después de cada uno.

## Alternativas descartadas

- **Apagar `AutoSaveChanges` en los stores de Identity** y guardar todo con `IUnitOfWork.SaveChangesAsync`. Cambia el comportamiento de `SignInManager` (bloqueo, intentos fallidos), del `SecurityStamp` y del `ConcurrencyStamp`: el riesgo de regresión es alto.
- **Dejar las dos formas de guardar.** Es el problema que encontró el code review: que un caso de uso sea atómico depende de que algún repositorio haya tomado un lock antes.

## Enmiendas

**Enmienda (2026-09-28, decisión P3 = B del usuario en la Etapa 7):** el seed de arranque también guarda en un límite, y es la única excepción con nombre a "solo un punto de entrada de un caso de uso abre el límite". `DatabaseSeeder` (`Infrastructure/Persistence/Seed`) recibe `IUnitOfWork` y corre `ExecuteInTransactionAsync` con `OnSuccess`: adentro toma el advisory lock `seed:database` y siembra los roles, los ajustes y OpenIddict. El motivo: el seed corre al arrancar en cada réplica (ADR 0006), así que tiene que ser atómico (un fallo no deja roles sin el cliente `web` ni una fila de ajustes suelta) y ponerse en fila entre réplicas, y eso es justo lo que da el límite. No es un caso de uso: no tiene contrato en `Interfaces/Services` ni un `Result` de negocio, sus errores son excepciones y el trabajo devuelve `Result.Success()` solo porque el límite pide un `Result`. Darle un contrato en Application para que entrara en la regla general habría inventado un caso de uso que ningún controller llama. `TransactionBoundaryTests` lo permite por nombre en `Only_use_case_entry_points_receive_the_unit_of_work` y `Only_use_case_entry_points_run_a_unit_of_work`, y afirma que lo ve, así la excepción no queda vieja; el seed ya no tiene excepción en `Only_the_unit_of_work_saves_the_context`, porque ningún seeder guarda por su cuenta.
