# Etapa 2 — Catálogos Manual y Filters

Estado 2026-10-12: Manual (K1–K3) implementado y verificado de punta a punta; `/catalogos` ya está montado en el panel. La Etapa 2 sigue en curso: continúa Filters/K4, luego K6, K7, los pendientes heredados de Etapa 1 y el piloto con Meta. Lo de Etapa 1 está en [formularios][feature].

## Qué se construye

- **K1 · Lista** ([tablero][t]). Lista Manual paginada y página `/catalogos` integradas. La API tiene búsqueda y filtros de origen y estado; los conteos por origen, los controles de filtros y «No se puede usar» con motivo se completan con K4.
- **K2 · Alta** ([tablero][t]). Diálogo integrado y alta Manual conectada. «Tomarlas de la base, con filtros» se habilita al entregar K4; Query no se ofrece.
- **K3 · Manual** ([tablero][t]). Alta, detalle, edición y opciones paginadas conectados, con `expectedRevision`, delta y `moves`. El servidor asigna posiciones y resuelve movimientos entre páginas en una sola escritura. Crear, guardar, recargar, editar, conflicto y permisos están verificados en la app.
- **K4 · Filters** ([tablero][t]). Backend: `Kind=Filters` con columnas y condiciones (CHECK por Kind); migración de la vista de usuarios con nombre y columnas en inglés, e inventario de columnas permitidas; condiciones compiladas a SQL con parámetros sobre la conexión normal; `POST /api/forms/catalog-previews` sin UoW; revalidación después de cada migración. Front: conectar `CatalogFiltersEditor` y la vista previa «Como si lo recibiera».
- **K6 · Pregunta «Elegir de un catálogo»** ([tablero][t]). Backend: habilitar `source.catalog` del formato 1 sin convertir versiones; el check de publicación avisa un catálogo inválido; el compilador genera un Dropdown con `data-source: ${data.<pregunta>_options}`; alimenta «Lo usan» (hoy 0). Front: tipo nuevo en el editor; inválidos visibles y no elegibles; la vista previa muestra la pregunta sin opciones.
- **K7 · Enviar y responder** ([tablero][t]). Backend: el puerto de preparación resuelve `[{id,text,detail}]` por destinatario y lo pone en los datos iniciales del Flow; la sesión fija catálogo, revisión y opciones ofrecidas; la captura valida la oferta y guarda `BusinessFormRecordReferences` en el mismo commit que la respuesta. Front (WhatsApp): el diálogo muestra las opciones preparadas y los bloqueos; la tarjeta muestra la opción con su catálogo, o el rechazo con «Mandar de nuevo».

## Decisiones tomadas

No se vuelven a preguntar.

- Alcance: Manual y Filters (K1–K4, K6, K7). Query/K5 queda después ([ADR 0019]): no hay SQL configurable y K2 muestra solo Manual y Filters.
- El origen no cambia: otro origen es otro catálogo (K2).
- Filters lo arma el sistema desde columnas permitidas de vistas versionadas por migración. Primera: la vista de usuarios (id, nombre, rol, activo), nunca correo, contraseña ni códigos. Las vistas las agrega desarrollo, no la pantalla ni la IA.
- La vista y sus columnas se nombran en inglés, con la convención de la base (los acuerdos la llamaban `catalogos.usuarios`). Es del módulo BusinessForms: se crea en su migración y se va si se quita el módulo.
- Una fila por persona. «Activo» es la cuenta activa, con la regla de Identidad; el rol se filtra como «tiene el rol», porque una persona puede tener varios.
- Valores siempre como parámetros, con la conexión normal: sin credencial restringida aparte ni pruebas de ataques SQL.
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
- «Dada de baja» aplica a las opciones escritas a mano. En Filters alcanza con que la opción haya estado en la oferta: la captura no vuelve a consultar la vista.
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
- Docker: migración que rompe la vista (el despliegue sigue y el catálogo queda «No se puede usar»); contexto y autorización del destinatario; ítem dado de baja (rechazo completo, sin número); revisión concurrente con 409; rollback y replay de la captura con referencias.
- Rutas en el inventario, OpenAPI y ejemplos HTTP exportados para el front.
- Piloto real con Meta: formulario con una pregunta Manual y una de Usuarios → mandar desde el chat → el teléfono vinculado ve el Dropdown con texto y detalle desde los datos iniciales → responder → la referencia queda guardada y se ve tras recargar. Comprobar tamaño y límites y que los ids vuelven intactos.
- No se cierra con el SQL de la demo ni solo con la vista previa web.

## Cierre del paso Manual — 2026-10-12

Backend: una suite completa con Docker terminó 2705/2706; el único fallo fue el login del arnés antes de levantar la API. La corrección pasó focalmente (1/1), sin otra suite completa. Front: 1542/1542 en 149 archivos con un worker, build y lint aprobados, sin ampliar timeouts ni cambiar assertions. Visual: 300/306 en la completa y 6/6 focales con configuración estable; Manual pasó 6/6, con capturas revisadas y referencias intactas. El recorrido API/PostgreSQL/Redis pasó 9/9: CRUD, movimientos entre páginas, 409 con adopción y permisos. Las fixtures usan cuerpos HTTP exportados y procedencia automática; las corridas previas se conservan con sus resultados reales.

Este cierre corresponde a Manual; no acredita Filters, referencias ni Meta. Continúa K4, después K6, K7, pendientes heredados y piloto real.

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
