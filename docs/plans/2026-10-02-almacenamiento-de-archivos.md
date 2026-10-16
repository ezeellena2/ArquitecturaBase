# Archivos en Azure Blob: alcance y pendientes

Estado al 2026-10-15: **primer consumidor implementado y verificado**. Azure Blob Storage
privado, metadata y foto/PDF de formularios funcionan en Azurite y Azure real con AzureCli.
Reglas implementadas en [arquitectura](../architecture/backend.md#archivos-privados),
[formularios](../features/formularios.md#fotos-y-documentos--implementación-local-2b-2026-10-15)
y [guía local](../guides/azure-blob-en-local.md). Los otros consumidores y las operaciones de
producción de abajo siguen pendientes; este plan no los da por implementados.
El diseño extenso del 2026-10-02 (árbol de clases, rutas orientativas, comparación de
proveedores) se recupera desde Git: `git show 4b151eb6:docs/plans/2026-10-02-almacenamiento-de-archivos.md`.

## Consumidores, en orden

1. **Fotos y documentos de formularios** ([Etapa 2B implementada](../features/formularios.md#fotos-y-documentos--implementación-local-2b-2026-10-15)).
   Es el primer consumidor real y fija su propio alcance: registro del archivo (dueño, nombre,
   tipo, tamaño, hash y clave), clave determinística por respuesta y archivo, reintento simple,
   sin versiones, sin staging, sin cargas desde el navegador y sin pruebas de dos réplicas.
   Sus reglas implementadas prevalecen sobre las propuestas generales de este documento.
2. **PDF de la Etapa 4** de formularios, sobre el mismo adaptador.
3. **Fondo del chat de WhatsApp**: consumidor posterior, todavía sin alcance ni tablero.
4. Documentos subidos desde la web, historial visible y transporte de volumen: sin fecha.

## Decisiones vigentes

Valen para cualquier consumidor; no se vuelven a preguntar.

- **Bytes en almacenamiento de objetos privado; catálogo en PostgreSQL.** Nunca en `wwwroot`,
  Git, Redis, base64 dentro de JSON ni en el disco del contenedor.
- **Puerto propio en Application, SDK en Infrastructure.** El puerto escribe por stream sin
  reemplazar (salvo que el consumidor elija clave determinística, como la 2B), lee por stream,
  consulta propiedades y borra un objeto exacto, con modelos propios y cancelación. No expone
  `BlobClient`, URLs permanentes ni excepciones del SDK como errores de negocio. Paquetes:
  `Azure.Storage.Blobs` y `Azure.Identity`, con versión en `Directory.Packages.props`.
- **Credenciales fuera de la base, del repo y del navegador.** En local, una credencial elegida
  de forma explícita (`AzureCliCredential` con `az login`, o `VisualStudioCredential`); en Azure,
  identidad administrada. Si la elegida falla no se prueba otra en silencio. La connection string
  es alternativa local en user-secrets, no el mecanismo principal.
- **La API no crea contenedores al arrancar.** Se preparan antes; con archivos habilitados, un
  health check de acceso sin escribir queda pendiente operativo. AppHost provisiona Azurite
  por defecto en local; para Azure real se desactiva explícitamente en user-secrets.
- **Azure real para cerrar.** Azurite sirve para pruebas repetibles; una integración no se da por
  cerrada solo con el emulador. Tampoco alcanza la prueba con CLI de la guía: prueba acceso, no
  la aplicación.
- **Acceso solo por la API, mismo origen.** Objetos privados, autorización por operación y por
  recurso (un UUID no es un permiso), sin CORS. Cargas o descargas directas con SAS/URL firmada
  requieren antes un ADR de orígenes.
- **Claves de objeto sin datos personales** (`<instalación>/<id>/…`); el prefijo separa
  instalaciones, no implementa multitenencia.
- **Logs sin nombres de archivo, contenido, URLs firmadas ni credenciales**; con `[LoggerMessage]`.
- **Robustez según [ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md).** El storage
  es un efecto externo, pero cada consumidor justifica cuánto necesita: con lecturas idempotentes
  y claves determinísticas alcanza el reintento simple. Leases, trabajos durables y staging se
  reservan para cargas que no se pueden repetir sin costo (por ejemplo, desde el navegador).
- **No transacción común PostgreSQL–storage.** Nunca se transmite ni procesa un archivo con una
  transacción SQL abierta; los huérfanos se limpian con una ventana de seguridad.
- **Imágenes:** SkiaSharp quedó elegido detrás del puerto de procesamiento en la 2B;
  no usar `System.Drawing.Common`. Imágenes en línea con `nosniff`; documentos siempre
  como descarga.
- **Descartado sin necesidad medida:** deduplicación global por hash, multicloud activo, MinIO
  (repositorio comunitario archivado), microservicio de archivos, adaptadores S3/AWS.

## Pendientes y criterio de cierre

| Pendiente | Se cierra cuando |
| --- | --- |
| Health check de acceso a storage | Verifica acceso al contenedor configurado sin escribir; no convierte una falla transitoria en un requisito de aprovisionamiento cloud al arrancar. |
| Antivirus antes de producción | Elegido con el despliegue (por ejemplo, Defender for Storage) y documentado en [despliegue](../guides/despliegue.md). Un escáner caído no aprueba. |
| Respaldo y restauración | Política que cubra PostgreSQL y objetos juntos (RPO/RTO, región, plazo) y una restauración probada. Versionado o soft delete del proveedor no reemplazan el backup. |
| Retención y purga | Plazo por propósito decidido por negocio. No usar lifecycle del proveedor para borrar contenido referenciado. Los archivos de formularios siguen a la respuesta (sin plazo hoy). |
| Fondo del chat | Elegir alcance (por operador, global o por conversación), permisos y tablero; recién entonces diseñar preferencia, versiones y selección con revisión y 409. |
| Documentos desde la web e historial | Uso concreto elegido; cuarentena y escaneo; versiones y restauración solo si el producto las pide. |
| Transporte de volumen | Umbral medido de tráfico/tamaño; ADR de orígenes; SAS de corta duración solo sobre staging; CDN si hay caso. |
| Operación y costos | Métricas de bytes, cola, reintentos y fallas por proveedor; presupuesto con región y volumen reales. Migrar de proveedor es copiar, verificar hash y cambiar locators, no un cambio de configuración. |
