# Common: mecanismos compartidos de Application

`Pagination` define el pedido y resultado paginado; `Validation`, el puente a FluentValidation y errores por campo; `Logging`, operaciones; `Exceptions`, fallas técnicas que cruzan puertos.

## Al modificar

- Subí una pieza acá cuando tenga consumidores de varias áreas; no acumular reglas propias de usuarios o identidad.
- Los mecanismos comunes siguen sin conocer EF ni HTTP. El orden y paginado de EF se implementan en Infrastructure.
- Conservá los constructores permitidos de `ValidationError` y el mapeo de campos; las declaraciones de errores de negocio pertenecen a Domain.
- Un cambio común necesita verificar a sus consumidores con Application.UnitTests y las reglas de arquitectura.

## Referencias

- [Validación y paginado](../../../docs/architecture/backend.md).
- [Tests de consumidores](../../../tests/ArquitecturaBase.Application.UnitTests).
- [Frontera de errores](../../../tests/ArquitecturaBase.ArchitectureTests/ErrorDeclarationTests.cs).
