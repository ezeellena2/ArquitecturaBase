# Arranque de formularios WhatsApp — Etapa 1

Fecha: 2026-10-09. Estado: inicio de Etapa 1 autorizado; coordinación de dos chats autorizada por el usuario. Contratos y tableros pendientes de cierre. No acredita código ni integración implementados.

Fuente: [plan principal](2026-10-07-formularios-dinamicos-y-whatsapp-flows.md#12-etapas-y-orden-de-implementación), [acuerdos](formularios-modelo-demo/acuerdos-2026-10-08.md) y [diseño elegido](../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md). Reglas de cada repositorio y ADR vigentes prevalecen. Demo mantenida por Claude: sólo lectura.

## Reparto aceptado

El chat original coordina y revisa; dos chats de desarrollo se ocupan respectivamente del **backend en ArquitecturaBase** y del **frontend en ArquitecturaBaseFront**. El usuario autorizó al coordinador a crearlos y enviarles tareas y correcciones. No hace falta que copie mensajes entre ellos.

El dueño backend mantiene modelo, migraciones, API, contrato compartido y evidencia de integración. El dueño frontend mantiene sus archivos, tableros, UI, i18n y pruebas. El coordinador mantiene el plan principal y este documento. Cambiar un contrato requiere revisarlo antes de cambiar el consumidor; ningún chat altera archivos del otro ni cambia el plan principal por su cuenta. Main, sin ramas, commits ni push en este desarrollo hasta pedido explícito. Se conserva el trabajo ajeno ya presente en ambos repositorios.

## Decisiones iniciales

**Confirmado por el usuario:**
- forms.records.read adicional a whatsapp.read para leer valores de respuestas en chat.
- Auditoría por Entity + IAuditable y los interceptores existentes, con ISoftDeletable donde corresponde; sin tabla AuditLog nueva.
- Redis sólo por un caso concreto necesario; reusar coordinación/cachés vigentes. Sin caché nuevo de formularios al arrancar; respuestas, recibos, operaciones y versiones durables en PostgreSQL.

**Criterios adicionales confirmados por el usuario el 2026-10-09:**
- Al aceptar la primera solicitud de Publicar, fijar prefijo; conserva el mismo número si Meta falla y se recupera el intento.
- Mensaje de WhatsApp: botón de apertura estándar «Abrir formulario». El botón final personalizado, por ejemplo «Confirmar visita», confirma la respuesta y es otro texto.
- Identificadores internos definitivos asignados por servidor al guardar borrador; el usuario sólo edita etiquetas. Antes de guardar, identidad local provisional no autoritativa.

Estos criterios ya no requieren otra confirmación. Su aceptación no elige automáticamente tableros faltantes ni resuelve otros conflictos de la sección 11 del plan. Decisiones de catálogos/SQL, reglas, PDF y Asistente pertenecen a sus etapas; no bloquean la preparación de Etapa 1.

## Incrementos de trabajo y dependencias

| Incremento | Backend | Frontend | Evidencia conjunta |
| --- | --- | --- | --- |
| 0. Contrato y estados | DTOs/esquema format=1, ejemplos, permisos, paginado/facetas, errores y estados de publicación/envío/captura; resolver sólo decisiones iniciales pertinentes | Completar/elegir estados y prefijo en el lienzo original; estado sin permiso de lectura; inventario de shared/ui | Contrato revisable y tableros elegidos antes del código de sus superficies |
| 1. Crear/editar | TDD de definición/borrador/seis tipos, IDs y revisiones; Entity/auditoría; persistencia EF/migración; servicios/rutas/permisos/listado | Lista y editor de seis tipos con opciones inline, borrador, validación y conflictos según contrato | Guardar/recargar conserva datos e IDs; permisos/409/paginado es/en |
| 2. Publicar | Snapshot de preguntas + Fields + operación durable; compilador Meta; recuperación y activación confirmada; preview | Confirmación/diff y estados del editor/preview; sin publicación duplicada por reintento | Publicar versión y verificar estado real con Meta; versión anterior intacta |
| 3. Mandar desde chat | Elegibles por cuenta vinculada; sesión fija; extensión de cola/dispatch/historia actuales | Diálogo, cuenta vinculada y tarjeta enviada; ventana/cola/errores | Formulario llega al destinatario autorizado; 202 sólo significa cola |
| 4. Recibir/leer | Webhook/parser/worker/sink/recibo/número/proyección; lectura por lote autorizada | Tarjeta histórica/pending/rechazada y estado sin permiso de leer valores | Una respuesta/número tras replay/reinicio; contenido de su versión |
| 5. Cierre | Suites completas con Docker, revisión de migraciones, límites/retención elegidos y recorrido real | Build/lint/tests/visual, teclado/es-en/responsive y contraste de tableros | Evidencia final y documentación sólo de lo implementado; detener Aspire si se usó |

No programar el backend completo y recién después integrar todo el front: cada incremento cierra su recorrido compartido. Front puede preparar piezas aprobadas mientras espera API usando simulaciones del **contrato acordado**, sin inventar endpoints/respuestas.

Etapa 1 no incluye catálogos, planilla, captura web, aprobación, PDF, avisos automáticos, IA, grupos, correcciones ni stock. Reservar group/source.catalog en formato no implementa esas capacidades.

## Mensaje para iniciar el chat del frontend

Ámbito de trabajo del chat frontend: C:/Users/ezequ/source/repos/ArquitecturaBaseFront. El coordinador envía este alcance directamente:

```text
Vamos a desarrollar sólo la Etapa 1 de formularios por WhatsApp.
Leé AGENTS.md y las fuentes canónicas del front; después:
- docs/design/formularios-whatsapp.md y las 12 capturas elegidas.
- ../ArquitecturaBase/docs/plans/formularios-etapa-1-arranque.md.
- ../ArquitecturaBase/docs/plans/2026-10-07-formularios-dinamicos-y-whatsapp-flows.md.
- ../ArquitecturaBase/docs/plans/formularios-modelo-demo/acuerdos-2026-10-08.md.

Este chat se ocupa exclusivamente del frontend; otro chat desarrolla el backend y
el original coordina y revisa el contrato. No cambies archivos del backend ni la demo.
Arrancá por el incremento 0: relevar/completar estados y el campo de prefijo en el
mismo lienzo elegido, con los componentes reales del sistema; no hagas otra maqueta
HTML. Sin permiso forms.records.read, no mostrar valores de respuestas del chat.
Si no podés editar el lienzo original, identificá exactamente qué acceso falta y
entregá las variantes pendientes para completar allí; no sustituirlo por otra referencia.

Después implementá por incrementos las superficies elegidas contra el contrato
acordado con backend, cumpliendo la elección previa de tableros y todas las reglas
de paginado, i18n, errores, permisos, mensajes, QueryCache y accesibilidad.
No avances a etapas posteriores. Main, sin ramas, commits ni push.
No adivines APIs ni uses la maqueta del 7/8 de octubre como fuente.
```

El frontend no figura como proyecto guardado en la lista consultada. Ambos chats se crean en el proyecto local guardado ArquitecturaBase; el chat frontend tiene alcance exclusivo en el repositorio hermano y ejecuta comandos con ese directorio explícito. Esto no mueve archivos ni modifica la arquitectura. Las escrituras fuera de su sandbox deben seguir el mecanismo de aprobación del entorno; no cambiar permisos ni fingir que su carpeta es la del backend.

## Coordinación y entregas

El contrato inicial se prepara en `formularios-etapa-1-contrato.md`, con estado de propuesta hasta revisión conjunta; no publica endpoints implementados por escribirlos en Markdown. Backend y frontend entregan sus hallazgos y evidencia en sus respectivos chats. El coordinador consulta avances, transmite correcciones y revisa cada incremento antes de habilitar el siguiente. El primer encargo es el incremento 0; backend puede avanzar con TDD del modelo puro aprobado, mientras frontend releva las superficies elegidas y las variantes pendientes. Ninguna pantalla nueva o variante pendiente se programa antes de dibujarla y elegirla en el lienzo original.

Chats creados el 2026-10-09, host local:

| Responsabilidad | Chat | Primer encargo |
| --- | --- | --- |
| Coordinación y revisión | 01a10cf6-23c4-7641-b084-a486091df430 | Criterios aceptados, reparto, revisión del contrato y secuencia de integración |
| Backend | 01a120d3-7f84-7471-ae4f-fa71986768f5 | Propuesta de contrato y primer bloque pequeño de modelo puro con TDD; sin controllers/migraciones/Meta hasta revisión |
| Frontend | 01a120d4-1415-7ca1-a73d-cabbfa72e695 | Relevamiento de componentes/tableros y variantes pendientes; revisión de contrato cuando esté disponible, sin UI/API hasta revisión y elección |

Preparación del front: `ArquitecturaBaseFront/docs/plans/formularios-etapa-1-preparacion.md`. Ambos encargos piden entregar evidencia y esperar el siguiente incremento. Este reparto no acredita seguimiento automático entre turnos ni integración terminada.

Primera entrega frontend revisada el 2026-10-09: relevamiento completo y estados pendientes; sin UI/API. El coordinador verificó e1-1: prefijo editable ya dibujado, faltan variantes de bloqueo/errores. Siguiente encargo enviado: propuestas concretas del primer bloque, verificar acceso editable al mismo lienzo y revisar contrato cuando esté disponible. Backend recibió prioridad para entregar primero crear/listar/obtener/guardar; la incompatibilidad observada de nombres de permisos con la convención vigente se mantiene pendiente de contrastar y decidir, no se renombra unilateralmente.

## Estado de verificación

Este documento organiza el trabajo; no declara build/tests/pilotos nuevos. La [revisión anterior](../reviews/2026-10-08-formularios-whatsapp-acuerdos.md) conserva resultados del producto existente. Cada incremento de código tendrá su TDD, checks y evidencia propios; el cierre exige Docker y Meta reales. Pantallas faltantes siguen la regla del front: primero tablero y elección explícita.

Verificación documental de este reparto: auditor estructural del 2026-10-09, ejecución `2026-10-09T13-24-10.383Z-5c7f1b66`, con diff-check correcto en ambos repositorios y los mismos cuatro pendientes previos de mapa/anclas del front; ninguno nuevo en estos documentos. El primer intento quedó incompleto por EPERM del sandbox; la ejecución con aprobación del entorno completó los controles. No se ejecutaron suites del producto por este cambio de planificación; los chats de desarrollo verifican sus propios incrementos.
