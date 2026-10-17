# Etapa 5 — Asistente, sugerencias y audios

Estado: en desarrollo · 2026-10-17. Los 18 arreglos del review de la Etapa 4 están implementados. Primero se completa D2 v79; el asistente todavía no empezó. H1–H7 y s1–s4 conservan las decisiones de abajo.

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


[tableros]: ../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#los-tableros
[ia]: ../../features/inteligencia-artificial.md
[0018]: ../../decisions/0018-robustez-proporcional-al-efecto.md
[0019]: ../../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md
[0020]: ../../decisions/0020-motor-unico-de-ia-con-capacidades.md
[permiso]: ../../guides/permiso-nuevo.md
[validar]: ../../guides/validar-un-cambio.md
