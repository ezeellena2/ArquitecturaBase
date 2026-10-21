# Casos del asistente

**Estado:** decidido el 2026-10-21; tableros elegidos el 2026-10-21 (AS1-A y AS2–AS6); sin empezar. Arranca después de la tanda del review de dar de baja y del arreglo de la espera del asistente. La etapa siguiente, **fotos y documentos**, está abajo y no se pierde.

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

  Esto cambia la regla actual «el modelo nunca escribe lo que sale» ([WhatsApp](../features/whatsapp.md#ofrecimientos-automáticos--etapa-5)), así que **lleva un ADR nuevo antes de programarse**. Un negocio puede quedarse solo con textos exactos.
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

1. El ADR de las respuestas redactadas.
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

[0018]: ../decisions/0018-robustez-proporcional-al-efecto.md
[0020]: ../decisions/0020-motor-unico-de-ia-con-capacidades.md
[tableros]: ../../../ArquitecturaBaseFront/docs/design/asistente-casos.md#los-tableros
