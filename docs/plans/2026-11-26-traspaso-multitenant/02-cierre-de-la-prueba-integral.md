# Traspaso 19 · 02 · El cierre de la prueba integral

Sigue al [03 del 18](../2026-11-23-traspaso-multitenant/03-prueba-integral.md), que la dejó el 23/11 a la mañana. El 24/11 se
probó a mano lo que entró durante la prueba y aparecieron O50 a O60. El 25/11 el usuario decidió O47 y O48 y mandó arreglar el
resto, y el 26/11, con la corrida completa en verde, **dio la prueba por cerrada sin los pasos a mano**: el micrófono, un celular
real, el lector de pantalla, el certificado de ARCA con su CUIT (169) y el correo con un secreto mal puesto (180). Esos pasos
quedaron en [verificación y producción](../verificacion-y-produccion.md).

## 1. Las observaciones resueltas desde el 18

Todas con el test en rojo primero. Las que vienen del 16 o del 18 conservan su número.

| | Qué pasaba | Cómo quedó | Commits |
| --- | --- | --- | --- |
| O28 | El correo de otra cuenta se decía recién después de mandar el código. | Pedir el código de un correo que es de otra cuenta, activa o borrada, responde 409 `Users.User.AlreadyExists` en `errors.email` sin mandar nada; el chequeo al confirmar sigue para el correo que otra cuenta toma en el medio. El costo, escrito en identidad: con una sesión se puede averiguar si un correo tiene cuenta, 20 veces cada 15 minutos por dirección. | back `c2cbf3fd`, front `35b695b6` |
| O45 | «Banco de origen» quedaba sin leer cuando el comprobante mostraba el banco en el logo y la cuenta no daba el código de entidad. | La lectura puede dar el nombre del banco como lo muestra el documento; formularios lo busca en la tabla del BCRA y completa la opción sólo si coincide con una única entidad que la copia tiene. Con dudas, queda marcado. El código de la cuenta sigue mandando. | `13ceedc5` |
| O46 (y O37) | «Este comprobante ya se cargó» aunque el primero sólo se propuso. | Si el primero es de la misma persona, no pasó al formulario y su plazo no venció, WhatsApp contesta «Ya me lo mandaste el {fecha}: completá el formulario que te mandé.». | `1001b68f` |
| O49 | Una respuesta de Flow con archivo que Meta no entrega no la veía nadie. | Ver §2. | `510b8383`, `183ce3f6` |
| O50 | El idioma del pie de las páginas de acceso eran dos botones. | Va en la pieza única ([01](01-lo-que-termino-de-programarse.md) §2). | front `57528c38` |
| O51 | «Ingresar con» había pasado a la pieza. | Volvió al subrayado; lo contó el 18. | front `cf60c593` |
| O52 y O56 | La corrección que encontraba la respuesta cambiada caía en un aviso genérico; la actividad ponía la acción antes que la respuesta. | [01](01-lo-que-termino-de-programarse.md) §3. | front `8d9bc33d`, `2c15cdb1`, `c7923674` |
| O53 | El aviso de un 409 de revisión en Configuración llevaba «Código para reportar», como un error inesperado. | El aviso de un conflicto dice sólo qué hacer («Revisar valores actuales»); el código es para un 5xx. | front `f92341d7` |
| O54 | Elegir un formulario en «Enviar formulario» y cancelar dejaba «¿Salir con respuestas sin enviar?». | La guarda cuenta lo escrito, un intento sin resolver y el mensaje que editó el operador, no la elección. | front `6250062e` |
| O55 | Pedir por WhatsApp un formulario que es sólo de la app terminaba en «Formularios que faltan». | Si el nombre pedido coincide con un único formulario activo «sólo en la app» (las mismas palabras, o el nombre más corto, de dos palabras o más, dentro del otro, sin tildes ni artículos), no se registra el pedido y WhatsApp contesta «El formulario «…» ya existe y se completa desde la app:» con el enlace a su carga. Con ninguno o con varios, todo sigue igual. | `2672354b` |
| O57 | Una subcarpeta del filtro «Carpeta» se leía sin su carpeta. | El lector dice «Norte, en Repartos». | front `b14784b3` |
| O58 | `/sin-permiso` mostraba la falta de permiso fuera de la app. | SP1-B ([01](01-lo-que-termino-de-programarse.md) §6). | front `a3bcdcda` |
| O59 | El asistente de Inicio le dijo a la persona «La consulta devolvió una lista parcial de formularios.». | Cuando la lectura no entra entera, el modelo pregunta cuál quiere (por nombre o prefijo) o pide acotar, sin nombrar la consulta ni la lista parcial. | `cf8b7be7` |
| O60 (era O33) | Abrir desde el chat la importación que propuso la IA daba «Hiciste demasiadas solicitudes» en desarrollo. | La revisión sale en una microtarea, cuando los efectos de StrictMode ya se asentaron, y cerrar el diálogo sigue abortando lo que va en camino. | front `88b71ff5` |
| O2 | Al cerrar «Editar» de una fila, el nombre del lápiz se abría con el foco devuelto y tapaba la fila de arriba. | El nombre de un ícono se abre con el foco sólo si la última interacción fue el teclado. | front `b7a9f392` |

Y un arreglo sin número: el año del pie de la portada salía «© 2.026»; un año va como texto, igual que un identificador (front
`4fea3d84`).

## 2. O49: una respuesta que Meta no entrega

Lo que se averiguó (`510b8383`): una respuesta de Flow con un comprobante adjunto, contestada desde WhatsApp Web, nunca llegó.
En su lugar, Meta mandó a los segundos un estado `failed` con el wamid de ese mensaje **entrante** y el código 131052 («no pudo
bajar el archivo»). La documentación de Meta describe el código, pero no que llegue como estado de un mensaje entrante.

Lo que hace la app desde el 26/11, a pedido del usuario (`183ce3f6`; la regla en
[WhatsApp](../../features/whatsapp.md#recepción-de-respuestas-de-flow)):

- El webhook guarda ese estado como cualquier recibo, por wamid, aunque ningún envío tenga ese id. Es la única huella de la
  pérdida: los logs del panel de Meta duran menos.
- Un `failed` con 131052 de un mensaje que no es nuestro es una respuesta perdida. En la misma transacción del webhook, la app
  busca a la persona por su BSUID o su número y le **reenvía cada formulario abierto que pide un archivo**, del mismo remitente
  y con «No me llegó tu respuesta al formulario «X». Completalo de nuevo, por favor.». Sólo si ese remitente todavía puede
  mandarlo (el operador en Atención manual, el asistente en Asistente) y con la ventana de 24 horas abierta.
- No se reenvía uno mandado después de la pérdida, ni uno cuyo reenvío ya espera en la salida. Un reintento de Meta con el mismo
  estado no reenvía otra vez, porque su recibo ya existe.

## 3. Las dos decisiones del 25/11 (`a821a309`)

- **O48, la fecha de un Flow, queda como la dibuja WhatsApp** («22 Nov. 2026» en el formulario, «Nov 22 2026» en su resumen).
  Es la excepción escrita de la regla de fechas sólo con números
  ([backend](../../architecture/backend.md#presentación-general-y-precedencia-2026-09-30)): el formato lo pone Meta y no se
  elige. Lo que la app escribe de esa fecha (avisos, planilla, PDF) sigue con números. **Reemplaza lo que el 18 contó como
  decidido el 22/11** (pasar la fecha a un texto con números, sin el calendario): eso no se programó. Cambia la pregunta 83
  ([05](05-preguntas.md)).
- **O47, un importe con signo:** el que el comprobante muestra con signo («Importe -4.800,00», un débito) se lee y se guarda
  negativo, como lo muestra, y quien lo ve lo corrige a mano. Está en el estándar del comprobante, en
  [formularios](../../features/formularios.md).

## 4. Lo que quedó abierto

- **Sin plan, en el registro de la prueba:** O32 (una sugerencia que el servidor rechaza con un 409 claro se presenta como
  resultado incierto), O34 (el resumen repite el código y la opción cuando salen de la misma columna), O40 (con la Api caída,
  recargar deja «Cargando…» sin aviso), O41 (el aviso de una regla con una opción borrada no dice cuál) y O42 (Meta no deja
  mandar «1.500,5» con separador de miles en el Flow).
- **Para decidir:** los cupos de la IA por persona en pantalla (O35, RC5), en el
  [plan de reglas a Configuración](../2026-11-22-reglas-a-configuracion.md); un formulario que no se combina con otra acción
  (154), en el [plan de casos](../2026-10-21-casos-del-asistente.md); y qué contestar cuando el proveedor devuelve vacío un audio.
- **A mano o con cuentas reales:** todo lo de [verificación y producción](../verificacion-y-produccion.md), incluidas las
  homologaciones de ARCA y la corrida real del robot.

## 5. Lo que enseñó

- **Lo que dibuja WhatsApp no siempre conviene reemplazarlo** (O48). El 22/11 se decidió cambiar el calendario por un texto; el
  25/11 el usuario prefirió conservar el selector de Meta y escribir la excepción. Una regla de la app puede tener una
  excepción escrita cuando el formato lo fija el proveedor.
- **Una pérdida que avisa el proveedor tiene que reparar lo que se pueda** (O49). Guardar el recibo sirve para diagnosticar;
  reenviar lo que quedó abierto es lo que le devuelve la respuesta al negocio.
- **Un pedido explícito no es lo mismo que una oferta** (O55). «Sólo en la app» impide ofrecer el formulario por WhatsApp, pero
  si la persona lo pide por su nombre, la respuesta útil es dónde se carga, no «no existe».
- **El modelo no le habla a la persona de sus límites técnicos** (O59): una lectura cortada se resuelve preguntando o pidiendo
  acotar.
- **Un 409 de revisión no es un error inesperado** (O53): sin código para reportar, con la salida a la vista.

## 6. Para el multitenant

Leído en tu `main` del 26/11 (`b2334284`).

- **O28 coincide:** tu ADR 0043 ya responde 400 `Identity.LoginMethod.AlreadyUsed` al agregar un correo verificado ajeno, sin
  pendiente, código ni aviso (`docs/features/identidad.md`). No hace falta pregunta; el costo de averiguar si un correo tiene
  cuenta es el mismo y conviene escribirlo como acá.
- **O48:** tu Flow ya pide la fecha con `CalendarPicker` (`docs/plans/formularios/14-parte-archivos-tests-y-meta.md`), que es lo
  que quedó acá. La pregunta 83 del 18 cambia de recomendada ([05](05-preguntas.md)).
- **O49:** tu webhook no maneja el 131052. Es la pregunta 87, en tu E18 o tu WhatsApp.
- **O45, O46 y O47** son de tu E25 (documentos): el banco por el nombre que muestra, el repetido que todavía espera y el signo
  del importe. O47 es la pregunta 88; O45 y O46 anotalos en tu lista de errores ya pagados.
- **O55** completa tu decisión 258 («el asistente no lo ofrece»): es la pregunta 89.
- **O59** vale para tu asistente de la web y tu E25: una lectura con tope que no entra entera no se le cuenta a la persona.
- **O53 y O54** son reglas de tus pantallas: un 409 de revisión sin código para reportar, y la guarda de salir que cuenta lo
  escrito, no lo elegido.
