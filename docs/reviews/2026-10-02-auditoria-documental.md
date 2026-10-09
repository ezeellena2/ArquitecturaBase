# Auditoría documental de backend y front — 2026-10-02

**Resultado:** documentación alineada con el estado local comprobado, siguiendo la
[rutina de auditoría](../guides/auditar-documentacion.md). Esta pasada modifica documentación;
no cambia el comportamiento del producto.

## Alcance y criterio

Se inventariaron **246 archivos Markdown** versionados o nuevos de ambos repositorios,
excluyendo dependencias y outputs. Se revisaron índices, instrucciones, fuentes canónicas,
guías, ADR, diseños, planes y evidencia fechada. Las afirmaciones vigentes se contrastaron
con contratos, servicios, adaptadores, configuración y pruebas; los diseños originales
conservan su carácter histórico cuando difieren del producto actual.

La revisión trabajó sobre `main`, con cambios locales existentes y desarrollos concurrentes.
Referencias de Git al iniciar: backend `6ed49ba3ea9a947d931a66a023d55a23cd61d05a`;
front `976a97321bf406529bc554a9bd6d2a7ea89b0e21`. La evidencia corresponde al árbol local
con sus cambios, no solamente a esos commits. Se conservaron los cambios ajenos y no se
crearon ramas, commits ni push.

## Diferencias reconciliadas

| Tema | Fuentes actualizadas y resultado |
| --- | --- |
| Configuración | Arquitectura de ambos repositorios y plan de mensajes: General reúne los cuatro ajustes con guardado conjunto; Apariencia es otra pantalla. Las URLs anteriores redirigen. |
| Permisos | Administración, recetas de permiso/área, despliegue y premisas: `Permissions.All` pertenece al núcleo; `IPermissionCatalog.All` compone las fuentes opcionales. Admin recibe ese catálogo al ejecutar el seed. |
| Acceso y atención | WhatsApp, Administración y fuentes del front: ingresar por código también confirma el número ya cargado; no vincula el contacto de atención. El webhook no autentica ni emite enlaces secretos. `/ingresar` conserva compatibilidad histórica. Manual es el default; Bot legado no tiene automatización configurada. |
| Colas y coordinación | Despliegue y receta de caché distinguen la cola en memoria de códigos/invitaciones/orientación de la salida manual durable en PostgreSQL. La lease renovable de envío comparte el adaptador Redis y no reemplaza la persistencia. |
| Retiro de WhatsApp | La receta incluye las cinco tablas actuales, dependencias de borrado, snapshot y reversión, salidas pendientes/inciertas, el front y la conservación de ADR. La prueba de retiro de dos tablas permanece identificada como histórica. |
| Front y diseño | Funciones de fecha vigentes, paleta Campo, restauración de Apariencia, teclado del chat y sus controles actuales. Se conservaron las fuentes de landing y logout actualizadas durante la pasada. |
| Planes y evidencia | Bandeja y separación distinguen inventarios originales de lo implementado y de las verificaciones abiertas. Producción incorpora la revisión funcional con API real y la validación automática de WhatsApp; conserva los pendientes externos. Unificación de mensajes sigue siendo adopción parcial. |
| Instrucciones e índices | Mapa y receta: 67 pares backend y 26 front. Recursos del núcleo y módulos, referencias a reviews y fechas de consolidación reflejan el estado actual. |

No se retiraron más planes: los vigentes conservan propuestas, adopción parcial,
verificaciones abiertas o información única todavía sin procedencia recuperable.
Los planes previamente retirados mantienen su índice en [history](../history/README.md).

## Validación ejecutada en esta pasada

| Check | Resultado |
| --- | --- |
| Backend: build de `ArquitecturaBase.slnx` | Correcto, cero advertencias y errores. Se usó `--artifacts-path .test-artifacts/documentation-audit/build` para aislar outputs. |
| Backend: `dotnet test --no-build --no-restore` sobre esos outputs | **2057 tests aprobados, cero fallidos y omitidos**, incluidos los de integración con Docker, PostgreSQL y Redis reales. |
| Front: build | TypeScript y Vite correctos; repetido después de los cambios concurrentes de landing/logout. |
| Front: lint | Correcto; **38 tests de arquitectura** aprobados. |
| Front: tests | **829 tests en 78 archivos** aprobados, repetidos sobre el estado reciente. |
| Estructura documental | Sin destinos o anclas rotos en enlaces Markdown locales; los **93 pares AGENTS/CLAUDE** importan la fuente correcta y figuran en sus mapas. |
| Espacios y diff | `git diff --check` limpio en ambos repositorios. |

El wrapper local de npm no resolvía su CLI; los tres scripts se ejecutaron con Node y el
`npm-cli.js` de la instalación existente. El backend necesitó acceso a la configuración
local de NuGet y a Docker. No se levantó Aspire ni se repitieron smokes manuales o visuales:
esta pasada documental no modifica UI. Las capturas y recorridos previos mantienen su fecha
y alcance en los informes originales, sin presentarse como nuevas pruebas de esta auditoría.

## Pendientes conservados

- Prueba con teléfono/payload de Meta posterior a la separación de acceso y atención.
- Sincronización de los cambios recientes con el Artifact externo y su biblioteca.
- Migración transversal de consumidores de mensajes; IA, contactos, plantillas y segunda instalación.
- Preparación de producción, Google/SMTP externos y comprobaciones operativas del
  [registro de verificación](../plans/verificacion-y-produccion.md).
- Archivos y fondo del chat: Azure elegido en ADR 0012, implementación y cuenta de desarrollo
  pendientes según su [plan](../plans/2026-10-02-almacenamiento-de-archivos.md).

Los checks locales no acreditan esas comprobaciones externas. La guía de retiro del módulo
queda reconciliada con su modelo actual; esta pasada no ejecutó un nuevo retiro en una copia
ni un despliegue de producción.

## Limpieza posterior solicitada por el usuario

El 2026-10-02 se retiraron los diseños redundantes de Administración y Configuración general:
**315 líneas** de descripción histórica ya reemplazada por las fuentes vigentes. Se corrigieron
las referencias y se conservó su recuperación desde Git en [history](../history/README.md#diseños-redundantes-retirados-el-2026-10-02).
La propuesta heredada de administrar dispositivos/sesiones quedó identificada en el registro
de pendientes, sin convertirla en una entrega acordada. El inventario resultante tiene
**244 archivos Markdown**. Los demás planes conservan propuestas o información única;
los ADR, guías vigentes e informes de validación mantienen su función.

La limpieza terminó con enlaces/anclas/mapas y `git diff --check` sin errores. Se repitieron
los builds de ambos repositorios, lint y 829 tests del front, y los 2057 tests del backend
incluida integración con Docker; todos aprobaron. No se levantó Aspire.

## Arnés manual agregado después de la auditoría

La implementación está en [harnesses](../../harnesses/README.md), con una entrada PowerShell,
runner Node, workflow y contrato comunes, y adaptadores de Codex/Claude. El usuario lo inicia;
`audit` guarda propuestas y `update` aplica solo documentación validada. El nuevo ámbito tiene
su par AGENTS/CLAUDE y figura en el mapa. Los resultados temporales se guardan fuera de la
documentación canónica, en `.test-artifacts/harnesses/documentation/`.

Se probaron **37 casos del arnés**: propuestas de ambos motores con respuestas simuladas,
lectura de resultados, rutas, enlaces, retiros recuperables, conservación de cambios,
concurrencia, fallas parciales, lock y captura de procesos. Se verificaron las opciones con
la ayuda de ambas CLIs instaladas; no se ejecutó un análisis semántico autenticado de los
motores. Las simulaciones no acreditan esa integración externa.

Se ejecutó la entrada real con `-DryRun` y con `-Engine none -Checks full`: cero errores
estructurales, build backend sin advertencias, **2057 tests backend** con Docker, build front,
lint con **38 pruebas de arquitectura**, **829 tests front** y diff de ambos repositorios
correctos. Este recorrido verifica el runner y sus controles, sin atribuirle una nueva
auditoría semántica ni iniciar Aspire.
