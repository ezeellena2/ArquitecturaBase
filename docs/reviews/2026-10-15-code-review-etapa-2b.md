# Code review: Etapa 2B, fotos y documentos — 2026-10-15

Revisión de lectura de backend `d33220fe..e441203` y front `5b2c25b..02532ce`. Se corrige en un solo chat, backend y front juntos, en el orden de abajo; cada arreglo con su test y commits chicos. Las decisiones del [resumen de la etapa](../plans/formularios/etapa-2b-fotos-y-documentos.md) no cambian. Los contratos de la etapa no están publicados: se cambian junto con el front y las fixtures exportadas, sin compatibilidad.

## Antes de cerrar la 2B

1. **Word o Excel dañado o cifrado** (`FileContentProcessor.cs:42`). El filtro no captura `FormatException` (OpenMcdf) ni `InvalidDataException` (`ZipArchive`).
   - Escenario: un .docx con contraseña o un PDF renombrado como .docx queda 15 minutos en «No hace falta hacer nada», bajando todo cada 30 s, y termina en `CaptureFailed`.
   - Arreglo: capturar las dos y rechazar con `Files.Content.InvalidType`. Los tests no lo detectan porque usan 10 bytes, así que hay que agregarlos con tamaño real.
   - En el mismo corte: `file_size` que no es número (`WhatsAppMediaClient.cs:34`) se revisa con `ValueKind`.
2. **El nombre de descarga lo elige quien manda** (`BusinessFormFileRecorder.cs:26-28`, `FormFileService.cs:34-35`).
   - Escenario: «acta.pdf.html», con un contenido que es PDF y HTML a la vez, se descarga como HTML. Tampoco se filtra U+202E, que invierte el texto.
   - Arreglo:
     - el nombre de descarga es la raíz saneada más la extensión del tipo verificado;
     - se quitan los caracteres de formato (categoría Cf);
     - el DTO expone ese nombre, y el front lo usa.
3. **Imágenes enormes** (`FileContentProcessor.cs:32-35`).
   - El tope de 32 MP se mide sobre la imagen ya escalada, así que un JPEG progresivo que declara 40.000×40.000 puede pedir GB de memoria.
   - Arreglo: rechazar por el ancho, el alto y el área de `codec.Info` antes de decodificar.
   - Los PNG que llegan como documento solo se validan por la cabecera: no se decodifican, y un escaneo grande no se rechaza como «tipo no permitido».
4. **Una respuesta trabada frena todas las capturas cuando Meta está lento** (`WhatsAppFlowReplyService.cs:31-46,162-181`).
   - Se procesa en serie, con 30 s por archivo, y la respuesta trabada va primera en cada lote.
   - Arreglo: un plazo total por respuesta, de 60 a 90 s, y saltear en la vuelta las que fallaron hace poco, con `FlowReplyFirstFailureAtUtc` y el contador que ya existen.
5. **Las miniaturas se vuelven a bajar y agotan el cupo de Formularios.**
   - Front: `usePrivateFormFile.ts:10-11` usa `staleTime:0` con los refetch encendidos, así que el visor y cada vuelta a la pestaña piden todo de nuevo.
   - Backend: todo comparte `forms-read`, con 120 pedidos por minuto (`BusinessFormsApiRegistration.cs:16`).
   - Arreglo:
     - en el front, `staleTime: Infinity`, sin refetch y conservando `gcTime:0`, con un test que cuente pedidos;
     - en el backend, una política propia para archivos con más cupo.
6. **El mismo error se avisa varias veces.** La query de archivos no tiene `errorOwner`, así que sale un toast por archivo. En el visor, el error sale como toast y además como `role="status"`.
   - Arreglo: dueño local y un único aviso por respuesta.
7. **`api.blob` fuerza `Accept: application/octet-stream`** (`httpClient.ts:121-124`). Con eso, los errores llegan como texto plano: un 429 sin `retryAfter` y un 500 sin `traceId`.
   - Arreglo: no sobrescribir el Accept, y probar con 404 y 429.
8. **Limpieza de huérfanos cuadrática.** `PrivateFileCleanupService.cs:16-17` hace una consulta por blob, con un OR sobre `ThumbnailKey`, que no tiene índice.
   - Arreglo: consultar por página del listado con `= ANY(@keys)`, y deducir la miniatura de la clave o indexarla.
9. **Visor y miniaturas en el front.**
   - Al perder el permiso con el visor abierto, el foco cae en `<body>`. Arreglo: llevarlo a la tarjeta o al aviso.
   - Una miniatura que falla muestra «No se p…» recortado, y su `aria-label` tapa el estado. Arreglo: un placeholder visual y el estado en el nombre accesible.
10. **Lo que el tablero F3/F4 muestra y hoy no llega.**
    - Los rechazos de archivo no nombran la pregunta.
    - La tarjeta «bajando» no dice cuántos archivos llegaron, y sin nombre queda «Formulario».
    - El pie del visor no tiene la fecha.
    - Arreglo: ampliar el DTO con lo que el backend ya tiene.
11. **Textos.**
    - «Podés mandar el formulario de nuevo» aparece sin permiso de envío.
    - El catálogo vacío no distingue una búsqueda sin resultados.
    - «0 MB» para archivos chicos: mostrar KB.
    - «Ver 1 fotos más»: usar los plurales `_one`/`_other`.
12. **Mínimo y máximo en el editor** (`FileQuestionSettings.tsx:14-15`). `Number()` convierte el campo vacío en 0 y acepta 2.5 o -1.
    - Arreglo: enteros acotados, y el error del grupo también marcado en Máximo.
13. **Un blob faltante responde 500** (`FormFileService.cs:26`). Arreglo: responder 404.
14. **Tests que faltan.**
    - Backend: lo de 1 a 3 con tamaños reales, nombres con comillas, no ASCII y U+202E, y un selector opcional vacío de punta a punta.
    - Front:
      - descarga de documento;
      - pedidos de red al abrir el visor y al volver a la pestaña;
      - «+N» con 5 y 8 fotos;
      - foco tras perder el permiso;
      - `formScreens` como función pura;
      - tipos habilitados con `form-files`;
      - `api.blob` con ProblemDetails.

## En el piloto

- Además de la foto con cámara y el PDF, una **pregunta de archivo opcional que se deja vacía**: no está comprobado qué manda Meta. Con `null` el webhook descarta la respuesta, y con `""` se rechaza entera. Si falla, se corrige con lo que Meta mande de verdad.

## Con la Etapa 3 o al tocar esos archivos

- **Backend de más:**
  - el doble chequeo de permiso en `FormFileService`;
  - `MarkDownloadingAsync`, que repite lo que ya marca el webhook;
  - validaciones de archivos duplicadas en `FormPublicationGuard` y `FormPublicationChannelVerifier`;
  - el modo de credencial `VisualStudio`, que no se usa.
- **Tope por archivo:** la captura usa el valor vigente y no el sellado al publicar, y el cliente de medios fija 25 MiB. Hay que alinearlos.
- **Front de más:**
  - el Escape propio del visor (Radix ya cierra), la prop `downloading` y el export `FormReplyFile` sin uso;
  - `recordId?` y `actor=""` opcionales;
  - `DownloadRequest` como componente con `useQuery`: alcanza con `fetchQuery`;
  - `savePrivateBlob` va a `shared/lib`;
  - la fixture duplicada `reply-reloaded.json`.
- **Accesibilidad y F1:**
  - el foco en botones que se deshabilitan (vista previa y visor);
  - «Descargar» sin el nombre del archivo;
  - el resumen plegado «solo cámara · de 1 a 4»;
  - el contador que no marca cuando se pasa de 10.

## Lo que está bien

- **Claves y guardado:** claves determinísticas sin el nombre del archivo, todo verificado antes de subir y la subida termina antes del commit. El recibo con su huella evita redescargas y saltos de numeración.
- **Acceso:**
  - la pertenencia entre respuesta y archivo da 404;
  - `nosniff` en las imágenes y los documentos siempre como descarga;
  - sin URL públicas ni SAS, y el token solo en el header.
- **Descarga de Meta:** el token va solo a hosts permitidos, sin redirecciones, y la lectura es en streaming con tope.
- **Arquitectura:** Azure, SkiaSharp y OpenMcdf quedan confinados a `Infrastructure/Files`. Las features del front no se importan entre sí.
- **Compilador:** una pregunta de archivo por pantalla y el tope de 10 controlado en tres capas. Los perfiles r3, r4 y r5 conservan su JSON y su hash.
- **Front:**
  - las object URLs se revocan;
  - el original se pide solo al abrir el visor;
  - textos es/en con voseo;
  - sin almacenamiento, los tipos se ven pero no se eligen.
