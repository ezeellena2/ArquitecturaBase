# BusinessForms: validación y recursos

Leer [formularios](../../../../docs/features/formularios.md). Probar presupuestos, defaults, paths y paridad de textos sin proveedores externos.
Los límites de candidatas se prueban con grafemas compuestos y exclusión individual que conserva la parcialidad. La privacidad de pedidos faltantes se acredita en propuesta y captura, con NFKC, nombres sin tildes por palabra completa, fechas y montos válidos y cambios de cuenta bajo lock.
Dobles sólo del borde; las garantías PostgreSQL/DataProtection/restore pertenecen a integración real.

Capacidad Manual: crear/editar hasta 10.000 filas, delta contra el total almacenado, rechazo de 10.001 y duplicado con `items[i].code`; `moves` conserva 500. Condiciones prueban lectura completa de 10.000, incluidas inactivas, y una lectura por catálogo compartido sin paginado.

Importación: plan puro por código con Append/Replace, errores localizados por fila y columna, ítem existente intacto si su fila falla, confirmaciones exactas y revisión. Cubrir plantilla es/en, la columna de traducción según el original (`textEs`/`detailEs` con original inglés, export y reimportación sin diferencias; la del idioma del original es desconocida), todos los códigos ambiguos, capacidad con inactivas y exportación/reimportación sin diferencias ni pérdida de precisión. Lector XLSX/CSV y archivo físico pertenecen a los tests técnicos de Infrastructure; HTTP y locks reales, a integración.

Importación: permisos y revisión antes del archivo y después de leer; parseo fuera del límite, hash y confirmaciones antes de mutar, locks del catálogo y sus fuentes ordenados, un commit y una revisión con marca UTC. Preview no guarda ni devuelve mapas locales; las filas inválidas conservan ítems/posiciones. PUT ordena todo por el texto original con es-AR o en-US según el original del catálogo, preserva valores y rechaza combinar A–Z con movimientos. El aislamiento real de rollback y lector físico queda en integración.

Publicación multiChoice: límite de 20 activos sólo para catálogo Manual, conteo SQL delegado una vez por catálogo compartido y error con FieldId/path propios. Choice ordinario, Filters y catálogos inválidos conservan sus recorridos.
Captura multiChoice: array GUID D distinto, min/max sellados, orden Position 0..N-1 y referencia ofertada por opción; omitida no agrega entrada, [] conserva array real sin filas; replay/huella escalar y atomicidad de metadata junto a archivos permanecen.

Campos extra: la captura prueba un lote para elegidas y otro para textos fuente, ambos después de los locks Manual. Sella sólo campos activos con valor, incluido cero/false y los nombres originales incompletos; replay conserva ese snapshot después de editar los datos. Filters e historia sin campos mantienen null y omiten la propiedad nueva al serializar.

Oferta/captura tipadas: pares Value/ValueType canónicos, tipo cerrado y validación antes de subir archivos. Una edición del catálogo no cambia la oferta ya sellada ni sus referencias finales/propuestas. Text, Filters y filas históricas mantienen los bytes JSON sin propiedades nuevas nulas; cambiar un valor ofrecido invalida el replay.

- Composición de chequeo rechaza ChannelId duplicado/vacío. Dobles neutrales en TestFeatures/Channels; los que usan tipos/errores BusinessForms viven en TestFeatures/Modules/BusinessForms.

D5: nombre por cultura, vacíos secundarios/normalización, original obligatorio, ayuda opcional/parcial y completitud con paths/cultura/IDs. Publicación distingue original bloqueante y secundaria informativa; compara culturas sumadas/perdidas y conserva las culturas selladas de activa/status. Duplicados sólo originales, sin nueva regla secundaria por inferencia.

BusinessFormRecordSinkTests y BusinessFormRecordSourceTests verifican autorización, schema sellado, replay/conflicto, precisión y distinción de ausencia/false con dobles de repositorio/puertos; PostgreSQL y constraints se prueban en Api.IntegrationTests.

Planilla/Excel: FieldId/versión, ausencia/vacía/[] y cero/false; multiChoice conserva lista, orden y referencias selladas, anyOf por intersección, opciones de reglas por catálogo compartido y rechazo de sort. El export prueba permisos/tope/revocación, etiquetas y valores canónicos unidos por '; ' y headers sin filas; SQL/XLSX físicos pertenecen a integración.
La navegación anclada prueba permiso propio, validación antes de leer, snapshot del filtro, cultura y descarte ante revocación sin exponer contenido ni el resultado de ausencia.

FormCatalogHistoricalLanguageTests cubre oferta y referencia por idioma, respaldo individual al texto/detalle original del ítem sin traducción (también al inglés cuando es el original del catálogo), una referencia sólo en el idioma de su catálogo que no deja incompleto el original del formulario, la captura de una opción ofrecida sólo en ese idioma dentro de un formulario de otro y ausencia de respuestas duplicadas en nuevos snapshots. El respaldo total al original responde a los demás textos usados de la presentación. FormCatalogOriginalLanguageTests y los casos de idioma de FormPersonDataServiceTests prueban que sólo el original es obligatorio (la cultura del pedido al crear y la guardada al editar) y el path del primer texto original que falta. PDF verifica lectura por Id con autor/decisión fijados y referencias históricas localizadas antes del renderer.

Preview PDF verifica fotos y arrays de ejemplo dentro de Selection sin persistencia. La completitud incluye el nombre del formulario es/en y usa el NameJson capturado para PDF y avisos aunque el borrador actual esté completo; nunca mezcla el idioma secundario incompleto.

La presencia tiene expectativas explícitas para pregunta ausente, opcional vacía y respondida, tanto escalar como archivo: answered false/false/true, unanswered true/true/false y notAsked true/false/false; cero y false son respuestas presentes.

FormExportServiceTests verifica el presupuesto de 30 segundos en lectura/escritura, su renovación por pedido y la precedencia de cancelación del caller, incluso si vence el presupuesto al mismo tiempo. Un resultado tardío se descarta; no se afirma que ClosedXML síncrono pueda interrumpirse a los 30 segundos.

FormOfferWhenTests verifica normalización, límite, controles/persistibilidad, ausencia de traducción y aislamiento del schema/huella de publicación. La serialización de instrucciones deshabilitadas conserva los bytes del request anterior para el replay de comprobantes existentes.

C2 prueba normalización de seis escalares, precisión decimal, cuenta activa y permiso propio, lectura acotada por destinatario sin locks, observación de una sola instantánea y codec cerrado, exclusión de archivos, opciones actuales y selección de campos sin sustitución. Una elección sin formato GUID D se omite sin perder los otros valores; GUID ajeno o vacío, identidad y tipo discrepantes rechazan la propuesta. El recorrido Execute→ParseOutput comprueba la salida normalizada. Los formularios no preparables se excluyen sin retirar otras candidatas. El presupuesto de cantidad/bytes conserva referencias válidas y marca parcial, incluyendo el envelope completo de la herramienta; H7 no permite probar ausencia con ese snapshot.

FormSuggestionPolicy/Mapping/History/Service verifican revisión independiente, permisos, orden y activación, gramática de condiciones existente, seis tipos escalares y RecordNumber a texto, ausencia frente a cero/false y opciones efectivamente ofrecidas. El helper de opciones conserva la cobertura de reglas. Los snapshots de versión se usan para evitar atribuir etiquetas actuales a una revisión histórica distinta.

H5 verifica dos/tres elecciones realmente incluidas, codec cerrado sin pregunta libre, revisiones derivadas y una única lectura observada del mismo turno/cultura/destinatario. Revalida publicación/configuración/permisos y catálogos actuales; pregunta fija es/en, cero locks y ningún registro confirmado.

Sugerencias s1–s4: cubrir Always sin propuesta, origen exacto del contexto del servidor y lecturas de candidatas del mismo turno/destinatario, propuesta derivada de una sola lectura, mapeos inalterables, revisión y catálogos actuales, publicación nueva válida para Always y bloqueo automático de ancestros como hecho. Asistencia conserva incluidos/cambiados/mapeados; el detalle usa modo del set capturado aunque cambie la configuración y descarta toda la respuesta ante revocación durante esa lectura.

H5 selección prueba revisión auténtica por botón, pertenencia a la propuesta, preparación real sin prellenado inventado, catálogo vigente y locks sólo al enviar. La cultura del Flow y el motivo fijo de selección pasan enteros al original si el secundario está incompleto.

La privacidad de sugerencias históricas prueba Always y Assistant en vista previa/envío: actor activo y permiso propio antes/después, revocación durante la lectura y retiro completo antes de entregar valores al modelo. Sólo el contexto automático exacto Actor igual a Subject puede consultar su propia captura sin RecordsRead; H2 nuevo no recibe ese requisito.

FormSuggestionAuthenticSelectionTests verifica derivación de IDs sólo desde propuesta/observación auténticas, conflicto de set actual, discrepancia explícita y ausencia de autoridad sin lecturas; conserva selección del operador, campos propuestos y mapeos. Always no obtiene IDs implícitos. FormSuggestionAssistantBudgetTests verifica el envoltorio completo de lectura/propuesta en el límite exacto y un byte por encima; una lectura excedida es Failure recuperable, sin truncar datos ni conceder autoridad.

FormAssistantChannelContextTests usa IDs opacos y modo neutral explícito para sugerencias, catálogos y pedidos faltantes. Manual, Unspecified y valores no definidos no reciben la excepción automática por llevar un ID privado conocido; conserva permisos propios, Actor/Subject y procedencia.

FormApprovalContinuationServiceTests cubre contrato, dirección/tamaño/definición, actor, permiso antes y después de leer, ancla ausente y ausencia de transacción en esta lectura.

Datos de personas: permisos OR/AND y revocación, revisión por fila/atributo, delta que conserva ausentes/inactivos y limpia null, source/opción activa, aislamiento por persona, máximo20, usados y confirmación que limpia sólo ese valor al cambiar tipo o fuente; la confirmación cuenta sólo personas no borradas y la limpieza alcanza a las borradas. La tabla pide los faltantes sólo de los datos activos, con su búsqueda, y descarta todo si el permiso cae durante esa lectura. El lector de opciones devuelve sólo id/text sin CatalogsRead. Casos PostgreSQL y locks reales van a integración.

E2-3 cubre canónico de Match, ausencia que nunca amplía oferta, huella exacta del esquema anterior y Source/Shown nulos omitidos. Preparación y asistente comparten cultura efectiva y límites; preview conserva exceso/títulos inválidos sólo para diagnóstico y revalida permisos también al devolver ausencia. El máximo por persona del editor da el grupo más grande con su etiqueta (o el conteo compartido sin etiqueta con sólo filtros fijos), rechaza un Match inválido sin leer el máximo y revalida permisos después de leerlo. Publicación separa bloqueos/avisos, máximo y unión de faltantes.

E2-4 verifica nombres/valores históricos es/en, precisión, ausencia y orden; selección Excel independiente por pregunta/campo, nulos/duplicados y tipos escalares. Chat omite extras; columnas no habilitan sort ni condiciones.
