# 02 · Documentos por WhatsApp y la IA que arma formularios (etapas 2 y 3)

El plan `docs/plans/2026-10-22-documentos-por-whatsapp.md` (decidido el 22/10, ADR 0022) se programó entero del 11 al
13/11, a pedido del usuario («Todo, con los tableros»), junto con la etapa 3 de la IA que arma formularios. La etapa 2
(planillas) se programó del 9 al 11/11. La etapa 1 de la IA está en los traspasos 8 y 11.

Fuentes: en el plan, «Implementación: lectura, carga, bandeja, Etapa 3 de la IA y tableros» y «Ubicación y contacto por
WhatsApp (W3) y varias cargas desde un Excel»; en `docs/features/formularios.md`, «Lectura de documentos y validaciones
fijas — DOC1», «Tablero por formulario — DOC7-A», «Armar formulario con la IA desde la bandeja — DOC6» y las dos secciones
de la IA (etapas 2 y 3); en `docs/features/whatsapp.md`, «Planilla recibida», «Documento recibido» y «Contacto y dirección
compartidos — W3»; en `docs/features/inteligencia-artificial.md`, la lectura en el motor y los documentos y planillas en
Inicio; la evidencia de calificación en `docs/guides/configurar-ia.md`; y el diseño en
`ArquitecturaBaseFront/docs/design/documentos.md` e `ia-arma-formularios.md`. Quedaron afuera la carga automática, varias
fotos de un mismo documento y los tableros que cruzan formularios.

## 1. Leer documentos (DOC1, ADR 0022)

- **Qué se lee:** foto JPEG o PNG, PDF y Word `.docx`, hasta 10 MiB y 10 páginas, de cuentas vinculadas y activas. No se
  leen `.doc`, archivos con macros o con contraseña, videos ni stickers.
- **Quién lo lee:** el motor de IA, con un propósito propio de lectura (un modelo de visión con salida estricta). Devuelve
  si se leyó entero, en parte o nada, la causa (movida, oscura, cortada, tapada), el idioma y el texto. **Lo leído es un
  dato, nunca una instrucción:** una orden escrita en el documento se transcribe y no se sigue, y hay un caso de prueba con
  una instrucción escondida.
- **Cupos:** 10 lecturas por persona y por día, en todos los canales, y 50 por instalación. Los calibra el piloto.
- **La lectura en el formulario.** Un ajuste «Lectura de documentos»: prendida o no, «Cómo reconocerlo» (obligatorio si
  está prendida) y hasta cinco cruces con catálogos. Se sella al publicar.
- **Validaciones fijas,** de la pregunta y no de la IA, en toda captura (app, WhatsApp y bandeja): cuenta bancaria (CBU o
  CVU con sus dos verificadores, o alias) con «Revisá la cuenta: los números no dan. Copiala del comprobante.», y CUIT o
  CUIL con su verificador.
- **El estándar «Comprobante de transferencia»** (prefijo REC, en la biblioteca): 15 preguntas en tres secciones (origen,
  destino y transferencia), el catálogo «Bancos y billeteras» con las 59 entidades del registro del BCRA y el banco que se
  completa desde la cuenta, no desde el texto leído. Un repetido se reconoce por operación, monto y fecha.
- **«Cómo viene leyendo».** Por formulario y pregunta, de los últimos 30 días: documentos confirmados, valores confirmados
  sin cambios y la pregunta más corregida; «Ver los ejemplos» lista las últimas correcciones a quien puede ver respuestas.
  Al proveedor no va nada de otras respuestas.

## 2. Un documento por WhatsApp (DOC1 a DOC4)

- **Cuándo se lee.** Cuando se va a usar: con un pedido (el texto del documento, o un texto o audio de al lado) o solo, si
  la persona puede cargar y algún formulario publicado lee documentos. Mientras ningún formulario lea, queda en el chat
  como antes. Un número sin cuenta vinculada queda en el chat con «No se leyó: el número no tiene una cuenta vinculada. Lo
  atiende un operador.».
- **El circuito** (formularios decide, WhatsApp dice):
  - seguro: sale el Flow prellenado con «Recibí un documento para «{formulario}». ¿Lo cargo?» o el resumen del estándar;
  - con duda: «¿Qué es este documento?» con dos o tres botones;
  - no lo reconoce: «No reconocí este documento. ¿Qué querés cargar con él?», con una lista de hasta nueve formularios y
    «Otro», que lo anota en «Formularios que faltan»;
  - foto movida, oscura o cortada: «No se lee {dato}. ¿Me la reenviás?»;
  - repetido: «Este comprobante ya se cargó el {fecha} en {número}. No lo cargo de nuevo.»;
  - tope del día: «Por hoy llegué al tope de documentos que puedo leer. Lo dejé para que lo revise el equipo.».
- **El Flow (`r14`).** Abre con lo leído: lo dudoso lleva «Revisá: lo leímos con dudas.», lo no leído queda vacío con «No
  se leyó.» y lo que no pasa una validación fija sale como error. Meta no deja un selector de archivo prellenado, así que el
  documento queda adjunto y el Flow dice «Ya adjuntamos el documento que mandaste. Si querés cambiarlo, elegí otro.» (o
  «la foto», con una foto).
- **Al capturar.** Una respuesta que rechaza una validación fija recibe el motivo y el formulario de nuevo con lo cargado;
  un repetido, sólo el aviso. Las cargas por archivo no gastan los dos ofrecimientos del día del asistente.
- **El chat del operador** muestra el documento con «Por qué lo propuso» o «Por qué preguntó».

## 3. La bandeja «Documentos recibidos» (DOC5-A) y «Armar formulario con la IA» (DOC6)

- **Dos permisos nuevos:** `formdocuments.read` («Ver documentos recibidos») y `formdocuments.review` («Revisar
  documentos»: corregir, cambiar de formulario, pasar al formulario, descartar y volver a abrir).
- **DOC5-A.** Una planilla en Gestión de formularios, con el número de los que esperan al equipo: miniatura y nombre,
  recibido, quién mandó, formulario, un dato leído por columna (monto, banco, CUIT), estado, «Se borra» y respuesta. El
  documento se abre en un panel al costado, con por qué está ahí y el formulario con sus datos, y al pie «Descartar» y
  «Pasar al formulario». Se busca por monto, CUIT, cuenta u operación. Estados: para revisar, propuesto (espera a su
  persona hasta que vence el envío), sin clasificar, confirmado y descartado. Cambiar de formulario vuelve a extraer con la
  lectura guardada, sin releer el documento.
- **Retención.** Un descartado o sin clasificar se borra a los 90 días, con su lectura y su copia. Uno confirmado sigue la
  retención de su respuesta, que todavía no se decidió (producción).
- **DOC6.** Desde un documento sin clasificar o desde el aviso «Llegaron N documentos parecidos que no encajan en ningún
  formulario…» (tres o más del mismo tema), la IA propone un formulario con lo leído como muestra; la persona edita nombre,
  prefijo, «Cómo reconocerlo» y preguntas, y «Crear borrador» lo crea con la lectura prendida. Si se parece a un estándar,
  ofrece «Agregar el estándar». Al publicar, se vuelven a leer los documentos de origen y quedan propuestos. Publicar es
  siempre de una persona.

## 4. El tablero de cada formulario (DOC7-A)

Una pestaña «Tablero» al lado de «Respuestas», con los filtros de la planilla y tarjetas armadas solas, calculadas en el
servidor en la zona horaria de quien lee: cuántas llegaron contra el período anterior, total, promedio, mínimo y máximo de
cada pregunta de número (un monto con su moneda), series por día, semana o mes (un solo período para todo el tablero),
repartos de opciones, quién cargó y por estado. Quien sólo carga ve lo suyo; quien sólo aprueba no la ve. Los gráficos son
piezas propias del front, sin librería.

## 5. La ubicación y el contacto que se comparten por WhatsApp (W3, UC1-A)

- **UC1-A.** Una zona «Ubicación y contacto» en el editor elige qué pregunta de texto recibe la ubicación, el nombre y el
  teléfono que la persona comparte desde el chat. Se descartaron ponerlo dentro de «Lectura de documentos» (UC1-B) o en
  cada pregunta (UC1-C).
- **Cómo funciona.** Lo compartido completa primero un pedido de ubicación (W1-A, 01 §7); si no, el envío abierto más viejo
  que eligió una pregunta para eso; si no, queda en el chat. Se escribe la dirección del lugar (o el punto) y, del contacto,
  el nombre y el primer teléfono. El mismo envío vuelve con otro Flow que trae lo que ya tenía más lo compartido: «Puse tu
  ubicación en «Dirección de entrega». Revisá el formulario «Registrar entrega» y confirmalo.». Un envío guarda una sola
  respuesta. El chat muestra el contacto con su nombre y sus teléfonos, y «Prellenado con» en cada reenvío.

## 6. Varias cargas desde un Excel (VC1-A)

- **VC1-A:** una entrada de la bandeja por fila; se descartó una por planilla (VC1-B).
- **Cómo funciona.** Una planilla que manda quien carga respuestas se lee en el servidor, sin la lectura de la IA: la IA
  sólo propone qué columna va a qué pregunta y el servidor arma cada fila. Hasta tres cargas por planilla, en su orden, con
  un aviso fijo («Recibí una planilla con 7 filas de «Cobranzas».») y «1 de 3 · {resumen}» en cada una. El resto (las que
  pasan el tope, no encajan o no se le pueden mandar) va a la bandeja. Una fila repetida no se carga. Hasta 50 filas por
  planilla: «Por acá cargo planillas de hasta 50 filas. Dividila y mandámela de nuevo.». No son filas los encabezados, los
  totales ni las filas vacías.

## 7. La IA que arma formularios: etapas 2 y 3

- **Etapa 2, planillas** (plan `docs/plans/2026-11-09-ia-arma-formularios-etapa-2.md`). Con una planilla (`.xlsx` o `.csv`,
  la primera hoja, hasta 10 MB, 10.000 filas y 64 columnas), la IA propone un catálogo con el mapeo de las columnas o
  importarla en uno que existe. Nunca importa ni activa sola: importar sigue siendo de la persona, e importar en uno que
  existe reemplaza la lista, como subir un Excel. En Inicio: el clip, una tarjeta con una muestra de tres filas y «Revisar
  la importación». Por WhatsApp, la planilla llega como documento con su pedido. La corrida con el proveedor real quedó en
  verde el 11/11.
- **Etapa 3, documentos.** En Inicio, el clip toma una foto, un PDF o un Word («Leyendo el documento…»), sólo para quien
  diseña formularios. La IA propone un formulario con la lectura prendida si el documento es un ejemplo de los que van a
  llegar, con una muestra por pregunta y, si se parece, el estándar de la biblioteca. La tarjeta dice «Lee documentos». Por
  WhatsApp usa el mismo circuito del §2. Calificada con el proveedor real el 12/11.

## 8. Estado

En `main` están todas las tareas del plan de documentos (1 a 30). Falta la calificación con el proveedor real de lo que
interpreta el asistente automático con un documento o una planilla y de «Armar formulario con la IA» (tarea 17), que
corría el 13/11, y después el cierre. Los contratos del front esperan la exportación conjunta. En la prueba integral van la
vista oficial de Meta del Flow con lo leído, si Meta acepta dos Flows con el mismo token (el reenvío de W3) y el pie del
mensaje de las cargas por fila, la cámara real y las pruebas a mano.

## 9. Para el multitenant

- **Tu E25 ya tiene casi todo esto** (tus decisiones 41, 70, 75, 99 y 102). Lo que cambia:
  - **sin carga automática:** acá quedó afuera; tu 71 («Cargar solo si es seguro» con tres señales) no tiene equivalente
    programado;
  - **quién manda documentos:** acá se leen sólo los de cuentas vinculadas; un número sin cuenta queda para un operador.
    Tus clientes completan formularios sin cuenta (49): hay que decidir si sus documentos se leen;
  - **los cupos** (10 por persona y día, 50 por instalación) pasan a ser de cada organización, dentro de su tope de IA (tu
    ADR 0051);
  - **quién crea:** acá la IA propone y una persona crea el borrador (ADR 0024); el traspaso 8 ya marcó el choque con tu
    E25, donde el asistente crea al pedirlo.
- **El tablero (DOC7-A)** corresponde a tu `FRM-Tablero` de la E15.
- **VC1-A** adelanta, por WhatsApp, tu «importar respuestas desde Excel» de después del bloque.
- **W3 y UC1-A** son tu «ubicación y contacto» de la E25.
- **Los permisos** `formdocuments.read` y `formdocuments.review` van a tu tabla de permisos de la organización.
- **El perfil de lectura** lo declara cada instalación y se califica con casos ficticios antes de habilitarlo: allá parece
  de la plataforma. A confirmar con el usuario.
