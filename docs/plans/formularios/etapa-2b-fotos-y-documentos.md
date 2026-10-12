# Etapa 2B — Fotos y documentos

Estado 2026-10-11: sin empezar. Va después de catálogos (Etapa 2) y antes de la planilla. Tableros F1–F5 elegidos. Requiere que el usuario cree la cuenta de Azure Storage de desarrollo ([guía local][azure]).

## Qué se construye

- **Archivos en Azure** (sin tablero). Backend: guardar y leer archivos privados en Azure Blob ([ADR 0012]), con su registro (dueño, nombre, tipo, tamaño, hash y clave). Solo lo que usa esta etapa: sin versiones, staging, fondo del chat ni pruebas de dos réplicas del [plan de archivos][files]. Azurite queda para pruebas repetibles. Front: nada propio.
- **F1 · Preguntas en el editor** ([tablero][t]). Backend: tipos «Foto» y «Documento» en el formato, sin reescribir versiones publicadas; origen de la foto, mínimo y máximo; el check de publicación rechaza si la suma de máximos pasa de 10. Front: los dos tipos, «De dónde sale la foto», «Cuántas fotos», contador «Archivos: hasta N de 10» y vista previa con una pantalla por archivo.
- **F2 · En el celular** ([tablero][t]). Backend: el compilador usa PhotoPicker y DocumentPicker (Flow JSON 4.0 o posterior; el perfil actual compila 7.3); una pregunta de archivo por pantalla y nunca los dos pickers juntos; el valor va como propiedad de nivel superior de `complete`, sin init-values. Front: nada.
- **F3 · La respuesta llega con archivos** ([tablero][t]). Backend: del webhook toma por archivo `id`, `file_name`, `mime_type` y `sha256`; el worker baja cada uno con la API de medios, verifica hash, tamaño y tipo real por contenido, arma la miniatura y lo guarda; la respuesta se registra completa o queda rechazada con motivo. Front (WhatsApp): tarjeta «bajando» sin valores; guardada con miniaturas, nombres y número; sin permiso, sin miniaturas ni nombres; rechazos con «Mandar de nuevo»; muchas fotos con «+N».
- **F4 · Ver y descargar** ([tablero][t]). Backend: miniatura y original solo por la API, con `formrecords.read`. Front: visor en el `Dialog` común (anterior y siguiente, flechas, Escape, foco de vuelta a la miniatura); descargar el original; los documentos siempre se descargan.
- **F5 · Límites y casos** ([tablero][t]). Más de 10 al publicar; armado de pantallas; tamaño y tipo que frena WhatsApp; reintentos («No hace falta hacer nada»); sin almacenamiento configurado, los tipos se ven pero no se eligen; permiso perdido con el visor abierto: se cierra y se quitan las miniaturas; cuánto duran.

## Decisiones tomadas

No se vuelven a preguntar.

- «Foto»: «Cámara o galería» (predeterminado) o «Solo cámara». «Documento»: PDF, Excel (xls y xlsx), Word e imágenes JPEG y PNG. Sin CSV (Meta no lo admite), video ni comprimidos. Textos es/en como el resto (D5).
- Mínimo y máximo por pregunta; obligatoria es mínimo 1.
- Hasta 10 archivos y 100 MiB por respuesta (límite de Meta): no se publica si la suma de los máximos pasa de 10.
- Tamaño por archivo: un solo tope de instalación en `BusinessFormsOptions`, hasta 25 MiB. No hay tope por pregunta.
- Una pregunta de archivo por pantalla: el compilador las ubica y la vista previa lo muestra.
- Nada guardado a medias. Si un archivo no se baja tras reintentos acotados, o no coinciden hash, tamaño o tipo, la respuesta queda rechazada y visible en el chat con su motivo; el operador puede reenviar.
- Bajar de Meta es una lectura idempotente: reintento simple, sin la recuperación durable de los efectos externos ([ADR 0018]). Meta guarda los archivos hasta 20 días.
- Subir a Azure con una clave determinística por respuesta y archivo: reintentar sobreescribe lo mismo, así que también alcanza con reintento simple.
- Las rutas de miniatura y descarga las define el código, bajo la respuesta y con `formrecords.read`.
- Los archivos que no quedan referenciados por una respuesta confirmada se limpian.
- Contenedor privado de Azure Blob. Se ven y descargan solo por la API, con el mismo permiso que los valores (`formrecords.read`); nunca URL públicas ni acceso directo del navegador a Azure. Sin permiso nuevo.
- Imágenes JPEG y PNG en línea con `nosniff`; documentos siempre como descarga.
- Miniaturas livianas armadas al guardar; la foto original se baja solo al abrir el visor.
- Nombres de archivo y contenidos nunca van a los logs, a la lista de chats ni a los avisos.
- Los archivos siguen a la respuesta, no a los 90 días del cuerpo de WhatsApp. Las respuestas hoy no se borran; sus archivos tampoco. La respuesta cruda del Flow se limpia como en Etapa 1.
- El tope se configura en MiB; en pantalla se muestra como MB.
- La biblioteca de imágenes se justifica en Infrastructure al implementarla.
- El antivirus (por ejemplo, Defender for Storage) es requisito antes de producción y se decide con el despliegue.

## Fuera de esta etapa

- Columna con cantidad y miniatura en la planilla, y galería en el detalle (Etapa 3, con el mismo visor).
- Exportar a Excel cantidad y nombres de archivos, hasta 10.000 respuestas por vez (Etapa 3, T5).
- Incluir fotos en el PDF (Etapa 4).
- Audios de WhatsApp (Etapa 5; reusan esta descarga).
- Fondo del chat y los demás consumidores del plan de archivos.
- Endpoint de intercambio cifrado.

## Cómo se cierra

- Checks de cierre de paso de [ADR 0018] y [validar un cambio], en los dos repos.
- TDD de tipos, límites y compilador (una pregunta de archivo por pantalla, suma de máximos).
- Integración con Docker: archivo bajado y guardado; hash que no coincide; tipo real distinto del declarado; tamaño excedido; Meta caído y archivo vencido; permiso de descarga; los archivos no se borran con la retención de WhatsApp y los huérfanos se limpian.
- El adaptador se prueba contra la cuenta real de Azure, no solo con Azurite (ADR 0012).
- Piloto real con Meta: una foto con cámara y un PDF desde el teléfono vinculado, visibles tras recargar.

## Pendientes que se arrastran

- La cuenta de Azure Storage de desarrollo la crea el usuario; sin ella no se prueba contra Azure.
- Elegir el antivirus con el despliegue, antes de producción.

[t]: ../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#los-tableros
[files]: ../2026-10-02-almacenamiento-de-archivos.md
[azure]: ../../guides/azure-blob-en-local.md
[ADR 0012]: ../../decisions/0012-azure-blob-como-primer-proveedor-de-archivos.md
[ADR 0018]: ../../decisions/0018-robustez-proporcional-al-efecto.md
[validar un cambio]: ../../guides/validar-un-cambio.md
