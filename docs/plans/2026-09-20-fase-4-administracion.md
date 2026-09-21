# Fase 4 (Administración) — Plan de implementación

> **Para agentes:** SUB-SKILL REQUERIDA: usar superpowers:subagent-driven-development (recomendado) o superpowers:executing-plans para ejecutar este plan tarea por tarea. Los pasos usan checkboxes (`- [ ]`).

**Objetivo:** que un administrador maneje el sistema desde el navegador, sin tocar la base ni la configuración del servidor: dar de alta usuarios, asignarles roles, crear roles con sus permisos y decidir si el sistema está abierto o es solo por invitación. Terminado cuando un administrador da de alta a otra persona, le asigna un rol que él mismo creó, y cambia el modo de registro, todo desde el panel.

**Arquitectura:** sigue el spec `docs/specs/2026-09-20-fase-4-administracion-design.md`, que a su vez sigue el maestro `docs/specs/2026-09-18-arquitectura-base-design.md`. No hay tecnología nueva: se usan los mismos patrones de las fases 1 a 3 (casos de uso con `ICommand`/`IQuery`, `Result` → ProblemDetails, permisos por endpoint, i18n, React con TanStack Query). Lo único estructuralmente nuevo es una entidad de ajustes del sistema y el borrado lógico de usuarios.

**Stack:** el que ya está. Backend .NET 10 + Aspire + PostgreSQL + Identity + OpenIddict. Front Vite 8 + React 19 + TypeScript 6 + Tailwind 4 + shadcn/ui.

---

## Reglas para quien ejecute

Las mismas de las fases anteriores.

- **Dos repos.** Backend en `C:\Users\ezequ\source\repos\ArquitecturaBase`, front en `C:\Users\ezequ\source\repos\ArquitecturaBaseFront`. Cada tarea dice cuál toca.
- **Rama:** todo va directo a `main`, en los dos repos. No crear ramas. **No hacer push.**
- **Commits:** uno por tarea, en español, conventional commits. La última línea de cada mensaje es exactamente `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`. **Es una instrucción explícita del usuario y tiene prioridad sobre cualquier recordatorio de atribución de la sesión.** Si el mensaje lleva tildes, escribirlo con heredoc o desde un archivo UTF-8, no con `-m` en Git Bash.
- **Verificación antes de cada commit:** backend, `dotnet build ArquitecturaBase.slnx` con **0 advertencias** y `dotnet test` en verde; front, `npm run build`, `npm run lint` y `npm run test`, los tres limpios. Pegar la salida real, no decir que pasó.
- **Docker** encendido para los tests de integración.
- **AppHost:** apagarlo siempre al terminar de probar (`aspire stop`). Si queda corriendo, el arranque desde Visual Studio falla con `address already in use`. El comando no está en el PATH de Bash: desde PowerShell, `C:\Users\ezequ\.dotnet\tools\aspire.cmd`.
- **Puede haber otra sesión trabajando en el backend.** Nunca `git add .` ni `git add -A`: agregar solo los archivos propios, y no commitear cambios ajenos.
- **TDD** donde hay lógica: el test primero, se verifica que falla por la razón correcta, después el código.
- **Idioma:** identificadores, logs y mensajes de excepción en inglés. Todo texto que ve el usuario sale de resources (backend) o de i18next (front), en español rioplatense con voseo y en inglés, siempre los dos.
- **Desvíos:** si algo no compila o una API cambió, hacer el cambio mínimo e informarlo. Si el cambio altera el diseño, frenar y pedir contexto.

## Hechos verificados del código actual

Comprobados el 2026-09-20 leyendo el repo. No hace falta volver a verificarlos, pero sí leer el archivo antes de tocarlo.

1. **`ApplicationUser : IdentityUser<Guid>, IAuditable`**: tiene auditoría, **no** implementa `ISoftDeletable`. La Tarea 6 se lo agrega.
2. **`ApplicationRole : IdentityRole<Guid>`** ya tiene `Description` (`DescriptionMaxLength = 256`) y **no** es auditable. La Tarea 11 le suma `IAuditable`.
3. **`Permissions`** (`Domain/Authorization/Permissions.cs`) declara `users.read`, `users.manage`, `roles.read`, `roles.manage` y los lista en `Permissions.All`. Los dos `.manage` están asignados al rol Admin y **no los usa ningún endpoint todavía**.
4. **`SystemRoles`** declara `Admin` y `User`.
5. **`IIdentityService`** (`Application/Abstractions/Identity/`) es la única puerta a Identity desde Application; hoy expone búsqueda, creación, roles, bloqueo, ingreso y `ListUsersAsync`. Cada tarea que necesite algo nuevo de Identity **le suma un método acá**, nunca usa `UserManager` fuera de Infrastructure.
6. **`UserAccount`** es el modelo que Application conoce del usuario: `(Guid Id, string Email, string? DisplayName, string Culture, string TimeZoneId, bool IsActive)`.
7. **El patrón de caso de uso** está en `Application/Features/Users/GetUsers/`: query, handler, validador y DTO, un archivo cada uno. Los comandos, en `Features/Auth/VerifyLoginCode/`. Los handlers se registran solos con Scrutor y quedan envueltos en logging → validación → unit of work.
8. **El patrón de endpoint** está en `Api/Endpoints/Users/UsersEndpoints.cs`: una clase `IEndpoint` por grupo, que se registra sola, recibe el handler por inyección y devuelve `result.ToHttpResult()`. Los parámetros del paginado se enlazan a mano porque `[AsParameters]` haría obligatorios los `int` de `PagedRequest`.
9. **`PermissionService`** cachea los permisos por rol en `HybridCache` con la clave `permissions:role:{roleId:N}` y los invalida con `InvalidateRoleAsync`. **Toda tarea que cambie los permisos de un rol tiene que llamarlo.**
10. **Los tests de integración** usan `ApiFactory` (WebApplicationFactory + Testcontainers). `AuthFlow.LoginAsync` hace el ingreso real; con el header `X-Test-UserId` se usa el usuario de prueba. `factory.EmailSender` guarda los emails enviados.
11. **`MigrationsTests`** falla si el modelo cambia y falta la migración: cada tarea que toque el modelo genera la suya.
12. **El front** tiene `usePagination` (página, orden y búsqueda en la URL), `DataTable`, `ConfirmDialog`, `Dialog`, `FormField`, `Switch`, `Checkbox`, `Badge` y los toasts, todos en `src/shared/ui`. Las claves de i18n van por módulo y `parity.test.ts` falla si una está en un idioma y no en el otro.

## Contratos

Estos tipos los usan varias tareas. **Se escriben una sola vez, con estos nombres exactos**, para que las tareas encajen entre sí.

### Domain

```csharp
// Domain/Settings/RegistrationMode.cs
namespace ArquitecturaBase.Domain.Settings;

/// <summary>Quién puede crear una cuenta (sección 4 del spec de la Fase 4).</summary>
public enum RegistrationMode
{
    /// <summary>Solo entra quien un administrador dio de alta.</summary>
    InviteOnly = 0,

    /// <summary>Cualquiera se crea la cuenta al ingresar por primera vez.</summary>
    Open = 1,
}
```

El permiso nuevo, en `Domain/Authorization/Permissions.cs`:

```csharp
    public static class Settings
    {
        public const string Manage = "settings.manage";
    }
```

y `Permissions.All` pasa a `[Users.Read, Users.Manage, Roles.Read, Roles.Manage, Settings.Manage]`.

### Application — usuarios

```csharp
public sealed record UserDetail(
    Guid Id,
    string Email,
    string? DisplayName,
    bool IsActive,
    DateTime CreatedAtUtc,
    IReadOnlyCollection<string> Roles);

public sealed record CreateUserCommand(string? Email, string? DisplayName, IReadOnlyCollection<string>? Roles)
    : ICommand<Guid>;

public sealed record UpdateUserCommand(Guid UserId, string? DisplayName, IReadOnlyCollection<string>? Roles) : ICommand;

public sealed record SetUserActiveCommand(Guid UserId, bool IsActive) : ICommand;

public sealed record DeleteUserCommand(Guid UserId) : ICommand;

public sealed record GetUserQuery(Guid UserId) : IQuery<UserDetail>;
```

### Application — roles y permisos

```csharp
public sealed record RoleListItem(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystemRole,
    int UserCount,
    IReadOnlyCollection<string> Permissions);

public sealed record GetRolesQuery : IQuery<IReadOnlyCollection<RoleListItem>>;

public sealed record CreateRoleCommand(string? Name, string? Description, IReadOnlyCollection<string>? Permissions)
    : ICommand<Guid>;

public sealed record UpdateRoleCommand(
    Guid RoleId,
    string? Name,
    string? Description,
    IReadOnlyCollection<string>? Permissions) : ICommand;

public sealed record DeleteRoleCommand(Guid RoleId) : ICommand;

/// <summary>Catálogo para armar la pantalla de roles: el código y su nombre traducido, agrupados por área.</summary>
public sealed record PermissionItem(string Code, string Name);

public sealed record PermissionGroup(string Area, string Name, IReadOnlyCollection<PermissionItem> Permissions);

public sealed record GetPermissionsQuery : IQuery<IReadOnlyCollection<PermissionGroup>>;
```

### Application — configuración y perfil

```csharp
public sealed record SystemSettingsResponse(RegistrationMode RegistrationMode);

public sealed record GetSystemSettingsQuery : IQuery<SystemSettingsResponse>;

public sealed record UpdateSystemSettingsCommand(RegistrationMode RegistrationMode) : ICommand;

public sealed record UpdateProfileCommand(string? DisplayName, string? Culture, string? TimeZoneId) : ICommand;
```

### Errores

Todos en Domain, con el formato `Area.Entidad.Motivo` y su clave en `Errors.resx` y `Errors.en.resx`:

El formato real del repo es `Area.Entidad.Motivo`, con las tres partes: `UserErrors.NotFoundCode` ya vale `Users.User.NotFound`. **Estos son los códigos exactos**, y son los que tienen que tener su clave en los dos `.resx`, porque `ErrorCodeTranslationTests` compara contra ellos:

| Código | Cuándo |
|---|---|
| `Users.User.AlreadyExists` | alta de un correo que ya tiene cuenta activa |
| `Users.User.NotFound` | id que no existe |
| `Users.User.CannotModifySelf` | desactivar o eliminar la propia cuenta, o quitarse el rol Admin |
| `Users.User.LastAdmin` | la acción dejaría al sistema sin ningún administrador activo |
| `Roles.Role.NotFound` | id que no existe |
| `Roles.Role.AlreadyExists` | nombre repetido |
| `Roles.Role.SystemRoleCannotChange` | renombrar o borrar `Admin` o `User`, o editar los permisos de `Admin` |
| `Roles.Role.HasUsers` | borrar un rol con usuarios asignados |
| `Auth.Account.NotInvited` | ingreso con Google, en modo `InviteOnly`, de un correo sin cuenta |
| `Settings.System.NotFound` | no existe la fila de ajustes (defensivo) |

Un usuario borrado que intenta ingresar recibe el `Auth.Account.Disabled` que ya existe: no hace falta vocabulario nuevo.

## Estructura de archivos

Backend, lo que se agrega:

```
src/ArquitecturaBase.Domain/
  Settings/RegistrationMode.cs · SystemSettings.cs · ISystemSettingsRepository.cs · SettingsErrors.cs
  Users/UserErrors.cs                                  (se le suman los códigos nuevos)
  Authorization/RoleErrors.cs · Permissions.cs         (se le suma settings.manage)
src/ArquitecturaBase.Application/
  Abstractions/Identity/IIdentityService.cs            (métodos nuevos)
  Abstractions/Settings/ISystemSettingsReader.cs       (lectura cacheada, para el ingreso)
  Features/Users/{CreateUser,UpdateUser,SetUserActive,DeleteUser,GetUser}/
  Features/Roles/{GetRoles,CreateRole,UpdateRole,DeleteRole,GetPermissions}/
  Features/Settings/{GetSystemSettings,UpdateSystemSettings}/
  Features/Users/UpdateProfile/
src/ArquitecturaBase.Infrastructure/
  Identity/IdentityService.cs                          (implementa los métodos nuevos)
  Settings/SystemSettingsRepository.cs · SystemSettingsReader.cs
  Persistence/Configurations/SystemSettingsConfiguration.cs
  Persistence/Seed/SystemSettingsSeeder.cs
  Persistence/Migrations/…                             (tres: ajustes, borrado lógico y auditoría de roles)
src/ArquitecturaBase.Api/Endpoints/
  Users/UsersEndpoints.cs · Users/MeEndpoint.cs        (se les suman rutas)
  Roles/RolesEndpoints.cs · Settings/SettingsEndpoints.cs
tests/…                                                (unitarios de las reglas e integración por endpoint)
```

Front, lo que se agrega:

```
src/features/users/
  api/users.ts                                         (se le suman las mutaciones)
  components/{UserFormDialog,UserRolesDialog}.tsx
src/features/roles/
  api/roles.ts · pages/RolesPage.tsx · components/RoleFormDialog.tsx
src/features/settings/
  api/settings.ts · pages/SettingsPage.tsx
src/locales/{es,en}/{roles,settings}.json             (y claves nuevas en users.json y common.json)
src/layouts/navigation.ts                              (entradas de Roles y Configuración)
src/app/routes.tsx                                     (rutas nuevas con su permiso)
```

## Tareas

| # | Tarea | Repo | Tests |
|---|---|---|---|
| 1 | Ajustes del sistema: entidad, lectura cacheada y seed | backend | unitarios + integración |
| 2 | El modo de registro decide si se manda el código | backend | integración |
| 3 | El modo de registro en el ingreso con Google | backend | integración |
| 4 | Endpoints de configuración y el permiso `settings.manage` | backend | integración |
| 5 | Las reglas que protegen al último administrador | backend | unitarios |
| 6 | Borrado lógico de usuarios | backend | integración |
| 7 | Alta de usuarios | backend | integración |
| 8 | Edición: nombre y roles | backend | integración |
| 9 | Activar y desactivar, cortando el acceso de verdad | backend | integración |
| 10 | Eliminar usuarios | backend | integración |
| 11 | Roles: listado y catálogo de permisos | backend | integración |
| 12 | Roles: crear, editar y borrar | backend | integración |
| 13 | Perfil propio: guardar idioma y último ingreso | backend | integración |
| 14 | Pantalla de usuarios: alta, roles y estado | front | Vitest |
| 15 | Pantalla de roles | front | Vitest |
| 16 | Pantalla de configuración y el menú | front | Vitest |
| 17 | Perfil propio: idioma guardado en la cuenta y último ingreso | front | Vitest |
| 18 | Documentación y verificación final | los dos | manual + comandos |

---
### Tarea 1: Ajustes del sistema: entidad, lectura cacheada y seed

Repo: **backend**.

Toda la fase cuelga de acá: el modo de registro decide quién puede crear una cuenta, y las tareas 2, 3 y 4 lo leen. La entidad es de una sola fila (clave primaria fija más un CHECK en la base), se lee cacheada con `HybridCache` igual que los permisos, y la fila la crea el seed con `Registration:Mode`, sin pisar nunca lo que ya esté guardado.

**Archivos:**
- Crear: `src/ArquitecturaBase.Domain/Settings/RegistrationMode.cs`
- Crear: `src/ArquitecturaBase.Domain/Settings/SystemSettings.cs`
- Crear: `src/ArquitecturaBase.Domain/Settings/ISystemSettingsRepository.cs`
- Crear: `src/ArquitecturaBase.Domain/Settings/SettingsErrors.cs`
- Crear: `src/ArquitecturaBase.Application/Abstractions/Settings/ISystemSettingsReader.cs`
- Crear: `src/ArquitecturaBase.Infrastructure/Settings/RegistrationOptions.cs`
- Crear: `src/ArquitecturaBase.Infrastructure/Settings/SystemSettingsRepository.cs`
- Crear: `src/ArquitecturaBase.Infrastructure/Settings/SystemSettingsReader.cs`
- Crear: `src/ArquitecturaBase.Infrastructure/Persistence/Configurations/SystemSettingsConfiguration.cs`
- Crear: `src/ArquitecturaBase.Infrastructure/Persistence/Seed/SystemSettingsSeeder.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/ApplicationDbContext.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Seed/SeedExtensions.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/DependencyInjection.cs`
- Modificar: `src/ArquitecturaBase.Application/Resources/Errors.resx`
- Modificar: `src/ArquitecturaBase.Application/Resources/Errors.en.resx`
- Crear: `src/ArquitecturaBase.Infrastructure/Persistence/Migrations/<timestamp>_SystemSettings.cs` (lo genera `dotnet ef`)
- Test: `tests/ArquitecturaBase.Domain.UnitTests/Settings/SystemSettingsTests.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Support/ApiFactory.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Support/RegistrationModeScope.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Settings/SystemSettingsSeedTests.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Settings/SystemSettingsReaderTests.cs`

- [x] **Paso 1: el test unitario de la entidad**

Crear `tests/ArquitecturaBase.Domain.UnitTests/Settings/SystemSettingsTests.cs`:

```csharp
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Domain.UnitTests.Settings;

public sealed class SystemSettingsTests
{
    [Fact]
    public void Settings_always_use_the_same_fixed_id()
    {
        var settings = SystemSettings.Create(RegistrationMode.Open);

        Assert.Equal(SystemSettings.SingletonId, settings.Id);
        Assert.Equal(RegistrationMode.Open, settings.RegistrationMode);
    }

    [Fact]
    public void Two_instances_share_the_id_so_the_table_can_only_have_one_row()
    {
        var first = SystemSettings.Create(RegistrationMode.InviteOnly);
        var second = SystemSettings.Create(RegistrationMode.Open);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Invite_only_is_the_default_value_of_the_enum()
    {
        Assert.Equal(RegistrationMode.InviteOnly, default(RegistrationMode));
    }

    [Fact]
    public void Registration_mode_can_be_changed()
    {
        var settings = SystemSettings.Create(RegistrationMode.InviteOnly);

        settings.SetRegistrationMode(RegistrationMode.Open);

        Assert.Equal(RegistrationMode.Open, settings.RegistrationMode);
    }
}
```

- [x] **Paso 2: correrlo y ver que falla**

```bash
dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj -- --filter-class "ArquitecturaBase.Domain.UnitTests.Settings.SystemSettingsTests"
```

Tiene que fallar al compilar, con `error CS0234: The type or namespace name 'Settings' does not exist in the namespace 'ArquitecturaBase.Domain'`.

- [x] **Paso 3: el enum, la entidad, el repositorio, los errores y el lector**

`src/ArquitecturaBase.Domain/Settings/RegistrationMode.cs`:

```csharp
namespace ArquitecturaBase.Domain.Settings;

/// <summary>Quién puede crear una cuenta (sección 4 del spec de la Fase 4).</summary>
public enum RegistrationMode
{
    /// <summary>Solo entra quien un administrador dio de alta.</summary>
    InviteOnly = 0,

    /// <summary>Cualquiera se crea la cuenta al ingresar por primera vez.</summary>
    Open = 1,
}
```

`src/ArquitecturaBase.Domain/Settings/SystemSettings.cs`:

```csharp
using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Settings;

/// <summary>
/// Ajustes del sistema. Es una tabla de una sola fila y la garantía es la clave primaria: siempre vale
/// <see cref="SingletonId"/>, así que un segundo INSERT choca con la PK. Es auditable porque un ajuste que decide
/// quién entra tiene que dejar rastro de quién lo cambió y cuándo (sección 5 del spec de la Fase 4).
/// Cada ajuste nuevo es una propiedad con su tipo y su migración, no un par clave-valor sin forma.
/// </summary>
public sealed class SystemSettings : Entity, IAuditable
{
    /// <summary>La clave de la única fila, como texto, para poder escribirla en el CHECK de la base.</summary>
    public const string SingletonIdValue = "00000000-0000-0000-0000-000000000001";

    /// <summary>La clave de la única fila.</summary>
    public static readonly Guid SingletonId = new(SingletonIdValue);

    // Para EF Core y para Create.
    private SystemSettings()
        : base(SingletonId)
    {
    }

    public RegistrationMode RegistrationMode { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? ModifiedAtUtc { get; private set; }

    public Guid? ModifiedBy { get; private set; }

    public static SystemSettings Create(RegistrationMode registrationMode) =>
        new() { RegistrationMode = registrationMode };

    public void SetRegistrationMode(RegistrationMode registrationMode) => RegistrationMode = registrationMode;
}
```

`src/ArquitecturaBase.Domain/Settings/ISystemSettingsRepository.cs`:

```csharp
namespace ArquitecturaBase.Domain.Settings;

/// <summary>La única fila de ajustes. No hay listado ni borrado: la fila se crea una vez y después se edita.</summary>
public interface ISystemSettingsRepository
{
    /// <summary>La fila de ajustes, o null si el seed todavía no la creó.</summary>
    Task<SystemSettings?> GetAsync(CancellationToken cancellationToken);

    void Add(SystemSettings settings);
}
```

`src/ArquitecturaBase.Domain/Settings/SettingsErrors.cs`:

```csharp
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Settings;

public static class SettingsErrors
{
    public const string NotFoundCode = "Settings.System.NotFound";

    /// <summary>No existe la fila de ajustes. La crea el seed: si falta, la base quedó a medio preparar.</summary>
    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The system settings were not found.");
}
```

`src/ArquitecturaBase.Application/Abstractions/Settings/ISystemSettingsReader.cs`:

```csharp
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Abstractions.Settings;

/// <summary>
/// Lectura cacheada de los ajustes, para el camino del ingreso: se consulta en cada pedido de código y en cada
/// ingreso con Google, así que no puede pegarle a la base todas las veces. Lo implementa Infrastructure sobre
/// HybridCache, igual que los permisos por rol.
/// </summary>
public interface ISystemSettingsReader
{
    Task<RegistrationMode> GetRegistrationModeAsync(CancellationToken cancellationToken);

    /// <summary>Descarta lo cacheado: lo que se cambia desde el panel vale al instante, sin reiniciar nada.</summary>
    Task InvalidateAsync(CancellationToken cancellationToken);
}
```

- [x] **Paso 4: la traducción del error nuevo**

En `src/ArquitecturaBase.Application/Resources/Errors.resx`, antes de `</root>`:

```xml
  <data name="Settings.System.NotFound" xml:space="preserve"><value>No encontramos la configuración del sistema.</value></data>
```

En `src/ArquitecturaBase.Application/Resources/Errors.en.resx`, antes de `</root>`:

```xml
  <data name="Settings.System.NotFound" xml:space="preserve"><value>We couldn't find the system settings.</value></data>
```

- [x] **Paso 5: los tests unitarios y los de recursos en verde**

```bash
dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj -- --filter-class "ArquitecturaBase.Domain.UnitTests.Settings.SystemSettingsTests"
dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Resources.ErrorCodeTranslationTests"
```

Los dos tienen que decir `Passed!` (el segundo prueba que `Settings.System.NotFound` tiene texto en los dos idiomas; sin el paso 4 diría `Missing Spanish text for Settings.System.NotFound`).

- [x] **Paso 6: la configuración de EF y el DbSet**

`src/ArquitecturaBase.Infrastructure/Persistence/Configurations/SystemSettingsConfiguration.cs`:

```csharp
using ArquitecturaBase.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Persistence.Configurations;

/// <summary>
/// Tabla de una sola fila. La clave primaria ya lo garantiza desde el modelo; el CHECK lo garantiza también contra
/// un INSERT hecho a mano en la base. El modo se guarda como texto, como el método de ingreso en LoginAudits: se
/// lee de una consulta SQL sin tener que traducir un número.
/// </summary>
internal sealed class SystemSettingsConfiguration : IEntityTypeConfiguration<SystemSettings>
{
    public const int RegistrationModeMaxLength = 20;
    public const string SingleRowConstraintName = "CK_SystemSettings_SingleRow";

    public void Configure(EntityTypeBuilder<SystemSettings> builder)
    {
        builder.Property(settings => settings.RegistrationMode)
            .HasConversion<string>()
            .HasMaxLength(RegistrationModeMaxLength);

        builder.ToTable(table => table.HasCheckConstraint(
            SingleRowConstraintName,
            "\"Id\" = '" + SystemSettings.SingletonIdValue + "'"));
    }
}
```

En `src/ArquitecturaBase.Infrastructure/Persistence/ApplicationDbContext.cs`, agregar el using `using ArquitecturaBase.Domain.Settings;` y el DbSet debajo de `LoginAudits`:

```csharp
    public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();
```

- [x] **Paso 7: la migración**

Con Docker encendido no hace falta: `migrations add` no se conecta, solo construye el modelo.

```bash
dotnet ef migrations add SystemSettings --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api --output-dir Persistence/Migrations -- --environment Development --ConnectionStrings:appdb "Host=localhost;Port=5433;Database=appdb;Username=postgres;Password=postgres"
```

Tiene que terminar con:

```
Build succeeded.
Done. To undo this action, use 'ef migrations remove'
```

y dejar `Persistence/Migrations/<timestamp>_SystemSettings.cs`, su `.Designer.cs` y el snapshot actualizado. Abrir el `.cs` y comprobar que crea la tabla `SystemSettings` con `Id`, `RegistrationMode` (`character varying(20)`), `CreatedAtUtc`, `CreatedBy`, `ModifiedAtUtc`, `ModifiedBy` y la restricción `CK_SystemSettings_SingleRow`.

- [x] **Paso 8: el arnés arranca abierto**

En `tests/ArquitecturaBase.Api.IntegrationTests/Support/ApiFactory.cs`, dentro de `ConfigureWebHost`, debajo de `builder.UseSetting("Seed:AdminEmail", AdminEmail);`:

```csharp
        // El arnés arranca abierto: casi todos los tests de las fases 1 a 3 ingresan con un correo nuevo y esperan
        // que la cuenta se cree sola. Los tests del modo de registro lo cambian con RegistrationModeScope, y el
        // valor por defecto (InviteOnly) se prueba sobre bases vacías en SystemSettingsSeedTests.
        builder.UseSetting("Registration:Mode", "Open");
```

Sin esta línea, a partir de la Tarea 2 fallan con `TimeoutException` todos los tests que ingresan con `TestEmails.Unique(...)`: `LoginCodeEndpointsTests`, `ConnectFlowTests`, `LoginCodeConcurrencyTests`, `UsersEndpointsTests` y `ExternalLoginTests`, entre otros.

- [x] **Paso 9: los tests de integración del seed**

Crear `tests/ArquitecturaBase.Api.IntegrationTests/Settings/SystemSettingsSeedTests.cs`:

```csharp
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Settings;

/// <summary>
/// El seed de los ajustes se prueba sobre bases vacías: la de la Api compartida ya tiene la fila creada y los
/// tests de los modos de registro la usan.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class SystemSettingsSeedTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Seed_creates_a_single_row_with_the_configured_mode()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting($"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("settings"))
            .UseSetting("Registration:Mode", "Open"));
        await using var scope = api.Services.CreateAsyncScope();
        await using var dbContext = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());

        try
        {
            await dbContext.Database.MigrateAsync(Ct);
            await api.Services.SeedDatabaseAsync(Ct);

            var settings = await dbContext.SystemSettings.AsNoTracking().SingleAsync(Ct);
            Assert.Equal(SystemSettings.SingletonId, settings.Id);
            Assert.Equal(RegistrationMode.Open, settings.RegistrationMode);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync(Ct);
        }
    }

    [Fact]
    public async Task Without_configuration_the_system_starts_closed_and_seeding_again_does_not_overwrite_it()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting($"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("settings")));
        await using var scope = api.Services.CreateAsyncScope();
        await using var dbContext = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());

        try
        {
            await dbContext.Database.MigrateAsync(Ct);
            await api.Services.SeedDatabaseAsync(Ct);

            var seeded = await dbContext.SystemSettings.SingleAsync(Ct);
            Assert.Equal(RegistrationMode.InviteOnly, seeded.RegistrationMode);

            // Lo que se cambió desde el panel: un despliegue posterior no lo puede pisar.
            seeded.SetRegistrationMode(RegistrationMode.Open);
            await dbContext.SaveChangesAsync(Ct);

            await api.Services.SeedDatabaseAsync(Ct);

            var current = await dbContext.SystemSettings.AsNoTracking().SingleAsync(Ct);
            Assert.Equal(RegistrationMode.Open, current.RegistrationMode);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync(Ct);
        }
    }

    [Fact]
    public async Task The_shared_api_is_seeded_from_its_configuration()
    {
        var settings = await factory.ExecuteDbContextAsync(db => db.SystemSettings.AsNoTracking().SingleAsync(Ct));

        // El arnés fija Registration:Mode = Open (ApiFactory).
        Assert.Equal(SystemSettings.SingletonId, settings.Id);
        Assert.Equal(RegistrationMode.Open, settings.RegistrationMode);
    }
}
```

Correrlos:

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Settings.SystemSettingsSeedTests"
```

Los tres tienen que fallar con `System.InvalidOperationException : Sequence contains no elements`: la tabla existe pero nadie crea la fila.

- [x] **Paso 10: las opciones, el repositorio, el seeder y sus registraciones**

`src/ArquitecturaBase.Infrastructure/Settings/RegistrationOptions.cs`:

```csharp
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Infrastructure.Settings;

/// <summary>
/// Valor inicial del modo de registro, en Registration:Mode. Solo lo usa el seed al crear la fila: después manda
/// la base, porque el modo se cambia desde el panel (sección 5 del spec de la Fase 4).
/// </summary>
internal sealed class RegistrationOptions
{
    public const string SectionName = "Registration";

    public RegistrationMode Mode { get; init; } = RegistrationMode.InviteOnly;
}
```

`src/ArquitecturaBase.Infrastructure/Settings/SystemSettingsRepository.cs`:

```csharp
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Settings;

internal sealed class SystemSettingsRepository(ApplicationDbContext dbContext) : ISystemSettingsRepository
{
    public Task<SystemSettings?> GetAsync(CancellationToken cancellationToken) =>
        dbContext.SystemSettings.FirstOrDefaultAsync(cancellationToken);

    public void Add(SystemSettings settings) => dbContext.SystemSettings.Add(settings);
}
```

`src/ArquitecturaBase.Infrastructure/Persistence/Seed/SystemSettingsSeeder.cs`:

```csharp
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Infrastructure.Settings;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Persistence.Seed;

/// <summary>
/// Crea la fila de ajustes con el valor de Registration:Mode. Si ya existe, manda la base: un despliegue nunca
/// pisa lo que se configuró desde el panel (sección 5 del spec de la Fase 4).
/// </summary>
internal sealed class SystemSettingsSeeder(
    ApplicationDbContext dbContext,
    ISystemSettingsRepository repository,
    IOptions<RegistrationOptions> options)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await repository.GetAsync(cancellationToken) is not null)
        {
            return;
        }

        repository.Add(SystemSettings.Create(options.Value.Mode));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

En `src/ArquitecturaBase.Infrastructure/Persistence/Seed/SeedExtensions.cs`, agregar el seeder entre los dos que ya están (queda después de `RoleSeeder` y antes de `OpenIddictSeeder`):

```csharp
        await scope.ServiceProvider.GetRequiredService<RoleSeeder>().SeedAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<SystemSettingsSeeder>().SeedAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<OpenIddictSeeder>().SeedAsync(cancellationToken);
```

En `src/ArquitecturaBase.Infrastructure/DependencyInjection.cs`, agregar los usings

```csharp
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using ArquitecturaBase.Infrastructure.Settings;
```

y, justo debajo de `services.AddScoped<ILoginAuditRepository, LoginAuditRepository>();`:

```csharp
        services.AddOptions<RegistrationOptions>()
            .BindConfiguration(RegistrationOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<ISystemSettingsRepository, SystemSettingsRepository>();
        services.AddScoped<SystemSettingsSeeder>();
```

- [x] **Paso 11: los tests del seed en verde**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Settings.SystemSettingsSeedTests"
```

`Passed! - Failed: 0, Passed: 3`.

- [x] **Paso 12: el helper de los tests y el test del lector cacheado**

`tests/ArquitecturaBase.Api.IntegrationTests/Support/RegistrationModeScope.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Settings;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// Cambia el modo de registro de la base (y descarta el caché del lector) mientras dure el scope, y lo deja como
/// estaba al salir. Los tests de la colección corren en serie, así que no se pisan entre ellos.
/// </summary>
internal sealed class RegistrationModeScope(ApiFactory factory, RegistrationMode previous) : IAsyncDisposable
{
    public static async Task<RegistrationModeScope> SetAsync(ApiFactory factory, RegistrationMode mode) =>
        new(factory, await ApplyAsync(factory, mode));

    public ValueTask DisposeAsync() => new(ApplyAsync(factory, previous));

    private static Task<RegistrationMode> ApplyAsync(ApiFactory factory, RegistrationMode mode) =>
        factory.ExecuteScopeAsync(async services =>
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var dbContext = services.GetRequiredService<ApplicationDbContext>();
            var settings = await dbContext.SystemSettings.SingleAsync(cancellationToken);
            var previousMode = settings.RegistrationMode;

            settings.SetRegistrationMode(mode);
            await dbContext.SaveChangesAsync(cancellationToken);
            await services.GetRequiredService<ISystemSettingsReader>().InvalidateAsync(cancellationToken);

            return previousMode;
        });
}
```

`tests/ArquitecturaBase.Api.IntegrationTests/Settings/SystemSettingsReaderTests.cs`:

```csharp
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Abstractions.Settings;
using ArquitecturaBase.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Settings;

[Collection(ApiTestGroup.Name)]
public sealed class SystemSettingsReaderTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_registration_mode_is_cached_until_it_is_invalidated()
    {
        // Al salir del scope, la fila y el caché vuelven a como estaban (el arnés arranca en Open).
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        Assert.Equal(RegistrationMode.InviteOnly, await ReadModeAsync());

        // Se cambia la fila por afuera del panel: el lector sigue devolviendo lo que tiene cacheado.
        await UpdateRowAsync(RegistrationMode.Open);
        Assert.Equal(RegistrationMode.InviteOnly, await ReadModeAsync());

        await InvalidateAsync();

        Assert.Equal(RegistrationMode.Open, await ReadModeAsync());
    }

    private Task<RegistrationMode> ReadModeAsync() =>
        factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<ISystemSettingsReader>().GetRegistrationModeAsync(Ct));

    private Task<bool> UpdateRowAsync(RegistrationMode mode) =>
        factory.ExecuteDbContextAsync(async dbContext =>
        {
            var settings = await dbContext.SystemSettings.SingleAsync(Ct);
            settings.SetRegistrationMode(mode);
            await dbContext.SaveChangesAsync(Ct);

            return true;
        });

    private Task<bool> InvalidateAsync() =>
        factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<ISystemSettingsReader>().InvalidateAsync(Ct);

            return true;
        });
}
```

Correrlo:

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Settings.SystemSettingsReaderTests"
```

Tiene que fallar con `System.InvalidOperationException : No service for type 'ArquitecturaBase.Application.Abstractions.Settings.ISystemSettingsReader' has been registered.`

- [x] **Paso 13: el lector cacheado**

El TTL es de **60 segundos**, no de una hora como el de los permisos por rol, y el comentario del código lo dice para que nadie lo "optimice" después: la invalidación al guardar corre dentro del handler, antes de que `UnitOfWorkDecorator` confirme, así que una lectura que caiga en esa ventana de milisegundos volvería a cachear el valor viejo. Con 60 segundos, el peor caso dura un minuto en lugar de una hora, y un valor que se lee una vez por ingreso no necesita más.

`src/ArquitecturaBase.Infrastructure/Settings/SystemSettingsReader.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Settings;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace ArquitecturaBase.Infrastructure.Settings;

/// <summary>
/// Mismo patrón que PermissionService: el valor se cachea en HybridCache y se descarta explícitamente cuando
/// cambia. Sin fila, devuelve InviteOnly, que es el modo cerrado: ante la duda, el sistema no se abre solo.
/// </summary>
internal sealed class SystemSettingsReader(ApplicationDbContext dbContext, HybridCache cache) : ISystemSettingsReader
{
    public const string CacheKey = "settings:system";

    /// <summary>
    /// Un minuto, y no una hora como los permisos por rol, a propósito: <b>no subirlo</b>. El comando que guarda
    /// los ajustes descarta el caché antes de que UnitOfWorkDecorator confirme el guardado, así que una lectura
    /// que caiga justo en esa ventana vuelve a cachear el valor viejo; el TTL es el techo de cuánto puede durar
    /// eso. Un ajuste que se lee una vez por ingreso no gana nada con una hora de caché.
    /// </summary>
    private static readonly HybridCacheEntryOptions CacheEntryOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(60),
        LocalCacheExpiration = TimeSpan.FromSeconds(60),
    };

    public async Task<RegistrationMode> GetRegistrationModeAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            CacheKey,
            dbContext,
            static async (context, token) => await context.SystemSettings
                .AsNoTracking()
                .Select(settings => settings.RegistrationMode)
                .FirstOrDefaultAsync(token),
            CacheEntryOptions,
            cancellationToken: cancellationToken);

    public async Task InvalidateAsync(CancellationToken cancellationToken) =>
        await cache.RemoveAsync(CacheKey, cancellationToken);
}
```

En `src/ArquitecturaBase.Infrastructure/DependencyInjection.cs`, agregar el using `using ArquitecturaBase.Application.Abstractions.Settings;` y la registración junto a las del paso 9:

```csharp
        services.AddScoped<ISystemSettingsReader, SystemSettingsReader>();
```

- [x] **Paso 14: el test del lector en verde**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Settings.SystemSettingsReaderTests"
```

`Passed! - Failed: 0, Passed: 1`.

- [x] **Paso 15: build y suite completa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

El build, con `0 Warning(s)` y `0 Error(s)`. La suite, en verde: `MigrationsTests.Model_has_no_pending_changes` confirma que la migración del paso 7 cubre el modelo.

- [x] **Paso 16: commit**

```bash
git add src/ArquitecturaBase.Domain/Settings src/ArquitecturaBase.Application/Abstractions/Settings src/ArquitecturaBase.Application/Resources/Errors.resx src/ArquitecturaBase.Application/Resources/Errors.en.resx src/ArquitecturaBase.Infrastructure/Settings src/ArquitecturaBase.Infrastructure/Persistence/Configurations/SystemSettingsConfiguration.cs src/ArquitecturaBase.Infrastructure/Persistence/Seed/SystemSettingsSeeder.cs src/ArquitecturaBase.Infrastructure/Persistence/Seed/SeedExtensions.cs src/ArquitecturaBase.Infrastructure/Persistence/ApplicationDbContext.cs src/ArquitecturaBase.Infrastructure/Persistence/Migrations src/ArquitecturaBase.Infrastructure/DependencyInjection.cs tests/ArquitecturaBase.Domain.UnitTests/Settings tests/ArquitecturaBase.Api.IntegrationTests/Settings tests/ArquitecturaBase.Api.IntegrationTests/Support/ApiFactory.cs tests/ArquitecturaBase.Api.IntegrationTests/Support/RegistrationModeScope.cs
git commit -m "feat: agregar los ajustes del sistema con el modo de registro" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 2: El modo de registro decide si se manda el código

Repo: **backend**.

En `InviteOnly`, un correo sin cuenta tiene que recibir **el mismo `202` de siempre y ningún email**. Si el endpoint respondiera distinto, o si mandara el correo, cualquiera podría averiguar qué direcciones están registradas; y mandarle un código a quien no puede entrar es correo inútil. Es el paso más fácil de hacer mal de todo el grupo.

**Lo único que cambia es que no se encola el email: el código se genera y se guarda igual.** Es contraintuitivo y es a propósito (sección 4 del spec). Los límites por dirección —el de reenvío y el de la ventana— se apoyan en las filas de `LoginCodes`, así que saltearlas haría que una dirección registrada empiece a responder `429` al insistir mientras una desconocida responde `202` para siempre: esa diferencia sola alcanza para enumerar qué correos tienen cuenta, que es justo lo que este modo tiene que impedir. Las filas que nadie usa vencen solas a los 10 minutos.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Application/Features/Auth/RequestLoginCode/RequestLoginCodeCommandHandler.cs`
- Test: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/AuthFakes.cs`
- Test: `tests/ArquitecturaBase.Application.UnitTests/Features/Auth/RequestLoginCodeCommandHandlerTests.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/RegistrationModeTests.cs`

- [x] **Paso 1: el doble del lector de ajustes**

Al final de `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/AuthFakes.cs`, después de `FakePermissionService`:

```csharp
internal sealed class FakeSystemSettingsReader : ISystemSettingsReader
{
    public RegistrationMode Mode { get; set; } = RegistrationMode.Open;

    public int Invalidations { get; private set; }

    public Task<RegistrationMode> GetRegistrationModeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Mode);

    public Task InvalidateAsync(CancellationToken cancellationToken)
    {
        Invalidations++;

        return Task.CompletedTask;
    }
}
```

y sumar los usings que faltan arriba del archivo:

```csharp
using ArquitecturaBase.Application.Abstractions.Settings;
using ArquitecturaBase.Domain.Settings;
```

El valor por defecto del doble es `Open` a propósito: así los tests que ya existen siguen probando el camino de siempre y solo los nuevos ponen `InviteOnly`.

- [x] **Paso 2: los tests unitarios del caso de uso**

En `tests/ArquitecturaBase.Application.UnitTests/Features/Auth/RequestLoginCodeCommandHandlerTests.cs`, agregar el campo y pasarlo al handler:

```csharp
    private readonly FakeSystemSettingsReader _settings = new();
```

y el constructor pasa a ser:

```csharp
    public RequestLoginCodeCommandHandlerTests()
    {
        _handler = new RequestLoginCodeCommandHandler(
            _loginCodes,
            _identity,
            new FakeLoginCodeGenerator(),
            new FakeLoginCodeHasher(),
            _renderer,
            _emailQueue,
            _settings,
            Options.Create(new LoginCodeOptions()),
            _clock);
    }
```

Agregar al final de la clase los dos tests nuevos:

```csharp
    [Fact]
    public async Task Invite_only_issues_the_code_but_sends_no_email_for_an_unknown_email()
    {
        _settings.Mode = RegistrationMode.InviteOnly;

        var result = await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        // La misma respuesta de siempre: responder distinto diría qué direcciones están registradas.
        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value.ResendAfterSeconds);

        // El código se emite igual: es lo que hace que los límites por dirección sigan valiendo.
        Assert.Single(_loginCodes.Codes);
        Assert.Empty(_emailQueue.Messages);
    }

    [Fact]
    public async Task Invite_only_still_emails_an_account_that_exists()
    {
        _settings.Mode = RegistrationMode.InviteOnly;
        _identity.AddUser(UserEmail);

        var result = await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        Assert.True(result.IsSuccess);
        Assert.Single(_loginCodes.Codes);
        Assert.Single(_emailQueue.Messages);
    }

    [Fact]
    public async Task Invite_only_applies_the_resend_limit_to_an_unknown_email_too()
    {
        _settings.Mode = RegistrationMode.InviteOnly;
        await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);
        _clock.Advance(TimeSpan.FromSeconds(20));

        var result = await _handler.Handle(new RequestLoginCodeCommand(UserEmail), Ct);

        // Exactamente el mismo error, y los mismos segundos, que recibe una dirección registrada al insistir.
        Assert.Equal(LoginCodeErrors.ResendTooSoonCode, result.Error.Code);
        Assert.Equal(40, result.Error.Metadata![LoginCodeErrors.RetryAfterKey]);
        Assert.Single(_loginCodes.Codes);
        Assert.Empty(_emailQueue.Messages);
    }
```

y el using `using ArquitecturaBase.Domain.Settings;`.

- [x] **Paso 3: correrlos y ver que fallan**

```bash
dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Features.Auth.RequestLoginCodeCommandHandlerTests"
```

Tiene que fallar al compilar, con `error CS1729: 'RequestLoginCodeCommandHandler' does not contain a constructor that takes 9 arguments`.

- [x] **Paso 4: el caso de uso**

`src/ArquitecturaBase.Application/Features/Auth/RequestLoginCode/RequestLoginCodeCommandHandler.cs` completo:

```csharp
using System.Globalization;
using ArquitecturaBase.Application.Abstractions.Emails;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Application.Abstractions.Settings;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Features.Auth.RequestLoginCode;

/// <summary>
/// Emite un código nuevo y encola el email. Aplica el reenvío y el límite por email (sección 5.3); el límite por IP
/// lo aplica el rate limiter de la Api. El email se encola antes de guardar: si el guardado fallara, el usuario
/// recibiría un código que no sirve y pediría otro.
/// En modo InviteOnly, un correo sin cuenta recorre exactamente el mismo camino y lo único que no pasa es el envío
/// del email (sección 4 del spec de la Fase 4).
/// </summary>
internal sealed class RequestLoginCodeCommandHandler(
    ILoginCodeRepository loginCodes,
    IIdentityService identityService,
    ILoginCodeGenerator codeGenerator,
    ILoginCodeHasher codeHasher,
    IEmailTemplateRenderer templateRenderer,
    IEmailQueue emailQueue,
    ISystemSettingsReader systemSettings,
    IOptions<LoginCodeOptions> options,
    TimeProvider timeProvider)
    : ICommandHandler<RequestLoginCodeCommand, RequestLoginCodeResponse>
{
    public async Task<Result<RequestLoginCodeResponse>> Handle(RequestLoginCodeCommand command, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(command.Email);

        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var email = emailResult.Value;

        // Los límites de la sección 5.3 se aplican de a un request por email.
        await loginCodes.LockEmailAsync(email, cancellationToken);

        var settings = options.Value;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var limitError = await CheckLimitsAsync(email, settings, nowUtc, cancellationToken);

        if (limitError is not null)
        {
            return limitError;
        }

        foreach (var activeCode in await loginCodes.ListActiveAsync(email, nowUtc, cancellationToken))
        {
            activeCode.Invalidate(nowUtc);
        }

        var code = codeGenerator.Generate();

        loginCodes.Add(LoginCode.Issue(
            email,
            codeHasher.Hash(email, code),
            nowUtc,
            TimeSpan.FromMinutes(settings.LifetimeMinutes),
            settings.MaxAttempts));

        var user = await identityService.FindByEmailAsync(email, cancellationToken);

        // InviteOnly: a un correo sin cuenta se le emitió el código igual, pero no se le manda ningún email, y la
        // respuesta es la misma de siempre. El código se emite a propósito: los límites por dirección se apoyan en
        // esta fila, y sin ella una dirección desconocida respondería 202 para siempre mientras una registrada
        // empieza a responder 429, que es todo lo que hace falta para enumerar cuentas (sección 4 del spec de la
        // Fase 4). La fila vence sola a los 10 minutos sin que nadie la use.
        if (user is not null || await systemSettings.GetRegistrationModeAsync(cancellationToken) is RegistrationMode.Open)
        {
            // El email sale en el idioma del perfil; si la cuenta todavía no existe, en el de la petición.
            var culture = user is null ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(user.Culture);

            await emailQueue.EnqueueAsync(
                templateRenderer.RenderLoginCode(email.Value, code, settings.LifetimeMinutes, culture),
                cancellationToken);
        }

        return new RequestLoginCodeResponse(settings.ResendCooldownSeconds);
    }

    private async Task<Error?> CheckLimitsAsync(Email email, LoginCodeOptions settings, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var window = TimeSpan.FromMinutes(settings.RequestWindowMinutes);
        var requestTimes = await loginCodes.ListRequestTimesSinceAsync(email, nowUtc - window, cancellationToken);

        if (requestTimes.Count >= settings.MaxRequestsPerWindow)
        {
            // Se libera un lugar cuando el pedido más viejo de la ventana sale de ella.
            return LoginCodeErrors.TooManyRequests(SecondsUntil(requestTimes[0] + window, nowUtc));
        }

        var latest = await loginCodes.GetLatestAsync(email, cancellationToken);
        var resendAllowedAtUtc = latest?.CreatedAtUtc + TimeSpan.FromSeconds(settings.ResendCooldownSeconds);

        return resendAllowedAtUtc > nowUtc
            ? LoginCodeErrors.ResendTooSoon(SecondsUntil(resendAllowedAtUtc.Value, nowUtc))
            : null;
    }

    private static int SecondsUntil(DateTime momentUtc, DateTime nowUtc) =>
        Math.Max(1, (int)Math.Ceiling((momentUtc - nowUtc).TotalSeconds));
}
```

La búsqueda del usuario quedó donde estaba y ahora se reusa para las dos cosas: decidir si se manda el email y elegir el idioma. El `||` corta antes: si la cuenta existe, no se consulta el modo.

- [x] **Paso 5: los tests unitarios en verde**

```bash
dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Features.Auth.RequestLoginCodeCommandHandlerTests"
```

`Passed!`, con los 11 tests de la clase (los 8 de antes más los 3 nuevos).

- [x] **Paso 6: el test de integración**

Crear `tests/ArquitecturaBase.Api.IntegrationTests/Auth/RegistrationModeTests.cs`:

```csharp
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

[Collection(ApiTestGroup.Name)]
public sealed class RegistrationModeTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Invite_only_answers_202_and_sends_nothing_to_an_email_without_an_account()
    {
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        using var client = factory.CreateClient();
        var unknown = TestEmails.Unique("uninvited");
        var invited = await CreateAccountAsync("invited");

        using var unknownResponse = await client.PostJsonAsync("/account/login-code", new { email = unknown });
        using var invitedResponse = await client.PostJsonAsync("/account/login-code", new { email = invited });

        // La cola de emails es FIFO y la vacía un solo lector: cuando llega el del correo invitado, el del correo
        // desconocido ya habría llegado si se hubiera encolado.
        await factory.EmailSender.WaitForAsync(invited);

        Assert.Equal(HttpStatusCode.Accepted, unknownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, invitedResponse.StatusCode);

        // Byte a byte la misma respuesta: no hay nada en el cuerpo que distinga una dirección de la otra.
        Assert.Equal(
            await invitedResponse.Content.ReadAsStringAsync(Ct),
            await unknownResponse.Content.ReadAsStringAsync(Ct));

        Assert.Equal(0, factory.EmailSender.CountFor(unknown));

        // El código se emitió igual, aunque no se haya mandado: es lo que sostiene los límites por dirección.
        Assert.True(await factory.ExecuteDbContextAsync(db => db.LoginCodes.AnyAsync(code => code.Email == unknown, Ct)));
    }

    [Fact]
    public async Task Insisting_with_an_unknown_email_is_limited_exactly_like_a_registered_one()
    {
        // El modo se cambia antes de levantar la Api hija: tiene su propio contenedor, así que su caché de ajustes
        // arranca frío y lee la fila en la primera petición.
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        var known = await CreateAccountAsync("limited");
        var unknown = TestEmails.Unique("limited");

        // El arnés no espera entre pedidos: para probar el límite hace falta el valor real.
        await using var api = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Authentication:LoginCode:ResendCooldownSeconds", "60"));
        using var client = api.CreateClient();

        using var firstUnknown = await client.PostJsonAsync("/account/login-code", new { email = unknown });
        using var firstKnown = await client.PostJsonAsync("/account/login-code", new { email = known });
        factory.Clock.Advance(TimeSpan.FromSeconds(15));
        using var secondUnknown = await client.PostJsonAsync("/account/login-code", new { email = unknown }, language: "es");
        using var secondKnown = await client.PostJsonAsync("/account/login-code", new { email = known }, language: "es");
        var unknownProblem = await secondUnknown.ReadJsonAsync();
        var knownProblem = await secondKnown.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Accepted, firstUnknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, firstKnown.StatusCode);

        // Lo que cierra el agujero: insistir con una dirección desconocida responde igual que con una registrada.
        // Si el código no se emitiera, acá la desconocida seguiría respondiendo 202 y la registrada, 429.
        Assert.Equal(HttpStatusCode.TooManyRequests, secondUnknown.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondKnown.StatusCode);
        Assert.Equal("Auth.LoginCode.ResendTooSoon", unknownProblem.GetProperty("code").GetString());
        Assert.Equal(
            knownProblem.GetProperty("code").GetString(),
            unknownProblem.GetProperty("code").GetString());
        Assert.Equal(45, unknownProblem.GetProperty("retryAfter").GetInt32());
        Assert.Equal(
            knownProblem.GetProperty("retryAfter").GetInt32(),
            unknownProblem.GetProperty("retryAfter").GetInt32());
        Assert.Equal(
            knownProblem.GetProperty("detail").GetString(),
            unknownProblem.GetProperty("detail").GetString());

        // Solo el `traceId` distingue los dos cuerpos, y cambia en cada petición.
        Assert.Equal(0, factory.EmailSender.CountFor(unknown));
    }

    [Fact]
    public async Task Open_mode_still_creates_the_account_of_an_unknown_email()
    {
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.Open);
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("open");

        var code = await client.RequestCodeAsync(factory, email);

        Assert.Matches("^[0-9]{6}$", code);
        Assert.True(await factory.ExecuteDbContextAsync(db => db.LoginCodes.AnyAsync(stored => stored.Email == email, Ct)));
    }

    private Task<string> CreateAccountAsync(string prefix)
    {
        var email = TestEmails.Unique(prefix);

        return factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>()
                .CreateAsync(Email.Create(email).Value, displayName: null, "es", Ct);

            return email;
        });
    }
}
```

- [x] **Paso 7: correrlo**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.RegistrationModeTests"
```

`Passed! - Failed: 0, Passed: 3`. Los dos modos de fallar, y qué significa cada uno:

- `Assert.Equal() Failure: Expected: 0, Actual: 1` sobre `CountFor(unknown)`: el `if` que decide el envío quedó mal y se está encolando el email igual.
- `Assert.Equal() Failure: Expected: TooManyRequests, Actual: Accepted` en `secondUnknown`: el caso de uso está salteando `loginCodes.Add` para los correos desconocidos, que es exactamente el agujero que esta tarea cierra.

- [x] **Paso 8: build y suite completa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

`0 Warning(s)` y la suite entera en verde. `LoginCodeEndpointsTests`, `ConnectFlowTests` y `LoginCodeConcurrencyTests` ingresan con correos nuevos y siguen andando porque el arnés fija `Registration:Mode = Open` (Tarea 1, paso 8). Si alguno fallara con `TimeoutException` esperando el email, es que esa línea no quedó en `ApiFactory`.

- [x] **Paso 9: commit**

```bash
git add src/ArquitecturaBase.Application/Features/Auth/RequestLoginCode/RequestLoginCodeCommandHandler.cs tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/AuthFakes.cs tests/ArquitecturaBase.Application.UnitTests/Features/Auth/RequestLoginCodeCommandHandlerTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Auth/RegistrationModeTests.cs
git commit -m "feat: no emitir codigos de ingreso a correos sin cuenta en modo InviteOnly" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 3: El modo de registro en el ingreso con Google

Repo: **backend**.

Con Google la persona ya probó ante el proveedor que la dirección es suya, así que acá no hay nada que ocultar: en `InviteOnly`, un correo sin cuenta vuelve al ingreso con `Auth.Account.NotInvited`, un mensaje que le dice que pida acceso a un administrador. Es el complemento de la tarea anterior: las dos puertas de entrada respetan el mismo ajuste.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Domain/Authentication/AccountErrors.cs`
- Modificar: `src/ArquitecturaBase.Application/Features/Auth/SignInWithExternalProvider/SignInWithExternalProviderCommandHandler.cs`
- Modificar: `src/ArquitecturaBase.Application/Resources/Errors.resx`
- Modificar: `src/ArquitecturaBase.Application/Resources/Errors.en.resx`
- Test: `tests/ArquitecturaBase.Application.UnitTests/Features/Auth/SignInWithExternalProviderCommandHandlerTests.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Auth/RegistrationModeTests.cs`

- [x] **Paso 1: los tests unitarios**

En `tests/ArquitecturaBase.Application.UnitTests/Features/Auth/SignInWithExternalProviderCommandHandlerTests.cs`, agregar el campo y pasarlo al handler:

```csharp
    private readonly FakeSystemSettingsReader _settings = new();
```

```csharp
    public SignInWithExternalProviderCommandHandlerTests()
    {
        _handler = new SignInWithExternalProviderCommandHandler(_identity, _audits, _settings, new FakeRequestInfo(), _clock);
    }
```

y al final de la clase:

```csharp
    [Fact]
    public async Task Invite_only_rejects_an_email_without_an_account_and_creates_nothing()
    {
        _settings.Mode = RegistrationMode.InviteOnly;
        _identity.PendingExternalLogin = GoogleLogin(emailVerified: true);

        var result = await _handler.Handle(new SignInWithExternalProviderCommand(ReturnUrl), Ct);

        Assert.Equal(AccountErrors.NotInvitedCode, result.Error.Code);
        Assert.Empty(_identity.Users);
        Assert.Empty(_identity.SignedInUsers);
        Assert.True(_identity.ExternalSignedOut);

        var audit = Assert.Single(_audits.Audits);
        Assert.False(audit.Succeeded);
        Assert.Equal(AccountErrors.NotInvitedCode, audit.FailureReason);
        Assert.Equal(UserEmail, audit.Email);
    }

    [Fact]
    public async Task Invite_only_lets_in_an_account_that_an_administrator_already_created()
    {
        _settings.Mode = RegistrationMode.InviteOnly;
        var user = _identity.AddUser(UserEmail);
        _identity.PendingExternalLogin = GoogleLogin(emailVerified: true);

        var result = await _handler.Handle(new SignInWithExternalProviderCommand(ReturnUrl), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([user.Id], _identity.SignedInUsers);
        Assert.Same(user, await _identity.FindByExternalLoginAsync("Google", "google-123", Ct));
    }
```

con el using `using ArquitecturaBase.Domain.Settings;`.

- [x] **Paso 2: correrlos y ver que fallan**

```bash
dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Features.Auth.SignInWithExternalProviderCommandHandlerTests"
```

Tiene que fallar al compilar, con `error CS1729: 'SignInWithExternalProviderCommandHandler' does not contain a constructor that takes 5 arguments` y `error CS0117: 'AccountErrors' does not contain a definition for 'NotInvitedCode'`.

- [x] **Paso 3: el error nuevo y sus traducciones**

`src/ArquitecturaBase.Domain/Authentication/AccountErrors.cs` completo:

```csharp
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Authentication;

public static class AccountErrors
{
    public const string DisabledCode = "Auth.Account.Disabled";
    public const string LockedOutCode = "Auth.Account.LockedOut";
    public const string NotInvitedCode = "Auth.Account.NotInvited";

    /// <summary>Cuenta deshabilitada. Se informa después de verificar el código: el usuario ya probó que el email es suyo.</summary>
    public static readonly Error Disabled = Error.Forbidden(DisabledCode, "The account is disabled.");

    /// <summary>Bloqueo de Identity por verificaciones fallidas seguidas.</summary>
    public static readonly Error LockedOut = Error.TooManyRequests(LockedOutCode, "The account is temporarily locked.");

    /// <summary>
    /// El sistema es solo por invitación y ese email no tiene cuenta. Solo se informa cuando el proveedor externo
    /// ya verificó la dirección: por eso no revela nada (sección 4 del spec de la Fase 4).
    /// </summary>
    public static readonly Error NotInvited = Error.Forbidden(
        NotInvitedCode, "The account must be created by an administrator.");
}
```

En `src/ArquitecturaBase.Application/Resources/Errors.resx`, junto a las otras claves `Auth.Account.*`:

```xml
  <data name="Auth.Account.NotInvited" xml:space="preserve"><value>Todavía no tenés acceso al sistema. Pedile a un administrador que te dé de alta.</value></data>
```

En `src/ArquitecturaBase.Application/Resources/Errors.en.resx`:

```xml
  <data name="Auth.Account.NotInvited" xml:space="preserve"><value>You don't have access to this system yet. Ask an administrator to create your account.</value></data>
```

- [x] **Paso 4: el caso de uso**

`src/ArquitecturaBase.Application/Features/Auth/SignInWithExternalProvider/SignInWithExternalProviderCommandHandler.cs` completo:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Settings;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Features.Auth.SignInWithExternalProvider;

internal sealed class SignInWithExternalProviderCommandHandler(
    IIdentityService identityService,
    ILoginAuditRepository loginAudits,
    ISystemSettingsReader systemSettings,
    IRequestInfo requestInfo,
    TimeProvider timeProvider)
    : ICommandHandler<SignInWithExternalProviderCommand, SignInWithExternalProviderResponse>
{
    public async Task<Result<SignInWithExternalProviderResponse>> Handle(
        SignInWithExternalProviderCommand command,
        CancellationToken cancellationToken)
    {
        var login = await identityService.GetExternalLoginAsync(cancellationToken);

        if (login is null)
        {
            return Fail(email: string.Empty, user: null, ExternalLoginErrors.Failed);
        }

        // La cookie externa solo sirve para este paso: se cierra pase lo que pase.
        await identityService.SignOutExternalAsync(cancellationToken);

        var user = await identityService.FindByExternalLoginAsync(login.Provider, login.ProviderKey, cancellationToken);

        if (user is null)
        {
            var email = Email.Create(login.Email);

            // Sin email verificado no se vincula ni se crea: alguien podría presentarse con un email ajeno.
            if (!login.EmailVerified || email.IsFailure)
            {
                return Fail(email.IsSuccess ? email.Value.Value : string.Empty, user: null, ExternalLoginErrors.EmailNotVerified);
            }

            user = await identityService.FindByEmailAsync(email.Value, cancellationToken);

            if (user is null)
            {
                // InviteOnly: la cuenta la tiene que crear un administrador. Acá se puede decir con todas las
                // letras, porque la persona ya probó ante el proveedor que la dirección es suya.
                if (await systemSettings.GetRegistrationModeAsync(cancellationToken) is RegistrationMode.InviteOnly)
                {
                    return Fail(email.Value.Value, user: null, AccountErrors.NotInvited);
                }

                user = await identityService.CreateAsync(
                    email.Value, login.DisplayName, UserCultures.FromCurrentRequest(), cancellationToken);
            }

            await identityService.AddExternalLoginAsync(user.Id, login, cancellationToken);
        }

        if (!user.IsActive)
        {
            return Fail(user.Email, user, AccountErrors.Disabled);
        }

        // Igual que el ingreso con código: una cuenta bloqueada no entra por ningún medio.
        if (await identityService.IsLockedOutAsync(user.Id, cancellationToken))
        {
            return Fail(user.Email, user, AccountErrors.LockedOut);
        }

        await identityService.SignInAsync(user.Id, cancellationToken);

        loginAudits.Add(LoginAudit.Success(
            user.Email, user.Id, LoginMethod.Google, requestInfo.IpAddress, requestInfo.UserAgent, UtcNow()));

        return new SignInWithExternalProviderResponse(command.ReturnUrl!);
    }

    private Error Fail(string email, UserAccount? user, Error error)
    {
        loginAudits.Add(LoginAudit.Failure(
            email, user?.Id, LoginMethod.Google, error.Code, requestInfo.IpAddress, requestInfo.UserAgent, UtcNow()));

        return error;
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
```

- [x] **Paso 5: los tests unitarios en verde**

```bash
dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Features.Auth.SignInWithExternalProviderCommandHandlerTests"
dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Resources.ErrorCodeTranslationTests"
```

Los dos, `Passed!`.

- [x] **Paso 6: el test de integración**

Agregar a `tests/ArquitecturaBase.Api.IntegrationTests/Auth/RegistrationModeTests.cs`, al final de la clase (y el using `using System.Globalization;`):

```csharp
    [Fact]
    public async Task Invite_only_sends_a_google_sign_in_without_an_account_back_to_login_with_the_error()
    {
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("notinvited");
        using var external = await client.PostJsonAsync(
            "/test/external-login",
            new { providerKey = "google-" + email, email, name = "Ana", emailVerified = true });

        using var callback = await client.SendAsync(
            HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(AuthFlow.AuthorizeReturnUrl));

        Assert.True(external.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/login?error=Auth.Account.NotInvited", callback.Headers.Location!.OriginalString);
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Users.AnyAsync(user => user.Email == email, Ct)));
        Assert.True(await factory.ExecuteDbContextAsync(db =>
            db.LoginAudits.AnyAsync(audit => audit.Email == email && audit.FailureReason == "Auth.Account.NotInvited", Ct)));
    }

    [Fact]
    public async Task Invite_only_lets_google_in_when_the_account_already_exists()
    {
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        using var client = factory.CreateClient();
        var email = await CreateAccountAsync("googleinvited");
        var providerKey = "google-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        using var external = await client.PostJsonAsync(
            "/test/external-login", new { providerKey, email, name = "Ana", emailVerified = true });

        using var callback = await client.SendAsync(
            HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(AuthFlow.AuthorizeReturnUrl));

        Assert.True(external.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal(AuthFlow.AuthorizeReturnUrl, callback.Headers.Location!.OriginalString);
    }
```

- [x] **Paso 7: correrlo**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.RegistrationModeTests"
```

`Passed! - Failed: 0, Passed: 5` (los 3 de la Tarea 2 más los 2 de esta).

- [x] **Paso 8: build y suite completa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

`0 Warning(s)` y todo en verde. `ExternalLoginTests.Verified_google_account_is_created_linked_signed_in_and_audited` crea la cuenta desde Google y sigue pasando porque el arnés está en `Open`; si fallara con `/login?error=Auth.Account.NotInvited` en lugar del `returnUrl`, es que se perdió la línea `Registration:Mode` de `ApiFactory`.

- [x] **Paso 9: commit**

```bash
git add src/ArquitecturaBase.Domain/Authentication/AccountErrors.cs src/ArquitecturaBase.Application/Features/Auth/SignInWithExternalProvider/SignInWithExternalProviderCommandHandler.cs src/ArquitecturaBase.Application/Resources/Errors.resx src/ArquitecturaBase.Application/Resources/Errors.en.resx tests/ArquitecturaBase.Application.UnitTests/Features/Auth/SignInWithExternalProviderCommandHandlerTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Auth/RegistrationModeTests.cs
git commit -m "feat: rechazar el ingreso con Google sin invitacion en modo InviteOnly" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 4: Endpoints de configuración y el permiso `settings.manage`

Repo: **backend**.

El modo de registro se cambia desde el panel, no con una variable de entorno: desplegar un producto nuevo tiene que ser levantarlo y configurarlo desde adentro. Se suma el permiso `settings.manage` al catálogo, el seed se lo da a Admin, y los dos endpoints (`GET` y `PUT /api/settings`) lo exigen. El `PUT` descarta el caché del lector, así que el cambio vale para el ingreso siguiente sin reiniciar nada.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Domain/Authorization/Permissions.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Settings/GetSystemSettings/GetSystemSettingsQuery.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Settings/GetSystemSettings/SystemSettingsResponse.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Settings/GetSystemSettings/GetSystemSettingsQueryHandler.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Settings/UpdateSystemSettings/UpdateSystemSettingsCommand.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Settings/UpdateSystemSettings/UpdateSystemSettingsCommandHandler.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Settings/UpdateSystemSettings/UpdateSystemSettingsCommandValidator.cs`
- Crear: `src/ArquitecturaBase.Api/Endpoints/Settings/SettingsEndpoints.cs`
- Modificar: `src/ArquitecturaBase.Api/DependencyInjection.cs`
- Modificar: `src/ArquitecturaBase.Application/Resources/ValidationMessages.cs`
- Modificar: `src/ArquitecturaBase.Application/Resources/Validation.resx`
- Modificar: `src/ArquitecturaBase.Application/Resources/Validation.en.resx`
- Test: `tests/ArquitecturaBase.Domain.UnitTests/Authorization/PermissionsTests.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Settings/SettingsEndpointsTests.cs`

- [x] **Paso 1: el permiso nuevo en el test del catálogo**

En `tests/ArquitecturaBase.Domain.UnitTests/Authorization/PermissionsTests.cs`, cambiar el primer test:

```csharp
    [Fact]
    public void All_lists_every_permission_once()
    {
        Assert.Equal(
            [
                Permissions.Users.Read,
                Permissions.Users.Manage,
                Permissions.Roles.Read,
                Permissions.Roles.Manage,
                Permissions.Settings.Manage,
            ],
            Permissions.All);
        Assert.Equal(Permissions.All.Count, Permissions.All.Distinct(StringComparer.Ordinal).Count());
    }
```

- [x] **Paso 2: correrlo y ver que falla**

```bash
dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj -- --filter-class "ArquitecturaBase.Domain.UnitTests.Authorization.PermissionsTests"
```

Tiene que fallar al compilar, con `error CS0117: 'Permissions' does not contain a definition for 'Settings'`.

- [x] **Paso 3: el permiso**

`src/ArquitecturaBase.Domain/Authorization/Permissions.cs` completo:

```csharp
namespace ArquitecturaBase.Domain.Authorization;

/// <summary>
/// Catálogo de permisos. Cada permiso se guarda como role claim de tipo <see cref="ClaimType"/> y un usuario suma
/// los de todos sus roles. Los endpoints piden permisos, nunca roles.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    public static class Users
    {
        public const string Read = "users.read";
        public const string Manage = "users.manage";
    }

    public static class Roles
    {
        public const string Read = "roles.read";
        public const string Manage = "roles.manage";
    }

    public static class Settings
    {
        public const string Manage = "settings.manage";
    }

    public static IReadOnlyCollection<string> All { get; } =
        [Users.Read, Users.Manage, Roles.Read, Roles.Manage, Settings.Manage];
}
```

`RoleSeeder` ya le da `Permissions.All` a Admin y agrega solo los que falten, así que una base existente recibe `settings.manage` al arrancar, sin tocar nada más.

- [x] **Paso 4: el test del catálogo en verde**

```bash
dotnet test --project tests/ArquitecturaBase.Domain.UnitTests/ArquitecturaBase.Domain.UnitTests.csproj -- --filter-class "ArquitecturaBase.Domain.UnitTests.Authorization.PermissionsTests"
```

`Passed! - Failed: 0, Passed: 3`.

- [x] **Paso 5: los tests de integración de los endpoints**

Crear `tests/ArquitecturaBase.Api.IntegrationTests/Settings/SettingsEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Api.IntegrationTests.Settings;

[Collection(ApiTestGroup.Name)]
public sealed class SettingsEndpointsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Admin_reads_the_registration_mode()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        // Se cambia el modo después de ingresar: en InviteOnly, una cuenta que todavía no exista no recibiría código.
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);

        using var response = await client.GetWithTokenAsync("/api/settings", tokens.AccessToken);
        var settings = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("InviteOnly", settings.GetProperty("registrationMode").GetString());
    }

    [Fact]
    public async Task Reading_the_settings_requires_the_settings_manage_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("nosettings"));

        using var response = await client.GetWithTokenAsync("/api/settings", tokens.AccessToken, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("No tenés permiso para realizar esta acción.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Changing_the_mode_from_the_panel_takes_effect_without_restarting()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        // El scope deja el modo como estaba, aunque el test lo cambie con el endpoint.
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        var email = TestEmails.Unique("afterswitch");

        using var closed = await client.PostJsonAsync("/account/login-code", new { email });
        using var updated = await PutSettingsAsync(client, tokens.AccessToken, new { registrationMode = "Open" });
        var code = await client.RequestCodeAsync(factory, email);

        Assert.Equal(HttpStatusCode.Accepted, closed.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);

        // Con el modo cerrado el pedido respondió 202 pero no llegó ningún email; con el cambio hecho desde el
        // panel, el código sale sin reiniciar nada. El único email de esta dirección es el de después del cambio.
        Assert.Matches("^[0-9]{6}$", code);
        Assert.Equal(1, factory.EmailSender.CountFor(email));
    }

    [Fact]
    public async Task An_unknown_registration_mode_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        // JsonStringEnumConverter acepta números: el que pone la lista blanca es el validador.
        using var response = await PutSettingsAsync(client, tokens.AccessToken, new { registrationMode = 7 }, "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "El modo de registro no es válido.",
            problem.GetProperty("errors").GetProperty("registrationMode")[0].GetString());
    }

    private static async Task<HttpResponseMessage> PutSettingsAsync(
        HttpClient client,
        string accessToken,
        object body,
        string? language = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri("/api/settings", UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (language is not null)
        {
            request.Headers.AcceptLanguage.ParseAdd(language);
        }

        return await client.SendAsync(request, Ct);
    }
}
```

`Changing_the_mode_from_the_panel_takes_effect_without_restarting` es el test que más importa de la tarea: prueba las dos mitades de la cadena (el `PUT` guarda y descarta el caché; el ingreso siguiente ve el valor nuevo) en una sola corrida.

Correrlos:

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Settings.SettingsEndpointsTests"
```

Los cuatro tienen que fallar con `Assert.Equal() Failure: Values differ / Expected: OK / Actual: NotFound`: todavía no existe la ruta.

- [x] **Paso 6: el mensaje de validación**

En `src/ArquitecturaBase.Application/Resources/Validation.resx`:

```xml
  <data name="RegistrationModeInvalid" xml:space="preserve"><value>El modo de registro no es válido.</value></data>
```

En `src/ArquitecturaBase.Application/Resources/Validation.en.resx`:

```xml
  <data name="RegistrationModeInvalid" xml:space="preserve"><value>The registration mode is not valid.</value></data>
```

En `src/ArquitecturaBase.Application/Resources/ValidationMessages.cs`, debajo de `LoginCodeFormat`:

```csharp
    public static string RegistrationModeInvalid => Get(nameof(RegistrationModeInvalid));
```

- [x] **Paso 7: los casos de uso**

`src/ArquitecturaBase.Application/Features/Settings/GetSystemSettings/SystemSettingsResponse.cs`:

```csharp
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Features.Settings.GetSystemSettings;

/// <summary>Los ajustes que ve el panel. Por ahora hay uno solo; cada ajuste nuevo suma una propiedad acá.</summary>
public sealed record SystemSettingsResponse(RegistrationMode RegistrationMode);
```

`src/ArquitecturaBase.Application/Features/Settings/GetSystemSettings/GetSystemSettingsQuery.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Settings.GetSystemSettings;

public sealed record GetSystemSettingsQuery : IQuery<SystemSettingsResponse>;
```

`src/ArquitecturaBase.Application/Features/Settings/GetSystemSettings/GetSystemSettingsQueryHandler.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Features.Settings.GetSystemSettings;

/// <summary>Lee la fila, no el caché: el panel tiene que mostrar lo que está guardado.</summary>
internal sealed class GetSystemSettingsQueryHandler(ISystemSettingsRepository repository)
    : IQueryHandler<GetSystemSettingsQuery, SystemSettingsResponse>
{
    public async Task<Result<SystemSettingsResponse>> Handle(GetSystemSettingsQuery query, CancellationToken cancellationToken)
    {
        var settings = await repository.GetAsync(cancellationToken);

        if (settings is null)
        {
            return SettingsErrors.NotFound;
        }

        return new SystemSettingsResponse(settings.RegistrationMode);
    }
}
```

`src/ArquitecturaBase.Application/Features/Settings/UpdateSystemSettings/UpdateSystemSettingsCommand.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Features.Settings.UpdateSystemSettings;

public sealed record UpdateSystemSettingsCommand(RegistrationMode RegistrationMode) : ICommand;
```

`src/ArquitecturaBase.Application/Features/Settings/UpdateSystemSettings/UpdateSystemSettingsCommandValidator.cs`:

```csharp
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Settings.UpdateSystemSettings;

internal sealed class UpdateSystemSettingsCommandValidator : AbstractValidator<UpdateSystemSettingsCommand>
{
    // System.Text.Json acepta cualquier número para un enum: la lista blanca la pone el validador.
    public UpdateSystemSettingsCommandValidator() =>
        RuleFor(command => command.RegistrationMode)
            .IsInEnum()
            .WithMessage(_ => ValidationMessages.RegistrationModeInvalid);
}
```

`src/ArquitecturaBase.Application/Features/Settings/UpdateSystemSettings/UpdateSystemSettingsCommandHandler.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Settings;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Features.Settings.UpdateSystemSettings;

internal sealed class UpdateSystemSettingsCommandHandler(
    ISystemSettingsRepository repository,
    ISystemSettingsReader reader)
    : ICommandHandler<UpdateSystemSettingsCommand>
{
    public async Task<Result> Handle(UpdateSystemSettingsCommand command, CancellationToken cancellationToken)
    {
        var settings = await repository.GetAsync(cancellationToken);

        if (settings is null)
        {
            return SettingsErrors.NotFound;
        }

        settings.SetRegistrationMode(command.RegistrationMode);

        // El caché se descarta acá y UnitOfWorkDecorator guarda enseguida: el cambio vale para el ingreso
        // siguiente, sin reiniciar la Api (sección 5 del spec de la Fase 4).
        await reader.InvalidateAsync(cancellationToken);

        return Result.Success();
    }
}
```

- [x] **Paso 8: el endpoint y el formato del enum en JSON**

`src/ArquitecturaBase.Api/Endpoints/Settings/SettingsEndpoints.cs`:

```csharp
using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Settings.GetSystemSettings;
using ArquitecturaBase.Application.Features.Settings.UpdateSystemSettings;
using ArquitecturaBase.Domain.Authorization;

namespace ArquitecturaBase.Api.Endpoints.Settings;

/// <summary>Configuración del sistema (sección 10 del spec de la Fase 4). Solo la ve quien tiene settings.manage.</summary>
internal sealed class SettingsEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").WithTags("Settings");

        group.MapGet("", async (
                IQueryHandler<GetSystemSettingsQuery, SystemSettingsResponse> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new GetSystemSettingsQuery(), cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Settings.Manage);

        group.MapPut("", async (
                UpdateSystemSettingsCommand command,
                ICommandHandler<UpdateSystemSettingsCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(command, cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Settings.Manage);
    }
}
```

En `src/ArquitecturaBase.Api/DependencyInjection.cs`, agregar el using `using System.Text.Json.Serialization;` y reemplazar la línea de `ConfigureHttpJsonOptions` por:

```csharp
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new UtcDateTimeConverter());

            // Los enums viajan por su nombre ("InviteOnly", "Open"): el front no tiene que conocer los números,
            // y un valor nuevo no corre la numeración de los que ya estaban.
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
```

- [x] **Paso 9: los tests de integración en verde**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Settings.SettingsEndpointsTests"
```

`Passed! - Failed: 0, Passed: 4`.

- [x] **Paso 10: build y suite completa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

`0 Warning(s)` y todo en verde. `UsersEndpointsTests.Admin_gets_every_permission`, `SeedTests` y `PermissionServiceTests` comparan contra `Permissions.All`, así que el permiso nuevo tiene que aparecer solo; si alguno fallara, el seed no le agregó `settings.manage` a Admin.

- [x] **Paso 11: commit**

```bash
git add src/ArquitecturaBase.Domain/Authorization/Permissions.cs src/ArquitecturaBase.Application/Features/Settings src/ArquitecturaBase.Application/Resources/ValidationMessages.cs src/ArquitecturaBase.Application/Resources/Validation.resx src/ArquitecturaBase.Application/Resources/Validation.en.resx src/ArquitecturaBase.Api/Endpoints/Settings src/ArquitecturaBase.Api/DependencyInjection.cs tests/ArquitecturaBase.Domain.UnitTests/Authorization/PermissionsTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Settings/SettingsEndpointsTests.cs
git commit -m "feat: agregar los endpoints de configuracion y el permiso settings.manage" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 5: Las reglas que protegen al último administrador

Repo: **backend**.

Un panel de administración mal hecho deja al dueño afuera de su propio sistema. Las tres reglas de la sección 8 del spec que dependen de datos (nadie se saca a sí mismo el rol Admin, nadie desactiva ni elimina su propia cuenta, siempre queda al menos un administrador activo) se escriben **una sola vez**, en `UserGuards`, y las tareas 8, 9 y 10 la llaman: si cada handler las repitiera, la primera que se olvide rompe el sistema. Viven en Application y no en Domain porque hay que contar administradores activos, y ese dato está en Identity.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Domain/Users/UserErrors.cs`
- Modificar: `src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Users/UserGuards.cs`
- Modificar: `src/ArquitecturaBase.Application/DependencyInjection.cs`
- Modificar: `src/ArquitecturaBase.Application/Resources/Errors.resx`
- Modificar: `src/ArquitecturaBase.Application/Resources/Errors.en.resx`
- Test: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`
- Test: `tests/ArquitecturaBase.Application.UnitTests/Features/Users/UserGuardsTests.cs`

- [x] **Paso 1: los tests unitarios de las reglas**

Crear `tests/ArquitecturaBase.Application.UnitTests/Features/Users/UserGuardsTests.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Features.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.Features.Users;

public sealed class UserGuardsTests
{
    private readonly FakeIdentityService _identity = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Nobody_can_take_the_admin_role_from_themselves()
    {
        var admin = AddAdmin("ana@example.com");
        AddAdmin("beto@example.com");
        var guards = GuardsFor(admin.Id);

        var result = await guards.EnsureRolesCanChangeAsync(admin.Id, [SystemRoles.User], Ct);

        Assert.Equal(UserErrors.CannotModifySelfCode, result.Error.Code);
    }

    [Fact]
    public async Task Keeping_the_admin_role_while_changing_the_rest_is_allowed()
    {
        var admin = AddAdmin("ana@example.com");
        var guards = GuardsFor(admin.Id);

        var result = await guards.EnsureRolesCanChangeAsync(admin.Id, [SystemRoles.Admin, SystemRoles.User], Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Taking_the_admin_role_from_the_last_active_admin_is_rejected()
    {
        var admin = AddAdmin("ana@example.com");
        var other = AddUser("beto@example.com");
        var guards = GuardsFor(other.Id);

        var result = await guards.EnsureRolesCanChangeAsync(admin.Id, [SystemRoles.User], Ct);

        Assert.Equal(UserErrors.LastAdminCode, result.Error.Code);
    }

    [Fact]
    public async Task Taking_the_admin_role_is_allowed_when_another_active_admin_remains()
    {
        var admin = AddAdmin("ana@example.com");
        var second = AddAdmin("beto@example.com");
        var guards = GuardsFor(second.Id);

        var result = await guards.EnsureRolesCanChangeAsync(admin.Id, [SystemRoles.User], Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Nobody_can_deactivate_or_delete_their_own_account()
    {
        var admin = AddAdmin("ana@example.com");
        AddAdmin("beto@example.com");
        var guards = GuardsFor(admin.Id);

        var result = await guards.EnsureCanBeRemovedAsync(admin.Id, Ct);

        Assert.Equal(UserErrors.CannotModifySelfCode, result.Error.Code);
    }

    [Fact]
    public async Task Removing_the_last_active_admin_is_rejected()
    {
        var admin = AddAdmin("ana@example.com");
        var other = AddUser("beto@example.com");
        var guards = GuardsFor(other.Id);

        var result = await guards.EnsureCanBeRemovedAsync(admin.Id, Ct);

        Assert.Equal(UserErrors.LastAdminCode, result.Error.Code);
    }

    [Fact]
    public async Task Removing_a_user_without_the_admin_role_is_allowed()
    {
        var admin = AddAdmin("ana@example.com");
        var other = AddUser("beto@example.com");
        var guards = GuardsFor(admin.Id);

        var result = await guards.EnsureCanBeRemovedAsync(other.Id, Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task An_admin_that_is_already_inactive_is_not_the_last_one()
    {
        var inactive = _identity.AddUser("ana@example.com", isActive: false);
        _identity.SetRoles(inactive.Id, SystemRoles.Admin);
        var other = AddUser("beto@example.com");
        var guards = GuardsFor(other.Id);

        var result = await guards.EnsureCanBeRemovedAsync(inactive.Id, Ct);

        Assert.True(result.IsSuccess);
    }

    private UserGuards GuardsFor(Guid currentUserId) =>
        new(new FakeCurrentUser { UserId = currentUserId }, _identity);

    private UserAccount AddAdmin(string email) => AddWithRole(email, SystemRoles.Admin);

    private UserAccount AddUser(string email) => AddWithRole(email, SystemRoles.User);

    private UserAccount AddWithRole(string email, string role)
    {
        var user = _identity.AddUser(email);
        _identity.SetRoles(user.Id, role);

        return user;
    }
}
```

- [x] **Paso 2: correrlos y ver que fallan**

```bash
dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Features.Users.UserGuardsTests"
```

Tiene que fallar al compilar, con `error CS0246: The type or namespace name 'UserGuards' could not be found` y `error CS0117: 'UserErrors' does not contain a definition for 'CannotModifySelfCode'`.

- [x] **Paso 3: los errores nuevos y sus traducciones**

`src/ArquitecturaBase.Domain/Users/UserErrors.cs` completo:

```csharp
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Users;

public static class UserErrors
{
    public const string EmailInvalidCode = "Users.Email.Invalid";
    public const string NotFoundCode = "Users.User.NotFound";
    public const string CannotModifySelfCode = "Users.User.CannotModifySelf";
    public const string LastAdminCode = "Users.User.LastAdmin";

    public static readonly Error EmailInvalid = Error.Validation(EmailInvalidCode, "The email address is not valid.");

    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The user was not found.");

    /// <summary>Desactivar o eliminar la propia cuenta, o quitarse a uno mismo el rol Admin.</summary>
    public static readonly Error CannotModifySelf = Error.Conflict(
        CannotModifySelfCode, "This change cannot be applied to your own account.");

    /// <summary>La acción dejaría al sistema sin ningún administrador activo.</summary>
    public static readonly Error LastAdmin = Error.Conflict(
        LastAdminCode, "The system must keep at least one active administrator.");
}
```

En `src/ArquitecturaBase.Application/Resources/Errors.resx`, junto a `Users.User.NotFound`:

```xml
  <data name="Users.User.CannotModifySelf" xml:space="preserve"><value>No podés aplicar este cambio sobre tu propia cuenta.</value></data>
  <data name="Users.User.LastAdmin" xml:space="preserve"><value>Tiene que quedar al menos un administrador activo en el sistema.</value></data>
```

En `src/ArquitecturaBase.Application/Resources/Errors.en.resx`:

```xml
  <data name="Users.User.CannotModifySelf" xml:space="preserve"><value>You can't apply this change to your own account.</value></data>
  <data name="Users.User.LastAdmin" xml:space="preserve"><value>The system must keep at least one active administrator.</value></data>
```

- [x] **Paso 4: contar administradores activos desde Identity**

En `src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs`, agregar al final de la interfaz:

```csharp
    /// <summary>
    /// Cuántos usuarios activos tienen el rol Admin. Lo usa <c>UserGuards</c> para no dejar al sistema sin
    /// administradores.
    /// </summary>
    Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken);
```

En `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs`, agregar el método debajo de `GetRolesAsync`:

```csharp
    public async Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken) =>
        (await userManager.GetUsersInRoleAsync(SystemRoles.Admin)).Count(user => user.IsActive);
```

`GetUsersInRoleAsync` consulta la tabla de usuarios, así que respeta los filtros globales del modelo: cuando la Tarea 6 agregue el borrado lógico, un administrador borrado deja de contar sin tocar esta línea.

En `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`, agregar el using `using ArquitecturaBase.Domain.Authorization;` y el método al final de la clase:

```csharp
    public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_users.Count(user =>
            user.IsActive && (_roles.GetValueOrDefault(user.Id) ?? []).Contains(SystemRoles.Admin, StringComparer.Ordinal)));
```

- [x] **Paso 5: las reglas**

`src/ArquitecturaBase.Application/Features/Users/UserGuards.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Features.Users;

/// <summary>
/// Las reglas que impiden que un administrador rompa el sistema (sección 8 del spec de la Fase 4). Están acá, en un
/// solo lugar, porque las usan varios casos de uso —cambiar roles, desactivar y eliminar— y alcanza con que uno se
/// las olvide para dejar al dueño afuera. Domain no las puede resolver solo: hay que contar administradores
/// activos, y eso vive en Identity.
/// Los casos de uso llaman a estos métodos recién después de comprobar que el usuario existe.
/// </summary>
internal sealed class UserGuards(ICurrentUser currentUser, IIdentityService identityService)
{
    /// <summary>
    /// Cambiar los roles de <paramref name="userId"/> a <paramref name="roles"/>: nadie se saca a sí mismo el rol
    /// Admin y nadie le saca el rol al último administrador activo. Lo demás se puede cambiar libremente.
    /// </summary>
    public async Task<Result> EnsureRolesCanChangeAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var current = await identityService.GetRolesAsync(userId, cancellationToken);
        var keepsAdmin = roles.Contains(SystemRoles.Admin, StringComparer.Ordinal);

        if (!current.Contains(SystemRoles.Admin, StringComparer.Ordinal) || keepsAdmin)
        {
            return Result.Success();
        }

        if (userId == currentUser.UserId)
        {
            return UserErrors.CannotModifySelf;
        }

        return await EnsureAnotherAdminRemainsAsync(userId, current, cancellationToken);
    }

    /// <summary>
    /// Desactivar o eliminar <paramref name="userId"/>: nunca la propia cuenta, y nunca al último administrador
    /// activo.
    /// </summary>
    public async Task<Result> EnsureCanBeRemovedAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == currentUser.UserId)
        {
            return UserErrors.CannotModifySelf;
        }

        var roles = await identityService.GetRolesAsync(userId, cancellationToken);

        return await EnsureAnotherAdminRemainsAsync(userId, roles, cancellationToken);
    }

    // El último administrador activo no se va de ninguna de las tres formas: ni quitándole el rol, ni
    // desactivándolo, ni eliminándolo. Si ya estaba inactivo no cuenta: el sistema ya estaba sin él.
    private async Task<Result> EnsureAnotherAdminRemainsAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        if (!roles.Contains(SystemRoles.Admin, StringComparer.Ordinal))
        {
            return Result.Success();
        }

        var user = await identityService.FindByIdAsync(userId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return Result.Success();
        }

        return await identityService.CountActiveAdminsAsync(cancellationToken) > 1
            ? Result.Success()
            : UserErrors.LastAdmin;
    }
}
```

En `src/ArquitecturaBase.Application/DependencyInjection.cs`, agregar el using `using ArquitecturaBase.Application.Features.Users;` y registrar la clase dentro de `AddApplication`, antes del `return`:

```csharp
        // No la registra Scrutor: no es un handler. Va acá y no en AddFeaturesFromAssembly, que también corre para
        // el ensamblado de los tests de integración.
        services.AddScoped<UserGuards>();
```

- [x] **Paso 6: los tests unitarios en verde**

```bash
dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Features.Users.UserGuardsTests"
dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Resources.ErrorCodeTranslationTests"
```

El primero, `Passed! - Failed: 0, Passed: 8`; el segundo, `Passed!`.

- [x] **Paso 7: build y suite completa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

`0 Warning(s)` y todo en verde. `DependencyInjectionTests` del proyecto de Application comprueba que el contenedor resuelve lo registrado; si fallara, `UserGuards` quedó registrado en `AddFeaturesFromAssembly` en lugar de en `AddApplication`.

- [x] **Paso 8: commit**

```bash
git add src/ArquitecturaBase.Domain/Users/UserErrors.cs src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs src/ArquitecturaBase.Application/Features/Users/UserGuards.cs src/ArquitecturaBase.Application/DependencyInjection.cs src/ArquitecturaBase.Application/Resources/Errors.resx src/ArquitecturaBase.Application/Resources/Errors.en.resx src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs tests/ArquitecturaBase.Application.UnitTests/Features/Users/UserGuardsTests.cs
git commit -m "feat: agregar las reglas que protegen al ultimo administrador" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 6: Borrado lógico de usuarios

Repo: **backend**.

Eliminar una cuenta no puede borrar su historial de ingresos, así que `ApplicationUser` pasa a implementar `ISoftDeletable`. Es el cambio más invasivo del grupo: el filtro global afecta **todas** las consultas de `UserManager`, así que hay que revisar qué se rompe por lo que deja de verse, y qué no se arregla solo porque el índice único del correo sigue mirando las filas borradas.

**Qué hay que revisar, y qué se hace con cada cosa:**

- `UserManager.Users`, `FindByEmailAsync`, `FindByIdAsync`, `FindByLoginAsync` y `GetUsersInRoleAsync` consultan la tabla de usuarios, así que el filtro los cubre solos: un usuario borrado desaparece del listado (`GET /api/users`), de los permisos y de la cuenta de administradores activos de la Tarea 5. No hay que tocar nada de eso.
- **Lo que sí se rompe:** los dos ingresos buscan por email, no encuentran nada y **crean una cuenta nueva**, que choca con el índice único de `NormalizedEmail` y termina en un 500. Por eso se agrega `IIdentityService.IsDeletedEmailAsync` y los dos handlers la consultan antes de crear: un correo borrado responde `Auth.Account.Disabled`, el mismo error que una cuenta deshabilitada.
- **El índice único del correo no se filtra a propósito.** La dirección queda tomada, y la Tarea 7 da de alta ese correo **restaurando** la fila en lugar de insertar otra (sección 7 del spec). Para eso queda `ApplicationUser.Restore()`, que acá se escribe y ahí se usa.
- Identity mapea sus tablas dependientes (`AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`) **sin propiedades de navegación** (en el snapshot se ve `b.HasOne("...ApplicationUser", null)`), así que el filtro no dispara la advertencia de EF sobre navegaciones requeridas y esas filas quedan donde están.
- `LoginAudits.UserId` es un `Guid?` sin clave foránea: el historial de ingresos sobrevive intacto, que es lo que pide el spec.
- **Cortar la sesión que ya está abierta** (revocar los tokens de OpenIddict y rotar el `SecurityStamp`) no es de esta tarea: va con las acciones de desactivar y eliminar, en las tareas 9 y 10.

**Archivos:**
- Modificar: `src/ArquitecturaBase.Infrastructure/Identity/ApplicationUser.cs`
- Modificar: `src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs`
- Modificar: `src/ArquitecturaBase.Application/Features/Auth/VerifyLoginCode/VerifyLoginCodeCommandHandler.cs`
- Modificar: `src/ArquitecturaBase.Application/Features/Auth/SignInWithExternalProvider/SignInWithExternalProviderCommandHandler.cs`
- Crear: `src/ArquitecturaBase.Infrastructure/Persistence/Migrations/<timestamp>_UserSoftDelete.cs` (lo genera `dotnet ef`)
- Test: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Users/UserSoftDeleteTests.cs`

- [x] **Paso 1: el test de integración**

Crear `tests/ArquitecturaBase.Api.IntegrationTests/Users/UserSoftDeleteTests.cs`:

```csharp
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

[Collection(ApiTestGroup.Name)]
public sealed class UserSoftDeleteTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deleting_marks_the_row_and_hides_the_user_from_the_listing()
    {
        var email = await CreateAccountAsync("deleted");
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var deletedAtUtc = factory.Clock.GetUtcNow().UtcDateTime;
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        await DeleteAsync(email);

        using var response = await client.GetWithTokenAsync($"/api/users?search={email}", tokens.AccessToken);
        var page = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());

        var stored = await factory.ExecuteDbContextAsync(db => db.Users
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .AsNoTracking()
            .SingleAsync(user => user.Email == email, Ct));
        Assert.True(stored.IsDeleted);
        Assert.Equal(deletedAtUtc, stored.DeletedAtUtc);
    }

    [Fact]
    public async Task A_deleted_user_cannot_sign_in_again_with_a_code()
    {
        // El arnés está en Open: el pedido de código sigue su camino y la cuenta se crearía sola. Es el caso
        // peligroso, el que chocaría con el índice único del email.
        var email = await CreateAccountAsync("deletedlogin");
        await DeleteAsync(email);
        using var client = factory.CreateClient();
        var code = await client.RequestCodeAsync(factory, email);

        using var response = await client.PostJsonAsync(
            "/account/login-code/verify",
            new { email, code, returnUrl = AuthFlow.AuthorizeReturnUrl },
            language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Auth.Account.Disabled", problem.GetProperty("code").GetString());
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Users
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .CountAsync(user => user.Email == email, Ct)));
    }

    [Fact]
    public async Task A_deleted_user_cannot_sign_in_again_with_google()
    {
        var email = await CreateAccountAsync("deletedgoogle");
        await DeleteAsync(email);
        using var client = factory.CreateClient();
        using var external = await client.PostJsonAsync(
            "/test/external-login",
            new { providerKey = "google-" + email, email, name = "Ana", emailVerified = true });

        using var callback = await client.SendAsync(
            HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(AuthFlow.AuthorizeReturnUrl));

        Assert.True(external.IsSuccessStatusCode);
        Assert.Equal("/login?error=Auth.Account.Disabled", callback.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_email_of_a_deleted_user_stays_taken_in_the_database()
    {
        // El índice único no se filtra a propósito: por eso el alta de la Tarea 7 restaura en lugar de insertar.
        var email = await CreateAccountAsync("deletedemail");
        await DeleteAsync(email);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<IIdentityService>().CreateAsync(Email.Create(email).Value, null, "es", Ct)));
    }

    private Task<string> CreateAccountAsync(string prefix)
    {
        var email = TestEmails.Unique(prefix);

        return factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>()
                .CreateAsync(Email.Create(email).Value, displayName: null, "es", Ct);

            return email;
        });
    }

    private Task<int> DeleteAsync(string email) =>
        factory.ExecuteDbContextAsync(async dbContext =>
        {
            var user = await dbContext.Users.SingleAsync(candidate => candidate.Email == email, Ct);
            dbContext.Users.Remove(user);

            return await dbContext.SaveChangesAsync(Ct);
        });
}
```

- [x] **Paso 2: correrlos y ver que fallan**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UserSoftDeleteTests"
```

Tiene que fallar al compilar, con `error CS1061: 'ApplicationUser' does not contain a definition for 'IsDeleted'`.

- [x] **Paso 3: el borrado lógico en la entidad de Identity**

`src/ArquitecturaBase.Infrastructure/Identity/ApplicationUser.cs` completo:

```csharp
using ArquitecturaBase.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// Usuario de Identity con el perfil de la sección 4.3. El UserName es el email. Se borra lógicamente: el filtro
/// global lo saca de todas las consultas, pero su historial de ingresos sigue existiendo (sección 7 del spec de la
/// Fase 4). El índice único del email sigue cubriendo las filas borradas, así que dar de alta ese correo de nuevo
/// restaura la cuenta en lugar de insertar otra.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>, IAuditable, ISoftDeletable
{
    public const int DisplayNameMaxLength = 100;
    public const int CultureMaxLength = 10;
    public const int TimeZoneIdMaxLength = 64;
    public const string DefaultCulture = "es";
    public const string DefaultTimeZoneId = "America/Argentina/Buenos_Aires";

    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
    }

    public string? DisplayName { get; set; }

    public string Culture { get; set; } = DefaultCulture;

    /// <summary>Zona horaria IANA con la que el front muestra las fechas.</summary>
    public string TimeZoneId { get; set; } = DefaultTimeZoneId;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? ModifiedAtUtc { get; private set; }

    public Guid? ModifiedBy { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTime? DeletedAtUtc { get; private set; }

    public Guid? DeletedBy { get; private set; }

    /// <summary>Deshace el borrado lógico. Lo usa el alta de usuarios cuando el correo ya tuvo una cuenta.</summary>
    public void Restore()
    {
        IsDeleted = false;
        DeletedAtUtc = null;
        DeletedBy = null;
    }
}
```

No hace falta tocar `ApplicationUserConfiguration`: `ApplySoftDeleteQueryFilter` recorre el modelo y le pone el filtro a toda entidad `ISoftDeletable`, y el `SoftDeleteInterceptor` convierte el `Remove` en una marca.

- [x] **Paso 4: la migración**

```bash
dotnet ef migrations add UserSoftDelete --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api --output-dir Persistence/Migrations -- --environment Development --ConnectionStrings:appdb "Host=localhost;Port=5433;Database=appdb;Username=postgres;Password=postgres"
```

Tiene que terminar con `Done. To undo this action, use 'ef migrations remove'` y el `Up` tiene que traer exactamente tres `AddColumn` sobre `AspNetUsers`: `IsDeleted` (`boolean`, `nullable: false`, `defaultValue: false`), `DeletedAtUtc` (`timestamp with time zone`, nullable) y `DeletedBy` (`uuid`, nullable). Si apareciera algo más, es que se coló otro cambio de modelo.

- [x] **Paso 5: la consulta que ve las cuentas borradas**

En `src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs`, agregar al final:

```csharp
    /// <summary>
    /// True si ese email pertenece a una cuenta borrada lógicamente. El filtro global las oculta de todas las demás
    /// búsquedas, así que sin esto un ingreso intentaría crear una cuenta nueva y chocaría con el índice único del
    /// email.
    /// </summary>
    Task<bool> IsDeletedEmailAsync(Email email, CancellationToken cancellationToken);
```

En `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs`, agregar el método debajo de `FindByEmailAsync`:

```csharp
    public Task<bool> IsDeletedEmailAsync(Email email, CancellationToken cancellationToken)
    {
        var normalized = userManager.NormalizeEmail(email.Value);

        return userManager.Users
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .AnyAsync(user => user.IsDeleted && user.NormalizedEmail == normalized, cancellationToken);
    }
```

En `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`, el doble en memoria: agregar el conjunto y el método.

```csharp
    /// <summary>Correos con una cuenta borrada lógicamente: el doble no las guarda en <see cref="Users"/>.</summary>
    public HashSet<string> DeletedEmails { get; } = new(StringComparer.Ordinal);
```

```csharp
    public Task<bool> IsDeletedEmailAsync(Email email, CancellationToken cancellationToken) =>
        Task.FromResult(DeletedEmails.Contains(email.Value));
```

- [x] **Paso 6: los dos ingresos dejan de crear cuentas sobre un correo borrado**

En `src/ArquitecturaBase.Application/Features/Auth/VerifyLoginCode/VerifyLoginCodeCommandHandler.cs`, reemplazar la línea

```csharp
        user ??= await identityService.CreateAsync(email, displayName: null, UserCultures.FromCurrentRequest(), cancellationToken);
```

por

```csharp
        // Una cuenta borrada no aparece en ninguna búsqueda, así que sin esto se intentaría crear otra con el mismo
        // correo y el índice único la rechazaría con un 500. Se informa como cuenta deshabilitada, que es lo que es.
        if (user is null && await identityService.IsDeletedEmailAsync(email, cancellationToken))
        {
            return Fail(email, user: null, AccountErrors.Disabled, nowUtc);
        }

        user ??= await identityService.CreateAsync(email, displayName: null, UserCultures.FromCurrentRequest(), cancellationToken);
```

En `src/ArquitecturaBase.Application/Features/Auth/SignInWithExternalProvider/SignInWithExternalProviderCommandHandler.cs`, dentro del `if (user is null)` interior, entre la comprobación de `InviteOnly` y la creación:

```csharp
                if (await identityService.IsDeletedEmailAsync(email.Value, cancellationToken))
                {
                    return Fail(email.Value.Value, user: null, AccountErrors.Disabled);
                }

                user = await identityService.CreateAsync(
                    email.Value, login.DisplayName, UserCultures.FromCurrentRequest(), cancellationToken);
```

- [x] **Paso 7: los tests de integración en verde**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UserSoftDeleteTests"
```

`Passed! - Failed: 0, Passed: 4`.

- [x] **Paso 8: build y suite completa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

`0 Warning(s)` y todo en verde. Lo que hay que mirar con atención en esta corrida:

- `MigrationsTests.Model_has_no_pending_changes`: confirma que la migración del paso 4 cubre las tres columnas.
- `IdentityModelTests.Normalized_email_is_unique_in_the_database`: sigue pasando, que es justamente lo que obliga a restaurar en la Tarea 7.
- `UsersEndpointsTests`, `PermissionServiceTests` y `SeedTests`: prueban que el filtro global no rompió el listado ni los permisos.
- `SoftDeleteTests`: el filtro por nombre sigue funcionando con más de una entidad `ISoftDeletable` en el modelo.

- [x] **Paso 9: commit**

```bash
git add src/ArquitecturaBase.Infrastructure/Identity/ApplicationUser.cs src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs src/ArquitecturaBase.Infrastructure/Persistence/Migrations src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs src/ArquitecturaBase.Application/Features/Auth/VerifyLoginCode/VerifyLoginCodeCommandHandler.cs src/ArquitecturaBase.Application/Features/Auth/SignInWithExternalProvider/SignInWithExternalProviderCommandHandler.cs tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/UserSoftDeleteTests.cs
git commit -m "feat: borrar usuarios logicamente y cortarles el ingreso" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarea 7: Alta de usuarios

Repo: **backend**.

Un administrador da de alta un correo con su nombre y sus roles, y la persona entra por el camino de siempre con su código. Si el correo ya tiene una cuenta activa, `Users.AlreadyExists`; si tiene una **borrada lógicamente, se restaura** con los roles del alta, porque el correo es único y dejar una dirección inutilizable para siempre sorprende a cualquiera.

**Archivos:**
- Crear: `src/ArquitecturaBase.Domain/Authorization/RoleErrors.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Users/CreateUser/CreateUserCommand.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Users/CreateUser/CreateUserCommandValidator.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Users/CreateUser/CreateUserCommandHandler.cs`
- Modificar: `src/ArquitecturaBase.Domain/Users/UserErrors.cs`
- Modificar: `src/ArquitecturaBase.Application/Resources/Errors.resx`
- Modificar: `src/ArquitecturaBase.Application/Resources/Errors.en.resx`
- Modificar: `src/ArquitecturaBase.Application/Common/Validation/ValidationRules.cs`
- Modificar: `src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs`
- Modificar: `src/ArquitecturaBase.Api/Endpoints/Users/UsersEndpoints.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`
- Modificar: `tests/ArquitecturaBase.Api.IntegrationTests/Support/AuthFlow.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Users/CreateUserEndpointTests.cs`

- [x] **Paso 1: los dos errores nuevos en Domain**

En `src/ArquitecturaBase.Domain/Users/UserErrors.cs`, **sin tocar lo que dejó la Tarea 5** (`CannotModifySelf` y `LastAdmin`), se agrega la constante con las otras:

```csharp
    public const string AlreadyExistsCode = "Users.User.AlreadyExists";
```

y el error al final de la clase:

```csharp
    /// <summary>Alta de un correo que ya tiene una cuenta activa. Una cuenta borrada no da este error: se restaura.</summary>
    public static readonly Error AlreadyExists = Error.Conflict(AlreadyExistsCode, "An account with that email already exists.");
```

`src/ArquitecturaBase.Domain/Authorization/RoleErrors.cs`, nuevo (la Tarea 12 le suma los demás códigos):

```csharp
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Authorization;

public static class RoleErrors
{
    public const string NotFoundCode = "Roles.Role.NotFound";

    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The role was not found.");
}
```

- [x] **Paso 2: los textos, en los dos idiomas**

En `src/ArquitecturaBase.Application/Resources/Errors.resx`, antes de `</root>`:

```xml
  <data name="Users.User.AlreadyExists" xml:space="preserve"><value>Ya existe una cuenta con ese correo.</value></data>
  <data name="Roles.Role.NotFound" xml:space="preserve"><value>No encontramos el rol.</value></data>
```

En `src/ArquitecturaBase.Application/Resources/Errors.en.resx`, antes de `</root>`:

```xml
  <data name="Users.User.AlreadyExists" xml:space="preserve"><value>An account with that email already exists.</value></data>
  <data name="Roles.Role.NotFound" xml:space="preserve"><value>We couldn't find the role.</value></data>
```

- [x] **Paso 3: un helper para pedir con token y cuerpo**

`GetWithTokenAsync` solo sirve para GET. En `tests/ArquitecturaBase.Api.IntegrationTests/Support/AuthFlow.cs` se agrega `using System.Net.Http.Json;` arriba (junto a los otros `using`) y este método al final de la clase `AuthFlow`, después de `GetWithTokenAsync`:

```csharp
    /// <summary>Cualquier método con el access token, y con cuerpo JSON si se pasa uno.</summary>
    public static async Task<HttpResponseMessage> SendWithTokenAsync(
        this HttpClient client,
        HttpMethod method,
        string url,
        string accessToken,
        object? body = null,
        string? language = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (language is not null)
        {
            request.Headers.AcceptLanguage.ParseAdd(language);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
```

- [x] **Paso 4: el test de integración, antes que el endpoint**

`tests/ArquitecturaBase.Api.IntegrationTests/Users/CreateUserEndpointTests.cs`:

```csharp
using System.Globalization;
using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

[Collection(ApiTestGroup.Name)]
public sealed class CreateUserEndpointTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Admin_creates_a_user_with_the_requested_roles()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var email = TestEmails.Unique("alta");

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post,
            "/api/users",
            tokens.AccessToken,
            new { email, displayName = "Ana", roles = new[] { SystemRoles.Admin } });
        var userId = JsonSerializer.Deserialize<Guid>((await response.ReadJsonAsync()).GetRawText());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([SystemRoles.Admin], await RolesOfAsync(userId));
        Assert.True(await factory.ExecuteDbContextAsync(db =>
            db.Users.Where(user => user.Id == userId).Select(user => user.IsActive && user.EmailConfirmed).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Without_roles_the_new_user_gets_the_user_role()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", tokens.AccessToken, new { email = TestEmails.Unique("sinroles") });
        var userId = JsonSerializer.Deserialize<Guid>((await response.ReadJsonAsync()).GetRawText());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([SystemRoles.User], await RolesOfAsync(userId));
    }

    [Fact]
    public async Task An_email_with_an_active_account_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var email = TestEmails.Unique("repetido");
        using var first = await client.SendWithTokenAsync(HttpMethod.Post, "/api/users", tokens.AccessToken, new { email });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", tokens.AccessToken, new { email }, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.AlreadyExistsCode, problem.GetProperty("code").GetString());
        Assert.Equal("Ya existe una cuenta con ese correo.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task An_email_with_a_deleted_account_is_restored_with_the_new_roles()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var email = TestEmails.Unique("restaurar");
        using var first = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", tokens.AccessToken, new { email, roles = new[] { SystemRoles.Admin } });
        var original = JsonSerializer.Deserialize<Guid>((await first.ReadJsonAsync()).GetRawText());

        // El borrado lógico, directo contra la base: el endpoint que lo hace es de la Tarea 10.
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Users.Remove(await db.Users.SingleAsync(user => user.Id == original, Ct));
            return await db.SaveChangesAsync(Ct);
        });

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", tokens.AccessToken, new { email, displayName = "Vuelta", roles = new[] { SystemRoles.User } });
        var restored = JsonSerializer.Deserialize<Guid>((await response.ReadJsonAsync()).GetRawText());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(original, restored);
        Assert.Equal([SystemRoles.User], await RolesOfAsync(restored));
        Assert.Equal("Vuelta", await factory.ExecuteDbContextAsync(db =>
            db.Users.Where(user => user.Id == restored).Select(user => user.DisplayName).SingleAsync(Ct)));
    }

    [Fact]
    public async Task A_role_that_does_not_exist_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post,
            "/api/users",
            tokens.AccessToken,
            new { email = TestEmails.Unique("rolraro"), roles = new[] { "Inventado" } });
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(RoleErrors.NotFoundCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_invalid_email_is_rejected_with_field_errors()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", tokens.AccessToken, new { email = "no-es-un-correo" }, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Ingresá un correo válido.", problem.GetProperty("errors").GetProperty("email")[0].GetString());
    }

    [Fact]
    public async Task Creating_a_user_requires_the_users_manage_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("sinpermiso"));

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", tokens.AccessToken, new { email = TestEmails.Unique("nada") }, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("No tenés permiso para realizar esta acción.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public void The_display_name_limit_matches_the_column()
    {
        Assert.Equal(ApplicationUser.DisplayNameMaxLength, ValidationRules.DisplayNameMaxLength);
    }

    private async Task<string[]> RolesOfAsync(Guid userId) =>
        await factory.ExecuteDbContextAsync(db => db.Roles
            .Where(role => db.UserRoles.Any(userRole => userRole.UserId == userId && userRole.RoleId == role.Id))
            .Select(role => role.Name!)
            .OrderBy(name => name)
            .ToArrayAsync(Ct));
}
```

> `Guid.ToString("D", CultureInfo.InvariantCulture)` no se usa acá, pero el `using System.Globalization;` hace falta para que `JsonSerializer.Deserialize<Guid>` no dispare IDE0005: si el analizador lo marca como innecesario, sacarlo.

- [x] **Paso 5: correr el test y ver que falla por la razón correcta**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.CreateUserEndpointTests"
```

Solo `/api/users` con GET está mapeado, así que el POST no encuentra método. Tiene que fallar con:

```
Assert.Equal() Failure: Values differ
Expected: OK
Actual:   MethodNotAllowed
```

(`The_display_name_limit_matches_the_column` falla a la compilación si todavía no existe `ValidationRules.DisplayNameMaxLength`; se agrega en el Paso 8.)

- [x] **Paso 6: los métodos nuevos de IIdentityService**

En `src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs`, después de `Task<IReadOnlyCollection<string>> GetRolesAsync(...)`:

```csharp
    /// <summary>El usuario con ese email que está borrado lógicamente, o null. Lo usa el alta para restaurarlo.</summary>
    Task<UserAccount?> FindDeletedByEmailAsync(Email email, CancellationToken cancellationToken);

    /// <summary>Deshace el borrado lógico, deja la cuenta activa y le pone el nombre del alta.</summary>
    Task RestoreAsync(Guid userId, string? displayName, CancellationToken cancellationToken);

    /// <summary>Deja al usuario exactamente con esos roles: agrega los que faltan y saca los que sobran.</summary>
    Task SetRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken);

    /// <summary>Los nombres de todos los roles. El alta y la edición validan contra esta lista.</summary>
    Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken);
```

- [x] **Paso 7: implementarlos en IdentityService**

En `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs`:

1. Los `using` pasan a incluir `ArquitecturaBase.Infrastructure.Persistence;` (el de `Persistence.Extensions`, que trae `ModelBuilderExtensions`, ya está: lo usa `ApplySort`).
2. El constructor primario suma el contexto, que las tareas 8, 11 y 12 van a necesitar para proyectar roles y permisos:

```csharp
internal sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ApplicationDbContext dbContext,
    IOptions<SeedOptions> seedOptions)
    : IIdentityService
```

3. En `CreateAsync`, la asignación de `DisplayName` pasa a usar el helper:

```csharp
            DisplayName = TrimDisplayName(displayName),
```

4. Después de `GetRolesAsync` se agregan los cuatro métodos:

```csharp
    public async Task<UserAccount?> FindDeletedByEmailAsync(Email email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        var normalized = userManager.NormalizeEmail(email.Value);

        // Mismo estilo que IsDeletedEmailAsync (Tarea 6): se saltea solo el filtro del borrado lógico.
        return ToAccountOrNull(await userManager.Users
            .AsNoTracking()
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .FirstOrDefaultAsync(user => user.IsDeleted && user.NormalizedEmail == normalized, cancellationToken));
    }

    public async Task RestoreAsync(Guid userId, string? displayName, CancellationToken cancellationToken)
    {
        var user = await userManager.Users
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("The user does not exist.");

        // ApplicationUser.Restore(), que dejó la Tarea 6, limpia IsDeleted, DeletedAtUtc y DeletedBy.
        user.Restore();
        user.IsActive = true;
        user.DisplayName = TrimDisplayName(displayName);

        (await userManager.UpdateAsync(user)).EnsureSucceeded("restore the user");
    }

    public async Task SetRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var user = await RequireUserAsync(userId, cancellationToken);
        var current = await userManager.GetRolesAsync(user);

        var removed = current.Except(roles, StringComparer.Ordinal).ToList();

        if (removed.Count > 0)
        {
            (await userManager.RemoveFromRolesAsync(user, removed)).EnsureSucceeded("remove the roles");
        }

        var added = roles.Except(current, StringComparer.Ordinal).ToList();

        if (added.Count > 0)
        {
            (await userManager.AddToRolesAsync(user, added)).EnsureSucceeded("assign the roles");
        }
    }

    public async Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken) =>
        await dbContext.Roles
            .AsNoTracking()
            .Select(role => role.Name!)
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);
```

5. Y el helper, junto a los otros privados del final:

```csharp
    private static string? TrimDisplayName(string? displayName) =>
        displayName is { Length: > ApplicationUser.DisplayNameMaxLength }
            ? displayName[..ApplicationUser.DisplayNameMaxLength]
            : displayName;
```

- [x] **Paso 8: el doble de pruebas y el límite del nombre**

En `src/ArquitecturaBase.Application/Common/Validation/ValidationRules.cs`, junto a `EmailMaxLength`:

```csharp
    /// <summary>Tiene que coincidir con ApplicationUser.DisplayNameMaxLength: Application no ve Infrastructure.</summary>
    public const int DisplayNameMaxLength = 100;
```

En `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`, al final de la clase:

```csharp
    public Task<UserAccount?> FindDeletedByEmailAsync(Email email, CancellationToken cancellationToken) =>
        Task.FromResult(DeletedUsers.SingleOrDefault(user => user.Email == email.Value));

    public Task RestoreAsync(Guid userId, string? displayName, CancellationToken cancellationToken)
    {
        var user = DeletedUsers.Single(user => user.Id == userId);
        DeletedUsers.Remove(user);

        // DeletedEmails lo dejó la Tarea 6 y lo lee IsDeletedEmailAsync: los dos tienen que decir lo mismo.
        DeletedEmails.Remove(user.Email);
        _users.Add(user with { DisplayName = displayName, IsActive = true });
        _roles[user.Id] = [];

        return Task.CompletedTask;
    }

    public Task SetRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        _roles[userId] = [.. roles];

        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>(RoleNames);
```

y, junto a las otras colecciones públicas de arriba:

```csharp
    /// <summary>Cuentas borradas lógicamente: las ve el alta, que las restaura. Acompaña a DeletedEmails (Tarea 6).</summary>
    public List<UserAccount> DeletedUsers { get; } = [];

    /// <summary>Los roles que existen en el sistema. El alta y la edición validan contra esta lista.</summary>
    public List<string> RoleNames { get; } = ["Admin", "User"];
```

- [x] **Paso 9: el caso de uso**

`src/ArquitecturaBase.Application/Features/Users/CreateUser/CreateUserCommand.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.CreateUser;

/// <summary>Alta de un correo por un administrador (sección 7 del spec de la Fase 4). Sin roles, la cuenta queda como User.</summary>
public sealed record CreateUserCommand(string? Email, string? DisplayName, IReadOnlyCollection<string>? Roles)
    : ICommand<Guid>;
```

`src/ArquitecturaBase.Application/Features/Users/CreateUser/CreateUserCommandValidator.cs`:

```csharp
using ArquitecturaBase.Application.Common.Validation;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Users.CreateUser;

internal sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(command => command.Email).ValidEmail();
        RuleFor(command => command.DisplayName).MaxLength(ValidationRules.DisplayNameMaxLength);
    }
}
```

`src/ArquitecturaBase.Application/Features/Users/CreateUser/CreateUserCommandHandler.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Features.Users.CreateUser;

internal sealed class CreateUserCommandHandler(IIdentityService identityService)
    : ICommandHandler<CreateUserCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var emailResult = Email.Create(command.Email);

        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var email = emailResult.Value;

        // Sin roles en el alta, la cuenta queda igual que una que se creó sola al ingresar.
        IReadOnlyCollection<string> roles = command.Roles is { Count: > 0 }
            ? [.. command.Roles.Distinct(StringComparer.Ordinal)]
            : [SystemRoles.User];

        var known = await identityService.ListRoleNamesAsync(cancellationToken);

        if (roles.Any(role => !known.Contains(role, StringComparer.Ordinal)))
        {
            return RoleErrors.NotFound;
        }

        if (await identityService.FindByEmailAsync(email, cancellationToken) is not null)
        {
            return UserErrors.AlreadyExists;
        }

        // El correo es único: una cuenta borrada se restaura en lugar de fallar, y queda con los roles del alta,
        // no con los que tenía antes (sección 7 del spec de la Fase 4).
        var deleted = await identityService.FindDeletedByEmailAsync(email, cancellationToken);

        Guid userId;

        if (deleted is null)
        {
            var created = await identityService.CreateAsync(
                email, command.DisplayName, UserCultures.FromCurrentRequest(), cancellationToken);
            userId = created.Id;
        }
        else
        {
            userId = deleted.Id;
            await identityService.RestoreAsync(userId, command.DisplayName, cancellationToken);
        }

        await identityService.SetRolesAsync(userId, roles, cancellationToken);

        return userId;
    }
}
```

- [x] **Paso 10: el endpoint**

`src/ArquitecturaBase.Api/Endpoints/Users/UsersEndpoints.cs` queda así:

```csharp
using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Features.Users.CreateUser;
using ArquitecturaBase.Application.Features.Users.GetUsers;
using ArquitecturaBase.Domain.Authorization;

namespace ArquitecturaBase.Api.Endpoints.Users;

/// <summary>
/// ABM de usuarios (sección 10 del spec de la Fase 4). El listado es el ejemplo del patrón de paginado
/// (sección 6.2): los parámetros se enlazan a mano porque [AsParameters] haría obligatorios los int de PagedRequest.
/// </summary>
internal sealed class UsersEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users");

        group.MapGet("", async (
                int? page,
                int? pageSize,
                string? sort,
                string? search,
                IQueryHandler<GetUsersQuery, PagedResult<UserListItem>> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(
                new GetUsersQuery
                {
                    Page = page ?? PagedRequest.DefaultPage,
                    PageSize = pageSize ?? PagedRequest.DefaultPageSize,
                    Sort = sort,
                    Search = search,
                },
                cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Users.Read);

        group.MapPost("", async (
                CreateUserCommand command,
                ICommandHandler<CreateUserCommand, Guid> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(command, cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Users.Manage);
    }
}
```

- [x] **Paso 11: correr el test y verlo pasar**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.CreateUserEndpointTests"
```

Tienen que pasar los 8 tests (`Passed! - Failed: 0, Passed: 8`).

- [x] **Paso 12: build y suite completa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

`dotnet build` sin advertencias y `dotnet test` en verde. Pegar la salida real.

- [x] **Paso 13: commit**

```bash
git add src/ArquitecturaBase.Domain/Authorization/RoleErrors.cs src/ArquitecturaBase.Domain/Users/UserErrors.cs src/ArquitecturaBase.Application/Resources/Errors.resx src/ArquitecturaBase.Application/Resources/Errors.en.resx src/ArquitecturaBase.Application/Common/Validation/ValidationRules.cs src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs src/ArquitecturaBase.Application/Features/Users/CreateUser src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs src/ArquitecturaBase.Api/Endpoints/Users/UsersEndpoints.cs tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs tests/ArquitecturaBase.Api.IntegrationTests/Support/AuthFlow.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/CreateUserEndpointTests.cs
git commit -m "feat: dar de alta usuarios desde el panel" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 8: Edición: nombre y roles

Repo: **backend**.

El detalle de un usuario con sus roles, y la edición de su nombre y sus roles. Acá entran por primera vez las reglas de protección de la Tarea 5: nadie se saca a sí mismo el rol `Admin` y nunca puede quedar el sistema sin un administrador activo.

> **Lo que ya dejó la Tarea 5.** Esta tarea y las dos siguientes **usan** `UserGuards`, que vive en `src/ArquitecturaBase.Application/Features/Users/UserGuards.cs`, está registrado como scoped en `AddApplication` y se inyecta como cualquier otra dependencia:
>
> ```csharp
> Task<Result> EnsureRolesCanChangeAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken);
> Task<Result> EnsureCanBeRemovedAsync(Guid userId, CancellationToken cancellationToken);
> ```
>
> Las reglas ya están escritas y probadas ahí: no se redefinen ni se duplican. Los dos errores que devuelven, `UserErrors.CannotModifySelf` y `UserErrors.LastAdmin`, son `Error.Conflict`, así que el endpoint responde **409**. `UserGuards` se llama recién después de comprobar que el usuario existe, y él mismo consulta los roles actuales y `IIdentityService.CountActiveAdminsAsync`.

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Features/Users/GetUser/UserDetail.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Users/GetUser/GetUserQuery.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Users/GetUser/GetUserQueryHandler.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Users/UpdateUser/UpdateUserCommand.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Users/UpdateUser/UpdateUserCommandValidator.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Users/UpdateUser/UpdateUserCommandHandler.cs`
- Modificar: `src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs`
- Modificar: `src/ArquitecturaBase.Api/Endpoints/Users/UsersEndpoints.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Users/UpdateUserEndpointTests.cs`

- [x] **Paso 1: el test, antes que el endpoint**

`tests/ArquitecturaBase.Api.IntegrationTests/Users/UpdateUserEndpointTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

[Collection(ApiTestGroup.Name)]
public sealed class UpdateUserEndpointTests(ApiFactory factory)
{
    [Fact]
    public async Task The_detail_shows_the_profile_and_the_roles()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var email = TestEmails.Unique("detalle");
        var userId = await CreateAsync(client, tokens.AccessToken, email, "Ana", [SystemRoles.User]);

        using var response = await client.GetWithTokenAsync($"/api/users/{userId}", tokens.AccessToken);
        var user = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(email, user.GetProperty("email").GetString());
        Assert.Equal("Ana", user.GetProperty("displayName").GetString());
        Assert.True(user.GetProperty("isActive").GetBoolean());
        Assert.Equal([SystemRoles.User], Strings(user, "roles"));
        Assert.EndsWith("Z", user.GetProperty("createdAtUtc").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_id_that_does_not_exist_is_not_found()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync($"/api/users/{Guid.CreateVersion7()}", tokens.AccessToken);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(UserErrors.NotFoundCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Editing_changes_the_name_and_replaces_the_roles()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var userId = await CreateAsync(client, tokens.AccessToken, TestEmails.Unique("editar"), "Ana", [SystemRoles.User]);

        using var update = await client.SendWithTokenAsync(
            HttpMethod.Put,
            $"/api/users/{userId}",
            tokens.AccessToken,
            new { displayName = "Ana María", roles = new[] { SystemRoles.Admin } });
        using var read = await client.GetWithTokenAsync($"/api/users/{userId}", tokens.AccessToken);
        var user = await read.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.Equal("Ana María", user.GetProperty("displayName").GetString());
        Assert.Equal([SystemRoles.Admin], Strings(user, "roles"));
    }

    [Fact]
    public async Task Nobody_takes_the_admin_role_away_from_themselves()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        using var me = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var myId = (await me.ReadJsonAsync()).GetProperty("id").GetGuid();

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Put,
            $"/api/users/{myId}",
            tokens.AccessToken,
            new { displayName = (string?)null, roles = new[] { SystemRoles.User } });
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.CannotModifySelfCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_role_that_does_not_exist_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var userId = await CreateAsync(client, tokens.AccessToken, TestEmails.Unique("rolraro2"), null, [SystemRoles.User]);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Put, $"/api/users/{userId}", tokens.AccessToken, new { roles = new[] { "Inventado" } });
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(RoleErrors.NotFoundCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Editing_without_roles_is_rejected_with_field_errors()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var userId = await CreateAsync(client, tokens.AccessToken, TestEmails.Unique("sinrol"), null, [SystemRoles.User]);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Put, $"/api/users/{userId}", tokens.AccessToken, new { roles = Array.Empty<string>() }, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Este campo es obligatorio.", problem.GetProperty("errors").GetProperty("roles")[0].GetString());
    }

    [Fact]
    public async Task Reading_a_user_requires_the_users_read_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("sinlectura"));

        using var response = await client.GetWithTokenAsync($"/api/users/{Guid.CreateVersion7()}", tokens.AccessToken, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("No tenés permiso para realizar esta acción.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Editing_a_user_requires_the_users_manage_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("sinedicion"));

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Put,
            $"/api/users/{Guid.CreateVersion7()}",
            tokens.AccessToken,
            new { roles = new[] { SystemRoles.User } },
            language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("No tenés permiso para realizar esta acción.", problem.GetProperty("detail").GetString());
    }

    private static async Task<Guid> CreateAsync(
        HttpClient client, string accessToken, string email, string? displayName, string[] roles)
    {
        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", accessToken, new { email, displayName, roles });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonSerializer.Deserialize<Guid>((await response.ReadJsonAsync()).GetRawText());
    }

    private static string[] Strings(JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray().Select(item => item.GetString()!).ToArray();
}
```

- [x] **Paso 2: correr el test y ver que falla por la razón correcta**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UpdateUserEndpointTests"
```

Ni `GET /api/users/{id}` ni `PUT /api/users/{id}` están mapeados, así que no hay ruta. Tiene que fallar con:

```
Assert.Equal() Failure: Values differ
Expected: OK
Actual:   NotFound
```

- [x] **Paso 3: los métodos nuevos de IIdentityService**

En `src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs` se agrega el `using ArquitecturaBase.Application.Features.Users.GetUser;` arriba y, después de `ListRoleNamesAsync`:

```csharp
    /// <summary>El detalle del usuario con sus roles, o null si no existe.</summary>
    Task<UserDetail?> FindDetailAsync(Guid userId, CancellationToken cancellationToken);

    Task SetDisplayNameAsync(Guid userId, string? displayName, CancellationToken cancellationToken);
```

No hace falta nada para contar administradores: `CountActiveAdminsAsync` ya lo dejó la Tarea 5 y lo usa `UserGuards`.

- [x] **Paso 4: implementarlos en IdentityService**

En `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs` se agrega el `using ArquitecturaBase.Application.Features.Users.GetUser;` y, después de `ListRoleNamesAsync`:

```csharp
    public async Task<UserDetail?> FindDetailAsync(Guid userId, CancellationToken cancellationToken)
    {
        var detail = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                user.Id,
                user.Email,
                user.DisplayName,
                user.IsActive,
                user.CreatedAtUtc,
                Roles = dbContext.Roles
                    .Where(role => dbContext.UserRoles.Any(userRole => userRole.UserId == user.Id && userRole.RoleId == role.Id))
                    .Select(role => role.Name!)
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return detail is null
            ? null
            : new UserDetail(
                detail.Id,
                detail.Email!,
                detail.DisplayName,
                detail.IsActive,
                detail.CreatedAtUtc,
                [.. detail.Roles.Order(StringComparer.Ordinal)]);
    }

    public async Task SetDisplayNameAsync(Guid userId, string? displayName, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        user.DisplayName = TrimDisplayName(displayName);

        (await userManager.UpdateAsync(user)).EnsureSucceeded("update the display name");
    }
```

- [x] **Paso 5: el doble de pruebas**

En `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`, al final de la clase (hace falta `using ArquitecturaBase.Application.Features.Users.GetUser;`):

```csharp
    public Task<UserDetail?> FindDetailAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = _users.SingleOrDefault(user => user.Id == userId);

        return Task.FromResult(user is null
            ? null
            : new UserDetail(
                user.Id,
                user.Email,
                user.DisplayName,
                user.IsActive,
                default,
                [.. (_roles.GetValueOrDefault(user.Id) ?? []).Order(StringComparer.Ordinal)]));
    }

    public Task SetDisplayNameAsync(Guid userId, string? displayName, CancellationToken cancellationToken)
    {
        var index = _users.FindIndex(user => user.Id == userId);
        _users[index] = _users[index] with { DisplayName = displayName };

        return Task.CompletedTask;
    }
```

- [x] **Paso 6: la consulta del detalle**

`src/ArquitecturaBase.Application/Features/Users/GetUser/UserDetail.cs`:

```csharp
namespace ArquitecturaBase.Application.Features.Users.GetUser;

/// <summary>El usuario con sus roles: lo que necesita el diálogo de edición (sección 10 del spec de la Fase 4).</summary>
public sealed record UserDetail(
    Guid Id,
    string Email,
    string? DisplayName,
    bool IsActive,
    DateTime CreatedAtUtc,
    IReadOnlyCollection<string> Roles);
```

`src/ArquitecturaBase.Application/Features/Users/GetUser/GetUserQuery.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.GetUser;

public sealed record GetUserQuery(Guid UserId) : IQuery<UserDetail>;
```

`src/ArquitecturaBase.Application/Features/Users/GetUser/GetUserQueryHandler.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Features.Users.GetUser;

internal sealed class GetUserQueryHandler(IIdentityService identityService)
    : IQueryHandler<GetUserQuery, UserDetail>
{
    public async Task<Result<UserDetail>> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await identityService.FindDetailAsync(query.UserId, cancellationToken) is { } detail
            ? detail
            : UserErrors.NotFound;
    }
}
```

- [x] **Paso 7: el comando de edición**

`src/ArquitecturaBase.Application/Features/Users/UpdateUser/UpdateUserCommand.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.UpdateUser;

/// <summary>Nombre y roles de un usuario. Los roles reemplazan a los que tenía, no se suman.</summary>
public sealed record UpdateUserCommand(Guid UserId, string? DisplayName, IReadOnlyCollection<string>? Roles) : ICommand;
```

`src/ArquitecturaBase.Application/Features/Users/UpdateUser/UpdateUserCommandValidator.cs`:

```csharp
using ArquitecturaBase.Application.Common.Validation;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Users.UpdateUser;

internal sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(command => command.DisplayName).MaxLength(ValidationRules.DisplayNameMaxLength);

        // A diferencia del alta, la edición manda siempre la lista completa: sin roles no se sabe si es
        // "dejalos como están" o "saquenle todos".
        RuleFor(command => command.Roles).Required();
    }
}
```

`src/ArquitecturaBase.Application/Features/Users/UpdateUser/UpdateUserCommandHandler.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Features.Users.UpdateUser;

internal sealed class UpdateUserCommandHandler(IIdentityService identityService, UserGuards guards)
    : ICommandHandler<UpdateUserCommand>
{
    public async Task<Result> Handle(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await identityService.FindByIdAsync(command.UserId, cancellationToken) is null)
        {
            return UserErrors.NotFound;
        }

        IReadOnlyCollection<string> roles = [.. command.Roles!.Distinct(StringComparer.Ordinal)];
        var known = await identityService.ListRoleNamesAsync(cancellationToken);

        if (roles.Any(role => !known.Contains(role, StringComparer.Ordinal)))
        {
            return RoleErrors.NotFound;
        }

        // Las reglas de la sección 8 del spec, que escribió la Tarea 5: nadie se saca a sí mismo el rol Admin y
        // siempre queda al menos un administrador activo.
        var allowed = await guards.EnsureRolesCanChangeAsync(command.UserId, roles, cancellationToken);

        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        await identityService.SetDisplayNameAsync(command.UserId, command.DisplayName, cancellationToken);
        await identityService.SetRolesAsync(command.UserId, roles, cancellationToken);

        return Result.Success();
    }
}
```

- [x] **Paso 8: las dos rutas nuevas**

En `src/ArquitecturaBase.Api/Endpoints/Users/UsersEndpoints.cs` se agregan los `using` de `ArquitecturaBase.Application.Features.Users.GetUser;` y `ArquitecturaBase.Application.Features.Users.UpdateUser;`, y al final del método `MapEndpoint`, después del `MapPost(""...)`:

```csharp
        group.MapGet("/{id:guid}", async (
                Guid id,
                IQueryHandler<GetUserQuery, UserDetail> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new GetUserQuery(id), cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Users.Read);

        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateUserRequest request,
                ICommandHandler<UpdateUserCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(
                new UpdateUserCommand(id, request.DisplayName, request.Roles), cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Users.Manage);
```

y, después de la clase `UsersEndpoints`, en el mismo archivo:

```csharp
/// <summary>El cuerpo de PUT /api/users/{id}: el id va en la ruta, no en el JSON.</summary>
public sealed record UpdateUserRequest(string? DisplayName, IReadOnlyCollection<string>? Roles);
```

- [x] **Paso 9: correr el test y verlo pasar**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UpdateUserEndpointTests"
```

Tienen que pasar los 8 tests (`Passed! - Failed: 0, Passed: 8`).

- [x] **Paso 10: build y suite completa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

`dotnet build` sin advertencias y `dotnet test` en verde.

- [x] **Paso 11: commit**

```bash
git add src/ArquitecturaBase.Application/Features/Users/GetUser src/ArquitecturaBase.Application/Features/Users/UpdateUser src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs src/ArquitecturaBase.Api/Endpoints/Users/UsersEndpoints.cs tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/UpdateUserEndpointTests.cs
git commit -m "feat: editar el nombre y los roles de un usuario" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarea 9: Activar y desactivar, cortando el acceso de verdad

Repo: **backend**.

Es la tarea que más importa de este grupo. Marcar `IsActive = false` no impide nada: quien ya entró tiene un access token que vale 15 minutos y una cookie que vale 30 días, así que seguiría trabajando como si nada. Desactivar tiene que, además, **revocar las autorizaciones y los tokens de esa persona en OpenIddict** (con `EnableTokenEntryValidation` ya activo, sus access y refresh tokens dejan de valer en el acto) y **renovar su `SecurityStamp`**, para que la cookie tampoco sirva. Y el validador del security stamp tiene que revisar en cada petición, no cada 30 minutos, que es su valor por defecto.

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Features/Users/SetUserActive/SetUserActiveCommand.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Users/SetUserActive/SetUserActiveCommandHandler.cs`
- Modificar: `src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Identity/IdentityRegistration.cs`
- Modificar: `src/ArquitecturaBase.Api/Endpoints/Users/UsersEndpoints.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Users/DeactivateUserEndpointTests.cs`

- [x] **Paso 1: el test, antes que nada**

`tests/ArquitecturaBase.Api.IntegrationTests/Users/DeactivateUserEndpointTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

[Collection(ApiTestGroup.Name)]
public sealed class DeactivateUserEndpointTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deactivating_cuts_off_the_session_that_was_already_open()
    {
        // La víctima entra de verdad: le queda el access token, el refresh token y la cookie.
        using var victim = factory.CreateClient();
        var email = TestEmails.Unique("corte");
        var tokens = await victim.LoginAsync(factory, email);
        var userId = await IdOfAsync(email);

        using var before = await victim.GetWithTokenAsync("/test/protected", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, before.StatusCode);

        using var admin = factory.CreateClient();
        var adminTokens = await admin.LoginAsync(factory, ApiFactory.AdminEmail);
        using var deactivate = await admin.SendWithTokenAsync(
            HttpMethod.Post, $"/api/users/{userId}/deactivate", adminTokens.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        // El access token que ya tenía deja de valer, sin esperar los 15 minutos.
        using var after = await victim.GetWithTokenAsync("/test/protected", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);

        // El refresh token tampoco sirve para conseguir uno nuevo.
        using var refresh = await victim.RefreshAsync(tokens.RefreshToken);
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);

        // Y la cookie ya no alcanza para pedir otro authorization code: vuelve al login.
        using var authorize = await victim.AuthorizeAsync(Pkce.ChallengeOf(Pkce.CreateVerifier()));
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.StartsWith("/login?returnUrl=", authorize.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deactivating_renews_the_security_stamp()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var email = TestEmails.Unique("stamp");
        var userId = await CreateAsync(client, tokens.AccessToken, email);
        var before = await SecurityStampOfAsync(userId);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, $"/api/users/{userId}/deactivate", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotEqual(before, await SecurityStampOfAsync(userId));
    }

    [Fact]
    public async Task Activating_puts_the_account_back()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var userId = await CreateAsync(client, tokens.AccessToken, TestEmails.Unique("reactivar"));
        using var off = await client.SendWithTokenAsync(HttpMethod.Post, $"/api/users/{userId}/deactivate", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);

        using var on = await client.SendWithTokenAsync(HttpMethod.Post, $"/api/users/{userId}/activate", tokens.AccessToken);
        using var read = await client.GetWithTokenAsync($"/api/users/{userId}", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, on.StatusCode);
        Assert.True((await read.ReadJsonAsync()).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Nobody_deactivates_their_own_account()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        using var me = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var myId = (await me.ReadJsonAsync()).GetProperty("id").GetGuid();

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, $"/api/users/{myId}/deactivate", tokens.AccessToken);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.CannotModifySelfCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_id_that_does_not_exist_is_not_found()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, $"/api/users/{Guid.CreateVersion7()}/deactivate", tokens.AccessToken);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(UserErrors.NotFoundCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Deactivating_requires_the_users_manage_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("sinbaja"));

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, $"/api/users/{Guid.CreateVersion7()}/deactivate", tokens.AccessToken, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("No tenés permiso para realizar esta acción.", problem.GetProperty("detail").GetString());
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string accessToken, string email)
    {
        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", accessToken, new { email, roles = new[] { SystemRoles.User } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonSerializer.Deserialize<Guid>((await response.ReadJsonAsync()).GetRawText());
    }

    private Task<Guid> IdOfAsync(string email) =>
        factory.ExecuteDbContextAsync(db => db.Users.Where(user => user.Email == email).Select(user => user.Id).SingleAsync(Ct));

    private Task<string?> SecurityStampOfAsync(Guid userId) =>
        factory.ExecuteDbContextAsync(db => db.Users.Where(user => user.Id == userId).Select(user => user.SecurityStamp).SingleAsync(Ct));
}
```

- [x] **Paso 2: correr el test y ver que falla por la razón correcta**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.DeactivateUserEndpointTests"
```

No hay ninguna ruta `/api/users/{id}/deactivate`. Tiene que fallar con:

```
Assert.Equal() Failure: Values differ
Expected: NoContent
Actual:   NotFound
```

- [x] **Paso 3: el security stamp se revisa en cada petición**

Por defecto, `SecurityStampValidatorOptions.ValidationInterval` es de 30 minutos: renovar el stamp no tendría efecto hasta media hora después. En `src/ArquitecturaBase.Infrastructure/Identity/IdentityRegistration.cs`, justo después del bloque `services.AddOptions<IdentityOptions>()...`:

```csharp
        // Desactivar o eliminar corta el acceso en el momento (sección 7 del spec de la Fase 4): el security stamp
        // se revisa en cada petición y no cada 30 minutos. Solo /account y /connect usan la cookie, así que la
        // consulta extra no pesa.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
```

(`SecurityStampValidatorOptions` está en `Microsoft.AspNetCore.Identity`, que el archivo ya importa.)

- [x] **Paso 4: los métodos nuevos de IIdentityService**

En `src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs`, después de `SetDisplayNameAsync`:

```csharp
    Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken);

    /// <summary>
    /// Corta el acceso que ya se entregó: revoca las autorizaciones y los tokens de OpenIddict de esa persona y le
    /// renueva el security stamp, con lo que su cookie de Identity deja de valer.
    /// </summary>
    Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken);
```

- [x] **Paso 5: implementarlos en IdentityService**

En `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs`:

1. Se agrega `using System.Globalization;` y `using OpenIddict.Abstractions;` a los `using`.
2. El constructor primario suma los dos managers de OpenIddict:

```csharp
internal sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ApplicationDbContext dbContext,
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictTokenManager tokenManager,
    IOptions<SeedOptions> seedOptions)
    : IIdentityService
```

3. Después de `SetDisplayNameAsync`:

```csharp
    public async Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        user.IsActive = isActive;

        (await userManager.UpdateAsync(user)).EnsureSucceeded("update the account status");
    }

    public async Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);

        // La cookie de Identity deja de valer en la próxima petición: el validador del security stamp la rechaza
        // (ValidationInterval está en cero, ver IdentityRegistration).
        (await userManager.UpdateSecurityStampAsync(user)).EnsureSucceeded("renew the security stamp");

        // El subject es el mismo que pone OpenIdPrincipalFactory en el claim "sub".
        var subject = userId.ToString("D", CultureInfo.InvariantCulture);

        // Primero las autorizaciones y después los tokens: si entre las dos llamadas se emitiera un token a partir
        // de una autorización que ya está revocada, la segunda llamada igual lo alcanza. Con
        // EnableTokenEntryValidation, un token revocado deja de valer en el acto, sin esperar a que venza.
        await authorizationManager.RevokeBySubjectAsync(subject, cancellationToken);
        await tokenManager.RevokeBySubjectAsync(subject, cancellationToken);
    }
```

> Las firmas son las de OpenIddict 7.7.1:
> `ValueTask<long> IOpenIddictAuthorizationManager.RevokeBySubjectAsync(string subject, CancellationToken cancellationToken)` y
> `ValueTask<long> IOpenIddictTokenManager.RevokeBySubjectAsync(string subject, CancellationToken cancellationToken)`.
> Devuelven cuántas filas se marcaron como revocadas; acá no interesa el número.

- [x] **Paso 6: el doble de pruebas**

En `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`, al final de la clase:

```csharp
    public Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken)
    {
        var index = _users.FindIndex(user => user.Id == userId);
        _users[index] = _users[index] with { IsActive = isActive };

        return Task.CompletedTask;
    }

    public Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        RevokedUsers.Add(userId);

        return Task.CompletedTask;
    }
```

y, junto a las otras colecciones públicas:

```csharp
    /// <summary>A quiénes se les cortó el acceso ya emitido.</summary>
    public List<Guid> RevokedUsers { get; } = [];
```

- [x] **Paso 7: el caso de uso**

`src/ArquitecturaBase.Application/Features/Users/SetUserActive/SetUserActiveCommand.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.SetUserActive;

/// <summary>Activa o desactiva una cuenta. Desactivar corta también el acceso ya emitido.</summary>
public sealed record SetUserActiveCommand(Guid UserId, bool IsActive) : ICommand;
```

`src/ArquitecturaBase.Application/Features/Users/SetUserActive/SetUserActiveCommandHandler.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Features.Users.SetUserActive;

internal sealed class SetUserActiveCommandHandler(IIdentityService identityService, UserGuards guards)
    : ICommandHandler<SetUserActiveCommand>
{
    public async Task<Result> Handle(SetUserActiveCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await identityService.FindByIdAsync(command.UserId, cancellationToken) is null)
        {
            return UserErrors.NotFound;
        }

        if (!command.IsActive)
        {
            // Las reglas de la sección 8 del spec, que escribió la Tarea 5: nunca la propia cuenta ni el último
            // administrador activo. Activar no saca acceso a nadie, así que no pasa por acá.
            var allowed = await guards.EnsureCanBeRemovedAsync(command.UserId, cancellationToken);

            if (allowed.IsFailure)
            {
                return allowed.Error;
            }
        }

        await identityService.SetActiveAsync(command.UserId, command.IsActive, cancellationToken);

        // Marcar la fila no impide nada: el access token vale 15 minutos y la cookie, 30 días (sección 7 del spec).
        if (!command.IsActive)
        {
            await identityService.RevokeSessionsAsync(command.UserId, cancellationToken);
        }

        return Result.Success();
    }
}
```

No lleva validador: el comando no tiene ningún campo que validar más allá del id, que enlaza la ruta.

- [x] **Paso 8: las dos rutas nuevas**

En `src/ArquitecturaBase.Api/Endpoints/Users/UsersEndpoints.cs` se agrega el `using ArquitecturaBase.Application.Features.Users.SetUserActive;` y, al final del método `MapEndpoint`:

```csharp
        group.MapPost("/{id:guid}/activate", async (
                Guid id,
                ICommandHandler<SetUserActiveCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new SetUserActiveCommand(id, IsActive: true), cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Users.Manage);

        group.MapPost("/{id:guid}/deactivate", async (
                Guid id,
                ICommandHandler<SetUserActiveCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new SetUserActiveCommand(id, IsActive: false), cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Users.Manage);
```

- [x] **Paso 9: correr el test y verlo pasar**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.DeactivateUserEndpointTests"
```

Tienen que pasar los 6 tests (`Passed! - Failed: 0, Passed: 6`).

- [x] **Paso 10: comprobar que el ingreso de siempre sigue andando**

El cambio del `ValidationInterval` toca la cookie de todos los ingresos, así que se corren también los flujos existentes:

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.ConnectFlowTests"
```

Tiene que quedar en verde, sin ningún test nuevo en rojo.

- [x] **Paso 11: build y suite completa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

`dotnet build` sin advertencias y `dotnet test` en verde.

- [x] **Paso 12: commit**

```bash
git add src/ArquitecturaBase.Application/Features/Users/SetUserActive src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs src/ArquitecturaBase.Infrastructure/Identity/IdentityRegistration.cs src/ArquitecturaBase.Api/Endpoints/Users/UsersEndpoints.cs tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/DeactivateUserEndpointTests.cs
git commit -m "feat: desactivar una cuenta corta el acceso en el momento" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 10: Eliminar usuarios

Repo: **backend**.

Borrado lógico: la fila queda, oculta por el filtro global que dejó la Tarea 6, para que el historial de ingresos siga existiendo. Como desactivar, tiene que cortar el acceso ya emitido, y **en ese orden**: primero se revocan las sesiones y después se borra, porque después del borrado el usuario ya no se encuentra por las consultas normales.

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Features/Users/DeleteUser/DeleteUserCommand.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Users/DeleteUser/DeleteUserCommandHandler.cs`
- Modificar: `src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs`
- Modificar: `src/ArquitecturaBase.Api/Endpoints/Users/UsersEndpoints.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Users/DeleteUserEndpointTests.cs`

- [x] **Paso 1: el test, antes que el endpoint**

`tests/ArquitecturaBase.Api.IntegrationTests/Users/DeleteUserEndpointTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

[Collection(ApiTestGroup.Name)]
public sealed class DeleteUserEndpointTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deleting_hides_the_user_but_keeps_the_row()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var email = TestEmails.Unique("borrar");
        var userId = await CreateAsync(client, tokens.AccessToken, email);

        using var response = await client.SendWithTokenAsync(HttpMethod.Delete, $"/api/users/{userId}", tokens.AccessToken);
        using var detail = await client.GetWithTokenAsync($"/api/users/{userId}", tokens.AccessToken);
        using var list = await client.GetWithTokenAsync($"/api/users?search={email}", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Equal(0, (await list.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
        Assert.True(await factory.ExecuteDbContextAsync(db => db.Users
            .IgnoreQueryFilters()
            .Where(user => user.Id == userId)
            .Select(user => user.IsDeleted)
            .SingleAsync(Ct)));
    }

    [Fact]
    public async Task Deleting_cuts_off_the_session_that_was_already_open()
    {
        using var victim = factory.CreateClient();
        var email = TestEmails.Unique("borrado");
        var tokens = await victim.LoginAsync(factory, email);
        var userId = await IdOfAsync(email);

        using var before = await victim.GetWithTokenAsync("/test/protected", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, before.StatusCode);

        using var admin = factory.CreateClient();
        var adminTokens = await admin.LoginAsync(factory, ApiFactory.AdminEmail);
        using var delete = await admin.SendWithTokenAsync(HttpMethod.Delete, $"/api/users/{userId}", adminTokens.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        using var after = await victim.GetWithTokenAsync("/test/protected", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task Nobody_deletes_their_own_account()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        using var me = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var myId = (await me.ReadJsonAsync()).GetProperty("id").GetGuid();

        using var response = await client.SendWithTokenAsync(HttpMethod.Delete, $"/api/users/{myId}", tokens.AccessToken);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.CannotModifySelfCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_id_that_does_not_exist_is_not_found()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Delete, $"/api/users/{Guid.CreateVersion7()}", tokens.AccessToken);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(UserErrors.NotFoundCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Deleting_requires_the_users_manage_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("sinborrado"));

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Delete, $"/api/users/{Guid.CreateVersion7()}", tokens.AccessToken, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("No tenés permiso para realizar esta acción.", problem.GetProperty("detail").GetString());
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string accessToken, string email)
    {
        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", accessToken, new { email, roles = new[] { SystemRoles.User } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonSerializer.Deserialize<Guid>((await response.ReadJsonAsync()).GetRawText());
    }

    private Task<Guid> IdOfAsync(string email) =>
        factory.ExecuteDbContextAsync(db => db.Users.Where(user => user.Email == email).Select(user => user.Id).SingleAsync(Ct));
}
```

- [x] **Paso 2: correr el test y ver que falla por la razón correcta**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.DeleteUserEndpointTests"
```

No hay `DELETE /api/users/{id}`; sí hay `GET` y `PUT` en esa ruta, así que la respuesta es 405. Tiene que fallar con:

```
Assert.Equal() Failure: Values differ
Expected: NoContent
Actual:   MethodNotAllowed
```

- [x] **Paso 3: el método nuevo de IIdentityService**

En `src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs`, después de `RevokeSessionsAsync`:

```csharp
    /// <summary>Borrado lógico: la fila queda y el filtro global la esconde, así el historial sigue existiendo.</summary>
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken);
```

- [x] **Paso 4: implementarlo en IdentityService**

En `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs`, después de `RevokeSessionsAsync`:

```csharp
    // userManager.DeleteAsync marca la entidad como borrada y SoftDeleteInterceptor la convierte en una modificación.
    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
        (await userManager.DeleteAsync(await RequireUserAsync(userId, cancellationToken))).EnsureSucceeded("delete the user");
```

- [x] **Paso 5: el doble de pruebas**

En `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`, al final de la clase:

```csharp
    public Task DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = _users.Single(user => user.Id == userId);
        _users.Remove(user);
        _roles.Remove(userId);
        DeletedUsers.Add(user);

        // DeletedEmails lo lee IsDeletedEmailAsync (Tarea 6): los dos tienen que decir lo mismo.
        DeletedEmails.Add(user.Email);

        return Task.CompletedTask;
    }
```

- [x] **Paso 6: el caso de uso**

`src/ArquitecturaBase.Application/Features/Users/DeleteUser/DeleteUserCommand.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.DeleteUser;

/// <summary>Borrado lógico de una cuenta. Como desactivar, corta el acceso ya emitido.</summary>
public sealed record DeleteUserCommand(Guid UserId) : ICommand;
```

`src/ArquitecturaBase.Application/Features/Users/DeleteUser/DeleteUserCommandHandler.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Features.Users.DeleteUser;

internal sealed class DeleteUserCommandHandler(IIdentityService identityService, UserGuards guards)
    : ICommandHandler<DeleteUserCommand>
{
    public async Task<Result> Handle(DeleteUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await identityService.FindByIdAsync(command.UserId, cancellationToken) is null)
        {
            return UserErrors.NotFound;
        }

        // Las mismas reglas que desactivar (Tarea 5): nunca la propia cuenta ni el último administrador activo.
        var allowed = await guards.EnsureCanBeRemovedAsync(command.UserId, cancellationToken);

        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        // Primero revocar y después borrar: una vez borrado, el filtro global lo esconde y ya no se lo encuentra.
        await identityService.RevokeSessionsAsync(command.UserId, cancellationToken);
        await identityService.DeleteAsync(command.UserId, cancellationToken);

        return Result.Success();
    }
}
```

- [x] **Paso 7: la ruta nueva**

En `src/ArquitecturaBase.Api/Endpoints/Users/UsersEndpoints.cs` se agrega el `using ArquitecturaBase.Application.Features.Users.DeleteUser;` y, al final del método `MapEndpoint`:

```csharp
        group.MapDelete("/{id:guid}", async (
                Guid id,
                ICommandHandler<DeleteUserCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new DeleteUserCommand(id), cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Users.Manage);
```

- [x] **Paso 8: correr el test y verlo pasar**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.DeleteUserEndpointTests"
```

Tienen que pasar los 5 tests (`Passed! - Failed: 0, Passed: 5`).

- [x] **Paso 9: comprobar que restaurar sigue andando**

Ahora que existe el borrado por endpoint, se vuelve a correr el alta, que es la que restaura:

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.CreateUserEndpointTests"
```

En verde.

- [x] **Paso 10: build y suite completa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

`dotnet build` sin advertencias y `dotnet test` en verde.

- [x] **Paso 11: commit**

```bash
git add src/ArquitecturaBase.Application/Features/Users/DeleteUser src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs src/ArquitecturaBase.Api/Endpoints/Users/UsersEndpoints.cs tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/DeleteUserEndpointTests.cs
git commit -m "feat: eliminar usuarios con borrado logico" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarea 11: Roles: listado y catálogo de permisos

Repo: **backend**.

Lo que hace falta para dibujar la pantalla de roles: el listado con cuántos usuarios tiene cada uno y qué permisos le dieron, y el catálogo de permisos agrupado por el prefijo del código (`users`, `roles`, `settings`) con los nombres traducidos. Además, `ApplicationRole` pasa a ser `IAuditable`: quién creó o cambió un rol es tan importante como quién cambió quién puede entrar.

> Esta tarea da por hecho que la Tarea 4 ya sumó `settings.manage` a `Permissions.All`. Si todavía no está, se hace primero la Tarea 4.

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Resources/Permissions.resx`
- Crear: `src/ArquitecturaBase.Application/Resources/Permissions.en.resx`
- Crear: `src/ArquitecturaBase.Application/Resources/PermissionTexts.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Roles/GetRoles/RoleListItem.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Roles/GetRoles/GetRolesQuery.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Roles/GetRoles/GetRolesQueryHandler.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Roles/GetPermissions/PermissionGroup.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Roles/GetPermissions/GetPermissionsQuery.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Roles/GetPermissions/GetPermissionsQueryHandler.cs`
- Crear: `src/ArquitecturaBase.Api/Endpoints/Roles/RolesEndpoints.cs`
- Crear: `src/ArquitecturaBase.Infrastructure/Persistence/Migrations/<timestamp>_RoleAuditing.cs` (la genera `dotnet ef`)
- Modificar: `src/ArquitecturaBase.Infrastructure/Identity/ApplicationRole.cs`
- Modificar: `src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Resources/ResourceParityTests.cs`
- Test: `tests/ArquitecturaBase.Application.UnitTests/Resources/PermissionTextsTests.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Roles/RolesEndpointsTests.cs`

- [x] **Paso 1: el test de integración, antes que los endpoints**

`tests/ArquitecturaBase.Api.IntegrationTests/Roles/RolesEndpointsTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authorization;

namespace ArquitecturaBase.Api.IntegrationTests.Roles;

[Collection(ApiTestGroup.Name)]
public sealed class RolesEndpointsTests(ApiFactory factory)
{
    [Fact]
    public async Task The_list_shows_the_system_roles_with_their_permissions_and_user_count()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync("/api/roles", tokens.AccessToken);
        var roles = (await response.ReadJsonAsync()).EnumerateArray().ToArray();
        var admin = roles.Single(role => role.GetProperty("name").GetString() == SystemRoles.Admin);
        var user = roles.Single(role => role.GetProperty("name").GetString() == SystemRoles.User);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(admin.GetProperty("isSystemRole").GetBoolean());
        Assert.True(user.GetProperty("isSystemRole").GetBoolean());
        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), Strings(admin, "permissions"));
        Assert.Empty(Strings(user, "permissions"));
        Assert.True(admin.GetProperty("userCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task The_permission_catalog_is_grouped_by_area_and_translated()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync("/api/permissions", tokens.AccessToken, language: "es");
        var groups = (await response.ReadJsonAsync()).EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["users", "roles", "settings"], groups.Select(group => group.GetProperty("area").GetString()));
        Assert.Equal(["Usuarios", "Roles", "Configuración"], groups.Select(group => group.GetProperty("name").GetString()));

        var users = groups[0].GetProperty("permissions").EnumerateArray().ToArray();
        Assert.Equal(
            [Permissions.Users.Read, Permissions.Users.Manage],
            users.Select(permission => permission.GetProperty("code").GetString()));
        Assert.Equal(["Ver usuarios", "Administrar usuarios"], users.Select(permission => permission.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task The_permission_catalog_is_also_in_english()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync("/api/permissions", tokens.AccessToken, language: "en");
        var groups = (await response.ReadJsonAsync()).EnumerateArray().ToArray();

        Assert.Equal(["Users", "Roles", "Settings"], groups.Select(group => group.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task The_role_list_requires_the_roles_read_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("sinroles"));

        using var response = await client.GetWithTokenAsync("/api/roles", tokens.AccessToken, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("No tenés permiso para realizar esta acción.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task The_permission_catalog_requires_the_roles_read_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("sincatalogo"));

        using var response = await client.GetWithTokenAsync("/api/permissions", tokens.AccessToken, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("No tenés permiso para realizar esta acción.", problem.GetProperty("detail").GetString());
    }

    private static string[] Strings(JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray().Select(item => item.GetString()!).ToArray();
}
```

- [x] **Paso 2: correr el test y ver que falla por la razón correcta**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Roles.RolesEndpointsTests"
```

Ni `/api/roles` ni `/api/permissions` existen. Tiene que fallar con:

```
Assert.Equal() Failure: Values differ
Expected: OK
Actual:   NotFound
```

- [x] **Paso 3: los roles quedan auditados**

`src/ArquitecturaBase.Infrastructure/Identity/ApplicationRole.cs` queda así:

```csharp
using ArquitecturaBase.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// Rol de Identity. Sus permisos son role claims de tipo "permission" (sección 5.6). Es auditable: quién cambió
/// los permisos de un rol importa tanto como quién cambió la configuración.
/// </summary>
public sealed class ApplicationRole : IdentityRole<Guid>, IAuditable
{
    public const int DescriptionMaxLength = 256;

    public ApplicationRole()
    {
        Id = Guid.CreateVersion7();
    }

    public ApplicationRole(string name)
        : this()
    {
        Name = name;
    }

    public string? Description { get; set; }

    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? ModifiedAtUtc { get; private set; }

    public Guid? ModifiedBy { get; private set; }
}
```

- [x] **Paso 4: la migración**

```bash
dotnet ef migrations add RoleAuditing --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api --output-dir Persistence/Migrations -- --environment Development --ConnectionStrings:appdb "Host=localhost;Port=5433;Database=appdb;Username=postgres;Password=postgres"
```

No hace falta que Postgres esté levantado: la cadena de conexión solo existe para que `AddInfrastructure` no falle al armar el host. Tiene que terminar con:

```
Build succeeded.
Done. To undo this action, use 'ef migrations remove'
```

y dejar `Persistence/Migrations/<timestamp>_RoleAuditing.cs`, su `.Designer.cs` y el snapshot actualizado.

**Un arreglo obligatorio en el archivo generado.** `CreatedAtUtc` es obligatoria y la tabla ya tiene filas, así que EF le pone un valor por defecto:

```csharp
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
```

Npgsql no puede escribir un literal `timestamp with time zone` con `Kind = Unspecified`. Se cambia por:

```csharp
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));
```

El snapshot no lleva ese valor (es solo para rellenar las filas que ya estaban), así que `MigrationsTests.Model_has_no_pending_changes` sigue conforme.

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Persistence.MigrationsTests"
```

Los dos tests en verde: `Model_has_no_pending_changes` y `Migrations_create_the_schema_on_an_empty_database`, que aplica todas las migraciones sobre una base vacía y es el que rompe si el literal quedó mal.

- [x] **Paso 5: los nombres traducidos de las áreas y los permisos**

`src/ArquitecturaBase.Application/Resources/Permissions.resx`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <data name="Area.users" xml:space="preserve"><value>Usuarios</value></data>
  <data name="Area.roles" xml:space="preserve"><value>Roles</value></data>
  <data name="Area.settings" xml:space="preserve"><value>Configuración</value></data>
  <data name="Permission.users.read" xml:space="preserve"><value>Ver usuarios</value></data>
  <data name="Permission.users.manage" xml:space="preserve"><value>Administrar usuarios</value></data>
  <data name="Permission.roles.read" xml:space="preserve"><value>Ver roles</value></data>
  <data name="Permission.roles.manage" xml:space="preserve"><value>Administrar roles</value></data>
  <data name="Permission.settings.manage" xml:space="preserve"><value>Administrar la configuración</value></data>
</root>
```

`src/ArquitecturaBase.Application/Resources/Permissions.en.resx`: el mismo archivo con los mismos `resheader` y estos valores:

```xml
  <data name="Area.users" xml:space="preserve"><value>Users</value></data>
  <data name="Area.roles" xml:space="preserve"><value>Roles</value></data>
  <data name="Area.settings" xml:space="preserve"><value>Settings</value></data>
  <data name="Permission.users.read" xml:space="preserve"><value>View users</value></data>
  <data name="Permission.users.manage" xml:space="preserve"><value>Manage users</value></data>
  <data name="Permission.roles.read" xml:space="preserve"><value>View roles</value></data>
  <data name="Permission.roles.manage" xml:space="preserve"><value>Manage roles</value></data>
  <data name="Permission.settings.manage" xml:space="preserve"><value>Manage settings</value></data>
```

`src/ArquitecturaBase.Application/Resources/PermissionTexts.cs`:

```csharp
using System.Globalization;
using System.Resources;

namespace ArquitecturaBase.Application.Resources;

/// <summary>
/// Nombres del catálogo de permisos en el idioma de la petición. La clave del área es el prefijo del código
/// (<c>Area.users</c>) y la del permiso, el código entero (<c>Permission.users.read</c>).
/// </summary>
public static class PermissionTexts
{
    internal static ResourceManager ResourceManager { get; } =
        new("ArquitecturaBase.Application.Resources.Permissions", typeof(PermissionTexts).Assembly);

    public static string Area(string area) => Get("Area." + area);

    public static string Permission(string permission) => Get("Permission." + permission);

    private static string Get(string key) => ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
```

- [x] **Paso 6: que nadie se olvide de traducir un permiso nuevo**

`tests/ArquitecturaBase.Application.UnitTests/Resources/PermissionTextsTests.cs`:

```csharp
using System.Globalization;
using System.Resources;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authorization;

namespace ArquitecturaBase.Application.UnitTests.Resources;

/// <summary>Cada permiso del catálogo y cada área tienen nombre en español y en inglés.</summary>
public sealed class PermissionTextsTests
{
    public static TheoryData<string> Keys()
    {
        var data = new TheoryData<string>();

        foreach (var key in Permissions.All
            .Select(permission => "Area." + permission[..permission.IndexOf('.', StringComparison.Ordinal)])
            .Concat(Permissions.All.Select(permission => "Permission." + permission))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal))
        {
            data.Add(key);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void Permission_key_has_spanish_and_english_texts(string key)
    {
        Assert.False(string.IsNullOrWhiteSpace(Text(CultureInfo.InvariantCulture, key)), $"Missing Spanish text for {key}");
        Assert.False(string.IsNullOrWhiteSpace(Text(CultureInfo.GetCultureInfo("en"), key)), $"Missing English text for {key}");
    }

    private static string? Text(CultureInfo culture, string key) =>
        PermissionTexts.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!.GetString(key);
}
```

y en `tests/ArquitecturaBase.Application.UnitTests/Resources/ResourceParityTests.cs`, después de `Validation_messages_have_the_same_keys_in_spanish_and_english`:

```csharp
    [Fact]
    public void Permission_texts_have_the_same_keys_in_spanish_and_english()
    {
        AssertSameKeys(PermissionTexts.ResourceManager);
    }
```

```bash
dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Resources.PermissionTextsTests"
```

En verde: los 8 casos (5 permisos + 3 áreas).

- [x] **Paso 7: los dos casos de uso**

`src/ArquitecturaBase.Application/Features/Roles/GetRoles/RoleListItem.cs`:

```csharp
namespace ArquitecturaBase.Application.Features.Roles.GetRoles;

/// <summary>Un rol con sus permisos y cuántos usuarios activos lo tienen (sección 6 del spec de la Fase 4).</summary>
public sealed record RoleListItem(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystemRole,
    int UserCount,
    IReadOnlyCollection<string> Permissions);
```

`src/ArquitecturaBase.Application/Features/Roles/GetRoles/GetRolesQuery.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Roles.GetRoles;

public sealed record GetRolesQuery : IQuery<IReadOnlyCollection<RoleListItem>>;
```

`src/ArquitecturaBase.Application/Features/Roles/GetRoles/GetRolesQueryHandler.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Features.Roles.GetRoles;

internal sealed class GetRolesQueryHandler(IIdentityService identityService)
    : IQueryHandler<GetRolesQuery, IReadOnlyCollection<RoleListItem>>
{
    public async Task<Result<IReadOnlyCollection<RoleListItem>>> Handle(GetRolesQuery query, CancellationToken cancellationToken) =>
        Result.Success(await identityService.ListRolesAsync(cancellationToken));
}
```

`src/ArquitecturaBase.Application/Features/Roles/GetPermissions/PermissionGroup.cs`:

```csharp
namespace ArquitecturaBase.Application.Features.Roles.GetPermissions;

/// <summary>Un permiso del catálogo: el código estable y su nombre traducido.</summary>
public sealed record PermissionItem(string Code, string Name);

/// <summary>Los permisos de un área, que es el prefijo del código (<c>users</c>, <c>roles</c>, <c>settings</c>).</summary>
public sealed record PermissionGroup(string Area, string Name, IReadOnlyCollection<PermissionItem> Permissions);
```

`src/ArquitecturaBase.Application/Features/Roles/GetPermissions/GetPermissionsQuery.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Roles.GetPermissions;

public sealed record GetPermissionsQuery : IQuery<IReadOnlyCollection<PermissionGroup>>;
```

`src/ArquitecturaBase.Application/Features/Roles/GetPermissions/GetPermissionsQueryHandler.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Features.Roles.GetPermissions;

/// <summary>
/// El catálogo sale del propio <see cref="Permissions.All"/>: no hay tabla de permisos. El área es el prefijo del
/// código y los nombres salen de Permissions.resx, en el idioma de la petición.
/// </summary>
internal sealed class GetPermissionsQueryHandler : IQueryHandler<GetPermissionsQuery, IReadOnlyCollection<PermissionGroup>>
{
    public Task<Result<IReadOnlyCollection<PermissionGroup>>> Handle(GetPermissionsQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<PermissionGroup> groups =
        [
            .. Permissions.All
                .GroupBy(AreaOf, StringComparer.Ordinal)
                .Select(group => new PermissionGroup(
                    group.Key,
                    PermissionTexts.Area(group.Key),
                    [.. group.Select(permission => new PermissionItem(permission, PermissionTexts.Permission(permission)))])),
        ];

        return Task.FromResult(Result.Success(groups));
    }

    private static string AreaOf(string permission) => permission[..permission.IndexOf('.', StringComparison.Ordinal)];
}
```

- [x] **Paso 8: el listado en IIdentityService**

En `src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs` se agrega el `using ArquitecturaBase.Application.Features.Roles.GetRoles;` y, al final de la interfaz:

```csharp
    /// <summary>Todos los roles, ordenados por nombre, con sus permisos y sus usuarios.</summary>
    Task<IReadOnlyCollection<RoleListItem>> ListRolesAsync(CancellationToken cancellationToken);
```

En `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs` se agrega el mismo `using` y, al final de la clase (antes de los helpers privados estáticos):

```csharp
    public async Task<IReadOnlyCollection<RoleListItem>> ListRolesAsync(CancellationToken cancellationToken) =>
        await LoadRolesAsync(roleId: null, cancellationToken);

    /// <summary>
    /// La misma forma para el listado y para el detalle. UserCount cuenta sobre dbContext.Users, que arrastra el
    /// filtro global: un usuario borrado no mantiene vivo a un rol.
    /// </summary>
    private async Task<List<RoleListItem>> LoadRolesAsync(Guid? roleId, CancellationToken cancellationToken)
    {
        var query = dbContext.Roles.AsNoTracking();

        if (roleId is { } id)
        {
            query = query.Where(role => role.Id == id);
        }

        var rows = await query
            .OrderBy(role => role.Name)
            .Select(role => new
            {
                role.Id,
                Name = role.Name!,
                role.Description,
                UserCount = dbContext.Users.Count(user =>
                    dbContext.UserRoles.Any(userRole => userRole.RoleId == role.Id && userRole.UserId == user.Id)),
                Permissions = dbContext.RoleClaims
                    .Where(claim => claim.RoleId == role.Id && claim.ClaimType == Domain.Authorization.Permissions.ClaimType)
                    .Select(claim => claim.ClaimValue!)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new RoleListItem(
                row.Id,
                row.Name,
                row.Description,
                SystemRoles.All.Contains(row.Name, StringComparer.Ordinal),
                row.UserCount,
                [.. row.Permissions.Order(StringComparer.Ordinal)])),
        ];
    }
```

> `Domain.Authorization.Permissions.ClaimType` va con el nombre largo para que no se confunda con la propiedad `Permissions` del tipo anónimo de la misma consulta.

Y en `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs` (con `using ArquitecturaBase.Application.Features.Roles.GetRoles;`), al final de la clase:

```csharp
    public Task<IReadOnlyCollection<RoleListItem>> ListRolesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<RoleListItem>>(
        [
            .. RoleNames.Order(StringComparer.Ordinal).Select(name => new RoleListItem(
                Guid.CreateVersion7(),
                name,
                Description: null,
                SystemRoles.All.Contains(name, StringComparer.Ordinal),
                _users.Count(user => (_roles.GetValueOrDefault(user.Id) ?? []).Contains(name, StringComparer.Ordinal)),
                [])),
        ]);
```

(hace falta también `using ArquitecturaBase.Domain.Authorization;`).

- [x] **Paso 9: los dos endpoints**

`src/ArquitecturaBase.Api/Endpoints/Roles/RolesEndpoints.cs`:

```csharp
using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Roles.GetPermissions;
using ArquitecturaBase.Application.Features.Roles.GetRoles;
using ArquitecturaBase.Domain.Authorization;

namespace ArquitecturaBase.Api.Endpoints.Roles;

/// <summary>Roles y catálogo de permisos (sección 10 del spec de la Fase 4).</summary>
internal sealed class RolesEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/roles").WithTags("Roles");

        group.MapGet("", async (
                IQueryHandler<GetRolesQuery, IReadOnlyCollection<RoleListItem>> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new GetRolesQuery(), cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Roles.Read);

        // El catálogo es lo que la pantalla de roles necesita para dibujar las casillas, así que pide el mismo permiso.
        app.MapGet("/api/permissions", async (
                IQueryHandler<GetPermissionsQuery, IReadOnlyCollection<PermissionGroup>> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new GetPermissionsQuery(), cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Roles.Read)
            .WithTags("Roles");
    }
}
```

- [x] **Paso 10: correr el test y verlo pasar**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Roles.RolesEndpointsTests"
```

Tienen que pasar los 5 tests (`Passed! - Failed: 0, Passed: 5`).

- [x] **Paso 11: build y suite completa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

`dotnet build` sin advertencias y `dotnet test` en verde.

- [x] **Paso 12: commit**

```bash
git add src/ArquitecturaBase.Application/Resources/Permissions.resx src/ArquitecturaBase.Application/Resources/Permissions.en.resx src/ArquitecturaBase.Application/Resources/PermissionTexts.cs src/ArquitecturaBase.Application/Features/Roles src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs src/ArquitecturaBase.Infrastructure/Identity/ApplicationRole.cs src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs src/ArquitecturaBase.Infrastructure/Persistence/Migrations src/ArquitecturaBase.Api/Endpoints/Roles/RolesEndpoints.cs tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs tests/ArquitecturaBase.Application.UnitTests/Resources/ResourceParityTests.cs tests/ArquitecturaBase.Application.UnitTests/Resources/PermissionTextsTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Roles/RolesEndpointsTests.cs
git commit -m "feat: listado de roles y catalogo de permisos" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 12: Roles: crear, editar y borrar

Repo: **backend**.

Un administrador arma sus propios roles con los permisos que quiera. `Admin` y `User` quedan protegidos: no se renombran ni se borran, y a `Admin` no se le editan los permisos, que es la garantía de que siempre hay alguien que puede arreglar cualquier cosa. Un rol con usuarios no se borra. Y todo cambio de permisos invalida el caché de `PermissionService`, o los permisos viejos seguirían valiendo hasta una hora.

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Features/Roles/CreateRole/CreateRoleCommand.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Roles/CreateRole/CreateRoleCommandValidator.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Roles/CreateRole/CreateRoleCommandHandler.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Roles/UpdateRole/UpdateRoleCommand.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Roles/UpdateRole/UpdateRoleCommandValidator.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Roles/UpdateRole/UpdateRoleCommandHandler.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Roles/DeleteRole/DeleteRoleCommand.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Roles/DeleteRole/DeleteRoleCommandHandler.cs`
- Modificar: `src/ArquitecturaBase.Domain/Authorization/RoleErrors.cs`
- Modificar: `src/ArquitecturaBase.Application/Resources/Errors.resx`
- Modificar: `src/ArquitecturaBase.Application/Resources/Errors.en.resx`
- Modificar: `src/ArquitecturaBase.Application/Common/Validation/ValidationRules.cs`
- Modificar: `src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs`
- Modificar: `src/ArquitecturaBase.Api/Endpoints/Roles/RolesEndpoints.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Roles/RoleCrudEndpointsTests.cs`

- [ ] **Paso 1: los errores nuevos y sus textos**

`src/ArquitecturaBase.Domain/Authorization/RoleErrors.cs` queda así:

```csharp
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Authorization;

public static class RoleErrors
{
    public const string NotFoundCode = "Roles.Role.NotFound";
    public const string AlreadyExistsCode = "Roles.Role.AlreadyExists";
    public const string SystemRoleCannotChangeCode = "Roles.Role.SystemRoleCannotChange";
    public const string HasUsersCode = "Roles.Role.HasUsers";

    public const string UserCountKey = "userCount";

    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The role was not found.");

    public static readonly Error AlreadyExists = Error.Conflict(AlreadyExistsCode, "A role with that name already exists.");

    /// <summary>Admin y User no se renombran ni se borran, y a Admin no se le editan los permisos.</summary>
    public static readonly Error SystemRoleCannotChange =
        Error.Forbidden(SystemRoleCannotChangeCode, "System roles can't be renamed, deleted or have their permissions changed.");

    /// <summary>Borrar un rol con usuarios. El metadato dice cuántos son, para que se reasignen primero.</summary>
    public static Error HasUsers(int userCount) =>
        Error.Conflict(
            HasUsersCode,
            "The role still has users assigned.",
            new Dictionary<string, object?> { [UserCountKey] = userCount });
}
```

En `src/ArquitecturaBase.Application/Resources/Errors.resx`, antes de `</root>`:

```xml
  <data name="Roles.Role.AlreadyExists" xml:space="preserve"><value>Ya existe un rol con ese nombre.</value></data>
  <data name="Roles.Role.SystemRoleCannotChange" xml:space="preserve"><value>Los roles del sistema no se pueden renombrar, borrar ni cambiarles los permisos.</value></data>
  <data name="Roles.Role.HasUsers" xml:space="preserve"><value>El rol tiene usuarios asignados. Reasignalos antes de borrarlo.</value></data>
```

En `src/ArquitecturaBase.Application/Resources/Errors.en.resx`, antes de `</root>`:

```xml
  <data name="Roles.Role.AlreadyExists" xml:space="preserve"><value>A role with that name already exists.</value></data>
  <data name="Roles.Role.SystemRoleCannotChange" xml:space="preserve"><value>System roles can't be renamed, deleted or have their permissions changed.</value></data>
  <data name="Roles.Role.HasUsers" xml:space="preserve"><value>The role still has users assigned. Reassign them before deleting it.</value></data>
```

Y en `src/ArquitecturaBase.Application/Common/Validation/ValidationRules.cs`, junto a las otras constantes:

```csharp
    /// <summary>La columna de Identity admite 256; 64 alcanza de sobra para un nombre de rol y se lee mejor.</summary>
    public const int RoleNameMaxLength = 64;

    /// <summary>Tiene que coincidir con ApplicationRole.DescriptionMaxLength.</summary>
    public const int RoleDescriptionMaxLength = 256;
```

- [ ] **Paso 2: el test, antes que los endpoints**

`tests/ArquitecturaBase.Api.IntegrationTests/Roles/RoleCrudEndpointsTests.cs`:

```csharp
using System.Globalization;
using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authorization;

namespace ArquitecturaBase.Api.IntegrationTests.Roles;

[Collection(ApiTestGroup.Name)]
public sealed class RoleCrudEndpointsTests(ApiFactory factory)
{
    [Fact]
    public async Task Admin_creates_a_role_with_its_permissions()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var name = UniqueName("lectores");

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post,
            "/api/roles",
            tokens.AccessToken,
            new { name, description = "Solo lectura", permissions = new[] { Permissions.Users.Read } });
        var roleId = JsonSerializer.Deserialize<Guid>((await response.ReadJsonAsync()).GetRawText());
        var role = await FindAsync(client, tokens.AccessToken, roleId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(name, role.GetProperty("name").GetString());
        Assert.Equal("Solo lectura", role.GetProperty("description").GetString());
        Assert.False(role.GetProperty("isSystemRole").GetBoolean());
        Assert.Equal(0, role.GetProperty("userCount").GetInt32());
        Assert.Equal([Permissions.Users.Read], Strings(role, "permissions"));
    }

    [Fact]
    public async Task A_repeated_name_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var name = UniqueName("repetido");
        await CreateAsync(client, tokens.AccessToken, name, []);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/roles", tokens.AccessToken, new { name, permissions = Array.Empty<string>() }, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(RoleErrors.AlreadyExistsCode, problem.GetProperty("code").GetString());
        Assert.Equal("Ya existe un rol con ese nombre.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_permission_outside_the_catalog_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post,
            "/api/roles",
            tokens.AccessToken,
            new { name = UniqueName("raro"), permissions = new[] { "inventado.total" } },
            language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Ese permiso no existe.", problem.GetProperty("errors").GetProperty("permissions")[0].GetString());
    }

    [Fact]
    public async Task Editing_changes_the_name_the_description_and_the_permissions()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var roleId = await CreateAsync(client, tokens.AccessToken, UniqueName("editar"), [Permissions.Users.Read]);
        var newName = UniqueName("editado");

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Put,
            $"/api/roles/{roleId}",
            tokens.AccessToken,
            new { name = newName, description = "Cambiada", permissions = new[] { Permissions.Roles.Read } });
        var role = await FindAsync(client, tokens.AccessToken, roleId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(newName, role.GetProperty("name").GetString());
        Assert.Equal("Cambiada", role.GetProperty("description").GetString());
        Assert.Equal([Permissions.Roles.Read], Strings(role, "permissions"));
    }

    [Fact]
    public async Task Changing_the_permissions_of_a_role_takes_effect_for_its_users_right_away()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var roleId = await CreateAsync(client, tokens.AccessToken, UniqueName("cache"), [Permissions.Users.Read]);

        // Una persona con ese rol entra y usa el permiso: acá se llena el caché de PermissionService.
        var email = TestEmails.Unique("cache");
        using var create = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", tokens.AccessToken, new { email, roles = new[] { RoleNameOf(await FindAsync(client, tokens.AccessToken, roleId)) } });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var member = factory.CreateClient();
        var memberTokens = await member.LoginAsync(factory, email);
        using var before = await member.GetWithTokenAsync("/api/me", memberTokens.AccessToken);
        Assert.Equal([Permissions.Users.Read], Strings(await before.ReadJsonAsync(), "permissions"));

        using var update = await client.SendWithTokenAsync(
            HttpMethod.Put,
            $"/api/roles/{roleId}",
            tokens.AccessToken,
            new { name = RoleNameOf(await FindAsync(client, tokens.AccessToken, roleId)), permissions = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        using var after = await member.GetWithTokenAsync("/api/me", memberTokens.AccessToken);

        Assert.Empty(Strings(await after.ReadJsonAsync(), "permissions"));
    }

    [Fact]
    public async Task The_admin_role_cannot_be_renamed()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var admin = await FindByNameAsync(client, tokens.AccessToken, SystemRoles.Admin);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Put,
            $"/api/roles/{admin.GetProperty("id").GetGuid()}",
            tokens.AccessToken,
            new { name = "Superadmin", permissions = Strings(admin, "permissions") });
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(RoleErrors.SystemRoleCannotChangeCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_admin_role_does_not_lose_permissions()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var admin = await FindByNameAsync(client, tokens.AccessToken, SystemRoles.Admin);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Put,
            $"/api/roles/{admin.GetProperty("id").GetGuid()}",
            tokens.AccessToken,
            new { name = SystemRoles.Admin, permissions = new[] { Permissions.Users.Read } });
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(RoleErrors.SystemRoleCannotChangeCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_system_role_cannot_be_deleted()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var user = await FindByNameAsync(client, tokens.AccessToken, SystemRoles.User);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Delete, $"/api/roles/{user.GetProperty("id").GetGuid()}", tokens.AccessToken);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(RoleErrors.SystemRoleCannotChangeCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_role_with_users_cannot_be_deleted_and_the_error_says_how_many()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var name = UniqueName("conusuarios");
        var roleId = await CreateAsync(client, tokens.AccessToken, name, []);
        using var create = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", tokens.AccessToken, new { email = TestEmails.Unique("miembro"), roles = new[] { name } });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var response = await client.SendWithTokenAsync(HttpMethod.Delete, $"/api/roles/{roleId}", tokens.AccessToken);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(RoleErrors.HasUsersCode, problem.GetProperty("code").GetString());
        Assert.Equal(1, problem.GetProperty(RoleErrors.UserCountKey).GetInt32());
    }

    [Fact]
    public async Task A_role_without_users_is_deleted()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var roleId = await CreateAsync(client, tokens.AccessToken, UniqueName("descartable"), []);

        using var response = await client.SendWithTokenAsync(HttpMethod.Delete, $"/api/roles/{roleId}", tokens.AccessToken);
        using var list = await client.GetWithTokenAsync("/api/roles", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(
            (await list.ReadJsonAsync()).EnumerateArray(),
            role => role.GetProperty("id").GetGuid() == roleId);
    }

    [Fact]
    public async Task An_id_that_does_not_exist_is_not_found()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Delete, $"/api/roles/{Guid.CreateVersion7()}", tokens.AccessToken);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(RoleErrors.NotFoundCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Managing_roles_requires_the_roles_manage_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("singestion"));

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post,
            "/api/roles",
            tokens.AccessToken,
            new { name = UniqueName("prohibido"), permissions = Array.Empty<string>() },
            language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("No tenés permiso para realizar esta acción.", problem.GetProperty("detail").GetString());
    }

    private static string UniqueName(string prefix) =>
        prefix + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];

    private static string RoleNameOf(JsonElement role) => role.GetProperty("name").GetString()!;

    private static async Task<Guid> CreateAsync(HttpClient client, string accessToken, string name, string[] permissions)
    {
        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/roles", accessToken, new { name, permissions });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonSerializer.Deserialize<Guid>((await response.ReadJsonAsync()).GetRawText());
    }

    private static async Task<JsonElement> FindAsync(HttpClient client, string accessToken, Guid roleId)
    {
        using var response = await client.GetWithTokenAsync("/api/roles", accessToken);

        return (await response.ReadJsonAsync()).EnumerateArray().Single(role => role.GetProperty("id").GetGuid() == roleId);
    }

    private static async Task<JsonElement> FindByNameAsync(HttpClient client, string accessToken, string name)
    {
        using var response = await client.GetWithTokenAsync("/api/roles", accessToken);

        return (await response.ReadJsonAsync()).EnumerateArray().Single(role => role.GetProperty("name").GetString() == name);
    }

    private static string[] Strings(JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray().Select(item => item.GetString()!).ToArray();
}
```

- [ ] **Paso 3: correr el test y ver que falla por la razón correcta**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Roles.RoleCrudEndpointsTests"
```

`/api/roles` solo acepta GET. Tiene que fallar con:

```
Assert.Equal() Failure: Values differ
Expected: OK
Actual:   MethodNotAllowed
```

- [ ] **Paso 4: el mensaje de validación del permiso inexistente**

En `src/ArquitecturaBase.Application/Resources/Validation.resx`, antes de `</root>`:

```xml
  <data name="PermissionUnknown" xml:space="preserve"><value>Ese permiso no existe.</value></data>
```

En `src/ArquitecturaBase.Application/Resources/Validation.en.resx`, antes de `</root>`:

```xml
  <data name="PermissionUnknown" xml:space="preserve"><value>That permission does not exist.</value></data>
```

Y en `src/ArquitecturaBase.Application/Resources/ValidationMessages.cs`, junto a las otras propiedades:

```csharp
    public static string PermissionUnknown => Get(nameof(PermissionUnknown));
```

- [ ] **Paso 5: los métodos nuevos de IIdentityService**

En `src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs`, después de `ListRolesAsync`:

```csharp
    Task<RoleListItem?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken);

    /// <summary>Si ya hay un rol con ese nombre, sin contar a <paramref name="excludedRoleId"/>.</summary>
    Task<bool> RoleNameExistsAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken);

    Task<Guid> CreateRoleAsync(
        string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken);

    Task UpdateRoleAsync(
        Guid roleId, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken);

    Task DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken);
```

- [ ] **Paso 6: implementarlos en IdentityService**

En `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs`:

1. Se agrega `using System.Security.Claims;` si todavía no está (ya está: lo usa `GetExternalLoginAsync`).
2. El constructor primario suma el `RoleManager`:

```csharp
internal sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    RoleManager<ApplicationRole> roleManager,
    ApplicationDbContext dbContext,
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictTokenManager tokenManager,
    IOptions<SeedOptions> seedOptions)
    : IIdentityService
```

3. Después de `ListRolesAsync`:

```csharp
    public async Task<RoleListItem?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        (await LoadRolesAsync(roleId, cancellationToken)).FirstOrDefault();

    public Task<bool> RoleNameExistsAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken)
    {
        var normalized = roleManager.NormalizeKey(name);

        return dbContext.Roles.AnyAsync(
            role => role.NormalizedName == normalized && (excludedRoleId == null || role.Id != excludedRoleId),
            cancellationToken);
    }

    public async Task<Guid> CreateRoleAsync(
        string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        var role = new ApplicationRole(name) { Description = description };

        (await roleManager.CreateAsync(role)).EnsureSucceeded("create the role");
        await SetRolePermissionsAsync(role, permissions);

        return role.Id;
    }

    public async Task UpdateRoleAsync(
        Guid roleId, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        var role = await RequireRoleAsync(roleId, cancellationToken);
        role.Description = description;

        // SetRoleNameAsync escribe el nombre y el normalizado en el store; UpdateAsync es el que guarda.
        (await roleManager.SetRoleNameAsync(role, name)).EnsureSucceeded("rename the role");
        (await roleManager.UpdateAsync(role)).EnsureSucceeded("update the role");

        await SetRolePermissionsAsync(role, permissions);
    }

    public async Task DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        (await roleManager.DeleteAsync(await RequireRoleAsync(roleId, cancellationToken))).EnsureSucceeded("delete the role");

    private async Task SetRolePermissionsAsync(ApplicationRole role, IReadOnlyCollection<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var current = (await roleManager.GetClaimsAsync(role))
            .Where(claim => claim.Type == Domain.Authorization.Permissions.ClaimType)
            .ToList();

        foreach (var claim in current.Where(claim => !permissions.Contains(claim.Value, StringComparer.Ordinal)))
        {
            (await roleManager.RemoveClaimAsync(role, claim)).EnsureSucceeded("remove a permission");
        }

        var kept = current.Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal);

        foreach (var permission in permissions.Where(permission => !kept.Contains(permission)))
        {
            (await roleManager.AddClaimAsync(role, new Claim(Domain.Authorization.Permissions.ClaimType, permission)))
                .EnsureSucceeded("add a permission");
        }
    }

    private async Task<ApplicationRole> RequireRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        await roleManager.Roles.FirstOrDefaultAsync(role => role.Id == roleId, cancellationToken)
            ?? throw new InvalidOperationException("The role does not exist.");
```

- [ ] **Paso 7: el doble de pruebas**

En `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`, al final de la clase:

```csharp
    public Task<RoleListItem?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        Task.FromResult(ListRolesAsync(cancellationToken).Result.SingleOrDefault(role => role.Id == roleId));

    public Task<bool> RoleNameExistsAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken) =>
        Task.FromResult(RoleNames.Contains(name, StringComparer.OrdinalIgnoreCase));

    public Task<Guid> CreateRoleAsync(
        string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        RoleNames.Add(name);

        return Task.FromResult(Guid.CreateVersion7());
    }

    public Task UpdateRoleAsync(
        Guid roleId, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
```

> `ListRolesAsync(...).Result` no bloquea nada: el doble devuelve una tarea ya completada.

- [ ] **Paso 8: crear**

`src/ArquitecturaBase.Application/Features/Roles/CreateRole/CreateRoleCommand.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Roles.CreateRole;

public sealed record CreateRoleCommand(string? Name, string? Description, IReadOnlyCollection<string>? Permissions)
    : ICommand<Guid>;
```

`src/ArquitecturaBase.Application/Features/Roles/CreateRole/CreateRoleCommandValidator.cs`:

```csharp
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authorization;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Roles.CreateRole;

internal sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .Required()
            .MaxLength(ValidationRules.RoleNameMaxLength);

        RuleFor(command => command.Description).MaxLength(ValidationRules.RoleDescriptionMaxLength);

        RuleFor(command => command.Permissions).ValidPermissions();
    }
}
```

Y la regla, en `src/ArquitecturaBase.Application/Common/Validation/ValidationRules.cs`, al final de la clase:

```csharp
    /// <summary>Todos los permisos pedidos tienen que estar en el catálogo de Domain.</summary>
    public static IRuleBuilderOptions<T, IReadOnlyCollection<string>?> ValidPermissions<T>(
        this IRuleBuilder<T, IReadOnlyCollection<string>?> ruleBuilder) =>
        ruleBuilder
            .Must(permissions => permissions is null
                || permissions.All(permission => Permissions.All.Contains(permission, StringComparer.Ordinal)))
            .WithMessage(_ => ValidationMessages.PermissionUnknown);
```

(el archivo suma `using ArquitecturaBase.Domain.Authorization;`).

`src/ArquitecturaBase.Application/Features/Roles/CreateRole/CreateRoleCommandHandler.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Features.Roles.CreateRole;

internal sealed class CreateRoleCommandHandler(IIdentityService identityService)
    : ICommandHandler<CreateRoleCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = command.Name!.Trim();

        if (await identityService.RoleNameExistsAsync(name, excludedRoleId: null, cancellationToken))
        {
            return RoleErrors.AlreadyExists;
        }

        IReadOnlyCollection<string> permissions = [.. (command.Permissions ?? []).Distinct(StringComparer.Ordinal)];

        // Un rol nuevo no lo tiene nadie todavía, así que no hay caché que invalidar.
        return await identityService.CreateRoleAsync(name, command.Description, permissions, cancellationToken);
    }
}
```

- [ ] **Paso 9: editar**

`src/ArquitecturaBase.Application/Features/Roles/UpdateRole/UpdateRoleCommand.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Roles.UpdateRole;

public sealed record UpdateRoleCommand(
    Guid RoleId,
    string? Name,
    string? Description,
    IReadOnlyCollection<string>? Permissions) : ICommand;
```

`src/ArquitecturaBase.Application/Features/Roles/UpdateRole/UpdateRoleCommandValidator.cs`:

```csharp
using ArquitecturaBase.Application.Common.Validation;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Roles.UpdateRole;

internal sealed class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator()
    {
        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .Required()
            .MaxLength(ValidationRules.RoleNameMaxLength);

        RuleFor(command => command.Description).MaxLength(ValidationRules.RoleDescriptionMaxLength);

        RuleFor(command => command.Permissions).ValidPermissions();
    }
}
```

`src/ArquitecturaBase.Application/Features/Roles/UpdateRole/UpdateRoleCommandHandler.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Features.Roles.UpdateRole;

internal sealed class UpdateRoleCommandHandler(IIdentityService identityService, IPermissionService permissionService)
    : ICommandHandler<UpdateRoleCommand>
{
    public async Task<Result> Handle(UpdateRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var role = await identityService.FindRoleAsync(command.RoleId, cancellationToken);

        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        var name = command.Name!.Trim();
        IReadOnlyCollection<string> permissions =
            [.. (command.Permissions ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        // Admin y User no se renombran (sección 6 del spec de la Fase 4).
        if (role.IsSystemRole && !string.Equals(role.Name, name, StringComparison.Ordinal))
        {
            return RoleErrors.SystemRoleCannotChange;
        }

        // Y Admin conserva siempre todos sus permisos: es la garantía de que alguien puede arreglar cualquier cosa.
        // La pantalla manda los que ya tiene, así que reenviarlos iguales no es un cambio.
        if (string.Equals(role.Name, SystemRoles.Admin, StringComparison.Ordinal)
            && !permissions.SequenceEqual(role.Permissions, StringComparer.Ordinal))
        {
            return RoleErrors.SystemRoleCannotChange;
        }

        if (await identityService.RoleNameExistsAsync(name, role.Id, cancellationToken))
        {
            return RoleErrors.AlreadyExists;
        }

        await identityService.UpdateRoleAsync(role.Id, name, command.Description, permissions, cancellationToken);

        // Sin esto, los permisos viejos seguirían valiendo hasta una hora (PermissionService cachea por rol).
        await permissionService.InvalidateRoleAsync(role.Id, cancellationToken);

        return Result.Success();
    }
}
```

- [ ] **Paso 10: borrar**

`src/ArquitecturaBase.Application/Features/Roles/DeleteRole/DeleteRoleCommand.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Roles.DeleteRole;

public sealed record DeleteRoleCommand(Guid RoleId) : ICommand;
```

`src/ArquitecturaBase.Application/Features/Roles/DeleteRole/DeleteRoleCommandHandler.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Features.Roles.DeleteRole;

internal sealed class DeleteRoleCommandHandler(IIdentityService identityService, IPermissionService permissionService)
    : ICommandHandler<DeleteRoleCommand>
{
    public async Task<Result> Handle(DeleteRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var role = await identityService.FindRoleAsync(command.RoleId, cancellationToken);

        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        if (role.IsSystemRole)
        {
            return RoleErrors.SystemRoleCannotChange;
        }

        // El error dice cuántos son, para que se reasignen primero.
        if (role.UserCount > 0)
        {
            return RoleErrors.HasUsers(role.UserCount);
        }

        await identityService.DeleteRoleAsync(role.Id, cancellationToken);
        await permissionService.InvalidateRoleAsync(role.Id, cancellationToken);

        return Result.Success();
    }
}
```

- [ ] **Paso 11: las tres rutas nuevas**

En `src/ArquitecturaBase.Api/Endpoints/Roles/RolesEndpoints.cs` se agregan los `using` de `ArquitecturaBase.Application.Features.Roles.CreateRole;`, `...UpdateRole;` y `...DeleteRole;`, y dentro de `MapEndpoint`, después del `group.MapGet("", ...)`:

```csharp
        group.MapPost("", async (
                CreateRoleCommand command,
                ICommandHandler<CreateRoleCommand, Guid> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(command, cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Roles.Manage);

        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateRoleRequest request,
                ICommandHandler<UpdateRoleCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(
                new UpdateRoleCommand(id, request.Name, request.Description, request.Permissions),
                cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Roles.Manage);

        group.MapDelete("/{id:guid}", async (
                Guid id,
                ICommandHandler<DeleteRoleCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new DeleteRoleCommand(id), cancellationToken)).ToHttpResult())
            .RequirePermission(Permissions.Roles.Manage);
```

y, después de la clase `RolesEndpoints`, en el mismo archivo:

```csharp
/// <summary>El cuerpo de PUT /api/roles/{id}: el id va en la ruta, no en el JSON.</summary>
public sealed record UpdateRoleRequest(string? Name, string? Description, IReadOnlyCollection<string>? Permissions);
```

- [ ] **Paso 12: correr el test y verlo pasar**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Roles.RoleCrudEndpointsTests"
```

Tienen que pasar los 12 tests (`Passed! - Failed: 0, Passed: 12`), incluido `Changing_the_permissions_of_a_role_takes_effect_for_its_users_right_away`, que es el que prueba la invalidación del caché.

- [ ] **Paso 13: build y suite completa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

`dotnet build` sin advertencias y `dotnet test` en verde.

- [ ] **Paso 14: commit**

```bash
git add src/ArquitecturaBase.Domain/Authorization/RoleErrors.cs src/ArquitecturaBase.Application/Resources/Errors.resx src/ArquitecturaBase.Application/Resources/Errors.en.resx src/ArquitecturaBase.Application/Resources/Validation.resx src/ArquitecturaBase.Application/Resources/Validation.en.resx src/ArquitecturaBase.Application/Resources/ValidationMessages.cs src/ArquitecturaBase.Application/Common/Validation/ValidationRules.cs src/ArquitecturaBase.Application/Features/Roles src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs src/ArquitecturaBase.Api/Endpoints/Roles/RolesEndpoints.cs tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs tests/ArquitecturaBase.Api.IntegrationTests/Roles/RoleCrudEndpointsTests.cs
git commit -m "feat: crear, editar y borrar roles con sus permisos" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarea 13: Perfil propio: guardar idioma y último ingreso

Repo: **backend**.

`PUT /api/me` deja cambiar el nombre, el idioma y la zona horaria: con eso el idioma deja de vivir solo en el navegador y pasa a la cuenta, que es el pendiente que dejó la Fase 3. Y `GET /api/me` suma `lastLoginAtUtc`, que sale de `LoginAudits` y completa la tarjeta del tablero.

> Este es el único endpoint de la fase que **no** pide un permiso: la sección 10 del spec dice "bearer". El test equivalente al de 403 es `Updating_the_profile_without_a_token_returns_a_401_problem`, que comprueba que sin token no pasa y que la respuesta es un ProblemDetails.

**Archivos:**
- Crear: `src/ArquitecturaBase.Application/Features/Users/UpdateProfile/UpdateProfileCommand.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Users/UpdateProfile/UpdateProfileCommandValidator.cs`
- Crear: `src/ArquitecturaBase.Application/Features/Users/UpdateProfile/UpdateProfileCommandHandler.cs`
- Modificar: `src/ArquitecturaBase.Application/Resources/Validation.resx`
- Modificar: `src/ArquitecturaBase.Application/Resources/Validation.en.resx`
- Modificar: `src/ArquitecturaBase.Application/Resources/ValidationMessages.cs`
- Modificar: `src/ArquitecturaBase.Application/Features/Auth/UserCultures.cs`
- Modificar: `src/ArquitecturaBase.Application/Features/Users/GetCurrentUser/CurrentUserResponse.cs`
- Modificar: `src/ArquitecturaBase.Application/Features/Users/GetCurrentUser/GetCurrentUserQueryHandler.cs`
- Modificar: `src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs`
- Modificar: `src/ArquitecturaBase.Domain/Authentication/ILoginAuditRepository.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Persistence/Repositories/LoginAuditRepository.cs`
- Modificar: `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs`
- Modificar: `src/ArquitecturaBase.Api/Endpoints/Users/MeEndpoint.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/AuthFakes.cs`
- Modificar: `tests/ArquitecturaBase.Application.UnitTests/Features/Users/GetCurrentUserQueryHandlerTests.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Users/MeProfileEndpointTests.cs`

- [ ] **Paso 1: el test, antes que nada**

`tests/ArquitecturaBase.Api.IntegrationTests/Users/MeProfileEndpointTests.cs`:

```csharp
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

[Collection(ApiTestGroup.Name)]
public sealed class MeProfileEndpointTests(ApiFactory factory)
{
    [Fact]
    public async Task Me_includes_the_last_successful_login()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("ultimo"));

        using var response = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var me = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(factory.Clock.GetUtcNow().UtcDateTime, me.GetProperty("lastLoginAtUtc").GetDateTime());
    }

    [Fact]
    public async Task Updating_the_profile_saves_the_name_the_language_and_the_time_zone()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("perfil"));

        using var update = await client.SendWithTokenAsync(
            HttpMethod.Put,
            "/api/me",
            tokens.AccessToken,
            new { displayName = "Ana", culture = "en", timeZoneId = "America/Sao_Paulo" });
        using var read = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var me = await read.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.Equal("Ana", me.GetProperty("displayName").GetString());
        Assert.Equal("en", me.GetProperty("culture").GetString());
        Assert.Equal("America/Sao_Paulo", me.GetProperty("timeZoneId").GetString());
    }

    [Fact]
    public async Task A_language_that_is_not_supported_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("idioma"));

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Put,
            "/api/me",
            tokens.AccessToken,
            new { culture = "fr", timeZoneId = "America/Argentina/Buenos_Aires" },
            language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Elegí un idioma disponible.", problem.GetProperty("errors").GetProperty("culture")[0].GetString());
    }

    [Fact]
    public async Task A_time_zone_that_does_not_exist_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("zona"));

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Put,
            "/api/me",
            tokens.AccessToken,
            new { culture = "es", timeZoneId = "Marte/Olympus" },
            language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Elegí una zona horaria válida.", problem.GetProperty("errors").GetProperty("timeZoneId")[0].GetString());
    }

    [Fact]
    public async Task Updating_the_profile_without_a_token_returns_a_401_problem()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            HttpMethod.Put,
            "/api/me",
            System.Net.Http.Json.JsonContent.Create(new { culture = "es", timeZoneId = "America/Argentina/Buenos_Aires" }));
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Http.Unauthorized", problem.GetProperty("code").GetString());
    }
}
```

- [ ] **Paso 2: correr el test y ver que falla por la razón correcta**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.MeProfileEndpointTests"
```

`/api/me` solo acepta GET y la respuesta todavía no trae `lastLoginAtUtc`. Tienen que fallar los cinco, el primero con:

```
System.Collections.Generic.KeyNotFoundException : The given key was not present in the dictionary.
```

y los otros cuatro con:

```
Assert.Equal() Failure: Values differ
Expected: NoContent
Actual:   MethodNotAllowed
```

- [ ] **Paso 3: los dos mensajes de validación**

En `src/ArquitecturaBase.Application/Resources/Validation.resx`, antes de `</root>`:

```xml
  <data name="CultureInvalid" xml:space="preserve"><value>Elegí un idioma disponible.</value></data>
  <data name="TimeZoneInvalid" xml:space="preserve"><value>Elegí una zona horaria válida.</value></data>
```

En `src/ArquitecturaBase.Application/Resources/Validation.en.resx`, antes de `</root>`:

```xml
  <data name="CultureInvalid" xml:space="preserve"><value>Choose an available language.</value></data>
  <data name="TimeZoneInvalid" xml:space="preserve"><value>Choose a valid time zone.</value></data>
```

En `src/ArquitecturaBase.Application/Resources/ValidationMessages.cs`, junto a las otras propiedades:

```csharp
    public static string CultureInvalid => Get(nameof(CultureInvalid));

    public static string TimeZoneInvalid => Get(nameof(TimeZoneInvalid));
```

- [ ] **Paso 4: la lista de idiomas se puede consultar**

`src/ArquitecturaBase.Application/Features/Auth/UserCultures.cs` queda así:

```csharp
using System.Globalization;

namespace ArquitecturaBase.Application.Features.Auth;

/// <summary>Idioma de una cuenta: el de la petición si está soportado; si no, español.</summary>
internal static class UserCultures
{
    public const string Default = "es";

    private static readonly string[] Supported = [Default, "en"];

    public static bool IsSupported(string? culture) => culture is not null && Supported.Contains(culture, StringComparer.Ordinal);

    public static string FromCurrentRequest()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        return IsSupported(language) ? language : Default;
    }
}
```

- [ ] **Paso 5: el último ingreso sale de la auditoría**

`src/ArquitecturaBase.Domain/Authentication/ILoginAuditRepository.cs`:

```csharp
namespace ArquitecturaBase.Domain.Authentication;

public interface ILoginAuditRepository
{
    void Add(LoginAudit audit);

    /// <summary>Cuándo ingresó bien por última vez, o null si nunca lo hizo.</summary>
    Task<DateTime?> GetLastSuccessAtUtcAsync(Guid userId, CancellationToken cancellationToken);
}
```

`src/ArquitecturaBase.Infrastructure/Persistence/Repositories/LoginAuditRepository.cs`:

```csharp
using ArquitecturaBase.Domain.Authentication;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class LoginAuditRepository(ApplicationDbContext dbContext) : ILoginAuditRepository
{
    public void Add(LoginAudit audit) => dbContext.LoginAudits.Add(audit);

    public Task<DateTime?> GetLastSuccessAtUtcAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.LoginAudits
            .AsNoTracking()
            .Where(audit => audit.UserId == userId && audit.Succeeded)
            .OrderByDescending(audit => audit.OccurredAtUtc)
            .Select(audit => (DateTime?)audit.OccurredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
}
```

En `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/AuthFakes.cs`, dentro de `InMemoryLoginAuditRepository`:

```csharp
    public Task<DateTime?> GetLastSuccessAtUtcAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Audits
            .Where(audit => audit.UserId == userId && audit.Succeeded)
            .Select(audit => (DateTime?)audit.OccurredAtUtc)
            .Max());
```

- [ ] **Paso 6: /api/me suma el último ingreso**

`src/ArquitecturaBase.Application/Features/Users/GetCurrentUser/CurrentUserResponse.cs`:

```csharp
namespace ArquitecturaBase.Application.Features.Users.GetCurrentUser;

/// <summary>Perfil, roles, permisos, idioma/zona horaria y último ingreso: lo que el front necesita al iniciar (sección 5.6).</summary>
public sealed record CurrentUserResponse(
    Guid Id,
    string Email,
    string? DisplayName,
    string Culture,
    string TimeZoneId,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions,
    DateTime? LastLoginAtUtc);
```

`src/ArquitecturaBase.Application/Features/Users/GetCurrentUser/GetCurrentUserQueryHandler.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Features.Users.GetCurrentUser;

internal sealed class GetCurrentUserQueryHandler(
    ICurrentUser currentUser,
    IIdentityService identityService,
    IPermissionService permissionService,
    ILoginAuditRepository loginAudits)
    : IQueryHandler<GetCurrentUserQuery, CurrentUserResponse>
{
    public async Task<Result<CurrentUserResponse>> Handle(GetCurrentUserQuery query, CancellationToken cancellationToken)
    {
        var user = currentUser.UserId is { } userId
            ? await identityService.FindByIdAsync(userId, cancellationToken)
            : null;

        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var roles = await identityService.GetRolesAsync(user.Id, cancellationToken);
        var permissions = await permissionService.GetPermissionsAsync(user.Id, cancellationToken);

        return new CurrentUserResponse(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Culture,
            user.TimeZoneId,
            [.. roles.Order(StringComparer.Ordinal)],
            [.. permissions.Order(StringComparer.Ordinal)],
            await loginAudits.GetLastSuccessAtUtcAsync(user.Id, cancellationToken));
    }
}
```

Y en `tests/ArquitecturaBase.Application.UnitTests/Features/Users/GetCurrentUserQueryHandlerTests.cs`:

1. Se suma el campo `private readonly InMemoryLoginAuditRepository _loginAudits = new();` junto a los otros dos.
2. El helper pasa a ser:

```csharp
    private GetCurrentUserQueryHandler Handler(Guid? userId) =>
        new(new FakeCurrentUser { UserId = userId }, _identity, _permissions, _loginAudits);
```

3. Y al final de `Returns_the_profile_with_sorted_roles_and_permissions`:

```csharp
        Assert.Null(result.Value.LastLoginAtUtc);
```

- [ ] **Paso 7: guardar el perfil en IIdentityService**

En `src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs`, al final de la interfaz:

```csharp
    /// <summary>El perfil que edita el propio usuario desde PUT /api/me.</summary>
    Task UpdateProfileAsync(
        Guid userId, string? displayName, string culture, string timeZoneId, CancellationToken cancellationToken);
```

En `src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs`, después de `SetDisplayNameAsync`:

```csharp
    public async Task UpdateProfileAsync(
        Guid userId, string? displayName, string culture, string timeZoneId, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userId, cancellationToken);
        user.DisplayName = TrimDisplayName(displayName);
        user.Culture = culture;
        user.TimeZoneId = timeZoneId;

        (await userManager.UpdateAsync(user)).EnsureSucceeded("update the profile");
    }
```

En `tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs`, al final de la clase:

```csharp
    public Task UpdateProfileAsync(
        Guid userId, string? displayName, string culture, string timeZoneId, CancellationToken cancellationToken)
    {
        var index = _users.FindIndex(user => user.Id == userId);
        _users[index] = _users[index] with { DisplayName = displayName, Culture = culture, TimeZoneId = timeZoneId };

        return Task.CompletedTask;
    }
```

- [ ] **Paso 8: el caso de uso**

`src/ArquitecturaBase.Application/Features/Users/UpdateProfile/UpdateProfileCommand.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.UpdateProfile;

/// <summary>El perfil propio (sección 9 del spec de la Fase 4). El usuario sale del token, no del cuerpo.</summary>
public sealed record UpdateProfileCommand(string? DisplayName, string? Culture, string? TimeZoneId) : ICommand;
```

`src/ArquitecturaBase.Application/Features/Users/UpdateProfile/UpdateProfileCommandValidator.cs`:

```csharp
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Features.Users.UpdateProfile;

internal sealed class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(command => command.DisplayName).MaxLength(ValidationRules.DisplayNameMaxLength);

        RuleFor(command => command.Culture)
            .Cascade(CascadeMode.Stop)
            .Required()
            .Must(UserCultures.IsSupported)
            .WithMessage(_ => ValidationMessages.CultureInvalid);

        RuleFor(command => command.TimeZoneId)
            .Cascade(CascadeMode.Stop)
            .Required()
            .Must(IsKnownTimeZone)
            .WithMessage(_ => ValidationMessages.TimeZoneInvalid);
    }

    // Identificadores IANA: .NET los resuelve en Windows y en Linux desde .NET 6 (ICU).
    private static bool IsKnownTimeZone(string? timeZoneId) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId!, out _);
}
```

`src/ArquitecturaBase.Application/Features/Users/UpdateProfile/UpdateProfileCommandHandler.cs`:

```csharp
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Features.Users.UpdateProfile;

internal sealed class UpdateProfileCommandHandler(IIdentityService identityService, ICurrentUser currentUser)
    : ICommandHandler<UpdateProfileCommand>
{
    public async Task<Result> Handle(UpdateProfileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (currentUser.UserId is not { } userId
            || await identityService.FindByIdAsync(userId, cancellationToken) is null)
        {
            return UserErrors.NotFound;
        }

        await identityService.UpdateProfileAsync(
            userId, command.DisplayName, command.Culture!, command.TimeZoneId!, cancellationToken);

        return Result.Success();
    }
}
```

- [ ] **Paso 9: el endpoint**

`src/ArquitecturaBase.Api/Endpoints/Users/MeEndpoint.cs` queda así:

```csharp
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Users.GetCurrentUser;
using ArquitecturaBase.Application.Features.Users.UpdateProfile;

namespace ArquitecturaBase.Api.Endpoints.Users;

/// <summary>
/// El propio usuario: perfil, roles, permisos, idioma, zona horaria y último ingreso (sección 5.6), y la edición
/// de su perfil (sección 9 del spec de la Fase 4). No pide permisos: alcanza con el bearer.
/// </summary>
internal sealed class MeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me").RequireAuthorization().WithTags("Users");

        group.MapGet("", async (
                IQueryHandler<GetCurrentUserQuery, CurrentUserResponse> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new GetCurrentUserQuery(), cancellationToken)).ToHttpResult());

        group.MapPut("", async (
                UpdateProfileCommand command,
                ICommandHandler<UpdateProfileCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(command, cancellationToken)).ToHttpResult());
    }
}
```

- [ ] **Paso 10: correr los dos conjuntos de tests que tocan /api/me**

```bash
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.MeProfileEndpointTests"
dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Users.UsersEndpointsTests"
```

El primero, 5 tests en verde; el segundo (el de siempre de `/api/me`, que ahora pasa por el grupo), también.

```bash
dotnet test --project tests/ArquitecturaBase.Application.UnitTests/ArquitecturaBase.Application.UnitTests.csproj -- --filter-class "ArquitecturaBase.Application.UnitTests.Features.Users.GetCurrentUserQueryHandlerTests"
```

Los 3 tests en verde.

- [ ] **Paso 11: build y suite completa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

`dotnet build` sin advertencias y `dotnet test` en verde. Pegar la salida real.

- [ ] **Paso 12: commit**

```bash
git add src/ArquitecturaBase.Application/Features/Users/UpdateProfile src/ArquitecturaBase.Application/Features/Users/GetCurrentUser src/ArquitecturaBase.Application/Features/Auth/UserCultures.cs src/ArquitecturaBase.Application/Resources/Validation.resx src/ArquitecturaBase.Application/Resources/Validation.en.resx src/ArquitecturaBase.Application/Resources/ValidationMessages.cs src/ArquitecturaBase.Application/Abstractions/Identity/IIdentityService.cs src/ArquitecturaBase.Domain/Authentication/ILoginAuditRepository.cs src/ArquitecturaBase.Infrastructure/Persistence/Repositories/LoginAuditRepository.cs src/ArquitecturaBase.Infrastructure/Identity/IdentityService.cs src/ArquitecturaBase.Api/Endpoints/Users/MeEndpoint.cs tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/FakeIdentityService.cs tests/ArquitecturaBase.Application.UnitTests/TestDoubles/Auth/AuthFakes.cs tests/ArquitecturaBase.Application.UnitTests/Features/Users/GetCurrentUserQueryHandlerTests.cs tests/ArquitecturaBase.Api.IntegrationTests/Users/MeProfileEndpointTests.cs
git commit -m "feat: guardar el perfil propio y mostrar el ultimo ingreso" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarea 14: Pantalla de usuarios: alta, roles y estado

Repo: **front**.

El listado de `/usuarios` de la Fase 3 solo mira. Esta tarea le agrega lo que hace falta para administrar de verdad: dar de alta un correo, cambiarle el nombre y los roles a alguien, activarlo o desactivarlo y eliminarlo, todo en diálogos sobre el mismo listado. Las dos reglas que protegen al sistema (`Users.LastAdmin`, `Users.CannotModifySelf`) se explican con lo que hay que hacer para destrabarlas, no como un error genérico.

**Archivos:**
- Crear: `src/shared/ui/CheckboxField.tsx`
- Crear: `src/shared/api/roles.ts`
- Crear: `src/features/users/errors.ts`
- Crear: `src/features/users/components/UserFormDialog.tsx`
- Crear: `src/features/users/components/UserRolesDialog.tsx`
- Modificar: `src/features/users/api/users.ts`
- Modificar: `src/features/users/columns.tsx`
- Modificar: `src/features/users/pages/UsersPage.tsx`
- Modificar: `src/locales/es/users.json`, `src/locales/en/users.json`
- Test: `src/shared/ui/CheckboxField.test.tsx`
- Test: `src/features/users/pages/UsersPage.test.tsx` (se le suman casos)

- [ ] **Paso 1: los textos, en los dos idiomas**

`src/locales/es/users.json`, completo:

```json
{
  "title": "Usuarios",
  "description": "Las cuentas que pueden entrar al sistema.",
  "searchLabel": "Buscar por correo o nombre",
  "columns": {
    "email": "Correo",
    "displayName": "Nombre",
    "isActive": "Estado",
    "createdAtUtc": "Creado",
    "actions": "Acciones"
  },
  "status": {
    "active": "Activo",
    "inactive": "Inactivo"
  },
  "sort": {
    "none": "Sin ordenar",
    "ascending": "Ordenado por {{field}}, ascendente",
    "descending": "Ordenado por {{field}}, descendente"
  },
  "empty": {
    "title": "No encontramos usuarios",
    "description": "Todavía no hay usuarios cargados.",
    "searchDescription": "Probá con otro correo o nombre."
  },
  "actions": {
    "new": "Nuevo usuario",
    "editRoles": "Roles",
    "editRolesFor": "Editar los roles de {{email}}",
    "activate": "Activar",
    "activateFor": "Activar a {{email}}",
    "deactivate": "Desactivar",
    "deactivateFor": "Desactivar a {{email}}",
    "delete": "Eliminar",
    "deleteFor": "Eliminar a {{email}}"
  },
  "create": {
    "title": "Nuevo usuario",
    "description": "Dale de alta el correo. Después entra con su código, como todos.",
    "submit": "Dar de alta",
    "success": "Listo: ya puede ingresar."
  },
  "edit": {
    "title": "Editar usuario",
    "description": "Cambiá el nombre y los roles de {{email}}.",
    "submit": "Guardar",
    "success": "Guardamos los cambios."
  },
  "form": {
    "email": "Correo electrónico",
    "emailInvalid": "Ingresá un correo electrónico válido.",
    "displayName": "Nombre",
    "displayNameHint": "Opcional. Es el nombre que se muestra en el sistema.",
    "roles": "Roles",
    "rolesEmpty": "Todavía no hay roles para asignar.",
    "rolesNeedPermission": "Para asignar roles necesitás permiso de lectura sobre ellos."
  },
  "deactivate": {
    "title": "Desactivar a {{email}}",
    "description": "Le cortamos el acceso ahora mismo: la sesión que tenga abierta deja de servir y no va a poder volver a entrar hasta que la actives de nuevo. Sus datos quedan como están.",
    "confirm": "Desactivar"
  },
  "delete": {
    "title": "Eliminar a {{email}}",
    "description": "La cuenta desaparece de los listados y no va a poder ingresar. Su historial de ingresos se conserva, y si más adelante la das de alta con el mismo correo, se restaura.",
    "confirm": "Eliminar"
  },
  "feedback": {
    "activated": "Ya puede volver a ingresar.",
    "deactivated": "Le cortamos el acceso.",
    "deleted": "Eliminamos la cuenta."
  },
  "errors": {
    "alreadyExists": "Ya hay una cuenta con ese correo.",
    "lastAdmin": "No podés dejar al sistema sin administradores. Asigná el rol Admin a otra cuenta activa y volvé a intentar.",
    "cannotModifySelf": "No podés hacerte esto a vos mismo. Pedíselo a otro administrador."
  },
  "errorTraceId": "Código para reportar: {{traceId}}"
}
```

`src/locales/en/users.json`, completo:

```json
{
  "title": "Users",
  "description": "The accounts that can sign in to the system.",
  "searchLabel": "Search by email or name",
  "columns": {
    "email": "Email",
    "displayName": "Name",
    "isActive": "Status",
    "createdAtUtc": "Created",
    "actions": "Actions"
  },
  "status": {
    "active": "Active",
    "inactive": "Inactive"
  },
  "sort": {
    "none": "Not sorted",
    "ascending": "Sorted by {{field}}, ascending",
    "descending": "Sorted by {{field}}, descending"
  },
  "empty": {
    "title": "We couldn't find any users",
    "description": "There are no users yet.",
    "searchDescription": "Try another email or name."
  },
  "actions": {
    "new": "New user",
    "editRoles": "Roles",
    "editRolesFor": "Edit the roles of {{email}}",
    "activate": "Activate",
    "activateFor": "Activate {{email}}",
    "deactivate": "Deactivate",
    "deactivateFor": "Deactivate {{email}}",
    "delete": "Delete",
    "deleteFor": "Delete {{email}}"
  },
  "create": {
    "title": "New user",
    "description": "Add the email address. They sign in with their code, like everyone else.",
    "submit": "Add user",
    "success": "Done: they can sign in now."
  },
  "edit": {
    "title": "Edit user",
    "description": "Change the name and the roles of {{email}}.",
    "submit": "Save",
    "success": "Changes saved."
  },
  "form": {
    "email": "Email address",
    "emailInvalid": "Enter a valid email address.",
    "displayName": "Name",
    "displayNameHint": "Optional. The name shown across the system.",
    "roles": "Roles",
    "rolesEmpty": "There are no roles to assign yet.",
    "rolesNeedPermission": "You need read permission on roles to assign them."
  },
  "deactivate": {
    "title": "Deactivate {{email}}",
    "description": "Their access is cut right away: any open session stops working and they can't sign in again until you activate them. Their data stays as it is.",
    "confirm": "Deactivate"
  },
  "delete": {
    "title": "Delete {{email}}",
    "description": "The account disappears from the listings and can't sign in. Their sign-in history is kept, and if you add the same email again later, the account is restored.",
    "confirm": "Delete"
  },
  "feedback": {
    "activated": "They can sign in again.",
    "deactivated": "Their access was cut.",
    "deleted": "The account was deleted."
  },
  "errors": {
    "alreadyExists": "There is already an account with that email.",
    "lastAdmin": "You can't leave the system without administrators. Give the Admin role to another active account and try again.",
    "cannotModifySelf": "You can't do this to your own account. Ask another administrator."
  },
  "errorTraceId": "Code to report: {{traceId}}"
}
```

- [ ] **Paso 2: el catálogo de roles, en `shared`**

`GET /api/roles` lo piden dos módulos: la pantalla de roles (Tarea 15) y el diálogo de usuarios que asigna roles. Como una feature nunca importa de otra, la lectura vive en `shared/api` y las mutaciones quedan en la feature de roles.

Crear `src/shared/api/roles.ts`:

```ts
import { api } from "./httpClient";

/// Un rol con sus permisos y cuánta gente lo tiene (`GET /api/roles`, sección 10 del spec de la Fase 4).
export interface RoleListItem {
  readonly id: string;
  readonly name: string;
  readonly description: string | null;
  readonly isSystemRole: boolean;
  readonly userCount: number;
  readonly permissions: readonly string[];
}

/// Vive acá y no en `features/roles` porque lo piden dos módulos: la pantalla de roles y el diálogo de
/// usuarios que asigna roles. Una feature nunca importa de otra: lo común sube a `shared`.
export const rolesQueryKey = ["roles"] as const;

export function fetchRoles(): Promise<readonly RoleListItem[]> {
  return api.get<readonly RoleListItem[]>("/api/roles");
}
```

- [ ] **Paso 3: las llamadas nuevas de usuarios**

`src/features/users/api/users.ts`, completo:

```ts
import { api } from "@/shared/api/httpClient";
import type { PagedResult } from "@/shared/api/pagedResult";

export interface UserListItem {
  readonly id: string;
  readonly email: string;
  readonly displayName: string | null;
  readonly isActive: boolean;
  readonly createdAtUtc: string;
}

/// El detalle que devuelve `GET /api/users/{id}`: lo mismo que el listado, más los roles.
export interface UserDetail extends UserListItem {
  readonly roles: readonly string[];
}

export interface UsersQuery {
  readonly page: number;
  readonly pageSize: number;
  readonly sort?: string;
  readonly search?: string;
}

export interface CreateUserBody {
  readonly email: string;
  readonly displayName: string | null;
  readonly roles: readonly string[];
}

/// `PUT /api/users/{id}` reemplaza los dos campos: mandar solo los roles le borraría el nombre a la persona.
export interface UpdateUserBody {
  readonly displayName: string | null;
  readonly roles: readonly string[];
}

/// Prefijo de todas las consultas del listado: es lo que invalidan las mutaciones, sin importar la página,
/// el orden ni la búsqueda que tenga puesta la pantalla.
export const usersQueryKeyRoot = ["users"] as const;

export const usersQueryKey = (query: UsersQuery) => ["users", query] as const;

export const userQueryKey = (id: string) => ["user", id] as const;

export function fetchUsers(query: UsersQuery): Promise<PagedResult<UserListItem>> {
  const params = new URLSearchParams({ page: String(query.page), pageSize: String(query.pageSize) });

  if (query.sort) {
    params.set("sort", query.sort);
  }

  if (query.search) {
    params.set("search", query.search);
  }

  return api.get<PagedResult<UserListItem>>(`/api/users?${params.toString()}`);
}

export function fetchUser(id: string): Promise<UserDetail> {
  return api.get<UserDetail>(`/api/users/${id}`);
}

/// Devuelve el id del usuario nuevo: el handler es `ICommand<Guid>` y `ToHttpResult` responde 200 con el valor.
export function createUser(body: CreateUserBody): Promise<string> {
  return api.post<string>("/api/users", body);
}

export function updateUser(id: string, body: UpdateUserBody): Promise<void> {
  return api.put<void>(`/api/users/${id}`, body);
}

export function setUserActive(id: string, isActive: boolean): Promise<void> {
  return api.post<void>(`/api/users/${id}/${isActive ? "activate" : "deactivate"}`);
}

export function deleteUser(id: string): Promise<void> {
  return api.delete<void>(`/api/users/${id}`);
}
```

- [ ] **Paso 4: el mensaje de cada error, decidido por el código**

Crear `src/features/users/errors.ts`:

```ts
import { ApiError } from "@/shared/api/ApiError";

/// Firma mínima que necesitamos de `t`, la misma que usa `columns.tsx`: alcanza con la del namespace "users",
/// sin acoplar el tipo exacto de react-i18next, que cambia de versión en versión.
type Translate = (key: string, options?: Record<string, unknown>) => string;

/// El front decide por el `code`, que es estable, nunca por el texto. Las dos reglas que protegen al sistema
/// (sección 8 del spec de la Fase 4) se explican con lo que hay que hacer para destrabarlas, que es algo que
/// el backend no puede saber. El resto de los errores ya vienen traducidos del servidor.
export function userActionErrorMessage(error: unknown, t: Translate): string {
  if (!(error instanceof ApiError)) {
    return t("common:states.error");
  }

  if (error.isNetworkError) {
    return t("common:errors.network");
  }

  switch (error.code) {
    case "Users.User.AlreadyExists":
      return t("errors.alreadyExists");
    case "Users.User.LastAdmin":
      return t("errors.lastAdmin");
    case "Users.User.CannotModifySelf":
      return t("errors.cannotModifySelf");
    default:
      return error.detail ?? t("common:states.error");
  }
}
```

- [ ] **Paso 5: el test de la casilla con etiqueta (tiene que fallar)**

`shared/ui` no tiene ninguna casilla con etiqueta: `checkbox.tsx` es el primitivo pelado de shadcn y `FormField` pone la etiqueta **arriba** del control, que para una casilla no sirve. Los dos diálogos de esta tarea y el de roles de la Tarea 15 necesitan lo mismo, así que va a `shared/ui` como componente nuestro (PascalCase).

Crear `src/shared/ui/CheckboxField.test.tsx`:

```tsx
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { CheckboxField } from "./CheckboxField";
import { renderWithProviders } from "@/test/utils/renderWithProviders";

describe("CheckboxField", () => {
  it("names the checkbox with its label", () => {
    renderWithProviders(<CheckboxField label="Admin" checked={false} onCheckedChange={vi.fn()} />);

    expect(screen.getByRole("checkbox", { name: "Admin" })).not.toBeChecked();
  });

  it("reports the new value when it is clicked", async () => {
    const onCheckedChange = vi.fn();
    renderWithProviders(<CheckboxField label="Admin" checked={false} onCheckedChange={onCheckedChange} />);

    await userEvent.click(screen.getByRole("checkbox", { name: "Admin" }));

    expect(onCheckedChange).toHaveBeenCalledWith(true);
  });

  it("describes the checkbox with its help line", () => {
    renderWithProviders(
      <CheckboxField label="Admin" description="Puede hacer todo." checked onCheckedChange={vi.fn()} />,
    );

    expect(screen.getByRole("checkbox", { name: "Admin" })).toHaveAccessibleDescription("Puede hacer todo.");
  });
});
```

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBaseFront
npm run test -- CheckboxField
```

Tiene que fallar, y por no existir el componente todavía: `Error: Failed to resolve import "./CheckboxField" from "src/shared/ui/CheckboxField.test.tsx"`.

- [ ] **Paso 6: la casilla con etiqueta**

Crear `src/shared/ui/CheckboxField.tsx`:

```tsx
import { useId, type ReactNode } from "react";
import { Checkbox } from "./checkbox";
import { Label } from "./label";

interface CheckboxFieldProps {
  label: string;
  checked: boolean;
  onCheckedChange: (checked: boolean) => void;
  /// Línea de ayuda debajo de la etiqueta.
  description?: string;
  disabled?: boolean;
}

/// Una casilla con su etiqueta al lado. `FormField` pone la etiqueta arriba del control, que para una casilla
/// no sirve: acá van en la misma fila, atadas por el id, así la etiqueta es el nombre accesible de la casilla
/// y hacerle clic la marca.
export function CheckboxField({
  label,
  checked,
  onCheckedChange,
  description,
  disabled,
}: CheckboxFieldProps): ReactNode {
  const id = useId();
  const descriptionId = `${id}-description`;

  return (
    <div className="flex items-start gap-2">
      <Checkbox
        id={id}
        checked={checked}
        disabled={disabled}
        aria-describedby={description ? descriptionId : undefined}
        // Radix informa `boolean | "indeterminate"`; acá solo hay marcada o no.
        onCheckedChange={(value) => onCheckedChange(value === true)}
        className="mt-0.5"
      />
      <div className="flex flex-col gap-0.5">
        <Label htmlFor={id} className="font-normal">
          {label}
        </Label>
        {description ? (
          <p id={descriptionId} className="text-xs text-[var(--color-content-muted)]">
            {description}
          </p>
        ) : null}
      </div>
    </div>
  );
}
```

```bash
npm run test -- CheckboxField
```

Esperado: `Test Files 1 passed (1)`, `Tests 3 passed (3)`.

- [ ] **Paso 7: los tests de la pantalla (tienen que fallar)**

`src/features/users/pages/UsersPage.test.tsx`, completo (los cinco casos de la Fase 3 quedan igual; se suman seis):

```tsx
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { HttpResponse, http } from "msw";
import { toast } from "sonner";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderRouteWithProviders } from "@/test/utils/renderWithProviders";
import { currentUser } from "@/test/mocks/handlers";
import { queryClient } from "@/shared/api/queryClient";
import { server } from "@/test/mocks/server";

vi.mock("react-oidc-context", async () => {
  const actual = await vi.importActual<typeof import("react-oidc-context")>("react-oidc-context");

  return { ...actual, useAuth: () => ({ isAuthenticated: true, isLoading: false, user: { access_token: "t" } }) };
});

const page = {
  items: [
    { id: "1", email: "ana@example.com", displayName: "Ana", isActive: true, createdAtUtc: "2026-09-18T12:00:00Z" },
    { id: "2", email: "beto@example.com", displayName: null, isActive: false, createdAtUtc: "2026-09-18T13:00:00Z" },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 2,
  totalPages: 1,
  hasPrevious: false,
  hasNext: false,
};

/// El perfil de quien sí puede administrar. El handler por defecto solo trae "users.read".
const admin = { ...currentUser, permissions: ["users.read", "users.manage", "roles.read"] };

const roles = [
  { id: "r1", name: "Admin", description: "Puede hacer todo.", isSystemRole: true, userCount: 1, permissions: [] },
  { id: "r2", name: "User", description: null, isSystemRole: true, userCount: 3, permissions: [] },
];

/// El arnés corre con `onUnhandledRequest: "error"`: cada test declara todo lo que su pantalla va a pedir.
function adminHandlers() {
  return [
    http.get("/api/me", () => HttpResponse.json(admin)),
    http.get("/api/users", () => HttpResponse.json(page)),
    http.get("/api/roles", () => HttpResponse.json(roles)),
  ];
}

describe("UsersPage", () => {
  // AppProviders usa el queryClient de la app (un singleton, con staleTime). Sin esto, la respuesta de
  // /api/users de un test queda cacheada y se filtra al siguiente, que pisó el handler con otra respuesta
  // (mismo patrón que Sidebar.test.tsx).
  beforeEach(() => {
    queryClient.clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("shows the users of the first page", async () => {
    server.use(http.get("/api/users", () => HttpResponse.json(page)));

    renderRouteWithProviders("/usuarios");

    // El usuario de sesión por defecto (test/mocks/handlers.ts) también se llama Ana <ana@example.com> y su
    // correo aparece siempre al pie del sidebar: hay que acotar la búsqueda a la tabla para no toparse con
    // esa otra coincidencia (y de paso, esperar a que la tabla haya montado, no cualquier texto suelto).
    const table = await screen.findByRole("table");
    expect(within(table).getByText("ana@example.com")).toBeInTheDocument();
    expect(within(table).getByText("beto@example.com")).toBeInTheDocument();
  });

  it("sends the search to the backend and keeps it in the URL", async () => {
    const requests: string[] = [];
    server.use(
      http.get("/api/users", ({ request }) => {
        requests.push(new URL(request.url).search);
        return HttpResponse.json(page);
      }),
    );

    renderRouteWithProviders("/usuarios");
    await screen.findByRole("table");
    await userEvent.type(screen.getByRole("searchbox"), "ana");

    await waitFor(() => expect(requests.at(-1)).toContain("search=ana"));
  });

  it("shows the message when the search returns nothing", async () => {
    server.use(http.get("/api/users", () => HttpResponse.json({ ...page, items: [], totalCount: 0, totalPages: 0 })));

    renderRouteWithProviders("/usuarios");

    expect(await screen.findByRole("heading", { name: /no encontramos usuarios/i })).toBeInTheDocument();
  });

  it("shows the forbidden page when the backend answers 403", async () => {
    server.use(
      http.get("/api/users", () =>
        HttpResponse.json({ status: 403, code: "Http.Forbidden", detail: "No tenés permiso." }, { status: 403 }),
      ),
    );

    renderRouteWithProviders("/usuarios");

    // La sección 6.1 del spec pide la pantalla de "sin permiso", no un error dentro del listado.
    expect(await screen.findByRole("heading", { name: /no tenés permiso/i })).toBeInTheDocument();
  });

  it("shows the error state with the traceId when the backend fails", async () => {
    server.use(
      http.get("/api/users", () =>
        HttpResponse.json(
          { status: 500, code: "General.Unexpected", detail: "Ocurrió un error inesperado.", traceId: "trace-123" },
          { status: 500 },
        ),
      ),
    );

    renderRouteWithProviders("/usuarios");

    expect(await screen.findByRole("heading", { name: /ocurrió un error inesperado/i })).toBeInTheDocument();
    // El texto exacto: el toast del queryClient global también menciona el traceId (sección 6.1 del spec),
    // así que no alcanza con buscar "trace-123" a secas.
    expect(screen.getByText("Código para reportar: trace-123")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /reintentar/i })).toBeInTheDocument();
  });

  it("creates a user from the new user dialog", async () => {
    const created: unknown[] = [];
    server.use(
      ...adminHandlers(),
      http.post("/api/users", async ({ request }) => {
        created.push(await request.json());

        return HttpResponse.json("0199a0c0-0000-7000-8000-000000000001");
      }),
    );

    renderRouteWithProviders("/usuarios");

    await userEvent.click(await screen.findByRole("button", { name: "Nuevo usuario" }));
    await userEvent.type(await screen.findByRole("textbox", { name: "Correo electrónico" }), "nueva@example.com");
    await userEvent.click(await screen.findByRole("checkbox", { name: "Admin" }));
    await userEvent.click(screen.getByRole("button", { name: "Dar de alta" }));

    await waitFor(() =>
      expect(created).toEqual([{ email: "nueva@example.com", displayName: null, roles: ["Admin"] }]),
    );
  });

  it("keeps the dialog open and explains that the email already has an account", async () => {
    server.use(
      ...adminHandlers(),
      http.post("/api/users", () =>
        HttpResponse.json(
          { status: 409, code: "Users.User.AlreadyExists", detail: "Ese correo ya tiene cuenta." },
          { status: 409 },
        ),
      ),
    );

    renderRouteWithProviders("/usuarios");

    await userEvent.click(await screen.findByRole("button", { name: "Nuevo usuario" }));
    await userEvent.type(await screen.findByRole("textbox", { name: "Correo electrónico" }), "ana@example.com");
    await userEvent.click(screen.getByRole("button", { name: "Dar de alta" }));

    // El texto lo decide el `code`, no el `detail`: el del backend no dice qué hacer con una cuenta borrada.
    expect(await screen.findByRole("alert")).toHaveTextContent("Ya hay una cuenta con ese correo.");
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("saves the roles of a user without erasing the name", async () => {
    const updates: unknown[] = [];
    server.use(
      ...adminHandlers(),
      http.get("/api/users/1", () => HttpResponse.json({ ...page.items[0], roles: ["User"] })),
      http.put("/api/users/1", async ({ request }) => {
        updates.push(await request.json());

        return new HttpResponse(null, { status: 204 });
      }),
    );

    renderRouteWithProviders("/usuarios");

    await userEvent.click(await screen.findByRole("button", { name: "Editar los roles de ana@example.com" }));
    await userEvent.click(await screen.findByRole("checkbox", { name: "Admin" }));
    await userEvent.click(screen.getByRole("button", { name: "Guardar" }));

    // El PUT reemplaza nombre y roles: el nombre que ya tenía tiene que volver tal cual.
    await waitFor(() => expect(updates).toEqual([{ displayName: "Ana", roles: ["User", "Admin"] }]));
  });

  it("explains the protection rule when the backend refuses to deactivate the last admin", async () => {
    const toastError = vi.spyOn(toast, "error");
    server.use(
      ...adminHandlers(),
      http.post("/api/users/1/deactivate", () =>
        HttpResponse.json(
          { status: 409, code: "Users.User.LastAdmin", detail: "Tiene que quedar un administrador." },
          { status: 409 },
        ),
      ),
    );

    renderRouteWithProviders("/usuarios");

    await userEvent.click(await screen.findByRole("button", { name: "Desactivar a ana@example.com" }));
    await userEvent.click(await screen.findByRole("button", { name: "Desactivar" }));

    await waitFor(() =>
      expect(toastError).toHaveBeenCalledWith(
        "No podés dejar al sistema sin administradores. Asigná el rol Admin a otra cuenta activa y volvé a intentar.",
      ),
    );
  });

  it("says what is lost before deleting, and deletes when confirmed", async () => {
    let deleted = 0;
    server.use(
      ...adminHandlers(),
      http.delete("/api/users/1", () => {
        deleted += 1;

        return new HttpResponse(null, { status: 204 });
      }),
    );

    renderRouteWithProviders("/usuarios");

    await userEvent.click(await screen.findByRole("button", { name: "Eliminar a ana@example.com" }));

    expect(await screen.findByText(/su historial de ingresos se conserva/i)).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Eliminar" }));

    await waitFor(() => expect(deleted).toBe(1));
  });

  it("hides the new user button and the row actions without users.manage", async () => {
    // El handler por defecto de /api/me devuelve permissions: ["users.read"].
    server.use(http.get("/api/users", () => HttpResponse.json(page)));

    renderRouteWithProviders("/usuarios");

    // Hay que esperar a que /api/me haya resuelto: hasta que no llega, los permisos están pendientes y las
    // acciones tampoco se ven, con lo cual la aserción pasaría sin haber probado nada. El pie del sidebar
    // solo pinta el correo cuando esa consulta trajo al usuario (mismo patrón que Sidebar.test.tsx).
    const sidebar = await screen.findByRole("complementary");
    await within(sidebar).findByText(currentUser.email);

    expect(screen.queryByRole("button", { name: "Nuevo usuario" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /eliminar a/i })).not.toBeInTheDocument();
  });
});
```

```bash
npm run test -- UsersPage
```

Tiene que fallar en los seis casos nuevos, y por no existir todavía la interfaz. El primero:

```
TestingLibraryElementError: Unable to find an accessible element with the role "button" and name "Nuevo usuario"
```

Los cinco de la Fase 3 tienen que seguir en verde.

- [ ] **Paso 8: la columna de acciones**

`src/features/users/columns.tsx`, completo:

```tsx
import type { UserListItem } from "./api/users";
import { Badge } from "@/shared/ui/badge";
import { Button } from "@/shared/ui/button";
import type { Column } from "@/shared/ui/DataTable";

/// Firma mínima que necesitamos de `t`: alcanza con la del namespace "users" (sin acoplar el tipo exacto de
/// react-i18next, que cambia de versión en versión).
type Translate = (key: string, options?: Record<string, unknown>) => string;

/// Lo que hace la pantalla cuando se aprieta una acción de la fila. Si no viene, la columna no se arma: es
/// lo que pasa cuando el usuario no tiene `users.manage`.
export interface UserRowActions {
  onEditRoles: (user: UserListItem) => void;
  onToggleActive: (user: UserListItem) => void;
  onDelete: (user: UserListItem) => void;
}

/// La fecha llega en UTC (sección 6.3 del spec): se muestra en la zona horaria del perfil, no la cadena cruda.
function formatCreatedAt(valueUtc: string, language: string, timeZone: string | undefined): string {
  return new Intl.DateTimeFormat(language, { dateStyle: "medium", timeStyle: "short", timeZone }).format(
    new Date(valueUtc),
  );
}

/// Las columnas ordenables (email, displayName, createdAtUtc) coinciden con la lista blanca del backend
/// (`GetUsersQuery.SortableFields`): el nombre es el que viaja en `sort`.
export function createUserColumns(
  t: Translate,
  language: string,
  timeZone: string | undefined,
  actions?: UserRowActions,
): Column<UserListItem>[] {
  const columns: Column<UserListItem>[] = [
    { id: "email", header: t("columns.email"), cell: (row) => row.email, sortable: true },
    { id: "displayName", header: t("columns.displayName"), cell: (row) => row.displayName ?? "—", sortable: true },
    {
      id: "isActive",
      header: t("columns.isActive"),
      cell: (row) =>
        row.isActive ? (
          <Badge variant="outline" className="border-transparent bg-[var(--color-success)]/15 text-[var(--color-success)]">
            {t("status.active")}
          </Badge>
        ) : (
          <Badge variant="secondary">{t("status.inactive")}</Badge>
        ),
    },
    {
      id: "createdAtUtc",
      header: t("columns.createdAtUtc"),
      cell: (row) => formatCreatedAt(row.createdAtUtc, language, timeZone),
      sortable: true,
    },
  ];

  if (!actions) {
    return columns;
  }

  // El nombre accesible de cada botón lleva el correo de la fila: sin eso, veinte filas dan veinte botones
  // llamados igual, y no hay forma de apretar el de una persona en concreto (ni con el teclado, ni en un test).
  columns.push({
    id: "actions",
    header: t("columns.actions"),
    align: "right",
    cell: (row) => (
      <div className="flex justify-end gap-1">
        <Button
          type="button"
          variant="ghost"
          size="sm"
          aria-label={t("actions.editRolesFor", { email: row.email })}
          onClick={() => actions.onEditRoles(row)}
        >
          {t("actions.editRoles")}
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          aria-label={
            row.isActive
              ? t("actions.deactivateFor", { email: row.email })
              : t("actions.activateFor", { email: row.email })
          }
          onClick={() => actions.onToggleActive(row)}
        >
          {row.isActive ? t("actions.deactivate") : t("actions.activate")}
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="text-[var(--color-danger)]"
          aria-label={t("actions.deleteFor", { email: row.email })}
          onClick={() => actions.onDelete(row)}
        >
          {t("actions.delete")}
        </Button>
      </div>
    ),
  });

  return columns;
}
```

- [ ] **Paso 9: el diálogo de alta**

Crear `src/features/users/components/UserFormDialog.tsx`:

```tsx
import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useForm, type Path } from "react-hook-form";
import { useTranslation } from "react-i18next";
import { toast } from "sonner";
import { z } from "zod";
import { createUser, usersQueryKeyRoot } from "../api/users";
import { userActionErrorMessage } from "../errors";
import { usePermissions } from "@/auth/usePermissions";
import { ApiError } from "@/shared/api/ApiError";
import { applyApiErrorToForm } from "@/shared/api/formErrors";
import { fetchRoles, rolesQueryKey } from "@/shared/api/roles";
import { Button } from "@/shared/ui/button";
import { CheckboxField } from "@/shared/ui/CheckboxField";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/shared/ui/dialog";
import { FormField } from "@/shared/ui/FormField";
import { Input } from "@/shared/ui/input";
import { Skeleton } from "@/shared/ui/skeleton";

const schema = z.object({ email: z.email(), displayName: z.string().optional() });

type FormValues = z.infer<typeof schema>;

/// Alta de un usuario (`POST /api/users`). Invitar es dar de alta el correo: la persona entra después con su
/// código, como todos (sección 3 del spec de la Fase 4).
///
/// La pantalla lo monta solo mientras está abierto, así el formulario arranca vacío cada vez sin tener que
/// resetearlo a mano cuando cambia `open`.
export function UserFormDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation("users");
  const queryClient = useQueryClient();
  const { has } = usePermissions();
  const canReadRoles = has("roles.read");

  const [roles, setRoles] = useState<string[]>([]);
  const [formError, setFormError] = useState<string | undefined>();

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  function setFieldError(field: string, error: { type: string; message: string }) {
    // El backend responde los nombres de campo en camelCase, que acá son las claves de FormValues.
    setError(field as Path<FormValues>, error);
  }

  const rolesQuery = useQuery({ queryKey: rolesQueryKey, queryFn: fetchRoles, enabled: canReadRoles });

  const mutation = useMutation({
    mutationFn: (values: FormValues) =>
      createUser({ email: values.email, displayName: values.displayName?.trim() || null, roles }),
    onSuccess: async () => {
      toast.success(t("create.success"));
      await queryClient.invalidateQueries({ queryKey: usersQueryKeyRoot });
      onClose();
    },
    onError: (error) => {
      if (error instanceof ApiError && applyApiErrorToForm(error, setFieldError)) {
        return;
      }

      setFormError(userActionErrorMessage(error, t));
    },
  });

  const availableRoles = rolesQuery.data ?? [];

  function toggleRole(name: string, checked: boolean) {
    setRoles((current) => (checked ? [...current, name] : current.filter((role) => role !== name)));
  }

  return (
    <Dialog
      open
      onOpenChange={(next) => {
        if (!next) {
          onClose();
        }
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("create.title")}</DialogTitle>
          <DialogDescription>{t("create.description")}</DialogDescription>
        </DialogHeader>

        <form
          noValidate
          className="flex flex-col gap-4"
          onSubmit={(event) => {
            setFormError(undefined);
            void handleSubmit((values) => mutation.mutate(values))(event);
          }}
        >
          <FormField label={t("form.email")} required error={errors.email ? t("form.emailInvalid") : undefined}>
            <Input type="email" autoComplete="off" {...register("email")} />
          </FormField>

          <FormField label={t("form.displayName")} hint={t("form.displayNameHint")}>
            <Input type="text" autoComplete="off" {...register("displayName")} />
          </FormField>

          {canReadRoles ? (
            <fieldset className="flex flex-col gap-2">
              <legend className="mb-1 text-sm font-medium">{t("form.roles")}</legend>
              {rolesQuery.isPending ? <Skeleton aria-hidden="true" className="h-10" /> : null}
              {!rolesQuery.isPending && availableRoles.length === 0 ? (
                <p className="text-sm text-[var(--color-content-muted)]">{t("form.rolesEmpty")}</p>
              ) : null}
              {availableRoles.map((role) => (
                <CheckboxField
                  key={role.id}
                  label={role.name}
                  description={role.description ?? undefined}
                  checked={roles.includes(role.name)}
                  onCheckedChange={(checked) => toggleRole(role.name, checked)}
                />
              ))}
            </fieldset>
          ) : (
            <p className="text-sm text-[var(--color-content-muted)]">{t("form.rolesNeedPermission")}</p>
          )}

          {formError ? (
            <p role="alert" className="text-sm text-[var(--color-danger)]">
              {formError}
            </p>
          ) : null}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose}>
              {t("common:actions.cancel")}
            </Button>
            <Button type="submit" disabled={mutation.isPending}>
              {t("create.submit")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
```

- [ ] **Paso 10: el diálogo de edición**

Crear `src/features/users/components/UserRolesDialog.tsx`:

```tsx
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { toast } from "sonner";
import { fetchUser, updateUser, userQueryKey, usersQueryKeyRoot, type UserListItem } from "../api/users";
import { userActionErrorMessage } from "../errors";
import { currentUserQueryKey } from "@/auth/useCurrentUser";
import { usePermissions } from "@/auth/usePermissions";
import { fetchRoles, rolesQueryKey } from "@/shared/api/roles";
import { Button } from "@/shared/ui/button";
import { CheckboxField } from "@/shared/ui/CheckboxField";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/shared/ui/dialog";
import { FormField } from "@/shared/ui/FormField";
import { Input } from "@/shared/ui/input";
import { Skeleton } from "@/shared/ui/skeleton";

interface DraftValues {
  displayName: string;
  roles: string[];
}

/// Edición de un usuario (`PUT /api/users/{id}`): nombre y roles.
///
/// El nombre va en el mismo diálogo que los roles porque el PUT reemplaza los dos campos: mandar solo los
/// roles le borraría el nombre a la persona. Los roles que ya tiene salen de `GET /api/users/{id}`, que es
/// lo único que los sabe (el listado no los trae).
export function UserRolesDialog({ user, onClose }: { user: UserListItem; onClose: () => void }) {
  const { t } = useTranslation("users");
  const queryClient = useQueryClient();
  const { has } = usePermissions();
  const canReadRoles = has("roles.read");

  const [draft, setDraft] = useState<DraftValues | undefined>();
  const [loadedUserId, setLoadedUserId] = useState<string | undefined>();
  const [formError, setFormError] = useState<string | undefined>();

  const detailQuery = useQuery({ queryKey: userQueryKey(user.id), queryFn: () => fetchUser(user.id) });
  const rolesQuery = useQuery({ queryKey: rolesQueryKey, queryFn: fetchRoles, enabled: canReadRoles });

  const detail = detailQuery.data;

  // El borrador arranca con lo que trajo el detalle. Se ajusta durante el render y no con un efecto que copia
  // datos a otro estado (regla de rendimiento del CLAUDE.md del front). Si el detalle se vuelve a consultar,
  // el borrador no se pisa: lo que esté escrito a medias es del usuario.
  if (detail && loadedUserId !== detail.id) {
    setLoadedUserId(detail.id);
    setDraft({ displayName: detail.displayName ?? "", roles: [...detail.roles] });
  }

  const mutation = useMutation({
    mutationFn: (values: DraftValues) =>
      updateUser(user.id, { displayName: values.displayName.trim() || null, roles: values.roles }),
    onSuccess: async () => {
      toast.success(t("edit.success"));
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: usersQueryKeyRoot }),
        queryClient.invalidateQueries({ queryKey: userQueryKey(user.id) }),
        // Si se cambió los roles a sí mismo, sus propios permisos pueden haber cambiado.
        queryClient.invalidateQueries({ queryKey: currentUserQueryKey }),
      ]);
      onClose();
    },
    onError: (error) => setFormError(userActionErrorMessage(error, t)),
  });

  const availableRoles = rolesQuery.data ?? [];

  function toggleRole(name: string, checked: boolean) {
    setDraft((current) =>
      current === undefined
        ? current
        : {
            ...current,
            roles: checked ? [...current.roles, name] : current.roles.filter((role) => role !== name),
          },
    );
  }

  return (
    <Dialog
      open
      onOpenChange={(next) => {
        if (!next) {
          onClose();
        }
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("edit.title")}</DialogTitle>
          <DialogDescription>{t("edit.description", { email: user.email })}</DialogDescription>
        </DialogHeader>

        {draft === undefined ? (
          <div className="flex flex-col gap-3">
            <Skeleton aria-hidden="true" className="h-9" />
            <Skeleton aria-hidden="true" className="h-24" />
          </div>
        ) : (
          <form
            noValidate
            className="flex flex-col gap-4"
            onSubmit={(event) => {
              event.preventDefault();
              setFormError(undefined);
              mutation.mutate(draft);
            }}
          >
            <FormField label={t("form.displayName")} hint={t("form.displayNameHint")}>
              <Input
                type="text"
                autoComplete="off"
                value={draft.displayName}
                onChange={(event) => setDraft({ ...draft, displayName: event.target.value })}
              />
            </FormField>

            {canReadRoles ? (
              <fieldset className="flex flex-col gap-2">
                <legend className="mb-1 text-sm font-medium">{t("form.roles")}</legend>
                {rolesQuery.isPending ? <Skeleton aria-hidden="true" className="h-10" /> : null}
                {!rolesQuery.isPending && availableRoles.length === 0 ? (
                  <p className="text-sm text-[var(--color-content-muted)]">{t("form.rolesEmpty")}</p>
                ) : null}
                {availableRoles.map((role) => (
                  <CheckboxField
                    key={role.id}
                    label={role.name}
                    description={role.description ?? undefined}
                    checked={draft.roles.includes(role.name)}
                    onCheckedChange={(checked) => toggleRole(role.name, checked)}
                  />
                ))}
              </fieldset>
            ) : (
              <p className="text-sm text-[var(--color-content-muted)]">{t("form.rolesNeedPermission")}</p>
            )}

            {formError ? (
              <p role="alert" className="text-sm text-[var(--color-danger)]">
                {formError}
              </p>
            ) : null}

            <DialogFooter>
              <Button type="button" variant="outline" onClick={onClose}>
                {t("common:actions.cancel")}
              </Button>
              <Button type="submit" disabled={mutation.isPending}>
                {t("edit.submit")}
              </Button>
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}
```

- [ ] **Paso 11: la pantalla**

`src/features/users/pages/UsersPage.tsx`, completo:

```tsx
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { toast } from "sonner";
import {
  deleteUser,
  fetchUsers,
  setUserActive,
  usersQueryKey,
  usersQueryKeyRoot,
  type UserListItem,
} from "../api/users";
import { createUserColumns } from "../columns";
import { UserFormDialog } from "../components/UserFormDialog";
import { UserRolesDialog } from "../components/UserRolesDialog";
import { userActionErrorMessage } from "../errors";
import { Can } from "@/auth/Can";
import { useCurrentUser } from "@/auth/useCurrentUser";
import { usePermissions } from "@/auth/usePermissions";
import { ForbiddenPage } from "@/features/errors/pages/ForbiddenPage";
import { ApiError } from "@/shared/api/ApiError";
import { usePagination } from "@/shared/hooks/usePagination";
import { Button } from "@/shared/ui/button";
import { ConfirmDialog } from "@/shared/ui/ConfirmDialog";
import { DataTable } from "@/shared/ui/DataTable";
import { PageHeader } from "@/shared/ui/PageHeader";
import { Pagination } from "@/shared/ui/Pagination";
import { SearchInput } from "@/shared/ui/SearchInput";

/// Las dos acciones que no se hacen de una: antes pasan por el diálogo de confirmación.
interface PendingConfirmation {
  kind: "deactivate" | "delete";
  user: UserListItem;
}

/// El texto de "orden actual" junto al buscador (maqueta aprobada): el nombre de columna sale de las mismas
/// columnas que arma la tabla, para no duplicar traducciones por campo.
function sortDescription(
  t: (key: string, options?: Record<string, unknown>) => string,
  sort: string | undefined,
  columns: ReturnType<typeof createUserColumns>,
): string {
  if (!sort) {
    return t("sort.none");
  }

  const descending = sort.startsWith("-");
  const field = descending ? sort.slice(1) : sort;
  const fieldLabel = columns.find((column) => column.id === field)?.header ?? field;

  return t(descending ? "sort.descending" : "sort.ascending", { field: fieldLabel });
}

/// `/usuarios` (sección 7.4 del spec maestro y sección 11 del de la Fase 4): listado real contra `/api/users`,
/// con búsqueda, orden y paginado a cargo del backend, más el alta y las acciones por fila, todo en diálogos.
export function UsersPage() {
  const { t, i18n } = useTranslation("users");
  const { data: currentUser } = useCurrentUser();
  const { has } = usePermissions();
  const queryClient = useQueryClient();
  const { page, pageSize, sort, search, setPage, setSearch, toggleSort } = usePagination();

  const [isCreating, setIsCreating] = useState(false);
  const [editingUser, setEditingUser] = useState<UserListItem | undefined>();
  const [confirmation, setConfirmation] = useState<PendingConfirmation | undefined>();

  const canManage = has("users.manage");
  const query = { page, pageSize, sort, search };

  const { data, error, isLoading, refetch } = useQuery({
    queryKey: usersQueryKey(query),
    queryFn: () => fetchUsers(query),
    placeholderData: keepPreviousData,
  });

  // Las dos acciones de fila que no abren un formulario. El resultado va a un aviso y no a un cartel dentro
  // del diálogo, porque `ConfirmDialog` se cierra al confirmar: cuando llega la respuesta ya no está.
  const activation = useMutation({
    mutationFn: ({ user, isActive }: { user: UserListItem; isActive: boolean }) => setUserActive(user.id, isActive),
    onSuccess: async (_result, variables) => {
      toast.success(variables.isActive ? t("feedback.activated") : t("feedback.deactivated"));
      await queryClient.invalidateQueries({ queryKey: usersQueryKeyRoot });
    },
    onError: (mutationError) => toast.error(userActionErrorMessage(mutationError, t)),
  });

  const removal = useMutation({
    mutationFn: (user: UserListItem) => deleteUser(user.id),
    onSuccess: async () => {
      toast.success(t("feedback.deleted"));
      await queryClient.invalidateQueries({ queryKey: usersQueryKeyRoot });
    },
    onError: (mutationError) => toast.error(userActionErrorMessage(mutationError, t)),
  });

  const apiError = error instanceof ApiError ? error : undefined;

  const columns = createUserColumns(
    t,
    i18n.language,
    currentUser?.timeZoneId,
    canManage
      ? {
          onEditRoles: (user) => setEditingUser(user),
          // Activar no se confirma: no se pierde nada. Desactivar sí, porque le corta el acceso en el acto.
          onToggleActive: (user) =>
            user.isActive
              ? setConfirmation({ kind: "deactivate", user })
              : activation.mutate({ user, isActive: true }),
          onDelete: (user) => setConfirmation({ kind: "delete", user }),
        }
      : undefined,
  );

  // La pantalla pide el permiso para no entrar (experiencia de uso), pero quien decide es el backend: un 403
  // acá lleva a la misma pantalla de "sin permiso" que ProtectedRoute.
  if (apiError?.status === 403) {
    return <ForbiddenPage />;
  }

  const isDeletion = confirmation?.kind === "delete";

  return (
    <div>
      <PageHeader
        title={t("title")}
        description={t("description")}
        actions={
          <Can permission="users.manage">
            <Button type="button" onClick={() => setIsCreating(true)}>
              {t("actions.new")}
            </Button>
          </Can>
        }
      />

      <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
        <div className="w-full max-w-sm">
          <SearchInput value={search ?? ""} onChange={setSearch} label={t("searchLabel")} />
        </div>
        <p className="text-sm text-[var(--color-content-muted)]">{sortDescription(t, sort, columns)}</p>
      </div>

      <div className="rounded-[var(--radius-card)] border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
        <DataTable
          columns={columns}
          rows={data?.items ?? []}
          rowKey={(row) => row.id}
          isLoading={isLoading}
          error={apiError ? (apiError.isNetworkError ? t("common:errors.network") : (apiError.detail ?? apiError.message)) : undefined}
          errorDescription={apiError?.traceId ? t("errorTraceId", { traceId: apiError.traceId }) : undefined}
          onRetry={() => void refetch()}
          sort={sort}
          onSortChange={toggleSort}
          emptyTitle={t("empty.title")}
          emptyDescription={search ? t("empty.searchDescription") : t("empty.description")}
        />

        {data ? (
          <Pagination
            page={data.page}
            pageSize={data.pageSize}
            totalCount={data.totalCount}
            totalPages={data.totalPages}
            hasPrevious={data.hasPrevious}
            hasNext={data.hasNext}
            onPageChange={setPage}
          />
        ) : null}
      </div>

      {isCreating ? <UserFormDialog onClose={() => setIsCreating(false)} /> : null}

      {editingUser ? <UserRolesDialog user={editingUser} onClose={() => setEditingUser(undefined)} /> : null}

      {confirmation ? (
        <ConfirmDialog
          open
          onOpenChange={(next) => {
            if (!next) {
              setConfirmation(undefined);
            }
          }}
          title={t(isDeletion ? "delete.title" : "deactivate.title", { email: confirmation.user.email })}
          description={t(isDeletion ? "delete.description" : "deactivate.description")}
          confirmLabel={t(isDeletion ? "delete.confirm" : "deactivate.confirm")}
          destructive
          onConfirm={() => {
            if (isDeletion) {
              removal.mutate(confirmation.user);

              return;
            }

            activation.mutate({ user: confirmation.user, isActive: false });
          }}
        />
      ) : null}
    </div>
  );
}
```

- [ ] **Paso 12: verificación**

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBaseFront
npm run test
npm run build
npm run lint
```

Esperado: los 11 casos de `UsersPage` y los 3 de `CheckboxField` en verde, la paridad de traducciones también (`locales > has the same keys in both languages for users`), `npm run build` sin errores de tipos y `npm run lint` sin salida. Pegar los totales reales.

- [ ] **Paso 13: commit**

```bash
git add src/shared/ui/CheckboxField.tsx src/shared/ui/CheckboxField.test.tsx src/shared/api/roles.ts src/features/users src/locales/es/users.json src/locales/en/users.json
git commit -m "feat: administrar usuarios desde el listado" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 15: Pantalla de roles

Repo: **front**.

La ruta `/roles` estaba anotada y oculta en el menú desde la Fase 3. Esta tarea la construye: el listado con nombre, descripción y cuántos usuarios tiene cada rol, y el alta y la edición en un diálogo con los permisos como casillas agrupadas por área, que salen de `GET /api/permissions`. `Admin` y `User` se ven, pero no se editan ni se borran, y la pantalla dice por qué.

**Archivos:**
- Crear: `src/features/roles/api/roles.ts`
- Crear: `src/features/roles/errors.ts`
- Crear: `src/features/roles/components/RoleFormDialog.tsx`
- Crear: `src/features/roles/pages/RolesPage.tsx`
- Crear: `src/locales/es/roles.json`, `src/locales/en/roles.json`
- Modificar: `src/app/routes.tsx`
- Test: `src/features/roles/pages/RolesPage.test.tsx`

- [ ] **Paso 1: los textos, en los dos idiomas**

Crear `src/locales/es/roles.json`:

```json
{
  "title": "Roles y permisos",
  "description": "Qué puede hacer cada grupo de personas.",
  "columns": {
    "name": "Rol",
    "description": "Descripción",
    "userCount": "Usuarios",
    "permissions": "Permisos",
    "actions": "Acciones"
  },
  "systemBadge": "Del sistema",
  "systemLocked": "No se cambia",
  "systemNote": "Admin y User son del sistema: no se renombran, no se borran y a Admin no se le editan los permisos. Es la garantía de que siempre haya alguien que pueda arreglar cualquier cosa.",
  "permissionsCount": "{{count}} permisos",
  "empty": {
    "title": "Todavía no hay roles",
    "description": "Creá el primero para agrupar permisos."
  },
  "actions": {
    "new": "Nuevo rol",
    "edit": "Editar",
    "editFor": "Editar el rol {{name}}",
    "delete": "Eliminar",
    "deleteFor": "Eliminar el rol {{name}}"
  },
  "form": {
    "createTitle": "Nuevo rol",
    "editTitle": "Editar el rol {{name}}",
    "description": "Elegí qué puede hacer quien tenga este rol. Los cambios valen al instante.",
    "name": "Nombre",
    "nameRequired": "Poné un nombre.",
    "descriptionLabel": "Descripción",
    "descriptionHint": "Opcional. Una línea que explique para qué sirve.",
    "permissions": "Permisos",
    "submit": "Guardar"
  },
  "delete": {
    "title": "Eliminar el rol {{name}}",
    "description": "Quien lo tenga pierde los permisos que le daba. No se puede deshacer.",
    "confirm": "Eliminar"
  },
  "feedback": {
    "created": "Creamos el rol.",
    "updated": "Guardamos los cambios.",
    "deleted": "Eliminamos el rol."
  },
  "errors": {
    "alreadyExists": "Ya hay un rol con ese nombre.",
    "systemRole": "Los roles del sistema no se cambian."
  },
  "errorTraceId": "Código para reportar: {{traceId}}"
}
```

Crear `src/locales/en/roles.json`:

```json
{
  "title": "Roles and permissions",
  "description": "What each group of people can do.",
  "columns": {
    "name": "Role",
    "description": "Description",
    "userCount": "Users",
    "permissions": "Permissions",
    "actions": "Actions"
  },
  "systemBadge": "System role",
  "systemLocked": "Can't be changed",
  "systemNote": "Admin and User are system roles: they can't be renamed or deleted, and Admin's permissions can't be edited. That is what guarantees there is always someone who can fix anything.",
  "permissionsCount": "{{count}} permissions",
  "empty": {
    "title": "There are no roles yet",
    "description": "Create the first one to group permissions."
  },
  "actions": {
    "new": "New role",
    "edit": "Edit",
    "editFor": "Edit the {{name}} role",
    "delete": "Delete",
    "deleteFor": "Delete the {{name}} role"
  },
  "form": {
    "createTitle": "New role",
    "editTitle": "Edit the {{name}} role",
    "description": "Choose what someone with this role can do. Changes take effect right away.",
    "name": "Name",
    "nameRequired": "Enter a name.",
    "descriptionLabel": "Description",
    "descriptionHint": "Optional. One line explaining what it is for.",
    "permissions": "Permissions",
    "submit": "Save"
  },
  "delete": {
    "title": "Delete the {{name}} role",
    "description": "Whoever has it loses the permissions it granted. This can't be undone.",
    "confirm": "Delete"
  },
  "feedback": {
    "created": "The role was created.",
    "updated": "Changes saved.",
    "deleted": "The role was deleted."
  },
  "errors": {
    "alreadyExists": "There is already a role with that name.",
    "systemRole": "System roles can't be changed."
  },
  "errorTraceId": "Code to report: {{traceId}}"
}
```

- [ ] **Paso 2: las llamadas de roles y permisos**

Crear `src/features/roles/api/roles.ts`:

```ts
import { api } from "@/shared/api/httpClient";

/// Un permiso del catálogo (`GET /api/permissions`): el código estable y su nombre ya traducido por el backend.
export interface PermissionItem {
  readonly code: string;
  readonly name: string;
}

/// Los permisos agrupados por área (el prefijo del código: users, roles, settings), como los muestra el
/// diálogo de rol.
export interface PermissionGroup {
  readonly area: string;
  readonly name: string;
  readonly permissions: readonly PermissionItem[];
}

export interface RoleBody {
  readonly name: string;
  readonly description: string | null;
  readonly permissions: readonly string[];
}

export const permissionsQueryKey = ["permissions"] as const;

export function fetchPermissions(): Promise<readonly PermissionGroup[]> {
  return api.get<readonly PermissionGroup[]>("/api/permissions");
}

/// Devuelve el id del rol nuevo: el handler es `ICommand<Guid>` y `ToHttpResult` responde 200 con el valor.
export function createRole(body: RoleBody): Promise<string> {
  return api.post<string>("/api/roles", body);
}

export function updateRole(id: string, body: RoleBody): Promise<void> {
  return api.put<void>(`/api/roles/${id}`, body);
}

export function deleteRole(id: string): Promise<void> {
  return api.delete<void>(`/api/roles/${id}`);
}
```

El listado (`fetchRoles`, `rolesQueryKey`, `RoleListItem`) ya vive en `src/shared/api/roles.ts` desde la Tarea 14, porque lo piden dos módulos.

- [ ] **Paso 3: el mensaje de cada error**

Crear `src/features/roles/errors.ts`:

```ts
import { ApiError } from "@/shared/api/ApiError";

/// Firma mínima que necesitamos de `t`, la misma que usa `columns.tsx` en la feature de usuarios.
type Translate = (key: string, options?: Record<string, unknown>) => string;

/// El front decide por el `code`, que es estable, nunca por el texto. `Roles.HasUsers` es la excepción a
/// escribir el texto acá, y es a propósito: el backend dice **cuántos** usuarios tiene el rol, que es justo el
/// dato que hace falta para poder reasignarlos, y el front no lo sabe.
export function roleActionErrorMessage(error: unknown, t: Translate): string {
  if (!(error instanceof ApiError)) {
    return t("common:states.error");
  }

  if (error.isNetworkError) {
    return t("common:errors.network");
  }

  switch (error.code) {
    case "Roles.Role.AlreadyExists":
      return t("errors.alreadyExists");
    case "Roles.Role.SystemRoleCannotChange":
      return t("errors.systemRole");
    default:
      return error.detail ?? t("common:states.error");
  }
}
```

- [ ] **Paso 4: los tests de la pantalla (tienen que fallar)**

Crear `src/features/roles/pages/RolesPage.test.tsx`:

```tsx
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { HttpResponse, http } from "msw";
import { toast } from "sonner";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderRouteWithProviders } from "@/test/utils/renderWithProviders";
import { currentUser } from "@/test/mocks/handlers";
import { queryClient } from "@/shared/api/queryClient";
import { server } from "@/test/mocks/server";

vi.mock("react-oidc-context", async () => {
  const actual = await vi.importActual<typeof import("react-oidc-context")>("react-oidc-context");

  return { ...actual, useAuth: () => ({ isAuthenticated: true, isLoading: false, user: { access_token: "t" } }) };
});

const roles = [
  {
    id: "r1",
    name: "Admin",
    description: "Puede hacer todo.",
    isSystemRole: true,
    userCount: 1,
    permissions: ["users.read", "users.manage"],
  },
  {
    id: "r2",
    name: "Soporte",
    description: "Mira usuarios y nada más.",
    isSystemRole: false,
    userCount: 4,
    permissions: ["users.read"],
  },
];

const permissionGroups = [
  {
    area: "users",
    name: "Usuarios",
    permissions: [
      { code: "users.read", name: "Ver usuarios" },
      { code: "users.manage", name: "Administrar usuarios" },
    ],
  },
  {
    area: "settings",
    name: "Configuración",
    permissions: [{ code: "settings.manage", name: "Cambiar la configuración" }],
  },
];

const manager = { ...currentUser, permissions: ["roles.read", "roles.manage"] };

/// El arnés corre con `onUnhandledRequest: "error"`: cada test declara todo lo que su pantalla va a pedir.
function managerHandlers() {
  return [
    http.get("/api/me", () => HttpResponse.json(manager)),
    http.get("/api/roles", () => HttpResponse.json(roles)),
    http.get("/api/permissions", () => HttpResponse.json(permissionGroups)),
  ];
}

describe("RolesPage", () => {
  beforeEach(() => {
    queryClient.clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("shows each role with how many users it has", async () => {
    server.use(...managerHandlers());

    renderRouteWithProviders("/roles");

    const table = await screen.findByRole("table");
    expect(within(table).getByText("Soporte")).toBeInTheDocument();
    expect(within(table).getByText("Mira usuarios y nada más.")).toBeInTheDocument();
    expect(within(table).getByText("4")).toBeInTheDocument();
  });

  it("marks the system roles and does not offer to edit or delete them", async () => {
    server.use(...managerHandlers());

    renderRouteWithProviders("/roles");

    expect(await screen.findByText("Del sistema")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Editar el rol Admin" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Eliminar el rol Admin" })).not.toBeInTheDocument();
    // El de verdad editable sí está, para que el test no pase por no haberse dibujado nada.
    expect(screen.getByRole("button", { name: "Editar el rol Soporte" })).toBeInTheDocument();
  });

  it("creates a role with the permissions grouped by area", async () => {
    const created: unknown[] = [];
    server.use(
      ...managerHandlers(),
      http.post("/api/roles", async ({ request }) => {
        created.push(await request.json());

        return HttpResponse.json("0199a0c0-0000-7000-8000-000000000002");
      }),
    );

    renderRouteWithProviders("/roles");

    await userEvent.click(await screen.findByRole("button", { name: "Nuevo rol" }));

    const usersGroup = await screen.findByRole("group", { name: "Usuarios" });
    expect(within(usersGroup).getByRole("checkbox", { name: "Ver usuarios" })).toBeInTheDocument();

    await userEvent.type(screen.getByRole("textbox", { name: "Nombre" }), "Auditoría");
    await userEvent.click(within(usersGroup).getByRole("checkbox", { name: "Ver usuarios" }));
    await userEvent.click(screen.getByRole("button", { name: "Guardar" }));

    await waitFor(() =>
      expect(created).toEqual([{ name: "Auditoría", description: null, permissions: ["users.read"] }]),
    );
  });

  it("shows the backend's message, with the count, when the role still has users", async () => {
    const toastError = vi.spyOn(toast, "error");
    server.use(
      ...managerHandlers(),
      http.delete("/api/roles/r2", () =>
        HttpResponse.json(
          { status: 409, code: "Roles.Role.HasUsers", detail: "4 usuarios todavía tienen este rol." },
          { status: 409 },
        ),
      ),
    );

    renderRouteWithProviders("/roles");

    await userEvent.click(await screen.findByRole("button", { name: "Eliminar el rol Soporte" }));
    await userEvent.click(await screen.findByRole("button", { name: "Eliminar" }));

    // La excepción a decidir el texto por el código: solo el backend sabe cuántos son.
    await waitFor(() => expect(toastError).toHaveBeenCalledWith("4 usuarios todavía tienen este rol."));
  });

  it("hides the create and edit actions without roles.manage", async () => {
    server.use(
      http.get("/api/me", () => HttpResponse.json({ ...currentUser, permissions: ["roles.read"] })),
      http.get("/api/roles", () => HttpResponse.json(roles)),
    );

    renderRouteWithProviders("/roles");

    await screen.findByRole("table");

    expect(screen.queryByRole("button", { name: "Nuevo rol" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Editar el rol Soporte" })).not.toBeInTheDocument();
  });
});
```

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBaseFront
npm run test -- RolesPage
```

Tiene que fallar porque `/roles` todavía no es una ruta: el router cae en el comodín `*` y pinta la pantalla de "no encontramos esta página".

```
TestingLibraryElementError: Unable to find role="table"
```

- [ ] **Paso 5: el diálogo de rol**

Crear `src/features/roles/components/RoleFormDialog.tsx`:

```tsx
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { toast } from "sonner";
import { createRole, fetchPermissions, permissionsQueryKey, updateRole, type RoleBody } from "../api/roles";
import { roleActionErrorMessage } from "../errors";
import { currentUserQueryKey } from "@/auth/useCurrentUser";
import { rolesQueryKey, type RoleListItem } from "@/shared/api/roles";
import { Button } from "@/shared/ui/button";
import { CheckboxField } from "@/shared/ui/CheckboxField";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/shared/ui/dialog";
import { FormField } from "@/shared/ui/FormField";
import { Input } from "@/shared/ui/input";
import { Skeleton } from "@/shared/ui/skeleton";
import { Textarea } from "@/shared/ui/textarea";

/// Alta y edición de un rol. Sin `role` es un alta (`POST /api/roles`); con `role`, una edición
/// (`PUT /api/roles/{id}`). La pantalla lo monta solo mientras está abierto, así los campos arrancan con los
/// valores del rol que se está editando sin tener que copiarlos en un efecto.
export function RoleFormDialog({ role, onClose }: { role?: RoleListItem; onClose: () => void }) {
  const { t } = useTranslation("roles");
  const queryClient = useQueryClient();

  const [name, setName] = useState(role?.name ?? "");
  const [description, setDescription] = useState(role?.description ?? "");
  const [permissions, setPermissions] = useState<string[]>([...(role?.permissions ?? [])]);
  const [isNameMissing, setIsNameMissing] = useState(false);
  const [formError, setFormError] = useState<string | undefined>();

  const groupsQuery = useQuery({ queryKey: permissionsQueryKey, queryFn: fetchPermissions });

  const mutation = useMutation({
    mutationFn: async (body: RoleBody) => {
      if (role) {
        await updateRole(role.id, body);

        return;
      }

      await createRole(body);
    },
    onSuccess: async () => {
      toast.success(role ? t("feedback.updated") : t("feedback.created"));
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: rolesQueryKey }),
        // Cambiar los permisos de un rol puede cambiar los propios: el backend ya invalidó su caché.
        queryClient.invalidateQueries({ queryKey: currentUserQueryKey }),
      ]);
      onClose();
    },
    onError: (error) => setFormError(roleActionErrorMessage(error, t)),
  });

  const groups = groupsQuery.data ?? [];

  function togglePermission(code: string, checked: boolean) {
    setPermissions((current) => (checked ? [...current, code] : current.filter((permission) => permission !== code)));
  }

  return (
    <Dialog
      open
      onOpenChange={(next) => {
        if (!next) {
          onClose();
        }
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{role ? t("form.editTitle", { name: role.name }) : t("form.createTitle")}</DialogTitle>
          <DialogDescription>{t("form.description")}</DialogDescription>
        </DialogHeader>

        <form
          noValidate
          className="flex flex-col gap-4"
          onSubmit={(event) => {
            event.preventDefault();
            setFormError(undefined);

            const trimmedName = name.trim();

            if (trimmedName === "") {
              setIsNameMissing(true);

              return;
            }

            setIsNameMissing(false);
            mutation.mutate({ name: trimmedName, description: description.trim() || null, permissions });
          }}
        >
          <FormField label={t("form.name")} required error={isNameMissing ? t("form.nameRequired") : undefined}>
            <Input type="text" autoComplete="off" value={name} onChange={(event) => setName(event.target.value)} />
          </FormField>

          <FormField label={t("form.descriptionLabel")} hint={t("form.descriptionHint")}>
            <Textarea rows={2} value={description} onChange={(event) => setDescription(event.target.value)} />
          </FormField>

          <div className="flex max-h-64 flex-col gap-4 overflow-y-auto">
            {groupsQuery.isPending ? <Skeleton aria-hidden="true" className="h-24" /> : null}
            {groups.map((group) => (
              <fieldset key={group.area} className="flex flex-col gap-2">
                <legend className="mb-1 text-sm font-medium">{group.name}</legend>
                {group.permissions.map((permission) => (
                  <CheckboxField
                    key={permission.code}
                    label={permission.name}
                    checked={permissions.includes(permission.code)}
                    onCheckedChange={(checked) => togglePermission(permission.code, checked)}
                  />
                ))}
              </fieldset>
            ))}
          </div>

          {formError ? (
            <p role="alert" className="text-sm text-[var(--color-danger)]">
              {formError}
            </p>
          ) : null}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose}>
              {t("common:actions.cancel")}
            </Button>
            <Button type="submit" disabled={mutation.isPending}>
              {t("form.submit")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
```

- [ ] **Paso 6: la pantalla**

Crear `src/features/roles/pages/RolesPage.tsx`:

```tsx
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { toast } from "sonner";
import { deleteRole } from "../api/roles";
import { RoleFormDialog } from "../components/RoleFormDialog";
import { roleActionErrorMessage } from "../errors";
import { Can } from "@/auth/Can";
import { usePermissions } from "@/auth/usePermissions";
import { ForbiddenPage } from "@/features/errors/pages/ForbiddenPage";
import { ApiError } from "@/shared/api/ApiError";
import { fetchRoles, rolesQueryKey, type RoleListItem } from "@/shared/api/roles";
import { Badge } from "@/shared/ui/badge";
import { Button } from "@/shared/ui/button";
import { ConfirmDialog } from "@/shared/ui/ConfirmDialog";
import { DataTable, type Column } from "@/shared/ui/DataTable";
import { PageHeader } from "@/shared/ui/PageHeader";

/// `/roles` (sección 11 del spec de la Fase 4). El endpoint devuelve la lista entera, no una página: son
/// pocos y se usan como catálogo desde otras pantallas, así que no hay buscador ni paginado.
export function RolesPage() {
  const { t } = useTranslation("roles");
  const { has } = usePermissions();
  const queryClient = useQueryClient();

  const [isCreating, setIsCreating] = useState(false);
  const [editingRole, setEditingRole] = useState<RoleListItem | undefined>();
  const [deletingRole, setDeletingRole] = useState<RoleListItem | undefined>();

  const canManage = has("roles.manage");

  const { data, error, isLoading, refetch } = useQuery({ queryKey: rolesQueryKey, queryFn: fetchRoles });

  const removal = useMutation({
    mutationFn: (role: RoleListItem) => deleteRole(role.id),
    onSuccess: async () => {
      toast.success(t("feedback.deleted"));
      await queryClient.invalidateQueries({ queryKey: rolesQueryKey });
    },
    // `ConfirmDialog` se cierra al confirmar, así que el error llega cuando el diálogo ya no está: va a un aviso.
    onError: (mutationError) => toast.error(roleActionErrorMessage(mutationError, t)),
  });

  const apiError = error instanceof ApiError ? error : undefined;

  const columns: Column<RoleListItem>[] = [
    {
      id: "name",
      header: t("columns.name"),
      cell: (row) => (
        <span className="flex items-center gap-2">
          {row.name}
          {row.isSystemRole ? <Badge variant="secondary">{t("systemBadge")}</Badge> : null}
        </span>
      ),
    },
    { id: "description", header: t("columns.description"), cell: (row) => row.description ?? "—" },
    { id: "userCount", header: t("columns.userCount"), cell: (row) => String(row.userCount) },
    {
      id: "permissions",
      header: t("columns.permissions"),
      cell: (row) => t("permissionsCount", { count: row.permissions.length }),
    },
  ];

  if (canManage) {
    // Los roles del sistema se ven igual que el resto, pero en lugar de los botones queda dicho que no se
    // cambian, y debajo de la tabla, por qué. Botones deshabilitados no servirían: no reciben foco, así que
    // con el teclado no habría forma de llegar a la explicación.
    columns.push({
      id: "actions",
      header: t("columns.actions"),
      align: "right",
      cell: (row) =>
        row.isSystemRole ? (
          <span className="text-sm text-[var(--color-content-muted)]">{t("systemLocked")}</span>
        ) : (
          <div className="flex justify-end gap-1">
            <Button
              type="button"
              variant="ghost"
              size="sm"
              aria-label={t("actions.editFor", { name: row.name })}
              onClick={() => setEditingRole(row)}
            >
              {t("actions.edit")}
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="text-[var(--color-danger)]"
              aria-label={t("actions.deleteFor", { name: row.name })}
              onClick={() => setDeletingRole(row)}
            >
              {t("actions.delete")}
            </Button>
          </div>
        ),
    });
  }

  if (apiError?.status === 403) {
    return <ForbiddenPage />;
  }

  return (
    <div>
      <PageHeader
        title={t("title")}
        description={t("description")}
        actions={
          <Can permission="roles.manage">
            <Button type="button" onClick={() => setIsCreating(true)}>
              {t("actions.new")}
            </Button>
          </Can>
        }
      />

      <div className="rounded-[var(--radius-card)] border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
        <DataTable
          columns={columns}
          rows={data ?? []}
          rowKey={(row) => row.id}
          isLoading={isLoading}
          error={apiError ? (apiError.isNetworkError ? t("common:errors.network") : (apiError.detail ?? apiError.message)) : undefined}
          errorDescription={apiError?.traceId ? t("errorTraceId", { traceId: apiError.traceId }) : undefined}
          onRetry={() => void refetch()}
          emptyTitle={t("empty.title")}
          emptyDescription={t("empty.description")}
        />
      </div>

      <p className="mt-3 text-sm text-[var(--color-content-muted)]">{t("systemNote")}</p>

      {isCreating ? <RoleFormDialog onClose={() => setIsCreating(false)} /> : null}

      {editingRole ? <RoleFormDialog role={editingRole} onClose={() => setEditingRole(undefined)} /> : null}

      {deletingRole ? (
        <ConfirmDialog
          open
          onOpenChange={(next) => {
            if (!next) {
              setDeletingRole(undefined);
            }
          }}
          title={t("delete.title", { name: deletingRole.name })}
          description={t("delete.description")}
          confirmLabel={t("delete.confirm")}
          destructive
          onConfirm={() => removal.mutate(deletingRole)}
        />
      ) : null}
    </div>
  );
}
```

- [ ] **Paso 7: la ruta**

En `src/app/routes.tsx`, agregar la rama de `/roles` justo después de la de `/usuarios`, adentro de los `children` de `AppLayout`:

```tsx
              {
                element: <ProtectedRoute permission="roles.read" />,
                children: [
                  {
                    path: "/roles",
                    lazy: async () => ({ Component: (await import("@/features/roles/pages/RolesPage")).RolesPage }),
                  },
                ],
              },
```

- [ ] **Paso 8: verificación**

```bash
npm run test -- RolesPage
npm run test
npm run build
npm run lint
```

Esperado: los 5 casos de `RolesPage` en verde, la corrida completa también (incluida la paridad del namespace `roles`), build y lint limpios.

- [ ] **Paso 9: commit**

```bash
git add src/features/roles src/locales/es/roles.json src/locales/en/roles.json src/app/routes.tsx
git commit -m "feat: pantalla de roles y permisos" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 16: Pantalla de configuración y el menú

Repo: **front**.

El modo de registro decide quién puede entrar al sistema, y hasta ahora solo se podía cambiar tocando la base. Esta tarea le da su pantalla: un interruptor con las dos opciones explicadas y una confirmación que dice qué implica el cambio, no un "¿estás seguro?". Además, abre en el menú las dos entradas nuevas de Administración, filtradas por permiso.

**Archivos:**
- Crear: `src/features/settings/api/settings.ts`
- Crear: `src/features/settings/pages/SettingsPage.tsx`
- Crear: `src/locales/es/settings.json`, `src/locales/en/settings.json`
- Modificar: `src/shared/ui/icons.tsx`
- Modificar: `src/layouts/navigation.ts`
- Modificar: `src/layouts/components/Sidebar.tsx`
- Modificar: `src/locales/es/common.json`, `src/locales/en/common.json`
- Modificar: `src/app/routes.tsx`
- Test: `src/features/settings/pages/SettingsPage.test.tsx`
- Test: `src/layouts/components/Sidebar.test.tsx` (se le suma un caso)

- [ ] **Paso 1: comprobar cómo viaja el modo de registro**

`RegistrationMode` es un `enum` de C#. System.Text.Json, con los valores por defecto de la web, serializa los enums **como número** salvo que haya un `JsonStringEnumConverter` registrado, y tampoco acepta leerlos como texto. Hoy, en `src/ArquitecturaBase.Api/DependencyInjection.cs`, lo único que se registra es `UtcDateTimeConverter`. Esta pantalla habla de `"InviteOnly"` y `"Open"`, que es lo que corresponde al criterio de códigos estables del proyecto, así que primero hay que ver qué dejó la Tarea 4.

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBase
grep -rn "JsonStringEnumConverter" src/
grep -rn "registrationMode" tests/ArquitecturaBase.Api.IntegrationTests/
```

- **Si `JsonStringEnumConverter` aparece registrado en `Api/DependencyInjection.cs`:** está todo bien, seguí con el Paso 2.
- **Si no aparece:** el endpoint responde `{"registrationMode":0}` y rechaza `{"registrationMode":"Open"}`. Agregalo, en `ConfigureHttpJsonOptions`, junto al converter de fechas:

  ```csharp
          services.ConfigureHttpJsonOptions(options =>
          {
              options.SerializerOptions.Converters.Add(new UtcDateTimeConverter());
              // Los enums viajan por su nombre, no por su número: el número no dice nada del otro lado y
              // reordenar el enum cambiaría en silencio lo que significa cada valor guardado.
              options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
          });
  ```

  con `using System.Text.Json.Serialization;` arriba. Es seguro para el resto de la API: los únicos enums del backend son `LoginMethod`, `ErrorType` y `EmailDelivery`, y ninguno viaja en una respuesta HTTP.

  Después, si algún test de integración de la Tarea 4 afirma el número (por ejemplo `registrationMode":0`), cambialo por el nombre (`"InviteOnly"`), y corré:

  ```bash
  dotnet build ArquitecturaBase.slnx
  dotnet test
  ```

  Esperado: 0 advertencias y todo en verde. Ese cambio se commitea acá mismo, en el commit del backend del Paso 11.

- [ ] **Paso 2: los textos, en los dos idiomas**

Crear `src/locales/es/settings.json`:

```json
{
  "title": "Configuración",
  "description": "Los ajustes del sistema.",
  "registration": {
    "title": "Quién puede entrar",
    "switchLabel": "Registro abierto",
    "open": "Abierto: cualquiera que ingrese con un correo nuevo se crea la cuenta sola, con el rol User.",
    "inviteOnly": "Solo por invitación: únicamente entra quien un administrador dio de alta. A los demás no les llega ningún código."
  },
  "confirm": {
    "openTitle": "Abrir el registro",
    "openDescription": "Desde que confirmes, cualquier persona con un correo válido va a poder crearse una cuenta y entrar sin que vos la des de alta. Podés volver atrás cuando quieras.",
    "inviteOnlyTitle": "Cerrar el registro",
    "inviteOnlyDescription": "Desde que confirmes, solo entra quien vos des de alta. Los que ya tienen cuenta siguen entrando igual: para sacar a alguien, desactivá su cuenta.",
    "confirm": "Cambiar"
  },
  "feedback": {
    "saved": "Guardamos el cambio."
  },
  "errorTraceId": "Código para reportar: {{traceId}}"
}
```

Crear `src/locales/en/settings.json`:

```json
{
  "title": "Settings",
  "description": "The system settings.",
  "registration": {
    "title": "Who can sign in",
    "switchLabel": "Open registration",
    "open": "Open: anyone signing in with a new email gets an account of their own, with the User role.",
    "inviteOnly": "Invite only: only people an administrator added can sign in. Nobody else gets a code."
  },
  "confirm": {
    "openTitle": "Open registration",
    "openDescription": "From the moment you confirm, anyone with a valid email will be able to create an account and sign in without you adding them. You can switch back whenever you want.",
    "inviteOnlyTitle": "Close registration",
    "inviteOnlyDescription": "From the moment you confirm, only people you add can sign in. Everyone who already has an account keeps signing in: to lock someone out, deactivate their account.",
    "confirm": "Change"
  },
  "feedback": {
    "saved": "The change was saved."
  },
  "errorTraceId": "Code to report: {{traceId}}"
}
```

Y las dos entradas nuevas del menú. En `src/locales/es/common.json`, el bloque `navigation` pasa a:

```json
  "navigation": {
    "general": "General",
    "administration": "Administración",
    "dashboard": "Inicio",
    "users": "Usuarios",
    "roles": "Roles y permisos",
    "settings": "Configuración"
  },
```

En `src/locales/en/common.json`:

```json
  "navigation": {
    "general": "General",
    "administration": "Administration",
    "dashboard": "Home",
    "users": "Users",
    "roles": "Roles and permissions",
    "settings": "Settings"
  },
```

- [ ] **Paso 3: los tests de la pantalla y del menú (tienen que fallar)**

Crear `src/features/settings/pages/SettingsPage.test.tsx`:

```tsx
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { HttpResponse, http } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderRouteWithProviders } from "@/test/utils/renderWithProviders";
import { currentUser } from "@/test/mocks/handlers";
import { queryClient } from "@/shared/api/queryClient";
import { server } from "@/test/mocks/server";

vi.mock("react-oidc-context", async () => {
  const actual = await vi.importActual<typeof import("react-oidc-context")>("react-oidc-context");

  return { ...actual, useAuth: () => ({ isAuthenticated: true, isLoading: false, user: { access_token: "t" } }) };
});

const admin = { ...currentUser, permissions: ["settings.manage"] };

describe("SettingsPage", () => {
  beforeEach(() => {
    queryClient.clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("shows which mode the system is in", async () => {
    server.use(
      http.get("/api/me", () => HttpResponse.json(admin)),
      http.get("/api/settings", () => HttpResponse.json({ registrationMode: "InviteOnly" })),
    );

    renderRouteWithProviders("/configuracion");

    expect(await screen.findByRole("switch", { name: "Registro abierto" })).not.toBeChecked();
    expect(screen.getByText(/solo por invitación/i)).toBeInTheDocument();
  });

  it("explains what changes before opening the registration, and saves it when confirmed", async () => {
    const saved: unknown[] = [];
    server.use(
      http.get("/api/me", () => HttpResponse.json(admin)),
      http.get("/api/settings", () => HttpResponse.json({ registrationMode: "InviteOnly" })),
      http.put("/api/settings", async ({ request }) => {
        saved.push(await request.json());

        return new HttpResponse(null, { status: 204 });
      }),
    );

    renderRouteWithProviders("/configuracion");

    await userEvent.click(await screen.findByRole("switch", { name: "Registro abierto" }));

    expect(await screen.findByText(/sin que vos la des de alta/i)).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Cambiar" }));

    await waitFor(() => expect(saved).toEqual([{ registrationMode: "Open" }]));
  });

  it("changes nothing when the confirmation is cancelled", async () => {
    let puts = 0;
    server.use(
      http.get("/api/me", () => HttpResponse.json(admin)),
      http.get("/api/settings", () => HttpResponse.json({ registrationMode: "Open" })),
      http.put("/api/settings", () => {
        puts += 1;

        return new HttpResponse(null, { status: 204 });
      }),
    );

    renderRouteWithProviders("/configuracion");

    const toggle = await screen.findByRole("switch", { name: "Registro abierto" });
    expect(toggle).toBeChecked();

    await userEvent.click(toggle);
    await userEvent.click(await screen.findByRole("button", { name: "Cancelar" }));

    expect(puts).toBe(0);
    expect(screen.getByRole("switch", { name: "Registro abierto" })).toBeChecked();
  });

  it("sends whoever lacks settings.manage to the forbidden page", async () => {
    // El handler por defecto de /api/me devuelve permissions: ["users.read"].
    renderRouteWithProviders("/configuracion");

    expect(await screen.findByRole("heading", { name: /no tenés permiso/i })).toBeInTheDocument();
  });
});
```

Y en `src/layouts/components/Sidebar.test.tsx`, agregar al final del `describe`:

```tsx
  it("shows Roles and Configuración only to whoever has their permissions", async () => {
    server.use(
      http.get("/api/me", () => HttpResponse.json({ ...currentUser, permissions: ["roles.read", "settings.manage"] })),
    );

    renderRouteWithProviders("/");

    expect(await screen.findByRole("link", { name: /roles y permisos/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /configuración/i })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /usuarios/i })).not.toBeInTheDocument();
  });
```

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBaseFront
npm run test -- SettingsPage Sidebar
```

Tienen que fallar los cuatro casos nuevos de `SettingsPage` (la ruta `/configuracion` todavía no existe, así que aparece la pantalla de "no encontramos esta página") y el nuevo de `Sidebar` (`Unable to find role="link" and name /roles y permisos/i`: la entrada sigue marcada como oculta y la de configuración no existe). Los tres casos viejos de `Sidebar` tienen que seguir en verde.

- [ ] **Paso 4: las llamadas de configuración**

Crear `src/features/settings/api/settings.ts`:

```ts
import { api } from "@/shared/api/httpClient";

/// Quién puede crear una cuenta (sección 4 del spec de la Fase 4). Viaja por su nombre, no por su número.
export type RegistrationMode = "InviteOnly" | "Open";

export interface SystemSettings {
  readonly registrationMode: RegistrationMode;
}

export const systemSettingsQueryKey = ["system-settings"] as const;

export function fetchSystemSettings(): Promise<SystemSettings> {
  return api.get<SystemSettings>("/api/settings");
}

export function updateSystemSettings(body: SystemSettings): Promise<void> {
  return api.put<void>("/api/settings", body);
}
```

- [ ] **Paso 5: la pantalla**

Crear `src/features/settings/pages/SettingsPage.tsx`:

```tsx
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { toast } from "sonner";
import {
  fetchSystemSettings,
  systemSettingsQueryKey,
  updateSystemSettings,
  type RegistrationMode,
} from "../api/settings";
import { ForbiddenPage } from "@/features/errors/pages/ForbiddenPage";
import { ApiError } from "@/shared/api/ApiError";
import { Button } from "@/shared/ui/button";
import { ConfirmDialog } from "@/shared/ui/ConfirmDialog";
import { EmptyState } from "@/shared/ui/EmptyState";
import { Label } from "@/shared/ui/label";
import { PageHeader } from "@/shared/ui/PageHeader";
import { Skeleton } from "@/shared/ui/skeleton";
import { Switch } from "@/shared/ui/switch";

/// Firma mínima que necesitamos de `t`, la misma que usan las otras features.
type Translate = (key: string, options?: Record<string, unknown>) => string;

/// Acá no hay códigos que merezcan un texto propio: los errores de negocio ya vienen traducidos del servidor.
function errorMessage(error: unknown, t: Translate): string {
  if (!(error instanceof ApiError)) {
    return t("common:states.error");
  }

  if (error.isNetworkError) {
    return t("common:errors.network");
  }

  return error.detail ?? t("common:states.error");
}

/// `/configuracion` (sección 11 del spec de la Fase 4). Es un ajuste que decide quién puede entrar al sistema,
/// así que el cambio pasa por una confirmación que dice qué implica, no por un "¿estás seguro?".
export function SettingsPage() {
  const { t } = useTranslation("settings");
  const queryClient = useQueryClient();
  const [pendingMode, setPendingMode] = useState<RegistrationMode | undefined>();

  const { data, error, refetch } = useQuery({ queryKey: systemSettingsQueryKey, queryFn: fetchSystemSettings });

  const mutation = useMutation({
    mutationFn: (registrationMode: RegistrationMode) => updateSystemSettings({ registrationMode }),
    onSuccess: async () => {
      toast.success(t("feedback.saved"));
      await queryClient.invalidateQueries({ queryKey: systemSettingsQueryKey });
    },
    onError: (mutationError) => toast.error(errorMessage(mutationError, t)),
  });

  const apiError = error instanceof ApiError ? error : undefined;

  if (apiError?.status === 403) {
    return <ForbiddenPage />;
  }

  const isOpeningRegistration = pendingMode === "Open";

  const body = apiError ? (
    <EmptyState
      title={apiError.isNetworkError ? t("common:errors.network") : (apiError.detail ?? apiError.message)}
      description={apiError.traceId ? t("errorTraceId", { traceId: apiError.traceId }) : undefined}
      action={
        <Button type="button" variant="outline" onClick={() => void refetch()}>
          {t("common:actions.retry")}
        </Button>
      }
    />
  ) : data ? (
    <>
      <div className="mt-4 flex items-center gap-3">
        <Switch
          id="registration-open"
          checked={data.registrationMode === "Open"}
          disabled={mutation.isPending}
          onCheckedChange={(checked) => setPendingMode(checked ? "Open" : "InviteOnly")}
        />
        <Label htmlFor="registration-open">{t("registration.switchLabel")}</Label>
      </div>

      {/* Las dos opciones explicadas, siempre las dos: el interruptor solo no dice qué pasa del otro lado. */}
      <ul className="mt-3 flex flex-col gap-1 text-sm text-[var(--color-content-muted)]">
        <li>{t("registration.open")}</li>
        <li>{t("registration.inviteOnly")}</li>
      </ul>
    </>
  ) : (
    <Skeleton aria-hidden="true" className="mt-4 h-10" />
  );

  return (
    <div>
      <PageHeader title={t("title")} description={t("description")} />

      <section className="rounded-[var(--radius-card)] border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
        <h2 className="text-sm font-semibold text-[var(--color-content)]">{t("registration.title")}</h2>
        {body}
      </section>

      {pendingMode ? (
        <ConfirmDialog
          open
          onOpenChange={(next) => {
            if (!next) {
              setPendingMode(undefined);
            }
          }}
          title={t(isOpeningRegistration ? "confirm.openTitle" : "confirm.inviteOnlyTitle")}
          description={t(isOpeningRegistration ? "confirm.openDescription" : "confirm.inviteOnlyDescription")}
          confirmLabel={t("confirm.confirm")}
          onConfirm={() => mutation.mutate(pendingMode)}
        />
      ) : null}
    </div>
  );
}
```

- [ ] **Paso 6: el ícono del menú**

En `src/shared/ui/icons.tsx`, agregar al final (un engranaje simplificado: el círculo del centro y ocho radios, con el mismo trazo que el resto del set):

```tsx
export function SettingsIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <circle cx="12" cy="12" r="3.25" />
      <path d="M12 2.75v2" />
      <path d="M12 19.25v2" />
      <path d="M21.25 12h-2" />
      <path d="M4.75 12h-2" />
      <path d="m18.55 5.45-1.4 1.4" />
      <path d="m6.85 17.15-1.4 1.4" />
      <path d="m18.55 18.55-1.4-1.4" />
      <path d="m6.85 6.85-1.4-1.4" />
    </Icon>
  );
}
```

- [ ] **Paso 7: el menú**

`src/layouts/navigation.ts`, completo. Con la pantalla de roles hecha, `hidden` se queda sin usos: sale del modelo y del filtro de la barra lateral, en vez de quedar como un campo muerto.

```ts
import type { ComponentType } from "react";
import { HomeIcon, SettingsIcon, ShieldIcon, UsersIcon } from "@/shared/ui/icons";

export interface NavigationItem {
  /// Clave del texto en el namespace common (navigation.*).
  labelKey: string;
  to: string;
  icon: ComponentType<{ className?: string }>;
  /// Si está, el ítem se muestra solo a quien tenga el permiso.
  permission?: string;
}

export interface NavigationGroup {
  labelKey: string;
  items: NavigationItem[];
}

export const navigation: NavigationGroup[] = [
  { labelKey: "navigation.general", items: [{ labelKey: "navigation.dashboard", to: "/", icon: HomeIcon }] },
  {
    labelKey: "navigation.administration",
    items: [
      { labelKey: "navigation.users", to: "/usuarios", icon: UsersIcon, permission: "users.read" },
      { labelKey: "navigation.roles", to: "/roles", icon: ShieldIcon, permission: "roles.read" },
      { labelKey: "navigation.settings", to: "/configuracion", icon: SettingsIcon, permission: "settings.manage" },
    ],
  },
];
```

En `src/layouts/components/Sidebar.tsx`, adentro del `navigation.map`, la línea del filtro pasa de:

```tsx
            const items = group.items.filter((item) => !item.hidden);
```

a:

```tsx
            const items = group.items;
```

- [ ] **Paso 8: la ruta**

En `src/app/routes.tsx`, agregar la rama de `/configuracion` después de la de `/roles`:

```tsx
              {
                element: <ProtectedRoute permission="settings.manage" />,
                children: [
                  {
                    path: "/configuracion",
                    lazy: async () => ({
                      Component: (await import("@/features/settings/pages/SettingsPage")).SettingsPage,
                    }),
                  },
                ],
              },
```

- [ ] **Paso 9: verificación**

```bash
npm run test -- SettingsPage Sidebar
npm run test
npm run build
npm run lint
```

Esperado: los 4 casos de `SettingsPage` y los 4 de `Sidebar` en verde, la corrida completa también (incluida la paridad del namespace `settings` y las claves nuevas de `common`), build y lint limpios.

- [ ] **Paso 10: commit del front**

```bash
git add src/features/settings src/locales/es/settings.json src/locales/en/settings.json src/locales/es/common.json src/locales/en/common.json src/shared/ui/icons.tsx src/layouts/navigation.ts src/layouts/components/Sidebar.tsx src/layouts/components/Sidebar.test.tsx src/app/routes.tsx
git commit -m "feat: pantalla de configuracion y las entradas del menu" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Paso 11: commit del backend (solo si el Paso 1 lo tocó)**

Si en el Paso 1 hubo que registrar `JsonStringEnumConverter`:

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBase
git add src/ArquitecturaBase.Api/DependencyInjection.cs
git commit -m "fix: los enums de la Api viajan por su nombre" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

Si además hubo que ajustar algún test de integración de la Tarea 4, agregá su archivo al mismo `git add`, nombrándolo explícitamente.

---
### Tarea 17: Perfil propio

Repo: **front**.

La Tarea 13 dejó `PUT /api/me` y sumó `lastLoginAtUtc` a `GET /api/me`, y nadie los llama: el idioma sigue viviendo solo en `localStorage`, "Mi perfil" sigue deshabilitado en el menú del usuario y el último ingreso no se muestra en ningún lado. Esta tarea cierra las tres cosas.

Va como **pantalla `/perfil`** y no como diálogo: el perfil no es una acción sobre un listado (que es lo que cubre la regla de los diálogos), sino una pantalla propia, con lugar para crecer, y como ruta mantiene las capas donde están (`layouts` no importa de una feature) y se carga `lazy` como todas las demás. Además, el menú del usuario es un `DropdownMenu` de Radix: un `Dialog` abierto desde adentro pelea por el foco cuando el menú se cierra, y un `Link` no.

**Archivos:**
- Crear: `src/shared/lib/dateTime.ts`
- Crear: `src/shared/api/profile.ts`
- Crear: `src/auth/useLanguagePreference.ts`
- Crear: `src/features/profile/pages/ProfilePage.tsx`
- Crear: `src/locales/es/profile.json`, `src/locales/en/profile.json`
- Modificar: `src/shared/i18n/index.ts`
- Modificar: `src/auth/useCurrentUser.ts`
- Modificar: `src/layouts/AppLayout.tsx`
- Modificar: `src/layouts/components/UserMenu.tsx`
- Modificar: `src/layouts/components/Breadcrumbs.tsx`
- Modificar: `src/features/home/pages/DashboardPage.tsx`
- Modificar: `src/features/users/columns.tsx`
- Modificar: `src/locales/es/common.json`, `src/locales/en/common.json`
- Modificar: `src/app/routes.tsx`
- Modificar: `CLAUDE.md`, `README.md`
- Test: `src/shared/lib/dateTime.test.ts` (crear)
- Test: `src/features/home/pages/DashboardPage.test.tsx` (crear)
- Test: `src/features/profile/pages/ProfilePage.test.tsx` (crear)
- Test: `src/layouts/components/UserMenu.test.tsx` (se rehace entero)

- [ ] **Paso 1: comprobar contra qué backend se está trabajando**

Esta tarea consume dos cosas que dejó la Tarea 13. Antes de escribir nada, ver que estén y con qué nombres:

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBase
cat src/ArquitecturaBase.Application/Features/Users/GetCurrentUser/CurrentUserResponse.cs
grep -n "MapPut\|UpdateProfileCommand" src/ArquitecturaBase.Api/Endpoints/Users/MeEndpoint.cs
cat src/ArquitecturaBase.Application/Features/Users/UpdateProfile/UpdateProfileCommandValidator.cs
```

Esperado:

- `CurrentUserResponse` tiene un campo `LastLoginAtUtc` (que en JSON sale como `lastLoginAtUtc`, y puede venir `null`);
- `MeEndpoint` mapea un `PUT /api/me` que recibe `UpdateProfileCommand(DisplayName, Culture, TimeZoneId)` y solo pide bearer, sin permiso;
- el validador dice cuánto puede medir `DisplayName` y contra qué se valida `Culture` y `TimeZoneId`.

Dos desvíos posibles:

- **Si `PUT /api/me` no existe**, la Tarea 13 no se ejecutó: frenar y avisar. Esta tarea no tiene contra qué correr.
- **Si el campo del último ingreso se llama distinto** (no `LastLoginAtUtc`), usar ese nombre en `useCurrentUser.ts` y en el test del tablero, y anotarlo como desvío.

El front manda **siempre los tres campos** con el valor que tienen que quedar, sin importar cuál se tocó: el comando reemplaza el perfil, no lo parchea, así que mandar solo el idioma le borraría el nombre y la zona horaria a la persona (mismo criterio que el `PUT /api/users/{id}` de la Tarea 14).

- [ ] **Paso 2: los textos, en los dos idiomas**

Crear `src/locales/es/profile.json`:

```json
{
  "title": "Mi perfil",
  "description": "Tus datos y cómo querés ver el sistema.",
  "form": {
    "email": "Correo electrónico",
    "emailHint": "No se cambia: es con el que ingresás.",
    "displayName": "Nombre",
    "displayNameHint": "Es el nombre que se muestra en el sistema.",
    "language": "Idioma",
    "languageHint": "Queda guardado en tu cuenta: entres desde donde entres, el sistema te habla en este idioma.",
    "timeZone": "Zona horaria",
    "timeZoneHint": "Con esto mostramos las fechas y las horas en tu hora local.",
    "submit": "Guardar"
  },
  "feedback": {
    "saved": "Guardamos tu perfil."
  }
}
```

Crear `src/locales/en/profile.json`:

```json
{
  "title": "My profile",
  "description": "Your details and how you want to see the system.",
  "form": {
    "email": "Email address",
    "emailHint": "It can't be changed: it's the one you sign in with.",
    "displayName": "Name",
    "displayNameHint": "The name shown across the system.",
    "language": "Language",
    "languageHint": "It's saved in your account: wherever you sign in from, the system speaks to you in this language.",
    "timeZone": "Time zone",
    "timeZoneHint": "We use it to show dates and times in your local time.",
    "submit": "Save"
  },
  "feedback": {
    "saved": "Your profile was saved."
  }
}
```

En `src/locales/es/common.json`, el bloque `home.session` pasa a:

```json
    "session": {
      "title": "Tu sesión",
      "email": "Correo",
      "role": "Rol",
      "lastLogin": "Último ingreso"
    },
```

y la línea de `language` (hoy está en una sola línea) pasa a:

```json
  "language": {
    "label": "Idioma",
    "es": "Español",
    "en": "Inglés",
    "saveFailed": "No pudimos guardar el idioma en tu perfil. Probá de nuevo."
  },
```

En `src/locales/en/common.json`, lo mismo:

```json
    "session": {
      "title": "Your session",
      "email": "Email",
      "role": "Role",
      "lastLogin": "Last sign-in"
    },
```

```json
  "language": {
    "label": "Language",
    "es": "Spanish",
    "en": "English",
    "saveFailed": "We couldn't save the language in your profile. Try again."
  },
```

`saveFailed` va en `common` y no en `profile` a propósito: lo usa el menú del usuario, que se dibuja en toda pantalla con sesión y pide solo el namespace `common`. En `profile` tendría que esperar a que ese namespace se cargue, y hasta entonces mostraría la clave cruda.

- [ ] **Paso 3: el test del formateador de fechas (tiene que fallar)**

La fecha del último ingreso se muestra igual que las del listado de usuarios: en UTC desde el backend, en la zona horaria del perfil en la pantalla. Hoy eso lo hace una función privada de `features/users/columns.tsx`, y ahora lo necesitan dos módulos, así que sube a `shared`.

Crear `src/shared/lib/dateTime.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { formatDateTimeInZone } from "./dateTime";

describe("formatDateTimeInZone", () => {
  it("shows a UTC instant in the profile's time zone", () => {
    // 12:00 UTC son las 9:00 en Buenos Aires (UTC-3). El texto es el que arma ICU para "es".
    expect(formatDateTimeInZone("2026-09-18T12:00:00Z", "es", "America/Argentina/Buenos_Aires")).toBe(
      "18 sept 2026, 9:00",
    );
  });

  it("uses the time zone even when it changes the day", () => {
    // Lo que se está probando: que la zona se aplique de verdad. En Madrid ese instante ya es del día siguiente.
    expect(formatDateTimeInZone("2026-09-19T23:30:00Z", "es", "Europe/Madrid")).toBe("20 sept 2026, 1:30");
  });
});
```

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBaseFront
npm run test -- dateTime
```

Tiene que fallar por no existir el módulo: `Error: Failed to resolve import "./dateTime" from "src/shared/lib/dateTime.test.ts"`.

- [ ] **Paso 4: el formateador compartido, y un solo formateador en todo el front**

Crear `src/shared/lib/dateTime.ts`:

```ts
/// El backend manda las fechas en UTC con `Z` (sección 6.3 del spec maestro) y la pantalla las muestra en la
/// zona horaria del perfil, nunca la cadena cruda. Es el único formateador de fecha y hora del front: si
/// aparece otro, las mismas fechas se van a ver distinto en dos pantallas.
///
/// `timeZone: undefined` deja la del navegador, que es lo correcto mientras el perfil todavía no llegó.
export function formatDateTimeInZone(valueUtc: string, language: string, timeZone: string | undefined): string {
  return new Intl.DateTimeFormat(language, { dateStyle: "medium", timeStyle: "short", timeZone }).format(
    new Date(valueUtc),
  );
}
```

Y en `src/features/users/columns.tsx`, tres cambios:

1. agregar `import { formatDateTimeInZone } from "@/shared/lib/dateTime";` arriba de la línea `import { Badge } from "@/shared/ui/badge";`;
2. borrar la función `formatCreatedAt` entera, con su comentario `/// La fecha llega en UTC (sección 6.3 del spec)…`;
3. en la celda de la columna `createdAtUtc`, `formatCreatedAt(row.createdAtUtc, language, timeZone)` pasa a `formatDateTimeInZone(row.createdAtUtc, language, timeZone)`.

```bash
npm run test -- dateTime UsersPage
```

Esperado: los 2 casos de `dateTime` en verde y los de `UsersPage` como estaban (el formateo de la columna no cambió, solo de dónde sale la función).

- [ ] **Paso 5: el último ingreso en el tipo de la sesión, y la llamada que guarda el perfil**

`src/auth/useCurrentUser.ts`, completo:

```ts
import { useQuery } from "@tanstack/react-query";
import { useAuth } from "react-oidc-context";
import { api } from "@/shared/api/httpClient";

/// Perfil, roles y permisos del usuario de la sesión (sección 5.6 del spec).
export interface CurrentUser {
  readonly id: string;
  readonly email: string;
  readonly displayName: string | null;
  readonly culture: string;
  readonly timeZoneId: string;
  /// Último ingreso, en UTC (sección 9 del spec de la Fase 4). Viene `null` si todavía no hay ninguno guardado.
  readonly lastLoginAtUtc: string | null;
  readonly roles: readonly string[];
  readonly permissions: readonly string[];
}

export const currentUserQueryKey = ["current-user"] as const;

export function useCurrentUser() {
  const auth = useAuth();

  return useQuery({
    queryKey: currentUserQueryKey,
    queryFn: () => api.get<CurrentUser>("/api/me"),
    enabled: auth.isAuthenticated,
    staleTime: 5 * 60_000,
  });
}
```

Crear `src/shared/api/profile.ts`:

```ts
import { api } from "./httpClient";

/// Lo que `PUT /api/me` deja cambiar del perfil propio (sección 9 del spec de la Fase 4).
///
/// Los tres campos viajan siempre, con el valor que tienen que quedar: el comando **reemplaza** el perfil, no
/// lo parchea, así que mandar solo el idioma le borraría el nombre y la zona horaria a la persona.
export interface UpdateProfileBody {
  readonly displayName: string | null;
  readonly culture: string;
  readonly timeZoneId: string;
}

/// Vive en `shared/api` y no en `features/profile` porque lo piden dos lugares: la pantalla del perfil y el
/// cambio de idioma del menú del usuario, que está en `layouts` y no puede importar de una feature.
export function updateProfile(body: UpdateProfileBody): Promise<void> {
  return api.put<void>("/api/me", body);
}
```

Y `src/shared/i18n/index.ts`, completo (se le suma el guardia de tipo y el comentario que deja claro qué nivel es cada cosa):

```ts
import i18n from "i18next";
import resourcesToBackend from "i18next-resources-to-backend";
import { initReactI18next } from "react-i18next";

export const supportedLanguages = ["es", "en"] as const;

export type SupportedLanguage = (typeof supportedLanguages)[number];

export const languageStorageKey = "arquitecturabase.language";

/// Un valor cualquiera (lo guardado en el navegador, lo que trae el perfil, lo que eligió un `<select>`) es
/// uno de los idiomas que hay.
export function isSupportedLanguage(value: string | null | undefined): value is SupportedLanguage {
  return supportedLanguages.includes(value as SupportedLanguage);
}

function initialLanguage(): SupportedLanguage {
  const stored = globalThis.localStorage?.getItem(languageStorageKey);

  return isSupportedLanguage(stored) ? stored : "es";
}

// Cada módulo tiene su archivo por idioma y se carga cuando alguien lo pide con useTranslation("modulo").
await i18n
  .use(resourcesToBackend((language: string, namespace: string) => import(`../../locales/${language}/${namespace}.json`)))
  .use(initReactI18next)
  .init({
    lng: initialLanguage(),
    fallbackLng: "es",
    supportedLngs: [...supportedLanguages],
    ns: ["common"],
    defaultNS: "common",
    interpolation: { escapeValue: false },
    react: { useSuspense: true },
  });

/// Cambia el idioma de la interfaz y lo recuerda en este navegador. Es el nivel de abajo, y lo llaman dos: la
/// pantalla de ingreso (donde todavía no hay cuenta a la que guardárselo) y `useLanguagePreference`, que
/// además lo persiste en el perfil. **Desde una pantalla con sesión no se llama a esto directo:** el idioma
/// tiene que quedar guardado en la cuenta, no solo en esta máquina.
export function changeLanguage(language: SupportedLanguage): Promise<unknown> {
  globalThis.localStorage?.setItem(languageStorageKey, language);

  return i18n.changeLanguage(language);
}

export default i18n;
```

- [ ] **Paso 6: los tests del tablero (tienen que fallar)**

Crear `src/features/home/pages/DashboardPage.test.tsx`:

```tsx
import { screen } from "@testing-library/react";
import { HttpResponse, http } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { queryClient } from "@/shared/api/queryClient";
import { changeLanguage, languageStorageKey } from "@/shared/i18n";
import { currentUser } from "@/test/mocks/handlers";
import { server } from "@/test/mocks/server";
import { renderRouteWithProviders } from "@/test/utils/renderWithProviders";

vi.mock("react-oidc-context", async () => {
  const actual = await vi.importActual<typeof import("react-oidc-context")>("react-oidc-context");

  return { ...actual, useAuth: () => ({ isAuthenticated: true, isLoading: false, user: { access_token: "t" } }) };
});

describe("DashboardPage", () => {
  // AppProviders usa el queryClient de la app (un singleton, con staleTime): sin esto, el perfil de un test
  // se filtra al siguiente (mismo patrón que Sidebar.test.tsx y UsersPage.test.tsx).
  beforeEach(() => {
    queryClient.clear();
  });

  afterEach(async () => {
    // i18next también es un módulo compartido: el test que cambia el idioma tiene que devolverlo.
    await changeLanguage("es");
  });

  it("shows the last sign-in in the profile's time zone", async () => {
    server.use(http.get("/api/me", () => HttpResponse.json({ ...currentUser, lastLoginAtUtc: "2026-09-18T12:00:00Z" })));

    renderRouteWithProviders("/");

    // El perfil de prueba está en America/Argentina/Buenos_Aires: 12:00 UTC son las 9:00.
    expect(await screen.findByText("18 sept 2026, 9:00")).toBeInTheDocument();
  });

  it("shows a dash when there is no last sign-in yet", async () => {
    server.use(http.get("/api/me", () => HttpResponse.json({ ...currentUser, lastLoginAtUtc: null })));

    renderRouteWithProviders("/");

    // Es el único "—" de la pantalla: el otro valor que podría faltar es el rol, y el perfil de prueba tiene.
    expect(await screen.findByText("—")).toBeInTheDocument();
  });

  it("applies the language saved in the account over the one kept in this browser", async () => {
    // i18next arrancó en español (lo que dice este navegador) y la cuenta dice inglés: manda la cuenta.
    server.use(http.get("/api/me", () => HttpResponse.json({ ...currentUser, culture: "en" })));

    renderRouteWithProviders("/");

    expect(await screen.findByRole("heading", { name: "Home" })).toBeInTheDocument();
    // Y queda anotado en el navegador, que es lo que va a leer la pantalla de ingreso la próxima vez.
    expect(globalThis.localStorage.getItem(languageStorageKey)).toBe("en");
  });
});
```

```bash
npm run test -- DashboardPage
```

Tienen que fallar los tres, cada uno por lo suyo:

- `Unable to find an element with the text: 18 sept 2026, 9:00` (la tarjeta no tiene esa fila);
- `Unable to find an element with the text: —`;
- `Unable to find an accessible element with the role "heading" and name "Home"` (la pantalla sigue en español).

- [ ] **Paso 7: el idioma de la cuenta manda**

Crear `src/auth/useLanguagePreference.ts`:

```tsx
import { useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import { useTranslation } from "react-i18next";
import { toast } from "sonner";
import { currentUserQueryKey, useCurrentUser } from "./useCurrentUser";
import { updateProfile } from "@/shared/api/profile";
import i18n, { changeLanguage, isSupportedLanguage, type SupportedLanguage } from "@/shared/i18n";

/// Aplica en la interfaz el idioma que tiene guardado la cuenta, apenas llega el perfil (sección 9 del spec de
/// la Fase 4). Está montado en `AppLayout`, así que vale para toda pantalla con sesión, y es el **único**
/// lugar del front donde el idioma de la cuenta se aplica.
///
/// **Cuando el idioma de la cuenta y el de este navegador difieren, gana el de la cuenta.** Es el que la
/// persona guardó a propósito y el que la sigue a cualquier máquina; el de `localStorage` es apenas lo último
/// que se eligió en esta. Desde esta tarea, todo cambio hecho con la sesión abierta se guarda en la cuenta,
/// así que si los dos valores no coinciden es porque el de la cuenta se cambió en otro navegador, o porque el
/// de acá se eligió antes de ingresar, en la pantalla de ingreso, que es la única que sigue guardando solo en
/// el navegador.
export function useProfileLanguageSync(): void {
  const { data: user } = useCurrentUser();
  const culture = user?.culture;

  useEffect(() => {
    // Depende solo del `culture` que trae el perfil, a propósito. Si dependiera también del idioma actual,
    // volvería a pisarlo apenas alguien lo cambia desde el menú, antes de que termine el guardado.
    // `i18n.language` se lee del módulo (no de `useTranslation`) para no suscribir a este hook a cada cambio.
    if (!isSupportedLanguage(culture) || culture === i18n.language) {
      return;
    }

    void changeLanguage(culture);
  }, [culture]);
}

/// Cambiar el idioma desde una pantalla con sesión: se aplica en la interfaz y se guarda en la cuenta.
///
/// No es un `useMutation` porque no es solo una llamada al servidor: primero toca un sistema externo
/// (i18next) y, si el guardado falla, lo tiene que devolver atrás. Lo que no puede pasar, de ninguna de las
/// tres formas, es que la interfaz y la cuenta queden diciendo cosas distintas.
export function useLanguagePreference(): { change: (language: SupportedLanguage) => Promise<void> } {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const { data: user } = useCurrentUser();

  async function change(language: SupportedLanguage): Promise<void> {
    const previous = isSupportedLanguage(i18n.language) ? i18n.language : "es";

    if (language === previous) {
      return;
    }

    // La interfaz cambia ya: es lo único que la persona pidió ver, y hacerla esperar una ida y vuelta sería
    // peor que el riesgo de tener que volver atrás.
    await changeLanguage(language);

    // Sin perfil no hay dónde guardarlo (todavía no llegó, o no hay sesión): vale para este navegador y listo.
    if (!user) {
      return;
    }

    try {
      await updateProfile({ displayName: user.displayName, culture: language, timeZoneId: user.timeZoneId });
      // El perfil lo usa medio front (el menú, la barra lateral, las fechas de los listados) y además es de
      // donde `useProfileLanguageSync` toma el idioma: si esto no se invalida, la caché queda diciendo el
      // idioma viejo y lo reaplica en el próximo montaje.
      await queryClient.invalidateQueries({ queryKey: currentUserQueryKey });
    } catch {
      await changeLanguage(previous);
      toast.error(t("language.saveFailed"));
    }
  }

  return { change };
}
```

En `src/layouts/AppLayout.tsx`, agregar el import debajo del de `signOutStatus`:

```tsx
import { useProfileLanguageSync } from "@/auth/useLanguagePreference";
```

y, en el cuerpo del componente, justo después de `const isSigningOut = useIsSigningOut();`:

```tsx
  // El idioma guardado en la cuenta manda sobre el de este navegador, y acá es donde se aplica: apenas llega
  // el perfil, y para toda pantalla con sesión (sección 9 del spec de la Fase 4).
  useProfileLanguageSync();
```

Queda antes del `if (isSigningOut)`, como todos los hooks.

- [ ] **Paso 8: el último ingreso en la tarjeta "Tu sesión"**

`src/features/home/pages/DashboardPage.tsx`, completo:

```tsx
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useCurrentUser } from "@/auth/useCurrentUser";
import { formatDateTimeInZone } from "@/shared/lib/dateTime";
import { PageHeader } from "@/shared/ui/PageHeader";
import { Badge } from "@/shared/ui/badge";
import { Skeleton } from "@/shared/ui/skeleton";

const featureKeys = ["passwordless", "permissions", "i18n", "listings", "dates"] as const;

/// Tarjeta blanca (misma forma que la usa `UsersPage`): un título y su contenido.
function Card({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="rounded-[var(--radius-card)] border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
      <h2 className="mb-3 text-sm font-semibold text-[var(--color-content)]">{title}</h2>
      {children}
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between gap-3 py-1 text-sm">
      <span className="text-[var(--color-content-muted)]">{label}</span>
      <span className="truncate font-medium text-[var(--color-content)]">{value}</span>
    </div>
  );
}

/// Una fila todavía sin datos: ocupa el mismo alto que `Row` (py-1 + una línea de text-sm) para que la
/// tarjeta no cambie de tamaño cuando llegan.
function RowSkeleton({ label }: { label: string }) {
  return (
    <div className="flex items-center justify-between gap-3 py-1 text-sm">
      <span className="text-[var(--color-content-muted)]">{label}</span>
      <div className="flex h-5 items-center">
        <Skeleton aria-hidden="true" className="h-3.5 w-28" />
      </div>
    </div>
  );
}

/// Los permisos del perfil, o el aviso de que no tiene ninguno.
function PermissionList({ permissions, emptyLabel }: { permissions: readonly string[]; emptyLabel: string }) {
  if (permissions.length === 0) {
    return <p className="text-sm text-[var(--color-content-muted)]">{emptyLabel}</p>;
  }

  return (
    <div className="flex flex-wrap gap-1.5">
      {permissions.map((permission) => (
        <Badge key={permission} variant="outline">
          {permission}
        </Badge>
      ))}
    </div>
  );
}

/// El lugar de los permisos mientras no llegan: unas etiquetas del alto exacto de un `Badge`.
function PermissionListSkeleton() {
  return (
    <div className="flex flex-wrap gap-1.5">
      <Skeleton aria-hidden="true" className="h-[22px] w-28" />
      <Skeleton aria-hidden="true" className="h-[22px] w-20" />
      <Skeleton aria-hidden="true" className="h-[22px] w-24" />
    </div>
  );
}

/// `/` (sección 7.2): el tablero de inicio. Nada de datos inventados, solo el perfil que ya devuelve
/// `/api/me` (roles, permisos, idioma, zona horaria y último ingreso).
export function DashboardPage() {
  const { t, i18n } = useTranslation();
  // `isPending` es "el perfil todavía no llegó" (al recargar, mientras se recupera la sesión). La pantalla se
  // dibuja igual y solo los datos que faltan van como bloques de carga, en vez de quedar en blanco.
  const { data: user, isPending } = useCurrentUser();

  // Si `/api/me` falló no hay nada que mostrar acá, como hasta ahora: del error se ocupa quien lo pidió.
  if (!user && !isPending) {
    return null;
  }

  return (
    <div>
      <PageHeader title={t("home.title")} description={t("home.description")} />

      <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
        <Card title={t("home.session.title")}>
          {user ? (
            <>
              <Row label={t("home.session.email")} value={user.email} />
              <Row label={t("home.session.role")} value={user.roles.length > 0 ? user.roles.join(", ") : "—"} />
              {/* Llega en UTC y se muestra en la zona horaria del perfil, igual que las fechas del listado de
                  usuarios. Puede venir `null`: ahí no hay nada que formatear. */}
              <Row
                label={t("home.session.lastLogin")}
                value={
                  user.lastLoginAtUtc ? formatDateTimeInZone(user.lastLoginAtUtc, i18n.language, user.timeZoneId) : "—"
                }
              />
            </>
          ) : (
            <>
              <RowSkeleton label={t("home.session.email")} />
              <RowSkeleton label={t("home.session.role")} />
              <RowSkeleton label={t("home.session.lastLogin")} />
            </>
          )}
        </Card>

        <Card title={t("home.permissions.title")}>
          {user ? <PermissionList permissions={user.permissions} emptyLabel={t("home.permissions.empty")} /> : <PermissionListSkeleton />}
        </Card>

        <Card title={t("home.locale.title")}>
          {user ? (
            <>
              <Row label={t("home.locale.language")} value={t(`language.${user.culture}`)} />
              <Row label={t("home.locale.timeZone")} value={user.timeZoneId} />
            </>
          ) : (
            <>
              <RowSkeleton label={t("home.locale.language")} />
              <RowSkeleton label={t("home.locale.timeZone")} />
            </>
          )}
        </Card>
      </div>

      <div className="mt-4 rounded-[var(--radius-card)] border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
        <h2 className="mb-3 text-sm font-semibold text-[var(--color-content)]">{t("home.features.title")}</h2>
        <ul className="list-disc space-y-1 pl-5 text-sm text-[var(--color-content-muted)]">
          {featureKeys.map((key) => (
            <li key={key}>{t(`home.features.${key}`)}</li>
          ))}
        </ul>
      </div>
    </div>
  );
}
```

```bash
npm run test -- DashboardPage
```

Esperado: `Test Files 1 passed (1)`, `Tests 3 passed (3)`.

- [ ] **Paso 9: los tests del menú del usuario (tienen que fallar)**

`src/layouts/components/UserMenu.test.tsx`, completo (los tres casos que ya había quedan, con el envoltorio nuevo; se suman tres):

```tsx
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { HttpResponse, http } from "msw";
import { MemoryRouter } from "react-router";
import { toast } from "sonner";
import { afterEach, describe, expect, it, vi } from "vitest";
import { UserMenu } from "./UserMenu";
import i18n, { changeLanguage, languageStorageKey } from "@/shared/i18n";
import { server } from "@/test/mocks/server";
import { renderWithProviders } from "@/test/utils/renderWithProviders";

const signoutRedirect = vi.fn();

vi.mock("react-oidc-context", async () => {
  const actual = await vi.importActual<typeof import("react-oidc-context")>("react-oidc-context");

  return {
    ...actual,
    useAuth: () => ({ isAuthenticated: true, isLoading: false, user: { access_token: "t" }, signoutRedirect }),
  };
});

// El menú ahora tiene un enlace a /perfil, y un Link necesita un router alrededor. Alcanza con uno de memoria:
// esto sigue siendo la prueba del menú, no la de la app entera.
function renderMenu() {
  return renderWithProviders(
    <MemoryRouter>
      <UserMenu />
    </MemoryRouter>,
  );
}

// Abrimos el menú con teclado (Enter sobre el trigger enfocado) en vez de con userEvent.click: es lo que pide
// la sección de accesibilidad (navegable con teclado) y evita una descoordinación de userEvent con el
// pointerdown de Radix cuando se abre más de un DropdownMenu en el mismo archivo de test.
async function openMenu() {
  const trigger = await screen.findByRole("button", { name: /ana/i });
  trigger.focus();
  await userEvent.keyboard("{Enter}");

  return trigger;
}

describe("UserMenu", () => {
  afterEach(async () => {
    signoutRedirect.mockClear();
    vi.restoreAllMocks();
    await changeLanguage("es");
  });

  it("shows the user's name and email", async () => {
    renderMenu();

    await openMenu();

    expect(await screen.findByText("ana@example.com")).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /cerrar sesión/i })).toBeInTheDocument();
  });

  it("links to the profile screen", async () => {
    renderMenu();

    await openMenu();

    expect(await screen.findByRole("menuitem", { name: "Mi perfil" })).toHaveAttribute("href", "/perfil");
  });

  it("changes the language and saves it in the profile", async () => {
    const saved: unknown[] = [];
    server.use(
      http.put("/api/me", async ({ request }) => {
        saved.push(await request.json());

        return new HttpResponse(null, { status: 204 });
      }),
    );

    renderMenu();

    await openMenu();
    // "language.en" en español es "Inglés" (el nombre del idioma, no el gentilicio en ese idioma).
    await userEvent.click(await screen.findByRole("menuitemradio", { name: /inglés/i }));

    // Las dos cosas, no una sola: la interfaz cambia y la cuenta queda guardada. El perfil viaja entero
    // porque el PUT lo reemplaza.
    await waitFor(() => expect(i18n.language).toBe("en"));
    await waitFor(() =>
      expect(saved).toEqual([{ displayName: "Ana", culture: "en", timeZoneId: "America/Argentina/Buenos_Aires" }]),
    );
  });

  it("goes back to the previous language when the profile can't be saved", async () => {
    const toastError = vi.spyOn(toast, "error");
    server.use(
      http.put("/api/me", () =>
        HttpResponse.json(
          { status: 500, code: "General.Unexpected", detail: "Ocurrió un error inesperado." },
          { status: 500 },
        ),
      ),
    );

    renderMenu();

    await openMenu();
    await userEvent.click(await screen.findByRole("menuitemradio", { name: /inglés/i }));

    // Primero hay que esperar a que el guardado haya fallado: afirmar el idioma antes pasaría igual, sin haber
    // probado nada, porque arranca en "es".
    await waitFor(() =>
      expect(toastError).toHaveBeenCalledWith("No pudimos guardar el idioma en tu perfil. Probá de nuevo."),
    );
    expect(i18n.language).toBe("es");
    expect(globalThis.localStorage.getItem(languageStorageKey)).toBe("es");
  });

  it("clears the session and calls signoutRedirect when signing out", async () => {
    renderMenu();

    await openMenu();

    await userEvent.click(await screen.findByRole("menuitem", { name: /cerrar sesión/i }));

    expect(signoutRedirect).toHaveBeenCalledTimes(1);
  });
});
```

```bash
npm run test -- UserMenu
```

Tienen que fallar los tres casos nuevos:

- `links to the profile screen`: `expected element to have attribute href="/perfil"` — el ítem todavía es un `div` deshabilitado, no un enlace;
- `changes the language and saves it in the profile`: el `waitFor` del cuerpo se agota (`expected [] to deeply equal [ { displayName: 'Ana', … } ]`), porque el menú cambia el idioma solo en el navegador;
- `goes back to the previous language…`: el `waitFor` del aviso se agota, porque no hay nada que pueda fallar.

Los tres viejos tienen que seguir en verde.

- [ ] **Paso 10: el menú del usuario**

`src/layouts/components/UserMenu.tsx`, completo:

```tsx
import { useTranslation } from "react-i18next";
import { useAuth } from "react-oidc-context";
import { Link } from "react-router";
import { beginSignOut, cancelSignOut } from "@/auth/signOutStatus";
import { useCurrentUser } from "@/auth/useCurrentUser";
import { useLanguagePreference } from "@/auth/useLanguagePreference";
import { isSupportedLanguage, supportedLanguages } from "@/shared/i18n";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/shared/ui/dropdown-menu";
import { ChevronDownIcon, LogOutIcon } from "@/shared/ui/icons";
import { Skeleton } from "@/shared/ui/skeleton";

function initialOf(name: string): string {
  return name.trim().charAt(0).toUpperCase() || "?";
}

/// Menú del usuario, en el avatar de la barra superior (sección 7.2): datos de la sesión, el acceso a su
/// perfil, el idioma y cerrar sesión.
///
/// El idioma se cambia acá porque es un atajo que se usa seguido, y desde la Fase 4 **también se guarda en la
/// cuenta**: lo hace `useLanguagePreference`, que lo aplica en el acto y lo devuelve atrás si el guardado
/// falla. El mismo idioma se puede cambiar desde `/perfil`, junto con el resto del perfil.
export function UserMenu() {
  const { t, i18n } = useTranslation();
  const auth = useAuth();
  const { data: user, isPending } = useCurrentUser();
  const { change: changeLanguage } = useLanguagePreference();

  if (!user) {
    // El menú necesita el nombre para poder nombrarse, así que hasta que llega no hay menú: queda el bloque
    // de carga del avatar, del mismo tamaño, para que después no aparezca de golpe.
    return isPending ? <Skeleton aria-hidden="true" className="size-8 shrink-0 rounded-full" /> : null;
  }

  const displayName = user.displayName ?? user.email;

  async function handleSignOut() {
    // AppLayout se entera por acá y muestra una transición en vez del layout con los datos ya vacíos
    // (entre que esto limpia la sesión en memoria y auth.signoutRedirect navega a /connect/logout).
    beginSignOut();

    try {
      await auth.signoutRedirect();
    } catch {
      // Si no se pudo ni empezar el cierre de sesión, seguimos en esta pantalla: no puede quedar trabada
      // mostrando la transición.
      cancelSignOut();
    }
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger className="flex shrink-0 items-center gap-1.5 rounded-full outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50">
        <span
          aria-hidden="true"
          className="flex size-8 items-center justify-center rounded-full bg-[var(--color-brand-600)] text-sm font-semibold text-white"
        >
          {initialOf(displayName)}
        </span>
        <span className="sr-only">{t("layout.userMenu.trigger", { name: displayName })}</span>
        <ChevronDownIcon className="size-4 text-[var(--color-content-muted)]" />
      </DropdownMenuTrigger>

      <DropdownMenuContent align="end" className="w-64">
        <DropdownMenuLabel className="font-normal">
          <p className="truncate text-sm font-medium text-[var(--color-content)]">{displayName}</p>
          <p className="truncate text-xs font-normal text-[var(--color-content-muted)]">{user.email}</p>
        </DropdownMenuLabel>

        <DropdownMenuSeparator />

        {/* asChild: el ítem del menú es el propio enlace, así navega con un clic o con Enter y conserva su
            rol de menuitem. */}
        <DropdownMenuItem asChild>
          <Link to="/perfil">{t("layout.userMenu.profile")}</Link>
        </DropdownMenuItem>

        <DropdownMenuSeparator />

        <DropdownMenuLabel className="text-xs font-normal tracking-wide text-[var(--color-content-muted)] uppercase">
          {t("language.label")}
        </DropdownMenuLabel>
        <DropdownMenuRadioGroup
          value={i18n.language}
          onValueChange={(value) => {
            // El grupo informa un string cualquiera; acá solo hay idiomas de los que existen.
            if (isSupportedLanguage(value)) {
              void changeLanguage(value);
            }
          }}
        >
          {supportedLanguages.map((language) => (
            <DropdownMenuRadioItem key={language} value={language}>
              {t(`language.${language}`)}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>

        <DropdownMenuSeparator />

        <DropdownMenuItem onSelect={() => void handleSignOut()}>
          <LogOutIcon className="size-4" />
          {t("layout.userMenu.signOut")}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
```

```bash
npm run test -- UserMenu
```

Esperado: `Test Files 1 passed (1)`, `Tests 6 passed (6)`.

- [ ] **Paso 11: los tests de la pantalla del perfil (tienen que fallar)**

Crear `src/features/profile/pages/ProfilePage.test.tsx`:

```tsx
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { HttpResponse, http } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { queryClient } from "@/shared/api/queryClient";
import { changeLanguage } from "@/shared/i18n";
import { currentUser } from "@/test/mocks/handlers";
import { server } from "@/test/mocks/server";
import { renderRouteWithProviders } from "@/test/utils/renderWithProviders";

vi.mock("react-oidc-context", async () => {
  const actual = await vi.importActual<typeof import("react-oidc-context")>("react-oidc-context");

  return { ...actual, useAuth: () => ({ isAuthenticated: true, isLoading: false, user: { access_token: "t" } }) };
});

interface ProfileBody {
  displayName: string | null;
  culture: string;
  timeZoneId: string;
}

/// Un `/api/me` que el PUT modifica de verdad: es lo que hace que el idioma recién guardado llegue a la
/// interfaz por el mismo camino que en la app (invalidar la consulta → volver a pedirla → aplicarlo).
function profileHandlers(saved: ProfileBody[]) {
  let profile = { ...currentUser };

  return [
    http.get("/api/me", () => HttpResponse.json(profile)),
    http.put("/api/me", async ({ request }) => {
      const body = (await request.json()) as ProfileBody;
      saved.push(body);
      profile = { ...profile, ...body };

      return new HttpResponse(null, { status: 204 });
    }),
  ];
}

describe("ProfilePage", () => {
  beforeEach(() => {
    queryClient.clear();
  });

  afterEach(async () => {
    await changeLanguage("es");
  });

  it("saves the name, the language and the time zone together", async () => {
    const saved: ProfileBody[] = [];
    server.use(...profileHandlers(saved));

    renderRouteWithProviders("/perfil");

    const name = await screen.findByRole("textbox", { name: "Nombre" });

    // El perfil no está en el menú lateral, pero las migas tienen que decir dónde está parada la persona.
    const breadcrumbs = screen.getByRole("navigation", { name: "Migas de pan" });
    expect(within(breadcrumbs).getByText("Mi perfil")).toBeInTheDocument();

    await userEvent.clear(name);
    await userEvent.type(name, "Ana María");
    await userEvent.selectOptions(screen.getByRole("combobox", { name: "Zona horaria" }), "America/Montevideo");
    await userEvent.click(screen.getByRole("button", { name: "Guardar" }));

    // Los tres campos viajan siempre, aunque se haya tocado uno: el PUT reemplaza el perfil.
    await waitFor(() =>
      expect(saved).toEqual([{ displayName: "Ana María", culture: "es", timeZoneId: "America/Montevideo" }]),
    );
  });

  it("shows the time zone of the profile even if the browser spells it another way", async () => {
    server.use(...profileHandlers([]));

    renderRouteWithProviders("/perfil");

    // El navegador canoniza esa zona como "America/Buenos_Aires". Si la lista fueran solo las suyas, el
    // desplegable arrancaría sin nada elegido y guardar le cambiaría la zona horaria a la persona sin avisar.
    expect(await screen.findByRole("combobox", { name: "Zona horaria" })).toHaveValue(
      "America/Argentina/Buenos_Aires",
    );
  });

  it("switches the interface to the language it just saved", async () => {
    server.use(...profileHandlers([]));

    renderRouteWithProviders("/perfil");

    await userEvent.selectOptions(await screen.findByRole("combobox", { name: "Idioma" }), "en");
    await userEvent.click(screen.getByRole("button", { name: "Guardar" }));

    // Sin recargar nada: el perfil guardado vuelve a la caché y de ahí lo toma `useProfileLanguageSync`.
    expect(await screen.findByRole("heading", { name: "My profile" })).toBeInTheDocument();
  });

  it("shows the message of the field the backend rejected, without losing what was typed", async () => {
    server.use(
      http.get("/api/me", () => HttpResponse.json(currentUser)),
      http.put("/api/me", () =>
        HttpResponse.json(
          {
            status: 400,
            code: "Validation.Failed",
            detail: "Revisá los datos ingresados.",
            errors: { displayName: ["El nombre no puede tener más de 128 caracteres."] },
          },
          { status: 400 },
        ),
      ),
    );

    renderRouteWithProviders("/perfil");

    const name = await screen.findByRole("textbox", { name: "Nombre" });
    await userEvent.clear(name);
    await userEvent.type(name, "Ana Larga");
    await userEvent.click(screen.getByRole("button", { name: "Guardar" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("El nombre no puede tener más de 128 caracteres.");
    // Lo escrito sigue ahí: el formulario no se recarga del perfil después de un error.
    expect(screen.getByRole("textbox", { name: "Nombre" })).toHaveValue("Ana Larga");
  });
});
```

```bash
npm run test -- ProfilePage
```

Tienen que fallar los cuatro, y por la misma razón: la ruta `/perfil` todavía no existe, así que se ve la pantalla de "No encontramos esta página". El primero:

```
TestingLibraryElementError: Unable to find an accessible element with the role "textbox" and name "Nombre"
```

- [ ] **Paso 12: la pantalla, la ruta y las migas de pan**

Crear `src/features/profile/pages/ProfilePage.tsx`:

```tsx
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { toast } from "sonner";
import { currentUserQueryKey, useCurrentUser } from "@/auth/useCurrentUser";
import { ApiError } from "@/shared/api/ApiError";
import { updateProfile } from "@/shared/api/profile";
import { isSupportedLanguage, supportedLanguages, type SupportedLanguage } from "@/shared/i18n";
import { Button } from "@/shared/ui/button";
import { FormField } from "@/shared/ui/FormField";
import { Input } from "@/shared/ui/input";
import { PageHeader } from "@/shared/ui/PageHeader";
import { Skeleton } from "@/shared/ui/skeleton";

interface Draft {
  displayName: string;
  culture: SupportedLanguage;
  timeZoneId: string;
}

/// Los desplegables son `<select>` nativos y no el `Select` de shadcn: la lista de zonas horarias pasa las
/// cuatrocientas opciones, y el nativo trae gratis la búsqueda por teclado del sistema operativo y el
/// selector de rueda del teléfono, que es lo que hace usable una lista así. Las clases son las del `Input`
/// (`shared/ui/input.tsx`) para que los dos controles del formulario se vean igual.
const selectClassName =
  "h-9 w-full min-w-0 rounded-md border border-input bg-transparent px-3 py-1 text-base shadow-xs transition-[color,box-shadow] outline-none disabled:cursor-not-allowed disabled:opacity-50 md:text-sm dark:bg-input/30 focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 aria-invalid:border-destructive aria-invalid:ring-destructive/20 dark:aria-invalid:ring-destructive/40";

/// Las zonas que conoce el navegador (ya vienen ordenadas), más la que tiene guardada el perfil si no está
/// entre ellas. `Intl.supportedValuesOf` devuelve los nombres canónicos de IANA y el backend puede tener
/// guardado un alias: el seed usa `America/Argentina/Buenos_Aires`, que el navegador llama
/// `America/Buenos_Aires`. Sin este agregado, el desplegable arrancaría sin ninguna opción elegida y guardar
/// le cambiaría la zona horaria a la persona sin que lo haya pedido.
function timeZoneOptions(current: string | undefined): string[] {
  const zones = Intl.supportedValuesOf("timeZone");

  return current && !zones.includes(current) ? [current, ...zones].sort() : zones;
}

/// `/perfil` (sección 9 del spec de la Fase 4): nombre, idioma y zona horaria de la propia cuenta. No pide
/// permiso, solo sesión: cualquiera edita el suyo. Se entra desde "Mi perfil", en el menú del usuario.
export function ProfilePage() {
  const { t } = useTranslation("profile");
  const { data: user, isPending } = useCurrentUser();
  const queryClient = useQueryClient();

  const [draft, setDraft] = useState<Draft | undefined>();
  const [loadedUserId, setLoadedUserId] = useState<string | undefined>();

  const mutation = useMutation({
    mutationFn: (values: Draft) =>
      updateProfile({
        displayName: values.displayName.trim() || null,
        culture: values.culture,
        timeZoneId: values.timeZoneId,
      }),
    onSuccess: async () => {
      toast.success(t("feedback.saved"));
      // El perfil lo usa medio front (el menú, la barra lateral, las fechas de los listados). Y si cambió el
      // idioma, es esta consulta la que lo aplica: `useProfileLanguageSync` mira el `culture` que llegue acá.
      // Por eso la pantalla no toca i18next por su cuenta: el idioma de la cuenta se aplica en un solo lugar.
      await queryClient.invalidateQueries({ queryKey: currentUserQueryKey });
    },
  });

  // Los valores del formulario salen del perfil, y se ajustan durante el render en vez de copiarlos con un
  // efecto. Mientras el id no cambie no se vuelven a pisar: lo que esté escrito a medias es de la persona
  // (mismo criterio que `UserRolesDialog`).
  if (user && loadedUserId !== user.id) {
    setLoadedUserId(user.id);
    setDraft({
      displayName: user.displayName ?? "",
      // Si la cuenta tuviera un idioma que el front no sabe hablar, el desplegable no lo podría mostrar.
      culture: isSupportedLanguage(user.culture) ? user.culture : "es",
      timeZoneId: user.timeZoneId,
    });
  }

  // La lista no cambia mientras la pantalla está abierta: sin el memo se volvería a armar en cada tecla que
  // se escribe en el nombre.
  const timeZones = useMemo(() => timeZoneOptions(user?.timeZoneId), [user?.timeZoneId]);

  if (!user || !draft) {
    return (
      <div>
        <PageHeader title={t("title")} description={t("description")} />
        {/* Mientras el perfil no llegó, el formulario ocupa su lugar. Si `/api/me` falló no hay nada que
            editar: de ese error se ocupa el aviso global del queryClient, igual que en el tablero. */}
        {isPending ? (
          <div className="flex max-w-lg flex-col gap-4 rounded-[var(--radius-card)] border border-[var(--color-border)] bg-[var(--color-surface)] p-4">
            <Skeleton aria-hidden="true" className="h-14" />
            <Skeleton aria-hidden="true" className="h-14" />
            <Skeleton aria-hidden="true" className="h-14" />
          </div>
        ) : null}
      </div>
    );
  }

  const apiError = mutation.error instanceof ApiError ? mutation.error : undefined;
  const fieldErrors = apiError?.errors;
  // Lo que el backend no le atribuyó a ningún campo va arriba del botón; lo que sí, debajo de su campo.
  const formError =
    mutation.isError && !fieldErrors
      ? apiError?.isNetworkError
        ? t("common:errors.network")
        : (apiError?.detail ?? t("common:states.error"))
      : undefined;

  return (
    <div>
      <PageHeader title={t("title")} description={t("description")} />

      <form
        noValidate
        className="flex max-w-lg flex-col gap-4 rounded-[var(--radius-card)] border border-[var(--color-border)] bg-[var(--color-surface)] p-4"
        onSubmit={(event) => {
          event.preventDefault();
          mutation.mutate(draft);
        }}
      >
        {/* El correo es la identidad de la cuenta: se muestra, no se edita. */}
        <div className="flex flex-col gap-1.5">
          <p className="text-sm font-medium text-[var(--color-content)]">{t("form.email")}</p>
          <p className="text-sm text-[var(--color-content)]">{user.email}</p>
          <p className="text-sm text-[var(--color-content-muted)]">{t("form.emailHint")}</p>
        </div>

        <FormField
          label={t("form.displayName")}
          hint={t("form.displayNameHint")}
          error={fieldErrors?.displayName?.[0]}
        >
          <Input
            type="text"
            autoComplete="name"
            value={draft.displayName}
            onChange={(event) => setDraft({ ...draft, displayName: event.target.value })}
          />
        </FormField>

        <FormField label={t("form.language")} hint={t("form.languageHint")} error={fieldErrors?.culture?.[0]}>
          <select
            className={selectClassName}
            value={draft.culture}
            onChange={(event) => {
              const value = event.target.value;

              if (isSupportedLanguage(value)) {
                setDraft({ ...draft, culture: value });
              }
            }}
          >
            {supportedLanguages.map((language) => (
              <option key={language} value={language}>
                {t(`common:language.${language}`)}
              </option>
            ))}
          </select>
        </FormField>

        {/* Los nombres de las zonas no se traducen: son identificadores de IANA, los mismos que guarda el
            backend, y son la forma en que la gente las busca ("Montevideo", "Madrid"). */}
        <FormField label={t("form.timeZone")} hint={t("form.timeZoneHint")} error={fieldErrors?.timeZoneId?.[0]}>
          <select
            className={selectClassName}
            value={draft.timeZoneId}
            onChange={(event) => setDraft({ ...draft, timeZoneId: event.target.value })}
          >
            {timeZones.map((zone) => (
              <option key={zone} value={zone}>
                {zone}
              </option>
            ))}
          </select>
        </FormField>

        {formError ? (
          <p role="alert" className="text-sm text-[var(--color-danger)]">
            {formError}
          </p>
        ) : null}

        <div>
          <Button type="submit" disabled={mutation.isPending}>
            {t("form.submit")}
          </Button>
        </div>
      </form>
    </div>
  );
}
```

En `src/app/routes.tsx`, agregar la ruta adentro de los hijos de `AppLayout`, justo después de la rama de `/` y antes de la de `/usuarios`:

```tsx
              {
                // Sin permiso: alcanza con tener sesión, que ya la exige el ProtectedRoute de arriba. Cada
                // quien edita el suyo, y el backend no mira más que el token.
                path: "/perfil",
                lazy: async () => ({ Component: (await import("@/features/profile/pages/ProfilePage")).ProfilePage }),
              },
```

Y `src/layouts/components/Breadcrumbs.tsx`, completo (el perfil no está en el menú lateral, así que hasta ahora las migas lo dejaban en "Inicio"):

```tsx
import { useTranslation } from "react-i18next";
import { Link, useLocation } from "react-router";
import { navigation } from "../navigation";

/// Pantallas que no están en el menú lateral pero igual tienen migas propias: al perfil se entra desde el
/// menú del usuario. La clave del texto es la misma que usa ese menú, para que los dos digan lo mismo.
const extraLabelKeys: Record<string, string> = { "/perfil": "layout.userMenu.profile" };

function findActiveLabelKey(pathname: string): string | undefined {
  const item = navigation.flatMap((group) => group.items).find((entry) => entry.to === pathname);

  return item?.labelKey ?? extraLabelKeys[pathname];
}

/// Migas de pan de la barra superior (sección 7.2): "Inicio" siempre, y la página activa al lado si no es el tablero.
export function Breadcrumbs() {
  const { t } = useTranslation();
  const location = useLocation();
  const activeLabelKey = location.pathname === "/" ? undefined : findActiveLabelKey(location.pathname);
  const homeLabel = t("navigation.dashboard");

  if (!activeLabelKey) {
    return (
      <nav aria-label={t("layout.breadcrumbs.label")} className="min-w-0">
        <ol className="flex min-w-0 items-center gap-1.5 truncate text-sm">
          <li aria-current="page" className="truncate font-medium text-[var(--color-content)]">
            {homeLabel}
          </li>
        </ol>
      </nav>
    );
  }

  return (
    <nav aria-label={t("layout.breadcrumbs.label")} className="min-w-0">
      <ol className="flex min-w-0 items-center gap-1.5 truncate text-sm">
        <li className="truncate">
          <Link to="/" className="text-[var(--color-content-muted)] hover:text-[var(--color-content)] hover:underline">
            {homeLabel}
          </Link>
        </li>
        <li aria-hidden="true" className="text-[var(--color-content-muted)]">
          /
        </li>
        <li aria-current="page" className="truncate font-medium text-[var(--color-content)]">
          {t(activeLabelKey)}
        </li>
      </ol>
    </nav>
  );
}
```

```bash
npm run test -- ProfilePage
```

Esperado: `Test Files 1 passed (1)`, `Tests 4 passed (4)`.

- [ ] **Paso 13: la documentación del front**

En `CLAUDE.md`, en la sección **Rutas y sesión**, agregar después del punto de las pantallas de administración:

```markdown
- `/perfil` es la pantalla del perfil propio (nombre, idioma y zona horaria) y se entra desde "Mi perfil", en el menú del usuario. No pide permiso: alcanza con tener sesión, porque cada quien edita el suyo. `PUT /api/me` **reemplaza** los tres campos, así que se mandan siempre los tres, aunque se haya tocado uno solo.
```

En la sección **Reglas**, el punto que empieza con "El idioma elegido se guarda en `localStorage`" pasa a decir:

```markdown
- **El idioma tiene dos niveles.** `changeLanguage` (`shared/i18n`) lo cambia en la interfaz y lo recuerda en `localStorage`: es lo único que puede hacer la pantalla de ingreso, donde todavía no hay cuenta a la que guardárselo. Con sesión abierta, el cambio pasa por `useLanguagePreference().change` (`src/auth`), que además lo guarda en el perfil con `PUT /api/me` y, si ese guardado falla, devuelve la interfaz al idioma anterior y lo avisa: la interfaz y la cuenta nunca quedan diciendo cosas distintas. **Cuando los dos valores difieren, gana el de la cuenta:** `useProfileLanguageSync`, montado en `AppLayout`, lo aplica apenas llega `/api/me`, así entrar desde otro navegador respeta lo que la persona guardó. El idioma sigue viajando en `Accept-Language`, así que los errores del servidor también vienen traducidos.
```

Y agregar al final de esa misma sección:

```markdown
- Las fechas del backend vienen en UTC y se muestran con `formatDateTimeInZone` (`shared/lib/dateTime`), en la zona horaria del perfil. Es el único formateador de fecha y hora del front: si aparece un segundo, las mismas fechas se ven distinto en dos pantallas.
```

En `README.md`, después de la tabla de **Administración (Fase 4)**, agregar:

```markdown
Aparte de esas tres, cualquiera con sesión tiene **`/perfil`**: nombre, idioma y zona horaria de su propia cuenta, desde "Mi perfil" en el menú del usuario. El idioma que se guarda ahí vale para la cuenta, no para el navegador: entrando desde otra máquina, el sistema sigue hablando en ese idioma.
```

- [ ] **Paso 14: verificación**

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBaseFront
npm run test
npm run build
npm run lint
```

Esperado: los 2 casos de `dateTime`, los 3 de `DashboardPage`, los 6 de `UserMenu` y los 4 de `ProfilePage` en verde; la paridad de traducciones también, con el namespace nuevo (`locales > has the same keys in both languages for profile`); `npm run build` sin errores de tipos y `npm run lint` sin salida. Pegar los totales reales.

- [ ] **Paso 15: commit**

```bash
git add src/shared/lib/dateTime.ts src/shared/lib/dateTime.test.ts src/shared/api/profile.ts src/shared/i18n/index.ts src/auth/useCurrentUser.ts src/auth/useLanguagePreference.ts src/features/profile src/features/home/pages/DashboardPage.tsx src/features/home/pages/DashboardPage.test.tsx src/features/users/columns.tsx src/layouts/AppLayout.tsx src/layouts/components/UserMenu.tsx src/layouts/components/UserMenu.test.tsx src/layouts/components/Breadcrumbs.tsx src/locales/es/profile.json src/locales/en/profile.json src/locales/es/common.json src/locales/en/common.json src/app/routes.tsx CLAUDE.md README.md
git commit -m "feat: perfil propio con el idioma guardado en la cuenta" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
### Tarea 18: Documentación y verificación final

Repos: **los dos**.

La fase cierra dejando escrito lo que cambió en los dos repos, corriendo todo lo que se puede comprobar por comando, y dejándole al usuario un checklist corto de lo único que no se puede comprobar sin una persona: leer un correo, apretar botones y pasar por la pantalla de Google.

**Archivos:**
- Modificar: `C:\Users\ezequ\source\repos\ArquitecturaBaseFront\CLAUDE.md`
- Modificar: `C:\Users\ezequ\source\repos\ArquitecturaBaseFront\README.md`
- Modificar: `C:\Users\ezequ\source\repos\ArquitecturaBase\CLAUDE.md`
- Modificar: `C:\Users\ezequ\source\repos\ArquitecturaBase\README.md`
- Modificar: `C:\Users\ezequ\source\repos\ArquitecturaBase\docs\plans\2026-09-20-fase-4-administracion.md`

- [ ] **Paso 1: `CLAUDE.md` del front**

En la sección **Rutas y sesión**, después del punto que empieza con "Las rutas del SPA están **en español**", agregar:

```markdown
- Las pantallas de administración son `/usuarios` (`users.read`), `/roles` (`roles.read`) y `/configuracion` (`settings.manage`). El permiso se pide en `routes.tsx` con `<ProtectedRoute permission="..." />` y se repite en `navigation.ts` para el menú. Son dos lugares a propósito: uno decide si la ruta se abre, el otro si el ítem se ve.
```

En la sección **Reglas**, agregar al final:

```markdown
- **Las acciones se resuelven en diálogos, no en pantallas de detalle.** El alta y la edición van en un `Dialog` sobre el listado, y lo que no se puede deshacer, en `ConfirmDialog` diciendo qué se pierde. La pantalla monta el diálogo solo mientras está abierto, así los campos arrancan con los valores correctos sin resetearlos a mano.
- **Los errores del backend se deciden por el `code`, nunca por el texto.** Cada feature tiene su `errors.ts` con un `switch` sobre `ApiError.code`, que traduce los códigos que merecen un texto propio (los que explican cómo destrabar la situación, como `Users.LastAdmin`) y deja pasar el `detail` del servidor para el resto. `Roles.HasUsers` es la excepción a propósito: el backend dice cuántos usuarios tiene el rol, y ese dato no lo tiene el front.
- Un error de una mutación que nace en un `ConfirmDialog` va a un aviso (`toast`), no a un cartel adentro del diálogo: el diálogo se cierra al confirmar, así que cuando llega la respuesta ya no está. Los de un formulario sí van adentro, en un `<p role="alert">`, y el diálogo queda abierto.
- Lo que consume más de una feature sube a `shared/api`: el catálogo de roles (`shared/api/roles.ts`) lo usan la pantalla de roles y el diálogo que asigna roles a un usuario.
- Las mutaciones son `useMutation` e invalidan lo que corresponde: el listado por su prefijo (`usersQueryKeyRoot`), el detalle por su clave, y `currentUserQueryKey` cuando el cambio puede haber tocado los permisos de quien está usando la pantalla.
```

Y el punto del idioma (el que empieza con "El idioma elegido se guarda en `localStorage`") pasa a decir:

```markdown
- El idioma elegido se guarda en `localStorage` y viaja al backend en `Accept-Language`, así que los errores del servidor también vienen traducidos. Para quien todavía no ingresó, eso es todo. Con sesión iniciada **gana el idioma de la cuenta**: el menú de usuario lo aplica y lo guarda con `PUT /api/me`, y al entrar desde otro navegador se respeta lo guardado (Tarea 18).
```

- [ ] **Paso 2: `README.md` del front**

En **Estructura**, la línea de `features/` pasa a:

```
  features/   un módulo por área (auth, home, users, roles, settings, errors), cada uno con sus páginas y llamadas
```

Y después de esa sección, antes de **Producción**, agregar:

```markdown
## Administración (Fase 4)

Tres pantallas, cada una detrás de su permiso:

| Ruta | Permiso | Qué hace |
|---|---|---|
| `/usuarios` | `users.read` (`users.manage` para las acciones) | listado, alta, edición de nombre y roles, activar, desactivar y eliminar |
| `/roles` | `roles.read` (`roles.manage` para las acciones) | listado de roles con sus permisos, alta, edición y borrado |
| `/configuracion` | `settings.manage` | el modo de registro del sistema: abierto o solo por invitación |

Sin el permiso, el menú no muestra la entrada y entrar a mano a la ruta lleva a `/sin-permiso`. Quien decide, igual, es el backend: el front solo acomoda la interfaz.
```

- [ ] **Paso 3: `CLAUDE.md` del backend**

Después de la sección **Identidad**, agregar:

```markdown
## Administración (Fase 4)

- **Ajustes del sistema:** `SystemSettings` es una entidad de **una sola fila**, auditable. Se lee cacheada con `HybridCache` y el caché se invalida al guardar, así el cambio vale al instante. El seed la crea con el valor de `Registration:Mode` (por defecto `InviteOnly`); **si la fila ya existe, manda la base**: un despliegue nunca pisa lo que se configuró desde el panel.
- **El modo de registro** decide quién puede *crear* una cuenta, no quién puede entrar. `POST /account/login-code` sigue respondiendo siempre `202`, y en `InviteOnly` un correo sin cuenta no genera ni recibe nada: responder distinto diría qué direcciones están registradas. Con Google, en cambio, la persona ya probó ser dueña de la dirección, así que vuelve al ingreso con `Account.NotInvited`.
- **Desactivar o eliminar tiene que cortar el acceso en el momento:** además de marcar la fila, se revocan las autorizaciones y los tokens de OpenIddict y se actualiza el `SecurityStamp` para invalidar la cookie. Sin eso, "desactivar" es una etiqueta que no impide nada durante los 15 minutos que vale el access token.
- **`ApplicationUser` es `ISoftDeletable`:** un usuario borrado desaparece de los listados y no puede entrar, pero su historial de ingresos sigue existiendo. Dar de alta el mismo correo restaura la cuenta, con los roles que diga el alta, no con los que tenía antes.
- **Las reglas que protegen al sistema viven en Domain, con sus tests unitarios:** nadie se saca a sí mismo el rol `Admin`, nadie desactiva ni elimina su propia cuenta, siempre queda al menos un usuario activo con rol `Admin`, y no se borra un rol con usuarios asignados.
- **`Admin` y `User` son del sistema:** no se renombran ni se borran, y a `Admin` no se le editan los permisos.
- El permiso nuevo es `settings.manage`, que el seed le da a `Admin`. El catálogo queda en `users.read`, `users.manage`, `roles.read`, `roles.manage` y `settings.manage`.
- Los enums que viajan en una respuesta lo hacen **por su nombre**, no por su número (`JsonStringEnumConverter` en `Api/DependencyInjection.cs`): el número no dice nada del otro lado y reordenar el enum cambiaría en silencio lo que significa cada valor guardado.
```

- [ ] **Paso 4: `README.md` del backend**

Después de la sección **Identidad (Fase 2)** y sus subsecciones, agregar:

```markdown
## Administración (Fase 4)

Todo se maneja desde el panel, sin tocar la base ni la configuración del servidor: usuarios (alta, roles, activar, desactivar y eliminar), roles con sus permisos, y quién puede entrar al sistema.

**El modo de registro** (`/configuracion` en el front, `PUT /api/settings` en la Api) tiene dos valores:

| Modo | Qué pasa con un correo que no tiene cuenta |
|---|---|
| `InviteOnly` | No entra. La cuenta la tiene que crear un administrador. |
| `Open` | Se crea la cuenta sola, con el rol `User`. |

El valor inicial, al crear la base, sale de `Registration:Mode` y por defecto es **`InviteOnly`**: una instalación nueva arranca cerrada y se abre a propósito. Después, manda lo que diga la base: el seed no pisa la fila si ya existe.

Desactivar o eliminar una cuenta le corta el acceso en el acto (se revocan sus tokens y se invalida su cookie), no solo en el próximo ingreso.
```

- [ ] **Paso 5: verificación por comandos**

Backend:

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBase
dotnet build ArquitecturaBase.slnx
dotnet test
```

Front:

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBaseFront
npm run build
npm run lint
npm run test
```

Esperado: backend con **0 advertencias** y todo en verde; front con los tres comandos limpios. Pegar los totales reales, no decir que pasó. Docker tiene que estar encendido para los tests de integración.

- [ ] **Paso 6: humo con el AppHost**

Desde **PowerShell** (el comando no está en el PATH de Bash):

```powershell
cd C:\Users\ezequ\source\repos\ArquitecturaBase
C:\Users\ezequ\.dotnet\tools\aspire.cmd run --detach
C:\Users\ezequ\.dotnet\tools\aspire.cmd describe
```

Esperado: `postgres`, `appdb`, `api` y `front` en Running / Healthy.

Después, desde Bash, contra el origen único:

```bash
curl -sk -o /dev/null -w "%{http_code}\n" --max-time 10 https://localhost:5173/roles
curl -sk -o /dev/null -w "%{http_code}\n" --max-time 10 https://localhost:5173/configuracion
curl -sk --max-time 10 https://localhost:5173/api/settings | head -3
curl -sk --max-time 10 https://localhost:5173/api/permissions | head -3
```

Esperado: `200` en las dos rutas del SPA (las sirve el fallback de Vite, no el backend), y en las dos de la Api, un `401` con ProblemDetails (`"code":"Http.Unauthorized"`), porque el `curl` no lleva sesión. Que respondan ProblemDetails y no una página en blanco es justamente lo que se está comprobando.

Al terminar, **apagarlo siempre**, desde PowerShell:

```powershell
C:\Users\ezequ\.dotnet\tools\aspire.cmd stop
```

Si queda corriendo, el arranque desde Visual Studio falla con `address already in use`.

- [ ] **Paso 7: el checklist para el usuario**

Esto no se puede comprobar sin una persona. Pedirle al usuario que, con `aspire run` levantado y `https://localhost:5173` abierto, con la cuenta de `Seed:AdminEmail` (que el seed hace Admin), verifique:

1. **El menú.** Bajo *Administración* se ven **Usuarios**, **Roles y permisos** y **Configuración**. Con una cuenta sin esos permisos no se ve ninguna, y entrar a mano a `/roles` o `/configuracion` lleva a `/sin-permiso`.
2. **Dar de alta a alguien.** *Nuevo usuario* → un correo suyo de verdad → *Dar de alta*. Aparece en el listado. Con ese correo, en una ventana de incógnito, pedir el código y entrar: tiene que poder.
3. **Un rol propio, de punta a punta.** En *Roles y permisos*, *Nuevo rol* ("Soporte", con solo *Ver usuarios*). Volver a *Usuarios*, apretar *Roles* en la persona del punto 2, marcar Soporte y guardar. En la ventana de incógnito, recargar: esa persona ahora ve **Usuarios** en su menú y el listado, pero **no** el botón *Nuevo usuario*.
4. **Los roles del sistema.** Admin y User se ven con la etiqueta *Del sistema* y **no** tienen botones de editar ni de eliminar; debajo de la tabla está dicho por qué.
5. **Borrar un rol que tiene gente.** Intentar eliminar *Soporte* mientras alguien lo tenga: tiene que salir un aviso que diga **cuántos** usuarios lo tienen. Sacarle el rol a esa persona y recién ahí se borra.
6. **Desactivar corta el acceso en el acto.** Con la otra persona **con la sesión abierta**, desactivarla desde el panel. En su ventana, la próxima acción (recargar, o pasar de página en el listado) tiene que echarla al ingreso, no seguir andando. Activarla de nuevo la deja volver a entrar.
7. **La regla del último administrador.** Intentar desactivarse o eliminarse a uno mismo, y quitarse el rol Admin: las tres tienen que fallar con un mensaje que diga qué hacer ("asigná el rol Admin a otra cuenta activa"), no con un error genérico.
8. **El modo de registro, sin reiniciar nada.** En *Configuración*, poner **Solo por invitación** (leer la confirmación: tiene que decir qué implica). Desde incógnito, pedir el código con un correo **que no tenga cuenta**: la pantalla responde igual que siempre (no dice que no existe) y **no llega ningún correo** — comprobarlo en `src/ArquitecturaBase.Api/.emails/`, que no tiene que tener un `.eml` nuevo. Con Google, ese mismo correo tiene que volver al ingreso con el mensaje de que pida acceso a un administrador. Volver a **Abierto** y comprobar que ahora sí entra, sin haber reiniciado nada.
9. **Eliminar y volver a dar de alta.** Eliminar a la persona del punto 2: desaparece del listado y no puede entrar. Darla de alta otra vez **con el mismo correo**: la cuenta vuelve, con los roles que diga el alta.
10. **El idioma.** Cambiar a inglés desde el menú de usuario: las tres pantallas nuevas y también los errores del backend (forzar uno, por ejemplo el del punto 5) tienen que salir en inglés.

- [ ] **Paso 8: cierre**

- Revisión de código de toda la fase con un subagente revisor, sobre los dos repos.
- Agregar al plan (`docs/plans/2026-09-20-fase-4-administracion.md`) la sección "Resultado de la ejecución", con la misma estructura que las fases anteriores: tests por proyecto, desvíos, riesgos aceptados y pendientes. Que queden anotados, como mínimo, estos tres:
  - **Lo que quedó del Paso 1 de la Tarea 16**, si hubo que registrar `JsonStringEnumConverter`: es un cambio de contrato de toda la Api, no solo de `/api/settings`.
- Commit de la documentación del front:

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBaseFront
git add CLAUDE.md README.md
git commit -m "docs: documentar las pantallas de administracion" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- Commit de la documentación y del resultado, en el backend:

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBase
git add CLAUDE.md README.md docs/plans/2026-09-20-fase-4-administracion.md
git commit -m "docs: registrar el resultado de la Fase 4" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---
