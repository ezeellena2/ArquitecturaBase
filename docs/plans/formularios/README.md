# Formularios por WhatsApp: etapas

Estado al 2026-10-16. Para trabajar en formularios se lee solo el resumen de la etapa en curso: tiene el alcance y las decisiones ya tomadas, que no se vuelven a preguntar. Lo ya implementado está en [formularios](../../features/formularios.md) y [WhatsApp](../../features/whatsapp.md); las pantallas, en el [diseño del front](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md).

| Etapa | Resumen | Estado |
| --- | --- | --- |
| 1 · Crear, publicar, mandar y recibir | [formularios](../../features/formularios.md#verificación-de-cierre--2026-10-11) | Cerrada el 2026-10-11 |
| 2 · Catálogos a mano y con filtros | [etapa-2-catalogos.md](etapa-2-catalogos.md) | Cerrada el 2026-10-15, con el piloto real con Meta |
| 2B · Fotos y documentos | [Cierre y reglas implementadas](../../features/formularios.md#cierre-de-la-etapa-2b--2026-10-15) | Cerrada el 2026-10-15, con Meta y Azure privado reales |
| 3 · Planilla, detalle y exportar a Excel | [Reglas y cierre](../../features/formularios.md#planilla-detalle-y-excel--etapa-3) | Cerrada el 2026-10-16, con respuestas reales y apertura del XLSX en Excel |
| 4 · Reglas, aprobación, PDF y avisos | [etapa-4-reglas-aprobacion-pdf.md](etapa-4-reglas-aprobacion-pdf.md) | Diseño elegido |
| 5 · Asistente, sugerencias y audios | [etapa-5-asistente.md](etapa-5-asistente.md) | Diseño elegido; [ADR 0020](../../decisions/0020-motor-unico-de-ia-con-capacidades.md) aceptado y OpenAI elegido el 2026-10-16 |

La carga de respuestas desde la web y la importación desde Excel quedan para después de la Etapa 4, con tableros propios.

## Después de la Etapa 5

Primero, **el idioma de los formularios** (decidido el 2026-10-16): solo el idioma original es obligatorio para guardar y publicar; la traducción es opcional. Si no está completa, el formulario, el PDF, los mensajes y las opciones de catálogo salen en el idioma original, sin mezclar. Cambia la regla D5 de la Etapa 1 (publicar exige es/en), la publicación en Meta (solo los idiomas completos), el envío y los catálogos; antes se ajustan los tableros del editor y de publicar. El idioma del sistema no cambia. Varios países y más idiomas, después.

Sin orden fijo; cada una necesita su tablero y su decisión antes de programarse:
- catálogo con una consulta (Query/K5), solo si una lista no puede armarse con filtros ([ADR 0019](../../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md));
- intercambio cifrado con Meta: búsqueda en vivo, listas dependientes y listas largas;
- grupos y remitos con líneas;
- corregir una respuesta (una respuesta nueva que reemplaza a la anterior);
- borrador de formulario armado por la IA a partir de los pedidos de formularios que faltan;
- copiar y eliminar definiciones, y deprecar en Meta los Flows de versiones reemplazadas (pendientes en [planes](../README.md)).

La [demo de datos](../formularios-modelo-demo/README.md) es una base aislada de demostración, no una migración.
