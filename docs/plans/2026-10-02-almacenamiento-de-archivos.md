# Archivos y fondo del chat: propuesta de arquitectura

**Estado:** diseño en elaboración, 2026-10-02. El usuario eligió Azure Blob Storage como primer proveedor y pruebas desde la aplicación local contra Azure real ([ADR 0012](../decisions/0012-azure-blob-como-primer-proveedor-de-archivos.md)). La implementación y el resto de las decisiones funcionales siguen pendientes. **El 2026-10-11 el primer consumidor real pasó a ser formularios:** fotos y documentos de la Etapa 2B ([plan de formularios, 12.2b](2026-10-07-formularios-dinamicos-y-whatsapp-flows.md)), antes del PDF de la Etapa 4. El fondo del chat queda como consumidor posterior.

**Objetivo:** permitir cambiar el fondo del chat como primer consumidor de una capacidad reutilizable para imágenes, PDF, Excel, CSV y otros archivos, con permisos, versiones, retención y almacenamiento reemplazable.

Este plan sigue la [arquitectura del backend](../architecture/backend.md), la [receta de áreas](../guides/agregar-un-area.md) y las [reglas del front](../../../ArquitecturaBaseFront/docs/architecture.md). No modifica las premisas vigentes. Al implementar y verificar, consolidar en las fuentes canónicas según la [auditoría documental](../guides/auditar-documentacion.md).

## 1. Qué existe y qué estamos proponiendo

- El chat ya compone `ChatTimeline` con `shared/ui/MessageHistory`; esa pieza compartida es dueña del fondo y del scroll. El fondo actual sale de los tokens `message-*`, sin carga de archivos.
- [WhatsApp](../features/whatsapp.md) es opcional y representa una instalación por negocio. El sistema de archivos se propone como capacidad del núcleo, utilizable sin WhatsApp. La preferencia del fondo queda dentro del módulo.
- PostgreSQL, Redis, MVC, servicios y Unit of Work ya tienen sus fronteras. Los archivos no agregan handlers, repositorio genérico, eventos de dominio ni otro caché de datos.
- Hipótesis inicial: un fondo por operador para todos sus chats y dispositivos. Falta elegir si debe ser global o por conversación. Esto cambia el consumidor y sus permisos, no el almacenamiento.
- Se conserva una organización por instalación, que es la arquitectura actual. Varias empresas en una sola aplicación requerirían otro diseño de aislamiento; un prefijo en el bucket no lo resuelve.

## 2. Recomendación inicial y motivo

Guardar **los bytes en almacenamiento de objetos privado** y **el catálogo funcional en PostgreSQL**. Una biblioteca cliente conecta .NET con ese almacenamiento; no reemplaza el servicio de almacenamiento ni las reglas del producto.

```text
React
  → Controllers MVC
  → Servicios de Files (Application)
      ├─ repositorios/lectores → PostgreSQL
      │    dueños, referencias, versiones, estados, cuotas, trabajos
      └─ puertos de integración → adaptadores de Infrastructure
           Azure Blob Storage (primer proveedor elegido)

Worker técnico → servicios de procesamiento y mantenimiento
  → inspeccionar, transformar, publicar, limpiar y reintentar
```

El contenido de un PDF, una imagen o un Excel son bytes para el almacenamiento. Lo que cambia por tipo y uso es la política de aceptación, inspección y presentación. Guardar un Excel no implica importarlo; guardar un PDF no implica renderizarlo ni extraer texto.

Este patrón permite escalar API, procesamiento y almacenamiento por separado. S3 y Blob Storage son servicios diseñados para objetos y grandes volúmenes de datos; esta propuesta adapta esas capacidades a los límites del proyecto. No afirma que todas las grandes empresas tengan la misma arquitectura. Fuentes: [S3](https://aws.amazon.com/s3/) y [Blob Storage](https://learn.microsoft.com/en-us/azure/storage/blobs/storage-blobs-introduction).

### Comparación de mecanismos

| Mecanismo | Utilidad | Límite para esta plantilla |
| --- | --- | --- |
| Bytes en PostgreSQL (`bytea`) | Atomicidad local entre fila y contenido; archivos pequeños y escasos | Agranda base, backups y tráfico de réplicas; no es la propuesta para grandes volúmenes |
| Carpeta en el disco de la API | Prototipo local o instalación simple con volumen persistente | Una réplica no ve el disco de otra; despliegues y fallas pueden perderlo. Escalar exige almacenamiento compartido y operación adicional |
| NAS / filesystem compartido | Integración con aplicaciones que necesitan filesystem | Hay que gestionar disponibilidad, capacidad y permisos; no aporta por sí solo historial funcional |
| Almacenamiento de objetos | Contenido independiente de los procesos; acceso por API y políticas de retención | Consistencia entre PostgreSQL y objetos, costos de transferencia y operación según proveedor |

No guardar archivos de usuarios en `wwwroot`, Git, Redis, base64 en JSON ni en el disco efímero del contenedor. Un volumen local puede ser un adaptador de desarrollo, pero no se promete que tenga la disponibilidad de un servicio administrado.

## 3. Azure elegido y portabilidad futura

**Elección del usuario, 2026-10-02:** empezar con Azure Blob Storage real, aunque la API, el front, PostgreSQL y Redis corran localmente. Todavía falta crear la cuenta de Storage. La [guía de Azure Blob en local](../guides/azure-blob-en-local.md) prepara cuenta, contenedores, acceso y una prueba de bytes independiente de la aplicación. No hace falta publicar la aplicación, abrir un túnel ni habilitar CORS para este recorrido.

La tabla conserva alternativas de portabilidad. Azure deja de ser candidato y es el único adaptador que se implementará inicialmente. AWS/S3/Ceph no forman parte de E1; Azurite puede servir para tests repetibles, pero la validación pedida es contra Azure real.

| Despliegue | Almacenamiento candidato | Cliente .NET | Evaluación |
| --- | --- | --- | --- |
| Azure real desde local | Azure Blob Storage | `Azure.Storage.Blobs` y `Azure.Identity` | Elegido; sesión de desarrollo en local, identidad administrada al desplegar en Azure |
| AWS | Amazon S3 | `AWSSDK.S3` | Primera opción si se elige AWS; roles y credenciales temporales |
| VPS / infraestructura propia | Servicio externo de objetos o servidor compatible con S3 | `AWSSDK.S3` con endpoint configurado, sujeto a pruebas | Un VPS también puede usar Blob o S3: el almacenamiento no tiene que vivir junto a la API |
| Infraestructura propia con operación de clúster | Ceph Object Gateway | Adaptador S3 con compatibilidad comprobada | Candidato si hay capacidad de operar almacenamiento distribuido |
| Desarrollo Azure | Azurite | Mismo SDK de Azure | Emulador para pruebas locales; no acredita todas las capacidades de Azure real |

**Propuesta:** contrato propio pequeño en Application y SDK oficial de Azure dentro de Infrastructure. Azure Blob tiene su propia API: no asumir compatibilidad S3 nativa.

El contrato inicial `IObjectStorage` necesita escribir por stream sin reemplazar, abrir lectura por stream, consultar propiedades y eliminar un objeto preciso. Usa modelos propios, cancelación y claves internas; no expone `BlobClient`, `S3Response`, URLs permanentes ni excepciones de SDK como errores de negocio. Un locator persistido conserva `StorageLocationId`, `StorageArea` (staging o contenido), `ObjectKey` y, cuando corresponda, la versión técnica del proveedor. El adaptador traduce el área a contenedor. Las credenciales viven fuera de la base y del repositorio.

### Recorrido local y organización de Azure

```text
PC de desarrollo
  navegador → Vite/React → API local → Azure Blob Storage por HTTPS
                             ├─ PostgreSQL local (Aspire)
                             └─ Redis local (Aspire)

Cuenta de Storage de desarrollo (nombre global único)
├─ uploads-staging          # privado: cargas pendientes de validar
│  └─ <installation-id>/<upload-id>/<attempt-id>
└─ files-content            # privado: versiones publicadas
   └─ <installation-id>/files/<file-id>/versions/<version-id>/
      ├─ original
      └─ background
```

Se recomiendan cuenta y contenedores distintos de producción. La primera cuenta será Standard StorageV2 para block blobs, con Hot y LRS como punto de partida de desarrollo, sin namespace jerárquico ni SFTP. Región, nombre y retención técnica se eligen al crearla; no hay recursos aprovisionados por este plan.

El usuario de desarrollo recibe `Storage Blob Data Contributor` sobre ambos contenedores. La API usa una credencial de desarrollo seleccionada explícitamente: `AzureCliCredential` con `az login` como primera propuesta, o `VisualStudioCredential` si se elige esa sesión. En Azure se propone `ManagedIdentityCredential`. El factory técnico resuelve la selección; no se prueba silenciosamente una cuenta distinta al fallar la esperada. Connection strings son una alternativa local en user-secrets, no el mecanismo principal ni una credencial del front.

`FileStorageOptions` define activación/proveedor/locationId; `AzureBlobStorageOptions`, endpoint, tenant opcional, contenedores y modo de credencial. `AzureBlobClientFactory` arma clientes reutilizables; `AzureBlobObjectStorage` implementa el puerto. La API no crea contenedores automáticamente al arrancar: se preparan antes y se verifica acceso con un health check cuando Files está habilitado. AppHost conserva servicios locales y no levanta Azurite para simular el storage elegido. Los límites funcionales siguen en `Application/Configuration/FilePolicyOptions`.

Dos verificaciones diferentes: la prueba CLI demuestra acceso e integridad en Azure; la integración posterior usa el SDK/adaptador real, y el caso de uso confirma también catálogo PostgreSQL, permisos y recuperación. Una prueba CLI exitosa no acredita que la aplicación ya pueda cargar archivos.

La portabilidad cubre la aplicación y los contratos. Migrar bytes requiere copiar, verificar integridad y cambiar locators; cambiar una configuración no mueve los archivos existentes. Se mantiene lectura del destino anterior durante una migración y las cargas nuevas pueden dirigirse al nuevo. Cambiar el proveedor no cambia el ID funcional del archivo.

Compatibilidad S3 debe probarse para cada implementación: creación condicional, streams, checksums, multipart, Range, firmas, versiones y borrado. No equivale a soportar todas las funciones de AWS. [Ceph documenta su matriz](https://docs.ceph.com/en/latest/radosgw/s3/).

**MinIO no queda elegido por defecto:** el repositorio comunitario consultado figura archivado y de solo lectura desde el 2026-04-25. Una eventual oferta comercial u otro proyecto se evalúan separadamente. [Fuente del proyecto](https://github.com/minio/minio). Otros servidores, como RustFS, requieren evaluar mantenimiento, recuperación y compatibilidad antes de seleccionarlos.

Para imágenes, evaluar `SkiaSharp` frente a `ImageSharp`, detrás de `IImageProcessor`. SkiaSharp requiere revisar dependencias nativas de la imagen de despliegue; ImageSharp requiere revisar la licencia aplicable a la versión y al proyecto derivado. No agregar `System.Drawing.Common` como procesador portable. No hacen falta bibliotecas de PDF/Excel/CSV para almacenar sus bytes. Fuentes: [SkiaSharp](https://github.com/mono/SkiaSharp), [ImageSharp](https://docs.sixlabors.com/articles/imagesharp/getting-started.html) y [licencia de Six Labors](https://sixlabors.com/posts/license-changes/).

Versiones de paquetes solo en `Directory.Packages.props`. Su selección y compatibilidad con .NET 10 se prueban al implementar; este plan no fija versiones sin esa prueba.

## 4. Modelo funcional propuesto

| Pieza | Responsabilidad y datos relevantes |
| --- | --- |
| `StoredFile` | Identidad estable, dueño, propósito, nombre visible, revisión y versión actual publicada. `Entity` con Guid v7; auditable y con borrado lógico según las reglas existentes |
| `FileVersion` | Versión de contenido: archivo, número, estado, MIME detectado, bytes reales, SHA-256 calculado por servidor, locator, autor y fecha UTC. Una versión publicada no se sobrescribe |
| `FileUpload` | Intento de carga: requestId, versión destino, staging privado, bytes reservados, expiración y estado. Permite retomar la consulta después de una respuesta incierta |
| `FileReference` | Registro de uso de una versión por un consumidor, con identificador de consumidor y propósito controlados por servidor. Impide purgar contenido en uso |
| `FileWorkItem` | Trabajo durable: inspección, derivados, publicación o purga; intento, próxima ejecución, lease, token de propiedad y fallo operativo sanitizado |
| `FileQuota` | Contabilidad de bytes reservados/ocupados por dueño o instalación. Necesaria si se exige una cuota dura concurrente; no calcularla como suma fuera de transacción |
| `WhatsAppChatAppearance` | Preferencia del operador en el módulo: versión de fondo, modo visual elegido y revisión. Referencia una versión publicada, nunca una URL del proveedor |

`FileReference` no se convierte en una tabla de relaciones arbitrarias enviada por el cliente. El consumidor registra y retira su referencia en la misma transacción que su propia selección. Su fila puede tener además una FK tipada a `FileVersion`. Claves de consumidor y reglas de autorización se registran explícitamente. El núcleo no conoce nombres ni entidades de WhatsApp.

El propósito de carga determina políticas: tipos permitidos, límites, permiso adicional, retención y procesadores. Se propone un catálogo compuesto mediante `IFilePurposeSource` en `Interfaces/Channels`; cada módulo aporta su política en `Modules/<M>/Channels`. Quitar el módulo quita la oferta de ese propósito, no purga automáticamente sus archivos. Evita que el núcleo contenga constantes de WhatsApp.

Estados iniciales orientativos:

```text
Carga:    Pending → Uploading → Uploaded → Completed
                         └─ fallo/caducidad → Expired / Aborted

Versión:  Pending → Processing → Ready
                        └─ rechazo → Rejected
          Ready → PurgePending → Purged
```

Un fallo temporal deja el trabajo reintentable con backoff, no convierte un archivo en seguro. Solo `Ready` permite seleccionar o descargar contenido. Invariantes e índices: `(FileId, VersionNumber)` único; requestId único por actor/operación; una referencia por consumidor/slot; trabajo único por versión/tipo/revisión de procesamiento. La idempotencia comprueba también que el pedido repetido coincida con el original.

Claves ilustrativas de objetos, sin nombre original, correo ni teléfono:

```text
uploads-staging: <installation-id>/<upload-id>/<attempt-id>
files-content:   <installation-id>/files/<file-id>/versions/<version-id>/original
files-content:   <installation-id>/files/<file-id>/versions/<version-id>/background
```

Son claves de objetos, no carpetas físicas. No otorgan autorización. El prefijo separa instalaciones que comparten infraestructura; las identidades del proveedor también deben limitar su acceso. No se implementa multitenancy por agregar ese prefijo.

## 5. Historial: tres cosas diferentes

1. **Versiones funcionales:** qué contenido cambió, quién lo hizo y cuándo. PostgreSQL identifica cada versión y sus bytes inmutables. Un fondo reemplazado no destruye inmediatamente el anterior. Mostrar o restaurar versiones es un caso de uso del producto.
2. **Auditoría de acciones:** subir, seleccionar, restaurar, quitar y pedir borrado. `IAuditable` registra la última modificación, no un historial completo. Un registro append-only de acciones se agregaría si se requiere; no implica event sourcing.
3. **Protección técnica:** versionado/soft delete del proveedor y backups independientes. Ayudan frente a sobrescritura o eliminación accidental, pero no saben qué significa «fondo elegido por el operador».

Propuesta de restauración de contenido: crear una nueva versión funcional a partir de una versión histórica, conservando la procedencia, y publicar antes de seleccionarla. Volver a seleccionar un fondo anterior puede ser solamente un cambio auditable de referencia si el producto elige esa semántica. Definir cuál acción ofrece la UI antes de implementar.

El versionado del proveedor es protección adicional y configurable, no una dependencia del historial funcional. Las versiones técnicas cuestan almacenamiento; la purga debe considerar versiones no actuales y soft delete. Versionado y replicación no reemplazan un backup independiente ni una prueba de restauración de PostgreSQL más objetos. [S3 Versioning](https://docs.aws.amazon.com/AmazonS3/latest/userguide/Versioning.html) y [Azure Blob Versioning](https://learn.microsoft.com/en-us/azure/storage/blobs/versioning-overview).

## 6. Carga y consistencia: primera etapa

No existe una transacción común entre PostgreSQL y S3/Blob. No simularla manteniendo una transacción SQL abierta mientras se transmite o procesa un archivo.

1. **Iniciar:** validar permiso, propósito y tamaño declarado; dentro de un único Unit of Work reservar cuota y crear archivo/versión/intento. El mismo requestId devuelve el mismo intento si sus datos coinciden. Responder después del commit.
2. **Transmitir:** enviar bytes a staging privado mediante la API con buffers acotados. El servicio recibe `Stream` y datos propios, nunca `IFormFile` ni `HttpContext`. El borde HTTP verifica sesión/propiedad y adapta el cuerpo. Antes del I/O, un caso de uso toma una lease del intento y confirma; los bytes se escriben fuera de SQL a una clave exclusiva del intento de transmisión.
3. **Completar:** el servidor comprueba el objeto real, no solo la afirmación del navegador. Otro caso de uso confirma el intento y crea el trabajo durable en un solo Unit of Work. Responder 202 significa pendiente de procesamiento; no significa fondo aplicado. Repetir es idempotente y el estado se puede consultar.
4. **Procesar:** worker reclama el trabajo mediante un caso de uso y confirma una lease. Lee staging, valida tipo/tamaño/contenido y genera el fondo normalizado fuera de SQL. Escribe objetos finales sin reemplazar. Un caso de uso posterior confirma el locator, hash, `Ready` y trabajo terminado, verificando token/lease y revisión.
5. **Seleccionar:** el servicio del módulo toma los locks correspondientes, comprueba dueño, propósito y `Ready`, y guarda preferencia más referencia en un solo límite. Los helpers/repositorios no guardan y el servicio no llama a otro servicio de escritura. Invalidación, si se agrega caché, sale después del commit.

Cada paso que escribe filas es un método público de servicio con un único `ExecuteInTransactionAsync`. El host técnico secuencia los pasos en scopes separados; el servicio que abre el límite no lo envuelve con try/catch. Las escrituras de storage nunca se ejecutan como una supuesta parte reversible del commit SQL.

Los trabajos durables en PostgreSQL cumplen la función de outbox para estas operaciones externas: se guardan con el estado que exige ejecutarlos. No se introduce un bus genérico ni eventos de dominio. Un worker `BackgroundService` consulta pendientes; una señal en memoria puede reducir latencia, pero reiniciar no pierde el trabajo.

| Corte o carrera | Recuperación requerida |
| --- | --- |
| Reserva confirmada, navegador no carga | Caducar intento, liberar reserva y limpiar staging |
| Bytes guardados, confirmación SQL falla | Intento sigue rastreable; consulta/reintento verifica contenido. Limpieza con ventana de seguridad |
| Objeto final escrito, commit de publicación falla | Trabajo vuelve a ejecutarse y reconoce el mismo objeto/hash; no sobrescribe contenido diferente |
| Worker muere después de reclamar | Lease expira y otro worker retoma. Un dueño vencido no puede confirmar resultados |
| Dos cargas consumen la última cuota | Reserva serializada y restricción transaccional; una se rechaza antes de aceptar bytes |
| Dos pestañas cambian el fondo | `expectedRevision`, 409 y relectura conservando el borrador |
| Seleccionar compite con purgar | Ambos toman el mismo lock del archivo antes de verificar referencias/estado; `PurgePending` bloquea referencias nuevas |
| Storage borra y el commit siguiente falla | Reintento trata objeto ausente como eliminación ya realizada y termina el registro |

Todas las leases usan expiración UTC de `TimeProvider`, token de propietario y confirmación condicional. El lock de negocio es PostgreSQL; Redis no es la fuente de verdad del procesamiento.

## 7. Seguridad y políticas por tipo

Propuesta: objetos privados; autorizar tanto la operación como la relación con el dueño/recurso en el servidor. Un UUID difícil de adivinar no es un permiso. No basta con «puede subir archivos» para leer documentos de otro usuario.

Para el primer fondo: lista cerrada de imágenes raster estáticas, MIME/extensión/firma y decodificación coherentes, límites de bytes y de píxeles, re-encoding a formato controlado y eliminación de metadata. SVG, HTML, animaciones y archivos arbitrarios quedan fuera de ese propósito. El máximo en bytes del proxy, ASP.NET y política funcional deben concordar, y el servicio limita bytes realmente recibidos aunque falte `Content-Length`.

Para documentos futuros: descarga como adjunto con nombre sanitizado y `nosniff`; incorporar escaneo de malware y cuarentena antes de publicar. Un escáner caído no da aprobación. Los formatos ZIP usados por Office requieren límites de expansión si se inspeccionan. CSV se almacena sin alterarlo; una futura exportación/importación decide cómo tratar fórmulas. Las previews de PDF/Office son procesadores adicionales con aislamiento y límites, no el comportamiento de almacenamiento básico. [Guía de carga de archivos de OWASP](https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html).

La política de fondos puede cubrir validación y transformación estricta sin instalar toda la plataforma de previews/antivirus documental. Antes de habilitar documentos se define su política y se verifica su pipeline. «Soportar bytes de cualquier formato» no implica aceptar cualquier contenido para cualquier uso.

No registrar URLs firmadas, credenciales, contenido ni nombres originales en logs operativos. Los logs usan IDs, tamaños, estados y códigos sanitizados, con `[LoggerMessage]`. Las excepciones técnicas conservan el 500 genérico del proyecto; los errores funcionales van en `Domain/Files/*Errors` y ambos idiomas de los recursos.

## 8. Descargar y escalar el transporte

**Etapa inicial:** tráfico por el origen de la aplicación. La API transmite contenido autorizado por stream, sin cargarlo entero en memoria. Para backgrounds se entrega el derivado normalizado; los originales no se exponen automáticamente. Como el SPA guarda tokens en memoria, no asumir que un `<img src="/api/…">` envía Bearer: proponer descarga autenticada por `httpClient` como Blob y `URL.createObjectURL`, con revocación al cambiar/desmontar. Revisar la autenticación real antes de fijar ese contrato.

Cache del navegador para contenido privado con política explícita; nunca `public` para un documento autorizado por usuario. La API comprueba autorización antes de devolver 304. Redis, si hiciera falta, cachea proyecciones pequeñas; no los bytes. Para rangos de PDF/archivos grandes, definir y probar 206, 416, ETag y cancelación antes de ofrecerlos.

**Etapa de volumen:** descargas delegadas y cargas directas/multipart/reanudables mediante URL firmada corta o SAS, más CDN cuando haya un caso medido. El usuario recibe acceso a un objeto y verbo concretos, nunca las claves del proveedor. Las descargas privadas mantienen una política de revocación: una URL ya emitida puede seguir válida hasta vencer.

S3 permite reutilizar URLs firmadas durante su vigencia y una subida a la misma clave puede reemplazar el objeto. Por eso el cliente solo escribe staging; la publicación usa una versión técnica fijada o una copia condicional comprobada a una clave final que el cliente no puede modificar. Inspección y publicación operan sobre los mismos bytes. Un checksum o un ETag opaco por sí solo no demuestra que el contenido sea seguro. [Documentación de URLs firmadas](https://docs.aws.amazon.com/AmazonS3/latest/userguide/using-presigned-url.html). Azure ofrece [SAS de delegación](https://learn.microsoft.com/en-us/azure/storage/blobs/storage-blob-user-delegation-sas-create-dotnet).

El navegador hablando directamente con un host de storage implica otro origen. Hoy el proyecto establece un origen y no configura CORS. Antes de adoptar ese transporte se debe aprobar un ADR y modificar las fuentes y pruebas correspondientes, o diseñar un gateway bajo el mismo origen y evaluar su costo. No activar CORS amplio en la API como supuesto requisito del almacenamiento.

El límite actual no es «una sola API»: con storage compartido se pueden ejecutar varias réplicas y workers. El proxy de bytes consume conexiones y ancho de banda; las cargas directas descargan ese costo, pero agregan complejidad. Cambiar el transporte más adelante no cambia el modelo de archivos.

## 9. Retención, operación y costos

- Retirar un fondo quita la selección, no implica borrar físicamente todas sus versiones. La UI debe distinguir «restablecer fondo» de «eliminar archivo».
- Antes de purgar: marcar candidato bajo lock, verificar referencias vigentes/históricas y plazo, impedir nuevos usos, crear trabajo durable, borrar contenido y registrar el resultado. Un `IsDeleted` no libera espacio en storage. No usar bulk delete/update en entidades auditables o con borrado lógico.
- Limpiar cargas caducadas, intentos abandonados, derivados huérfanos y versiones fuera de retención. Reconciliar objetos y filas con ventana de seguridad; no borrar todo objeto sin fila observado en un listado mientras hay cargas activas.
- Lifecycle del proveedor sirve para staging y protección técnica; no debe borrar originales referenciados por edad sin coordinar con la política funcional. Una copia técnica retenida puede seguir costando después de la purga lógica.
- Medir bytes almacenados y reservados, cantidad/tamaño de cargas, descargas, cola pendiente, edad del trabajo más antiguo, reintentos, rechazados y fallas por proveedor. Alertas por atasco, cuota y falta de acceso a storage.
- Definir health/readiness cuando Files esté habilitado, concurrencia acotada de workers y límites de buffers/CPU. Separar workers en otro host solo si carga, aislamiento o despliegue lo justifica.
- Backups y restores abarcan PostgreSQL más objetos y sus referencias; elegir RPO/RTO, región y plazo. Un storage administrado evita operar discos, no elimina esta responsabilidad.
- Presupuestar originales + versiones + derivados + staging + copias técnicas; requests, lectura, egreso, procesamiento y backups. Para comparar proveedores faltan región y volumen: no hay un «más barato» defendible sin esos datos.

No proponer deduplicación global por hash al inicio: agrega referencias compartidas, carreras, riesgo de revelar coincidencias y complejidad de borrado. No proponer replicación multicloud activa, Elasticsearch, Kafka ni microservicio de archivos sin una necesidad medida.

## 10. Árbol orientativo de archivos a crear

Este es un mapa de responsabilidades, no una orden de generar esqueletos. **Hasta ahora se crearon el plan, la guía local y el ADR del proveedor; todavía no hay código de carga.** Los nombres de clases/modelos se ajustan al cerrar contratos. `[E1a]` implementa y verifica el adaptador Azure; `[E1b]`, catálogo/carga/procesamiento; `[E1c]`, consumidor de fondo. `[E2]` amplía documentos/historial; `[E3]` transporte de volumen. El proveedor único de E1 ya es Azure.

```text
ArquitecturaBase/
├─ docs/
│  ├─ plans/2026-10-02-almacenamiento-de-archivos.md       # este documento
│  ├─ decisions/0012-azure-blob-como-primer-proveedor-de-archivos.md # elección del proveedor
│  ├─ guides/azure-blob-en-local.md                       # preparar cuenta y probar desde la PC
│  ├─ features/archivos.md                               # reglas verificadas, durante E1
│  ├─ guides/almacenamiento-de-archivos.md                # setup, migración y recuperación E1
│  └─ deploy/azure-blob.md                               # identidad y operación productiva E1
├─ src/ArquitecturaBase.Domain/Files/                    # E1
│  ├─ AGENTS.md / CLAUDE.md
│  ├─ StoredFile.cs
│  ├─ FileVersion.cs
│  ├─ FileVersionStatus.cs
│  ├─ FileUpload.cs / FileUploadStatus.cs
│  ├─ FileReference.cs
│  ├─ FileWorkItem.cs / FileWorkItemStatus.cs
│  ├─ FileQuota.cs                                       # si se elige cuota dura en E1
│  └─ StoredFileErrors.cs / FileUploadErrors.cs
├─ src/ArquitecturaBase.Application/
│  ├─ Interfaces/Services/                              # E1
│  │  ├─ IFileService.cs                                # catálogo, lectura y borrado
│  │  ├─ IFileUploadService.cs                          # reservar, reclamar y completar
│  │  ├─ IFileProcessingService.cs                      # trabajos y publicación
│  │  └─ IFileMaintenanceService.cs                     # caducidad y purga
│  ├─ Interfaces/Persistence/Files/                    # E1, contratos por responsabilidad
│  │  ├─ IFileRepository.cs / IFileReader.cs
│  │  ├─ IFileUploadRepository.cs
│  │  ├─ IFileWorkRepository.cs
│  │  └─ IFileQuotaRepository.cs                        # condicional a cuota dura
│  ├─ Interfaces/Integrations/Files/                    # E1
│  │  ├─ IObjectStorage.cs
│  │  ├─ IFileContentInspector.cs
│  │  ├─ IImageProcessor.cs
│  │  ├─ IFileMalwareScanner.cs                         # E2, antes de habilitar documentos
│  │  └─ IObjectTransferSigner.cs                       # E3, no forzar en E1
│  ├─ Interfaces/Channels/IFilePurposeSource.cs          # E1, políticas aportadas por módulo
│  ├─ Services/Files/                                  # E1
│  │  ├─ AGENTS.md / CLAUDE.md
│  │  ├─ FileService.cs / FileUploadService.cs
│  │  ├─ FileProcessingService.cs / FileMaintenanceService.cs
│  │  ├─ FileAccessGuard.cs                             # helper internal sealed
│  │  ├─ FileUploadPolicy.cs / FileRetentionPolicy.cs
│  │  └─ FilePurposePolicy.cs                           # composición del catálogo
│  ├─ Models/Files/                                    # E1
│  │  ├─ StartFileUploadRequest.cs / CompleteFileUploadRequest.cs
│  │  ├─ FileUploadResponse.cs / FileResponse.cs / FileVersionResponse.cs
│  │  ├─ FileContentResponse.cs                         # stream/metadata propios
│  │  └─ ObjectLocator.cs / ObjectProperties.cs / StorageArea.cs # E1a, modelos sin SDK
│  ├─ Validation/Files/StartFileUploadRequestValidator.cs
│  └─ Configuration/FilePolicyOptions.cs
├─ src/ArquitecturaBase.Infrastructure/
│  ├─ Files/                                           # E1
│  │  ├─ AGENTS.md / CLAUDE.md                           # E1a
│  │  ├─ FileStorageRegistration.cs / FileStorageOptions.cs # E1a
│  │  ├─ Azure/AzureBlobStorageOptions.cs                # E1a, endpoint/containers/auth
│  │  ├─ Azure/AzureBlobClientFactory.cs                 # E1a, SDK y credencial explícita
│  │  ├─ Azure/AzureBlobObjectStorage.cs                 # E1a, único proveedor inicial
│  │  ├─ Azure/AzureBlobStorageHealthCheck.cs            # E1a, acceso sin escribir al arrancar
│  │  ├─ Processing/FileContentInspector.cs
│  │  ├─ Processing/<ProcesadorElegido>ImageProcessor.cs
│  │  └─ Workers/FileProcessingBackgroundService.cs
│  │     Workers/FileMaintenanceBackgroundService.cs
│  └─ Persistence/
│     ├─ Repositories/Files/                           # E1, implementan puertos
│     │  ├─ FileRepository.cs / FileUploadRepository.cs
│     │  └─ FileWorkRepository.cs / FileQuotaRepository.cs
│     ├─ Readers/Files/FileReader.cs
│     ├─ Configurations/Files/                         # una configuración por entidad E1
│     │  ├─ StoredFileConfiguration.cs / FileVersionConfiguration.cs
│     │  ├─ FileUploadConfiguration.cs / FileReferenceConfiguration.cs
│     │  └─ FileWorkItemConfiguration.cs / FileQuotaConfiguration.cs
│     └─ Migrations/<generada>_AddFileStorage.cs          # E1, procedimiento existente
├─ src/ArquitecturaBase.Api/
│  ├─ Controllers/FilesController.cs / FileUploadsController.cs
│  └─ Contracts/Files/                                 # E1, adaptan HTTP/stream
│     ├─ StartFileUploadHttpRequest.cs
│     ├─ UploadFileContentHttpRequest.cs                 # si el contrato elegido es multipart
│     └─ CompleteFileUploadHttpRequest.cs
├─ src/ArquitecturaBase.Domain/Modules/WhatsApp/
│  └─ WhatsAppChatAppearance.cs                         # E1 si se elige preferencia por operador
├─ src/ArquitecturaBase.Application/Modules/WhatsApp/
│  ├─ Channels/WhatsAppFilePurposeSource.cs
│  ├─ Interfaces/Services/IWhatsAppChatAppearanceService.cs
│  ├─ Interfaces/Persistence/IWhatsAppChatAppearanceRepository.cs
│  ├─ Interfaces/Persistence/IWhatsAppChatAppearanceReader.cs
│  ├─ Services/WhatsAppChatAppearanceService.cs
│  └─ Models/UpdateWhatsAppChatAppearanceRequest.cs      # Response y validador junto al área
├─ src/ArquitecturaBase.Infrastructure/Modules/WhatsApp/Persistence/
│  ├─ Configurations/WhatsAppChatAppearanceConfiguration.cs
│  ├─ Repositories/WhatsAppChatAppearanceRepository.cs
│  └─ Readers/WhatsAppChatAppearanceReader.cs
├─ src/ArquitecturaBase.Api/Modules/WhatsApp/
│  ├─ Controllers/WhatsAppChatAppearanceController.cs
│  └─ Contracts/UpdateWhatsAppChatAppearanceHttpRequest.cs
└─ tests/
   ├─ ArquitecturaBase.Domain.UnitTests/Files/            # estados e invariantes E1
   ├─ ArquitecturaBase.Application.UnitTests/Services/Files/
   │  ├─ AGENTS.md / CLAUDE.md
   │  └─ FileUploadServiceTests.cs / FileProcessingServiceTests.cs
   │     FileMaintenanceServiceTests.cs                 # commits, cortes y reintentos
   ├─ ArquitecturaBase.Api.IntegrationTests/Files/
   │  ├─ AGENTS.md / CLAUDE.md
   │  └─ AzureBlobObjectStorageTests.cs                   # E1a, contrato de storage real
   │     FileEndpointsTests.cs / FileStorageContractTests.cs
   │     FileProcessingRecoveryTests.cs / FileRetentionTests.cs
   ├─ ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp/
   │  └─ WhatsAppChatAppearanceEndpointsTests.cs
   ├─ ArquitecturaBase.Api.IntegrationTests/TestFeatures/Files/
   │  └─ AzureBlobTestScope.cs                            # E1a, prefijo propio y cleanup preciso
   └─ ArquitecturaBase.ArchitectureTests/FileStorageBoundaryTests.cs

ArquitecturaBaseFront/
└─ src/
   ├─ shared/api/files.ts / fileTypes.ts                  # E1, varias áreas pueden consumirlos
   ├─ shared/hooks/useFileUpload.ts / usePrivateFileContent.ts
   ├─ shared/ui/FileUploadField.tsx / FileUploadField.test.tsx
   ├─ features/whatsapp/api/appearance.ts
   ├─ features/whatsapp/hooks/useChatAppearance.ts
   ├─ features/whatsapp/components/ChatBackgroundDialog.tsx
   ├─ features/whatsapp/components/ChatBackgroundPreview.tsx
   └─ features/whatsapp/components/ChatBackgroundDialog.test.tsx
```

E2 agrega modelos, validadores, contratos y pruebas de listado de versiones/restauración; no se crean ahora nombres para un gestor documental completo. E3 agrega contratos del transporte directo y pruebas de su seguridad/compatibilidad. Nuevos pares de instrucciones solo donde la responsabilidad lo justifique; subcarpetas siguen el padre.

### Archivos existentes a modificar durante la implementación

| Área | Cambios previstos |
| --- | --- |
| Backend composición | `Application/DependencyInjection.cs`, `Infrastructure/DependencyInjection.cs`, `PersistenceRegistration.cs`, `Api/DependencyInjection.cs` y `Api/Program.cs`; repositorios solo en el registro de persistencia |
| Proveedor/hosting | `Directory.Packages.props`, proyecto Infrastructure, AppHost y configuración no secreta; los secretos siguen en user-secrets/entorno |
| Autorización/i18n | `Domain/Authorization/Permissions.cs` y `Permissions.All`, catálogo/permisos del módulo cuando correspondan, recursos es/en, seed y tests de permisos. No dar acceso global implícito a archivos por ser operador |
| Persistencia | Configuraciones, snapshot y migración nueva. Registros del módulo en su `WhatsAppInfrastructureRegistration`; no en el núcleo |
| Tests backend | Inventario explícito de rutas, OpenAPI, contratos HTTP, DI, recursos, locks en `AdvisoryLockKeys` y pruebas de fronteras. Integración con storage real además de PostgreSQL/Redis |
| Front | `MessageHistory.tsx` recibe una presentación de fondo; `ChatTimeline.tsx`/`ChatThread.tsx` la pasan sin acoplar la pieza común a WhatsApp. `src/index.css`, catálogo y traducciones es/en según elección visual |
| Documentación | Guía del área, arquitectura cuando corresponda, mapas de ambos repos, instrucciones locales/importaciones y fuentes de premisas solo para decisiones implementadas |

Los archivos del árbol representan capacidades propuestas. No es necesario crear una carpeta `features/files` en el front para que WhatsApp use `shared/api/files`; un gestor de archivos futuro tendría su propia feature sin importaciones cruzadas.

## 11. Contratos HTTP orientativos

Se cierran después de elegir semántica y transporte. Todas las rutas son MVC bajo `/api`, con permisos de operación y validación del recurso, rate limit, ProblemDetails, OpenAPI e inventario. No agregan prefijos de backend nuevos.

| Ruta propuesta | Función y éxito |
| --- | --- |
| `POST /api/file-uploads` | Iniciar/reservar carga idempotente; 201 con intento y Location a su consulta |
| `GET /api/file-uploads/{id}` | Estado autorizado; 200 |
| `PUT /api/file-uploads/{id}/content` | Transmitir staging por la API; 204 si terminó. Ante incertidumbre, consultar antes de retransmitir |
| `POST /api/file-uploads/{id}/complete` | Confirmar objeto y crear trabajo; 202; un fallo permanente no se presenta como en cola |
| `GET /api/files/{id}` | Metadata autorizada; 200 |
| `GET /api/files/{id}/versions/{versionId}/content` | Stream autorizado de Ready; 200 y contrato específico para el contenido binario |
| `DELETE /api/files/{id}` | Solicitar borrado si referencias/retención lo permiten; 202 si la purga es asíncrona |
| `GET /api/whatsapp/chat-appearance` | Preferencia propia, incluido fondo predeterminado; 200 |
| `PUT /api/whatsapp/chat-appearance` | Seleccionar versión Ready propia o restablecer, con expectedRevision; 204 |

Las respuestas de metadata siguen las extensiones Result existentes. Para streams hay que definir la adaptación de `Result<FileContentResponse>` a `FileStreamResult`, disposal, desconexión y OpenAPI sin mover lógica de negocio al controller; documentar y probar esa extensión acotada antes de usarla. Los errores previos al inicio del stream siguen ProblemDetails; después de enviar headers una falla corta la transmisión y se registra sanitizada.

Si se elige fondo global o por conversación, cambian las últimas rutas y el consumidor del árbol. El plan no fija todavía los permisos `files.*`: distinguir administración de archivos, cargas propias, descargas propias y acceso mediado por un recurso. Tener permiso genérico no evita comprobar la propiedad y el propósito; no se acepta un fileId ajeno para el fondo.

## 12. Etapas y criterios para avanzar

### E0 — Conversación y diseño

- [ ] Elegir quién configura el fondo y su alcance.
- [x] Elegir Azure Blob Storage como primer proveedor y prueba local contra Azure real (2026-10-02).
- [ ] Crear cuenta de desarrollo y dos contenedores privados siguiendo la guía local; probar upload/download con CLI.
- [ ] Definir volumen, tamaño máximo, cuotas y plazos; no hay valores aprobados todavía.
- [ ] Elegir procesador de imágenes y formatos raster admitidos con una prueba acotada.
- [ ] Resolver permisos propios y política de referencias.
- [x] Registrar ADR 0012 de proveedor. El modelo funcional sigue como propuesta hasta acordar sus puntos abiertos.
- [ ] Dibujar el diálogo y sus estados con la biblioteca común antes de implementarlo; seguir el contrato visual del front.

### E1 — Archivos como capacidad y fondo como primer consumidor

Orden de implementación:

1. **E1a, adaptador Azure:** puerto y modelos BCL propios, opciones/credenciales/registro, upload sin reemplazo, propiedades, lectura y borrado exacto. Pruebas del contrato contra Azure en un prefijo exclusivo; pruebas locales repetibles pueden usar Azurite como complemento. No agregar endpoints de prueba a producción.
2. **E1b, flujo durable:** entidades, migración, repositorios/lectores, servicios y rutas de carga. Confirmar catálogo y estados en PostgreSQL, procesamiento y limpieza después de cortes, acceso autorizado y dos réplicas. SDK sin credenciales en Application.
3. **E1c, fondo:** una vez elegido su alcance y el tablero, preferencia del módulo y front. Consume archivos Ready y conserva diseño/lectura/borradores del chat.

E1a puede empezar sin decidir aún si el fondo será global o propio. La verificación remota requiere que existan cuenta, contenedores y sesión con permisos. No dar por terminada esa integración si solo corren pruebas de emulador.

- Un proveedor real, modelo con versiones, cargas idempotentes, staging privado, procesamiento de imagen y limpieza durable desde el comienzo.
- Stream por la API con memoria acotada. Solo propósito de fondo habilitado; otros tipos no se anuncian como utilizables todavía.
- Elegir, previsualizar localmente, guardar y restablecer; el chat mantiene lectura/scroll y el fondo anterior hasta confirmar el cambio.
- Estados de carga, procesamiento, rechazo, falta de permiso, conflicto e incertidumbre. Borrador conservado ante fallo; validar campos localmente y usar el único Toaster para resultados generales.
- Pruebas de aislamiento, MIME falso, imagen excesiva, contenido corrupto, reintento, fallas de commit/storage, caída y recuperación del worker, dos réplicas y carrera selección/purga.
- Adaptador probado contra storage real o emulador más una verificación del proveedor real elegido; no acreditar capacidades cloud solamente con emulador.
- TDD para lógica; build y todos los tests backend, integración con Docker; build/lint/test/visual del front y revisión de capturas. Detener Aspire si se levantó. Consolidar solo lo implementado.

### E2 — Documentos e historial visible

- Habilitar PDF/Excel/CSV por propósito con cuarentena y escaneo adecuado; descarga de adjuntos.
- Listado de versiones, restauración y auditoría de acciones si se eligen; permisos y retención por tipo/uso.
- Backups con restauración comprobada y operación documentada; previews, OCR e importación son alcances separados.

### E3 — Volumen demostrado

- Si tráfico/tamaños lo justifican: multipart/reanudación, carga/descarga delegada, ADR de orígenes, CDN y workers separados.
- Pruebas contra cada proveedor implementado y ensayo de migración con inventario/hash.
- Retención por clases de almacenamiento si latencia de recuperación y costo lo permiten.

## 13. Decisiones abiertas para la próxima ronda

| Decisión | Recomendación inicial | Falta resolver |
| --- | --- | --- |
| Fondo | Por operador, compartido entre sus chats/dispositivos | Global o por conversación si ese es el producto deseado |
| Aislamiento | Una organización por instalación, arquitectura actual | Si se quiere SaaS compartido, abrir alcance explícito de multitenancy |
| Primer proveedor | Azure Blob Storage, elegido el 2026-10-02 | Crear cuenta de desarrollo, región, nombres y acceso; luego identidad del despliegue |
| Primeros archivos | Fondos raster estáticos | Qué uso concreto tendrán PDF/Excel/CSV antes de habilitarlos |
| Historial | Modelo de versiones desde E1, UI después | Cuántas/plazo, restaurar contenido o cambiar selección, auditoría requerida |
| Límites | Bytes/píxeles/cuota configurables por propósito | Cantidad de operadores, cargas por día y máximo de archivos futuros |
| Procesamiento | Trabajos durables en PostgreSQL, dentro del monolito | Procesador elegido y necesidad de scanner para cada propósito |
| Transporte | Stream por API inicialmente | Umbral medido para direct uploads y cambio explícito de orígenes |

## 14. Fuentes consultadas

Documentación primaria consultada el 2026-10-02; revisar mantenimiento, licencias y compatibilidad nuevamente al elegir e implementar. Las recomendaciones de diseño de este plan son una síntesis para el proyecto, no garantías de los proveedores.

- [Azure Blob Storage](https://learn.microsoft.com/en-us/azure/storage/blobs/storage-blobs-introduction), [SDK .NET](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/storage.blobs-readme?view=azure-dotnet), [Azurite](https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite).
- [AWS SDK .NET](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/welcome.html), [S3](https://aws.amazon.com/s3/), [URLs firmadas](https://docs.aws.amazon.com/AmazonS3/latest/userguide/using-presigned-url.html).
- [S3 Versioning](https://docs.aws.amazon.com/AmazonS3/latest/userguide/Versioning.html), [Blob Versioning](https://learn.microsoft.com/en-us/azure/storage/blobs/versioning-overview), [SAS de delegación](https://learn.microsoft.com/en-us/azure/storage/blobs/storage-blob-user-delegation-sas-create-dotnet).
- [Ceph S3 API](https://docs.ceph.com/en/latest/radosgw/s3/), [estado del repositorio MinIO](https://github.com/minio/minio).
- [OWASP File Upload](https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html), [SkiaSharp](https://github.com/mono/SkiaSharp), [ImageSharp](https://docs.sixlabors.com/articles/imagesharp/getting-started.html), [licencia Six Labors](https://sixlabors.com/posts/license-changes/).
