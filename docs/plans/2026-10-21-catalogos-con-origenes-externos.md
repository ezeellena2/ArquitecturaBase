# Catálogos con orígenes externos

**Estado:** borrador para pensar, del 2026-10-21. No hay decisiones tomadas ni tableros, y no se programa hasta que el usuario lo defina.

## Hoy

- **Hay dos tipos de catálogo.**
  - **Manual:** las opciones se cargan en la app.
  - **Filtros:** salen de los usuarios y roles del sistema.
- **No hay conexión a sistemas externos.** El tipo «Consulta» (SQL) se descartó por ahora en el [ADR 0019](../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md). Además, ese ADR solo lo admite sobre vistas de nuestra propia base, nunca sobre la base de otro sistema.
- **Un ítem tiene** código, texto en español e inglés (hasta 30 caracteres, por el límite de WhatsApp), detalle (hasta 300), orden y si está activo (`BusinessFormCatalogItem`). Una baja nunca borra: las respuestas viejas conservan su texto.
- **Una pregunta muestra hasta 200 opciones en WhatsApp.** Una lista más grande necesita filtrarse por persona, o la búsqueda en vivo con Meta, que está pendiente en «[intercambio cifrado](formularios/README.md#después-de-la-etapa-5)».

## La idea central: el origen alimenta una copia local

Cada catálogo externo tiene un **origen** que lo alimenta. Las opciones se guardan en una **copia local**, dentro de nuestra base, y **los formularios siempre leen esa copia**. Nunca se consulta el sistema del cliente en el momento de mandar un formulario.

Por qué:
- **Disponibilidad:** si el sistema del cliente se cae, los formularios siguen funcionando.
- **Velocidad:** las opciones tienen que estar listas al mandar el formulario.
- **Historia:** lo que el origen deja de mandar queda inactivo, no se borra.
- **Seguridad:** nada externo se ejecuta ni se consulta durante un envío.
- **Uniformidad:** cambiar de origen no cambia nada en formularios, WhatsApp ni el asistente.

## Los orígenes

| Origen | Cómo funciona | Para quién | Riesgo |
| --- | --- | --- | --- |
| **1. Archivo (Excel o CSV)** | Alguien sube la planilla, mapea las columnas, ve las diferencias y confirma. | Cualquier cliente, desde el día 1. | Ninguno: no tocamos su sistema. |
| **2. Recepción por API** | El sistema del cliente nos manda la lista completa o los cambios, con una clave que solo sirve para ese catálogo. | Clientes con área de sistemas. | Bajo: no guardamos credenciales ajenas. |
| **3. Lectura de su API** | Configuramos dirección, autenticación, paginación y mapeo. El sistema la lee cada cierto tiempo. | ERP con API: Tango, SAP, Odoo y otros. | Medio: guardamos una credencial suya. |
| **4a. Conector en su red** | Un programa chico, instalado en la red del cliente, lee su base y nos manda la lista por el origen 2. | Base sin API y el sistema en la nube. | Medio: hay que distribuir y actualizar el conector. |
| **4b. Conexión directa a su base** | Motor, cadena de conexión y una consulta de solo lectura validada. | Solo si la app corre en el servidor del cliente, en su misma red. | Alto: drivers por motor, SQL y credenciales. Necesita un ADR que amplíe el 0019. |

## El mapeo y la sincronización, iguales para todos los orígenes

**El mapeo** relaciona cada campo del origen con uno nuestro:

| Campo | Regla |
| --- | --- |
| Código externo | Obligatorio y estable. Es el que evita duplicados. |
| Texto en español | Obligatorio, hasta 30 caracteres. |
| Texto en inglés | Opcional. |
| Detalle (español e inglés) | Opcional. |
| Activo | Opcional. |
| Orden | Opcional. |
| **Atributos para filtrar** | Opcionales (zona, rubro, sucursal). Sirven para mostrarle a cada persona solo lo suyo, por ejemplo «proveedores de mi zona». |

**Cada sincronización:**
1. lee el origen;
2. valida fila por fila;
3. calcula las diferencias (altas, cambios y bajas);
4. aplica todo en una transacción;
5. deja un reporte.

**Reglas de la sincronización:**
- **Bajas:** lo que deja de venir se inactiva, nunca se borra. Si vuelve, se reactiva.
- **Protección contra errores del origen:**
  - un lote vacío no se aplica sin confirmación;
  - un lote que daría de baja más de un porcentaje del catálogo, tampoco;
  - un lote con demasiadas filas inválidas no se aplica.
- **Filas inválidas:** se informan con su motivo, sin frenar las demás, salvo que superen el umbral.
- **Lotes repetidos:** un mismo lote no se aplica dos veces (se compara por su hash).
- **Robustez ([ADR 0018][0018]):** leer un origen es un efecto externo. Va con operación durable, lease y fence, reintento con espera creciente y estado visible: «Actualizado hace 2 h · 1.240 opciones» o «Falló la última actualización: …».
- **Frecuencia:** manual, cada N horas, o al recibir un lote (origen 2).

## Seguridad

- **Credenciales:**
  - se guardan cifradas, como las claves de IA;
  - el front solo ve «configurada»;
  - nunca van a los logs y se pueden rotar.
- **Permiso propio** para configurar orígenes externos, distinto de administrar catálogos Manual o Filtros, como pide el ADR 0019 para «Consulta».
- **Lectura de APIs (origen 3):**
  - solo HTTPS y sin redirecciones;
  - lista de hosts permitidos;
  - sin direcciones de red privada, salvo una instalación en el servidor del cliente que lo configure explícitamente;
  - tiempo máximo, tamaño máximo y tope de filas.
- **Recepción (origen 2):**
  - una clave por catálogo, que se ve una sola vez y se guarda como hash;
  - se puede revocar y rotar;
  - límite de pedidos por minuto y, si se quiere, lista de IP permitidas.
- **Base directa (4b):**
  - un usuario de solo lectura del lado del cliente;
  - la consulta se valida como una única lectura, con las reglas del ADR 0019;
  - siempre se prefiere el conector.
- **Lo que llega son datos:** se valida tipo y largo, y nada se ejecuta.
- **Multitenant:** orígenes, claves y copias van por organización.

## Esquema de base (propuesta)

| Tabla | Qué guarda |
| --- | --- |
| `FormCatalogs` (existe) | Suma el tipo `External`. |
| `FormCatalogSources` (nueva, 1 a 1 con el catálogo) | Tipo de origen, configuración no secreta (JSON validado: dirección, ruta de la lista, paginación, mapeo), secreto cifrado, frecuencia, revisión, estado (Ok, Falla, Requiere atención), última sincronización exitosa, próxima ejecución y las columnas de lease y fence. |
| `FormCatalogItems` (existe) | Suma el código externo (único por catálogo), los atributos para filtrar (JSON), la fecha en el origen y el último lote en que se vio. |
| `FormCatalogSyncRuns` (nueva) | Una fila por sincronización: disparador, estado, contadores (leídas, válidas, altas, cambios, bajas, inválidas), motivo seguro, hash del lote, inicio, fin y quién la pidió. |
| `FormCatalogSyncIssues` (nueva) | Las filas inválidas con su motivo, sin datos sensibles, guardadas un tiempo limitado. |
| `FormCatalogPushKeys` (nueva) | Hash de la clave, catálogo, creación, vencimiento y revocación. |

Los lotes grandes se cargan primero en una tabla de paso y se aplican en partes. **Escala:**
- un índice único por catálogo y código externo;
- lectura por persona filtrada por atributos;
- un tope de opciones por catálogo, que hay que definir.

La copia local hace de caché: no hace falta Redis.

## Pantallas (para los tableros)

- **Crear catálogo:** elegir el origen entre Manual, Filtros, Archivo, Recepción por API, Lectura de API o Base de datos.
- **Configuración guiada por pasos:**
  1. conexión, según el tipo;
  2. probar la conexión;
  3. mapeo, con una vista previa de 10 filas;
  4. filtros por persona;
  5. frecuencia;
  6. revisión de diferencias;
  7. activar.
- **Detalle del catálogo:**
  - estado de la sincronización;
  - las opciones, de solo lectura;
  - historial de sincronizaciones con sus problemas;
  - «Actualizar ahora» y «Pausar».
- **Datos para tu sistema (origen 2):** la dirección, la clave (se ve una sola vez), un ejemplo del JSON y el último lote recibido.
- **Avisos:** un catálogo con fallas se ve en el listado de catálogos y en los formularios que lo usan.

## Complejidad

| Pieza | Tamaño | Comentario |
| --- | --- | --- |
| Base común: tipo Externo, copia, sincronización, historial y pantalla de estado | Grande | Se hace una vez y habilita todo lo demás. |
| 1. Archivo | Chica a mediana | Sobre la base común. |
| 2. Recepción por API | Mediana | Clave, endpoint, idempotencia. |
| 3. Lectura de API | Mediana a grande | Autenticación, paginación, protección de red y operación durable. |
| 4a. Conector | Grande | Un programa aparte instalado en el cliente, con versiones y actualizaciones. |
| 4b. Base directa | Grande | Drivers por motor, SQL validado y un ADR nuevo. |

**Fases sugeridas:**
- **A:** la base común, más Archivo y Recepción por API.
- **B:** Lectura de API.
- **C:** Base de datos, con conector o directa, según los clientes.

## Preguntas abiertas

1. ¿Qué sistemas tienen los primeros clientes (ERP, SQL Server, planillas)? Eso define si las fases B y C hacen falta y en qué orden.
2. ¿Cuántas opciones puede tener el catálogo más grande?
3. ¿Se pueden tocar localmente las opciones externas? Por ejemplo, ocultar una o cambiarle el texto. Recomendado: solo ocultar.
4. ¿Cada cuánto se actualiza por defecto?
5. ¿Hace falta filtrar por persona con atributos del origen, como «proveedores de mi zona»?
6. ¿La base directa se ofrece solo en instalaciones en el servidor del cliente?
7. ¿Quién configura los orígenes: nosotros en la implementación, o el cliente?

## Relación con otros planes

- El [ADR 0019](../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md) sigue vigente para «Consulta» sobre nuestra base. La base directa del cliente necesita su propio ADR.
- El [intercambio cifrado con Meta](formularios/README.md#después-de-la-etapa-5) habilita las listas largas y la búsqueda en vivo.
- En el multitenant, todo va por organización.

[0018]: ../decisions/0018-robustez-proporcional-al-efecto.md
