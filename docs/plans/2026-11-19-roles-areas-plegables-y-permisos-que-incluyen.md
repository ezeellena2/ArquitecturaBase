# Roles: áreas plegables (RO3-D) y permisos que incluyen a otros

Decidido por el usuario el 2026-11-19, durante la prueba integral, en la fila RO3 del lienzo
[Roles y permisos: rediseño](https://claude.ai/artifact/MDJUPkopujPycib3mnFqcc) (versión 14). Reemplaza la matriz RO1-A del
2026-11-11 ([diseño](../../../ArquitecturaBaseFront/docs/design/roles-y-permisos.md)). **Aprobado el 2026-11-19 y en
programación.** El usuario eligió hacerlo **antes de la prueba integral**.

## Por qué

- **La forma.** En la prueba integral el usuario no reconoció la matriz (Área · Ver · Administrar · Otros permisos). Esperaba
  áreas plegables con cada permiso como fila, como el multitenant: su front, su maqueta y el tablero «Rol» de su lienzo. El
  11/11 esa forma liviana no estuvo entre las opciones: «RO · Hoy» era el selector pesado, con buscador, filtros, la
  descripción a media pantalla y la casilla al borde derecho.
- **Administrar sin Ver.** Hoy se puede guardar un rol con «Administrar usuarios» y sin «Ver usuarios», y no sirve: la ruta
  `/usuarios`, el menú y `GET /api/users` piden `users.read`. El backend compara cada permiso literal
  (`PermissionAuthorizationHandler`) y los validadores del rol aceptan cualquier combinación.

## Lo que decidió el usuario

1. **RO3-D, áreas plegables.** Cada área es una banda con la flecha, su casilla (vacía, parcial o completa), su nombre y su
   (i). Abierta, cada permiso es una fila con su casilla y su nombre entero.
   - Al entrar se abren las áreas que tienen algo, hasta que la persona abre o cierra una.
   - Sin cuentas, como el lienzo del multitenant desde su versión 145.
   - Toda área lleva su casilla, también las de un solo permiso.
   - Sin buscador, filtros ni descripciones a la vista: qué permite cada permiso sigue en la (i) del área.
   - En el celular, la misma lista con filas de 44 px (tablero RO3-D · En el celular).
2. **Un permiso incluye a otro, empezando por «Administrar incluye Ver».** Marcar un permiso marca lo que incluye y lo deja
   apagado, con el motivo encima, como MA1-A: «Lo incluye «Administrar usuarios».». Quitarlo deja marcado lo incluido. El plan
   revisa uno por uno los demás permisos que no sirven sin su «Ver».
3. **Antes de la prueba integral.**

## Lo que fija el plan

Lo que sigue se resuelve con la opción más simple y robusta que cumple lo decidido.

### Qué permiso incluye a cuál

Entran los que no sirven sin el «Ver» de su misma área:

| Permiso | Incluye | Por qué |
| --- | --- | --- |
| `users.manage` | `users.read` | Se administra desde el listado de Usuarios. |
| `roles.manage` | `roles.read` | El editor se abre desde el listado de roles. |
| `ai.manage` | `ai.read` | La conexión se configura en su pantalla, que pide «Ver conexión». |
| `ai.business.manage` | `ai.business.read` | Igual, en «Información del negocio». |
| `arca.manage` | `arca.read` | Igual, en la conexión con ARCA. |
| `formtemplates.manage` | `formtemplates.read` | El editor se abre desde «Formularios». |
| `formcatalogs.manage` | `formcatalogs.read` | El editor se abre desde «Catálogos». |
| `whatsapp.send` | `whatsapp.read` | Se responde dentro del chat, que pide «Ver chats». |
| `formtemplates.publish` | `formtemplates.read` | El front ya lo exige (`useFormPublication`). |
| `formdocuments.review` | `formdocuments.read` | La bandeja y cada documento piden «Ver documentos recibidos». |
| `formrecords.amend` | `formrecords.read` | `FormRecordAmendmentGuard` sólo lo usa junto con «Ver respuestas»: sin él queda lo mismo que «Completar formularios» (confirmado el 2026-11-19). |

No entran:

- **Los que sirven solos:** `whatsapp.configureassistant`, que tiene su pantalla (`/whatsapp/asistente`);
  `formrecords.export`, que también sirve con «Completar formularios», sobre las respuestas propias; `formrecords.create`;
  `formapprovals.decide` y `ai.assistant.use`.
- **Los que dependen de otra área:** `arca.issue` (de las respuestas), `formrules.manage` y `formsuggestions.manage` (del
  editor de formularios), `formpersondata.manage` (de «Ver usuarios») y armar un formulario desde un documento (de los
  permisos de Formularios). Dar otra área sin que se vea en la fila que se tocó sería dar acceso sin avisar. Su (i) dice qué
  más necesitan, como ya lo hace «Administrar reglas y PDF».

### Dónde vive la regla

- **Una sola lista.** Domain declara qué incluye cada permiso, en `Permissions` y en el `*Permissions` de cada módulo, y el
  catálogo la junta. El front no repite la lista.
- **Lo que la cuenta tiene de verdad.** Los permisos de una cuenta son los de sus roles más lo que esos incluyen. Los calcula
  una sola función del catálogo y la usan los dos caminos que hoy leen permisos:
  - `PermissionService`: autorización, el perfil y las guardas de IA y de WhatsApp;
  - `PermissionDelegationGuard`, que los lee de la base.
  Así un rol viejo con «Administrar» y sin «Ver» funciona enseguida, sin migración.
- **Guardar.** `RoleService` guarda el rol con lo incluido: lo guardado es lo que permite. La guarda de delegación compara
  contra los permisos de quien pide, calculados igual.
- **El contrato.** `PermissionItem` suma `Includes` (los códigos que incluye). El OpenAPI, los ejemplos exportados y el
  tipo del front cambian juntos. El backend entra antes que el front, o en el mismo momento, porque el front nuevo cuenta
  con `includes` (lee `GET /api/permissions` sin esquema de validación).
- **Sin ADR.** Es una regla de identidad: va en `docs/features` y en las premisas, y no cambia la estructura.

### La pantalla

- **`PermissionPicker`, como RO3-D.** Una tabla de una columna, «Permiso».
  - La banda del área es una fila `tone="section"` con la flecha (como `TableSectionToggle`), la casilla del área, el
    nombre, que también abre y cierra, y su `InfoTip`. Las filas de permiso llevan su `Checkbox` y su nombre.
  - El teclado recorre la flecha, la casilla del área, la (i) y los permisos abiertos.
  - Escritorio y celular usan la misma lista; en el celular, 44 px por fila.
- **Lo incluido.** La casilla se marca y queda con `aria-disabled` y su motivo en `DisabledReason`, que sale con el mouse,
  el foco, un clic o un toque. El motivo nombra a todos los marcados que lo incluyen: «Lo incluyen «Administrar formularios»
  y «Publicar formularios».».
- **Admin** se lee con tildes, sin casillas, y sus áreas empiezan cerradas: están todas completas, y cada una se abre para
  leer los nombres.
- **Rol nuevo:** todas las áreas cerradas. **Carga:** el esqueleto con la forma de las bandas. **Error y sin permiso:** como
  hoy.
- **Conflicto de revisión** (`RoleReview`): no cambia de forma; compara lo que cada versión permite, con lo incluido.
- **Lo que se va:** la matriz, `areaColumns`, `hasOtherPermissions` y los anchos fijos. El tooltip conserva sus
  renglones.

## Etapas

0. **Los estados de D en el lienzo:** Admin, rol nuevo, carga, error, sin permiso y conflicto, con la lista de chequeo y una
   revisión adversarial contra las capturas. Se dibujan mientras se programa el backend, antes del front.
1. **Backend, con TDD.** **Hecha el 2026-11-19** (`cbd5ed08`).
   - Hecho: Domain declara las once inclusiones (`Permissions.Includes` y el `Includes` de cada módulo) e
     `IPermissionCatalog` suma `Includes(code)`, con su clausura en el orden del catálogo, y `Effective(codes)`; al
     componerse valida que lo incluido esté en el catálogo, sea de la misma área y lo declare su fuente. `RoleService`
     guarda lo incluido y da o quita la diferencia entre lo que el rol permitía y lo que permite. No hay ejemplos
     exportados ni cohortes de `GET /api/permissions`: el OpenAPI suma `includes` solo. Seis pruebas de módulos que pedían
     403 a quien sólo tenía «Administrar», «Responder» o «Revisar» usan ahora un permiso que no incluye «Ver».
   - Revisión adversarial, el 2026-11-19 (`39ed143b`): el seed completa cada rol con lo que incluyen sus permisos, con su
     historia sin actor; el asistente lista lo que el rol permite; las inclusiones se validan con todo el prefijo; vuelve
     el 403 sin «Ver catálogos», y las premisas pasan a RO3-D.
   - La lista de inclusiones, la función que calcula lo que una cuenta tiene, su uso en `PermissionService` y en
     `PermissionDelegationGuard`, guardar con lo incluido y `Includes` en el contrato.
   - Unitarios: el catálogo, `RoleService` y la guarda.
   - Integración: el catálogo trae `includes`; guardar sólo «Administrar usuarios» guarda los dos; un rol viejo con sólo
     `users.manage` entra a `GET /api/users`.
   - El OpenAPI, los ejemplos exportados y las cohortes que lee el front.
2. **Front, con TDD**, en una copia fuera de la carpeta del front.
   - `PermissionPicker`, su `lib` y `RoleEditorPage`, con el esquema que suma `includes`.
   - Pruebas: `PermissionPicker.test.tsx`, `permissionPicker.test.ts` y `RoleEditorPage.test.tsx`.
   - Escenas y capturas: `role-groups.spec.ts` (Admin, editor 1440 y 390), `design.spec.ts` (editor 1440 y 320) y
     `business-information.spec.ts`, que busca el segundo par de IA.
3. **Documentación.**
   - Front: `roles-y-permisos.md` (RO3-D reemplaza la regla de la matriz), «Roles» de `visual-baseline.md`, el estándar
     visual y el `AGENTS.md` de roles.
   - Backend: la regla de los permisos que incluyen en `docs/features` y en las premisas. Hecha con la etapa 1:
     `administracion.md` («Un permiso incluye a otro»), `identidad.md`, el documento de cada módulo, `backend.md`, las
     premisas, `permiso-nuevo.md` y los `AGENTS.md` de las carpetas.
   - El traspaso 16: el multitenant conserva sus áreas plegables (el 13, §3 del 05, le pedía la matriz) y suma la regla.
4. **Verificación y entrada.**
   - En cada corte, el build y las pruebas rápidas del área. Al terminar: el build sin advertencias, los unitarios y
     `ArchitectureTests` enteros, la integración de roles, permisos y usuarios; en el front, tipos, oxlint, vitest de roles
     y lo que toque, y las escenas visuales de roles.
   - La corrida completa se coordina con la sesión de la prueba integral, porque con la máquina ocupada da tiempos vencidos
     falsos.
   - **Entrada:** el backend entra cuando se pueda reiniciar la app (`aspire stop` y `aspire run`), y el front cuando el
     usuario diga que no está cargando nada, porque Vite le recarga la página.

## Riesgos

- La regla le da «Ver» a quien tiene lo que lo incluye. Es lo buscado; por eso no entran los de otra área.
- El contrato cambia en los dos lados a la vez; con un front nuevo y un backend viejo, el editor fallaría al leer
  `includes`.
- Las capturas de roles cambian todas, y la de Admin cambia de forma.
