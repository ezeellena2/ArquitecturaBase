# Anexo B del traspaso 2: lo que cambió en el front desde el 2026-10-17

**Repositorio relevado:** `C:\Users\ezequ\source\repos\ArquitecturaBaseFront`, rama `main`.

**Rango:** de `9261e3d` (2026-10-17 12:32, «fix(formularios): mostrar y revisar cada destinatario del aviso») a
`982ade8` (2026-10-23 20:27, «fix(catalogos): un valor escrito sin textos quita la etiqueta de la opción anterior»).
Son **311 commits** y **1.159 archivos**: 26.772 líneas agregadas y 4.018 quitadas. De esos archivos, 509 son cuerpos
HTTP exportados y sus manifiestos, en `src/test/fixtures`, y 55 son escenas, dobles y referencias visuales, en
`src/test/visual`.

**Cómo se relevó:**
- Solo el estado commiteado, con `git show 982ade8:<ruta>`, `git log` y `git diff 9261e3d..982ade8`.
- No se miró el árbol de trabajo, porque otro chat lo está editando mientras se escribe esto.
- No se corrieron builds, tests ni la app. Los conteos de pruebas que aparecen son los que registran los docs del front.
- Todo lo que sigue sale del código. Lo que no se pudo verificar dice «(no verificado)», y cada sección cierra con sus
  huecos.

**Para quién es:** el equipo de ArquitecturaBaseMutitenant, que va a reconstruir el producto adaptado a multitenant
B2B + B2C. Se describe qué hay en cada zona, en qué orden, qué estados existen y qué textos se ven. No se describen
colores ni componentes concretos. El vocabulario de disposición (`Page`, `Surface`, `FilterBar`, `DataTable`, diálogo,
panel lateral, `ConfirmDialog`, toast) es el del apartado 2 del
[anexo B del traspaso 1](../../../../ArquitecturaBaseMutitenant/docs/plans/2026-10-17-formularios-traspaso/anexo-b-front.md).

**Qué ya se contó:** el anexo B del traspaso 1 relevó el front en `f8212b2` más el trabajo sin commit de ese momento:
la revisión de envío por destinatario de G5, que entró en `9261e3d`. Este anexo cuenta solo el delta. Igual, cada
pantalla tocada se describe entera, para que se entienda sin abrir el anterior. Lo que quedó obsoleto de aquel anexo
se dice en cada sección. Lo más grande es su apartado 1, la navegación, que ahora describe la sección 5 de este anexo.

## 0. Estado por etapa en `982ade8`

| Etapa | Tableros | Estado en el front | Sección |
| --- | --- | --- | --- |
| Arreglos del review de la Etapa 4 (2026-10-17) | G1–G5 | Implementado: avisos operativos unificados, foco en errores de reglas, PDF en visor nativo, historial | 5.2 |
| 5. Asistente, sugerencias y audios | H1–H7, s1–s4, 03 | Implementado. El modo «Bot» y `whatsapp.manage` se retiraron: quedan Atención manual y Asistente | 3 |
| Idioma original y traducción opcional | I1–I3 | Implementado. **Lo reemplaza la regla de idioma del 2026-10-23, todavía sin aplicar en el front** | 4 |
| Menú y detalle | MF1, D2, DC0, DC1 ajustado | Implementado. D2 es la vista rápida de escritorio y DC1 la pantalla completa. No hay bandeja global de aprobación | 5 |
| Ciclo de vida | BA1-A, BA2–BA6 | Implementado | 6 |
| Chat de Inicio | (asistente de Inicio) | **Sin cambios en el período.** Se describe completo porque el anexo anterior no lo cubrió. **No muestra respuestas** (review E2, punto 34) | 7 |
| Catálogos E1 | CA1-A, CA2–CA6 | Implementado | 8 |
| Catálogos E2 | CE1, CE2, CE3-B, CE4-A, CE5 (CE6 es la guía) | Implementado, **con los errores del review E2 en corrección** | 9 y 10 |
| Documentos por WhatsApp | DOC1–DOC7 | Solo tableros elegidos; nada programado | 11.6 |
| Casos del asistente | (tableros de `asistente-casos.md`) | Solo tableros elegidos; nada programado | 11.6 |

**Alrededor del corte.** El otro chat siguió commiteando mientras se relevaba. Esto **no** entra en este anexo:
- **Front, después de `982ade8`:**
  - `ed1a62d`: test del aviso de títulos largos con el chequeo de la importación aplicada.
  - `28f5cc3`: sin prueba por persona con un catálogo de Filtros.
  - `d3bac82`: test de esa prueba.
- **Back, commits de la misma tarde (2026-10-23, desde las 20:12) que cambian contratos o puntos del review:**
  - `f473c14f`: convertir catálogo o booleano a texto con el texto visible (punto 6).
  - `95e200c8`: pedir confirmación y limpiar los valores al cambiar el tipo o la fuente de un dato. El front de
    `982ade8` no manda esa confirmación; ver 10.
  - `6023f7d7`, `d7f1cad7` y `001ef2c9`: un valor que no es opción de la fuente se trata como dato faltante.
  - `84289eef`: `questionId` en cada uso de un campo. El guard estricto del front de `982ade8` todavía no lo acepta;
    ver 9.
  - `8e6585e7`: nombres `List…` (punto 24).
  - `2baa3c3c`: corrige el texto del punto 35 del review. Ya no hay pestaña de idioma a la que cambiar: un error del
    original con el menú en el otro idioma dice qué falta y en qué idioma.

**Rutas de código:** van completas desde `src/`, entre comillas invertidas. Los enlaces a docs del front usan
`../../../../ArquitecturaBaseFront/docs/...`; los del back, `../../features/...`, `../../reviews/...` y
`../../decisions/...`.

## 1. Avisos para el multitenant: qué no copiar

Hay tres familias de avisos. Cada sección los marca **en la pantalla afectada**, con una cita en bloque que empieza
con «**No copiar —**». Esta sección los junta y dice dónde están.

### 1.1 Reglas de interfaz del 2026-10-23

El usuario las fijó en
[`visual-baseline.md`](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), commit `bb98bf2`, en los
apartados «Pantalla» y «Formularios y diálogos». La transcripción completa está en 11.6. En resumen:
- **Un botón está habilitado o deshabilitado, sin explicaciones al lado:**
  - nada de textos debajo o al costado, como «No hay cambios para guardar», «Revisá los datos marcados antes de
    guardar» o «Guardá … antes»;
  - los botones no cambian de lugar ni de forma con cada acción;
  - si el motivo de un deshabilitado no es obvio, va como tooltip con el mouse o el foco, y para el lector de pantalla;
  - los errores se marcan en sus campos.
- **El `status` de la cabecera muestra solo el estado del objeto,** como «Activo» o «Dado de baja». Nunca «Cambios
  sin guardar» ni números de revisión: que Guardar esté habilitado ya dice que hay cambios. Se conserva el aviso al
  salir con cambios sin guardar.
- **Las acciones de una superficie van al borde derecho de su encabezado,** a la altura del título.
- **Se valida al intentar guardar.** Después del primer error, en vivo mientras se corrige.
- Al guardar, el botón se deshabilita mientras trabaja, sin tapar la pantalla ni perder lo escrito.

**Las pantallas de `982ade8` todavía las violan.** El punto 38 del
[review E2](../../reviews/2026-10-23-code-review-catalogos-e2.md) da la lista inicial, y el relevamiento encontró más
casos:

| Pantalla | Qué viola | Sección |
| --- | --- | --- |
| Editor del formulario (`FormEditorPage`) | La insignia muestra «Cambios sin guardar» y «Guardado sin confirmar». **No está en el punto 38**. También la línea de espera ante un 429 | 4.3, 6.4 |
| Diálogo de publicación (P1/I2/I3) y reactivación | Texto al costado de los botones del pie y fecha de guardado al pie. La espera de un 429 aparece dos veces | 4.4, 6.6 |
| Superficie de publicación (P2) | «Consultar publicación» y «Repetir el mismo intento» van en el cuerpo, no en el encabezado | 4.5 |
| «Cuando llega una respuesta» (`FormRulesSection`) | «Cambios sin guardar», «Revisión N» junto a Guardar y textos debajo de «Guardar reglas». Según el punto 38, también «Completá este dato» al agregar una acción | 5.2 |
| DC1 (pantalla completa) | La banda plegada de Actividad dice «Reglas de la revisión N». El resumen plegado está elegido en el tablero; lo que conviene revisar es el número de revisión en el encabezado. **No está en el punto 38** | 5.7 |
| «Qué sugerir después» (`FormSuggestionsSection`) | «Cambios sin guardar», «Revisión N», el motivo debajo de «Guardar sugerencias» y errores al agregar una sugerencia | 3.7, 6.8 |
| Diseño del PDF (`FormPdfDesignPage`) | «· Cambios sin guardar» y número de revisión | 4.7, 5.2 |
| Compositor del chat | Textos con el motivo del deshabilitado, arriba del cuadro y debajo de «Enviar formulario». **No está en el punto 38** | 3.3 |
| D2 (vista rápida) | Acciones en una segunda fila a la izquierda; la decisión explica los botones con texto («No podés aprobar…», espera, conflicto); la historia explica Consultar y Reintentar con texto. **No está en el punto 38** | 5.6 |
| Eliminar un formulario | Cuenta regresiva y texto de bloqueo visibles; el botón destructivo a la derecha, no separado a la izquierda | 6.7 |
| Menú «⋯» del editor y BA6 | Motivo visible dentro del menú y texto rojo en la fila. La regla funcional del 2026-10-21 los pide así: **decidir con el usuario** | 6.4, 6.8 |
| Editor de catálogo Manual (`CatalogEditorPage`) | «Cambios sin guardar», «Revisá las opciones marcadas antes de guardar», superficies «Se ordenará A–Z al guardar» y «N movimientos pendientes…», y el botón de la banda «Campos», que cambia de «Editar campos» a «Agregar campo» | 8.3, 9.3, 9.4 |
| Acciones de «Opciones» (`CatalogOptionsActions`) | «Guardá el catálogo antes» y «Primero guardá los cambios» debajo de los botones, que quedan desalineados (punto 37). Una prueba fija hoy ese texto | 8.3, 9.6 |
| Opción nueva del catálogo | Texto y Código en rojo al agregarla (punto 36). En catálogos tipados, también «Valor» | 8.3, 9.6 |
| «Elegir varias», mínimo y máximo | Marcan error apenas se vacían, antes de intentar guardar | 8.3 |
| Editor de catálogo Filters y propuesta H4 (`CatalogFiltersEditorPage`) | «· Cambios sin guardar» en el estado | 3.8 |
| Pregunta de catálogo (`CatalogQuestionSettings`) | «Guardá el formulario para probar con una persona» | 10.3 |
| Componentes compartidos | `Surface.actions` admite párrafos con motivos y probablemente deja las acciones a la izquierda cuando bajan de renglón; `forms.json` conserva las claves que la regla retira | 11.1, 11.5 |
| Fuera de Formularios | `AiSettingsPage` («Cambios sin guardar» y «Revisión N:» en el resumen), `AppearancePage`, `SettingsPage`, `RoleEditorPage` y `BusinessInformationPage` («Cambios sin guardar» en el `status`), verificados en `982ade8` | No se describen en este anexo |

### 1.2 Regla de idioma del 2026-10-23 (review E2, punto 35)

Es la decisión del usuario del 2026-10-23, que todavía **no se aplicó en el front**. Está escrita en el
[review](../../reviews/2026-10-23-code-review-catalogos-e2.md). En el back,
[`formularios.md`](../../features/formularios.md#idioma-original-y-traducción-opcional--2026-10-19) sigue con la regla del
2026-10-19: el alta común de catálogos «sigue siendo bilingüe», aunque cada catálogo y cada dato de las personas ya guarda
su idioma original (back `58fb4e8e`, sin cambiar todavía contratos ni validadores; ver 4.10).
- Lo que se crea en la web se escribe en el idioma elegido en el menú. Ese es su idioma original y el único
  obligatorio.
- El otro idioma es una traducción opcional, sin autotraducción ni relleno.
- Se muestra en el idioma de la pantalla y, si falta, en el original.
- **No hay selector de idioma en las pantallas.** Para traducir, se cambia el idioma del menú y se edita lo mismo:
  - se saca «Textos en» de `FormEditorPage`, `CatalogMetadataFields`, `OfficialFormPreview` y `FormPdfPreview`;
  - los pares de campos por idioma pasan a un solo campo en el idioma del menú. Pasa con «Nombre» y «Nombre en inglés»
    en los campos del catálogo y en los datos de las personas, y con los pares del diseño del PDF;
  - con el menú en el idioma que no es el original, un campo sin traducción muestra el original como referencia, sin
    copiarlo.
- La comprobación de publicación sigue avisando lo que falte del original.
- Se retira la excepción bilingüe de los catálogos: hoy el alta exige nombre y descripción en es y en.
- También se revisa todo lo que exige «es», «es y en» o cae a «es» por defecto, incluido lo que viaja a WhatsApp.
- Un error de guardado en un campo que no está a la vista se muestra en la página. Si el error es del original y el
  menú está en el otro idioma, dice qué falta y en qué idioma.

**Dónde se marca:**
- 4, en cada pantalla, con la tabla 4.10 de qué queda del I1–I3 implementado;
- 3.8: «Textos en» en la propuesta H4;
- 5.2: pares y segmentado «Español/English» escrito en el código, en el diseño del PDF;
- 6.4 y 6.8: nombres de destinos que dependen del selector;
- 8.3: alta bilingüe, `text.es` obligatorio y caídas fijas a español;
- 9.3 y 9.4: «Textos en» y «Nombre en inglés»;
- 10.3: datos de las personas y etiquetas que caen a `es`;
- 11.1: `wrapLabels` del segmentado, que se queda sin consumidor.

**Un hallazgo que pesa con esta regla** (4.10):
- Los editores del formulario y del catálogo se remontan al cambiar el idioma del menú: su `key` incluye la cultura.
- Ese cambio (`changeLanguage` desde el menú de usuario) no pasa por la guarda de cambios sin guardar.
- Hoy se pierde el borrador sin preguntar, justo con el gesto que la regla nueva usa para traducir.
- Sale de leer el código; no se reprodujo.

### 1.3 Errores del front del review E2

El [review de Catálogos E2](../../reviews/2026-10-23-code-review-catalogos-e2.md) relevó el front `9f0d270..e02d1b4`.
Sus errores se corrigen en un chat aparte, back y front juntos, y varios ya se corrigieron entre `c2572b8` y `982ade8`.
Estado en `982ade8`:

| Punto | Tema | Estado | Sección |
| --- | --- | --- | --- |
| 2 | Un 409 de negocio obligaba a descartar el borrador del catálogo | Corregido: `c2572b8`, `4a24702`, `dfb1d26`, `e3e81a7` | 9.4 |
| 3 | Nombre en inglés vacío dejaba el catálogo sin abrir (parte del front) | Corregido: `b12a297` | 9.4 |
| 4 | Cambiar el tipo o pasar a obligatorio se validaba en el front y tapaba al servidor | Corregido: `4475892`, `2efb86e`, `bc71fd4`, `47e8d2a`, `ff28cbc`, `8e37dd3`, `696557e`, `783473c`, `28ab6d6`. La captura exportada del error no trae todavía las filas que el guard exige (9.11, 11.3) | 9.5 |
| 7 (front) | Cambiar el tipo o la fuente de un dato de las personas | **Pendiente:** el back ya pide `confirmValueRemoval` (`95e200c8`) y el front no lo manda ni ofrece confirmarlo | 10.3 |
| 12 | Importar después de guardar campos usaba la definición vieja | Corregido: `9efd8e2` | 9.8 |
| 13 | Vaciar una celda de catálogo seguía mostrando la opción | Corregido: `584aec8` y `982ade8` | 9.6 |
| 14 | El valor fijo de un filtro sobre un campo de catálogo no se veía | Corregido: `d72caf4`, `7080141` | 10.3 |
| 15 | Filtros y «Propio de esta pregunta» en catálogos de Filtros | Corregido: `e133d93` | 10.3 |
| 16 | El aviso de títulos largos en la pregunta mira 20 opciones | Pendiente | 10.3 (y 8.3) |
| 17 | El desplegable de una celda de catálogo se recorta en la tabla ancha | Pendiente (plausible, sale del CSS) | 8.3, 9.6, 11.1 |
| 25 | «En uso» no dice si es el borrador o la versión activa | Parcial | 9.4 |
| 26 | Datos de las personas: carga y error fuera de pantalla, tabla que no se actualiza, 409 sin cómo releer | Pendiente | 10.3 |
| 27 | Guardar no hacía nada con una opción inactiva sin obligatorio | Corregido: `70067e0`, `420c311`, `2efb86e` | 9.6 |
| 28 | «Como si lo recibiera» sin reintentar ni espera ante un 429 | Pendiente | 10.3 |
| 29 | Guards de avisos de publicar más laxos y «Como mucho 1 opciones» | Pendiente | 4.4, 8.3, 10.3 |
| 30 | Flechas que no mueven nada con campos quitados ocultos | Pendiente | 9.4 |
| 31 | El formato de cada tipo no sale de una sola pieza | Pendiente | 8.3, 9.6, 10.3, 11.2 |
| 32 | Lo que falta de los tableros CE1, CE2, CE3-B, CE4-A y CE5 | Pendiente, con partes hechas | 5.5, 9.4, 9.6, 10.3 |
| 33 | Pruebas que faltan (`FieldInUse` y usos Draft/Active en las cohortes) | Pendiente | 9.11, 10.6 |
| 34 | El chat de Inicio no muestra la respuesta (estado en minúsculas en el back, con mayúscula en el front) | Pendiente | 7 |
| 35 | Regla de idioma | Pendiente | 1.2 |
| 36 | Opción nueva marcada en rojo antes de escribir | Pendiente | 8.3, 9.6 |
| 37 | Texto debajo de los botones de «Opciones» | Pendiente | 8.3, 9.6 |
| 38 | Botones y estados | Pendiente | 1.1 |
| 39 | La sección «El catálogo» con solo Nombre y Descripción opcional; origen y tipo como dato de solo lectura | Pendiente | 3.8, 8.3, 9.3 |

**Corregido no quiere decir copiable tal cual:** cada sección describe en una línea el error original de los puntos
corregidos, para que no se repita.

### 1.4 Otros hallazgos del relevamiento que el review no tiene

- **Chat de WhatsApp** (3.3): el compositor explica con texto por qué un botón está deshabilitado.
- **Editores del formulario y del catálogo** (4.10): cambiar el idioma del menú pierde el borrador sin preguntar.
- **D2** (5.6): las acciones van en segunda fila a la izquierda, y la decisión y la historia explican los botones con
  texto.
- **Ciclo de vida** (6.4 a 6.8):
  - la insignia «Cambios sin guardar» del editor;
  - la espera de un 429 duplicada en el diálogo de reactivación;
  - el destructivo de Eliminar a la derecha;
  - el motivo visible en el menú «⋯», que la regla del 2026-10-21 pide y la del 2026-10-23 manda a tooltip.
- **Catálogos E1** (8.3):
  - en español, el editor toma «1.500» como 1500, y la importación del servidor lo rechaza como ambiguo;
  - mínimo y máximo de «elegir varias» validan en vivo;
  - `CatalogOptionsActions.test.tsx` fija el texto visible del punto 37.
- **Catálogos E2, editor** (9):
  - la revisión ante un 409 compara solo nombre y descripción;
  - con cambios sin guardar, el aviso de títulos largos cuenta solo la página visible;
  - el pie del buscador de opciones dice «Buscar catálogo»;
  - no hay recorrido visual de «Cambiar el tipo» ni del filtro de filas con error;
  - la captura `catalogs-http/manual-current/fields-conversion-error.json` no trae `row`, `value` ni `reason`, que el
    guard de `ff28cbc` exige: el consumidor de contrato no puede pasar hasta un export nuevo.
- **Catálogos E2, pregunta y personas** (10):
  - en la revisión de conflictos, un valor fijo de filtro vacío se muestra como «Sin traducir»;
  - el front no manda `confirmValueRemoval`.
- **Pruebas** (11):
  - `src/test/AGENTS.md` tiene conteos de cohortes viejos;
  - `forms-person-data-http` y `catalogs-http/imports` no tienen la marca `-text` en `.gitattributes`;
  - ninguna referencia visual «golden» es de Formularios ni de Catálogos.
- **Chat de Inicio** (7): solo tiene tipos escritos a mano, sin zod, y no tiene cohorte HTTP exportada.

## 2. Mapa vigente de rutas y menú

La sección 5.3 da el detalle: menú, migas, rutas y URL. Esta tabla es el índice de todas las rutas del producto en
`982ade8`, sacadas de `src/app/routes.tsx` y `src/layouts/navigation.ts`.

| Ruta | Página | Permiso de la ruta | Secciones |
| --- | --- | --- | --- |
| `/` | `src/app/AuthenticatedHome.tsx`: Inicio con el asistente, o `DashboardPage` sin `ai.assistant.use` | Sesión | 7 |
| `/formularios/respuestas` | `FormOverviewPage` (MF1) | `formrecords.read` **o** `formapprovals.decide` | 5.4 |
| `/formularios/{formId}/respuestas` y `…/{recordId}` | `FormResponseWorkspacePage`: planilla con D2 encima, o pendientes para quien solo decide | `formrecords.read` o `formapprovals.decide` | 5.5, 5.6, 10.3 |
| `/formularios/{formId}/respuestas/{recordId}/completa` | `FormResponseFullPage` (DC0/DC1) | `formrecords.read` | 5.7 |
| `/formularios` | `FormsPage` (A, BA1-A) | `formtemplates.read` | 6.3 |
| `/formularios/nuevo` | `FormEditorPage` | `formtemplates.read` + `formtemplates.manage` | 3.6, 4.3, 6.4 |
| `/formularios/{formId}` | `FormEditorPage`, con reglas, sugerencias, ciclo de vida y publicación | `formtemplates.read` | 3.6, 3.7, 4.3–4.6, 6.4–6.8, 8.3, 10.3 |
| `/formularios/{formId}/pdf` | `FormPdfDesignPage` | `formrules.manage` | 4.7, 5.2 |
| `/formularios/faltantes` | `MissingFormsPage` (H7, **nueva**) | `formtemplates.manage` | 3.9 |
| `/catalogos` | `CatalogsPage` | `formcatalogs.read` | 8.3 |
| `/catalogos/nuevo` (y `?kind=Filters`) | `CatalogEditorPage` / `CatalogFiltersEditorPage` | `formcatalogs.read` + `formcatalogs.manage` | 8.3, 9 |
| `/catalogos/{catalogId}` | `CatalogEditorPage` / `CatalogFiltersEditorPage`, con la propuesta H4 | `formcatalogs.read` | 3.8, 8.3, 9 |
| `/datos-de-personas` | `PersonDataPage` (CE4-A, **nueva**) | `formpersondata.manage`; la tabla, además `users.read` | 10.3 |
| `/whatsapp/chat` | `WhatsAppChatPage` | `whatsapp.read` | 3.3–3.5, 4.8, 8.3, 10.3 |
| `/usuarios` (diálogo «Editar usuario») | `UsersPage`. El diálogo suma la sección opcional «Datos», que aporta Formularios | `users.read`; la sección, `users.manage` y datos activos | 10.3 |

**Retiradas:**
- `/formularios/por-aprobar` (`ApprovalInboxPage`) y la página «Respuestas: todos los formularios»
  (`ResponseFormsPage`).
- La sección dinámica «Respuestas» del menú (`FormResponsesShell`, `FormResponsesNavigation`, `formsNavigation`) y
  `GET /api/forms/navigation`.

La dirección vieja `/formularios/por-aprobar` no redirige: ahora cae en `/formularios/{formId}` (5.3.3).

**Menú:**
- **Panel «Gestión de formularios»** (antes «Formularios»), en este orden:
  - «Formularios», con el número de pendientes entre paréntesis si tiene `formapprovals.decide`;
  - «Diseñar formularios»;
  - «Catálogos»;
  - «Formularios que faltan».

  En el celular es un grupo desplegable dentro del cajón.
- **Administración › Gestión de usuarios:** suma «Datos de las personas».
- **WhatsApp:** sigue con «Chat».

**Permisos que se suman en el período:**
- `whatsapp.configureassistant`: pasar a Asistente.
- `formsuggestions.manage`: «Qué sugerir después».
- `formpersondata.manage`: «Datos de las personas».

`whatsapp.manage` se retiró sin alias: tomar Atención manual pide `whatsapp.send`. Las rutas y el menú aceptan además
una lista de permisos que se cumple con cualquiera (`PermissionRequirement` en `src/auth/usePermissions.ts`; ver 5.3.1
y 11.2).

---

## 3. Etapa 5: asistente, sugerencias y audios en el chat

Relevado en el front `982ade8` (2026-10-23) contra `9261e3d`. El anexo anterior decía «Etapa 5: nada implementado en el
front; el modo Bot se muestra como "Automatización sin configurar"». Todo lo que sigue es nuevo, salvo donde se aclara.

### 3.1 Resumen

**Qué cambió.** Se implementaron los tableros H1–H7 y s1–s4 elegidos el 2026-10-16/17 (registro en
[formularios-whatsapp.md](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#implementación-local-de-etapa-5--2026-10-17)).
El modo «Bot» y el permiso `whatsapp.manage` desaparecieron sin alias: el chat ahora tiene dos modos, **Atención manual**
(inicial) y **Asistente**. Reglas funcionales del back:
[indicación de ofrecimiento](../../features/formularios.md#indicación-de-ofrecimiento--etapa-5),
[propuestas de prellenado](../../features/formularios.md#propuestas-de-prellenado--etapa-5),
[qué sugerir después](../../features/formularios.md#configuración-de-qué-sugerir-después--etapa-5),
[catálogos propuestos](../../features/formularios.md#catálogos-propuestos--etapa-5-h4),
[formularios que faltan](../../features/formularios.md#formularios-que-faltan--etapa-5),
[sugerencias al operador](../../features/whatsapp.md#sugerencias-al-operador-y-explicación--etapa-5) y
[audio recibido](../../features/whatsapp.md#audio-recibido--etapa-5-h6).

| Tablero | Qué es | Dónde vive en el front | Estado en `982ade8` |
| --- | --- | --- | --- |
| H1 | Chat en modo Asistente: autor «Asistente», «Por qué lo mandó», «Tomar atención manual» | `src/features/whatsapp/components/ChatThread.tsx`, `ChatTimeline.tsx`, `src/features/whatsapp/lib/timeline.ts` | Implementado; sin escena visual propia |
| H2 | En Atención manual, sugerencia arriba del compositor y diálogo de envío con datos destildables | `ChatComposer.tsx`, `FormComposer.tsx`, `src/features/whatsapp/hooks/useAssistantSuggestion.ts` | Implementado (incluye la sugerencia configurada «Preguntar siempre», sin IA); sin escena visual propia |
| H3 | Cambiar de modo, IA no disponible, marca en la lista, sin permiso | `ChatThread.tsx`, `src/features/whatsapp/pages/WhatsAppChatPage.tsx` | Implementado; una referencia visual (`whatsapp-manual-390.png`) |
| H4 | Catálogo Filters propuesto por el asistente, revisión y aprobación humana | `src/features/business-forms/pages/CatalogFiltersEditorPage.tsx`, `useCatalogDraftEditor.ts` | Implementado; sin escena visual propia |
| H5 | Límites del asistente | Casi todo es comportamiento del back; el front muestra respuestas rápidas, motivos y bloqueos | Implementado lo que toca al front |
| H6 | Audios: escuchar y transcripción | `ChatAudio.tsx`, `src/shared/ui/PrivateAudio.tsx`, `useChatAudioTranscription.ts` | Implementado; escena `whatsapp-audio.spec.ts` |
| H7 | Formularios que faltan | `src/features/business-forms/pages/MissingFormsPage.tsx` (ruta nueva) | Implementado; escena `forms-missing.spec.ts` |
| 03 | «Cuándo lo ofrece el asistente» en «El formulario» | `src/features/business-forms/pages/FormEditorPage.tsx` | Implementado |
| s1–s3 | «Qué sugerir después» en el editor | `src/features/business-forms/components/FormSuggestionsSection.tsx`, `src/features/business-forms/suggestions.ts` | Implementado; sólo la escena BA6 lo recorre |
| s4 | Procedencia y cadena en la respuesta | `src/features/business-forms/components/ResponseAssistance.tsx` | Implementado; escena `responses-assisted.spec.ts` |

**Commits clave** (todos entre el 2026-10-17 y el 2026-10-19):

- Modo y autoría: `12631db` feat(whatsapp): controlar el asistente y mostrar su autoría; `622b5e5` fix(whatsapp): aplicar los
  controles H1 y los avisos H3; `7ecabd6` test(whatsapp): verificar la atención manual inicial.
- Sugerencia en Manual: `1702daa` feat(whatsapp): revisar sugerencias antes de enviar formularios; `ab1d3ba`
  feat(whatsapp): revisar sugerencias configuradas con IA pausada; `b41d8dc`, `dbc03d5` (consumo de cuerpos reales);
  `dbd7550` fix(whatsapp): usar el idioma completo al preparar formularios (el detalle del idioma va en la sección 4).
- Audio: `88c4115` feat(whatsapp): escuchar audios y analizar su transcripción sin perder el borrador; `2b93fbd`
  conservar los intentos de audio durante la recuperación; `ac9bf1d` informar el límite de transcripción; `a1cc4eb`
  conservar la transcripción ante fallas de lectura; `3b046bc`, `9bdd549`.
- Cuándo lo ofrece: `a673eda` conservar indicación del asistente en el borrador; `9b2d9d8` editar cuándo se ofrece.
- Sugerencias s1–s3: `91bf5c1` editar qué sugerir después y revisar sus cambios; `6b911a3`; `34cc87f`; `370fdf7`
  limitar mapeos de elección al mismo catálogo.
- H4: `b027bb0` feat(catalogos): revisar y aprobar propuestas del asistente; `ec3c8a3`, `7318153`, `f34ca11`.
- s4: `1a8b3be` mostrar procedencia y cadena de sugerencias en la respuesta; `0c83036`, `b0e9767`, `5252154`.
- H7: `c5525b8` revisar pedidos de formularios que faltan; `5672af7` marcar sólo el tablero de pedidos en la navegación;
  `d1b2aac` respetar la espera al filtrar pedidos; `8efdf75`, `53b17f0`, `228e69c`.
- Privacidad del chat (no exclusivo de la Etapa 5): `fc306b7` fix(whatsapp): mantener envío privado fuera de mutaciones;
  `722d926`, `0f8a54f` (tests de retiro tras 403 y de cancelación).
- Cierre: `5c19ad6` docs(formularios): cerrar D2 y los recorridos de la etapa 5; `d6edb56` test(contratos): actualizar
  sugerencias y pedidos faltantes reales.

### 3.2 Navegación, rutas y permisos

**Ruta nueva:** `/formularios/faltantes` → `MissingFormsPage`, protegida con `formtemplates.manage` en
`src/app/routes.tsx` (la página lo vuelve a comprobar y muestra `ForbiddenPage`). Todo lo demás de la etapa vive dentro de
pantallas existentes:

| Pieza | Ruta donde aparece | Permiso que la muestra |
| --- | --- | --- |
| Modo, sugerencias, audios | `/whatsapp/chat?conversacion={id}` | `whatsapp.read` (ver); ver tabla de permisos |
| «Cuándo lo ofrece el asistente» | `/formularios/nuevo`, `/formularios/{formId}` (superficie «El formulario») | El del editor (`formtemplates.read`; editar con `formtemplates.manage`) |
| «Qué sugerir después» | `/formularios/{formId}`, debajo de las preguntas, sólo con el formulario guardado una vez | `formsuggestions.manage` |
| Propuesta de catálogo | `/catalogos/{catalogId}` (el editor Filters) | `formcatalogs.read`; decidir con `formcatalogs.manage` |
| Procedencia y cadena (s4) | D2 (pestaña Respuesta) y DC1 (sección Respuesta) | `formrecords.read` |

**Menú.** El panel «Gestión de formularios» (`src/layouts/navigation.ts`, objeto `forms`) suma, debajo de «Catálogos»,
**«Formularios que faltan»** (`navigation.missingForms`, ícono de formulario) con `formtemplates.manage` y `exact: true`.
`5672af7` evita que «Diseñar formularios» (`/formularios`) quede marcado como activo cuando se está en
`/formularios/faltantes`. El orden completo del panel y el resto del menú los describe la sección 5.

**Migas:** `Inicio / Gestión de formularios / Formularios que faltan` (enlace exacto del menú; el grupo no es enlace). No
enlazan al listado de diseño, que pide otro permiso (`src/layouts/AGENTS.md`).

**URL:**

| Ruta | Qué va en la URL | Qué nunca va |
| --- | --- | --- |
| `/formularios/faltantes` | `page`, `pageSize` (se fija al abrir con el tamaño configurado), `search`, `sort` (`lastRequestedAtUtc` o `-lastRequestedAtUtc`), `state=Discarded` (ausente = Pendientes) | El tema elegido para descartar, la espera de un 429 |
| `/whatsapp/chat` | `conversacion={id}` (igual que antes) | Propuesta, selección de datos, `requestId` congelado, bytes de audio, intentos de transcripción |
| `/formularios/nuevo` | Nada nuevo; la semilla de H7 viaja en el `state` del historial (`formSeed`) | La semilla nunca se escribe en la URL ni en storage |
| `/formularios/{id}` | Nada nuevo | Borrador de sugerencias, revisión vista del historial |

**Permisos que cambian o se suman** (la autorización real la hace el servidor):

| Permiso | Qué habilita en pantalla | Antes |
| --- | --- | --- |
| `whatsapp.send` | Además de responder y enviar formularios: **tomar Atención manual** y revisar/enviar una sugerencia | «Tomar atención manual» pedía `whatsapp.manage` |
| `whatsapp.configureassistant` (nuevo) | «Pasar a modo Asistente» y el aviso de disponibilidad de la IA | No existía |
| `whatsapp.manage` | **Retirado**: el front ya no lo lee (`WhatsAppChatPage` pasa `canConfigureAssistant` en lugar de `canManage`) | Tomaba atención manual |
| `whatsapp.read` | También escuchar audios y pedir su transcripción (sin exigir Send) | — |
| `formrecords.read` | También «Por qué lo mandó / lo sugirió» y la procedencia s4 | — |
| `formsuggestions.manage` (nuevo) | Sección «Qué sugerir después» | No existía |
| `formtemplates.manage` | Tablero «Formularios que faltan» y descartar | — |
| `formtemplates.read` + `formtemplates.manage` | «Crear formulario» desde un tema faltante | — |
| `formcatalogs.read` + `formcatalogs.manage` | «Aprobar y activar» / «Descartar propuesta» en H4 | — |

### 3.3 El chat: modos Atención manual y Asistente (H1, H3, H5)

`src/features/whatsapp/components/ChatThread.tsx`, `src/features/whatsapp/pages/WhatsAppChatPage.tsx`,
`src/features/whatsapp/hooks/useChatThreadActions.ts`. Evidencia del tablero:
[H1](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/asi-h1-modo-asistente.png),
[H3](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/asi-h3-modos.png),
[H5](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/asi-h5-limites.png).

#### Bandeja (lista de chats)

- Cada fila suma un `Badge` contorneado **«Asistente»** cuando `chat.mode === "Assistant"`, entre el nombre (que ahora
  trunca con `flex-1`) y la cantidad sin leer. Atención manual no lleva marca.
- Nada más cambió en la bandeja (búsqueda, «Todos/Sin leer», sondeo de 5 s, borradores por conversación).

#### Cabecera de la conversación (de izquierda a derecha)

1. «Chats» (sólo debajo de `xl`, para volver a la bandeja), nombre del contacto (encabezado enfocable) y teléfono; la cuenta
   vinculada sigue como antes.
2. A la derecha: `Badge` con el modo, **«Atención manual»** o **«Asistente»**.
3. Junto al badge, **«Pasar a modo Asistente»** (botón contorneado chico) sólo si: el modo es Manual, la persona tiene
   `whatsapp.configureassistant`, `assistantAvailability.state === "available"` y la última lectura del hilo no tiene
   error. Con `notInstalled`, `notConfigured`, `paused` o `modelNotQualified` no se ofrece.
   El botón «Tomar atención manual» ya **no** va en la cabecera (lo movió `622b5e5`).

#### Aviso de disponibilidad de la IA (H3)

Debajo de la cabecera, un `Banner` `warning` **sólo para quien tiene `whatsapp.configureassistant`** y sólo si el estado
no es `available` ni `notInstalled` (con el módulo de IA ausente no se dice nada):

- `notConfigured`: «El asistente no está disponible: falta configurar la conexión de IA.»
- `paused`: «El asistente está pausado.»
- `modelNotQualified`: «El asistente no está disponible: falta verificar el modelo.»
- Si el modo es Manual, segunda línea: «La conversación sigue en atención manual.» (en Asistente no se afirma eso).

No hay toast al abrir ni al refrescar (`622b5e5` retiró el toast que había en `12631db`).

#### Zona inferior en modo Asistente (H1)

- El compositor ordinario queda **montado pero oculto** (`hidden`), para no perder un formulario abierto ni el borrador.
- En su lugar, una sección rotulada «Asistente» con un `Banner` informativo: «El asistente atiende esta conversación. Para
  escribirle vos, tomá la atención manual.» y, como acción del banner (a la derecha si cabe, debajo si no), el botón
  principal **«Tomar atención manual»** sólo con `whatsapp.send`.
- Sin Send, debajo del banner: «Tenés permiso para ver el chat. Para contestar necesitás el permiso de responder.»

#### Confirmaciones de cambio de modo (`ConfirmDialog`)

| Acción | Título | Descripción | Botón |
| --- | --- | --- | --- |
| Pasar a Asistente | «Pasar a modo Asistente» | «El asistente va a atender la conversación con {{name}}: reconoce pedidos y le manda formularios prellenados. Nunca guarda una respuesta solo. Podés tomar la atención manual cuando quieras.» | «Pasar a Asistente» |
| Tomar Manual | «Tomar atención manual» | «Vas a tomar la atención manual de la conversación con {{name}}. El asistente deja de responder. Si ya empezó un envío, ese envío termina y queda en la conversación.» | «Tomar atención manual» |

- Ambas mandan la revisión de modo vigente (`contact.modeRevision`) como `expectedRevision`.
- Si mientras está abierta se pierde el permiso que la habilita (Send para Manual; ConfigureAssistant/disponibilidad para
  Asistente), la confirmación se retira en el mismo render.
- Al confirmar y desaparecer el disparador, el foco vuelve al encabezado de la conversación.
- Éxito: invalida `["whatsapp-chat"]`. Si el 200 de pasar a Manual trae `assistantSendInProgress: true`, toast info:
  «Tomaste la atención manual. El asistente estaba mandando un formulario: ese envío termina y queda en la conversación.
  Desde ahora escribís vos.»
- Error: incierto (red/5xx) → toast «No se pudo confirmar el cambio. Actualizá el chat y revisá el modo antes de volver a
  intentarlo.»; otro rechazo (incluido un 409 de revisión) → el `detail` del servidor o «No se pudo completar la acción.».
  En los dos casos se relee el hilo. No hay reintento automático.

#### Historial: autoría, explicación y respuestas rápidas

- **Autor** (sale del `origin` del servidor, nunca del modo actual): saliente con `origin: "Assistant"` → «Asistente»;
  `FormAction` o sin operador → «Automático»; con operador → «Vos» u «Operador». Antes existía «Bot».
- **«Por qué lo mandó»** (origin Assistant) o **«Por qué lo sugirió»** (envío manual que vino de una sugerencia): recuadro
  gris dentro de la burbuja con el motivo capturado (`assistant.reason`) y una lista «etiqueta: valor» de los datos
  propuestos. Sólo con valores autorizados: sin `formrecords.read` (o con `canReadFormRecords: false`) se borra en la
  lectura (`withoutFormReplyValues` pone `assistant: null` en mensajes y salidas). Nunca en mensajes de acceso.
- **Respuestas rápidas** (aclaraciones del asistente con botones, H5): mensajes `Interactive` (no Access) o salidas
  `QuickReplies` muestran una lista rotulada «Opciones de respuesta» con hasta 3 títulos de hasta 20 caracteres, sólo
  lectura, sin IDs ni acciones.
- Las acciones del historial (revisar un incierto, «Usar este texto en el borrador») quedan sólo para `origin`
  Conversation: un envío del asistente o un aviso automático no ofrecen recuperación manual.

#### Estados del hilo

- Sin datos todavía: «Cargando conversaciones…» (clave `loading`); error sin datos: «No se pudo cargar el chat.» + «Reintentar»;
  **403 de lectura (nuevo):** «Ya no tenés permiso para ver esta conversación.», sin Reintentar.
- Un 403 del hilo, del audio o de la transcripción llama a `denyRead`: cancela sin rollback y **quita** todas las
  consultas de esa conversación, vacía mensajes y deja el estado de error; no confundir con el 403 de una sugerencia, que
  sólo retira la sugerencia.
- Error con datos: franja «No se pudo actualizar. Se conserva el historial visible.» + «Reintentar» (igual que antes).

#### 390 y 320 px

- Debajo de `xl` la bandeja y la conversación alternan; la cabecera usa `flex-wrap`, así que badge y «Pasar a modo
  Asistente» bajan a otra línea si no entran.
- La referencia visual `src/test/visual/screenshots/whatsapp-manual-390.png` (escena `whatsapp-access.spec.ts`,
  contacto migrado) fija a 390 px el modo inicial «Atención manual» con compositor habilitado, borrador que sobrevive a
  «Actualizar» y sin «Tomar atención manual» ni «Activar bot». La referencia vieja `whatsapp-access-legacy-390.png`
  sigue en el repo como evidencia histórica del estado Bot retirado.
- El modo Asistente, el banner de disponibilidad y las confirmaciones **no tienen escena a 390/320**: lo dicho sale del
  código (no verificado en navegador).

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), commit `bb98bf2`):
> el compositor del chat muestra un párrafo con el motivo por el que no se puede escribir, arriba del cuadro de texto
> («Tomá la atención manual para contestar.», «Hay un envío pendiente o sin confirmar en esta conversación.», «Pasaron
> 24 horas…»), y `FormComposer` agrega debajo de «Enviar formulario» un texto con el motivo del deshabilitado (claves
> `forms.*`, por ejemplo «La cuenta vinculada está inactiva: no puede completar formularios.»). El review E2 no los
> lista, pero son textos al lado de un botón deshabilitado: con la regla nueva el motivo va como tooltip. Es anterior a la
> Etapa 5; la etapa sumó el caso «envío pendiente» por una sugerencia en curso. Estado en `982ade8`: pendiente.

### 3.4 La sugerencia en Atención manual (H2) y la sugerencia configurada «Preguntar siempre»

`src/features/whatsapp/components/ChatComposer.tsx`, `FormComposer.tsx`,
`src/features/whatsapp/hooks/useAssistantSuggestion.ts`, `src/features/whatsapp/api/assistant.ts`,
`src/features/whatsapp/lib/assistantSendSession.ts`. Evidencia:
[H2](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/asi-h2-sugerencia.png).

#### De dónde sale la sugerencia

`ChatThread` arma el contexto:

- **Con puntero del servidor** (`thread.assistantSuggestion = {requestId, sourceMessageId, state}`): manda el puntero,
  aunque la IA no esté disponible y aunque la fuente sea un `FlowReply` sin cuerpo. Se consulta por GET.
- **Sin puntero:** la fuente es el último entrante (por `inboundSequence`, fuera de Access) de tipo Text, Audio Completed
  con transcripción, o FlowReply Completed. Un audio pendiente o rechazado no revive un texto anterior. Se pide **una vez**
  por POST y sólo si se puede analizar: IA `available`, modo Manual con Send, ventana de 24 h abierta, número, cuenta
  vinculada activa y cultura del destinatario conocida. No exige formularios elegibles (sí los exige enviar).
- **Dueño** de la sugerencia: JSON de actor, conversación, cultura de la UI, permisos ordenados, revisión de modo, fuente y
  revisión del destinatario (`formEligibility.recipientContextRevision`). Cambiar cualquiera reinicia sólo el estado
  privado de la sugerencia, con UUID nuevo; **no** remonta el editor ordinario (conserva nodo, cursor y borrador).

#### Tarjeta arriba del compositor (estado Ready)

- Recuadro con borde del color primario, ícono de formulario y:
  - con IA (`proposalId` no nulo): rótulo accesible «Sugerencia del asistente» y texto «El asistente sugiere mandarle
    {{name}}»;
  - configurada sin IA (`proposalId: null`, modo «Preguntar siempre»): rótulo «Sugerencia de formulario» y texto
    «Podés mandarle {{name}}»;
  - si hay datos, segunda línea: «Prellenado con: {{campo}}: {{valor}} · …».
- Acciones a la derecha de la tarjeta (con `flex-wrap`): **«No, gracias»** (contorneado; descarta, deshabilitado mientras
  hay un intento o una espera) y **«Revisar y enviar»** (principal; deshabilitado mientras hay otra operación, una espera o
  un envío de texto/formulario sin resolver).
- Estados que no muestran tarjeta: Pending (se consulta cada 5 s con la pestaña visible; una falla pausa ese sondeo),
  NoReply, Blocked, Discarded y Sent (este último invalida el chat para que aparezca el envío).
- La tarjeta sólo se muestra a quien puede revisar: Send, modo Manual y (IA disponible o puntero existente).

#### Diálogo «Revisar y enviar» (reutiliza el diálogo C2 de enviar formulario)

- Título «Enviar un formulario a {{name}}»; descripción «Revisá los datos propuestos antes de mandar el formulario.».
- Cuerpo, en orden:
  1. «{{nombre del formulario}} · versión {{n}}» (fija; no se elige otro formulario).
  2. «Le llega en {{español|inglés}}.» con la cultura de la propuesta.
  3. Si hay datos: grupo «DATOS QUE VAN PRELLENADOS» con una casilla por dato (rótulo = pregunta, descripción = valor
     mostrado), todas tildadas al llegar. **No se editan valores**: sólo se destildan. Destildar todo es válido.
  4. «{{name}} los ve cargados y los puede cambiar. Lo que destildes va vacío.»
  5. Con un 429 vigente: «Esperá {{seconds}} segundos antes de volver a consultar o enviar.» (`role="status"`).
- Pie: «Cancelar» (vuelve el foco al disparador exacto) y la principal: «Enviar formulario» → «Guardando…» mientras
  manda → «Repetir el mismo pedido» si hay un intento conservado; o «Consultar envío» si el resultado quedó incierto.
- Mientras manda, el diálogo no se cierra (Escape y clic afuera bloqueados).
- No muestra la preparación de catálogos ni el mensaje de apertura editable: el servidor prepara al enviar.

#### Escrituras, recuperación y errores

- Enviar congela `{requestId (UUID nuevo), includedFieldIds}` en el orden de la propuesta, filtrados contra lo ofrecido
  (`freezeAssistantSend`); nunca valores. Una sola operación a la vez con `AbortController`; un resultado de otro dueño se
  descarta.
- Incierto (red/5xx) o 409 al enviar → recuperación «consultar»: toast warning «No se pudo confirmar si el pedido quedó
  guardado. Revisá su estado antes de repetirlo.» con acción «Consultar envío». Consultar hace GET de la sugerencia; si
  vuelve Ready, se habilita repetir **el mismo** intento; si vuelve Sent, se toma el recibo compartido (también si lo
  inició otro operador, sin atribuírselo a quien consulta). No hay POST automático.
- Falla al pedir o descartar → toast de error con «Consultar envío». Un 404 al consultar sin sugerencia ni puntero →
  toast info «El pedido todavía no figura guardado. Podés repetir el mismo pedido; el sistema evita duplicarlo.» con
  «Repetir el mismo pedido».
- 429 en cualquier operación: espera de 1 s a 24 h (30 s si falta `Retry-After`); toast «Esperá N segundos…»; al vencer
  no se repite nada solo.
- 403 → retira propuesta, intento, diálogo y selección; toast «Ya no tenés permiso para esta acción.»; la lectura del chat
  sigue.
- **Sugerencia configurada con IA pausada** (`ab1d3ba`): si el servidor ya dejó el puntero de una sugerencia «Preguntar
  siempre», se consulta y se puede confirmar aunque la IA esté pausada o no instalada; sin puntero no se inicia una
  interpretación nueva con la IA caída. La pausa conserva la selección y el diálogo del puntero activo.

#### 390 y 320 px

Sin escena visual propia. Del código: la tarjeta es `flex-wrap` (los botones bajan debajo del texto) y el diálogo es el
común (ancho de pantalla menos 2 rem en celular, `sm:max-w-[600px]`), con el pie de siempre. No verificado en navegador.

### 3.5 Audios recibidos (H6)

`src/features/whatsapp/components/ChatAudio.tsx`, `src/shared/ui/PrivateAudio.tsx`,
`src/features/whatsapp/hooks/useChatAudioTranscription.ts`, `src/features/whatsapp/api/audio.ts`. Evidencia:
[H6](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/asi-h6-audios.png).

#### Qué se ve en la burbuja de un audio entrante

- La burbuja no muestra texto propio (`body` se usa sólo como transcripción). Sección rotulada «audio».
- Si `audio.canPlay`: botón **«Escuchar audio»** (contorneado chico) → «Cargando audio…» mientras baja los bytes. Al
  llegar, se reemplaza por un reproductor nativo `<audio controls>` rotulado «Audio recibido», ancho completo, que se enfoca
  y empieza a reproducir. No hay duración ni forma de onda fabricadas.
- Debajo, separado por una línea:
  - Completed con texto: «Transcripción» (rótulo chico) y el texto con saltos de línea conservados.
  - Si no: un texto gris según el estado, en este orden de prioridad: Pending/Transcribing «Pasando el audio a texto…»;
    Expired «El audio y su transcripción ya no están disponibles.» (sin reproducir, aunque quede un cuerpo viejo);
    `reasonCode: tooLarge` «El audio es demasiado grande para pasarlo a texto.»; Unavailable sin reproducción «El audio no
    está disponible.»; reproducible sin transcripción «No pudimos pasar este audio a texto. Escuchalo: el asistente no lo
    va a responder.»; si no, «No hay una transcripción disponible.».
- Con un 429 al escuchar: el botón queda deshabilitado y aparece «Esperá {{count}} segundo(s) para volver a escuchar.»
  (`useCountdown`, 60 s si falta `Retry-After`); al vencer no reintenta.
- Nunca se muestran códigos internos.

#### Transcripción automática

- En modo Manual y con `whatsapp.read` (sin exigir Send), cada audio Pending cargado se pide **una vez** con un POST sin
  cuerpo. El sondeo del hilo (5 s) observa la finalización de todos los audios: no hay un GET por audio.
- Pausar la lectura (modo Asistente, pestaña oculta o lectura deshabilitada) aborta el transporte pero **conserva** las
  identidades ya intentadas: un POST incierto no se repite al volver. Sólo un dueño nuevo (actor, conversación, cultura,
  permisos) las olvida.
- Un GET del hilo que falla de forma transitoria no cancela un POST en vuelo (`a1cc4eb`, `2b93fbd`).
- Un error operativo muestra un toast por audio; si después el hilo observa la finalización, se retira.
- Un 403 (del POST o de los bytes) llama a `denyRead` y retira toda la conversación.
- Un audio Completed vigente pasa a ser fuente de la sugerencia de H2; uno fallido o vencido nunca se convierte en texto.

#### Bytes privados

- Se piden sólo al tocar «Escuchar», con `canPlay`, por `api.blob` con `accept: audio/ogg` y `no-store`; se rechaza un tipo
  distinto de `audio/ogg` o un blob vacío.
- `PrivateAudio` crea la Object URL al montar y la revoca (pausa y quita `src`) al desmontar o cambiar de blob. Al cambiar
  de dueño, dejar de poder reproducirse o desmontar, se aborta la descarga y se tiran los bytes. Nada va a QueryCache.

#### 390 y 320 px

Escena `src/test/visual/whatsapp-audio.spec.ts` (escenario `chat-audio`) en es/en a 1440, 390 y 320 px: comprueba la
transcripción visible, tres botones «Escuchar audio», el texto de vencido y que la página no se desborde a lo ancho. Guarda
capturas de evidencia en `test-results/visual/whatsapp-audio-{es|en}-{ancho}.png` (no son referencias comparadas).

### 3.6 «Cuándo lo ofrece el asistente» en el editor del formulario (tablero 03)

`src/features/business-forms/pages/FormEditorPage.tsx`, `draftSession.ts`, `editorModel.ts`, `model.ts`.

- **Zona:** superficie «El formulario», último campo, a todo el ancho (`sm:col-span-2`) debajo de Nombre, Texto del botón
  final y Prefijo.
- **Campo:** `Textarea` «Cuándo lo ofrece el asistente», hasta 1000 caracteres, con la ayuda «El asistente lee el
  mensaje y, si coincide con esto, ofrece el formulario. Sin este texto no lo ofrece. No guarda nada sin que la persona
  confirme.». Sólo lectura cuando el editor lo está.
- **Es un solo texto, sin idioma:** no cambia con el selector de idioma del contenido ni cuenta como traducción faltante.
- Vacío viaja `null` (apaga la oferta). El front no recorta: manda lo escrito y el servidor devuelve el valor recortado; el
  guard rechaza una respuesta con espacios sobrantes.
- Viaja con «Guardar borrador» (mismo UUID, revisión y recibo). **No viaja** en la comprobación ni en la aceptación de la
  publicación (`checkFormPublication` y `acceptFormPublication` arman el cuerpo sin `offerWhen`): guardarlo rige sin
  republicar.
- Error 400 en `offerWhen`: se marca en el campo con foco y sin toast duplicado (está en la lista de rutas visibles de
  `FormEditorPage`).
- **409 del borrador:** la revisión del borrador (`DraftReview`) suma una fila para este texto; si una de las dos versiones
  está vacía dice «No lo ofrece el asistente»; se puede conservar el mío o usar el actual.
- La regla de idioma nueva (punto 35) no lo afecta: ya es un texto único.

### 3.7 «Qué sugerir después» (s1–s3)

`src/features/business-forms/components/FormSuggestionsSection.tsx`, `src/features/business-forms/suggestions.ts`,
`src/features/business-forms/api/suggestions.ts`, `FormConditionsEditor.tsx` (compartido con reglas). Evidencia del
tablero: [s1](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/s1-sugerir-idea.png),
[s2](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/s2-sugerir-editor.png),
[s3](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/s3-sugerir-celular.png).

**Dónde y cuándo aparece:** en el editor `/formularios/{id}`, después de las preguntas, cargada en diferido (`lazy` +
`Suspense` con spinner). Sólo si el formulario ya fue guardado una vez (`session.confirmed`) y la persona tiene
`formsuggestions.manage`; sin ese permiso no se dibuja ni se consulta nada.

#### Disposición

Dos columnas en escritorio (`lg`): el editor a la izquierda y «Historial de sugerencias» a la derecha (320 px). Debajo
de `lg`, el historial baja debajo del editor.

**Superficie «Qué sugerir después»:**

- Encabezado: título a la izquierda; a la derecha, en este orden: `Badge` **«Cambios sin guardar»** si hay cambios, o el
  texto **«Revisión {{n}}»** si no; botón **«Guardar sugerencias»** («Guardando sugerencias…» mientras guarda); y, cuando
  está deshabilitado, un **párrafo debajo del botón** con el motivo: «Esperá a que termine la operación.», «Esperá N
  segundos para volver a intentar.», «Releé y elegí qué conservar antes de guardar.», «Revisá los datos marcados antes de
  guardar.» o «No hay cambios para guardar.».
- Primer párrafo: «Después de esta respuesta, el asistente puede ofrecer estos formularios a quien respondió. Se aplica al
  guardar: no hace falta volver a publicar en WhatsApp.».
- Lista vacía: «Este formulario todavía no sugiere otros formularios.».
- **Fila plegada** (`aria-label` «Sugerencia N»): nombre del formulario destino (en el idioma del contenido, con respaldo
  al original) + `Badge` del modo («La IA decide» / «Preguntar siempre») o, si el destino ya no está, «Dado de baja» /
  «Eliminado»; `Badge` «Inactiva» si está apagada; «Cuándo sugerirlo: …» (sólo modo IA); «Si: …» con las condiciones en
  palabras; «Datos que pasan: …». A la derecha, íconos «Editar sugerencia N» (lápiz, `aria-expanded`) y «Quitar sugerencia
  N» (tacho). Un destino que ya no está en la lista de publicados se nombra «Formulario publicado no disponible»; uno de
  otra versión, «Formulario de otra versión».
- **Fila abierta** (una por vez):
  1. «Formulario a sugerir» (`NativeSelect`, ayuda «Solo aparecen formularios publicados.», primera opción «Elegí un
     formulario publicado»). Cambiar el destino borra los mapeos.
  2. «Cómo se ofrece» (radios en tarjeta): «La IA decide» — «Lo ofrece si encaja con el cuándo o si la persona lo pide.»;
     «Preguntar siempre» — «Lo ofrece después de cada respuesta, sin IA. Se puede limitar con Solo si.».
  3. «Cuándo sugerirlo» (`Textarea` hasta 1000, sólo en «La IA decide», ayuda «Es lo que lee el asistente para decidirlo.
     Escribilo como se lo explicarías a un compañero.»). Es un texto único, sin idioma. En «Preguntar siempre» se conserva
     en el borrador local pero viaja `null`.
  4. Casilla «Sugerencia activa».
  5. «Si (opcional)»: el editor de condiciones compartido con reglas (hasta 10; misma gramática que la planilla).
  6. «Datos que pasan»: filas «Dato de origen N» / «Pregunta de destino N» / ícono «Quitar dato N»; botón «Agregar dato»
     (hasta 40, deshabilitado sin destino); ayuda «Solo se unen preguntas del mismo tipo. El resto lo completa la persona;
     siempre revisa los datos antes de confirmar.».
  7. «Listo» (cierra la fila y devuelve el foco a su lápiz).
- Al pie: «Agregar sugerencia» (hasta 20); la nueva se abre en el lugar.

**Reglas de los mapeos** (`mappingTargets`): «Número de la respuesta» sólo va a texto corto o largo; una pregunta va a
otra del mismo tipo; una elección sólo a otra elección **del mismo catálogo explícito** (`370fdf7`: dos listas propias no se
unen aunque coincidan IDs o etiquetas); Foto, Documento y «elegir varias» no se mapean como origen ni como destino. Una
pregunta destino no puede recibir dos datos.

**Destino dado de baja o eliminado (BA6):** sólo se puede quitar; lo describe la sección 6.

**Superficie «Historial de sugerencias»:** lista de revisiones con enlace «Revisión N», `Badge` «Vigente» en la actual,
«{{fecha y hora}} · {{autor}}» y el resumen de cambios («N sugerencias agregadas · N actualizadas · N quitadas · cambió el
orden» —«Cambió el orden»— o «Sin cambios», claves de reglas reutilizadas); paginado si hay más de una página; al pie «Cada guardado
conserva una copia. Ver una revisión no cambia el borrador.». Tocar una revisión abre un diálogo (alto máximo 90 % con
desplazamiento) con el resumen de esa revisión; si la versión publicada de origen cambió, condiciones y mapeos se cuentan
(«N condiciones guardadas», «N datos mapeados») en lugar de nombrarlos. Al cerrar, el foco vuelve al enlace.

#### Estados

| Caso | Qué se ve |
| --- | --- |
| Cargando | Superficie con spinner |
| Falla de lectura | `EmptyState` «No pudimos cargar los datos» (clave común) + «Reintentar» |
| Formulario no publicado (`Forms.Suggestion.SourceNotPublished` en `options`) | «Publicá el formulario para configurar qué sugerir después.» como estado de configuración, sin toast |
| 403 (lectura o guardado) | «Ya no tenés permiso para administrar las sugerencias.»; se retiran borrador y consultas |
| 400 con errores de campo | Se normalizan las rutas con corchetes, se abre la sugerencia del primer error y se enfoca su control; una ruta desconocida deja un único toast general |
| 409 `Forms.Suggestion.RevisionConflict` | Bloque «Las sugerencias cambiaron. Releelas y elegí qué conservar.» + «Releer sugerencias»; con la revisión releída: «Conservá tu borrador completo. El próximo Guardar reemplazará las sugerencias de la revisión N.» + «Usar la revisión actual» / «Conservar mis cambios». Nada se combina solo |
| 429 | Espera con cuenta regresiva; todo bloqueado; motivo bajo «Guardar» |
| Red/5xx al guardar | Un toast de error; se conserva el borrador y la misma revisión |
| Guardado | Toast «Guardamos las sugerencias.»; se relee el historial |

- Validación local (`prepareSuggestions`): destino disponible, «Cuándo» obligatorio en modo IA y hasta 1000, condiciones
  válidas, mapeos compatibles y sin destinos repetidos. Textos: «Completá este dato.», «Usá hasta 1000 caracteres.»,
  «Elegí un formulario publicado disponible.», «Elegí preguntas del mismo tipo.», «Esta pregunta ya recibe otro dato.»,
  «Revisá las condiciones.», «Superaste el límite permitido.».
- Las sugerencias sin guardar entran en la guarda de salida y en «Descartar» del editor (`FormSuggestionsRoute.test.tsx`),
  sin guardar el borrador del formulario.

#### 390 y 320 px

Lo recorre sólo la escena BA6 de `src/test/visual/formLifecycle.spec.ts` (es/en a 390 y 320): la página no se desborda,
la fila del destino dado de baja tiene un único botón y «Guardar sugerencias» se habilita al quitarla. Los campos de la
fila abierta van en una columna debajo de `sm`; los mapeos, en tres columnas desde `sm`. El encabezado con badge, botón y
motivo se parte en varias líneas (no hay captura de la fila abierta en celular).

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), commit `bb98bf2`; review E2, punto 38):
> el encabezado de «Qué sugerir después» muestra «Cambios sin guardar» o «Revisión N» al lado de «Guardar sugerencias» y un
> párrafo con el motivo del deshabilitado debajo del botón («No hay cambios para guardar.», «Revisá los datos marcados
> antes de guardar.», la espera y el conflicto). Además marca errores **antes de intentar guardar**: al tocar «Agregar
> sugerencia», la fila nueva aparece con «Elegí un formulario publicado disponible.» y «Completá este dato.» porque la
> validación corre en vivo desde el primer render (el review lo señala en reglas; acá pasa lo mismo). Con la regla nueva:
> sin estado de guardado ni revisión junto al botón, motivo como tooltip y validación recién al intentar guardar. Los
> números de revisión del historial sí se conservan. Estado en `982ade8`: pendiente (ningún commit posterior a
> `bb98bf2` toca `FormSuggestionsSection.tsx`).

Observación del relevamiento: la ayuda del modo «Preguntar siempre» dice «Se puede limitar con Solo si.», pero la sección
se titula «Si (opcional)».

### 3.8 Propuesta de catálogo del asistente (H4)

`src/features/business-forms/pages/CatalogFiltersEditorPage.tsx`, `src/features/business-forms/hooks/useCatalogDraftEditor.ts`,
`src/features/business-forms/api/catalogs.ts`, `src/features/business-forms/api/catalogResponse.ts`,
`src/features/business-forms/components/CatalogMetadataFields.tsx`. Evidencia:
[H4](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/asi-h4-catalogo.png).

**Qué es:** el asistente crea, a pedido por WhatsApp, un catálogo **Filters** inactivo con `proposal` (nunca SQL). Se
revisa en el mismo editor Filters de `/catalogos/{id}`. En el listado de catálogos aparece como un catálogo Filters
inactivo más: `CatalogListTable` no tiene una marca de propuesta (verificado: no lee `proposal`). Cómo se entera la persona
que tiene que revisarla no está en el front (no verificado).

#### Pantalla (sólo cambia lo que sigue; el editor Filters se describe en el anexo anterior y en la sección 9)

- **Cabecera:**
  - Estado junto al título: con la propuesta pendiente, `Badge` con borde warning «Propuesta del asistente»; si no,
    «Activo» o «Inactivo»; con cambios, se agrega « · Cambios sin guardar».
  - Acciones con `formcatalogs.manage`:
    - pendiente: «Descartar propuesta» (contorneado) y **«Aprobar y activar»** (principal);
    - aprobada, o catálogo sin propuesta: dar de baja/reactivar, «Descartar» y «Guardar»;
    - descartada: «Descartar» y «Guardar», sin dar de baja/reactivar.
- **Bloque de la propuesta** (enfocable; recibe el foco cuando la propuesta deja de estar pendiente):
  - `Banner` con «**{estado}.** La pidió {{name}} por WhatsApp el {{fecha y hora}}.» y, según el caso, «No se usa hasta
    que alguien la apruebe.» o «La resolvió {{name}} el {{fecha}}.»;
  - los estados se leen «Propuesta del asistente», «Propuesta aprobada» o «Propuesta descartada»;
  - debajo, una línea warning por limitación (hasta 5): «No se pudo filtrar por «{{criterio}}»: ese dato no existe en la
    vista Usuarios.»;
  - no muestra identificadores del modelo ni de instrucciones (`provenance` se valida pero no se dibuja).
- **«El catálogo»** (`CatalogMetadataFields`):
  - selector «Textos en» arriba, que arranca en el idioma original de la propuesta;
  - ayuda «El original ({{idioma}}) es obligatorio; el otro idioma es opcional.»;
  - Nombre y Descripción obligatorios sólo en el original;
  - «De dónde salen» y «Tipo de valor» de sólo lectura, con ayuda.
- Condiciones del filtro y vista previa con persona de ejemplo, como el editor Filters común.

#### Aprobar y descartar

- **Aprobar y activar** exige elegir una persona de ejemplo (sin ella el botón queda deshabilitado; la ayuda del campo dice «Elegí una persona de
  ejemplo. Esta prueba no cambia su identidad ni sus permisos.»). Manda un
  solo comando `POST /api/forms/catalogs/{id}/proposal/approve` con `expectedRevision`, nombre y descripción (sólo los
  idiomas con texto), filtros y `sampleUserId`. No pasa por el PUT común ni corre antes la vista previa local: el servidor
  vuelve a probar con esa persona. El guard exige revisión siguiente y estado Approved.
  - éxito: toast «Catálogo guardado.» (clave común del editor);
  - red o 5xx: conserva la edición para un reintento explícito con la misma revisión;
  - 400: se liga al campo del idioma original, sin toast duplicado.
- **Descartar propuesta** abre un `ConfirmDialog` destructivo «¿Descartar esta propuesta?» / «No se activará ni podrá
  usarse en formularios. Se conserva quién la pidió y la propuesta original.».
  - Confirmar manda `POST …/proposal/discard` con `expectedRevision`; el guard exige revisión siguiente y estado
    Discarded; toast «Propuesta descartada.»; se invalida el listado de catálogos.
  - Cancelar devuelve el foco al disparador.
- **409:** sólo `Forms.Catalog.RevisionConflict` pasa a la revisión de solo lectura («Releer» / adoptar), conservando lo
  editado hasta adoptar explícitamente; no se repite la aprobación sola.
- **Sin manage:** se ve la historia de la propuesta sin acciones ni personas de ejemplo. Perder manage con la pantalla
  abierta retira los controles y descarta un resultado tardío.
- **Después de resolverla,** el original sigue obligatorio y la traducción ausente queda vacía.

#### 390 y 320 px

Sin escena visual propia (los recorridos de `catalogFilters.spec.ts` usan catálogos sin propuesta). La cabecera común
baja el estado y las acciones a otra línea debajo de 640 px; el editor y la vista previa se apilan debajo de `xl`. No
verificado en navegador.

> **No copiar — regla de idioma del 2026-10-23** (review E2, punto 35):
> la propuesta usa el selector «Textos en» de `CatalogMetadataFields` y un par de textos por idioma. Con la regla nueva no
> hay selector: se escribe en el idioma del menú (el original), la traducción se carga cambiando el idioma del menú y, con el
> menú en el otro idioma, el campo sin traducción muestra el original como referencia. Estado en `982ade8`: pendiente.

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), commit `bb98bf2`; review E2, punto 38):
> el estado de la cabecera de `CatalogFiltersEditorPage` agrega «· Cambios sin guardar». Estado en `982ade8`: pendiente.

> **No copiar — review E2, punto 39** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)):
> «El catálogo» debería tener sólo Nombre y Descripción (opcional), con origen y tipo de valor como una línea de sólo
> lectura arriba, sin caja ni explicación. Hoy muestra «De dónde salen» y «Tipo de valor» como campos con ayudas que
> explican que no se cambian. Estado en `982ade8`: pendiente.

### 3.9 Formularios que faltan (H7)

`src/features/business-forms/pages/MissingFormsPage.tsx`, `src/features/business-forms/api/missingRequests.ts`,
`src/features/business-forms/missingFormSeed.ts`. Evidencia:
[H7](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/asi-h7-faltan.png).

**Ruta:** `/formularios/faltantes` · **Permiso:** `formtemplates.manage` (ruta y página) · Tablero H7.

#### Zona por zona

- **Cabecera (`Page`):** título «Formularios que faltan», sin acciones ni estado.
- **Descripción:** «Lo que alguien quiso registrar por WhatsApp y no tenía un formulario que pudiera completar. El
  asistente los agrupa por tema; no se guardan nombres ni mensajes.».
- **Barra de filtros:**
  - buscador «Buscar por tema» (todo el ancho en celular, 18 rem desde `sm`);
  - segmentado «Estado del pedido» con **«Pendientes (N)» · «Descartados (N)»**, conteos del servidor (`stateCounts`;
    «…» mientras no llegan); cambiar de estado vuelve a la página 1 y cierra un descarte a medio confirmar;
  - a la derecha, el total «{{N temas}} · {{N pedidos}}» (por ejemplo «3 temas · 12 pedidos»).
- **Tabla** (en `Surface`):

| Columna | Contenido | Ordenable |
| --- | --- | --- |
| Tema | Nombre en negrita y descripción; si está descartado, «Descartado por {{name}} · {{fecha y hora}}» | No |
| Pedidos | Cantidad | No |
| Personas | Personas distintas | No |
| Último pedido | Fecha y hora en la zona del perfil | Sí (`lastRequestedAtUtc`) |
| Acciones | «Crear formulario» (con ícono +, nombre accesible «Crear formulario {{tema}}») sólo con `formtemplates.read`; «Descartar» (nombre accesible «Descartar {{tema}}») sólo en pendientes | — |

- Paginado común debajo.
- No hay nombres de quienes piden, mensajes, teléfonos ni valores: el guard los rechaza si llegaran.

#### Crear formulario

Es un enlace a `/formularios/nuevo` con `state.formSeed = {ownerActorId, name, originalCulture}`. El editor sólo la usa si
es un alta, la persona es la misma y no hay un borrador ya editado: abre un formulario vacío en el idioma original del
tema, con el nombre puesto. No resuelve ni descarta el tema.

#### Descartar

- `ConfirmDialog` destructivo «Descartar pedido» / «"{{tema}}" pasará a Descartados. Si lo vuelven a pedir, aparecerá otra
  vez en Pendientes.» → «Descartar».
- Manda `POST /api/forms/missing-requests/{id}/discard` con la revisión del tema; el guard exige revisión siguiente y
  estado Discarded.
- Éxito: toast «Pedido descartado.» y se relee la lista.
- **409:** toast warning «El pedido cambió. Releímos la lista; revisalo antes de volver a descartar.» con «Reintentar». Se
  relee la lista y los descartes quedan bloqueados hasta una relectura exitosa. Nunca se repite el POST solo.
- **429:** cuenta regresiva; buscador, segmentado y tabla quedan deshabilitados (`fieldset disabled`); aparece «Esperá N
  segundos para volver a intentar.»; al vencer hace falta confirmar de nuevo.

#### Estados

| Caso | Qué se ve |
| --- | --- |
| Cargando | Spinner de la tabla |
| Nadie pidió nada (los dos conteos en 0) | «Nadie pidió formularios que no existen» |
| Vacío por estado | «No hay pedidos pendientes» / «No hay pedidos descartados» |
| Búsqueda sin coincidencias | «No hay temas que coincidan con la búsqueda» + «Limpiar filtros» |
| Error de lectura | Error en la tabla «No pudimos cargar los pedidos» + «Reintentar»; si había filas, se conservan, aparece un «Reintentar» arriba de la tabla y un único toast con la causa |
| 429 en la lectura | Espera y bloqueo; las filas cargadas se conservan; la búsqueda no cambia durante la espera (`d1b2aac`) |
| 401/403 | `ForbiddenPage`; se cancelan y se quitan las consultas, sin dejar filas a la vista |
| Cambio de actor | Una respuesta de descarte del actor anterior se ignora |

**Claves de consulta:** `["form-missing-requests", actorId, cultura, permisos]` + el pedido; `gcTime: 0`, `staleTime: 0`, sin
reintentos, `keepPreviousData`, error de dueño local; `useUnobservedQueryCleanup` retira lo que queda sin observadores.

#### 390 y 320 px

Escena `src/test/visual/forms-missing.spec.ts` (escenario `forms-missing`) en es/en a 1440, 390 y 320: tablero, diálogo de
descarte y pestaña «Descartados (2)», sin desborde horizontal de página. La tabla se desplaza dentro de su caja (columna
Tema entre 12 y 18 rem). Capturas de evidencia en `test-results/visual/forms-missing-*.png` (no referencias comparadas).

> **A evaluar contra la regla de interfaz del 2026-10-23:** la línea «Esperá N segundos para volver a intentar.» y el
> «Reintentar» suelto arriba de la tabla no son textos pegados a un botón ni el review los lista, pero conviene ubicarlos
> según el contrato de mensajes al adaptarlo.

### 3.10 Procedencia y cadena en la respuesta (s4)

`src/features/business-forms/components/ResponseAssistance.tsx`, usado por `ResponseAnswer.tsx` en la pestaña Respuesta
de D2 (`ResponseDetailContent.tsx`) y en la sección Respuesta de DC1 (`FormResponseFullPage.tsx`). El marco D2/DC1 lo
describe la sección 5. Evidencia:
[s4](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/s4-sugerir-cadena.png).

- **Marcas por pregunta** (debajo de cada valor, sólo en los datos incluidos de la propuesta): «De {{nombre del formulario
  de origen}}» si el dato vino mapeado y el origen está en la cadena (si no, «De la respuesta de origen»), y «Corregido al
  completar» si la persona lo cambió.
- **Bloque «Cómo empezó»** (región con ese nombre, después de la lista de valores; no aparece si no hay asistencia ni
  cadena):
  - «Lo preparó el asistente» o «Lo preparó **{{operador}}**» (Manual; «Persona sin información» si falta el nombre);
  - si vino de una sugerencia: «Preguntar siempre · Sin IA» o «La IA decide»;
  - el motivo capturado, con saltos de línea;
  - enlace «Ver datos propuestos» / «Ocultar datos propuestos» (`aria-expanded`) que pliega la región «Datos propuestos»:
    pregunta, valor histórico (con fotos y documentos por el visor común) y `Badge` «Incluido», «No incluido» o
    «Corregido al completar»;
  - «Cadena de respuestas»: lista «{{número}} · {{formulario}}»; la actual va sin enlace con `aria-current="page"`; las
    otras enlazan a `/formularios/{definitionId}/respuestas/{recordId}`; mientras se carga otra respuesta, los enlaces se
    ocultan.
- Todo es captura histórica: modo, motivo y operador no se reconstruyen con la configuración actual ni se inventan
  aperturas.
- Sin `formrecords.read` (o al revocarlo) se retiran motivos y cadena completa; quien sólo decide (Decide sin Read) no ve
  la respuesta y por eso tampoco esto.
- La actividad de D2/DC1 dice «Se lo mandó {{name}}» o «Se lo mandó el asistente» a partir de `origin.senderKind`; eso es
  parte de la historia de la sección 5.

**390 y 320 px:** escena `src/test/visual/responses-assisted.spec.ts` en es/en a 1440, 390 y 320. En escritorio abre D2
sobre la planilla; debajo de 640 px no hay tabla, se abre directo la pantalla completa. Comprueba «De Entrega»,
«Corregido al completar», el operador, «Preguntar siempre · Sin IA», el enlace al origen con su `href`, el actual con
`aria-current`, el plegado y que la página no se desborde. Capturas `responses-s4-origin-*` y `responses-s4-proposed-*`.

### 3.11 Hooks y estado

| Dueño | Claves / estado | Cancelación y retiro | Concurrencia |
| --- | --- | --- | --- |
| Hilo (`useChatThread`) | `["whatsapp-chat", actor, "read", cultura, permisos, "conversation", id]` | `denyRead` (nuevo) marca el alcance como denegado, cancela sin rollback y quita las consultas; se usa ante 403 de hilo, audio y transcripción | Gate de valores de respuestas sin cambios; ahora también borra `assistant` |
| Modo (`useChatThreadActions`) | Mutaciones `retry: false`; la revisión a confirmar guarda sólo `{requestId}` | Al desmontar, retira su toast y no notifica | `expectedRevision`; el 200 debe traer el mismo modo y revisión igual o siguiente |
| Sugerencia (`useAssistantSuggestion`) | Estado local, **sin** QueryCache ni MutationCache; dueño = actor+conversación+cultura+permisos+revisión de modo+fuente+revisión del destinatario | Desmontar aborta la operación; cambiar de dueño reinicia el estado privado | Una operación a la vez; UUID por pedido; Consultar antes de repetir; sondeo de Pending cada 5 s con pestaña visible |
| Transcripción (`useChatAudioTranscription`) | Mapa de intentos por mensaje, por dueño `[owner, contactId]` | Salir del dueño aborta y olvida; pausar aborta y conserva | Un POST por audio Pending; un resultado tardío de otro dueño se ignora |
| Audio (`ChatAudio`) | Blob en estado local por `[owner, contactId, messageId, canPlay]` | Desmontar o cambiar de alcance aborta y descarta; `PrivateAudio` revoca la URL | Un pedido a la vez; 429 con cuenta regresiva |
| Envío de texto (`ChatComposer`, `fc306b7`) | Estado local (`sending`), ya **no** `useMutation` | — | El texto y el `requestId` congelados no quedan en MutationCache |
| Sugerencias s1–s3 | `[...formScopeKey, "suggestions", definitionId, "editor", definitionRevision, publishedVersionId]` y `[..., "history", page]`; `gcTime: 0`, `placeholderData` previo | `usePrivateQueryCleanup` al desmontar; 403 retira borrador y consultas | `expectedRevision`; 409 conserva el borrador entero y exige elegir |
| Propuesta H4 | Borrador del editor Filters (`useCatalogDraftEditor`) | Perder manage retira controles; resultado tardío descartado | `expectedRevision`; revisión sólo con `Forms.Catalog.RevisionConflict` |
| Faltantes H7 | `["form-missing-requests", actorId, cultura, permisos, query]` | 401/403 cancela y quita; sin observadores se retiran | Revisión del tema; 409 bloquea hasta releer |
| Cuándo lo ofrece | Parte del borrador del formulario (UUID, revisión y recuperación existentes) | — | Fila propia en la revisión 409 |
| s4 | Lectura del detalle de respuesta (dueño único de la sección 5) | Revocar Read retira la región | — |

Privacidad: propuestas, selección de datos, intentos de envío, bytes de audio e intentos de transcripción viven en su
dueño, fuera de MutationCache, URL, storage y logs. La semilla de H7 viaja sólo en el `state` del historial y se valida
contra el actor.

### 3.12 Clientes HTTP y guards zod

Todos con `z.strictObject` (una clave desconocida es un error genérico sin payload).

**WhatsApp (`src/features/whatsapp/api/`):**

- `chat.ts`:
  - **`getThread` cambió.** Valida `assistantAvailability.state ∈ {available, notInstalled, notConfigured, paused,
    modelNotQualified}`, `assistantSuggestion` nullable, `origin ∈ {Conversation, FormAction, Assistant}` en mensajes y
    salidas, `assistant` nullable (`{reason, proposedFields[≤40, ids únicos]}`), `quickReplies` nullable (1–3 títulos de
    1–20 caracteres) y `audio` nullable. Después aplica el retiro de valores privados.
  - **`changeMode`.** Modo `Assistant | Manual` (no hay `Bot`). Ahora exige un 200 con
    `{mode, modeRevision, assistantSendInProgress}`, el mismo modo y revisión igual o siguiente. Antes era un PUT sin
    cuerpo de respuesta.
  - `Outgoing.kind` admite `QuickReplies`.
- `assistant.ts`:
  - **Sugerencia:** `{requestId, sourceMessageId, state, proposal, outgoing}`, con coherencia: Ready trae `proposal` y no
    `outgoing`; Sent trae `outgoing` y no `proposal`; los demás estados, ninguno. Debe corresponder a la fuente o al
    pedido consultado.
  - **`proposal`:** `{proposalId (nullable: null = sugerencia configurada sin IA), definitionId, versionId, versionNumber,
    name, culture es|en, reason, fields}`.
  - **Recibo Sent:** Flow, `origin: Conversation`, `quickReplies: null`, metadata sellada, `recoveryPath` nulo o la ruta
    propia `/api/whatsapp/chats/{id}/outgoing/{requestId}`. No se compara el operador con el actor.
- `audio.ts`:
  - `chatAudioSchema = {state ∈ Pending|Transcribing|Completed|Failed|Uncertain|Unavailable|Expired, canPlay,
    reasonCode?: "tooLarge" | null}`; otro `reasonCode` se rechaza;
  - los bytes deben ser `audio/ogg` no vacío.
- `formMetadata.ts` (nuevo, separado de `forms.ts`): único decoder de la metadata sellada de un formulario. Nombre como mapa
  `es`/`en` con al menos un texto no blanco y nombre no blanco en su cultura efectiva. Lo comparten formularios, asistente
  y línea de tiempo.

**Formularios (`src/features/business-forms/api/`):**

- `response.ts`: la definición trae `offerWhen` (`null` o 1–1000 caracteres ya recortados).
- `forms.ts`:
  - el guardado del borrador manda `offerWhen`;
  - `checkFormPublication` y `acceptFormPublication` arman un cuerpo explícito `{name, prefix, draft, expectedRevision[,
    requestId, checkToken]}` **sin** `offerWhen`.
- `suggestions.ts` (nuevo):
  - **Conjunto:** `{id|null, definitionId, sourceVersionId|null, revision, suggestions[≤20], createdAtUtc|null,
    author|null}`.
  - **Sugerencia:** `{id, destinationDefinitionId, destinationVersionId, mode Assistant|Always, when ≤1000|null,
    isActive, conditions[≤10], mappings[≤40]}`.
  - **Condición:** operadores `in, anyOf, contains, equals, gte, lte, answered, unanswered, notAsked`.
  - **Mapeo:** `{source{kind Field|RecordNumber, fieldId|null}, destinationFieldId}`.
  - **`options`:** `{source, destinations[], unavailableDestinations[{definitionId, name, state Deactivated|Deleted}],
    conditionChoices}`; las preguntas se validan con `canonicalFormFieldSchema`.
  - **Historial:** paginado con `changes{addedSuggestions, updatedSuggestions, removedSuggestions, orderChanged}`.
  - El POST manda `{expectedRevision, suggestions}` sin `destinationVersionId`, que fija el servidor.
- `missingRequests.ts` (nuevo):
  - **Tema:** `{id, revision>0, name, description, originalCulture, requestCount, personCount, lastRequestedAtUtc, state
    Pending|Discarded, discardedBy|null, discardedAtUtc|null}`.
  - **Página:** `{items, page, pageSize 1–100, totalCount, totalRequests, stateCounts{pending, discarded}, totalPages,
    hasPrevious, hasNext}`.
  - Descartar valida identidad, revisión `+1` y estado Discarded.
- `catalogResponse.ts` / `catalogs.ts`:
  - **Detalle de catálogo:** suma `proposal` nullable (requerida) con `{state Pending|Approved|Discarded, originalCulture,
    requestedBy, requestedAtUtc, decidedBy, decidedAtUtc, provenance{modelProfileId, modelProfileVersion, instructionsId,
    instructionsVersion, occurredAtUtc, channelId "whatsapp-assistant"}, originalProposal{metadataReadOrdinal, name,
    description, filters, limitations[≤5, code UnsupportedColumn]}}`.
  - Pendiente ⇔ sin decisión.
  - Con propuesta: Filters, original no vacío e inactivo salvo que esté aprobada. Sin propuesta: nombre y descripción
    bilingües, como antes de I1–I3.
  - `approveCatalogProposal` y `discardCatalogProposal` validan la revisión `+1` y el estado.
- `responses.ts`:
  - **Detalle:** suma `assistance` nullable `{senderKind Manual|Assistant, operator, reason, values[{proposed (valor
    histórico tipado), included, changed, mapped}], suggestion{originRecordId, suggestionId, mode Always|Assistant} |
    null}`.
  - **Cadena:** `chain[{id, definitionId, originRecordId|null, number, name, submittedAtUtc}]`.
  - **Origen:** `origin.senderKind`.

### 3.13 Cohortes y fixtures exportadas

Todas producidas por la misma corrida backend `20261023-064235-…` (HEAD del back `7e5b7a94`) y verificadas con
`httpCohort(...).verify()` (bytes, tamaños y SHA256 contra `provenance.json`; ver la sección 11).

| Cohorte (`httpCohort`) | Carpeta `src/test/fixtures/…` | Variable de entrada | Productor backend | Cuerpos | Consumidor |
| --- | --- | --- | --- | --- | --- |
| `suggestions` | `forms-suggestions-http` | `FORMS_SUGGESTIONS_HTTP_CONTRACT_INPUT` | `FormSuggestionHttpTests` + `FormSuggestionContractCapture` | 8 (vacío, guardado, opciones es/en, historial, revisión, conflicto, origen no publicado) | `business-forms/api/suggestionsHttpContract.test.ts` |
| `suggestionDetails` | `forms-suggestion-detail-http` | `FORMS_SUGGESTION_DETAILS_HTTP_CONTRACT_INPUT` | `FormSuggestionCaptureIntegrationTests` + `FormSuggestionDetailContractCapture` | 2 (detalle es/en con asistencia y cadena) | `business-forms/api/suggestionDetailHttpContract.test.ts` |
| `catalogProposals` | `forms-catalog-proposals-http` | `FORMS_CATALOG_PROPOSALS_HTTP_CONTRACT_INPUT` | `FormCatalogProposalHttpTests` + `FormCatalogProposalScenario` + `FormCatalogProposalContractCapture` | 6 (pendiente es/en, aprobada en, descartada es, conflicto, listado con original en en UI es) | `business-forms/api/catalogProposalHttpContract.test.ts` |
| `missingRequests` | `forms-missing-requests-http` | `FORMS_MISSING_REQUEST_HTTP_CONTRACT_INPUT` | `FormMissingRequestHttpTests` + `FormMissingRequestScenario` + `FormMissingRequestContractCapture` | 6 (pendientes es/en, descartados es/en, 409, 403) | `business-forms/api/missingRequestsHttpContract.test.ts` |
| `whatsappAssistant` | `whatsapp-assistant-http` | `WHATSAPP_ASSISTANT_HTTP_CONTRACT_INPUT` | `WhatsAppAssistantHttpTests` (+ `.Suggestions`, escenario, captura y dobles de operaciones) | 12 (seis estados de sugerencia, recibo, hilo con y sin RecordsRead, y tres de «Preguntar siempre»: sugerencia, hilo y recibo) | `whatsapp/api/assistantHttpContract.test.ts` |
| `whatsappAudio` | `whatsapp-audio-http` | `WHATSAPP_AUDIO_HTTP_CONTRACT_INPUT` | `WhatsAppChatTests.Audio` + `WhatsAppAudioContractCapture` | 3 (pedido Pending, hilo Pending, hilo Completed) | `whatsapp/api/audioHttpContract.test.ts` |

Los códigos de conflicto exportados son `Forms.Suggestion.RevisionConflict`, `Forms.Catalog.RevisionConflict`,
`Forms.MissingRequest.RevisionConflict` y el estado `Forms.Suggestion.SourceNotPublished` (409). Los dobles de
comportamiento (`src/test/mocks/catalogProposal.ts`, `src/test/responseAssistanceFixture.ts`,
`src/test/visual/missingRequestFixtures.ts`) están marcados como sintéticos y no acreditan el contrato. Ningún cuerpo
acredita Meta, el modelo ni la entrega real.

### 3.14 i18n

- **`whatsapp.json` (es/en):**
  - `author.assistant` reemplaza `author.bot`;
  - `mode.*` cambia (`Assistant`, `enable`, `enableConfirm`, `assistantDescription`, `availability.{notConfigured,
    paused, modelNotQualified}`, `manualSendInProgress`, `manualDescription` nuevo, `assistantControlDescription`,
    `manualContinues`);
  - `send.assistantMode` reemplaza `send.botMode`;
  - grupo nuevo `assistant.*` (sugerencia, configurada, prellenado, «Por qué lo mandó/lo sugirió», respuestas rápidas);
  - grupo nuevo `audio.*`, con `waiting_one` y `waiting_other` (plural verificado en `src/locales/parity.test.ts`);
  - `readForbidden`, `kind.Audio`;
  - `forms.language` pasó de «Le llega en {{language}}, el idioma de su cuenta.» a «Le llega en {{language}}.» y
    `forms.defaultMessage` a «Completá este formulario.».
- **`forms.json` (es/en):**
  - `offerWhen`, `offerWhenHint`, `offerWhenEmpty`;
  - grupo `suggestions.*` (con `modes`, `modeHints`, `errors`, historial con plurales);
  - grupo `missing.*` (con plurales `topicCount` y `requestCount`);
  - `detail.assistance.*`;
  - `catalogs.proposal.*` (con `states`).
- **`common.json`:** `navigation.missingForms` («Formularios que faltan» / «Missing forms»).
- **Textos en inglés de referencia:** «Switch to Assistant mode», «Take manual attention», «The assistant suggests
  sending {{name}}», «Review and send», «No, thanks», «Listen to audio», «What to suggest next», «AI decides» / «Always
  ask», «How it started», «Approve and activate», «Discard proposal».
- Reutiliza claves de reglas (`rules.revision`, `rules.save*`, `rules.useCurrent`, `rules.ifOptional`) en el editor de
  sugerencias.

### 3.15 Pruebas

**Unitarias y de comportamiento (Vitest + MSW):**

- WhatsApp:
  - `api/chatMode.test.ts`: acuse vacío rechazado, revisión y `assistantSendInProgress` observados, revisión fuera de
    rango rechazada.
  - `api/assistant.test.ts`: Ready, recibo compartido, estados sin propuesta, fuente o pedido ajenos, rutas cerradas,
    `proposalId` null.
  - `api/chatAssistant.test.ts`: explicación null y títulos de aclaración capturados.
  - `api/audio.test.ts`: los siete estados, tipos MIME rechazados y `reasonCode` seguro o inseguro.
  - `components/AssistantSuggestion.test.tsx` (13 casos):
    - envío con IDs incluidos, Cancelar con foco, consulta tras respuesta perdida, selección congelada;
    - 403, sólo lectura, propuesta tardía de otro contexto;
    - cuenta sin formularios elegibles, editor ordinario conservado, fuente FlowReply;
    - IA no disponible sin puntero, pausa con puntero, cultura de la propuesta.
  - `hooks/useAssistantSuggestion.test.tsx`: 429 pausa el sondeo.
  - `components/ChatAudio.test.tsx` (9): escuchar con transcripción pendiente, transcripción debajo, vencido,
    URL revocada, 403, aborto al desmontar, 429, audio sin transcripción, `tooLarge`.
  - `components/ChatAudioContext.test.tsx` (7): audio como fuente de la sugerencia, sólo lectura, 403 de bytes y de
    POST, un GET por audio, falla transitoria del hilo.
  - `hooks/useChatAudioTranscription.test.tsx` (7).
  - `hooks/useChatThreadActions.test.tsx`: sólo `requestId` en el estado de confirmación.
  - `lib/assistantPrivacy.test.ts`, `lib/assistantSendSession.test.ts`.
  - `pages/WhatsAppChatPage.test.tsx`:
    - modo Asistente, disponibilidad sin toasts repetidos, Manual con Send, confirmación retirada al perder Send;
    - cancelar, activar con ConfigureAssistant, sin oferta por cada estado de disponibilidad, foco tras el cambio;
    - texto y `requestId` fuera de MutationCache (`fc306b7`).
  - `hooks/useChatThread.test.tsx` suma la espera del retiro tras 403 y la cancelación antes de una respuesta tardía
    (`722d926`, `0f8a54f`).
- Formularios:
  - `pages/FormsPage.test.tsx`: tres casos de «Cuándo lo ofrece».
  - `draftSession.test.ts` y `editorModel.test.ts`: indicación en apertura, guardado congelado, recuperación y rebase.
  - `components/FormSuggestionsSection.test.tsx` (13), `suggestions.test.ts` (9), `api/suggestions.test.ts` (7),
    `pages/FormSuggestionsRoute.test.tsx`.
  - `pages/CatalogProposalPage.test.tsx` (8), `api/catalogProposal.test.ts` (3).
  - `pages/MissingFormsPage.test.tsx` (10), `api/missingRequests.test.ts` (3), `missingFormSeed.test.ts` (2).
  - `pages/ResponseAssistance.test.tsx` (2), `api/responseAssistance.test.ts`.
- Navegación: `src/layouts/navigation.test.ts` exige la entrada «Formularios que faltan» con `formtemplates.manage` y
  `exact`.
- Contratos reales: los seis `*HttpContract.test.ts` de la tabla 3.13. Se corren después del export conjunto.

**Visuales (Playwright, `src/test/visual`):**

| Spec | Escena | Anchos | Tipo |
| --- | --- | --- | --- |
| `whatsapp-access.spec.ts` | Contacto migrado en Atención manual | 390 | Referencia comparada `whatsapp-manual-390.png` |
| `whatsapp-audio.spec.ts` | `chat-audio` | es/en × 1440/390/320 | Aserciones + capturas de evidencia |
| `forms-missing.spec.ts` | `forms-missing` (tablero, diálogo, descartados) | es/en × 1440/390/320 | Aserciones + capturas |
| `responses-assisted.spec.ts` | `responses-assisted` (s4) | es/en × 1440/390/320 | Aserciones + capturas |
| `formLifecycle.spec.ts` | BA6, `forms-lifecycle-suggestions` | es/en × 390/320 | Aserciones + capturas |

Sin escena: modo Asistente, banner de disponibilidad, tarjeta y diálogo de H2, propuesta H4 y el editor de sugerencias con
una fila abierta. El cierre del 2026-10-17 dice que se inspeccionaron 36 capturas nuevas de H6, H7 y s4
([cierre local](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#implementación-local-de-etapa-5--2026-10-17)).
El back registra el recorrido en navegador y el piloto real en
[cierre local de la Etapa 5](../../features/formularios.md#cierre-local-de-la-etapa-5-y-del-review-de-la-etapa-4) y
[piloto del asistente y audios](../../features/formularios.md#piloto-del-asistente-y-audios--2026-10-18).

### 3.16 Para el multitenant

- **Un número por organización.** El front asume «una instalación comparte un número»; no hay configuración por operador
  (`src/features/whatsapp/AGENTS.md`). En multitenant, modo, disponibilidad de la IA, sugerencias y audios dependen de la
  organización activa: hay que sumarla a las claves (`chatReadScopeKey`, dueño de la sugerencia, dueño de audio y
  transcripción) y retirar todo al cambiar de organización, como hoy se hace al cambiar de actor.
- **Disponibilidad de la IA por organización.** `assistantAvailability` (`notConfigured`, `paused`, `modelNotQualified`,
  `notInstalled`) es hoy global. Si cada organización tiene su conexión o su cupo, el banner y el permiso de activar tienen
  que leer el estado de la organización.
- **Permisos nuevos por organización:** `whatsapp.configureassistant` y `formsuggestions.manage`. `whatsapp.manage` no
  existe más: no lo recrees como alias.
- **B2C.** Analizar y enviar una sugerencia exige cuenta vinculada activa con cultura. Para un cliente final sin cuenta,
  hay que definir otra identidad de destinatario antes de reutilizar la lógica de H2.
- **Formularios que faltan.** Los temas y conteos tienen que agregarse por organización. El tablero promete «no se guardan
  nombres ni mensajes»: en B2C, con datos de consumidores, eso pesa más.
- **Sugerencias y cadena.**
  - Destinos sólo de formularios publicados de la misma organización.
  - Los enlaces de la cadena s4 (`/formularios/{definitionId}/respuestas/{id}`) nunca deben cruzar organizaciones.
  - Mapeos de elección «mismo catálogo»: en multitenant, el mismo catálogo de la misma organización.
- **Propuestas de catálogo.** Filtran «la vista Usuarios». En multitenant tiene que ser la vista de personas de la
  organización (y en B2C, decidir si incluye clientes).
- **Audio privado.** Bytes con `no-store` y Object URL revocada: conservar eso y la retención por organización.
- **No copiar:**
  - los textos al lado de botones, «Cambios sin guardar», «Revisión N» y la validación en vivo de «Qué sugerir después»;
  - el selector «Textos en» de la propuesta H4;
  - la sección «El catálogo» con origen y tipo como campos.
- **Una oportunidad.** Los textos «Cuándo lo ofrece» y «Cuándo sugerirlo» son únicos y sin idioma. Si el multitenant
  atiende organizaciones con otro idioma principal, conviene decidir si ese texto (que lee la IA) sigue el idioma original
  de la organización.

### 3.17 Endpoints nuevos o cambiados

- WhatsApp:
  - `GET /api/whatsapp/chats/{id}/messages`: cambió el cuerpo (`assistantSuggestion`, `assistantAvailability`, `origin`,
    `assistant`, `quickReplies`, `audio`).
  - `PUT /api/whatsapp/chats/{id}/mode`: modos `Assistant|Manual`; ahora responde
    `{mode, modeRevision, assistantSendInProgress}`.
  - `POST /api/whatsapp/chats/{id}/assistant-suggestions` (`{requestId, sourceMessageId, expectedModeRevision}`).
  - `GET /api/whatsapp/chats/{id}/assistant-suggestions/{requestId}` (`no-store`).
  - `POST /api/whatsapp/chats/{id}/assistant-suggestions/{requestId}/discard`.
  - `POST /api/whatsapp/chats/{id}/assistant-suggestions/{requestId}/send` (`{requestId, includedFieldIds}`).
  - `POST /api/whatsapp/chats/{contactId}/messages/{messageId}/audio-transcription` (sin cuerpo, `no-store`).
  - `GET /api/whatsapp/chats/{contactId}/messages/{messageId}/audio` (`audio/ogg`, `no-store`).
- Formularios:
  - `POST /api/forms/definitions` y `PUT /api/forms/definitions/{id}/draft`: suman `offerWhen`;
    `GET /api/forms/definitions/{id}` lo devuelve.
  - `POST …/publication-checks` y `POST …/publications`: cuerpo explícito, sin `offerWhen`.
  - Sugerencias: `GET|POST /api/forms/definitions/{id}/suggestions`, `GET …/suggestions/options`,
    `GET …/suggestions/revisions?page=&pageSize=10` y `GET …/suggestions/revisions/{revision}` (todas las lecturas
    `no-store`).
  - Faltantes: `GET /api/forms/missing-requests?page=&pageSize=&search=&state=&sort=` (`no-store`) y
    `POST /api/forms/missing-requests/{id}/discard` (`{expectedRevision}`).
  - Propuestas: `POST /api/forms/catalogs/{id}/proposal/approve` y `POST /api/forms/catalogs/{id}/proposal/discard`;
    `GET /api/forms/catalogs/{id}` suma `proposal`.
  - `GET /api/forms/records/{id}`: suma `assistance`, `chain` y `origin.senderKind`.

### 3.18 Huecos

- Sin escena visual ni verificación en navegador, en este relevamiento, para:
  - modo Asistente y su banner;
  - aviso de disponibilidad;
  - confirmaciones de modo;
  - tarjeta y diálogo de H2;
  - propuesta H4;
  - el editor de sugerencias con una fila abierta.

  Lo dicho de 390 y 320 px para esas piezas sale del código.
- Cómo se entera una persona de que hay una propuesta de catálogo para revisar: el front no la marca en el listado ni
  avisa; el resto del circuito es del back (no verificado).
- El código de error exacto de un 409 de modo y su texto: el front muestra el `detail` del servidor; no hay cuerpo
  exportado de ese 409 en las cohortes de esta etapa (no verificado).
- El recorte de «Cuándo lo ofrece» lo hace el servidor; el front manda lo escrito (verificado en el front; la regla del
  back dice que se recorta al guardar).
- No se verificaron píxeles: las capturas de `test-results/` no están en el repo; sólo `whatsapp-manual-390.png` es una
  referencia commiteada.

---

## 4. Idioma original y traducción opcional (I1–I3)

> **No copiar — regla de idioma del 2026-10-23** (review E2, punto 35,
> [review](../../reviews/2026-10-23-code-review-catalogos-e2.md)): todo lo que esta sección describe como selector
> «Textos en», pares de campos por idioma («Nombre» y «Nombre en inglés», «Nombre del PDF» y «En Inglés») o exigencia de
> español o de los dos idiomas **queda retirado por decisión del usuario**. La regla nueva: lo que se crea en la web se
> escribe en el idioma elegido en el menú, que es su idioma original y el único obligatorio; el otro idioma es una
> traducción opcional que se carga **cambiando el idioma del menú** y editando lo mismo; se muestra en el idioma de la
> pantalla y, si falta, en el original. En `982ade8` el front **todavía no aplicó** esta regla (no hay commits de idioma
> después de `e02d1b4`); el back empezó con `58fb4e8e` (guarda `OriginalCulture` de catálogos y datos de las personas,
> «sin cambios de contrato ni de validadores todavía»). Qué queda y qué no del I1–I3 implementado está en
> [4.10](#410-lo-que-cambia-con-la-regla-del-2026-10-23).

Relevado sobre el front `982ade8` (2026-10-23), contra `9261e3d` (traspaso del 2026-10-17). Todo sale del código
commiteado; lo que no se pudo comprobar se marca «(no verificado)».

### 4.1 Resumen

**Qué había en `9261e3d`.** El editor ya tenía el selector «Textos en» con «Español · Original» / «English · N
pendientes», guardaba con traducciones incompletas y **exigía los dos idiomas para publicar** (tablero E y P1). La vista
previa mezclaba: lo que faltaba en el idioma mostrado se veía en el original con la marca «· Traducción pendiente». La
comprobación decía «En español y en inglés. Cada empleado lo recibe en el idioma de su cuenta.» y avisaba «Faltan N
textos en inglés.» como impedimento. El chat de WhatsApp exigía nombre y botón final en la cultura de la cuenta del
destinatario.

**Qué cambió (I1–I3, elegidos el 2026-10-18 en la versión 85 del lienzo, cerrados localmente el 2026-10-19):**

| Tablero | Qué define | Implementado en |
| --- | --- | --- |
| [I1 · El editor con la traducción opcional](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idi-i1-editor.png) | Original obligatorio, inglés opcional; conteo neutro «3 sin traducir»; cada texto vacío dice «Sin traducir», sin alerta; vista previa entera en un idioma, nunca mezclada | `47546f6`, `9fc142a`, `7d97c5b` |
| [I2 · Publicar con la traducción incompleta](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idi-i2-publicar.png) | La comprobación no frena por una traducción que falta: «En WhatsApp, en español», «El inglés no se publica», qué le falta y «Completar el inglés» | `17f2869`, `2d840c2` |
| [I3 · Los idiomas al publicar](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idi-i3-idiomas.png) | Confirmación con traducción incompleta o completa; versión nueva que suma o pierde el inglés, con aviso | `17f2869`, `2d840c2` |

Regla funcional del back:
[idioma original y traducción opcional](../../features/formularios.md#idioma-original-y-traducción-opcional--2026-10-19)
y su [verificación local](../../features/formularios.md#verificación-local-de-idioma-original-y-traducción-opcional--2026-10-19).
Cierre del front: [cierre local de idioma](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#cierre-local-de-idioma-original-y-traducción-opcional--2026-10-19).
Correcciones posteriores: puntos 1, 6 y 7 del [review del 2026-10-19](../../reviews/2026-10-19-code-review-cierre-formularios.md).

**Commits** (en orden):

| Commit | Fecha | Asunto | Qué tocó |
| --- | --- | --- | --- |
| `93d8160` | 10-18 | docs(formularios): sumar los tableros elegidos del idioma | Las tres capturas I1–I3 en `docs/design/evidence/formularios-whatsapp/` y su registro |
| `dbd7550` | 10-18 | fix(whatsapp): usar el idioma completo al preparar formularios | C2 presenta la cultura de `preparation`; `message:null` para la apertura sin editar (ver 4.8) |
| `db740a2` | 10-19 | test(contratos): capturar la preparación de idioma y los canales | Renovación de cuerpos de `whatsapp-assistant-http`, `whatsapp-audio-http`, `whatsapp-catalogs-http` |
| `17f2869` | 10-19 | feat(formularios): confirmar los idiomas de cada publicación | Guards `cultures`, `addedCultures`/`removedCultures`, `activeVersion.cultures`, `status.cultures`; I2/I3 en el diálogo; toast de publicado con idiomas; vista oficial limitada |
| `0481ef0` | 10-19 | fix(formularios): pluralizar temas y pedidos por separado | Es de H7 (Formularios que faltan, sección 3), no de idioma |
| `47546f6` | 10-19 | feat(formularios): previsualizar el idioma completo del formulario | `FormPreview` en una sola cultura; marcas neutras «Sin traducir» |
| `f93c7d4` | 10-19 | test(formularios): preparar escenas de idiomas y publicación | `formLanguages.spec.ts` y ajustes de `forms.spec.ts` |
| `638fd19` | 10-19 | docs(formularios): consolidar la traducción opcional vigente | `docs/architecture.md`, `visual-baseline.md`, AGENTS |
| `9a515e3` | 10-19 | test(formularios): preparar contratos de culturas publicadas | Consumidor de las 18 capturas de idioma |
| `c4a13fd` | 10-19 | test(formularios): consumir el PDF inglés con ítem original | PDF y workflow ingleses con un ítem de catálogo en su original |
| `9fc142a` | 10-19 | fix(formularios): mostrar el contador de idiomas en móvil | `SegmentedControl wrapLabels` |
| `0eb600c` | 10-19 | docs(formularios): registrar el recorrido de idiomas en navegador | Doce JPEG de evidencia a 390/320 |
| `086d6ae` | 10-19 | test(formularios): renovar los contratos HTTP de idiomas | Export conjunto renovado |
| `bf626a0` | 10-19 | test(formularios): habilitar publicación en las escenas de idiomas | `src/test/visual/fixtures.ts`: permiso publish en `forms-languages-*` |
| `3d97ccd` | 10-19 | docs(formularios): registrar el cierre local de idiomas | Cierre en el diseño |
| `208951c` | 10-19 | fix(whatsapp): admitir formularios publicados en un solo idioma | Punto 1 del review del 2026-10-19 |
| `7d97c5b` | 10-19 | fix(formularios): ajustar las etiquetas y la vista orientativa | Punto 6: etiquetas «Español · original», «English · 3 sin traducir»; vista previa «orientativa» |
| `2d840c2` | 10-19 | fix(formularios): unificar los avisos de publicación | Punto 7: un aviso por idioma, hasta tres faltantes, «y N más», estado publicado estático |
| `be0824e` | 10-19 | fix(ui): conservar el ancho del texto en avisos con acción | `Banner` con `action` que baja si no entra (lo usa I2) |
| `0cad264` | 10-19 | test(whatsapp): consumir formularios con traducción opcional | Consumidor de 8 capturas de idioma original del chat |
| `b1a5b90` | 10-19 | test(whatsapp): registrar los contratos reales de idioma original | Las 8 capturas `original-only-*` y `empty-secondary-*` |
| `44e464b` | 10-19 | test(contratos): renovar las cohortes del cierre de formularios | Export conjunto renovado |

### 4.2 Navegación y rutas

No hay rutas nuevas. I1–I3 viven en pantallas que ya existían:

| Ruta | Pantalla | Permiso de la ruta | Qué parte del idioma vive ahí |
| --- | --- | --- | --- |
| `/formularios/nuevo`, `/formularios/{formId}` | Editor del formulario (`src/features/business-forms/pages/FormEditorPage.tsx`) | `formtemplates.read` (alta además `formtemplates.manage`) | «Textos en», marcas, vista previa, diálogo de publicación I2/I3, estado P2, vista oficial P3 |
| `/formularios/{formId}/pdf` | Diseño del PDF (`src/features/business-forms/pages/FormPdfDesignPage.tsx`) | `formrules.manage` | Pares nombre/pie por idioma y vista previa por idioma (sin cambios de código desde `9261e3d`) |
| `/whatsapp/chat` | Chat (`src/features/whatsapp/pages/WhatsAppChatPage.tsx`) | `whatsapp.read` | Diálogo C2 «Enviar un formulario a {nombre}» en la cultura preparada |
| `/catalogos/nuevo`, `/catalogos/{id}` | Editor de catálogo | `formcatalogs.read` (+ manage para alta) | «Textos en» bilingüe del catálogo (contraste, 4.9) |

**Qué vive en la URL y qué nunca:**
- El idioma del contenido que se está editando (`contentCulture`) es **estado de React del editor**: no va a la URL, ni a
  storage, ni a QueryCache. Arranca en el idioma original del borrador (`draft.culture`) en cada montaje.
- El idioma del menú es el de i18next; lo cambia el menú de la cuenta (`src/layouts/components/UserMenu.tsx`) y se guarda
  en el perfil (`src/auth/useLanguagePreference.ts`). No es parte de ninguna ruta de formularios.
- La cultura pedida a la vista oficial va como `?culture=` en el GET al servidor, pero la URL firmada que vuelve vive solo
  en el estado del diálogo (nunca en QueryCache ni storage), igual que antes.
- La cultura preparada del chat vive en la query `preparation` del compositor (clave por actor, contacto, revisión del
  destinatario, cultura de la cuenta, selección y `i18n.resolvedLanguage`), nunca en la URL.

**Migas:** sin cambios. En el editor la hoja es el nombre **en el idioma original** (`originalName`), o «Nuevo
formulario».

### 4.3 Editor del formulario: «Textos en» y marcas neutras (I1)

`src/features/business-forms/pages/FormEditorPage.tsx`, `src/features/business-forms/components/QuestionEditor.tsx`,
`src/features/business-forms/components/FormPreview.tsx`, lógica pura en `src/features/business-forms/editorModel.ts`
(`missingFormTexts`, `missingQuestionTexts`, `questionTexts`, `localizedValue`).

El original de un formulario nuevo es el **idioma del menú** al crearlo: `missingFormInitial` devuelve
`emptyForm(scope.culture)` salvo que venga una semilla de «Formularios que faltan» con su `originalCulture`
(`src/features/business-forms/missingFormSeed.ts`). El original es inmutable (`draft.culture`).

#### Cabecera (sin cambios de disposición respecto del anexo anterior)

- Título: nombre en el idioma original o «Nuevo formulario». Volver «Diseñar formularios».
- Estado (insignia): publicación («Aceptando publicación…», «Publicación sin confirmar», «Publicando · versión N»,
  «Publicado · versión N», «No se pudo publicar · versión N»), ciclo de vida (dado de baja, sección 6) o, sin
  publicación: «Guardado sin confirmar», **«Cambios sin guardar»** o «Borrador».
- Acciones a la derecha: «Descartar», «Guardar borrador», «Publicar en WhatsApp» (o «Reactivar», sección 6), menú «⋯» de
  ciclo de vida y «Ver vista oficial».

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), commit `bb98bf2`):
> la insignia muestra «Cambios sin guardar» cuando el borrador difiere de lo guardado (`t("dirty")` en
> `FormEditorPage.tsx`, línea de `Page status`). La regla dice que el estado muestra solo el estado del objeto («Activo»,
> «Dado de baja», «Publicado · versión N»), sin «Cambios sin guardar»: que «Guardar borrador» esté habilitado ya lo dice.
> «Guardado sin confirmar» es un estado del intento de guardado, no del objeto: conviene revisarlo con el mismo criterio
> (el punto 38 no lo nombra). Se conserva el aviso al salir con cambios.

#### Cuerpo, en orden (lo que cambió está marcado)

1. Línea de espera ante un 429 (`role="status"`): «Esperá N segundos para volver a intentar.»

   > **No copiar — regla de interfaz del 2026-10-23:** es un texto que explica por qué los botones quedan
   > deshabilitados, arriba del cuerpo. El punto 38 nombra «la espera» como texto al lado de los botones en reglas y
   > sugerencias; el mismo criterio aplica acá: el motivo va como tooltip del botón deshabilitado.

2. Aviso de ciclo de vida y superficie P2 (4.5), guardado sin confirmar, revisión pendiente: sin cambios de idioma.
3. **Superficie sin banda «Textos en»** (cambió en `47546f6`, `9fc142a`, `7d97c5b`):
   - Rótulo «Textos en» (en: «Content language»).
   - Control segmentado `SegmentedControl` con `wrapLabels` y dos opciones, `aria-label` «Textos en». Cada opción dice
     el idioma **en su propio idioma** (`i18n.getFixedT(language, "forms")("cultures." + language)`: «Español» y
     «English», también con la interfaz en inglés) y después:
     - el original: «· original» (antes «· Original»; el tablero I1 decía «original · obligatorio» y así quedó la
       evidencia del 2026-10-19 a las 11:43, anterior a `7d97c5b`);
     - el otro: «· N sin traducir» en gris si falta algo, o «· completo» (en: «N untranslated», «complete»). Ya no dice
       «opcional» ni «pendientes».
   - Debajo, en gris: «El original (español) es obligatorio; el otro idioma es opcional. Mientras le falte algo, el
     formulario sale entero en el original.» (antes: «Los tipos, el orden, las opciones y la obligatoriedad son
     compartidos por los dos idiomas.»).
   - Elegir una opción cambia **solo** `contentCulture`: no cambia el idioma de la aplicación ni guarda nada.
   - Cuenta faltantes con `missingFormTexts(value, culture)`: nombre, botón final y, por pregunta, etiqueta, opciones
     propias (no las de catálogo) y ayuda **solo si la ayuda existe en algún idioma** (`questionTexts`). Un texto con solo
     espacios cuenta como faltante.

   > **No copiar — regla de idioma del 2026-10-23** (review E2, punto 35): el punto 35 saca «Textos en» de
   > `FormEditorPage`. Para traducir se cambia el idioma del menú. El conteo «N sin traducir» y su texto explicativo
   > dejan de tener lugar en este selector; qué reemplaza al conteo no está decidido (ver 4.10).

4. **Superficie «El formulario»** (dos columnas desde `sm`, una en celular):
   - «Nombre» (máx. 120) y «Texto del botón final». En el idioma no original: el campo vacío muestra el placeholder
     **«Sin traducir»** (antes «Falta en inglés») y la ayuda «Original (Español): {texto}» (en: «Original (Spanish):
     …»). El texto del original nunca se copia al otro idioma.
   - «Prefijo de numeración»: con candado y solo lectura en el idioma no original, con la ayuda «El prefijo es
     compartido. Editalo desde el idioma original.»
   - «Cuándo ofrecerlo» (`offerWhen`, Etapa 5, sección 3): un solo texto, no es por idioma.
   - Pie de la superficie: «Los textos se editan por idioma y no se traducen automáticamente.»

   > **No copiar — regla de idioma del 2026-10-23:** el bloqueo del prefijo «desde el idioma original» y el pie «Los
   > textos se editan por idioma» son consecuencia del selector. Con la regla nueva, «un campo sin traducción muestra el
   > original como referencia, sin copiarlo» (eso sí se conserva: placeholder y ayuda «Original (…)»). Qué pasa con el
   > prefijo cuando el menú está en el idioma no original no está decidido (no verificado en el review).

5. **Superficie «Preguntas»**, fila de cada pregunta (`QuestionEditor`):
   - Segunda línea (tipo · obligatoria): en el idioma no original suma «· N sin traducir» si a la pregunta le faltan
     textos. En el original **no** se cuenta nada (antes contaba en los dos).
   - Insignia gris neutra **«Sin traducir»** si falta la etiqueta en el idioma mostrado (no original). Sin color de alerta.
   - La etiqueta faltante se ve en cursiva con el texto de respaldo.
   - Si la comprobación de publicación marcó la pregunta: insignia ámbar «Revisá esta pregunta · {Español|Inglés}».
   - Desplegada: «Pregunta» (placeholder «Sin traducir» solo en el no original; ayuda «Original (Español): …»),
     «Ayuda para el empleado» (placeholder «Sin traducir» solo si el original tiene ayuda; si no, ayuda «Opcional»),
     cada «Opción N» con el mismo placeholder y referencia. Los mapas `{es,en}` se conservan completos; borrar una ayuda
     manda la clave con `null` (sin cambios respecto del anexo anterior).
6. **Columna derecha: vista previa** (`FormPreview`, reescrita en `47546f6` y `7d97c5b`):
   - Encabezado chico: **«Vista previa para una cuenta en {inglés|español}»** (el idioma elegido en «Textos en»; en:
     «Preview for an account using English»).
   - Si falta algo en ese idioma: aviso info (`Banner`) **«Se muestra en español: faltan 3 textos en inglés.»** (en:
     «Shown in Spanish: 3 texts are untranslated in English.»). Si no: «Vista previa en {idioma}.» en gris.
   - Con varias pantallas, «Pantalla X de Y» en el idioma de la vista previa.
   - El teléfono usa **una sola cultura para todo** (`previewCulture`): el original entero mientras falte un texto del
     idioma pedido; el pedido entero cuando está completo. Incluye los textos de plataforma («Sí»/«No», «Elegí una opción»,
     «Continuar», «Pantalla anterior», el botón final, «Esta vista no envía ni guarda respuestas.»), porque se resuelven
     con `useTranslation("forms", { lng: previewCulture })`. El contenedor lleva `lang={previewCulture}`.
   - Ya no hay marcas «· Traducción pendiente» ni «N opciones muestran el original: falta la traducción.»
   - Preguntas de catálogo: la vista previa por persona de E2 (secciones 9 y 10) solo se usa si su cultura coincide con
     `previewCulture`; si no, «Las opciones se preparan según quien lo recibe, al mandar el formulario.»
   - Debajo del teléfono: «Cada pregunta de foto o documento abre su propia pantalla…» (si hay varias), **«La vista
     previa usa un solo idioma.»** y «Vista previa orientativa. La vista oficial de WhatsApp estará disponible al
     publicar.»
   - El review del 2026-10-19 (punto 6) pidió no decir «El formulario llega…», porque los avisos y el PDF pueden cambiar
     el idioma del envío: por eso los textos son «Vista previa…» / «Se muestra en…», no «Le llega…».

   > **No copiar — regla de idioma del 2026-10-23:** hoy la vista previa sigue al selector «Textos en». Sin selector, el
   > idioma de la vista previa tendría que salir del idioma del menú («se muestra en el idioma de la pantalla y, si
   > falta, en el original»). Es una inferencia: el review no lo dice explícitamente.

7. Secciones «Qué sugerir después» y «Cuando llega una respuesta» (Etapa 5 y Etapa 4): reciben `contentCulture` del
   editor. Las etiquetas de preguntas y opciones se muestran con `localizedValue(texto, contentCulture, original)` y el
   mensaje de cada acción de una regla se edita en `contentCulture`. Con la regla nueva pasan a seguir el idioma del menú.

#### Validación, foco y «Completar»

- Un 400 al guardar con rutas `name.es|en`, `draft.submitLabel.es|en`, `….label|help.es|en`, etc.: el editor toma el
  idioma del **primer** error visible (`/\.(es|en)$/`), **cambia «Textos en» a ese idioma**, despliega la pregunta y
  enfoca el primer `aria-invalid`. Un error que el editor no puede ubicar da el toast «No pudimos guardar un dato que este
  editor no puede corregir.»
- Desde la comprobación de publicación o desde P2, «Ir a la pregunta», «Ir al texto» y «Completar el inglés» llaman a
  `goToPublicationIssue`: cierran el diálogo, cambian `contentCulture` a `issue.culture`, despliegan la pregunta por
  `FieldId` y enfocan el control exacto (opción por `ChoiceId`, ayuda, selección, catálogo o etiqueta; el nombre y el
  botón final por `data-form-text`).

> **No copiar — regla de idioma del 2026-10-23:** «cambiar al idioma del error» depende del selector. La regla nueva pide
> que un error en un campo que no está a la vista se vea: sin selector, si el error es del otro idioma, se muestra en la
> página (no hay pestaña a la que cambiar). Lo pide el punto 35 para catálogos; para formularios el review no lo detalla.

#### Estados

Sin cambios respecto del anexo anterior: carga, 404 («no encontrado» con «Reintentar»), 403 (`ForbiddenPage`), 409
(revisión D con «Revisar»), 429 (línea de espera y toast warning), red/5xx (guardado sin confirmar con «Consultar
guardado»). I1 no agrega estados: una traducción incompleta **no** es un error ni un aviso.

#### Permisos

- Ver el editor y cambiar «Textos en»: `formtemplates.read` (en solo lectura los campos quedan `readOnly`, el selector
  sigue funcionando).
- Escribir traducciones: `formtemplates.manage`.
- Publicar el original sin traducción: `formtemplates.read` + `formtemplates.publish` y `canPublish` del servidor, **sin
  manage**; en ese caso no se ofrece «Completar el inglés» (test «allows the original with Read and Publish without
  offering translation editing»).

#### Cómo se ve a 390 y 320 px

- La grilla de dos columnas existe recién desde `lg` (1024 px): en celular la vista previa queda **debajo** de las
  preguntas. «El formulario» pasa a una columna debajo de `sm` (640 px).
- La cabecera parte en varias filas: título, insignia y debajo los botones alineados a la derecha, en filas
  («Descartar» y «Guardar borrador» en una, «Publicar en WhatsApp» en otra, «Ver vista oficial» en otra) —
  [I1 en español a 320](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idioma-20261019-i1-es-320.jpg).
- El segmentado «Textos en» usa `wrapLabels` (`9fc142a`): cada opción puede ocupar dos o tres renglones dentro del ancho
  disponible, con alto mínimo de control, para que el conteo no se corte. `forms.spec.ts` mide que el grupo no se salga
  del contenedor y que ningún texto desborde su botón a 1440/390/320. El review del 2026-10-19 (punto 6) pidió quitar
  `wrapLabels` si a 320 no hacía falta; el cierre lo conservó porque a 320 las etiquetas van en dos renglones.
- Evidencia del recorrido en navegador (`0eb600c`, JPEG sin convertir; **anteriores a `7d97c5b` y `2d840c2`**: muestran
  «original · obligatorio», «opcional · 3 sin traducir» y dos avisos separados en I3):
  [I1 es 320](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idioma-20261019-i1-es-320.jpg),
  [I1 es 390](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idioma-20261019-i1-es-390.jpg),
  [I1 en 320](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idioma-20261019-i1-en-320.jpg),
  [I1 completo es 320](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idioma-20261019-i1-complete-es-320.jpg),
  [vista previa en 390](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idioma-20261019-i1-preview-en-390.jpg),
  [vista previa es 390](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idioma-20261019-i1-preview-es-390.jpg).

### 4.4 Comprobación y confirmación de idiomas al publicar (I2 e I3)

`src/features/business-forms/components/PublicationCheckDialog.tsx`, hook
`src/features/business-forms/hooks/useFormPublicationCheck.ts`. Se abre con «Publicar en WhatsApp» (o «Reactivar»,
sección 6). Diálogo central `sm:max-w-2xl`; solo la variante de reactivación usa `mobileFullscreen`.

#### Título, descripción y pie

- Título: «Primero guardá los cambios» (con cambios sin guardar), «Publicar en WhatsApp» (comprobando) o «Publicar la
  versión N en WhatsApp» (`nextVersionNumber` del chequeo). Reactivar: «Reactivar «{nombre}»».
- Descripción: «Se publica sólo lo guardado. Guardá el borrador antes de comprobarlo.» o «Revisamos el borrador guardado
  antes de publicar. Esta comprobación no publica ni guarda cambios.»
- Pie: a la izquierda «Se publica el borrador guardado el {fecha}.»; a la derecha «Cancelar» y, según el caso,
  «Guardar borrador» (con cambios) o «Publicar versión N» / «Reactivar con la versión N» (deshabilitado sin `canAccept` o
  vencido) y «Volver a comprobar» si venció.

> **No copiar — regla de interfaz del 2026-10-23:** el pie tiene un texto al costado de los botones («Se publica el
> borrador guardado el …») y los botones cambian de forma según el estado (aparece «Volver a comprobar», «Guardar
> borrador» reemplaza a «Publicar versión N»); además «La comprobación venció. Volvé a comprobar antes de publicar.» y
> «La publicación todavía no está disponible…» son textos que explican un botón deshabilitado. La regla pide botones
> fijos, habilitados o deshabilitados, con el motivo como tooltip. El punto 38 no nombra este diálogo; es un caso a
> revisar con la misma regla.

#### Cuerpo, en orden (estado con resultado)

1. (Solo reactivación) superficie con la numeración que sigue (sección 6).
2. **Superficie «Qué incluye»** (primera publicación) o **«Qué cambia respecto de la versión N»** («…de la versión
   publicada» si la base no coincide): «Agregadas: …», «Cambiadas: …», «N preguntas quitadas», «N preguntas», la lista de
   nombres y, nuevo en `17f2869`:
   - en negrita **«En WhatsApp, en español»** o **«En WhatsApp, en español y en inglés»** (idiomas de
     `channels[].cultures`, unidos con « y en »);
   - debajo, en gris: «Le llega en español a todas las cuentas.» (un idioma) o «Se publican los dos idiomas. Cada cuenta
     recibe todo en su idioma si también están completos sus avisos y PDF; si no, en el original.» (dos).
   - Se retiró «En español y en inglés. Cada empleado lo recibe en el idioma de su cuenta.»
3. Si la versión suma un idioma (`diff.addedCultures`, solo cuando hay versión base): renglón «**Se suma el inglés.** Se
   publica la traducción completa con esta versión.»
4. **Un aviso por idioma** (`Banner`, unificado en `2d840c2`): por cada cultura no original incompleta o perdida
   (`diff.removedCultures`):
   - si se pierde: tono warning, «**El inglés deja de publicarse.** Desde la versión N, las cuentas en inglés lo reciben
     en español.» y, si además le faltan textos, «Le faltan N textos. Podés publicar igual y sumarlo en otra versión.»;
   - si solo está incompleto: tono info, «**El inglés no se publica.** Le faltan N textos. Podés publicar igual y sumarlo
     en otra versión.»;
   - lista de hasta **3** faltantes (`role="list"`): «Nombre», «Texto del botón final», «{pregunta} · la pregunta»,
     «{pregunta} · la ayuda», «{pregunta} · la opción «{opción}»»; si hay más, «y N más»;
   - acción del aviso (botón enlace a la derecha, o debajo si no entra): **«Completar el inglés»**, solo con
     `formtemplates.manage` y si hay un faltante ubicable. Cierra el diálogo, cambia «Textos en» y enfoca el control
     exacto, **sin** devolver el foco al disparador y sin cambiar el borrador.
   - Los faltantes del secundario **no** entran en «Problemas · frenan».
5. **«Problemas · frenan»** (encabezado `h3`, de E2 `a561037`; detalle en la sección 10): tarjetas con nombre
   («{pregunta} · Inglés», «Nombre», «Texto del botón final», «El canal de publicación», «El formulario»), motivo del
   servidor y «Ir a la pregunta» / «Ir al texto»; o «Ninguno.» con tilde verde. Solo faltantes **del original** frenan.
6. **«Avisos · no frenan»** (E2): «Como mucho N opciones por persona, de un límite de M.», «N personas no tienen el dato
   necesario para recibirlo.», o «Ninguno.».
7. Sin problemas: superficie «Qué queda fijo».
8. «La comprobación venció…», «Ya hay una publicación en curso…», «La publicación todavía no está disponible…» o
   superficie «Qué va a pasar» con tres pasos; el segundo dice ahora **«Se manda a WhatsApp, en español y en inglés.»**
   (o el idioma que corresponda) en lugar de «Se enviará a WhatsApp para comprobarla y publicarla.»

> **No copiar — review E2, punto 29** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)): los avisos de
> publicar admiten cualquier `code` y detalles opcionales (`src/features/business-forms/api/response.ts:118`,
> `publicationWarning` con `code: z.string()`) y «Como mucho {{maximum}} opciones» no tiene `_one`/`_other` (se ve «Como
> mucho 1 opciones»). Estado en `982ade8`: **pendiente** (verificado en `response.ts` y `src/locales/es/forms.json:247-248`).

#### Estados

- Con cambios sin guardar: no comprueba nada; «Tus cambios siguen en el editor.» y «Guardar borrador» en el pie.
- Comprobando: spinner «Comprobando el formulario…».
- 403: estado vacío «Ya no tenés permiso para publicar.», sin reintento.
- Otro error: estado vacío «Comprobación pendiente» + «Reintentar»; toast «No pudimos comprobar el formulario.» con
  `traceId`.
- 409: cierra el diálogo y lleva a la revisión del borrador (`onConflict`).
- 429: toast warning y cuenta regresiva «Esperá N segundos para volver a intentar.»; «Reintentar» y «Volver a
  comprobar» quedan deshabilitados mientras corre.
- Vacío: no existe (siempre hay al menos el original).

#### Cómo se ve a 390 y 320 px

- `src/test/visual/formLanguages.spec.ts` recorre cuatro estados (`original`, `complete`, `add`, `remove`) en es/en a
  390 y 320 y comprueba: sin desplazamiento horizontal de página; el diálogo dentro del ancho; en los avisos, el texto
  ocupa al menos el 60 % del ancho del aviso, la acción «Completar el inglés» queda **debajo** del texto y dentro del
  aviso, y el texto no desborda (`be0824e`); exactamente un `status` con tres ítems; Escape y «Cancelar» devuelven el
  foco a «Publicar en WhatsApp»; «Completar el inglés» deja «English» presionado y el foco en «Opción 1»; **ninguna
  escritura** (`POST`/`PUT` fuera de `publication-checks`).
- Evidencia: [I2 es 390](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idioma-20261019-i2-es-390.jpg),
  [I2 es 320](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idioma-20261019-i2-es-320.jpg),
  [I3 suma en 320](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idioma-20261019-i3-add-en-320.jpg),
  [I3 suma es 390](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idioma-20261019-i3-add-es-390.jpg),
  [I3 pierde en 320](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idioma-20261019-i3-remove-en-320.jpg),
  [I3 pierde es 390](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/idioma-20261019-i3-remove-es-390.jpg).
  Las de «pierde» son anteriores a `2d840c2`: muestran dos avisos (pérdida y faltantes) que hoy son uno.

### 4.5 Estado de la publicación con sus idiomas (P2)

`src/features/business-forms/components/PublicationProgress.tsx`, `src/features/business-forms/hooks/useFormPublication.ts`.

- Con la operación en `Published`: el renglón de estado pasa a ser **«En WhatsApp, en español y en inglés»** (idiomas de
  `status.cultures` de esa operación o, al abrir el editor, de `activeVersion.cultures`), **sin** `role="status"`: es
  estático y no se anuncia (`2d840c2`).
- Una sola vez por operación, y solo si la publicación empezó en este editor (`startedHere`, que expone
  `useFormPublication`), toast de éxito **«Publicamos la versión N, en español y en inglés.»** Abrir el editor con una
  versión ya publicada no lo dispara. También se anuncia si el POST se perdió y un GET confirmó la publicación propia.
- El resto (pasos, diagnósticos con idioma debajo, «La versión N publicada sigue activa.», «Consultar publicación»,
  «Repetir el mismo intento», espera 429) no cambió.

> **No copiar — regla de interfaz del 2026-10-23:** «Consultar publicación» y «Repetir el mismo intento» van en el cuerpo
> de la superficie, a la izquierda, y la espera («Esperá N segundos…») queda como texto debajo de esos botones. La regla
> pide las acciones de una superficie al borde derecho de su encabezado y el motivo del deshabilitado como tooltip.

### 4.6 Vista oficial limitada a las culturas publicadas (P3)

`src/features/business-forms/components/OfficialFormPreview.tsx` (`17f2869`).

- El segmentado ofrece **solo** `activeVersion.cultures` de la versión publicada. Si el formulario se publicó solo en
  español, hay una sola opción. Las etiquetas son `common:language.*` en el idioma de la interfaz («Español», «Inglés»;
  distinto del selector del editor, que nombra cada idioma en su propio idioma).
- Cultura inicial: la de «Textos en» si está publicada; si no, la primera publicada. Nunca pide una cultura ausente
  (test «only offers published %s and never requests an absent initial culture»). Si cambian las culturas (otra versión),
  se retira la elegida y se renueva con una publicada.
- Pide la vista al abrir (GET `…/versions/{versionId}/preview?culture=`, `cache: "no-store"`); cambiar de idioma aborta
  el pedido en vuelo, limpia la vista y espera «Cargar vista». Sin cambios: vencimiento que retira iframe y enlace, 403
  «Ya no tenés permiso para ver esta vista previa.» sin botón de pedir, 429 con espera, `ExpiredFormPreview`.
- Celular: diálogo predeterminado (`sm:max-w-[600px]`). La escena `src/test/visual/formPublicationReads.spec.ts` («official
  preview explicit culture and expiry») la recorre por idioma y ancho con una vista simulada.

> **No copiar — regla de idioma del 2026-10-23** (punto 35): se saca «Textos en» de `OfficialFormPreview`. La vista
> oficial se mostraría en el idioma del menú si está publicado y, si no, en el original (inferido de la regla; no
> verificado en el review).

### 4.7 Diseño del PDF y copia fija: idioma

Sin cambios de código de idioma en `FormPdfDesignPage.tsx` ni `FormPdfPreview.tsx` desde `9261e3d` (los commits que los
tocaron son de visor PDF y avisos: `4ef7825`, `a9848c0`, `07ef34f`, `1610f9e`). Se describe el estado vigente porque la
regla nueva lo cambia:

- Superficie «El comprobante» (título `pdf.receipt`): «Nombre del PDF» (obligatorio, en el idioma original, 160) y al lado
  **«En Inglés»** / «En Español» (`pdf.translation` con `cultures.*`, que lleva mayúscula) con «Opcional. Si falta una
  traducción, el PDF sale entero en el idioma original.»; «Texto al pie» («Opcional. Si lo agregás, completá el idioma
  original.») y **«Texto al pie en Inglés»**.
- Superficie «Vista previa» con un segmentado de idioma en la banda (`aria-label` «Idioma de la vista previa») con
  **«Español» / «English» fijos en el código** (observación 7 del anexo anterior, sigue igual en `982ade8`). La vista
  previa dice «Contenido en {idioma}» o, si el pedido estaba incompleto, «No está completo {pedido}. La vista previa usa
  todo el contenido en {real}.», según el `Content-Language` real de la respuesta.
- Estado de la cabecera: «{nombre} · diseño del PDF» + «· Cambios sin guardar»; debajo de los campos, «Revisión N ·
  {fecha}» o «Todavía no se guardó una revisión de este diseño.»
- Copia fija de una respuesta (DC1/D2, sección 5): `FormPdfPreview` muestra «Contenido en {idioma}» desde
  `Content-Language`; `c4a13fd` agrega el contrato de un PDF **en inglés** cuando solo un ítem de catálogo cae a su
  original (el resto del recorrido sigue en inglés; la excepción individual de ítems está en la regla del back).

> **No copiar — regla de idioma del 2026-10-23** (punto 35): los pares «Nombre del PDF» / «En Inglés» y «Texto al pie» /
> «Texto al pie en Inglés» pasan a un solo campo en el idioma del menú, y se saca el segmentado de idioma de la vista
> previa (el punto lo nombra como `FormPdfPreview`; el control vive en `FormPdfDesignPage`).
>
> **No copiar — regla de interfaz del 2026-10-23** (punto 38, `FormPdfDesignPage`): «· Cambios sin guardar» en la
> cabecera y «Revisión N · {fecha}» junto al guardado.

### 4.8 Chat de WhatsApp: mandar formularios con traducción opcional (C2)

`src/features/whatsapp/components/FormComposer.tsx`, `src/features/whatsapp/hooks/useFormComposer.ts`,
`src/features/whatsapp/lib/formSendSession.ts`, `src/features/whatsapp/api/forms.ts`,
`src/features/whatsapp/api/formMetadata.ts`, `src/features/whatsapp/components/FormCatalogPreparation.tsx`.

**Qué cambió en el diálogo «Enviar un formulario a {nombre}»:**

- **Lista de formularios:** antes de preparar, cada tarjeta muestra el nombre en el idioma de la cuenta del destinatario
  y, si falta o está vacío, **el del otro idioma** (`formOptionName`, `208951c`). Eso no decide el idioma del envío.
- **Toda selección consulta `preparation`** antes de habilitar «Enviar», aunque el formulario no tenga catálogos
  (`dbd7550`). Mientras tanto, sin catálogos: «Preparando el formulario…»; con catálogos, el texto de carga de catálogos
  de siempre. Si falla: «No pudimos preparar el formulario.» + «Reintentar» (sin catálogos) o el texto de catálogos; un
  409 da «Cambió la cuenta…» o «formulario no disponible» como antes. Se bloquea **solo** el envío del formulario; el
  chat, el compositor de texto y sus borradores siguen montados.
- **La cultura que se presenta** es `preparation.culture`: el idioma completo confirmado por el servidor para ese
  formulario y esa cuenta (el de la cuenta si la versión lo tiene publicado y completos sus avisos y PDF; si no, el
  original). Con esa cultura se muestran el nombre elegido, la indicación **«Le llega en {español|inglés}.»** (antes «Le
  llega en {idioma}, el idioma de su cuenta.»), el botón «Abrir formulario» y el mensaje predeterminado. La web no decide
  el respaldo.
- **Mensaje predeterminado:** «Completá este formulario.» (antes «Completá este formulario, por favor.»), en la cultura
  preparada. Si el operador **no lo editó**, el intento congela `message: null` y el servidor elige su texto
  predeterminado en la cultura vigente al aceptar; el recibo solo exige un `body` no vacío. Si lo editó
  (`messageEdited`), se manda explícito y el recibo debe coincidir con su `trim()`.
- `recipientCulture` del borrador sigue siendo **la de la cuenta** (para detectar «cambió el contexto del destinatario»);
  `recipientContextRevision` de `preparation` debe coincidir con el del borrador o se bloquea.
- Los recursos i18n de la cultura presentada tienen que estar cargados (`englishReady`/`spanishReady`); hasta entonces se
  bloquea solo «Enviar» y el mensaje predeterminado espera.
- **Recibo e historial:** `name` y `submitLabel` aceptan el secundario ausente o vacío, con al menos un texto no blanco
  y solo claves `es`/`en` (`formTextSchema`); la metadata sellada del historial (`formMetadataSchema`) exige un nombre no
  blanco **en su cultura efectiva** (`culture`). La tarjeta del chat muestra el formulario en esa cultura.
- H2 (sugerencia del asistente, sección 3): «Revisar y enviar» muestra `proposal.culture`.

Celular: el diálogo C2 no cambió de disposición (anexo anterior, E1.4). Sin evidencia nueva a 390/320 propia de idioma en
el chat (no verificado en capturas; los tests son de jsdom).

### 4.9 Catálogos, campos y datos de las personas: cómo está el idioma hoy (contraste)

Solo para fijar el contraste con la regla nueva; las pantallas se describen en las secciones 8–10.

| Pieza | Comportamiento en `982ade8` | Archivo |
| --- | --- | --- |
| Alta y edición de un catálogo (Manual y Filtros) | Superficie «Textos en» con segmentado **«Español» / «Inglés»** (nombres en el idioma de la interfaz, sin conteo) y debajo «Completá el nombre y la descripción en español e inglés. En las opciones, el inglés es opcional.» Nombre y Descripción con asterisco en **los dos idiomas** | `src/features/business-forms/components/CatalogMetadataFields.tsx` |
| Catálogo propuesto por el asistente (H4) | Única excepción: con `originalCulture`, «El original ({idioma}) es obligatorio; el otro idioma es opcional.» y asterisco solo en el original | idem |
| Idioma con que abre el editor de catálogo | Siempre **«es»** (`useState<FormCulture>("es")`), sea cual sea el idioma del menú. Ante un 400 con `name.en`/`description.en` cambia a «en»; con errores de una opción, al idioma de la clave con error | `src/features/business-forms/pages/CatalogEditorPage.tsx` |
| Texto de una opción | Obligatorio **en español** (`culture === "es" && !text.trim()` → `text.es` requerido); el código sugerido se arma con `label.es` | `src/features/business-forms/catalogEditing.ts` |
| Nombre de un campo del catálogo (E2) | Par «Nombre» (español, obligatorio) + «Nombre en inglés» (opcional); desde `b12a297` no manda un inglés en blanco (punto 3 del review E2) | `src/features/business-forms/components/CatalogFieldEditor.tsx`, `src/features/business-forms/catalogFields.ts` |
| Dato de las personas (E2) | Par «Nombre» (español, obligatorio; «Guardar» deshabilitado sin él) + «Nombre en inglés»; omite el inglés en blanco | `src/features/business-forms/components/PersonAttributeDialog.tsx` |
| Valor de un campo de catálogo | La etiqueta cae a `text.es` si falta la del idioma mostrado | `src/features/business-forms/components/CatalogExtraFieldInput.tsx` |
| Origen y tipo de valor | Campos de solo lectura con «¿De dónde salen las opciones? No se puede cambiar después…» y «Se elige al crear el catálogo y después no se puede cambiar.» (punto 39) | `CatalogMetadataFields.tsx` |

> **No copiar — regla de idioma del 2026-10-23** (punto 35): todo lo de esta tabla exige español, los dos idiomas o cae
> a español. Con la regla nueva: nombre y descripción del catálogo solo en el idioma del menú (y la descripción pasa a ser
> opcional, punto 39), campos y datos de las personas con un solo campo «Nombre», el texto de las opciones obligatorio
> solo en el original, y sin selector. El punto 35 describe además el síntoma de hoy: al crear un catálogo solo en
> español, el 400 trae `name.en` y `description.en` y «Guardar» parece no hacer nada. El código de `982ade8` sí cambia el
> segmentado a «en» cuando el 400 trae esas claves (`CatalogEditorPage.tsx`, bloque `handledValidation`), pero no marca
> la pestaña con error; la discrepancia con el síntoma del review **no se pudo verificar en ejecución**.
>
> **Estado en el back:** `58fb4e8e` (2026-10-23 19:50) agrega `OriginalCulture` inmutable a catálogos y datos de las
> personas, fijado al crear con la cultura del pedido (el idioma del menú; H4, el de la conversación), exige el nombre
> solo en ese idioma y descarta textos en blanco, **sin cambiar todavía contratos ni validadores**. La regla quedó
> anotada en [la regla del back](../../features/formularios.md#idioma-original-y-traducción-opcional--2026-10-19)
> («Cada catálogo y cada dato de las personas guarda su idioma original…»), que todavía dice que «el alta común sigue
> siendo bilingüe». En el front (`982ade8`) no hay cambios ni notas: `docs/design/formularios-whatsapp.md` y
> `src/features/business-forms/pages/AGENTS.md` siguen describiendo «Textos en».

### 4.10 Lo que cambia con la regla del 2026-10-23

Fuente: punto 35 del [review E2](../../reviews/2026-10-23-code-review-catalogos-e2.md). Lo marcado «inferido» no está
escrito en el review.

| Pieza implementada en I1–I3 | Con la regla nueva | Estado en `982ade8` |
| --- | --- | --- |
| Original obligatorio, inmutable, fijado al crear con el idioma del menú (`emptyForm(scope.culture)`) | **Queda** | Ya era así en formularios |
| Traducción opcional, sin autotraducción, sin relleno al guardar; mapas `{es,en}` y vaciados con `null` | **Queda** | Vigente |
| Selector «Textos en» en el editor de formularios | **Se retira**: se edita en el idioma del menú; para traducir se cambia el idioma del menú | Pendiente |
| Selector «Textos en» en catálogos (`CatalogMetadataFields`) | **Se retira**; además el alta deja de ser bilingüe | Pendiente (back: modelo listo en `58fb4e8e`, contrato y validadores no) |
| Segmentado de la vista oficial (`OfficialFormPreview`) y de la vista previa del PDF | **Se retira**; se muestra en el idioma de la pantalla o en el original (inferido) | Pendiente |
| Pares de campos por idioma (PDF, nombre de campo, dato de las personas) | **Un solo campo** en el idioma del menú | Pendiente |
| Placeholder «Sin traducir» y ayuda «Original (Español): …» cuando el menú no está en el original | **Queda**: «un campo sin traducción muestra el original como referencia, sin copiarlo» | Vigente |
| Conteo «N sin traducir» del selector e insignia «Sin traducir» por pregunta | Sin selector, el conteo pierde su lugar; la insignia por pregunta podría quedar con el menú en el otro idioma (inferido; no decidido) | — |
| Vista previa entera en una cultura (`missingFormTexts`) | **Queda** la regla de no mezclar; la cultura pedida pasaría a ser la del menú (inferido) | Vigente con el selector |
| Comprobación por idioma (I2/I3), `cultures`, `addedCultures`/`removedCultures` | **Queda**: «la comprobación de publicación sigue avisando lo que falte del original»; I2/I3 siguen informando el secundario | Vigente |
| «Completar el inglés» (cambia el selector y enfoca) | **No decidido**: sin selector habría que cambiar el idioma del menú; el review no lo resuelve | — |
| Error de guardado en el otro idioma: el editor cambia de pestaña y enfoca | Sin pestañas: el error tiene que verse en la página | Pendiente |
| Prefijo editable solo desde el original | **No decidido** (el prefijo no es un texto por idioma) | — |
| Envío del chat en la cultura preparada por el servidor; `message:null` | **Queda** (no depende del editor) | Vigente |
| Mensajes de reglas y textos de sugerencias editados en `contentCulture` | Pasan a editarse en el idioma del menú | Pendiente |

**Riesgo concreto para «traducir cambiando el idioma del menú»** (verificado en código, no probado en ejecución): el
editor de formularios se monta con `key={`${actorId}:${culture}:${formId}`}` (`FormEditorPage.tsx`) y el de catálogos con
una clave que también incluye `scope.culture` (`CatalogEditorPage.tsx`); `useFormDraftEditor` limpia su estado al cambiar
de cultura. El menú de idioma (`UserMenu.tsx` → `changeLanguage`) **no pasa por la guarda de cambios sin guardar**
(`useUnsavedChangesGuard` solo frena navegación del router y `beforeunload`). Hoy, cambiar el idioma del menú con un
borrador sin guardar **lo descarta sin preguntar**. Con la regla nueva ese gesto pasa a ser el flujo normal para
traducir: hay que guardar antes o conservar el borrador al cambiar de idioma. Ni el review ni el plan lo mencionan.

### 4.11 Hooks y estado

- **`contentCulture`** (estado local de `FormEditor`): inicia en `draft.culture`; lo cambian el selector, un error de
  validación y `goToPublicationIssue`. No se persiste. `FormEditor` llama a `useTranslation("forms", { lng: "en" })` y
  `{ lng: "es" }` para tener cargados los dos recursos (los textos de plataforma de la vista previa y los nombres de
  idioma en su propio idioma).
- **Remontaje por cultura de la interfaz:** la clave del editor incluye la cultura de la UI; la lectura del detalle
  (`useFormQueries`, claves por actor, cultura y permisos) se repite al cambiar de idioma.
- **`useFormPublicationCheck`**: el chequeo pertenece al diálogo; sin QueryCache, sin MutationCache y sin reintentos;
  aborta al desmontar o al cambiar actor/cultura/permisos (sello `[actorId, culture, permisos]`); descarta resultados de
  otro dueño. Exige Read + Publish.
- **`useFormPublication`**: suma `startedHere` (hay un intento propio en este editor) para decidir el toast de publicado;
  `PublicationProgress` guarda en un `ref` los `operationId` ya anunciados. Al llegar a `Published` o `Failed` el editor
  relee el detalle e invalida `["form-overview", owner]` si la publicación empezó acá.
- **`OfficialFormPreview`**: estado local; `AbortController` por pedido; generación para descartar respuestas tardías;
  `waitUntil` por 429; `denied` por 403.
- **WhatsApp:** la query `preparation` tiene clave `[...chatKey, actor, "forms", contactId, "preparation",
  recipientContextRevision, recipientCulture, definitionId, versionId, i18n.resolvedLanguage]`; al desmontar sin
  observadores se cancela y se retira. `messageEdited` separa la apertura escrita por el operador; el intento congelado
  (`freezeFormMessage`) vive fuera de MutationCache, URL y storage.

### 4.12 Clientes HTTP y guards zod

| Método y ruta | Cliente | Guard | Qué cambió |
| --- | --- | --- | --- |
| `GET /api/forms/definitions/{id}` | `src/features/business-forms/api/forms.ts` | `readFormDetail` en `api/response.ts` (`strictObject`) | `publication.activeVersion.cultures`: array de `es|en`, **mínimo 1, obligatorio** |
| `POST /api/forms/definitions/{id}/publication-checks` | `checkFormPublication` | `readFormPublicationCheck` | `cultures[]`: `{ culture, isOriginal, isComplete, missing[] }` (strict); `diff.addedCultures` y `diff.removedCultures`; los `missing` deben apuntar a preguntas y opciones **del cuerpo enviado** (si no, `UnsupportedFormResponse`); `warnings[]` (E2) |
| `GET /api/forms/definitions/{id}/publications/{requestId}` | estado de la publicación | `readFormPublicationStatus` | `cultures` (array) obligatorio: las capturadas por esa operación, también en Accepted/Publishing |
| `GET /api/forms/definitions/{id}/versions/{versionId}/preview?culture=` | `fetchFormPreview` | `readFormPreview` | Sin cambios; ahora solo se pide con culturas publicadas |
| `GET /api/whatsapp/chats/{id}/forms?…` | `src/features/whatsapp/api/forms.ts` | `formsSchema` | `name`/`submitLabel` con `formTextSchema` (secundario ausente o vacío permitido); ya no exige texto en la cultura de la cuenta |
| `GET /api/whatsapp/chats/{id}/forms/{definitionId}/versions/{versionId}/preparation` | idem | `preparationSchema` strict | `{ culture, recipientContextRevision, preparation: { catalogs, issues, canSend } }`, con `canSend === (issues.length === 0)` y `fieldId` sin repetir |
| `POST /api/whatsapp/chats/{id}/form-messages` | idem | `parseFormOutgoing` | `message` puede ser `null`; con `null` el recibo debe traer un `body` no vacío |
| Metadata de formulario en mensajes e historial | `src/features/whatsapp/api/formMetadata.ts` | `formMetadataSchema` | Nombre no blanco en su `culture` efectiva |
| `GET /api/forms/records/{id}/documents/{documentId}/content` | PDF fijo | Exige `application/pdf` y `Content-Language` es/en | Sin cambios; contrato nuevo con PDF inglés e ítem en su original |

### 4.13 Cohortes y fixtures exportadas de esta etapa

(El mecanismo general, `src/test/httpCohort.ts`, está en la sección 11.)

| Carpeta | Cohorte / variable | Productor del back | Cuerpos de idioma | Consumidor |
| --- | --- | --- | --- | --- |
| `src/test/fixtures/forms-http/languages/{es,en}/v{1,2,3}-{check,status,detail}.json` | `forms` / `FORMS_HTTP_CONTRACT_INPUT` | `FormPublicationLanguageTests` | **18**: por cada original (es y en), v1 solo original → v2 los dos → v3 pierde el secundario | `src/features/business-forms/api/httpContract.test.ts` («reads sealed language additions and removals with … as the original from actual HTTP»): `nextVersionNumber`, sin `issues`, culturas de canales, `isComplete` del secundario solo en v2, `addedCultures`/`removedCultures` y `status.cultures === activeVersion.cultures` |
| `src/test/fixtures/whatsapp-forms-http/en/{original-only,empty-secondary}-{choices,preparation,receipt,history}.json` | `whatsappForms` / `WHATSAPP_FORMS_HTTP_CONTRACT_INPUT` | `WhatsAppFormHttpContractTests` y otros de WhatsApp | **8**: cuenta en inglés, formulario solo en español (sin `en` o con `en: ""`); `preparation.culture: "es"`; recibo con `body` «Completá este formulario.» | `src/features/whatsapp/api/httpContract.test.ts` |
| `src/test/fixtures/forms-workflow-http/e2e-catalog-original-item-en.json` y `src/test/fixtures/forms-pdf-http/e2e-catalog-original-item-en.{pdf,http.json}` | `workflow` / `FORMS_WORKFLOW_HTTP_CONTRACT_INPUT`, `pdf` / `FORMS_PDF_HTTP_CONTRACT_INPUT` | `FormWorkflowEndToEndHttpTests`, `FormPdfHttpTests` y otros | 1 workflow + 1 PDF con companion | `workflowHttpContract.test.ts` (aprobada, 4 acciones completas, documentos es y en, nombre «Final receipt-….pdf») y `pdfHttpContract.test.ts` (descarga inglesa, mismo tamaño y SHA256) |

Estado de las cohortes en `982ade8` (misma corrida `20261023-064235-5542f7f13d844d5985f55c4142c9385f`, back
`7e5b7a94`): `forms` tiene **60** capturas (28 base + 2 de elegir varias + 12 de filtros por persona + 18 de idioma);
`whatsappForms` tiene **76**. Las renovaciones de cuerpos de esta etapa fueron `db740a2`, `086d6ae`, `b1a5b90` y
`44e464b`; ningún cuerpo se escribió a mano.

### 4.14 i18n

Namespace `forms` (`src/locales/{es,en}/forms.json`), claves de raíz:

| Clave | es | en |
| --- | --- | --- |
| `contentLanguage` | «Textos en» | «Content language» |
| `languageOriginal` | «original» | «original» |
| `languageMissing_one/_other` | «N sin traducir» | «N untranslated» |
| `languageComplete` | «completo» | «complete» |
| `contentLanguageHint` | «El original ({{language}}) es obligatorio; el otro idioma es opcional. Mientras le falte algo, el formulario sale entero en el original.» | «The original … is required; …» |
| `missingText`, `translationPending` | «Sin traducir» | «Untranslated» |
| `originalText` | «Original ({{language}}): {{text}}» | igual |
| `previewForAccount` | «Vista previa para una cuenta en {{language}}» | «Preview for an account using {{language}}» |
| `previewContentLanguage` | «Vista previa en {{language}}.» | «Preview in {{language}}.» |
| `previewFallback_one/_other` | «Se muestra en {{actual}}: falta(n) {{count}} texto(s) en {{requested}}.» | «Shown in {{actual}}: …» |
| `previewMissingHint` | «La vista previa usa un solo idioma.» | «This preview uses one language.» |
| `cultureNamesInText.es/en` | «español» / «inglés» | «Spanish» / «English» |

Retiradas: `sharedStructure`, `optionsMissing_*`, `languageOptional`, `publication.bilingual`.

Grupo `publication` nuevo: `languages` («En WhatsApp, en {{languages}}»), `languageJoin` (« y en » / « and »),
`oneLanguageHint`, `severalLanguagesHint`, `cultureNotPublished` («El {{language}} no se publica.»),
`optionalMissing_*`, `moreMissing_*` («y {{count}} más»), `completeCulture` («Completar el {{language}}»),
`missingQuestion`, `missingHelp`, `missingChoice`, `cultureAdded`, `cultureAddedHint`, `cultureRemoved`,
`cultureRemovedHint`, `publishedLanguages` («Publicamos la versión {{version}}, en {{languages}}.»), `willSendCultures`.
Los de E2 en el mismo grupo (`blockingIssues`, `nonBlockingWarnings`, `noDiagnostics`, `recipientMaximum*`,
`recipientsMissingData_*`) se describen en la sección 10.

Namespace `whatsapp`: `forms.language` («Le llega en {{language}}.»), `forms.defaultMessage` («Completá este
formulario.» / «Complete this form.»), `forms.preparing` («Preparando el formulario…»), `forms.preparationFailed` («No
pudimos preparar el formulario.»).

Observaciones: el selector del editor nombra cada idioma en su propio idioma («English» con la interfaz en español);
la vista oficial usa `common:language.*` («Inglés»); el PDF usa «Español»/«English» fijos en código; `pdf.translation`
compone «En Inglés» con mayúscula. `src/locales/parity.test.ts` exige las mismas claves en es y en.

### 4.15 Pruebas

**Unitarias (Vitest, jsdom):**

| Archivo | Qué cubre |
| --- | --- |
| `src/features/business-forms/components/FormPreview.test.tsx` | Todo en el original hasta completar el secundario y después todo en el secundario, para los dos originales; una sola cultura para todas las pantallas de archivos; la ayuda ausente en los dos idiomas no cuenta; textos con solo espacios; textos «orientativos» por idioma de interfaz |
| `src/features/business-forms/components/QuestionEditor.test.tsx` | «Sin traducir» neutro y referencia al original, sin validación |
| `src/features/business-forms/pages/FormsPage.test.tsx` | Etiquetas I1 con original inglés; publicar el original con Read+Publish sin ofrecer completar; «Completar» vuelve al control exacto sin cambiar el borrador |
| `src/features/business-forms/components/PublicationCheckDialog.test.tsx` | Publicar con secundario incompleto y completar solo con Manage; omisiones sin acción para Publish; bilingüe completo; idioma sumado; idioma perdido; un aviso unificado por idioma (es/en, dos originales); impedimentos de canal siguen frenando; interfaz en inglés sin cambiar culturas del contenido |
| `src/features/business-forms/components/PublicationProgress.test.tsx` | Toast de publicado una vez con sus idiomas; no anunciar al abrir una versión existente; anunciar si el GET confirma tras perder el POST; estado publicado estático en inglés |
| `src/features/business-forms/hooks/useFormPublication.test.ts` | Propiedad local cuando el GET confirma tras perder el POST |
| `src/features/business-forms/components/OfficialFormPreview.test.tsx` | Solo culturas publicadas; nunca pide una ausente; retira una cultura quitada al cambiar de versión |
| `src/features/business-forms/api/publicationChecks.test.ts`, `publicationRead.test.ts`, `publications.test.ts` | Guards de `cultures`, `missing` de otro snapshot rechazado, `activeVersion.cultures` obligatorio, `status.cultures` sin derivarlo del borrador |
| `src/features/business-forms/api/httpContract.test.ts`, `pdfHttpContract.test.ts`, `workflowHttpContract.test.ts` | Contratos reales (4.13) |
| `src/features/whatsapp/api/forms.test.ts` | Metadata desconocida rechazada; selector para la otra cultura con secundario ausente o vacío; recibo e historial; apertura predeterminada resuelta por el servidor |
| `src/features/whatsapp/pages/WhatsAppForms.test.tsx` | Elegir y mandar un original a la otra cultura; idioma del destinatario para mensaje y botones con interfaz en español; esperar la cultura preparada sin catálogos; conservar la apertura editada; bloquear y reintentar tras fallar la preparación |
| `src/features/whatsapp/components/ChatTimeline.test.tsx` | Tarjetas de formularios en el original en pendiente y confirmado |
| `src/features/whatsapp/components/ChatLanguageLoading.test.tsx` | Esperar recursos de presentación cuando una cuenta en español recibe un original inglés completo |
| `src/features/whatsapp/components/AssistantSuggestion.test.tsx` | Cultura de la propuesta (H2) |
| `src/features/whatsapp/lib/formSendSession.test.ts` | Sin apertura explícita si no se editó |
| `src/features/whatsapp/api/httpContract.test.ts` | Cultura preparada y revisión real; las 8 capturas de idioma original |

**Visuales (Playwright, `src/test/visual`):**
- `forms.spec.ts`: escena `forms-translations` (I1) en es/en a 1440/390/320: «English · 3 sin traducir», geometría del
  segmentado, «Original (Español): Importe cobrado», vista previa en español entero con aviso «Se muestra en español:
  faltan 3 textos en inglés.», completar los tres textos y ver «English · completo» con la vista previa entera en inglés,
  sin cambiar el `lang` de la página. Capturas `forms-translations-*`, `forms-translations-preview-*` y
  `forms-translations-complete-*` en `test-results/`.
- `formLanguages.spec.ts`: I2/I3 (4.4), 16 casos.
- `formPublicationReads.spec.ts`: vista oficial por cultura y vencimiento.
- Escenarios sintéticos en `src/test/visual/formFixtures.ts` (`forms-translations`, `forms-languages-*`) y permisos en
  `src/test/visual/fixtures.ts`.
- Cierre del 2026-10-19: visual normal 527/527 con las 127 referencias intactas; 40 capturas I1–I3 inspeccionadas (24 de
  I1 a 1440/390/320, 16 de I2/I3 a 390/320). Las capturas nuevas van a `test-results/` y se revisan contra los tableros;
  no se agregaron referencias comparadas.

### 4.16 Para el multitenant

- **Aplicar la regla del 2026-10-23 desde el principio**, no I1 tal como está: sin selectores de idioma en pantallas,
  sin pares de campos, original = idioma del menú al crear, traducción cargada con el menú en el otro idioma. Decidir
  antes qué pasa con «Completar el inglés», la vista previa, la vista oficial y el prefijo (4.10).
- **Cambiar de idioma con cambios sin guardar** tiene que preguntar o conservar el borrador: hoy el editor se remonta y
  pierde lo escrito (4.10). En multitenant el idioma del menú es por usuario; conviene que el cambio de idioma pase por la
  misma guarda que la navegación.
- **Idiomas por organización:** hoy el conjunto es fijo `es|en` en tipos, guards (`z.enum(["es","en"])`), locales y
  `SegmentedControl`. Si cada organización (B2B) habilita idiomas distintos, los guards, `missingFormTexts`, la
  comprobación por idioma y los textos «español/inglés» tienen que salir de la configuración del tenant, no de una
  enumeración.
- **B2C con personas en otros idiomas:** la cultura del envío la decide el servidor (`preparation.culture`) con la cuenta
  del destinatario y la completitud de formulario, avisos y PDF; mantener esa autoridad del servidor y que la web solo
  presente. En B2C la «cuenta» puede no existir: definir de dónde sale la cultura del destinatario.
- **Claves de caché y dueños:** incluir el tenant junto con actor, cultura y permisos en las claves de detalle,
  `preparation` y en los sellos de dueño de los hooks privados (`useFormPublicationCheck`).
- **Contratos:** reproducir las cohortes de idioma (v1 original → v2 ambos → v3 pierde; cuenta en otro idioma con
  formulario solo original; PDF con ítem en su original) como ejemplos exportados por tenant.
- **No copiar** la insignia «Cambios sin guardar», el pie con texto del diálogo de publicación, los botones de P2 en el
  cuerpo ni los segmentados de idioma escritos a mano en el PDF.

### 4.17 Endpoints nuevos o cambiados que consume esta etapa

- `GET /api/forms/definitions/{id}`: `publication.activeVersion.cultures` (nuevo, obligatorio).
- `POST /api/forms/definitions/{id}/publication-checks`: `cultures[]`, `diff.addedCultures`, `diff.removedCultures` (y
  `warnings[]` de E2).
- `GET /api/forms/definitions/{id}/publications/{requestId}`: `cultures` (nuevo).
- `GET /api/forms/definitions/{id}/versions/{versionId}/preview?culture=`: sin cambios de forma; solo culturas publicadas.
- `GET /api/whatsapp/chats/{id}/forms?…`: mapas de nombre y botón con secundario opcional.
- `GET /api/whatsapp/chats/{id}/forms/{definitionId}/versions/{versionId}/preparation`: ahora se consulta para toda
  selección; `culture` es la cultura presentada.
- `POST /api/whatsapp/chats/{id}/form-messages`: `message: null` para la apertura predeterminada.

### 4.18 Huecos

- No se ejecutó nada (ni tests ni navegador): los comportamientos salen de leer el código y los tests de `982ade8`.
- La pérdida del borrador al cambiar el idioma del menú se dedujo del código (clave del editor y ausencia de guarda en
  `UserMenu`); no se reprodujo.
- La discrepancia entre el síntoma del punto 35 (catálogo bilingüe que «parece no hacer nada») y el código que cambia el
  segmentado a «en» ante `name.en`/`description.en` no se pudo verificar en ejecución.
- Qué hacer con «Completar el inglés», el conteo «N sin traducir», el prefijo y la cultura de la vista previa y de la vista
  oficial sin selector no está decidido en el review ni en los docs: lo marcado «inferido» no es una decisión.
- No hay evidencia a 390/320 nueva del chat C2 con cultura preparada; la del editor y del diálogo es anterior a `7d97c5b`
  y `2d840c2` (etiquetas y avisos ya cambiaron).

---

## 5. Menú de formularios MF1, vista rápida D2 y detalle completo DC0/DC1

Relevado sobre el front en `982ade8` (estado commiteado; no se miró el árbol de trabajo). Lo que sigue describe lo
vigente y marca qué quedó obsoleto del anexo anterior (`anexo-b-front.md` del traspaso del 2026-10-17, apartados 1,
E3.1, E3.4 y E4.3). Las reglas funcionales del backend están en
[Gestión de formularios y detalle completo](../../features/formularios.md#gestión-de-formularios-y-detalle-completo--2026-10-21)
y en [Motor de acciones y aprobación](../../features/formularios.md#motor-de-acciones-y-aprobación--etapa-4-g5-y-g3g4);
los tableros, en [formularios-whatsapp.md](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md).

### 5.1 Resumen

Entre `9261e3d` y `982ade8` el área de respuestas cambió de forma:

1. **D2 (2026-10-17):** el detalle de una respuesta dejó de ser una página vertical (T3/G4/G5) y pasó a ser un
   **panel lateral sobre la planilla**, con momentos arriba, pestañas Respuesta/Fotos/Actividad/PDF, ↑ ↓ y Escape.
2. **MF1 (2026-10-21):** el grupo del menú pasó a llamarse «Gestión de formularios». Desaparecieron la sección
   «Respuestas» con un enlace por formulario, «Ver todos (N)» y la bandeja global «Por aprobar». En su lugar hay **una
   sola entrada «Formularios»** que abre una tabla de formularios publicados con sus pendientes, cantidad de respuestas
   y última respuesta. Lo que espera aprobación se ve **dentro de cada formulario** («Por aprobar · N» arriba de la
   planilla).
3. **DC0/DC1 (2026-10-21):** D2 quedó como **vista rápida de escritorio** y se sumó una **pantalla completa** con
   dirección propia (`…/completa`), compartible, con secciones plegables Respuesta y Actividad y el PDF pegado a la
   acción que lo generó. En el celular no hay vista rápida para quien lee respuestas: tocar una fila abre la pantalla
   completa.
4. **Permisos alternativos:** `ProtectedRoute` y el menú aceptan una lista de permisos que se cumple con cualquiera
   (`formrecords.read` **o** `formapprovals.decide`).

Tableros: D2 (versión 79 del lienzo, ajustado en la 96), MF1, DC0 y DC1 ajustado (versión 94/96). Capturas elegidas:
[MF1](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-mf1-tabla.png),
[D2](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/det-d2-panel.png),
[DC0](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/det-dc0-vista-rapida.png) y
[DC1 ajustado](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/det-dc1-ajustado.png).

**Commits clave** (hash corto y asunto):

| Tema | Commits |
| --- | --- |
| Arreglos del review de la Etapa 4 (5.2) | `4ef7825` unificar avisos de errores operativos · `764d68f` marcar y enfocar los errores de reglas · `e3533e3` conservar la decisión y las consultas observadas · `a9848c0` abrir el PDF en el visor nativo · `cb242bb` mostrar estado y ordenar el historial de respuestas · `2b27e0d` recuperar reglas y conservar el foco · `02c43cc` aplicar filtros aunque falle el conteo · `3f085b6` conservar el regreso a Por aprobar · `bcf7824` aceptar el origen del formulario enviado · `400f342` verificar las siete cohortes HTTP |
| D2 | `978a8d1` pestañas y selección accesible de filas · `d9c8581` tipar estado y origen del detalle D2 · `66acf6b` agrupar la actividad por momentos observados · `f5874aa` abrir respuestas en un panel navegable · `355c581` escenas y referencias de D2 · `98022bc` cerrar el panel desde una pestaña con Escape · `edb7ff3` conservar el panel al cambiar de respuesta · `c448fcf` recuperar el teclado al elegir otra respuesta · `1d300c4` ajustar el panel en escritorio y celular · `4f3a922` momentos con estados observados · `64e95a2` distinguir el estado en la planilla · `736f01f` devolver el foco al detalle seleccionado |
| MF1 y DC0/DC1 | `3e359a8` alternativas de permisos entre rutas y menú · `cefc2dd` separar el dueño del detalle y su historia · `9510b05` secciones plegables accesibles · `eddb095` contrato de la tabla de gestión · `d4dfdc3` origen de cada PDF · `06890f7` navegación de una respuesta · `4a7fd7d`/`3f9a476` resumen y pendientes por formulario · `8e959f1` rutas compartibles sin filtros privados · `8ab67fb` respuestas completas con navegación privada · `1610f9e` copias PDF en la historia completa · `c382c3a` retirar navegación y bandeja global · `66917ba` textos y guías de MF1 · `53b1069` conservar detalle y permisos al revisar pendientes · `30a65a6` respuesta completa en las migas · `9a91172` densidad y filtros de MF1 · `07ef34f` descarga dentro del visor PDF · `64eac1b` migas legibles en detalle móvil · `54cca75` selección retenida tras revocar aprobación |
| Review de menú y detalle (2026-10-21) | `604305b` distinguir bajas entre formularios por aprobar · `71cd169` vacíos según permisos y filtro · `36e54d0` mantener activa la sección de respuestas · `bf0820b` migas de la planilla en D2 · `a9f8a6f` conservar la fila seleccionada · `2da26a1` abrir Actividad desde el atajo PDF móvil · `2c4058b`/`a3ca9fc` continuar pendientes desde la tarea observada · `756a825`, `e3be850`, `c9a9d68`, `4e2cfd7` pruebas · `4099d36`, `5fa0bc0` contratos |

**Qué quedó obsoleto del anexo anterior:**

- **1.1 y 1.2 (subpanel «Formularios»):** el orden «Por aprobar (N)» → sección «Respuestas» con hasta 4 formularios y
  «Ver todos» → «Diseñar formularios» → «Catálogos» ya no existe. Ver 5.3.1.
- **1.3 (cajón del celular):** el grupo se llama «Gestión de formularios» y tiene las cuatro entradas de 5.3.1.
- **1.4 (migas):** cambiaron las de respuestas (5.3.2). Las del diseño PDF también (5.2).
- **1.5 (rutas):** se retiraron `/formularios/por-aprobar` (`ApprovalInboxPage`) y la página de
  `ResponseFormsPage`; `/formularios/respuestas` es ahora MF1 (`FormOverviewPage`). Se sumó `…/completa`.
- **1.6 (URL):** se reemplaza por la tabla de 5.3.4.
- **1.7 (permisos):** `formrecords.read` ya no es el único permiso de las respuestas: con solo
  `formapprovals.decide` se entra a MF1 y a los pendientes de cada formulario.
- **E3.1** (Respuestas: todos los formularios), **E3.4** (detalle como página) y **E4.3** (Por aprobar global): retirados.
- **Observaciones 3 y 4** del apartado 5 del anexo anterior (rótulo «Volver a la planilla» desde Por aprobar; error de la
  bandeja con el texto de formularios): ya no aplican; la bandeja no existe y el error de pendientes dice
  «No pudimos cargar las aprobaciones.».
- **Observación 6** (los botones «Recibida» y «Más filtros» abren el mismo panel): **sigue igual** en
  `src/features/business-forms/pages/FormResponsesPage.tsx`.

### 5.2 Arreglos del review de la Etapa 4 (2026-10-17, antes de D2)

Diez commits cerraron hallazgos del review de la Etapa 4 sobre reglas, aprobación, PDF, historial y filtros. Son
cambios de comportamiento sobre pantallas que el anexo anterior ya describió (E4.1, E4.2, E4.4–E4.6, E3.3):

- **Avisos operativos unificados (`4ef7825`):** los fallos de guardar reglas, guardar el diseño del PDF, decidir una
  aprobación y descargar un PDF fijo dejaron de ser párrafos dentro de la superficie y pasaron a **un toast por
  operación** (`notify` con un id de dueño y operación: `forms:rules:…:save`, `forms:pdf-design:…:save`,
  `forms:approval:{owner}:{taskId}:decide`, `forms:document:{owner}:{recordId}:{documentId}:download`). Se descartan al
  salir o al reintentar. Siguen junto al control: campo inválido, conflicto vigente, espera de un 429, respuesta propia
  y falta de permiso. Las lecturas de reglas, historial, diseño y aprobaciones dejaron de declarar `errorOwner: "local"`
  y notifican por el `QueryCache` común; un refetch fallido conserva los datos. Los estados de error de lectura muestran
  «No pudimos cargar los datos» (`common:states.loadFailed`) con «Reintentar». Esto cierra la observación 5 del anexo
  anterior.
- **Errores de reglas ligados y enfocados (`764d68f`):** `ruleFieldErrors` (`src/features/business-forms/rules.ts`)
  normaliza las rutas `rules[0].actions[1]…` del 400 a `rules.0.actions.1…`, expande la primera regla con error y
  enfoca su primer control con `aria-invalid`.
- **Decisión que no se desmonta (`e3533e3`):** el panel de decisión sigue montado después de decidir o de un 409, y el
  resultado confirmado recibe el foco. Nace `useUnobservedQueryCleanup`
  (`src/features/business-forms/hooks/useUnobservedQueryCleanup.ts`): al desmontar o cambiar de dueño, una microtarea
  cancela y retira **solo las consultas sin observadores** de esa raíz (respuestas, aprobaciones, catálogos).
- **PDF en el visor nativo (`a9848c0`, `07ef34f`):** `EmbeddedPreview` (`src/shared/ui/EmbeddedPreview.tsx`) tiene la
  variante `kind="pdf"` **sin `sandbox`**, para que el navegador use su visor; la variante externa (vista oficial de
  Meta) conserva el sandbox. `FormPdfPreview` ofrece «Descargar» sobre el mismo blob, sin otra petición. Las migas del
  diseño PDF pasan a `Inicio / {nombre del formulario} / Diseño del PDF`; el formulario es enlace al editor solo con
  `formtemplates.read` (`PdfBreadcrumbs` en `src/layouts/components/Breadcrumbs.tsx`).
- **Historial ordenado y estado en la cabecera (`cb242bb`):** `composeWorkflowHistory`
  (`src/features/business-forms/workflowHistory.ts`) ordena eventos de la respuesta, decisión y acciones por instante
  UTC con desempate estable (clase, orden de evento, regla y orden de la acción, id). «Entregado» y «Leído» usan solo
  las fechas del proveedor por destinatario. Los textos «Pendiente de aprobación» y «Pendiente» pasaron a «Por aprobar».
- **Reglas: conflicto y foco (`2b27e0d`):** ante un 409, después de «Releer reglas» aparecen «Usar la revisión actual» y
  **«Conservar mis cambios»** (adopta solo la revisión base y mantiene el borrador, con la leyenda «Podés conservar tu
  borrador completo. Al guardarlo, reemplazará las reglas de la revisión {{revision}}.»). El historial de revisiones
  muestra los cambios que da el servidor (`changes`: «N reglas agregadas», «N actualizadas», «N quitadas», «Cambió el
  orden», «Sin cambios»). Quitar una regla, acción o condición enfoca la vecina o «Agregar». `MultiSelect` admite
  `disabled`.
- **Filtros de la planilla (`02c43cc`):** «Aplicar filtros» ya no depende del conteo: alcanza con un borrador válido.
  Si el conteo falla, el botón dice «Aplicar filtros» y aparece «Reintentar» solo por red, 5xx o 429 (después de la
  espera).
- **`3f085b6`** (regreso a Por aprobar) quedó superado al retirarse la bandeja. Dejó un cambio transversal:
  `useQueryUpdate` (`src/shared/hooks/useQueryUpdate.ts`) **conserva el `location.state`** al reescribir la query.
- **`bcf7824`:** el recibo de un envío de formulario desde el chat exige `origin` (área WhatsApp). **`400f342`:**
  `httpCohort` admite cohortes con subcarpetas es/en (mecanismo en la sección 11).

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), commit `bb98bf2`):
> la superficie «Cuando llega una respuesta» (`src/features/business-forms/components/FormRulesSection.tsx`) muestra en
> su encabezado la insignia «Cambios sin guardar» o «Revisión N» al lado de «Guardar reglas», y **debajo del botón** un
> párrafo con el motivo del bloqueo («No hay cambios para guardar.», «Revisá los datos marcados antes de guardar.»,
> «Releé y elegí qué conservar antes de guardar.», «Esperá a que termine la operación.» o la espera de un 429). El
> bloque del conflicto suma la leyenda de «Conservar mis cambios». El diseño del PDF
> (`src/features/business-forms/pages/FormPdfDesignPage.tsx`) muestra «· Cambios sin guardar» en el estado de la cabecera
> y «Revisión N · {fecha}» al pie de «El comprobante». Todo eso lo lista el punto 38 del
> [review E2](../../reviews/2026-10-23-code-review-catalogos-e2.md). Estado en `982ade8`: **pendiente**. Lo correcto:
> botón habilitado o deshabilitado, motivo como tooltip, sin estado de guardado ni número de revisión, errores en sus
> campos.

> **No copiar — regla de idioma del 2026-10-23** (review E2, punto 35): el diseño del PDF tiene pares de campos por
> idioma («Nombre del PDF» y «En {otro idioma}», «Texto al pie» y «Texto al pie en {otro idioma}») y un segmentado de
> vista previa con «Español»/«English» escritos en el código (sin traducir). La regla nueva pide un solo campo en el
> idioma del menú. Estado en `982ade8`: **pendiente**.

### 5.3 Navegación y rutas

#### 5.3.1 Menú: grupo «Gestión de formularios»

Archivos: `src/layouts/navigation.ts` (modelo), `src/layouts/components/Sidebar.tsx` (dibujo), `src/app/FormOverviewShell.tsx`
y `src/app/FormOverviewCount.tsx` (contador), `src/layouts/formsOverviewNavigation.ts` (contexto neutral).

El modelo `forms` declara cuatro enlaces, en este orden:

| Entrada | Ruta | Permiso | Notas |
| --- | --- | --- | --- |
| **«Formularios»** (o «Formularios (N)») | `/formularios/respuestas` | `formrecords.read` **o** `formapprovals.decide` | N es la cantidad global de pendientes que la cuenta puede decidir; solo se muestra con `formapprovals.decide` |
| «Diseñar formularios» | `/formularios` | `formtemplates.read` | |
| «Catálogos» | `/catalogos` | `formcatalogs.read` | |
| «Formularios que faltan» | `/formularios/faltantes` | `formtemplates.manage` | Etapa 5 (sección 3); coincidencia exacta |

- **Escritorio:** el botón del menú principal dice **«Gestión de formularios»** (antes «Formularios») y abre el
  subpanel de 224 px con el mismo título. Arriba de la lista va `FormsOverviewLink`: el enlace «Formularios» con el
  número entre paréntesis, en **texto** (el tablero dibujaba una insignia). Mientras carga el número se ve un bloque de
  carga de 16 px debajo; si falla, un botón fantasma «Reintentar». Después van las otras tres entradas según permisos.
  El subpanel se muestra si hay alguna entrada visible o si la cuenta tiene lectura o decisión. El botón de cierre del
  borde sigue diciendo «Cerrar el panel Formularios» (no se actualizó al nuevo nombre del grupo).
- **Celular (cajón):** «Gestión de formularios» es un grupo desplegable; al abrirlo aparece primero «Formularios (N)» y
  después las demás.
- **Marcado activo:** «Formularios» queda activo en `/formularios/respuestas` y en cualquier
  `/formularios/{id}/respuestas…`, incluida la pantalla completa (arreglo P7 del review, `36e54d0`). «Diseñar
  formularios» **no** se marca en esas rutas ni en `/formularios/faltantes`.
- **Contador diferido:** `FormOverviewShell` envuelve a `AppLayout` (en `src/app/routes.tsx`, dentro de
  `UserDataSectionShell`) y monta el componente lazy `FormOverviewCount` **solo** si la cuenta tiene
  `formapprovals.decide` y además está en una ruta de la sección (`isFormsPath`) o abrió el panel/cajón (el `Sidebar`
  llama a `setActive`). La lectura es `useApprovalCount` (`GET /api/forms/approvals/count?onlyMine=true`). El estado
  se guarda por dueño (`id:idioma:permisos ordenados`) y se descarta al cambiar de dueño.
- **Permisos alternativos (`3e359a8`):** `PermissionRequirement = string | readonly string[]` en
  `src/auth/usePermissions.ts`; `has([...])` es "alguno de". `ProtectedRoute` y `canSee` del menú usan la misma regla
  (`src/layouts/components/Sidebar.permissions.test.tsx` lo fija para las cuatro combinaciones).

#### 5.3.2 Migas

`ResponseBreadcrumbs` (en `src/layouts/components/Breadcrumbs.tsx`) cubre todas las rutas de respuestas:

| Ruta | Migas |
| --- | --- |
| `/formularios/respuestas` | `Inicio / Gestión de formularios / Formularios` |
| `/formularios/{id}/respuestas` (planilla, o pendientes para quien solo decide) | `Inicio / Gestión de formularios / Formularios (enlace a MF1) / {nombre del formulario}` |
| `/formularios/{id}/respuestas/{recordId}` (D2 abierto) | **Igual que la planilla**: D2 no agrega nivel (arreglo P8, `bf0820b`) |
| `/formularios/{id}/respuestas/{recordId}/completa` | `Inicio / Gestión de formularios / Formularios / {nombre del formulario (enlace a la planilla)} / {número}` |

- «Gestión de formularios» es texto, no enlace. El nombre del formulario y el número los ponen las páginas con
  `useBreadcrumbLeaf` (hoja y padre); hasta entonces se ve «Respuestas».
- **Celular, pantalla completa:** se ocultan Inicio, el grupo, «Formularios» y sus separadores (`hidden sm:block`);
  queda `{formulario} / {número}`. La prueba visual comprueba que el padre tiene ancho legible y el número entra
  completo.

#### 5.3.3 Rutas

Todas en `src/app/routes.tsx`, lazy. Las tres rutas de respuestas comparten el layout invisible
`ResponseWorkspaceLayout` (`src/features/business-forms/components/ResponseWorkspaceLayout.tsx`), que monta el dueño
de memoria del formulario (5.8).

| Ruta | Página | Permiso de la ruta |
| --- | --- | --- |
| `/formularios/respuestas` | `src/features/business-forms/pages/FormOverviewPage.tsx` (MF1) | `formrecords.read` o `formapprovals.decide` |
| `/formularios/{formId}/respuestas` y `/formularios/{formId}/respuestas/{recordId}` | `src/features/business-forms/pages/FormResponseWorkspacePage.tsx` (planilla + D2, o pendientes) | `formrecords.read` o `formapprovals.decide` |
| `/formularios/{formId}/respuestas/{recordId}/completa` | `src/features/business-forms/pages/FormResponseFullPage.tsx` (DC1) | `formrecords.read` (sin lectura: `/sin-permiso`) |

- La ruta hija `:recordId` tiene `element: null`: la planilla sigue montada y el panel se dibuja encima.
- `FormResponseWorkspacePage` decide qué se ve: con lectura, la planilla (`FormResponsesPage`); si además es celular y
  la URL trae `recordId`, **redirige a `…/completa`**; sin lectura pero con decisión, la pantalla de pendientes
  (`FormPendingApprovals standalone`); sin ninguno, «No tenés permiso».
- **No hay redirección** desde la dirección vieja `/formularios/por-aprobar`: esa URL ahora coincide con
  `/formularios/:formId` (editor, con `formtemplates.read`). Qué muestra no se verificó en navegador.

#### 5.3.4 Qué vive en la URL y qué nunca

| Ruta | Parámetros |
| --- | --- |
| `/formularios/respuestas` | `page`, `pageSize` (se fija al abrir con el tamaño del sistema), `search`, `state=Deactivated` (ausente = Activos), `pendingOnly=true` (solo se respeta con lectura **y** decisión: una URL compartida no da el filtro) |
| `/formularios/{id}/respuestas` | Los de antes (`page`, `pageSize`, `sort`, `sortFieldId`, `versionIds`, `columns`) más `approval` (TaskId del pendiente abierto en D2), `approvalPage`, `approvalPageSize` (20 por defecto) y `tab` |
| `/formularios/{id}/respuestas/{recordId}` | Lo mismo, con `tab` = `photos`, `activity` o `pdf` (ausente = Respuesta). Una pestaña que no corresponde (Fotos sin fotos, PDF sin copias) se quita sola de la URL |
| `…/completa` | **Solo** `sort`, `sortFieldId` y `versionIds` (`responseFullPath` en `src/features/business-forms/responseNavigationPaths.ts`). Si llegan otros, la página reemplaza la URL sin ellos |

Volver de la pantalla completa arma la planilla con `responseSheetPath`: `page`, `pageSize`, `sort`, `sortFieldId`,
`versionIds`, `columns`, `approvalPage` y `approvalPageSize` (no `approval`).

**Nunca en la URL, en `history.state` ni en storage:** la búsqueda en textos largos, el filtro **Estado** (nuevo), las
personas, las condiciones por pregunta y el rango de fechas; el **origen** del recorrido (planilla o pendientes), la
selección (RecordId/TaskId), la cola de pendientes capturada y la página observada; la **intención «Ver PDF»** de la
pantalla completa; los blobs y object URLs de los PDF. Todo eso vive en el dueño de memoria del formulario (5.8).

### 5.4 MF1: «Formularios» (`FormOverviewPage`)

**Ruta:** `/formularios/respuestas` · **Permiso:** lectura o decisión ·
`src/features/business-forms/pages/FormOverviewPage.tsx`, `src/features/business-forms/hooks/useFormOverview.ts`,
`src/features/business-forms/api/overview.ts`.

**Cabecera:** título «Formularios». Sin acciones ni flecha de volver.

**Barra de filtros** (una fila en escritorio; la prueba visual lo exige desde 1024 px):
1. Buscador **«Buscar formularios»** (máx. 100; 288 px desde `sm`, todo el ancho en el celular). Escribe `search` y
   vuelve a la página 1.
2. Segmentado **«Estado de los formularios»**: **«Activos (N)» · «Dados de baja (N)»**, con conteos del servidor
   (`filterCounts`); «—» mientras no hay datos. Va dentro de una caja con desplazamiento horizontal propio.
3. Casilla **«Con algo por aprobar»**: solo con lectura **y** decisión. Marcada, ningún segmento queda presionado y no
   se muestran los avisos de abajo (arreglo P3, `604305b`).

**Avisos** (`Banner`, encima de la tabla):
- En «Dados de baja»: «Los formularios dados de baja conservan sus respuestas y lo que espera una decisión.»
- En «Activos», si `deactivatedPending > 0`: «Hay N formulario(s) dado(s) de baja con algo por aprobar.» con el botón
  **«Ver»** (nombre accesible «Ver dados de baja»), que pasa a Dados de baja y quita `pendingOnly` en la misma
  actualización.

**Tabla** (superficie sin banda; columnas según permisos; **ninguna ordenable**: el orden lo fija el servidor, primero
los que esperan una decisión y después por la última respuesta):

| Columna | Permiso | Contenido |
| --- | --- | --- |
| Formulario | siempre | Nombre como enlace a `/formularios/{id}/respuestas`; insignia «Dado de baja» si `deactivatedAtUtc` no es null (también con «Con algo por aprobar») |
| Por aprobar | decisión | Insignia de advertencia con N, enlace a la planilla (nombre accesible «Revisar N respuestas de {nombre}»); «—» si es 0 |
| Respuestas | lectura | Cantidad; «—» si viene null |
| Última respuesta | lectura | Número como enlace a `/formularios/{id}/respuestas/{recordId}` (abre D2 en escritorio), fecha y hora en la zona de la cuenta e insignia de estado («Recibida», «Por aprobar», «Aprobada», «Rechazada»); sin respuestas: «Sin respuestas todavía» |

Debajo, el paginado (`Pagination`). No hay resumen «N formularios».

**Estados:**
- Cargando: spinner de la tabla.
- Vacío (arreglo P4, `71cd169`): con búsqueda → «Sin coincidencias» / «No encontramos formularios con estos filtros.»;
  solo decisión o con «Con algo por aprobar» → «No hay respuestas por aprobar.» / «Los formularios con respuestas que
  podés decidir van a aparecer acá.»; en Dados de baja → «No hay formularios dados de baja» / «Los formularios
  publicados que se den de baja van a aparecer acá.»; si no → «Todavía no hay formularios publicados» / «Los
  formularios publicados van a aparecer acá.». No ofrece «Limpiar filtros».
- Error: en la tabla «No pudimos cargar los formularios.» + «Reintentar»; si ya había datos del mismo dueño se
  conservan. La causa va en **un** toast (`useResponseFeedback`, id `forms:overview:{owner}`), con «Reintentar» en el
  toast solo si fue de red.
- 429: toast con el mensaje del servidor; no hay línea de espera propia.
- 403 o sin ninguno de los dos permisos: «No tenés permiso». Un 403 marca al dueño como denegado, retira de la caché
  las filas y conteos ya observados y no vuelve a pedir.
- 409: no aplica (solo lectura).

**Perfiles:**
- Solo lectura: columnas Formulario, Respuestas y Última respuesta; sin «Por aprobar», sin casilla, sin número en el
  menú.
- Solo decisión: columnas Formulario y Por aprobar; el servidor manda solo los formularios con algo para decidir.
- Ambos: las cuatro columnas y la casilla.

**390 y 320 px:** la tabla se desplaza a lo ancho **dentro de su caja**, nunca la página (la prueba visual lo exige con
lectura por debajo de 640 px); el segmentado entra en el ancho y ninguna opción se recorta; las filas miden hasta 40 px.
Evidencia: [escritorio](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-detalle-20261021/form-overview-active-es-1440.png),
[lectura en inglés a 390](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-detalle-20261021/form-overview-read-en-390.png),
[pendientes en escritorio](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-review-20261021/mf1-pending-es-desktop.png)
y [pendientes en inglés a 320](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-review-20261021/mf1-pending-en-320.png).

### 5.5 Dentro de un formulario: «Por aprobar · N» y la planilla

#### 5.5.1 Quién ve qué (`FormResponseWorkspacePage`)

- **Lectura (con o sin decisión):** la planilla de siempre (`FormResponsesPage`). Con decisión, arriba de la barra de
  filtros, el bloque «Por aprobar · N».
- **Solo decisión:** una pantalla propia (`PendingFormPage` dentro de
  `src/features/business-forms/components/FormPendingApprovals.tsx`): título = nombre del formulario (sale de la
  primera tarea), flecha volver «Formularios» a `/formularios/respuestas`, y solo el bloque «Por aprobar · N». Nunca lee
  valores, planilla ni workflow.

#### 5.5.2 Bloque «Por aprobar · N» (`FormPendingApprovals`)

Superficie con banda: título **«Por aprobar · N»** (N = total del servidor; «Por aprobar» mientras carga). Tabla con:

| Columna | Contenido |
| --- | --- |
| N° | Número de la respuesta |
| Cargó | Persona («Persona sin información» sin nombre) |
| Recibida | Fecha y hora del pedido de aprobación (`requestedAtUtc`), en la zona de la cuenta |
| Rol | Rol que decide |
| Acciones | Enlace **«Revisar»** (nombre accesible «Revisar {número}») |

- Orden: de la más vieja a la más nueva (servidor). Lee solo tareas de ese formulario que la cuenta puede decidir
  (`definitionId`, `onlyMine=true`). La fila seleccionada queda marcada (`aria-selected`) y tocar la fila hace lo
  mismo que «Revisar».
- Paginado solo si hay más de una página (`approvalPage`, `approvalPageSize`); cambiar de página cierra la tarea abierta.
- **Revisar:** en escritorio pone `?approval={TaskId}` y abre D2 sobre la planilla; con lectura, D2 completo con el
  recorrido de pendientes; sin lectura, el panel de metadata (5.6.6). En el celular con lectura navega directo a
  `…/completa`; sin lectura abre el diálogo de metadata a pantalla completa.
- Vacío: «No hay respuestas por aprobar.» / «Las respuestas que esperan aprobación aparecen acá, de la más vieja a la
  más nueva.». **Se dibuja aunque no haya pendientes** para quien lee y decide (el tablero no lo definía; queda en
  «Después, sin bloquear» del review de menú y detalle).
- Error de la lista: «No pudimos cargar las aprobaciones.» + «Reintentar», y un único toast. Una tarea que no existe
  (404): «No encontramos esa respuesta o no está disponible».
- 403 de decisión: el bloque muestra «Ya no tenés permiso para decidir esta aprobación.», se retiran lista, tarea,
  cola y continuación de ese formulario; si la cuenta sigue leyendo, el registro ya observado se sigue mostrando en D2
  sin decisión.
- Al cerrar el panel, el foco vuelve al «Revisar» de esa tarea (`data-pending-task`) o a la tabla.

#### 5.5.3 Cambios en la planilla (`FormResponsesPage`)

Sobre lo descripto en E3.2 del anexo anterior:
- **Filtro «Estado»** (nuevo, `f5874aa`/`d9c8581`): desplegable nativo con «Todos los estados», «Recibida», «Por
  aprobar», «Aprobada» y «Rechazada», solo si el formulario pide aprobación (`hasApproval`). Es privado (memoria del
  formulario) y suma el chip «Estado: {valor}».
- **Columna «Estado»:** insignia con variante por estado (advertencia para Por aprobar, éxito para Aprobada, peligro
  para Rechazada, contorno para Recibida).
- **Columna «PDF»** (nueva): aparece si alguna fila de la página tiene `hasPdf`. Botón de ícono «Ver PDF de {número}»:
  en escritorio abre D2 en la pestaña PDF; en el celular abre la pantalla completa con la intención «Ver PDF» (Actividad
  abierta). No lee el workflow por fila.
- **Abrir una respuesta:** el número es enlace (a D2 en escritorio, a `…/completa` en el celular) y la fila entera es
  clicable (`onRowClick` de `DataTable`, que ignora clics sobre enlaces, botones y campos). La fila abierta o recordada
  queda marcada con `aria-selected`.
- Con el diálogo de metadata abierto en el celular, la planilla queda `inert`.
- Las subcolumnas de catálogo y la ordenación de «elegir varias» se describen en las secciones 8 y 10.

> **No copiar — review E2, punto 32** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)): falta «Columnas ·
> N ocultas» de CE5 en el menú de columnas. Lo detalla la sección 10.

### 5.6 D2: la vista rápida (`FormResponseDetailPage`)

`src/features/business-forms/pages/FormResponseDetailPage.tsx` (elige marco), `src/features/business-forms/components/ResponsePanelFrame.tsx`
(marco y teclado), `src/features/business-forms/components/ResponseDetailContent.tsx` (contenido),
`src/features/business-forms/hooks/useResponseDetailState.ts` (dueño de lecturas y avisos).

#### 5.6.1 Marco

- **Escritorio:** una `section` pegada a la derecha del área de la planilla, del alto disponible, ancho hasta 520 px,
  borde izquierdo y sombra, **sin fondo oscurecido**: la planilla sigue visible y usable. Región con nombre «Detalle de
  la respuesta». Al abrir o cambiar de respuesta, el foco pasa al panel.
- **Celular:** con lectura no hay panel (redirige a la pantalla completa). Solo quien decide sin leer ve el panel como
  diálogo a la derecha, de ancho completo y alto `100svh − 3rem` debajo de la barra superior.

**Cabecera del panel**, de izquierda a derecha:
1. Número de la respuesta (encabezado h2; en el celular ocupa toda la línea).
2. Insignia del estado confirmado por el workflow («Recibida», «Por aprobar», «Aprobada», «Rechazada»).
3. En el celular, «X de Y» o «X de Y por aprobar».
4. Botones de ícono ↑ «Anterior» y ↓ «Siguiente» (con tooltip) y ✕ «Cerrar».

Debajo: subtítulo «{formulario} · Versión N» y una fila de acciones: **«Abrir en pantalla completa»** (enlace con
estilo de botón; solo para quien lee), «Abrir conversación» (enlace a `/whatsapp/chat?conversacion={id}`, con
`whatsapp.read` y conversación) y **«Descargar ▾»** (menú con las copias PDF, si hay).

**Pie del panel (escritorio):** «X de Y · ↑ ↓ otra respuesta · Esc cerrar» o, entrando por pendientes, «X de Y por
aprobar · ↑ ↓ recorren lo que espera · Escape cierra».

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md)):
> las acciones «Abrir en pantalla completa», «Abrir conversación» y «Descargar ▾» van en una segunda fila, alineadas a
> la izquierda debajo del subtítulo, y no al borde derecho del encabezado a la altura del título. No figura en el punto
> 38; conviene resolverlo al adaptarlo.

#### 5.6.2 Teclado y foco

- ↑ y ↓ pasan de respuesta cuando el foco está dentro del panel, salvo en campos, IME, combos, listas, menús, la lista
  de pestañas (ahí las flechas son de las pestañas) o un diálogo hijo. Sin modificadores.
- Escape cierra, también con el foco en una pestaña (`98022bc`).
- En el borde de la página, el recorrido pide la página vecina y salta (`useResponsePanelNavigation`); cruzar de página
  actualiza `page` en la URL. En el extremo final, el foco vuelve al panel.
- Al cerrar, el foco vuelve a la fila de esa respuesta (`data-response-id`) o al contenedor, y la selección recordada
  **sigue a la última respuesta vista** (arreglo P9, `a9f8a6f`).
- Mientras llega la respuesta siguiente, la anterior queda visible con `aria-busy` y se bloquean decidir y descargar.

#### 5.6.3 Momentos (arriba de las pestañas)

Una lista con un renglón por momento, con ícono y estado (`composeWorkflowMoments` en
`src/features/business-forms/workflowMoments.ts`, armado con lo **capturado** del workflow, no con las reglas actuales):

1. **«Recibida»**: «{persona} · {fecha}» y, si hay origen, «Se lo mandó {nombre}» o «Se lo mandó el asistente».
2. **«Al recibirla»** (si hubo acciones): enlace «N de M hechas» que abre la pestaña Actividad y, si algo falló o quedó
   sin confirmar, «Hay fallas o envíos sin confirmar.».
3. **Aprobación** (si hay tarea): «Por aprobar» con el tiempo relativo (tooltip con la fecha exacta), «Aprobada» o
   «Rechazada». Con decisión, adentro va el panel de decisión embebido y los botones «Rechazar» y «Aprobar» justo debajo
   (en el celular, barra fija abajo). Sin decisión, rol, quién decidió y el motivo.
4. **«Al aprobarla» / «Al rechazarla»** (si hubo acciones después de decidir).

Se omiten las acciones «No aplica» y la acción completada de pedir aprobación cuando ya hay tarea. Estado del renglón:
pendiente (fondo de advertencia), fallido o rechazado, sin confirmar, o completo.

#### 5.6.4 Pestañas

Componente compartido `Tabs` (`src/shared/ui/Tabs.tsx`, detallado en la sección 11). La pestaña va en la URL (`tab`).

| Pestaña | Cuándo | Contenido |
| --- | --- | --- |
| «Respuesta» | siempre | Lista «pregunta / valor» en dos columnas (2:3) con las preguntas de la versión de la respuesta, fotos y documentos en su valor, y las marcas de asistencia (sección 3) |
| «Fotos (N)» | si la versión tiene preguntas de foto | Galería con todas las fotos; abre el visor compartido |
| «Actividad» | siempre | Superficie «Actividad» con «Reglas de la revisión N» en la banda y la historia agrupada por momentos (5.6.5). Se monta siempre (`forceMount`) para no perder su estado entre pestañas |
| «PDF» | si hay copias | Superficie «PDF de la respuesta»: por copia, nombre, fecha, insignia del idioma, «Ver PDF» (diálogo «Copia fija» con el visor nativo y «Descargar») y «Descargar»; «Los PDF ya armados no cambian: son una copia fija del momento.» y, tras un 429, «Esperá N segundos para volver a intentar.» |

La procedencia de las sugerencias y la cadena de respuestas (s4) dentro de «Respuesta» se describen en la sección 3.

#### 5.6.5 La historia de Actividad (`ResponseWorkflowHistory`)

Es la única historia de una respuesta; la usan D2 (en la pestaña) y DC1 (en la sección). Se agrupa por los mismos
momentos: «Recibida» (origen y eventos «Se mandó el formulario», «Se respondió el formulario», «Se guardó la
respuesta»), «Al recibirla» con «N de M hechas» y sus acciones, «Por aprobar» o «Por aprobar → Aprobada/Rechazada»
(«Se pidió aprobación al rol {rol}» y la decisión con quién, cuándo y motivo) y «Al aprobarla»/«Al rechazarla». Cada
acción conserva lo del anexo anterior (estado, Consultar/Reintentar, destinatarios con «Resolver envío») y suma, por
destinatario, «Entregado {fecha}» y «Leído {fecha}» con las fechas del proveedor. En DC1, la acción «Armar el PDF» de
«Al aprobarla» muestra además la miniatura de su copia (5.7.3).

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md)):
> la historia explica sus botones con texto fijo: «No sabemos si salió. Consultá el estado y, si sigue sin confirmar,
> revisá el envío.» junto a «Consultar» y, al pie, «Reintentar retoma sólo lo que no salió: no repite lo confirmado ni
> arma otro PDF.». No están en el punto 38; según la regla, si el motivo no es obvio va como tooltip. Además la banda de
> «Actividad» muestra «Reglas de la revisión N»: es un dato histórico (con qué reglas se procesó), no un estado de
> guardado, pero es un número de revisión en un encabezado; conviene decidir si se conserva.

#### 5.6.6 Para quien solo decide

El panel (escritorio) o el diálogo (celular) muestra título = número, subtítulo = nombre del formulario, sin pestañas y
sin «Abrir en pantalla completa». Adentro, el panel de decisión no embebido: insignia de estado, rol, decisión y motivo;
«Rechazar» y «Aprobar» debajo (en el celular, en una barra fija abajo). Ninguna lectura de valores ni de workflow
(`approvals-pending-*` en la prueba visual lo verifica). Evidencia:
[solo decisión a 320](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-review-20261021/decide-only-es-320.png).

#### 5.6.7 Estados de D2

- Cargando: marco con título «Detalle de la respuesta» y spinner.
- Respuesta que no existe o de otro formulario (404): «No encontramos esa respuesta o no está disponible», sin
  reintentar.
- Error de lectura: «No pudimos cargar las respuestas.» + «Reintentar», y un único toast para valores, workflow y
  navegación (`useResponseFeedback`, id `responses:detail-page:{owner}:{formId}`).
- Error al cruzar de página: arriba del contenido, «No pudimos cargar las respuestas.» + «Reintentar».
- 403 de lectura: «No tenés permiso» dentro del marco y el espacio del formulario se niega (5.8).
- 429: toast con la causa; en descargas, además deshabilita y muestra la espera en la pestaña PDF.
- 409: solo en la decisión (5.6.8).

#### 5.6.8 Aprobar o rechazar (cambios sobre E4.4)

`src/features/business-forms/components/ApprovalDecisionPanel.tsx`:
- Orden de botones: «Rechazar» (secundario) y después «Aprobar» (principal). Los botones se ubican por portal donde los
  pide el marco: en el renglón del momento en D2, en las acciones de la cabecera en DC1, en una barra fija abajo en el
  celular.
- El panel queda montado después de decidir o de un 409; el resultado recibe el foco. Toast «Respuesta aprobada.» o
  «Respuesta rechazada.». Invalida las listas y conteos de aprobaciones (no la tarea, que se escribe con la respuesta
  del POST) y la tabla MF1; la planilla y el workflow se releen (el workflow solo acepta una tarea cuya revisión no
  retroceda: `observeApproval`).
- Errores: un fallo operativo va a un toast; el error del motivo queda junto al campo (`errors.reason`); un 409 relee y
  muestra «La aprobación cambió. Se actualizó con la decisión vigente.»; un 429 muestra «Esperá N segundos para volver
  a intentar.» y deshabilita; una lectura de la tarea fallida por red, 5xx o 429 conserva los datos; otra muestra «No
  pudimos cargar los datos» + «Reintentar».
- Diálogo «Rechazar {número}» sin cambios funcionales (motivo 1–500, recortado).

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md)):
> con los botones deshabilitados, el panel muestra debajo «No podés aprobar ni rechazar tu propia respuesta.», la espera
> de un 429 y el texto del conflicto. Según la regla, el motivo de un botón deshabilitado va como tooltip y no como texto
> al lado. No está en el punto 38.

### 5.7 DC0/DC1: la pantalla completa (`FormResponseFullPage`)

**Ruta:** `/formularios/{formId}/respuestas/{recordId}/completa` · **Permiso:** `formrecords.read` ·
`src/features/business-forms/pages/FormResponseFullPage.tsx`, `src/features/business-forms/hooks/useFullResponseNavigation.ts`.

#### 5.7.1 Cómo se entra

- **Desde D2 (escritorio):** «Abrir en pantalla completa». Antes de navegar se recuerda el origen y se marca una
  entrada interna (`prepareFull`).
- **Celular:** tocar una fila, el número, «Revisar» (con lectura) o el enlace de «Última respuesta» de MF1 lleva directo
  acá. El atajo «Ver PDF» de la planilla lleva además la **intención PDF**, que abre Actividad (arreglo P10, `2da26a1`).
  Cambiar el tamaño de una vista rápida de escritorio a celular conserva el recorrido.
- **Enlace compartido** (sin entrada interna): se **limpia la memoria privada** del formulario (búsqueda, personas,
  condiciones, estado), el origen pasa a "planilla" y el servidor ubica la respuesta con el orden y las versiones
  públicos (`POST …/records/{recordId}/navigation`), aunque esté fuera de la página 1. Con esa posición se calcula la
  página a la que vuelve «Volver».

#### 5.7.2 Zonas

**Cabecera (`Page`):**
- Flecha volver **«Volver a las respuestas»**: a la planilla con su página, columnas, orden y versiones; la memoria
  conserva filtros privados y selección, y el foco vuelve a la fila (o al «Revisar» del pendiente).
- Título: número de la respuesta. Estado: insignia del estado confirmado. El formulario **no** se repite: está en las
  migas.
- Acciones a la derecha, en este orden: «X de Y» o «X de Y por aprobar»; ↑ «Anterior» y ↓ «Siguiente» (botones de
  ícono); «Abrir conversación» (con `whatsapp.read`); **«Descargar ▾»** (copias PDF **y** documentos cargados en la
  respuesta; los bytes se leen recién al elegir); y, si la tarea está abierta y la cuenta decide, «Rechazar» y
  «Aprobar» (en el celular, barra fija abajo con margen inferior reservado).

**Cuerpo:** dos secciones plegables a todo el ancho (`CollapsibleSection`, `src/shared/ui/CollapsibleSection.tsx`;
superficie con título y un botón de ícono a la derecha de la banda, «Plegar …»/«Expandir …», con `aria-expanded`):

| Sección | Abierta al entrar | Banda abierta | Banda plegada | Contenido |
| --- | --- | --- | --- | --- |
| «Respuesta» | sí | «Versión N» | «N preguntas» | La misma lista de `ResponseAnswer` que D2 |
| «Actividad» | sí en escritorio; **plegada en el celular**, salvo con la intención PDF | «Reglas de la revisión N» | los momentos unidos por « · » (por ejemplo «Recibida · Al recibirla · Por aprobar») | La historia de 5.6.5, con la miniatura del PDF en su acción |

Debajo, con decisión, el panel de decisión embebido (rol, decisión, motivo). No hay «De dónde vino», PDF al costado ni
fila de momentos arriba (lo pedía DC1).

#### 5.7.3 El PDF dentro de Actividad

- La copia se ubica en la acción «Armar el PDF» de «Al aprobarla», por identidad: `documents[].runId == action.id`
  (`d4dfdc3`), nunca por fecha ni por idioma.
- `ResponseDocumentPreview` muestra nombre, insignia del idioma y una **miniatura** (iframe de 176 px de alto y hasta
  320 de ancho; la prueba exige ≤ 320 × 180) con «Ver PDF» (amplía en el diálogo «Copia fija», con «Descargar») y
  «Descargar». Los dos usan **el mismo blob** ya leído.
- Con Actividad plegada al entrar no se leen los bytes; se leen al abrirla por primera vez y se reutilizan al plegar y
  volver a abrir o al ampliar. Salir de la respuesta, cambiar de dueño o perder la lectura revoca la URL.
- Evidencia: [Actividad en inglés a 390](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-detalle-20261021/response-full-activity-en-390.png),
  [visor a 320](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-detalle-20261021/response-full-pdf-es-320.png)
  y [atajo PDF a 390](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-review-20261021/pdf-intent-en-390.png).
  El iframe nativo sale en blanco en las capturas: acreditan marco, nombre, idioma y controles, no los píxeles del PDF.

#### 5.7.4 Flechas

- Si se entró por pendientes, ↑ ↓ recorren **la cola capturada** de tareas, de la más vieja a la más nueva («1 de 2 por
  aprobar»). En el borde se pide una continuación desde la tarea ancla (`GET /api/forms/approvals/{taskId}/continuation`),
  aunque esa tarea ya esté decidida; nunca por offset. Decidir no pierde el sucesor ya capturado.
- Si se entró por la planilla o por un enlace, siguen el anclaje del servidor (`previousId`, `nextId`, `position`,
  `totalCount`) con la misma búsqueda y orden de la planilla («1 de 24»).
- Con el foco en el contenedor (`tabindex=-1`, recibe el foco al cargar cada respuesta) también funcionan ↑ y ↓.

#### 5.7.5 Estados

- Cargando: cabecera «Detalle de la respuesta» + spinner. Mientras se confirma la ruta siguiente, la respuesta anterior
  queda visible y ocupada.
- No existe o es de otro formulario: «No encontramos esa respuesta o no está disponible».
- Error: «No pudimos cargar las respuestas.» + «Reintentar».
- 403 de lectura: «No tenés permiso» dentro de la página, con la flecha volver. Sin lectura, la ruta lleva a
  `/sin-permiso` (quien solo decide se queda en el panel de metadata).
- 403 de decisión durante el recorrido: se retira solo el contexto de pendientes y la decisión; los valores siguen.
- 429 en descargas: toast y descargas deshabilitadas durante la espera (en DC1 no se muestra la línea de espera;
  verificado en código, no en navegador).

#### 5.7.6 390 y 320 px

Una columna; la cabecera de `Page` pone el título a todo el ancho y las acciones debajo; las migas se reducen a
`{formulario} / {número}`; la decisión queda en una barra fija abajo; nada provoca desplazamiento horizontal de la página
(las pruebas lo exigen). Evidencia: [DC1 inicial a 320](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-detalle-20261021/response-full-initial-es-320.png),
[pendientes a 390](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-review-20261021/pending-full-es-390.png),
[pendientes en inglés a 320](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-review-20261021/pending-full-en-320.png)
y [D2 de escritorio](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/menu-detalle-20261021/response-full-quick-en-1440.png).

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md)):
> la banda de cada sección plegable pone un texto («N preguntas», «Versión N», la lista de momentos o «Reglas de la
> revisión N») justo al lado del botón de plegar. El tablero DC1 pedía que plegadas «digan lo que tienen», así que el
> resumen está elegido; lo que conviene revisar es el número de revisión en el encabezado. Fuera del punto 38.

### 5.8 Hooks y estado

**Dueños y claves de consulta** (todas con `gcTime: 0`; los datos previos solo se reutilizan del mismo dueño):

| Lectura | Clave | Dueño |
| --- | --- | --- |
| MF1 | `["form-overview", owner, filtro]` | `owner = id:idioma:permisos ordenados`; `errorOwner: "local"` |
| Número del menú | `["form-approvals", owner, "count", onlyMine, definitionId]` | sin `errorOwner` local: avisa el `QueryCache` común |
| Pendientes de un formulario | `["form-approvals", owner, "list", {definitionId, onlyMine: true, page, pageSize}]` | `useFormPendingApprovals` |
| Tarea | `["form-approvals", owner, "task", taskId]` | panel de decisión y bloque de pendientes |
| Continuación | `["form-approvals", owner, "continuation", formId, taskId, dirección, pageSize]` | `fetchQuery` con `retry: false`, `staleTime: 0` |
| Planilla | `["form-responses", owner, "sheet", formId, filtro]` | `owner = id:idioma:permisos:read|denied` |
| Detalle | `["form-responses", owner, "record", formId, recordId]` | `useResponseDetail` |
| Workflow | `["form-responses", owner, "workflow", formId, recordId]` | relee cada 2 s mientras haya acciones Esperando o En curso |
| Anclaje | `["form-responses", owner, "record-navigation", formId, recordId, consulta]` | solo si no se entró por pendientes |

**Memoria del formulario** (`ResponseWorkspaceProvider`, `src/features/business-forms/components/ResponseWorkspaceProvider.tsx`,
contrato en `src/features/business-forms/hooks/useResponseWorkspace.ts`):
- `filter`: los filtros privados (búsqueda, estado, personas, condiciones, fechas).
- `navigation`: `{ origin: "sheet" | "pending", selection: { recordId, taskId? }, search, pending? }`, donde `pending`
  es la cola capturada `{ tasks: [{ id, recordId, requestedAtUtc, position? }], page, pageSize, totalCount, offset?, hasPrevious?, hasNext? }`.
- `fullEntry`: `{ formId, recordId, locationKey, intent?: "pdf" }`; `prepareFull` la sella con `locationKey: null`
  antes de navegar; `enterFull` la confirma con la `location.key`; una entrada que no coincide es un enlace compartido y
  limpia el filtro.
- Se reinicia al cambiar de formulario o de dueño; `deny()` lo vacía todo y marca `denied`, que cambia el dueño de las
  claves de respuestas a `…:denied`. También provee el alcance de archivos privados (`PrivateFormFilesScopeProvider`,
  `owner:formId`).
- Nunca guarda callbacks, ni va a la URL, `history.state` o storage.

**Recorridos:**
- `useResponsePanelNavigation`: mueve dentro de la página y, en el borde, pide la página vecina con `client.fetchQuery`;
  descarta resultados si cambió el contexto (`contextKey` = dueño, formulario, registro y consulta).
- `usePendingApprovalNavigation`: recorre la cola capturada; en un extremo pide continuación por TaskId; une referencias
  por `RequestedAtUtc` + Id inmutables (sin perder precisión UTC); una relectura paginada actualiza la tabla pero no
  amplía la cola; un extremo vacío queda agotado; un fallo se reintenta solo por acción explícita; cerrar, cambiar de
  dueño o perder la decisión cancela y retira la continuación.
- `useFullResponseNavigation`: elige entre cola de pendientes y anclaje según el origen; recalcula la página de vuelta
  con la posición.

**Invalidaciones:** decidir invalida `["form-approvals", owner]` salvo las tareas y `["form-overview", owner]`; la
planilla se relee por `onDecided`; el workflow se cancela y actualiza con la tarea confirmada antes de releer. Las
invalidaciones de baja y reactivación (sección 6) también incluyen el dueño de Overview.

**Limpieza y permisos:**
- `useUnobservedQueryCleanup` (respuestas, aprobaciones, Overview): al desmontar, retira en una microtarea solo lo que
  ningún otro componente observa.
- Un 403 de lectura niega el espacio y retira valores, archivos y PDF; con decisión vigente se sigue con metadata.
- Un 403 de decisión (`useFormPendingApprovals.deny`) cancela y retira lista, tareas y continuación de ese formulario,
  sin tocar la planilla autorizada. El registro observado para seguir mostrándolo en D2 pertenece solo a la selección
  actual y se limpia al cerrar o cambiar de cuenta, formulario o tarea.
- Una respuesta tardía nunca toma otro RecordId.

**Blobs y descargas:** `FormPdfPreview` crea y revoca su object URL; `useResponseDocumentDownloads` posee una descarga
por respuesta, compartida por el menú «Descargar ▾» y la pestaña PDF (cerrar el menú no la aborta; cambiar de dueño,
perder lectura o desmontar sí), respeta la espera de un 429 y guarda con `savePrivateBlob`.

### 5.9 Clientes HTTP y guards zod

| Método y ruta | Cliente | Guard |
| --- | --- | --- |
| `GET /api/forms/overview?page&pageSize&state&search&pendingOnly=true` (`no-store`) | `fetchFormOverview` en `src/features/business-forms/api/overview.ts` | `strictObject`: `forms` paginado con filas `{ id, name, prefix, deactivatedAtUtc|null, pendingApprovalCount|null, responseCount|null, lastResponse|null { id, number, submittedAtUtc, state } }`; `filterCounts { active, deactivated, deactivatedPending|null }`. Los null **no** se convierten en cero |
| `GET /api/forms/approvals?definitionId&onlyMine=true&page&pageSize` | `fetchApprovalTasks` en `src/features/business-forms/api/approvals.ts` | Igual que antes; ahora siempre por formulario |
| `GET /api/forms/approvals/count?onlyMine=true` | `fetchApprovalCount` | `{ count }` |
| `GET /api/forms/approvals/{taskId}` | `fetchApprovalTask` | Tarea `strictObject` (sin cambios) |
| `POST /api/forms/approvals/{taskId}/decisions` | `decideApprovalTask` | Sin cambios |
| `GET /api/forms/approvals/{taskId}/continuation?definitionId&direction=Next|Previous&pageSize` (1–100) | `fetchApprovalContinuation` | Valida el pedido antes de enviarlo; la respuesta exige ids únicos, distintos del ancla, del mismo formulario, abiertas, decidibles y sin motivo de bloqueo, posiciones contiguas ≤ total, `hasMore` con lote lleno; el cliente compara ancla, formulario y dirección con lo pedido |
| `POST /api/forms/definitions/{formId}/records/{recordId}/navigation` (cuerpo = la misma consulta que la búsqueda de la planilla) | `fetchResponseRecordNavigation` en `src/features/business-forms/api/responses.ts` | `{ recordId, previousId|null, nextId|null, position, totalCount }` con coherencia (null en los extremos, sin repetir ids); el cliente valida los GUID y que `recordId` sea el pedido |
| `POST /api/forms/definitions/{formId}/records/search` | `fetchResponseSheet` | Fila suma **`hasPdf`** |
| `GET /api/forms/records/{id}/workflow` | `fetchResponseWorkflow` | Tarea suma **`requestedAtUtc`**; cada documento suma **`runId`** |
| `GET /api/forms/records/{id}` | `fetchResponseDetail` | `origin` suma `senderKind: Manual | Assistant`; `assistance` y `chain` (sección 3) |

**Retirados:** `GET /api/forms/navigation` (enlaces por formulario del menú) y `GET /api/forms/approvals/options`
(desplegable de la bandeja global). Los errores de contrato lanzan `Error("Invalid form … contract.")` y el dueño los
muestra como fallo de lectura.

### 5.10 Cohortes y fixtures exportadas

Mecanismo general en la sección 11. Lo propio de esta parte:

| Cohorte (carpeta, variable) | Cuerpos de esta etapa | Productores del backend | Consumidores |
| --- | --- | --- | --- |
| `responses` (`src/test/fixtures/forms-responses-http`, `FORMS_RESPONSES_HTTP_CONTRACT_INPUT`) | `overview-{read,decide,combined,pending,deactivated}-{es,en}.json` (10), `record-navigation-{first,middle,last,singleton}-{es,en}.json` (8), `state-{received,pendingapproval,approved,rejected}-d2-es.json` y sus `export-state-*-d2.xlsx`, `approval-sheet-{es,en}.json`, `files-detail-without-chat-d2-es.json`. La cohorte entera tiene 55 capturas | `FormOverviewHttpTests`, `FormRecordNavigationHttpTests`, `FormResponseStateHttpTests`, `FormResponseApprovalHttpTests` y los anteriores | `src/features/business-forms/api/overviewHttpContract.test.ts`, `src/features/business-forms/api/recordNavigationHttpContract.test.ts`, `src/features/business-forms/api/responseHttpContract.test.ts` |
| `approvals` (`src/test/fixtures/forms-approvals-http`, `FORMS_APPROVALS_HTTP_CONTRACT_INPUT`) | `approval-continuation-{next,previous,closed}-{es,en}.json` (6) más tareas, decisiones, conflictos y conteo (17 en total; `options` se retiró) | `FormApprovalContinuationHttpTests`, `FormApprovalHttpTests` | `src/features/business-forms/api/approvalContinuationHttpContract.test.ts`, `src/features/business-forms/api/approvalsHttpContract.test.ts` |
| `workflow` (`src/test/fixtures/forms-workflow-http`, `FORMS_WORKFLOW_HTTP_CONTRACT_INPUT`) | `approval-*-d2-*.json` (5) para los momentos, `role-delivered-read-*`, `role-late-accepted-read-only-*` (32 en total) | `FormDetailMomentsHttpTests`, `FormWorkflowHttpTests` y otros | `src/features/business-forms/api/workflowHttpContract.test.ts` |
| `pdf` (`src/test/fixtures/forms-pdf-http`, `FORMS_PDF_HTTP_CONTRACT_INPUT`) | Copias con companions `.http.json` (32) | `FormPdfHttpTests` y otros | `src/features/business-forms/api/pdfHttpContract.test.ts` |

Las cuatro cohortes salen de la misma corrida backend: run `20261023-064235-5542f7f13d844d5985f55c4142c9385f`, HEAD
`7e5b7a94`. Los consumidores verifican bytes, tamaño y SHA256 contra `provenance.json`, validan cada cuerpo con el guard
real (`toEqual(raw)`: el guard no agrega ni quita nada) y comprueban las proyecciones por permiso (por ejemplo, con solo
decisión `responseCount` y `lastResponse` son null; con solo lectura, `pendingApprovalCount` y `deactivatedPending`).

**Dobles de las escenas visuales** (sintéticos, declarados así en el código): `src/test/visual/formOverviewFixtures.ts`
(escenarios `forms-overview`, `-deactivated`, `-read`, `-decide`) y `src/test/visual/formResponseFullFixtures.ts`
(`responses-full`, `-read-decide`, `-decide`, `-multiple`), más `approvalFixtures.ts` y `responseFixtures.ts`.
`src/test/formResponseFullFixture.test.ts` pasa el recorrido sintético por los guards reales y comprueba que las
escrituras devuelvan 405.

### 5.11 i18n

- `common.json` (`navigation`): `forms` «Formularios» → **«Gestión de formularios»** («Form management»); nuevos
  `formOverview` «Formularios» («Forms»), `missingForms` y `personData`; retirados `allResponses`, `allResponsesCount` y
  `approvals`. Nuevo `actions.view` «Ver».
- `forms.json`:
  - `overview.*` (nuevo, 23 claves): título, búsqueda, segmentado, columnas, «Con algo por aprobar», avisos de dados de
    baja con plural `_one`/`_other`, vacíos por perfil, «Por aprobar · N», «X de N por aprobar» y la ayuda de teclado.
  - `detail.*` (nuevo): «Abrir en pantalla completa», «Volver a las respuestas», plegar/expandir, «N preguntas»,
    «Actividad», «Fotos», «PDF», eventos «Al recibirla»/«Al aprobarla»/«Al rechazarla», «N de M hechas», «Se pidió
    aprobación al rol {rol}», «Ver PDF de {número}», origen manual o asistente, «↑ ↓ otra respuesta · Esc cerrar», «Hay
    fallas o envíos sin confirmar.». `detail.assistance.*` es de la sección 3.
  - `approvals`: nuevo `loadFailed` «No pudimos cargar las aprobaciones.»; retirados `title`, `scope`, `mine`, `all`,
    `form`, `allForms`, `waiting`, `empty`, `back`; `taskStates.Open` y `states.PendingApproval` pasan a «Por aprobar».
  - `workflow.delivered` «Entregado» y `workflow.read` «Leído».
  - `rules.*` de 5.2 (`saveUnchanged`, `saveInvalid`, `saveConflict`, `saveBusy`, `keepMineHint`, `history*`).
  - Retirado `responses.searchForms`.
- Paridad es/en verificada por `src/locales/parity.test.ts` (no se corrió acá).

### 5.12 Pruebas

**Unitarias y de componentes** (Vitest; cantidad de casos contada en `982ade8`):

| Archivo | Casos | Qué cubre |
| --- | --- | --- |
| `src/features/business-forms/pages/FormOverviewPage.test.tsx` | 7 | Facetas del servidor, «Ver dados de baja» quita `pendingOnly`, sin permisos no lee, 403 purga sin releer, un solo enlace con el conteo global, conteo diferido al abrir el menú, textos en inglés |
| `src/features/business-forms/pages/FormPendingApprovals.test.tsx` y `FormPendingApprovals.regressions.test.tsx` | 3 + 14 | Motivo obligatorio y recortado, fallo con un solo aviso, error del motivo junto al campo, 409 sin segunda decisión, cola tras compactarse, cruce de páginas con flechas y foco al cerrar, 403 al pedir la página siguiente, cancelación de una tarea denegada, referencias compartidas con la pantalla completa |
| `src/features/business-forms/pages/ResponsePanel.test.tsx` | 25 | Marco D2, teclado, foco, pestañas, selección, pendientes |
| `src/features/business-forms/pages/FormResponseFullPage.test.tsx` | 20 | Ir y volver a la misma fila con filtros privados, enlace compartido fuera de la página 1, solo decisión sin pantalla completa, sucesor tras decidir, flechas en la cola sin publicar filtros, 403 de pendientes, paso de escritorio a celular, memoria limpia en enlace compartido, respuesta anterior ocupada, PDF en su acción con el mismo blob, documentos en el menú, 403 del PDF, migas |
| `src/features/business-forms/pages/FormResponsesPage.test.tsx` | 61 | Planilla, filtros (incluido aplicar sin conteo), estado, PDF por fila |
| `src/features/business-forms/api/overview.test.ts`, `recordNavigation.test.ts`, `approvalContinuation.test.ts` | 4, 6, 5 | Guards y clientes con cuerpos inválidos |
| `src/features/business-forms/hooks/usePendingApprovalNavigation.test.tsx` | 9 | Cola, continuación, merges, cancelación |
| `src/features/business-forms/responseNavigationPaths.test.ts` | 4 | Parámetros públicos de las rutas |
| `src/features/business-forms/workflowHistory.test.ts`, `workflowMoments.test.ts` | 6, 11 | Orden estable y momentos |
| `src/features/business-forms/components/ApprovalDecisionPanel.feedback.test.tsx`, `ResponseWorkflowHistory.test.tsx`, `FormPdfPreview.test.tsx` | 3, 3, 1 | Avisos de la decisión, historia, descarga del mismo blob |
| `src/features/business-forms/hooks/useUnobservedQueryCleanup.test.tsx` | 3 | Retira solo lo no observado |
| `src/layouts/components/Sidebar.permissions.test.tsx` | 4 (un `it.each`) | Regla alternativa de permisos en el menú |
| `src/layouts/components/Breadcrumbs.test.tsx`, `src/layouts/navigation.test.ts`, `src/auth/ProtectedRoute.test.tsx` | 11, 15, 8 | Migas de D2 y completa, marcado activo, permisos alternativos |

**Contratos HTTP:** los de 5.10 (`overviewHttpContract` 2, `recordNavigationHttpContract` 2,
`approvalContinuationHttpContract` 5).

**Escenas visuales** (Playwright, `src/test/visual`; dejan capturas en `test-results/` y comprueban geometría; **no
comparan contra referencias**: las 127 referencias con `toHaveScreenshot` son de otras pantallas):
- `formOverview.spec.ts`: MF1 Activos y Dados de baja en es/en a 1440/390/320, todas las entradas del menú, «Con algo
  por aprobar» con la baja marcada y sin segmentos presionados, perfiles lectura y decisión a 390/320; exige tabla
  contenida y que no haya escrituras.
- `formResponseFull.spec.ts`: rutas reales DC0/DC1 y PDF fijo, teclado en pendientes con lectura y decisión, y solo
  decisión sin pantalla completa, en es/en a 1440/390/320.
- `responses.spec.ts`: planilla, D2 (pestañas Fotos y Actividad, flechas, Escape, foco), búsqueda privada fuera de la
  URL y estados (vacía, inexistente, sin permiso, límite de exportación).
- `formApprovals.spec.ts`: pendientes por formulario con solo decisión (rechazar, aprobar, foco) y decisión desde una
  respuesta legible.
- `formWorkflow.spec.ts` y `formPdf.spec.ts`: historia sin confirmar y PDF fijo.

Evidencia del cierre: [cierre local de menú y detalle](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#cierre-local-de-menú-y-detalle--2026-10-21)
y [verificación posterior al review](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#verificación-interactiva-posterior-al-review-de-menú--2026-10-21).
Los resultados (617/617 y 623/623 escenas) son los que declaran esos documentos; no se volvieron a correr.

### 5.13 Lo que quedó del review de menú y detalle

El [review del 2026-10-21](../../reviews/2026-10-21-code-review-menu-y-detalle.md) cerró sus puntos 1–10 (P3/P4, P7–P10
en el front, verificados en `982ade8`). De «Después, sin bloquear», en el front sigue así:

| Punto | Estado en `982ade8` |
| --- | --- |
| El número del menú es texto «Formularios (4)», no insignia | Sigue igual (`FormsOverviewLink` en `src/layouts/components/Sidebar.tsx`) |
| «(N)» junto a «Con algo por aprobar» y resumen «N formularios» | No existen |
| Conteos del segmentado con el filtro activo | Muestra lo que da `filterCounts`; si el servidor los recalcula con `pendingOnly` no se verificó |
| Resúmenes de DC1 plegado con preguntas, fotos y documentos, y «N momentos · estado» | Siguen como «N preguntas» y la lista de momentos unida por « · » |
| Bloque «Por aprobar» vacío arriba de la planilla para quien lee y decide | Se sigue dibujando con su vacío |
| El contador del menú no declara su manejo de errores y podría avisar dos veces | `useApprovalCount` no tiene `errorOwner` local (avisa el `QueryCache`) y el menú suma «Reintentar»; doble aviso no verificado |
| Ancla que da 404 si la respuesta deja de cumplir un filtro privado | Es del servidor; no verificable en el front |

### 5.14 Para el multitenant

- **Dueño por organización:** todas las claves usan `id:idioma:permisos` (y `:read|denied` en respuestas). Con tenants,
  el dueño tiene que incluir la organización activa; si una persona cambia de organización sin recargar, la memoria del
  formulario (`ResponseWorkspaceProvider`) y el contador del menú tienen que reiniciarse igual que hoy al cambiar de
  cuenta.
- **Permisos Read y Decide independientes:** conservar las tres personas (solo lee, solo decide, ambos) y sus
  proyecciones con null. En B2C conviene decidir si existe "decidir sin leer"; si no existe, se simplifican D2 de
  metadata y la pantalla de pendientes propia.
- **Pendientes por rol vigente:** el número del menú y «Por aprobar · N» dependen de roles; con roles por tenant, la
  continuación por TaskId y el anclaje tienen que autorizar dentro de la organización.
- **Nada privado en la URL:** la regla vale doble con datos de clientes finales: los enlaces compartibles solo llevan
  orden y versiones.
- **La pantalla de entrada es costosa:** el backend tuvo que proyectar nombre e idiomas en SQL (punto 2 del review);
  con muchos tenants y formularios, medir antes.
- **No copiar** los textos al lado de botones, «Cambios sin guardar» y revisiones de 5.2, ni las acciones en segunda
  fila del panel D2 (5.6.1).
- **Nombre del grupo y del cierre del panel:** unificar («Gestión de formularios» vs «Cerrar el panel Formularios»).
- **Dirección vieja `/formularios/por-aprobar`:** no existe en el multitenant; no hace falta redirigirla.

### 5.15 Endpoints nuevos o cambiados

- Nuevos: `GET /api/forms/overview`, `POST /api/forms/definitions/{formId}/records/{recordId}/navigation`,
  `GET /api/forms/approvals/{taskId}/continuation`.
- Cambiados: `GET /api/forms/approvals` (siempre con `definitionId`), `POST …/records/search` (fila con `hasPdf`),
  `GET /api/forms/records/{id}/workflow` (`task.requestedAtUtc`, `documents[].runId`), `GET /api/forms/records/{id}`
  (`origin.senderKind`), `GET /api/forms/definitions/{id}/rules/revisions` (`changes`, de 5.2).
- Retirados del front: `GET /api/forms/navigation`, `GET /api/forms/approvals/options`.

### 5.16 Huecos

- No se corrieron builds, tests ni escenas: las cifras de pruebas son conteos de casos en el código o las que declaran
  los documentos de cierre.
- Qué muestra la dirección vieja `/formularios/por-aprobar` (cae en la ruta del editor): no verificado.
- Si el servidor recalcula los conteos del segmentado de MF1 con `pendingOnly`: no verificado desde el front.
- La geometría a 390/320 sale del código responsive, de las aserciones de las escenas y de la evidencia; no se miró en
  un navegador.
- El doble aviso del contador del menú (QueryCache más «Reintentar») es plausible; no se reprodujo.
- `ResponsePanel.test.tsx` y `FormResponsesPage.test.tsx` se resumieron por su alcance, sin leer cada caso.

---

## 6. Ciclo de vida: dar de baja, reactivar y eliminar (BA1–BA6)

Relevado sobre el commit `982ade8` del front (leído con `git show`, sin el árbol de trabajo). Reglas funcionales del
back: [dar de baja, reactivar y eliminar](../../features/formularios.md#dar-de-baja-reactivar-y-eliminar--2026-10-21) y
su [verificación local](../../features/formularios.md#verificación-local-del-ciclo-de-vida--2026-10-21). Tableros y
evidencia: [diseño del área](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#cierre-local-de-baja-reactivación-y-eliminación--2026-10-21)
y [verificación posterior al review](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#verificación-posterior-al-review-de-baja--2026-10-21).
Review que corrigió la primera versión: [code review de dar de baja](../../reviews/2026-10-21-code-review-dar-de-baja.md).

### 6.1 Resumen

**Qué cambió desde `9261e3d`.** En el traspaso anterior no existía nada de esto: el listado tenía «Todos · Publicados ·
Borradores» y el editor solo publicaba. Ahora:

- **Listado «Diseñar formularios» (BA1-A):** el segmentado pasa a «Activos (N) · Publicados (N) · Borradores (N) · Dados
  de baja (N)», con «Activos» como valor predeterminado (publicados + borradores, sin los dados de baja). Suma la columna
  **Respuestas**, el estado «Dado de baja · versión N» con lo que pasa en WhatsApp debajo, y tres acciones de fila nuevas:
  **Dar de baja**, **Reactivar** y **Eliminar**.
- **Editor (BA2):** un menú «⋯» («Acciones del formulario») después de la acción principal, con Dar de baja o Reactivar y
  Eliminar; los ítems imposibles quedan apagados con su motivo, alcanzable por teclado. Un aviso de «dado de baja» con
  quién y cuándo. Mientras está dado de baja, el botón principal dice **Reactivar** en lugar de «Publicar en WhatsApp».
- **Diálogos:** «Dar de baja» (BA3) con el impacto leído del servidor; Reactivar (BA4) es el mismo diálogo de comprobación
  de publicación en modo reactivación; «Eliminar» (BA4) con el prefijo reservado y la advertencia de WhatsApp solo si
  se publicó. Los tres usan la variante `mobileFullscreen` del diálogo en el celular.
- **Sugerencias (BA6):** una sugerencia que apunta a un formulario dado de baja o eliminado se ve con la etiqueta
  «Dado de baja» o «Eliminado», solo se puede quitar y bloquea «Guardar sugerencias».
- **Estado del canal:** Pending, Uncertain, NeedsAttention («Requiere atención») y Deprecated, siempre con la evidencia
  del servidor; nada se infiere en el front.
- **Contrato:** el detalle y cada fila del listado llevan un objeto `lifecycle` obligatorio; las facetas pasan a cuatro
  conteos. Por eso se regeneraron todas las cohortes HTTP que traen detalles o filas.

**Tableros.** BA1-A (acciones en la fila; BA1-B, ficha al costado, no se eligió), BA2 editor, BA3 dar de baja, BA4
reactivar y eliminar, BA5 circuito de punta a punta (solo guía) y BA6 sugerencias hacia un dado de baja, todos con los
ajustes de la versión 96 del lienzo (2026-10-20):

| Tablero | Captura elegida |
| --- | --- |
| BA1-A | [baja-ba1-a-fila.png](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-ba1-a-fila.png) |
| BA2 | [baja-ba2-editor.png](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-ba2-editor.png) |
| BA3 | [baja-ba3-dar-de-baja.png](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-ba3-dar-de-baja.png) |
| BA4 | [baja-ba4-reactivar.png](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-ba4-reactivar.png) |
| BA5 | [baja-ba5-circuito.png](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-ba5-circuito.png) |
| BA6 | [baja-ba6-sugerencias.png](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-ba6-sugerencias.png) |

**Commits clave** (2026-10-20 a 2026-10-21):

| Commit | Asunto |
| --- | --- |
| `0b0c0a3`, `593c1d8`, `5db98e8` | docs: tableros elegidos de dar de baja y del menú; paginación del filtro en BA1-A; ajustes de los tableros y BA6 |
| `fbb17cf` | feat(ui): permitir diálogos completos en móvil (`mobileFullscreen`, ícono `MoreVerticalIcon`) |
| `ba95e0c` | feat: dar de baja y reactivar sin perder el borrador |
| `dae7cf7` | feat: eliminar sin respuestas con confirmación revisada |
| `f64f512` | feat: mostrar la deprecación confirmada por WhatsApp |
| `c45cbb7`, `9854ac6` | test: escenas de baja y eliminación; confirmación de eliminar en móvil |
| `589ff76`, `72a3947` | fix: explicar por qué no se elimina con respuestas; motivo neutral |
| `a031c11` | test: completar los dobles de publicación con `lifecycle` |
| `91cfd6e`, `a74aca3`, `15ff4d5`, `d20697e` | test: exportación HTTP conjunta, cohorte `forms-lifecycle-http` y su consumidor |
| `65ed60a`, `aec9281`, `d040fda` | docs: evidencia visual del ciclo de vida y cierre del review |
| `02c5076` | fix: refrescar navegación tras baja y eliminación (review, punto 4) |
| `08fffcf` | fix: explicar baja sin envíos abiertos (punto 5) |
| `2bedb9f` | fix: releer bajas y eliminaciones inciertas (punto 6) |
| `55a7b1d` | fix: refrescar siempre una baja confirmada (punto 7) |
| `58f1922` | fix: respetar capacidad de publicación en el menú (punto 8) |
| `77f0066` | fix: completar confirmación de eliminación (punto 9) |
| `11dd92e` | fix: conservar espaciado al esperar publicación (punto 10, la escena «P10») |
| `00c3f93` | fix: motivos de acciones apagadas accesibles (punto 11, la escena «P11») |
| `f27c540` | feat: mostrar deprecación que requiere atención (punto 1 del back, NeedsAttention) |
| `fc306b7` | fix(whatsapp): mantener el envío privado fuera de mutaciones (punto 14; es del chat, no del ciclo de vida) |
| `4a7fd7d` | refactor: refrescar también el resumen MF1 (`form-overview`) tras baja o eliminación |

«P10» y «P11» en la evidencia del diseño son los **puntos 10 y 11 del review de baja**, no tableros nuevos.

### 6.2 Navegación y rutas

No hay rutas nuevas. Todo vive en las rutas que ya existían:

| Ruta | Pantalla | Permiso de la ruta | Archivo |
| --- | --- | --- | --- |
| `/formularios` | Diseñar formularios (listado BA1-A) | `formtemplates.read` | `src/features/business-forms/pages/FormsPage.tsx` |
| `/formularios/:formId` | Editor (BA2, diálogos BA3/BA4, BA6) | `formtemplates.read` | `src/features/business-forms/pages/FormEditorPage.tsx` |

- **Migas:** `Inicio › Gestión de formularios › Diseñar formularios` y, en el editor,
  `… › Diseñar formularios › {nombre en el idioma original}`. El grupo del menú se llama «Gestión de formularios» desde
  MF1 (sección 5).
- **En la URL** (listado): `page`, `pageSize`, `sort`, `search` y `publicationFilter` = `Published`, `Drafts` o
  `Deactivated`; **ausente = Activos**. Cambiar el filtro vuelve a la página 1. Antes del cambio, ausente era «Todos».
- **Nunca en la URL ni en el almacenamiento:** el diálogo abierto, el impacto leído, la revisión esperada y la
  **intención de reactivar**. Esta última viaja en el `state` del historial (`{ reactivateFormId }`) al tocar
  «Reactivar» en una fila. El editor la consume una sola vez y la borra con
  `navigate(pathname + search, { replace: true, state: null })`, así que recargar o volver no reabre el diálogo.
- El MF1 («Formularios», `/formularios/respuestas`) tiene su propio segmentado «Activos · Dados de baja». Lo cuenta la
  sección 5; acá solo se invalida su caché tras una baja o eliminación.

### 6.3 Diseñar formularios: el listado (BA1-A)

`src/features/business-forms/pages/FormsPage.tsx`. Lo que no cambió (cabecera, buscador, orden, paginado, estados de
carga y error) sigue como en el anexo anterior (E1.1); acá va lo vigente completo, con el delta marcado.

**Cabecera (`Page`):** título «Diseñar formularios»; a la derecha, «Nuevo formulario» (enlace a `/formularios/nuevo`) solo
con `formtemplates.manage`.

**Barra de filtros (`FilterBar`), una fila que se parte en el celular:**
1. Buscador «Buscar por nombre» (`w-full` en el celular, 18 rem desde `sm`).
2. Control segmentado «Estado en WhatsApp», dentro de una caja con `overflow-x-auto`:
   **«Activos (N)» · «Publicados (N)» · «Borradores (N)» · «Dados de baja (N)»**. Los cuatro conteos vienen del servidor
   (`/facets`) y dependen solo de la búsqueda; mientras no llegan se ve «…». Inglés: «Active · Published · Drafts ·
   Deactivated».

**Tabla** (`Surface` sin banda + `DataTable` + `Pagination`), columnas en este orden:

| Columna | Contenido | Ordenable |
| --- | --- | --- |
| Formulario | Enlace al editor. Nombre en el idioma de la interfaz o, si está vacío, en el otro (`administrativeName`) | Sí |
| Preguntas | Cantidad | Sí |
| En WhatsApp | Si **no** está dado de baja: la etiqueta de publicación de siempre («Borrador», «Publicado · versión N»…) y, si hay una versión activa y el estado no es Publicado, «La versión N publicada sigue activa.». Si **está dado de baja**: «Dado de baja · versión N» (o «Dado de baja» sin versión activa); si hay una reactivación en curso (`unavailableReason = PublicationInProgress`), debajo «Reactivando con una versión nueva…»; y debajo, en chico y con ancho máximo de 16 rem, la línea del canal (6.9). La deprecación **solo** se rotula en los dados de baja | No |
| **Respuestas** (nueva) | `lifecycle.responseCount`, alineada a la derecha; **null se muestra «—»**, nunca 0 | No |
| Último cambio | «{fecha} · {autor}» | Sí |
| Acciones | `RowActions` (ver abajo) | — |

**Acciones de fila** (botones de ícono con nombre accesible «{acción} {nombre}»; lo que no se puede, no se dibuja):

| Acción | Ícono | Se ve si | Qué hace |
| --- | --- | --- | --- |
| Editar | lápiz | `formtemplates.manage` y `row.canEdit` | Navega al editor |
| Dar de baja | prohibido (`BanIcon`) | `formtemplates.publish` y `lifecycle.canDeactivate` | Abre BA3 sobre el listado |
| Reactivar | flechas (`RefreshIcon`) | `formtemplates.publish` y `lifecycle.canReactivate` | Navega al editor con `state.reactivateFormId`; el editor abre la comprobación (6.6). **No publica sola** |
| Eliminar | papelera, destructiva, última | `formtemplates.manage`, `lifecycle.canDelete` y **sin respuestas** (`deletionUnavailableReason ≠ HasResponses` y `responseCount` null o 0) | Abre el diálogo Eliminar (6.7) |

Ningún permiso implica otro: Dar de baja y Reactivar piden **publicar**, Editar y Eliminar piden **administrar**. Las
capacidades `canDeactivate`, `canReactivate` y `canDelete` vienen del servidor; el front no las deduce.

**Estados:**
- Cargando: spinner en la tabla.
- Vacío sin filtros (Activos y sin búsqueda): «Todavía no hay formularios» / «Creá el primero para empezar.» + «Nuevo
  formulario» con manage.
- Con filtro distinto de Activos o con búsqueda, sin filas: «Sin coincidencias» / «No encontramos formularios con estos
  filtros.» + «Limpiar filtros», que vuelve a Activos y borra la búsqueda. Elegir «Dados de baja (0)» cae acá.
- Error de lista o facetas: en la tabla «No pudimos cargar los formularios.» + «Reintentar», y un único toast con la
  causa (`readErrorMessage`); si ya había datos del mismo dueño, se conservan.
- 403 en la lista o en las facetas, o sin `formtemplates.read`: `ForbiddenPage`.
- 409 y 429: no aplican a la lectura; los de las acciones se ven en sus diálogos.
- Si cambia el dueño (actor, permisos o idioma) con un diálogo abierto, el diálogo se cierra solo (`action.owner`).

**390 y 320 px** (escena `BA1 lifecycle table and contained scrolling`, evidencia
[BA1-A a 390](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-20261021-ba1-es-390.png) y
[NeedsAttention a 320](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-review-20261021-needsattention-list-es-320.png)):
el título ocupa el ancho y «Nuevo formulario» baja alineado a la derecha; el buscador ocupa todo el ancho; el segmentado
se desplaza **dentro de su caja** («Dados de baja» queda fuera de vista a 390 hasta desplazarlo); la tabla se desplaza a
lo ancho dentro de su contenedor (`data-slot="table-container"`) y las acciones quedan a la derecha de todo. La página
nunca tiene desplazamiento horizontal (la escena lo comprueba con `scrollWidth ≤ ancho`).

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), commit `bb98bf2`):
> el listado en sí cumple la regla (acciones a la derecha, sin textos al lado de los botones). La línea «Reactivando con
> una versión nueva…» y la del canal son **estado del objeto**, no explicaciones de un botón: se pueden conservar.

### 6.4 El editor: cabecera, menú «⋯» y aviso de dado de baja (BA2)

`src/features/business-forms/pages/FormEditorPage.tsx`, `src/features/business-forms/components/FormLifecycleMenu.tsx`,
`src/features/business-forms/components/FormLifecycleNotice.tsx`, `src/features/business-forms/lifecyclePresentation.ts`.

**Cabecera**, de izquierda a derecha:
- Flecha «Diseñar formularios», título con el nombre en el idioma original.
- **Estado** (insignia): si está dado de baja, «Dado de baja · versión N» (o «Dado de baja»), sin color de alerta y
  **sin** el estado de guardado; si no, las etiquetas de siempre (publicación, «Guardado sin confirmar», «Cambios sin
  guardar» o «Borrador»).
- **Acciones**, en este orden:
  1. «Descartar» (como antes).
  2. «Guardar borrador» (como antes, con manage). **Un dado de baja se sigue pudiendo editar y guardar:** la baja no toca
     el borrador.
  3. Botón principal: **«Reactivar»** si está dado de baja, «Publicar en WhatsApp» si no. Se ve con
     `formtemplates.publish` + `capabilities.canPublish` + formulario guardado una vez. Está apagado si el formulario fue
     eliminado mientras se editaba, si está dado de baja y `canReactivate` es false (por ejemplo, con una reactivación en
     curso), con el guardado congelado o en curso, o con una publicación aceptándose o sin confirmar.
  4. **Menú «⋯»** (`IconButton` con nombre «Acciones del formulario», menú alineado al final). Se dibuja si hay `lifecycle`
     y la persona tiene publish **o** manage, y nunca vacío.
  5. «Ver vista oficial» (como antes).

**Contenido del menú «⋯»:**

| Ítem | Se ve si | Apagado (con `aria-disabled`) cuando | Motivo visible dentro del ítem |
| --- | --- | --- | --- |
| «Dar de baja» o «Reactivar» (según esté dado de baja) | `canPublish` (el mismo criterio que el botón principal, `58f1922`) | `canDeactivate`/`canReactivate` false, o el editor está bloqueado (eliminado, congelado, ocupado, publicación pendiente) | `lifecycle.reasons.{PublicationInProgress \| NotPublished \| Unavailable}`: «Hay una publicación en curso.», «Primero hay que publicar el formulario.», «La acción no está disponible.» |
| Separador + «Eliminar» | `formtemplates.manage` y sin respuestas | `canDelete` false o editor bloqueado | `lifecycle.deletionReasons.{PublicationInProgress \| OpenSends \| Unavailable}`: «Hay una publicación en curso.», «Tiene envíos vigentes o respuestas en proceso.», «No se puede eliminar este formulario.» |
| Etiqueta no accionable (`DropdownMenuLabel`) | manage y **con** respuestas | — | «Tiene respuestas y no se puede eliminar.» |

- Sin publish y con respuestas, el menú **no existe** (no hay menú con solo una etiqueta).
- Los ítems apagados usan `aria-disabled` en lugar de `disabled`: se alcanzan con las flechas, se leen con el motivo, y
  elegirlos no ejecuta nada ni cierra el menú (`event.preventDefault()`, `00c3f93`). `dropdown-menu.tsx` suma el estilo
  `aria-disabled:opacity-50`.
- «Reactivar» desde el menú abre el mismo diálogo que el botón principal; el foco vuelve al menú «⋯» al cerrarlo.

**Aviso de dado de baja** (arriba del contenido, solo si `deactivatedAtUtc`):
- `Banner` informativo: «Dado de baja por {autor} el {fecha y hora en la zona del perfil}.» (autor desconocido: «Persona
  sin información»), «No se manda desde el chat y el asistente no lo ofrece.», «Los PDF, las aprobaciones pendientes y
  las acciones de reglas se conservan.» y, en chico, la línea del canal si es Pending o Deprecated.
- Si el canal es Uncertain o NeedsAttention, la línea va aparte en un `Banner` de advertencia (6.9). No hay acción nueva.
- No muestra la cantidad de respuestas (desvío aceptado en el review).
- Mientras está dado de baja, la superficie de progreso de la publicación se oculta, salvo que haya una aceptación en
  curso o sin confirmar, o una versión más nueva que la activa (es decir, una reactivación en marcha).

**Formulario eliminado mientras se edita:** si un guardado responde 404 `Forms.Definition.NotFound`, el editor queda en
solo lectura (`knownDeleted`), sale un toast «El formulario ya fue eliminado. Tus cambios sin guardar se conservan.»,
Publicar y el menú se apagan y no hay un ciclo de recuperación.

**Estados de carga del editor** (sin cambios salvo la intención de reactivar): mientras se lee el detalle, y además
**mientras no hay una lectura fresca cuando llegó con la intención** (`isFetchedAfterMount`), se ve un spinner; así
no se abre la reactivación sobre un detalle activo que estaba en caché. 404: «No encontramos este formulario.» con
«Reintentar»; otro error: «No pudimos cargar el formulario.»; 403: `ForbiddenPage`.

**390 y 320 px** (evidencia [BA2 a 320, en](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-20261021-ba2-en-320.png),
[NeedsAttention a 390, en](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-review-20261021-needsattention-editor-en-390.png),
[menú apagado a 320, en](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-review-20261021-unavailable-menu-en-320.png)):
el título ocupa el ancho, la insignia baja a otra línea y las acciones se parten en dos filas alineadas a la derecha. A
390: «Descartar · Guardar borrador · Reactivar · ⋯», y debajo «Ver vista oficial». A 320: «Descartar · Guardar borrador
· Reactivar», y debajo «⋯ · Ver vista oficial». El menú se abre pegado al borde derecho y la etiqueta de «tiene
respuestas» se parte en varias líneas (ancho máximo de 16 rem).

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), commit `bb98bf2`):
> - La insignia del editor todavía muestra «Cambios sin guardar» y «Guardado sin confirmar» cuando el formulario
>   **no** está dado de baja (`t("dirty")` y `t("uncertain")` en `FormEditorPage.tsx`). La regla nueva dice que el estado
>   muestra solo el del objeto («Activo», «Dado de baja»). El punto 38 del review E2 no nombra `FormEditorPage`, pero
>   aclara que su lista no es completa.
> - El menú «⋯» muestra el motivo de los ítems apagados como texto visible dentro del ítem. La
>   [regla funcional](../../features/formularios.md#dar-de-baja-reactivar-y-eliminar--2026-10-21) del 2026-10-21 lo pide
>   así («texto no accionable»), y la regla del 2026-10-23 pide **tooltip** para el motivo de un botón deshabilitado.
>   Dentro de un menú desplegable no es un texto al lado de un botón, pero conviene confirmarlo con el usuario antes de
>   copiarlo.
> - Con dos filas de acciones en el celular, el «⋯» queda en el medio de la cabecera según el ancho. Revisarlo contra
>   «las acciones van al borde derecho, a la altura del título».

> **No copiar — regla de idioma del 2026-10-23** (review E2, punto 35): las capturas del editor dado de baja muestran
> todavía el selector «Textos en» («Content language»). Se retira (sección 4); el ciclo de vida no depende de él.

### 6.5 Diálogo «Dar de baja» (BA3)

`src/features/business-forms/components/FormDeactivationDialog.tsx`. Se abre desde la fila (listado) o desde el menú
«⋯» (editor). Requiere `formtemplates.publish`.

**Al abrir** lee el impacto: `GET /api/forms/definitions/{id}/deactivation` (`cache: "no-store"`, `gcTime: 0`, se cancela
y se retira al cerrar).

**Cabecera:** «Dar de baja «{nombre}»» / «Revisá qué cambia antes de confirmar. Lo cargado se conserva.»

**Cuerpo** (desplazable), por secciones con título chico:
1. **«Qué deja de pasar»**, tres `Banner` informativos:
   - «No se manda más desde el chat» / «Sale del selector «Enviar formulario».»;
   - «El asistente no lo ofrece» / «Si alguien lo pide, se anota en «Formularios que faltan».»;
   - solo si hay sugerencias entrantes de conjuntos activos: «Deja de ofrecerse 1 sugerencia» / «Dejan de ofrecerse N
     sugerencias», con los nombres de los formularios **que sugieren** a este (`affectedSuggestions`, en el idioma de la
     interfaz con respaldo).
2. **«Envíos abiertos y WhatsApp»**, solo si hay un vencimiento de envíos abiertos o un estado de canal:
   - con vencimiento: «Los envíos abiertos se pueden responder hasta que vencen.» + «El último envío abierto vence el
     {fecha y hora en la zona del perfil}.»;
   - con estado de canal, un `Banner` de advertencia: con envíos abiertos, «En WhatsApp no se deshace. Al vencer los
     envíos abiertos, se deprecan sus Flows. Para volver a usarlo, se reactiva con una versión nueva.»; **sin envíos
     abiertos** (el caso común), «En WhatsApp no se deshace. No hay envíos abiertos: se deprecan ahora sus Flows. …»
     (`08fffcf`).
   - Sin Flows confirmados (`deprecationState` null) no se afirma nada sobre WhatsApp.
3. **«Lo que se conserva»**: «Se conservan N respuestas.» (o «Las respuestas se conservan.» si el conteo es null,
   sin lectura de respuestas) + «Con sus fotos y documentos, en la planilla y en el Excel.»; y «Los PDF, las aprobaciones
   pendientes y las acciones de reglas se conservan.» + «Las N aprobaciones pendientes se pueden seguir decidiendo.»
   (solo si hay).
4. Si una baja quedó sin confirmar y la relectura falló: un botón «Reintentar» (relee el impacto).
5. Un párrafo final, si corresponde: «Cambió la revisión guardada. Revisala antes de dar de baja el formulario.»
   (revisión del impacto distinta de la del borrador del editor), o el motivo por el que no se puede
   (`lifecycle.reasons.*` o «El formulario ya está dado de baja.»).

**Pie:** «Cancelar» + «Dar de baja» / «Dando de baja…». Si la revisión cambió y el diálogo viene del editor, el botón
principal pasa a ser **«Revisar»**: cierra el diálogo y abre la revisión del borrador del editor (un 409 sintético
`Forms.Draft.RevisionConflict`, el flujo D de siempre). «Dar de baja» se apaga mientras trabaja, con una relectura
pendiente, sin permiso o con `canDeactivate` false. Mientras trabaja no se puede cerrar (ni con la X ni con Escape).

**Estados:**
- Cargando: «Consultando el impacto…» con spinner.
- Error de lectura: estado vacío «No pudimos cargar los datos» + «Reintentar», y **un** toast con la causa.
- 403 (de la lectura o del POST): estado vacío «Ya no tenés permiso para dar de baja este formulario.»; confirmar queda
  apagado.
- 409 u otro rechazo del POST: toast con el `detail` del servidor y relectura del impacto; el diálogo queda abierto.
- Red o 5xx en el POST (resultado incierto): toast «No pudimos confirmar el resultado. Revisá el estado actual antes de
  repetir la operación. Referencia: …», y relee el impacto **antes** de permitir otro intento. Si la relectura trae
  `deactivatedAtUtc`, la baja se da por hecha («observada»): refresca y cierra **sin** el toast de éxito y sin adoptar la
  revisión. Si la relectura falla, aparece «Reintentar» y «Dar de baja» sigue apagado.
- 429: no tiene cuenta regresiva propia; sale el toast común con el `detail` del servidor (ver 6.18).
- Éxito: toast «Dimos de baja {nombre}.», refresco y cierre.

**390 y 320 px** (evidencia [BA3 a 390](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-20261021-ba3-es-390.png),
**anterior** al arreglo del punto 5, y [BA3 sin envíos abiertos a 320](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-review-20261021-no-open-es-320.jpg),
vigente): con `mobileFullscreen` el diálogo ocupa toda la pantalla (`inset-0`, `h-svh`); el cuerpo se desplaza y el pie
queda fijo abajo, con «Dar de baja» arriba y «Cancelar» debajo, a todo el ancho (`flex-col-reverse` del pie bajo 640 px).
En escritorio, centrado, ancho máximo `sm:max-w-2xl` y alto máximo de la pantalla menos 2 rem, con «Cancelar» a la
izquierda de «Dar de baja».

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), commit `bb98bf2`):
> el párrafo final del cuerpo («Cambió la revisión guardada…», el motivo de no poder dar de baja) y el «Reintentar»
> suelto explican el estado del botón principal desde afuera de su campo. Con la regla nueva, el botón está habilitado o
> deshabilitado, con el motivo como tooltip si no es obvio. La revisión cambiada puede seguir como el botón «Revisar».

### 6.6 Reactivar (BA4)

No hay un endpoint de reactivación: **reactivar es publicar** el borrador guardado con la publicación de siempre
(chequeo + aceptación + estado), que crea una versión nueva y Flows nuevos. El formulario sigue dado de baja durante esa
publicación y vuelve a activo solo cuando se confirma la versión nueva. La numeración continúa.

**Entradas:**
- Botón principal «Reactivar» del editor, o el ítem «Reactivar» del menú «⋯».
- «Reactivar» en una fila del listado: navega al editor con `state.reactivateFormId`. El editor espera el detalle fresco
  y abre el diálogo solo si sigue dado de baja, con `canReactivate` y con `canPublish`; después borra la intención del
  historial. Nunca acepta sola.

**El diálogo** es `src/features/business-forms/components/PublicationCheckDialog.tsx` con la prop `reactivation`
(`{ name, nextRecordNumber }`):
- Título «Reactivar «{nombre}»» / «Se publica una versión nueva con el borrador guardado y Flows nuevos.» (si hay cambios
  sin guardar, en su lugar «Primero guardá los cambios» / «Se publica sólo lo guardado. Guardá el borrador antes de
  comprobarlo.», como en la publicación normal).
- Primera superficie, propia de la reactivación: «La numeración sigue donde estaba.», «La próxima respuesta es
  {VIS-000025}.» (solo si el servidor da `nextRecordNumber`, es decir, con lectura de respuestas) y «Vuelve a mandarse
  desde el chat y el asistente lo puede ofrecer. Vuelven las sugerencias que se conservaron.»
- Después, la comprobación de siempre (sección 4 y anexo anterior P1): qué cambia respecto de la versión activa,
  idiomas, problemas, avisos, qué queda fijo y «Qué va a pasar».
- Pie: «Cancelar» + **«Reactivar con la versión {N}»** (N = `nextVersionNumber` del chequeo); con la comprobación vencida,
  además «Volver a comprobar». A la izquierda del pie (debajo, en el celular): «Se publica el borrador guardado el {fecha}.»
- Solo en reactivación usa `mobileFullscreen` con cuerpo desplazable y pie fijo; la publicación normal conserva el
  diálogo predeterminado (`11dd92e`, punto 10 del review).

**Después de aceptar:** la publicación avanza como siempre. En el listado, la fila sigue «Dado de baja · versión N» con
«Reactivando con una versión nueva…» mientras `unavailableReason = PublicationInProgress`; el menú y el botón quedan
apagados con «Hay una publicación en curso.». Al publicarse, el detalle se relee y `lifecycle.deactivatedAtUtc` vuelve en
null. **Una reactivación fallida no se ve en el listado**: queda «Dado de baja · versión N», y el detalle está en el
editor (desvío aceptado en el review).

**Estados del diálogo** (los de P1): comprobando; «Comprobación pendiente» + «Reintentar» ante un error; 403 «Ya no tenés
permiso para publicar.»; **429**: «Esperá N segundos para volver a intentar.» como texto en el cuerpo, con «Reintentar»
apagado durante la espera, y además como toast de advertencia (evidencia
[P10 a 320](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-review-20261021-publication-wait-es-320.png)).

**390 y 320 px** (evidencia [BA4 Reactivar a 320](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-20261021-ba4-reactivar-es-320.png)):
pantalla completa, cuerpo desplazable, pie fijo con «Reactivar con la versión 2» arriba, «Cancelar» debajo y la fecha de
guardado al final.

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), commit `bb98bf2`):
> - «Se publica el borrador guardado el {fecha}.» es un texto pegado a los botones del pie.
> - La espera de un 429 aparece **dos veces**, en el cuerpo como texto bajo «Reintentar» apagado y como toast; es «la
>   espera» que el punto 38 marca en otras pantallas.
>
> Los dos vienen de P1, no del ciclo de vida, pero la reactivación los hereda.

### 6.7 Diálogo «Eliminar» (BA4)

`src/features/business-forms/components/FormDeletionDialog.tsx`. Se abre desde la fila o desde el menú «⋯». Requiere
`formtemplates.manage` (no publish). Solo se ofrece sin respuestas.

- **Cabecera:** «Eliminar «{nombre}»» / «Deja de aparecer en los listados y no se puede volver a usar.»
- **Cuerpo:**
  - «El prefijo {ENT} queda reservado.»;
  - solo si **llegó a publicarse** (hay versión activa), un `Banner` de advertencia: «En WhatsApp no se deshace. Sus Flows
    se deprecan cuando no quedan envíos abiertos.»;
  - «Esta acción no se deshace.»;
  - si quedó bloqueado, un estado: «Cambió la situación del formulario. Cerrá este diálogo y revisalo antes de
    eliminarlo.» o «Ya no tenés permiso para eliminar este formulario.»;
  - durante una espera por 429: «Esperá N segundos para volver a intentar.»
- **Pie:** «Cancelar» + «Eliminar» / «Eliminando…» (variante destructiva, como acción principal del pie).
- **No lee impacto:** manda `DELETE` con la revisión que ya tenía: la de la fila en el listado, o la del borrador
  confirmado en el editor, aunque haya cambios sin guardar.

**Estados y errores:**
- 403, 404 o 409 (por ejemplo `Forms.Definition.HasResponses`): queda bloqueado con su texto, un toast con el `detail`
  del servidor, y el listado o el editor se refrescan.
- 429: cuenta regresiva en el cuerpo y «Eliminar» apagado hasta que termina; también el toast.
- Red o 5xx: toast «No pudimos confirmar el resultado…» y queda bloqueado mientras relee el detalle
  (`GET /api/forms/definitions/{id}`). Un 404 `Forms.Definition.NotFound` confirma que se eliminó, y se trata como
  éxito. Otra revisión o `canDelete` false lo dejan bloqueado con «Cambió la situación…». Un 403 lo deja sin permiso.
  Nunca repite el DELETE solo, ni lo repite con otra revisión.
- Éxito: toast «Eliminamos {nombre}.». Desde el listado: retira el detalle de la caché, refresca y devuelve el foco al
  buscador (la fila ya no existe). Desde el editor: autoriza la salida sin preguntar por cambios sin guardar, vuelve a
  `/formularios` con `replace` y refresca. Lo que no se había guardado se pierde con la eliminación.

**390 y 320 px** (evidencia [eliminar borrador a 320](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-review-20261021-delete-draft-es-320.png)
y [eliminar publicado a 320, en](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-review-20261021-delete-published-en-320.png);
[BA4 Eliminar a 320](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-20261021-ba4-eliminar-en-320.png)
es **anterior** a `77f0066`, con textos que ya no existen): pantalla completa, mucho espacio libre, pie fijo con
«Eliminar» rojo arriba y «Cancelar» debajo. En escritorio, centrado, con el ancho predeterminado del diálogo
(`sm:max-w-lg`).

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), commit `bb98bf2`):
> la cuenta regresiva «Esperá N segundos…» y «Cambió la situación del formulario…» son textos que explican por qué
> «Eliminar» está apagado. Con la regla nueva, el motivo va como tooltip del botón y el toast ya informa la causa.
> Además, la regla de diálogos pide «una destructiva, separada a la izquierda»; acá «Eliminar» es la acción principal
> del pie, a la derecha. Conviene decidirlo al rehacer el diálogo.

### 6.8 Sugerencias hacia un dado de baja o eliminado (BA6)

`src/features/business-forms/components/FormSuggestionsSection.tsx` («Qué sugerir después», en el editor del formulario
**de origen**, con `formsuggestions.manage`). Las opciones (`GET …/suggestions/options`) traen ahora
`unavailableDestinations` (`{ definitionId, name, state: "Deactivated" | "Deleted" }`), aparte de los `destinations`
elegibles.

- **La fila** de una sugerencia cuyo destino no está disponible muestra:
  - el nombre conservado, en el idioma del contenido con respaldo;
  - en lugar de la insignia del modo, una insignia «Dado de baja» o «Eliminado»;
  - **sin el lápiz de editar**, solo «Quitar sugerencia N» (papelera);
  - debajo, en rojo: «Este formulario está dado de baja. Quitá la sugerencia para guardar.» o «Este formulario fue
    eliminado. Quitá la sugerencia para guardar.» Es texto común; ya no es `role="alert"` (punto 11 del review).
- **«Guardar sugerencias» queda apagado** mientras quede alguna: la validación local (`prepareSuggestions` en
  `src/features/business-forms/suggestions.ts`) marca `destinationUnavailable` para todo destino que no esté en
  `destinations`.
- **Quitar** mueve el foco a la vecina (a su lápiz, o a su papelera si también está no disponible) o a «Agregar
  sugerencia».
- «Formulario a sugerir» solo lista publicados elegibles, con la ayuda «Solo aparecen formularios publicados.»
- **Dar de baja no borra las sugerencias:** dejan de ofrecerse y, si se reactiva, vuelven las que quedaron.

**390 px** (evidencia [BA6 a 390, en](../../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/baja-20261021-ba6-en-390.png)):
en la banda de la superficie, «Revision 1» y «Save suggestions» apagado, y debajo «Check the marked fields before
saving.»; la fila con «Previous inspection», la insignia «Deactivated», solo la papelera y el texto rojo.

> **No copiar — review E2, punto 38** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)) y **regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md)):
> `FormSuggestionsSection` es uno de los lugares que nombra el punto 38. Hay que sacar:
> - la insignia «Cambios sin guardar» o el texto «Revisión N» al lado de «Guardar sugerencias»;
> - el texto bajo el botón (`saveReason`: «Revisá los datos marcados antes de guardar.», «No hay cambios para guardar.»,
>   «Esperá a que termine la operación.», «Releé y elegí qué conservar antes de guardar.»).
>
> Pendiente en `982ade8`. El texto rojo de la fila, visible apenas se abre la pantalla, también choca con «se valida al
> intentar guardar». El tablero BA6 sí pedía decir que hay que quitarla: conviene resolver con el usuario si queda como
> estado de la fila (sin color de error) o como tooltip del Guardar apagado.

> **No copiar — regla de idioma del 2026-10-23** (review E2, punto 35): el nombre del destino se muestra en
> `contentCulture`, el idioma elegido en «Textos en». Al retirar ese selector, va en el idioma del menú con respaldo al
> original.

### 6.9 Estado del canal: deprecación y «Requiere atención»

`deprecationMessage` (`src/features/business-forms/lifecyclePresentation.ts`) traduce el `deprecationState` del servidor.
WhatsApp mantiene la operación durable y BusinessForms solo lee por un puerto; el front **no** consulta Meta ni decide
por reloj.

| `deprecationState` | Texto | Dónde y cómo |
| --- | --- | --- |
| `Pending` con `lastOpenSessionExpiresAtUtc` | «En WhatsApp: se deprecan cuando venzan los envíos abiertos. El último vence el {fecha}.» | Listado (línea chica) y aviso del editor (línea chica) |
| `Pending` sin vencimiento | «Deprecación pendiente en WhatsApp.» | idem |
| `Uncertain` | «Sin confirmar en WhatsApp. Se sigue consultando.» | Listado (línea chica); editor, **Banner de advertencia** aparte |
| `NeedsAttention` | «No se pudo deprecar en WhatsApp. Requiere atención.» | idem; **sin acción nueva** ni fecha inventada (`f27c540`) |
| `Deprecated` con fecha | «Deprecados el {fecha} en WhatsApp.» | Listado y aviso del editor |
| `Deprecated` sin fecha | «Deprecados en WhatsApp.» | idem |
| `null` | nada | Un null no es deprecación. Un formulario **activo** puede traer `Pending` (regla del back), pero el front solo lo rotula en los dados de baja |

Las fechas usan la zona horaria del perfil (`formatDateTimeInZone`).

### 6.10 Hooks y estado

- **Claves de query** (`src/features/business-forms/hooks/useFormQueries.ts`):
  - `formActorKey(actorId) = ["business-forms", actorId]`;
  - `formScopeKey(scope) = [...formActorKey, permisos ordenados, cultura]`;
  - listado `[...scope, "page", query]` y facetas `[...scope, "facets", { search }]`, las dos con `gcTime: 0` y
    `placeholderData` solo del mismo dueño;
  - detalle `formDetailKey = [...scope, "detail", id]`, con `staleTime: 0` y `gcTime: 0`;
  - impacto de la baja `[...scope, "deactivation", id]`, con `gcTime: 0`, `usePrivateQueryCleanup` (cancela y retira al
    desmontar) y `errorOwner: "local"`.
- **Escrituras sin `useMutation`:** POST de baja y DELETE se llaman directo, con un `AbortController` en un ref (`flight`)
  que impide dos vuelos y se aborta al desmontar, y un ref `active` que descarta respuestas tardías. No hay reintento
  automático ni nada en MutationCache.
- **Invalidaciones tras una baja o eliminación** (listado y editor):
  - `formActorKey(actorId)`: todo lo de formularios del actor (listado, facetas, detalles);
  - `["form-overview", overviewScope.owner]`: la tabla MF1 (`4a7fd7d`);
  - `["form-responses", responseScope.owner]`: lo de respuestas, para que el panel no ofrezca un formulario eliminado
    (`02c5076`, punto 4).
  - Al eliminar, además se retira el detalle (`removeQueries`).
- **`confirmRevision` (editor)** (`src/features/business-forms/hooks/useFormDraftEditor.ts`): tras una baja propia
  confirmada, el borrador adopta la revisión nueva solo si es el mismo formulario, está en fase Editing, no hay otra
  escritura en vuelo, la revisión confirmada es la esperada y la nueva es exactamente esperada + 1. Conserva valor y
  baseline: **los cambios sin guardar siguen ahí**, y el siguiente Guardar va con la revisión nueva. Si adopta, también
  parchea el detalle en caché (revisión y `lifecycle`) solo si seguía en la revisión esperada. **Invalida siempre**,
  adopte o no (`55a7b1d`, punto 7).
- **Baja observada** (resultado incierto que la relectura muestra aplicado): refresca sin adoptar la revisión ni
  atribuírsela al intento. El próximo Guardar con la revisión vieja recibirá 409 y pasará a la revisión D.
- **Revisión distinta al abrir la baja:** «Revisar» llama a `editor.requireReview` con un 409 sintético
  `Forms.Draft.RevisionConflict`. No se combina nada solo.
- **Eliminado durante la edición:** el hook marca `knownDeleted` (404 `Forms.Definition.NotFound` en un PUT), apaga
  `canSave` y no reintenta.
- **Intención de reactivar:** en `location.state`, consumida tras `isFetchedAfterMount` y borrada con `replace`.
- **Foco:**
  - listado: al cerrar un diálogo vuelve a la acción de la fila o, si la fila desapareció, al buscador;
  - editor: la baja vuelve al menú «⋯»; la eliminación, al menú o al campo Nombre;
  - la reactivación vuelve al último disparador conectado y habilitado (botón principal, «⋯»).
- **Cambio de dueño:** el diálogo del listado se descarta si cambian actor, permisos o idioma.
- **Privacidad:**
  - `responseCount`, `pendingApprovalCount` y `nextRecordNumber` llegan **null sin `formrecords.read`**; null es
    «desconocido», nunca 0;
  - administrar o publicar no revela esos conteos;
  - el impacto se pide con `no-store` y se retira de la caché al cerrar.
- **Chat:** el selector «Enviar formulario» del chat es de otra feature y usa el `staleTime` global de 30 s
  (`src/shared/api/queryClient.ts`). Puede ofrecer durante ese tiempo un formulario recién dado de baja; el servidor
  rechaza el envío igual (pendiente «después, sin bloquear» del review).

### 6.11 Clientes HTTP y guards zod

Clientes (`src/features/business-forms/api/forms.ts`):

| Función | HTTP | Notas |
| --- | --- | --- |
| `fetchFormDefinitions(query)` | `GET /api/forms/definitions?page&pageSize&search&sort&publicationFilter` | `publicationFilter` siempre viaja: `Active` (predeterminado), `Published`, `Drafts` o `Deactivated` |
| `fetchFormFacets({ search })` | `GET /api/forms/definitions/facets?search=` | Cuatro conteos |
| `fetchFormDefinition(id)` | `GET /api/forms/definitions/{id}` | También se usa para releer una eliminación incierta |
| `fetchFormDeactivation(id)` | `GET /api/forms/definitions/{id}/deactivation` | `cache: "no-store"` |
| `deactivateForm(id, expectedRevision)` | `POST /api/forms/definitions/{id}/deactivation` con `{ expectedRevision }` | |
| `deleteForm(id, expectedRevision)` | `DELETE /api/forms/definitions/{id}` con **cuerpo JSON** `{ expectedRevision }` | Sin respuesta tipada (204) |
| `fetchSuggestionOptions(id)` | `GET /api/forms/definitions/{id}/suggestions/options` | `src/features/business-forms/api/suggestions.ts`, `no-store` |

Reactivar usa los clientes de publicación existentes (`checkFormPublication`, `acceptFormPublication`,
`fetchFormPublication`).

Guards (`src/features/business-forms/api/response.ts`, todos `z.strictObject`):
- **`lifecycle`**, obligatorio en el detalle y en cada fila del listado:

  | Campo | Tipo |
  | --- | --- |
  | `revision`, `nextVersionNumber` | entero |
  | `deactivatedAtUtc` | UTC nullable |
  | `deactivatedBy` | autor nullable |
  | `responseCount`, `pendingApprovalCount` | entero ≥ 0 nullable |
  | `nextRecordNumber` | texto nullable |
  | `canDeactivate`, `canReactivate`, `canDelete` | booleano |
  | `unavailableReason` | `PublicationInProgress` \| `NotPublished` \| null |
  | `deletionUnavailableReason` | `PublicationInProgress` \| `HasResponses` \| `OpenSends` \| null |
  | `deprecationState` | `Pending` \| `Uncertain` \| `Deprecated` \| `NeedsAttention` \| null |
  | `deprecatedAtUtc`, `lastOpenSessionExpiresAtUtc` | UTC nullable |

  Un estado desconocido o un campo de más rechazan la respuesta. `lifecycle` es metadata **hermana** del borrador: no
  participa del guardado, de su huella ni de sus comprobantes.
- **Detalle:** `{ definition, lifecycle, capabilities: { canSave, canPublish, canPreview }, audit, publication }`.
- **Fila:** suma `lifecycle` a la fila de siempre.
- **Facetas:** `{ active, published, drafts, deactivated }`, los cuatro obligatorios.
- **Resultado de la baja:** `{ definitionId, revision, lifecycle }`. `readFormDeactivation(value, id)` exige además que el
  id coincida (sin distinguir mayúsculas) y que `revision === lifecycle.revision`; si no, `UnsupportedFormResponse`.
- **Impacto:** el mismo, más `affectedSuggestions: [{ definitionId, name }]`, con las mismas comprobaciones.
- **Opciones de sugerencias:** `unavailableDestinations: [{ definitionId, name, state: "Deactivated" | "Deleted" }]`.

### 6.12 Cohorte exportada `forms-lifecycle-http`

- **Carpeta:** `src/test/fixtures/forms-lifecycle-http/{es,en}/` + `provenance.json`. Declarada en `src/test/httpCohort.ts`
  como `lifecycle: ["forms-lifecycle-http", "FORM_LIFECYCLE_HTTP_CONTRACT_INPUT", "lifecycle"]` (`artifactSet` lifecycle).
- **30 cuerpos**, 15 por idioma:
  - `preview`, `preview-affected`, `deactivated`;
  - `detail-deactivated`, `list-deactivated`, `facets`;
  - `detail-reactivation-pending`, `detail-reactivated`;
  - `options-unavailable`, `options-deleted`;
  - `delete-responses-conflict` (ProblemDetails 409 `Forms.Definition.HasResponses`);
  - `status-pending`, `status-uncertain`, `status-deprecated`, `status-needsattention`.
- **Productores del back** (10 fuentes con SHA256 en el manifiesto): `FormDeactivationHttpTests`,
  `FormDeletionHttpTests`, `FormRetirementStatusHttpTests`, `FormLifecycleContractCapture`,
  `ControlledFormChannelStateSource`, `FormDraftEditingTests`, `FormDraftDurabilityTests`, `FormSuggestionHttpTests`,
  `FormPublicationAcceptanceTests` y `FormPublishedSelectionTests`. Transporte: HTTP real con PostgreSQL/Redis en
  Testcontainers.
- **Última renovación** dentro del export conjunto de E2 (`e4db46c`): run `20261023-064235-5542f7f13d844d5985f55c4142c9385f`,
  back `7e5b7a94`. El cierre del review de baja usó el run `20261021-141839-…` (`49e14c21`).
- **`.gitattributes`:** `src/test/fixtures/forms-lifecycle-http/** -text whitespace=cr-at-eol`, para que
  `core.autocrlf` no cambie los bytes ni los SHA.
- **Consumidor:** `src/features/business-forms/api/lifecycleHttpContract.test.ts`, cinco tests por idioma más uno común:
  - califica las 30 capturas y sus productores;
  - lee la baja, el detalle, las cuatro facetas y la reactivación pendiente y confirmada (revisiones 2 → 3 → 4,
    `nextRecordNumber` `{prefijo}-000002` → `-000004`, una versión nueva con otro `versionId`);
  - comprueba que los destinos dados de baja o eliminados quedan fuera de los elegibles;
  - comprueba que los conteos privados son null y que las fechas del canal solo vienen en el estado que corresponde;
  - pasa los cuerpos sin tocar por los adaptadores HTTP reales (MSW), incluido el POST con `{ expectedRevision: 2 }` y el
    DELETE que termina en 409.
- `src/features/business-forms/api/stage4HttpProvenance.test.ts` suma `lifecycle` a la procedencia conjunta.
- **Efecto en otras cohortes:** como `lifecycle` pasó a ser obligatorio, el export conjunto `91cfd6e` reescribió 328
  archivos de fixtures (detalles y filas en `forms-http`, `whatsapp-forms-http`, catálogos…), y `a031c11` completó los
  dobles de prueba con `fixtureLifecycle` (`src/test/visual/formFixtures.ts`).

### 6.13 i18n

- `src/locales/{es,en}/forms.json`, grupo nuevo **`lifecycle`** (66 claves por idioma):
  - acciones, títulos y descripciones de los tres diálogos;
  - `reasons.*` y `deletionReasons.*`;
  - `deprecation.*` (seis textos de canal);
  - plurales `_one`/`_other` de sugerencias, respuestas y aprobaciones;
  - `state`/`stateWithoutVersion`, `deactivatedBy`, `reservedPrefix`, `deletePublishedHint` y `deletedWhileEditing`.
- Claves sueltas nuevas: `active` («Activos» / «Active»), `deactivated` («Dados de baja» / «Deactivated») y
  `responseCountColumn` («Respuestas» / «Responses»). `all` («Todos») sigue en el archivo, pero `FormsPage` ya no la usa.
- En `suggestions`: `deactivated`, `deactivatedHint`, `deleted` y `deletedHint`.
- Textos notables:
  - «Dar de baja» / «Deactivate»;
  - «Reactivar con la versión {{version}}» / «Reactivate with version {{version}}»;
  - «Dado de baja · versión {{version}}»;
  - «No se pudo deprecar en WhatsApp. Requiere atención.» / «Could not deprecate on WhatsApp. Needs attention.»;
  - «Tiene respuestas y no se puede eliminar.»;
  - «El formulario ya fue eliminado. Tus cambios sin guardar se conservan.»
- La paridad es/en la verifica `src/locales/parity.test.ts`.

### 6.14 Pruebas

**Unitarias y de página (Vitest + MSW):**

| Archivo | Qué cubre |
| --- | --- |
| `src/features/business-forms/pages/FormLifecycle.test.tsx` (13) | La baja lee las sugerencias afectadas y conserva el borrador en la revisión confirmada; un POST fallido deja el diálogo abierto con un solo toast; reactivar pasa por la comprobación normal y sigue dado de baja tras aceptar; sin Publish no ofrece la baja y conserva el conteo desconocido; el foco vuelve al «⋯»; la intención de la fila no acepta sola y espera el detalle fresco; vencimiento en la zona del perfil; sin Flows no afirma deprecación; refresco del MF1; una baja observada no adopta la revisión; el 409 de revisión pasa a la revisión explícita; menú y botón usan la misma capacidad de publicar |
| `src/features/business-forms/pages/FormDeletion.test.tsx` (10) | Eliminar con conteo desconocido; un fallo con un solo aviso; eliminar con la revisión confirmada y volver sin preguntar; la fila desaparece y el foco va al buscador; guardar contra un formulario eliminado da el aviso definitivo sin bucle; se oculta con respuestas; no hay menú vacío; refresco del MF1; eliminación observada tras perder la respuesta (red/5xx); un GET de recuperación ajeno no se toma como eliminación |
| `src/features/business-forms/pages/FormLifecycleRefresh.test.tsx` | Una baja confirmada refresca aunque el borrador no pueda adoptar la revisión |
| `src/features/business-forms/components/FormDeactivationDialog.test.tsx` | Sin envíos abiertos, «se deprecan ahora» (es/en); baja observada tras red/500 sin éxito falso; espera la relectura antes de permitir otro intento; exige una relectura exitosa |
| `src/features/business-forms/components/FormDeletionDialog.test.tsx` | Prefijo reservado y advertencia de WhatsApp solo si se publicó (es/en) |
| `src/features/business-forms/components/FormLifecycleMenu.test.tsx` | Los motivos apagados se leen con el teclado sin ejecutar ni cerrar |
| `src/features/business-forms/components/FormLifecycleNotice.test.tsx` | Estados autoritativos; no inventa deprecación sin Flows; no rotula como baja un activo con evidencia vieja; NeedsAttention sin acción nueva |
| `src/features/business-forms/api/formLifecycle.test.ts` | Contrato: conteos null, cuatro facetas, `lifecycle` independiente del borrador, `canDelete` y vencimiento nullable, estados de deprecación y NeedsAttention |
| `src/features/business-forms/api/lifecycleHttpContract.test.ts` | Cohorte real (6.12) |
| `src/features/business-forms/components/FormSuggestionsSection.test.tsx`, `src/features/business-forms/api/suggestions.test.ts` | BA6: destino no disponible, solo Quitar, Guardar apagado, sin `role="alert"` |
| `src/features/business-forms/pages/FormsPage.test.tsx` | Facetas y filtro del ciclo de vida en la URL |

**Visuales (Playwright, `src/test/visual/formLifecycle.spec.ts`, 52 escenas):**
- Matriz: es/en × 390/320.
  - `BA1 lifecycle table and contained scrolling`: segmentado de cuatro, tabla desplazada, diálogo de eliminar borrador y
    filtro Dados de baja.
  - `BA lifecycle {editor|deactivate|reactivate|delete}`.
  - `BA6 unavailable suggestion can only be removed`.
  - `BA WhatsApp {uncertain|deprecated|needsattention} {list|editor}`.
  - `BA2 unavailable action reasons stay reachable with the keyboard` (P11).
- Comprueban geometría:
  - diálogo en (0,0) del ancho y alto de la pantalla;
  - el pie no se mueve al desplazar el cuerpo;
  - `scrollWidth` de la página y del diálogo menor o igual al ancho;
  - foco al cerrar;
  - que **ninguna confirmación escriba** (sin POST, PUT ni DELETE salvo el chequeo).
- Guardan capturas en `test-results/` como evidencia; **no hay `toHaveScreenshot`**: no suman referencias comparadas
  (las 127 referencias del front son de otras escenas).
- Escenarios en `src/test/visual/formFixtures.ts`:
  - `forms-lifecycle-table`, `-open`, `-deletable` y `-publishing`;
  - `-deactivated`, `-deactivated-uncertain`, `-deactivated-deprecated` y `-deactivated-needsattention`;
  - `-suggestions`.
- P10: 6 escenas en `src/test/visual/formPublicationReads.spec.ts`
  (`normal publication wait keeps useful separation after a 429`, es/en × 1440/390/320).
- La variante `?scenario=mobile-dialog` del catálogo visual (`src/test/visual/Catalog.tsx`) muestra `mobileFullscreen`
  ([estándar visual](../../../../ArquitecturaBaseFront/docs/guides/estandar-visual.md)).

**Evidencia** ([cierre local](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#cierre-local-de-baja-reactivación-y-eliminación--2026-10-21)
y [posterior al review](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#verificación-posterior-al-review-de-baja--2026-10-21)):
- seis capturas `baja-20261021-*`: BA1, BA2, BA3, BA4 reactivar, BA4 eliminar y BA6;
- ocho capturas `baja-review-20261021-*`: sin envíos abiertos, eliminar borrador y publicado, NeedsAttention en editor y
  en tabla, P10 es/en y P11.
- Las de BA3 y BA4-eliminar de la primera tanda son **anteriores** a los arreglos 5 y 9.
- Cierre del front: 2511/2511 tests, visual 585/585 y arquitectura 49/49 (según el diseño; no se corrió en este
  relevamiento).

### 6.15 Review de dar de baja: qué se corrigió y qué sigue

Verificado en `982ade8`:

| Punto del review | Estado en el front |
| --- | --- |
| 4. Eliminar no actualiza el panel de Respuestas | Corregido (`02c5076` y `4a7fd7d`): invalida `form-responses` y `form-overview` |
| 5. BA3 con textos que no aplican sin envíos abiertos | Corregido (`08fffcf`): el aviso de envíos solo con vencimiento y «se deprecan ahora» |
| 6. Red/5xx al dar de baja o eliminar | Corregido (`2bedb9f`): «Revisá el estado actual…» y relectura que reconoce la baja o el 404 tipado |
| 7. Baja confirmada que no refresca | Corregido (`55a7b1d`) |
| 8. Reactivar sin `canPublish` en el menú | Corregido (`58f1922`) |
| 9. Eliminar sin BA4 en un publicado | Corregido (`77f0066`) |
| 10. Regresión de espaciado en la publicación | Corregido (`11dd92e`) |
| 11. Accesibilidad del menú y `role="alert"` de BA6 | Corregido (`00c3f93`) |
| 14. Texto del chat en MutationCache | Corregido (`fc306b7`, WhatsApp) |
| 1 (back) NeedsAttention | Front corregido (`f27c540`) |

**Sigue pendiente o aceptado** (no son errores nuevos, pero conviene no repetirlos sin decidir):
- Selector del chat con caché de 30 s que puede ofrecer un dado de baja (6.10).
- Aceptados en el review:
  - el error de la baja va en el toast común y no dentro del diálogo;
  - el aviso del editor no lleva la cantidad de respuestas;
  - la reactivación fallida no se ve en el listado;
  - «Tiene respuestas y no se puede eliminar» se le muestra a quien solo administra.
- Deuda menor del front que nombra el review: los filtros de la bandeja del chat viven en el estado de la página y no en
  la URL (es de WhatsApp; no verificado en esta parte).

### 6.16 Para el multitenant

- **Prefijo reservado:** eliminar es lógico y deja el prefijo ocupado para siempre. En multitenant, la unicidad y la
  reserva del prefijo tienen que ser **por organización**. El diálogo lo nombra; cuidado con mostrar prefijos de otra
  organización.
- **Flows de Meta por tenant:** la deprecación es irreversible y se ejecuta sobre la cuenta de WhatsApp vinculada.
  `NeedsAttention` aparece, por ejemplo, ante `AccountMismatch`, que en multitenant es más probable (cada organización
  con su cuenta). El estado del canal tiene que leerse por tenant y nunca cruzarse.
- **Permisos:** conservar la separación publicar (baja y reactivación) / administrar (eliminar) y que ninguno implique al
  otro; en B2C, decidir si un cliente final puede tener alguno (probablemente no).
- **Claves de caché:** hoy son `["business-forms", actorId, permisos, cultura]` y `["form-overview" | "form-responses",
  owner]`. En multitenant suman el **tenant** en la raíz, y una baja o eliminación tiene que invalidar solo lo de ese
  tenant.
- **Conteos privados:** `responseCount`, `pendingApprovalCount` y `nextRecordNumber` null sin leer respuestas. En
  multitenant, null también si el actor no pertenece a la organización dueña de las respuestas.
- **Reactivar es publicar:** no hace falta un endpoint propio; la definición sigue dada de baja hasta confirmar la
  versión nueva. Mantenerlo así evita estados intermedios.
- **`DELETE` con cuerpo JSON:** algunos proxies o gateways descartan el cuerpo de un DELETE. Si el multitenant pasa por
  un gateway, conviene la revisión en un encabezado (`If-Match`) o en la query.
- **Sugerencias entre formularios:** los destinos dados de baja o eliminados se conservan y bloquean Guardar. En
  multitenant, las sugerencias solo pueden apuntar a formularios del mismo tenant, y `unavailableDestinations` no puede
  filtrar nombres de otro.
- **No copiar:** «Cambios sin guardar» en la insignia del editor, los textos bajo «Guardar sugerencias», la fecha de
  guardado en el pie del diálogo de publicación ni la espera duplicada (6.4–6.8).

### 6.17 Endpoints nuevos o cambiados

| Método y ruta | Cambio |
| --- | --- |
| `GET /api/forms/definitions?…&publicationFilter=Active\|Published\|Drafts\|Deactivated` | Valores nuevos `Active` (predeterminado) y `Deactivated`; cada fila suma `lifecycle` |
| `GET /api/forms/definitions/facets?search=` | Responde `{ active, published, drafts, deactivated }` |
| `GET /api/forms/definitions/{id}` | El detalle suma `lifecycle` |
| `GET /api/forms/definitions/{id}/deactivation` | **Nuevo:** impacto de la baja (`no-store`) |
| `POST /api/forms/definitions/{id}/deactivation` | **Nuevo:** dar de baja con `{ expectedRevision }` |
| `DELETE /api/forms/definitions/{id}` | **Nuevo:** eliminar con cuerpo `{ expectedRevision }`; 409 `Forms.Definition.HasResponses` y otros |
| `GET /api/forms/definitions/{id}/suggestions/options` | Suma `unavailableDestinations` con `state` `Deactivated` o `Deleted` |
| `POST …/publication-checks`, `POST …/publications`, `GET …/publications/{requestId}` | Sin cambio de forma; se usan para **reactivar** |

### 6.18 Huecos

- **429 en el POST de la baja:** el diálogo no tiene cuenta regresiva; muestra el toast común con el `detail` del 429 o
  «Algo salió mal». No hay un test que lo cubra (verificado en el código; el texto exacto del `detail` depende del back).
- **`canEdit` de una fila dada de baja:** en la captura real `list-deactivated` viene `false`. No pude verificar si es
  por el estado o porque el actor capturado no tenía manage (el editor dado de baja tiene `canSave: true` en
  `detail-deactivated`). (no verificado)
- **Aspecto a 1440 px:** solo se verificó por el código y las capturas de los tableros. Las escenas de esta etapa son de
  390/320 (P10 también de 1440).
- **Píxeles del menú y de los diálogos tras los arreglos:** la evidencia comprometida de BA3 a 390 y de BA4-eliminar a 320
  es anterior a `08fffcf` y `77f0066`; el aspecto vigente se dedujo del código y de las capturas `baja-review-*`.
- **Que el texto rojo de BA6 y los motivos del menú violen la regla del 2026-10-23:** es interpretación; el punto 38 no
  los nombra explícitamente (sí nombra `FormSuggestionsSection` por los textos junto a Guardar).

---

## 7. Chat de Inicio

### 7.1 Resumen

**Sin cambios en este período.** `git diff --stat 9261e3d..982ade8` sobre `src/features/artificial-intelligence`,
`src/features/home`, `src/shared/ui/assistant`, `src/locales/{es,en}/assistant.json`, `src/app/AuthenticatedHome.tsx` y
`src/layouts/HomeLayout.tsx` sale vacío: ningún commit de los ~310 de este traspaso tocó el chat de Inicio. El anexo
anterior no lo describía, así que acá va el **estado vigente completo** en `982ade8`, para que el multitenant sepa qué
hay, y sobre todo **qué no copiar**.

Qué es: la pantalla `/` de una persona con sesión. Si el módulo de IA está instalado y la persona tiene
`ai.assistant.use`, Inicio es un chat de consultas sobre el sistema (usuarios, roles, permisos, información del negocio y
ayuda), armado con `@assistant-ui/react` (`ExternalStoreRuntime`, versión fijada 0.15.23) sobre turnos durables del
backend: el servidor acepta la pregunta (202), la procesa en segundo plano y el front consulta el estado hasta que
termina. Si no hay módulo o permiso, Inicio es una página vacía con el título «Inicio». No tiene relación con
formularios: el canal Inicio del motor de IA solo ofrece cinco herramientas de lectura
([regla del back](../../features/inteligencia-artificial.md#fuentes-y-autorización)).

> **No copiar — review E2, punto 34** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)): **el chat de
> Inicio no muestra nunca la respuesta.** El back serializa el estado del turno en minúsculas
> (`AiAssistantResponsePolicy.State(state) => state.ToString().ToLowerInvariant()`, en
> `ArquitecturaBase.Application/Modules/ArtificialIntelligence/Services/AiAssistantResponsePolicy.cs`; lo usa también
> el 202 de `AiAssistantSubmissionPolicy.cs:134`) y sus tests de integración lo fijan así
> (`AiAssistantEndpointsTests.cs:64` espera `"pending"`, `:75` `"canceled"`; `AiAssistantExecutionIntegrationTests.cs:41,60`
> y `AiAssistantRecoveryTests.cs:37` esperan `"completed"`). El front compara siempre con mayúscula inicial
> (`"Pending"`, `"Completed"`…). Resultado: al mandar «Hola», el primer GET del turno trae `"pending"`, el front lo da por
> terminado, deja de consultar, muestra la clave cruda «states.pending» y nunca proyecta la respuesta. Detalle línea por
> línea en 7.9. Pasa por lo menos desde `ceaed208` (2026-10-09) en el back, que es donde aparece la serialización en
> minúsculas en el archivo actual (si antes otro archivo devolvía PascalCase: no verificado). No hay ejemplos HTTP
> exportados del asistente de Inicio, y los dobles de las pruebas usan mayúsculas, por eso ninguna prueba lo detecta.
> **Estado en `982ade8`: pendiente.**

### 7.2 Navegación y rutas

- **Menú:** grupo General, entrada **«Inicio»** (`/`), sin permiso (`src/layouts/navigation.ts`, `navigation.dashboard`).
- **Ruta:** `/` bajo `HomeLayout` (`src/layouts/HomeLayout.tsx`). Sin sesión, la misma dirección muestra la bienvenida
  pública (`WelcomePage` dentro de `AuthLayout`); con sesión (o mientras se cierra la sesión) monta el shell autenticado.
  La hoja es `src/app/AuthenticatedHome.tsx`, cargada lazy desde `src/app/routes.tsx`; no tiene `ProtectedRoute` con
  permiso: alcanza la sesión.
- **Decisión de qué se muestra** (`AuthenticatedHome`):
  - leyendo el usuario o la presentación del sistema: `Page` «Inicio» con spinner;
  - si falla cualquiera de las dos lecturas: `EmptyState` con el texto común de error y «Reintentar» (relee ambas);
  - si `user.permissions` incluye `ai.assistant.use` **y** `presentation.installedFeatures` incluye `"assistant"`:
    `AiAssistantPage` lazy, montada con `key={user.id}` (cambiar de cuenta la remonta y aísla borrador y acciones tardías);
  - si no: `DashboardPage` (`src/features/home/pages/DashboardPage.tsx`): `Page` «Inicio» con solo el aviso de
    recuperación de cuenta (`AccountRecoveryNotice`, «Agregá un correo para no perder el acceso si cambiás de número.»).
  - Si la persona pierde `ai.assistant.use`, un efecto retira del caché todo `["assistant", userId]`.
- **URL:** `/?conversacion={id}` abre una conversación guardada; `/` abre una conversación nueva (vacía, no se crea nada
  hasta la primera aceptación). El paginado del panel de conversaciones usa `page`, `pageSize` y `sort` en la URL
  (`usePagination`); elegir otra conversación los limpia.
- **Nunca en la URL ni en storage:** el borrador, el pedido congelado (UUID y cuerpo), el estado de aceptación incierta,
  las respuestas y las fuentes. Viven en el estado del componente o en el QueryCache privado.
- **Migas:** la barra superior muestra solo «Inicio» (es la raíz; no verificado en captura más allá del título de `Page`).

### 7.3 La pantalla, zona por zona

Archivos: `src/features/artificial-intelligence/pages/AiAssistantPage.tsx` (página y diálogos), componentes de la feature
en `src/features/artificial-intelligence/components/assistant/` (`AssistantConversationPanel`, `AssistantOperationState`,
`AssistantRuntimeProvider`, `AssistantSources`, `AssistantWelcome`) y presentación compartida en `src/shared/ui/assistant/`
(`AssistantWorkspace`, `AssistantThread`, `AssistantComposer`, `AssistantSuggestions`, `AssistantMessage`,
`AssistantConversationList`, `conversationActions.ts`). Estilos en `src/index.css` (clases `assistant-*` y
`conversation-*`). Tableros: «Inicio: composición assistant-ui corregida» y «Inicio: panel de conversaciones» en
[visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md#inicio-composición-assistant-ui-corregida--2026-10-04);
propuesta original en [inicio-asistente-propuesta](../../../../ArquitecturaBaseFront/docs/design/inicio-asistente-propuesta.md).

#### Marco general

- `Page viewport` con título «Inicio» y sin acciones en la cabecera. Debajo, el aviso de recuperación de cuenta (si
  corresponde) y un **marco con borde** (`conversation-workspace`) que ocupa todo el alto disponible.
- Adentro, de izquierda a derecha: el **panel de conversaciones** (256 px, plegable; solo escritorio ≥1024 px) y la
  **columna principal**: una barra de 48 px con el disparador del panel y el título actual, y debajo el **hilo**.
- Título de la barra: el de la conversación abierta (lo da el servidor), «Conversación» mientras no llega o si es
  genérico, o «Nueva conversación» en `/`.

#### Panel de conversaciones (`AssistantConversationPanel` + `AssistantConversationList`)

De arriba hacia abajo:
1. Encabezado «Conversaciones» con botón de ícono «Ocultar conversaciones» (chevron a la izquierda) en el borde derecho.
   Plegado, la barra del hilo muestra «Mostrar conversaciones» (chevron a la derecha).
2. Botón fantasma con lápiz **«Nueva conversación»** (todo el ancho). Deshabilitado mientras hay una acción en curso o
   una aceptación incierta.
3. **Lista** agrupada por día en la zona del perfil: «Hoy», «Ayer», «Anteriores» (`conversationGroups.ts`, fechas de
   calendario, no resta de 24 h; el "ahora" se recalcula al volver el foco o la visibilidad, sin temporizador). Cada fila:
   el título en una línea con elipsis (botón que abre la conversación) y un botón «…» con nombre «Acciones de {título}»
   que abre un menú con **«Eliminar conversación»** (ícono de tacho, color destructivo). El «…» aparece al pasar el
   mouse, con foco o en la fila activa; en táctil y en móvil siempre.
4. **Paginado** al pie, solo si `totalCount > 0`: «X–Y de Z» y flechas «Página anterior»/«Página siguiente»
   (deshabilitadas con una acción en curso). 20 conversaciones por página (el tamaño viene de la presentación del
   sistema si la URL no lo trae), orden `-lastActivityAtUtc`.

Estados del panel: cargando «Cargando conversaciones…» con spinner; error sin datos «No pudimos cargar las
conversaciones» con «Reintentar»; vacío «Todavía no tenés conversaciones». Si la página pedida ya no existe, vuelve a la
última.

#### Hilo vacío (conversación nueva)

Centrado verticalmente en una columna de 704 px (contenido 672 px):
1. Bienvenida `h2` **«¿Qué querés consultar?»** (en: «What would you like to know?»).
2. **Compositor** (ver abajo).
3. Línea de **disponibilidad**, solo si no está disponible.
4. **Sugerencias**: hasta 3 del servidor (`bootstrap.suggestions.slice(0, 3)`), en columna, como renglones con un
   chevron chico. Tocar una copia la pregunta en el compositor (no envía).

#### Hilo con turnos

- **Pregunta** a la derecha, en burbuja redondeada (máx. 90 % del ancho), con «Vos» y la hora en la zona del perfil.
- **Respuesta** a la izquierda, sin tarjeta, texto plano con saltos de línea (sin Markdown ni HTML), con «Asistente», la
  hora y el botón de ícono «Copiar».
- **Fuentes** debajo de la respuesta (`AssistantSources`): título «Fuentes verificadas» y, por cada evidencia, su nombre
  traducido («Usuarios», «Roles», «Permisos del rol», «Información del negocio», «Ayuda del sistema»; si el id no está
  traducido se muestra el id), «Consultado {fecha y hora}» y « · Resultado parcial» si `isComplete` es falso, las filas
  como pares etiqueta/valor, y el enlace **«Abrir fuente»** solo si el destino está en la lista blanca de
  `assistant/model.ts` y la persona tiene el permiso: `users`/`users.summary` → `/usuarios` (`users.read`), `roles*` →
  `/roles` (`roles.read`), `business*` → `/configuracion/negocio` (`ai.business.read`), `profile` → `/perfil`.
- **Zona de operación**, debajo de los mensajes:
  - una región `sr-only` `aria-live` que anuncia «Respuesta verificada» cuando el turno seguido termina;
  - «Cargar anteriores» si el historial tiene otra página (páginas de 20, orden `-ordinal`);
  - por cada turno que no está completo, `AssistantOperationState`: mientras corre, spinner + etapa («Consulta pendiente»,
    «Preparando la consulta», «Consultando las fuentes», «Preparando la respuesta», «Verificando las fuentes») y
    **«Cancelar consulta»** (pasa a «Cancelación solicitada», deshabilitado, cuando el servidor confirma el pedido de
    cancelación); terminado, el texto del estado («No se completó la consulta», «Resultado incierto», «Consulta
    cancelada», «Consulta vencida»), **«Consultar estado»** si es incierto y **«Preparar otra consulta»** si falló, es
    incierto, venció o se canceló y la pregunta es legible;
  - «Reintentar» si falló la lectura del turno o del historial;
  - si la aceptación quedó incierta: «Aceptación por confirmar» con **«Consultar estado»** y **«Repetir el mismo
    pedido»**;
  - tras un 429: «Esperá {N} s para enviar otra consulta.» con cuenta regresiva.
- Con turnos, el pie del hilo queda pegado abajo (`sticky`) con las sugerencias **arriba** del compositor, como chips
  que se envuelven, y la disponibilidad debajo.

#### Compositor (`AssistantComposer`)

- Área de texto redondeada de assistant-ui (no `FormField`), con nombre accesible «Tu pregunta», marcador
  «Preguntale al sistema…» y ayuda oculta para lector de pantalla «Enter agrega una línea. Ctrl+Enter o ⌘+Enter envían.»
  Respeta IME. A la derecha abajo, botón redondo con flecha «Enviar» (sin texto visible).
- **Deshabilitado** si: hay una acción en curso, una aceptación incierta, una espera por 429, un turno activo, contenido
  bloqueado, el bootstrap o la conversación se están releyendo, la disponibilidad no es `available`, o la conversación de
  la URL todavía no cargó. El botón Enviar además se apaga con el texto vacío.
- Validación al enviar: vacío «Ingresá una pregunta.»; más largo que `limits.questionMaxLength` (4000 por defecto)
  «La pregunta admite hasta {max} caracteres.»; un 400 del servidor con error en `question` se muestra igual, debajo, en
  rojo y asociado por `aria-describedby`. Escribir limpia el error.

#### Disponibilidad

Línea `role="status"` junto al compositor, sin caja: «El asistente está pausado», «El asistente todavía no está
configurado», «El modelo configurado todavía no está habilitado para consultas» o «La capacidad del asistente está
ocupada». A la derecha, el enlace «Revisar configuración» (a `/configuracion/ia`) si el servidor manda
`configurationDestination: "aiConfiguration"` y la persona tiene `ai.read`; si no, «Reintentar» (relee el bootstrap).

#### Diálogos

| Diálogo | Cuándo | Texto | Botones |
| --- | --- | --- | --- |
| «¿Empezar otra conversación?» (`Dialog`) | Elegir otra conversación o «Nueva conversación» con borrador escrito o turno activo | «Se descartará el borrador. La consulta activa puede seguir y la vas a encontrar en el historial.» | «Seguir editando»; si hay turno activo, «Cancelar y continuar» (cancela y recién entonces navega); principal «Continuar en segundo plano» (con turno activo) o «Nueva conversación» |
| Guarda de salida (`ConfirmDialog`) | Salir de la ruta o cambiar `conversacion` por el Atrás del navegador con borrador o aceptación incierta | Mismo título; descripción `newHelp` o, si es incierta, «No sabemos si se aceptó el pedido. Consultá su estado antes de enviar otro.» | «Seguir editando» / «Nueva conversación» o «Consultar estado» (este último se queda y consulta) |
| «¿Eliminar esta conversación?» (`ConfirmDialog` destructivo) | «Eliminar conversación» del menú de una fila | «Vas a perder el acceso a sus preguntas, respuestas y fuentes. También se pedirá cancelar cualquier consulta activa.» | «Cancelar» / «Eliminar conversación»; el foco vuelve al «…» de la fila |
| «¿Preparar otra consulta?» (`ConfirmDialog`) | «Preparar otra consulta» de un turno terminado | «Esta acción prepara el texto en el editor. Al enviarlo será un pedido nuevo y puede consumir la cuota del proveedor.» | «Cancelar» / «Preparar otra consulta» (copia la pregunta al compositor; no envía) |

Observaciones: el botón principal del primer diálogo dice «Nueva conversación» aunque la persona haya elegido una
conversación existente; y la guarda de salida a otra pantalla usa el mismo título «¿Empezar otra conversación?».

#### Estados y errores

| Caso | Qué se ve |
| --- | --- |
| Carga del bootstrap | Spinner dentro del marco |
| Error del bootstrap | `EmptyState` «No pudimos cargar la conversación» + «Reintentar» |
| Conversación de la URL cargando | Spinner |
| Error al leer la conversación (sin datos previos) | `EmptyState` «No pudimos cargar la conversación» + «Reintentar» |
| Contenido bloqueado (permisos actuales insuficientes) o vencido | `EmptyState` «Ya no tenés acceso al contenido de esta conversación» o «El contenido de esta conversación venció» + «Nueva conversación». El historial se retira del caché y los turnos guardados se sobrescriben sin pregunta, respuesta ni fuentes |
| 403 en cualquier lectura o acción | `ForbiddenPage` en lugar del chat; se borra el borrador, se purga todo `["assistant", userId]` y se relee el usuario actual |
| 409 al aceptar | Códigos `Ai.Assistant.RevisionConflict`, `ConnectionChanged`, `ActiveTurn` o `Unavailable`: toast de error y relectura de todo el asistente; se puede volver a enviar. Otro 409: queda como aceptación incierta |
| 429 | Toast de error y cuenta regresiva (`Retry-After` o 30 s); la pregunta queda en el compositor |
| Red o 5xx al aceptar | Aceptación incierta: toast warning «No sabemos si se aceptó el pedido…» con acción «Consultar estado»; nunca se repite solo |
| 404 al consultar la aceptación | Toast warning «Todavía no encontramos el pedido. Esto no confirma que haya fallado; podés consultar otra vez o repetir exactamente el mismo pedido.» |
| Turno que termina en falla, incierto o vencido | Un toast por pedido y estado (error, o warning con «Consultar estado» si es incierto), con el `failure.detail` del servidor, solo si se observó la transición o es el pedido recién aceptado |
| Falla al cancelar o eliminar | Toast (warning si es incierto); no se confunde con la aceptación |
| Error de lectura de una query | Toast global del QueryCache (las queries del asistente no declaran dueño local) |

#### 390 y 320 px

- Debajo de 1024 px (`useMediaQuery("(max-width: 1023px)")` y `@media (max-width:1023px)` en `index.css`) no hay panel
  lateral: la barra del hilo (44 px) muestra siempre «Mostrar conversaciones», que abre el panel como **cajón** a la
  izquierda (`Dialog` compartido, ancho `min(300px, 100vw - 32px)`, alto completo, sin encabezado propio del panel). Elegir
  una conversación o aceptar un pedido cierra el cajón; el foco vuelve al disparador.
- Filas, «Nueva conversación», el «…» y «Eliminar conversación» suben a 44 px de alto; el «…» queda siempre visible.
- El hilo ocupa todo el ancho con 16 px de relleno; las sugerencias se envuelven dentro del chat (la escena de 320 px lo
  comprueba) y no hay desplazamiento horizontal de la página (la escena del panel lo comprueba a 1440/390/320).
- Capturas de referencia a 1440/390/320 en es/en: `src/test/visual/screenshots/assistant-*-{es,en}-{1440,390,320}.png`.

### 7.4 Hooks y estado

**Claves** (todas bajo `assistantKey(userId) = ["assistant", userId]`, con idioma y "versión de permisos" = permisos
ordenados unidos por `|`; `staleTime: 0`, `gcTime: 60000`):

| Query | Clave | Notas |
| --- | --- | --- |
| Bootstrap | `[..., "bootstrap", idioma, permisos]` | En `AiAssistantPage` |
| Conversación | `[..., "conversation", id, idioma, permisos]` | `useAssistantConversation`; solo con `?conversacion` |
| Turno seguido | `[..., "turn", requestId, idioma, permisos]` | `requestId` = `activeRequestId` de la conversación o el del pedido recién aceptado. **Sondeo** cada `max(1, pollingAfterSeconds) × 1000` ms mientras `isActiveTurn`, nunca con la pestaña oculta, y se corta ante un error |
| Historial de turnos | `[..., "turns", id, idioma, permisos]` | `useInfiniteQuery`, páginas de 20, `-ordinal`; apagado si el contenido está bloqueado |
| Lista de conversaciones | `[..., "conversations", page, pageSize, sort, idioma, permisos]` | `useAssistantConversationList` |

**Invalidaciones y limpieza:**
- Cuando el turno seguido deja de estar activo: invalida lista, conversación e historial.
- Aceptar un pedido invalida la lista; eliminar retira conversación, historial y turnos de esa conversación, la saca de
  las páginas de la lista en caché y después invalida.
- Cancelar escribe la respuesta del POST en el caché del turno e invalida todo el asistente.
- Desmontar la página cancela y retira **todo** `["assistant", userId]`; cambiar la versión de permisos retira las
  queries de la versión anterior. Cambiar de conversación cancela las lecturas de la anterior.
- Una suscripción al QueryCache detecta cualquier 403 del asistente y pasa a `ForbiddenPage` (con purga).

**Acciones** (`useAssistantTurnActions`, sin `useMutation` ni MutationCache):
- `submit` congela el cuerpo (`createSubmission`: UUID nuevo, pregunta con `\r\n` normalizado y recortada,
  `expectedConnectionRevision` del bootstrap, `conversationId` y `expectedRevision` de la conversación abierta) y lo
  manda. Una referencia `working` bloquea envíos concurrentes antes de que React pinte el estado ocupado.
- Aceptación incierta: el cuerpo congelado queda; «Consultar estado» hace GET `turns/{requestId}`; «Repetir el mismo
  pedido» reenvía el mismo cuerpo y UUID. Nunca hay reintento automático.
- Un contador de generación y `mounted` ignoran resultados tardíos tras desmontar o cambiar de usuario; al desmontar se
  cierran los toasts propios.
- Al aceptar: borra el borrador, cierra el cajón, deja pasar la navegación y lleva a `?conversacion={id}`.
- La espera por 429 usa `useCountdown`.

**Runtime de assistant-ui** (`AssistantRuntimeProvider`): `ExternalStoreRuntime` con los mensajes proyectados desde los
turnos confirmados (`projectMessages`: solo `contentAccess === "allowed"`; pregunta siempre, respuesta solo si
`state === "Completed"`), `isRunning` = turno activo, y el adaptador experimental `threadList` (lista de la página
actual) cuyas callbacks solo piden la navegación con guarda. Si el envío falla, el texto vuelve al compositor del runtime.

**Privacidad:** sin edición, regeneración, adjuntos, Markdown ni herramientas en el navegador. El borrador y el pedido
congelado viven solo en el componente. El contenido bloqueado se retira del caché.

### 7.5 Cliente HTTP y tipos

`src/features/artificial-intelligence/api/assistant.ts`, objeto `assistantApi` sobre el `api` común
(`src/shared/api/httpClient.ts`, con sesión, `Accept-Language` y renovación 401):

| Función | Método y ruta |
| --- | --- |
| `bootstrap` | GET `/api/ai/assistant/bootstrap` |
| `conversations` | GET `/api/ai/assistant/conversations?page=&pageSize=&sort=` |
| `conversation` | GET `/api/ai/assistant/conversations/{id}` |
| `turns` | GET `/api/ai/assistant/conversations/{id}/turns?page=&pageSize=20&sort=-ordinal` |
| `submit` | POST `/api/ai/assistant/turns` (202) |
| `turn` | GET `/api/ai/assistant/turns/{requestId}` |
| `cancel` | POST `/api/ai/assistant/turns/{requestId}/cancel` |
| `remove` | DELETE `/api/ai/assistant/conversations/{id}` (204) |

**No hay guards zod.** Los tipos son interfaces TypeScript escritas a mano (`AssistantState`, `ContentAccess`,
`AssistantTurn`, `AssistantBootstrap`…) y `api.get<T>` no valida nada en tiempo de ejecución. Por eso el desajuste del
estado no rompe ninguna lectura: llega `"pending"`, el tipo dice `"Pending" | …`, y el código sigue como si fuera otro
valor. Además el back manda campos que el tipo no declara (por ejemplo `usage` en el turno), que se ignoran en silencio.
Es lo contrario de la regla de formularios (guards `strictObject` contra cuerpos exportados).

### 7.6 Fixtures y cohortes

**No hay cohorte HTTP exportada del asistente de Inicio.** `src/test/fixtures` no tiene carpeta para
`/api/ai/assistant` y `src/test/httpCohort.ts` no la declara (la familia `whatsapp-assistant-http` es del asistente en el
chat de WhatsApp, Etapa 5, no de Inicio). Las pruebas usan dobles TypeScript fabricados: `AiAssistantPage.test.tsx`
(`state: "Completed"`, `"Running"`) y `src/test/visual/assistantFixtures.ts` (`"Running"`, `"Uncertain"`, `"Failed"`,
`"Completed"`), tipados con las mismas interfaces del front. Ninguno parte de un cuerpo real del back, así que todos
repiten el error de mayúsculas.

### 7.7 i18n

Namespace `assistant` (`src/locales/{es,en}/assistant.json`), más `common` para «Inicio», «Reintentar» y el paginado.
Grupos: textos sueltos del chat, `availability.*` (cuatro motivos), `stages.*` (claves en minúscula: `pending`,
`preparing`, `consulting`, `composing`, `verifying`; el back también usa `finished`, que no tiene clave pero solo se
muestra con el turno activo), **`states.*` con claves en PascalCase** (`Pending`, `Running`, `Completed`, `Failed`,
`Uncertain`, `Canceled`, `Expired`), `sourceNames.*` (los cinco ids de herramienta) y `groups.*` (Hoy/Ayer/Anteriores).
La configuración de i18next (`src/shared/i18n/index.ts`) no tiene `fallbackNS` ni manejador de claves faltantes, así que
una clave inexistente se ve literal («states.pending»). Claves sin uso en el código: `welcomeHelp`, `history` y
`openConversation`. Voseo y paridad es/en como el resto (`src/locales/parity.test.ts`).

### 7.8 Pruebas

**Unitarias (Vitest):**
- `src/features/artificial-intelligence/pages/AiAssistantPage.test.tsx` (17 casos): chat nuevo con panel interno y
  reanudar solo eligiendo; conservar borrador y selección si se cancela el cambio; plegar el historial sin generar al
  paginar; anunciar una transición terminal una sola vez en StrictMode; no avisar como error la cancelación esperada;
  un envío tipado que publica la evidencia confirmada y sigue la revisión; Enter como salto e IME antes de Ctrl+Enter;
  no repetir una aceptación perdida y recuperar por UUID sin borrar el borrador; purgar historial bloqueado; retirar
  historial al revocar el permiso de la fuente; ocultar fuentes al perder el permiso; preguntar antes de que el Atrás
  cambie de conversación; conservar borrador al cancelar «Nueva conversación»; mostrar la cancelación durable mientras
  sigue activa; publicar la respuesta si la cancelación pierde la carrera; no consultar la API sin el módulo; espera y
  pregunta conservada ante 429.
- `src/features/artificial-intelligence/hooks/useAssistantTurnActions.test.tsx` (8 casos): renovación 401 con el mismo
  UUID, cuerpo y `Accept-Language`; conflictos de revisión que releen; ignorar la aceptación de otro dueño; no publicar
  una cancelación tras desmontar; no usar la recuperación de aceptación tras borrar o cancelar con resultado incierto;
  no repetir solo un POST incierto; bloquear envíos concurrentes.
- `src/features/artificial-intelligence/assistant/model.test.ts` (5) y `conversationGroups.test.ts` (2).
- Todas usan estados con mayúscula: confirman el comportamiento contra el contrato equivocado.

**Visuales (Playwright, `src/test/visual`):**
- `assistant.spec.ts`: 11 escenas (`empty`, `answer`, `running`, `cancel-requested`, `paused`, `missing`, `model`,
  `uncertain`, `failed`, `blocked`, `load-error`) × es/en × 1440/390/320 = 66 capturas
  (`assistant-{escena}-{idioma}-{ancho}.png`); además, recuperar la aceptación y la espera por idioma, y conservar la
  posición de lectura y el borrador ante un GET fallido con borrado solo tras confirmar.
- `assistant-layout.spec.ts`: sugerencias dentro del chat a 320 px; geometría de la composición elegida (panel de 256
  px, compositor ≤704 px centrado entre el 25 % y el 70 % del alto, sugerencias debajo, radio ≥16 px, Enviar sin texto,
  ayuda oculta). Incluye también una prueba de recuperación de un import fallido de la ruta de WhatsApp.
- `assistant-conversations.spec.ts`: 6 capturas `assistant-conversations-{es,en}-{1440,390,320}.png`; paginado
  «21–24 de 24» con `pageSize=20`, conservar el borrador al cancelar el cambio, foco de vuelta a la fila, menú por
  teclado, cancelar el borrado sin escribir, alto táctil de 44 px y sin desplazamiento horizontal.
- Total de referencias del asistente: 72 PNG (12 familias × 6).

Verificaciones históricas del front: [corrección de la composición](../../../../ArquitecturaBaseFront/docs/verification/inicio-asistente-correccion-2026-10-04.md)
y [verificación inicial](../../../../ArquitecturaBaseFront/docs/verification/inicio-asistente-2026-10-04.md). Review del
back de entonces: [2026-10-04](../../reviews/2026-10-04-inicio-asistente.md).

### 7.9 Avisos «No copiar»

> **No copiar — review E2, punto 34** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)): el contrato
> publicado es el del back (estados en minúsculas: `pending`, `running`, `completed`, `failed`, `uncertain`, `canceled`,
> `expired`). Lugares del front que lo esperan con mayúscula en `982ade8`:
> - `src/features/artificial-intelligence/api/assistant.ts:3-10`: el tipo `AssistantState` es `"Pending" | "Running" | …`.
> - `src/features/artificial-intelligence/assistant/model.ts:11`: `isActiveTurn` compara con `"Pending"` y `"Running"`.
>   Con `"pending"` devuelve falso: el sondeo del turno (`refetchInterval` de `useAssistantConversation`) no arranca,
>   `active` pasa a falso apenas llega el turno, el compositor se rehabilita y el efecto "terminó" invalida lista e
>   historial como si hubiera concluido. El turno no se vuelve a pedir (salvo, por lo deducido del código, un refetch al
>   volver el foco).
> - `model.ts:44`: `projectMessages` solo agrega la respuesta si `state === "Completed"`: con `"completed"` la respuesta
>   nunca se dibuja, ni siquiera al recargar.
> - `src/features/artificial-intelligence/components/assistant/AssistantOperationState.tsx:32`: muestra
>   `t(\`states.${turn.state}\`)` y las claves son PascalCase: se ve «states.pending» (o «states.completed» en un turno
>   terminado, deducido del código). `:44` (`"Uncertain"`) y `:55` (`["Failed","Uncertain","Expired","Canceled"]`): nunca
>   aparecen «Consultar estado» ni «Preparar otra consulta».
> - `src/features/artificial-intelligence/pages/AiAssistantPage.tsx:204,215,218`: los toasts de falla, incierto y
>   vencido no se disparan nunca. `:410-411`: el anuncio accesible «Respuesta verificada» nunca suena. `:427`: el filtro
>   `turn.state !== "Completed"` deja pasar también los turnos completos, que se dibujan como estado operativo.
> - `src/locales/{es,en}/assistant.json:60-66`: las claves `states.*` en PascalCase.
> - Dobles: `AiAssistantPage.test.tsx:63,132,209,248,448` y `src/test/visual/assistantFixtures.ts` fabrican estados con
>   mayúscula; las 72 capturas de referencia muestran un comportamiento que en la app real no ocurre.
>
> **Cómo hacerlo en el multitenant:** un único enum del contrato validado con zod (`z.enum([...])` en minúsculas o el
> casing que fije el back, pero uno solo, generado o probado contra el OpenAPI), claves i18n derivadas de ese enum, y
> ejemplos HTTP reales exportados del back (bootstrap, enviar, turno, turnos y cancelar) consumidos con `strictObject`,
> como pide el arreglo del review. **Estado en `982ade8`: pendiente** (no hay commits del front sobre el asistente en el
> período).

> **Revisar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md)):
> el punto 38 del review no nombra esta pantalla. No tiene «Cambios sin guardar», números de revisión ni validación
> anticipada (valida al enviar). Lo que más se acerca a «textos al lado de los botones» es la línea «Esperá {N} s para
> enviar otra consulta.» bajo el compositor deshabilitado y el bloque «Aceptación por confirmar»; ambos describen un
> estado operativo durable y el tablero de Inicio pide la disponibilidad junto al editor, así que no se marcan como
> error, pero al rehacerla conviene decidir si la espera va como tooltip del botón Enviar.

**Regla de idioma del 2026-10-23:** no aplica. El contenido es la pregunta de la persona; no hay selector «Textos en» ni
campos por idioma. Las sugerencias y los nombres de fuentes vienen en el idioma de la interfaz.

### 7.10 Para el multitenant

- **Corregir el contrato antes de copiar nada** (7.9). Es el único error funcional grave de la pantalla: tal como está,
  el chat no sirve.
- **Claves por organización:** hoy todo cuelga de `["assistant", userId]`. Con usuarios que pertenecen a varias
  organizaciones hay que sumar el tenant a la clave raíz, montar la página con `key={tenantId + userId}` y purgar al
  cambiar de organización (igual que hoy al cambiar de usuario o de permisos).
- **Quién ve el chat:** `installedFeatures` y `ai.assistant.use` deben resolverse por organización. Las cinco fuentes son
  de administración B2B (usuarios, roles, permisos, información del negocio): para usuarios B2C probablemente Inicio deba
  ser el `DashboardPage` o una variante sin estas fuentes.
- **Lista blanca de destinos** (`assistant/model.ts`): si los módulos instalados varían por organización, los enlaces
  «Abrir fuente» tienen que depender también de lo instalado, no solo del permiso.
- **Cuotas, espera y capacidad** (`capacityLimited`, 429) pasan a ser por organización en el back; el front ya muestra lo
  que dice el servidor.
- **Zona horaria y grupos Hoy/Ayer:** salen del perfil; definir el valor por defecto por organización.
- **Detalles menores a no repetir:** el botón «Nueva conversación» del diálogo cuando se eligió una conversación
  existente; el mismo título para la guarda de salida; claves i18n sin uso; tipos a mano sin validación.

### 7.11 Endpoints

Sin cambios en el período. Los ocho de `/api/ai/assistant` de 7.5, todos con `ai.assistant.use`, `no-store` y límites
de lectura/escritura propios del módulo (`AiAssistantController.cs`; contrato en
[inteligencia artificial](../../features/inteligencia-artificial.md#contrato-y-privacidad) y el
[ADR 0017](../../decisions/0017-inicio-asistente-y-operaciones-durables.md)). Panel interno:
[identificación del historial](../../features/inteligencia-artificial.md#panel-interno-e-identificación-del-historial--2026-10-05).

### 7.12 Huecos

- No se levantó la app (restricción de solo lectura): que se vea «states.pending» y nunca la respuesta sale del review y
  de leer el código; que un turno terminado muestre «states.completed» y que un refetch al volver el foco no lo arregle
  es deducción del código, no verificado en navegador.
- Si antes de `ceaed208` (2026-10-09) el back devolvía el estado en PascalCase desde otro archivo: no verificado.
- Migas de `/`: no verificadas en captura.

---

## 8. Catálogos E1: tabla editable, pegar e importar, tipo de valor, elegir varias y vista previa

Relevado sobre el front en `982ade8` (2026-10-23), leyendo sólo lo commiteado. Los tableros son CA1-A y CA2–CA6 de
[mejoras de catálogos](../../../../ArquitecturaBaseFront/docs/design/catalogos-mejoras.md), elegidos el 2026-10-21, más
el ajuste de claridad de la vista previa de la versión 110 del lienzo. Las reglas funcionales del back están en:

- [base de E1](../../features/formularios.md#catálogos-base-de-la-etapa-e1--2026-10-21);
- [tipo de valor](../../features/formularios.md#catálogos-con-tipo-de-valor--e1-b-2026-10-22);
- [carga desde una planilla](../../features/formularios.md#carga-de-catálogos-desde-una-planilla--e1-a-2026-10-22);
- elegir varias: [base y captura](../../features/formularios.md#elegir-varias-base-y-captura--e1-c1-2026-10-22),
  [lecturas](../../features/formularios.md#elegir-varias-lecturas--e1-c2-2026-10-22),
  [WhatsApp](../../features/formularios.md#elegir-varias-en-whatsapp--e1-c3-2026-10-22) y
  [front](../../features/formularios.md#elegir-varias-en-el-front--e1-c4-2026-10-22).

E2 cambió después varias de estas piezas: las columnas de campos extra en la misma tabla, la sección «Campos», la
presentación «Qué viaja a WhatsApp», «Cambiar el tipo» de un campo, importar campos extra y la vista previa por persona.
Esta sección describe la base E1 tal como quedó en `982ade8`. Lo que agregó E2 está en las secciones 9 (editor del
catálogo) y 10 (datos de las personas, la pregunta y las respuestas); acá sólo se nombra donde toca la misma pantalla.

### 8.1 Resumen

**Qué cambió desde `9261e3d`.** El anexo anterior (E2.3, tablero K3) describía un editor Manual con una lista de
opciones, cada una con su lápiz, y nada más. E1 suma:

1. **Tipo de valor (CA3).** Un catálogo Manual se crea como Texto (por defecto), Entero, Decimal o Fecha, y el tipo no
   se cambia después. **No hay tipo Sí/No para el catálogo:** el booleano existe sólo como tipo de un campo extra de E2.
   Un catálogo tipado suma la columna «Valor», obligatoria y validada sin pasar por `Number`.
2. **Tabla editable (CA1-A).** Desde 640 px y con permiso de administrar, el texto, el código, el valor, el detalle y
   «Activa» se editan en la fila. Hay una casilla por fila y otra para toda la página, para dar de baja o reactivar sólo
   lo elegido de esa página. El texto cuenta hasta 30, un código repetido bloquea Guardar y dice en qué fila está el
   otro, y un texto repetido sólo avisa. «⋯» trae ordenar A–Z (se aplica al guardar), generar los códigos que faltan y
   descargar la plantilla. Debajo de 640 px no hay edición en la tabla: se edita por opción.
3. **Pegar desde Excel e importar (CA2).** El mismo diálogo de tres pasos (archivo o filas, revisar, aplicar) usa una
   vista previa del servidor: pegar es Append e importar XLSX/CSV es Replace. Las filas importadas nunca entran al
   borrador del navegador. Exportar y descargar la plantilla bajan un XLSX del servidor.
4. **Vista previa «Así lo ve el empleado» (CA1-A, ajuste v110).** Muestra el campo cerrado, la lista con círculos al
   tocarlo y un selector «Una opción / Varias» que cambia sólo la ilustración.
5. **Elegir varias (CA4/CA5).** «Elegir de una lista» y «Elegir de un catálogo» suman «Permitir elegir varias», con
   mínimo y máximo (hasta 20). Por dentro es otro tipo de pregunta (`multiChoice`) con identidad nueva. Las respuestas
   se leen como lista en el orden elegido: planilla, filtro «alguna de», detalle, chat, Excel y PDF. Reglas y
   sugerencias suman el operador «Eligió alguna de».
6. **Valores sellados.** La respuesta trae el par `value`/`valueType` de la opción elegida. Las pantallas siguen
   mostrando sólo la etiqueta histórica; el valor aparece en el Excel y en el PDF, que arma el servidor.

**Commits clave** (cronológicos; hay más de prueba y de evidencia):

| Tema | Commits |
| --- | --- |
| Diseño congelado | `4017845` docs(catálogos): congelar los tableros elegidos de las mejoras de catálogos · `193bc14` enlazar el resumen de la etapa |
| Refactors previos | `9638e3e` centralizar las opciones de respuesta · `d1652a3` compartir los campos de mínimo y máximo (`SelectionRangeFields`) · `e0ebe9a` unificar el editor de condiciones (`FormConditionsEditor`) · `1a6fe77` separar la tabla y los campos del catálogo |
| Tipo de valor | `ca61129` convertir valores de catálogo sin perder precisión · `243c31b` editar valores tipados de catálogos · `094899c` leer valores sellados sin cambiar etiquetas históricas · `9aab213` (whatsapp) aceptar el par tipado de opciones capturadas |
| Carga e importación | `bf4440b` (http) enviar archivos con FormData y sesión compartida · `1d077c0` preparar importación privada · `dac8d49` preparar validación y orden de opciones · `194f71b` revisar importaciones privadas · `865e454` activar la carga elegida de opciones |
| Elegir varias | `1c5a0bd` preservar identidad y rango · `89a2c45` editar preguntas de varias opciones con confirmación · `539aa48` leer selecciones múltiples históricas · `5d28fbf` (whatsapp) mostrar selecciones múltiples capturadas · `7c7cdab` habilitar filtros y reglas de varias opciones · `0473958` nombrar opciones múltiples en el idioma elegido |
| Contratos HTTP | `127943e` catálogos tipados · `adee248` opciones múltiples · `62bbcab` (whatsapp) respuestas múltiples · `2d046f8` importaciones · `c14e8de` importaciones en la procedencia conjunta · `9e7a2a1` aislar el transporte multipart de jsdom · `66eaba5`, `0f478f6`, `a23c83b` regenerar exportaciones |
| Arreglos del review E1 | `b7b47c5` traducir la cancelación del descarte · `eec996b` valores normalizados al importar · `aabec44` (whatsapp) explicar el límite de varias · `4c442af` comillas dentro de celdas pegadas · `139dfff` errores y avisos separados · `a2a1411`, `ef05d92` identificar la hoja que excede los topes · `76074cb` textos propios para filas pegadas · `de1faf8` tope de varias en la vista previa · `febf258` (whatsapp) detalles por opción elegida · `40a1c74` textos del diseño · `3027563` columnas de la revisión · `040e1f4` encabezado desconocido literal · `e78a946` título separado del cierre |
| Pruebas visuales y evidencia | `cccbac9`, `3cbbb13`, `e433e9b`, `5e2701c`, `36233ec`, `f9e1009` · docs `917712d`, `738300c`, `9f0d270` (cierre visual del review E1) |
| Después de E1 que toca estas pantallas | `3566ffb` unificar las columnas (`catalogColumns.ts`), `89462fd` tabla ancha (`WideTable`), `db1ab5e` importar campos extra, `9efd8e2` el diálogo usa el catálogo confirmado (punto 12 del review E2), `584aec8`/`982ade8` celdas de catálogo vacías (punto 13) |

### 8.2 Navegación y rutas

No hay rutas nuevas. Cambian los parámetros y el contenido de las existentes.

| Ruta | Permiso de la ruta | Qué cambió en E1 |
| --- | --- | --- |
| `/catalogos` | `formcatalogs.read` | «Nuevo catálogo» abre el diálogo de origen, que suma «Tipo de valor» para Manual |
| `/catalogos/nuevo?valueType=Integer\|Decimal\|Date` | `formcatalogs.read` + `formcatalogs.manage` | `valueType` viaja en la URL sólo hasta el primer guardado; Texto no lo escribe. Un valor desconocido cae a Texto (`isCatalogValueType`). `?kind=Filters` sigue eligiendo Filters |
| `/catalogos/{catalogId}` | `formcatalogs.read` (escribir: `formcatalogs.manage`) | Tabla editable, importación y exportación. Sigue con `page`, `pageSize` y `search` del listado de opciones en la URL |

- **Migas:** `Inicio / Gestión de formularios / Catálogos / {nombre}`. El grupo pasó a llamarse «Gestión de formularios»
  con MF1 (sección 5). La hoja es `draft.name[scope.culture]`, el nombre en el **idioma de la interfaz**, o «Nuevo
  catálogo».
- **En la URL:** el tipo de un alta, la página, el tamaño y la búsqueda de opciones.
- **Nunca en la URL ni en storage:**
  - el archivo elegido, el texto pegado, la vista previa de importación y sus confirmaciones;
  - el borrador de opciones, los movimientos, la marca «ordenar A–Z» y la selección por página;
  - el filtro «Con error · N filas» de E2;
  - los bytes exportados.
- **Guarda de salida:** `useUnsavedChangesGuard(editor.dirty || editor.busy, ["kind", "valueType"])`. Cambiar sólo
  esos parámetros no cuenta como salir.
- Al guardar un alta, la ruta se reemplaza por `/catalogos/{id}` (`navigate(..., { replace: true })`) con la guarda
  autorizada.

### 8.3 Pantallas y diálogos

#### 8.3.1 Diálogo «Nuevo catálogo» con tipo de valor — CA3 (`src/features/business-forms/components/CatalogOriginDialog.tsx`)

Se abre desde «Nuevo catálogo» del listado, sólo con `formcatalogs.manage`. Es el diálogo K2 del anexo anterior, con un
bloque más.

- **Título:** «Nuevo catálogo».
- **Descripción:** «¿De dónde salen las opciones? No se puede cambiar después: para otro origen, se arma otro catálogo.»
- **Origen:** tarjetas «Escribirlas a mano» y «Tomarlas de la base, con filtros», como en K2.
- **Tipo de valor**, sólo si el origen es Manual: título «Tipo de valor» y radios «Texto» (por defecto), «Entero»,
  «Decimal» y «Fecha». Debajo: «Se elige al crear el catálogo y después no se puede cambiar.»
- **Pie:** «Cancelar» y «Continuar».
- **Continuar:** no crea nada; navega a `/catalogos/nuevo`, con `?valueType=` si el tipo no es Texto. Filters siempre
  manda Texto.
- **Estados:** no hay carga, error, 403, 409 ni 429; el diálogo no llama al servidor.
- **Celular:** diálogo central común (`DialogContent` sin `mobileFullscreen`), al ancho menos 2 rem. Los radios quedan
  en una columna. Sin escena visual propia (no verificado con captura).

> **No copiar — review E2, punto 39** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)): «De dónde
> salen» y «Tipo de valor» se eligen en este paso. Después se muestran como dato de sólo lectura en una línea, sin
> caja ni explicación; ver 8.3.2. El review no dice si este diálogo conserva sus textos «No se puede cambiar
> después…» (a confirmar con el usuario). Estado en `982ade8`: pendiente.

#### 8.3.2 Editor Manual: disposición general (`src/features/business-forms/pages/CatalogEditorPage.tsx`, `ManualCatalogEditor`)

De arriba hacia abajo, a 1440 px:

1. **Cabecera (`Page`):**
   - flecha «Catálogos» y título con el nombre del catálogo en el idioma de la interfaz;
   - estado en texto chico: «Usado en N formularios · » (sólo si `usageCount` no es null), «Activo» o «Dado de baja» y,
     si hay cambios, « · Cambios sin guardar»;
   - acciones, con manage y sólo en Manual: «Dar de baja» o «Reactivar» (`CatalogStateAction`, que confirma y se
     aplica con Guardar; sólo si el catálogo ya existe), «Descartar» y «Guardar».
   - **Guardar** queda deshabilitado:
     - sin capacidad de guardar (falta manage, guardado en curso o revisión pendiente);
     - con un valor tipado inválido;
     - con un campo extra inválido (E2);
     - con un problema local: texto en español vacío o de más de 30, código vacío o repetido.
2. **Grilla de dos columnas desde `xl` (1280 px):**
   - a la izquierda:
     - la superficie «Textos en» (selector es/en);
     - «El catálogo» (`CatalogMetadataFields`);
     - «Presentación» de E2 (`CatalogPresentationSection`, sección 9);
   - a la derecha, pegada arriba al desplazar (`xl:sticky`): «Así lo ve el empleado» (8.3.5).
3. **«Campos»** de E2 (`CatalogFieldsSection`, sección 9).
4. **Si se pidió ordenar A–Z:** superficie con «Se ordenará A–Z al guardar.» y «Cancelar orden A–Z».
5. **Si hay problemas locales:** párrafo rojo «Revisá las opciones marcadas antes de guardar.»
6. **Si hay movimientos encolados:** superficie con «N movimientos pendientes. Se aplicarán en este orden al guardar.» y
   «Descartar movimientos». Viene de K3.
7. **Superficie «Opciones»** (8.3.3).

**«El catálogo» (`src/features/business-forms/components/CatalogMetadataFields.tsx`):**

- «Nombre» (obligatorio, hasta 120);
- «De dónde salen», un `Input` de sólo lectura con «Escritas a mano» y el hint «¿De dónde salen las opciones? No se
  puede cambiar después…»;
- «Tipo de valor» (nuevo en E1), otro `Input` de sólo lectura con «Texto», «Entero», «Decimal» o «Fecha», y el hint
  «Se elige al crear el catálogo y después no se puede cambiar.»;
- «Descripción» (obligatoria, hasta 1000), con el hint «La leen las personas para saber cuándo usarlo.»

La superficie «Textos en» explica: «Completá el nombre y la descripción en español e inglés. En las opciones, el inglés
es opcional.»

**Estados de la página:**

| Estado | Qué se ve |
| --- | --- |
| Sin usuario | Spinner en `Page` |
| Sin `formcatalogs.read` o 403 del detalle | `ForbiddenPage` («No tenés permiso») |
| 403 al leer opciones o exportar | `ForbiddenPage`, se pierde el borrador visible |
| Cargando el detalle | Spinner |
| 404 del detalle | EmptyState «No encontramos este catálogo.» con «Reintentar» |
| Otro error del detalle | EmptyState «No pudimos cargar el catálogo.» con «Reintentar» y un toast con la causa |
| Alta sin `formcatalogs.manage` | `ForbiddenPage` |
| `kind` Filters o `?kind=Filters` | Delega en `CatalogFiltersEditorPage` |

**Celular (390 y 320):**

- no hay breakpoints propios entre 320 y 390: es el mismo apilado;
- la vista previa baja debajo de «Presentación», antes de «Campos» y «Opciones»;
- la cabecera de `Page` baja las acciones a otra línea, alineadas a la derecha;
- evidencia: [CA1-A a 390](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/e1-ca1-es-390.jpg) y
  [a 320](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/e1-ca1-es-320.jpg).

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), commit `bb98bf2`):
> - la cabecera no muestra « · Cambios sin guardar»: que Guardar esté habilitado ya lo dice;
> - el párrafo «Revisá las opciones marcadas antes de guardar.» es un texto que explica el botón. Los errores van en sus
>   campos y Guardar sólo se habilita o se deshabilita;
> - es el punto 38 del review E2, que nombra `CatalogEditorPage`. Estado en `982ade8`: pendiente
>   (`t("dirty")` y `catalogs.items.reviewInvalid` siguen en la página).
>
> Las superficies «Se ordenará A–Z al guardar.» y «N movimientos pendientes…» son estado de borrador junto a un botón.
> El review no las nombra; conviene revisarlas contra la misma regla antes de copiarlas.

> **No copiar — regla de idioma del 2026-10-23** (review E2, punto 35):
> - acá hay un selector «Textos en» y el nombre y la descripción exigen español e inglés. El guard
>   `requiredText` de `src/features/business-forms/api/catalogResponse.ts` rechaza un detalle sin `en`, y el hint dice
>   «Completá … en español e inglés»;
> - la regla nueva: se escribe en el idioma del menú, que es el original y el único obligatorio, y la traducción se carga
>   cambiando el idioma del menú;
> - al crear un catálogo sólo en español, «Guardar» parece no hacer nada: el 400 trae `name.en` y la página cambia
>   sola a la pestaña «en» (`setCulture("en")`), sin otra marca visible.
>
> Estado en `982ade8`: pendiente.

> **No copiar — review E2, punto 39:**
> - «El catálogo» debe tener sólo Nombre y Descripción, y la descripción es opcional;
> - «De dónde salen» y «Tipo de valor» van como una línea de sólo lectura arriba de Nombre (por ejemplo «Escritas a mano ·
>   Texto»), sin caja ni explicación;
> - hoy son dos `Input` de sólo lectura con hints que explican que no se pueden cambiar, y la descripción es
>   obligatoria.
>
> Estado en `982ade8`: pendiente.

#### 8.3.3 Superficie «Opciones» — CA1-A

Archivos: `src/features/business-forms/components/CatalogManualItemsEditor.tsx`, `CatalogItemsTable.tsx`,
`CatalogItemFields.tsx`, `CatalogInlineInput.tsx` y `CatalogOptionsActions.tsx`, todos en `src/features/business-forms/components/`;
las columnas salen de `src/features/business-forms/catalogColumns.ts`.

**Banda (`Surface` con título «Opciones»), acciones a la derecha:**

- **Desde 640 px**, en este orden:
  - «+ Agregar opción» (con manage);
  - «Pegar desde Excel» e «Importar» (con manage). Quedan deshabilitados si el catálogo no se guardó («Guardá el
    catálogo antes.») o si hay cambios sin guardar («Guardá o descartá los cambios antes de importar.»);
  - «Exportar», también sólo con lectura; deshabilitado hasta guardar;
  - «⋯» («Más acciones de opciones»):
    - «Descargar la plantilla», sólo con lectura;
    - con manage, separados: «Ordenar A–Z» y «Generar los códigos que faltan».
  - Debajo de los botones, un párrafo con el motivo del deshabilitado, enlazado por `aria-describedby`.
- **Debajo de 640 px:** sólo «+ Agregar opción» y «⋯». El menú lleva «Pegar desde Excel», «Importar», «Exportar», un
  separador, «Descargar la plantilla», «Ordenar A–Z» y «Generar los códigos que faltan».
- Cada ítem no disponible conserva el foco con `aria-disabled` y muestra el motivo como una segunda línea de texto
  chico dentro del ítem.
- **Motivos de «Ordenar A–Z»:** «Guardá el catálogo antes.», «Primero descartá los cambios de posición.» o «Se ordenará
  A–Z al guardar.»
- Al abrir el diálogo de importación desde el menú, el foco de vuelta va al disparador real (`focusRef`), no a «⋯».

> **No copiar — review E2, punto 37** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)) **y regla de interfaz del 2026-10-23:**
> - el párrafo visible con «Guardá el catálogo antes.» o «Guardá o descartá los cambios antes de importar.» empuja y
>   desalinea los botones. Está en `CatalogOptionsActions.tsx`, en el `<p id={reasonId} className="w-full …">`;
> - lo decidido: sin texto visible, todas las acciones alineadas al margen derecho de la sección a la altura del título,
>   y el motivo como tooltip al pasar el mouse o con el foco, y para el lector de pantalla;
> - la prueba `CatalogOptionsActions.test.tsx` («keeps a new catalog's reason visible…») fija hoy el comportamiento
>   viejo.
>
> Estado en `982ade8`: pendiente. Lo mismo vale para la segunda línea de motivo dentro de los ítems de «⋯»: el review
> no la nombra, pero es un texto junto a la acción (a revisar con la regla).

**Debajo de la banda:**

- Con opciones elegidas en la página: «N seleccionada(s) de esta página», «Dar de baja», «Reactivar» y «Quitar
  selección». Los dos primeros sólo preparan cambios en el borrador; se guardan con Guardar. Cambiar de página vacía la
  selección: la clave es la lista de `localId` de la página.
- El buscador «Buscar opción», sólo en un catálogo ya guardado. Con campos extra activos (E2), al lado aparece el
  desplegable «Campos», que elige las columnas visibles. Esa elección vive sólo en memoria.

**Tabla (`WideTable`).** Las columnas salen de `catalogItemColumns(valueType, culture, fields)`:

| Columna | Desde 640 px y con manage (edición en la fila) | Debajo de 640 px o sin manage |
| --- | --- | --- |
| Casilla | «Seleccionar {nombre}»; en el encabezado, «Seleccionar opciones de esta página» (indeterminado si hay algunas). Sólo con manage | Igual, sólo con manage |
| # | Posición confirmada; «—» en una opción nueva | Igual |
| Texto | `Input` en el idioma de contenido elegido, contador «N/30» y error o aviso debajo. Fija a la izquierda (`pinned="start"`) | Se vuelve «Texto y código»: texto con el código debajo |
| Código | `Input` (hasta 80) | No se muestra como columna |
| Campos extra de E2 | Editor por tipo (sección 9) | Texto formateado o «—» |
| Valor (sólo si no es Texto) | `Input` de texto con `inputMode` numérico o decimal; en Fecha, `type="date"` | Fecha con formato medio («15 mar 2027»); número como se cargó; o «—» |
| Detalle | `Input` (hasta 300) | Texto o «—» |
| Estado | Casilla «Activa: {nombre}» | Insignia «Activa» o «Dado de baja» |
| Acciones | Fija a la derecha: Subir, Bajar y lápiz «Editar {nombre}»; en una opción inactiva, «Reactivar» en lugar del lápiz | Igual |

**Reglas de la fila:**

- Las flechas se deshabilitan en el primer y el último lugar, con «Ordenar A–Z» pendiente y en una opción nueva. Ahí
  el nombre accesible suma «Guardá la opción para poder moverla.»
- **El lápiz abre la opción entera:**
  - desde 640 px, en una fila de la tabla debajo de la suya;
  - debajo de 640 px, después de la tabla, con el título «Editando {nombre}».
- **La edición completa (`CatalogItemFields`)**, en una grilla de dos columnas desde `sm`:
  - «Texto» con contador «N/30», obligatorio sólo en español. En inglés lleva la ayuda «Si no completás el texto en
    inglés, se usa el de español.»;
  - «Código», obligatorio, con la ayuda «El código es para la empresa: no se muestra en WhatsApp.»;
  - los campos extra (E2);
  - «Valor · {Tipo}», con ayuda por tipo: «Número entero, hasta 18 dígitos.», «Número decimal, sin redondeos.» o
    «Fecha sin hora.»;
  - «Detalle», opcional;
  - si falta el código, el botón «Generar desde el texto»;
  - la casilla «Activa», con «Una opción dada de baja deja de ofrecerse en los envíos nuevos. Las respuestas que ya la
    eligieron siguen mostrándola.»;
  - «Listo», que cierra y devuelve el foco al lápiz de esa opción, o a «Agregar opción» si la fila ya no está.
- **«Agregar opción»** crea una fila vacía al final de la página visible (`itemId` null), la abre y enfoca «Texto».
  Las opciones nuevas se agregan al final de cualquier página que se mire, hasta guardar.

**Validación local** (`src/features/business-forms/catalogEditing.ts`, sobre todo lo conocido del borrador):

- código vacío: «Completá este campo.»;
- código repetido sin distinguir mayúsculas: «Repetido: también está en la fila N.» o «Repetido: otra opción nueva
  tiene el mismo código.»;
- texto en español vacío: «Completá este campo.»;
- texto de más de 30 en cualquier idioma: «WhatsApp muestra hasta 30 caracteres.» No se recorta;
- texto repetido, sólo aviso en ámbar: «También está en la fila N: en WhatsApp se verán iguales.» o «Otra opción nueva
  tiene el mismo texto…»;
- los errores del servidor `items[n].…` se ligan a la identidad de la opción por `indexedValidationIssues`: la página
  abre la primera opción con error y cambia la pestaña de idioma si el error es `.en`, sin toast.

**Ordenar A–Z y generar códigos:**

- **«Ordenar A–Z»** no reordena en pantalla. Marca `sortAlphabetically` en el borrador, apaga las flechas y viaja como
  una sola orden en el PUT, sin movimientos (`catalogDraftUpdateBody`). Es incompatible con movimientos encolados.
- **«Generar los códigos que faltan»** arma el código desde el texto **en español**: sin tildes, en mayúsculas, con
  guiones y hasta 80 caracteres. Desambigua con «-2», «-3»… contra los códigos conocidos y no toca los que ya existen.

**Estados de la superficie:**

| Estado | Qué se ve |
| --- | --- |
| Cargando | Spinner dentro de una superficie |
| Error sin datos | EmptyState «No pudimos cargar las opciones.» con «Reintentar» |
| Vacío | «Todavía no hay opciones.» |
| Error con datos | Conserva la tabla y muestra un toast |
| 403 | La página pasa a `ForbiddenPage` |
| Sin manage | Sin casillas ni flechas, sin «Agregar», «Pegar» ni «Importar»; tabla compacta de sólo lectura con «Exportar» y la plantilla |
| Perder manage con el editor abierto | El borrador queda de sólo lectura mientras siga la lectura |

**Paginado:** `Pagination` común. Cambiar de página o de tamaño cierra la opción abierta y conserva los cambios ya
hechos en otras páginas.

**Celular (390 y 320):**

- la tabla compacta se desplaza a lo ancho dentro de su caja. La escena visual afirma `scrollWidth > clientWidth`
  debajo de 640 px y que la página no supera el ancho;
- el editor por opción ocupa el ancho;
- «Listo» devuelve el foco a «Editar {nombre}».

> **Ojo con la evidencia:** las capturas E1 a 390 y 320 (`e1-ca1-*`) son anteriores a `3566ffb` y todavía muestran una
> columna «Código» en la tabla compacta. En `982ade8`, `catalogTableColumns` la saca y rotula «Texto y código». No hay
> captura nueva de E1 a 390/320 con ese cambio (no verificado con imagen).

> **No copiar — review E2, punto 36** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)):
> - «Agregar opción» crea la fila y en el mismo render `catalogItemProblems` marca «Texto» y «Código» con «Completá
>   este campo.»; arriba aparece «Revisá las opciones marcadas antes de guardar.»;
> - en un catálogo tipado pasa lo mismo con «Valor»: «Ingresá un valor.» sale enseguida, porque `invalidValues` se
>   calcula sobre toda opción cambiada (verificado en `CatalogEditorPage.tsx`);
> - lo decidido: un campo vacío se marca recién al intentar guardar, o al salir de un campo ya tocado. El código
>   repetido y el valor inválido siguen apareciendo al escribir.
>
> Estado en `982ade8`: pendiente.

> **No copiar — review E2, punto 17:**
> - con campos extra de tipo catálogo (E2), la celda usa `Autocomplete`. Su lista es un `ul` con
>   `position: absolute` dentro de la tabla ancha, sin portal (`src/shared/ui/Autocomplete.tsx`);
> - en las últimas filas, o con pocas filas, la lista se corta o obliga a desplazar la caja.
>
> Estado en `982ade8`: pendiente. Es plausible: sale del CSS, sin prueba de navegador.

> **No copiar — review E2, punto 31:**
> - el formato de cada tipo no sale de una sola pieza;
> - en esta misma tabla, la columna «Valor» de un catálogo de tipo Fecha usa `formatCatalogValue` →
>   `formatDateOnly(..., "medium")` («15 mar 2027»);
> - un campo extra de tipo fecha usa `formatDateOnly(..., "numeric")` («15/03/2027»);
> - la revisión de importación usa el formato medio para los dos.
>
> Estado en `982ade8`: pendiente.

> **No copiar — regla de idioma del 2026-10-23** (review E2, punto 35):
> - el texto de la opción es obligatorio en español (`required: culture === "es"` en `catalogColumns.ts`);
> - el guard `item` exige `text.es`;
> - la ayuda en inglés dice que se usa el español;
> - «Generar los códigos que faltan» parte del texto en español.
>
> Con la regla nueva, lo obligatorio es el idioma original. Estado en `982ade8`: pendiente.

#### 8.3.4 Valor tipado — CA3 (`src/features/business-forms/catalogValues.ts`)

- **Tipos:** `Text`, `Integer`, `Decimal` y `Date` (`catalogValueTypes`). En Texto no hay columna de valor y el valor
  viaja `null`.
- **Interpretación de lo escrito** (`parseCatalogValue`, con la cultura del borrador, que es la de la interfaz al
  abrir; no la de «Textos en»):
  - **Entero:** hasta 18 dígitos; acepta separador de miles («1.500» en español, «1,500» en inglés);
  - **Decimal:** coeficiente de 96 bits y escala de hasta 28. En español, «1.500,50» da `1500.5` y «1500.50» también da
    `1500.5`; en inglés, «1,500.50» da `1500.5`. Nunca pasa por `Number`;
  - **Fecha:** ISO desde el `input type="date"`, o «dd/mm/aaaa» en español y «mm/dd/aaaa» en inglés si se escribe a
    mano;
  - más de 64 caracteres o un formato que no se reconoce da «Ingresá un valor válido para este tipo.»; vacío da
    «Ingresá un valor.»
- **Al cargar:** `catalogItemValue` formatea número y decimal a la cultura (`formatExactDecimal`, es-AR para español) y
  deja la fecha en ISO para el `input type="date"`.
- **Al guardar:** `canonicalItemValue` vuelve a interpretar y manda el string canónico. Un valor inválido bloquea
  Guardar y conserva lo escrito.
- **El tipo es inmutable:** sólo el alta manda `valueType` y el PUT nunca. Filters es siempre Texto (lo valida el
  guard).
- **Celular:** el campo de valor ocupa el ancho del editor por opción. La escena `catalogValues.spec.ts` comprueba que
  queda dentro de la pantalla a 390 y 320 con el error visible
  ([evidencia 320](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/e1-review-ca3-invalid-es-320.jpg)).

**Observación del relevamiento**, no está en los reviews:

- el editor interpreta «1.500» en español como 1500 sin preguntar;
- la importación del servidor rechaza esa misma forma como ambigua (`Forms.CatalogImport.ValueAmbiguous`, ver la
  [regla de carga](../../features/formularios.md#carga-de-catálogos-desde-una-planilla--e1-a-2026-10-22));
- al replicar, conviene una sola regla de ambigüedad para el editor y para la importación.

#### 8.3.5 «Así lo ve el empleado» — CA1-A con el ajuste de claridad (`src/features/business-forms/components/CatalogPreview.tsx`)

Es una sección (`<section aria-label>`), no una `Surface`.

- **Título:** «Así lo ve el empleado».
- **Selector «Una opción / Varias»** («Vista previa de la elección»), y debajo «Lo decide cada pregunta del formulario.
  Este selector cambia solo la vista previa.»
- **Teléfono (`PhonePreview`)** con el nombre del catálogo en el idioma de contenido:
  - **Una opción:** el botón cerrado «Elegí una opción». Al tocarlo, aparecen los radios y «Volver»; elegir uno cierra
    la lista y el botón muestra lo elegido;
  - **Varias:** «Elegí hasta 3» y casillas, con la cuarta deshabilitada. El 3 es fijo e ilustrativo;
  - al pie, «Texto del botón final» deshabilitado y «Esta vista no envía ni guarda respuestas.»
- **Debajo:** «Vista orientativa de esta página. WhatsApp admite hasta 200 opciones activas al enviar.» En Varias:
  «…hasta 20 opciones activas en una pregunta de varias.» (arreglo `de1faf8`).
- **Qué ilustra:**
  - sólo las opciones activas de la página visible, más las nuevas, que tengan texto en el idioma de contenido (si
    falta, el español);
  - el título y la descripción siguen la presentación de E2 (`catalogOptionPresentation`);
  - los textos fijos del teléfono usan el idioma de contenido (`useTranslation("forms", { lng: culture })`), no el de
    la interfaz.
- No lee nada del servidor ni cambia el catálogo.
- **Celular:** apilada debajo de «Presentación».
  Evidencia: [390](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/e1-review-ca1-preview-es-390.jpg) y
  [320](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/e1-review-ca1-preview-es-320.jpg).

> **No copiar — regla de idioma del 2026-10-23:** el «idioma de contenido» de esta vista sale del selector «Textos en»,
> que desaparece con el punto 35. Con la regla nueva, se muestra en el idioma del menú y, si falta, en el original.
> Estado en `982ade8`: pendiente.

#### 8.3.6 Diálogo «Importar opciones a «{nombre}»» — CA2 (`src/features/business-forms/components/CatalogImportDialog.tsx`)

**Apertura:**

- se abre desde «Pegar desde Excel» (fuente «pegar») o «Importar» (fuente «archivo»);
- requiere catálogo guardado, sin cambios, `formcatalogs.read` y `formcatalogs.manage`;
- recibe el **catálogo confirmado más reciente** (`editor.confirmed ?? initial`): el leído al entrar, el guardado, el
  importado o el adoptado.

> **Review E2, punto 12** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)): antes recibía el detalle
> de la entrada, y después de guardar campos nuevos la revisión mostraba GUID. Estado en `982ade8`: corregido en
> `9efd8e2`.

**Disposición:**

- **Diálogo `mobileFullscreen`:** debajo de 640 px ocupa toda la pantalla, con el cuerpo desplazable y el pie fijo. La
  escena visual mide `top = 0`, `bottom = 900` y que el pie no se mueve al desplazar.
- **Encabezado:**
  - título «Importar opciones a «{nombre}»». Con `pr-6` deja lugar a la ×: un título largo en inglés a 320 px se
    superponía (`e78a946`);
  - descripción «Archivo o filas pegadas, revisar y aplicar. No se guarda nada hasta aplicar.»
- **Pasos:** «1 · Archivo» (o «1 · Filas» al pegar), «2 · Revisar» y «3 · Aplicar», con `aria-current="step"` en el
  actual.

**Paso 1** (pestañas «Subir un archivo» y «Pegar desde Excel»; cambiar de pestaña reinicia el estado):

- **Archivo:**
  - enlace «Descargar la plantilla»;
  - campo «Archivo» (`accept=".xlsx,.csv"`), con la ayuda «XLSX o CSV, hasta 10 MB y 10.000 filas. No se ejecutan
    fórmulas ni macros.»;
  - el front valida la extensión y los 10 MB: «Elegí un archivo XLSX o CSV de hasta 10 MB.»
- **Pegar:**
  - área «Pegá las filas con encabezados», con la ayuda «Copiá Código, Texto y las demás columnas con sus encabezados
    desde Excel. Pegar agrega o actualiza por código.»;
  - `parseCatalogPaste` (`src/features/business-forms/catalogPaste.ts`) convierte lo pegado, separado por tabulaciones,
    en un CSV entre comillas;
  - conserva tabulaciones, saltos y comillas escapadas dentro de una celda entre comillas;
  - una comilla en medio de una celda sin comillas es literal, por ejemplo `Caño 1/2"` (`4c442af`);
  - comillas rotas dan «Revisá las comillas y los encabezados de las filas pegadas.»;
  - más de 64 columnas, más de 10.002 filas o más de 10 MB dan «Pegá hasta 10.000 filas y 64 columnas, sin superar
    10 MB de texto.»;
  - el archivo resultante se llama «Pegado.csv» o «Pasted.csv».
- **Errores de encabezado que llegan del servidor**, junto al campo y sin toast:
  - `MissingColumn`: «Falta la columna «{col}». Agregala y revisá de nuevo.»;
  - `DuplicateColumn`: «La columna «{col}» está repetida…»
- **Topes por hoja** (`TooManyColumns`, `TooManyRows` y `TooManyCells`): toast «Hoja «{hoja}»: {mensaje del
  servidor}».
- **Pie:** «Cancelar» y «Revisar».

**Paso 2 (revisión):**

- Una línea con el archivo, las filas leídas y la hoja: «{archivo} · N filas leídas · {hoja}».
- Insignias de conteo: «N altas», «N cambios», «N bajas», «N reactivadas», «N sin cambios», «N filas con errores» y
  «N avisos».
- Si hubo 409: aviso ámbar «Otra persona guardó el catálogo mientras lo revisabas. Las diferencias son de la revisión
  anterior: revisá de nuevo antes de aplicar.»
- Si la planilla ya se aplicó: aviso informativo «Esta planilla ya se aplicó el {fecha y hora en la zona del perfil}.»,
  que no bloquea.
- **Pestañas** (abre en «Errores y avisos» si hay incidencias; si no, en «Altas»):
  - **«Errores y avisos»:** tabla Fila · Tipo («Error» rojo o «Aviso» ámbar) · Columna · Qué pasa. Los errores van
    primero (lo ordena el servidor). La columna desconocida muestra el encabezado literal (`040e1f4`); las conocidas,
    su nombre traducido (`catalogs.import.columns.*`) o el nombre del campo extra. Si hay otra fila relacionada, debajo
    del motivo va «Fila N»;
  - **«Altas»:** Fila · Código · [campos extra] · Texto · [Valor]. El valor y los campos se muestran formateados en la
    cultura de la interfaz (`eec996b`, `3027563`);
  - **«Cambios»:** Fila · Código · una línea por columna, «{Columna}: antes → después» (por ejemplo «Valor: 1.000 →
    1.500,5»);
  - **«Bajas»:** Código · Texto.
- Si las listas se cortaron: «Se muestran hasta 100 filas por lista. El archivo conserva el detalle completo.»
- Si hay filas con error: aviso ámbar «Las N filas con errores no se importan. Corregilas o aplicá sin ellas.»
- El modo, en palabras:
  - Replace: «Lo que no viene en la planilla se da de baja, no se borra. Vuelve a ofrecerse si aparece en otra
    importación.»;
  - Append: «Pegar agrega opciones y actualiza las que tienen el mismo código, sin dar de baja las demás.»
- **Confirmaciones** que pide el servidor, como casillas:
  - «Sí, dar de baja N opciones», con la cantidad exacta;
  - «Sí, aplicar esta planilla sin filas válidas»;
  - «Sí, aplicar sin las filas con errores», si superan el 10 %.
- **Pie:** «Cancelar» y «Aplicar» o «Aplicar sin las N filas con errores», deshabilitado hasta marcar las
  confirmaciones. Con un 409, el botón pasa a ser «Revisar de nuevo».

**Paso 3 (aplicar):**

- manda el mismo archivo con `sheetSha256` y las confirmaciones;
- mientras aplica, no se puede cerrar: «Cancelar» deshabilitado, sin × y Escape ignorado;
- al terminar: toast «Opciones importadas.», el editor adopta el catálogo devuelto (si estaba limpio) y el diálogo se
  cierra.

**Estados:**

| Estado | Comportamiento |
| --- | --- |
| Carga | Botones deshabilitados mientras corre la vista previa o la aplicación; no hay spinner propio |
| Vacío | Una planilla sin filas válidas pide la confirmación «sin filas válidas» |
| 403 | Dentro del diálogo, «Ya no tenés permiso para importar opciones.»; se descartan archivo, texto y vista previa, sin reintentar |
| 409 | Ver arriba. «Revisar de nuevo» relee el catálogo (`fetchCatalog`), avisa al editor (`onObserved`) y repite la vista previa con la revisión nueva y **el mismo archivo** |
| 429 | Sin manejo propio: cae en el toast genérico, con el `detail` del servidor o «Algo salió mal», sin cuenta regresiva. El límite es una operación concurrente por actor (`forms-export`, ver la regla del back) |
| Red o 5xx al aplicar | Toast «No pudimos confirmar el guardado. Volvé a guardar. Referencia: …». El diálogo conserva archivo y vista previa para otro «Aplicar» explícito |
| Perder el alcance | Cambiar de actor, idioma, permisos o catálogo reinicia el estado; cerrar aborta la lectura en curso y descarta respuestas tardías |

**Celular (390 y 320):**

- pantalla completa;
- las insignias pasan a varias líneas;
- las pestañas y las tablas se desplazan a lo ancho dentro del cuerpo;
- evidencia de [errores a 320](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/e1-review-ca2-error-es-320.jpg),
  [altas a 390](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/e1-review-ca2-added-es-390.jpg) y
  [aviso con el encabezado real a 320](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/e1-review-ca2-warning-en-320.jpg).

> **No copiar — review E2, punto 31:** la revisión formatea las fechas con el formato medio («15 mar 2027») y la tabla
> de opciones muestra los campos de fecha como «15/03/2027». Tiene que haber un solo formateador por tipo. Estado en
> `982ade8`: pendiente.

#### 8.3.7 Exportar y descargar la plantilla (`src/features/business-forms/hooks/useCatalogExport.ts`)

- **Exportar:** `GET /api/forms/catalogs/{id}/export?template=false`. Baja todas las opciones, activas y dadas de baja,
  con el valor canónico como texto y el encabezado «Valor canónico (tipo)», según la regla del back.
- **Plantilla:** `?template=true`, con los encabezados para carga humana.
- **Permisos y alcance:**
  - sólo `formcatalogs.read`;
  - una descarga a la vez por dueño (actor, idioma, catálogo y permisos);
  - el nombre del archivo lo da el servidor (si falta, «Catalog.xlsx»);
  - los bytes no quedan en caché: `savePrivateBlob`;
  - cambiar de dueño o perder la lectura cancela la descarga.
- **Errores:** toast con `readErrorMessage`. Un 403 deja `exported.error` en 403 y la página pasa a `ForbiddenPage`.

#### 8.3.8 Revisión ante un 409 de guardado

Viene de K3 y E1 no la cambió. Lo cambió E2: sólo `Forms.Catalog.RevisionConflict` entra a revisión.

- La página muestra la superficie «Revisión del catálogo» con «El catálogo puede haber cambiado. Revisá la versión
  actual antes de guardar de nuevo.» y «Revisar».
- El diálogo compara «Tu borrador» y «La revisión actual», pero **sólo con nombre y descripción es/en**.
- Botones: «Cerrar», «Releer» y «Usar la revisión actual», que descarta los cambios, incluidas las opciones pendientes.

> **Review E2, punto 2:** antes cualquier 409 obligaba a descartar el borrador. Estado en `982ade8`: corregido en
> `c2572b8`, con `4a24702`, `dfb1d26` y `e3e81a7` para los usos y la presentación (detalle en la sección 9).

#### 8.3.9 Elegir varias en el editor de formularios — CA4

Archivos:

- `src/features/business-forms/components/QuestionEditor.tsx` y `SelectionRangeFields.tsx`;
- `src/features/business-forms/selectionQuestions.ts`;
- `src/features/business-forms/components/FormPreview.tsx` y `DraftReview.tsx`.

**Dónde aparece:**

- en una pregunta «Elegir de una lista», arriba de las opciones propias;
- en «Elegir de un catálogo», dentro de `CatalogQuestionSettings`, debajo del selector de catálogo.

**Controles:**

- Casilla «Permitir elegir varias».
- Activada, dos campos numéricos, «Mínimo» y «Máximo» (`type="number"`, paso 1, de 0 a 20). Debajo: «Con mínimo 1, la
  pregunta es obligatoria. WhatsApp muestra hasta 20 opciones en una pregunta de varias.»
- «Obligatoria» y el mínimo van juntos:
  - un mínimo mayor que 0 hace obligatoria la pregunta;
  - marcar «Obligatoria» sube el mínimo a 1 como mínimo;
  - desmarcarla lo deja en 0.
- Con opciones propias, «Agregar opción» se deshabilita a las 20 en varias (50 en una).
- **Errores** (`selectionError`, `optionError`):
  - «Elegí un mínimo entre 0 y el máximo, y un máximo entre 1 y 20.»;
  - «El mínimo no puede superar la cantidad de opciones de esta pregunta.»;
  - «Esta pregunta admite hasta 20 opciones propias.»
- Con un rango inválido, «Guardar borrador» queda deshabilitado (`hasInvalidSelectionCounts` en `FormEditorPage`).
- Un error del servidor en `selection.minSelected` o `selection.maxSelected` enfoca el campo que corresponde.

**Cambiar entre una y varias:**

- **Pregunta sin guardar:** cambia en el lugar y conserva identidades.
- **Pregunta guardada** (`fieldId`), confirmación:
  - título «¿Crear una pregunta nueva?»;
  - texto «Cambiar entre elegir una y varias crea una pregunta nueva. Las respuestas anteriores conservan la pregunta
    original.»;
  - botón «Crear la pregunta nueva».
- Al confirmar: `LocalId` nuevo, `FieldId` null, `replacesFieldId` y, en opciones propias, `ChoiceId` nuevos. Se
  conservan textos, orden y traducciones. El rango inicial es mínimo 1 (si era obligatoria) o 0, y máximo 3.
- Cancelar devuelve el foco a la casilla; confirmar enfoca «Pregunta».
- Editar sólo el rango conserva la pregunta.

**Resumen y revisión:**

- La fila cerrada de la pregunta dice, por ejemplo, «Elegir de una lista · varias, hasta 3 · Obligatoria». El tipo
  sigue siendo «Elegir de una lista» o «Elegir de un catálogo»: no hay un tipo «Elegir varias» en el selector, aunque
  la clave `types.multiChoice` existe.
- `DraftReview`, la comparación ante un conflicto, muestra «varias, hasta N · Mínimo: M». Los nombres de las opciones
  salen en el idioma elegido (`0473958`).

**Vista previa del teléfono (`FormPreview`):**

- **Opciones propias:** casillas y «Elegí hasta N» (o la ayuda de la pregunta). Las demás se deshabilitan al llegar al
  máximo.
- **Catálogo:**
  - en E1 sólo decía «Las opciones se preparan según quien lo recibe, al mandar el formulario.»;
  - desde E2, si la pregunta no tiene filtros por persona, muestra hasta 20 opciones activas del catálogo, ordenadas por
    texto, que lee `CatalogQuestionSettings`;
  - con una persona elegida en «Como si lo recibiera», muestra sus opciones (sección 10).
- **Celular:** los dos campos del rango van en dos columnas (`grid-cols-2`, ancho máximo `max-w-sm`). La escena mide que
  cada control tiene más de 40 px y no se sale de la pantalla a 320
  ([evidencia 320](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/e1-ca4-es-320.jpg)).
- **Publicar:**
  - un catálogo Manual con más de 20 opciones activas en una pregunta de varias frena la comprobación con
    `Forms.Publication.CatalogTooLargeForMultiple`;
  - el texto lo da el servidor y el diálogo lo muestra con «Ir a la pregunta»;
  - la captura real es `forms-http/multiple-choice/catalog-too-large.json`.

> **No copiar — regla de interfaz del 2026-10-23:**
> - los errores de rango se calculan en cada render;
> - vaciar «Mínimo» o «Máximo» muestra «Elegí un mínimo entre 0 y el máximo…» al instante;
> - con la regla, un campo vacío se marca al intentar guardar. Un valor fuera de rango puede seguir marcándose al
>   escribir, como decidió el punto 36.
>
> El review no lo nombra; queda verificado en `QuestionEditor.tsx`.

> **No copiar — review E2, punto 16:** el aviso de títulos de más de 30 caracteres en la pregunta mira sólo las 20
> opciones que se leen para la vista previa. Detalle en la sección 10. Estado en `982ade8`: pendiente.

> **No copiar — review E2, punto 29:**
> - la clave de aviso al publicar `publication.recipientMaximum` no tiene `_one` ni `_other`, así que puede decir «Como
>   mucho 1 opciones»;
> - los avisos de publicar admiten cualquier `code`.
>
> Estado en `982ade8`: pendiente (sección 10).

#### 8.3.10 Respuestas con varias — CA5

**Planilla** (`src/features/business-forms/pages/FormResponsesPage.tsx`,
`src/features/business-forms/components/ResponseValue.tsx`):

- la celda une las etiquetas con «, » en el orden elegido («Frío, Ruido»). Pasa a la línea de abajo si no entra
  (`max-w-64 whitespace-normal`; en las de una opción, `truncate`);
- la columna no se ordena (`sortable: … column.type !== "multiChoice"`): el encabezado no tiene botón;
- Unanswered y NotAsked se muestran como en una pregunta simple: «Sin respuesta» o «—».

**Filtro rápido** (`src/features/business-forms/components/ResponseChoiceMenu.tsx`):

- toma la primera pregunta de lista o de varias;
- en varias, usa el operador `anyOf` y el botón dice «Fallas: alguna de 1»;
- cada opción trae el conteo del servidor y las que tienen 0 quedan deshabilitadas;
- «Ver más opciones» abre «Más filtros».

**Más filtros** (`src/features/business-forms/components/ResponseFiltersPanel.tsx`):

- usa la misma faceta, con `anyOf` para varias e `in` para una;
- una respuesta con dos opciones cuenta en las dos (lo cuenta el servidor con EXISTS);
- el chip dice «Fallas: alguna de Frío».

**Detalle D2 y pantalla completa DC1:**

- una opción por renglón, con su detalle y el nombre del catálogo debajo;
- desde E2, también los campos capturados (`CapturedFields`).

**Excel y PDF:** los arma el servidor. En el Excel las opciones van en una celda («Frío; Ruido») y, si el catálogo es
tipado, una columna de valor con el texto canónico unido. El PDF muestra cada etiqueta con su valor localizado. El
front sólo pide el archivo.

**Reglas y sugerencias** (`src/features/business-forms/components/FormConditionsEditor.tsx`, compartido desde `e0ebe9a`):

- para una pregunta de varias con opciones, el único operador de valor es «Eligió alguna de»;
- los valores se eligen con un `MultiSelect` («Elegí los valores»);
- las sugerencias no permiten mapear una pregunta de varias, ni como origen ni como destino (`mappingTargets` en
  `src/features/business-forms/suggestions.ts`).

**Celular:**

- la planilla conserva el desplazamiento dentro de su caja;
- la celda larga se parte en varias líneas sin recortarse (lo mide la escena);
- con lectura, el detalle se abre en pantalla completa;
- evidencia de la [planilla filtrada a 320](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/e1-review-ca5-sheet-es-320.jpg)
  y del [detalle a 320](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/e1-review-ca5-detail-es-320.jpg).

#### 8.3.11 Chat de WhatsApp — CA4 y CA5

Archivos: `src/features/whatsapp/components/FormCatalogPreparation.tsx`, `FormReplyValues.tsx` y
`src/features/whatsapp/lib/formReplyValue.ts`.

**Al mandar (diálogo C2):**

- si a la persona le tocarían más de 20 opciones en una pregunta de varias, la preparación trae
  `Forms.Record.CatalogTooLargeForMultiple`;
- aparece el aviso rojo «{pregunta}: a esta persona le tocarían más de 20 opciones. Filtrá el catálogo o usá una
  pregunta de una sola opción.» y no se puede mandar (`aabec44`, punto 4 del review E1).

**Tarjeta de respuesta (R1–R3):**

- muestra «Frío, Ruido»;
- el nombre del catálogo aparece **una sola vez**;
- cada detalle va asociado a su opción: «Frío: {detalle}» (`febf258`, punto 13 del review E1);
- una lista de opciones nunca abre el visor de archivos ni ofrece descargas;
- sin `formrecords.read` no hay valores.

**Celular:** la escena de chat a 390 y 320 comprueba que no hay desplazamiento horizontal y que no aparece ningún
«Descargar»
([evidencia a 320](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-mejoras/e1-review-ca5-chat-es-320.jpg)).

### 8.4 Hooks y estado

**Claves de consulta** (`src/features/business-forms/hooks/useCatalogQueries.ts`):

- **raíz del dueño:** `catalogScopeKey` = `["form-catalogs", actorId, permisos ordenados y unidos, cultura]`;
- **detalle:** `[...raíz, "detail", id]`, con `staleTime: 0`, `gcTime: 0` y `meta.errorOwner: "local"`;
- **opciones:** `[...raíz, "items", id, revision, query]`:
  - la revisión es parte de la clave: después de guardar o importar, las páginas se piden de nuevo;
  - `loadItems` ignora una página de otra revisión (`revision !== draft.revision`);
  - `gcTime: 0`;
- **opciones por id** (`items-by-id`, de E2 en `7080141`): usada por «Ver los N en la tabla» y por las etiquetas de un
  filtro fijo;
- `useUnobservedQueryCleanup` retira las consultas sin observadores.

**Borrador** (`src/features/business-forms/hooks/useCatalogDraftEditor.ts`, `src/features/business-forms/catalogDraft.ts`,
`src/features/business-forms/catalogItemEdits.ts`):

- **Dueño:** `[actorId, culture, catalogId, kind, valueType]`, más una `epoch` que separa callbacks de un baseline
  anterior. Cambiar de dueño reinicia el estado y aborta lo que esté en curso.
- **Opciones:**
  - `originals` (lo cargado, por `item:<id>`), `changes` (lo tocado) y opciones nuevas con `localId`
    (`crypto.randomUUID()`), `itemId` null y `position` null;
  - lo editado en una página se conserva al cargar otra;
  - el PUT manda sólo las identidades tocadas, sin posiciones;
  - las flechas encolan `{ itemId, direction }`;
  - `sortAlphabetically` es una orden aparte, incompatible con movimientos.
- **Valores:** se guardan como entrada local y se convierten una sola vez al armar el cuerpo. Un valor inválido corta
  `save()` antes de escribir.
- **Escritura:**
  - una sola a la vez (`operation`, un `AbortController`);
  - sin MutationCache, sin reintento automático y con el cuerpo congelado al pulsar;
  - **red o 5xx:** el borrador se conserva y otro Guardar va con la misma revisión;
  - **409:** sólo `RevisionConflict` pasa a revisión (8.3.8). `SourceInUse` y `FieldInUse` son errores del editor (E2).
- **Invalidación:** guardar, descartar una propuesta y adoptar un catálogo observado invalidan
  `["form-catalogs", actorId]` completo.
- **`observeCatalog`:**
  - después de importar o de «Revisar de nuevo», adopta el catálogo nuevo **sólo si el borrador está limpio**: sin
    cambios, movimientos, orden pendiente ni campos o presentación distintos del baseline;
  - si no, lo ignora.
- **`dirty`** compara nombre, descripción, estado, campos, presentación y filtros contra el baseline, más cambios de
  opciones, movimientos y orden.

**Importación** (estado propio del diálogo, no del borrador):

- **Dueño:** `[actorId, culture, permisos, catalogId]`.
- El `File`, el texto pegado y la vista previa viven sólo en el `useState` del diálogo: nunca en QueryCache,
  MutationCache, URL ni storage.
- Una operación a la vez, con su `AbortController`:
  - `accepts()` descarta respuestas tardías si cambió el dueño, se cerró el diálogo o se abortó;
  - cerrar aborta la vista previa; aplicar no se puede cortar desde la interfaz.
- La revisión que se manda en Apply es la capturada en la vista previa; «Revisar de nuevo» la renueva con
  `fetchCatalog`.

**Exportación:** `useCatalogExport`, con una descarga a la vez, cancelación por dueño y sin caché de bytes.

**Elegir varias** (`src/features/business-forms/selectionQuestions.ts`, `src/features/business-forms/draftSession.ts`):

- el rango puede quedar `null` mientras se edita, pero el cuerpo canónico exige enteros válidos;
- `draftSession` compara selección y opciones al confirmar el cuerpo congelado: el recibo debe traer la misma
  selección.

### 8.5 Clientes HTTP y guards zod

**Clientes:**

- **`src/features/business-forms/api/catalogs.ts`:**
  - `createManualCatalog` (POST) suma `valueType` y `items[].value`;
  - `updateManualCatalog` (PUT) suma `items[].value` y `sortAlphabetically`, y nunca manda `valueType`;
  - `fetchCatalogItems` admite `ids` repetidos (`?ids=a&ids=b`), agregado en E2.
- **`src/features/business-forms/api/catalogImports.ts`:**
  - **`previewCatalogImport`:** POST multipart con `file`, `mode` (`Replace` o `Append`) y `expectedRevision`.
    `cache: "no-store"`;
  - **`applyCatalogImport`:** el mismo multipart más `sheetSha256`, `confirmDeactivations` (número),
    `confirmEmpty` y `confirmErrors`. Se manda `confirmErrors = errorRows > 0`: aplicar sin las filas con error se
    acepta con el botón explícito, y la casilla sólo se exige si el servidor pide `manyErrors`;
  - **`exportCatalog`:** usa `api.file` y exige que el blob sea XLSX y no esté vacío.
- **`src/shared/api/httpClient.ts`** (`bf4440b`): si el cuerpo es `FormData`, no fija `content-type` (el navegador
  pone el boundary) y no lo serializa a JSON. El token y la sesión son los mismos de siempre. El detalle técnico va en
  la sección 11.

**Guards:**

- **Guards de catálogo** (`src/features/business-forms/api/catalogResponse.ts`, `strictObject`):
  - detalle y fila de listado: `valueType` requerido y cerrado (`Text`, `Integer`, `Decimal`, `Date`);
  - Filters debe ser `Text`;
  - cada opción trae `value` requerido y nullable (hasta 64), `position` de 1 a 10.000 y `text.es` no vacío;
  - el recibo de guardado exige un `itemIdentityMap` exacto para las opciones nuevas y una revisión mayor que la
    esperada.
- **Guard de importación** (`previewSchema`, `strictObject`, en `catalogImports.ts`):
  - **conteos** enteros no negativos;
  - **`issues`:** `row`, `column`, `severity` (`Error`/`Warning`), `code`, `message`, `relatedRow` nullable y `length`
    nullable;
  - **`added`:** hasta 100, con `value` nullable requerido y, de E2, `fields` y `fieldTexts` por GUID;
  - **`changed`:** hasta 100, con `before`/`after` y textos de catálogo;
  - **`deactivated`:** hasta 100;
  - **`confirmations`** y `alreadyAppliedAtUtc`;
  - `readCatalogImportPreview` además exige que `catalogId`, `revision` y `mode` coincidan con lo pedido;
  - Apply acepta una revisión igual a la capturada (sin diferencias no sube revisión), mientras que el PUT exige una
    mayor;
  - los errores de zod no se conservan, porque pueden tener contenido privado.
- **Guards de formulario** (`src/features/business-forms/api/response.ts`, `canonicalFormFieldSchema`):
  - `multiChoice` exige `selection` con mínimo de 0 a máximo, máximo de 1 a 20 y `required` si y sólo si mínimo > 0;
  - con opciones propias, de 1 a 20 opciones, sin que el mínimo supere su cantidad; una simple admite hasta 50;
  - `selection` está prohibida fuera de `multiChoice`.
- **Guards de respuestas** (`src/features/business-forms/api/responses.ts`):
  - **celda:** una lista de hasta 20 opciones con `choiceId` distintos;
  - **estado de la celda:** Answered no vacía, Unanswered null o `[]` y NotAsked null; no acepta archivos;
  - **opción:** puede traer `value` y `valueType` juntos, canónicos (`isCanonicalCatalogValue`), nunca uno solo;
  - **columna:** puede traer `valueType` sólo si es de una o de varias.
- **Guard del chat** (`src/features/whatsapp/api/formReplies.ts`):
  - `multiChoice` es un array de hasta 20 sin `choiceId` repetidos;
  - cada opción admite `catalogId` y `catalogName` juntos, `detail` sólo con catálogo y el par `value`/`valueType`
    completo.
- **Reglas y sugerencias** (`src/features/business-forms/api/rules.ts` y `src/features/business-forms/api/suggestions.ts`):
  el enum de operadores suma `anyOf`.

### 8.6 Cohortes y fixtures exportadas

El mecanismo general (`src/test/httpCohort.ts`, procedencia y SHA256) está en la sección 11. Lo propio de E1:

| Cohorte (`httpCohort`) | Carpeta | Variable de entrada | Productor del back | Cuerpos | Consumidor |
| --- | --- | --- | --- | --- | --- |
| `catalogImports` (artifactSet `catalog-imports`) | `src/test/fixtures/catalogs-http/imports/{es,en}` | `FORM_CATALOG_IMPORT_HTTP_INPUT` | `FormCatalogImportHttpTests`, `FormCatalogImportFieldsHttpTests` (E2), `FormCatalogEndpointsTests` y `FormCatalogImportContractCapture`; también declara como fuentes `FormCatalogImportModels.cs`, `FormCatalogImportTemplate.cs` y `Catalogs.resx`/`.en.resx` | 30: 15 por idioma. E1 dejó 24 (`already-applied`, `append-preview`, `applied`, `confirmation-required`, `conflict`, `empty-preview`, `replace-preview`, `round-trip-preview`, y `export.xlsx` y `template.xlsx` con su `.http.json`); E2 sumó `extra-source-preview`, `extra-source-applied` y `extra-source-unchanged` | `src/features/business-forms/api/catalogImportsHttpContract.test.ts` |
| `manualCatalogs` | `src/test/fixtures/catalogs-http/manual-current` | `FORMS_CATALOG_HTTP_CONTRACT_INPUT` | `FormCatalogEndpointsTests` (+`.Fields`) y `FormCatalogValueHttpTests` | 41 en total. Las de E1: `typed-{text,integer,decimal,date}-{created,items}`, `typed-invalid-value-{es,en}`, `typed-required-value` y `typed-type-change-rejected` | `catalogValueHttpContract.test.ts` y `catalogCurrentHttpContract.test.ts` |
| `forms` | `src/test/fixtures/forms-http` | `FORMS_HTTP_CONTRACT_INPUT` | `FormMultipleChoiceAuthoringHttpTests`, entre otros | 60 en total; E1: `multiple-choice/draft.json` y `multiple-choice/catalog-too-large.json` | `multipleChoiceHttpContract.test.ts` |
| `responses` | `src/test/fixtures/forms-responses-http` | `FORMS_RESPONSES_HTTP_CONTRACT_INPUT` | `FormMultipleChoiceReadHttpTests`, entre otros | 55 en total; E1: `multi-sheet`, `multi-detail`, `multi-counts` (`.json`) y `multi-export` (`.xlsx`), en es y en | `multipleChoiceHttpContract.test.ts` |
| `pdf` | `src/test/fixtures/forms-pdf-http` | `FORMS_PDF_HTTP_CONTRACT_INPUT` | `FormMultipleChoiceReadHttpTests`, entre otros | 32 en total; E1: `multi-preview-{es,en}.pdf` con su `.http.json` | `multipleChoiceHttpContract.test.ts` |
| `whatsappForms` | `src/test/fixtures/whatsapp-forms-http/{es,en}` | `WHATSAPP_FORMS_HTTP_CONTRACT_INPUT` | `WhatsAppMultipleChoiceCompositionTests`, entre otros | 76 en total; E1: `reply-multiple-choice.json` por idioma | `src/features/whatsapp/api/formRepliesHttp.test.ts` y `httpContract.test.ts` |

- Las seis provienen de la misma corrida conjunta, generada el `2026-10-23T07:21:40Z`, con transporte HTTP real y
  PostgreSQL/Redis en Testcontainers, según el `provenance.json`.
- Los 14 artefactos de elegir varias (2 de formularios, 8 de respuestas y 4 de PDF) se califican juntos en
  `multipleChoiceHttpContract.test.ts`.
- Las pruebas de importación por HTTP real aíslan el multipart de jsdom (`9e7a2a1`).
- Los dobles de comportamiento son sintéticos y lo dicen:
  - `src/test/catalogImportFixtures.ts` (vista previa);
  - `src/test/visual/catalogImportVisualFixtures.ts` (transporte simulado para las escenas);
  - `src/test/catalogImportVisualFixtures.test.ts`, que valida la semilla visual tipada con los guards reales.

### 8.7 i18n

Namespace `forms` (y `whatsapp` para el chat), en `src/locales/{es,en}`, con paridad que comprueba
`src/locales/parity.test.ts`. Grupos nuevos o ampliados en E1:

| Grupo | Para qué | Ejemplos (es / en) |
| --- | --- | --- |
| `catalogs.valueTypes.*`, `catalogs.valueType.*` | Tipo de valor, ayudas y errores | «Entero» / «Whole number»; «Número decimal, sin redondeos.»; «Ingresá un valor válido para este tipo.»; «Valor · {{type}}» |
| `catalogs.actions.*` | Barra de Opciones | «Pegar desde Excel», «Importar», «Exportar», «Más acciones de opciones», «Descargar la plantilla», «Ordenar A–Z», «Generar los códigos que faltan», «Guardá el catálogo antes.», «Guardá o descartá los cambios antes de importar.» |
| `catalogs.items.*` (nuevas) | Tabla editable | «Seleccionar opciones de esta página», «N seleccionadas de esta página» (`_one`/`_other`), «Texto y código», «Texto de la opción {{number}}», «Repetido: también está en la fila {{row}}.», «WhatsApp muestra hasta 30 caracteres.», «Revisá las opciones marcadas antes de guardar.», «Generar desde el texto» |
| `catalogs.import.*` | Diálogo de importación (unas 80 claves) | pasos, pestañas, conteos con plural, confirmaciones, «Pegado.csv» / «Pasted.csv», «Hoja «{{sheet}}»: {{message}}», «Fila {{row}}» |
| `catalogs.preview.*` | Vista previa | «Así lo ve el empleado», «Una opción», «Varias», «Elegí hasta {{count}}», «Vista orientativa de esta página…» |
| `catalogs.usageHeader` | Cabecera | «Usado en {{count}} formularios» (`_one`/`_other`) |
| `multiple.*` | Elegir varias en el editor | «Permitir elegir varias», «Mínimo», «Máximo», «varias, hasta {{maximum}}», «¿Crear una pregunta nueva?» |
| `responses.anyOf`, `responses.anyOfCount`, `rules.operators.anyOf`, `rules.chooseValues` | Filtros y condiciones | «alguna de», «alguna de {{count}}», «Eligió alguna de», «Elegí los valores» |
| `whatsapp:forms.catalogs.tooLargeForMultiple` | Bloqueo al mandar | «{{label}}: a esta persona le tocarían más de 20 opciones…» |

- **Arreglos de texto del review E1** (`40a1c74`): «Pegar desde Excel», «Descargar la plantilla», «Generar los códigos
  que faltan» y «solo» sin tilde.
- El descarte del editor de catálogos usa ahora «Seguir editando» (`stay`) y no la clave inexistente `keepEditing`
  (`b7b47c5`). Es la observación 1 del anexo anterior, corregida.
- Los textos de las incidencias de importación («Repetido: también está en otra fila.», etc.) **vienen del servidor**
  (`issue.message`, en `Catalogs.resx`); el front sólo traduce columnas y tipos.

### 8.8 Pruebas

**Unitarias** (Vitest y jsdom; `it` en inglés, como frase):

| Archivo | Qué cubre |
| --- | --- |
| `src/features/business-forms/catalogValues.test.ts` | Formato exacto por idioma («1.500» es, «1,500.50» en), canónicos y null sólo en Texto |
| `catalogEditing.test.ts` | Códigos repetidos sin mayúsculas, aun en una inactiva; texto largo sin recortar; generación de códigos; avisos agrupados con 10.000 opciones |
| `catalogColumns.test.ts` | Misma lista de columnas para tabla y editor; código plegado en la vista compacta; columnas de campos extra |
| `catalogDraft.test.ts` | Sólo identidades tocadas; sin posiciones; movimientos por identidad; A–Z como una orden; tipo inmutable y valores canónicos; no completa bilingüe desde el español |
| `catalogItemEdits.test.ts` | Ediciones entre páginas, baseline, copias defensivas |
| `catalogPaste.test.ts` | Comillas, tabulaciones y saltos; `Caño 1/2"` literal; topes |
| `selectionQuestions.test.ts` | Identidad nueva al cambiar la multiplicidad; mínimo imposible; dejar sin cambios lo que no cambia |
| `src/features/business-forms/api/catalogImports.test.ts` | Guard estricto, multipart, confirmaciones exactas, export por GET, Apply sin diferencias a la misma revisión |
| `api/catalogValueContract.test.ts`, `api/multipleChoice.test.ts` | `valueType` requerido y cerrado; Filters sólo Texto; rango canónico de varias |
| `components/CatalogImportDialog.test.tsx` | Append sin guardar, confirmaciones, 409 con el mismo archivo, abortar al cerrar, 403, columna faltante junto al campo, etiquetas de catálogo en vez de GUID |
| `components/CatalogManualItemsEditor.test.tsx` | Idioma elegido, baja y reactivación sin HTTP, foco al cerrar, selección sólo de la página |
| `components/CatalogOptionsActions.test.tsx` | Acciones de escritorio y menú móvil, motivo visible (punto 37), foco al diálogo |
| `components/CatalogPreview.test.tsx`, `SelectionRangeFields.test.tsx`, `FormConditionsEditor.test.tsx`, `CatalogOriginDialog.test.tsx` | Ilustración una/varias, rango null distinto de 0, `anyOf` sólo en varias, alta sin crear nada |
| `hooks/useCatalogExport.test.tsx` | Descarga sólo con lectura, cancelación por dueño, 403 |
| `pages/CatalogEditorPage.test.tsx` | Tipo sólo al crear, código repetido contra aviso de texto, importar exige catálogo limpio, A–Z, aplicar un pegado adopta el baseline, importar con los campos guardados en la sesión |
| `QuestionEditor.test.tsx`, `FormPreview.test.tsx`, `DraftReview.test.tsx`, `draftSession.test.ts`, `editorModel.test.ts`, `validationPaths.test.ts` | Elegir varias en el editor y el borrador |
| `responses.test.ts`, `api/responses.test.ts`, `ResponseValue.test.tsx`, `ResponseFiltersPanel.test.tsx`, `FormResponsesPage.test.tsx`, `rules.test.ts`, `suggestions.test.ts` | Lecturas de varias, `anyOf`, sin ordenar |
| `src/features/whatsapp/components/FormReplyMultipleChoices.test.tsx`, `FormReplyValues.test.tsx`, `FormCatalogPreparation.test.tsx`, `lib/formReplyValue.test.ts`, `api/formReplies.test.ts` | Tarjeta del chat y bloqueo de más de 20 |

**Visuales** (Playwright sobre `src/test/visual/index.html` con rutas y componentes productivos y transporte simulado):

- `src/test/visual/catalogLoading.spec.ts`:
  - «CA1 inline options and page selection»;
  - «CA2 import review» de archivo y de pegado;
  - «CA2 typed values and diagnostics».
- `catalogValues.spec.ts`: «Typed catalog CA3».
- `catalogMultipleChoice.spec.ts`:
  - «CA4 multiplicity», con opciones propias y con catálogo;
  - «CA5 ordered choices and any-of filter»;
  - «CA5 captured choices in chat».
- `catalogManual.spec.ts`: ajustada para el tipo de valor.

Todas corren en es y en, a 1440, 390 y 320 × 900, y miden geometría: sin desplazamiento horizontal de la página, tabla
contenida, pie fijo del diálogo y título sin superposición. También comprueban que no se escribe nada
(`writes`/`applies` vacíos).

**Son escenas de evidencia, sin referencias doradas:** no usan `toHaveScreenshot`. Lo dicen los propios archivos («no
goldens»). Las referencias comparadas del proyecto (127) no incluyen E1.

**Evidencia:**

- `docs/design/evidence/catalogos-mejoras/`, con los seis tableros elegidos (`ca1-a`…`ca6`), el recorrido E1 (42 JPEG)
  y el cierre del review E1 (30 JPEG);
- tablas completas en [mejoras de catálogos](../../../../ArquitecturaBaseFront/docs/design/catalogos-mejoras.md#recorrido-local-de-e1--2026-10-22)
  y en el [cierre del review E1](../../../../ArquitecturaBaseFront/docs/design/catalogos-mejoras.md#cierre-del-review-e1--2026-10-22).

### 8.9 Estado del review E1 en el front

El [review de E1](../../reviews/2026-10-22-code-review-catalogos-e1.md) quedó cerrado sobre `e78a946`, con sus 15
puntos corregidos. Lo del front, verificado en `982ade8`:

| Punto | Front | Estado en `982ade8` |
| --- | --- | --- |
| 4 | Texto propio para más de 20 en el chat | Corregido (`aabec44`); el de publicar lo da el servidor |
| 5 | Comilla dentro de una celda pegada | Corregido (`4c442af`) |
| 6 | Error y Aviso separados, encabezado real, fila relacionada | Corregido (`139dfff`, `040e1f4`) |
| 7 | Nombre de la hoja en los topes | Corregido (`a2a1411`, `ef05d92`) |
| 11 | Textos propios del pegado | Corregido (`76074cb`) |
| 12 | Tope 20 en la vista previa de Varias | Corregido (`de1faf8`) |
| 13 | Catálogo una vez y detalle por opción en el chat | Corregido (`febf258`) |
| 14 | Textos del diseño | Corregido (`40a1c74`) |
| 15 | Encabezados de Altas, Cambios y Bajas | Corregido (`3027563`) |
| «Después»: columna obligatoria faltante o repetida | Se muestra en el diálogo | Corregido (lo confirma el review E2 en «Lo que está bien») |

En el front no queda nada abierto de ese review. Del resto de «Después, sin bloquear», todo es del back:

- «Aplicar» sin diferencias, que el review E2 da por resuelto;
- las 10.000 opciones de las reglas;
- el multipart y la protección contra CSRF.

### 8.10 Para el multitenant

- **Catálogo por organización:**
  - la clave de consulta hoy es `["form-catalogs", actorId, permisos, cultura]`. Hay que sumar el tenant, o garantizar
    que el actor ya lo implica, para que un cambio de organización cancele y retire lo privado;
  - lo mismo vale para el dueño del borrador, del diálogo de importación y de la exportación.
- **Topes por plan:**
  - 10.000 opciones, 10 MB, 64 columnas, 10.002 filas físicas, 20 en varias y 200 en una están fijos en el front
    (`catalogPaste.ts`, la ayuda del archivo, `SelectionRangeFields limit={20}`, los textos);
  - si el multitenant los hace variar por plan, conviene que lleguen del servidor y que los textos los interpolen.
- **Importación concurrente:** el límite `forms-export` es por actor y por proceso, no global. En un SaaS con varias
  réplicas, decidir si el tope es por tenant y mostrar el 429 con su espera: hoy el front no la muestra.
- **Archivos privados:** el archivo nunca se persiste ni se guarda en el navegador. Mantenerlo así, y revisar que el
  multipart autentique por bearer y no por cookie (pendiente «después» del review E1).
- **Cultura de importación y de edición:**
  - se interpreta con la cultura de la interfaz, no con la del contenido;
  - en B2C, con usuarios en más idiomas, definir qué cultura manda;
  - unificar la ambigüedad de «1.500» entre el editor y el servidor (8.3.4).
- **Idioma del contenido:** aplicar la regla del 2026-10-23 desde el primer día:
  - sin «Textos en»;
  - el idioma del menú es el original y el único obligatorio;
  - sin caída a español fija en `generateMissingCatalogCodes`, en el texto obligatorio, en la ayuda en inglés ni en
    los guards `requiredText` y `text.es`.
- **Interfaz:** no copiar el motivo visible bajo los botones de «Opciones», «Cambios sin guardar», «Revisá las opciones
  marcadas…» ni la validación de vacíos al agregar una opción.
- **Formato:** un solo formateador por tipo (fecha, número y booleano) para la tabla, la revisión, la presentación y
  las respuestas.
- **Elegir varias y WhatsApp:** el tope de 20 sale de Meta (CheckboxGroup) y no del tenant. Mantener la identidad
  nueva al cambiar la multiplicidad y la lista ordenada en las respuestas.
- **B2C:** si los catálogos los ven clientes finales, «Usado en N formularios» y las vistas previas siguen exigiendo
  `formcatalogs.read`. No hay nada pensado para un catálogo compartido entre organizaciones.

### 8.11 Endpoints nuevos o cambiados

| Método y ruta | Uso | Cambio |
| --- | --- | --- |
| `POST /api/forms/catalogs` | Alta Manual | Suma `valueType` y `items[].value` |
| `PUT /api/forms/catalogs/{id}` | Guardar Manual | Suma `items[].value` y `sortAlphabetically` (sin `moves`) |
| `GET /api/forms/catalogs/{id}` y `GET /api/forms/catalogs?…` | Detalle y listado | Devuelven `valueType`; las opciones, `value` |
| `GET /api/forms/catalogs/{id}/items?…&ids=…` | Páginas de opciones | `value` en cada opción; `ids` repetido desde E2 |
| `POST /api/forms/catalogs/{id}/import-previews` | Vista previa (multipart) | Nuevo |
| `POST /api/forms/catalogs/{id}/imports` | Aplicar (multipart) | Nuevo |
| `GET /api/forms/catalogs/{id}/export?template=true\|false` | Exportar y plantilla (XLSX) | Nuevo |
| `POST /api/forms/definitions/{id}/records/search` y `…/records/filter-counts/search` | Planilla y facetas | Condiciones con `anyOf` |
| Reglas y sugerencias (`…/rules`, `…/suggestions`) | Condiciones | Operador `anyOf` |
| Definición, borrador, chequeo y publicación | Editor | `multiChoice` con `selection`; diagnóstico `Forms.Publication.CatalogTooLargeForMultiple` |
| Preparación del envío en el chat | C2 | Incidencia `Forms.Record.CatalogTooLargeForMultiple` |

### 8.12 Huecos

- **Sin captura nueva del editor de opciones a 390 y 320 después de `3566ffb`:** la vista compacta sin columna
  «Código» está verificada en el código, no en una imagen.
- **El diálogo «Nuevo catálogo» a 390 y 320** no tiene escena visual; su forma se deduce del componente.
- **El 429 de la importación y la exportación** no tiene prueba ni pantalla propia; el comportamiento sale de leer
  `errorFeedback` y el diálogo.
- **Los textos de Excel y PDF de varias** («Frío; Ruido», valor canónico) son del servidor; no se abrieron los XLSX ni
  los PDF exportados.
- **«Tests que faltan» del review E1:** se verificó la presencia de las regresiones de pegado con comillas, «1.500» y
  409 en la vista previa. Las demás no se buscaron una por una.
- **El destino del texto «No se puede cambiar después…»** del diálogo de alta con el punto 39: el review no lo dice.

---

## 9. Catálogos E2 (1): campos y presentación en el editor del catálogo

Relevado sobre el front en `982ade8` (2026-10-23 20:27), leyendo sólo el estado commiteado. Cubre el editor de un catálogo
escrito a mano (Manual) con lo que sumó E2: la sección «Campos», las columnas extra de la tabla de opciones, la sección
«En WhatsApp» del catálogo con su vista previa, importar y pegar con campos extra, el diálogo «Cambiar el tipo» y los
arreglos posteriores al [review de E2](../../reviews/2026-10-23-code-review-catalogos-e2.md). La base E1 (tabla editable,
pegar e importar, tipo de valor, elegir varias) está en la sección 8; los datos de las personas, los filtros y la
presentación propia en la pregunta, la vista previa por persona, la comprobación de publicar y las subcolumnas de las
respuestas, en la sección 10.

Fuentes leídas: [tableros elegidos de campos extra](../../../../ArquitecturaBaseFront/docs/design/catalogos-campos-extra.md)
(incluido su [ajuste del review de E2](../../../../ArquitecturaBaseFront/docs/design/catalogos-campos-extra.md#ajuste-del-review-de-e2-2026-10-23)),
la [regla funcional de E2](../../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23)
(en particular [Campos del catálogo](../../features/formularios.md#campos-del-catálogo)), `src/features/business-forms/AGENTS.md`
y sus guías `api/`, `hooks/`, `components/` y `pages/`, y el código.

### 9.1 Resumen

**Qué cambió desde `9261e3d`.** Antes de E2 un catálogo Manual tenía sólo código, texto, detalle, estado y (desde E1) un
valor tipado. E2 le suma:

- **Campos extra por catálogo (CE1):** hasta 20, cada uno con nombre (español obligatorio, inglés opcional), clave
  inmutable después de guardar, tipo (texto, entero, decimal, fecha, sí o no, o «Elegir de un catálogo» con otro catálogo
  Manual como fuente), reglas (obligatorio, único, mínimo y máximo, largo) y papel (filtro o dato).
- **Valores por opción (CE2):** una columna por campo en la tabla de opciones, editable según el tipo; un campo nuevo no
  se carga hasta guardarlo.
- **Qué viaja a WhatsApp (CE3-B, sección del catálogo):** título (hasta 30 caracteres) y descripción (hasta 300) armados
  con campos fijos o extra separados por « · », con vista previa y aviso de títulos largos.
- **Un solo borrador** para metadatos, campos, presentación, opciones y movimientos, guardado en un único PUT con revisión.
- **Después del review (2026-10-23 16:44–20:27):** sólo el 409 de revisión pasa a revisión; los usos del 409 `FieldInUse`
  junto al campo; quitar un campo lo saca de la presentación del borrador y restaurarlo lo devuelve; nombres sin
  traducciones vacías; lo editado se valida contra el borrador y lo no editado contra lo guardado, y viaja sólo lo editado;
  el servidor revisa los cambios de tipo y de obligatorio y el front abre «Cambiar el tipo» con sus filas; el diálogo de
  importación usa el catálogo confirmado más reciente; vaciar una celda de catálogo ya no muestra la opción anterior.

**Tableros:** CE1, CE2 y CE3-B (la sección del catálogo se dibujó en CE3-A y CE3-B la adopta), con CE6 como guía del
circuito. Capturas: [CE1](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-campos-extra/ce1-definir-los-campos.png),
[CE2](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-campos-extra/ce2-cargar-los-valores.png),
[sección «En WhatsApp» del catálogo](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-campos-extra/ce3-seccion-del-catalogo.png),
[CE3-B](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-campos-extra/ce3-b-tambien-en-la-pregunta.png) y
[CE6](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-campos-extra/ce6-el-circuito.png). Son los tableros
elegidos; **no hay capturas de la implementación de E2 commiteadas** en `docs/design/evidence/` (los recorridos visuales
escriben sus capturas en la salida de Playwright, ver 9.13).

**Commits de esta parte:**

| Grupo | Commits |
| --- | --- |
| Diseño | `64d5ad0` congelar los tableros de campos extra · `c7d3f33` ajuste de la evaluación de impacto · `89b9002` ajuste de claridad de la vista previa · `e02d1b4` cierre visual de E2 |
| Preparación | `3566ffb` unificar las columnas de opciones (`catalogColumns`) · `f7cbc51` rutas de validación por segmentos · `5e75aff` comprobar la ruta antes de ligar errores · `89462fd` tabla ancha con desplazamiento accesible (`WideTable`) |
| E2 en el editor | `7c316ee` contratos de campos extra · `2b7812f` interpretar y presentar valores · `330caba` campos y presentación en el borrador · `c199be0` editar campos y lo que viaja a WhatsApp · `dd36911` campos extra en la tabla · `785b8f4` integrar el editor · `db1ab5e` revisar los campos extra antes de importar · `3138e04` restaurar un campo cuya baja fue rechazada |
| Contratos y pruebas | `9e4da0e` dobles alineados · `96965e4` etiquetas fuente exportadas · `96a2b41` consumir las capturas HTTP de campos extra · `d7365f7` y `68b742e` recorridos visuales · `9807676` posición entera de los campos · `e4db46c` renovar juntas las cohortes de E2 · `24d3f93` recibo Manual |
| Arreglos del review | `c2572b8` revisión sólo con `Forms.Catalog.RevisionConflict` · `4a24702` usos del 409 `FieldInUse` junto al campo · `dfb1d26` quitar un campo lo saca de la presentación · `b12a297` sin traducciones vacías en el nombre · `70067e0` y `420c311` obligatorio sólo en opciones activas · `e3e81a7` restaurar devuelve el campo a la presentación · `47e8d2a` motivo de un valor por tipo · `4475892` validar lo editado contra el borrador y mandar sólo lo editado · `e4de18c` textos del motivo · `7080141` pedir opciones por id · `ff28cbc` diálogo «Cambiar el tipo» · `2efb86e` obligatorio no editado sólo si lo piden las dos definiciones · `bc71fd4` valor no admitido por booleano o fecha, tal cual · `8e37dd3` separador de miles · `696557e` quitar el filtro de filas con error · `783473c` motivo en la celda del celular · `28ab6d6` la última opción no baja con el filtro · `9efd8e2` importación con el catálogo confirmado · `584aec8` y `982ade8` vaciar una celda de catálogo quita la etiqueta |

**Estado de los puntos del review que tocan esta parte** (detalle en cada pantalla):

| Punto | Tema | Estado en `982ade8` |
| --- | --- | --- |
| 2 | Un 409 de negocio obligaba a descartar el borrador | Corregido: `c2572b8`, `4a24702`, `dfb1d26`, `e3e81a7` |
| 3 | Nombre en inglés vacío dejaba el catálogo sin abrir | Front corregido: `b12a297` (la parte del back es del back) |
| 4 | El front validaba el cambio de tipo y el obligatorio y tapaba al servidor | Corregido: `4475892`, `2efb86e`, `bc71fd4`, `47e8d2a`, `ff28cbc`, `8e37dd3`, `696557e`, `783473c`, `28ab6d6` |
| 12 | Importar después de guardar campos usaba la definición vieja | Corregido: `9efd8e2` |
| 13 | Vaciar una celda de catálogo seguía mostrando la opción | Corregido: `584aec8` y `982ade8` |
| 16 | Aviso de títulos largos con 20 opciones | Es de la pregunta (sección 10). En el catálogo hay una limitación parecida, ver 9.7 |
| 17 | El desplegable de una celda se recorta dentro de la tabla | Pendiente |
| 23 | Chequeo de presentación sólo en la cultura de la UI y sin descripciones | Pendiente (back); el front muestra lo que llega |
| 25 | «En uso» no dice si es borrador o versión activa | Parcial |
| 27 | Guardar no hacía nada con una opción inactiva sin obligatorio | Corregido: `70067e0`, `420c311`, `2efb86e` |
| 30 | Flechas que no mueven nada con campos quitados ocultos | Pendiente |
| 31 | El formato de cada tipo no sale de una sola pieza | Pendiente |
| 32 | Lo que falta de los tableros (CE1, CE2, CE3-B) | Pendiente, salvo partes (ver 9.4, 9.6) |
| 33 | Pruebas que faltan (`FieldInUse` y usos en cohortes) | Pendiente |
| 35 | Regla de idioma | Pendiente |
| 36 | Opción nueva marcada en rojo antes de escribir | Pendiente |
| 37 | Texto debajo de los botones de «Opciones» | Pendiente |
| 38 | Botones y estados (regla de `visual-baseline`) | Pendiente en esta pantalla |
| 39 | Sección «El catálogo» con sólo Nombre y Descripción | Pendiente |

### 9.2 Navegación y rutas

| Ruta | Pantalla | Permiso de la ruta | Archivo |
| --- | --- | --- | --- |
| `/catalogos/{catalogId}` | Editor del catálogo (Manual; Filters delega en `CatalogFiltersEditorPage`) | `formcatalogs.read` | `src/features/business-forms/pages/CatalogEditorPage.tsx` |
| `/catalogos/nuevo?valueType=…` | Alta Manual con el tipo de valor elegido en el diálogo de origen | `formcatalogs.read` + `formcatalogs.manage` | idem |

- Las rutas no cambiaron con E2 (están en `src/app/routes.tsx` bajo `ProtectedRoute permission="formcatalogs.read"`;
  `/catalogos/nuevo` además bajo `formcatalogs.manage`).
- **Migas:** `Inicio › Gestión de formularios › Catálogos › {nombre}`. Desde MF1 el grupo se llama «Gestión de
  formularios» (sección 5). La hoja la pone `useBreadcrumbLeaf` con el nombre del borrador **en el idioma de la interfaz**
  (`draft.name[scope.culture]`) o «Nuevo catálogo»; el título de la página usa el mismo texto.
- **En la URL:** `page`, `pageSize` y `search` del listado de opciones (`usePagination(..., true)`); en el alta, `kind` y
  `valueType`. La guarda de salida ignora `kind` y `valueType` al comparar la dirección.
- **Nunca en la URL ni en almacenamiento:** el borrador (metadatos, campos, presentación, valores, movimientos), el idioma
  de textos elegido, las columnas ocultas del menú «Campos», el estado de «Cambiar el tipo» y su filtro de filas con error
  (los `ids` viajan sólo en la consulta HTTP), el archivo o el pegado de la importación.

### 9.3 Editor del catálogo Manual: disposición general

`src/features/business-forms/pages/CatalogEditorPage.tsx` (función `ManualCatalogEditor`). Es una sola pantalla, sin
pestañas.

#### Cabecera (`Page`)

- Flecha volver «Catálogos» (a `/catalogos`).
- Título: nombre del catálogo en el idioma de la interfaz, o «Nuevo catálogo».
- **Estado**, en texto chico: «Usado en N formularios · » (sólo si `usageCount` es conocido; `null` no se muestra) +
  «Activo» o «Dado de baja» + « · Cambios sin guardar» cuando el borrador difiere del baseline.

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md#encabezados), commit `bb98bf2`):
> el estado junto al título muestra sólo el estado del objeto («Activo», «Dado de baja»). «Cambios sin guardar» (clave
> `dirty`) no va: que Guardar esté habilitado ya lo dice. Es uno de los lugares del punto 38 del review. Se conserva el
> aviso al salir con cambios.

- **Acciones a la derecha** (sólo con `formcatalogs.manage` y origen Manual), en este orden:
  1. «Dar de baja» o «Reactivar» (`CatalogStateAction`, sólo con catálogo guardado): abre una confirmación y se aplica con
     Guardar. Deshabilitado si no se puede guardar.
  2. «Descartar» (secundario): abre «¿Descartar los cambios?» / «Vas a perder lo que cambiaste sin guardar.» con
     «Seguir editando» y «Descartar» (destructivo). Deshabilitado mientras guarda. (El anexo anterior registró que este
     botón mostraba la clave literal `keepEditing`; se corrigió en E1, `b7b47c5`, y hoy usa `stay`.)
  3. «Guardar»: deshabilitado sin capacidad de guardar (sin Manage, guardando o en revisión), o si hay valores tipados
     inválidos, valores de campos extra inválidos en opciones cambiadas o problemas locales de opciones (código
     faltante o repetido, texto faltante o de más de 30).

#### Cuerpo, de arriba hacia abajo

1. **Superficie «Revisión del catálogo»** (sólo tras un 409 `Forms.Catalog.RevisionConflict`): «El catálogo puede haber
   cambiado. Revisá la versión actual antes de guardar de nuevo.» + «Revisar», que abre el diálogo de revisión (ver 9.9).
2. **Grilla de dos columnas desde `xl` (1280 px)**: a la izquierda, metadatos (`CatalogMetadataFields`: el selector
   «Textos en» y la superficie «El catálogo») y la sección «En WhatsApp» (`CatalogPresentationSection`, 9.7); a la
   derecha, de 24 rem y fija al desplazar, la **vista previa** (`CatalogPreview`, 9.7). Debajo de `xl`, la vista previa baja
   debajo de las secciones, sin quedar fija.
3. **Sección «Campos»** (`CatalogFieldsSection`, 9.4).
4. Si hay orden A–Z pendiente: superficie con «Se ordenará A–Z al guardar.» y «Cancelar orden A–Z».
5. Si hay problemas locales de opciones: el párrafo en rojo «Revisá las opciones marcadas antes de guardar.»
6. Si hay movimientos encolados: «N movimientos pendientes. Se aplicarán en este orden al guardar.» y su botón para
   descartarlos, «Descartar movimientos» (E1).
7. **Sección «Opciones»** (`CatalogManualItemsEditor`, 9.6), con el buscador, el chip de filas con error si corresponde, el
   menú «Campos», la tabla y el paginado. Mientras cargan las opciones de un catálogo guardado: spinner en una superficie.

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md#formularios-y-diálogos)):
> «Revisá las opciones marcadas antes de guardar.» es un texto que explica el estado de Guardar; los errores van en sus
> campos (punto 38). Los avisos «Se ordenará A–Z al guardar.» y «N movimientos pendientes…» no figuran en el punto 38,
> pero son textos de estado junto a botones: conviene revisarlos con la misma regla (observación de este relevamiento).

> **No copiar — review E2, punto 39** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)): la superficie
> «El catálogo» muestra hoy Nombre, «De dónde salen» y «Tipo de valor» como campos de sólo lectura con explicaciones
> («¿De dónde salen las opciones? No se puede cambiar después: para otro origen, se arma otro catálogo.» y «Se elige al
> crear el catálogo y después no se puede cambiar.») y una Descripción obligatoria. La decisión: sólo Nombre y
> Descripción, descripción opcional en back y front, y origen y tipo como un dato de una línea arriba de Nombre (por
> ejemplo «Escritas a mano · Texto»), sin caja ni explicación. Estado en `982ade8`: pendiente.

> **No copiar — regla de idioma del 2026-10-23** (review E2, punto 35): la pantalla tiene el selector «Textos en»
> (`contentLanguage`) con Español/English y el texto «Completá el nombre y la descripción en español e inglés. En las
> opciones, el inglés es opcional.»; el guard exige nombre y descripción en es **y** en (`requiredText` en
> `src/features/business-forms/api/catalogResponse.ts`); el texto de una opción es obligatorio sólo en español
> (`catalogItemColumns` marca `required: culture === "es"` y `catalogItemProblems` exige `text.es`); el editor de la opción
> dice «Si no completás el texto en inglés, se usa el de español.»; los campos tienen «Nombre» y «Nombre en inglés». La
> regla nueva: se escribe en el idioma del menú, que es el original y el único obligatorio; la traducción se carga
> cambiando el idioma del menú; sin selector en pantalla; un campo sin traducir muestra el original como referencia, sin
> copiarlo. Además, un error de guardado en un campo que no está a la vista tiene que verse en la página: como no hay
> pestañas de idioma, un error del idioma original con el menú en el otro idioma dice qué falta y en qué idioma (texto del
> punto 35 actualizado en el back `2baa3c3c`). Hoy la página cambia de pestaña de idioma ante `name.en`/`description.en` y
> el error sólo se ve en esa pestaña. Estado en `982ade8`: pendiente.

#### Estados de la página

| Situación | Qué se ve |
| --- | --- |
| Sin usuario todavía | `Page` «Catálogos» con spinner |
| Sin `formcatalogs.read`, o 403 en el detalle, las opciones, el guardado o la exportación | Página «No tenés permiso» (`ForbiddenPage`) |
| Detalle cargando (sin baseline) | Spinner |
| Detalle 404 | Estado vacío «No encontramos este catálogo.» + «Reintentar», sin toast |
| Detalle con otro error | Estado vacío «No pudimos cargar el catálogo.» + «Reintentar» y **un** toast con el error de lectura |
| Alta sin `formcatalogs.manage` | «No tenés permiso» |
| Opciones con error y sin datos | Estado vacío «No pudimos cargar las opciones.» + «Reintentar»; el toast lo pone el dueño de la página |
| Guardado 400 con errores de campo | Se ligan a su control (ver 9.4, 9.6 y 9.7), sin toast |
| 409 `Forms.Catalog.RevisionConflict` | Superficie de revisión; sin toast; el borrador queda de sólo lectura hasta releer y adoptar |
| 409 `Forms.Catalog.SourceInUse` (dar de baja un catálogo que es fuente de un campo o de un dato de las personas) | Toast con el `detail` del servidor; el editor sigue editable |
| 409 `Forms.Catalog.FieldInUse` | Error y usos junto al campo (9.4); sin toast si todas las rutas de error se reconocen |
| Red o 5xx al guardar | Toast «No pudimos confirmar el guardado. Volvé a guardar. Referencia: {traceId}»; se puede volver a Guardar con la misma revisión |
| 429 | Toast con el `detail` del servidor. No hay cuenta regresiva ni se deshabilita Guardar (no hay manejo propio de `retryAfterSeconds` en esta página) |
| Guardado confirmado | Toast «Catálogo guardado.»; en el alta, la dirección pasa a `/catalogos/{id}` reemplazando el historial |

Una relectura del detalle con permiso **no** reemplaza el borrador montado: la página conserva el detalle del montaje
(`mountedDetail`) mientras no cambie el dueño (actor, idioma, catálogo). Perder Read lo descarta.

#### Celular (390 y 320 px)

- La cabecera sigue el `Page` común: título a todo el ancho, estado debajo y acciones alineadas a la derecha en otra fila.
- Todas las secciones van en una columna; la vista previa baja al final de la grilla superior.
- «Campos» deja de ser tabla y pasa a lista (9.4). La tabla de opciones no edita en línea: cada opción se edita entera
  debajo de la tabla (9.6).
- Los recorridos `src/test/visual/catalogExtraFields.spec.ts` comprueban a 1440, 390 y 320 px, en es y en, que el campo
  nuevo, la celda y la región de la tabla quedan dentro del ancho y que el documento no se desplaza de costado.

### 9.4 Sección «Campos» (CE1)

`src/features/business-forms/components/CatalogFieldsSection.tsx` y `CatalogFieldEditor.tsx`.

#### Banda de la superficie

- Título «Campos». Acción a la derecha de la banda:
  - sin Manage (o en sólo lectura): «Ver campos», que abre y cierra;
  - con Manage y la sección cerrada: «Editar campos», que la abre;
  - con Manage y la sección abierta: **«Agregar campo»** (deshabilitado con 20 campos). Una vez abierta, no hay botón para
    volver al resumen.

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md#formularios-y-diálogos)):
> el mismo botón de la banda cambia de texto y de función («Editar campos» → «Agregar campo») con la acción; la regla dice
> que los botones no cambian de lugar ni de forma con cada acción. No está en la lista del punto 38: es una observación de
> este relevamiento para revisarla con la regla.

- Si el último guardado devolvió conteos del servidor, debajo de la banda va un bloque de alerta en rojo con los errores de
  la raíz `fields` y, según lo que vino: «Faltan valores en N opciones activas.», «N opciones tienen valores que no
  convierten.» y «N valores pueden convertirse.»

#### Escritorio (640 px o más)

- **Cerrada:** una fila «Fijos: código, texto y detalle. Extra:» seguida de una insignia por campo activo («Zona ·
  Filtro» para los de papel filtro), o «Todavía no hay campos extra.».
- **Abierta:** tabla con columnas Campo, Clave, Tipo, Reglas, Papel y Acciones.
  - Campo: nombre en el idioma de textos (si falta, el español; sin nombre, «Nuevo campo»), y «Nuevo, sin guardar» si no
    tiene identidad.
  - Clave, o «—». Tipo en palabras: «Texto», «Entero», «Decimal», «Fecha», «Sí o no», «Elegir de un catálogo».
  - Reglas: «Obligatorio · Único» o «—». Papel: insignia «Filtro» o «Dato».
  - Acciones: flechas Subir/Bajar (deshabilitadas en el primer y último activo), lápiz «Editar {campo}» (o «Ver {campo}»
    en sólo lectura) que despliega el editor en una fila debajo, y tacho «Quitar {campo}». Sin Manage sólo queda el lápiz.
- Se listan los campos activos y, además, los quitados que tienen un error del servidor (para poder restaurarlos).

> **No copiar — review E2, punto 32 (CE1)** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)): falta la
> columna «#», el detalle del tipo («Zonas · 72 opciones», «De 0 a 180»; hoy la columna Tipo dice sólo el nombre del tipo)
> y «y sus N valores» en la casilla de quitar (hoy «Sí, quitar el campo y sus valores», sin número). Estado en `982ade8`:
> pendiente.

> **No copiar — review E2, punto 30**: las flechas se habilitan por la posición entre los campos **activos**, pero mueven
> en el arreglo completo (`moveItem(fields, fields.indexOf(field), ±1)` en `src/features/business-forms/editorModel.ts`),
> que incluye los quitados ocultos: a veces el cambio no se ve. Hay que mover sólo entre los activos. Estado en
> `982ade8`: pendiente.

#### Celular (menos de 640 px)

- No hay resumen cerrado ni tabla: siempre se ve una lista, un campo por bloque, con nombre, tipo, «Nuevo, sin guardar»
  y el error de un quitado; a la derecha, las mismas acciones. El editor del campo se despliega debajo de su bloque, en una
  columna. El botón de la banda funciona igual que en escritorio.
- Sin campos activos y con Manage: «Todavía no hay campos extra.» y un botón «Agregar campo» al pie de la sección.

#### Editor de un campo (`CatalogFieldEditor`)

Caja gris dentro de la fila (o del bloque), con estos controles, en dos columnas desde `sm`:

| Control | Comportamiento |
| --- | --- |
| «Nombre» (obligatorio, hasta 120) | Edita `name.es`. Al abrir o al llegar un error, toma el foco (o lo toma el primer control con `aria-invalid`) |
| «Nombre en inglés» (hasta 120) | Edita `name.en`; si queda en blanco **no viaja** (`catalogFieldsBody`, `b12a297`) |
| «Clave» (obligatoria, hasta 40) | Ayuda «La usan la plantilla, la importación y lo guardado. No cambia después de guardar.» Sólo lectura una vez guardado. El formato (`^[a-z][a-z0-9_]{0,39}$`) lo valida el servidor; el front no lo valida antes |
| «Tipo» (obligatorio) | Desplegable con los seis tipos. Si el campo ya tiene valores, ayuda «Al guardar se revisan todos los valores. Si alguno no convierte, el cambio no se guarda.» Cambiar el tipo limpia la fuente (salvo catálogo), el mínimo y máximo (salvo entero, decimal y fecha) y el largo (salvo texto) |
| Fuente (sólo «Elegir de un catálogo») | Reutiliza `CatalogQuestionSelector` con `manualOnly`: lista catálogos Manual activos; los demás se ven deshabilitados. No excluye al propio catálogo: lo rechaza el servidor |
| «Reglas»: «Obligatorio» y «Único» | «Obligatorio» está deshabilitado mientras el campo no se guardó, con la descripción «Un campo nuevo nace opcional: para hacerlo obligatorio, todas las opciones activas necesitan un valor.» |
| «Papel» | Dos tarjetas: «Filtro» («Para mostrarle a cada persona lo suyo, o para un filtro fijo de una pregunta.») y «Dato» («Se guarda con la opción y viaja con la respuesta.»). Un campo nuevo nace «Dato» |
| «Mínimo» y «Máximo» (entero, decimal, fecha) | Texto con teclado numérico o decimal, o fecha. Se interpretan en la cultura del borrador y viajan canónicos |
| «Máximo de caracteres» (texto) | Número de 1 a 300 |
| «Listo» | Cierra el editor |

Si un 409 `FieldInUse` marca el papel, el tipo o la fuente, arriba del editor se listan los usos (ver abajo).

> **No copiar — regla de idioma del 2026-10-23** (review E2, punto 35): los pares «Nombre» / «Nombre en inglés» pasan a un
> solo campo en el idioma del menú; con el menú en el idioma que no es el original, el campo sin traducción muestra el
> original como referencia, sin copiarlo. Estado en `982ade8`: pendiente.

> **Regla de interfaz, a revisar:** «Obligatorio» deshabilitado con su motivo escrito debajo. La regla nueva pide que el
> motivo de un deshabilitado no obvio vaya como tooltip; no figura en el punto 38 (observación de este relevamiento).

#### Agregar, ordenar, quitar y restaurar

- **Agregar campo:** suma uno al final (`newCatalogField`: tipo texto, papel dato, opcional, sin clave), abre la sección y
  despliega su editor. Su columna en la tabla aparece apagada (9.6).
- **Ordenar:** las flechas cambian la posición; al guardar, `position` se reescribe de 1 en adelante según el orden.
- **Quitar:** el tacho abre el diálogo «Quitar «{campo}»».
  - Si algún formulario lo usa (usos con etapa distinta de `Catalog`), la descripción dice «El campo está en uso. Sacá
    esos usos para poder quitarlo.», lista «{formulario} · {pregunta}» y sólo ofrece «Cerrar».
  - Si no, dice «Se quita de la tabla, de la plantilla y del Excel. Las respuestas que ya lo guardaron lo conservan.»; si
    tiene valores, pide la casilla «Sí, quitar el campo y sus valores» antes de habilitar «Quitar el campo» (destructivo).
  - Un campo nunca guardado se descarta del borrador; uno guardado queda inactivo y, si tenía valores, su id viaja en
    `confirmFieldRemovals`.
  - El uso por la presentación «En WhatsApp» del propio catálogo **no impide** quitarlo: al quitarlo sale también del
    título y la descripción del borrador; si el título queda vacío vuelve al texto de la opción (`dfb1d26`).
- **Restaurar:** un campo quitado que volvió con error del servidor (por ejemplo 409 `FieldInUse` o
  `FieldRemovalNotConfirmed`) queda visible con el error en rojo, sus usos y el botón «Restaurar el campo», que lo
  reactiva, retira su confirmación y despliega su editor, sin perder los demás cambios. Si la presentación del borrador
  sólo había seguido esa baja, el campo vuelve a su lugar en «En WhatsApp»; si el autor la editó después, no se toca
  (`e3e81a7`).

#### Errores del servidor que llegan a la sección

Los errores `fields[i].…` se ligan al campo por su `localId` según el índice del cuerpo enviado
(`indexedValidationIssues` en `src/features/business-forms/validationPaths.ts`). La sección se abre sola y despliega el
primer campo con error.

| Código | Dónde se ve |
| --- | --- |
| `Forms.Catalog.FieldValuesMissing` (pasar a obligatorio con faltantes) | Error en «Obligatorio» y «Faltan valores en N opciones activas.» arriba de la sección |
| `Forms.Catalog.FieldTypeNotConvertible` (cambio de tipo o de fuente) | Error en «Tipo», conteos arriba y el diálogo «Cambiar el tipo» (9.5) |
| `Forms.Catalog.FieldRemovalNotConfirmed` | Error en el campo quitado, con «Restaurar el campo» |
| `Forms.Catalog.FieldValueDuplicate` (único) | En la celda (`items[n].fields.{clave}`) o en «Único» |
| `Forms.Catalog.FieldInUse` (409) | Error en el quitado o en papel/tipo/fuente, más la lista de usos «{formulario} · {pregunta}» sin GUID (`4a24702`) |

> **No copiar — review E2, punto 2** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)): antes, **cualquier**
> 409 pasaba el editor a revisión de sólo lectura y la única salida era «Usar la revisión actual», que perdía lo editado
> (le pasaba a `SourceInUse` y a `FieldInUse`); «Restaurar el campo» no aparecía y se ignoraban los `uses`. Estado en
> `982ade8`: corregido. Sólo `Forms.Catalog.RevisionConflict` entra en revisión (`isRevisionConflict` en
> `useCatalogDraftEditor`, `c2572b8`); `SourceInUse` va al Toaster; `FieldInUse` se liga al campo con sus usos (`4a24702`);
> quitar un campo lo saca de la presentación (`dfb1d26`) y restaurarlo lo devuelve (`e3e81a7`).

> **No copiar — review E2, punto 25**: la lista de usos (en el diálogo de quitar y junto al error del 409) muestra
> formulario y pregunta pero ignora `stage`: no dice si el uso es del borrador o de la versión activa, y falta «Ir a la
> pregunta» de CE1. Lo que sí se corrigió: el uso por la presentación del propio catálogo ya no aparece como formulario en
> el diálogo de quitar (se filtra `stage === "Catalog"`). Estado en `982ade8`: parcial. En el back, `84289eef` (posterior a
> `982ade8`, en el repo del back) suma `questionId` a cada uso para poder llevar a la pregunta; el guard estricto del front
> en `982ade8` (`catalogFieldUsageSchema`) todavía no admite esa propiedad, así que **el contrato está en movimiento**.

> **No copiar — review E2, punto 3**: si alguien escribía el nombre en inglés de un campo y lo borraba, viajaba `en: ""`, el
> back lo guardaba y desde ahí fallaban el guard del PUT y de cada GET (el catálogo no volvía a abrir). Estado en
> `982ade8`: el front corrigió su parte (`catalogFieldsBody` recorta el español y omite el inglés en blanco, `b12a297`);
> el guard no se relajó (`textMapForField` exige `min(1)`). La parte del back (descartar traducciones vacías e ignorar
> encabezados vacíos al importar) es del back.

### 9.5 Diálogo «Cambiar el tipo» y filtro «Con error · N filas»

`src/features/business-forms/components/CatalogFieldConversionDialog.tsx`; estado en `CatalogEditorPage` (`FieldConversion`).

**Cuándo se abre:** al guardar, si el servidor responde 400 `Forms.Catalog.FieldTypeNotConvertible` con `invalidRows`
legibles y el campo con error en `type` tiene un tipo guardado. El front **ya no** frena Guardar por un cambio de tipo o de
obligatorio: el servidor revisa todos los valores guardados y responde (`4475892`).

**Contenido (diálogo central, `sm:max-w-xl`; en el celular, el diálogo común):**

1. Título «Cambiar el tipo de «{campo}»».
2. Si cambió el tipo (no sólo la fuente): «De {tipo guardado} a {tipo nuevo}», en minúscula.
3. Descripción: «Se revisan los {N} valores cargados: {M} se convierten y {K} no.» (con separador de miles: «1.200» en
   español, «1,200» en inglés, `8e37dd3`; en singular, «Se revisa 1 valor cargado: …»).
4. Tabla «Fila» / «Qué pasa» con hasta **5** filas: el número de fila del servidor y el motivo armado por
   `catalogFieldProblemText` con el valor intentado recortado a 40 caracteres: «Falta: es obligatorio.», ««30 días» no es un
   número entero.», ««x» no es una fecha.», ««x» no es sí o no.», ««x» no es una opción del catálogo.», ««x» está fuera del
   rango: de 0 a 180.», ««x» supera los N caracteres.»
5. «Y N más.» si quedan filas sin mostrar (N calculado contra `invalidCount`, el total del servidor).
6. Aviso: «Corregilos en la tabla, o exportá, corregí el Excel e importalo. El tipo cambia al guardar, cuando todos entran.»
7. Pie: «Cancelar» y **«Ver los N en la tabla»** («Ver la fila en la tabla» con una). N es la cantidad de filas recibidas,
   como mucho 50 (el servidor manda las primeras 50 en orden de fila).

**Después:**

- Cada fila recibida queda marcada en su celda con su motivo hasta que se edita ese valor, hasta el próximo Guardar, o hasta
  que el campo vuelva a cambiar de tipo o de fuente (o deje de estar activo). Cancelar sólo cierra el diálogo: el borrador
  conserva el tipo nuevo y las marcas.
- «Ver los N en la tabla» cierra el diálogo y deja en la tabla **sólo esas opciones**: la consulta de ítems pasa a
  `{ page: 1, pageSize: 50, sort: "position", ids: [...] }` (los `ids` se repiten en la query, `7080141`), se oculta el
  paginado y aparece el chip «Con error · N filas» con «Quitar el filtro». Buscar, guardar de nuevo, quitar el chip o
  adoptar la revisión actual vuelven al listado (`696557e`).
- Mientras se ven sólo esas filas, la página conserva el total del listado completo para que la última opción del catálogo
  siga sin poder bajar (`28ab6d6`).
- En el celular, donde la celda es texto, el motivo va debajo del valor (`783473c`).

> **No copiar — review E2, punto 4** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)): el front validaba
> en local el cambio de tipo y el pase a obligatorio: marcaba en error cada fila visitada y deshabilitaba Guardar, así que
> el servidor (que sí convierte por código o texto y responde «faltan N») nunca recibía el pedido; además, cambiar de entero
> a texto y editar una fila en el mismo guardado mandaba formatos mezclados. Estado en `982ade8`: corregido. Lo editado (en
> una opción nueva, todo) se valida contra la definición del borrador; lo no editado, contra la definición guardada
> (`savedFields`), y el obligatorio de un valor no editado se exige sólo si lo piden las dos (`catalogItemFieldIssues`,
> `4475892`, `2efb86e`). El PUT manda en `items[i].fields` sólo los valores editados. El diálogo «Cambiar el tipo» se arma
> con las filas del servidor (`ff28cbc`, `47e8d2a`).

### 9.6 Tabla de opciones con campos extra (CE2)

`src/features/business-forms/components/CatalogManualItemsEditor.tsx`, `CatalogItemsTable.tsx`, `CatalogExtraFieldInput.tsx`,
`CatalogItemFields.tsx`, `src/features/business-forms/catalogColumns.ts` y `src/shared/ui/WideTable.tsx`.

#### Superficie «Opciones»

- Banda: «Opciones» y, a la derecha, «Agregar opción» (con Manage) y las acciones de E1 (`CatalogOptionsActions`: «Pegar
  desde Excel», «Importar», «Exportar» y el menú «Más acciones de opciones» con «Descargar la plantilla», «Ordenar A–Z» y
  «Generar los códigos que faltan»; en el celular, todo dentro del menú). El detalle de esas acciones está en la sección 8.
- Barra bajo la banda: el buscador «Buscar opción» (sólo con catálogo guardado), el chip de filas con error si está activo y,
  si hay campos activos, el menú **«Campos»** (`FilterMenuTrigger` + casillas por campo) que muestra u oculta columnas.
  Esa visibilidad es local de la sesión; no cambia valores ni viaja.
- Con selección de página (E1): «N seleccionadas de esta página», «Dar de baja», «Reactivar» y el botón para quitar la selección.

> **No copiar — review E2, punto 37** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)): con el catálogo
> sin guardar o con cambios, debajo de los botones aparece un texto («Guardá el catálogo antes.» o «Guardá o descartá los
> cambios antes de importar.») que los empuja y los desalinea del título y del margen. En el celular el motivo va dentro
> de cada ítem del menú. Decisión del 2026-10-23: sin texto visible; los botones al margen derecho de la sección, a la
> altura del título; el motivo como ayuda al pasar el mouse o con el foco, y para el lector de pantalla. Estado en
> `982ade8`: pendiente (`src/features/business-forms/components/CatalogOptionsActions.tsx` sigue dibujando el párrafo).

#### Columnas

Orden (`catalogItemColumns`): selección (con Manage) · «#» (posición; «—» en una opción nueva) · **Texto** (fija a la
izquierda) · Código · **una columna por campo activo**, en el orden de los campos · Valor (si el tipo de valor no es
Texto) · Detalle · Estado · Acciones (fija a la derecha).

- El encabezado de un campo es su nombre en el idioma de textos (si falta, el español). Un campo **sin guardar** suma
  debajo, en el mismo encabezado, «Guardá para cargar valores», y sus celdas quedan deshabilitadas con el mismo texto.
- La tabla va dentro de una región con nombre «Opciones» que se desplaza de costado con teclado (`WideTable`, rol
  `region` y `tabIndex=0`); la página nunca se desplaza de costado.

> **No copiar — review E2, punto 32 (CE2)**: falta la indicación visual de que hay más columnas a la derecha (la región sólo
> se desplaza) y faltan las ayudas de tipo y rango en el editor del celular. Estado en `982ade8`: pendiente.

> **Regla de interfaz, a revisar:** «Guardá para cargar valores» escrito en el encabezado y debajo de cada celda apagada es
> el motivo de un deshabilitado. CE2 lo dibujaba así; con la regla nueva, si el motivo no es obvio va como tooltip, y el
> punto 37 pide anotar en el diseño cuando el tablero mostraba el texto. No está en el punto 38 (observación de este
> relevamiento).

#### Celdas de un campo extra (escritorio con Manage)

`CatalogExtraFieldInput`, con nombre accesible «Fila {n} · {campo}»:

| Tipo | Control |
| --- | --- |
| Texto | Entrada con largo máximo del campo (o 300) |
| Entero / Decimal | Entrada de texto con teclado numérico o decimal; valor exacto (sin `Number`), en la cultura del borrador |
| Fecha | Entrada de fecha |
| Sí o no | Casilla de tres estados: vacía (indeterminada), Sí, No; si el campo no es obligatorio y tiene valor, «Limpiar» |
| Elegir de un catálogo | Buscador (`Autocomplete`) con las opciones **activas** de la fuente ordenadas por texto, de a 20, con espera de 300 ms; si hay más, el pie reutiliza la clave `catalogQuestion.search`, que dice «Buscar catálogo» aunque se están buscando opciones (texto equivocado, observación de este relevamiento); si la fuente no está activa o no es Manual, el control queda deshabilitado; un fallo muestra «Reintentar». Muestra el texto de la opción elegida, nunca el GUID |

- En una opción **inactiva**, un campo obligatorio se presenta como opcional (sin asterisco y con «Limpiar»), con la misma
  regla del guardado (`catalogItemFieldRule`, `420c311`).
- **Valor que el tipo nuevo no admite** (cambio de tipo pendiente de guardar, `unconvertedFieldValue`): en catálogo, uno que
  no es una opción, se ve tal cual en el buscador; en sí o no y fecha, en una entrada de sólo lectura con «Limpiar», que lo
  vacía para cargarlo con la casilla o la fecha (`bc71fd4`).
- Un error queda debajo de la celda, en rojo y con `role="alert"`. Para los valores editados, el texto es genérico por
  motivo: «Revisá el valor: no corresponde al tipo del campo.», «Falta el valor obligatorio.», «El valor está fuera del
  mínimo o máximo del campo.» o «El texto supera el largo permitido.» Para las filas marcadas por «Cambiar el tipo», el
  texto con valor y motivo de 9.5.
- Sin Manage o por debajo de 640 px, la celda es texto: «—» si no hay valor; la etiqueta de la opción fuente; «Sí»/«No»; la
  fecha numérica (`formatDateOnly(…, "numeric")`); y, si hay una marca, el motivo debajo.

> **No copiar — review E2, punto 32 (CE2)**: el tablero pide que la fila diga qué pasa con el valor y el motivo («Fila 3 ·
> Plazo de pago (días): «30 días» no es un número entero»). Hoy sólo las marcas de «Cambiar el tipo» llevan valor y motivo;
> un valor inválido escrito en la tabla muestra el texto genérico por motivo. Estado en `982ade8`: parcial.

> **No copiar — review E2, punto 13**: vaciar una celda de catálogo dejaba el valor en null pero la celda volvía a mostrar
> la opción anterior («Centro»), y al guardar se borraba sin que se viera. Estado en `982ade8`: corregido. El buscador
> muestra texto sólo si hay valor (`584aec8`) y el borrador guarda sólo los textos que llegan con el valor elegido: vaciar
> o escribir el valor sin elegir una opción quita la etiqueta anterior (`withCatalogFieldValue` en
> `src/features/business-forms/catalogItemEdits.ts`, `982ade8`).

> **No copiar — review E2, punto 17**: la lista del buscador se dibuja con posición absoluta dentro del contenedor de la
> tabla, que tiene `overflow-x-auto` (`src/shared/ui/Autocomplete.tsx` y `src/shared/ui/table.tsx`): en las últimas filas o
> con pocas filas se corta u obliga a desplazar la caja (el review lo marca como plausible, sale del CSS). Arreglo: lista en
> un portal, verificado en el navegador. Estado en `982ade8`: pendiente.

> **No copiar — review E2, punto 31**: el formato de cada tipo no sale de una sola pieza. La tabla usa
> `formatDateOnly(…, "numeric")` («15/03/2027»); la revisión de importación usa `formatCatalogValue` con el formato por
> defecto («15 mar 2027»); la presentación arma el decimal con `replace(".", ",")` en
> `src/features/business-forms/catalogPresentation.ts`. Estado en `982ade8`: pendiente.

#### Celular: editor por opción

- Por debajo de 640 px la tabla no edita en línea: la columna Texto pasa a «Texto y código» (código debajo del texto) y
  cada fila tiene el lápiz «Editar {opción}».
- Al tocarlo, debajo de la tabla aparece «Editando {opción}» y el editor completo de la opción (`CatalogItemFields`): Texto
  con contador «n/30», Código con «El código es para la empresa: no se muestra en WhatsApp.», **un control por campo**
  (mismos controles que la celda, con su nombre como etiqueta y el asterisco si corresponde), Valor, Detalle, «Generar
  desde el texto» si falta el código, «Activa» y «Listo». En escritorio, el mismo editor se despliega en una fila debajo de la opción.
- «Listo» devuelve el foco al lápiz de la fila (o a «Agregar opción»).

#### Validación local de las opciones

- Se validan sólo las opciones cambiadas (`catalogItemChanges`), con `catalogItemFieldIssues`: lo editado contra el
  borrador, lo no editado contra la definición guardada; el obligatorio sólo en activas, y en lo no editado sólo si lo
  piden las dos definiciones.
- Un problema bloquea Guardar y marca la celda; la página expande la primera opción con error tras un 400 del servidor.

> **No copiar — review E2, punto 27**: si se editaba una opción inactiva a la que le faltaba un obligatorio, Guardar
> quedaba habilitado pero no hacía nada (la página y el hook aplicaban reglas distintas). Estado en `982ade8`: corregido;
> la página, el guard del guardado y el cuerpo usan la misma regla (`catalogItemFieldRule`, `70067e0`, `420c311`).

> **No copiar — review E2, punto 36**: «Agregar opción» crea la fila vacía y enseguida marca Texto y Código con «Completá
> este campo.» (`catalogItemProblems` exige código y `text.es` en toda opción conocida) y aparece «Revisá las opciones
> marcadas antes de guardar.». Un campo vacío se marca recién al intentar guardar o al salir de un campo tocado; el código
> repetido y el valor inválido siguen apareciendo al escribir. Estado en `982ade8`: pendiente.

### 9.7 Sección «En WhatsApp» del catálogo y vista previa (CE3-B)

`src/features/business-forms/components/CatalogPresentationSection.tsx`, `CatalogPreview.tsx` y
`src/features/business-forms/catalogPresentation.ts`.

#### Sección «En WhatsApp»

Superficie con relleno, título «En WhatsApp», en la columna izquierda bajo «El catálogo».

1. **«Título · hasta 30 caracteres»:** insignias con las partes elegidas (por defecto «Texto»), cada una con «×» («Quitar
   {parte} de {Título…}»); la última del título no se puede quitar. Al lado, el desplegable «Agregar campo» con lo que falta
   elegir: Texto, Código, Detalle, Valor (si el tipo no es Texto) y los campos activos; un campo sin guardar aparece
   deshabilitado con « · Guardá para cargar valores». Hasta 24 partes (lo fija el guard).
2. **«Descripción · hasta 300»:** igual, por defecto «Detalle»; puede quedar vacía.
3. **«Por ejemplo»:** una caja con el título y la descripción de la primera opción activa cargada, armados igual que en el
   envío: partes no vacías unidas por « · », etiquetas fuente para campos de catálogo, «Sí»/«No», fecha numérica y decimal en
   el formato del idioma.
4. **Aviso de títulos largos** (en color de advertencia, `role="status"`): «N opciones superan los 30 caracteres. Podés
   guardar; revisá el título antes de publicar.» Con el borrador limpio y ejemplos del servidor, debajo una lista con hasta 3
   títulos de ejemplo.
5. «No salen del sistema: {campos}.» con los campos activos que no se eligieron.
6. «Un cambio vale para los envíos nuevos: lo ya enviado y las respuestas guardadas conservan lo que se mostró.»

Errores del servidor en `presentation`, `presentation.title` o `presentation.description` van debajo de su parte. El aviso
no bloquea Guardar; publicar lo frena después (sección 10). Sin Manage, todo queda de sólo lectura.

**De dónde sale el número del aviso:** con el borrador **limpio**, del chequeo completo del servidor (`presentationCheck`
del detalle confirmado: el leído, el guardado, el importado o el adoptado). Con cambios, el mayor entre ese número (que
entonces es 0, porque no se pasa) y una cuenta **local sobre las opciones cargadas de la página visible**. Es decir, con el
borrador sucio el aviso puede subestimar los títulos largos de un catálogo de muchas páginas (observación de este
relevamiento; es el mismo problema de fondo que el punto 16 describe para la pregunta).

> **No copiar — review E2, punto 23**: el chequeo del servidor mira sólo la cultura de la UI y no las descripciones; debería
> calcularse en es y en, con el tope de 300 de la descripción. Es del back; el front muestra lo que llega. Estado en
> `982ade8`: pendiente.

#### Vista previa del catálogo (`CatalogPreview`)

Columna derecha desde `xl`, fija al desplazar; debajo de `xl`, al final de la grilla superior.

- Encabezado «Así lo ve el empleado», el segmentado «Una opción / Varias» y «Lo decide cada pregunta del formulario. Este
  selector cambia solo la vista previa.»
- Un teléfono con el nombre del catálogo: en «Una opción», el campo cerrado «Elegí una opción» y, al tocarlo, la lista con
  título y descripción de cada opción activa y «Volver»; en «Varias», «Elegí hasta 3» y casillas (como mucho 3). Abajo, el
  botón final deshabilitado y la aclaración de vista previa.
- Usa las opciones activas **de la página cargada** y la presentación del borrador; los textos del teléfono salen en el
  idioma de textos elegido.

### 9.8 Importar y pegar con campos extra

`src/features/business-forms/components/CatalogImportDialog.tsx` y `src/features/business-forms/api/catalogImports.ts`. El
recorrido completo (archivo o pegado, revisión, confirmaciones, aplicar) es de E1 (sección 8); E2 suma:

- **Altas:** una columna por campo activo, después de Código, con el nombre del campo en el idioma de la interfaz; los
  valores con su formato (sí/no, número y fecha); los de catálogo con la etiqueta que manda el productor (`fieldTexts`), sin
  consultas ni GUID.
- **Cambios:** «{columna}: {antes} → {después}», con `beforeText`/`afterText` del productor para los campos de catálogo.
- **Problemas:** la columna de un error se rotula con el nombre del campo; un encabezado desconocido se muestra literal.
  `MissingColumn` y `DuplicateColumn` se muestran junto al archivo o el pegado, sin toast.
- Aplicar acepta un recibo con revisión **igual o mayor** a la capturada (un «Aplicar» sin diferencias conserva la revisión).

> **No copiar — review E2, punto 12**: el diálogo recibía el detalle de cuando se entró a la pantalla: tras guardar campos
> nuevos, «Altas» no los mostraba y «Cambios» mostraba GUID como nombre de columna y «GUID → GUID» en la zona. Estado en
> `982ade8`: corregido. El editor conserva `confirmed`, el detalle del que salió su baseline (leído, guardado, importado o
> adoptado), y la página pasa `editor.confirmed ?? initial` al diálogo y al chequeo de la presentación (`9efd8e2`).

### 9.9 Hooks y estado

**`useCatalogDraftEditor`** (`src/features/business-forms/hooks/useCatalogDraftEditor.ts`), dueño único del borrador:

- **Dueño:** `[actorId, culture, catalogId, kind, valueType]` más una **época** (`epoch`) que separa callbacks de un baseline
  anterior. Cambiar de dueño reinicia el estado y aborta la operación en curso; una respuesta tardía de otro dueño o de una
  operación abortada se descarta (`accepts`).
- **Permisos:** perder Manage deja el borrador en sólo lectura mientras siga Read (los `change*` se ignoran); perder Read
  retira el borrador. `canSave = owned && canManage && !busy && !requiresReview`.
- **Estado:** `draft`, `baseline` (nombre, descripción, filtros, activo, campos, presentación), `busy`, `requiresReview`,
  `current` (lectura para revisar), `confirmed`, `saved`, `error`.
- **`dirty`:** compara estructuralmente metadatos, campos (por `catalogFieldsBody`), presentación y filtros, y suma cambios de
  opciones, movimientos y orden A–Z.
- **`changeFields`:** copia los campos; un campo activo que se quita o se descarta sale también de la presentación
  (`withoutPresentationFields`); mientras la presentación sólo haya seguido esas bajas, se recalcula desde el baseline, así
  restaurar el campo lo devuelve.
- **`save`:** no escribe si hay valores tipados o de campos inválidos en opciones cambiadas. Hace POST (alta) o PUT con
  `expectedRevision`. Al confirmar, reinicia el baseline con el catálogo guardado (`fresh`) e invalida `["form-catalogs",
  actorId]`. Un error: si es 409 `Forms.Catalog.RevisionConflict` en una escritura sobre un catálogo existente, pasa a
  revisión; cualquier otro error deja el borrador editable. Red o 5xx permiten otro Guardar con la misma revisión; un alta
  fallida sin id permite reintentar sin ofrecer releer.
- **Revisión:** `readCurrent` hace GET del detalle sin tocar el borrador; `adoptCurrent` reinicia el baseline con esa lectura
  (descarta todo lo no guardado, incluidas opciones pendientes).
- **`observeCatalog`:** adopta un catálogo confirmado por la importación sólo si el borrador está limpio y la revisión no
  retrocede.
- Nada de esto pasa por MutationCache, URL ni almacenamiento; no hay reintentos automáticos de escritura.

**Diálogo de revisión** (en la página): «Revisión del catálogo», «Tu borrador se conserva al releer. Usar la revisión actual
descarta los cambios sin guardar, incluidas las opciones pendientes.», dos superficies «Tu borrador» y «La revisión actual»
(«Releé para comparar con la revisión actual.» hasta releer) y el pie «Cerrar», «Releer» y «Usar la revisión actual».
**Compara sólo nombre y descripción** en es y en: no muestra diferencias de campos, presentación ni opciones (observación
de este relevamiento; conviene que el multitenant muestre al menos qué secciones cambiaron).

**Lecturas** (`src/features/business-forms/hooks/useCatalogQueries.ts`):

| Hook | Clave | Notas |
| --- | --- | --- |
| `useCatalogDetail` | `["form-catalogs", actorId, permisosOrdenados, cultura, "detail", id]` | `staleTime: 0`, `gcTime: 0`, error local |
| `useCatalogItems` | `[…scope, "items", id, revisión, query]` | Sin revisión no consulta; la query incluye `ids` en el modo «filas con error» |
| `useCatalogItemsById` (`7080141`) | `[…scope, "items-by-id", id, itemIds]` | Lee opciones por identidad, sin revisión en la clave e incluidas las inactivas; error global. Lo usa la pregunta (sección 10) |

Todas limpian lo no observado (`useUnobservedQueryCleanup`). Las celdas de catálogo usan `useCatalogDetail` +
`useCatalogItems` de la **fuente** con error global (un solo aviso en QueryCache).

**Lógica pura:** `catalogDraft.ts` (borrador, `savedFields`, `editedFieldIds`, `catalogItemFieldIssues`, cuerpos de alta y
PUT), `catalogFields.ts` (definición, `parseCatalogFieldValue` exacto sin `Number`, `catalogFieldProblemText`,
`unconvertedFieldValue`, `catalogFieldsBody`), `catalogItemEdits.ts` (`withCatalogFieldValue`), `catalogPresentation.ts` y
`validationPaths.ts` (segmentos de propiedad e índice, sin fijar nombres de campos; los errores se ligan a los `localId` del
cuerpo enviado).

### 9.10 Clientes HTTP y guards zod

**Clientes** (`src/features/business-forms/api/catalogs.ts`):

- `POST /api/forms/catalogs`: alta Manual con `valueType`, `name`, `description`, `fields`, `presentation` e `items`. El
  recibo exige `itemIdentityMap` exacto para las opciones nuevas y `fieldIdentityMap` exacto para los campos nuevos (cada id
  tiene que estar en `catalog.fields`).
- `PUT /api/forms/catalogs/{id}`: `name`, `description`, `fields` (lista completa, con `position` de 1 en adelante),
  `presentation`, `confirmFieldRemovals`, `items` (sólo las cambiadas; `items[i].fields` es un **delta**: clave ausente
  conserva, `null` limpia, y se omite si no queda ninguna), `expectedRevision`, `isActive`, y `moves` o `sortAlphabetically`.
  El recibo exige revisión mayor.
- `GET /api/forms/catalogs/{id}` y `GET /api/forms/catalogs/{id}/items?page=&pageSize=&search=&sort=&isActive=&ids=…` (cada
  `ids` repetido, no separado por comas).
- Importar: `POST …/{id}/import-previews` y `POST …/{id}/imports` (FormData, `no-store`); exportar: `GET
  …/{id}/export?template=`.

**Guards** (`src/features/business-forms/api/catalogResponse.ts`, todo `z.strictObject` y un `read` que lanza
`UnsupportedCatalogResponse` sin conservar los issues de zod, porque pueden traer contenido privado):

- **Campo:** `id` GUID no vacío, `key` `^[a-z][a-z0-9_]{0,39}$`, `name` `{ es (1–120), en? (1–120) }`, `type` de los seis,
  `sourceCatalogId` sólo y siempre en `catalog`, `required`, `unique`, `min`/`max` string o null, `maxLength` 1–300 o null,
  `role` filter/data, `position` entero de 0 a int.MaxValue (`9807676`: no es número de fila ni se completa), `isActive`,
  `hasValues`, `usedBy`.
- **Uso (`catalogFieldUsageSchema`):** `{ definitionId, formName, question, stage: Draft|Active|Catalog }`; `Catalog` exige
  `definitionId` y `question` null; los otros, no null. Lo comparten `usedBy` y los `uses` del 409.
- **Catálogo:** `fields` (hasta 20, ids y claves únicos) y `presentationCheck` obligatorios en Manual; `presentation` `{ title
  (1–24 partes), description (0–24) }` o null; `presentationCheck` `{ titleTooLongCount, examples (hasta 3 {itemId, title}) }`.
  Un Manual sin propuesta exige nombre y descripción en es **y** en.
- **Opción:** `fields` (mapa GUID → string ≤300 o null) y `fieldTexts` (mapa GUID → textos con es obligatorio).
- **Metadata de fallo** (`src/features/business-forms/api/catalogFieldFeedback.ts`, `readCatalogFieldFeedback`): copia sólo
  `missingCount`, `invalidCount`, `convertedCount`, `count`, `invalidRows` (hasta 50: `itemId`, `code`, `row` desde 1,
  `value` ≤300 o null, `reason` required/invalid/range/length) y `uses` (hasta 100). Si no valida, devuelve null y la página
  sólo muestra el error de campo.
- **Importación:** `added[].fields`/`fieldTexts` y `changed[].changes[].beforeText`/`afterText` opcionales.

### 9.11 Cohortes y fixtures exportadas

| Familia (`httpCohort`) | Carpeta | Variable de entrada | Productor del back | Cuerpos | Consumidor |
| --- | --- | --- | --- | --- | --- |
| `manualCatalogs` | `src/test/fixtures/catalogs-http/manual-current` | `FORMS_CATALOG_HTTP_CONTRACT_INPUT` | `FormCatalogEndpointsTests.cs`, `FormCatalogEndpointsTests.Fields.cs`, `FormCatalogValueHttpTests.cs` | 41 capturas + `provenance.json` (run `20261023-064235-5542…`, back `7e5b7a94`, renovada en `e4db46c`) | `src/features/business-forms/api/catalogCurrentHttpContract.test.ts` |
| `catalogImports` | `src/test/fixtures/catalogs-http/imports/{es,en}` | `FORM_CATALOG_IMPORT_HTTP_INPUT` | (sección 8) | suma `extra-source-preview`, `extra-source-applied` y `extra-source-unchanged` por idioma | `src/features/business-forms/api/catalogImportsHttpContract.test.ts` (`96965e4`) |

Capturas de campos en `manual-current`: `fields-create.request`, `fields-update.request`, `fields-created`,
`fields-values-saved`, `fields-detail`, `fields-items`, `fields-removed`, `fields-presentation` (con `titleTooLongCount` 1),
`fields-source-items` (etiqueta fuente), `fields-required-error` (`FieldValuesMissing`, `missingCount`),
`fields-conversion-error` (`FieldTypeNotConvertible`), `fields-unique-error` (`FieldValueDuplicate`),
`fields-removal-confirmation` (`FieldRemovalNotConfirmed`, `count`) y `fields-source-in-use` (409 `SourceInUse`). El
consumidor verifica bytes y SHA256 del manifiesto, lee los recibos y el detalle con los guards reales y pasa los rechazos
intactos por el cliente Manual.

**Desfase detectado por lectura (no ejecutado):** la captura commiteada `fields-conversion-error.json` (470 bytes, de la
corrida de `e4db46c`) trae `invalidRows: [{ itemId, code }]`, sin `row`, `value` ni `reason`; el guard de `ff28cbc` los exige
y el test «reads the row, attempted value and reason of a type change that does not convert» espera `row: 1`, `value: "1"`,
`reason: "invalid"`. Con estos fixtures ese test no puede pasar: hace falta una nueva exportación conjunta del back (que ya
devuelve fila, valor y motivo en `FormCatalogFieldPolicy.Items.cs`).

> **No copiar — review E2, punto 33** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)): no hay en las
> cohortes un 409 `FieldInUse` ni usos `Draft` y `Active` consumidos por el front (la §6.2 del brief lo pedía). Estado en
> `982ade8`: pendiente; `manual-current` no tiene ninguna captura de `FieldInUse`.

La cohorte histórica `catalogs-http/manual` (anterior a K4) sigue inmutable y su consumidor rechaza la ausencia de los campos
nuevos. Los recorridos visuales usan un transporte sintético explícito (`src/test/visual/catalogExtraFieldsFixtures.ts`:
«Proveedores» con CUIT, Zona de «Zonas», Plazo de pago (días) de 0 a 180 y Usa GPS; responde 405 a toda escritura y registra
`writes` para comprobar que no se escribe).

### 9.12 i18n

Namespace `forms` (es y en en paridad, `src/locales/parity.test.ts`). Grupos nuevos o ampliados:

- `catalogs.fields.*`: sección, columnas, tipos (`types.*`), papeles (`roles.*`, `roleHint.*`), ayudas (`keyHint`,
  `optionalFirst`, `typeReview`), quitar (`removeTitle`, `remove`, `removeHint`, `confirmRemoval`, `inUse`), restaurar
  (`restoreField`), conteos del servidor (`missingValues_*`, `invalidValues_*`, `convertedValues_*`), motivos genéricos
  (`invalid`, `required`, `range`, `length`), motivos con valor (`problem.*`, con `invalid.{tipo}`, `range` + `rule.between|from|to`,
  `length_*`), «Cambiar el tipo» (`conversion.*`, con `number(useGrouping: always)` en los conteos), celdas (`valueLabel`
  «Fila {{row}} · {{field}}», `savedFirst`, `clear`, `clearValue`).
- `catalogs.presentation.*`: «En WhatsApp», «Título · hasta 30 caracteres», «Descripción · hasta 300», «Agregar campo», «Por
  ejemplo», `tooLong_*`, `hidden`, `hint`, `remove`, `yes`/`no`, `unavailable` («Campo no disponible»).
- `catalogs.items.errorRows_*` («Con error · N filas» / «With errors · N rows») y `clearErrorRows` («Quitar el filtro»).

Textos en inglés notables: «Change the type of “{{name}}”»,
«Not shared outside the system: {{fields}}.», «Save to enter values», «Yes, remove the field and its values», «Name in
English». Los textos de contenido del teléfono de la vista previa se piden en el idioma de textos elegido
(`useTranslation("forms", { lng: culture })`).

### 9.13 Pruebas

**Unitarias (Vitest)**, en `src/features/business-forms/`:

| Archivo | Qué cubre |
| --- | --- |
| `components/CatalogFieldsSection.test.tsx` | Campo nuevo opcional y columna apagada hasta guardar; clave inmutable y casilla para quitar valores; usos que impiden quitar; quitar uno usado sólo por la presentación propia; abrir el campo con error y mostrar faltantes; sólo lectura; restaurar un quitado rechazado sin perder otros cambios |
| `components/CatalogFieldConversionDialog.test.tsx` | Fila, valor y motivo con «Y N más»; como mucho 5 filas y sin tipos si sólo cambió la fuente; inglés; separador de miles |
| `components/CatalogExtraFieldInput.test.tsx` | Campo sin guardar apagado; entrada tipada inválida conservada sin coerción numérica; limpiar un booleano opcional sin convertirlo en false; sin etiqueta tras vaciar la fuente |
| `components/CatalogExtraColumns.test.tsx` | Columnas por identidad y ocultar sin cambiar valores; valor guardado que todavía no es sí/no, tal cual |
| `components/CatalogPresentationSection.test.tsx` | Agregar datos a la descripción; campos sin guardar no elegibles |
| `catalogFields.test.ts`, `catalogFieldsDraft.test.ts`, `hooks/useCatalogFieldsDraft.test.tsx` | Definición y cultura exacta; nombre sin traducciones en blanco; motivo por tipo; deltas canónicos; obligatorio sólo en activas y en lo no editado sólo si lo piden las dos; sólo los valores editados; campos y presentación como cambios privados que se limpian al cambiar de dueño |
| `catalogPresentation.test.ts` | Quitar campos de la presentación con vuelta al texto; composición con « · », etiquetas fuente, fechas numéricas y booleanos traducidos |
| `api/catalogFieldFeedback.test.ts`, `api/catalogFieldsContract.test.ts` | Metadata acotada (50 filas, usos estrictos); guard de campos, usos, presentación, mapas de valores y `fieldIdentityMap` exacto; referencia de la presentación propia |
| `hooks/useCatalogDraftEditor.test.tsx` | Un solo commit sin MutationCache; revisión sólo con `RevisionConflict` y editable ante un 409 de negocio; quitar y restaurar con la presentación; concurrencia y épocas; pérdida de permisos; `confirmed` tras guardar, importar o adoptar |
| `pages/CatalogEditorPage.test.tsx` | Opción inactiva sin obligatorio que se guarda; el servidor revisa el cambio de tipo y el obligatorio; no manda valores reformateados; valor que todavía no es opción; «Cambiar el tipo» y filtro de filas; marca a 390 px; la última opción no baja; buscar, volver al tipo guardado o guardar quitan el filtro; `SourceInUse` sin revisión; usos de `FieldInUse` y restaurar; importar con los campos guardados en la sesión |
| `catalogItemEdits.test.ts`, `catalogColumns.test.ts`, `components/CatalogImportDialog.test.tsx`, `api/catalogImports.test.ts`, `hooks/useCatalogQueries.test.tsx`, `src/shared/ui/WideTable.test.tsx` | Etiqueta que se va con el valor; columnas compartidas; revisión de importación con campos; `ids` repetidos y lectura por id; tabla ancha accesible |

**Contratos:** `api/catalogCurrentHttpContract.test.ts` y `api/catalogImportsHttpContract.test.ts` (9.11).

**Visuales (Playwright):** `src/test/visual/catalogExtraFields.spec.ts`, 12 recorridos (es/en × 1440/390/320 × 2):

- «CE1 fields and an optional unsaved column»: «Editar campos», «Agregar campo» con el foco en Nombre, «Obligatorio»
  deshabilitado, contención del ancho, «Listo» y la celda del campo nuevo deshabilitada (en el celular, desde el editor de la
  opción); sin escrituras.
- «CE2 typed values columns and contained scrolling»: región «Opciones» desplazable dentro del ancho, menú «Campos» que oculta
  y vuelve a mostrar CUIT con el foco de vuelta en el menú, valor «treinta» inválido que deshabilita Guardar y «45» que lo
  habilita; sin escrituras.

No comparan contra referencias (`toHaveScreenshot`): escriben capturas en la salida del test (`ce1-new-optional-field.png`,
`ce1-column-before-save.png`, `ce2-invalid-typed-value.png`, `ce2-table-contained.png`) y verifican geometría, foco y estado.
El [cierre local de E2](../../../../ArquitecturaBaseFront/docs/design/catalogos-campos-extra.md#cierre-local-de-e2--2026-10-23)
registra la corrida visual 719/719 sobre `e02d1b4` (antes de los arreglos del review). Los arreglos posteriores tienen sus
tests unitarios; **no hay recorrido visual del diálogo «Cambiar el tipo» ni del filtro de filas con error** (verificado: no
aparecen en `src/test/visual/`).

### 9.14 Para el multitenant

- **Todo es por organización:** los campos, la fuente de un campo «Elegir de un catálogo» (tiene que ser un catálogo Manual
  activo de la misma organización y distinto del propio; hoy el selector no excluye al propio y lo frena el servidor), los
  usos «En uso» (formularios de la misma organización) y los conteos. La clave de caché (`catalogScopeKey`: actor,
  permisos, cultura) tiene que sumar la organización.
- **Topes que vienen de Meta o del diseño:** 20 campos, título 30, descripción 300, 24 partes, 50 filas inválidas en la
  respuesta, 10.000 opciones. Conviene mantenerlos.
- **Idioma:** no copiar el par fijo `{ es, en? }` ni el selector «Textos en»; modelar idioma original por catálogo (o por
  organización) y traducción opcional, como pide el punto 35. Lo mismo para el nombre de los campos y el texto de las
  opciones.
- **Contrato en movimiento:** el back está sumando `questionId` a los usos (`84289eef`); el guard estricto del front de
  `982ade8` no lo acepta. Definir el contrato de usos con etapa e identidad de la pregunta desde el principio (punto 25).
- **No copiar** la validación local de cambios de tipo (punto 4), el 409 genérico como revisión (punto 2), la revisión que
  compara sólo metadatos, el aviso de títulos largos calculado sobre la página visible cuando hay cambios, ni los textos
  junto a botones (puntos 36–38).
- **B2C:** el editor de catálogos es de administración; no hay nada de cara al cliente final en esta parte. Lo que el cliente
  ve es la presentación en WhatsApp (título y descripción), que sale de acá: cualquier campo de papel «dato» que se elija en la
  descripción viaja a Meta.
- **Rendimiento (back, «Después, sin bloquear» del review):** cada guardado reescribe las 10.000 opciones (punto 10) y el
  detalle carga todas para calcular `hasValues`; conviene diseñarlo distinto desde el inicio.

### 9.15 Endpoints que consume esta parte (nuevos o cambiados desde `9261e3d`)

- `GET /api/forms/catalogs/{id}`: suma `fields`, `presentation` y `presentationCheck` (y `valueType` de E1).
- `GET /api/forms/catalogs/{id}/items?…&ids={id}&ids={id}`: suma `fields` y `fieldTexts` por opción y el filtro por `ids`.
- `POST /api/forms/catalogs` y `PUT /api/forms/catalogs/{id}`: suman `fields`, `presentation`, `confirmFieldRemovals` y el
  delta `items[i].fields`; el recibo suma `fieldIdentityMap`. Errores nuevos: `Forms.Catalog.FieldValuesMissing`,
  `FieldTypeNotConvertible`, `FieldValueDuplicate`, `FieldRemovalNotConfirmed` (400) y `FieldInUse`, `SourceInUse` (409).
- `POST /api/forms/catalogs/{id}/import-previews` y `POST /api/forms/catalogs/{id}/imports`: `added[].fields`/`fieldTexts` y
  `changed[].changes[].beforeText`/`afterText`.

### 9.16 Huecos

- No se ejecutaron builds ni tests: la falla del consumidor de `fields-conversion-error.json` (9.11) es una deducción por
  lectura.
- La geometría a 390 y 320 px sale del código responsive y de las aserciones de los recorridos; no se miraron capturas de la
  implementación (no están commiteadas).
- El recorte del desplegable dentro de la tabla (punto 17) sigue siendo plausible, no verificado en navegador.
- El cambio de contrato de usos del back (`84289eef`) se leyó en el repo del back después de `982ade8`; no se sabe en qué
  commit del front se adopta.

---

## 10. Catálogos E2 (2): datos de las personas, filtros y vista previa en la pregunta, y subcolumnas en las respuestas

Relevado en el commit `982ade8` del front (2026-10-23). Cubre los tableros **CE4-A** (datos de las personas y filtro
por persona), **CE3-B en la pregunta** («Como en el catálogo» / «Propio de esta pregunta») y **CE5** (las respuestas),
congelados en [catalogos-campos-extra.md](../../../../ArquitecturaBaseFront/docs/design/catalogos-campos-extra.md), con
las reglas del back en [formularios: E2](../../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23)
([datos de las personas](../../features/formularios.md#datos-de-las-personas),
[presentación y filtros por destinatario](../../features/formularios.md#presentación-y-filtros-por-destinatario--e2) y
[campos sellados en las respuestas](../../features/formularios.md#campos-sellados-en-las-respuestas)). El editor del
catálogo (sección «Campos», tabla de valores, «En WhatsApp» del catálogo, «Cambiar el tipo», importar con campos) está
en la **sección 9**; acá solo se remite a ella.

### 10.1 Resumen

**Qué cambió desde `9261e3d`.** En el anexo anterior la pregunta «Elegir de un catálogo» (K6) solo elegía un catálogo
(`source: { catalog }`), el teléfono del editor decía que las opciones se preparan al mandar, la planilla tenía una
columna por pregunta y no existían los datos de las personas. Ahora:

1. **Datos de las personas (CE4-A).** Pantalla nueva `/datos-de-personas` en Administración, con las definiciones de
   datos (Zona, Sucursal…: «Elegir de un catálogo» o «Texto») y la tabla de personas con sus valores. Los valores se
   cargan en una sección «Datos» nueva de «Editar usuario», que **se guarda aparte del usuario** y la aporta el módulo de
   formularios por un contrato neutral (el núcleo de usuarios no importa formularios).
2. **Filtros en la pregunta (CE4-A).** La pregunta de catálogo suma la superficie «Qué opciones ve cada persona»:
   filtros «Según quien lo recibe» (campo de papel filtro = dato de la persona de la misma fuente) y «Filtro fijo»
   (campo = valor). Se suman (Y lógico). Solo con catálogos **Manual**.
3. **Presentación en la pregunta (CE3-B).** La misma pregunta suma «En WhatsApp» con «Como en el catálogo» (por
   defecto, `presentation` ausente) o «Propio de esta pregunta» (título y descripción armados con campos).
4. **Vista previa por persona.** «Como si lo recibiera» elige una persona de ejemplo, pide al servidor sus opciones y
   llena el teléfono del editor con ellas (o con el motivo si no se le puede mandar).
5. **Publicar separa problemas y avisos.** La comprobación de publicación muestra «Problemas · frenan» y «Avisos · no
   frenan» (máximo de opciones por persona y personas sin el dato).
6. **Chat.** El diálogo «Enviar un formulario» muestra dos bloqueos nuevos por destinatario: dato faltante y filtro no
   disponible.
7. **Respuestas (CE5).** La planilla intercala subcolumnas «Pregunta · Campo» con los campos sellados de cada opción
   (las de papel dato visibles por defecto; las de filtro, en «Columnas»); el detalle D2/DC1 muestra todos los campos
   bajo cada opción; el Excel «Solo las visibles» exporta las subcolumnas elegidas aunque la pregunta base esté oculta.

**Commits clave** (todos del 2026-10-22/23):

| Commit | Asunto |
| --- | --- |
| `3baef58` | refactor(forms): separar los ajustes de preguntas de catálogo |
| `f0ab198` | feat(formularios): validar datos de personas en el cliente |
| `8ccf786` | test(formularios): calificar los contratos de datos de personas |
| `20ab21d` | feat(users): guardar los datos opcionales aparte de la cuenta |
| `b4feb8c` | feat(forms): aislar el borrador de datos por persona |
| `fcc48eb` | feat(forms): aportar la sección de datos al editor de usuario |
| `877ef20` | feat(forms): administrar los datos y sus valores en CE4 |
| `e65af46` | feat(app): componer la pantalla y la sección opcional de datos |
| `d1c33b6` | feat(forms): conservar filtros y presentación en el borrador |
| `431d4f9` | feat(forms): validar filtros y avisos de publicación |
| `1100d5b` / `1f848b3` | feat/test(formularios): validar y consumir la vista previa por persona |
| `66588b7` | test(formularios): preparar los recorridos visuales de datos de personas |
| `a561037` | feat(forms): separar avisos y problemas al publicar |
| `7a1acc7` | feat(forms): aislar la vista previa por persona y fuente |
| `87e044f` | feat(forms): configurar filtros y presentación en la pregunta |
| `a594fc1` | feat(forms): explicar los cambios de fuente al revisar un conflicto |
| `ab31f5a` | feat(forms): mostrar las opciones de la persona en el teléfono |
| `59491cb` / `be4314b` | feat/test(whatsapp): explicar y consumir los bloqueos por datos y filtros |
| `9146caf`, `f382364`, `6d9cfec`, `565647e` | recorridos visuales de opciones por persona; acciones largas del filtro en móvil |
| `b0ac79a` / `7babce3` | feat/test(formularios): validar y consumir los campos históricos de las respuestas |
| `ae25a3a` | feat(forms): seleccionar subcolumnas de catálogo por separado |
| `a476c16` | feat(forms): mostrar los campos capturados de cada opción |
| `88ebff8` | feat(forms): exportar los campos visibles de las respuestas |
| `8658464`, `0a2c007`, `b78a97b` | evidencia visual de campos de catálogo y respuestas |
| `64e039f`, `ab82204`, `68b742e` | ajustes de pruebas a los contratos E2 y a la vista previa |
| `7080141` | feat(catalogos): pedir opciones por id (posterior al review) |
| `d72caf4` | fix(formularios): mostrar el valor fijo de un filtro de catálogo por id (review, punto 14) |
| `e133d93` | fix(formularios): filtros y presentación propia solo en catálogos Manual (review, punto 15) |

> **Estado del review.** El [review de E2](../../reviews/2026-10-23-code-review-catalogos-e2.md) se hizo sobre
> `e02d1b4`. De los puntos de esta sección, en `982ade8` están corregidos el **14** y el **15**; siguen pendientes el
> **16**, **26**, **28**, **29**, **31**, **32**, **33**, **35**, **38** y la parte de front del **7**. Cada uno se marca
> en la pantalla que afecta.

### 10.2 Navegación y rutas

| Ruta | Pantalla | Permiso de la ruta | Archivo |
| --- | --- | --- | --- |
| `/datos-de-personas` | Datos de las personas | `formpersondata.manage` | `src/features/business-forms/pages/PersonDataPage.tsx` |
| `/formularios/{formId}` y `/formularios/nuevo` | Editor del formulario: la pregunta de catálogo suma filtros, presentación y «Como si lo recibiera» | sin cambios (`formtemplates.read`; alta con manage) | `src/features/business-forms/components/CatalogQuestionSettings.tsx` |
| `/formularios/{formId}/respuestas` (+ D2 y `/completa`) | Planilla con subcolumnas y detalle con campos | sin cambios | `src/features/business-forms/pages/FormResponsesPage.tsx` |
| `/whatsapp/chat` | Diálogo de envío con los bloqueos nuevos | sin cambios | `src/features/whatsapp/components/FormCatalogPreparation.tsx` |

- **Menú.** «Datos de las personas» (`navigation.personData`, ícono de usuarios) es el tercer enlace del grupo
  «Gestión de usuarios» dentro del panel **Administración** (`src/layouts/navigation.ts`), después de Usuarios y Roles,
  con permiso `formpersondata.manage`. No está en el panel de Formularios.
- **Migas.** Por la regla general de `branchOf` serían `Inicio › Gestión de usuarios › Datos de las personas`
  (no verificado en navegador).
- **Ruta registrada siempre.** `src/app/routes.tsx` registra `/datos-de-personas` sin preguntar si el módulo está
  instalado: la barrera es el permiso, que solo existe con el módulo (back). La sección de «Editar usuario», en cambio,
  sí exige `installedFeatures` con `"forms"` (ver 10.3.3).
- **URL de `/datos-de-personas`:** `page`, `pageSize`, `sort` (`displayName` / `-displayName`, sin encabezado
  ordenable en pantalla: solo se puede cambiar por URL) y **`search`** (la búsqueda de personas), vía
  `src/shared/hooks/usePagination.ts`.
- **URL de la planilla:** `columns` ahora admite, además de FieldId de pregunta, ids de subcolumna
  `"{questionId}:{fieldId}"` (`responseCatalogColumnId` en `src/features/business-forms/responses.ts`). Ausente = la
  selección por defecto (todas las preguntas y las subcolumnas de papel dato); `none` = ninguna.
- **Nunca en la URL ni en storage:** la persona de ejemplo de «Como si lo recibiera» y su resultado, la búsqueda de
  muestras, el borrador de datos de una persona, el borrador de una definición de dato, los filtros y la presentación
  sin guardar de la pregunta (viven en el borrador del formulario, como el resto).

> **No copiar — búsqueda de personas en la URL** (review E2, «Después, sin bloquear»): `/datos-de-personas?search=…`
> deja un dato que puede identificar personas en la dirección y el historial. El review pide alinearla con la lista de
> usuarios. Estado en `982ade8`: pendiente.

### 10.3 Pantallas y diálogos

#### 10.3.1 «Datos de las personas» — CE4-A (`src/features/business-forms/pages/PersonDataPage.tsx`)

**Entrada.** Si el usuario actual no carga, spinner. Sin `formpersondata.manage`, `ForbiddenPage`. El espacio de trabajo
se remonta (`key`) al cambiar actor, idioma o permisos.

**Cabecera (`Page`):** título «Datos de las personas» (en: «People data»). Sin flecha volver, sin estado, sin acciones.

**Zona 1 — superficie «Datos»** (banda con título «DATOS»; acción a la derecha de la banda):
- Acción: **«Agregar dato»** (botón secundario con ícono +). Deshabilitado mientras la lista no cargó o cuando ya hay
  **20** datos (el tope incluye inactivos). No tiene tooltip con el motivo.
- Debajo de la banda, un párrafo fijo: «Los datos que tiene cada persona eligen de un catálogo o son un texto. Los
  valores se cargan en Editar usuario.»
- Tabla (`WideTable`, rótulo accesible «Definiciones de datos»), columnas:

| Columna | Contenido |
| --- | --- |
| Dato | Nombre en el idioma de la interfaz, con caída a **español** (`localizedValue(name, culture, "es")`); si está inactivo, segunda línea chica «Dado de baja» (reutiliza `catalogs.state.Inactive`) |
| Clave | La clave (`[a-z][a-z0-9_]{0,39}`) |
| Tipo | «Elegir de un catálogo · {nombre del catálogo fuente}» o «Texto» |
| Lo usan | Nombres de los formularios que lo usan, separados por coma, o «—» |
| Acciones | Lápiz «Editar {nombre}» (abre el diálogo) |

- Estados: cargando → spinner; error sin datos → estado vacío embebido con el mensaje (`readErrorMessage`) y
  «Reintentar»; vacío → «Todavía no hay datos definidos.»; 401/403 en esta lectura → toda la página pasa a
  `ForbiddenPage` y se cancelan y retiran las consultas privadas de ese dueño. 409 y 429 no aplican a la lectura
  (no hay espera ante 429: cae como error con «Reintentar»).

**Zona 2 — superficie «Personas»** (solo con `users.read` además de `formpersondata.manage`; si no, en su lugar un
párrafo «Para ver las personas necesitás Ver usuarios.»):
- `FilterBar` con «Buscar personas» (escribe `search` en la URL y vuelve a la página 1).
- Tabla (`WideTable`, rótulo «Personas»): «Persona» (nombre visible o «Sin nombre»), **una columna por dato activo**
  con el texto del valor o «—», y «Acciones» con el lápiz «Editar a {nombre}» solo si el módulo aportó la extensión y
  la persona tiene `users.manage` (abre «Editar usuario», ver 10.3.3). La tabla no trae correo (el back no lo
  devuelve).
- Debajo, el paginado común.
- Estados: cargando → spinner; error sin datos → mensaje y «Reintentar»; vacío → «No hay personas para mostrar.»;
  401/403 en esta lectura → se oculta solo la tabla de personas (la de definiciones sigue) y se retiran sus consultas.

**390 y 320 px.** Las dos tablas se desplazan a lo ancho **dentro de su caja** (`WideTable`); la página no se desplaza
de costado. La escena visual exige que a menos de 640 px la tabla de definiciones tenga desplazamiento propio y que la
tabla de personas tenga 7 filas (encabezado + 6). El buscador ocupa el ancho.

> **No copiar — review E2, punto 26** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)): **la tabla no
> se actualiza al cambiar el nombre** de una persona. Guardar el usuario invalida las claves de usuarios, pero la tabla
> vive bajo `["forms-person-data", …]` y solo se invalida si además cambió un dato (`usePersonData.save`). Estado en
> `982ade8`: pendiente.

> **No copiar — review E2, punto 32** (CE4-A): falta «N personas · M sin zona» arriba de la tabla de personas. Estado en
> `982ade8`: pendiente (no hay clave de texto ni cálculo).

> **No copiar — regla de idioma del 2026-10-23** (review E2, punto 35): el nombre del dato se muestra con caída fija a
> español, no al idioma original de ese dato.

#### 10.3.2 Diálogo «Agregar dato» / «Editar dato» (`src/features/business-forms/components/PersonAttributeDialog.tsx`)

Diálogo central. Título «Agregar dato» o «Editar dato»; descripción = el mismo texto de ayuda de la superficie. Al abrir,
el foco va a «Nombre»; al cerrar vuelve al disparador (`useRestoreFocusOnClose`).

Campos, de arriba hacia abajo:
1. Fila de dos columnas (una en celular): **«Nombre»** (obligatorio, hasta 120, es el español `name.es`) y **«Nombre en
   inglés»** (opcional, hasta 120). Al guardar, el inglés en blanco se omite (no viaja `en: ""`).
2. **«Clave»** (obligatoria, hasta 40) con la ayuda «La usan la plantilla, la importación y lo guardado. No cambia
   después de guardar.» (texto reutilizado de los campos del catálogo). En edición queda de solo lectura.
3. **«Tipo»**: «Elegir de un catálogo» (por defecto) o «Texto».
4. Con «Elegir de un catálogo»: con `formcatalogs.read`, el selector de catálogos común en modo `manualOnly` (solo
   Manual activos); sin ese permiso, un campo de solo lectura «Catálogo» con el nombre guardado y la ayuda «Necesitás
   permiso para leer catálogos y elegir uno.».
5. En edición: casilla «Dato activo».
6. Si lo usa algún formulario: «Se usa en estos formularios. Mientras se use, no se puede cambiar su tipo o catálogo
   ni darlo de baja.» y la lista de nombres. En ese caso Tipo, Catálogo y Activo quedan deshabilitados.

**Pie:** «Cancelar» y «Guardar». Guardar está deshabilitado mientras guarda, sin Nombre, sin Clave o sin catálogo con
tipo «Elegir de un catálogo». Mientras guarda, el diálogo no se cierra.

**Guardar.** Alta: `POST /api/forms/person-attributes` con `{key, name, type, sourceCatalogId}`; edición:
`PUT /api/forms/person-attributes/{id}` con `{expectedRevision, name, type, sourceCatalogId, position, isActive}`. Al
terminar invalida todo el dueño (`personDataScopeKey`) y cierra. Errores: los de campo (`name.es`, `name.en`, `key`,
`type`, `sourceCatalogId`, `isActive`) van bajo su control; los demás, como un párrafo `role="alert"` dentro del
diálogo con el `detail` del servidor (o «No pudimos confirmar el guardado. Volvé a guardar. Referencia: …» ante
red/5xx). Sin `formpersondata.manage`
el guardado no sale.

Estados: no hay carga propia; 403 → mensaje del servidor en el diálogo (no cierra ni redirige); 409
`Forms.PersonAttribute.RevisionConflict` → el texto del back «Otra persona cambió el dato. Volvé a cargarlo.»; 409
`InUse` / `SourceUnavailable` → su texto; 429 → sin espera dedicada (no verificado).

> **No copiar — review E2, punto 26:** un 409 de revisión dice «Volvé a cargarlo», pero el diálogo **no ofrece cómo**
> (no hay «Releer» ni se refresca la fila). Estado en `982ade8`: pendiente.

> **No copiar — review E2, punto 7 (parte del front):** desde el back `95e200c8` (2026-10-23), si alguna persona tiene
> el dato cargado, cambiar el tipo o la fuente devuelve 400 `Forms.PersonAttribute.ValueRemovalNotConfirmed` (texto
> «Confirmá que querés cambiar el dato y borrar los valores que tienen cargados las personas.», en `type` o
> `sourceCatalogId`) hasta que el PUT lleve `confirmValueRemoval: true`. El front en `982ade8` **no tiene** ese campo en
> `UpdatePersonAttributeBody` ni una casilla de confirmación: el error aparece bajo «Tipo» o «Catálogo» y no hay forma
> de confirmarlo. Pendiente; la referencia de diseño es la casilla «Sí, quitar el campo y sus N valores» de CE1.

> **No copiar — regla de idioma del 2026-10-23** (review E2, punto 35): el par «Nombre» / «Nombre en inglés» se
> reemplaza por **un solo campo en el idioma del menú**, que es el original; la traducción se carga cambiando el idioma
> del menú. Hoy el guard exige `name.es` (`src/features/business-forms/api/personData.ts`), el botón exige `name.es` y la
> lectura cae a `es`. Pendiente.

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), commit `bb98bf2`):
> Guardar se deshabilita por campos obligatorios vacíos **antes** de intentar guardar y sin decir por qué; la regla pide
> validar al intentar guardar y marcar los campos. A revisar al rehacerlo.

> **Para revisar (no está en el review):** «Lo usan» y la lista «Se usa en estos formularios» muestran nombres de
> formularios a quien tiene `formpersondata.manage` aunque no tenga `formtemplates.read` (lo decide el back; el punto 11
> del review describe el mismo problema para los campos del catálogo). La clave reutiliza una ayuda que habla de
> «plantilla» e «importación», que no existen para los datos de las personas.

#### 10.3.3 Sección «Datos» en «Editar usuario» (`src/features/business-forms/components/PersonDataSection.tsx`, `src/app/UserDataSectionShell.tsx`, `src/features/users/components/UserEditDialog.tsx`)

**Cómo se compone (punto de extensión).**
- `src/shared/hooks/userDataSectionContext.ts` declara el contrato neutral: `UserDataSectionExtension { Section,
  openUser(userId) }`, `UserDataSectionProps { userId, disabled, userSaved, onState }` y `UserDataSaveState { dirty,
  busy, ready, save(): Promise<boolean> }`.
- `src/app/UserDataSectionShell.tsx` envuelve el layout autenticado (`routes.tsx`: `UserDataSectionShell >
  FormOverviewShell > AppLayout`) y publica la extensión **solo si** `presentation.installedFeatures` incluye `"forms"`
  y el usuario tiene `users.manage`. La sección se carga perezosa (`lazy`) desde `business-forms`.
  `openUser(userId)` (lo usa «Editar a {nombre}» de la tabla) además exige `users.read`: pide el usuario
  (`fetchUser`, clave `userQueryKey`) y abre el `UserEditDialog` real. El dueño del destino es actor + idioma +
  permisos: si cambian, el diálogo abierto se descarta.
- `UserEditDialog` (feature `users`) no importa formularios: si hay extensión, monta `<extension.Section>` **debajo de
  Roles** y la consulta por `onState`.

**Qué se ve.** Una superficie con título «Datos» (nivel 3, con relleno) dentro del cuerpo del diálogo, con un campo por
**dato activo** en grilla de dos columnas (una en celular):
- Dato de tipo catálogo: autocompletado con el nombre del dato como rótulo; busca opciones con 300 ms de espera en
  `GET /api/forms/person-attributes/{id}/options` (20 por página, **sin** `formcatalogs.read`); si hay más, el pie de
  la lista dice «Escribí para buscar más opciones.». Vaciar el texto limpia el valor. Si la lectura de opciones falla,
  el control se deshabilita y debajo aparecen el mensaje y «Reintentar».
- Dato de texto: campo de hasta 300 caracteres; vacío = null.
- Mientras carga: spinner en la superficie. Si no hay datos activos, la sección **no se dibuja**. Sin `users.manage` o
  ante 401/403 en cualquiera de sus lecturas o en el guardado, la sección **desaparece** y retira sus consultas.

**Guardar (un solo botón, el del diálogo).**
1. El botón «Guardar» del diálogo queda deshabilitado mientras la sección no está lista o está guardando (sin
   explicación).
2. Primero va `PUT /api/users/{id}` (la cuenta). Si responde bien y la sección tiene cambios, recién entonces
   `PUT /api/forms/person-data/{userId}` con `{expectedRevision, values}`, donde `values` es **solo el delta** (los datos
   cambiados; null limpia).
3. Si los dos salen bien: aviso de éxito del usuario y cierre. Si la cuenta salió pero los datos no: el diálogo
   **queda abierto**, sin aviso de éxito, y la sección muestra «No se pudieron guardar los datos. Probá de nuevo. El
   resto del usuario ya se guardó.» con el botón «Volver a cargar los datos». Volver a tocar Guardar repite el PUT de la
   cuenta y después el de los datos.
4. 400 `Forms.PersonData.ValueInvalid` → el error queda bajo el control (`values.{attributeId}`).
5. 409 `Forms.PersonData.RevisionConflict` → «Los datos cambiaron. Volvé a cargarlos para revisar.» + «Volver a cargar
   los datos»; el borrador se conserva y Guardar queda deshabilitado hasta recargar. «Volver a cargar los datos» abre
   una confirmación («Se reemplazan tus cambios en Datos por lo guardado ahora. El resto del usuario se conserva.»)
   y relee datos y definiciones.
6. Red/5xx → mismo mensaje de fallo; se puede volver a guardar con la misma revisión.

**390 y 320 px.** La escena visual comprueba a 390 y 320 que, tras el fallo parcial, el diálogo sigue abierto, el
valor elegido se conserva y «Volver a cargar los datos» queda dentro del diálogo y alcanzable con foco; Escape cierra y
devuelve el foco a «Editar a Juan Gómez». Sin desplazamiento horizontal de la página.

> **No copiar — review E2, punto 26:** al abrir una persona desde la tabla, la **carga y el error del usuario se
> dibujan fuera de la pantalla**: `UserDataSectionShell` pone el spinner o el estado vacío «No pudimos cargar los datos» con
> «Cerrar» como hermano del layout, no en un diálogo; según el review, con un 403 no se ve nada (no verificado en
> navegador). Estado en `982ade8`: pendiente.

#### 10.3.4 La pregunta «Elegir de un catálogo» con E2 (`src/features/business-forms/components/CatalogQuestionSettings.tsx`)

`3baef58` separó la configuración de la pregunta de catálogo en `CatalogQuestionSettings`, que compone, en este orden
dentro de la fila expandida de la pregunta (`QuestionEditor`):

1. **Selector de catálogo** (`CatalogQuestionSelector`, igual que K6; ahora recibe el detalle ya leído para no
   pedirlo dos veces).
2. **Multiplicidad**: la casilla «Permitir elegir varias» y, con varias, mínimo y máximo (E1; ver sección 8). Se movió
   adentro de este bloque.
3. **Superficie «Qué opciones ve cada persona»** (filtros + «Como si lo recibiera»).
4. **Superficie «En WhatsApp»** (presentación).

Las superficies 3 y 4 aparecen **solo** con `formcatalogs.read`, un catálogo elegido y **catálogo Manual**. Con un
catálogo de Filtros quedan el selector y la multiplicidad (`e133d93`). Mientras se lee el detalle del catálogo, spinner;
si falla, «No se pudo leer la configuración del catálogo.» y «Reintentar».

> **No copiar — review E2, punto 15** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)): se ofrecían
> filtros y «Propio de esta pregunta» con catálogos de Filtros; se guardaba bien y publicar frenaba con `MatchInvalid`.
> Estado en `982ade8`: **corregido en `e133d93`** (el front ya no los ofrece; el back sigue rechazándolos).

> **Para revisar (review, «Después, sin bloquear»):** sin `formcatalogs.read`, quien diseña formularios **no ve** los
> filtros ni la presentación guardados de una pregunta (las superficies no se dibujan). Si hace falta, el review
> propone un resumen de solo lectura.

##### Superficie «Qué opciones ve cada persona» (`src/features/business-forms/components/CatalogQuestionFilters.tsx`)

- Arriba, ayuda fija: «Los filtros se suman y usan los campos de papel «filtro». Un dato de la persona debe elegir del
  mismo catálogo.»
- Una **condición por campo** (no se repite campo), cada una en un `fieldset` con rótulo «Condición N» y leyenda
  «Según quien lo recibe» o «Filtro fijo». Grilla de tres columnas en escritorio (`sm:`), apilada en celular:
  - **«Campo»**: desplegable con los campos activos de papel filtro aún no usados (en una condición por persona, solo
    los de tipo catálogo con algún dato compatible). Si el campo guardado ya no existe: «Campo no disponible».
  - Por persona: **«Dato de la persona»**: «Elegí una opción» + los datos activos de tipo catálogo **con la misma
    fuente** que el campo. Un dato guardado que ya no es compatible se conserva con su nombre o «Campo no disponible».
  - Fijo: **«Valor fijo»** con el control del tipo del campo (`CatalogExtraFieldInput`: texto, número, fecha, sí/no o
    buscador de opciones de la fuente). Un valor que no se puede interpretar marca «Revisá el valor y el tipo del
    campo.» y viaja como null. La etiqueta de un valor de catálogo guardado se **lee por id**
    (`useCatalogItemsById`, `GET …/catalogs/{id}/items?ids=…`), aunque la opción esté fuera de las primeras 20 o
    inactiva.
  - Botón «×» con nombre accesible «Quitar condición N» (oculto en solo lectura).
- Debajo, dos botones secundarios que envuelven el texto en pantallas angostas (`size="sm-wrap"`, `565647e`):
  **«Agregar filtro según quien lo recibe»** (deshabilitado si no queda campo con dato compatible) y **«Agregar filtro
  fijo»** (deshabilitado si no quedan campos de filtro).
- Si el catálogo no tiene campos de filtro: «Este catálogo no tiene campos de filtro disponibles.»
- Si falla la lectura de los datos de las personas: «No se pudieron leer los datos de las personas.» (alerta).
- Los datos de las personas se leen (`GET /api/forms/person-attributes`) solo si el catálogo tiene algún campo de
  filtro activo, y con `users.manage`, `formpersondata.manage` o `formtemplates.manage`.
- Los errores del servidor se ligan por ruta: `match[i].fieldId`, `match[i].personAttributeId`, `match[i].value`, o
  `match[i]` / `match` como respaldo (las rutas `…source.…` del guardado se pasan desde `QuestionEditor`).
- Cada cambio normaliza la fuente (`normalizeCatalogQuestionSource`: filtros ordenados por FieldId, `[]` y
  `presentation` null se omiten). Editar filtros o presentación **no** renueva la identidad de la pregunta.

> **No copiar — review E2, punto 14:** el valor de un filtro fijo sobre un campo de catálogo se buscaba entre las
> primeras 20 opciones: «Zona es Sur» en un catálogo de 72 se veía vacía al reabrir, en solo lectura y en la revisión de
> un conflicto. Estado en `982ade8`: **corregido en `d72caf4`** (lectura por id con `7080141`, también en
> `CatalogQuestionSourceSummary`).

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), commit `bb98bf2`):
> los dos «Agregar filtro…» se apagan sin tooltip con el motivo, y «Este catálogo no tiene campos de filtro
> disponibles.» es un texto que explica botones apagados. La regla pide el motivo como tooltip y nada al lado.

> **No copiar — review E2, punto 32** (CE4-A): en el editor de la pregunta faltan «Como mucho N por persona» y «Todos
> ven las mismas N». Estado en `982ade8`: pendiente (solo existen en la comprobación de publicar).

##### «Como si lo recibiera» (misma superficie; `src/features/business-forms/hooks/useRecipientPreview.ts`)

- Autocompletado **«Como si lo recibiera»**: busca personas de ejemplo con 300 ms de espera
  (`GET /api/forms/recipient-samples?page=1&pageSize=20&search=`). Deshabilitado si el formulario nunca se guardó (con
  la ayuda «Guardá el formulario para probar con una persona.»), si falta `formtemplates.read` + `formtemplates.manage`
  o si algún filtro está incompleto (sin texto en este último caso).
- Al elegir una persona: `POST /api/forms/definitions/{id}/recipient-previews` con `{type, source, sampleUserId,
  culture}`. **No guarda el borrador**: prueba la fuente que está en pantalla, normalizada.
- Debajo, el resultado:
  - cargando: spinner + «Preparando las opciones de esta persona…»;
  - `Ready`: «14 opciones» (plural `_one`/`_other`), una línea por dato de la persona usado («Zona: Centro», «—» si
    vacío) y, si supera el límite, «Supera el límite de 20 opciones de esta pregunta.» (200 para una opción);
  - `RecipientDataMissing`: «La persona no tiene el dato necesario; no se le puede mandar el formulario.»;
  - `FilterUnavailable`: «El filtro no está disponible; no se puede preparar el formulario.»;
  - 401/403: «Ya no tenés permiso para probar con personas.» y la prueba queda apagada para ese dueño;
  - otro error (incluido 429) o fallo de las muestras: «No se pudo preparar la vista previa.».
- Cambiar de persona, de fuente (filtros o presentación), de idioma o de permisos aborta el pedido en curso y retira el
  resultado anterior al instante; un resultado tardío de otra persona se ignora.

> **No copiar — review E2, punto 28:** «Como si lo recibiera» **no se puede reintentar** (el mensaje de fallo no trae
> botón y repetir la misma persona no vuelve a pedir) y **no espera ante un 429** (no lee `retryAfter`). Estado en
> `982ade8`: pendiente.

> **No copiar — regla de interfaz del 2026-10-23** (review E2, punto 38): «Guardá el formulario para probar con una
> persona.» es un texto que explica un control deshabilitado. Va como tooltip. Estado en `982ade8`: pendiente.

##### Superficie «En WhatsApp» de la pregunta (`src/features/business-forms/components/CatalogPresentationSection.tsx`, embebida)

- Tarjetas de radio **«Qué viaja a WhatsApp»**:
  - «Como en el catálogo» — «Cambia si cambia el catálogo.» (por defecto; `presentation` ausente);
  - «Propio de esta pregunta» — «Otros campos, solo para este formulario.»: copia la presentación del catálogo (o
    texto + detalle) como punto de partida.
- Debajo, el mismo editor que la sección del catálogo (sección 9): «Título · hasta 30 caracteres» y «Descripción · hasta
  300», cada uno con insignias quitables («Quitar {campo} de {parte}») y un desplegable «Agregar campo» con los fijos
  (Texto, Código, Detalle y, si el catálogo no es de texto, Valor) y los campos extra activos; «Por ejemplo» con la
  primera opción activa armada; «N opciones superan los 30 caracteres. Podés guardar; revisá el título antes de
  publicar.» con hasta 3 ejemplos; «No salen del sistema: {campos}.»; y la nota «Un cambio vale para los envíos nuevos:
  lo ya enviado y las respuestas guardadas conservan lo que se mostró.». Con «Como en el catálogo» todo queda de solo
  lectura y muestra la presentación del catálogo.
- El aviso de títulos largos toma, en este orden: el chequeo de la persona probada (servidor, hasta 201 opciones); si
  no hay prueba y se hereda, el chequeo del catálogo (servidor); si es propia, **solo las primeras 20 opciones**
  leídas en el front.

> **No copiar — review E2, punto 16:** el aviso de títulos de más de 30 caracteres en la pregunta mira solo 20
> opciones: con 41 títulos largos entre 1.200 puede decir «1 opción» o nada. CE3-B muestra «41 opciones… y 38 más» con
> ejemplos. El arreglo es que el servidor calcule el chequeo para la fuente de la pregunta, en es y en en. Estado en
> `982ade8`: pendiente.

> **No copiar — review E2, punto 32** (CE3-B): falta decir qué campos usa «Como en el catálogo». Pendiente.

> **No copiar — regla de idioma del 2026-10-23** (review E2, punto 35): las etiquetas de campos caen a `es`
> (`localizedValue(field.name, culture, "es")`), no al original.

##### El teléfono del editor (`src/features/business-forms/components/FormPreview.tsx`, `ab31f5a`)

- `FormEditorPage` guarda, por dueño (actor, idioma, permisos y editor), la última vista calculada por cada pregunta de
  catálogo y la pasa al teléfono. El teléfono la usa solo si coincide la cultura de la vista previa y la fuente es
  igual a la de la pregunta (`sameCatalogQuestionSource`).
- Qué muestra en una pregunta de catálogo:
  - sin vista utilizable (con filtros y sin persona probada, sin permiso de leer catálogos, o leyendo): una opción
    deshabilitada «Elegí una opción» (si es de una opción) y «Las opciones se preparan según quien lo recibe, al mandar
    el formulario.»;
  - **sin filtros**: las primeras 20 opciones activas del catálogo armadas con la presentación de la pregunta, sin
    pedir nada al servidor;
  - con persona probada: sus opciones; «No hay opciones para esta persona.» si quedaron 0; o el motivo de
    `RecipientDataMissing` / `FilterUnavailable`, **nunca** el catálogo entero;
  - con varias: «Elegí hasta N» y casillas (se deshabilitan al llegar al máximo); con una: desplegable y, debajo, la
    descripción de la opción elegida.
- A menos de 1024 px (`lg`) el teléfono va debajo del editor, como antes.

##### Revisión de un conflicto (`src/features/business-forms/components/DraftReview.tsx`, `CatalogQuestionSourceSummary.tsx`, `a594fc1`)

En la comparación «Guardado actualmente» / «Mis cambios», cada pregunta de catálogo suma, debajo del nombre del
catálogo, un resumen: una línea por filtro («Campo: valor» o «Campo: dato de la persona»; si un valor de catálogo
difiere del otro lado, « · Cambió el valor elegido»), «Sin filtros de opciones» si no hay, «Como en el catálogo» o
«Propio de esta pregunta» y, con propia, «Título · hasta 30 caracteres: …» y «Descripción · hasta 300: …». Los valores
de catálogo se leen por id (sin GUID); el booleano muestra «Sí»/«No»; la fecha, numérica; el decimal, con coma en
español. Sin `formcatalogs.read` o ante 401/403 el resumen no se dibuja.

> **Hallazgo del relevamiento (no está en el review):** un valor fijo todavía vacío (o un booleano incompleto) se
> muestra con la clave `translationPending`, es decir «Sin traducir» / «Untranslated», que no corresponde.

> **No copiar — review E2, punto 31:** el formato de cada tipo no sale de una sola pieza: este resumen formatea el
> decimal reemplazando «.» por «,» y la planilla usa `formatExactDecimal` (`src/features/business-forms/responses.ts`).
> Estado en `982ade8`: pendiente.

#### 10.3.5 Comprobación de publicación: problemas y avisos (`src/features/business-forms/components/PublicationCheckDialog.tsx`, `a561037`)

En el diálogo P1 (anexo anterior, E1.3), entre los bloques de idiomas y «Qué queda fijo», ahora hay dos bloques fijos:

1. **«Problemas · frenan»**: la lista de problemas de siempre (tarjeta con pregunta, mensaje y «Ir a la pregunta» /
   «Ir al texto»), o «Ninguno.» con un tilde.
2. **«Avisos · no frenan»**: cada aviso con ícono (triángulo para personas sin el dato, información para el resto), el
   nombre de la pregunta, el mensaje del servidor y, según lo que traiga:
   - «Como mucho {maximum} opciones por persona, de un límite de {limit}.» o, con grupo, «Como mucho {maximum} opciones
     por persona ({grupo}), de un límite de {limit}.» (`Forms.Publication.OptionsPerRecipientHigh`);
   - «N personas no tienen el dato necesario para recibirlo.» y hasta 5 nombres separados por coma
     (`Forms.Publication.RecipientsWithoutData`).
   - Sin avisos: «Ninguno.».

Los avisos no cambian `canAccept`: la versión se puede publicar igual. Los títulos largos y los filtros imposibles son
problemas que frenan (back).

> **No copiar — review E2, punto 29:** el guard de avisos acepta **cualquier `code`** y deja todos los detalles
> opcionales (`publicationWarning` en `src/features/business-forms/api/response.ts`); el arreglo es una unión
> discriminada por `code`. Además «Como mucho 1 opciones»: `recipientMaximum` no tiene `_one`/`_other`. Estado en
> `982ade8`: pendiente las dos cosas. (La parte del guard de ítems de catálogo del mismo punto está en la sección 9.)

> **Para revisar — review E2, punto 18 (back):** un filtro fijo que deja más de 20 opciones en «elegir varias» llega
> como aviso y se dibuja en «Avisos · no frenan», aunque todos los envíos fallarían. Cuando el back lo corrija, pasará a
> «Problemas · frenan» sin cambiar el front.

> **Privacidad:** el bloque de avisos muestra **nombres de personas** (hasta 5) a quien publica. El back limita los
> ejemplos; en multitenant conviene decidir si quien publica puede verlos.

#### 10.3.6 El chat: bloqueos por datos y filtros (`src/features/whatsapp/components/FormCatalogPreparation.tsx`, `59491cb`)

En el diálogo «Enviar un formulario a {nombre}» (C2, anexo anterior), la preparación por destinatario suma dos códigos
a su tabla de motivos. Cada problema es un `Banner` de peligro con la etiqueta de la pregunta:
- `Forms.Record.RecipientDataMissing`: «{pregunta}: a esta persona le falta un dato para recibir estas opciones.
  Cargalo en Editar usuario y volvé a mandar.»
- `Forms.Record.CatalogFilterUnavailable`: «{pregunta}: el filtro no se puede usar. Avisale a quien administra el
  formulario.»

Con cualquier problema, `canSend` es false y «Enviar» queda bloqueado (`useFormComposer`). El servidor no manda
opciones ni valores privados en esos casos (`catalogs: []`). Desde el back `6023f7d7`, un valor guardado que ya no es
opción de la fuente actual del dato también da `RecipientDataMissing`.

> **No copiar — review E2, punto 32** (CE4-A): el bloqueo en el chat debería **nombrar a la persona y el dato que le
> falta**; hoy dice «a esta persona le falta un dato». Estado en `982ade8`: pendiente.

#### 10.3.7 Las respuestas — CE5

##### Planilla (`src/features/business-forms/pages/FormResponsesPage.tsx`)

- Después de cada pregunta de catálogo con campos, una **subcolumna por campo** con encabezado «{pregunta} · {campo}»
  (p. ej. «Proveedores a consultar · CUIT»). Por defecto se ven las de papel **dato**; las de papel **filtro** se
  prenden en «Columnas».
- Las subcolumnas **no se ordenan** (sin botón en el encabezado), no tienen filtro ni cuentan en facetas.
- Celda: los valores de cada opción elegida, en el orden capturado, separados por «, », formateados por el **tipo
  sellado** (entero/decimal exactos con `formatExactDecimal`, fecha numérica, «Sí»/«No», texto de la opción fuente en
  los de catálogo, nunca el GUID). Una opción sin ese campo aporta «—»; una respuesta anterior a los campos (sin
  snapshot) muestra «—». Ancho máximo 16 rem con salto de línea.
- 390/320 px: la tabla se desplaza dentro de su caja; la página no (escena visual).

##### Menú «Columnas» (`src/features/business-forms/components/ResponseColumnsMenu.tsx`)

Rótulo «Siempre visibles» con número, recibida y cargó (marcadas y apagadas); separador; cada pregunta con su casilla
(«(desde vN)» si se agregó después) y, sangradas debajo, sus campos como «{campo} · dato» o «{campo} · filtro». Cada
subcolumna es **independiente** de su pregunta: se puede ocultar la pregunta y dejar sus campos. La selección se guarda
normalizada en `columns` de la URL (ausente si coincide con la selección por defecto).

> **No copiar — review E2, punto 32** (CE5): el botón debería decir «Columnas · N ocultas»; hoy dice «Columnas».
> Pendiente.

##### Detalle D2 y pantalla completa (`src/features/business-forms/components/ResponseValue.tsx`)

Solo en el detalle (no en la celda de la planilla), debajo de cada opción elegida —etiqueta, detalle y nombre del
catálogo, como antes— una lista «nombre del campo · valor» con **todos** los campos sellados (dato y filtro), con el
nombre, tipo y texto del snapshot (no los actuales). Vale para una opción y para varias. En celular el detalle es la
pantalla completa (región «Respuesta»); en escritorio, D2 («Detalle de la respuesta»).

##### Exportar a Excel (`src/features/business-forms/components/ResponseExportDialog.tsx`)

- «Todas las preguntas (N columnas)» cuenta ahora preguntas **más** campos; «Solo las visibles (N columnas)» cuenta
  preguntas visibles más subcolumnas visibles.
- El POST `…/records/export` manda `{ filter, columns, catalogFields }`: con «Todas», `columns: null` y
  `catalogFields: null` (el back incluye todos los campos); con «Solo las visibles», los FieldId de preguntas visibles
  y la lista de pares `{questionId, fieldId}` visibles, aunque su pregunta base esté oculta.
- El resto del diálogo (10.000 como tope, 403, 429 con espera, cancelación) no cambió.

> **Para revisar:** la tarjeta de respuesta del chat **no** muestra campos (decisión de CE5); el PDF muestra los de
> dato (back).

### 10.4 Hooks y estado

| Pieza | Claves y ciclo | Concurrencia y privacidad |
| --- | --- | --- |
| `usePersonAttributes` (`src/features/business-forms/hooks/usePersonData.ts`) | `["forms-person-data", actorId, permisos ordenados, cultura, "attributes"]`; `staleTime 0`, `gcTime 0`, `errorOwner: "local"`; habilitada con `users.manage`, `formpersondata.manage` o `formtemplates.manage` | `useUnobservedQueryCleanup` la retira cuando nadie la observa |
| `usePersonDataPage` | `[…, "page", query]`; requiere `formpersondata.manage` + `users.read` | Un 403 retira solo `[…, "page"]` |
| `usePersonAttributeOptions` | `[…, "options", attributeId, search]`, página 1 de 20 | Sin `formcatalogs.read` |
| `usePersonData(scope, userId)` | `[…, "person", userId]`; el borrador vive en estado del hook con dueño = la clave serializada | Cambiar actor/idioma/permisos/persona crea un estado nuevo y aborta el guardado en curso; un resultado tardío se ignora. `save` manda el delta con la revisión capturada (null la primera vez) **fuera de MutationCache**; el recibo exige `revision = esperada + 1` y la misma persona. 409 de revisión → `conflict`, conserva el borrador hasta `reload`. 401/403 → `denied`, cancela y retira todo el dueño. Tras guardar, invalida el dueño |
| `PersonAttributeDialog` | Sin hook: `AbortController` propio; al cerrar, aborta | Tras guardar invalida `personDataScopeKey(scope)` |
| `useRecipientSamples` (`src/features/business-forms/hooks/useRecipientPreview.ts`) | `[...formScopeKey(scope), "recipient-samples", query]`, `gcTime 0`, local | Requiere `formtemplates.read` + `formtemplates.manage` |
| `useRecipientPreview` | **Sin TanStack Query**: `useEffect` con `AbortController` y un sello `[dueño, tipo, fuente normalizada, persona]`; dueño = alcance + definición + pregunta | Cambiar cualquier parte aborta y oculta lo anterior; 401/403 apaga la prueba del dueño. Sin reintento ni espera ante 429 (punto 28) |
| Vistas del teléfono (`FormEditorPage`) | Estado `{owner, items}` por pregunta; dueño = actor, idioma, permisos y editor | Se descartan al cambiar de dueño |
| `useCatalogItemsById` (`src/features/business-forms/hooks/useCatalogQueries.ts`, `7080141`) | `[...catalogScopeKey, "items-by-id", catalogId, ids]`, sin revisión en la clave, incluye inactivas; `errorOwner: "global"` | Para etiquetas de valores guardados |
| Borrador del formulario (`src/features/business-forms/draftSession.ts`, `catalogQuestionSource.ts`, `d1c33b6`) | El cuerpo congelado del guardado normaliza la fuente; el recibo compara **toda** la fuente (`sameCatalogQuestionSource`): si el servidor la devuelve sin filtros o sin presentación, el guardado queda sin confirmar | La comparación del editor (`editorModel.metadata`) usa la fuente normalizada: ni el orden de los filtros ni una herencia vacía inventan cambios |
| Planilla (`src/features/business-forms/hooks/useResponseRouteQuery.ts`, `responses.ts`) | `responseColumnDescriptors`, `visibleResponseColumnDescriptors`, `responseColumnSelection` | La selección por defecto no se escribe en la URL |

### 10.5 Clientes HTTP y guards zod

**Datos de las personas** (`src/features/business-forms/api/personData.ts`): todos con `cache: "no-store"` y
`AbortSignal`; los GUID de ruta se validan antes de enviar; un fallo de zod lanza `UnsupportedPersonDataResponse` **sin
conservar los issues** (pueden contener valores privados).

| Cliente | HTTP | Guard |
| --- | --- | --- |
| `fetchPersonAttributes` | `GET /api/forms/person-attributes` | `{items: atributo[] ≤ 20}`, ids y claves únicos. Atributo `strictObject`: `id`, `key` (`^[a-z][a-z0-9_]{0,39}$`), `name {es requerido, en opcional}` (≤120, no vacíos), `type` `Catalog`/`Text`, `sourceCatalogId` (requerido si Catalog, null si Text), `sourceCatalogName`, `position` 0–19, `isActive`, `revision ≥ 1`, `usedBy [{definitionId, name}]` |
| `createPersonAttribute` | `POST /api/forms/person-attributes` `{key, name, type, sourceCatalogId}` | Recibo con la misma clave y `revision = 1` |
| `updatePersonAttribute` | `PUT /api/forms/person-attributes/{id}` `{expectedRevision, name, type, sourceCatalogId, position, isActive}` | Mismo id y `revision = esperada + 1`. **Sin `confirmValueRemoval`** (punto 7) |
| `fetchPersonAttributeOptions` | `GET /api/forms/person-attributes/{id}/options?page&pageSize&sort&search` | Página `{id, text ≤ 200}` ≤ 100 |
| `fetchPersonDataPage` | `GET /api/forms/person-data?page&pageSize&sort&search` | Página de `{userId, displayName nullable, values}` |
| `fetchPersonData` | `GET /api/forms/person-data/{userId}` | `{userId, revision nullable, values}`; `values` = registro GUID → `{value ≤ 300, text ≤ 300}`, ≤ 20 |
| `savePersonData` | `PUT /api/forms/person-data/{userId}` `{expectedRevision, values: delta}` | Misma persona y `revision = (esperada ?? 0) + 1` |

**Vista previa por persona** (`src/features/business-forms/api/recipientPreview.ts`):
- `fetchRecipientSamples`: `GET /api/forms/recipient-samples?page&pageSize&search`, página `{id, name}`.
- `previewRecipient`: valida el GUID, la fuente contra `canonicalCatalogSourceSchema` y la persona **antes** de
  enviar; `POST /api/forms/definitions/{id}/recipient-previews`. Guard: `{recipient {id, name, values[{attributeId,
  name, text}] ≤ 20}, state Ready|RecipientDataMissing|FilterUnavailable, optionCount 0–201, limit 20|200,
  exceedsLimit, options [{id, title, description nullable}] ≤ 200, presentationCheck {titleTooLongCount ≤ 201, examples
  ≤ 3}}` con coherencias: `exceedsLimit ⇔ optionCount > limit`, `options = min(optionCount, limit)`, fuera de Ready
  `optionCount = 0`, ejemplos = `min(titleTooLongCount, 3)`, ids únicos; además, misma persona pedida, límite según el
  tipo (20 varias, 200 una) y solo valores de datos usados por los filtros. El título puede venir largo o vacío para
  poder mostrar el diagnóstico. Fallo → `UnsupportedRecipientPreviewResponse` sin issues.

**Contrato de la pregunta** (`src/features/business-forms/api/response.ts`, `431d4f9`):
`canonicalCatalogSourceSchema = strictObject({catalog, match?: (strictObject{fieldId, value 1–300} |
strictObject{fieldId, personAttributeId})[1..20] nullish, presentation?: {title [1..24], description [0..24]} nullish})`,
con FieldId únicos en `match`; las partes de presentación son `text|code|detail|value` o un GUID de campo. Reemplaza al
`source: {catalog}` de K6 en detalle, recibo y chequeo. En el modelo (`src/features/business-forms/model.ts`):
`FormQuestionSource` y `DraftQuestionSource` (el borrador admite nulls pendientes).

**Comprobación de publicación:** `warnings: publicationWarning[]` con `{fieldId nullable, path, code, message, limit?,
maximum?, maximumLabel?, count?, people? ≤ 5}`; un `fieldId` que no esté en el borrador hace fallar la lectura (punto 29:
`code` libre y detalles opcionales).

**Chat** (`src/features/whatsapp/api/forms.ts`): la preparación ya tenía `issues[{fieldId, label, code, catalogName,
invalidOptionCount}]` con `code` libre; el front mapea los dos códigos nuevos a texto.

**Respuestas** (`src/features/business-forms/api/responses.ts`, `b0ac79a`):
- Columna: `fields?: [{fieldId, key, label ≤ 120, type, role data|filter}]` sin tope (une actuales e históricos), ids
  únicos; solo en `choice`/`multiChoice`.
- Opción capturada: `fields?: [{fieldId, label, type, role, value ≤ 300, text?}] ≤ 20`, ids únicos y no vacíos; `text`
  solo en tipo `catalog`; el valor se valida **canónico por tipo** (entero, decimal, fecha ISO, `true`/`false`, GUID en
  catálogo) y no se formatea ni redondea. Ausente = respuesta anterior (no se completa); `null` se rechaza.
- Export: el cuerpo suma `catalogFields`: null/ausente = todos, `[]` = ninguno, lista de pares = esos.

**Opciones por id** (`src/features/business-forms/api/catalogs.ts`, `7080141`): `fetchCatalogItems` acepta
`ids: string[]` y los manda repetidos (`?ids=a&ids=b`, hasta 100).

### 10.6 Cohortes y fixtures exportadas de esta parte

El mecanismo general (`src/test/httpCohort.ts`, manifiesto, SHA256, una sola corrida) está en la sección 11.

| Cohorte (`httpCohort`) | Carpeta | Variable de entrada | Productor del back | Cuerpos | Consumidor |
| --- | --- | --- | --- | --- | --- |
| `personData` (artifactSet `person-data`) | `src/test/fixtures/forms-person-data-http` | `FORM_PERSON_DATA_HTTP_INPUT` | `FormPersonDataHttpTests.cs` + `FormPersonDataContractCapture.cs` (corrida `20261023-064235-…`, back `7e5b7a94`) | 24 (12 × es/en): attribute-created/updated, attributes, person-options, person-data, person-data-empty, person-data-saved, people, conflict (`Forms.PersonData.RevisionConflict`), invalid-value (`Forms.PersonData.ValueInvalid`), source-in-use (`Forms.Catalog.SourceInUse`), forbidden (403) | `src/features/business-forms/api/personDataHttpContract.test.ts` |
| `forms` (subcarpeta) | `src/test/fixtures/forms-http/recipient-filters` | `FORMS_HTTP_CONTRACT_INPUT` | `FormRecipientFilterIntegrationTests.cs` | 12 (6 × es/en): source-draft, recipient-samples, recipient-ready, recipient-missing, recipient-unavailable, source-check-warnings | `src/features/business-forms/api/recipientPreviewHttpContract.test.ts` |
| `responses` | `src/test/fixtures/forms-responses-http` (archivos `extra-fields-*`) | `FORMS_RESPONSES_HTTP_CONTRACT_INPUT` | `FormCatalogFieldsReadHttpTests.cs` | 10: sheet, detail y legacy (JSON) y export y visible-export (XLSX), × es/en | `src/features/business-forms/api/responseCatalogFieldsHttpContract.test.ts` (verifica que los XLSX empiecen con `PK`) |
| `whatsappCatalogs` | `src/test/fixtures/whatsapp-catalogs-http` | `WHATSAPP_CATALOG_HTTP_CONTRACT_INPUT` | suma `WhatsAppCatalogRecipientSendingTests.cs` | 30 en total; nuevos: `preparation-recipient-data-missing` y `preparation-filter-unavailable` × es/en | `src/features/whatsapp/api/catalogsHttpContract.test.ts` |

`stage4HttpProvenance.test.ts` incorpora `personData` a la procedencia conjunta (`8ccf786`).

> **Desalineación con el back:** las correcciones del back posteriores al export (`95e200c8` agrega
> `ValueRemovalNotConfirmed` y cambia `FormPersonDataHttpTests.cs` y los scripts de export) no están en la cohorte del
> front en `982ade8`. Al regenerar el export conjunto, el consumidor de datos de las personas tendrá que sumar ese caso.

> **No copiar — review E2, punto 33:** faltan en las cohortes los usos `Draft` y `Active` de `FieldInUse` (sección 9) y,
> del lado del back, las pruebas con PostgreSQL de `PersonAttribute.InUse` con borrador y con versión activa. La cohorte
> de datos de las personas no tiene un caso de `Forms.PersonAttribute.InUse`. Estado en `982ade8`: pendiente.

### 10.7 i18n

- `common.json`: `navigation.personData` («Datos de las personas» / «People data»).
- `forms.json`:
  - **`personData.*`** (nuevo): título, sección «Datos», ayuda, tabla, acciones, estados vacíos, «Para ver las personas
    necesitás Ver usuarios.», búsqueda, «Escribí para buscar más opciones.», fallos y recarga («No se pudieron guardar
    los datos. Probá de nuevo.», «El resto del usuario ya se guardó.», «Los datos cambiaron. Volvé a cargarlos para
    revisar.», «Volver a cargar los datos» y su confirmación).
  - **`catalogQuestion.filter.*`**, **`catalogQuestion.presentation.*`**, **`catalogQuestion.preview.*`** (con
    `count_one`/`count_other`), **`catalogQuestion.review.*`** y `catalogQuestion.optionsAtSend` («Las opciones se
    preparan según quien lo recibe, al mandar el formulario.»).
  - **`publication.blockingIssues`** («Problemas · frenan»), **`nonBlockingWarnings`** («Avisos · no frenan»),
    `noDiagnostics` («Ninguno.»), `recipientMaximum` y `recipientMaximumForGroup` (**sin plural**, punto 29),
    `recipientsMissingData_one/_other`.
  - Reutiliza `catalogs.presentation.*`, `catalogs.fields.types.*`, `catalogs.fields.roles.*` (para «· dato» / «·
    filtro» en Columnas), `catalogs.fields.englishName`, `catalogs.fields.key(Hint)`, `responses.yes/no`.
  - `d72caf4` retiró `catalogValue` («opción de catálogo»).
- `whatsapp.json`: `forms.catalogs.recipientDataMissing` y `forms.catalogs.filterUnavailable`.
- La paridad es/en la verifica `src/locales/parity.test.ts`. Voseo en todos los textos nuevos.

### 10.8 Pruebas

**Unitarias (Vitest)**

| Archivo | Qué cubre |
| --- | --- |
| `src/features/business-forms/api/personData.test.ts` | Nombres solo en original; valores privados con revisión nula inicial e identidad; tabla con nombre nulo; delta con revisión capturada; identidades y revisiones de alta y edición; ruta angosta de opciones; `no-store`, cancelación y rechazo de identidades inválidas antes del transporte |
| `src/features/business-forms/hooks/usePersonData.test.tsx` | Solo el delta, fuera de MutationCache; conserva borrador y revisión tras un 409 hasta recargar; limpia al cambiar de actor e ignora un guardado tardío; retira valores ante un 403 |
| `src/features/business-forms/components/PersonDataSection.test.tsx` | Opciones con `users.manage` solo; fallo independiente informado en «Datos» |
| `src/features/business-forms/pages/PersonDataPage.test.tsx` | Tabla con valores con los dos permisos; definición sin pedir personas que no se pueden leer; alta de un dato de texto con clave inmutable sin leer catálogos |
| `src/app/UserDataSectionShell.test.tsx` | Sin módulo no hay sección; con módulo, sección neutral |
| `src/features/users/components/UserEditDialog.data.test.tsx` | Guarda la cuenta primero y conserva el diálogo si fallan los datos; no escribe datos sin cambios |
| `src/features/business-forms/api/recipientPreview.test.ts` | Página de muestras cerrada; presentación y diagnósticos de títulos largos; estados cerrados sin opciones; rechazo de otra persona, dato no usado y conteos incoherentes; 201 filas con 20 en varias; ruta dedicada, `no-store` y cancelación |
| `src/features/business-forms/hooks/useRecipientPreview.test.tsx` | Prueba fuera de cachés sin guardar el borrador; ignora resultados tardíos; retira la persona al cambiar de dueño; limpia tras cambio de fuente y fallo |
| `src/features/business-forms/components/CatalogQuestionConfiguration.test.tsx` | Solo campos de filtro elegibles y datos de la misma fuente; presentación heredada ausente hasta elegir la propia; valor fijo fuera de las primeras 20 opciones, también en solo lectura |
| `src/features/business-forms/components/CatalogQuestionSettings.test.tsx` | Referencia de solo lectura sin leer catálogos; catálogo de Filtros sin filtros ni presentación |
| `src/features/business-forms/components/CatalogQuestionSourceSummary.test.tsx` | Cambios de valor y presentación sin identidades; booleano incompleto no es «No» |
| `src/features/business-forms/catalogQuestionSource.test.ts`, `catalogSourceSession.test.ts` | Copia, orden por FieldId, valores pendientes, comparación independiente del orden; congelado completo y recibo sin confirmar si pierde metadata |
| `src/features/business-forms/api/catalogSourceContract.test.ts` | Condiciones canónicas y presentación propia sin coerción; avisos no bloqueantes, incluso con máximo bajo el límite |
| `src/features/business-forms/components/FormCatalogPreview.test.tsx` | El teléfono muestra solo las opciones de la vista coincidente; no muestra las de una persona anterior tras cambiar la fuente; dato faltante sin el catálogo entero |
| `src/features/business-forms/components/PublicationCheckDialog.test.tsx` | (nuevo caso) diagnósticos por destinatario sin deshabilitar una versión publicable |
| `src/features/whatsapp/pages/WhatsAppForms.test.tsx` | Textos de los dos bloqueos nuevos |
| `src/features/business-forms/api/responseCatalogFields.test.ts` | Valores sellados exactos; más de 20 definiciones históricas con ≤ 20 por opción; referencias viejas sin inventar campos (null rechazado); ids inválidos; valor canónico por tipo y etiqueta solo en catálogo; orden de varias |
| `src/features/business-forms/responseCatalogColumns.test.ts` | Subcolumnas de dato tras su pregunta y filtros ocultos; selección independiente y normalizada; formato por tipo sin GUID; orden, faltantes y ausencia histórica |
| `src/features/business-forms/components/ResponseValue.test.tsx` | Todos los campos sellados solo en el detalle (una y varias) |
| `src/features/business-forms/components/ResponseCatalogExport.test.tsx`, `ResponseColumnsMenu.test.tsx` | Exportar una subcolumna visible con la pregunta oculta; selección independiente |
| Contratos HTTP | `personDataHttpContract`, `recipientPreviewHttpContract`, `responseCatalogFieldsHttpContract`, `catalogsHttpContract` (WhatsApp) — ver 10.6 |

**Visuales (Playwright, transporte sintético, sin goldens nuevos)**: cada escena corre en es/en a 1440, 390 y 320 px y
guarda capturas en `test-results/` (no se versionan).

| Spec | Escenario | Qué comprueba |
| --- | --- | --- |
| `src/test/visual/personData.spec.ts` (`personDataFixtures.ts`) | `person-data`, `person-data-save-error` | Dos tablas contenidas con desplazamiento propio a < 640 px; foco en «Nombre» del diálogo y retorno al cerrar; ningún POST/PUT al mirar; fallo parcial: escribe `/api/users/{id}` y después `/api/forms/person-data/{id}`, conserva el diálogo y el valor, «Volver a cargar los datos» visible y dentro del diálogo; opciones por la ruta del dato, **sin** leer `/api/forms/catalogs` (permisos del fixture: `formpersondata.manage`, `users.read`, `users.manage`) |
| `src/test/visual/catalogQuestionFields.spec.ts` (`catalogQuestionFixtures.ts`) | `forms-catalog-fields`, `forms-catalog-fields-missing` | Heredada con descripción apagada; Juan Gómez → «14 opciones» y 14 casillas en el teléfono; propia con campos quitados y agregados («Frío · 30»); filtro fijo «Frío» → 7 opciones; botones «Agregar filtro…» dentro de su contenedor (`565647e`); cero escrituras; la última fuente probada; Pablo Sosa sin el dato → el motivo en el editor y en el teléfono, sin casillas |
| `src/test/visual/catalogResponseFields.spec.ts` (`responseFixtures.ts`) | `responses-catalog-fields` | Subcolumna CUIT visible y Zona oculta por defecto; encabezado sin botón de orden; tabla contenida; prender «Zona · filtro» y ocultar la pregunta; exportar «Solo las visibles» con los tres pares y sin la pregunta base; detalle con los campos sellados («Plazo anterior») |
| `src/test/visual/catalogMultipleChoice.spec.ts`, `formCatalogQuestion.spec.ts` (`ab82204`) | — | Ajustados porque el teléfono ahora muestra las opciones del catálogo sin filtros |

Evidencia versionada: solo los tableros elegidos en
`docs/design/evidence/catalogos-campos-extra/` ([CE3-B](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-campos-extra/ce3-b-tambien-en-la-pregunta.png),
[CE4-A](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-campos-extra/ce4-a-datos-de-la-persona.png),
[CE5](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-campos-extra/ce5-las-respuestas.png) y
[CE6](../../../../ArquitecturaBaseFront/docs/design/evidence/catalogos-campos-extra/ce6-el-circuito.png)). El
[cierre local de E2](../../../../ArquitecturaBaseFront/docs/design/catalogos-campos-extra.md#cierre-local-de-e2--2026-10-23)
registra la VRT 719/719 y el recorrido es/en a 390/320; las capturas de la implementación no están en el repo.

### 10.9 Resumen de avisos de esta parte

| Punto | Dónde | Estado en `982ade8` |
| --- | --- | --- |
| 7 (front) | Diálogo de dato: falta confirmar el borrado de valores al cambiar tipo o fuente | Pendiente (el back ya lo exige) |
| 14 | Valor fijo de un filtro de catálogo | Corregido (`d72caf4`, `7080141`) |
| 15 | Filtros y presentación con catálogos de Filtros | Corregido (`e133d93`) |
| 16 | Aviso de títulos largos en la pregunta con 20 opciones | Pendiente |
| 26 | Persona abierta desde la tabla; tabla sin refrescar el nombre; 409 sin recarga | Pendiente |
| 28 | «Como si lo recibiera» sin reintento ni espera ante 429 | Pendiente |
| 29 | Guard de avisos laxo; «Como mucho 1 opciones» | Pendiente |
| 31 | Formato por tipo repartido (resumen de conflicto y planilla) | Pendiente |
| 32 | «Como mucho N por persona», «Todos ven las mismas N», «N personas · M sin zona», bloqueo que nombre persona y dato, «Columnas · N ocultas», campos de «Como en el catálogo» | Pendiente |
| 33 | Cohortes y pruebas de `InUse` | Pendiente |
| 35 | «Nombre en inglés», `name.es` obligatorio, caída a `es` | Pendiente |
| 38 | «Guardá el formulario para probar con una persona»; botones apagados sin tooltip; texto bajo botones | Pendiente |
| Después | `search` de personas en la URL | Pendiente |

### 10.10 Para el multitenant

- **Datos de las personas por organización.** Definiciones, valores y la tabla son por organización: claves de query,
  permisos y la búsqueda de personas tienen que incluir el tenant; un dato no puede apuntar a un catálogo de otra
  organización ni un filtro a un dato de otra.
- **B2C.** En B2B las «personas» son usuarios internos con «Editar usuario». En B2C quien recibe el formulario puede
  ser un cliente final sin cuenta interna: hay que decidir dónde se cargan sus datos (no en «Editar usuario»), quién
  los carga y cómo se elige la persona de ejemplo de «Como si lo recibiera».
- **Privacidad.** La tabla de personas, los nombres de los avisos de publicar y las muestras de «Como si lo recibiera»
  exponen nombres y valores. Revisar qué ve cada rol en cada tenant, que nada privado vaya a la URL (hoy `search` sí) y
  conservar `no-store`, `gcTime 0`, sin MutationCache y la retirada inmediata ante 403.
- **`users.read` y `users.manage` por tenant.** La tabla exige `formpersondata.manage` + `users.read`; cargar valores,
  `users.manage`; las opciones del dato no exigen leer catálogos. Mapear esos permisos al modelo de roles del
  multitenant sin que uno implique otro.
- **Punto de extensión.** Conservar el contrato neutral (`UserDataSectionContext`): la pantalla de usuarios no debe
  importar formularios, y sin el módulo no aparecen ni la ruta ni la sección. Mostrar la carga y el error del usuario
  dentro del diálogo, no fuera de la pantalla.
- **Guardado en dos pasos.** Cuenta primero y datos después, con mensaje de resultado parcial; o un solo endpoint que
  guarde ambos. Si se mantiene en dos, conservar el delta con revisión y la recarga explícita ante 409.
- **Filtros por persona.** Los filtros son igualdad sobre campos de papel filtro, sumados; el valor de la persona es
  la identidad de una opción. Con tenants, revisar índices y topes (201 opciones, 20 en varias, 200 en una) y que la
  preparación del envío, el asistente y la vista previa usen el mismo punto (como hace el back).
- **Idioma.** Rehacer los nombres de datos y campos con la regla nueva: un solo campo en el idioma del menú y caída al
  original, no a `es`.
- **Respuestas.** Las subcolumnas leen solo lo sellado: no recalcular con el catálogo actual. La selección de columnas
  en la URL lleva ids de pregunta y campo (no datos privados).

### 10.11 Endpoints nuevos o cambiados

Nuevos:
- `GET /api/forms/person-attributes`
- `POST /api/forms/person-attributes`
- `PUT /api/forms/person-attributes/{id}` (el back acepta además `confirmValueRemoval`, que el front no manda)
- `GET /api/forms/person-attributes/{id}/options?page=&pageSize=&sort=&search=`
- `GET /api/forms/person-data?page=&pageSize=&sort=&search=`
- `GET /api/forms/person-data/{userId}`
- `PUT /api/forms/person-data/{userId}`
- `GET /api/forms/recipient-samples?page=&pageSize=&search=`
- `POST /api/forms/definitions/{id}/recipient-previews`

Cambiados:
- `GET /api/forms/catalogs/{id}/items?ids=…&ids=…` (opciones por id, hasta 100).
- Detalle, guardado y recibo del formulario (`GET /api/forms/definitions/{id}`, `POST /api/forms/definitions`,
  `PUT …/{id}/draft`): `source` con `match` y `presentation`.
- `POST /api/forms/definitions/{id}/publication-checks`: `warnings`.
- `GET /api/whatsapp/chats/{id}/forms/{definitionId}/versions/{versionId}/preparation`: códigos
  `Forms.Record.RecipientDataMissing` y `Forms.Record.CatalogFilterUnavailable`.
- `POST /api/forms/definitions/{id}/records/search` y `GET /api/forms/records/{id}`: `columns[].fields` y
  `choice.fields`.
- `POST /api/forms/definitions/{id}/records/export`: `catalogFields`.
- `PUT /api/users/{id}`: sin cambio de contrato; ahora va antes del PUT de datos.

### 10.12 Huecos

- Nada se verificó en navegador ni corriendo pruebas (solo lectura del commit `982ade8`): las migas de
  `/datos-de-personas`, el comportamiento exacto con un 403 al abrir una persona desde la tabla y el aspecto a 390/320
  salen del código y de las escenas, no de capturas vistas.
- 429 en las lecturas de datos de las personas y en el diálogo de dato: no hay manejo dedicado en el código; no
  verificado qué muestra.
- Los nombres que devuelven las muestras de «Como si lo recibiera» en el back real (el fixture visual los arma como
  «Juan Gómez · Centro»): no verificado.
- No se revisó si el back filtra `usedBy` de los datos de las personas por `formtemplates.read`.
- Fuera de esta parte, para la sección 9: el back `84289eef` (2026-10-23) suma `questionId` a cada uso de un campo;
  el guard `catalogFieldUsageSchema` del front en `982ade8` es `strictObject` sin ese campo, así que los detalles de
  catálogo con usos fallarían al leerse hasta que el front lo sume (no verificado contra un cuerpo real).

---

## 11. Piezas transversales: componentes compartidos, HTTP, cohortes, pruebas e i18n

Relevado en el commit `982ade8` del front, comparado con `9261e3d`, leyendo solo el estado commiteado. Esta sección
cubre lo que cambió **fuera de las pantallas de cada etapa**: la biblioteca `src/shared/ui`, el transporte HTTP y los
hooks comunes, el mecanismo de cohortes HTTP exportadas, el arnés de pruebas, la estructura de traducciones y la
documentación del front. Las secciones 3 a 10 describen cómo usa cada pantalla estas piezas; acá se explica la pieza en
sí, una sola vez.

Lo más importante para quien reconstruye:

- **Seis piezas nuevas en `shared/ui`** (pestañas, selección de filas en `DataTable`, sección plegable, tabla ancha con
  celdas fijas, diálogo a pantalla completa en el celular y audio privado) y ajustes chicos en otras nueve. Ninguna
  conoce una feature: reciben textos, estado y callbacks del dueño.
- **El cliente HTTP manda `FormData`** sin fijar `Content-Type` y por el mismo camino de sesión, idioma, cancelación y
  renovación única que el JSON.
- **Los permisos admiten alternativas**: `PermissionRequirement` es un permiso o una lista «cualquiera de»; la usan
  la ruta y el menú por igual.
- **Las cohortes HTTP pasaron de 6 familias con helper a 22**, todas producidas por **una sola corrida** del backend y
  verificadas byte a byte. En `982ade8` suman **499 capturas** del run `20261023-064235-5542f7f13d844d5985f55c4142c9385f`
  (backend `7e5b7a94`).
- **Ninguna pantalla de Formularios ni de Catálogos tiene captura de referencia (golden).** Sus escenas Playwright
  verifican geometría, texto, teclado y foco, y dejan evidencia en `test-results/` para inspección manual.
- **`forms.json` casi duplicó su tamaño** (636 → 1.207 claves por idioma) y sigue en paridad es/en.

---

### 11.1 Componentes compartidos (`src/shared/ui`)

Vocabulario del apartado 2 del anexo anterior. Lo que sigue es el delta.

#### Resumen

| Pieza | Estado | Commit | Qué agrega | Quién la usa en `982ade8` |
| --- | --- | --- | --- | --- |
| `Tabs`, `TabsList`, `TabsTrigger`, `TabsContent` | Nueva | `978a8d1` | Pestañas subrayadas sobre Radix | Pestañas del panel D2 (`src/features/business-forms/components/ResponseDetailContent.tsx`), pestañas de la revisión de importación (`src/features/business-forms/components/CatalogImportDialog.tsx`) |
| `DataTable` (`selectedRowKey`, `onRowClick`) | Cambiada | `978a8d1` | Fila seleccionada y apertura por clic en celda | Planilla de respuestas (`src/features/business-forms/pages/FormResponsesPage.tsx`) y «Por aprobar · N» (`src/features/business-forms/components/FormPendingApprovals.tsx`) |
| `CollapsibleSection` | Nueva | `9510b05` | `Surface` con flecha que pliega el cuerpo | Secciones Respuesta y Actividad de DC1 (`src/features/business-forms/pages/FormResponseFullPage.tsx`) |
| `WideTable`, `WideTableHead`, `WideTableCell` + `Table.scrollLabel` | Nueva / cambiada | `89462fd` | Región de desplazamiento con nombre y celdas fijas a los costados | Tabla de opciones con campos extra (`src/features/business-forms/components/CatalogItemsTable.tsx`), tablas de datos de las personas (`src/features/business-forms/pages/PersonDataPage.tsx`) |
| `DialogContent.mobileFullscreen` | Cambiada | `fbb17cf` | Diálogo que ocupa la pantalla en el celular, con cuerpo desplazable y pie fijo | Dar de baja (`FormDeactivationDialog.tsx`), Eliminar (`FormDeletionDialog.tsx`), Reactivar (`PublicationCheckDialog.tsx`, solo en modo reactivación), Importar opciones (`CatalogImportDialog.tsx`) |
| `Badge` variantes `success`, `warning`, `danger` | Cambiada | `4f3a922` | Colores semánticos comunes | Estados de aprobación (`src/features/business-forms/approvals.ts`), pendientes de MF1 (`FormOverviewPage.tsx`), severidad en la importación (`CatalogImportDialog.tsx`) |
| `Button size="sm-wrap"` | Cambiada | `565647e` | Botón chico que envuelve su texto | Acciones largas de los filtros de la pregunta de catálogo (`src/features/business-forms/components/CatalogQuestionFilters.tsx`) |
| `DropdownMenuItem` con `aria-disabled` | Cambiada | `00c3f93` | Ítem apagado que igual recibe foco | Menú «⋯» del ciclo de vida (`FormLifecycleMenu.tsx`), menú de Opciones del catálogo (`CatalogOptionsActions.tsx`) |
| `MultiSelect.disabled` | Cambiada | `2b27e0d` | Deshabilita disparador y opciones | Editor de condiciones compartido por reglas y sugerencias (`src/features/business-forms/components/FormConditionsEditor.tsx`) |
| `SegmentedControl.wrapLabels` | Cambiada | `9fc142a` | Etiquetas en varias líneas | Solo el selector «Textos en» del editor (`src/features/business-forms/pages/FormEditorPage.tsx`) |
| `PrivateAudio` | Nueva | `88c4115` | `<audio>` nativo sobre un Blob privado | Audios del chat (`src/features/whatsapp/components/ChatAudio.tsx`) |
| `Banner` | Cambiada | `be0824e` | La acción baja de renglón si no entra | Todos los consumidores de `Banner` |
| `EmbeddedPreview` (`kind`, `className`) | Cambiada | `a9848c0`, `1610f9e` | Variante PDF sin `sandbox` y altura ajustable | Visor y mini vista del PDF (`src/features/business-forms/components/FormPdfPreview.tsx`) |
| `FormRecordFiles` | Cambiada | `0d9a9f1` | El nombre del documento conserva ancho; «Descargar» baja si no entra | Archivos en chat, planilla y detalle |
| `icons` | Cambiada | `978a8d1`, `fbb17cf` | `ArrowDownIcon`, `CloseIcon`, `DocumentIcon`, `MoreVerticalIcon` (Lucide `EllipsisVertical`) | Flechas y cierre del panel D2 (`ResponsePanelFrame.tsx`), marca PDF de la planilla, menús «⋯» |
| `Autocomplete` | **Sin cambios** desde `9261e3d` | — | — | Celdas de campos de catálogo, persona de ejemplo de Filters, filtros por persona, sección de datos en «Editar usuario», búsqueda de lugares |

#### `Tabs` (`src/shared/ui/Tabs.tsx`)

- **API:** reexpone `Tabs.Root`, `List`, `Trigger` y `Content` de Radix (`radix-ui`) con las clases del sistema. La
  selección puede ser controlada (`value`/`onValueChange`) o inicial (`defaultValue`).
- **Aspecto:** pestañas subrayadas sobre una línea inferior; la activa en negrita con borde de marca. `TabsList` se
  desplaza a lo ancho (`overflow-x-auto`) si no entra. En celular (`max-sm`) cada pestaña mide al menos 44 px de alto.
- **Accesibilidad:** la de Radix. Una sola parada de tabulación; flechas, Inicio y Fin mueven foco y selección,
  saltando las deshabilitadas; cada pestaña queda asociada a su panel. Cambiar de pestaña con el teclado no envía el
  formulario que la contiene.
- **Pruebas:** `src/shared/ui/Tabs.test.tsx` tiene 5 casos. El catálogo visual las muestra con `?scenario=tabs`
  (`src/test/visual/Catalog.tsx`).

#### Selección de filas en `DataTable` (`src/shared/ui/DataTable.tsx`)

- **`selectedRowKey?: string`.** La fila con esa identidad recibe `data-state="selected"` y `aria-selected="true"`; las
  demás, `aria-selected="false"`. Sin la prop, ninguna fila lleva `aria-selected`: los listados existentes no cambian.
- **`onRowClick?: (row) => void`.** Clic izquierdo en una celda sin controles abre la fila y el cursor pasa a puntero.
  Se ignoran: un evento ya prevenido, el clic con Ctrl, Meta, Shift o Alt, y el que cae sobre un enlace, botón,
  campo, `role` button/checkbox/combobox/menuitem o contenido editable.
- **Teclado:** la fila **no** es foco. La navegación por teclado la aporta el enlace del número de respuesta dentro de
  la fila; las pruebas fijan que Enter en un enlace o en un botón no abre la fila por su cuenta.
- **Pruebas:** 4 casos nuevos en `src/shared/ui/DataTable.test.tsx`: selección tras reordenar, sin props, apertura
  desde contenido anidado y evento ya prevenido.

#### `CollapsibleSection` (`src/shared/ui/CollapsibleSection.tsx`)

- **API:** `title`, `open`, `onOpenChange`, `expandLabel`, `collapseLabel`, `summary?` (se muestra plegada), `meta?`
  (se muestra abierta), `children`. Abrir o cerrar lo decide el consumidor.
- **Aspecto:** es un `Surface` con título. En la banda, a la derecha, van el texto de `summary` o `meta` y un
  `IconButton` con flecha que gira.
- **Comportamiento:** el cuerpo queda montado y oculto (`hidden`) al plegar, para no perder estado. No mueve el foco.
  El botón lleva `aria-expanded` y `aria-controls` con una identidad estable.
- **Pruebas:** 4 casos en `src/shared/ui/CollapsibleSection.test.tsx`: Enter y Espacio sin enviar el formulario,
  independencia entre secciones, y que espera al dueño antes de cambiar.

> **Revisar con la regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), commit `bb98bf2`): el texto de `summary`/`meta`, al lado del botón de plegar, resume el **contenido** («Versión 3», los momentos de la actividad). No explica el botón, así que no es el caso que prohíbe la regla; conviene no usarlo para motivos ni estados de guardado.

#### `WideTable` y `Table.scrollLabel` (`src/shared/ui/WideTable.tsx`, `src/shared/ui/table.tsx`)

- **`Table` acepta `scrollLabel`.** Con la prop, el contenedor `overflow-x-auto` pasa a ser `role="region"` con
  `aria-label` y `tabIndex=0`, así que se desplaza con el teclado. Sin la prop, el comportamiento anterior no cambia.
- **`WideTable label`** es `Table` con `scrollLabel` obligatorio.
- **`WideTableHead` y `WideTableCell` aceptan `pinned="start" | "end"`.** Agregan `sticky z-10 bg-[inherit]` pegado a
  izquierda o derecha. El fondo se **hereda de la fila** para conservar alternado, hover y selección. El consumidor fija
  juntos el encabezado y las celdas de la misma columna.
- **En `CatalogItemsTable`** se fijan la columna «Texto» al inicio y la de acciones al final.
- **Pruebas:** 1 caso en `src/shared/ui/WideTable.test.tsx` (semántica de tabla y región nombrada). La geometría
  queda para el navegador, como dice la guía de `shared/ui`.

> **No copiar — «Después, sin bloquear» del review E2** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)): con una fila expandida, las celdas fijas se transparentan al desplazar de costado.
> - **Mecanismo deducido del CSS, sin verificar en navegador:** `TableRow` pone `has-aria-expanded:bg-muted/50`, un fondo semitransparente, y la celda fija lo hereda con `bg-[inherit]`, así que el contenido que pasa por debajo se ve a través.
> - **Estado en `982ade8`:** pendiente; `WideTable.tsx` no cambió después de `89462fd`.
> - **Al replicar:** la celda fija necesita un fondo opaco propio, que se calcule sobre el de la fila.

> **No copiar — review E2, punto 17** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)): el desplegable de `Autocomplete` se recorta dentro de la tabla ancha.
> - **Causa:** la lista es un `<ul>` con `absolute z-20` dentro del propio componente (`src/shared/ui/Autocomplete.tsx:28`), sin portal. La tabla ancha es un contenedor `overflow-x-auto` (`src/shared/ui/table.tsx:8`), y en CSS eso vuelve `auto` también el desborde vertical: en las últimas filas, o con pocas filas, la lista se corta o obliga a desplazar la caja.
> - **Dónde pega:** `Autocomplete` se usa dentro de celdas en `src/features/business-forms/components/CatalogExtraFieldInput.tsx`, que `CatalogItemsTable` dibuja dentro de `WideTable`.
> - **Estado en `982ade8`:** pendiente; `Autocomplete.tsx` no cambió en todo el período. Es plausible según el CSS, sin verificar en navegador.
> - **Al replicar:** la lista va en un portal con posición calculada, como ya hacen los menús de Radix.

#### `DialogContent mobileFullscreen` (`src/shared/ui/dialog.tsx`)

- **Prop optativa `mobileFullscreen?: boolean`**, con `false` por defecto. Con `placement="right"` sigue mandando el
  panel lateral.
- **Por debajo de 640 px (`sm`):** `fixed inset-0`, alto `h-svh`, ancho completo, `flex-col` y `overflow-hidden`.
  El consumidor arma un cuerpo `min-h-0 flex-1 overflow-y-auto` y deja `DialogFooter` afuera, así el pie con las
  acciones queda siempre visible.
- **Desde 640 px:** vuelve al diálogo central de hasta `max-w-lg`, con alto máximo `100svh-2rem`.
- **Uso:** solo en decisiones largas. La guía [estándar visual](../../../../ArquitecturaBaseFront/docs/guides/estandar-visual.md)
  la registra como «Decisión extensa en móvil». El catálogo la muestra con `?scenario=mobile-dialog`.

#### Ajustes chicos

- **`Badge`:** tres variantes semánticas nuevas, `success`, `warning` y `danger`, con la paleta común. Caso suelto:
  la insignia de estado de `FormEditorPage` todavía arma esos colores con clases a mano en vez de usar las variantes
  (`src/features/business-forms/pages/FormEditorPage.tsx:244`). `CatalogFiltersEditorPage` hace lo mismo con la
  insignia de propuesta.
- **`Button size="sm-wrap"`:** altura mínima chica, `whitespace-normal` y `max-w-full`. Sirve para acciones largas en
  contenedores angostos sin crear variantes locales.
- **`DropdownMenuItem`:** suma `aria-disabled:opacity-50`. Un ítem con `aria-disabled` (y no `disabled`) sigue
  recibiendo foco con las flechas, así que el lector de pantalla lee el motivo. Lo usan los menús «⋯» del ciclo de vida
  y de Opciones del catálogo (secciones 6 y 8).
- **`MultiSelect.disabled`:** bloquea el disparador y cada opción, aun con el menú abierto. Tiene 2 casos nuevos en
  `src/shared/ui/MultiSelect.test.tsx`.
- **`SegmentedControl.wrapLabels`:** permite etiquetas en varias líneas con alto mínimo. Su **único** consumidor es el
  selector «Textos en» del editor de formularios, para que el contador de faltantes se vea en el celular.

  > **No copiar — regla de idioma del 2026-10-23** (review E2, punto 35): el selector «Textos en» se retira de las pantallas, así que `wrapLabels` se queda sin consumidor. No hace falta reconstruirlo para ese uso.

- **`PrivateAudio`:** recibe `blob`, `label` y `playOnLoad`. Crea la URL local al montar y la revoca al desmontar o al
  cambiar el blob, después de pausar y retirar `src`. Usa `<audio controls preload="none">` nativo, sin duración ni
  forma de onda inventadas. Con `playOnLoad` enfoca y reproduce: lo dispara la acción explícita «Escuchar».
- **`Banner`:** el bloque de ícono y texto reserva un ancho útil (`basis-56 grow`); la acción queda a la derecha si
  cabe y baja de renglón si no.
- **`EmbeddedPreview`:**
  - `kind="external"`, el predeterminado, conserva `sandbox="allow-scripts allow-same-origin allow-forms"`;
  - `kind="pdf"` **quita el sandbox** para que el visor nativo del navegador pinte el PDF. Solo debe recibir una URL
    `blob:` creada desde bytes PDF ya validados, nunca una URL remota;
  - `className` ajusta la altura de la mini vista.
- **`FormRecordFiles`:** el bloque de descarga de un documento pasa a `flex-wrap` y el nombre toma `basis-40 grow`.
- **Nota sobre `Surface`, que no cambió:** su banda es `flex flex-wrap justify-between` y el bloque de acciones no
  lleva `ml-auto`. Cuando las acciones no entran al lado del título y bajan de renglón, quedan a la izquierda. Es una
  deducción del CSS, sin verificar en navegador. Si se confirma, viola «las acciones van al borde derecho» y conviene
  corregirlo en la pieza común, no en cada pantalla.

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), commit `bb98bf2`): `Surface.actions` acepta cualquier nodo, y dos pantallas lo usaron para poner un párrafo `basis-full` con el motivo **debajo** de «Guardar»: `src/features/business-forms/components/FormRulesSection.tsx:134` y `src/features/business-forms/components/FormSuggestionsSection.tsx:108`. La segunda también pone «Cambios sin guardar» o «Revisión N» al lado.
> - **La pieza correcta para el motivo ya existe:** `ActionTooltip`, que funciona con hover y foco, también en botones deshabilitados.
> - **Estado en `982ade8`:** pendiente; el detalle está en las secciones 3 y 5.

---

### 11.2 HTTP, hooks comunes, fechas y permisos

#### `FormData` en el cliente HTTP (`src/shared/api/httpClient.ts`, `bf4440b`)

- **`api.post`, `api.put` y `api.patch`** mandan el cuerpo tal cual si es `FormData` y, si no, lo serializan con
  `JSON.stringify`.
- **En `send()`:** con `FormData` se **borra** el `Content-Type`, porque el navegador agrega el `boundary`; con
  cualquier otro cuerpo se fija `application/json`.
- **Igual que el JSON:** el pedido conserva token, `accept-language`, señal de cancelación y la **renovación única**
  ante un 401. El test nuevo de `src/shared/api/httpClient.test.ts` manda un CSV con `mode` y `expectedRevision`,
  recibe un 401, renueva y reintenta con el **mismo** `FormData`, el token nuevo, sin `Content-Type` y con el idioma.
- **Uso:** lo usan la vista previa y la aplicación de la importación de catálogos (sección 8).

#### Hooks (`src/shared/hooks`)

- **`useQueryUpdate`** (`3f085b6`) conserva el `history.state` opaco de la entrada actual al cambiar la query o el
  pathname. Así, fijar el paginado o pasar de una respuesta a otra no pierde el contexto de regreso: el origen
  «Por aprobar» o MF1 de la sección 5. Ese estado no se serializa en la URL. Lo prueba un caso nuevo de
  `src/shared/hooks/useFilters.test.tsx`.
- **`userDataSectionContext`** (`20ab21d`, archivo nuevo `src/shared/hooks/userDataSectionContext.ts`) es el contrato
  neutral de una sección opcional en «Editar usuario»:
  - `UserDataSectionExtension = { Section, openUser(userId) }`;
  - la `Section` recibe `{ userId, disabled, userSaved, onState }` y reporta `{ dirty, busy, ready, save(): Promise<boolean> }`;
  - `app` aporta la implementación (`src/app/UserDataSectionShell.tsx`), y `users` la consume sin importar el módulo de
    formularios. El detalle funcional está en la sección 10.

#### Fechas (`src/shared/lib/dateTime.ts`, `2b7812f`)

`formatDateOnly(value, language, style = "medium")` suma el estilo `"numeric"`, que da `dd/MM/yyyy` en es y
`MM/dd/yyyy` en en, sin zona horaria. El predeterminado sigue siendo `medium`, por ejemplo «15 mar 2027».

> **No copiar — review E2, punto 31** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)): el formato de cada tipo no sale de una sola pieza.
> - **Con `"numeric"`:** la tabla de opciones (`CatalogItemsTable.tsx:64`), el resumen de filtros (`CatalogQuestionSourceSummary.tsx:30`), la presentación del catálogo (`catalogPresentation.ts:27`) y las subcolumnas de respuestas (`responses.ts:109`).
> - **Con el predeterminado:** `formatCatalogValue` en `catalogValues.ts:55`, que usa la revisión de la importación (`CatalogImportDialog.tsx`), y las respuestas (`ResponseValue.tsx:100`).
> - **Efecto:** la misma fecha se ve «15/03/2027» en la tabla y «15 mar 2027» en la revisión.
> - **Estado en `982ade8`:** pendiente.
> - **Al replicar:** un único formateador por tipo de valor, con el estilo decidido una vez.

#### Permisos con alternativas (`src/auth/usePermissions.ts`, `src/auth/ProtectedRoute.tsx`, `3e359a8`)

- **`PermissionRequirement = string | readonly string[]`.** `has(requirement)` exige ese permiso si es un string, y
  **cualquiera** de la lista si es una lista. Una lista vacía no autoriza.
- **El mismo tipo en ruta y menú:** `ProtectedRoute permission` y `NavigationLink.permission` usan el mismo tipo, así
  que ruta y menú no se desalinean.
- **Caso de uso:** MF1 y el espacio por formulario, con `["formrecords.read", "formapprovals.decide"]`.
- **Pruebas:** 6 casos nuevos en `src/auth/ProtectedRoute.test.tsx`, incluido que la lista vacía no vuelve pública la
  ruta. El backend sigue autorizando cada pedido.

---

### 11.3 Cohortes y fixtures exportadas: cómo funcionan

#### Qué es una cohorte

Una **cohorte** es una carpeta bajo `src/test/fixtures/` con **respuestas HTTP reales** que produjeron los tests de
integración del backend: controller, serializer y EF reales contra PostgreSQL y Redis de Testcontainers, con datos
sintéticos. Se guardan **byte a byte**, junto a un manifiesto `provenance.json` que dice de qué corrida salieron.

Sirven para que los guards zod del front se califiquen contra lo que el back **de verdad** serializa, no contra DTO
escritos a mano. Cada familia tiene su consumidor `*HttpContract.test.ts`.

En `9261e3d`, solo 6 familias pasaban por el helper común (responses, rules, workflow, approvals, pdf y
whatsappActions). Las demás tenían lecturas propias. En `982ade8` las **22** familias activas pasan por
`src/test/httpCohort.ts`, y `src/features/business-forms/api/stage4HttpProvenance.test.ts` exige que todas salgan de
la misma corrida.

#### Cómo las produce el backend

- **Captura en cada productor.** Cada test productor del back tiene un `*ContractCapture` en
  `tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/...`. Si su variable de entorno `*_OUTPUT` está definida,
  escribe ahí los bytes de `response.Content` (por ejemplo, `FormLifecycleContractCapture` usa
  `FORM_LIFECYCLE_HTTP_OUTPUT/<cultura>/<nombre>.json`). Sin la variable, no escribe nada.
- **El export conjunto** es `tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Modules/BusinessForms/export-responses-http.ps1`.
  Su configuración se prueba con archivos sintéticos en `test-export-responses-http.ps1`. Pasos:
  1. Crea un `runId` (`yyyyMMdd-HHmmss-<guid>`) y una carpeta nueva bajo `.artifacts/assistant-validation/stage5-final/<runId>`.
  2. Define cada `*_OUTPUT` apuntando a una subcarpeta de esa corrida. Varias cohortes comparten
     `FORMS_HTTP_CONTRACT_OUTPUT` con subdirectorios propios: `catalog-filters`, `k6-source` y las carpetas de `forms-http`.
  3. Registra `git rev-parse HEAD`, `git status --short` y el SHA256 de cada archivo fuente de los productores.
  4. Corre **una sola vez** `dotnet build ArquitecturaBase.slnx --no-restore` y `dotnet test --no-build --no-restore`,
     la suite completa. Si falla, aborta.
  5. Valida el inventario de cada cohorte contra su lista `RequiredNames`, si la tiene, y que se haya producido al menos
     una captura. Verifica que ningún archivo fuente de productor cambió durante la corrida.
  6. Copia los bytes al front, en `../ArquitecturaBaseFront/src/test/fixtures/<destino>`, comprueba el SHA256 después de
     copiar, **borra los archivos viejos** del destino que no estén en la corrida y escribe `provenance.json`.
- **Regla del back:** las exportaciones se hacen solo en el cierre conjunto, con Docker, nunca con datos de la base de
  desarrollo (`tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Modules/BusinessForms/AGENTS.md`).

#### El manifiesto `provenance.json`

Lo valida un `z.strictObject` dentro de `httpCohort.ts`.

| Campo | Contenido |
| --- | --- |
| `schemaVersion` | `1` |
| `artifactSet` | Nombre de la familia; tiene que coincidir con el que declara `httpCohort` (por ejemplo `manual-catalogs`) |
| `runId` | Identidad de la corrida |
| `generatedAtUtc` | Instante ISO |
| `repositoryHead` | SHA completo del backend |
| `sourceState` | `git status --short` al exportar. En la corrida vigente figuran tres `.pending` sin seguimiento en `docs/` |
| `command` | `dotnet build ArquitecturaBase.slnx --no-restore; dotnet test --no-build --no-restore` |
| `producer` | Clases productoras, separadas por « and » |
| `sources[]` | `{ path, sha256 }` de cada fuente productora |
| `transport` | Por ejemplo, «Actual HTTP; PostgreSQL/Redis Testcontainers; synthetic records and private files» |
| `captures[]` | `{ name, length, sha256 }` de cada archivo. El nombre puede llevar subcarpeta `es/` o `en/` |

#### Cómo las consume el front (`src/test/httpCohort.ts`)

- **`httpCohort(nombre)`** resuelve la carpeta: por defecto `src/test/fixtures/<carpeta>`, o la ruta que diga la
  variable `*_INPUT` declarada para esa familia. Eso permite calificar una salida nueva del productor sin copiarla.
- **Devuelve estas funciones:**
  - `bytes(archivo)` y `json(archivo)` leen la captura;
  - `response(archivo, status, headers)` arma un `HttpResponse` de MSW con **los mismos bytes**;
  - `capturedResponse(archivo, companion)` usa el companion `.http.json` del productor (`status`, `contentType`,
    `contentLanguage`, `contentDisposition`) para servir binarios PDF y XLSX con sus encabezados reales;
  - `verify()` parsea el manifiesto y controla:
    - que `artifactSet` coincida;
    - que no haya nombres repetidos;
    - que cada captura tenga el tamaño y el SHA256 declarados;
    - que la carpeta contenga **exactamente** las capturas más `provenance.json`, recorrida en profundidad. Un archivo
      de más o un byte distinto hace fallar.
- **`stage4HttpProvenance.test.ts`** verifica las 22 familias y exige que todas compartan `runId`, `repositoryHead`,
  `generatedAtUtc`, `sourceState` y `command`, y que el comando tenga un solo `dotnet test`.
- **`src/test/fixtures/whatsappFormsHttp.ts`** es un lector aparte para `whatsapp-forms-http`, usado por las pruebas de
  página del chat. Respeta `WHATSAPP_FORMS_HTTP_CONTRACT_INPUT`.
- **Lecturas directas:** algunas pruebas de página y escenas visuales sirven cuerpos de cohortes sin modificarlos:
  - `FormsPage.test.tsx`, `FormPublicationBudgetHttp.test.tsx`, `fileQuestions.test.tsx` y `catalogQuestion.test.ts`
    leen `forms-http`;
  - `CatalogsPage.http.test.tsx` y `CatalogListTable.http.test.tsx` leen `manual-current`;
  - `CatalogEditorPage*.test.tsx` y `useCatalogQueries.test.tsx` leen `filters`;
  - en lo visual, `src/test/visual/formFixtures.ts`, `fixtures.ts` y `catalogFixtures.ts`.
- **`httpCohort.test.ts`** prueba el helper con cohortes **sintéticas temporales**, planas y anidadas, para 17 familias.
  También comprueba el rechazo de un byte cambiado y de un archivo no listado. Esas capturas sintéticas se escriben en
  `tmpdir` y se borran: no son capturas del backend.

#### Cuándo se corren y qué está prohibido

- **Siempre corren.** Los consumidores son parte de `npm test` (`vitest run`; la configuración solo excluye
  `.claude/**`, `.test-artifacts/**` y `src/test/visual/**`) y leen las capturas commiteadas.
- **Contrato nuevo:** el guard se escribe contra el contrato y el consumidor **queda rojo hasta el export real**. No se
  crea ni se completa un cuerpo para que pase: las guías dicen «se ejecutan sólo después del export final».
- **Prohibido:**
  - fabricar cuerpos o serializar DTO a mano y presentarlos como respuesta del productor;
  - normalizar bytes o finales de línea;
  - completar propiedades ausentes en capturas viejas;
  - agregar capas de compatibilidad en el guard para aceptar formas viejas.
- **Bytes exactos en git:** `.gitattributes` marca casi todas las carpetas con `-text whitespace=cr-at-eol`, para que
  `core.autocrlf` (activo en este repo) no altere el CRLF de los cuerpos.
- **Qué acreditan:** controller, serializer y guards. **No** acreditan Meta, Azure, el modelo de IA ni el navegador.

#### Las 22 familias en `982ade8`

Todas salen del run `20261023-064235-5542f7f13d844d5985f55c4142c9385f`, backend `7e5b7a94`, generado el
`2026-10-23T07:21:40Z` y commiteado en `e4db46c`. «Capturas» cuenta los archivos del manifiesto: JSON, XLSX, PDF y
companions `.http.json`.

| Familia (`httpCohort`) | Carpeta | Variable de entrada | Productores principales del back | Capturas | Consumidores | Etapa |
| --- | --- | --- | --- | --- | --- | --- |
| `forms` | `forms-http` (`published` 13, `failed` 13, `languages` 18, `recipient-filters` 12, `budget-http` 2, `multiple-choice` 2) | `FORMS_HTTP_CONTRACT_INPUT` | `FormHttpContractFixturesTests`, `FormPublicationCheckTests`, `FormPublicationLanguageTests`, `FormRecipientFilterIntegrationTests`, `FormMultipleChoiceAuthoringHttpTests` y otros | 60 | `api/httpContract.test.ts`, `multipleChoiceHttpContract.test.ts`, `recipientPreviewHttpContract.test.ts` + lecturas directas | Etapa 1, I1–I3, elegir varias, E2 |
| `lifecycle` | `forms-lifecycle-http` (es/en) | `FORM_LIFECYCLE_HTTP_CONTRACT_INPUT` | `FormDeactivationHttpTests`, `FormDeletionHttpTests`, `FormRetirementStatusHttpTests` | 30 | `lifecycleHttpContract.test.ts` | Ciclo de vida |
| `personData` | `forms-person-data-http` | `FORM_PERSON_DATA_HTTP_INPUT` | `FormPersonDataHttpTests` | 24 | `personDataHttpContract.test.ts` | E2 |
| `catalogImports` | `catalogs-http/imports` (es/en) | `FORM_CATALOG_IMPORT_HTTP_INPUT` | `FormCatalogImportHttpTests`, `FormCatalogImportFieldsHttpTests`, `FormCatalogEndpointsTests` | 30 (incluye 4 XLSX) | `catalogImportsHttpContract.test.ts` | E1 (+ campos E2) |
| `manualCatalogs` | `catalogs-http/manual-current` | `FORMS_CATALOG_HTTP_CONTRACT_INPUT` | `FormCatalogEndpointsTests` (+ `.Fields`), `FormCatalogValueHttpTests` | 41 | `catalogCurrentHttpContract.test.ts`, `catalogValueHttpContract.test.ts` + lecturas directas | K3, E1, E2 |
| `catalogFilters` | `catalogs-http/filters` | `FORMS_CATALOG_FILTERS_HTTP_CONTRACT_INPUT` | `FormCatalogFiltersEndpointsTests` | 8 | `catalogFilters.test.ts` + lecturas directas | K4 |
| `catalogSource` | `catalogs-http/k6-source` | `FORMS_CATALOG_SOURCE_HTTP_CONTRACT_INPUT` | `FormPublicationCheckTests` | 11 | `catalogQuestionHttpContract.test.ts` | K6 |
| `formFiles` | `form-files-http` | `FORM_FILES_HTTP_INPUT` | `WhatsAppFormFilesIntegrationTests` | 5 | `formFileDraftHttpContract.test.ts`, `whatsapp/api/formFilesHttpContract.test.ts` | 2B |
| `whatsappCatalogs` | `whatsapp-catalogs-http` (es/en) | `WHATSAPP_CATALOG_HTTP_CONTRACT_INPUT` | `WhatsAppFormHttpContractTests`, `WhatsAppCatalogRecipientSendingTests`, `WhatsAppFormSendingTests` | 30 | `whatsapp/api/catalogsHttpContract.test.ts` | K7, E2 |
| `responses` | `forms-responses-http` | `FORMS_RESPONSES_HTTP_CONTRACT_INPUT` | `FormResponseHttpTests`, `FormOverviewHttpTests`, `FormRecordNavigationHttpTests`, `FormResponseStateHttpTests`, `FormMultipleChoiceReadHttpTests`, `FormCatalogFieldsReadHttpTests` | 55 (incluye 11 XLSX) | `responseHttpContract.test.ts`, `overviewHttpContract.test.ts`, `recordNavigationHttpContract.test.ts`, `responseCatalogFieldsHttpContract.test.ts`, `multipleChoiceHttpContract.test.ts` | Etapa 3, MF1, D2, elegir varias, CE5 |
| `rules` | `forms-rules-http` | `FORMS_RULES_HTTP_CONTRACT_INPUT` | `FormRuleHttpTests` | 8 | `rulesHttpContract.test.ts`, `components/FormRulesSection.test.tsx` | G1 |
| `missingRequests` | `forms-missing-requests-http` | `FORMS_MISSING_REQUEST_HTTP_CONTRACT_INPUT` | `FormMissingRequestHttpTests` | 6 | `missingRequestsHttpContract.test.ts` | H7 |
| `suggestions` | `forms-suggestions-http` | `FORMS_SUGGESTIONS_HTTP_CONTRACT_INPUT` | `FormSuggestionHttpTests` | 8 | `suggestionsHttpContract.test.ts` | s1–s3 |
| `suggestionDetails` | `forms-suggestion-detail-http` | `FORMS_SUGGESTION_DETAILS_HTTP_CONTRACT_INPUT` | `FormSuggestionCaptureIntegrationTests` | 2 | `suggestionDetailHttpContract.test.ts` | s4 |
| `catalogProposals` | `forms-catalog-proposals-http` | `FORMS_CATALOG_PROPOSALS_HTTP_CONTRACT_INPUT` | `FormCatalogProposalHttpTests` | 6 | `catalogProposalHttpContract.test.ts` | H4 |
| `workflow` | `forms-workflow-http` | `FORMS_WORKFLOW_HTTP_CONTRACT_INPUT` | `FormWorkflowHttpTests`, `FormDetailMomentsHttpTests`, `FormWorkflowEndToEndHttpTests` y otros | 32 | `workflowHttpContract.test.ts`, `pdfHttpContract.test.ts` | G5, D2 |
| `approvals` | `forms-approvals-http` | `FORMS_APPROVALS_HTTP_CONTRACT_INPUT` | `FormApprovalHttpTests`, `FormApprovalContinuationHttpTests` | 17 | `approvalsHttpContract.test.ts`, `approvalContinuationHttpContract.test.ts` | G3/G4, MF1 |
| `pdf` | `forms-pdf-http` | `FORMS_PDF_HTTP_CONTRACT_INPUT` | `FormPdfHttpTests`, `FormMultipleChoiceReadHttpTests` y otros | 32 (incluye 9 PDF) | `pdfHttpContract.test.ts`, `multipleChoiceHttpContract.test.ts` | G2, elegir varias |
| `whatsappAssistant` | `whatsapp-assistant-http` | `WHATSAPP_ASSISTANT_HTTP_CONTRACT_INPUT` | `WhatsAppAssistantHttpTests` (+ `.Suggestions`) | 12 | `whatsapp/api/assistantHttpContract.test.ts` | H1–H3 |
| `whatsappActions` | `whatsapp-form-actions-http` | `WHATSAPP_FORM_ACTIONS_HTTP_CONTRACT_INPUT` | `WhatsAppFormActionHttpTests` | 3 | `whatsapp/api/formActionHttpContract.test.ts` | G6 |
| `whatsappAudio` | `whatsapp-audio-http` | `WHATSAPP_AUDIO_HTTP_CONTRACT_INPUT` | `WhatsAppChatTests.Audio` | 3 | `whatsapp/api/audioHttpContract.test.ts` | H6 |
| `whatsappForms` | `whatsapp-forms-http` (es 34, en 42) | `WHATSAPP_FORMS_HTTP_CONTRACT_INPUT` | `WhatsAppFormHttpContractTests`, `WhatsAppFormSendingTests`, `WhatsAppFormReplyAppPreviewTests`, `WhatsAppFormPrivacyTests`, `WhatsAppMultipleChoiceCompositionTests` | 76 | `whatsapp/api/httpContract.test.ts` + `whatsappFormsHttp.ts` (pruebas de página del chat) | C1–C5, R1–R3, idioma original, elegir varias |
| **Total** | | | | **499** | | |

**Nueve familias son nuevas desde `9261e3d`:** `lifecycle`, `personData`, `catalogImports`, `missingRequests`,
`suggestions`, `suggestionDetails`, `catalogProposals`, `whatsappAssistant` y `whatsappAudio`. El resto ya existía como
carpeta y ahora pasa por el helper con manifiesto común. `src/test/fixtures` pasó de 255 a 538 archivos.

**Cohorte Manual histórica.** `catalogs-http/manual` conserva 15 cuerpos de `FormCatalogEndpointsTests` del 2026-10-12.
Su manifiesto propio está en `catalogs-http/provenance.json`, con `artifactSet` `manual-bilingual` y backend
`d687e34c`, y lo consume `src/features/business-forms/api/catalogHttpContract.test.ts`. Es **inmutable** y queda como
fixture de rechazo: el guard actual tiene que rechazar esos cuerpos porque les faltan propiedades que hoy son
obligatorias. El test lo comprueba con `filters` en el detalle y el guardado, `value` en los ítems y las facetas por
origen en el listado. No se regenera ni se atribuye a otra corrida.

#### Exports conjuntos del período

Cada fila reemplazó en bloque las cohortes declaradas en su momento.

| Commit | Fecha | Asunto | Archivos de fixtures |
| --- | --- | --- | --- |
| `f5b6978` | 10-18 | actualizar las capturas HTTP del cierre de la etapa 5 | 299 |
| `34901a4`, `36ae564`, `d6edb56`, `db740a2` | 10-19 | renovaciones por familia, incluida la preparación de idioma | 61 / 101 / 26 / 114 |
| `086d6ae` | 10-19 | renovar los contratos HTTP de idiomas | 322 |
| `b1a5b90`, `44e464b` | 10-19 | contratos reales de idioma original; cierre de formularios | 75 / 254 |
| `91cfd6e`, `a74aca3` | 10-21 | export de cierre y cohorte de baja | 328 / 29 |
| `15ff4d5`, `d20697e` | 10-21 | ciclo de vida renovado y export conjunto verificado | 29 / 328 |
| `4099d36` | 10-21 | menú y navegación con el export completo | 375 |
| `5fa0bc0` | 10-21 | capturas conjuntas del review de menú | 378 |
| `0f478f6` | 10-22 | export completo de E1 | 431 |
| `a23c83b` | 10-22 | cohortes del review de catálogos E1 | 429 |
| `e4db46c` | 10-23 | renovar juntas las cohortes de catálogos E2 | 500 |

No hay commits de fixtures después de `e4db46c` dentro de `9261e3d..982ade8`.

#### Fixtures que no son cohortes: los dobles

Son datos **sintéticos de comportamiento**. Cada uno lo declara en un comentario y ninguno se presenta como respuesta
del productor:

- `src/test/responseFixtures.ts`: planilla y detalle con identidades fijas, para pruebas de comportamiento de
  respuestas.
- `src/test/responseAssistanceFixture.ts`: asistencia histórica y cadena de sugerencias (s4).
- `src/test/catalogImportFixtures.ts`: vista previa de importación con un error y una baja.
- `src/test/mocks/catalogProposal.ts`: propuesta H4 «que coincide con los DTO actuales».
- `src/test/mocks/handlers.ts`: handlers MSW comunes; en el período solo perdió una línea.
- `src/test/visual/*Fixtures.ts`: 15 archivos que alimentan el fetch simulado del catálogo visual. Siete son nuevos:
  `catalogExtraFieldsFixtures`, `catalogImportVisualFixtures`, `catalogQuestionFixtures`, `formOverviewFixtures`,
  `formResponseFullFixtures`, `missingRequestFixtures` y `personDataFixtures`.

**La regla que los separa:** un doble prueba comportamiento; la calificación del contrato la hacen solo las cohortes.
Los dobles visuales nuevos **pasan por los guards reales**:

- `src/test/formResponseFullFixture.test.ts` lee las respuestas del doble de DC1 con `readFormOverview`,
  `readApprovalTask` y `readResponseWorkflow`;
- `src/test/catalogImportVisualFixtures.test.ts` lee la escena tipada con `readCatalog` y `readCatalogImportPreview`;
- `7318153` («validar semillas visuales con el contrato real») hace lo mismo con las de propuestas.

Así un doble no puede alejarse del contrato sin que una prueba falle.

#### Hallazgos sobre las cohortes en `982ade8`

- **Captura vieja frente a un guard nuevo.** `catalogs-http/manual-current/fields-conversion-error.json`, de la corrida
  de `e4db46c`, trae `invalidRows: [{ itemId, code: "ONE" }]` sin `row`, `value` ni `reason`.
  - El guard que endureció `ff28cbc` exige los tres (`src/features/business-forms/api/catalogFieldFeedback.ts:10`).
  - El caso «reads the row, attempted value and reason…» de
    `src/features/business-forms/api/catalogCurrentHttpContract.test.ts:65-68` espera `row: 1`, `value: "1"` y
    `reason: "invalid"`.
  - Por lectura, sin correrlo, ese test no puede pasar hasta que el backend vuelva a exportar. Es el caso previsto del
    consumidor que queda rojo hasta el export, pero hoy convive con el código commiteado.
  - La sección 9 lo detectó primero.
- **Contrato del back que ya cambió.** Las secciones 9 y 10 informan que el back `84289eef`, posterior a `982ade8`,
  suma `questionId` a cada uso de un campo, y que el guard estricto del front en `982ade8` todavía no lo acepta. No lo
  verifiqué contra un cuerpo exportado.
- **Conteos viejos en la guía.** `src/test/AGENTS.md` todavía dice:
  - que `forms` exige 48 cuerpos (hay 60);
  - que `manualCatalogs` tiene 27 (hay 41);
  - que `whatsappCatalogs` tiene 26 (hay 30);
  - que `catalogImports` tiene 24 cuerpos dentro de una procedencia de 21 cohortes. Hoy son 30 capturas (22 cuerpos
    JSON, 4 XLSX y 4 companions) dentro de 22 cohortes.
  
  El código manda; la guía quedó atrás.
- **Carpetas sin `-text` en `.gitattributes`.** `forms-person-data-http` y `catalogs-http/imports` no tienen la marca.
  Hoy no hay daño: sus JSON no tienen saltos de línea y git detecta los XLSX como binarios. Pero no siguen la
  convención del resto.

---

### 11.4 Pruebas

#### Vitest

- **Archivos `*.test.ts(x)` fuera de `visual`:** 185 en `9261e3d` → **307** en `982ade8`. Los de contrato o HTTP real
  (`*HttpContract`, `*Http.test`, `*.http.test`) pasaron de 17 a 36.
- **Configuración (`vite.config.ts`):** `maxWorkers: 1`; excluye `.claude/**`, `.test-artifacts/**` y
  `src/test/visual/**`. `npm run lint` corre `oxlint` y además `vitest run src/test/architecture`.
- **Regla de cierre** (`4c568fd`, `src/test/AGENTS.md`): una corrida completa que falla se corrige y se repite; vale la
  última en verde.

#### Arquitectura (`src/test/architecture`)

- **Cuatro archivos:** `designArchitecture.test.ts`, `designRules.test.ts`, `designRules.ts` y `featureDependencies.test.ts`.
- **Qué controlan:** que no haya imports entre features ni de `shared` hacia features (con el AST de TypeScript), los
  límites de uso del sistema visual y el montaje único del Toaster.
- **Único cambio del período:** `featureDependencies.test.ts` reemplaza `./formsNavigation` por
  `./formsOverviewNavigation` en el caso que prueba que `layouts` no depende de `app` (`3e359a8` y los commits de MF1).
- Los cierres reportan **49/49** controles.

#### Visuales (Playwright, `src/test/visual`)

- **Configuración (`playwright.config.ts`):** Chromium, `locale es-AR`, zona `America/Argentina/Buenos_Aires`, tema
  claro, movimiento reducido, 3 workers y `toHaveScreenshot` con `maxDiffPixelRatio: 0.002`. Las referencias están en
  `src/test/visual/screenshots/`.
- **`server.mjs`** compila catálogo, sesión y SPA en modo desarrollo dentro de `.test-artifacts/visual-app` y sirve en
  el puerto 5178. Cambio del período (`0ad49f3`): agrupa `node_modules` y `src/shared` en un chunk común
  `visual-shared`, para reducir pedidos iniciales; en Windows se veía `ERR_NO_BUFFER_SPACE`.
- **`preview.tsx`** (`64eac1b`) expone `window.visualRouterLocation` con `pathname` y `search` del MemoryRouter real,
  así las escenas verifican la ruta sin leer `history.state` ni el workspace privado.
- **`Catalog.tsx`** suma las variantes `success`, `warning` y `danger` de `Badge`, el escenario `?scenario=tabs` y el
  escenario `?scenario=mobile-dialog`.
- **`CatalogComponentsPreview.tsx`** solo se adaptó a `valueType` y `value`.
- **Specs:** pasaron de 34 a **48**. Las 14 nuevas son `catalogExtraFields`, `catalogLoading`, `catalogMultipleChoice`,
  `catalogQuestionFields`, `catalogResponseFields`, `catalogValues`, `formLanguages`, `formLifecycle`, `formOverview`,
  `formResponseFull`, `forms-missing`, `personData`, `responses-assisted` y `whatsapp-audio`. La tabla por área está en
  `src/test/visual/AGENTS.md`.
- **Referencias:** de 126 a **127**. Cambios del período:
  - `7ecabd6` agregó `whatsapp-manual-390.png`;
  - `d580aef` («actualizar referencias de modos y estados aprobados», sin cuerpo explicativo) reemplazó 11:
    `catalog-320` y `catalog-en-390`, que son el **catálogo de UI**, no catálogos de formularios, y las de `whatsapp*`
    por el modo Manual que reemplazó a Bot.
- **Reparto de las 127 en `982ade8`:** 72 de `assistant` (Inicio), 14 de `whatsapp`, 7 de `appearance`, 6 de `ai`, 6
  de `business`, 6 de `landing`, 3 de `catalog` (catálogo de UI), 3 de `settings` y el resto de usuarios, roles, perfil
  y login. **Ninguna es de Formularios ni de Catálogos.** Las specs de esas áreas solo hacen aserciones de geometría,
  texto, teclado y foco, y guardan `page.screenshot({ path: "test-results/..." })` como evidencia para inspección, sin
  comparación automática. Solo 9 specs usan `toHaveScreenshot`.
- **Reglas** (`src/test/visual/AGENTS.md`):
  - una pantalla nueva cubre es/en a 1440, 390 y 320 px;
  - actualizar una referencia requiere una decisión visual autorizada;
  - cada familia de escenas concede solo sus permisos;
  - una escena que usa una cohorte la renderiza sin modificar sus bytes;
  - un doble no acredita PostgreSQL, Redis, Azure, Meta ni el modelo;
  - `npm run test:visual` va una vez, al cerrar la tarea.

#### Conteos de cierre que reportan los docs

No los volví a ejecutar.

| Cierre | Tests Vitest | Visual | Cohortes / capturas | Fuente |
| --- | --- | --- | --- | --- |
| Etapa 5 (2026-10-17) | 2.245/2.245 en 227 archivos | 511/511 | 19 / 295 (53 fuentes) | [diseño](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#implementación-local-de-etapa-5--2026-10-17) |
| Idioma I1–I3 (2026-10-19) | 2.355/2.355 en 228 | 527/527; 127 referencias | 19 / 318 (54 fuentes); 79 tests de contrato | [diseño](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#cierre-local-de-idioma-original-y-traducción-opcional--2026-10-19) |
| Ciclo de vida (2026-10-21) | 2.479/2.479 en 233 | 567/567 | 20 / 354 (63 fuentes); 109 tests de contrato | [diseño](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#cierre-local-de-baja-reactivación-y-eliminación--2026-10-21) |
| Review de baja (2026-10-21) | 2.511/2.511 en 237 | 585/585 | 20 / 356 | [diseño](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#verificación-posterior-al-review-de-baja--2026-10-21) |
| Menú y detalle (2026-10-21) | 2.689/2.689 (review) | 617/617, luego 623/623 | — | [diseño](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#cierre-local-de-menú-y-detalle--2026-10-21) |
| Catálogos E1 (2026-10-22) | 2.959 | 677 | — | [diseño](../../../../ArquitecturaBaseFront/docs/design/catalogos-mejoras.md) |
| Catálogos E2 (2026-10-23) | 3.116/3.116 en 309 | 719/719 | 22 / 499 | [back](../../features/formularios.md#verificación-local-de-e2--2026-10-23) |

Los commits posteriores al review E2 (`c2572b8` a `982ade8`) no tienen un cierre registrado en `982ade8`.

#### Paridad de idiomas (`src/locales/parity.test.ts`)

Lee todos los JSON con `import.meta.glob`. Exige los mismos namespaces y las mismas claves en es y en, y fija algunos
plurales. En el período sumó el plural de la espera para volver a escuchar un audio, `whatsapp:audio.waiting`
(`622b5e5`): «Esperá 1 segundo para volver a escuchar.» y «Esperá 2 segundos…».

---

### 11.5 i18n

- **Estructura sin cambios:** un JSON por namespace y por idioma en `src/locales/{es,en}/`. Son 11 namespaces:
  `artificialIntelligence`, `assistant`, `auth`, `businessInformation`, `common`, `forms`, `profile`, `roles`,
  `settings`, `users` y `whatsapp`. No se agregaron namespaces: todo lo de formularios y catálogos vive en `forms`, y lo
  del chat, en `whatsapp`.
- **Tono:** español rioplatense con voseo; el inglés lleva la misma estructura de claves.
- **Tamaños:**

| Namespace | Claves (es) `9261e3d` → `982ade8` | Bytes es | Bytes en |
| --- | --- | --- | --- |
| `forms` | 636 → **1.207** | 34.648 → 67.655 | 32.707 → 64.765 |
| `whatsapp` | 169 → 211 | 10.524 → 13.596 | 9.817 → 12.791 |
| `common` | 113 → 114 | 5.366 → 5.406 | 5.148 → 5.165 |

`git diff --stat` da +647 líneas en cada `forms.json`.

#### `forms.json`: grupos de primer nivel nuevos

| Grupo | Para qué |
| --- | --- |
| `offerWhen`, `offerWhenHint`, `offerWhenEmpty` | «Cuándo lo ofrece el asistente» en «El formulario» (sección 3) |
| `suggestions` | Editor «Qué sugerir después» y su historial (s1–s3) |
| `missing` | Formularios que faltan (H7) |
| `detail` | D2 y DC1: pantalla completa, plegables, pestañas Fotos/Actividad/PDF, momentos, asistencia (sección 5) |
| `overview` | MF1: tabla, pendientes, dados de baja, navegación de la cola |
| `lifecycle` | Dar de baja, reactivar y eliminar (sección 6) |
| `active`, `deactivated`, `responseCountColumn` | Segmentado y columna del listado (BA1-A) |
| `languageComplete`, `contentLanguageHint`, `previewForAccount`, `previewFallback_one/_other` | Idioma original y vista previa por cultura (I1) |
| `multiple` | Elegir varias: rango, confirmación de cambio de tipo |
| `personData` | Datos de las personas y la sección en «Editar usuario» (CE4-A) |

- **Se retiraron:** `sharedStructure` y `optionsMissing_one/_other`.
- **Crecieron:**
  - `catalogs`, de 115 a 355 claves. Suma `fields`, `presentation`, `proposal`, `valueTypes`, `valueType`, `import`,
    `preview`, `actions` y `usageHeader_*`;
  - `publication`, de 84 a 109. Suma `languages`, `cultureAdded/Removed`, `completeCulture`, `blockingIssues`,
    `nonBlockingWarnings`, `recipientMaximum*`, `recipientsMissingData_*` y otras; se retiró `bilingual`;
  - `catalogQuestion`, de 12 a 46: `filter`, `presentation`, `preview` y `review`;
  - `rules`, de 65 a 80: `saveUnchanged`, `saveInvalid`, `saveConflict`, `saveBusy` y el historial de cambios;
  - `types`, que suma `multiChoice`; `workflow`, que suma `delivered` y `read`; `responses`, que suma `anyOf`.
- **`approvals` bajó de 32 a 24 claves:** se retiraron `title`, `scope`, `mine`, `all`, `form`, `allForms`, `waiting`,
  `empty` y `back` con la bandeja global, y se sumó `loadFailed`.

> **No copiar — regla de interfaz del 2026-10-23** ([visual-baseline](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), commit `bb98bf2`): `forms.json` todavía tiene las claves que la regla retira de las pantallas, todas usadas en `982ade8`:
> - `dirty` («Cambios sin guardar») y `uncertain` («Guardado sin confirmar») como estado junto al título;
> - `rules.revision` («Revisión N») al lado de Guardar;
> - `rules.saveUnchanged`, `rules.saveInvalid`, `rules.saveBusy` y `rules.saveConflict`, que son los motivos que se
>   muestran como texto debajo del botón.
>
> Al replicar, los motivos que no sean obvios van a un tooltip, y el estado de guardado no se muestra.

#### `whatsapp.json`

- **Grupos nuevos:** `assistant` (sugerencia y prellenado H2, «Por qué lo mandó», respuestas rápidas), `audio`
  (escuchar, transcripción y sus estados, `waiting_*`) y `readForbidden`.
- **`mode`:** pasó de `Bot`/`Manual` a `Assistant`/`Manual`, con la activación, la disponibilidad, el envío iniciado y
  la descripción de cada modo. **Bot desapareció.**
- **Otros:** `kind` suma `Audio`; `forms` suma `preparing` y `preparationFailed`.

#### `common.json`

- `navigation.forms` pasó a «Gestión de formularios».
- Se sumaron `navigation.formOverview` («Formularios»), `navigation.missingForms` («Formularios que faltan»),
  `navigation.personData` («Datos de las personas») y `actions.view` («Ver»).
- Se retiraron `navigation.allResponses`, `allResponsesCount` y `approvals`, de la navegación dinámica y la bandeja
  anteriores.

---

### 11.6 Documentación del front que cambió

#### Mapa e instrucciones por carpeta

- **[Mapa de instrucciones](../../../../ArquitecturaBaseFront/docs/mapa-de-instrucciones.md)** (`1fab63b`, `0c72a76`):
  pasó de **27 a 44 ámbitos**, cada uno con `AGENTS.md` y un `CLAUDE.md` que importa `@AGENTS.md`. Explica el motivo:
  Codex combina las guías desde la raíz hasta la carpeta con un tope de 32 KiB, así que cada cadena queda por debajo de
  30 KB. La guía de un área pasa a ser un índice corto y el detalle baja a la subcarpeta (receta del back:
  [tamaño de las guías](../../guides/instrucciones-por-carpeta.md#tamaño-de-las-guías)).
- **División de guías (`1fab63b`).** `src/features/business-forms/AGENTS.md` bajó de 28 KB a un índice de 4 KB, y
  `whatsapp/AGENTS.md`, de 9,5 KB a 4 KB. Guías nuevas:
  - `business-forms/api`, `hooks`, `components` y `pages`;
  - `whatsapp/api`, `components`, `hooks` y `lib`;
  - `layouts/components`.

  Esas guías son la mejor fuente de reglas por capa del front; las secciones 3 a 10 las citan.
- **[Arquitectura](../../../../ArquitecturaBaseFront/docs/architecture.md)** (`6521d05`): reescribe «Áreas con guía
  propia».
  - WhatsApp: modo y origen Assistant explícitos, tomar Manual con Send, activar Assistant con ConfigureAssistant, audio.
  - Formularios: MF1 por Read o Decide, D2 y DC0/DC1 con un único dueño, OfferWhen, publicación con original completo y
    traducción opcional, catálogos propuestos con original obligatorio y catálogos comunes bilingües.
- **Guías de capas:** `src/app/AGENTS.md`, `src/layouts/AGENTS.md`, `src/shared/*/AGENTS.md` y `src/auth/AGENTS.md`
  suman las reglas de las piezas de 11.1 y 11.2.

#### [Fundamento visual](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md)

**`bb98bf2`, 2026-10-23.** Reglas nuevas, transcriptas:

- **«Encabezados», pantalla hija:**
  > `status` acompaña al título sin formar parte del nombre y muestra solo el estado del objeto, como «Activo» o «Dado de baja». No muestra «Cambios sin guardar» ni números de revisión: que Guardar esté habilitado ya dice que hay cambios.
- **«Formularios y diálogos»** (se mantiene «Se valida al enviar; después del primer error, en vivo mientras se
  corrige»):
  > - Al guardar, el botón se deshabilita mientras trabaja, sin tapar la pantalla ni perder lo escrito.
  > - **Un botón está habilitado o deshabilitado, sin explicaciones al lado** (decisión del usuario, 2026-10-23):
  >   - no se agregan textos debajo ni al costado, como «No hay cambios para guardar», «Revisá los datos marcados antes de guardar» o «Guardá … antes»;
  >   - los botones no cambian de lugar ni de forma con cada acción;
  >   - si el motivo de un deshabilitado no es obvio, va como tooltip al pasar el mouse o con el foco;
  >   - los errores se marcan en sus campos.
  > - **Las acciones de una superficie van al borde derecho de su encabezado,** a la altura del título.

  Reemplazó a «Al guardar, el botón se deshabilita y explica el estado».

**Otros cambios del período en el mismo documento:**

- el chat: Atención manual con Send, Asistente con ConfigureAssistant, autoría «Asistente» y «Automático»;
- formularios: I1–I3, D2, MF1 y DC0/DC1;
- en «Pantallas por completar», las filas de Casos del asistente, Mejoras de catálogos, Campos extra y Documentos;
- las secciones fechadas «Menú y detalle de formularios — 2026-10-21» y «Catálogos E2 — 2026-10-23».

> **No copiar:** las pantallas de `982ade8` todavía violan estas reglas en los lugares que lista el punto 38 del [review E2](../../reviews/2026-10-23-code-review-catalogos-e2.md). Cada sección lo marca en su pantalla.

#### Otros documentos

- **[Estándar visual](../../../../ArquitecturaBaseFront/docs/guides/estandar-visual.md)** (`fbb17cf`): suma la fila
  «Decisión extensa en móvil» (`DialogContent mobileFullscreen`), el escenario `?scenario=mobile-dialog` y la nota de
  que los diálogos de baja, reactivación y eliminación usan esa variante.
- **Diseños de etapa nuevos:**
  [catálogos-mejoras](../../../../ArquitecturaBaseFront/docs/design/catalogos-mejoras.md) (E1, sección 8) y
  [catálogos-campos-extra](../../../../ArquitecturaBaseFront/docs/design/catalogos-campos-extra.md) (E2, secciones 9 y 10).
  [formularios-whatsapp](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md) creció de 48 a 83 KB,
  con I1–I3, BA1–BA6, MF1, DC0/DC1 y los cierres fechados.
- **Diseños solo dibujados, no programados en `982ade8`:**
  - [Casos del asistente](../../../../ArquitecturaBaseFront/docs/design/asistente-casos.md) (`86f3b26`, AS1-A y
    AS2–AS6): pantalla «Asistente» en el panel de WhatsApp, con pestañas Perfil, Conocimiento, Casos y Límites, panel
    «Probar», casos propios y de formularios, derivar a una persona y el porqué de cada acción en el chat.
  - [Documentos por WhatsApp](../../../../ArquitecturaBaseFront/docs/design/documentos.md) (`2fec976`, `090aeb2`,
    DOC1–DOC4, DOC5-A, DOC6 y DOC7-A):
    - «Biblioteca» de formularios estándar y la sección «Lectura de documentos» en el editor;
    - lectura con IA de comprobantes por WhatsApp, con prellenado y validaciones fijas;
    - «Documentos recibidos» como planilla con panel al costado;
    - armar un formulario con la IA y un tablero que se arma solo.

---

### 11.7 Commits del período que no caen en una etapa

**Piezas transversales descritas en esta sección:**

| Commit | Asunto |
| --- | --- |
| `978a8d1` | feat(ui): agregar pestañas y selección accesible de filas |
| `9510b05` | feat(ui): compartir secciones plegables accesibles |
| `89462fd` | refactor(ui): sumar tabla ancha con desplazamiento accesible |
| `fbb17cf` | feat(ui): permitir diálogos completos en móvil |
| `be0824e` | fix(ui): conservar el ancho del texto en avisos con acción |
| `0d9a9f1` | fix(ui): conservar ancho del nombre de adjuntos |
| `bf4440b` | feat(http): enviar archivos con FormData y sesión compartida |
| `3e359a8` | refactor(auth): compartir alternativas de permisos entre rutas y menú |
| `0ad49f3` | test(visual): agrupar los módulos comunes del catálogo |
| `400f342` | test(formularios): verificar las siete cohortes HTTP |
| `d58e7ee` | test(front): exigir contratos vigentes de formularios y catálogos |
| `d580aef` | test(ui): actualizar referencias de modos y estados aprobados |
| `1fab63b` | docs(guías): dividir las guías de formularios y WhatsApp por carpeta |
| `4c568fd` | docs(tests): alinear las corridas fallidas con la regla de cierre |
| `0c72a76` | docs: enlazar las instrucciones históricas del laboratorio |
| `6521d05` | docs(front): consolidar formularios y asistente implementados |
| `bb98bf2` | docs(diseño): botones sin explicaciones ni estados de guardado |
| `86f3b26` | docs(asistente): sumar los tableros elegidos de los casos del asistente |
| `2fec976` | docs(documentos): anotar los tableros elegidos de Documentos por WhatsApp |
| `090aeb2` | docs(documentos): dejar DOC2 y DOC4 como quedaron en el plan |

**Re-exports de cohortes** (detalle en 11.3): `f5b6978`, `34901a4`, `36ae564`, `d6edb56`, `db740a2`, `086d6ae`,
`b1a5b90`, `44e464b`, `91cfd6e`, `a74aca3`, `15ff4d5`, `d20697e`, `4099d36`, `5fa0bc0`, `0f478f6`, `a23c83b` y
`e4db46c`.

**Docs, evidencia y escenas de una etapa que las otras secciones no citan por hash:**

- Etapa 5: `feb50b5`, `05318ef`, `63accc8`, `9ecf34f`, `4ace3dd`, `e693b7c`.
- Menú y detalle: `f0ebf96`, `64ac0f5`, `4d7088b`, `25af5d6`, `0fc3c41`, `693bfde`, `7b55f9a`, `8a84f78`, `35a538d`,
  `4c550b8`, `74b206b`, `846704d`, `0365ec5`. Todos son evidencia, escenas o docs de MF1, D2 y DC; `f0ebf96` y
  `35a538d` son arreglos chicos de navegación y de traducción.

---

### 11.8 Para el multitenant

- **Piezas de `shared/ui`:** no dependen del tenant y se pueden reconstruir tal cual. Dos se corrigen antes de copiar:
  - `Autocomplete` con la lista en un portal (punto 17);
  - las celdas fijas de `WideTable` con fondo opaco.
  
  Conviene además resolver en `Surface` que las acciones queden a la derecha cuando bajan de renglón, y no agregar
  párrafos con motivos dentro de `actions`.
- **`FormData` y tenant:** si el multitenant identifica al tenant por encabezado o subdominio, ese dato tiene que entrar
  por `send()`, el mismo camino del JSON, para que importar archivos lo lleve, incluso en el reintento después de
  renovar sesión.
- **Permisos con alternativas:** `PermissionRequirement` sirve igual, pero `has` tiene que evaluar los permisos **de la
  membresía activa**: B2B, con varias organizaciones por persona, y B2C, con permisos propios del consumidor. Las claves
  de caché de `/api/me` llevan el tenant.
- **Contrato neutral de la sección de datos:** `userDataSectionContext` es un buen patrón para módulos opcionales por
  tenant (módulo instalado o no). En B2C, ver si los «datos de las personas» aplican a cuentas que no son usuarios
  internos (sección 10).
- **Cohortes:**
  - mantener la corrida única y el manifiesto con SHA;
  - sumar escenarios con **dos tenants** que prueben el aislamiento (404 o 403 cruzados y conteos que no se mezclan),
    con capturas propias;
  - ningún cuerpo exportado debería llevar identificadores de un tenant real;
  - proteger **todas** las carpetas en `.gitattributes`;
  - que la guía de pruebas no fije conteos a mano, porque envejecen (11.3).
- **Dobles:** que pasen por los guards reales, como ya hacen los nuevos (11.3); con tenant, el doble lleva su
  organización explícita.
- **Visuales:** Formularios y Catálogos no tienen referencias golden. Decidir si el multitenant las agrega para las
  pantallas estables (listados, MF1, DC1) o sigue con aserciones de geometría más evidencia inspeccionada. En cualquier
  caso, conservar es/en a 1440, 390 y 320 px y permisos por escenario.
- **i18n:**
  - retirar las claves de estado de guardado y motivos visibles (`dirty`, `uncertain`, `rules.revision`, `rules.save*`);
  - un formateador único por tipo de valor (punto 31);
  - el idioma del contenido no es el de la interfaz: en un B2C con personas en varios idiomas, decidir si los
    namespaces de UI se amplían más allá de es/en (no decidido en este repo);
  - el voseo rioplatense es una decisión de este producto; no está dicho que valga para todos los tenants.
- **Guías para agentes:** el patrón de índices cortos por área, con detalle en la subcarpeta para no pasar los 32 KiB,
  conviene adoptarlo desde el principio.

### 11.9 Endpoints

Ninguno nuevo es transversal. El cambio de transporte (`FormData`) lo usan los endpoints de importación de catálogos de
la sección 8. `/api/me`, que da los permisos, no cambió de forma.

### 11.10 Huecos

- **No corrí builds ni tests.** Que `catalogCurrentHttpContract.test.ts` falle con la captura de `e4db46c` sale de
  leer el guard, la captura y la aserción, no de ejecutarlo. No verifiqué si hubo un export nuevo después de `982ade8`.
- **Sin navegador:** el recorte del desplegable (punto 17), la transparencia de las celdas fijas y la alineación de las
  acciones de `Surface` al bajar de renglón son deducciones del CSS.
- **El `questionId` del back `84289eef`** lo tomo de las secciones 9 y 10; no lo verifiqué contra un cuerpo.
- **Productores del backend:** los saqué del campo `producer` de cada manifiesto y del script de export. No revisé uno
  por uno los tests productores.
- **Conteos de cierre:** son los que registran los documentos, no una corrida mía.
- **Escenas visuales:** conté specs y referencias y leí la guía, pero no cada escena. El detalle de qué recorre cada
  spec está en las secciones de cada etapa.
- **`d580aef`** no explica en su mensaje qué cambió en `catalog-320` y `catalog-en-390`. Coincide con el agregado de las
  insignias semánticas al catálogo de UI (`4f3a922`), pero no lo comprobé comparando imágenes.

---

## 12. Qué conviene revisar al adaptarlo a multitenant

Junta las listas «Para el multitenant» de las secciones 3 a 11 y las ordena por tema. Cada viñeta remite a la
sección donde está el detalle.

### 12.1 Antes de copiar una pantalla

- **Partir de las reglas del 2026-10-23, no de las pantallas de `982ade8`** (1.1 y 1.2). Desde el primer día:
  - botones sin textos al lado, con el motivo en un tooltip;
  - el estado de la cabecera muestra solo el estado del objeto;
  - las acciones van al borde derecho del encabezado de cada superficie;
  - se valida al intentar guardar;
  - no hay selectores de idioma ni pares de campos por idioma: se escribe en el idioma del menú, que es el original, y
    la traducción se carga con el menú en el otro idioma.
- **Decidir antes lo que la regla de idioma no resuelve** (4.10):
  - qué pasa con «Completar el inglés» de la comprobación de publicación;
  - el conteo «N sin traducir»;
  - en qué idioma se ve la vista previa y la vista oficial;
  - desde qué idioma se edita el prefijo.
- **Cambiar de idioma con cambios sin guardar** (4.10): tiene que preguntar o conservar el borrador. Hoy los editores
  se remontan y lo pierden. En multitenant el idioma del menú es por usuario: conviene que ese cambio pase por la misma
  guarda que la navegación.
- **No copiar los errores pendientes del review E2** (1.3), en especial:
  - el contrato del chat de Inicio, que hoy no muestra respuestas (7.9);
  - la confirmación que falta al cambiar el tipo o la fuente de un dato de las personas (10.3);
  - los guards laxos de los avisos de publicar (punto 29);
  - el formateador por tipo repartido (punto 31);
  - lo que falta de los tableros (punto 32).
- **Decidir con el usuario** el motivo visible en el menú «⋯» del editor y el texto rojo de BA6 (6.4 y 6.8): la regla
  funcional del 2026-10-21 los pide visibles y la del 2026-10-23 los manda a un tooltip.

### 12.2 La organización en claves, dueños y limpieza

- **Hoy las claves llevan actor, permisos y cultura, no la organización.** Ejemplos:
  - `["business-forms", actorId, permisos, cultura]`;
  - `["form-catalogs", …]` y `catalogScopeKey`;
  - `["forms-person-data", …]`;
  - `["form-overview" | "form-responses", owner]`;
  - `["assistant", userId]`;
  - las del chat: `chatReadScopeKey`, el dueño de la sugerencia y el del audio.

  En multitenant la organización activa entra en la raíz de cada clave y en cada sello de dueño
  (`useFormPublicationCheck`, `ResponseWorkspaceProvider`, el contador del menú). Ver 3.16, 4.16, 5.14, 6.16, 7.10,
  8.10, 9.14 y 10.10.
- **Cambiar de organización sin recargar** tiene que cancelar las lecturas y retirar todo lo privado, como hoy al
  cambiar de cuenta, permisos o idioma:
  - borradores, filtros privados de la planilla y origen del recorrido;
  - colas de pendientes, blobs de PDF, audio y miniaturas;
  - sugerencias y transcripciones.

  Las páginas que hoy se montan con `key` de actor y cultura suman la organización.
- **Un 403 retira lo que esa lectura mostró**, no alcanza con ocultarlo. Mantener esa regla también para un 403 que
  aparezca por cambio de organización (5.8, 10.4).
- **Invalidaciones acotadas:** una baja, una eliminación o un guardado de catálogo invalidan solo lo de su
  organización (6.10, 8.4).

### 12.3 Permisos

- **Las alternativas de permisos** (`PermissionRequirement`, «alguno de») sirven igual, pero `has` tiene que evaluar los
  permisos **de la membresía activa** (11.2). Las claves de `/api/me` llevan la organización.
- **Lectura y decisión independientes** (5.14): conservar las tres personas, que solo lee, solo decide o ambas, y sus
  proyecciones con null. En B2C, decidir si existe «decidir sin leer». Si no existe, se simplifican el D2 de metadata
  y la pantalla de pendientes propia.
- **Permisos nuevos del período:** `whatsapp.configureassistant`, `formsuggestions.manage` y `formpersondata.manage`.
  `whatsapp.manage` no existe más: no recrearlo como alias (3.16).
- **Publicar y administrar no se implican** (6.16): Dar de baja y Reactivar piden publicar; Editar y Eliminar,
  administrar.
- **Datos de las personas** (10.10):
  - la tabla pide `formpersondata.manage` + `users.read`;
  - cargar valores pide `users.manage`;
  - las opciones de un dato no exigen leer catálogos.

  Mapear todo eso a los roles del multitenant sin que uno implique otro.
- **«En uso» muestra nombres de formularios** a quien no puede leerlos (punto 11 del review, back). Al definir el
  contrato de usos con organización, dar los nombres solo con lectura de formularios.

### 12.4 B2B y B2C

- **Destinatario sin cuenta interna.** Hoy todo supone una «cuenta vinculada» con cultura:
  - analizar y mandar una sugerencia (3.16);
  - la cultura del envío, que decide el servidor (4.16);
  - los datos de las personas, que se cargan en «Editar usuario» (10.10).

  Para un cliente final hay que definir:
  - otra identidad de destinatario;
  - de dónde sale su cultura;
  - dónde y quién carga sus datos;
  - cómo se elige la persona de ejemplo de «Como si lo recibiera».
- **Qué ve un usuario B2C en Inicio** (7.10). Las fuentes del asistente son de administración B2B: usuarios, roles,
  permisos e información del negocio. Para B2C, probablemente Inicio sea el `DashboardPage` o una variante sin esas
  fuentes.
- **Catálogos y propuestas** (3.16, 8.10):
  - las propuestas filtran «la vista Usuarios»: en multitenant es la vista de personas de la organización, y en B2C
    hay que decidir si incluye clientes;
  - no hay nada pensado para un catálogo compartido entre organizaciones.
- **Formularios que faltan** (3.16): los temas y conteos se agregan por organización. La promesa de no guardar nombres
  ni mensajes pesa más con datos de consumidores.
- **Permisos de cara al cliente:** un cliente final probablemente no tenga baja, reactivación ni eliminación
  (6.16).

### 12.5 WhatsApp, Meta y la IA por organización

- **Un número por organización** (3.16): el front asume que la instalación comparte un número. Por organización
  dependen:
  - el modo del chat;
  - la disponibilidad de la IA (`assistantAvailability`: `notConfigured`, `paused`, `modelNotQualified`,
    `notInstalled`);
  - las sugerencias y los audios.
- **Flows y deprecación** (6.16): la deprecación es irreversible y corre sobre la cuenta vinculada. `NeedsAttention`
  aparece, por ejemplo, ante `AccountMismatch`, más probable con una cuenta por organización. El estado del canal se lee
  por organización y no se cruza.
- **Topes que vienen de Meta y no del plan:**
  - 20 opciones en «elegir varias» (CheckboxGroup);
  - 30 caracteres de título y 300 de descripción;
  - 200 en una sola elección.

  Los topes de producto, como 10.000 opciones, 10 MB, 64 columnas o 20 campos, hoy están fijos en el front. Si varían
  por plan, que lleguen del servidor y que los textos los interpolen (8.10, 9.14).
- **Cuotas y esperas por organización** (7.10, 8.10): la capacidad del asistente y los 429 de importación y
  exportación. Hoy el límite de importación es por actor y por proceso, y el front no muestra su espera.
- **El texto que lee la IA** («Cuándo lo ofrece», «Cuándo sugerirlo») es único y sin idioma. Si hay organizaciones con
  otro idioma principal, decidir si sigue el idioma original de la organización (3.16).

### 12.6 Identidades que no pueden cruzar organizaciones

- **Prefijos** (6.16): eliminar es lógico y deja el prefijo reservado para siempre. La unicidad y la reserva van **por
  organización**, y el diálogo no puede mostrar prefijos de otra.
- **Sugerencias y cadena** (3.16, 6.16):
  - los destinos son solo formularios publicados de la misma organización;
  - los enlaces de la cadena s4 no cruzan organizaciones;
  - `unavailableDestinations` no filtra nombres de otra;
  - los mapeos de elección son «mismo catálogo de la misma organización».
- **Catálogos fuente y filtros** (9.14, 10.10):
  - la fuente de un campo es un catálogo Manual activo de la misma organización y distinto del propio; hoy el selector
    no excluye al propio y lo frena el servidor;
  - un dato no apunta a un catálogo de otra organización ni un filtro a un dato de otra.
- **Pendientes por rol vigente** (5.14): el número del menú, «Por aprobar · N», la continuación por TaskId y el anclaje
  autorizan dentro de la organización.
- **Conteos privados** (6.16): `responseCount`, `pendingApprovalCount` y `nextRecordNumber` vienen null sin lectura de
  respuestas. También tienen que venir null si el actor no pertenece a la organización dueña.

### 12.7 Idiomas por organización

- Hoy el conjunto es fijo `es|en` en:
  - los tipos y los guards (`z.enum(["es","en"])`, mapas `{ es, en? }`);
  - los locales y el `SegmentedControl`;
  - `missingFormTexts` y la comprobación por idioma.

  Si cada organización habilita idiomas distintos, todo eso sale de la configuración de la organización (4.16, 9.14).
- El idioma del contenido no es el de la interfaz. Con personas en varios idiomas (B2C), decidir si los namespaces de
  la interfaz se amplían más allá de es/en. No está decidido en este repo (11.5).
- El voseo rioplatense es una decisión de este producto; no está dicho que valga para todas las organizaciones (11.5).
- Quitar las caídas fijas a español:
  - `generateMissingCatalogCodes`;
  - el texto obligatorio de la opción, los guards `requiredText` y `text.es`;
  - las etiquetas de campos y datos.

  Se cae al **original** (8.10, 10.10).

### 12.8 Privacidad y URL

- **Nada privado en la URL ni en storage.** Vale doble con datos de clientes finales:
  - los enlaces compartibles de DC1 llevan solo orden y versiones (5.14);
  - la búsqueda de personas hoy sí va a la URL (`/datos-de-personas?search=`): no copiarlo (10.2).
- **Conservar** (3.16, 8.10, 10.10):
  - `no-store`, `gcTime 0` y nada en MutationCache para los datos de las personas, las sugerencias y el audio;
  - los Object URL revocados al salir;
  - el archivo importado, que nunca se persiste.
- **Cuánto se ve en cada pantalla.** La tabla de personas, los nombres en los avisos de publicar y las muestras de
  «Como si lo recibiera» exponen nombres y valores. Revisar qué ve cada rol en cada organización (10.10).

### 12.9 Contratos, cohortes y pruebas

- **Contratos que se están moviendo al cierre** (9.11, 9.14, 10.3, 11.3):
  - `questionId` en los usos de un campo (back `84289eef`);
  - `confirmValueRemoval` en los datos de las personas (back `95e200c8`);
  - las filas de `FieldTypeNotConvertible`: la captura exportada todavía no trae `row`, `value` ni `reason`.

  Definir esos contratos completos desde el principio, con etapa e identidad de la pregunta en cada uso (punto 25).
- **Cohortes** (11.3):
  - mantener la corrida única del back con manifiesto y SHA256;
  - sumar escenarios con **dos organizaciones** que prueben el aislamiento: 404 o 403 cruzados y conteos que no se
    mezclan;
  - ningún cuerpo exportado lleva identificadores de una organización real;
  - proteger todas las carpetas en `.gitattributes`;
  - no fijar conteos a mano en las guías.

  Reproducir como cuerpos exportados los recorridos que hoy cubren las cohortes:
  - idioma: v1 original, v2 ambos y v3 que pierde un idioma;
  - ciclo de vida;
  - catálogos con campos;
  - filtros por persona;
  - el asistente de Inicio, que hoy no tiene cohorte (7.6).
- **Dobles:** que pasen por los guards reales, como ya hacen los nuevos, y con la organización explícita (11.3).
- **Visuales** (11.4): Formularios y Catálogos no tienen referencias «golden». Decidir si se agregan para las pantallas
  estables, como listados, MF1 y DC1, o si se sigue con aserciones de geometría y evidencia inspeccionada. En los dos
  casos, conservar es/en a 1440, 390 y 320 px y los permisos por escenario.

### 12.10 Componentes y transporte

- **Antes de reconstruir `shared/ui`** (11.1):
  - `Autocomplete` muestra la lista en un portal (punto 17);
  - las celdas fijas de `WideTable` llevan fondo opaco;
  - `Surface` deja las acciones a la derecha cuando bajan de renglón y no admite párrafos de motivos en `actions`.
- **`FormData` y organización** (11.2): si la organización viaja por encabezado o subdominio, tiene que entrar por
  `send()`, el mismo camino del JSON. Así importar archivos la lleva, también en el reintento después de renovar la
  sesión.
- **`DELETE` con cuerpo JSON** (6.16): algunos proxies descartan el cuerpo de un DELETE. Si hay un gateway, la revisión
  conviene en `If-Match` o en la query.
- **El contrato neutral de la sección de datos** (`userDataSectionContext`, 10.10 y 11.2) es un buen patrón para
  módulos opcionales por organización: la pantalla de usuarios no importa Formularios, y sin el módulo no aparecen ni la
  ruta ni la sección. Mostrar la carga y el error dentro del diálogo, no fuera de la pantalla (punto 26).

### 12.11 Rendimiento

Hay que medir antes de optimizar, pero conviene diseñarlo distinto desde el inicio:
- la entrada a MF1, para la que el back tuvo que proyectar nombre e idiomas en SQL (5.14);
- cada guardado de catálogo, que reescribe las 10.000 opciones (punto 10 del review, back) (9.14);
- el detalle, que carga todas las opciones para calcular `hasValues`;
- la preparación, que carga todas las opciones de cada fuente;
- la planilla, que expande los campos sellados en cada pedido.

### 12.12 Guías para agentes

- El patrón de índices cortos por área, con el detalle en la subcarpeta para no pasar los 32 KiB, conviene adoptarlo
  desde el principio (11.6).
- Nombres coherentes en el menú: hoy el grupo dice «Gestión de formularios» y el cierre del panel «Cerrar el panel
  Formularios» (5.14).

## Anexo: endpoints que el front empezó a consumir, cambió o dejó de usar

Rutas relativas a `/api`. La forma de cada cuerpo está en la sección indicada, en los guards zod que describe y en la
[regla funcional del back](../../features/formularios.md).

### Nuevos

| Método y ruta | Para qué | Sección |
| --- | --- | --- |
| `POST /whatsapp/chats/{id}/assistant-suggestions` | Pedir una sugerencia del asistente (`{requestId, sourceMessageId, expectedModeRevision}`) | 3.4 |
| `GET /whatsapp/chats/{id}/assistant-suggestions/{requestId}` | Consultar la sugerencia (`no-store`) | 3.4 |
| `POST /whatsapp/chats/{id}/assistant-suggestions/{requestId}/discard` | Descartarla | 3.4 |
| `POST /whatsapp/chats/{id}/assistant-suggestions/{requestId}/send` | Mandarla con los datos elegidos (`{requestId, includedFieldIds}`) | 3.4 |
| `GET /whatsapp/chats/{contactId}/messages/{messageId}/audio` | Bytes del audio (`audio/ogg`, `no-store`) | 3.5 |
| `POST /whatsapp/chats/{contactId}/messages/{messageId}/audio-transcription` | Pedir la transcripción (sin cuerpo, `no-store`) | 3.5 |
| `GET\|POST /forms/definitions/{id}/suggestions`, `GET …/suggestions/options`, `GET …/suggestions/revisions?page=&pageSize=10`, `GET …/suggestions/revisions/{revision}` | «Qué sugerir después» y su historial | 3.7, 6.8 |
| `GET /forms/missing-requests?page=&pageSize=&search=&state=&sort=` y `POST /forms/missing-requests/{id}/discard` | Formularios que faltan | 3.9 |
| `POST /forms/catalogs/{id}/proposal/approve` y `POST /forms/catalogs/{id}/proposal/discard` | Resolver una propuesta de catálogo H4 | 3.8 |
| `GET /forms/overview` | Tabla MF1 | 5.4 |
| `POST /forms/definitions/{formId}/records/{recordId}/navigation` | Vecinas de una respuesta para ↑ ↓ en la pantalla completa | 5.7 |
| `GET /forms/approvals/{taskId}/continuation` | Continuar la cola de pendientes desde la tarea observada | 5.7 |
| `GET /forms/definitions/{id}/deactivation` y `POST /forms/definitions/{id}/deactivation` | Impacto de la baja (`no-store`) y dar de baja (`{expectedRevision}`) | 6.5 |
| `DELETE /forms/definitions/{id}` | Eliminar, con cuerpo `{expectedRevision}` | 6.7 |
| `POST /forms/catalogs/{id}/import-previews` y `POST /forms/catalogs/{id}/imports` | Vista previa y aplicación de una importación (multipart) | 8.3, 9.8 |
| `GET /forms/catalogs/{id}/export?template=true\|false` | Exportar y plantilla (XLSX) | 8.3 |
| `GET\|POST /forms/person-attributes`, `PUT /forms/person-attributes/{id}`, `GET /forms/person-attributes/{id}/options?…` | Definir los datos de las personas | 10.3 |
| `GET /forms/person-data?…`, `GET /forms/person-data/{userId}`, `PUT /forms/person-data/{userId}` | Tabla y valores por persona | 10.3 |
| `GET /forms/recipient-samples?…` y `POST /forms/definitions/{id}/recipient-previews` | Persona de ejemplo y «Como si lo recibiera» | 10.3 |

### Cambiados

| Método y ruta | Cambio | Sección |
| --- | --- | --- |
| `GET /whatsapp/chats/{id}/messages` | Suma `assistantSuggestion`, `assistantAvailability`, `origin`, `assistant`, `quickReplies` y `audio` | 3.3 |
| `PUT /whatsapp/chats/{id}/mode` | Modos `Assistant` y `Manual`; responde `{mode, modeRevision, assistantSendInProgress}` | 3.3 |
| `GET /whatsapp/chats/{id}/forms?…` | Mapas de nombre y botón con el secundario opcional | 4.8 |
| `GET /whatsapp/chats/{id}/forms/{definitionId}/versions/{versionId}/preparation` | Se consulta para toda selección; `culture` es la presentada. Incidencias `Forms.Record.CatalogTooLargeForMultiple`, `RecipientDataMissing` y `CatalogFilterUnavailable` | 4.8, 8.3, 10.3 |
| `POST /whatsapp/chats/{id}/form-messages` | `message: null` para la apertura predeterminada; el recibo exige `origin` | 4.8, 5.2 |
| `GET /forms/definitions?…&publicationFilter=` | Valores `Active` (predeterminado) y `Deactivated`; cada fila suma `lifecycle` | 6.3 |
| `GET /forms/definitions/facets?search=` | `{ active, published, drafts, deactivated }` | 6.3 |
| `GET /forms/definitions/{id}` | Suma `lifecycle`, `offerWhen` y `publication.activeVersion.cultures`; en las preguntas de catálogo, `source` con `match` y `presentation` | 3.6, 4.3, 6.4, 10.3 |
| `POST /forms/definitions` y `PUT /forms/definitions/{id}/draft` | Suman `offerWhen`, `multiChoice` con `selection` y `source` con `match` y `presentation` | 3.6, 8.3, 10.3 |
| `POST /forms/definitions/{id}/publication-checks` | Suma `cultures[]`, `diff.addedCultures`, `diff.removedCultures` y `warnings[]`; diagnóstico `Forms.Publication.CatalogTooLargeForMultiple`. Se usa también para reactivar | 4.4, 6.6, 8.3, 10.3 |
| `POST /forms/definitions/{id}/publications` y `GET …/publications/{requestId}` | `cultures` en el estado; se usan también para reactivar | 4.5, 6.6 |
| `GET /forms/definitions/{id}/versions/{versionId}/preview?culture=` | Misma forma; solo culturas publicadas | 4.6 |
| `POST /forms/definitions/{id}/records/search` | Fila con `hasPdf`; `columns[].fields`; condiciones con `anyOf` | 5.5, 8.3, 10.3 |
| `POST /forms/definitions/{id}/records/filter-counts/search` | Condiciones con `anyOf` | 8.3 |
| `GET /forms/records/{id}` | Suma `assistance`, `chain`, `origin.senderKind` y `choice.fields` | 3.10, 5.6, 10.3 |
| `POST /forms/definitions/{id}/records/export` | `catalogFields` (los campos visibles) | 10.3 |
| `GET /forms/records/{id}/workflow` | `task.requestedAtUtc` y `documents[].runId` | 5.6, 5.7 |
| `GET /forms/approvals` | Siempre con `definitionId` | 5.5 |
| `GET /forms/definitions/{id}/rules/revisions` | `changes` | 5.2 |
| Reglas (`…/rules`) | Operador `anyOf` | 8.3 |
| `POST /forms/catalogs` y `PUT /forms/catalogs/{id}` | Suman `valueType` (en el alta), `items[].value` y `sortAlphabetically` (sin `moves`), `fields`, `presentation`, `confirmFieldRemovals` y el delta `items[i].fields`. El recibo suma `fieldIdentityMap`. Errores nuevos: `Forms.Catalog.FieldValuesMissing`, `FieldTypeNotConvertible`, `FieldValueDuplicate`, `FieldRemovalNotConfirmed` (400), y `FieldInUse` y `SourceInUse` (409) | 8.3, 9.4 |
| `GET /forms/catalogs/{id}` y `GET /forms/catalogs?…` | `valueType`; el detalle suma `fields`, `presentation`, `presentationCheck` y `proposal` | 3.8, 8.3, 9.3 |
| `GET /forms/catalogs/{id}/items?…&ids=…` | `value`, `fields` y `fieldTexts` por opción; filtro por `ids` repetido (hasta 100) | 8.3, 9.6, 10.3 |
| `GET /forms/definitions/{id}/suggestions/options` | Suma `unavailableDestinations` con `state` `Deactivated` o `Deleted` | 6.8 |
| `PUT /users/{id}` | Sin cambio de contrato; ahora va antes del PUT de datos de la persona | 10.3 |

### Que el front dejó de usar

| Método y ruta | Por qué | Sección |
| --- | --- | --- |
| `GET /forms/navigation` | MF1 reemplazó la sección dinámica «Respuestas» del menú | 5.3 |
| `GET /forms/approvals/options` | Se retiró la bandeja global «Por aprobar» | 5.3 |

**Sin cambios en el período:** los ocho endpoints de `/api/ai/assistant` del chat de Inicio (7.5 y 7.11).
