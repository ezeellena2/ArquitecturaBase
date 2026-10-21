# Catálogos: hoja de ruta

**Estado:** borrador para pensar, del 2026-10-21. **La próxima etapa ya está decidida** y tiene su propio resumen: [carga de una vez, tipo de valor y elegir varias](formularios/catalogos-carga-tipo-y-varias.md). Lo que sigue acá viene después y no se programa hasta que el usuario lo defina.

## Lo que definió el usuario

No se vuelve a preguntar.

- **Primer cliente: OCHA S.A.** Usa una base con el esquema de **Macrosistemas**, el mismo que La Clementina. Hace falta traer sus listas (por ejemplo, proveedores) y, más adelante, un conector a esa base.
- **Primer paso: Excel, con una plantilla definida por el catálogo.** Se resuelve en la próxima etapa.
- **Tipos de datos validados al cargar.** El tipo de valor de las opciones entra en la próxima etapa; los tipos de los campos extra, más adelante.
- **Nada fijo en el código:** el catálogo, sus campos y sus tipos son configuración. En el código vive solo el conjunto de tipos y el mecanismo de importación y validación.
- **Catálogos grandes:** hasta 10.000 opciones por catálogo.
- **Quién configura los orígenes:** las dos posibilidades, nosotros en la implementación y el cliente.
- **El asistente ayuda:**
  - arma un catálogo a pedido y devuelve su plantilla;
  - lee el Excel que sube la persona, propone a qué catálogo va y cómo se mapea, y lo importa cuando la persona confirma;
  - en la sección Catálogos hay una entrada o una guía para usarlo.
- **El sistema va a vivir en Azure.** Lo que se diseñe tiene que permitir que cualquier sistema se conecte, no solo Macrosistemas.

## La idea central: el origen alimenta una copia local

Cualquiera sea el origen (Excel, una API o una base), las opciones se guardan en una **copia local** dentro de nuestra base, y **los formularios siempre leen esa copia**. Nunca se consulta el sistema del cliente al mandar un formulario. Así:
- si el sistema del cliente se cae, los formularios siguen andando;
- lo que deja de venir queda inactivo, no se borra;
- cambiar de origen no toca formularios, WhatsApp ni el asistente.

## Campos extra del catálogo

**Qué son.** Hoy cada opción tiene campos fijos:
- un **código**, para la empresa;
- un **texto**, lo que se ve en WhatsApp;
- un **detalle** opcional.

Después de la próxima etapa suma un **valor** tipado. Los campos extra son **columnas propias que define cada catálogo**, además de esas. Por ejemplo, un catálogo de proveedores:

| Código | Texto (se ve en WhatsApp) | CUIT | Zona | Rubro | Plazo de pago (días) |
| --- | --- | --- | --- | --- | --- |
| PRV-001 | Frigorífico Del Sur | 30-71234567-8 | Centro | Frío | 30 |
| PRV-002 | Repuestos Norte | 30-70987654-3 | Norte | Repuestos | 15 |

En WhatsApp la persona sigue viendo solo «Frigorífico Del Sur». Las columnas extra viajan escondidas y sirven para tres cosas:

1. **Filtrar por persona.** Si «Zona» es un campo de filtro, cada empleado ve solo los proveedores de su zona: Juan, de zona Centro, ve 14 de 1.200. Así una lista enorme entra en los 200 del desplegable o en los 20 de elegir varias. Es lo que muestra el prototipo con «Proveedores de mi zona».
2. **Completar otras preguntas.** Elegir el proveedor completa solo su CUIT en el formulario.
3. **Validar al importar.** «Plazo de pago» tiene que ser un número entero, y la carga lo controla.

**Cómo se definen.** Cada campo tiene clave, nombre (es/en), tipo (texto, entero, decimal, fecha, sí o no, lista de valores…), reglas (obligatorio, único, mínimos y máximos) y **papel**: filtro o dato. Se guardan con revisión y 409, como el resto de la configuración.

**Cómo se guardan.** Los valores de cada opción van en un JSON validado por el servidor contra la definición del campo y normalizado (fechas `AAAA-MM-DD`, decimales canónicos). Quien sabe el tipo es la definición, no el JSON. Si algún día hace falta filtrar u ordenar por rangos de número o de fecha, esos campos pasan a una tabla con columnas por tipo, como las respuestas de los formularios.

**Qué viaja a WhatsApp (pedido del usuario, 2026-10-21).** En WhatsApp cada opción tiene un **título** (hasta 30 caracteres) y una **descripción** (hasta 300). Hoy son el texto y el detalle. Con campos extra, el catálogo elige **qué campos arman cada uno**. Por ejemplo, primero «texto» y «código», y más adelante «texto» y «CUIT · zona». Reglas:
- **El título no pasa de 30 caracteres.** Si la combinación elegida no entra en alguna opción, la pantalla lo avisa con las opciones afectadas, y la comprobación de publicación lo frena.
- **Un cambio vale para los envíos nuevos.** Lo ya enviado y las respuestas guardadas conservan lo que se mostró.
- **A Meta solo viaja lo que se muestra.** Los campos de filtro o de dato que no se eligieron no salen del sistema. El filtrado por persona se hace en el servidor, antes de enviar.

Falta decidir si esta presentación es solo del catálogo o si una pregunta del formulario puede cambiarla.

**Hoy no existen y no tienen tablero.** Son el siguiente diseño de catálogos, porque OCHA los va a necesitar para filtrar listas grandes.

**Abierto para el diseño: de dónde sale el dato de la persona.** Para que Juan vea «solo los de su zona», el sistema tiene que saber que Juan es de zona Centro. Opciones:
- **atributos propios de la persona** (Zona, Sucursal), definidos una vez y cargados en Usuarios;
- **roles**, como hoy hacen los catálogos de Filtros;
- **un filtro fijo de la pregunta,** que no depende de la persona. Por ejemplo, «solo rubro Frío».

## El asistente arma catálogos e importa

Sigue la regla de siempre: **la IA propone, una persona aprueba y el sistema ejecuta** ([ADR 0019](../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md), [ADR 0020](../decisions/0020-motor-unico-de-ia-con-capacidades.md)).

- **Crear a pedido.** Por ejemplo: «Creame un catálogo de proveedores con CUIT, razón social, rubro y zona». El asistente propone los campos y sus tipos, la persona los aprueba, el catálogo se crea inactivo y el asistente ofrece la plantilla.
- **Importar un Excel que ya tiene la persona:**
  - el servidor le pasa al modelo **solo los encabezados y unas pocas filas de muestra**, nunca el archivo entero;
  - el modelo propone el catálogo y el mapeo;
  - la persona confirma;
  - la importación la hace el sistema, por el mismo recorrido de la próxima etapa.
- **Desde la sección Catálogos:** una entrada del tipo «Pasale tu Excel al asistente».

## Más de 200 opciones

WhatsApp muestra hasta 200 opciones por pregunta en el desplegable y hasta 20 en elegir varias. Para catálogos grandes:
- **Filtrar por persona,** con los campos extra de filtro.
- **Búsqueda en vivo:** la persona escribe y WhatsApp consulta nuestras opciones. Necesita el [intercambio cifrado con Meta](formularios/README.md#después-de-la-etapa-5), que está pendiente. **Con las listas de OCHA, sube de prioridad.**

## Los orígenes

| Origen | Cómo funciona | Riesgo |
| --- | --- | --- |
| **1. Excel o CSV** | La próxima etapa. | Ninguno: no tocamos su sistema. |
| **2. Recepción por API** | El sistema del cliente nos manda la lista con una clave que solo sirve para ese catálogo. | Bajo: no guardamos credenciales ajenas. |
| **3. Conector en su red** | Un programa chico, junto a su base, lee la consulta configurada y nos manda la lista por el origen 2. **Solo hace conexiones de salida:** no hace falta VPN ni abrir puertos. | Medio: hay que distribuirlo y mantenerlo. |
| **4. Lectura de su API** | Configuramos dirección, autenticación, paginación y mapeo, y el sistema la lee cada cierto tiempo. | Medio: guardamos una credencial suya. |
| **5. Conexión directa a su base** | Solo si la app corre en su misma red. Consulta de solo lectura validada. | Alto. Necesita un ADR que amplíe el 0019. |

**Plantillas de origen.** Para sistemas conocidos, como Macrosistemas, se guarda la consulta de proveedores y su mapeo, lista para reusar en La Clementina u otro cliente con el mismo sistema. Son datos que se cargan, no código.

## Conectividad: el servidor del cliente o Azure

**Instalar en el servidor del cliente (IIS)** sirve para salir rápido, pero tiene dos condiciones sin resolver:
- **WhatsApp necesita llegar al sistema desde internet.** Meta llama a una dirección pública con HTTPS (el webhook). Un servidor al que solo se entra por VPN no recibe esos avisos sin exponer esa dirección o sin un túnel.
- **Todo lo demás tiene que correr ahí:** PostgreSQL, Redis, el almacenamiento de archivos y la salida a OpenAI. La [guía de despliegue](../guides/despliegue.md) es para Azure; no hay guía para IIS.

**Azure con el conector** escala a cualquier cliente:
- la app recibe a WhatsApp sin problemas;
- el conector, desde la red del cliente, lee su base y manda los datos hacia afuera por HTTPS;
- no hace falta VPN, túnel ni abrir puertos del lado del cliente.

**Si un cliente no acepta el conector:** una VPN de sitio a sitio con Azure (cara y con trabajo de red de los dos lados) o un relay de Azure.

**MCP** sirve para que un asistente consulte datos en vivo, como el stock o el saldo; encaja en la etapa de agentes del [plan del asistente](2026-10-21-casos-del-asistente.md). Para sincronizar catálogos no conviene: hace falta una carga controlada, validada y repetible, y eso lo da el conector.

## Sincronización y seguridad (orígenes externos)

- **Sincronización:** leer → validar → diferencias → aplicar → reporte, con las mismas protecciones que la importación de Excel.
- **Robustez:** cada lectura externa es una operación durable con lease, fence y reintentos con espera creciente ([ADR 0018][0018]). El estado se ve: «Actualizado hace 2 h · 1.240 opciones» o «Falló la última actualización: …».
- **Credenciales:** cifradas, nunca vuelven al front ni a los logs, y se pueden rotar.
- **Claves de recepción y del conector:** se ven una sola vez, se guardan como hash, se revocan y tienen alcance mínimo.
- **Permiso propio** para configurar orígenes externos.
- **Lectura de APIs:** solo HTTPS, sin redirecciones, con lista de hosts permitidos y topes de tiempo, tamaño y filas.
- **Lo que llega son datos:** se valida y nada se ejecuta.
- **Multitenant:** todo va por organización.

## Esquema de base (propuesta para después de la próxima etapa)

| Tabla | Qué guarda |
| --- | --- |
| `FormCatalogs` (existe) | Los **campos extra** del catálogo (JSON validado contra los tipos, con revisión) y el tipo de origen. |
| `FormCatalogItems` (existe) | El código externo (único por catálogo) y los **valores de los campos extra** (JSONB), con un índice para los de filtro. |
| `FormCatalogSources` (nueva) | Origen: tipo, configuración no secreta, secreto cifrado, frecuencia, estado, lease y fence. |
| `FormCatalogSourceTemplates` (nueva) | Plantillas de origen, como «Macrosistemas · Proveedores». |
| `FormCatalogSyncRuns` (nueva) | Una fila por sincronización: disparador, estado, contadores, hash del lote y motivo seguro. |
| `FormCatalogSyncIssues` (nueva) | Filas con errores: fila, columna y motivo, con retención limitada. |
| `FormCatalogSourceKeys` (nueva) | Claves de recepción y del conector, guardadas como hash. |

## Fases

| Fase | Qué incluye | Estado |
| --- | --- | --- |
| **Próxima** | Carga de una vez, tipo de valor y elegir varias | [Decidida, con tableros](formularios/catalogos-carga-tipo-y-varias.md) |
| **2** | Campos extra y filtrar por persona | Por diseñar |
| **3** | Búsqueda en vivo con Meta | Por diseñar (intercambio cifrado) |
| **4** | El asistente arma catálogos y mapea el Excel | Por diseñar |
| **5** | Recepción por API, conector para Macrosistemas y plantillas de origen | Por diseñar |
| **6** | Lectura de APIs; conexión directa solo si hace falta | Por diseñar |

## Preguntas abiertas

1. **Macrosistemas:** ¿qué motor y versión de base usa (SQL Server)? ¿Qué listas hacen falta primero (proveedores, artículos, sucursales, clientes) y cuántas filas tiene cada una?
2. **Primera instalación de OCHA:** ¿servidor del cliente o Azure? Si es su servidor, ¿cómo llega WhatsApp y quién mantiene PostgreSQL y Redis?
3. **Opciones que vienen de afuera:** ¿se pueden ocultar o renombrar localmente? Recomendado: solo ocultar.
4. **Formularios:** al elegir una opción, ¿se completan otras preguntas con sus campos extra?

## Relación con otros planes

- El [ADR 0019](../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md) sigue vigente para «Consulta» sobre nuestra base. Conectarse a la base del cliente necesita su propio ADR.
- El [intercambio cifrado con Meta](formularios/README.md#después-de-la-etapa-5) sube de prioridad por los catálogos grandes.
- Los [casos del asistente](2026-10-21-casos-del-asistente.md) comparten la idea de que la IA propone y el sistema ejecuta. MCP encaja en su etapa de agentes.
- En el multitenant, todo va por organización.

[0018]: ../decisions/0018-robustez-proporcional-al-efecto.md
