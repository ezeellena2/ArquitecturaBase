# Users: reglas de cuentas e invitaciones

Contiene reglas puras de cuenta, invitaciones y sus errores. La entidad técnica de Identity vive en Infrastructure; acá quedan las decisiones del negocio.

## Al modificar

- Conservá las reglas del último administrador y del último medio de ingreso; probalas como invariantes.
- Una invitación y un ingreso son hechos distintos: el envío no verifica automáticamente correo ni teléfono.
- Las invitaciones expresan canal, consumo y estado de envío sin depender de proveedores. Las operaciones que leen otras cuentas se coordinan en Application.

## Referencias

- [Administración de cuentas e invitaciones](../../../docs/features/administracion.md).
- [Medios de ingreso](../../../docs/features/identidad.md).
- [Pruebas de reglas de cuentas](../../../tests/ArquitecturaBase.Domain.UnitTests/Users).
