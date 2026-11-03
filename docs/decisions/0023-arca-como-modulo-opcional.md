# ADR 0023: ARCA como módulo opcional y sus credenciales

## Estado

**Aceptada**, 2026-11-03. Recoge lo que el usuario aprobó en la [hoja de ruta de ARCA](../plans/2026-10-31-arca-formularios-estandar.md) y
en el [diseño de la etapa 1](../plans/2026-11-03-arca-etapa-1-conexion.md), y rige desde esa etapa. Sigue el modelo de módulos de los
ADR [0007](0007-whatsapp-como-modulo-opcional.md), [0010](0010-catalogo-de-permisos-extensible.md) y
[0013](0013-composicion-de-modulos-en-tests.md), y extiende el [ADR 0014](0014-credenciales-de-ia-fuera-de-la-base.md) a las credenciales
de ARCA.

## Contexto

La plataforma se va a conectar con los web services de ARCA para emitir comprobantes con efecto legal: primero la factura y
después liquidaciones y remitos. Un proyecto derivado puede no necesitarlo.

El acceso pasa por el WSAA:
- un certificado X.509 y su clave privada firman en CMS un pedido de acceso;
- ARCA devuelve un ticket (token y firma) que dura unas 12 horas y sirve para un solo servicio;
- ARCA rechaza otro pedido para el mismo servicio durante 2 minutos en producción y 10 en homologación;
- un mismo certificado no puede usarse desde dos equipos a la vez.

Con la clave privada o un ticket vigente se puede emitir en nombre de la empresa. El riesgo es mayor que el de la clave de
IA que protege el ADR 0014, porque además del costo hay efecto legal.

## Decisión

1. **Módulo opcional `Arca`**, en `Modules/Arca` de cada capa:
   - ni el núcleo ni los otros módulos lo nombran;
   - suma sus permisos con `IPermissionCatalogSource`, y el menú del front lo muestra por permiso, como a la IA;
   - sus migraciones van en la cadena única;
   - tiene su guía para quitarlo.
2. **El motor de formularios lo usa por un puerto neutral** en `Application/Interfaces/Channels/`, con versión apagada en
   `Application/Channels/`. El puerto llega con la emisión (etapa 3). No hay un módulo `Integrations` genérico hasta que
   exista un segundo proveedor.
3. **La clave privada se genera en el servidor** (RSA 2048), con el pedido de certificado que descarga la persona. Nunca
   se importa ni se descarga. Se cifra con un anillo externo propio del módulo (`Arca:SecretProtection`), del mismo diseño
   que el del ADR 0014, porque los módulos no comparten código. Lleva un propósito propio y un AAD que la ata a la
   instalación, la conexión y el certificado.
4. **El ticket de acceso se guarda en PostgreSQL**, cifrado con ese anillo y otro propósito:
   - lo comparten todos los hosts;
   - lo renueva un solo host por vez, con una reserva corta en la fila del ticket (dueño y vencimiento) que se toma y se
     confirma en transacciones cortas. El pedido a ARCA va fuera de la transacción, como pide la regla de locks.

   No va en Redis: es una credencial, no un dato cacheado del [ADR 0009](0009-redis-como-unico-cache-de-datos.md). Si se
   perdiera, ARCA rechazaría otro pedido durante unos minutos.
5. **Los clientes SOAP son propios:**
   - `HttpClient` con XML y firma con `SignedCms`;
   - sin bibliotecas de terceros ni intermediarios que reciban la clave;
   - solo HTTPS y sin redirecciones;
   - las direcciones de cada ambiente son configuración.
6. **Una conexión por instalación, con un solo ambiente.** El certificado de la CUIT de la plataforma de los modos de
   delegación (etapas 5 y 6) sigue estas mismas reglas.
7. **Lo que nunca sale del módulo en claro:** la clave, el token y la firma, ni en logs, ni en respuestas, ni en
   comprobantes. El pedido y el certificado son públicos, pero tampoco van a los logs; el pedido se descarga solo mientras
   está pendiente. La CUIT va enmascarada en los logs.

## Consecuencias

- **Respaldo:** la base y el anillo del módulo se respaldan y restauran juntos, como en el ADR 0014. Si se pierde una
  clave del anillo, no hay recuperación: se genera un certificado nuevo y se repite el alta en ARCA.
- **Rotación:** rotar el anillo no recifra nada; renovar el certificado lo cifra con la clave activa.
- **Pruebas:** un ARCA simulado en los tests, con varios hosts para el ticket compartido, según el
  [ADR 0018](0018-robustez-proporcional-al-efecto.md). La homologación real la hace el usuario con su clave fiscal.
- **Robot:** el robot de la etapa 6 y la clave fiscal que maneja solo en memoria necesitan su propio ADR antes de
  programarse.
- **Multitenant:** la conexión pasa a ser por organización. El anillo y la CUIT de la plataforma siguen siendo por
  instalación.

## Alternativas descartadas

- **Afip SDK u otro intermediario en la nube:** recibe la clave privada y el certificado del cliente en sus servidores.
- **Bibliotecas .NET de terceros, como ElRoso.ARCA:** no documentan qué hacer con una respuesta que no llega ni cómo
  consultar antes de reintentar, y no dicen estar listas para producción. Quedan como referencia de lectura.
- **WCF:** suma una dependencia para unas pocas operaciones SOAP.
- **Importar un certificado existente:** la clave viaja, y si otro sistema sigue usando el certificado, los tickets
  chocan.
- **Data Protection con sus claves en la base:** el backup de la base alcanzaría para descifrar, como ya descartó el ADR
  0014.
- **El ticket en Redis:** se puede perder, y ARCA no da otro enseguida.
- **Un módulo `Integrations` genérico desde ya:** no hay un segundo proveedor que lo justifique.
