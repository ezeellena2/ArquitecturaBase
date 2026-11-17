# Traspaso 14 · 02 · El núcleo

Lo que cambió en el núcleo de la plantilla (identidad, administración, correo, arranque y datos) entre el traspaso 13
(backend `23797fff`, front `7b95095b`) y hoy. Sale de la [revisión de arquitectura del núcleo](../../reviews/2026-11-14-arquitectura-nucleo.md)
del 14/11, que juzgó el núcleo con 30 criterios, y de las 31 tareas de su [plan](../2026-11-14-arreglos-del-nucleo.md),
programadas el 15 y el 16/11 en seis etapas. Cada punto cuenta la regla o el comportamiento nuevo del producto, no el
código. Todo está commiteado y **todavía no pasó la prueba integral**: puede cambiar algún detalle. Los códigos de error
(`Users.User.…`, `Roles.Role.…`) son los estables que el front traduce.

## 1. Quién puede qué (etapa 1)

- **Nadie da lo que no tiene** (N1, `6f6e8c2d`). Delegar «Administrar usuarios» o «Administrar roles» ya no equivale a
  ser administrador. Quien pide sólo da o quita roles y permisos que tiene, y sólo actúa sobre una cuenta cuyos permisos
  tiene todos: cambiarle el correo o el número, desvincularlo, desactivarla, reactivarla o borrarla. Los permisos de «User»
  los cambia sólo Admin. Los rechazos son 403 propios: `Users.User.RolesNotHeld`, `Roles.Role.PermissionsNotHeld`,
  `Users.User.HasMorePermissions` y `Roles.Role.UserRoleAdminOnly`. Los permisos de «User», que recibe toda cuenta,
  cuentan como propios de cualquiera. Se descartó que sólo Admin asigne roles: quita delegar y no cubre el cambio de correo.
- **El administrador inicial no pierde Admin desde la pantalla** (N25, mismo commit). Quitarle Admin o cambiarle el correo a
  la cuenta de `Seed:AdminEmail` da 409 `Users.User.InitialAdmin`, con el aviso de que hay que cambiar esa clave. Darle ese
  correo a otra cuenta cuenta como darle Admin.
- **Restaurar una cuenta borrada la deja sin el acceso anterior** (N4, `14d0d4b6`; front `493a72f4`). El correo o el número
  pudieron pasar a otra persona. La cuenta vuelve con su historia pero sólo con lo que trajo el alta, sin verificar: lo que
  el alta no trajo (correo, número, logins externos) se quita. La respuesta dice que se restauró, y la pantalla lo muestra.
- **Google sólo se vincula a un correo ya confirmado** (N4, decisión del 15/11; `d7d7fa9e`, front `10360f3d`). Una cuenta
  cargada por un administrador, o restaurada, entra la primera vez con el código al correo; Google se vincula después. Si
  no, 403 `Auth.ExternalLogin.AccountEmailNotConfirmed`. Costo aceptado: la persona invitada por correo no entra con Google
  de entrada.
- **Cada medio confirma** (N11, `531d55c0`; front `5b72690c`). Desde «Mi perfil»: agregar un correo con número verificado
  pide además un código al número; vincular un número con correo verificado, uno al correo; desvincular el número, uno al
  correo. Un correo o un número ya verificados no se reemplazan desde el perfil (`EmailAlreadySet`, `PhoneAlreadySet`):
  los cambia un administrador. Rutas nuevas de confirmación: `POST /api/me/whatsapp/confirmation-code` y
  `POST /api/me/email/confirmation-code` (con `change`).
- **El bloqueo no se usa contra otros** (N3, `64b6ae9e`). El bloqueo por códigos sólo suma contra un código activo y ya no
  frena a Google. `AlreadyUsed` y `Expired` responden sólo al código que coincide, y `LockedOut` sale después de un código
  correcto. Con un código cualquiera no se descubre si la cuenta existe. Dos canjes del mismo código a la vez dan un 200.
- **El límite por IP agrupa IPv6 por /64** (N20, `64b6ae9e`).
- **Cerrar sesión cierra las demás pestañas** (N12; front `3199dedb`, regla `860c6c04`). El front avisa por
  `BroadcastChannel` y cada pestaña revoca su propia autorización.
- **Se retira el enlace de ingreso** (B4, `44c731c6`; front `32bc6a00`): el emisor, las dos rutas de canje y la pantalla
  `/ingresar`. La tabla queda hasta que la vacía la retención (§5).

Plan: [etapa 1](../2026-11-14-arreglos-del-nucleo.md#etapa-1-quién-puede-qué). Decisiones 2, 3, 4, 9 y 10 de la revisión.

## 2. Ediciones simultáneas y su historia (etapa 2)

- **Usuarios y roles con control de revisión** (N2, B2 y O4, `ede20d37`; front `98bbacbe`). Cuentas y roles traen `revision`
  (`long`, nace en 1) y la edición la devuelve en `expectedRevision`. Si cambió en el medio, 409
  `Users.User.RevisionConflict` o `Roles.Role.RevisionConflict`. La avanza cada cambio del nombre, el correo, el número, los
  roles, el estado o el borrado, venga de donde venga; el idioma y la zona no. Activar, desactivar y borrar bloquean la
  fila y la avanzan, pero no la piden. Dos activaciones a la vez ya no dan 500.
- **Borrar un rol choca con asignarlo**: gana quien llega primero y el otro recibe `Roles.Role.HasUsers` o `NotFound`, en vez
  de borrar en cascada.
- **El front conserva el borrador.** Si el 409 llega y no cambió nada de lo que el guardado reemplaza, lo resuelve solo; si
  cambió, ofrece revisar, como `/configuracion`. El editor de roles avisa qué permisos descartaría.
- **Admin se edita después de quitar un módulo** (B3, `2de0cd3d`): sólo se comparan los permisos del catálogo.
- **Queda la historia de los cambios administrativos** (N13, `7b5d24eb`). Una tabla `AdministrativeChanges`: quién, sobre
  qué, roles o permisos agregados y quitados, estado antes y después. Cubre roles (con el borrado y los permisos que tenía),
  asignación, estado, borrado, restauración, cambio de correo o número y el modo de registro. Sólo guarda el estado del
  correo y del número, nunca el dato; el seed queda con actor vacío. **No tiene pantalla**: verla sería una pantalla nueva y
  se dibuja antes. Se conserva, porque es el rastro.

Plan: [etapa 2](../2026-11-14-arreglos-del-nucleo.md#etapa-2-ediciones-simultáneas-y-su-historia).

## 3. El correo (etapa 3)

- **No se pierde ni sale dos veces** (N5, N8 y O3, `e7ce1bbc`; front `d16e21a0`). Una falla antes de mandar el cuerpo no
  descarta: la fila vuelve a pendiente con espera creciente, hasta que vence. Se reintentan sólo las fallas de conexión, de
  autenticación y las 4xx; un corte durante el envío queda incierto y no se repite. El envío tiene plazo propio, que cabe
  en el apagado de 25 s. El correo de un código vence en la cola con el `ResendCooldown`.
- **La invitación tiene estado de entrega**: `Pending` hasta que el servidor acepta el correo, `Sent` cuando guarda el
  Message-ID, `Uncertain` si vence sin noticias y `Failed` si se agotó. El front deja de dar por enviada una sin estado.
- **Un chequeo `email` en Degraded** que marca enseguida un 535, y contadores de enviados y descartados.
- **Topes diarios** (N7, `882b8717`; front `c7fd7551`; decisión 6, Gmail se mantiene). Por dirección agrupada por /64, uno
  global por debajo del cupo de la cuenta (unos 500 por día en una gratuita) con aviso al 80 %, uno por cuenta para los
  códigos del perfil y uno diario de invitaciones por administrador, con una reserva de 50 invitaciones dentro del tope
  global. Se cuenta todo código que sale, para no revelar qué cuentas existen. Costo aceptado: un abuso desde muchas
  direcciones puede agotar el global y dejar a todos sin códigos ese día. Las esperas largas se muestran en horas y minutos.
- **El modo `.eml` sólo en Development y Testing** (N14, `42cb2297`): fuera de ahí la Api no arranca con `PickupDirectory`.

Plan: [etapa 3](../2026-11-14-arreglos-del-nucleo.md#etapa-3-el-correo).

## 4. Redis, la salud y el arranque (etapa 4)

- **Con Redis caído, la app sigue** (N9, N33 y O12, `8815e4cc`; front `20698a7f`; decisión 5;
  [ADR 0032](../../decisions/0032-sin-redis-la-app-sigue.md), que enmienda el 0009 y el 0010 en lo que toca a la
  invalidación). `/health` marca Redis en Degraded y la copia sigue en el tráfico. Los permisos se leen de la base, una
  consulta por persona y por pedido, sin caché (se va la invalidación de permisos). La configuración se lee de la base si
  Redis no responde y, sin la fila, vale `InviteOnly` con la presentación por defecto. El seed no frena el arranque. Lo
  que sigue necesitando Redis: guardar ajustes (500 después de confirmar) y el ingreso por WhatsApp (el código espera la
  lease). El front tiene un texto propio para un 5xx sin `traceId`, que cubre los 502 y 504 del ingress.
- **`/health` no agota la base** (N6, `997a9983`). El resultado se guarda dos segundos y una ráfaga lo comparte. Hay un
  limitador global, uno de concurrencia y un tope de cuerpo, antes de la autenticación, y el pool por réplica queda por
  debajo del límite del servidor. `/health` sigue pública.
- **El arranque frena la configuración incompleta** (N17, `bc618e93`; N19, `390d1a4a`): fuera de Development y Testing se
  validan `ForwardedHeaders` (`KnownProxies`, `KnownNetworks`) y es obligatorio `Caching:KeyPrefix`. El apagado queda en 25 s.
- **La resiliencia por defecto no reintenta un POST** (N18, `bc74b2ca`).
- **El error 500 se puede rastrear** (N10, `848ced95`): fuera de dev, la consola escribe JSON con el id del usuario en el
  scope, y la guía documenta el exportador OTLP y una alerta de 5xx.
- N6, N10, N17 y N19 se terminan de comprobar en el primer despliegue a Azure.

Plan: [etapa 4](../2026-11-14-arreglos-del-nucleo.md#etapa-4-redis-la-salud-y-el-arranque).

## 5. Datos, archivos y retención (etapa 5)

- **Los datos del ingreso se purgan** (N16 y N31, `12eb2a28`; decisión 8). Un worker, en todas las copias y sin que dos
  corran el mismo lote, borra por lotes: códigos y enlaces a los 30 días; `LoginAudits` sin IP ni navegador a los 12 meses
  (conserva el último ingreso de cada cuenta, y el identificador escrito de los fallidos también se olvida); autorizaciones
  y tokens de OpenIddict a los 14 días de vencidos; invitaciones a los 30 días, salvo la última de cada cuenta. Un control
  exige que cada tabla con datos personales nombre quién la purga. Van también los índices parciales de N31.
- **La limpieza de archivos borra con el ETag del listado** (N28, `9be27c20`), para no borrar una foto vuelta a subir.
- **Los archivos privados tienen borrado temporal** (N15, `e870c645`): la guía de Azure pide borrado temporal, versionado
  y feed de cambios, y el ensayo de restauración restaura base y contenedor juntos.
- **Usuarios deja de pedir los conteos** que no muestra (N30, front `9dd88005`).

Plan: [etapa 5](../2026-11-14-arreglos-del-nucleo.md#etapa-5-datos-archivos-y-retención).

## 6. Lo trivial y la documentación (etapa 6)

- **La plantilla viene apagada y sin datos de esta instalación** (B1, `b3d864fa`): WhatsApp apagado, y sin correo
  personal, administrador inicial ni id de Google en la configuración versionada; van a user-secrets.
- **Sin Formularios, el asistente de WhatsApp anda** (B5, `3083af18`): pide las capacidades `forms.*` sólo si están.
- **Números con la región de quien lee** (N21, `ea6739df`; front `cf059e74`) en roles y usuarios, también lo que lee el modelo.
- **Textos neutrales en el núcleo** (N22, `f8ffddf5`; front `6a71e6ef`): «no hay almacenamiento» y «falta el objeto».
- **Volver a agregar un módulo pide revisar los roles** (N23, `d794610c`): los permisos que nadie quitó vuelven a valer.
- **Los modelos neutros redactan su `ToString`** (N26, `1e330082`) y **leen lo desconocido sin romper** (N27, `93f643c1`):
  un valor nuevo de un enum se lee como el neutro, o la fila queda para otra copia. Se escribe lo nuevo un despliegue
  después del que ya sabe leerlo.

## Contratos HTTP que cambiaron

- **Se retiró `PUT api/settings`** (el de un solo ajuste, sin revisión): nadie lo usaba. Queda el que lleva `expectedRevision`.
- **`revision` y `expectedRevision`**: `GET /api/users/{id}`, `GET /api/me` y `RoleResponse` la traen; `PUT /api/users/{id}`,
  `PUT /api/me` y `PUT /api/roles/{id}` la devuelven. 409 `RevisionConflict` si cambió.
- **Rutas nuevas:** `POST /api/me/whatsapp/confirmation-code` y `POST /api/me/email/confirmation-code`; `PUT /api/me/email`
  acepta `phoneCode`, `PUT /api/me/whatsapp` acepta `emailCode` y `DELETE /api/me/whatsapp` lleva `{ code }`.
- **Rutas retiradas:** `/account/login-link/preview` y `/redeem`.
- **Errores nuevos** (403, 409 y 400 traducidos): los de N1, `InitialAdmin`, `NoVerifiedPhone`, `NoVerifiedEmail`,
  `EmailAlreadySet`, `PhoneAlreadySet`, `NoPhone`, `ConfirmationCodeRequired` y `AccountEmailNotConfirmed`.
- `deliveryStatus` de la última invitación suma `Uncertain` y usa `Pending` y `Sent` con el estado real del correo.
- El front reconoce un 5xx sin `traceId` como falla de infraestructura.

## Lo que quedó para después

Eligió el usuario el horizonte «Hoy», más N27. Quedan fuera, hasta que haya volumen real que medir o se pase de una réplica:
N24 (la invitación sale en el idioma general, costo aceptado); la lease de N28, antes de pasar de una réplica; listar sólo
lo pendiente (N28), el índice y los trigramas de N30, N29, N32 y N34; las pruebas reales de la revisión (carga, un millón
de cuentas, el primer despliegue y la restauración); y la revisión de IA y Formularios. Falta también la pantalla de la
historia de N13.

## Para el multitenant

- **Permisos por organización.** N1 se evalúa con los permisos efectivos de quien pide dentro de la organización, no
  globales; «User» y Admin son por organización, y `Seed:AdminEmail` pasa a ser el dueño de la organización o un
  administrador de plataforma. La historia de N13 y la revisión llevan la organización.
- **RLS.** Los topes de correo (por dirección, globales, por administrador), la retención y el recuento de administradores
  tienen que decidir si miran una organización o toda la plataforma. El worker de retención corre sin una organización y
  necesita un rol que salte RLS. `AdministrativeChanges` entra en las políticas.
- **Varias copias.** La cola de correo, los topes y la retención ya están pensados para varias copias; con Redis caído,
  cada copia sigue.
- **Adaptar:** `Seed:AdminEmail`, el cupo de Gmail por organización (o un proveedor), el orden de bloqueos y el modelo de
  empresas y clientes en el borrado y la restauración.

Preguntas para el usuario del multitenant (recomendada: la de ArquitecturaBase):
1. ¿Nadie da lo que no tiene, evaluado por organización? Recomendada: sí.
2. ¿Los topes de correo son globales de la plataforma, o uno por organización? Recomendada: global, bajo el cupo.
3. ¿La retención con los mismos plazos (30 días, 12 meses, 14 días)? Recomendada: sí.
4. ¿Con Redis caído la app sigue, leyendo permisos de la base? Recomendada: sí.
5. ¿La historia administrativa sin pantalla, hasta dibujarla? Recomendada: sí.
