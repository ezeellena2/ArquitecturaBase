# Administración: ajustes, usuarios y roles

Leelo antes de tocar la administración: los ajustes del sistema (`SystemSettings`) y el modo de registro, el alta, la desactivación o el borrado de una cuenta, los roles del sistema, las guardas que impiden romper el sistema, o el listado de usuarios con sus filtros y conteos. El código vive en `Application/Services/{Settings,Users,Roles}`, en `UserReader` y en los controllers `SettingsController`, `UsersController` y `RolesController`. `UsersController` usa además `ISystemSettingsService` para resolver el tamaño omitido del listado; sus otros tres servicios son: `IUserQueryService` (el listado, los conteos y el detalle), `IUserAdministrationService` (el alta, la edición y el reenvío de la invitación, con los pasos de `UserContactLinker` y `UserInvitationIssuer`) e `IUserAccessService` (activar o desactivar, borrar y desvincular el número: desactivar y borrar cortan el acceso; desvincular el número propio conserva la sesión). Las reglas generales de la plantilla están en [`AGENTS.md`](../../AGENTS.md); el ingreso y el número de teléfono, [`identidad.md`](identidad.md), y, con el módulo WhatsApp, lo que toca el número de una cuenta sigue además [`whatsapp.md`](whatsapp.md). Roles es además el área de referencia para copiar ([ADR 0004](../decisions/0004-roles-como-area-de-referencia.md)). Lo propio de esta área en el límite de transacción está en [backend.md, "Una sola forma de guardar"](../architecture/backend.md#una-sola-forma-de-guardar): `SystemSettingsReader` lee en su propio scope cuando se lo llama adentro de un límite.

## Reglas

Las invitaciones de WhatsApp conservan consentimiento y plantilla con WANT_TO_ENTER. Su respuesta orienta a `/login` y continúa con un código explícito; no emite enlaces secretos ni confirma el teléfono desde el webhook. Los botones históricos conservan sus IDs. Véase [separación de recorridos](whatsapp.md#propósito-migración-y-recorridos--2026-10-02).

- **Ajustes del sistema:** `SystemSettings` es una entidad de **una sola fila**, auditable. El modo usado por ingreso y registro se lee desde Redis (TTL de 60 segundos); el panel lee la base. El caché se descarta después del commit del cambio y del seed. Una fábrica anterior al descarte no puede volver a publicar su valor viejo. Si el descarte falla después del commit, la escritura queda confirmada y la respuesta es 500; el TTL acota el atraso tras recuperar Redis ([reglas de caché](../architecture/backend.md#caché-compartido-en-redis)). El seed la crea con `Registration:Mode`: la plantilla configura `Open` en `appsettings.json` desde 2026-09-30; sin configuración conserva `InviteOnly` como respaldo restrictivo. **Si la fila ya existe, manda la base**: un despliegue nunca pisa lo que se configuró desde el panel.
- **El modo de registro** decide quién puede *crear* una cuenta, no quién puede entrar. `POST /account/login-code` responde `403 Auth.Account.Disabled` si la cuenta está deshabilitada, sin emitir ni enviar el código; para los demás destinos conserva el `202`, y en `InviteOnly` un correo sin cuenta **igual emite y guarda su fila de `LoginCode`**: lo único que no pasa es que se mande el email. La fila se emite a propósito y no hay que "optimizarla": los límites por dirección se apoyan en ella, y sin ella una dirección desconocida respondería `202` para siempre mientras una registrada empieza a responder `429`, que es todo lo que hace falta para enumerar cuentas. Vence sola a los 10 minutos sin que nadie la use. Con Google, al intentar crear una cuenta no admitida por la política, la persona ya probó ser dueña de la dirección y vuelve al acceso con `Auth.Account.NotInvited`. Lo mismo pasa si alguien llega a verificar un código válido para un correo sin cuenta (por ejemplo, porque el modo cambió con el código en vuelo): el verify que intenta crear la cuenta responde `403 Auth.Account.NotInvited`; el ingreso explícito sin cuenta usa `NotRegistered` según el caso descrito abajo. La única excepción es el administrador inicial (`Seed:AdminEmail`), que crea su cuenta en cualquier modo, por código o con Google, porque se crea en su primer ingreso y sin esto una base nueva en `InviteOnly` no deja entrar a nadie; la regla vive solo en `AccountCreationPolicy`, y el pedido le responde igual que a cualquier otro correo (lo único distinto es que el código le llega).
- **La intención de acceso también cuenta.** Con `register=false`, una identidad sin cuenta responde `Auth.Account.NotRegistered` después de probarla, salvo el administrador inicial. Con `register=true`, una cuenta activa existente responde `Auth.Account.AlreadyRegistered`; una nueva se crea solo si la política lo permite. Omitir `register` conserva el comportamiento anterior de crear durante el ingreso cuando está permitido. Las reglas y el consumo del código están en [identidad](identidad.md#ingreso-y-registro-explícitos-2026-09-30); `Auth.Account.NotInvited` corresponde al rechazo de creación por la política, no reemplaza todos los rechazos de intención.
- **Desactivar o eliminar tiene que cortar el acceso en el momento:** además de marcar la fila, se revocan las autorizaciones y los tokens de OpenIddict y se actualiza el `SecurityStamp` para invalidar la cookie. Sin eso, "desactivar" es una etiqueta que no impide nada durante los 15 minutos que vale el access token.
- **`ApplicationUser` es `ISoftDeletable`:** un usuario borrado desaparece de los listados y no puede entrar, pero su historial de ingresos sigue existiendo. Dar de alta el mismo correo restaura la cuenta, con los roles que diga el alta, no con los que tenía antes.
- **Lo que carga un administrador queda sin verificar**, el correo también. Se verifica cuando la persona lo usa: al entrar con el código del correo, al vincular Google o, con WhatsApp, al ingresar con el código del número ya cargado o al vincular/verificar el número desde el perfil o la edición de su propia cuenta, que abre la misma verificación por código. El ingreso por código confirma el número de la cuenta; no vincula por sí solo el contacto de atención. Dar de alta un correo o un número de una cuenta borrada la restaura, pero solo si todo lo que se cargó es de esa misma cuenta: puede completar lo que falte, nunca reemplazarlo. Desvincular el número desde la administración (`DELETE /api/users/{id}/whatsapp`, `UserAccessService`, del núcleo aunque la ruta diga `whatsapp`) cierra las sesiones con `AccountAccessRevoker` si es otra cuenta, como desactivar o borrar; sobre la propia, conserva las sesiones y anula los enlaces pendientes; desvincularlo desde el propio perfil (`DELETE /api/me/whatsapp`, `ProfilePhoneService`, también del núcleo) no las cierra, con el módulo prendido, apagado o quitado.
- **El número de una cuenta cambia siempre por `PhoneNumberLinker`** (`Application/Services/Users`, del núcleo). Reúne los locks, la anulación de los enlaces, quitar el número y soltarlo, pasos que la edición y el desvincular de la administración comparten con el perfil (vincular o reemplazar el número propio mediante verificación corresponde al perfil; ingresar por código también confirma un número ya cargado, sin vincular el contacto de atención; el desvincular de la administración, cuando la cuenta tiene número, anula los enlaces con `AccountAccessRevoker`, que además cierra las sesiones si es otra cuenta; sobre la propia usa `VoidPendingLinksAsync`), y les avisa a los participantes del número (`IPhoneLinkParticipant`; sin ninguno, el número es solo un dato de la cuenta). `PhoneNumberLinker.LockAsync` toma los locks de los participantes y después el de la cuenta (`login-link:`), y quien lo llama lee la cuenta **después** de tomarlos: mientras espera, otra transacción puede escribirla (por ejemplo, otra confirmación del perfil), y guardar con lo leído antes choca con el `ConcurrencyStamp` de Identity. **Todo cambio de número anula los enlaces de ingreso pendientes de la cuenta** (reemplazarlo en la edición, desvincularlo o, con WhatsApp, confirmar otro desde el perfil): el chat del número que se va puede no ser más de esa persona. Reemplazar el número en la edición no cierra las sesiones; desvincularlo desde la administración las cierra solo si es otra cuenta. Lo prueban `UpdateUserContactTests.Changing_the_phone_voids_the_links_of_the_previous_one_but_keeps_the_sessions` y `UnlinkUserPhoneEndpointTests.Unlinking_removes_the_number_voids_the_links_and_closes_the_sessions`.
- **El país de un número nuevo** (en el alta y la edición) lo decide el canal telefónico: `UserContactLinker` le pregunta a `IPhoneChannel.EnsureCanSendTo`. Con WhatsApp responde `Auth.WhatsApp.CountryNotSupported` fuera de `WhatsApp:AllowedCountries`, salvo que la cuenta ya tenga ese número; sin un módulo, `DisabledPhoneChannel` acepta cualquier país. Una cuenta con solo teléfono se permite aunque no haya canal, porque un administrador le puede agregar un correo. El front muestra el campo del teléfono solo con `whatsapp: true` en `login-methods`: sin el módulo, desde la web no se cargan números nuevos, y una cuenta que ya tiene uno lo muestra y un administrador lo puede desvincular.
- **Las reglas que protegen al sistema viven en `Application/Services/Users/UserGuard.cs`**, con sus tests unitarios en `Application.UnitTests`. No están en Domain porque hay que contar administradores activos mediante un lector: nadie se saca a sí mismo el rol `Admin`, nadie desactiva ni elimina su propia cuenta, siempre queda al menos un usuario activo con rol `Admin`, y no se borra un rol con usuarios asignados. Además, nadie desvincula su único medio de ingreso (`HasOtherLoginMethodAsync`: otro medio es un correo verificado o un Google vinculado; un correo sin verificar no cuenta). Un administrador sí puede dejar a otra cuenta sin ninguno, por el caso del teléfono robado; sobre la propia vale la misma regla (con WhatsApp, la misma que en el perfil), y `DELETE /api/users/{id}/whatsapp` responde `409 Users.User.LastLoginMethod` (`EnsurePhoneCanBeUnlinkedAsync`).
- **El conteo del último administrador va en fila.** Cuando la regla aplica (se desactiva, se elimina o se le saca el rol `Admin` a un administrador activo), `UserGuard` toma el advisory lock global `users:admins` (`IUserRepository.LockAdminsAsync`) justo antes de contar, después de los locks de los participantes del número (con WhatsApp, los contactos) y de cuenta que ya tomó el caso de uso. Sin él, dos administradores que se desactivan (o se borran, o se sacan el rol) entre sí a la vez cuentan dos cada uno y dejan el sistema sin administradores; con él, el segundo cuenta después del commit del primero y recibe `Users.User.LastAdmin`. El conteo (`IUserReader.CountActiveAdminsAsync`) es un `CountAsync` en SQL sobre `userManager.Users`, con la semántica de `UserManager.GetUsersInRoleAsync` más `IsActive`: el filtro global de borrados, la unión `AspNetUserRoles` → `AspNetRoles` y el rol por su `NormalizedName` (normalizado con el `UserManager`), no por claim; lo caracteriza `UserReaderTests.Admin_count_includes_only_active_and_not_deleted_accounts`. Una edición que no le saca el rol `Admin` a un administrador activo, o la baja de alguien que no lo es, no lo toma. Lo prueban `AdminsLockTests` (el orden, con los dobles) y `LastAdminLockTests` (con Docker: el caso de uso espera el lock tomado desde otra conexión, y dos desactivaciones cruzadas dejan exactamente un administrador).
- **Las invitaciones salen por canales.** `UserInvitationIssuer` busca el canal pedido, toma el lock de invitaciones de la cuenta, guarda la invitación (con el consentimiento, si el canal lo registra) y le pide al canal que encole el mensaje. Cada canal es un `IInvitationChannel` con sus propias reglas, en el orden en que el front muestra el primer error: el correo lo trae el núcleo (`Application/Channels/EmailInvitationChannel`, que pide un correo) y WhatsApp, su módulo (`WhatsAppInvitationChannel`: WhatsApp prendido, un número, el consentimiento y el nombre). Sin un canal para el pedido, la invitación se rechaza en el campo del canal con el mismo mensaje que WhatsApp apagado. Si la cola no toma el mensaje, el canal deja la invitación como no enviada (`MarkSendFailed`, con un log) y el alta no se deshace. Entre dos invitaciones a la misma cuenta hay que esperar un minuto (`UserInvitation.ResendCooldown`), contado desde la última que salió: antes, `POST /api/users/{id}/invitation` responde `429 Users.Invitation.TooManyRequests` con `retryAfter`. Ese 429 no sale del rate limiter sino del servicio, y por eso la acción lo declara con `[ProducesProblem]`. Una invitación que no salió (`SendFailed`) no hace esperar.
- **El estado de entrega de la última invitación** (`lastInvitation.deliveryStatus` en `GET /api/users/{id}`) es `Failed` si no se pudo mandar (`SendFailed`: la cola no la tomó o, por WhatsApp, Meta la rechazó al enviarla), por cualquier canal y sin preguntarle a la fuente. Si salió, lo da la fuente de su canal (`IInvitationDeliveryStatusSource`), si tiene una: con WhatsApp, la suya, con el estado que avisa Meta del mensaje saliente ([whatsapp.md](whatsapp.md)). Sin fuente, como el correo, queda en `null`. `UserQueryService` le pide el estado a la fuente con el id que dio el proveedor (`UserInvitationRow.ProviderMessageId`), y `LastInvitation.From` junta las dos cosas.
- **`Admin` y `User` son del sistema:** no se renombran ni se borran, y a `Admin` no se le editan los permisos.
- **Roles tiene un catálogo y un listado.** `GET /api/roles` es el catálogo completo, sin paginar y ordenado por nombre, para los selectores del front. `GET /api/roles/paged` es el listado paginado de la administración (`ListRolesRequest`: `page`, `pageSize`, `sort` por `name` o `createdAtUtc`, y `search`, que busca sin distinguir mayúsculas en el nombre y en la descripción, con `%` y `_` como literales). Los dos, y el detalle `GET /api/roles/{id}`, devuelven cada rol con la misma forma (`RoleResponse`) y salen de una sola proyección en `RoleReader`. Es una ruta aparte y no una query opcional porque una operación no puede declarar dos esquemas de respuesta ([ADR 0004](../decisions/0004-roles-como-area-de-referencia.md), enmienda); un área nueva pone su listado paginado directamente en `GET /api/<recurso>`.
- El catálogo del núcleo es `users.read`, `users.manage`, `roles.read`, `roles.manage` y `settings.manage`. `IPermissionCatalog` agrega las fuentes de módulos presentes; con WhatsApp suma `whatsapp.read`, `whatsapp.send` y `whatsapp.manage`. Roles y seed consumen ese catálogo compuesto; retirar una fuente deja sus claims históricos sin efecto ([ADR 0010](../decisions/0010-catalogo-de-permisos-extensible.md)).

## Filtros de un listado

Los filtros de un listado (desde la Fase 5):

- **viven en `Application/Models/Users/ListUsersRequest.cs`**, el mismo pedido para el listado y los conteos; un solo validador (`ListUsersRequestValidator`) y `UserReader` aplican el mismo filtro. Listado y conteos deben filtrar **exactamente igual** o los conteos dejan de describir el listado.
- **Un valor que no existe no es un 400, es una lista vacía.** `role=NoExiste` devuelve cero resultados: contestar 400 diría qué nombres de rol existen, y eso no se cuenta por el camino de un filtro. Lo que sí es 400 es un `role=` **presente y vacío**, porque el parámetro ausente ya significa "sin filtro" y devolver todo parecería un filtro roto.
- Los filtros se enlazan en el controller MVC y se comparan por la columna que tiene índice (para un rol, `NormalizedName`, no `Name`).
- Las fechas relativas salen de `TimeProvider`, nunca de `DateTime.UtcNow`. En los tests de integración se mueven con `factory.Clock.Advance`, que es el mismo reloj que usa el interceptor de auditoría: no se toca `CreatedAtUtc` a mano.

## Conteos por opción de filtro

Los conteos por opción de filtro (`GET /api/users/filter-counts`):

- cada dimensión se cuenta **con los demás filtros puestos e ignorando el propio**. Es toda la gracia: con "solo activos" puesto, el número de Admin es cuántos activos quedarían al elegir Admin, y "Inactivos" sigue diciendo cuántos hay del otro lado en vez de 0.
- el catálogo viene **completo**, con los que dan cero: la opción apagada tiene que poder verse, y para eso hay que saber que existe.
- las opciones de un tramo (los días) viven en el backend, al lado del filtro (`ListUsersRequest.CreatedWithinOptions`): el que cuenta y el que dibuja las opciones tienen que estar de acuerdo.
- es un endpoint aparte y no un campo de `PagedResult<T>`, que es genérico y lo comparten todos los listados: meterle facetas lo ataría a este caso.
- **Cuesta cuatro consultas de agregación por pedido** (estado, roles y una por tramo). Con miles de usuarios es despreciable; con cientos de miles hay que medir antes de sumar dimensiones.


## Configuración general (2026-09-30)

La configuración general está implementada; este apartado conserva su contrato vigente.
La fila auditable agrega DefaultCulture (es/en, inicial es), DefaultTimeZoneId (IANA, inicial
America/Argentina/Buenos_Aires), DefaultPageSize (10/20/50/100, inicial 20) y Revision (inicial 1).
La migración agrega defaults sin modificar cuentas ni RegistrationMode existentes.

GET /api/settings requiere settings.manage y lee la fila actual. PATCH /api/settings requiere
el mismo permiso y uno o más ajustes conocidos no nulos, más expectedRevision positiva.
Desde la elección del 2026-10-01, el formulario general guarda sus cambios juntos; un pedido
vacío, desconocido o inválido responde 400 y no cambia ningún ajuste. El servicio valida antes de su único
límite OnSuccess, bloquea la fila con SELECT FOR UPDATE y compara Revision antes de modificarla.
Una revisión desactualizada responde 409 Settings.System.RevisionConflict. Cada escritura
confirmada incrementa Revision una sola vez, aunque cambien varios campos. PUT conserva su
contrato de registro y preserva los otros ajustes. Los pedidos de un solo ajuste siguen siendo válidos.

El front presenta `/configuracion` con tres superficies: preferencias regionales, listados y
registro, una cabecera y Guardar/Descartar comunes. Las cuatro rutas anteriores redirigen a esa
pantalla. Cambiar el registro requiere confirmar su consecuencia antes de guardar el conjunto.
Ante un conflicto o resultado incierto se conserva el borrador y se comparan los cuatro valores.
Conservar las ediciones adopta cambios concurrentes en campos que la persona no había tocado;
no sobrescribe esos cambios al repetir el guardado.

GET /api/settings/presentation es anónimo: publica cultura, zona, tamaño inicial y apariencia,
sin registro, revisión ni auditoría. Su caché settings:presentation:v2 tiene TTL de 60 segundos y
fábrica en scope propio; settings:system conserva el modo de registro. Servicio y seed descartan
ambas claves después del commit. Un 500 posterior al commit exige releer, no repetir la escritura.

Las altas administrativas copian cultura y zona generales; código y Google usan la
cultura de la petición localizada y la zona general. Toda creación productiva pasa ambas
preferencias explícitamente al repositorio. El parámetro opcional del puerto conserva solo
compatibilidad con preparaciones técnicas existentes. Ingresar o restaurar no cambia preferencias.
Los correos e invitaciones conservan el idioma del perfil destinatario.

GET /api/users y GET /api/roles/paged usan el tamaño general si pageSize se omite. Los controllers
lo obtienen por ISystemSettingsService y mapean el pedido; Application conserva validación 1..100.
GET /api/roles sigue siendo el catálogo completo para selectores. UTC y el máximo técnico no son ajustes.

## Apariencia global (2026-10-01)

La elección del usuario suma Configuración → Apariencia (`/configuracion/apariencia`) con
`settings.manage`. SystemSettings guarda cuatro columnas de longitud 7: PrimaryColor,
MenuColor, HeaderColor y AlternateRowColor. La migración SystemAppearance rellena instalaciones
existentes con Jade (#176B4C, #194F3A, #D0E5D5, #F2F7F3), sin cambiar el resto de sus ajustes.

Elección del 2026-10-02: la base pasa a Campo (#285A3D, #24352B, #DFE8D9, #F4F6F0),
solo colores y con los componentes y medidas existentes. `FieldAppearanceDefaults` cambia
los defaults del esquema y actualiza una fila únicamente si sus cuatro colores coinciden
con Jade original, sin distinguir mayúsculas. Un solo color personalizado conserva todo el
grupo. El cambio registra una revisión y auditoría técnica en UTC, sin atribuirlo a una cuenta.
La migración inversa aplica el mismo criterio a Campo completo; los demás ajustes no cambian.

PATCH /api/settings acepta `appearance` como objeto completo con primaryColor, menuColor,
headerColor y alternateRowColor, todos en formato #RRGGBB, y expectedRevision. El dominio
valida todo antes de mutar y normaliza a mayúsculas. La revisión avanza una sola vez;
se conservan el lock, la transacción OnSuccess y el descarte de caché posterior al commit.
Formatos inválidos, objetos incompletos/nulos o miembros desconocidos responden 400;
la validación del color identifica appearance.<campo>. Se puede enviar junto a ajustes
generales, con la misma atomicidad; en el SPA, General y Apariencia son pantallas distintas.

La proyección pública incluye appearance para todas las personas, acceso público y portales.
El front deriva contraste de texto/foco/hover, conserva los colores semánticos y muestra
un borrador local con Guardar/Descartar y Restaurar colores iniciales (Campo). Restaurar no escribe hasta Guardar.
Un conflicto o resultado incierto conserva los cuatro colores del borrador y exige releer
y comparar con los actuales antes de repetir. Elegir Conservar mis cambios acepta la
revisión actual y mantiene la paleta completa del borrador.
