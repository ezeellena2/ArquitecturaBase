# Revisión de arquitectura: el núcleo — 2026-11-14

Segunda corrida de [revisar la arquitectura de un desarrollo](../guides/revisar-arquitectura.md) y la primera
sobre el núcleo: todo lo que está fuera de `Modules/` y sus pantallas en el front. Es de solo lectura, sobre
el backend en `23797fff` y el front en `7b95095b`; no incluye los cambios sin commitear de otras sesiones (ese
día seguían los arreglos de WhatsApp y la lectura de documentos).

- **Revisión:** siete revisores contestaron los 30 criterios, uno por pieza de la [ficha](#ficha).
- **Verificación:** siete verificadores recibieron el escenario y el código, sin la conclusión, e intentaron
  refutar cada «No cumple» y «Con reservas». Refutaron el efecto de cuatro (las claves foráneas que faltan, la
  historia única de migraciones, el 500 de un único diferido y un comentario tomado por bug); el resto quedó con
  ajustes de horizonte, severidad o arreglo, ya incorporados abajo. También sumaron hallazgos: la toma de la
  cuenta de un administrador con «Administrar usuarios», la copia que borra lo que no puede descifrar, los
  chequeos de salud que se cortarían al restringir `AllowedHosts` y el asistente de WhatsApp, que sin Formularios
  no anda.
- **Contraste:** un revisor independiente comparó después este informe con los originales y con el código; sus
  correcciones están aplicadas.
- **Sin ejecución:** no se corrió nada. Los tiempos y los techos salen del código, y las pruebas que los
  confirmarían están en [controles y pruebas](#controles-y-pruebas-que-conviene-sumar).
- **Lo que ya registró WhatsApp** se cita y no se repite: H3, H8, H9, H10, H14, H15, H19, H25, H26, H32 y H34 de la
  [revisión de WhatsApp](2026-11-12-arquitectura-whatsapp.md), que tocaron piezas del núcleo.

## Resumen

- **Lo fuerte.** Las piezas que más se reutilizan están bien resueltas y probadas:
  - guardar: un solo límite por caso de uso, sin anidar, con locks que se sueltan y el único traducido a 409;
  - la cola durable: el correo se encola cifrado dentro de la transacción y un rollback lo deshace;
  - el ingreso: códigos guardados como HMAC y comparados en tiempo constante, límites por destino bajo lock y
    la sesión que se corta en el acto al desactivar una cuenta;
  - los permisos se leen en cada pedido: un cambio vale en el pedido siguiente y en todas las copias.
- **Hoy, con una sola copia**, lo que más pesa:
  - delegar «Administrar usuarios» o «Administrar roles» alcanza para tenerlo todo (N1);
  - dos personas que editan el mismo usuario o el mismo rol se pisan sin aviso (N2);
  - cualquiera puede dejar bloqueada la cuenta de otro, y saber si existe (N3);
  - restaurar una cuenta borrada le devuelve el acceso a su dueño anterior (N4);
  - con el correo caído, códigos e invitaciones se pierden sin que nadie se entere (N5);
  - `/health` es pública y el pool de conexiones no está dimensionado para la base del despliegue (N6).
- **Con varias copias**, falla la convivencia de versiones en la cola durable y en los modelos que guardan los
  módulos (N27), que pide la misma regla que H25; y la limpieza de archivos corre en cada copia (N28). Los
  límites y la salud de cada copia siguen siendo H26.
- **A gran escala**, el techo está en el lock global del correo (N29), el listado de usuarios (N30), el tope
  diario de WhatsApp (N31), el conteo de personas por rol (N32), los permisos que piden otra conexión dentro de
  una transacción (N33) y las migraciones que bloquean tablas (N34).
- **Fuera de los criterios:** la plantilla trae datos de esta instalación y no arranca en Development sin el
  token de WhatsApp (B1), y sin Formularios el asistente de WhatsApp no anda, aunque la guía para quitarlo diga
  lo contrario (B5).
- **Lo que decidiste el 2026-11-14** ([detalle](#decisiones-del-2026-11-14)):
  - nadie da lo que no tiene, ni actúa sobre una cuenta con más permisos que los suyos (N1);
  - el bloqueo por códigos deja de servir contra otros (N3);
  - restaurar una cuenta quita el acceso que el alta no trajo (N4);
  - con Redis caído, la app degrada de verdad (N9);
  - el correo de producción sigue en Gmail, con los topes de N7;
  - una sola regla de versiones para el núcleo, WhatsApp y los módulos (N27);
  - retención: códigos y enlaces, 30 días; la IP de los registros de ingreso, 12 meses; OpenIddict vencido,
    14 días (N16);
  - la invitación sigue saliendo en el idioma general (N24), y la pantalla no deja quitarle Admin al
    administrador inicial (N25);
  - el enlace de ingreso se retira (B4);
  - el horizonte es «Hoy», más N27 y lo trivial de N28, N30 y N31.

## Ficha

**Entradas**

- **Rutas** (`src/ArquitecturaBase.Api/Controllers`):
  - anónimas: `connect/*` (OpenIddict: autorizar, token, cerrar sesión, userinfo), `account/external/google`
    y su vuelta, `account/login-code` (pedir y verificar), `account/login-link` (ver y canjear),
    `account/login-methods` y `api/settings/presentation`;
  - con sesión: `api/me` (perfil, correo con código, quitar WhatsApp);
  - administración: `api/users` (listado, conteos, alta, edición, invitación, activar, desactivar, borrar y
    desvincular WhatsApp), `api/roles`, `api/permissions` y `api/settings`.
- **Borde:** los middlewares de `Program.cs` (encabezados reenviados, de seguridad, errores, idioma, límites,
  autenticación y autorización, SPA) y `/health` y `/alive` de `ServiceDefaults`.
- **Workers:** `EmailBackgroundService`, el correo por la cola durable; `PrivateFileCleanupBackgroundService`,
  la limpieza de archivos privados; y la migración y el seed al arrancar (`DatabaseInitialization`).
- **Pantallas** (`../ArquitecturaBaseFront/src`): `auth` (cliente OIDC y renovación), `features/auth`,
  `features/users`, `features/roles`, `features/settings`, `features/profile` y `shared/api`.

**Datos**

- **Tablas:** las de Identity y OpenIddict, `LoginCodes`, `LoginLinks`, `LoginAudits`, `UserInvitations`,
  `SystemSettings`, `QueuedDeliveries`, `StoredFiles` y `DataProtectionKeys`. Las tocan 20 de las 112
  migraciones.
- **Redis:** `settings:system`, `settings:presentation:v2`, los permisos por rol y versión y las leases de
  `RedisCache` (las usa WhatsApp), bajo el prefijo `{aplicación}:{ambiente}:{base}`.
- **En memoria:** los límites de pedidos por IP (H26).
- **Archivos:** el almacenamiento privado en Azure Blob (`StoredFiles`) y, en desarrollo, los correos como `.eml`.

**Terceros**

- Google (ingreso), SMTP (correo), Azure Blob, PostgreSQL y Redis.
- **Hacia los módulos:** 31 puertos en `Application/Interfaces/Channels`, con sus versiones apagadas en
  `Application/Channels`, los modelos neutros de `Models/StructuredData` y `Models/Assistant`, y las consultas
  del sistema que usa el asistente (`Channels/SystemQueries`).

**Reglas:** [identidad](../features/identidad.md) y [administración](../features/administracion.md); los ADR
[0001](../decisions/0001-transaccion-explicita-por-caso-de-uso.md) a
[0006](../decisions/0006-migraciones-y-seed-fuera-de-development.md),
[0008](../decisions/0008-nombres-de-repositorios-y-lectores.md) a
[0010](../decisions/0010-catalogo-de-permisos-extensible.md),
[0012](../decisions/0012-azure-blob-como-primer-proveedor-de-archivos.md),
[0013](../decisions/0013-composicion-de-modulos-en-tests.md),
[0015](../decisions/0015-colas-transaccionales-cifradas.md),
[0016](../decisions/0016-proteccion-de-claves-data-protection.md),
[0018](../decisions/0018-robustez-proporcional-al-efecto.md) y
[0019](../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md); el
[diseño base](../specs/2026-09-18-arquitectura-base-design.md); [despliegue](../guides/despliegue.md) y
[Azure](../deploy/azure-setup.md); y los `AGENTS.md` de cada carpeta.

**Pruebas:** integración en `tests/ArquitecturaBase.Api.IntegrationTests` (152 archivos fuera de `Modules`),
unitarias de Application (84) y de Domain (18), y arquitectura (39); en el front, 35 archivos de test en esas
pantallas.

**Tamaño:** 464 archivos `.cs` sin migraciones (Api 55, Application 263, Domain 42, Infrastructure 102,
ServiceDefaults 1 y AppHost 1); en el front, unos 125 archivos.

**Piezas:** A ingreso y sesión; B cuentas y perfil; C roles y permisos; D configuración, archivos, correo y cola
durable; E borde HTTP y operación; F persistencia, caché y migraciones; G puertos hacia los módulos.

## Matriz

| # | Criterio | Hoy | Varias copias | Gran escala | Hallazgos |
| --- | --- | --- | --- | --- | --- |
| 1 | Estado por pedido | Cumple | Cumple | Cumple | — |
| 2 | Ediciones simultáneas | No cumple | No cumple | No cumple | N2 |
| 3 | Repeticiones | Con reservas | Con reservas | Con reservas | N2, N8 |
| 4 | Orden | Con reservas | Con reservas | Con reservas | N2 |
| 5 | Todo o nada | Con reservas | Con reservas | Con reservas | N4, N28 |
| 6 | Cada uno lo suyo | No cumple | No cumple | No cumple | N1, N19, N25 |
| 7 | Sin estado propio | Con reservas | Con reservas | Con reservas | N14, H26 |
| 8 | Trabajos de fondo | Con reservas | Con reservas | Con reservas | N8, N28 |
| 9 | Arranque, apagado y configuración | Con reservas | Con reservas | Con reservas | N5, N6, N8, N9, N14, N17 |
| 10 | Despliegue sin corte | Con reservas | No cumple | No cumple | N27, N34 |
| 11 | Consultas | Cumple | Cumple | No cumple | N28, N30, N31, N32, N33 |
| 12 | Paginado y exportaciones | Cumple | Cumple | No cumple | N30 |
| 13 | Filtros y búsqueda | Cumple | Cumple | No cumple | N30 |
| 14 | Caché | Con reservas | Con reservas | Con reservas | N9, N19, N33 |
| 15 | Trabajo pesado fuera del pedido | Cumple | Cumple | Con reservas | Los archivos se procesan en el pedido de Formularios, por diseño: lo mira su revisión |
| 16 | Filas únicas | Con reservas | Con reservas | No cumple | N29, N33 y H32 |
| 17 | Plazos y reintentos | Con reservas | Con reservas | Con reservas | N5, N8, N18 |
| 18 | Estado incierto | Con reservas | Con reservas | Con reservas | N8, N18 |
| 19 | Degradación | No cumple | No cumple | No cumple | N5, N9, N22, N23 |
| 20 | Límites y costo | No cumple | No cumple | No cumple | N3, N6, N7, N20, N29, H26 |
| 21 | Acceso | No cumple | No cumple | No cumple | N1, N4, N11, N12, N25 |
| 22 | Entradas hostiles | Con reservas | Con reservas | Con reservas | N3, N6, N20 |
| 23 | Datos sensibles | Con reservas | Con reservas | Con reservas | N26 |
| 24 | Integridad en la base | Con reservas | Con reservas | Con reservas | N2 |
| 25 | Privacidad y respaldo | No cumple | No cumple | No cumple | N4, N15, N16, H14, H15 |
| 26 | Idioma | Con reservas | Con reservas | Con reservas | N24 |
| 27 | Fechas, zonas horarias y números | Con reservas | Con reservas | Con reservas | N21 |
| 28 | Errores | Con reservas | Con reservas | Con reservas | N2, N10, N22 |
| 29 | Observabilidad | Con reservas | Con reservas | Con reservas | N5, N10, N13, H19 |
| 30 | Separabilidad | Con reservas | Con reservas | Con reservas | N22, N23 y el [inventario](#separar-el-núcleo-de-los-módulos-inventario-no-defecto) |

Un solo criterio se cumple entero en los tres horizontes, pero en la mayoría de las filas «Con reservas» la
reserva es un hueco acotado y lo principal está bien (por ejemplo, logs limpios con un `ToString` sin redactar,
o fechas en UTC con dos pantallas que muestran números sin la región).

## Hallazgos

Cada hallazgo dice dónde empieza a fallar, el escenario, la evidencia (archivo y línea en `23797fff`, o
`front:` en `7b95095b`) y el arreglo con su esfuerzo. Cuando el comportamiento viene de una decisión
documentada, se cita la fuente: cambiarla lo decidís vos.

### Fuera de los criterios

#### B1. La plantilla trae datos de esta instalación y no arranca en Development sin el token de WhatsApp

- **Dónde falla:** al copiar la plantilla para otro negocio, y en cada clon nuevo del repositorio.
- **Escenario:** alguien clona la plantilla para un negocio nuevo y la levanta. La Api no arranca: WhatsApp
  viene prendido y pide su token, aunque el README dice que viene apagado y no lo nombra entre los secretos.
  La configuración de desarrollo trae además un correo personal como remitente y como administrador inicial,
  y la configuración base, el id del cliente de Google de esta instalación, que vale también en Production:
  la copia arranca con Google prendido y se frena sin su secreto.
- **Evidencia:** `README.md:150-155,194`, `appsettings.Development.json:33-34,38,41-43`, `appsettings.json:29`
  y `WhatsAppOptionsValidator.cs:19-23`. El README lo cuenta como estado de esta instalación
  (`README.md:144-148,159`), y la segunda instalación independiente sigue pendiente
  (`verificacion-y-produccion.md:159`).
- **Arreglo:** que la configuración versionada venga apagada y sin datos de esta instalación (los de acá, en
  user-secrets), y que el README nombre el token. Trivial.

#### B2. El editor de roles descarta sin avisar los permisos que su catálogo no conoce

- **Escenario:** mientras conviven dos versiones al agregar un módulo, o con el catálogo viejo todavía en el
  navegador, alguien guarda un rol que ya tiene permisos del módulo nuevo: el editor los filtra contra el
  catálogo que cargó y los quita, sin aviso.
- **Evidencia:** `front:src/features/roles/pages/RoleEditorPage.tsx:314-321`.
- **Arreglo:** lo cierra la revisión de N2, más un aviso de lo que se descarta. Chico.

#### B3. Después de quitar un módulo, el rol Admin no se puede editar por la API

- **Evidencia:** `RoleService.cs:186-190` compara contra todos los permisos guardados, sin filtrar por el
  catálogo (`RoleReader.cs:109-111`), y el validador rechaza los desconocidos (`ValidationRules.cs:70-75`):
  responde 403 aunque sólo cambie la descripción. El front no edita Admin, así que el efecto es mínimo.
- **Arreglo:** comparar sólo los códigos del catálogo. Trivial.

#### B4. El enlace de ingreso sigue vivo sin nadie que lo emita

- **Evidencia:** `LoginLinkIssuer` está registrado (`Application/DependencyInjection.cs:70`) y sólo lo usan los
  tests; las dos rutas de canje siguen abiertas. [Identidad](../features/identidad.md) lo documenta (`:43`),
  pero su razón, canjear los enlaces pendientes del bot, venció a los diez minutos de retirarlo.
- **Arreglo:** retirar el emisor y las dos rutas, con su regla. Trivial. **Lo decidís vos:** cambia
  `identidad.md:43`.
- **Decidido el 2026-11-14:** se retira ([decisiones](#decisiones-del-2026-11-14)).

#### B5. Sin Formularios, el asistente de WhatsApp no anda

- **Dónde falla:** en un proyecto que quita Formularios y conserva WhatsApp e IA. Lo encontró un verificador y se
  confirmó leyendo el código; no se ejecutó.
- **Escenario:** alguien sigue la guía para quitar Formularios, que promete que «WhatsApp e IA siguen andando». Desde
  ahí, ninguna conversación en modo Asistente recibe respuesta: el motor rechaza cada turno.
- **Evidencia:** el contexto del asistente de WhatsApp pide siempre capacidades `forms.*`
  (`WhatsAppAssistantAccessPolicy.cs:45-49,56`), y el catálogo del motor rechaza el contexto entero si falta cualquiera
  (`AiAssistantCapabilityCatalogPolicy.cs:53-56`). La guía dice otra cosa (`quitar-formularios.md:9-11`).
- **Arreglo:** que WhatsApp pida las capacidades de formularios sólo si están instaladas, con un test que componga
  WhatsApp e IA sin Formularios, y corregir la guía. Chico.

### Hoy, con una sola copia

#### N1. Delegar «Administrar usuarios» o «Administrar roles» equivale a ser administrador

- **Dónde falla:** hoy, apenas una instalación le da `users.manage` o `roles.manage` a un rol que no es Admin;
  con los roles del seed no pasa. Severidad alta.
- **Escenario:** el dueño crea el rol «Mesa de ayuda», con sólo «Administrar usuarios», para que Lucía dé de
  alta empleados.
  - Lucía abre su propia cuenta, tilda Admin y guarda: ya tiene todos los permisos.
  - Sin tocar roles, le cambia el correo a un administrador por el suyo, entra con el código que le llega y
    queda dentro de esa cuenta; el administrador no recibe aviso ni pierde sus sesiones.
  - Con «Administrar roles» pasa lo mismo por otro camino: esa persona se tilda todos los permisos en su
    propio rol, o se los da a «User», el rol que recibe toda cuenta nueva (el registro viene abierto).
- **Evidencia:**
  - la guarda de roles sólo impide quitarse Admin y dejar sin administradores; su comentario dice que lo demás
    se cambia libremente (`UserGuard.cs:40-65`);
  - el alta y la edición asignan cualquier rol existente y reemplazan correo y número de cualquier cuenta
    (`UserAdministrationService.cs:117-133,170-173,179-200`);
  - `RoleService` no sabe quién pide (`RolesController.cs:59-70`, `RoleService.cs:19-26,180-190`) y el validador
    sólo exige que el permiso exista (`ValidationRules.cs:70-75`);
  - toda cuenta nueva recibe «User» (`UserRepository.cs:221-223`), y un permiso vale desde el pedido siguiente
    (`RoleCrudEndpointsTests.cs:116-143`);
  - ninguna regla ni test lo acepta, y las descripciones de los permisos no avisan (`Permissions.resx:24,26`).
- **Arreglo:** una regla en cuentas y en roles, evaluada dentro del límite con los permisos efectivos de quien
  pide:
  - nadie da ni quita un permiso, o un rol con permisos, que no tiene, ni en el alta ni en la edición;
  - nadie actúa sobre una cuenta con permisos que él no tiene: cambiarle el correo o el número, desactivarla
    o borrarla;
  - sólo Admin edita los permisos de «User».

  Chico, con tests de integración con cuentas que tienen sólo cada permiso. **Lo decidís vos.**
- **Decidido el 2026-11-14:** la regla entera: nadie da lo que no tiene, nadie actúa sobre una cuenta con más
  permisos que los suyos y sólo Admin edita «User» ([decisiones](#decisiones-del-2026-11-14)).

#### N2. Usuarios y roles se guardan sin control de revisión: el último pisa al anterior

- **Dónde falla:** hoy, cada vez que dos personas tienen abierto el mismo usuario o el mismo rol. Raro en un
  negocio chico; severidad alta cuando lo pisado era quitar un permiso.
- **Escenario:** Ana y Bruno abren el rol «Ventas». Ana le saca «Administrar usuarios» y guarda; cinco minutos
  después Bruno sólo corrige la descripción y guarda. Su pantalla manda la lista de permisos como estaba al
  abrir, así que el permiso vuelve y ninguno de los dos se entera.
  - Lo mismo pasa con los roles de una cuenta y con el nombre entre «Mi perfil» y la edición del administrador.
  - Si guardan en el mismo instante, uno ve «Algo salió mal» (500) en vez de un aviso.
  - Si Ana borra un rol vacío mientras otro se lo asigna a alguien, la asignación desaparece en silencio por
    la cascada, o falla con 500.
- **Evidencia:**
  - ni los contratos ni las respuestas llevan revisión (`UpdateUserHttpRequest.cs:4-8`,
    `UpdateRoleHttpRequest.cs:4`, `RoleResponse.cs:4-10`);
  - los repositorios aplican la diferencia contra lo que hay (`UserRepository.cs:147-166`,
    `RoleRepository.cs:41,59-79`), y el `ConcurrencyStamp` sólo choca si las dos transacciones se superponen,
    como excepción (`IdentityResultExtensions.cs:11-17`);
  - los editores siembran el borrador una vez y mandan todo
    (`front:src/features/users/components/UserEditDialog.tsx:132-138,229-234`, `front:RoleEditorPage.tsx:178-184,317-321`);
  - el borrado cuenta sin lock (`RoleService.cs:204-220`) y la FK borra en cascada (`InitialIdentity.cs:233-237`);
  - el PUT de `api/settings` cambia el modo de registro sin revisión (`SystemSettingsService.cs:116-127`); nadie
    lo usa (el front usa PATCH), pero es un contrato vigente (`administracion.md:59-60`): esa regla contradice el
    ADR 0018 y, sin consumidores, el [ADR 0005](../decisions/0005-sin-versionado-de-api-por-ahora.md) no lo protege;
  - la regla pide revisión y 409 para la configuración interna ([ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md),
    `backend.md:119`), y la receta de un área nueva manda copiar `RoleService`, que no la tiene
    (`agregar-un-area.md:153`).
- **Arreglo:**
  - una revisión `long`, como `ExpectedRevision` de la configuración, que avancen sólo las ediciones de esos
    datos: el `ConcurrencyStamp` cambia con cualquier escritura de la cuenta (un código mal escrito, el reinicio
    de intentos o el perfil) y daría 409 falsos;
  - bloquear la fila al guardar (en el borrado de un rol, `FOR UPDATE`, para que choque con la asignación),
    comparar y responder 409 traducido; el perfil, con el mismo control;
  - el front conserva el borrador y ofrece releer, como `/configuracion`;
  - retirar el PUT de ajustes, con su regla y sus tests, y corregir la receta.

  Mediano.

#### N3. Cualquiera puede bloquear la cuenta de otro y saber si existe

- **Dónde falla:** hoy, ante un ataque dirigido. Severidad media-baja: sólo frena los ingresos nuevos; una sesión
  abierta sigue, pero la persona no puede entrar desde otro dispositivo ni después de cerrar sesión.
- **Escenario:** alguien conoce el correo del administrador y, cada 15 minutos, manda diez verificaciones con un
  código cualquiera, sin haber pedido ninguno. Cada intento suma un fallo a la cuenta; al décimo queda bloqueada
  15 minutos, y mientras tanto no entra ni con el código correcto, ni con Google, ni con el enlace. Una sola IP
  mantiene así tres cuentas bloqueadas sin fin. Además, la respuesta dice si la cuenta existe: `LockedOut` sólo
  sale si hay cuenta, y `AlreadyUsed` sale con cualquier código si esa persona ya usó el suyo.
- **Evidencia:** `LoginCodeVerifier.cs:44-47,51-62`; Google y el enlace rechazan la cuenta bloqueada
  (`ExternalLoginService.cs:142-145`, `LoginLinkVerifier.cs:73-76`); diez fallos y 15 minutos
  (`IdentityRegistration.cs:70-74`); `AlreadyUsed` antes de comparar (`LoginCode.cs:154-159`); test
  `Parallel_wrong_codes_lock_an_existing_account`. Contradice el comentario de `LoginCodeVerifier.cs:67` y la
  sección 5.3 del [diseño base](../specs/2026-09-18-arquitectura-base-design.md) («sin código activo, el mismo
  error que un código incorrecto»).
- **Arreglo:** sumar fallos sólo contra un código activo no alcanza solo, porque el atacante pide dos códigos y
  gasta sus intentos, aunque conviene igual: le saca el camino gratis. Lo efectivo, combinado:
  - que el bloqueo por códigos no frene a Google;
  - `AlreadyUsed` y `Expired` sólo si el código coincide;
  - `LockedOut` recién después de un código correcto, como ya hace la verificación con `Disabled` (el pedido de
    código, en cambio, lo revela antes: ver O11).

  Chico. **Lo decidís vos:** si se acepta que `LockedOut` revele la cuenta, y que el bloqueo por códigos deje de
  frenar a Google, que cambia la regla de `identidad.md:29`.
- **Decidido el 2026-11-14:** las tres medidas juntas ([decisiones](#decisiones-del-2026-11-14)).

#### N4. Restaurar una cuenta borrada le devuelve el acceso a su dueño anterior

- **Dónde falla:** hoy, cuando el número o el correo de una cuenta borrada pasa a otra persona. Poco frecuente,
  aunque es justo la baja típica: el celular de la empresa pasa a otra persona. Severidad alta cuando pasa.
- **Escenario:** Diego tiene correo, Google vinculado y el celular de la empresa, y lo borran. Meses después el
  celular pasa a Nora y el administrador la da de alta sólo con ese número. No se crea una cuenta nueva: se
  restaura la de Diego con el nombre de Nora, el correo de Diego verificado y su Google vinculado, y la pantalla
  dice «creado». Diego puede volver a entrar con Google o con un código a su correo y ver lo de Nora.
- **Evidencia:** sin correo en el alta se restaura por el número (`UserContactLinker.cs:134-136,166-173`);
  restaurar sólo cambia nombre, borrado, actividad y bloqueo (`ApplicationUser.cs:113-126`); borrar no suelta
  nada (`UserAccessService.cs:79-98`, `SoftDeleteInterceptor.cs:49-55`); el login de Google vale apenas se
  restaura (`UserReader.cs:79-85`); el front no distingue restaurar de crear
  (`front:src/features/users/components/UserFormDialog.tsx:124-125`). La regla acepta restaurar y «completar lo
  que falte, nunca reemplazarlo» (`administracion.md:14`), pero no contempla un número reasignado.
- **Arreglo:** dejar sin verificar no alcanza, porque entrar con el código lo verifica. Al restaurar, quitar lo
  que el alta no trajo (correo, número y logins externos) y que la pantalla diga que se restauró; o no restaurar
  con el número solo. Chico. **Lo decidís vos:** cambia una regla documentada.
- **Decidido el 2026-11-14:** al restaurar se quita lo que el alta no trajo, y la pantalla dice que se restauró
  ([decisiones](#decisiones-del-2026-11-14)).

#### N5. Con el correo caído, códigos e invitaciones se pierden en segundos y nadie se entera

- **Dónde falla:** hoy, ante cualquier corte o rechazo del servidor de correo: una contraseña de aplicación
  revocada (Google la revoca al cambiar la de la cuenta), el cupo diario agotado o la red. Severidad media-alta.
- **Escenario:** se revoca la contraseña de aplicación del Gmail que manda los correos. Esa mañana se invita a
  cinco personas y diez piden un código.
  - Cada correo falla tres veces en 7 a 10 segundos y se borra de la cola: los quince se pierden en unos dos
    minutos.
  - La ficha de cada usuario muestra la invitación como si hubiera salido.
  - Las diez personas ven «te mandamos un código» y no reciben nada.
  - `/health` sigue sana, no hay métrica y el log no dice de qué correo se trata. Arreglada la contraseña, hay
    que reiniciar la Api.
- **Evidencia:** tres intentos y descarte (`EmailBackgroundService.cs:44-56`; test
  `After_three_failed_attempts_the_email_is_dropped_and_the_next_one_is_sent`); la fila se completa y se borra
  (`DurableMessageSource.cs:64,71`); la invitación queda fallida sólo si no se encoló
  (`EmailInvitationChannel.cs:47-52`) y el código se marca enviado al encolar (`SignInCodeIssuer.cs:55-59`); el
  front toma la invitación sin estado como enviada (`front:src/features/users/api/users.ts:30-31`,
  `front:LastInvitationStrip.tsx:332-344`); los chequeos de salud son `self`, `database`, Redis y `whatsapp`;
  el log del descarte no lleva id (`EmailBackgroundService.cs:64-65`). WhatsApp ya avisa a `/health` y publica
  métricas (H9).
- **Arreglo:**
  - una falla antes de mandar el cuerpo no descarta: la fila vuelve a pendiente con espera creciente hasta que
    vence, con una columna `AvailableAtUtc` que la reserva respete para no trabar la fila;
  - cuando vence sin salir, la invitación pasa a fallida (el mensaje lleva su id, con la regla de N27);
  - un chequeo `email` en Degraded, no Unhealthy, que sacaría las copias del tráfico; un 535 se marca enseguida;
  - contadores de enviados y descartados, y el id en el log.

  Mediano.

#### N6. `/health` es pública y el pool de conexiones no está dimensionado para la base

- **Dónde falla:** hoy, ante un abuso deliberado; con varias copias o con picos legítimos, sin abuso. Severidad
  alta para la disponibilidad. Depende del comportamiento del proveedor y del plan de la base: lo confirma una
  prueba de carga.
- **Escenario:** alguien manda unos cientos de pedidos por segundo a `/health`, que es pública. Cada consulta
  abre una conexión nueva a PostgreSQL fuera del pool (el chequeo de EF usa `CanConnectAsync`, que en el
  proveedor de Npgsql verifica con una conexión sin pool) y consulta Redis. El servidor B1ms del despliegue
  acepta unas 50 conexiones, y cada copia de la Api puede abrir hasta 100. Los pedidos reales se quedan sin
  conexión, `/health` tarda más de 5 segundos y Container Apps saca la copia del tráfico: con una sola copia se
  cae todo. Las demás rutas anónimas (`/connect`, Google, métodos de ingreso y presentación) tampoco tienen
  límite.
- **Evidencia:** `/health` sin `RequireHost` (`Extensions.cs:93-106`) y el smoke del despliegue la consulta desde
  afuera (`.github/workflows/deploy.yml:376`); el chequeo de la base (`PersistenceRegistration.cs:36-38`); el
  B1ms (`azure-setup.md:71`) y la cadena sin `Maximum Pool Size` (`azure-setup.md:210`); sólo hay políticas por
  IP en el ingreso (`RateLimitingExtensions.cs:24-41`); el núcleo no fija tope de cuerpo (rige el de Kestrel,
  unos 28,6 MB).
- **Arreglo:**
  - el chequeo de la base con un `SELECT 1` del pool y el resultado de `/health` guardado uno o dos segundos;
  - `Maximum Pool Size` por réplica, sumados los workers, por debajo del límite del servidor, escrito en la guía;
  - un limitador global y uno de concurrencia, y un tope de cuerpo global;
  - si `/health` pasa a ser interna, el smoke mira el `healthState` que ya consulta.

  Chico.

#### N7. Los códigos por correo no tienen tope diario y pueden agotar el cupo del proveedor

- **Dónde falla:** hoy, con el registro abierto, que es el modo que trae la plantilla (`appsettings.json:9-10`,
  `despliegue.md:83`), y también con el registro sólo por invitación, si se conocen cuatro correos de cuentas.
  Severidad media-alta: corta los códigos y las invitaciones de todos hasta por 24 horas; frecuencia baja, porque
  hace falta un abuso deliberado. Cuánto aguanta depende del proveedor: Gmail da unos 500 correos por día a una
  cuenta gratuita.
- **Escenario:** alguien pide códigos para direcciones al azar. Una IP dispara unos 1.900 correos por día, y los
  alias de Gmail cuentan como destinos distintos. En unas seis horas se agota el cupo del Gmail y desde ahí no
  sale ningún correo, ni códigos ni invitaciones: con N5 se descartan sin aviso. Lo mismo, más lento, desde
  «Mi perfil» con cualquier cuenta o con las invitaciones de un administrador, que no tienen tope.
- **Evidencia:** `SignInCodeIssuer.cs:47-60` (manda a cualquier destino con el registro abierto);
  `LoginCodeIssuer.cs:92-119` (límites sólo por destino); `RateLimitingOptions.cs:13-18` (por IP y por copia,
  H26); `Email.cs:21` no normaliza los alias; `MeController.cs:36-43` y `UsersController.cs:68-82`. Es el H10
  del correo: aquel arreglo quedó en WhatsApp (`WhatsAppCodeQuotaGuard.cs:20-21`).
- **Arreglo:** contar todo código que sale, no sólo los que van a destinos sin cuenta (eso revelaría cuáles
  existen), con un tope diario por dirección agrupada por /64, como WhatsApp; un tope diario global por debajo
  del cupo del proveedor, con aviso al 80 %; un tope por cuenta para los códigos del perfil y uno diario de
  invitaciones por administrador. Chico. Para producción, un proveedor transaccional con su cupo medido.
- **Decidido el 2026-11-14:** van los topes, y el correo de producción sigue en Gmail, con su cupo escrito en la
  guía y el tope global por debajo ([decisiones](#decisiones-del-2026-11-14)).

#### N8. Un correo se pierde en un despliegue o sale dos veces si el servidor tarda

- **Dónde falla:** hoy, en cada despliegue o reinicio con un correo en curso, y cuando el servidor acepta pero no
  confirma a tiempo. Frecuencia baja; severidad media-baja. Es el H8 del correo.
- **Escenario:**
  - un administrador invita a Laura y, mientras el worker se conecta al servidor de correo o espera entre
    reintentos, la copia se apaga por un despliegue. El correo queda tomado y nadie lo retoma; se borra al
    vencer (un día para una invitación, diez minutos para un código) y la pantalla lo muestra como enviado;
  - el servidor recibe un código y tarda más de 30 segundos en confirmarlo: el worker lo da por fallido, lo
    vuelve a mandar y llegan dos.
- **Evidencia:** el token de apagado entra en el envío y en la espera (`EmailBackgroundService.cs:40,56`); nadie
  devuelve el mensaje a la cola (sólo WhatsApp llama a `GiveBackCurrent`, `WhatsAppSenderBackgroundService.cs:49`),
  así que `DurableMessageSource.cs:66-69` no lo suelta; toda excepción que no es una cancelación se reintenta
  (`EmailBackgroundService.cs:44-57`), también el plazo vencido de un envío ya hecho. Ningún test prueba el
  apagado sobre la cola durable.
- **Arreglo:** el de H8: el envío con un plazo propio menor que el apagado del host y sin el token de apagado;
  devolver lo que esperaba su reintento o se cortó antes de mandar el cuerpo; reintentar sólo las fallas de
  conexión, de autenticación y las 4xx, y tomar como incierto, sin repetirlo, un corte durante el envío; con su
  test sobre la cola durable. Chico.

#### N9. Con Redis caído no responde nada, ni la pantalla de ingreso ni los webhooks

- **Dónde falla:** hoy, en cada corte o mantenimiento de Redis de más de unos 30 segundos; con varias copias,
  salen todas juntas. Severidad media: sin Redis ya fallaban casi todas las llamadas a `/api`, pero así se caen
  también la app, el ingreso y los webhooks, y el mensaje que se ve engaña. Es el costo del
  [ADR 0009](../decisions/0009-redis-como-unico-cache-de-datos.md), escrito a medias.
- **Escenario:** Redis se reinicia un minuto. Toda ruta con permiso responde «Algo salió mal», y también
  `/api/me` y el ingreso, porque permisos y modo de registro se leen de Redis. A los 30 segundos `/health` falla
  y Container Apps saca la copia del tráfico: tampoco cargan la app, la pantalla de ingreso ni los webhooks de
  Meta, que no necesitan Redis. Quien tenía la app abierta ve «…informá el código ?», porque la respuesta del
  ingress no es un ProblemDetails. Si la copia se reinicia durante el corte, no arranca.
- **Evidencia:** `CachingRegistration.cs:22-29` (`FailFast`, ya en H3), `PermissionAuthorizationHandler.cs:21`,
  `HealthCheckTests.cs:39-50` y `DatabaseSeeder.cs:55-60`; `azure-setup.md:197-199` lo acepta («la app deja de
  atender») sin nombrar la app, el ingreso ni los webhooks; `front:src/shared/api/httpClient.ts:40-48` y
  `errorFeedback.ts`.
- **Un dato para decidir:** la caché de permisos no ahorra consultas: cada pregunta lee igual en la base los
  roles de la persona y la versión de cada rol antes de ir a Redis (`PermissionService.cs:17-30,46,71-75`). Una
  sola consulta indexada daría lo mismo sin depender de Redis (N33).
- **Arreglo:** **lo decidís vos.** Degradar de verdad (`/health` en Degraded, como WhatsApp; permisos desde la
  base; la configuración, desde la base si Redis no responde; un texto propio para un 5xx sin código) es mediano y
  enmienda el ADR 0009. Lo mínimo es el chequeo en Degraded y el texto del front, que hace falta igual: cubre
  también los 502 y 504 del ingress.
- **Decidido el 2026-11-14:** degradar de verdad, con la enmienda del ADR 0009
  ([decisiones](#decisiones-del-2026-11-14)).

#### N10. El código de error que ve la persona no se puede rastrear en producción

- **Dónde falla:** hoy, en cada 500. Severidad media: no se pierden datos, pero soporte no ubica el error.
- **Escenario:** un cliente manda una captura con «informá el código 00-…-01». En Azure nadie lo encuentra: no
  hay exportador de telemetría configurado, la consola no escribe el código y el log del 500 no dice ruta ni
  usuario; sólo se busca por la hora.
- **Evidencia:** el código es `Activity.Current?.Id` (`ProblemDetailsMapper.cs:69`); el exportador se prende sólo
  con `OTEL_EXPORTER_OTLP_ENDPOINT` (`Extensions.cs:70-80`), que ninguna guía define; `IncludeScopes` vale sólo
  para ese exportador (`Extensions.cs:43`); `GlobalExceptionHandler.cs:37,52-53`; `OperationLog.cs:37-44` no
  lleva quién. Figura a medias en `verificacion-y-produccion.md:161`. La traza de punta a punta es H19.
- **Arreglo:** en Production, la consola en JSON con scopes; el id del usuario en un scope; el exportador
  documentado en la guía de despliegue y una alerta de 5xx. Trivial a chico.

#### N11. Agregar o cambiar el correo desde el perfil no pide confirmar con el medio que ya existe

- **Dónde falla:** hoy; severidad media.
- **Escenario:** alguien usa la sesión abierta de Marta en una PC sin bloquear, pone su propio correo en «Mi
  perfil», lo confirma con el código que le llega y después desvincula el WhatsApp de Marta, que queda afuera
  sin ningún aviso.
- **Evidencia:** `ProfileService.cs:120-162` reemplaza el correo sin avisar ni pedir nada al medio que ya existía
  (el front dice que se agrega una sola vez, pero la API acepta reemplazarlo); `ProfilePhoneService.cs:25,43-55`
  exige otro medio y conserva las sesiones a propósito.
- **Arreglo:** pedir un código al medio que ya existe antes de agregar o reemplazar un correo, o de desvincular el
  número, y rechazar en el backend el reemplazo del correo, como ya hace el front. Chico.

#### N12. Cerrar sesión deja abiertas las otras pestañas

- **Dónde falla:** hoy, en una PC compartida; severidad media.
- **Escenario:** alguien tiene la app en dos pestañas y toca «Cerrar sesión» en una. La otra sigue usando la app
  y se renueva sin la cookie; con el refresh rotativo y deslizante, mientras se use no vence nunca. Quien se
  sienta después sigue adentro.
- **Evidencia:** el cierre revoca sólo la autorización de su `id_token_hint` (`ConnectController.cs:89-94`,
  `OpenIddictTokenRevoker.cs:8-9`); cada pestaña tiene su autorización (`front:src/auth/SessionRecovery.tsx:46-49`)
  y renueva con su refresh token (`front:src/auth/AuthProvider.tsx:18`); el test
  `Logout_revokes_the_tokens_and_closes_the_session` prueba una sola. Cerrar sesiones a distancia es una propuesta
  sin decidir (`verificacion-y-produccion.md:107-113`).
- **Arreglo:** avisar el cierre a las demás pestañas con `BroadcastChannel`. Chico.

#### N13. No queda historia de quién cambió roles, permisos o cuentas

- **Dónde falla:** hoy; severidad media, que sube con N1.
- **Escenario:** un lunes Carlos aparece con rol Admin. Nadie puede saber quién se lo dio: la fila guarda sólo la
  última modificación, que se pisa con la siguiente; un rol borrado no deja rastro, y los logs dicen «Handled
  UpdateRole» sin el rol ni quién.
- **Evidencia:** `AuditableEntityInterceptor.cs:41-53` pisa los campos de modificación; el borrado de un rol es
  físico y se lleva sus permisos (`RoleRepository.cs:51-57`); `OperationLog.cs:37-44`. Sólo las invitaciones y el
  borrado de cuentas guardan quién. Precedente propio: `LoginAudit`.
- **Arreglo:** una tabla de cambios administrativos (quién, sobre qué, permisos o roles agregados y quitados,
  estado antes y después), escrita en el mismo límite, para roles (el borrado de un rol, con la lista de permisos
  que tenía), asignación de roles, estado, borrado y cambio de correo o de número de una cuenta, y modo de
  registro; el seed queda con el actor vacío. Sin el cambio de correo, la toma de cuentas de N1 no deja rastro.
  Chico a mediano.

#### N14. El modo `.eml` se puede prender en producción

- **Dónde falla:** hoy, si queda configurado de una prueba. Raro; severidad alta.
- **Escenario:** en producción queda `Email__Delivery=PickupDirectory`. La Api arranca, cada correo se escribe en
  el disco de la copia, que se pierde con el próximo despliegue, y se da por enviado. Nadie recibe nada, y los
  archivos guardan los códigos legibles.
- **Evidencia:** `EmailRegistration.cs:56-60`; `SmtpOptionsValidator.cs:11-14` saltea la validación;
  `EmailRegistrationTests.cs:33-39,47` prueba que arranca así en Production.
- **Arreglo:** que no arranque con ese modo fuera de Development y Testing, como ya pasa con el origen público
  (`EmailRegistration.cs:34-38`). Trivial.

#### N15. Los archivos privados no tienen respaldo ni borrado temporal

- **Dónde falla:** hoy; severidad alta: lo borrado no se recupera, y los PDF de ARCA tienen valor legal.
- **Escenario:** la fuente de retención de un módulo, con un bug, devuelve menos claves de las que debe, y el
  barrido borra PDF de comprobantes, documentos recibidos o archivos de la IA. El borrado es definitivo, y
  restaurar la base no trae los objetos. Las fotos confirmadas, en cambio, las protege su fila en `StoredFiles`.
- **Evidencia:** el borrado no tiene condición (`AzureBlobFileStore.cs:77-78`); la guía de Azure no crea el
  almacenamiento (`azure-setup.md:5-8`) ni pide borrado temporal o versionado; el ensayo de restauración no
  incluye el contenedor (`verificacion-y-produccion.md:157`).
- **Arreglo:** crear el contenedor en la guía con borrado temporal (por ejemplo, 14 días), versionado y feed de
  cambios, y restaurar base y contenedor juntos en el ensayo de H15. Chico.

#### N16. Los datos del ingreso no se purgan nunca

- **Dónde falla:** hoy, como privacidad; a gran escala, también como volumen. Ya figura como pendiente
  (`verificacion-y-produccion.md:177-179`).
- **Escenario:** cada pedido de código guarda el correo o el número de quien lo pide, tenga cuenta o no; cada
  intento guarda la IP y el navegador, también los de un atacante; cada recarga de la página crea una
  autorización de OpenIddict con cuatro tokens, y cada renovación suma tres filas. Nada se borra: con los años la
  base guarda todas las IP de todos los intentos y decenas de millones de tokens que pesan en el respaldo.
- **Evidencia:** no hay purga, poda ni worker de retención en `src`; `LoginCodeRepository.cs:93`,
  `LoginAuditRecorder.cs:19-30`, `OpenIddictRegistration.cs:25-29` y `front:src/auth/SessionRecovery.tsx:46-49`.
  Tampoco se purgan `LoginLinks` ni `UserInvitations`. Borrar a una persona que lo pide es H14.
- **Arreglo:** un worker de retención con lease que conserve al menos las 24 horas que leen los límites y el
  último ingreso de cada cuenta, borre por lotes (el núcleo no usa `ExecuteDelete`) los códigos y enlaces viejos,
  borre o anonimice los `LoginAudits` y corra la poda de OpenIddict. Mediano. **Lo decidís vos:** los plazos.
- **Decidido el 2026-11-14:** códigos y enlaces, 30 días; `LoginAudits`, 12 meses y después sin la IP; la poda de
  OpenIddict, a los 14 días de vencidos ([decisiones](#decisiones-del-2026-11-14)).

#### N17. El arranque deja pasar configuración de producción incompleta

- **Dónde falla:** en el primer despliegue real y en cada cambio de configuración. Severidad baja a media; lo que
  depende de Container Apps está sin probar, porque todavía no hubo despliegue.
- **Escenario:**
  - sin `ForwardedHeaders`, la Api arranca, pero OpenIddict rechaza los pedidos que le llegan por HTTP y se cae
    el ingreso entero; además, el límite por IP junta a todos en una sola partición;
  - si se restringe `AllowedHosts` al dominio, como pide la guía, los chequeos de Container Apps, que mandan como
    host la IP del pod, recibirían 400 y la versión nueva nunca quedaría lista;
  - un `KnownProxies` mal escrito falla recién al armar el pipeline, después de comprobar las migraciones y sembrar;
  - el plazo de apagado de la Api y el de la Container App valen 30 segundos los dos, sin margen.
- **Evidencia:** `despliegue.md:114,117,159`, `azure-setup.md:172-185,218`, `ForwardedHeadersExtensions.cs:19,49-59`
  y `Program.cs:90,113`; ni `ShutdownTimeout` ni `terminationGracePeriodSeconds` figuran en el repositorio.
- **Arreglo:** validar `ForwardedHeaders` al arrancar fuera de Development; los chequeos con el encabezado `Host`,
  o mantener `*` y escribir que el ingress ya filtra por host; interpretar `KnownProxies` con `ValidateOnStart`;
  y fijar unos 25 segundos de apagado contra 40 a 60 de la plataforma. Trivial a chico.

#### N18. La resiliencia por defecto reintentaría un POST en un cliente nuevo

- **Dónde falla:** latente, porque hoy ningún cliente la conserva. Severidad alta cuando pase: duplica efectos
  externos, justo lo que el ADR 0018 manda proteger.
- **Escenario:** alguien agrega un cliente HTTP para un proveedor nuevo y no le quita la resiliencia de
  `ServiceDefaults`. Ante un 503 o un plazo vencido de 10 segundos, el POST se repite hasta tres veces: dos
  cobros, dos mensajes.
- **Evidencia:** `Extensions.cs:29-33` sin `DisableForUnsafeHttpMethods`; los 14 clientes de los módulos la quitan
  uno por uno (por ejemplo, `WhatsAppInfrastructureRegistration.cs:106` y `ArcaInfrastructureRegistration.cs:105`);
  los tests son por cliente (`WhatsAppRegistrationTests.cs:222-246`). El núcleo no registra clientes.
- **Arreglo:** sacar el handler de los valores por defecto, porque ningún cliente lo usa tal cual; o
  `DisableForUnsafeHttpMethods` y un test que recorra los clientes con nombre. Trivial.

#### N19. El prefijo de Redis de una instalación es opcional

- **Dónde falla:** hoy, sólo con un Redis compartido entre instalaciones y configurado contra la guía. Raro;
  severidad media.
- **Escenario:** dos negocios comparten el Redis con el mismo nombre de aplicación, ambiente y base. Durante hasta
  60 segundos, uno lee el modo de registro y la presentación del otro: un negocio por invitación acepta altas.
  Los permisos no chocan, porque su clave lleva el id del rol.
- **Evidencia:** `CachingRegistration.cs:41-52` y `SystemSettingsCache.cs:11-12`; la guía lo advierte
  (`despliegue.md:121`).
- **Arreglo:** exigir `Caching:KeyPrefix` fuera de Development y Testing, con `ValidateOnStart`. Trivial.

#### N20. El límite por IP no agrupa IPv6

- **Dónde falla:** hoy, si el balanceador entrega IPv6. Severidad baja; agrava N3 y N7.
- **Evidencia:** `RateLimitingExtensions.cs:50-58` usa la dirección completa; WhatsApp ya agrupa por /64
  (`WhatsAppCodeAddressLimiter.cs:37-45`). Agrupar por /64 no alcanza contra un /48, y los límites siguen siendo
  de cada copia (H26).
- **Arreglo:** la misma normalización que WhatsApp. Trivial.

#### N21. Números sin la región de quien lee

- **Dónde falla:** hoy, desde 1.000 cuentas; es cosmético.
- **Escenario:** con 1.234 cuentas, el asistente y la pantalla de roles muestran «1234» en vez de «1.234» (es-AR)
  o «1,234» (en-US).
- **Evidencia:** `RolesSystemQuerySource.cs:69` y `UsersSystemQuerySource.cs:31-32` usan `InvariantCulture`, y
  `front:src/features/roles/pages/RolesPage.tsx:62` usa `String(row.userCount)`; `NumberFormattingTests` deja
  afuera `InvariantCulture` a propósito.
- **Arreglo:** `DisplayNumbers` con la cultura del pedido y `formatNumber` en el front. Esos valores son también la
  evidencia que lee el modelo: conviene confirmarlo en la prueba del asistente. Trivial.

#### N22. Un texto del núcleo nombra a WhatsApp, y dos versiones apagadas dicen «no se encontró»

- **Dónde falla:** hoy; severidad baja.
- **Escenario:** el almacenamiento no está configurado y alguien carga un formulario con fotos desde la web: lee
  «No pudimos bajar los archivos de WhatsApp». En un proyecto que acaba de quitar Formularios, un pedido del
  asistente que pedía un formulario termina en una derivación genérica: la versión apagada contesta «no se
  encontró» donde su contrato promete «no disponible», aunque ese texto no le llega a nadie.
- **Evidencia:** `FileErrors.cs:10` y `Errors.resx:337,341` (también en inglés), que usan la web y el audio de la
  IA (`AiAssistantHomeAudioPolicy.cs`, `FormPdfActionService.cs:37`); el front mapea ese código
  (`FormReplyCard.tsx:36-49`). `UnavailableStructuredMissingFormRequestSink.cs:9` y
  `UnavailableStructuredCatalogProposalSink.cs:13` devuelven `SystemQueryErrors.NotFound`, mientras
  `Interfaces/Channels/AGENTS.md:48,56` promete «no disponible»; casi no se alcanza, porque sin Formularios esa
  capacidad no existe, y nadie decide por ese código.
- **Arreglo:** códigos neutrales en el núcleo para «no hay almacenamiento» y «falta el objeto» (el de WhatsApp, en
  su módulo), con su lugar en el front; para las versiones apagadas alcanza con corregir el `AGENTS.md`. Chico.

#### N23. Los permisos de un módulo quitado vuelven en algunos roles

- **Dónde falla:** raro, porque quitar y volver a agregar un módulo es una decisión de desarrollo. Severidad baja
  a media.
- **Escenario:** se quita ARCA y los roles conservan sus permisos sin efecto. Meses después se vuelve a agregar: en
  los roles que nadie guardó en el medio, quien tenía permiso de facturar lo recupera solo; en los que se
  guardaron, el editor ya los había descartado.
- **Evidencia:** que un permiso valga depende sólo del catálogo (`PermissionService.cs:26`); el seed nunca quita
  (`RoleSeeder.cs:38-46`); las guías los dejan sin efecto (`quitar-arca.md:80-82`, `quitar-whatsapp.md:11`); el
  editor descarta al guardar los códigos que no conoce (`front:RoleEditorPage.tsx:314-318`). El
  [ADR 0010](../decisions/0010-catalogo-de-permisos-extensible.md) acepta que queden sin efecto, no que vuelvan.
- **Arreglo:** al quitar un módulo, una migración que borre sus permisos y avance la versión de cada rol; o, más
  proporcional, un paso en la guía para volver a agregarlo: «revisá los roles». Chico.

#### N24. La invitación sale en el idioma general del sistema

- **Dónde falla:** hoy; severidad baja. Es el costo de una decisión documentada: las altas administrativas copian
  la cultura y la zona generales (`administracion.md:74-78`).
- **Escenario:** la instalación está en español y el administrador invita a un proveedor que no lo lee: la
  invitación le llega en español, y el alta no deja elegir otro idioma.
- **Evidencia:** `UserContactLinker.cs:140-142` y `UserInvitationIssuer.cs:85`; test
  `Resending_by_email_goes_in_the_language_of_the_account_and_not_in_the_one_of_the_administrator`.
- **Arreglo, si se quiere:** un campo opcional de idioma en el alta y la edición. Chico.
- **Decidido el 2026-11-14:** se mantiene, como costo aceptado ([decisiones](#decisiones-del-2026-11-14)).

#### N25. El seed le devuelve Admin al administrador inicial en cada arranque

- **Dónde falla:** hoy; severidad baja. Es el costo de una decisión documentada (`despliegue.md:82`).
- **Escenario:** el consultor que instaló el sistema termina su trabajo, otro administrador le saca Admin y la
  pantalla lo acepta. En el despliegue siguiente lo vuelve a tener, sin aviso. Si en cambio le cambian el correo,
  el seed pierde esa cuenta; si la desactivan, queda como un Admin inactivo.
- **Evidencia:** `RoleSeeder.cs:50-65` y `UserGuard.cs:44-65`.
- **Arreglo, si se quiere:** que la guarda rechace quitarle Admin a esa cuenta, o cambiarle el correo, con un
  mensaje que diga que hay que cambiar `Seed:AdminEmail`. Trivial.
- **Decidido el 2026-11-14:** se cierra con la guarda ([decisiones](#decisiones-del-2026-11-14)).

#### N26. Un modelo con respuestas de personas no redacta su `ToString`

- **Dónde falla:** latente: hoy ningún log lo imprime. Severidad baja.
- **Evidencia:** `StructuredRecordReadValue` (`StructuredRecordReadModels.cs:14`, con un `object? Value`) usa el
  `ToString` del compilador, mientras sus vecinos lo redactan; `ControllerInputContractTests` sólo controla los
  contratos HTTP de entrada.
- **Arreglo:** redactarlo, y un control sobre los records de `Models/StructuredData` y `Models/Assistant` que mire
  las propiedades de valor (hoy 41 de 152 no redactan, casi todos con sólo ids) o lleve una lista de excepciones.
  Trivial.

### Con varias copias

#### N27. En un despliegue, la cola durable borra lo que no entiende y manda incompleto lo que entiende a medias

- **Dónde falla:** con una sola copia, en los segundos en que conviven sus workers en cada despliegue y al volver
  a la imagen anterior; con varias copias, durante todo un despliegue gradual. Severidad media. Es H25 en la clase
  común del núcleo, más tres casos que H25 no nombra.
- **Escenario:** mientras conviven las dos versiones:
  - una versión agrega un adjunto al correo, como el PDF de la copia de un formulario; la copia vieja lo toma,
    ignora el campo nuevo, manda el correo sin el PDF y lo da por completado;
  - si cambia el nombre de un tipo de mensaje, la copia vieja no lo puede leer y lo borra;
  - una copia cuyo anillo de Data Protection no descifra borra todo lo que toma;
  - el núcleo suma un valor a un enum de un modelo neutro que guardan los módulos (por ejemplo, un motivo nuevo de
    `StructuredDocumentProblem`): la copia vieja de WhatsApp no lo reconoce, deshace el turno y lo reintenta en cada
    vuelta, y mientras tanto no atiende lo demás de ese contacto; si se vuelve a la imagen anterior, queda trabado.
    Peor: si la lectura la completó la IA de la copia nueva, la copia vieja la recibe como contenido no disponible y
    guarda el documento como «No disponible», aunque se haya leído bien.
- **Evidencia:** la lectura del correo ignora lo desconocido (`EmailMessageSource.cs:14-16`); lo que no se descifra
  o no se lee se completa y se borra (`DurableMessageSource.cs:44-55`), la base común de `EmailMessageSource` y de
  `WhatsAppMessageSource`; WhatsApp guarda el enum por nombre y lanza ante uno desconocido
  (`WhatsAppReceivedDocumentReadingPolicy.cs:23-26`, `WhatsAppAssistantSourcePolicy.cs:84-88`,
  `WhatsAppInboundProcessor.cs:136-165`); la IA devuelve `ContentUnavailable` (`AiAssistantDocumentPolicy.cs:74-88`) y
  WhatsApp guarda «No disponible» (`WhatsAppReceivedDocumentDispatchService.cs:171-181`); Formularios, en cambio, lee
  tolerante. Hay reglas para que la versión nueva lea lo viejo (`Models/StructuredData/AGENTS.md:9-11,70`), ninguna
  para que la vieja tolere lo nuevo.
- **Ya decidido para WhatsApp:** la tarea 22 del [plan de la bandeja](../plans/2026-10-01-bandeja-whatsapp.md),
  en su etapa 7 y todavía sin programar, dice «una entrega de acceso que no se decodifica no se borra» (H25). Es el
  mismo camino de código: hecho en `DurableMessageSource`, cubre también el correo.
- **Arreglo, una sola regla para el núcleo y para H25:**
  - lo que una copia no puede leer no se borra: vuelve a la cola con una espera (`AvailableAtUtc`) y un tope de
    intentos;
  - la reserva toma sólo los tipos de mensaje que su versión conoce;
  - un campo, tipo o valor nuevo se escribe un despliegue después del que ya sabe leerlo, como ya pide la tarea 22;
    para los modelos neutros la regla va en `Models/StructuredData/AGENTS.md`, con un test que lea un nombre
    desconocido (pasar lo desconocido a `Other` sirve sólo para el motivo, no para los campos que restringen).

  Chico a mediano. **Lo decidís vos:** que sea una sola regla.
- **Decidido el 2026-11-14:** una sola regla, que extiende H25 a `DurableMessageSource` y a los modelos que guardan
  los módulos, y entra con los arreglos de «Hoy» ([decisiones](#decisiones-del-2026-11-14)).
- **Casi entero arreglado el 2026-11-14** en 42d8f47f, con H25 de los arreglos de WhatsApp: cada fuente de la cola durable
  reserva sólo los tipos que conoce; lo que no descifra o no lee vuelve a la cola con una espera de 30 s, hasta diez veces,
  en lugar de borrarse; el correo y la cola de acceso leen estricto; un motivo de lectura desconocido se lee como `Other`; y
  las guías piden escribir lo nuevo un despliegue después. Faltan los demás enums de los modelos neutros que los módulos
  guardan por nombre ([tarea 31](../plans/2026-11-14-arreglos-del-nucleo.md#etapa-6-lo-trivial-y-la-documentación)).

#### N28. La limpieza de archivos corre en cada copia, recorre todo el contenedor y puede borrar una foto recién subida

- **Dónde falla:** con varias copias, el trabajo se repite sin daño, porque borrar es idempotente; a gran escala,
  cada copia lista millones de objetos por hora; en cualquier horizonte, una carrera rarísima, de severidad media:
  se pierde para siempre el archivo de una respuesta confirmada (N15).
- **Escenario:**
  - con tres copias y dos millones de fotos, cada copia lista cada hora el contenedor entero y hace unas 110.000
    consultas a la base, un número que nunca baja;
  - un celular reintenta al día siguiente una carga que había fallado y vuelve a subir la misma foto con la misma
    clave justo después de que el barrido comprobó que no tenía referencia: el barrido la borra y la respuesta
    queda apuntando a un archivo que no existe.
- **Evidencia:** un servicio por copia, sin lease (`FileStorageRegistration.cs:15`,
  `PrivateFileCleanupBackgroundService.cs:11-18`); lista sin prefijo, filtra la fecha en memoria
  (`AzureBlobFileStore.cs:80-85`) y borra sin condición (`:77-78`); la clave del consumidor es determinística
  (`BusinessFormFileRecorder.cs:44-47`). El primer barrido llega una hora después de cada arranque.
- **Arreglo:** una lease para que barra una sola copia (chico); borrar con el ETag del listado (trivial, aunque no
  alcanza para quien reaprovecha un archivo con `WriteOnceAsync` sin reescribirlo); y a gran escala, listar sólo lo
  que espera confirmación (mediano: el ADR 0018 pide justificar ese staging, y a esa escala se justifica).
- **Decidido el 2026-11-14:** el ETag entra con «Hoy»; la lease, antes de pasar de una réplica; listar sólo lo
  pendiente, a gran escala ([decisiones](#decisiones-del-2026-11-14)).

### A gran escala

#### N29. Todos los correos pasan por un mismo lock y salen de a uno

- **Dónde falla:** hoy, frenos de menos de un segundo; con varias copias, severidad media; a gran escala, una falla
  sostenida en toda la Api. Antes muerde el cupo del proveedor (N7). La prueba real: carga sobre el pedido de
  código midiendo la espera del lock.
- **Escenario:** en un pico de pedidos de código por correo, cada pedido toma `delivery:email`, un lock único para
  toda la instalación, para contar la cola, y lo retiene hasta su commit; cada reserva de cualquier canal toma
  además un lock global para borrar vencidos. La espera es sincrónica: cada pedido que espera retiene un hilo y una
  conexión con su transacción abierta, y con la Container App de 0,25 vCPU se agotan primero los hilos y se frena
  toda la Api. Además, cada copia manda un correo por vez y abre una conexión nueva por correo.
- **Evidencia:** `DurableMessageStore.cs:12-22` y `AdvisoryLockExtensions.cs:7-17` (sincrónicos), `IEmailQueue.cs:14`,
  `DeliveryQueueService.cs:14-17`, `EmailBackgroundService.cs:27-30` y `SmtpEmailSender.cs:22-28`. El
  [ADR 0015](../decisions/0015-colas-transaccionales-cifradas.md) acepta la capacidad compartida por canal, no la fila.
- **Arreglo:** del lado del productor, contar la capacidad sin el lock, porque es una protección y no una invariante.
  El consumidor conserva el lock mientras la exclusión dependa de él (`QueuedDelivery` no tiene token de
  concurrencia); reservar por lotes con `FOR UPDATE SKIP LOCKED` da la exclusión por fila y lo vuelve innecesario.
  Además, sacar la limpieza de vencidos a un trabajo propio, reutilizar la conexión SMTP y mandar varios por copia.
  Mediano.

#### N30. El listado de usuarios recorre la tabla entera en cada búsqueda

- **Escenario:** con un millón de cuentas, cada búsqueda en Usuarios dispara dos pedidos: el listado hace un COUNT
  y un orden por fecha de alta sobre toda la tabla, el `ILIKE '%gomez%'` no usa índice, y los conteos de los
  filtros hacen cuatro consultas más una por rol. Son unos 5 + R recorridos completos por búsqueda.
- **Evidencia:** `AspNetUsers` tiene sólo tres índices (snapshot `:4916-4925`), ninguno por `CreatedAtUtc`, aunque el
  comentario de `UserReader.cs:263-264` diga que sí; `UserReader.cs:40-47,230-274,286-297`;
  `QueryableExtensions.cs:56-61`; el front pide los conteos en cada búsqueda y no los muestra
  (`front:src/features/users/components/UsersFilterBar.tsx:50-61`). La regla acepta el costo a la escala actual
  (`administracion.md:43`).
- **Arreglo:** dejar de pedir los conteos que no se muestran (trivial); un índice parcial
  `(CreatedAtUtc, Id) WHERE NOT "IsDeleted"` y trigramas sobre correo y nombre; el total estimado. Chico a mediano;
  antes, medir con un millón de filas.
- **Decidido el 2026-11-14:** dejar de pedir los conteos que no se muestran entra con «Hoy»; el resto, cuando haya
  volumen real ([decisiones](#decisiones-del-2026-11-14)).

#### N31. El tope diario de WhatsApp recorre toda la tabla de códigos

- **Escenario:** con millones de códigos acumulados (N16), cada pedido de código por WhatsApp cuenta los envíos por
  teléfono de las últimas 24 horas recorriendo `LoginCodes` entera.
- **Evidencia:** `LoginCodeRepository.cs:66-91` (el comentario lo reconoce), `LoginCodeConfiguration.cs:29` y
  `WhatsAppCodeQuotaGuard.cs:46,89`. La revisión de WhatsApp no lo registró.
- **Arreglo:** índices parciales `(Channel, SentAtUtc) WHERE SentAtUtc IS NOT NULL` y por `RequestedByUserId`, con la
  purga de N16. Trivial.
- **Decidido el 2026-11-14:** entra con los arreglos de «Hoy» ([decisiones](#decisiones-del-2026-11-14)).

#### N32. Cada lectura de roles vuelve a contar a todas las personas

- **Escenario:** con un millón de cuentas, todas con «User», abrir «Editar usuario» pide el catálogo de roles, que
  cuenta las personas de cada rol con una subconsulta; corregir la descripción de «User» cuenta ese millón dentro de
  la transacción. Sólo lo disparan pantallas de administración: severidad media-baja.
- **Evidencia:** la proyección común (`RoleReader.cs:102-112`), que usan el catálogo, el detalle, la página y el
  asistente (`RoleService.cs:31,170,204`, `front:RolesField.tsx:18`, `RolesSystemQuerySource.cs:47`).
- **Arreglo:** una proyección sin conteo para el catálogo, el detalle y la edición, y en el borrado preguntar si existe
  alguna persona. Chico; cambia la forma del catálogo, a revisar con el front. La página de roles y `roles.list`
  del asistente siguen contando: a gran escala queda elegir entre un contador mantenido o aceptar ese costo.

#### N33. Cada pregunta de permisos consulta la base igual y, dentro de una transacción, pide otra conexión

- **Escenario:** cada `HasPermissionAsync` hace 1 + R consultas (R son los roles de la persona) y R viajes a Redis, y
  hay políticas que preguntan tres veces seguidas. Dentro de un límite transaccional, leer la versión de cada rol abre
  otra conexión mientras la transacción retiene la suya: con el pool saturado, los pedidos se esperan entre sí hasta
  vencer. Si una copia muere con la lease de la clave de «User» tomada, la autorización de todos espera hasta 15
  segundos, una vez.
- **Evidencia:** `PermissionService.cs:17-30,46,71-75`, `WhatsAppAssistantPresentationPolicy.cs:69-72`,
  `WhatsAppChatModeService.cs:27-30` y `RedisCacheOptions.cs:15-18`; `backend.md:125` permite preguntar permisos
  dentro de un límite.
- **Arreglo:** leer los permisos con una consulta en el contexto del pedido, que resuelve las tres cosas y vuelve
  innecesaria la invalidación (va con N9 y enmienda el ADR 0009 y lo que lo cita); como mínimo, memorizarlos por
  scope, que sólo evita las preguntas repetidas: no saca la segunda conexión en los workers ni en los scopes del
  asistente. Chico.
- **Decidido el 2026-11-14:** se resuelve con N9, que lee los permisos con una sola consulta en el contexto del
  pedido, y entra con los arreglos de «Hoy» ([decisiones](#decisiones-del-2026-11-14)).

#### N34. Las migraciones no tienen un control que impida bloquear tablas grandes

- **Escenario:** `AddRecordAndLoginAuditIndexes` borra y vuelve a crear el índice de `LoginAudits` en la misma
  transacción: mientras se construye, nadie puede ingresar ni leer su último ingreso. Hoy no hace daño, porque no hay
  producción; el riesgo es el patrón, que repite H34 y además frena las lecturas. Ningún test lo impide.
- **Evidencia:** `20261107004318_AddRecordAndLoginAuditIndexes.cs:13-31`; `MigrationSqlPortabilityTests` sólo busca
  finales de línea; hay regla escrita para renombres y borrados (`despliegue.md:37`, `migracion.md:47`), no para
  índices que bloquean. El `Down` de `20261113213511` borra columnas con datos, pero volver atrás es volver a la
  imagen vieja, que no corre `Down`; `20260923105719_LoginCodeDestination.cs` renombra columnas y no figura en la
  lista de `despliegue.md:39`, aunque es anterior al pipeline.
- **Arreglo:** un test sobre las migraciones posteriores a una fecha de corte: sin renombres ni borrados en `Up`,
  índices concurrentes sobre tablas que ya existen y reemplazos en dos despliegues (crear antes de borrar), con una
  lista de excepciones; la guía avisa que un `CONCURRENTLY` fallido deja un índice inválido. Chico. La decisión del
  2026-11-13 sobre H34, «con volumen real», alcanza también a estas.

### Observaciones menores

- **O1.** Los 429 de reglas de negocio llevan `retryAfter` en el cuerpo pero no el encabezado `Retry-After`
  (`ControllerResultExtensions.cs:76-81`); el front lee el cuerpo, así que funciona.
- **O2.** El seed reescribe los redirect URIs del cliente `web` con la configuración de la versión que arranca
  (`OpenIddictSeeder.cs:66-78`): dos versiones con URIs distintas se pisan al reiniciarse.
- **O3.** Con la cola durable llena, un código que no se puede encolar no deja ningún log (`EmailQueue.cs:43-46`).
- **O4.** Activar y desactivar la misma cuenta a la vez puede dar 500 (`UserAccessService.cs:48-52`), de la familia
  de N2; editar una cuenta con el correo de una cuenta borrada da un 409 intencional que no dice que esa cuenta está
  borrada.
- **O5.** La reserva de la cola trae todos los vencidos sin tope y los borra con el lock del canal tomado, aunque
  `backend.md:139` diga «antes del lock del canal» (`DeliveryRepository.cs:16-20`); el conteo bajo el lock recorre
  hasta la capacidad y su índice no incluye `ExpiresAtUtc` (`QueuedDeliveryConfiguration.cs:15`).
- **O6.** Las claves de lock `delivery:` y `delivery-retention:` no las fija el test de claves, aunque
  `AdvisoryLockKeys.cs:15` dice que el test fija cada texto; tampoco figuran en `backend.md:104`. Si alguien cambia
  el texto de una clave, durante un despliegue las dos versiones reservan la misma fila y mandan dos veces.
- **O7.** Un único diferido que choca al confirmar da 500, lo correcto para un bug, pero `IUnitOfWork.cs:23-25`
  promete traducirlo.
- **O8.** Comentarios desactualizados: `LoginCodeService.cs:39-41` (la cola ya es durable),
  `AccountCreationPolicy.cs:11-12` (llama «por defecto» a InviteOnly), `UserReader.cs:263-264` (un índice que no
  existe), `EmailOptions.cs:28-31` y `WhatsAppOptions.cs:72` (una sección que ya no existe), `RoleRow.cs:3`
  («activos»), `ISignInService.cs:29-30` y `LoginCodeVerifier.cs:67`; `Errors.resx:62` dice que a los roles del
  sistema no se les cambian permisos, pero a «User» sí; y en el front, `users.ts:30-31` dice que una invitación sin
  estado salió, lo que ayuda a ocultar N5.
- **O9.** `IStructuredCatalogProposalSink` no cruza ninguna frontera: su único consumidor y su único implementador
  están en Formularios, y su versión apagada es inalcanzable (`FormCatalogAssistantProposalService.cs:19,73`,
  `FormCatalogProposalService.cs:21-29`), contra `Interfaces/Channels/AGENTS.md:12,22`; además, un servicio llama
  al que guarda de otro, contra la letra de `backend.md:94`. Arreglo: una pieza interna de Formularios sin
  `IUnitOfWork`, dentro del límite de quien la llama.
- **O10.** `LoginAudit`, `LoginCode` y `StoredFile` no tienen FK a la cuenta y no dicen por qué en su archivo, como
  sí lo dicen `LoginLink` y `UserInvitation` («las cuentas no se borran de verdad»); con el borrado lógico no tiene
  efecto.
- **O11. Costos documentados del ingreso.** Pedir un código para una cuenta desactivada responde
  `403 Auth.Account.Disabled` antes de emitirlo, así que revela qué cuentas están desactivadas (`identidad.md:42`,
  `SignInCodeIssuer.cs:35-39`), contra la sección 5.3 del diseño base, que dice «se informa después de verificar el
  código» (`:288`); regenerar los PFX de OpenIddict invalida todos los tokens (`identidad.md:38`); y si Google y otra
  escritura tocan la misma cuenta a la vez, la respuesta es un 500 (`identidad.md:33`).
- **O12.** `InvalidateRoleAsync` sobra en `RoleService` con claves versionadas, porque la clave nueva sólo se llena con
  lo confirmado; quitarlo cambia `permiso-nuevo.md:26`, `identidad.md:37` y el ADR 0010.

## Separar el núcleo de los módulos: inventario, no defecto

Si un módulo pasara a ser un servicio con su propia base, además de lo que ya inventarió la revisión de WhatsApp
habría que resolver:

- **Locks y lecturas en la misma transacción a través de puertos:** WhatsApp toma, en su transacción, el lock global
  de formularios al enviar y al retirar Flows (`WhatsAppFormSendPolicy.cs:75,89` y
  `WhatsAppFlowDeprecationService.cs:31,109`, hacia `BusinessFormSource.cs:30`), y Formularios lee tablas de WhatsApp
  bajo su lock en la transacción que borra una definición (`FormLifecycleService.cs:21-30`). Está documentado
  (`Interfaces/Channels/AGENTS.md:13,58,60`) y no hay escrituras cruzadas, pero corrige el inventario de WhatsApp,
  que decía «sin transacciones compartidas»: los puertos corren en la transacción de quien llama.
- **Un guardado del núcleo que corre código de un módulo:** la invitación por WhatsApp marca la `UserInvitation`
  dentro del guardado del núcleo (`WhatsAppInvitationChannel.cs:82-85`).
- **Lectores que unen tablas del núcleo:** Formularios en 18 archivos, WhatsApp en 3 e IA en 2; ARCA en ninguno.
- **Quien recibe confía en quien llama:** ARCA revisa, cancela y encola una reversión por `runId` sin volver a pedir
  el permiso, que controla Formularios (`ArcaStructuredRecordIssuer.cs:74-81`, `FormIssueReversalRecorder.cs:28`).
- **Lo que el núcleo tiene por los módulos:** `CuitNumber`, que usan ARCA y Formularios, y `BankAccountKey`, que usa
  sólo Formularios; y `LoginMethod.WhatsAppCode` y `WhatsAppLink` (`LoginMethod.cs:15-18`).
- **H32 pasa por un puerto del núcleo:** arreglar el lock global de formularios cambia también `LockSelectionAsync(ct)`,
  que no recibe la definición (`IStructuredFormSource`, `IStructuredFormRetirementSource`; `BusinessFormSource.cs:30`).
- **Una sola historia de migraciones,** con archivos que mezclan tablas del núcleo y de un módulo (`20261107004318`,
  `20261113213511`): es lo que pide la regla (`backend.md:51`), y las guías de quitar un módulo conservan todas las
  migraciones.

Ninguna regla vigente lo prohíbe: `backend.md:249-250` sólo prohíbe llamar al escritor de otro módulo dentro de una
transacción. Separar un módulo pediría un ADR nuevo; el [ADR 0007](../decisions/0007-whatsapp-como-modulo-opcional.md)
lo descarta por prematuro.

## Lo que está bien

- **Guardar.** `UnitOfWork` no se anida, suelta los locks aunque se cancele el pedido, traduce el único a 409 después
  de deshacer y no reintenta (`UnitOfWork.cs:14-16,31-45,63-101`); lo prueban sus tests de commit fallido. Los locks
  van por recurso, ordenados y sin repetir (`AdvisoryLockExtensions.cs:24-38`).
- **La cola durable.** Encolar exige la transacción del llamador y un rollback deshace el envío
  (`Enqueue_requires_the_callers_transaction`,
  `Rollback_removes_the_delivery_and_a_new_consumer_can_claim_committed_content_once`); la reserva es exclusiva entre
  dos hosts (`Two_hosts_compete_for_one_delivery_and_only_one_claims_it`), y el contenido va cifrado con un propósito
  por canal y tipo (`DurableMessageStore.cs:20`).
- **El ingreso.** Los códigos se guardan como HMAC ligado a destino y propósito, se comparan en tiempo constante,
  admiten cinco intentos y sus límites corren bajo lock (`LoginCodeConcurrencyTests`); el enlace es de un solo uso
  (`Simultaneous_redeems_of_the_same_link_open_a_single_session`); la cookie sale después del commit y sólo desde los
  puntos de entrada (`IdentityBoundaryTests`).
- **Las sesiones.** PKCE obligatorio; reusar un refresh token revoca la cadena
  (`Refresh_token_rotates_and_reusing_an_old_one_revokes_the_whole_chain`); desactivar o borrar corta la sesión en el
  acto (`Deleting_cuts_off_the_session_that_was_already_open`). En el front, los tokens viven sólo en memoria y se
  renueva una vez por pestaña.
- **Los permisos.** No viajan en el token y se leen en cada pedido: un cambio vale en el pedido siguiente y en todas
  las copias (`Changing_the_permissions_of_a_role_takes_effect_for_its_users_right_away`,
  `Committed_claim_additions_and_removals_are_visible_to_an_independent_api`). La caché no guarda lo que no se
  confirmó, Admin no pierde permisos y un rol del sistema no se borra.
- **El último administrador.** Se cuenta en fila con un lock global
  (`Two_admins_deactivating_each_other_at_once_leave_exactly_one`), y nadie se saca Admin a sí mismo.
- **La configuración.** El PATCH compara la revisión bajo `FOR UPDATE` y responde 409
  (`Two_concurrent_edits_have_one_winner_and_another_host_observes_the_commit`); la caché se descarta después del
  commit; el front conserva el borrador ante un 409. Es el modelo para N2.
- **Los archivos.** Se controlan el tipo real, el tamaño, el SHA-256 en tiempo constante y los píxeles antes de
  decodificar, y OOXML se lee sin DTD (`Hash_size_and_actual_type_fail_before_any_file_can_be_stored`); la subida va
  fuera de la transacción y la escritura condicional deja una sola copia
  (`Conditional_private_uploads_from_two_stores_preserve_exactly_one_winning_copy`).
- **El correo.** Cada valor de las plantillas se escapa (`The_name_in_the_invitation_is_html_encoded`) y sale en el
  idioma de quien recibe (`The_email_goes_in_the_language_of_the_account`).
- **El arranque.** Valida las opciones antes de tocar la base y no arranca sin migrar (`ProductionStartupTests`);
  liveness y readiness van separadas (`HealthCheckTests`); el seed corre con lock y en un solo límite
  (`DatabaseSeederTests`).
- **El borde HTTP.** Todo error sale como ProblemDetails traducido y con `traceId` (`FrameworkErrorsTests`,
  `ErrorHandlingTests`); los encabezados reenviados son de confianza y la CSP es estricta; el front no reintenta
  escrituras y avisa cuando un 5xx deja el resultado incierto.
- **Data Protection.** El anillo se comparte entre copias, va cifrado y sin cifrado la Api no arranca
  (`DataProtectionAtRestTests`).
- **Los puertos.** El ejecutor de capacidades abre un scope por llamada (`AssistantCapabilityExecutorTests`), las
  capacidades sólo leen (`AiAssistantCapabilityBoundaryTests`), las versiones apagadas fallan del lado seguro, los
  dueños revalidan a la persona nombrada y antes de repetir una emisión se consulta al emisor.
- **Los logs.** No llevan códigos, tokens, enlaces ni números (`No_log_carries_the_token_its_hash_the_url_or_the_number`).

## Controles y pruebas que conviene sumar

- **Controles automáticos, para que no vuelva:**
  - los contratos de escritura de las áreas de configuración llevan `ExpectedRevision` (N2);
  - una cuenta con sólo `users.manage` o sólo `roles.manage` no da lo que no tiene (N1);
  - todo worker que lee una cola durable devuelve lo que no mandó, y la reserva salta los tipos que su versión no
    conoce (N8 y N27);
  - ningún cliente HTTP repite un POST ante un 503 (N18);
  - cada orden y cada `ILIKE` con comodín inicial tienen su índice, o una excepción declarada (N30);
  - las migraciones posteriores a una fecha no renombran, no borran ni crean índices que bloquean (N34);
  - la composición completa con `ValidateScopes` y `ValidateOnBuild`, y la unicidad de capacidades, emisores y
    permisos entre módulos; y WhatsApp con IA sin Formularios, armando el contexto del asistente (B5);
  - `Caching:KeyPrefix` y `ForwardedHeaders` obligatorios fuera de Development, y el modo `.eml` sólo en Development
    y Testing (N14, N17 y N19);
  - las claves `delivery:` en el test de claves de locks (O6);
  - los records de `Models/StructuredData` y `Models/Assistant` redactan su `ToString` (N26);
  - cada tabla con datos personales nombra quién la purga (N16);
  - sin llamadas sincrónicas de EF en Infrastructure, salvo una lista (N29).
- **Pruebas que faltan:**
  - bloquear una cuenta con verificaciones sin código, y que Google siga entrando (N3);
  - dos canjes simultáneos del código correcto: un 200 y una sola cookie;
  - apagar el worker de correo durante su espera y con el SMTP caído (N5 y N8);
  - restaurar por número una cuenta que tenía correo y Google (N4);
  - dos ediciones del mismo usuario y del mismo rol (N2);
  - Redis caído con la app abierta (N9).
- **Pruebas reales:**
  - carga del pedido de código y de `/health` contra el plan de base del despliegue, midiendo conexiones y la espera
    del lock (N6 y N29);
  - el listado, los conteos y el catálogo de roles con un millón de cuentas, con `EXPLAIN ANALYZE` (N30 a N32);
  - el primer despliegue real, con los chequeos de Container Apps y `AllowedHosts` (N17);
  - restaurar base y contenedor juntos (N15 y H15).

## Límites de esta revisión

- **Alcance.** Es de solo lectura sobre `23797fff` y `7b95095b`, y no incluye los cambios sin commitear de ese día.
- **Sin ejecución.** No se corrió nada: los tiempos, los techos y las frecuencias salen del código y se confirman con
  las pruebas de la sección anterior.
- **Despliegue.** El despliegue a Azure todavía no está activado. Lo de Container Apps y del plan de la base sale de
  las guías y del comportamiento por defecto de la plataforma y de las librerías (por ejemplo, la conexión sin pool
  del chequeo de salud), sin una prueba en el repositorio.
- **Front.** Se leyó en `7b95095b`, sólo donde hacía falta para un criterio.
- **Módulos.** Se miraron sólo donde cruzan la frontera; IA y Formularios tienen su propia revisión pendiente.

## Decisiones del 2026-11-14

En nueve de las diez elegiste la opción recomendada; en el correo de producción, mantener Gmail. Las reglas escritas
cambian con su arreglo, no antes: N3 y B4, en [identidad](../features/identidad.md); N4 y el PUT de ajustes de N2, en
[administración](../features/administracion.md); N1 y N25 suman reglas nuevas; N9 enmienda el
[ADR 0009](../decisions/0009-redis-como-unico-cache-de-datos.md) y lo que lo cita; y N27 suma la suya a
`Models/StructuredData/AGENTS.md`.

- **Horizonte: «Hoy», más N27.** El plan lleva N1 a N26, con el tamaño del pool de N6 y el retiro del PUT de ajustes
  de N2, y N27, que ya pesa con una sola copia: cada despliegue hace convivir dos versiones, y volver a la imagen
  anterior rompe lo que guardó la nueva.
  - De N28, el ETag va con «Hoy», porque la carrera pasa también con una sola copia; la lease, antes de pasar de una
    réplica (hoy `--max-replicas 1`, en [desplegar a Azure](../deploy/azure-setup.md)); y listar sólo lo pendiente,
    a gran escala.
  - De gran escala entra sólo lo trivial que no cambia contratos: los índices de N31 y dejar de pedir los conteos
    que no se muestran (N30). N33 se resuelve con N9.
  - El resto de N29 a N34, cuando haya volumen real que medir.
- **N1: nadie da lo que no tiene.** La regla vale en cuentas y en roles, evaluada dentro del límite con los permisos
  efectivos de quien pide:
  - nadie da ni quita un permiso, o un rol con permisos, que no tiene, ni en el alta ni en la edición;
  - nadie actúa sobre una cuenta con permisos que él no tiene: cambiarle el correo o el número, desactivarla o
    borrarla;
  - sólo Admin edita los permisos de «User».

  Se sigue pudiendo delegar. Se descartó que sólo Admin asigne y edite roles, porque quita delegar y no cubre el
  cambio de correo de un administrador, y también dejar escrito que esos dos permisos equivalen a ser
  administrador, porque deja abierta la toma de cuentas.
- **N3: el bloqueo no se usa contra otros.** Van las tres medidas juntas: el bloqueo por códigos deja de frenar a
  Google, `AlreadyUsed` y `Expired` responden sólo al código que coincide, y `LockedOut` sale recién después de un
  código correcto. Cambia la regla de `identidad.md:29`. Se descartó aceptar que `LockedOut` revele la cuenta.
- **N4: restaurar sin el acceso anterior.** Al restaurar se quita lo que el alta no trajo (correo, número y logins
  externos), y la pantalla dice que se restauró: la cuenta conserva su historia y el dueño anterior queda afuera.
  Cambia `administracion.md:14`. Se descartó no restaurar con el número solo, que no cubre un correo que pasó a otra
  persona, y también pedir confirmación con un 409, que deja la decisión en quien da el alta.
- **N9: con Redis caído, la app sigue.** Se degrada de verdad:
  - `/health` en Degraded, como WhatsApp;
  - los permisos, desde la base con una consulta en el contexto del pedido, que también resuelve N33;
  - la configuración, desde la base si Redis no responde;
  - un texto propio en el front para un 5xx sin código, que cubre también los 502 y 504 del ingress.

  Enmienda el ADR 0009. Se descartó lo mínimo, sólo el chequeo y el texto, porque deja sin ingreso hasta que vuelva
  Redis.
- **N7: el correo de producción sigue en Gmail.** Van los topes de N7: uno diario por dirección agrupada por /64,
  uno global por debajo del cupo de la cuenta con aviso al 80 %, uno por cuenta para los códigos del perfil y uno
  diario de invitaciones por administrador. El cupo de la cuenta de Gmail que se use (unos 500 correos por día en
  una cuenta gratuita) se escribe en la [guía de despliegue](../guides/despliegue.md), con el tope global por
  debajo. El costo aceptado: ese cupo es el techo de códigos e invitaciones juntos, y un abuso desde varias
  direcciones puede agotar el tope global y dejar a todos sin códigos ese día. Se descartó el proveedor
  transaccional que recomendaba la revisión.
- **N27: una sola regla de versiones.** H25 se extiende a `DurableMessageSource` y a los modelos que guardan los
  módulos, con las tres partes de N27:
  - lo que una copia no puede leer no se borra: vuelve a la cola con una espera y un tope de intentos;
  - la reserva toma sólo los tipos de mensaje que su versión conoce;
  - un campo, tipo o valor nuevo se escribe un despliegue después del que ya sabe leerlo.

  Hecha en la clase común, cubre de una vez el correo, WhatsApp y los módulos: la tarea 22 del
  [plan de la bandeja](../plans/2026-10-01-bandeja-whatsapp.md) se programa con esta regla.
- **N16: plazos de retención.** Los códigos y enlaces se conservan 30 días (los límites leen 24 horas); los
  `LoginAudits`, 12 meses, y después quedan sin la IP; las autorizaciones y tokens de OpenIddict se podan a los 14
  días de vencidos. Lo hace un worker con lease que borra por lotes y conserva el último ingreso de cada cuenta.
  Completa el pendiente de [verificación y producción](../plans/verificacion-y-produccion.md). La pregunta no nombró
  las invitaciones (`UserInvitations`): su plazo lo fija el plan, con la opción más simple que no le quite datos a
  una pantalla.
- **N24 y N25: uno se mantiene y el otro se cierra.** N24 queda como costo aceptado: la invitación sale en el idioma
  general, como dice `administracion.md:74-78`. N25 se cierra con la guarda trivial: la pantalla no deja quitarle
  Admin ni cambiarle el correo al administrador del seed, y avisa que hay que cambiar `Seed:AdminEmail`.
- **B4: se retira el enlace de ingreso.** Se quitan el emisor, las dos rutas de canje y su regla (`identidad.md:43`).

## Cómo seguir

- **Tus decisiones:** tomadas el 2026-11-14 ([decisiones](#decisiones-del-2026-11-14)).
- **El plan:** B1 a B5, N1 a N26 (menos N24) y lo que sumaron las decisiones (lo que falta de N27, el ETag de N28, los
  conteos de N30, los índices de N31 y N33 con N9), en 31 tareas y seis etapas del
  [plan de los arreglos del núcleo](../plans/2026-11-14-arreglos-del-nucleo.md), que se programa antes de la prueba
  integral, como elegiste el mismo día. La lease de N28 va antes de pasar de una réplica; el resto de gran escala, cuando
  haya volumen real que medir.
- **Lo que ya estaba anotado:**
  - la retención de N16, en [verificación y producción](../plans/verificacion-y-produccion.md);
  - la exportación de telemetría de N10, a medias, en el mismo plan;
  - H25, en la etapa 7 del [plan de la bandeja](../plans/2026-10-01-bandeja-whatsapp.md), que se programó junto con
    N27 en 42d8f47f.
- **IA y Formularios:** su revisión arranca después, con el cupo medido.
