# Azure Blob Storage real desde la aplicación local

Estado, 2026-10-15: el adaptador privado Azure Blob y el recorrido de fotos/documentos están implementados y probados con Azurite. La cuenta de Azure de desarrollo y su verificación real siguen pendientes. La [Etapa 2B](../plans/formularios/etapa-2b-fotos-y-documentos.md) delimita este recorrido; los demás consumidores del [plan de archivos](../plans/2026-10-02-almacenamiento-de-archivos.md) siguen pendientes. Proveedor: [ADR 0012](../decisions/0012-azure-blob-como-primer-proveedor-de-archivos.md).

## 1. Qué corre dónde

```text
PC: navegador → Vite/React → API .NET local
                              ├─ PostgreSQL local
                              ├─ Redis local
                              └─ HTTPS saliente → Azure Blob Storage real
```

La aplicación no necesita estar publicada. La API hace la conexión saliente al storage y envía/recibe bytes. No se abre un túnel para Azure, no se entrega una connection string al front y no se cambia el origen del navegador. `aspire run` inicia Azurite por defecto, conserva su volumen y provisiona el contenedor privado `files-content` antes de la API. Las pruebas de integración también usan Azurite, sin depender de credenciales personales. Para usar Azure real, desactivar `Azurite:Enabled` en la configuración del AppHost y configurar la API como en la sección 5. No iniciar ambos modos para la misma prueba.

## 2. Preparar los recursos desde el portal

Usar una suscripción activa y crear una cuenta dedicada a desarrollo en [Azure Portal](https://portal.azure.com/). Se propone esta configuración inicial, ajustable al entorno:

| Campo | Propuesta para desarrollo |
| --- | --- |
| Grupo de recursos | Uno de desarrollo, por ejemplo `rg-arquitecturabase-dev` |
| Nombre de cuenta | Globalmente único, 3–24 caracteres en minúsculas/números; ejemplo ilustrativo `starquitecturabasedev123` |
| Tipo / rendimiento | General-purpose v2 (`StorageV2`), Standard, block blobs |
| Región | La elegida para el proyecto; anotar la selección |
| Redundancia / acceso | LRS y Hot como punto de partida para pruebas |
| HTTPS / anónimo | Transferencia segura habilitada; acceso anónimo a blobs deshabilitado |
| Namespace jerárquico / SFTP | Deshabilitados para este caso |

Esto crea únicamente storage: no requiere Container Apps, PostgreSQL administrado ni publicar la API. LRS/Hot son una recomendación de desarrollo; la disponibilidad y recuperación productivas se deciden aparte. La cuenta real tiene consumo facturable según región/uso.

Dentro de la cuenta, abrir **Data storage → Containers** y crear `files-content`, con acceso privado: originales y miniaturas. La Etapa 2B de formularios no usa staging; un contenedor de staging se agrega recién cuando haya cargas desde el navegador. No es un contenedor por usuario ni por archivo. Las claves de respuestas las genera el servidor por recibo, pregunta, posición y hash; un reintento reemplaza el mismo objeto. No configurar lifecycle que borre respuestas por antigüedad: sus archivos duran mientras exista la respuesta y no siguen la retención del cuerpo de WhatsApp.

Fuentes: [crear cuenta](https://learn.microsoft.com/en-us/azure/storage/common/storage-account-create) y [administrar contenedores](https://learn.microsoft.com/en-us/azure/storage/blobs/blob-containers-portal).

## 3. Dar acceso a la sesión de desarrollo

En IAM, asignar a tu usuario de Microsoft Entra **Storage Blob Data Contributor** sobre el contenedor privado `files-content`. Esa asignación necesita una identidad con permiso para administrar roles. Ser Owner/Contributor de recursos no implica automáticamente poder leer/escribir los datos de blobs con Entra.

Para navegar los contenedores en el portal con Entra puede hacer falta además Reader del recurso o un permiso de administración equivalente. Los roles pueden tardar en propagarse; no cambiar a acceso anónimo para resolver un 403. [Permisos de datos](https://learn.microsoft.com/en-us/azure/storage/blobs/assign-azure-role-data-access).

Para la primera prueba, usar Azure CLI en tu PC; en este entorno se encontró `az.cmd` instalado. Iniciar sesión en el tenant correspondiente:

```powershell
az login --tenant "<tenant-id>"
az account set --subscription "<subscription-id>"
az account show --query "{subscription:name,tenant:tenantId}" --output table
```

Reemplazar los placeholders. Login es interactivo. No pegar tokens, claves de cuenta ni connection strings en el chat. Tenant, nombre de cuenta, contenedores y endpoint son configuración que se puede compartir.

**Red:** si la cuenta usa firewall, permitir la IP pública de salida de tu PC. Un endpoint HTTPS accesible en red y un blob público anónimo son cosas distintas: el contenedor sigue privado y exige autenticación. Si se usa exclusivamente private endpoint, la PC necesita conectividad privada y resolución DNS correctas (por ejemplo VPN). [Firewall de Storage](https://learn.microsoft.com/en-us/azure/storage/common/storage-network-security).

## 4. Primera prueba de bytes contra Azure

Ejecutar estos bloques en la misma PowerShell, después de crear el contenedor e iniciar sesión. La prueba usa un blob con GUID bajo `manual-tests/`; no cambia archivos existentes ni crea recursos. Conserva el blob para poder verlo en el portal. Es una prueba de conexión e integridad, no del catálogo PostgreSQL ni del adaptador .NET.

### Preparar un archivo propio

```powershell
$blobAccountName = "<nombre-real-de-la-cuenta>"
$blobContainerName = "files-content"
$blobRunId = [Guid]::NewGuid().ToString("N")
$blobObjectName = "manual-tests/$blobRunId/sample.txt"
$blobInputPath = Join-Path ([IO.Path]::GetTempPath()) "blob-input-$blobRunId.txt"
$blobOutputPath = Join-Path ([IO.Path]::GetTempPath()) "blob-output-$blobRunId.txt"
[IO.File]::WriteAllText($blobInputPath, "ArquitecturaBase Azure Blob smoke test: $blobRunId")
```

### Subir sin reemplazar

```powershell
az storage blob upload `
  --account-name $blobAccountName `
  --container-name $blobContainerName `
  --name $blobObjectName `
  --file $blobInputPath `
  --type block `
  --content-type "text/plain; charset=utf-8" `
  --auth-mode login `
  --overwrite false `
  --only-show-errors `
  --output none
if ($LASTEXITCODE -ne 0) { throw "Blob upload failed." }
```

### Descargar y comparar SHA-256

```powershell
az storage blob download `
  --account-name $blobAccountName `
  --container-name $blobContainerName `
  --name $blobObjectName `
  --file $blobOutputPath `
  --auth-mode login `
  --only-show-errors `
  --output none
if ($LASTEXITCODE -ne 0) { throw "Blob download failed." }

$blobInputHash = (Get-FileHash -LiteralPath $blobInputPath -Algorithm SHA256).Hash
$blobOutputHash = (Get-FileHash -LiteralPath $blobOutputPath -Algorithm SHA256).Hash
if ($blobInputHash -ne $blobOutputHash) { throw "The downloaded content does not match the uploaded content." }
Write-Output "Upload/download verified: $blobContainerName/$blobObjectName"
```

En el portal o Storage Explorer, abrir `files-content` y buscar **esa clave exacta**. Su presencia y la comparación de hash comprueban que los bytes llegaron y se recuperaron intactos. Se puede repetir el upload original para verificar que el mismo block blob no se reemplaza; se espera un fallo al existir.

Al terminar de inspeccionar, la limpieza opcional borra solamente ese blob y los dos archivos temporales generados:

```powershell
az storage blob delete `
  --account-name $blobAccountName `
  --container-name $blobContainerName `
  --name $blobObjectName `
  --auth-mode login `
  --only-show-errors `
  --output none
if ($LASTEXITCODE -ne 0) { throw "Test blob cleanup failed." }
Remove-Item -LiteralPath $blobInputPath, $blobOutputPath
```

No borrar el contenedor, la cuenta ni el grupo de recursos para limpiar una prueba. Fuente de los comandos: [Azure CLI Blob](https://learn.microsoft.com/en-us/cli/azure/storage/blob?view=azure-cli-latest).

## 5. Configuración de la API

Con `Azurite:Enabled=false` en el AppHost, configurar el proyecto **Api** mediante user-secrets o variables de entorno:

```json
{
  "FileStorage": {
    "Enabled": true,
    "ContentContainer": "files-content",
    "CredentialMode": "AzureCli",
    "ServiceUri": "https://<cuenta>.blob.core.windows.net",
    "TenantId": "<tenant-id>"
  },
  "BusinessForms": { "MaxFileSizeMiB": 25 }
}
```

El endpoint se copia de la cuenta, sin SAS ni query. `MaxFileSizeMiB` es el único tope por archivo de la instalación (1–25 MiB); la publicación lo sella para esa versión. No hay tamaño por pregunta.

Credenciales explícitas, sin fallback automático:

- `AzureCli`: sesión de `az login` del tenant elegido.
- `VisualStudio`: sesión del IDE con el mismo permiso de datos.
- `ManagedIdentity`: identidad de la API; `ManagedIdentityClientId` opcional para identidad asignada por usuario.
- `ConnectionString`: sólo en user-secrets/entorno; es el modo que Aspire inyecta para Azurite. No imprimir la cadena ni ejecutar `user-secrets list` como diagnóstico.

La API valida la configuración y no crea contenedores al arrancar. El contenedor cloud se provisiona antes; sólo el emulador local lo provisiona desde Aspire. El navegador consume las rutas autorizadas de la API, nunca Azure ni SAS. Al terminar la app local: `aspire stop`.

## 6. Verificación de la aplicación

La integración usa PostgreSQL, Redis y Azurite reales; simula únicamente Meta. Comprueba la captura atómica de foto/PDF, recarga, miniatura, descarga y permisos, integridad/tipo/tamaño, reintentos acotados, limpieza de huérfanos y conservación tras la retención del chat. Azure Blob se prueba además con sobreescritura determinística y borrado idempotente.

Cuando exista la cuenta de desarrollo, ejecutar explícitamente el adaptador contra ella y comprobar original/miniatura/descarga con el recorrido de la aplicación. La prueba CLI anterior ayuda a diagnosticar acceso e integridad, pero no sustituye esa comprobación. No usar producción ni depender de la sesión cloud en la suite normal. Versiones generales, staging, carga web y fondo del chat quedan fuera de esta entrega.

## 7. Diagnóstico y evidencia

| Síntoma | Revisar |
| --- | --- |
| 401 / no se encuentra sesión | Login, tenant y modo de credencial elegido |
| 403 | Rol de datos, alcance, propagación y firewall. Guardar el código Azure sanitizado, nunca tokens |
| Contenedor inexistente | Nombre configurado y creación previa; el arranque no lo crea |
| Timeout / DNS | Acceso saliente, endpoint, firewall o conectividad privada |
| Se ve en portal pero no hay acceso SDK | Portal y SDK pueden estar usando mecanismos o identidades distintos |
| Upload correcto pero fondo sin cambiar | La prueba de storage no implementa ni confirma la selección funcional |

Pendientes al 2026-10-15: crear la cuenta de desarrollo y su contenedor privado, configurar la sesión y verificar el adaptador y el recorrido contra Azure real. Azurite no acredita esa verificación cloud. El antivirus se elige con el despliegue y es requisito antes de producción.
