# Etapa 2 — Catálogos Manual y Filters

Estado 2026-10-12: Manual (K1–K3), Filters (K4), preguntas de catálogo (K6) y envío/captura (K7) implementados y verificados localmente; `/catalogos` está montado en el panel. La Etapa 2 sigue en curso: continúan los pendientes heredados de Etapa 1 y el piloto conjunto con Meta. Lo de Etapa 1 está en [formularios][feature].

## Qué se construye

- **K1 · Lista** ([tablero][t], completado con K4). Lista paginada, búsqueda, filtros de origen y `Active|Inactive|Unavailable` conectados. Los conteos por origen vienen del backend, respetan búsqueda/estado e ignoran origen/página; «No se puede usar» muestra el motivo seguro.
- **K2 · Alta** ([tablero][t]). Diálogo integrado y alta Manual conectada. «Tomarlas de la base, con filtros» ya está habilitada por K4; Query no se ofrece.
- **K3 · Manual** ([tablero][t]). Alta, detalle, edición y opciones paginadas conectados, con `expectedRevision`, delta y `moves`. El servidor asigna posiciones y resuelve movimientos entre páginas en una sola escritura. Crear, guardar, recargar, editar, conflicto y permisos están verificados en la app.
- **K4 · Filters** ([tablero][t], implementado). Backend: `Kind=Filters` con columnas y condiciones (CHECK por Kind); el lector arma un SELECT fijo del origen Usuarios y Roles permitidos, sin vista persistente; los valores de rol son RoleId y la etiqueta es su nombre actual; filas sin texto se excluyen. Condiciones compiladas a SQL con parámetros sobre la conexión normal; `POST /api/forms/catalog-previews` sin UoW. El listado entrega conteos por origen calculados antes de aplicar origen/página. Front: `CatalogFiltersEditor` y la vista previa «Como si lo recibiera» consumen las rutas compiladas; la metadata del origen no lleva estado de disponibilidad porque el lector consulta tablas del esquema.
- **K6 · Pregunta «Elegir de un catálogo»** ([tablero][t], implementado). Backend: habilitar `source.catalog` del formato 1 sin convertir versiones; el check de publicación avisa un catálogo inválido; el compilador genera un Dropdown con `data-source: ${data.<pregunta>_options}`; la lista y el detalle alimentan «Lo usan» con definiciones actuales distintas y lo ocultan sin permiso de lectura. Front: tipo nuevo en el editor; inválidos visibles y no elegibles; la vista previa muestra la pregunta sin opciones.
- **K7 · Enviar y responder** ([tablero][t]). Backend: el puerto de preparación resuelve `[{id,text,detail}]` por destinatario y lo pone en los datos iniciales del Flow; la sesión fija catálogo, revisión y opciones ofrecidas; la captura valida la oferta y guarda `BusinessFormRecordReferences` en el mismo commit que la respuesta. Front (WhatsApp): el diálogo muestra las opciones preparadas y los bloqueos; la tarjeta muestra la opción con su catálogo, o el rechazo con «Mandar de nuevo».

## Decisiones tomadas

No se vuelven a preguntar.

- Alcance: Manual y Filters (K1–K4, K6, K7). Query/K5 queda después ([ADR 0019]): no hay SQL configurable y K2 muestra solo Manual y Filters.
- El origen no cambia: otro origen es otro catálogo (K2).
- Filters lo arma el sistema desde columnas permitidas de un origen fijo. Primero: usuarios (id, nombre, roles, activo), nunca correo, contraseña ni códigos. Los orígenes los agrega desarrollo, no la pantalla ni la IA.
- El origen de usuarios es un SELECT fijo del lector del módulo, sin vista en la base: una vista sobre las tablas de Identity frenaría migraciones futuras del núcleo (code review del 2026-10-12). El filtro por rol guarda el Id del rol y muestra su nombre actual. Las opciones sin texto se excluyen.
- Una fila por persona. «Activo» es la cuenta activa, con la regla de Identidad; el rol se filtra como «tiene el rol», porque una persona puede tener varios.
- Valores siempre como parámetros, con la conexión normal: sin credencial restringida aparte ni pruebas de ataques SQL.
- Lo que el plan no fija se resuelve con la opción más simple que cumple el plan y las reglas, sin mecanismos preventivos para casos inexistentes. Una ruta no publicada se cambia coordinando el contrato con el front, sin capa de compatibilidad.
- Condiciones con la gramática común (columna permitida, operador según tipo, valor); en K4 se cumplen todas.
- Único parámetro de contexto: el usuario destinatario, puesto por el servidor, nunca por el mensaje. La zona no existe. «Sin incluir a quien lo recibe» usa ese dato.
- Vista previa con una persona de ejemplo elegida explícitamente, sin suplantar su identidad ni sus permisos. Se prueba antes de guardar, con tope de tiempo y de filas.
- Catálogo inválido después de una migración: no frena el despliegue; queda «No se puede usar» con motivo seguro (K1), no se elige en K6 y no se puede mandar (K7).
- Guardar un catálogo es configuración interna: un límite, revisión y 409, sin comprobantes recuperables ([ADR 0018]).
- Nombre y descripción del catálogo en es y en. Cada ítem tiene texto, detalle y código; el español es obligatorio y el inglés opcional, con respaldo en español. El código no se muestra en WhatsApp.
- Dar de baja un ítem no lo borra y se puede reactivar; las respuestas viejas lo siguen mostrando.
- Posiciones únicas por catálogo. El servidor ubica las nuevas al final; reordenar es mover por `itemId` (arriba o abajo) dentro del mismo Guardar.
- La versión guarda solo `source.catalog`: cambiar ítems no obliga a republicar. Cambiar el catálogo de una pregunta publicada crea una pregunta nueva; versiones y sesiones anteriores quedan intactas.
- Las opciones se resuelven al enviar, para cada destinatario, y viajan dentro del mensaje. Sin endpoint de intercambio.
- Más de 200 opciones (tope fijo), o ninguna en una pregunta obligatoria: no se puede mandar. Nunca se trunca.
- Al responder se valida que la opción esté en la oferta fijada en la sesión. Se guarda catálogo, revisión, id y el texto que vio el empleado; renombrar después no cambia la respuesta.
- «Dada de baja» aplica a las opciones escritas a mano. En Filters alcanza con que la opción haya estado en la oferta: la captura no vuelve a consultar el origen.
- Opción dada de baja antes de responder: no se guarda la respuesta (ni registro ni número); el operador ve el motivo sin valores y puede reenviar a mano con la lista actual. Sin reenvío automático. El aviso nombra la pregunta, no la opción elegida (el caso 6 del tablero K7 muestra el valor: vale esta regla).
- Un catálogo se da de baja y se reactiva desde su pantalla, con su revisión; no se borra. Dado de baja, no se elige en preguntas nuevas.
- K1: ignorar el catálogo de tipo consulta que todavía aparece en el tablero.
- Permisos `formcatalogs.read` y `formcatalogs.manage`, ya creados. Las rutas son las del código: ítems y movimientos se guardan en el PUT del catálogo.

## Fuera de esta etapa

- Query/K5: editor, parser, permiso propio y credencial restringida (ADR 0019).
- Endpoint de intercambio cifrado: búsqueda en vivo, listas dependientes y listas largas.
- Catálogos propuestos por la IA (Etapa 5, H4).
- Vistas de maestros que todavía no existen (clientes, productos, depósitos).
- Importar ítems desde Excel (con la carga web, después de Etapa 4).
- Fotos y documentos (2B), planilla (3), reglas y PDF (4), grupos y remitos.

## Cómo se cierra

- Checks de cierre de paso de [ADR 0018] y [validar un cambio], en los dos repos.
- TDD de catálogos, ítems, referencias, gramática de condiciones y compilador del Dropdown.
- Docker: roles sin miembros y cuentas sin nombre visible en el SELECT actual; contexto y autorización del destinatario; ítem dado de baja (rechazo completo, sin número); revisión concurrente con 409; rollback y replay de la captura con referencias.
- Rutas en el inventario, OpenAPI y ejemplos HTTP exportados para el front.
- Piloto real con Meta: formulario con una pregunta Manual y una de Usuarios → mandar desde el chat → el teléfono vinculado ve el Dropdown con texto y detalle desde los datos iniciales → responder → la referencia queda guardada y se ve tras recargar. Comprobar tamaño y límites y que los ids vuelven intactos.
- No se cierra con el SQL de la demo ni solo con la vista previa web.

## Cierre del paso Manual — 2026-10-12

Backend: una suite completa con Docker terminó 2705/2706; el único fallo fue el login del arnés antes de levantar la API. La corrección pasó focalmente (1/1), sin otra suite completa. Front: 1542/1542 en 149 archivos con un worker, build y lint aprobados, sin ampliar timeouts ni cambiar assertions. Visual: 300/306 en la completa y 6/6 focales con configuración estable; Manual pasó 6/6, con capturas revisadas y referencias intactas. El recorrido API/PostgreSQL/Redis pasó 9/9: CRUD, movimientos entre páginas, 409 con adopción y permisos. Las fixtures usan cuerpos HTTP exportados y procedencia automática; las corridas previas se conservan con sus resultados reales.

Este cierre corresponde a Manual; no acredita Filters, referencias ni Meta. Continúa K4, después K6, K7, pendientes heredados y piloto real.

## Cierre de Filters K4 — 2026-10-12

El origen Usuarios usa un SELECT fijo de las tablas actuales con la conexión normal, sin vista persistente ni conexión restringida. Los filtros guardan RoleId y muestran el nombre vigente; los roles sin miembros aparecen y se excluyen cuentas sin texto. La migración que elimina la vista y su reversión pasaron 5/5; el test HTTP de PostgreSQL/Redis verifica RoleId tras renombrar, roles sin miembros y nombres vacíos. Backend: build sin advertencias y suite completa 2715/2715. Front: suite unitaria 1599/1599, tipos, lint/arquitectura 48/48; contrato actual 6/6, pantalla 1/1 y visual K4 6/6 en es/en a 1440/390/320, con 36 capturas revisadas. No se agregó compatibilidad ni se usó Meta; navegador con transporte simulado y API/PostgreSQL/Redis se acreditan por separado.

Este cierre acredita K4 y habilita K6. No acredita K7 ni el piloto real.

## Cierre de referencias de catálogo K6 — 2026-10-12

Las preguntas `choice` aceptan opciones manuales o `source.catalog`, conservan el formato 1 y exigen reemplazar la identidad al cambiar de origen. El chequeo de publicación bloquea referencias inexistentes o inactivas; el Dropdown compilado usa `data-source: ${data.<pregunta>_options}`, mientras la vista previa web muestra la pregunta sin opciones. La lista y el detalle cuentan definiciones actuales distintas y devuelven `null` si falta permiso de lectura. Una prueba HTTP con PostgreSQL/Redis verifica que dos preguntas de una definición cuentan como un solo uso, también en el detalle. Backend: build completo sin advertencias y suite completa 2735/2735. Front: suite unitaria 1599/1599, tipos, lint/arquitectura 48/48; contrato K6 6/6 y visual K6 6/6 en es/en a 1440/390/320, con 30 capturas revisadas. Son verificaciones separadas de navegador y API; no se usó Meta.

Este cierre acredita K6. No acredita K7 ni el piloto real.

## Cierre local de K7 — 2026-10-12

Implementados oferta por destinatario/idioma, datos iniciales del Flow, sesión protegida, pertenencia, baja Manual con rechazo completo, referencias históricas y lectura/reenvío desde el chat con sus permisos. Filters no vuelve a consultar el origen al capturar. Registro, valores, referencia, recibo y numeración comparten la transacción; rollback y replay están probados.

Verificación final: backend **2735/2735** con PostgreSQL/Redis y build sin advertencias; front **1614/1614**, consumidor de cuerpos HTTP reales **7/7**, build y lint/arquitectura **48/48**; visual completa **398/398**, tres workers, sin cambiar referencias ni timeouts. Recorridos y capturas representativas revisados en es/en a 1440/390/320 y ancho intermedio. El arnés compila sólo sus entradas, conserva CSS de desarrollo y no usa proxy a una API real. La persistencia SQL, los cuerpos HTTP y el navegador con transporte simulado se acreditan por separado; no se afirma un recorrido físico con Meta.

Meta queda para hacerlo con el usuario: Manual + Usuarios, IDs intactos, textos/detalles, respuesta y recarga. La Etapa 2 sigue abierta por ese piloto y sus pendientes heredados. La diferencia del optimizador CSS en el panel de conversaciones se sigue en el sistema visual; no se cambió el build productivo para K7.

## Pendientes que se arrastran

De Etapa 1; se cierran en esta etapa:
- Presentación de R3: versión e idioma de la respuesta y señal explícita de purga del mensaje (no inferirla de `Body=null`), respetando el tablero.
- Prueba física en el teléfono: decimales (12,50 y 12.50), ausencia opcional, máximos de texto y sesión de una versión anterior.
- Test intermitente de Roles en el front: diagnosticar sin relajar assertions.
- Dos pruebas visuales del editor (es, 390 y 320 px) fallan en la espera inicial de Nombre: comprobar la preparación de Idiomas con un control negativo antes de tocar los tests.

[t]: ../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#los-tableros
[feature]: ../../features/formularios.md
[ADR 0018]: ../../decisions/0018-robustez-proporcional-al-efecto.md
[ADR 0019]: ../../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md
[validar un cambio]: ../../guides/validar-un-cambio.md

## Cierre de las correcciones solicitadas — 2026-10-13

Cerrados los puntos 1, 2, 7, 8 y 9 de «Antes de K7» del review: rechazos definitivos de Accept como Failed; captura con cinco intentos fallidos persistidos y Rejected visible; baja/reactivación Manual y Filters en sus PUT con revisión; reintento explícito tras red/5xx y revisión sólo por 409; búsqueda de personas con espera de 300 ms, editor y foco conservados.

Backend: **2746/2746** con PostgreSQL/Redis, build sin advertencias. Front: **1621/1621**, lint/arquitectura **48/48**, build limpio, visual completa **416/416** y capturas representativas revisadas en es/en a 1440/390/320. Cada punto tiene su regresión; no se cambiaron referencias ni timeouts. La persistencia y el navegador simulado se verificaron por separado. Permanece el aviso conocido NO_COLOR/FORCE_COLOR del runner, sin silenciarlo.

Decisiones aplicadas: cinco fallas por mensaje; `isActive` se guarda junto con el contenido y su revisión, sin ruta adicional; la acción de la pantalla prepara el estado y Guardar lo aplica; una baja Filters no necesita preview y una reactivación sí. No se agregaron cachés ni SQL configurable.

Falta el piloto conjunto con Meta: Manual + Usuarios, envío al celular, respuesta, referencia/número y recarga. La etapa sigue abierta hasta esa comprobación; los pendientes heredados conservan su alcance anterior. Después se inicia 2B en este mismo hilo, usando Azurite hasta contar con Azure; la prueba contra Azure real queda para el cierre de 2B.
