# Common: mecanismos compartidos de Application

`Pagination` define el pedido y resultado paginado; `Validation`, el puente a FluentValidation y errores por campo; `Logging`, operaciones; `Exceptions`, fallas técnicas que cruzan puertos; `Formatting`, el único formato de números para una persona (`DisplayNumbers`).

## Al modificar

- Subí una pieza acá cuando tenga consumidores de varias áreas; no acumular reglas propias de usuarios o identidad.
- Los mecanismos comunes siguen sin conocer EF ni HTTP. El orden y paginado de EF se implementan en Infrastructure.
- Conservá los constructores permitidos de `ValidationError` y el mapeo de campos; las declaraciones de errores de negocio pertenecen a Domain.
- Un cambio común necesita verificar a sus consumidores con Application.UnitTests y las reglas de arquitectura.
- Un número que ve una persona sale de `DisplayNumbers`, con la región de su idioma (es-AR, en-US): `Number` o `Format` para un texto con argumentos, y `Percent` (`{0:percent}` en un texto) para un porcentaje, con el signo pegado como lo escribe Intl («21%»). Un `ToString("N0")`, un patrón `"#,…"`, un texto formateado con una cultura o un «%» pegado a mano fuera de la pieza los frena `NumberFormattingTests` (2026-11-12).

## Referencias

- [Validación y paginado](../../../docs/architecture/backend.md).
- [Tests de consumidores](../../../tests/ArquitecturaBase.Application.UnitTests).
- [Frontera de errores](../../../tests/ArquitecturaBase.ArchitectureTests/ErrorDeclarationTests.cs).
