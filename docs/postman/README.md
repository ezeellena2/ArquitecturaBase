# Probar la identidad con Postman

La colección `ArquitecturaBase.postman_collection.json` recorre el ingreso completo contra la Api local, como lo va a hacer el SPA en la Fase 3.

## Antes de empezar

1. Levantá todo con `aspire run` desde la raíz del repo. La Api queda en `https://localhost:7180`.
2. Importá la colección en Postman (Import → archivo).
3. Postman tiene que aceptar el certificado de desarrollo. Hay dos opciones:
   - confiar en él con `dotnet dev-certs https --trust`;
   - desactivar "SSL certificate verification" en Settings → General.
4. En las variables de la colección, cambiá `email` por tu email. Con el de `Seed:AdminEmail` entrás como Admin y podés ver `/api/users`.

## Ingreso con código

| Paso | Request | Qué pasa |
|---|---|---|
| 1 | 1. Account → Pedir código | 202. En desarrollo, el email se guarda como `.eml` en `src/ArquitecturaBase.Api/.emails/`. El código es la primera palabra del asunto. |
| 2 | Copiá el código en la variable `code` | |
| 3 | 1. Account → Verificar código | 200. Postman guarda la cookie de sesión del servidor. |
| 4 | 2. Connect → Authorize | Genera el PKCE y guarda el `authorizationCode` de la redirección. |
| 5 | 2. Connect → Token (canjear el code) | Guarda el access token, el refresh token y el id token. |
| 6 | 3. Api → Mi perfil / Usuarios | Con el access token. `/api/users` responde 403 si tu usuario no tiene `users.read`. |
| 7 | 2. Connect → Token (refresh) | Rota el refresh token. Si reusás el anterior, se revoca toda la cadena. |
| 8 | 2. Connect → Logout | Cierra la sesión y revoca los tokens. |

Límites que conviene conocer:
- Entre dos pedidos de código hay que esperar 60 segundos.
- Se aceptan 5 pedidos por email cada 15 minutos, y 20 por IP.
- Cada código admite 5 intentos. Con 10 fallos seguidos, la cuenta se bloquea 15 minutos. El bloqueo se configura en `Authentication:LoginCode` (`LockoutMaxFailedAttempts` y `LockoutMinutes`).

## Ingreso con Google

Requiere el secreto del cliente en user-secrets (ver el README principal).

1. Ejecutá "4. Google → Armar la URL de ingreso con Google". La consola de Postman muestra la URL.
2. Abrila en el navegador y entrá con Google. Al final, el navegador queda en `https://oauth.pstmn.io/v1/callback?code=...`.
3. Copiá el valor de `code` en la variable `authorizationCode`.
4. Ejecutá "2. Connect → Token (canjear el code)". Usa el `codeVerifier` que generó el paso 1.

## Emails reales con Gmail

Por defecto, en desarrollo los emails se guardan como archivos. Para enviarlos con Gmail, ver "Emails" en el README principal.
