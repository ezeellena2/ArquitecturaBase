# Casos del asistente

**Estado:** decidido el 2026-10-21; tableros elegidos el 2026-10-21 (AS1-A y AS2–AS6). El 2026-11-11 el usuario lo sumó a la tanda de la prueba integral única; la [implementación](#implementación-2026-11-11), contrastada con el código, espera su visto bueno. La etapa siguiente, **fotos y documentos**, está abajo y no se pierde.

## Por qué

**Hoy el asistente de WhatsApp sabe hacer bien una cosa: ofrecer formularios.** Lo demás falla así:
- **Respuestas fijas en el código.** Su menú está en `WhatsAppAssistantAccessPolicy` y las reglas finas en las descripciones de cada capacidad. Agregar un tipo de respuesta exige programar.
- **Silencio ante cualquier otra cosa.** Un saludo, «¿qué hora es?» o «¿hacen envíos?» terminan en «no responder». La persona no recibe nada, y nadie se entera de por qué.
- **Sin contexto de la conversación.** El modelo ve solo el último mensaje, sin los anteriores.
- **Ciego a los archivos.** Las fotos y los documentos no le llegan.

**La meta:** que responda con sentido a cualquier mensaje, según el negocio. Los formularios siguen siendo el fuerte; lo demás también tiene respuesta.

## Qué se construye

Una pantalla **Asistente** con su configuración guardada en la base. En el multitenant va una por organización. Se guarda con revisión y 409, como el resto de la configuración interna ([ADR 0018][0018]).

| Pieza | Qué guarda |
| --- | --- |
| **Perfil** | Nombre, presentación, tono, qué hace y qué no, y qué hacer cuando no sabe. |
| **Conocimiento** | La **información del negocio** que ya existe, hoy solo usada por Inicio. Antes de usarla, se corrige su deuda conocida: hoy presenta preguntas sin respuesta y textos anteriores como hechos ([planes](README.md)). |
| **Casos** | Por caso: cuándo (texto libre), tipo de entrada (texto, audio o cualquiera; fotos y documentos, en la etapa siguiente), qué hacer, orden y si está activo. |
| **Límites** | Temas que no responde, lo que no puede prometer, largo máximo de una respuesta y un tope propio de respuestas por conversación y día. |

**«Qué hacer»** se elige de un menú cerrado. Es lo único que vive en el código:
1. mandar el formulario X;
2. responder con un texto exacto, escrito por el negocio;
3. **responder con la información del negocio:** la IA redacta, con los límites de abajo;
4. preguntar con botones;
5. derivar a una persona;
6. no responder.

**Lo que no es un caso nuevo:**
- **El caso «Cualquier otra cosa»** siempre existe y no se borra. Ahí caen el saludo, la hora o una consulta general. Ejemplo: *«Respondé breve con la información del negocio; si no la tenés, decí que lo pasás a una persona y derivá»*.
- **Los formularios** conservan su «Cuándo lo ofrece», guardado en el formulario. La lista de casos los muestra como «Mandar formulario», sin duplicarlos.

**Probar el asistente:** escribís un mensaje y ves qué caso elige, por qué y qué respondería, sin mandar nada.

**El chat:**
- una respuesta redactada se ve con «Por qué respondió esto»: el caso, lo que usó y el texto guardado;
- una derivación marca la conversación «Necesita una persona», con el motivo, en el chat y en la lista, y a la persona le llega el mensaje fijo del perfil.

**Cómo encaja en el motor ([ADR 0020][0020]):** casos y acciones son capacidades como las de hoy, con lectura y propuesta. El sistema ejecuta y vuelve a validar. Perfil, casos, formularios candidatos e historial van precargados al principio del pedido, en un orden estable para aprovechar la caché del proveedor. El objetivo es 1 o 2 consultas por turno en lugar de las ~3,6 de hoy.

## Decisiones tomadas

No se vuelven a preguntar.

- **El asistente redacta respuestas con la información del negocio, con límites:**
  - solo con lo cargado (perfil, conocimiento y casos);
  - corto y en el idioma de la persona;
  - sin enlaces, teléfonos, precios ni compromisos que no estén cargados;
  - si no sabe, deriva;
  - se guarda qué redactó y por qué.

  Esto cambia la regla actual «el modelo nunca escribe lo que sale» ([WhatsApp](../features/whatsapp.md#ofrecimientos-automáticos--etapa-5)), y se fija en el [ADR 0021](../decisions/0021-respuestas-redactadas-por-el-asistente.md). Un negocio puede quedarse solo con textos exactos.
- **Historial:** ve los mensajes de las últimas 24 horas **de esa misma conversación, nunca de otra**, con un tope (por ejemplo, los últimos 10, contando texto y transcripciones), siempre como datos y nunca como instrucciones. Lleva un test con dos personas escribiendo a la vez.
- **Derivar:** aviso en el chat y en la lista, más el mensaje fijo a la persona. Sin correo ni avisos fuera del sistema.
- **Dónde vive la configuración:** en nuestra base, por organización. No va en el panel del proveedor de IA: quedaría atado a una cuenta y a un proveedor, y el negocio no podría editarla, versionarla ni probarla.
- **Genérico:** ninguna respuesta queda escrita en el código. Un caso nuevo es configuración; una acción nueva es una opción más del menú, con su capacidad, su texto y sus casos de evaluación.
- **Fotos y documentos:** van en una etapa aparte, la siguiente a esta.
- **Ráfagas de mensajes:** hoy solo se interpreta el último mensaje y el más nuevo cancela el pedido anterior; con varios mensajes seguidos se pierde lo dicho antes. Desde esta etapa cambia así:
  - el turno espera **4 s sin mensajes nuevos**, con un tope de **15 s**, y procesa la ráfaga entera;
  - cancelar lo anterior solo descarta un borrador, porque el turno nuevo incluye todo el historial.
- **Historial con lo hecho:** el historial suma **lo que el asistente ya hizo y cómo quedó** (por ejemplo, «ayer se mandó Registrar visita y lo completó» o «hoy se mandó Pedir repuesto, sin abrir»). Así no repite lo de ayer y entiende cuándo le piden uno nuevo.
- **Varias acciones por respuesta:** hasta **3, en orden**, cada una validada por separado; por ejemplo, un texto y un formulario, o dos formularios. El tope diario de ofrecimientos sigue valiendo.

  Ráfagas y acciones se tomaron con lo recomendado el 2026-10-21 y se confirman al elegir los tableros.
- **El asistente es un agente:** se modela desde ya como el **agente principal**, una fila con su perfil, conocimiento, casos, límites y acciones permitidas, y no como una configuración única. Así la tercera etapa, Agentes, agrega más filas y su ruteo sin rehacer nada.
- **Tableros (2026-10-21):** se eligieron AS1-A (la pantalla del agente principal, con pestañas) y AS2–AS6 de la versión 98 del lienzo ([diseño del front][tableros]). Con eso se confirman las ráfagas, las 3 acciones por respuesta, el borrador para el operador en Atención manual (AS4) y lo fijado al dibujar: la pantalla en el panel de WhatsApp con «Configurar el asistente», un solo Guardar con revisión y 409, Probar con lo guardado, los formularios en un bloque fijo antes de «Cualquier otra cosa», derivar que pasa a Atención manual y las acciones que puede usar cada agente.

## Relación con otros planes

- [Sugerencias de texto con revisión humana](2026-10-03-sugerencias-ia-whatsapp.md): en Atención manual, la respuesta redactada se le puede ofrecer al operador como borrador, con el mismo motor. Se decide al dibujar los tableros, sin duplicar el mecanismo.
- **Multitenant:** perfil, casos y límites son por organización. Hay que sumarlo al traspaso cuando se cierre esta etapa.

## Etapa siguiente: fotos y documentos

**Ya tiene plan propio, con sus decisiones:** [Documentos por WhatsApp](2026-10-22-documentos-por-whatsapp.md). Lo que sigue en esta sección es el origen del pedido.

**Es prioridad del usuario.** Si una persona manda una factura, un ticket o cualquier comprobante, el sistema lo carga en el formulario que corresponde, o lo propone prellenado para que la persona lo confirme. Para eso hace falta:
1. **Descargar el archivo** y guardar una copia privada, como con los audios.
2. **Interpretarlo:** que un modelo calificado lo describa y lea sus datos (fecha, importe, comercio, número).
3. **Casos por tipo de entrada:** por ejemplo, «cuando manda una foto de un ticket».
4. **Prellenar las preguntas** con los datos leídos y adjuntar el archivo a la pregunta de foto o documento del formulario. Hoy el prellenado no propone archivos ([formularios](../features/formularios.md)).
5. **Probar el asistente con archivos.**

## Tercera etapa: agentes

**Qué cambia.** Un agente es un asistente especializado: perfil e instrucciones propias, solo el conocimiento que le corresponde, sus casos, sus acciones permitidas y su tono. En la primera etapa hay uno solo, el principal. En esta, el negocio crea varios desde un menú «Agentes». Por ejemplo:
- ventas: catálogo, precios y pedidos;
- soporte: reclamos y el formulario de visita;
- personal interno: licencias y recibos.

**Qué se construye:**
1. **Crear agentes** con su prompt de «qué hace», más casos, conocimiento, acciones y límites, y «Probar el agente».
2. **Ruteo:** a qué agente va una conversación, de una de tres formas:
   - por número de WhatsApp o canal;
   - por tipo de contacto (empleados o clientes, según su cuenta o su rol);
   - un clasificador que lee el primer mensaje y elige el agente. El clasificador es lo último y lleva su propia calificación.
3. **Pases:** un agente pasa la conversación a otro o a una persona, con el motivo visible en el chat.
4. **Un solo motor:** cada agente es una configuración de canal con su conjunto de capacidades, sin otro motor ([ADR 0020][0020]).

**Cuándo conviene.** Cuando el público o el propósito cambian mucho (empleados frente a clientes, o números distintos), o cuando un solo agente junta tantos casos que empieza a confundirse. Mientras tanto, alcanza un agente con casos.

## Cómo se encara

1. El [ADR 0021](../decisions/0021-respuestas-redactadas-por-el-asistente.md), aceptado el 2026-10-21.
2. Los tableros, con el chat de diseño, y la elección del usuario.
3. **Back:** configuración y su lectura precargada, los desenlaces nuevos (texto exacto, texto redactado y derivar), el historial, los límites y la simulación. **Front:** la pantalla Asistente, lo que se ve en el chat y la lista.
4. Calificar el modelo con casos nuevos: consultas con y sin información, fuera de tema, saludo, hora, derivación e inyección de instrucciones.
5. El piloto real con Meta.

## Cómo se cierra

- **Checks:** por corte y al cerrar, según [validar un cambio](../guides/validar-un-cambio.md), con TDD de configuración, límites, historial y desenlaces.
- **Integración con Docker y front:** es/en, a 390 y 320 px.
- **Calificación:** aprobada antes de habilitar el perfil.
- **Piloto real con Meta:**
  - «hola» y «¿qué hora es?» reciben una respuesta con sentido;
  - una consulta con la información cargada se responde, y una sin información deriva;
  - «se rompió la heladera» sigue mandando el formulario;
  - «quiero hablar con alguien» deriva y aparece en la lista.

## Implementación (2026-11-11)

Propuesta para el visto bueno del usuario, contrastada con `main` en `d9db8c1d`. Lo decidido arriba no se vuelve a abrir.

### Lo que hay hoy

- **Un mensaje, sin contexto.** El modo Asistente interpreta sólo el último mensaje de negocio: un texto, un audio transcripto o una planilla con su pedido. Cada mensaje crea su referencia (`WhatsAppAssistantRequest`) y el nuevo cancela el anterior. Al motor le llega ese texto solo, sin los mensajes anteriores del chat.
- **El catálogo está lleno.** El canal automático ofrece siete capacidades, más las ocho de armar formularios y catálogos. Con la terminal son 15 definiciones, o 16 con una planilla, que es el tope del transporte.
- **Una propuesta por turno.** La terminal de los canales, `assistant_proposal@1`, elige una sola propuesta. Cada respuesta del modelo trae una herramienta y la terminal va sola, así que un ofrecimiento cuesta cuatro llamadas: candidatos, ofertas, propuesta y terminal. No hay contexto precargado y la hora del mensaje cambia el texto de sistema en cada turno.
- **Nada de texto propio.** El asistente manda un formulario, una aclaración con botones o un texto fijo: el formulario faltante, los resúmenes de IA3-A o el rechazo de una planilla. «No responder» no deja nada en el chat. No existen el agente ni «Necesita una persona». `whatsapp.configureassistant` sólo deja pasar una conversación a Asistente.
- **La deuda del conocimiento.** `business.information`, de Inicio, todavía presenta como hechos las preguntas sin respuesta y los textos anteriores (`Hours`, `Location` y `Faq`). WhatsApp no lee Información del negocio.
- **El front.** No están la pantalla «Asistente», «Por qué respondió esto», la marca ni el filtro. La sugerencia de Atención manual (H2) sólo propone formularios. El front no tiene arrastre: se reordena con ↑ ↓, como en `ItemTable` o «Campos de…». El panel al costado es `DialogContent placement="right"`.
- **Otra sesión.** El plan de [documentos](2026-10-22-documentos-por-whatsapp.md) toca el motor y el asistente de WhatsApp, y había previsto armar Derivar genérico. Se acuerda con esa sesión quién lo arma y en qué orden van los archivos compartidos.

### Diferencias con los tableros

Los tableros son del 21/10. Después llegaron la lista de chequeo y varias reglas: sin leyendas, MA1-A, AC1-A, un dato por columna, el estado una vez y el idioma del menú (23/10). Al programar se aplican estos ajustes mecánicos:

1. **Las ayudas pasan a la (i).** Se mueven la introducción de «Casos», «Salen de «Cuándo lo ofrece»…» y las ayudas del Perfil, del editor de un caso, de Límites, de Probar y del pie de Conocimiento. Las descripciones de las seis opciones de «Qué hacer» van a la (i) de cada opción, y sale el recuadro de «No responder».
2. **Flechas en lugar de arrastrar.** Las flechas ↑ ↓ llevan su nombre, como en «Campos de…», y sale «Arrastrá para cambiar el orden».
3. **El estado de un caso, una vez.** Un caso pausado lleva sólo la casilla «Activo», sin la etiqueta «Pausado».
4. **La fila de un caso.** La configuración va en gris debajo, sin «Motivo:» ni otros rótulos corridos.
5. **«Por qué», en dos columnas.** En Probar y en el chat, «Caso», «Por qué» y «Usó» van en `DataList`, como «Por qué lo mandó». Sale «Texto: el de arriba…».
6. **Probar dice sólo lo cierto.** El cuadro de mensajes se llama «Mensajes». Sale «esperó 4 s…», porque la prueba no espera. «No se mandó nada.» pasa a la (i) del panel.
7. **Probar sin la conexión de IA** queda apagado con su motivo, como «Pasar a modo Asistente» (MA1-A), sin el recuadro amarillo.
8. **«Necesita una persona».** La etiqueta va en la cabecera y la franja lleva quién derivó y el motivo, sin repetir la etiqueta ni «Se quita cuando…». Es como la propuesta del asistente en un catálogo (EP1-A).
9. **Conocimiento.** La tabla lleva encabezados de columna, y las filas usan los nombres de las zonas de Información del negocio: Datos, Ubicación, Horarios, Servicios y políticas, y Respuestas.
10. **Los textos que salen tal cual** (texto exacto, mensaje al derivar, pregunta y botones) van en un solo campo en el idioma del menú, con «Sin traducir» en el otro, como en formularios. Reemplaza el par «en español» + «en inglés (opcional)».
11. **El Perfil.** Sale «así figura en el chat», que no es cierto: el chat dice «Asistente».
12. **Lo que el tablero no dibuja** se arma con las piezas comunes: el esqueleto de carga, el error con reintento, el 409 como en Información del negocio, la espera de un 429, la falta de permiso y los 320 px.

Dos cambios no son mecánicos. Van al usuario con su recomendación:

- **La zona «Cuando deriva», en Límites.** Repite el mensaje del Perfil y explica cuándo deriva. Recomendado: sacarla y pasar los cuatro motivos a la (i) del mensaje al derivar, en Perfil.
- **El recuadro «Siempre, en todas las respuestas redactadas».** Explica reglas fijas. Recomendado: pasarlo a la (i) del título «Límites», como las zonas que explicaban en la revisión de leyendas del 01/11.

### Decisiones técnicas

1. **El agente es una fila de WhatsApp,** `WhatsAppAssistantAgent`, la principal, porque el menú cerrado es del canal. Guarda el perfil, los límites, las acciones permitidas y el idioma original, más los casos en JSONB. Se guarda con `Revision` y 409, como Configuración (`SystemSettings`), sin comprobantes ([ADR 0018][0018]). Mientras no hay fila, el asistente hace lo de hoy: todo lo nuevo corre sólo con el agente guardado.
2. **El conocimiento llega por un puerto neutral del núcleo,** `IBusinessKnowledgeSource`, con su versión apagada. Lo implementa IA y da sólo hechos confirmados: las preguntas con respuesta y los detalles guiados. Cada zona trae su estado: «Cargado», «Sin cargar» o «Texto anterior, sin revisar». Con la misma regla se corrige `business.information` de Inicio.
3. **El motor precarga lecturas.** El canal declara qué leer, y el motor lo corre antes del primer paso.
   - Lo leído queda como lectura observada, así que autoriza una propuesta y explica el porqué.
   - Va al principio del pedido, en un orden estable, y no cuenta como herramienta ofrecida. La hora del mensaje pasa al final.
   - Con agente, el automático precarga tres lecturas: `whatsapp.agent@1` (perfil, casos, límites y conocimiento), `forms.assistant-candidates@1` y `whatsapp.agent-history@1`. La última trae las últimas 24 h de esa conversación, hasta diez mensajes, con lo que hizo el asistente, cómo terminó y los rechazos.
   - Un turno común cuesta dos llamadas: la propuesta y la terminal.
4. **Cada acción es una propuesta de su dueño.**
   - Un formulario o una aclaración son de formularios, como hoy.
   - El texto exacto, los botones, derivar y no responder salen de `whatsapp.reply@1`, que reemplaza a `whatsapp.no-reply@1`.
   - La respuesta redactada sale de `whatsapp.draft-reply@1`. El motor la ofrece sólo si el perfil de modelo declara que está calificado para redactar ([ADR 0021](../decisions/0021-respuestas-redactadas-por-el-asistente.md), punto 8).
   - La terminal `assistant_proposal@2` elige hasta tres propuestas, en orden. Con una planilla quedan 15 definiciones.
5. **Se valida antes de encolar** (ADR 0021, punto 5).
   - El caso existe, está activo y su acción está permitida.
   - El texto redactado respeta el largo máximo y el idioma de la conversación, y no trae enlaces, teléfonos, correos ni precios que no estén cargados. Lo que no pasa no sale y deriva.
   - Cada acción se valida por separado. Los formularios conservan sus dos por día, y las respuestas tienen su propio tope por conversación y día, que al llegar deriva.
6. **Derivar lo arma este desarrollo,** acordado con la sesión de documentos el 2026-11-11, con la semántica de AS4.
   - `WhatsAppContact` guarda la marca y el motivo, en la migración `AddWhatsAppAssistantAgent`.
   - Derivar pasa a Atención manual y encola un mensaje fijo, que el envío acepta aunque el modo ya sea Manual. Es el mensaje del perfil o un texto propio de quien deriva: documentos manda el suyo, de DOC4/CE4-A.
   - La marca se quita con la primera respuesta de una persona o al volver a Asistente.
7. **Ráfagas.** Con agente, el turno espera 4 s sin mensajes nuevos, como mucho 15 s desde el primero, y lleva toda la ráfaga. Si llega otro mensaje después, se cancela el borrador y el turno nuevo lo trae en el historial.
8. **«Por qué respondió esto».** La salida guarda el caso, la acción, lo que usó y su lugar («1 de 3»). El hilo lo devuelve con `whatsapp.read` y se borra con el texto a los 90 días. «No responder» y derivar quedan en el hilo como líneas, con el caso y el motivo.
9. **Probar** es una operación del motor en su propio canal, `whatsapp-assistant-test`.
   - El Actor es quien configura y el Subject es la cuenta elegida.
   - Usa lo guardado y el historial real de esa cuenta, y valida igual que una conversación.
   - No encola nada ni gasta los topes de WhatsApp; sí gasta el cupo diario del motor de quien prueba.
10. **El borrador para el operador.** En Atención manual, la sugerencia (H2) también puede ser un texto, exacto o redactado. «Usar como borrador» lo pone en el compositor y descarta la sugerencia.

### Tareas

**Backend.** TDD; en cada corte, el build y `Application.UnitTests` y `ArchitectureTests` enteros; la integración afectada con PostgreSQL; commits chicos.

1. **El agente.**
   - Entidad, errores `WhatsApp.AssistantAgent.*`, configuración EF y migración `AddWhatsAppAssistantAgent`, con la marca del contacto.
   - Servicio con revisión y 409, y validador con los topes.
   - `GET` y `PUT /api/whatsapp/assistant-agent`, con `whatsapp.configureassistant`.
   - Por el puerto de formularios, los formularios del bloque y los que puede mandar. También las cuentas para Probar y el conocimiento.
   - Inventario de rutas, OpenAPI y capturas.
2. **El conocimiento.** El puerto con su versión apagada, la implementación de IA y la deuda de `business.information`.
3. **El motor.** Las precargas, `assistant_proposal@2`, la hora al final y el filtro de `whatsapp.draft-reply` por perfil. Tests de arquitectura y de los dos proveedores.
4. **El canal.**
   - Las cuatro capacidades nuevas.
   - El contexto del automático y el de Atención manual con agente.
   - La terminal de WhatsApp con hasta tres acciones.
   - La validación del texto y el tope de respuestas.
   - «No responder», derivar con su mensaje y «Por qué respondió esto».
5. **Ráfagas e historial.** La espera, el texto de la ráfaga, el historial con lo hecho y un test con dos personas escribiendo a la vez.
6. **Probar y el borrador del operador.** Las rutas de prueba y la sugerencia con texto.
7. **El hilo y la lista.** La marca, el motivo, el filtro y su conteo, las líneas y los porqués. Las capturas de las cohortes que cambian.
8. **Calificación.**
   - Casos nuevos del arnés con sus chequeos sin red: con y sin información, fuera de tema, saludo, hora, derivación, e inyección en el mensaje y en el conocimiento.
   - La corrida real espera la clave y el visto bueno del usuario. Mientras tanto, la respuesta redactada sigue apagada por el perfil.

**Front.** En cada tarea, lint, `tsc`, vitest de lo tocado y visuales es/en a 1440, 390 y 320.

9. **La pantalla «Asistente».**
   - La ruta `/whatsapp/asistente`, su entrada en el panel y las pestañas.
   - Perfil, Conocimiento, Casos con su editor al costado y Límites.
   - Un solo Guardar con revisión y 409, y Probar.
10. **El chat.** «Por qué respondió esto», las líneas «Sin respuesta del asistente» y de derivar, la marca y el filtro, y el borrador del operador.

**Cierre.**

11. **Documentación y reporte.**
    - Las reglas de WhatsApp e IA, el ADR 0021, las guías y los `AGENTS.md`.
    - El diseño en el front con sus diferencias.
    - El reporte a la coordinadora con los commits, las cohortes y los pasos de la prueba a mano.

### Cohortes que cambian

Las exporta la coordinadora en la exportación conjunta:

- `whatsapp-assistant-agent-http`, nueva: el agente, el 409, las validaciones y la prueba.
- `whatsapp-assistant-http`: la sugerencia con texto.
- La cohorte del hilo y de la lista del chat.

### Queda afuera

- Las marcas de caché propias de cada proveedor: alcanza con el orden estable.
- El traspaso al multitenant, que va después de la prueba integral.
- El piloto real con Meta.
- Fotos y documentos, que son de la otra sesión.
- La etapa Agentes.

### Riesgos

- **El motor lo tocan dos sesiones.** Documentos suma `ReadDocument` (su tarea 2) y el documento del turno (su tarea 3) en los mismos archivos. Los cambios del motor y del contexto del canal (tareas 3 y 4 de acá) van sobre el commit de su tarea 3. Derivar, el agente, la migración y el front no tocan esos archivos y pueden ir antes. Con agente y documento a la vez, la precarga también deja afuera los candidatos, como las herramientas.
- **La calificación real** necesita la clave y el visto bueno del usuario.
- **El tamaño.** Cada tarea cierra con su commit.

[0018]: ../decisions/0018-robustez-proporcional-al-efecto.md
[0020]: ../decisions/0020-motor-unico-de-ia-con-capacidades.md
[tableros]: ../../../ArquitecturaBaseFront/docs/design/asistente-casos.md#los-tableros
