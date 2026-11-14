# WhatsApp: tests de Domain.UnitTests

Prueba entidades y reglas puras del módulo, sin proveedores.

## Al modificar

- Probar default Manual, combinaciones válidas de propósito/evento y rechazo de metadata inválida; conservar transiciones, identidad y estados del historial.
- Para Flows, distinguir reserva (`Begin`) del inicio durable del efecto (`MarkEffectStarted`); probar retry sólo cuando no se envió y reconciliación si el resultado es incierto.
- Probar que el estado propio de cada FlowReply no hereda accidentalmente un resultado de otra respuesta repetida.

- Los tests siguen el dominio del módulo; no mover sus errores o entidades al núcleo.
- El módulo debe poder quitarse sin dejar referencias en pruebas del núcleo.

## Referencias

- [Reglas funcionales del módulo](../../../../docs/features/whatsapp.md).
- [Independencia del núcleo](../../../../docs/guides/quitar-whatsapp.md).

Las salidas Assistant prueban identidad capturada sin operador, botones acotados e inmutables y el fence compartido con FormAction. Una reserva de otro origen o vencida no inicia el POST; la incertidumbre sólo admite una aceptación tardía, sin recrear el envío.

H6 verifica descriptor inicial sin reclasificar Media, identidad inmutable del pedido de audio, publicación idempotente del texto completo y estados terminales. La recuperación de adquisición requiere cinco fallas y un día; con cinco y quince minutos deja de frenar lo que empareja (H11, `WhatsAppAcquisitionRetryTests`).

Assistant prueba origen explícito, operador ausente sólo automático, copia durable de opciones con revisión, fence compartido y marcador de análisis que no altera lectura del operador. Session limpia exclusivamente su contenido privado vencido y conserva el payload anterior.

WhatsAppFlowDeprecationTests separa reserva de inicio, recuperación de lease, fence tardío y conciliación remota de publicación/deprecación. Comprueba que Unknown o identidad discordante conservan incertidumbre, que un rechazo exige consulta antes de repetir y que fallas permanentes conservan NeedsAttention y espera creciente sin tocar la publicación sellada.

IA, Etapa 2: WhatsAppMessageSpreadsheetTests prueba el descriptor y su nombre acotado, la copia guardada una vez, el rechazo cierto una sola vez, No disponible final y las fallas que cuentan como las del audio.

WhatsAppFormActionChoiceTests prueba las opciones de un aviso: botones fijados con el id de la salida, los topes de Meta, sin documento, el reintento con los mismos botones y el id que se lee de vuelta sólo con su prefijo.

Documentos (tarea 8): WhatsAppMessageDocumentTests prueba el descriptor y su nombre por tipo, la espera de su pedido, la copia una vez, el rechazo cierto una sola vez, No disponible final, las fallas que cuentan y la lectura sólo al usarse: un Word con lo preparado y lo demás con el pedido fijado una vez.

W3 (tarea 22): WhatsAppMessageSharedTests prueba la dirección de un lugar y la primera tarjeta de un contacto, guardadas una vez y tal como vinieron: el nombre recortado o nulo, los diez primeros teléfonos y lo que no es un teléfono, afuera.
