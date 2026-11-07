# La carga desde la app, etapas 2 a 5: traspaso 7 al multitenant (2026-11-06)

Autor: el chat guía de ArquitecturaBase. Destino: ArquitecturaBaseMutitenant, para sus etapas **E20 · Cargar desde la web**,
**E21 · Reglas y aprobación**, **E14 · Formularios** y **E15 · Respuestas**, y para el texto de diseño de cada una
(`docs/plans/formularios/` del multitenant).

El [traspaso 3](2026-10-30-traspaso-multitenant-carga-desde-la-app.md) contó las once decisiones de la carga desde la app y
cómo quedó la Etapa 1. El multitenant ya las tomó en su hoja de ruta: lo propio (83 y 85), los títulos de sección (88),
corregir y anular (91 a 93) y las listas dependientes (94). Acá se programaron las etapas que faltaban, el 2026-11-04 y el
2026-11-05, y con ellas cerró el [plan de la carga](2026-10-29-cargar-formularios-desde-la-app.md):
- la 2, lo propio de quien sólo carga (CW6-C);
- la 4, las listas dependientes (CW7);
- la 3, corregir y anular (CW10-A y CW11-A);
- la 5, los títulos de sección (CW15-B).

Este traspaso cuenta cómo quedó cada una, lo que se encontró al programarlas y en qué difiere de lo que el multitenant ya
escribió. Las decisiones no se repiten: están en el §2 del traspaso 3, con los mismos números.

**Como los anteriores, no es código para copiar** (decisión 18 de su hoja de ruta) ni hace falta para dibujar (decisión
108): los tableros de acá son un ejemplo para adaptar con sus piezas y su decisión 43.

**Se lee después de los traspasos 4 y 5,** que el multitenant todavía no incorporó: la planilla de lo propio es la de PL1-A y
PL2-A, la corrección usa las zonas y los dos anchos de AR1-A y sus acciones van con los íconos de TL3-A.

## 1. En pocas palabras

- **Lo propio (Etapa 2):** quien sólo tiene «Completar formularios» lee lo que cargó, por la app o por WhatsApp, en la misma
  planilla recortada: el estado, el motivo de un rechazo, los archivos y el PDF. El servidor acota cada lectura, y una
  respuesta ajena da el mismo 404 que una inexistente. No hay permiso nuevo.
- **Listas dependientes (Etapa 4):** un tercer filtro de una pregunta de catálogo, «según otra respuesta». Es sólo para la
  app: un formulario que lo usa no se manda por WhatsApp hasta que exista el intercambio con Meta, y publicar lo avisa sin
  frenar.
- **Corregir y anular (Etapa 3):** corregir guarda otra respuesta, que reemplaza a la anterior con el mismo número y vuelve a
  pasar por las reglas. Anular deja la respuesta con su número, marcada «Anulada» y con su motivo. Nada se borra. Trae un
  permiso nuevo, una migración y la regla de inmutabilidad reescrita.
- **Títulos de sección (Etapa 5):** el autor pone títulos entre las preguntas. La app arma una zona por título y WhatsApp lo
  muestra como subtítulo. La planilla, el Excel y el PDF no cambian.
- **Lo que difiere del multitenant** (§5): casi todo coincide con lo que ya escribió. Hay una sola decisión para el usuario
  («Mis cargas» en «Formularios») y varios detalles para sumar a sus etapas.
- **Falta acá** (§6): la prueba manual de las cuatro etapas, cerrar la verificación con Meta (que ya aceptó los títulos) y la
  cámara de un celular real, que quedó de la Etapa 1.
- **Parte de los commits no está en GitHub.** Desde esta PC se leen en `C:\Users\ezequ\source\repos\ArquitecturaBase` y
  `C:\Users\ezequ\source\repos\ArquitecturaBaseFront` (§7).

## 2. Lo que decidió el usuario

Son las decisiones del §2 del traspaso 3, del 2026-10-29 y el 2026-10-30:
- la 3, lo cargado en la planilla de siempre (CW6-A y CW6-C);
- la 4, las listas dependientes sólo en la app (CW7);
- la 6, corregir (CW10-A);
- la 7, anular con motivo (CW11-A);
- la 8, quién corrige y anula;
- la 11, los títulos de sección con zonas plegables (CW15-B y CW16).

Al programarlas no cambió ninguna. Lo que el plan no fijaba se resolvió con la opción más simple, y está en el §3. Las
elecciones posteriores que valen para toda la app (PL1-A, PL2-A, TL1-A, TL3-A y AR1-A, del traspaso 5) cambiaron algo del
dibujo, como dice cada etapa.

## 3. Cómo quedó

### Etapa 2 · Lo propio de quien sólo carga (CW6-C, 2026-11-04)

Backend:
- Las lecturas de respuestas (planilla, conteos, navegación, detalle, archivos, documentos y workflow) se abren con «Ver
  respuestas» (`formrecords.read`) o con «Completar formularios» (`formrecords.create`). Un guard decide: con lectura, todo;
  sólo con completar, lo propio, y revalida ese permiso al terminar. El Excel sigue pidiendo lectura y exportación.
- Lo propio es la respuesta con su autor, el mismo para la app y para WhatsApp. La planilla, los conteos y la navegación
  llevan el dueño en el pedido interno, nunca desde el cliente. El conteo de personas sólo la nombra a ella, el de un
  catálogo sólo trae las opciones que eligió y un filtro por otra persona no devuelve nada. El detalle, un archivo, un
  documento o el workflow de una respuesta ajena dan el mismo 404 que una inexistente.
- El workflow de lo propio trae el estado, la tarea con su decisión y su motivo, y los documentos. No trae las acciones
  internas (los avisos y sus destinatarios).
- «Formularios» (`GET /api/forms/overview`) cuenta las respuestas propias de cada formulario y trae la última propia: número,
  fecha y estado. Quien también lee ve el total de siempre.

Front:
- «Formularios» suma «Mis cargas» y «Mi última carga», con número y fecha; sin cargas, «Todavía no cargaste».
- La planilla es la de siempre sin la columna «Cargó», sin «Cargó» en «Filtros» y sin «Exportar a Excel». Conserva
  «Agregar respuesta».
- El detalle dice en qué quedó: cuándo llegó y por dónde, el motivo si la rechazaron (sin el rol ni quién decidió), la
  respuesta, las fotos y el PDF. No tiene Actividad.
- Después de enviar, «Ver en la planilla» lleva a su planilla.

### Etapa 4 · Listas dependientes (CW7, 2026-11-04)

Backend:
- **El filtro.** Cada filtro de una pregunta de catálogo lleva uno solo de tres orígenes: un valor fijo, un dato de quien lo
  recibe o la pregunta de la que depende (`match[i].questionId`). Este último compara un campo de papel filtro y tipo
  catálogo con la opción elegida en esa pregunta, que tiene que ir antes, ser de elegir una y elegir del catálogo fuente del
  campo. En el borrador, una pregunta nueva se nombra por su id local, y guardar sella su identidad. La propiedad se omite
  cuando no hay filtro, así lo publicado y sus huellas no cambian.
- **Publicar.** Frena con `Forms.Publication.MatchQuestionInvalid` si la pregunta no cumple. Avisa sin frenar con
  `Forms.Publication.DependsOnAnswer`: por WhatsApp no se manda y en la app se carga igual. Una lista dependiente no lleva los
  límites de WhatsApp (el aviso del máximo por persona ni el bloqueo de «elegir varias» con más de 20).
- **WhatsApp y el asistente.** La preparación del canal devuelve `Forms.Record.CatalogDependsOnAnswer` en esa pregunta: un
  envío no sale y el asistente no ofrece el formulario. La lista de «Enviar formulario» trae la primera pregunta dependiente,
  para mostrar el formulario apagado con el motivo.
- **La carga.** La versión dice en `catalog.dependsOn` de qué pregunta depende una lista. Las opciones reciben
  `answers[<pregunta>]=<opción>` y filtran en SQL; sin esa respuesta, 409. Al enviar, la oferta se arma con la opción elegida
  en la pregunta de la que depende: una opción que no corresponde da `Forms.Record.CatalogItemInactive` y no se guarda nada.

Front:
- En el editor, «Agregar filtro según otra respuesta», con «Campo» y «Respuesta de». Las preguntas que no sirven se ven
  apagadas con su motivo («Elige varias», «Va después de Lote»). «Como si lo recibiera» queda apagado: se prueba cargando.
- En la carga, la lista espera («Elegí primero «Campo»»), trae sólo lo que corresponde y se limpia si cambia la respuesta.
- Al publicar, el aviso va en «Avisos», con la pregunta como título. En el chat, la tarjeta de «Enviar formulario» queda
  apagada con el motivo.

### Etapa 3 · Corregir y anular (CW10-A y CW11-A, 2026-11-05)

Backend:
- **Quién (decisión 8).** Quien la cargó, con «Completar formularios», mientras nadie la decidió: recibida o por aprobar, y
  siempre si el formulario no pide aprobación. Con el permiso nuevo, «Corregir y anular respuestas» (`formrecords.amend`),
  junto con «Ver respuestas», cualquiera y en cualquier estado. Quien sólo ve respuestas, no. Un guard decide para abrir,
  guardar, anular y para el `canAmend` del detalle, y revalida al terminar, bajo el lock de definiciones. Una respuesta
  ajena sin el permiso da 403 a quien la lee y 404 a quien sólo ve lo propio. Una reemplazada, una anulada o una propia ya
  decidida dan 409 `Forms.Record.AmendUnavailable`.
- **Corregir es una carga más.** Abrir la corrección trae la versión activa con la respuesta llevada a ella: los valores, las
  opciones elegidas y los archivos. Sus listas usan los filtros por persona de quien la cargó, no de quien corrige. Guardar
  recibe el mismo envío de la carga, más el motivo (obligatorio, hasta 500 caracteres) y los archivos que siguen. Valida como
  la carga y es idempotente por pedido.
- **El reemplazo.** La corrección copia el número, el autor, la fecha de recepción y el canal de la anterior, y guarda a cuál
  reemplaza, el recibo de la primera, quién corrigió y por qué. La anterior guarda quién la reemplazó. El número es único
  entre las respuestas originales y cada enlace es único: hay una sola corrección vigente. Los archivos que siguen se enlazan
  también a la corrección.
- **Vuelve a pasar por las reglas** de «Cuando llega una respuesta», con los valores nuevos. Los avisos salen otra vez y, si
  el formulario pide aprobación, queda «Por aprobar» aunque la anterior estuviera aprobada; el PDF nuevo sale al aprobarla.
  La tarea abierta de la anterior se cierra sin decisión. Lo que ya se avisó, aprobó o imprimió queda en su historia.
- **Anular.** Recibe el motivo, con las mismas reglas, y responde 204. La respuesta queda «Anulada» con quién, cuándo y por
  qué, y su tarea abierta se cierra.
- **Lecturas y conteos.** La planilla, las facetas, la navegación y el Excel nunca traen las reemplazadas, y dejan afuera las
  anuladas salvo con el estado «Anulada»; el Excel las suma con «Incluir las anuladas». «Formularios» cuenta las respuestas
  vigentes y no anuladas, y ya no sale del último número. El detalle trae la corrección, quién la reemplazó, la anulación y
  `canAmend`, y cada dato se omite cuando no aplica, así los cuerpos de antes no cambian.
- **El chat de WhatsApp.** La respuesta que llegó por el chat dice si después se corrigió o se anuló.

Front:
- «Corregir» y «⋯ › Anular» van en la fila del título de la vista rápida y de la pantalla completa, también en la planilla de
  lo propio, y sólo aparecen con `canAmend`.
- La corrección se abre en un panel donde estaba el detalle (en la compu) o en su página (desde la pantalla completa y en el
  celular). Tiene las zonas de la carga, «Antes: …» en gris debajo de lo que cambió, los archivos que ya estaban con
  «Quitar» y «Motivo de la corrección *» al final. Cerrar con cambios pregunta antes de descartar.
- Después de corregir, la fila y el detalle dicen «Corregida», el momento lleva el motivo y «Ver la versión anterior», y una
  aprobada pasa a «Por aprobar otra vez».
- Anular pide confirmar con el motivo y el botón en rojo. «Estado» suma «Anulada» y el Excel, «Incluir las anuladas».
- La tarjeta del chat de WhatsApp dice al pie «Después se corrigió…» o «Después se anuló…».

### Etapa 5 · Títulos de sección (CW15-B, 2026-11-05)

Backend:
- **El borrador.** Los títulos van en una lista aparte de las preguntas (`draft.sections`): cada uno con cuántas preguntas
  van antes (`start`) y su texto, de hasta 80 caracteres, con un máximo de 40 títulos. Un título no es una pregunta: no se
  responde, no cuenta entre las 40 preguntas y no tiene identidad. Nunca cae entre el bloque de renglones y sus preguntas. Sin
  títulos la lista se omite, así borradores, versiones y huellas anteriores conservan sus bytes.
- **Publicar.** Frena con `Forms.Publication.SectionEmpty` un título sin preguntas debajo. La versión sellada y el pedido al
  canal llevan los títulos.
- **WhatsApp.** El Flow pone un subtítulo (`TextSubheading`) justo antes de la primera pregunta de cada sección, en la
  pantalla de esa pregunta, sin sumar pantallas. Cuenta entre los 50 componentes de la pantalla. Sin títulos, el Flow sale
  con los mismos bytes. Va en el perfil nuevo de los renglones (r10). El 2026-11-06, en la verificación de renglones con
  Meta, la primera variante (preguntas después del bloque y dos títulos de sección) quedó aceptada y publicada; esa
  verificación sigue en curso.
- **La carga.** La pregunta donde empieza cada título trae `section`.

Front:
- En el editor, «Agregar título» va antes de «Agregar pregunta». El título es un renglón de la lista, sin número, con ↑ ↓,
  lápiz y tacho, y las preguntas se numeran sin contarlo.
- La vista previa lo muestra como subtítulo arriba de la primera pregunta de su sección, como en WhatsApp.
- Al publicar, un título sin preguntas es un problema con «Ir al título».
- En la carga, cada título abre una zona plegable. Lo de antes del primero queda en «Datos», y los archivos van al final, en
  «Fotos y documentos» (AR1-A): una sección de sólo archivos no arma zona.

### Contratos, permisos y migraciones

| Etapa | Rutas | Permiso | Migración |
| --- | --- | --- | --- |
| 2 · Lo propio | Ninguna nueva: las lecturas de respuestas se abren también con «Completar formularios» | Ninguno | Ninguna |
| 4 · Listas dependientes | Ninguna nueva: `match[i].questionId` en el borrador, y `catalog.dependsOn` y `answers[...]` en la carga | Ninguno | Ninguna |
| 3 · Corregir y anular | `GET` y `POST /api/forms/entries/{definitionId}/records/{recordId}`, `GET …/records/{recordId}/versions/{versionId}/questions/{fieldId}/options` y `POST /api/forms/definitions/{definitionId}/records/{recordId}/void` | `formrecords.amend`, «Corregir y anular respuestas» | `AddFormRecordCorrections`, con un `Down` que se niega si ya hay correcciones, anuladas, tareas cerradas o archivos compartidos |
| 5 · Títulos de sección | Ninguna nueva: `draft.sections` en el borrador y `section` en la carga | Ninguno | Ninguna |

Los códigos nuevos son `Forms.Publication.MatchQuestionInvalid`, `Forms.Publication.DependsOnAnswer` (aviso),
`Forms.Record.CatalogDependsOnAnswer`, `Forms.Record.AmendUnavailable`, `Forms.Record.ReasonRequired`,
`Forms.Record.ReasonInvalid` y `Forms.Publication.SectionEmpty`. Los ejemplos HTTP están en las cohortes exportadas al front:
la de la carga (`forms-entries-http`, 29 cuerpos) y la de formularios (`forms-http`, con `sections/*`).

## 4. Lo que se encontró al programarlas (no repetir)

1. **Un dato nuevo se omite cuando no aplica.** La pregunta de la que depende una lista, los títulos, la corrección, la
   anulación y `canAmend` no viajan en null. Así las versiones selladas, sus huellas y los cuerpos que ya lee el front no
   cambian. En el detalle y la fila hubo que arreglarlo después de la primera entrega.
2. **Lo propio lo acota el servidor.** El dueño va en el pedido interno, nunca del cliente, y una dirección directa a algo
   ajeno da 404, como algo inexistente. Los conteos también se acotan: el de personas sólo la nombra a ella.
3. **Una corrección usa los filtros por persona de quien la cargó.** Si usara los de quien corrige, una persona con otro dato
   vería otras opciones y podría dejar una que el autor no tenía.
4. **El conteo de respuestas no sale del último número.** Con anuladas, hay que contar las vigentes.
5. **Una migración cuya vuelta atrás pierde datos se niega a volver** si ya hay correcciones o anuladas.
6. **Cerrar una corrección con cambios pregunta** («¿Descartar la corrección?», con «Seguir corrigiendo»); sin cambios, cierra.
7. **En el front, un texto de otro espacio de traducciones se declara en `useTranslation`.** Anular desde la pantalla completa
   mostraba la clave del motivo. Los tests unitarios no lo veían: lo encontró el recorrido en el navegador.
8. **Una lista dependiente no entra en un Flow estático.** Es un problema de la preparación del canal, como un catálogo
   demasiado grande: publicar avisa sin frenar y el formulario se carga en la app.
9. **Un título no es una pregunta.** No lleva identidad ni cuenta entre las preguntas, y moverlo nunca lo deja adentro del
   bloque de renglones.
10. **Los tests con datos compartidos fallan de a ratos.** La purga de borradores vencidos, de a 100, dejaba afuera la fila de
    un test en la suite completa, y una siembra que guardaba una foto sin fuente se colaba como destino de sugerencias.

## 5. Adaptación al multitenant

Leído en el multitenant en `c339b58d` (front `c5172e65`), con la hoja de ruta hasta la decisión 108. Las cuatro etapas ya
están escritas allá, así que la tabla dice en qué difieren y qué conviene sumar.

| Tema | Su hoja de ruta y sus etapas hoy | En ArquitecturaBase | Qué evaluar |
| --- | --- | --- | --- |
| **Lo propio** (83, 85 y 101; E20 «Lo propio»; E21 «Lo propio») | Rutas propias, `/{id}/own-records/…`, con `formrecords.create`, filtradas por quien respondió; lo ajeno da 404. La E21 suma el estado, el PDF y el motivo. Con sólo completar, «Formularios» muestra sus formularios con «Cargar», **sin conteos** (83). «Dados de baja» en «Estado» (101). | Las mismas rutas de la planilla, el detalle, los archivos y el workflow, abiertas también con «Completar formularios», con un guard que acota. «Formularios» suma «Mis cargas» y «Mi última carga», y quien sólo carga no tiene el segmento de los dados de baja. | Rutas propias o las mismas con un guard es una elección técnica suya: el efecto es el mismo. Su 101 (lo suyo de los dados de baja) va más allá de lo de acá y conviene conservarla. «Mis cargas» y «Mi última carga» en «Formularios» chocan con su 83: **lo decide el usuario.** |
| **Listas dependientes** (94; E20 «Las listas dependientes (94)»; E16 y E12 «Fuera») | Las mismas condiciones; se validan al guardar (`Forms.Draft.DependentListInvalid` y `DependentListForCustomers`); sólo en una pregunta suelta, no en una columna de un grupo; se ocultan con la pregunta de origen (E19); el formulario sale «solo para la app» (`AppOnly`). WhatsApp, después del bloque. | El mismo filtro, que frena al publicar y avisa que por WhatsApp no se manda. | Coincide. Para sumar: que el asistente no ofrezca el formulario y que «Enviar formulario» lo muestre apagado con su motivo; que la lista no lleve los límites de WhatsApp (máximo por persona y 20 en «elegir varias»); y que al enviar se revise con la opción elegida en la pregunta de la que depende. |
| **Corregir y anular** (91 a 93; E21 «Corregir y anular (91 a 93)») | Las mismas reglas, con el permiso `formrecords.correct` y rutas propias para quien cargó (`own-records/{recordId}/correction`, `corrections` y `void`). Suma que quien corrigió no puede aprobar y que la copia de «Mis mensajes» de un cliente no cambia. No dice qué pasa con los avisos ya emitidos ni con la tarea abierta de la anterior. | §3, Etapa 3. Acá se puede aprobar lo que corrigió otra persona: la regla compara con el autor de la respuesta, que la corrección copia de la anterior. | Coincide en lo de producto; su regla de que quien corrigió no aprueba es más estricta y conviene conservarla. Para sumar a su E21: los avisos salen otra vez y lo emitido queda en la historia; la tarea abierta se cierra sin decisión; las listas de la corrección son las de quien la cargó; el conteo de «Formularios» no sale del último número; y el `Down` que se niega a perder datos. |
| **Títulos de sección** (88; T24 de la E14; E20 «Zonas»; c339b58d) | Un elemento del borrador de hasta 80 caracteres, que no cuenta entre las 40 preguntas; `TextSubheading` dentro de los 50 componentes; publicar frena un título sin preguntas (`Forms.Publication.Incomplete`, con «Ir al título»); una zona por título en la web; con la E19, un título con todas sus preguntas ocultas no se ve. | §3, Etapa 5. | Coincide. Para sumar: el tope de 40 títulos; que un título no cae entre las columnas de un grupo de líneas (su E19); que sin títulos el Flow sale con los mismos bytes; y que una sección de sólo archivos no arma zona si los archivos van al final (AR1-A, traspaso 5). La traducción del título no aplica (su 35). Meta aceptó el subtítulo el 2026-11-06, en la primera variante de la verificación de renglones, y su E14 todavía lo da por «sin probar» en ArquitecturaBase. |
| **Permisos** (hoja de ruta §2) | 13 permisos de formularios, con `formrecords.correct` en la E21. | 13, con `formrecords.amend`. Corregir lo ajeno pide además «Ver respuestas». | Sólo cambia el nombre. Decidir si su `formrecords.correct` también exige leer respuestas para corregir lo ajeno. |
| **Alcance por organización y empresa** | RLS, `ITenantOwned`, `[FeatureGate("forms")]` y el alcance de empresa de sus roles. | Una instalación. | Lo propio es de la persona dentro de su organización (y de su empresa, si el formulario es de una). Las columnas de la corrección y la anulación van en las tablas del esquema `tenant`, con su test de aislamiento por ruta. El número único entre las originales es por organización, como su numeración. Con el módulo apagado, las rutas nuevas responden 404. |

**Lo que tiene que decidir el usuario allá:** si quien sólo carga ve «Mis cargas» y «Mi última carga» en «Formularios», contra
su decisión 83.

## 6. Lo que falta acá

- **La prueba manual** de las cuatro etapas: con una cuenta que sólo tenga «Completar formularios», con una lista
  dependiente en la carga, corrigiendo y anulando de punta a punta, y con un formulario con títulos.
- **Cerrar la verificación con Meta.** El 2026-11-06 Meta aceptó un Flow con dos títulos de sección, en la primera variante
  de la verificación de renglones (r10); esa verificación sigue en curso.
- **De la Etapa 1:** la cámara de un celular real, por USB y con la red limitada
  ([probar en el celular](../guides/probar-en-el-celular.md)). El túnel no sirve para esto: solo publica la Api para Meta.
- **Después del plan:** las listas dependientes por WhatsApp, con el intercambio cifrado con Meta, y cargar en nombre de otra
  persona, si aparece el caso. Ninguna tiene chat ni fecha.

## 7. Dónde está cada cosa en ArquitecturaBase

- **Regla vigente:** [formularios](../features/formularios.md), en «Lo propio de quien sólo carga — Etapa 2», «Listas
  dependientes — Etapa 4», «Corregir y anular — Etapa 3» y «Títulos de sección — Etapa 5». La regla de inmutabilidad quedó
  reescrita en la Etapa 3.
- **Plan:** [cargar formularios desde la app](2026-10-29-cargar-formularios-desde-la-app.md), con el resumen y la
  verificación de cada etapa.
- **Diseño del front:** `ArquitecturaBaseFront/docs/design/carga-desde-la-app.md`, con una sección por etapa y sus
  diferencias con el dibujo.
- **Tableros:** el lienzo [Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M): CW6-C, CW7, CW10-A,
  CW11-A y CW15-B, las elegidas, y CW9 (quién ve qué).
- **Commits:**
  - Etapa 2: backend `692890b4` y `e13f0724`; front `b65ec26a`, `eda2aec7` y `1e49723a`;
  - Etapa 4: backend `0dadfaa2`, `3e83abf2`, `0897784c` y `9a17bac2`; front `395f2b37`, `b6b7ddb9`, `fe80376e`, `6bd7eed2`,
    `f311b8c2` y `abc999f6`;
  - Etapa 3: backend `298e8b35`, `442283fa`, `5e834fcf`, `4885d311`, `2a92c0f5`, `446e6b7f`, `75bfc6e4` y `a2c5616f`; front
    `6b9671eb`, `353674c6`, `8019295c`, `f0729f77`, `c268b80b` y `07538978`;
  - Etapa 5: backend `033dd883`, `f8c794b9`, `c0e9555b`, `0f35321e`, `750d9e29` y `e9756adb`; front `b363ed19`, `faf68e5f`,
    `1f33cb80` y `62112ae4`;
  - contratos exportados al front: `0f0f037a`, `b4562403`, `15ce4405` y `96062932`.
- **Código de referencia:**
  - backend, en `Modules/BusinessForms` de cada capa: `FormRecordAccessGuard` y `FormWorkflowReadPolicy` (lo propio);
    `FormCatalogMatchPolicy`, `FormEntryCatalogPolicy` y `FormPublicationGuard` (listas dependientes); `BusinessFormRecord`,
    `BusinessFormTask`, `FormRecordAmendmentGuard`, `FormEntryCorrectionsController` y `FormRecordVoidsController` (corregir
    y anular); `FormDraftPolicy`, `FormPublicationPolicy`, `WhatsAppFlowScreenBuilder` y `FormEntryVersionPolicy` (títulos);
  - front, en `src/features/business-forms/`: `FormOverviewPage`, `FormResponsesPage` y `ResponseDetailContent` (lo propio);
    `CatalogQuestionFilters`, `dependentLists.ts` y `EntryCatalogField` (listas dependientes); `FormEntryCorrectionPanel`,
    `EntryCorrectionFields`, `ResponseAmendActions` y `ResponseExportDialog` (corregir y anular); `SectionTitleEditor`,
    `sectionTitles.ts`, `FormPreview` y `FormEntryForm` (títulos); y `whatsapp/components/FormReplyCard` (la marca del chat).

## 8. Prompt para el chat guía del multitenant

```text
Leé el traspaso 7 de ArquitecturaBase, en
C:\Users\ezequ\source\repos\ArquitecturaBase\docs\plans\2026-11-06-traspaso-multitenant-carga-etapas-2-a-5.md.
Cuenta cómo quedaron programadas allá, el 4/11 y el 5/11, las etapas 2 a 5 de la carga desde la app, cuyas decisiones
ya tomaste con el traspaso 3: lo propio de quien sólo carga (tus 83 y 85), las listas dependientes (94), corregir y
anular (91 a 93) y los títulos de sección (88). Parte de los commits no está en GitHub: leelos en
C:\Users\ezequ\source\repos\ArquitecturaBase y ...\ArquitecturaBaseFront.

1. Si todavía no incorporaste los traspasos 4 y 5 (en la misma carpeta), hacelo antes: la planilla, la carga y los
   íconos de este traspaso son los que contó el 5. No son código para copiar (tu decisión 18) ni hacen falta para
   dibujar (tu decisión 108).
2. Compará cada etapa con tu E20, tu E21, tu E14 y tu E15 y sus textos de diseño, con la tabla del §5. Sumá lo que dice
   «Para sumar» donde corresponda, sin cambiar lo que ya decidiste.
3. El único punto que choca con una decisión tuya es «Mis cargas» y «Mi última carga» en «Formularios», contra tu 83.
   No lo cambies por tu cuenta: preguntale al usuario, con las opciones y la recomendada marcada. Lo mismo vale para
   cualquier otra adaptación que tenga que decidir él.
4. Sumá la lista de «no repetir» del §4 a donde vayan las lecciones de esas etapas.
5. Decile al usuario qué tableros cambian, si alguno. Como ejemplo para adaptar, tiene CW6-C, CW7, CW10-A, CW11-A y
   CW15-B en el lienzo de ArquitecturaBase (https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M).
6. No programes nada. Commits chicos en español, en main, sin push.

Al terminar, contame qué cambió en tus specs, qué decisiones quedaron pendientes y qué tableros hay que ajustar.
```

Este traspaso se retira cuando el multitenant lo haya incorporado a sus etapas.
