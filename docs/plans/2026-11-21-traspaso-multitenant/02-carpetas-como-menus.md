# Traspaso 16 · 02 · Las carpetas son menús del menú lateral (CM1 a CM6)

Lo pidió el usuario el 20/11, durante la prueba integral; aprobó los tableros y se programó el mismo día. Backend `5969a47c`
(el plan) y `a7cee7cf`; front `76d6d722` (el diseño) y `d0cb2c9c`. Fuentes: la sección «Las carpetas son menús» del
[plan de carpetas](../../history/README.md#cierre-del-2026-11-26), la
regla «Carpetas» de `docs/features/formularios.md` y `ArquitecturaBaseFront/docs/design/carpetas.md`. Cae en la tanda 2 del
15.

Reemplaza lo que el T13/01 §4 contaba del panel de los listados (CP1-A), de administrar en el panel (CP3-A), de «En el
menú…» (CP5-A) y de los cinco niveles. Sigue lo demás: carpetas con subcarpetas, un mismo elemento en varias, un solo árbol
para formularios y catálogos, «Carpetas…» con casillas (CP2-A) y las carpetas que crea el sistema.

## 1. Lo que pidió el usuario

Había creado una carpeta y quedó en el panel de «Diseñar formularios», no en el menú. Pidió que crear una carpeta sea crear
un menú del menú lateral, donde va poniendo formularios para elegirlos ahí y no en la lista completa; lo que no está en
ninguna carpeta sigue sólo en la lista. Del panel de carpetas al costado de los listados dijo que no le gustaba. Es lo que
el 4/11 se había descartado como CP5-C.

## 2. Las decisiones (8 a 13 del plan)

8. **Cada carpeta del primer nivel es un menú del menú lateral.** Abre su panel, como Administración, con sus formularios y
   un grupo por subcarpeta.
9. **Se administran en el menú,** con «Nueva carpeta» y un ⋮ en el panel de cada carpeta y en cada grupo. Las crea, edita,
   mueve y borra quien tiene «Administrar formularios»; quien sólo administra catálogos pone catálogos en carpetas que ya
   existen.
10. **Un formulario entra desde la carpeta o desde el formulario:** «Agregar formularios», con una casilla por formulario, o
    «Carpetas…». Puede estar en varias.
11. **Dos niveles.** Una carpeta del menú puede tener subcarpetas, que son grupos de su panel; una subcarpeta no.
12. **Catálogos suma un filtro «Carpeta»** al lado de Estado y pierde el panel. Un catálogo se sigue poniendo en carpetas con
    «Carpetas…» y no aparece en el menú.
13. **El menú se relee solo** al publicar, dar de baja, reactivar o eliminar un formulario, con el panel abierto.

**Resueltas por defecto,** aprobadas con los tableros:

- Las carpetas van después de WhatsApp y antes de Administración, en orden alfabético y con «Nueva carpeta» al final. Así,
  cuando llegan, no corren lo que ya se ve.
- En el menú van sólo los formularios activos; «Agregar formularios» lista también los borradores, con su etiqueta.
- Quien administra formularios ve todas las carpetas, también las vacías o con sólo catálogos.
- Una carpeta nueva arranca con el primero de doce íconos que ninguna otra usa.
- «Formularios» (la lista de respuestas) pierde el panel y no suma filtro, porque sus carpetas son el menú; «Diseñar
  formularios» suma el filtro «Carpeta», como Catálogos.

## 3. Cómo quedó

- **Qué abre un formulario del menú:** a quien ve o aprueba respuestas, la planilla (con «Agregar respuesta» si además
  carga); a quien sólo carga, la carga; a quien sólo administra, el diseño. Entrando desde el menú, las migas dicen «Carpeta
  / Formulario» y el panel queda abierto con el formulario marcado.
- **El ⋮ de una carpeta:** «Editar…» (nombre e ícono), «Nueva subcarpeta…», «Mover a…» y, tras una línea, «Borrar» en rojo.
  El de una subcarpeta: «Editar…» (sólo el nombre), «Agregar formularios…», «Mover a…» y «Borrar». Una carpeta con
  subcarpetas no entra en otra, y su «Mover a…» queda apagado con el motivo. Borrar cuenta lo que se pierde y nunca borra un
  formulario ni un catálogo.
- **Quien sólo carga** no ve «Nueva carpeta», los ⋮ ni «Agregar formularios», y no le aparece nada vacío.
- **En el celular,** cada carpeta es un grupo del cajón, con su ⋮; con el menú contraído, su ícono, con el nombre en el
  tooltip.
- **Estados:** sin carpetas, sólo «Nueva carpeta» para quien administra; el aviso del menú con «Reintentar» si no se pudo
  leer; el nombre repetido, debajo del campo; el 409 con «Volver a leer»; «Ver sus N catálogos» en una carpeta con sólo
  catálogos; mientras se lee, las carpetas no ocupan lugar; si se borra la carpeta abierta, su panel se cierra.
- **El backend:**
  - `GET api/forms/menu` devuelve `{ canManage, menus }`, cada menú con sus formularios y sus grupos. A quien administra,
    todas las carpetas; a los demás, sólo los formularios activos que puede abrir, sin nada vacío.
  - `GET` y `PUT api/forms/folders/{id}/forms` leen y guardan los formularios de una carpeta junto con la lista que se leyó;
    si otro la cambió, 409. Los formularios dados de baja quedan como estaban, así vuelven si se reactivan.
  - Se fue `PUT api/forms/folders/{id}/menu`. El tope sigue en 500 carpetas y las carpetas siguen sin dar ni quitar acceso.

## 4. La migración, en dos despliegues

Pasar de cinco niveles a dos con datos cargados se hace en dos despliegues, con la regla de versiones del T14/01:

- **El primero (`FoldersAsMenus`)** toma el lock de las carpetas, pasa lo que estaba del tercer nivel para abajo a su
  antecesora del segundo, sin repetir pertenencias, y le da un ícono a cada carpeta del primero. La columna vieja
  (`MenuPlacement`) se queda para la versión anterior, y la nueva lee y escribe lo que escriba la anterior como dos niveles.
- **El segundo,** cuando el primero esté en todos los ambientes, repite el pase, anula el ícono de las subcarpetas, agrega la
  restricción «primer nivel si y sólo si tiene ícono» y borra la columna vieja. Después se saca la tolerancia del lector.
  Está pendiente, en `docs/plans/verificacion-y-produccion.md`.

## 5. Lo que vio la prueba

- El paso 65 (con el panel abierto, el formulario aparece al publicar y se va al dar de baja o eliminar), el 59 (el ⋮ del
  editor) y la prueba a mano del plan, de siete puntos, dieron bien el 20/11, también con alguien que sólo carga y a 390 px.
- O8 (el foco después de «Agregar subcarpeta») y O12 (un aviso de más después de borrar la carpeta elegida) se vieron en el
  panel de carpetas, que se fue con este rediseño.
- **Falta,** en curso: la corrida completa con la exportación de los ejemplos HTTP, que hace la sesión de las carpetas al
  cerrar la prueba, y el segundo despliegue de la migración.

## 6. No repetir

- Si una carpeta recién creada no aparece donde la persona la busca, el diseño no se entiende. Acá se la buscaba en el menú.
- Achicar una jerarquía con datos es una migración en dos despliegues, con un lector tolerante en el medio.
- Un menú que depende del estado de un formulario se relee solo; si no, ni la persona ni la prueba ven el cambio.

## 7. Para el multitenant

- **La pregunta 30 del 15 cambia.** Preguntaba en qué etapa entran las carpetas, que allá no tienen etapa ni rastro. Ahora
  son menús: la reemplaza la 72 del [07](07-preguntas.md).
- **Tu menú.** Tu manifiesto (`layouts/navigation/business.ts`) tiene entradas fijas con permiso, el panel de Gestión arriba
  y el de Administración al pie, uno abierto a la vez, y los módulos suman secciones por registro. Las carpetas serían
  entradas que crea cada organización, con su panel: entre lo de los módulos y Administración, y sólo con el módulo de
  formularios prendido para esa organización (`[FeatureGate("forms")]`).
- **Tu decisión 79** (una sola lista «Formularios», con pestañas, sin «Diseñar formularios») no choca: lo que no está en una
  carpeta sigue sólo en la lista. Como tu lista hace de «Formularios» y de «Diseñar formularios» a la vez, el filtro
  «Carpeta» iría en ella, como en «Diseñar formularios» acá.
- **Tu decisión 83** («Cargar» en cada fila, sin una entrada nueva en el menú para cargar) tampoco choca: la carpeta no es
  una entrada para cargar, y a quien sólo carga le abre la carga de cada formulario. El «Cargar» de la fila pasa a ícono con
  la regla del [03](03-diseno-para-toda-la-app.md).
- **Por organización.** Las carpetas son de la organización, como los formularios (tu 50 dice para quién es cada uno). No
  dan ni quitan acceso: el menú muestra a cada persona lo que puede abrir, con sus permisos y su «Vale en». El tope de 500 y
  los doce íconos son por organización.
- **Los permisos:** administrar carpetas pide tu `forms.manage` (acá `formtemplates.manage`), y poner un catálogo en
  carpetas, `formcatalogs.manage`.
- **Si ya diseñaste carpetas con el T13/01 §4,** el panel, «En el menú…» y los cinco niveles se van; no hace falta la
  migración en dos despliegues, porque allá no hay datos.
