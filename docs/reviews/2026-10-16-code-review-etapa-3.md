# Code review: Etapa 3, planilla y Excel — 2026-10-16

Revisión de lectura de backend `9b774bfc..bef6d615` y front `3f6051f..5f3aa18`. No hay huecos de seguridad: permisos en controller y servicio, filtros con parámetros y lista blanca, y datos privados solo por POST. Se corrige en el chat de la Etapa 4, al cerrar el corte en curso, backend y front juntos, cada arreglo con su test y commits chicos. Las decisiones del [cierre de la Etapa 3](../features/formularios.md#planilla-detalle-y-excel--etapa-3) no cambian.

## Arreglar ahora

1. **Los conteos del panel de filtros mandan el borrador en cada tecla** (front `ResponseFiltersPanel.tsx:201-204`).
   - «12,5» en un número, «12,», «-» o un «Contiene» de solo espacios llegan crudos y el backend responde 400; cada tecla deja un toast con un Reintentar que vuelve a fallar.
   - Arreglo: los conteos usan el borrador ya normalizado y válido, con espera. La coma se convierte y los espacios se recortan antes de mandar. El 400 y el 429 siguen el contrato de mensajes.
2. **El Excel repite toda la lectura en cada página** (backend `FormExportService.cs:31,56`).
   - Cada página de 100 vuelve a hacer permisos, definición, esquemas, COUNT y ORDER BY con OFFSET de hasta 9.900. Con unas 9.500 filas puede vencer el plazo, y solo se probó con 3.
   - Arreglo: leer la definición y las columnas una vez; traer los Id ordenados en una sola consulta (hasta 10.001) e hidratar por lotes de 100. Agregar una prueba con unas 250 filas y una unitaria del borde de 10.000.
3. **Un `sortFieldId` ajeno da 500** (`FormRecordFilterPolicy.cs:14-15` y `Filters.cs:90-92`).
   - Escenario: `sort=number&sortFieldId=<otro>`.
   - Arreglo: 400 para cualquier `sortFieldId` desconocido, con su test.
4. **El esquema JSON se deserializa fila por fila** (`FormRecordReader.cs:21`, `FormRecordPresentationPolicy.cs:12-17`, `Filters.cs:29-30,92`).
   - Arreglo: usar los esquemas de la definición, deserializados una vez, en un diccionario por versión.
5. **Ordenar por una pregunta de elección arma un CASE por versión y opción**, con literales que cambian el SQL (`Filters.cs:106-121`).
   - Arreglo: una subconsulta que traiga el `ChoiceId` y un CASE sobre él, con parámetros. Agregar un test que ordene por elección.
6. **El Excel no tiene límite propio.** Comparte el cupo de lectura y mantiene todo en memoria.
   - Arreglo: una política de exportación con una sola exportación a la vez por usuario.
7. **Un 403 al exportar oculta toda la planilla** (front `FormResponsesPage.tsx:368`, `ResponseExportDialog.tsx:80-82`).
   - Arreglo: cerrar el diálogo y avisar que falta el permiso de exportar, sin quitar la lectura.
8. **El total del diálogo de exportar puede ser viejo** (`FormResponsesPage.tsx:150-155,365`). Además, la espera de un 429 se pierde al cerrar.
   - Arreglo: deshabilitar mientras los datos sean previos o con error, y llevar la espera al dueño de la página.
9. **Avisos duplicados.**
   - Detalle y planilla avisan por separado la misma falla.
   - La navegación muestra un toast en cualquier pantalla, y la barra y la lista comparten el id del aviso.
   - Reintentar aparece aunque no sea un error de red.
   - Arreglo: un solo dueño por pantalla, la navegación sin toast global y la acción solo para red.
10. **Anterior y Siguiente desmontan la página.** Pierden el foco y el título parpadea (`FormResponseDetailPage.tsx:126-131`).
    - Arreglo: conservar el detalle anterior mientras carga, con `aria-busy`.
11. **Las miniaturas se vuelven a bajar en cada visita** (`usePrivateFormFile.ts:10-11` con `gcTime: 0`). Con 100 filas son cientos de pedidos.
    - Arreglo: retenerlas mientras siga el ámbito de respuestas (la limpieza por dueño ya existe) y cargar solo las visibles.
12. **El panel de filtros, frente a T2.**
    - Hoy el pie no queda fijo, se abre una sección por vez, falta «Ver N respuestas» y las fechas del borrador no entran en los conteos.
    - Arreglo: pie fijo, todas las secciones, el conteo en el botón de aplicar y el mismo borrador para todo.
13. **`layouts` importa `app`** (`layouts/HomeLayout.tsx:5,19`). Arma un ciclo, suma respuestas al bundle inicial y pide la navegación en todas las pantallas.
    - Arreglo: componer el shell desde `app` y que `layouts` use solo el contexto neutral.
14. **Tests que faltan.**
    - Backend:
      - el permiso revocado durante la lectura en `ListAsync`, `CountFacetsAsync` y `GetAsync`;
      - encabezados del XLSX en inglés;
      - la navegación que excluye borradores (hoy no crea ninguno);
      - el EXPLAIN sobre el SQL que genera EF y no sobre uno escrito a mano.
    - Front:
      - coma decimal e inválidos al aplicar;
      - Anterior y Siguiente cruzando de página;
      - limpiar filtros privados al salir;
      - 403 y 429 al exportar;
      - un solo toast;
      - foco después de Siguiente;
      - ordenar por pregunta;
      - «Abrir conversación» sin `whatsapp.read`.

## Con la Etapa 4 o al tocar esos archivos

- **Backend:**
  - las facetas muestran la etiqueta de la versión más vieja;
  - un `FieldId` en las facetas de persona o versión descarta condiciones;
  - la faceta de versión se ordena como texto;
  - «phone» se traduce como WhatsApp dentro de BusinessForms;
  - `FormExportsController` usa el mapeo de otro controller;
  - controles de permiso repetidos en el export y en el origen de WhatsApp.
- **Front, frente a los tableros:**
  - chips sin valor;
  - falta «Columnas · N ocultas»;
  - falta «1 de 24» en el detalle;
  - falta «Ver todos (12)»;
  - T5 no lista los filtros aplicados;
  - «1 filtros aplicados»;
  - migas con el nombre de la navegación en lugar de `detail.name`.
- **Front, otros:**
  - un 404 de planilla con un Reintentar que no sirve;
  - el tipo de documento sale de la extensión y no de `contentType`;
  - el historial del navegador en el chat de WhatsApp;
  - pedidos de facetas de más;
  - código sin uso (`useCleanup`, la rama `presence`, claves `selected_*`, el shim de `FormReplyFiles`).
