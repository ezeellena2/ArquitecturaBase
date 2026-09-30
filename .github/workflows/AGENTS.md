# Workflows: build y despliegue

El workflow compila y prueba, genera el bundle EF y la imagen, y despliega a Azure. Es configuración de operación del backend.

## Al modificar

- Conservá el orden migración antes de revisión nueva: una migración fallida no debe publicar código contra un esquema incompatible.
- Los secretos salen de GitHub/Azure; no escribir conexiones o credenciales reales en YAML ni logs. La identidad de Azure usa OIDC.
- Los valores de conexión de diseño del bundle permiten construir el contexto; no sustituyen las conexiones de ejecución de PostgreSQL y Redis.
- Si se abre el firewall del runner, el cierre corre también al fallar. Revisá la documentación de despliegue junto a cambios del workflow.

## Referencias

- [Flujo de despliegue](../../docs/guides/despliegue.md).
- [Recursos de Azure](../../docs/deploy/azure-setup.md).
- [Bundle de migraciones](../../docs/guides/migracion.md).
