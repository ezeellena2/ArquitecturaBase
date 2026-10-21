# Catálogos con campos tipados, importación y orígenes externos

**Estado:** borrador para pensar, del 2026-10-21, con las definiciones del usuario que siguen. No hay tableros, y no se programa hasta que el usuario lo defina.

## Lo que definió el usuario

No se vuelve a preguntar.

- **Primer cliente: OCHA S.A.** Usa una base con el esquema de **Macrosistemas**, el mismo que La Clementina. Hace falta traer sus listas (por ejemplo, proveedores) y, más adelante, un conector a esa base.
- **Primer paso: importar desde Excel**, con una **plantilla definida por el catálogo**. Solo se importa lo que encaja en la plantilla.
- **Tipos de datos validados al cargar:** texto, entero, decimal, fecha, sí o no, y los que sigan.
- **Nada fijo en el código:** el catálogo, sus campos y sus tipos son configuración. En el código vive solo el conjunto de tipos y el mecanismo de importación y validación, igual que el menú de acciones del asistente.
- **Más de 200 opciones:** los catálogos pueden ser grandes.
- **Quién configura los orígenes:** las dos posibilidades, nosotros en la implementación y el cliente.
- **El asistente ayuda:**
  - arma un catálogo a pedido y devuelve su plantilla;
  - lee el Excel que sube la persona, propone a qué catálogo va y cómo se mapea, y lo importa cuando la persona confirma;
  - en la sección Catálogos hay una entrada o una guía para usarlo.
- **El sistema va a vivir en Azure.** Lo que se diseñe tiene que permitir que cualquier sistema se conecte, no solo Macrosistemas. La primera instalación puede ser en el servidor del cliente, o en Azure con una conexión a su red.

## Hoy

- **Hay dos tipos de catálogo:** Manual, que se carga en la app, y Filtros, que sale de los usuarios y roles del sistema.
- **Un ítem tiene campos fijos:** código, texto en español e inglés (hasta 30 caracteres, por el límite de WhatsApp), detalle (hasta 300), orden y si está activo. No hay campos propios ni tipos de datos.
- **No hay importación ni conexión externa.** El tipo «Consulta» (SQL) se descartó por ahora en el [ADR 0019](../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md), que además solo lo admite sobre nuestra base.
- **El chat administrativo** (el asistente de Inicio) ya propone catálogos de tipo Filtros. Quedan inactivos hasta que una persona los aprueba ([catálogos propuestos](../features/formularios.md#catálogos-propuestos--etapa-5-h4)). Es la base para la ayuda del asistente.
- **Una pregunta muestra hasta 200 opciones en WhatsApp.**

## La idea central: el origen alimenta una copia local

Cualquiera sea el origen (Excel, una API, una base), las opciones se guardan en una **copia local** dentro de nuestra base, y **los formularios siempre leen esa copia**. Nunca se consulta el sistema del cliente al mandar un formulario. Así:
- si el sistema del cliente se cae, los formularios siguen andando;
- lo que deja de venir queda inactivo, no se borra;
- cambiar de origen no toca formularios, WhatsApp ni el asistente.

## Campos tipados

Cada catálogo define sus **campos**, con revisión y 409 como el resto de la configuración:

| Atributo del campo | Ejemplo |
| --- | --- |
| Clave y nombre (es/en) | `cuit` · «CUIT» |
| Tipo | texto, entero, decimal, fecha, fecha y hora, sí o no, lista de valores, correo, teléfono |
| Reglas | obligatorio, único, largo máximo, mínimo y máximo, decimales, formato |
| Papel | **código** (identifica la opción), **texto** (lo que se ve en WhatsApp, hasta 30), **detalle**, **filtro** (para mostrarle a cada persona solo lo suyo) o **dato** (se guarda y se puede usar después) |

- **Los tipos son un conjunto cerrado en el código;** los catálogos y sus campos son datos. Agregar un tipo nuevo es programar una opción más, como en el menú del asistente.
- **Validación al importar:**
  - fechas en varios formatos, incluida la fecha numérica de Excel;
  - decimales con coma o punto, según la cultura del catálogo;
  - «sí», «no», «1», «0», «true» y «false» para los valores sí o no.
- **Cada error se informa con su fila, su columna y el motivo.**
- **Más adelante:** al elegir una opción en un formulario, otras preguntas se pueden completar con sus datos (por ejemplo, el CUIT del proveedor). No entra en este plan.

## Importar desde Excel (primer paso)

1. **Descargar la plantilla del catálogo:** un Excel con una columna por campo, su tipo, si es obligatorio y una fila de ejemplo.
2. **Subir la planilla completa.**
3. **Validar:** tipos y reglas por celda; códigos repetidos y obligatorios faltantes.
4. **Ver las diferencias:** altas, cambios, bajas y filas con errores.
5. **Confirmar.** Se aplica en una transacción y queda en el historial.

Reglas:
- **El reemplazo es completo:** lo que no viene en la planilla se inactiva, y lo que vuelve se reactiva.
- **Protecciones:** una planilla vacía, una que daría de baja una parte grande del catálogo o una con demasiados errores no se aplica sin confirmación.
- **Una misma planilla no se aplica dos veces.**
- **El archivo:** se lee en el servidor con topes de tamaño, filas y columnas, sin ejecutar fórmulas ni macros. Se guarda privado y se retiene un tiempo limitado.

## El asistente arma catálogos e importa

Sigue la regla de siempre: **la IA propone, una persona aprueba y el sistema ejecuta** ([ADR 0019](../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md), [ADR 0020](../decisions/0020-motor-unico-de-ia-con-capacidades.md)).

- **Crear a pedido.** Por ejemplo: «Creame un catálogo de proveedores con CUIT, razón social, rubro y zona». El asistente propone los campos y sus tipos; la persona los revisa y aprueba; el catálogo se crea inactivo y el asistente ofrece la plantilla.
- **Importar un Excel que ya tiene la persona:**
  - el servidor lee la planilla y le pasa al modelo **solo los encabezados y unas pocas filas de muestra**, nunca el archivo entero;
  - el modelo propone a qué catálogo va (o uno nuevo), el mapeo de columnas y los tipos;
  - la persona ve la vista previa y confirma;
  - la importación la hace el sistema, no el modelo.
- **Entrada desde la sección Catálogos:** un botón o una guía del tipo «Pasale tu Excel al asistente y te arma el catálogo», que abre el chat con ese contexto.

## Más de 200 opciones

WhatsApp muestra hasta 200 opciones por pregunta. Para catálogos grandes:
- **Filtrar por persona:** con los campos de papel «filtro», como «proveedores de mi zona».
- **Búsqueda en vivo:** la persona escribe y WhatsApp consulta nuestras opciones. Necesita el [intercambio cifrado con Meta](formularios/README.md#después-de-la-etapa-5), que está pendiente. **Con catálogos de OCHA de más de 200 opciones, sube de prioridad.**

## Los orígenes

| Origen | Cómo funciona | Riesgo |
| --- | --- | --- |
| **1. Excel o CSV** | Lo de arriba. | Ninguno: no tocamos su sistema. |
| **2. Recepción por API** | El sistema del cliente nos manda la lista con una clave que solo sirve para ese catálogo, con el mismo formato de campos. | Bajo: no guardamos credenciales ajenas. |
| **3. Conector en su red** | Un programa chico, instalado junto a su base, lee la consulta configurada y nos manda la lista por el origen 2. **Solo hace conexiones de salida:** no hace falta VPN ni abrir puertos. | Medio: hay que distribuirlo y mantenerlo. |
| **4. Lectura de su API** | Configuramos dirección, autenticación, paginación y mapeo, y el sistema la lee cada cierto tiempo. | Medio: guardamos una credencial suya. |
| **5. Conexión directa a su base** | Solo si la app corre en su misma red. Consulta de solo lectura validada. | Alto. Necesita un ADR que amplíe el 0019. |

**Plantillas de origen.** Para sistemas conocidos, como Macrosistemas, se puede guardar una **plantilla de origen**: la consulta de proveedores y su mapeo a campos, lista para reusar en La Clementina u otro cliente con el mismo sistema. Son datos que se cargan, no código.

## Conectividad: el servidor del cliente o Azure

**Instalar en el servidor del cliente (IIS)** sirve para salir rápido, pero tiene dos condiciones que hoy no están resueltas:
- **WhatsApp necesita llegar al sistema desde internet.** Meta llama a una dirección pública con HTTPS, el webhook. Un servidor al que solo se entra por VPN no recibe esos avisos sin exponer esa dirección (DMZ o proxy inverso) o sin un túnel.
- **Todo el resto tiene que correr en ese servidor:** PostgreSQL, Redis, el almacenamiento de archivos y la salida a OpenAI. Hoy la [guía de despliegue](../guides/despliegue.md) es para Azure; no hay guía para IIS.

**Azure con el conector** es lo que escala a cualquier cliente:
- la app vive en Azure y recibe a WhatsApp sin problemas;
- en la red del cliente se instala el conector, que lee su base y manda los datos hacia afuera por HTTPS;
- no hace falta VPN, túnel ni abrir puertos del lado del cliente.

**Otras formas de llegar a una base cerrada**, si un cliente no acepta el conector:
- una VPN de sitio a sitio con Azure: cara y con trabajo de red de los dos lados;
- un relay de Azure.

**MCP.** Es un protocolo para que un asistente de IA consulte datos o use herramientas de otro sistema. Sirve para que el asistente pregunte algo en vivo, como el stock o el saldo de un cliente: eso encaja en la etapa de agentes del [plan del asistente](2026-10-21-casos-del-asistente.md). **Para sincronizar catálogos no conviene:** hace falta una carga controlada, validada y repetible, y eso lo da el conector.

## Sincronización y seguridad (todos los orígenes)

- **Sincronización:** leer → validar → diferencias → aplicar → reporte, con las mismas protecciones que el Excel.
- **Robustez:** cada lectura externa es una operación durable con lease, fence y reintentos con espera creciente ([ADR 0018][0018]). El estado se ve: «Actualizado hace 2 h · 1.240 opciones» o «Falló la última actualización: …».
- **Credenciales:** cifradas, nunca vuelven al front ni a los logs, y se pueden rotar.
- **Claves de recepción y del conector:** se ven una sola vez, se guardan como hash, se revocan y tienen alcance mínimo.
- **Permiso propio** para configurar orígenes externos.
- **Lectura de APIs:** solo HTTPS, sin redirecciones, con lista de hosts permitidos y topes de tiempo, tamaño y filas.
- **Lo que llega son datos:** se valida y nada se ejecuta.
- **Multitenant:** todo va por organización.

## Esquema de base (propuesta)

| Tabla | Qué guarda |
| --- | --- |
| `FormCatalogs` (existe) | Suma el tipo `Imported`/`External` y los **campos** del catálogo (JSON validado contra los tipos, con revisión). |
| `FormCatalogItems` (existe) | Suma el código externo (único por catálogo) y los **valores de los campos** (JSONB validado por el servidor), con un índice para los campos de filtro. Texto y detalle siguen como columnas, para WhatsApp. |
| `FormCatalogSources` (nueva) | Origen: tipo, configuración no secreta, secreto cifrado, frecuencia, estado, lease y fence. |
| `FormCatalogSourceTemplates` (nueva) | Plantillas de origen reutilizables, como «Macrosistemas · Proveedores». |
| `FormCatalogSyncRuns` (nueva) | Una fila por importación o sincronización: disparador, estado, contadores, hash del lote y motivo seguro. |
| `FormCatalogSyncIssues` (nueva) | Filas con errores: fila, columna y motivo, sin datos sensibles y con retención limitada. |
| `FormCatalogSourceKeys` (nueva) | Claves de recepción y del conector, guardadas como hash. |

Los lotes grandes se cargan primero en una tabla de paso y se aplican en partes.

## Pantallas (para los tableros)

- **Catálogo nuevo:**
  - elegir cómo nace: a mano, con Filtros, con campos propios o pidiéndoselo al asistente;
  - el editor de campos: tipo, reglas y papel.
- **Importar:** descargar la plantilla, subir, ver los errores por fila y columna, ver las diferencias y confirmar.
- **El asistente en Catálogos:** la entrada «Pasale tu Excel al asistente», la propuesta de campos y mapeo, y la aprobación.
- **Detalle del catálogo:** campos, opciones (con búsqueda, porque pueden ser miles), historial de importaciones y estado del origen.
- **Origen externo:** configuración por pasos (conexión, prueba, mapeo, frecuencia, activar) y la pantalla con los datos para el conector o para el área de sistemas.

## Complejidad y fases

| Fase | Qué incluye | Tamaño |
| --- | --- | --- |
| **1** | Campos tipados, plantilla, importación de Excel con validación y diferencias, historial | Grande, pero es la base de todo |
| **1b** | El asistente arma el catálogo y mapea el Excel | Mediana, sobre la 1 y los catálogos propuestos |
| **2** | Búsqueda en vivo con Meta para más de 200 opciones | Mediana a grande |
| **3** | Recepción por API, conector para Macrosistemas y plantillas de origen | Grande |
| **4** | Lectura de APIs; conexión directa solo si hace falta | Mediana a grande |

## Preguntas abiertas

1. **Macrosistemas:** ¿qué motor y versión de base usa (SQL Server)? ¿Qué listas hacen falta primero (proveedores, artículos, sucursales, clientes) y cuántas filas tiene cada una?
2. **Primera instalación de OCHA:** ¿servidor del cliente o Azure? Si es su servidor, ¿cómo llega WhatsApp (dirección pública o túnel) y quién mantiene PostgreSQL y Redis?
3. **Opciones externas:** ¿se pueden ocultar o renombrar localmente? Recomendado: solo ocultar.
4. **Excel:** ¿una hoja por catálogo? ¿Se acepta CSV desde el principio?
5. **Formularios:** ¿una pregunta de catálogo muestra el detalle o completa otras preguntas con los datos de la opción? Queda para después de la fase 1.

## Relación con otros planes

- El [ADR 0019](../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md) sigue vigente para «Consulta» sobre nuestra base. Conectarse a la base del cliente necesita su propio ADR.
- El [intercambio cifrado con Meta](formularios/README.md#después-de-la-etapa-5) sube de prioridad por los catálogos grandes.
- Los [casos del asistente](2026-10-21-casos-del-asistente.md) comparten la idea de que la IA propone y el sistema ejecuta. MCP encaja en su etapa de agentes.
- En el multitenant, todo va por organización.

[0018]: ../decisions/0018-robustez-proporcional-al-efecto.md
