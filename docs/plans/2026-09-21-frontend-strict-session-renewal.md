# Frontend Strict Session Renewal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `subagent-driven-development` (recommended) or `executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hacer efectiva la promesa de TypeScript estricto y garantizar que toda renovación iniciada por un 401 sea única, fail-closed y termine en el mismo error HTTP tipado cuando no obtiene un token.

**Architecture:** Ambos proyectos TypeScript referenciados habilitarán `strict`. Se desactivará la renovación automática independiente de `oidc-client-ts` para que no compita por el refresh token con la recuperación single-flight iniciada por el cliente HTTP. `react-oidc-context` actualmente captura los rechazos de su `signinSilent()` y devuelve `null`; aun así, el contrato defensivo de `HttpClientOptions` admitirá callbacks que rechacen, los normalizará a “sin token”, conservará el 401 ProblemDetails original y notificará expiración una vez por cada request 401 final. `finally` siempre liberará la promesa compartida para permitir una renovación posterior.

**Tech Stack:** TypeScript 6, React 19, Vitest, MSW.

---

### Task 1: Especificar el rechazo de silent renew

**Files:**
- Modify: `../ArquitecturaBaseFront/src/shared/api/httpClient.test.ts`
- Create: `../ArquitecturaBaseFront/src/auth/authConfig.test.ts`

- [x] **Step 1: Write the failing test**

Simular una respuesta 401 ProblemDetails y un `renewSession` defensivo que rechaza; verificar una sola renovación, una llamada a `onSessionExpired` y un `ApiError` que conserva `status`, `code`, `detail` y `traceId` del 401 original en lugar de propagar el error ajeno al contrato HTTP. Agregar otro caso con tres requests concurrentes para verificar una sola renovación compartida, una expiración por request final y, después del rechazo, un request nuevo que sí pueda volver a renovar. Agregar un test de configuración que exija `automaticSilentRenew: false`.

- [x] **Step 2: Run the focused test and verify it fails**

Run from `../ArquitecturaBaseFront`: `npm run test -- src/shared/api/httpClient.test.ts src/auth/authConfig.test.ts`

Expected: FAIL porque hoy escapa el error crudo y no expira la sesión.

### Task 2: Unificar y normalizar la renovación

**Files:**
- Modify: `../ArquitecturaBaseFront/src/shared/api/httpClient.ts`
- Modify: `../ArquitecturaBaseFront/src/auth/authConfig.ts`

- [x] **Step 1: Implement the minimal behavior**

Capturar cualquier rechazo dentro de la promesa single-flight de `renewOnce`, devolver `undefined` y conservar el `finally` que libera la renovación compartida. Es una decisión fail-closed: el 401 original es la respuesta autoritativa y los detalles internos de OIDC no reemplazan el contrato de la API. Desactivar `automaticSilentRenew`; la recuperación al cargar la página sigue a cargo de `SessionRecovery` y, con sesión activa, el primer 401 renueva y reintenta una sola vez.

- [x] **Step 2: Run the focused tests**

Run from `../ArquitecturaBaseFront`: `npm run test -- src/shared/api/httpClient.test.ts src/auth/authConfig.test.ts`

Expected: todos los tests de `httpClient` pasan, incluidos renovación exitosa, concurrencia y rechazo.

### Task 3: Activar TypeScript strict

**Files:**
- Modify: `../ArquitecturaBaseFront/tsconfig.app.json`
- Modify: `../ArquitecturaBaseFront/tsconfig.node.json`

- [x] **Step 1: Enable strict in both referenced projects**

Agregar `"strict": true` a ambos `compilerOptions`; el `tsconfig.json` raíz sólo contiene referencias y no propaga esa opción.

- [x] **Step 2: Verify the frontend**

Run from `../ArquitecturaBaseFront`: `npm run build`

Run: `npm run lint`

Run: `npm run test`

Expected: build y lint limpios y suite verde.
