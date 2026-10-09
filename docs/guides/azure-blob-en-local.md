# Azure Blob Storage real desde la aplicación local

Estado, 2026-10-02: Azure elegido; cuenta de desarrollo todavía sin crear. Esta guía permite preparar Azure y comprobar carga/descarga con CLI. El adaptador y el flujo de archivos de la aplicación **todavía no están implementados**. Sus archivos y etapas están en el [plan](../plans/2026-10-02-almacenamiento-de-archivos.md); la elección del proveedor, en el [ADR 0012](../decisions/0012-azure-blob-como-primer-proveedor-de-archivos.md).

## 1. Qué corre dónde

```text
PC: navegador → Vite/React → API .NET local
                              ├─ PostgreSQL local
                              ├─ Redis local
                              └─ HTTPS saliente → Azure Blob Storage real
```

La aplicación no necesita estar publicada. La API hace la conexión saliente al storage y envía/recibe bytes. No se abre un túnel para Azure, no se entrega una connection string al front y no se cambia el origen del navegador. Azurite es un emulador diferente; se puede sumar más adelante para tests sin dependencia cloud.

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

Dentro de la cuenta, abrir **Data storage → Containers** y crear:

- `uploads-staging`: blobs pendientes de inspección y cargas temporales.
- `files-content`: originales y derivados publicados, incluidas versiones históricas.

Ambos con acceso privado. Son contenedores distintos para separar políticas de ciclo de vida; no un contenedor por usuario ni por archivo. Las claves internas usan IDs generados y un prefijo de instalación, según el plan. No configurar todavía lifecycle que borre contenido publicado por antigüedad: su retención funcional debe cerrarse primero.

Fuentes: [crear cuenta](https://learn.microsoft.com/en-us/azure/storage/common/storage-account-create) y [administrar contenedores](https://learn.microsoft.com/en-us/azure/storage/blobs/blob-containers-portal).

## 3. Dar acceso a la sesión de desarrollo

En IAM, asignar a tu usuario de Microsoft Entra **Storage Blob Data Contributor** sobre cada uno de los dos contenedores. Esa asignación necesita una identidad con permiso para administrar roles. Ser Owner/Contributor de recursos no implica automáticamente poder leer/escribir los datos de blobs con Entra.

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
$blobContainerName = "uploads-staging"
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

En el portal o Storage Explorer, abrir `uploads-staging` y buscar **esa clave exacta**. Su presencia y la comparación de hash comprueban que los bytes llegaron y se recuperaron intactos. Se puede repetir el upload original para verificar que el mismo block blob no se reemplaza; se espera un fallo al existir.

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

## 5. Configuración prevista para la API

**Contrato propuesto; todavía sin consumidor en el código.** Estos valores serán opciones de Infrastructure después de implementar E1a. Ponerlos ahora en configuración no hace que la aplicación guarde archivos.

```json
{
  "FileStorage": {
    "Enabled": true,
    "Provider": "AzureBlob",
    "LocationId": "azure-dev-01",
    "Azure": {
      "ServiceUri": "https://<cuenta>.blob.core.windows.net",
      "StagingContainer": "uploads-staging",
      "ContentContainer": "files-content",
      "CredentialMode": "AzureCli",
      "TenantId": "<tenant-id>"
    }
  }
}
```

El endpoint oficial se copia de la cuenta; no se escribe un SAS en `ServiceUri`. Para evitar fijar una cuenta personal en la plantilla, estos valores pueden ir en los user-secrets del proyecto **Api**, que ya tiene `UserSecretsId`, o en variables de entorno. No hay secreto de cuenta que guardar cuando se usa la sesión de CLI. No ejecutar `user-secrets list` ni imprimir connection strings como paso de diagnóstico.

Propuesta de credenciales explícitas:

- Local/CLI: `AzureCliCredential`, con la sesión iniciada arriba.
- Local/Visual Studio: `VisualStudioCredential`, si se elige ese modo, con la misma asignación de permisos de datos.
- Despliegue Azure: `ManagedIdentityCredential` y rol de datos para la identidad de la API sobre sus contenedores.
- Alternativa local con clave: connection string solo en user-secrets/entorno, como modo separado; no hay fallback automático a claves.

No hace falta un recurso Aspire de Blob para acceder a una cuenta existente. AppHost mantiene PostgreSQL/Redis/API/front locales; la API carga sus opciones y abre los clientes Azure. Al activar Files se validan opciones y disponibilidad; no se crean contenedores por efecto de arrancar la aplicación. [Autenticación del SDK](https://learn.microsoft.com/en-us/azure/storage/blobs/storage-quickstart-blobs-dotnet).

## 6. Qué falta para cargar desde la aplicación

1. **E1a:** agregar puerto, modelos del storage y adaptador Azure, configurar credenciales y verificar stream/propiedades/lectura/borrado sin reemplazos. Usar prefijos propios por ejecución en las pruebas remotas.
2. **E1b:** catálogo en PostgreSQL, versiones, intentos, servicios/rutas autorizadas y procesamiento durable. Reintentos y limpieza de staging deben funcionar después de cortes.
3. **E1c:** definir el alcance del fondo, dibujar el diálogo y conectar el front con archivos publicados.

El árbol detallado está en el plan. Las pruebas automáticas normales no deben depender silenciosamente de tu sesión personal ni tocar un contenedor de producción. La verificación remota se ejecuta explícitamente con cuenta/prefijo de desarrollo; Azurite puede complementar el contrato sin demostrar por sí solo las capacidades cloud.

## 7. Diagnóstico y evidencia

| Síntoma | Revisar |
| --- | --- |
| 401 / no se encuentra sesión | Login, tenant y modo de credencial elegido |
| 403 | Rol de datos, alcance, propagación y firewall. Guardar el código Azure sanitizado, nunca tokens |
| Contenedor inexistente | Nombre configurado y creación previa; el arranque no lo crea |
| Timeout / DNS | Acceso saliente, endpoint, firewall o conectividad privada |
| Se ve en portal pero no hay acceso SDK | Portal y SDK pueden estar usando mecanismos o identidades distintos |
| Upload correcto pero fondo sin cambiar | La prueba de storage no implementa ni confirma la selección funcional |

Pendientes al 2026-10-02: crear cuenta y contenedores, configurar sesión y verificar esta prueba en Azure real. Después, registrar el resultado del adaptador .NET y del flujo completo por separado. Las instrucciones y ejemplos de esta guía no acreditan que esas pruebas ya se hayan ejecutado.
