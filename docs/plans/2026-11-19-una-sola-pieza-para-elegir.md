# Una sola pieza para elegir (DS1-A y DS2-A)

Fecha: **2026-11-19**. Estado: **decidido el 2026-11-19; sin programar.** Se programa cuando el usuario lo diga, después
de la prueba integral; hasta entonces el front no se toca. Sólo cambia el front: ni contratos ni servidor.

## Por qué

En la prueba integral, armando cuentas en Usuarios, el usuario abrió «Editar usuario» y vio que Roles y «Campo
asignado» se ven y se manejan distinto: «Los dos son desplegables, ¿por qué los desarrollamos de formas distintas para
algo igual?». No era un caso suelto. El relevamiento del front (91b5556d) encontró seis formas de elegir de una lista,
cada una puesta cuando apareció su caso:

| Pieza hoy | Lugares | Qué tiene de distinto |
| --- | --- | --- |
| Select del navegador (`NativeSelect`) | 60 | La lista la dibuja el sistema: la elegida sin tilde, sin descripción ni buscador. La zona horaria son unas 420 zonas de IANA en crudo, armadas tres veces por separado. |
| Campo que se escribe (`Autocomplete`) | 14 | 8 con flecha (los filtros de la planilla, «Campos de…», «Elegir primero por un campo») y 6 sin flecha, como «Campo asignado», que no tilda la elegida. |
| Menú con tildes (`MultiSelect`) | 2 | Roles y el valor de una condición: descripción en gris, sin buscador. |
| Menú del botón de un filtro | 4 | Rol y Creado en Usuarios, Estado en Catálogos, idioma de la cuenta. |
| Select de la biblioteca (`select.tsx`) | 2 | País del WhatsApp e ícono de una carpeta, con la tilde a la derecha (contra OE1-A). |
| Lista propia de la carga en el celular | 1 | A pantalla completa con buscador, pero la elegida de una sola no lleva tilde. |

El usuario pidió una sola pieza para elegir una o varias opciones, con sus tableros antes del código. Los tableros están
en el lienzo aparte [Desplegables: una sola pieza](https://claude.ai/artifact/87Bkvms9FrDK76HWdFskKE): «DS · Hoy»
(capturas reales), DS1-A a DS1-C y DS2-A a DS2-C. El dónde exacto de cada uso está en el [anexo](#anexo-dónde-se-usa-cada-pieza-hoy).

## Lo que se decidió

- **DS1-A, un botón que abre la lista; el buscador, adentro, cuando son muchas** (descartó DS1-B, el campo que se escribe
  con chips, y DS1-C, elegir en un diálogo). Cerrada, la pieza es un campo con su flecha fina que dice lo elegido, igual
  para una y para varias. Abierta, una lista con la descripción en gris debajo del nombre; la elegida lleva su tilde a la
  izquierda y, en varias, una casilla, y marcar no cierra. Con más de diez opciones, o si vienen del servidor, el buscador
  va arriba, adentro de la lista, con el foco puesto y la cantidad debajo. En la barra de un listado el filtro conserva su
  botón, como «Columnas» y «Más filtros», y abre la misma lista. En el celular la lista ocupa la pantalla, como hoy la
  pregunta de catálogo de la carga.
- **DS2-A, varias elegidas por su nombre, y «y 2 más» si no entran** (descartó DS2-B, chips debajo del campo, y DS2-C,
  «3 de 8»). El campo dice las elegidas separadas por coma, en el orden de la lista; si no entran en su ancho, las que
  entran y «y N más». Nunca crece. El nombre accesible lleva todas.
- **Lo que vale en toda la pieza** (la columna «Igual en las tres opciones» de los tableros):
  - la lista con el borde, la sombra y el renglón de 24 px de los menús, del ancho del campo;
  - «Todas» o «Sin …» primero, con el color del texto y separada por una línea;
  - una opción apagada dice su motivo (MA1-A);
  - del servidor: «Buscando…», «Sin coincidencias», «Mostrar 50 más» y «Reintentar», como la pregunta de catálogo;
  - en una lista del servidor con varias, las elegidas que no están en la página van primero, como en LR3-A;
  - con un tope, la lista dice «Hasta 5» y, al llegar, «Elegiste 5 de 5», y las demás se apagan;
  - lo que llega en mayúsculas del catálogo se muestra como llega;
  - dos a cuatro opciones fijas de un formulario van en radios o un segmentado, como ya pide la lista de chequeo, también
    «Filas por página»; los filtros no entran en esta regla y siguen siendo desplegables (FO1-A);
  - la zona horaria: la ciudad y, en gris, la hora de su país y el desfase («Montevideo · hora de Uruguay · GMT−3»), en
    una sola lista para Mi perfil, Configuración y el horario del negocio.
- **Cambia una decisión vigente.** PL2-A y FO1-A, en un punto: en el combo de un filtro con muchas opciones se escribe en
  el buscador de la lista, no en el campo. El resto de PL2-A queda: «Todas», una por renglón, la tilde y sin cantidades.

## Qué tiene que quedar

Igual a los tableros «DS1-A · Un botón que abre la lista; el buscador, adentro» y «DS2-A · Los nombres y «y 2 más» si no
entran». Sus figuras son el criterio de cierre; al programar, se abre el tablero y se compara elemento por elemento
([revisión del cambio](../guides/validar-un-cambio.md#antes-de-cerrar-lo-que-más-se-repite-en-las-revisiones)):

1. «Editar usuario» cerrado: Roles («Encargado de campo, User») y los datos de la persona («LA ESPERANZA», «Sin zona» en
   gris) con el mismo campo y la misma flecha.
2. Roles abierto: casillas a la izquierda, la descripción en gris debajo y marcar no cierra; ocho roles sin buscador.
3. «Campo asignado» abierto: buscador y «38 opciones» arriba, «Sin campo asignado» primero y separado, la elegida tildada
   y la barra de desplazamiento fina.
4. El filtro de la barra de Usuarios: «Cualquier rol» conserva su botón y abre la lista con «Cualquier rol» primero y
   separado.
5. La zona horaria con buscador, la ciudad y su hora en gris; «Filas por página» en un segmentado, en el pie y en
   Configuración.
6. En el celular, a pantalla completa: buscador si son más de diez, renglones de 56 px y, en varias, «Listo (N)».
7. El teclado y el lector de la figura 7 de DS1-A.
8. Varias: los nombres, y «y N más» cuando no entran («Soja 1ª y 2 más» en el valor de una condición); el lector dice
   todas (DS2-A, figuras 1 a 5).

## Etapas

Cada etapa con TDD donde hay lógica, el build, los tests del área y sus escenas visuales, y un commit por tarea. Las
referencias visuales que cambian se rehacen con `--update-snapshots=all` y se miran una por una. La corrida completa va
una sola vez, al cerrar.

### Etapa 1 · La pieza

1. **`shared/ui/Picker.tsx`**, con su test primero. Recibe las opciones (nombre, descripción, apagada con motivo, línea
   antes, ícono), el valor (uno o varios), `multiple`, el texto de lo vacío («Sin zona») y la opción primera («Todas»,
   «Sin campo asignado»). El buscador aparece con más de diez opciones o cuando el dueño busca en el servidor, y entonces
   la pieza recibe la cantidad, si hay más («Mostrar 50 más»), la carga, el error y su reintento. Dos disparadores: el
   campo de un formulario y el botón de la barra de un listado. Sin el buscador, escribir la inicial va a la primera que
   empieza así. Lo que todavía no existe en ninguna pieza y hay que probar con tests: el resumen «y N más» medido sobre el
   ancho del campo, las elegidas de otra página primero, el tope y el nombre accesible con todas las elegidas.
2. **Accesibilidad y teclado** (figura 7): el disparador es un botón con `aria-haspopup="listbox"` y `aria-expanded`; la
   lista, un `listbox` con `aria-multiselectable` en varias; con buscador, el foco va a él y las flechas mueven la opción
   activa (`aria-activedescendant`); la descripción es la descripción accesible de su opción; «Buscando…», «Sin
   coincidencias» y la cantidad se anuncian como estado. Enter elige (y en varias marca sin cerrar), Escape cierra y
   devuelve el foco al disparador, Tab cierra y sigue.
3. **Celular** (menos de 640 px): la lista a pantalla completa, con el título del campo, la ×, el buscador, la cantidad,
   renglones de 56 px y «Listo (N)» en varias. La elegida de una sola lleva su tilde, que hoy falta.
4. **`shared/lib/timeZones.ts`**: la lista única de zonas (la ciudad del identificador de IANA con espacios; en gris, el
   nombre de la hora con `Intl.DateTimeFormat` y `timeZoneName: "longGeneric"`, y el desfase con `shortOffset`). El valor
   que viaja sigue siendo el identificador de IANA, y el buscador encuentra por ciudad, por país o por el identificador.
5. **Escena del catálogo visual** con los estados de los tableros (una, varias, con buscador, del servidor, vacía, con
   error, apagada y en el celular), en es/en a 1440, 390 y 320 px.

### Etapa 2 · Usuarios, lo que se vio en la prueba

6. Roles (`RolesField`) y los datos de la persona (`PersonDataSection`) pasan a la pieza; el país del WhatsApp
   (`PhoneField`) también, con el disparador angosto («AR +54»).
7. Los filtros Rol y Creado de `UsersFilterBar` pasan a la pieza con el disparador de la barra.
8. «Filas por página» de `Pagination` pasa a un `SegmentedControl` (10, 20, 50, 100); el de Configuración, en la
   etapa 4.

### Etapa 3 · Formularios, catálogos y documentos

9. Los combos (`Autocomplete`) de elegir pasan a la pieza: catálogos, filtros de la planilla, documentos recibidos, la
   persona de ejemplo, «Elegir primero por un campo», las condiciones de una pregunta y el destinatario de prueba del
   asistente. La pregunta de catálogo de la carga (`EntryCatalogField`) también, en la compu y en el celular: se va su
   lista propia (`MobileCatalogField`).
10. Los selects nativos de los editores (pregunta, reglas, condiciones, sugerencias, cálculos, catálogos y documentos)
    pasan a la pieza; los de dos a cuatro opciones fijas, a un segmentado o radios (el tipo de un dato, Catálogo o Texto;
    el valor Sí o No de una condición).
11. Canal y Estado de la planilla pasan a la pieza: en la barra, con el botón; adentro de «Filtros», con el campo.
    «Columnas» pasa a la pieza con varias y el botón de la barra, que sigue diciendo «Columnas».

### Etapa 4 · El resto y retirar lo viejo

12. Configuración, Mi perfil, la IA, el asistente y el ícono de una carpeta. El idioma (dos opciones) va en un
    segmentado, en Configuración y en Mi perfil, y también «Filas por página» de Configuración.
13. Se retiran `MultiSelect`, `NativeSelect` y `select.tsx`. `Autocomplete` queda sólo para buscar un lugar
    (`BusinessPlaceSearch`), que es un buscador con sugerencias y no una lista conocida. Una regla de arquitectura del
    front impide un `<select>` o esas piezas fuera de `shared/ui`.

### Etapa 5 · Documentación y cierre

14. `src/shared/ui/AGENTS.md` (la pieza y qué ya no se usa); el [fundamento visual](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md)
    («Elegir de una lista», con DS1-A y DS2-A; «Listados y filtros», con el cambio de PL2-A; la tabla de «Opción elegida»);
    la pregunta 3 de la lista de chequeo; las premisas de desarrollo del front y el estándar visual. La decisión va a las
    fuentes de las [premisas](../architecture/premisas-de-la-plantilla.md).
15. La corrida completa ([validar un cambio](../guides/validar-un-cambio.md#verificación-y-cierre)) y la prueba guiada con
    el usuario: «Editar usuario», un filtro de la planilla, una pregunta de catálogo en el celular y la zona horaria.
16. Los tableros pasan al lienzo del sistema visual cuando se pueda abrir; lo hecho va al traspaso al multitenant que siga.

## Excepciones

- La vista previa de un formulario (`FormPreview`) imita WhatsApp y conserva su select, como conserva sus radios (OE1-A).
- El idioma del menú de la cuenta y «Contar» de una tarjeta del tablero (`ActionsMenu choice`) son menús de acciones, no
  campos: no cambian.

## Coordinación

[Alta de usuario con el asistente](https://claude.ai/artifact/93Go5haPSAqikPyhtc5RZV) (sin programar) también toca
«Nuevo usuario». El que se programe segundo usa la pieza.

## Anexo: dónde se usa cada pieza hoy

Front en 91b5556d, rutas desde `src/`.

**`NativeSelect` (60).** `features/artificial-intelligence/components/BusinessFaqEditor.tsx:24`;
`BusinessHoursEditor.tsx:43, 59, 70, 72`; `BusinessLocationEditor.tsx:19`; `features/artificial-intelligence/pages/AiSettingsPage.tsx:82, 117`;
en `features/business-forms/components/`: `CalculationSettings.tsx:46`, `CatalogFieldsDialog.tsx:239`,
`CatalogFiltersEditor.tsx:48, 50, 51, 57, 61, 62, 75, 76`, `CatalogGroupBy.tsx:31`, `CatalogPresentationSection.tsx:43`,
`CatalogQuestionFilters.tsx:139, 141`, `CatalogSortDialog.tsx:40`, `ConditionSettings.tsx:116`,
`FormConditionsEditor.tsx:72, 83, 88`, `FormDocumentReadingZone.tsx:238`, `FormEntryForm.tsx:326`,
`FormLocationContactZone.tsx:51, 53`, `FormPreview.tsx:81` (dos), `96, 97`, `FormRulesSection.tsx:197, 209, 214`,
`FormStandardAddDialog.tsx:107`, `FormSuggestionsSection.tsx:156, 160` (dos), `PersonAttributeDialog.tsx:109`,
`QuestionEditor.tsx:161`, `ResponseFilterBar.tsx:44, 51`, `ResponseFiltersMenu.tsx:111, 118`,
`documents/DocumentFormQuestions.tsx:127`, `documents/ReceivedDocumentData.tsx:105`, `documents/ReceivedDocumentPanel.tsx:152`;
`features/profile/pages/ProfilePage.tsx:338, 362`; `features/settings/components/SettingsForm.tsx:27, 32, 44`;
`features/whatsapp/components/AssistantCaseEditor.tsx:88, 131, 177`; `shared/ui/Pagination.tsx:29`.

**`Autocomplete` (14).** Con flecha: en `features/business-forms/components/`, `CatalogFieldSourceInput.tsx:32`,
`CatalogQuestionFilters.tsx:76`, `ConditionSettings.tsx:79` (varias), `EntryCatalogField.tsx:71`,
`ResponseFacetControl.tsx:72`, `documents/ReceivedDocumentFacetControl.tsx:34`, `documents/ReceivedDocumentOptionSearch.tsx:55`;
y `features/whatsapp/components/AssistantTestPanel.tsx:78`. Sin flecha: `features/artificial-intelligence/components/BusinessPlaceSearch.tsx:55`;
en `features/business-forms/components/`, `CatalogExtraFieldInput.tsx:44`, `CatalogQuestionSettings.tsx:146`,
`EntryCatalogField.tsx:105` (una o varias), `PersonDataSection.tsx:27`; y `features/business-forms/pages/CatalogFiltersEditorPage.tsx:136`.

**`MultiSelect` (3).** `features/users/components/RolesField.tsx:25, 47`; `features/business-forms/components/FormConditionsEditor.tsx:89`.

**`select.tsx` (2).** `shared/ui/PhoneField.tsx:64` (país); `features/business-forms/components/FolderMenuDialog.tsx:48` (ícono).

**Menú del botón con radios (5).** `features/users/components/UsersFilterBar.tsx:109` (Rol) y `141` (Creado);
`features/business-forms/pages/CatalogsPage.tsx:73` (Estado); `layouts/components/UserMenu.tsx:106` (idioma, queda);
`shared/ui/ActionsMenu.tsx:89` (`choice`, queda).

**Menú con tildes, «Columnas».** `features/business-forms/components/ResponseColumnsMenu.tsx:46` y
`features/business-forms/components/CatalogManualItemsEditor.tsx:69`.

**Lista propia del celular.** `features/business-forms/components/EntryCatalogField.tsx` (`MobileCatalogField`).

**Zona horaria, tres listas.** `features/profile/pages/ProfilePage.tsx:44`, `features/settings/components/SettingsForm.tsx:20`
y `features/artificial-intelligence/components/BusinessHoursEditor.tsx:25`.
