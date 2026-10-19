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

## Contraste previo con código y decisiones — 2026-10-19

Lectura de ambos repositorios antes de modificar producto. La lista contiene grupos de hallazgos y cobertura; no son veinte defectos nuevos independientes.

| Punto | Resultado del contraste y alcance |
| --- | --- |
| 1 | Confirmado: el decoder del chat rechaza mapas secundarios ausentes/vacíos y exige la cultura de la cuenta antes de preparar. No hay corrección posterior del piloto en Git. |
| 2 | El bloqueo existe, pero el arreglo propuesto cambia la decisión vigente: sólo texto/detalle de ítems tienen respaldo individual; la metadata de catálogos mantiene sus reglas. `FormCatalogPreparationPolicyTests.A_catalog_without_the_chosen_original_name_is_unpreparable_instead_of_mixing_or_throwing` acredita ese rechazo intencional. Se conserva. |
| 3 | Ya protegido: `CompleteForRecordAsync` lee `NameJson`/`SchemaJson` capturados y `CompleteAsync` comprueba etiquetas, ayudas y opciones. Falta la regresión específica del rol con opción secundaria incompleta; no una corrección productiva. |
| 4 | Confirmado para contenido histórico: el respaldo por mapa mezcla traducciones parciales; el preview del chat puede carecer de nombre. Resolver cultura por versión sin cambiar los rótulos del sistema ni los sellos. |
| 5 | Ya protegido: `WhatsAppAssistantAccessPolicy.RevalidateAsync` compara la cultura capturada con la actual, dentro del límite de aceptación manual y automática. Agregar regresión específica, sin otra revisión almacenada. |
| 6 | Confirmados el error bilingüe obsoleto y los textos orientativos imprecisos; ajustar etiquetas I1. Retirar `wrapLabels` sólo si deja de ser necesario a 320 px. |
| 7 | Confirmados anuncio repetido, avisos duplicados y lista sin límite. |
| 8 | Confirmado: la preparación bloquea antes de interpretar el rechazo; el cierre ya conserva la guarda al ofrecer. |
| 9 | Confirmado: el cliente compartido aplica el límite decimal del audio a archivos que admiten 25 MiB. |
| 10 | Confirmada la restricción activa compartida entre chats manuales; es recuperable, no pérdida definitiva. Acotar el arreglo sin eliminar cuotas ni replay. |
| 11 | Confirmado: `AggregateException` previa al POST no recibe la clasificación de contenido no disponible. |
| 12 | Confirmado en Asistente con cuenta desvinculada/inactiva; Manual ya copia sin transcribir. |
| 13 | Confirmado el cómputo de C4 sin envoltorio. La lectura general parcial y la prohibición de inferir ausencia están decididas y documentadas; quitar opciones observadas requiere otro contrato autorizante, no es una corrección mecánica. |
| 14 | Confirmado: elección con formato inválido descarta los otros valores válidos. La pertenencia de GUID debe seguir siendo obligatoria. |
| 15 | Confirmados los límites de texto/tamaño, clasificación HTTP, privacidad de propuestas, contadores y trabajo innecesario. Orden y cupo deben preservar rechazo, copia privada y retiro de interpretación anterior. No bloquear relatos legítimos antes del modelo por contener hechos personales. |
| 16 | Confirmados aislamiento de retención, puerto con efecto y requisito de PostgreSQL. El reloj real del coordinador responde al TTL real de Redis; no sustituir esa garantía por avances ficticios. El cambio de idioma entre envío/captura es cobertura faltante, sin bug productivo demostrado. |
| 17 | Confirmado: el estado usa `outline` fijo. |
| 18 | Confirmado: una falla pasajera del GET aborta un POST que puede no haber sido aceptado y deja su identidad marcada. |
| 19 | Confirmados el foco al reseleccionar y la copia manual incondicional del selector. |
| 20 | Cobertura faltante de 1/7/17/18; se incorpora con esos arreglos, sin duplicar producto. |

El registro de [verificación y producción](../plans/verificacion-y-produccion.md#formularios-idioma-original-y-traducción-opcional) no contiene un piloto físico nuevo: conserva el respaldo P24 observado el 2026-10-18 y los recorridos ES/EN pendientes. Se incluye su garantía en las regresiones locales y se mantiene abierto el piloto real.

### Avance de las correcciones

Pendiente. Por corte se ejecutan sólo pruebas rápidas del área; Docker, contratos HTTP exportados, suites completas, visuales y navegador quedan para el cierre conjunto.

- **P1 cerrado localmente:** front `208951c` admite secundarios ausentes/vacíos y conserva la cultura efectiva estricta en recibos/historial; 179 pruebas rápidas, TypeScript y Oxlint focal en verde. Backend `7934eeee` prepara ocho capturas HTTP del recorrido con cuenta inglesa y original español; build de integración sin advertencias. Ejecución/exportación con Docker quedan para el cierre.
- **P2 contrastado, sin cambio de regla:** 12/12 pruebas rápidas de `FormCatalogPreparationPolicyTests` en verde, incluido el rechazo del nombre necesario ausente. Se conserva el contrato de metadata y la excepción individual sólo de ítems.
- **P3 verificado:** la nueva teoría de aviso a otro integrante del rol con una opción capturada sin traducción pasa en es/en con el código existente; 21/21 rápidos de `FormActionRunRecorderTests`. No se agregó una segunda política de idioma.
- **P4, tarjeta del chat:** TDD de nombres ausentes/parciales en es/en; el detalle autorizado toma nombre y valores de la misma proyección del dueño. Sin valores autorizados conserva el nombre de la sesión enviada. 26/26 rápidos de `WhatsAppChatQueryActionPrivacyTests` en verde; sellos y permisos intactos. Continúa el resto de lecturas históricas.
