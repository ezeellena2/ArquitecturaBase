# Dar de baja, reactivar y eliminar formularios

Estado: en curso. Tableros elegidos el 2026-10-20 (BA1-A, BA2–BA6 y, para la etapa siguiente, MF1). El arreglo del envío manual sin prellenado ya se cerró.

Avance: previos y baja/reactivación completos (BA1-A, BA2, BA3, BA4-reactivar y BA6), con revisión de foco y recuperación de publicación. Backend de eliminación y lectura neutral de envíos abiertos implementados; front de eliminar en verificación. Build sin advertencias, 98 unitarios de Domain y 970 de Application de BusinessForms, 3 del fallback, 159 de arquitectura y los técnicos SQL sin Docker aprobados. Integración HTTP y 22 capturas es/en preparadas para el cierre conjunto; sigue la deprecación durable en WhatsApp.

## Qué se construye

Un formulario pasa de **activo** a **dado de baja** y puede volver con una versión nueva. Eliminar queda para los que no tienen respuestas. Todo es dinámico: los formularios son datos, y ninguna parte del código los nombra.

- **Dar de baja** (`formtemplates.publish`):
  - **Corta lo nuevo:**
    - el formulario sale del selector del chat;
    - el asistente no lo ofrece (cuenta como inexistente: si alguien lo pide, se anota en «Formularios que faltan»);
    - las sugerencias de otros formularios hacia él se dejan de ofrecer.
  - **Envíos abiertos:** se pueden responder hasta que vencen (como mucho, `FlowSessionLifetimeHours`).
  - **Deprecación en Meta:**
    - cuando no quedan sesiones vigentes, una operación durable (efecto externo, [ADR 0018][0018]) depreca en Meta los Flows de todas sus versiones;
    - en Meta no se deshace;
    - el estado se ve: «Se deprecan cuando venzan los envíos abiertos» o «Deprecados el …».
  - **Lo que sigue igual:** respuestas, archivos, PDF, aprobaciones pendientes y acciones de reglas, cada una con la revisión con que llegó.
- **Reactivar** (`formtemplates.publish`): publica una versión nueva con el borrador actual. Pasa por la comprobación normal, crea Flows nuevos y la numeración sigue donde estaba.
- **Eliminar** (`formtemplates.manage`):
  - solo formularios sin respuestas, sin envíos abiertos (una respuesta que llegue después se perdería) y sin publicación en curso;
  - es un borrado lógico: desaparece de los listados y el prefijo queda reservado;
  - si llegó a publicarse, sus Flows se deprecan igual que al dar de baja.
- **Versiones reemplazadas:** al publicar una versión nueva, los Flows de la anterior se deprecan cuando ya no quedan sesiones vigentes de esa versión. Así Meta no acumula Flows en uso. Es el mismo mecanismo, sin pantalla.
- **Pantallas** ([tableros elegidos][tableros]):
  - **Diseñar formularios:** el segmentado suma «Dados de baja (N)», el estado dice «Dado de baja · versión N» y cada fila tiene en sus acciones Dar de baja, Reactivar o Eliminar, según corresponda y con permiso.
  - **Editor:** las mismas acciones en un menú «⋯» de la cabecera. Un formulario dado de baja muestra un aviso con quién y cuándo, y que no se manda ni lo ofrece el asistente.
  - **Diálogos:**
    - **Dar de baja** dice qué pasa: envíos abiertos hasta que vencen, qué sugerencias de otros formularios dejan de ofrecerse, que en Meta no se deshace y que las respuestas se conservan.
    - **Reactivar** muestra la versión nueva.
    - **Eliminar** solo aparece sin respuestas.
  - **Respuestas:**
    - los dados de baja salen de la lista principal, y sus planillas se ven con un filtro que los incluye y un aviso;
    - esa lista es la página «Formularios» del menú elegido, MF1: «Dados de baja (N)» con su aviso, y una línea si alguno espera una decisión ([pendientes](README.md#después-de-la-etapa-5)).

## Decisiones tomadas

No se vuelven a preguntar.

- Dar de baja conserva todo lo cargado: respuestas, fotos, PDF, aprobaciones y el Excel.
- No se borran respuestas desde el formulario. Borrarlas se decide con la retención, por fecha, que sigue pendiente. La basura de prueba se limpia con una herramienta de desarrollo, fuera del producto.
- Eliminar, solo sin respuestas.
- Reactivar es publicar una versión nueva.
- Los envíos abiertos se pueden responder hasta que vencen; recién después se depreca en Meta.
- **Tableros (2026-10-20):** se eligieron BA1-A, BA2–BA5 y MF1 de la versión 88 del lienzo ([diseño del front][tableros]). BA1-A pone las acciones en la fila de Diseñar formularios; BA2, en el menú «⋯» del editor; BA3 es el diálogo «Dar de baja»; BA4, reactivar y eliminar; BA5, el circuito de punta a punta. MF1 reemplaza Respuestas y Por aprobar del panel por «Formularios», dentro de «Gestión de formularios»: una tabla con todos los formularios y lo que cada uno tiene por aprobar, sin bandeja aparte.
- **Ajustes de los tableros (2026-10-20, versión 96 del lienzo):** al entrar por «Por aprobar · N», ↑ ↓ recorren lo que espera, de la más vieja a la más nueva, y desde la planilla, la planilla (MF1, D2 y DC0); en el celular, la tabla con su desplazamiento contenido (BA1-A y MF1); quien solo aprueba ve sus formularios y cuántas esperan, sin respuestas ni la última; los dos segmentados dicen «Activos (N) · Dados de baja (N)»; y BA6: la sugerencia hacia un dado de baja dice «Dado de baja» y no se guarda hasta quitarla.

## Fuera de esta etapa

- Copiar un formulario.
- Borrar respuestas por antigüedad (retención).
- La herramienta de limpieza de desarrollo, que va aparte.

## Cómo se encara

Se evaluó contra el código el 2026-10-20. Son dos etapas, una detrás de la otra y cada una con su review y su piloto:
- primero esta;
- después «Menú y detalle»: MF1 y el detalle completo, en los [pendientes](README.md#después-de-la-etapa-5).

El orden importa porque MF1 muestra los dados de baja.

**Primero, sin cambio visible:**
- **Un solo filtro de «se puede ofrecer»** en Infrastructure. Hoy `FormSelectionReader.Active()` (chat, asistente y sugerencias) y `FormSuggestionReader.FindOptionsAsync` repiten la misma condición. La baja se agrega una sola vez.
- **Las lecturas que dejarían de ver un formulario borrado:**
  - `FormDraftRepository.ExistsPrefixAsync`: el prefijo sigue reservado, y hoy chocaría con el índice único con un 500;
  - el `.Single()` de `FormDefinitionReader.FindChangeAsync`.

**Cortes:**
1. **Baja y reactivación dentro de BusinessForms, sin Meta:**
   - el estado en la definición, con su migración y su endpoint;
   - el filtro;
   - Diseñar formularios: segmentado, estado, cantidad de respuestas y acciones;
   - el menú «⋯» y el aviso del editor;
   - el diálogo con las sugerencias afectadas, con un lector inverso de los conjuntos de sugerencias activos;
   - el editor de sugerencias, que marca el destino dado de baja.
2. **Eliminar.**
3. **Deprecación en Meta,** entera dentro de WhatsApp, con el estado visible en el listado, el editor y el diálogo.

**Reglas para el código.** No se vuelven a preguntar.
- **El dado de baja:**
  - es un campo propio de la definición (quién y cuándo), no un valor más del estado de publicación;
  - `ActiveVersionId` se conserva, porque lo usan la planilla y las aprobaciones;
  - reactivar es la publicación de siempre: al activar la versión nueva, la baja se limpia.
- **Publicación en curso:** dar de baja o eliminar responde 409.
- **Una sola deprecación para la baja, el borrado y las versiones reemplazadas:**
  - el worker de WhatsApp toma los Flows publicados sin envíos abiertos y le pregunta a BusinessForms, por un puerto neutral, qué versiones ya no se usan;
  - no hay operación durable en BusinessForms ni escrituras entre módulos;
  - nunca depreca una versión con publicación en curso;
  - el estado va por Flow, con lease, fence y «Sin confirmar» consultado antes de repetir ([ADR 0018][0018]);
  - las sesiones tienen un índice por versión.
- **Otro puerto, de lectura,** le da a BusinessForms el vencimiento del último envío abierto y el estado de la deprecación.
- **Sin `formrecords.read`,** la cantidad de respuestas viene en null.

**Para «Menú y detalle»,** ya evaluado:
- **Arreglos previos en el front:**
  - separar el contenido del detalle de su panel (`FormResponseDetailPage`), para usarlo en D2 y en la pantalla completa;
  - permisos «leer o aprobar» en el menú y las rutas;
  - una sección plegable común;
  - un único componente de historia, para la pestaña Actividad y la pantalla completa.
- **El back:**
  - guarda qué acción generó cada documento, para el PDF en «Al aprobarla»;
  - un lector nuevo para la tabla de Formularios, con la cantidad de respuestas desde `LastRecordNumber` y un índice de tareas abiertas por formulario;
  - se retiran `api/forms/navigation` y `approvals/options`.
- **Las flechas ↑ ↓:**
  - al entrar por «Por aprobar» recorren los pendientes;
  - una pantalla completa abierta desde un enlace no trae los filtros privados.
- **La bandeja global** se borra al final de esa etapa.

## Cómo se cierra

- **Checks:** por corte y al cerrar, según [ADR 0018][0018] y [validar un cambio][validar].
- **TDD:**
  - estados y permisos;
  - el asistente, el selector y las sugerencias sin el formulario dado de baja;
  - la deprecación solo sin sesiones vigentes;
  - la reactivación con la numeración que sigue;
  - eliminar rechazado con respuestas.
- **Integración con Docker:** la operación durable de deprecación (incierta, consultada antes de repetir), una sesión abierta respondida después de dar de baja y las versiones reemplazadas.
- **Front:** los tableros en el navegador, es/en, a 390 y 320 px.
- **Piloto real con Meta:**
  - dar de baja con un envío abierto, responderlo igual y ver los Flows deprecados en Meta después del vencimiento;
  - reactivar y mandar la versión nueva;
  - eliminar un formulario sin respuestas.

[0018]: ../../decisions/0018-robustez-proporcional-al-efecto.md
[validar]: ../../guides/validar-un-cambio.md
[tableros]: ../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#los-tableros
