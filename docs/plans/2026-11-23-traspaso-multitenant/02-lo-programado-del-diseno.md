# Traspaso 18 · 02 · Lo que se programó del diseño del 17

El [17](../2026-11-22-traspaso-multitenant/README.md) contó las reglas de diseño de los catálogos y formularios, con lo
decidido el 22/11. Desde entonces se programó buena parte: esto dice qué entró, qué falta y lo que se aprendió al
programarlo. Las reglas no se repiten: siguen valiendo como las cuenta el 17.

## 1. Ordenar las tablas (OT1-C): el backend completo

El usuario pidió arrancar el backend durante la prueba integral, y quedó completo el 22/11:

| Corte | Listas | Commit |
| --- | --- | --- |
| B1 y B2 | La regla común y Formularios | `366bedb7` |
| B3 y B4 | Usuarios y Roles | `21ffbfdb` |
| B5 y B6 | Catálogos y Diseñar formularios | `525d748f` |
| B9 y B10 | Documentos recibidos y Formularios que faltan | `67924ec4` |
| B7 y B11 | La planilla de respuestas y Datos de personas | `d6564f3b` |

«Por aprobar» (B8) no se hizo: quedó como cola, de la más vieja a la más nueva (`65607d38`).

- **La regla común está en un solo lugar** (`ApplySort`, en Infrastructure), y cada lista sólo declara sus campos:
  - lo vacío va al final en los dos sentidos: en un orden ascendente ya lo hace PostgreSQL; en uno descendente, o con varias
    claves, se ordena primero por «es nulo»;
  - con el mismo valor, las filas quedan en el orden de siempre de esa lista y después por su clave única, así el paginado
    no repite ni saltea filas;
  - un estado o un tipo ordena por su orden de declaración (el del circuito), con un `CASE`, aunque se guarde como texto;
  - un campo puede ordenar por varias claves (el número de una respuesta es su prefijo y su número), y una lista cuyo orden de
    siempre tiene varias claves lo declara aparte (`ApplySortWithDefaultOrder`).
- **Las columnas dinámicas suman su id:** la planilla ordena por una pregunta (`sortFieldId`), por un dato del catálogo de una
  pregunta (`sortCatalogFieldId`) o por un resultado de una regla (`sortResultId`), comparando el valor guardado, así un número
  ordena como número; Datos de personas, por un atributo (`sortAttributeId`).
- **Lo que no ordena, y por qué:** en Diseñar formularios, la deprecación y el estado de los dados de baja, porque los da el
  canal de WhatsApp y no se pueden unir en la consulta; en Documentos recibidos, los datos leídos, porque viven en la propuesta
  cifrada. Lo anota el plan, para que no se vuelva a intentar.
- **Lo que cambió para el usuario:** en la planilla, dos respuestas con la misma fecha muestran primero la de número más nuevo.
  En Documentos recibidos y en Formularios, lo que no tiene respuesta empata al ordenar por su número y sigue el orden de
  siempre (`54fd8fd2`).
- **El contrato no cambió de forma:** `sort=campo` o `sort=-campo`, con el nombre del backend, como antes. El OpenAPI sigue
  diciendo que `sort` es un texto.
- **Falta el front,** que entra después de la prueba: la flecha en el encabezado, «Ordenar por» al final de la barra (con la
  lista de la pieza única), el primer toque en el sentido útil y el orden en el navegador para las dos listas que llegan
  enteras, con lo vacío al final y el orden estable.

## 2. La pieza única para elegir: aplicada en el front

Programada en copias fuera del front y aplicada en cortes el 22/11, cuando el usuario dijo «Sí, aplicá ya»: la pieza, sus
pantallas y la extensión del 22/11 (las opciones fijas en toda la app, la carga, los filtros sin cantidades, «Ingresar con»
y lo que se elige en WhatsApp). Se retiraron las piezas viejas, y una regla de arquitectura impide volver a usarlas: el select
del navegador queda sólo en la vista previa que imita WhatsApp, y el autocompletar, sólo en el buscador de lugares.

Lo que se aprendió al programarla:

- **El foco.** En el celular, cerrar la lista devuelve el foco al botón, y una confirmación que abre una elección también lo
  devuelve si se cancela. Además, una regla nueva de la pasada final: el foco queda donde lo llevó la elección. En «Pegar desde
  Excel», la pieza lo devolvía a su botón y no al lugar al que la elección lo había llevado.
- **Elegir lo ya elegido también avisa.** La pieza llama a su cambio aunque se toque la opción que ya estaba: donde elegir
  reinicia algo (otro campo, una lista que depende), hace falta una guarda.
- **Los detalles que se notan:** la lista abre con la elegida a la vista; una lista del servidor no dice «0 opciones» cuando
  ya dice que no hay; la lista de un botón de la barra toma el ancho de sus nombres.
- **Falta la pasada final:** corregir las guías de componentes que todavía nombran las piezas viejas, rehacer las capturas
  comparadas con las de antes, sumar la regla del foco y la corrida completa con la máquina quieta.

## 3. La respuesta como el formulario (VF1-A y VF2-A): casi entera en la app

- **El contrato del detalle (`b857e1d6`).** El detalle de una respuesta trae, además de los valores, la forma de carga de la
  versión con que se cargó: las preguntas en orden con su tipo, los títulos de sección, las condiciones y los cálculos y, en
  cada pregunta de catálogo, sus datos visibles con su nombre en cada idioma y cuál filtra (CW7) o agrupa (LD1-A). Es una sola
  lectura, con los mismos permisos de antes.
- **Las dos pantallas,** la vista rápida y la pantalla completa, leen la respuesta con la forma de su formulario (front
  `559759a6` y `d3eab5d7`), y «Corregir» destraba los mismos controles en el lugar, sin la página de corregir (`1060ce41`). La
  regla «nada debajo de otro dato» está en el fundamento visual (`6908c39b`), y la regla funcional, en `docs/features/formularios.md`
  (`208d1ad0`).
- **La actividad, fila por fila,** va en una tabla en la pantalla completa (`3b977974` y `d092a78f`), y el renglón de cada
  persona de un aviso a un rol dice el rol, con su nombre vigente (back `06df1a4b`, front `427c4250`).
- **El barrido de «nada debajo de otro dato»** quedó hecho con el tablero VF3 y R2-A, que eligió el usuario: lo que propuso el
  asistente, con cada dato en su renglón, y la celda de la planilla, de donde sale el detalle de una opción (`6c760450`); la
  tarjeta de una respuesta en el chat (`d4a83756`); y el PDF, con cada dato del catálogo en su renglón y, en un bloque, en su
  columna (`e8f503e9`). Los PDF ya emitidos no cambian.
- **Faltan** la exportación de los ejemplos HTTP, la verificación y el cierre.

## 4. Información del negocio: decidida, sin programar

El 22/11 el usuario eligió IN1-C con IN2 e IN3 (el plan vive en el front, `docs/plans/2026-11-22-informacion-del-negocio-reordenar.md`):

- **IN1-C:** Servicios y Preguntas van debajo de Ubicación, y el horario pasa a una tabla de lectura (Día · Horario · lápiz).
  Cada día se edita en un diálogo igual al de una fecha especial, y las fechas especiales van en otra tabla (Fecha · Horario ·
  lápiz y tacho). En la compu, la pantalla pasa de unos 1.350 px de alto a unos 670.
- **IN2:** los diálogos de cada día y de una fecha, con la atención (sin definir, abierto, cerrado o 24 horas) en la pieza
  única, no en un segmentado.
- **IN3:** los estados: sin permiso, un error entre días al guardar, muchas fechas especiales y la carga.
- De esta pantalla salió «todo en desplegable»: preguntado si valía sólo para el horario, el usuario contestó «En toda la app».

## 5. Formularios en una sola entrada (FU1-B y FU2-A): elegida, sin programar

El 23/11 el usuario pidió unir «Formularios» y «Diseñar formularios», que listaban los mismos formularios en dos entradas:
«Formularios y adentro sería el ABM, y un ícono más que sea cargar datos». El diseño está en el front,
`docs/design/formularios-una-entrada.md`.

- **FU1-B:** se va «Diseñar formularios». En el panel de «Gestión de formularios» quedan arriba «Formularios» y «Documentos
  recibidos», con su número, una línea sin título y abajo «Catálogos», «Biblioteca» y «Formularios que faltan».
- **FU2-A:** `/formularios` es la lista de diseño, con sus columnas y sus filtros, más la columna «Por aprobar», sólo para quien
  aprueba, cuyo número abre la planilla con lo que espera, y «Cargar» como primer ícono de cada fila publicada. El nombre abre
  el editor, y el número de respuestas, la planilla.
- **Según quién entra:** quien no diseña ve la lista de Formularios de hoy, con su «Cargar». El filtro de estado suma «Con algo
  por aprobar».

## 6. Para el multitenant

Leído en tu `main` (`c339b58d`). Tu módulo de formularios todavía no tiene código: lo que sigue va a tus specs y tus fichas.

- **El orden en la base (tu E15, tu E21 y tu ficha de paginado).**
  - Coincide: tu `ApplySort` ya desempata siempre con una clave única (`Infrastructure/Persistence/Extensions/QueryableExtensions.cs`),
    y tu «Por aprobar» ya es una cola, de la más vieja a la más nueva (`docs/plans/formularios/21-reglas-y-aprobacion.md`).
  - Falta lo vacío al final: tu `ApplySort` ordena sin «es nulo», y tus specs tienen casos donde importa, como un formulario
    sin respuestas al ordenar por `lastRecordAtUtc` (su `lastRecord` es `null`) o «Sin respuesta» al ordenar por una pregunta
    (`15-respuestas.md`).
  - Falta un estado en el orden de su circuito: tu E21 suma la columna «Estado» y su filtro sin decir cómo ordena.
  - Las dos van a tu ficha de paginado, sin pregunta nueva: son la regla de la base de lo que el 17 preguntó para la vista
    (su pregunta 76).
- **El detalle de una respuesta (tu E15 y tu E21).**
  - Tu `GET /{id}/records/{recordId}` ya trae las preguntas de su versión (`questions[]`, `titles[]` y `values`), pero no lo
    que hace falta para leerla como el formulario de carga: las condiciones, los cálculos y, en cada pregunta de catálogo, qué
    datos se ven y cuál filtra o agrupa. Los anchos se calculan del tipo, como en tu E20.
  - Tu corrección abre otra pantalla con la versión activa (`GET …/correction`). VF1-A corrige en el lugar, y avisa al empezar
    si la versión activa cambia algo: es la pregunta 77 del 17.
- **La pieza única (tus `docs/rules`):** cuando la adoptes (pregunta 74 del 17), sumá a su ficha el foco y la guarda de
  «elegir lo ya elegido».
- **Información del negocio** es de esta app. Si dibujás un horario por día, la lectura en tabla con un diálogo por día es lo
  que eligió el usuario acá.
- **Formularios en una sola entrada:** acá se llegó a lo que tu decisión 79 ya tenía, una sola lista «Formularios», sin
  «Diseñar formularios» (`docs/plans/formularios/14-formularios.md`). De FU2-A te sirven la columna «Por aprobar» y «Cargar»
  como primer ícono de cada fila, y de FU1-B, los dos grupos del panel. Si cambian tus tableros, mostráselos al usuario.
