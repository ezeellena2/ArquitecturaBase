# Etapa 3 — Planilla, detalle y exportar a Excel

Estado: Diseño elegido; sin empezar · 2026-10-11.

## Qué se construye

Tableros: T1–T5 en [Formularios por WhatsApp][tableros]. Lo hecho en Etapa 1 está en [formularios.md][hecho]. Esta etapa solo lee.

- **Panel «Respuestas»** (T1).
  - Backend: `GET /api/forms/navigation`, `PagedResult` de publicados que el usuario puede ver (`formrecords.read`). Autoriza, filtra y ordena en SQL antes de contar.
  - Front: «Respuestas» arriba, un enlace por formulario a `/formularios/{id}/respuestas`, «Ver todos» si son muchos. «Diseñar formularios» y «Catálogos» abajo. Nada fijo en `navigation.ts`.
- **Planilla** (T1).
  - Backend: `GET /api/forms/definitions/{id}/records`. Columnas fijas: número, recibida y cargó (persona y canal). Una columna por pregunta (FieldId) de todas las versiones, en el orden del formulario actual. Catálogo: el texto capturado al responder.
  - Front: «—» si la pregunta no existía en esa versión; «Sin respuesta» si era opcional y quedó vacía. Selector «Columnas». Paginado y orden según [AGENTS.md][persistencia].
- **Filtros** (T2).
  - Backend: un filtro por pregunta según su tipo, más versión del formulario. Usa la gramática de condiciones de la Etapa 2, con FieldId. Conteos con la misma definición de filtro ([conteos][conteos]).
  - Texto libre o datos personales van por `POST …/records/search` y `POST …/records/filter-counts/search`, sin UoW.
  - Front: la búsqueda mira los textos largos. Valores privados nunca en la URL, storage ni logs; en la URL, solo página, tamaño, orden y filtros no sensibles.
- **Detalle** (T3).
  - Backend: `GET /api/forms/records/{id}`: valores con las preguntas y opciones de su versión, de dónde vino e historial.
  - Front: anterior y siguiente dentro de la planilla, con el mismo filtro y orden.
- **Estados** (T4): carga, sin respuestas, sin coincidencias, error, muchos formularios, sin permiso, respuesta que no está y pregunta que cambió de tipo.
- **Fotos y documentos de la 2B**: columna con cantidad y miniatura; galería en el detalle; se abren con el visor de F4. Mismo permiso que los valores.
- **Exportar a Excel** (T5).
  - Backend: un `.xlsx` con los filtros aplicados (en el cuerpo, nunca en la URL). Recorre páginas en el servidor con presupuesto. La biblioteca se elige y se justifica en Infrastructure.
  - Permiso nuevo `formrecords.export`, según la [guía de permisos][permiso].
  - Contenido: todas las preguntas o solo las columnas visibles; número, recibida, cargó y canal siempre. Números y fechas como tipos de Excel; encabezados en el idioma de quien exporta. «Sin respuesta» es celda vacía; «—» marca pregunta inexistente.
  - Front: diálogo de T5. Sin el permiso, el botón no aparece. El resultado se informa con el aviso común.

## Decisiones tomadas

No se vuelven a preguntar.

- Etapa 3 es planilla, filtros, detalle y exportación. No crea rutas ni servicios de captura.
- Las respuestas de Etapas 1–2B aparecen sin migrar ni volver a capturarse.
- Exportar: `.xlsx` con los filtros aplicados, permiso propio `formrecords.export`, hasta 10.000 respuestas por vez. Con más, el diálogo pide acotar los filtros. Sin trabajos en segundo plano.
- De las preguntas de archivo se exporta la cantidad y los nombres, no los archivos.
- Foto y documento tienen su columna y se ven con el visor de F4.
- Ausencia, cero y false se distinguen en pantalla y en el Excel.
- Filtrar por una opción retirada conserva la historia.
- 403 o permiso revocado no exponen datos ni conteos.
- La columna «Estado» llega con la aprobación (Etapa 4), solo en formularios que la piden.

## Fuera de esta etapa

- Cargar respuestas desde la web e importar desde Excel: después de la Etapa 4, con su tablero.
- Correcciones: después, con tableros propios.
- Estado, aprobación, PDF y avisos (Etapa 4); lo que hizo el asistente y la cadena de origen (Etapa 5).
- Exportar los archivos o hacerlo en segundo plano.
- Permisos por formulario: siguen los permisos globales.

## Cómo se cierra

- Checks por corte y al cerrar, según [ADR 0018][0018] y [validar un cambio][validar]. Todo es lectura: sin recibos, leases ni pruebas de varios hosts.
- TDD: operadores por tipo, «—» frente a «Sin respuesta», cero y false, opción retirada, columnas de varias versiones, orden con desempate.
- Docker: filtros compilados en SQL antes de contar y paginar; conteos coherentes con el listado; 403 y revocación sin datos; tope de 10.000.
- EXPLAIN con volumen real sobre respuestas y valores.
- Front: recorrido en el navegador de T1–T5, es/en, 390 y 320 px.
- Prueba real: no hay Meta ni IA. Abrir la planilla con respuestas reales (incluidas foto y documento) y abrir el `.xlsx` exportado en Excel.

## Requisitos previos

- Etapa 2 cerrada: catálogos, referencias capturadas y gramática de condiciones.
- Etapa 2B cerrada: preguntas de archivo, almacenamiento privado y visor F4.
- La elección de «Columnas» vive en la URL, como los filtros no sensibles; no se guarda en el servidor.

## Ajustes ya decididos (2026-10-11)

- T1–T5 están elegidos. Exportar va en esta etapa, con T5.
- Antes de programar esa parte, el lienzo suma a T1 la columna de fotos y documentos y a T3 la galería, con el visor de F4.


[tableros]: ../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#los-tableros
[hecho]: ../../features/formularios.md
[persistencia]: ../../../AGENTS.md#guardar-errores-y-persistencia
[conteos]: ../../features/administracion.md#conteos-por-opción-de-filtro
[permiso]: ../../guides/permiso-nuevo.md
[0018]: ../../decisions/0018-robustez-proporcional-al-efecto.md
[validar]: ../../guides/validar-un-cambio.md
