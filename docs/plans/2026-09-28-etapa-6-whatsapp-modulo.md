# Etapa 6: WhatsApp como módulo opcional, plan detallado

> **Para agentes:** este es el plan de ejecución de la Etapa 6 del [plan maestro](2026-09-26-plantilla-estandar-por-etapas.md), que la describe en su sección "Etapa 6". Rige la [Forma de trabajo desde la Etapa 3](2026-09-26-plantilla-estandar-por-etapas.md#forma-de-trabajo-desde-la-etapa-3), con una diferencia que pidió el usuario: **cada tanda termina con el build y los cuatro proyectos de tests en verde, con Docker**, no solo la puerta. Se trabaja en el checkout principal, sin ramas ni worktrees (también para la prueba de fuego, ver la sección 7), y sin push. Commits en español con conventional commits y las dos líneas de atribución. `git add` siempre con rutas explícitas, nunca `global.json`.

**Fecha:** 2026-09-28. **Estado:** listo para ejecutar. Las siete decisiones las tomó el usuario ese día, todas como estaban recomendadas. Una revisión adversarial contra el código (el mismo día, sobre `5d72826`) corrigió lo que dejaba una tanda en rojo o sin compilar (2, 3, 4, 5, 7 y 8), el orden de la prueba de fuego, reglas de arquitectura que no veían el módulo (`ControllerServiceRepositoryTests`, `ErrorDeclarationTests`, `DependencyInjectionTests`) y las dudas de la sección 9.2.

**Fuentes, todas verificadas contra el código de `main-q6zany` en `9f93846`:** el relevamiento de la Etapa 6 (en el scratchpad de la sesión que escribió este plan, no está en el repo), la Etapa 6 del plan maestro, el [ADR 0007](../decisions/0007-whatsapp-como-modulo-opcional.md), [`whatsapp.md`](../features/whatsapp.md), [`identidad.md`](../features/identidad.md), [`administracion.md`](../features/administracion.md), [`backend.md`](../architecture/backend.md) y [`AGENTS.md`](../../AGENTS.md). Las citas `archivo:línea` son de ese commit y se corren con las tandas: manda el texto citado, no el número.

**Alias de rutas:** `Dom` = `src/ArquitecturaBase.Domain`, `App` = `src/ArquitecturaBase.Application`, `Infra` = `src/ArquitecturaBase.Infrastructure`, `Api` = `src/ArquitecturaBase.Api`, `DUT` = `tests/ArquitecturaBase.Domain.UnitTests`, `AUT` = `tests/ArquitecturaBase.Application.UnitTests`, `Arch` = `tests/ArquitecturaBase.ArchitectureTests`, `IT` = `tests/ArquitecturaBase.Api.IntegrationTests`. `M/W` abrevia la carpeta `Modules/WhatsApp` de cada uno.

---

## 1. Resumen para una persona

**Qué se busca.** Que un proyecto que nace de la plantilla y no usa WhatsApp lo pueda sacar entero siguiendo una guía corta: borrar cinco carpetas `Modules/WhatsApp` (una por proyecto de `src` con código del módulo, más las de tests), un bloque de tres líneas en `Program.cs` con sus tres `using`, la sección `WhatsApp` de los `appsettings`, y generar una migración que borra las dos tablas. Después de eso compila sin advertencias y los tests del núcleo pasan.

**Qué cambia.**
- Todo lo que es 100 % WhatsApp (el bot, el webhook, el cliente de Meta, la cola de envío, la retención, sus entidades, repositorios, textos del bot, controllers y rutas condicionales) se muda a carpetas `Modules/WhatsApp` dentro de los mismos proyectos. No se crean proyectos nuevos.
- Donde el núcleo hoy llama a WhatsApp, pasa a llamar a **cuatro puertos** chicos en `App/Interfaces/Channels`: el canal telefónico (`IPhoneChannel`), quien ata algo al número de una cuenta (`IPhoneLinkParticipant`), un canal de invitación (`IInvitationChannel`) y quien sabe el estado de entrega de una invitación (`IInvitationDeliveryStatusSource`). El núcleo trae una versión "apagada" o de correo de cada uno; el módulo registra la suya.
- El pedido de códigos por teléfono (ingreso y perfil), su tope diario y el perfil `/api/me/whatsapp` pasan al módulo, con las mismas rutas.
- Dos valores guardados cambian de nombre, con migración: `LoginCodes.Channel` pasa de `'WhatsApp'` a `'Phone'` y la columna `UserInvitations.WaMessageId` pasa a `ProviderMessageId`.
- Los tests de arquitectura aprenden que un `Modules.<M>` es la misma capa que el núcleo, y un test nuevo prohíbe que el núcleo nombre un módulo.

**Qué no cambia.** Ninguna ruta, verbo, status, código de error, texto ni JSON que ve el front (decisión 7): el front no se toca. Las reglas funcionales de [`whatsapp.md`](../features/whatsapp.md) siguen iguales (la regla de oro, los duplicados, el orden de los locks, la retención). El teléfono sigue siendo un dato de la cuenta del núcleo, y `LoginLink` sigue en el núcleo.

**Por qué así.** Carpetas y no proyectos, porque los tipos internos siguen visibles y la regla de capas no se complica; los tests de arquitectura hacen cumplir la frontera. Puertos y no referencias, porque es la única forma de que el núcleo compile sin el módulo. Mismas rutas y códigos, porque el front ya los usa. Es la etapa más grande del plan (4 a 6 días): la hacemos en diez tandas que dejan todo en verde, primero la mudanza mecánica y después los puertos, del de menos riesgo al de más.

---

## 2. Decisiones

### 2.1 Las siete decisiones del usuario (2026-09-28)

| # | Decisión | Por qué |
|---|---|---|
| 1 | **Carpetas `Modules/WhatsApp` dentro de los mismos proyectos, también en Domain**, con tests de arquitectura que hacen cumplir la frontera. | Proyectos aparte harían cumplir la frontera con el compilador, pero pedirían `InternalsVisibleTo` cruzados o volver públicos tipos internos (el módulo usa `LoginCodeIssuer`, `PhoneNumberLinker`, `AccountCreationPolicy`, `UserGuard`, `AdvisoryLockExtensions`, todos `internal`), y cuatro proyectos más en la regla de capas. `Domain/Modules/WhatsApp` y no `Domain/WhatsApp`, para que un solo patrón (`*.Modules.WhatsApp`) cubra las cuatro capas. |
| 2 | **Un bloque de tres líneas en `Program.cs`, una por capa:** `AddWhatsAppApplication()`, `AddWhatsAppInfrastructure(configuration)` y `AddWhatsAppApi()`. | Así cada capa registra lo suyo (hoy Infrastructure registra servicios de Application del webhook, `WhatsAppRegistration.cs:121`) y la regla "Api toca Infrastructure solo desde `Program.cs`" (`LayerDependencyTests.Api_uses_infrastructure_only_from_the_composition_root`) sigue sin excepciones. Una sola línea obligaría a que Api llame a Infrastructure desde otro lugar. |
| 3 | **`LoginLink` queda en el núcleo** como enlace mágico genérico, con su tabla y `/account/login-link/*`. | `AccountAccessRevoker` y `PhoneNumberLinker.VoidPendingLinksAsync` los anulan desde el núcleo, y el lock `login-link:` es el lock "de la cuenta" que usan la administración y el perfil. Llevarlo al módulo arrastraría dos rutas, una tabla y ese lock. Sin el módulo nadie lo emite (hoy el único emisor es el bot), pero la capacidad queda. |
| 4 | **Una cuenta con solo teléfono se permite aunque no haya canal telefónico.** | `AccountRules` acepta correo **o** teléfono, y un administrador puede agregarle un correo. Consecuencia de diseño: la versión apagada de `IPhoneChannel` no rechaza ningún país (sin canal no hay tarifa por país). Con el módulo, la regla del país no cambia. |
| 5 | **Se migran solo `LoginCodes.Channel` (`'WhatsApp'` → `'Phone'`) y `UserInvitations.WaMessageId` → `ProviderMessageId` (`RenameColumn`).** `LoginMethod.WhatsAppCode` y `WhatsAppLink` quedan. | El canal del código es el tipo de destino (un número), no el proveedor, y la columna de la invitación sirve para cualquier proveedor. Los métodos de `LoginAudits` son auditoría ya guardada: renombrarlos reescribe historia sin ganar nada. |
| 6 | **Los textos de error de WhatsApp quedan en los `.resx` del núcleo.** | Son seis claves inocuas (`Auth.WhatsApp.CountryNotSupported`, `Users.Invitation.ConsentRequired`, `Users.Invitation.NameRequired` en `Errors.resx`; `LoginCodeFormatWhatsApp`, `InvitationPhoneRequired`, `InvitationWhatsAppUnavailable` en `Validation.resx`) y el front usa sus códigos. Un `.resx` por módulo pediría un traductor compuesto. `ErrorCodeTranslationTests` solo exige que cada código declarado tenga texto, no al revés, así que una clave sin su `Error` (sin el módulo) no rompe nada. Los textos del bot (`Bot.resx`) sí se mudan: son del módulo entero. |
| 7 | **Ninguna ruta HTTP, código de error ni JSON cambia.** | El front usa `POST /account/login-code/whatsapp`, `/api/me/whatsapp` (code, PUT y DELETE), `DELETE /api/users/{id}/whatsapp`, `whatsapp`/`whatsappCountries`/`whatsappNumber` de `GET /account/login-methods`, `lastInvitation.deliveryStatus` y el valor `"WhatsApp"` de `invitation.channel`. Por eso `UserInvitationChannel { Email, WhatsApp }` queda en el núcleo tal cual: es el valor del JSON y el que se guarda. |

### 2.2 Correcciones al plan maestro y al ADR 0007

1. **Migraciones.** El plan maestro dice que las tablas de WhatsApp "quedan en la migración inicial". No es así: las crea `20260923174423_WhatsAppMessages` y un índice lo agrega `20260923214525_WhatsAppInboundProcessing`. Las migraciones viejas nombran las entidades con cadenas (`modelBuilder.Entity("ArquitecturaBase.Domain.WhatsApp.WhatsAppContact", …)` en el snapshot) y no usan `typeof` de ninguna entidad, así que **siguen compilando sin el módulo**. `WhatsAppContacts.UserId` no tiene clave foránea a `AspNetUsers`; la única FK es interna del módulo (`WhatsAppMessages.ContactId` → `WhatsAppContacts`).
2. **La línea del registro cruzado** es `WhatsAppRegistration.cs:121` (`services.AddWhatsAppWebhookApplicationServices();`), no la 120.
3. **Carpeta de Domain:** `Domain/Modules/WhatsApp`, no `Domain/WhatsApp` (decisión 1).
4. **Registro:** no es "un solo `AddWhatsAppModule()` por capa" con el mismo nombre, sino tres métodos con nombre propio (decisión 2), porque se encadenan en la misma `IServiceCollection` y tres extensiones con el mismo nombre chocarían.
5. **`ApplyConfigurationsFromAssembly` filtrado por namespace no hace falta:** `ApplicationDbContext` ya toma todas las configuraciones del ensamblado (`ApplicationDbContext.cs:44`); al borrar la carpeta, desaparecen solas.
6. **Los puertos del borrador (`IInvitationChannel`, `ILoginCodeChannel`, `IPhoneLinkObserver`) no alcanzan tal cual:** quedan afuera el país permitido, el tope diario, el estado de entrega y el orden de los locks, y un "observador" no puede tomar locks *antes* que el núcleo. Los cuatro de la sección 3 los reemplazan.
7. **"Borrando carpetas y una línea de registro"** (ADR, Consecuencias) es en realidad: carpetas, un bloque de tres líneas y tres `using` en `Program.cs`, la sección `WhatsApp` y dos claves de `RateLimiting` en los `appsettings`, y una migración generada.

### 2.3 Donde el código contradijo al relevamiento

1. **`IPhoneCodeSender` sobra.** Los únicos que mandan un código por teléfono son `SignInCodeIssuer.RequestWhatsAppLoginCodeCoreAsync` (lo llama solo `LoginCodeService.RequestWhatsAppLoginCodeAsync`) y `DestinationCodeIssuer.RequestPhoneCodeAsync` (lo llama solo `ProfileWhatsAppService`). Los dos pedidos se van al módulo, y el tope diario con ellos: el núcleo no manda códigos por teléfono, así que no necesita un puerto para eso. Verificado con `grep` de los tres métodos.
2. **El inventario de rutas tiene 43, no 41** (`ExplicitRouteInventoryTests`: `Assert.Equal(43, ExpectedRoutes.Length)`). 41 eran las de la migración a MVC; después se sumaron `GET /api/roles/{id:guid}` y `GET /api/roles/paged`.
3. **`WhatsAppLockOrderTests` no prueba el orden contactos → cuenta.** Prueba que los locks de *un* webhook se toman ordenados y sin repetir. El orden entre el perfil o la administración y el bot lo prueban `MeWhatsAppEndpointsTests` (cinco tests de concurrencia con `HeldContactRepository`/`HeldLoginLinkRepository`, por ejemplo `Replacing_the_number_while_the_bot_answers_the_previous_chat_waits_for_it_and_voids_the_link_it_sent` y `Unlinking_while_the_bot_moves_the_account_to_another_chat_of_the_number_does_not_end_in_a_deadlock`) y, sin Docker, `UserAccessServicePhoneTests.Unlink_locks_contact_then_account_revokes_sessions_and_commits`. Esos son los que tienen que quedar en verde en cada tanda.
4. **Un test de IL no alcanza para la frontera.** `UserInvitation.MaxWaMessageIdLength = WhatsAppMessage.MaxWaMessageIdLength` es una constante: el compilador la copia como literal y el IL no nombra `WhatsAppMessage`. Lo mismo pasa con un `<see cref>` (no deja IL) y con `nameof`. Ninguno de los tres lo ve Mono.Cecil ni NetArchTest, y los tres rompen la compilación al borrar el módulo. El test de la frontera lee el **código fuente** (sección 6), como ya hace `MinimalApiRoutesTests`.
5. **El registro condicional del webhook no se puede conservar sin el cruce de capas.** Hoy los servicios de Application del webhook existen solo con el webhook prendido, y `WhatsAppRegistrationTests` lo afirma dos veces (`Assert.Empty(api.Services.GetServices<IWhatsAppWebhookService>())`). Solo Infrastructure sabe si el webhook está prendido (lee los secretos). Con la decisión 2, Application los registra siempre, e Infrastructure registra siempre los adaptadores del webhook (sin estado); lo condicional queda en lo que corre solo: el procesador, el cliente HTTP y el envío. Esas dos aserciones cambian a "no hay `WhatsAppInboundProcessor` entre los `IHostedService`" (tanda 3). Nada HTTP cambia: las rutas del webhook siguen sin existir con el webhook apagado.
6. **El AppHost no nombra el webhook "solo en un comentario":** el bloque `DevTunnel` de `AppHost.cs` (líneas 28-50) es código que existe solo para que Meta llegue al webhook. No rompe nada sin el módulo; la guía lo marca como opcional de quitar.
7. **`PUT` y `DELETE /api/me/whatsapp` no son condicionales hoy:** solo `POST /api/me/whatsapp/code` lleva `[WhatsAppRoute(Messaging)]` (`MeController.cs:59-87`). Se mudan igual, con los mismos atributos.
8. **`IPhoneChannel` apagado no devuelve `Auth.WhatsApp.CountryNotSupported`:** por la decisión 4 acepta cualquier país.

---

## 3. Diseño

### 3.1 Dirección de las dependencias

```text
núcleo ──(nunca)──► Modules.WhatsApp
Modules.WhatsApp ──► núcleo (tipos internos incluidos: mismo ensamblado)
Program.cs ──► los tres *Registration del módulo (la única excepción)
```

El núcleo define los puertos y trae su implementación "apagada" (o la de correo). El módulo implementa los puertos y los registra. Nada del núcleo nombra un tipo del módulo, ni en el IL ni en el fuente (`using`, `cref`, `nameof`, constantes). Un módulo no nombra a otro módulo.

### 3.2 Los cuatro puertos

Viven en `App/Interfaces/Channels/` (namespace `ArquitecturaBase.Application.Interfaces.Channels`), una carpeta nueva: no son integraciones con un proveedor (no los implementa Infrastructure) ni contratos de persistencia. Las implementaciones del núcleo van en `App/Channels/` y las del módulo en `App/Modules/WhatsApp/Channels/`, **fuera de `Services`**, porque `ApplicationHelpersTests` trata todo lo registrado bajo `Services` que no implementa un contrato de `Interfaces/Services` como un helper, y un adaptador se registra por su interfaz.

#### `IPhoneChannel`: ¿hay canal telefónico y a qué países manda?

```csharp
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// El canal por el que se mandan códigos a un número de teléfono. Lo leen los medios de ingreso (GET
/// /account/login-methods) y el alta y la edición de una cuenta, que controlan el país de un número nuevo. Hay uno solo:
/// sin módulo, DisabledPhoneChannel (apagado, sin países, acepta cualquier país); con WhatsApp, el del módulo.
/// </summary>
public interface IPhoneChannel
{
    /// <summary>Si se pueden mandar códigos por teléfono. Apagado, el ingreso no ofrece la opción.</summary>
    bool IsEnabled { get; }

    /// <summary>Los países a los que se mandan códigos, ISO 3166-1 alfa-2 en mayúsculas. Vacío si está apagado.</summary>
    IReadOnlyList<string> Countries { get; }

    /// <summary>El número del canal, solo dígitos, para volver a él desde la web; null si no hay.</summary>
    string? DisplayNumber { get; }

    /// <summary>
    /// Si se le pueden mandar códigos a <paramref name="phone"/> por su país. Vale aunque el canal esté apagado: es la regla
    /// de un número nuevo en el alta y la edición. El error es el del canal (con WhatsApp, Auth.WhatsApp.CountryNotSupported).
    /// </summary>
    Result EnsureCanSendTo(PhoneNumber phone);
}
```

- **Núcleo:** `App/Channels/DisabledPhoneChannel.cs`, `internal sealed`: `IsEnabled = false`, `Countries = []`, `DisplayNumber = null`, `EnsureCanSendTo` → `Result.Success()` (decisión 4). Registro: `services.TryAddSingleton<IPhoneChannel, DisabledPhoneChannel>()` en `AddApplication`.
- **Módulo:** `App/M/W/Channels/WhatsAppPhoneChannel.cs`, `internal sealed`, singleton, con `IWhatsAppAvailability`, `IOptions<WhatsAppLoginOptions>` e `IPhoneNumberParser`: `IsEnabled = availability.IsEnabled`; `Countries` y `DisplayNumber` salen de las opciones **solo si está prendido** (lo mismo que hace hoy `LoginMethodsService`); `EnsureCanSendTo` es el `EnsureCountryAllowed` de hoy (`UserContactLinker.cs:83-86`), sin mirar `IsEnabled`, igual que hoy. Registro: `services.Replace(ServiceDescriptor.Singleton<IPhoneChannel, WhatsAppPhoneChannel>())`.
- **Por qué `TryAdd` en el núcleo y `Replace` en el módulo:** funciona en cualquier orden. Si el núcleo se registra primero, `Replace` saca su descriptor; si el módulo va primero, `TryAdd` no agrega el apagado. Con `Add` + `Add`, el orden decidiría en silencio cuál gana. Un test del módulo lo fija en los dos órdenes (tanda 4).
- **Quién lo llama:** `LoginMethodsService` (`IsEnabled`, `Countries`, `DisplayNumber`) y `UserContactLinker.EnsureCountryAllowed` (`EnsureCanSendTo`). Ninguno abre transacción: no hay límite ni lock en juego.

#### `IPhoneLinkParticipant`: quien ata algo al número de una cuenta

```csharp
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// Algo que un módulo ata al número de una cuenta (con WhatsApp, el contacto del chat). PhoneNumberLinker lo avisa en
/// cada cambio del número: vincularlo desde el perfil, reemplazarlo o quitarlo desde el perfil o la administración. Puede
/// haber cero o más; se llaman en el orden en que se registraron. Todos los métodos corren adentro del límite del caso de
/// uso: no abren ni confirman transacciones, y sus locks la exigen.
/// </summary>
public interface IPhoneLinkParticipant
{
    /// <summary>
    /// Toma los locks de lo que va a tocar el cambio: lo atado a la cuenta y, si pasa a <paramref name="newPhone"/>, lo
    /// atado a ese número. PhoneNumberLinker lo llama ANTES del lock de la cuenta (login-link:), que es el orden del bot:
    /// al revés, el perfil y el bot se esperan mutuamente y Postgres corta a uno con un deadlock (40P01). Espera a quien
    /// tenga esos locks. Quien llama lee la cuenta después.
    /// </summary>
    Task LockAsync(Guid userId, PhoneNumber? newPhone, CancellationToken cancellationToken);

    /// <summary>La cuenta probó desde el perfil que <paramref name="phone"/> es suyo, y ya quedó guardado.</summary>
    Task PhoneConfirmedAsync(Guid userId, PhoneNumber phone, CancellationToken cancellationToken);

    /// <summary>La cuenta soltó su número (lo reemplazó o lo quitó): el módulo suelta lo que tenía atado.</summary>
    Task PhoneReleasedAsync(Guid userId, CancellationToken cancellationToken);
}
```

- **Núcleo:** no trae ninguno. Sin participantes, `PhoneNumberLinker.LockAsync` toma solo `login-link:`.
- **Módulo:** `App/M/W/Channels/WhatsAppPhoneLinkParticipant.cs`, `internal sealed`, scoped, con `WhatsAppContactLinker`: `LockAsync` → `contactLinker.LockAsync` (`LockForNumberChangeAsync`, los locks de fila de los contactos), `PhoneConfirmedAsync` → `contactLinker.LinkNumberAsync`, `PhoneReleasedAsync` → `contactLinker.UnlinkUserAsync`. `WhatsAppContactLinker` no cambia y sigue siendo el único que vincula y suelta contactos (la regla de `whatsapp.md`). Registro: `services.TryAddEnumerable(ServiceDescriptor.Scoped<IPhoneLinkParticipant, WhatsAppPhoneLinkParticipant>())`, idempotente.
- **Quién lo llama: solo `PhoneNumberLinker`** (recibe `IEnumerable<IPhoneLinkParticipant>` en lugar de `WhatsAppContactLinker`):
  - `LockAsync(userId, newPhone)`: `foreach` participante `LockAsync`, **después** `loginLinks.LockAccountAsync(userId)`. Es el orden canónico de hoy (`PhoneNumberLinker.cs:45-49`): contactos antes que cuenta.
  - `ConfirmOwnPhoneAsync`: igual que hoy, y al final `foreach` participante `PhoneConfirmedAsync` en lugar de `contactLinker.LinkNumberAsync` (`:112`).
  - `ReleaseContactAsync` pasa a llamarse **`ReleasePhoneAsync`** y hace `foreach` participante `PhoneReleasedAsync` (hoy delega en `contactLinker.UnlinkUserAsync`, `:124-125`). Sus llamadores: `UserContactLinker.ChangeAsync` (`:234`), `UserAccessService.UnlinkPhoneCoreAsync` (`:115` y `:127`) y `ProfileWhatsAppService.UnlinkOwnPhoneCoreAsync` (`:136`).
- **Orden de locks, completo** (lo que tiene que decir `backend.md` al final): `login-code:` del correo y después el del número, en dos llamadas; **los locks de los participantes**; `login-link:` o `user-invitation:`; `users:admins`, último. El contacto anterior que suelta el bot lo sigue tomando sin esperar (`GetByUserIdForUnlinkAsync`), eso no cambia.
- **Límite:** todo adentro del límite de quien llama (`UserAdministrationService`, `UserAccessService`, `ProfileWhatsAppService`). Ninguno de los tres cambia su `CommitPolicy`.

#### `IInvitationChannel`: por dónde sale una invitación

```csharp
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// Un canal por el que un administrador manda una invitación. Uno por valor de <see cref="UserInvitationChannel"/>: el
/// de correo lo trae el núcleo y el de WhatsApp, su módulo. Sin adaptador para el canal pedido, la invitación se rechaza
/// en el campo del canal. No abre ni confirma transacciones.
/// </summary>
public interface IInvitationChannel
{
    UserInvitationChannel Channel { get; }

    /// <summary>Si la invitación guarda quién confirmó el consentimiento de la persona y cuándo (WhatsApp sí, correo no).</summary>
    bool RecordsConsent { get; }

    /// <summary>
    /// Las reglas del canal, antes de tocar nada: afuera de todo lock. Devuelve el error ya atado a su campo
    /// (<see cref="InvitationCheck"/> trae los nombres de los campos del alta o del reenvío).
    /// </summary>
    Result Check(InvitationCheck check);

    /// <summary>
    /// Encola el mensaje de <paramref name="invitation"/>, ya agregada al repositorio, en el idioma
    /// <paramref name="culture"/> de la cuenta. Si la cola no lo toma, la marca como no enviada (MarkSendFailed) y lo
    /// registra sin datos personales: el alta no se deshace por un envío que falló. Adentro del límite y del lock
    /// user-invitation: que tomó quien llama, antes del commit.
    /// </summary>
    void Enqueue(UserAccount user, UserInvitation invitation, string culture);
}
```

`InvitationCheck` es un modelo nuevo, `App/Models/Users/InvitationCheck.cs`:

```csharp
namespace ArquitecturaBase.Application.Models.Users;

/// <summary>Lo que mira un canal de invitación, con el nombre de cada campo en el cuerpo del alta o del reenvío.</summary>
public sealed record InvitationCheck(
    bool HasEmail,
    bool HasPhone,
    bool Consent,
    string? DisplayName,
    string ChannelField,
    string ConsentField,
    string DisplayNameField);
```

- **Núcleo:** `App/Channels/EmailInvitationChannel.cs`, `internal sealed partial`, scoped, con `IEmailQueue`, `IEmailTemplateRenderer`, `IPublicOrigin` e `ILogger<EmailInvitationChannel>`: `Channel = Email`, `RecordsConsent = false`; `Check` → sin correo, `FieldErrors.Validation(check.ChannelField, ValidationMessages.InvitationEmailRequired)`; `Enqueue` es la rama de correo de hoy de `UserInvitationIssuer.SendAsync` (`:106-118`), con `LoginUrl()` y `LogEmailNotQueued` mudados tal cual (el log cambia de categoría: de `UserInvitationIssuer` a `EmailInvitationChannel`; ningún test filtra por esa categoría, verificado con `grep "did not take an invitation" tests`). Registro: `services.TryAddEnumerable(ServiceDescriptor.Scoped<IInvitationChannel, EmailInvitationChannel>())`.
- **Módulo:** `App/M/W/Channels/WhatsAppInvitationChannel.cs`, que es `WhatsAppInvitationIssuer` mudado con `git mv` y adaptado: `Channel = WhatsApp`, `RecordsConsent = true`; `Check` con las cuatro reglas de hoy, en el mismo orden (`UserInvitationIssuer.cs:60-67`): apagado → `InvitationWhatsAppUnavailable` en el canal; sin teléfono → `InvitationPhoneRequired` en el canal; sin consentimiento → `UserInvitationErrors.ConsentRequired` en el consentimiento; sin nombre → `UserInvitationErrors.NameRequired` en el nombre; `Enqueue` sin cambios. Registro: `TryAddEnumerable` scoped.
- **Quién lo llama:** `UserInvitationIssuer` (recibe `IEnumerable<IInvitationChannel>` en lugar de `WhatsAppInvitationIssuer`, `IEmailQueue`, `IEmailTemplateRenderer`, `IPublicOrigin` y `ILogger`; queda con `IUserInvitationRepository`, los canales, `ICurrentUser` y `TimeProvider`):
  - `Check(channel, consent, displayName, hasEmail, hasPhone, fields)`: busca el adaptador del canal. Si no hay, `FieldErrors.Validation(fields.Channel, ValidationMessages.InvitationWhatsAppUnavailable)`, que es lo que hoy responde WhatsApp apagado (el único canal que puede faltar es WhatsApp; ver la duda 3 de la sección 9). Si hay, `adapter.Check(new InvitationCheck(hasEmail, hasPhone, consent, displayName, fields.Channel, fields.Consent, fields.DisplayName))`.
  - `SendAsync(user, channel)`: el lock `user-invitation:` como hoy, `UserInvitation.Send(user.Id, channel, sentBy, nowUtc, adapter.RecordsConsent)`, `invitations.Add`, `adapter.Enqueue(user, invitation, culture)`.
  - Un test del núcleo fija que no haya dos adaptadores para el mismo canal (tanda 5).

#### `IInvitationDeliveryStatusSource`: el estado de entrega

```csharp
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// El estado de entrega de las invitaciones de un canal que lo sigue (con WhatsApp, el que avisa Meta por el webhook).
/// Cero o uno por canal; el correo no tiene. Solo lee, sin límite.
/// </summary>
public interface IInvitationDeliveryStatusSource
{
    UserInvitationChannel Channel { get; }

    /// <summary>
    /// El estado de la invitación que salió con <paramref name="providerMessageId"/>; Pending si todavía no tiene id o el
    /// proveedor no avisó nada. Las fallidas antes de salir (SendFailed) no llegan acá: las resuelve quien llama.
    /// </summary>
    Task<InvitationDeliveryStatus> FindStatusAsync(string? providerMessageId, CancellationToken cancellationToken);
}
```

- **Núcleo:** ninguno. Sin fuente para el canal, el detalle muestra `Failed` si `SendFailed`, y si no, `null` (hoy el correo ya funciona así).
- **Módulo:** `App/M/W/Channels/WhatsAppInvitationDeliveryStatusSource.cs`, scoped, con un lector nuevo del módulo, `IWhatsAppMessageReader.FindOutboundStatusAsync(string waMessageId, CancellationToken) → Task<WhatsAppMessageStatus?>` (`App/M/W/Interfaces/Persistence/`, implementado por `Infra/M/W/Persistence/Readers/WhatsAppMessageReader.cs` con `AsNoTracking`, dirección `Outbound` y el índice único de `WaMessageId`). Mapea como hoy `LastInvitation.DeliveryStatusOf` (`LastInvitation.cs:25-50`): id null → `Pending`; `Sent`/`Delivered`/`Read`/`Failed` → el mismo; sin mensaje o sin estado → `Pending`.
- **Quién lo llama: `UserQueryService.GetUserAsync`**, en lugar de la subconsulta de `UserInvitationReader` (`:26-31`):
  - `UserInvitationRow` pasa a `(UserInvitationChannel Channel, DateTime SentAtUtc, bool SendFailed, string? ProviderMessageId)`: sin `HasWaMessageId` ni `OutboundStatus`, y sin `using` del dominio del módulo.
  - `LastInvitation.From(UserInvitationRow row, InvitationDeliveryStatus? trackedStatus)` sigue siendo una función pura: `SendFailed` → `Failed`; si no, `trackedStatus`.
  - `UserQueryService` calcula `trackedStatus` solo si `!row.SendFailed` y hay fuente para `row.Channel`. Recibe `IEnumerable<IInvitationDeliveryStatusSource>` (queda con 6 dependencias).
  - **Costo:** el detalle de un usuario pasa de una consulta a dos cuando la última invitación es por WhatsApp. Es una lectura por pedido, por índice único: despreciable. `IUserInvitationReader.FindLatestAsync` actualiza su XML ("una sola consulta").

### 3.3 Registro y orden de llamada en DI

`Program.cs` queda así (el resto del archivo no cambia):

```csharp
using ArquitecturaBase.Api;
using ArquitecturaBase.Api.Hosting;
using ArquitecturaBase.Api.Modules.WhatsApp;            // módulo WhatsApp
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application;
using ArquitecturaBase.Application.Modules.WhatsApp;    // módulo WhatsApp
using ArquitecturaBase.Infrastructure;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp; // módulo WhatsApp
using ArquitecturaBase.Infrastructure.Persistence;

// …
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration, builder.Environment)
    .AddPresentation();

// Módulo WhatsApp (ADR 0007). Para quitarlo: este bloque, los tres using marcados y docs/guides/quitar-whatsapp.md.
builder.Services
    .AddWhatsAppApplication()
    .AddWhatsAppInfrastructure(builder.Configuration)
    .AddWhatsAppApi();
```

Qué registra cada método (y qué deja de registrar el núcleo):

| Método | Registra | Sale del núcleo |
|---|---|---|
| `AddWhatsAppApplication()` (`App/M/W/WhatsAppApplicationRegistration.cs`, `public static`) | `WhatsAppLoginOptions` (con su validación, igual que hoy `App/DependencyInjection.cs:29-33`); `Replace` de `IPhoneChannel`; `TryAddEnumerable` de los otros tres puertos; los helpers `WhatsAppContactLinker`, `WhatsAppCodeIssuer`, `WhatsAppCodeQuotaGuard`, `WhatsAppLinkIssuer`, `WhatsAppReplyPolicy`; los servicios `IWhatsAppLoginCodeService`, `IProfileWhatsAppService`, `IWhatsAppDeliveryService`, `IWhatsAppWebhookPersistence`, `IWhatsAppWebhookService`, `IWhatsAppInboundService` (todos scoped); sus validadores con `AddValidatorsFromAssembly(assembly, filter: solo los de Modules.WhatsApp, includeInternalTypes: true)` | `AddApplication` pierde `WhatsAppLoginOptions`, `WhatsAppContactLinker`, `WhatsAppInvitationIssuer`, `IProfileWhatsAppService`, `IWhatsAppDeliveryService`; `AddWhatsAppWebhookApplicationServices` se borra; `AddApplication` deja afuera los validadores de `ArquitecturaBase.Application.Modules.*` con el `filter` de su propia llamada (no dentro de `AddApplicationValidatorsFromAssembly`, que también usa `ApiFactory` con el ensamblado de tests) |
| `AddWhatsAppInfrastructure(IConfiguration)` (`Infra/M/W/WhatsAppInfrastructureRegistration.cs`, `public static`; es `WhatsAppRegistration` renombrado) | todo lo de `AddWhatsApp` de hoy; los tres repositorios (salen de `PersistenceRegistration.cs:45-47`) y el lector nuevo, **al principio y sin condición**, antes del `if (!enabled) return` de hoy (`WhatsAppRegistration.cs:48-53`): hoy se registran siempre, y los necesitan la retención y los servicios del módulo con WhatsApp apagado; **siempre** `IWhatsAppSignatureValidator`, `IWhatsAppWebhookReader`, `IWhatsAppWebhookRetry` e `IWhatsAppInboundSignal`; **solo con el webhook prendido** la validación del origen público y `WhatsAppInboundProcessor`; sin él, `WhatsAppWebhookOffNotice` | `Infra/DependencyInjection.cs` pierde `services.AddWhatsApp(configuration)`; `PersistenceRegistration` pierde las tres líneas |
| `AddWhatsAppApi()` (`Api/M/W/WhatsAppApiRegistration.cs`, `public static`) | la convención de rutas condicionales (`AddOptions<MvcOptions>().Configure<IWhatsAppAvailability>(…)`, hoy `AddConditionalWhatsAppRoutes`); `WhatsAppWebhookRateLimitOptions` (sección `RateLimiting`, claves `WhatsAppWebhookPermitLimit` y `WhatsAppWebhookWindowMinutes`, las mismas de hoy, con su validación); la política `whatsapp-webhook` con `services.Configure<RateLimiterOptions>(o => o.AddPolicy(…))` | `Api/DependencyInjection.cs` pierde `.AddConditionalWhatsAppRoutes()`; `RateLimitingExtensions` pierde la política y `RateLimitingOptions` sus dos propiedades; `FixedWindowByIp` pasa de `private` a `internal` para que la use el módulo |

**Por qué el orden no importa:** las opciones y las convenciones de MVC se componen con `Configure`; `IPhoneChannel` usa `TryAdd` + `Replace`; los enumerables usan `TryAddEnumerable`. Aun así, `Program.cs` llama al núcleo primero y al módulo después.

**Los adaptadores del webhook se registran siempre** (hallazgo 5 de la sección 2.3): son baratos y sin estado, y nadie los resuelve con el webhook apagado, porque sus rutas no existen. `WhatsAppSignatureValidator` lanza en el constructor sin los secretos (`WhatsAppSignatureValidator.cs:24-33`), y eso sigue siendo lo correcto: `ValidateOnBuild` valida los constructores sin instanciar, y `WhatsAppRegistrationTests` lo prueba con `ValidateOnBuild = true` en los dos escenarios apagados.

### 3.4 Límite transaccional, locks y colas

Nada cambia de lugar respecto del límite:

- **Los pedidos de código por teléfono** abren su límite en el servicio del módulo (`WhatsAppLoginCodeService` y `ProfileWhatsAppService`), con `OnSuccess`, igual que hoy `LoginCodeService` y `ProfileWhatsAppService`. Afuera y antes: el validador y el chequeo de "WhatsApp prendido" (que lanza `InvalidOperationException` con el mismo texto de hoy). Adentro y en este orden: parsear el número, el país, **el tope diario** (sin lock, igual que hoy: se mira antes del lock del destino), `LoginCodeIssuer.Issue*` (lock `login-code:` del número, límites, invalidar, agregar), buscar la cuenta, encolar y `MarkSent`.
- **Los cambios de número**: ver `IPhoneLinkParticipant`. `ConfirmPhoneLinkAsync` sigue con `OnAnyResult`.
- **Las invitaciones**: el adaptador encola adentro del límite y del lock `user-invitation:`, antes del commit, como hoy.
- **El estado de entrega**: una lectura, sin límite.
- **Las claves de los locks del módulo** (`whatsapp-contact:user:`, `whatsapp-contact:wa:`, `whatsapp-message:`) salen de `AdvisoryLockKeys` a `Infra/M/W/Persistence/WhatsAppLockKeys.cs`, **con el mismo texto** (el texto es el lock: cambiarlo dejaría de poner en fila a la versión anterior durante un despliegue). `AdvisoryLockExtensions` se queda en el núcleo y el módulo la usa.

### 3.5 Tabla uso → puerto

Cada uso del núcleo que el relevamiento listó, verificado en el código, y cómo se resuelve:

| Uso de hoy (archivo) | Qué hace | Resolución | Tanda |
|---|---|---|---|
| `LoginMethodsService` | `IWhatsAppAvailability` y `WhatsAppLoginOptions` para `whatsapp*` | `IPhoneChannel` | 4 |
| `UserContactLinker.EnsureCountryAllowed` | país de un número nuevo con `WhatsAppLoginOptions`, error `WhatsAppErrors.CountryNotSupported` | `IPhoneChannel.EnsureCanSendTo` | 4 |
| `IPhoneNumberParser.FromWhatsAppId` | leer un `wa_id` | sale del puerto; `WhatsAppIds.Parse(IPhoneNumberParser, string?)`, estático del módulo: dígitos, largo 8 a 15 (`LibPhoneNumberParser.cs:19-20,54-66`) y `parser.Parse(country: null, "+" + digits)`, que es el mismo camino que hoy (`Parse` con "+" llama a `Interpret(input, region: null)`) | 4 |
| `UserInvitationIssuer.Check` y `SendAsync` | reglas y envío por WhatsApp (`WhatsAppInvitationIssuer`) y por correo | `IInvitationChannel` (correo en el núcleo, WhatsApp en el módulo) | 5 |
| `UserInvitation` (Domain) | `ByWhatsApp`, `AttachWhatsAppMessage`, `MaxWaMessageIdLength = WhatsAppMessage.MaxWaMessageIdLength` | `UserInvitation.Send(…, consentConfirmed)`, `AttachProviderMessage`, `MaxProviderMessageIdLength = 256` (el mismo largo de columna) | 5 (la propiedad y la columna, en la 8) |
| `UserInvitationReader` + `LastInvitation` + `UserInvitationRow` | subconsulta a `WhatsAppMessages` y mapeo de `WhatsAppMessageStatus` | `IInvitationDeliveryStatusSource` + `IWhatsAppMessageReader` | 5 |
| `SignInCodeIssuer.RequestWhatsAppLoginCodeCoreAsync`, `LoginCodeService.RequestWhatsAppLoginCodeAsync`, `ILoginCodeService` | el pedido de código de ingreso por WhatsApp | al módulo: `IWhatsAppLoginCodeService` / `WhatsAppLoginCodeService` + `WhatsAppCodeIssuer.RequestSignInCodeAsync` | 6 |
| `DestinationCodeIssuer.EnsureWhatsAppEnabled` y `RequestPhoneCodeAsync` | el pedido de código del perfil para un número | al módulo: `WhatsAppCodeIssuer.EnsureEnabledForLink` y `RequestVerificationCodeAsync` | 6 |
| `LoginCodeIssuer.CheckDailyLimitAsync` | tope diario de los códigos por WhatsApp | al módulo: `WhatsAppCodeQuotaGuard.CheckAsync`, llamado por `WhatsAppCodeIssuer` antes de `LoginCodeIssuer.Issue*` | 6 |
| `PhoneNumberLinker` | locks de contactos, vincular y soltar el contacto | `IPhoneLinkParticipant` | 7 |
| `LoginCodeDestination.ForPhone` / `LoginCodeChannel.WhatsApp` | el canal del código | `LoginCodeChannel.Phone` + migración | 8 |
| `LoginCodeVerifier.PhoneIdentifier`, `LoginLinkVerifier` | `LoginMethod.WhatsAppCode` y `WhatsAppLink` | se quedan (decisión 5): no nombran tipos del módulo | — |
| `LoginLinkService` | `IPhoneNumberParser.Mask` | núcleo, sin cambios | — |
| `App/DependencyInjection`, `Infra/DependencyInjection`, `PersistenceRegistration`, `Api/DependencyInjection` | registran piezas de WhatsApp | los tres `AddWhatsApp*` (sección 3.3) | 2 y 3 |
| `ApplicationDbContext` | `DbSet<WhatsAppContact>`, `DbSet<WhatsAppMessage>` | se borran; el módulo usa `dbContext.Set<T>()` | 2 |
| `AdvisoryLockKeys` | tres claves de WhatsApp | `WhatsAppLockKeys` del módulo | 2 |
| `ProblemResponsesConvention.OwnProtocolControllers` | `typeof(WhatsAppWebhookController)` | atributo `[OwnProtocol]` en los tres controllers | 3 |
| `RateLimitingExtensions`/`RateLimitingOptions` | política `whatsapp-webhook` | `AddWhatsAppApi` | 3 |
| `LoginCodeController` (`POST whatsapp`), `MeController` (tres acciones) | rutas de WhatsApp en controllers del núcleo | `WhatsAppLoginCodeController` y `MeWhatsAppController` del módulo, mismas rutas y atributos | 3 |
| `UsersController` `DELETE {id}/whatsapp` | desvincular el número de una cuenta | se queda en el núcleo (administración de un dato de la cuenta; la ruta no cambia) | — |

Resultado: **cuatro puertos, ninguno sobra y no falta ninguno**. `IPhoneCodeSender` del relevamiento sobra (sección 2.3, punto 1).

### 3.6 Lo que queda en el núcleo con "WhatsApp" en el nombre, a propósito

- `UserInvitationChannel.WhatsApp` y `LoginMethod.WhatsAppCode`/`WhatsAppLink`: valores guardados y del JSON (decisiones 5 y 7).
- `DELETE /api/users/{id}/whatsapp` y `UsersController.UnlinkPhone` (decisión 7).
- `LoginMethodsResponse.WhatsApp*` con `[JsonPropertyName("whatsapp…")]` (decisión 7).
- Las seis claves de los `.resx` (decisión 6) y `UserInvitationErrors.ConsentRequired`/`NameRequired`, que son errores de la invitación y los usa el adaptador del módulo.
- Comentarios que citan el spec del ingreso con WhatsApp: no rompen nada. Los `<see cref>` a tipos del módulo, en cambio, **sí** rompen al borrar, y salen en la tanda 2.

### 3.7 Ganchos de módulo en los tests

Para que los tests del núcleo sigan compilando sin el módulo y, con el módulo, lo incluyan, se usa un solo patrón: **una clase `partial` del núcleo declara métodos `partial void` sin implementar, y el archivo del módulo, en `tests/…/Modules/WhatsApp/`, es otra parte de la misma clase (mismo namespace que la del núcleo, no el de su carpeta) que los implementa**. Sin el archivo, el compilador borra las llamadas. Se usa en:

| Clase (proyecto) | Ganchos | Lo que pone el módulo |
|---|---|---|
| `ApiFactory` (`IT/Support`) | `partial void ConfigureModuleSettings(IWebHostBuilder builder)` al final de la configuración; `partial void ConfigureModuleServices(IServiceCollection services)` dentro de `ConfigureTestServices` | las claves `WhatsApp:*` y `RateLimiting:WhatsAppWebhookPermitLimit` de hoy (`ApiFactory.cs:228,243-265`), el reemplazo de `IWhatsAppSendQueue` y el handler sin red del `HttpClient` (`:275-290`), la propiedad `WhatsApp` y las tres constantes `WhatsApp*` |
| `ExplicitRouteInventoryTests` (`IT/Contracts`) | `static partial void AddModuleRoutes(List<string> routes)` | las seis rutas del módulo |
| `DependencyInjectionTests`, `ApplicationHelpersTests` (`AUT`) | `static partial void AddModules(IServiceCollection services)`, llamado en **todo** test que registra `AddApplication()` y después recorre el ensamblado entero: `Every_application_validator_is_registered_and_resolves_in_a_scope` (escanea todos los validadores del ensamblado, también los del módulo, que desde la tanda 3 `AddApplication` ya no registra), `Every_application_dependency_is_registered`, `Application_services_helpers_and_the_request_validator_are_scoped` y `ApplicationHelpersTests.Helpers()` | `services.AddWhatsAppApplication()` |
| `TransactionBoundaryTests` (`Arch`) | `static partial void AddModuleLockKeyOwners(List<string>)`, `AddModuleLockKeyPrefixes(List<string>)`, `AddModuleBulkUpdateOwners(List<string>)` | `WhatsAppLockKeys`, los tres prefijos, `WhatsAppMessageRetentionRepository` |
| `IdentityBoundaryTests` (`Arch`) | ninguno: el módulo suma sus propios `[Fact]` en una parte de la clase, con acceso a los helpers privados | la regla de oro (`Services` del módulo llaman a `ISignInService` solo para `IsLockedOutAsync` y no abren sesión) |

`IDE0130` (namespace distinto de la carpeta) no está activo en `.editorconfig`, así que la parte del módulo compila sin advertencias; es la única excepción a "namespace = carpeta", y se documenta en `backend.md`.

---

## 4. Árbol final y mudanzas

### 4.1 Árbol final (solo lo nuevo, lo que se muda y lo que cambia)

```text
src/ArquitecturaBase.Domain/
  Authentication/LoginCodeChannel.cs            (Email, Phone)                           ← cambia (8)
  Authentication/LoginCodeDestination.cs        (ForPhone → Phone)                       ← cambia (8)
  Users/UserInvitation.cs                       (Send, AttachProviderMessage, ProviderMessageId) ← cambia (5, 8)
  Modules/WhatsApp/  AGENTS.md CLAUDE.md WhatsAppContact WhatsAppErrors WhatsAppMessage
                     WhatsAppMessageDirection WhatsAppMessageKind WhatsAppMessageStatus

src/ArquitecturaBase.Application/
  DependencyInjection.cs                        (sin nada de WhatsApp; TryAdd de los puertos del núcleo)
  Interfaces/Channels/  IPhoneChannel IPhoneLinkParticipant IInvitationChannel IInvitationDeliveryStatusSource  ← nuevos
  Channels/             DisabledPhoneChannel EmailInvitationChannel                                                ← nuevos
  Models/Users/InvitationCheck.cs                                                                                  ← nuevo
  Services/Auth/        (LoginCodeService, SignInCodeIssuer, DestinationCodeIssuer, LoginCodeIssuer, LoginMethodsService sin WhatsApp)
  Services/Users/       (PhoneNumberLinker, UserContactLinker, UserInvitationIssuer, UserQueryService con los puertos)
  Modules/WhatsApp/
    AGENTS.md CLAUDE.md
    WhatsAppApplicationRegistration.cs
    Channels/      WhatsAppPhoneChannel WhatsAppPhoneLinkParticipant WhatsAppInvitationChannel WhatsAppInvitationDeliveryStatusSource
    Configuration/ WhatsAppLoginOptions
    Interfaces/Services/     IWhatsAppLoginCodeService IProfileWhatsAppService IWhatsAppDeliveryService IWhatsAppInboundService
                             IWhatsAppWebhookPersistence IWhatsAppWebhookService
    Interfaces/Persistence/  IWhatsAppContactRepository IWhatsAppMessageRepository IWhatsAppMessageRetentionRepository IWhatsAppMessageReader
    Interfaces/Integrations/ IWhatsAppAvailability IWhatsAppInboundSignal IWhatsAppSendQueue IWhatsAppSignatureValidator
                             IWhatsAppWebhookReader IWhatsAppWebhookRetry
    Models/        BotButtons WhatsAppOutboundMessage WhatsAppWebhookBatch RequestWhatsAppLoginCodeRequest RequestWhatsAppLoginCodeResponse
                   RequestPhoneLinkCodeRequest RequestPhoneLinkCodeResponse ConfirmPhoneLinkRequest
    Validation/    RequestWhatsAppLoginCodeRequestValidator RequestPhoneLinkCodeRequestValidator ConfirmPhoneLinkRequestValidator
    Services/      WhatsAppLoginCodeService ProfileWhatsAppService WhatsAppCodeIssuer WhatsAppCodeQuotaGuard WhatsAppContactLinker
                   WhatsAppDeliveryService WhatsAppInboundService WhatsAppLinkIssuer WhatsAppReplyPolicy WhatsAppWebhookPersistence
                   WhatsAppWebhookService BotReply WhatsAppIds
    Resources/     Bot.resx Bot.en.resx BotTexts.cs

src/ArquitecturaBase.Infrastructure/
  DependencyInjection.cs                        (sin AddWhatsApp)
  Persistence/ApplicationDbContext.cs           (sin DbSet de WhatsApp)
  Persistence/PersistenceRegistration.cs        (sin los tres repositorios de WhatsApp)
  Persistence/Extensions/AdvisoryLockKeys.cs    (sin las claves whatsapp-*)
  Persistence/Readers/UserInvitationReader.cs   (sin la subconsulta)
  Persistence/Configurations/UserInvitationConfiguration.cs (ProviderMessageId)
  Persistence/Migrations/<ts>_LoginCodePhoneChannel.cs (+ .Designer.cs)          ← nueva (8)
  Persistence/Migrations/<ts>_UserInvitationProviderMessageId.cs (+ .Designer.cs) ← nueva (8)
  Phones/LibPhoneNumberParser.cs                (sin FromWhatsAppId)
  Modules/WhatsApp/
    AGENTS.md CLAUDE.md
    WhatsAppInfrastructureRegistration.cs       (era WhatsAppRegistration.cs)
    DisabledWhatsAppSendQueue IWhatsAppCloudClient WhatsAppAvailability WhatsAppCloudClient WhatsAppHealth WhatsAppHealthCheck
    WhatsAppInboundProcessor WhatsAppInboundSignal WhatsAppMessagePayload WhatsAppMessageRetentionOptions
    WhatsAppMessageRetentionService WhatsAppOptions WhatsAppOptionsValidator WhatsAppSendQueue WhatsAppSendResult
    WhatsAppSenderBackgroundService WhatsAppSignatureValidator WhatsAppWebhookOffNotice WhatsAppWebhookReader WhatsAppWebhookRetry
    Persistence/WhatsAppLockKeys.cs
    Persistence/Configurations/ WhatsAppContactConfiguration WhatsAppMessageConfiguration
    Persistence/Repositories/   WhatsAppContactRepository WhatsAppMessageRepository WhatsAppMessageRetentionRepository
    Persistence/Readers/        WhatsAppMessageReader

src/ArquitecturaBase.Api/
  Program.cs                                    (bloque del módulo y tres using)
  DependencyInjection.cs                        (sin AddConditionalWhatsAppRoutes)
  OpenApi/OwnProtocolAttribute.cs               ← nuevo; ProblemResponsesConvention lo lee
  RateLimiting/                                 (sin la política ni las opciones del webhook)
  Controllers/LoginCodeController.cs            (sin POST whatsapp)
  Controllers/MeController.cs                   (sin las tres acciones whatsapp ni IProfileWhatsAppService)
  Modules/WhatsApp/
    AGENTS.md CLAUDE.md
    WhatsAppApiRegistration.cs  WhatsAppWebhookRateLimitOptions.cs
    Controllers/ WhatsAppWebhookController WhatsAppLoginCodeController MeWhatsAppController
    Contracts/   RequestWhatsAppLoginCodeHttpRequest RequestPhoneLinkCodeHttpRequest ConfirmPhoneLinkHttpRequest
    Routing/     WhatsAppRouteAttribute ConditionalWhatsAppRouteConvention

tests/ArquitecturaBase.Domain.UnitTests/Modules/WhatsApp/        WhatsAppContactTests WhatsAppMessageTests
tests/ArquitecturaBase.Application.UnitTests/
  TestDoubles/Channels/  FakePhoneChannel RecordingPhoneLinkParticipant FakeInvitationChannel FakeInvitationDeliveryStatusSource  ← nuevos
  Channels/              DisabledPhoneChannelTests EmailInvitationChannelTests                                                  ← nuevos
  Services/Users/PhoneNumberLinkerTests.cs                                                                                       ← nuevo
  Modules/WhatsApp/      DependencyInjectionTests.WhatsApp.cs ApplicationHelpersTests.WhatsApp.cs WhatsAppApplicationRegistrationTests
                         Channels/ Configuration/ Models/ Resources/ Services/ TestDoubles/WhatsAppFakes.cs
tests/ArquitecturaBase.ArchitectureTests/
  Support/ModuleNamespaces.cs  ModuleBoundaryTests.cs                                                                           ← nuevos
  Modules/WhatsApp/      TransactionBoundaryTests.WhatsApp.cs IdentityBoundaryTests.WhatsApp.cs
tests/ArquitecturaBase.Api.IntegrationTests/
  Modules/WhatsApp/      ApiFactory.WhatsApp.cs ExplicitRouteInventoryTests.WhatsApp.cs los 20 archivos de WhatsApp/ y los que se parten
```

### 4.2 Mudanzas archivo por archivo

`mv` = `git mv` y solo el namespace (y los `using` de quien lo usa); `mv+` = `git mv` y además cambia algo (dicho en la columna); `nuevo`, `cambia` o `se parte` = edición. El namespace nuevo es siempre el de la carpeta: `ArquitecturaBase.<Proyecto>.Modules.WhatsApp[.<Subcarpeta>]`.

| Hoy | Nuevo | Namespace nuevo | Cómo | Tanda |
|---|---|---|---|---|
| `Dom/WhatsApp/*.cs` (6) y `AGENTS.md`, `CLAUDE.md` | `Dom/M/W/` | `…Domain.Modules.WhatsApp` | `mv` (el `AGENTS.md` ajusta la profundidad del enlace) | 2 |
| `App/Services/WhatsApp/*.cs` (son 9: los 8 que no son `WhatsAppInvitationIssuer`) | `App/M/W/Services/` | `…Application.Modules.WhatsApp.Services` | `mv`; los `cref` relativos al núcleo (`Users.UserInvitationIssuer` en `WhatsAppInvitationIssuer.cs:14`, `Users.PhoneNumberLinker.LockAsync` en `WhatsAppContactLinker.cs:71`) pasan a nombre completo, o dan CS1574 con el namespace nuevo | 2 |
| `App/Services/WhatsApp/WhatsAppInvitationIssuer.cs` | `App/M/W/Services/` y en la 5 `App/M/W/Channels/WhatsAppInvitationChannel.cs` | `…Modules.WhatsApp.Services`, después `.Channels` | `mv`; en la 5 `mv+` (implementa `IInvitationChannel`) | 2, 5 |
| `App/Services/Users/ProfileWhatsAppService.cs` | `App/M/W/Services/` | `…Modules.WhatsApp.Services` | `mv`; cambia en la 6 (usa `WhatsAppCodeIssuer`) y la 7 (`ReleasePhoneAsync`) | 2 |
| `App/Interfaces/Services/IProfileWhatsAppService.cs`, `IWhatsAppDeliveryService.cs`, `IWhatsAppInboundService.cs`, `IWhatsAppWebhookPersistence.cs`, `IWhatsAppWebhookService.cs` | `App/M/W/Interfaces/Services/` | `…Modules.WhatsApp.Interfaces.Services` | `mv` | 2 |
| `App/Interfaces/Persistence/IWhatsAppContactRepository.cs`, `IWhatsAppMessageRepository.cs`, `IWhatsAppMessageRetentionRepository.cs` | `App/M/W/Interfaces/Persistence/` | `…Modules.WhatsApp.Interfaces.Persistence` | `mv` | 2 |
| `App/Interfaces/Integrations/WhatsApp/*.cs` (6) y sus `AGENTS.md`/`CLAUDE.md` | `App/M/W/Interfaces/Integrations/` | `…Modules.WhatsApp.Interfaces.Integrations` | `mv`; los `AGENTS.md` de subcarpeta se borran (queda uno en la raíz del módulo) | 2 |
| `App/Models/WhatsApp/*.cs` (3) | `App/M/W/Models/` | `…Modules.WhatsApp.Models` | `mv` | 2 |
| `App/Models/Auth/RequestWhatsAppLoginCodeRequest.cs`, `RequestWhatsAppLoginCodeResponse.cs` | `App/M/W/Models/` | ídem | `mv` | 2 |
| `App/Models/Users/RequestPhoneLinkCodeRequest.cs`, `RequestPhoneLinkCodeResponse.cs`, `ConfirmPhoneLinkRequest.cs` | `App/M/W/Models/` | ídem | `mv` | 2 |
| `App/Validation/Auth/RequestWhatsAppLoginCodeRequestValidator.cs`, `App/Validation/Users/RequestPhoneLinkCodeRequestValidator.cs`, `ConfirmPhoneLinkRequestValidator.cs` | `App/M/W/Validation/` | `…Modules.WhatsApp.Validation` | `mv` | 2 |
| `App/Configuration/Auth/WhatsAppLoginOptions.cs` | `App/M/W/Configuration/` | `…Modules.WhatsApp.Configuration` | `mv` | 2 |
| `App/Resources/Bot.resx`, `Bot.en.resx`, `BotTexts.cs` | `App/M/W/Resources/` | `…Modules.WhatsApp.Resources` | `mv+`: el nombre del recurso de `BotTexts.ResourceManager` pasa a `"ArquitecturaBase.Application.Modules.WhatsApp.Resources.Bot"` (el SDK lo arma con la carpeta) | 2 |
| `App/DependencyInjection.cs` | — | — | cambia: en la 2 sigue registrando lo mudado (violación conocida); en la 3 pierde todo lo del módulo | 2, 3 |
| — | `App/M/W/WhatsAppApplicationRegistration.cs` | `…Application.Modules.WhatsApp` | nuevo | 3 |
| — | `App/Interfaces/Channels/*.cs` (4), `App/Channels/*.cs` (2), `App/Models/Users/InvitationCheck.cs` | `…Interfaces.Channels`, `…Application.Channels`, `…Models.Users` | nuevos | 4, 5, 7 |
| — | `App/M/W/Channels/*.cs` (3 nuevos + el mudado) | `…Modules.WhatsApp.Channels` | nuevos | 4, 5, 7 |
| — | `App/M/W/Services/WhatsAppLoginCodeService.cs`, `WhatsAppCodeIssuer.cs`, `WhatsAppCodeQuotaGuard.cs`, `WhatsAppIds.cs`; `App/M/W/Interfaces/Services/IWhatsAppLoginCodeService.cs`; `App/M/W/Interfaces/Persistence/IWhatsAppMessageReader.cs` | ídem | nuevos | 4, 5, 6 |
| `Infra/WhatsApp/*.cs` (21) y `AGENTS.md`, `CLAUDE.md` | `Infra/M/W/` | `…Infrastructure.Modules.WhatsApp` | `mv`; `WhatsAppRegistration.cs` se muda **ya en la 2** con su nombre final, `WhatsAppInfrastructureRegistration.cs` (clase renombrada, todavía `internal` y con el método `AddWhatsApp`), porque desde la 2 registra los tres repositorios y `PersistenceRegistrationTests` (tanda 1) solo acepta como dueño a `<M>InfrastructureRegistration`; en la 3 pasa a `public` y el método a `AddWhatsAppInfrastructure` | 2, 3 |
| `Infra/Persistence/Configurations/WhatsAppContactConfiguration.cs`, `WhatsAppMessageConfiguration.cs` | `Infra/M/W/Persistence/Configurations/` | `…Modules.WhatsApp.Persistence.Configurations` | `mv` | 2 |
| `Infra/Persistence/Repositories/WhatsAppContactRepository.cs`, `WhatsAppMessageRepository.cs`, `WhatsAppMessageRetentionRepository.cs` | `Infra/M/W/Persistence/Repositories/` | `…Modules.WhatsApp.Persistence.Repositories` | `mv+`: `dbContext.WhatsAppContacts` → `dbContext.Set<WhatsAppContact>()` (8 usos), `WhatsAppMessages` → `Set<WhatsAppMessage>()` (5) | 2 |
| `Infra/Persistence/Extensions/AdvisoryLockKeys.cs` | se parte: las tres claves `whatsapp-*` a `Infra/M/W/Persistence/WhatsAppLockKeys.cs` | `…Modules.WhatsApp.Persistence` | se parte (texto idéntico) | 2 |
| `Infra/Persistence/ApplicationDbContext.cs` | — | — | cambia: sin los dos `DbSet` ni el `using` | 2 |
| `Infra/Persistence/PersistenceRegistration.cs` | — | — | cambia: las líneas 45-47 pasan al registro del módulo | 2 |
| `Infra/Persistence/Readers/UserInvitationReader.cs` | — | — | cambia: en la 2, `dbContext.Set<WhatsAppMessage>()` (violación conocida); en la 5, sin subconsulta | 2, 5 |
| — | `Infra/M/W/Persistence/Readers/WhatsAppMessageReader.cs` | `…Modules.WhatsApp.Persistence.Readers` | nuevo | 5 |
| `Infra/Persistence/Configurations/UserInvitationConfiguration.cs` | — | — | cambia: `ProviderMessageId`, `UserInvitation.MaxProviderMessageIdLength` | 5, 8 |
| `Infra/Phones/LibPhoneNumberParser.cs` | — | — | cambia: sin `FromWhatsAppId` ni sus dos constantes | 4 |
| — | `Infra/Persistence/Migrations/<ts>_LoginCodePhoneChannel.*`, `<ts>_UserInvitationProviderMessageId.*`, snapshot | — | nuevas (generadas) | 8 |
| `Api/Controllers/WhatsAppWebhookController.cs` | `Api/M/W/Controllers/` | `…Api.Modules.WhatsApp.Controllers` | `mv`; en la 3 suma `[OwnProtocol]` y usa la constante de la política del módulo | 2, 3 |
| `Api/Routing/*.cs` (2) y `AGENTS.md`, `CLAUDE.md` | `Api/M/W/Routing/` | `…Api.Modules.WhatsApp.Routing` | `mv`; la extensión `AddConditionalWhatsAppRoutes` se borra en la 3 (la reemplaza `AddWhatsAppApi`) | 2, 3 |
| `Api/Contracts/Auth/RequestWhatsAppLoginCodeHttpRequest.cs`, `Api/Contracts/Users/RequestPhoneLinkCodeHttpRequest.cs`, `ConfirmPhoneLinkHttpRequest.cs` | `Api/M/W/Contracts/` | `…Api.Modules.WhatsApp.Contracts` | `mv` | 2 |
| `Api/Controllers/LoginCodeController.cs` (acción `RequestWhatsAppLoginCode`) | `Api/M/W/Controllers/WhatsAppLoginCodeController.cs` | ídem | se parte: `[Route("account/login-code/whatsapp")]`, `[Tags("Account")]`, `[ApiController]`, `[HttpPost]` y los mismos atributos de la acción | 3 |
| `Api/Controllers/MeController.cs` (tres acciones `whatsapp`) | `Api/M/W/Controllers/MeWhatsAppController.cs` | ídem | se parte: `[Route("api/me/whatsapp")]`, `[Tags("Users")]`, `[Authorize]`, `[ProducesProblem(404)]` de clase, `[HttpPost("code")]`, `[HttpPut]`, `[HttpDelete]`, con los mismos atributos por acción | 3 |
| `Api/DependencyInjection.cs`, `Api/RateLimiting/*`, `Api/OpenApi/ProblemResponsesConvention.cs`, `ConnectController.cs`, `ExternalLoginController.cs` | — | — | cambian (sección 3.3); `[OwnProtocol]` en los dos controllers | 3 |
| — | `Api/OpenApi/OwnProtocolAttribute.cs`, `Api/M/W/WhatsAppApiRegistration.cs`, `Api/M/W/WhatsAppWebhookRateLimitOptions.cs` | `…Api.OpenApi`, `…Api.Modules.WhatsApp` | nuevos | 3 |
| `Api/Program.cs` | — | — | cambia: el bloque y los tres `using` | 3 |
| `Api/appsettings.json`, `appsettings.Development.json` | — | — | **no cambian**: las claves `WhatsApp:*` y `RateLimiting:WhatsAppWebhook*` se leen igual | — |
| `DUT/WhatsApp/*.cs` (2) | `DUT/M/W/` | `…Domain.UnitTests.Modules.WhatsApp` | `mv` | 2 |
| `AUT/Services/WhatsApp/*.cs` (6) | `AUT/M/W/Services/` | `…Application.UnitTests.Modules.WhatsApp.Services` | `mv` | 2 |
| `AUT/Services/Auth/RequestWhatsAppLoginCodeServiceTests.cs` | `AUT/M/W/Services/WhatsAppLoginCodeServiceTests.cs` | ídem | `mv` en la 2; `mv+` en la 6 (arma `WhatsAppLoginCodeService`) | 2, 6 |
| `AUT/Services/Users/ProfileWhatsAppServiceTests.cs` | `AUT/M/W/Services/` | ídem | `mv` | 2 |
| `AUT/Configuration/Auth/WhatsAppLoginOptionsTests.cs`, `AUT/Models/WhatsApp/WhatsAppOutboundMessageTests.cs`, `AUT/TestDoubles/WhatsApp/WhatsAppFakes.cs` | `AUT/M/W/Configuration/`, `…/Models/`, `…/TestDoubles/` | ídem | `mv` | 2 |
| `IT/WhatsApp/*.cs` (20) | `IT/M/W/` | `…IntegrationTests.Modules.WhatsApp` | `mv` | 2 |
| `IT/Auth/WhatsAppLoginCodeTests.cs`, `IT/Contracts/MvcWhatsAppRouteConventionTests.cs`, `WhatsAppHttpContractsTests.cs`, `WhatsAppRouteContractsTests.cs`, `IT/Users/MeWhatsAppEndpointsTests.cs`, `IT/Support/CapturingWhatsAppSendQueue.cs` | `IT/M/W/` | ídem | `mv` | 2 |
| Los archivos mixtos de `IT`, `AUT` y `Arch` (lista en la tanda 9) | se parten | — | los tests que necesitan el módulo pasan a `M/W` | 9 |

---

## 5. Tandas

Cada tanda es uno o más commits chicos y termina así (el **criterio de terminado común**, que no se repite abajo):

1. `dotnet build ArquitecturaBase.slnx` sin advertencias. Si el AppHost no compila por la Aspire CLI (pasó en la nube), `dotnet build src/ArquitecturaBase.Api` y los cuatro `.csproj` de tests, y se anota.
2. Los cuatro proyectos en verde, con Docker: `dotnet test` (o uno por uno con `dotnet test --project tests/<P>/<P>.csproj`). Se anotan los números (hoy: Domain 167, Application 523, Architecture 93, integración 1015).
3. `dotnet ef migrations has-pending-model-changes` con los argumentos de la [guía de la migración](../guides/migracion.md) responde "No changes" (salvo en la 8, que agrega sus migraciones).
4. `python3 <scratchpad>/checklinks.py` sobre los `.md` tocados: 0 rotos.
5. La documentación que nombra lo tocado, en el mismo commit (lo dice cada tanda).
6. En el plan maestro, la casilla de la tanda con su commit.

**Los tests que no se tocan y tienen que seguir en verde en todas las tandas** (la red de seguridad): `MeWhatsAppEndpointsTests` (los cinco de concurrencia con el bot), `UnlinkUserPhoneEndpointTests`, `UpdateUserContactTests`, `UserAccessServicePhoneTests`, `WhatsAppBotTests`, `WhatsAppWebhookTests`, `WhatsAppLoginCodeTests`, `UserInvitationEndpointsTests`, `LoginMethodsControllerTests`, `ExplicitRouteInventoryTests`, `OpenApiTests`, `MigrationsTests` y los `*LogPrivacyTests`. Si uno de estos cambia una aserción, se para y se explica en el commit por qué (las únicas previstas están nombradas en su tanda).

### Tanda 1. Preparar los tests: namespaces de módulo, ganchos y la frontera (S)

**Objetivo.** Antes de mover nada, que los tests de arquitectura sepan que `X.Modules.<M>.Y` es la misma capa que `X.Y`, que los tests del núcleo tengan sus ganchos de módulo, y que exista el test de la frontera. Todo en verde sin cambiar `src`.

**Archivos.**
- Nuevo `Arch/Support/ModuleNamespaces.cs`.
- Cambian `Arch/TypeNamespaceExtensions.cs`, `ControllerServiceRepositoryTests.cs`, `EntityConfigurationTests.cs`, `PersistenceRegistrationTests.cs`, `PersistenceNamingTests.cs`, `TransactionBoundaryTests.cs`, `IdentityBoundaryTests.cs` (`partial`), `LayerDependencyTests.cs` (sin cambios de regla; se revisa).
- Nuevo `Arch/ModuleBoundaryTests.cs`.
- `AUT/ApplicationHelpersTests.cs` y `AUT/DependencyInjectionTests.cs` pasan a `partial` con el gancho `AddModules`.
- `IT/Support/ApiFactory.cs` y `IT/Contracts/ExplicitRouteInventoryTests.cs` pasan a `partial` con sus ganchos, todavía sin implementación (las rutas y la configuración de WhatsApp siguen donde están hasta la tanda 9).

**Pasos.**
1. `ModuleNamespaces` (internal static):
   ```csharp
   // "ArquitecturaBase.Application.Modules.Control.Services.X" → "ArquitecturaBase.Application.Services.X"
   public static string Canonical(string name) => ModuleSegment.Replace(name, "ArquitecturaBase.${layer}", 1);
   public static string? ModuleOf(string name);        // el nombre del módulo ("Control") o null
   public static string Matching(string canonicalNamespace); // regex para NetArchTest: ^ArquitecturaBase\.Layer(\.Modules\.[A-Z]\w*)?\.Rest(\.|$)
   private static readonly Regex ModuleSegment =
       new(@"^ArquitecturaBase\.(?<layer>Domain|Application|Infrastructure|Api)\.Modules\.[A-Z][A-Za-z0-9]*(?=\.|$)");
   ```
2. `ResidesIn` compara `ModuleNamespaces.Canonical(type.Namespace)` en lugar de `type.Namespace`. Con eso ven el módulo, sin más cambios: `ApplicationServicesTests`, `ServiceDependencyLimitTests`, `ServiceOutputNamingTests`, `ControllerInputContractTests`, `ApiRequestContextTests`, `EntityConfigurationTests` (ya usa `ResidesIn`) y `UseCaseEntryPoints`. **`ErrorDeclarationTests` no usa `ResidesIn`**: su `IsInDomainArea` toma el primer segmento después de `ArquitecturaBase.Domain.` (`ErrorDeclarationTests.cs:173-185`), así que hoy aceptaría `Domain.Modules.WhatsApp.WhatsAppErrors` con el área `Modules` y no se rompe; pero también aceptaría `Domain.Modules.WhatsApp.Common.XErrors`. **No** se le aplica `Canonical` sin más: el namespace canónico de `Domain.Modules.WhatsApp` es la raíz de Domain, y `WhatsAppErrors` fallaría por área. La regla pasa a ser: si `ModuleOf(namespace)` no es null, el área es el módulo (`Domain.Modules.<M>` es un área, y debajo valen las mismas `NonAreaFolders`); si no, como hoy. Controles nuevos en `ErrorDeclarationControls.cs`: `ArquitecturaBase.Domain.Modules.Control.ControlErrors` pasa, `ArquitecturaBase.Domain.Modules.Control.Common.ControlErrors` falla por área. `backend.md` dice que en un módulo los errores van en la raíz de `Domain/Modules/<M>/`.
3. Donde la regla compara nombres del IL por prefijo o por igualdad, aplicar `Canonical` al dueño y al tipo: `PersistenceNamingTests` (contratos con `Namespace == …Interfaces.Persistence` y dueños de `AsNoTracking` bajo `…Persistence.Readers`), `EntityConfigurationTests` (`ConfigurationsNamespace`), `PersistenceRegistrationTests` (namespaces de repositorios y lectores).
4. `ControllerServiceRepositoryTests`: donde usa NetArchTest `.ResideInNamespace(controllersNamespace)`, pasar a `.ResideInNamespaceMatching(ModuleNamespaces.Matching("ArquitecturaBase.Api.Controllers"))`; sumar `…Interfaces.Channels` a lo prohibido para un controller; la comparación exacta con `Interfaces.Services` usa `Canonical` (las dos de `Every_controller_injects_an_application_service_interface`, `ControllerServiceRepositoryTests.cs:79-83`, o el controller de login del módulo falla por inyectar `…Modules.WhatsApp.Interfaces.Services`). **Ojo con lo prohibido:** `HaveDependencyOnAny` de NetArchTest 1.3.2 compara por prefijo del nombre y no acepta un patrón, así que `…Application.Interfaces.Persistence` no ve `…Application.Modules.WhatsApp.Interfaces.Persistence`. La lista de prohibidos se arma con el núcleo más, por cada módulo que aparezca en los namespaces del ensamblado de Application (`ModuleOf`), `…Application.Modules.<M>.Interfaces.{Persistence,Integrations,Channels}`; el control: un controller de prueba en `…Api.Modules.Control.Controllers` que depende de un tipo en `…Application.Modules.Control.Interfaces.Persistence` falla.
5. `PersistenceRegistrationTests`: dueños permitidos = `PersistenceRegistration` **o** `ArquitecturaBase.Infrastructure.Modules.<M>.<M>InfrastructureRegistration` para tipos de su mismo módulo. Casos de control nuevos con `CallSites.TypeUse` armados a mano, con módulos inventados (`Control`, `Sms`): el registro de un módulo nombra un repositorio suyo (pasa), uno del núcleo (falla) y uno de otro módulo (falla).
6. `TransactionBoundaryTests` pasa a `partial` y declara `AddModuleLockKeyOwners`, `AddModuleLockKeyPrefixes`, `AddModuleBulkUpdateOwners`; `Advisory_lock_sql_and_keys_live_in_one_place` y `Bulk_updates_and_deletes_only_where_documented` arman sus listas con el núcleo más lo que agregan los ganchos. En esta tanda las listas del núcleo todavía incluyen lo de WhatsApp; pasan al gancho en la tanda 2.
7. `IdentityBoundaryTests` pasa a `partial` (sin cambiar reglas).
8. `ModuleBoundaryTests` (detalle en la sección 6): la regla del fuente con su lista `KnownViolations` vacía (todavía no hay `Modules`), la del `DbContext` y la de los registros por capa, con sus casos de control. Dos partes de la sección 6 **no se pueden afirmar sobre el código real hasta la tanda 3**, porque en la 2 ya existen las carpetas `Modules/WhatsApp` pero todavía no el bloque de `Program.cs` ni `WhatsAppApplicationRegistration`/`WhatsAppApiRegistration`: la condición "si hay `src/*/Modules/*`, `Program.cs` coincide" (6.1) y la regla de los registros por capa (6.3) sobre los ensamblados reales. En la tanda 1 entran con sus controles y con una lista `KnownMissingRegistrations` (vacía), que la tanda 2 carga con `"WhatsApp"` y la 3 vacía y borra; se compara con `Assert.Equal`, como `KnownViolations`, y mientras el módulo está en ella se saltean la condición de `Program.cs` y la 6.3 para ese módulo.
9. Los ganchos de `AUT` e `IT` (declarados, sin implementar).
10. `AUT` no ve `ModuleNamespaces` (vive en `Arch`): `DependencyInjectionTests` y `ApplicationHelpersTests` llevan su propia copia chica de `Canonical` (un `internal static` en `AUT/Support/`, con sus tres casos de control). `DependencyInjectionTests.IsUnderNamespace` y `ApplicationServiceInterfaces` la usan, así `Every_application_dependency_is_registered` sigue las dependencias de `…Modules.<M>.Services` y exige un registro único y scoped de cada contrato de `…Modules.<M>.Interfaces.Services`; sin esto, la tanda 6 no podría decir que el test "ve los tres nuevos". `IsTrackedDependency` suma `…Interfaces.Channels`, así un puerto sin registrar (por ejemplo, sin el `TryAddSingleton` de `IPhoneChannel`) también falla acá y no recién en integración. En `ApplicationHelpersTests`, en cambio, `Canonical` sin más haría de `…Modules.WhatsApp.Services` la raíz de `Services` y fallaría a todos los helpers del módulo: ahí la regla es la de la tabla 6.4 (la carpeta `Modules/<M>/Services` cuenta como carpeta de área), con su control.

**Tests.** Rojo primero: los casos de control de `ModuleBoundaryTests` y de `ModuleNamespaces` (`Canonical`, `ModuleOf`, `Matching` sobre cadenas armadas a mano) se escriben antes del detector. Todos los existentes, en verde sin tocar aserciones.

**Riesgos.** Que `Canonical` haga que una regla deje de mirar algo: cada test que cambia conserva su "el detector ve al menos un caso conocido" (los tienen casi todos, por ejemplo `Assert.Contains(AdvisoryLockExtensions, sqlOwners)`). Se corre `Arch` antes y después y se compara el número de tests.

**Terminado.** El común. Solo cambian tests. Commit `test: los tests de arquitectura reconocen Modules/<M> y prueban la frontera del núcleo`.

### Tanda 2. La mudanza mecánica (L)

**Objetivo.** Mover a `Modules/WhatsApp` todo lo que es 100 % WhatsApp, con `git mv` para no perder la historia, sin cambiar comportamiento. Donde el núcleo todavía llama al módulo, queda anotado en `KnownViolations`, y cada tanda siguiente borra sus renglones.

**Archivos.** Las filas de la tabla 4.2 marcadas con tanda 2.

**Pasos.**
1. Crear las carpetas y hacer los `git mv` de la tabla, proyecto por proyecto (Domain, Application, Infrastructure, Api, y los tests de cada uno).
2. Cambiar el `namespace` de cada archivo movido al de su carpeta nueva. Arreglar los `using` de todo el repo (`dotnet build` los señala; `IDE0005` señala los que sobran).
3. **`<see cref>` del núcleo a tipos mudados:** pasarlos a `<c>…</c>` (hoy son cinco: `ProfileService.cs:21`, `UserInvitationIssuer.cs:21` y `PhoneNumberLinker.cs:20,39,123`; los dos cruzados entre `IWhatsAppContactRepository` e `IWhatsAppMessageRepository` se mudan juntos y quedan como `cref`). Así la regla del fuente no los cuenta como violación. Al revés, dos `cref` **del módulo al núcleo** usan un nombre relativo que deja de resolverse con el namespace nuevo y rompen el build (CS1574, con `GenerateDocumentationFile` y `TreatWarningsAsErrors`): `<see cref="Users.UserInvitationIssuer"/>` (`WhatsAppInvitationIssuer.cs:14`) y `<see cref="Users.PhoneNumberLinker.LockAsync"/>` (`WhatsAppContactLinker.cs:71`). Pasan a nombre completo (`ArquitecturaBase.Application.Services.Users.…`), como ya hace `WhatsAppReplyPolicy.cs:73`.
4. `BotTexts`: el nombre del recurso a `"ArquitecturaBase.Application.Modules.WhatsApp.Resources.Bot"`.
5. `ApplicationDbContext`: borrar los dos `DbSet` y el `using`. En los tres repositorios mudados, `Set<T>()`. En los tests, `db.WhatsAppMessages`/`db.WhatsAppContacts` → `db.Set<WhatsAppMessage>()`/`Set<WhatsAppContact>()` (23 usos en 9 archivos de `IT`; `grep -rno "\.WhatsAppMessages\b\|\.WhatsAppContacts\b" tests --include=*.cs`).
6. `AdvisoryLockKeys`: las tres claves de WhatsApp pasan a `WhatsAppLockKeys` (`internal static`, mismo texto, mismos comentarios). Los repositorios del módulo y `IT/Persistence/AdvisoryLockKeysTests` (`WhatsApp_keys_keep_the_text_that_meta_sends`) las leen de ahí. En `TransactionBoundaryTests`, los tres prefijos, `WhatsAppLockKeys` y la excepción de `ExecuteUpdate` pasan al archivo del módulo `Arch/M/W/TransactionBoundaryTests.WhatsApp.cs`, que implementa los tres ganchos.
7. `PersistenceRegistration.cs:45-47` → al método de registro mudado, **al principio y fuera del `if (!enabled) return`** (hoy se registran siempre: la retención corre con WhatsApp apagado). La clase se muda ya con su nombre final, `WhatsAppInfrastructureRegistration` (tabla 4.2): `PersistenceRegistrationTests`, desde la tanda 1, solo acepta a `ArquitecturaBase.Infrastructure.Modules.<M>.<M>InfrastructureRegistration` como dueño de un repositorio del módulo, y con el nombre de hoy (`WhatsAppRegistration`) la tanda quedaría en rojo. En esta tanda sigue `internal`, el método sigue siendo `AddWhatsApp` y lo sigue llamando `Infra/DependencyInjection`; los seis archivos de `IT` que nombran `WhatsAppRegistration.HttpClientName` o `.MissingPublicOriginMessage` (`ApiFactory`, `UserInvitationEndpointsTests` y cuatro de `IT/WhatsApp`) cambian solo el nombre de la clase. En `ModuleBoundaryTests`, `KnownMissingRegistrations` queda en `["WhatsApp"]` (paso 8 de la tanda 1).
8. `IdentityBoundaryTests`: `WhatsAppServices` pasa a `"ArquitecturaBase.Application.Modules.WhatsApp.Services."` y las dos aserciones de la regla de oro se mudan a `Arch/M/W/IdentityBoundaryTests.WhatsApp.cs` como `[Fact]` propios.
9. Mover y ajustar los `AGENTS.md`/`CLAUDE.md` (sección 8). Uno por raíz de módulo en cada proyecto; se borran los de `Interfaces/Integrations/WhatsApp`, `Models/WhatsApp` y `Services/WhatsApp` después de pasar su segunda línea al de la raíz.
10. Correr `ModuleBoundaryTests`: falla con la lista de archivos del núcleo que todavía nombran el módulo. Cargar `KnownViolations` con **exactamente** esa lista, cada archivo con la tanda que lo saca. Se espera esta (19):

    | Archivo | Sale en |
    |---|---|
    | `App/DependencyInjection.cs`, `Infra/DependencyInjection.cs`, `Api/DependencyInjection.cs`, `Api/OpenApi/ProblemResponsesConvention.cs`, `Api/Controllers/LoginCodeController.cs`, `Api/Controllers/MeController.cs` | 3 |
    | `App/Services/Auth/LoginMethodsService.cs`, `App/Services/Users/UserContactLinker.cs` | 4 |
    | `App/Services/Users/UserInvitationIssuer.cs`, `App/Models/Users/LastInvitation.cs`, `App/Models/Users/UserInvitationRow.cs`, `Dom/Users/UserInvitation.cs`, `Infra/Persistence/Readers/UserInvitationReader.cs` | 5 |
    | `App/Services/Auth/SignInCodeIssuer.cs`, `LoginCodeIssuer.cs`, `LoginCodeService.cs`, `DestinationCodeIssuer.cs`, `App/Interfaces/Services/ILoginCodeService.cs` | 6 |
    | `App/Services/Users/PhoneNumberLinker.cs` | 7 |

    Si aparece otro archivo, no se agrega a la lista sin pensar: se mira por qué y, si es una referencia que no hace falta (un `cref`, un `using` sobrante), se arregla ahí.
11. `dotnet ef migrations has-pending-model-changes`: el namespace de las entidades cambió, pero la comparación es sobre tablas y columnas. Se espera "No changes". Si informara cambios, se genera una migración `WhatsAppModuleNamespaces` y se verifica que `Up` y `Down` estén vacíos (solo cambia el snapshot); si no están vacíos, se para.

**Tests.** No hay rojo propio: es una mudanza. El rojo es el paso 10 (la regla nueva viendo lo que falta). Todos los existentes en verde **sin tocar aserciones**; solo cambian `using`, namespaces y `db.Set<T>()`.

**Riesgos.**
- Un `cref` que queda apuntando a un tipo movido (CS1574 rompe el build: lo ve el compilador).
- El recurso del bot con otro nombre: `BotReplyTests` y `ResourceParityTests.Bot_texts_have_the_same_keys_in_spanish_and_english` fallarían (lanzan "Missing bot text").
- Los validadores mudados siguen registrados por el escaneo del ensamblado (`AddApplicationValidatorsFromAssembly`): no cambia nada todavía.
- Diff enorme: el commit es solo de mudanza (`refactor:`), y el revisor mira `git diff -M --stat` (tienen que ser todos renombres con cambios de pocas líneas).

**Docs.** `whatsapp.md` (las rutas del código en su primer párrafo), el árbol de `backend.md` (carpetas `Modules/WhatsApp`), `AGENTS.md` "Dónde va cada cosa" (fila nueva: "Módulo opcional (hoy WhatsApp)" → `<Proyecto>/Modules/<Módulo>/`), `despliegue.md` (rutas a `WhatsAppRegistration.cs`, líneas 70 y 77).

**Terminado.** El común. Commit `refactor: WhatsApp se muda a Modules/WhatsApp en los cuatro proyectos`.

### Tanda 3. Un registro por capa y el borde HTTP del módulo (M)

**Objetivo.** Que el núcleo deje de registrar el módulo y que `Program.cs` tenga el bloque de tres líneas. Que las rutas de WhatsApp vivan en controllers del módulo, con los mismos verbos, rutas y atributos. Que la política de rate limit, la convención de rutas y la de OpenAPI dejen de nombrar tipos del módulo.

**Archivos.** `App/DependencyInjection.cs`, `App/M/W/WhatsAppApplicationRegistration.cs` (nuevo), `Infra/DependencyInjection.cs`, `Infra/M/W/WhatsAppRegistration.cs` → `WhatsAppInfrastructureRegistration.cs`, `Api/DependencyInjection.cs`, `Api/RateLimiting/RateLimitingExtensions.cs`, `RateLimitingOptions.cs`, `Api/OpenApi/ProblemResponsesConvention.cs`, `Api/OpenApi/OwnProtocolAttribute.cs` (nuevo), `Api/Controllers/{LoginCodeController,MeController,ConnectController,ExternalLoginController}.cs`, `Api/M/W/{WhatsAppApiRegistration,WhatsAppWebhookRateLimitOptions}.cs` y `Controllers/{WhatsAppLoginCodeController,MeWhatsAppController,WhatsAppWebhookController}.cs`, `Api/M/W/Routing/ConditionalWhatsAppRouteConvention.cs`, `Api/Program.cs`; tests: `AUT/M/W/{DependencyInjectionTests.WhatsApp.cs,ApplicationHelpersTests.WhatsApp.cs}`, `IT/M/W/WhatsAppRegistrationTests.cs`, y las partes del núcleo `AUT/DependencyInjectionTests.cs` (sus dos llamadas a `AddWhatsAppWebhookApplicationServices`, `:89` y `:116`) y `AUT/ApplicationHelpersTests.cs` (la llamada de `:91` y el `<see cref="DependencyInjection.AddWhatsAppWebhookApplicationServices"/>` de `:12`, que con el método borrado es CS1574 y rompe el build).

**Pasos.**
1. `WhatsAppApplicationRegistration.AddWhatsAppApplication()` con lo de la sección 3.3 (en esta tanda, los puertos todavía no existen: registra opciones, helpers, servicios y validadores del módulo). `AddApplication` pierde esos registros; se borra `AddWhatsAppWebhookApplicationServices`.
2. `AddApplication` deja afuera los validadores cuyo namespace empiece con `ArquitecturaBase.Application.Modules.` (un `filter` en su propia llamada; `AddApplicationValidatorsFromAssembly` no cambia, porque `ApiFactory.cs:299` la usa con el ensamblado de tests); el módulo registra los suyos con el filtro inverso.
3. `WhatsAppInfrastructureRegistration` (ya renombrada en la 2) pasa a `public static`, y su método a `AddWhatsAppInfrastructure(this IServiceCollection, IConfiguration)`. Los adaptadores del webhook se registran siempre (sección 3.3); la línea 121 (`AddWhatsAppWebhookApplicationServices`) se borra. `Infra/DependencyInjection` deja de llamar a `AddWhatsApp`. `HttpClientName` y `MissingPublicOriginMessage` conservan el nombre y el texto (los usan tests).
4. `OwnProtocolAttribute` (`[AttributeUsage(AttributeTargets.Class)]`, `public sealed`) y `ProblemResponsesConvention` excluye los controllers que lo llevan. Se lo ponen `ConnectController`, `ExternalLoginController` y `WhatsAppWebhookController`.
5. `WhatsAppWebhookRateLimitOptions` (`SectionName = "RateLimiting"`, las dos propiedades con los mismos valores por defecto y `[Range]` que tenían en `RateLimitingOptions`) y la política `whatsapp-webhook` en `AddWhatsAppApi`. `RateLimitingExtensions.WhatsAppWebhookPolicy` pasa al módulo (`WhatsAppApiRegistration.WebhookRateLimitPolicy = "whatsapp-webhook"`, mismo texto).
6. `AddWhatsAppApi` registra la convención de rutas condicionales; se borra `ConditionalWhatsAppRouteRegistration`.
7. `WhatsAppLoginCodeController` y `MeWhatsAppController` (tabla 4.2): copian las acciones con sus atributos; `LoginCodeController` y `MeController` las pierden (y `MeController` pierde `IProfileWhatsAppService`). `WhatsAppLoginCodeController` sigue inyectando `ILoginCodeService` hasta la tanda 6.
8. `Program.cs`: el bloque y los tres `using` de la sección 3.3.
9. Implementar los ganchos `AddModules` de `AUT` (`services.AddWhatsAppApplication()`), llamados en los cuatro lugares de la sección 3.7, así `Every_application_dependency_is_registered`, `Application_services_helpers_and_the_request_validator_are_scoped` y `ApplicationHelpersTests` ven el módulo, y `Every_application_validator_is_registered_and_resolves_in_a_scope` no falla: escanea **todos** los validadores del ensamblado (`DependencyInjectionTests.cs:37-43`) y, sin el gancho, los tres del módulo quedarían sin registrar. La aserción de hoy "`AddApplication()` sola no registra `IWhatsAppWebhookService`" (`DependencyInjectionTests.cs:87`) pasa a "`AddApplication()` sola no registra ningún tipo de `*.Modules.*`" (servicio o implementación), que es más fuerte.
10. Borrar de `KnownViolations` las seis filas de la tanda 3, y borrar `KnownMissingRegistrations`: desde acá la condición de `Program.cs` (6.1) y la 6.3 se afirman sobre el código real.

**Tests.**
- Rojo primero: `ModuleBoundaryTests` con la lista achicada; `AUT/M/W/WhatsAppApplicationRegistrationTests.The_module_registers_its_services_only_through_its_own_registration` (con `AddApplication()` sola no hay nada del módulo; con las dos, están todos).
- **Aserciones que cambian, con su motivo (hallazgo 5):** en `WhatsAppRegistrationTests`, `Without_a_phone_number_id_whatsapp_is_off_and_the_api_starts` y `Without_the_webhook_secrets_the_webhook_is_off_and_the_api_starts`, `Assert.Empty(api.Services.GetServices<IWhatsAppWebhookService>())` pasa a `Assert.DoesNotContain(api.Services.GetServices<IHostedService>(), s => s is WhatsAppInboundProcessor)`. Las rutas siguen dando el 404 del framework (lo prueban esos mismos tests).
- En verde sin tocar: `ExplicitRouteInventoryTests` (43), `OpenApiTests` (tags `Users` y `Account`, 202/204, sin 403 en las rutas de perfil), `AuthConnectAccountContractTests`, `ApiAdministrationHttpContractsTests.Profile_code_routes_return_429_with_retry_after_when_the_ip_limit_is_reached`, `MvcWhatsAppRouteConventionTests`, `WhatsAppRouteContractsTests`, `WhatsAppWebhookTests` y `LayerDependencyTests`.

**Riesgos.**
- Olvidar un atributo al partir un controller: cambia el OpenAPI o el rate limit. Lo ven `OpenApiTests` y los contratos HTTP.
- Que el módulo no registre algo que antes registraba el núcleo: lo ve `Every_application_dependency_is_registered`, que ahora incluye el módulo por el gancho, y en integración sería un 500.
- Orden de registro: ver sección 3.3.

**Docs.** `backend.md` (el árbol, "Reglas de ubicación y acceso" punto 5 sobre DI, el borde HTTP: "Un controller nuevo con protocolo propio lleva `[OwnProtocol]`"), `AGENTS.md` (fila "Registro en DI": los `*Registration` de un módulo), `whatsapp.md` (el rate limiter y las rutas condicionales), `Api/Controllers/AGENTS.md` (los controllers del módulo están en `Api/Modules/WhatsApp/Controllers`).

**Terminado.** El común. Commits `refactor: un registro por capa para el módulo WhatsApp` y `refactor: las rutas de WhatsApp en controllers del módulo`.

### Tanda 4. Puerto `IPhoneChannel` (S, riesgo bajo)

**Objetivo.** Que los medios de ingreso y la regla del país de un número nuevo pregunten a un puerto en lugar de a WhatsApp.

**Archivos.** Nuevos `App/Interfaces/Channels/IPhoneChannel.cs`, `App/Channels/DisabledPhoneChannel.cs`, `App/M/W/Channels/WhatsAppPhoneChannel.cs`, `App/M/W/Services/WhatsAppIds.cs`, `AUT/TestDoubles/Channels/FakePhoneChannel.cs`, `AUT/Channels/DisabledPhoneChannelTests.cs`, `AUT/M/W/Channels/WhatsAppPhoneChannelTests.cs`. Cambian `LoginMethodsService.cs`, `UserContactLinker.cs`, `App/DependencyInjection.cs`, `WhatsAppApplicationRegistration.cs`, `IPhoneNumberParser.cs`, `LibPhoneNumberParser.cs`, `WhatsAppInboundService.cs` (usa `WhatsAppIds.Parse`), `AUT/TestDoubles/Auth/AuthFakes.cs` (`FakePhoneNumberParser` pierde `FromWhatsAppId`), `AUT/M/W/Services/WhatsAppLoginCodeServiceTests.cs` (el mudado en la 2: su parser envoltorio implementa `FromWhatsAppId`, hoy `RequestWhatsAppLoginCodeServiceTests.cs:460`, y sin el método en la interfaz no compila), `AUT/Services/Auth/LoginMethodsServiceTests.cs`, `AUT/Services/Users/UserServiceTestHost.cs`, `IT/Phones/LibPhoneNumberParserTests.cs` (sus dos casos de `wa_id` pasan a `IT/M/W/WhatsAppIdsTests.cs`, con el `LibPhoneNumberParser` real).

**Pasos.**
1. El puerto y `DisabledPhoneChannel`, con `TryAddSingleton` en `AddApplication`.
2. `WhatsAppPhoneChannel` y el `Replace` en `AddWhatsAppApplication`.
3. `LoginMethodsService(IGoogleAvailability, IPhoneChannel, ILogger)`: `phone.IsEnabled ? new(google.IsEnabled, WhatsApp: true, phone.Countries, phone.DisplayNumber) : new(google.IsEnabled, WhatsApp: false, [], null)`.
4. `UserContactLinker`: cambia `IOptions<WhatsAppLoginOptions>` por `IPhoneChannel`; `EnsureCountryAllowed(phone) => phoneChannel.EnsureCanSendTo(phone)`. El resto no cambia (`ReadPhone` y la edición siguen llamándolo en los mismos puntos).
5. `FromWhatsAppId` sale de `IPhoneNumberParser` y de `LibPhoneNumberParser` (con `MinWhatsAppIdDigits` y `MaxWhatsAppIdDigits`, que pasan a `WhatsAppIds`). `WhatsAppInboundService.cs:84` usa `WhatsAppIds.Parse(phoneNumbers, waId)`.
6. Borrar de `KnownViolations` `LoginMethodsService.cs` y `UserContactLinker.cs`.

**Tests.**
- Rojo primero: `WhatsAppApplicationRegistrationTests.The_module_replaces_the_disabled_phone_channel_in_either_order` (núcleo → módulo y módulo → núcleo: un solo `IPhoneChannel` y es `WhatsAppPhoneChannel`); `DisabledPhoneChannelTests` (apagado, sin países, sin número, acepta `+598…`); `WhatsAppPhoneChannelTests` (prendido y apagado; país permitido y no permitido, también apagado); `LoginMethodsServiceTests` pasa a `FakePhoneChannel`.
- Los casos de `LoginMethodsServiceTests` que hoy arman `WhatsAppLoginOptions` para probar los países y el número pasan a `WhatsAppPhoneChannelTests` (mismas aserciones sobre el canal).
- En verde sin tocar: `LoginMethodsControllerTests`, `CreateUserWithPhoneTests.A_phone_from_a_country_that_is_not_enabled_is_rejected`, `UpdateUserContactTests.A_phone_that_is_not_a_mobile_or_from_a_country_that_is_not_enabled_is_rejected`, `UserAdministrationServiceTests`, `WhatsAppBotTests` (el `wa_id`).

**Riesgos.** Que `LoginMethods` muestre países con WhatsApp apagado (hoy no los muestra): lo fija `WhatsAppPhoneChannelTests` y `WhatsAppLoginCodeTests` con la Api apagada.

**Docs.** `identidad.md` y `administracion.md` no cambian de regla. `whatsapp.md` "Configuración": `AllowedCountries` vale para vincular y para el alta y la edición, vía `IPhoneChannel`.

**Terminado.** El común. Commit `refactor: el canal telefónico es un puerto del núcleo`.

### Tanda 5. Puertos de invitación (M)

**Objetivo.** Que la invitación por correo y por WhatsApp sean dos adaptadores del mismo puerto, que el detalle pida el estado de entrega a quien lo sabe, y que `UserInvitation` deje de nombrar WhatsApp.

**Archivos.** Nuevos: `IInvitationChannel.cs`, `IInvitationDeliveryStatusSource.cs`, `App/Models/Users/InvitationCheck.cs`, `App/Channels/EmailInvitationChannel.cs`, `App/M/W/Channels/WhatsAppInvitationDeliveryStatusSource.cs`, `App/M/W/Interfaces/Persistence/IWhatsAppMessageReader.cs`, `Infra/M/W/Persistence/Readers/WhatsAppMessageReader.cs`, dobles `FakeInvitationChannel` y `FakeInvitationDeliveryStatusSource`, tests `EmailInvitationChannelTests`, `AUT/M/W/Channels/{WhatsAppInvitationChannelTests,WhatsAppInvitationDeliveryStatusSourceTests}.cs`, `IT/M/W/WhatsAppMessageReaderTests.cs`. `git mv` de `App/M/W/Services/WhatsAppInvitationIssuer.cs` → `App/M/W/Channels/WhatsAppInvitationChannel.cs`. Cambian: `UserInvitationIssuer.cs`, `UserQueryService.cs`, `LastInvitation.cs`, `UserInvitationRow.cs`, `IUserInvitationReader.cs` (XML), `UserInvitationReader.cs`, `Dom/Users/UserInvitation.cs`, `UserInvitationConfiguration.cs` (`MaxProviderMessageIdLength`), `WhatsAppDeliveryService.cs` (`AttachProviderMessage`), `App/DependencyInjection.cs`, `WhatsAppApplicationRegistration.cs`, `WhatsAppInfrastructureRegistration.cs` (el lector); tests `LastInvitationTests`, `UserQueryServiceTests`, `UserServiceTestHost`, `UserInvitationServiceTests`, `UserAdministrationServiceTests`, `DUT/Users/UserInvitationTests`, `IT/Persistence/UserInvitationReaderTests`, y los tres del módulo que arman invitaciones con `ByWhatsApp` o llaman a `AttachWhatsAppMessage` (`AUT/M/W/Services/{WhatsAppDeliveryServiceTests,RecordOutboundWhatsAppMessageTests,RecordUnsentWhatsAppMessageTests}.cs`: sin ellos la tanda no compila).

**Pasos.**
1. `Dom/Users/UserInvitation.cs`: `Send(Guid userId, UserInvitationChannel channel, Guid sentBy, DateTime nowUtc, bool consentConfirmed)`; `ByEmail` queda como atajo de `Send(…, Email, …, false)`; se borra `ByWhatsApp`; `AttachWhatsAppMessage` → `AttachProviderMessage(string providerMessageId)`, que lanza si el canal es `Email` ("An invitation by email has no provider message."); `MaxWaMessageIdLength` → `MaxProviderMessageIdLength = 256` (el mismo valor, así la columna no cambia). La propiedad sigue llamándose `WaMessageId` hasta la tanda 8. Se borra el `using` del módulo.
2. Los dos puertos y `InvitationCheck` (sección 3.2).
3. `EmailInvitationChannel` con la rama de correo de `UserInvitationIssuer`; `WhatsAppInvitationChannel` con las reglas de WhatsApp de `Check` y el `Enqueue` de hoy.
4. `UserInvitationIssuer` con `IEnumerable<IInvitationChannel>` (sección 3.2). `InvitationFields` no cambia.
5. `IWhatsAppMessageReader` + `WhatsAppMessageReader` + `WhatsAppInvitationDeliveryStatusSource`.
6. `UserInvitationRow` sin `HasWaMessageId` ni `OutboundStatus`, con `ProviderMessageId`; `UserInvitationReader` proyecta `invitation.WaMessageId` como `ProviderMessageId`, sin subconsulta; `LastInvitation.From(row, trackedStatus)`; `UserQueryService` con las fuentes.
7. Registros: `EmailInvitationChannel` en el núcleo; el canal, la fuente y el lector en el módulo.
8. Borrar de `KnownViolations` las cinco filas de la tanda 5.

**Tests.**
- Rojo primero: `EmailInvitationChannelTests` (sin correo, en el campo del canal; cola llena → `SendFailed` y log); `UserInvitationServiceTests.Without_an_adapter_for_the_channel_the_invitation_is_rejected_on_the_channel` (host sin el canal de WhatsApp); `UserQueryServiceTests.The_detail_asks_the_status_source_of_the_channel_and_not_for_a_failed_invitation`; `An_invitation_channel_is_registered_once_per_channel` (en `WhatsAppApplicationRegistrationTests`: correo y WhatsApp, uno cada uno, aun llamando dos veces a `AddWhatsAppApplication`); `WhatsAppMessageReaderTests` (con Docker: saliente con y sin estado, un entrante con el mismo id no cuenta).
- **Se mueven casos, con las mismas aserciones:** de `LastInvitationTests`, el mapeo de `WhatsAppMessageStatus` pasa a `WhatsAppInvitationDeliveryStatusSourceTests`; `LastInvitationTests` se queda con "fallida gana", "sin estado seguido es null" y "el estado seguido pasa tal cual". De `UserInvitationReaderTests`, los casos de estado (`A_whatsapp_invitation_without_a_saved_message_has_no_status`, `A_saved_message_without_a_status_yet_has_no_status`, `A_whatsapp_invitation_carries_the_status_of_its_message`) pasan a `WhatsAppMessageReaderTests`; el lector del núcleo conserva el orden, el desempate y el id.
- `UserServiceTestHost` arma `UserInvitationIssuer` con los dos adaptadores **reales**, `[EmailInvitationChannel, WhatsAppInvitationChannel(SendQueue, …)]`, en esta tanda: con un `FakeInvitationChannel(WhatsApp)`, `host.SendQueue` no recibiría nada y `UserAdministrationServiceTests.Create_with_whatsapp_invitation_locks_the_account_invitations_before_queueing` (que afirma `Assert.Single(host.SendQueue.Messages)` y el lock visto desde la cola) dejaría de estar "en verde sin tocar". `FakeInvitationChannel` se usa en los tests nuevos de esta tanda; la tanda 9 deja el host del núcleo sin nada del módulo y pasa ese test al host del módulo.
- En verde sin tocar: `UserInvitationEndpointsTests` (las 21), `CreateUserWithPhoneTests`, `ProblemDetailsMapperTests`, `UserAdministrationServiceTests.Create_with_whatsapp_invitation_locks_the_account_invitations_before_queueing`.

**Riesgos.**
- El orden de las reglas de `Check` (hoy: apagado, teléfono, consentimiento, nombre): el front muestra el primer error. Lo fijan `UserInvitationEndpointsTests.Inviting_by_whatsapp_without_*`.
- El `Pending` de una invitación sin id: lo fija `The_sender_fills_the_meta_id_of_the_invitation_and_the_detail_shows_the_status_that_meta_sends`.

**Docs.** `administracion.md` (`UserInvitationIssuer` y los canales), `whatsapp.md` ("La plantilla de invitación": el estado lo da la fuente del módulo), `backend.md` ("Colas en memoria": la fila de invitaciones nombra los adaptadores), `AGENTS.md` (fila nueva "Puerto del núcleo hacia un módulo" → `App/Interfaces/Channels`, implementación del núcleo en `App/Channels`, la del módulo en `App/Modules/<M>/Channels`), `Services/Users/AGENTS.md` (ya no nombra `WhatsAppInvitationIssuer`).

**Terminado.** El común. Commit `refactor: las invitaciones salen por canales y el estado de entrega por su fuente`.

### Tanda 6. Los códigos por teléfono se van al módulo (M)

**Objetivo.** Que el núcleo no mande ni cuente códigos por teléfono: el pedido de ingreso, el del perfil y el tope diario pasan al módulo. El núcleo sigue emitiendo (`LoginCodeIssuer`) y verificando (`LoginCodeVerifier`, `DestinationCodeVerifier`) cualquier código.

**Archivos.** Nuevos: `App/M/W/Interfaces/Services/IWhatsAppLoginCodeService.cs`, `App/M/W/Services/{WhatsAppLoginCodeService,WhatsAppCodeIssuer,WhatsAppCodeQuotaGuard}.cs`, `AUT/M/W/Services/WhatsAppCodeQuotaGuardTests.cs`. Cambian: `SignInCodeIssuer.cs`, `DestinationCodeIssuer.cs`, `LoginCodeIssuer.cs`, `LoginCodeService.cs`, `ILoginCodeService.cs`, `ProfileWhatsAppService.cs`, `WhatsAppLoginCodeController.cs`, `WhatsAppApplicationRegistration.cs`, `App/DependencyInjection.cs`; tests `WhatsAppLoginCodeServiceTests` (el mudado), `ProfileWhatsAppServiceTests`, `RequestLoginCodeServiceTests`, `VerifyLoginCodeServiceTests`, `ProfileEmailServiceTests` (sus constructores pierden `WhatsAppLoginOptions`, la cola y la disponibilidad).

**Pasos.**
1. `WhatsAppCodeQuotaGuard` (`internal sealed partial`, scoped; `ILoginCodeRepository`, `IOptions<WhatsAppLoginOptions>`, `TimeProvider`, `ILogger<WhatsAppCodeQuotaGuard>`): `Task<Error?> CheckAsync(CancellationToken)` es `LoginCodeIssuer.CheckDailyLimitAsync` mudado con su `[LoggerMessage]` (mismo texto; cambia la categoría) y la ventana de 24 horas. Cuenta `LoginCodeChannel.WhatsApp` hasta la tanda 8.
2. `WhatsAppCodeIssuer` (`internal sealed`, scoped; `LoginCodeIssuer`, `WhatsAppCodeQuotaGuard`, `IUserReader`, `IPhoneNumberParser`, `IWhatsAppSendQueue`, `IWhatsAppAvailability`, `AccountCreationPolicy`, `IOptions<WhatsAppLoginOptions>`: 8, el tope):
   - `EnsureEnabledForSignIn()` y `EnsureEnabledForLink()`: lanzan con los textos de hoy (`LoginCodeService.cs:64` y `DestinationCodeIssuer.cs:73`).
   - `RequestSignInCodeAsync(RequestWhatsAppLoginCodeRequest, ct)`: el cuerpo de `SignInCodeIssuer.RequestWhatsAppLoginCodeCoreAsync`, con `await quota.CheckAsync(ct)` **después del país y antes de** `issuer.IssueSignInCodeAsync` (el mismo lugar lógico que hoy: primera línea de `IssueAsync`).
   - `RequestVerificationCodeAsync(UserAccount, RequestPhoneLinkCodeRequest, ct)`: el de `DestinationCodeIssuer.RequestPhoneCodeAsync`, con la cuota en el mismo lugar.
3. `WhatsAppLoginCodeService : IWhatsAppLoginCodeService` (`WhatsAppCodeIssuer`, `IRequestValidator`, `IUnitOfWork`, `ILogger`): `RequestAsync` con `OperationLog.RunAsync(logger, "RequestWhatsAppLoginCode", …)` (**el mismo nombre de operación**), validar, `EnsureEnabledForSignIn()` afuera del límite, `ExecuteInTransactionAsync(…, OnSuccess)`.
4. `ProfileWhatsAppService`: `DestinationCodeIssuer` → `WhatsAppCodeIssuer` (sigue con 8 dependencias).
5. El núcleo pierde: `SignInCodeIssuer.RequestWhatsAppLoginCodeCoreAsync` (y `IPhoneNumberParser`, `IWhatsAppSendQueue`, `IOptions<WhatsAppLoginOptions>`); `DestinationCodeIssuer.EnsureWhatsAppEnabled` y `RequestPhoneCodeAsync` (y sus cuatro dependencias de WhatsApp); `LoginCodeIssuer` pierde el `if` del canal (`:55`), `CheckDailyLimitAsync`, `DailyWindow`, `IOptions<WhatsAppLoginOptions>`, `ILogger` y el `partial`; `LoginCodeService.RequestWhatsAppLoginCodeAsync` e `IWhatsAppAvailability`; `ILoginCodeService` pierde el método. `ILoginCodeRepository.ListLatestSentTimesAsync` se queda en el núcleo (es por canal, genérico).
6. `WhatsAppLoginCodeController` inyecta `IWhatsAppLoginCodeService`.
7. Borrar de `KnownViolations` las cinco filas de la tanda 6.

**Tests.**
- Rojo primero: `WhatsAppCodeQuotaGuardTests` (debajo del tope no hay error; en el tope, `TooManyRequests` con `retryAfter` desde el más viejo, y el log); `WhatsAppLoginCodeServiceTests` pasa a armar `WhatsAppLoginCodeService` (mismas aserciones; `Assert.Equal(LoginCodeChannel.WhatsApp, code.Channel)` sigue igual hasta la 8); `AUT/Services/Auth/LoginCodeIssuerTests.A_phone_code_has_no_daily_limit_in_the_core` (archivo nuevo: el emisor del núcleo emite un código de teléfono con el repositorio lleno de enviados).
- `DependencyInjectionTests.Every_application_dependency_is_registered` ve los tres nuevos.
- En verde sin tocar: `IT/M/W/WhatsAppLoginCodeTests` (con el tope diario real), `MeWhatsAppEndpointsTests` (el código del perfil comparte límites y cuota), `AuthConnectAccountContractTests`, `RequestLoginCodeServiceTests`, `VerifyLoginCodeServiceTests`, `ProfileEmailServiceTests`, `OperationLogTests`.

**Riesgos.**
- **Seguridad del ingreso.** Que un pedido por teléfono no pase por los límites por destino: sigue pasando por `LoginCodeIssuer.Issue*`, que no cambia salvo la cuota. Lo fijan `WhatsAppLoginCodeTests` y `LoginSecurityTests`.
- Que la cuota se cuente distinto: mismo repositorio, mismo canal, misma ventana.
- Que la enumeración de cuentas se note: sigue respondiendo 202 con el mismo cuerpo (`RequestWhatsAppLoginCodeResponse`).

**Docs.** `identidad.md` ("Los códigos son por destino y propósito": `LoginCodeIssuer` es el único que emite; el tope diario es del módulo; `SignInCodeIssuer` y `DestinationCodeIssuer` quedan solo con el correo; los pedidos por número están en `WhatsAppCodeIssuer`), `whatsapp.md` (la fila `DailyAuthCodeLimit`), `backend.md` ("Colas en memoria": los llamadores del código por WhatsApp), `Services/Auth/AGENTS.md`.

**Terminado.** El común. Commit `refactor: los códigos por teléfono y su tope diario son del módulo WhatsApp`.

### Tanda 7. Puerto `IPhoneLinkParticipant` (L, el de más riesgo)

**Objetivo.** Que `PhoneNumberLinker` deje de conocer el contacto de WhatsApp y avise a los participantes, **tomando sus locks antes del de la cuenta**.

**Archivos.** Nuevos: `App/Interfaces/Channels/IPhoneLinkParticipant.cs`, `App/M/W/Channels/WhatsAppPhoneLinkParticipant.cs`, `AUT/TestDoubles/Channels/RecordingPhoneLinkParticipant.cs`, `AUT/Services/Users/PhoneNumberLinkerTests.cs`, `AUT/M/W/Channels/WhatsAppPhoneLinkParticipantTests.cs`. Cambian: `PhoneNumberLinker.cs`, `UserContactLinker.cs`, `UserAccessService.cs`, `ProfileWhatsAppService.cs` (`ReleaseContactAsync` → `ReleasePhoneAsync`), `WhatsAppContactLinker.cs` (solo el texto del XML; su `cref` a `PhoneNumberLinker.LockAsync` ya quedó con nombre completo en la tanda 2), `WhatsAppApplicationRegistration.cs`, `AUT/Services/Users/UserServiceTestHost.cs`, `AUT/M/W/Services/ProfileWhatsAppServiceTests.cs` (arma su propio `new PhoneNumberLinker(…, contactLinker, …)`, `ProfileWhatsAppServiceTests.cs:402`), `AUT/M/W/TestDoubles/WhatsAppFakes.cs`.

**Pasos.**
1. El puerto (sección 3.2) y `WhatsAppPhoneLinkParticipant`, con `TryAddEnumerable`.
2. `PhoneNumberLinker(IUserReader, IUserRepository, DestinationCodeVerifier, IEnumerable<IPhoneLinkParticipant>, ILoginLinkRepository, TimeProvider)`: materializar los participantes una vez (`participants.ToArray()` en el constructor), `LockAsync` con el `foreach` antes de `LockAccountAsync`; `ConfirmOwnPhoneAsync` con `PhoneConfirmedAsync` al final; `ReleasePhoneAsync`. Los XML dicen el orden y por qué (copiar el de hoy, cambiando "contactos" por "lo que los participantes atan al número").
3. Renombrar las cuatro llamadas a `ReleaseContactAsync`.
4. Borrar la última fila de `KnownViolations`: la lista queda vacía y la regla pasa a `Assert.Empty` (se borra el diccionario).

**Tests.**
- Rojo primero, `PhoneNumberLinkerTests` (con `RecordingPhoneLinkParticipant` e `InMemoryLoginLinkRepository` anotando en la misma lista de eventos):
  - `Participants_lock_before_the_account_links` (`["participant-lock:<id>", "login-link:<id>"]`, y con `newPhone` el participante lo recibe);
  - `Without_participants_only_the_account_links_are_locked` (el núcleo sin módulo);
  - `Confirming_the_own_phone_tells_the_participants_after_saving` y `Releasing_the_phone_tells_every_participant`;
  - `Participants_are_called_in_registration_order`.
- `WhatsAppPhoneLinkParticipantTests`: cada método delega en `WhatsAppContactLinker` (con el `InMemoryWhatsAppContactRepository` de `WhatsAppFakes`).
- `UserServiceTestHost` arma `PhoneNumberLinker` con el participante **real**, `[new WhatsAppPhoneLinkParticipant(new WhatsAppContactLinker(Contacts))]`, sobre el mismo `InMemoryWhatsAppContactRepository(MessagesLog)` de hoy: así `MessagesLog` anota exactamente lo mismo (`"number-change:" + userId` y las lecturas `read:GetByUserIdAsync`) y quedan en verde sin tocar `UserAccessServicePhoneTests.Unlink_locks_contact_then_account_revokes_sessions_and_commits` y `UserAdministrationServiceTests.Update_replacing_phone_invalidates_pending_link_after_unlinking_contact`, que afirma que `number-change:` va antes de `read:GetByUserIdAsync` y que el contacto quedó suelto (con un `RecordingPhoneLinkParticipant` en el host, esa lectura no existiría, `IndexOf` daría -1 y el test fallaría). `RecordingPhoneLinkParticipant` se usa en `PhoneNumberLinkerTests`; el host del núcleo pasa a él en la tanda 9, cuando esos dos tests se parten.
- La red, con Docker, sin tocar: los cinco tests de concurrencia de `MeWhatsAppEndpointsTests`, `UnlinkUserPhoneEndpointTests.Unlinking_removes_the_number_releases_the_chat_voids_the_links_and_closes_the_sessions`, `UpdateUserContactTests.Changing_the_phone_releases_the_chat_of_the_previous_one_and_voids_its_links_but_keeps_the_sessions`, `UserRepositoryTransactionTests.Failed_contact_unlink_rolls_back_autosaved_name_email_and_phone`, `WhatsAppBotTests`, `LastAdminLockTests`.

**Riesgos.**
- **Deadlock 40P01** si un participante toma su lock después de `login-link:`: lo ven `PhoneNumberLinkerTests` (orden) y `MeWhatsAppEndpointsTests` (el choque real con el bot). Correr los cinco de concurrencia **tres veces** seguidas antes de dar la tanda por terminada: dependen de cómo se crucen los pedidos.
- Registrar el participante dos veces tomaría el lock dos veces (es reentrante, pero duplica el trabajo): `TryAddEnumerable` y un test de "uno solo".
- Que el perfil lea la cuenta antes del lock: no cambia quién lee ni cuándo.

**Docs.** `backend.md` ("Una sola forma de guardar": el orden de los locks con "los locks de los participantes" en lugar de "filas de contactos"; la lista de claves de `AdvisoryLockKeys` sin las de WhatsApp, que pasan a `whatsapp.md`), `whatsapp.md` ("Los locks van siempre en el mismo orden" y "Vincular y soltar contactos": `PhoneNumberLinker` avisa a `WhatsAppPhoneLinkParticipant`, que delega en `WhatsAppContactLinker`), `administracion.md` (el lock `users:admins` va "después de los locks de los participantes y de cuenta").

**Terminado.** El común, más las tres corridas de concurrencia. Commit `refactor: los cambios de número avisan a los participantes del núcleo`.

### Tanda 8. Los valores guardados: `Phone` y `ProviderMessageId` (M)

**Objetivo.** Renombrar el canal del código y la columna de la invitación, con dos migraciones, sin perder datos.

**Archivos.** `Dom/Authentication/LoginCodeChannel.cs`, `LoginCodeDestination.cs`, `Dom/Users/UserInvitation.cs`, `Infra/Persistence/Configurations/UserInvitationConfiguration.cs`, `UserInvitationReader.cs`, `WhatsAppCodeQuotaGuard.cs`, las migraciones nuevas y el snapshot; tests con `LoginCodeChannel.WhatsApp` (seis aserciones en seis archivos: `DUT/Authentication/LoginCodeDestinationTests.cs:22`, `LoginCodeTests.cs:44`, `IT/Persistence/LoginCodeRepositoryTests.cs:151`, `IT/M/W/WhatsAppLoginCodeTests.cs:55`, `IT/M/W/MeWhatsAppEndpointsTests.cs:69`, `AUT/M/W/Services/WhatsAppLoginCodeServiceTests.cs:47`), los de `WaMessageId` de invitaciones (`UserInvitationTests`, `UserInvitationReaderTests`, `WhatsAppDeliveryServiceTests`, `RecordOutboundWhatsAppMessageTests` y `IT/Users/UserInvitationEndpointsTests`, que en `:385`, `:417` y `:480` espera `invitation.WaMessageId == waMessageId` leyendo la fila con EF) y un test nuevo `IT/Persistence/StoredValuesMigrationTests.cs`.

**Pasos.**
1. `LoginCodeChannel { Email = 1, Phone = 2 }` (el número no cambia; se guarda el nombre). `LoginCodeDestination.ForPhone` usa `Phone`. `WhatsAppCodeQuotaGuard` cuenta `Phone`. XML: "Por teléfono, a un número en formato internacional (hoy, por WhatsApp)".
2. Generar `LoginCodePhoneChannel` con el comando de la guía. Va a salir **vacía** (el enum se guarda como texto y el modelo no cambia): se le escribe a mano
   ```csharp
   migrationBuilder.Sql("""UPDATE "LoginCodes" SET "Channel" = 'Phone' WHERE "Channel" = 'WhatsApp';""");
   ```
   y en `Down` el inverso. Es una de las ediciones permitidas por la guía ("para corregir lo generado"), y se explica en un comentario de la migración.
3. `UserInvitation.WaMessageId` → `ProviderMessageId`; `UserInvitationConfiguration` la configura con `MaxProviderMessageIdLength`; `UserInvitationReader` la proyecta sin alias.
4. Generar `UserInvitationProviderMessageId` y revisar que sea `RenameColumn(name: "WaMessageId", table: "UserInvitations", newName: "ProviderMessageId")` en `Up` y el inverso en `Down`. Si EF generó `DropColumn` + `AddColumn`, **se reemplaza a mano por el `RenameColumn`** (un `DropColumn` perdería los ids de Meta de las invitaciones ya mandadas).
5. `has-pending-model-changes`: "No changes".

**Tests.**
- Rojo primero: `LoginCodeDestinationTests` espera `Phone`; `StoredValuesMigrationTests` (con Docker, sobre una base nueva): migra hasta la migración anterior con `IMigrator.MigrateAsync("<la anterior>")`, inserta con SQL una fila de `LoginCodes` con `Channel = 'WhatsApp'` y una invitación con `WaMessageId = 'wamid.x'`, migra a la última y lee con EF: el código tiene `Channel == Phone` y la invitación `ProviderMessageId == "wamid.x"`. Después `Down` hasta la anterior y los valores vuelven.
- **Aserciones que cambian por el renombre (las únicas):** las seis de `LoginCodeChannel.WhatsApp` → `Phone`, y los nombres de propiedad `WaMessageId` → `ProviderMessageId` en los tests de invitaciones.
- En verde: `MigrationsTests` (`Model_has_no_pending_changes`, `Migrations_create_the_schema_on_an_empty_database`), `WhatsAppLoginCodeTests` (el tope diario ya cuenta `Phone`).

**Riesgos.**
- **Convivencia durante un despliegue.** La [guía de la migración](../guides/migracion.md) pide que una migración conviva con la imagen anterior, y estas dos no: entre el bundle y la imagen nueva, la vieja no puede leer un código con `'Phone'` (el enum no tiene ese nombre) ni la columna `WaMessageId`. Hoy la plantilla no tiene un despliegue productivo con datos, así que se acepta; la guía `quitar-whatsapp.md` y `despliegue.md` lo dicen para los proyectos derivados que ya estén en producción (ver duda 1 de la sección 9).
- Un código en vuelo durante la migración: vive 10 minutos; en el peor caso la persona pide otro.

**Docs.** `identidad.md` (`LoginCodeDestination.ForPhone`, con su `Channel` `Phone`), `whatsapp.md` (la invitación guarda `ProviderMessageId`), `backend.md` "Migraciones" (una migración de datos se escribe a mano adentro de la generada vacía, con un test de migración).

**Terminado.** El común. Commit `feat: el canal del código es Phone y la invitación guarda ProviderMessageId` (cambia datos guardados, no es solo un refactor).

### Tanda 9. El arnés y los tests del núcleo sin el módulo (L)

**Objetivo.** Que los tests del núcleo compilen y pasen **sin** las carpetas del módulo, y que con ellas sigan pasando todos. Hasta acá, muchos tests del núcleo usan WhatsApp prendido; ahora se parten.

**Archivos.** `IT/Support/ApiFactory.cs` y su parte `IT/M/W/ApiFactory.WhatsApp.cs`; `IT/Contracts/ExplicitRouteInventoryTests.cs` y `IT/M/W/ExplicitRouteInventoryTests.WhatsApp.cs`; los archivos mixtos (abajo); `AUT/Services/Users/UserServiceTestHost.cs`, `AUT/TestDoubles/Auth/AuthFakes.cs`.

**Cómo se decide, test por test.** Un test va al módulo si necesita algo del módulo: una ruta del módulo, un tipo del módulo, WhatsApp prendido (los países, el contacto, el bot, una invitación por WhatsApp, el estado de Meta, `factory.WhatsApp`). Si solo menciona WhatsApp en un comentario, en `DELETE /api/users/{id}/whatsapp`, en `LoginMethod.WhatsApp*` o en una clave de configuración como texto (por ejemplo `UseSetting("WhatsApp:PhoneNumberId", "")`), se queda. Se mueve el método entero, con sus aserciones, a una clase del módulo con el mismo nombre más `WhatsApp` (`UserInvitationEndpointsTests` → `IT/M/W/WhatsAppUserInvitationEndpointsTests`); si depende de helpers privados de la clase original, la parte del módulo es `partial` de esa clase.

**Los mixtos**, contados con un `grep` por método (hay que repetir la cuenta al ejecutar: es la de `9f93846`, y las tandas anteriores ya movieron algunos):

| Archivo | Tests que usan WhatsApp / total | Qué se hace |
|---|---|---|
| `IT/Auth/LoginMethodsControllerTests.cs` | 3/3 | los valores de `whatsapp*` al módulo; el núcleo conserva la forma del contrato (las cuatro propiedades) y Google |
| `IT/Contracts/AuthConnectAccountContractTests.cs` | 6/7 | las `[InlineData("/account/login-code/whatsapp")]` salen a un `[Theory]` del módulo con la misma lógica; `Login_link_routes_stay_mapped_when_whatsapp_is_off` se queda |
| `IT/Contracts/ApiAdministrationHttpContractsTests.cs` | 1/17 | los dos `[InlineData]` de `/api/me/whatsapp*` al módulo |
| `IT/OpenApiTests.cs` | 1/5 | las aserciones de `/api/me/whatsapp*` y `/account/login-code/whatsapp` (`:59-71`, `:103-107`) a `IT/M/W/WhatsAppOpenApiTests.cs`; `DevelopmentApi` pasa a `internal static` con la fábrica como parámetro |
| `IT/Persistence/UnitOfWorkTransactionTests.cs` | 3/20 | `Locks_outside_the_boundary_throw_even_without_keys` y `Cancellation_during_the_work_rolls_back_and_releases_the_locks` pierden los repositorios de WhatsApp (quedan los del núcleo) y sus casos de WhatsApp van al módulo; `A_unique_violation_in_the_final_flush_is_translated_after_rolling_back` se reescribe en el núcleo con `LoginLink` (índice único de `TokenHash`, `LoginLinkConfiguration.cs:21`) y la versión con `WhatsAppMessage` va al módulo |
| `IT/Persistence/UserRepositoryTransactionTests.cs` | 2/6 | `Failed_contact_unlink_rolls_back_autosaved_name_email_and_phone` se reescribe en el núcleo con un `IPhoneLinkParticipant` que lanza en `PhoneReleasedAsync` (en `IT/TestFeatures`), mismas aserciones; la versión con `WhatsAppContactRepository` va al módulo |
| `IT/Persistence/LoginCodeRepositoryTests.cs`, `AdvisoryLockKeysTests.cs`, `UserInvitationReaderTests.cs` | 2/14, 1/6, lo que quede después de la 5 | el caso de las claves de WhatsApp ya se movió en la 2; los de código por teléfono se quedan (el canal es `Phone`, del núcleo); lo de estado ya se movió en la 5 |
| `IT/Phones/LibPhoneNumberParserTests.cs` | 3/17 | los de `wa_id` ya se movieron en la 4 |
| `IT/Users/CreateUserWithPhoneTests.cs` | 3/15 | el país no habilitado y la invitación por WhatsApp al módulo |
| `IT/Users/UnlinkUserPhoneEndpointTests.cs` | 4/7 | los que usan `BotConversation` o `factory.WhatsApp` al módulo; permiso, 204 sin número y "no se deja sin medio de ingreso" se quedan |
| `IT/Users/UpdateUserContactTests.cs` | 3/7 | el chat anterior y el país al módulo |
| `IT/Users/UserInvitationEndpointsTests.cs` | 15/21 | los de WhatsApp al módulo; correo, espera y permisos se quedan |
| `AUT`: `LastInvitationTests`, `LoginMethodsServiceTests`, `UserAdministrationServiceTests`, `UserQueryServiceTests`, `UserInvitationServiceTests` | lo que quede | lo que arma `WhatsAppContact`, `WhatsAppErrors` o el canal de WhatsApp, al módulo (entre ellos `Update_replacing_phone_invalidates_pending_link_after_unlinking_contact` y `Create_with_whatsapp_invitation_locks_the_account_invitations_before_queueing`, que hasta acá corren con los adaptadores reales en el host) |
| `AUT/Resources/ResourceParityTests.cs` | 1/4 | `Bot_texts_have_the_same_keys_in_spanish_and_english` nombra `BotTexts`, del módulo: pasa a `AUT/M/W/Resources/` como parte `partial` de la clase (usa su `AssertSameKeys` privado) |
| `AUT/DependencyInjectionTests.cs` | — | la aserción de la tanda 3 ("`AddApplication()` sola no registra ningún tipo de `*.Modules.*`") no nombra tipos del módulo; el `using` de `IWhatsAppWebhookService` que quedara se borra |
| `AUT/Services/Users/UserServiceTestHost.cs`, `AUT/TestDoubles/Auth/AuthFakes.cs` | — | el host del núcleo arma todo con los dobles de `TestDoubles/Channels` y sin nada del módulo; el módulo tiene `AUT/M/W/WhatsAppUserServiceTestHost.cs`, que compone el del núcleo (constructor con canales, participantes y fuentes extra) y suma los reales del módulo; `FakeWhatsAppSendQueue` y `FakeWhatsAppAvailability` pasan a `WhatsAppFakes.cs` |

**Pasos.**
1. `ApiFactory`: mover a la parte del módulo las claves de WhatsApp, `RateLimiting:WhatsAppWebhookPermitLimit`, el reemplazo de la cola, el `HttpClient` sin red, la propiedad `WhatsApp` y las constantes (sección 3.7). Los tests del módulo que usan `ApiFactory.WhatsAppPhoneNumberId` siguen compilando (es la misma clase).
2. `ExplicitRouteInventoryTests`: `CoreRoutes` (37) + `AddModuleRoutes`; `Assert.Equal(37, CoreRoutes.Length)` en el núcleo y un `[Fact]` del módulo que fija sus seis (`POST /account/login-code/whatsapp`, `POST /api/me/whatsapp/code`, `PUT /api/me/whatsapp`, `DELETE /api/me/whatsapp`, `GET /webhooks/whatsapp`, `POST /webhooks/whatsapp`). La comparación de la lista completa no cambia: con el módulo sigue siendo 43.
3. Partir los mixtos según la tabla.
4. **Ensayo de la prueba de fuego** (el procedimiento de la sección 7, pasos 1 a 6) para encontrar lo que falte. Se repite hasta que compile y pase. Lo que aparezca se arregla en esta tanda.

**Tests.** El rojo es el ensayo del paso 4 (la copia sin el módulo no compila o falla). Con el módulo, todos en verde y la misma cantidad de tests que antes de la tanda, más los reescritos (se comparan los totales por proyecto: un test que se pierde en el camino es un error).

**Riesgos.** Perder cobertura al partir (un test que queda en ninguno de los dos lados): se compara el total antes y después, y la lista de nombres (`dotnet test --list-tests` en los dos árboles).

**Docs.** `backend.md` "Tests: arquitectura y arnés" (los ganchos de módulo; `factory.WhatsApp` es del módulo), `whatsapp.md` ("Los tests capturan los envíos": `ApiFactory.WhatsApp.cs`).

**Terminado.** El común, más el ensayo del paso 4 en verde. Es la tanda más difícil de revisar de una vez (unos quince archivos que se parten más el arnés): va en dos commits que dejan todo en verde cada uno, primero `AUT` y `Arch` (host del núcleo con dobles, `WhatsAppUserServiceTestHost`, `ResourceParityTests`), después `IT` (`ApiFactory`, inventario y los mixtos), y el ensayo al final. Commits `test: los tests unitarios del núcleo no nombran el módulo WhatsApp` y `test: los tests de integración del núcleo corren sin el módulo WhatsApp`.

### Tanda 10. La prueba de fuego, la guía y la documentación (M)

**Objetivo.** Probar en una copia descartable que el módulo se quita siguiendo la guía al pie de la letra, y dejar la documentación de la etapa cerrada.

**Pasos.**
1. Escribir `docs/guides/quitar-whatsapp.md` (sección 8).
2. Hacer la prueba de fuego de la sección 7 **siguiendo la guía, sin mirar este plan**. Lo que la guía no diga y haga falta, se agrega a la guía.
3. Actualizar el resto de la documentación (sección 8).
4. Puerta de la etapa (sección 5, común) con los números, y el estado de la Etapa 6 en el plan maestro.

**Terminado.** Puerta general del plan maestro, más la prueba de fuego y `ModuleBoundaryTests` en verde. Commits `docs: guía para quitar el módulo WhatsApp` y `docs: la Etapa 6 cierra con la prueba de fuego`.

---

## 6. Tests de arquitectura nuevos

Todos en `Arch/ModuleBoundaryTests.cs` salvo que se diga otra cosa. Cada regla tiene un detector (una función pura) y su caso de control (entradas armadas a mano que el detector tiene que aceptar o rechazar), como `PersistenceRegistrationTests.The_seed_owners_may_name_only_seed_types`.

### 6.1 El núcleo no nombra un módulo (en el fuente)

- **Qué escanea:** los `.cs` y `.csproj` de `src/ArquitecturaBase.{Domain,Application,Infrastructure,Api}`, sin `bin` ni `obj`, desde `SolutionRoot.FullPath` (igual que `MinimalApiRoutesTests`).
- **Detector:** `static IEnumerable<string> Violations(string relativePath, string text)`. Busca `\bArquitecturaBase\.(Domain|Application|Infrastructure|Api)\.Modules\.(?<module>[A-Z][A-Za-z0-9]*)`. Un archivo **fuera** de `src/<Proyecto>/Modules/` que la contenga es una violación, salvo `src/ArquitecturaBase.Api/Program.cs`. Un archivo **dentro** de `Modules/<A>/` que nombre `Modules.<B>` con `B ≠ A` también (un módulo no depende de otro).
- **Por qué el fuente y no el IL:** una constante (`UserInvitation.MaxWaMessageIdLength` hoy), un `<see cref>` y un `nameof` no dejan rastro en el IL y rompen la compilación al borrar el módulo (sección 2.3, punto 4). Todos necesitan un `using` o el nombre completo, y los dos los ve este detector. Los `global using` también, porque están en un `.cs` o en el `.csproj`.
- **Controles** (con módulos inventados, `Control` y `Sms`, nunca `WhatsApp`: estos tests son del núcleo y quedan en la prueba de fuego, donde el paso 3 de la sección 7 busca que nada nombre `Modules.WhatsApp`): `using ArquitecturaBase.Application.Modules.Control.Services;` en `src/ArquitecturaBase.Application/Services/Users/X.cs` → violación; lo mismo en `src/ArquitecturaBase.Application/Modules/Control/Services/X.cs` → nada; `ArquitecturaBase.Api.Modules.Sms` en un archivo de `Modules/Control` → violación; en `Program.cs` → nada; `ArquitecturaBase.Application.ModulesLegacy` → nada (el `\.` después de `Modules` importa). Lo mismo vale para los ejemplos en comentarios de `ModuleNamespaces` y para los controles de las tablas 6.4.
- **Lo que no ve:** un nombre parcial (`Modules.WhatsApp.X` escrito desde un archivo con namespace `ArquitecturaBase.Application`) resuelve sin el prefijo completo. Es raro y lo ve la prueba de fuego; el detector no intenta resolver nombres.
- **Que el detector vea código real:** el escaneo encuentra más de 100 archivos y `Program.cs` entre ellos; y **si existe alguna carpeta `src/*/Modules/*`**, `Program.cs` tiene que coincidir (el bloque del módulo). La condición hace que la regla siga pasando en la prueba de fuego, sin módulo.
- **`KnownViolations`:** existe de la tanda 2 a la 7, como diccionario `ruta → tanda`, comparado con `Assert.Equal` (así un archivo arreglado de más también falla y obliga a achicar la lista). En la tanda 7 se borra y la regla termina en `Assert.Empty`.

### 6.2 El `DbContext` no expone entidades de un módulo

- **Detector:** `static IEnumerable<string> ModuleSets(Type contextType)`: las propiedades públicas cuyo tipo es `DbSet<T>` con `T` en un namespace `ArquitecturaBase.Domain.Modules.*`.
- **Regla:** `ModuleSets(typeof(ApplicationDbContext))` vacío. El módulo usa `dbContext.Set<T>()`.
- **Control:** una clase del archivo de test, `ControlContext`, con una propiedad `DbSet<ControlModuleEntity>` donde `ControlModuleEntity` vive en un bloque `namespace ArquitecturaBase.Domain.Modules.Control { … }` del mismo archivo (como `ErrorDeclarationControls.cs`), tiene que dar un resultado; y `ApplicationDbContext` tiene que tener al menos un `DbSet` (el detector mira algo).

### 6.3 Cada módulo tiene su registro por capa

- **Detector:** para cada módulo `M` que aparezca en algún namespace de los cuatro ensamblados: existen `public static class` `ArquitecturaBase.Application.Modules.M.MApplicationRegistration`, `ArquitecturaBase.Infrastructure.Modules.M.MInfrastructureRegistration` y `ArquitecturaBase.Api.Modules.M.MApiRegistration`, cada una con un método de extensión público `AddM<Capa>` sobre `IServiceCollection`, salvo que el módulo no tenga tipos en esa capa (un módulo sin Api no necesita `MApiRegistration`).
- **Control:** la función que arma la lista de faltantes, con un conjunto de nombres de tipos armado a mano (un módulo con tipos en Api y sin su registro → falta).
- **Por qué:** es el contrato que permite que `Program.cs` sea un bloque de tres líneas, y lo que la guía de quitar da por supuesto.

### 6.4 Los cambios en los tests que ya existen

| Test | Cambio | Tanda |
|---|---|---|
| `TypeNamespaceExtensions.ResidesIn` | compara el namespace canónico (sin `.Modules.<M>`) | 1 |
| `ControllerServiceRepositoryTests` | NetArchTest con `ResideInNamespaceMatching`; `Interfaces.Channels` prohibido para un controller; los `Interfaces.{Persistence,Integrations,Channels}` de cada módulo, sumados a mano a lo prohibido (NetArchTest compara por prefijo); `Canonical` en las dos comparaciones con `Interfaces.Services` | 1 |
| `EntityConfigurationTests`, `PersistenceNamingTests` | namespaces canónicos para configuraciones, contratos y lectores (`EntityConfigurationTests` ya usa `ResidesIn`) | 1 |
| `ErrorDeclarationTests` | `IsInDomainArea`: en un módulo el área es el módulo (no `Canonical` sin más, que dejaría a `WhatsAppErrors` en la raíz de Domain); dos controles nuevos | 1 |
| `ModuleBoundaryTests` | `KnownMissingRegistrations`: `["WhatsApp"]` en la 2, se borra en la 3 | 1, 2, 3 |
| `PersistenceRegistrationTests` | dueños: `PersistenceRegistration` o el `<M>InfrastructureRegistration` de su módulo, con tres controles nuevos | 1 |
| `TransactionBoundaryTests` | `partial`; claves, prefijos y `ExecuteUpdate` del módulo por gancho | 1, 2 |
| `IdentityBoundaryTests` | `partial`; la regla de oro en la parte del módulo con el namespace nuevo | 1, 2 |
| `ApplicationHelpersTests` (`AUT`) | un helper vive en `Services/<Área>` o en `Modules/<M>/Services`; gancho `AddModules`; control nuevo: un tipo en `…Modules.Control.Services` con sufijo de la tabla pasa, y en `…Modules.Control` (sin `Services`) falla | 1, 3 |
| `DependencyInjectionTests` (`AUT`) | gancho `AddModules` en sus tres tests que recorren el ensamblado; namespaces canónicos (copia de `Canonical` en `AUT`) para seguir las dependencias y los contratos del módulo; `Interfaces.Channels` entre las dependencias vigiladas; "`AddApplication()` sola no registra ningún tipo de `*.Modules.*`" | 1, 3 |
| `ExplicitRouteInventoryTests` (`IT`) | 37 del núcleo + gancho de rutas del módulo | 9 |

---

## 7. Prueba de fuego

Se hace en una **copia descartable fuera del repo**, sin ramas, sin worktrees y sin `git stash` (el árbol de trabajo no se toca). La copia sale de `HEAD`, así que todo lo que se quiera probar tiene que estar commiteado.

```bash
# 1. La copia (git archive no trae .git: la copia no es un repo, nadie puede commitear ahí por error).
SCRATCH=<el directorio de scratchpad de la sesión>/prueba-de-fuego
rm -rf "$SCRATCH" && mkdir -p "$SCRATCH"
git -C /home/user/ArquitecturaBase archive --format=tar HEAD | tar -x -C "$SCRATCH"
cd "$SCRATCH"

# 2. Lo que se borra (lo que dice la guía).
rm -rf src/ArquitecturaBase.Domain/Modules/WhatsApp \
       src/ArquitecturaBase.Application/Modules/WhatsApp \
       src/ArquitecturaBase.Infrastructure/Modules/WhatsApp \
       src/ArquitecturaBase.Api/Modules/WhatsApp \
       tests/ArquitecturaBase.Domain.UnitTests/Modules/WhatsApp \
       tests/ArquitecturaBase.Application.UnitTests/Modules/WhatsApp \
       tests/ArquitecturaBase.ArchitectureTests/Modules/WhatsApp \
       tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp
#    Program.cs: borrar el bloque del módulo y los tres using marcados "// módulo WhatsApp", a mano, como dice la guía.
#    appsettings.json: borrar la sección "WhatsApp" y las claves RateLimiting:WhatsAppWebhookPermitLimit y
#    RateLimiting:WhatsAppWebhookWindowMinutes; appsettings.Development.json: la sección "WhatsApp".

# 3. Que no quede nada que nombre el módulo (el mismo patrón que la regla 6.1; los tests del núcleo usan módulos
#    inventados en sus controles, así que no aparecen).
grep -rnE "ArquitecturaBase\.(Domain|Application|Infrastructure|Api)(\.[A-Za-z]+)*\.Modules\.WhatsApp" \
  src tests --include=*.cs --include=*.csproj   # vacío

# 4. Compilar sin advertencias.
dotnet build ArquitecturaBase.slnx     # si el AppHost no compila por la Aspire CLI: la Api y los cuatro .csproj de tests

# 5. La migración que borra las tablas. Va ANTES de los tests: sin ella, el modelo ya no tiene las dos tablas y el
#    snapshot sí, y MigrationsTests.Model_has_no_pending_changes falla.
dotnet ef migrations add RemoveWhatsApp --project src/ArquitecturaBase.Infrastructure \
  --startup-project src/ArquitecturaBase.Api --output-dir Persistence/Migrations \
  -- --environment Development --ConnectionStrings:appdb "Host=localhost;Port=5433;Database=appdb;Username=postgres;Password=postgres"
#    Revisar que Up tenga exactamente: DropTable("WhatsAppMessages") y DropTable("WhatsAppContacts"), en ese orden (la FK
#    va de mensajes a contactos), y nada de tablas del núcleo. Down las vuelve a crear, con sus índices (el filtrado
#    IX_WhatsAppMessages_PendingInbound incluido) y la FK.
dotnet ef migrations has-pending-model-changes <los mismos argumentos>   # "No changes"

# 6. Los tests del núcleo: los cuatro proyectos, con Docker (MigrationsTests y StoredValuesMigrationTests ya corren con
#    RemoveWhatsApp en la cadena).
dotnet test
```

**Qué tiene que pasar.**
- El build, sin advertencias (con `TreatWarningsAsErrors`, un `using` que sobra o un `cref` que no existe lo rompen).
- Los cuatro proyectos de tests: todos los que quedan. Se anotan los números; se espera que sean los de la puerta menos los del módulo (se cuentan con `dotnet test --list-tests` en los dos árboles).
- En particular: `ModuleBoundaryTests` (sin módulos, la 6.1 pasa y la 6.3 no tiene módulos que mirar), `ExplicitRouteInventoryTests` (37), `OpenApiTests`, `LoginMethodsControllerTests` (`whatsapp: false`, sin países, sin número), `UserInvitationEndpointsTests` (una invitación con `channel: "WhatsApp"` se rechaza en el campo del canal con `InvitationWhatsAppUnavailable`), `UnlinkUserPhoneEndpointTests`, `CreateUserWithPhoneTests` (una cuenta solo con teléfono, de cualquier país, se crea: decisión 4), `MigrationsTests` y `StoredValuesMigrationTests`.
- `RemoveWhatsApp` genera solo los dos `DropTable`.

**Qué no se hace.** No se commitea nada de la copia: la plantilla **no** trae `RemoveWhatsApp`. Lo que se aprende va a la guía (tanda 10) o, si es un bug, se arregla en el repo y se vuelve a copiar.

---

## 8. Documentación

| Documento | Qué cambia | Tanda |
|---|---|---|
| `docs/guides/quitar-whatsapp.md` (nueva) | los pasos de la sección 7 para un proyecto derivado: las carpetas (src y tests), el bloque y los tres `using` de `Program.cs`, los `appsettings`, la migración `RemoveWhatsApp` (con la advertencia: se genera **después** de borrar el código y **antes** de correr los tests, que sin ella fallan en `Model_has_no_pending_changes`, y en un proyecto con base desplegada se aplica con el bundle como cualquier otra), la opción de aplastar las migraciones en una inicial nueva solo si no hay base desplegada, lo opcional (el bloque `DevTunnel` del AppHost y el paquete `Aspire.Hosting.DevTunnels`, `Microsoft.Extensions.Http.Resilience` en `Infra/*.csproj` con su comentario, `/webhooks` de `BackendPrefixes` y sus otros tres lugares de la [guía del prefijo](../guides/prefijo-de-backend.md), los textos del bot ya se fueron con la carpeta), lo que ve el front sin el módulo (`login-methods` responde `whatsapp: false` y sin países, así que el alta y la edición de usuarios no ofrecen el campo del teléfono, `UserFormDialog.tsx:87-88,220` y `UserEditDialog.tsx:180-181` del front: la decisión 4 vale en la Api, pero desde la web no se cargan números nuevos; las cuentas que ya tienen uno lo muestran y se pueden desvincular), la documentación que se borra (`whatsapp.md`, `whatsapp-en-local.md`, el spec del ingreso con WhatsApp si se quiere) y las líneas que enlazan `whatsapp.md` (lista con `grep -rl "whatsapp.md" docs src AGENTS.md README.md`), lo que queda en el núcleo con "WhatsApp" en el nombre (sección 3.6) y por qué no se toca, y cómo comprobar (build, tests, `has-pending-model-changes`) | 10 |
| [`whatsapp.md`](../features/whatsapp.md) | el primer párrafo: el código vive en `*/Modules/WhatsApp` y se registra con el bloque de `Program.cs`; los cuatro adaptadores de los puertos; las claves de locks del módulo (salen de `backend.md`); `IdentityBoundaryTests` con el namespace nuevo; el tope diario en `WhatsAppCodeQuotaGuard`; la invitación con `ProviderMessageId`; `factory.WhatsApp` en `ApiFactory.WhatsApp.cs`. Las reglas que son del núcleo se mudan: "Los celulares argentinos se guardan con el 9" a `identidad.md` (es del parser del núcleo), "Lo que carga un administrador queda sin verificar… `DELETE /api/users/{id}/whatsapp` cierra las sesiones" a `administracion.md` (ya está allá en parte), y en `whatsapp.md` queda el enlace | 2 a 10 |
| [`backend.md`](../architecture/backend.md) | sección nueva "Módulos opcionales": la frontera (el núcleo no nombra un módulo; `ModuleBoundaryTests`), las carpetas `Modules/<M>` por proyecto, un `*Registration` por capa y el bloque de `Program.cs`, los puertos de `Interfaces/Channels` con `TryAdd`/`Replace`/`TryAddEnumerable`, las migraciones de un módulo (quedan en la cadena del núcleo; quitarlo genera la suya) y los ganchos de módulo en los tests. Además: el árbol; "Reglas de ubicación y acceso" 5; "Una sola forma de guardar" (el orden de los locks con los participantes, la lista de claves sin las de WhatsApp, las excepciones de `WhatsAppWebhookService` y los hosts pasan a nombrar "el webhook del módulo WhatsApp" con enlace); "Colas en memoria"; "Convención de sufijos" (la lista de helpers de hoy sale y queda la regla, con los helpers en `Services/<Área>` o `Modules/<M>/Services`); "Borde HTTP" (`[OwnProtocol]`); "Tests" | 1 a 10 |
| **Tarea 3 de la Etapa 5** (nombres del producto en `backend.md`) | **Sí corresponde, en la tanda 10:** la lista de los diecisiete contratos de `Interfaces/Services` (`backend.md:134`), el árbol con cada controller y servicio, y la lista de helpers de hoy (`:224`) se reemplazan por la regla y un enlace a `docs/features/`; quedan los nombres de la plantilla (Roles, el área de referencia por el ADR 0004; `UnitOfWork`, `DatabaseSeeder`, `PersistenceRegistration`, `SignInService`, `PermissionService`) y los ejemplos que ilustran una regla. Con eso se tilda la tarea 3 de la Etapa 5 | 10 |
| [`AGENTS.md`](../../AGENTS.md) | "Dónde va cada cosa": filas "Módulo opcional (hoy WhatsApp)" y "Puerto del núcleo hacia un módulo"; "Registro en DI": los `*Registration` de un módulo registran sus repositorios; "Más documentación": la guía nueva; el primer párrafo nombra los módulos quitables | 2, 3, 5, 10 |
| [ADR 0007](../decisions/0007-whatsapp-como-modulo-opcional.md) | "Implementada el <fecha>", con los commits; las correcciones de la sección 2.2 en una sección "Enmiendas" fechada (como el ADR 0001); las Consecuencias con lo que de verdad hay que borrar | 10 |
| [`identidad.md`](../features/identidad.md), [`administracion.md`](../features/administracion.md) | lo de las tandas 4 a 8; la regla del 9 argentino y la de lo que carga un administrador | 4 a 10 |
| `AGENTS.md` de carpeta | uno por raíz de módulo (`src/<P>/Modules/WhatsApp/AGENTS.md` + `CLAUDE.md` con `@AGENTS.md`), con la línea de hoy de `Services/WhatsApp` sobre el reparto del bot; `Api/Controllers/AGENTS.md` sin `WhatsAppWebhook` (dice dónde están los del módulo); `Services/Users/AGENTS.md` sin `WhatsAppInvitationIssuer`; `Infra/Phones/AGENTS.md` apunta a `identidad.md` | 2, 3, 5, 10 |
| [`despliegue.md`](../guides/despliegue.md) | las rutas de `WhatsAppRegistration.cs` (líneas 70 y 77) y la convivencia de las dos migraciones de la tanda 8 | 2, 8 |
| Plan maestro | la casilla de cada tanda con su commit; al cierre, el estado de la Etapa 6 y de la tarea 3 y la guía de la Etapa 5 | todas |

---

## 9. Riesgos, dudas y lo que no cambia

### 9.1 Riesgos y cómo se mitigan

| Riesgo | Cómo se ve | Mitigación |
|---|---|---|
| Deadlock 40P01 por el orden de los locks | 500 en el perfil o la administración mientras el bot contesta | `PhoneNumberLinker` llama a los participantes antes de `login-link:`; `PhoneNumberLinkerTests` fija el orden; los cinco de concurrencia de `MeWhatsAppEndpointsTests` en cada tanda, tres veces en la 7 |
| La app arranca sin WhatsApp y no avisa (un puerto apagado que el módulo no reemplazó) | `whatsapp: false` con WhatsApp configurado | `TryAdd` + `Replace`; `WhatsAppApplicationRegistrationTests` en los dos órdenes; `LoginMethodsControllerTests` del módulo con WhatsApp prendido |
| El núcleo nombra el módulo por un camino que el IL no ve | la prueba de fuego no compila | la regla del fuente (6.1) desde la tanda 1; el ensayo de la tanda 9 |
| Un test que se pierde al partir los mixtos | cobertura menor sin que nada falle | se comparan los totales y las listas de nombres antes y después (tanda 9) |
| Un atributo que se pierde al partir un controller | cambia el OpenAPI, el rate limit o la autorización | `OpenApiTests`, `ExplicitRouteInventoryTests`, `ApiAdministrationHttpContractsTests`, `AuthConnectAccountContractTests` sin tocar en la 3 |
| Un contrato con el front que cambia | el front deja de entender una respuesta | decisión 7; ninguna aserción de status, código o JSON cambia; las únicas aserciones que cambian están nombradas (tandas 3 y 8) y ninguna es HTTP |
| Las migraciones no conviven con la imagen anterior | durante un despliegue, errores al leer códigos por teléfono o invitaciones | la plantilla no tiene producción con datos; se documenta en la guía y en `despliegue.md`; duda 1 |
| Un diff de mudanza imposible de revisar | se cuela un cambio de comportamiento en la tanda 2 | la tanda 2 es solo `git mv` + namespaces + `Set<T>()`; `git diff -M --stat`; las lógicas en commits aparte |
| El módulo supera el tope de 8 dependencias | `ServiceDependencyLimitTests` rojo | los helpers de la sección 3 ya vienen contados (`WhatsAppCodeIssuer` 8, `ProfileWhatsAppService` 8, `UserInvitationIssuer` 4, `UserQueryService` 6) |

### 9.2 Dudas para el usuario (no frenan la ejecución)

Después de la revisión adversarial quedan dos, las dos con recomendación; ninguna frena las tandas 1 a 7.

1. **Migraciones que no conviven con la versión anterior (tanda 8).** Rompen la regla de la guía de la migración para un despliegue con dos versiones a la vez: entre el bundle y la imagen nueva, la vieja no lee un código con `'Phone'` ni la columna `WaMessageId`. Si algún proyecto derivado ya está en producción con WhatsApp, la alternativa es hacerlo en dos pasos (agregar `ProviderMessageId` y copiar; en un despliegue posterior, borrar `WaMessageId`; y para el canal, que la versión anterior aprenda a leer `Phone` antes). **Recomendación:** un solo paso, como dice la decisión 5, porque la plantilla no tiene producción con datos; la guía y `despliegue.md` lo avisan para los derivados. Se pregunta solo para confirmar que no hay un derivado en producción.
2. **Sin el módulo, la web no deja cargar números nuevos.** La decisión 4 (una cuenta solo con teléfono, de cualquier país, sin canal) vale en la Api, pero el front, que no se toca (decisión 7), muestra el campo del teléfono solo con `whatsapp: true` en `login-methods` (`UserFormDialog.tsx:87-88,220`, `UserEditDialog.tsx:180-181,430`). Sin el módulo, las cuentas que ya tienen número lo muestran y se pueden desvincular, pero no se agregan números desde la web. **Recomendación:** aceptarlo y decirlo en `quitar-whatsapp.md`; un proyecto sin WhatsApp no tiene cómo usar un número (nadie le manda un código), así que no ofrecerlo es lo coherente. Cambiarlo pediría tocar el front, fuera de esta etapa.

Dejan de ser preguntas, porque se siguen de las decisiones del usuario:

- **Las dos aserciones de `WhatsAppRegistrationTests` que cambian** (hallazgo 5): con el webhook apagado, los servicios de Application del webhook quedan registrados y nadie los resuelve; no cambia nada visible. Conservarlas obligaría a que Infrastructure registre servicios de Application, justo lo que la decisión 2 saca. Se decide así y se explica en el commit de la tanda 3.
- **Sin módulo, una invitación con `channel: "WhatsApp"` responde `InvitationWhatsAppUnavailable`**, el mismo código, campo y texto que hoy con WhatsApp apagado. `UserInvitationChannel.WhatsApp` sigue siendo un valor del núcleo (decisión 7) y la clave sigue en su `.resx` (decisión 6): que el núcleo diga "WhatsApp no está configurado" para ese valor es exacto. Una clave genérica recién tiene sentido con un segundo canal opcional.

### 9.3 Lo que no cambia (los contratos con el front)

- **Rutas y verbos:** las 43 combinaciones de `ExplicitRouteInventoryTests`, incluidas `POST /account/login-code/whatsapp`, `POST /api/me/whatsapp/code`, `PUT` y `DELETE /api/me/whatsapp`, `DELETE /api/users/{id}/whatsapp` y `GET`/`POST /webhooks/whatsapp`, con la misma autorización, el mismo rate limit (`login-code`, `login-verify`, `whatsapp-webhook`) y la misma aparición condicional.
- **Cuerpos y respuestas:** los mismos contratos (`*HttpRequest`) con los mismos nombres de JSON; `LoginMethodsResponse` con `google`, `whatsapp`, `whatsappCountries`, `whatsappNumber`; `lastInvitation.deliveryStatus` con los mismos valores; `invitation.channel` con `"Email"` y `"WhatsApp"`.
- **Errores:** los mismos códigos (`Auth.WhatsApp.CountryNotSupported`, `Users.Invitation.*`, `Users.Phone.*`), los mismos textos en los dos idiomas y el mismo campo en `errors`.
- **Configuración:** las mismas claves (`WhatsApp:*`, `RateLimiting:WhatsAppWebhook*`), con los mismos valores por defecto y la misma validación al arrancar.
- **Comportamiento:** la regla de oro, los duplicados del webhook, el procesador, la retención (90 días), la cola de envío, el tope diario, los límites por destino y el orden de los locks.
- **Datos:** ninguno se pierde; los dos renombres se migran (tanda 8) y `LoginAudits.Method` no se toca.
