# Producción por cliente

Cómo dar de alta, actualizar y dar de baja la instalación de una empresa cliente. Hasta que el multitenant esté listo,
cada cliente tiene su instalación aislada en Azure ([plan y decisiones](../plans/2026-11-27-produccion-por-cliente.md)):
su grupo de recursos, su entorno con la Api y Redis, su PostgreSQL, su Storage, sus anillos de claves, su origen público
y su número de WhatsApp. Lo único compartido es el registry, porque todos corren la misma imagen. [Azure dev](azure-dev.md)
sigue siendo donde se prueba y se rompe.

Un push a `main` publica sólo en dev. Un cliente cambia cuando el titular corre **«Publicar en un cliente»**
(`deploy-production.yml`) y lo aprueba: recibe la imagen y el bundle de una corrida de dev que terminó en verde, sin volver a
compilar.

**Costo:** unos USD 35 a 60 por mes por cliente (PostgreSQL B1ms, la Api y Redis siempre prendidos, Storage y logs), más
los mensajes de WhatsApp que cobre Meta. El script deja un presupuesto mensual del grupo que avisa al titular.

## Alta de un cliente

### 1. Lo que junta el titular

| Dato | Para qué |
|---|---|
| Nombre corto, de 2 a 12 minúsculas y dígitos (por ejemplo `clementina`) | Los nombres de los recursos y el environment `prod-<cliente>` |
| Subdominio (por ejemplo `ocha.midominio.com`) | El origen público; opcional al principio |
| Correo del administrador del cliente | `SEED_ADMIN_EMAIL`: la única cuenta que se puede crear al empezar (InviteOnly); desde ahí invita al resto |
| Cuenta SMTP | Por defecto la Gmail del titular; con varias instalaciones comparte el cupo de unos 500 por día, y conviene `EMAIL_DAILY_LIMIT` (más de 50, por la reserva de invitaciones) |
| Número de WhatsApp del cliente, registrado en la app de Meta del titular | `WHATSAPP_PHONE_NUMBER_ID`, `WHATSAPP_WABA_ID` y `WHATSAPP_DISPLAY_PHONE_NUMBER`; el token, el App Secret y la palabra de verificación son los de la app |

El número se registra en la app de Meta («Paso 2. Configuración de producción» › agregar número): el código que llega por
SMS o llamada lo pone el titular. En esa cuenta de WhatsApp tienen que estar aprobadas las plantillas (`codigo_ingreso` e
`invitacion_acceso`) y cargado un medio de pago para los mensajes que inicia la empresa.

### 2. Crear la instalación

```bash
INSTALLATION=ocha CUSTOM_DOMAIN=ocha.midominio.com ALERT_EMAIL=<correo del titular> \
AZURE_SUBSCRIPTION_ID=<id> GITHUB_REPOSITORY=ezeellena2/ArquitecturaBase \
SEED_ADMIN_EMAIL=admin@cliente.com SMTP_USERNAME=... SMTP_PASSWORD=... SMTP_FROM_ADDRESS=... EMAIL_DAILY_LIMIT=150 \
WHATSAPP_PHONE_NUMBER_ID=... WHATSAPP_ACCESS_TOKEN=... WHATSAPP_APP_SECRET=... WHATSAPP_VERIFY_TOKEN=... \
WHATSAPP_WABA_ID=... WHATSAPP_DISPLAY_PHONE_NUMBER=... WHATSAPP_SEND_ARGENTINE_MOBILES_WITHOUT_NINE=true \
GOOGLE_CLIENT_ID=... GOOGLE_CLIENT_SECRET=... GOOGLE_PLACES_API_KEY=... EXTRA_SETTINGS_FILE=ia-perfiles.txt \
LOCATION=centralus bash docs/deploy/provision-azure.sh
```

`EXTRA_SETTINGS_FILE` lleva una configuración no secreta por línea (`Clave__Anidada=valor`). Sirve para los perfiles
calificados del asistente (`ArtificialIntelligence__Assistant__…`): sin ellos el asistente no anda aunque se cargue la
clave del proveedor. Esa clave se carga después en Configuración › Inteligencia artificial de la instalación, porque se
guarda cifrada en su base.

Lo mismo que en dev ([etapa 2](azure-dev.md#etapa-2-elegir-y-crear-los-recursos)): se corre en Cloud Shell o en Git Bash
con `az login`, genera dentro de Azure las contraseñas, los PFX y los anillos con un `InstallationId` propio, y se puede
volver a correr sin regenerar nada. Para un cliente además:
- crea el environment `prod-<cliente>`, limitado a `main` y con el titular como revisor obligatorio, con sus variables;
- le da a la identidad de GitHub de la instalación AcrPush y Reader sobre el registry compartido, y a la Api, AcrPull;
- deja los respaldos de PostgreSQL en 14 días.

Si la región no tiene B1ms para la suscripción, `PG_LOCATION` lo pone en otra cercana; un intento fallido deja el nombre
ocupado un rato, y `PG=` elige otro ([azure-setup.md](azure-setup.md#4-postgresql-flexible-server)).

### 3. El dominio

En la primera corrida el script no puede enlazar el dominio: al terminar imprime los dos registros que pide Azure, un
`CNAME` del subdominio hacia el FQDN de la Container App y un `TXT` `asuid.<subdominio>` con su ID de verificación. El
titular los crea en el proveedor del dominio y vuelve a correr el script: enlaza el subdominio con un certificado
administrado (gratis, se renueva solo) y pasa el issuer y las URI del cliente `web` al dominio. Hasta entonces la
instalación anda con el FQDN de Azure.

Para el ingreso con Google, `https://<subdominio>/signin-google` va a los redirect URIs del cliente OAuth. Sin
`GOOGLE_CLIENT_ID`, Google queda apagado y se entra con el código por correo.

### 4. Primera publicación

En GitHub: **Actions › Publicar en un cliente › Run workflow**, con el environment `prod-<cliente>`. Sin corrida elegida,
toma la última de dev en verde (su paquete se guarda 30 días). El job espera la aprobación del titular («Review
deployments»), y después:
1. comprueba los probes, sube la imagen si falta y aplica las migraciones con el acceso temporal del runner;
2. crea la revisión, espera que quede sana y prueba `/health`, la web y el discovery desde Internet, en el origen público;
3. le pone al número de WhatsApp del cliente un webhook propio hacia `https://<origen>/webhooks/whatsapp`
   ([webhook overrides](https://developers.facebook.com/documentation/business-messaging/whatsapp/webhooks/override)).
   El webhook de la app de Meta sigue en dev; cada número entrega sólo en su instalación.

Después, el administrador del cliente entra con su correo y queda como Admin.

## Actualizar un cliente

El mismo workflow, con la corrida de dev que se quiere llevar. Una publicación por instalación a la vez: si hay otra en
curso, espera. Las migraciones van antes de la revisión nueva, igual que en dev ([despliegue](../guides/despliegue.md#el-orden-bundle-después-imagen)).

## Baja de un cliente

1. Respaldar lo que el cliente se lleve: la base (`pg_dump` o una restauración a un momento dado) y el contenedor de archivos.
2. Sacarle al número su webhook propio (`override_callback_uri` vacío, [en local](../guides/whatsapp-en-local.md#el-túnel-para-recibir-los-webhooks)) y darlo de baja en la app de Meta.
3. `az group delete --name rg-arquitecturabase-<cliente>`: borra la instalación entera, con su identidad de GitHub.
4. Borrar el environment `prod-<cliente>` en GitHub y su redirect en la consola de Google, si tenía.

## Restaurar

Igual que en dev ([azure-setup.md](azure-setup.md#restaurar-a-un-momento-dado)): la base y el contenedor de archivos a la
misma hora, con los workers de WhatsApp, formularios y ARCA apagados la primera vez.
