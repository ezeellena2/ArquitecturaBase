# Catálogos de formularios: carga, tipo de valor, elegir varias, campos extra y filtrar por persona — Diseño técnico

## 0. Encabezado

**Estado: diseño histórico.** E1 y E2 están implementados; las reglas y la verificación vigentes están en [formularios](../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23). Este documento conserva el diseño del 2026-10-21: su árbol de archivos, supuestos y pendientes reflejan ese momento, no el contrato actual. Las propuestas futuras siguen en la [hoja de ruta](../plans/2026-10-21-catalogos-con-origenes-externos.md).

**Prevalecen las reglas canónicas de formularios y sus DTO/OpenAPI.** El brief terminado de E2 conserva su [procedencia en Git](../history/README.md#catálogos-campos-extra-y-filtros-por-persona--e2-2026-10-23).

- **Fuentes vigentes:**
  - E1 · [reglas implementadas](../features/formularios.md#catálogos-base-de-la-etapa-e1--2026-10-21) y [verificación local](../features/formularios.md#verificación-local-de-catálogos-e1--2026-10-22);
  - E2 · [campos extra, qué viaja a WhatsApp y filtrar por persona](../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23).
- **Contexto:** la [hoja de ruta de catálogos](../plans/2026-10-21-catalogos-con-origenes-externos.md). Lo que está ahí y no en los resúmenes queda fuera.
- **Tableros elegidos:** [CA1-A y CA2–CA6](../../../ArquitecturaBaseFront/docs/design/catalogos-mejoras.md#los-tableros) y [CE1, CE2, CE3-B, CE4-A, CE5 y CE6](../../../ArquitecturaBaseFront/docs/design/catalogos-campos-extra.md#los-tableros), con sus ajustes de [claridad](../../../ArquitecturaBaseFront/docs/design/catalogos-mejoras.md#ajuste-de-claridad-2026-10-21).
- **Navegable:** https://claude.ai/artifact/2noVifPc2MX33UnNvT2jhY (guía de los recorridos, no es fuente).
- **Evaluación de impacto (E2):** el [modelo de datos ajustado](../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23), los [ajustes en las decisiones](../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23) y el [ajuste de pantallas](../../../ArquitecturaBaseFront/docs/design/catalogos-campos-extra.md#ajuste-de-la-evaluación-de-impacto-2026-10-21).
- **Reglas que se aplican:** [AGENTS.md](../../AGENTS.md), [backend.md](../architecture/backend.md), [ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md), [ADR 0019](../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md) y [ADR 0020](../decisions/0020-motor-unico-de-ia-con-capacidades.md).

**Convenciones de esta spec:**
- **E1** y **E2** son las dos etapas. Los cortes son E1-0, E1-B, E1-A, E1-C1…C4 y E2-0, E2-1…E2-4, como en los resúmenes.
- **(nuevo)**, **(cambia)** y **(se retira)** describen el árbol previsto al diseñar esta spec; el estado actual de E1 está en sus fuentes canónicas. Los nombres siguen la convención de su carpeta.
- **Fijado acá:** lo que el resumen no dice y esta spec resuelve con la opción más simple.
- **A confirmar:** lo que no se pudo comprobar en el código o depende de Meta. Están juntos en la sección 14.

## 1. Objetivo

Que un catálogo «Escritas a mano» sirva para las listas reales de OCHA S.A.: miles de filas, con tipo, con columnas propias y filtradas por persona.

Terminada cuando, con la app local y el número de prueba:
1. un catálogo de proveedores se pega, se exporta, se edita en Excel y se vuelve a importar, con errores por fila y cero diferencias al reimportar lo exportado;
2. un catálogo decimal llega a la respuesta con su valor, en la planilla, el Excel y el PDF;
3. una pregunta de elegir varias llega al celular como casillas y vuelve como lista;
4. Proveedores tiene CUIT, Zona y Rubro, y el título en WhatsApp es «texto · CUIT»;
5. Juan, de zona Centro, ve sus 14 de 1.200 proveedores; a Pablo, sin zona, no se le manda;
6. la respuesta muestra el CUIT sellado en la planilla, el detalle, el Excel y el PDF.

## 2. Alcance

### E1 · Carga de una vez, tipo de valor y elegir varias

- **E1-0, arreglos previos:**
  - unicidad diferida del código;
  - `items[i].code` en el error;
  - tope de 10.000;
  - prellenado con varios perfiles;
  - listas de WhatsApp clasificadas por su mapeo;
  - cuatro piezas del front.
- **E1-B, tipo de valor:** texto, entero, decimal o fecha, elegido al crear. El valor se sella en la respuesta y sale en el Excel y el PDF.
- **E1-A, carga de una vez:**
  - tabla editable con búsqueda, páginas y selección;
  - pegar, importar, exportar y la plantilla;
  - ordenar A–Z y generar los códigos que faltan;
  - «Así lo ve el empleado».
- **E1-C, elegir varias:** un tipo de pregunta nuevo (`multiChoice`) con mínimo y máximo, `CheckboxGroup` en WhatsApp con el perfil r9, varias filas por pregunta en las respuestas y «alguna de» en filtros y reglas.

### E2 · Campos extra, qué viaja a WhatsApp y filtrar por persona

- **E2-0, arreglos previos:**
  - comparar `Source?.Catalog`;
  - un solo mapeo de `FormQuestionSource`;
  - la política de presentación;
  - el lector de ofertas con contención;
  - el uso que mira la versión activa;
  - seis piezas del front.
- **E2-1, el catálogo:**
  - `FieldsJson` y `FieldValuesJson`;
  - `PresentationJson` («En WhatsApp» del catálogo);
  - el sellado de los campos al capturar.
- **E2-2, datos de las personas:** dos tablas nuevas, la pantalla `/datos-de-personas` y la sección «Datos» de «Editar usuario».
- **E2-3, la pregunta:**
  - `Match` y `Presentation` en `FormQuestionSource`;
  - «Como si lo recibiera» y la comprobación con avisos;
  - la preparación filtrada y `Shown` en la oferta;
  - el motivo nuevo en el chat.
- **E2-4, las respuestas:** subcolumnas, detalle, Excel y PDF con los campos sellados.

### Lo que queda afuera

- Búsqueda en vivo con Meta (más de 200 o 20 opciones sin filtrar).
- El asistente que arma catálogos o mapea un Excel. Tampoco prellena ni mapea sugerencias en preguntas de varias.
- Orígenes externos, conector a Macrosistemas y sincronización.
- Completar otras preguntas con los datos de la opción elegida.
- Varios valores de un dato por persona; operadores distintos de «es igual a»; rangos.
- La columna de datos en la lista de usuarios.
- Cambiar el tipo de valor de un catálogo ya creado.

## 3. Decisiones

Resumidas. El detalle está en su fuente y no se vuelve a preguntar.

| Decisión | Fuente |
| --- | --- |
| La carga de a una se conserva; pegar, importar y la tabla editable se suman. | [E1 · Base](../features/formularios.md#catálogos-base-de-la-etapa-e1--2026-10-21) |
| Hasta 10.000 opciones por catálogo, fijo. WhatsApp muestra 200 en el desplegable y 20 en el de varias; si una persona recibiría más, el envío se bloquea, nunca se corta. | E1 · Decisiones |
| Pegar es importar: el front interpreta y manda por la misma vista previa. El archivo reemplaza; pegar agrega. Lo que no viene se inactiva, nunca se borra. | [E1 · A](../features/formularios.md#carga-de-catálogos-desde-una-planilla--e1-a-2026-10-22) |
| Pegar e importar solo sobre un catálogo guardado y sin cambios, con la revisión esperada. | E1 · A |
| El tipo de valor se elige al crear y no cambia. Filtros siempre es texto. El valor no cambia WhatsApp, filtros, reglas ni el asistente. | [E1 · B](../features/formularios.md#catálogos-con-tipo-de-valor--e1-b-2026-10-22) |
| Elegir varias es otro tipo de pregunta, no una marca. Activarlo en una pregunta guardada crea una pregunta nueva. | [E1 · C](../features/formularios.md#elegir-varias-base-y-captura--e1-c1-2026-10-22) |
| Varias filas por pregunta con posición; referencias una por opción elegida. | E1 · C |
| Campos extra en `FieldsJson` del catálogo, con la misma revisión. Valores en `FieldValuesJson` canónico con GIN. | [E2 · Modelo de datos](../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23) |
| Una sola fuente de valores: «elegir de un catálogo» sobre otro catálogo «Escritas a mano». Se guarda el id de la opción. | [E2 · Una sola fuente](../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23) |
| CE3-B: el catálogo define lo de siempre y la pregunta puede tener lo propio. Solo viaja lo elegido. | [E2 · CE3-B](../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23) |
| CE4-A: los datos de las personas son del módulo, con pantalla propia y sección en «Editar usuario». | [E2 · CE4-A](../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23) |
| `Match` compara la misma opción; el operador es «es igual a»; al que le falta el dato no se le manda. | E2 · CE4-A |
| Se sella al capturar; lo mostrado queda en la sesión (`Shown`). | E2 · Modelo de datos |
| Un solo punto de preparación: `FormCatalogOptionsPolicy.PrepareAsync`. | E2 · Modelo de datos |
| Configuración interna con revisión y 409; SQL fijo y parametrizado. | [ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md), [ADR 0019](../decisions/0019-sql-de-configuracion-acotado-a-catalogos.md) |
| Los campos nuevos de lo sellado llevan `JsonIgnore(WhenWritingNull)`. | [Application/Modules/BusinessForms](../../src/ArquitecturaBase.Application/Modules/BusinessForms/AGENTS.md) |
| Lo fijado al dibujar: archivo, apagados con motivo, validación, protecciones, permisos, columna de valor, mínimo y máximo, subcolumnas, Excel y PDF. | [Diseño E1](../../../ArquitecturaBaseFront/docs/design/catalogos-mejoras.md#lo-que-se-fijó-al-dibujar) y [diseño E2](../../../ArquitecturaBaseFront/docs/design/catalogos-campos-extra.md#lo-que-se-fijó-al-dibujar) |

## 4. Recorridos de punta a punta

### 4.1 Catálogo chico, de a una (E1-A)

- **Pantalla:** `/catalogos/nuevo` → «Nuevo catálogo» (Escritas a mano, tipo Texto) → editor CA1-A.
- **Persona:**
  - carga nombre y descripción;
  - «Agregar opción» tres veces: Preventivo, Correctivo, De urgencia;
  - en «⋯», «Generar los códigos que faltan» (PREV, CORR, DE-URGENCIA);
  - Guardar.
- **Sistema:**
  - pegar e importar se ven apagados: «Guardá o descartá los cambios para pegar o importar»;
  - Guardar manda `POST /api/forms/catalogs` con `valueType: "Text"` y los tres ítems;
  - el servidor valida bajo el lock `forms:catalog:{id}`, asigna posiciones 1–3 y responde 201 con el mapa de identidades.
- **WhatsApp:** nada hasta usarlo en una pregunta. Ahí, desplegable con título = texto y descripción = detalle, como hoy.

### 4.2 Pegar 1.200 (E1-A)

- **Pantalla:** el editor de «Proveedores», guardado (puede estar vacío) → «Pegar desde Excel».
- **Persona:**
  - copia del Excel las columnas Código, Texto y Detalle con su encabezado y pega;
  - revisa: 1.200 filas leídas, 1.196 altas, 4 con errores;
  - toca «Aplicar sin las 4 filas con errores».
- **Sistema:**
  - el front interpreta lo pegado (`catalogPaste.ts`, nuevo) y lo manda como un CSV generado, con `mode=Append` y la revisión, a `POST /api/forms/catalogs/{id}/import-previews`;
  - el servidor lo lee con `IFormSpreadsheetReader` (nuevo) y arma el plan con `FormCatalogImportPolicy` (nuevo); no guarda nada;
  - Aplicar manda el mismo CSV con la huella `sheetSha256` a `POST /api/forms/catalogs/{id}/imports`;
  - el servidor vuelve a leer, compara la huella y aplica con el núcleo de guardado compartido con el PUT (`FormCatalogItemRecorder`, nuevo), bajo el lock y con la revisión;
  - la tabla pagina 1–20 de 1.196; al costado, «Así lo ve el empleado» avisa que una pregunta muestra hasta 200.
- **WhatsApp:** nada al cargar. Una pregunta de una sola con este catálogo se bloquea al mandar (`Forms.Record.CatalogTooLarge`) hasta filtrarlo por persona en E2.

### 4.3 Importar sobre uno existente (E1-A)

- **Pantalla:** «Exportar» y después «Importar» → CA2 en tres pasos: archivo, revisar y aplicar.
- **Persona:**
  - baja el Excel (`GET /api/forms/catalogs/{id}/export`), corrige detalles, borra 15 filas y agrega 128;
  - sube el archivo;
  - en «Revisar» ve altas 128, cambios 8, bajas 15 y 2 filas con errores;
  - si las bajas son una parte grande, marca «Sí, dar de baja las N»;
  - aplica.
- **Sistema:**
  - `mode=Replace`: lo que no viene se inactiva y lo que vuelve se reactiva;
  - una fila con error no entra; si su código existe, ese ítem queda como estaba (fijado acá);
  - si la huella coincide con la última aplicada, la vista previa trae `alreadyAppliedAtUtc` («Esta planilla ya se aplicó el …»);
  - si otra persona guardó en el medio, aplicar da 409 `Forms.Catalog.RevisionConflict` y el front ofrece «Revisar de nuevo» (otra vista previa con la revisión nueva);
  - exportar y reimportar sin tocar da cero diferencias.
- **WhatsApp:** las bajas dejan de ofrecerse en los envíos nuevos. Una sesión abierta que elige un ítem dado de baja se rechaza al capturar con `Forms.Record.CatalogItemInactive`, como hoy.

### 4.4 Catálogo decimal (E1-B)

- **Pantalla:** «Nuevo catálogo» con «¿Qué es el valor de cada opción?» = Decimal → editor con la columna «Valor».
- **Persona:**
  - carga CABA 0,00, GBA 1.500,00 e Interior 3.200,00;
  - en «Registrar entrega» usa el catálogo en «Zona de entrega»; el selector muestra «Decimal».
- **Sistema:**
  - el front muestra en es-AR («1.500,00») y manda el valor canónico (`"1500"`);
  - el servidor valida con `FormCatalogValuePolicy` (nuevo): `items[i].value` si no corresponde;
  - el valor es obligatorio en un catálogo tipado;
  - al capturar, la referencia sella `Value = "1500"` y `ValueType = "Decimal"` junto al texto.
- **WhatsApp:** no cambia. Viaja solo el texto («GBA») y el detalle.
- **Respuesta:**
  - planilla y detalle: «GBA»;
  - Excel: la columna «Zona de entrega · valor» con 1500 como número;
  - PDF: «GBA · 1.500,00».

### 4.5 Pregunta de elegir varias (E1-C)

- **Pantalla:** el editor de «Registrar mantenimiento» → la pregunta «Fallas» (elegir de un catálogo, «Fallas», 5 opciones).
- **Persona:**
  - marca «Permitir elegir varias», mínimo 1 y máximo 3;
  - si la pregunta ya estaba publicada, confirma «Crear la pregunta nueva»;
  - la lista de preguntas dice «varias, hasta 3»;
  - la vista previa muestra casillas con «Elegí hasta 3»;
  - publica.
- **Sistema:**
  - el borrador guarda `type: "multiChoice"`, `source` y `selection: { minSelected: 1, maxSelected: 3 }`;
  - en una pregunta ya guardada, activarlo manda `replacesFieldId`, como cambiar de catálogo (`FormDraftPolicy.Resolve`);
  - la comprobación frena si el catálogo Manual tiene más de 20 opciones activas (`Forms.Publication.CatalogTooLargeForMultiple`);
  - el canal compila con el perfil `flow-static-7.3-r9`.
- **WhatsApp:**
  - `CheckboxGroup` con `data-source` dinámico, `min-selected-items` y `max-selected-items`;
  - la respuesta llega como lista de ids;
  - el servicio de respuestas la clasifica por el mapeo: lista de textos es selección y lista de objetos es archivos;
  - la captura guarda una fila por opción en `BusinessFormRecordValues`, con su `Position`, y una referencia por opción.
- **Respuesta:**
  - planilla: «Frío, Ruido» en la celda, que no se ordena, y el filtro «alguna de» con su chip;
  - Más filtros: cada opción con su cantidad; una respuesta con dos cuenta en las dos;
  - detalle: una opción por renglón;
  - chat y PDF: «Frío, Ruido»;
  - Excel: «Frío; Ruido».

### 4.6 Definir campos extra (E2-1)

- **Pantalla:**
  - un catálogo «Zonas» (escrito a mano, 72 opciones);
  - el editor de «Proveedores», en la sección «Campos» (CE1).
- **Persona:**
  - «Agregar campo» cuatro veces:
    - CUIT: texto, único, dato;
    - Zona: elegir de «Zonas», filtro;
    - Rubro: elegir de «Rubros», filtro;
    - Plazo de pago (días): entero de 0 a 180, dato;
  - Guardar;
  - las columnas, apagadas con «Guardá para cargar valores», se habilitan;
  - pega 1.200 filas con esas columnas;
  - pasa CUIT a obligatorio.
- **Sistema:**
  - el PUT trae `fields`; el servidor asigna ids y valida clave única, tipo, reglas, papel y tope de 20;
  - todo nace opcional;
  - pegar reconoce las columnas por clave o por nombre;
  - en «Zona» se escribe el código o el texto de la zona y el servidor guarda el id de la opción;
  - los valores se guardan canónicos en `FieldValuesJson`;
  - pasar a obligatorio controla todas las opciones activas; si faltan, `fields[i].required` dice cuántas;
  - cambiar el tipo de un campo cargado convierte los hasta 10.000 valores y, si alguno no entra, `fields[i].type` con la lista por fila;
  - quitar un campo con datos pide `confirmFieldRemovals`; uno en uso da 409 `Forms.Catalog.FieldInUse`;
  - «Zonas» no se puede dar de baja mientras un campo o un dato la use (`Forms.Catalog.SourceInUse`).
- **WhatsApp:** nada todavía.

### 4.7 Qué viaja a WhatsApp, en el catálogo y en la pregunta (E2-1 y E2-3)

- **Catálogo:**
  - en «En WhatsApp», título = [Texto] y descripción = [CUIT, Zona]; guarda en `PresentationJson`;
  - la vista previa usa el pedazo de catálogo cargado;
  - el aviso definitivo de títulos de más de 30 sale del servidor (`presentationCheck` en el detalle), con la cantidad y tres ejemplos (fijado acá).
- **Pregunta:**
  - en «Proveedores a consultar», «En WhatsApp» deja «Como en el catálogo» (`Presentation: null`) o «Propio de esta pregunta», por ejemplo título [Texto] y descripción [Rubro, Plazo];
  - se guarda en `source.presentation` del borrador y se publica como cualquier cambio.
- **Sistema:**
  - `FormCatalogPresentationPolicy` (nuevo) arma título y descripción con « · » y es la misma en el editor, la comprobación, «Como si lo recibiera» y la preparación;
  - un título de más de 30 se puede guardar;
  - la comprobación frena con `Forms.Publication.CatalogTitleTooLong` o `Forms.Publication.QuestionTitleTooLong`;
  - al mandar se vuelve a controlar.
- **WhatsApp:**
  - la preparación sella por opción `Shown { Title, Description }` en la oferta de la sesión;
  - `WhatsAppMessagePayload` manda `title = Shown.Title` y `description = Shown.Description`; sin `Shown`, `Text` y `Detail`, como hoy;
  - CUIT y Zona, si no se eligieron, no salen del sistema;
  - lo ya enviado conserva lo que se mostró.

### 4.8 Datos de las personas y filtrar por persona (E2-2 y E2-3)

- **Pantalla:**
  - Administración → «Datos de las personas» (`/datos-de-personas`);
  - «Editar usuario» → sección «Datos»;
  - en el editor de formularios, «Qué opciones ve cada persona» y «Como si lo recibiera».
- **Persona:**
  1. agrega el dato «Zona» (clave `zona`, elegir de «Zonas»);
  2. en «Editar usuario» de Juan Gómez, Zona = Centro, y guarda;
  3. en «Pedir repuesto» → «Proveedores a consultar», agrega:
     - «Zona es la zona de quien lo recibe»;
     - un filtro fijo, «Rubro es Frío» (opcional);
  4. prueba «Como si lo recibiera: Juan Gómez · Zona Centro → 14 opciones»;
  5. comprueba y publica.
- **Sistema:**
  - «Editar usuario» guarda primero el usuario (núcleo) y después, solo si cambió, `PUT /api/forms/person-data/{userId}` con su revisión;
  - si eso falla, el diálogo queda abierto con el error en la sección («El resto del usuario ya se guardó»);
  - la pregunta guarda `source.match = [{ fieldId: zona, personAttributeId: zona }]`; `Normalize` ordena por `fieldId` y una lista vacía pasa a `null`;
  - la comprobación:
    - frena lo que no puede funcionar: campo inexistente o inactivo, papel distinto de filtro, fuente distinta;
    - suma avisos que no frenan: «como mucho 18 por persona (Zona Sur)» contra 20 o 200, y «2 personas sin zona: Pablo Sosa y Marta Ruiz».
- **WhatsApp, al mandar a Juan:**
  - `FormCatalogOptionsPolicy.PrepareAsync`:
    1. lee los datos de Juan;
    2. arma la contención `{"<zona>":"<id de Centro>","<rubro>":"<id de Frío>"}`;
    3. trae con `ListOfferItemsAsync(catalogId, contención)` hasta 201;
    4. controla 20 o 200;
    5. arma `Shown` y sella la oferta;
  - Juan ve sus 14 en el celular;
  - el asistente pasa por la misma preparación.
- **Al mandar a Pablo (sin zona):** la preparación devuelve `Forms.Record.RecipientDataMissing` y no se manda nada. El chat lo dice: «Pablo Sosa no tiene zona…».
- Si el campo o el dato quedaron inactivos, da `Forms.Record.CatalogFilterUnavailable`. El filtro nunca se quita solo.

### 4.9 La respuesta en la planilla, el Excel y el PDF (E1-B, E1-C y E2-4)

- **Captura:**
  - Juan elige Frío Sur S.A. y Electro Centro;
  - bajo el lock del catálogo, `BusinessFormRecordRecorder` lee por lote `FieldsJson` y el `FieldValuesJson` de los dos ítems;
  - sella en cada referencia, por campo: id, clave, nombre es/en, tipo, papel y valor (y el texto de la opción en los de «elegir de un catálogo»).
- **Planilla:**
  - «Proveedores a consultar»: «Frío Sur S.A., Electro Centro»;
  - después, las subcolumnas de dato visibles («· CUIT», «· Plazo de pago (días)»), con las opciones en el mismo orden;
  - las de filtro (Zona, Rubro) se prenden en «Columnas»;
  - las subcolumnas no se ordenan, ni filtran, ni cuentan en las facetas;
  - una respuesta anterior a los campos muestra «—».
- **Detalle:** cada opción con todos sus campos sellados.
- **Excel:**
  - una columna por campo, todos, con «; » entre opciones;
  - con «Solo las columnas visibles», sigue a las columnas de la planilla;
  - el valor tipado del catálogo (E1-B) va en «· valor».
- **PDF:** los campos de dato junto a la opción; el valor tipado como «texto · valor».
- **Chat:** la tarjeta queda como hoy; el adaptador del chat no manda los campos.
- **Inglés:** nombres de campo en inglés si están cargados; si no, el original.

## 5. Modelo de datos

### 5.1 Las tablas de hoy

Leídas de las configuraciones EF y de [`ApplicationDbContextModelSnapshot.cs`](../../src/ArquitecturaBase.Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs).

**`BusinessFormCatalogs`** ([configuración](../../src/ArquitecturaBase.Infrastructure/Modules/BusinessForms/Persistence/Configurations/BusinessFormCatalogConfiguration.cs), [entidad](../../src/ArquitecturaBase.Domain/Modules/BusinessForms/BusinessFormCatalog.cs))

| Columna | Tipo | Nulo | Notas |
| --- | --- | --- | --- |
| `Id` | uuid | no | PK |
| `Key` | varchar(32) | no | índice único; el `Id` sin guiones |
| `NameJson`, `DescriptionJson` | jsonb | no | `{es, en}` |
| `DisplayNameEs/En` | varchar(120) | — | calculadas y guardadas (`stored`) |
| `DisplayDescriptionEs/En` | varchar(1000) | — | calculadas y guardadas (`stored`) |
| `FiltersJson` | jsonb | sí | solo Filters |
| `Kind` | varchar(16) | no | `Manual` o `Filters` |
| `Revision` | bigint | no | token de concurrencia |
| `IsActive` | boolean | no | |
| `ProposalId` | uuid | sí | índice único |
| `ProposalEvidenceJson`, `ProposalState`, `ProposalDecidedByUserId`, `ProposalDecidedAtUtc` | jsonb, varchar(16), uuid, timestamptz | sí | propuesta H4 |
| `CreatedAtUtc`, `CreatedBy`, `ModifiedAtUtc`, `ModifiedBy` | | | auditoría |

- **CHECK:**
  - `CK_BusinessFormCatalogs_KindFilters`: Manual sin `FiltersJson`, Filters con `FiltersJson`;
  - `CK_BusinessFormCatalogs_Proposal`: coherencia de la propuesta.

**`BusinessFormCatalogItems`** ([configuración](../../src/ArquitecturaBase.Infrastructure/Modules/BusinessForms/Persistence/Configurations/BusinessFormCatalogItemConfiguration.cs), [entidad](../../src/ArquitecturaBase.Domain/Modules/BusinessForms/BusinessFormCatalogItem.cs))

| Columna | Tipo | Nulo | Notas |
| --- | --- | --- | --- |
| `Id` | uuid | no | PK |
| `CatalogId` | uuid | no | FK → `BusinessFormCatalogs`, `Restrict` |
| `Code` | varchar(80) | no | |
| `TextEs` / `TextEn` | varchar(200) | no / sí | Domain limita a 30 (`MaxTextLength`) |
| `DetailEs` / `DetailEn` | varchar(500) | sí | Domain limita a 300 (`MaxDetailLength`) |
| `Position` | integer | no | |
| `IsActive` | boolean | no | |
| auditoría | | | |

- **Índices:**
  - `(CatalogId, Code)` único e **inmediato**: por eso intercambiar dos códigos da 500;
  - `(CatalogId, IsActive, Position, Id)`.
- **Clave diferida en SQL, fuera del snapshot:** `AK_BusinessFormCatalogItems_CatalogId_Position UNIQUE ("CatalogId", "Position") DEFERRABLE INITIALLY DEFERRED` ([migración](../../src/ArquitecturaBase.Infrastructure/Persistence/Migrations/20261012015620_AddUniqueManualCatalogItemPositions.cs)).
- La unicidad del código sin distinguir mayúsculas la controla Application ([`FormCatalogService`](../../src/ArquitecturaBase.Application/Modules/BusinessForms/Services/FormCatalogService.cs)); la base distingue mayúsculas.

**`BusinessFormRecordValues`** ([configuración](../../src/ArquitecturaBase.Infrastructure/Modules/BusinessForms/Persistence/Configurations/BusinessFormRecordConfiguration.cs), [entidad](../../src/ArquitecturaBase.Domain/Modules/BusinessForms/BusinessFormRecord.cs))

| Columna | Tipo | Nulo | Notas |
| --- | --- | --- | --- |
| `Id`, `RecordId`, `DefinitionId`, `FieldId` | uuid | no | |
| `Type` | varchar(32) | no | `shortText`, `decimal`, `choice`… |
| `TextValue` | varchar(10000) | sí | |
| `DecimalValue` | numeric | sí | |
| `DateValue` | date | sí | |
| `BooleanValue` | boolean | sí | |
| `ChoiceId` | uuid | sí | opción propia o ítem de catálogo |

- **Índices:** `(RecordId, FieldId)` **único**, `(DefinitionId, FieldId)` y `(DefinitionId, RecordId)`.
- **CHECK:** `CK_BusinessFormRecordValues_ExactlyOneValue`.
- **FK:** `(DefinitionId, RecordId)` → registros, en cascada; `(DefinitionId, FieldId)` → campos, `Restrict`.

**`BusinessFormRecordReferences`** ([configuración](../../src/ArquitecturaBase.Infrastructure/Modules/BusinessForms/Persistence/Configurations/BusinessFormRecordReferenceConfiguration.cs), [entidad](../../src/ArquitecturaBase.Domain/Modules/BusinessForms/BusinessFormRecordReference.cs))

| Columna | Tipo | Nulo | Notas |
| --- | --- | --- | --- |
| `Id`, `RecordId`, `DefinitionId`, `FieldId` | uuid | no | |
| `CatalogId` | uuid | no | FK → catálogos, `Restrict` |
| `CatalogRevision` | bigint | no | |
| `ItemId` | uuid | no | opaco: en Filters no apunta a un ítem Manual |
| `Text` | varchar(30) | no | |
| `Detail` | varchar(300) | sí | |
| `CatalogName` | varchar(200) | no | |
| `TranslationsJson` | jsonb | sí | textos por idioma de la oferta |

- **Índices:** `(RecordId, FieldId)` **único**, `CatalogId`, `(DefinitionId, FieldId)` y `(DefinitionId, RecordId)`.
- **FK:** como los valores.

**Lo relacionado:**
- `BusinessFormRecords.ValuesJson` es jsonb con `[{ "fieldId", "value" }]`.
- `BusinessFormRecordFiles` ya resuelve «varias filas por pregunta»: `(RecordId, FieldId, Position)` único y `CK_BusinessFormRecordFiles_Position` (0–9). Es el modelo a copiar.
- `FormDefinitions.SchemaJson` (borrador) es `{ "format": 1, "fields": [...] }` y `BusinessFormVersions.SchemaJson` (sellado) es `{ "format": 1, "questions": [...] }`. Los dos con `CanonicalFormField` ([`FormDraftModels.cs`](../../src/ArquitecturaBase.Application/Modules/BusinessForms/Models/FormDraftModels.cs)).

### 5.2 Los cambios de E1

| Dónde | Cambio | Tipo / nulo / default | Corte |
| --- | --- | --- | --- |
| `BusinessFormCatalogItems` | Se quita del modelo el índice único `(CatalogId, Code)`. Se agrega en SQL `AK_BusinessFormCatalogItems_CatalogId_Code UNIQUE ("CatalogId", "Code") DEFERRABLE INITIALLY DEFERRED`, como la de posición. | — | E1-0 |
| `BusinessFormCatalogs.ValueType` | Nueva | varchar(16), no nulo, default `'Text'` | E1-B |
| `CK_BusinessFormCatalogs_ValueType` | `"ValueType" IN ('Text','Integer','Decimal','Date') AND ("Kind" = 'Manual' OR "ValueType" = 'Text')` | CHECK | E1-B |
| `BusinessFormCatalogItems.Value` | Nueva; el valor canónico (sección 8.1) | varchar(64), nulo | E1-B |
| `BusinessFormRecordReferences.Value` y `.ValueType` | Nuevas; sellados al capturar | varchar(64) y varchar(16), nulos | E1-B |
| `CK_BusinessFormRecordReferences_Value` | `("Value" IS NULL) = ("ValueType" IS NULL)` | CHECK | E1-B |
| `BusinessFormCatalogs.LastImportSha256` y `.LastImportAtUtc` | Nuevas; la marca de la última planilla aplicada, para «Esta planilla ya se aplicó el …» (fijado acá) | char(64) y timestamptz, nulos | E1-A |
| `CK_BusinessFormCatalogs_LastImport` | `("LastImportSha256" IS NULL) = ("LastImportAtUtc" IS NULL)` | CHECK | E1-A |
| `BusinessFormRecordValues.Position` | Nueva | integer, no nulo, default 0 | E1-C1 |
| Índice de valores | `(RecordId, FieldId)` único pasa a `(RecordId, FieldId, Position)` único | — | E1-C1 |
| `CK_BusinessFormRecordValues_Position` | `"Position" >= 0 AND "Position" < 20` | CHECK | E1-C1 |
| Índice de referencias | `(RecordId, FieldId)` único pasa a `(RecordId, FieldId, ItemId)` único: una por opción elegida | — | E1-C1 |

**Notas:**
- `Value` no tiene CHECK por tipo: el tipo está en el catálogo. Lo valida `FormCatalogValuePolicy`. En un catálogo tipado es obligatorio en todos los ítems, activos o no.
- Las referencias no suman posición: el orden de lo elegido sale del array de `ValuesJson`, y cada opción se busca por `(FieldId, ItemId)`, como hace hoy [`FormRecordPresentationPolicy`](../../src/ArquitecturaBase.Application/Modules/BusinessForms/Services/FormRecordPresentationPolicy.cs).
- Las CHECK y los filtros del modelo van en una sola línea. El SQL multilínea de las migraciones (claves diferidas, bloques `RAISE EXCEPTION`) usa `.ReplaceLineEndings("\n")`, como pide la [guía de migraciones](../../src/ArquitecturaBase.Infrastructure/Persistence/Migrations/AGENTS.md) (sección 13).

### 5.3 Los cambios de E2

| Dónde | Cambio | Tipo / nulo / default | Corte |
| --- | --- | --- | --- |
| `BusinessFormCatalogs.FieldsJson` | Nueva; los campos extra, en el agregado y con su revisión, como `FiltersJson` | jsonb, nulo (`null` = sin campos) | E2-1 |
| `BusinessFormCatalogs.PresentationJson` | Nueva; lo que viaja a WhatsApp por defecto | jsonb, nulo (`null` = lo de hoy) | E2-1 |
| `CK_BusinessFormCatalogs_Fields` | `"Kind" = 'Manual' OR ("FieldsJson" IS NULL AND "PresentationJson" IS NULL)` | CHECK | E2-1 |
| `BusinessFormCatalogItems.FieldValuesJson` | Nueva | jsonb, no nulo, default `'{}'` | E2-1 |
| `IX_BusinessFormCatalogItems_FieldValuesJson` | `USING gin ("FieldValuesJson" jsonb_path_ops)`, en el modelo con `HasMethod("gin").HasOperators("jsonb_path_ops")` | índice | E2-1 |
| `BusinessFormRecordReferences.FieldValuesJson` | Nueva; campos de la opción, sellados al capturar | jsonb, nulo | E2-1 |
| `BusinessFormPersonAttributes` | Nueva tabla (abajo) | — | E2-2 |
| `BusinessFormPersonData` | Nueva tabla (abajo) | — | E2-2 |
| `FormQuestionSource` (en `SchemaJson` del borrador, de la versión y del manifiesto) | Suma `Match` y `Presentation`, al final y con `JsonIgnore(WhenWritingNull)`. Sin migración. | JSON | E2-3 |
| Oferta de la sesión (`StructuredFormCatalogOption`) | Suma `Shown` con `JsonIgnore(WhenWritingNull)`, sellado en el payload protegido de la sesión. Sin migración. | JSON | E2-3 |

**`BusinessFormPersonAttributes`** (nueva)

| Columna | Tipo | Nulo | Notas |
| --- | --- | --- | --- |
| `Id` | uuid | no | PK, `Entity` (v7) |
| `Key` | varchar(40) | no | índice único; `^[a-z][a-z0-9_]{0,39}$` (fijado acá) |
| `NameJson` | jsonb | no | `{es, en}`; `es` obligatorio |
| `Type` | varchar(16) | no | `Catalog` o `Text` |
| `SourceCatalogId` | uuid | sí | FK → `BusinessFormCatalogs`, `Restrict`; obligatorio si `Type = 'Catalog'` |
| `Position` | integer | no | orden en la pantalla |
| `IsActive` | boolean | no | inactivo conserva el nombre |
| `Revision` | bigint | no | token de concurrencia; 409 por dato |
| auditoría | | | `IAuditable` |

- **CHECK:** `CK_BusinessFormPersonAttributes_Source`: `("Type" = 'Catalog') = ("SourceCatalogId" IS NOT NULL)`.

**`BusinessFormPersonData`** (nueva)

| Columna | Tipo | Nulo | Notas |
| --- | --- | --- | --- |
| `Id` | uuid | no | PK |
| `UserId` | uuid | no | índice único; **sin FK** a las tablas del núcleo |
| `ValuesJson` | jsonb | no, default `'{}'` | `{ "<id del dato>": valor }` |
| `Revision` | bigint | no | token de concurrencia; 409 por persona |
| auditoría | | | `IAuditable` |

- Una persona borrada conserva su fila.
- Un dato inactivo conserva su valor y no se ofrece.

### 5.4 Los JSON

Los ids de los ejemplos son ilustrativos.

**`FieldsJson`**: lista ordenada, con los inactivos incluidos. Las claves salen en camelCase con `JsonSerializerDefaults.Web`, como el resto.

```json
[
  { "id": "0199f0a1-0000-7000-8000-00000000c017", "key": "cuit",
    "name": { "es": "CUIT", "en": "Tax ID" }, "type": "text",
    "required": true, "unique": true, "min": null, "max": null, "maxLength": 13,
    "role": "data", "position": 1, "isActive": true },
  { "id": "0199f0a1-0000-7000-8000-00000000z0a1", "key": "zona",
    "name": { "es": "Zona", "en": "Zone" }, "type": "catalog",
    "sourceCatalogId": "0199f0a1-0000-7000-8000-0000000zonas",
    "required": true, "unique": false, "role": "filter", "position": 2, "isActive": true },
  { "id": "0199f0a1-0000-7000-8000-0000000p1a20", "key": "plazo_pago",
    "name": { "es": "Plazo de pago (días)", "en": "Payment term (days)" }, "type": "integer",
    "required": false, "unique": false, "min": "0", "max": "180",
    "role": "data", "position": 4, "isActive": true }
]
```

- **Tipos:** `text`, `integer`, `decimal`, `date`, `boolean` y `catalog`.
- `min` y `max` son canónicos (texto) y valen para número y fecha. `maxLength` vale solo para texto, hasta 300.
- `sourceCatalogId` solo en `catalog`.
- **Papel:** `filter` o `data`.

**`PresentationJson`** del catálogo y `Presentation` de la pregunta: la misma forma.

```json
{ "title": ["text", "0199f0a1-0000-7000-8000-00000000c017"], "description": ["detail"] }
```

- Los fijos son `text`, `code`, `detail` y `value` (este último solo en un catálogo tipado). Un campo extra va por su id.
- `title` no puede quedar vacío; `description` puede ser `[]`.
- **Separador:** « · ». Un campo vacío en una opción se saltea, sin dejar separadores dobles (fijado acá).

**`FieldValuesJson` del ítem, canónico:** la contención compara JSON tipado.

```json
{
  "0199f0a1-0000-7000-8000-00000000c017": "30-71234567-8",
  "0199f0a1-0000-7000-8000-00000000z0a1": "0199f0a1-0000-7000-8000-0000000centr",
  "0199f0a1-0000-7000-8000-0000000p1a20": 30,
  "0199f0a1-0000-7000-8000-0000000recar": "1500.5",
  "0199f0a1-0000-7000-8000-0000000vtv00": "2027-03-15",
  "0199f0a1-0000-7000-8000-0000000gps00": true
}
```

- **Clave:** el id del campo, en `"D"` y minúsculas.
- **Valores:**
  - texto: string tal cual, recortado;
  - entero: número JSON (hasta 18 dígitos; fijado acá);
  - decimal: string con `FormScalarPrefillPolicy.CanonicalDecimal`, la normalización del prellenado;
  - fecha: `"AAAA-MM-DD"`;
  - sí o no: `true` o `false`;
  - «elegir de un catálogo»: el id de la opción como string.
- Un valor vacío **no se guarda**: la clave no aparece.

**`FormQuestionSource` con `Match` y `Presentation`**, dentro de un campo del borrador o de la versión:

```json
"source": {
  "catalog": "0199f0a1-0000-7000-8000-0000000provs",
  "match": [
    { "fieldId": "0199f0a1-0000-7000-8000-0000000rubro", "value": "0199f0a1-0000-7000-8000-00000000frio" },
    { "fieldId": "0199f0a1-0000-7000-8000-00000000z0a1", "personAttributeId": "0199f0a1-0000-7000-8000-0000000pzona" }
  ],
  "presentation": { "title": ["text"], "description": ["0199f0a1-0000-7000-8000-0000000rubro", "0199f0a1-0000-7000-8000-0000000p1a20"] }
}
```

- Cada filtro tiene `fieldId` y **uno** de `value` (fijo, canónico como string) o `personAttributeId` (según quien lo recibe).
- Un filtro por campo. `Normalize` los ordena por `fieldId` y `[]` pasa a `null`.
- `presentation: null` es «Como en el catálogo».
- Un `source` sin `match` ni `presentation` se serializa igual que hoy: no cambia la huella.

**`Shown` en la oferta sellada** (`StructuredFormCatalogOption`):

```json
{ "id": "…", "text": "Frío Sur S.A.", "detail": "Refrigeración · CABA",
  "translations": { "es": { "text": "Frío Sur S.A.", "detail": "Refrigeración · CABA" } },
  "value": "1500",
  "shown": { "title": "Frío Sur S.A. · 30-71234567-8", "description": "Frío · 30" } }
```

- `value` (E1-B) y `shown` (E2-3) llevan `JsonIgnore(WhenWritingNull)`.
- La oferta suma `valueType` con la misma regla.
- WhatsApp usa `Shown?.Title ?? Text`; `Text` sigue siendo el del ítem.

**`FieldValuesJson` sellado en la referencia** (E2-1):

```json
[
  { "id": "…c017", "key": "cuit", "name": { "es": "CUIT", "en": "Tax ID" }, "type": "text", "role": "data", "value": "30-71234567-8" },
  { "id": "…z0a1", "key": "zona", "name": { "es": "Zona", "en": "Zone" }, "type": "catalog", "role": "filter",
    "value": "…centr", "text": { "es": "Centro", "en": "Center" } },
  { "id": "…p1a20", "key": "plazo_pago", "name": { "es": "Plazo de pago (días)" }, "type": "integer", "role": "data", "value": 30 }
]
```

- Solo los campos activos al capturar y con valor.
- `text` existe solo en `catalog`, con el texto de la opción fuente en ese momento: lo usan la planilla, el Excel y el PDF (fijado acá).

**`ValuesJson` del registro con una pregunta de varias** (E1-C1):

```json
[ { "fieldId": "…fallas", "value": ["…frio", "…ruido"] } ]
```

- Una entrada por pregunta, con el array en el orden recibido.
- En `BusinessFormRecordValues` hay dos filas, con `Position` 0 y 1 y `Type = "multiChoice"`.

**`ValuesJson` de `BusinessFormPersonData`** (E2-2): `{ "<id del dato>": "<id de la opción>" }` para `Catalog`, y string para `Text`.

### 5.5 Las migraciones

Van en la cadena única ([guía](../guides/migracion.md)) y nombran las entidades con cadenas, como pide [módulos opcionales](../architecture/backend.md#módulos-opcionales). El `Down` sigue la regla de la [migración de sugerencias](../../src/ArquitecturaBase.Infrastructure/Persistence/Migrations/AGENTS.md): rechaza antes de borrar hechos que no puede representar.

| Corte | Nombre propuesto | `Up` | `Down` |
| --- | --- | --- | --- |
| E1-0 | `DeferCatalogItemCodeUniqueness` | Borra `IX_BusinessFormCatalogItems_CatalogId_Code` y agrega en SQL la clave diferida `AK_BusinessFormCatalogItems_CatalogId_Code`. | Borra la clave y recrea el índice único. |
| E1-B | `AddCatalogValueType` | `ValueType` con default `'Text'` y su CHECK; `Value` en ítems; `Value`, `ValueType` y su CHECK en referencias. Los datos de hoy quedan `Text` y `null`. | `RAISE EXCEPTION` si hay un catálogo con `ValueType <> 'Text'` o una referencia con `Value`; si no, borra columnas y CHECK. |
| E1-A | `AddCatalogImportMarker` | `LastImportSha256`, `LastImportAtUtc` y su CHECK. | Borra las columnas. |
| E1-C1 | `AddFormMultipleChoiceValues` | `Position` con default 0 y su CHECK; cambia el índice único de valores y el de referencias. | `RAISE EXCEPTION` si hay un valor con `Position > 0` o dos referencias del mismo `(RecordId, FieldId)`; si no, vuelve a los índices de hoy. |
| E2-1 | `AddCatalogFieldsAndPresentation` | `FieldsJson`, `PresentationJson` y su CHECK; `FieldValuesJson` con default `'{}'` y el GIN; `FieldValuesJson` en referencias. | `RAISE EXCEPTION` si hay campos, presentación, valores distintos de `'{}'` o referencias selladas; si no, borra. |
| E2-2 | `AddFormPersonData` | Las dos tablas, índices, CHECK y FK. | `RAISE EXCEPTION` si alguna tiene filas; si no, las borra. |

- Ninguna migración reescribe `SchemaJson`, manifiestos, huellas, bindings ni capturas.
- Cada una tiene su prueba con datos (sección 13).
- `MigrationsTests.Model_has_no_pending_changes` cubre que el snapshot coincida.

## 6. Contratos HTTP

Todas las rutas son del módulo, en `api/forms`, MVC, con contrato propio (`*HttpRequest` o `*Query`), `ToString()` redactado si llevan contenido, `no-store` en las lecturas privadas, rate limit del módulo y [`ToActionResult` o `ToCreatedResult`](../architecture/backend.md#borde-http). El contrato definitivo son los DTO, el OpenAPI y los ejemplos exportados ([ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md)); estas formas son el punto de partida.

### 6.1 E1

| Método | Ruta | Permiso | Cuerpo | Respuesta |
| --- | --- | --- | --- | --- |
| POST (cambia) | `/api/forms/catalogs` | CatalogsRead + CatalogsManage | Suma `valueType` (`Text` por defecto, `Integer`, `Decimal` o `Date`). Los ítems suman `value` (canónico). | 201, `FormCatalogSaveResponse` |
| PUT (cambia) | `/api/forms/catalogs/{id}` | ídem | Los ítems suman `value`. Suma `sortAlphabetically?: true`, que no se combina con `moves`. Tope de 10.000. El error de código repetido va a `items[i].code`. | 200 o 409 |
| GET (cambia) | `/api/forms/catalogs/{id}` | CatalogsRead | — | Suma `valueType` |
| GET (cambia) | `/api/forms/catalogs` | CatalogsRead | — | Cada fila suma `valueType`, para el selector del editor de formularios |
| GET (cambia) | `/api/forms/catalogs/{id}/items` | CatalogsRead | — | Cada ítem suma `value` |
| POST (nueva) | `/api/forms/catalogs/{id}/import-previews` | CatalogsRead + CatalogsManage | `multipart/form-data`: `file` (XLSX o CSV), `mode` (`Replace` o `Append`) y `expectedRevision` | 200, `FormCatalogImportPreviewResponse`; 400 por archivo; 409 por revisión |
| POST (nueva) | `/api/forms/catalogs/{id}/imports` | ídem | Lo mismo, más `sheetSha256`, `confirmDeactivations?` (número), `confirmEmpty?` y `confirmErrors?` | 200, `FormCatalogSaveResponse`; 400 `ConfirmationRequired`; 409 por revisión o `SheetChanged` |
| GET (nueva) | `/api/forms/catalogs/{id}/export?template=false` | CatalogsRead | `FormCatalogExportQuery(bool Template)` | 200 XLSX, `nosniff`; con `template=true`, la plantilla |
| POST y PUT (cambia) | `/api/forms/definitions`, `/{id}/draft`, `/{id}/publication-checks`, `/{id}/publications` | como hoy | El campo admite `type: "multiChoice"` y `selection: { minSelected, maxSelected }` | como hoy; issue `Forms.Publication.CatalogTooLargeForMultiple` |
| POST (cambia) | `/api/forms/definitions/{id}/records/search`, `/records/filter-counts/search`, `/records/export`, `/records/{recordId}/navigation` | como hoy | La condición admite `operator: "anyOf"` sobre `multiChoice` | Celda `multiChoice` con lista de opciones; la columna suma `valueType` |
| GET (cambia) | `/api/forms/records/{id}` | RecordsRead | — | `multiChoice` como lista; la opción de catálogo suma `value` y `valueType` |

**Vista previa** (`FormCatalogImportPreviewResponse`). Cada lista trae como mucho 100 filas, con `listsTruncated`; el detalle completo está en el Excel (fijado acá).

```json
{
  "catalogId": "…", "revision": 7, "mode": "Replace", "sheetSha256": "9f2c…", "sheetName": "Proveedores",
  "rowsRead": 1212,
  "counts": { "added": 128, "changed": 8, "deactivated": 15, "reactivated": 0, "unchanged": 1059, "errorRows": 2, "warnings": 1 },
  "issues": [
    { "row": 37, "column": "code", "severity": "Error", "code": "Forms.CatalogImport.DuplicateCode", "message": "Repetido: también está en otra fila.", "relatedRow": 12 },
    { "row": 52, "column": "text", "severity": "Error", "code": "Forms.CatalogImport.TextTooLong", "message": "…", "length": 34 }
  ],
  "added": [ { "row": 2, "code": "PRV-1201", "text": "Gases del Sur" } ],
  "changed": [ { "row": 14, "itemId": "…", "code": "PRV-0002", "changes": [ { "column": "detail", "before": "Repuestos · GBA", "after": "Repuestos · GBA Norte" } ] } ],
  "deactivated": [ { "itemId": "…", "code": "PRV-0050", "text": "…" } ],
  "listsTruncated": false,
  "confirmations": { "deactivations": 15, "emptySheet": false, "manyErrors": false },
  "alreadyAppliedAtUtc": null
}
```

- **Fila:** la del archivo, contando el encabezado como 1.
- **Columna:** la clave lógica (`code`, `text`, `textEn`, `detail`, `detailEn`, `active`, `value` y, en E2, el id del campo). El front la traduce.
- **Avisos que no frenan:** `DuplicateText`, una columna desconocida que se ignora y `alreadyAppliedAtUtc`.

**Ejemplos que hay que exportar para el front (E1):**

| Cohorte | Qué suma | Productor |
| --- | --- | --- |
| `catalogs-http/manual-current` | Detalle, ítems, alta y guardado con `valueType` y `value`; 409; `items[i].code`; orden A–Z | [`FormCatalogEndpointsTests.cs`](../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormCatalogEndpointsTests.cs), variable `FORMS_CATALOG_HTTP_CONTRACT_OUTPUT` |
| `catalogs-http/imports` (nueva) | Vista previa `Replace` con errores, `Append`, ya aplicada y vacía; aplicar bien; 409; `ConfirmationRequired`; plantilla y exportación (XLSX) | `FormCatalogImportHttpTests.cs` (nuevo) |
| `forms-http` | Borrador con `multiChoice` y `selection`; chequeo con más de 20 | [`FormHttpContractFixturesTests.cs`](../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormHttpContractFixturesTests.cs) |
| `forms-responses-http` | Planilla, conteos, detalle y Excel con `multiChoice` y con valor tipado | [`export-responses-http.ps1`](../../tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Modules/BusinessForms/export-responses-http.ps1) |
| `whatsapp-catalogs-http` | Tarjeta del chat con una respuesta de varias | [`export-k7-http.ps1`](../../tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Modules/WhatsApp/export-k7-http.ps1) |

### 6.2 E2

| Método | Ruta | Permiso | Cuerpo | Respuesta |
| --- | --- | --- | --- | --- |
| PUT (cambia) | `/api/forms/catalogs/{id}` | CatalogsRead + CatalogsManage | Suma `fields` (lista completa, ordenada; un quitado va con `isActive: false`), `confirmFieldRemovals` (ids), `presentation`; los ítems suman `fields: { "<id>": "<canónico>" \| null }` | Suma `fieldIdentityMap` (`localId` → id); 400 en `fields[i].*` o `items[i].fields.<id>`; 409 `FieldInUse` o `SourceInUse` |
| GET (cambia) | `/api/forms/catalogs/{id}` | CatalogsRead | — | Suma `fields` (con `hasValues`), `presentation` y `presentationCheck { titleTooLongCount, examples[≤3] }` |
| GET (cambia) | `/api/forms/catalogs/{id}/items` | CatalogsRead | — | Cada ítem suma `fields` y `fieldTexts` (el texto de las opciones de los campos `catalog`) |
| POST (cambia) | `import-previews`, `imports`, `export` | como en E1 | Columnas de los campos, reconocidas por clave o nombre es/en | ídem |
| GET (nueva) | `/api/forms/person-attributes` | cualquiera de `formpersondata.manage`, `users.manage` o `formtemplates.manage` | — | `{ items: [{ id, key, name, type, sourceCatalogId, sourceCatalogName, position, isActive, revision, usedBy: [{ definitionId, name }] }] }` |
| POST (nueva) | `/api/forms/person-attributes` | `formpersondata.manage` | `{ key, name, type, sourceCatalogId }` | 200 con el dato; 400; 409 `TooMany` |
| PUT (nueva) | `/api/forms/person-attributes/{id}` | ídem | `{ expectedRevision, name, type, sourceCatalogId, position, isActive }`; la clave no cambia | 200; 409 `RevisionConflict` o `InUse` |
| GET (nueva) | `/api/forms/person-data` | `formpersondata.manage` y `users.read` (los dos; fijado acá) | `?page&pageSize&search&sort` | Página de `{ userId, displayName, values: { "<dato>": { value, text } } }` |
| GET (nueva) | `/api/forms/person-data/{userId}` | `users.manage` | — | `{ userId, revision \| null, values }` |
| PUT (nueva) | `/api/forms/person-data/{userId}` | `users.manage` | `{ expectedRevision \| null, values: { "<dato>": "<canónico>" \| null } }` | 200; 409 `Forms.PersonData.RevisionConflict` |
| GET (nueva) | `/api/forms/recipient-samples` | formtemplates.read + formtemplates.manage | `?page&pageSize&search` | Página de `{ id, name }`, el mismo lector que las personas de ejemplo de Filters |
| POST (nueva) | `/api/forms/definitions/{id}/recipient-previews` | formtemplates.read + formtemplates.manage | `{ type, source: { catalog, match, presentation }, sampleUserId, culture }` | Abajo |
| POST y PUT (cambia) | borrador, `publication-checks`, `publications` | como hoy | `source.match` y `source.presentation` | El chequeo suma `warnings[]` |
| POST y GET (cambia) | planilla, detalle y export | como hoy | — | La columna suma `fields: [{ fieldId, key, label, type, role }]`; la opción elegida suma `fields: [{ fieldId, label, type, role, value, text }]` |

**«Como si lo recibiera»** (`FormRecipientPreviewResponse`):

```json
{
  "recipient": { "id": "…", "name": "Juan Gómez",
                 "values": [ { "attributeId": "…", "name": "Zona", "text": "Centro" } ] },
  "state": "Ready",
  "optionCount": 14, "limit": 20, "exceedsLimit": false,
  "options": [ { "id": "…", "title": "Frío Sur S.A.", "description": "Frío · 30" } ],
  "presentationCheck": { "titleTooLongCount": 0, "examples": [] }
}
```

- `state` puede ser `Ready`, `RecipientDataMissing` o `FilterUnavailable`.
- `options` trae hasta `limit` (200 o 20 según `type`) y nunca devuelve `FieldValuesJson`.

**Avisos del chequeo** (`FormPublicationWarningResponse`):

```json
"warnings": [
  { "fieldId": "…", "path": "draft.fields[2].source.match", "code": "Forms.Publication.OptionsPerRecipientHigh",
    "message": "…", "limit": 20, "maximum": 18, "maximumLabel": "Zona Sur" },
  { "fieldId": "…", "path": "draft.fields[2].source.match", "code": "Forms.Publication.RecipientsWithoutData",
    "message": "…", "count": 2, "people": ["Pablo Sosa", "Marta Ruiz"] }
]
```

- `people` trae como mucho 5 nombres (fijado acá).
- `canAccept` depende solo de `issues`.

**Ejemplos que hay que exportar para el front (E2):**
- **Todas las cohortes de catálogos, juntas** (como pide el corte 1): `catalogs-http/manual-current`, `filters`, `k6-source` e `imports`, `forms-catalog-proposals-http` y `whatsapp-catalogs-http`. Con campos, presentación, `presentationCheck`, `fieldIdentityMap`, 400 por tipo, `FieldInUse` y `SourceInUse`.
- `forms-person-data-http` (nueva cohorte): atributos, tabla, datos de una persona, 409 y 403.
- `forms-http`: borrador con `match` y `presentation`; chequeo con `warnings`; `recipient-previews` en sus tres estados.
- `forms-responses-http`: planilla, detalle y Excel con subcolumnas y una respuesta anterior a los campos.
- `whatsapp-catalogs-http`: preparación con `RecipientDataMissing` y `CatalogFilterUnavailable`.

### 6.3 Inventario y OpenAPI

- **Inventario:** [`FormsTestModule.cs`](../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormsTestModule.cs) suma tres rutas en E1 y ocho en E2:
  - E1: `POST /api/forms/catalogs/{id:guid}/import-previews`, `POST /api/forms/catalogs/{id:guid}/imports` y `GET /api/forms/catalogs/{id:guid}/export`;
  - E2: `GET` y `POST /api/forms/person-attributes`, `PUT /api/forms/person-attributes/{id:guid}`, `GET /api/forms/person-data`, `GET` y `PUT /api/forms/person-data/{userid:guid}`, `GET /api/forms/recipient-samples` y `POST /api/forms/definitions/{id:guid}/recipient-previews`.
- **OpenAPI:**
  - la exportación declara el binario como [`FormExportsController`](../../src/ArquitecturaBase.Api/Modules/BusinessForms/Controllers/FormExportsController.cs);
  - las dos rutas multipart declaran `[Consumes("multipart/form-data")]` y, a mano, `[ProducesProblem(400)]` y `[ProducesProblem(409)]`: la convención deduce el 400 de un body o una query, no de un formulario (a confirmar);
  - el contrato multipart es `[FromForm] FormCatalogImportHttpRequest` con `IFormFile File`;
  - [`ControllerInputContractTests`](../../tests/ArquitecturaBase.ArchitectureTests/ControllerInputContractTests.cs) hoy mira solo body y query: se extiende a `BindingSource.Form` con su caso de control.
- **Límites:**
  - las rutas multipart llevan `[RequestSizeLimit]` de 10 MiB (a confirmar con la medición) y la política `forms-export` (una a la vez por persona);
  - el resto conserva 512 KiB y `forms-read` o `forms-write`.

## 7. Errores nuevos

Declarados en Domain (`<Entidad>Errors`), con texto en [`Errors.resx`](../../src/ArquitecturaBase.Application/Resources/Errors.resx) y `Errors.en.resx`.
- Los de fila de la importación son `Error` declarados que viajan dentro de la vista previa, no como ProblemDetails.
- Los que llevan detalle (filas, conteos, usos) lo mandan en `Metadata`, que `ProblemDetailsMapper` copia a las extensiones.

### 7.1 E1

| Código | Tipo | Español | Inglés |
| --- | --- | --- | --- |
| `Forms.Catalog.TooManyItems` | Validation | Un catálogo admite hasta 10.000 opciones. | A catalog can have up to 10,000 options. |
| `Forms.Catalog.ItemValueRequired` | Validation | Cargá el valor de la opción. | Enter the option value. |
| `Forms.Catalog.ItemValueInvalid` | Validation | El valor no corresponde al tipo del catálogo. | The value doesn't match the catalog type. |
| `Forms.CatalogImport.FileUnreadable` | Validation | No pudimos leer el archivo. Subí un XLSX o un CSV. | We couldn't read the file. Upload an XLSX or CSV file. |
| `Forms.CatalogImport.FileTooLarge` | Validation | El archivo supera el tamaño permitido. | The file exceeds the allowed size. |
| `Forms.CatalogImport.TooManyRows` | Validation | La planilla supera las 10.000 filas. | The sheet has more than 10,000 rows. |
| `Forms.CatalogImport.TooManyColumns` | Validation | La planilla tiene demasiadas columnas. | The sheet has too many columns. |
| `Forms.CatalogImport.MissingColumn` | Validation | Falta una columna obligatoria. | A required column is missing. |
| `Forms.CatalogImport.DuplicateColumn` | Validation | Hay dos columnas con el mismo encabezado. | Two columns have the same header. |
| `Forms.CatalogImport.UnknownColumn` | aviso | Esta columna no es del catálogo: se ignora. | This column isn't part of the catalog: it's ignored. |
| `Forms.CatalogImport.SheetChanged` | Conflict | La planilla no es la que revisaste. Revisala de nuevo. | This isn't the sheet you reviewed. Review it again. |
| `Forms.CatalogImport.ConfirmationRequired` | Validation | Confirmá las bajas o las filas con errores antes de aplicar. | Confirm the deactivations or the rows with errors before applying. |
| `Forms.CatalogImport.RequiredValue` | fila | Falta: es obligatorio. | Missing: it's required. |
| `Forms.CatalogImport.DuplicateCode` | fila | Repetido: también está en otra fila. | Duplicate: it's also in another row. |
| `Forms.CatalogImport.TextTooLong` | fila | Tiene más de 30 caracteres: WhatsApp muestra hasta 30. | Longer than 30 characters: WhatsApp shows up to 30. |
| `Forms.CatalogImport.TooLong` | fila | Supera el largo permitido. | It exceeds the allowed length. |
| `Forms.CatalogImport.ActiveInvalid` | fila | Escribí «Sí» o «No». | Write "Yes" or "No". |
| `Forms.CatalogImport.DuplicateText` | aviso | El texto se repite: en WhatsApp se verían iguales. | Duplicate text: they'd look the same in WhatsApp. |
| `Forms.Publication.CatalogTooLargeForMultiple` | Validation | En una pregunta de varias, WhatsApp muestra hasta 20 opciones. Filtrá las opciones por persona o esperá la búsqueda en vivo. | In a multiple-choice question, WhatsApp shows up to 20 options. Filter the options by person or wait for live search. |
| `Forms.Record.CatalogTooLargeForMultiple` | Conflict | A esta persona le tocarían más de 20 opciones en una pregunta de varias. Ajustá los filtros antes de mandar. | This person would get more than 20 options in a multiple-choice question. Adjust the filters before sending. |

Una celda de valor inválido usa el código compartido `Forms.Catalog.ItemValueInvalid` en la incidencia de fila y columna `value`.

Se reutilizan:
- `Forms.Catalog.DuplicateItemCode`, ahora en `items[i].code`;
- `Forms.Catalog.RevisionConflict`, también al aplicar;
- `Forms.Catalog.ManualItemsUnavailable`, para pegar o importar en Filters;
- `Forms.Record.InvalidResponse`, para mínimo, máximo, repetidos u opción no ofrecida;
- `Forms.Record.CatalogItemInactive`;
- `Forms.Record.FilterInvalid`, para «alguna de» mal formado u ordenar por una pregunta de varias.

### 7.2 E2

| Código | Tipo | Español | Inglés |
| --- | --- | --- | --- |
| `Forms.Catalog.TooManyFields` | Validation | Un catálogo admite hasta 20 campos. | A catalog can have up to 20 fields. |
| `Forms.Catalog.FieldKeyDuplicate` | Validation | Ya hay un campo con esa clave. | There's already a field with that key. |
| `Forms.Catalog.FieldInvalid` | Validation | Revisá el tipo, las reglas y el papel del campo. | Check the field type, rules and role. |
| `Forms.Catalog.FieldInUse` | Conflict | El campo está en uso. Sacá esos usos para quitarlo. | The field is in use. Remove those uses to delete it. |
| `Forms.Catalog.FieldRemovalNotConfirmed` | Validation | Confirmá que querés quitar el campo y sus valores. | Confirm you want to remove the field and its values. |
| `Forms.Catalog.FieldTypeNotConvertible` | Validation | Hay valores que no se convierten al tipo nuevo. | Some values can't be converted to the new type. |
| `Forms.Catalog.FieldValuesMissing` | Validation | Faltan valores en opciones activas para hacerlo obligatorio. | Some active options have no value, so it can't be required. |
| `Forms.Catalog.FieldValueInvalid` | Validation | El valor no corresponde al tipo del campo. | The value doesn't match the field type. |
| `Forms.Catalog.FieldValueRequired` | Validation | Este campo es obligatorio. | This field is required. |
| `Forms.Catalog.FieldValueDuplicate` | Validation | Ese valor ya está en otra opción y el campo es único. | That value is already used by another option and the field is unique. |
| `Forms.Catalog.SourceInUse` | Conflict | Un campo o un dato de las personas toma sus valores de este catálogo: no se puede dar de baja. | A field or person data takes its values from this catalog: it can't be deactivated. |
| `Forms.Catalog.PresentationInvalid` | Validation | El título necesita al menos un campo, y los campos tienen que existir. | The title needs at least one field, and the fields must exist. |
| `Forms.PersonAttribute.NotFound` | NotFound | El dato no existe. | The data doesn't exist. |
| `Forms.PersonAttribute.RevisionConflict` | Conflict | Otra persona cambió el dato. Volvé a cargarlo. | Someone else changed this data. Reload it. |
| `Forms.PersonAttribute.KeyDuplicate` | Validation | Ya hay un dato con esa clave. | There's already data with that key. |
| `Forms.PersonAttribute.InUse` | Conflict | Un filtro usa este dato: no se puede quitar ni cambiar su tipo. | A filter uses this data: it can't be removed or change type. |
| `Forms.PersonAttribute.TooMany` | Conflict | Se pueden definir hasta 20 datos. | You can define up to 20 data items. |
| `Forms.PersonData.RevisionConflict` | Conflict | Otra persona cambió estos datos. Volvé a cargarlos. | Someone else changed this data. Reload it. |
| `Forms.PersonData.ValueInvalid` | Validation | El valor no corresponde al dato. | The value doesn't match the data type. |
| `Forms.PersonData.UserNotFound` | NotFound | La persona no existe. | The person doesn't exist. |
| `Forms.Publication.MatchInvalid` | Validation | El filtro usa un campo inexistente, inactivo o que no es de papel «filtro». | The filter uses a field that doesn't exist, is inactive or isn't a filter. |
| `Forms.Publication.MatchSourceMismatch` | Validation | El campo y el dato de la persona tienen que elegir del mismo catálogo. | The field and the person data must use the same catalog. |
| `Forms.Publication.CatalogTitleTooLong` | Validation | Con lo que define el catálogo, hay opciones con título de más de 30 caracteres. | With the catalog setting, some option titles exceed 30 characters. |
| `Forms.Publication.QuestionTitleTooLong` | Validation | Con el título propio de la pregunta, hay opciones de más de 30 caracteres. | With the question's own title, some options exceed 30 characters. |
| `Forms.Publication.OptionsPerRecipientHigh` | aviso | Algunas personas recibirían más opciones de las que muestra WhatsApp. | Some people would get more options than WhatsApp shows. |
| `Forms.Publication.RecipientsWithoutData` | aviso | Hay personas sin el dato del filtro: no van a poder recibirlo. | Some people lack the filter data: they won't be able to receive it. |
| `Forms.Record.RecipientDataMissing` | Conflict | No se puede mandar: a esta persona le falta un dato que usa el filtro de la pregunta. Cargáselo en «Editar usuario». | It can't be sent: this person is missing data used by the question filter. Add it in "Edit user". |
| `Forms.Record.CatalogFilterUnavailable` | Conflict | No se puede mandar: el filtro de la pregunta usa un campo o un dato que ya no está activo. | It can't be sent: the question filter uses a field or data that is no longer active. |

- El título demasiado largo al mandar reutiliza `Forms.Record.CatalogOptionInvalid`, como hoy.
- Los textos de los avisos se completan en el front con `maximum`, `maximumLabel` y `people`.

## 8. Reglas y validaciones

### 8.1 Tipos y formato canónico

| Tipo | HTTP y `Value` | `FieldValuesJson` | Pantalla (es-AR) | Excel |
| --- | --- | --- | --- | --- |
| Texto | string recortado | string | tal cual | texto |
| Entero | `"-?[0-9]{1,18}"` sin ceros a la izquierda | número | `1.500` | número |
| Decimal | `CanonicalDecimal`; hasta la precisión de `System.Decimal` | string canónico | `1.500,50` | número |
| Fecha | `"AAAA-MM-DD"` | string | `21/10/2026` | fecha |
| Sí o no (solo E2) | `"true"` o `"false"` | booleano | Sí / No | booleano |
| Elegir de un catálogo (solo E2) | id `"D"` de una opción activa del catálogo fuente | string | el texto de la opción | el texto |

- El front lee y formatea con una sola pieza (`catalogValues.ts`, nuevo), que usa [`decimal.ts`](../../../ArquitecturaBaseFront/src/shared/lib/decimal.ts) y [`dateTime.ts`](../../../ArquitecturaBaseFront/src/shared/lib/dateTime.ts).
- Al importar o pegar se aceptan también los formatos de pantalla:
  - «1.500,50» y «1500.50»;
  - «21/10/2026»;
  - «Sí», «No» y «Yes»;
  - el código o el texto de la opción fuente.
- El servidor guarda siempre lo canónico.

### 8.2 Topes

| Qué | Tope | Dónde se controla |
| --- | --- | --- |
| Opciones por catálogo | 10.000, fijo | `BusinessFormCatalog.MaxItems`, validador, núcleo de guardado, importación |
| Texto, detalle y código de una opción | 30, 300 y 80 | como hoy |
| Archivo de importación | XLSX o CSV hasta 10 MiB, 10.000 filas de datos y 64 columnas; un XLSX descomprimido, hasta 100 MiB (a confirmar) | `IFormSpreadsheetReader`, antes de cargarlo |
| Ítems en un PUT | hasta 10.000 (antes 500) dentro de los 512 KiB; `moves` sigue en 500 | `FormCatalogRequestValidators.cs` |
| Opciones en el desplegable / en elegir varias | 200 / 20 por persona | `FormCatalogOfferPolicy` y la comprobación |
| Mínimo y máximo de elegir varias | `0 ≤ min ≤ max ≤ 20`, `max ≥ 1`; obligatoria si y solo si `min ≥ 1`, como en archivos | validador del borrador |
| Opciones propias en elegir varias | 1 a 20 (fijado acá: el `CheckboxGroup` muestra hasta 20) | validador y compilador |
| Campos extra por catálogo | 20 | `FormCatalogFieldPolicy` |
| Texto de un campo | `maxLength` hasta 300 | ídem |
| Filtros por pregunta | uno por campo | validador del borrador |
| Datos de las personas | 20 | `FormPersonDataService` |
| Título y descripción en WhatsApp | 30 y 300 | `FormCatalogPresentationPolicy` |

### 8.3 Unicidad y obligatoriedad

- **Código:** único en el catálogo, sin distinguir mayúsculas, en Application. La base lo cierra con la clave diferida, que permite intercambiarlos en un guardado.
- **Texto repetido:** solo avisa, en la tabla, al pegar y al importar.
- **Valor (E1-B):** obligatorio en todos los ítems de un catálogo tipado.
- **Clave de campo:** `^[a-z][a-z0-9_]{0,39}$`, única en el catálogo, inactivos incluidos. No cambia después de guardar.
- **Campo obligatorio:** exige valor en las opciones activas. Pasarlo a obligatorio controla todas; si faltan, `FieldValuesMissing` con la cantidad.
- **Campo único:** sin repetidos entre las opciones activas (fijado acá), bajo el lock del catálogo.
- **Clave de dato:** única entre los datos.
- **Dato por persona:** un valor por dato. Si es de catálogo, una opción activa de su catálogo fuente.
- **Cambiar el tipo:**
  - convierte los hasta 10.000 valores con `FormCatalogFieldPolicy`;
  - si alguno no entra, no guarda y devuelve en `Metadata` cuántos se convierten y hasta 50 filas que no;
  - a `catalog` se convierte por código o texto de la opción fuente.
- **Un campo nuevo:** nace opcional; los ítems no mandan valores para un campo sin id.

### 8.4 Protecciones de la importación

- **Antes de leer:**
  - el catálogo existe, es Manual y está en la revisión esperada; si no, 404, `ManualItemsUnavailable` o 409;
  - el archivo es XLSX (firma ZIP con `xl/workbook.xml`) o CSV en UTF-8, con o sin BOM;
  - el separador del CSV se detecta en el encabezado: coma, punto y coma o tabulación (a confirmar).
- **Al leer:**
  - se toma la primera hoja;
  - no se ejecutan fórmulas ni macros: de una celda con fórmula se usa el valor guardado en el archivo, sin recalcular (a confirmar), y si no lo tiene es error de celda;
  - se rechazan topes de tamaño, filas y columnas antes de cargar todo;
  - la vista previa no guarda el archivo ni sus filas.
- **Encabezados:**
  - se reconocen sin distinguir mayúsculas ni tildes, en español o inglés: Código o Code; Texto o Text; Texto en inglés o English text; Detalle o Detail; Detalle en inglés o English detail; Activa o Active; «Valor (…)» o «Value (…)»;
  - en E2, cada campo por su clave o su nombre es/en;
  - falta Código o Texto → `MissingColumn`; repetida → `DuplicateColumn`; desconocida → aviso y se ignora;
  - la fila de reglas de la plantilla (fila 2, sin código) se saltea si coincide con la que genera el servidor para ese catálogo, en es o en (fijado acá).
- **Por fila:**
  - código y texto obligatorios;
  - texto de hasta 30, detalle de hasta 300 y código de hasta 80;
  - código repetido en la planilla, con la otra fila;
  - valor y campos por tipo;
  - Activa vacía es Sí.
- **El plan:**
  - con `Replace`, lo que no viene se inactiva y lo que vuelve se reactiva;
  - con `Append`, lo que coincide por código se actualiza y lo nuevo va al final, sin bajas;
  - lo nuevo se agrega al final en el orden de la planilla; lo existente conserva su posición (fijado acá);
  - el total resultante no pasa de 10.000.
- **Confirmaciones al aplicar**, con 400 `ConfirmationRequired` si faltan:
  - planilla sin filas válidas: `confirmEmpty`;
  - bajas de más del 25 % de las activas: `confirmDeactivations` igual a la cantidad del plan (a confirmar el umbral);
  - filas con errores: `confirmErrors`, que es el botón «Aplicar sin las N filas con errores»; si son muchas (`manyErrors`, más del 10 % de las filas), el front pide además marcar una casilla, como con las bajas (fijado acá).
- **Aplicar:**
  - vuelve a leer y a armar el plan;
  - exige la misma huella `sheetSha256` (si no, 409 `SheetChanged`) y la revisión;
  - aplica bajo el lock con el mismo núcleo del PUT;
  - sube la revisión una vez y guarda la marca `LastImportSha256` y `LastImportAtUtc` con `TimeProvider`;
  - aplicar dos veces la misma vista previa da 409 por revisión.
- **Exportar:** todas las opciones, activas y dadas de baja, con las columnas de la plantilla, en la cultura del pedido. Reimportar lo exportado da cero diferencias.

### 8.5 «En uso»

- **Un catálogo (conteo «Usado en N formularios»):**
  - `FindUsageCounts` pasa a unir las preguntas de `FormDefinitions.SchemaJson -> 'fields'` con las de la versión activa (`BusinessFormVersions.SchemaJson -> 'questions'`);
  - cuenta cada definición una vez, excluye las eliminadas e incluye las dadas de baja, como hoy.
- **Un campo extra está en uso si:**
  - un `match` lo usa, en el borrador o en la versión activa de algún formulario;
  - una `presentation` de pregunta lo nombra, en el mismo alcance;
  - el `PresentationJson` del catálogo lo nombra.
  - La respuesta dice dónde: formulario, pregunta y borrador o activa.
- **Un catálogo como fuente de valores** está en uso si un campo de otro catálogo o un dato de las personas lo tiene como `sourceCatalogId`. No se da de baja mientras tanto.
- **Un dato de las personas** está en uso si un `match` lo usa en el borrador o en la versión activa. No se quita ni cambia de tipo.
- Lo calculan lectores con SQL fijo (`jsonb_array_elements` y contención), sin consultas por fila.

## 9. WhatsApp

### 9.1 `CheckboxGroup` y el perfil r9 (E1-C3)

- `WhatsAppFlowProfile.Revision` pasa de `flow-static-7.3-r8` a `flow-static-7.3-r9`.
- Lo publicado en r8 conserva su JSON, huella y bindings: nunca se recompila.
- **`WhatsAppFlowCompiler`, para `multiChoice`:**
  - control `CheckboxGroup` con `name`, `label` (hasta 30; a confirmar contra la referencia 7.3) y `required`;
  - `min-selected-items` y `max-selected-items` de `Selection`;
  - `data-source` dinámico `${data.<key>_options}` para un catálogo, o la lista propia de hasta 20;
  - `description` con «Elegí hasta N» o «Choose up to N», de `FlowTexts`, salvo que la pregunta tenga ayuda (fijado acá; a confirmar el largo).
- **Presupuesto de respuesta:** `2 + max × 39` bytes por pregunta, como los demás tipos.
- **Mapeo sellado:** `WhatsAppFlowFieldMapping` y `WhatsAppFlowProjectionField` suman `Selection` con `JsonIgnore(WhenWritingNull)`.
- `ProjectionIsWellFormed` y la lista de tipos del compilador admiten `multiChoice`.
- `StructuredFormQuestion` suma `Selection` con la misma regla. El chequeo y la huella de lo anterior no cambian.

### 9.2 Prellenado con un conjunto de perfiles (E1-0)

- `WhatsAppFlowPrefillPolicy` compara contra `WhatsAppFlowProfile.PrefillRevisions = ["flow-static-7.3-r8", "flow-static-7.3-r9"]`, no contra `Revision`.
- Sin esto, publicar en r9 apaga el prellenado de los formularios r8 (hoy `projection.ProfileRevision != ProfileRevision` devuelve `UnsupportedConfiguration`).
- Las preguntas de varias no tienen prellenado: BusinessForms no las propone, porque las listas de tipos del asistente ([`FormAssistantCapabilityCodec`](../../src/ArquitecturaBase.Application/Modules/BusinessForms/Services/FormAssistantCapabilityCodec.cs), [`FormSuggestionPolicy`](../../src/ArquitecturaBase.Application/Modules/BusinessForms/Services/FormSuggestionPolicy.cs)) no incluyen `multiChoice`.
- La política rechaza una clave `multiChoice` por las dudas de un dato mal formado.

### 9.3 Listas en las respuestas: archivos u opciones (E1-0)

- Hoy `TryReadResponse` toma **todo** array como archivo.
- **Pasa a guardar los arrays crudos aparte** y, después de leer la proyección, los clasifica **por el mapeo**:
  - `photo` o `document` → `WhatsAppFlowMediaPolicy.Decode`, que exige objetos;
  - `multiChoice` → `WhatsAppFlowValueCodec` exige strings `"D"` distintos, ofrecidos (oferta de catálogo u opciones propias) y entre `min` y `max`;
  - un array en otra clave, o un array del tipo equivocado, es `FlowErrors.InvalidValue`.
- **La respuesta canónica** que va a la captura es el array JSON de ids, en el orden recibido, como string en `Answers[key]`.
- **Vacío:** `[]` o la clave ausente es «sin respuesta», válido solo si `min = 0` (a confirmar el formato real en el piloto).
- Casillas y archivos conviven en el mismo formulario.

### 9.4 `Shown` y qué viaja (E2-3)

- La preparación arma `Shown { Title, Description }` por opción con `FormCatalogPresentationPolicy`, en la cultura del envío, y lo sella en la oferta.
- **`WhatsAppMessagePayload.FlowNavigation`:**
  - con `Shown`, `title = Shown.Title` y `description = Shown.Description`, omitida si está vacía;
  - sin `Shown`, `Text` y `Detail`, como hoy.
- No viajan a Meta `FieldValuesJson`, el código, el valor ni los campos no elegidos.
- El filtrado por persona se hace antes de sellar.
- `FormCatalogOfferPolicy.Validate` controla `Shown.Title` (30) y `Shown.Description` (300).
- La captura valida la oferta sellada como hoy.
- `StructuredFormQuestion`, la compilación y la huella de publicación **no cambian** en E2.

### 9.5 Lo que no cambia

- E1-B: el valor tipado no viaja; WhatsApp sigue por opción.
- Los catálogos Filters siguen leyendo Usuarios con su SELECT fijo y su tope de 201.
- Una sesión abierta conserva su oferta sellada: lo enviado no cambia aunque cambie el catálogo.

## 10. Permisos

| Quién | Qué | Permiso |
| --- | --- | --- |
| Ver un catálogo, sus ítems, sus campos, exportarlo y bajar la plantilla | lectura | `formcatalogs.read` |
| Crear y editar un catálogo, su tipo, la tabla, pegar, importar, campos y «En WhatsApp» del catálogo | escritura | `formcatalogs.read` + `formcatalogs.manage` |
| En una pregunta: elegir varias, «Qué opciones ve cada persona», «Propio de esta pregunta» y «Como si lo recibiera» | escritura del borrador | `formtemplates.read` + `formtemplates.manage`; nombrar catálogos en el selector sigue pidiendo leerlos, como hoy |
| Comprobar y publicar | | `formtemplates.read` + `formtemplates.publish` |
| Definir los datos de las personas | **nuevo** | `formpersondata.manage` (`BusinessFormsPermissions.PersonDataManage`) |
| Ver la tabla de personas con sus valores | lectura de datos personales | `formpersondata.manage` y `users.read` (fijado acá) |
| Cargar los valores de una persona («Editar usuario» → «Datos») | | `users.manage` |
| Leer la lista de datos (nombres y tipos, sin valores) | | cualquiera de los tres de arriba, con `HasAnyPermission` |
| Planilla, detalle y chat con valores y campos sellados | | `formrecords.read`; Excel, además `formrecords.export` |

**El permiso nuevo** sigue la [guía](../guides/permiso-nuevo.md) para módulos:
- constante y `All` en [`BusinessFormsPermissions.cs`](../../src/ArquitecturaBase.Domain/Modules/BusinessForms/BusinessFormsPermissions.cs);
- textos en [`Permissions.resx`](../../src/ArquitecturaBase.Application/Modules/BusinessForms/Resources/Permissions.resx) y `Permissions.en.resx`: «Definir datos de las personas» y «Qué datos tiene cada persona para filtrar catálogos»;
- la fuente existente ([`FormPermissionCatalogSource`](../../src/ArquitecturaBase.Application/Modules/BusinessForms/Channels/FormPermissionCatalogSource.cs)) lo publica sin cambios;
- el seed se lo da a Admin al arrancar;
- el formato `^[a-z]+\.[a-z]+$` se cumple.

**Quién ve qué:**
- «Como si lo recibiera» le muestra a quien administra formularios el valor del dato de la persona de ejemplo, como pide el resumen. No muestra otros datos de esa persona.
- Los avisos de la comprobación nombran hasta 5 personas sin el dato. Los ve quien publica.
- Los campos de filtro son datos del catálogo y se ven con `formcatalogs.read`. Su valor nunca va a Meta salvo que se elija en la presentación.
- **Sin el módulo:** no existen las rutas, el permiso, la capacidad `forms` ni la sección «Datos». El núcleo de usuarios no nombra formularios.

## 11. Árbol de archivos

Solo lo que se toca. Etapas: E1-0, E1-B, E1-A, E1-C1…C4, E2-0, E2-1…E2-4. Rutas relativas a la raíz de cada repo.

### 11.1 `ArquitecturaBase`

```text
src/ArquitecturaBase.Domain/Modules/BusinessForms/
  BusinessFormCatalog.cs (cambia) E1-0 MaxItems = 10.000 · E1-B ValueType y CreateManual con tipo · E1-A MarkImported(sha, nowUtc)
  BusinessFormCatalogItem.cs (cambia) E1-B Value en Create y Update
  BusinessFormCatalogValueType.cs (nuevo) E1-B enum Text, Integer, Decimal, Date
  BusinessFormRecord.cs (cambia) E1-C1 BusinessFormRecordValue.Position (0–19) en Create
  BusinessFormRecordReference.cs (cambia) E1-B Value y ValueType · E2-1 FieldValuesJson sellado
  FormQuestionType.cs (cambia) E1-C1 MultiChoice
  FormQuestionIdentity.cs (cambia) E1-C1 admite MultiChoice
  FormCatalogErrors.cs (cambia) E1-0 TooManyItems · E1-B ItemValue* · E2-1 Field*, SourceInUse, PresentationInvalid
  FormCatalogImportErrors.cs (nuevo) E1-A errores de archivo y de fila de la importación
  FormRecordErrors.cs (cambia) E1-C3 CatalogTooLargeForMultiple · E2-3 RecipientDataMissing, CatalogFilterUnavailable
  FormPublicationErrors.cs (cambia) E1-C1 CatalogTooLargeForMultiple · E2-3 Match*, *TitleTooLong y los dos avisos
  BusinessFormCatalog.Fields.cs (nuevo) E2-1 FieldsJson y PresentationJson; clave única e «inactivo conserva el nombre»
  BusinessFormCatalogField.cs (nuevo) E2-1 definición de campo, tipos y papeles
  BusinessFormPersonAttribute.cs (nuevo) E2-2 dato de las personas, con revisión
  BusinessFormPersonData.cs (nuevo) E2-2 una fila por persona, con revisión
  FormPersonDataErrors.cs (nuevo) E2-2 errores de datos y atributos
  BusinessFormsPermissions.cs (cambia) E2-2 PersonDataManage = "formpersondata.manage"
  AGENTS.md (cambia) E1 y E2 tipo, varias, campos, personas y sellado
src/ArquitecturaBase.Application/Models/StructuredData/
  StructuredFormModels.cs (cambia) E1-C1 StructuredFormQuestion.Selection y StructuredFormSelectionSettings
  StructuredFormPreparationModels.cs (cambia) E1-B Value y ValueType en la oferta · E2-3 Shown
  StructuredRecordReadModels.cs (cambia) E1-B Value y ValueType en StructuredRecordChoiceValue · E2-4 Fields
  AGENTS.md (cambia) E1-C y E2-3 campos opcionales sellados
src/ArquitecturaBase.Application/Modules/BusinessForms/
  BusinessFormsApplicationRegistration.cs (cambia) E1-A, E2-2 y E2-3 servicios, helpers y validadores nuevos
  AGENTS.md (cambia) E1-A puerto lector de planillas · E2-2 datos de las personas
src/ArquitecturaBase.Application/Modules/BusinessForms/Models/
  FormCatalogModels.cs (cambia) E1-B ValueType y Value · E1-A SortAlphabetically · E2-1 campos, presentación y filas de uso
  FormCatalogImportModels.cs (nuevo) E1-A pedidos, plan, vista previa, planilla leída (FormSpreadsheet)
  FormDraftModels.cs (cambia) E1-C1 FormSelectionSettings · E2-3 FormQuestionSource.Match y Presentation, FormCatalogMatch, FormCatalogPresentation
  FormPublicationModels.cs (cambia) E1-B y E2-1 FormRecordReferenceRow con Value, ValueType y FieldValuesJson (JsonIgnore)
  FormPublicationCheckModels.cs (cambia) E2-3 Warnings y FormPublicationWarningResponse
  FormResponseModels.cs (cambia) E1-B ValueType en la columna · E2-4 subcolumnas
  FormPersonDataModels.cs (nuevo) E2-2 pedidos, respuestas y filas
  FormRecipientPreviewModels.cs (nuevo) E2-3 «Como si lo recibiera»
src/ArquitecturaBase.Application/Modules/BusinessForms/Interfaces/Integrations/
  IFormSpreadsheetReader.cs (nuevo) E1-A lee XLSX o CSV con topes, sin fórmulas
src/ArquitecturaBase.Application/Modules/BusinessForms/Interfaces/Persistence/
  IFormCatalogReader.cs (cambia) E1-0 ListAllItemsAsync (≤ 10.000) · E1-C1 CountActiveItemsAsync · E2-0 ListOfferItemsAsync con contención y uso con versión activa · E2-1 usos de campos y de fuente · E2-3 máximo por valor
  IFormRecordRepository.cs (cambia) E2-1 FindCatalogFieldValuesAsync por lote, bajo el lock
  IFormPersonDataRepository.cs (nuevo) E2-2 locks, Get y Add de datos y atributos
  IFormPersonDataReader.cs (nuevo) E2-2 atributos, valores de una persona, tabla y personas sin el dato
  AGENTS.md (cambia) E2-2 orden de locks forms:person-*
src/ArquitecturaBase.Application/Modules/BusinessForms/Interfaces/Services/
  IFormCatalogImportService.cs (nuevo) E1-A vista previa y aplicar
  IFormCatalogExportService.cs (nuevo) E1-A exportación y plantilla
  IFormPersonDataService.cs (nuevo) E2-2 atributos y datos por persona
  IFormRecipientPreviewService.cs (nuevo) E2-3 personas de ejemplo y «Como si lo recibiera»
src/ArquitecturaBase.Application/Modules/BusinessForms/Services/
  FormCatalogService.cs (cambia) E1-0 items[i].code y 10.000 · E1-B tipo · E1-A delega en FormCatalogItemRecorder y orden A–Z · E2-1 campos, presentación, en uso, fuente
  FormCatalogItemRecorder.cs (nuevo) E1-A núcleo de guardado de ítems compartido por el PUT y la importación
  FormCatalogValuePolicy.cs (nuevo) E1-B lectura y forma canónica por tipo · E2-1 también para campos
  FormCatalogImportService.cs (nuevo) E1-A vista previa sin guardar y aplicar con revisión
  FormCatalogImportPolicy.cs (nuevo) E1-A encabezados, filas, plan, confirmaciones y huella
  FormCatalogExportService.cs (nuevo) E1-A XLSX de opciones y plantilla con el escritor existente
  FormConditionChoicePolicy.cs (cambia) E1-0 lee hasta 10.000 · E1-C1 también multiChoice
  FormCatalogOptionsPolicy.cs (cambia) E1-B valor en la oferta · E1-C3 tope 20 · E2-3 datos de la persona, contención, presentación y Shown
  FormCatalogOfferPolicy.cs (cambia) E1-C3 Validate con tope 200 o 20 · E2-3 Shown
  FormCatalogPreparationPolicy.cs (cambia) E1-C3 pasa el tipo de la pregunta · E2-3 motivo estable
  FormDraftPolicy.cs (cambia) E1-C1 tipo multiChoice · E2-0 compara Source?.Catalog (líneas 70 y 72) · E2-3 Normalize ordena Match
  FormPublicationGuard.cs (cambia) E1-C1 más de 20 en varias · E2-3 Match, presentación y avisos
  BusinessFormPublicationService.cs (cambia) E2-3 lista Warnings aparte de Issues
  BusinessFormRecordRecorder.cs (cambia) E1-B sella el valor · E1-C1 varias filas y referencias · E2-1 sella los campos
  FormRecordPresentationPolicy.cs (cambia) E1-C2 lista de opciones · E1-B valor · E2-4 campos sellados
  FormResponsePresentationPolicy.cs (cambia) E1-B columna de valor · E2-4 subcolumnas
  FormRecordFilterPolicy.cs (cambia) E1-C2 anyOf y sin orden en multiChoice
  FormRecordConditionEvaluator.cs (cambia) E1-C2 anyOf
  FormExportPolicy.cs (cambia) E1-B valor como número o fecha · E1-C2 «; » · E2-4 campos
  FormExportService.cs (cambia) E1-B y E2-4 encabezados « · valor» y « · <campo>»
  FormActionMessagePolicy.cs (cambia) E1-C2 lista de opciones en los mensajes
  FormPdfPreviewIssuer.cs (cambia) E1-C2 ejemplo para multiChoice
  FormCatalogPresentationPolicy.cs (nuevo) E2-0 arma título y descripción; la usan editor, comprobación, vista previa y preparación
  FormCatalogFieldPolicy.cs (nuevo) E2-1 definiciones, reglas, conversión de tipo y valores
  FormCatalogMatchPolicy.cs (nuevo) E2-3 valida filtros y arma la contención por persona
  FormPersonDataService.cs (nuevo) E2-2 atributos y datos, con revisión y 409
  FormPersonDataAccessGuard.cs (nuevo) E2-2 permisos vigentes por operación
  FormRecipientPreviewService.cs (nuevo) E2-3 personas de ejemplo y vista previa
  AGENTS.md (cambia) E1 y E2 catálogos, importación, captura, planilla y datos de personas
src/ArquitecturaBase.Application/Modules/BusinessForms/Channels/
  BusinessFormRecordSource.cs (cambia) E2-4 el chat no recibe los campos
  BusinessFormSuggestionAssistantCapabilitySource.cs (cambia) E1-C2 omite las preguntas de varias en sourceValues
  AGENTS.md (cambia) E1-C2 y E2-4
src/ArquitecturaBase.Application/Modules/BusinessForms/Validation/
  FormCatalogRequestValidators.cs (cambia) E1-0 10.000 · E1-B tipo y valor · E1-A orden A–Z · E2-1 campos y presentación
  FormCatalogImportRequestValidators.cs (nuevo) E1-A modo, revisión, huella y confirmaciones
  SaveFormDraftRequestValidator.cs (cambia) E1-C1 multiChoice y selection · E2-3 match y presentation
  FormPersonDataRequestValidators.cs (nuevo) E2-2
  FormRecipientPreviewRequestValidator.cs (nuevo) E2-3
src/ArquitecturaBase.Application/Modules/BusinessForms/Resources/
  Catalogs.resx (cambia) E1-A encabezados, reglas y Sí o No de la plantilla
  Catalogs.en.resx (cambia) E1-A ídem en inglés
  FormCatalogTexts.cs (cambia) E1-A accesores
  Responses.resx (cambia) E1-B « · valor»
  Responses.en.resx (cambia) E1-B « · value»
  Permissions.resx (cambia) E2-2 texto del permiso nuevo
  Permissions.en.resx (cambia) E2-2 ídem
src/ArquitecturaBase.Application/Resources/
  Errors.resx (cambia) E1 y E2 textos de la sección 7
  Errors.en.resx (cambia) E1 y E2 ídem en inglés
src/ArquitecturaBase.Application/Modules/WhatsApp/Configuration/
  WhatsAppFlowProfile.cs (cambia) E1-0 PrefillRevisions · E1-C3 Revision r9
src/ArquitecturaBase.Application/Modules/WhatsApp/Models/
  WhatsAppFlowCompilationResponse.cs (cambia) E1-C3 Selection en el mapeo
  WhatsAppFlowPublicationModels.cs (cambia) E1-C3 Selection en la proyección
src/ArquitecturaBase.Application/Modules/WhatsApp/Services/
  WhatsAppFlowPrefillPolicy.cs (cambia) E1-0 conjunto de perfiles
  WhatsAppFlowReplyService.cs (cambia) E1-0 listas por mapeo · E1-C3 multiChoice
  WhatsAppFlowMediaPolicy.cs (cambia) E1-0 solo arrays de objetos de preguntas de archivo
  WhatsAppFlowValueCodec.cs (cambia) E1-C3 decodifica la selección
  WhatsAppFlowCompiler.cs (cambia) E1-C3 CheckboxGroup
  AGENTS.md (cambia) E1-0 y E1-C3 perfiles, listas y casillas
src/ArquitecturaBase.Application/Modules/WhatsApp/Resources/
  FlowTexts.resx (cambia) E1-C3 «Elegí hasta {0}»
  FlowTexts.en.resx (cambia) E1-C3 «Choose up to {0}»
src/ArquitecturaBase.Infrastructure/Modules/BusinessForms/
  BusinessFormsInfrastructureRegistration.cs (cambia) E1-A lector de planillas · E2-2 repositorio y lector
  AGENTS.md (cambia) E1-A carpeta Import
src/ArquitecturaBase.Infrastructure/Modules/BusinessForms/Import/ (nuevo)
  ClosedXmlFormSpreadsheetReader.cs (nuevo) E1-A XLSX con ClosedXML y despacho a CSV, con topes
  FormCsvReader.cs (nuevo) E1-A CSV RFC 4180, separador detectado, UTF-8
src/ArquitecturaBase.Infrastructure/Modules/BusinessForms/Persistence/
  FormAdvisoryLockKeys.cs (cambia) E2-2 forms:person-attributes y forms:person-data:{id}
  AGENTS.md (cambia) E1 y E2 claves diferidas, Position, GIN, contención y tablas nuevas
src/ArquitecturaBase.Infrastructure/Modules/BusinessForms/Persistence/Configurations/
  BusinessFormCatalogConfiguration.cs (cambia) E1-B ValueType · E1-A marca · E2-1 FieldsJson y PresentationJson; CHECK
  BusinessFormCatalogItemConfiguration.cs (cambia) E1-0 sin índice único de código · E1-B Value · E2-1 FieldValuesJson y GIN
  BusinessFormRecordConfiguration.cs (cambia) E1-C1 Position, índice y CHECK en valores
  BusinessFormRecordReferenceConfiguration.cs (cambia) E1-B Value y ValueType · E1-C1 índice por ItemId · E2-1 FieldValuesJson
  BusinessFormPersonAttributeConfiguration.cs (nuevo) E2-2
  BusinessFormPersonDataConfiguration.cs (nuevo) E2-2
src/ArquitecturaBase.Infrastructure/Modules/BusinessForms/Persistence/Readers/
  FormCatalogReader.cs (cambia) E1-0, E1-C1, E2-0, E2-1 y E2-3 los métodos de IFormCatalogReader
  FormRecordReader.cs (cambia) E1-B y E2-1 hidrata las columnas nuevas de referencias
  FormResponseReader.Filters.cs (cambia) E1-C2 anyOf y sin orden por multiChoice
  FormResponseReader.Facets.cs (cambia) E1-C2 facetas de multiChoice
  FormPersonDataReader.cs (nuevo) E2-2
src/ArquitecturaBase.Infrastructure/Modules/BusinessForms/Persistence/Repositories/
  FormRecordRepository.cs (cambia) E2-1 FindCatalogFieldValuesAsync
  FormPersonDataRepository.cs (nuevo) E2-2
src/ArquitecturaBase.Infrastructure/Modules/BusinessForms/Pdf/
  SkiaFormPdfRenderer.cs (cambia) E1-B «texto · valor» · E1-C2 lista · E2-4 campos de dato
  AGENTS.md (cambia) E1-B, E1-C2 y E2-4
src/ArquitecturaBase.Infrastructure/Modules/WhatsApp/
  WhatsAppMessagePayload.cs (cambia) E2-3 título y descripción desde Shown
src/ArquitecturaBase.Infrastructure/Persistence/Migrations/
  ApplicationDbContextModelSnapshot.cs (cambia) E1 y E2 modelo
  <fecha>_DeferCatalogItemCodeUniqueness.cs (nuevo) E1-0
  <fecha>_AddCatalogValueType.cs (nuevo) E1-B
  <fecha>_AddCatalogImportMarker.cs (nuevo) E1-A
  <fecha>_AddFormMultipleChoiceValues.cs (nuevo) E1-C1
  <fecha>_AddCatalogFieldsAndPresentation.cs (nuevo) E2-1
  <fecha>_AddFormPersonData.cs (nuevo) E2-2
src/ArquitecturaBase.Api/Modules/BusinessForms/
  AGENTS.md (cambia) E1-A y E2 rutas, permisos, multipart y límites
src/ArquitecturaBase.Api/Modules/BusinessForms/Contracts/
  FormCatalogHttpRequests.cs (cambia) E1-B valueType y value · E1-A sortAlphabetically, FormCatalogExportQuery · E2-1 campos y presentación
  FormCatalogImportHttpRequests.cs (nuevo) E1-A contrato multipart con IFormFile
  SaveFormDraftHttpRequest.cs (cambia) E1-C1 selection · E2-3 match y presentation
  FormDraftRequestMapping.cs (nuevo) E2-0 un solo mapeo del borrador y de FormQuestionSource
  FormPersonDataHttpRequests.cs (nuevo) E2-2
  FormRecipientPreviewHttpRequests.cs (nuevo) E2-3
src/ArquitecturaBase.Api/Modules/BusinessForms/Controllers/
  FormCatalogsController.cs (cambia) E1-B, E1-A y E2-1 mapeos nuevos
  FormCatalogImportsController.cs (nuevo) E1-A import-previews, imports y export
  FormDefinitionsController.cs (cambia) E1-C1 selection · E2-0 usa FormDraftRequestMapping
  FormPublicationChecksController.cs (cambia) E1-C1 · E2-0 ídem
  FormPublicationsController.cs (cambia) E1-C1 · E2-0 ídem
  FormPersonAttributesController.cs (nuevo) E2-2 GET, POST y PUT de atributos
  FormPersonDataController.cs (nuevo) E2-2 tabla y datos por persona
  FormRecipientPreviewsController.cs (nuevo) E2-3 recipient-samples y recipient-previews
tests/ArquitecturaBase.Domain.UnitTests/Modules/BusinessForms/
  FormCatalogTests.cs (cambia) E1-B tipo y valor · E2-1 campos
  BusinessFormsPermissionsTests.cs (cambia) E2-2 permiso nuevo
  BusinessFormPersonDataTests.cs (nuevo) E2-2 revisión y valores
tests/ArquitecturaBase.Application.UnitTests/Modules/BusinessForms/
  FormCatalogOfferPolicyTests.cs (cambia) E1-C3 tope 20 · E2-3 Shown
  FormRecordFilterPolicyTests.cs (cambia) E1-C2 anyOf
  FormRecordConditionEvaluatorTests.cs (cambia) E1-C2 anyOf
  FormExportPolicyTests.cs (cambia) E1-B, E1-C2 y E2-4
  FormCatalogPreparationPolicyTests.cs (cambia) E2-3 filtros y motivos
  FormCatalogValuePolicyTests.cs (nuevo) E1-B formatos canónicos
  FormCatalogImportPolicyTests.cs (nuevo) E1-A encabezados, filas, plan y confirmaciones
  FormCatalogPresentationPolicyTests.cs (nuevo) E2-0
  FormCatalogFieldPolicyTests.cs (nuevo) E2-1
  FormCatalogMatchPolicyTests.cs (nuevo) E2-3
  AGENTS.md (cambia) E1 y E2
tests/ArquitecturaBase.Application.UnitTests/Modules/WhatsApp/
  WhatsAppFlowCompilerTests.cs (cambia) E1-C3 CheckboxGroup, presupuesto y huellas r8
  WhatsAppFlowPrefillTests.cs (cambia) E1-0 r8 y r9
tests/ArquitecturaBase.Application.UnitTests/Modules/WhatsApp/Services/
  WhatsAppFlowReplyServiceTests.cs (cambia) E1-0 listas por mapeo · E1-C3 selección
tests/ArquitecturaBase.ArchitectureTests/
  ControllerInputContractTests.cs (cambia) E1-A también [FromForm]
tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/
  FormsTestModule.cs (cambia) E1-A 3 rutas · E2 8 rutas
  FormCatalogEndpointsTests.cs (cambia) E1-0 intercambio de códigos, items[i].code · E1-B · E2-1; exporta la cohorte
  FormCatalogCaptureIntegrationTests.cs (cambia) E1-B valor sellado · E1-C1 · E2-1 campos sellados
  FormRecordCaptureIntegrationTests.cs (cambia) E1-C1 mínimo, máximo, repetidos y replay
  FormResponseQueryTests.cs (cambia) E1-C2 alguna de y facetas
  FormResponseHttpTests.cs (cambia) E1-C2 y E2-4
  FormPdfRendererTests.cs (cambia) E1-B, E1-C2 y E2-4
  FormWorkbookWriterTests.cs (cambia) E1-A plantilla y exportación
  FormPublicationCheckTests.cs (cambia) E1-C1 · E2-3 avisos
  FormHttpContractFixturesTests.cs (cambia) E1-C1 y E2-3 cuerpos para el front
  FormCatalogImportHttpTests.cs (nuevo) E1-A XLSX, CSV, fórmulas, topes, 409, permisos y cohorte
  FormCatalogCapacityTests.cs (nuevo) E1-0 10.000 · E2-1 10.000 × 20 al guardar y al filtrar
  FormCatalogMigrationTests.cs (nuevo) E1 y E2 Up y Down con datos
  FormPersonDataHttpTests.cs (nuevo) E2-2 rutas, permisos, 409 y módulo quitado
  FormRecipientFilterIntegrationTests.cs (nuevo) E2-3 contención, motivos, asistente y payload
  AGENTS.md (cambia) E1 y E2
tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp/
  WhatsAppFlowProfileUpgradeTests.cs (cambia) E1-C3 sesión r8 capturada después de publicar r9
  WhatsAppRealCatalogSendingTests.cs (cambia) E1-C3 casillas · E2-3 Shown y solo lo elegido
tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Modules/BusinessForms/
  export-responses-http.ps1 (cambia) E1-C2 y E2-4 cohortes de respuestas
docs/
  features/formularios.md (cambia) al cerrar cada etapa: reglas implementadas
  features/whatsapp.md (cambia) E1-C3 perfil r9 y casillas · E2-3 Shown
  architecture/mapa-de-instrucciones.md (cambia) Import y datos de las personas
  plans/formularios/README.md (cambia) estado de las etapas
```

### 11.2 `ArquitecturaBaseFront`

```text
src/features/business-forms/api/
  catalogs.ts (cambia) E1-B valueType y value · E1-A sortAlphabetically · E2-1 campos y presentación
  catalogResponse.ts (cambia) E1-B, E1-A y E2-1 guards estrictos
  catalogImports.ts (nuevo) E1-A vista previa, aplicar, exportar y plantilla
  forms.ts (cambia) E2-3 recipient-samples y recipient-previews
  response.ts (cambia) E1-C1 multiChoice y selection · E2-3 match, presentation y warnings
  responses.ts (cambia) E1-C2 celdas y detalle de varias · E1-B valor · E2-4 subcolumnas
  personData.ts (nuevo) E2-2 atributos y datos con sus guards
  catalogCurrentHttpContract.test.ts (cambia) E1 y E2 cohortes de catálogos
  catalogImportsHttpContract.test.ts (nuevo) E1-A
  responseHttpContract.test.ts (cambia) E1-C2 y E2-4
  httpContract.test.ts (cambia) E1-C1 y E2-3
  personDataHttpContract.test.ts (nuevo) E2-2
  AGENTS.md (cambia) E1-A y E2-2 familias nuevas
src/features/business-forms/hooks/
  useCatalogDraftEditor.ts (cambia) E1-A selección, orden A–Z, bloqueo de pegar · E2-1 campos
  useCatalogQueries.ts (cambia) E1-A y E2-1 invalidaciones
  useFormDraftEditor.ts (cambia) E1-C4 bindResult compara selection · E2-3 source
  usePersonData.ts (nuevo) E2-2
  useRecipientPreview.ts (nuevo) E2-3
  useCatalogDraftEditor.test.tsx (cambia) E1-A y E2-1
  AGENTS.md (cambia) E1-A y E2
src/features/business-forms/
  model.ts (cambia) E1-C4 multiChoice · E2-3 source.match y presentation
  catalogDraft.ts (cambia) E1-B valor · E1-A orden A–Z · E2-0 comparación por estructura · E2-1 campos
  catalogItemEdits.ts (cambia) E1-B valor · E2-1 campos
  catalogPaste.ts (nuevo) E1-A interpreta lo pegado y arma el CSV
  catalogEditing.ts (nuevo) E1-A genera los códigos que faltan
  catalogColumns.ts (nuevo) E2-0 descripción de columnas para la tabla y el editor por opción
  catalogValues.ts (nuevo) E2-0 leer, formatear y validar cada tipo (lo usa E1-B)
  catalogPresentation.ts (nuevo) E2-1 compone el ejemplo de título y descripción
  editorModel.ts (cambia) E1-C4 multiChoice
  draftSession.ts (cambia) E1-C4 multiChoice
  validationPaths.ts (cambia) E2-0 items[i].fields.<id> y fields[i].* sin regex fijas
  responses.ts (cambia) E1-0 una sola lectura de las opciones elegidas de una celda
  rules.ts (cambia) E1-C4 anyOf
  suggestions.ts (cambia) E1-C4 excluye multiChoice de los mapeos
  catalogDraft.test.ts (cambia) E1 y E2
  catalogPaste.test.ts (nuevo) E1-A
  catalogValues.test.ts (nuevo) E2-0
  catalogColumns.test.ts (nuevo) E2-0
src/features/business-forms/components/
  FormConditionsEditor.tsx (nuevo) E1-0 un solo editor de condiciones
  FormRulesSection.tsx (cambia) E1-0 usa FormConditionsEditor
  FormSuggestionsSection.tsx (cambia) E1-0 ídem
  SelectionRangeFields.tsx (nuevo) E1-0 mínimo y máximo reutilizables
  FileQuestionSettings.tsx (cambia) E1-0 usa SelectionRangeFields
  CatalogItemsTable.tsx (nuevo) E1-0 tabla separada · E1-A edición en el lugar, selección por página · E2-1 columnas por campo
  CatalogItemFields.tsx (nuevo) E1-0 editor de la fila · E1-B valor · E2-1 campos
  CatalogManualItemsEditor.tsx (cambia) E1-0 compone tabla y editor · E1-A barra Pegar, Importar, Exportar y «⋯»
  CatalogOriginDialog.tsx (cambia) E1-B tipo de valor al crear
  CatalogImportDialog.tsx (nuevo) E1-A dueño de archivo o pegado, revisión, aplicación y recuperación del 409
  CatalogPreview.tsx (nuevo) E1-A «Así lo ve el empleado» con «Una opción / Varias»
  CatalogQuestionSelector.tsx (cambia) E1-B muestra el tipo
  QuestionEditor.tsx (cambia) E1-C4 «Permitir elegir varias» · E2-0 delega en CatalogQuestionSettings
  CatalogQuestionSettings.tsx (nuevo) E2-0 configuración de la pregunta de catálogo · E2-3 filtros y «En WhatsApp»
  FormPreview.tsx (cambia) E1-C4 casillas con «Elegí hasta N» · E2-3 lo que viaja
  DraftReview.tsx (cambia) E1-C4 selection · E2-3 match y presentation
  PublicationCheckDialog.tsx (cambia) E1-C4 issue de 20 · E2-3 lista aparte de avisos
  ResponseValue.tsx (cambia) E1-C4 lista · E1-B valor · E2-4 campos
  ResponseFiltersPanel.tsx (cambia) E1-C4 «alguna de» con chip
  ResponseChoiceMenu.tsx (cambia) E1-C4 conteos por opción
  ResponseColumnsMenu.tsx (cambia) E2-4 subcolumnas de dato y filtro
  ResponseExportDialog.tsx (cambia) E2-4 «Solo las columnas visibles» sigue a las subcolumnas
  CatalogFieldsSection.tsx (nuevo) E2-1 sección «Campos»
  CatalogFieldEditor.tsx (nuevo) E2-1 un campo, quitar y cambiar tipo
  CatalogPresentationSection.tsx (nuevo) E2-1 «En WhatsApp» del catálogo y de la pregunta
  CatalogQuestionMatch.tsx (nuevo) E2-3 «Qué opciones ve cada persona»
  RecipientPreview.tsx (nuevo) E2-3 «Como si lo recibiera»
  PersonDataSection.tsx (nuevo) E2-2 sección «Datos» que aporta el módulo
  QuestionEditor.test.tsx (cambia) E1-C4 y E2-3
  FormRulesSection.test.tsx (cambia) E1-0
  FormSuggestionsSection.test.tsx (cambia) E1-0
  CatalogManualItemsEditor.test.tsx (cambia) E1-0 y E1-A
  AGENTS.md (cambia) E1 y E2
src/features/business-forms/pages/
  CatalogEditorPage.tsx (cambia) E1-A barra, apagados con motivo, «Usado en N formularios» · E2-1 campos
  FormEditorPage.tsx (cambia) E1-C4 lista «varias, hasta N» · E2-3 vista previa por persona
  PersonDataPage.tsx (nuevo) E2-2 /datos-de-personas
  CatalogEditorPage.test.tsx (cambia) E1-A
  AGENTS.md (cambia) E2-2 ruta y permiso nuevos
src/features/whatsapp/api/
  formReplies.ts (cambia) E1-C4 valor de varias en la tarjeta
  formReplies.test.ts (cambia) E1-C4
src/features/whatsapp/lib/
  formReplyValue.ts (cambia) E1-C4 «Frío, Ruido»
src/features/whatsapp/components/
  FormReplyValues.tsx (cambia) E1-C4 lista
  FormCatalogPreparation.tsx (cambia) E2-3 motivos RecipientDataMissing y CatalogFilterUnavailable
src/features/users/components/
  UserEditDialog.tsx (cambia) E2-2 aloja la sección del contexto neutral, guarda aparte y conserva el error
  UserEditDialog.test.tsx (cambia) E2-2
src/features/users/
  AGENTS.md (cambia) E2-2 punto de extensión
src/shared/ui/
  WideTable.tsx (nuevo) E2-0 tabla ancha con columnas fijas y desplazamiento en su caja
  AGENTS.md (cambia) E2-0
src/shared/hooks/
  userDataSectionContext.ts (nuevo) E2-2 contrato neutral de la sección «Datos»
  AGENTS.md (cambia) E2-2
src/app/
  routes.tsx (cambia) E2-2 /datos-de-personas con formpersondata.manage
  UserDataSectionShell.tsx (nuevo) E2-2 aporta la sección si installedFeatures incluye forms
  AGENTS.md (cambia) E2-2
src/layouts/
  navigation.ts (cambia) E2-2 «Datos de las personas» en Administración
  AGENTS.md (cambia) E2-2
src/locales/es/
  forms.json (cambia) E1 y E2
  common.json (cambia) E2-2 navegación
src/locales/en/
  forms.json (cambia) E1 y E2
  common.json (cambia) E2-2
src/test/
  httpCohort.ts (cambia) E1-A y E2-2 familias catalogImports y personData
src/test/visual/
  catalogManual.spec.ts (cambia) E1-A y E2-1 a 390 y 320 px, es y en
  formCatalogQuestion.spec.ts (cambia) E1-C4 y E2-3
  responses.spec.ts (cambia) E1-C4 y E2-4
  chatForms.spec.ts (cambia) E1-C4 tarjeta con varias
  catalogFixtures.ts (cambia) E1 y E2
  personData.spec.ts (nuevo) E2-2
docs/
  mapa-de-instrucciones.md (cambia) E2-2 contrato neutral y pantalla nueva
  design/catalogos-mejoras.md (cambia) al cerrar E1: programado
  design/catalogos-campos-extra.md (cambia) al cerrar E2: programado
```

## 12. Orden de trabajo

Cada corte cierra con build, los tests del área sin Docker y su code review ([validar un cambio](../guides/validar-un-cambio.md)). El cierre de cada etapa corre la integración con Docker, `dotnet test` completo, el front completo y `aspire stop`.

### E1-0 · Arreglos previos, sin cambio visible

Back:
1. **Clave diferida del código:**
   - `BusinessFormCatalogItemConfiguration.cs` sin el índice único;
   - migración `DeferCatalogItemCodeUniqueness`;
   - prueba de intercambio en `FormCatalogEndpointsTests.cs`.
2. **`items[i].code` al editar:** `FormCatalogService.cs`, línea 124, con el índice del pedido.
3. **Tope 10.000:**
   - `BusinessFormCatalog.MaxItems` en `BusinessFormCatalog.cs`;
   - `FormCatalogService.cs` (línea 115) y `FormCatalogRequestValidators.cs` (línea 175);
   - `FormConditionChoicePolicy.cs`, que hoy lee 5 páginas de 100, pasa a `IFormCatalogReader.ListAllItemsAsync`;
   - prueba de rendimiento en `FormCatalogCapacityTests.cs` (nuevo).
4. **Prellenado con perfiles:** `WhatsAppFlowProfile.cs`, `WhatsAppFlowPrefillPolicy.cs` y `WhatsAppFlowPrefillTests.cs`.
5. **Listas por mapeo:** `WhatsAppFlowReplyService.cs`, `WhatsAppFlowMediaPolicy.cs` y `WhatsAppFlowReplyServiceTests.cs`.

Front:
6. `FormConditionsEditor.tsx` (nuevo), usado por `FormRulesSection.tsx` y `FormSuggestionsSection.tsx`.
7. La lectura única de opciones elegidas en `responses.ts`.
8. `SelectionRangeFields.tsx` (nuevo), sacado de `FileQuestionSettings.tsx`.
9. `CatalogItemsTable.tsx` (nuevo) y `CatalogItemFields.tsx` (nuevo), sacados de `CatalogManualItemsEditor.tsx`.

**Recomendado acá** (es E2-0 del front, pero el resumen de E2 aconseja hacerlo ahora): `catalogColumns.ts` (nuevo), `catalogValues.ts` (nuevo), la comparación por estructura en `catalogDraft.ts`, `validationPaths.ts`, `CatalogQuestionSettings.tsx` (nuevo) y `WideTable.tsx` (nuevo).

### E1-B · Tipo de valor

- **Domain:** `BusinessFormCatalogValueType.cs` (nuevo), `BusinessFormCatalog.cs`, `BusinessFormCatalogItem.cs`, `BusinessFormRecordReference.cs` y `FormCatalogErrors.cs`.
- **Migración:** `AddCatalogValueType`, más las configuraciones de catálogo, ítem y referencia.
- **Application:**
  - `FormCatalogValuePolicy.cs` (nuevo), `FormCatalogService.cs`, `FormCatalogModels.cs` y `FormCatalogRequestValidators.cs`;
  - la oferta (`StructuredFormPreparationModels.cs`, `FormCatalogOptionsPolicy.cs`) y el sellado (`BusinessFormRecordRecorder.cs`);
  - las lecturas (`FormPublicationModels.cs`, `StructuredRecordReadModels.cs`, `FormRecordPresentationPolicy.cs`, `FormResponsePresentationPolicy.cs`, `FormExportPolicy.cs`, `FormExportService.cs`) y `Responses.resx`.
- **Infrastructure:** `FormCatalogReader.cs`, `FormRecordReader.cs` y `SkiaFormPdfRenderer.cs`.
- **Api:** `FormCatalogHttpRequests.cs` y `FormCatalogsController.cs`.
- **Front:**
  - `CatalogOriginDialog.tsx`, `CatalogItemsTable.tsx` (nuevo en E1-0), `CatalogItemFields.tsx` (nuevo en E1-0), `CatalogQuestionSelector.tsx`;
  - `catalogs.ts`, `catalogResponse.ts`, `catalogDraft.ts`, `catalogItemEdits.ts`, `catalogValues.ts` (nuevo en E1-0);
  - `ResponseValue.tsx`.
- **Pruebas:** migración sobre datos, `items[i].value`, valor sellado tras editar, Excel tipado y una sesión sellada antes del cambio que conserva su huella.

### E1-A · Carga de una vez

- **Domain:** `BusinessFormCatalog.cs` (marca) y `FormCatalogImportErrors.cs` (nuevo).
- **Migración:** `AddCatalogImportMarker`.
- **Application:**
  - `IFormSpreadsheetReader.cs` (nuevo), `FormCatalogImportModels.cs` (nuevo), `FormCatalogImportPolicy.cs` (nuevo);
  - `FormCatalogItemRecorder.cs` (nuevo), extraído de `FormCatalogService.UpdateCoreAsync` y `CreateCoreAsync`;
  - `IFormCatalogImportService.cs` (nuevo), `FormCatalogImportService.cs` (nuevo), `IFormCatalogExportService.cs` (nuevo), `FormCatalogExportService.cs` (nuevo);
  - `FormCatalogImportRequestValidators.cs` (nuevo), `Catalogs.resx`, `FormCatalogTexts.cs`;
  - orden A–Z en `FormCatalogService.cs`.
- **Infrastructure:** `Import/ClosedXmlFormSpreadsheetReader.cs` (nuevo), `Import/FormCsvReader.cs` (nuevo), `FormCatalogReader.ListAllItemsAsync` y el registro.
- **Api:** `FormCatalogImportHttpRequests.cs` (nuevo) y `FormCatalogImportsController.cs` (nuevo); el inventario en `FormsTestModule.cs`; `ControllerInputContractTests.cs`.
- **Front:**
  - `catalogImports.ts` (nuevo), `catalogPaste.ts` (nuevo), `catalogEditing.ts` (nuevo);
  - `CatalogImportDialog.tsx` (nuevo), `CatalogPreview.tsx` (nuevo), `CatalogManualItemsEditor.tsx`, `CatalogItemsTable.tsx` (nuevo en E1-0);
  - `CatalogEditorPage.tsx` y `useCatalogDraftEditor.ts`.
- **Pruebas:**
  - XLSX y CSV con errores por fila y columna, fórmulas y topes;
  - aplicar dos veces da 409;
  - exportar y reimportar da cero diferencias;
  - permisos, rutas y OpenAPI.

### E1-C · Elegir varias, en cuatro cortes

1. **Base y captura:**
   - `FormQuestionType.cs`, `FormQuestionIdentity.cs`, `BusinessFormRecord.cs`;
   - migración `AddFormMultipleChoiceValues` y las configuraciones de valores y referencias;
   - `FormDraftModels.cs`, `StructuredFormModels.cs`, `SaveFormDraftRequestValidator.cs`, `FormDraftPolicy.cs`;
   - `FormPublicationGuard.cs` (más de 20), `FormCatalogReader.CountActiveItemsAsync`;
   - `BusinessFormRecordRecorder.cs` (normaliza el array y crea filas y referencias);
   - contratos y controllers del borrador.
2. **Lecturas:**
   - `FormRecordPresentationPolicy.cs`, `FormResponsePresentationPolicy.cs`, `FormRecordFilterPolicy.cs`, `FormRecordConditionEvaluator.cs`;
   - `FormResponseReader.Filters.cs` y `FormResponseReader.Facets.cs`;
   - `FormExportPolicy.cs`, `SkiaFormPdfRenderer.cs`, `FormActionMessagePolicy.cs`, `FormPdfPreviewIssuer.cs`;
   - `BusinessFormSuggestionAssistantCapabilitySource.cs`, `FormConditionChoicePolicy.cs`.
3. **WhatsApp r9, codec y webhook:**
   - `WhatsAppFlowProfile.cs` (r9), `WhatsAppFlowCompiler.cs`, `WhatsAppFlowValueCodec.cs`, `WhatsAppFlowReplyService.cs`;
   - los modelos de mapeo y proyección y `FlowTexts.resx`;
   - `FormCatalogOfferPolicy.cs` y `FormCatalogOptionsPolicy.cs` (tope 20 al mandar).
4. **Front:**
   - `model.ts`, `editorModel.ts`, `draftSession.ts`, `useFormDraftEditor.ts`;
   - `QuestionEditor.tsx`, `FormPreview.tsx`, `DraftReview.tsx`, `PublicationCheckDialog.tsx`;
   - `response.ts`, `responses.ts`, `ResponseValue.tsx`, `ResponseFiltersPanel.tsx`, `ResponseChoiceMenu.tsx`;
   - `rules.ts`, `suggestions.ts`;
   - la tarjeta del chat (`formReplies.ts`, `formReplyValue.ts`, `FormReplyValues.tsx`).

La [verificación local de E1](../features/formularios.md#verificación-local-de-catálogos-e1--2026-10-22) conserva los resultados y sus límites; el code review y el [piloto con Meta](../plans/verificacion-y-produccion.md#formularios-catálogos-e1) siguen pendientes.

### E2-0 · Arreglos previos, sin cambio visible

Back:
1. `FormDraftPolicy.cs:70` y `:72` comparan `Source?.Catalog`.
2. `FormDraftRequestMapping.cs` (nuevo) reemplaza los tres mapeos de `FormDefinitionsController.cs` (línea 79), `FormPublicationChecksController.cs` (línea 31) y `FormPublicationsController.cs` (línea 52).
3. `FormCatalogPresentationPolicy.cs` (nuevo), con sus pruebas, sin consumidor visible todavía.
4. `IFormCatalogReader.ListOfferItemsAsync(catalogId, contención)` con `EF.Functions.JsonContains` y tope 201. `null` es lo de hoy.
5. `FindUsageCounts` con la versión activa.

Front: lo que no se haya hecho en E1-0.

### E2-1 · El catálogo

- **Domain:** `BusinessFormCatalog.Fields.cs` (nuevo), `BusinessFormCatalogField.cs` (nuevo), `BusinessFormRecordReference.cs` y `FormCatalogErrors.cs`.
- **Migración:** `AddCatalogFieldsAndPresentation` y las configuraciones de catálogo, ítem y referencia.
- **Application:**
  - `FormCatalogFieldPolicy.cs` (nuevo), `FormCatalogValuePolicy.cs` (nuevo en E1-B), `FormCatalogService.cs`, `FormCatalogItemRecorder.cs` (nuevo en E1-A), `FormCatalogImportPolicy.cs` (nuevo en E1-A), `FormCatalogExportService.cs` (nuevo en E1-A);
  - `FormCatalogModels.cs`, `FormCatalogRequestValidators.cs`;
  - `BusinessFormRecordRecorder.cs` con `IFormRecordRepository.FindCatalogFieldValuesAsync`.
- **Infrastructure:** `FormCatalogReader.cs` (usos de campos y de fuente) y `FormRecordRepository.cs`.
- **Api:** `FormCatalogHttpRequests.cs` y `FormCatalogsController.cs`.
- **Front:**
  - `CatalogFieldsSection.tsx` (nuevo), `CatalogFieldEditor.tsx` (nuevo), `CatalogPresentationSection.tsx` (nuevo, para el catálogo);
  - `CatalogItemsTable.tsx` (nuevo en E1-0) con columnas por campo y menú «Campos»;
  - `catalogPresentation.ts` (nuevo), `catalogDraft.ts`, `catalogItemEdits.ts`, `catalogs.ts`, `catalogResponse.ts`.
- **Reexportar juntas todas las cohortes de catálogos.**

### E2-2 · Datos de las personas

- **Domain:** `BusinessFormPersonAttribute.cs` (nuevo), `BusinessFormPersonData.cs` (nuevo), `FormPersonDataErrors.cs` (nuevo) y `BusinessFormsPermissions.cs`.
- **Migración:** `AddFormPersonData` y las dos configuraciones.
- **Application:**
  - `IFormPersonDataRepository.cs` (nuevo), `IFormPersonDataReader.cs` (nuevo);
  - `IFormPersonDataService.cs` (nuevo), `FormPersonDataService.cs` (nuevo), `FormPersonDataAccessGuard.cs` (nuevo);
  - `FormPersonDataModels.cs` (nuevo), `FormPersonDataRequestValidators.cs` (nuevo) y `Permissions.resx`.
- **Infrastructure:** `FormPersonDataRepository.cs` (nuevo), `FormPersonDataReader.cs` (nuevo) y `FormAdvisoryLockKeys.cs`.
- **Api:** `FormPersonDataHttpRequests.cs` (nuevo), `FormPersonAttributesController.cs` (nuevo) y `FormPersonDataController.cs` (nuevo).
- **Front:**
  - `personData.ts` (nuevo), `usePersonData.ts` (nuevo), `PersonDataPage.tsx` (nuevo), `PersonDataSection.tsx` (nuevo);
  - `userDataSectionContext.ts` (nuevo), `UserDataSectionShell.tsx` (nuevo), `UserEditDialog.tsx`;
  - `routes.tsx`, `navigation.ts` y los textos.

### E2-3 · La pregunta

- **Application:**
  - `FormDraftModels.cs` (`Match`, `Presentation`), `SaveFormDraftRequestValidator.cs`, `FormDraftPolicy.Normalize`;
  - `FormCatalogMatchPolicy.cs` (nuevo), `FormCatalogOptionsPolicy.cs` (los cinco pasos), `FormCatalogOfferPolicy.cs`, `FormCatalogPreparationPolicy.cs`;
  - `StructuredFormPreparationModels.cs` (`Shown`);
  - `FormPublicationGuard.cs`, `BusinessFormPublicationService.cs`, `FormPublicationCheckModels.cs`;
  - `IFormRecipientPreviewService.cs` (nuevo), `FormRecipientPreviewService.cs` (nuevo), `FormRecipientPreviewModels.cs` (nuevo) y su validador.
- **Infrastructure:** `WhatsAppMessagePayload.cs` y `FormCatalogReader.cs` (máximo por valor).
- **Api:** `SaveFormDraftHttpRequest.cs`, `FormDraftRequestMapping.cs` (nuevo en E2-0), `FormRecipientPreviewHttpRequests.cs` (nuevo) y `FormRecipientPreviewsController.cs` (nuevo).
- **Front:**
  - `CatalogQuestionSettings.tsx` (nuevo en E2-0), `CatalogQuestionMatch.tsx` (nuevo), `CatalogPresentationSection.tsx` (nuevo en E2-1, para la pregunta), `RecipientPreview.tsx` (nuevo), `useRecipientPreview.ts` (nuevo);
  - `PublicationCheckDialog.tsx`, `FormPreview.tsx`, `DraftReview.tsx`, `forms.ts`, `response.ts`;
  - `FormCatalogPreparation.tsx` en WhatsApp.
- **Pruebas de huellas** (sección 13).

### E2-4 · Las respuestas

- **Application:** `FormRecordPresentationPolicy.cs`, `FormResponsePresentationPolicy.cs`, `FormExportPolicy.cs`, `FormExportService.cs`, `StructuredRecordReadModels.cs` y `BusinessFormRecordSource.cs` (el chat sin campos).
- **Infrastructure:** `SkiaFormPdfRenderer.cs`.
- **Front:** `responses.ts`, `ResponseValue.tsx`, `ResponseColumnsMenu.tsx` y `ResponseExportDialog.tsx`.

Cierre de E2: lo de [«Cómo se cierra» del resumen](../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23), más el piloto con Meta.

## 13. Pruebas

**Unitarias (sin Docker), TDD donde hay lógica:**
- **Domain:**
  - tipo fijo al crear;
  - valor por ítem;
  - campos: clave única e inactivo con nombre;
  - datos de personas con revisión;
  - el permiso nuevo en `All`.
- **Application:**
  - `FormCatalogValuePolicy`: cada tipo, formatos de pantalla y canónico; decimal con `CanonicalDecimal`;
  - `FormCatalogImportPolicy`: encabezados es y en, fila de reglas, filas, `Replace` y `Append`, bajas, reactivación, confirmaciones, huella, cero diferencias;
  - `FormDraftPolicy`: `multiChoice` como pregunta nueva, `Source?.Catalog`, `Match` ordenado y `[]` como `null`;
  - `FormRecordFilterPolicy` y `FormRecordConditionEvaluator`: `anyOf`;
  - `FormCatalogOfferPolicy`: 200 o 20 y `Shown`;
  - `FormCatalogPresentationPolicy`: « · », vacíos y 30 y 300;
  - `FormCatalogFieldPolicy`: tipos, reglas y conversión;
  - `FormCatalogMatchPolicy`: contención, dato faltante e inactivo;
  - `FormExportPolicy`: valor, «; » y campos.
- **WhatsApp:**
  - compilador con `CheckboxGroup`, presupuesto y huellas r8 intactas;
  - prellenado con r8 y r9;
  - respuestas con arrays de strings, de objetos y mezclados.

**Arquitectura:**
- `ControllerInputContractTests` cubre `[FromForm]` con su caso de control.
- `ServiceDependencyLimitTests`: hasta 8 dependencias. Por eso importación y exportación son dos servicios y `FormCatalogImportPolicy` lee la planilla.
- `ApplicationHelpersTests`: los sufijos `Policy`, `Guard` y `Recorder`.
- `ErrorCodeTests` y `ErrorDeclarationTests`: los códigos nuevos.
- `PersistenceNamingTests`: los métodos nuevos de lectores y repositorios.
- `ModuleBoundaryTests`: `UserEditDialog` no nombra formularios; el núcleo no nombra el módulo.
- `TransactionBoundaryTests`: las claves `forms:person-*` ya entran en el prefijo `forms:` de [`FormsTransactionContribution`](../../tests/ArquitecturaBase.ArchitectureTests/Modules/BusinessForms/FormsTransactionContribution.cs).
- **SQL y filtros de migraciones sin `\r`:** [`MigrationSqlPortabilityTests.cs`](../../tests/ArquitecturaBase.ArchitectureTests/MigrationSqlPortabilityTests.cs) (ya en `main`) revisa el snapshot, los Designers y las operaciones `Up` y `Down`.
  - Aplica a las CHECK nuevas, a los `defaultValueSql`, al índice GIN y a los `migrationBuilder.Sql` de las claves diferidas y de los `Down` que rechazan.
  - Filtros y CHECK en una línea; SQL multilínea con `.ReplaceLineEndings("\n")`.

**Integración con Docker (PostgreSQL y Redis reales):**
- **E1-0:** intercambio de códigos en un guardado; 10.000 opciones (guardar, listar, condiciones de reglas).
- **E1-B:**
  - migración sobre datos existentes;
  - `items[i].value`;
  - el valor sellado sobrevive a editar el catálogo;
  - Excel tipado;
  - una sesión sellada antes del cambio conserva su huella.
- **E1-A:**
  - vista previa de XLSX y CSV con errores por fila y columna, fórmulas y topes;
  - aplicar dos veces da 409;
  - exportar y reimportar da cero diferencias;
  - permisos, rutas y OpenAPI.
- **E1-C:**
  - Up y Down con datos;
  - captura con mínimo y máximo, repetidos, opción no ofrecida e ítem dado de baja;
  - replay;
  - «alguna de» en filtros, facetas y reglas;
  - Excel, PDF, detalle y chat;
  - casillas y archivos en el mismo formulario;
  - una sesión r8 abierta que se captura después de publicar en r9;
  - más de 20 opciones bloqueado.
- **E2:**
  - migraciones con datos;
  - validación por tipo al guardar, pegar e importar, y el formato canónico;
  - cambiar el tipo con valores que no convierten;
  - pasar a obligatorio con faltantes;
  - quitar un campo en uso en el borrador y en la versión activa;
  - un catálogo fuente en uso que no se da de baja;
  - filtros fijos y por persona con el SQL parametrizado;
  - envío bloqueado sin el dato y con campo o dato inactivo;
  - la comprobación con avisos y el máximo por persona;
  - título de más de 30 en el catálogo y en la pregunta;
  - solo viaja lo elegido, revisando el payload a Meta;
  - el sellado después de editar el catálogo;
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

**Contratos del front:**
- los guards estrictos contra los cuerpos exportados, sin completar fixtures;
- las familias nuevas en [`httpCohort.ts`](../../../ArquitecturaBaseFront/src/test/httpCohort.ts) con su variable de entrada y su `provenance.json`.

**Visuales:**
- los tableros en el navegador, en es y en, a 390 y 320 px y en escritorio;
- escenas en `catalogManual`, `formCatalogQuestion`, `responses`, `chatForms` y `personData` (nueva), contra las capturas elegidas;
- el navegable sirve de guía de los recorridos.

**Piloto real con Meta:**
- **E1:**
  - pegar y después importar un catálogo de proveedores de OCHA;
  - un catálogo decimal;
  - mandar un formulario con una pregunta de varias y confirmar en el celular el formato del `nfm_reply` y el caso vacío;
  - verlo en la planilla, el Excel y el PDF.
- **E2:**
  - Proveedores con Zona y CUIT, filtrados para dos personas de zonas distintas;
  - cada una ve lo suyo en el celular;
  - la respuesta muestra el CUIT;
  - mandar a alguien sin zona queda bloqueado con el motivo.

## 14. Riesgos y temas abiertos

### Decisiones confirmadas en E1

Las reglas implementadas se consolidan en [formularios: base de E1](../features/formularios.md#catálogos-base-de-la-etapa-e1--2026-10-21), [tipo de valor](../features/formularios.md#catálogos-con-tipo-de-valor--e1-b-2026-10-22), [importación](../features/formularios.md#carga-de-catálogos-desde-una-planilla--e1-a-2026-10-22), [captura múltiple](../features/formularios.md#elegir-varias-base-y-captura--e1-c1-2026-10-22), [lecturas](../features/formularios.md#elegir-varias-lecturas--e1-c2-2026-10-22), [WhatsApp](../features/formularios.md#elegir-varias-en-whatsapp--e1-c3-2026-10-22) y [front](../features/formularios.md#elegir-varias-en-el-front--e1-c4-2026-10-22). Los resultados y límites de la verificación se conservan en el [cierre local de E1](../features/formularios.md#verificación-local-de-catálogos-e1--2026-10-22), por separado del piloto físico.

- **2 · CheckboxGroup:** la referencia oficial de [componentes de Meta](https://developers.facebook.com/documentation/business-messaging/whatsapp/flows/guides/components#checkboxgroup) confirma `data-source` dinámico, `min-selected-items` y `max-selected-items` enteros, de 1 a 20 opciones, título y label de hasta 30 y descripción de hasta 300. El compilador r9 aplica esos límites. Esto no demuestra el formato del webhook real, que permanece en el piloto.
- **4 · Multipart:** la convención no deduce el 400 de `[FromForm]`; los dos POST de importación lo declaran explícitamente. Preview, Apply y exportación comparten `forms-export`, una operación concurrente por actor y proceso, sin cola. No es un lock global ni un límite entre réplicas.
- **5 · Archivo y memoria:** se aplican 10 MiB de archivo, 64 columnas, 10.000 filas de datos y 100 MiB de ZIP declarados y efectivamente entregados por la biblioteca. El preflight comprueba todas las hojas antes de ClosedXML; los huecos no cuentan como datos. No se afirma un benchmark de memoria de 10.000 filas por 20 campos. ClosedXML carga el libro; si una medición futura exige streaming, se puede cambiar su implementación dentro del mismo puerto.
- **6 · Confirmaciones:** más del 25 % de las opciones activas dadas de baja exige confirmar la cantidad exacta; una planilla sin filas válidas exige confirmar vacío. Las filas con errores se excluyen al aplicar y, si superan el 10 %, requieren además la casilla de confirmación.
- **7 · CSV:** UTF-8 estricto con BOM opcional y detección de coma, punto y coma o tabulación, con comillas y registros multilínea. Un archivo Windows-1252 no se recodifica: se devuelve el error seguro que pide XLSX o CSV UTF-8.
- **8 · Fórmulas:** se usa exclusivamente el valor guardado, sin recalcular ni ejecutar macros; una fórmula sin caché es un error de celda. Rechazar todas las fórmulas no es la regla elegida.
- **10 · Portabilidad:** `MigrationSqlPortabilityTests` inspecciona el SQL efectivo del modelo, snapshot, Designers y operaciones Up/Down. Los filtros son de una línea y el SQL multilínea normaliza sus saltos en el argumento. Sigue aplicando a cada migración de E2.
- **11 · Filas erróneas:** el resumen vigente y CA2 coinciden: se aplican las filas válidas con confirmación, se omiten todas las filas de un código ambiguo y una fila inválida con código existente no provoca su baja. La contradicción inicial del resumen quedó resuelta; `confirmErrors` se conserva.

Los campos opcionales nuevos se omiten con `JsonIgnore(WhenWritingNull)` y las huellas anteriores se conservan. R9 mantiene el prellenado escalar r8/r9 y el recorrido de sesiones antiguas sin reescribirlas. Los Down se protegen con locks antes de DDL y rechazan los hechos que el esquema anterior no representa; los datos compatibles sí permiten bajar. Si hay hechos no representables, volver a ese esquema requiere un respaldo compatible, no borrar ni transformar esos hechos para pasar la guarda.

### Piloto físico pendiente de E1

- **1 · `nfm_reply` de CheckboxGroup:** confirmar en el celular el array de identidades, su orden real y el caso vacío (`[]` o clave ausente). La implementación conserva el orden recibido y distingue vacío explícito de omisión; no afirma que Meta devuelva el orden de selección. El piloto también conserva pegar e importar un catálogo de proveedores ficticio, un catálogo decimal y la lectura de la respuesta en planilla, Excel y PDF, como fija el [piloto pendiente de E1](../plans/verificacion-y-produccion.md#formularios-catálogos-e1).

### E2: decisiones y comprobaciones que siguen pendientes

- **3 · Metadata y paths:** `FieldErrors.On` conserva código y mensajes por campo, pero no copia `Metadata`. E1 devuelve los problemas de la planilla mediante DTOs explícitos; E2 debe resolver cómo entregar el detalle de conversiones, valores faltantes y usos sin perderlo al asociar un path. Si se usa una extensión de ProblemDetails, debe contener sólo el detalle seguro necesario.
- **9 · Personas sin el dato, por lote:** falta el lector de cuentas activas con `formrecords.create` para los avisos de la comprobación. Se incorpora en `IFormPersonDataReader` con SQL fijo sobre roles y claims, sin consultas por persona.
- **12 · Permisos y privacidad:** la tabla de personas exige `formpersondata.manage` y `users.read`; no se elimina el segundo permiso por inferencia. Zona, sucursal y otros datos viven en BusinessForms y sólo viajan a Meta cuando se eligieron para mostrarse. Esta pantalla y su lector no están implementados en E1.
- **Contención y GIN:** medir con 10.000 opciones por 20 campos antes de cerrar E2-1. El planificador puede preferir `(CatalogId, IsActive, Position, Id)` y filtrar después. Una tabla tipada para rangos u orden sigue fuera de alcance, salvo una decisión posterior basada en esa necesidad.
- **Huellas de E2:** `Match`, `Presentation`, `Shown` y los campos sellados nuevos conservan la omisión al ser null y requieren las regresiones previstas para publicaciones y sesiones anteriores. Las pruebas de E1 no acreditan estos contratos aún sin implementar.

### Pendiente aparte

- **13 · Quitar formularios:** sigue pendiente la guía del módulo, registrada en [planes](../plans/README.md). E1 no la da por hecha ni la retira.
