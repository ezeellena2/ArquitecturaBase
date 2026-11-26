# Configurar IA, cifrado y restauración

La [regla funcional](../features/inteligencia-artificial.md) fija alcance y contratos.
Esta guía no configura una sesión de Codex ni usa una suscripción de ChatGPT/Claude como
clave de API. Cada negocio provisiona su propia cuenta/clave con límites de gasto en el proveedor.

El buscador de Información del negocio usa una credencial separada de Google Places;
seguí [su configuración](configurar-google-places.md). No usa la clave ni el proveedor LLM.

## Preparación de una instalación

1. Aplicá el bundle de migraciones antes de publicar la API, según [despliegue](despliegue.md).
   `AiConnection` agrega `AiConnections`/`AiConnectionChanges`, y `AddAiBusinessInformation`,
   `AiBusinessInformation`/`AiBusinessInformationChanges`. `AddAiAssistant` agrega conversaciones,
   turnos, herramientas y comprobantes; no siembra credenciales ni hechos ficticios. `AddAiKeyRingControls` agrega
   `AiKeyRingControls`, vacía: la llena la primera copia que arranca.
2. Provisioná el anillo externo antes de arrancar. Cada réplica usa exactamente el mismo
   InstallationId, claves históricas y ActiveKeyId. Un derivado independiente usa ID y claves nuevos.
   La readiness lo comprueba (I17): la primera copia guarda en `AiKeyRingControls` un valor de control por clave, y una
   copia con otro InstallationId u otros bytes para el mismo keyId, o cuya clave activa no tiene control cuando ya hay otros
   (un anillo de otras claves), deja `/health` en Unhealthy (chequeo `ai-key-ring`, con un log de error que nombra el keyId)
   y no recibe tráfico. El anillo bueno es el que guardaste fuera de la base para esta instalación: el de las copias que
   ya andaban y leen la clave del proveedor guardada (la pantalla de IA no pide reemplazarla). Cargalo en la copia que no
   está lista y reiniciala: lee el anillo al arrancar. Sólo si el control lo escribió una copia con el anillo equivocado
   (por ejemplo, la primera de una base nueva), corregila, borrá esa fila (`DELETE FROM "AiKeyRingControls" WHERE "KeyId" = '<keyId>'`) y reiniciá las
   copias; lo que aquella cifró se lee sólo con su anillo. Nunca la borres para que pase una copia con otro anillo.
3. Ingresá con `ai.read` + `ai.manage` (Admin los recibe mediante el catálogo compuesto).
4. Elegí OpenAI o Anthropic/Claude, ingresá un modelo de texto de tu cuenta y la clave de API.
   Probá si querés: es una llamada facturable, acotada y sin datos del negocio. Guardá para
   confirmar. Disponible prepara la conexión; el motor opera sólo al aceptar una petición del canal
   con su contexto autorizado y un perfil técnico calificado.
5. Información del negocio tiene pantalla y permisos propios: ai.business.read + ai.business.manage.
   Admin también los recibe al arrancar. Guardar sus siete campos no llama al proveedor ni activa
   sugerencias; no ingreses claves ni datos personales de clientes en ese editor.

## Configuración técnica fuera de la base

| Clave .NET | Requisito/default |
| --- | --- |
| `ArtificialIntelligence:SecretProtection:InstallationId` | ID propio, no vacío, hasta 100 caracteres; conservarlo en una restauración |
| `ArtificialIntelligence:SecretProtection:ActiveKeyId` | identificador de una clave del anillo |
| `ArtificialIntelligence:SecretProtection:Keys:<keyId>` | 32 bytes aleatorios en base64; keyId alfanumérico/guion, 1–32 caracteres |
| `ArtificialIntelligence:Transport:TimeoutSeconds` | 15; rango 1–60 |
| `ArtificialIntelligence:Transport:MaxResponseBytes` | 65536; rango 1024–262144 |
| `ArtificialIntelligence:Transport:MaxConcurrentTests` | 2 por proceso; rango 1–8, sin cola |
| `ArtificialIntelligence:Transport:MaxOutputTokens` | 128; rango 64–1024 |
| `ArtificialIntelligence:RateLimiting:ReadPermitLimit` | 120/min por operador/réplica |
| `ArtificialIntelligence:RateLimiting:WritePermitLimit` | 20/min por operador/réplica |
| `ArtificialIntelligence:RateLimiting:TestPermitLimit` | 4/min por operador/réplica |
| `ArtificialIntelligence:RateLimiting:UploadConcurrencyLimit` | 4 subidas de Inicio a la vez por réplica (`file-turns`, audio y documentos), además de una por persona |
| `ArtificialIntelligence:RateLimiting:UploadQueueLimit` | 8 en espera por réplica; con la cola llena, 429 con `retryAfter` |
| `ArtificialIntelligence:RateLimiting:UploadBodySeconds` | 120 s para recibir el cuerpo de una subida con su lugar tomado; rango 1–3600 |

Los límites HTTP aceptan 1–100000 (la cola de subidas, 0–100000). No son contabilidad ni presupuesto distribuido. No hay
URL, timeout, tope ni prompt configurables por el administrador funcional. El startup
rechaza opciones inválidas, sin incluir sus valores sensibles en el diagnóstico.

Las subidas de Inicio son un dato del piloto (I9). Sólo ocupa lugar una subida con sesión, `ai.assistant.use` y su ventana de
envíos (`AssistantSubmitPermitLimit`) disponible: sin sesión, sin el permiso o con la ventana agotada, el pedido recibe su 401, 403
o 429 sin tomar lugar ni cola, y el turno JSON no cuenta. Cada persona tiene un solo lugar, sin cola: su segunda subida a la vez
recibe 429 enseguida. Con el lugar tomado, el cuerpo tiene `UploadBodySeconds` para llegar (120 s alcanzan para 10 MiB a unos
90 KB/s); vencido, el pedido se corta y el lugar se libera, como al terminar, con un error o cuando el cliente corta. La cola
espera a que se libere un lugar, sin plazo propio. Cada subida tiene su archivo, de hasta 10 MiB, una sola vez en memoria, más lo
que use el lector del dueño, que puede decodificar una foto de hasta 32 Mpx (unos 128 MiB): con 4 a la vez son unos 40 MiB de
archivos, y hasta unos 550 MiB si todas son fotos grandes. Calibrá `UploadConcurrencyLimit` con la memoria de la réplica: bajalo
si una réplica chica se queda sin memoria, y subilo en una con más o si el piloto ve 429 frecuentes con poca carga.

En **una instalación local nueva**, desde la raíz backend, PowerShell 7:

```powershell
$aiBytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
$aiSettings = @{
  'ArtificialIntelligence:SecretProtection:InstallationId' = [Guid]::NewGuid().ToString()
  'ArtificialIntelligence:SecretProtection:ActiveKeyId' = 'local-v1'
  'ArtificialIntelligence:SecretProtection:Keys:local-v1' = [Convert]::ToBase64String($aiBytes)
}
$aiSettings | ConvertTo-Json | dotnet user-secrets set --project src/ArquitecturaBase.Api
[Security.Cryptography.CryptographicOperations]::ZeroMemory($aiBytes)
Remove-Variable aiSettings, aiBytes
```

No ejecutar esa inicialización sobre una instalación con datos: cambiar ID o sobrescribir
una clave impide descifrar. La CLI fusiona los tres valores sin borrar otros user-secrets.
El anillo local de esta entrega se inicializó sin clave de proveedor y sin mostrar valores.
User-secrets es sólo para desarrollo; no lo copies a Git ni a la imagen. La clave del proveedor
se ingresa en el campo password del producto y queda cifrada en PostgreSQL.

En Production, provisioná en el gestor de secretos del host:

```text
ArtificialIntelligence__SecretProtection__InstallationId
ArtificialIntelligence__SecretProtection__ActiveKeyId
ArtificialIntelligence__SecretProtection__Keys__v1
```

Para Azure Container Apps, las claves se referencian como secrets de la app o Key Vault,
con acceso restringido. El workflow actual cambia la imagen y conserva la configuración
del host; no crea ni reemplaza el anillo. Provisionarlo es requisito previo al despliegue.
El bundle construye el host de diseño sin iniciar la API ni validar readiness y no necesita
clave de proveedor. No hace falta modificar la imagen para rotar secretos del host.

## Rotación

La clave de API y la de cifrado son cosas distintas. Para rotar la API, emití otra en el
proveedor, reemplazala/pruebala/guardala en la pantalla y revocá la anterior cuando confirmes
el resultado. Un timeout de prueba puede haber tenido costo; no se repite automáticamente.

Para rotar cifrado:

1. Generá otra clave aleatoria con **otro keyId**; nunca cambies los bytes de un keyId existente.
2. Agregala al anillo de todas las réplicas manteniendo activa la anterior; reinicialas y
   comprobá su arranque. Las opciones del anillo se validan/cargan al inicio, sin hot reload.
   La primera réplica que arranca con la clave nueva escribe su control; las demás tienen que coincidir (readiness).
   Antes del paso 3, al menos una réplica con este anillo tiene que haber llegado a la readiness: una que arranca con la
   nueva activa sin que nadie haya escrito su control no queda lista.
3. Cambiá ActiveKeyId en todas y reinicialas. Las escrituras nuevas y HMAC nuevos usan la nueva clave;
   lecturas y reintentos de comprobantes usan el keyId original.
4. Reemplazá la clave del proveedor en el editor si necesitás recifrar la fila con la activa.
   Un guardado `Keep` no recifra: preserva el ciphertext. No hay worker de recifrado.
5. Conservá las claves anteriores mientras las necesiten la fila, comprobantes de 30 días
   o **cualquier backup retenido**. No basta con esperar 30 días si la fila sigue con la antigua.
   Un reintento cuyo comprobante usó una clave que ya salió del anillo no se puede verificar: recibe el 409 de un pedido
   distinto, salvo en Inicio el audio y los documentos, que recuperan su operación si es el mismo archivo del mismo
   dueño. El control de esa clave queda en `AiKeyRingControls` y no se compara.

Los comprobantes de Información del negocio comparten este anillo, con otro propósito de HKDF.
La rotación conserva su keyId original y la ventana de 30 días. Los hechos no están cifrados con
el anillo: perderlo no impide leerlos, pero impide verificar reintentos históricos. Tampoco
regeneres el mismo keyId para ese caso; recuperá la clave original del respaldo.

## Backups, restauración y clones

Respaldá PostgreSQL y el anillo/ID por canales separados y con acceso controlado. Restaurar
requiere las claves originales, ID original y migraciones compatibles. Probá en ambiente
aislado que el ciphertext se descifra antes de habilitar tráfico; la comprobación criptográfica
no necesita llamar al proveedor. Al habilitar una cuenta restaurada verificá vigencia y permisos
de su clave de API, sin suponer que sigue activa. Si falta una clave histórica, conservá los datos
y recuperala del respaldo: no regeneres el mismo keyId. Reemplazar la API restablece la conexión,
pero no recupera comprobantes HMAC anteriores ni backups todavía cifrados con la clave perdida.

Si la clave de API guardada no se puede descifrar (I7), Inteligencia artificial › Conexión con
el proveedor dice «No pudimos leer la clave guardada: reemplazala» junto a la clave, con
**Reemplazar clave** resaltado (cargar modelos y probar responden 409
`Ai.Connection.CredentialUnavailable`), y el asistente, el audio y los documentos quedan no
disponibles (`notConfigured`, también para WhatsApp y Formularios): la disponibilidad prueba el
descifrado sin llamar al proveedor, así nadie gasta cupo en turnos que no pueden andar. Un turno
aceptado antes cierra como no disponible, sin el texto de reemplazar la clave. Reemplazar la clave
lo arregla.

Una conversación o un turno que no se puede descifrar (I8) se muestra «no disponible» en Inicio
(título genérico, sin contenido) y las demás se siguen viendo; cada pedido deja un solo aviso en el
log, con cuántas son y el primer id, y la persona la puede borrar desde Conversaciones. El worker
saltea un turno anterior ilegible como uno vencido, y una pregunta que heredaría un archivo ilegible
sigue sin él. Recuperar la clave del respaldo las vuelve a mostrar.

Un negocio nuevo clona código, no datos ni secretos. Base nueva, anillo/ID nuevos y credencial
de proveedor propia. Copiar un backup con sus secretos es una restauración de la instalación
original, no una instalación independiente. El núcleo puede funcionar sin IA: [retirada](quitar-ia.md).

Si dos copias se desarrollan en la misma computadora, **antes de inicializar secretos**
cambiá los UserSecretsId de Api y AppHost por IDs propios del derivado; conservar los de
la plantilla compartiría el mismo archivo de user-secrets. Asigná además volumen/base de
PostgreSQL, prefijo Redis (`Caching:KeyPrefix`), puertos y origen/redirect URIs propios.
No ejecutes migraciones ni quites secretos contra la configuración de otra copia. En
servidores independientes también separás base, prefix/origen y gestor de secretos. Esta
entrega documenta el requisito; no incluye un generador de derivados ni acredita una
segunda instalación desplegada.

## Proveedores y verificación real

Las claves de Anthropic con varios workspaces no están soportadas: necesitan `anthropic-workspace-id`, que el producto no envía. Usá una clave acotada a un proyecto o workspace.

En `/configuracion/ia`, elegí proveedor, ingresá su clave y usá **Cargar modelos**. Elegí
un identificador de la lista y ejecutá **Probar conexión**; guardar confirma la configuración
en un paso separado. Con una clave ya guardada se consulta el catálogo al entrar. Un modelo
listado todavía puede ser incompatible con la prueba de texto o requerir crédito. Si no podés
listar por permisos del proveedor, podés ingresar un identificador manualmente. Cambiar de
proveedor limpia la candidata y requiere una clave del nuevo destino.

Documentación oficial verificada durante esta entrega:

- [OpenAI Responses](https://developers.openai.com/api/docs/guides/migrate-to-responses):
  Bearer, `store:false` y respuestas de Messages/Responses distintas. `store:false` evita
  almacenar la respuesta para recuperación; no promete eliminación de todo registro del proveedor.
- [Modelo GPT-4.1 mini](https://developers.openai.com/api/docs/models/gpt-4.1-mini): ejemplo
  `gpt-4.1-mini`, sin razonamiento que pueda agotar el pequeño presupuesto de salida.
- [Anthropic Messages](https://platform.claude.com/docs/en/api/messages/create),
  [autenticación](https://platform.claude.com/docs/en/manage-claude/authentication) y
  [errores](https://platform.claude.com/docs/en/api/errors): API key y versión por request.
- [Claude Haiku 4.5](https://platform.claude.com/docs/en/models/haiku-4-5/migration-guide):
  ejemplo `claude-haiku-4-5-20251001`.

Son ejemplos documentados, no defaults obligatorios ni prueba de acceso de tu cuenta.
Los tests automáticos usan proveedores simulados. OpenAI tiene la calificación real acotada que
se registra [abajo](#evidencia-para-habilitar-perfiles); no acredita Anthropic, otra cuenta ni la
latencia de un despliegue. Anthropic sigue pendiente de una prueba real. Nunca agregues claves
reales a fixtures ni suites. La [revisión inicial](../reviews/2026-10-02-configuracion-llm.md)
conserva el alcance de las pruebas de conexión.

Un proveedor nuevo se guarda recién un despliegue después de la versión que ya lo lee (I4, la regla de
[despliegue](despliegue.md)). Si igual una copia encuentra guardado uno que no conoce, por ejemplo al volver a la imagen
anterior, nada responde 500: la pantalla de la conexión la muestra vacía y Pausada, con su revisión y un aviso, el
asistente queda no disponible (`paused`, o `notConfigured` si le falta la clave o el modelo), la clave guardada nunca va a otro proveedor (guardar sin tocarla la descarta, y la prueba y el catálogo piden una) y los
turnos sellados con ese proveedor quedan para la versión que lo conoce. Para seguir con esta versión, elegí un proveedor,
cargá su clave y guardá; si no, volvé a la versión que lo conoce.

## Habilitar el asistente

Aplicá la cadena de migraciones vigente mediante el bundle antes de activar esta versión;
`AddAiAssistant` es su base histórica, no el último cambio de canales y audio. En Inicio,
concedé `ai.assistant.use`; las fuentes exigen además users.read, roles.read o ai.business.read
según su contenido. Los otros canales declaran sus permisos propios de Actor y Subject y
revalidan cuentas activas: ni heredan permisos entre personas ni exigen Inicio por analogía.
Admin recibe los permisos del catálogo al seed, sin habilitar una credencial. El permiso
ai.read sólo habilita el enlace a configuración; no es necesario para consultar.

No hay perfiles habilitados por defecto. Calificá el modelo de la conexión guardada con el motor
real: las cinco fuentes y la terminal de Inicio, las capacidades y propuestas de los canales,
argumentos, continuación, límites, contexto y latencia. Un alias, Cargar modelos o Probar
conexión no acreditan ese comportamiento. Sin coincidencia exacta, la disponibilidad de texto
informa `modelNotQualified` y no acepta generación.

Después de la evidencia, un perfil de texto se declara bajo
`ArtificialIntelligence:Assistant:ModelProfiles:<índice>`:

| Campo | Criterio |
| --- | --- |
| `Provider` / `Model` | OpenAI o Anthropic y modelo exacto de la conexión guardada. |
| `Id` / `Version` | Identidad explícita y versión positiva de la configuración calificada. |
| `ContextBytes` | Debe cubrir el presupuesto de contexto configurado en el motor. |
| `SupportsTools` / `SupportsTerminalAnswer` | Ambos true y comprobados con llamadas reales. |
| `QualifiedToDraft` | True sólo después de calificar el redactado con los casos del [ADR 0021](../decisions/0021-respuestas-redactadas-por-el-asistente.md) (punto 8). Sin él, el asistente de WhatsApp no redacta: responde con los textos fijos de sus casos. |

La aceptación fija perfil e instrucciones por Id/versión. Inicio usa `home-system@2` (los turnos
sellados con la 1 se siguen leyendo); los canales usan `capabilities-v1@1`. El worker exige la versión capturada y no cambia una operación
pendiente al perfil nuevo. Para modificar un perfil o instrucciones, calificá y declará otra
versión; no reescribas la versión que conservan turnos anteriores. Reiniciá las réplicas después
de configurar: los perfiles no se recargan en caliente y la revisión de conexión protege cada paso.

El contexto ofrece únicamente capacidades aplicables: las sugerencias de continuación requieren
una captura fijada por el servidor. El transporte admite hasta dieciséis definiciones entre capacidades
y terminal. Es una cota de catálogo, independiente de las seis invocaciones y
cinco pasos máximos de ejecución por turno (tres lecturas, la propuesta y el cierre de «Armar formulario con la IA»). No aumentes cuotas, bytes ni llamadas para
hacer pasar una calificación.

| Clave bajo `ArtificialIntelligence:Assistant` | Valor inicial, aún propuesto para piloto |
| --- | --- |
| `Limits:ActorDailyQuota` / `InstallationDailyQuota` | 10 por actor, canal y propósito / 100 para toda la instalación, por día del negocio (se renuevan a la medianoche de la zona horaria de Configuración) |
| `Limits:DocumentActorDailyQuota` / `DocumentInstallationDailyQuota` | 10 lecturas de documentos por persona en todos los canales / 50 para toda la instalación, por día del negocio y aparte de las anteriores |
| `Limits:DocumentOutputTokens` / `MaximumReadingCharacters` | 8192 / 20000, los techos del código: sólo se pueden bajar |
| `Limits:PendingCapacity` / `RunningCapacity` | 4 / 4 para toda la instalación, entre réplicas. El tope cuenta cuántos esperan a la vez, no el tamaño de una tanda: cuando los carriles toman cuatro, la espera se vuelve a llenar con los reenvíos. Para que todo lo aceptado empiece antes de vencer, vale `RunningCapacity × floor(PendingSeconds / duración del turno largo)`: con turnos de 60 a 80 s, como «Armar formulario con la IA», 4 × floor(120 / 80) = 4. Con más, una tanda acepta lo que va a vencer sin intentarse: eso devuelve la cuota, pero la persona espera dos minutos para leer «venció», y es mejor un 429 inmediato con su espera. Si el piloto mide turnos de menos de 60 s, se puede subir a 8 (4 × 2); el código admite hasta 50. El tope es de toda la instalación y lo comparten Inicio, WhatsApp, Formularios, el audio y las lecturas de documentos: una tanda de un canal puede dejar a Inicio en `capacityLimited`, y conviene medirlo en el piloto |
| `Limits:RetentionDays` | 30 desde cada turno. Es también el techo de los días de Inicio que el administrador elige en Configuración › IA › «Privacidad» (RC1-A): lo de Inicio dura esos días y acortarlos vence lo ya guardado en la purga siguiente; los demás canales siguen con este valor |
| `Limits:QuestionCharacters` / `HistoryTurns` / `HistoryCharacters` | 4000 / 12 / 24000 |
| `Limits:ContextBytes` / `ContinuationBytes` | 131072 / 131072 |
| `Limits:ProviderSteps` / `ToolCalls` | 5 / 6 |
| `Limits:ToolResultBytes` / `ToolAggregateBytes` | 16384 / 65536 |
| `Limits:OutputTokens` / `ProviderResponseBytes` | 4096 / 262144 |
| `Limits:ToolTimeoutSeconds` / `ProviderTimeoutSeconds` | 5 / 30 |
| `Limits:RunningSeconds` / `PendingSeconds` / `LeaseGraceSeconds` | 90 / 120 / 15 |
| `Limits:MaintenanceBatchSize` / `PollingAfterSeconds` | 100 / 2 |
| `WorkerPollingSeconds` / `MaintenanceIntervalMinutes` | 1 / 60 |
| `CompletionPersistenceSeconds` | 5; sólo repite persistencia, nunca HTTP |
| `ProcessInBackground` | true; false permite arneses controlados sin worker automático |

La cuota del actor se separa entre Inicio, cada canal y la interpretación o normalización de audio; transcribir no consume el cupo textual. El recibo fija canal y propósito, sin FK al contenido: borrarlo no libera cuota. Sólo la devuelve lo que nunca se intentó: un turno que venció sin haber reservado un paso deja de contar, y su recibo sigue deduplicando el UUID (I1). El backfill usa sólo evidencia retenida; una dimensión histórica desconocida cuenta en cada grupo compatible con lo conocido. El cupo global y las capacidades pendientes/en ejecución siguen siendo compartidos.

Startup valida valores positivos, cotas y consistencia de cuotas/capacidades/perfiles. No se
pueden ampliar las cotas de protección del código sin revisar implementación y pruebas. El rate
limit HTTP `ArtificialIntelligence:RateLimiting:AssistantSubmitPermitLimit` parte de 6/min por
operador/réplica; es distinto de la cuota distribuida. Ninguno calcula gasto: configurá límites
y alertas en la cuenta del proveedor. Retención/cuotas y habilitación de modelos requieren la
calibración del piloto; las pruebas con handlers falsos no los convierten en premisas operativas.

### Máximo diario en tokens

El motor no conoce precios ni corta por monto: el gasto se limita en la cuenta del proveedor. Lo que sí está acotado es
cuánto le puede pedir la instalación en un día, por las cuotas y los techos de cada turno. Con los valores por
defecto:

| Qué | Cuenta | Máximo por día |
| --- | --- | --- |
| Llamadas de texto | `InstallationDailyQuota` × `ProviderSteps` = 100 × 5 | 500 llamadas |
| Entrada de texto | 500 × `ContextBytes` (131.072 bytes, el cuerpo entero de cada llamada: instrucciones, herramientas, historia, archivo y resultados) | 65.536.000 bytes: hasta 65,5 millones de tokens, unos 22 millones a 3 bytes por token |
| Salida de texto | 500 × `OutputTokens` (4.096 por llamada, razonamiento incluido) | 2.048.000 tokens |
| Lecturas de documentos | `DocumentInstallationDailyQuota` × páginas = 50 × 10 (una llamada por lectura) | 500 páginas de entrada |
| Salida de las lecturas | 50 × `DocumentOutputTokens` (8.192) | 409.600 tokens |
| Audios | ocupan lugares de las 100 de `InstallationDailyQuota`: una llamada por audio, de hasta 1.000.000 bytes en Inicio y en WhatsApp | se cobran por duración |

- **Texto:** la cuota de la instalación cuenta juntos los turnos de Inicio y de los canales (también las lecturas para
  otro formulario y las relecturas de Formularios) y los audios, así que el peor caso en tokens es que las 100 sean de
  texto, con los cinco pasos y el cuerpo lleno. Un token ocupa al menos un byte: 65,5 millones es el techo de la
  entrada, y unos 22 millones, una estimación con el español y el JSON del pedido. La cuota por persona no sube ese
  techo, y tampoco lo que vence sin intentarse: devuelve su lugar (I1), pero nunca llamó al proveedor.
- **Documentos:** la entrada de una lectura depende del modelo de su perfil, que cuenta cada página como una imagen en
  alta resolución (y un PDF suma su texto): multiplicá las 500 páginas por lo que su proveedor cobra una página.
- **Total:** hasta 65,5 millones de tokens de entrada de texto (unos 22 millones), 500 páginas de documentos y
  2.457.600 tokens de salida (2.048.000 + 409.600), más la duración de los audios.
- **Fuera de las cuotas** quedan sólo la prueba de conexión, un pedido breve que hace a mano quien administra
  (`TestPermitLimit`, 4 por minuto y persona), y el catálogo de modelos, que no genera.
- **Con otros valores**, la cuenta es la misma: `InstallationDailyQuota × ProviderSteps × ContextBytes` de entrada y
  `InstallationDailyQuota × ProviderSteps × OutputTokens` de salida, más `DocumentInstallationDailyQuota × 10` páginas y
  `DocumentInstallationDailyQuota × DocumentOutputTokens` de salida. Las cuotas no tienen techo en la validación:
  subirlas sube el máximo en la misma proporción.

Con ese máximo y el precio del modelo elegido, configurá en la cuenta del proveedor (OpenAI o Anthropic) una alerta de
gasto y, si la cuenta lo permite, un límite mensual: es el único freno por monto.

### Perfil de transcripción con la misma conexión

Normalizar audio usa `NormalizeAudio` en el mismo motor, recibo, worker, cuota y capacidad global.
La disponibilidad textual no bloquea STT: se valida su propio perfil al aceptar el audio.
No necesita un perfil de texto calificado ni soporte de herramientas. Sí necesita la conexión
OpenAI guardada, Disponible y con su clave vigente, y un perfil STT calificado bajo
`ArtificialIntelligence:Assistant:AudioModelProfiles:<índice>`:

| Campo | Criterio |
| --- | --- |
| `Provider` / `Model` | OpenAI y el modelo STT exacto que se probó. No modifica el modelo de texto guardado. |
| `Id` / `Version` | Identidad explícita y versión positiva que se fijan al aceptar la operación. |
| `ContentTypes` | Formatos realmente calificados entre `audio/ogg`, `audio/wav`, `audio/webm` y `audio/mp4` (los dos últimos son lo que graba el navegador en Inicio). |
| `MaximumAudioBytes` | Límite calificado de hasta 25.000.000 bytes; puede ser menor. |

El micrófono de Inicio aparece sólo si un perfil califica `audio/webm` o `audio/mp4` con `MaximumAudioBytes` de al
menos 1.000.000 y hay almacenamiento privado. Un perfil para WebM o MP4 se califica con el arnés antes de declararlo,
como el de Ogg: el formato no se hereda de otro perfil.

El motor usa la misma conexión, credencial cifrada y revisión. No cargues otra API key ni cambies
`AiConnection.Model` para transcribir. Las instrucciones capturadas son `audio-none-v1@1`,
sin prompt, historia, capacidades ni terminal textual. Se conserva la transcripción completa
sin traducción ni idioma forzado; texto excesivo o audio que no cumple el descriptor se rechazan.
La posterior interpretación es otra operación del canal, con su autorización actual. En Atención
manual normalizar requiere lectura, mientras interpretar para ofrecer exige además envío;
normalizar no concede ese permiso ni `ai.assistant.use`.

El arnés mantiene candidatos aislados para calificar; no habilita perfiles de la aplicación.
Los modelos, perfiles e instrucciones que esta instalación calificó y declaró se registran
[en la evidencia](#evidencia-para-habilitar-perfiles). Otra identidad, versión, formato o
límite requiere su propia calificación.

### Retención y recuperación

La clave histórica del anillo protege contenido del asistente además de credenciales y huellas.
Conservá keyId/bytes/InstallationId mientras haya contenido, comprobantes o backups dependientes.
Eliminar una conversación retira el acceso inmediatamente y pide cancelación; la purga física
se ejecuta después en lotes, y los comprobantes sin texto impiden eludir cuotas o repetir UUID.
Después de vencer todos los turnos y cerrar los activos, la conversación de Inicio retirada se
elimina físicamente con sus turnos/herramientas; la de un canal, retirada o no, y la de Inicio que
nadie retiró, también, pero sólo cuando además vencieron sus comprobantes. Una cuenta borrada
retira las suyas aunque ya no tengan contenido, y siguen la misma regla (I3). La purga
usa un presupuesto total por lote y scopes nuevos; conserva los comprobantes independientes
mientras siga su ventana.
No borres filas de operación para recuperar una generación incierta.

## Runbook del asistente

- Una operación Pending espera un claim; si excede el plazo, el reconciliador la vuelve Expired.
  GET y recarga sólo leen estado. Para recuperar una aceptación incierta se conserva el mismo
  UUID y cuerpo; un 404 no demuestra rollback. Una operación terminal sólo admite un intento
  nuevo mediante la acción explícita del canal y otro UUID.
- Una Running vencida con paso reservado pasa a Uncertain. Revisá estado del proveedor por sus
  herramientas operativas, sin reenviar el pedido desde la base. No hay recuperación automática
  de continuación externa tras reiniciar el proceso.
- Si la respuesta existe pero falla su commit, se reintenta sólo la persistencia acotada con
  fence original. Una vez vencido, reconciliación cierra la operación; no publique manualmente
  ciphertext ni resultados de otro fence.
- Para cancelar en Inicio, usá el endpoint propio o quitá la conversación. Durante HTTP el worker observa
  el pedido durable y cancela el transporte; eso no garantiza que el proveedor no haya cobrado.
- Para purga atrasada, revisá `ai.assistant.content.purge.overdue`, disponibilidad de PostgreSQL,
  `ai.assistant.maintenance.failures` y ejecución del mantenimiento. Con acceso ya retirado,
  corregí el mantenimiento antes de reducir límites o eliminar metadata.
- Observá Pending/Running, edad pendiente, resultados, pasos, duración, fallas de persistencia
  y `ai.assistant.usage.unknown`. `ai.assistant.accepted.today` cuenta comprobantes confirmados
  sin duplicados; duración por herramienta y tokens informados tienen métricas propias.
  Los meters `ArquitecturaBase.AiAssistant` y
  `ArquitecturaBase.AiAssistant.Maintenance` están registrados con OpenTelemetry. Las métricas
  no incluyen contenido/UUID/cuenta como etiquetas; no acreditan exportación a un host desplegado.
- Una consulta fallida por el proveedor dice qué revisar, y el log del coordinador repite su código con el turno, el proveedor y
  el estado HTTP, sin el cuerpo: `Ai.Assistant.ProviderCredentialRejected` (401 o 403), la clave de la conexión, que se cambia
  con **Reemplazar clave** en Inteligencia artificial › Conexión con el proveedor (`/configuracion/ia`); `ProviderLimited` (402
  o 429), el crédito y los límites de la cuenta del proveedor; `ProviderModelRejected` (400 o 404), el modelo configurado para
  esa tarea, que puede haberse retirado, o el archivo o el pedido (un audio o un PDF que el proveedor no puede leer):
  en texto, el modelo es el de la conexión; en audio y documentos, el del perfil (`AudioModelProfiles` o
  `DocumentModelProfiles`); `ProviderUnavailable`, que nada salió (DNS, conexión rechazada, TLS o el plazo de conexión de
  10 s): la red del servidor hacia el proveedor. **Probar conexión** usa el modelo de texto de la conexión: confirma la clave,
  el cupo y la red sin tocar el turno, pero no comprueba el modelo de audio ni el de documentos, ni el archivo.
- `System.Query.Unavailable` (una fuente cuya base falló) y `Ai.Assistant.ExecutionFailed` (una excepción del motor fuera de la
  llamada al proveedor) no son del proveedor: revisá PostgreSQL y el log, que nombra la capacidad, la versión, el turno y el
  SQLSTATE. En Error, la falla no es transitoria (por ejemplo 42703, una columna que falta después de un despliegue) y se repite en
  cada turno hasta corregirla; en Warning, es transitoria.
- Si faltan claves históricas, recuperalas de su respaldo. Cambiar la conexión o recrear un keyId
  no recupera preguntas ni comprobantes antiguos y no justifica regenerarlos.
- En audio, conservá la referencia ya fijada: una pérdida de acceso, vencimiento o incertidumbre
  no autoriza descargar/transcribir otra vez ni reemplazar su UUID. La lectura del estado no
  ejecuta el efecto. El archivo original sigue la retención de su módulo, y el contenido IA usa
  el menor vencimiento entre esa fuente y la retención del motor. Segundos o tokens ausentes
  siguen desconocidos; el uso válido de una respuesta tardía se conserva sin publicar su texto.

[Evidencia local y piloto pendiente](../reviews/2026-10-04-inicio-asistente.md).

## Evidencia para habilitar perfiles

La implementación y los dobles no califican un modelo. El `2026-10-18`, la corrida
`stage5_20261017_05` sobre `fa27e9e6` completó 48/48 casos ficticios
con OpenAI por el motor real, sin ampliar sus presupuestos: Inicio 20/20, canales 24/24 y
audios es/en 4/4. El host aislado leyó la conexión guardada, conservó sus cuotas y usó el mismo
recibo, worker, claim y fence de producción. No envió mensajes de Meta.

| Perfil declarado | Identidad exacta y cotas configuradas |
| --- | --- |
| Texto | `gpt-6.1-sol`; `gpt-6.1-sol-candidate-1@1`; cota de contexto `131072`; herramientas y terminal; instrucciones `home-system@1 y capabilities-v1@1`. |
| Audio | `gpt-transcribe`; `openai-stt-candidate@1`; `audio/ogg y audio/wav`; cota por audio `25000000`; instrucciones `audio-none-v1@1`; misma conexión y clave. |

La corrida midió máximos de 15.843 bytes de contexto, 9.781 de continuación, cuatro pasos, tres invocaciones y 4.755 bytes de herramientas. Los cuatro audios sumaron 30 segundos informados y el mayor archivo tuvo 312.416 bytes. Las cotas configuradas de 131.072 bytes de contexto y 25.000.000 bytes por audio no se ensayaron en su extremo; esta evidencia no acredita capacidad ni latencia de producción.

La evidencia conserva los pasos y bytes serializados reales. Informó 146.225 tokens de entrada y 6.788 de salida informados; cuatro casos STT con uso completo desconocido y 30 segundos de audio informados; mediana local de 8,15 s, p95 de 14,25 s y máximo de 14,88 s por caso. El uso
no informado permanece desconocido. Sus checks de hechos finitos y exactitud tipada se hicieron
en memoria: `fixture-facts-checked-in-memory; style/free-text-not-exhaustively-assessed`.
No se exportan preguntas, transcripciones, propuestas, credenciales ni hashes de datos privados.

La reevaluación `review5_20261018_03`, sobre `761b564c`, usó la misma conexión guardada y el mismo perfil de texto `gpt-6.1-sol-candidate-1@1` con `gpt-6.1-sol`. Completó 30/30 casos de canal y 434/434 checks, incluidos seis casos de inyección: 108 pasos de proveedor, 78 invocaciones de herramientas, 198.572 tokens de entrada y 6.618 de salida; máximo local por caso de 14,2365 s. Conserva el alcance `fixture-facts-checked-in-memory; style/free-text-not-exhaustively-assessed`, sin acreditar estilo exhaustivo ni rendimiento de producción. Inicio 20/20 y STT 4/4 conservan la calificación anterior porque sus capacidades, esquemas e instrucciones no cambiaron. La descripción propia de la herramienta evaluada se identifica por ese commit; los intentos previos, su ledger y evidencia se conservan sin repetir una aceptación.

El `2026-11-08`, la Etapa 1 de [la IA arma formularios](../features/formularios.md#la-ia-arma-formularios-y-catálogos--etapa-1-2026-11-04) se calificó con la misma conexión guardada, `gpt-6.1-sol` y el perfil de texto `gpt-6.1-sol-candidate-1@1`. Fueron tres corridas, cada una con su base vacía:

| Corrida | Commit | Resultado |
| --- | --- | --- |
| `ia1_authoring_20261108_07` (`ia-authoring`) | `162db253` | 46/46 casos: 24 de Inicio con `home-system@2` y 22 del automático con `capabilities-v1@1`. Además, 4/4 normalizaciones WebM/MP4 y 976/976 checks. |
| `ia1_home_existing_20261108_01` (`home-existing`) | `162db253` | 20/20 casos de Inicio con `home-system@2`; 200/200 checks. |
| `ia1_channel_stage5_20261108_02` (`channel-stage5`) | `98b0d50c` | 30/30 casos del canal, incluidas las seis inyecciones; 434/434 checks. |

Entre las tres sumaron 306 pasos de proveedor, 206 invocaciones de herramientas, 621.116 tokens de entrada y 17.190 de salida, más 45 segundos de audio en cuatro STT cuyos tokens no se informaron. Los máximos fueron:
- contexto: 19.031 bytes, en un ajuste con historia;
- continuación: 10.821 bytes;
- herramientas: 5.256 bytes;
- por caso: cuatro pasos y tres invocaciones.

La mediana local por caso fue de 16,04 s en `ia-authoring`, 6,86 s en `home-existing` y 10,91 s en `channel-stage5`. El caso más largo tardó 33,76 s repartidos en varios pasos, y ningún paso llegó a los 30 s del proveedor.

El perfil STT del navegador, `openai-stt-browser-candidate@1` (`gpt-transcribe`, `audio/webm` y `audio/mp4`, 1.000.000 bytes, `audio-none-v1@1`), quedó calificado. Ese mismo día se declaró en la instalación local como `AudioModelProfiles:1`, con siete claves públicas, sin tocar el perfil Ogg/WAV ni la credencial. Con la configuración real de la Api (appsettings, Development y user-secrets), `AiAssistantOptions.IsValid()`, que es lo que comprueba la Api al arrancar, lo acepta. El micrófono de Inicio aparece al reiniciar la Api. Cada instalación lo declara por su cuenta; el arnés no lo hace. El alcance sigue siendo `fixture-facts-checked-in-memory; style/free-text-not-exhaustively-assessed`.

Los intentos anteriores de ese día no cuentan para la calificación. Se cortaron por fallas del arnés (un archivo retenido por Windows, un scope compartido en el dictado y un control con la regla de idioma del 2026-10-18), por la carga de la máquina (un `ProviderUncertain` y un timeout de Redis) o por dos ajustes del producto (`b09aed3c`: el prefijo inválido y la aclaración con cifras).

El `2026-11-09` se sumó el dictado por WhatsApp: `ia1_channel_dictation_20261109_01` (`channel-dictation`, sobre `5f61a087`), con la misma conexión guardada. Una nota de voz Ogg ficticia por idioma, con el mismo audio que el WebM del dictado de Inicio, se normalizó con el perfil Ogg/WAV ya declarado, `openai-stt-candidate@1` (`gpt-transcribe`, `audio-none-v1@1`), en la conversación automática propia de la persona, como la captura de WhatsApp. Después, en otra operación, `gpt-6.1-sol` con `gpt-6.1-sol-candidate-1@1` y `capabilities-v1@1` interpretó el texto que transcribió el servidor y propuso el formulario pedido. Resultado: 2/2 normalizaciones, 2/2 casos y 62/62 checks.
- Las dos interpretaciones sumaron 8 pasos de proveedor, 6 invocaciones de herramientas, 20.407 tokens de entrada y 307 de salida.
- Las dos STT informaron 22 segundos de audio y ningún token.
- Los máximos fueron 17.806 bytes de contexto, 4.848 de continuación y 1.582 de herramientas, con notas de hasta 41.334 bytes.
- Cada STT tardó menos de 5 s y la interpretación más larga, 12,34 s.

El alcance sigue siendo `fixture-facts-checked-in-memory; style/free-text-not-exhaustively-assessed`.

El `2026-11-11` se calificó la Etapa 2, planillas, de [la IA arma formularios](../features/formularios.md#la-ia-arma-formularios-y-catálogos--etapa-2-planillas-2026-11-09-a-2026-11-11). Se usaron la misma conexión guardada, `gpt-6.1-sol` y el perfil de texto `gpt-6.1-sol-candidate-1@1`, desde una copia limpia de `51db5a59`. Desde ese commit, Inicio y el asistente automático ofrecen `forms.authoring.propose-catalog@2` y `forms.authoring.propose-import@1`; la segunda, sólo en un turno con planilla. Fueron tres corridas, cada una con su base vacía:

| Corrida | Resultado |
| --- | --- |
| `ia2_spreadsheets_20261111_02` (`ia-spreadsheets`) | 30/30 casos con planillas (18 de Inicio y 12 del automático) y 4/4 normalizaciones (2 WebM y 2 Ogg); 892/892 checks. |
| `ia2_authoring_20261111_01` (`ia-authoring`) | 48/48 casos (24 de Inicio y 24 del automático, incluidos los dos dictados por WhatsApp) y 6/6 normalizaciones; 1038/1038 checks. |
| `ia2_channel_stage5_20261111_01` (`channel-stage5`) | 30/30 casos del canal, incluidas las seis inyecciones; 434/434 checks. |

Entre las tres sumaron 365 pasos de proveedor, 247 invocaciones de herramientas, 942.240 tokens de entrada y 20.953 de salida. Las STT de Inicio informaron 64 segundos de audio y ningún token. Los máximos fueron:
- contexto: 23.709 bytes, con la planilla preparada en el mensaje;
- continuación: 10.839 bytes;
- herramientas: 5.266 bytes;
- por caso: cuatro pasos y tres invocaciones.

La mediana local por caso fue de 10,29 s en `ia-spreadsheets`, 10,23 s en `ia-authoring` y 11,21 s en `channel-stage5`. El caso más largo tardó 19,66 s, así que ningún paso llegó a los 30 s del proveedor. Pasaron los cuatro puntos a mirar:
- OpenAI aceptó en modo estricto el mapeo en null de un catálogo sin planilla;
- el contexto con la planilla entró holgado;
- en Inicio, una planilla sin texto pregunta qué hacer;
- un pedido que no dice qué hacer con la planilla da `unclear`.

`home-existing`, `audio-es-en` y `stage5-all` no se repitieron, porque sus capacidades, esquemas e instrucciones no cambiaron. El primer intento (`ia2_spreadsheets_20261111_01`) se cortó al sembrar el catálogo de importar, antes de llamar al proveedor, por una falla del arnés corregida en `51db5a59`, y no cuenta. El alcance sigue siendo `fixture-facts-checked-in-memory; style/free-text-not-exhaustively-assessed`.

Desde la tarea 12 de [documentos](../features/formularios.md#la-ia-arma-formularios-con-un-documento--etapa-3-propose-form2-2026-11-12) (2026-11-12), el asistente automático de WhatsApp ofrece `forms.authoring.propose-form@2`, con otro esquema, en lugar de la 1. La evidencia real de `ia-authoring` (su mitad automática), de `ia-spreadsheets` y de `channel-dictation` se tomó con la 1 y ya no la cubre: esos sets se repitieron con la 2 el 2026-11-13 (`channel-authoring`, `channel-spreadsheets` y `channel-dictation`, más abajo). `channel-stage5` no cambia; desde la tarea 13 Inicio también ofrece la 2, así que su mitad de `ia-authoring` y de `ia-spreadsheets`, tomada con la 1, se repitió con la 2 el 2026-11-12, junto con `home-documents` (más abajo).

El `2026-11-12` se calificó la [lectura de documentos](../features/inteligencia-artificial.md#lectura-de-documentos-en-el-mismo-motor--2026-11-11)
con la misma conexión guardada, desde una copia limpia de `093f184f`: `doc_reading_20261112_02` (`document-reading`) leyó con
`ReadDocument` los quince documentos ficticios del arnés en español y en inglés, con el perfil candidato
`openai-document-candidate@1` (`gpt-6.1-sol`, PDF, JPEG y PNG hasta 10 MiB y 10 páginas, `document-read-v1@1`, razonamiento
bajo). Dio 30/30 lecturas y 502/502 checks: cada dato con sus dígitos y ningún número inventado, el idioma original, la foto
movida, la oscura y la cortada con su legibilidad y su causa, la orden escondida transcripta sin seguirla y el PDF escaneado y
el de permisos leídos. Sumaron 30 pasos de proveedor, 65.863 tokens de entrada y 6.360 de salida, en 198 s en total. OpenAI
aceptó en modo estricto el esquema de la lectura, el `input_file` y el `input_image` y el razonamiento bajo, que el arnés no
podía comprobar sin conexión. El primer intento (`doc_reading_20261112_01`) se cortó al crear la base de evaluación, antes de
llamar al proveedor, y no cuenta. Para que una instalación lea documentos falta declarar ese perfil en `DocumentModelProfiles`.

El `2026-11-12` se calificó el redactado del asistente de WhatsApp ([Casos del asistente](../features/whatsapp.md#casos-del-asistente--2026-11-11),
[ADR 0021](../decisions/0021-respuestas-redactadas-por-el-asistente.md), punto 8) con la misma conexión guardada, desde una copia
limpia de `9753a3a0`. `agent_drafting_20261112_04` (`channel-agent`) corrió el canal automático con agente tal como lo deja
`WithAgent`: tres precargas, `whatsapp.reply@1`, `whatsapp.draft-reply@1`, `whatsapp.agent-knowledge@1` y `assistant_proposal@2`,
con `capabilities-v1@1` y el perfil candidato `gpt-6.1-sol-candidate-1@2`, el de la 1 con `QualifiedToDraft`. Dio 32/32 casos y
452/452 checks:
- los 24 del punto 8: saludo y hora; consultas con información y sin ella (derivan con `unknown`); fuera de tema; pedir una
  persona y un tema bloqueado; una instrucción escondida en el mensaje y otra en el conocimiento, sin seguirlas;
- el resto del menú: no responder, los botones, el formulario y dos textos fijos en un mismo mensaje.

Los 16 redactados pasaron la revisión del servidor al primer intento. Sumaron 86 pasos de proveedor, 54 invocaciones de
herramientas, 406.427 tokens de entrada y 4.875 de salida. Los máximos fueron 29.468 bytes de contexto, 21.448 de continuación,
9.846 de herramientas (con las precargas) y, por caso, tres pasos y dos invocaciones. La mediana local por caso fue de 10,80 s,
el p95 de 18,22 s y el máximo de 20,27 s, lejos de los 30 s del proveedor por paso.

Los tres intentos anteriores no cuentan:
- `agent_drafting_20261112_01` se cortó en el primer caso con `ProviderUncertain`: su primer paso no respondió en 30 s, y el
  mismo caso tardó 15 s en el intento siguiente;
- `agent_drafting_20261112_02` dio un falso fallo del oráculo, que exigía que la propuesta de un formulario quedara `complete`
  cuando su dueño la registra `proposed` (`eff16abe`);
- `agent_drafting_20261112_03` mostró que, ante dos preguntas, el modelo proponía una sola acción, porque la descripción del
  agente mandaba seguir «el primer caso»; lo corrigió `9753a3a0`.

Ese mismo día se declaró en la instalación local como `ModelProfiles:1`, con ocho claves públicas, y se conservó `ModelProfiles:0`
(la 1) para los turnos ya sellados. Con la configuración real de la Api, `AiAssistantOptions.IsValid()` la acepta y un turno nuevo
sella la 2; el redactado aparece al reiniciar la Api. Cada instalación lo declara por su cuenta, y el arnés no lo hace. El alcance
sigue siendo `fixture-facts-checked-in-memory; style/free-text-not-exhaustively-assessed`.

El `2026-11-12` se calificó armar un formulario con un documento en Inicio ([formularios](../features/formularios.md#la-ia-arma-formularios-con-un-documento--etapa-3-propose-form2-2026-11-12)), con la misma conexión guardada, desde una copia limpia de `e70542a3`. Desde la tarea 13, Inicio ofrece `forms.authoring.propose-form@2`. Cada foto o PDF se subió por el servicio real de documentos de Inicio y se leyó en `home-document` con `openai-document-candidate@1`; los turnos usaron `gpt-6.1-sol-candidate-1@1` y `home-system@2`. Fueron tres corridas, cada una con su base vacía:

| Corrida | Resultado |
| --- | --- |
| `ia3_home_documents_20261112_02` (`home-documents`) | 16/16 turnos con documento y 12/12 lecturas; 676/676 checks. |
| `ia3_home_authoring_20261112_01` (`home-authoring`) | 24/24 casos de Inicio y 4/4 dictados; 528/528 checks. |
| `ia3_home_spreadsheets_20261112_01` (`home-spreadsheets`) | 18/18 casos con planillas y 2/2 dictados; 538/538 checks. |

Entre las tres sumaron 206 pasos de proveedor, 130 invocaciones de herramientas, 609.771 tokens de entrada y 18.001 de salida; los dictados informaron 64 segundos de audio. Los máximos fueron:
- contexto: 33.531 bytes, con la lectura del documento en el mensaje;
- continuación: 19.997 bytes;
- herramientas: 4.326 bytes;
- por caso: cuatro pasos y tres invocaciones.

La mediana local por caso fue de 11,30 s en `home-documents`, 12,23 s en `home-authoring` y 10,29 s en `home-spreadsheets`, y el caso más largo tardó 22,44 s. Pasaron los puntos a mirar:
- cada lectura de Inicio cumplió lo que `document-reading` espera del mismo archivo;
- el formulario prendió la lectura con los comprobantes y la dejó apagada con la planilla en papel;
- «Cómo reconocerlo» no copió números, nombres ni la orden escondida, y cada muestra quedó como en el documento;
- un documento sin texto pregunta qué hacer.

El primer intento (`ia3_home_documents_20261112_01`) pasó los dos comprobantes y se cortó en la planilla en papel por un falso fallo del oráculo: pedía la etiqueta exacta de cada columna y el modelo repitió la tabla por renglón, con su número (`e70542a3`). No cuenta. El alcance sigue siendo `fixture-facts-checked-in-memory; style/free-text-not-exhaustively-assessed`.

El `2026-11-13` y el `2026-11-14` se calificaron con la misma conexión guardada y `gpt-6.1-sol-candidate-1@1` los dos sets de [documentos por WhatsApp](../decisions/0022-lectura-de-documentos-con-ia.md) y las mitades automáticas que la tarea 12 dejó con `propose-form@2`. Cada corrida salió de una copia limpia de HEAD, con su base vacía, y cada documento se leyó antes con `openai-document-candidate@1`, como lo lee WhatsApp:

| Corrida | Resultado |
| --- | --- |
| `doc_channel_authoring_20261113_01` (`channel-authoring`, desde `5394e8dc`) | 24/24 casos y 2/2 dictados; 510/510 checks. |
| `doc_channel_spreadsheets_20261113_01` (`channel-spreadsheets`, desde `5394e8dc`) | 12/12 casos y 2/2 dictados; 354/354 checks. |
| `doc_channel_dictation_20261113_01` (`channel-dictation`, desde `5394e8dc`) | 2/2 casos y 2/2 dictados; 62/62 checks. |
| `doc_channel_documents_20261113_04` (`channel-documents`, desde `63d3e847`) | 28/28 casos, 2/2 dictados y 16/16 lecturas; 794/794 checks. |
| `doc_forms_documents_20261114_05` (`forms-documents`, desde `5e9f5644`) | 12/12 casos y 12/12 lecturas; 556/556 checks. |

Entre las cinco sumaron 323 pasos de proveedor, 209 invocaciones de herramientas, 867.938 tokens de entrada y 31.164 de salida, y los dictados informaron 70 segundos de audio. Los máximos fueron 25.694 bytes de contexto, 18.967 de continuación y 4.919 de herramientas, y por caso cinco pasos y cuatro invocaciones, en «Armar formulario con la IA». La mediana local por caso fue de 8,27 s a 15,33 s según el set, y el caso más largo tardó 20,35 s.

Los intentos anteriores no cuentan, y cada uno dejó un arreglo:
- `channel-documents`: el `_01` se cortó al sembrar, antes del proveedor, porque el compilador real rechazaba los formularios que leen documentos (`84f2d5d8`). El `_02` dio un falso fallo del oráculo: comparaba el contexto enviado con el sellado, pero el motor sella sólo lo que admiten el archivo y los permisos (`6a8fecbd`; `80ae4bac` arregló además la compilación del arnés). En el `_03`, con un comprobante neutro mandado solo y en inglés, el modelo eligió un formulario donde correspondía una duda; la descripción de `propose-entry@1` ahora nombra el dato que falta, si el negocio pagó o cobró (`fac348ab`).
- `forms-documents`: el `_01` cortó con `LimitExceeded`, porque cuatro pasos no alcanzaban para leer catálogos, formularios y estándares, proponer y cerrar (`0a70cdbb`, cinco pasos). En el `_02` y el `_03`, en inglés, una muestra salió reescrita, y el pedido pasó a pedirlas tal como las escribe el primer documento, sin traducir y nunca una instrucción (`fac348ab`; el check `samples-copy-no-instruction`, `63d3e847`). En el `_04` otra muestra salió traducida, y desde `e0b2978b` el servidor descarta una muestra que la lectura no escribe: no la rechaza, porque el pedido ya usa los cinco pasos de proveedor y un rechazo lo dejaba sin propuesta.

El alcance sigue siendo `fixture-facts-checked-in-memory; style/free-text-not-exhaustively-assessed`.

Para otra calificación, ejecutá el [arnés](../../harnesses/assistant-qualification/README.md)
con fixtures ficticios y presupuesto controlado. Detenelo ante el primer check fallido. Conservá
la base, ledger, archivos privados y key ring DPAPI propio del run: `--resume` consulta Get
con el mismo UUID y contexto, sin un nuevo Submit ni copiar claves de la aplicación. También
inicia el host y sus workers: un Pending todavía válido puede continuar la operación aceptada
original; una operación incierta no se vuelve a enviar. Inspeccioná su estado antes de reanudar.
Si se corrige con TDD un oráculo finito, `--continue-existing` vuelve a leer las aceptaciones
por Get, conserva su evidencia y métricas originales y sólo envía casos sin ledger previo;
no sirve para cambiar modelos, perfiles, instrucciones ni fixtures de una aceptación.
Registrá modelos/perfiles e instrucciones exactos, capacidades/versiones, continuación, uso,
duración, bytes, resultados y límites antes de declarar otra versión.

La declaración técnica se aplicó el `2026-10-18T14:13:15Z`: se verificaron las 14 claves públicas,
la conservación de la otra configuración y de la credencial. Un host construido de nuevo, sin
iniciarlo ni llamar al proveedor, verificó el servicio real de disponibilidad en modo sólo lectura:
OpenAI Disponible, `gpt-6.1-sol` y un perfil de texto más uno STT, sin modificar la conexión. La aplicación debe cargar esa configuración al reiniciarse
antes del piloto; esta evidencia no acredita el reinicio de sus réplicas. Disponible sigue siendo
insuficiente por sí solo; no hay habilitación automática desde el arnés.

El [piloto físico con Meta e IA](../plans/verificacion-y-produccion.md#formularios-piloto-del-asistente-y-audios)
ya acreditó texto, audio, revisión manual y el recorrido en inglés con traducción completa.
Quedan la continuación tras responder y repetir el respaldo integral al original con la
corrección del review. Esa evidencia física es distinta de la calificación y las suites locales.
