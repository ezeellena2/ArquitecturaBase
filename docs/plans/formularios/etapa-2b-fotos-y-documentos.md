# Etapa 2B — Fotos y documentos

Estado 2026-10-15: implementación local de archivos y F1–F5 terminada; pendientes el piloto conjunto de Meta y la verificación contra Azure real para cerrar la etapa. La Etapa 2 se cerró con el piloto real. Tableros F1–F5 elegidos. Por decisión del usuario, el desarrollo usa Azurite hasta que cree la cuenta de Azure Storage; la prueba contra Azure real se hace al cerrar esta etapa ([guía local][azure]).

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
- Piloto real con Meta: una foto con cámara, un PDF y una pregunta de archivo opcional que se deja vacía, desde el teléfono vinculado y visibles tras recargar. Comprobar el formato real de esa ausencia; no asumir null, cadena vacía ni array vacío.

## Entrega local — 2026-10-15

- Implementados almacenamiento privado/Azurite, editor bilingüe, compilador nativo r6, recepción completa con verificación, visor/descarga con permiso y limpieza de huérfanos. Las reglas vigentes se consolidaron en [formularios](../../features/formularios.md#fotos-y-documentos--implementación-local-2b-2026-10-15), [WhatsApp](../../features/whatsapp.md#fotos-y-documentos-en-flows--2b-2026-10-15) y la [guía Azure][azure]. No se agregaron staging, versiones generales ni protocolo durable para lecturas/subidas idempotentes.
- Backend: build sin advertencias y `dotnet test` completo **2857/2857**, sin omitidos, con PostgreSQL/Redis/Azurite. El recorrido verifica foto/PDF atómicos, recarga, 401/403/404, integridad/tipo/tamaño, Meta caído/vencido con reintentos acotados, número sin saltos al rechazar, limpieza de huérfanos y conservación tras la retención del chat. Aspire apagado con `aspire stop`.
- Front: build/lint limpios, arquitectura 48/48, tests 1681/1681 y visual 434/434. Fixtures copiadas de cuerpos HTTP de la integración; variantes es/en a 1440/390/320. En el navegador/API final se creó, guardó, recargó y editó el borrador bilingüe «Piloto 2B · Fotos y documentos»: una foto de cámara y un documento obligatorios, todavía sin publicar. Los binarios/recepción se verifican con PostgreSQL/Azurite y Meta simulado; eso no acredita el teléfono físico.
- Aplicado lo ya previsto: topes 10/100 MiB/25 MiB, una pregunta de archivo por pantalla, permiso existente, claves determinísticas, reintentos acotados y retención ligada a la respuesta. Los puntos heredados de K6–K7 se corrigieron sólo en los archivos tocados (nombre de catálogo/revisión, redacción de valores y uso del guard de publicación); los demás no se amplían en esta entrega.
- Próximo piloto conjunto: agregar al borrador una tercera pregunta de foto opcional; publicar/activar, mandar desde el chat al teléfono vinculado, sacar una foto, adjuntar un PDF, dejar vacía la pregunta opcional y confirmar y comprobar número, miniatura, visor, descarga y recarga. Registrar aquí el resultado. Azure real se verifica cuando el usuario cree su cuenta. Sin estos dos checks no se declara cerrada la etapa.

## Cierre del code review — 2026-10-15

- Corregidos los puntos 1–14 del [review 2B](../../reviews/2026-10-15-code-review-etapa-2b.md), con regresiones y commits chicos. Incluye nombres seguros, límites previos a decodificar, captura acotada, lecturas/limpieza eficientes, 404 y los estados de archivos del front. Los puntos reservados a Etapa 3 no se amplían.
- Validación: backend **2857/2857**, sin omitidos, con PostgreSQL/Redis/Azurite y build sin advertencias; front **1681/1681**, arquitectura **48/48**, build/lint limpios y visual **434/434**, con revisión de capturas. Fixtures exportadas, sin reconstruir los cuerpos; recepción automática con foto, PDF y archivo opcional vacío. Aspire apagado.
- Siguen pendientes Meta físico (incluida la forma real de enviar el archivo opcional vacío) y Azure real. No se declara cerrada la etapa antes de esos checks.

## Pendientes que se arrastran

- La cuenta de Azure Storage de desarrollo la crea el usuario; sin ella no se prueba contra Azure.
- Elegir el antivirus con el despliegue, antes de producción.
- Si un cambio de esta etapa toca un archivo nombrado en «Con la 2B o al tocar esos archivos» del [code review de K6–K7](../../reviews/2026-10-14-code-review-etapa-2-k6-k7.md), se corrige ese punto en el mismo corte. El resto de esa lista no entra en la 2B.

[t]: ../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#los-tableros
[files]: ../2026-10-02-almacenamiento-de-archivos.md
[azure]: ../../guides/azure-blob-en-local.md
[ADR 0012]: ../../decisions/0012-azure-blob-como-primer-proveedor-de-archivos.md
[ADR 0018]: ../../decisions/0018-robustez-proporcional-al-efecto.md
[validar un cambio]: ../../guides/validar-un-cambio.md
