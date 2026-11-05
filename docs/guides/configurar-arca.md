# Configurar ARCA

Cómo dejar lista una instalación para el módulo ARCA y conectarla con el certificado de la empresa. La regla del área
está en [ARCA](../features/arca.md) y las decisiones, en el [ADR 0023](../decisions/0023-arca-como-modulo-opcional.md).
Los pasos dentro de ARCA los hace alguien con la clave fiscal de la empresa: la app nunca la pide.

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

Los emisores (`IssuerCommonName`: «Computadores Test» en homologación y «Computadores» en producción) sirven para saber
de qué ambiente es un certificado. Falta confirmarlos con certificados reales en la homologación.

## Conectar la instalación

Con `arca.manage`, en Administración → Configuración → ARCA:

1. **Datos.** «Conectar con ARCA» abre el alta. Se cargan la CUIT de la empresa, el ambiente, el alias y los servicios
   que usa. El alias es el nombre con el que se crea el certificado en ARCA: de 3 a 30 minúsculas, dígitos o guiones,
   empezando con una letra. «Generar el pedido» crea la clave privada en el servidor y el pedido de certificado (`.csr`);
   no llama a ARCA.
2. **Pedido.** Se descarga el `.csr`. Es un dato público: no contiene la clave.
3. **En ARCA**, con la clave fiscal:
   - **Homologación:** en [WSASS](https://wsass-homo.afip.gob.ar/wsass/portal/main.aspx) (el servicio «WSASS -
     Autogestión Certificados Homologación»), «Nuevo certificado» con el alias y el contenido del `.csr`; el certificado
     que devuelve se guarda como `.crt`. Después, «Crear autorización a servicio» para cada servicio elegido (`wsfe`,
     `ws_sr_constancia_inscripcion`), con ese alias y la CUIT representada.
   - **Producción:** en «Administración de certificados digitales», se agrega el alias con el `.csr` y se descarga el
     certificado. En «Administrador de relaciones de clave fiscal», una relación nueva para cada servicio elegido, con el
     alias como representante.
4. **Certificado.** Se sube el `.crt`. La app comprueba que corresponda a la clave que generó, que sea de la CUIT y del
   ambiente de la conexión y que no esté vencido; si pasa, prueba sola cada servicio elegido.
5. **Punto de venta**, si se eligió Facturación: la lista trae los puntos de venta de web service de la CUIT. Si no hay,
   se crea uno de web service en ARCA («Administración de puntos de venta y domicilios») y se vuelve a buscar. Conviene
   uno que no use otro sistema. «Activar la conexión» lo guarda y activa la conexión.

Se puede salir y volver en cualquier paso: lo hecho queda guardado. Una conexión es de un solo ambiente: pasar a
producción es desconectar y empezar de nuevo, con otro certificado y otro punto de venta.

## Renovar el certificado

Treinta días antes del vencimiento, la pantalla avisa en amarillo; vencido, en rojo, y la conexión pasa a «Requiere
atención». «Renovar», en la zona «Certificado», pide el alias del certificado nuevo y genera otro pedido mientras el actual
sigue andando. En ARCA se repiten los pasos del alta con ese pedido, incluida la autorización de los servicios si el alias
es otro. Al subir el certificado nuevo, la app lo prueba y, si sale bien, reemplaza al anterior. «Cancelar la renovación»
descarta el pedido pendiente.

## Desconectar

«Desconectar» borra la clave privada, los certificados, los tickets de acceso y los datos de cada familia. Para volver a
conectar hay que generar un certificado nuevo. El certificado viejo sigue existiendo en ARCA hasta que se lo revoque allá.

## Lo que informa la prueba

| Mensaje | Qué hacer |
| --- | --- |
| «Falta autorizar el servicio para el alias «…»» | Crear la autorización (homologación) o la relación (producción) de ese servicio para el alias, y probar de nuevo |
| «La hora del servidor no coincide con la de ARCA» | Sincronizar el reloj del servidor (NTP) |
| «ARCA no acepta otro pedido hasta las …» | ARCA ya dio un ticket para ese certificado y servicio que la app no tiene: otro sistema con el mismo certificado, o un ticket perdido. Esperar hasta esa hora (`TicketRetryWindow`: 2 minutos en producción, 10 en homologación) |
| «ARCA no respondió» o «ARCA rechazó la consulta» | Probar de nuevo en unos minutos |
| El certificado no corresponde, es de otra CUIT, del otro ambiente o venció | Generar un pedido nuevo en la app y repetir los pasos en ARCA con ese pedido |
| «ARCA no devolvió puntos de venta de web service para esta CUIT.» | Crear un punto de venta de web service en ARCA y «Volver a buscar» |
