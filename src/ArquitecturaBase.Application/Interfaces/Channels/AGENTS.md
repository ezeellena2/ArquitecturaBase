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
