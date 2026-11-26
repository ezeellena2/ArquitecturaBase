# Formularios por WhatsApp: etapas

Estado al `2026-11-26`. Las reglas implementadas están en [formularios](../../features/formularios.md) y [WhatsApp](../../features/whatsapp.md); las pantallas, en el [diseño del front](../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md). Los próximos desarrollos conservan abajo su alcance y requieren sus decisiones y tableros antes de programarse.

| Etapa | Resumen | Estado |
| --- | --- | --- |
| 1 · Crear, publicar, mandar y recibir | [formularios](../../features/formularios.md#verificación-de-cierre--2026-10-11) | Cerrada el 2026-10-11 |
| 2 · Catálogos a mano y con filtros | [Reglas y cierre](../../features/formularios.md#catálogos-al-enviar-y-responder--k7-2026-10-12) | Cerrada el 2026-10-15, con el [piloto real con Meta](../../features/formularios.md#preparación-de-catálogos-cierre-del-review-k6k7--2026-10-15). El resumen se retiró el 2026-11-18 ([procedencia](../../history/README.md#barrido-documental-del-2026-11-18)) |
| 2B · Fotos y documentos | [Cierre y reglas implementadas](../../features/formularios.md#cierre-de-la-etapa-2b--2026-10-15) | Cerrada el 2026-10-15, con Meta y Azure privado reales |
| 3 · Planilla, detalle y exportar a Excel | [Reglas y cierre](../../features/formularios.md#planilla-detalle-y-excel--etapa-3) | Cerrada el 2026-10-16, con respuestas reales y apertura del XLSX en Excel |
| 4 · Reglas, aprobación, PDF y avisos | [Reglas y verificación local](../../features/formularios.md#verificación-de-la-etapa-4--2026-10-16) | Verificada localmente y en el [piloto total con Meta y Azure](../verificacion-y-produccion.md#formularios-piloto-total-con-proveedores-reales--2026-10-20), incluidos inglés completo y P24. |
| 5 · Asistente, sugerencias y audios | [Reglas y cierre local](../../features/formularios.md#cierre-local-de-la-etapa-5-y-del-review-de-la-etapa-4) | Implementada y verificada localmente con [calificación OpenAI](../../guides/configurar-ia.md#evidencia-para-habilitar-perfiles); [piloto físico completado con texto, audio, revisión Manual y continuación tras responder](../verificacion-y-produccion.md#formularios-piloto-total-con-proveedores-reales--2026-10-20). La regla de publicación vigente es [original obligatorio y traducción opcional](../../features/formularios.md#idioma-original-y-traducción-opcional--2026-10-19) |

## Programado después de la Etapa 5

Cada desarrollo tiene sus reglas en [formularios](../../features/formularios.md); sus planes se retiraron el 2026-11-26 ([procedencia](../../history/README.md#cierre-del-2026-11-26)) y lo que les queda a mano está en [verificación y producción](../verificacion-y-produccion.md#pruebas-a-mano-de-formularios-el-criterio):

- **La carga desde la app** ([reglas](../../features/formularios.md#carga-desde-la-app--etapa-1-2026-10-30)): las cinco etapas, del 2026-10-30 al 2026-11-05: cargar, lo propio de quien sólo carga, corregir y anular, listas dependientes y títulos de sección.
- **Carpetas** ([reglas](../../features/formularios.md#carpetas--cp1-a-a-cp5-a-2026-11-09)), **elegir primero por un campo** ([reglas](../../features/formularios.md#elegir-primero-por-un-campo--ld1-a-2026-11-07)), **el Excel en una hoja** ([reglas](../../features/formularios.md#el-bloque-de-renglones-en-la-lectura--rg4-a-2026-11-07)) y **formularios sólo en la app** ([reglas](../../features/formularios.md#formularios-sólo-en-la-app--sa1-a-a-sa5-2026-11-09)), del 2026-11-07 al 2026-11-11.
- **Grupos y remitos con líneas:** programados y probados de punta a punta el 2026-11-09 ([el bloque de renglones](../../features/formularios.md#el-bloque-de-renglones-al-armar-y-publicar--rg1-a-rg3-a-y-rg6-a-2026-11-04)).
- **Idioma original y traducción opcional** (I1–I3): [reglas](../../features/formularios.md#idioma-original-y-traducción-opcional--2026-10-19) y [cierre local](../../features/formularios.md#verificación-local-de-idioma-original-y-traducción-opcional--2026-10-19); el [piloto físico con Meta](../verificacion-y-produccion.md#formularios-idioma-original-y-traducción-opcional) cerró el 2026-10-20.
- **MF1, D2 como vista rápida y DC0/DC1:** [reglas vigentes](../../features/formularios.md#gestión-de-formularios-y-detalle-completo--2026-10-21); el [review 1–10](../../reviews/2026-10-21-code-review-menu-y-detalle.md#cierre-local) quedó corregido y el [piloto propio](../verificacion-y-produccion.md#formularios-menú-y-detalle-completo) sigue pendiente.
- **Catálogos E1 y E2:** carga de una vez, tipo de valor, elegir varias, campos extra, presentación y filtros por persona ([reglas](../../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23)). El review de E2 precede al [piloto único con Meta](../verificacion-y-produccion.md#formularios-piloto-único-con-meta-después-de-e2). Búsqueda en vivo y orígenes externos conservan su alcance en la [hoja de ruta](../2026-10-21-catalogos-con-origenes-externos.md), donde los catálogos `External` ya tienen su copia local; el asistente que arma catálogos está programado con [la IA arma formularios y catálogos](../../features/formularios.md#la-ia-arma-formularios-y-catálogos--etapa-1-2026-11-04).
- **Dar de baja, reactivar, eliminar y deprecar en Meta:** [reglas](../../features/formularios.md#dar-de-baja-reactivar-y-eliminar--2026-10-21) y su [piloto después del code review](../verificacion-y-produccion.md#formularios-baja-reactivación-y-eliminación).
- **El borrador armado por la IA:** decidido el 2026-11-04, con «Armar con la IA» en cada tema; sus tres etapas (texto y audio, planillas y documentos) están programadas, con las reglas en [formularios](../../features/formularios.md#la-ia-arma-formularios-y-catálogos--etapa-1-2026-11-04).

## Después de la Etapa 5

Sin orden fijo; cada una necesita su tablero y su decisión antes de programarse:
- catálogo con una consulta (Query/K5), solo si una lista no puede armarse con filtros ([ADR 0019](../../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md));
- intercambio cifrado con Meta: búsqueda en vivo, listas dependientes y listas largas;
- las listas dependientes por WhatsApp sin intercambio, con el `update_data` de LD2-A, cuando las dos preguntas van en la misma pantalla («Para después» del diseño de [elegir primero por un campo](../../../../ArquitecturaBaseFront/docs/design/carga-desde-la-app.md#elegir-primero-por-un-campo-ld1-a-a-ld3-a--2026-11-07));
- importar respuestas desde un Excel (fuera de alcance de la carga desde la app);
- descargar todas las fotos juntas desde el detalle de una respuesta;
- llamar a otro formulario, extendiendo «Qué sugerir después» con «Se carga sola», «Preguntar siempre» en la app y fotos y documentos en «Datos que pasan»: planeado el 2026-11-12 como la etapa 1b de los [tickets en Jira](../integraciones/estudio-2026-10-22.md#g-tickets-en-jira-desde-formularios), sin programar y sin depender de Jira;
- copiar definiciones (pendiente en [planes](../README.md));
- la IA propone «Renglones» al armar (decidido el 2026-11-04): una `propose-form@3` con el bloque y sus preguntas `repeated`, validada como RG1-A y RG6-A, registrada un despliegue antes de ofrecerla, con la tarjeta, el resumen de WhatsApp y «Crear borrador» con el bloque. Hoy la IA arma esas columnas como preguntas sueltas. Falta el visto bueno para la corrida real ([I-1](../../reviews/2026-11-18-barrido-documental.md#i-1-la-ia-propone-renglones-propose-form3));
- qué tipos nuevos del editor propone la IA (la pregunta «Ubicación», los títulos de sección, elegir primero por un campo y los demás del barrido): sin decidir. Cada uno es una versión nueva con su calificación, que puede ir junto con Renglones ([I-4](../../reviews/2026-11-18-barrido-documental.md#i-4-lo-que-la-ia-no-propone-de-los-tipos-nuevos-del-editor));
- R3, heredado de la Etapa 1 y por confirmar con WhatsApp: que la tarjeta del chat muestre la versión y el idioma de la respuesta y una señal explícita de que el mensaje se purgó, sin inferirla de `Body=null`. Se cierra cuando el usuario confirma con el tablero R3 si sigue haciendo falta; si sí, el dato va en la lectura del chat, con su texto y su test;
- varios países y más idiomas.

Decididos para después, sin tablero: cargar en nombre de otra persona, ofrecer en la app «qué cargar después», borradores guardados y carga sin conexión.
