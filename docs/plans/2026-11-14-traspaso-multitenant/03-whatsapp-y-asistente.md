# 03 · WhatsApp y el asistente

Fuentes: `docs/features/whatsapp.md`, `docs/features/whatsapp-servicios.md` (nuevo, con el mecanismo de cada arreglo),
`docs/features/inteligencia-artificial.md`, la revisión `docs/reviews/2026-11-12-arquitectura-whatsapp.md` y, en
`docs/plans/2026-10-01-bandeja-whatsapp.md`, la sección «Arreglos de la revisión de arquitectura». Lo de documentos,
planillas, contacto y ubicación compartidos está en el 02, y el pedido de ubicación antes del Flow (W1-A), en el 01.

## 1. La revisión de arquitectura de WhatsApp (2026-11-12)

- **Qué fue.** La primera corrida de la guía de revisión (06, §3) sobre el módulo entero: nueve revisores, siete
  verificadores y un contraste independiente, sobre backend `9c0eb048`, sin ejecutar nada (`f7da5e4b`).
- **Qué dio.** Cumple en los tres horizontes el estado por pedido, las ediciones simultáneas, el paginado, las entradas
  hostiles y la integridad en la base. No cumplía el orden de los envíos, el estado incierto, la degradación cuando falla
  algo de afuera, la caché, la privacidad y el respaldo. Los hallazgos van de H1 a H34: H1 a H22 son de «hoy», H23 a H26
  de varias copias y H27 a H34 de gran escala.
- **Cuatro fallas funcionales, arregladas el 12/11** (backend `37135d7c` y `a50fd75f`; front `a27091de` y `c2cce54e`): las
  preguntas con botones de un caso no salían (B1), el resumen de importar no salía (B2), una pregunta de caso tapaba el
  último resumen (B3) y dos botones podían salir con el mismo título en el idioma en que salen (B4). Con H1, un aviso o
  una respuesta del asistente sin resolver ya no traban el cuadro de respuesta del operador.
- **Las seis decisiones del usuario (2026-11-13, `77de7dc1`), todas las recomendadas:**
  - horizonte: «hoy» (H2 a H22) más H25, H33 y la prioridad de H31; H23, H24 y H26 antes de pasar de una copia de la Api
    (hoy `--max-replicas 1`); el resto de gran escala, con volumen real;
  - H2: un envío incierto lo revisa cualquiera que puede enviar, y queda quién;
  - H5: si el motor de IA no contesta, el chat pasa a una persona;
  - H16: se cifra todo el contenido del chat; el nombre y el número quedan en claro para la bandeja;
  - H21: el mismo toque de un botón recibe la misma respuesta;
  - H31: lo que le contesta a alguien sale antes que los avisos de las reglas.
- **Separar WhatsApp como servicio** no es un defecto: el informe hace el inventario (los lectores que unen cuentas y roles
  del núcleo, el código de ingreso con la cola de acceso y la invitación, y la confirmación del número dentro del guardado
  del núcleo). Separarlo pediría un ADR nuevo; el 0007 lo descarta por prematuro. La frontera con Formularios y la IA está
  limpia.
- **Sin arreglar a propósito:** borrar o anonimizar a una persona (H14) quedó en `docs/plans/verificacion-y-produccion.md`.

## 2. Los arreglos de «hoy» (plan `be294cdb`, 22 tareas en 7 etapas)

Las etapas 1 a 5 están commiteadas hasta `23797fff` (14/11). Lo que cambia para el producto o la operación:

| Arreglo | Qué cambia | Commits |
| --- | --- | --- |
| **H18** | El perfil del Flow `r15` pide coma decimal en español: «Ingresá el número sin separador de miles; usá coma para los decimales.»; en inglés, punto. El control acepta las dos marcas. Los Flows ya publicados conservan su texto hasta republicarse. La invitación manda el botón con su idioma (`WANT_TO_ENTER:es` o `:en`) y la respuesta sale en ese idioma. | `5372b7b4`, `c0caf0ad` |
| **H17, H22, H15** | La paridad de todos los `.resx` (06); quitar WhatsApp cuenta diez tablas, con la del asistente; el respaldo y la restauración (06, §4). | `64d878a0`, `ac5403bc`, `54b213a4` |
| **H20** | «Crear» desde un resumen del asistente revisa antes que el chat siga en Asistente, con el mismo número, la misma cuenta y la misma revisión del vínculo; si no, no crea nada. | `c7798d5d` |
| **H4** | «Probar» el asistente sigue los permisos de quien prueba: pide también ver los chats, y sin ver respuestas no muestra el porqué ni lo que llenaría un formulario. | `61de6935`, front `b2ea62b7` |
| **H10** | Topes de códigos: 10 por cuenta y por día para vincular un número desde el perfil, 20 por dirección con el registro abierto (una IPv6 cuenta por su /64) y el global de 100 que ya existía. Se gastan sólo si el código salió. | `ac5b5ca8` |
| **H2** | Un envío incierto del chat lo revisa cualquier operador que puede enviar, por el id del envío, y queda quién y cuándo: «Lo revisaste vos» o «Lo revisó otro operador». Revisar no reenvía ni afirma que llegó. | `3e750848`, front `a202c056` |
| **H6** | Las acciones de una respuesta del asistente salen en su orden. | `7bf19995` |
| **H12 y H31** | La salida elige el próximo chat sin esperar al que está en su pausa de 6 s, y lo que contesta a alguien (operador, asistente, confirmación de un toque) va antes que los avisos de las reglas. Dentro de un chat se mantiene el orden. | `b3c2fb5e` |
| **H8** | Apagar la Api no corta los envíos en curso: lo que esperaba su turno vuelve a la cola sin llamar a Meta, y lo que se corta después de llamar queda incierto y nunca se repite. | `79925e34` |
| **H7** | Se separa «no salió» de «no se sabe». No salió (no se conectó, Meta limitó, 429, ciertos 5xx con código): se reintenta a los 30 s, dos veces como mucho, y ese chat espera para no desordenarse. No se sabe (timeout, respuesta cortada, 5xx sin código): queda incierto y no se repite. Una invitación incierta dice «Sin confirmar» en Usuarios y pide revisar con la persona antes de reenviarla. | `83e53357`, front `85fa7666` |
| **H11** | Lo recibido con archivos (respuesta de un Flow, audio, planilla, documento) aguanta un día de caída de Meta, con esperas que crecen hasta una hora, y sólo se abandona con cinco fallas y el día cumplido. | `4f2df684` |
| **H33** | «Enviar» un formulario no le pregunta a Meta por la cuenta; lo verifica sólo el worker antes de mandar el Flow. | `2232dfe7` |
| **H3** | Con Redis caído no se pierden mensajes: sin turno, la salida espera 30 s sin gastar un reintento, y la cola de acceso vuelve a pedir el turno hasta que el mensaje vence. | `8303e7d4` |
| **H9** | Un envío fallido guarda un código estable (`WhatsApp.Send.NotAllowed`, `WindowClosed`, `Undeliverable`, `RateLimited`, `TokenInvalid`, `PermissionMissing`, `Unavailable`, `Rejected`) y el chat lo muestra debajo de «No se pudo enviar», para saber cuál repetir. Los logs llevan el motivo sin el número; `/health` nombra el permiso de Meta que falta y hay métricas de resultados (`whatsapp.outgoing.outcomes`, `whatsapp.access.outcomes`) y de pendientes. | `0097643b`, front `9cb1f971` |
| **H5** | Si el motor de IA no contesta un mensaje de la persona (falló, venció o quedó incierto), el chat pasa a Atención manual con «Necesita una persona» y «El asistente no pudo responder», y sale el mensaje al derivar si la ventana de 24 horas sigue abierta. No deriva lo que todavía puede reintentarse ni un chat que ya tomó una persona. | `8c357060`, front `7b95095b` |
| **H21** | El dueño de un toque guarda su id con la decisión: el mismo toque recibe la misma respuesta, otro toque sobre el mismo aviso no decide otra vez, y cada confirmación sale apenas decide el dueño. | `852857fa`, `23797fff` |

**En curso al escribir esto** (no los des por hechos):
- **Etapa 6:** H13 (vaciar la ubicación y el texto de un envío cuando ya no hacen falta), H16 (cifrar todo el contenido del
  chat con Data Protection, con un ADR nuevo) y H19 (quién cambió el modo y cuándo, y trazas del módulo). Para H16 el
  usuario eligió **vaciar lo ya guardado**, como la retención, en vez de convertirlo, porque acá sólo hay datos de
  desarrollo: **una instalación con chats reales tiene que convertirlos**.
- **Etapa 7:** H25, que una copia de la Api deje para otra lo que su versión todavía no entiende durante un despliegue.

## 3. Casos del asistente (AS1-A a AS5), programados el 11 y el 12/11

El asistente de WhatsApp ya atiende con casos configurables (plan `docs/plans/2026-10-21-casos-del-asistente.md`; reglas
en `whatsapp.md`, «Casos del asistente — 2026-11-11», y en `inteligencia-artificial.md`, «Hechos confirmados para los
canales» y «Lecturas precargadas y varias propuestas»; diseño en `ArquitecturaBaseFront/docs/design/asistente-casos.md`).

- **La pantalla «Asistente» (AS1-A).** En WhatsApp, con cuatro pestañas que se guardan juntas, con revisión y 409:
  - **Perfil:** nombre, presentación, tono, qué hace y qué no, y el mensaje al derivar.
  - **Conocimiento.**
  - **Casos**, en su orden: cuándo aplica, tipo de entrada, activo y acción, más «Cualquier otra cosa», que no se borra.
  - **Límites:** temas que no responde, lo que no puede prometer, el largo máximo (100 a 1000) y las respuestas por
    conversación y día (1 a 50).

  La configuración vive en la base, no en el panel del proveedor de IA. Los textos que salen tal cual tienen el original
  obligatorio y la traducción opcional. Sin agente guardado, el asistente hace lo de antes.
- **Las acciones, un menú cerrado:** mandar un formulario, un texto exacto, responder con la información del negocio
  (redactado), preguntar con botones, derivar y no responder.
- **Cómo trabaja (AS2).** Espera 4 s sin mensajes nuevos, como mucho 15, y lleva la ráfaga entera. Con el historial de 24
  horas de esa conversación, contesta con una a tres acciones distintas, en orden: un mensaje que pide varias cosas recibe
  una por cada una. Un formulario va solo. Antes de encolar, el servidor revisa que el caso exista, esté activo y su
  acción esté permitida, y manda el texto actual del caso, no el que vio el modelo.
- **Redacta sólo con hechos confirmados.** Lee sólo los hechos confirmados de Información del negocio: no usa las
  preguntas sin respuesta ni textos sin revisar (la misma regla corrigió el Inicio). El texto redactado se revalida: largo,
  idioma, y enlaces, teléfonos, correos o precios sólo si están en los hechos, el perfil o el caso. Si no pasa, deriva
  con su código (`replyNotAllowed`, `replyTooLong`, `dailyReplyLimit`, `assistantFailed`, `button`).
- **Derivar y «Por qué respondió esto» (AS4).** Derivar pasa el chat a Atención manual, lo marca «Necesita una persona»
  con su motivo (un filtro nuevo del listado, con su cantidad) y manda el mensaje al derivar. La marca se va con la
  primera respuesta de una persona. Cada respuesta guarda el caso, la acción, el motivo y lo que usó, y el chat lo muestra
  en dos columnas. En Atención manual, la sugerencia puede ser un texto: «Usar como borrador» lo pone en el cuadro y nada
  sale solo.
- **Probar (AS3).** Hasta cinco mensajes de prueba contra la configuración, sin mandar nada ni gastar los topes de
  WhatsApp; el cupo del motor es de quien prueba.
- **El redactado, calificado (ADR 0021 enmendado).** La regla «no se envía texto libre del modelo» pasó a «sólo texto
  redactado dentro de estos límites, o fijo». El perfil del modelo pasó el set `channel-agent` del arnés (32 casos es/en)
  con el proveedor real el 12/11, y cada instalación declara qué perfil está calificado: sin eso, el motor no redacta.
- **Estado.** Programado en back y front. Faltan los 13 pasos de la prueba a mano, en la prueba integral, y el cierre del
  plan. Los varios agentes con ruteo («Tercera etapa: agentes») siguen sin programar.

## 4. Los perfiles nuevos del Flow

El texto de un Flow queda sellado en su JSON: los publicados no cambian hasta republicarse. Un perfil nuevo, sin lo que
suma, compila los mismos bytes que el anterior. El `r12` (el bloque de renglones al final) está en el traspaso 12.

| Perfil | Qué suma | Lo que exigió Meta |
| --- | --- | --- |
| `r11` (08/11) | Elegir primero por un campo (LD2-A): un desplegable de valores antes de la pregunta, que se llena con `update_data`. | Un desplegable obligatorio no puede ir oculto, así que la pregunta es obligatoria sólo mientras se ve. En una pantalla de renglón, el `navigate` pasa los datos vacíos; sin ellos, `INVALID_NAVIGATE_ACTION_PAYLOAD`. |
| `r13` (09/11, probado con Meta el 11/11) | Las preguntas calculadas no van en el Flow (las calcula el servidor) y las condiciones van dentro de un `If`. | Rechaza `\|\|` y `&&` entre comparaciones y el mismo control en dos ramas. Con varias respuestas que muestran una pregunta, se usa un booleano fijado con `update_data` en cada opción; una condición que depende de otra va anidada. |
| `r14` (13/11) | Abre con lo leído de un documento y sus marcas (02). | La elección entre pantallas gemelas va con un `If` sobre un booleano. |
| `r15` (13/11) | La coma decimal en español (H18). | Sin probar con Meta: va a la prueba integral, en la vista oficial y en el celular. |

## 5. Para el multitenant

- **Un asistente por organización.** El plan de casos lo dice tal cual. Tu E23 ya tiene casi todo lo de acá (seis
  acciones, ráfagas de 4 y 15 s, hasta tres acciones, el historial de 24 horas, derivar si no pasa el validador): sumá lo
  nuevo, que son los hechos confirmados como única fuente del redactado, la validación antes de encolar con sus códigos,
  «Probar», el borrador del operador, «Necesita una persona» y H5. Los varios agentes con ruteo son tu E24.
- **Quién califica el modelo.** Acá cada instalación declara el perfil calificado para redactar. En el multitenant parece
  de la plataforma, y el cupo de uso, de cada organización (tu ADR 0051). A confirmar con el usuario.
- **Una cuenta de WhatsApp por organización (tu E11).** Los arreglos suponen una cuenta por instalación. Con varias,
  `/health`, las métricas de H9 y la verificación de la cuenta de H33 van por organización; los topes de códigos de H10
  son de la plataforma, porque los códigos de ingreso no son de una organización.
- **El punto decimal.** Tu hoja de ruta (§4, «errores que ya pagamos») dice que la ayuda pide punto decimal. Acá, desde
  `r15`, en español se pide coma y el control acepta las dos. Es una decisión para el usuario.
- **El cifrado de H16** se suma a la RLS, no la reemplaza. Si allá hay chats reales cuando se adopte, la migración tiene
  que convertir lo guardado; acá lo vacía.
- **Lo incierto.** H2 (revisa cualquiera que puede enviar), H7 (separar «no salió» de «no se sabe», «Sin confirmar» en una
  invitación) y H21 (mismo toque, misma respuesta) son reglas de producto, no sólo de código: van a tus specs de la E11,
  la E14 y la E21.
