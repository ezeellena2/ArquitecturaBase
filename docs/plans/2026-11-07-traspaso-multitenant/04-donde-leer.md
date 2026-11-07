# 04 · Dónde leer cada cosa

Las rutas son relativas a cada repo: `ArquitecturaBase` (backend y documentación) y `ArquitecturaBaseFront` (front y
diseño registrado).

## La plantilla y sus reglas (ArquitecturaBase)

- `AGENTS.md`: el índice de reglas para agentes. `CLAUDE.md` suma lo propio de Claude Code.
- `docs/architecture/backend.md`: la arquitectura canónica. Prevalece sobre cualquier descripción histórica.
- `docs/architecture/premisas-de-la-plantilla.md` (las premisas, con su fuente) y `docs/architecture/mapa-de-instrucciones.md`
  (los `AGENTS.md` por carpeta).
- `docs/guides/validar-un-cambio.md`: cómo se revisa y se verifica cada cambio.
- `docs/decisions/README.md`: los ADR 0001 a 0025. Los últimos son 0023 (ARCA como módulo opcional), 0024 (la IA arma
  formularios y catálogos) y 0025 (familias de servicios de ARCA).
- `README.md`: cómo levantar y probar la app (`aspire run` y apagarla con `aspire stop`).

## Las reglas de producto implementadas (ArquitecturaBase/docs/features)

`formularios.md`, `whatsapp.md`, `inteligencia-artificial.md`, `arca.md`, `identidad.md` y `administracion.md`. Son la
fuente de verdad de lo que existe hoy. Cada sección dice desde cuándo vale.

## Los planes (ArquitecturaBase/docs/plans)

- `README.md`: el índice de lo pendiente, con una línea de estado por plan.
- Renglones: `2026-11-03-renglones-en-formularios.md` (decisiones RG1-A a RG13-A y las cuatro etapas), más los planes de
  implementación de las etapas 1, 2 y 3 (`2026-11-04-…-etapa-1-…`, `2026-11-07-…-etapa-2-…` y `2026-11-07-…-etapa-3-…`).
- `2026-11-07-elegir-primero-por-un-campo.md` (LD1-A a LD3-A).
- `2026-10-29-cargar-formularios-desde-la-app.md`, `2026-11-03-carpetas-de-formularios-y-catalogos.md`,
  `2026-10-31-arca-formularios-estandar.md` y `2026-11-04-ia-arma-formularios-y-catalogos.md`.
- `verificacion-y-produccion.md`: lo que falta verificar antes de salir a producción.

## El diseño registrado (ArquitecturaBaseFront/docs/design)

Cada archivo registra lo elegido tablero por tablero, con lo descartado:

| Archivo | Qué registra |
| --- | --- |
| `visual-baseline.md` | El fundamento visual vigente: principios, piezas, mensajes y estados. Incluye «El estado, una vez» (EP1-A) y «Esqueletos de carga» (ES1-A, ES2 y ES3). |
| `formularios-whatsapp.md` | Formularios por WhatsApp: etapas 1 a 5, baja y reactivación, menú y detalle, el estado de la publicación y la vista oficial (EP1-A, VO1-A) y el mensaje que nombra el formulario. |
| `carga-desde-la-app.md` | La carga desde la app (CW): lo propio, corregir y anular, títulos de sección y elegir primero por un campo (LD1-A a LD3-A). |
| `renglones-en-formularios.md` | Renglones, RG0 a RG12, con la tabla de textos de RG12-A («ítem»). |
| `catalogos-mejoras.md` y `catalogos-campos-extra.md` | Catálogos (CA1 a CA6) y sus campos extra y filtros por persona (CE1 a CE6). |
| `ia-arma-formularios.md` | La IA arma formularios y catálogos. |
| `arca-conexion.md` | La conexión con ARCA. |
| `documentos.md` y `asistente-casos.md` | Documentos por WhatsApp (DOC1 a DOC7) y casos del asistente (AS1 a AS6), decididos y sin programar. |
| `informacion-del-negocio-guiada.md`, `inicio-asistente-propuesta.md`, `ubicacion-por-lugar.md`, `whatsapp-chat-propuesta.md` y `whatsapp-bibliotecas-propuesta.md` | Información del negocio, Inicio con el asistente, ubicación con Google Places, la bandeja de WhatsApp y la comparación de bibliotecas de chat. |

Las capturas que acompañan esos registros están en `docs/design/evidence/`.

## La interfaz real, sin levantar la app

`ArquitecturaBaseFront/src/test/visual/screenshots/` tiene 179 capturas de referencia de los tests visuales:
- Se llaman `<escena>-<es|en>-<ancho>.png`, a 1440, 390 y 320 de ancho.
- Cubren Inteligencia artificial y el asistente, ARCA, Configuración e identidad (ajustes, apariencia, Información del
  negocio, perfil, usuarios, roles, ingreso), catálogos, el editor de formularios y WhatsApp.
- Las escenas más nuevas (el progreso de la publicación, la vista oficial, los esqueletos, el mensaje del formulario y los
  renglones) están en los specs de `src/test/visual/`, pero no se versionan como PNG.

Para ver una pantalla que no tiene captura, hay que levantar la app (README del backend) o correr los visuales del front.

## Código de referencia de lo nuevo

- **Renglones:**
  - backend: `WhatsAppFlowScreenBuilder` (`BuildRows`, el `routing_model`), `WhatsAppFlowCompiler` (los presupuestos),
    `WhatsAppWebhookReader` (las claves `_r<n>`) y `WhatsAppFlowReplyService.ProjectionIsWellFormed` (hoy rechaza `group`);
  - front: el editor del bloque y su vista previa en `features/business-forms`.
- **Vista oficial y estado:**
  - backend: `FormCatalogOptionsPolicy`, `WhatsAppFlowNavigation`, `WhatsAppFlowStartScreen` y `SecurityHeadersExtensions`;
  - front: `OfficialFormPreview`, `shared/ui/EmbeddedPreview`, `PublicationProgress`, `usePublicationProgress` y
    `FormEditorPage`.
- **El nombre del Flow:** `WhatsAppFlowTechnicalName`.
- **El mensaje que acompaña:**
  - backend: `WhatsAppFormSendService`, `FlowTexts` y `AssistantTexts`;
  - front: `whatsapp/hooks/useFormComposer` y `whatsapp/lib/formSendSession`.
- **Rendimiento:**
  - backend: `PermissionAuthorizationHandler`, `UseSpaStaticFiles` (en `Api/Hosting/SpaExtensions`) y la compresión en la
    composición de la Api;
  - front: `shared/ui/PageLoading`, `shared/ui/FormSkeleton`, `DataTable` y `Page`.
