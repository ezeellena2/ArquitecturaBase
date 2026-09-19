# Desplegar a Azure Container Apps

Pasos para crear, una sola vez, la infraestructura que usa `.github/workflows/deploy.yml`.
El pipeline no crea recursos: solo sube una imagen nueva y actualiza la aplicación.

> **Costo.** Container Apps tiene una cuota gratuita mensual, pero PostgreSQL Flexible
> Server y el registry se facturan desde el primer día. El SKU más chico de Postgres
> (`Standard_B1ms`) ronda los 15 USD/mes. Si es solo para probar, borrá el grupo de
> recursos al terminar: `az group delete --name rg-arquitecturabase --yes`.

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
agregale algo al final y actualizá también `AZURE_REGISTRY` en el workflow.

```bash
RG=rg-arquitecturabase
LOCATION=brazilsouth
ACR=acrarquitecturabase
ACA_ENV=cae-arquitecturabase
APP=ca-arquitecturabase-api
PG=psql-arquitecturabase
PG_ADMIN=pgadmin
PG_PASSWORD="<poné-una-contraseña-fuerte-acá>"
```

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
az postgres flexible-server create --resource-group $RG --name $PG --location $LOCATION --admin-user $PG_ADMIN --admin-password "$PG_PASSWORD" --sku-name Standard_B1ms --tier Burstable --version 18 --storage-size 32 --public-access 0.0.0.0
```

`--public-access 0.0.0.0` crea el servidor sin abrirlo a internet; el acceso se habilita
por regla de firewall. El pipeline abre una regla temporal para migrar y la borra al
terminar.

Creá la base:

```bash
az postgres flexible-server db create --resource-group $RG --server-name $PG --database-name appdb
```

Permitir que los servicios de Azure (entre ellos tu Container App) alcancen el servidor:

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
az containerapp create --resource-group $RG --name $APP --environment $ACA_ENV --image mcr.microsoft.com/k8se/quickstart:latest --target-port 8080 --ingress external --system-assigned --min-replicas 1 --max-replicas 3
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
az containerapp update --resource-group $RG --name $APP --set-env-vars ASPNETCORE_ENVIRONMENT=Production ConnectionStrings__appdb=secretref:db-connection Authentication__LoginCode__HashKey=secretref:login-code-hash-key
```

> El doble guion bajo es cómo .NET lee secciones anidadas desde variables de entorno:
> `ConnectionStrings__appdb` es `ConnectionStrings:appdb`.

Faltan las de Google y SMTP: ver *Pendientes* al final.

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

Ahora las credenciales federadas. **Hacen falta dos**, porque el token que emite GitHub
describe distinto a cada job: el de `build` dice de qué rama viene, y el de `deploy` dice
a qué environment apunta. Si falta la segunda, el deploy falla con un error de
autenticación que no explica nada.

```bash
az ad app federated-credential create --id $APP_ID --parameters '{"name":"github-main","issuer":"https://token.actions.githubusercontent.com","subject":"repo:ezeellena2/ArquitecturaBase:ref:refs/heads/main","audiences":["api://AzureADTokenExchange"]}'
```

```bash
az ad app federated-credential create --id $APP_ID --parameters '{"name":"github-production","issuer":"https://token.actions.githubusercontent.com","subject":"repo:ezeellena2/ArquitecturaBase:environment:production","audiences":["api://AzureADTokenExchange"]}'
```

## 8. Secrets en GitHub

En el repo: *Settings > Secrets and variables > Actions > New repository secret*.

| Secret | De dónde sale |
|---|---|
| `AZURE_CLIENT_ID` | `echo $APP_ID` |
| `AZURE_TENANT_ID` | `az account show --query tenantId --output tsv` |
| `AZURE_SUBSCRIPTION_ID` | `az account show --query id --output tsv` |
| `DB_CONNECTION_STRING` | el `$CONN` del paso 6 |

Los tres primeros no son secretos en sentido estricto: son identificadores. El que sí
importa es el último.

Para exigir aprobación manual antes de cada deploy: *Settings > Environments >
production > Required reviewers*.

## 9. Probar

Hacé push a `main` y seguí la corrida en la pestaña *Actions* del repo.

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
`ASPNETCORE_ENVIRONMENT=Production`:

1. **Certificados de OpenIddict.** `OpenIddictRegistration` fuera de Development exige dos
   PFX (firma y cifrado) con ruta y contraseña en
   `Authentication:Certificates:{Signing,Encryption}`. Un archivo en disco no encaja bien
   con un contenedor: hay que montar un Azure File Share o cambiar la carga para aceptar
   el certificado en base64 desde configuración. **Sin esto la aplicación no inicia.**
2. **Health probes.** `MapDefaultEndpoints` expone `/health` y `/alive` solo en
   Development. Si configurás probes en Container Apps, hay que exponerlos también en
   producción.
3. **SMTP.** `Email:Delivery` y `Email:Smtp:*`, con la contraseña como secret.
4. **Google.** `Authentication:Google:ClientId` y `ClientSecret`, y agregar la URL de
   producción a los redirect URIs autorizados en la consola de Google.
5. **`Seed:AdminEmail`**, si querés que la cuenta administradora se cree sola.
