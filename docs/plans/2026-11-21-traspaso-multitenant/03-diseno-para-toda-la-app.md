# Traspaso 16 · 03 · Diseño para toda la app: las filas, sólo con íconos, y una sola pieza para elegir

Dos reglas para todas las pantallas, las dos pedidas por el usuario durante la prueba integral. La primera está en la app; la
segunda está programada en una copia y todavía no entró al front. Cae en la tanda 1 del 15 (diseño para toda la app).

## 1. Una fila de tabla no lleva botones ni enlaces con texto (20/11)

Front `852b1699`. La regla está en «Íconos y acciones» de `ArquitecturaBaseFront/docs/design/visual-baseline.md` y en el
estándar visual del front.

- **De dónde sale.** El usuario vio «Cargar» como un botón con texto en la tabla de Formularios y recordó que se había
  diseñado con íconos.
- **La regla.** Lo que se hace con una fila (cargar, crear, revisar, agregar, restaurar, reactivar, descartar) es un ícono con
  su nombre en el tooltip y el dato de la fila en el nombre accesible, en la columna «Acciones», al borde derecho. En un
  listado va en el grupo de íconos de `RowActions`; en una tabla que se edita en el lugar (las opciones de un catálogo, los
  renglones), como un `IconButton` más de la fila.
- Ir a otro lado sigue siendo un enlace. Una acción que tarda cambia su dibujo por el aro (LC1-A) y se apaga mientras otra
  fila guarda.
- Lo que es un dato de la fila (el nombre que la abre, el número de la última respuesta, lo que espera aprobación) sigue
  siendo texto.
- En el celular, la tarjeta de un listado puede llevar su botón con texto a todo el ancho, como la Biblioteca.
- **Lo que cambió:** «Cargar» en Formularios, que pasó a un enlace con su ícono; «Armar con la IA», «Crear formulario»,
  «Marcar como revisado» y «Descartar» en Formularios que faltan; «Agregar» en la Biblioteca; «Revisar» en «Por aprobar»; y
  «Restaurar» y «Reactivar» en las opciones de un catálogo.

## 2. Una sola pieza para elegir de una lista (DS1-A y DS2-A, en curso)

Decidido el 19/11. Se programó entero el 20/11 en una copia, fuera de la carpeta del front, porque la prueba corre con la app
levantada: **todavía no está en el front.** Se aplica al cerrar la prueba, con la app apagada y después de la corrida de las
carpetas, y lo trae el traspaso 17. El plan está en el front: `ArquitecturaBaseFront/docs/plans/2026-11-19-una-sola-pieza-para-elegir.md`
(`3a0e0e51`; el backend lo escribió en `cc55de86` y lo pasó al front en `649ef796`).

- **De dónde sale.** En «Editar usuario», Roles y «Campo asignado» se veían y se manejaban distinto. El relevamiento encontró
  seis formas de elegir de una lista, cada una puesta cuando apareció su caso: el select del navegador en 60 lugares (la zona
  horaria eran unas 420 zonas de IANA en crudo, armadas tres veces), un campo que se escribe en 14, un menú con tildes, el
  menú del botón de un filtro, el select de la biblioteca de componentes y una lista propia de la carga en el celular.
- **DS1-A, un botón que abre la lista.** Cerrada, la pieza es un campo con su flecha fina que dice lo elegido, igual para una
  y para varias. Abierta, una lista con la descripción en gris debajo del nombre; la elegida lleva la tilde a la izquierda y,
  en varias, una casilla, y marcar no cierra. Con más de diez opciones, o si vienen del servidor, el buscador va arriba,
  adentro de la lista, con el foco puesto y la cantidad debajo. En la barra de un listado el filtro conserva su botón y abre
  la misma lista. En el celular la lista ocupa la pantalla. Se descartaron el campo que se escribe con chips y elegir en un
  diálogo.
- **DS2-A, varias elegidas por su nombre.** El campo dice las elegidas separadas por coma, en el orden de la lista; si no
  entran, las que entran y «y N más». Nunca crece, y el nombre accesible lleva todas. Se descartaron los chips debajo del
  campo y «3 de 8».
- **Para toda la pieza:**
  - «Todas» o «Sin …» va primero, separada por una línea;
  - una opción apagada dice su motivo (MA1-A);
  - si las opciones vienen del servidor, «Buscando…», «Sin coincidencias», «Mostrar 50 más» y «Reintentar»; y las elegidas
    que no están en la página van primero;
  - con un tope, la lista dice «Hasta 5» y, al llegar, «Elegiste 5 de 5», y las demás se apagan;
  - de dos a cuatro opciones fijas van en radios o en un segmentado, también «Filas por página» y el idioma; los filtros
    siguen siendo desplegables.
- **La zona horaria:** una sola lista, con la ciudad y, en gris, la hora de su país y el desfase («Montevideo · hora de
  Uruguay · GMT−3»), para Mi perfil, Configuración y el horario del negocio. Viaja el identificador de IANA, como hoy.
- **Cambia PL2-A y FO1-A en un punto:** en el combo de un filtro con muchas opciones se busca en el buscador de la lista, no
  escribiendo en el campo. Lo demás de PL2-A sigue: «Todas», una opción por renglón, la tilde y sin cantidades.
- **Excepciones:** la vista previa del formulario, que imita WhatsApp, y los menús de acciones.
- **Al terminar,** la pieza (`Picker`) reemplaza a las demás, salvo el select de la vista previa y el buscador de lugares, que
  sugiere mientras se escribe. Una regla de arquitectura del front impide elegir de una lista con otra pieza.

## 3. Para el multitenant

- **Las acciones de una fila.** Tu regla las pone en el ⋮ (`RowActions` de tu `pantallas-y-ui.md`), con un ícono en cada
  opción. Acá son íconos a la vista, con su tooltip, desde antes del 20/11. Lo nuevo vale en los dos: nunca un botón con
  texto en una fila. Tu decisión 83 pone «Cargar» en cada fila de «Formularios»: con esta regla es un ícono con su tooltip o
  una opción del ⋮. Es la pregunta 70 del [07](07-preguntas.md).
- **La pieza única.** Tenés varias piezas para elegir (`SearchSelect`, `MultiSelect`, `CountrySelect`, `TimeZoneSelect`,
  `CultureSelect`, el segmentado de `RadioGroupField` y los desplegables de `FilterBar`) y ninguna regla de cuándo va cada
  una. DS1-A y DS2-A son esa regla. Coinciden en lo principal con tu `CountrySelect`, que ya muestra el buscador con más de
  diez opciones. Como acá todavía no está aplicada, conviene anotarla como regla y dibujar con ella los tableros nuevos; las
  medidas finas llegan con el traspaso 17. Es la pregunta 71.
- **Las preguntas 7 y 25 del 15** (el filtro como desplegable simple y el combo de una opción con «Todas» y buscador) siguen,
  con el cambio de dónde se busca. Tus desplegables de filtro llevan la cantidad por opción: es lo que pregunta la 7.
- **Tu `TimeZoneSelect`:** revisá que diga la ciudad, con la hora de su país y el desfase.
