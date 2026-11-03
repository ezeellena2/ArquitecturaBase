# ARCA · Etapa 1: conexión

Diseño del 2026-11-03, con las decisiones del usuario del 2026-11-01 al 2026-11-03. Está sin programar. Es la etapa 1 de la
[hoja de ruta de ARCA](2026-10-31-arca-formularios-estandar.md): conectar una instalación con ARCA usando el certificado
de la empresa, probar la conexión, traer los datos de la empresa desde el padrón y elegir el punto de venta. No emite
comprobantes ni tiene efecto legal. El [ADR 0023](../decisions/0023-arca-como-modulo-opcional.md) fija el módulo y sus
credenciales, y el [plan de implementación](2026-11-03-arca-etapa-1-implementacion.md) lo parte en nueve tareas.

**Tableros:** AC1-A, AC2-A y AC3 en el lienzo provisorio
[Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), en las filas «ARCA · conexión». Las
descartadas (AC1-B y AC2-B) quedan al lado. El lienzo [Sistema visual](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ)
sigue sin abrirse desde la cuenta de estas sesiones: los tableros elegidos se pasan allí antes de programar.

## Decisiones (2026-11-01 al 2026-11-03)

1. **Alcance:** conexión, certificado, prueba, datos de la empresa desde el padrón y punto de venta. Las tablas de ARCA como
   catálogos y el autocompletar de la CUIT del cliente pasan a la etapa 3, cuando ya existan las carpetas de formularios y
   catálogos (etapa 2).
2. **Un ambiente por vez.** La conexión es de homologación o de producción. Pasar a producción es configurarla de nuevo,
   con otro certificado y otro punto de venta.
3. **Solo certificados nuevos, generados en la app.** No se importa un certificado existente: la clave privada no viaja, y
   compartir el certificado con otro sistema traba los tickets de acceso.
4. **El alta en la misma página, por zonas (AC1-A).** No es un asistente en un diálogo (AC1-B).
5. **La conexión activa, por zonas (AC2-A).** No es una ficha con historial (AC2-B).
6. **Los modos de delegación no se muestran** hasta las etapas 5 y 6, aunque el modelo de datos ya los admite.

**Resueltas por defecto**, sin objeción del usuario:

- La pantalla va en Administración → Configuración → ARCA (`/configuracion/arca`), junto a la de IA.
- El ticket de acceso se guarda en PostgreSQL, no en Redis: es una credencial, no un dato cacheado.
- Los clientes SOAP se escriben a mano con `HttpClient` y `System.Xml.Linq`, sin WCF ni bibliotecas de terceros.
- El padrón se consulta con `ws_sr_constancia_inscripcion` (`getPersona_v2`), que reemplaza al alcance 5.
- El aviso de vencimiento del certificado aparece 30 días antes.

## Recorrido

### El alta (AC1-A)

La pantalla ARCA muestra cinco zonas. La que está en curso está abierta, las hechas quedan plegadas con su resumen y las
siguientes, apagadas. Se puede salir y volver, porque cada paso queda guardado.

1. **Datos:** CUIT con el dígito verificador validado, ambiente y alias. El alias es el nombre con el que se crea el
   certificado en ARCA, y su ayuda va en la (i). «Generar el pedido» crea la clave privada (RSA 2048) en el servidor, la
   guarda cifrada y arma el CSR con `serialNumber=CUIT …`. No llama a ARCA.
2. **Pedido de certificado:** se descarga el `.csr`. Plegado, ofrece «Descargar de nuevo».
3. **Certificado:** una lista corta de lo que se hace en ARCA, con «Abrir ARCA»: crear el alias con el pedido, descargar el
   certificado y autorizar «Facturación electrónica» y «Constancia de inscripción» para el alias. En homologación, esos
   pasos se hacen en WSASS. Después se sube el `.crt`. La app comprueba que corresponda a la clave que generó, que tenga la
   CUIT de la conexión, que sea del ambiente elegido y que no esté vencido. Si pasa, prueba sola.
4. **Prueba:** pide un ticket de acceso para cada servicio. Facturación responde a `FEDummy` y el padrón trae los datos de
   la empresa: razón social, condición frente al IVA y domicilio fiscal. Cada servicio muestra su resultado, y si falla,
   cuál y por qué.
5. **Punto de venta:** los puntos de web service que devuelve `FEParamGetPtosVenta`. Los bloqueados se ven apagados y los
   dados de baja no aparecen. La (i) pide uno que no use otro sistema. «Activar la conexión» se habilita al elegir uno con
   la última prueba en verde.

### La conexión activa (AC2-A)

- **Arriba:** el estado y el ambiente junto al título, y «Desconectar» en rojo, con confirmación.
- **Empresa:** los datos del padrón, que se actualizan con cada prueba.
- **Servicios:** cada uno dice si responde y hasta cuándo vale su ticket. La banda tiene «Probar» y la hora de la última
  prueba.
- **Certificado:** alias y vencimiento, con «Renovar». Renovar genera un pedido nuevo mientras el certificado actual sigue
  andando; cuando el nuevo pasa la prueba, reemplaza al anterior. La renovación se puede cancelar.
- **Punto de venta:** número y descripción, con «Cambiar».

### Estados (AC3)

| Estado | Qué se ve |
| --- | --- |
| Sin conexión | «Conectar con ARCA», que abre el paso 1. Sin `arca.manage`, el mismo vacío sin acción |
| Cargando | Las zonas con su esqueleto |
| No se pudo leer | El error con «Reintentar», sin datos viejos |
| Probando | El botón ocupado, como en el resto del front |
| Un servicio sin autorizar | La prueba dice cuál falla. La conexión pasa a «Requiere atención» hasta que una prueba salga bien |
| Certificado que no corresponde | Otra clave, otra CUIT, otro ambiente o vencido, cada uno con su mensaje, junto a la carga |
| Por vencer | Aviso amarillo con «Renovar». La conexión sigue activa |
| Vencido | Aviso rojo, «Requiere atención» y sin poder emitir |
| Sin puntos de venta de web service | Lo dice, con «Volver a buscar» |
| Otro cambió la conexión | Un 409 al guardar trae la zona «Revisar», con «Leer lo actual» |
| Desconectar | La confirmación dice que se borran la clave y los tickets |
| Sin permiso | Con `arca.read` y sin `arca.manage`, «Solo lectura» y sin acciones. Sin `arca.read`, ARCA no aparece en el menú |

Nada vive en la URL: es una sola pantalla.

**El ciclo de estados:** sin conexión → esperando el certificado → sin verificar → activa ⇄ requiere atención. Una prueba
fallida o un certificado vencido llevan a «requiere atención», y una prueba en verde con el certificado vigente vuelve a
«activa».

## Backend

### Módulo y permisos

- **Ubicación:** `Modules/Arca` en Domain, Application, Infrastructure y Api, con un `Arca<Capa>Registration` por capa que
  compone `Program.cs`. El núcleo no lo nombra ([módulos opcionales](../architecture/backend.md#módulos-opcionales)).
- **Permisos:** `ArcaPermissions` aporta `arca.read` y `arca.manage` con `IPermissionCatalogSource`, según
  [permiso nuevo](../guides/permiso-nuevo.md). El de emitir llega con la etapa 3.
- **Menú:** «ARCA» aparece con `arca.read`, como «Inteligencia artificial» con `ai.read`. Ese permiso existe solo si el
  módulo está instalado, así que no hace falta publicar una capacidad.
- **ADR:** el [ADR 0023](../decisions/0023-arca-como-modulo-opcional.md), aceptado el 2026-11-03, fija el módulo, el
  puerto y las credenciales. Extiende el [ADR 0014](../decisions/0014-credenciales-de-ia-fuera-de-la-base.md) a la clave
  privada del certificado y al ticket de acceso, cada uno con su propósito en el AAD.

### Datos

Hay una sola conexión por instalación, con un índice único como el de IA.

| Tabla | Qué guarda |
| --- | --- |
| `ArcaConnections` | Modo (los tres; solo se habilita el del certificado de la empresa), ambiente, CUIT, estado, punto de venta, los datos de la empresa del padrón, el resultado y la fecha de la última prueba, y la revisión. Es `IAuditable` |
| `ArcaCertificates` | Conexión, papel (activo o pendiente), alias, el CSR (público), la clave privada cifrada, el certificado, sus fechas y su huella. Hay a lo sumo uno activo y uno pendiente |
| `ArcaAccessTickets` | Certificado, servicio, token y firma cifrados, y vencimiento. Uno por certificado y servicio |

Desconectar borra las tres de verdad, sin eliminación lógica: son secretos.

### Ticket de acceso

- **El pedido (TRA)** se arma con `TimeProvider`, restando unos minutos a la hora de generación como recomienda el manual
  del WSAA. Se firma en CMS con `SignedCms` y se manda a `LoginCms`.
- **Reutilizar:** el ticket se usa mientras le quede margen.
- **Renovar, un host por vez:** en una transacción corta, con la fila del ticket bloqueada, se vuelve a mirar si otro host
  ya lo renovó. Si no, se toma una reserva con dueño y vencimiento. El pedido a ARCA va fuera de la transacción, y otra
  transacción corta guarda el ticket solo si la reserva sigue siendo propia. Un host que encuentra la reserva de otro espera
  unos segundos y vuelve a leer.
- **«Ya posee un TA válido»:** si ARCA contesta así, el ticket anterior se perdió. La fila guarda hasta cuándo no se puede
  pedir otro (2 minutos en producción, 10 en homologación) y la prueba lo informa con ese plazo. Ningún pedido HTTP queda
  esperando minutos.

### Clientes de ARCA

- **WSAA:** `LoginCms`. **WSFEv1:** `FEDummy` y `FEParamGetPtosVenta`. **Padrón:** `getPersona_v2` sobre la CUIT de la
  conexión.
- **Configuración:** las direcciones de cada ambiente van en `ArcaOptions`, validadas al arrancar, porque los documentos de
  ARCA alternan entre `afip.gov.ar` y `arca.gov.ar`. También son configuración los plazos, el tope de tamaño de las
  respuestas y el margen del ticket.
- **Transporte:** solo HTTPS, sin seguir redirecciones, con los loggers del `HttpClient` apagados y sin reintentos
  automáticos.

### Contrato HTTP

Son rutas MVC bajo `api/arca/connection`. Los nombres son indicativos y se fijan en el plan de implementación.

| Verbo y ruta | Permisos | Qué hace |
| --- | --- | --- |
| GET `api/arca/connection` | `arca.read` | Estado, ambiente, CUIT, datos de la empresa, certificados sin secretos, punto de venta, última prueba y revisión |
| POST `api/arca/connection/certificate-requests` | `arca.read` + `arca.manage` | Con CUIT, ambiente, alias y revisión esperada, genera la clave y el CSR. Sirve para el alta y para renovar |
| GET `api/arca/connection/certificate-requests/current` | ambos | Descarga el `.csr` pendiente |
| DELETE `api/arca/connection/certificate-requests/current` | ambos | Cancela la renovación |
| PUT `api/arca/connection/certificate` | ambos | Sube el `.crt`, lo valida y deja la conexión sin verificar, o reemplaza al activo si es una renovación |
| POST `api/arca/connection/test` | ambos | Prueba los dos servicios y guarda el resultado y los datos de la empresa |
| GET `api/arca/connection/points-of-sale` | ambos | Consulta a ARCA los puntos de venta de web service |
| PUT `api/arca/connection/point-of-sale` | ambos | Elige el punto de venta y activa la conexión si la última prueba salió bien |
| DELETE `api/arca/connection` | ambos | Desconecta |

- **Revisión:** las escrituras llevan la revisión esperada y responden 409 si cambió.
- **Llamadas a ARCA:** solo la prueba, la subida del certificado (que prueba sola) y la lista de puntos de venta, con un
  límite por operador. Guardar nunca llama a ARCA.
- **Lo que guarda la prueba:** el resultado se guarda solo si el certificado probado sigue siendo el activo, así una
  prueba lenta no pisa una renovación.
- **Contratos:** los `ToString` de los contratos ocultan la CUIT completa y el contenido del certificado.

### Errores

Se declaran en `ArcaConnectionErrors` en Domain y se traducen en `Errors.resx` y `Errors.en.resx`:

- **De validación:** CUIT inválida y alias inválido.
- **Del certificado:** ilegible, otra clave, otra CUIT, otro ambiente, vencido y sin pedido pendiente.
- **De la conexión:** revisión desactualizada (409), punto de venta que ARCA ya no devuelve y conexión sin probar.

Lo que contesta ARCA en una prueba no es un error del pedido: es el resultado de cada servicio, con un motivo estable
(servicio sin autorizar, reloj desfasado, ya posee un ticket, no responde).

### Robustez y privacidad

- **Robustez:** la prueba solo lee y no tiene efecto legal, así que no usa el motor durable. Alcanzan la revisión, el 409 y
  la reserva del ticket ([ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md)).
- **Logs:** con `[LoggerMessage]` y sin token, firma, clave, CSR ni certificado. La CUIT va enmascarada, como los teléfonos.
- **Fechas:** UTC desde `TimeProvider`. El TRA lleva sus horas con el huso explícito, como pide el manual del WSAA.

## Pruebas

- **Unitarias (Domain y Application):** el dígito de la CUIT, el ciclo de estados, el armado del TRA, la firma CMS
  verificada con la clave pública, el CSR, la validación del certificado (otra clave, otra CUIT, otro ambiente, vencido) y
  la lectura del padrón.
- **De arquitectura:** las rutas en `ExplicitRouteInventoryTests`, el módulo sin referencias desde el núcleo y sus aportes
  con `IModuleArchitectureContribution`.
- **De integración:** un ARCA simulado dentro de los tests, que responde WSAA, WSFEv1 y el padrón con los mismos
  sobres SOAP, más PostgreSQL con Testcontainers. Cubre el alta completa, el ticket compartido entre dos hosts con la reserva,
  «ya posee un TA válido», un servicio sin autorizar, la renovación sin corte, el 409 y desconectar.
  [arcasim](https://github.com/federicomoroz/arcasim) sirve de referencia para los sobres, revisado antes de copiar nada.
- **Homologación real, al cerrar:** con un certificado de WSASS para la CUIT del usuario. La app genera el CSR, el usuario
  hace los pasos en ARCA con su clave fiscal y después se prueba la conexión.

## A confirmar al programar

- **En el manual 3.4 del padrón:** cómo se deduce la condición frente al IVA a partir de los impuestos inscriptos, y la
  dirección de homologación de `personaServiceA5`. Producción es `aws.afip.gov.ar/sr-padron/webservices/personaServiceA5`
  ([referencia](https://docs.afipsdk.com/siguientes-pasos/web-services/padron-de-constancia-de-inscripcion)).
- **Del certificado:** el algoritmo de firma que exige ARCA y los emisores de cada ambiente, que sirven para saber si un
  `.crt` es de homologación o de producción.
- **El sujeto del CSR:** además de `serialNumber=CUIT …`, `O` y `CN`. El ejemplo usual pone el alias en `CN`
  ([guía](https://docs.afipsdk.com/recursos/tutoriales-pagina-de-arca/obtener-certificado-de-produccion.md)).

## Al cerrar la etapa

- La regla vigente pasa a `docs/features/arca.md`, junto con la guía `docs/guides/configurar-arca.md` (certificados,
  ambientes y renovación) y `docs/guides/quitar-arca.md`, como las de IA.
- Cada carpeta nueva lleva su `AGENTS.md` y entra en el [mapa de instrucciones](../architecture/mapa-de-instrucciones.md).
- Se ofrece el traspaso al multitenant.

## Fuera de la etapa

- Emitir comprobantes, el PDF fiscal y la nota de crédito: etapa 3.
- Las tablas de ARCA como catálogos y el autocompletar del cliente: etapa 3.
- La delegación guiada y el robot: etapas 5 y 6.
- Importar un certificado existente y tener los dos ambientes a la vez: descartados.
