# Verificaciones y preparación de producción pendientes

Estado: **abierto**. Reconciliado el 2026-10-02 con el código, la documentación y las revisiones locales; lo del núcleo y lo
que quedó fuera de los arreglos de Formularios, otra vez el 2026-11-18.
Conserva información pendiente de los planes retirados; no autoriza un despliegue ni nuevas
funciones. Las pruebas automáticas y capturas con datos simulados no cierran los recorridos
manuales con proveedores reales. La [procedencia](../history/README.md) permite consultar los originales.

## Verificación manual con la aplicación real

La [revisión funcional del 2026-10-01](../reviews/2026-10-01-revision-funcional.md) y su
[consolidado](../reviews/2026-10-01/resultados.json) acreditan recorridos con SPA/API reales
en HTTPS y PostgreSQL/Redis aislados: correo local, OIDC, perfil, usuarios, roles, permisos,
configuración y recuperación HTTP. P2/P3 se corrigieron con regresiones posteriores.
No fue una prueba de Google/SMTP/Meta externos ni del refactor de WhatsApp del día siguiente.
La [validación del 2026-10-02](../reviews/2026-10-02-separacion-acceso-whatsapp-validacion.md)
acredita el refactor con Meta simulado; no reemplaza la prueba con teléfono.

- [ ] Refactor acceso/atención del 2026-10-02: recepción con/sin cuenta sin efectos de identidad,
  respuesta manual, código en Manual y nueva invitación + WANT_TO_ENTER histórico → `/login` →
  código. Comprobar payload real de la plantilla y receipts con el titular del teléfono.
  Usar [la guía local](../guides/whatsapp-en-local.md#probar-acceso-y-atención-separados--2026-10-02).
  Los tests automáticos con Meta simulado no cierran esta prueba.

- [ ] Completar los recorridos con proveedores externos: Google real, SMTP real e invitaciones
  y verificar/vincular/desvincular teléfono con Meta, además de los casos del refactor anterior.
  No volver a declarar ausentes los recorridos locales acreditados el 2026-10-01. Registrar
  entorno, versión, resultados y límites; una suite automática no prueba al proveedor real.
- [ ] Comprobar el arranque real con una base nueva aislada, migraciones, seed e ingreso del
  administrador inicial. Las pruebas automáticas de bases vacías y el bundle ya pasaron;
  queda la experiencia de la aplicación levantada, sin borrar la base habitual.
- [ ] Completar en roles los caminos de salida con cambios todavía no individualizados en la
  evidencia (migas, menú, Atrás y recarga) y los escenarios responsive/teclado pendientes.
  Alta/edición, Admin protegido, User inmutable, rol inexistente, rechazos de permisos y
  Cancelar/volver tienen evidencia con API real en la revisión del 2026-10-01.

Si se inicia Aspire, terminar con `aspire stop`. No enviar mensajes de prueba a terceros sin
la autorización correspondiente al recorrido.

## Formularios: piloto de aprobación y PDF

- [x] Repetir con Meta la visita, aprobación por otra cuenta y PDF con foto: el 2026-10-18 se abrieron en el celular el PDF inglés con traducción completa y el PDF español al faltar una traducción usada; los avisos respetaron el mismo idioma y las acciones quedaron en Hecho.

El [recorrido real en español con foto](../features/formularios.md#piloto-de-aprobación-y-pdf--2026-10-17) quedó verificado el 2026-10-17 con Meta y Azure privado reales. El [piloto del 2026-10-18](../features/formularios.md#piloto-del-asistente-y-audios--2026-10-18) agrega la evidencia física en inglés y del respaldo al español del PDF y los avisos. Chrome conserva el alcance local de visor y descarga del [cierre](../features/formularios.md#cierre-local-de-la-etapa-5-y-del-review-de-la-etapa-4); Edge no se recorrió.

## Formularios: piloto del asistente y audios

El 2026-10-18 se republicó **Registrar visita**, versión 2, con `flow-static-7.3-r8`, «Cuándo lo ofrece el asistente» y foto obligatoria. Se usaron Aspire con túnel, Meta, OpenAI y Azure privado reales; el [resultado](../features/formularios.md#piloto-del-asistente-y-audios--2026-10-18) conserva versiones y límites.

- [x] Texto y audio: formulario prellenado confirmado por la persona, foto guardada, motivo visible y transcripción completa en español en el chat (`VISITA-000002/000003`).
- [x] Pedido sin formulario: aviso recibido y tema agrupado en Formularios que faltan, sin datos personales; sugerencia manual revisada y enviada por la persona (`VISITA-000004`).
- [x] Sugerencia tras confirmar una respuesta: el 2026-10-20 se confirmó `VISITA-000012` → `PRB1010-000002`, con «Preguntar siempre», revisión Manual y ambos datos mapeados conservados. Ver el [piloto total](#formularios-piloto-total-con-proveedores-reales--2026-10-20).
- [x] Cuenta en inglés y traducción completa: formulario, avisos y PDF con foto en inglés (`VISITA-000005`).
- [x] Respaldo de todo el recorrido al idioma original: la repetición física del 2026-10-20 cerró P24 con faltantes separados de aviso y PDF (`VISITA-000011/000012`), incluida la apertura y el formulario. Conserva como antecedente el fallo del 2026-10-18: con sólo el nombre inglés del PDF vacío, avisos y PDF salían en español, pero formulario y apertura seguían en inglés (`VISITA-000006`). Ver el [piloto de idioma original y traducción opcional](#formularios-idioma-original-y-traducción-opcional).

La [calificación OpenAI y su reevaluación de canales](../guides/configurar-ia.md#evidencia-para-habilitar-perfiles) son evidencia separada. No se corrigió código durante el piloto. Se restauraron el idioma español de la cuenta, el título inglés del PDF y el modo original Manual; Aspire y el túnel se apagaron con `aspire stop`.

## Formularios: idioma original y traducción opcional

La [revisión local del cierre](../reviews/2026-10-19-code-review-cierre-formularios.md) incorpora regresiones y un recorrido de navegador con proveedores controlados.
La verificación completa y los contratos HTTP exportados quedan registrados allí; este trabajo no acredita un piloto físico con Meta ni una nueva calificación LLM.

Piloto físico del 2026-10-20 con Meta y la misma cuenta configurada en inglés:

- [x] Original español solo, versión 3: apertura, formulario, avisos y PDF con foto en español (`VISITA-000007`).
- [x] Inglés completo, versión 4: apertura, formulario, avisos y PDF con foto en inglés (`VISITA-000010`).
- [x] P24 con versión 4 completa: faltó sólo el inglés del aviso de recepción, reglas r3 (`VISITA-000011`); después se restauraron los avisos, reglas r4, y faltó sólo el título inglés del PDF, diseño r5 (`VISITA-000012`). En ambos casos apertura, formulario, avisos y PDF con foto salieron enteros en español y se abrieron en el celular.
- [x] Sin inglés en texto/detalle del ítem «Seguimiento», catálogo r2: conservó «Seguimiento» / «Seguimiento de una visita»; formulario, avisos y PDF con foto mantuvieron el inglés (`VISITA-000013`, diseño PDF completo r6).

Registrar las versiones y resultados reales. Conservar las sesiones, bindings, perfiles, presentaciones y copias fijas anteriores; restaurar los idiomas y el modo de conversación utilizados para la prueba y terminar con `aspire stop`. No requiere repetir la calificación OpenAI.

## Formularios: piloto total con proveedores reales — 2026-10-20

Se usaron Aspire con túnel, Meta, OpenAI y Azure Blob privado reales, sobre backend `a7cea3f2` y front `44e464b`. El titular recorrió Chrome y el celular; la preparación y el contraste de estados se hicieron por la API autenticada porque falló el control del navegador. No se cambió código de producto ni se repitió la calificación de IA.

| Recorrido | Resultado comprobado |
| --- | --- |
| Configuración y publicación | Registrar visita v3, sólo español, con catálogo Manual, foto y documento PDF obligatorios; recepción con aviso y aprobación por rol, aprobación con PDF que incluye fotos y envío al autor. V4 agregó el inglés completo. |
| Texto en Asistente | `VISITA-000007`: lugar y tipo prellenados, foto y documento enviados, aviso «Recibimos…» recibido y motivo del envío visible. |
| Aprobación y PDF | Otra cuenta con permiso de decidir aprobó la visita; el titular recibió y abrió el PDF con la foto. `VISITA-000007` a `000013` quedaron aprobadas con las cuatro acciones en Hecho. |
| Planilla y D2 | Confirmados panel lateral, catálogo/documento, Fotos, PDF, motivo, momentos y actividad; ↑/↓, filtro Aprobada y Excel abierto con sólo las aprobadas. |
| Audio | `VISITA-000008`: transcripción visible en el chat, formulario prellenado confirmado con foto y documento; aprobación y PDF generados. |
| Formulario inexistente | Aviso recibido y pedido visible en Formularios que faltan; «Préstamo de herramientas» agrupó el nuevo pedido y pasó de 1 a 2. |
| Qué sugerir después | `VISITA-000012` → `PRB1010-000002`: revisión y envío de Bilingual form test v3, apertura y confirmación en el celular; «Central workshop» y «VISITA-000012» quedaron mapeados sin cambios y la cadena conserva ambas respuestas. Se probó «Preguntar siempre» en Manual. |
| Revisión Manual | `VISITA-000009`: sugerencia del asistente revisada por el operador con «Revisar y enviar»; abrió prellenada y se confirmó con foto y documento. |
| Idioma | Original solo, traducción completa, los dos faltantes P24 por separado y excepción individual del catálogo comprobados en el celular, como se detalla arriba. |

- [x] Error de apertura Manual sin prellenado corregido después del piloto, el 2026-10-20. Los dos envíos fallidos v3/es se conservaron como antecedente; la reproducción y el arreglo sobre la misma v5 están registrados abajo.

El primer intento de continuación apuntó al destino v2 anterior y fue rechazado como NotRepresentable antes de crear una salida. Se republicó el mismo contenido de Bilingual form test como v3 con `flow-static-7.3-r8`; el recorrido nuevo sí terminó en `PRB1010-000002`, sin modificar propuestas anteriores.

Cierre verificado: el contenido original de Registrar visita se restauró y publicó como v5 es/en; reglas originales en r4, diseño PDF original de dos preguntas en r7 y sugerencias vacías en r2. Bilingual form test conserva su contenido original, publicado como v3. Se restauraron las traducciones del catálogo nuevo y se lo desactivó (r3), conservando respuestas, archivos, versiones y copias PDF históricas. Cuenta de nuevo en español y chat en su modo original Manual. Se eliminó la sesión temporal de preparación y se ejecutó `aspire stop`: Api y front dejaron de escuchar; PostgreSQL persistente se conservó. Sin push.

### Corrección de apertura Manual — 2026-10-20

Se reprodujo en el Android del titular con Registrar visita v5/es, sólo lugar y foto: el envío Manual vacío fue aceptado por Meta pero falló al abrir; otro envío de esa misma publicación con el lugar prellenado abrió. El transporte mandaba el objeto vacío `prefill_FORM:{}`. Se agregó primero la regresión del despacho Manual y de una propuesta sin campos incluidos; después se omitieron los mapas sin valores, conservando los mapas parciales y poblados. El envío Manual nuevo de la misma v5 abrió vacío en Android, sin republicar ni cambiar JSON, hash, binding o codec. No se completaron ni enviaron respuestas durante esta comparación.

Se conservaron definición v5, cuenta en español y chat en Manual. La sesión temporal de preparación se eliminó y se terminó con `aspire stop`. Las capacidades, esquemas e instrucciones de IA no cambiaron; no se repitió su calificación.

Verificación final: regresiones del despacho en rojo antes del arreglo; 649 tests rápidos de WhatsApp y 6 del transporte en verde. `dotnet test` completo con PostgreSQL y Redis en Docker: 4.911 aprobados, sin omitidos. `dotnet build ArquitecturaBase.slnx`: cero advertencias y errores. Corrección en `4dc36cba`, en main y sin push.

## Formularios: menú y detalle completo

El [review 1–10](../reviews/2026-10-21-code-review-menu-y-detalle.md#cierre-local) quedó corregido y verificado localmente: Docker **5.389/5.389**, front **2.689/2.689**, visual **623/623**, builds limpios y recorrido es/en a 390/320. El piloto propio sigue pendiente; no se usó Meta durante el desarrollo. Las reglas y el alcance vigente se consolidan en [formularios](../features/formularios.md#gestión-de-formularios-y-detalle-completo--2026-10-21).

Los recorridos pendientes de MF1, D2/DC1, la cuenta que sólo decide y los contactos simultáneos se reúnen en el [piloto único posterior a E2](#formularios-piloto-único-con-meta-después-de-e2). La evidencia local anterior se conserva; no acredita esa comprobación física.

## Propuesta heredada por confirmar

La administración de dispositivos y sesiones (listar sesiones activas y cerrarlas a distancia)
es una propuesta heredada del diseño inicial y del diseño de Administración retirado.
No está implementada ni tiene una entrega actual acordada: antes de retomarla, confirmar
su alcance y diseñar el manejo de autorizaciones/tokens de OpenIddict. Esto es distinto de
la revocación de todas las sesiones al desactivar o borrar una cuenta, que ya existe.

## Adopción visual y mensajes

- [ ] Completar la [unificación transversal de mensajes](../../../ArquitecturaBaseFront/docs/plans/2026-09-30-unificacion-mensajes.md).
  Configuración y el mecanismo común ya existen; quedan otros consumidores, 429 centralizado,
  recuperación y límites de imports. El adaptador sin props de apariencia y su montaje único
  ya están resueltos; no se vuelven a abrir como pendientes.
- [ ] Sincronizar biblioteca y lienzo externos según el [fundamento visual](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), incluidos los tableros de Inicio (2026-10-04) y del panel (2026-10-05).
  Incluye las piezas de WhatsApp que quedaron atrás, los tokens Campo y roles de Apariencia, filtros, layout y editor
  de roles; corregir el texto de invitación y notas antiguas del lienzo. Esta auditoría no accedió a esos artefactos.
- [ ] Completar los tableros responsive y estados que todavía enumera el fundamento. Roles,
  Configuración y acceso público ya tienen evidencia local a 320 px; no declarar todos los
  tableros móviles ausentes ni todos los recorridos verificados.
- [ ] Revisar con teclado el desplazamiento de `main` al entrar desde el menú o con foco en
  `body` (Re Pág, Av Pág, Espacio y flechas), pendiente heredado del editor de roles.
  Definir una solución con el diseño antes de cambiar foco o caparazón.
- [ ] Completar la auditoría de accesibilidad móvil y objetivos táctiles del fundamento visual.
  `Button`/`IconButton` compactos conservan 30/26 px y `Page.backTo` una caja de 24 px;
  las capturas desde 320 px no demuestran blancos de 44 × 44 px en todas las acciones.
  Acceso público y los checks móviles del selector de permisos sí tienen tamaños ampliados.
- [ ] Evaluar enlaces reales para Ver/Editar en `RowActions`, para abrir en otra pestaña o copiar
  destino. El separador de acciones y el tema claro ya se corrigieron; no son trabajo pendiente.
- [ ] Confirmar con el diseño las decisiones heredadas por omisión: rol User preseleccionado,
  invitación inicialmente marcada, verificación de medios y formato visible del número.
  Selección masiva, marca y pantallas nuevas requieren su propio contrato y elección.

## Despliegue y operación

- [ ] Ejecutar los dos disparadores y validar el primer despliegue real con la [guía Azure dev](../deploy/azure-dev.md).
  La preparación local de los workflows no acredita Actions, login ni arranque en Azure.
- [ ] Completar la [lista antes del primer despliegue](../guides/despliegue.md#lista-antes-del-primer-despliegue),
  con Redis privado/TLS, prefijos por instalación, secretos, issuer, redirects y certificados.
- [ ] Planificar WhatsApp en producción: el piloto de formularios del 2026-10-11 ya usó una cuenta
  configurada, con pago y número reales. Falta el número de producción y su PIN, las plantillas en su
  cuenta, HTTPS/webhook propio, secreto de la app requerido, monitoreo del token y del costo, y el
  control de cambio de identidad (un número reciclado llega con otro BSUID y no traslada la cuenta).

## Núcleo: lo que quedó fuera de los arreglos del 2026-11-14

Las 31 tareas de los arreglos del núcleo están programadas (15 y 16/11). Esto quedó
afuera por decisión del usuario del 2026-11-14 y sigue anotado, con su evidencia, en la
[revisión del núcleo](../reviews/2026-11-14-arquitectura-nucleo.md). Contrastado con el código el 2026-11-18.

- [ ] **Antes de pasar de una réplica:** la lease de la limpieza de archivos (N28). Hoy cada copia recorre todo el contenedor;
  el borrado con el ETag del listado ya está (9be27c20). Se cierra con la lease y un test de dos hosts.
- [ ] **Con volumen real que medir:** un solo lock y un correo por vez en la cola (N29); listar sólo lo pendiente en la
  limpieza (N28); el índice y los trigramas de la búsqueda de usuarios (N30); los conteos de personas en cada lectura de roles
  (N32); y un control que impida crear índices que bloquean tablas grandes en una migración (N34: el renombre y el borrado
  ya los frena `MigrationCoexistenceTests`). Se cierran con `EXPLAIN ANALYZE` sobre datos de ese tamaño.
- [ ] **Pruebas reales:** carga del pedido de código y de `/health` contra el plan de la base (N6 y N29); listado, conteos y
  catálogo de roles con un millón de cuentas (N30 a N32); el primer despliegue con los chequeos de Container Apps,
  `AllowedHosts`, `ForwardedHeaders`, `Caching:KeyPrefix`, el pool y la consola JSON (N6, N10, N17 y N19); y restaurar base
  y contenedor juntos (N15, abajo en IA).
- [ ] **Observaciones menores todavía abiertas:** O1 (los 429 de negocio no mandan el encabezado `Retry-After`; el front lee
  el cuerpo), O2 (dos versiones con redirect URIs distintas se pisan el cliente `web` al reiniciar), O5 (la reserva borra
  todos los vencidos sin tope), O7 (un diferido que choca al confirmar da 500, aunque `IUnitOfWork` promete traducirlo),
  O10 (`LoginAudit`, `LoginCode` y `StoredFile` no dicen por qué no tienen FK a la cuenta) y lo que queda de O8, los
  comentarios viejos de `LoginCodeService.cs:39-41`, `AccountCreationPolicy.cs:11-12`, `RoleRow.cs:3` y
  `EmailOptions.cs:27-30`. Se arreglan al tocar esos archivos.
- [ ] **Limpieza de lo retirado (B4):** la tabla `LoginLinks` la vacía la retención; después se pueden ir la tabla, la
  entidad, lo de los enlaces en `ILoginLinkRepository` y la invalidación de los pendientes
  (`PhoneNumberLinker.VoidPendingLinksAsync`), que ya no cambia nada. Se queda el lock por cuenta (`LockAccountAsync`,
  `login-link:`) con el mismo texto, porque el texto es el lock ([backend](../architecture/backend.md#locks)).

## Formularios: lo que quedó fuera de los arreglos del 2026-11-14

Las 41 tareas de los arreglos de Formularios se programaron el 15 y el 16/11, y la marca de F2, el 17/11. La corrida completa
del cierre dio verde el 17/11 sobre `02c60e91` (11.656/11.656; el detalle está en `4b3a292b`). La
[revisión de Formularios](../reviews/2026-11-14-arquitectura-formularios.md) dice qué commit arregló cada hallazgo y qué se hizo
distinto de lo previsto. Esto quedó afuera. Contrastado con el código el 2026-11-18.

- [ ] **B2, para el núcleo.** Una cuenta borrada conserva los permisos de sus roles: `PermissionReader.cs:14-19` lee
  `UserRoles` sin mirar si la cuenta está borrada, y el borrado lógico deja los roles. El test en rojo
  (`A_deleted_account_keeps_no_permissions_of_its_roles`) está en `.artifacts/pruebas/nucleo-B2-DeleteUserEndpointTests.cs`,
  fuera de Git. Se cierra con ese test commiteado y un lector que no le conceda nada a una cuenta borrada o inactiva.
- [ ] **Menores de la segunda revisión adversarial (2026-11-16), sin decisión nueva:**
  - F4: quien corrigió una versión anterior puede aprobar. `FormApprovalReader.cs:21-23` mira sólo el `CorrectedByUserId` de
    la respuesta de la tarea y el `ReviewedByUserId` del documento que apunta a ella, sin recorrer `ReplacesRecordId`.
  - B1, el toque viejo: `NoLongerIssuable` le contesta a cualquier botón viejo de una respuesta anulada sin `DecisionTapId`,
    también a una ya emitida y revertida, con un texto un poco inexacto (`FormIssueConfirmationService.cs:45-48`).
  - F22: el canal de WhatsApp compara códigos `Forms.*` literales, y cualquier otro `Forbidden` dice «tu cuenta no puede
    armar» (`WhatsAppAssistantAuthoringPolicy.cs:49-50`). Los módulos se cruzan por puertos neutrales: falta un motivo
    neutral en el puerto.
- [ ] **Menores que decide el usuario.** Los de migraciones y versiones (2b-1, 2b-2, 2b-5 y `NotApplicable`) conviene
  cerrarlos antes del primer despliegue. Lo que se corrija lleva su test; lo que se acepte, una línea en la regla.
  - F2, la auto-consulta de la primera pasada: si `channel.GetAsync` falla sin nada enviado, la reconsulta no encola y el
    `NotFound` termina «destinatario no disponible», que se reintenta a mano (`FormActionEffectService.cs:86-100`). Reenviar,
    porque no hubo efecto, o dejarla «En duda».
  - 2b-1 (F15): sólo la migración y la captura escriben `BusinessFormRecordCatalogFields`. Un campo que selló un nodo viejo
    durante el despliegue no entra hasta otra captura. Un relleno idempotente en el despliegue siguiente, o aceptarlo por escrito.
  - 2b-2 (F15): `20261116111033_AddFormRecordCatalogFields.cs:60-69` inserta `->> 'key'` y `-> 'name'` en columnas NOT NULL,
    sin COALESCE ni filtro. `appdb` no tiene casos (85 campos, 16/11). Corregir el SQL antes del primer despliegue, o dejarlo
    verificado.
  - 2b-3 (F31), heredado sin comprobar: un «no encontrado» del motor deja la extracción en `pending` hasta siete días, y quien
    revisa ve «leyendo». Un plazo menor, si molesta.
  - 2b-5: `20261116114811_FormActionRunStallNotice.cs:20` marca como avisados los runs fallidos o en duda que ya había. Lo que
    un nodo viejo deje fallido después lo anuncia el nuevo. Es esperable: falta aceptarlo por escrito.
  - 2c-6 (F47): el tope de 500 carpetas se aplica sólo al crear una (`FormFolderTree.cs:22`); las del sistema que crea
    «Agregar» no lo cuentan.
  - La convivencia de `NotApplicable`, heredado sin comprobar: una copia vieja no toma ese valor por terminal en el reintento
    de un aviso a un rol y encola a quien se fue del rol. Aceptarlo ([ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md))
    o sellar el estado en el run.
  - La tarea 10, el PDF: un snapshot sin `TimeZoneId` sale en UTC (`SkiaFormPdfRenderer.cs:507-508`), y un `WriteOnce` con un
    `Uncertain` pendiente podría ver otra hora que el original. Sin producción, sólo toca datos de desarrollo.
  - F6-4: el escáner de PDF no ve variantes raras de la sintaxis de `stream` ni un `/Filter` por referencia (anotado en F6).
- **Aceptado por diseño:** 2c-5 (F11). El tope de dos exportaciones o importaciones a la vez por copia lo pueden ocupar dos
  pedidos lentos de una persona (`BusinessFormsApiRegistration.cs:20-23`). Ese `GlobalLimiter` es el único de la app: si otro
  módulo asignara uno, lo reemplazaría.
- [ ] **Antes de pasar de una réplica:** la reserva de una acción que no se renueva (F32). Se cierra con la renovación y un
  test de dos hosts.
- [ ] **Con volumen real que medir:** lo demás de F33, F34 a F36, F39, F41 a F46, el aviso de «hay más» de F48 y escribir el
  XLSX por partes (F11). Los escenarios están en la revisión.
- [ ] **Pruebas reales:** las de la [revisión](../reviews/2026-11-14-arquitectura-formularios.md#controles-y-pruebas-que-conviene-sumar):
  millones de filas con `EXPLAIN ANALYZE`, exportaciones y cargas a la vez midiendo la memoria, los encabezados de uso de
  Meta y un despliegue gradual con dos versiones.

### Pruebas a mano de Formularios: el criterio

Van en la prueba integral; lo que quede abierto después se anota acá.

- [ ] **Carpetas** ([regla](../features/formularios.md#carpetas--cp1-a-a-cp5-a-2026-11-09)): crear una carpeta y una
  subcarpeta; poner un formulario y un catálogo en dos carpetas y verlos una sola vez en «Todos»; mover una carpeta y ver que
  no pasa del quinto nivel; borrar una con subcarpetas y comprobar que los elementos quedan en sus otras carpetas o en «Sin
  carpeta»; poner una carpeta en un menú propio y otra en «Administración»; entrar como alguien que sólo carga y ver sólo las
  carpetas y los formularios que puede abrir, y que el menú le abre la carga.
- [ ] **Sólo en la app** ([regla](../features/formularios.md#formularios-sólo-en-la-app--sa1-a-a-sa5-2026-11-09)): crear un
  formulario de la app, publicarlo, cargarlo, pasarlo a WhatsApp con Meta y volverlo a la app. Mirar «Publicamos la versión
  N.» sin pasar por «Publicación aceptada», la columna «Dónde se completa» en el listado, que «Enviar formulario» no lo lista,
  el aviso «Deja de completarse por WhatsApp…» al volver a la app y que un envío abierto de la versión anterior se sigue
  respondiendo.
- [ ] **Acciones por resolver, F2** ([regla](../features/formularios.md#motor-de-acciones-y-aprobación--etapa-4-g5-y-g3g4)):
  con una regla cuya acción falla sin reintento (por ejemplo, «Pedir aprobación» a un rol sin miembros), la columna «Reglas»
  dice «Por resolver» para todo el que ve la planilla, y quien administra reglas y ve todas las respuestas tiene la pestaña
  «Por resolver (N)», que filtra y cuenta respuestas. A quien cargó le llega un aviso, una sola vez. Al anular o corregir la
  respuesta, deja de contar.
- [ ] **Revisar un tema de «Formularios que faltan», F7** ([regla](../features/formularios.md#formularios-que-faltan--etapa-5)):
  pedir por WhatsApp un formulario que no existe; el tema nuevo aparece sin revisar, con «Marcar como revisado». Hasta
  revisarlo, el asistente no se lo ofrece a otra persona; después, sí, sólo con el nombre. Un tema descartado que otro pedido
  reabre vuelve a quedar sin revisar.
- [ ] **Confirmar un documento repetido, F13** ([regla](../features/formularios.md#la-bandeja-documentos-recibidos--doc5-a-2026-11-12)):
  un comprobante que repite una respuesta guardada (la misma operación, monto y fecha) y quedó «para revisar acá». Al pasarlo
  desde la bandeja, la app avisa que repite la guardada, con su número; sin confirmar no se guarda nada, y al confirmar pasa
  igual.
- [ ] **El teléfono, heredado de la Etapa 2 de catálogos** ([verificación de cierre](../features/formularios.md#verificación-de-cierre--2026-10-11)):
  los máximos de texto, una pregunta opcional sin responder frente a false y una sesión abierta de una versión anterior. Los
  decimales (12,50 y 12.50) se comprueban con r15 en la prueba integral ([WhatsApp](../features/whatsapp.md#piloto-de-formularios)).

## Inteligencia artificial

La [calificación OpenAI](../guides/configurar-ia.md#evidencia-para-habilitar-perfiles) registra el modelo de texto y STT exactos, sus perfiles/instrucciones y los 48 casos ficticios; no acredita otra cuenta, proveedor ni datos reales.

- [ ] Verificar Anthropic con credenciales propias y calificar su modelo exacto antes de declararlo. Registrar proveedor, modelo, resultado, uso y límites sin secretos; las pruebas de OpenAI no lo sustituyen.
- [ ] Revisar egress y retención de las cuentas OpenAI y Anthropic antes de usar datos reales
  (`store:false` no es retención cero). Hasta entonces, el piloto usa datos ficticios.
- [ ] Restaurar en un ambiente aislado la base, el anillo de claves y el `InstallationId`, y comprobar que se
  descifran la credencial, los comprobantes y el contenido del asistente antes de habilitar tráfico. El contenedor de
  archivos privados se restaura a la misma hora que la base ([Azure](../deploy/azure-setup.md#restaurar-a-un-momento-dado)),
  y se comprueba que abren una foto de una respuesta, un PDF de ARCA y un archivo del asistente, y que un archivo borrado
  por la limpieza se recupera del borrado temporal (N15).
- [ ] Probar una segunda instalación independiente (base, anillo e ID, UserSecretsId, prefijo de Redis,
  origen y credenciales propios) y comprobar que no comparte nada con la primera.
- [ ] En el despliegue: anillo de IA en secrets o Key Vault, el destino de OpenTelemetry y la alerta de 5xx, como
  dice la [guía](../guides/despliegue.md#logs-trazas-y-alertas) (la consola ya escribe JSON con el `TraceId` y el
  `UserId`, N10), dos réplicas reales y apagado con turnos en curso (deben quedar Uncertain o Expired, sin reenviarse).

## Privacidad de la instalación de prueba

El plan de WhatsApp registró una política publicada en el repo `ezeellena2/privacidad`, para
«Servicios Ya», con Ezequiel Ellena (Rosario, Santa Fe) como responsable y edad mínima de 18 años.
Es información de aquella instalación, no una premisa de cada proyecto derivado. El contenido
externo y la vigencia jurídica no se verificaron en esta auditoría.

Se conservan estos pendientes del registro original para revisar antes de incorporar personas reales:

- [ ] Procedimiento probado de borrado/anonimización: cuenta, roles/logins, códigos/enlaces/auditorías,
  invitaciones, contactos, mensajes, lecturas por operador, operaciones salientes y receipts
  (mensajes, lecturas y operaciones antes de contactos por sus FK Restrict), autorizaciones
  y tokens OpenIddict, y copias de correos en Enviados de Gmail. ARCA se suma (H25): los comprobantes, su pedido y su PDF se
  conservan durante el plazo fiscal que fija el negocio con su contador, y un pedido de borrado no los alcanza dentro de ese
  plazo; la purga al vencer queda para después.
- [ ] Plazos y retención de los contactos y de las cuentas borradas; reflejar en la política y el procedimiento los que
  ya rigen. Los de `LoginCodes`, `LoginLinks`, `LoginAudits` (IP, navegador y lo escrito en un fallido), `UserInvitations` y los tokens de
  OpenIddict los aplica la retención de N16 ([identidad](../features/identidad.md#retención-de-los-datos-del-ingreso-n16-2026-11-16)).
  El cuerpo de mensajes ya se limpia a los 90 días mientras la Api corre; ninguna de las dos limpia con la Api apagada.
- [ ] Revisión profesional de transferencias internacionales, registro de la base, domicilio del
  responsable y plazos prometidos. Es una consulta pendiente heredada, no asesoramiento jurídico.
- [ ] Alinear nombre del negocio en web/correos/orientación de acceso y política; enlazarla desde
  login, registro, canje histórico, pie y comunicaciones de acceso antes de crear cuenta.
  El bot anterior está retirado; las superficies nuevas se diseñan primero.
- [ ] Canal humano de contacto en los recursos de acceso de ambos idiomas y un remitente propio para correo;
  revisar el tratamiento de los datos por los proveedores.
- [ ] Revisar que las suscripciones del webhook incluyan solo los campos usados (`messages`).
- [ ] Revisar opt-in y propósito del código de vinculación, que usa la misma plantilla del ingreso;
  conservar en la retención qué cuenta pidió un código para el contacto de otra persona.
- [ ] Evaluar exclusión de invitaciones («no me invites») y la evidencia de consentimiento:
  actualmente queda quién y cuándo lo confirmó como administrador, no una aceptación del destinatario.
- [ ] Revisar la confirmación del contacto al invitar, para mitigar números/correos mal cargados.
- [x] Un límite diario de invitaciones por correo, además de la espera por cuenta: N7 (882b8717) suma el tope por
  administrador y la reserva dentro del tope de todos ([administración](../features/administracion.md)).
- [ ] Evaluar un límite diario de invitaciones por WhatsApp, que hoy no miran los topes del correo, y su costo.
- [ ] Evaluar si `users.read` debe ver «leída» o solamente «entregada» para la última invitación.

El plan también registraba dos actualizaciones ya hechas: retención del cuerpo a 90 días y
adaptación de la política a perfil/invitaciones. No se vuelven a abrir como tareas de implementación;
la coherencia con un producto derivado sí se revisa al preparar su producción.

## Notas heredadas por confirmar

- La carrera de arranque en tests de configuración WhatsApp/Google requiere revisar el test
  actual antes de actuar: `WhatsAppRegistrationTests` ya invoca `IStartupValidator`; no afirmar
  que conserva el defecto del plan original.
- Resuelto el 2026-11-16 (N31, 12eb2a28): el tope diario de códigos tiene su índice parcial
  `(Channel, SentAtUtc) WHERE SentAtUtc IS NOT NULL`, en la migración `AddIdentityRetentionIndexes`.

Cada casilla se cierra con evidencia del trabajo concreto y fecha/commit. Una modificación de
documentación no acredita ejecución, aprobación externa ni despliegue.

## Formularios: baja, reactivación y eliminación

Las [reglas implementadas](../features/formularios.md#dar-de-baja-reactivar-y-eliminar--2026-10-21), su review y la integración local conservan su evidencia propia. La baja con un envío abierto, la deprecación confirmada, reactivar y eliminar se prueban una sola vez en el [piloto único posterior a E2](#formularios-piloto-único-con-meta-después-de-e2), junto con los otros recorridos pendientes. No se inició ese piloto durante el desarrollo.

## Formularios: catálogos E1

La carga de una vez, el valor tipado y elegir varias están implementados según las [reglas de E1](../features/formularios.md#catálogos-base-de-la-etapa-e1--2026-10-21). El [cierre local conjunto](../features/formularios.md#verificación-local-de-catálogos-e1--2026-10-22) y el [code review cerrado el 2026-10-22](../reviews/2026-10-22-code-review-catalogos-e1.md#cierre-local) se conservan. La prueba física de importación, valores y casillas se reúne con [E2](../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23) en el [piloto único](#formularios-piloto-único-con-meta-después-de-e2), por decisión del usuario.

## Formularios: piloto único con Meta después de E2

**Pendiente:** primero corregir el [code review de E2](../reviews/2026-10-23-code-review-catalogos-e2.md), después una sola prueba guiada con Aspire, túnel y proveedores reales. Este recorrido reúne los pendientes de baja/reactivación/eliminación, menú/detalle, contactos simultáneos y catálogos E1/E2; no repite como pendientes las comprobaciones físicas anteriores ya acreditadas arriba.

- [ ] Preparar Zonas y Proveedores ficticios con CUIT, Zona de ese catálogo y al menos otro campo de dato; pegar opciones e importar su XLSX. Revisar altas, cambios, bajas y errores antes de aplicar, conservando las identidades por código. Definir qué título y descripción viajan y la presentación propia de una pregunta.
- [ ] Cargar zonas distintas a dos personas y publicar un formulario con catálogo, elegir varias y archivos. Cada persona ve sólo sus opciones en su celular y sólo los campos elegidos para mostrarse. A una persona sin el dato se le bloquea el envío con el motivo, sin mandar la lista completa. Revisar la muestra por destinatario y los avisos no bloqueantes antes de publicar.
- [ ] Con dos o tres contactos simultáneos, en Asistente y Atención manual, comprobar que cada número recibe sólo su propuesta y contexto; un turno lento no detiene a los otros. Revisar y enviar una sugerencia desde el chat y completar su formulario.
- [ ] Confirmar en un `nfm_reply` real de CheckboxGroup el array de identidades, el orden que entrega Meta y la diferencia entre `[]` y clave ausente. La implementación conserva el orden recibido; no se afirma que Meta devuelva el orden físico de selección.
- [ ] Una pregunta opcional de «elegir varias», filtrada, que queda sin opciones para una persona manda un `CheckboxGroup` vacío. La referencia de Meta pide de 1 a 20 opciones, y E1 sólo confirmó el Dropdown vacío: confirmar qué hace Meta al publicar y al abrir el Flow.
- [ ] Completar respuestas con varias opciones y archivos. En MF1 y su bloque Por aprobar, recorrer D2 y pantalla completa, regreso a la misma fila y filtros, flechas según origen y PDF dentro de la acción que lo generó. La cuenta que sólo decide no ve respuestas ni pantalla completa.
- [ ] Revisar los campos sellados en planilla, detalle, Excel y PDF, también después de editar valores/nombres del catálogo. Probar Columnas y «Solo las columnas visibles», el orden entre opciones y «alguna de». El chat conserva su tarjeta. Los campos de dato se ven por defecto y los de filtro son optativos en la planilla; el PDF muestra sólo los de dato.
- [ ] Usar además un catálogo decimal y comprobar texto y valor histórico en respuesta y PDF. En una elección simple Excel conserva el límite de 15 cifras significativas y los valores de varias conservan texto canónico exacto; exportar el catálogo escribe números de Excel hasta 15 cifras y, más largos, su texto exacto. En inglés comprobar nombres de pregunta/campo y el respaldo individual de una etiqueta sin traducir.
- [ ] Con un envío abierto, dar de baja el formulario: deja de ofrecerse en selector, asistente y sugerencias. Responder ese envío antes de vencer y comprobar respuesta, aprobación, avisos y PDF conservados. Al vencer el último envío abierto, verificar la deprecación de todas las versiones y su estado en listado/editor; un resultado Sin confirmar se consulta antes de repetir.
- [ ] Reactivar: sigue en Dados de baja durante la publicación y vuelve a Activos sólo al aprobar Meta la versión nueva; usa Flows nuevos y continúa la numeración. Eliminar otro formulario sin respuestas ni envíos abiertos: desaparece, conserva el prefijo reservado y depreca sus Flows. Uno con respuestas no ofrece Eliminar.

Al cerrar cada recorrido se registra su resultado y el alcance real, sin confundirlo con escenas sintéticas o Meta controlado. Se conservan respuestas y archivos reales; se restauran configuración de prueba, datos de cuenta y modo original de los chats, se cierran las sesiones temporales y se ejecuta `aspire stop`. No se inicia este piloto como parte del cierre técnico de E2.

## Formularios: renglones, lo que quedó de la prueba integral — 2026-11-11

La prueba integral del 2026-11-09 aprobó con el usuario los ocho puntos de renglones ([traspaso 12](2026-11-11-traspaso-multitenant-renglones.md#5-cómo-se-verificó)). Quedan:

- [ ] Por WhatsApp, volver con la flecha del ítem 3 al 2 y contestar «No» antes de enviar. La respuesta tiene que traer
  sólo los ítems 1 y 2. El 2026-11-07 se comprobó que llegan sólo las claves de lo visitado, y la prueba integral terminó
  con un «No» sin volver.
- [ ] La lectura de una respuesta con ítems en inglés: la planilla, el detalle, el Excel y el PDF. La prueba integral la
  recorrió en español.
- [ ] Los ejemplos HTTP exportados al front (`src/test/fixtures/forms-http/`) todavía dicen `flow-static-7.3-r11`. Se
  actualizan con la próxima exportación conjunta.
- [ ] Opcional: sumar el bloque a las escenas visuales `responses` (vista rápida), `chatForms` y `formPdf`. Hoy lo cubren
  sus tests de componente.
