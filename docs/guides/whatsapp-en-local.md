# WhatsApp en local

Cómo probar el ingreso y el bot de WhatsApp en tu máquina: la configuración, los secretos, el túnel para recibir los webhooks de Meta y las plantillas. Las reglas del área (qué hace el bot, qué se registra, la configuración completa con sus valores por defecto, que acá no se repite) están en [`docs/features/whatsapp.md`](../features/whatsapp.md); lo que pide WhatsApp en producción, en la [guía de despliegue](despliegue.md#configuración-obligatoria-en-production). Sin `WhatsApp:PhoneNumberId`, WhatsApp queda apagado y el resto del proyecto se levanta igual: nada de esto hace falta para `aspire run`.

El ingreso con WhatsApp usa la app de Meta `4601782356805744` y su número de prueba: se manda el código de ingreso, se reciben los mensajes que le escriben al bot y se responden con el enlace de entrada. Lo que no es secreto ya está en el repo; lo secreto va en los user-secrets de la Api, y nunca en el chat ni en un archivo versionado.

## MCP de Meta en Codex

El repositorio registra el servidor oficial [WhatsApp Business Tools MCP](https://developers.facebook.com/documentation/mcp/whatsapp-business-tools-mcp) en [`.codex/config.toml`](../../.codex/config.toml). Es una conexión remota para configurar y probar activos de Meta; no instala paquetes ni participa en la ejecución de la Api.

Para usarlo, abrí este proyecto como confiable en Codex, reiniciá Codex para cargar la configuración y, en **Settings → MCP servers → WhatsApp Business Tools**, elegí **Authenticate**. Iniciá sesión con una cuenta de Meta que administre el negocio y la app de WhatsApp correspondiente. Después, pedile al agente que liste los negocios disponibles antes de modificar números, plantillas o webhooks. La autorización OAuth queda en tu sesión local; el repositorio solo contiene la URL pública del servidor. Los secretos que necesita la Api siguen en user-secrets como se explica abajo. Meta habilita este MCP gradualmente: si aparece **Not yet available for your account**, la configuración queda preparada y hay que esperar a que Meta habilite esa cuenta.

## Qué va en `appsettings` y qué en user-secrets

La regla es la de siempre: **el secreto va en user-secrets, el resto en el repo**. Toda la configuración de WhatsApp cuelga de la sección `WhatsApp` y se lee **al arrancar**, así que después de cambiar cualquier valor —el token incluido— hay que reiniciar la Api.

| Clave | Dónde | Valor en local | Qué es |
|---|---|---|---|
| `WhatsApp:PhoneNumberId` | `appsettings.Development.json` | `1340198875839831` | **El interruptor.** Es el id del número de la Graph API, no el número. Sin él, WhatsApp queda apagado y la app arranca igual; con él y sin token, la Api **no** arranca |
| `WhatsApp:AccessToken` | user-secrets | — | el token del usuario del sistema |
| `WhatsApp:AppSecret` | user-secrets | — | con lo que Meta firma cada webhook |
| `WhatsApp:VerifyToken` | user-secrets | — | la palabra de verificación del webhook |
| `WhatsApp:DisplayPhoneNumber` | `appsettings.Development.json` | `15551632662` | el número del bot, solo dígitos, para el enlace "Volver a WhatsApp" |
| `WhatsApp:SendArgentineMobilesWithoutNine` | `appsettings.Development.json` | `true` | **solo para el número de prueba**: su lista de destinatarios guarda los celulares argentinos sin el 9 y rechaza `+549…` con el error 131030. En producción va apagada; el número se sigue guardando con el 9 |

El resto de las claves (la versión de la Graph API, los nombres de las plantillas, los países, el tope diario, la retención, la cola, los reintentos, el procesador y el rate limit del webhook) no hace falta tocarlas para probar en local: sus valores por defecto y para qué sirve cada una están en [`whatsapp.md`, "Configuración"](../features/whatsapp.md#configuración).

`appsettings.Development.json` trae además `WhatsApp:BusinessAccountId` (`1658125822339116`), que es el id de la cuenta de WhatsApp. Hoy **no lo lee nadie**: está anotado ahí porque es el dato que pide el panel de Meta y el que hay que cambiar al pasar al número real.

**`Authentication:Issuer` es obligatorio con el webhook prendido.** Es el origen público de la web (en local, `https://localhost:5173/`), y de él salen el enlace que el bot manda al chat y el botón del correo de invitación. Ya está en `appsettings.Development.json`; si falta, la Api no arranca. Fuera de Development y Testing es obligatorio siempre, y tiene que apuntar al origen del despliegue, no a `localhost`.

## Los tres secretos, y de dónde salen

| Clave | Qué es | De dónde sale |
|---|---|---|
| `WhatsApp:AccessToken` | el token del usuario del sistema, para mandar mensajes | [Configuración del negocio](https://business.facebook.com/latest/settings) › Usuarios del sistema, con `whatsapp_business_messaging` y `whatsapp_business_management` |
| `WhatsApp:AppSecret` | el secreto de la app, con el que Meta firma cada webhook | [Configuración › Básica](https://developers.facebook.com/apps/4601782356805744/settings/basic/) de la app |
| `WhatsApp:VerifyToken` | la palabra de verificación del webhook | la inventás vos, larga y al azar (solo letras y números, por ejemplo de un generador de contraseñas), y cargás la misma en Meta |

**El webhook se prende solo con `AppSecret` y `VerifyToken` juntos.** Sin ninguno, queda apagado: la Api arranca con un Warning que nombra las dos claves y el envío funciona igual. Con uno solo, la Api no arranca. Se leen al iniciar: después de cargarlos, reiniciá la Api.

Se cargan desde la raíz del repo, en PowerShell (sirve igual en Windows PowerShell 5.1 y en PowerShell 7). El valor se escribe sin que se vea. Con el SDK de .NET 10, `dotnet user-secrets set` solo nombra la clave al guardar, pero versiones viejas repetían también el valor, así que su salida va a `Out-Null` por las dudas. Eso se come la línea que confirma el guardado (los errores se siguen viendo): para confirmarlo está el comando que lista las claves, más abajo. No uses `-MaskInput`: en 5.1 no existe y el valor queda a la vista.

```powershell
$s = Read-Host "WhatsApp:AccessToken" -AsSecureString
dotnet user-secrets set "WhatsApp:AccessToken" (New-Object System.Net.NetworkCredential('', $s)).Password --project src/ArquitecturaBase.Api | Out-Null
Remove-Variable s
```

```powershell
$s = Read-Host "WhatsApp:AppSecret" -AsSecureString
dotnet user-secrets set "WhatsApp:AppSecret" (New-Object System.Net.NetworkCredential('', $s)).Password --project src/ArquitecturaBase.Api | Out-Null
Remove-Variable s
```

```powershell
$s = Read-Host "WhatsApp:VerifyToken" -AsSecureString
dotnet user-secrets set "WhatsApp:VerifyToken" (New-Object System.Net.NetworkCredential('', $s)).Password --project src/ArquitecturaBase.Api | Out-Null
Remove-Variable s
```

Para confirmar qué claves quedaron cargadas, sin mostrar los valores:

```powershell
(dotnet user-secrets list --project src/ArquitecturaBase.Api) -replace ' = .*', ''
```

## El túnel, para recibir los webhooks

Meta le pega al webhook desde internet y exige HTTPS con un certificado válido: el de desarrollo de `localhost` no le sirve. Por eso el AppHost puede levantar un [dev tunnel](https://aspire.dev/integrations/devtools/dev-tunnels/) de Microsoft. **Viene apagado**, así `aspire run` no le pide la CLI a quien no la usa.

1. **Una sola vez:** instalá la CLI con `winget install Microsoft.devtunnel`, abrí una terminal nueva (para que tome el `PATH`) e iniciá sesión con `devtunnel user login`. La sesión dura unos días: si el túnel no arranca, `devtunnel user show` dice si venció, y se renueva con el mismo `devtunnel user login`.
2. **Prendé el túnel** en los user-secrets del AppHost. Este valor no es secreto: va ahí para que cada uno lo prenda en su máquina sin tocar el repo.

   ```powershell
   dotnet user-secrets set "DevTunnel:Enabled" "true" --project src/ArquitecturaBase.AppHost
   ```

3. **`aspire run`.** En el dashboard aparece el recurso `tunnel` y, debajo, `tunnel-api-https`. La URL de este último es la dirección pública de la Api, del estilo `https://tunnel-xxxxxxxx-7180.brs.devtunnels.ms`. El enlace "Inspect" es el inspector del túnel y no se carga en Meta. En esta máquina la URL es siempre la misma, porque la región es fija y el id sale de la ruta del AppHost, y queda reservada 30 días aunque no se use. El id no está escrito en el repo porque forma parte de la dirección pública: es único entre todos los usuarios de Dev Tunnels y uno fijo sería fácil de adivinar. Si hace falta uno a mano (de 3 a 60 caracteres, minúsculas, números y guiones), va en `DevTunnel:TunnelId`, en los user-secrets del AppHost.
4. **En Meta**, en [Paso 2. Configuración de producción](https://developers.facebook.com/apps/4601782356805744/use_cases/customize/wa-configurations-v2/?use_case_enum=WHATSAPP_BUSINESS_MESSAGING) › Configurar webhooks:
   - la URL de devolución de llamada es `https://<la-url-del-túnel>/webhooks/whatsapp`;
   - la palabra de verificación es la misma de `WhatsApp:VerifyToken`;
   - tocá **Verificar y guardar** (Meta hace un GET y la Api le responde el `challenge`);
   - suscribí el campo `messages`.

   Como la URL no cambia, esto se hace una sola vez.
5. **Al terminar, `aspire stop`.** El túnel expone solo el endpoint `https` de la Api (ni el front, ni Postgres), con acceso anónimo en ese puerto porque Meta no inicia sesión. Pero mientras está prendido **la Api entera queda en internet**, no solo el webhook: se prende para probar y se apaga al terminar.

   Con la Api apagada, la dirección del túnel sigue reservada y el servidor de Dev Tunnels responde **`200` con el cuerpo vacío**: a la Api no llega nada, pero Meta da el evento por entregado y no lo reintenta. Lo que se le escriba al bot mientras la Api está apagada se pierde.

Si instalaste la CLI con Visual Studio o una terminal ya abiertos, reinicialos antes de `aspire run`: el AppHost busca `devtunnel` en el `PATH` que tenían al abrirse.

Para que `aspire run` deje de levantar el túnel: `dotnet user-secrets remove "DevTunnel:Enabled" --project src/ArquitecturaBase.AppHost`.

## Las plantillas de Meta

Un mensaje que abre una conversación tiene que salir de una plantilla aprobada. Hay dos, cada una en `es` y en `en` (el panel las muestra como "Spanish" y "English"), y se eligen por la cultura del perfil. Se administran en [Administrador de WhatsApp](https://business.facebook.com/wa/manage/home/) › Plantillas, y sus nombres son configurables (`WhatsApp:Templates:*`).

| Nombre | Categoría | Qué manda |
|---|---|---|
| `codigo_ingreso` | Autenticación | el código de 6 dígitos, con el aviso de seguridad, "Este código caduca en 10 minutos" y el botón "Copiar código". El código va **dos veces** en el JSON (cuerpo y botón `url`), como pide la doc de Meta |
| `invitacion_acceso` | **Marketing** | la invitación de un administrador, con el nombre de la persona y el del sistema, y el botón de respuesta rápida "Quiero entrar" |

**`invitacion_acceso` es Marketing a propósito.** Se intentó como Utilidad y Meta no la aceptó: Utilidad pide un mensaje que la persona haya pedido, y una invitación que manda un administrador no lo es. Dos consecuencias: cada invitación cuesta más, y Meta limita cuántos mensajes de marketing recibe cada persona, así que **una invitación puede no llegar**. El respaldo es reenviarla o invitar por correo.

La cuenta de prueba no necesita medio de pago para mandar plantillas. Al pasar a un número real se crea una cuenta de WhatsApp nueva, así que **las plantillas se cargan de nuevo ahí**.
