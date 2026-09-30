# Domain: modelo y reglas puras

Define entidades, valores y errores de negocio. `Common` reúne identidad y auditoría; `Results`, los resultados; `ValueObjects`, correo y teléfono; las otras carpetas agrupan áreas funcionales.

## Al modificar

- Esta capa usa solo la BCL. Una regla que necesita base, sesión, reloj o proveedor externo se coordina en Application.
- Las invariantes pertenecen a entidades o valores. Los errores se declaran en `<Entidad>Errors`; sus traducciones se agregan en Application.
- Las entidades propias heredan de `Entity`; auditoría y borrado lógico se completan al guardar mediante interceptores. No agregar eventos de dominio.
- Probá primero las reglas nuevas en Domain.UnitTests, con fechas explícitas y casos de borde.

## Referencias

- [Arquitectura y dependencias](../../docs/architecture/backend.md).
- [Cómo agregar una entidad y un área](../../docs/guides/agregar-un-area.md).
- [Tests del dominio](../../tests/ArquitecturaBase.Domain.UnitTests).
