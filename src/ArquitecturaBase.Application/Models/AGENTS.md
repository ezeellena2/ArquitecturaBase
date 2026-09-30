# Models: datos de los casos de uso

Contiene pedidos, respuestas y filas de cada área. `Auth` e `Identity` describen acceso y cuentas; `Users`, `Roles` y `Settings`, administración; `Emails`, mensajes para entrega.

## Al modificar

- `Request` es el pedido funcional; el contrato HTTP se adapta en Api. No agregar binding HTTP ni dependencias técnicas.
- `Response` es una salida; `Row`, una proyección compartida entre lector y servicio. Conservá el criterio canónico de sufijos.
- Los listados declaran campos de orden permitidos y usan los modelos comunes de paginado. Un campo ordenable debe estar disponible en la respuesta.
- No trasladar entidades seguidas ni datos sensibles innecesarios. Leé la guía del área antes de cambiar campos del contrato.

## Referencias

- [Modelos: Response y Row](../../../docs/architecture/backend.md).
- [Listados y conteos](../../../docs/features/administracion.md).
- [Modelos de referencia](Roles).
