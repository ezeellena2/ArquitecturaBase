# Revisión del plan con el diseño elegido de formularios WhatsApp

Fecha: 2026-10-08. Alcance: documentación y orden de entrega del
[plan](../plans/2026-10-07-formularios-dinamicos-y-whatsapp-flows.md).
**Diseño elegido, implementación pendiente.**

## Fuentes y cambio de referencia

Se leyó [Formularios por WhatsApp](../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md)
y se inspeccionaron sus doce capturas en
[evidence/formularios-whatsapp](../../../ArquitecturaBaseFront/docs/design/evidence/formularios-whatsapp/):
01–08 y e1-1–e1-4. El documento y esa evidencia congelada son autoridad de pantallas,
navegación, textos y entrega; no se accedió ni se modificó el Artifact privado original.

La maqueta aislada del 7/8 de octubre queda como procedencia sustituida. Se anotó esa
condición en su [revisión histórica](2026-10-07-formularios-dinamicos-maqueta.md), sin mejorar,
promover ni borrar el HTML ni sus variantes. El plan conserva arquitectura híbrida, puertos,
versiones, persistencia, seguridad y contratos; no los promueve a reglas implementadas.

Se contrastaron instrucciones de ambos repositorios/documentación, backend canónico,
reglas de WhatsApp, migración, arquitectura y contratos visuales/mensajes del front.
Se revisó el chat existente: WhatsAppChatService/ChatDispatchService, la cola manual
WhatsAppOutgoingMessage y su worker/coordinador, parser WhatsAppWebhookReader, persistencia
del webhook y vínculos explícitos; ChatComposer, ChatTimeline y navegación estática.
El parser hoy no interpreta nfm_reply y el dispatcher manual sólo envía texto: su extensión
está en el alcance, no se describe como ya disponible.

## Decisiones propuestas y alcance

- Etapa 1: editor único con preview, seis tipos y listas manuales; publicar versión con una
  acción durable, enviar desde el chat actual y guardar/ver respuesta numerada allí.
  Sin IA, planilla, aprobación, PDF, catálogos ni respuestas automáticas.
- Diez tablas nuevas propuestas: siete de BusinessForms (incluida operación de publicación)
  y tres de WhatsApp; ampliar cola/historia actuales, sin otro outbox manual. Dos migraciones
  en cadena única, rutas mínimas/inventario/permisos, TDD, Docker y piloto real detallados.
- La publicación durable nueva se justifica por el recorrido de una sola acción y por la
  recuperación entre commits de módulos sin depender del navegador; no se reutiliza la
  cola cifrada de entregas como un motor genérico de procesos ni se anidan UoW.
- Numeración: regla propuesta de prefijo automático único F000001, inmutable por definición,
  y secuencia de respuesta F000001-000001 bajo lock/commit. No añade campo a la UI.
- Panel inicial sólo Diseñar formularios; después lector paginado/autorizado del backend
  compone destinos por formulario. No codificar nombres de ejemplos en navigation.ts.
- La sección 11 identifica variantes faltantes y conflictos con propuestas para decisión.
  Los tableros nuevos deben completarse en el mismo lienzo elegido antes del código.

Se registraron especialmente: lista completa versus alcance inicial, CTA de apertura versus
botón final, rechazo Meta global versus error de pregunta, garantía externa de no duplicar,
permisos por formulario/lectura en chat, idioma del contenido, oficial iframe/CSP y retorno
del editor tras guardar. Las propuestas no se atribuyen a una elección posterior del usuario.

## Límites de evidencia externa

Se consultaron fuentes primarias de Meta (colección oficial Postman y herramientas oficiales
WhatsApp en GitHub) sobre publicación, preview y mensaje/respuesta Flow. La documentación
actual de envío/Flow JSON no se obtuvo completa por 429/cache miss. No se fijan versiones
Graph históricas, límites ni prerequisitos por ejemplos. Perfil de los seis tipos, iframe/CSP
en nuestro origen y publicación/envío/respuesta reales deben calificarse en la Etapa 1.

El Flow visto el 7 de octubre no se consultó de nuevo ni se modificó. Ninguna llamada a Meta,
publicación, envío a un teléfono o actualización de cuenta externa se realizó en esta revisión.

## Validación de esta revisión

Checks del producto existente, separados de las pruebas específicas de Etapa 1, que todavía
no existen:

- `dotnet build ArquitecturaBase.slnx --no-restore`: correcto, cero advertencias y errores.
- `dotnet test --no-build --no-restore`: 2.340 correctas, cero errores/omitidas; incluye
  integración con Docker PostgreSQL 18.3 y Redis 8.6 (11 min 24 s de ejecución total).
- Front `npm run build`: TypeScript/Vite correctos; aviso informativo PLUGIN_TIMINGS de
  Tailwind/Vite, sin error de compilación. No se describe como ejecución sin avisos.
- Front `npm run lint`: correcto, incluidos 48 controles de arquitectura.
- Front `npm run test`: 96 archivos y 1.014 pruebas correctas.
- `git diff --check`: correcto en ambos repositorios. Los cuatro documentos modificados
  están sin versionar; además del diff se revisaron contenido/enlaces y whitespace directamente.
- Auditor documental `-Engine none -Checks structural`: sin enlaces rotos introducidos por
  esta revisión, pero estado general **incomplete** por cuatro hallazgos previos ajenos al
  alcance. Informe en `.test-artifacts/harnesses/documentation/2026-10-08T16-57-28.262Z-664647a9/report.md`.
  El modo estructural no acredita una auditoría semántica completa de ambos repositorios.

Hallazgos conservados, sin corregir pantallas/documentación ajenas para obtener verde:

| Hallazgo previo | Fuente | Criterio de cierre |
| --- | --- | --- |
| Dos AGENTS de laboratorios de Inicio fuera del mapa | `ArquitecturaBaseFront/design-lab/history/home-assistant-comparison-2026-10-04/AGENTS.md` y `design-lab/home-assistant/chosen/AGENTS.md` | Auditar vigencia de esos laboratorios y corregir mapa/procedencia en su tarea, sin borrarlos por su antigüedad |
| Ancla inexistente de comparación de bibliotecas | `ArquitecturaBaseFront/docs/design/inicio-asistente-propuesta.md` → plan 2026-10-04 | Corregir consumidor o rescatar destino real tras revisar la decisión de Inicio |
| Ancla inexistente en fundamento visual | `ArquitecturaBaseFront/docs/design/visual-baseline.md` → inicio-asistente-propuesta.md | Resolver la referencia histórica en su revisión y repetir controles |

No corresponde `npm run test:visual` ni abrir/actualizar capturas de producto: esta entrega
sólo cambia Markdown y no implementa ni altera una pantalla. La revisión manual fue de las
doce imágenes elegidas y del encaje del plan con el código existente; no un piloto Meta ni
una validación de UI nueva. La primera ejecución del arnés encontró también los tres enlaces
a este registro antes de crearlo; al completar el documento esos hallazgos desaparecieron.

No se creó código productivo, migración ni tablero nuevo. Se conservan los cambios previos
de otras tareas; los documentos del plan/índice/revisión histórica ya estaban sin versionar.
No se hicieron ramas, commits ni push. No se levantó Aspire.
