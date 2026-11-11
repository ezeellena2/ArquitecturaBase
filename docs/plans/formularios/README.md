# Formularios por WhatsApp: etapas

Estado al `2026-10-23`. Las reglas implementadas están en [formularios](../../features/formularios.md) y [WhatsApp](../../features/whatsapp.md); las pantallas, en el [diseño del front](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md). Los próximos desarrollos conservan abajo su alcance y requieren sus decisiones y tableros antes de programarse.

| Etapa | Resumen | Estado |
| --- | --- | --- |
| 1 · Crear, publicar, mandar y recibir | [formularios](../../features/formularios.md#verificación-de-cierre--2026-10-11) | Cerrada el 2026-10-11 |
| 2 · Catálogos a mano y con filtros | [etapa-2-catalogos.md](etapa-2-catalogos.md) | Cerrada el 2026-10-15, con el piloto real con Meta |
| 2B · Fotos y documentos | [Cierre y reglas implementadas](../../features/formularios.md#cierre-de-la-etapa-2b--2026-10-15) | Cerrada el 2026-10-15, con Meta y Azure privado reales |
| 3 · Planilla, detalle y exportar a Excel | [Reglas y cierre](../../features/formularios.md#planilla-detalle-y-excel--etapa-3) | Cerrada el 2026-10-16, con respuestas reales y apertura del XLSX en Excel |
| 4 · Reglas, aprobación, PDF y avisos | [Reglas y verificación local](../../features/formularios.md#verificación-de-la-etapa-4--2026-10-16) | Verificada localmente y en el [piloto total con Meta y Azure](../verificacion-y-produccion.md#formularios-piloto-total-con-proveedores-reales--2026-10-20), incluidos inglés completo y P24. |
| 5 · Asistente, sugerencias y audios | [Reglas y cierre local](../../features/formularios.md#cierre-local-de-la-etapa-5-y-del-review-de-la-etapa-4) | Implementada y verificada localmente con [calificación OpenAI](../../guides/configurar-ia.md#evidencia-para-habilitar-perfiles); [piloto físico completado con texto, audio, revisión Manual y continuación tras responder](../verificacion-y-produccion.md#formularios-piloto-total-con-proveedores-reales--2026-10-20). La regla de publicación vigente es [original obligatorio y traducción opcional](../../features/formularios.md#idioma-original-y-traducción-opcional--2026-10-19) |

La carga de respuestas desde la web tiene su [propuesta con tableros](../2026-10-29-cargar-formularios-desde-la-app.md), con las decisiones del usuario del 2026-10-29 y el 2026-10-30 y cinco etapas, entre ellas corregir y anular respuestas y los títulos de sección; la Etapa 1, cargar desde la app, está implementada desde el 2026-10-30, la 2 (lo propio de quien sólo carga) y la 4 (las listas dependientes) desde el 2026-11-04, y la 3 (corregir y anular) y la 5 (los títulos de sección) desde el 2026-11-05; la importación de respuestas desde Excel sigue pendiente. La importación de catálogos está implementada en E1.

## Después de la Etapa 5

El cambio de idioma original y traducción opcional (I1–I3) está implementado y verificado localmente; [reglas](../../features/formularios.md#idioma-original-y-traducción-opcional--2026-10-19) y [cierre local](../../features/formularios.md#verificación-local-de-idioma-original-y-traducción-opcional--2026-10-19). El [piloto físico con Meta](../verificacion-y-produccion.md#formularios-idioma-original-y-traducción-opcional) cerró los casos de publicación, traducción completa y respaldo P24 el 2026-10-20. Varios países y más idiomas quedan para después.

MF1, D2 como vista rápida y DC0/DC1 están implementados según los tableros elegidos y la versión 96. Las [reglas vigentes](../../features/formularios.md#gestión-de-formularios-y-detalle-completo--2026-10-21) reemplazan el plan retirado; el [review 1–10](../../reviews/2026-10-21-code-review-menu-y-detalle.md#cierre-local) quedó corregido y verificado localmente, y el [piloto propio](../verificacion-y-produccion.md#formularios-menú-y-detalle-completo) sigue pendiente.

Sin orden fijo; cada una necesita su tablero y su decisión antes de programarse:
- catálogo con una consulta (Query/K5), solo si una lista no puede armarse con filtros ([ADR 0019](../../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md));
- intercambio cifrado con Meta: búsqueda en vivo, listas dependientes y listas largas;
- grupos y remitos con líneas: programados y probados de punta a punta el 2026-11-09 ([el bloque de renglones](../../features/formularios.md#el-bloque-de-renglones-al-armar-y-publicar--rg1-a-rg3-a-y-rg6-a-2026-11-04));
- descargar todas las fotos juntas desde el detalle de una respuesta;
- corregir una respuesta (la corrección reemplaza a la anterior) y anularla con motivo: implementado el 2026-11-05, con sus [reglas](../../features/formularios.md#corregir-y-anular--etapa-3-2026-11-05);
- borrador de formulario armado por la IA a partir de los pedidos de formularios que faltan: decidido el 2026-11-04 en
  [la IA arma formularios y catálogos](../2026-11-04-ia-arma-formularios-y-catalogos.md), con «Armar con la IA» en cada tema;
- **catálogos E1 y E2 implementados:** carga de una vez, tipo de valor, elegir varias, campos extra, presentación y filtros por persona. Las [reglas vigentes](../../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23) y los tableros CA1-A/CA2–CA6 y CE1/CE2/CE3-B/CE4-A/CE5/CE6 sustituyen los resúmenes retirados. El review de E1 está cerrado; el de E2 precede al [piloto único con Meta](../verificacion-y-produccion.md#formularios-piloto-único-con-meta-después-de-e2). Búsqueda en vivo, asistente y orígenes externos conservan su alcance en la [hoja de ruta](../2026-10-21-catalogos-con-origenes-externos.md);
- copiar definiciones (pendiente en [planes](../README.md)); dar de baja, reactivar, eliminar y deprecar en Meta tienen [reglas implementadas](../../features/formularios.md#dar-de-baja-reactivar-y-eliminar--2026-10-21) y su [piloto después del code review](../verificacion-y-produccion.md#formularios-baja-reactivación-y-eliminación).

La [demo de datos](../formularios-modelo-demo/README.md) es una base aislada de demostración, no una migración.
