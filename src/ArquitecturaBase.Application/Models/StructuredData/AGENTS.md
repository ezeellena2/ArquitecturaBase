# StructuredData: contratos neutrales de formularios
Leer [formularios](../../../../docs/features/formularios.md) y los módulos opcionales del backend.
- Primer consumidor real: chequeo de publicación de BusinessForms y adaptador de canal instalado.
- DTOs tipados y cerrados: preguntas canónicas, presentación, culturas, compatibilidad y problemas. Una pregunta puede llevar el ID opaco `SourceCatalogId` para compilar un selector dinámico; resolver opciones por destinatario pertenece al envío, no al chequeo. Sin JsonElement, tipos de módulos, HTTP, EF, credenciales ni URLs del cliente.
- Perfil y huella de compilación son opacos para el consumidor; el canal conserva JSON/mapping/codec. No exponer hash/JSON técnico en HTTP ni usarlos como autoridad enviada por navegador.
- Sólo ampliar el puerto al existir consumidores reales. MultiChoice agrega Selection neutral (MinSelected/MaxSelected), opcional con JsonIgnore(WhenWritingNull); no importa tipos del módulo ni cambia los bytes anteriores. Se conserva en el chequeo y en el snapshot de publicación.
2b agrega consumidores reales de Accept/GetPublication/GetPreview. Requests incluyen operación/versión opacas, actor/permisos, snapshot/perfil/huella; responses tipados de progreso/issues y preview oficial con vencimiento exacto. Inputs y URL/huellas privados redactados. StructuredRecord también contiene requests/responses tipados de captura y lectura histórica, sin campos de proveedor, JSON técnico o datos de sesión.

K7 agrega ofertas tipadas (campo, catálogo, revisión, opciones) y problemas de preparación. Capture recibe únicamente la oferta protegida del servidor, nunca listas del empleado. Read devuelve referencias históricas sin consultar el catálogo; los contratos conservan compatibilidad de las sesiones selladas sin exponer proveedor, token o JSON técnico.

E1-B suma `Option.Value` y `Offer.ValueType` (`Integer`, `Decimal` o `Date`) canónicos y opcionales. Text, Filters y sesiones antiguas omiten las dos propiedades nulas, conservando sus bytes y huellas. La captura copia la pareja ofrecida a la referencia final y a la propuesta histórica; no la reconstruye del catálogo actual. Los tipos son strings neutrales, sin importar el enum del módulo. La lectura histórica agrega `Choice.Value`/`ValueType` sólo desde la referencia capturada y los omite cuando son null; no infiere un valor del texto ni relee opciones actuales.

F3 agrega entradas de archivo con bytes transitorios ignorados al serializar; su ToString es redactado. La lectura sólo entrega ID, nombre, tipo, tamaño y disponibilidad de miniatura, nunca claves o URLs del proveedor.

`StructuredRecordOrigin` conserva canal, operador, instante del envío y referencia opcional a conversación, con dirección enmascarada y ToString redactado. No expone tokens, payload de sesión ni teléfono completo.

`StructuredRecordActionMessage` fija ejecución, destinatario y texto ya resuelto; un documento lleva sólo metadata privada generada por el servidor, nunca SAS ni URL del proveedor. Mensajes y documentos redactan `ToString`. `StructuredRecordActionResponse` conserva evidencia independiente del transporte: un aceptado no se repite y un incierto sólo se consulta hasta tener evidencia suficiente.

StructuredRecordActionReview lleva decisión humana, actor y fecha originales; no es evidencia de entrega del proveedor. Redacta su representación y no acepta identidad ni tiempo del navegador.
Review incluye la revisión esperada original; Retry recibe el fence del trabajo. El canal aplica ambos con un único control monotónico por salida.

La respuesta neutral separa Receipt (fechas de entrega/lectura del proveedor) de Review (decisión humana atribuida); una revisión no crea fechas de entrega.

D2 agrega SenderKind (Manual | Assistant) como metadata del productor de origen. Un origen desconocido permanece null; el tipo nunca se deduce de un operador ausente. La atribución preserva el operador real y no concede acceso al enlace de conversación.

La observación tipada por lote lleva sólo pares ejecución/destinatario y estado seguro. Receipt conserva fechas reales independientes de entrega/lectura; Read no implica Delivered y Review nunca las crea.

Etapa 5 C2: StructuredFormPrefillRequest recibe una propuesta auténtica del motor y sólo permite quitar FieldIds; ForSending es una decisión del servidor. El dueño devuelve etiquetas, valor canónico y texto visible ya validados, publicación/revisión y ofertas actuales, sin exponer payload privado de BusinessForms. No hay puerto de descubrimiento sin consumidor: la capacidad Read del dueño se usa dentro del motor.

H5 agrega ValidateClarification con propuesta auténtica y respuesta neutral de pregunta fija y elecciones DefinitionId/VersionId/Label. El canal no interpreta payloads del dueño ni fabrica propuestas a partir del botón; vuelve a validar la propuesta y elige sólo una referencia devuelta. El dueño prepara esa selección actual mediante SelectedForm.

StructuredRecordCaptureOrigin lleva atribución explícita, operador nullable, procedencia sellada, valores escalares propuestos e incluidos y origen/mapeos de sugerencia. No lleva mensajes, prompts ni herramientas. Request.Origin se omite cuando es null.

Las sugerencias preparadas conservan origen real, campos propuestos completos y subconjunto incluido por separado. La cultura resuelta acompaña al prellenado como dato requerido. El payload neutral de ofertas aporta referencias recientes y un conjunto independiente de definiciones rechazadas del destinatario; no concede autoridad a un UUID elegido por el modelo. El origen de C4 viene del contexto exacto del servidor y la lectura de candidatas sólo autoriza las sugerencias incluidas de ese origen.

StructuredMissingFormRequestCaptureRequest conserva SourceReceiptId opaco y RequestedAtUtc del inbound original junto al contexto/propuesta/procedencia auténticos; no transporta texto bruto, datos de la persona ni un ID de tema elegido por el cliente. El recibo devuelve IDs internos y WasAlreadyRecorded para el consumo idempotente del dueño, con ToString redactado.

H5 selection extends the authentic clarification request with server-only SelectedDefinitionId/ForSending. Every choice carries the observed DefinitionRevision; SelectedForm is null while asking and contains the owner-prepared current form, empty scalar fields, catalog offers and one effective culture after choosing. The channel never fabricates a registration proposal.

StructuredFormSuggestionPreparationRequest.ActorUserId es metadata del servidor: el actor actual de vista previa/confirmación manual es explícito; null se reserva al automático del Subject vinculado. El dueño revalida lectura histórica y devuelve Forbidden completo, sin transformar una revocación en una propuesta vacía.

En PrepareSuggestion, SuggestionSetId/SuggestionId pueden ser null únicamente cuando se aporta Proposal auténtica: el dueño deriva las referencias de su payload/lectura observada. Always conserva ambos IDs explícitos y no usa propuesta; el consumidor recibe OriginSuggestion validado y no interpreta payloads privados del dueño.

Review24: StructuredFormPreparationResponse.Culture es la cultura efectiva completa elegida por el dueño del formulario; no sustituye la cultura real de cuenta usada para autorización. StructuredRecordCaptureRequest.PresentationCulture es metadata opcional derivada de la sesión, omitida si null. No forma parte de la huella de respuestas: replay conserva el primer registro y su cultura, incluidos recibos históricos sin ese dato.

StructuredFormChannelStateRow conserva DefinitionId, LastOpenSessionExpiresAtUtc y HasPendingReplies, más DeprecationState nullable (Pending, Uncertain, Deprecated o NeedsAttention) y DeprecatedAtUtc nullable. Pending también se informa para un formulario activo y no autoriza el retiro de su versión. NeedsAttention informa fallos que requieren atención, sin publicar diagnósticos ni agregar acciones. El estado y la fecha provienen de evidencia durable local del canal por Flow; la ausencia de envíos o replies pendientes no acredita deprecación. No lleva personas, contactos, tokens ni IDs de Meta, y su lectura no consulta al proveedor. El consumidor usa la misma observación para bloquear borrado y mostrar vencimiento y deprecación, sin copiarla a snapshots.

StructuredFormVersionReference contiene sólo el par opaco DefinitionId/VersionId. Una lectura de retiro devuelve únicamente pares candidatos confirmados por su dueño; no transporta estado Meta, perfil, manifiesto ni identidad de la persona.

E2-4: Choice.Fields contiene campos históricos tipados con valor canónico string exacto y texto fuente localizado; null se omite. El adaptador del chat lo excluye expresamente, conservando su contrato.
