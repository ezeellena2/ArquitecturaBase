# Configurar ARCA

Cómo dejar lista una instalación para el módulo ARCA ([ADR 0023](../decisions/0023-arca-como-modulo-opcional.md)). La pantalla
de la conexión, el certificado y la renovación se agregan a esta guía cuando se cierre la
[etapa 1](../plans/2026-11-03-arca-etapa-1-conexion.md).

## El anillo de claves

El módulo cifra la clave privada del certificado y el ticket de acceso con un anillo externo propio, del mismo diseño que
el de IA. La Api no arranca sin él, aunque todavía no haya ningún certificado.

| Clave | Valor |
| --- | --- |
| `Arca:SecretProtection:InstallationId` | Un ID propio, no vacío, de hasta 100 caracteres. Se conserva en una restauración |
| `Arca:SecretProtection:ActiveKeyId` | El identificador de una clave del anillo |
| `Arca:SecretProtection:Keys:<keyId>` | 32 bytes aleatorios en base64; el keyId lleva letras, dígitos o guiones, de 1 a 32 caracteres |

En **una instalación local nueva**, desde la raíz del backend y con PowerShell 7:

```powershell
$arcaBytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
dotnet user-secrets set "Arca:SecretProtection:InstallationId" ([Guid]::NewGuid().ToString()) --project src/ArquitecturaBase.Api
dotnet user-secrets set "Arca:SecretProtection:ActiveKeyId" "local-v1" --project src/ArquitecturaBase.Api
dotnet user-secrets set "Arca:SecretProtection:Keys:local-v1" ([Convert]::ToBase64String($arcaBytes)) --project src/ArquitecturaBase.Api
[Security.Cryptography.CryptographicOperations]::ZeroMemory($arcaBytes)
Remove-Variable arcaBytes
```

- **Con datos:** no hay que correr esto sobre una instalación que ya tiene una conexión. Si cambian el ID o la clave, la
  clave privada y los tickets guardados quedan ilegibles, y hay que generar un certificado nuevo y repetir el alta en ARCA.
- **Fuera de Development:** el anillo va en variables protegidas (`Arca__SecretProtection__InstallationId`,
  `Arca__SecretProtection__ActiveKeyId`, `Arca__SecretProtection__Keys__<keyId>`) o en un gestor de secretos. Nunca va en
  appsettings ni en Git.
- **Respaldo:** se respalda junto con la base, aparte de ella. Rotar agrega una clave nueva y cambia `ActiveKeyId`; las
  claves anteriores se conservan mientras haya datos o backups cifrados con ellas.

## Direcciones y plazos

La sección `Arca` de `appsettings.json` trae las direcciones de cada ambiente, el emisor de sus certificados y la ventana
en la que ARCA no da otro ticket. La Api las valida al arrancar: solo HTTPS, con emisor y ventana. Cada servicio se nombra
por su código del WSAA (`wsfe`, `ws_sr_constancia_inscripcion`), y cada familia suma los suyos.

`Arca:RateLimiting` fija los pedidos por minuto de cada operador: `ReadPermitLimit` (120), `WritePermitLimit` (20) y
`RemotePermitLimit` (6), que cubre lo que llama a ARCA: subir el certificado, probar y consultar los puntos de venta.
