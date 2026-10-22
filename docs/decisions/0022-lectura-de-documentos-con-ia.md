# ADR 0022: Lectura de documentos con IA

## Estado

**Aceptada**, 2026-10-22, por decisión del usuario. Rige desde [Documentos por WhatsApp](../plans/2026-10-22-documentos-por-whatsapp.md). Amplía el motor de IA del [ADR 0020](0020-motor-unico-de-ia-con-capacidades.md) a imágenes y documentos. Mantiene que la IA propone y una persona confirma.

## Contexto

Hoy una foto, un PDF o un archivo de Office que llega por WhatsApp se guarda como «archivo» sin contenido, y el asistente no lo ve. El usuario quiere que cualquier documento (un comprobante, una factura, un remito o una planilla) se reconozca y se relacione con un formulario. Ningún tipo de documento puede quedar escrito en el código.

Leer un documento con IA implica mandar su contenido a un proveedor externo. Esos documentos suelen tener datos financieros y personales: CBU, CUIT, montos y nombres.

## Decisión

1. **Qué se lee:**
   - solo lo que mandan **personas con cuenta vinculada y activa**;
   - los tipos: foto (JPEG y PNG), PDF, Word (`.docx`), Excel (`.xlsx`), ubicación y contacto;
   - **no se leen** los formatos viejos (`.doc`, `.xls`), los archivos con macros, los protegidos con contraseña, el video ni los stickers.
2. **Cómo se prepara, antes de la IA:**
   - el tipo se verifica por el contenido;
   - se aplican topes de tamaño, páginas, hojas y celdas, contando también las celdas vacías con formato;
   - Word y Excel se convierten en el servidor a texto y tablas, sin ejecutar nada;
   - ubicación y contacto **no pasan por la IA:** son datos estructurados de WhatsApp.
3. **Qué se manda al proveedor de IA:**
   - el documento preparado y las preguntas del formulario candidato;
   - **nada más de la persona** ni de otras respuestas;
   - la cuenta del proveedor usa las opciones de no retener contenido que ofrezca, y se registra qué se mandó (hash y tamaño), sin el contenido.
4. **Lo leído es un dato, nunca una instrucción.** Un texto dentro del documento que intente dar órdenes al modelo no cambia nada. Las capacidades solo leen o proponen ([ADR 0020](0020-motor-unico-de-ia-con-capacidades.md)).
5. **Validaciones fijas del lado del servidor,** que no dependen de la IA:
   - dígitos verificadores (CBU, CVU, CUIT y CUIL);
   - la entidad que sale del CBU o el CVU;
   - formatos;
   - duplicados.

   Un dato que no las pasa no se propone como válido.
6. **Confirmación:** en esta etapa, **una persona confirma siempre** antes de que se cargue la respuesta. La carga automática, «Cargar solo si es seguro», se decide en otra etapa con datos medidos. Exige las tres señales del plan:
   - un tipo claro;
   - las validaciones fijas;
   - un historial sin correcciones.

   **Nunca se usa la seguridad que declara el modelo.**
7. **Formularios armados por la IA:** la IA propone el borrador y una persona lo revisa y publica, como ya fija el [ADR 0019](0019-sql-de-configuracion-acotado-a-catalogos.md) para la configuración propuesta por un modelo.
8. **Retención:**
   - el documento que termina en una respuesta sigue la retención de la respuesta, que todavía es una decisión pendiente de producción;
   - el que queda en la bandeja descartado o sin clasificar se borra a los **90 días**, junto con su lectura;
   - el contenido que guarda el motor de IA vence según su propio límite.
9. **Costos:** topes de documentos leídos por persona y por día, y en total, configurables. Al llegar al tope, el documento queda en la bandeja sin leerse.

## Consecuencias

- **El motor de IA suma un propósito,** «leer documento», con su perfil calificado, como la transcripción de audio. Las capacidades nuevas son de BusinessForms: clasificar y proponer datos.
- **Antes de habilitar un perfil, se califica con casos ficticios:**
  - comprobantes de varios bancos;
  - fotos movidas;
  - documentos que no son ningún tipo;
  - inyección de instrucciones dentro del documento;
  - Word y Excel.
- **La bandeja «Documentos recibidos» es una pantalla nueva,** con permisos propios.
- **El riesgo que queda** es un dato bien formado pero equivocado, por ejemplo un monto mal leído en una foto. Lo acotan las validaciones fijas, la confirmación de la persona y el registro de correcciones.

## Alternativas descartadas

- **Azure AI Document Intelligence como único lector:** es muy preciso con diseños fijos, pero los documentos propios exigen entrenarlo con ejemplos marcados. Queda como opción por tipo, más adelante, si un cliente tiene mucho volumen de un mismo diseño.
- **Cargar sin confirmación desde el principio:** sin datos medidos de precisión no hay cómo saber cuándo es seguro.
- **Leer también lo que mandan números desconocidos:** queda para después, porque son personas sin cuenta que no pueden completar formularios.
