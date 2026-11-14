# 0030 — El contenido del chat de WhatsApp se guarda cifrado

**Estado:** Aceptada, 2026-11-14. Rige desde la tarea 20 (H16) del [plan de la bandeja](../plans/2026-10-01-bandeja-whatsapp.md), en tres partes: primero las sesiones de envío y el porqué de las referencias (`EncryptWhatsAppSendContent`), después las salidas (`EncryptWhatsAppOutgoingContent`) y por último los mensajes (`EncryptWhatsAppMessageContent`).

## Contexto

La [revisión de arquitectura de WhatsApp](../reviews/2026-11-12-arquitectura-whatsapp.md#h16-los-chats-se-guardan-en-claro) encontró los chats en claro. Quien llega a una copia de la base, como un respaldo o un usuario de reportes, lee 90 días de textos, transcripciones, ubicaciones, filas de planillas y avisos con valores de formularios que la app oculta a quien no tiene `formrecords.read`. En cambio, la respuesta de Flow, la sesión del envío y la cola de acceso ya se cifran con Data Protection. La amenaza es la del [ADR 0015](0015-colas-transaccionales-cifradas.md): una copia de PostgreSQL. Las claves de Data Protection viven en la misma base, pero un despliegue las protege con un certificado externo ([despliegue](../guides/despliegue.md)), así que una copia sola no alcanza para leer. El 2026-11-13 se decidió cifrar todo el contenido del chat.

## Decisión

Se cifra todo el contenido del chat, en todas las tablas del módulo, con Data Protection y un propósito propio (`ArquitecturaBase.WhatsApp.ChatContent.v1`):

- `WhatsAppMessages`: `Body`, `QuickReplyTitlesJson`, `AssistantReplyJson`, `SpreadsheetRowsJson`, `Latitude`, `Longitude`, `LocationAddress`, `ContactName`, `ContactPhonesJson`, `SpreadsheetFileName`, `SpreadsheetPreparedJson`, `DocumentFileName` y `DocumentReadingJson`.
- `WhatsAppOutgoingMessages`: `Body`, `QuickRepliesJson`, `AssistantReplyJson`, `ListButton`, `Footer`, `SpreadsheetRowsJson` y `DocumentName`.
- `WhatsAppFlowSessions`: `FlowBody`, `LocationLatitude` y `LocationLongitude`.
- `WhatsAppAssistantRequests`: `OutcomeJson`.

Quedan en claro el nombre del perfil y el número de cada chat, porque la bandeja los muestra y busca por ellos. También los identificadores técnicos (de Meta, de los botones, de los archivos), los estados, las fechas y los datos del negocio: el motivo de un caso, el nombre del formulario, la pregunta rechazada y el número de registro.

Esas columnas pasan a `bytea`. Un conversor de EF cifra al guardar y descifra al leer, así que entidades, repositorios y lectores siguen trabajando con texto, números y JSON, y una lectura nueva no puede olvidarse de descifrar. Data Protection nunca da el mismo cifrado dos veces, por eso el contenido no se busca ni se compara en SQL: sólo se pregunta si es nulo; el dominio no guarda textos vacíos (un cuerpo recibido en blanco se guarda como nulo), así que eso alcanza. Lo que una consulta completa junto al contenido (el texto de una fuente del asistente, el nombre de un archivo) se calcula con lo leído: una constante junto a la columna pasaría por el conversor. El propósito no se ata a la fila: un cifrado copiado a otra fila se lee, lo que alcanza contra una copia de la base. Un punto se guarda con seis decimales, como la columna de antes. Lo que el anillo de claves ya no abre (claves perdidas, una base restaurada en otro lado) se lee como vacío, como si lo hubiera vaciado la retención, en lugar de romper la carga de su fila. En SQL, en cambio, sigue presente: lo preparado de una planilla o lo leído de un documento que ya no se abre llega vacío al asistente, que lo trata como un dato dañado. EF arma el modelo una vez por proceso, así que el conversor usa el protector de la copia, que se fija al arrancar y antes de leer contenido. Todas las copias comparten el anillo de claves en la base.

La migración cambia el tipo de las columnas y deja vacío lo que ya hay, como hace la retención a los 90 días: los chats y sus mensajes siguen, sin el contenido. Vacía más que la retención (también las salidas en cola, que después fallan, los botones de lo ya enviado y lo que todavía se procesa: lo que el asistente no leyó y las planillas y documentos en curso, que pierden su nombre y lo preparado o leído), así que conviene migrar sin salidas en cola ni conversaciones del asistente en curso. Las consultas que preguntaban por un valor nulo de lo enviado, como las opciones de una pregunta, lo toman como sin opciones.

## Consecuencias

- Un respaldo sin el certificado no se lee. Si se pierden las claves, el contenido de los últimos 90 días queda ilegible, como ya pasa con las respuestas de Flow.
- Las consultas que miraban el cuerpo en SQL (si está vacío o en blanco, el `COALESCE`) pasan a preguntar si es nulo y a completar lo que falta con lo leído. La vista previa con `CASE` sigue en SQL: sólo elige la columna, y el conversor descifra lo que devuelve.
- Hoy sólo hay datos de desarrollo. Una instalación con chats reales tiene que convertirlos antes de migrar, y el traspaso al multitenant lo avisa.
- Estas migraciones no conviven con la imagen anterior: entre el bundle y la imagen nueva, la anterior falla al leer o guardar ese contenido ([despliegue](../guides/despliegue.md)).
- Cifrar y descifrar cuesta algo por fila; la bandeja lee páginas acotadas.

## Alternativas descartadas

- **Dejar el chat en claro,** protegido sólo por los 90 días de retención y el acceso a la base.
- **Cifrar sólo lo más sensible** (avisos, transcripciones, planillas): el cuerpo de los avisos comparte columna con el de los demás mensajes.
- **Cifrar en cada servicio con propósitos atados a cada fila,** como la respuesta de Flow: es más fuerte contra quien puede escribir en la base, pero obliga a tocar cada lectura y cada escritura, y una lectura nueva podría olvidarse.
- **Cifrar en PostgreSQL (pgcrypto):** la clave viajaría en las consultas.

Detalle: [WhatsApp](../features/whatsapp.md) y el [relevamiento de columnas en la revisión](../reviews/2026-11-12-arquitectura-whatsapp.md#h16-los-chats-se-guardan-en-claro).
