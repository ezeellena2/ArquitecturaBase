# Publicar en Azure

Por dónde empezar para subir ArquitecturaBase a Azure, publicar una versión o cambiar algo de una instalación, y qué
quedó configurado en cada lugar. El detalle de cada pieza está en las guías enlazadas. Claude Code sigue este
procedimiento con el skill [`publicar-en-azure`](../../.claude/skills/publicar-en-azure/SKILL.md).

## Cómo está armado

- **Una instalación por grupo de recursos** (`rg-arquitecturabase-<instalación>`): la Api con el front adentro (Container
  App), Redis interno, PostgreSQL, Storage, sus anillos de claves, su identidad de GitHub, un presupuesto y una alerta de
  errores. `dev` es donde se prueba y se rompe; cada cliente tiene la suya hasta que esté el multitenant
  ([producción por cliente](produccion-por-cliente.md)).
- **Lo compartido:** el registry `acrarqbasedev689638` (en el grupo de dev; todas corren la misma imagen), la suscripción,
  la app de Meta, el cliente OAuth de Google, la clave de Google Places y la Gmail que manda los correos.
- **Región:** Central US, la más barata con PostgreSQL B1ms disponible para la suscripción
  ([por qué](azure-dev.md#etapa-2-elegir-y-crear-los-recursos)).
- **Publicar:** un push a `main` del back o del front corre `deploy.yml`, que compila, prueba, arma el paquete (imagen y
  bundle de migraciones) y lo publica en dev. Un cliente recibe ese mismo paquete con **«Publicar en un cliente»** y la
  aprobación del titular; nada se compila para producción ([despliegue](../guides/despliegue.md)).
- **La infraestructura** no la crea el pipeline: la crea [`provision-azure.sh`](provision-azure.sh), que se puede volver a
  correr sin regenerar contraseñas, certificados ni anillos.

Dev está en https://ca-arquitecturabase-dev.lemonmushroom-745a5099.centralus.azurecontainerapps.io; los clientes, con su
dirección y desde cuándo, en la [tabla de clientes](produccion-por-cliente.md#clientes).

## Qué se configura y dónde

| Lugar | Qué | Cómo se carga |
|---|---|---|
| Azure, Container App de cada instalación | Secretos generados (`db-connection`, `redis-connection`, `login-code-hash-key`, los tres PFX, `pfx-password`, anillos de IA y ARCA) y los del titular (SMTP, Google, Places, WhatsApp); las variables de entorno que los referencian; probes y tamaño | `provision-azure.sh`. Un cambio puntual, con `az containerapp secret set` y reinicio de la revisión |
| Azure, grupo de cada instalación | Presupuesto mensual (USD 70) y alerta de errores 5xx al correo del titular | `provision-azure.sh` con `ALERT_EMAIL` |
| GitHub, backend | Environment `dev` o `prod-<cliente>` (éste con el titular como revisor) con las variables `AZURE_*`; variable de repositorio `DEV_DEPLOY_ENABLED=true` | `provision-azure.sh` con `gh` autenticado; la variable de repositorio, una vez ([etapa 4](azure-dev.md#etapa-4-conectar-github-con-azure)) |
| GitHub, front | Secreto `BACKEND_WORKFLOW_TOKEN` (PAT fine-grained, vence el 2027-11-26) y `DEV_DEPLOY_ENABLED=true` | Una vez ([etapa 5](azure-dev.md#etapa-5-permitir-que-un-push-del-front-solicite-la-publicación)) |
| Google Cloud, proyecto `arquitecturabase-509114` | En el cliente OAuth, el redirect `https://<origen>/signin-google` de cada instalación. La clave «ArquitecturaBase Places Servidor», sólo para Places API (New) y sin restricción de IP | A mano en la consola, una vez por instalación ([Places](../guides/configurar-google-places.md#credencial)) |
| Meta, app de WhatsApp | Webhook de la app hacia Azure dev; el número de prueba con un webhook propio hacia el túnel de la PC; cada número de cliente, con un webhook propio hacia su instalación | El de cada cliente lo pone la publicación ([WhatsApp en local](../guides/whatsapp-en-local.md#el-túnel-para-recibir-los-webhooks)) |
| DNS del dominio del cliente | `CNAME` del subdominio hacia la Container App y `TXT` `asuid.<subdominio>` | El titular, con lo que imprime el script ([dominio](produccion-por-cliente.md#3-el-dominio)) |
| La pantalla de cada instalación | Clave de OpenAI y modelo (`gpt-6.1-sol`) en Configuración › Inteligencia artificial, guardada cifrada en su base; información del negocio; usuarios | El administrador, después de publicar |

## De dónde salen los valores

Los del titular están en los user-secrets de la Api de su PC: se leen de ahí y se pasan al script como variables de
entorno, sin escribirlos en archivos del repositorio ni mostrarlos.

| Variable de `provision-azure.sh` | User-secret de la Api |
|---|---|
| `SMTP_USERNAME`, `SMTP_PASSWORD`, `SMTP_FROM_ADDRESS` | `Email:Smtp:UserName`, `Email:Smtp:Password`, `Email:Smtp:FromAddress` |
| `SEED_ADMIN_EMAIL` y `ALERT_EMAIL` | `Seed:AdminEmail` (el titular; un cliente puede tener otro administrador inicial) |
| `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET` | `Authentication:Google:ClientId`, `Authentication:Google:ClientSecret` |
| `GOOGLE_PLACES_API_KEY` | `ArtificialIntelligence:GooglePlaces:ApiKey` |
| `EXTRA_SETTINGS_FILE` | Cada `ArtificialIntelligence:Assistant:*`, con `__` en lugar de `:`, una por línea: sin esos perfiles el asistente no anda |
| `WHATSAPP_*` | `WhatsApp:PhoneNumberId`, `AccessToken`, `AppSecret`, `VerifyToken`, `WabaId`, `DisplayPhoneNumber`, `SendArgentineMobilesWithoutNine`; sólo en la instalación dueña de ese número |

No se copian `FileStorage:*` (la cuenta de Storage del desarrollo local), los `SecretProtection` de IA y ARCA (cada
instalación genera su anillo) ni los user-secrets del AppHost. Fijos: `AZURE_SUBSCRIPTION_ID=59608b9d-bdb8-466a-a5a3-149fe20a8e2b`,
`GITHUB_REPOSITORY=ezeellena2/ArquitecturaBase` y `LOCATION=centralus`; dev además lleva `PG=psql-arqbase-dev-cus-689638` y
`ACA_ENV=cae-arquitecturabase-dev-cus`.

## Procedimientos

### Instalación nueva

1. Juntar el nombre corto del cliente (2 a 12 minúsculas y dígitos), el subdominio si lo hay, el administrador inicial y
   el tope de correos. Confirmar el costo con el titular: unos USD 35 a 60 por mes por instalación.
2. Correr `provision-azure.sh` con los valores de arriba (tarda 15 a 20 minutos).
3. Agregar `https://<origen>/signin-google` al cliente OAuth de Google.
4. Con dominio: crear los dos registros DNS y volver a correr el script; el redirect de Google pasa al dominio.
5. **Actions › Publicar en un cliente** con `prod-<cliente>` (o
   `gh workflow run deploy-production.yml -f environment=prod-<cliente>`) y aprobarlo en «Review deployments».
6. Comprobar (abajo), entrar con el administrador, cargar la clave de IA con «Probar conexión» y la información del negocio.
7. Anotar la instalación en la [tabla de clientes](produccion-por-cliente.md#clientes).

### Publicar una versión

- **Dev:** un push a `main` publica solo. Se sigue en Actions › «Verificar y desplegar Azure dev» (20 a 30 minutos).
- **Un cliente:** «Publicar en un cliente», que por defecto toma la última corrida de dev en verde; el paquete dura 30 días.

### Cambiar una configuración

- Un valor que pasa el script (SMTP, Google, Places, WhatsApp, perfiles de IA): volver a correrlo con el valor nuevo.
  Vuelve a cargar los secretos que recibe y crea una revisión con las variables; no regenera lo generado.
- Un secreto suelto: `az containerapp secret set` y después `az containerapp revision restart` de la revisión activa;
  sin el reinicio, la Api sigue con el valor anterior.
- El tamaño: `az containerapp update --cpu … --memory …` (dev usa 0,25 vCPU y 0,5 GiB; los clientes, 0,5 y 1 GiB).

### Dar de baja

[Baja de un cliente](produccion-por-cliente.md#baja-de-un-cliente): respaldar, sacarle el webhook al número y borrar el
grupo, el environment y el redirect de Google.

## Comprobar

- `az containerapp revision list -g <grupo> -n <app>`: la revisión activa `Healthy` y `Running`.
- `https://<origen>/health` responde `Healthy`.
- Entrar en el navegador con el administrador: la cuenta nace con el idioma del navegador y se cambia desde su menú.
- Logs de la Api: `az containerapp logs show -g <grupo> -n <app> --tail 100`.

## Trampas conocidas

- **Azure CLI 2.93:** `az postgres flexible-server db` usa `--name`, `firewall-rule` usa `--server-name` y `--name`,
  `--public-access None` deja el acceso público apagado, y `az containerapp create --yaml` falla con «System.Boolean» (el
  script usa `az rest`). Están resueltos en el script ([azure-setup](azure-setup.md)).
- **Git Bash:** sin `MSYS_NO_PATHCONV=1` convierte los IDs de recursos en rutas de Windows; la salida de `az` trae `\r`.
  Los `.sh` van con LF (`.gitattributes`).
- **Región:** que `list-skus` ofrezca B1ms no garantiza que se pueda crear; con `SkuNotAvailable`, `PG_LOCATION`. Un
  intento fallido deja el nombre del servidor ocupado un rato: `PG=` elige otro.
- **Un secreto mal guardado** (por ejemplo, la conexión de una base que no llegó a crearse) se borra antes de volver a
  correr el script: él sólo agrega los que faltan.
- **Google Places:** la clave del navegador (restringida por referencia) no sirve desde un servidor y la Api lo registra
  como `Ai.Places.ConfigurationInvalid`.
- **Asistente:** la clave de OpenAI no viaja con el script; sin los perfiles calificados tampoco anda aunque la clave esté.
- **WhatsApp:** mientras dev use el número de prueba, que apunta al túnel de la PC, Azure dev no recibe mensajes.
