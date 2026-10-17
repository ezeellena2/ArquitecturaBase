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
17. **El piloto encontró un falso error al mandar un formulario desde el chat:** el recibo del envío trae `origin` y el esquema estricto del front lo rechaza, aunque el POST dio 202 y la consulta 200. Arreglo: sumar `origin` al contrato del front con su fixture exportada y un test.
18. **Tests que faltan.**
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

## Avance de las correcciones

- **1 cerrado:** el worker drena el lote completo y aísla fallas por candidato; prioriza la próxima consulta con desempate estable. Una ejecución incierta espera un pedido explícito y sólo se reconcilia. TDD rojo→verde: dominio 5/5 y bucle real 2/2; aplicación BusinessForms 232/232, arquitectura 148/148 y build sin errores ni advertencias. Sin Docker; integración, visual y navegador quedan para el cierre conjunto.

- **2 cerrado:** revisión humana atribuida y durable de un aviso incierto, con revisión esperada y consulta antes de repetir un POST sin respuesta. «No se envió» habilita reintentar el aviso al autor; el aviso a un rol conserva la falla individual visible sin reintento parcial. Consultar nunca reenvía. TDD rojo→verde: dominio 24/24, BusinessForms 237/237, WhatsApp 262/262, arquitectura 148/148; front 74/74, TypeScript y lint limpios (arquitectura 49/49). Build sin errores ni advertencias; migración aditiva sin cambios de modelo pendientes. Integración preparada para el cierre conjunto.

- **3 cerrado:** ofertas y referencias fijan los textos de catálogo disponibles por idioma; PDF y avisos eligen una cultura completa, considerando sólo textos que usan. Nuevos runs compactos, lectura del registro por Id, AppName obligatorio y deduplicación redundante retirada; JSON históricos intactos. TDD rojo→verde: BusinessForms 250/250, dominio 53/53, renderer real 8/8 y arquitectura 148/148. Build sin advertencias y migración nullable simétrica sin pendientes de modelo; captura PostgreSQL preparada y compilada para el cierre.

- **4 cerrado:** aviso al rol con resultado por persona: fallas individuales, falta de canal y rol vacío no bloquean lo siguiente; no se repiten aceptados ni hay retry parcial. Revisión por destinatario con replay tras revisar a otra persona; conserva motivos y aceptación tardía. TDD rojo→verde: BusinessForms 262/262, dominio 54/54, WhatsApp 262/262 y arquitectura 148/148; front 88/88, build, TypeScript y lint limpios (arquitectura 49/49). Migración nullable simétrica, sin pendientes de modelo. HTTP y capturas preparados; lectura viva de receipts tras Completed se completa en13 por lote, sin sondeo del worker.

- **5 cerrado:** reserva revalida el rol dentro del lock y la UoW; un rol borrado deja Failed con Forms.Rule.RoleUnavailable, sin tarea ni cambio de estado de la respuesta. TDD rojo→verde: reserva 8/8, BusinessForms 263/263 y arquitectura 148/148; build sin errores ni advertencias. Sin cambios de front ni Docker.

- **6 cerrado:** preview incluye metadata ficticia de foto y el renderer muestra la ilustración sin leer archivos privados. La completitud considera el nombre histórico del formulario; falta de traducción elige el original para PDF y avisos. TDD rojo→verde: BusinessForms 271/271, renderer real 9/9 y arquitectura 148/148; build sin advertencias. PNG y texto del preview revisados. Se preservó el commit externo de mecánica/tests y se completa su verde, sin Docker ni navegador.

- **7 cerrado:** hidratación liviana sin NameJson/SchemaJson por respuesta; listado y export comparten las versiones ya leídas, chat/detalle cargan sólo versiones distintas una vez. Sort con espacios exige SortFieldId y devuelve 400; el 429 de concurrencia incluye Retry-After/retryAfter. Facetas y orden de catálogo usan textos históricos por cultura UI con los mismos fallbacks; el export conserva Approved/Rejected. El front ya consume retryAfter, sin cambio adicional. TDD rojo→verde: cuatro casos de sort, tres de SQL y el rechazo 429; BusinessForms 275/275, SQL técnico Npgsql sin conexión 5/5, limiter TestServer sin Docker 1/1 y arquitectura 148/148. Build sin errores ni advertencias, diff limpio; sin migración. PostgreSQL para hidratación/estados y facetas/orden/búsqueda es/en queda preparado para la única corrida final.

- **8 cerrado:** «Sin respuesta» incluye preguntas ausentes en la versión en reglas, SQL y conteos; «No preguntada» mantiene ese conjunto y las celdas históricas siguen «—». TDD rojo→verde con presencia escalar/archivo, cero y false; BusinessForms **281/281**, arquitectura **148/148** y build sin advertencias. Paridad PostgreSQL y facetas con conjuntos explícitos preparadas y compiladas para el cierre conjunto, sin ejecutar Docker en este corte.

- **9 cerrado:** errores generales de reglas, PDF, aprobación, bandeja, documentos e historial usan un aviso común por operación; campos, 409, espera y respuesta propia siguen junto al control. Lecturas por QueryCache y escrituras/bytes con id contextual; éxito persiste al desmontar y un 401/403 no llega a mostrar tarea cacheada. TDD rojo→verde: 91/91 en seis archivos, build y TypeScript limpios; lint/arquitectura 49/49. Front 4ef7825. Sin cambios de layout, raw HTTP, Docker, visual ni navegador; limpieza privada de aprobaciones activada para11.

- **10 cerrado:** normaliza paths de arrays sin perder mensajes ni campos desconocidos, abre por Id la regla del primer error y enfoca el campo marcado, conservando el resto del borrador. La fixture exportada role-unavailable-es.json se consume íntegra y verificada, sin cambios. TDD rojo→verde: siete casos nuevos; 23/23 focales, build/TypeScript y lint limpios (arquitectura 49/49). Front 764d68f; sin cambios de contrato, Docker, visual ni navegador.

- **11 cerrado:** el detalle mantiene el panel mientras exista la tarea, también tras aprobar/rechazar y ante 409; conserva conflicto, motivo y foco sin repetir el POST por red. Approval, Response y Catalog comparten limpieza privada sólo de queries sin observadores, incluyendo remount y dos paneles; retirada por permiso independiente, sin TTL ni registro. TDD rojo→verde: ocho regresiones nuevas y 82/82 focales; build/TypeScript, lint y arquitectura 49/49 limpios. Front e3533e3; sin rediseño, Docker, visual ni navegador.

- **12 código y rápidos listos:** preview y copia fija usan visor nativo PDF local sin sandbox, con descarga del mismo blob autorizado; EmbeddedPreview conserva sandbox por defecto para Meta. Migas G2 con nombre confirmado, enlace según permiso y hoja actual durante carga. TDD rojo→verde: seis regresiones y 26/26 focales; build/TypeScript y lint limpios (arquitectura 49/49). Front a9848c0. La apertura y descarga reales en Chrome/Edge quedan inventariadas para la única recorrida final, junto a visual e integración.

- **13 cerrado:** cabecera con estado y acciones, «Por aprobar» e historial cronológico UTC; decisión con un único dueño y foco conservado. Entregado/Leído se observan por lote local también tras Hecho, sin reenviar ni modificar la ejecución; fechas reales separadas de la revisión humana, aceptación tardía retira revisión/retry obsoletos. TDD rojo→verde: receipts 9/9, BusinessForms+WhatsApp 551/551, SQL Npgsql sin conexión 1/1 y arquitectura 148/148; front 107/107, build/TypeScript, lint y arquitectura 49/49 limpios. Front cb242bb. Build backend sin advertencias; migración de dos columnas nullable sin backfill ni cambios pendientes. HTTP/PostgreSQL y capturas preparados para el cierre conjunto; sin D2, Docker, visual ni navegador en este corte.

- **14 código y rápidos listos:** condiciones en palabras, bloqueo de Guardar explicado y recuperación explícita del409 que conserva el borrador completo sobre la revisión leída, con aviso de reemplazo; otro409 exige releer. Listo, quitar regla/acción/condición y mover conservan foco. Selector múltiple compartido, adjunto PDF descartado al perder su validez y resumen de cambios requerido en historial, calculado contra la predecesora en la misma página SQL. TDD rojo→verde: BusinessForms307/307 y arquitectura148/148, build sin advertencias; front70/70, build/TypeScript, lint y arquitectura49/49 limpios. Front2b27e0d. PostgreSQL escrito/compilado; consumidor HTTP crudo history-es intacto y pendiente de refrescar con el exportador final único, sin default ni compatibilidad. Sin Docker, visual ni navegador en el corte.
