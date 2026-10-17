# StructuredData: contratos neutrales de formularios
Leer [formularios](../../../../docs/features/formularios.md) y los módulos opcionales del backend.
- Primer consumidor real: chequeo de publicación de BusinessForms y adaptador de canal instalado.
- DTOs tipados y cerrados: preguntas canónicas, presentación, culturas, compatibilidad y problemas. Una pregunta puede llevar el ID opaco `SourceCatalogId` para compilar un selector dinámico; resolver opciones por destinatario pertenece al envío, no al chequeo. Sin JsonElement, tipos de módulos, HTTP, EF, credenciales ni URLs del cliente.
- Perfil y huella de compilación son opacos para el consumidor; el canal conserva JSON/mapping/codec. No exponer hash/JSON técnico en HTTP ni usarlos como autoridad enviada por navegador.
- Sólo ampliar el puerto al existir consumidores reales.
2b agrega consumidores reales de Accept/GetPublication/GetPreview. Requests incluyen operación/versión opacas, actor/permisos, snapshot/perfil/huella; responses tipados de progreso/issues y preview oficial con vencimiento exacto. Inputs y URL/huellas privados redactados. StructuredRecord también contiene requests/responses tipados de captura y lectura histórica, sin campos de proveedor, JSON técnico o datos de sesión.

K7 agrega ofertas tipadas (campo, catálogo, revisión, opciones) y problemas de preparación. Capture recibe únicamente la oferta protegida del servidor, nunca listas del empleado. Read devuelve referencias históricas sin consultar el catálogo; los contratos conservan compatibilidad de las sesiones selladas sin exponer proveedor, token o JSON técnico.

F3 agrega entradas de archivo con bytes transitorios ignorados al serializar; su ToString es redactado. La lectura sólo entrega ID, nombre, tipo, tamaño y disponibilidad de miniatura, nunca claves o URLs del proveedor.

`StructuredRecordOrigin` conserva canal, operador, instante del envío y referencia opcional a conversación, con dirección enmascarada y ToString redactado. No expone tokens, payload de sesión ni teléfono completo.

`StructuredRecordActionMessage` fija ejecución, destinatario y texto ya resuelto; un documento lleva sólo metadata privada generada por el servidor, nunca SAS ni URL del proveedor. Mensajes y documentos redactan `ToString`. `StructuredRecordActionResponse` conserva evidencia independiente del transporte: un aceptado no se repite y un incierto sólo se consulta hasta tener evidencia suficiente.

StructuredRecordActionReview lleva decisión humana, actor y fecha originales; no es evidencia de entrega del proveedor. Redacta su representación y no acepta identidad ni tiempo del navegador.
Review incluye la revisión esperada original; Retry recibe el fence del trabajo. El canal aplica ambos con un único control monotónico por salida.

La respuesta neutral separa Receipt (fechas de entrega/lectura del proveedor) de Review (decisión humana atribuida); una revisión no crea fechas de entrega.

D2 agrega SenderKind (Manual | Assistant) como metadata del productor de origen. Un origen desconocido permanece null; el tipo nunca se deduce de un operador ausente. La atribución preserva el operador real y no concede acceso al enlace de conversación.

La observación tipada por lote lleva sólo pares ejecución/destinatario y estado seguro. Receipt conserva fechas reales independientes de entrega/lectura; Read no implica Delivered y Review nunca las crea.

Etapa 5 C2: StructuredFormPrefillRequest recibe una propuesta auténtica del motor y sólo permite quitar FieldIds; ForSending es una decisión del servidor. El dueño devuelve etiquetas, valor canónico y texto visible ya validados, publicación/revisión y ofertas actuales, sin exponer payload privado de BusinessForms. No hay puerto de descubrimiento sin consumidor: la capacidad Read del dueño se usa dentro del motor.

H5 agrega ValidateClarification con propuesta auténtica y respuesta neutral de pregunta fija y elecciones DefinitionId/VersionId/Label. El canal no interpreta payloads del dueño ni fabrica propuestas a partir del botón; vuelve a validar la propuesta y elige sólo una referencia devuelta antes de Find+Prepare actuales.

StructuredRecordCaptureOrigin lleva atribución explícita, operador nullable, procedencia sellada, valores escalares propuestos e incluidos y origen/mapeos de sugerencia. No lleva mensajes, prompts ni herramientas. Request.Origin se omite cuando es null.

Las sugerencias preparadas conservan origen real, campos propuestos completos y subconjunto incluido por separado. La cultura resuelta acompaña al prellenado como dato requerido. El payload neutral de ofertas aporta referencias y registros confirmados del destinatario para una lectura observada del mismo turno; no concede autoridad a un UUID elegido por el modelo.
