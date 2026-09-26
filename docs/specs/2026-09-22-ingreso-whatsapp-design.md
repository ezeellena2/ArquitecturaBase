# Ingreso con WhatsApp — Diseño

> **Documento histórico para la estructura del código.** Sus reglas funcionales siguen vigentes mientras no las contradiga la [arquitectura canónica del backend](2026-09-24-backend-mvc-architecture.md) ni una decisión posterior: la regla de oro de las sesiones, el webhook, el bot, el envío a Meta, el ingreso, el perfil y la administración, los límites, la configuración, las rutas y los errores. Lo que describe o da por supuesto sobre la estructura del código (handlers, comandos con decoradores, `Application/Features`, `IEndpoint` o interfaces de repositorios en Domain), como que cada mensaje entrante corre como un comando de Application con sus decoradores (sección 7), quedó reemplazado por esa especificación: controllers MVC, servicios de Application con su interfaz y repositorios o lectores especializados.

**Estado: aprobado el 2026-09-22.** La estructura del código la fija la [arquitectura canónica del backend](2026-09-24-backend-mvc-architecture.md). En lo funcional, sigue el diseño inicial `docs/specs/2026-09-18-arquitectura-base-design.md` y el de la Fase 4: lo que no se diga acá, vale de ahí. Las pantallas y los mensajes aprobados son los tableros de la sección "Ingreso con WhatsApp" del canvas del sistema visual (`WA-*.dc.html`).

## 1. Objetivo

Que una persona pueda crear su cuenta y entrar a la web usando solo su WhatsApp, sin que un mensaje de WhatsApp alcance por sí solo para abrir una sesión.

Terminada cuando, con el número de prueba y la app corriendo en local:

1. alguien entra en `/login` con su número y el código que le llega por WhatsApp;
2. alguien le escribe al bot, toca "Entrar" y queda adentro;
3. un número nuevo crea su cuenta desde el chat (con registro abierto);
4. una cuenta con correo vincula su WhatsApp desde el perfil, y una cuenta con WhatsApp agrega su correo;
5. un admin da de alta a alguien por número y la invitación le llega.

## 2. Alcance

**Entra:** ingreso web con código por WhatsApp, ingreso desde el chat con enlace, alta desde el chat, cuentas sin correo, vincular y desvincular (la persona y el admin), invitaciones por correo y por WhatsApp, y el webhook con lo que necesita para no fallar.

**No entra:**
- **La bandeja del admin.** Es la segunda entrega. Desde esta se guardan los mensajes, así la bandeja arranca con historial.
- **El número de producción, el medio de pago y la publicación real.** Esta entrega se termina en local; producción es otro paso, con este mismo diseño.
- Multi-empresa.

## 3. Decisiones (2026-09-22)

Del usuario:

- Se puede tener cuenta **solo con WhatsApp**. El correo se agrega y se valida después, desde el perfil.
- En `/login` se elige **correo o WhatsApp**. Con WhatsApp, el código llega al WhatsApp del número.
- Se entra **desde la web o desde el chat**. Desde el chat, con un **enlace** de un solo uso.
- **El bot crea cuentas** con registro abierto. Con registro solo por invitación, el admin **invita por correo o por WhatsApp**.
- **Un número por cuenta y cada número en una sola cuenta.**
- **La persona y el admin** pueden desvincular y volver a vincular.

Recomendadas en la etapa 2 y aprobadas con los tableros:

- El número va **en la cuenta, igual que el correo**, y la conversación de WhatsApp en una tabla aparte de contactos (sección 6).
- La invitación **no lleva nada que sirva para entrar**: ni código ni enlace (sección 6.6).
- **Nadie se deja a sí mismo sin forma de entrar** (sección 12).

Esto cambia una decisión de la Fase 4 ("invitar es dar de alta el correo, sin correos de invitación"): ahora la invitación existe, pero sigue sin credenciales adentro, que era el motivo de fondo.

## 4. Cómo se prueba la identidad en cada camino

| Camino | Qué demuestra la persona | Quién lo garantiza |
|---|---|---|
| Código al correo | que lee ese correo | nuestro código (el de hoy) |
| Google | que Google verificó ese correo | Google |
| Código por WhatsApp (web) | que tiene abierto el WhatsApp de ese número **en el celular** | la plantilla de autenticación de Meta solo se entrega en el teléfono principal |
| Mensaje en el chat | que escribió desde ese WhatsApp | la firma de Meta en el webhook, hecha con el secreto de la app |
| Enlace del chat | que tiene ese chat (o que alguien se lo reenvió) | un token de 256 bits que dura 10 minutos y sirve una sola vez |

## 5. La regla de oro: un mensaje de WhatsApp nunca abre una sesión

- La cookie de sesión la crea `IIdentityService.SignInAsync`, y **en la respuesta del pedido que la pide** (hallazgo de la etapa 1). El webhook lo manda Meta, así que si la creara, la cookie le llegaría a Meta.
- **El webhook nunca llama a `SignInAsync` ni emite tokens.** Lo máximo que produce un mensaje es un **enlace de un solo uso, mandado al mismo chat**.
- El enlace **no abre la sesión al abrirse**: la abre el botón **Continuar** de `/ingresar`, con un `POST`. Así ni la vista previa de WhatsApp ni un antivirus que abra el enlace lo gastan.
- El token viaja en el **fragmento** de la URL (`/ingresar#t=…`), que el navegador no le manda al servidor: no queda en logs, en el historial del servidor ni en el `Referer`. Se guarda solo su hash.
- Después del canje, el SPA hace el OIDC de siempre: `signinRedirect` → `/connect/authorize` (encuentra la cookie) → `/auth/callback`.

El recorrido completo:

1. La persona escribe "Hola" al número.
2. Meta llama a `POST /webhooks/whatsapp` con el mensaje firmado.
3. La Api valida la firma, guarda el mensaje y responde `200`. No hace nada más en ese pedido.
4. El procesador en segundo plano encuentra la cuenta, emite un enlace (guarda el hash) y manda al chat el mensaje con el botón **Entrar**.
5. La persona toca **Entrar** y el navegador abre `https://<web>/ingresar#t=<token>`.
6. El SPA manda `POST /account/login-link/preview` y recibe el nombre y el número enmascarado. El enlace no se consume.
7. La persona toca **Continuar**: `POST /account/login-link/redeem` consume el enlace y crea la cookie.
8. El SPA hace `signinRedirect` y la persona llega al inicio.

## 6. Modelo de datos

### 6.1 La cuenta

- **El `UserName` pasa a ser el Id** de la cuenta (hoy es el correo). Una cuenta sin correo necesita un `UserName` único, y usar el correo o el teléfono haría que cambiar uno cambie el otro.
- **No se migran datos.** No hay producción: las migraciones cambian el esquema, y la base de desarrollo se borra y la Api la recrea al arrancar, con el seed. Nada de scripts que acomoden filas viejas.
- **El correo pasa a ser opcional.** `RequireUniqueEmail` pasa a `false`, porque Identity con `true` exige que haya correo. La unicidad la sigue dando el índice único que ya existe sobre `NormalizedEmail` (en Postgres, un índice único admite varios `NULL`).
- **El número va en `PhoneNumber`, en formato internacional**, con un índice único nuevo. `PhoneNumberConfirmed` dice si la persona ya demostró que es suyo.
  - Si lo carga un admin, queda **sin verificar** hasta que la persona entra con él, sea escribiendo al bot desde ese número o con un código.
  - Mientras tanto nadie más puede usarlo: el índice único ya lo reserva.
- **Toda cuenta tiene al menos un correo o un número.** Lo valida Application.
- Una cuenta borrada **conserva su número**, como hoy conserva su correo: si vuelve a escribir, el bot la trata como deshabilitada.
- `UserAccount` pasa a tener `Email` opcional y suma `PhoneNumber`, `PhoneNumberConfirmed` y `EmailConfirmed`.
- En los tokens, el claim `email` va solo si hay correo, y `name` pasa a ser `DisplayName ?? Email ?? teléfono`.
- Todo lo que hoy muestra el correo como identidad pasa a mostrar el teléfono cuando no hay correo: el listado, el detalle, `/api/me`, el menú del usuario y la auditoría.

### 6.2 Los números

- **Domain:** un value object `PhoneNumber` que solo acepta el formato internacional (`+` seguido de 8 a 15 dígitos). Domain no puede depender de paquetes.
- **Interpretar lo que escribe una persona** (país + número, con o sin 0, 15 o 9) se hace en Infrastructure con **`libphonenumber-csharp`** (9.0.39 en NuGet), detrás de `IPhoneNumberParser` en `Application/Abstractions`. Application tampoco puede depender de ese paquete.
- **Argentina:** los celulares se guardan **con el 9** (`+549…`), que es como llegan de WhatsApp. El payload real del 2026-09-22 trae `wa_id: 5493413654813`.
  - Mucha gente escribe el celular sin el 9 y a veces sin el 15. Como un número de WhatsApp es un celular, si el número argentino no trae el 9 y agregándolo es un celular válido, se le agrega.
  - Los casos van en tests.
- **Al mandar**, siempre `+` y código de país. Meta lo recomienda: sin el `+`, antepone el código de país del número del negocio.

### 6.3 Los códigos: `LoginCode` pasa a ser por destino

- `Email` pasa a ser `Destination`: el correo normalizado o el número en formato internacional.
- Se suman `Channel` (Email o WhatsApp) y `Purpose`: `SignIn` para entrar, `VerifyDestination` para vincular un número o agregar un correo desde el perfil. Con `VerifyDestination` se guarda también el `UserId` que lo pidió, para que el código solo sirva para esa cuenta.
- El hash pasa a ser HMAC de `destino:propósito:código`. Los códigos en vuelo (10 minutos) dejan de valer con el cambio, y no importa.
- El lock y los límites son **por destino**, compartidos entre propósitos: protegen a quien recibe los mensajes, sea cual sea el motivo.
- Una migración renombra la columna.

### 6.4 Los enlaces: `LoginLink` (nuevo)

- `Id`, `UserId`, `TokenHash`, `CreatedAtUtc`, `ExpiresAtUtc` (10 minutos), `ConsumedAtUtc` e `InvalidatedAtUtc`.
- El token son 32 bytes al azar en base64url, y se guarda su SHA-256. Con 256 bits no hace falta una clave: no se puede adivinar.
- Emitir uno nuevo invalida los anteriores de la misma cuenta, igual que los códigos.
- Un enlace vencido, usado, invalidado o inventado responde **el mismo error**, `Auth.LoginLink.Invalid`. "Deshabilitada" o "bloqueada" se dice recién después de un enlace válido.

### 6.5 Contactos y mensajes de WhatsApp (nuevo)

- **`WhatsAppContact`:** una fila por cada persona que le escribe al bot, tenga cuenta o no.
  - Campos: `WaId` (el número que manda WhatsApp, opcional), `UserIdentifier` (el BSUID, único), `ProfileName`, `UserId` (la cuenta, opcional y única) y `LastInboundAtUtc`.
  - El BSUID ya llega hoy: el payload del 2026-09-22 trae `user_id: "AR.1102953142229032"`. Cuando WhatsApp empiece a ocultar números, el contacto sigue apuntando a la cuenta por ese identificador.
  - Se busca primero por BSUID y después por `WaId`. Si la persona cambia de número, WhatsApp le da otro BSUID y aparece como un contacto nuevo.
- **`WhatsAppMessage`:** una fila por mensaje, entrante o saliente.
  - Campos: `ContactId`, `Direction`, `WaMessageId` (**único**: es la defensa contra los duplicados), `Kind`, `Body`, `Status` (para los salientes) y `OccurredAtUtc`/`ProcessedAtUtc`.
  - Los salientes con código o enlace se guardan **enmascarados**: "[enlace de ingreso]", "[código]". Si no, la futura bandeja le mostraría a un admin cómo entrar como otra persona.
  - No se guarda el payload crudo.
- **Retención:** propuesta de **90 días** para el texto de los mensajes, configurable, con una tarea que lo borra. Los contactos se conservan. Es un dato personal y lo tiene que decir la política de privacidad.

### 6.6 Invitaciones (nuevo)

- `UserInvitation`: `UserId`, `Channel`, `SentAtUtc`, `SentBy`, y para WhatsApp `ConsentConfirmedBy` y `ConsentConfirmedAtUtc`.
- **Por correo:** un correo nuevo con un enlace a `/login` (plantilla embebida, como la del código). La persona entra con su código de siempre.
- **Por WhatsApp:** la plantilla de utilidad con el botón de respuesta **Quiero entrar**. Al tocarlo, llega un mensaje con ese botón, se abre la ventana de 24 horas y el bot manda el enlace (sección 8).
- El consentimiento es obligatorio para invitar por WhatsApp, y queda guardado quién lo confirmó y cuándo.

### 6.7 Auditoría

- `LoginMethod` suma `WhatsAppCode` y `WhatsAppLink`.
- `LoginAudit.Email` pasa a ser `Identifier` (el correo o el número). Sigue sin guardar nunca un código ni un token.

## 7. El webhook

Una ruta nueva, `/webhooks/whatsapp`. Por la regla de `CLAUDE.md`, el prefijo `/webhooks` se suma a `BackendPrefixes`, a `SpaHostingTests` y al proxy de Vite.

**`GET` (verificación):** si `hub.mode` es `subscribe` y `hub.verify_token` coincide con el configurado (comparados en tiempo constante), responde `200` con `hub.challenge` en texto plano. Si no, `403`.

**`POST` (eventos):**

1. Lee el cuerpo **crudo**, hasta 5 MB (Meta agrupa hasta 1000 novedades).
2. Valida la firma: `X-Hub-Signature-256: sha256=<hex>` tiene que ser el HMAC-SHA256 del cuerpo con el **secreto de la app**, comparado en tiempo constante. Si falta o no coincide, responde `401` y registra el rechazo sin el cuerpo.
3. Ignora lo que no sea `whatsapp_business_account` o no venga del `phone_number_id` configurado.
4. Por cada mensaje: crea o actualiza el contacto y guarda el mensaje. Si el `WaMessageId` ya existe, no hace nada: es un reintento.
5. Por cada estado (enviado, entregado, leído, falló): actualiza el mensaje saliente, solo si el estado es más nuevo que el guardado.
6. Guarda y responde `200`. **No llama a Meta ni procesa nada** en este pedido.

**Procesamiento, en segundo plano** (`BackgroundService`):

- Se despierta con una señal en memoria y además revisa la tabla periódicamente, así nada se pierde si la app se reinicia.
- Toma los mensajes pendientes en orden, con `FOR UPDATE SKIP LOCKED` (sirve con varias instancias) y un lock por contacto.
- Cada mensaje corre como un comando de Application, con sus decoradores. Marcarlo procesado va en la misma transacción.
- **Los mensajes de hace más de 24 horas** (reintentos de Meta después de una caída) se marcan procesados sin responder: fuera de la ventana, Meta rechaza cualquier respuesta que no sea plantilla.
- Meta reintenta durante 7 días y puede mandar los eventos desordenados. Por eso todo lo anterior es idempotente y se ordena por `timestamp`.

## 8. El bot

**No tiene estado de conversación.** Decide con dos cosas: la cuenta del número y el botón que se tocó. Los botones llevan un identificador propio (`CREATE_ACCOUNT`, `HAVE_ACCOUNT`, `WANT_TO_ENTER`). Así no hay conversaciones "a medio camino" que vencer ni que limpiar.

| Situación | Respuesta (tablero Bot) | Qué cambia |
|---|---|---|
| Cuenta activa (por contacto vinculado o por número) | el mensaje con **Entrar** y el pie "Por ahora este chat solo sirve para entrar" | emite un enlace, vincula el contacto y, si el número estaba sin verificar, lo verifica |
| Cuenta deshabilitada, bloqueada o borrada | "Tu cuenta está deshabilitada. Contactá a un administrador." | nada |
| Sin cuenta, registro abierto | la pregunta con **Crear cuenta** y **Ya tengo cuenta** | nada |
| **Crear cuenta** | "Listo, Ana: creamos tu cuenta…" con **Entrar** | crea la cuenta con el número verificado y el nombre del perfil; si ya existía, se comporta como la primera fila |
| **Ya tengo cuenta** | "Entrá a la web con tu correo…" con **Ir a la web** | nada |
| Sin cuenta, solo por invitación | "Todavía no tenés acceso…" con **Ir a la web** | nada |
| **Quiero entrar** (invitación) | igual que la primera fila | igual que la primera fila |
| Pidió un enlace hace menos de un minuto | "Esperá un momento antes de pedir otro enlace…" | nada |
| Foto, audio, sticker o lo que sea | igual que un texto | igual que un texto |

Los textos salen de resources nuevos (`Bot.resx` y `Bot.en.resx`, con el control de paridad), en el idioma de la cuenta o en español si no hay cuenta.

## 9. Mandar mensajes a Meta

- Application dice **qué** mandar: el enlace, la pregunta, un código, una invitación. Infrastructure arma el JSON de Meta.
- Se manda **por una cola**, igual que los correos: la cola en memoria más un `BackgroundService`. Así ni el webhook ni los pedidos de la web esperan a Meta. Si la app se reinicia con algo en la cola, se pierde, igual que un correo: la persona pide otro.
- Un `HttpClient` tipado contra `https://graph.facebook.com/v25.0/{phone_number_id}/messages`, con el token como `Bearer`.
- La resiliencia estándar de ServiceDefaults **no reintenta los POST** de este cliente, para no duplicar mensajes. Reintenta nuestra cola, con espera. Además, Meta limita a un mensaje cada 6 segundos a la misma persona (error 131056).
- Al enviarse, se guarda el mensaje saliente (enmascarado si corresponde) con el `WaMessageId` que devuelve Meta, para cruzarlo con los estados.
- **Errores que se esperan y cómo se tratan:**

| Error | Qué significa | Qué se hace |
|---|---|---|
| 131030 | el destinatario no está en la lista (solo con el número de prueba) | se registra; ver sección 16 |
| 131047 | pasaron más de 24 horas | no se reintenta |
| 131026 | no se pudo entregar | se marca fallido |
| 131056 | demasiados mensajes seguidos a la misma persona | se reintenta más tarde |
| token inválido | el token dejó de valer | error en el log y en la salud de la app |

- **Nunca se registra** el cuerpo de un mensaje con código o enlace, ni el token.

## 10. Ingreso en la web con WhatsApp

- **`POST /account/login-code/whatsapp`** con `{ country, number }`: mismo contrato y mismas reglas que el pedido por correo. Siempre responde `202` con el mismo cuerpo.
  1. Interpreta el número. Si no se puede interpretar, `400 Users.Phone.Invalid`, igual que un correo mal escrito.
  2. Controla que el país esté permitido (configurable, por defecto solo Argentina).
  3. Aplica el lock, los límites y la invalidación de los códigos anteriores, y emite el código.
  4. **Manda la plantilla solo si** el número pertenece a una cuenta que no está borrada, o si el registro es abierto. Si no, guarda la fila igual y no manda nada (hallazgo 3 de la etapa 1: sin la fila, los límites servirían para averiguar qué números tienen cuenta).
- **`POST /account/login-code/verify`** acepta `phone` en lugar de `email`, exactamente uno de los dos. Si el código es de un número sin verificar, lo verifica.
- **Se corrige el hallazgo 1 de la etapa 1:** antes de crear una cuenta, el verify **mira el modo de registro** para los dos canales. Hoy crea la cuenta con cualquier código válido y el modo `InviteOnly` se sostiene solo porque no se manda el correo; con dos canales, esa defensa sola no alcanza.
- Un **tope diario** configurable de plantillas de autenticación, para acotar el costo si alguien abusa del formulario con números ajenos.
- **`GET /account/login-methods`** devuelve qué medios están activos (Google y WhatsApp). Sin la configuración de WhatsApp, la opción no aparece, igual que Google sin su `ClientId`.

## 11. Ingreso con el enlace: `/ingresar`

- Una ruta nueva del SPA, dentro de `AuthLayout` (tablero Enlace). Lee el token del fragmento y lo borra de la barra de direcciones.
- **`POST /account/login-link/preview`** con `{ token }` devuelve `{ displayName, maskedPhone }` o `400 Auth.LoginLink.Invalid`. No consume el enlace. Mostrarle el nombre a quien tiene el enlace no agrega riesgo: con el enlace ya podría entrar.
- **`POST /account/login-link/redeem`** con `{ token }` valida, consume, audita (`WhatsAppLink`) y llama a `SignInAsync`. Después el SPA hace `signinRedirect`.
- Los dos solo aceptan JSON del mismo origen, como el resto de `/account`, y tienen límite por IP.

## 12. Perfil y administración

**Perfil** (bearer, sin permiso: cada uno maneja lo suyo):

| Método | Ruta | Qué hace |
|---|---|---|
| POST | `/api/me/whatsapp/code` | pide el código para vincular un número (`VerifyDestination`) |
| PUT | `/api/me/whatsapp` | vincula el número con el código |
| DELETE | `/api/me/whatsapp` | desvincula el número |
| POST | `/api/me/email/code` | pide el código para agregar un correo |
| PUT | `/api/me/email` | agrega el correo con el código |

- "Este número ya está vinculado a otra cuenta" y "Ya existe una cuenta con ese correo" se dicen **después** de un código correcto, nunca antes.

**Administración** (`users.manage`):

- El alta acepta correo, número o los dos, y la invitación con su canal y su consentimiento.
- La edición permite cargar un correo o un número, que quedan sin verificar.
- `DELETE /api/users/{id}/whatsapp` desvincula el número y **cierra las sesiones** con `RevokeSessionsAsync`, igual que desactivar.
- `POST /api/users/{id}/invitation` reenvía la invitación.

**Reglas nuevas en `UserGuards`:**

- **Nadie desvincula su único medio de ingreso.** Otro medio es un correo verificado o un Google vinculado.
- El admin sí puede dejar a alguien sin medio de ingreso, porque es el caso del teléfono robado, pero la pantalla se lo advierte (tablero Usuarios).

Desvincular un número también desvincula su contacto de WhatsApp.

## 13. Límites

| Qué | Límite |
|---|---|
| Pedir un código por WhatsApp | los mismos del correo: 5 cada 15 minutos por número, reenvío cada 60 segundos y 20 cada 15 minutos por IP |
| Verificar un código | 5 intentos por código, 10 fallos seguidos bloquean la cuenta 15 minutos y 30 cada 15 minutos por IP |
| Enlaces desde el chat | uno por minuto por cuenta y 5 cada 15 minutos |
| Ver y canjear enlaces | por IP, con la misma política que verificar |
| Respuestas del bot | una cada 6 segundos a la misma persona (lo impone Meta); los mensajes que llegan en ráfaga reciben una sola respuesta |
| Plantillas de autenticación | tope diario configurable |
| Países de los códigos | configurable, por defecto Argentina |

## 14. Configuración y secretos

| Clave | Qué es | ¿Secreto? | Dónde vive en desarrollo |
|---|---|---|---|
| `WhatsApp:AccessToken` | el token del usuario del sistema | sí | user-secrets de la Api |
| `WhatsApp:AppSecret` | el secreto de la app, para la firma | sí | user-secrets de la Api |
| `WhatsApp:VerifyToken` | la palabra de verificación del webhook | sí | user-secrets de la Api |
| `WhatsApp:PhoneNumberId` | `1340198875839831` | no | `appsettings.Development.json` |
| `WhatsApp:BusinessAccountId` | `1658125822339116` | no | `appsettings.Development.json` |
| `WhatsApp:GraphApiVersion` | `v25.0` | no | `appsettings.json` |
| `WhatsApp:Templates:*` | los nombres de las dos plantillas; cada una existe en `es` y `en`, el mismo código que la cultura del perfil | no | `appsettings.json` |
| `WhatsApp:AllowedCountries`, `WhatsApp:DailyAuthCodeLimit` | los topes de la sección 13 | no | `appsettings.json` |

- La dirección del enlace sale de `Authentication:Issuer`, que ya existe.
- Si la sección `WhatsApp` está, se valida al arrancar (`ValidateOnStart`). Si no está, WhatsApp queda apagado y la app arranca igual.
- En producción, estos valores van en variables de entorno o en un almacén de secretos. Además conviene activar "Requerir secreto de la app" en Meta.
- El token del usuario del sistema no vence. Si se filtra, se revoca en Meta y se genera otro.

## 15. Lo que se reutiliza

| Pieza que ya existe | Uso |
|---|---|
| `LoginCode`, `LoginCodeHasher`, `LoginCodeGenerator`, el lock y los límites | el código por WhatsApp y los de vincular, con el cambio de la sección 6.3 |
| `VerifyLoginCode` y `SignInAsync` | el mismo ingreso, para el correo y el número |
| `ReturnUrls`, `/connect/authorize` y `CallbackPage` | sin cambios: el enlace termina en el OIDC de siempre |
| `SystemSettings` y el modo de registro | el bot, el alta y el verify lo consultan |
| `RevokeSessionsAsync` | la desvinculación desde el admin |
| `UserGuards` | las reglas nuevas de la sección 12 |
| `EmailQueue` y `EmailBackgroundService` | el mismo patrón para la cola de WhatsApp |
| `EmailTemplateRenderer` | el correo de invitación |
| Result, errores traducidos y ProblemDetails | todos los errores nuevos |
| Rate limiter, `ForwardedHeaders` y el lock de Postgres | los límites, el túnel y el orden por contacto |
| Front: `LoginPage`, `LoginCodePage`, `OtpInput`, `FormField`, diálogos | las pantallas de los tableros |

## 16. Probar en local

Los tres niveles de la etapa 4:

| Nivel | Hace falta | Se prueba |
|---|---|---|
| 1. Sin túnel y sin publicar | el token, la plantilla de autenticación aprobada y los celulares en la lista | el código por WhatsApp desde `/login`, de punta a punta |
| 2. Con túnel y sin publicar | lo anterior más el túnel | que Meta acepte la URL, y los webhooks de prueba del panel (firma, guardado, duplicados) |
| 3. Con túnel y la app publicada | lo anterior más la política de privacidad | todo el chat con los dos celulares |

- **El túnel:** **Dev Tunnels** de Microsoft con la [integración de Aspire](https://aspire.dev/integrations/devtools/dev-tunnels/) (`Aspire.Hosting.DevTunnels` 13.5.4, la misma versión que el resto de Aspire).
  - Da una dirección pública **HTTPS con certificado válido** (`https://<nombre>.<región>.devtunnels.ms`), que es lo que Meta exige. El certificado de desarrollo de `localhost` no le sirve a Meta: solo es de confianza en tu PC.
  - Con un `tunnelId` fijo, la dirección no cambia entre arranques, así que Meta se configura una sola vez. La dirección queda reservada 30 días aunque no se use.
  - El túnel reenvía tráfico mientras corre el AppHost: se levanta con `aspire run` y deja de reenviar con `aspire stop`.
  - Expone **solo el endpoint `https` de la Api**, con acceso anónimo en ese puerto, porque Meta no inicia sesión. Mientras está prendido, la Api queda en internet: se prende para probar y se apaga al terminar.
  - Necesita la CLI `devtunnel` instalada y un `devtunnel user login` una sola vez.
  - Si la integración no sirve, la alternativa es ngrok con su dominio fijo gratuito.
- **El botón "Entrar" se prueba desde WhatsApp Web en la PC.** El enlace apunta a `https://localhost:5173`, que el celular no puede abrir.
- **El 9 argentino:** el `wa_id` llega con el 9 (confirmado), pero la lista de destinatarios lo muestra sin el 9. En el nivel 1 se prueba si mandar a `+549…` pasa el control de la lista.
  - Si falla con 131030, se agrega una adaptación **solo para el número de prueba**, activada por configuración.
  - Otra opción a probar: responder usando el BSUID en lugar del número.
- **Los tests automáticos no dependen de Meta:**
  - la firma (con un HMAC calculado en el test);
  - los duplicados;
  - la tabla del bot (sección 8);
  - los casos argentinos de números;
  - el ciclo de vida del enlace y del código por destino.
  - En los de integración, `ApiFactory` reemplaza el cliente de Meta por uno falso que guarda lo enviado, igual que hoy `factory.EmailSender`.

## 17. Riesgos y temas abiertos

- **Números reciclados:** la compañía telefónica le puede dar a otra persona el número de alguien que lo dejó, y esa persona entraría por WhatsApp. En producción, Meta ofrece un **control de cambio de identidad** por número. Por ahora queda documentado.
- **Cambio de número:** WhatsApp avisa con un mensaje de sistema y el BSUID cambia. En esta entrega se registra; manejarlo queda para producción.
- **Nombres de usuario de WhatsApp:** el número puede dejar de llegar. Se cubre guardando el BSUID desde el primer día.
- **Plantillas:** Meta decide su categoría al aprobarlas. Si la invitación queda como "marketing", cuesta más.
- **Costo:** cada código por WhatsApp se cobra, con una tarifa por país. Los topes de la sección 13 lo acotan.

## 18. Endpoints nuevos o que cambian

| Método | Ruta | Acceso | Qué hace |
|---|---|---|---|
| GET | `/account/login-methods` | anónimo | qué medios de ingreso están activos |
| POST | `/account/login-code/whatsapp` | anónimo + rate limit | pide el código por WhatsApp |
| POST | `/account/login-code/verify` | anónimo + rate limit | ahora también con `phone` |
| POST | `/account/login-link/preview` | anónimo + rate limit | a qué cuenta lleva el enlace |
| POST | `/account/login-link/redeem` | anónimo + rate limit | canjea el enlace y abre la sesión |
| GET y POST | `/webhooks/whatsapp` | firma de Meta | verificación y eventos |
| POST, PUT, DELETE | `/api/me/whatsapp`, `/api/me/email` | bearer | vincular, agregar y desvincular |
| POST y PUT | `/api/users` | `users.manage` | correo y/o número, invitación |
| DELETE | `/api/users/{id}/whatsapp` | `users.manage` | desvincula y cierra las sesiones |
| POST | `/api/users/{id}/invitation` | `users.manage` | reenvía la invitación |

## 19. Errores nuevos

Códigos estables, con sus textos en `Errors.resx` y `Errors.en.resx` (los de los tableros):

| Código | Texto |
|---|---|
| `Auth.LoginLink.Invalid` | Este enlace ya no sirve. |
| `Users.Phone.Invalid` | Ingresá un número de celular válido. |
| `Users.Phone.AlreadyExists` | Ya existe una cuenta con ese número. |
| `Users.Identity.Required` | Cargá un correo o un número de WhatsApp. |
| `Users.Invitation.ConsentRequired` | Confirmá que la persona aceptó recibir mensajes por WhatsApp. |
| `Users.User.LastLoginMethod` | Es tu único medio de ingreso: agregá otro antes de desvincularlo. |
| `Auth.WhatsApp.CountryNotSupported` | Todavía no mandamos códigos a números de ese país. |
