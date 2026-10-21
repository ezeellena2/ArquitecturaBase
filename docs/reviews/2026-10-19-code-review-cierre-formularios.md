# Code review: correcciones de la Etapa 5 y cambio de idioma — 2026-10-19

Revisión de lectura de backend `ca40328b..209cde90` y front `93d8160..0c72a76`: los 24 arreglos del [review de la Etapa 5](2026-10-18-code-review-etapa-5.md) y el [cambio de idioma](../features/formularios.md#idioma-original-y-traducción-opcional--2026-10-19).

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

Los 20 puntos quedaron contrastados y resueltos con el alcance de la auditoría previa. Cada corte tuvo sus pruebas rápidas; la verificación completa se registra al final. Las decisiones vigentes se conservan.

- **P1 cerrado:** front `208951c` admite secundarios ausentes/vacíos y conserva la cultura efectiva estricta en recibos/historial; 179 pruebas rápidas, TypeScript y Oxlint focal en verde. Backend `7934eeee` y `f36956fb` exporta ocho capturas HTTP con cuenta inglesa y original español, usando una cuenta propia por escenario. Los consumidores reales de lista, preparación, recibo e historial pasaron en el cierre.
- **P2 contrastado, sin cambio de regla:** 12/12 pruebas rápidas de `FormCatalogPreparationPolicyTests` en verde, incluido el rechazo del nombre necesario ausente. Se conserva el contrato de metadata y la excepción individual sólo de ítems.
- **P3 verificado:** la nueva teoría de aviso a otro integrante del rol con una opción capturada sin traducción pasa en es/en con el código existente; 21/21 rápidos de `FormActionRunRecorderTests`. No se agregó una segunda política de idioma.
- **P4 cerrado:** backend `e1e41c72`, `be67532a` y `b326336e`, más tarjeta `98e4f4da`. Culturas publicadas leídas del manifiesto por lote, contenido coherente en planilla/detalle/Excel/cadena/aprobación y SQL antes de paginar. La tarjeta autorizada toma nombre y valores de la misma proyección del dueño; sin valores autorizados conserva el nombre de la sesión enviada. TDD de nombres ausentes/parciales en es/en; 880/880 rápidos (854 de BusinessForms y 26 de WhatsApp), 157/157 de arquitectura y solución/integración compiladas sin advertencias; peer final limpio. Sellos y permisos intactos. Escenarios HTTP/SQL verificados con PostgreSQL en el cierre; la procedencia de las capturas incluye el nuevo helper de publicación del fixture.
- **P5 verificado sin cambio productivo:** `a423650b` agrega regresiones públicas manuales y automáticas es/en. Cambiar la cuenta impide aceptar la propuesta anterior, sin crear sesión, salida ni otro Submit; vínculo/modo permanecen iguales. 135/135 rápidos de Assistant, 157/157 de arquitectura y build sin advertencias, peer limpio.

- **P6 cerrado:** el diagnóstico de traducción pide completar el texto para publicar ese idioma, con código estable y descripción técnica coherentes. Front `7d97c5b3` aplica las etiquetas I1 y presenta la vista previa como orientativa; conserva el ajuste de rótulos necesario en pantallas angostas. Backend: 30/30 rápidos de publicación por idioma, 88/88 de recursos y build sin advertencias. Front: 81/81 rápidos, TypeScript y Oxlint focal en verde; peer limpio. Navegador y capturas a 320 px acreditan las etiquetas en dos renglones: se conserva wrapLabels.

- **P7 cerrado:** front 2d840c22 deja el anuncio final sólo en el toast y el estado persistido con sus idiomas. I2/I3 usan un aviso por idioma, hasta tres faltantes, el resto contado y la acción de completar según permiso. 112/112 rápidos, TypeScript y Oxlint focal en verde; peer limpio. El cierre agrega `be0824e`: el aviso común conserva ancho útil y baja la acción al faltar espacio, con regresión geométrica y capturas es/en a 320/390 px.

- **P8 cerrado:** c5990ea9 permite interpretar el rechazo a la segunda oferta aun con el cupo agotado; la aceptación sigue impidiendo una tercera oferta. C4 conserva su control previo y recuperación. TDD, 179/179 rápidos Assistant, 157/157 de arquitectura y build sin advertencias; peer limpio. No cambian capacidades, esquemas ni instrucciones de IA.

- **P9 cerrado:** el cliente de medios admite 25 MiB para Flows; la copia privada de audio conserva 25.000.000 bytes. TDD de ambos bordes y cuerpo mayor que su cabecera: 19/19 técnicos sin Docker, 12/12 rápidos de almacenamiento de audio, 157/157 de arquitectura y build sin advertencias; peer limpio.

- **P10 cerrado:** d3677a7d excluye sólo NormalizeAudio de la reserva activa por actor/canal; conserva unicidad por conversación, cuotas, replay y cancelación propia. Migración aditiva con rechazo atómico del Down si hay trabajo no representable. TDD, 288/288 rápidos de IA/audio, 157/157 arquitectura, build sin advertencias y modelo EF coherente; peer limpio. Los cuatro casos PostgreSQL se verifican en el cierre.

- **P10, interpretación Manual completada — 2026-10-21:** `efe3b3e8` agrega el ámbito neutral de conversación a la reserva de interpretación: distintos contactos del mismo operador avanzan sin compartir la exclusión, incluidos los que no tienen Subject. Conserva cuotas por actor/canal/propósito, huella y recuperación/cancelación propias; no atribuye un contacto a turnos anteriores. Build 0 advertencias/errores, Application 2489/2489, dominio 10/10, técnicos 24/24 y arquitectura 161/161; modelo EF sin cambios pendientes y peer limpio. La integración Assistant/Manual de tres chats (`c1f139be`, confirmación durable en `64303a4b`) se verificó con PostgreSQL y Redis: retiene el primer proveedor y comprueba que los otros dos envían y confirman sus propuestas propias antes de liberarlo. La [regla y el alcance de esa evidencia](../features/whatsapp.md#aislamiento-entre-conversaciones--2026-10-21) conservan cuotas y replay; no se hizo piloto con Meta. El cierre completo se registra junto con Menú y detalle.

- **P11 cerrado:** toda excepción no cancelada al adquirir/leer/cerrar el archivo privado devuelve ContentUnavailable antes de STT; el POST queda fuera de ese catch. TDD de AggregateException al abrir y leer, cancelación durante apertura: 14/14 técnicos, 37/37 contratos STT sin Docker, 157/157 arquitectura y build sin advertencias; peer limpio. Perfil y transporte calificados intactos.

- **P12 cerrado:** 1538e25f conserva la copia de audios nuevos en Assistant sin vínculo/cuenta activa y ante desvinculación durante descarga; sólo después marca Unavailable, sin RequestId/contexto IA. La adquisición temporal mantiene retry y las solicitudes revocadas conservan cero I/O. TDD, 56/56 rápidos de audio, 157/157 arquitectura y build sin advertencias; peer limpio.

- **P13 cerrado:** d76210f1 comparte el cómputo del envoltorio real. C4 excedido devuelve ReadUnavailable/Failure sin recortar respuestas, opciones ni mapeos; una propuesta excedida sigue inválida. El descubrimiento general conserva su parcialidad explícita y la autoridad de opciones observadas. TDD, 859/859 rápidos BF, 19/19 IA, 88/88 recursos, 157/157 arquitectura y build sin advertencias; peer limpio. Definiciones, esquemas e instrucciones intactos, sin reevaluación del modelo.

- **P14 cerrado:** abb3a605 omite sólo elecciones sin formato GUID D y conserva los otros valores. Un GUID ajeno/vacío, tipo o identidad discrepantes siguen rechazando la propuesta; Execute y ParseOutput verifican la salida estricta. TDD, 868/868 rápidos BF, 157/157 arquitectura y build sin advertencias; peer limpio. No cambia el contrato calificado de IA.

- **P15 cerrado:** 29d6e572, fd6b0b9b, 7a5d8255 y eb8d6b9b; front ac9bf1d4. Candidatas y C4 respetan grafemas; una candidata inválida conserva las demás con lectura parcial. La privacidad se valida en propuesta/captura sin filtrar relatos, con fechas y montos válidos. Audio conserva copia, consulta cupo, recupera UUID previos, completa en la misma vuelta y expone sólo el motivo seguro tooLarge; reinicia fallas al guardar. retryable queda interno. TDD, 916/916 rápidos BF, 647/647 WhatsApp, 18/18 Domain Audio, 48/48 técnicos HTTP/pipeline/texto, 157/157 arquitectura; front 70/70, TypeScript y lint focal verdes; build sin advertencias y peer limpio. HTTP con Docker verificado en el cierre; capacidades, esquemas e instrucciones IA intactos.

- **P16 cerrado:** lectura de permisos separada de la invalidación, con aliases scoped de la misma implementación y guardas de arquitectura en ambas direcciones. Retención limpia sólo sus filas en finally; la integración cambia la cuenta después del envío y comprueba la cultura sellada al capturar. Mantiene el reloj real del TTL Redis y elimina el retry exterior. PostgreSQL mínimo 16 documentado. TDD, 57/57 rápidos del área, 159/159 arquitectura y build sin advertencias; peer limpio. Casos PostgreSQL/Redis y de composición scoped verificados en el cierre.

- **P17 cerrado:** front 64e95a2d reutiliza approvalStateVariant en la columna Estado, sin cambiar la composición D2. TDD de los cuatro estados, 80/80 rápidos, TypeScript y lint focal verdes; peer limpio.

- **P18 cerrado:** front a1cc4eb5 conserva el POST de transcripción aún no aceptado ante un GET500 del hilo autorizado, sin abortar ni emitir otro. GET403/POST403 retiran el alcance completo; cambio de dueño y salida conservan sus guardas. TDD, 31/31 rápidos, TypeScript y lint focal verdes; peer limpio.

- **P19 cerrado:** front 736f01f4 pide foco al activar de nuevo la fila seleccionada en planilla y Por aprobar, sin remontar el panel ni apropiarse del foco de ordenamiento/filtros. Flechas, Escape, retorno y foco interior quedan cubiertos. La frase de atención manual queda sólo junto al aviso de IA no disponible. TDD, 124/124 rápidos, TypeScript y lint focal verdes; peer limpio.

- **P20 cerrado:** la cobertura de P1/P7/P17/P18 incluye mapas secundarios ausentes/vacíos, anuncio único, faltantes acotados, color de Estado y ciclo del POST de transcripción ante fallas transitorias o pérdida de alcance. Los ocho cuerpos HTTP de cuenta inglesa con original español o secundario vacío tienen consumidores reales de lista, preparación, recibo e historial; el exporter exige las 74 capturas de WhatsApp Forms y registra la fuente del helper de versión. Las 19 cohortes se exportaron desde la corrida completa verde y sus consumidores pasaron en el cierre.

## Cierre — 2026-10-19

- Backend sobre `d61ae244`: build sin errores ni advertencias y **4.903/4.903 tests** completos con Docker, sin fallos ni omitidos. Las 19 cohortes (326 capturas y 55 fuentes) se exportaron conjuntamente; WhatsApp Forms incluye sus 74 capturas y las ocho variantes de original español para cuenta inglesa. La navegación conserva filtro, orden, conteo y paginado en PostgreSQL.
- Front de código `be0824e` y capturas `44e464b`: build y lint completos verdes, **2.424/2.424 tests en 228 archivos** y **527/527 visuales**, sin actualizar referencias ni tolerancias. Los ocho cuerpos HTTP nuevos pasan por los consumidores de lista, preparación, recibo e historial; las 19 cohortes conservan bytes, SHA256 y procedencia.
- Navegador con SPA de producción y API/PostgreSQL/Redis/Azurite de prueba: I1–I3 publica original español, inglés completo y pérdida de inglés en versiones 1/2/3; preview entera, aviso único y acción legible a 320/390 px. D2 conserva composición, colores, momentos, foco al reseleccionar, orden externo, flechas y Escape; rechazo con otra cuenta y motivo registrado. Selector manual sin frase redundante y aviso de IA pausada comprobados; consola sin errores ni avisos.
- Audio en navegador: una cuenta con lectura y sin envío escucha la copia y ve la transcripción sembrada. La solicitud del audio nuevo se aceptó y persistió con UUID y actor de esa cuenta; el preview no ejecuta una transcripción nueva. Los hosts retiraron su estado y quedaron detenidos; viewport y disponibilidad restaurados, idioma de prueba en español y modos sin cambios. `aspire stop` terminó sin AppHost corriendo.
- La auditoría estructural de documentación pasó. No se repitió la evaluación OpenAI: capacidades, esquemas, instrucciones y perfiles calificados no cambiaron. El piloto físico de idioma, incluido P24 con faltante exclusivo de aviso o PDF, sigue [pendiente](../plans/verificacion-y-produccion.md#formularios-idioma-original-y-traducción-opcional). No se alteraron versiones, sesiones, bindings ni PDF sellados de la instalación real.
