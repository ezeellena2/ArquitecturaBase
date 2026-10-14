# Code review: Etapa 2, K6–K7 y correcciones del 13 — 2026-10-14

Revisión de lectura de backend `7567b966..db0052e7` y front `a390126..df9776e`. Se corrige en un solo chat, backend y front juntos, en el orden de abajo; cada arreglo con su test y commits chicos. Las decisiones del [resumen de la etapa](../plans/formularios/etapa-2-catalogos.md) no cambian: tampoco el «nunca se trunca».

## Antes del piloto con Meta

1. **La preparación entrega al operador todas las opciones** (backend `WhatsAppFormsController.Prepare` y front `api/forms.ts`).
   - Un operador con `whatsapp.send`, sin `users.read` ni `formcatalogs.read`, recibe nombres y detalles de hasta 200 personas. El diálogo solo usa la cantidad.
   - Arreglo: un DTO de WhatsApp con `{fieldId, label, count}` y los problemas. El contrato no está publicado: se cambia junto con el front y la fixture exportada.
2. **Una opción que no entra en WhatsApp bloquea todos los envíos y nadie sabe cuál es** (`FormCatalogOfferPolicy`, 30 caracteres de texto y 300 de detalle).
   - Manual acepta 200 y 500 al guardar.
   - Filters toma `DisplayName`, que admite 100: una persona con nombre largo bloquea el catálogo para todos.
   - Arreglo:
     - Manual valida 30/300 al guardar, en backend y front (`maxLength` y error junto al campo).
     - La vista previa de K4 marca las personas que no entran.
     - El problema al mandar dice cuántas opciones no entran y de qué catálogo.
3. **Texto vacío con la columna rol** (`FormCatalogFilterReader`). `TextColumn` admite `role` y `active`, y un usuario sin roles da `""`.
   - Arreglo: `TextColumn` solo admite `name`.
4. **Una pregunta de catálogo nueva no se puede guardar sin catálogo** (front `editorModel.ts:15`).
   - Manda `catalog: ""`, el backend responde 400 genérico y no marca la pregunta.
   - Arreglo: mandar `catalog: null` y agregar el test del alta completa (tipo → catálogo → guardar).
5. **Reintentos de captura acotados solo por cantidad** (`WhatsAppFlowReplyService`). Cinco fallas son unos 2,5 minutos: una caída de Redis de 3 minutos deja respuestas válidas en Rejected.
   - Arreglo: además, una antigüedad mínima antes de rechazar, con un test de caída.
6. **Error al preparar con dos dueños** (front `useFormComposer.ts:49-54`).
   - Sale un toast global y además el aviso del diálogo, cada uno con su propio Reintentar. Un 409 se presenta como falla de carga.
   - Arreglo: dueño local, como el listado de formularios, y el 409 se muestra como formulario bloqueado.
7. **«Mandar de nuevo» puede no hacer nada** (front `ChatThread.tsx`).
   - No mira la cuenta ni los formularios elegibles. Además, su reloj de 1 s redibuja toda la conversación.
   - Arreglo: que `useFormComposer` diga si se puede abrir, y quitar el reloj.
8. **Textos que no dicen la verdad.**
   - Dar de baja un catálogo también frena el envío de los formularios publicados que lo usan, y la confirmación no lo dice.
   - Después de un error de red o 5xx al guardar un catálogo, el aviso pide «revisar el estado», pero lo que corresponde es volver a guardar.
9. **`description: ""`** en las opciones sin detalle (`WhatsAppMessagePayload.cs:143`). Arreglo: omitir `description`.
10. **Tests que faltan.**
    - Backend:
      - un test HTTP con PostgreSQL y el origen real (Manual + Usuarios) que manda, y otro que bloquea por baja, por más de 200 opciones o por un texto largo;
      - unitarios de `FormCatalogPreparationPolicy`;
      - el golden r5 con una pregunta de catálogo;
      - `WhatsAppFlowReplySessionReaderTests.cs:46` consulta el mensaje propio, no el lote compartido.
    - Front:
      - el texto de cada motivo de rechazo, incluido `CatalogOptionInvalid`;
      - error y Reintentar de la preparación;
      - «Mandar de nuevo» oculto sin envío.
11. **Documentación.** `src/features/business-forms/AGENTS.md:52,57` y `docs/design/formularios-whatsapp.md:30` del front todavía dicen que K6 y K7 están pendientes.

## En el piloto

- Mandar al celular un formulario con una pregunta Manual y otra de Usuarios, una obligatoria y otra opcional vacía (`data-source: []`).
- Responder y recargar: comprobar la tarjeta con la opción y su catálogo, y el número de respuesta.
- Dar de baja una opción antes de responder: tiene que llegar el rechazo con «Mandar de nuevo».
- Un catálogo de unas 200 opciones con detalle, para ver que Meta acepta el tamaño. Si lo rechaza, se agrega un tope de bytes medido al preparar.

## Con la 2B o al tocar esos archivos

- **Front frente a los tableros:**
  - K7 en una línea con el destinatario y en amarillo;
  - K6 con el catálogo en la fila plegada, «según quien lo recibe», sin el punto suelto y con un vacío que no habla de búsqueda;
  - la cabecera del catálogo con «No se puede usar»;
  - la revisión D con el nombre del catálogo.
- **Bandeja de WhatsApp:** el corte pasó de `md` a `xl` sin tablero. Se vuelve a `md`, o se dibuja y se elige.
- **Backend de más:**
  - el lock global de definiciones tomado durante el SQL de Filters;
  - el lock de catálogo al enviar y el flag `forSending`;
  - el formulario se busca tres veces;
  - `HasCatalogs` deserializa el esquema por cada fila;
  - `FormCatalogErrors.Inactive` y `FormPublicationGuard` sin uso real.
- **Privacidad menor:**
  - redactar `ToString` en `StructuredFormPreparationIssue`, `FormRecordReferenceRow` y `StructuredRecordChoiceValue`;
  - la oferta sellada en `WhatsAppFlowSessions.ProtectedPayload` entra en la retención de la Etapa 3.
- **Limpieza del front:**
  - `lastLoaded` duplicado y la limpieza de `usePrivateCleanup` repetida;
  - las fixtures `catalogs-http/manual/*` con su `catalogHttpContract.test.ts`, y las capturas que solo se verifican por hash;
  - los tests de detalles irrelevantes (`flowToken`, contar textos y aserciones de escritura repetidas en 12 corridas visuales).
- **Review anterior:** sigue vigente la lista «Con K7 o al tocar esos archivos» del [review del 12](2026-10-12-code-review-etapa-2.md).

## Lo que está bien

- **Oferta sellada:** queda por sesión y viaja sin volver a consultar el catálogo. La pertenencia a la oferta se valida en el canal y en el sink.
- **Rechazos y respuestas:**
  - el rechazo por baja ocurre antes de numerar, sin número y con un aviso sin valores;
  - Filters no vuelve a consultar el origen al capturar;
  - la tarjeta usa el texto histórico y oculta los valores sin `formrecords.read`.
- **Compatibilidad:** los perfiles r3/r4 siguen mandando y capturando, y la huella es compatible con los recibos viejos.
- **Correcciones del 12:** las 1, 3, 4, 5, 7, 8 y 9 están completas y con test. La 2 está completa salvo el punto 5 de arriba, y la 6 es parcial (punto 3).
- **Reglas del front:** sin imports entre features, textos en es y en, y nada privado en la URL ni en el storage.
