# Carpetas para formularios y catálogos

Decidido el 2026-11-03 y el 2026-11-04; backend y front programados del 2026-11-09 al 2026-11-11, con la etapa 2 de ARCA
([regla](../features/formularios.md#carpetas--cp1-a-a-cp5-a-2026-11-09),
[diseño](../../../ArquitecturaBaseFront/docs/design/carpetas.md)); faltan la prueba integral y el cierre. Revisado contra el
código el 2026-11-18: rutas, permisos, tope de 500 y menú coinciden con la regla. Los ejemplos HTTP (`forms-http/folders`)
entraron con la exportación del 2026-11-17 y el front todavía no tiene un test que los lea. La prueba a mano no tiene
pasos escritos: abajo, en «Pruebas», va el recorrido mínimo. Sirve para ordenar «Formularios», «Diseñar formularios» y
«Catálogos» en carpetas con subcarpetas, y para llevar al menú lateral las que se elijan, antes de que lleguen la
biblioteca de estándares y lo de ARCA. Lo pidió el usuario al decidir la etapa 1 de ARCA, y la
[hoja de ruta de ARCA](2026-10-31-arca-formularios-estandar.md) lo ubica en la etapa 2. Pertenece al módulo de formularios
(BusinessForms), no a ARCA. Las reglas de hoy están en [formularios](../features/formularios.md).

**Rediseño del 2026-11-20, aprobado y sin programar:** las carpetas pasan a ser menús del menú lateral
([CM1 a CM6](#las-carpetas-son-menús-cm1-a-cm6-2026-11-20)). Reemplaza el panel de los listados, la administración en el
panel y «En el menú…» (decisiones 4, 6 y 7) y el límite de cinco niveles. Hasta que se programe, el código sigue lo que
describen las secciones de CP1-A a CP5-A.

**Tableros:** CP1 a CP4 en el lienzo provisorio [Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M),
en las filas «Carpetas», y CP5 a su derecha. Las elegidas llevan la banda verde y el sello «elegida». El lienzo
[Sistema visual](https://claude.ai/artifact/HPbmDPLnr8JZ9TxevtTqJJ) no se abre desde la cuenta de estas sesiones. Por eso,
el 2026-11-05 el usuario decidió hacer lo mismo que con renglones y la IA: antes de programar, el diseño elegido se registra
en el front (`docs/design`). Pasar los tableros a ese lienzo queda para cuando se pueda abrir.

## Decisiones (2026-11-01, 2026-11-03 y 2026-11-04)

1. **Carpetas con subcarpetas, y un mismo elemento en varias** (2026-11-01). Un catálogo se usa en formularios de
   categorías distintas, así que tiene que poder estar en todas. El usuario lo pidió «lo más completo posible».
2. **Un solo árbol para formularios y catálogos.** Las carpetas son por tema: «Facturación» tiene la factura y sus
   catálogos. El mismo árbol aparece en los tres listados, y cada uno muestra lo suyo.
3. **Las carpetas solo ordenan.** Quién ve, carga o diseña cada formulario sigue saliendo de los permisos de hoy. Una
   carpeta no da ni quita acceso.
4. **El árbol va en un panel a la izquierda de cada listado (CP1-A).** Se descartaron entrar a las carpetas como en una
   unidad de archivos (CP1-B) y un filtro con una columna (CP1-C). *Reemplazada el 2026-11-20 por las decisiones 8 y 12.*
5. **«Carpetas…» abre un diálogo con el árbol y casillas (CP2-A).** Está en el menú de la fila y en el encabezado del
   formulario o del catálogo. Se descartaron arrastrar y las etiquetas en el encabezado (CP2-B).
6. **Las carpetas se administran en el mismo panel (CP3-A).** Se descartó una pantalla «Carpetas» aparte (CP3-B).
   *Reemplazada el 2026-11-20 por la decisión 9.*
7. **Una carpeta puede ir al menú lateral, y lo elige cada una (CP5-A, 2026-11-04).** El usuario lo pidió para tener a mano
   los formularios de una categoría, en un menú propio, como «Repartos», o adentro de un panel que ya existe, como
   Administración. Se descartaron una pantalla «Menú» en Configuración (CP5-B) y que cada carpeta del primer nivel sea un
   menú con el árbol adentro (CP5-C). **CP1-A no cambia:** el árbol sigue a la izquierda de los tres listados, con todos sus
   niveles, y el menú se suma como atajo. Tampoco cambia el límite de cinco niveles. CP3-A suma «En el menú…» al «…» de
   cada carpeta. *Reemplazada el 2026-11-20 por la decisión 8: toda carpeta del primer nivel es un menú.*

**Resueltas por defecto**, sin objeción del usuario:

- Hasta cinco niveles de carpetas. *Reemplazada el 2026-11-20 por la decisión 11: dos niveles.*
- Arriba del árbol van «Todos» y «Sin carpeta». Un elemento sin carpetas está en «Sin carpeta».
- Elegir una carpeta muestra lo que está en ella y en sus subcarpetas.
- Sin cantidades junto a las carpetas, como los filtros desde FO1-A.
- Las carpetas se ordenan alfabéticamente.
- Dos carpetas no pueden llamarse igual dentro de la misma carpeta, sin distinguir mayúsculas. En carpetas distintas, sí.
- La carpeta elegida va en la dirección (`?carpeta=`), así se puede compartir el enlace.

## Recorrido

### Los listados (CP1-A)

*Reemplazado el 2026-11-20: los listados quedan sin panel, y Diseñar formularios y Catálogos suman un filtro «Carpeta»
([CM3](#las-carpetas-son-menús-cm1-a-cm6-2026-11-20)).*

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

*Reemplazado el 2026-11-20: las carpetas se administran desde el menú lateral
([CM2 y CM4](#las-carpetas-son-menús-cm1-a-cm6-2026-11-20)).*

- **El +** de la banda «Carpetas» agrega una carpeta en el primer nivel.
- **El «…» de cada carpeta** aparece al pasar el mouse o con el foco: agregar subcarpeta, renombrar en el lugar, mover a…
  y borrar.
- **Mover a…** abre el árbol con un botón de radio por carpeta y «Arriba de todo». La carpeta misma y sus subcarpetas no se
  pueden elegir.
- **Borrar** pide confirmación y cuenta lo que se pierde: «También se borran sus 2 subcarpetas. Los 9 formularios y
  catálogos que tenían quedan en sus otras carpetas o en «Sin carpeta»». Nunca se borra un formulario ni un catálogo.

### En el menú lateral (CP5-A)

*Reemplazado el 2026-11-20: toda carpeta del primer nivel es un menú, sin nada que elegir
([CM1](#las-carpetas-son-menús-cm1-a-cm6-2026-11-20)).*

- **Dónde se elige:** «En el menú…», en el «…» de cada carpeta, entre «Mover a…» y «Borrar», para quien administra
  carpetas. Abre «Repartos» en el menú con cuatro opciones: fuera del menú, un menú propio, adentro de «Gestión de
  formularios» o adentro de «Administración». Un menú propio elige además su ícono entre doce genéricos, con la carpeta
  por defecto; su (i) dice que es lo que se ve con el menú contraído. Se guarda al tocar «Guardar».
- **Por defecto:** toda carpeta arranca fuera del menú, también la de ARCA. Puede ir al menú una carpeta de cualquier
  nivel, y el menú lleva su nombre: renombrar la carpeta lo renombra.
- **Un menú propio:** va debajo de Inicio, con los demás menús propios en orden alfabético, y abre su panel como
  Administración. Adentro, un grupo por subcarpeta y, debajo, sus formularios. Lo que está más abajo se suma a su grupo,
  así que el menú nunca pasa de dos niveles.
- **Adentro de un panel:** la carpeta es un grupo más, después de lo que el panel ya tiene (en Administración, después de
  «Configuración»; en «Gestión de formularios», después de «Formularios que faltan»), con todos sus formularios, también
  los de sus subcarpetas. Varias carpetas en el mismo panel van en orden alfabético.
- **Qué aparece:** solo formularios activos, en orden alfabético; ni borradores, ni dados de baja, ni catálogos. Un
  formulario que está en dos carpetas del menú aparece en las dos.
- **Qué abre:** a quien ve o aprueba respuestas, la planilla del formulario (`/formularios/{id}/respuestas`), con «Cargar»
  en el encabezado; a quien solo carga, la carga (`/formularios/{id}/cargar`). Si se entró desde el menú, su panel queda
  abierto con el formulario marcado y las migas lo nombran («Repartos / Hoja de ruta»). Desde un enlace o desde
  «Formularios», todo sigue como hoy.
- **Para quién:** cada uno ve los formularios que puede abrir. Un menú o un grupo sin nada para esa persona no aparece, y
  un panel se ve si se ve algo adentro: si lo único que alguien ve en Administración es «Facturación», Administración le
  aparece con ese grupo solo.
- **En el celular:** cada menú propio es un grupo del cajón, con sus subcarpetas como grupos adentro. Una carpeta puesta
  en un panel queda adentro del grupo de ese panel.
- **Con el menú contraído:** el ícono del menú propio, con su tooltip, abre el panel entero, como Administración.
- **Estados:** mientras se lee el menú, los menús propios no ocupan lugar y aparecen al leerse (programado así el
  2026-11-11: un bloque que desapareciera sin menús propios haría subir al resto, contra «Lo que carga no mueve lo que ya
  se ve» del 2026-11-05). Si no se pudo leer, el
  menú lateral muestra el aviso que ya tiene, con «Reintentar», y lo demás sigue andando. Borrar una carpeta la saca del
  menú; moverla no cambia su lugar en el menú. El cambio se ve enseguida para quien lo hizo, y los demás lo ven la
  próxima vez que se lee el menú.

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
  aunque la hayan renombrado; si la borraron, se crea de nuevo. Llega fuera del menú, como toda carpeta nueva.
- **Biblioteca:** al agregar un estándar se elige su carpeta; por defecto, la que trae el estándar.

## Backend

### Permisos

No hay permisos nuevos:
- **El árbol** lo lee cualquiera que vea alguno de los tres listados. Quien administra carpetas ve todas, también las
  vacías; los demás ven solo las que tienen algo visible para ellos en ese listado.
- **Crear, renombrar, mover y borrar carpetas**, y elegir su lugar en el menú, exige `formtemplates.manage` o
  `formcatalogs.manage`.
- **Poner un formulario en carpetas** exige `formtemplates.manage`; un catálogo, `formcatalogs.manage`.
- **El menú** lo lee cualquier cuenta activa; a quien no puede abrir formularios le llega vacío.

### Datos

| Tabla | Qué guarda |
| --- | --- |
| `FormFolders` | Carpeta madre (nula en el primer nivel), nombre, nombre normalizado, la clave del sistema si la creó ARCA o la biblioteca, su lugar en el menú (ninguno, propio, «Gestión de formularios» o «Administración»), el ícono de un menú propio, de una lista fija, y la revisión. Es `IAuditable`. Índice único por madre y nombre normalizado |
| `FormFolderMemberships` | Carpeta, tipo de elemento (formulario o catálogo) y su Id. Índice único por los tres; se borra con la carpeta |

La profundidad y la ausencia de ciclos se validan al crear y al mover. Borrar un formulario o un catálogo de forma lógica
oculta sus membresías, sin borrarlas.

### Lecturas y escrituras

- **Los listados** (`api/forms/overview`, el de diseños y el de catálogos) reciben una carpeta opcional o «sin carpeta».
  Filtran en SQL, con una consulta recursiva de las subcarpetas, antes de contar, ordenar y paginar. Un elemento en dos
  subcarpetas de la elegida sale una sola vez. Las facetas de cada listado se calculan dentro de la carpeta.
- **El árbol:** una lectura por listado devuelve las carpetas que esa persona ve en él, ya podadas.
- **El menú:** una lectura devuelve, para esa persona, los menús propios y los grupos de cada panel, con sus formularios
  activos ya podados y en el orden en que se muestran.
- **Guardar:** cada operación sobre carpetas es una transacción con un advisory lock de carpetas, así dos movimientos
  simultáneos no arman un ciclo ni dos nombres iguales. Renombrar, mover, borrar y cambiar su lugar en el menú llevan la
  revisión de la carpeta y responden 409 si cambió. El diálogo de un elemento guarda el conjunto entero junto con el que
  leyó; si otro lo cambió en el medio, 409.
- **Rutas:** son MVC, por ejemplo `api/forms/folders` (leer el árbol de un listado, crear, renombrar o mover y borrar),
  `api/forms/folders/{id}/menu` (su lugar en el menú), `api/forms/menu` (leer el menú),
  `api/forms/definitions/{id}/folders` y `api/forms/catalogs/{id}/folders`. Se fijan en el plan de implementación y entran
  en `ExplicitRouteInventoryTests`.
- **Errores:** en `FormFolderErrors`, traducidos en `Errors.resx` y `Errors.en.resx`: nombre vacío o largo, nombre repetido
  en el nivel, más de cinco niveles, ciclo, carpeta inexistente, ícono fuera de la lista o sin menú propio, revisión
  desactualizada y conjunto cambiado.
- **Fuera del alcance:** las carpetas no viajan a WhatsApp, al asistente ni a «Formularios que faltan».

## Pruebas

- **Unitarias:** nombre y normalización, profundidad, ciclo al mover, el conjunto de un elemento, y el lugar en el menú
  con su ícono.
- **De integración, con PostgreSQL:**
  - el árbol podado por permisos en cada listado;
  - el filtro con subcarpetas antes de paginar, sin filas repetidas;
  - «Sin carpeta» y borrar con subcarpetas sin tocar elementos;
  - dos movimientos simultáneos que formarían un ciclo;
  - los 409 de revisión y de conjunto cambiado;
  - la carpeta del sistema que vuelve aunque la hayan renombrado;
  - el menú podado por permisos, sin borradores ni dados de baja, con las subcarpetas en su grupo y un formulario que está
    en dos carpetas.
- **Front:** el panel en los tres listados, el selector del celular, el diálogo, el menú de cada carpeta, los estados de CP4
  y la carpeta en la dirección; los menús propios y los grupos en la barra, el cajón y la barra contraída, y el diálogo
  «En el menú…». Todo con sus capturas visuales. Al programar, la regla del menú se suma a la sección «Navegación» de
  `visual-baseline.md` (hecho).
- **A mano, en la prueba integral** (agregado el 2026-11-18, sale de CP4 y CP5-A): crear una carpeta y una subcarpeta,
  poner un formulario y un catálogo en dos carpetas y verlos una vez en «Todos»; mover una carpeta y probar que no deja
  pasar del quinto nivel; borrar una con subcarpetas y comprobar que los elementos quedan en sus otras carpetas o en «Sin
  carpeta»; poner una carpeta en un menú propio y otra en «Administración»; entrar como alguien que sólo carga y ver sólo
  las carpetas y los formularios que puede abrir, y que el menú le abre la carga.

## Cortes

Los cuatro están hechos: backend 724bcc25, e62d80ae y dde47722; front aa10d91d, 9d34e95d, 56eeec07 y 24d9e50a.

1. **Backend:** modelo, rutas, filtro en los tres listados y pruebas.
2. **Ver y filtrar:** el panel en los tres listados, la carpeta en la dirección y el selector del celular.
3. **Ordenar:** el diálogo «Carpetas…», el + y el «…» de cada carpeta, mover y borrar.
4. **Menú:** el lugar de cada carpeta en el menú y su lectura, el diálogo «En el menú…», y los menús propios y los grupos
   en la barra, el cajón y la barra contraída.

## Las carpetas son menús (CM1 a CM6, 2026-11-20)

Lo pidió el usuario el 2026-11-20, en la prueba integral. Había creado «Repartos» y quedó en el panel de «Diseñar
formularios», no en el menú: «Si creo carpetas, vendrían a ser menús del menú lateral, donde ahí dentro voy colocando los
formularios para poder seleccionarlos directamente en el menú y no dentro de la lista completa de formularios. Si no está en
ningún lugar, sigue sólo en la lista de formularios». Del panel de carpetas dijo que es horrible. Es lo que el 2026-11-04 se
había descartado como CP5-C. Aprobó los tableros el mismo día («Sí, aprobado»). Se programa cuando él lo diga: desde el
2026-11-15 no entra nada nuevo antes de la prueba integral, salvo pedido suyo.

**Tablero:** lienzo [Carpetas en el menú lateral](https://claude.ai/artifact/Ae5jKLH9BkQbgG2X7P8GLe). Main tiene el pedido,
la comparación con lo de hoy y lo resuelto por defecto. Después siguen el menú (CM1), crear y poner formularios (CM2), los
listados y «Carpetas…» (CM3), ordenar (CM4), quien sólo carga, el celular y el menú contraído (CM5) y los estados (CM6). Lo
revisó un agente contra las capturas reales y la lista de chequeo, y sus arreglos están en los tableros. El diseño está
registrado en [`docs/design/carpetas.md`](../../../ArquitecturaBaseFront/docs/design/carpetas.md) del front.

### Decisiones (2026-11-20)

8. **Cada carpeta del primer nivel es un menú del menú lateral.** Abre su panel como Administración: sus formularios y un
   grupo por subcarpeta. Crear una carpeta es crear un menú. Reemplaza las decisiones 4 (en los listados de formularios), 6
   y 7.
9. **Se administran en el menú**, con «Nueva carpeta» y un ⋮ arriba del panel de cada carpeta y en cada grupo. Las crea,
   edita, mueve y borra quien tiene «Administrar formularios» (`formtemplates.manage`). Quien sólo administra catálogos
   (`formcatalogs.manage`) pone catálogos en carpetas que ya existen.
10. **Un formulario entra a una carpeta desde la carpeta o desde el formulario.** «Agregar formularios» abre una casilla por
    formulario, y «Carpetas…» sigue como en CP2-A. Un formulario puede estar en varias carpetas (decisión 1).
11. **Dos niveles.** Una carpeta del menú puede tener subcarpetas, que son grupos de su panel; una subcarpeta no. Reemplaza
    el límite de cinco: un menú tiene dos niveles, como ya mostraba el de CP5-A.
12. **Catálogos: un filtro «Carpeta».** Lo eligió el usuario entre quitarle las carpetas a Catálogos (la recomendada), el
    filtro y dejar el panel. Catálogos pierde el panel y suma «Carpeta» al lado de Estado. Un catálogo se sigue poniendo en
    carpetas con «Carpetas…» y no aparece en el menú. Sigue la decisión 2: un solo árbol.
13. **El menú se relee solo.** Lo pidió la prueba integral (paso 65, «ARCA 2 premisas 1»), que no lo pudo probar porque
    ninguna carpeta estaba en el menú. Al publicar, dar de baja, reactivar o eliminar un formulario, el menú se relee sin
    recargar: con el panel abierto, el formulario aparece o desaparece solo.

**Resueltas por defecto**, aprobadas con los tableros:

- Las carpetas van después de WhatsApp y antes de Administración. Así, cuando llegan, no corren lo que ya se ve (pregunta 9
  de la lista de chequeo). Con muchas, el menú se desplaza.
- Formularios (`/formularios/respuestas`) pierde el panel y el desplegable «Carpeta: Todos», y no suma filtro: sus carpetas
  son el menú. Diseñar formularios suma el mismo filtro que Catálogos, porque el menú abre la planilla o la carga, no el
  diseño.
- En el menú van sólo los formularios activos, como en CP5-A. «Agregar formularios» lista también los borradores, con la
  etiqueta «Borrador».
- Quien administra formularios ve en el menú todas las carpetas, también las vacías o con sólo catálogos, aunque no vea
  respuestas. A esa persona, un formulario le abre su diseño.
- El ícono de una carpeta nueva arranca en el primero de los doce que ninguna carpeta usa.
- Los nombres, las revisiones, los 409, el borrar y lo que llega del sistema siguen como hoy, con los textos de dos niveles.

### Recorrido

Cada punto queda igual a su tablero.

- **El menú (CM1).**
  - **El orden:** Inicio, Gestión de formularios, WhatsApp, las carpetas en orden alfabético con «Nueva carpeta» al final, y
    Administración al pie. Las carpetas van en su sección, separada con la línea de siempre.
  - **Cada carpeta:** un botón de panel con su ícono, como Administración.
  - **El panel:**
    - en el encabezado, el ícono, el nombre y el ⋮ para quien administra;
    - arriba, los formularios de la carpeta, con el ícono de formulario;
    - debajo, un grupo plegable por subcarpeta, con sus formularios; sólo se abre el del formulario abierto;
    - al final, «Agregar formularios» para quien administra.
  - **Qué abre un formulario:**
    - a quien ve o aprueba respuestas, la planilla, con «Agregar respuesta» si además carga;
    - a quien sólo carga, la carga;
    - a quien sólo administra, el diseño.
  - **Si se entró desde el menú,** las migas dicen «Repartos / Control de carga» y el panel queda abierto con el formulario
    marcado.
- **Crear y poner formularios (CM2).**
  - «Nueva carpeta» abre un diálogo de 32 rem con el nombre y el ícono. El ícono se elige en una lista con el dibujo y el
    nombre.
  - La carpeta aparece abierta, en su lugar alfabético. Vacía, el panel dice «Todavía no hay formularios en «Compras»» y
    ofrece «Agregar formularios».
  - «Agregar formularios» abre «Formularios de «Compras»», con un buscador y una casilla por formulario:
    - lista todos los formularios que no están dados de baja; al abrir, los que ya están en la carpeta van primero;
    - un borrador lleva la etiqueta «Borrador»;
    - guarda los formularios de esa carpeta, no los de sus subcarpetas, junto con la lista que se leyó; si otro la cambió,
      avisa con «Volver a leer» y conserva lo marcado.
- **Los listados y «Carpetas…» (CM3).**
  - Los tres listados, a todo el ancho.
  - Diseñar formularios y Catálogos suman el filtro «Carpeta»:
    - es un `FilterMenuTrigger` con radios, como Estado;
    - ofrece «Todas» separada, «Sin carpeta» y las carpetas con sus subcarpetas debajo, con la tilde a la izquierda;
    - va en la dirección (`?carpeta=`) y mantiene los vacíos de CP4.
  - «Carpetas…» muestra dos niveles. «Nueva carpeta» abre el diálogo de CM2 y deja tildada la carpeta creada; sólo la ve
    quien administra formularios.
- **Ordenar (CM4).**
  - **El ⋮ de la carpeta:** «Editar…» (nombre e ícono), «Nueva subcarpeta…», «Mover a…» y «Borrar». Con subcarpetas, «Mover
    a…» queda apagado, con «Tiene subcarpetas: no puede ir adentro de otra.» debajo.
  - **El ⋮ de una subcarpeta** aparece al pasar el mouse o con el foco, y en una pantalla táctil está siempre a la vista
    (`@media (hover: none)`). Ofrece «Editar…» (sólo el nombre), «Agregar formularios…», «Mover a…» y «Borrar».
  - **Mover** ofrece «Al menú, como carpeta propia» o «Adentro de «Compras»». Arranca en donde está la carpeta.
  - **Borrar** cuenta lo que se pierde. Una carpeta «sale del menú»; una subcarpeta «sale de «Repartos»». Nunca se borra un
    formulario ni un catálogo.
- **Personas y pantallas (CM5).**
  - Quien sólo carga no ve «Nueva carpeta», ni los ⋮, ni «Agregar formularios», y no le aparece nada vacío.
  - En el celular, cada carpeta es un grupo del cajón, con sus subcarpetas adentro y el ⋮ en el renglón de la carpeta y de
    cada subcarpeta.
  - Con el menú contraído, cada carpeta es su ícono, con el nombre en el tooltip, y abre el panel.
- **Estados (CM6).**
  - **Sin carpetas:** sólo «Nueva carpeta», para quien administra.
  - **No se pudo leer:** el aviso del menú, con «Reintentar».
  - **Nombre repetido:** el error debajo del nombre.
  - **Otro la cambió:** un 409 con «Volver a leer».
  - **Subcarpeta vacía:** su propio «Agregar formularios» adentro del grupo.
  - **Carpeta con sólo catálogos:** «Ver sus 14 catálogos», que abre Catálogos con esa carpeta elegida.
  - **Mientras se lee:** las carpetas no ocupan lugar.
  - **La carpeta abierta se borró:** su panel se cierra.
  - **Lo que crea el sistema:** se crea en el primer nivel y en su lugar alfabético.
  - **Se relee solo:** como dice la decisión 13. Quien hizo el cambio lo ve enseguida; una publicación por WhatsApp, cuando
    termina, si sigue en la pantalla del formulario. Los demás lo ven al volver a la pestaña o al recargar.

### Backend

- **Dominio:**
  - `BusinessFormFolder.MaxDepth` pasa a 2, y `TooDeep` dice dos niveles.
  - Una carpeta del primer nivel lleva siempre uno de los doce íconos; una subcarpeta, ninguno (`IconInvalid`).
  - Se van `FormFolderMenuPlacement`, `MenuPlacement` y `SetMenu`.
- **Crear y mover:**
  - `POST api/forms/folders` recibe un `icon` opcional, sólo en el primer nivel; sin él, toma el primero libre.
  - `PUT api/forms/folders/{id}` recibe un `icon` opcional. La carpeta que sube al primer nivel toma ese ícono o el primero
    libre; la que entra en otra lo pierde.
  - Se va `PUT api/forms/folders/{id}/menu`.
- **Permisos:** crear, renombrar, mover, borrar y contar lo que se borra piden `formtemplates.manage`. Poner un catálogo en
  carpetas sigue con `formcatalogs.manage`.
- **Los formularios de una carpeta:**
  - `GET api/forms/folders/{id}/forms` devuelve `{ formIds }`.
  - `PUT api/forms/folders/{id}/forms` recibe `{ expectedFormIds, formIds }` y responde 409 `Forms.Folder.SetChanged` si la
    lista cambió.
  - Pide `formtemplates.manage`, en una transacción con el lock `forms:folders`.
  - Reemplaza sólo los formularios que no están dados de baja. Los dados de baja quedan como estaban, así vuelven si se
    reactivan.
- **El menú (`GET api/forms/menu`):**
  - Lo lee quien puede abrir formularios (ver, aprobar o cargar) y quien administra formularios.
  - Devuelve `{ canManage, menus: [{ folderId, name, icon, revision, forms, groups: [{ folderId, name, revision, forms }] }] }`;
    a quien administra, cada menú suma `catalogCount`.
  - A quien no administra, sólo formularios activos que puede abrir y nada vacío, como hoy. A quien administra, todas las
    carpetas y subcarpetas, también vacías.
  - Se van los grupos de «Gestión de formularios» y de «Administración».
- **Los listados:**
  - `api/forms/overview` deja de recibir `folderId` y `withoutFolder`.
  - El de diseños y el de catálogos los conservan.
- **La migración:**
  1. Las pertenencias de las carpetas del tercer nivel o más abajo pasan a su antecesora del segundo nivel, sin repetir (hay
     un índice único por carpeta, tipo y elemento).
  2. Si una de esas tenía clave del sistema y la que la recibe no, la recibe.
  3. Se borran esas carpetas.
  4. `MenuIcon` toma el ícono que tenía, o «folder», en el primer nivel, y queda nulo en el segundo.
  5. La restricción pasa a «primer nivel si y sólo si tiene ícono».
  6. Se quitan `MenuPlacement` y su restricción.
  Las carpetas del primer nivel que estaban fuera del menú o adentro de un panel pasan a ser menús.
- **Las carpetas del sistema:** `FormSystemFolderLinker` las crea en el primer nivel, con el primer ícono libre.
- **Rutas:**
  - Se suman `GET` y `PUT api/forms/folders/{id}/forms` y se quita `PUT api/forms/folders/{id}/menu`, en
    `ExplicitRouteInventoryTests` y en el inventario del módulo.
  - Los textos de los errores van en `Errors.resx` y `Errors.en.resx`.

### Front

- **El caparazón:**
  - `FormMenuShell` lee el menú también para quien administra formularios.
  - `folderMenuNavigation` queda con `menus` y `canManage`.
  - `formMenu.ts` decide a dónde lleva cada formulario: la planilla, la carga o el diseño.
- **`Sidebar.tsx`:**
  - la sección de carpetas después de WhatsApp, con «Nueva carpeta»;
  - el panel con su ⋮, los grupos con el suyo, «Agregar formularios», el vacío y «Ver sus N catálogos»;
  - el cajón y la barra contraída.
- **Los diálogos:**
  - nuevos: `FolderEditDialog` (crear y editar, con nombre e ícono) y `FolderFormsDialog`;
  - con dos niveles y textos nuevos: `FolderMoveDialog`, `FolderDeleteDialog` e `ItemFoldersDialog`;
  - se van `FolderLayout`, `FolderBrowser`, `FolderMenuDialog`, `FolderNameEditor` y `TreeList`, que no tiene otros usos.
- **Los listados:** `FormOverviewPage` queda sin carpetas; `FormsPage` y `CatalogsPage` suman el filtro.
- **La relectura:**
  - Se invalida la lectura del menú después de publicar (al recibir `Published`), dar de baja, reactivar, eliminar, cambiar
    las carpetas de un formulario y guardar los formularios de una carpeta. El editor y el listado ya invalidan
    `folderActorKey`; queda escrito y con su prueba.
  - Para los demás, alcanza `refetchOnWindowFocus` con los 30 s de `staleTime`.
- **Textos y guías:**
  - `formFolders.json`, en es y en.
  - La sección «Navegación» de `visual-baseline.md` se reescribe al programar.
  - La guía [Quitar Formularios](../guides/quitar-formularios.md) nombra los archivos del menú.

### Pruebas

- **Unitarias:**
  - dos niveles, al crear y al mover;
  - el ícono al crear, al subir y al entrar en otra, y el primero libre;
  - los formularios de una carpeta, con su 409 y sin tocar los dados de baja;
  - los permisos.
- **De integración, con PostgreSQL:**
  - el menú para quien administra: todas las carpetas, las vacías, la revisión y `catalogCount`;
  - el menú podado para quien sólo carga;
  - el menú de quien diseña sin ver respuestas;
  - el `PUT` de los formularios de una carpeta;
  - `overview` sin `folderId`;
  - la migración con una carpeta del tercer nivel, una del sistema y pertenencias repetidas;
  - las rutas en el inventario.
- **Front, con Vitest:**
  - el menú con las carpetas después de WhatsApp;
  - el panel con su ⋮, los grupos, el vacío y el de sólo catálogos;
  - los diálogos;
  - el filtro en Diseñar formularios y en Catálogos;
  - el cajón y la barra contraída;
  - que la carga no ocupe lugar;
  - **la relectura del menú al publicar, dar de baja, reactivar y eliminar, desde el listado y desde el editor, con el panel
    abierto** (decisión 13).
- **Visuales:** `folders.spec.ts` dibuja CM1 a CM6 a 1440, 390 y 320 px, en es y en; se van las escenas del panel.
- **Ejemplos HTTP:** `forms-http/folders` se regenera con la exportación, y el test del front que los lea sigue pendiente.
- **A mano, en la prueba integral** (reemplaza la de arriba):
  1. crear una carpeta y verla en el menú;
  2. ponerle formularios desde el menú y desde «Carpetas…»;
  3. con el panel abierto, publicar, dar de baja, reactivar y eliminar uno;
  4. crear una subcarpeta, mover y borrar;
  5. entrar como alguien que sólo carga;
  6. usar el filtro en Catálogos y en Diseñar formularios;
  7. repetirlo en el celular.

### Cortes

1. **Backend:** dominio, migración, rutas, menú, los formularios de una carpeta, listados, permisos, pruebas y ejemplos HTTP.
2. **Front, el menú:** la sección de carpetas, el panel, los ⋮, los diálogos, el cajón, la barra contraída, los estados, la
   relectura y sus escenas.
3. **Front, los listados:** sin panel, el filtro «Carpeta», «Carpetas…» con dos niveles y sus escenas.
4. **Documentación y cierre:** `visual-baseline.md`, la guía de quitar formularios, la regla de
   [formularios](../features/formularios.md) y este plan, con la corrida que corresponda.

## Relación con otros planes

- **[ARCA](2026-10-31-arca-formularios-estandar.md):** es parte de la etapa 2 y la carpeta «ARCA» llega con la etapa 3.
- **[Documentos por WhatsApp](2026-10-22-documentos-por-whatsapp.md):** la biblioteca de estándares usa la carpeta que trae
  cada estándar.
- **Multitenant:** las carpetas son por organización. Al cerrar, se ofrece el traspaso.
- **[Quitar Formularios](../guides/quitar-formularios.md):** sacar el módulo saca también los menús y los grupos de
  carpetas del menú lateral.
