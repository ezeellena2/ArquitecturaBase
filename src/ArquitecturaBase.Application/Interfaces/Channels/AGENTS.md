# Channels: puntos de extensión del núcleo

Define capacidades que aportan canales opcionales: ingreso por teléfono, entrega y estado de invitaciones, y participantes de un vínculo de número.

IClientCapabilitySource aporta identificadores instalados neutrales; Settings los compone fuera del caché y falla por duplicados. IAssistantCapabilitySource aporta capacidades Read/Proposal con ID y versión exactos, requisitos separados para Actor/Subject, presupuestos y codec tipado del dueño. Su ejecución aislada usa el puerto técnico IAssistantCapabilityExecutor en Interfaces/Integrations/Assistant. Las fuentes no guardan, no transportan ni abren UoW. No hay SQL, URLs, identidad ni permisos elegidos por el modelo. Sin módulos el núcleo conserva usuarios, roles y ayuda curada.

## Al modificar

- `IPermissionCatalogSource` aporta códigos y textos de un módulo mediante `TryAddEnumerable`. No requiere versión apagada: sin fuentes el catálogo mantiene los permisos del núcleo.

- El núcleo consume estos puertos sin nombrar el módulo. Los adaptadores opcionales se registran dentro del módulo.
- Distinguí un puerto reemplazable (`TryAdd`), uno por canal (`TryAddEnumerable`) y uno que avisa a todos los participantes; no inventar un adaptador apagado por simetría.
- Los participantes no abren límites ni guardan. Respetan el lock y la transacción del caso de uso que los invoca.
- Probá el caso con el módulo prendido, apagado y quitado cuando el puerto debe seguir funcionando.

## Referencias

- [Módulos opcionales](../../../../docs/architecture/backend.md).
- [Vínculos e invitaciones por canales](../../../../docs/features/administracion.md).
- [Verificación al quitar un módulo](../../../../docs/guides/quitar-whatsapp.md).

IStructuredFormChannel es el puerto neutral de chequeo de formularios estructurados. Primer consumidor: consulta BusinessForms; primer adaptador: canal opcional de teléfono. TryAddEnumerable, sin versión apagada ficticia; ausencia explícita y duplicados como error de configuración. Consulta local sin UoW, locks ni proveedor. Modelos en Models/StructuredData; JSON/mapping/codec pertenecen al adaptador. No agregar métodos futuros sin consumidor.

IStructuredFormChannel tiene consumidores 2b de aceptación durable/status/preview; sólo Check es puro. El coordinador técnico llama fuera de transacciones de otro módulo. No importa tipos de módulo, EF/HTTP ni un canal opcional concreto.

IStructuredFormSource aporta selección neutral al servicio WhatsApp: destinatario/idioma resueltos por servidor, modelos StructuredData. Core TryAddSingleton apagado conserva paginado y cancelación; BusinessForms Replace scoped verifica permiso del empleado y consulta su lector SQL de versión activa sellada. Sin referencias cruzadas entre módulos.

IStructuredRecordSink y IStructuredRecordSource son los puertos neutrales de captura/lectura de registros por un canal; el núcleo aporta fallbacks apagados y BusinessForms los reemplaza sólo cuando está instalado. No aceptar tipos de proveedor ni payloads JSON brutos.

`IStructuredRecordOriginSource` aporta origen histórico por lote para la planilla y el detalle. Se consume como colección opcional; sin adaptadores, el origen queda sin información. Sólo DTO neutral, actor y recibos opacos: sin consultas del módulo consumidor a tablas de otro módulo.

`IStructuredRecordActionChannel` aporta entrega durable opcional de acciones sobre registros ya autorizados. El motor fija destinatarios, idioma y contenido; el canal deduplica por ejecución y destinatario, conserva el contacto aceptado y publica evidencia tipada. Se consume como colección opcional, sin fallback apagado; los identificadores de canal duplicados son error de composición. Consultar un resultado incierto no autoriza reenviarlo.

`IPrivateFileRetentionSource` aporta referencias durables privadas por lotes a la limpieza del núcleo. Colección opcional sin adaptador apagado; no abre transacciones ni escribe. La ausencia de fuentes conserva la limpieza normal; una falla se propaga antes de borrar el lote. El núcleo no interpreta prefijos de módulos.

La revisión de entrega usa evidencia humana tipada y atribuida del consumidor. El canal sólo aplica la decisión sobre una salida incierta; no reenvía ni genera un recibo del proveedor. La llamada queda fuera de la UoW del módulo dueño del registro.

ObserveAsync del canal de acciones consulta sólo evidencia local por lote de pares ejecución/destinatario. No consulta al proveedor, no escribe ni reserva trabajo; ausencia de fila no prueba un fallo y no habilita retry.

IAssistantOperations es el puerto productivo neutral del motor opcional: disponibilidad local, aceptación durable y recuperación por RequestId con contexto de servidor. El núcleo registra fallback notInstalled; AI reemplaza el adaptador. Submit/Get/Cancel no se invocan dentro de una UoW ajena. Cancel comprueba el contexto sellado y permite frenar una operación propia aunque haya perdido permisos; no revela contenido ni afecta otro canal. No elegir Actor/Subject ni permisos desde HTTP/modelo; Get compara el alcance sellado y revalida antes de descifrar.

H5: IStructuredFormSource valida la aclaración real del asistente por propuesta auténtica y destinatario; su respuesta contiene pregunta fija y referencias con etiquetas del dueño. El adaptador apagado rechaza la operación, sin catálogo ni intérprete de payloads opcionales en el núcleo.

IStructuredCatalogProposalSink recibe una propuesta tipada auténtica y su procedencia del motor. El dueño la valida y guarda dentro de su único caso de uso; el consumidor llama fuera de una UoW propia. El núcleo aporta una respuesta no disponible cuando el módulo dueño no está instalado.

IStructuredMissingFormRequestSink recibe el mensaje opaco estable, fecha original, contexto y propuesta auténtica del motor. BusinessForms conserva su única UoW; el canal llama fuera de la propia y sólo después puede confirmar su aviso fijo. El fallback del núcleo devuelve no disponible; no se interpreta un payload de módulo en el canal.

IStructuredFormChannelStateSource lee evidencia local por lote de definiciones: último vencimiento vigente y replies durables pendientes. No llama al proveedor ni escribe. El núcleo aporta un fallback vacío, reemplazable por el módulo del canal; una falla de lectura se propaga y nunca se interpreta como ausencia de envíos. BusinessForms lo consume para metadata y para impedir el borrado bajo el lock de definiciones.
