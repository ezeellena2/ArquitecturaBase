# Carpetas para formularios y catálogos

Decidido el 2026-11-03, sin programar. Sirve para ordenar «Formularios», «Diseñar formularios» y «Catálogos» en carpetas
con subcarpetas, antes de que lleguen la biblioteca de estándares y lo de ARCA. Lo pidió el usuario al decidir la etapa 1
de ARCA, y la [hoja de ruta de ARCA](2026-10-31-arca-formularios-estandar.md) lo ubica en la etapa 2. Pertenece al módulo
de formularios (BusinessForms), no a ARCA. Las reglas de hoy están en [formularios](../features/formularios.md).

**Tableros:** CP1 a CP4 en el lienzo provisorio [Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M),
en las filas «Carpetas», y CP5 a su derecha. Las elegidas llevan la banda verde y el sello «elegida». Se pasan al lienzo
[Sistema visual](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ) antes de programar, cuando la cuenta lo abra.

## Decisiones (2026-11-01 y 2026-11-03)

1. **Carpetas con subcarpetas, y un mismo elemento en varias** (2026-11-01). Un catálogo se usa en formularios de
   categorías distintas, así que tiene que poder estar en todas. El usuario lo pidió «lo más completo posible».
2. **Un solo árbol para formularios y catálogos.** Las carpetas son por tema: «Facturación» tiene la factura y sus
   catálogos. El mismo árbol aparece en los tres listados, y cada uno muestra lo suyo.
3. **Las carpetas solo ordenan.** Quién ve, carga o diseña cada formulario sigue saliendo de los permisos de hoy. Una
   carpeta no da ni quita acceso.
4. **El árbol va en un panel a la izquierda de cada listado (CP1-A).** Se descartaron entrar a las carpetas como en una
   unidad de archivos (CP1-B) y un filtro con una columna (CP1-C).
5. **«Carpetas…» abre un diálogo con el árbol y casillas (CP2-A).** Está en el menú de la fila y en el encabezado del
   formulario o del catálogo. Se descartaron arrastrar y las etiquetas en el encabezado (CP2-B).
6. **Las carpetas se administran en el mismo panel (CP3-A).** Se descartó una pantalla «Carpetas» aparte (CP3-B).

**Resueltas por defecto**, sin objeción del usuario:

- Hasta cinco niveles de carpetas.
- Arriba del árbol van «Todos» y «Sin carpeta». Un elemento sin carpetas está en «Sin carpeta».
- Elegir una carpeta muestra lo que está en ella y en sus subcarpetas.
- Sin cantidades junto a las carpetas, como los filtros desde FO1-A.
- Las carpetas se ordenan alfabéticamente.
- Dos carpetas no pueden llamarse igual dentro de la misma carpeta, sin distinguir mayúsculas. En carpetas distintas, sí.
- La carpeta elegida va en la dirección (`?carpeta=`), así se puede compartir el enlace.

## Por decidir: las carpetas en el menú lateral (CP5)

El 2026-11-04 el usuario pidió mostrar las carpetas como menús del menú lateral: una adentro de Administración, o un menú
nuevo, como «Repartos», con sus formularios. Los tableros CP5-A, CP5-B y CP5-C están en el lienzo y falta que elija uno:

| Opción | Dónde aparece y quién lo decide | Niveles y catálogos | Panel de los listados (CP1-A) |
| --- | --- | --- | --- |
| **CP5-A, recomendada** | Quien administra carpetas elige, en el «…» de cada una, «En el menú…»: fuera del menú (como vienen todas), un menú propio con ícono, o adentro de «Gestión de formularios» o de «Administración» | Dos: las subcarpetas son grupos y lo de más abajo se suma a su grupo. Sin catálogos | Se suma |
| CP5-B | Una pantalla «Menú» en Administración › Configuración (`settings.manage`) arma los menús propios y pone carpetas o formularios sueltos en cualquier menú, en el orden que se quiera | Dos. Sin catálogos | Se suma |
| CP5-C | Cada carpeta del primer nivel es un menú, sola, con un ícono que se elige al crearla; su panel tiene el árbol y ahí se administra | Tres, en lugar de cinco. Con catálogos, para quien los administra | Lo reemplaza: los listados filtran con un desplegable «Carpeta» |

Las tres coinciden en lo demás. Un formulario del menú abre su planilla, con «Cargar», y a quien solo carga lo lleva a la
carga. Solo aparecen los formularios activos, y un menú sin nada para esa persona no aparece. En el celular, cada menú
propio es un grupo del cajón; con el menú contraído, su ícono abre el panel.

## Recorrido

### Los listados (CP1-A)

- **Dónde está el árbol:** a la izquierda de Formularios (`/formularios/respuestas`), Diseñar formularios (`/formularios`) y
  Catálogos (`/catalogos`). «Formularios que faltan» no lo lleva.
- **Qué muestra la tabla:** al elegir una carpeta, solo lo que está en ella y en sus subcarpetas. La búsqueda y los demás
  filtros se aplican dentro de la carpeta. Lo que está en dos carpetas aparece en las dos, y una sola vez en «Todos».
- **Para quién:** quien solo carga o aprueba ve el mismo árbol, sin el + ni los «…». Una carpeta en la que esa persona no ve
  nada no le aparece.
- **En el celular y en el panel de carga:** el árbol pasa a un selector arriba de la lista, que abre las carpetas en una
  hoja.

### Poner un elemento en carpetas (CP2-A)

- **Dónde está:** «Carpetas…» en el menú de la fila (Diseñar formularios y Catálogos) y en el encabezado del formulario o
  del catálogo.
- **Qué hace:** abre «Carpetas de «Artículos»» con el árbol y una casilla por carpeta. Se marcan todas las que
  correspondan; destildar todas lo deja en «Sin carpeta».
- **Crear sobre la marcha:** «Agregar carpeta» crea una ahí mismo.
- **Cuándo guarda:** al tocar «Guardar».

### Administrar (CP3-A)

- **El +** de la banda «Carpetas» agrega una carpeta en el primer nivel.
- **El «…» de cada carpeta** aparece al pasar el mouse o con el foco: agregar subcarpeta, renombrar en el lugar, mover a…
  y borrar.
- **Mover a…** abre el árbol con un botón de radio por carpeta y «Arriba de todo». La carpeta misma y sus subcarpetas no se
  pueden elegir.
- **Borrar** pide confirmación y cuenta lo que se pierde: «También se borran sus 2 subcarpetas. Los 9 formularios y
  catálogos que tenían quedan en sus otras carpetas o en «Sin carpeta»». Nunca se borra un formulario ni un catálogo.

### Estados (CP4)

| Estado | Qué se ve |
| --- | --- |
| Todavía no hay carpetas | Solo «Todos» y «Sin carpeta», con «Agregar carpeta» para quien administra |
| Carpeta vacía | «No hay catálogos en «Siembra y cosecha»» (o formularios, según el listado) |
| Búsqueda sin coincidencias | «No hay coincidencias en «Facturación»», con «Buscar en todas», que conserva lo escrito |
| Sin permiso para administrar | El árbol sin el + ni los «…», y sin «Carpetas…» en la fila |
| Una carpeta sin nada para esa persona | No aparece |
| Nombre repetido | El error junto al nombre que se está editando |
| Borrar | La confirmación con subcarpetas y elementos |
| Lo que llega de ARCA o de la biblioteca | Una carpeta que se crea sola y es como cualquier otra |
| No se pudieron leer las carpetas | El error en el panel con «Reintentar». El listado sigue andando con «Todos» |
| Una carpeta que ya no existe | Si la carpeta de la dirección se borró, vuelve a «Todos» con un aviso |
| Quinto nivel | «Agregar subcarpeta» se apaga y su tooltip dice por qué. Mover tampoco deja pasar de cinco |

## Lo que llega del sistema

- **ARCA:** los catálogos de ARCA (etapa 3) llegan a una carpeta «ARCA» que se crea sola. Se puede renombrar, mover o
  borrar como cualquier otra. La carpeta recuerda que la creó el sistema, así un catálogo nuevo de ARCA vuelve a ella
  aunque la hayan renombrado; si la borraron, se crea de nuevo.
- **Biblioteca:** al agregar un estándar se elige su carpeta; por defecto, la que trae el estándar.

## Backend

### Permisos

No hay permisos nuevos:
- **El árbol** lo lee cualquiera que vea alguno de los tres listados. Quien administra carpetas ve todas, también las
  vacías; los demás ven solo las que tienen algo visible para ellos en ese listado.
- **Crear, renombrar, mover y borrar carpetas** exige `formtemplates.manage` o `formcatalogs.manage`.
- **Poner un formulario en carpetas** exige `formtemplates.manage`; un catálogo, `formcatalogs.manage`.

### Datos

| Tabla | Qué guarda |
| --- | --- |
| `FormFolders` | Carpeta madre (nula en el primer nivel), nombre, nombre normalizado, la clave del sistema si la creó ARCA o la biblioteca, y la revisión. Es `IAuditable`. Índice único por madre y nombre normalizado |
| `FormFolderMemberships` | Carpeta, tipo de elemento (formulario o catálogo) y su Id. Índice único por los tres; se borra con la carpeta |

La profundidad y la ausencia de ciclos se validan al crear y al mover. Borrar un formulario o un catálogo de forma lógica
oculta sus membresías, sin borrarlas.

### Lecturas y escrituras

- **Los listados** (`api/forms/overview`, el de diseños y el de catálogos) reciben una carpeta opcional o «sin carpeta».
  Filtran en SQL, con una consulta recursiva de las subcarpetas, antes de contar, ordenar y paginar. Un elemento en dos
  subcarpetas de la elegida sale una sola vez. Las facetas de cada listado se calculan dentro de la carpeta.
- **El árbol:** una lectura por listado devuelve las carpetas que esa persona ve en él, ya podadas.
- **Guardar:** cada operación sobre carpetas es una transacción con un advisory lock de carpetas, así dos movimientos
  simultáneos no arman un ciclo ni dos nombres iguales. Renombrar, mover y borrar llevan la revisión de la carpeta y
  responden 409 si cambió. El diálogo de un elemento guarda el conjunto entero junto con el que leyó; si otro lo cambió en
  el medio, 409.
- **Rutas:** son MVC, por ejemplo `api/forms/folders` (leer el árbol de un listado, crear, renombrar o mover y borrar),
  `api/forms/definitions/{id}/folders` y `api/forms/catalogs/{id}/folders`. Se fijan en el plan de implementación y entran
  en `ExplicitRouteInventoryTests`.
- **Errores:** en `FormFolderErrors`, traducidos en `Errors.resx` y `Errors.en.resx`: nombre vacío o largo, nombre repetido
  en el nivel, más de cinco niveles, ciclo, carpeta inexistente, revisión desactualizada y conjunto cambiado.
- **Fuera del alcance:** las carpetas no viajan a WhatsApp, al asistente ni a «Formularios que faltan».

## Pruebas

- **Unitarias:** nombre y normalización, profundidad, ciclo al mover y el conjunto de un elemento.
- **De integración, con PostgreSQL:**
  - el árbol podado por permisos en cada listado;
  - el filtro con subcarpetas antes de paginar, sin filas repetidas;
  - «Sin carpeta» y borrar con subcarpetas sin tocar elementos;
  - dos movimientos simultáneos que formarían un ciclo;
  - los 409 de revisión y de conjunto cambiado;
  - la carpeta del sistema que vuelve aunque la hayan renombrado.
- **Front:** el panel en los tres listados, el selector del celular, el diálogo, el menú de cada carpeta, los estados de CP4
  y la carpeta en la dirección, con sus capturas visuales.

## Cortes sugeridos

1. **Backend:** modelo, rutas, filtro en los tres listados y pruebas.
2. **Ver y filtrar:** el panel en los tres listados, la carpeta en la dirección y el selector del celular.
3. **Ordenar:** el diálogo «Carpetas…», el + y el «…» de cada carpeta, mover y borrar.

## Relación con otros planes

- **[ARCA](2026-10-31-arca-formularios-estandar.md):** es parte de la etapa 2 y la carpeta «ARCA» llega con la etapa 3.
- **[Documentos por WhatsApp](2026-10-22-documentos-por-whatsapp.md):** la biblioteca de estándares usa la carpeta que trae
  cada estándar.
- **Multitenant:** las carpetas son por organización. Al cerrar, se ofrece el traspaso.
