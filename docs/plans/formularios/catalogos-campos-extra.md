# Catálogos: campos extra, qué viaja a WhatsApp y filtrar por persona

**Estado:** decidida y con tableros elegidos el 2026-10-21 (CE1, CE2, CE3-B, CE4-A, CE5 y CE6). La evaluación de impacto, back y front, está incorporada abajo. Sin empezar. Va después de «[Catálogos: carga de una vez, tipo de valor y elegir varias](catalogos-carga-tipo-y-varias.md)», sobre la que se apoya: la tabla editable, importar y pegar, el valor tipado y la clave de referencias por opción de elegir varias. La visión completa está en la [hoja de ruta de catálogos](../2026-10-21-catalogos-con-origenes-externos.md).

**Spec técnica:** el modelo de datos, los contratos, los errores, el árbol de archivos y el orden de trabajo de esta etapa (E2) están en la [spec de catálogos](../../specs/2026-10-21-catalogos-design.md). Si la spec y este resumen se contradicen, manda este resumen.

## Para qué

Las listas reales tienen más datos que un código y un texto, y son grandes. Esta etapa permite:
- **definir columnas propias en cada catálogo**, por ejemplo CUIT, Zona, Rubro y Plazo de pago en Proveedores;
- **elegir qué se ve en WhatsApp** en cada opción;
- **mostrarle a cada persona solo lo que le corresponde:** Juan, de zona Centro, ve 14 de 1.200 proveedores.

**Es general, no de un cliente.** Los nombres de los campos y de los datos de las personas los define cada empresa: zona, sede, depósito, región o lo que sea. Ningún nombre de campo ni de dato de la persona va en el código. Vale para los catálogos «Escritas a mano»; los de Filtros no tienen opciones propias.

## Qué se construye

### CE1 · Definir los campos de un catálogo

- **Cada campo extra tiene:**
  - una clave (`cuit`), única en el catálogo, que es la que reconoce la plantilla y lo pegado;
  - un nombre en español e inglés;
  - un tipo:
    - texto, entero, decimal, fecha o sí/no;
    - **elegir de un catálogo**: los valores salen de otro catálogo «Escritas a mano», por ejemplo «Zonas» (ver «Una sola fuente de valores», abajo);
  - reglas: obligatorio, único, mínimo y máximo, y largo máximo;
  - un **papel**:
    - **filtro**: sirve para elegir qué opciones ve cada persona;
    - **dato**: se guarda y se muestra en las respuestas.
- **Los campos fijos siguen como hoy:** código, texto, detalle y, desde la etapa anterior, valor.
- **Se agregan, ordenan y quitan** en el editor del catálogo, con la revisión y el 409 del catálogo. Un menú «Campos» muestra u oculta columnas en la tabla.
- **Un campo nuevo:**
  - no se carga hasta guardarlo; su columna aparece apagada con «Guardá para cargar valores», como pegar e importar en la etapa anterior;
  - **nace opcional;** pasarlo a obligatorio exige que todas las opciones activas tengan valor, y si no, la pantalla dice cuántas faltan.
- **Quitar un campo:**
  - si está **en uso** (un filtro o lo que viaja a WhatsApp, en el borrador o en la versión activa de un formulario, o lo que viaja del catálogo), no se puede, y la pantalla dice dónde se usa;
  - si **tiene datos**, pide marcar una casilla;
  - un campo quitado queda inactivo, para no perder su nombre en lo histórico.
- **Cambiar el tipo** de un campo cargado lo valida el servidor contra todos los valores (hasta 10.000), y no deja guardar mientras alguno no convierta (`fields[i].type`).

### CE2 · Cargar los valores

- **La tabla editable** de la etapa anterior suma una columna por campo, con el error en la celda. Las columnas texto y acciones quedan fijas y el resto se desplaza dentro de su caja, con la indicación. En el celular sigue el editor por opción.
- **La plantilla de Excel, importar y pegar** suman las columnas de los campos, reconocidas por su **clave**. Cada valor se valida por tipo («Fila 37 · Plazo de pago: no es un número entero»). En un campo «elegir de un catálogo» se escribe el código o el texto de la opción, y el servidor lo resuelve.

### CE3-B · Qué viaja a WhatsApp: el catálogo, y la pregunta puede cambiarlo

- **En WhatsApp, cada opción tiene un título**, de hasta 30 caracteres, **y una descripción**, de hasta 300. Cada uno se arma con uno o más campos, fijos o extra, separados por « · ».
- **El catálogo define lo de siempre.** Si no se define, sigue lo de hoy: texto en el título y detalle en la descripción.
- **Una pregunta puede elegir:**
  - «Como en el catálogo» (por defecto): sigue al catálogo y cambia si el catálogo cambia;
  - «Propio de esta pregunta»: otros campos solo para ese formulario. Se publica como cualquier cambio de la pregunta.
- **Solo viaja lo elegido.** Los demás campos no salen del sistema, y el filtrado por persona se hace en el servidor, antes de mandar.
- **El título no pasa de 30:**
  - el editor avisa con las opciones que no entran, y se puede guardar;
  - la comprobación de publicar lo frena, diciendo si el problema es del catálogo o de la pregunta;
  - al mandar se vuelve a controlar.
- **Un cambio vale para los envíos nuevos:** lo ya enviado conserva lo que se mostró, porque queda sellado en la sesión.
- **Permisos:** en el catálogo, «Administrar catálogos»; en la pregunta, «Administrar formularios».

### CE4-A · Filtrar por persona: datos de las personas

- **«Datos de las personas»** es una pantalla nueva del módulo, que se ve en Administración:
  - ahí se definen los datos que tiene cada persona, por ejemplo Zona o Sucursal;
  - muestra una **tabla de personas con sus valores**;
  - su ruta es plana (`/datos-de-personas`), para no colgarse de Usuarios;
  - definir los datos pide un permiso propio del módulo, que se crea con la [guía de permisos](../../guides/permiso-nuevo.md).
- **«Editar usuario» suma la sección «Datos».** La aporta el módulo de formularios: el front la muestra solo si el módulo está instalado (`installedFeatures`), con un contrato neutral fuera de las features, como `layouts/formsOverviewNavigation.ts` del resumen de Formularios del menú. Se guarda aparte del usuario, por rutas propias del módulo (como `MeWhatsAppController`), y solo si cambió. Si falla, el diálogo queda abierto con el error en la sección. Cargar los valores pide «Administrar usuarios».
- **La columna «Zona» en la lista de usuarios no entra en esta etapa:** la tabla de personas cumple ese papel.
- **Los filtros de una pregunta de catálogo** («Qué opciones ve cada persona») actúan sobre los campos de papel «filtro»:
  - **según quien lo recibe:** «Zona del proveedor = la zona de quien lo recibe». Solo entre un campo y un dato que salen del **mismo catálogo de valores**, comparando la opción;
  - **fijo:** «Rubro = Frío», igual para todos;
  - el operador es siempre «es igual a».
- **En el editor:** «Como si lo recibiera: Juan Gómez · Zona Centro → 14 opciones». Usa una lista de personas de ejemplo con su propia ruta y el permiso de administrar formularios (la de los catálogos de Filtros exige administrar catálogos; ver la [spec](../../specs/2026-10-21-catalogos-design.md)). Le muestra a quien administra formularios el dato de esa persona.
- **La comprobación de publicar:**
  - frena lo que no puede funcionar: un campo inexistente o inactivo, un papel o tipo que no corresponde, un título de más de 30;
  - suma **avisos que no frenan**, en una lista aparte de los problemas: el máximo de opciones por persona («como mucho 18, Zona Sur») contra el tope de WhatsApp (200 en el desplegable, 20 en elegir varias), y quién quedaría sin poder recibirlo por no tener el dato.
- **Al mandar a alguien sin el dato,** la preparación falla con un motivo estable y no se manda nada; nunca va la lista entera. Si el campo o el dato del filtro quedaron inactivos, también falla: el filtro nunca se quita solo.
- **El asistente** usa la misma preparación, así que respeta los filtros sin cambios propios.

### CE5 · Las respuestas

- **Al capturar la respuesta se sellan los campos extra de la opción elegida,** con su id, nombre (es/en), tipo, papel y valor. Si el catálogo cambia después, la respuesta conserva lo que era.
- **Planilla:**
  - cada pregunta de catálogo suma subcolumnas, por ejemplo «Proveedor · CUIT»;
  - los campos de dato se ven por defecto y los de filtro se prenden en «Columnas»;
  - las subcolumnas **solo se muestran:** no se ordenan, ni filtran, ni cuentan en las facetas.
- **Detalle:** se ven todos los campos sellados.
- **Excel:** trae todos los campos, con su tipo. Con «solo columnas visibles», sigue a las columnas visibles.
- **PDF:** muestra los campos de dato junto a la opción.
- **Chat:** la tarjeta queda como hoy.
- **Inglés:** con los nombres en inglés si están cargados. Un nombre sin traducir no frena el idioma del PDF ni del envío.

### CE6 · El circuito

1. Crear el catálogo «Zonas».
2. Definir en Proveedores CUIT, Zona (elegir de «Zonas») y Rubro.
3. Pegar 1.200 proveedores.
4. Elegir que viaje «texto + CUIT».
5. Cargarle a Juan su zona.
6. Usar Proveedores en «Pedir repuesto», filtrado por zona.
7. Juan ve sus 14 en el celular.
8. La respuesta, con el CUIT en la planilla.

## Una sola fuente de valores

Una lista de valores (zonas, rubros, sucursales) es **un catálogo «Escritas a mano» más**. Tanto el campo «Zona» de Proveedores como el dato «Zona» de las personas son «elegir de un catálogo» sobre «Zonas». Así:
- no hay dos editores de listas;
- se cargan e importan como cualquier catálogo;
- tienen sus nombres en español e inglés;
- «Zona del proveedor = la zona de quien lo recibe» compara siempre **la misma opción**.

Se guarda el id de la opción, no su texto, y cambiar el texto o el código de una zona no rompe nada. Un catálogo usado como fuente de valores no se puede dar de baja mientras un campo o un dato lo use.

## Modelo de datos

Sigue las reglas de siempre:
- todo dentro de BusinessForms y sin FK a las tablas del núcleo;
- configuración con revisión y 409 ([ADR 0018][0018]);
- SQL fijo y parametrizado, nunca configurable ([ADR 0019][0019]);
- los campos nuevos de lo ya publicado y sellado llevan `JsonIgnore(WhenWritingNull)`, así no cambian sus huellas.

| Dónde | Nuevo o existente | Qué guarda |
| --- | --- | --- |
| `BusinessFormCatalogs.FieldsJson` | Nuevo, en la tabla existente | Los campos extra, en el mismo agregado y con la misma revisión que el catálogo, como `FiltersJson`. Cada campo: id estable, clave, nombre es/en, tipo (con el catálogo fuente si es «elegir de un catálogo»), reglas, papel, orden y activo. Domain valida la unicidad de la clave y el «inactivo conserva el nombre». |
| `BusinessFormCatalogs.PresentationJson` | Nuevo, en la tabla existente | Lo que viaja a WhatsApp por defecto: `{ título: [campos], descripción: [campos] }`. Los campos son `text`, `code`, `detail`, `value` (solo en catálogos tipados) o el id de un campo extra. El título no puede quedar vacío. `null` es lo de hoy. |
| `BusinessFormCatalogItems.FieldValuesJson` | Nuevo, en la tabla existente | `jsonb NOT NULL DEFAULT '{}'`, `{ "<id del campo>": valor }`, con índice GIN `jsonb_path_ops`. **Formato canónico único,** porque la contención compara JSON tipado: fechas `AAAA-MM-DD`, decimales con la normalización que ya usa el prellenado, sí o no como `true`/`false`, y el id de la opción en «elegir de un catálogo». Obligatorio y único se controlan en Application, bajo el lock del catálogo. |
| `BusinessFormPersonAttributes` | Nueva | Los datos de las personas: id, clave, nombre es/en, tipo («elegir de un catálogo» o texto), catálogo fuente, orden, activo y revisión. Un dato usado por un filtro no se quita ni cambia de tipo. |
| `BusinessFormPersonData` | Nueva | Una fila por persona: `UserId` (único), `ValuesJson` (`{ "<id del dato>": valor }`) y su revisión, para el 409 por persona. |
| `FormQuestionSource` (en el esquema de la versión) | Existente | Suma, al final y con `JsonIgnore(WhenWritingNull)`: `Match`, los filtros (`{ campo, valor fijo }` o `{ campo, dato de la persona }`, uno por campo y con tope; `Normalize` los ordena y una lista vacía pasa a `null`), y `Presentation` (`null` = «Como en el catálogo»). Se llama `Match` para no confundirlo con los catálogos de Filtros. |
| Oferta de la sesión (`StructuredFormCatalogOption`) | Existente | Suma `Shown { Title, Description }` opcional, sellado al mandar. WhatsApp usa `Shown?.Title ?? Text`. `Text` sigue siendo el del ítem, y la planilla y el chat no cambian. |
| `BusinessFormRecordReferences.FieldValuesJson` | Nuevo, en la tabla existente | Los campos de la opción elegida, sellados al capturar con id, nombre es/en, tipo, papel y valor. Se leen por lote bajo el lock de la captura, para no cargar 200×20 valores en la sesión de WhatsApp. |

**La compilación del canal de WhatsApp no se toca:** `StructuredFormQuestion` no suma nada, así que no cambian la compilación, la huella de publicación de WhatsApp ni sus constructores. Lo único que cambia en WhatsApp es el mensaje, que usa `Shown` (`WhatsAppMessagePayload`), y el chat del front, que suma el motivo nuevo de falla.

**Cómo se resuelve un envío.** Todo pasa por un solo punto, `FormCatalogOptionsPolicy.PrepareAsync`, que también usan los candidatos, el prellenado, la aclaración y las sugerencias del asistente:
1. lee los datos de la persona que recibe;
2. trae las opciones activas con un lector que filtra por contención: `ListOfferItemsAsync(catalogId, contención)`, con `EF.Functions.JsonContains` y el mismo tope de 201;
3. controla el tope de WhatsApp;
4. arma título y descripción con una política pura (`FormCatalogPresentationPolicy`), la misma del editor, la comprobación y «Como si lo recibiera»;
5. sella todo en la sesión.

Los motivos estables de falla son `Forms.Record.RecipientDataMissing` y `Forms.Record.CatalogFilterUnavailable`.

**Topes iniciales,** que se ajustan si un cliente lo necesita:
- 20 campos extra por catálogo;
- textos de hasta 300 caracteres;
- un filtro por campo;
- 20 datos de las personas.

Las 10.000 opciones por catálogo siguen como en la etapa anterior.

**Si más adelante hace falta filtrar u ordenar por rangos,** esos campos pasan a una tabla con columnas por tipo, como las respuestas. No entra ahora.

## Decisiones tomadas

No se vuelven a preguntar.

- **Tableros:** CE1, CE2, **CE3-B**, **CE4-A**, CE5 y CE6, en la versión 106 del lienzo, marcados como elegidos en la 107 ([diseño del front][tableros]). CE3-A y CE4-B quedan como alternativas no elegidas.
- **Lo fijado al dibujar,** confirmado al elegir:
  - en la planilla, los datos se ven por defecto y los filtros, en «Columnas»;
  - el Excel trae todos y el PDF, los de dato;
  - el chat queda como hoy;
  - un campo en uso no se quita, y quitar uno con datos pide una casilla;
  - cambiar el tipo revisa los valores;
  - a alguien sin el dato no se le manda.
- **Ajustes de la evaluación de impacto,** el 2026-10-21:
  - una sola fuente de valores;
  - `FieldsJson` en el catálogo en lugar de otra tabla;
  - una fila de datos por persona;
  - los filtros se llaman `Match` y comparan la misma opción;
  - se sella al capturar, y lo mostrado queda en la sesión;
  - avisos que no frenan en la comprobación de publicar;
  - sin columna en la lista de usuarios;
  - un campo nuevo nace opcional y se carga después de guardarlo;
  - «en uso» mira el borrador y la versión activa;
  - el detalle muestra todos los campos;
  - el Excel con «solo visibles» sigue a las columnas.
- **General:** ningún nombre de campo ni de dato de la persona va en el código, y los datos de las personas son del módulo de formularios.

## Fuera de esta etapa

- Varios valores de un mismo dato para una persona (alguien que atiende dos zonas).
- Operadores distintos de «es igual a»: rangos, «contiene», «alguna de».
- Completar otras preguntas con los datos de la opción elegida, por ejemplo el CUIT.
- La columna de datos en la lista de usuarios.
- La búsqueda en vivo con Meta, el asistente que arma catálogos y los orígenes externos: están en la [hoja de ruta](../2026-10-21-catalogos-con-origenes-externos.md).

## Cómo se encara

**Arreglos previos, sin cambio visible.**

En el back:
- **Sin esto, toda edición de una pregunta con filtros falla.** `FormDraftPolicy.cs:70` y `:72` comparan `Source` entero; pasan a comparar solo `Source?.Catalog`, porque una lista dentro del record nunca da igual.
- **Un solo mapeo de `FormQuestionSource`,** que hoy está repetido en `FormDefinitionsController`, `FormPublicationChecksController` y `FormPublicationsController`. Con parámetros opcionales, los filtros se perderían sin avisar.
- La política de presentación.
- El lector de ofertas con filtro.
- `FindUsageCounts`, que pasa a mirar también la versión activa.

En el front (conviene meterlos al programar la etapa anterior):
- la tabla y el editor por opción armados a partir de una descripción de las columnas;
- una pieza común para leer, formatear y validar cada tipo;
- comparar las opciones del borrador por estructura;
- ubicar los errores `items[i].fields.<id>` y `fields[i].*` sin expresiones regulares fijas;
- separar la configuración de la pregunta de catálogo de `QuestionEditor`;
- la variante de tabla ancha en `shared/ui`.

**Cortes:**
1. **El catálogo:** campos, valores, lo que viaja por defecto y el sellado al capturar (CE1, CE2 y la parte del catálogo de CE3). Se reexportan juntas todas las cohortes de catálogos.
2. **Datos de las personas:** la pantalla, la tabla de personas y la sección de «Editar usuario».
3. **La pregunta:** `Match` y lo propio que viaja a WhatsApp juntos, con las pruebas de huellas, «Como si lo recibiera», la comprobación con avisos, la preparación al mandar, `Shown` en WhatsApp y el motivo nuevo en el chat (CE3-B y CE4-A).
4. **Las respuestas:** planilla, detalle, Excel y PDF (CE5).

## Cómo se cierra

- **Checks:** por corte y al cerrar, según [validar un cambio](../../guides/validar-un-cambio.md), con TDD donde hay lógica.
- **Integración con Docker:**
  - migraciones con datos;
  - validación por tipo al guardar, pegar e importar, y el formato canónico;
  - cambiar el tipo con valores que no convierten;
  - pasar un campo a obligatorio con opciones sin valor;
  - quitar un campo en uso en el borrador y en la versión activa;
  - un catálogo fuente en uso que no se da de baja;
  - filtros fijos y por persona con el SQL parametrizado;
  - envío bloqueado a alguien sin el dato, y con un campo o dato inactivo;
  - la comprobación con los avisos y el máximo por persona;
  - el título de más de 30 en el catálogo y en la pregunta;
  - que solo viaje lo elegido, revisando el payload a Meta;
  - el sellado en la respuesta después de editar el catálogo;
  - **huellas:**
    - el hash de una versión vieja guardada tal cual;
    - una publicación en curso;
    - el replay de una sesión vieja;
    - editar filtros conserva el `FieldId`;
    - comprobar y aceptar con los filtros desordenados;
  - el asistente con una persona sin el dato;
  - 10.000 opciones con 20 campos, al guardar y al filtrar;
  - el módulo quitado, sin la ruta, la capacidad ni la sección «Datos»;
  - rutas, OpenAPI y ejemplos exportados.
- **Front:** contratos con los ejemplos exportados, visuales y los tableros en el navegador, es/en, a 390 y 320 px. El [navegable](https://claude.ai/artifact/2noVifPc2MX33UnNvT2jhY) sirve de guía.
- **Piloto real con Meta:** Proveedores con Zona y CUIT, filtrados para dos personas de zonas distintas. Cada una ve lo suyo en el celular, y la respuesta muestra el CUIT.

**Pendiente aparte:** la guía `quitar-formularios.md` ([planes](../README.md)), que esta etapa vuelve más necesaria.

[0018]: ../../decisions/0018-robustez-proporcional-al-efecto.md
[0019]: ../../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md
[tableros]: ../../../../ArquitecturaBaseFront/docs/design/catalogos-campos-extra.md#los-tableros
