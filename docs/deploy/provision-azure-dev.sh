#!/usr/bin/env bash
# Crea o completa el ambiente Azure dev de ArquitecturaBase y lo deja listo para que deploy.yml publique.
#
# Se puede volver a correr: lo que existe se deja como está y los secretos generados no se regeneran (regenerar un PFX
# o un anillo de claves cerraría las sesiones y dejaría ilegible lo cifrado). Corre en Azure Cloud Shell (Bash) o en
# Git Bash con Azure CLI y `az login` hecho. Receta, porqués y costos: docs/deploy/azure-dev.md.
#
# Datos de entrada, como variables de entorno:
#   AZURE_SUBSCRIPTION_ID    obligatoria.
#   GITHUB_OIDC_SUBJECT      obligatoria: el subject que GitHub emite para el environment dev, por ejemplo
#                            repo:<owner>@<owner-id>/<repo>@<repo-id>:environment:dev (azure-dev.md, etapa 4).
#   SMTP_USERNAME, SMTP_PASSWORD, SMTP_FROM_ADDRESS
#                            obligatorias la primera vez: sin SMTP la Api no arranca en Production. La contraseña es
#                            una contraseña de aplicación de Gmail.
#   SEED_ADMIN_EMAIL         el correo del administrador inicial (InviteOnly: es la única cuenta que se puede crear).
#   GOOGLE_CLIENT_ID, GOOGLE_CLIENT_SECRET
#                            opcionales: con ellas se prende el ingreso con Google.
#   GITHUB_REPOSITORY        opcional (owner/repo): con `gh` autenticado, crea el environment dev y sus variables.
#   LOCATION, RG y los nombres de abajo tienen valores por defecto; los que deben ser únicos en Azure llevan un sufijo
#   derivado de la suscripción, así una segunda corrida encuentra los mismos recursos.
set -euo pipefail

# Git Bash convierte en rutas de Windows los argumentos que empiezan con «/» (los IDs de recursos y el -subj de
# openssl). En Cloud Shell no hace nada.
export MSYS_NO_PATHCONV=1

: "${AZURE_SUBSCRIPTION_ID:?Falta AZURE_SUBSCRIPTION_ID}"
: "${GITHUB_OIDC_SUBJECT:?Falta GITHUB_OIDC_SUBJECT (ver docs/deploy/azure-dev.md, etapa 4)}"

suffix=$(printf %s "$AZURE_SUBSCRIPTION_ID" | sha256sum | cut -c1-6)
LOCATION=${LOCATION:-centralus}
RG=${RG:-rg-arquitecturabase-dev}
ACR=${ACR:-acrarqbasedev$suffix}
PG=${PG:-psql-arqbase-dev-$suffix}
PG_ADMIN=${PG_ADMIN:-pgadmin}
# PostgreSQL puede ir en otra región cercana si la del resto no tiene el tamaño B1ms para la suscripción (SkuNotAvailable).
PG_LOCATION=${PG_LOCATION:-$LOCATION}
ST=${ST:-starqbasedev$suffix}
ACA_ENV=${ACA_ENV:-cae-arquitecturabase-dev}
APP=${APP:-ca-arquitecturabase-dev}
REDIS_APP=${REDIS_APP:-ca-redis-dev}
DEPLOY_IDENTITY=${DEPLOY_IDENTITY:-id-github-arquitecturabase-dev}
CACHE_KEY_PREFIX=${CACHE_KEY_PREFIX:-arquitecturabase:dev}
REDIS_IMAGE_SOURCE=${REDIS_IMAGE_SOURCE:-docker.io/library/redis:8.6}

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
if [[ "$OSTYPE" == msys* || "$OSTYPE" == cygwin* ]]; then
  # En Windows, az y openssl son programas nativos: la carpeta temporal va como C:/..., y az termina cada línea con \r.
  tmp=$(cygpath -m "$tmp")
  az() { command az "$@" | tr -d '\r'; }
fi

log() { printf '\n==> %s\n' "$*"; }
exists() { "$@" --only-show-errors --output none >/dev/null 2>&1; }
# Hex y base64 no llevan caracteres que rompan una cadena de conexión ni la línea de comandos de az.cmd.
random_hex() { openssl rand -hex "$1"; }
random_b64() { openssl rand -base64 "$1" | tr -d '\n'; }
# PostgreSQL pide tres clases de caracteres: minúsculas, mayúsculas y dígitos alcanzan.
random_pg_password() { printf '%s%s' "$(random_hex 16)" "$(random_hex 8 | tr 'a-f' 'A-F')"; }

az account set --subscription "$AZURE_SUBSCRIPTION_ID"
for namespace in Microsoft.App Microsoft.OperationalInsights Microsoft.ContainerRegistry Microsoft.DBforPostgreSQL \
  Microsoft.Storage Microsoft.ManagedIdentity; do
  az provider register --namespace "$namespace" --wait --only-show-errors
done

log "Grupo de recursos $RG"
# Si ya existe se reusa aunque esté en otra región: la del grupo es sólo dónde guarda sus datos de gestión.
if ! exists az group show --name "$RG"; then
  az group create --name "$RG" --location "$LOCATION" --tags environment=dev --output none
fi

log "Registry $ACR (Basic, sin usuario administrador)"
if ! exists az acr show --resource-group "$RG" --name "$ACR"; then
  az acr create --resource-group "$RG" --name "$ACR" --location "$LOCATION" --sku Basic --admin-enabled false --output none
fi
ACR_ID=$(az acr show --resource-group "$RG" --name "$ACR" --query id --output tsv)
# Redis sale del registry propio: Container Apps no depende del límite de descargas anónimas de Docker Hub.
redis_image="$ACR.azurecr.io/redis:${REDIS_IMAGE_SOURCE##*:}"
if ! exists az acr repository show --name "$ACR" --image "redis:${REDIS_IMAGE_SOURCE##*:}"; then
  az acr import --name "$ACR" --source "$REDIS_IMAGE_SOURCE" --image "redis:${REDIS_IMAGE_SOURCE##*:}" --output none
fi

log "Entorno de Container Apps $ACA_ENV"
if ! exists az containerapp env show --resource-group "$RG" --name "$ACA_ENV"; then
  # Log Analytics explícito: la consola JSON de la Api llega a ContainerAppConsoleLogs_CL (despliegue.md, logs).
  az containerapp env create --resource-group "$RG" --name "$ACA_ENV" --location "$LOCATION" \
    --logs-destination log-analytics --output none
fi
ENV_ID=$(az containerapp env show --resource-group "$RG" --name "$ACA_ENV" --query id --output tsv)

# Las Container Apps se crean con un PUT a ARM en una versión estable de la API: `az containerapp create --yaml` arma un
# cuerpo que la API rechaza («could not be converted to System.Boolean»), con la extensión y sin ella (az 2.93).
CONTAINER_APPS_API=2024-03-01
apps_url="https://management.azure.com/subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/$RG/providers/Microsoft.App/containerApps"
create_app() { # $1 nombre, $2 archivo JSON
  az rest --method PUT --url "$apps_url/$1?api-version=$CONTAINER_APPS_API" --body "@$2" --output none
  local state=""
  for _ in $(seq 1 60); do
    state=$(az containerapp show --resource-group "$RG" --name "$1" --query properties.provisioningState --output tsv)
    [[ "$state" == Succeeded ]] && return 0
    [[ "$state" == Failed ]] && break
    sleep 5
  done
  echo "La Container App $1 quedó en «$state»." >&2
  return 1
}

# Redis interno: ingress TCP sólo dentro del entorno, sin puerto en Internet. Guarda valores reconstruibles, así que no
# persiste a disco; «noeviction» porque una lease de coordinación no se puede expulsar por presión de memoria, y el
# caché, que siempre tiene TTL, no llega al tope en dev.
log "Redis interno $REDIS_APP"
if ! exists az containerapp show --resource-group "$RG" --name "$REDIS_APP"; then
  cat > "$tmp/redis.json" <<JSON
{"location": "$LOCATION", "identity": {"type": "SystemAssigned"}, "properties": {
  "managedEnvironmentId": "$ENV_ID",
  "configuration": {
    "activeRevisionsMode": "Single",
    "secrets": [{"name": "redis-password", "value": "$(random_hex 32)"}],
    "ingress": {"external": false, "transport": "tcp", "targetPort": 6379, "exposedPort": 6379}
  },
  "template": {
    "containers": [{"name": "redis", "image": "mcr.microsoft.com/k8se/quickstart:latest", "resources": {"cpu": 0.25, "memory": "0.5Gi"}}],
    "scale": {"minReplicas": 1, "maxReplicas": 1}
  }
}}
JSON
  create_app "$REDIS_APP" "$tmp/redis.json"
  rm -f "$tmp/redis.json"
fi
redis_identity=$(az containerapp show --resource-group "$RG" --name "$REDIS_APP" --query identity.principalId --output tsv)
az role assignment create --assignee-object-id "$redis_identity" --assignee-principal-type ServicePrincipal \
  --role AcrPull --scope "$ACR_ID" --output none
if [[ "$(az containerapp show --resource-group "$RG" --name "$REDIS_APP" --query 'properties.template.containers[0].image' --output tsv)" != "$redis_image" ]]; then
  az containerapp registry set --resource-group "$RG" --name "$REDIS_APP" --server "$ACR.azurecr.io" --identity system --output none
  # El contenedor real va en un PATCH, que en Container Apps es JSON Merge Patch: reemplaza el template y deja la
  # configuración (el secreto y el registry). `az containerapp update --args` no acepta valores que empiezan con «-».
  cat > "$tmp/redis-template.json" <<JSON
{"properties": {"template": {
  "containers": [{
    "name": "redis",
    "image": "$redis_image",
    "command": ["/bin/sh"],
    "args": ["-c", "exec redis-server --requirepass \"\$REDIS_PASSWORD\" --maxmemory 256mb --maxmemory-policy noeviction --save '' --appendonly no"],
    "env": [{"name": "REDIS_PASSWORD", "secretRef": "redis-password"}],
    "resources": {"cpu": 0.25, "memory": "0.5Gi"}
  }],
  "scale": {"minReplicas": 1, "maxReplicas": 1}
}}}
JSON
  az rest --method PATCH --url "$apps_url/$REDIS_APP?api-version=$CONTAINER_APPS_API" \
    --body "@$tmp/redis-template.json" --output none
fi

log "Container App $APP (Api y front)"
if ! exists az containerapp show --resource-group "$RG" --name "$APP"; then
  # La imagen de ejemplo no pasa los probes: el primer despliegue la reemplaza por la Api (azure-setup.md#probes).
  cat > "$tmp/app.json" <<JSON
{"location": "$LOCATION", "identity": {"type": "SystemAssigned"}, "properties": {
  "managedEnvironmentId": "$ENV_ID",
  "configuration": {
    "activeRevisionsMode": "Single",
    "ingress": {"external": true, "transport": "auto", "targetPort": 8080, "allowInsecure": false}
  },
  "template": {
    "terminationGracePeriodSeconds": 60,
    "containers": [{
      "name": "api",
      "image": "mcr.microsoft.com/k8se/quickstart:latest",
      "resources": {"cpu": 0.5, "memory": "1Gi"},
      "probes": [
        {"type": "Startup", "httpGet": {"path": "/alive", "port": 8080}, "initialDelaySeconds": 5, "periodSeconds": 10, "timeoutSeconds": 3, "failureThreshold": 10},
        {"type": "Liveness", "httpGet": {"path": "/alive", "port": 8080}, "periodSeconds": 30, "timeoutSeconds": 5},
        {"type": "Readiness", "httpGet": {"path": "/health", "port": 8080}, "periodSeconds": 10, "timeoutSeconds": 5}
      ]
    }],
    "scale": {"minReplicas": 1, "maxReplicas": 1}
  }
}}
JSON
  create_app "$APP" "$tmp/app.json"
fi
APP_IDENTITY=$(az containerapp show --resource-group "$RG" --name "$APP" --query identity.principalId --output tsv)
FQDN=$(az containerapp show --resource-group "$RG" --name "$APP" --query properties.configuration.ingress.fqdn --output tsv)
ORIGIN="https://$FQDN"
az role assignment create --assignee-object-id "$APP_IDENTITY" --assignee-principal-type ServicePrincipal \
  --role AcrPull --scope "$ACR_ID" --output none
az containerapp registry set --resource-group "$RG" --name "$APP" --server "$ACR.azurecr.io" --identity system --output none

log "Archivos privados en $ST"
if ! exists az storage account show --resource-group "$RG" --name "$ST"; then
  az storage account create --resource-group "$RG" --name "$ST" --location "$LOCATION" --kind StorageV2 \
    --sku Standard_LRS --allow-blob-public-access false --allow-shared-key-access false --min-tls-version TLS1_2 \
    --output none
  az storage account blob-service-properties update --resource-group "$RG" --account-name "$ST" \
    --enable-delete-retention true --delete-retention-days 14 --enable-container-delete-retention true \
    --container-delete-retention-days 14 --enable-versioning true --enable-change-feed true --change-feed-days 14 \
    --enable-restore-policy true --restore-days 13 --output none
fi
if ! exists az storage container-rm show --resource-group "$RG" --storage-account "$ST" --name files-content; then
  az storage container-rm create --resource-group "$RG" --storage-account "$ST" --name files-content \
    --public-access off --output none
fi
ST_ID=$(az storage account show --resource-group "$RG" --name "$ST" --query id --output tsv)
az role assignment create --assignee-object-id "$APP_IDENTITY" --assignee-principal-type ServicePrincipal \
  --role "Storage Blob Data Contributor" --scope "$ST_ID/blobServices/default/containers/files-content" --output none

# Los secretos de la Api viven en la Container App: se crean sólo los que faltan y nunca se muestran. deploy.yml lee
# de ahí la conexión de la base para las migraciones.
existing_secrets=" $(az containerapp secret list --resource-group "$RG" --name "$APP" --query '[].name' --output tsv | tr '\n' ' ') "
has_secret() { [[ "$existing_secrets" == *" $1 "* ]]; }
new_secrets=()

log "PostgreSQL $PG (B1ms, 32 GB, versión 18) y base appdb"
if ! has_secret db-connection; then
  pg_password=$(random_pg_password)
  if exists az postgres flexible-server show --resource-group "$RG" --name "$PG"; then
    # El servidor existe pero su contraseña no quedó guardada (una corrida cortada): se cambia por una nueva.
    az postgres flexible-server update --resource-group "$RG" --name "$PG" --admin-password "$pg_password" --output none
  else
    az postgres flexible-server create --resource-group "$RG" --name "$PG" --location "$PG_LOCATION" \
      --admin-user "$PG_ADMIN" --admin-password "$pg_password" --sku-name Standard_B1ms --tier Burstable \
      --version 18 --storage-size 32 --public-access None --backup-retention 7 --geo-redundant-backup Disabled \
      --yes --output none
  fi
  # Se guarda enseguida: si la corrida se corta después, la próxima no vuelve a cambiar la contraseña.
  # Maximum Pool Size: el B1ms admite unas 50 conexiones; 15 por réplica deja lugar a dos revisiones y al bundle.
  az containerapp secret set --resource-group "$RG" --name "$APP" --output none --secrets \
    "db-connection=Host=$PG.postgres.database.azure.com;Port=5432;Database=appdb;Username=$PG_ADMIN;Password=$pg_password;SSL Mode=Require;Maximum Pool Size=15"
  unset pg_password
fi
# az 2.93 renombró argumentos de `flexible-server db` y `firewall-rule`: el servidor pasó a --server-name y la base o la
# regla, a --name (antes --database-name y --rule-name). Se arma la forma que entiende la CLI instalada.
if az postgres flexible-server firewall-rule create --help 2>/dev/null | grep -q -- '--rule-name'; then
  database_args=(--server-name "$PG" --database-name appdb)
  rule_args=(--name "$PG" --rule-name allow-azure-services)
else
  database_args=(--server-name "$PG" --name appdb)
  rule_args=(--server-name "$PG" --name allow-azure-services)
fi
# Desde az 2.93, `--public-access None` crea el servidor con el acceso público apagado, y sin él no hay reglas de
# firewall. Prenderlo no abre nada: sólo entran las IP de las reglas.
if [[ "$(az postgres flexible-server show --resource-group "$RG" --name "$PG" --query network.publicNetworkAccess --output tsv)" != Enabled ]]; then
  az postgres flexible-server update --resource-group "$RG" --name "$PG" --public-access Enabled --yes --output none
fi
if ! exists az postgres flexible-server db show --resource-group "$RG" "${database_args[@]}"; then
  az postgres flexible-server db create --resource-group "$RG" "${database_args[@]}" --output none
fi
# Permite conexiones desde Azure (también de otras suscripciones; siguen pidiendo usuario y contraseña). Es la
# simplificación de dev de azure-setup.md: antes de datos reales, red privada.
if ! exists az postgres flexible-server firewall-rule show --resource-group "$RG" "${rule_args[@]}"; then
  az postgres flexible-server firewall-rule create --resource-group "$RG" "${rule_args[@]}" \
    --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 --output none
fi

log "Secretos de la Api"
if ! has_secret redis-connection; then
  redis_password=$(az containerapp secret show --resource-group "$RG" --name "$REDIS_APP" --secret-name redis-password --query value --output tsv)
  new_secrets+=("redis-connection=$REDIS_APP:6379,password=$redis_password,abortConnect=false")
  unset redis_password
fi
has_secret login-code-hash-key || new_secrets+=("login-code-hash-key=$(random_b64 48)")
# Tres PFX autofirmados: firma y cifrado de OpenIddict, y el que cifra las claves de Data Protection. Comparten una
# contraseña, que se genera con ellos. Respaldo: `az containerapp secret show`, guardado aparte de la base.
if ! has_secret signing-pfx; then
  pfx_password=$(random_hex 24)
  make_pfx() {
    openssl req -x509 -newkey rsa:2048 -nodes -days 1825 -subj "/CN=ArquitecturaBase $1" -addext "keyUsage=$2" \
      -keyout "$tmp/key.pem" -out "$tmp/cert.pem" 2>/dev/null
    openssl pkcs12 -export -inkey "$tmp/key.pem" -in "$tmp/cert.pem" -passout "pass:$pfx_password" -out "$tmp/cert.pfx"
    base64 -w0 "$tmp/cert.pfx"
    rm -f "$tmp/key.pem" "$tmp/cert.pem" "$tmp/cert.pfx"
  }
  new_secrets+=("pfx-password=$pfx_password")
  new_secrets+=("signing-pfx=$(make_pfx Signing digitalSignature)")
  new_secrets+=("encryption-pfx=$(make_pfx Encryption keyEncipherment)")
  new_secrets+=("dataprotection-pfx=$(make_pfx DataProtection keyEncipherment,digitalSignature)")
  unset pfx_password
fi
# Anillos de IA y ARCA: un ID y una clave propios de esta instalación, que no se comparten con otra (configurar-ia.md).
has_secret ai-installation-id || new_secrets+=("ai-installation-id=$(random_hex 16)")
has_secret ai-key-v1 || new_secrets+=("ai-key-v1=$(random_b64 32)")
has_secret arca-installation-id || new_secrets+=("arca-installation-id=$(random_hex 16)")
has_secret arca-key-v1 || new_secrets+=("arca-key-v1=$(random_b64 32)")
if [[ -n "${SMTP_PASSWORD:-}" ]]; then
  new_secrets+=("smtp-password=$SMTP_PASSWORD")
elif ! has_secret smtp-password; then
  echo "Falta SMTP_PASSWORD: sin SMTP la Api no arranca en Production." >&2
  exit 1
fi
[[ -n "${GOOGLE_CLIENT_SECRET:-}" ]] && new_secrets+=("google-client-secret=$GOOGLE_CLIENT_SECRET")
if ((${#new_secrets[@]})); then
  az containerapp secret set --resource-group "$RG" --name "$APP" --secrets "${new_secrets[@]}" --output none
fi
unset new_secrets

log "Configuración de la Api"
settings=(
  ASPNETCORE_ENVIRONMENT=Production
  ConnectionStrings__appdb=secretref:db-connection
  ConnectionStrings__cache=secretref:redis-connection
  "Caching__KeyPrefix=$CACHE_KEY_PREFIX"
  ForwardedHeaders__TrustAll=true
  "Authentication__Issuer=$ORIGIN/"
  "Authentication__Clients__Web__RedirectUris__0=$ORIGIN/auth/callback"
  "Authentication__Clients__Web__RedirectUris__1=$ORIGIN/silent-renew.html"
  "Authentication__Clients__Web__PostLogoutRedirectUris__0=$ORIGIN/login"
  Authentication__LoginCode__HashKey=secretref:login-code-hash-key
  Authentication__Certificates__Signing__Base64=secretref:signing-pfx
  Authentication__Certificates__Signing__Password=secretref:pfx-password
  Authentication__Certificates__Encryption__Base64=secretref:encryption-pfx
  Authentication__Certificates__Encryption__Password=secretref:pfx-password
  Authentication__Certificates__DataProtection__Base64=secretref:dataprotection-pfx
  Authentication__Certificates__DataProtection__Password=secretref:pfx-password
  ArtificialIntelligence__SecretProtection__InstallationId=secretref:ai-installation-id
  ArtificialIntelligence__SecretProtection__ActiveKeyId=v1
  ArtificialIntelligence__SecretProtection__Keys__v1=secretref:ai-key-v1
  Arca__SecretProtection__InstallationId=secretref:arca-installation-id
  Arca__SecretProtection__ActiveKeyId=v1
  Arca__SecretProtection__Keys__v1=secretref:arca-key-v1
  FileStorage__Enabled=true
  FileStorage__CredentialMode=ManagedIdentity
  "FileStorage__ServiceUri=https://$ST.blob.core.windows.net/"
  FileStorage__ContentContainer=files-content
  Email__Smtp__Password=secretref:smtp-password
)
[[ -n "${SMTP_USERNAME:-}" ]] && settings+=("Email__Smtp__UserName=$SMTP_USERNAME")
[[ -n "${SMTP_FROM_ADDRESS:-}" ]] && settings+=("Email__Smtp__FromAddress=$SMTP_FROM_ADDRESS")
[[ -n "${SEED_ADMIN_EMAIL:-}" ]] && settings+=("Seed__AdminEmail=$SEED_ADMIN_EMAIL")
if [[ -n "${GOOGLE_CLIENT_ID:-}" ]]; then
  settings+=("Authentication__Google__ClientId=$GOOGLE_CLIENT_ID" Authentication__Google__ClientSecret=secretref:google-client-secret)
fi
az containerapp update --resource-group "$RG" --name "$APP" --set-env-vars "${settings[@]}" --output none

log "Identidad de GitHub $DEPLOY_IDENTITY (OIDC, sin contraseña)"
if ! exists az identity show --resource-group "$RG" --name "$DEPLOY_IDENTITY"; then
  az identity create --resource-group "$RG" --name "$DEPLOY_IDENTITY" --location "$LOCATION" --output none
fi
DEPLOY_CLIENT_ID=$(az identity show --resource-group "$RG" --name "$DEPLOY_IDENTITY" --query clientId --output tsv)
DEPLOY_PRINCIPAL=$(az identity show --resource-group "$RG" --name "$DEPLOY_IDENTITY" --query principalId --output tsv)
if ! exists az identity federated-credential show --resource-group "$RG" --identity-name "$DEPLOY_IDENTITY" --name github-dev; then
  az identity federated-credential create --resource-group "$RG" --identity-name "$DEPLOY_IDENTITY" --name github-dev \
    --issuer https://token.actions.githubusercontent.com --subject "$GITHUB_OIDC_SUBJECT" \
    --audiences api://AzureADTokenExchange --output none
fi
RG_ID=$(az group show --name "$RG" --query id --output tsv)
az role assignment create --assignee-object-id "$DEPLOY_PRINCIPAL" --assignee-principal-type ServicePrincipal \
  --role Contributor --scope "$RG_ID" --output none
az role assignment create --assignee-object-id "$DEPLOY_PRINCIPAL" --assignee-principal-type ServicePrincipal \
  --role AcrPush --scope "$ACR_ID" --output none

TENANT_ID=$(az account show --query tenantId --output tsv)
github_variables=(
  "AZURE_CLIENT_ID=$DEPLOY_CLIENT_ID"
  "AZURE_TENANT_ID=$TENANT_ID"
  "AZURE_SUBSCRIPTION_ID=$AZURE_SUBSCRIPTION_ID"
  "AZURE_RESOURCE_GROUP=$RG"
  "AZURE_REGISTRY=$ACR"
  "AZURE_CONTAINER_APP=$APP"
  "AZURE_POSTGRES_SERVER=$PG"
)
if [[ -n "${GITHUB_REPOSITORY:-}" ]] && command -v gh >/dev/null; then
  log "Environment dev de $GITHUB_REPOSITORY"
  gh api --method PUT "repos/$GITHUB_REPOSITORY/environments/dev" --input - >/dev/null <<'JSON'
{"deployment_branch_policy": {"protected_branches": false, "custom_branch_policies": true}}
JSON
  if ! gh api "repos/$GITHUB_REPOSITORY/environments/dev/deployment-branch-policies" --jq '.branch_policies[].name' | grep -qx main; then
    gh api --method POST "repos/$GITHUB_REPOSITORY/environments/dev/deployment-branch-policies" -f name=main -f type=branch >/dev/null
  fi
  for variable in "${github_variables[@]}"; do
    gh variable set "${variable%%=*}" --env dev --body "${variable#*=}" --repo "$GITHUB_REPOSITORY"
  done
fi

log "Listo"
echo "URL: $ORIGIN"
echo "Variables del environment dev de GitHub:"
printf '  %s\n' "${github_variables[@]}"
echo "Después: DEV_DEPLOY_ENABLED=true como variable de repositorio y correr deploy.yml en main."
