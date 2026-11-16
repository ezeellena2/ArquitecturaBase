# RequestContext: adaptación de la petición

`CurrentUser` y `RequestInfo` implementan puertos de Application sobre `HttpContext`. Es el lugar de acceso al usuario y datos técnicos de la petición.

## Al modificar

- Application consume interfaces; no mover `HttpContext` ni claims técnicos a servicios de negocio.
- Las implementaciones son scoped y se registran en `DependencyInjection.cs`. No capturar la petición desde singletons o workers.
- Un dato de petición nuevo debe ser necesario para un caso de uso y respetar privacidad y origen de headers.
- `UserLogScope` pone el id de quien pide en un scope de log, después de la autenticación, y el manejador de errores lo repite en el log del 500 (N10). Sólo el id: nunca el correo, el número ni otro dato de la cuenta.

## Referencias

- [Adaptadores HTTP](../../../docs/architecture/backend.md).
- [Puertos de petición](../../ArquitecturaBase.Application/Interfaces/Integrations/Request).
