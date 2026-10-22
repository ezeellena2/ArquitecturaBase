# Formularios por WhatsApp: etapas

Estado al `2026-10-21`. Las reglas implementadas están en [formularios](../../features/formularios.md) y [WhatsApp](../../features/whatsapp.md); las pantallas, en el [diseño del front](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md). Los próximos desarrollos conservan abajo su alcance y requieren sus decisiones y tableros antes de programarse.

| Etapa | Resumen | Estado |
| --- | --- | --- |
| 1 · Crear, publicar, mandar y recibir | [formularios](../../features/formularios.md#verificación-de-cierre--2026-10-11) | Cerrada el 2026-10-11 |
| 2 · Catálogos a mano y con filtros | [etapa-2-catalogos.md](etapa-2-catalogos.md) | Cerrada el 2026-10-15, con el piloto real con Meta |
| 2B · Fotos y documentos | [Cierre y reglas implementadas](../../features/formularios.md#cierre-de-la-etapa-2b--2026-10-15) | Cerrada el 2026-10-15, con Meta y Azure privado reales |
| 3 · Planilla, detalle y exportar a Excel | [Reglas y cierre](../../features/formularios.md#planilla-detalle-y-excel--etapa-3) | Cerrada el 2026-10-16, con respuestas reales y apertura del XLSX en Excel |
| 4 · Reglas, aprobación, PDF y avisos | [Reglas y verificación local](../../features/formularios.md#verificación-de-la-etapa-4--2026-10-16) | Verificada localmente y en el [piloto total con Meta y Azure](../verificacion-y-produccion.md#formularios-piloto-total-con-proveedores-reales--2026-10-20), incluidos inglés completo y P24. |
| 5 · Asistente, sugerencias y audios | [Reglas y cierre local](../../features/formularios.md#cierre-local-de-la-etapa-5-y-del-review-de-la-etapa-4) | Implementada y verificada localmente con [calificación OpenAI](../../guides/configurar-ia.md#evidencia-para-habilitar-perfiles); [piloto físico completado con texto, audio, revisión Manual y continuación tras responder](../verificacion-y-produccion.md#formularios-piloto-total-con-proveedores-reales--2026-10-20). La regla de publicación vigente es [original obligatorio y traducción opcional](../../features/formularios.md#idioma-original-y-traducción-opcional--2026-10-19) |

La carga de respuestas desde la web y la importación desde Excel quedan para después de la Etapa 4, con tableros propios.

## Después de la Etapa 5

El cambio de idioma original y traducción opcional (I1–I3) está implementado y verificado localmente; [reglas](../../features/formularios.md#idioma-original-y-traducción-opcional--2026-10-19) y [cierre local](../../features/formularios.md#verificación-local-de-idioma-original-y-traducción-opcional--2026-10-19). El [piloto físico con Meta](../verificacion-y-produccion.md#formularios-idioma-original-y-traducción-opcional) cerró los casos de publicación, traducción completa y respaldo P24 el 2026-10-20. Varios países y más idiomas quedan para después.

MF1, D2 como vista rápida y DC0/DC1 están implementados según los tableros elegidos y la versión 96. Las [reglas vigentes](../../features/formularios.md#gestión-de-formularios-y-detalle-completo--2026-10-21) reemplazan el plan retirado; el [review 1–10](../../reviews/2026-10-21-code-review-menu-y-detalle.md#cierre-local) quedó corregido y verificado localmente, y el [piloto propio](../verificacion-y-produccion.md#formularios-menú-y-detalle-completo) sigue pendiente.

Sin orden fijo; cada una necesita su tablero y su decisión antes de programarse:
- catálogo con una consulta (Query/K5), solo si una lista no puede armarse con filtros ([ADR 0019](../../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md));
- intercambio cifrado con Meta: búsqueda en vivo, listas dependientes y listas largas;
- grupos y remitos con líneas;
- descargar todas las fotos juntas desde el detalle de una respuesta;
- corregir una respuesta (una respuesta nueva que reemplaza a la anterior);
- borrador de formulario armado por la IA a partir de los pedidos de formularios que faltan;
- **catálogos, en desarrollo:** carga de una vez (pegar, importar y exportar), tipo de valor y elegir varias, decidida con tableros el 2026-10-21 en [catalogos-carga-tipo-y-varias.md](catalogos-carga-tipo-y-varias.md). Menú y detalle está cerrado localmente; E1-0, E1-B y E1-A están implementados con pruebas rápidas. Sigue elegir varias (C1–C4); el cierre conjunto y el piloto siguen pendientes. Después, [campos extra, qué viaja a WhatsApp y filtrar por persona](catalogos-campos-extra.md), también decidida con tableros. Lo demás (búsqueda en vivo, asistente y conector) está en la [hoja de ruta](../2026-10-21-catalogos-con-origenes-externos.md);
- copiar definiciones (pendiente en [planes](../README.md)); dar de baja, reactivar, eliminar y deprecar en Meta tienen [reglas implementadas](../../features/formularios.md#dar-de-baja-reactivar-y-eliminar--2026-10-21) y su [piloto después del code review](../verificacion-y-produccion.md#formularios-baja-reactivación-y-eliminación).

La [demo de datos](../formularios-modelo-demo/README.md) es una base aislada de demostración, no una migración.
