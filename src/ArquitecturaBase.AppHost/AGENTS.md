# AppHost: entorno local con Aspire

Orquesta PostgreSQL, Redis, Api y el front del repo vecino. Las referencias inyectan conexiones y endpoints; `WaitFor` ordena el arranque.

## Al modificar

- Un recurso técnico nuevo se referencia desde la Api y espera su readiness cuando es requisito de arranque.
- Conservá el puerto, volumen y vida persistente de PostgreSQL. Redis se levanta como `cache`, con la misma conexión que registra Infrastructure.
- El front mantiene HTTPS y el origen del navegador. Un cambio de puerto u origen requiere revisar issuer, redirect URIs, proxy y cliente OpenIddict.
- El túnel opcional se activa por configuración local; no incrustar credenciales. Si levantás la app para probar, terminá con `aspire stop`.

## Referencias

- [Arranque y conexiones locales](../../README.md).
- [Hosting y origen único](../../docs/architecture/backend.md).
- [Prueba local del túnel](../../docs/guides/whatsapp-en-local.md).
