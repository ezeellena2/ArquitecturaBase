# Workflows: build y despliegue

El workflow obtiene ambos repositorios, verifica React y .NET, genera una imagen conjunta y el bundle EF, y despliega a Azure dev solo con DEV_DEPLOY_ENABLED=true. Es configuración de operación del backend; el workflow del front solo solicita esta ejecución.

## Al modificar

- Las pruebas corren en shards paralelos: Playwright en Windows con referencias del mismo sistema, vitest y .NET por módulo; cada artefacto lleva su shard. El build exige el mismo commit del front en todos, además de lint.

- Conservá el orden migración antes de revisión nueva: una migración fallida no debe publicar código contra un esquema incompatible.
- Incluí todo dist en wwwroot, verificá la integridad del publicado y registrá ambos commits. Un paquete superado por main se omite; no uses latest para elegir la revisión.
- Antes de subir la imagen, el deploy exige los probes Startup y Liveness en `/alive` y Readiness en `/health`, sin configurarlos.
- Conservá la exclusión entre despliegues y la espera de la revisión concreta antes de probar salud, SPA y discovery. La pipeline no resetea la base ni crea infraestructura.
- Los secretos salen de GitHub/Azure; no escribir conexiones o credenciales reales en YAML ni logs. La identidad de Azure usa OIDC. Las migraciones leen el secreto `db-connection` de la Container App (el mismo de la Api) y lo enmascaran; GitHub no guarda la conexión.
- Los valores de conexión de diseño del bundle permiten construir el contexto; no sustituyen las conexiones de ejecución de PostgreSQL y Redis.
- Si se abre el firewall del runner, el cierre corre también al fallar. Revisá la documentación de despliegue junto a cambios del workflow.

## Referencias

- [Flujo de despliegue](../../docs/guides/despliegue.md).
- [Recursos de Azure](../../docs/deploy/azure-setup.md).
- [Ambiente dev y configuración de GitHub](../../docs/deploy/azure-dev.md).
- [Bundle de migraciones](../../docs/guides/migracion.md).
