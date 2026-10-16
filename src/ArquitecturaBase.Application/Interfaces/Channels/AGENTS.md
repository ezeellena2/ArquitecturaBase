# Channels: puntos de extensión del núcleo

Define capacidades que aportan canales opcionales: ingreso por teléfono, entrega y estado de invitaciones, y participantes de un vínculo de número.

IClientCapabilitySource aporta identificadores instalados neutrales; Settings los compone fuera del caché y falla por duplicados. ISystemQuerySource aporta definiciones versionadas/consultas tipadas. Su ejecución aislada usa el puerto técnico ISystemQueryExecutor en Interfaces/Integrations/SystemQueries. No hay SQL, URLs, identidad ni permisos elegidos por el modelo. Sin módulos el núcleo conserva usuarios, roles y ayuda curada.

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
