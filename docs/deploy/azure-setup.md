# Desplegar a Azure Container Apps

Receta técnica de recursos para `.github/workflows/deploy.yml`. Para este ambiente de pruebas, empezá por [Azure dev](azure-dev.md): explica conceptos, etapas, región/tamaños pendientes y la configuración exacta de GitHub. Los bloques Bash se ejecutan en Azure Cloud Shell con Bash; no copiarlos directamente en PowerShell.
El pipeline no crea recursos: solo sube una imagen nueva y actualiza la aplicación.

Este procedimiento prepara Container Apps, PostgreSQL y el registry. **También hace falta Redis**:
provisionarlo con red privada/TLS y configurar `ConnectionStrings__cache` y `Caching__KeyPrefix`
según la [guía de despliegue](../guides/despliegue.md#redis). Los comandos de esta guía no crean
ese recurso ni Blob Storage. El workflow ya compila y empaqueta el front con la API; su ejecución real en Azure sigue en los [pendientes](../plans/verificacion-y-produccion.md).

> **Costo.** Container Apps tiene una cuota gratuita mensual, pero PostgreSQL Flexible
> Server, el registry y Redis pueden generar cargos. Revisá los precios de la región y los
> recursos elegidos antes de crearlos; esta guía no fija una estimación mensual. Si es solo para probar, borrá el grupo de
> recursos al terminar: `az group delete --name rg-arquitecturabase-dev --yes`.

## 0. Requisitos

- Una cuenta de Azure con una suscripción activa.
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) instalada.

```bash
az login
```

```bash
az account show --query "{suscripcion:name, id:id, tenant:tenantId}" --output table
```

Anotá el `id` (subscription id) y el `tenant`: los vas a necesitar al final.

## 1. Variables

Todo lo demás usa estos nombres. El nombre del registry tiene que ser único en todo
Azure y solo admite minúsculas y números: si `az acr create` falla por nombre tomado,
agregale algo al final y configurá `AZURE_REGISTRY` en las variables del environment `dev` de GitHub, sin editar el workflow.

```bash
RG=rg-arquitecturabase-dev
LOCATION="REGION_ELEGIDA"
ACR=acrarquitecturabasedev
ACA_ENV=cae-arquitecturabase-dev
APP=ca-arquitecturabase-dev
PG=psql-arquitecturabase-dev
PG_ADMIN=pgadmin
PG_PASSWORD="<poné-una-contraseña-fuerte-acá>"
```

Reemplazá `REGION_ELEGIDA` y revisá disponibilidad y precios antes de ejecutar. Estos nombres son ejemplos; ACR, PostgreSQL y Storage pueden requerir sufijos únicos.

## 2. Grupo de recursos

Es la carpeta que contiene todo. Borrarla borra todo lo de adentro.

```bash
az group create --name $RG --location $LOCATION
```

## 3. Container Registry

El depósito de imágenes.

```bash
az acr create --resource-group $RG --name $ACR --sku Basic
```

## 4. PostgreSQL Flexible Server

La base real. Reemplaza al contenedor que levanta el AppHost en desarrollo.

```bash
az postgres flexible-server create --resource-group $RG --name $PG --location $LOCATION --admin-user $PG_ADMIN --admin-password "$PG_PASSWORD" --sku-name Standard_B1ms --tier Burstable --version 18 --storage-size 32 --public-access None
```

`--public-access None` crea el servidor con acceso público habilitado pero sin reglas de firewall. El acceso se habilita por reglas posteriores. La pipeline abre una regla temporal para su runner y la borra al terminar. `0.0.0.0` tiene otro significado: permite recursos de Azure. [Referencia de Azure CLI](https://learn.microsoft.com/en-us/cli/azure/postgres/flexible-server?view=azure-cli-latest#az-postgres-flexible-server-create).

Creá la base:

```bash
az postgres flexible-server db create --resource-group $RG --server-name $PG --database-name appdb
```

Permitir que los servicios de Azure (entre ellos tu Container App) alcancen el servidor:

Esta regla permite conexiones de recursos de Azure de otras suscripciones también; sigue exigiendo autenticación a PostgreSQL. Es una simplificación de la receta de dev, no una red privada. Antes de incorporar datos reales, elegir acceso privado o restringir a las IP de salida del ambiente.

```bash
az postgres flexible-server firewall-rule create --resource-group $RG --name $PG --rule-name allow-azure-services --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0
```

## 5. Container Apps environment y aplicación

```bash
az extension add --name containerapp --upgrade
```

```bash
az provider register --namespace Microsoft.App --wait
```

```bash
az containerapp env create --resource-group $RG --name $ACA_ENV --location $LOCATION
```

La aplicación se crea con una imagen de ejemplo: el primer deploy del pipeline la
reemplaza por la tuya. `--system-assigned` le da identidad propia para leer del registry
sin usuario ni contraseña.

```bash
az containerapp create --resource-group $RG --name $APP --environment $ACA_ENV --image mcr.microsoft.com/k8se/quickstart:latest --target-port 8080 --ingress external --system-assigned --min-replicas 1 --max-replicas 1 --revisions-mode single
```

Darle permiso de lectura sobre el registry:

```bash
APP_IDENTITY=$(az containerapp show --resource-group $RG --name $APP --query identity.principalId --output tsv) && ACR_ID=$(az acr show --resource-group $RG --name $ACR --query id --output tsv) && az role assignment create --assignee $APP_IDENTITY --role AcrPull --scope $ACR_ID
```

```bash
az containerapp registry set --resource-group $RG --name $APP --server $ACR.azurecr.io --identity system
```

## 6. Configuración de la aplicación

La Api fuera de Aspire no recibe nada automáticamente: hay que darle todo. Los valores
sensibles van como *secrets* de Container Apps y se referencian con `secretref:`.

```bash
CONN="Host=$PG.postgres.database.azure.com;Port=5432;Database=appdb;Username=$PG_ADMIN;Password=$PG_PASSWORD;SSL Mode=Require"
```

```bash
az containerapp secret set --resource-group $RG --name $APP --secrets db-connection="$CONN" login-code-hash-key="$(openssl rand -base64 48)"
```

```bash
az containerapp update --resource-group $RG --name $APP --set-env-vars ASPNETCORE_ENVIRONMENT=Production ConnectionStrings__appdb=secretref:db-connection Authentication__LoginCode__HashKey=secretref:login-code-hash-key ForwardedHeaders__TrustAll=true
```

> El doble guion bajo es cómo .NET lee secciones anidadas desde variables de entorno:
> `ConnectionStrings__appdb` es `ConnectionStrings:appdb`.
> `ForwardedHeaders__TrustAll=true` es seguro acá porque Kestrel sólo recibe tráfico a través del ingress de
> Container Apps. No hay que usarlo si la aplicación también queda accesible directamente: en ese caso se declaran
> `ForwardedHeaders:KnownProxies` o `ForwardedHeaders:KnownNetworks` (CIDR).

Este ejemplo carga solo PostgreSQL, HMAC y confianza del proxy. Faltan Redis, Google, SMTP,
el origen público y el cliente `web`: completar la [tabla de configuración](../guides/despliegue.md#configuración-obligatoria-en-production)
antes de desplegar la imagen de la Api. Redis usa `ConnectionStrings__cache` como secreto y
`Caching__KeyPrefix` compartido entre réplicas y distinto por instalación/ambiente.

### Certificados de OpenIddict

Fuera de Development, OpenIddict firma y cifra los tokens con dos PFX propios. No son
certificados TLS: son del servidor de tokens, pueden ser autofirmados y de larga duración.
Se pasan en base64 como cualquier otro secreto, sin archivos en el contenedor.

Generarlos (uno para firma y otro para cifrado):

```bash
openssl req -x509 -newkey rsa:2048 -nodes -days 1825 -subj "/CN=ArquitecturaBase Signing" -keyout signing.key -out signing.crt && openssl pkcs12 -export -out signing.pfx -inkey signing.key -in signing.crt -passout pass:"$PFX_PASSWORD"
```

```bash
openssl req -x509 -newkey rsa:2048 -nodes -days 1825 -subj "/CN=ArquitecturaBase Encryption" -keyout encryption.key -out encryption.crt && openssl pkcs12 -export -out encryption.pfx -inkey encryption.key -in encryption.crt -passout pass:"$PFX_PASSWORD"
```

En Windows, sin openssl, generá ambos PFX. Este ejemplo crea `signing.pfx`; repetilo con el sujeto `CN=ArquitecturaBase Encryption` y el archivo `encryption.pfx`, usando la misma contraseña que cargarás como secreto:

```powershell
$cert = New-SelfSignedCertificate -Subject "CN=ArquitecturaBase Signing" -CertStoreLocation "Cert:\CurrentUser\My" -KeyExportPolicy Exportable -KeySpec Signature -NotAfter (Get-Date).AddYears(5); Export-PfxCertificate -Cert $cert -FilePath signing.pfx -Password (ConvertTo-SecureString "PONÉ-UNA-CLAVE" -Force -AsPlainText)
```

Cargarlos como secrets de la aplicación:

```bash
az containerapp secret set --resource-group $RG --name $APP --secrets signing-pfx="$(base64 -w0 signing.pfx)" encryption-pfx="$(base64 -w0 encryption.pfx)" pfx-password="$PFX_PASSWORD"
```

```bash
az containerapp update --resource-group $RG --name $APP --set-env-vars Authentication__Certificates__Signing__Base64=secretref:signing-pfx Authentication__Certificates__Signing__Password=secretref:pfx-password Authentication__Certificates__Encryption__Base64=secretref:encryption-pfx Authentication__Certificates__Encryption__Password=secretref:pfx-password
```

> **Guardá los PFX en un lugar seguro y borralos de tu máquina.** Si los perdés y los
> regenerás, todos los tokens y refresh tokens emitidos dejan de valer y las sesiones
> abiertas se caen. Rotarlos es una operación planificada, no algo que se hace de apuro.

## 7. La identidad del pipeline (OIDC)

Acá es donde GitHub y Azure se dan la mano, sin contraseñas.

Creá la identidad:

```bash
az ad app create --display-name github-arquitecturabase && APP_ID=$(az ad app list --display-name github-arquitecturabase --query "[0].appId" --output tsv) && az ad sp create --id $APP_ID && echo "APP_ID=$APP_ID"
```

Dale permiso, acotado al grupo de recursos y a nada más:

```bash
SUB_ID=$(az account show --query id --output tsv) && az role assignment create --assignee $APP_ID --role Contributor --scope /subscriptions/$SUB_ID/resourceGroups/$RG
```

Contributor permite administrar el grupo dev. Declarar también AcrPush sobre el registry hace explícito el permiso de publicar imágenes y permite acotar Contributor más adelante:

```bash
ACR_ID=$(az acr show --resource-group $RG --name $ACR --query id --output tsv)
az role assignment create --assignee $APP_ID --role AcrPush --scope $ACR_ID
```

Usar el modo de permisos RBAC del registry para esta receta. Los recursos con ABAC requieren otros roles y una revisión de la configuración.

Solo el job deploy usa Azure y apunta al environment dev. Agregar una credencial federada con el subject de ese environment; build no necesita otra. Para el formato inmutable por defecto de este repositorio:

```bash
az ad app federated-credential create --id $APP_ID --parameters '{"name":"github-dev","issuer":"https://token.actions.githubusercontent.com","subject":"repo:ezeellena2@128924334/ArquitecturaBase@1376411164:environment:dev","audiences":["api://AzureADTokenExchange"]}'
```

Si el repo personalizó sus claims, usar el subject realmente emitido. Ver [la explicación y referencia oficial](azure-dev.md#etapa-4-conectar-github-con-azure). No agregar la credencial anterior de production ni una de build que no se usa.

## 8. Configuración en GitHub

Crear el environment **dev** en el backend y permitir solo main. Identificadores y nombres son variables de ese environment; DB_CONNECTION_STRING es un secret del environment. DEV_DEPLOY_ENABLED es una variable **de repositorio**, inicialmente false.

La [tabla de Azure dev](azure-dev.md#etapa-4-conectar-github-con-azure) es la fuente exacta de los nombres. El front tiene su propio interruptor y el secret BACKEND_WORKFLOW_TOKEN limitado a solicitar el workflow del backend; su [configuración](azure-dev.md#etapa-5-permitir-que-un-push-del-front-solicite-la-publicación) no requiere credenciales de Azure.

Para dev automático no configurar revisores obligatorios. Eso se decide aparte al crear un ambiente de producción.

## 9. Probar

Primero completá recursos, Redis, configuración y permisos. Con ambos workflows en GitHub, verificá build con `DEV_DEPLOY_ENABLED=false`. Después activá el interruptor del backend y ejecutá manualmente en `main`; seguí la corrida en *Actions*. Activá el front cuando funcione la primera publicación. La pipeline comprueba salud, SPA y discovery; todavía hay que probar ingreso y renovación en el navegador.

La URL de la aplicación:

```bash
az containerapp show --resource-group $RG --name $APP --query properties.configuration.ingress.fqdn --output tsv
```

Si algo falla:

```bash
az containerapp logs show --resource-group $RG --name $APP --follow
```

## Pendientes antes de que esto sirva en producción

Cosas que el pipeline no resuelve y que hoy impiden que la Api arranque con
`ASPNETCORE_ENVIRONMENT=Production`. La tabla completa de la configuración obligatoria, con el
archivo que exige cada clave, está en la [guía de despliegue](../guides/despliegue.md#configuración-obligatoria-en-production).

1. **SMTP.** `Email:Delivery` y `Email:Smtp:*`, con la contraseña como secret.
2. **Google.** `Authentication:Google:ClientSecret` (el `ClientId` ya viene en `appsettings.json`), y agregar la URL
   de producción a los redirect URIs autorizados en la consola de Google.
3. **`Authentication:Issuer`**, el origen público de la web (el que ve el navegador). Sin él la Api no arranca: de
   él salen el botón de las invitaciones por correo y los botones públicos de orientación de WhatsApp.
4. **`Authentication:Clients:Web:RedirectUris` y `Authentication:Clients:Web:PostLogoutRedirectUris`**, al menos
   una URI cada una, del origen público. Sin ellas la Api no arranca, y además alimentan el seed: en cada arranque
   el cliente `web` de OpenIddict queda exactamente como dice esta configuración, y lo que se haya cargado a mano en
   la base se borra.
5. **`Seed:AdminEmail`**, correo del administrador inicial. No impide arrancar; sin él, el registro abierto permite
   cuentas User pero no asigna Admin. El seed no crea cuentas: el dueño crea la suya en su primer ingreso y el seed
   le asegura Admin en cada arranque. `appsettings.json` configura `Registration:Mode=Open`; si elegís `InviteOnly`
   en una base sin cuentas previas, ese correo es la única excepción que puede crear una cuenta.
6. **Redis**, provisionado aparte, con `ConnectionStrings__cache` como secreto y `Caching__KeyPrefix`.
   Los comandos anteriores no lo crean ni cargan su conexión: sin ella la Api no arranca.

Los certificados de OpenIddict ya no están en esta lista: se resuelven con la carga en
base64 del paso 6. Tampoco las migraciones: las aplica el bundle del pipeline antes de
desplegar la imagen, y el seed corre solo en cada arranque.

## Probes (opcional)

La aplicación expone `/alive` y `/health` en todos los entornos. Container Apps funciona
sin configurarlos —da el contenedor por listo cuando el puerto acepta conexiones—, pero
declararlos mejora dos cosas: no recibir tráfico hasta que PostgreSQL y Redis estén alcanzables (y el check de WhatsApp pase, si está habilitado), y
reiniciar el contenedor si el proceso queda colgado.

Los probes no se configuran por parámetros de la CLI: hay que exportar el YAML de la
aplicación, agregarlos y volver a aplicarlo.

```bash
az containerapp show --resource-group $RG --name $APP --output yaml > app.yaml
```

Dentro de `properties.template.containers[0]`, agregar:

```yaml
probes:
  - type: Liveness
    httpGet: { path: /alive, port: 8080 }
    periodSeconds: 30
  - type: Readiness
    httpGet: { path: /health, port: 8080 }
    periodSeconds: 10
```

```bash
az containerapp update --resource-group $RG --name $APP --yaml app.yaml
```

`/alive` no consulta PostgreSQL ni Redis a propósito: si lo hiciera, una caída de esas dependencias marcaría el
proceso como muerto y Container Apps reiniciaría los contenedores en cadena sin arreglar
nada.
