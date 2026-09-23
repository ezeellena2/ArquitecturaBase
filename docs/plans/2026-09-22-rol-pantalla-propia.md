# Un rol en su propia pantalla — Plan de implementación

> **Para agentes:** SUB-SKILL REQUERIDA: usar superpowers:subagent-driven-development (recomendado) o superpowers:executing-plans para ejecutar este plan tarea por tarea. Los pasos usan checkboxes (`- [ ]`).

**Objetivo:** que el alta y la edición de un rol dejen de ser un diálogo y pasen a ser una pantalla (`/roles/nuevo` y `/roles/{id}`) que siga funcionando con veinte áreas de permisos. Termina cuando la pantalla queda igual a los tableros aprobados, con todos sus estados, y el diálogo de rol ya no existe.

**Fuente de verdad del diseño:** los dos tableros aprobados el 2026-09-22 en el Artifact [Sistema visual — ArquitecturaBase](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ), fila “Un rol en su propia pantalla”:

- **“Roles · Editar un rol”** (`project/Rol-Editar.dc.html`, interactivo): la pantalla con datos. Su `renderVals()` es el comportamiento: filtros, áreas abiertas, conteos, textos.
- **“Roles · Estados y recorrido”** (`project/Rol-Estados.dc.html`): carga, rol que ya no existe, error, Admin, User, salir sin guardar, recorrido, URL y permisos.

Cada tarea visual **queda igual a su tablero**. Antes de cerrarla se abre el tablero y se compara con la pantalla; no alcanza con tachar atributos. Las reglas generales siguen en `../ArquitecturaBaseFront/docs/design/visual-baseline.md`, que ya decía que “un formulario largo, seccionado o compartible tiene ruta propia”.

**Arquitectura:** no hay tecnología nueva. El backend suma un campo al catálogo de permisos. El front suma una ruta con dos variantes, una guarda de cambios sin guardar (`useBlocker` de react-router 8.4) y un mecanismo para que una pantalla hija ponga el último nivel de las migas. De paso se arregla un bug de la Fase 5: la banda de `Page` nunca quedó adherida (ver Hechos).

> **Revisión previa.** Tres revisores independientes contrastaron la primera versión de este plan con los tableros y el código. Todo lo que encontraron está incorporado: el título y la miga siguen al nombre escrito, los textos del vacío, “Todos los permisos” en Admin, el `AppLayout` que no scrolleaba, la guarda con el botón Atrás, el orden de los tests de migas, el Enter del buscador, la tabla de textos y las claves que se borran.

---

## Reglas para quien ejecute

- **Dos repos.** Backend en `C:\Users\ezequ\source\repos\ArquitecturaBase`, front en `C:\Users\ezequ\source\repos\ArquitecturaBaseFront`. Cada tarea dice cuál toca.
- **Rama:** todo va directo a `main`, en los dos repos. No crear ramas. **No hacer push.**
- **Commits:** uno por tarea, en español, conventional commits. La última línea del mensaje es exactamente `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Si el mensaje lleva tildes, escribirlo con heredoc o desde un archivo UTF-8.
- **Verificación antes de cada commit:** backend, `dotnet build ArquitecturaBase.slnx` con **0 advertencias** y `dotnet test` en verde (necesita Docker); front, `npm run build`, `npm run lint` y `npm run test`, los tres limpios. Pegar la salida real.
- **Hay otra sesión trabajando en el backend** (el ingreso con WhatsApp): `Phones/`, `PhoneNumber.cs`, `Errors.resx`, `UserErrors.cs`, `Directory.Packages.props` y `Infrastructure` tienen cambios sin commitear que **no son de este plan**. No se tocan, no se agregan y no se “arreglan”. Si el build o un test falla por esos archivos, se informa y la Tarea 1 se verifica con `--filter-class` sobre `PermissionTextsTests`, `ResourceParityTests` y `RolesEndpointsTests`.
- **Nunca `git add .` ni `git add -A`:** agregar solo los archivos propios, por nombre.
- **TDD** donde hay lógica: el test primero, verificar que falla por la razón correcta, después el código.
- **Idioma:** identificadores, logs y excepciones en inglés. Todo texto visible sale de resources (backend) o de i18next (front), en español rioplatense con voseo y en inglés, con las claves de la tabla “Textos del front”.
- **Desvíos:** si algo no compila o una API cambió, hacer el cambio mínimo e informarlo. Si el cambio altera el diseño, frenar.

## Hechos verificados del código actual

Comprobados el 2026-09-22 leyendo los repos, y revisados por un segundo par de ojos.

**Backend:**

- `GET /api/permissions` pide `roles.read` y lo arma `GetPermissionsQueryHandler` a partir de `Permissions.All`: no hay tabla. Devuelve `PermissionGroup(Area, Name, Permissions)` con `PermissionItem(Code, Name)`.
- Los textos salen de `Application/Resources/Permissions.resx` y `.en.resx` a través de `PermissionTexts` (`Area.<área>` y `Permission.<código>`). `PermissionTextsTests` verifica que cada clave tenga texto en los dos idiomas y `ResourceParityTests`, que los dos archivos tengan las mismas claves.
- **No hay `GET /api/roles/{id}`.** La edición pide `GET /api/roles` de nuevo y busca el suyo. Se mantiene así: son pocos roles.
- `UpdateRoleCommandHandler`: un rol del sistema no se renombra; a `Admin` no se le cambian los permisos (mandar los mismos no es un cambio); a `User` sí; la descripción de los dos se puede cambiar. Nombre repetido → `Roles.Role.AlreadyExists`.

**Front:**

- `routes.tsx`: `/roles` cuelga de `<ProtectedRoute permission="roles.read" />`. `ProtectedRoute` sin el permiso redirige a `/sin-permiso`. El `*` de nivel superior (`NotFoundPage`) está **fuera** de `AppLayout`.
- `RolesPage` abre `RoleFormDialog` para crear y editar. Los roles del sistema dicen “No se cambia” en lugar de acciones.
- `RoleFormDialog` ya resuelve lo difícil de la edición: pide los roles de nuevo al abrir (`refetchOnMount: "always"`) y siembra el formulario **cuando termina ese pedido**, no con lo que había en caché. Eso se conserva en la pantalla.
- **La banda de `Page` hoy no queda adherida, en ninguna pantalla.** `AppLayout` tiene la raíz en `flex min-h-svh` y `<main className="flex-1 overflow-y-auto">`: la raíz crece con el contenido, `main` nunca desborda y el que scrollea es el documento. Como `main` tiene `overflow-y: auto`, igual es el contenedor de los `sticky` de adentro, pero no se mueve, así que nada queda adherido. Reproducido en un navegador: después de scrollear 800 px, la banda queda en `top = -736`. Con la raíz en `h-svh`, `main` scrollea y la banda queda fija. jsdom no maqueta, así que ningún test lo detecta.
- `Breadcrumbs` busca la ruta **exacta** en `navigationLinks`: en `/roles/abc` mostraría solo “Inicio”. `branchOf` también compara exacto, así que el grupo “Gestión de usuarios” no se abriría solo en una ruta hija. El `NavLink` del menú no usa `end` (salvo `/`), así que “Roles y permisos” sí queda marcado en `/roles/abc`.
- El segmentado de estado de usuarios está escrito a mano en `UsersFilterBar`, sin `role` ni nombre de grupo. No hay un `SegmentedControl` compartido.
- `ConfirmDialog` tiene el botón de cancelar fijo en “Cancelar”, y al confirmar llama a `onConfirm()` y enseguida a `onOpenChange(false)`, en el mismo clic.
- `CheckboxField` ya acepta `description` (va como descripción accesible) y `disabled`. `FormField` acepta `hint`.
- `Page.test.tsx` usa `renderWithProviders`, que **no monta router**: un `Link` adentro necesita envolverse en `MemoryRouter` (como `UserMenu.test.tsx`).
- `useBlocker` y `useBeforeUnload` existen en `react-router` 8.4, y el router es de datos (`createBrowserRouter`; los tests usan `createMemoryRouter`). **El `reset()` de un blocker no valida la transición:** pisa `"proceeding"` con el estado inicial. Con un POP (Atrás del navegador), `proceed()` hace `history.go(delta)` más tarde; si antes corrió un `reset()`, el bloqueo se vuelve a evaluar y el diálogo reaparece.
- Los desplegables de Radix se abren con teclado en los tests (`CLAUDE.md` del front).

## Contratos

### `GET /api/permissions` — la descripción de cada permiso

Cada permiso suma `description`, traducido como el nombre:

```json
{ "code": "users.read", "name": "Ver usuarios", "description": "El listado y el detalle de cada cuenta." }
```

Clave `PermissionDescription.<código>` en `Permissions.resx` / `.en.resx`:

| Código | Español | Inglés |
|---|---|---|
| `users.read` | El listado y el detalle de cada cuenta. | The list and the details of each account. |
| `users.manage` | Dar de alta, editar, desactivar y eliminar cuentas. | Add, edit, deactivate and delete accounts. |
| `roles.read` | Los roles y qué permisos da cada uno. | The roles and which permissions each one grants. |
| `roles.manage` | Crear, editar y eliminar roles. | Create, edit and delete roles. |
| `settings.manage` | El modo de registro y los ajustes del sistema. | The sign-up mode and the system settings. |

### Rutas del front

| Ruta | Permiso | Qué es |
|---|---|---|
| `/roles` | `roles.read` | El listado (sin cambios de ruta). |
| `/roles/nuevo` | `roles.manage` | El alta, vacía. |
| `/roles/:roleId` | `roles.manage` | La edición. Se puede compartir y recargar. |

La búsqueda, el filtro “Elegidos” y qué áreas están abiertas **no van en la URL**: son del momento de editar (“Estados y recorrido”, “Fuera de la URL”).

### Textos del front

`roles.json`, claves nuevas (la columna del medio es el español, la última el inglés):

| Clave | Español | Inglés |
|---|---|---|
| `editor.createTitle` | Nuevo rol | New role |
| `editor.editTitle` | Editar el rol {{name}} | Edit the {{name}} role |
| `editor.unnamed` | sin nombre | unnamed |
| `editor.crumbNew` | Nuevo rol | New role |
| `editor.crumbLoading` | Editar rol | Edit role |
| `editor.crumbUnnamed` | Rol | Role |
| `editor.back` | Volver a Roles y permisos | Back to Roles and permissions |
| `editor.unsaved` | Cambios sin guardar | Unsaved changes |
| `editor.allPermissions` | Todos los permisos | All permissions |
| `editor.saving` | Guardando… | Saving… |
| `editor.details` | Datos del rol | Role details |
| `editor.systemNameHint` | Los roles del sistema no cambian de nombre. | System roles can't be renamed. |
| `editor.loadError` | No pudimos cargar el rol | We couldn't load the role |
| `editor.gone.title` | Este rol ya no existe | This role no longer exists |
| `editor.gone.description` | Alguien lo borró, o el link que abriste es de antes. | Someone deleted it, or the link you opened is outdated. |
| `editor.discard.title` | ¿Descartar los cambios? | Discard the changes? |
| `editor.discard.description` | Lo que cambiaste en este rol no se guardó. | What you changed in this role wasn't saved. |
| `editor.discard.confirm` | Descartar | Discard |
| `editor.discard.stay` | Seguir editando | Keep editing |
| `summary.title` | Lo que va a poder hacer | What it will allow |
| `summary.none` | Ninguno | None |
| `summary.empty` | Todavía no elegiste ningún permiso. Un rol sin permisos se puede guardar, pero no habilita nada. | You haven't picked any permission yet. A role without permissions can be saved, but it doesn't allow anything. |
| `summary.areas_one` / `_other` | {{count}} área / {{count}} áreas | {{count}} area / {{count}} areas |
| `summary.count` | {{permissions}} en {{areas}} | {{permissions}} in {{areas}} |
| `summary.remove` | Quitar {{name}} | Remove {{name}} |
| `picker.title` | Permisos | Permissions |
| `picker.searchLabel` | Buscar un permiso | Search for a permission |
| `picker.searchPlaceholder` | Buscar un permiso o un área | Search for a permission or an area |
| `picker.show` | Mostrar | Show |
| `picker.all` | Todos | All |
| `picker.picked` | Elegidos · {{total}} | Picked · {{total}} |
| `picker.expandAll` | Expandir todo | Expand all |
| `picker.collapseAll` | Contraer todo | Collapse all |
| `picker.areaProgress` | {{picked}} de {{total}} | {{picked}} of {{total}} |
| `picker.pickAll` | Elegir todos | Pick all |
| `picker.pickAllFor` | Elegir todos los permisos de {{area}} | Pick every permission in {{area}} |
| `picker.clearAll` | Quitar todos | Remove all |
| `picker.clearAllFor` | Quitar todos los permisos de {{area}} | Remove every permission in {{area}} |
| `picker.noMatch.title` | Ningún permiso coincide | No permission matches |
| `picker.noMatch.nonePicked` | Este rol todavía no tiene permisos elegidos. | This role has no permissions picked yet. |
| `picker.noMatch.query` | No hay permisos ni áreas que digan “{{query}}”. | There are no permissions or areas that say “{{query}}”. |
| `picker.noMatch.queryPicked` | No hay permisos ni áreas que digan “{{query}}” entre los elegidos. | There are no permissions or areas that say “{{query}}” among the picked ones. |
| `picker.noMatch.action` | Ver todos los permisos | See all permissions |

- “3 permisos en 2 áreas” se arma con `summary.count`, pasándole `permissions` = `permissionsCount` (ya existe, con `_one`/`_other`) y `areas` = `summary.areas`. Así cada número se pluraliza por separado: “1 permiso en 1 área”.
- `picker.picked` usa `{{total}}` y no `{{count}}`, para que i18next no busque formas plurales que no hacen falta.
- Se reutilizan: `form.name`, `form.nameRequired`, `form.descriptionLabel`, `form.descriptionPlaceholder`, `form.submit`, `systemBadge`, `permissionsCount_*`, `feedback.created`, `feedback.updated`, `errors.alreadyExists`, `errors.systemRole`, `errorTraceId`, `common:actions.retry` y `common:actions.cancel`.
- `users.json` suma `filters.status.label`: “Estado” / “Status” (el nombre del grupo del segmentado de usuarios).
- **Se borran en la Tarea 8**, cada una después de un `grep -rn "<clave>" src` que confirme que nadie más la usa: `form.createTitle`, `form.editTitle`, `form.permissions`, `form.permissionsPicked_zero`, `form.permissionsPicked_one`, `form.permissionsPicked_other` y `errors.gone`. También `systemLocked`, porque los roles del sistema pasan a tener acciones (decisión de la Tarea 8).

## La pantalla, anclada al tablero

**Queda igual a “Roles · Editar un rol”.** De arriba abajo:

1. **Migas:** `Inicio / Gestión de usuarios / Roles y permisos / {hoja}`. “Roles y permisos” es un enlace. La hoja sigue al **nombre que está en el formulario** (recortado), como en el tablero: en el alta, “Nuevo rol”; en la edición, el nombre escrito, o “Rol” si quedó vacío; mientras carga, “Editar rol”.
2. **Banda (`Page`):** en lugar del ícono, un botón de 32 px con la flecha a la izquierda que vuelve a `/roles` (`aria-label` “Volver a Roles y permisos”). Título: en el alta, “Nuevo rol”; en la edición, “Editar el rol {nombre escrito}”, o “Editar el rol sin nombre” si quedó vacío; para Admin, solo “Admin”. Si el rol es del sistema, la insignia “Del sistema” al lado. Si hay cambios, “· Cambios sin guardar” en gris. A la derecha, **Cancelar** (vuelve a `/roles`) y **Guardar**. **Admin**, en ese mismo lugar, muestra en gris “Todos los permisos” y ningún botón.
3. **Cuerpo:** un `<form id noValidate>` con dos columnas (`340px` + el resto, 16 px entre columnas, `items-start`; en menos de `lg` se apilan). El Guardar de la banda es `type="submit" form={id}`.
   - **Izquierda, adherida al scrollear** (`lg:sticky`, con `top` igual a la banda más el padding del cuerpo: `lg:top-[calc(3.5rem+1.5rem)]`):
     - Tarjeta **“Datos del rol”** con banda: Nombre (obligatorio) y Descripción (textarea de 3 líneas, placeholder “Opcional. Una línea que explique para qué sirve.”).
     - Tarjeta **“Lo que va a poder hacer”** con banda y el conteo a la derecha (“3 permisos en 2 áreas” / “Ninguno”). Adentro, los elegidos agrupados por área (nombre del área arriba, en negrita chica) como chips que se quitan con su botón (`aria-label` “Quitar {permiso}”). Sin elegidos, el texto `summary.empty`. Tiene alto máximo (330 px) y scroll propio.
   - **Derecha, tarjeta “Permisos”:**
     - Barra: título “Permisos”, buscador (`aria-label` “Buscar un permiso”, placeholder “Buscar un permiso o un área”, con la lupa adentro), segmentado **Todos | Elegidos · N** (grupo “Mostrar”), y a la derecha **“Expandir todo”** y **“Contraer todo”**, siempre visibles.
     - Cada área es un **`fieldset` que envuelve la fila y sus permisos, esté abierta o cerrada**, con `legend` `sr-only` (el nombre del área): así toda área es un `group` con nombre, que es lo que hoy protege un test. La fila mide 46 px: un botón con `aria-expanded` que ocupa todo el ancho libre y lleva adentro el chevron, el nombre y la píldora “N de M” (con color de marca si N > 0); a su derecha, **“Elegir todos”** o **“Quitar todos”** (el segundo cuando ya están todos).
     - Abierta, el área muestra sus permisos en **dos columnas**, cada uno con su casilla, su nombre y la descripción debajo en gris (`CheckboxField` con `description`).
     - Sin coincidencias: “Ningún permiso coincide”, el motivo y el botón “Ver todos los permisos”, que limpia búsqueda y filtro. El motivo, según el caso: solo “Elegidos” → `picker.noMatch.nonePicked`; búsqueda sin “Elegidos” → `picker.noMatch.query`; búsqueda con “Elegidos” → `picker.noMatch.queryPicked`. `{{query}}` va recortado.
4. **Aviso al guardar** (toast): “Creamos el rol.” o “Guardamos los cambios.”, y se vuelve a `/roles`.

**Comportamiento del selector** (lo que hace el `renderVals()` del tablero):

- La búsqueda se **recorta** y cuenta como activa solo si lo recortado no está vacío. No distingue mayúsculas ni tildes (“factura” encuentra “Facturación”; “configuracion”, “Configuración”). Coincide por nombre del área, nombre del permiso o descripción. Si coincide el área, se ven todos sus permisos.
- “Elegidos” muestra solo los permisos marcados. Se combina con la búsqueda.
- Con búsqueda o con “Elegidos”, las áreas que quedan se muestran abiertas. “Expandir todo” y “Contraer todo” siguen visibles y cambian lo que se ve al limpiar.
- Al abrir la pantalla arrancan abiertas las áreas que tienen algún permiso elegido; en un rol nuevo, la primera.
- “Elegir todos” / “Quitar todos” actúa sobre **todos** los permisos del área, estén o no visibles, y la píldora cuenta sobre todos. Su `aria-label` nombra el área (“Elegir todos los permisos de Usuarios”).
- **Enter en el buscador no guarda:** su `onKeyDown` corta el `Enter`. Es el campo donde un Enter es natural, y adentro del `<form>` dispararía el Guardar de la banda.

**Estados, anclados a “Roles · Estados y recorrido”:**

| Estado | Qué se ve |
|---|---|
| Cargando (solo edición) | Esqueleto de las dos columnas. El alta arranca vacía apenas llega el catálogo. |
| El rol ya no existe | `editor.gone.title`, `editor.gone.description` y el botón “Volver a Roles y permisos”. Sin reintento. |
| No se pudo cargar | `editor.loadError`, el código para reportar si vino (`errorTraceId`) y “Reintentar”. Vale para el rol y para el catálogo. |
| 403 del backend | `ForbiddenPage`, como hace `RolesPage`. |
| Rol del sistema: Admin | Nombre y descripción de solo lectura, todo marcado y deshabilitado, sin “Elegir/Quitar todos”, chips sin botón de quitar, “Todos los permisos” en la banda y ningún botón. |
| Rol del sistema: User | Nombre de solo lectura con la ayuda `editor.systemNameHint`; descripción y permisos editables. |
| Salir con cambios | Cancelar, la flecha, las migas, el menú o el Atrás del navegador abren “¿Descartar los cambios?” / “Lo que cambiaste en este rol no se guardó.” con **Seguir editando** y **Descartar** (destructivo). Sin cambios, se sale directo. Recargar o cerrar la pestaña con cambios dispara el aviso del navegador (`useBeforeUnload`). |
| Nombre vacío | “Poné un nombre.” debajo del campo, al guardar. **Escribir en el nombre borra el error.** |
| Nombre repetido | “Ya hay un rol con ese nombre.” debajo del nombre. Lo elegido no se pierde. |
| Otro error al guardar | `<p role="alert">` arriba de las dos columnas, con el texto de `roleActionErrorMessage`. |
| Guardando | “Guardar” se deshabilita y dice “Guardando…”. La pantalla no se tapa. |
| Sin `roles.manage` | La ruta redirige a `/sin-permiso` (`ProtectedRoute`). |

“Hay cambios” se calcula contra lo sembrado: nombre, descripción y el **conjunto** de permisos. Si se vuelve al estado original, deja de haber cambios.

## Estructura de archivos

Backend:

```
src/ArquitecturaBase.Application/
  Features/Roles/GetPermissions/PermissionGroup.cs          (PermissionItem suma Description)
  Features/Roles/GetPermissions/GetPermissionsQueryHandler.cs
  Resources/PermissionTexts.cs                              (+ Description)
  Resources/Permissions.resx, Permissions.en.resx           (+ PermissionDescription.*)
tests/ArquitecturaBase.Application.UnitTests/Resources/PermissionTextsTests.cs
tests/ArquitecturaBase.Api.IntegrationTests/Roles/RolesEndpointsTests.cs
CLAUDE.md                                                   (el paso de “un permiso nuevo”)
```

Front:

```
src/layouts/AppLayout.tsx              la raíz pasa a h-svh: main scrollea y la banda se adhiere
src/shared/ui/
  SegmentedControl.tsx (+ test)        nuevo; UsersFilterBar pasa a usarlo
  Page.tsx (+ test)                    + backTo y status
  ConfirmDialog.tsx (+ test)           + cancelLabel
src/shared/hooks/
  useUnsavedChangesGuard.ts (+ test)   nuevo
  useBreadcrumbLeaf.ts (+ test)        nuevo, con su store
src/layouts/
  navigation.ts                        parentLinkOf, y branchOf por prefijo
  components/Breadcrumbs.tsx (+ test)  rutas hijas
src/features/roles/
  api/roles.ts                         PermissionItem suma description
  lib/permissionPicker.ts (+ test)     nuevo: la lógica pura del selector
  lib/systemRoles.ts                   nuevo: ADMIN_ROLE_NAME e isAdminRole
  components/PermissionPicker.tsx (+ test)
  components/RoleSummary.tsx (+ test)
  pages/RoleEditorPage.tsx (+ test)
  pages/RolesPage.tsx (+ test)         navega en lugar de abrir el diálogo
  components/RoleFormDialog.tsx        se borra
src/app/routes.tsx                     /roles/nuevo y /roles/:roleId
src/locales/{es,en}/roles.json, users.json
```

## Tareas

| # | Tarea | Repo | Tests |
|---|---|---|---|
| 1 | La descripción de cada permiso | backend | unitarios + integración |
| 2 | Piezas compartidas y la banda que no se adhería | front | unitarios |
| 3 | Migas y menú en rutas hijas | front | unitarios |
| 4 | La guarda de cambios sin guardar | front | unitarios |
| 5 | La lógica del selector de permisos | front | unitarios |
| 6 | `PermissionPicker` y `RoleSummary` | front | unitarios |
| 7 | `RoleEditorPage` y sus rutas | front | de pantalla |
| 8 | El listado navega y el diálogo se va | front | de pantalla |
| 9 | Documentación y cierre | ambos | — |

La Tarea 1 es independiente y puede ir en paralelo. Las del front van en orden: las 2 a 6 dejan las piezas listas y la 7 las compone.

---

### Tarea 1: La descripción de cada permiso

Repo: **backend**.

- [x] **Paso 1: los tests (tienen que fallar).** En `PermissionTextsTests.Keys()`, sumar `"PermissionDescription." + permiso` para cada permiso de `Permissions.All`. En `RolesEndpointsTests`, `The_permission_catalog_is_grouped_by_area_and_translated` verifica las dos descripciones de `users` en español, y `The_permission_catalog_is_also_in_english`, al menos la de `users.read` en inglés.
- [x] **Paso 2: el código.** `PermissionItem(string Code, string Name, string Description)`. `PermissionTexts.Description(string permission) => Get("PermissionDescription." + permission)`. El handler la completa. Las diez entradas de la tabla de Contratos, cinco por archivo, con el mismo formato de una línea que las que ya están.
- [x] **Paso 3: `CLAUDE.md`.** El paso 1 de “Un permiso nuevo” pasa a decir: se declara en `Domain/Authorization/Permissions.cs` y en `Permissions.All`, y en `Permissions.resx` y `.en.resx` lleva `Permission.<código>` y `PermissionDescription.<código>` (y `Area.<área>` si el área es nueva); lo verifican `PermissionTextsTests` y `ResourceParityTests`.
- [x] **Paso 4:** buscar en el backend otros consumidores de `PermissionItem` o de `/api/permissions` (colecciones de Postman, `.http`, snapshots de OpenAPI) y actualizarlos. Verificación y commit: `feat: el catálogo de permisos trae una descripción de cada uno`.
  > No había nada que actualizar: la colección de Postman no tiene un pedido a `/api/permissions`, no hay archivos `.http` y el documento de OpenAPI se genera al vuelo, sin snapshot. Las menciones de `PermissionItem(Code, Name)` en el plan y el diseño de la Fase 4 quedan como están, porque son el registro de esa fase.

### Tarea 2: Piezas compartidas y la banda que no se adhería

Repo: **front**.

- [x] **`AppLayout`:** la raíz pasa de `flex min-h-svh` a `flex h-svh` (con lo que haga falta para que la columna tenga `min-h-0` y `main` sea el que scrollea; el menú lateral, si su contenido supera el alto, scrollea por su cuenta). Con eso la banda de `Page` queda adherida de verdad en todas las pantallas. **jsdom no lo puede probar:** verificarlo en un navegador (Playwright o el navegador de la app sobre `npm run dev`, o una reproducción estática con la misma estructura) y pegar la evidencia: después de scrollear, la banda sigue arriba y la barra superior también.
  > Verificado con Vite y un arnés temporal (borrado antes del commit) que monta el `AppLayout` real con una `Page` de 80 filas, a 1440×900: la app entera necesita la Api y el ingreso. Con `min-h-svh`, después de scrollear 800 px, la banda queda en `top = -736` y la barra superior en `-800` (el hecho de arriba, reproducido). Con `h-svh` el documento no scrollea, `main` sí (800 px, y 1000 px con la rueda del mouse), la barra superior queda en `top = 0` y la banda en `top = 64`, justo debajo. Con 200 px de alto, el menú lateral scrollea por su cuenta; a 375 px, lo mismo que en escritorio.
- [x] **`SegmentedControl`** (`shared/ui/SegmentedControl.tsx`): un grupo (`role="group"`, `aria-label` obligatorio) de botones con `aria-pressed`, con el aspecto del segmentado de hoy en `UsersFilterBar` (36 px, borde, separadores, la opción activa en `brand-50`/`brand-700`). Props: `options: { key: string; label: ReactNode; pressed: boolean; onSelect: () => void }[]`. Tests: nombre del grupo, `aria-pressed` y el clic. **`UsersFilterBar` pasa a usarlo**, con `aria-label` = `users:filters.status.label` (clave nueva, “Estado” / “Status”); los tests de `UsersPage` siguen en verde sin tocarlos.
  > Se sumaron detalles chicos, sin cambiar el diseño: el fondo `bg-surface`, como en el tablero; el contorno de foco hacia adentro, porque el grupo recorta lo que sale de su borde; `whitespace-nowrap` y `shrink-0`, para que una barra angosta no parta ni aplaste las opciones; y un test más, que el segmentado nunca envía el formulario que lo contiene.
- [x] **`Page`:** `backTo?: { to: string; label: string }` dibuja, en lugar del ícono, un `Link` de 32 px con borde y `ChevronLeftIcon`, con `aria-label = label`. `status?: ReactNode` va al lado del `h1`. Tests, **envueltos en `MemoryRouter`**: el enlace de volver con su nombre y su `href`; el estado al lado del título; sin `backTo`, el ícono como hasta ahora.
  > El enlace de volver lleva también `title={label}`, como `IconButton`: es solo una flecha, y el `title` le dice a dónde vuelve a quien pasa el mouse.
- [x] **`ConfirmDialog`:** `cancelLabel?: string`, que por defecto sigue siendo `actions.cancel`. Test.
- [x] Verificación y commit: `feat: segmentado compartido, volver en Page, y la banda por fin se adhiere`.

### Tarea 3: Migas y menú en rutas hijas

Repo: **front**.

Una pantalla hija (`/roles/abc`) tiene que mostrar a qué sección pertenece, y su último nivel lo sabe solo la pantalla.

- [x] **`navigation.ts`:** `parentLinkOf(pathname)` devuelve el enlace del menú del que cuelga una ruta hija (`pathname.startsWith(link.to + "/")`, sin contar `/`). `branchOf` pasa a encontrar el grupo también para las rutas hijas, así el menú se despliega solo en `/roles/abc` (el `Sidebar` ya usa `branchOf`). Tests.
- [x] **`useBreadcrumbLeaf(label?: string)`** (`shared/hooks`): un store de módulo (`subscribe`/`getSnapshot`) que la pantalla escribe en un efecto y borra al desmontarse, y que `Breadcrumbs` lee con `useSyncExternalStore`. Es un efecto que sincroniza con algo externo, no un estado derivado. Si `oxlint` se queja de exportar un hook y un store juntos, separarlos en dos archivos. Tests: la escribe, la cambia y la borra al desmontar.
  > `oxlint` no se quejó, así que quedó en un solo archivo. El `useSyncExternalStore` no lo llama `Breadcrumbs` directo: va envuelto en un segundo hook del mismo archivo, `useCurrentBreadcrumbLeaf()`, como hace `signOutStatus` con `useIsSigningOut`. Así `subscribe` y `getSnapshot` no salen del módulo y nadie más puede escribir la hoja.
- [x] **`Breadcrumbs`:** en una ruta hija dibuja `Inicio / {grupo} / {enlace padre como Link} / {hoja}`, con `aria-current="page"` en la hoja. Si la pantalla todavía no puso la hoja, el enlace padre queda último y **sin** `aria-current`. Las rutas exactas no cambian.
- [x] **Tests de las piezas sueltas**, no de las rutas reales (`/roles/abc` recién existe en la Tarea 7, y hoy cae en el `*`, fuera de `AppLayout`): `Breadcrumbs` y `Sidebar` montados con `renderWithProviders` dentro de `<MemoryRouter initialEntries={["/roles/abc"]}>`, como `UserMenu.test.tsx`, más un componente de prueba que llama a `useBreadcrumbLeaf` (con hoja y sin hoja). En `Sidebar`: el grupo está desplegado y “Roles y permisos”, marcado. El test contra las rutas reales va en la Tarea 7.
  > Se sumó un caso más en `Breadcrumbs`: en `/roles`, una hoja que quedó puesta se ignora, que es lo que asegura que “las rutas exactas no cambian”.
- [x] Verificación y commit: `feat: las migas y el menú reconocen las rutas hijas`.

### Tarea 4: La guarda de cambios sin guardar

Repo: **front**.

- [x] **`useUnsavedChangesGuard(isDirty: boolean)`** (`shared/hooks`). Por dentro, `useBlocker` bloquea toda navegación del router a otro `pathname` mientras `isDirty`, y `useBeforeUnload` pide confirmación al recargar o cerrar mientras `isDirty`. Devuelve `{ isBlocked, stay, leave, allowNextNavigation }`:
  - `leave()` marca un `useRef` (`leavingRef.current = true`) y llama a `blocker.proceed()`.
  - `stay()` **no hace nada si `leavingRef` está puesto**; si no, y el estado es `"blocked"`, llama a `blocker.reset()`. Es necesario porque `ConfirmDialog` llama a `onConfirm` y enseguida a `onOpenChange(false)` en el mismo clic, y ese `reset()` viejo pisaría el `"proceeding"`: con el Atrás del navegador, “Descartar” no saldría y el diálogo reaparecería.
  - El ref se limpia cuando el blocker vuelve a `"unblocked"`.
  - `allowNextNavigation()` deja pasar la próxima navegación sin preguntar. La usa el guardado exitoso antes de volver al listado.
  > `leave()` marca el ref y llama a `proceed()` solo cuando el blocker está en `"blocked"`: el tipo `Blocker` de react-router es una unión discriminada y solo en ese estado tiene `proceed` y `reset`. El comportamiento es el planeado, porque el diálogo solo se ve en ese estado.
- [x] Tests con `createMemoryRouter` y un componente de prueba que monta un `ConfirmDialog` como lo va a hacer la pantalla: sin cambios navega directo; con cambios bloquea; “Seguir editando” se queda; “Descartar” navega (PUSH); **con cambios, `router.navigate(-1)` abre el diálogo y “Descartar” termina en la ruta anterior (POP)**; `allowNextNavigation` deja pasar una sola vez.
  > Se sumaron dos casos: un cambio de la query string en la misma ruta no pregunta (el “a otro `pathname`” de arriba), y el `beforeunload` queda cancelado solo con cambios. El del Atrás se vio fallar primero con un `stay()` sin la marca de `leavingRef`: el diálogo reaparecía y la ruta seguía en el editor.
- [x] Verificación y commit: `feat: guarda de cambios sin guardar`.

### Tarea 5: La lógica del selector de permisos

Repo: **front**. Funciones puras en `features/roles/lib/permissionPicker.ts`, con los tests primero. `PermissionItem` (`features/roles/api/roles.ts`) suma `description: string`.

- [x] `normalizeForSearch(text)`: recortado, en minúsculas y sin tildes (`normalize("NFD")` y fuera los diacríticos).
- [x] `visibleAreas(groups, { query, onlyPicked, picked })`: las áreas con sus permisos visibles según “Comportamiento del selector”. Una búsqueda de solo espacios no filtra. Las áreas sin permisos visibles no aparecen.
  > Devuelve, por área, `{ group, permissions }` (`AreaSubset`): `group` es el área entera y `permissions`, lo que se ve. Así la píldora y “Elegir todos” cuentan sobre el área sin tener que volver a buscarla. Para probar que coincidir por el área muestra todos sus permisos, los tests suman el área de ejemplo “Facturación” del tablero: en el catálogo real, cada permiso nombra a su área.
- [x] `areaProgress(group, picked)`: `{ picked, total, all }` sobre **todos** los permisos del área.
- [x] `toggleArea(group, picked)`: si están todos, los saca; si no, suma los que faltan, sin duplicados.
  > Después de la revisión devuelve un `Set` en las dos ramas: antes conservaba los repetidos de la lista de elegidos y copiaba dos veces un código que el área repitiera. En el mismo arreglo, los permisos de prueba de `RolesPage.test.tsx` suman `description` (tipados como `PermissionGroup`) y el comentario de `PermissionGroup` deja de nombrar al diálogo de rol, así que las Tareas 7 y 8 ya no tienen que tocarlos.
- [x] `pickedSummary(groups, picked)`: los elegidos agrupados por área en el orden del catálogo, y los totales (`permissions`, `areas`).
  > Devuelve `{ groups, permissions, areas }`, con `groups` en la misma forma que `visibleAreas`: es el filtro “Elegidos” sin búsqueda. Cuenta solo los códigos que están en el catálogo: uno que el backend ya no declara no tiene chip, y el conteo tiene que coincidir con los chips.
- [x] `initialOpenAreas(groups, picked)`: las áreas con algún elegido; sin ninguno, la primera.
- [x] `sameSelection(a, b)`: igualdad de conjuntos.
- [x] Verificación y commit: `feat: lógica del selector de permisos`.

### Tarea 6: `PermissionPicker` y `RoleSummary`

Repo: **front**. Los dos **quedan iguales a su parte del tablero** “Roles · Editar un rol”, con los textos de la tabla.

- [x] **`PermissionPicker`** (`features/roles/components`). Props: `groups`, `picked: readonly string[]`, `onChange(picked)`, `readOnly`. Guarda en su propio estado la búsqueda, “Elegidos” y las áreas abiertas (arrancan con `initialOpenAreas`). Usa `SegmentedControl`, `CheckboxField` y la lógica de la Tarea 5. El buscador es `Input type="search"` con la lupa adentro, sin debounce (filtra en memoria), y corta el Enter. Con `readOnly`, casillas deshabilitadas y sin “Elegir/Quitar todos”.
  > Para quedar igual al tablero se tocaron tres piezas compartidas, sin cambiar el diseño: `CheckboxField` pasa a ser la fila del tablero (se ilumina al pasar el mouse y se marca con un clic en cualquier parte, con la etiqueta estirada sobre la fila para que la ayuda siga siendo la descripción accesible), y la casilla vacía lleva el borde de `--color-content-muted`, porque con `--color-border` casi no se veía; `SegmentedControl` pasa a 13 px de padding, como en los tableros (los tests de `UsersFilterBar` siguen en verde); y `EmptyState` acepta `className`, para el vacío que vive adentro de la tarjeta sin su recuadro punteado. El vacío conserva los tamaños de `EmptyState` (16 y 14 px, no 15 y 13,5) y la última área no repite el borde inferior de la tarjeta.
  > Después de la revisión, `EmptyState` acepta también `descriptionClassName`, y el motivo del vacío no pasa de 360 px, como en el tablero: repite la búsqueda, y con una larga se estiraba en una sola línea de punta a punta de la tarjeta. Quedan dos diferencias a propósito: la píldora de un área con elegidos lleva el texto de `--color-brand-700` (0.48) y no el 0.42 del tablero, que no es un token; y la cruz de cada chip de `RoleSummary` es el “×” de los chips de filtro de `UsersFilterBar`, no la cruz SVG del tablero, para que los dos chips de la aplicación se vean iguales.
- [x] **`RoleSummary`**: la tarjeta “Lo que va a poder hacer”. Props: `groups`, `picked`, `onRemove?` (sin él, los chips no tienen botón).
  > En 340 px, el título y “3 permisos en 2 áreas” entran justo en una línea; con números de dos cifras ya no. El conteo no se parte nunca y el título baja de renglón, con la banda creciendo con él.
- [x] Tests de `PermissionPicker`, por rol y nombre accesible: buscar filtra y no distingue tildes; una búsqueda de solo espacios no filtra; las tres variantes del vacío y su botón que limpia; “Elegidos · N”; “Elegir todos” / “Quitar todos” con su `aria-label` y el “N de M”; abrir y cerrar un área (`aria-expanded`); “Expandir todo” y “Contraer todo”; **cada área es un `group` con su nombre, aunque esté cerrada**; `readOnly`; la descripción es la descripción accesible de la casilla; Enter en el buscador no envía el formulario que lo contiene. De `RoleSummary`: “1 permiso en 1 área”, “3 permisos en 2 áreas”, “Ninguno” con el texto de vacío, y quitar desde un chip.
  > Se sumaron casos: las áreas que arrancan abiertas (con elegidos, o la primera), buscar por la descripción, “Elegir todos” sobre un área que la búsqueda muestra a medias, lo que se contrae buscando vale al limpiar, y los chips sin botón cuando no hay `onRemove`. Con los componentes vacíos, los 25 tests fallaron por no encontrar lo que buscaban; y sacando el `legend`, el corte del Enter o el recorte de la búsqueda, se pone en rojo el test de cada caso.
- [x] Verificación y commit: `feat: selector de permisos por área, con búsqueda y resumen`.
  > Comparado con el tablero en un navegador, con un arnés temporal de Vite (borrado antes del commit) al lado de una copia estática del tablero con el mismo estado: filas de 46 px, chips de 28, píldoras, segmentado y buscador con las mismas medidas, a menos de un píxel. También se vieron la búsqueda que coincide por área, el vacío, Admin y el rol nuevo.

### Tarea 7: `RoleEditorPage` y sus rutas

Repo: **front**. **Queda igual a “Roles · Editar un rol”, con cada estado de “Roles · Estados y recorrido”.**

- [x] **Rutas:** `/roles/nuevo` y `/roles/:roleId`, `lazy`, debajo de un `<ProtectedRoute permission="roles.manage" />` propio (el de `/roles` pide `roles.read`). `/roles/nuevo` se declara antes que `/:roleId`.
  > Las dos rutas cargan `RoleEditorPage`, que es solo un envoltorio: lee `roleId` y monta el editor con `key={roleId ?? "new"}`. Ir de `/roles/abc` a `/roles/def` es la misma ruta con otro parámetro, react-router conserva el elemento montado y, sin la `key`, la pantalla se quedaba con lo sembrado del rol anterior. Tiene su test (pasar de Soporte a Auditoría con `router.navigate`), que se vio fallar sin la `key`. De paso, dos arreglos de la Tarea 3: `/roles/` es el listado con una barra de más, no una hija (test nuevo en `navigation.test.ts`), y los comentarios de `Breadcrumbs.test.tsx` y `Sidebar.test.tsx` ya no dicen que las rutas hijas no existen: la pieza se prueba sola a propósito y el recorrido contra las rutas de verdad vive en `RoleEditorPage.test.tsx`.
  > Después de la revisión, `/roles/` no solo deja de ser una hija: se lee como `/roles`. `navigation.ts` exporta `withoutTrailingSlash`, que usan `parentLinkOf`, `branchOf` y `Breadcrumbs`; antes, en `/roles/` las migas decían solo “Inicio” como página actual y el menú no desplegaba “Gestión de usuarios”. Tests nuevos en `navigation.test.ts` y `Breadcrumbs.test.tsx`.
- [x] **Datos:** el catálogo con `permissionsQueryKey`. En la edición, `GET /api/roles` con `refetchOnMount: "always"`, y la siembra **cuando ese pedido termina**, como hacía `RoleFormDialog` (copiar el comentario que explica por qué). Se siembra una sola vez, durante el render y no con un efecto.
  > Un error de carga cuenta solo mientras no hay nada que mostrar: si falla un pedido posterior (al guardar se vuelven a pedir los roles), la pantalla no se cambia por el cartel de error y lo escrito sigue ahí.
- [x] **Admin:** `features/roles/lib/systemRoles.ts` exporta `ADMIN_ROLE_NAME = "Admin"` e `isAdminRole(role)` (`isSystemRole` y ese nombre), con un comentario que diga que refleja `SystemRoles.Admin` del backend.
  > Admin también muestra la ayuda `editor.systemNameHint` debajo del nombre: vale para los dos roles del sistema. Con la descripción de solo lectura, el placeholder no se muestra.
- [x] **Guardar:** `POST /api/roles` o `PUT /api/roles/{id}` con `name` recortado, `description` recortada o `null`, y `permissions`. Al terminar bien: toast, invalidar `rolesQueryKey` y `currentUserQueryKey`, `allowNextNavigation()` y `navigate("/roles")`. Errores: `Roles.Role.AlreadyExists` y un 400 con `errors.name` van debajo del nombre; el resto va al alerta de arriba (`roleActionErrorMessage`).
  > Después de la revisión, `permissions` viaja filtrado contra el catálogo cargado. Un código que el backend sacó de `Permissions.All` pero que sigue guardado en el rol no tiene casilla ni chip, así que no había forma de quitarlo, y `ValidPermissions` rechazaba el rol entero con un 400 que caía en el alerta de arriba. Se limpia al guardar y no al sembrar: sembrando sin él, la pantalla arrancaría con “Cambios sin guardar”. Tiene su test.
- [x] **Título y migas** siguen al nombre escrito, con los respaldos de la tabla (sección “La pantalla”, puntos 1 y 2).
  > Mientras carga, y en los estados del rol que ya no existe y del error, el título también dice “Editar rol” (`editor.crumbLoading`): la tabla no fijaba un título para esos casos y todavía no hay nombre que poner.
- [x] **Guarda:** `useUnsavedChangesGuard(isDirty)` y el `ConfirmDialog` con los textos `editor.discard.*` (`cancelLabel` = “Seguir editando”, destructivo).
  > Después de la revisión, **mientras se guarda, salir no pregunta** (`useUnsavedChangesGuard(isDirty && !mutation.isPending)`): lo cambiado ya salió, y “Lo que cambiaste en este rol no se guardó” no era cierto. Tampoco se trae de vuelta a quien se fue: la vuelta a `/roles` y el error debajo del nombre pasaron a los callbacks de `mutate(body, { onSuccess, onError })`, que TanStack no llama si la pantalla ya se desmontó (antes, el `onSuccess` de `useMutation` corría igual y lo llevaba al listado sin que lo pidiera). El toast de éxito y las invalidaciones siguen en `useMutation`, porque valen aunque la persona se haya ido. Si el guardado falla después de salir, no queda formulario donde mostrarlo: lo avisa un toast (`roleActionErrorMessage`), como hacen las mutaciones que nacen en un `ConfirmDialog`. Mientras dura el guardado, tampoco pregunta el navegador al recargar o cerrar. Dos tests nuevos: salir durante un guardado que termina bien, y durante uno que falla.
- [x] **Tests** (`RoleEditorPage.test.tsx`, con MSW y `renderRouteWithProviders`):
  - el alta manda el cuerpo esperado, avisa “Creamos el rol.” y vuelve a `/roles`;
  - la edición se siembra con el rol recién pedido y no con el de la caché;
  - guardar sin nombre muestra “Poné un nombre.” y no llama al backend; escribir lo saca;
  - `Roles.Role.AlreadyExists` queda debajo del nombre y lo elegido sigue marcado;
  - al cambiar el nombre cambian el título y la miga; vacío, dicen “Editar el rol sin nombre” y “Rol”;
  - “· Cambios sin guardar” aparece al cambiar algo y desaparece al volverlo atrás;
  - con cambios, Cancelar abre la confirmación; “Seguir editando” se queda y “Descartar” vuelve al listado; sin cambios, Cancelar vuelve directo;
  - el rol que no existe muestra su estado y su botón vuelve al listado;
  - un 500 al cargar muestra el código para reportar y “Reintentar” vuelve a pedir;
  - Admin: casillas deshabilitadas, “Todos los permisos” en la banda, sin Guardar ni Cancelar;
  - User: el nombre es de solo lectura con su ayuda, y se puede guardar un cambio de permisos;
  - sin `roles.manage`, `/roles/nuevo` termina en la pantalla de sin permiso;
  - las migas muestran “Roles y permisos” como enlace y el nombre del rol, y el menú tiene el grupo desplegado.
  > Veinte tests. Además de la lista: una descripción en blanco viaja como `null`; un 400 con `errors.name` va debajo del nombre y otro error, arriba de las columnas; “Guardando…” deshabilita el botón sin tapar la pantalla; el catálogo que falla muestra el mismo estado de error; la flecha de la banda también pasa por la guarda; y el remontado por rol. `renderRouteWithProviders` devuelve también el router, para mirar `router.state.location` y navegar. Antes de sumar las rutas fallaron los veinte (caían en “No encontramos esta página”); sembrando con la caché, se pone en rojo el de la siembra; sin la `key`, el del remontado.
- [x] **Comparar con el tablero**, incluida la columna izquierda adherida al scrollear en un navegador, y anotar en el commit cualquier diferencia que quede a propósito.
  > Con un arnés temporal de Vite (borrado antes del commit) que monta el `AppLayout` real y `RoleEditorPage` sobre un `fetch` simulado con el catálogo de nueve áreas del tablero, al lado de una copia estática del tablero con el mismo estado, a 1440×920: tarjetas, filas de área (46 px), chips, buscador y botones coinciden a un píxel. **La columna izquierda queda adherida:** con las nueve áreas abiertas, `main` scrollea 200 px con la rueda del mouse y hasta el fondo (514 px), y la barra superior sigue en `top = 0`, la banda en 64 y la columna izquierda en 144 (64 + 56 + 24), mientras el selector va de 144 a −370. Por debajo de `lg` (900 px) las columnas se apilan y la izquierda scrollea con el resto.
  >
  > **Arreglo en `AppLayout`:** el `legend` `sr-only` de cada área es `absolute` y, sin un ancestro posicionado, se ubicaba respecto del documento: lo estiraba a 1316 px, la página entera scrolleaba (barra superior incluida) y la barra de scroll le comía 15 px al selector. `main` pasa a ser `relative`; el documento queda en 920 px y no scrollea.
  >
  > **Diferencias que quedan a propósito**, todas de piezas compartidas: la barra superior mide 64 px y no 60; el título de la banda, 18 px y no 16 (`Page`); el cuerpo, 24 px de padding arriba y no 20 (`Page`), así que la columna se adhiere 24 px debajo de la banda y no 20; y los rótulos de los campos, 14 px y no 13 (`FormField`), con lo que “Datos del rol” mide 241 px y no 246. Después de la revisión se suman tres, también de piezas compartidas: el asterisco de “Nombre *” sale del color del rótulo y no rojo (`FormField`); el código para reportar va como texto y no con estilo `code` (`EmptyState` recibe la descripción como texto, y así la muestran todas las pantallas); y el rol que ya no existe y el error de carga usan el recuadro punteado de `EmptyState`, no una tarjeta blanca. Alinearlas es cambiar la pieza compartida y su biblioteca, en una tarea aparte. Las áreas “de ejemplo” no existen, y con “Elegidos” de dos cifras (Admin) “Expandir todo” y “Contraer todo” bajan de renglón. También se vieron la carga, Admin, User con cambios, “¿Descartar los cambios?” y el guardado que vuelve al listado sin preguntar.
- [x] Verificación y commit: `feat: el rol se crea y se edita en su propia pantalla`.
  > Commit `800ccc2` en el front. `npm run build` limpio, `npm run lint` sin salida (código 0), `npm run test`: 48 archivos y 321 tests en verde.
  >
  > **Arreglos de la revisión:** commit `8e08809` en el front, `fix: el editor de rol no vuelve al listado si saliste mientras guardaba`: salir mientras se guarda, los permisos que el catálogo ya no declara y `/roles/` (en los pasos de arriba), y la columna izquierda en pantallas bajas: con muchos elegidos medía unos 650 px, y con menos de ~800 px de alto el final del resumen quedaba debajo del borde hasta llegar al fondo del selector. Ahora la columna no pasa de `lg:max-h-[calc(100svh-4rem-3.5rem-3rem)]` (la barra superior, la banda y el padding de arriba y de abajo), “Datos del rol” no se achica y el que cede es el resumen, que ya tenía scroll propio. Visto en el navegador con un arnés temporal fuera del repo: a 1366×640, con 14 elegidos, la columna va de 144 a 616 y se queda ahí al scrollear `main` hasta el fondo; a 1440×920 las medidas no cambian (Soporte, de 144 a 579). También se vio salir por las migas durante un guardado de 800 ms: sin diálogo, la ruta queda en `/` y llega el toast “Guardamos los cambios.”. `npm run build` limpio, `npm run lint` sin salida (código 0), `npm run test`: 48 archivos y 326 tests en verde.

### Tarea 8: El listado navega y el diálogo se va

Repo: **front**.

> **Decisión del usuario (2026-09-22):** en el listado, **Admin** tiene “Ver” (`EyeIcon`, “Ver el rol Admin”), que abre la pantalla de solo lectura, y **User**, “Editar”; ninguno de los dos, “Eliminar”. Está dibujado en el tablero “Roles · Acciones del listado”, que es la referencia de esta tarea.

- [x] **`RolesPage`:** “Nuevo rol” es un `Button asChild` con un `Link` a `/roles/nuevo`. “Editar” navega a `/roles/{id}`. Las acciones de los roles del sistema, según la decisión de arriba: `EyeIcon` nuevo en el set, con el mismo lenguaje que el tablero (`icons.test.tsx` lo cubre solo); claves `actions.view` “Ver” / “View” y `actions.viewFor` “Ver el rol {{name}}” / “View the {{name}} role”; `systemLocked` se borra.
  > Todas las filas usan `RowActions`, también las del sistema: Admin lleva “Ver” en lugar de “Editar” (`isAdminRole`, de la Tarea 7) y la acción “Eliminar” de los dos va con `hidden: row.isSystemRole`, como una acción sin permiso. “Ver” y “Editar” son botones que navegan (`navigate`), no enlaces, porque `RowActions` dibuja botones. `EyeIcon` es el ojo del tablero tal cual (el contorno en un trazo y la pupila de radio 2.75, con el trazo de 1.75 del set). La insignia “Del sistema” y la fila quedan como estaban.
- [x] **Se borra `RoleFormDialog.tsx`** y las claves de la lista cerrada de “Textos del front”, cada una después de su `grep`. `parity.test.ts` sigue en verde.
  > Después de borrar el diálogo, ni `RoleFormDialog` ni ninguna de las ocho claves aparece en `src`, `docs`, `AGENTS.md` ni `README.md`, tampoco armadas con plantillas. El único rastro es la regla del `legend` del `CLAUDE.md` del front (“El diálogo de rol es el caso”), que se cambia en la Tarea 9. En el código no quedaban comentarios que nombraran al diálogo de rol.
- [x] **Tests de `RolesPage`:** los del diálogo (grupos con nombre, contador, siembra fresca, alta) se borran de acá porque ya los cubre la Tarea 7. `hides the create and edit actions without roles.manage` pasa a buscar `queryByRole("link", { name: "Nuevo rol" })` (hoy busca un `button` y, con el `Link`, pasaría siempre). Nuevos: con `roles.manage`, el enlace “Nuevo rol” apunta a `/roles/nuevo`; “Editar el rol Soporte” lleva a su pantalla. Los datos de prueba suman un rol `User` del sistema; Admin tiene “Ver” y no “Eliminar”; User tiene “Editar” y no “Eliminar”; sin `roles.manage` no hay “Ver el rol Admin”.
  > Los de navegación siguen hasta la pantalla: el enlace “Nuevo rol” termina en `/roles/nuevo` con el título “Nuevo rol”, “Editar el rol Soporte” en `/roles/r2` con “Editar el rol Soporte”, y “Ver el rol Admin” en `/roles/r1` con el título “Admin”. Sin `roles.manage` tampoco está “Editar el rol User”. Antes del código fallaron los cuatro nuevos: tres por no encontrar el enlace o el botón que buscaban, y el de “Editar el rol Soporte” por no encontrar el título de la pantalla (el botón ya existía y abría el diálogo viejo, cuyo título era un `h2`).
  > Después de la revisión, el test sin `roles.manage` se llama `hides every action without roles.manage` y verifica también que no estén “Eliminar el rol Soporte” ni la columna “Acciones”: es todo lo que el tablero dice que desaparece sin el permiso. Cada aserción nueva se vio fallar con el código roto a propósito (la columna siempre dibujada, con “Eliminar” a la vista y sin él). Commit `c73f3be` en el front. Quedan para una tarea aparte, porque cambian `RowActions` y su biblioteca, dos cosas que la revisión encontró y que la Tarea 9 suma a los pendientes: el separador entre Editar y Eliminar (ver el paso siguiente), y que “Ver” y “Editar” sean enlaces y no botones, para poder abrir la pantalla del rol en otra pestaña o copiar su dirección (una acción de `RowActions` con `to` que dibuje un `Link` con el mismo aspecto).
- [x] Verificación y commit: `feat: el listado de roles lleva a la pantalla del rol`.
  > Commit `acfcb91` en el front. `npm run build` limpio, `npm run lint` sin salida (código 0), `npm run test`: 48 archivos y 327 tests en verde (salen los cinco del diálogo y el viejo de los roles del sistema; entran cuatro del listado y los tres de `EyeIcon` en `icons.test.tsx`). Comparado con el tablero en un navegador, con un arnés temporal de Vite fuera del repo (borrado) que monta `RolesPage` sobre un `fetch` simulado al lado de una copia del tablero: los grupos de acciones miden lo mismo (botones de 32×28, íconos de 18 px, borde y radio de 8), el ojo se ve igual y “Ver” lleva a `/roles/r1`. **Una diferencia que no es de esta tarea:** en el grupo de dos acciones (Editar y Eliminar), el tablero tiene un separador entre los botones y la aplicación no, porque el `border-0` de cada botón de `RowActions` le gana al `divide-x` del grupo (que en Tailwind 4 va con `:where`). Pasa igual en el listado de usuarios; alinearlo es cambiar la pieza compartida y su biblioteca, aparte.

### Tarea 9: Documentación y cierre

Repos: **los dos**.

- [x] **`CLAUDE.md` del front:** la regla “Las acciones se resuelven en diálogos” pasa a decir que un formulario corto va en diálogo y uno largo o seccionado tiene ruta propia (el rol es el caso), con `backTo` en `Page`, `useUnsavedChangesGuard` y `useBreadcrumbLeaf`. La regla del `legend` en `sr-only` pasa a nombrar al selector de permisos. Las rutas de administración suman `/roles/nuevo` y `/roles/{id}` (`roles.manage`). Anotar que `AppLayout` tiene la raíz en `h-svh` para que `main` scrollee, y por qué.
  > La regla quedó en dos: “un formulario corto va en un diálogo; uno largo o seccionado tiene ruta propia”, y “una pantalla de formulario propia se arma con tres piezas”, que suma la trampa del `reset()` de `useBlocker` y que mientras se guarda la guarda se apaga. Se sumó también una regla de las rutas hijas (sin ítem propio en el menú, reconocidas por prefijo, migas de cuatro niveles, `/roles/nuevo` antes que `/roles/:roleId`), y en la de `AppLayout`, que `main` es `relative` (Tarea 7).
- [x] **`docs/design/visual-baseline.md`:**
  - fila nueva en el mapa Artifact → proyecto: “Roles · Editar un rol” y “Roles · Estados y recorrido” → `/roles/nuevo`, `/roles/{id}`, `RoleEditorPage`, `PermissionPicker`, `SegmentedControl`: **Implementado**;
  - la comparación cerrada “Alta y edición de un rol: diálogo / pantalla propia → **pantalla propia**, porque con veinte áreas el diálogo no tiene dónde crecer”;
  - en “Encabezados”: una pantalla hija reemplaza el ícono por el botón de volver de 32 px (`backTo`) y puede llevar un estado al lado del título; las migas de una ruta hija tienen cuatro niveles, con el padre como enlace;
  - que la banda adherida recién funciona desde este cambio (el hecho falso de la Fase 5);
  - la revisión del encabezado al 2026-09-22.
  > El mapa lleva dos filas: “Roles · Editar un rol” con “Roles · Estados y recorrido”, y “Roles · Acciones del listado” (`RolesPage`, `RowActions` y `EyeIcon`), implementado salvo el separador entre Editar y Eliminar. La fila de “Encabezados, íconos y color” dice que la banda está adherida de verdad desde este cambio, y cuenta 17 íconos y no 15 (ver “Hechos falsos, corregidos”). En la revisión del encabezado solo cambiaron la fecha y la mención de los tres tableros del rol, sin tocar las líneas que suma la otra sesión.
- [x] **La biblioteca [ArquitecturaBase UI](https://claude.ai/artifact/Ew763kqorVHSYeUqE8CZ7h):** si para entonces el `CLAUDE.md` del front ya pide llevar a la biblioteca todo cambio de aspecto de `shared/ui` (al cerrar la Tarea 6 era un cambio sin commitear de otra sesión), llevar los de la Tarea 6: `CheckboxField` como fila del selector (padding de 9 y 10 px, etiqueta de 13,5 px con peso 500, fondo al pasar el mouse y clic en toda la fila), `SegmentedControl` con 13 px de padding, y `EmptyState` con `className` y `descriptionClassName`. Si la regla no está, anotarlo en los pendientes del “Resultado de la ejecución”.
  > **Omitido, con la regla a medias:** el `CLAUDE.md` del front la tiene en el working tree (“Un cambio de aspecto va a la biblioteca y a `shared/ui` a la vez”), pero como cambio sin commitear de otra sesión, que además es la dueña de la biblioteca y del lienzo. Ni la biblioteca ni el lienzo se tocaron. Lo que tendrían que reflejar quedó en el punto 3 de los pendientes, y no solo lo de la Tarea 6: también `SegmentedControl` entero (es nuevo), `backTo` y `status` de `Page`, `cancelLabel` de `ConfirmDialog`, el caparazón en `h-svh` con `main` `relative`, `EyeIcon` y las migas de cuatro niveles.
- [x] **Este plan:** tachar los pasos y sumar al final “Resultado de la ejecución” (verificación final, desvíos, pendientes), como en la Fase 5.
  > Suma también la tabla de commits y los hechos falsos que se corrigieron.
- [x] Commits: `docs: el rol en su propia pantalla` en cada repo.
  > `5e6d545` en el front y `87776da` en el backend. El del front lleva solo los cambios de este plan: `CLAUDE.md` y `visual-baseline.md` tenían cambios sin commitear de la otra sesión, así que se armó un parche desde `HEAD` con solo los de acá y se agregó con `git apply --cached`; `git diff --cached` mostró solo lo propio y, después del commit, `git diff` sigue mostrando exactamente lo de la otra sesión (`AGENTS.md`, una línea de `CLAUDE.md` y la sección de la biblioteca en `visual-baseline.md`). Antes del commit del front: `npm run build` limpio, `npm run lint` sin salida (código 0) y `npm run test` con 48 archivos y 327 tests en verde; en el backend, `dotnet build` con 0 advertencias y `dotnet test` con 602 de 602.

## Lo que este plan no hace

- **El responsive dibujado.** Los dos tableros son de 1440 px. La pantalla apila las columnas en menos de `lg` para no romperse, pero el diseño móvil sigue siendo el pendiente general del fundamento.
- **`GET /api/roles/{id}`.** La edición sigue pidiendo la lista entera.
- **Las áreas “de ejemplo” del tablero.** No existen: el catálogo real tiene tres.

## Resultado de la ejecución

Ejecutado entre el 2026-09-22 y el 2026-09-23, tarea por tarea, con un commit por tarea y los arreglos de cada revisión en commits aparte. En los dos repos trabajaban otras sesiones a la vez (el ingreso con WhatsApp en el backend; la biblioteca ArquitecturaBase UI en los documentos del front): nada de lo suyo se tocó ni se agregó.

### Commits

| Tarea | Repo | Commits |
| --- | --- | --- |
| 1 | backend | `0d2835c` |
| 2 | front | `1aac839` |
| 3 | front | `d6d55ae` |
| 4 | front | `72e7f80` |
| 5 | front | `fbf7d66`, y `e1cd309` de la revisión |
| 6 | front | `84075a7`, y `19ca80c` de la revisión |
| 7 | front | `800ccc2`, y `8e08809` de la revisión |
| 8 | front | `acfcb91`, y `c73f3be` de la revisión; en el backend, `48240e6` anotó la decisión del usuario |
| 9 | los dos | `5e6d545` en el front y `87776da` en el backend (este resultado) |

Cada tarea sumó además, en el backend, su `docs: marcar la Tarea N del rol en pantalla propia`. En el front, entre la Tarea 6 y la 7 entró `6be645f`, que estabiliza un test de `SessionRecovery` que fallaba de a ratos con la suite entera: no es de ninguna tarea de este plan.

### Verificación final

| Repo | Comando | Resultado |
| --- | --- | --- |
| backend | `dotnet build ArquitecturaBase.slnx` | 0 advertencias, 0 errores |
| backend | `dotnet test` | **602 / 602**, la suite entera (sobre `c1a5006`, sin cambios sin commitear de la otra sesión) |
| front | `npm run build` | limpio |
| front | `npm run lint` | sin salida, código 0 |
| front | `npm run test` | **327 / 327** en 48 archivos |

Las comparaciones con los tableros se hicieron en un navegador, con arneses temporales de Vite que montan las piezas reales sobre un `fetch` simulado (borrados antes de cada commit). **La pantalla no se probó contra la Api real ni con un ingreso de verdad** (ver pendientes).

### Desvíos del plan

Ninguno cambia el diseño. El detalle está en la nota de cada paso; en resumen:

| Dónde | Qué dice el plan | Qué se hizo, y por qué |
| --- | --- | --- |
| Tarea 2 | `SegmentedControl` con el aspecto del de `UsersFilterBar` | Además, fondo `bg-surface`, foco hacia adentro (el grupo recorta su borde), `whitespace-nowrap` y `shrink-0`, y un test de que no envía el formulario. El volver de `Page` lleva también `title`. |
| Tarea 3 | `Breadcrumbs` lee el store con `useSyncExternalStore` | Lo lee con `useCurrentBreadcrumbLeaf()`, del mismo archivo: `subscribe` y `getSnapshot` no salen del módulo y nadie más puede escribir la hoja. |
| Tarea 4 | `leave()` llama a `proceed()` | Solo en `"blocked"`: el `Blocker` de react-router es una unión discriminada. Dos casos de test más (la query string no pregunta; `beforeunload` solo con cambios). |
| Tarea 5 | las firmas sueltas de la lógica | `visibleAreas` devuelve `{ group, permissions }` por área, `pickedSummary` `{ groups, permissions, areas }` contando solo lo que está en el catálogo, y `toggleArea` un `Set` sin repetidos. |
| Tarea 6 | usar `CheckboxField`, `SegmentedControl` y `EmptyState` | Para quedar igual al tablero cambiaron las tres piezas compartidas (ver pendientes). Dos diferencias a propósito: la píldora usa `--color-brand-700` y no un 0.42 que no es token, y la cruz de los chips es la “×” de los chips de filtro. |
| Tarea 7 | las dos rutas montan `RoleEditorPage` | Es un envoltorio que monta el editor con `key={roleId ?? "new"}`: de un rol a otro, react-router conservaba lo sembrado del anterior. También: `/roles/` se lee como `/roles` (`withoutTrailingSlash`); un error de carga cuenta solo si no hay nada que mostrar; mientras carga, en el error y en el rol que no existe, el título dice “Editar rol”; Admin también muestra la ayuda del nombre. |
| Tarea 7 | al guardar bien, toast, invalidar, `allowNextNavigation()` y volver a `/roles` | Mientras se guarda, la guarda se apaga, y la vuelta al listado y el error del nombre van en los callbacks de `mutate`, que no corren si la pantalla ya se desmontó; si el guardado falla después de salir, avisa un toast. `permissions` viaja filtrado contra el catálogo, para no trabar un rol con un permiso que el backend ya no declara. |
| Tarea 7 | `AppLayout` en `h-svh` (Tarea 2) | Además, `main` pasa a `relative`, y la columna izquierda tiene alto máximo: en pantallas bajas el resumen quedaba debajo del borde (ver hechos). |
| Tarea 8 | “Editar” navega a `/roles/{id}` | Todas las filas usan `RowActions`, también las del sistema: Admin “Ver” (`EyeIcon`), User “Editar”, y “Eliminar” con `hidden` en las dos. “Ver” y “Editar” son botones que navegan, no enlaces, porque `RowActions` dibuja botones. El test sin `roles.manage` pasó a `hides every action without roles.manage`. |
| Tarea 9 | llevar a la biblioteca los cambios de la Tarea 6, si la regla ya está | **No se hizo.** La regla (“un cambio de aspecto va a la biblioteca y a `shared/ui` a la vez”) sigue siendo un cambio sin commitear de otra sesión, que además es la dueña de la biblioteca y del lienzo. Lo que habría que llevar quedó en los pendientes. |
| Tarea 9 | commit de los documentos del front | `CLAUDE.md` y `visual-baseline.md` tenían cambios sin commitear de esa otra sesión: el commit lleva solo los de este plan, armados como un parche desde `HEAD` y agregados con `git apply --cached`. Los suyos siguen en el working tree, sin tocar. |

### Hechos falsos, corregidos

1. **“La banda de `Page` queda adherida”**, de la Fase 5, que lo daban por hecho el fundamento visual (“Implementado: banda adherida”) y el `CLAUDE.md` del front. **Nunca quedó adherida, en ninguna pantalla:** la raíz de `AppLayout` era `min-h-svh`, el que scrolleaba era el documento y el `sticky` se quedaba pegado a un `main` que no se movía. No lo encontró la ejecución sino la revisión previa de este plan (está en los Hechos); se arregló en la Tarea 2 y los documentos se corrigieron en la 9. jsdom no maqueta, así que ningún test lo podía ver.
2. **“Con la raíz en `h-svh`, `main` scrollea y la banda queda fija”**, de los Hechos de este plan, era cierto pero no alcanzaba. El `legend` `sr-only` de cada área es `absolute` y, sin un ancestro posicionado, estiraba el documento: volvía a scrollear la página entera, barra superior incluida. `main` pasó a `relative` en la Tarea 7.
3. **La columna izquierda “adherida al scrollear”** cabía en el tablero (920 px de alto) y no en una pantalla baja: con muchos elegidos medía unos 650 px, y con menos de ~800 px de alto el final del resumen quedaba fuera de la vista. La columna tiene ahora un alto máximo y el que cede es el resumen, que ya tenía scroll propio.
4. **“15 íconos propios”**, en el mapa del fundamento: al cerrar la Fase 5 ya eran 16; con `EyeIcon`, son 17.

### Pendientes al cerrar

1. **La prueba manual en el navegador, con la Api real y el ingreso.** Todo lo visto en un navegador fue con arneses y un `fetch` simulado. Falta, con `aspire run` (y `aspire stop` al terminar): crear un rol, editar uno, ver Admin, editar User, el rol que ya no existe, salir con cambios por cada camino (Cancelar, la flecha, las migas, el menú, el Atrás), recargar con cambios, y entrar sin `roles.manage`.
2. **El responsive, sin dibujar.** Los tableros son de 1440 px; por debajo de `lg` las columnas se apilan para no romperse, pero eso no es un diseño. Sigue siendo el pendiente general del fundamento, y empieza dibujando.
3. **Lo que la biblioteca ArquitecturaBase UI tendría que reflejar** de este plan, cuando la regla de llevar allí cada cambio de aspecto se confirme (hoy es un cambio sin commitear de otra sesión, que es la dueña de la biblioteca y del lienzo):
   - `SegmentedControl`, **nuevo**: grupo con `role="group"` y nombre, botones con `aria-pressed`, 36 px, borde y separadores, fondo `bg-surface`, la opción activa en `brand-50`/`brand-700`, `px-[13px]` y texto de 13 px, foco hacia adentro, sin partir ni aplastar las opciones. `UsersFilterBar` ya lo usa.
   - `Page`: `backTo` (en lugar del ícono, un enlace de 32 px con borde y `ChevronLeftIcon`, con `aria-label` y `title`) y `status` al lado del `h1`.
   - `ConfirmDialog`: `cancelLabel`.
   - `EmptyState`: `className` (sin el recuadro punteado, adentro de una tarjeta) y `descriptionClassName`.
   - `CheckboxField`, como fila: padding de 9 y 10 px, fondo `surface-muted` al pasar el mouse, la etiqueta de 13,5 px estirada sobre la fila para marcar con un clic en cualquier parte, la descripción de 12,5 px debajo, y la casilla vacía con el borde de `--color-content-muted`.
   - `AppLayout` (en la biblioteca, el caparazón): la raíz en `h-svh`, la columna con `min-h-0`, y `main` `relative`, con `min-h-0` y `overflow-y-auto`.
   - `EyeIcon`, nuevo en el set.
   - Las migas: cuatro niveles en una ruta hija, con el padre como enlace y la hoja que pone la pantalla (`useBreadcrumbLeaf`).
   También la nota del lienzo sobre “Un rol en su propia pantalla” dice “en implementación”: ya está implementado.
4. **`RowActions`**, que la revisión de la Tarea 8 dejó para una tarea aparte porque cambia la pieza y su biblioteca: el separador entre Editar y Eliminar (el `border-0` de cada botón le gana al `divide-x` del grupo; pasa también en usuarios), y que “Ver” y “Editar” sean enlaces, para abrir el rol en otra pestaña o copiar su dirección.
5. **Diferencias a propósito con el tablero que son de piezas compartidas** (Tarea 7): la barra superior mide 64 px y no 60; el título de la banda, 18 px y no 16; el padding del cuerpo, 24 y no 20; los rótulos de campo, 14 px y no 13, con el asterisco del color del rótulo; el código para reportar va como texto; y el vacío y el error usan el recuadro punteado de `EmptyState`. Alinearlas es cambiar esas piezas y su biblioteca.
6. **La variante `dark:` de los componentes de shadcn**, reportada aparte: `index.css` define la clase `.dark`, pero no la variante (`@custom-variant dark`), así que en Tailwind 4 el `dark:` sigue a `prefers-color-scheme`. Con el sistema en modo oscuro, `input`, `textarea`, `checkbox` y compañía aplican sus clases `dark:` sobre una paleta que sigue siendo la clara.
