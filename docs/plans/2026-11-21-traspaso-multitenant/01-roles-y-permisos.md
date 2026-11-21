# Traspaso 16 · 01 · Roles: áreas plegables y un permiso que incluye a otro

Decidido y programado el 19/11, durante la prueba integral, y en la app ese mismo día. Backend `cbd5ed08`, `39ed143b` y
`9f74a236`; front `68fa867b` a `61364814` (siete commits). Fuentes: el
[plan](../2026-11-19-roles-areas-plegables-y-permisos-que-incluyen.md), la regla «Un permiso incluye a otro» de
`docs/features/administracion.md` y el diseño `ArquitecturaBaseFront/docs/design/roles-y-permisos.md`, con su prueba manual.
Cae en la tanda 1 del 15 (el núcleo).

## 1. Por qué

- **La forma.** En la prueba, el usuario no reconoció la matriz RO1-A del 11/11 (Área · Ver · Administrar · Otros permisos).
  Esperaba áreas plegables con un permiso por fila, como tu editor de roles, tu maqueta y tu tablero «Rol».
- **Administrar sin Ver.** Un rol con «Administrar usuarios» y sin «Ver usuarios» se guardaba y no servía: el listado, el
  menú y la ruta piden `users.read`. Lo encontró la preparación de la prueba (O1 del [05](05-prueba-integral.md)).

## 2. RO3-D, las áreas plegables

- Cada área es una banda con la flecha, su casilla (vacía, parcial o completa), su nombre y su (i). Abierta, cada permiso es
  una fila con su casilla y su nombre entero.
- Al entrar se abren las áreas que tienen algo, hasta que la persona abre o cierra una. Un rol nuevo arranca con todas
  cerradas.
- Sin cuentas por área, sin buscador, sin filtros y sin descripciones a la vista: qué permite cada permiso sigue en la (i)
  del área, uno por renglón. Toda área lleva su casilla, también la de un solo permiso.
- Admin se lee con tildes, sin casillas y con sus áreas cerradas.
- En el celular, la misma lista con filas de 44 px. La flecha y la casilla del área tienen cada una su zona de 44 px, una al
  lado de la otra.
- La banda del área quedó escrita como excepción a `TableSectionToggle`, porque lleva una casilla al frente.
- El conflicto de revisión (el 409) no cambia de forma: compara lo que permite cada versión, con lo incluido.

## 3. Un permiso incluye a otro

**El criterio:** un permiso que no sirve sin el «Ver» de su misma área lo incluye. Son once:

| Permiso | Incluye | Por qué |
| --- | --- | --- |
| `users.manage` | `users.read` | Se administra desde el listado de Usuarios. |
| `roles.manage` | `roles.read` | El editor se abre desde el listado de roles. |
| `ai.manage` | `ai.read` | La conexión se configura en su pantalla, que pide «Ver conexión». |
| `ai.business.manage` | `ai.business.read` | Igual, en «Información del negocio». |
| `arca.manage` | `arca.read` | Igual, en la conexión con ARCA. |
| `formtemplates.manage` | `formtemplates.read` | El editor se abre desde «Formularios». |
| `formtemplates.publish` | `formtemplates.read` | El front ya lo exigía. |
| `formcatalogs.manage` | `formcatalogs.read` | El editor se abre desde «Catálogos». |
| `whatsapp.send` | `whatsapp.read` | Se responde dentro del chat, que pide «Ver chats». |
| `formdocuments.review` | `formdocuments.read` | La bandeja y cada documento piden «Ver documentos recibidos». |
| `formrecords.amend` | `formrecords.read` | Sin «Ver respuestas», corregir queda igual que completar. |

- **No entran** los que sirven solos (`whatsapp.configureassistant`, `formrecords.export`, `formrecords.create`,
  `formapprovals.decide`, `ai.assistant.use`) ni los que dependen de otra área (`arca.issue`, `formrules.manage`,
  `formsuggestions.manage`, `formpersondata.manage`): dar otra área sin que se vea en la fila que se tocó sería dar acceso
  sin avisar. Su (i) dice qué más necesitan.
- **En la pantalla,** marcar un permiso marca lo que incluye y lo deja apagado, con el motivo encima (MA1-A): «Lo incluye
  «Administrar usuarios».». Si lo incluyen dos, los nombra a los dos. Al quitar el que incluye, lo incluido queda marcado y
  se puede quitar.
- **Una sola lista.** Domain declara qué incluye cada permiso, en `Permissions` y en el `*Permissions` de cada módulo. El
  catálogo la junta y la valida al componerse: lo incluido existe, tiene el mismo prefijo y lo declara su misma fuente. El
  front no repite la lista: cada permiso de `GET /api/permissions` trae `includes`.
- **Lo que tiene una cuenta** son los permisos de sus roles más lo que esos incluyen. Lo calcula una sola función del
  catálogo, que usan los dos caminos que leen permisos: la autorización (con el perfil y las guardas de los módulos) y la
  guarda de «nadie da lo que no tiene». Un rol viejo con «Administrar» y sin «Ver» funciona sin migración.
- **Guardar un rol** guarda lo pedido más lo que incluye: lo guardado es lo que el rol permite. Lo que se da o se quita se
  compara con lo que tiene quien pide, calculado igual: quien tiene «Administrar usuarios» da «Ver usuarios».
- **Al arrancar, cada rol se completa** con lo que incluyen sus permisos y le falta, con su historia sin autor. Así, editar
  un rol viejo no escribe lo incluido en nombre de quien edita, que quizás no lo podía dar, ni le da un 409 falso a otro
  editor. El cálculo de lo efectivo queda como red para lo que guarde la versión anterior durante un despliegue.
- **Sin ADR:** es una regla de identidad, que vive en `docs/features` y en las premisas.

## 4. Lo que vio la prueba

- La prueba manual del diseño (nueve pasos) y los pasos 23, 24, 29, 31, 32 y 36 de la lista dieron bien. La sección de
  usuarios y roles quedó completa, salvo una recarga que va a mano.
- **O10.** El selector de roles de una cuenta muestra «Admin», y los roles con permisos que quien edita no tiene, como
  cualquier otro; el rechazo llega al guardar. En el editor de roles pasa lo mismo con las casillas de los permisos que la
  persona no tiene. Convendría apagarlos con su motivo (MA1-A). Queda para después de la prueba.
- **O11 (menor).** A 320 px, los nombres largos de área van en dos renglones y su (i) queda contra el borde derecho.
- **Las corridas.** Dos completas del backend con la app levantada, con fallas sólo por tiempos que pasan solas, y la del
  front con 14 escenas visuales que ya fallaban antes del cambio. Falta una corrida en verde con la app apagada, al cierre de
  la prueba.

## 5. No repetir

- Cuando un permiso pasa a incluir otro, las pruebas que pedían 403 con ese permiso solo dejan de probar lo que decían: cinco
  se corrigieron después (`9f74a236`). Buscalas al sumar la regla.
- El contrato cambia en los dos lados a la vez, y un front nuevo con un backend viejo falla al leer `includes`: el backend
  entra antes que el front o junto con él.
- En el celular, la zona táctil de la casilla del área se montaba sobre la de la flecha, y un toque en el borde de la flecha
  marcaba el área entera (`6f0294af`). Cada una tiene su zona de 44 px, con una prueba de toques.
- Un conflicto de revisión sin nada que elegir adopta lo guardado (`6f0294af`).

## 6. Para el multitenant

- **Tu editor se queda como está.** El «Para el multitenant» del T13/05 §3, que pedía redibujarlo como matriz, queda viejo:
  RO3-D coincide con tus «Permisos» en grupos que se abren (`RoleEditorPage` y el tablero «Rol»). Revisá los detalles del
  §2: toda área con su casilla, se abren las que tienen algo, sin cuentas, filas de 44 px en el celular y el rol con todos
  los permisos (tu Dueño) leído con tildes.
- **La regla de inclusión no existe allá:** cada permiso de tus catálogos es independiente, y tu única proyección
  (`OrganizationProjectedFromCompany`) es otra cosa. Se suma con el mismo criterio, dentro de cada catálogo (organización,
  empresa, plataforma) y nunca entre catálogos ni entre áreas. Candidatos, a confirmar contra tus pantallas:
  - del núcleo: `users.manage` → `users.read`, `roles.manage` → `roles.read`, `companies.manage` → `companies.read`,
    `settings.manage` → `settings.read` y `company.members.manage` → `company.members.read`;
  - de tu hoja de ruta: `formcatalogs.manage` → `formcatalogs.read`, `forms.manage` y `forms.publish` → `forms.read`, y
    `formrecords.correct` → `formrecords.read`;
  - `users.invite`, `roles.assign`, los de WhatsApp, los del asistente y los de plataforma, uno por uno.
- **Tus roles de sistema** (Dueño y CompanyAdmin) son plantillas inmutables que se actualizan con una migración (tu
  `docs/rules/permisos.md`). Ya tienen todo lo de su catálogo, así que la inclusión no les cambia nada. Los roles que crea
  cada organización se completan una vez, con una migración de datos o al arrancar, recorriendo todas las organizaciones, y
  el cálculo de lo efectivo queda como red durante un despliegue.
- **Lo que se guarda en caché** (tu caché de permisos, que se invalida con la generación de los roles) es lo efectivo, con
  lo incluido.
- **«Nadie da lo que no tiene»** (la pregunta 15 del 15): si la adoptás, compara contra los permisos efectivos de quien pide,
  con lo incluido, dentro de cada organización.
- **La pregunta:** la 69 del [07](07-preguntas.md).
