# Code review: Etapa 2 (K1–K4) y salud de la Etapa 1 — 2026-10-12

Revisión de lectura de lo commiteado hasta backend `7567b966` y front `a390126`. Lo de K6 sin commitear no entra. Se corrige en el orden de abajo; cada arreglo con su test, commits chicos.

## Antes de K7

Afectan lo que K7 usa, o bloquean a quien usa la app.

1. **Etapa 1, publicación trabada (backend).** `FormPublicationChannelDispatchService.cs:30-33` pasa `UnsupportedConfiguration` y `PayloadMismatch` del Accept a Uncertain.
   - Con el perfil r5 de K6, una publicación aceptada bajo r4 que todavía no se confirmó queda reintentando cada 30 s para siempre, y ese formulario no se puede volver a publicar.
   - Arreglo: los dos terminan en Failed visible.
2. **Etapa 1, respuesta que falla siempre (backend).** Queda pendiente para siempre: ocupa el lote de 50 (con 50 así se frena la cola) y no se purga.
   - Arreglo: reintento acotado y después Rejected visible.
3. **Tests que dependen del orden (backend)**, la causa de las corridas completas que fallaron hoy:
   - la vista y la clave diferida de posiciones las crea un solo test, porque `EnsureCreated` no las crea;
   - `Assert.Single` sobre `active=false` en toda la base;
   - más de 200 usuarios devuelven `ExceedsLimit`.

   Arreglo: crear vista y constraint en el fixture del módulo, y acotar cada aserción a los datos propios del test.
4. **Origen de usuarios sin vista en la base (backend).** La vista sobre `AspNetUsers`, `AspNetUserRoles` y `AspNetRoles` frena cualquier migración futura del núcleo que cambie esas columnas, y corta el despliegue.
   - Arreglo, lo más simple: el lector del módulo arma el SELECT fijo con las columnas permitidas, y se quita la vista con una migración. El resumen de la etapa ya lo dice así.
5. **Filtro por rol guardado por nombre (backend).** Si se renombra el rol, el catálogo queda con 0 opciones y en K7 no se puede mandar. Además, un rol sin miembros no se puede elegir.
   - Arreglo: guardar el Id del rol y mostrar el nombre actual.
6. **Opciones con texto vacío (backend).** La vista previa y la lista incluyen usuarios sin nombre, y en K7 serían opciones vacías en WhatsApp.
   - Arreglo: excluirlas, como ya hace la muestra.
7. **Dar de baja y reactivar un catálogo (backend y front).** Está decidido en el resumen y no existe: `SetActive` no tiene ruta, así que el estado «Dado de baja» de K1 no se puede producir.
   - Arreglo: `isActive` en el PUT, con revisión y 409, y la acción en la pantalla del catálogo.
8. **Guardar después de un error de red o 5xx (front).** `useCatalogDraftEditor.ts:108` exige revisión, y la única salida es descartar todo.
   - Arreglo: reintentar está protegido por `expectedRevision`. Solo un 409 pide revisión.
9. **Persona de ejemplo en K4 (front).** Cada tecla cambia la clave de `useCatalogFilterOptions` y un Spinner reemplaza al editor; si falla, el editor desaparece.
   - Arreglo: conservar los datos anteriores, mostrar el Spinner solo en la primera carga y agregar debounce.

## Con K7 o al tocar esos archivos

**Backend:**
- **Validación y errores:**
  - omitir `isActive` en un ítem lo da de baja: hacerlo obligatorio;
  - intercambiar códigos entre dos ítems da 500;
  - la vista previa responde «catálogo no encontrado» cuando la persona no existe, no tiene topes de condiciones ni de largo y su timeout sale como 500;
  - OpenAPI no declara el 409 de filtros y declara uno que el alta manual no devuelve.
- **Lector:** devuelve `*Response` y decide el estado: tiene que devolver Rows y el servicio decide. Además busca por `Key` (el GUID).
- **Tests que faltan:** la gramática (columna no permitida, operador inválido, orden fuera de la lista) y el 403 en cada ruta.
- **Robustez de más:** un lock sobre un Id recién creado y un token de concurrencia redundante con la revisión. `Key` duplica el Id sin uso, y la gramática está definida en tres lugares.
- **Otros:** falta `OperationLog.RunAsync` en los servicios de catálogo, y `BusinessFormsManualAppPreviewTests` es un arnés de navegador puesto como test.

**Front:**
- **Avisos:**
  - el aviso de éxito al crear se borra al navegar;
  - «Más de 200 opciones» solo sale como un aviso de 8 s;
  - un 403 al guardar reemplaza el editor por «Sin permiso», en lugar de dejarlo en solo lectura;
  - se muestra «resultado incierto» cuando no se escribió nada.
- **K3 frente al tablero:** falta «Así lo ve el empleado», falta el segmentado Activas / Dadas de baja / Todas y falta «English · N distintas». El badge tiene que decir «Dada de baja».
- **Mover opciones:** las flechas usan un límite equivocado con la búsqueda activa.
- **Botones sin cambios:** Descartar pide confirmación sin cambios, Guardar sin cambios sube la revisión, y borrar el texto en inglés deja `""` en vez de `null`.
- **Limpieza:**
  - borrar las fixtures de la cohorte anterior a K4 y su test de rechazo;
  - borrar el código muerto (`filtersAvailable`, `comingSoon`);
  - `onViewUsage` navega al editor;
  - resolver la duplicación (`lastLoaded`, `CatalogListRow` y los tipos importados desde `components/`).

**Etapa 1, menores:**
- el chat muestra como «Unrecognized» una respuesta de más de 16 KiB que fue rechazada;
- una respuesta chica pero malformada se descarta sin rastro.

## Lo que está bien

- **Etapa 1:**
  - de los 12 hallazgos importantes del review anterior, 8 siguen arreglados con su test;
  - los parciales son 1 y 2 de arriba, y M6, ceros decimales, que ya estaba decidido: la precisión no conserva ceros finales;
  - la Etapa 2 no tocó WhatsApp, el envío, la captura ni la numeración.
- **Backend de catálogos:**
  - SQL desde una lista blanca y con parámetros, sin correo ni teléfono;
  - permisos en cada ruta;
  - una transacción por escritura y 409 por revisión;
  - el módulo no nombra WhatsApp ni IA.
- **Front de catálogos:**
  - piezas comunes y textos es/en;
  - el borrador queda fuera de la URL y de los cachés;
  - el 409 conserva el borrador;
  - los errores aparecen junto al campo.
