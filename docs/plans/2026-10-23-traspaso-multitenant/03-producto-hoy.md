# 03 · El producto hoy, de punta a punta

Qué hace ArquitecturaBase al 2026-10-23, área por área. El detalle exacto está en los anexos [A](anexo-a-backend-desde-17-10.md) (back) y [B](anexo-b-front-desde-17-10.md) (front). Lo que está mal y se está corrigiendo lleva el número de su punto en el [review E2](../../reviews/2026-10-23-code-review-catalogos-e2.md).

## 1. Diseñar un formulario

- **Preguntas:**
  - texto corto y largo, número, número con decimales y fecha;
  - **elegir una opción,** con opciones propias o de un catálogo;
  - **elegir varias,** que en WhatsApp es un `CheckboxGroup` de hasta 20 opciones, con mínimo y máximo;
  - **foto y documento,** con mínimo y máximo de archivos.
- **Idioma:** se escribe en el idioma original y la traducción es opcional. Desde el 23/10 se escribe en el idioma del menú, sin selector (punto 35).
- **Identidad:** nombre, prefijo de numeración (`VISITA-000012`) y texto del botón final.
- **«Cuándo lo ofrece el asistente»:** texto opcional que le dice al asistente cuándo proponer este formulario.
- **Publicar:**
  - una comprobación muestra qué se publica, qué idiomas están completos y los avisos;
  - publicar crea una versión inmutable y un Flow en Meta por cada idioma completo, con el perfil `flow-static-7.3-r9`. Es una operación durable: si el resultado queda incierto, se consulta antes de repetir.
- **Cuando llega una respuesta, las reglas:**
  - eventos: recibida, aprobada y rechazada;
  - condiciones sobre las preguntas;
  - acciones en orden: pedir aprobación a una persona con un rol, generar el PDF y avisar al autor o a un rol.

  Las reglas tienen sus propias revisiones y no tocan la versión publicada.
- **Diseño del PDF,** con sus propias revisiones y una vista previa con datos de ejemplo.
- **Qué sugerir después:** al confirmar una respuesta, el asistente puede ofrecer otro formulario con datos ya mapeados. Hay dos modos: Asistente (decide la IA) y Siempre (por condición).
- **Ciclo de vida:**
  - **dar de baja:** deja de ofrecerse, pero los envíos abiertos siguen hasta vencer y después se deprecan los Flows;
  - **reactivar:** publica una versión nueva;
  - **eliminar:** borrado lógico, solo sin respuestas ni envíos abiertos; el prefijo queda reservado.

## 2. Catálogos

- **Escritas a mano (Manual):**
  - **tipo de valor,** que se elige al crear y no se cambia: texto, entero, decimal o fecha;
  - **cada opción** tiene código, texto, detalle, valor y estado;
  - **carga de a una** en una tabla editable o en el editor del celular;
  - **carga de una vez:** pegar desde Excel o importar un XLSX o CSV, con vista previa de altas, cambios, bajas y errores, y aplicar con revisión;
  - **exportar** y bajar la plantilla;
  - **orden A–Z** y flechas para mover;
  - **hasta 10.000 opciones,** contando las inactivas.
- **De la base (Filtros):** personas del sistema con filtros (rol, activo…), sin escribir SQL (ADR 0019).
- **Campos extra (E2):**
  - **cada opción puede tener datos propios,** como CUIT, Zona o Teléfono;
  - **tipos:** texto, entero, decimal, fecha, sí/no o «elegir de otro catálogo», que toma los valores de otro catálogo Manual: es «una sola fuente de valores»;
  - **reglas:** obligatorio, único y mínimo o máximo;
  - **papel «Dato»:** viaja con la respuesta y se ve en la planilla, el Excel y el PDF;
  - **papel «Filtro»:** sirve para mostrarle a cada persona lo suyo, o para un filtro fijo de una pregunta.
- **Qué viaja a WhatsApp:** el catálogo define qué campos van al título (hasta 30 caracteres) y a la descripción (hasta 300) de cada opción. Una pregunta puede tener su propia presentación (CE3-B). A Meta viaja solo lo elegido.
- **Datos de las personas (E2, CE4-A):**
  - una pantalla define datos por persona, como «Zona», y los carga en una tabla;
  - «Editar usuario» suma su sección;
  - una pregunta puede filtrar: «Zona del proveedor = zona de quien lo recibe»;
  - a quien no tiene el dato no se le manda y se le dice por qué. Nunca va la lista entera.
- **Vista previa «Como si lo recibiera»:** muestra las opciones que vería una persona concreta.
- **Sellado:** al responder, la respuesta guarda el texto, el valor y los campos de la opción tal como estaban. Editar el catálogo después no cambia lo respondido.

**Correcciones en curso:** puntos 1–17, 22, 23 y 35–39 del review E2.

## 3. Mandar un formulario

- **Desde el chat de WhatsApp:** un operador elige el formulario para un contacto con cuenta vinculada y con el permiso de cargar respuestas.
- **Por el asistente:** reconoce el pedido y manda el formulario prellenado, o le sugiere al operador en Atención manual.
- **Después de una respuesta:** por las sugerencias.
- **La sesión:**
  - fija formulario, versión, idioma (una cultura común completa) y destinatario;
  - dura 24 horas y vence según la hora de Meta;
  - el prellenado propone valores, también opciones de catálogo.
- **La oferta del catálogo se prepara por destinatario:** filtros por persona, presentación y tope de 200, o de 20 en «elegir varias».

## 4. Recibir y procesar

- **Captura:**
  - Meta manda el `nfm_reply`;
  - la captura valida contra la versión sellada y crea la respuesta con su número;
  - es idempotente por el recibo.
- **Archivos:** se bajan a Azure Blob privado y se verifican por contenido, con topes.
- **Reglas:** corren como operaciones durables por respuesta, evento, regla y acción.
- **Aprobación:**
  - la decide alguien con el rol de la regla y `formapprovals.decide`, nunca quien cargó la respuesta;
  - el rechazo exige un motivo.
- **PDF y avisos:**
  - el PDF se genera por idioma y se guarda una sola vez;
  - los avisos van por WhatsApp. Fuera de las 24 horas quedan «No se pudo», a la vista.

## 5. Ver las respuestas

- **«Formularios» (MF1):**
  - los formularios activos y los dados de baja, con su última respuesta;
  - el bloque «Por aprobar» con lo que la persona puede decidir;
  - quien solo aprueba ve solo eso.
- **Planilla por formulario:**
  - filtros con una gramática común (pregunta, persona, fecha, estado, «alguna de» para elegir varias);
  - columnas elegibles y subcolumnas de los campos sellados de catálogo;
  - Excel con «solo las columnas visibles»;
  - paginado del servidor.
- **D2:** la vista rápida de una respuesta, al costado de la planilla.
- **DC0 y DC1:** el detalle completo, con flechas para recorrer en el orden de la planilla, el historial del recorrido (reglas, aprobaciones, avisos y PDF) y los archivos.

## 6. Asistente por WhatsApp

- **Modo por conversación:** Asistente o Atención manual.
- **Qué hace:**
  - interpreta texto y audio, con el audio transcripto y la transcripción visible;
  - propone el formulario prellenado y pide aclaraciones;
  - sugiere después de una respuesta;
  - registra los «Formularios que faltan»;
  - propone catálogos con filtros, que aprueba una persona.
- **Motor único de IA con capacidades** (ADR 0020): el modelo lee o propone y ejecuta el módulo dueño. Los perfiles del modelo se califican con casos antes de habilitarse.
- **Rendimiento y aislamiento (21/10):** de texto a envío tarda 6,6 s y con audio 7,7 s. Cada conversación tiene su ámbito, y tres contactos a la vez no se mezclan.
- **Decidido sin programar:** Casos del asistente, Documentos por WhatsApp y agentes ([anexo F](anexo-f-planes-pendientes.md)).

## 7. Asistente de Inicio (web)

- **Qué es:** un chat «Preguntale al sistema», con consultas del sistema que muestran su evidencia («¿Cuántos usuarios activos hay?») y conversaciones guardadas.
- **Roto, en corrección:** no mostraba nunca la respuesta, porque el back manda el estado en minúsculas y el front lo espera con mayúscula (punto 34).

## 8. Administración

- **Usuarios, roles y permisos.** Son permisos, nunca roles fijos.
- **Información del negocio,** que usa el asistente.
- **Configuración de IA:** credenciales, perfiles y calificación.
- **Apariencia y configuración general.**

## Permisos de formularios

`formtemplates.read`, `formtemplates.manage` y `formtemplates.publish`; `formrecords.create`, `formrecords.read` y `formrecords.export`; `formcatalogs.read` y `formcatalogs.manage`; `formrules.manage`; `formapprovals.decide`; `formsuggestions.manage`; `formpersondata.manage`, nuevo de E2. El detalle de qué habilita cada uno está en el [anexo A](anexo-a-backend-desde-17-10.md).
