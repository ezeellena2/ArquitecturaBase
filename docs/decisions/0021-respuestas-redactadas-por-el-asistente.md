# ADR 0021: Respuestas redactadas por el asistente, con límites

## Estado

**Aceptada**, 2026-10-21, por decisión del usuario. Rige desde [Casos del asistente](../plans/2026-10-21-casos-del-asistente.md). Cambia una regla de los ofrecimientos automáticos de WhatsApp: hasta ahora, «no se envía texto libre del modelo».

## Contexto

El asistente de WhatsApp solo manda formularios, preguntas con botones y acuses fijos ([WhatsApp](../features/whatsapp.md#ofrecimientos-automáticos--etapa-5)). Una consulta, un saludo o «¿qué hora es?» terminan en «no responder», y la persona no recibe nada. El motor solo admite como salidas finales `assistant_proposal` y `assistant_answer`, y el canal de WhatsApp no habilita la segunda ([ADR 0020](0020-motor-unico-de-ia-con-capacidades.md)).

El usuario quiere que el asistente responda con sentido a cualquier mensaje, según la información y los casos que configura cada negocio. Tampoco quiere respuestas escritas en el código.

## Decisión

El asistente puede **redactar una respuesta de texto** cuando un caso configurado lo indica, con estos límites:

1. **Fuentes:** solo usa lo que el negocio cargó (perfil, conocimiento y casos), el historial de esa conversación y los datos del sistema que el canal le da (por ejemplo, la hora y la zona del negocio). Lo leído son datos, nunca instrucciones.
2. **Forma:** corta, con el largo máximo de los límites y en el idioma de la conversación.
3. **Contenido:** sin enlaces, teléfonos, correos, precios ni compromisos que no estén en el conocimiento cargado.
4. **Si no sabe:** deriva a una persona con el acuse fijo del perfil. No inventa.
5. **Control del servidor:** la respuesta llega como una propuesta tipada de la capacidad dueña. WhatsApp la valida (largo, idioma, enlaces y datos de contacto) **antes de encolarla**. Si no pasa, no sale y deriva. El texto nunca se manda directo desde el modelo.
6. **Trazabilidad:**
   - se guarda el texto, el caso y lo que usó, visibles en «Por qué respondió esto»;
   - los logs no llevan el contenido.
7. **Configurable:** cada negocio puede quedarse solo con textos exactos, apagando «Responder con la información del negocio» en las acciones permitidas.
8. **Calificación:** un perfil de modelo responde texto solo si está calificado con casos que incluyan:
   - consultas con y sin información cargada;
   - fuera de tema, saludo y hora;
   - derivación;
   - inyección de instrucciones dentro del mensaje o del conocimiento.

Lo demás de ADR 0020 sigue vigente:
- las capacidades solo leen o proponen;
- ejecuta el módulo dueño;
- no se agrega otro motor.

## Consecuencias

- **El canal habilita la propuesta de respuesta.** Puede ser una capacidad de propuesta de texto o `assistant_answer` adaptado; se decide al implementarlo, con la opción más simple que cumpla los límites. Necesita su validador en WhatsApp, su forma de verse en el chat y su desenlace en la cola de salida.
- **La regla de [WhatsApp](../features/whatsapp.md) cambia al cerrar la etapa:** «no se envía texto libre del modelo» pasa a «solo texto redactado con estos límites, o fijo».
- **Respuestas de texto y ofrecimientos van por separado:** las respuestas tienen su propio tope por conversación y día, en los límites, y no consumen el cupo de ofrecimientos.
- **El riesgo que queda** es que una respuesta correcta en forma tenga un error de contenido. Lo acotan las fuentes cerradas, la derivación ante la duda, la calificación y que el negocio vea cada respuesta con su «por qué».

## Alternativas descartadas

- **Solo textos exactos:** es más seguro, pero no responde con sentido a mensajes que el negocio no previó. Queda como opción por negocio.
- **Configurar el contexto en el panel del proveedor de IA:** no ahorra tokens, porque el proveedor lo vuelve a mandar en cada llamada. Además ata la configuración a un proveedor y a una cuenta, y no se puede editar, versionar ni probar desde la app.
- **Texto libre sin validación del servidor:** contradice ADR 0020, donde el modelo propone y el sistema valida.
