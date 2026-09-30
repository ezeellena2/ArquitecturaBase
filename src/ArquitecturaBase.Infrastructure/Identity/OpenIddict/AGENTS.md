# OpenIddict: servidor de autorización

Configura protocolo, cliente web, credenciales y revocación. Los endpoints passthrough de Api deciden la cuenta y los claims; el framework conserva el protocolo.

## Al modificar

- Conservá Authorization Code con PKCE y la comprobación de tokens revocados. Una modificación de renovación requiere probar reuso y cierre de sesión.
- El issuer es el origen del navegador; sincronizá configuración del front, redirects del seed y hosting.
- Desarrollo usa certificados locales; tests, credenciales efímeras; Production, certificados configurados. No guardar certificados privados ni passwords en el repo.
- Los cambios de cliente y scopes se reflejan en el seed y los tests de `/connect`; no agregar lógica del producto al registro.

## Referencias

- [Protocolo y sesiones](../../../../docs/features/identidad.md).
- [Configuración de Production](../../../../docs/guides/despliegue.md).
- [Flujos OIDC](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Auth/ConnectFlowTests.cs).
