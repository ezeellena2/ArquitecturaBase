# BusinessForms: validación y recursos

Leer [formularios](../../../../docs/features/formularios.md). Probar presupuestos, defaults, paths y paridad de textos sin proveedores externos.
Dobles sólo del borde; las garantías PostgreSQL/DataProtection/restore pertenecen a integración real.

- Composición de chequeo rechaza ChannelId duplicado/vacío. Dobles neutrales en TestFeatures/Channels; los que usan tipos/errores BusinessForms viven en TestFeatures/Modules/BusinessForms.

D5: nombre por cultura, vacíos secundarios/normalización, original obligatorio, ayuda opcional/parcial y completitud con paths/cultura/IDs. Publicación distingue original bloqueante y secundaria informativa; compara culturas sumadas/perdidas y conserva las culturas selladas de activa/status. Duplicados sólo originales, sin nueva regla secundaria por inferencia.

BusinessFormRecordSinkTests y BusinessFormRecordSourceTests verifican autorización, schema sellado, replay/conflicto, precisión y distinción de ausencia/false con dobles de repositorio/puertos; PostgreSQL y constraints se prueban en Api.IntegrationTests.

Planilla/Excel: probar columnas por FieldId y versión, ausencia frente a opcional vacía, cero/false, operadores tipados y permisos antes/después. El export usa dobles de lector/escritor para tope y revocación; la validez física del XLSX y el SQL se verifican en integración.

FormCatalogHistoricalLanguageTests cubre oferta y referencia por idioma, respaldo individual al texto/detalle original del ítem sin traducción y ausencia de respuestas duplicadas en nuevos snapshots. El respaldo total al original responde a los demás textos usados de la presentación. PDF verifica lectura por Id con autor/decisión fijados y referencias históricas localizadas antes del renderer.

Preview PDF verifica fotos de ejemplo sin persistencia. La completitud incluye el nombre del formulario es/en y usa el NameJson capturado para PDF y avisos aunque el borrador actual esté completo; nunca mezcla el idioma secundario incompleto.

La presencia tiene expectativas explícitas para pregunta ausente, opcional vacía y respondida, tanto escalar como archivo: answered false/false/true, unanswered true/true/false y notAsked true/false/false; cero y false son respuestas presentes.

FormExportServiceTests verifica el presupuesto de 30 segundos en lectura/escritura, su renovación por pedido y la precedencia de cancelación del caller, incluso si vence el presupuesto al mismo tiempo. Un resultado tardío se descarta; no se afirma que ClosedXML síncrono pueda interrumpirse a los 30 segundos.

FormOfferWhenTests verifica normalización, límite, controles/persistibilidad, ausencia de traducción y aislamiento del schema/huella de publicación. La serialización de instrucciones deshabilitadas conserva los bytes del request anterior para el replay de comprobantes existentes.

C2 prueba normalización de seis escalares, precisión decimal, cuenta activa y permiso propio, lectura acotada por destinatario sin locks, observación de una sola instantánea y codec cerrado, exclusión de archivos, opciones actuales y selección de campos sin sustitución. Los formularios no preparables se excluyen sin retirar otras candidatas. El presupuesto de cantidad/bytes conserva referencias válidas y marca parcial, incluyendo el envelope completo de la herramienta; H7 no permite probar ausencia con ese snapshot.

FormSuggestionPolicy/Mapping/History/Service verifican revisión independiente, permisos, orden y activación, gramática de condiciones existente, seis tipos escalares y RecordNumber a texto, ausencia frente a cero/false y opciones efectivamente ofrecidas. El helper de opciones conserva la cobertura de reglas. Los snapshots de versión se usan para evitar atribuir etiquetas actuales a una revisión histórica distinta.

H5 verifica dos/tres elecciones realmente incluidas, codec cerrado sin pregunta libre, revisiones derivadas y una única lectura observada del mismo turno/cultura/destinatario. Revalida publicación/configuración/permisos y catálogos actuales; pregunta fija es/en, cero locks y ningún registro confirmado.

Sugerencias s1–s4: cubrir Always sin propuesta, origen exacto del contexto del servidor y lecturas de candidatas del mismo turno/destinatario, propuesta derivada de una sola lectura, mapeos inalterables, revisión y catálogos actuales, publicación nueva válida para Always y bloqueo automático de ancestros como hecho. Asistencia conserva incluidos/cambiados/mapeados; el detalle usa modo del set capturado aunque cambie la configuración y descarta toda la respuesta ante revocación durante esa lectura.

H5 selección prueba revisión auténtica por botón, pertenencia a la propuesta, preparación real sin prellenado inventado, catálogo vigente y locks sólo al enviar. La cultura del Flow y el motivo fijo de selección pasan enteros al original si el secundario está incompleto.

La privacidad de sugerencias históricas prueba Always y Assistant en vista previa/envío: actor activo y permiso propio antes/después, revocación durante la lectura y retiro completo antes de entregar valores al modelo. Sólo el contexto automático exacto Actor igual a Subject puede consultar su propia captura sin RecordsRead; H2 nuevo no recibe ese requisito.

FormSuggestionAuthenticSelectionTests verifica derivación de IDs sólo desde propuesta/observación auténticas, conflicto de set actual, discrepancia explícita y ausencia de autoridad sin lecturas; conserva selección del operador, campos propuestos y mapeos. Always no obtiene IDs implícitos. FormSuggestionAssistantBudgetTests verifica el envoltorio completo de lectura/propuesta en el límite exacto y un byte por encima; una lectura excedida es Failure recuperable, sin truncar datos ni conceder autoridad.
