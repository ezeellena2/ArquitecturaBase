# Hosting: un origen para SPA y backend

Configura headers de seguridad, proxies de confianza y el fallback del SPA. El navegador usa un origen para HTML, API y OpenIddict.

## Al modificar

- `BackendPrefixes` es una lista manual. Un prefijo nuevo se agrega junto a sus pruebas y al proxy de Vite para que un 404 técnico no sea HTML con 200.
- Preservá cookies, issuer y Host público al configurar proxies. No agregar CORS para separar el SPA del backend.
- Los headers y el fallback tienen comportamiento diferente en Development y Production; verificá archivos estáticos, rutas SPA y rutas técnicas inexistentes.
- No aceptar forwarded headers de cualquier origen sin la configuración de confianza documentada.

## Referencias

- [Actualizar los cuatro lugares](../../../docs/guides/prefijo-de-backend.md).
- [Hosting del SPA](../../../docs/architecture/backend.md).
- [Pruebas de hosting](../../../tests/ArquitecturaBase.Api.IntegrationTests/Hosting/SpaHostingTests.cs).

Los PDF privados se previsualizan en iframe con URL blob del mismo navegador; `frame-src` admite blob, pero `script-src` conserva sólo el origen propio. La API sigue sirviendo el contenido con permiso y no-store.

Los audios privados se reproducen con una URL blob creada desde el GET autenticado; `media-src` permite sólo self y blob. No amplía `script-src` ni `connect-src`, y el GET conserva permiso y no-store.
