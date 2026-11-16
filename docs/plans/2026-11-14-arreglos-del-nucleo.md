# Arreglos de la revisión de arquitectura del núcleo

Decididos el 2026-11-14 y sin programar. Salen de la [revisión de arquitectura del núcleo](../reviews/2026-11-14-arquitectura-nucleo.md),
que tiene el escenario, la evidencia (archivo y línea en `23797fff`) y el esfuerzo de cada hallazgo, y de las
[decisiones del usuario](../reviews/2026-11-14-arquitectura-nucleo.md#decisiones-del-2026-11-14), que no se vuelven a preguntar.
El núcleo no tenía un plan propio donde anexarlos; el pendiente de retención de
[verificación y producción](verificacion-y-produccion.md) lo completa la tarea 21.

- **Alcance:** los hallazgos de «Hoy» (B1 a B5 y N1 a N26, salvo N24) y lo que las decisiones sumaron de los otros
  horizontes: lo que falta de N27, el ETag de N28, dejar de pedir los conteos de N30, los índices de N31 y N33, que se
  resuelve con N9. De las observaciones menores entran las que un arreglo toca de paso: O3 con N5, O4 con N2, O12 con N9
  y los comentarios de O8 que contradicen un arreglo.
- **Fuera:** N24, que quedó como costo aceptado; la lease de N28, antes de pasar de una réplica; el resto de gran escala
  (listar sólo lo pendiente en N28, el índice y los trigramas de N30, N29, N32 y N34), cuando haya volumen real que medir;
  las demás observaciones, que quedan anotadas en la revisión; y las pruebas reales de la revisión (carga, un millón de
  cuentas, el primer despliegue y la restauración), que esperan al despliegue a Azure o a tener volumen real.
- **N27, casi hecho:** 42d8f47f, de los arreglos de WhatsApp, ya aplicó la regla única. Cada fuente de la cola durable
  reserva sólo los tipos que conoce, lo que no descifra o no lee vuelve a la cola con espera y tope en lugar de borrarse, el
  correo y la cola de acceso leen estricto, un motivo de lectura desconocido se lee como `Other` y las guías piden escribir
  lo nuevo un despliegue después del que ya sabe leerlo. Falta lo que ese commit dejó afuera en los modelos neutros: la
  tarea 31.
- **Al día:** el 2026-11-14, el único commit que tocó el código del núcleo desde `23797fff` fue 42d8f47f, que sumó a
  `QueuedDelivery` la espera (`AvailableAtUtc`) y el conteo de lecturas fallidas; N5 y N8 reusan esa espera. Cada tarea
  vuelve a leer sus archivos antes de empezar.
- **Sin revisor:** el cupo semanal estaba en 85 % al escribirlo, así que el plan se contrastó sólo con la revisión, sin un
  revisor contra el código. La primera tarea de cada etapa relee el código de sus hallazgos y corrige el plan si hace falta.
- **Sin datos que convertir:** no hay producción. La revisión nueva de usuarios y roles arranca en 0 en las filas que ya
  existen, y la retención corre igual sobre los datos de desarrollo.
- **Cuándo:** antes de la prueba integral, como eligió el usuario el 2026-11-14, para probar todo una sola vez. Arranca
  cuando lo permita el cupo semanal, que se reinicia el 17/11 a las 23:59, de a una etapa y mirándolo antes de cada una. B1
  (tarea 25) va antes de la prueba y no durante, porque cambia la configuración de esta PC.

**Cómo se trabaja.** Cada tarea sigue [validar un cambio](../guides/validar-un-cambio.md): el test en rojo primero y las suites
rápidas del área. La revisión adversarial va completa en lo que toca permisos, cuentas, datos personales o el correo, y no en
documentación, textos ni arreglos de una línea. Cada etapa cierra con el build sin advertencias, los unitarios y
`ArchitectureTests` enteros y la integración de lo que cambió; la corrida completa, con Docker y los controles del front, va
una sola vez, al cerrar el desarrollo. Además:

- una regla escrita que cambia se corrige con su arreglo, no antes, y cada tarea nombra las suyas. Casi todas están en
  [identidad](../features/identidad.md), [administración](../features/administracion.md), [backend](../architecture/backend.md),
  las guías de [despliegue](../guides/despliegue.md) y de [Azure](../deploy/azure-setup.md) y los `AGENTS.md` de cada carpeta,
  que repiten algunas. Lo que cambia de un ADR aceptado va en uno nuevo ([índice de ADR](../decisions/README.md));
- un error nuevo se declara en Domain, con su código estable y sus textos en `Errors.resx` y `.en.resx`;
- los contratos de usuarios y roles los usa sólo el front: cambian en la misma tarea que el front, el OpenAPI y los ejemplos
  exportados, sin capas de compatibilidad. El PUT de ajustes, que nadie usa, se retira
  ([ADR 0005](../decisions/0005-sin-versionado-de-api-por-ahora.md));
- ninguna tarea agrega una pantalla: los avisos nuevos (N2, B2, N4 y N9) usan las piezas que ya existen, como el conflicto
  de `/configuracion`, y sus textos van con su par en inglés;
- una ruta que se agrega o se retira se refleja en `ExplicitRouteInventoryTests`;
- los logs van con `[LoggerMessage]`, sin códigos, tokens, correos ni teléfonos enteros.

Las etapas van en orden, y dentro de cada etapa las tareas también, salvo en la etapa 6, donde son independientes.

### Etapa 1: quién puede qué

1. **N1. Nadie da lo que no tiene** (decisión 2). Una regla en cuentas y en roles, evaluada dentro del límite con los
   permisos efectivos de quien pide:
   - nadie da ni quita un permiso, o un rol con permisos, que no tiene, ni en el alta ni en la edición;
   - nadie actúa sobre una cuenta con permisos que él no tiene: cambiarle el correo o el número (también desvincularlo,
     `DELETE /api/users/{id}/whatsapp`), desactivarla o borrarla;
   - sólo Admin edita los permisos de «User».

   `RoleService` pasa a saber quién pide, y cada rechazo es un error propio, traducido. Los permisos efectivos se leen de
   la base adentro del límite con `IRoleReader` (suma `ListRolesForUserAsync`: los roles de una cuenta con sus permisos, en una
   sola consulta, para compararlos por id), no del caché. Los tests de
   integración que llaman a los servicios sin sesión (`LastAdminLockTests` y el del servicio en
   `RoleRepositoryTransactionTests`) pasan a llamarlos como un administrador.
   [Administración](../features/administracion.md) suma la regla, y dejan de contradecirla el texto de `Errors.resx:62` (O8)
   y las descripciones de los dos permisos (`Permissions.resx:24,26`). Tests de integración con cuentas que tienen sólo cada permiso: no se tilda Admin, no le cambia
   el correo a un administrador y no le da todo a «User». Chico.
2. **N25. El administrador inicial no pierde Admin desde la pantalla** (decisión 9). La guarda rechaza quitarle Admin o
   cambiarle el correo a la cuenta de `Seed:AdminEmail`, con un mensaje que dice que hay que cambiar esa clave. Va después de
   N1 porque toca la misma guarda (`UserGuard`). Por la misma razón, darle a otra cuenta ese correo (en el alta o la
   edición) cuenta para N1 como darle Admin: el arranque se lo daría. La regla va en [administración](../features/administracion.md), y la del
   seed en [despliegue](../guides/despliegue.md) la nombra. Trivial.
3. **N4. Restaurar sin el acceso anterior** (decisión 4). Al restaurar se quita lo que el alta no trajo (correo, número y
   logins externos), y la respuesta dice que se restauró, para que la pantalla lo muestre en vez de «creado». Cambia
   `administracion.md:14`. Test: restaurar por número una cuenta que tenía correo y Google, y que su dueño anterior ya no
   entre por ninguno de los dos. Chico.
4. **N11. Agregar un correo pide el medio que ya existe.** Agregar un correo desde «Mi perfil» pide antes un código al número
   que ya existe, y desvincular el número, uno al correo; reemplazar el correo se rechaza también en el backend, como ya hace
   el front. Cambia la regla del perfil en [identidad](../features/identidad.md). Chico.
5. **N3. El bloqueo no se usa contra otros** (decisión 3). El bloqueo por códigos deja de frenar a Google; `AlreadyUsed` y
   `Expired` responden sólo al código que coincide; y `LockedOut` sale recién después de un código correcto, como ya hace
   `Disabled`. Cambian `identidad.md:29` y el comentario de `LoginCodeVerifier.cs:67` (O8). Tests: bloquear una cuenta con
   verificaciones sin código y que Google siga entrando; que un código cualquiera no revele si la cuenta existe; y dos
   canjes simultáneos del código correcto, con un 200 y una sola cookie. Chico.
6. **N20. El límite por IP agrupa IPv6 por /64**, con la normalización que ya usa WhatsApp (`WhatsAppCodeAddressLimiter`), en
   una pieza del núcleo que reusa N7. Trivial.
7. **N12. Cerrar sesión cierra las demás pestañas.** El front avisa el cierre a las otras pestañas con `BroadcastChannel`, y
   cada una revoca su propia autorización con su `id_token_hint` (el cierre del servidor revoca sólo la de quien cierra),
   deja sus tokens y vuelve al ingreso. Cambia el cierre de sesión en [identidad](../features/identidad.md). Chico.
8. **B4. Se retira el enlace de ingreso** (decisión 10): el emisor (`LoginLinkIssuer`), las dos rutas de canje con lo que el
   front tenga para ellas, sus tests y su regla (`identidad.md:43`). La tabla `LoginLinks` queda, y la vacía la retención de
   la tarea 21. Se va también `ISecureTokenGenerator`, que sólo usaba el enlace; quedan el lock `login-link:` por cuenta y la
   invalidación de los pendientes (`PhoneNumberLinker`, `AccountAccessRevoker`). Trivial.

**Hecha el 2026-11-15 y el 2026-11-16:**
- N1 y N25 en 6f6e8c2d. Los permisos de «User», que recibe toda cuenta, cuentan como propios de cualquiera que pide.
- N4 en 14d0d4b6 (front 493a72f4). Además, Google se vincula por correo sólo a una cuenta con el correo ya verificado
  (decidido el 2026-11-15): d7d7fa9e (front 10360f3d).
- N11 en 531d55c0 (front 5b72690c), con la decisión del 2026-11-15, «cada medio confirma»: agregar un correo con el número
  verificado pide también un código al número; vincular un número con el correo verificado, uno al correo; desvincular el
  número, uno al correo; y un correo o un número ya verificados no se reemplazan desde el perfil.
- N3 y N20 en 64b6ae9e.
- N12 en el front (3199dedb), y su regla en 860c6c04.
- B4 en 44c731c6 (front 32bc6a00). La invalidación de los enlaces pendientes queda sin efecto, porque ya no se emite
  ninguno; se puede ir con la tabla en la tarea 21.
- Las tareas 1 a 7 pasaron por la revisión adversarial; B4, que sólo retira código, no.

### Etapa 2: ediciones simultáneas y su historia

9. **N2, B2 y O4. Usuarios y roles con control de revisión.**
   - Una revisión `long`, como `ExpectedRevision` de la configuración, que avanzan sólo las ediciones de esos datos: el
     `ConcurrencyStamp` cambia con cualquier escritura de la cuenta y daría 409 falsos. Guardar bloquea la fila, compara y
     responde 409 traducido; el perfil usa el mismo control.
   - El borrado de un rol bloquea la fila (`FOR UPDATE`), para que choque con una asignación en vez de borrarla en cascada.
   - Activar y desactivar la misma cuenta a la vez no da 500 (O4).
   - El front conserva el borrador y ofrece releer, como en `/configuracion`, y el editor de roles avisa qué permisos
     descartaría (B2).
   - Se retira el PUT de `api/settings`, con su regla (`administracion.md:59-60`) y sus tests, y la receta de un área nueva
     (`agregar-un-area.md:153`) deja de mandar copiar un servicio sin revisión.

   Un control de arquitectura: los contratos de escritura de usuarios, roles y configuración llevan `ExpectedRevision`.
   Tests: dos ediciones del mismo usuario y del mismo rol, y borrar un rol mientras se asigna. Mediano.

   Backend hecho y revisado el 2026-11-16; el front va en un commit propio. Contra lo de arriba:
   la revisión nace en 1, no en 0, como la de la configuración, así un pedido sin ella choca (también en las filas que ya
   existen). La avanza el repositorio con cada cambio del nombre, el correo, el número, los roles, el estado o el borrado,
   venga de donde venga, y no al guardar lo mismo. Activar, desactivar y borrar bloquean la fila y la avanzan, pero no la
   piden (el estado va en la ruta); la verificación de un correo o un número desde el perfil la avanza y no la pide, porque
   la confirma un código. La asignación de un rol lo bloquea con `FOR KEY SHARE` antes de comprobar que exista, así choca con
   su borrado: `HasUsers` o `NotFound`. Las reglas quedaron en [administración](../features/administracion.md).

   La revisión adversarial sumó: la fila de la cuenta va con `FOR UPDATE` y no `FOR NO KEY UPDATE`, porque cambiar el
   correo o el número choca con el `FOR KEY SHARE` de un ingreso y se trababan; el ingreso con un código toma la misma
   fila antes de escribir la cuenta; el alta toma los roles después de los destinos y de la restauración, y
   `user-invitation:` al final queda como excepción escrita en [backend](../architecture/backend.md); el seed avanza la
   revisión de Admin cuando le agrega permisos y la de la cuenta de `Seed:AdminEmail` cuando le devuelve Admin; y los
   controles de rutas y contratos dejaron de esperar el PUT de ajustes. El idioma y la zona no avanzan la revisión, a
   propósito. Con `Users`, `Roles` y `Settings` juntos, la integración da 429 al pedir el código del administrador
   también sin este cambio: es del arnés (el límite de códigos del `ApiFactory`), y cada clase sola pasa.
10. **B3. Admin se puede editar después de quitar un módulo.** `RoleService` compara sólo los códigos del catálogo. Trivial.
11. **N13. Queda la historia de los cambios administrativos.** Una tabla de cambios (quién, sobre qué, permisos o roles
    agregados y quitados, estado antes y después), escrita en el mismo límite, para los roles (el borrado, con la lista de
    permisos que tenía), la asignación de roles, el estado, el borrado y el cambio de correo o de número de una cuenta, y el
    modo de registro; el seed queda con el actor vacío. Guarda qué dato cambió, sin el correo ni el número enteros, como los
    logs. No tiene pantalla: verla sería una pantalla nueva, que se dibuja antes. La regla va en
    [administración](../features/administracion.md). Chico a mediano.

### Etapa 3: el correo

12. **N5, N8 y O3. El correo no se pierde ni sale dos veces.**
    - Una falla antes de mandar el cuerpo no descarta: la fila vuelve a pendiente con una espera creciente, en la
      `AvailableAtUtc` de 42d8f47f, hasta que vence. Si vence sin salir, la invitación pasa a fallida; para eso el mensaje
      lleva su id. El worker no espera en memoria: un intento por reserva, y el reintento espera en la fila.
    - El envío tiene un plazo propio, menor que el apagado del host, y no usa el token de apagado más que antes del cuerpo.
      Lo que se cortó antes de mandar el cuerpo vuelve a la cola, como hace WhatsApp con `GiveBackCurrent`; lo que esperaba
      su reintento ya está en la cola.
    - Se reintentan sólo las fallas de conexión, de autenticación y las 4xx; un corte durante el envío queda incierto y no se
      repite.
    - Un chequeo `email` en Degraded, no Unhealthy, que marca enseguida un 535; contadores de enviados y descartados; y el id
      en el log, también cuando la cola llena no deja encolar un código (O3).
    - El front deja de tomar como enviada una invitación sin estado, y se corrige su comentario (`users.ts:30-31`, O8). Para
      eso el correo pasa a tener estado: una fuente del núcleo da `Pending` hasta que el servidor la acepta y `Sent` cuando
      la invitación guarda el Message-ID en `ProviderMessageId`, sin migración.

    Tests: apagar el worker durante su espera y con el SMTP caído, y una invitación que vence sin salir queda fallida.
    Cambian la cola de correo en [backend](../architecture/backend.md) y las invitaciones en
    [administración](../features/administracion.md). Mediano.

    Hecha el 2026-11-16, con lo que encontró su revisión adversarial: una invitación sin noticias deja de ser `Pending` cuando
    vence su fila (`QueuedDelivery.DefaultLifetime`) y pasa a `Uncertain`; el host frena los workers a la vez
    (`ServicesStopConcurrently`) y el envío (12 s) más anotarlo y completarlo (un token de 10 s) caben en los 25 s del apagado;
    el remitente rechazado (el cupo de Gmail) cuenta para la salud; el correo de un código vence en la cola con el
    `ResendCooldown`; las métricas salen de `IMeterFactory`; un 530 a MAIL FROM se reintenta; devolver a la cola distingue
    devuelto, vencido y no devuelto; y `Email:RetryDelaySeconds` va de 1 a 300.
13. **N7. Topes para los códigos y las invitaciones** (decisión 6). Se cuenta todo código que sale, no sólo los que van a
    destinos sin cuenta, que revelaría cuáles existen:
    - un tope diario por dirección agrupada por /64, con la pieza de la tarea 6 (`AddressPartition`);
    - un tope diario global por debajo del cupo de la cuenta de correo, con aviso al 80 %;
    - un tope por cuenta para los códigos del perfil y uno diario de invitaciones por administrador.

    Producción sigue con Gmail: la [guía de despliegue](../guides/despliegue.md) escribe el cupo de la cuenta que se use (unos
    500 correos por día en una cuenta gratuita), y el tope global se fija por debajo. Cambian los límites del ingreso en
    [identidad](../features/identidad.md). Chico.
14. **N14. El modo `.eml` sólo en Development y Testing.** Fuera de esos ambientes la Api no arranca con `PickupDirectory`,
    como ya pasa con el origen público, y cambia el test que hoy prueba que arranca así en Production. Trivial.

### Etapa 4: Redis, la salud y el arranque

15. **N9, N33 y O12. Con Redis caído, la app sigue** (decisión 5).
    - `/health` marca Redis en Degraded, como WhatsApp, y la copia sigue en el tráfico.
    - Los permisos se leen de la base con una sola consulta en el contexto del pedido, sin caché. Eso resuelve N33 (la
      segunda conexión dentro de una transacción y las preguntas repetidas) y deja sin uso `InvalidateRoleAsync` (O12).
    - La configuración se lee de la base si Redis no responde, y el seed no frena el arranque sin Redis: hoy invalida en
      Redis los permisos y la configuración al terminar (`DatabaseSeeder.cs:55-60`).
    - El front muestra un texto propio para un 5xx sin código, que cubre también los 502 y 504 del ingress.

    Un ADR nuevo enmienda el [ADR 0009](../decisions/0009-redis-como-unico-cache-de-datos.md), y el 0010 en lo que toca a la
    invalidación. Cambian lo que los cita: `azure-setup.md:197-199`, `identidad.md:37`, `permiso-nuevo.md:26` y
    `backend.md:125`. Test: Redis caído con la app abierta. Mediano.

    Programada el 2026-11-16 y commiteada después de su revisión adversarial, con el
    [ADR 0032](../decisions/0032-sin-redis-la-app-sigue.md). Lo que el plan no fijaba:
    - la consulta es `IPermissionReader.ListPermissionsForUserAsync` (el lector de permisos queda con ese solo método):
      un `SELECT DISTINCT` de los claims de permiso de los roles de la cuenta, con el `ApplicationDbContext` del pedido.
      `IRoleReader.ListRolesForUserAsync` no sirve para esto: cuenta las personas de cada rol en cada pedido (N32);
    - `PermissionService` es scoped y memoriza por persona en un diccionario: una consulta por persona y por pedido;
    - los permisos efectivos son, como antes, los de los roles que la cuenta tiene, filtrados por el catálogo. «User»
      cuenta sólo si la cuenta lo tiene, como hasta ahora: sumarlo a todos es la regla de N1 para dar permisos, no la de
      autorizar;
    - sin la fila de ajustes, la lectura desde la base da lo mismo que la fábrica: `InviteOnly` y la presentación por
      defecto;
    - `RedisCache.IsUnavailable` (una `RedisException` o un `TimeoutException`) decide cuándo se sigue sin Redis: los
      ajustes y el seed. Guardar los ajustes sin Redis sigue respondiendo 500 después de confirmar, como dice el ADR 0009;
    - con Redis colgado, el cliente cancela los comandos pendientes al soltar el socket: el adaptador convierte en
      `TimeoutException` toda cancelación que no pidió quien llama, en el llenado, el descarte y las leases, y la liberación
      de la lease del llenado sólo deja que venza. Sin eso, `/api/settings/presentation` daba 500 con Redis pausado;
    - la lectura de los ajustes sin Redis deja un Warning por proceso y por minuto (un timestamp con `Interlocked`), y las
      demás, Debug sin la excepción;
    - el chequeo de Redis es el de Aspire con `FailureStatus` en Degraded;
    - el front usa el texto propio cuando el 5xx no trae `traceId`, es decir, no lo armó la Api (que siempre manda `code` y
      `traceId`), con tres textos: lectura, escritura incierta y guardado con revisión.

    Lo que encontró la revisión adversarial, ya arreglado: el 500 con Redis colgado (la sonda con Redis pausado queda en
    `RedisFailureTests`), un Warning por lectura, dos `AGENTS.md` viejos (`Models/Identity` y `Seed`), el comentario de
    `deploy.yml`, el ingreso por WhatsApp sin Redis (el código no sale hasta que vuelve, porque espera la lease; lo dicen el
    ADR 0032 y la guía de Azure) y las enmiendas al 0009 y al 0010, que pasan a su sección `Enmiendas`.
16. **N6. `/health` no agota la base.**
    - El chequeo de la base usa un `SELECT 1` del pool: en Npgsql 10, el `CanConnectAsync` del chequeo de EF ya lo hace, y un
      test lo fija. El resultado de `/health` se guarda dos segundos, y una ráfaga comparte el cálculo.
    - `Maximum Pool Size` por réplica, sumados los workers, queda por debajo del límite del servidor (unas 50 conexiones en el
      B1ms) y se escribe en [Azure](../deploy/azure-setup.md).
    - Se suman un limitador global y uno de concurrencia, y un tope de cuerpo global, antes de la autenticación y con sus
      valores en `RequestLimits` ([despliegue](../guides/despliegue.md)).
    - `/health` sigue pública: el smoke del despliegue la consulta y no cambia.

    Chico.
17. **N17. El arranque frena la configuración de producción incompleta.** Valida `ForwardedHeaders` fuera de Development y
    Testing (el arnés prueba cada caso con su propia configuración, como el origen público) e interpreta `KnownProxies` y
    `KnownNetworks` con `ValidateOnStart`. `AllowedHosts` sigue en `*`, y la guía dice que el ingress ya filtra por
    host, para que los chequeos de Container Apps, que mandan la IP del pod, no reciban 400. El apagado de la Api queda en
    unos 25 segundos, contra 40 a 60 de la Container App. Cambian [despliegue](../guides/despliegue.md) y
    [Azure](../deploy/azure-setup.md). Trivial a chico.
18. **N19. `Caching:KeyPrefix` es obligatorio fuera de Development y Testing**, con `ValidateOnStart`; cambian la sección
    Redis y la tabla de [despliegue](../guides/despliegue.md), y lo que repite el prefijo derivado (`backend.md`,
    `agregar-cache.md` y el README). Trivial.
19. **N18. La resiliencia por defecto no reintenta un POST.** Se saca el handler de los valores por defecto de
    `ServiceDefaults`, porque ningún cliente lo usa tal cual; los 14 clientes que hoy lo quitan quedan igual. Un control:
    ningún cliente con nombre repite un POST ante un 503. ServiceDefaults deja también el paquete; cambian su `AGENTS.md`, el
    [mapa](../architecture/mapa-de-instrucciones.md) y [quitar WhatsApp](../guides/quitar-whatsapp.md). Trivial.
20. **N10. El código de error se puede rastrear.** Fuera de Development y Testing, la consola escribe JSON con scopes y el id
    del usuario va en un scope, también en el log del 500; la [guía de despliegue](../guides/despliegue.md) documenta el exportador (`OTEL_EXPORTER_OTLP_ENDPOINT`) y
    una alerta de 5xx. Completa lo que figura a medias en [verificación y producción](verificacion-y-produccion.md). Trivial
    a chico.

### Etapa 5: datos, archivos y retención

21. **N16 y N31. Los datos del ingreso se purgan** (decisión 8). Un worker de retención con lease, que borra por lotes,
    porque el núcleo no usa `ExecuteDelete`:
    - los códigos y los enlaces, a los 30 días, que cubren las 24 horas que leen los límites;
    - los `LoginAudits` quedan sin la IP a los 12 meses, sin perder el último ingreso de cada cuenta;
    - las autorizaciones y tokens de OpenIddict se podan a los 14 días de vencidos;
    - las invitaciones (`UserInvitations`), que la decisión no nombró, siguen el plazo más simple que no le quite datos a
      una pantalla.

    Los índices parciales de N31, `(Channel, SentAtUtc) WHERE SentAtUtc IS NOT NULL` y por `RequestedByUserId`, van en la
    misma migración. Un control: cada tabla con datos personales nombra quién la purga. La regla va en
    [identidad](../features/identidad.md), y al cerrar se quita el pendiente de retención de
    [verificación y producción](verificacion-y-produccion.md). Mediano.
22. **N28. La limpieza borra con el ETag del listado** (decisión 1), para no borrar una foto que se volvió a subir con la
    misma clave después del barrido. No alcanza para quien reaprovecha un archivo con `WriteOnceAsync` sin reescribirlo, como
    dice la revisión. Trivial.
23. **N15. Los archivos privados tienen borrado temporal.** La guía de [Azure](../deploy/azure-setup.md) crea el contenedor
    con borrado temporal (por ejemplo, 14 días), versionado y feed de cambios, y el ensayo de restauración de H15 restaura
    base y contenedor juntos ([verificación y producción](verificacion-y-produccion.md)). Chico.
24. **N30. El front deja de pedir los conteos que no muestra** en cada búsqueda de Usuarios (`UsersFilterBar.tsx`). Trivial.

### Etapa 6: lo trivial y la documentación

25. **B1. La plantilla viene apagada y sin datos de esta instalación.** La configuración versionada trae WhatsApp apagado y
    no trae el correo personal (remitente y administrador inicial) ni el id del cliente de Google. Antes, esos valores pasan
    a los user-secrets de esta PC, para que la instalación de acá siga igual, y el [README](../../README.md) nombra el token
    de WhatsApp entre los secretos. No se programa durante la prueba integral. Trivial.
26. **B5. Sin Formularios, el asistente de WhatsApp anda.** WhatsApp pide las capacidades `forms.*` sólo si están
    instaladas, con un test que compone WhatsApp e IA sin Formularios y arma el contexto del asistente, y
    [quitar Formularios](../guides/quitar-formularios.md) se corrige. Toca código de WhatsApp: se coordina con los arreglos de
    ese módulo si siguen en curso. Chico.
27. **N21. Los números, con la región de quien lee.** `DisplayNumbers` con la cultura del pedido en `RolesSystemQuerySource` y
    `UsersSystemQuerySource`, y `formatNumber` en `RolesPage`. Se confirma en la prueba del asistente, porque esos valores son
    también lo que lee el modelo. Trivial.
28. **N22. Textos neutrales en el núcleo.** Códigos del núcleo para «no hay almacenamiento» y «falta el objeto», con su lugar
    en el front; el de WhatsApp va a su módulo. Para las versiones apagadas alcanza con corregir
    `Interfaces/Channels/AGENTS.md:48,56`. Chico.
29. **N23. Volver a agregar un módulo pide revisar los roles.** Las guías para quitar
    [WhatsApp](../guides/quitar-whatsapp.md), [Formularios](../guides/quitar-formularios.md), [IA](../guides/quitar-ia.md) y
    [ARCA](../guides/quitar-arca.md) suman el paso: al volver a agregar el módulo, revisar los roles, porque los permisos que
    nadie quitó vuelven a valer. Es la opción proporcional de la revisión; el
    [ADR 0010](../decisions/0010-catalogo-de-permisos-extensible.md) no cambia. Trivial.
30. **N26. Los modelos neutros redactan su `ToString`.** Se redacta `StructuredRecordReadValue`, y un control mira los
    records de `Models/StructuredData` y `Models/Assistant` con propiedades de valor, con una lista de excepciones. Trivial.
31. **N27. Lo que falta de la regla de versiones** (decisión 7). Los demás enums de los modelos neutros que un módulo guarda
    por nombre leen un nombre desconocido como su valor neutro, como `StructuredDocumentProblem` desde 42d8f47f; los que no
    tienen uno dejan la fila para otra copia. Lleva el test de un nombre desconocido; la regla ya está en
    `Models/StructuredData/AGENTS.md`. Los enums propios de WhatsApp son lo que falta de H25, en el
    [plan de la bandeja](2026-10-01-bandeja-whatsapp.md). Chico.

**Cierre.** Cada regla que cambió queda en su fuente, y la revisión anota qué commit arregló cada hallazgo. Las decisiones
van a las fuentes de las [premisas](../architecture/premisas-de-la-plantilla.md), y el plan se retira con la
[auditoría documental](../guides/auditar-documentacion.md).
