# 03 · Reglas del usuario

Esto es lo que la memoria de la cuenta de ArquitecturaBase sabía del usuario y de su forma de trabajar, sin datos
personales. La otra cuenta no tiene esa memoria: tomalo como reglas de trabajo con él desde el primer día. Las reglas de
diseño ya escritas en `ArquitecturaBaseFront/docs/design/visual-baseline.md` siguen valiendo; acá están resumidas, con el
porqué cuando se conoce.

Marcas: «(nueva)» no estaba en los traspasos 2 a 9; «(cambió)» reemplaza algo anterior. Sin marca, ya estaba.

## Forma de trabajo

- **Todo en `main`.** Se trabaja en el checkout principal de cada repo, sin ramas ni worktrees (tampoco temporales), y sin
  push salvo que lo pida. La recomendación genérica de «branch first» no aplica. Lo marcó como fundamental. (nueva)
- **Commits chicos, en español, con conventional commits.**
- **Varias sesiones trabajan a la vez en el mismo checkout.** (nueva)
  - Cada sesión commitea sólo lo suyo: los archivos exclusivos con `git commit -- <rutas>`.
  - En un archivo con cambios de otra sesión, se prepara sólo el fragmento propio en el índice.
  - Nunca stash, reset ni cambio de rama, y nunca se revierte lo que escribió otra sesión.
- **Si el checkout compartido no compila** por trabajo a medias de otra sesión, se prueba en una copia de `HEAD` dentro de
  una carpeta ignorada del repo. (nueva)
- **Las decisiones confirmadas no se cambian sin volver a preguntarle**, aunque una revisión recomiende otra cosa. Se frena
  ese punto, se hace el resto y se trae la pregunta con su porqué. Una decisión que ya está en el plan no se vuelve a
  preguntar, y lo que el plan no fija se resuelve con la opción más simple que cumpla las reglas.
- **Para decidir, pocas opciones**, con la recomendada primero, de a una pregunta, y con una captura o un tablero antes de
  preguntar. (nueva)
- **Arquitectura clásica de .NET.** Controllers MVC finos, servicios por entidad y repositorios. Rechazó con enojo los
  endpoints mínimos y los handlers por caso de uso. Antes de proponer una convención estructural distinta, hay que
  confirmarla con él. (nueva)
- **Modo liviano en las etapas de un plan grande:** un diseño con un revisor, de 6 a 10 tareas por etapa y una sola
  revisión por tarea. La suite completa va al cerrar la etapa. Sólo se frena para preguntar si algo cambia lo que se ve o
  contradice una decisión suya. Lo pidió porque el proceso completo costaba de 25 a 30 horas por etapa. (nueva)
- **Cada cambio se revisa y se valida.**
  - TDD donde hay lógica.
  - En cada corte, el build y los tests rápidos enteros. Nunca filtrados por módulo: un filtro dejó pasar un test que rompió
    `main`. (nueva)
  - Al cerrar, una sola corrida completa con Docker. Si falla, se corrige, se repite lo que falló y después otra completa:
    vale la última en verde.
- **Para un cambio de presentación chico** alcanzan los tipos, el lint, los tests del componente y sus capturas. (nueva)
- **Corridas largas sin que la PC se suspenda:** Docker no se recupera de una suspensión, y la falla parece un bug del
  producto. Se corren en tandas cortas en primer plano, con la PC despierta. (nueva)
- **Tests que fallan:** un test intermitente del front por carga de la máquina se repite antes de buscar un bug. Un test
  que falla sólo en la suite completa tiene datos mal aislados: se arregla el test. (nueva)
- **Con la app levantada, ninguna sesión edita el `src` del front.** Vite le recarga la página al usuario y le borra lo que
  no guardó. Mientras él prueba, las sesiones dibujan, investigan o corren tests. (nueva)
- **Lo que levanta el AppHost se apaga con `aspire stop`** antes de devolver el turno. El contenedor de PostgreSQL queda vivo
  a propósito.
- **Sin manejar la PC del usuario:** nada de computer use ni escritorio remoto. Se usan SQL directo, la consola o un
  navegador propio, y se le avisa de cualquier efecto visible en su pantalla. (nueva)
- **Claves y credenciales:** Claude nunca guarda, copia, carga ni imprime claves reales, aunque se lo pida. La clave de un
  proveedor la pega el usuario en la pantalla de configuración. Los logs nunca llevan valores. (nueva)
- **Los traspasos al multitenant** son documentos en `docs/plans/` de ArquitecturaBase, con un prompt para el chat guía y
  sin código para copiar. Nunca se escribe en el repo del multitenant. Lo de ArquitecturaBase se adapta a su modelo:
  organizaciones, RLS, feature gates, empresas y clientes. (cambió: primero fue un paquete grande; después, documentos chicos
  por desarrollo; este traspaso 10 vuelve a ser un paquete, a pedido del usuario)
- **Los videos que manda** se miran por cuadros y con una transcripción local, corrigiendo los términos técnicos con los
  cuadros. (nueva)

## Pruebas con el usuario

- **En las pruebas de punta a punta, Claude no carga catálogos, formularios ni datos por él.** Le da los pasos concretos, de
  a uno o de a pocos, clic por clic y con la ruta exacta del menú, y espera lo que vio. Quiere recorrer el circuito como lo
  va a usar el cliente. Una primera versión sin rutas no se entendió. (nueva)
- **El usuario pierde el hilo de lo pendiente** y pidió que se le recuerde. La fuente es el índice de planes.
- **«Algo salió mal» sin código en el front** casi siempre es el esquema estricto del front que rechazó un valor nuevo del
  backend. Se reproduce la respuesta con un test de integración antes de sospechar de permisos o migraciones. (nueva)
- **Una pantalla nueva se prueba en los dos idiomas y en tres anchos:** 1440, 390 y 320.

## Diseño: cómo se decide

- **Una pantalla nueva se dibuja antes de programarse**, con la funcionalidad completa. (cambió: el lugar del dibujo)
  - Un tablero por pantalla con layout, acción primaria, carga, vacío y sin coincidencias, error con reintento, lo que se
    ve sin permiso, los diálogos y lo que vive en la URL.
  - El usuario elige y recién entonces se programa.
  - Se dibuja en un lienzo provisorio con el formato del sistema visual; después se registra lo elegido en `docs/design/`
    del front. Pasar los tableros al lienzo del sistema visual quedó pendiente.
- **El dibujo va en un lienzo**, nunca en rutas de diseño del front. Lo quiere ver «parecido a Figma», probarlo y
  reajustarlo ahí. Se arma con las piezas de la biblioteca, no pintando a mano. (nueva)
- **Un plan visual ancla al tablero:** «queda igual al tablero X», con su estructura. Al programar se abre el tablero y se
  compara antes de cerrar. Una lista de atributos sueltos se puede cumplir entera y dar una pantalla que no se parece al
  dibujo. (nueva)
- **Un cambio pedido sobre una captura vale para toda la app.** Se buscan todos los usos de la pieza o el patrón, se dibujan
  casos de varias áreas y se escribe la regla general en los textos de diseño del front. Una regla nueva se aplica también a
  lo que ya existía: el usuario preguntó por qué todo seguía «feo» si ya habían hablado de diseño. (nueva en parte)
- **Los cambios de diseño de varias áreas** van a sesiones propias: primero dibujan, y programan cuando el usuario elige y
  deja de probar. (nueva)

## Diseño: reglas visuales

- **Que no parezca plantilla.** Los colores son lo de menos: delatan la escala y las formas repetidas. Sus referencias son
  productos densos, de tipo consola. Evitar el cuadrado de color en lugar del logo, la banda tintada del encabezado, el
  ícono de sección en su cuadradito, las versalitas, las pastillas redondas, los avatares circulares y las sombras. El tema
  vigente es el claro aprobado el 2026-09-30. (nueva)
- **Escala de escritorio al 75 %:** en escritorio la raíz va en 75 % y todo se mide en `rem`. Una medida nueva va en rem o
  en la escala de Tailwind, nunca en px. Quedan en px sólo los bordes, los anillos de foco y los breakpoints. (nueva)
- **Botones habilitados o apagados**, sin textos al lado. Lo apagado dice por qué en su tooltip. Una acción que tarda muestra
  el aro y su gerundio («Guardando…») y no acepta otro clic. Una confirmación queda abierta hasta la respuesta. Un listado
  que relee atenúa sus filas viejas y corre una línea bajo el encabezado. (cambió: afinado)
- **Sin leyendas explicativas.** Lo que ayuda a decidir va en el ícono (i), con su tooltip; lo demás se saca. Se mantienen
  los errores de validación. La regla está protegida por tests de arquitectura.
- **Recuadros y separaciones.** (nueva)
  - Cada zona va en un recuadro con banda de título y acciones.
  - Las filas van separadas por líneas, y el elegido de una lista lleva una barra a la izquierda.
  - Quitar va en rojo.
  - Los botones de alta dicen «Agregar …».
- **Tablas, en toda la app:** una línea fina entre columnas, los números a la derecha y una línea de cierre abajo. Un código
  no se parte en sus guiones. (nueva)
- **Mucho texto, con jerarquía:** todo a la vista, sin plegar ni cortar, y cada cosa en un lugar fijo. (nueva)
  - El rótulo va en gris y el valor en negro, en dos columnas; lo secundario, más chico y en gris.
  - Las fechas van a la derecha, una vez por momento, y no se repiten datos.
  - Lo que pasa en orden va en pasos con ícono.
  - Lo que se consulta poco, como un historial, va detrás de un botón en el encabezado de su sección.
  - Toda comparación se lee lado a lado y sólo con lo distinto.
- **Lo elegido se distingue con algo más que un tinte:** relleno, tilde, punto, subrayado o barra. (nueva)
- **Filtros de un listado.** (nueva; reemplaza mostrar las cantidades del servidor)
  - A la vista, hasta tres filtros frecuentes. El resto va en un solo «Filtros».
  - Sin contador de resultados, y cada cambio se aplica al instante.
  - Lo que espera una acción, como «Por aprobar», va en solapas.
  - Todo filtro de opciones es un desplegable simple, de a una opción, con «Todas» y buscador, sin cantidades.
- **El estado se dice una vez:** lo que dice el encabezado no se repite debajo (EP1-A). (nueva)
- **Lo que carga no mueve lo que ya se ve:** un bloque de carga mide lo que reemplaza, y recargar no deja saltos. (nueva)
- **Una pantalla de ajustes se arma como Configuración**, con zonas apiladas. Un dato con dos usos dice el estado de cada
  uno. (nueva)
- **Idioma:** el contenido se escribe en el idioma del menú, que es el original y el único obligatorio. La traducción es
  opcional y vuelve al original. El idioma se nombra sólo cuando no es el del menú. (cambió)
- **Ninguna elección se descarta en silencio:** un cambio de tipo no pierde lo escrito sin avisar. (nueva)
- **Decidir a propósito:** una acción que no se deshace y dispara avisos, como aprobar, pide confirmar con el número.
  (nueva)
- **Producto genérico:** ningún cliente, tabla ni ERP en el código, los textos, los datos de ejemplo, los tests ni las
  capturas. Lo particular es configuración.
- **Integraciones con bases externas** genéricas y en autoservicio, no por ERP. Es una propuesta sin decidir. (nueva)
- **Soluciones simples:** ante dos caminos, el más simple, sin mecanismos por las dudas. La robustez es proporcional al
  efecto (ADR 0018).

## Comunicación

- **Lo que necesita su visto bueno** (una corrida con un proveedor real, un cambio en la plantilla, programar algo de otra
  sesión) se pregunta con una pregunta cerrada, y se espera la respuesta. No se avanza por inferencia.
- **Lo que descubre al usar la app** se suma al mismo plan o revisión, para corregir todo junto.

## Advertencias técnicas

- **WhatsApp Flows:**
  - Con navegación condicional (`If`), declarar el `routing_model`: el que deduce Meta deja afuera la rama `else`.
  - Un Flow estático puede filtrar una lista por otra con `update_data` (Flow JSON 6.0 o más).
  - Un desplegable obligatorio no puede ir oculto: `required` va atado a `visible`.
  - El `label` de un `RadioButtonsGroup` admite 30 caracteres.
  - En la vista oficial, `flow_action_payload` se decodifica dos veces, así que el `%` se escapa dos veces. El modo
    interactivo necesita `flow_action=navigate` y la primera pantalla.
  - La política de seguridad de la Api tiene que permitir el marco de `business.facebook.com`.
  - Lo que mandó el celular se ve desde la base, con las claves, los tipos y los largos, nunca con los valores.
- **El agente de IA de Meta** (Meta Business Agent): con el agente como primer respondedor, los mensajes llegan por
  `standby` y no por `messages`, y la app deja de verlos. Recomendado no prenderlo en el número de la app. Si dejan de
  llegar mensajes, preguntar primero si se prendió.
- **El depurador frena la Api:** con el depurador enganchado, el log Debug va a la ventana de salida y la Api cae de unos 700
  a unos 11 pedidos por segundo. Por eso `Logging:Debug` va en Warning en desarrollo.
- **Compilar con la app abierta en el IDE:** usar `--artifacts-path` dentro de una carpeta ignorada del repo. En el directorio
  temporal del sistema fallan los ArchitectureTests.
- **Migraciones con `core.autocrlf`:** el snapshot puede salir con `\r\n` escapado en un CHECK. Hay que volverlo a LF antes
  de commitear, y un CHECK se escribe en una sola línea.
- **El exportador de ejemplos HTTP** corre la suite entera y escribe en el front: se corre desde una copia de `HEAD`, y sólo
  se copia la cohorte propia.
- **Tests lentos del front:** los primeros tests de un archivo pagan la carga diferida y, con la máquina cargada, se cortan
  a los 5 s. Se repiten antes de buscar un bug.
- **Las guías `AGENTS.md`:** cada cadena de guías desde la raíz queda por debajo de 30 KB. Un agente con límite de 32 KiB
  perdía reglas sin avisar.
- **Números de ADR:** mirar el índice antes de numerar uno nuevo. Dos sesiones tomaron el mismo número el mismo día.
- **Sin Docker no se da por cerrado**, y con la PC suspendida Docker queda colgado.
