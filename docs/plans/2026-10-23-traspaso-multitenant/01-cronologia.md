# 01 · Qué pasó del 17 al 23 de octubre

Cronología de ArquitecturaBase desde el traspaso anterior. Cada punto remite a su fuente; el detalle técnico está en los anexos.

## Viernes 17

- **Piloto real de aprobación y PDF** con Meta y Azure privado ([formularios](../../features/formularios.md#piloto-de-aprobación-y-pdf--2026-10-17)): aviso, aprobación, rechazo con motivo y PDF por WhatsApp.
- **Primer traspaso al multitenant**, con las Etapas 1 a 4. Se verificó contra `9575269d` en el back y `9261e3d` en el front.

## Sábado 18

- **Piloto del asistente y los audios** con Meta, OpenAI y Azure reales ([formularios](../../features/formularios.md#piloto-del-asistente-y-audios--2026-10-18)):
  - **«Registrar visita» v2**, con el perfil `flow-static-7.3-r8`, recibió texto y audio;
  - la persona confirmó el prellenado y mandó la foto;
  - un pedido sin formulario creó un tema en «Formularios que faltan»;
  - con la cuenta en inglés, formulario, avisos y PDF salieron en inglés.
- **Hueco del piloto.** Si faltaba la traducción solo en el PDF, el formulario igual salía en inglés y los avisos volvían al español. De ahí salió la regla de idioma del 19.
- **Code review de la Etapa 5:** `docs/reviews/2026-10-18-code-review-etapa-5.md`.

## Domingo 19

- **Idioma original y traducción opcional (I1–I3)** ([regla](../../features/formularios.md#idioma-original-y-traducción-opcional--2026-10-19)):
  - el único obligatorio para guardar y publicar es el idioma original del formulario, que no cambia;
  - la traducción es opcional;
  - a Meta se le publica un Flow por cada idioma completo;
  - al mandar, se elige una cultura común completa para todo el recorrido (formulario, apertura, avisos y PDF). Si falta algo del idioma de la cuenta, va todo en el original, sin mezclar;
  - el texto y el detalle de cada opción de catálogo sin traducción usan su original.

  Reemplazó la regla bilingüe anterior. El 23/10 se amplió (ver más abajo).
- **Cierre del review de la Etapa 5** y review del cierre de formularios (`docs/reviews/2026-10-19-code-review-cierre-formularios.md`).

## Lunes 20

- **Piloto total con proveedores reales** ([verificación](../verificacion-y-produccion.md#formularios-piloto-total-con-proveedores-reales--2026-10-20)):
  - sugerencia después de confirmar, de `VISITA-000012` a `PRB1010-000002`;
  - respaldo al idioma original en todo el recorrido;
  - la cuenta configurada en inglés.
- **Error encontrado:** una apertura Manual sin prellenado mandaba `prefill_FORM:{}`, y Meta lo aceptaba pero el formulario no abría. Se corrigió primero con la regresión.

## Martes 21

- **Dar de baja, reactivar y eliminar formularios** ([reglas](../../features/formularios.md#dar-de-baja-reactivar-y-eliminar--2026-10-21)), decidido con el usuario:
  - **las respuestas se conservan siempre;**
  - **eliminar:** solo sin respuestas ni envíos abiertos;
  - **reactivar:** es publicar una versión nueva;
  - **envíos abiertos:** siguen vigentes hasta vencer;
  - **los Flows de Meta se deprecan** al vencer el último envío, como operación durable ([WhatsApp](../../features/whatsapp.md#deprecación-durable-de-flows--2026-10-21)).

  Su review: `docs/reviews/2026-10-21-code-review-dar-de-baja.md`.
- **Menú de formularios y detalle completo** (tableros MF1, D2 y DC0/DC1): una pantalla «Formularios» con el bloque «Por aprobar», la vista rápida de una respuesta y su pantalla completa ([reglas](../../features/formularios.md#gestión-de-formularios-y-detalle-completo--2026-10-21)). Su review: `docs/reviews/2026-10-21-code-review-menu-y-detalle.md`. Un hallazgo grave: un filtro de migración en varias líneas dejaba `\r\n` en el snapshot y Linux iba a ver cambios pendientes.
- **Asistente: espera y aislamiento** ([espera](../../features/whatsapp.md#espera-del-asistente-y-avisos-locales--2026-10-21), [aislamiento](../../features/whatsapp.md#aislamiento-entre-conversaciones--2026-10-21)):
  - **la respuesta tardaba 35 s con texto y 66 s con audio.** Con señales de terminación, sin colas nuevas, bajó a 6,6 s y 7,7 s;
  - **cada conversación tiene su ámbito,** con el índice `(OwnerUserId, ChannelId, ChannelConversationId)`. Tres contactos simultáneos ya no se mezclan.
- **Reorganización de las guías para agentes.** Las cadenas de `AGENTS.md` superaban los 32 KiB que Codex lee desde la raíz, y quedaron por debajo de 30 KB ([regla](../../guides/instrucciones-por-carpeta.md#tamaño-de-las-guías)).
- **Casos del asistente:** plan, tableros AS1-A y AS2–AS6, y el [ADR 0021](../../decisions/0021-respuestas-redactadas-por-el-asistente.md), sobre las respuestas redactadas con límites. Sin programar.
- **Catálogos, la hoja de ruta:**
  - E1: carga de una vez, tipo de valor y elegir varias;
  - E2: campos extra, qué viaja a WhatsApp y filtrar por persona;
  - después: búsqueda en vivo, asistente que arma catálogos y catálogos desde una base externa.

  Hay [hoja de ruta](../2026-10-21-catalogos-con-origenes-externos.md), brief de cada etapa y una spec de 120 KB ([diseño histórico](../../specs/2026-10-21-catalogos-design.md)). Se eligieron los tableros CA1-A, CA2–CA6, CE1, CE2, CE3-B, CE4-A, CE5 y CE6.
- **Conexión a la base de un cliente** (el primer cliente usa Macrosistemas):
  - se descartaron un conector propio y Data Factory porque «es un lío»;
  - la propuesta es una VPN sitio a sitio, un usuario de solo lectura, «Conexiones externas» y un catálogo «Desde una base externa», sin que nadie escriba SQL;
  - queda pendiente.
- **Regla del usuario:** el producto es **totalmente genérico**. Nada específico de un cliente; OCHA era solo un ejemplo.

## Miércoles 22

- **Catálogos E1 programado** por Codex y cerrado localmente ([verificación](../../features/formularios.md#verificación-local-de-catálogos-e1--2026-10-22)):
  - tipo de valor (texto, entero, decimal o fecha);
  - pegar e importar XLSX o CSV con vista previa de altas, cambios, bajas y errores;
  - exportar;
  - elegir varias con `CheckboxGroup` y el perfil r9;
  - tope de 10.000 opciones por catálogo.
- **Review de E1 con 15 puntos** ([review](../../reviews/2026-10-22-code-review-catalogos-e1.md)):
  - importar leía «1.500» como 1,5 en español;
  - una columna opcional ausente borraba el inglés y los detalles;
  - los topes del XLSX no limitaban lo que cargaba ClosedXML;
  - entre otros.

  Se corrigió el mismo día. Un corte de luz interrumpió el cierre y se repitió.
- **Maqueta viva:** el chat de diseño armó un repo con el front real en modo demostración, con un backend falso, selector de persona e idioma y un celular simulado. El usuario quiere navegar, no solo ver tableros.
- **Documentos por WhatsApp,** la parte 1 de tres ([plan](../2026-10-22-documentos-por-whatsapp.md), [ADR 0022](../../decisions/0022-lectura-de-documentos-con-ia.md)):
  - **biblioteca de formularios estándar,** empezando por el comprobante de transferencia;
  - **lectura con IA,** que propone el formulario prellenado con el documento adjunto;
  - **validaciones fijas:** CBU/CVU con la entidad, CUIT/CUIL y duplicados;
  - **bandeja «Documentos recibidos»;**
  - **formulario armado por la IA** desde un documento sin clasificar;
  - **un tablero automático por formulario.**

  Se leen foto, PDF, Word, Excel, ubicación y contacto. Solo se lee lo que mandan cuentas vinculadas, y lo descartado se borra a los 90 días. Se eligieron los tableros DOC5-A (la bandeja como planilla con panel) y DOC7-A (el tablero con tarjetas). Sin programar.
- **Estudio de integraciones,** las partes 2 y 3 ([estudio](../integraciones/estudio-2026-10-22.md)):
  - **las fuentes:** ARCA (WSAA, WSFEv1 y RG 5616), Mercado Libre, Mercado Pago, plantillas de Meta, MCP, API pública y webhooks;
  - **las 13 decisiones de su sección E.4** quedaron pendientes.
- **Orden pedido por el usuario:** después del review de E1, el piloto con Meta y E2. Después decidió hacer primero E2 y probar todo junto.

## Jueves 23

- **Catálogos E2 programado por Codex durante la noche,** en unos 115 commits. Se cerró «en verde»: 6.266 tests del back, 3.116 del front y 719 visuales.
- **Code review de E2** ([review](../../reviews/2026-10-23-code-review-catalogos-e2.md)), con tres revisores en paralelo. Dejó **33 puntos**, 4 de ellos graves:
  - dar de baja una opción de un catálogo fuente bloqueaba editar e importar los catálogos que la usan;
  - un 409 de regla obligaba a descartar todo el borrador;
  - un nombre con la traducción vacía dejaba el catálogo sin poder abrirse;
  - el front tapaba la revisión del servidor al cambiar el tipo.
- **El usuario probó la app y encontró más**, que se sumaron como puntos 34 a 39:
  - **el chat de Inicio no mostraba nunca la respuesta.** El back manda `pending` en minúsculas y el front espera `Pending`, desde por lo menos el 09/10, y no había ejemplos exportados que lo detectaran;
  - **«Guardar» de un catálogo nuevo no hacía nada.** Exigía el nombre y la descripción en inglés, y el error se veía solo en la pestaña de inglés;
  - **los campos aparecían en rojo** apenas se tocaba «Agregar opción»;
  - **los botones tenían textos debajo** («Guardá el catálogo antes», «No hay cambios para guardar», «Revisá los datos marcados») que los desalineaban, más «Cambios sin guardar» y números de revisión al lado de Guardar.
- **Reglas nuevas del usuario** (ver [04](04-reglas-del-usuario.md)): botones sin explicaciones, sin estados de guardado ni revisiones, acciones al borde derecho, validar al intentar guardar, idioma sin selector y «El catálogo» con solo nombre y descripción opcional.
  - **La causa era la guía visual.** Pedía que el botón «explicara el estado» y daba «Cambios sin guardar» como ejemplo; se corrigió.
- **Cambio de programador.** El usuario dijo que «Codex programó bastante mal». Desde ese día programa un chat de Claude, el «Codificador», que corrige los 39 puntos.
- **Este traspaso.**
