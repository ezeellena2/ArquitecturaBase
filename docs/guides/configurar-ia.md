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
   turnos, herramientas y comprobantes; no siembra credenciales ni hechos ficticios.
2. Provisioná el anillo externo antes de arrancar. Cada réplica usa exactamente el mismo
   InstallationId, claves históricas y ActiveKeyId. Un derivado independiente usa ID y claves nuevos.
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

Los límites HTTP aceptan 1–100000. No son contabilidad ni presupuesto distribuido. No hay
URL, timeout, tope ni prompt configurables por el administrador funcional. El startup
rechaza opciones inválidas, sin incluir sus valores sensibles en el diagnóstico.

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
3. Cambiá ActiveKeyId en todas y reinicialas. Las escrituras nuevas y HMAC nuevos usan la nueva clave;
   lecturas y reintentos de comprobantes usan el keyId original.
4. Reemplazá la clave del proveedor en el editor si necesitás recifrar la fila con la activa.
   Un guardado `Keep` no recifra: preserva el ciphertext. No hay worker de recifrado.
5. Conservá las claves anteriores mientras las necesiten la fila, comprobantes de 30 días
   o **cualquier backup retenido**. No basta con esperar 30 días si la fila sigue con la antigua.

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

## Proveedores y prueba real pendiente

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
No se llamó a estos servicios en tests. Quedan pendientes llamadas reales de ambos proveedores,
permisos/crédito de las cuentas y latencia del ambiente desplegado. Nunca agregues claves
reales a fixtures ni suites. [Evidencia](../reviews/2026-10-02-configuracion-llm.md).

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

La aceptación fija perfil e instrucciones por Id/versión. Inicio usa `home-system@1`; los
canales usan `capabilities-v1@1`. El worker exige la versión capturada y no cambia una operación
pendiente al perfil nuevo. Para modificar un perfil o instrucciones, calificá y declará otra
versión; no reescribas la versión que conservan turnos anteriores. Reiniciá las réplicas después
de configurar: los perfiles no se recargan en caliente y la revisión de conexión protege cada paso.

El catálogo actual del canal necesita once capacidades y una terminal: el transporte admite
hasta doce definiciones. Es una cota de catálogo, independiente de las seis invocaciones y
cuatro pasos máximos de ejecución por turno. No aumentes cuotas, bytes ni llamadas para
hacer pasar una calificación.

| Clave bajo `ArtificialIntelligence:Assistant` | Valor inicial, aún propuesto para piloto |
| --- | --- |
| `Limits:ActorDailyQuota` / `InstallationDailyQuota` | 10 / 100 aceptaciones por día UTC |
| `Limits:PendingCapacity` / `RunningCapacity` | 50 / 4 para toda la instalación, entre réplicas |
| `Limits:RetentionDays` | 30 desde cada turno |
| `Limits:QuestionCharacters` / `HistoryTurns` / `HistoryCharacters` | 4000 / 12 / 24000 |
| `Limits:ContextBytes` / `ContinuationBytes` | 131072 / 131072 |
| `Limits:ProviderSteps` / `ToolCalls` | 4 / 6 |
| `Limits:ToolResultBytes` / `ToolAggregateBytes` | 16384 / 65536 |
| `Limits:OutputTokens` / `ProviderResponseBytes` | 1500 / 262144 |
| `Limits:ToolTimeoutSeconds` / `ProviderTimeoutSeconds` | 5 / 30 |
| `Limits:RunningSeconds` / `PendingSeconds` / `LeaseGraceSeconds` | 90 / 120 / 15 |
| `Limits:MaintenanceBatchSize` / `PollingAfterSeconds` | 100 / 2 |
| `WorkerPollingSeconds` / `MaintenanceIntervalMinutes` | 1 / 60 |
| `CompletionPersistenceSeconds` | 5; sólo repite persistencia, nunca HTTP |
| `ProcessInBackground` | true; false permite arneses controlados sin worker automático |

Startup valida valores positivos, cotas y consistencia de cuotas/capacidades/perfiles. No se
pueden ampliar las cotas de protección del código sin revisar implementación y pruebas. El rate
limit HTTP `ArtificialIntelligence:RateLimiting:AssistantSubmitPermitLimit` parte de 6/min por
operador/réplica; es distinto de la cuota distribuida. Ninguno calcula gasto: configurá límites
y alertas en la cuenta del proveedor. Retención/cuotas y habilitación de modelos requieren la
calibración del piloto; las pruebas con handlers falsos no los convierten en premisas operativas.

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
| `ContentTypes` | Formatos realmente calificados entre `audio/ogg` y `audio/wav`. |
| `MaximumAudioBytes` | Límite calificado de hasta 25.000.000 bytes; puede ser menor. |

El motor usa la misma conexión, credencial cifrada y revisión. No cargues otra API key ni cambies
`AiConnection.Model` para transcribir. Las instrucciones capturadas son `audio-none-v1@1`,
sin prompt, historia, capacidades ni terminal textual. Se conserva la transcripción completa
sin traducción ni idioma forzado; texto excesivo o audio que no cumple el descriptor se rechazan.
La posterior interpretación es otra operación del canal, con su autorización actual. En Atención
manual normalizar requiere lectura, mientras interpretar para ofrecer exige además envío;
normalizar no concede ese permiso ni `ai.assistant.use`.

Los candidatos aislados de la Etapa 5 son OpenAI/`gpt-6.1-sol` con
`gpt-6.1-sol-candidate-1@1`, contexto 131072, y OpenAI/`gpt-transcribe` con
`openai-stt-candidate@1`, Ogg/Wav y máximo 25.000.000 bytes. Están definidos sólo para la
calificación del [arnés](../../harnesses/assistant-qualification/README.md); siguen sin evidencia
real y no constituyen una declaración de perfiles habilitados en la aplicación.

### Retención y recuperación

La clave histórica del anillo protege contenido del asistente además de credenciales y huellas.
Conservá keyId/bytes/InstallationId mientras haya contenido, comprobantes o backups dependientes.
Eliminar una conversación retira el acceso inmediatamente y pide cancelación; la purga física
se ejecuta después en lotes, y los comprobantes sin texto impiden eludir cuotas o repetir UUID.
Después de vencer todos los turnos y cerrar los activos, la conversación retirada se elimina
físicamente con sus turnos/herramientas. La purga usa un presupuesto total por lote y scopes
nuevos; conserva los comprobantes independientes mientras siga su ventana.
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
- Si faltan claves históricas, recuperalas de su respaldo. Cambiar la conexión o recrear un keyId
  no recupera preguntas ni comprobantes antiguos y no justifica regenerarlos.
- En audio, conservá la referencia ya fijada: una pérdida de acceso, vencimiento o incertidumbre
  no autoriza descargar/transcribir otra vez ni reemplazar su UUID. La lectura del estado no
  ejecuta el efecto. El archivo original sigue la retención de su módulo, y el contenido IA usa
  el menor vencimiento entre esa fuente y la retención del motor. Segundos o tokens ausentes
  siguen desconocidos; el uso válido de una respuesta tardía se conserva sin publicar su texto.

[Evidencia local y piloto pendiente](../reviews/2026-10-04-inicio-asistente.md).

## Evidencia pendiente para habilitar perfiles

La implementación y los tests con dobles no califican un modelo. La calificación real de la
Etapa 5 sigue pendiente; no hay una declaración de modelo habilitado derivada de esta guía.
El [arnés manual](../../harnesses/assistant-qualification/README.md) prepara veinte casos de Inicio,
veinticuatro de canal y cuatro audios ficticios es/en. Usa una base de evaluación aislada, la
conexión guardada de origen mediante un lector read-only y las cuotas reales. No llama al
transporte directamente ni crea otro worker. Cortá ante un check fallido y revisá además la
calidad semántica antes de declarar el perfil exacto.

Para cerrar esta evidencia falta registrar:

- El modelo/perfil e instrucciones exactos, capacidades/versiones, continuación y resultados
  esperados de los casos reales de Inicio y canales, con sus permisos y propuestas observadas.
- Los cuatro audios Ogg/Wav es/en por el mismo motor y conexión: transcripción completa, idioma
  original, uso informado o desconocido, duración y límites; sin segunda clave ni conversión
  automática de un formato que falle.
- Contexto y continuación medidos sobre los bytes realmente serializados, pasos, llamadas,
  latencia y fallas. La evidencia exportada sólo contiene metadata segura y hashes de fixtures
  públicos, nunca preguntas, transcripciones, propuestas, claves ni sus hashes privados.
- Recuperación de las operaciones existentes con su UUID y contexto, sin otro Submit ante un
  resultado incierto. El host aislado conserva ledger, archivos privados y key ring propio
  cifrado con DPAPI de Windows bajo el destino del run; no copia claves DataProtection de la
  app. `--resume` sólo consulta Get y requiere esos archivos y la base de la misma ejecución.
- La verificación conjunta de integración y del recorrido real con Meta de la Etapa 5, cuyo
  alcance sigue en [el plan vigente](../plans/formularios/etapa-5-asistente.md). No se sustituye
  por los self-tests offline ni por la prueba acotada de conexión.

Sólo después se declaran los perfiles calificados en la configuración técnica, se reinicia la
app y se agrega la evidencia real. Una conexión Disponible sigue siendo insuficiente para
habilitar el asistente o STT sin esa calificación.
