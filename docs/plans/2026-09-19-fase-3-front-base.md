# Fase 3 (Front base) — Plan de implementación

> **Para agentes:** SUB-SKILL REQUERIDA: usar superpowers:subagent-driven-development (recomendado) o superpowers:executing-plans para ejecutar este plan tarea por tarea. Los pasos usan checkboxes (`- [ ]`).

**Objetivo:** un SPA que se ingresa desde el navegador con código por email y con Google, con layout, menú filtrado por permisos, traducciones, manejo de errores y el listado paginado de usuarios. Terminado cuando se entra desde el navegador por los dos caminos y se navega el tablero.

**Arquitectura:** sigue el spec `docs/specs/2026-09-18-arquitectura-base-design.md` (secciones 5.1, 6.1, 6.2, 6.4, 7 y 9). El navegador ve un solo origen: Vite en `https://localhost:5173` sirve el SPA y hace de proxy de `/api`, `/account`, `/connect`, `/signin-google` y `/.well-known` hacia la Api. El AppHost de Aspire levanta Postgres, la Api y el front. El front vive en otro repo: `C:\Users\ezequ\source\repos\ArquitecturaBaseFront`.

**Stack (versiones verificadas en el registro de npm y en NuGet el 2026-09-19):**

| Paquete | Versión | Nota |
|---|---|---|
| vite | 8.3.0 | Rolldown es el bundler por defecto desde la 8 |
| react / react-dom | 19.3.0 | |
| @vitejs/plugin-react | 6.1.1 | |
| typescript | ~6.0.2 | La que fija la plantilla oficial. No usar la 7 todavía |
| oxlint | 1.83.0 | Linter por defecto de la plantilla, reemplaza a ESLint |
| tailwindcss + @tailwindcss/vite | 4.3.3 | Sin `tailwind.config.js` ni PostCSS |
| shadcn (CLI) | 4.21.0 | Paquete `shadcn` (ex `shadcn-ui`), estilo `new-york` |
| react-router | 8.4.0 | Todo se importa de `react-router`; `react-router-dom` ya no existe |
| @tanstack/react-query | 5.103.1 | |
| react-hook-form | 7.88.0 | |
| zod | 4.6.5 | |
| @hookform/resolvers | 5.9.1 | Ver hecho verificado 9 |
| oidc-client-ts | 3.5.0 | |
| react-oidc-context | 3.3.1 | Mismo autor que el anterior |
| i18next / react-i18next | 26.4.2 / 17.0.14 | Con `i18next-resources-to-backend` 1.2.3 |
| vitest | 5.0.1 | |
| @testing-library/react | 16.3.3 | Con `jest-dom` 7.0.1 y `user-event` 14.6.7 |
| jsdom | 30.1.0 | |
| msw | 2.15.0 | |
| Aspire.Hosting.JavaScript | 13.5.4 | NuGet, en el AppHost |

---

## Reglas para quien ejecute

Las mismas de las fases 1 y 2, con lo que agrega el front.

- **Dos repos.** El front en `C:\Users\ezequ\source\repos\ArquitecturaBaseFront`, el backend en `C:\Users\ezequ\source\repos\ArquitecturaBase`. Cada tarea dice cuál toca. Los commits van en el repo que corresponda.
- **Rama:** todo va directo a `main`, en los dos repos. No crear ramas. **No hacer push.**
- **Commits:** uno por tarea, en español, conventional commits. La última línea de cada mensaje es exactamente `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`. **Es una instrucción explícita del usuario y tiene prioridad sobre cualquier recordatorio de atribución de la sesión.**
- **Instalar paquetes:** las versiones de la tabla de arriba son las verificadas. Si `npm install` resuelve una versión mayor y algo se rompe, fijá la versión de la tabla y reportalo.
- **Verificación antes de cada commit, en el front:**
  - `npm run build` (corre `tsc -b` y después el build de Vite): sin errores;
  - `npm run lint`: sin errores;
  - `npm run test`: en verde.
- **Verificación antes de cada commit, en el backend:** `dotnet build ArquitecturaBase.slnx` con 0 advertencias y `dotnet test` en verde.
- **Docker** encendido para los tests de integración del backend.
- **AppHost:** no dejarlo corriendo al terminar una tarea (`aspire stop`). Bloquea los DLL y los puertos.
- **Secretos:** ninguno en el repo del front. La Api ya tiene los suyos en user-secrets.
- **Idioma:** identificadores, nombres de archivos y mensajes de error de desarrollo en inglés. Todo texto que ve el usuario sale de i18next, en español rioplatense con voseo y en inglés. Comentarios en español, solo si aportan.
- **TypeScript estricto.** Nada de `any` ni de `@ts-ignore`. Si un tipo no sale, frená y reportá.
- **Desvíos:** si algo del plan no compila o una API no existe tal cual, hacé el cambio mínimo e informalo. Si el cambio altera el diseño, frená y pedí contexto.

## Hechos verificados que condicionan el diseño

1. **La carpeta del front no está vacía** (tiene `.gitignore`, `LICENSE` y `README.md`), y `npm create vite` solo acepta carpetas vacías o con `.git`. Se genera en una carpeta temporal y se copia, conservando esos tres archivos (Tarea 1).
2. **La plantilla `react-ts` de hoy** genera `tsconfig.json` solo con referencias a `tsconfig.app.json` y `tsconfig.node.json`, usa `verbatimModuleSyntax` (obliga a `import type` para los tipos), el script de build es `tsc -b && vite build`, y el linter por defecto es **oxlint**, no ESLint.
3. **Tailwind 4 es CSS-first:** no hay `tailwind.config.js` ni PostCSS. Los tokens de marca se declaran con `@theme` en el CSS.
4. **shadcn/ui** necesita el alias `@/*` en los dos tsconfig y en `vite.config.ts` antes de correr `init`. Genera `components.json`, `src/lib/utils.ts` y los componentes en `src/components/ui/`.
5. **React Router 8:** todo se importa de `react-router`. Se usa el modo *data* (`createBrowserRouter` + `RouterProvider`) solo para el árbol de rutas, sin loaders ni actions: los datos los maneja TanStack Query.
6. **TanStack Query 5:** `keepPreviousData` como booleano ya no existe; para paginados se usa `placeholderData: keepPreviousData` importando el helper.
7. **Aspire + Vite:** el paquete es `Aspire.Hosting.JavaScript` 13.5.4, con `AddViteApp(nombre, ruta)`, `.WithNpm()` (instala si falta `node_modules`), `.WithReference(api)` y `.WaitFor(api)`.
   - `WithReference` inyecta `services__api__https__0` (y `API_HTTPS`), que `vite.config.ts` lee con `process.env` para armar el proxy. No hace falta el prefijo `VITE_`, porque el proxy corre en Node y no en el navegador.
   - Hay dos cosas que **no** están confirmadas y se resuelven probando en la Tarea 3: cómo queda el nombre del endpoint al combinar HTTPS, y si el puerto 5173 queda fijo o Aspire asigna uno al azar. La tarea trae un plan B.
   - El proxy de Aspire rompe el WebSocket de recarga en caliente de Vite; por eso el endpoint va con `IsProxied = false`.
8. **OIDC en el SPA** (`oidc-client-ts` 3.5.0 con `react-oidc-context` 3.3.1):
   - PKCE con S256 es obligatorio y no configurable: la librería solo soporta code flow.
   - `userStore` va con `InMemoryWebStorage`: los tokens quedan solo en memoria. El default sería `sessionStorage`.
   - `stateStore` se deja en el default (`sessionStorage`). Ahí viaja solo el `state` y el `code_verifier` de la transacción, nunca tokens; si fuera en memoria, el ida y vuelta a `/connect/authorize` perdería el estado y el callback fallaría.
   - `signinSilent()` usa el refresh token si hay usuario en memoria, y el iframe con `prompt=none` si no lo hay. Con los tokens en memoria, al recargar la página siempre usa el iframe y la cookie del servidor, que es justo lo que pide el spec.
   - `react-oidc-context` ya resuelve el doble montaje de StrictMode con un guard, y lo tiene testeado. Exige definir `onSigninCallback` para limpiar `code` y `state` de la URL.
   - `signoutRedirect()` manda el `id_token_hint` solo y limpia los tokens en memoria antes de navegar.
   - Un `login_required` en la renovación silenciosa llega como `ErrorResponse` con `error === "login_required"`; la librería no reintenta.
9. **`@hookform/resolvers` 5.9.1 con zod 4.6.5:** hay un problema conocido de tipos (el runtime anda bien). Si `tsc -b` se queja en `zodResolver`, la Tarea 11 trae el plan B. En zod 4 los validadores de formato son funciones sueltas: `z.email()` en vez de `z.string().email()`.
10. **MSW 2:** se usa `http` y `HttpResponse`; `rest` ya no existe. Para tests en Node/jsdom no hace falta `npx msw init`.
11. **Vitest 5** limpia los mocks antes de cada test por su cuenta.
12. **Renovación de tokens y rotación:** el backend revoca toda la cadena si se reusa un refresh token, y hay issues abiertos en `oidc-client-ts` por renovaciones concurrentes. Con los tokens en memoria, cada pestaña tiene su propia cadena y el riesgo baja mucho, pero dentro de una pestaña puede haber dos renovaciones a la vez (varias peticiones con 401 al mismo tiempo). Por eso el cliente HTTP renueva de a una (Tarea 5).
13. **El issuer tiene que ser el origen que ve el navegador.** Hoy OpenIddict lo deduce del request, así que a través del proxy quedaría `https://localhost:5173` en unos casos y la URL interna en otros. La Tarea 3 lo fija con `Authentication:Issuer`.

## Decisiones (2026-09-19)

- **Marca:** paleta neutra de placeholder (grises y un azul de acento) y el nombre "Arquitectura Base", con tokens en CSS. Cambiar la marca es cambiar variables.
- **Biblioteca de componentes:** se construye completa la sección 7.4 del spec, aunque algunos no tengan todavía una pantalla que los use.
- **Gestor de paquetes:** npm (ya instalado, y el repo tiene `package-lock.json`).
- **Linter:** el que trae la plantilla (oxlint).
- **Traducciones:** español por defecto, inglés completo, con un namespace por módulo cargado bajo demanda.

## Desvíos respecto del spec, y por qué

- **Sección 7.2, menú del usuario:** la entrada "dispositivos y sesiones" no se implementa: es de la Fase 4. El menú queda con perfil, idioma y cerrar sesión.
- **Sección 7.2, grupo Administración:** el menú incluye "Usuarios"; "Roles y permisos" queda anotado pero oculto, porque su pantalla es de la Fase 4.
- **Sección 7.3, `ProtectedRoute`:** sin sesión no arranca el flujo OIDC en cualquier ruta, sino que redirige a `/login` guardando a dónde quería ir. Es lo mismo de cara al usuario y evita un ida y vuelta con el servidor cuando ya sabemos que no hay sesión.
- **Perfil e idioma:** el menú permite cambiar el idioma en la sesión actual, pero **no** lo guarda en el perfil del usuario. Guardarlo necesita un endpoint de actualización de perfil que la Fase 2 no construyó. Queda anotado como pendiente.
- **Tests del front:** el spec pide tests de OtpInput, DataTable, Pagination, httpClient/ApiError y Sidebar. El plan agrega los de rutas protegidas y permisos, que son baratos y cubren la parte de seguridad de cara al usuario.

## Estructura de archivos

Front (`ArquitecturaBaseFront`):

```
components.json · index.html · package.json · vite.config.ts
tsconfig.json · tsconfig.app.json · tsconfig.node.json
public/silent-renew.html
src/
  main.tsx · App.tsx · index.css            tokens de marca y Tailwind
  app/
    router.tsx                              createBrowserRouter y el árbol de rutas
    providers.tsx                           Query, Auth, i18n y Toaster
  auth/
    authConfig.ts · AuthProvider.tsx        react-oidc-context con tokens en memoria
    ProtectedRoute.tsx · Can.tsx
    useCurrentUser.ts · usePermissions.ts   perfil y permisos desde /api/me
    silentRenew.ts                          entrada del iframe de renovación
  shared/
    api/httpClient.ts · ApiError.ts · problemDetails.ts · queryClient.ts
    hooks/usePagination.ts · useLocalStorage.ts · useMediaQuery.ts
    i18n/index.ts
    lib/utils.ts                            cn(), lo genera shadcn
    ui/                                     sección 7.4 del spec
      Button · IconButton · Input · Textarea · Select · Checkbox · Switch · FormField
      Dialog · ConfirmDialog · DropdownMenu · Tooltip
      Badge · Toast · Skeleton · Spinner · EmptyState
      PageHeader · SearchInput · DataTable · Pagination
  layouts/
    AuthLayout.tsx · AppLayout.tsx · navigation.ts
    components/Sidebar.tsx · Topbar.tsx · UserMenu.tsx · Breadcrumbs.tsx
  features/
    auth/pages/LoginPage.tsx · LoginCodePage.tsx · CallbackPage.tsx
    auth/components/OtpInput.tsx · auth/api/loginCode.ts
    users/pages/UsersPage.tsx · users/api/users.ts · users/columns.tsx
    home/pages/DashboardPage.tsx
    errors/pages/ForbiddenPage.tsx · NotFoundPage.tsx
  locales/es/*.json · locales/en/*.json      un namespace por módulo
  test/setup.ts · mocks/handlers.ts · mocks/server.ts · utils/renderWithProviders.tsx
CLAUDE.md · README.md
```

Backend (`ArquitecturaBase`), lo que cambia:

```
src/ArquitecturaBase.AppHost/AppHost.cs                    suma el recurso del front
src/ArquitecturaBase.Api/appsettings.Development.json      Authentication:Issuer
src/ArquitecturaBase.Api/Identity/…                        SetIssuer en OpenIddict (Infrastructure)
src/ArquitecturaBase.Api/Hosting/SecurityHeadersExtensions.cs
src/ArquitecturaBase.Api/Hosting/SpaExtensions.cs          sirve el SPA con fallback, sin tocar /api
src/ArquitecturaBase.Api/Program.cs
tests/ArquitecturaBase.Api.IntegrationTests/Hosting/…      tests de encabezados y del fallback
```

## Tareas

| # | Tarea | Repo | Tests |
|---|---|---|---|
| 1 | Proyecto Vite + React + TypeScript y convenciones | front | build y lint |
| 2 | Tailwind 4, tokens de marca y shadcn/ui | front | build |
| 3 | Aspire levanta el front, proxy e issuer | los dos | humo manual + integración |
| 4 | Traducciones (i18next) en español e inglés | front | Vitest |
| 5 | Cliente HTTP y ApiError desde ProblemDetails | front | Vitest + MSW |
| 6 | Ingreso OIDC: tokens en memoria y renovación silenciosa | front | Vitest |
| 7 | Rutas, rutas protegidas y permisos | front | Vitest |
| 8 | Componentes: controles y formularios | front | Vitest |
| 9 | Componentes: superposiciones y estados | front | Vitest |
| 10 | Componentes: página y listados (DataTable, Pagination, paginado en la URL) | front | Vitest |
| 11 | Pantallas de ingreso: email, código y callback | front | Vitest |
| 12 | AppLayout: sidebar, topbar y menú por permisos | front | Vitest |
| 13 | Pantalla de usuarios y tablero | front | Vitest |
| 14 | La Api sirve el SPA, con HTTPS y encabezados de seguridad | backend | integración |
| 15 | Documentación y verificación final | los dos | manual + comandos |

---

### Tarea 1: Proyecto Vite + React + TypeScript y convenciones

Repo: **front**.

La carpeta ya tiene `.git`, `.gitignore`, `LICENSE` y `README.md`, y `npm create vite` solo escribe en carpetas vacías. Por eso se genera aparte y se copia.

**Archivos:**
- Crear: todo el esqueleto de Vite, `vite.config.ts`, los tres `tsconfig`, `src/test/setup.ts`, `src/test/mocks/{handlers,server}.ts`, `src/App.test.tsx`, `CLAUDE.md`
- Modificar: `package.json`, `README.md`, `.gitignore`

- [ ] **Paso 1: generar el esqueleto y moverlo**

```bash
cd /c/Users/ezequ/source/repos
npm create vite@latest arquitecturabase-front-tmp -- --template react-ts
```

Después, desde la raíz del repo del front:

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBaseFront
rm -f package-lock.json
cp -r ../arquitecturabase-front-tmp/. .
rm -rf ../arquitecturabase-front-tmp
git status --short
```

`cp -r` no pisa `.git`, y el `.gitignore` generado por Vite sí reemplaza al del repo: eso está bien, porque el de Vite es el específico del proyecto. Agregale al final:

```gitignore
# Certificado de desarrollo exportado para el servidor de Vite (Tarea 3)
.certs/
```

Comprobá con `git status` que `LICENSE` y `README.md` siguen ahí y que no quedó nada de la carpeta temporal.

- [ ] **Paso 2: dependencias**

```bash
npm install
npm install -D vitest@5.0.1 jsdom@30.1.0 @testing-library/react@16.3.3 @testing-library/jest-dom@7.0.1 @testing-library/user-event@14.6.7 msw@2.15.0 @types/node
```

Si `npm install` deja versiones distintas de las de la tabla del plan para `vite`, `react` o `typescript`, dejá lo que resolvió pero reportalo.

- [ ] **Paso 3: configuración**

`vite.config.ts`:

```ts
/// <reference types="vitest/config" />
import path from "node:path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: { "@": path.resolve(import.meta.dirname, "./src") },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: "./src/test/setup.ts",
    css: false,
  },
});
```

En `tsconfig.json` y en `tsconfig.app.json`, dentro de `compilerOptions` (en el primero hay que crear el objeto, porque la plantilla lo deja solo con referencias):

```json
    "baseUrl": ".",
    "paths": { "@/*": ["./src/*"] }
```

En `package.json`, agregá a `scripts`:

```json
    "test": "vitest run",
    "test:watch": "vitest"
```

- [ ] **Paso 4: arnés de tests**

`src/test/mocks/handlers.ts`:

```ts
import { http, HttpResponse } from "msw";

/// Handlers por defecto. Cada test agrega los suyos con server.use(...).
export const handlers = [
  http.get("/api/me", () => new HttpResponse(null, { status: 401 })),
];
```

`src/test/mocks/server.ts`:

```ts
import { setupServer } from "msw/node";
import { handlers } from "./handlers";

export const server = setupServer(...handlers);
```

`src/test/setup.ts`:

```ts
import "@testing-library/jest-dom/vitest";
import { afterAll, afterEach, beforeAll } from "vitest";
import { server } from "./mocks/server";

// onUnhandledRequest: "error" evita que un test pegue a la red real sin darse cuenta.
beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());
```

- [ ] **Paso 5: test de humo**

`src/App.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import App from "./App";

describe("App", () => {
  it("renders the application shell", () => {
    render(<App />);

    expect(screen.getByRole("heading", { level: 1 })).toBeInTheDocument();
  });
});
```

Para que pase, dejá `src/App.tsx` con lo mínimo (el contenido real llega en las tareas siguientes) y borrá `src/App.css` y el `src/assets/react.svg` de la plantilla:

```tsx
export default function App() {
  return <h1>Arquitectura Base</h1>;
}
```

- [ ] **Paso 6: correr y ver que pasa**

```bash
npm run build
npm run lint
npm run test
```

Esperado: build sin errores, lint sin errores y 1 test en verde.

- [ ] **Paso 7: convenciones del repo**

`CLAUDE.md` en la raíz del front:

````markdown
# ArquitecturaBaseFront: guía para agentes

SPA de la plantilla base. El backend vive en `../ArquitecturaBase` y el diseño aprobado, en `../ArquitecturaBase/docs/specs/2026-09-18-arquitectura-base-design.md` (sección 7). Los planes por fase están en `../ArquitecturaBase/docs/plans/`.

## Forma de trabajo

- Se trabaja directo en `main`. No crear ramas ni hacer push sin un pedido explícito.
- Commits chicos, en español, con conventional commits.
- Antes de dar algo por terminado: `npm run build`, `npm run lint` y `npm run test`, los tres limpios.

## Comandos

- Desarrollo: `aspire run` desde `../ArquitecturaBase` levanta Postgres, la Api y este front en `https://localhost:5173`.
- Solo el front: `npm run dev` (necesita la Api aparte y el certificado de desarrollo).
- Tests: `npm run test`; en modo watch, `npm run test:watch`.

## Estructura

- `src/app`: composición (router y providers).
- `src/auth`: sesión OIDC, rutas protegidas y permisos.
- `src/shared`: lo que usa todo el proyecto (cliente HTTP, hooks, i18n, componentes de `ui`).
- `src/layouts`: AuthLayout, AppLayout y el menú.
- `src/features/<módulo>`: páginas, componentes y llamadas al backend de cada módulo.
- `src/locales/<idioma>/<módulo>.json`: traducciones, un namespace por módulo.

Una feature nunca importa de otra feature: lo común sube a `shared`.

## Reglas

- TypeScript estricto: nada de `any` ni de `@ts-ignore`. Los tipos se importan con `import type` (`verbatimModuleSyntax`).
- Todo texto que ve el usuario sale de i18next. Español rioplatense con voseo e inglés, siempre los dos.
- Los datos del servidor se piden con TanStack Query; no hay `useEffect` con `fetch`.
- Todas las llamadas al backend pasan por `shared/api/httpClient`, que agrega el token, el idioma y convierte los errores en `ApiError`.
- Los tokens viven en memoria. Nunca en `localStorage` ni en `sessionStorage`.
- Los permisos del front son solo para la experiencia de uso: quien decide es el backend.
- Estilos con Tailwind y los tokens de marca de `index.css`. Los componentes de `shared/ui` no traen colores propios.
- Tests con Vitest y Testing Library, consultando por rol y texto accesible, no por clases CSS. Las llamadas HTTP se simulan con MSW.

## Rendimiento

- Las páginas de cada módulo se cargan con `lazy()` desde el router: cada ruta es su propio trozo del bundle.
- No se definen componentes adentro de otros componentes: se remonta todo el subárbol en cada render.
- El estado derivado se calcula durante el render, no con un `useEffect` que copia datos a otro estado.
- En condicionales de JSX se usa ternario, no `&&`, para no renderizar un `0` o un `""` sin querer.
- En `localStorage` va lo mínimo y con clave propia (`arquitecturabase.*`). Nunca tokens ni datos del usuario.
````

Y reemplazá el `README.md` por una versión corta: qué es, cómo levantarlo con Aspire, cómo correr los tests y el enlace al spec.

- [ ] **Paso 8: commit**

```bash
git add .
git commit -m "feat: crear el proyecto Vite con React, TypeScript y el arnés de tests" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 2: Tailwind 4, tokens de marca y shadcn/ui

Repo: **front**.

Tailwind 4 es CSS-first: los tokens van con `@theme` en el CSS y no hay `tailwind.config.js`. La paleta es un placeholder neutro; cambiar la marca es cambiar estas variables.

**Archivos:**
- Crear: `components.json` y `src/lib/utils.ts` (los genera la CLI de shadcn)
- Modificar: `vite.config.ts`, `src/index.css`, `src/main.tsx`

- [ ] **Paso 1: Tailwind**

```bash
npm install tailwindcss@4.3.3 @tailwindcss/vite@4.3.3
npm install @fontsource-variable/inter
```

Inter va self-hosted (decisión del 2026-09-19): así la CSP de la Tarea 14 no tiene que abrir dominios de Google, el front anda sin internet y no se le avisa a un tercero cada visita. En `src/main.tsx`, antes del import de `./index.css`:

```ts
import "@fontsource-variable/inter";
```

En `vite.config.ts`, sumá el plugin:

```ts
import tailwindcss from "@tailwindcss/vite";
// ...
  plugins: [react(), tailwindcss()],
```

- [ ] **Paso 2: tokens de marca**

`src/index.css` queda así (reemplaza todo lo que trae la plantilla):

```css
@import "tailwindcss";

/* Tokens de marca. Cambiar la marca es cambiar estas variables (sección 7.4 del spec). */
@theme {
  /* La escala tiene dos usos y no son intercambiables:
     -50/-100 para fondos tintados, -500 para bordes, íconos y anillos de foco,
     -600 para rellenos con texto blanco encima y -700 para su hover.
     -500 con texto blanco da 3.65:1 y no llega al 4.5:1 de WCAG AA; -600 da 5.2:1.
     Si cambiás el tono, mantené -600 en L <= 0.56 o el texto blanco deja de leerse. */
  --color-brand-50: oklch(0.97 0.02 250);
  --color-brand-100: oklch(0.93 0.04 250);
  --color-brand-500: oklch(0.62 0.16 250);
  --color-brand-600: oklch(0.55 0.16 250);
  --color-brand-700: oklch(0.48 0.14 250);

  --color-surface: oklch(1 0 0);
  --color-surface-muted: oklch(0.97 0.005 250);
  --color-border: oklch(0.9 0.01 250);
  --color-content: oklch(0.25 0.02 250);
  --color-content-muted: oklch(0.5 0.02 250);
  --color-danger: oklch(0.58 0.2 25);
  --color-success: oklch(0.6 0.14 150);
  --color-warning: oklch(0.75 0.15 80);

  --radius-card: 0.75rem;
  --radius-control: 0.5rem;

  /* "Inter Variable" es el nombre de familia que declara @fontsource-variable/inter.
     Inter queda de respaldo por si alguien la tiene instalada en el sistema. */
  --font-sans: "Inter Variable", Inter, ui-sans-serif, system-ui, sans-serif;
}

html,
body,
#root {
  height: 100%;
}

body {
  background-color: var(--color-surface-muted);
  color: var(--color-content);
}
```

Asegurate de que `src/main.tsx` importe `./index.css` (la plantilla ya lo hace) y de borrar restos de estilos de la plantilla.

- [ ] **Paso 3: shadcn/ui**

El alias `@/*` ya quedó configurado en la Tarea 1, que es requisito de la CLI.

```bash
npx shadcn@latest init
```

Respuestas: estilo **new-york**, color base **neutral**, variables CSS **sí**. Genera `components.json` y `src/lib/utils.ts` con `cn()`, y agrega sus propias variables al CSS.

> **Lo que pasó al ejecutar (2026-09-19).** El `init` de la CLI 4.21.0 ya no ofrece ese combo: pregunta por uno de ocho presets, cada uno con su fuente y su librería de íconos, y no hay flag que lo evite. Se escribieron a mano `components.json` (con `style: "new-york"`, `baseColor: "neutral"`, `cssVariables: true`, `iconLibrary: "lucide"` y los alias de abajo), `src/shared/lib/utils.ts` con `cn()`, el bloque de variables y las dependencias (`class-variance-authority`, `clsx`, `tailwind-merge`, `lucide-react`). **`shadcn add` sí funciona sin prompts** con `--yes`, lee ese `components.json` y respeta los alias: se comprobó con `--dry-run`. Las tareas 8, 9 y 10 lo usan así, con la versión fijada.
>
> Tres cosas más de esta versión de la CLI, que valen para las tareas 8 a 10:
> - Los componentes de `new-york` importan del paquete unificado `radix-ui` (`import { Dialog as DialogPrimitive } from "radix-ui"`), no de los `@radix-ui/react-*` sueltos. La CLI lo instala sola.
> - `npx shadcn@4.21.0 docs <componente>` y `view <componente>` imprimen la API y el código de un componente sin escribir nada: convienen antes de envolver uno.
> - `Tooltip` necesita un `TooltipProvider` arriba de todo. Va en `app/providers.tsx`, junto a los demás.

Después, en `components.json`, cambiá los alias para que los componentes caigan donde los quiere el spec (sección 7.4) y no en `src/components`:

```json
  "aliases": {
    "components": "@/shared",
    "ui": "@/shared/ui",
    "lib": "@/shared/lib",
    "utils": "@/shared/lib/utils",
    "hooks": "@/shared/hooks"
  }
```

Si la CLI ya creó `src/lib/utils.ts`, movelo a `src/shared/lib/utils.ts` y borrá la carpeta vieja.

La CLI agrega su propio bloque de variables a `src/index.css` (`:root { --primary: ...; }`, un `.dark`, un `@theme inline` y un `@layer base`). Va debajo del `@theme` de marca, y las variables que llevan la marca se conectan **dentro de ese mismo `:root`**, no en un bloque aparte al final: si no, el botón primario de shadcn queda gris y la marca no se ve en ninguna pantalla.

```css
  --primary: var(--color-brand-600);
  --primary-foreground: oklch(1 0 0);
  --border: var(--color-border);
  --input: var(--color-border);
  --ring: var(--color-brand-500);
```

Y **borrá la línea `--color-border: var(--border);` del `@theme inline`**. Ese bloque va después del `@theme` de marca, así que redeclarar ahí `--color-border` lo pisa y `border-border` deja de ser el token de marca; además, con `--border: var(--color-border)` quedaría una referencia circular. Los demás nombres de ese bloque (`--color-primary`, `--color-ring`, `--color-input`) no colisionan con ninguno de marca, así que quedan como vinieron.

El resto de las variables de shadcn (neutros, `--destructive`, radios, la paleta de `.dark`) queda como la CLI las dejó. Anotá en un comentario cuál bloque es cuál.

El `@layer base` de shadcn trae `body { @apply bg-background text-foreground; }`, que compite con el fondo de la aplicación: dejá solo la regla `*` de ese bloque y definí el `body` una sola vez, con `--color-surface-muted`. El blanco es para las tarjetas y la barra lateral, que así se despegan del fondo.

- [ ] **Paso 4: lo que dejó pendiente la Tarea 1**

- La CLI de shadcn puede agregar `"baseUrl": "."` a los tsconfig, siguiendo su documentación para Vite. Con TypeScript 6 eso rompe el build (`TS5101`, `baseUrl` está deprecada). Si lo agregó, sacalo: `paths` con rutas relativas al propio tsconfig alcanza, y así quedó verificado en la Tarea 1.
- `public/favicon.svg` es el de la plantilla de Vite, con su violeta y su celeste, que no tienen nada que ver con los tokens. Reemplazalo por una marca de placeholder coherente: un SVG chico, un cuadrado de esquinas redondeadas con `--color-brand-600` de fondo y un glifo blanco de trazo simple adentro. Nada de gradientes ni de degradados.

- [ ] **Paso 5: correr y ver que pasa**

```bash
npm run build
npm run lint
npm run test
```

Esperado: todo limpio. El test de humo de la Tarea 1 sigue en verde.

Comprobación visual rápida: `npm run dev` y ver que el `h1` se muestra con la tipografía y el fondo de los tokens. Cortá el servidor al terminar.

- [ ] **Paso 6: commit**

```bash
git add .
git commit -m "feat: configurar Tailwind 4 con los tokens de marca y shadcn/ui" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 3: Aspire levanta el front, proxy e issuer

Repos: **los dos**. Son dos commits: uno en el backend y otro en el front.

Esta tarea deja funcionando la topología de la sección 5.1: el navegador habla solo con `https://localhost:5173`, Vite reenvía al backend, y el issuer de OpenIddict es el que ve el navegador.

**Archivos:**
- Backend: `Directory.Packages.props`, el csproj del AppHost, `AppHost.cs`, `OpenIddictRegistration.cs`, `appsettings.Development.json`, y un test en `tests/ArquitecturaBase.Api.IntegrationTests/Auth/OpenIddictServerTests.cs`
- Front: `vite.config.ts`

- [ ] **Paso 1 (backend): el issuer, con test que falla primero**

Agregá a `OpenIddictServerTests`:

```csharp
    [Fact]
    public async Task Issuer_can_be_configured_for_the_public_origin()
    {
        await using var api = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Authentication:Issuer", "https://app.test/"));
        using var client = api.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/.well-known/openid-configuration");
        var document = await response.ReadJsonAsync();

        Assert.Equal("https://app.test/", document.GetProperty("issuer").GetString());
        Assert.Equal("https://app.test/connect/authorize", document.GetProperty("authorization_endpoint").GetString());
    }
```

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Auth.OpenIddictServerTests"`
Esperado: FALLA. Hoy el issuer se deduce del request, así que devuelve `https://localhost/`.

En `OpenIddictRegistration.AddOpenIddictServer`, dentro del `AddServer`, antes de `AddCredentials`:

```csharp
                // El issuer es el origen que ve el navegador (sección 5.1). En desarrollo, el de Vite; si no se
                // configura, OpenIddict lo deduce del request, que a través de un proxy no es el correcto.
                var issuer = configuration[IssuerKey];

                if (!string.IsNullOrWhiteSpace(issuer))
                {
                    options.SetIssuer(issuer);
                }
```

Y la constante junto a las otras:

```csharp
    public const string IssuerKey = "Authentication:Issuer";
```

`appsettings.Development.json`, dentro de `Authentication`:

```json
    "Issuer": "https://localhost:5173/",
```

Run el mismo test: pasa. Después `dotnet test` completo.

Commit (backend):

```bash
git add src/ArquitecturaBase.Infrastructure src/ArquitecturaBase.Api tests/ArquitecturaBase.Api.IntegrationTests
git commit -m "feat: permitir configurar el issuer de OpenIddict con el origen público" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Paso 2 (front): proxy en Vite**

En `vite.config.ts`, agregá el bloque `server`. El destino sale de la variable que inyecta Aspire; el fallback sirve para correr `npm run dev` suelto.

```ts
// Aspire inyecta la URL de la Api con WithReference. Esto corre en Node (el proxy), no en el navegador.
const apiTarget =
  process.env.services__api__https__0 ?? process.env.API_HTTPS ?? "https://localhost:7180";

// changeOrigin queda en false a propósito: la Api tiene que ver el Host del navegador (localhost:5173) para
// armar bien el redirect_uri de Google. secure: false acepta el certificado de desarrollo.
const backend = { target: apiTarget, changeOrigin: false, secure: false, ws: true };
```

y dentro de `defineConfig`:

```ts
  server: {
    port: Number(process.env.PORT ?? 5173),
    strictPort: true,
    proxy: {
      "/api": backend,
      "/account": backend,
      "/connect": backend,
      "/signin-google": backend,
      "/.well-known": backend,
    },
  },
```

- [ ] **Paso 3 (backend): el AppHost levanta el front**

`Directory.Packages.props`, grupo `Aspire`: `<PackageVersion Include="Aspire.Hosting.JavaScript" Version="13.5.4" />`. En el csproj del AppHost, su `PackageReference`.

`AppHost.cs`, reemplazando el bloque de la Api:

```csharp
var api = builder.AddProject<Projects.ArquitecturaBase_Api>("api")
    .WithReference(appDb)
    .WaitFor(appDb)
    .WithUrlForEndpoint("https", _ => new() { Url = "/swagger", DisplayText = "Swagger UI" });

// El SPA vive en otro repo. El navegador habla solo con este origen y Vite reenvía a la Api (sección 5.1).
builder.AddViteApp("front", "../../../ArquitecturaBaseFront")
    .WithNpm()
    .WithReference(api)
    .WaitFor(api)
    .WithHttpsEndpoint(port: 5173, env: "PORT", isProxied: false)
    .WithHttpsDeveloperCertificate();

builder.Build().Run();
```

Tres cosas que hay que verificar acá, porque la documentación no las confirma (hecho verificado 7):
- que la firma de `WithHttpsEndpoint` acepte esos parámetros (si no, usá la sobrecarga que exista y fijá el puerto con `WithEndpoint("https", e => { e.Port = 5173; e.IsProxied = false; })`);
- que el endpoint se llame `https` y no `http`;
- que `WithHttpsDeveloperCertificate` realmente haga que Vite sirva HTTPS.

Si el build o el arranque fallan por nombres de API, probá las alternativas y **reportá cuál anduvo**. Si `WithHttpsDeveloperCertificate` no funciona, seguí con el plan B del paso 5.

- [ ] **Paso 4: probar de punta a punta**

Con Docker encendido y el secreto de Google cargado:

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBase
aspire run --detach
aspire describe
```

Esperado: `postgres`, `appdb`, `api` y `front` en Running. Después:

```bash
curl -sk --max-time 10 https://localhost:5173/ | head -5
curl -sk --max-time 10 https://localhost:5173/.well-known/openid-configuration
curl -sk -o /dev/null -w "%{http_code}\n" --max-time 10 https://localhost:5173/api/me
```

Esperado:
- el HTML del SPA (se ve `<div id="root">`);
- el documento de discovery con `"issuer":"https://localhost:5173/"` y los endpoints en `https://localhost:5173/connect/...`;
- `401` en `/api/me`, que es el ProblemDetails de siempre pasando por el proxy.

Al terminar: `aspire stop`.

- [ ] **Paso 5: plan B del certificado (solo si el paso 4 falló por HTTPS)**

Si Vite quedó sirviendo HTTP o el certificado no es válido:

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBaseFront
mkdir -p .certs
dotnet dev-certs https --export-path .certs/dev.pem --format Pem --no-password
```

y en `vite.config.ts`, dentro de `server`:

```ts
    https: fs.existsSync(".certs/dev.pem")
      ? { cert: fs.readFileSync(".certs/dev.pem"), key: fs.readFileSync(".certs/dev.key") }
      : undefined,
```

con `import fs from "node:fs";` arriba. `.certs/` ya está ignorado por git desde la Tarea 1. Sacá entonces `WithHttpsDeveloperCertificate()` del AppHost y documentá el paso del certificado en el README del front.

- [ ] **Paso 6: commits**

Front:

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBaseFront
git add .
git commit -m "feat: reenviar las rutas del backend desde el servidor de desarrollo" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

Backend:

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBase
git add Directory.Packages.props src/ArquitecturaBase.AppHost
git commit -m "feat: levantar el front desde el AppHost" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 4: Traducciones (i18next) en español e inglés

Repo: **front**.

Español por defecto, inglés completo, un namespace por módulo cargado bajo demanda (sección 6.4 del spec).

**Archivos:**
- Crear: `src/shared/i18n/index.ts`, `src/locales/es/common.json`, `src/locales/en/common.json`, `src/app/providers.tsx`, `src/test/utils/renderWithProviders.tsx`
- Modificar: `src/main.tsx`, `src/App.tsx`
- Test: `src/shared/i18n/i18n.test.tsx`

- [ ] **Paso 1: dependencias**

```bash
npm install i18next@26.4.2 react-i18next@17.0.14 i18next-resources-to-backend@1.2.3
```

- [ ] **Paso 2: test que falla**

`src/shared/i18n/i18n.test.tsx`:

```tsx
import { screen } from "@testing-library/react";
import { useTranslation } from "react-i18next";
import { describe, expect, it } from "vitest";
import i18n from "./index";
import { renderWithProviders } from "@/test/utils/renderWithProviders";

function Greeting() {
  const { t } = useTranslation();

  return <p>{t("app.name")}</p>;
}

describe("i18n", () => {
  it("uses Spanish by default", async () => {
    renderWithProviders(<Greeting />);

    expect(await screen.findByText("Arquitectura Base")).toBeInTheDocument();
  });

  it("translates the same key to English", async () => {
    await i18n.changeLanguage("en");
    renderWithProviders(<Greeting />);

    expect(await screen.findByText("Arquitectura Base")).toBeInTheDocument();
    expect(i18n.t("actions.cancel")).toBe("Cancel");

    await i18n.changeLanguage("es");
    expect(i18n.t("actions.cancel")).toBe("Cancelar");
  });
});
```

Run: `npm run test`
Esperado: FALLA porque no existe `@/shared/i18n`.

- [ ] **Paso 3: implementación**

`src/shared/i18n/index.ts`:

```ts
import i18n from "i18next";
import resourcesToBackend from "i18next-resources-to-backend";
import { initReactI18next } from "react-i18next";

export const supportedLanguages = ["es", "en"] as const;

export type SupportedLanguage = (typeof supportedLanguages)[number];

export const languageStorageKey = "arquitecturabase.language";

function initialLanguage(): SupportedLanguage {
  const stored = globalThis.localStorage?.getItem(languageStorageKey);

  return supportedLanguages.includes(stored as SupportedLanguage) ? (stored as SupportedLanguage) : "es";
}

// Cada módulo tiene su archivo por idioma y se carga cuando alguien lo pide con useTranslation("modulo").
await i18n
  .use(resourcesToBackend((language: string, namespace: string) => import(`../../locales/${language}/${namespace}.json`)))
  .use(initReactI18next)
  .init({
    lng: initialLanguage(),
    fallbackLng: "es",
    supportedLngs: [...supportedLanguages],
    ns: ["common"],
    defaultNS: "common",
    interpolation: { escapeValue: false },
    react: { useSuspense: true },
  });

export function changeLanguage(language: SupportedLanguage): Promise<unknown> {
  globalThis.localStorage?.setItem(languageStorageKey, language);

  return i18n.changeLanguage(language);
}

export default i18n;
```

`src/locales/es/common.json`:

```json
{
  "app": { "name": "Arquitectura Base" },
  "actions": {
    "cancel": "Cancelar",
    "confirm": "Confirmar",
    "save": "Guardar",
    "search": "Buscar",
    "retry": "Reintentar",
    "close": "Cerrar"
  },
  "states": {
    "loading": "Cargando…",
    "empty": "No hay nada para mostrar",
    "error": "Algo salió mal"
  },
  "errors": {
    "network": "No pudimos conectarnos. Revisá tu conexión.",
    "unexpected": "Ocurrió un error inesperado. Si el problema continúa, informá el código {{traceId}}."
  },
  "language": { "label": "Idioma", "es": "Español", "en": "Inglés" }
}
```

`src/locales/en/common.json`:

```json
{
  "app": { "name": "Arquitectura Base" },
  "actions": {
    "cancel": "Cancel",
    "confirm": "Confirm",
    "save": "Save",
    "search": "Search",
    "retry": "Retry",
    "close": "Close"
  },
  "states": {
    "loading": "Loading…",
    "empty": "Nothing to show",
    "error": "Something went wrong"
  },
  "errors": {
    "network": "We couldn't connect. Check your connection.",
    "unexpected": "Something went wrong. If it keeps happening, report the code {{traceId}}."
  },
  "language": { "label": "Language", "es": "Spanish", "en": "English" }
}
```

`src/app/providers.tsx` (las tareas siguientes le suman Query y la sesión):

```tsx
import { Suspense, type ReactNode } from "react";
import { I18nextProvider } from "react-i18next";
import i18n from "@/shared/i18n";

export function AppProviders({ children }: { children: ReactNode }) {
  return (
    <I18nextProvider i18n={i18n}>
      <Suspense fallback={null}>{children}</Suspense>
    </I18nextProvider>
  );
}
```

`src/test/utils/renderWithProviders.tsx`:

```tsx
import { render } from "@testing-library/react";
import type { ReactElement } from "react";
import { AppProviders } from "@/app/providers";

/// Renderiza con los mismos providers que la app: traducciones y, más adelante, datos y sesión.
export function renderWithProviders(ui: ReactElement) {
  return render(ui, { wrapper: AppProviders });
}
```

En `src/main.tsx`, envolvé `<App />` con `<AppProviders>`. En `src/App.tsx`, usá `t("app.name")` en el `h1` con `useTranslation()`; el test de humo de la Tarea 1 pasa a buscar ese texto con `findByRole`.

- [ ] **Paso 4: correr y ver que pasa**

```bash
npm run build && npm run lint && npm run test
```

Esperado: todo limpio, 3 tests en verde.

- [ ] **Paso 5: commit**

```bash
git add .
git commit -m "feat: agregar las traducciones en español e inglés" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 5: Cliente HTTP y ApiError desde ProblemDetails

Repo: **front**.

Todas las llamadas al backend pasan por acá. Traduce las respuestas de error (sección 6.1 del spec) a un `ApiError`, agrega el token y el idioma, y renueva la sesión **de a una** cuando llega un 401 (hecho verificado 12).

**Archivos:**
- Crear: `src/shared/api/problemDetails.ts`, `ApiError.ts`, `httpClient.ts`, `formErrors.ts`
- Test: `src/shared/api/httpClient.test.ts`, `src/shared/api/formErrors.test.ts`

- [ ] **Paso 1: tests que fallan**

`src/shared/api/httpClient.test.ts`:

```ts
import { HttpResponse, http } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "./ApiError";
import { api, configureHttpClient, resetHttpClient } from "./httpClient";
import { server } from "@/test/mocks/server";

describe("httpClient", () => {
  beforeEach(() => resetHttpClient());
  afterEach(() => resetHttpClient());

  it("returns the parsed body and sends the token and the language", async () => {
    configureHttpClient({ getAccessToken: () => "token-123", getLanguage: () => "en" });
    let authorization: string | null = null;
    let language: string | null = null;
    server.use(
      http.get("/api/me", ({ request }) => {
        authorization = request.headers.get("authorization");
        language = request.headers.get("accept-language");
        return HttpResponse.json({ email: "ana@example.com" });
      }),
    );

    const me = await api.get<{ email: string }>("/api/me");

    expect(me.email).toBe("ana@example.com");
    expect(authorization).toBe("Bearer token-123");
    expect(language).toBe("en");
  });

  it("turns a problem details response into an ApiError", async () => {
    server.use(
      http.post("/account/login-code/verify", () =>
        HttpResponse.json(
          {
            title: "Datos inválidos",
            status: 400,
            detail: "El código no es válido.",
            code: "Auth.LoginCode.Invalid",
            traceId: "trace-1",
            attemptsLeft: 4,
          },
          { status: 400, headers: { "content-type": "application/problem+json" } },
        ),
      ),
    );

    const error = await api.post("/account/login-code/verify", { code: "000000" }).catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    const apiError = error as ApiError;
    expect(apiError.status).toBe(400);
    expect(apiError.code).toBe("Auth.LoginCode.Invalid");
    expect(apiError.detail).toBe("El código no es válido.");
    expect(apiError.traceId).toBe("trace-1");
    expect(apiError.problem.attemptsLeft).toBe(4);
  });

  it("exposes the field errors of a validation problem", async () => {
    server.use(
      http.post("/account/login-code/verify", () =>
        HttpResponse.json(
          { status: 400, code: "Validation.Failed", errors: { returnUrl: ["La dirección de retorno no es válida."] } },
          { status: 400 },
        ),
      ),
    );

    const error = (await api.post("/account/login-code/verify", {}).catch((caught: unknown) => caught)) as ApiError;

    expect(error.errors?.returnUrl?.[0]).toBe("La dirección de retorno no es válida.");
  });

  it("renews the session once and retries after a 401", async () => {
    let token = "expired";
    const renewSession = vi.fn(async () => {
      token = "fresh";
      return token;
    });
    configureHttpClient({ getAccessToken: () => token, renewSession });
    server.use(
      http.get("/api/users", ({ request }) =>
        request.headers.get("authorization") === "Bearer fresh"
          ? HttpResponse.json({ items: [] })
          : new HttpResponse(null, { status: 401 }),
      ),
    );

    const page = await api.get<{ items: unknown[] }>("/api/users");

    expect(page.items).toEqual([]);
    expect(renewSession).toHaveBeenCalledTimes(1);
  });

  it("renews only once for several requests that fail at the same time", async () => {
    let token = "expired";
    const renewSession = vi.fn(async () => {
      await new Promise((resolve) => setTimeout(resolve, 10));
      token = "fresh";
      return token;
    });
    configureHttpClient({ getAccessToken: () => token, renewSession });
    server.use(
      http.get("/api/users", ({ request }) =>
        request.headers.get("authorization") === "Bearer fresh"
          ? HttpResponse.json({ items: [] })
          : new HttpResponse(null, { status: 401 }),
      ),
    );

    await Promise.all([api.get("/api/users"), api.get("/api/users"), api.get("/api/users")]);

    expect(renewSession).toHaveBeenCalledTimes(1);
  });

  it("gives up with a 401 error when the session cannot be renewed", async () => {
    const onSessionExpired = vi.fn();
    configureHttpClient({
      getAccessToken: () => "expired",
      renewSession: async () => undefined,
      onSessionExpired,
    });
    server.use(http.get("/api/users", () => new HttpResponse(null, { status: 401 })));

    const error = (await api.get("/api/users").catch((caught: unknown) => caught)) as ApiError;

    expect(error.status).toBe(401);
    expect(onSessionExpired).toHaveBeenCalledTimes(1);
  });

  it("reports network failures", async () => {
    server.use(http.get("/api/me", () => HttpResponse.error()));

    const error = (await api.get("/api/me").catch((caught: unknown) => caught)) as ApiError;

    expect(error.isNetworkError).toBe(true);
    expect(error.status).toBe(0);
  });

  it("returns undefined for an empty response", async () => {
    server.use(http.delete("/api/users/1", () => new HttpResponse(null, { status: 204 })));

    await expect(api.delete("/api/users/1")).resolves.toBeUndefined();
  });
});
```

Run: `npm run test`
Esperado: FALLA porque no existe `@/shared/api/httpClient`.

- [ ] **Paso 2: implementación**

`src/shared/api/problemDetails.ts`:

```ts
/// El cuerpo de error que devuelve el backend (RFC 9457, sección 6.1 del spec).
export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  readonly code?: string;
  readonly traceId?: string;
  readonly errors?: Record<string, string[]>;
  /// Los errores agregan datos propios: retryAfter en los 429, attemptsLeft en un código incorrecto.
  readonly [extension: string]: unknown;
}

export function isProblemDetails(value: unknown): value is ProblemDetails {
  return typeof value === "object" && value !== null;
}
```

`src/shared/api/ApiError.ts`:

```ts
import type { ProblemDetails } from "./problemDetails";

/// Error de una llamada al backend, ya traducido por el servidor al idioma de la petición.
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem: ProblemDetails,
    readonly isNetworkError = false,
  ) {
    super(problem.detail ?? problem.title ?? `HTTP ${status}`);
    this.name = "ApiError";
  }

  /// Código estable del backend (Area.Entidad.Motivo). El front decide por este, nunca por el texto.
  get code(): string | undefined {
    return this.problem.code;
  }

  get detail(): string | undefined {
    return this.problem.detail;
  }

  get traceId(): string | undefined {
    return this.problem.traceId;
  }

  /// Errores por campo de una validación, en camelCase, listos para setError de react-hook-form.
  get errors(): Record<string, string[]> | undefined {
    return this.problem.errors;
  }

  /// Segundos a esperar que informan los 429.
  get retryAfterSeconds(): number | undefined {
    return typeof this.problem.retryAfter === "number" ? this.problem.retryAfter : undefined;
  }

  static network(): ApiError {
    return new ApiError(0, { code: "Network.Unavailable" }, true);
  }
}
```

`src/shared/api/httpClient.ts`:

```ts
import { ApiError } from "./ApiError";
import { isProblemDetails, type ProblemDetails } from "./problemDetails";

interface HttpClientOptions {
  getAccessToken?: () => string | undefined;
  getLanguage?: () => string;
  /// Renueva la sesión y devuelve el token nuevo, o undefined si ya no se puede.
  renewSession?: () => Promise<string | undefined>;
  /// Se llama cuando un 401 no se pudo recuperar: la app manda al login.
  onSessionExpired?: () => void;
}

const defaults: HttpClientOptions = {};
let options: HttpClientOptions = defaults;

/// Una sola renovación a la vez: el backend revoca toda la cadena si se reusa un refresh token.
let renewal: Promise<string | undefined> | undefined;

export function configureHttpClient(next: HttpClientOptions): void {
  options = { ...defaults, ...next };
}

export function resetHttpClient(): void {
  options = defaults;
  renewal = undefined;
}

function renewOnce(): Promise<string | undefined> {
  renewal ??= (options.renewSession?.() ?? Promise.resolve(undefined)).finally(() => {
    renewal = undefined;
  });

  return renewal;
}

async function readProblem(response: Response): Promise<ProblemDetails> {
  try {
    const body: unknown = await response.json();

    return isProblemDetails(body) ? body : {};
  } catch {
    return {};
  }
}

async function send(path: string, init: RequestInit, token: string | undefined): Promise<Response> {
  const headers = new Headers(init.headers);
  headers.set("accept", "application/json");

  if (token) {
    headers.set("authorization", `Bearer ${token}`);
  }

  const language = options.getLanguage?.();

  if (language) {
    headers.set("accept-language", language);
  }

  if (init.body !== undefined) {
    headers.set("content-type", "application/json");
  }

  return fetch(path, { ...init, headers });
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  let token = options.getAccessToken?.();
  let response: Response;

  try {
    response = await send(path, init, token);
  } catch {
    throw ApiError.network();
  }

  // Un 401 puede ser solo un access token vencido: se renueva una vez y se reintenta.
  if (response.status === 401 && options.renewSession) {
    token = await renewOnce();

    if (token) {
      try {
        response = await send(path, init, token);
      } catch {
        throw ApiError.network();
      }
    }
  }

  if (response.status === 401) {
    options.onSessionExpired?.();
  }

  if (!response.ok) {
    throw new ApiError(response.status, await readProblem(response));
  }

  if (response.status === 204 || response.headers.get("content-length") === "0") {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export const api = {
  get: <T>(path: string, init?: RequestInit) => request<T>(path, { ...init, method: "GET" }),
  post: <T>(path: string, body?: unknown, init?: RequestInit) =>
    request<T>(path, { ...init, method: "POST", body: body === undefined ? undefined : JSON.stringify(body) }),
  put: <T>(path: string, body?: unknown, init?: RequestInit) =>
    request<T>(path, { ...init, method: "PUT", body: body === undefined ? undefined : JSON.stringify(body) }),
  delete: <T>(path: string, init?: RequestInit) => request<T>(path, { ...init, method: "DELETE" }),
};
```

- [ ] **Paso 3: los errores de validación van a los campos del formulario**

La sección 6.1 del spec pide que `errors` del ProblemDetails se muestre en cada campo. El backend los manda en camelCase, igual que los nombres de los campos del formulario.

`src/shared/api/formErrors.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";
import { ApiError } from "./ApiError";
import { applyApiErrorToForm } from "./formErrors";

describe("applyApiErrorToForm", () => {
  it("sets one error per field and returns true", () => {
    const setError = vi.fn();
    const error = new ApiError(400, {
      code: "Validation.Failed",
      errors: { email: ["Ingresá un correo válido."], code: ["Ingresá el código."] },
    });

    expect(applyApiErrorToForm(error, setError)).toBe(true);
    expect(setError).toHaveBeenCalledWith("email", { type: "server", message: "Ingresá un correo válido." });
    expect(setError).toHaveBeenCalledWith("code", { type: "server", message: "Ingresá el código." });
  });

  it("returns false when the error is not a validation problem", () => {
    const setError = vi.fn();

    expect(applyApiErrorToForm(new ApiError(429, { code: "Auth.LoginCode.ResendTooSoon" }), setError)).toBe(false);
    expect(setError).not.toHaveBeenCalled();
  });
});
```

`src/shared/api/formErrors.ts`:

```ts
import type { ApiError } from "./ApiError";

type SetError = (field: string, error: { type: string; message: string }) => void;

/// Lleva los errores por campo del backend al formulario. Devuelve false si el error no era de validación,
/// para que la pantalla lo muestre de otra forma.
export function applyApiErrorToForm(error: ApiError, setError: SetError): boolean {
  const errors = error.errors;

  if (!errors) {
    return false;
  }

  for (const [field, messages] of Object.entries(errors)) {
    const message = messages[0];

    if (message) {
      setError(field, { type: "server", message });
    }
  }

  return true;
}
```

- [ ] **Paso 4: correr y ver que pasa**

```bash
npm run build && npm run lint && npm run test
```

Esperado: los 10 tests nuevos en verde.

- [ ] **Paso 5: commit**

```bash
git add .
git commit -m "feat: agregar el cliente HTTP con ApiError y renovación única de sesión" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 6: Ingreso OIDC con los tokens en memoria

Repos: **los dos** (el backend solo suma la URI de la renovación silenciosa). Dos commits.

Sigue el hecho verificado 8: `react-oidc-context` sobre `oidc-client-ts`, tokens en memoria, y renovación silenciosa por iframe usando la cookie del servidor.

**Archivos:**
- Front, crear: `src/auth/authConfig.ts`, `src/auth/AuthProvider.tsx`, `src/auth/useCurrentUser.ts`, `src/shared/api/queryClient.ts`, `silent-renew.html`, `src/silent-renew.ts`
- Front, modificar: `vite.config.ts` (segunda entrada), `src/app/providers.tsx`
- Backend, modificar: `src/ArquitecturaBase.Api/appsettings.Development.json`
- Test: `src/auth/auth.test.tsx`

- [ ] **Paso 1: dependencias**

```bash
npm install oidc-client-ts@3.5.0 react-oidc-context@3.3.1 @tanstack/react-query@5.103.1
npm install -D @tanstack/react-query-devtools@5.103.1
```

- [ ] **Paso 2 (backend): registrar la URI de la renovación silenciosa**

En `appsettings.Development.json`, `Authentication:Clients:Web:RedirectUris` pasa a incluir la página del iframe:

```json
      "Web": {
        "RedirectUris": [
          "https://localhost:5173/auth/callback",
          "https://localhost:5173/silent-renew.html",
          "https://oauth.pstmn.io/v1/callback"
        ],
        "PostLogoutRedirectUris": [ "https://localhost:5173/login" ]
      }
```

El seed actualiza el cliente al arrancar, así que no hace falta nada más. Commit:

```bash
git add src/ArquitecturaBase.Api/appsettings.Development.json
git commit -m "feat: permitir la renovación silenciosa del SPA en desarrollo" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Paso 3 (front): test que falla**

`src/auth/auth.test.tsx`:

```tsx
import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { authConfig } from "./authConfig";
import { useCurrentUser } from "./useCurrentUser";
import { renderWithProviders } from "@/test/utils/renderWithProviders";

vi.mock("react-oidc-context", async () => {
  const actual = await vi.importActual<typeof import("react-oidc-context")>("react-oidc-context");

  return {
    ...actual,
    useAuth: () => ({ isAuthenticated: true, isLoading: false, user: { access_token: "token-123" } }),
  };
});

function Profile() {
  const { data } = useCurrentUser();

  return <p>{data?.email ?? "sin sesión"}</p>;
}

describe("auth", () => {
  it("keeps the tokens out of browser storage", () => {
    expect(authConfig.userStore).toBeDefined();
    expect(JSON.stringify(authConfig)).not.toContain("localStorage");
    expect(authConfig.scope).toBe("openid profile email roles offline_access api");
    expect(authConfig.client_id).toBe("web");
  });

  it("loads the profile of the signed in user", async () => {
    renderWithProviders(<Profile />);

    expect(await screen.findByText("ana@example.com")).toBeInTheDocument();
  });
});
```

En `src/test/mocks/handlers.ts`, cambiá el handler de `/api/me` para que devuelva un perfil:

```ts
export const currentUser = {
  id: "0199a0c0-0000-7000-8000-000000000000",
  email: "ana@example.com",
  displayName: "Ana",
  culture: "es",
  timeZoneId: "America/Argentina/Buenos_Aires",
  roles: ["User"],
  permissions: [] as string[],
};

export const handlers = [http.get("/api/me", () => HttpResponse.json(currentUser))];
```

Run: `npm run test`
Esperado: FALLA porque no existen `authConfig` ni `useCurrentUser`.

- [ ] **Paso 4 (front): implementación**

`src/auth/authConfig.ts`:

```ts
import { InMemoryWebStorage, WebStorageStateStore, type UserManagerSettings } from "oidc-client-ts";

/// El SPA y la Api comparten origen (sección 5.1), así que el authority es el propio origen.
const origin = globalThis.location?.origin ?? "https://localhost:5173";

export const authConfig: UserManagerSettings = {
  authority: origin,
  client_id: "web",
  redirect_uri: `${origin}/auth/callback`,
  post_logout_redirect_uri: `${origin}/login`,
  silent_redirect_uri: `${origin}/silent-renew.html`,
  response_type: "code",
  scope: "openid profile email roles offline_access api",
  // Los tokens viven solo en memoria: al recargar, la sesión se recupera con la cookie del servidor.
  userStore: new WebStorageStateStore({ store: new InMemoryWebStorage() }),
  // stateStore queda en sessionStorage (el default): ahí viaja el code_verifier, que tiene que sobrevivir
  // la ida y vuelta a /connect/authorize. Nunca guarda tokens.
  automaticSilentRenew: true,
  accessTokenExpiringNotificationTimeInSeconds: 60,
};
```

`src/silent-renew.ts` y `silent-renew.html` (en la raíz del proyecto, para que Vite lo tome como segunda entrada):

```ts
import { UserManager } from "oidc-client-ts";
import { authConfig } from "@/auth/authConfig";

// Página del iframe de renovación: solo completa el flujo y avisa a la ventana principal.
void new UserManager(authConfig).signinSilentCallback();
```

```html
<!doctype html>
<html lang="es">
  <head>
    <meta charset="UTF-8" />
    <title>…</title>
  </head>
  <body>
    <script type="module" src="/src/silent-renew.ts"></script>
  </body>
</html>
```

En `vite.config.ts`, agregá la segunda entrada al build:

```ts
  build: {
    rollupOptions: {
      input: {
        main: path.resolve(import.meta.dirname, "index.html"),
        silentRenew: path.resolve(import.meta.dirname, "silent-renew.html"),
      },
    },
  },
```

`src/shared/api/queryClient.ts`:

```ts
import { QueryClient } from "@tanstack/react-query";
import { ApiError } from "./ApiError";

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      // No tiene sentido reintentar lo que el backend ya respondió con un error de negocio.
      retry: (failureCount, error) => !(error instanceof ApiError) || (error.isNetworkError && failureCount < 2),
    },
  },
});
```

`src/auth/AuthProvider.tsx`:

```tsx
import { useEffect, type ReactNode } from "react";
import { AuthProvider as OidcProvider, useAuth } from "react-oidc-context";
import type { User } from "oidc-client-ts";
import { authConfig } from "./authConfig";
import { configureHttpClient } from "@/shared/api/httpClient";
import i18n from "@/shared/i18n";

/// Limpia code y state de la URL después del callback, como pide react-oidc-context.
function onSigninCallback(): void {
  globalThis.history.replaceState({}, document.title, globalThis.location.pathname);
}

/// Conecta la sesión con el cliente HTTP: token, idioma y renovación.
function HttpClientBridge({ children }: { children: ReactNode }) {
  const auth = useAuth();

  useEffect(() => {
    configureHttpClient({
      getAccessToken: () => auth.user?.access_token,
      getLanguage: () => i18n.language,
      renewSession: async () => {
        const user: User | null = await auth.signinSilent();

        return user?.access_token;
      },
      onSessionExpired: () => void auth.removeUser(),
    });
  }, [auth]);

  return <>{children}</>;
}

export function AppAuthProvider({ children }: { children: ReactNode }) {
  return (
    <OidcProvider {...authConfig} onSigninCallback={onSigninCallback}>
      <HttpClientBridge>{children}</HttpClientBridge>
    </OidcProvider>
  );
}
```

`src/auth/useCurrentUser.ts`:

```ts
import { useQuery } from "@tanstack/react-query";
import { useAuth } from "react-oidc-context";
import { api } from "@/shared/api/httpClient";

/// Perfil, roles y permisos del usuario de la sesión (sección 5.6 del spec).
export interface CurrentUser {
  readonly id: string;
  readonly email: string;
  readonly displayName: string | null;
  readonly culture: string;
  readonly timeZoneId: string;
  readonly roles: readonly string[];
  readonly permissions: readonly string[];
}

export const currentUserQueryKey = ["current-user"] as const;

export function useCurrentUser() {
  const auth = useAuth();

  return useQuery({
    queryKey: currentUserQueryKey,
    queryFn: () => api.get<CurrentUser>("/api/me"),
    enabled: auth.isAuthenticated,
    staleTime: 5 * 60_000,
  });
}
```

`src/app/providers.tsx` suma los dos providers nuevos:

```tsx
import { QueryClientProvider } from "@tanstack/react-query";
import { Suspense, type ReactNode } from "react";
import { I18nextProvider } from "react-i18next";
import { AppAuthProvider } from "@/auth/AuthProvider";
import { queryClient } from "@/shared/api/queryClient";
import i18n from "@/shared/i18n";

export function AppProviders({ children }: { children: ReactNode }) {
  return (
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <AppAuthProvider>
          <Suspense fallback={null}>{children}</Suspense>
        </AppAuthProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}
```

- [ ] **Paso 5: correr y ver que pasa**

```bash
npm run build && npm run lint && npm run test
```

Esperado: todo limpio. En el build tienen que aparecer las dos entradas (`index.html` y `silent-renew.html`).

- [ ] **Paso 6: commit (front)**

```bash
git add .
git commit -m "feat: iniciar sesión con OIDC manteniendo los tokens en memoria" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 7: Rutas, rutas protegidas y permisos

Repo: **front**.

**Archivos:**
- Crear: `src/app/router.tsx`, `src/auth/ProtectedRoute.tsx`, `src/auth/usePermissions.ts`, `src/auth/Can.tsx`, `src/features/errors/pages/ForbiddenPage.tsx`, `NotFoundPage.tsx`, `src/features/home/pages/DashboardPage.tsx`
- Modificar: `src/App.tsx`, `src/main.tsx`, `src/test/utils/renderWithProviders.tsx`, `src/locales/{es,en}/common.json`
- Test: `src/auth/ProtectedRoute.test.tsx`, `src/auth/usePermissions.test.tsx`

- [ ] **Paso 1: dependencias**

```bash
npm install react-router@8.4.0
```

- [ ] **Paso 2: tests que fallan**

`src/auth/usePermissions.test.tsx`:

```tsx
import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { Can } from "./Can";
import { renderWithProviders } from "@/test/utils/renderWithProviders";

vi.mock("react-oidc-context", async () => {
  const actual = await vi.importActual<typeof import("react-oidc-context")>("react-oidc-context");

  return { ...actual, useAuth: () => ({ isAuthenticated: true, isLoading: false, user: { access_token: "t" } }) };
});

describe("Can", () => {
  it("shows the children when the user has the permission", async () => {
    renderWithProviders(<Can permission="users.read">contenido</Can>);

    expect(await screen.findByText("contenido")).toBeInTheDocument();
  });

  it("hides the children when the user does not have the permission", async () => {
    renderWithProviders(<Can permission="users.manage">contenido</Can>);

    expect(await screen.findByTestId("ready")).toBeInTheDocument();
    expect(screen.queryByText("contenido")).not.toBeInTheDocument();
  });
});
```

Para que el segundo test tenga algo que esperar, `Can` renderiza `null` y el test se apoya en un marcador que agrega el helper: en `renderWithProviders`, envolvé los children con `<div data-testid="ready">`. Y en el handler de `/api/me` de `src/test/mocks/handlers.ts`, poné `permissions: ["users.read"]`.

`src/auth/ProtectedRoute.test.tsx`:

```tsx
import { screen } from "@testing-library/react";
import { RouterProvider, createMemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import { ProtectedRoute } from "./ProtectedRoute";
import { AppProviders } from "@/app/providers";
import { render } from "@testing-library/react";

const authState = { isAuthenticated: false, isLoading: false, user: undefined as unknown };

vi.mock("react-oidc-context", async () => {
  const actual = await vi.importActual<typeof import("react-oidc-context")>("react-oidc-context");

  return { ...actual, useAuth: () => authState };
});

function renderAt(path: string) {
  const router = createMemoryRouter(
    [
      { path: "/login", element: <p>pantalla de ingreso</p> },
      {
        element: <ProtectedRoute />,
        children: [
          { path: "/tablero", element: <p>tablero</p> },
          { path: "/usuarios", element: <ProtectedRoute permission="users.manage" />, children: [] },
        ],
      },
    ],
    { initialEntries: [path] },
  );

  return render(
    <AppProviders>
      <RouterProvider router={router} />
    </AppProviders>,
  );
}

describe("ProtectedRoute", () => {
  it("sends anonymous visitors to the login screen", async () => {
    authState.isAuthenticated = false;

    renderAt("/tablero");

    expect(await screen.findByText("pantalla de ingreso")).toBeInTheDocument();
  });

  it("lets a signed in user through", async () => {
    authState.isAuthenticated = true;
    authState.user = { access_token: "t" };

    renderAt("/tablero");

    expect(await screen.findByText("tablero")).toBeInTheDocument();
  });
});
```

Run: `npm run test`
Esperado: FALLAN por los módulos que no existen.

- [ ] **Paso 3: implementación**

`src/auth/usePermissions.ts`:

```ts
import { useCurrentUser } from "./useCurrentUser";

/// Los permisos del front son solo para la experiencia de uso: quien decide es el backend.
export function usePermissions() {
  const { data, isPending } = useCurrentUser();
  const permissions = data?.permissions ?? [];

  return {
    isPending,
    permissions,
    has: (permission: string) => permissions.includes(permission),
  };
}
```

`src/auth/Can.tsx`:

```tsx
import type { ReactNode } from "react";
import { usePermissions } from "./usePermissions";

/// Muestra a sus hijos solo si el usuario tiene el permiso.
export function Can({ permission, children }: { permission: string; children: ReactNode }) {
  const { has, isPending } = usePermissions();

  return !isPending && has(permission) ? <>{children}</> : null;
}
```

`src/auth/ProtectedRoute.tsx`:

```tsx
import { Navigate, Outlet, useLocation } from "react-router";
import { useAuth } from "react-oidc-context";
import { usePermissions } from "./usePermissions";
import { Spinner } from "@/shared/ui/Spinner";

/// Exige sesión y, si se pide, un permiso (sección 7.3). Sin sesión manda a /login guardando a dónde iba.
export function ProtectedRoute({ permission }: { permission?: string } = {}) {
  const auth = useAuth();
  const location = useLocation();
  const { has, isPending } = usePermissions();

  if (auth.isLoading) {
    return <Spinner />;
  }

  if (!auth.isAuthenticated) {
    return <Navigate to="/login" state={{ returnTo: location.pathname + location.search }} replace />;
  }

  if (!permission) {
    return <Outlet />;
  }

  if (isPending) {
    return <Spinner />;
  }

  return has(permission) ? <Outlet /> : <Navigate to="/sin-permiso" replace />;
}
```

`Spinner` llega en la Tarea 9; hasta entonces, dejá un `<p>{t("states.loading")}</p>` y cambialo en esa tarea.

`src/app/router.tsx`:

```tsx
import { createBrowserRouter } from "react-router";
import { ProtectedRoute } from "@/auth/ProtectedRoute";

// Las rutas del SPA están en español, porque son parte de la interfaz.
// Cada página se carga cuando se visita: `lazy` parte el bundle por ruta.
export const router = createBrowserRouter([
  {
    element: <ProtectedRoute />,
    children: [
      { path: "/", lazy: async () => ({ Component: (await import("@/features/home/pages/DashboardPage")).DashboardPage }) },
    ],
  },
  {
    path: "/sin-permiso",
    lazy: async () => ({ Component: (await import("@/features/errors/pages/ForbiddenPage")).ForbiddenPage }),
  },
  {
    path: "*",
    lazy: async () => ({ Component: (await import("@/features/errors/pages/NotFoundPage")).NotFoundPage }),
  },
]);
```

Si la forma de `lazy` cambió en React Router 8 y no compila, usá `React.lazy` con `<Suspense>` en el elemento de la ruta y reportalo.

Las pantallas de ingreso y el layout se enchufan en las Tareas 11 y 12; por ahora `DashboardPage`, `ForbiddenPage` y `NotFoundPage` son páginas mínimas con su texto traducido (`errors.forbidden.*`, `errors.notFound.*` en `common.json`).

`src/App.tsx` pasa a ser `<RouterProvider router={router} />` y `src/main.tsx` sigue envolviendo con `<AppProviders>`.

- [ ] **Paso 4: correr y ver que pasa**

```bash
npm run build && npm run lint && npm run test
```

Esperado: todo limpio, con los 4 tests nuevos en verde.

- [ ] **Paso 5: commit**

```bash
git add .
git commit -m "feat: agregar el árbol de rutas con rutas protegidas y permisos" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 8: Componentes: controles y formularios

Repo: **front**.

Los primitivos los genera la CLI de shadcn en `src/shared/ui` y toman los tokens de marca del CSS. Lo que escribimos a mano es lo que el spec pide y shadcn no trae: `IconButton` y `FormField`.

**Archivos:**
- Generar: `src/shared/ui/{button,input,textarea,select,checkbox,switch,label}.tsx`
- Crear: `src/shared/ui/IconButton.tsx`, `src/shared/ui/FormField.tsx`
- Test: `src/shared/ui/FormField.test.tsx`, `src/shared/ui/IconButton.test.tsx`

- [ ] **Paso 1: generar los primitivos**

```bash
npx shadcn@4.21.0 add button input textarea select checkbox switch label --yes
```

Revisá que los archivos hayan quedado en `src/shared/ui/` (si no, revisá los alias de `components.json` de la Tarea 2). No los edites salvo para reemplazar colores fijos por los tokens de marca cuando aparezcan.

- [ ] **Paso 2: tests que fallan**

`src/shared/ui/FormField.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { FormField } from "./FormField";
import { Input } from "./input";

describe("FormField", () => {
  it("links the label with the control", () => {
    render(
      <FormField label="Email" htmlFor="email">
        <Input id="email" />
      </FormField>,
    );

    expect(screen.getByLabelText("Email")).toBeInTheDocument();
  });

  it("shows the error and marks the control as invalid", () => {
    render(
      <FormField label="Email" htmlFor="email" error="Ingresá un correo válido.">
        <Input id="email" />
      </FormField>,
    );

    const input = screen.getByLabelText("Email");
    expect(screen.getByRole("alert")).toHaveTextContent("Ingresá un correo válido.");
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(input).toHaveAccessibleDescription("Ingresá un correo válido.");
  });

  it("shows the hint when there is no error", () => {
    render(
      <FormField label="Código" htmlFor="code" hint="Te lo mandamos por email.">
        <Input id="code" />
      </FormField>,
    );

    expect(screen.getByLabelText("Código")).toHaveAccessibleDescription("Te lo mandamos por email.");
  });
});
```

`src/shared/ui/IconButton.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { IconButton } from "./IconButton";

describe("IconButton", () => {
  it("exposes its label to assistive technology", async () => {
    const onClick = vi.fn();
    render(
      <IconButton label="Cerrar" onClick={onClick}>
        <span aria-hidden="true">×</span>
      </IconButton>,
    );

    await userEvent.click(screen.getByRole("button", { name: "Cerrar" }));

    expect(onClick).toHaveBeenCalledTimes(1);
  });
});
```

Run: `npm run test`
Esperado: FALLAN, los dos módulos no existen.

- [ ] **Paso 3: implementación**

`src/shared/ui/FormField.tsx`:

```tsx
import { cloneElement, useId, type ReactElement, type ReactNode } from "react";
import { Label } from "./label";

interface FormFieldProps {
  label: string;
  /// Id del control. Si no se pasa, se genera uno y se le enchufa al hijo.
  htmlFor?: string;
  hint?: string;
  error?: string;
  required?: boolean;
  children: ReactElement<{ id?: string; "aria-invalid"?: boolean; "aria-describedby"?: string }>;
}

/// Etiqueta, control y mensaje de error, atados entre sí para lectores de pantalla (sección 7.4).
export function FormField({ label, htmlFor, hint, error, required, children }: FormFieldProps): ReactNode {
  const generatedId = useId();
  const controlId = htmlFor ?? children.props.id ?? generatedId;
  const messageId = `${controlId}-message`;
  const message = error ?? hint;

  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={controlId}>
        {label}
        {required ? <span aria-hidden="true"> *</span> : null}
      </Label>
      {cloneElement(children, {
        id: controlId,
        "aria-invalid": error ? true : undefined,
        "aria-describedby": message ? messageId : undefined,
      })}
      {message ? (
        <p
          id={messageId}
          role={error ? "alert" : undefined}
          className={error ? "text-sm text-[var(--color-danger)]" : "text-sm text-[var(--color-content-muted)]"}
        >
          {message}
        </p>
      ) : null}
    </div>
  );
}
```

`src/shared/ui/IconButton.tsx`:

```tsx
import type { ComponentProps, ReactNode } from "react";
import { Button } from "./button";

/// Botón que solo muestra un ícono: el nombre accesible es obligatorio.
export function IconButton({
  label,
  children,
  ...props
}: Omit<ComponentProps<typeof Button>, "aria-label"> & { label: string }): ReactNode {
  return (
    <Button type="button" variant="ghost" size="icon" aria-label={label} title={label} {...props}>
      {children}
    </Button>
  );
}
```

- [ ] **Paso 4: correr y ver que pasa**

```bash
npm run build && npm run lint && npm run test
```

Esperado: los 4 tests nuevos en verde.

- [ ] **Paso 5: commit**

```bash
git add .
git commit -m "feat: agregar los controles de formulario compartidos" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 9: Componentes: superposiciones y estados

Repo: **front**.

**Archivos:**
- Generar: `src/shared/ui/{dialog,dropdown-menu,tooltip,badge,skeleton,sonner}.tsx`
- Crear: `src/shared/ui/ConfirmDialog.tsx`, `Spinner.tsx`, `EmptyState.tsx`
- Modificar: `src/app/providers.tsx` (el `Toaster`), `src/auth/ProtectedRoute.tsx` (usar `Spinner`), `src/locales/{es,en}/common.json`
- Test: `src/shared/ui/ConfirmDialog.test.tsx`, `src/shared/ui/EmptyState.test.tsx`

- [ ] **Paso 1: generar los primitivos**

```bash
npx shadcn@4.21.0 add dialog dropdown-menu tooltip badge skeleton sonner --yes
```

`sonner` es el componente de notificaciones que usa shadcn hoy. Si la CLI pide instalar la dependencia `sonner`, aceptá.

- [ ] **Paso 2: tests que fallan**

`src/shared/ui/ConfirmDialog.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { ConfirmDialog } from "./ConfirmDialog";

describe("ConfirmDialog", () => {
  it("confirms and closes", async () => {
    const onConfirm = vi.fn();
    const onOpenChange = vi.fn();
    render(
      <ConfirmDialog
        open
        onOpenChange={onOpenChange}
        title="Deshabilitar usuario"
        description="No va a poder ingresar."
        confirmLabel="Deshabilitar"
        onConfirm={onConfirm}
      />,
    );

    await userEvent.click(screen.getByRole("button", { name: "Deshabilitar" }));

    expect(onConfirm).toHaveBeenCalledTimes(1);
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("cancels without calling the action", async () => {
    const onConfirm = vi.fn();
    render(
      <ConfirmDialog
        open
        onOpenChange={vi.fn()}
        title="Deshabilitar usuario"
        confirmLabel="Deshabilitar"
        onConfirm={onConfirm}
      />,
    );

    await userEvent.click(screen.getByRole("button", { name: "Cancelar" }));

    expect(onConfirm).not.toHaveBeenCalled();
  });
});
```

`src/shared/ui/EmptyState.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { EmptyState } from "./EmptyState";

describe("EmptyState", () => {
  it("shows the message and the action", () => {
    render(<EmptyState title="No hay usuarios" description="Probá con otra búsqueda." action={<button>Limpiar</button>} />);

    expect(screen.getByRole("heading", { name: "No hay usuarios" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Limpiar" })).toBeInTheDocument();
  });
});
```

Run: `npm run test`. Esperado: FALLAN.

- [ ] **Paso 3: implementación**

`src/shared/ui/Spinner.tsx`:

```tsx
import { useTranslation } from "react-i18next";

/// Indicador de carga con nombre accesible. El texto sale de common.json.
export function Spinner({ className }: { className?: string }) {
  const { t } = useTranslation();

  return (
    <span role="status" aria-live="polite" className={className}>
      <span
        aria-hidden="true"
        className="inline-block size-5 animate-spin rounded-full border-2 border-[var(--color-border)] border-t-[var(--color-brand-500)]"
      />
      <span className="sr-only">{t("states.loading")}</span>
    </span>
  );
}
```

`src/shared/ui/EmptyState.tsx`:

```tsx
import type { ReactNode } from "react";

/// Estado vacío de listados y páginas (sección 7.4).
export function EmptyState({
  title,
  description,
  action,
}: {
  title: string;
  description?: string;
  action?: ReactNode;
}) {
  return (
    <div className="flex flex-col items-center gap-2 rounded-[var(--radius-card)] border border-dashed border-[var(--color-border)] p-10 text-center">
      <h3 className="text-base font-medium">{title}</h3>
      {description ? <p className="text-sm text-[var(--color-content-muted)]">{description}</p> : null}
      {action}
    </div>
  );
}
```

`src/shared/ui/ConfirmDialog.tsx`:

```tsx
import { useTranslation } from "react-i18next";
import { Button } from "./button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "./dialog";

interface ConfirmDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description?: string;
  confirmLabel: string;
  destructive?: boolean;
  onConfirm: () => void;
}

/// Diálogo de confirmación para acciones que no se pueden deshacer.
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel,
  destructive,
  onConfirm,
}: ConfirmDialogProps) {
  const { t } = useTranslation();

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          {description ? <DialogDescription>{description}</DialogDescription> : null}
        </DialogHeader>
        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
            {t("actions.cancel")}
          </Button>
          <Button
            type="button"
            variant={destructive ? "destructive" : "default"}
            onClick={() => {
              onConfirm();
              onOpenChange(false);
            }}
          >
            {confirmLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
```

En `src/app/providers.tsx`, agregá el `<Toaster />` de sonner al final del árbol, y en `ProtectedRoute` reemplazá el texto de carga provisorio por `<Spinner />`.

Y conectá los errores que no maneja ninguna pantalla con un aviso, como pide la sección 6.1 del spec. En `src/shared/api/queryClient.ts`:

```ts
import { QueryCache, QueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import i18n from "@/shared/i18n";
import { ApiError } from "./ApiError";

export const queryClient = new QueryClient({
  // Errores que la pantalla no muestra por su cuenta: un aviso, y reintento si fue de red.
  queryCache: new QueryCache({
    onError: (error, query) => {
      if (query.meta?.silent === true || !(error instanceof ApiError)) {
        return;
      }

      // 401 y 403 los resuelven el cliente HTTP y las pantallas.
      if (error.status === 401 || error.status === 403) {
        return;
      }

      if (error.isNetworkError) {
        toast.error(i18n.t("errors.network"), {
          action: { label: i18n.t("actions.retry"), onClick: () => void query.fetch() },
        });

        return;
      }

      toast.error(error.detail ?? i18n.t("states.error"));
    },
  }),
  defaultOptions: { /* igual que antes */ },
});
```

Test nuevo, `src/shared/api/queryClient.test.tsx`: una consulta que falla con un `ApiError` 500 muestra el aviso con el `detail`, y una que falla con 403 no muestra ninguno.

- [ ] **Paso 4: correr y ver que pasa**

```bash
npm run build && npm run lint && npm run test
```

Esperado: los 3 tests nuevos en verde y los anteriores igual.

- [ ] **Paso 5: commit**

```bash
git add .
git commit -m "feat: agregar los diálogos, avisos y estados compartidos" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 10: Componentes: página y listados

Repo: **front**.

`DataTable` y `Pagination` consumen el `PagedResult<T>` del backend (sección 6.2). `usePagination` guarda página, orden y búsqueda **en la URL**, para poder compartir un listado filtrado y que el botón atrás funcione.

**Archivos:**
- Generar: `src/shared/ui/table.tsx`
- Crear: `src/shared/ui/PageHeader.tsx`, `SearchInput.tsx`, `DataTable.tsx`, `Pagination.tsx`, `src/shared/hooks/usePagination.ts`, `src/shared/api/pagedResult.ts`
- Modificar: `src/locales/{es,en}/common.json`
- Test: `src/shared/hooks/usePagination.test.tsx`, `src/shared/ui/DataTable.test.tsx`, `src/shared/ui/Pagination.test.tsx`

- [ ] **Paso 1: generar la tabla**

```bash
npx shadcn@4.21.0 add table --yes
```

- [ ] **Paso 2: tests que fallan**

`src/shared/hooks/usePagination.test.tsx`:

```tsx
import { renderHook, act } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import type { ReactNode } from "react";
import { describe, expect, it } from "vitest";
import { usePagination } from "./usePagination";

function wrapper({ children }: { children: ReactNode }) {
  return <MemoryRouter initialEntries={["/usuarios?page=2&sort=-email&search=ana"]}>{children}</MemoryRouter>;
}

describe("usePagination", () => {
  it("reads the state from the URL", () => {
    const { result } = renderHook(() => usePagination(), { wrapper });

    expect(result.current.page).toBe(2);
    expect(result.current.pageSize).toBe(20);
    expect(result.current.sort).toBe("-email");
    expect(result.current.search).toBe("ana");
  });

  it("resets to the first page when the search changes", () => {
    const { result } = renderHook(() => usePagination(), { wrapper });

    act(() => result.current.setSearch("beto"));

    expect(result.current.page).toBe(1);
    expect(result.current.search).toBe("beto");
  });

  it("toggles the sort direction of the same field", () => {
    const { result } = renderHook(() => usePagination(), { wrapper });

    act(() => result.current.toggleSort("email"));
    expect(result.current.sort).toBe("email");

    act(() => result.current.toggleSort("email"));
    expect(result.current.sort).toBe("-email");
  });
});
```

`src/shared/ui/Pagination.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { Pagination } from "./Pagination";

describe("Pagination", () => {
  it("shows the range and moves between pages", async () => {
    const onPageChange = vi.fn();
    render(
      <Pagination page={2} pageSize={10} totalCount={25} totalPages={3} hasPrevious hasNext onPageChange={onPageChange} />,
    );

    expect(screen.getByText("11–20 de 25")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /siguiente/i }));
    expect(onPageChange).toHaveBeenCalledWith(3);

    await userEvent.click(screen.getByRole("button", { name: /anterior/i }));
    expect(onPageChange).toHaveBeenCalledWith(1);
  });

  it("disables the edges", () => {
    render(
      <Pagination
        page={1}
        pageSize={10}
        totalCount={5}
        totalPages={1}
        hasPrevious={false}
        hasNext={false}
        onPageChange={vi.fn()}
      />,
    );

    expect(screen.getByRole("button", { name: /anterior/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /siguiente/i })).toBeDisabled();
  });
});
```

`src/shared/ui/DataTable.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { DataTable, type Column } from "./DataTable";

interface Row {
  id: string;
  email: string;
}

const columns: Column<Row>[] = [
  { id: "email", header: "Email", cell: (row) => row.email, sortable: true },
];

const rows: Row[] = [
  { id: "1", email: "ana@example.com" },
  { id: "2", email: "beto@example.com" },
];

describe("DataTable", () => {
  it("renders the rows", () => {
    render(<DataTable columns={columns} rows={rows} rowKey={(row) => row.id} />);

    expect(screen.getAllByRole("row")).toHaveLength(3); // encabezado + 2 filas
    expect(screen.getByText("ana@example.com")).toBeInTheDocument();
  });

  it("asks to sort by a sortable column", async () => {
    const onSortChange = vi.fn();
    render(<DataTable columns={columns} rows={rows} rowKey={(row) => row.id} sort="email" onSortChange={onSortChange} />);

    const header = screen.getByRole("columnheader", { name: /email/i });
    expect(header).toHaveAttribute("aria-sort", "ascending");

    await userEvent.click(screen.getByRole("button", { name: /email/i }));
    expect(onSortChange).toHaveBeenCalledWith("email");
  });

  it("shows the loading state", () => {
    render(<DataTable columns={columns} rows={[]} rowKey={(row) => row.id} isLoading />);

    expect(screen.getByRole("status")).toBeInTheDocument();
  });

  it("shows the empty state", () => {
    render(<DataTable columns={columns} rows={[]} rowKey={(row) => row.id} emptyTitle="No hay usuarios" />);

    expect(screen.getByRole("heading", { name: "No hay usuarios" })).toBeInTheDocument();
  });

  it("shows the error state with a retry action", async () => {
    const onRetry = vi.fn();
    render(<DataTable columns={columns} rows={[]} rowKey={(row) => row.id} error="Algo salió mal" onRetry={onRetry} />);

    await userEvent.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });
});
```

Run: `npm run test`. Esperado: FALLAN.

- [ ] **Paso 3: implementación**

`src/shared/api/pagedResult.ts`:

```ts
/// Lo que devuelve el backend en los listados (sección 6.2 del spec).
export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totalPages: number;
  readonly hasPrevious: boolean;
  readonly hasNext: boolean;
}
```

`src/shared/hooks/usePagination.ts`:

```ts
import { useCallback, useMemo } from "react";
import { useSearchParams } from "react-router";

export const defaultPageSize = 20;

/// Página, orden y búsqueda viven en la URL: el listado se puede compartir y el botón atrás funciona.
export function usePagination() {
  const [params, setParams] = useSearchParams();

  const page = Number(params.get("page") ?? 1);
  const pageSize = Number(params.get("pageSize") ?? defaultPageSize);
  const sort = params.get("sort") ?? undefined;
  const search = params.get("search") ?? undefined;

  const update = useCallback(
    (changes: Record<string, string | undefined>) => {
      setParams(
        (previous) => {
          const next = new URLSearchParams(previous);

          for (const [key, value] of Object.entries(changes)) {
            if (value === undefined || value === "") {
              next.delete(key);
            } else {
              next.set(key, value);
            }
          }

          return next;
        },
        { replace: true },
      );
    },
    [setParams],
  );

  const setPage = useCallback((next: number) => update({ page: next === 1 ? undefined : String(next) }), [update]);

  // Cambiar la búsqueda o el orden vuelve a la primera página: si no, se puede quedar en una página que ya no existe.
  const setSearch = useCallback((next: string) => update({ search: next, page: undefined }), [update]);

  const toggleSort = useCallback(
    (field: string) => update({ sort: sort === field ? `-${field}` : field, page: undefined }),
    [sort, update],
  );

  return useMemo(
    () => ({ page, pageSize, sort, search, setPage, setSearch, toggleSort }),
    [page, pageSize, sort, search, setPage, setSearch, toggleSort],
  );
}
```

`src/shared/ui/PageHeader.tsx`:

```tsx
import type { ReactNode } from "react";

/// Título de la página y sus acciones (sección 7.4).
export function PageHeader({ title, description, actions }: { title: string; description?: string; actions?: ReactNode }) {
  return (
    <header className="mb-6 flex flex-wrap items-center justify-between gap-3">
      <div>
        <h1 className="text-xl font-semibold">{title}</h1>
        {description ? <p className="text-sm text-[var(--color-content-muted)]">{description}</p> : null}
      </div>
      {actions ? <div className="flex items-center gap-2">{actions}</div> : null}
    </header>
  );
}
```

`src/shared/ui/SearchInput.tsx`:

```tsx
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Input } from "./input";

/// Búsqueda con espera: no dispara una consulta por cada tecla.
export function SearchInput({
  value,
  onChange,
  delay = 300,
  label,
}: {
  value: string;
  onChange: (value: string) => void;
  delay?: number;
  label?: string;
}) {
  const { t } = useTranslation();
  const [draft, setDraft] = useState(value);

  useEffect(() => setDraft(value), [value]);

  useEffect(() => {
    if (draft === value) {
      return;
    }

    const timer = setTimeout(() => onChange(draft), delay);

    return () => clearTimeout(timer);
  }, [draft, delay, onChange, value]);

  return (
    <Input
      type="search"
      value={draft}
      onChange={(event) => setDraft(event.target.value)}
      aria-label={label ?? t("actions.search")}
      placeholder={label ?? t("actions.search")}
    />
  );
}
```

`src/shared/ui/DataTable.tsx`:

```tsx
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Button } from "./button";
import { EmptyState } from "./EmptyState";
import { Spinner } from "./Spinner";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "./table";

export interface Column<TRow> {
  /// Coincide con el nombre del campo ordenable del backend.
  id: string;
  header: string;
  cell: (row: TRow) => ReactNode;
  sortable?: boolean;
  align?: "left" | "right";
}

interface DataTableProps<TRow> {
  columns: Column<TRow>[];
  rows: readonly TRow[];
  rowKey: (row: TRow) => string;
  isLoading?: boolean;
  error?: string;
  onRetry?: () => void;
  emptyTitle?: string;
  emptyDescription?: string;
  /// Orden actual, en el formato del backend: "campo" o "-campo".
  sort?: string;
  onSortChange?: (field: string) => void;
}

function ariaSort(columnId: string, sort: string | undefined): "ascending" | "descending" | "none" {
  if (sort === columnId) {
    return "ascending";
  }

  return sort === `-${columnId}` ? "descending" : "none";
}

/// Listado con encabezados ordenables y estados de carga, error y vacío (sección 7.4).
export function DataTable<TRow>({
  columns,
  rows,
  rowKey,
  isLoading,
  error,
  onRetry,
  emptyTitle,
  emptyDescription,
  sort,
  onSortChange,
}: DataTableProps<TRow>) {
  const { t } = useTranslation();

  if (error) {
    return (
      <EmptyState
        title={error}
        action={
          onRetry ? (
            <Button type="button" variant="outline" onClick={onRetry}>
              {t("actions.retry")}
            </Button>
          ) : undefined
        }
      />
    );
  }

  if (isLoading) {
    return (
      <div className="flex justify-center p-10">
        <Spinner />
      </div>
    );
  }

  if (rows.length === 0) {
    return <EmptyState title={emptyTitle ?? t("states.empty")} description={emptyDescription} />;
  }

  return (
    <Table>
      <TableHeader>
        <TableRow>
          {columns.map((column) => (
            <TableHead key={column.id} aria-sort={ariaSort(column.id, sort)} className={column.align === "right" ? "text-right" : undefined}>
              {column.sortable && onSortChange ? (
                <Button type="button" variant="ghost" size="sm" onClick={() => onSortChange(column.id)}>
                  {column.header}
                </Button>
              ) : (
                column.header
              )}
            </TableHead>
          ))}
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map((row) => (
          <TableRow key={rowKey(row)}>
            {columns.map((column) => (
              <TableCell key={column.id} className={column.align === "right" ? "text-right" : undefined}>
                {column.cell(row)}
              </TableCell>
            ))}
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
```

`src/shared/ui/Pagination.tsx`:

```tsx
import { useTranslation } from "react-i18next";
import { Button } from "./button";
import type { PagedResult } from "@/shared/api/pagedResult";

type PaginationProps = Pick<
  PagedResult<unknown>,
  "page" | "pageSize" | "totalCount" | "totalPages" | "hasPrevious" | "hasNext"
> & { onPageChange: (page: number) => void };

/// Navegación de un listado paginado. Los números salen del PagedResult del backend.
export function Pagination({ page, pageSize, totalCount, totalPages, hasPrevious, hasNext, onPageChange }: PaginationProps) {
  const { t } = useTranslation();
  const from = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, totalCount);

  return (
    <nav className="mt-4 flex items-center justify-between gap-3" aria-label={t("pagination.label")}>
      <p className="text-sm text-[var(--color-content-muted)]">{t("pagination.range", { from, to, total: totalCount })}</p>
      <div className="flex items-center gap-2">
        <Button type="button" variant="outline" size="sm" disabled={!hasPrevious} onClick={() => onPageChange(page - 1)}>
          {t("pagination.previous")}
        </Button>
        <span className="text-sm">{t("pagination.page", { page, totalPages })}</span>
        <Button type="button" variant="outline" size="sm" disabled={!hasNext} onClick={() => onPageChange(page + 1)}>
          {t("pagination.next")}
        </Button>
      </div>
    </nav>
  );
}
```

Textos nuevos en `common.json`:

```json
  "pagination": {
    "label": "Paginado",
    "range": "{{from}}–{{to}} de {{total}}",
    "page": "Página {{page}} de {{totalPages}}",
    "previous": "Anterior",
    "next": "Siguiente"
  }
```

y en inglés: `"Pagination"`, `"{{from}}–{{to}} of {{total}}"`, `"Page {{page}} of {{totalPages}}"`, `"Previous"`, `"Next"`.

Los tests de `Pagination` y `DataTable` usan textos traducidos, así que se renderizan con `renderWithProviders`. Ajustá los `render(...)` de esos dos archivos para usarlo.

- [ ] **Paso 4: correr y ver que pasa**

```bash
npm run build && npm run lint && npm run test
```

Esperado: los 10 tests nuevos en verde.

- [ ] **Paso 5: commit**

```bash
git add .
git commit -m "feat: agregar el listado paginado, la búsqueda y el encabezado de página" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 11: Pantallas de ingreso: email, código y callback

Repo: **front**.

Implementa el flujo de la sección 5.2 del spec:
1. El SPA sin sesión arranca OIDC y el servidor lo manda a `/login?returnUrl=<el authorize original>`.
2. La persona escribe su email y el front pide el código.
3. Escribe el código y el front lo verifica; el backend devuelve el `returnUrl`.
4. El front navega a ese `returnUrl` (navegación real, no del router): el servidor ya encuentra la cookie y emite el code.
5. Vuelve a `/auth/callback`, donde `react-oidc-context` canjea el code.

**Archivos:**
- Crear: `src/features/auth/api/loginCode.ts`, `src/features/auth/components/OtpInput.tsx`, `src/features/auth/pages/LoginPage.tsx`, `LoginCodePage.tsx`, `CallbackPage.tsx`, `src/layouts/AuthLayout.tsx`, `src/shared/hooks/useCountdown.ts`, `src/locales/{es,en}/auth.json`
- Modificar: `src/app/router.tsx`
- Test: `src/features/auth/components/OtpInput.test.tsx`, `src/features/auth/pages/LoginCodePage.test.tsx`

- [ ] **Paso 1: dependencias**

```bash
npm install react-hook-form@7.88.0 zod@4.6.5 @hookform/resolvers@5.9.1
```

Si `tsc -b` se queja de tipos en `zodResolver` (hecho verificado 9), probá en este orden y reportá cuál hizo falta:
1. actualizar `@hookform/resolvers` a la última;
2. fijar `zod` en la última 4.5.x;
3. como último recurso, tipar el resolver con `zodResolver(schema) as Resolver<FormValues>` y un comentario que explique por qué.

- [ ] **Paso 2: tests que fallan**

`src/features/auth/components/OtpInput.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { OtpInput } from "./OtpInput";

describe("OtpInput", () => {
  it("moves to the next box while typing and reports the code", async () => {
    const onChange = vi.fn();
    render(<OtpInput length={6} value="" onChange={onChange} label="Código" />);

    const boxes = screen.getAllByRole("textbox");
    await userEvent.type(boxes[0], "1");

    expect(onChange).toHaveBeenLastCalledWith("1");
    expect(boxes[1]).toHaveFocus();
  });

  it("fills every box when the code is pasted", async () => {
    const onChange = vi.fn();
    render(<OtpInput length={6} value="" onChange={onChange} label="Código" />);

    const boxes = screen.getAllByRole("textbox");
    boxes[0].focus();
    await userEvent.paste("482913");

    expect(onChange).toHaveBeenLastCalledWith("482913");
  });

  it("goes back with backspace on an empty box", async () => {
    const onChange = vi.fn();
    render(<OtpInput length={6} value="48" onChange={onChange} label="Código" />);

    const boxes = screen.getAllByRole("textbox");
    boxes[2].focus();
    await userEvent.keyboard("{Backspace}");

    expect(onChange).toHaveBeenLastCalledWith("4");
    expect(boxes[1]).toHaveFocus();
  });

  it("ignores anything that is not a digit", async () => {
    const onChange = vi.fn();
    render(<OtpInput length={6} value="" onChange={onChange} label="Código" />);

    await userEvent.type(screen.getAllByRole("textbox")[0], "a");

    expect(onChange).not.toHaveBeenCalled();
  });
});
```

`src/features/auth/pages/LoginCodePage.test.tsx`:

```tsx
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { HttpResponse, http } from "msw";
import { describe, expect, it, vi } from "vitest";
import { renderRouteWithProviders } from "@/test/utils/renderWithProviders";
import { server } from "@/test/mocks/server";

const assign = vi.fn();

vi.stubGlobal("location", { ...globalThis.location, assign, origin: "https://localhost:5173" });

const returnUrl = "/connect/authorize?client_id=web";

describe("LoginCodePage", () => {
  it("verifies the code and goes to the authorize request", async () => {
    server.use(
      http.post("/account/login-code/verify", () => HttpResponse.json({ returnUrl })),
    );
    renderRouteWithProviders(`/login/codigo?returnUrl=${encodeURIComponent(returnUrl)}`, {
      state: { email: "ana@example.com" },
    });

    await userEvent.type(screen.getAllByRole("textbox")[0], "482913");
    await userEvent.click(screen.getByRole("button", { name: /ingresar/i }));

    await waitFor(() => expect(assign).toHaveBeenCalledWith(returnUrl));
  });

  it("shows the message and the attempts left when the code is wrong", async () => {
    server.use(
      http.post("/account/login-code/verify", () =>
        HttpResponse.json(
          { status: 400, code: "Auth.LoginCode.Invalid", detail: "El código no es válido.", attemptsLeft: 4 },
          { status: 400 },
        ),
      ),
    );
    renderRouteWithProviders(`/login/codigo?returnUrl=${encodeURIComponent(returnUrl)}`, {
      state: { email: "ana@example.com" },
    });

    await userEvent.type(screen.getAllByRole("textbox")[0], "000000");
    await userEvent.click(screen.getByRole("button", { name: /ingresar/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("El código no es válido.");
    expect(screen.getByText(/te quedan 4 intentos/i)).toBeInTheDocument();
  });

  it("waits before letting the code be sent again", async () => {
    renderRouteWithProviders(`/login/codigo?returnUrl=${encodeURIComponent(returnUrl)}`, {
      state: { email: "ana@example.com", resendAfterSeconds: 60 },
    });

    expect(screen.getByRole("button", { name: /reenviar/i })).toBeDisabled();
  });
});
```

Agregá a `src/test/utils/renderWithProviders.tsx` el helper de rutas:

```tsx
import { RouterProvider, createMemoryRouter } from "react-router";
import { routes } from "@/app/routes";

/// Renderiza la app entera en una ruta concreta, con los providers reales.
export function renderRouteWithProviders(path: string, options?: { state?: unknown }) {
  const router = createMemoryRouter(routes, { initialEntries: [{ pathname: path.split("?")[0], search: path.includes("?") ? `?${path.split("?")[1]}` : "", state: options?.state }] });

  return render(
    <AppProviders>
      <RouterProvider router={router} />
    </AppProviders>,
  );
}
```

Para que esto funcione, `src/app/router.tsx` pasa a exportar el arreglo `routes` y a crear el router con él en `src/app/routes.ts`; el archivo del router queda con `export const router = createBrowserRouter(routes)`.

Run: `npm run test`. Esperado: FALLAN.

- [ ] **Paso 3: implementación**

`src/features/auth/api/loginCode.ts`:

```ts
import { api } from "@/shared/api/httpClient";

export interface RequestLoginCodeResponse {
  readonly resendAfterSeconds: number;
}

export interface VerifyLoginCodeResponse {
  readonly returnUrl: string;
}

export function requestLoginCode(email: string): Promise<RequestLoginCodeResponse> {
  return api.post<RequestLoginCodeResponse>("/account/login-code", { email });
}

export function verifyLoginCode(input: { email: string; code: string; returnUrl: string }): Promise<VerifyLoginCodeResponse> {
  return api.post<VerifyLoginCodeResponse>("/account/login-code/verify", input);
}

/// El ingreso con Google es una navegación del navegador, no una llamada de la Api.
export function externalLoginUrl(returnUrl: string): string {
  return `/account/external/google?returnUrl=${encodeURIComponent(returnUrl)}`;
}
```

`src/shared/hooks/useCountdown.ts`:

```ts
import { useEffect, useState } from "react";

/// Cuenta regresiva en segundos, para el reenvío del código.
export function useCountdown(initialSeconds: number) {
  const [seconds, setSeconds] = useState(initialSeconds);

  useEffect(() => setSeconds(initialSeconds), [initialSeconds]);

  useEffect(() => {
    if (seconds <= 0) {
      return;
    }

    const timer = setTimeout(() => setSeconds((current) => current - 1), 1000);

    return () => clearTimeout(timer);
  }, [seconds]);

  return { seconds, isRunning: seconds > 0, restart: setSeconds };
}
```

`src/features/auth/components/OtpInput.tsx`:

```tsx
import { useRef, type ChangeEvent, type ClipboardEvent, type KeyboardEvent } from "react";
import { Input } from "@/shared/ui/input";

interface OtpInputProps {
  length: number;
  value: string;
  onChange: (value: string) => void;
  label: string;
  disabled?: boolean;
}

/// Casilleros para el código de ingreso: avanzan solos, aceptan pegar y vuelven con Backspace.
export function OtpInput({ length, value, onChange, label, disabled }: OtpInputProps) {
  const boxes = useRef<(HTMLInputElement | null)[]>([]);
  const digits = value.padEnd(length, " ").slice(0, length).split("");

  function focusBox(index: number) {
    boxes.current[Math.min(Math.max(index, 0), length - 1)]?.focus();
  }

  function handleChange(index: number, event: ChangeEvent<HTMLInputElement>) {
    const digit = event.target.value.replace(/\D/gu, "").slice(-1);

    if (!digit) {
      return;
    }

    const next = value.padEnd(index, " ").slice(0, index) + digit + value.slice(index + 1);
    onChange(next.trimEnd());
    focusBox(index + 1);
  }

  function handleKeyDown(index: number, event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Backspace" && !digits[index]?.trim()) {
      event.preventDefault();
      onChange(value.slice(0, Math.max(index - 1, 0)));
      focusBox(index - 1);
    }
  }

  function handlePaste(event: ClipboardEvent<HTMLInputElement>) {
    const pasted = event.clipboardData.getData("text").replace(/\D/gu, "").slice(0, length);

    if (!pasted) {
      return;
    }

    event.preventDefault();
    onChange(pasted);
    focusBox(pasted.length);
  }

  return (
    <div role="group" aria-label={label} className="flex gap-2">
      {digits.map((digit, index) => (
        <Input
          // El índice es la identidad real de cada casillero.
          key={index}
          ref={(element: HTMLInputElement | null) => {
            boxes.current[index] = element;
          }}
          type="text"
          inputMode="numeric"
          autoComplete={index === 0 ? "one-time-code" : "off"}
          maxLength={1}
          disabled={disabled}
          aria-label={`${label} ${index + 1}`}
          value={digit.trim()}
          onChange={(event) => handleChange(index, event)}
          onKeyDown={(event) => handleKeyDown(index, event)}
          onPaste={handlePaste}
          className="size-12 text-center text-lg"
        />
      ))}
    </div>
  );
}
```

`src/layouts/AuthLayout.tsx`: tarjeta centrada sobre el fondo de marca, con el nombre de la app arriba y `<Outlet />` adentro.

`src/features/auth/pages/LoginPage.tsx`:
- Lee `returnUrl` de la query. Si no hay, llama a `auth.signinRedirect({ state: { returnTo: location.state?.returnTo ?? "/" } })` y no muestra nada: el servidor va a volver con el `returnUrl` correcto (sección 5.2, último párrafo).
- Formulario con react-hook-form y zod (`z.object({ email: z.email() })`), `FormField` + `Input`.
- Al enviar: `requestLoginCode(email)` y navega a `/login/codigo?returnUrl=…` pasando `{ email, resendAfterSeconds }` en el state del router.
- Errores: si `ApiError.code` es `Auth.LoginCode.ResendTooSoon` o `TooManyRequests`, muestra `error.detail` y usa `retryAfterSeconds` para la cuenta regresiva; el resto, `error.detail` en un `role="alert"`.
- Botón "Ingresar con Google": `<a href={externalLoginUrl(returnUrl)}>`.

`src/features/auth/pages/LoginCodePage.tsx`:
- Toma `email` y `resendAfterSeconds` del state del router; si no hay email, vuelve a `/login` conservando el `returnUrl`.
- `OtpInput` + botón "Ingresar" (deshabilitado hasta tener los 6 dígitos).
- Al enviar: `verifyLoginCode(...)` y `globalThis.location.assign(response.returnUrl)`.
- Errores: muestra `error.detail` en `role="alert"`; si viene `attemptsLeft`, agrega el texto `auth.code.attemptsLeft` con el número; si el código es `Auth.Account.LockedOut` o `Auth.LoginCode.TooManyAttempts`, además deshabilita el botón y ofrece volver a pedir un código.
- Botón "Reenviar código": deshabilitado mientras `useCountdown` esté corriendo; al reenviar, vuelve a arrancar la cuenta con `resendAfterSeconds`.

`src/features/auth/pages/CallbackPage.tsx`:
- Muestra `<Spinner />` mientras `auth.isLoading`.
- Cuando `auth.isAuthenticated`, navega a `auth.user?.state?.returnTo ?? "/"` con `replace`.
- Si `auth.error`, muestra el mensaje y un enlace a `/login`.

Rutas nuevas en `src/app/routes.ts`, todas dentro de `AuthLayout`: `/login`, `/login/codigo` y `/auth/callback`.

Textos en `src/locales/{es,en}/auth.json`: título y ayuda de cada pantalla, etiquetas de los campos, "Ingresar", "Reenviar código", "Reenviar en {{seconds}} s", "Te quedan {{count}} intentos", "Ingresar con Google", y el error genérico.

- [ ] **Paso 4: correr y ver que pasa**

```bash
npm run build && npm run lint && npm run test
```

Esperado: los 7 tests nuevos en verde.

- [ ] **Paso 5: commit**

```bash
git add .
git commit -m "feat: agregar las pantallas de ingreso con código y con Google" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 12: AppLayout: sidebar, topbar y menú por permisos

Repo: **front**.

Sección 7.2 del spec. El menú sale de `navigation.ts` y se filtra con los permisos del usuario.

**Archivos:**
- Crear: `src/layouts/AppLayout.tsx`, `src/layouts/navigation.ts`, `src/layouts/components/{Sidebar,Topbar,UserMenu,Breadcrumbs}.tsx`, `src/shared/hooks/useLocalStorage.ts`, `src/shared/hooks/useMediaQuery.ts`
- Modificar: `src/app/routes.ts`, `src/locales/{es,en}/common.json`
- Test: `src/layouts/components/Sidebar.test.tsx`, `src/layouts/components/UserMenu.test.tsx`

- [ ] **Paso 1: tests que fallan**

`src/layouts/components/Sidebar.test.tsx`:

```tsx
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { renderRouteWithProviders } from "@/test/utils/renderWithProviders";

vi.mock("react-oidc-context", async () => {
  const actual = await vi.importActual<typeof import("react-oidc-context")>("react-oidc-context");

  return { ...actual, useAuth: () => ({ isAuthenticated: true, isLoading: false, user: { access_token: "t" } }) };
});

describe("Sidebar", () => {
  it("hides the items whose permission the user does not have", async () => {
    // El handler por defecto de /api/me devuelve permissions: ["users.read"].
    renderRouteWithProviders("/");

    expect(await screen.findByRole("link", { name: /usuarios/i })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /roles/i })).not.toBeInTheDocument();
  });

  it("remembers that the menu is collapsed", async () => {
    renderRouteWithProviders("/");

    await userEvent.click(await screen.findByRole("button", { name: /contraer|expandir/i }));

    expect(globalThis.localStorage.getItem("arquitecturabase.sidebar")).toBe('"collapsed"');
  });
});
```

`src/layouts/components/UserMenu.test.tsx`: abre el menú, comprueba que muestra el email del usuario, que cambia el idioma (el texto de un ítem pasa a inglés) y que "Cerrar sesión" llama a `signoutRedirect`.

Run: `npm run test`. Esperado: FALLAN.

- [ ] **Paso 2: implementación**

`src/shared/hooks/useLocalStorage.ts`: hook genérico con clave prefijada `arquitecturabase.`, que lee una vez en la inicialización perezosa de `useState` y escribe en cada cambio, envuelto en `try/catch` (puede fallar en modo privado).

`src/shared/hooks/useMediaQuery.ts`: `matchMedia` con suscripción, para distinguir escritorio de móvil (768 px).

`src/layouts/navigation.ts`:

```ts
import type { ComponentType } from "react";

export interface NavigationItem {
  /// Clave del texto en el namespace common (navigation.*).
  labelKey: string;
  to: string;
  icon: ComponentType<{ className?: string }>;
  /// Si está, el ítem se muestra solo a quien tenga el permiso.
  permission?: string;
  /// Ítems que existen pero todavía no tienen pantalla (Fase 4).
  hidden?: boolean;
}

export interface NavigationGroup {
  labelKey: string;
  items: NavigationItem[];
}

export const navigation: NavigationGroup[] = [
  { labelKey: "navigation.general", items: [{ labelKey: "navigation.dashboard", to: "/", icon: HomeIcon }] },
  {
    labelKey: "navigation.administration",
    items: [
      { labelKey: "navigation.users", to: "/usuarios", icon: UsersIcon, permission: "users.read" },
      { labelKey: "navigation.roles", to: "/roles", icon: ShieldIcon, permission: "roles.read", hidden: true },
    ],
  },
];
```

Los íconos son componentes propios en `src/shared/ui/icons.tsx` (SVG inline, sin dependencia nueva).

`Sidebar.tsx`:
- Estado `expanded | collapsed` guardado con `useLocalStorage("sidebar", "expanded")`.
- Contraído muestra solo íconos, con `Tooltip` en cada uno.
- Los grupos se despliegan; el grupo activo arranca abierto.
- Filtra los ítems: `!item.hidden && (!item.permission || has(item.permission))`.
- En menos de 768 px es un panel deslizable sobre un fondo oscurecido, que se cierra al navegar y con Escape.
- Usa `NavLink` de react-router para marcar el ítem activo.

`Topbar.tsx`: botón ☰ (contrae en escritorio, abre el panel en móvil), `Breadcrumbs` con el título de la página y `UserMenu`.

`UserMenu.tsx`: `DropdownMenu` con el nombre y el email del usuario, "Mi perfil" (deshabilitado, Fase 4), submenú de idioma que llama a `changeLanguage`, y "Cerrar sesión" que llama a `auth.signoutRedirect()`.

`AppLayout.tsx`: `Sidebar` + `Topbar` + `<main><Outlet /></main>`, y envuelve el contenido en `<Suspense fallback={<Spinner />}>` por las páginas cargadas con `lazy`.

En `src/app/routes.ts`, las rutas protegidas pasan a colgar de `AppLayout`.

- [ ] **Paso 3: correr y ver que pasa**

```bash
npm run build && npm run lint && npm run test
```

Esperado: los tests nuevos en verde y los anteriores sin tocar.

- [ ] **Paso 4: commit**

```bash
git add .
git commit -m "feat: agregar el layout con menú lateral, barra superior y menú de usuario" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 13: Pantalla de usuarios y tablero

Repo: **front**.

**Archivos:**
- Crear: `src/features/users/api/users.ts`, `src/features/users/pages/UsersPage.tsx`, `src/features/users/columns.tsx`, `src/features/home/pages/DashboardPage.tsx` (real), `src/locales/{es,en}/users.json`
- Modificar: `src/app/routes.ts`
- Test: `src/features/users/pages/UsersPage.test.tsx`

- [ ] **Paso 1: test que falla**

`src/features/users/pages/UsersPage.test.tsx`:

```tsx
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { HttpResponse, http } from "msw";
import { describe, expect, it, vi } from "vitest";
import { renderRouteWithProviders } from "@/test/utils/renderWithProviders";
import { server } from "@/test/mocks/server";

vi.mock("react-oidc-context", async () => {
  const actual = await vi.importActual<typeof import("react-oidc-context")>("react-oidc-context");

  return { ...actual, useAuth: () => ({ isAuthenticated: true, isLoading: false, user: { access_token: "t" } }) };
});

const page = {
  items: [
    { id: "1", email: "ana@example.com", displayName: "Ana", isActive: true, createdAtUtc: "2026-09-18T12:00:00Z" },
    { id: "2", email: "beto@example.com", displayName: null, isActive: false, createdAtUtc: "2026-09-18T13:00:00Z" },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 2,
  totalPages: 1,
  hasPrevious: false,
  hasNext: false,
};

describe("UsersPage", () => {
  it("shows the users of the first page", async () => {
    server.use(http.get("/api/users", () => HttpResponse.json(page)));

    renderRouteWithProviders("/usuarios");

    expect(await screen.findByText("ana@example.com")).toBeInTheDocument();
    expect(screen.getByText("beto@example.com")).toBeInTheDocument();
  });

  it("sends the search to the backend and keeps it in the URL", async () => {
    const requests: string[] = [];
    server.use(
      http.get("/api/users", ({ request }) => {
        requests.push(new URL(request.url).search);
        return HttpResponse.json(page);
      }),
    );

    renderRouteWithProviders("/usuarios");
    await screen.findByText("ana@example.com");
    await userEvent.type(screen.getByRole("searchbox"), "ana");

    await waitFor(() => expect(requests.at(-1)).toContain("search=ana"));
  });

  it("shows the message when the search returns nothing", async () => {
    server.use(http.get("/api/users", () => HttpResponse.json({ ...page, items: [], totalCount: 0, totalPages: 0 })));

    renderRouteWithProviders("/usuarios");

    expect(await screen.findByRole("heading", { name: /no encontramos usuarios/i })).toBeInTheDocument();
  });

  it("shows the forbidden page when the backend answers 403", async () => {
    server.use(
      http.get("/api/users", () =>
        HttpResponse.json({ status: 403, code: "Http.Forbidden", detail: "No tenés permiso." }, { status: 403 }),
      ),
    );

    renderRouteWithProviders("/usuarios");

    // La sección 6.1 del spec pide la pantalla de "sin permiso", no un error dentro del listado.
    expect(await screen.findByRole("heading", { name: /no tenés permiso/i })).toBeInTheDocument();
  });
});
```

Run: `npm run test`. Esperado: FALLAN.

- [ ] **Paso 2: implementación**

`src/features/users/api/users.ts`:

```ts
import { api } from "@/shared/api/httpClient";
import type { PagedResult } from "@/shared/api/pagedResult";

export interface UserListItem {
  readonly id: string;
  readonly email: string;
  readonly displayName: string | null;
  readonly isActive: boolean;
  readonly createdAtUtc: string;
}

export interface UsersQuery {
  readonly page: number;
  readonly pageSize: number;
  readonly sort?: string;
  readonly search?: string;
}

export const usersQueryKey = (query: UsersQuery) => ["users", query] as const;

export function fetchUsers(query: UsersQuery): Promise<PagedResult<UserListItem>> {
  const params = new URLSearchParams({ page: String(query.page), pageSize: String(query.pageSize) });

  if (query.sort) {
    params.set("sort", query.sort);
  }

  if (query.search) {
    params.set("search", query.search);
  }

  return api.get<PagedResult<UserListItem>>(`/api/users?${params.toString()}`);
}
```

`src/features/users/columns.tsx`: las columnas `email`, `displayName`, `isActive` (con `Badge`) y `createdAtUtc` (fecha formateada con `Intl.DateTimeFormat` en el idioma actual y la zona horaria del perfil, sección 6.3). Los nombres de las columnas ordenables coinciden con la lista blanca del backend: `email`, `displayName`, `createdAtUtc`.

`src/features/users/pages/UsersPage.tsx`:
- `usePagination()` para página, orden y búsqueda;
- `useQuery` con `queryKey: usersQueryKey(query)`, `queryFn` y `placeholderData: keepPreviousData` (hecho verificado 6);
- `PageHeader` + `SearchInput` + `DataTable` + `Pagination`;
- si el error es un `ApiError` con status 403, la página muestra `<ForbiddenPage />` (sección 6.1 del spec);
- el resto de los errores van al estado de error del `DataTable`, con `error.detail` y botón de reintento.

`DashboardPage` pasa a mostrar un saludo con el nombre del usuario y tarjetas de ejemplo. Nada de datos inventados: solo el perfil que ya devuelve `/api/me`.

La ruta `/usuarios` va protegida con el permiso: `<ProtectedRoute permission="users.read" />`.

- [ ] **Paso 3: correr y ver que pasa**

```bash
npm run build && npm run lint && npm run test
```

Esperado: los 4 tests nuevos en verde.

- [ ] **Paso 4: commit**

```bash
git add .
git commit -m "feat: agregar el listado de usuarios y el tablero" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 14: La Api sirve el SPA, con HTTPS y encabezados de seguridad

Repo: **backend**.

Cierra la sección 6.9 del spec y el pendiente de la Fase 1: cuando la Api sirva el SPA, las rutas del backend tienen que seguir devolviendo 404 y no el `index.html`.

**Archivos:**
- Crear: `src/ArquitecturaBase.Api/Hosting/SecurityHeadersExtensions.cs`, `src/ArquitecturaBase.Api/Hosting/SpaExtensions.cs`
- Modificar: `src/ArquitecturaBase.Api/Program.cs`, `src/ArquitecturaBase.Api/DependencyInjection.cs`, `tests/ArquitecturaBase.Api.IntegrationTests/Support/ApiFactory.cs`
- Test: `tests/ArquitecturaBase.Api.IntegrationTests/Hosting/SpaHostingTests.cs`

- [ ] **Paso 1: el arnés sirve un SPA de mentira**

En `ApiFactory`, agregá una carpeta temporal con un `index.html` mínimo y decile a la Api que esa es su raíz web:

```csharp
    /// Raíz web de los tests: un index.html mínimo para probar el fallback del SPA.
    private readonly string _webRoot = Directory.CreateTempSubdirectory("arquitecturabase-wwwroot").FullName;

    public const string SpaMarker = "<!doctype html><title>spa</title>";
```

En `ConfigureWebHost`, antes de `ConfigureTestServices`:

```csharp
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), SpaMarker);
        builder.UseWebRoot(_webRoot);
```

y en `DisposeAsync`, borrala:

```csharp
        Directory.Delete(_webRoot, recursive: true);
```

- [ ] **Paso 2: tests que fallan**

`tests/ArquitecturaBase.Api.IntegrationTests/Hosting/SpaHostingTests.cs`:

```csharp
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests.Hosting;

[Collection(ApiTestGroup.Name)]
public sealed class SpaHostingTests(ApiFactory factory)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/usuarios")]
    [InlineData("/login/codigo")]
    public async Task Spa_routes_are_served_with_the_index(string url)
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, url);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("spa", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/no-existe")]
    [InlineData("/account/no-existe")]
    [InlineData("/connect/no-existe")]
    [InlineData("/health/no-existe")]
    public async Task Backend_routes_keep_returning_a_problem(string url)
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, url, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Http.NotFound", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Responses_carry_the_security_headers()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("form-action 'self' https://accounts.google.com", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Api_responses_are_not_cached_by_mistake()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
    }
}
```

Run: `dotnet test --project tests/ArquitecturaBase.Api.IntegrationTests/ArquitecturaBase.Api.IntegrationTests.csproj -- --filter-class "ArquitecturaBase.Api.IntegrationTests.Hosting.SpaHostingTests"`
Esperado: FALLAN. Hoy `/` y `/usuarios` devuelven 404 y no hay encabezados de seguridad.

- [ ] **Paso 3: implementación**

`Hosting/SecurityHeadersExtensions.cs`:

```csharp
namespace ArquitecturaBase.Api.Hosting;

internal static class SecurityHeadersExtensions
{
    /// <summary>
    /// Encabezados de seguridad de la sección 6.9 del spec. La CSP permite lo que necesita el SPA: sus propios
    /// archivos, estilos en línea (los inyectan los componentes accesibles) y el formulario que va a Google.
    /// </summary>
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-src 'self'; " +
        "frame-ancestors 'self'; " +
        "form-action 'self' https://accounts.google.com; " +
        "base-uri 'self'";

    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            headers.ContentSecurityPolicy = ContentSecurityPolicy;

            await next(context);
        });

        return app;
    }
}
```

`Hosting/SpaExtensions.cs`:

```csharp
namespace ArquitecturaBase.Api.Hosting;

internal static class SpaExtensions
{
    /// <summary>Rutas que atiende el backend: nunca caen en el index.html del SPA.</summary>
    private static readonly string[] BackendPrefixes =
        ["/api", "/account", "/connect", "/signin-google", "/.well-known", "/swagger", "/openapi", "/health", "/alive"];

    /// <summary>
    /// Sirve el build del SPA desde wwwroot y manda sus rutas al index.html (sección 5.1). Las rutas del backend
    /// que no existen siguen devolviendo 404, que UseStatusCodePages convierte en ProblemDetails.
    /// </summary>
    public static WebApplication MapSpaFallback(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var index = Path.Combine(app.Environment.WebRootPath ?? string.Empty, "index.html");

        if (!File.Exists(index))
        {
            // En desarrollo el SPA lo sirve Vite: no hay nada que servir desde acá.
            return app;
        }

        app.MapFallback(async context =>
        {
            if (IsBackendPath(context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;

                return;
            }

            context.Response.ContentType = "text/html";
            await context.Response.SendFileAsync(index, context.RequestAborted);
        });

        return app;
    }

    private static bool IsBackendPath(PathString path) =>
        BackendPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
}
```

`Program.cs`, en orden:

```csharp
app.UseSecurityHeaders();

// Primero la localización: todo lo que sigue, incluidos los errores, sale en el idioma pedido.
app.UseRequestLocalization();
...
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    await app.Services.ApplyMigrationsAsync();
    await app.Services.SeedDatabaseAsync();
    app.MapOpenApiDocumentation();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.MapDefaultEndpoints();
app.MapEndpoints();
app.MapSpaFallback();

await app.RunAsync();
```

`UseStaticFiles` va después de la autenticación a propósito: el SPA es público, pero así los encabezados de seguridad ya están puestos.

- [ ] **Paso 4: correr y ver que pasa**

```bash
dotnet build ArquitecturaBase.slnx
dotnet test
```

Esperado: 0 advertencias y todo en verde, incluidos los 9 tests nuevos.

- [ ] **Paso 5: commit**

```bash
git add src/ArquitecturaBase.Api tests/ArquitecturaBase.Api.IntegrationTests
git commit -m "feat: servir el SPA con encabezados de seguridad sin tapar las rutas de la Api" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Tarea 15: Documentación y verificación final

Repos: **los dos**.

- [ ] **Paso 1: documentación del front**

`README.md` del front: qué es, cómo levantarlo (con Aspire y suelto), los scripts (`dev`, `build`, `lint`, `test`), la estructura de carpetas en tres líneas y el enlace al spec y a los planes del backend.

`CLAUDE.md` del front: agregá lo que quedó definido durante la fase y no estaba en la Tarea 1:
- las rutas del SPA están en español y viven en `src/app/routes.ts`;
- el ingreso arranca en `/login` con el `returnUrl` que manda el servidor;
- la paginación vive en la URL (`usePagination`);
- los textos nuevos van en el namespace del módulo, en los dos idiomas.

- [ ] **Paso 2: documentación del backend**

`README.md`: en la sección de identidad, agregá cómo se levanta todo junto y qué puertos usa cada cosa (5173 el SPA, 7180 la Api), y que en producción la Api sirve el SPA desde `wwwroot`.

`CLAUDE.md`: una sección corta "Front" con el repo, el origen único, el issuer y el fallback que no toca las rutas del backend.

En `docs/plans/2026-09-18-fase-1-fundaciones.md`, marcá como hecho el último subpunto del pendiente 8 (excluir `/api` del fallback), con el mismo formato que los otros.

- [ ] **Paso 3: verificación por comandos**

Backend:

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBase
dotnet build ArquitecturaBase.slnx
dotnet test
```

Front:

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBaseFront
npm run build
npm run lint
npm run test
```

Esperado: backend con 0 advertencias y todo en verde; front con los tres comandos limpios. Mostrá los totales.

- [ ] **Paso 4: humo con el AppHost**

```bash
cd /c/Users/ezequ/source/repos/ArquitecturaBase
aspire run --detach
aspire describe
curl -sk --max-time 10 https://localhost:5173/ | head -3
curl -sk --max-time 10 https://localhost:5173/.well-known/openid-configuration
curl -sk -o /dev/null -w "%{http_code}\n" --max-time 10 https://localhost:5173/api/me
aspire stop
```

Esperado: los cuatro recursos en Running; el HTML del SPA; el issuer en `https://localhost:5173/`; y 401 en `/api/me`.

- [ ] **Paso 5: pruebas manuales (las hace el usuario)**

Pedirle al usuario que, con `aspire run` levantado, abra `https://localhost:5173` y verifique:
1. Sin sesión, lo manda al ingreso.
2. Ingreso con código: escribe su email, busca el código en el último `.eml` de `src/ArquitecturaBase.Api/.emails/`, lo escribe y entra al tablero.
3. El menú muestra Usuarios solo si su cuenta tiene el permiso (con `Seed:AdminEmail` lo tiene).
4. El listado de usuarios pagina, busca y ordena, y la URL refleja el estado.
5. Cambiar el idioma a inglés cambia los textos, incluidos los errores del backend.
6. Recargar la página mantiene la sesión (renovación silenciosa).
7. Cerrar sesión vuelve al ingreso y, al volver atrás en el navegador, no entra.
8. Ingreso con Google desde el botón de la pantalla de ingreso.

- [ ] **Paso 6: cierre**

- Revisión de código de toda la fase con un subagente revisor, sobre los dos repos.
- Agregar a este plan la sección "Resultado de la ejecución", con la misma estructura que las fases anteriores: tests por proyecto, desvíos, riesgos aceptados y pendientes para la Fase 4.
- Commit del plan en el backend:

```bash
git add docs/plans/2026-09-19-fase-3-front-base.md
git commit -m "docs: registrar el resultado de la Fase 3" -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```
