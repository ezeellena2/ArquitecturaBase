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
   confirmar. Disponible prepara la conexión; el asistente genera sólo ante una pregunta aceptada
   explícitamente y con un perfil técnico habilitado.
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

Aplicá `20261004164813_AddAiAssistant` mediante el bundle antes de activar esta versión.
Concedé `ai.assistant.use`; las fuentes exigen además users.read, roles.read o ai.business.read
según su contenido. Admin recibe los permisos del catálogo al seed, sin habilitar una credencial.
El permiso ai.read sólo habilita el enlace a configuración; no es necesario para consultar.

No hay perfiles habilitados por defecto. Un operador técnico califica el modelo de la conexión
guardada con pruebas reales controladas de las cinco herramientas, argumentos, continuación,
salida terminal y contexto/latencia. Después declara un elemento bajo
`ArtificialIntelligence:Assistant:ModelProfiles:<índice>` con `Provider` (OpenAI/Anthropic),
`Model` exacto, `ContextBytes` suficiente, `SupportsTools=true` y `SupportsTerminalAnswer=true`.
Un alias de modelo o un catálogo visible no acredita esas capacidades. Sin coincidencia exacta,
Inicio informa `modelNotQualified` y no acepta generación. Un cambio de perfil requiere reinicio;
las revisiones de la conexión guardada siguen protegiendo cada turno.

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

La clave histórica del anillo protege contenido del asistente además de credenciales y huellas.
Conservá keyId/bytes/InstallationId mientras haya contenido, comprobantes o backups dependientes.
Eliminar una conversación retira el acceso inmediatamente y pide cancelación; la purga física
se ejecuta después en lotes, y los comprobantes sin texto impiden eludir cuotas o repetir UUID.
Después de vencer todos los turnos y cerrar los activos, la conversación retirada se elimina
físicamente con sus turnos/herramientas. La purga usa un presupuesto total por lote y scopes
nuevos; conserva los comprobantes independientes mientras siga su ventana.
No borres filas de operación para recuperar una generación incierta.

## Runbook del asistente

- Una pregunta Pending espera un claim; si excede el plazo, el reconciliador la vuelve Expired.
  GET y recarga sólo leen estado. Un nuevo intento del usuario lleva otro UUID.
- Una Running vencida con paso reservado pasa a Uncertain. Revisá estado del proveedor por sus
  herramientas operativas, sin reenviar el pedido desde la base. No hay recuperación automática
  de continuación externa tras reiniciar el proceso.
- Si la respuesta existe pero falla su commit, se reintenta sólo la persistencia acotada con
  fence original. Una vez vencido, reconciliación cierra la operación; no publique manualmente
  ciphertext ni resultados de otro fence.
- Para cancelar, usá el endpoint propio o quitá la conversación. Durante HTTP el worker observa
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

[Evidencia local y piloto pendiente](../reviews/2026-10-04-inicio-asistente.md).
