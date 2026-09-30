# Services: coordinación de negocio

Cada subcarpeta agrupa un área. Los servicios públicos implementan interfaces de casos de uso y delegan reglas parciales a helpers con una responsabilidad nombrada.

## Al modificar

- Leé las instrucciones del área y su documento funcional. Para un área nueva usá Roles y la receta versionada como referencia.
- El servicio público que escribe abre una sola transacción. Validación de forma va antes; locks y lecturas dependientes de escritura, dentro; invalidación de caché y efectos definidos como posteriores, después del commit.
- Los helpers `Policy`, `Guard`, `Issuer`, `Verifier`, `Linker`, `Revoker` y `Recorder` no son casos de uso ni guardan. No introducir handlers ni un pipeline de CQRS.
- Mapeá a mano y consumí puertos específicos; TDD para decisiones y coordinación.

## Referencias

- [Límite transaccional y sufijos de helpers](../../../docs/architecture/backend.md).
- [Cómo agregar un servicio](../../../docs/guides/agregar-un-area.md).
- [Servicio de referencia](Roles/RoleService.cs).
