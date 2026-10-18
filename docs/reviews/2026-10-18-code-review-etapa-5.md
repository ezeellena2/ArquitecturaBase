# Code review: Etapa 5 y detalle D2 — 2026-10-18

Revisión de lectura de backend `540d7b6f..7e365708` y front `400f342..5c19ad6`.

- **Bien resuelto:** el ADR 0020 se cumple en lo central. Las capacidades no escriben y lo verifica un test de arquitectura; las propuestas se validan contra las lecturas del turno; el contexto lo arma el servidor; una respuesta tardía después de tomar Manual no sale; los perfiles de modelo son exactos; el catálogo propuesto es solo Filters e inactivo hasta aprobarlo; no hay contenido en los logs.
- **D2:** sin problemas en el backend.
- **Lo que falla:** la disponibilidad del asistente con uso real, su costo y la retención.

Se corrige en un solo chat, backend y front juntos, cada arreglo con su test y commits chicos. Las decisiones del resumen retirado (`git show 7e365708^:docs/plans/formularios/etapa-5-asistente.md`) no cambian. Lo que encuentre el piloto en curso se suma acá.

## Arreglar ahora

**Backend: disponibilidad del asistente**

1. **Un solo turno activo por persona en todos los canales.** El índice único de `AiAssistantTurnConfiguration.cs:26` y `AiAssistantSubmissionPolicy.cs:86` hacen que un segundo mensaje, un audio mientras se interpreta un texto, Inicio en paso o dos sugerencias del operador queden `Blocked` para siempre (`ActiveTurn`), y el turno superado sigue corriendo y se cobra.
   - Arreglo:
     - `ActiveTurn` y `CapacityLimited` son transitorios: la referencia queda Pending y se reintenta;
     - la unicidad es por persona y canal, salvo Inicio;
     - el turno superado se cancela, sumando la cancelación al puerto, que también usa «Tomar atención manual»;
     - un test con el motor real.
2. **La lectura de candidatos cae entera y deja al asistente sin funcionar** (`BusinessFormAssistantCapabilitySource.cs:44-50`, `FormAssistantCandidatePolicy.cs`, `FormMissingRequestReader.cs:29-31`, tope de 16 KiB y de 101).
   - Escenarios: muchos temas de pedidos (también se leen los descartados), varios formularios con «Cuándo lo ofrece», o un formulario con un catálogo vacío para esa persona.
   - Arreglo:
     - excluir el formulario que no se puede preparar (inaccesible es como ausente, ya está decidido);
     - no leer los temas descartados;
     - acotar o paginar;
     - que una lectura que falla no aborte el turno entero.
3. **Las ofertas se rompen para siempre en los usuarios más activos:** `WhatsAppAssistantReader.cs:131-141,199-205` lista sin ventana, con `Take(101)`, y la capacidad exige como máximo 100.
   - Arreglo: una ventana reciente, los rechazos como un conjunto de definiciones y verificar el registro de origen (C4) por el contexto del servidor.
4. **Costo y cuotas.** Cada «ok» o «gracias» en modo Asistente llama al proveedor, aunque ya se usaron los 2 ofrecimientos del día (`WhatsAppAssistantDispatchService.cs:43-53`, el tope se mira recién en `WhatsAppAssistantAutomaticPolicy.cs:149`). Además comparte la cuota diaria con Inicio, y un audio consume dos operaciones.
   - Arreglo: mirar el cupo de ofrecimientos antes del Submit, y separar o calibrar las cuotas por canal.
5. **Fallas no transitorias que se reprocesan en cada vuelta hasta la retención** (`WhatsAppAssistantDispatchService.cs:47-48,67-69`): chat de más de 24 horas, cuenta desactivada o un destino de sugerencia con catálogo inactivo (que además tira toda la lista, `FormSuggestionCandidatePolicy.cs:41`). Un audio en modo Asistente puede quedar Pending para siempre (`WhatsAppAudioDispatchService.cs:25`).
   - Arreglo: guardar la referencia `Blocked` o marcar procesado, excluir el destino que no se puede preparar y dejar el audio Unavailable con su código.
6. **Always ofrece la primera candidata aunque no sea elegible** (`WhatsAppAssistantDispatchService.cs:71`).
   - Arreglo: filtrar las que forman ciclo y las rechazadas antes de elegir.

**Backend: propuestas, audio y retención**

7. **Un solo prellenado inválido descarta toda la propuesta y corta el turno** (`FormAssistantObservationPolicy.cs:38-39`). Ejemplo: «1.500,50» o «18/10/2026».
   - Arreglo: descartar solo ese valor y declarar el formato en el esquema de la capacidad.
8. **Un mapeo de elección a elección se guarda pero nunca prellena** (`FormSuggestionPolicy.cs:51`, `FormSuggestionMappingPolicy.cs:37-38`).
   - Arreglo: aceptarlo solo si las dos preguntas usan el mismo catálogo; si no, 400 en `mappings[i].source`.
9. **Los temas de pedidos pueden guardar datos de terceros** (`FormMissingRequestService.cs:124-129`). Ejemplo: «Llegadas tarde de Carlos Gómez 11-5555-1234». Además hay falsos positivos: «Ana» dentro de «mañana».
   - Arreglo: rechazar de forma determinística secuencias largas de dígitos, `@` y URL, y comparar por palabra completa.
10. **Un audio largo se paga y se pierde.** No hay control de duración. El texto de más de 4000 caracteres se rechaza después de cobrar, y una nota de 20 minutos supera el plazo de 30 s y queda incierta.
    - Arreglo: un tope de bytes o de duración antes del Submit, y alinear el cliente de medios (25 MiB) con el almacenamiento (25.000.000).
11. **Una falla del almacenamiento al normalizar el audio queda «incierta»,** aunque no se mandó nada al proveedor (`LlmConversationClient.cs:37-54`, `AiAssistantCoordinator.cs:68,106-111`).
    - Arreglo: mapearla a `ContentUnavailable`, con un test con un almacenamiento que falla.
12. **Las conversaciones de IA de los canales nunca se borran** (`AiAssistantRepository.cs:71-75`). Cada mensaje en modo Asistente deja filas para siempre, con actor y permisos.
    - Arreglo: borrar las que no son de Inicio cuando vencieron sus turnos y recibos, como la retención de los mensajes.
13. **En atención manual el audio no se guarda como en la 2B:** para escucharlo hay que pedir la transcripción, que cuesta IA, y si vence en Meta se pierde (`WhatsAppAudioReader.cs:23`).
    - Arreglo: guardar la copia privada al recibirlo, aparte de la normalización.
14. **El catálogo propuesto no va en atención manual.** Hoy se ofrece la capacidad: el modelo se paga y la sugerencia queda Blocked.
    - Arreglo: no ofrecer `forms.catalog-filters.*` en ese canal.
15. **Menores con efecto:**
    - el tope diario falla el día del cambio de horario (`WhatsAppAssistantAutomaticPolicy.cs:146-148`: usar `IsInvalidTime`);
    - el perfil de modelo tiene un Id por defecto (`home@1`: exigirlo);
    - el tope de 12 herramientas ya está lleno y no se valida al aceptar (validarlo en `BuildAsync`);
    - la descarga del audio se reintenta sin espera (usar la escala de la captura de Flows);
    - el blob de audio se vuelve a bajar en cada vuelta mientras se transcribe;
    - la retención del contenido limpia como máximo 500 por día (repetir lotes).
16. **Tests y casos:**
    - el test de arquitectura del ADR 0020 decide por nombre: usar una lista de permitidos, con casos negativos;
    - casos de evaluación de los canales con inyección (pedir otro formulario, otro destinatario o saltear permisos);
    - `ActiveTurn`, cuota y capacidad con el motor real;
    - volumen para los topes de 16 KiB y 101;
    - el despacho de audio contra PostgreSQL;
    - el test de retención de audio, que nunca guarda una transcripción;
    - los temas de pedidos sin compartir datos entre tests.

**Front**

17. **Por aprobar con el panel abierto:** en `/formularios/por-aprobar/:taskId` las migas dicen «Diseñar formularios» y ningún ítem del menú queda activo (`layouts/navigation.ts:89`, `Sidebar.tsx:689`).
    - Arreglo: que la bandeja reconozca sus rutas hijas, con un test.
18. **En Por aprobar, cada ↑/↓ desmonta el panel:** el foco salta y las miniaturas se vuelven a bajar (`ApprovalInboxPage.tsx:42-43,100,114`).
    - Arreglo: conservar el detalle anterior mientras carga.
19. **Clic en otra fila con el panel abierto:** ↑, ↓ y Escape dejan de andar (`ResponsePanelFrame.tsx:20,26-34`).
    - Arreglo: enfocar el panel al cambiar de respuesta, o manejar las teclas desde el contenedor.
20. **Panel en escritorio y celular.**
    - En escritorio tapa la cabecera de la página y «Exportar» (`FormResponsesPage.tsx:198`): anclarlo debajo.
    - En el celular no es modal: falta `inert` en la planilla y `aria-modal`, tapa la barra de la app, falta «3 de 24» y el número se corta a 320 px.
21. **H1 y H3.**
    - «Tomar atención manual» va como acción principal en el lugar del compositor, con «El asistente atiende esta conversación…», y el modo como insignia en la cabecera.
    - El aviso «IA no disponible» sale como toast cada vez que se abre un chat: va en línea, junto al control.
22. **Momentos de D2.**
    - «Por aprobar» aparece dos veces.
    - Faltan el ícono de estado por momento y el renglón resaltado mientras espera.
    - Faltan las variantes de éxito, atención y peligro en el Badge común.
23. **Tests del front:**
    - migas y menú en la bandeja;
    - ↑/↓ en la bandeja conservando detalle, foco y miniaturas;
    - clic en otra fila y después teclado;
    - foco contenido en el celular;
    - canal y frecuencia del aviso de disponibilidad;
    - sin repetir el POST de transcripción tras una falla pasajera.

**Hallazgo del piloto real: idioma**

24. **El formulario no comparte el respaldo al idioma original con los avisos y el PDF.** En el [piloto físico](../features/formularios.md#piloto-del-asistente-y-audios--2026-10-18), con la cuenta en inglés y sin el nombre inglés del PDF, `VISITA-000006` recibió el formulario y su apertura en inglés, aunque los avisos y el PDF salieron en español.
    - Arreglo: resolver una cultura completa común antes de ofrecer o enviar el formulario; si falta una traducción usada, formulario, apertura, avisos y PDF salen enteros en el idioma original, tanto en Asistente como en revisión manual y sugerencias. No exigir traducciones nuevas ni modificar la publicación bilingüe de las Etapas 1–3.
    - Regresión: traducción completa e incompleta, incluyendo un faltante solo en reglas o PDF, con valores y opciones de catálogo; comprobar el mismo idioma del formulario enviado y de las acciones posteriores.

## Avance de correcciones

- **1 corregido:** unicidad por persona/canal salvo Inicio, cancelación por contexto sellado y recuperación de conflictos transitorios con el mismo UUID. Una fuente nueva o Manual cancelan la interpretación superada; H6 conserva STT aceptado. Rojo previo y 763 tests rápidos del área, 156 de arquitectura y 24 de H2/configuradas en verde. Migración y regresiones PostgreSQL preparadas para el cierre conjunto; sin cambios HTTP ni de front.

- **2 corregido:** candidatos preparables y temas pendientes, lectura acotada por cantidad y bytes con marca de parcialidad. Las fallas recuperables quedan observadas sin autoridad y no descartan las otras lecturas; autorización, validación y cancelación siguen siendo terminales. Rojo previo y 935 tests rápidos de Formularios/IA en verde; regresión PostgreSQL del lector preparada para el cierre. Cambiaron los esquemas de capacidades: se reevaluarán los canales al final.

- **3 corregido:** ofertas de las últimas 24 horas, hasta veinte referencias y rechazos duraderos consultados por definiciones, dentro de 16 KiB. C4 toma el origen exacto del contexto del servidor y sólo ofrece sus capacidades en una captura; no enumera el historial. Rojo previo y 1506 unitarios, 156 de arquitectura y 16 consultas técnicas en verde; arnés offline con código 0. APIIntegration compilable y prueba de historial PostgreSQL preparada para el cierre; sin cambios HTTP.

- **4 corregido:** el cupo de dos ofrecimientos se consulta antes de crear trabajo automático nuevo, con la zona del negocio; replay y aclaración conservan la operación aceptada. Una referencia Pending sin cupo sólo consulta: NotFound la bloquea sin Submit, mientras Pending/Completed conocidos se recuperan. Las cuotas del actor se separan por canal e interpretación/audio mediante metadata durable en el recibo; instalación y capacidad siguen globales. Rojo previo, 795 unitarios de WhatsApp/IA, 14 pruebas del pipeline en memoria y 13 pruebas de arquitectura en verde. Migración aditiva y casos PostgreSQL de aceptación, backfill y atribución desconocida preparados para el cierre; sin cambio de capacidades o instrucciones.
- **5 corregido:** las fuentes nuevas con fallas permanentes quedan procesadas y salen del escaneo, sin impedir recuperar referencias existentes. Los destinos no preparables se excluyen individualmente; los audios automáticos inválidos quedan Unavailable con código y Manual conserva H6. Rojo previo, 1300 unitarios de WhatsApp/Formularios, 51 pruebas de arquitectura focal y 18 consultas técnicas sin base en verde. La regresión PostgreSQL de recibos equivalentes marcados está preparada para el cierre. Sin cambios HTTP, de capacidades ni de instrucciones; integración completa pendiente del cierre conjunto.
- **6 corregido:** Always automático recorre el orden configurado y excluye ciclos y definiciones rechazadas antes de elegir; Manual conserva la primera para confirmación humana. Si ninguna sirve, mantiene el recorrido Assistant/NoReply existente y la aceptación revalida los rechazos tardíos. Rojo previo y 25 pruebas focales de sugerencias configuradas más 25 de arquitectura en verde; sin cambios de capacidades o instrucciones.

- **7 corregido:** un formato escalar inválido se omite sin perder el resto del prellenado; las identidades, tipos y opciones ajenas siguen rechazándose. El esquema compartido declara decimal, fecha, booleano y opción canónicos. Rojos previos, incluida la entrada real con valores vacíos/no persistibles, y 732 pruebas rápidas de Formularios en verde. El esquema cambiado queda incluido en la reevaluación de canales del cierre; sin cambios HTTP ni de front.

- **8 corregido:** choice sólo se mapea entre preguntas del mismo catálogo explícito, al guardar y al preparar; los rechazos se asocian a `.source`. La web ofrece únicamente esas correspondencias y conserva la metadata canónica. Rojos previos, backend 139/139 y front 22/22, TypeScript/lint focal limpios; peer sin pendientes. Front `370fdf7`, sin cambios HTTP ni edición de s2.

- **9 corregido:** temas sin `@`, URL ni secuencias largas de dígitos de terceros, con separadores de teléfono incluidos. Los nombres conocidos se comparan por palabra completa y con normalización Unicode. Diez rojos previos y 48 pruebas rápidas de pedidos faltantes en verde; sin cambios de capacidades, instrucciones o HTTP.

- **10 corregido:** normalización de audio WhatsApp acotada a 1.000.000 bytes antes del trabajo IA, conservando la copia reproducible hasta 25.000.000. El cliente de medios usa esa misma cota decimal de almacenamiento. Tres rojos previos, 51 unitarios de audio y 16 contratos técnicos HTTP sin Docker en verde. Perfil STT e instrucciones intactos; sin atribuir duración o latencia a la cota.

- **11 corregido:** fallas conocidas al abrir o leer el archivo privado se devuelven como ContentUnavailable antes de STT; el cierre de la reserva queda fallido conocido, sin incertidumbre. Dos rojos previos por almacenamiento Azure y 22 pruebas técnicas rápidas de audio/coordinación en verde, sin Docker ni proveedor real. El transporte facturable, perfiles e instrucciones permanecen iguales.

- **12 corregido:** las conversaciones de canales se eliminan al vencer contenido y recibos, sin turnos activos ni retirada explícita. Inicio conserva su política anterior. Rojo previo y prueba de traducción SQL en verde sin abrir una base; seis regresiones PostgreSQL (canales, contenido/recibo vivo, turno activo e Inicio) preparadas y compiladas para el cierre conjunto.

- **13 corregido:** todo audio Manual conserva una copia privada al recibir, sin usuario vinculado, permisos de transcripción ni pedido/contexto IA. La copia queda reproducible y sale del escaneo hasta una petición explícita. Dos rojos previos, 52 unitarios y 2 consultas técnicas SQL en verde; lectura PostgreSQL de copia pendiente/ya guardada preparada y compilada para el cierre.

- **14 corregido:** Atención manual no ofrece las capacidades de catálogo H4; el canal automático las conserva y C4 sigue su catálogo acotado. Un rojo previo y 129 pruebas rápidas del asistente en verde. El catálogo ofrecido cambió y se incluye en la reevaluación real de canales del cierre; Home y STT intactos.

- **15 corregido:** cupo diario con medianoche inexistente, Id de perfil explícito y máximo de doce herramientas con terminal, tras permisos. Cuatro rojos previos y 25 pruebas de cupo más 196 de IA en verde; 4 pruebas técnicas de opciones sin base en verde. Segundo corte: espera de descarga con la escala de Flows antes de Take y polling del sello privado sin reabrir el blob ni repetir UUID. Dos rojos previos y 53 unitarios de audio en verde; siete consultas/bordes técnicos en verde sin base. La retención repite lotes de 500 hasta cero con scope/UoW nuevos; rojo previo y dos pruebas técnicas de scheduler sin base en verde. Los seis menores quedaron cubiertos, sin cambiar perfiles reales ni instrucciones.

## Al tocar esos archivos

- **Historial del navegador:** push al abrir el panel y replace al moverse con ↑/↓.
- **`tab=pdf`:** que no sobreviva al pasar a una respuesta sin PDF.
- **Columna PDF:** con IconButton y tooltip.
- **Pedidos de más:**
  - el efecto de transcripción corre de más (`onChanged` inline, un GET por cada POST, POST repetido de los Pending);
  - las sugerencias se vuelven a pedir con cada guardado del borrador.
- **Textos:**
  - plurales de `audio.waiting` y `missing.totals`;
  - `mode.manualDescription` de la era Bot;
  - «Confirmar» genérico al tomar Manual;
  - falta «La conversación sigue en atención manual»;
  - la pestaña Fotos vacía y visible sin preguntas de foto;
  - Reintentar sin decir qué falló;
  - una respuesta de otro formulario tiene que decir «no disponible».
- **Duplicación:** la lógica de mover entre respuestas está copiada en la planilla y en la bandeja (hacer un hook común), y a s2 le falta «Así lo ve el empleado».
- **Docs:** `docs/features/administracion.md:23` todavía lista `whatsapp.manage`.
