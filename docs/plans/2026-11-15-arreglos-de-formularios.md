# Arreglos de la revisión de arquitectura de Formularios

Decididos el 2026-11-14 y sin programar. Salen de la [revisión de arquitectura de Formularios](../reviews/2026-11-14-arquitectura-formularios.md),
que tiene el escenario, la evidencia (archivo y línea en `a9f57202`) y el esfuerzo de cada hallazgo, y de las
[decisiones del usuario](../reviews/2026-11-14-arquitectura-formularios.md#decisiones-del-2026-11-14), que eligió las ocho
recomendadas y no se vuelven a preguntar. Va aparte del [índice de etapas de formularios](formularios/README.md), como el plan
del núcleo.

- **Alcance:** F1 a F31 (los de «Hoy» y la convivencia de versiones), B1 y B3, y de gran escala lo chico que eligió el usuario:
  el índice por fecha de F33, la consulta por lote de F37, contar sólo los documentos abiertos en F38, la purga de F40, el índice
  de carpetas de F47 y, de F48, ordenar por nombre antes de cortar. De las observaciones entran las que un arreglo toca de paso:
  O3 con F6, O8 con F18, O11 con F1 y O13 con F4. B2 entra sólo con su test; el arreglo, si se confirma, es del núcleo.
- **Fuera:** F32, antes de pasar de una réplica; el resto de gran escala (lo demás de F33, F34 a F36, F39, F41 a F46 y el aviso
  de «hay más» de F48), cuando haya volumen real que medir; las demás observaciones, que quedan anotadas en la revisión; y las
  pruebas reales de la revisión, que esperan al despliegue o a tener volumen.
- **Al día:** desde `a9f57202`, el único commit que tocó Formularios fue `42d8f47f` (H25 y N27 de WhatsApp): un motivo de lectura
  de documento desconocido se lee como `Other`, y la [guía de migración](../guides/migracion.md) pide escribir un valor nuevo un
  despliegue después del que ya sabe leerlo. La sesión de WhatsApp tenía cambios sin commitear en `BusinessForms/Channels/AGENTS.md`
  y `Models/FormActionModels.cs`. Cada etapa vuelve a leer sus archivos antes de empezar.
- **Sin revisor:** el cupo semanal estaba en 92 % al escribirlo, así que el plan se contrastó sólo con la revisión, sin un revisor
  contra el código. La primera tarea de cada etapa relee el código de sus hallazgos y corrige el plan si hace falta.
- **Sin datos que convertir:** no hay producción. Lo que se agrega a filas existentes (el estado de revisión de los temas, el
  plazo de los documentos propuestos) se completa en la misma migración con los datos de desarrollo.
- **Cuándo:** después del reinicio del cupo semanal (17/11, 23:59), en una sesión nueva: la revisión alcanza como brief. La etapa
  1 va antes de la prueba integral, que también quedó para después del reinicio; las demás, de a una y mirando el cupo antes de
  cada una.

**Cómo se trabaja.** Cada tarea sigue [validar un cambio](../guides/validar-un-cambio.md): el test en rojo primero y las suites
rápidas del área. La revisión adversarial va completa en lo que toca permisos, datos de personas o efectos fuera del sistema
(avisos, Meta, archivos, ARCA), y no en documentación, textos ni arreglos de una línea. Cada etapa cierra con el build sin
advertencias, los unitarios y `ArchitectureTests` enteros y la integración de lo que cambió; la corrida completa, con Docker y
los controles del front, va una sola vez, al cerrar el desarrollo. Además:

- una regla escrita que cambia se corrige con su arreglo, no antes, y cada tarea nombra las suyas. Casi todas están en
  [formularios](../features/formularios.md), el [detalle de los servicios](../features/formularios-servicios.md), los `AGENTS.md`
  del módulo, [WhatsApp](../features/whatsapp.md) (F1) y la [guía de migración](../guides/migracion.md) (F29 y F30);
- un error nuevo se declara en Domain, con su código estable y sus textos en `Errors.resx` y `.en.resx`;
- un contrato que cambia (la acción de descartar una publicación, la marca de acciones por resolver, el estado de un tema) cambia
  en la misma tarea que el front, el OpenAPI y los ejemplos exportados, sin capas de compatibilidad; una ruta nueva va a
  `ExplicitRouteInventoryTests` con su test;
- ninguna tarea agrega una pantalla. Si una marca, un filtro o una acción cambian la forma de una pantalla que existe (F2, F3,
  F7), se dibujan antes en el tablero, como pide la [línea visual](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md);
- lo que toca código de otro módulo se coordina con su sesión si sigue en curso: WhatsApp en F1, F2, F22 y B1; IA en F26 y F31;
  el núcleo en B2;
- los logs van con `[LoggerMessage]`, sin valores de respuestas, nombres de archivos ni teléfonos.

Las etapas van en orden, y dentro de cada etapa las tareas también, salvo en la 6 y la 7, donde son independientes.

### Etapa 1: los números (antes de la prueba integral)

1. **F1. Los números con la región del idioma** (decisión 5). En la app, `entryForm.ts` lee el número con la región del idioma
   de quien carga (en español, coma decimal y punto de miles; en inglés, al revés) y marca en su pregunta lo que admite dos
   lecturas, como «1.500» con un solo separador y tres cifras, para que la persona lo confirme, con el criterio de
   `FormCatalogValuePolicy`; el servidor sigue recibiendo el número canónico. Las preguntas con decimales muestran por defecto la
   ayuda «sin separador de miles», como el Flow. En WhatsApp, `WhatsAppFlowValueCodec` lee con la cultura del Flow y cambia
   `whatsapp.md:524`. De paso, `formularios.md:153` remite a la regla vigente (O11). Tests del front con «1.500», «1,500»,
   «1.234» y «72,5» en los dos idiomas, y del codec con las dos culturas. Chico.
   La app, hecha el 2026-11-15 en `8f518391` del front (sin la ayuda por defecto: el error alcanza); el Flow, hecho el 2026-11-15 en `0c88e7ea`
   (decodificador por idioma y `WhatsApp.Flow.NumberAmbiguous` por el camino de la validación fija; sin tocar el Flow compilado).

### Etapa 2: permisos y privacidad

2. **F4. Quien fijó los valores no aprueba.** La tarea guarda también a quien corrigió la respuesta o la pasó desde un documento,
   y la decisión, la bandeja y el conteo lo dejan afuera como al autor (`BusinessFormTask.cs:31`, `FormApprovalReader.cs:46,74`).
   De paso, la guarda que crea un borrador a nombre de una persona mira que su cuenta esté activa, como las demás (O13). Tests de
   integración: quien corrige una respuesta ajena, o pasa un documento, y es del rol que aprueba recibe 403. Chico.
3. **F5. Guardar una regla que cita respuestas pide «Ver respuestas»** (decisión 3). `FormRuleAccessGuard` pide `formrecords.read`
   cuando un aviso usa datos de preguntas, y lo dicen la descripción del permiso (`Permissions.resx:26`) y la regla del dominio.
   El editor de reglas muestra el rechazo con su texto. Test: sin «Ver respuestas», guardar esa regla da 403. Chico.
4. **F7 y F14. Los temas de «Formularios que faltan» se revisan antes de ofrecerse** (decisión 7). El tema suma un estado de
   revisión: uno nuevo, o uno descartado que reabre otro pedido, no entra en la lectura de candidatas de otras personas hasta que
   lo revisa alguien con «Diseñar formularios», y los que ya existen quedan sin revisar. El asistente recibe sólo el nombre, sin la
   descripción. La completitud de los temas se separa de la de los formularios (`BusinessFormAssistantCapabilitySource.cs:102,107,114`),
   y el registro sigue encontrando el tema por su nombre. La pantalla de «Formularios que faltan» muestra los temas sin revisar con
   su acción. Mediano.
5. **F20. El menú muestra sólo lo que la persona puede abrir.** `ListMenuFormsAsync` recibe a la persona y sus permisos y usa el
   mismo filtro de formularios visibles que el árbol, conservando el de los dados de baja; el test del menú suma una cuenta que
   sólo decide aprobaciones. Chico.
6. **B2. El test de la cuenta borrada.** Un test de integración: una cuenta con un rol, borrarla y preguntar sus permisos. Si los
   conserva, el arreglo (que el servicio de permisos no conceda nada a una cuenta borrada o inactiva) es del núcleo y se suma a su
   [plan](2026-11-14-arreglos-del-nucleo.md). Trivial el test.

### Etapa 3: reglas y acciones

7. **F2. Una regla que no puede seguir avisa** (decisión 2). Se consulta sola, con espera creciente, la acción que quedó en duda
   por un error local (la consulta nunca reenvía); lo que pide una decisión (un aviso que el canal no puede confirmar, una emisión
   en revisión) sigue a mano. La planilla marca las respuestas «Con acciones por resolver», con su filtro, y quien administra
   reglas ve un contador. A quien cargó le llega un aviso cuando su regla se frena, que es lo que pedía ARCA H11. El aviso que no
   puede salir porque la ventana de 24 horas está cerrada queda «No aplica» para esa persona, con su motivo, y no frena lo que
   sigue. Cambian las reglas del motor de acciones en [formularios](../features/formularios.md). Tests con `FakeTimeProvider`: un
   PDF en duda se confirma solo sin volver a generarse, y un aviso con la ventana cerrada deja seguir la regla. Mediano.
8. **F28. El worker reserva sólo los tipos de acción que conoce.** Filtro de tipos en la búsqueda de trabajo y un `switch` de
   efectos sin rama por defecto, con un test que recorre los tipos que acepta el editor de reglas y otro donde un tipo desconocido
   sigue esperando. Chico.
9. **F19. Queda quién pidió reintentar o consultar.** El run guarda quién y cuándo pidió el reintento, el reintento por persona y
   la consulta, y el detalle de la respuesta lo muestra. Chico.
10. **Tres reglas escritas** (decisión 4). Las horas del PDF van en la zona de la cuenta de quien cargó, con su nombre, en lugar de
    UTC (cambia `formularios.md:425` y los textos de `Pdf.resx`); el aviso a un rol saltea al mandar, como «No aplica», a quien ya
    no es del rol (cambia `formularios.md:271`); y ante un toque viejo en «¿La emito?», quien cargó recibe una vez «Esa respuesta
    ya no se puede emitir», en su idioma, mientras lo ajeno se sigue ignorando (cambia `formularios.md:1199-1204`; toca la
    respuesta del canal de WhatsApp). Chico cada una.
11. **B1. El «¿La emito?» que se vuelve a preguntar no ignora el toque.** Al retomar se reusa la vista previa guardada, que se
    guarda antes de mandar, o el id del mensaje sale de su contenido. Chico.
12. **B3. «Pedir aprobación» a un rol sin miembros se avisa.** El editor de reglas lo advierte, o la acción falla con un motivo
    propio. Chico.
13. **F18. Se puede seguir una respuesta de la captura al aviso.** Los logs de los workers llevan el id del run, de la respuesta o
    de la operación, y las dos purgas registran sólo el tipo de la excepción (O8). Una fuente de trazas del módulo guarda el
    contexto al crear el run y la operación y lo continúa en el worker, como WhatsApp desde H19. Chico los ids; mediano la traza.

### Etapa 4: publicación

14. **F3. Una publicación trabada se puede descartar.** Espera creciente para las operaciones en curso o en duda, como el retiro;
    una acción «Descartar esta publicación» para cualquiera con «Publicar formularios», con la revisión, que la cierra como fallida
    y deja que el puerto existente retire sus Flows; y el front sigue consultando, con espera larga, mientras dice «sin
    confirmar». Ruta nueva, con su test y su lugar en el inventario; el test de integración es la contraparte de
    `FormPublicationCrashHttpTests`: otra persona descarta y el formulario se vuelve a publicar. Mediano.
15. **F27. Las lecturas no descifran el manifiesto para saber los idiomas.** El detalle y el editor, el lector de versiones de las
    respuestas y el chequeo leen los idiomas sellados de la versión; descifran sólo el despacho, la repetición de la aceptación, el
    seeder, la vista oficial y el estado de una operación con problemas. Test: con un manifiesto ilegible
    (`UnreadableFormPublicationProtector`), el editor, la planilla y las aprobaciones responden 200. Chico.
16. **F17. La vista oficial tiene su propio límite.** Un límite propio para la vista oficial y la consulta del estado, la
    verificación reutilizada unos minutos, un plazo para la consulta del estado y el mismo tope de opciones que el Flow. Chico.
17. **F48. La lista de formularios del asistente se ordena por nombre antes de cortar.** Y el tope de 100 queda escrito. Trivial.

### Etapa 5: documentos

18. **F6. Un PDF hostil no frena la lectura** (con O3). Antes de PdfPig se inflan los flujos comprimidos con un tope acumulado;
    cada lectura tiene un plazo que contesta «ilegible» y libera su lugar de `PdfGate`; el worker de WhatsApp cuenta los intentos de
    un mensaje y lo deja ilegible en lugar de tomarlo para siempre; Inicio decide por los bytes, sin abrir, si la ruta admite un
    PDF; y una falta de memoria no se informa como archivo ilegible sin log. Tests con una bomba en la tabla de referencias y en un
    flujo de objetos, y con un PDF lento. Mediano.
19. **F9. El documento propuesto vence** (decisión 6). Un documento propuesto que nadie revisa, o uno descartado que alguien
    reabre, pasa a descartado con motivo «venció» a los 90 días (desde que vence su envío, o desde que se reabrió) y sigue la purga.
    Cambia la regla de los documentos recibidos (la decisión 9 de su plan, en [formularios](../features/formularios.md)). Test con
    el reloj adelantado. Chico.
20. **F13. El revisor ve la repetición al pasar un documento.** Se recalcula al leer el detalle o al pasar y vuelve como aviso que
    el revisor confirma con una marca en el pedido. Chico.
21. **F23. «Cómo reconocerlo» se marca como texto de la IA.** En DOC6 y en la zona de lectura del editor dice que lo escribió la IA
    a partir de un documento recibido y pide revisarlo. Chico.
22. **F31. Las extracciones en curso sobreviven a un despliegue.** El pedido guarda las versiones con que se selló y el contexto
    se rearma con ésas; un 404 del motor queda incierto hasta el vencimiento del pedido. Es el lado de Formularios de IA I4, cuya
    regla para el motor ya se decidió. Chico.
23. **F26. Quitar la IA con extracciones en curso.** La [guía para quitar la IA](../guides/quitar-ia.md) pide descartar o resolver
    antes las extracciones pendientes, y el pedido vence. Trivial.
24. **F38. El número del menú cuenta sólo los documentos abiertos.** Lo que cubre el índice por estado y fecha. Trivial.
25. **F40. La purga de documentos corre al arrancar y por lotes seguidos.** Una vuelta al arrancar y lotes mientras salgan llenos,
    cada uno en su transacción y con un tope de tiempo; test con 250 vencidos. Chico.

### Etapa 6: carga, planilla y catálogos

26. **F8. Un rechazo pasajero de la captura no queda pegado.** No se guarda como recibo un rechazo que puede cambiar (una versión
    que la copia no entiende, un permiso perdido en una carrera), o el front arma un pedido nuevo tras un 400 sin errores por
    pregunta; y los textos de esos dos errores sirven también en la app. Chico.
27. **F11. Un tope de exportaciones a la vez por copia.** Un limitador global (por ejemplo, dos a la vez) con 429 y `Retry-After`.
    Escribir el XLSX por partes queda para gran escala. Chico.
28. **F12. La planilla que se infla no pasa.** Se cuentan también los estilos y los textos compartidos, o baja el tope descomprimido,
    y hay un tope global de importaciones a la vez por copia; antes, medir el pico con una planilla de casi 100 MiB. Chico.
29. **F15. Los campos históricos de catálogo, en una tabla chica.** La unión de campos por formulario, catálogo y campo se escribe
    al capturar, nunca al leer, se rellena con lo existente en la misma migración y la planilla la lee por índice. Mediano.
30. **F16. Una regla no nombra una opción que no existe.** Guardar una regla, un filtro o una sugerencia comprueba que sus opciones
    existen, y borrar opciones toma antes el lock de definiciones. Chico.
31. **F21. «Agregar» un estándar resuelve sus catálogos dentro del guardado.** Por clave, después del lock, usando el que ya
    apareció; un doble clic devuelve el mismo borrador. Chico.
32. **F22. El canal dice por qué no pudo crear un borrador.** WhatsApp traduce el nombre repetido, el catálogo no disponible y la
    falta de permiso. Chico.
33. **F24. Crear un catálogo es idempotente.** Un id de pedido en la creación, como los borradores. Chico.
34. **F33. El índice por fecha de las respuestas.** Por formulario y fecha de recepción, sobre las vigentes; lo demás de F33 queda
    para gran escala. Trivial.
35. **F37. La captura revisa las opciones elegidas por lote.** Una consulta por catálogo que conserva el orden del rechazo. Chico.
36. **F47. El menú arma el índice de carpetas una vez.** Y las carpetas suman un tope, con su error. Chico.

### Etapa 7: versiones y documentación

37. **F29. Un CHECK que ata una columna nueva a una vieja va en el despliegue siguiente.** La regla en la guía de migración y un
    control de las migraciones. Chico.
38. **F30. La versión vieja tolera lo que escribe la nueva.** La regla general de la guía de migración con un control (CHECK nuevos,
    columnas que pasan a aceptar nulos, borrados junto con su reemplazo) y la lista versionada de las enumeraciones guardadas como
    texto; el comprobante de guardado se abre sin rechazar lo desconocido, o con su formato versionado. Mediano.
39. **F25. La guía para quitar Formularios, al día.** Las 29 tablas, la bandeja y DOC6, qué le queda a ARCA y lo del B5 del núcleo;
    un test compara las tablas de la guía con las del módulo, y se repite la prueba de quitarlo. Chico.
40. **F10. La retención nombra todo** (decisión 6). La fila de la retención en los [planes pendientes](README.md) suma lo que lista
    F10 y el borrado definitivo de una persona como un puerto del núcleo que implementa cada módulo. Trivial.
41. **Los números en claro de la bandeja, por escrito** (decisión 8). La regla de los documentos recibidos escribe el costo, y el
    cifrado de las copias de archivos queda con el N15 del núcleo. Trivial.

**Cierre.** Cada regla que cambió queda en su fuente, y la revisión anota qué commit arregló cada hallazgo. Las decisiones van a
las fuentes de las [premisas](../architecture/premisas-de-la-plantilla.md), y el plan se retira con la
[auditoría documental](../guides/auditar-documentacion.md).
