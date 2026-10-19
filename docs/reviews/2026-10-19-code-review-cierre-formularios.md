# Code review: correcciones de la Etapa 5 y cambio de idioma — 2026-10-19

Revisión de lectura de backend `ca40328b..209cde90` y front `93d8160..0c72a76`: los 24 arreglos del [review de la Etapa 5](2026-10-18-code-review-etapa-5.md) y el [cambio de idioma](../plans/formularios/idioma-de-los-formularios.md).

No hay huecos de seguridad. Estos puntos están completos y con test real: 3, 5, 6, 8, 9, 12, 14, 15, 16, 17 a 21 y 24.
- **Idioma:** se resuelve antes de elegir el binding y se vuelve a comprobar dentro de la transacción; a Meta van solo los idiomas completos; no se recompila nada sellado; la migración es aditiva.

Se corrige en un solo chat, backend y front juntos, cada arreglo con su test y commits chicos. Las decisiones no cambian. Lo que encuentre el piloto de idioma se suma acá.

## Arreglar ahora

**Idioma**

1. **El chat no lista ni manda un formulario publicado solo en el original a una cuenta en inglés.** Los esquemas exigen `name` y `submitLabel` en el idioma de quien recibe, con `min(1)` en cada texto (front `whatsapp/api/forms.ts:13,18`, `api/formMetadata.ts:2-3`). Un `"en": ""` rompe además la lista, el recibo del envío y la tarjeta del historial.
   - Arreglo:
     - aceptar mapas en los que falte o esté vacío el secundario y exigir solo algún nombre no vacío;
     - mostrar el nombre con respaldo al original;
     - un ejemplo HTTP exportado con una cuenta en inglés y una versión solo en español;
     - reescribir `api/forms.test.ts:82-84`.

   Si ya se corrigió durante el piloto, alcanza con verificarlo.
2. **Un catálogo propuesto con el nombre solo en otro idioma deja el formulario sin poder mandarse** (`FormPresentationCulturePolicy.cs:18-25,61-62`, `FormCatalogProposalService.cs:47`).
   - Arreglo: que el nombre del catálogo no bloquee el idioma original, como la excepción de los ítems, con un test.
3. **Los avisos a un rol mezclan idiomas cuando la versión se publicó solo en el original:** `CompleteForRecordAsync` no mira los textos de la versión capturada.
   - Arreglo: cruzar los idiomas completos con los de la versión (sus etiquetas y opciones), con un test de `NotifyRole` en inglés y una opción sin traducir.
4. **Planilla, detalle, Excel, aprobaciones y la tarjeta del chat mezclan idiomas** o salen sin nombre en versiones con la traducción parcial (`FormRecordPresentationPolicy.Text`, `FormResponsePresentationPolicy`, `FormResponseReader.Text` y `WhatsAppChatQueryService.cs:84-88`).
   - Arreglo: si la versión no publicó el idioma de la pantalla, mostrarla entera en el original, sin mezclar.
5. **Un cambio de idioma de la cuenta entre la propuesta del asistente y su aceptación no se detecta** (`WhatsAppAssistantAcceptancePolicy.cs:67` y `WhatsAppFormSendPolicy.cs:48,101`).
   - Arreglo: guardar la revisión del destinatario del turno, o volver a preparar con el idioma actual.
6. **Textos:**
   - `Forms.Publication.MissingTranslation` todavía dice «Completá … en español e inglés antes de publicar».
   - Hay que aplicar las etiquetas del tablero I1: «Español · original» y «English · 3 sin traducir». Si a 320 px no hace falta, quitar `wrapLabels` del componente común.
   - La vista previa dice «El formulario llega…», porque los avisos y el PDF pueden cambiar el idioma del envío.
7. **Al publicar.**
   - El anuncio sale dos veces: el renglón de estado y el toast. El renglón vuelve a ser un estado con los idiomas.
   - En I3, perder el inglés muestra dos avisos: queda solo el de atención, con la lista.
   - En I2, la lista de faltantes no tiene tope: mostrar unos pocos más «y N más», con «Completar» en la acción del aviso.

**Asistente y audios**

8. **El rechazo de la segunda oferta del día nunca se registra:** con el cupo en 2, el «no, gracias» queda `Blocked` sin interpretarse, y al día siguiente vuelve a ofrecer lo rechazado (`WhatsAppAssistantDispatchService.cs:64,135-140`).
   - Arreglo: mirar el cupo antes de ofrecer, no antes de interpretar.
9. **El tope de 25.000.000 bytes rompe archivos de Flows** que el selector y el registro aceptan (25 MiB) (`WhatsAppMediaClient.cs:13`).
   - Arreglo: el cliente de medios en 25 MiB y el tope de 25.000.000 solo en la copia de audio.
10. **En atención manual, un operador tiene una sola operación de IA en todos sus chats:** el índice es `(OwnerUserId, ChannelId)` y el dueño es el operador.
    - Arreglo: incluir el contacto o el sujeto en la clave del canal manual, o sacar de esa unicidad la normalización de audio.
11. **Punto 11 incompleto.** Al agotar los reintentos, Azure lanza `AggregateException`, que sigue terminando «incierta» (`LlmConversationClient.cs:53-59`).
    - Arreglo: en el tramo previo al POST, toda excepción no cancelada pasa a `ContentUnavailable`, con un test que lance `AggregateException`.
12. **Punto 13 incompleto:** en modo Asistente, un audio con la cuenta desvinculada o inactiva no se copia (`WhatsAppAudioDispatchService.cs:24-26,37-41`).
    - Arreglo: descargarlo y guardarlo siempre, y marcarlo `Unavailable` después.
13. **Lecturas que se pasan del tope:**
    - La lectura de sugerencias (C4) corta en 16 KiB sin envoltorio y falla el turno (`BusinessFormSuggestionAssistantCapabilitySource.cs:83-86`).
    - Con catálogos grandes, la lectura de candidatos queda parcial para siempre, siempre con los mismos formularios afuera, y apaga los pedidos faltantes (`BusinessFormAssistantCapabilitySource.cs:89-118`).
    - Arreglo: el mismo `ResponseBytes` con envoltorio y recortes; no mandar las opciones de catálogo en el descubrimiento; y medir la completitud de los pedidos faltantes sobre la lista de formularios.
14. **Punto 7 incompleto para elección:** un valor que no es un GUID descarta toda la propuesta (`FormAssistantObservationPolicy.cs:39-43`).
    - Arreglo: omitir solo ese valor; un GUID ajeno a la lectura se sigue rechazando.
15. **Menores con efecto:**
    - **Tope del editor:** texto medido en UTF-16 contra grafemas en el editor; excluir la candidata en lugar de tirar la lectura.
    - **Cupo y audios:** en Asistente, con el cupo agotado, los audios se siguen transcribiendo; mirar el cupo antes.
    - **Marca interna en HTTP:** `retryable` sale en la respuesta de Inicio; no exponerlo.
    - **Orden de procesamiento:** el audio en Asistente tarda una vuelta más; volver al orden anterior.
    - **Pedidos faltantes:**
      - rechazan fechas y montos, y no rechazan `＠` ni las variantes sin tilde del nombre;
      - el control corre recién al aceptar, cuando el modelo ya se pagó.
    - **Contadores de descarga:** los de falla no se reinician al guardar el audio.
    - **Pedido de transcripción:** acepta un audio que supera el tope; rechazarlo con su motivo.
16. **Tests y otros:**
    - el caso `activeTurn=true` de `AiAssistantRetentionTests.cs:101-114` deja un turno en la base compartida;
    - `IPermissionService` está entre los puertos permitidos y tiene un método con efecto: separar la lectura;
    - la migración de cuotas exige PostgreSQL 16 (`IS JSON OBJECT`): dejarlo documentado en el despliegue;
    - integración con el reloj inyectado y sin `Task.Delay` (`WhatsAppFormLanguageIntegrationTests.cs`);
    - cambio de idioma de la cuenta entre el envío y la captura.

**Front**

17. **Punto 22 incompleto:** el Estado de la planilla no tiene color (`FormResponsesPage.tsx:144`). Usar `approvalStateVariant`.
18. **Un audio puede quedar «Transcribiendo…» para siempre:** una falla pasajera del hilo aborta el POST y no se vuelve a pedir (`useChatAudioTranscription.ts:19-24`).
19. **Detalles:**
    - tocar la fila que ya está abierta deja el teclado en la tabla;
    - «La conversación sigue en atención manual» solo va con «IA no disponible».
20. **Tests del front:**
    - lista y recibo con una versión de un solo idioma o un secundario vacío;
    - un solo anuncio al publicar;
    - el aviso de atención con la lista de faltantes;
    - el color del Estado;
    - el POST de transcripción abortado.
