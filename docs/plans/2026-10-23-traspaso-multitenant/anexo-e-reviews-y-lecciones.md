# Anexo E · Code reviews, pilotos y lecciones desde el 2026-10-17

Traspaso 2 de «Formularios por WhatsApp» a ArquitecturaBaseMutitenant. Corte: 2026-10-23 a las 20:42, backend `d7f1cad7` y front `ed1a62d`, ambos en `main`. Todo sale del estado commiteado (`git show HEAD:…` y `git log`); no se evaluaron cambios sin commitear.

**Para qué sirve.** El multitenant reconstruye el producto, y lo que más importa es que no repita los errores. Este anexo junta lo que encontraron los code reviews y los pilotos desde el 2026-10-17. Para cada hallazgo cuenta el escenario, la causa raíz, el arreglo y su estado, y al final lo convierte en una lista de control.

**Qué no repite.** Las lecciones hasta el 2026-10-17 están en el [06 · Lecciones](https://github.com/ezeellena2/ArquitecturaBaseMutitenant/blob/main/docs/plans/2026-10-17-formularios-traspaso/06-lecciones.md) y en el [anexo C §3](https://github.com/ezeellena2/ArquitecturaBaseMutitenant/blob/main/docs/plans/2026-10-17-formularios-traspaso/anexo-c-reglas.md) del traspaso anterior. Acá se citan solo cuando un hallazgo nuevo las confirma o las corrige. La lista de la sección 5 complementa la del 06, y el capítulo [05 · Calidad](05-calidad.md) de este traspaso la resume.

## Cómo leerlo

| Sección | Qué tiene |
| --- | --- |
| [1 · Resumen](#1-resumen) | Los números y lo que más importa. |
| [2 · Por review](#2-por-review) | Cada hallazgo de los siete reviews: gravedad, escenario, causa, arreglo, regresión y estado. |
| [3 · Por piloto](#3-por-piloto) | Qué se probó con proveedores reales, qué falló y qué se aprendió. |
| [4 · Errores recurrentes](#4-catálogo-de-errores-recurrentes) | Los hallazgos agrupados por tipo, con ejemplos, causa común y cómo se evita. |
| [5 · Lista «no repetir»](#5-lista-de-verificación-no-repetir) | Qué revisar en cada etapa del multitenant antes de darla por cerrada. |
| [6 · Huecos](#6-huecos-de-este-anexo) | Lo que este anexo no pudo verificar. |

**Reviews** (los códigos siguen los del traspaso anterior):

| Código | Review | Rango revisado | Estado al corte |
| --- | --- | --- | --- |
| R1017 | Etapa 4. Retirado: `git show 7e365708^:docs/reviews/2026-10-17-code-review-etapa-4.md` | back `e07f1db8..ff1dc7b8`, front `69e2ba8..5aa6318` | Cerrado el 2026-10-18 |
| R1018 | [Etapa 5 y detalle D2](../../reviews/2026-10-18-code-review-etapa-5.md) | back `540d7b6f..7e365708`, front `400f342..5c19ad6` | Cerrado el 2026-10-19 |
| R1019 | [Correcciones de la Etapa 5 y cambio de idioma](../../reviews/2026-10-19-code-review-cierre-formularios.md) | back `ca40328b..209cde90`, front `93d8160..0c72a76` | Cerrado el 2026-10-19; P10 completado el 2026-10-21 |
| R1021a | [Dar de baja, reactivar y eliminar](../../reviews/2026-10-21-code-review-dar-de-baja.md) | back `3156d486..b7a7188e`, front `5db98e8..65ed60a` | Cerrado el 2026-10-21 |
| R1021b | [Menú y detalle, y aislamiento del asistente](../../reviews/2026-10-21-code-review-menu-y-detalle.md) | back desde `29d3bc64`, front desde `d040fda` | Cerrado el 2026-10-21 |
| R1022 | [Catálogos E1](../../reviews/2026-10-22-code-review-catalogos-e1.md) | back `e871e292..4a4c283e`, front `0365ec5..738300c` | Cerrado el 2026-10-22 |
| R1023 | [Catálogos E2](../../reviews/2026-10-23-code-review-catalogos-e2.md) | back `66900db2..dab9c281`, front `9f0d270..e02d1b4` | En corrección |

Los reviews de las Etapas 2 y 2B (12, 14 y 15 de octubre) se cerraron antes del 17 y ya están en el traspaso anterior.

**Gravedad.** R1021b, R1022 y R1023 la declaran (Alta, Media, Baja). R1017, R1018, R1019 y R1021a no la declaran, y tampoco los puntos 34–39 de R1023. En esos casos la asigna este anexo, con asterisco (Alta\*), según el efecto que describe el review:
- **Alta:** deja algo sin funcionar, o pierde o expone datos;
- **Media:** guarda o muestra mal, o cuesta de más;
- **Baja:** detalles, textos y pruebas.

**Estado.**
- **Corregido:** hay commits que lo arreglan (`b:` backend, `f:` front).
- **Contrastado, sin cambio:** el review proponía algo que ya estaba protegido o que contradecía una decisión vigente.
- **En corrección:** hay commits parciales.
- **Abierto:** ningún commit lo cita al corte.
- **Después:** el review lo dejó fuera de la tanda.

En R1023, «Corregido» quiere decir que hay commits con sus tests rápidos. Todavía no corrió el cierre conjunto: la suite completa con Docker, las cohortes exportadas y las visuales.

## 1. Resumen

- **142 hallazgos en siete reviews:** R1017 tuvo 18, R1018 24, R1019 20, R1021a 16, R1021b 10, R1022 15 y R1023 39. Al corte, de R1023 hay 12 corregidos, 4 en corrección y 23 abiertos.
- **Lo que más se repitió:**
  - leer números sin la cultura: «1.500» terminó como 1,5 en E1 y otra vez en E2, y «1.500,50» tiraba una propuesta entera en la Etapa 5;
  - el front y el back con reglas distintas para lo mismo;
  - cambiar una definición (tipo, fuente, papel, baja) y dejar datos huérfanos;
  - topes desalineados entre capas;
  - arreglos incompletos: R1019 encontró cuatro arreglos de R1018 a medias y dos que rompieron otro camino.
- **Lo que solo encontró el uso real:**
  - el piloto del 2026-10-18 encontró el idioma mezclado entre formulario, avisos y PDF;
  - el del 2026-10-20, que un envío Manual sin prellenado no abría en Android;
  - la calificación con OpenAI encontró un esquema que el modo estricto rechaza;
  - el usuario, al usar la app el 2026-10-23, encontró los puntos 34–39 de R1023. Uno de ellos: el chat de Inicio no mostraba la respuesta desde por lo menos el 2026-10-09.
- **Verde no es correcto.** E2 cerró localmente con 6.266 tests de backend, 3.116 del front y 719 visuales en verde. El review encontró después 33 puntos, cuatro de gravedad alta, y el uso sumó 6 más.
- **Sin prueba física al corte:** baja, reactivación y eliminación; menú y detalle; contactos simultáneos; catálogos E1 y E2. Todo quedó para el [piloto único posterior a E2](../verificacion-y-produccion.md#formularios-piloto-único-con-meta-después-de-e2).
- **Commits sin el porqué.** Desde el 17/10 hay 149 commits `fix` sin coautoría de Claude (82 del back y 67 del front), del período en que programaba Codex. Ninguno tiene cuerpo: la causa de cada arreglo solo está en el review. Los de Claude, desde el 2026-10-23, sí la explican.

## 2. Por review

### 2.1 R1017 · Etapa 4 (cerrado el 2026-10-18)

Al corte del traspaso anterior estaban cerrados los puntos 1 a 3, y del 4 al 18 seguían abiertos (anexo C §4.5). Se cerraron todos entre la noche del 17 y el 18 de octubre. La verificación conjunta está en el [cierre local de la Etapa 5 y del review de la Etapa 4](../../features/formularios.md#cierre-local-de-la-etapa-5-y-del-review-de-la-etapa-4): backend `59c03587` con 4.480 tests, front `7ecabd6` con 2.245 tests y 511 visuales. El escenario y la causa de cada punto ya están en el anexo C §3 del traspaso anterior; acá va solo cómo se cerró.

| # | Hallazgo | Cómo se cerró |
| --- | --- | --- |
| 1 | Unas pocas acciones «Sin confirmar» frenaban el motor de reglas | El worker drena el lote y aísla las fallas; una acción incierta espera un pedido explícito. Test del bucle real. |
| 2 | «Sin confirmar» no tenía salida | Revisión humana durable, «salió» o «no salió»; consultar nunca reenvía. |
| 3 | El catálogo Manual mezclaba idiomas en la copia fija | Textos del ítem por idioma al capturar; PDF y avisos eligen una cultura completa. |
| 4 | Un miembro sin WhatsApp tumbaba «Avisar a un rol» | Resultado por persona; un rol vacío o una falla individual no bloquean. |
| 5 | Una aprobación pedida a un rol borrado quedaba colgada | Se revalida el rol al reservar, dentro del lock: `Forms.Rule.RoleUnavailable`. |
| 6 | La vista previa del PDF no mostraba fotos y daba 500 sin nombre | Foto ficticia en la vista previa; el nombre entra en la completitud. |
| 7 | Arreglos de la Etapa 3 incompletos | Sin `SchemaJson` ni `NameJson` por fila, `sort` con espacios da 400 y el 429 lleva `retryAfter`. |
| 8 | «Sin respuesta» sobre una pregunta inexistente | La misma regla en reglas, SQL y conteos. |
| 9 | Errores generales como párrafos, a veces repetidos | Aviso común con un id por operación (f: `4ef7825`). |
| 10 | Rutas de error `rules[0]` contra `rules.0` | Normalización de rutas y la fixture exportada consumida (f: `764d68f`). |
| 11 | El panel de decisión desaparecía al decidir | Se mantiene mientras exista la tarea, con el 409 y el foco (f: `e3533e3`). |
| 12 | PDF en un iframe con sandbox | Visor nativo sin sandbox, con descarga (f: `a9848c0`). Edge nunca se recorrió. |
| 13 | Detalle e historial frente a G4 y G5 | Cabecera con estado y acciones, e historial cronológico (f: `cb242bb`). |
| 14 | Reglas frente a G1 | Condiciones en palabras, Guardar deshabilitado con su motivo explicado, 409 recuperable y foco (f: `2b27e0d`). Ese texto explicativo es uno de los que [R1023 #38](#27-r1023--catálogos-e2-en-corrección) manda sacar. |
| 15 | Conteo de filtros | Aplicar no depende del conteo; Reintentar solo ante red, 5xx y 429 (f: `02c43cc`). |
| 16 | Textos | f: `3f085b6`. |
| 17 | `origin` rechazado por el esquema estricto del front (encontrado en el piloto) | Sumado al contrato con su fixture (f: `bcf7824`). |
| 18 | Faltaban recorridos de punta a punta | Tres recorridos HTTP: captura → tarea → decisión → PDF → avisos (f: `400f342`). |
| — | Los emojis salían como cuadrados en el PDF | Fuente emoji embebida (b: `5d8040eb`). |

### 2.2 R1018 · Etapa 5 y D2

Del 2026-10-18; se cerró el 2026-10-19 ([cierre](../../reviews/2026-10-18-code-review-etapa-5.md#cierre--2026-10-19)):
- 4.672 tests de backend con Docker;
- 2.304 del front y 511 visuales;
- la reevaluación real de los canales con OpenAI pasó 30 de 30 casos y 434 comprobaciones.

**Lo bien resuelto:** el ADR 0020 se cumplía en lo central:
- las capacidades del asistente no escriben, y lo verifica un test de arquitectura;
- las propuestas se validan contra las lecturas del turno;
- el contexto lo arma el servidor.

**Lo que fallaba:** la disponibilidad con uso real, el costo y la retención.

| # | Grav. | Qué pasaba | Causa raíz | Arreglo y regresión | Estado |
| --- | --- | --- | --- | --- | --- |
| 1 | Alta\* | Un segundo mensaje, un audio mientras se interpretaba un texto, Inicio en paso o dos sugerencias del operador quedaban `Blocked` para siempre, y el turno superado seguía corriendo y se cobraba. | Índice único de turno activo por persona en todos los canales (`AiAssistantTurnConfiguration.cs:26`). Además, `ActiveTurn` y `CapacityLimited` se trataban como definitivos. | Unicidad por persona y canal, salvo Inicio; los dos códigos pasan a transitorios, con reintento del mismo UUID; el turno superado se cancela. Test con el motor real. b: `d92f99ac`, `455e2d33`. | Corregido |
| 2 | Alta\* | La lectura de candidatos caía entera y dejaba al asistente sin funcionar: con muchos temas de pedidos (también se leían los descartados), con varios «Cuándo lo ofrece» o con un catálogo vacío para esa persona. | Lectura sin acotar contra un tope de 16 KiB y 101, y una sola falla abortaba el turno. | Excluir lo no preparable, no leer descartados, acotar por cantidad y bytes con marca de parcial; una falla recuperable no aborta. b: `2f22ab60`. | Corregido |
| 3 | Alta\* | Las ofertas se rompían para siempre en los usuarios más activos. | `WhatsAppAssistantReader` listaba sin ventana con `Take(101)`, y la capacidad admite hasta 100. | Últimas 24 horas, hasta 20 referencias y rechazos por definición, dentro de 16 KiB. b: `fe63361d`. Después, la calificación real exigió quitar `uniqueItems` (b: `b9c41e37`, ver [3.3](#33-calificación-real-de-la-ia-2026-10-18)). | Corregido |
| 4 | Media\* | Cada «ok» o «gracias» en modo Asistente llamaba al proveedor, aunque ya se hubieran usado los 2 ofrecimientos del día. La cuota se compartía con Inicio y un audio consumía dos operaciones. | El cupo se miraba recién después del Submit. | Mirar el cupo antes; cuotas por canal y por propósito. b: `9fed438c`. R1019 #8 encontró que este arreglo rompía el rechazo de la segunda oferta. | Corregido |
| 5 | Media\* | Fallas no transitorias se reprocesaban en cada vuelta hasta la retención: chat de más de 24 horas, cuenta desactivada, destino con un catálogo inactivo (que además tiraba la lista entera). Un audio podía quedar Pending para siempre. | Ningún estado final para lo permanente. | Marcar procesado, excluir el destino no preparable y dejar el audio `Unavailable` con su código. b: `ea8ab617`. | Corregido |
| 6 | Media\* | Always ofrecía la primera candidata aunque no fuera elegible. | Se elegía antes de filtrar. | Excluir ciclos y rechazadas antes de elegir. b: `e0d5e27b`. | Corregido |
| 7 | Media\* | Un solo prellenado inválido, como «1.500,50» o «18/10/2026», descartaba toda la propuesta y cortaba el turno. | Validación todo o nada, y el esquema no le declaraba al modelo el formato esperado. | Omitir solo ese valor; el esquema declara decimal, fecha, booleano y opción canónicos. b: `3f49a8ea`. R1019 #14: quedó incompleto para elección. | Corregido |
| 8 | Media\* | Un mapeo de elección a elección se guardaba, pero nunca prellenaba. | Se aceptaban mapeos entre catálogos distintos. | Solo entre preguntas del mismo catálogo; si no, 400 en `mappings[i].source`. b: `c361ce98`, f: `370fdf7`. | Corregido |
| 9 | Alta\* | Los temas de pedidos podían guardar datos de terceros, como «Llegadas tarde de Carlos Gómez 11-5555-1234». Había falsos positivos: «Ana» dentro de «mañana». | Sin control determinístico; comparación por subcadena. | Rechazar secuencias largas de dígitos, `@` y URL; comparar por palabra completa con normalización Unicode. Un repaso encontró que los guiones Unicode (U+2010, U+2011, U+2013, U+FF0D, U+2212) escapaban al control. b: `8c43f7ff`, `b860b575`. | Corregido |
| 10 | Media\* | Un audio largo se pagaba y se perdía: no había control de duración, el texto de más de 4000 caracteres se rechazaba después de cobrar y una nota de 20 minutos superaba el plazo de 30 s y quedaba incierta. | Ningún tope antes de la llamada paga. | Tope de 1.000.000 bytes antes del trabajo de IA. b: `f2624e9f`. R1019 #9: alinear el cliente de medios rompió los archivos de Flows. | Corregido |
| 11 | Media\* | Una falla del almacenamiento al normalizar el audio quedaba «incierta», aunque no se hubiera mandado nada al proveedor. | Toda excepción se clasificaba como incertidumbre del proveedor. | `ContentUnavailable` antes del POST. b: `7d9e9fc1`. R1019 #11: quedaba afuera `AggregateException`. | Corregido |
| 12 | Media\* | Las conversaciones de IA de los canales nunca se borraban, con actor y permisos. | La retención solo cubría Inicio y los mensajes. | Borrarlas al vencer contenido y recibos. b: `d365781c`. | Corregido |
| 13 | Media\* | En atención manual el audio no se guardaba: escucharlo costaba una transcripción y, si vencía en Meta, se perdía. | La copia privada dependía de la transcripción. | Copia privada al recibir. b: `c8b56660`. R1019 #12: faltaba en Asistente sin cuenta activa. | Corregido |
| 14 | Baja\* | En atención manual se ofrecía la capacidad de proponer catálogos: el modelo se pagaba y la sugerencia quedaba `Blocked`. | Capacidad ofrecida en un canal donde no aplica. | No ofrecerla en Manual. b: `64034c12`. | Corregido |
| 15 | Baja\* | Menores con efecto: el tope diario fallaba el día del cambio de horario; el perfil tenía un Id por defecto; el tope de 12 herramientas no se validaba; la descarga se reintentaba sin espera; el blob se volvía a bajar; la retención limpiaba 500 por día. | Varias. | `IsInvalidTime`, Id obligatorio, validar en `BuildAsync`, escala de Flows y lotes repetidos. b: `a55ea27d`, `097c73bf`, `4e986a14`. | Corregido |
| 16 | Baja\* | Tests: el test de arquitectura del ADR 0020 decidía por nombre; faltaban casos con inyección, volumen y PostgreSQL. | — | Lista explícita de puertos de lectura, con negativos; seis inyecciones es/en. La evaluación real agregó una nota no solicitada: b: `761b564c`. | Corregido |
| 17 | Baja\* | En `/formularios/por-aprobar/:taskId` las migas decían «Diseñar formularios» y ningún ítem del menú quedaba activo. | La bandeja no reconocía sus rutas hijas. | f: `f0ebf96`. | Corregido |
| 18 | Media\* | En Por aprobar, cada ↑/↓ desmontaba el panel: saltaba el foco y se volvían a bajar las miniaturas. | El detalle se reemplazaba mientras cargaba. | Conservar el detalle anterior. f: `edb7ff3`. | Corregido |
| 19 | Baja\* | Después de hacer clic en otra fila, ↑, ↓ y Escape dejaban de andar. | El foco quedaba en la tabla. | Enfocar el panel al cambiar de respuesta. f: `c448fcf`. | Corregido |
| 20 | Baja\* | En escritorio, el panel tapaba la cabecera y «Exportar»; en el celular no era modal y tapaba la barra de la app. | Posición y modalidad sin definir por ancho. | Panel bajo la cabecera y `Dialog` común en el celular. f: `1d300c4`. | Corregido |
| 21 | Baja\* | «IA no disponible» salía como toast cada vez que se abría un chat; «Tomar atención manual» no era la acción principal. | Aviso global para un estado persistente. | Banner en línea y controles H1. f: `622b5e5`. | Corregido |
| 22 | Baja\* | «Por aprobar» aparecía dos veces y faltaban íconos y variantes del Badge. | — | f: `4f3a922`. R1019 #17: faltaba el color del Estado en la planilla. | Corregido |
| 23 | Baja\* | Faltaban tests del front, y el POST de transcripción se repetía después de una falla pasajera. | — | f: `2b93fbd`. | Corregido |
| 24 | Alta\* | **Piloto real:** con la cuenta en inglés y sin el nombre inglés del PDF, el formulario y su apertura llegaron en inglés, pero los avisos y el PDF salieron en español (`VISITA-000006`). | Cada pieza resolvía su idioma por separado. | Una cultura completa común antes de ofrecer o enviar, para formulario, apertura, avisos y PDF. b: `4b3e5638`, `33d92985`, f: `dbd7550`. Verificado en Meta el 2026-10-20. | Corregido |

### 2.3 R1019 · Correcciones de la Etapa 5 y cambio de idioma

Del 2026-10-19. Revisó las 24 correcciones de R1018 y el cambio a idioma original con traducción opcional. Sin huecos de seguridad.

**Antes de corregir se contrastó cada punto con el código y las decisiones** ([tabla](../../reviews/2026-10-19-code-review-cierre-formularios.md#contraste-previo-con-código-y-decisiones--2026-10-19)): el punto 2 contradecía una decisión vigente, y el 3 y el 5 ya estaban protegidos. Cierre el 2026-10-19, sobre `d61ae244`: 4.903 tests de backend, 2.424 del front y 527 visuales.

| # | Grav. | Qué pasaba | Causa raíz | Arreglo y regresión | Estado |
| --- | --- | --- | --- | --- | --- |
| 1 | Alta\* | El chat no listaba ni mandaba un formulario publicado solo en el original a una cuenta en inglés. Un `"en": ""` rompía la lista, el recibo y la tarjeta del historial. | El zod del front exigía `name` y `submitLabel` en el idioma del destinatario, con `min(1)`: más estricto que el servidor. No había un ejemplo exportado de ese caso. | Aceptar el secundario ausente o vacío y mostrar el original. Ocho capturas HTTP de cuenta inglesa con original español. f: `208951c`; b: `7934eeee`, `f36956fb`. | Corregido |
| 2 | — | Un catálogo propuesto con el nombre solo en otro idioma dejaba el formulario sin poder mandarse. | El arreglo propuesto cambiaba una decisión: solo el texto de los ítems tiene respaldo individual. | Se conservó; un test acredita el rechazo intencional. | Contrastado, sin cambio |
| 3 | — | Avisos a un rol con idiomas mezclados. | Ya protegido. | Regresión nueva en es/en. | Contrastado, sin cambio |
| 4 | Media\* | La planilla, el detalle, el Excel, las aprobaciones y la tarjeta del chat mezclaban idiomas o salían sin nombre con una traducción parcial. | Respaldo campo por campo en lugar de por versión. | Cultura por versión, leída del manifiesto por lote y resuelta en SQL antes de paginar. b: `e1e41c72`, `be67532a`, `b326336e`, `98e4f4da`. | Corregido |
| 5 | — | Un cambio de idioma de la cuenta entre la propuesta y su aceptación no se detectaba. | Ya protegido por `RevalidateAsync`. | Regresión b: `a423650b`. | Contrastado, sin cambio |
| 6 | Baja\* | `Forms.Publication.MissingTranslation` todavía decía «Completá … en español e inglés»; faltaban las etiquetas del tablero I1. | Texto de la regla anterior. | b: `15752c1c`; f: `7d97c5b`. | Corregido |
| 7 | Baja\* | Al publicar, el anuncio salía dos veces y la lista de faltantes no tenía tope. | Dos dueños para el mismo aviso. | Un solo toast, hasta tres faltantes y «y N más». f: `2d840c2`, `be0824e`. | Corregido |
| 8 | Media\* | Con el cupo en 2, el «no, gracias» a la segunda oferta quedaba `Blocked` sin interpretarse, y al día siguiente se volvía a ofrecer lo rechazado. | El arreglo de R1018 #4 movió el cupo antes de interpretar. | Mirar el cupo antes de ofrecer, no antes de interpretar. b: `c5990ea9`. | Corregido |
| 9 | Alta\* | El tope de 25.000.000 bytes rompía archivos de Flows que el selector y el registro aceptaban (25 MiB). | El arreglo de R1018 #10 aplicó el tope del audio al cliente de medios compartido. | Cliente en 25 MiB; 25.000.000 solo para la copia de audio. b: `bc3e72f1`. | Corregido |
| 10 | Media\* | En atención manual, un operador tenía una sola operación de IA para todos sus chats. | Índice `(OwnerUserId, ChannelId)`, con el operador como dueño. | Primero se sacó de esa unicidad la normalización de audio (b: `d3677a7d`). El 2026-10-21 se sumó el ámbito por conversación (b: `efe3b3e8`), con una integración de tres chats concurrentes. | Corregido |
| 11 | Media\* | El arreglo de R1018 #11 quedó incompleto: al agotar los reintentos, Azure lanzaba `AggregateException` y seguía terminando «incierta». | El catch solo cubría excepciones conocidas. | Toda excepción no cancelada antes del POST es `ContentUnavailable`. b: `255bfad7`. | Corregido |
| 12 | Media\* | El arreglo de R1018 #13 quedó incompleto: en Asistente, un audio de una cuenta desvinculada o inactiva no se copiaba. | La copia iba después del chequeo de cuenta. | Guardar siempre y marcar `Unavailable` después. b: `1538e25f`. | Corregido |
| 13 | Media\* | La lectura de sugerencias cortaba en 16 KiB sin contar el envoltorio y fallaba el turno. | El tope se medía sin el JSON que lo envuelve. | El mismo cálculo con envoltorio. La parcialidad de la lectura general se conservó por decisión. b: `d76210f1`. | Corregido |
| 14 | Media\* | El arreglo de R1018 #7 quedó incompleto para elección: un valor que no era un GUID descartaba toda la propuesta. | La omisión cubría solo escalares. | Omitir solo esa elección; un GUID ajeno se sigue rechazando. b: `abb3a605`. | Corregido |
| 15 | Baja\* | Menores: tope medido en UTF-16 contra grafemas en el editor; audios transcritos con el cupo agotado; la marca interna `retryable` salía en HTTP; pedidos faltantes que rechazaban fechas y montos y no detectaban `＠`; contadores sin reiniciar. | Varias. | b: `29d6e572`, `fd6b0b9b`, `7a5d8255`, `eb8d6b9b`; f: `ac9bf1d`. | Corregido |
| 16 | Baja\* | Un test dejaba un turno en la base compartida. `IPermissionService` figuraba entre los puertos de lectura con un método con efecto. La migración de cuotas exige PostgreSQL 16 (`IS JSON OBJECT`). | Aislamiento de tests, frontera mal declarada y requisito sin documentar. | Lectura de permisos separada, limpieza propia y PostgreSQL 16 documentado. | Corregido |
| 17 | Baja\* | El Estado de la planilla no tenía color. | El arreglo de R1018 #22 no llegó a la planilla. | f: `64e95a2`. | Corregido |
| 18 | Media\* | Un audio podía quedar «Transcribiendo…» para siempre. | Una falla pasajera del GET del hilo abortaba el POST, que no se volvía a pedir. | Conservar el POST ante un GET 500. f: `a1cc4eb`. | Corregido |
| 19 | Baja\* | Tocar la fila ya abierta dejaba el teclado en la tabla. | — | f: `736f01f`. | Corregido |
| 20 | Baja\* | Faltaban tests del front. | — | Incluidos en 1, 7, 17 y 18. | Corregido |

### 2.4 R1021a · Dar de baja, reactivar y eliminar

Del 2026-10-21. Sin huecos de seguridad; lo central estaba completo y con test. Se cerró el mismo día ([contraste y corrección](../../reviews/2026-10-21-code-review-dar-de-baja.md#contraste-y-corrección--2026-10-21)): 5.236 tests de backend, 2.511 del front y 585 visuales. Los puntos 12 a 15 no venían de la etapa: aparecieron al releer el código para dividir las guías.

| # | Grav. | Qué pasaba | Causa raíz | Arreglo y regresión | Estado |
| --- | --- | --- | --- | --- | --- |
| 1 | Alta\* | Un rechazo definitivo de la deprecación se repetía para siempre y no se veía. Pasaba con un Flow ya deprecado a mano en WhatsApp Manager, con la cuenta cambiada o con un manifiesto ilegible. La pantalla decía «Se deprecan cuando venzan los envíos abiertos», aunque no hubiera ninguno. | `WhatsAppFlowClient.EffectAsync` devolvía un 4xx como «sin efecto y sin incertidumbre», el binding volvía a `Pending` y el servicio ignoraba `FailureCode`. | Ante un 4xx, consultar con GET antes de otro POST; lo permanente pasa a `NeedsAttention`, visible y con espera creciente. b: `432070c4`, `a8e5a90f`; f: `f27c540`. | Corregido |
| 2 | Media\* | `FindPublishedAsync` lanzaba una excepción si el formulario se eliminaba entre el encolado y el despacho. | Subconsultas `.Single()` con el filtro de borrado devolvían NULL sobre un `long`. | Exigir la definición con el set filtrado, para que devuelva null. b: `dc8af3f8`. | Corregido |
| 3 | Media\* | El uso de un catálogo contaba formularios eliminados. | SQL directo sin el filtro global de borrado. | `AND NOT "IsDeleted"`, con test HTTP. b: `48c66359`. | Corregido |
| 3b | Baja\* | El prellenado repetía el perfil vigente como texto (`"flow-static-7.3-r8"`): al subir el perfil, iba a rechazar los valores. | Literal duplicado. | Usar el perfil del compilador. b: `8be1da5c`. | Corregido |
| 4 | Media\* | Eliminar no actualizaba el panel de Respuestas: el enlace abría un 404. | Faltaba invalidar `["form-responses", owner]`. | f: `02c5076`. | Corregido |
| 5 | Baja\* | El diálogo de baja mostraba textos de envíos abiertos cuando no había ninguno (el caso común). | Texto fijo. | f: `08fffcf`. | Corregido |
| 6 | Media\* | Un error de red al dar de baja decía «Volvé a guardar» en un diálogo sin nada que guardar. Si la baja sí se había aplicado, aparecía «Cambió la revisión». | Se reutilizó el texto del editor y no se releía el resultado. | Consultar primero y reconocer la baja o el 404. f: `2bedb9f`. | Corregido |
| 7 | Baja\* | Una baja confirmada podía no refrescar el editor. | Salida temprana si `confirmRevision` daba false. | f: `55a7b1d`. | Corregido |
| 8 | Baja\* | El menú ofrecía Reactivar sin `canPublish`, mientras la cabecera lo ocultaba. | Dos condiciones para lo mismo. | f: `58f1922`. | Corregido |
| 9 | Baja\* | Eliminar un publicado no avisaba que en WhatsApp no se deshace ni nombraba el prefijo reservado. | Borrador y publicado sin distinguir. | f: `77f0066`. | Corregido |
| 10 | Baja\* | Una regresión quitó el espacio entre el resultado y «Esperá…» en todas las publicaciones. | Envoltorio nuevo aplicado a todos los casos. | f: `11dd92e`. | Corregido |
| 11 | Baja\* | Los motivos de los ítems apagados no se alcanzaban con el teclado; un texto inicial usaba `role="alert"`. | — | f: `00c3f93`. | Corregido |
| 12 | Media\* | Editar un catálogo devolvía «usado por 0» aunque lo usaran formularios. | El mapeo de edición no contaba. | b: `2a9f6d82`. | Corregido |
| 13 | Media\* | BusinessForms comparaba IDs de canal de WhatsApp escritos a mano (`"whatsapp-assistant"`, `"whatsapp-manual"`), y `ModuleBoundaryTests` no lo detectaba. | Frontera entre módulos rota por literales. | Modo neutral en el contexto, y el test de frontera busca los literales. b: `6314f6f5`, `dc709706`, `07a16c23`. | Corregido |
| 14 | Media\* | El texto enviado desde el chat quedaba en la caché de mutaciones, contra la regla de editores privados. | El componente no seguía el patrón de formularios y sugerencias. | f: `fc306b7`. | Corregido |
| 15 | Baja\* | El cliente de mensajes de WhatsApp seguía redirecciones; los de Flows y medios no. | Configuración distinta para clientes hermanos. | b: `68369657`. | Corregido |

**Deuda menor que se arregla al tocar esos archivos:** `Add` sin exigir transacción en dos repositorios; lectores que devuelven `*Response`; filtros de la bandeja del chat fuera de la URL; un guard que acepta `filters` en un catálogo Manual; un comentario mal codificado.

**Después, sin bloquear:**
- el selector del chat puede ofrecer durante 30 s un formulario recién dado de baja;
- las revisiones periódicas toman el lock global de definiciones cada 5 minutos;
- BLOCKED y THROTTLED de Meta se leen como desconocidos;
- «No se puede eliminar porque tiene respuestas» se le muestra a quien solo administra: lo fija el plan.

### 2.5 R1021b · Menú y detalle, y aislamiento del asistente

Del 2026-10-21. La parte A, el aislamiento por conversación de los turnos manuales, estaba bien. La parte B era la etapa de menú y detalle. Cierre ([contraste y corrección](../../reviews/2026-10-21-code-review-menu-y-detalle.md#contraste-y-corrección--2026-10-21)): 5.389 tests de backend sobre `6732fa5d`, 2.689 del front y 623 visuales.

| # | Grav. | Qué pasaba | Causa raíz | Arreglo y regresión | Estado |
| --- | --- | --- | --- | --- | --- |
| 1 | Alta | En Linux, EF veía cambios pendientes y `MigrateAsync` fallaba, igual que los tests de migración de IA y el `efbundle`: el despliegue se caía. En Windows pasaba, por eso la corrida local dio verde. | El filtro del índice de `AiAssistantTurns` estaba escrito como texto de varias líneas. En Windows, la migración y el snapshot grabaron el salto como `\r\n`; en Linux el modelo lo arma con `\n`. | Filtro en una línea; snapshot y Designers regenerados; `MigrationSqlPortabilityTests` falla si un filtro o SQL de snapshot, migración o Designer tiene `\r`. Se normalizaron a LF los 19 SQL multilínea de 14 migraciones anteriores. No se agregó `.gitattributes`. b: `bd771cd0`. | Corregido |
| 2 | Media | La tabla de Formularios, que es la pantalla de entrada, leía y descifraba la versión activa de cada formulario publicado en cada carga, solo para mostrar el nombre y los idiomas. | Reutilizar el lector de esquemas para una proyección liviana. | `PublishedCultures` sellado al publicar y proyección en SQL sin esquema ni descifrado. b: `b3ea9253`, `61fc7ed6`. | Corregido |
| 3 | Media | «Con algo por aprobar» mezclaba formularios dados de baja sin marcarlos; el segmentado seguía en «Activos» y el aviso se repetía. | Filtro y segmentos sin coordinar. | Etiqueta de baja en la fila y segmentos sin marcar. f: `604305b`. | Corregido |
| 4 | Media | Quien solo aprueba y no tenía pendientes veía «Todavía no hay formularios publicados». Lo mismo en «Dados de baja» vacío. | Un solo texto para varios vacíos. | Un texto por caso. f: `71cd169`. | Corregido |
| 5 | Baja | Cada flecha traía todos los ids del conjunto filtrado: con 100.000 respuestas, varios MB para quedarse con tres. | Navegación calculada en memoria. | `ROW_NUMBER`, `LAG` y `LEAD` en SQL; una sola fila. b: `0c63a8ab`. | Corregido |
| 6 | Baja | «Por aprobar» se recorría por offset: si otro aprobador decidía tareas anteriores, las flechas salteaban pendientes. El review lo dio como plausible; los tests lo reprodujeron. | Paginado por posición sobre una lista que se compacta. | Continuación por `RequestedAtUtc` e `Id` desde la tarea ancla. b: `5583009b`, `6732fa5d`; f: `2c4058b`, `a3ca9fc`. | Corregido |
| 7 | Baja | «Formularios» no quedaba marcado en el menú dentro de un formulario. | Coincidencia `exact`. | f: `36e54d0`. | Corregido |
| 8 | Baja | Las migas de D2 sumaban un «Respuestas» genérico. | — | f: `bf0820b`. | Corregido |
| 9 | Baja | Al cerrar D2 después de navegar con ↓, el foco iba a la fila nueva pero quedaba marcada la anterior. | La selección no seguía a la navegación. | f: `a9f8a6f`. | Corregido |
| 10 | Baja | En el celular, «Ver PDF» abría la pantalla completa con la Actividad plegada y el PDF escondido. | No se pasaba la intención. | f: `2da26a1`. | Corregido |

**Después, sin bloquear:** el número del menú en texto en lugar de insignia; detalles de MF1 y de los resúmenes plegados; el ancla puede dar 404 si, después de aprobar, la respuesta deja de cumplir un filtro privado; `FindRecordNavigationAsync` devuelve 404 antes de revalidar el permiso.

### 2.6 R1022 · Catálogos E1

Del 2026-10-22. Lo bien resuelto:
- los arreglos previos del plan: código único diferido (antes, intercambiar dos códigos en un guardado daba 500) y tope de 10.000 sin cortes residuales a 500;
- migraciones con CHECK en una línea y snapshot sin `\r`: la lección de R1021b se aplicó;
- importación con SHA-256 y revalidación bajo lock.

Cierre el mismo día ([cierre local](../../reviews/2026-10-22-code-review-catalogos-e1.md#cierre-local)): 6.016 tests de backend sobre `76b07d6f`, 2.959 del front y 677 visuales.

| # | Grav. | Qué pasaba | Causa raíz | Arreglo y regresión | Estado |
| --- | --- | --- | --- | --- | --- |
| 1 | Media | Importar en un catálogo decimal en español guardaba «1.500» como **1,5**; en inglés, «1,500» también; «3/4/2026» se leía siempre con el día primero. No se notaba: el editor del front lo leía distinto y la vista previa no mostraba el valor. El error quedaba sellado en las respuestas, el Excel y el PDF. | `FormCatalogValuePolicy.NormalizeImport` probaba primero el formato invariante e ignoraba la cultura que ya llegaba. | Interpretar con la cultura, rechazar lo ambiguo (`\d{1,3}(\.\d{3})+` sin coma) y mostrar el valor en la vista previa. b: `3bb93d21`; f: `eec996b`. | Corregido |
| 2 | Media | Pegar o importar solo «Código / Texto» sobre opciones existentes ponía en blanco el inglés y los detalles. | Columna ausente tratada como columna vacía. | Ausente conserva; presente y vacía limpia. b: `5d2da8a3`. | Corregido |
| 3 | Media | Los topes del XLSX no acotaban lo que cargaba ClosedXML: un archivo de pocos MB con celdas vacías con estilo u otras hojas podía ocupar mucha memoria en cada pedido. Plausible. | Se contaban solo las celdas con valor. | Presupuesto físico de todas las hojas antes de cargar. b: `089c5264`. | Corregido |
| 4 | Media | El freno de más de 20 opciones en «elegir varias» no decía la causa: en el chat caía en «el catálogo no se puede usar». | Código nuevo sin texto propio. | Texto propio es/en con qué hacer. b: `15e4ad2c`; f: `aabec44`. | Corregido |
| 5 | Media | Pegar rechazaba todo si una celda sin comillas tenía `"`, como `Caño 1/2"`. | Parser que abría comillas en cualquier lugar. | Comilla literal salvo al empezar la celda. f: `4c442af`. | Corregido |
| 6 | Media | La revisión de importación mezclaba errores y avisos; como el servidor corta en 100, los avisos podían tapar los errores. La columna desconocida no tenía nombre y «Repetido» no decía con qué fila. | Sin severidad en la presentación. | Errores primero, Error y Aviso, encabezado real y fila relacionada. b: `4b466003`; f: `139dfff`, `040e1f4`. | Corregido |
| 7 | Baja | Una segunda hoja ancha rechazaba un archivo válido sin decir cuál. | Diagnóstico sin el nombre de la hoja. | Se nombra la hoja y se conserva el tope. b: `fbfd1884`; f: `a2a1411`, `ef05d92`. | Corregido |
| 8 | Baja | Una excepción rara de ClosedXML salía como 500. | Sin mapeo general. | `FileUnreadable`. b: `86513081`. | Corregido |
| 9 | Baja | Los decimales de fórmulas salían con 17 cifras: `0.1+0.2` daba `0.30000000000000004`. | Se leía el double crudo. | 15 cifras significativas, como Excel. b: `27b524e7`. | Corregido |
| 10 | Baja | El `Down` de elegir varias miraba solo las preguntas publicadas: un borrador con `multiChoice` pasaba el rollback. | Guarda incompleta. | También el borrador, con lock previo al DDL. b: `1345fe37`. | Corregido |
| 11 | Baja | Pegar usaba textos pensados para archivos, como «Pegado.csv» también en inglés. | — | f: `76074cb`. | Corregido |
| 12 | Baja | La vista previa decía «hasta 200 opciones» también en «Varias», con tope 20. | Constante equivocada. | f: `de1faf8`. | Corregido |
| 13 | Baja | La tarjeta del chat con varias opciones repetía el catálogo y no decía a qué opción correspondía cada detalle. | — | f: `febf258`. | Corregido |
| 14 | Baja | Textos distintos del diseño. | — | f: `40a1c74`. | Corregido |
| 15 | Baja | Las tablas de Altas, Cambios y Bajas no tenían encabezados. | — | f: `3027563`. | Corregido |

**Después, sin bloquear:**
- una columna obligatoria faltante daba un aviso genérico: se resolvió en E2;
- «Aplicar» con cero diferencias subía la revisión: también se resolvió en E2 (b: `7c94f14c`);
- las opciones de las reglas devuelven hasta 10.000 por pregunta;
- hay que confirmar que el primer endpoint `multipart/form-data` que modifica datos autentica con bearer y no con cookies;
- la spec nombraba archivos y un código de error que no existían: se alineó.

### 2.7 R1023 · Catálogos E2 (en corrección)

Del 2026-10-23. Revisó E2-0 a E2-4: campos extra, datos de las personas, filtros por destinatario, qué viaja a WhatsApp y las respuestas.

**Lo bien resuelto:**
- huellas compatibles: todo lo nuevo que se sella lleva `JsonIgnore(WhenWritingNull)`;
- SQL parametrizado, con la contención antes del tope;
- a Meta viajan solo `id`, `title` y `description`;
- locks ordenados;
- `ToString()` redactado.

Los puntos 34 a 39 los sumó el usuario al usar la app. Los commits de corrección empezaron a las 16:39; al corte no hay sección de cierre en el review.

| # | Grav. | Qué pasaba | Causa raíz | Arreglo y regresión | Estado |
| --- | --- | --- | --- | --- | --- |
| 1 | Alta | Si en «Zonas» se daba de baja «Norte» y 300 proveedores tenían Zona = Norte, desde ahí cualquier PUT de «Proveedores» respondía 400 `FieldValueInvalid` en `fields[i].type`, sin decir qué fallaba. Lo mismo al renombrar, editar un CUIT o agregar una opción. Importar pasaba la vista previa y fallaba al aplicar. | `ValidateItems` revalidaba en cada guardado todos los valores guardados contra las opciones **activas** de la fuente. Además, importar no tomaba los locks de los catálogos fuente. | Opción activa solo para lo nuevo, cambiado o convertido; lo guardado tiene que seguir siendo una opción, activa o dada de baja; importar toma los locks ordenados. b: `b75f40cd`, `c6f622d5`, `f02af5dd`, `781c1cea`, `00dc20af`. Test de orden con Ids invertidos: `1e82f704`. | Corregido |
| 2 | Alta | Cualquier 409 ponía el editor del catálogo en revisión de solo lectura, y la única salida, «Usar la revisión actual», perdía lo editado. Pasaba con `SourceInUse` y con `FieldInUse`. | El front trataba todo 409 como conflicto de revisión. | Revisión solo con `Forms.Catalog.RevisionConflict`; los otros 409 se corrigen en el editor, mostrando `uses`. Quitar un campo lo saca también de la presentación del borrador. f: `c2572b8`, `4a24702`, `dfb1d26`, `e3e81a7`. | Corregido |
| 3 | Alta | Si alguien escribía el nombre en inglés de un campo y lo borraba, viajaba `en: ""` y el back lo guardaba. Desde ahí fallaba el guard del PUT y de cada GET: el catálogo no volvía a abrir. Además, una columna sin encabezado se tomaba como ese campo al importar. | El back guardaba traducciones vacías y el guard del front las rechazaba: reglas distintas a cada lado. | El front omite el inglés en blanco, el back lo descarta, la importación ignora encabezados vacíos y el guard no se relaja. f: `b12a297`; b: `79e2193a`, `c0b9c37e`. | Corregido |
| 4 | Alta | Pasar «Zona» de texto a «Elegir de un catálogo» marcaba en error cada fila visitada y deshabilitaba Guardar: el back, que sí convierte, nunca recibía el pedido. Pasar CUIT a obligatorio con faltantes también lo deshabilitaba, y nunca aparecía el «faltan N» del servidor. | El front validaba las filas no editadas contra la definición nueva y mandaba todos los valores. | Lo no editado se valida contra lo guardado y viaja solo lo editado (delta); el servidor responde `invalidRows` con fila, valor y motivo; diálogo «Cambiar el tipo» de CE1. f: `4475892`, `2efb86e`, `ff28cbc`, `bc71fd4`, `783473c`, `28ab6d6`, `8e37dd3`; b: `4537fc26`, `d9667c5a`, `83d10ff7`. | Corregido |
| 5 | Media | Pasar un campo de texto a decimal interpretaba «1.500» como 1,5, y «12.345» como 12,345, sin avisar. | La conversión usaba la lectura invariante. Es el error de R1022 #1 en otro camino. | Convertir con `NormalizeImport` en la cultura del pedido; lo ambiguo no convierte. b: `c3353d35`, `69253495`. | Corregido |
| 6 | Media | Pasar un campo de catálogo a texto copiaba el GUID de la opción: quedaba en la tabla, el Excel y `Shown`. En la descripción viajaba a Meta; en el título superaba los 30 caracteres y frenaba los envíos. De booleano a texto quedaban «true» y «false». | Conversión por representación interna. | Texto visible de la opción fuente, y «Sí» o «No» en el idioma original del catálogo. b: `f473c14f`. | Corregido |
| 7 | Media | Cambiar el tipo o la fuente de un dato de las personas dejaba valores huérfanos. Si se corregía la fuente de «Zona» a «Regiones», 300 personas quedaban con IDs de «Zonas»: la contención daba 0 opciones y salía `CatalogEmpty` en lugar de `RecipientDataMissing`. | El control de «en uso» solo frenaba si un filtro ya usaba el dato; `ValuesJson` no se tocaba. | El back pide confirmación (`Forms.PersonAttribute.ValueRemovalNotConfirmed`) y limpia; un valor que no es opción cuenta como dato faltante, también en la vista previa por persona. b: `95e200c8`, `6023f7d7`, `d2c2cd9f`, `d7f1cad7`. El front no maneja todavía ese código. | En corrección |
| 8 | Media | Pasar «Zona» de filtro a dato, por ejemplo para verla en la planilla, se guardaba sin aviso, y desde ahí todos los envíos del formulario publicado daban `CatalogFilterUnavailable`. | Solo se frenaba quitar el campo, no cambiar su papel, tipo o fuente. Un test fijaba la falla como esperada. | `FieldInUse` con `uses` ante cualquier cambio de `Role`, `Type` o `SourceCatalogId`. | Abierto |
| 9 | Media | La vista previa no detectaba repetidos de un campo único con «ñ», tildes, «+» o «&»: «Peñalba S.A.» repetida pasaba y al aplicar daba `FieldValueDuplicate`. | Comparaba `GetRawText()`: el valor de jsonb viene sin escapar y el normalizado, escapado. | Comparar el valor decodificado, con un test sobre PostgreSQL. | Abierto |
| 10 | Media | Cada guardado reescribía las 10.000 opciones y su auditoría: renombrar el catálogo hacía 10.000 UPDATE, todas figuraban modificadas por esa persona y se retenía el lock que también usa el envío. | Se asignaba el JSON a todos los ítems; PostgreSQL devuelve jsonb con espacios y EF los marcaba modificados. | Asignar solo si el mapa normalizado cambió, con un test de que un guardado sin cambios no toca `ModifiedAtUtc`. | Abierto (ningún commit lo cita) |
| 11 | Media | Con solo `formcatalogs.read` se veían `usedBy` y los `uses` del 409, con nombres e IDs de formularios y borradores. | El filtro de permisos se aplicaba al conteo y no a la lista ni al error. | Nombres solo con `formtemplates.read`; sin ese permiso, el bloqueo y la cantidad. | Abierto |
| 12 | Media | Importar después de guardar campos usaba la definición vieja: «Cambios» mostraba un GUID como nombre de columna. | El diálogo recibía el detalle de cuando se entró a la pantalla. | El catálogo confirmado más reciente. f: `9efd8e2`. | Corregido |
| 13 | Media | Vaciar una celda de catálogo seguía mostrando la opción anterior, y al guardar se borraba sin que se viera. | La etiqueta derivada sobrevivía al valor. | f: `584aec8`, `982ade8`. | Corregido |
| 14 | Media | «Zona es Sur» en un catálogo de 72 opciones se veía vacío al reabrir. | La etiqueta se buscaba entre las primeras 20 opciones. | Resolver la etiqueta por Id. b: `8b83c0bd`; f: `7080141`, `d72caf4`. | Corregido |
| 15 | Media | Los filtros y «Propio de esta pregunta» se ofrecían en catálogos de Filtros: se guardaba bien, pero publicar se frenaba con `MatchInvalid`. | La UI ofrecía lo que el back no admite. | Mostrarlos solo con catálogos Manual. f: `e133d93`. | Corregido |
| 16 | Media | El aviso de títulos de más de 30 caracteres miraba solo 20 opciones: con 41 largos entre 1.200, podía decir «1 opción» o nada. | Cálculo en el front sobre la página cargada. | Que el servidor calcule el chequeo en es y en. | Abierto |
| 17 | Media | El desplegable de una celda de catálogo se recortaba dentro de la tabla ancha. Plausible. | CSS del contenedor. | Portal, verificado en el navegador. | Abierto |
| 18 | Baja | Un filtro fijo que deja más de 20 opciones en «elegir varias» ya no frenaba publicar; antes de E2 sí. | Regresión de E2. | Problema, no aviso, cuando no hay filtros por persona. | Abierto |
| 19 | Baja | El valor fijo de un filtro no se validaba contra su fuente: un ID inexistente o inactivo daba 0 opciones para todos. | — | Verificar que exista y esté activa. | Abierto |
| 20 | Baja | «Como si lo recibiera» no exigía `formcatalogs.read`: quien solo administra formularios podía ver hasta 200 valores de cualquier catálogo. | Permiso incompleto en una vista previa. | Exigirlo. | Abierto |
| 21 | Baja | Las sugerencias pasaban los campos sellados al contrato del núcleo. | Contradice la regla del puerto. | `includeCatalogFields: false`. | Abierto |
| 22 | Baja | Cambiar reglas o reactivar un campo con valores incompatibles daba solo el primer error, en el control equivocado. | — | Acumular como `TypeChanges`. | Abierto |
| 23 | Baja | El chequeo de presentación miraba solo la cultura de la UI y no las descripciones. | — | es y en, con el tope de 300. | Abierto |
| 24 | Baja | `Find…` que devolvían colecciones, contra el [ADR 0008](../../decisions/0008-nombres-de-repositorios-y-lectores.md). | El test de nombres no lo detectaba. | `List…` y `PersistenceNamingTests` lo rechaza. b: `8e6585e7`. | Corregido |
| 25 | Baja | «En uso» no decía si era el borrador o la versión activa, y faltaba «Ir a la pregunta». | — | b: `84289eef` devuelve `questionId`; falta el front. | En corrección |
| 26 | Baja | En datos de las personas, la carga y el error se dibujaban fuera de la pantalla, la tabla no se actualizaba al cambiar el nombre y un 409 decía «Volvé a cargarlo» sin cómo hacerlo. | — | — | Abierto |
| 27 | Baja | Guardar no hacía nada, sin aviso, si se editaba una opción inactiva sin un obligatorio. | La página y el hook usaban reglas distintas. | `required && isActive` en los dos lugares. f: `70067e0`, `420c311`. | Corregido |
| 28 | Baja | «Como si lo recibiera» no se podía reintentar y no esperaba ante un 429. | — | — | Abierto |
| 29 | Baja | Los guards admitían cualquier `code` en los avisos de publicar; se veía «Como mucho 1 opciones». | Guards más laxos que el contrato y sin plurales. | Unión discriminada por `code` y claves `_one`/`_other`. | Abierto |
| 30 | Baja | Con campos quitados ocultos, las flechas a veces no movían nada a la vista. | — | Mover solo entre activos. | Abierto |
| 31 | Baja | La misma fecha se veía «15/03/2027» en la tabla y «15 mar 2027» en la revisión de la importación. | El formato por tipo no salía de una sola pieza, como pedía E2-0. | Una sola pieza de formato. | Abierto |
| 32 | Baja | Faltaban partes de CE1 a CE5. | — | El diálogo de CE1 (f: `ff28cbc`) y el motivo de la fila de CE2 (f: `47e8d2a`) ya están. | En corrección |
| 33 | Baja | Faltaban pruebas: con PostgreSQL, y `FieldInUse` con usos del borrador y de la versión activa en las cohortes exportadas. | — | — | Abierto |
| 34 | Alta\* | El chat de Inicio no mostraba la respuesta. Al mandar «Hola», el front daba la consulta por terminada, mostraba «states.pending» sin traducir y nunca la respuesta. Pasaba desde por lo menos el 2026-10-09. | El back devuelve el estado en minúsculas (`AiAssistantResponsePolicy.State` usa `ToLowerInvariant()`), y sus tests de integración lo fijan así. El front lo tipa con mayúscula inicial y sus dobles de test también. No hay ejemplos exportados del asistente. | El contrato publicado es el del back: corregir el front y exportar ejemplos reales del asistente validados con zod estricto. | Abierto |
| 35 | Alta\* | Al crear un catálogo solo en español, «Guardar» parecía no hacer nada: el 400 traía `name.en` y `description.en`, y el front lo mostraba solo en la pestaña del otro idioma. La persona se iba y perdía lo escrito. Los nombres de campos, las opciones y los datos de las personas exigían español. | La regla de idioma original del 2026-10-19 dejó escrita una excepción bilingüe para el alta de catálogos, y el español quedó como valor por defecto. | Lo que se crea se escribe en el idioma del menú, que es el original; el otro idioma es traducción opcional; no hay selector de idioma en pantalla. b: `58fb4e8e` guarda `OriginalCulture`; faltan contratos, validadores y front. | En corrección |
| 36 | Baja\* | «Agregar opción» marcaba Texto y Código en rojo antes de escribir nada, con «Revisá las opciones marcadas antes de guardar». | Validación en vivo desde el alta, contra la regla «se valida al enviar». | Marcar al intentar guardar o al salir de un campo tocado. | Abierto |
| 37 | Baja\* | El aviso «Guardá el catálogo antes» empujaba los botones de Opciones y los desalineaba. | Texto explicativo debajo de los botones. | Sacarlo, alinear las acciones a la derecha y dejar el motivo como tooltip. | Abierto |
| 38 | Media\* | En todo el front había textos que explicaban los botones, estados «Cambios sin guardar», números de revisión junto a Guardar, botones corridos al medio y errores antes de intentar guardar. | La guía visual decía que el botón «explica el estado» y daba «Cambios sin guardar» como ejemplo de estado. | Regla nueva en la guía (f: `bb98bf2`), que hay que aplicar en todas las pantallas. | Abierto (la regla está; el código no) |
| 39 | Baja\* | La sección «El catálogo» mostraba en cajas datos que no se pueden cambiar, con textos que lo explicaban; la descripción era obligatoria. | — | Solo Nombre y Descripción opcional; origen y tipo como dato de solo lectura. | Abierto |

**Después, sin bloquear:**
- rendimiento, a medir antes de optimizar: la preparación carga todas las opciones de cada fuente para ofrecer 201, y el detalle carga las 10.000 para calcular `hasValues`;
- «En uso» no se serializa con la aceptación de una publicación;
- la búsqueda de personas queda en la URL (`?search=`);
- volver a una build anterior a E2 mandaría las listas sin filtrar, porque el código viejo ignora `match`;
- quitar el módulo no está automatizado.

**Para el piloto:** una pregunta opcional de «elegir varias» sin opciones para una persona manda un `CheckboxGroup` vacío; Meta pide de 1 a 20.

### 2.8 Correcciones fuera de los reviews

No todos los `fix` vienen de un review. Estos no tienen cuerpo: lo único que dicen es el asunto.

| Cuándo | Commits | Qué dice el asunto |
| --- | --- | --- |
| 2026-10-17, durante la Etapa 5 | b: `4963d708`, `b2edf210`, `492637b1`, `44ebaa5a`, `649ae20c`, `2e5ec9a8`, `1d1fd476`, `3ea86622`, `a5186bea`, `6218cdeb`, `ea16b2e5`; f: `98022bc` | Selección desde la propuesta auténtica, rechazo conservado, títulos de opciones de aclaración, fuente canónica de una captura repetida, idioma del motivo, esquema binario del audio, opción retirada, chat ocupado, recuperación antes de aceptar, evidencia del catálogo fuera de la caché, filtrar candidatos antes de proyectarlos y Escape en el panel. |
| 2026-10-20, después del piloto | b: `4dc36cba` | Abrir formularios manuales sin prellenado (ver [3.4](#34-piloto-total-con-proveedores-reales-2026-10-20)). |
| 2026-10-21, baja y menú, antes de sus reviews | b: `d4c23839`; f: `9854ac6`, `589ff76`, `72a3947`, `53b1069`, `35a538d`, `30a65a6`, `0d9a9f1`, `9a91172`, `07ef34f`, `64eac1b`, `54cca75` | Entradas de baja y eliminación. «Explicar por qué no se elimina con respuestas» y después «usar un motivo neutral al impedir la eliminación». Detalle y permisos en pendientes, migas, densidad, descarga en el visor y selección retenida tras revocar. |
| 2026-10-21, arreglos previos de E1 (plan, §0) | b: `82d7beb0`, `5ed1bcc7`, `e1e087e3` | Unicidad diferida de códigos, arrays y prellenado por perfil, y conteo de planillas dispersas. |
| 2026-10-22 y 23, durante E1 y E2 | b: `81aeca8b`, `7a27c78a`, `7c94f14c`, `8d2e5ddc`, `a37e0868`, `873186f8`; f: `9aab213`, `0473958`, `b7b47c5`, `5e75aff`, `3138e04`, `9807676`, `565647e` | Uso del borrador y de la versión activa, revisión conservada sin diferencias, recibo de importación, todas las conversiones inválidas y búsquedas de datos personales redactadas. |
| 2026-10-21, latencia | b: `1a7b14b1`, `5d875ed5` (`perf`) | Texto recibido → envío pasó de 35,35 s a 6,56 s, y audio de 65,82 s a 7,73 s ([medición local](../../features/whatsapp.md#espera-del-asistente-y-avisos-locales--2026-10-21)). Qué lo disparó no está documentado. |

## 3. Por piloto

### 3.1 Antecedente: aprobación y PDF, 2026-10-17

Está en el traspaso anterior. En español, con Meta y Azure reales. Se probó `VISITA-000001` con aprobación por otra cuenta y el PDF con foto abierto en el celular. Encontró dos cosas que corrigió R1017:
- la web rechazaba `origin` aunque el POST diera 202 (#17);
- Chrome bloqueaba el visor dentro de un iframe con sandbox (#12).

### 3.2 Asistente y audios, 2026-10-18

Con Aspire y túnel, y Meta, OpenAI y Azure privado reales. Se usó **Registrar visita** v2, con `flow-static-7.3-r8`, «Cuándo lo ofrece» y foto obligatoria ([resultado](../../features/formularios.md#piloto-del-asistente-y-audios--2026-10-18)).

| Se probó | Resultado |
| --- | --- |
| Texto y audio con prellenado | Bien: la persona confirmó, mandó la foto y se vieron la transcripción completa y el motivo (`VISITA-000002/000003`). |
| Pedido sin formulario | Bien: aviso, tema en Formularios que faltan y sugerencia en Manual con Revisar y enviar (`VISITA-000004`). |
| Cuenta en inglés con traducción completa | Bien: formulario, avisos y PDF en inglés (`VISITA-000005`). |
| Cuenta en inglés sin el nombre inglés del PDF | **Falló:** avisos y PDF en español, formulario y apertura en inglés (`VISITA-000006`). Lo corrigió R1018 #24. |
| Continuación después de responder | No se probó: el formulario no tenía opciones. Quedó para el 2026-10-20. |

No se tocó código durante el piloto. Después se restauraron el idioma de la cuenta, el título inglés del PDF y el modo Manual.

### 3.3 Calificación real de la IA, 2026-10-18

Es evidencia separada del piloto con Meta ([evidencia](../../guides/configurar-ia.md#evidencia-para-habilitar-perfiles)). Dos fallas que ningún doble mostró:
- **El esquema estricto de OpenAI rechaza `uniqueItems`.** El intento `review5_20261018_01` quedó fallido. Se quitó del descriptor de ofertas y la validación de duplicados quedó en el servidor (b: `b9c41e37`).
- **El modelo agregó una nota no solicitada** en un caso de inyección sobre el destinatario. La descripción de la herramienta ahora separa hechos de negocio de instrucciones y metacomentarios (b: `761b564c`).

La reevaluación `review5_20261018_03` pasó 30 de 30 casos y 434 de 434 comprobaciones, con seis inyecciones. No acredita estilo exhaustivo ni rendimiento de producción.

### 3.4 Piloto total con proveedores reales, 2026-10-20

Con Aspire y túnel, y Meta, OpenAI y Azure Blob privado reales, sobre backend `a7cea3f2` y front `44e464b` ([registro](../verificacion-y-produccion.md#formularios-piloto-total-con-proveedores-reales--2026-10-20)). El control del navegador falló, así que la preparación y el contraste de estados se hicieron por la API autenticada.

| Recorrido | Resultado |
| --- | --- |
| Publicación | Registrar visita v3 solo en español, con catálogo Manual, foto y documento obligatorios, aviso, aprobación por rol y PDF. La v4 sumó el inglés completo. |
| Texto en Asistente | `VISITA-000007`: prellenado, foto y documento, aviso y motivo. |
| Aprobación y PDF | Otra cuenta aprobó; el PDF con foto se abrió en el celular. De `000007` a `000013`, con las cuatro acciones en Hecho. |
| Planilla y D2 | Panel, Fotos, PDF, momentos, ↑/↓, filtro Aprobada y Excel. |
| Audio | `VISITA-000008`: transcripción, prellenado, aprobación y PDF. |
| Formulario inexistente | El pedido apareció en Formularios que faltan y se agrupó con el tema existente. |
| Qué sugerir después | `VISITA-000012` → `PRB1010-000002`, con «Preguntar siempre» y los dos datos mapeados. |
| Revisión Manual | `VISITA-000009`, abierta prellenada y confirmada. |
| Idioma | Cerró físicamente R1018 #24: original solo (`000007`), inglés completo (`000010`), faltante solo del aviso (`000011`), faltante solo del título del PDF (`000012`) y la excepción individual del ítem (`000013`). |

**Lo que falló:**
1. **Un envío Manual sin prellenado no abría.** Meta aceptaba el envío, pero el Flow fallaba al abrir en Android. Con el lugar prellenado, sí abría. Causa: el transporte mandaba `prefill_FORM:{}`, un objeto vacío. Arreglo el mismo día, con la regresión primero: omitir los mapas sin valores (b: `4dc36cba`). Se verificó en el mismo Android, sin republicar ([corrección](../verificacion-y-produccion.md#corrección-de-apertura-manual--2026-10-20)).
2. **La primera continuación apuntó al destino v2, con el perfil anterior,** y se rechazó como NotRepresentable antes de crear una salida. Hubo que republicar el destino como v3 con r8.

Se restauraron el contenido, las reglas, el diseño del PDF, las sugerencias y el idioma de la cuenta, y se ejecutó `aspire stop`.

### 3.5 Lo que sigue sin prueba física

El [piloto único posterior a E2](../verificacion-y-produccion.md#formularios-piloto-único-con-meta-después-de-e2) reúne, por decisión del usuario, todo lo que no se probó con Meta desde el 20/10:
- baja con un envío abierto, deprecación real (`POST /{id}/deprecate`), reactivar y eliminar;
- MF1, D2 y pantalla completa con datos reales, y la cuenta que solo decide;
- dos o tres contactos simultáneos en Asistente y en Manual;
- el `nfm_reply` real de un `CheckboxGroup`: array, orden, `[]` contra clave ausente, y el grupo vacío;
- filtros por persona, campos sellados en planilla, Excel y PDF, y catálogos decimales.

Además, Edge nunca se recorrió, y los estados BLOCKED y THROTTLED de Meta no se vieron nunca.

### 3.6 Qué enseñaron los pilotos

Esto suma a lo que ya dice el 06:
- **Que Meta acepte un envío no quiere decir que abra.** `prefill_FORM:{}` pasó la aceptación y falló en el teléfono. Hay que abrir cada variante en un celular, no solo mirar el estado del envío.
- **El idioma se decide una vez por recorrido.** Apertura, formulario, avisos y PDF salen de la misma decisión. Se prueba quitando una sola traducción por vez, de cada pieza.
- **Los datos del piloto tienen que cubrir las ramas.** Sin opciones de catálogo no se pudo probar la continuación el 18/10.
- **Lo publicado con un perfil anterior no gana capacidades nuevas.** Hay que republicar, y el piloto tiene que saber qué versión usa cada destino.
- **El proveedor de IA real valida distinto que el doble.** El esquema se prueba contra el modo estricto real antes de declararlo.
- **Agrupar pilotos acumula riesgo.** Al corte, cuatro etapas no tienen prueba física.

## 4. Catálogo de errores recurrentes

Cada grupo dice el patrón, los ejemplos con su review y su número, la causa común y cómo se evita. Cuando el 06 ya tiene la regla, se aclara.

### 4.1 Front y back con reglas distintas para lo mismo

**Patrón.** La misma regla se escribe dos veces, en el front y en el back, y difieren. Las validaciones del front tapan al servidor o rechazan lo que el servidor aceptó.

- R1023 #4: el front validaba filas no editadas contra el tipo nuevo y deshabilitaba Guardar; el servidor, que sí convierte y explica, nunca recibía el pedido.
- R1023 #27: Guardar no hacía nada porque la página y el hook aplicaban reglas distintas de obligatorio.
- R1023 #3: el back guardaba `en: ""` y el guard del front lo rechazaba: un catálogo que no se abre más.
- R1019 #1: el zod del chat exigía el nombre en el idioma del destinatario con `min(1)`; el servidor admitía el original solo.
- R1023 #35: el 400 del servidor se mostraba solo en una pestaña que no estaba a la vista, así que Guardar «no hacía nada».
- R1023 #15: el front ofrecía filtros en catálogos de Filtros que el back rechazaba al publicar.

**Causa común.** No hay un caso que pruebe los dos lados con los mismos valores borde. Además, el front se adelanta a decidir lo que depende de datos guardados.

**Cómo se evita.**
- El servidor decide todo lo que depende de datos guardados o de otras entidades. El front valida forma, manda el pedido y muestra la respuesta.
- Los guards son exactamente el contrato.
- Una prueba de ida y vuelta (guardar y releer) con los valores borde: traducción vacía, null, inactivo.
- Un error de un campo que no está a la vista se hace visible.

### 4.2 Contratos sin ejemplos exportados o tipados a mano

**Patrón.** Un contrato que el front consume sin un ejemplo exportado de la corrida real se rompe en silencio. El 06 ya pide exportar cada campo nuevo; esto muestra qué pasa con un área entera sin ejemplos.

- R1023 #34: estado del asistente en minúsculas contra mayúsculas. Los tests del back fijaban `pending` y los dobles del front `Pending`. Roto desde por lo menos el 2026-10-09, sin que lo detectaran la suite, las visuales ni los reviews de esas dos semanas.
- R1019 #1: no había un ejemplo de cuenta en inglés con una versión solo en español. Se exportaron ocho.
- R1023 #29: guards que admitían cualquier `code`.
- R1023 #33: `FieldInUse` y los usos del borrador y de la versión activa no estaban en las cohortes.

**Causa común.**
- La red de seguridad (cohortes exportadas consumidas con zod estricto) cubría formularios y WhatsApp, pero no Inicio.
- Una convención de serialización distinta en un solo lugar: el resto de los estados viaja con el nombre del enum.

**Cómo se evita.**
- Todo endpoint que consume el front exporta ejemplos de la corrida real, incluidos estados y errores de negocio.
- El front no escribe dobles a mano para lo que tiene ejemplo.
- Una sola convención para enums en todo el contrato. *Propuesta de este anexo, no está implementada:* un test que detecte `ToLower` al serializar estados.

### 4.3 Cultura e idioma

**Números y fechas.** El 06 ya tenía `12.50` contra `12,50` y la coma en los conteos.
- R1018 #7: «1.500,50» o «18/10/2026» en un prellenado descartaba toda la propuesta.
- R1022 #1: importar «1.500» en español daba 1,5; «1,500» en inglés también; «3/4/2026» siempre con el día primero.
- R1023 #5: el mismo error al convertir texto a decimal, por la lectura invariante.
- R1022 #9: fórmulas con 17 cifras.
- R1023 #31: la misma fecha con dos formatos.
- R1023 #6: booleano a texto «true»/«false», y catálogo a texto con el GUID.

**Idioma del contenido.**
- R1018 #24: el formulario en inglés y los avisos y el PDF en español.
- R1019 #1–#4: listas, avisos, planilla y Excel con idiomas mezclados o sin nombre.
- R1023 #35: español obligatorio por defecto, y alta de catálogos bilingüe por una excepción escrita en la regla.
- R1023 #23: chequeos solo en la cultura de la UI.

**Texto y Unicode.**
- R1018 #9: «Ana» dentro de «mañana», y teléfonos con guiones Unicode que escapaban al control.
- R1019 #15: tope en UTF-16 contra grafemas, y `＠` sin detectar.
- R1023 #9: comparación del JSON crudo, escapado contra sin escapar.
- R1018 #15: el tope diario fallaba el día del cambio de horario.

**Causa común.**
- Cada camino que lee un número o elige un idioma lo resuelve por su cuenta.
- La lectura invariante va como primer intento.
- Se comparan representaciones en lugar de valores.

**Cómo se evita.**
- Una sola pieza por tipo para leer y mostrar, con la cultura del pedido, en el back y otra en el front. Lo ambiguo se rechaza.
- Una sola decisión de cultura por recorrido.
- Comparar valores decodificados y normalizados.
- Un test en es y en para **cada** camino que lee números: importar, pegar, convertir tipo, prellenado y filtros.
- El idioma original es el del menú; ningún «es» por defecto.

### 4.4 Concurrencia, unicidad y locks

- R1018 #1: un índice único de turno activo por persona en todos los canales dejaba un segundo mensaje `Blocked` para siempre.
- R1019 #10 y R1021b, parte A: con el índice `(OwnerUserId, ChannelId)`, un operador tenía una sola operación de IA para todos sus chats. Se resolvió con un ámbito por conversación y un test de tres chats que retiene el primer proveedor y exige que los otros dos terminen.
- R1023 #1: importar no tomaba los locks de los catálogos fuente que sí tomaba el PUT.
- `1e82f704`: el test del orden de locks pasaba algunas veces, porque dos Guid v7 del mismo milisegundo tienen orden aleatorio. Ahora usa Ids en orden inverso.
- R1023 #10: una reescritura de 10.000 filas retenía el lock que también usa el envío.
- R1021b #6: el offset sobre una cola que se compacta salteaba tareas cuando decidía otro aprobador.
- Plan de E1, §0: intercambiar dos códigos en un mismo guardado daba 500. Se resolvió con unicidad diferida.

**Causa común.** El alcance del índice o del lock se eligió por comodidad, no por lo que había que excluir. Las rutas hermanas no comparten el orden de los locks.

**Cómo se evita.**
- Escribir qué se excluye («un turno por conversación») y derivar de ahí el índice.
- Un test con dos actores, dos conversaciones o dos canales simultáneos.
- Todas las rutas toman los locks en el mismo orden.
- El test de orden usa Ids que no salgan ordenados por casualidad.
- Recorrer colas por clave estable, no por posición.

### 4.5 Reescrituras masivas y lecturas sin tope

- R1023 #10: renombrar un catálogo hacía 10.000 UPDATE, con auditoría a nombre de esa persona.
- R1021b #2: la pantalla de entrada descifraba la versión activa de cada formulario para mostrar el nombre.
- R1021b #5: cada flecha traía todos los ids.
- R1018 #2 y #3: lecturas sin ventana contra topes que rompían el asistente.
- R1022 #3: celdas con estilo y otras hojas cargadas sin contar.
- R1018 #15: la retención limpiaba 500 por día.
- 2026-10-21: el asistente tardaba unos 35 s en responder un texto y 66 s un audio, por un sondeo de 30 s ([2.8](#28-correcciones-fuera-de-los-reviews)).

**Causa común.** Se reutiliza la lectura completa para una proyección chica, y se asigna en lugar de comparar.

**Cómo se evita.**
- Guardar solo lo que cambió: comparar el valor normalizado antes de asignar.
- Proyecciones livianas y ventanas en SQL.
- Medir con el volumen máximo declarado (10.000 opciones, 100.000 respuestas).
- Medir la latencia de punta a punta del asistente con reloj real.

### 4.6 Topes y unidades desalineados entre capas

- R1018 #3: `Take(101)` contra una capacidad de 100.
- R1018 #10 y R1019 #9: 25 MiB contra 25.000.000 bytes. Alinear el cliente compartido rompió los Flows.
- R1022 #12: «hasta 200» en una pregunta de varias, con tope 20.
- R1023 #14 y #16: etiquetas y avisos calculados sobre las primeras 20 opciones.
- R1023 #18: el tope de 20 en varias dejó de frenar la publicación.
- R1019 #13 y #15: 16 KiB medidos sin el envoltorio, y UTF-16 contra grafemas.

**Cómo se evita.**
- Cada tope es una constante con su unidad y una lista de dónde se aplica.
- Un test del borde en cada consumidor.
- Nunca calcular sobre la página cargada lo que habla del total.

### 4.7 Permisos que filtran nombres o datos

- R1023 #11: «En uso» y el 409 daban nombres e IDs de formularios a quien solo podía leer catálogos, aunque el conteo ya se ocultaba.
- R1023 #20: una vista previa sin el permiso de leer los datos que muestra.
- R1021a, «Después»: «No se puede eliminar porque tiene respuestas» a quien solo administra. En el front hubo un ida y vuelta: f: `589ff76` y después `72a3947`.
- R1021a #14: el texto enviado quedaba en la caché de mutaciones.
- R1018 #9: datos de terceros en temas de pedidos.
- R1018 #12: conversaciones de IA sin retención.
- R1023, «Después»: la búsqueda de personas en la URL.
- R1021b, «Después»: un 404 antes de revalidar el permiso.

**Causa común.** La proyección se arma completa y el permiso se mira en otro lado. Los 409 y los metadatos de error no pasan por el filtro.

**Cómo se evita.**
- Toda respuesta que nombra otro recurso, incluidos los errores, se filtra por el permiso de leer ese recurso.
- Un test con un actor que tiene solo el permiso propio.
- *En el multitenant, por extensión:* el mismo control con datos de otra organización.

### 4.8 Estados sin salida y trabajo que se repite para siempre

El 06 ya tiene la regla. Ejemplos nuevos:
- R1021a #1: un 4xx de deprecación leído como «sin efecto» repetía el POST cada pocos segundos, sin que se viera.
- R1018 #1 y #5: `Blocked` y Pending eternos.
- R1019 #8: el rechazo de una oferta nunca se registraba y se volvía a ofrecer.
- R1019 #18: «Transcribiendo…» para siempre.
- R1018 #11 y R1019 #11: una falla local tratada como incertidumbre del proveedor.

**Cómo se evita.**
- Clasificar cada respuesta del proveedor como definitiva, transitoria o desconocida, con un test por clase.
- «Incierto» solo si de verdad se mandó algo.
- Cada estado tiene una salida visible.

### 4.9 Cambios de definición que dejan datos huérfanos o inválidos

- R1023 #1: dar de baja una opción fuente bloqueaba todo.
- R1023 #6, #7, #8 y #22: cambiar tipo, fuente o papel sin convertir, sin limpiar o sin frenar.
- R1021a #2, #3 y #12: definiciones eliminadas que rompían una lectura o se contaban.
- R1022 #2: una columna ausente borraba datos.
- R1022 #10: un Down que ignoraba los borradores.

**Causa común.** Se piensa el estado final, pero no la transición, ni los datos que ya existen con la definición anterior.

**Cómo se evita.**
- Para cada propiedad que se puede cambiar (tipo, fuente, papel, obligatorio, activo, eliminado), una matriz: qué pasa con los valores guardados, con los usos y con lo sellado.
- Lo guardado se acepta aunque apunte a algo dado de baja; solo lo nuevo exige activo.
- Un cambio con datos pide confirmación y limpia o convierte con el texto visible.
- Un cambio con usos se frena con `uses`.

### 4.10 Errores mostrados antes de tiempo

- R1023 #36: «Agregar opción» marcaba la fila en rojo enseguida.
- R1023 #38: al agregar una acción a una regla, aparecía «Completá este dato».

La guía visual ya decía «Se valida al enviar; después del primer error, en vivo mientras se corrige», y no se cumplía.

**Cómo se evita.** Cada formulario o editor guarda si ya se intentó guardar. Un test agrega una fila vacía y comprueba que no hay errores hasta Guardar.

### 4.11 Textos explicativos junto a botones y estados de guardado

- R1023 #37: «Guardá el catálogo antes» desalineaba los botones.
- R1023 #38: «No hay cambios para guardar», «Revisá los datos marcados antes de guardar» y «Cambios sin guardar» en diez pantallas, y números de revisión junto a Guardar.
- R1023 #39: textos que explicaban que algo no se puede cambiar.

**Cómo se llegó.** Hay tres causas documentadas:
1. La [guía visual](../../../../ArquitecturaBaseFront/docs/design/visual-baseline.md) decía «Al guardar, el botón se deshabilita y explica el estado», y daba «Cambios sin guardar» como ejemplo de estado. Se corrigió en f: `bb98bf2`.
2. R1017 #14 marcó «Guardar reglas queda deshabilitado sin motivo visible», y el arreglo agregó el motivo como texto.
3. El 06 dice «Acciones que pueden no hacer nada: se ocultan o se explican». Desde el 2026-10-23 se lee así: la explicación va en un tooltip, nunca como texto visible.

**Cómo se evita.** Aplicar la regla de la guía desde el primer día:
- un botón está habilitado o deshabilitado, sin texto al lado;
- el motivo va en un tooltip y en el nombre accesible;
- las acciones van al borde derecho del encabezado;
- no hay «Cambios sin guardar» ni revisiones a la vista.

### 4.12 Migraciones

- R1021b #1: el CRLF en un filtro de índice daba verde en Windows y rompía el despliegue en Linux. Arreglo: `MigrationSqlPortabilityTests` y la normalización de 19 SQL multilínea. En R1022 ya estaba aplicado.
- R1022 #10: un Down que ignoraba los borradores.
- R1019 #16: la migración de cuotas exige PostgreSQL 16 (`IS JSON OBJECT`).
- `703c9348`: los tests de migración sobre un esquema intermedio materializaban la entidad actual y fallaban con 42703 al sumar una columna.
- R1023, «Después»: volver a una build anterior a E2 ignora `match` y manda las listas sin filtrar.

**Cómo se evita.**
- SQL y filtros en una línea, con un test de portabilidad, y las migraciones corridas en Linux antes de cerrar.
- El Down mira también los borradores y se niega antes de perder datos.
- Los requisitos de versión van en la guía de despliegue.
- Los tests de esquemas intermedios proyectan columnas.
- Se anota qué hace el código viejo con los datos nuevos.

### 4.13 Documentación de diseño y reglas que inducían errores

- La guía visual, ver [4.11](#411-textos-explicativos-junto-a-botones-y-estados-de-guardado).
- `formularios.md` decía que el error de conversión iba «sin contenido de otras filas», y eso contradecía el diálogo de CE1, que muestra fila, valor y motivo. Lo corrigió b: `d9667c5a`.
- La regla de idioma del 2026-10-19 dejó escrito que «los catálogos comunes conservan su metadata bilingüe»: de ahí sale R1023 #35.
- El propio R1023 escribió primero el punto 35 como «el inglés es opcional», y hubo que corregirlo a «el original es el idioma del menú» (b: `2b804161`).
- La spec de E1 nombraba archivos y un código de error que no existían (R1022, «Después»).
- R1019 #2: el review propuso un arreglo que contradecía una decisión, y el contraste lo frenó.

**Causa común.** Un ejemplo en una guía es una instrucción para el agente que la lee. Una excepción escrita «por ahora» se aplica como regla.

**Cómo se evita.**
- Cuando el usuario decide algo que contradice una guía, se corrige la guía en el mismo momento y se busca el patrón en todo el código.
- Las excepciones se evitan o llevan una condición de retiro.
- El review contrasta sus propuestas con las decisiones antes de aplicarlas.

### 4.14 Arreglos incompletos o en un solo camino

- R1019 encontró cuatro arreglos de R1018 a medias (#11, #13, #7 y #22) y dos que rompieron otro camino: R1018 #4 rompió el rechazo de la segunda oferta (R1019 #8), y R1018 #10 rompió los archivos de Flows (R1019 #9).
- R1022 #1 se arregló en la importación, pero volvió en el cambio de tipo (R1023 #5).
- R1017 #7 y #15 ya eran arreglos incompletos de la Etapa 3.
- R1023 #1 necesitó tres correcciones de seguimiento (b: `f02af5dd`, `781c1cea`, `00dc20af`).

**Cómo se evita.**
- Al arreglar, buscar el mismo patrón en todo el código (el método, la constante, el formato) y poner un test en cada camino.
- Si el arreglo mueve un control en un componente compartido, probar a todos sus consumidores.
- El review siguiente revisa los arreglos, no solo lo nuevo.

### 4.15 Tests que pasan por la razón equivocada

- R1021b #1: verde en Windows y rojo en Linux.
- `1e82f704`: un test de orden que pasaba por azar.
- R1023 #8: un test que fijaba la falla como esperada.
- R1023 #34: los tests del back fijaban las minúsculas y los dobles del front, las mayúsculas.
- R1019 #16: un test que dejaba datos en la base compartida.
- R1018 #16: un test de arquitectura que decidía por nombre.

**Cómo se evita.**
- Un test que fija un comportamiento raro se contrasta con el contrato antes de adaptarlo.
- Los tests de orden y concurrencia usan datos que no salgan ordenados por casualidad.
- La integración corre también en Linux.

### 4.16 Estado viejo en el front

- R1021a #4 y #7: falta de invalidación después de eliminar o dar de baja.
- R1023 #12: un diálogo con la definición de cuando se entró.
- R1023 #13: una etiqueta que sobrevive a su valor.
- R1023 #26: una tabla que no se actualiza.

**Cómo se evita.**
- Al confirmar una escritura, invalidar todas las lecturas que la muestran.
- Los diálogos reciben el estado confirmado más reciente.
- Al vaciar un valor, se borra también lo que se deriva de él.

### 4.17 Literales duplicados y fronteras entre módulos

- R1021a #3b: el perfil vigente repetido como texto.
- R1021a #13: IDs de canal escritos a mano en BusinessForms.
- R1023 #21: campos sellados que pasaban al contrato del núcleo.

**Cómo se evita.** Una constante con un solo dueño. El test de frontera busca también los literales.

### 4.18 Proceso

- **El porqué no queda en Git.** Los 149 commits `fix` del período de Codex no tienen cuerpo.
- **Verde no es correcto.** E2 cerró localmente con miles de tests, visuales y un recorrido en el navegador, y el review encontró cuatro hallazgos de gravedad alta. El 06 ya lo decía; los números de E2 lo confirman.
- **El uso real encuentra lo que nadie buscó.** El chat de Inicio estuvo roto por lo menos dos semanas.
- **El contraste antes de corregir funcionó.** En R1019, tres de veinte puntos no requerían un cambio de producto: dos ya estaban protegidos y uno contradecía una decisión. Conviene mantenerlo.
- **«Después, sin bloquear» se acumula.** Cada review deja su lista, y no hay un lugar único que la siga.

## 5. Lista de verificación «no repetir»

Complementa la lista del 06; no repite sus ítems. Cada casilla se marca con evidencia (un test, un ejemplo exportado o una captura), no con una afirmación.

**Antes de programar la etapa**
- [ ] El tablero y el plan no dicen nada que contradiga la guía visual vigente. Si el usuario decidió algo nuevo, ya está en la guía.
- [ ] Ningún modelo, validador ni recurso fija «es» como idioma por defecto u obligatorio: el original es el del menú (R1023 #35).
- [ ] No hay excepciones «por ahora» a una regla general. Si las hay, tienen su condición de retiro.
- [ ] Cada tope nuevo tiene una constante, su unidad (bytes decimales o MiB, UTF-16 o grafemas) y la lista de sus consumidores.
- [ ] Para cada propiedad editable de algo que tiene datos o usos (tipo, fuente, papel, obligatorio, activo, eliminado) está escrito qué pasa con los valores guardados, los usos y lo sellado.
- [ ] Cada índice único y cada lock dicen qué excluyen (por persona, canal o conversación).

**Contratos**
- [ ] Cada endpoint que consume el front tiene ejemplos exportados de la corrida real, también los estados y los errores de negocio (409 con `uses`, 400 con filas). Sin dobles a mano donde hay ejemplo.
- [ ] Los enums viajan con una sola convención en todo el contrato.
- [ ] Los guards del front son exactamente el contrato: ni más estrictos ni más laxos. Los avisos y errores van como unión discriminada por `code`.
- [ ] Hay una prueba de ida y vuelta (guardar y releer) con traducción vacía, null y una referencia inactiva.

**Backend**
- [ ] Un guardado acepta lo guardado que apunta a algo dado de baja, y exige activo solo a lo nuevo, cambiado o convertido.
- [ ] Un guardado sin cambios no escribe: un test comprueba que no cambian `ModifiedAtUtc` ni la revisión.
- [ ] Cada camino que lee números o fechas usa la misma pieza, con la cultura del pedido, y rechaza lo ambiguo. Hay tests con «1.500», «1,500», «12.345» y «3/4/2026» en es y en.
- [ ] Las rutas hermanas toman los locks en el mismo orden, y el test usa Ids en orden inverso.
- [ ] Hay un test con dos conversaciones, canales o actores simultáneos para cada exclusión.
- [ ] El SQL directo incluye los filtros globales: el borrado lógico y, en el multitenant, la organización. Hay un test con un eliminado y con datos de otra organización.
- [ ] Ninguna lectura trae todo para quedarse con poco: ventanas y proyecciones en SQL, probadas con el volumen máximo.
- [ ] Cada respuesta del proveedor externo está clasificada (definitiva, transitoria, desconocida) y cada estado tiene salida.

**Permisos y privacidad**
- [ ] Nada nombra otro recurso sin el permiso de leerlo, tampoco un 409 ni un aviso. Hay un test con un actor que tiene solo el permiso propio.
- [ ] Una vista previa o simulación exige el permiso de leer los datos que muestra.
- [ ] No hay búsquedas con datos personales en la URL.

**Front**
- [ ] El front no valida lo que decide el servidor: manda el pedido y muestra la respuesta.
- [ ] Solo el conflicto de revisión pone el editor en revisión; cualquier otro 409 es un error editable.
- [ ] Un error de un campo que no está a la vista se hace visible, cambiando de pestaña o en la página.
- [ ] Se valida al intentar guardar. Un test agrega una fila vacía y no ve errores.
- [ ] Ningún botón tiene texto al lado: el motivo va en un tooltip. Las acciones van a la derecha del encabezado, y no hay «Cambios sin guardar» ni revisiones a la vista. Esto actualiza el ítem del 06 «se ocultan o se explican».
- [ ] Las etiquetas se resuelven por Id, y los avisos sobre el total los calcula el servidor.
- [ ] Los diálogos reciben el estado confirmado más reciente; vaciar un valor borra lo que se deriva de él.
- [ ] Fechas y números se formatean desde una sola pieza por tipo.

**Migraciones**
- [ ] SQL y filtros en una línea, con un test de portabilidad, y las migraciones corridas en Linux.
- [ ] El Down mira también los borradores y se niega antes de perder datos.
- [ ] Los requisitos de versión y lo que hace una build anterior con los datos nuevos están en la guía de despliegue.

**Al cerrar**
- [ ] Cada arreglo buscó el mismo defecto en los caminos hermanos y en los consumidores del componente compartido.
- [ ] Ningún test fija un comportamiento raro sin haberlo contrastado con el contrato.
- [ ] Alguien usó la app como la va a usar el cliente, incluidas las áreas vecinas (Inicio, por ejemplo): crear, equivocarse, cambiar de idioma y volver.
- [ ] El review contrastó cada hallazgo antes de corregirlo: confirmado, ya protegido o contrario a una decisión.
- [ ] El review siguiente revisa los arreglos del anterior.
- [ ] Cada commit `fix` dice en el cuerpo qué pasaba y por qué.
- [ ] «Después, sin bloquear» tiene un lugar único y se revisa en cada etapa.

**Piloto**
- [ ] Si se agrupan pilotos, la lista de lo que no tiene prueba física está escrita y la siguiente etapa la hereda.
- [ ] Los datos cubren las ramas: catálogo con opciones, continuación, un faltante de traducción por pieza, Manual sin prellenado, varios contactos y una pregunta de varias vacía.
- [ ] Cada variante se abre en un celular, no solo se mira la aceptación de Meta.
- [ ] El esquema de herramientas se probó contra el proveedor de IA real en modo estricto.
- [ ] Se registran las versiones e IDs usados y se restaura la configuración.

## 6. Huecos de este anexo

- **Gravedad asignada.** En R1017, R1018, R1019, R1021a y en R1023 #34–#39 la puso este anexo (con asterisco), según el efecto descrito. Los reviews no la declaran.
- **R1023 en curso.** Los estados son los de los commits al corte (back `d7f1cad7`, front `ed1a62d`). No hay sección de cierre ni corrió la suite completa. Del punto 7 no se encontró manejo de `ValueRemovalNotConfirmed` en el front. Del punto 10 no hay ningún commit que lo cite; `4537fc26` y `b75f40cd` tocan el mismo código, pero no se verificó si lo resuelven.
- **Commits asignados a puntos.** R1018 y parte de R1019 no listan los commits del backend. Se asignaron por el asunto y la hora de cada commit, sin revisar cada diff.
- **R1017.** El cierre sale del propio review y del registro de cierre en `formularios.md`; no se volvió a verificar en el código.
- **Autoría.** Que los commits sin coautoría de Claude sean de Codex se infiere del contexto del traspaso; Git no lo registra.
- **Latencia.** No está documentado qué disparó la mejora del 2026-10-21.
- **Front.** El front no tiene reviews propios. La evidencia de diseño (`docs/design/formularios-whatsapp.md`, `catalogos-mejoras.md`) no se leyó en detalle.
- **Correcciones sin cuerpo.** Para los `fix` de [2.8](#28-correcciones-fuera-de-los-reviews), el escenario y la causa no se pueden reconstruir sin leer cada diff.
