# Formularios por WhatsApp: etapas

Estado al `2026-10-19`. Las reglas implementadas están en [formularios](../../features/formularios.md) y [WhatsApp](../../features/whatsapp.md); las pantallas, en el [diseño del front](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md). Los próximos desarrollos conservan abajo su alcance y requieren sus decisiones y tableros antes de programarse.

| Etapa | Resumen | Estado |
| --- | --- | --- |
| 1 · Crear, publicar, mandar y recibir | [formularios](../../features/formularios.md#verificación-de-cierre--2026-10-11) | Cerrada el 2026-10-11 |
| 2 · Catálogos a mano y con filtros | [etapa-2-catalogos.md](etapa-2-catalogos.md) | Cerrada el 2026-10-15, con el piloto real con Meta |
| 2B · Fotos y documentos | [Cierre y reglas implementadas](../../features/formularios.md#cierre-de-la-etapa-2b--2026-10-15) | Cerrada el 2026-10-15, con Meta y Azure privado reales |
| 3 · Planilla, detalle y exportar a Excel | [Reglas y cierre](../../features/formularios.md#planilla-detalle-y-excel--etapa-3) | Cerrada el 2026-10-16, con respuestas reales y apertura del XLSX en Excel |
| 4 · Reglas, aprobación, PDF y avisos | [Reglas y verificación local](../../features/formularios.md#verificación-de-la-etapa-4--2026-10-16) | Verificada localmente y con pilotos Meta/Azure reales en español e inglés; [comprobaciones físicas de idiomas restantes](../verificacion-y-produccion.md#formularios-idioma-original-y-traducción-opcional) |
| 5 · Asistente, sugerencias y audios | [Reglas y cierre local](../../features/formularios.md#cierre-local-de-la-etapa-5-y-del-review-de-la-etapa-4) | Implementada y verificada localmente con [calificación OpenAI](../../guides/configurar-ia.md#evidencia-para-habilitar-perfiles); [piloto físico parcial y comprobaciones restantes](../verificacion-y-produccion.md#formularios-piloto-del-asistente-y-audios). La regla de publicación vigente es [original obligatorio y traducción opcional](../../features/formularios.md#idioma-original-y-traducción-opcional--2026-10-19) |

La carga de respuestas desde la web y la importación desde Excel quedan para después de la Etapa 4, con tableros propios.

## Después de la Etapa 5

El cambio de idioma original y traducción opcional (I1–I3) está implementado y verificado localmente; [reglas](../../features/formularios.md#idioma-original-y-traducción-opcional--2026-10-19) y [cierre local](../../features/formularios.md#verificación-local-de-idioma-original-y-traducción-opcional--2026-10-19). El [piloto físico con Meta](../verificacion-y-produccion.md#formularios-idioma-original-y-traducción-opcional) conserva los casos de publicación, traducción completa y respaldo P24. Varios países y más idiomas quedan para después.

Sin orden fijo; cada una necesita su tablero y su decisión antes de programarse:
- catálogo con una consulta (Query/K5), solo si una lista no puede armarse con filtros ([ADR 0019](../../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md));
- intercambio cifrado con Meta: búsqueda en vivo, listas dependientes y listas largas;
- grupos y remitos con líneas;
- descargar todas las fotos juntas desde el detalle de una respuesta;
- corregir una respuesta (una respuesta nueva que reemplaza a la anterior);
- borrador de formulario armado por la IA a partir de los pedidos de formularios que faltan;
- menú de Formularios: una entrada «Formularios» con todos los formularios y lo que cada uno tiene por aprobar, en lugar de las secciones Respuestas y Por aprobar del panel. Diseñar formularios, Catálogos y Formularios que faltan quedan como están. Elegido MF1 el 2026-10-20, junto con los tableros de [dar de baja](dar-de-baja-formularios.md);
- detalle completo de una respuesta: el panel D2 queda como vista rápida y suma «Abrir en pantalla completa» (DC0). La pantalla completa es DC1 ajustado: la respuesta y la actividad a todo el ancho y plegables, y la actividad contada como historia. Elegidos el 2026-10-20 ([tableros](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#los-tableros)); cambia lo elegido con D2 el 2026-10-17, que era el único detalle;
- copiar definiciones (pendiente en [planes](../README.md)); dar de baja, reactivar, eliminar y deprecar en Meta ya están decididos en [dar-de-baja-formularios.md](dar-de-baja-formularios.md).

La [demo de datos](../formularios-modelo-demo/README.md) es una base aislada de demostración, no una migración.
