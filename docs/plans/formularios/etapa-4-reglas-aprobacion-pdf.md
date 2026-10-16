# Etapa 4 — Reglas, aprobación, PDF y avisos

Estado: En desarrollo · 2026-10-16.

Review de la Etapa 3: los 14 puntos de «Arreglar ahora» están cerrados con pruebas rápidas. Se implementa en el orden G1 → G5 → G3/G4 → G2 → G6. La integración con Docker, visuales y recorrido en navegador se ejecutan al terminar todo el desarrollo.

## Avance del desarrollo

- G1 cerrado: reglas e historial propios, configuración original obligatoria y traducción opcional; backend 164/164 BusinessForms y arquitectura 148/148, build limpio. Front `68ba254`, pruebas rápidas y build/lint limpios. La migración no deja cambios pendientes.
- G5: motor, recuperación, historial y controles implementados; pruebas rápidas 196/196 Application, 30/30 Domain y 148/148 arquitectura, build sin advertencias y migración coherente. La conexión con la captura se completa con el servicio PDF real en G2 y el canal de WhatsApp en G6.
- G3/G4 cerrado: dominio 44/44, Application BusinessForms 207/207, arquitectura 148/148 y build limpio. Front `206cf07`, `6b9e260`, `a51c985`, 139 focales/paridad/arquitectura y build/lint limpios; HTTP y visuales preparados para el cierre.
- G2 en desarrollo: diseño y copias fijas, captura conectada al motor. La verificación conjunta con Docker, visuales y navegador sigue reservada al cierre de todo.

## Qué se construye

Tableros: G1–G6 en [Formularios por WhatsApp][tableros]. Robustez según [ADR 0018][0018]: reglas y diseño del PDF son configuración interna (revisión y 409); mensajes, avisos y PDF son efectos externos (recuperación durable).

- **Reglas «Cuando llega una respuesta»** (G1).
  - Backend: `BusinessFormRuleSets` con revisiones inmutables y `ActiveRuleSetId` en la definición. Guardar crea la revisión y mueve el puntero en un commit, con `expectedRevision`; no toca borrador, versión ni WhatsApp. Rutas: historial, detalle y alta de revisión.
  - Cada regla: cuándo (se recibe, se aprueba, se rechaza), si (condición opcional, la gramática de la planilla) y hacer (acciones en orden).
  - Acciones: pedir aprobación a un rol, armar el PDF, mandar mensaje a quien la cargó, avisar por WhatsApp a las personas con un rol. Textos en el idioma original del formulario (el otro, opcional) con `{número}` y `{pregunta}` que se completan con la respuesta.
  - Front: sección del mismo editor, sin pestañas, con «Guardar reglas» propio, «Cambios sin guardar» e historial de revisiones.
- **Motor único de acciones** (G5).
  - Backend: `BusinessFormActionRuns`, creados en el commit de la captura (se recibe) o de la decisión (se aprueba o se rechaza). Fijan revisión, regla, evento, orden y configuración. Clave idempotente: respuesta + evento + regla + acción. En cada regla, cada acción espera a la anterior (`DependsOnRunId`); las reglas no se frenan entre sí.
  - Ejecutar: reservar el run, hacer el efecto fuera de la transacción con el mismo RunId y confirmar en otro límite. Un timeout queda «Sin confirmar» y se reconcilia antes de repetir.
  - Front: «Qué pasó después» en el detalle: esperando, en curso, hecho, no se pudo, sin confirmar, no aplica.
- **Aprobación de un nivel** (G3, G4).
  - Backend: `BusinessFormTasks` (aprobación, rol asignado). Pedir aprobación crea la tarea y deja la respuesta pendiente. Decidir exige permiso, pertenecer al rol, revisión vigente y no ser el autor. Cierra la tarea, cambia el estado y crea los runs siguientes en un commit; si otro decidió antes, 409.
  - Front: «Por aprobar» en el panel con su cantidad, de la más vieja a la más nueva. Aprobar o Rechazar en el detalle; rechazar pide motivo. Columna «Estado» en la planilla solo si el formulario pide aprobación.
- **PDF** (G2, G5).
  - Backend: una acción más del motor. Arma el PDF con datos, etiquetas, número y diseño fijados en el run, en el idioma de quien lo recibe. Lo sube con el almacenamiento de la 2B y registra `BusinessFormDocuments` en su propio límite. Se descarga solo por la API, con permiso.
  - Diseño: nombre en el idioma original (el otro, opcional), qué preguntas incluye, quién aprobó y cuándo, texto al pie, y la opción de incluir las fotos (12.2b). «Guardar diseño» crea una revisión.
  - Front: página «Diseño del PDF» desde «Ver el diseño» en la acción, con vista previa en los idiomas que tenga. En el detalle, la copia fija y su descarga.
- **Avisos** (G6).
  - Backend: salen por la cola actual de WhatsApp con `Origin` FormAction, `Kind` Text o Document, `RequestId` = RunId y `OperatorUserId` opcional para las salidas del sistema. Solo dentro de las 24 h; fuera, la acción queda «No se pudo» con su motivo.
  - Con las reglas de G1, el empleado recibe confirmación al llegar la respuesta, aviso con el PDF al aprobarse y motivo al rechazarse; en su idioma, con voseo y el número.

## Decisiones tomadas

No se vuelven a preguntar.

- Reglas con guardado e historial propios: no crean versión ni republican en WhatsApp.
- Aprobación de un nivel. Varios niveles o aprobadores, después.
- «Pedir aprobación» es la última acción de su regla: guardar una regla con acciones posteriores se rechaza. Lo que sigue va en «se aprueba» o «se rechaza». Nunca hay PDF de algo sin aprobar.
- La respuesta conserva la revisión de reglas con que llegó, en respuesta, tarea y runs. La decisión tardía usa esa revisión.
- Nadie aprueba lo que cargó. En WhatsApp el autor es la cuenta que confirmó, no el operador ni la IA. Se valida en el servidor, dentro del límite de la decisión.
- El rol asignado delimita quién decide; nunca reemplaza al permiso.
- PDF: diseño simple, en el idioma de quien lo recibe, copia fija. Cambiar reglas, catálogos o diseño no altera lo emitido.
- Un solo motor: sin `ActionOperations`, `ApprovalDecisions`, `documents.issue`, PDF a mano por fuera ni segunda cola de salida.

## Fuera de esta etapa

- Plantillas de Meta para escribir fuera de las 24 h.
- Varios niveles o aprobadores.
- Acciones de otros módulos (stock) y compensaciones.
- Cargar respuestas desde la web e importar desde Excel (después de esta etapa); correcciones.
- Asistente y sugerencias (Etapa 5).

## Cómo se cierra

- Checks por corte y al cerrar, según [ADR 0018][0018] y [validar un cambio][validar].
- TDD: validación de reglas, eventos, secuencia, decisión y autoaprobación. La misma condición da igual en SQL (planilla) y sobre la respuesta (reglas).
- Docker: 409 al guardar reglas; dos decisiones a la vez; run idempotente y dependencia; caída o timeout después del efecto; PDF ya subido; permiso o rol revocado. Varios hosts, solo para el motor.
- Prueba real: aviso por Meta dentro de las 24 h y PDF guardado en Azure que llega al teléfono, también en inglés.

## Requisitos previos

- Etapa 3 cerrada; gramática de condiciones de la Etapa 2.
- Etapa 2B cerrada: almacenamiento de archivos en Azure implementado y probado ([guía local][azure]).
- Permisos, con el formato de la [guía][permiso]: `formrules.manage` (reglas y diseño del PDF) y `formapprovals.decide` (aprobar o rechazar). Los PDF se ven con `formrecords.read`, como los valores.
- La clave de deduplicación de un aviso es el Id de su ejecución. Un envío del sistema «Sin confirmar» lo revisa quien tiene `formrules.manage`, desde G5: primero se consulta y recién después se reintenta.
- Una condición sobre una pregunta que no existe en la versión de la respuesta se toma como «sin respuesta». Hay una sola tarea de aprobación abierta por respuesta.
- El diseño del PDF tiene su propia tabla con revisiones por formulario, como las reglas; no forma parte de la versión de preguntas.
- Los PDF emitidos duran lo que dura la respuesta, igual que sus archivos.

## Ajustes ya decididos (2026-10-11)

- **Idioma (2026-10-16):** en lo que agrega esta etapa (reglas, mensajes y diseño del PDF) solo es obligatorio el idioma original del formulario; el otro es opcional. Si el idioma de quien recibe no está completo, el PDF y los mensajes salen enteros en el idioma original, sin mezclar. Lo de las Etapas 1–3 sigue exigiendo los dos idiomas hasta el cambio posterior a la Etapa 5.

- G1–G6 están elegidos.
- «Reintentar» no repite lo que ya salió confirmado. Si quedó «Sin confirmar», primero se consulta, como en el chat de la Etapa 1. El texto de G5 se ajusta a esa regla.
- «No aplica» es una acción cuya condición no se cumplió: solo se muestra, no es un estado de error.
- El editor dice «con el diseño vigente», no «de esta versión»: el diseño tiene revisiones propias (G2).
- G2 y G5 ajustados y elegidos el 2026-10-16 (lienzo, versión 75):
  - **«Incluir las fotos» (G2):** marcada, las fotos van chicas debajo de su pregunta y quedan en la copia fija; sin marcar, el PDF dice cuántas fotos hay; los documentos solo se nombran.
  - **«Reintentar» (G5):** retoma solo lo que no salió; lo «Sin confirmar» se consulta primero.


[tableros]: ../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#los-tableros
[0018]: ../../decisions/0018-robustez-proporcional-al-efecto.md
[azure]: ../../guides/azure-blob-en-local.md
[permiso]: ../../guides/permiso-nuevo.md
[validar]: ../../guides/validar-un-cambio.md
