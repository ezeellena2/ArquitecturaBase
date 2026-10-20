# Dar de baja, reactivar y eliminar formularios

Estado: decidido el 2026-10-20; tableros por dibujar; sin empezar. Arranca después del arreglo del envío manual sin prellenado.

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
  - solo formularios sin respuestas y sin operaciones abiertas;
  - es un borrado lógico: desaparece de los listados y el prefijo queda reservado;
  - si llegó a publicarse, sus Flows se deprecan igual que al dar de baja.
- **Versiones reemplazadas:** al publicar una versión nueva, los Flows de la anterior se deprecan cuando ya no quedan sesiones vigentes de esa versión. Así Meta no acumula Flows en uso. Es el mismo mecanismo, sin pantalla.
- **Pantallas** (tableros a dibujar):
  - **Diseñar formularios:** el segmentado suma «Dados de baja (N)», el estado dice «Dado de baja · versión N» y cada fila tiene en sus acciones Dar de baja, Reactivar o Eliminar, según corresponda y con permiso.
  - **Editor:** las mismas acciones en un menú «⋯» de la cabecera. Un formulario dado de baja muestra un aviso con quién y cuándo, y que no se manda ni lo ofrece el asistente.
  - **Diálogos:**
    - **Dar de baja** dice qué pasa: envíos abiertos hasta que vencen, qué sugerencias de otros formularios dejan de ofrecerse, que en Meta no se deshace y que las respuestas se conservan.
    - **Reactivar** muestra la versión nueva.
    - **Eliminar** solo aparece sin respuestas.
  - **Respuestas:**
    - los dados de baja salen de la lista principal, y sus planillas se ven con un filtro que los incluye y un aviso;
    - dónde está esa lista lo define el rediseño del menú de Formularios, que se dibuja junto con esto ([pendientes](README.md#después-de-la-etapa-5)).

## Decisiones tomadas

No se vuelven a preguntar.

- Dar de baja conserva todo lo cargado: respuestas, fotos, PDF, aprobaciones y el Excel.
- No se borran respuestas desde el formulario. Borrarlas se decide con la retención, por fecha, que sigue pendiente. La basura de prueba se limpia con una herramienta de desarrollo, fuera del producto.
- Eliminar, solo sin respuestas.
- Reactivar es publicar una versión nueva.
- Los envíos abiertos se pueden responder hasta que vencen; recién después se depreca en Meta.

## Fuera de esta etapa

- Copiar un formulario.
- Borrar respuestas por antigüedad (retención).
- La herramienta de limpieza de desarrollo, que va aparte.

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
