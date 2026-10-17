# Etapa 5 — Asistente, sugerencias y audios

Estado: en desarrollo · 2026-10-17. Los 18 arreglos del review de la Etapa 4 y D2 v79 están implementados y commiteados. Motor, prellenado, configuración de sugerencias y revisión de catálogos están implementados. Siguen la conexión completa con WhatsApp, los audios y los pedidos faltantes, conservando H1–H7 y s1–s4 y las decisiones de abajo.

## Qué se construye

Tableros: H1–H7, s1–s4 y el editor 03 en [Formularios por WhatsApp][tableros]. Todo uso del modelo pasa por el motor único del [ADR 0020][0020]: las capacidades leen o proponen; ejecuta el módulo dueño.

- **Motor único.**
  - Backend: generalizar el motor del [asistente de Inicio][ia] con canales: conversación en modo Asistente, sugerencia al operador en Atención manual y chat administrativo. BusinessForms y WhatsApp aportan capacidades tipadas (id con versión, permiso, lectura o propuesta, esquemas, presupuesto); reemplazan a los puertos `IStructuredIntentInference`, `IStructuredSuggestionSource` e `IStructuredCatalogProposalSource` del plan.
  - Casos de evaluación con datos ficticios para cada capacidad que decide sobre lenguaje.
- **Modo Asistente** (H1, H3).
  - Backend: «Bot / automatización sin configurar» pasa a ser Asistente. Lee, reconoce el formulario y lo manda solo, prellenado, por la cola existente (`Origin` Assistant, sin operador). Mandar no guarda nada.
  - Cambiar el modo, en la ruta existente, pide «Configurar el asistente». Los chats en Bot pasan a Atención manual; nadie queda en Asistente sin que lo elijan. Sin conexión de IA o sin el módulo, no se ofrece.
  - «Tomar atención manual» corta al asistente; solo termina el envío ya iniciado.
  - Front: autor «Asistente», «Por qué lo mandó», mensajes de reglas, marca en la lista de chats y confirmación al cambiar de modo.
- **Atención manual, solo sugiere** (H2): sugerencia arriba del compositor y diálogo con prellenados que se pueden destildar. Envía el operador.
- **«Cuándo lo ofrece el asistente»** (03): texto en «El formulario». Sin ese texto, el asistente no ofrece el formulario.
- **Límites** (H5): si no está seguro, pregunta con botones de respuesta rápida. Solo cuentas vinculadas, activas y con permiso; dentro de las 24 h. La respuesta registra que la mandó el asistente, por qué y qué propuso.
- **Sugerencias entre formularios** (s1–s4).
  - Backend: `BusinessFormSuggestions`: origen, destino publicado, «cuándo», condición opcional, modo Assistant o Always, mapeo entre preguntas del mismo tipo, activa y revisión. Las respuestas guardan `OriginRecordId`, `OriginSuggestionId` y qué se aceptó y mapeó.
  - Assistant: decide la IA. Always: se ofrece al cumplirse la condición, sin IA; en Atención manual confirma el operador.
  - Front: sección «Qué sugerir después» con «Guardar sugerencias» (s2); la cadena de origen en el detalle (s4).
- **Catálogo propuesto** (H4): pedido desde el chat administrativo («los supervisores activos»). BusinessForms valida y arma la vista previa; el catálogo queda inactivo hasta que una persona con `formcatalogs.manage` lo aprueba en la web.
- **Audios** (H6): se bajan como los archivos de la 2B y se transcriben con un perfil calificado. La transcripción entra al asistente como texto y el operador la ve en el chat, también en Atención manual.
- **Formularios que faltan** (H7): si la persona quiere registrar algo y ningún formulario corresponde, el asistente le dice que no hay uno y el pedido queda registrado. Quien administra formularios los ve agrupados por tema (cuántos pedidos, de cuántas personas y cuándo, sin nombres ni mensajes) y crea el formulario o descarta el pedido.

## Decisiones tomadas

No se vuelven a preguntar.

- El ADR 0020 se aceptó el 2026-10-16. Ejecutar una propuesta queda en el servicio del módulo dueño.
- En Asistente manda el formulario prellenado; en Atención manual solo sugiere. La IA nunca guarda ni confirma una respuesta: la confirma la persona en el formulario.
- No responde lo que no es un formulario: la conversación queda sin leer para un operador.
- Hasta 2 ofrecimientos por día por conversación. No repite uno rechazado; no abrirlo no es rechazarlo.
- Un solo botón, «Abrir formulario», también en las sugerencias.
- Sugerencias Assistant/Always, con guardado e historial propios, sin republicar. Sin circuitos, pasos ni estados de proceso.
- Catálogo del asistente: solo Filters, nunca SQL ([ADR 0019][0019]). Siempre lo aprueba una persona.
- Permiso «Configurar el asistente»: código `whatsapp.configureassistant`, Admin por seed.
- Proveedor: OpenAI para el asistente y para pasar audios a texto, con una sola cuenta (elegido el 2026-10-16). El modelo y el perfil de transcripción se califican al implementarlo. Audio y transcripción siguen la retención de los mensajes de WhatsApp. Sin transcripción, el audio queda para el operador y el asistente no lo interpreta.

## Fuera de esta etapa

- Borrador de formulario armado por IA a partir de los pedidos: después, siempre revisado y publicado por una persona.
- Responder consultas generales; SQL configurable en catálogos.
- Plantillas de Meta para escribir fuera de las 24 h.
- Capacidades que escriben (piden un ADR nuevo) e imágenes como entrada.

## Cómo se cierra

- Checks por corte y al cerrar, según [ADR 0018][0018] y [validar un cambio][validar]. Modelo, transcripción y envíos son efectos externos: operación durable del motor y cola de WhatsApp.
- Pruebas de arquitectura del ADR 0020: nadie llama al transporte del modelo fuera del motor; las capacidades no reciben `IUnitOfWork` ni servicios de escritura. Casos de evaluación corridos antes de habilitar un modelo, perfil o instrucciones.
- Docker: tope diario con tiempo inyectado y replay; respuesta tardía tras tomar Manual; propuesta con destino o valores no permitidos; permiso revocado; ofertas repetidas y bucles de sugerencias; IA no disponible.
- Prueba real con el proveedor de IA y Meta, incluido un audio transcripto. Los dobles no prueban la decisión.

## Requisitos previos

- Etapas 2, 2B, 3 y 4 cerradas: Filters, descarga de medios, detalle y cola con `Origin`/`Kind`.
- ADR 0020 aceptado y motor de Inicio generalizado con canales, conservando sus pruebas.
- La clave de OpenAI quedó cargada el 2026-10-16 (modelo `gpt-6.1-sol`, Disponible, prueba de conexión correcta). Falta calificar el perfil: hasta declarar `ArtificialIntelligence:Assistant:ModelProfiles` ([guía](../../guides/configurar-ia.md#habilitar-el-asistente)), Inicio responde «modelo no habilitado», como corresponde.
- El tope de 2 por día cuenta los formularios que el asistente ofrece o manda en una conversación, por día calendario en la zona horaria del negocio. Las sugerencias al operador en Atención manual no consumen cupo.
- «Cuándo lo ofrece el asistente» rige al guardarlo, sin publicar de nuevo, como las reglas.
- Permisos, con el formato de la [guía][permiso]: `formsuggestions.manage` y `whatsapp.configureassistant`.
- Los pedidos de formularios que faltan los guarda BusinessForms y se ven con `formtemplates.manage`.
- «Tomar atención manual» solo pide poder escribir en el chat (`whatsapp.send`): siempre se puede frenar al asistente. Pasar a Asistente pide `whatsapp.configureassistant`.

## Ajustes ya decididos (2026-10-11)

- **Idioma (2026-10-16):** nada de esta etapa exige traducciones. «Cuándo lo ofrece», los pedidos y los temas se escriben en un solo idioma; el asistente responde en el idioma de la conversación.

- H1–H7 y s1–s4 están elegidos (H6 y H7, el 2026-10-16).
- Si la persona quiere registrar algo y no hay un formulario que corresponda, el asistente avisa y registra el pedido. Cualquier otro mensaje (consultas o charla) no se contesta y queda sin leer para un operador. H5 se ajusta a esta regla.
- Un formulario que la persona no puede completar se trata como inexistente: no se revela que existe y se registra el pedido. H5 «Sin permiso» se ajusta a esta regla.
- **Recorrido guiado (2026-10-17):** se eligió el recorrido de punta a punta de la versión 78 del lienzo. Muestra una visita en ocho capítulos de las Etapas 1 a 5, y cada paso indica su etapa y qué hacen la persona, el sistema y la IA. Resume tableros ya elegidos: no suma decisiones.
- **Detalle de una respuesta (2026-10-17):** se eligió D2 de la versión 79 del lienzo y reemplaza el detalle de T3, G4 y G5. La respuesta se abre en un panel al costado de la planilla (o de Por aprobar), con ↑ ↓ para pasar a la anterior o la siguiente; la dirección la lleva abierta y en el celular ocupa la pantalla. El estado se arma con las reglas que tenía el formulario al llegar la respuesta y muestra momentos, no acciones sueltas: Recibida con su origen (operador o asistente), Por aprobar → Aprobada o Rechazada, y Al recibirla, Al aprobarla o Al rechazarla con el resumen de sus acciones. Lo que no aplica no se muestra, y mientras espera, Aprobar y Rechazar van en el renglón «Por aprobar». Pestañas Respuesta, Fotos, Actividad y PDF (solo con la copia fija). «Descargar todas las fotos juntas» queda para después.


## Avance

- **Audio en WhatsApp, backend listo:** 47413aab, 5c88d25f, e1505906, ee648e55, 4c302209 y b0c9ca2d separan recepción, copia privada, normalización durable, permisos, retención y reproducción. Domain15, parser6, storage12, Audio46, privacidad13, retención5, MIME14, SQL2 y CSP2 verdes; build sin advertencias. La transcripción usa el motor y la misma conexión. Quedan el consumo de AudioCompleted, la web y la calificación real; PostgreSQL/HTTP se corren al cierre.

- **Pedidos faltantes, backend listo:** fc8bec63, 4cb800fa, b6f8ce6c y 16f49609 agregan temas originales, recibo del mensaje, lectura combinada y validación del dueño antes de registrar. Replay no incrementa ni reabre; un pedido nuevo reabre. Domain12, capacidades16 y propietario20 verdes, BusinessForms657/657 y build limpio. El tablero usa Manage y no muestra mensajes ni personas; su web y PostgreSQL/HTTP siguen pendientes.

- **Detalle s4 listo:** front1a8b3be, 0c83036 y b0e9767 agregan valores propuestos plegables, mapeos/correcciones y cadena real dentro de D2. Focales43/43, arquitectura49/49, build y lint limpios; capturas físicas y navegador quedan para el cierre conjunto.

- **Detalle asistido y sugerencias entre respuestas, backend listo:** `21b66b6d`, `fee7e90d` y `86b03e6f` agregan preparación Always/Assistant, mapeos validados, lecturas observadas y cadena de registros reales con modo histórico. BusinessForms 618/618, frontera de capacidades 6/6 y build limpio. El idioma completo del prellenado viene del dueño (`7ab48f27`). El envío automático después de capturar, la web s4 y PostgreSQL siguen en desarrollo.

- **Aclaración y procedencia listas:** `4ba340a3` propone dos o tres referencias vigentes con una pregunta fija; `a9d2c1b9` conserva el canal de la ejecución y `fc5fe504` sella atribución, incluidos/cambiados y origen al confirmar. La captura usa su mismo límite y conserva replay. Las pruebas rápidas correspondientes están verdes; los botones y su consumo real siguen con WhatsApp.

- **Revisión manual en la web lista:** `1702daa`, `e693b7c` y `feb50b5` integran el preview y las exclusiones sobre C2, recuperación del envío compartido, explicaciones capturadas y retiro ante revocación. Focales 242 casos distintos, arquitectura 49/49 y build/lint limpios. La cola incorpora salidas Assistant sin operador (`c879ec22`), con 19 rojos previos y Domain WhatsApp 154/154 verde; continúa la revalidación y el worker.

- **Catálogo propuesto, backend listo:** `8b5aaa45`, `8667ac01` y `fbb7ff06` incorporan metadata sin personas, propuesta Filters observada, creación inactiva e idempotente, revisión y aprobación o descarte humano. Evidencia y decisión sobreviven al GET y a la edición; sólo el idioma original es obligatorio para H4 y el alta normal sigue bilingüe. Focales 71/71, arquitectura 156/156 y build sin advertencias. La web (`b027bb0`, `ec3c8a3`, `7318153`, `f34ca11`) conserva K4, revisión/409 y permisos: 122 casos rápidos, arquitectura 49/49, build y lint limpios. Migración y HTTP/PostgreSQL pendientes del cierre conjunto.

- **Audio en el motor listo:** `d36e26dc`, `6da8239f` y `b88a1332` reutilizan aceptación, perfil sellado, cuotas, worker y recuperación para transcribir con la misma conexión de OpenAI. Focales IA 195/195, transporte STT 37/37, adaptador 9/9, perfiles 8/8 y coordinador 2/2; arquitectura 156/156 y build limpio. Continúan adquisición/reproducción/retención en WhatsApp y la calificación real; todavía no se declara el perfil habilitado.

- **Prellenado y nueva publicación listos:** backend `39b73495` y `36e9ba6d` validan seis tipos escalares contra una lectura observada, la cuenta destinataria, la publicación y las opciones reales de catálogo. El perfil `flow-static-7.3-r8` fija los valores iniciales sólo para publicaciones nuevas; una publicación anterior debe republicarse para usarlo. BusinessForms 489/489, compilador de Flow 38/38, arquitectura 155/155 y build sin advertencias.

- **Configuración de sugerencias lista:** backend `4398ab32`; front `91bf5c1`, `6b911a3` y `34cc87f`. Guardado e historial propios, revisión y 409, versiones publicadas capturadas, condiciones y mapeos escalares; Always guarda «cuándo» como null. Pruebas rápidas backend 56/56; front 53/53 y arquitectura 49/49, build y lint limpios. El editor muestra errores HTTP junto al campo y abre la fila afectada sin perder la edición. El consumo, la cadena entre respuestas y las capturas HTTP siguen pendientes del cierre conjunto.

- **Operaciones de canal y cambio de modo listos:** el puerto común recupera propuestas y procedencia con UUID, permisos y vencimiento del mensaje; disponibilidad local cerrada, sin clave ni permisos de Inicio. Backend9e4f8f83: Application249/249, DomainIA47/47, técnicos26/26 y arquitectura155/155. El modo (backend96a89dcf) usa permisos por destino, migra Bot a Manual y distingue el POST ya iniciado: Application19/19 y DomainWhatsApp105/105; front12631db y4ace3dd, focales223/223 y arquitectura49/49. Builds sin advertencias y lint/TypeScript limpios. La conexión automática y manual de las propuestas con la cola sigue en desarrollo; PostgreSQL, proveedor y capturas quedan para el cierre conjunto.

- **«Cuándo lo ofrece» listo (backend00c852d8; fronta673eda y9b2d9d8):** un texto original de hasta1000 caracteres en «El formulario», guardado y recuperado con el borrador, su revisión y requestId; el409 conserva la edición para revisión. Publicación, snapshot, fingerprint y preguntas bilingües permanecen independientes. DomainBF57/57, ApplicationBF332/332, arquitectura154/154 y build sin advertencias; front193/193 focales, lint/arquitectura49/49 y build/TypeScript limpios. Migración aditiva generada; HTTP/PostgreSQL y capturas pendientes del cierre conjunto.

- **Motor único listo (6096d1cc):** capacidades Read/Proposal con identidad y versión exactas, permisos separados de Actor/Subject, presupuestos y propuesta durable con lecturas observadas; Inicio conserva sus cinco consultas y su aislamiento. Application IA/core191/191, Domain IA44/44, técnicos12/12 y arquitectura154/154; build sin advertencias. Revisión independiente de aislamiento y recuperación sin hallazgos. La migración está generada, sin aplicar; PostgreSQL, proveedor real y cierre conjunto siguen pendientes.

- **D2 listo antes del asistente:** panel junto a planilla o Por aprobar, URL, ↑/↓ y teclado, momentos de reglas capturadas, una decisión y pestañas con PDF sólo emitido. Navegación tardía, retiro de permisos y descargas mantienen su dueño y foco. Backend99804326: BusinessForms321/321, SQL técnico4/4, arquitectura148/148 y build sin advertencias. Front978a8d1, d9c8581, 66acf6b, f5874aa y355c581: área196/196, límites finales65/65, arquitectura49/49, build/TypeScript y lint limpios. TDD y lectura independiente; las capturas HTTP, PostgreSQL, visual y navegador quedan para el cierre conjunto de toda la tarea.

[tableros]: ../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#los-tableros
[ia]: ../../features/inteligencia-artificial.md
[0018]: ../../decisions/0018-robustez-proporcional-al-efecto.md
[0019]: ../../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md
[0020]: ../../decisions/0020-motor-unico-de-ia-con-capacidades.md
[permiso]: ../../guides/permiso-nuevo.md
[validar]: ../../guides/validar-un-cambio.md
