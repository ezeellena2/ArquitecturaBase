# ADR 0027: delegación guiada en ARCA

## Estado

**Aceptada**, 2026-11-09, con las decisiones del usuario de ese día. Es la decisión estructural de la etapa 5 de la
[hoja de ruta de ARCA](../plans/2026-10-31-arca-formularios-estandar.md), con su
[plan](../plans/2026-11-09-arca-etapa-5-delegacion-guiada.md). Extiende el [ADR 0023](0023-arca-como-modulo-opcional.md) a los
modos de delegación, sin cambiar el anillo ni el ticket compartido.

## Contexto

En el modo 1 la empresa genera en la app el certificado de su propia CUIT y lo da de alta en ARCA. En el modo 2 la empresa
delega sus web services en la CUIT de la plataforma: la plataforma firma el pedido al WSAA con su certificado y opera en
nombre de la empresa. ARCA lo registra en el Administrador de Relaciones, que no tiene API, y lo informa en el ticket de
acceso: el token trae, en `relations`, las CUIT que la plataforma representa para ese servicio.

ARCA no deja usar un certificado desde dos equipos a la vez, así que cada instalación tiene su propio certificado de la CUIT
de la plataforma. En esta plantilla hay una conexión por instalación.

## Decisión

1. **El certificado de la plataforma lo guarda la conexión,** como dice la hoja de ruta. Es el mismo `ArcaCertificate` del
   modo 1, con el mismo anillo, el mismo propósito en el AAD, la misma renovación y los mismos tickets. Sólo cambia la CUIT
   del pedido y de la inspección: la de la plataforma. No hay una cuenta de plataforma aparte.
2. **La CUIT de la plataforma es configuración de la instalación** (`Arca:Platform:Cuit`, con su nombre para la guía), no
   un dato que se carga en la pantalla. La conexión la guarda al empezar (`PlatformCuit`), porque el certificado queda atado
   a ella. Sin la configuración, el modo 2 no se ofrece.
3. **La CUIT de la conexión es siempre la de la empresa,** y cada servicio declara en nombre de quién actúa. Los que
   operan para la empresa (Facturación, `Auth.Cuit`) se delegan; las consultas (la constancia de inscripción,
   `cuitRepresentada`) van con la CUIT de quien firma, que en el modo 2 es la plataforma, y no se delegan: el manual de la
   constancia sólo pide que esa CUIT esté en las relaciones. En el modo 1 las dos CUIT son la misma.
4. **La delegación se detecta en `Connection`:** en el modo 2, el ticket de un servicio que actúa para la empresa no se
   entrega si sus relaciones no la incluyen, y el servicio queda «sin delegar». El proveedor de tickets lee el token;
   `Connection` le dice qué CUIT tiene que estar. Si el ticket guardado no la incluye, se pide uno nuevo
   una vez, con el bloqueo de la etapa 1 si ARCA todavía no da otro. La prueba es la detección: no hay un proceso que mire
   solo.
5. **La app nunca pide ni recibe una clave fiscal** en este modo: los pasos en ARCA los hace cada uno con la suya, guiado
   por la pantalla. El robot de la etapa 6 ([ADR 0026](0026-robot-de-delegacion-arca.md)) es lo único que la maneja.

## Consecuencias

- **Un alias por instalación:** las relaciones de ARCA son por computador fiscal, así que la guía pide un alias propio.
  Renovar conviene con el mismo alias, para que las delegaciones sigan asignadas.
- **Desconectar borra la clave de la plataforma** de esa instalación, como en el modo 1. Para volver, la plataforma agrega
  un certificado nuevo al mismo alias.
- **El token se lee:** la detección depende del formato del token del WSAA. Si no se puede leer, no frena y deja un log;
  ARCA rechaza igual el servicio que no corresponde.
- **Multitenant:** allá la CUIT de la plataforma es una sola para todas las organizaciones y el certificado no puede vivir
  en cada conexión: es una cuenta de plataforma por instalación, con un ticket cuyas relaciones sirven a todas. El traspaso
  lo explica.

## Alternativas descartadas

- **Una cuenta de plataforma aparte de la conexión, con su pantalla y su permiso:** duplica el ciclo del certificado para
  una instalación que tiene una sola conexión. Se retoma en el multitenant, donde sí hace falta.
- **La CUIT de la plataforma cargada en la pantalla:** la empresa podría cambiarla, y es un dato de quien despliega.
- **Detectar con un error de WSFEv1** (el 600 o 601 de la CUIT que no está en las relaciones): ata la detección a una
  familia, y `Connection` no las conoce.
- **Que la empresa delegue también las consultas:** suma un paso para la empresa y otro para la plataforma, y ARCA no lo
  pide.
- **Un proceso que revise solo cada tanto:** suma un worker para algo que la persona confirma con un botón.
