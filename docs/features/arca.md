# ARCA: la conexión con los web services

Regla vigente desde la etapa 1 (2026-11-05). Una instalación se conecta con ARCA con el certificado de la empresa,
prueba la conexión, trae del padrón los datos de la empresa y elige el punto de venta. Todavía no emite comprobantes ni
tiene efecto legal. Las decisiones están en el [ADR 0023](../decisions/0023-arca-como-modulo-opcional.md) (módulo y
credenciales) y el [ADR 0025](../decisions/0025-familias-de-servicios-arca.md) (familias); la operación, en
[configurar ARCA](../guides/configurar-arca.md), y la retirada, en [quitar ARCA](../guides/quitar-arca.md). Las etapas
que siguen están en la [hoja de ruta](../plans/2026-10-31-arca-formularios-estandar.md).

## Módulo y familias

- **Módulo opcional** en `Modules/Arca` de Domain, Application, Infrastructure y Api, con un `Arca<Capa>Registration`
  por capa que compone `Program.cs`. El núcleo no lo nombra.
- **Una carpeta por familia de servicios** debajo de cada carpeta de capa: `Connection` (conexión, certificado y ticket),
  `Registry` (padrón y datos de la empresa) e `Invoicing` (`FEDummy` y punto de venta). Una familia usa sólo
  `Connection`; `Connection` conoce a las demás sólo por `IArcaServiceDefinition` (sus servicios, su prueba y si su
  configuración está completa). `ArcaFamilyBoundaryTests` lo controla. Cada familia se quita sola.
- **Cada empresa elige qué servicios usa** entre los instalados. La constancia de inscripción es obligatoria, porque
  trae los datos de la empresa. Sólo los elegidos se prueban y muestran su zona.
- **Un ambiente por vez:** la conexión es de homologación o de producción. Pasar a producción es configurarla de nuevo.
- **Sólo certificados nuevos**, generados en la app. No se importa uno existente.

## Permisos y pantalla

- `arca.read` lee; cambiar algo o llamar a ARCA pide también `arca.manage` (`ArcaPermissions`, por
  `IPermissionCatalogSource`). Sin `arca.read`, «ARCA» no aparece en el menú.
- La pantalla es `/configuracion/arca`, en Administración → Configuración, sin estado en la URL. El alta va por zonas que
  se habilitan de a una y la conexión activa, por zonas con su acción en la banda. Los tableros y lo que se fijó al
  programar están en el [diseño del front](../../../ArquitecturaBaseFront/docs/design/arca-conexion.md).

## Estados

Sin conexión → esperando el certificado → sin verificar → activa ⇄ requiere atención.

- Subir el primer certificado lo prueba y deja la conexión sin verificar, o activa si todo salió bien.
- Una prueba en verde con el certificado vigente y las familias elegidas completas lleva a «activa».
- Una prueba fallida, un certificado vencido o una familia elegida sin su configuración llevan una conexión activa a
  «requiere atención»; en el alta, la dejan sin verificar.
- El servidor guarda el resultado general de la última prueba y su hora, no el de cada servicio. Elegir servicios sólo
  guarda la elección: la pantalla vuelve a probar.

## Contrato HTTP

Rutas MVC, todas con `arca.read`; las que escriben o llaman a ARCA, también con `arca.manage`. Las escrituras llevan la
revisión esperada y responden 409 con `Arca.Connection.RevisionConflict` si cambió.

| Verbo y ruta | Qué hace |
| --- | --- |
| `GET api/arca/connection` | Estado, ambiente, CUIT, servicios instalados (con su familia) y elegidos, certificados sin secretos, última prueba y revisión |
| `POST api/arca/connection/certificate-requests` | Con CUIT, ambiente, alias y servicios genera la clave y el pedido. Sin conexión la crea; con conexión pide otro certificado para ella (renovación, o un pedido nuevo en el alta) y reemplaza el pendiente |
| `GET api/arca/connection/certificate-requests/current` | Descarga el `.csr` pendiente (`application/pkcs10`, `arca-<alias>.csr`) |
| `DELETE api/arca/connection/certificate-requests/current` | Cancela la renovación (revisión en la query) |
| `PUT api/arca/connection/services` | Elige los servicios que usa la empresa |
| `PUT api/arca/connection/certificate` | Sube el `.crt` en PEM, lo valida y prueba; responde el resultado de la prueba. En una renovación, el nuevo reemplaza al activo sólo con la prueba en verde |
| `POST api/arca/connection/test` | Prueba los servicios elegidos con la prueba de cada familia |
| `DELETE api/arca/connection` | Desconecta (revisión en la query) |
| `GET api/arca/registry/company` | Razón social, condición frente al IVA y domicilio fiscal que trajo el padrón |
| `GET api/arca/invoicing/settings` | El punto de venta elegido |
| `GET api/arca/invoicing/points-of-sale` | Consulta a ARCA los puntos de venta de web service |
| `PUT api/arca/invoicing/point-of-sale` | Elige el punto de venta; la conexión se activa si la prueba salió bien y las demás familias están completas |

- **Límites por operador** (`Arca:RateLimiting`): lectura, escritura y `arca-remote`, que cubre lo que llama a ARCA
  (subir, probar, listar y elegir el punto de venta). Los cuerpos tienen 16 KiB como mucho.
- **Contratos:** el `ToString` de los pedidos enmascara la CUIT y oculta el certificado.

## Certificado y credenciales

- **El pedido:** clave RSA de 2048 bits generada en el servidor y PKCS#10 con SHA-256, con `C=AR`, `O` y `CN` con el alias
  y `serialNumber` «CUIT …». No llama a ARCA. El alias va de 3 a 30 minúsculas, dígitos o guiones, y empieza con una letra.
- **Al subir el certificado** se comprueba que corresponda a la clave generada, que tenga la CUIT de la conexión, que el
  emisor sea el del ambiente elegido (`IssuerCommonName`) y que no esté vencido. Cada caso tiene su error.
- **A lo sumo un certificado activo y uno pendiente.** Renovar genera un pedido nuevo mientras el actual sigue andando.
- **Cifrado:** la clave privada y el ticket van cifrados con el anillo propio `Arca:SecretProtection` (AES-GCM, como el
  de IA), con un AAD atado a la instalación, la clave del anillo, el propósito, la conexión y el certificado. La Api no
  arranca sin el anillo. Si la clave guardada no se descifra, es una falla del anillo, no de la persona.
- **Desconectar** borra de verdad la conexión, los certificados, los tickets y los datos de cada familia: hay secretos.

## Ticket de acceso (WSAA)

- El pedido (TRA) lleva las horas con el huso explícito y un `uniqueId` al azar, y se firma en CMS (`SignedCms`).
- El ticket se guarda en PostgreSQL, uno por certificado y servicio, y se reutiliza mientras le quede margen
  (`TicketMargin`).
- **Renovar, un host por vez:** una transacción corta toma la reserva de la fila con dueño y vencimiento
  (`TicketLease`); el pedido al WSAA va fuera de la transacción, y otra transacción guarda el ticket sólo si la reserva
  sigue siendo propia. Quien encuentra la reserva de otro espera de a medio segundo y vuelve a leer.
- **«Ya posee un TA válido»:** la fila guarda hasta cuándo no se puede pedir otro (la ventana del ambiente:
  `TicketRetryWindow`) y la prueba lo informa con ese plazo. Ningún pedido HTTP espera minutos.
- Las fallas del WSAA se traducen a un motivo estable: servicio sin autorizar, reloj desfasado, bloqueado o sin respuesta.

## La prueba

- Pide un ticket para cada servicio elegido y corre la prueba de su familia. Lo que contesta ARCA no es un error del
  pedido: es el resultado de cada servicio, con su estado y un motivo estable.
- Corre fuera de la transacción; el resultado se guarda sólo si el certificado probado sigue siendo el activo. Si cambió
  mientras corría, da `RevisionConflict`. La prueba de una renovación en rojo no guarda nada.

## Familias

### Registry: padrón y datos de la empresa

- `ws_sr_constancia_inscripcion`, `getPersona_v2` sobre la CUIT de la conexión.
- La prueba guarda razón social, condición frente al IVA y domicilio fiscal con `IArcaRegistryCompanyService`, en su
  transacción y con el lock de la conexión. Una CUIT sin nombre no guarda nada.
- La condición frente al IVA: el monotributo manda; si no, el impuesto 30 es responsable inscripto y el 32, exento. Una
  persona humana se nombra «nombre apellido».

### Invoicing: facturación electrónica

- `wsfe` (WSFEv1). La prueba responde a `FEDummy`: una parte distinta de «OK» la deja sin respuesta.
- Los puntos de venta salen de `FEParamGetPtosVenta`: quedan afuera los dados de baja y se marcan los bloqueados. Elegir
  uno bloqueado, dado de baja o que ARCA ya no devuelve da `Arca.Invoicing.PointOfSaleUnavailable`.
- La configuración de la familia está completa con el punto de venta elegido.

## Robustez y privacidad

- La prueba sólo lee y no tiene efecto legal: alcanzan la revisión, el 409 y la reserva del ticket
  ([ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md)).
- Los clientes SOAP son propios (`HttpClient` y `System.Xml.Linq`), sólo HTTPS, sin seguir redirecciones, con tope de
  tamaño de respuesta, sin reintentos automáticos (se saca la resiliencia común) y con los loggers del `HttpClient`
  apagados.
- Los logs no llevan token, firma, clave, pedido ni certificado; la CUIT va enmascarada.

## Pendiente

- **Homologación real** con un certificado de WSASS para una CUIT del usuario: confirmar los CN de los emisores de cada
  ambiente y la condición frente al IVA con CUIT reales. Se cierra cuando la conexión, los datos de la empresa y los
  puntos de venta respondan contra ARCA.
