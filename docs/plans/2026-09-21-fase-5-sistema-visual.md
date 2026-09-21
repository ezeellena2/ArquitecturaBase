# Fase 5 (Sistema visual) — Plan de implementación

> **Para agentes:** SUB-SKILL REQUERIDA: usar superpowers:subagent-driven-development (recomendado) o superpowers:executing-plans para ejecutar este plan tarea por tarea. Los pasos usan checkboxes (`- [ ]`).

**Objetivo:** que las pantallas dejen de resolver cada una su propio aspecto y pasen a componerse con las mismas piezas. Terminado cuando un listado nuevo se arma con `PageHeader`, `FilterBar` y `DataTable` sin escribir un solo color, alto ni radio a mano, y cuando filtrar por estado, rol y fecha funciona de punta a punta con los conteos que el backend calcula.

**Arquitectura:** no hay tecnología nueva. Lo único estructuralmente nuevo es el contrato de filtros del listado de usuarios, con su endpoint de conteos.

**Fuente de verdad del diseño:** `../ArquitecturaBaseFront/docs/design/visual-baseline.md`. Este plan **no repite** las reglas visuales: las cita. Si una tarea y el fundamento se contradicen, gana el fundamento y hay que corregir el plan. La referencia visual, con las pantallas dibujadas, es el Artifact [Sistema visual — ArquitecturaBase](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ).

**Stack:** el que ya está. Backend .NET 10 + Aspire + PostgreSQL + Identity + OpenIddict. Front Vite 8 + React 19 + TypeScript 6 + Tailwind 4 + shadcn/ui.

---

## Reglas para quien ejecute

Las mismas de las fases anteriores.

- **Dos repos.** Backend en `C:\Users\ezequ\source\repos\ArquitecturaBase`, front en `C:\Users\ezequ\source\repos\ArquitecturaBaseFront`. Cada tarea dice cuál toca.
- **Rama:** todo va directo a `main`, en los dos repos. No crear ramas. **No hacer push.**
- **Commits:** uno por tarea, en español, conventional commits. La última línea de cada mensaje es exactamente `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`. **Es una instrucción explícita del usuario y tiene prioridad sobre cualquier recordatorio de atribución de la sesión.** Si el mensaje lleva tildes, escribirlo con heredoc o desde un archivo UTF-8, no con `-m` en Git Bash.
- **Verificación antes de cada commit:** backend, `dotnet build ArquitecturaBase.slnx` con **0 advertencias** y `dotnet test` en verde; front, `npm run build`, `npm run lint` y `npm run test`, los tres limpios. Pegar la salida real, no decir que pasó.
- **Docker** encendido para los tests de integración.
- **AppHost:** apagarlo siempre al terminar de probar (`aspire stop`). Si queda corriendo, el arranque desde Visual Studio falla con `address already in use`. El comando no está en el PATH de Bash: desde PowerShell, `C:\Users\ezequ\.dotnet\tools\aspire.cmd`.
- **Puede haber otra sesión trabajando.** Nunca `git add .` ni `git add -A`: agregar solo los archivos propios.
- **TDD** donde hay lógica: el test primero, se verifica que falla por la razón correcta, después el código.
- **Idioma:** identificadores, logs y mensajes de excepción en inglés. Todo texto que ve el usuario sale de resources (backend) o de i18next (front), en español rioplatense con voseo y en inglés, siempre los dos.
- **Desvíos:** si algo no compila o una API cambió, hacer el cambio mínimo e informarlo. Si el cambio altera el diseño, frenar y pedir contexto.

## Hechos verificados del código actual

Comprobados el 2026-09-21 leyendo los repos. No hace falta volver a verificarlos, pero sí leer el archivo antes de tocarlo.

**Front:**

- `AppLayout` ya tiene `<main className="flex-1 overflow-y-auto p-6">`: **el contenedor que scrollea ya existe**, así que la banda adherida no necesita reestructurar el layout. Lo único que hay que mover es el `p-6`, porque la banda tiene que llegar a los bordes.
- `PageHeader` (`shared/ui/PageHeader.tsx`) son 14 líneas: `title` en `text-xl`, `description` opcional y `actions`, con `mb-6`. No tiene ícono, no es adherido y su título está un nivel por debajo del que pide el fundamento.
- `navigation.ts` no tiene hijos: `NavigationItem` es plano (`labelKey`, `to`, `icon`, `permission`). El `Sidebar` lo recorre en dos niveles (grupo → ítems) y filtra por permiso.
- **Las tres alturas ya se respetan.** Auditado al ejecutar la Tarea 1: `Input` y `Button` por defecto son `h-9` (36 px), `Button size="sm"` es `h-8` (32) y `TableHead` es `h-10` (40). El único tamaño fuera de la escala es `Button size="lg"` (40 px), que no usa ninguna pantalla y viene del archivo generado por shadcn.
  > La versión original de este plan decía que el buscador medía 38 y el botón 36. **Era falso**: los dos usan `h-9`. El dato venía de una maqueta del Artifact, no del código, y pasó al fundamento visual y de ahí acá sin verificarse. Corregido en los dos lugares.
- `useRestoreFocusOnClose` (`shared/hooks`) ya existe y lo usan los cuatro diálogos.
- `formatDateTimeInZone` (`shared/lib/dateTime`) ya es el único formateador de fecha.
- `FormField`, `ConfirmDialog`, `DataTable`, `CheckboxField` y `Pagination` ya existen y respetan el fundamento salvo por los tokens que faltan.
- `usePagination` (`shared/hooks`) ya guarda `page`, `pageSize`, `sort` y `search` en la URL, con `replace: true`, omitiendo los valores por defecto, y vuelve a la página 1 al cambiar búsqueda u orden. **Los filtros nuevos tienen que entrar por ahí, no por un mecanismo paralelo.**
- El único `{{count}}` del proyecto es `roles:permissionsCount`, y ya tiene sus formas `_one`/`_other`. Cualquier clave nueva con `count` las necesita: sin ellas i18next cae a la base y escribe "1 usuarios".

**Backend:**

- `GetUsersQuery` solo hereda `PagedRequest` (página, tamaño, orden y búsqueda por texto). **Estado, rol y fecha no existen.**
- `IIdentityService.ListUsersAsync(PagedRequest request, …)` recibe la base, no la consulta: para filtrar hay que cambiar esa firma.
- `ListUsersAsync` arma el `IQueryable` con `userManager.Users.AsNoTracking()`, aplica la búsqueda con `EF.Functions.ILike` escapando `%` y `_`, ordena con `ApplySort` y pagina con `ToPagedResultAsync`.
- `PagedRequest` fija `MaxPageSize = 100`, `MaxPage = 1_000_000` y `MaxSearchLength = 100`.
- Los roles de un usuario salen de `dbContext.UserRoles`; el `UserCount` de un rol ya se cuenta así en `LoadRolesAsync`, y arrastra el filtro global de borrado lógico.
- `JsonStringEnumConverter` ya está registrado: los enums viajan por nombre.

## Contratos

### `GET /api/users` — filtros nuevos

Se suman tres parámetros, todos opcionales. Los que ya estaban no cambian.

| Parámetro | Tipo | Qué hace |
|---|---|---|
| `isActive` | `bool?` | `true` solo activos, `false` solo inactivos, ausente todos |
| `role` | `string?` | nombre exacto de un rol existente |
| `createdWithinDays` | `int?` | creados en los últimos N días, contra `TimeProvider` |

Validación (`GetUsersQueryValidator`, que ya hereda de `PagedRequestValidator<T>`):

- `role`, si viene, no puede estar vacío ni medir más de `ValidationRules.RoleNameMaxLength`. **No se valida contra la lista de roles:** un rol que no existe devuelve cero resultados, no un 400. Un 400 ahí filtraría qué nombres de rol existen.
- `createdWithinDays`, si viene, entre 1 y 3650.

### `GET /api/users/filter-counts` — los conteos por opción

Permiso: `users.read`, el mismo que el listado.

Recibe **los mismos parámetros de filtro** que el listado (sin `page`, `pageSize` ni `sort`) y devuelve, para cada dimensión, el conteo que daría cada valor **con los demás filtros aplicados e ignorando el propio**. Eso es lo que hace que los números no mientan: el conteo de "Admin" es cuántos quedarían si además de lo que ya está puesto se filtrara por Admin.

```json
{
  "status": { "all": 148, "active": 132, "inactive": 16 },
  "roles": [
    { "name": "Admin", "count": 12 },
    { "name": "User", "count": 134 },
    { "name": "Soporte", "count": 0 }
  ],
  "createdWithin": [
    { "days": 7, "count": 12 },
    { "days": 30, "count": 48 }
  ]
}
```

- `roles` trae **todos** los roles del sistema, incluidos los que dan cero: la opción con cero se muestra apagada, y para eso hay que saber que existe.
- `createdWithin` trae los mismos tramos que ofrece la interfaz (7 y 30). Si mañana se agrega un tramo, se agrega acá.
- Es un endpoint aparte y no un campo del listado a propósito: `PagedResult<T>` es genérico y compartido por todos los listados, y meterle facetas lo ataría a este caso.

## Estructura de archivos

Front, lo que se agrega o cambia:

```
src/index.css                          (+ --color-surface-header)
src/shared/ui/
  PageHeader.tsx                       (se rehace: banda adherida)
  RowActions.tsx                       (nuevo)
  icons.tsx                            (+ PowerIcon, TrashIcon, PencilIcon, SlidersIcon, SearchIcon)
src/shared/hooks/
  useFilters.ts                        (nuevo, al lado de usePagination)
src/shared/api/
  users.ts                             (+ los parámetros de filtro y fetchFilterCounts)
src/features/users/components/
  UsersFilterBar.tsx                   (nuevo)
src/layouts/
  navigation.ts                        (NavigationItem gana children)
  components/Sidebar.tsx               (grupo desplegable)
  AppLayout.tsx                        (el padding sale de main)
```

Backend, lo que se agrega o cambia:

```
src/ArquitecturaBase.Application/
  Abstractions/Identity/UserListRequest.cs         (nuevo)
  Abstractions/Identity/UserFilterCounts.cs        (nuevo)
  Abstractions/Identity/IIdentityService.cs        (dos firmas)
  Features/Users/GetUsers/GetUsersQuery.cs         (hereda de UserListRequest)
  Features/Users/GetUsers/GetUsersQueryValidator.cs
  Features/Users/GetUserFilterCounts/              (consulta nueva)
src/ArquitecturaBase.Infrastructure/
  Identity/IdentityService.cs                      (filtros y conteos)
src/ArquitecturaBase.Api/Endpoints/Users/
  UsersEndpoints.cs                                (+ /filter-counts)
```

## Tareas

| # | Tarea | Repo | Tests |
|---|---|---|---|
| 1 | El token que falta y las tres alturas | front | los que ya hay |
| 2 | `RowActions`: ícono, tooltip y nombre accesible | front | unitarios |
| 3 | Los íconos que faltan del set | front | unitarios |
| 4 | `PageHeader` como banda adherida | front | unitarios |
| 5 | Submenú desplegable en el menú lateral | front | unitarios |
| 6 | La tabla de usuarios con las piezas nuevas | front | los de `UsersPage` |
| 7 | Backend: los tres filtros del listado | backend | unitarios + integración |
| 8 | Backend: el endpoint de conteos | backend | integración |
| 9 | `useFilters` y la barra de filtros | front | unitarios |
| 10 | El diálogo de rol con permisos agrupados | front | unitarios |
| 11 | Documentación y cierre | ambos | — |

El orden no es arbitrario: las tareas 1 a 3 no cambian ninguna pantalla, solo dejan las piezas listas. Si se empieza por las pantallas, hay que tocarlas dos veces.

---

### Tarea 1: El token que falta y las tres alturas

Repo: **front**.

El fundamento fija una banda de encabezado con más contraste que `surface-muted`, y tres alturas de control. Hoy no existe el token y el buscador mide 38 donde el botón mide 36.

- [x] **Paso 1: el token**

En `src/index.css`, dentro del bloque `@theme`, después de `--color-surface-muted`:

```css
  /* La banda que abre una superficie: encabezado de tabla, de diálogo, de panel y rótulo de grupo.
     Es más oscura que surface-muted a propósito: con surface-muted el encabezado de una tabla no se
     despega de sus filas, que es lo que hacía que la tabla se viera como un bloque sin cabeza. */
  --color-surface-header: oklch(0.945 0.008 250);
  --color-surface-header-border: oklch(0.86 0.012 250);
```

- [x] **Paso 2: usarlo donde ya había un gris a mano**

Buscar en `src/` los encabezados de tabla y reemplazar el fondo por `var(--color-surface-header)` y el borde inferior por `var(--color-surface-header-border)`. Hoy `DataTable` usa el borde común; el encabezado pasa a llevar el suyo.

- [x] **Paso 3: las tres alturas**

Auditar `src/` buscando `h-8`, `h-9`, `h-10` y alturas escritas a mano. Todo control tiene que caer en una de tres: **32** acción de ícono, **36** control, **44** fila.

**Resultado de la auditoría: no había nada que arreglar.** `Input` y `Button` por defecto son `h-9`, `Button size="sm"` es `h-8`, `TableHead` es `h-10`. `Button size="lg"` (`h-10`) queda fuera de la escala pero no lo usa ninguna pantalla y está en un archivo generado por shadcn, así que se deja: un `add` lo volvería a escribir.

- [x] **Paso 4: verificación y commit**

`npm run build`, `npm run lint`, `npm run test`. Ningún test debería cambiar: esto es puramente visual.

```bash
git add src/index.css src/shared/ui
git commit -F - <<'EOF'
feat: el token de la banda de encabezado y las tres alturas

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
```

---

### Tarea 2: `RowActions`

Repo: **front**.

Hoy cada listado arma su columna de acciones a mano. El fundamento pide ícono + tooltip + nombre accesible **con el dato de la fila**, y que la acción sin permiso no se dibuje. Que eso sea un componente es lo que evita que la próxima tabla lo resuelva distinto.

- [x] **Paso 1: el test (tiene que fallar)**

Crear `src/shared/ui/RowActions.test.tsx`. Los casos:

1. cada acción es un `button` cuyo nombre accesible es el que se le pasó, con el dato de la fila;
2. el SVG está oculto para el lector (`aria-hidden`), así el botón no se anuncia dos veces;
3. al pasar el mouse aparece el tooltip con el texto corto;
4. una acción con `hidden: true` no se renderiza — no se dibuja deshabilitada;
5. la acción destructiva va última y lleva su clase de peligro.

- [x] **Paso 2: el componente**

`src/shared/ui/RowActions.tsx`. La forma:

```tsx
export interface RowAction {
  /// Texto corto del tooltip: "Roles", "Desactivar", "Eliminar".
  label: string;
  /// Nombre accesible completo, con el dato de la fila: "Eliminar a ana@ejemplo.com".
  accessibleName: string;
  icon: ComponentType<{ className?: string }>;
  onSelect: () => void;
  /// Lo que no se puede deshacer. Va última y se tiñe solo al interactuar.
  destructive?: boolean;
  /// Sin permiso no se dibuja. Un botón deshabilitado no recibe foco, así que con teclado no hay
  /// forma de llegar a saber por qué está apagado.
  hidden?: boolean;
}
```

Las acciones van en un control segmentado (un solo borde alrededor, separadores entre botones), 32 px de alto, ícono 18. **La destructiva va en su propio grupo**, separada por 6 px: el fundamento pide "última y separada", y dentro del mismo segmentado no queda separada de nada. Es una diferencia menor con la maqueta del Artifact, que las dibujaba en un solo grupo. El tooltip es CSS puro sobre `:hover` y `:focus-visible`: un Tooltip de Radix por cada botón de cada fila son cientos de componentes montados en un listado de cien filas.

- [x] **Paso 3: verificación y commit**

---

### Tarea 3: Los íconos que faltan

Repo: **front**.

`icons.tsx` no tiene los de las acciones de fila ni los de la barra de filtros.

- [x] **Paso 1: agregarlos**

`PowerIcon` (activar/desactivar), `TrashIcon`, `PencilIcon`, `SlidersIcon` (más filtros) y `SearchIcon`. Mismo trazo 1.75, grilla de 24, puntas redondeadas, `aria-hidden` heredado del `Icon` que ya está.

- [x] **Paso 2: un test para todo el set** *(agregado al ejecutar; el plan no lo pedía)*

`icons.test.tsx` recorre todo lo que exporta `icons.tsx` y afirma tres cosas por ícono: que renderiza un `svg`, que se oculta del lector y que usa el trazo 1.75. **Un `d` con un typo no rompe nada** —el SVG queda vacío y el botón en blanco—, así que sin esto no lo atrapa nadie. Se verificó en rojo vaciando un ícono a propósito.

La tabla de tareas de arriba decía "Tests: —" para esta tarea. Era un error de criterio: un set de íconos es justo lo que se rompe en silencio.

- [x] **Paso 3: verificación y commit**

---

### Tarea 4: `PageHeader` como banda adherida

Repo: **front**.

La decisión está cerrada en el fundamento, sección "Encabezados": banda fija de 56 px, adherida, con ícono 32, el título de la pantalla en el nivel *título de sección* y la acción primaria. Sin antetítulo ni descripción.

- [x] **Paso 1: sacar el padding de `main`**

En `AppLayout`, `<main className="flex-1 overflow-y-auto p-6">` pasa a `<main className="flex-1 overflow-y-auto">`. **El padding se mueve a cada pantalla**, porque la banda tiene que llegar a los bordes y el resto del contenido no. Cada página envuelve su contenido en un `div` con el padding.

> **Desvío tomado:** el padding no quedó en cada pantalla. `Page` dibuja la banda **y** el cuerpo con su padding, en un solo componente. Un padding que cada pantalla tiene que acordarse de poner es exactamente el criterio que el fundamento visual existe para evitar: la primera que se olvide queda distinta y nadie lo nota hasta verla. `PageHeader` se borró.

- [x] **Paso 2: el test (tiene que fallar)**

`src/shared/ui/Page.test.tsx` (no `PageHeader.test.tsx`, por el desvío del paso 1): que el título salga como `heading` de nivel 1 y sea el único, que el ícono esté oculto para el lector, que la acción se renderice, y que el contenido quede **fuera** del `<header>` —lo que prueba que la banda y el cuerpo son dos regiones y no una—. Falló primero por el import que no existía.

- [x] **Paso 3: el componente**

Recibe `icon`, `title` y `actions`. **`description` se elimina de la firma**: quien la esté pasando hoy (`UsersPage`, `RolesPage`, `SettingsPage`, `ProfilePage`) deja de hacerlo, y su texto se borra de los `.json` de los dos idiomas.

> **Desvío a informar si aparece:** si alguna pantalla usa la descripción para decir algo que no está en ningún otro lado, frenar y preguntar antes de borrarla. El fundamento dice que el grupo ya se lee en el menú y las migas; no dice que se pueda perder información.

> **No apareció:** las cuatro descripciones repetían el título con otras palabras ("Administrá las cuentas del sistema" sobre *Usuarios*). Se borraron de los ocho `.json` sin perder nada.

`DashboardPage` también pasó a `Page`, aunque no tenía `PageHeader`: es la única forma de que su título se vea como el de las demás. Su cuerpo sigue vacío a propósito. Y se sumó `UserIcon` (una persona) a `icons.tsx` para `/perfil`, distinto de `UsersIcon` (dos), que es el de la sección.

- [x] **Paso 4: verificación y commit**

`npm run build`, `npm run lint` y `npm run test` (192 en verde). Commit `3bb248d` en el front.

---

### Tarea 5: Submenú desplegable

Repo: **front**.

Fundamento, sección "Navegación". Agrupar en el menú **no cambia las rutas**: `/usuarios` y `/roles` se quedan donde están.

- [x] **Paso 1: el modelo**

`NavigationItem` gana `children?: NavigationItem[]`. Un ítem con hijos no tiene `to`: es un grupo desplegable, no un enlace. `navigation.ts` pasa a:

```ts
{
  labelKey: "navigation.administration",
  items: [
    {
      labelKey: "navigation.userManagement",
      icon: UsersIcon,
      children: [
        { labelKey: "navigation.users", to: "/usuarios", icon: UsersIcon, permission: "users.read" },
        { labelKey: "navigation.roles", to: "/roles", icon: ShieldIcon, permission: "roles.read" },
      ],
    },
    { labelKey: "navigation.settings", to: "/configuracion", icon: SettingsIcon, permission: "settings.manage" },
  ],
}
```

> **Desvío tomado:** no es un `children?` opcional sino una unión (`NavigationLink | NavigationBranch`, con `isBranch`). Con `to` opcional, cada uso —el `key` del `li`, las migas, el `NavLink`— habría terminado en un `item.to!` o un `?? ""`, que es apagar el chequeo justo donde el modelo dejó de ser uniforme. `navigation.ts` exporta además `navigationLinks` (la lista plana, para las migas) y `branchOf(pathname)`.

- [x] **Paso 2: los tests (tienen que fallar)**

En `Sidebar.test.tsx`:

1. el grupo se dibuja como `button` con `aria-expanded`, y apretarlo lo pliega;
2. **el grupo se abre solo si la ruta activa es uno de sus hijos**, y se queda abierto al navegar entre ellos;
3. **quien tiene `users.read` pero no `roles.read` ve un solo hijo**;
4. **quien no tiene ninguno de los dos no ve el grupo**, ni siquiera plegado;
5. contraída, el grupo muestra su ícono y sus hijos quedan accesibles (decidir: tooltip con la lista, o desplegar al expandir; lo que se elija va al fundamento).

El caso 3 es el que importa: es el riesgo real del patrón de submenús, que se dibujen todos los hijos y el permiso se controle recién al entrar.

Fallaron 8 de 11 antes de tocar el componente. El caso 5 se decidió con el usuario: **contraída, los hijos suben a la lista como íconos sueltos** y no hay grupo. Contraída la barra es un lanzador y no un mapa, y un desplegable de 40 px cambiaría un clic por dos. Y con la barra expandida, **el grupo arranca plegado salvo que la ruta activa sea un hijo** (la otra opción era abierto por defecto). Las dos quedaron en el fundamento, en "Navegación" y en "Comparaciones cerradas".

- [x] **Paso 3: el `Sidebar`**

Recorrer tres niveles. El estado de plegado vive en `useLocalStorage` con clave propia, como el de la barra contraída, pero **el grupo de la ruta activa se abre siempre**, aunque estuviera plegado: si no, al recargar `/roles` el menú no muestra dónde estás.

> **Desvío tomado:** el plegado **no** se persiste; vive en `useState`. Con "plegado salvo el activo", guardarlo lo dejaría abierto para siempre apenas entrás una vez a `/usuarios`, que es la opción que el usuario descartó, tomada por la puerta de atrás. Quedar abierto porque estás parado adentro no es una preferencia. Y el grupo activo se abre **al cambiar de grupo**, no siempre: si fuera siempre, su botón tendría `aria-expanded="true"` y no podría cerrarse, que es un control muerto.
>
> Los hijos van **sin ícono** en el submenú: con ícono, su texto arrancaba a 67 px contra los 44 del padre. La sangría y la guía vertical ya cuentan la jerarquía. El ícono sigue en el modelo porque lo usa la barra contraída.

- [x] **Paso 4: los textos**

`navigation.userManagement` en los dos idiomas: "Gestión de usuarios" / "User management".

- [x] **Paso 5: verificación y commit**

> **Agregado que el plan no tenía:** las **migas**. El fundamento dice que tienen tres niveles (`Inicio / Gestión de usuarios / Usuarios`) y que el grupo se lee "en el menú y en las migas" —que fue lo que justificó sacarle la descripción a las pantallas en la Tarea 4—. Sin esto, ese segundo lugar no existía. `Breadcrumbs` usa `branchOf`; el grupo va como texto, no como enlace, porque no tiene ruta. Con su `Breadcrumbs.test.tsx`.

`npm run build`, `npm run lint` y `npm run test` (201 en verde, 38 archivos). Commit `0e1f4bd` en el front, con el fundamento actualizado.

---

### Tarea 6: La tabla de usuarios con las piezas nuevas

Repo: **front**.

- [x] **Paso 1: los tests que cambian**

`UsersPage.test.tsx` ya afirma los nombres accesibles de las acciones ("Editar los roles de ana@example.com"). **Esos casos no deberían cambiar**: `RowActions` conserva los mismos nombres. Si un test se rompe por el nombre, es que el componente lo armó distinto y hay que arreglar el componente, no el test.

No cambió ninguno: los 16 pasaron sin tocarse.

- [x] **Paso 2: la columna de acciones pasa a `RowActions`**

En `columns.tsx`, la celda de acciones devuelve `<RowActions actions={[…]} />`. El `hidden` reemplaza al `if (!actions) return columns` de hoy para las acciones sueltas; la columna entera se sigue omitiendo si no hay ninguna.

Íconos: escudo para Roles, botón de encendido para Activar/Desactivar, tacho para Eliminar. Hoy las tres acciones dependen del mismo permiso (`users.manage`), así que `hidden` todavía no tiene usuario y el `if (!actions)` se queda: existe para cuando una acción suelta tenga permiso propio.

> **Agregado que el plan no tenía: la tabla de roles.** El plan solo convertía usuarios, y la Tarea 11 iba a documentar "`RowActions` para toda columna de acciones" mientras `/roles` seguía con botones de texto: dos tablas con dos estéticas, que es exactamente lo que esta fase viene a terminar. Lápiz para editar y tacho para eliminar. Los roles del sistema siguen mostrando por qué no se tocan en vez de un grupo de botones vacío: `RowActions` devuelve `null` si no queda ninguna acción, y ese texto es información que no está en ningún otro lado.

- [x] **Paso 3: densidad, estado y encabezado**

Filas de 44 px, encabezado de 40 con la banda nueva, estado como punto + texto (ya está así).

> **Hecho falso del plan, corregido:** el estado **no** estaba como punto + texto, era una píldora `Badge` de color. Es el segundo dato del plan que se dio por verificado sin serlo (el otro fue el buscador de 38 px). Se implementó punto + texto: en veinte filas, veinte fondos teñidos compiten con los datos. El punto es `aria-hidden` y la palabra es lo que se lee, con un test propio, porque "el color nunca comunica solo" es de las reglas que se rompen sin que nadie se entere.
>
> La densidad sí estaba: `TableHead` trae `h-10` de shadcn y las filas quedaron en `h-11` en la Tarea 1.

- [x] **Paso 4: verificación y commit**

`npm run build`, `npm run lint` y `npm run test` (202 en verde, 38 archivos). Commit `a08d9bc` en el front.

---

### Tarea 7: Backend, los tres filtros

Repo: **backend**.

- [ ] **Paso 1: mover los filtros a las abstracciones**

Crear `Application/Abstractions/Identity/UserListRequest.cs`:

```csharp
/// <summary>
/// Lo que el listado de usuarios sabe filtrar. Vive en las abstracciones y no en la consulta para que
/// IIdentityService no tenga que conocer un tipo de Features: la interfaz es de Abstractions y las
/// dependencias no van en esa dirección.
/// </summary>
public abstract record UserListRequest : PagedRequest
{
    public bool? IsActive { get; init; }

    public string? Role { get; init; }

    public int? CreatedWithinDays { get; init; }
}
```

`GetUsersQuery` pasa a `public sealed record GetUsersQuery : UserListRequest, IQuery<PagedResult<UserListItem>>` y `IIdentityService.ListUsersAsync` a recibir `UserListRequest`.

- [ ] **Paso 2: los tests (tienen que fallar)**

Unitarios del validador: `role` vacío da 400, `role` de 200 caracteres da 400, `createdWithinDays` en 0 o en 4000 da 400. **Y uno que afirma que un `role` que no existe NO da 400**, que es la decisión de no filtrar la existencia de roles por el código de respuesta.

De integración, en `UsersEndpointsTests`: filtrar por `isActive=false` trae solo inactivos; por `role=Admin` solo los que lo tienen; por `createdWithinDays=7` solo los recientes; y los tres combinados se intersecan.

- [ ] **Paso 3: la consulta**

En `IdentityService.ListUsersAsync`, después de la búsqueda:

```csharp
if (request.IsActive is { } isActive)
{
    users = users.Where(user => user.IsActive == isActive);
}

if (!string.IsNullOrWhiteSpace(request.Role))
{
    // El nombre normalizado es el que tiene índice. Comparar por Name haría un scan.
    var normalized = roleManager.NormalizeKey(request.Role);
    users = users.Where(user => dbContext.UserRoles.Any(userRole =>
        userRole.UserId == user.Id
        && dbContext.Roles.Any(role => role.Id == userRole.RoleId && role.NormalizedName == normalized)));
}

if (request.CreatedWithinDays is { } days)
{
    var desde = timeProvider.GetUtcNow().UtcDateTime.AddDays(-days);
    users = users.Where(user => user.CreatedAtUtc >= desde);
}
```

La fecha sale de `TimeProvider`, nunca de `DateTime.UtcNow`: `BannedSymbols.txt` rompe el build.

- [ ] **Paso 4: verificación y commit**

---

### Tarea 8: Backend, el endpoint de conteos

Repo: **backend**.

- [ ] **Paso 1: el contrato**

Crear `Features/Users/GetUserFilterCounts/` con su consulta, su handler y su respuesta, según la sección "Contratos" de este plan. La consulta hereda de `UserListRequest` pero **ignora `Page`, `PageSize` y `Sort`**.

- [ ] **Paso 2: los tests (tienen que fallar)**

De integración:

1. sin filtros, `status.all` es el total y `active + inactive == all`;
2. **con `isActive=true` puesto, los conteos de `roles` reflejan solo activos** — esa es la parte que hace que los números no mientan;
3. **el conteo de `status` ignora el propio `isActive`**: con `isActive=true` puesto, `inactive` sigue diciendo cuántos inactivos hay, porque es lo que pasaría si se cambiara la opción;
4. un rol sin usuarios aparece con `count: 0`, no se omite;
5. sin `users.read` da 403.

- [ ] **Paso 3: el cálculo**

En `IdentityService`, un método que arma el `IQueryable` base **una sola vez** con los filtros que no son de la dimensión que se está contando, y proyecta los conteos. Son tres consultas de agregación; no se resuelven en memoria.

> **Riesgo aceptado, anotarlo en el resultado:** son tres consultas extra por cada pedido del listado. Con miles de usuarios es despreciable; con cientos de miles hay que medir antes de seguir sumando dimensiones.

- [ ] **Paso 4: el endpoint**

En `UsersEndpoints`, `MapGet("/filter-counts", …)` con `.RequirePermission(Permissions.Users.Read)`.

- [ ] **Paso 5: verificación y commit**

---

### Tarea 9: `useFilters` y la barra de filtros

Repo: **front**.

Fundamento, sección "Listados y filtros".

- [ ] **Paso 1: los tests (tienen que fallar)**

De `useFilters`, al lado de los de `usePagination`:

1. un filtro se lee de la query string y se escribe ahí, con `replace: true`;
2. un filtro en su valor por defecto **no aparece en la URL**;
3. cambiar un filtro **vuelve a la página 1**;
4. `limpiar()` saca todos los filtros y la búsqueda, y deja el orden.

De la barra, en `UsersPage.test.tsx`:

5. los chips aparecen solo con filtros puestos, y cada uno se quita solo;
6. sin resultados y con filtros, el vacío dice cuántos hay y ofrece limpiarlos;
7. sin resultados y sin filtros, dice que no hay usuarios cargados — son dos textos distintos;
8. las opciones del desplegable muestran su conteo, y **la de cero queda deshabilitada**.

- [ ] **Paso 2: `useFilters`**

Al lado de `usePagination` y con la misma forma. No duplicar la lectura de la query string: `useFilters` se apoya en `useSearchParams` igual que `usePagination`, y **cambiar un filtro resetea `page` por el mismo camino que ya usa el cambio de búsqueda**.

- [ ] **Paso 3: `UsersFilterBar`**

Buscador + segmentado de estado + desplegable de rol + "Más filtros" con contador + los chips. Los conteos salen de `GET /api/users/filter-counts`, en su propia consulta de TanStack Query con la misma clave de filtros que el listado, para que las dos se invaliden juntas.

- [ ] **Paso 4: el vacío que distingue**

`DataTable` ya recibe `emptyTitle` y `emptyDescription`. La pantalla le pasa el texto según haya filtros o no, y **el botón de limpiar** cuando los hay.

- [ ] **Paso 5: los textos, en los dos idiomas**

Las claves con `count` (cuántos filtros aplicados) necesitan `_one` y `_other`.

- [ ] **Paso 6: verificación y commit**

---

### Tarea 10: El diálogo de rol con permisos agrupados

Repo: **front**.

`RoleFormDialog` ya existe y ya se siembra con datos frescos. Lo que cambia es cómo se ven los permisos: hoy son `fieldset` sueltos, y el fundamento pide la banda de superficie.

- [ ] **Paso 1: el test (tiene que fallar)**

Que cada área siga siendo un `group` con su nombre accesible, aunque el rótulo visible sea otro elemento. **Ese es el punto delicado:** estilar un `<legend>` obliga a trucos frágiles, así que el `legend` va oculto para el lector y la banda visible va aparte con `aria-hidden`. El test existe para que nadie "limpie" el legend oculto y se lleve puesto el nombre del grupo.

- [ ] **Paso 2: el diálogo**

Cada área en su caja con la banda; el contador de permisos elegidos arriba, al lado del rótulo; el cuerpo scrollea y el pie no.

- [ ] **Paso 3: verificación y commit**

---

### Tarea 11: Documentación y cierre

Repos: **los dos**.

- [ ] **Paso 1: el fundamento visual**

Actualizar `docs/design/visual-baseline.md`: el mapa Artifact → proyecto pasa de "pendiente" a "implementado" en las filas que correspondan, y la revisión del encabezado sube de fecha.

- [ ] **Paso 2: el `CLAUDE.md` del front**

Sumar lo que quedó forzado por construcción: `RowActions` para toda columna de acciones, `useFilters` para todo listado filtrable, y que `PageHeader` ya no recibe descripción.

- [ ] **Paso 3: el `CLAUDE.md` del backend**

La sección de paginado gana los filtros: qué se valida, qué no, y por qué un rol inexistente devuelve cero en vez de 400.

- [ ] **Paso 4: verificación por comandos**

Backend con 0 advertencias y todo verde; front con los tres comandos limpios. Pegar los totales reales.

- [ ] **Paso 5: humo con el AppHost**

`aspire run --detach`, `aspire describe`, y contra `https://localhost:5173`:

```bash
curl -sk -o /dev/null -w "%{http_code}\n" --max-time 10 https://localhost:5173/usuarios
curl -sk --max-time 10 "https://localhost:5173/api/users/filter-counts" | head -3
```

Esperado: `200` en la ruta del SPA y `401` con ProblemDetails en la de la Api. **Apagarlo al terminar.**

- [ ] **Paso 6: el checklist para el usuario**

Lo que necesita una persona:

1. El menú muestra "Gestión de usuarios" plegable, con Usuarios y Roles adentro. Con una cuenta que solo tenga `users.read`, se ve un solo hijo.
2. Filtrar por estado, rol y fecha cambia el listado, y los números de cada opción coinciden con lo que trae al elegirla.
3. Filtrar hasta que no quede nada muestra el vacío con la cuenta de filtros y el botón de limpiar.
4. Compartir la URL con filtros puestos abre el mismo listado filtrado.
5. El encabezado se queda arriba al scrollear un listado largo.
6. Las acciones de fila se entienden solo con los íconos, y el tooltip aparece también al llegar con el teclado.

- [ ] **Paso 7: el resultado**

Agregar a este plan la sección "Resultado de la ejecución" con la estructura de las fases anteriores: tests por proyecto, desvíos, riesgos aceptados y pendientes. Anotar como mínimo el riesgo de las tres consultas de agregación por pedido.

---

## Lo que esta fase no hace

Queda escrito para que no se cuele por el costado:

- **Responsive.** Ningún tablero del Artifact está dibujado abajo de 1440 px. El fundamento tiene la sección escrita pero sin dibujo, así que **esta fase no toca responsive**: hacerlo sin diseño es volver a la situación que el fundamento existe para evitar. Es la primera candidata para la Fase 6, y empieza dibujando, no programando.
- **Selección y acciones masivas.** No hay contrato de backend y la maqueta que existía usaba checkboxes que no se exponían en el árbol de accesibilidad.
- **Modo oscuro.** Es la prueba de fuego de si los tokens están bien puestos, pero no entra acá.
- **Pantalla de inicio, 403 y 404, y la marca real.**
