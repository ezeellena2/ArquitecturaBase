# Traspaso 14 · 03 · WhatsApp

Cubre lo programado en WhatsApp desde el traspaso 13: backend `23797fff..HEAD` y front `7b95095b..HEAD` (14 al 16 de
noviembre de 2026). El 13 ya contó la revisión de arquitectura y las etapas 1 a 5 de sus arreglos; acá va sólo lo posterior:
la etapa 6 (H13, H19 y H16, el contenido del chat cifrado), la etapa 7 (H25 con N27, la regla de versiones al desplegar), los
tres avisos menores y lo que cambió en WhatsApp por arreglos de otras áreas.

**Todo está commiteado, pero no pasó la prueba integral**, que se hace con el usuario después del cierre. No es código para
copiar (decisión 18 del multitenant): se pasan reglas y decisiones.

Fuentes: [`docs/features/whatsapp.md`](../../features/whatsapp.md), [`whatsapp-servicios.md`](../../features/whatsapp-servicios.md),
el [ADR 0030](../../decisions/0030-contenido-del-chat-cifrado.md), la [revisión](../../reviews/2026-11-12-arquitectura-whatsapp.md)
(hallazgos y [decisiones](../../reviews/2026-11-12-arquitectura-whatsapp.md#decisiones-del-2026-11-13)) y las tareas 19 a 22
del [plan de la bandeja](../../plans/2026-10-01-bandeja-whatsapp.md#etapa-6-datos-y-privacidad).

## 1. Etapa 6: datos y privacidad (2026-11-14)

### H13. La ubicación y el texto del envío vencen (`379b38fc`)

[Hallazgo](../../reviews/2026-11-12-arquitectura-whatsapp.md#h13-la-ubicación-y-el-texto-del-envío-no-vencen) · [tarea 19](../../plans/2026-10-01-bandeja-whatsapp.md#etapa-6-datos-y-privacidad).

- Cuando un formulario se manda por WhatsApp, el envío guardaba el punto de la ubicación pedida y el texto del mensaje. Ahora
  la retención diaria los vacía en la pasada siguiente a que se cierra la captura, o cuando el envío vence sin una respuesta en
  camino. Se conserva el estado de la ubicación (para saber qué pasó), no el punto.
- Si Meta entrega una respuesta después de vaciado, queda sin reconocer, igual que con el contenido vencido.
- Se vacía por lotes de hasta 500, en el mismo trabajo que ya vaciaba lo compartido por la persona.

### H19. Quién cambió el modo, y una traza (`dbc81d12`)

[Hallazgo](../../reviews/2026-11-12-arquitectura-whatsapp.md#h19-no-queda-quién-cambió-el-modo-ni-una-traza-de-punta-a-punta) · [tarea 21](../../plans/2026-10-01-bandeja-whatsapp.md#etapa-6-datos-y-privacidad).

- Cada cambio de modo del chat (Asistente o Atención manual) queda en un historial con **quién y cuándo**; al derivar el
  asistente, sin persona. No sirve la auditoría del contacto, porque cada mensaje entrante la cambia.
- Cada mensaje y cada salida guardan, al crearse, el contexto de traza de donde se escribieron. Una traza une el webhook, los
  workers y la llamada a Meta, y sigue hasta el motor de IA (I10, del 05). Las etiquetas no llevan números, cuerpos ni ids.
- Todavía no: la captura de los Flows (con su rechazo y su reenvío) y la cola de acceso no continúan la traza.

### H16. El contenido del chat se guarda cifrado (`1a538093`, `e8f91535`, `e355c639`, [ADR 0030](../../decisions/0030-contenido-del-chat-cifrado.md))

[Hallazgo](../../reviews/2026-11-12-arquitectura-whatsapp.md#h16-los-chats-se-guardan-en-claro) · [tarea 20](../../plans/2026-10-01-bandeja-whatsapp.md#etapa-6-datos-y-privacidad).
Decisión del usuario del 13/11: se cifra todo el contenido; el nombre y el número quedan en claro para la bandeja.

- **La amenaza** es una copia de la base (un respaldo, un usuario de reportes): leía 90 días de textos, transcripciones,
  ubicaciones, filas de planillas y avisos con valores que la app oculta a quien no tiene `formrecords.read`. Las claves viven en
  la misma base, pero un despliegue las protege con un certificado externo, así que una copia sola no alcanza para leer.
- **Qué se cifra** (Data Protection, un propósito propio, `ArquitecturaBase.WhatsApp.ChatContent.v1`):
  - mensajes: texto, títulos de los botones, lo que respondió el asistente, filas de planilla, lugar y ubicación, contacto
    compartido, nombres de archivo, y lo preparado o leído de planillas y documentos;
  - salidas: texto, botones, lista, pie, filas y nombre del documento;
  - sesiones de envío: texto del Flow y punto de la ubicación;
  - el desenlace de cada pedido al asistente.
- **Qué queda en claro:** nombre de perfil y número de cada chat, los identificadores técnicos (Meta, botones, archivos), los
  estados, las fechas y los datos del negocio (el motivo de un caso, el nombre del formulario, la pregunta rechazada, el número
  de registro).
- **Cómo se nota:** el contenido ya no se busca ni se compara en SQL. Un cuerpo recibido en blanco se guarda como nulo. La vista
  previa del chat y el historial se leen igual.
- **Lo anterior se vació, con la confirmación del usuario.** La migración (en tres partes, una por tabla) cambia el tipo de las
  columnas y deja vacío lo que había, como hace la retención: los chats y sus mensajes siguen, sin el contenido. Se hizo así
  porque acá sólo hay datos de desarrollo. Vacía más que la retención: salidas en cola (que después fallan), botones de lo ya
  enviado y lo que se procesa en el momento (planillas y documentos en curso). **Una instalación con chats reales tiene que
  convertir los datos antes de migrar**; conviene migrar sin salidas en cola ni conversaciones del asistente en curso. El Down
  se niega mientras haya contenido cifrado.
- **Si se pierden las claves,** el contenido de los últimos 90 días queda ilegible: se lee como vacío (como si lo hubiera vaciado
  la retención) en lugar de romper la carga del chat, y el asistente trata lo preparado o leído que ya no abre como un dato
  dañado. Lo mismo pasa con un respaldo restaurado sin el certificado. El anillo de claves lo comparten todas las copias.
- **Límite conocido:** el cifrado no se ata a la fila; un cifrado copiado a otra fila se lee. Alcanza contra una copia de la
  base, no contra quien puede escribir en ella.
- Estas migraciones no conviven con la imagen anterior: entre el bundle y la imagen nueva, la anterior falla al leer o guardar
  ese contenido ([despliegue](../../guides/despliegue.md#el-orden-bundle-después-imagen)).

## 2. Etapa 7: versiones que conviven al desplegar (H25 y N27, `42d8f47f`)

[Hallazgo H25](../../reviews/2026-11-12-arquitectura-whatsapp.md#h25-la-versión-vieja-no-entiende-lo-que-escribe-la-nueva) · [tarea 22](../../plans/2026-10-01-bandeja-whatsapp.md#etapa-7-despliegues-sin-romper).
N27 es el mismo problema en el núcleo; se resolvió con **una sola regla para la cola durable, el correo y WhatsApp**.

**La regla.** Mientras conviven dos versiones (un despliegue, o volver a la imagen anterior), una copia **deja para otra lo que
su versión no sabe leer**, con un tope; no lo borra, no lo falla de inmediato y no lo manda como si lo entendiera.

- Un mensaje de un tipo que no conoce se lee como «Otro»: el historial abre; la columna conserva el tipo. Un entrante así queda
  para la copia que lo conoce.
- Una salida de un tipo que no conoce vuelve a pendiente, en su lugar, sin gastar reintentos, hasta diez minutos desde que se
  pidió. Después falla como una que no salió (un aviso, con su código); nunca sale como texto.
- Una respuesta de Flow que escribió otra versión (un formato nuevo, un miembro o una forma desconocidos) queda pendiente, con su
  envío abierto, hasta cinco fallas y un día, y recién ahí se rechaza.
- La cola de acceso y la del correo: lo que no se descifra o no se lee vuelve a la cola con espera, hasta diez veces; cada copia
  reserva sólo los tipos que conoce. Un motivo de lectura de documento desconocido se lee como «Otro»; una legibilidad
  desconocida deja la lectura para la copia que la conoce (N27, `93f643c1`).
- **Para desplegar sin corte:** cada campo o valor nuevo (un tipo, un formato sellado, un perfil, un proveedor) se **escribe un
  despliegue después del que ya sabe leerlo**. La guía de despliegue y la de migraciones lo piden.
- **Todavía no** (escrito en whatsapp.md): un formulario preparado con otro perfil de Flows queda fallido si lo publica una copia
  que no lo conoce; una respuesta cuyo envío escribió otra versión queda sin reconocer; un Flow que preparó otra versión falla al
  despacharse; y un valor nuevo de otro enum guardado como texto (origen o estado de una salida, modo del contacto, origen,
  propósito o estado de un mensaje) rompe su lectura y, en la salida, frena la cola.

## 3. Tres avisos menores (`7601b1e5`)

Quedaron del reporte de los arreglos de la revisión (H5 y H21):

- Un mensaje que nunca es fuente de un pedido (sticker, video, lugar, contacto, reacción) ya no cuenta como «el último»: no
  reemplaza el pedido en curso ni impide derivar cuando el motor no contesta.
- Si el motor no puede continuar una conversación (no la encuentra), el chat deriva con «El asistente no pudo responder» en vez
  de quedar pendiente y reenviarse en cada vuelta.
- Del «¿La emito?» sólo decide el botón del mensaje que se espera ahora: después de «Volver a probar», un botón viejo no decide
  nada aunque diga lo mismo. El mismo toque repetido sigue recibiendo la misma respuesta (H21).

## 4. Lo que cambió en WhatsApp por arreglos de otras áreas

- **Sin Formularios, el asistente funciona (B5 del núcleo, `3083af18`).** El contexto del asistente (conversación en Asistente,
  Atención manual y «Probar») pedía siempre las capacidades de formularios, y el motor rechaza el contexto entero si nadie las
  declara: sin Formularios ninguna conversación automática recibía respuesta. Ahora las pide sólo si un módulo instalado las
  declara por el puerto neutral; las propias de WhatsApp no se filtran. Es la regla de «el núcleo no nombra módulos».
- **Cada medio de ingreso confirma los cambios del otro (N11, `531d55c0`, front `5b72690c`).** Con un correo verificado, vincular
  o desvincular un número desde «Mi perfil» pide el código que llega al correo; agregar un correo con número verificado pide el
  código que llega por WhatsApp (ruta nueva `POST /api/me/whatsapp/confirmation-code`, plantilla de código). Un número verificado
  no se reemplaza desde el perfil (`409 Users.User.PhoneAlreadySet`). La administración no cambia. Detalle en el 02.
- **El límite del webhook agrupa una IPv6 por su /64** (N3 y N20, `64b6ae9e`), igual que los del núcleo.
- **Respuesta rechazada (N22, front `6a71e6ef`):** la tarjeta distingue «No pudimos bajar los archivos de WhatsApp.» de «No pudimos
  guardar los archivos.» (almacenamiento); los dos con «Mandar de nuevo».
- **Documentos (F6, `ca730ae4`, `7a30c616`):** una toma que se corta antes de volver devuelve su cuenta, y un PDF armado para
  atacar no frena la lectura. Los cuenta el 02.
- **Ayuda del decimal en el Flow (F1):** el texto del Flow sigue la región del idioma; ver el 06.

## Para el multitenant

**Dónde cae.** Es la etapa de WhatsApp del multitenant, sobre lo que ya tenga del traspaso 13. El cifrado, la traza y las
versiones son de plataforma; lo demás es producto.

**Qué tiene que adaptar:**

- **Cifrado por organización.** ArquitecturaBase cifra con un solo anillo de claves por instalación. En el multitenant el
  propósito puede incluir la organización (o una clave por organización): así el contenido de una no se lee con la clave de otra,
  y borrar una organización es perder su clave. Cambia qué significa «claves perdidas» (una organización, no toda la base) y la
  rotación.
- **El nombre y el número en claro** tienen que quedar detrás de RLS: son lo único que la bandeja busca en SQL.
- **El historial de modo (H19)** guarda quién: acá es un usuario; allá, un usuario dentro de una organización (y quizás un
  cliente o empresa). La traza no debe llevar ids de organización en etiquetas abiertas.
- **H25 y N27** valen igual con RLS: una copia que deja un entrante «para otra» tiene que hacerlo sin cruzar organizaciones.
- **Capacidades de formularios (B5):** las declara el feature gate de Formularios de la organización, no sólo el módulo instalado.
- Si el multitenant parte de cero en WhatsApp, no necesita la migración que vacía: nace con las columnas cifradas.

**Preguntas para el usuario de allá** (la opción de ArquitecturaBase, primero y recomendada):

1. ¿Cifrado con una clave por instalación, como acá, o una por organización? Recomendada: **una por instalación, con el propósito
   atado a la organización**; la clave por organización se suma si un cliente la exige.
2. ¿Qué pasa con un chat real ya guardado en claro al migrar? Recomendada: **convertirlo antes de migrar**; vaciarlo sólo si son
   datos de prueba, como acá.
3. ¿Se cifra todo el contenido del chat y quedan en claro nombre y número? Recomendada: **sí, igual**; el nombre y el número son
   lo que la bandeja muestra y busca.
4. ¿Cuándo vence el contenido? Recomendada: **90 días, igual que acá**, con el texto y la ubicación del envío vaciados al cerrar
   la captura; si una organización quiere otro plazo, es configuración, no código.
5. ¿Se adopta ya la regla de desplegar «un campo nuevo, un despliegue después de leerlo»? Recomendada: **sí, desde el primer
   despliegue**, porque después de producción es más caro.
