# Code review: Etapa 4, reglas, aprobación y PDF — 2026-10-17

Revisión de lectura de backend `e07f1db8..ff1dc7b8` y front `69e2ba8..5aa6318`, que incluyen los arreglos del review de la Etapa 3. No hay huecos de seguridad: permisos, aprobación dentro del lock, idempotencia por ejecución, 24 horas y logs sin valores están bien. Se corrige al empezar el chat de la Etapa 5, antes de programarla, backend y front juntos, cada arreglo con su test y commits chicos. Las reglas del [cierre de la Etapa 4](../features/formularios.md#verificación-de-la-etapa-4--2026-10-16) y la regla de idioma del 2026-10-16 no cambian.

## Arreglar ahora

**Backend**

1. **Unos pocos «Sin confirmar» frenan el motor.** `FormActionBackgroundService.cs:16-27` procesa una ejecución cada 2 s, y las Uncertain vuelven a la fila cada 5 s para siempre, con el lock global de definiciones.
   - Escenario: tres avisos que Meta dejó inciertos (timeout, 5xx o circuito abierto) y no sale nada más: ni confirmaciones, ni tareas, ni PDF.
   - Arreglo: procesar el lote de ejecuciones reservables, ordenar por `NextCheckAtUtc` y no volver a consultar una Uncertain sin pedido. Agregar un test del bucle con ejecuciones atascadas, que hoy no existe porque el worker está apagado en los tests.
2. **Un aviso «Sin confirmar» no tiene salida y frena el resto de la regla.** `RetryAsync` solo reintenta Failed (`WhatsAppFormActionService.cs:54,67-75`), pero el detalle ofrece Consultar y Reintentar.
   - Arreglo: «Consultar» mira el estado de la salida. Si sigue incierta, una persona resuelve «salió» o «no salió», como «Revisado» en el chat. No se ofrece Reintentar mientras esté incierta.
3. **El catálogo Manual mezcla idiomas en la copia fija y en los avisos.** Se guarda un solo `option.Text`, en el idioma de quien respondió (`BusinessFormRecordRecorder.cs:125-128`).
   - Escenario: formulario en español y empleado con cuenta en inglés: el PDF en español dice «North branch».
   - Arreglo: guardar el texto del ítem por idioma al capturar, usarlo en el PDF y en los mensajes con la regla de idioma, y agregar un test.
4. **«Avisar a un rol» falla entero si un miembro no tiene WhatsApp, y frena lo que sigue** (`FormActionRecipientReader.cs:9-10`, `WhatsAppFormActionService.cs:27`).
   - Arreglo: mandar a los que se puede, mostrar a quién no y no bloquear. Un rol vacío no frena las acciones siguientes.
5. **Pedir aprobación a un rol borrado deja la respuesta pendiente para siempre** (`FormActionDispatchService.cs:37`).
   - Arreglo: verificar el rol al reservar y fallar de forma visible con `Forms.Rule.RoleUnavailable`, sin abrir la tarea.
6. **La vista previa del PDF nunca muestra fotos** (`FormPdfPreviewIssuer.cs:18-20`). Además, da 500 si falta el nombre en un idioma (`SkiaFormPdfRenderer.cs:177`).
   - Arreglo: archivos de ejemplo, el nombre dentro de la completitud y un test con el renderer real.
7. **Arreglos de la Etapa 3 incompletos.**
   - `SchemaJson` y `NameJson` todavía viajan por fila en el listado y el export (`FormRecordReader.cs:20-21`).
   - `sort=field%20` sin `sortFieldId` da 500 (`FormRecordFilterPolicy.cs:15`).
   - El 429 no lleva `retryAfter`.
8. **«Sin respuesta» sobre una pregunta que no existe en la versión da falso** (`FormRecordConditionEvaluator.cs:20`, `Filters.cs:37`). El plan decidió que cuenta como «sin respuesta».
   - Arreglo: aplicarlo igual en las reglas y en el SQL, manteniendo la paridad. La planilla sigue mostrando «—».

**Front**

9. **Las pantallas nuevas muestran los errores generales como párrafos y a veces dos veces:** reglas, diseño del PDF, decisión, bandeja, documentos e historial.
   - Arreglo: `notify` con un id por operación. Junto al campo o en la pantalla queda solo lo que sigue vigente: el 409, la espera y «tu propia respuesta».
10. **Los errores de campo de las reglas nunca se marcan.** El backend usa `rules[0].actions[0].roleId` y el front busca `rules.0.actions.0.roleId`.
    - Arreglo: normalizar los paths, abrir la regla del primer error y agregar un test con la fixture `role-unavailable-es.json`, que está exportada y sin uso.
11. **En el detalle, el panel de decisión desaparece al decidir.** Un 409 se ve un instante y el foco cae en `body`.
    - Arreglo: mantener el panel mientras exista la tarea, avisar el 409 y llevar el foco a la decisión. Agregar un test en el detalle.
12. **La vista previa y la copia fija del PDF van en un iframe con sandbox.** Chrome y Edge no abren ahí su visor.
    - Arreglo: `<object>` o un iframe sin sandbox, con descarga como alternativa, y verificarlo en un navegador real.
13. **Detalle e historial frente a G4 y G5.**
    - La cabecera no muestra el estado ni las acciones.
    - El historial no está en orden cronológico.
    - El estado dice «Pendiente» en lugar de «Por aprobar».
14. **Reglas frente a G1.**
    - El «si» no está en palabras: hay que reutilizar `responseFilterLabels`.
    - «Guardar reglas» queda deshabilitado sin motivo visible.
    - Después de un 409 no hay forma de guardar.
    - El foco cae en `body` tras Listo, quitar o mover.
15. **Arreglo de la Etapa 3 incompleto en filtros.** Un error del conteo deshabilita Aplicar, y Reintentar aparece también ante un 400.
16. **Textos.**
    - La bandeja, al fallar, dice «No pudimos cargar los formularios».
    - `wait` dice «antes de consultar» al guardar, decidir y descargar.
    - Desde la bandeja, volver dice «Volver a respuestas».
17. **Tests que faltan.**
    - El recorrido captura → tarea → decisión → PDF → aviso, en inglés y con caída al idioma original.
    - El catálogo en otro idioma.
    - El presupuesto vencido y la liberación del límite del export.
    - En el front: errores de campo de reglas, decisión en el detalle (409, foco, red), un solo aviso al rechazar, orden del historial y conteo con 400 y 429.

## Con la Etapa 5 o al tocar esos archivos

- **Cada ejecución copia la respuesta entera, y la del PDF dos veces** (`FormActionRunRecorder.cs:91-92`, `FormPdfActionService.cs:23`). Hay que fijar solo lo que cambia y leer el registro por Id.
- **Los avisos a un rol traban el chat manual en la cola única.** Medirlo y documentarlo.
- **Límites del export y del PDF:** el límite de exportación es por proceso, y los emojis salen como cuadrados en el PDF.
- **Hacer de más:**
  - el control de duplicados inalcanzable en `FormPdfDocumentRecorder`;
  - `AppName` por defecto para snapshots que no existen;
  - el puerto de retención privada;
  - tres limpiezas privadas casi iguales en el front;
  - `NativeSelect multiple`.
- **Front, frente a los tableros:**
  - filtro de Estado y PDF en las filas aprobadas;
  - resumen de cambios en el historial de reglas;
  - Entregado y Leído en el historial;
  - migas del diseño del PDF;
  - «Adjuntar el PDF» que queda marcado al cambiar de evento.
