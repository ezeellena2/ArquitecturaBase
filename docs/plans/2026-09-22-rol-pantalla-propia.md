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

- [ ] **`PermissionPicker`** (`features/roles/components`). Props: `groups`, `picked: readonly string[]`, `onChange(picked)`, `readOnly`. Guarda en su propio estado la búsqueda, “Elegidos” y las áreas abiertas (arrancan con `initialOpenAreas`). Usa `SegmentedControl`, `CheckboxField` y la lógica de la Tarea 5. El buscador es `Input type="search"` con la lupa adentro, sin debounce (filtra en memoria), y corta el Enter. Con `readOnly`, casillas deshabilitadas y sin “Elegir/Quitar todos”.
- [ ] **`RoleSummary`**: la tarjeta “Lo que va a poder hacer”. Props: `groups`, `picked`, `onRemove?` (sin él, los chips no tienen botón).
- [ ] Tests de `PermissionPicker`, por rol y nombre accesible: buscar filtra y no distingue tildes; una búsqueda de solo espacios no filtra; las tres variantes del vacío y su botón que limpia; “Elegidos · N”; “Elegir todos” / “Quitar todos” con su `aria-label` y el “N de M”; abrir y cerrar un área (`aria-expanded`); “Expandir todo” y “Contraer todo”; **cada área es un `group` con su nombre, aunque esté cerrada**; `readOnly`; la descripción es la descripción accesible de la casilla; Enter en el buscador no envía el formulario que lo contiene. De `RoleSummary`: “1 permiso en 1 área”, “3 permisos en 2 áreas”, “Ninguno” con el texto de vacío, y quitar desde un chip.
- [ ] Verificación y commit: `feat: selector de permisos por área, con búsqueda y resumen`.

### Tarea 7: `RoleEditorPage` y sus rutas

Repo: **front**. **Queda igual a “Roles · Editar un rol”, con cada estado de “Roles · Estados y recorrido”.**

- [ ] **Rutas:** `/roles/nuevo` y `/roles/:roleId`, `lazy`, debajo de un `<ProtectedRoute permission="roles.manage" />` propio (el de `/roles` pide `roles.read`). `/roles/nuevo` se declara antes que `/:roleId`.
- [ ] **Datos:** el catálogo con `permissionsQueryKey`. En la edición, `GET /api/roles` con `refetchOnMount: "always"`, y la siembra **cuando ese pedido termina**, como hacía `RoleFormDialog` (copiar el comentario que explica por qué). Se siembra una sola vez, durante el render y no con un efecto.
- [ ] **Admin:** `features/roles/lib/systemRoles.ts` exporta `ADMIN_ROLE_NAME = "Admin"` e `isAdminRole(role)` (`isSystemRole` y ese nombre), con un comentario que diga que refleja `SystemRoles.Admin` del backend.
- [ ] **Guardar:** `POST /api/roles` o `PUT /api/roles/{id}` con `name` recortado, `description` recortada o `null`, y `permissions`. Al terminar bien: toast, invalidar `rolesQueryKey` y `currentUserQueryKey`, `allowNextNavigation()` y `navigate("/roles")`. Errores: `Roles.Role.AlreadyExists` y un 400 con `errors.name` van debajo del nombre; el resto va al alerta de arriba (`roleActionErrorMessage`).
- [ ] **Título y migas** siguen al nombre escrito, con los respaldos de la tabla (sección “La pantalla”, puntos 1 y 2).
- [ ] **Guarda:** `useUnsavedChangesGuard(isDirty)` y el `ConfirmDialog` con los textos `editor.discard.*` (`cancelLabel` = “Seguir editando”, destructivo).
- [ ] **Tests** (`RoleEditorPage.test.tsx`, con MSW y `renderRouteWithProviders`):
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
- [ ] **Comparar con el tablero**, incluida la columna izquierda adherida al scrollear en un navegador, y anotar en el commit cualquier diferencia que quede a propósito.
- [ ] Verificación y commit: `feat: el rol se crea y se edita en su propia pantalla`.

### Tarea 8: El listado navega y el diálogo se va

Repo: **front**.

> **Decisión del usuario (2026-09-22):** en el listado, **Admin** tiene “Ver” (`EyeIcon`, “Ver el rol Admin”), que abre la pantalla de solo lectura, y **User**, “Editar”; ninguno de los dos, “Eliminar”. Está dibujado en el tablero “Roles · Acciones del listado”, que es la referencia de esta tarea.

- [ ] **`RolesPage`:** “Nuevo rol” es un `Button asChild` con un `Link` a `/roles/nuevo`. “Editar” navega a `/roles/{id}`. Las acciones de los roles del sistema, según la decisión de arriba: `EyeIcon` nuevo en el set, con el mismo lenguaje que el tablero (`icons.test.tsx` lo cubre solo); claves `actions.view` “Ver” / “View” y `actions.viewFor` “Ver el rol {{name}}” / “View the {{name}} role”; `systemLocked` se borra.
- [ ] **Se borra `RoleFormDialog.tsx`** y las claves de la lista cerrada de “Textos del front”, cada una después de su `grep`. `parity.test.ts` sigue en verde.
- [ ] **Tests de `RolesPage`:** los del diálogo (grupos con nombre, contador, siembra fresca, alta) se borran de acá porque ya los cubre la Tarea 7. `hides the create and edit actions without roles.manage` pasa a buscar `queryByRole("link", { name: "Nuevo rol" })` (hoy busca un `button` y, con el `Link`, pasaría siempre). Nuevos: con `roles.manage`, el enlace “Nuevo rol” apunta a `/roles/nuevo`; “Editar el rol Soporte” lleva a su pantalla. Los datos de prueba suman un rol `User` del sistema; Admin tiene “Ver” y no “Eliminar”; User tiene “Editar” y no “Eliminar”; sin `roles.manage` no hay “Ver el rol Admin”.
- [ ] Verificación y commit: `feat: el listado de roles lleva a la pantalla del rol`.

### Tarea 9: Documentación y cierre

Repos: **los dos**.

- [ ] **`CLAUDE.md` del front:** la regla “Las acciones se resuelven en diálogos” pasa a decir que un formulario corto va en diálogo y uno largo o seccionado tiene ruta propia (el rol es el caso), con `backTo` en `Page`, `useUnsavedChangesGuard` y `useBreadcrumbLeaf`. La regla del `legend` en `sr-only` pasa a nombrar al selector de permisos. Las rutas de administración suman `/roles/nuevo` y `/roles/{id}` (`roles.manage`). Anotar que `AppLayout` tiene la raíz en `h-svh` para que `main` scrollee, y por qué.
- [ ] **`docs/design/visual-baseline.md`:**
  - fila nueva en el mapa Artifact → proyecto: “Roles · Editar un rol” y “Roles · Estados y recorrido” → `/roles/nuevo`, `/roles/{id}`, `RoleEditorPage`, `PermissionPicker`, `SegmentedControl`: **Implementado**;
  - la comparación cerrada “Alta y edición de un rol: diálogo / pantalla propia → **pantalla propia**, porque con veinte áreas el diálogo no tiene dónde crecer”;
  - en “Encabezados”: una pantalla hija reemplaza el ícono por el botón de volver de 32 px (`backTo`) y puede llevar un estado al lado del título; las migas de una ruta hija tienen cuatro niveles, con el padre como enlace;
  - que la banda adherida recién funciona desde este cambio (el hecho falso de la Fase 5);
  - la revisión del encabezado al 2026-09-22.
- [ ] **Este plan:** tachar los pasos y sumar al final “Resultado de la ejecución” (verificación final, desvíos, pendientes), como en la Fase 5.
- [ ] Commits: `docs: el rol en su propia pantalla` en cada repo.

## Lo que este plan no hace

- **El responsive dibujado.** Los dos tableros son de 1440 px. La pantalla apila las columnas en menos de `lg` para no romperse, pero el diseño móvil sigue siendo el pendiente general del fundamento.
- **`GET /api/roles/{id}`.** La edición sigue pidiendo la lista entera.
- **Las áreas “de ejemplo” del tablero.** No existen: el catálogo real tiene tres.
