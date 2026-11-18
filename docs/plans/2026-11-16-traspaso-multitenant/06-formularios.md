# Traspaso 14 · 06 · Formularios

Los arreglos que salieron de la revisión de arquitectura de Formularios del 14/11, programados el 15 y el 16 de noviembre
(backend `23797fff..HEAD`, front `7b95095b..HEAD`). La revisión miró el módulo con 30 criterios, por piezas, y verificó 48
hallazgos (F1 a F48; [revisión][rev], commit `39b6de46`). El usuario tomó sus ocho decisiones el mismo día (`739d8ea5`) y
eligió hacer las 41 tareas del [plan][plan], en siete etapas, antes de la prueba integral («Todo antes»). Se programaron 39,
con dos revisiones adversariales. Faltan dos, al final de este archivo.

Son reglas de producto y de seguridad, no código para copiar. Cada punto lleva sus commits (backend, salvo «front») y su tarea o hallazgo.
**Todo está commiteado, pero no pasó la prueba integral.** Reglas vigentes: `docs/features/formularios.md` y sus pares; estilo:
[01 del traspaso 13](../2026-11-14-traspaso-multitenant/01-formularios.md).

## 1. Los números con la región del idioma (Etapa 1, F1)

La única etapa que iba antes de todo, porque cambia datos sin avisar. Tarea 1 del [plan][plan], decisión 5.

- **En la app.** Un decimal se lee con la región del idioma de quien carga: en español, coma decimal y punto de miles; en inglés, al revés. Lo que se
  lee distinto con y sin los miles («1.500» en español, un solo separador y tres cifras) es un error de su pregunta, `numberAmbiguous`: pide escribirlo
  sin separador de miles. Al servidor le llega siempre el número canónico, con punto. Es el mismo criterio de la importación de catálogos. Front
  `8f518391`.
- **En el Flow de WhatsApp.** El decodificador lee con la cultura del Flow y lo ambiguo se rechaza con `WhatsApp.Flow.NumberAmbiguous`, por el camino de
  la validación fija; el Flow compilado no cambia. Cambia la regla de `whatsapp.md` que aceptaba el costo con cualquier separador. Backend `0c88e7ea`;
  reglas en `59873523` y `d1fe1ef4`.
- Se descartó cambiar sólo la app. La ayuda por defecto «sin separador de miles» no se programó: el error alcanza.

## 2. Permisos y privacidad (Etapa 2)

- **Quien fijó los valores no aprueba (F4, tarea 2).** La tarea de aprobación guarda también a quien corrigió la respuesta o la pasó desde un documento
  recibido, y la decisión, la bandeja, su conteo, el resumen y el árbol lo dejan afuera como al autor; decidir siendo uno de ellos da 403. La guarda que
  crea un borrador a nombre de una persona (también por canal) pide cuenta activa. `054a3566`, `3207e2b4`, `7e04e3df`.
- **Guardar una regla que cita respuestas pide «Ver respuestas» (F5, decisión 3, tarea 3).** Además de «Administrar reglas y PDF», si un aviso usa datos
  de preguntas, adjunta el PDF (`IncludePdf`) o manda una copia (`SendCopy`). Lo mismo al agregar un estándar cuyas reglas los citan. Código
  `Forms.Rule.RecordsReadRequired`; el editor de reglas muestra el texto del servidor. `a765080f`, `974fea7c`, `b9344a0c`, `673d5bcc`; front `3e000326`.
- **Los temas de «Formularios que faltan» se revisan antes de ofrecerse (F7 y F14, decisión 7).** Un tema nuevo, o uno descartado que reabre otro
  pedido, no llega a otras personas hasta que alguien con «Diseñar formularios» lo marca como revisado (`POST /api/forms/missing-requests/{id}/review`,
  con la revisión esperada); los existentes quedan sin revisar. El asistente recibe sólo el nombre del tema, y su completitud se separa de la de los
  formularios. `39812a81`, `60e815aa`, `79e74667`; front `32db5ad9`.
- **El menú muestra sólo lo que la persona puede abrir (F20).** Usa el filtro de formularios visibles del árbol; una cuenta que sólo decide aprobaciones
  no ve el resto. `1b1d5165`.
- **B2, la cuenta borrada.** Su test **falla** (una cuenta borrada conserva los permisos de sus roles): no se commiteó en rojo y el
  arreglo es del núcleo.

## 3. Reglas y acciones (Etapa 3)

- **Una regla que no puede seguir avisa (F2, decisión 2, tarea 7).**
  - Una acción en duda por un error local (el almacenamiento o el canal cortaron, o un PDF en duda) se consulta sola, con espera creciente (15 segundos,
    el doble cada vez, hasta 15 minutos, ocho veces). La consulta nunca reenvía; pasadas las ocho queda «En duda» para una persona. Lo que pide una
    decisión sigue a mano: un aviso que el canal no puede confirmar y una emisión en revisión o a la espera del «¿La emito?».
  - **Aviso a quien cargó:** cuando su regla se frena, le llega un aviso, una sola vez por acción, por el canal durable de los avisos (es lo que pedía
    ARCA H11).
  - **Ventana de 24 horas cerrada:** el aviso que no puede salir queda «No aplica» para esa persona, con su motivo, y no frena lo que sigue. Se descartó
    la plantilla aprobada por Meta (pide el trámite de plantillas).
  - `4dc53b94`, `63edb23e`, `f4041e4e`, `7c0b0bfa`; front `511b6800` (estado «No aplica» con su texto y tono neutro).
- **Quién pidió reintentar o consultar (F19) y los tipos de acción (F28).** El run guarda quién y cuándo, y el historial lo muestra (`lastRequest`;
  `760fee98`, front `511b6800`). El worker reserva sólo los tipos que conoce, y un tipo desconocido espera a una copia que lo entienda (`4dc53b94`,
  `37c56149`).
- **Tres reglas escritas (decisión 4, tarea 10).** `312898b2`, `0ccdee62`.
  - Las horas del PDF van en la zona de la cuenta de quien cargó, con su nombre, en lugar de UTC.
  - Un aviso a un rol saltea al mandar, como «No aplica» (`Forms.Action.NoLongerInRole`), a quien ya no es del rol.
  - Ante un toque viejo en «¿La emito?», quien cargó recibe una vez «Esa respuesta ya no se puede emitir», en su idioma
    (`Forms.Action.NoLongerIssuable`); lo ajeno se sigue ignorando.
- **B1.** Un «¿La emito?» retomado reusa la vista previa guardada, y el botón del mensaje sigue decidiendo. `9fd3bfbc`, `0ccdee62`.
- **B3, «Pedir aprobación» a un rol sin personas activas (tarea 12).** La acción falla con `Forms.Rule.RoleWithoutMembers`, sin abrir una tarea que
  nadie puede decidir, y se reintenta cuando el rol tiene a alguien. Quien cargó o corrigió no cuenta como miembro. `84e22c2f`, `07839c92`. El editor no
  advierte.
- **Se puede seguir una respuesta de la captura al aviso (F18 y O8).** Los logs de los workers llevan el id del run, de la operación o de la propuesta;
  las purgas registran sólo el tipo de la excepción. Cada run y cada publicación guardan el contexto de traza W3C (`TraceParent`) y su worker lo
  continúa (fuente `ArquitecturaBase.BusinessForms`). `032a682d`, `e25b41c1`.

## 4. Publicación (Etapa 4, tareas 14 a 17 del [plan][plan])

- **Una publicación trabada se puede descartar (F3).** Las operaciones en curso o en duda se vuelven a mirar con espera creciente, como el retiro.
  «Descartar esta publicación» (`POST api/forms/definitions/{id}/publications/{requestId}/discard`, con la revisión esperada, 204) la puede usar
  cualquiera con «Publicar formularios»: la cierra como fallida y deja que el puerto existente retire sus Flows; sólo se descarta lo sin confirmar.
  Quien publica otra vez recibe el id de la operación abierta, así otra persona llega al descarte desde el formulario. El front sigue consultando con
  espera larga mientras dice «sin confirmar». `fbec87c5`, `baa6245e`, `2aa7d2bd`, `d73177a1`; front `bb99803a`.
- **Las lecturas no descifran el manifiesto (F27).** El detalle, el editor, las respuestas y las aprobaciones leen los idiomas sellados de la versión y
  responden 200 aunque el manifiesto sea ilegible; sólo el despacho y la vista oficial lo descifran. `5f2bb56d`.
- **La vista oficial tiene su propio límite (F17).** Límite y plazo propios para la vista y la consulta del estado; la consulta de una publicación en
  curso vuelve al límite de lectura. `9f027356`, `4fce41dc`.
- **La lista de formularios del asistente (F48)** se ordena por nombre antes de cortar en 100. `e590dc95`.

## 5. Documentos recibidos (Etapa 5, tareas 18 a 25)

- **Un PDF hostil no frena la lectura (F6 y O3).** Los flujos comprimidos se inflan con un tope acumulado; cada lectura tiene un plazo que contesta
  «ilegible» y libera su lugar; el worker de WhatsApp cuenta los intentos de un mensaje (migración `WhatsAppDocumentReadAttempts`); un PDF con
  `/Encrypt` es «Protegido», salvo el que sólo restringe permisos, que se abre con contraseña vacía. `7a30c616`, `97a6dbe2`, `ca730ae4`, `6dcaef6f`.
- **El documento propuesto vence (F9, decisión 6).** Uno que nadie revisa en 90 días (desde el último cambio o el fin del envío), o uno descartado que
  alguien reabre, pasa a descartado con motivo «venció» («Nadie lo revisó en 90 días: venció.») y sigue la purga. Cambia la regla de los documentos
  recibidos. `78263d6f`; front `f03450b0`.
- **El revisor ve la repetición al pasar un documento (F13); «Cómo reconocerlo» dice que lo escribió la IA (F23, front `bd63e3c2`).** Si repite una respuesta guardada, pasar da 409 `Forms.ReceivedDocument.Repeated` y el
  revisor lo confirma con una marca en el pedido (`confirmRepeated`), desde un aviso. La repetición se busca al pasar, no al leer el detalle.
  `6ff85b99`; front `ac8cfe3b`.
- **Las extracciones en curso sobreviven a un despliegue (F31, F26).** El pedido guarda las versiones con que se selló y el contexto se rearma con ésas;
  un «no encontrado» del motor espera al vencimiento del pedido (7 días). Quitar la IA pide descartar o resolver antes las extracciones pendientes.
  `0c5ddba2`, `14425785`, `72f567e9`.
- **El menú cuenta sólo los documentos abiertos (F38)** y **la purga corre al arrancar y en lotes seguidos (F40)**, cada lote en su transacción y con
  tope de tiempo. `34b4e557`, `a587c396`, `37c56149`.
- **Los números en claro de la bandeja, por escrito (decisión 8)**, y la retención de respuestas que nombra todo lo de F10, con el borrado definitivo de
  una persona como puerto del núcleo. `0a89d4e0`.

## 6. Carga, planilla y catálogos (Etapa 6, tareas 26 a 36)

- **Un rechazo pasajero de la captura no queda pegado (F8).** Un permiso perdido en una carrera (`AuthorizationChanged`, `Forbidden`) no deja recibo,
  así repetir el pedido se decide de nuevo; tras un 400 sin errores por pregunta, el front arma un pedido nuevo. Los textos de `ReplayConflict` y
  `AuthorizationChanged` ya no hablan de la sesión. `aba97b16`; front `65e89b74`.
- **Topes de carga (F11, F12).** A lo sumo dos exportaciones o importaciones de catálogo a la vez por copia (429 con `Retry-After`); la planilla que se
  infla no pasa (se cuentan también estilos y textos compartidos). `13eb9b41`, `2760dc30`, `79c9d792`.
- **Los campos históricos de catálogo, en una tabla chica (F15).** `BusinessFormRecordCatalogFields` (formulario, catálogo, campo) la escribe la
  captura, nunca la lectura, y la planilla la lee por índice. `5fd3972d`, `8dadf004`.
- **Una regla no nombra una opción que no existe (F16).** Guardar una regla o sugerencia comprueba sus opciones (`Forms.Rule.OptionUnavailable`, 400) y
  borrar opciones toma antes el lock de definiciones. `fc86240b`.
- **«Agregar» un estándar a la vez (F21).** Sus catálogos se resuelven dentro del guardado; un doble clic devuelve el mismo borrador, y con otra carpeta
  es un conflicto (`Forms.Library.CatalogAppeared`, 409). `fe6ed930`, `96bee949`. Por WhatsApp, crear un borrador dice el motivo (F22, `ec9d212c`).
- **Crear un catálogo es idempotente (F24).** `requestId` opcional (el front siempre lo manda): repetido devuelve el catálogo ya creado; con otro
  contenido, 409. `fb0545a0`, `f8c6f6f3`; front `133b4686`.
- **Escala chica (F33, F37, F47).** Índice por formulario y fecha de recepción; la captura revisa las opciones por lote; el menú arma el
  índice de carpetas una vez, con tope de 500 (`Forms.Folder.TooMany`). `38b8db06`, `23573b08`, `64874daf`.

## 7. Versiones y documentación (Etapa 7, tareas 37 a 41)

- **Convivencia de versiones (F29 y F30).** Un CHECK que ata una columna nueva a una vieja va en el despliegue siguiente, y la versión vieja tolera lo
  que escribe la nueva (CHECK nuevos, nulos nuevos, borrado con su reemplazo, renombres): lo comprueban `MigrationCoexistenceTests` y
  `FormStoredEnumTests`, y el comprobante de guardado ignora lo que no conoce. `2b816506`. Excepción conocida: `FormActionRunState.NotApplicable`.
- **La guía para quitar Formularios, al día (F25).** Cuenta las 30 tablas del módulo, dice qué le queda a ARCA (sin Formularios no tiene emisor) y que
  el asistente de WhatsApp pide las `forms.*` sólo si un dueño instalado las declara (B5 del núcleo). `FormRemovalGuideTests` compara su lista con el
  modelo. `2bc41b3e`, `a80a6532`.

## 8. Contratos HTTP y migraciones

- **Rutas nuevas:** `POST api/forms/definitions/{id}/publications/{requestId}/discard` (204) y `POST api/forms/missing-requests/{id}/review` (200). El
  OpenAPI pasó a 123 operaciones.
- **Cambios en lo que ya existía:** `PassFormReceivedDocument` suma `confirmRepeated`; el tema de «Formularios que faltan» trae su estado de revisión;
  el historial de acciones trae `recipientStates` («No aplica») y `lastRequest`; crear catálogos recibe `requestId`; la página de la bandeja suma
  `groups` sólo con temas revisados; la lectura de una publicación en curso llega a quien publica.
- **Errores nuevos:** `Forms.Rule.RecordsReadRequired`, `.OptionUnavailable`, `.RoleWithoutMembers`, `Forms.Action.NoLongerInRole`, `.NoLongerIssuable`,
  `Forms.ReceivedDocument.Repeated`, `Forms.Library.CatalogAppeared`, `Forms.Folder.TooMany` y `WhatsApp.Flow.NumberAmbiguous`; el front de la app tiene
  `numberAmbiguous` por pregunta.
- **Migraciones (todas aditivas, sin datos que convertir):** `ReviewMissingFormTopics`, `WhatsAppDocumentReadAttempts`,
  `AddFormReceivedDocumentExtractionContext`, `FormActionRunRequestsAndAutoConsult`, `AddFormCatalogCreationRequest`, `AddFormRecordReceivedDateIndex`,
  `AddFormRecordCatalogFields` (tabla nueva, rellenada en la misma migración), `FormActionRunStallNotice` y `FormTraceParent`. El multitenant las rehace
  con su modelo.

## 9. Lo que falta y los menores sin programar

- **F2, la marca de la planilla.** Hecha el 2026-11-17 (`06ac5482`; front `0b3a75c6`): la columna «Reglas» marca «Por resolver» para todo el que ve
  la planilla, y quien administra reglas y ve todas las respuestas tiene la pestaña «Por resolver (N)», que es el filtro y el contador. Cuenta una
  acción fallida, o en duda esperando a una persona, aunque ya se haya avisado a quien cargó; sin reintento, sigue hasta anular o corregir la respuesta.
- **Repetir la prueba de quitar Formularios** en una copia (migración `RemoveBusinessForms`, su `Down`): hecha el 2026-11-17 sobre `72510c01`
  (`04df45ef`), con la guía corregida; el detalle está en el [02 del traspaso 15](../2026-11-18-traspaso-multitenant/02-estado-al-18-11.md#la-prueba-de-quitar-formularios-repetida-2026-11-17).
- **El cierre:** hecho el 2026-11-17, en verde (la corrida completa con Docker, `MigrationsTests` y la exportación de cohortes). Falta la prueba integral.
- **Menores anotados** ([plan][plan], «Qué falta»): la tabla de F15 no se completa con un campo sellado sólo por una captura vieja; la migración de F15
  aborta con un campo sin `name` o `key` (la base de desarrollo no los tiene); un «no encontrado» del motor deja la extracción «leyendo» hasta siete
  días; quien corrigió una versión anterior aún puede aprobar; el tope de carpetas sólo vale al crear.

## 10. Para el multitenant

- **Dónde cae.** E21: F2, F19, F28, F5, B1, B3, las tres reglas y F18. E15 y E17: F4, F20, F12, F15, F33. E14: F3, F17, F27. E25: F6, F9, F13, F23, F31,
  F38, F40. E22: F21, F47. E23: F7, F14, F22, F48. E12 y E19: F1, F16, F24. E20: F8, F37. F29 y F30 son una regla de despliegue de toda la etapa.
- **Qué adaptar.** Cada tabla nueva lleva `organization_id` y RLS, y los índices de F33 y de carpetas, por organización. Los permisos («Publicar»,
  «Diseñar formularios», administrar reglas) son los de la organización, con `[FeatureGate("forms")]`. La zona horaria del PDF podría ser la de la
  empresa. Los topes globales (F11, F12) pasan a ser también por organización, y la purga de F40 recorre organizaciones.
- **Preguntas para el usuario de allá** (ArquitecturaBase eligió la primera de cada una):
  1. ¿Las horas del PDF van en la zona de la cuenta de quien cargó (recomendada) o en la zona de la empresa?
  2. ¿El aviso a quien cargó, cuando su regla se frena, sale por el canal durable de avisos y queda «No aplica» con la ventana de 24 horas cerrada
     (recomendada), o se pide una plantilla aprobada por Meta?
  3. ¿Un tema de «Formularios que faltan» nuevo se revisa antes de ofrecerse (recomendada), o se ofrece de entrada?
  4. ¿Un documento propuesto sin revisar vence a los 90 días (recomendada) o se queda hasta la decisión de retención?
  5. ¿Quien corrigió o pasó una respuesta queda afuera de su aprobación (recomendada), o alcanza con excluir al autor?

[plan]: ../../history/README.md#barrido-documental-del-2026-11-18
[rev]: ../../reviews/2026-11-14-arquitectura-formularios.md
