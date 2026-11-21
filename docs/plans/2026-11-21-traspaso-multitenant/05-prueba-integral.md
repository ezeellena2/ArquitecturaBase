# Traspaso 16 · 05 · La prueba integral, hasta el 21/11 (parcial)

La prueba integral es una sola prueba de punta a punta con el usuario, al final de la tanda de desarrollos: 181 pasos en
nueve secciones, armada el 17/11. Arrancó el 19/11 y sigue. Su registro, paso por paso y con las observaciones O1 a O30,
vive fuera de Git (`.artifacts/pruebas/`). Esto es su resumen al 21/11 a las 12:30. Lo que pase después va al traspaso 17.

## 1. Cómo se hace

- **La guía una sesión, y el usuario mira y decide.** Claude prepara los datos y recorre la app en su navegador integrado y,
  con la extensión de Chrome, en varios perfiles con cuentas de prueba distintas. Los códigos de ingreso se leen del asunto
  del correo, sin abrir los mensajes. Lo que llega por WhatsApp se lee de lo guardado, sin los valores.
- **Sólo se arregla lo que bloquea.** Lo demás va a la lista de después. Un cambio de diseño que pide el usuario se programa
  en un chat aparte y entra con su aprobación, reiniciando la app cuando lo autoriza.
- **Cada paso** dice qué hacer, qué tiene que pasar y de dónde sale (la sigla de su decisión o de su arreglo). El registro
  anota lo que pasó (✅, ◐ a medias, ✗ falla), lo que el paso resumía de más, los datos de prueba que quedan y cada
  observación con su número.
- **Mientras se prueba, ningún chat edita el front** (Vite recargaría la página) y nadie reinicia la app sin avisar.
- Lo que el navegador no puede hacer (el micrófono, el selector de archivos, el portapapeles del sistema, el lector de
  pantalla, un celular por USB) queda para hacer a mano.

## 2. Dónde va

| Sección | Hecho | Falta |
| --- | --- | --- |
| 1. Ingreso y cuenta (1 a 19) | Todo, la parte de WhatsApp el 21/11 | El 17, que es opcional |
| 2. Usuarios y roles (20 a 36) | Todo, con RO3-D | Recargar la página en el 36, a mano |
| 3. Formularios, de los catálogos a la carga (37 a 76) | Hasta el 75; el 59 y el 65, con las carpetas como menús | El 76, con un celular real; el 67 no aplica |
| 4. WhatsApp (77 a 99) | Empezó el 21/11: el 77, partes del 78 y del 90, y el 81 a medias | El 80 falló (O29); del 82 al 89 no hay un formulario de WhatsApp que sirva; lo demás |
| 5. Respuestas, planilla, aprobación, reglas y avisos (100 a 124) | Lo que no pide WhatsApp, con partes a medias en el 102, el 103, el 107 y el 110 | El 106, el aviso del 117, el 122 y el 123; el 124 es opcional |
| 6. IA y Casos (125 a 158) | Lo que no pide el micrófono ni WhatsApp | El 126, el 134, el 136 y el 137 (micrófono); del 138 al 144 y del 150 al 157 (WhatsApp) |
| 7. Documentos (159 a 166) | — | Toda: los documentos sólo entran por WhatsApp |
| 8. ARCA, sin emitir (167 a 170) | El 167, el 168 y el 170 | El 169, con la CUIT del usuario |
| 9. Configuración, salud y toda la app (171 a 181) | Lo que no pide WhatsApp | El conflicto de Información del negocio (172); lo de WhatsApp del 178, el 179 y el 181; el 180, con un secreto; los esqueletos (175) y algunas teclas (176), a mano |

## 3. Lo que cambió por la prueba

Decisiones del usuario que entraron, o quedaron tomadas, mientras se probaba:

| Qué | Cuándo y cómo quedó | Dónde |
| --- | --- | --- |
| Roles con áreas plegables (RO3-D) y un permiso que incluye a otro | 19/11, en la app | [01](01-roles-y-permisos.md) |
| Una sola pieza para elegir (DS1-A y DS2-A) | 19/11; programada en una copia, sin aplicar | [03](03-diseno-para-toda-la-app.md) §2 |
| Las acciones del asistente (ADR 0034) | 19/11, aceptado y sin programar | [04](04-ia-y-procesos-del-campo.md) §1 |
| Las filas de una tabla, sólo con íconos | 20/11, en la app | [03](03-diseno-para-toda-la-app.md) §1 |
| Las carpetas como menús (CM1 a CM6) | 20/11, en la app | [02](02-carpetas-como-menus.md) |

Y un arreglo que bloqueaba: la pantalla «Asistente» de WhatsApp (O25, `5648de3d`).

## 4. Las observaciones

Van numeradas en el orden en que aparecieron (no hay O7). «Para después» es la lista de después de la prueba: nada de eso
está decidido ni programado.

### Resueltas

- **O1.** Marcar «Administrar» no marcaba «Ver», y un rol con sólo «Administrar usuarios» no abría Usuarios. Lo resolvió «un
  permiso incluye a otro» ([01](01-roles-y-permisos.md)).
- **O25 (bloqueaba seis pasos).** La pantalla «Asistente» de WhatsApp decía «No pudimos cargar el asistente.»: la respuesta
  traía `knowledge.facts`, una propiedad calculada de un modelo que System.Text.Json serializaba y que el esquema estricto
  del front no conoce. Se arregló el 20/11 (`5648de3d`): se quitó la propiedad, que nadie usaba, y el test del GET exige
  las claves exactas del contrato.
- **O27 (bloqueaba WhatsApp).** Desde el 9/11 Meta no mandaba nada al webhook de desarrollo: la suscripción de la app de Meta
  apuntaba a la dirección de otro sistema que usa la misma app. El 21/11 el usuario la pasó al túnel para la prueba; al
  terminar lo de WhatsApp vuelve a la de antes. Lo que llegó en el medio no se reenvía.
- **O8 y O12.** El foco después de «Agregar subcarpeta» y un aviso de más después de borrar la carpeta elegida eran del panel
  de carpetas, que se fue con CM1 a CM6.

### Para después

| | Qué se vio | Área |
| --- | --- | --- |
| O2 | Después de cerrar un diálogo, el primer clic en otra fila o en el menú lateral no hace nada y el segundo sí (cuatro de cuatro veces). | Toda la app |
| O5 | Después de un código equivocado, las casillas conservan el viejo y escribir encima no lo reemplaza. A confirmar con el teclado. | Ingreso |
| O6 | El nombre de la app en los correos sale de `Email:AppName`, que por defecto dice «Acceso»: queda «Te dieron acceso a Acceso». | Correo |
| O9 | Sin referencia, un resultado incierto muestra «Referencia: ?». | Toda la app |
| O10 | El selector de roles y las casillas de permisos muestran encendido lo que quien edita no puede dar; el rechazo llega al guardar. | Roles |
| O11 | A 320 px, los nombres largos de área van en dos renglones y su (i) queda contra el borde (menor). | Roles |
| O13 | Un campo de texto de un catálogo admite 300 caracteres en el servidor; la pantalla acepta 1000 y el error no dice el tope. | Catálogos |
| O14 | La plantilla de un catálogo nombra los campos por su clave y la exportación por su nombre; la importación acepta los dos (menor). | Catálogos |
| O15 | La comparación de revisión de un catálogo no lista los cambios de las opciones, y «Usar la revisión actual» los descarta sin mostrarlos. | Catálogos |
| O16 | Guardar un filtro «según otra respuesta» sin elegir la respuesta da 400 y marca los dos combos; convendría frenarlo en la pantalla y marcar sólo el que falta. | Formularios |
| O17 | La vista previa del formulario no aplica las condiciones (menor). | Formularios |
| O18 | A 1024 px, la opción «Sólo en la app» queda cortada por el borde de su tarjeta (menor). | Formularios |
| O19 | El cálculo en vivo de la carga no lee los números con la región: «1.500» da un importe como si fuera 1,5 antes de que la validación lo marque, y el resumen de después de enviar usa la misma cuenta. La base, la planilla y el detalle están bien (media). | Carga |
| O20 | Al publicar otra versión del formulario sugerido, la sugerencia pasa a decir «Formulario de otra versión», sin nombrarlo ni decir qué hacer. | Formularios |
| O21 | El visor de fotos no muestra la fecha, porque las pantallas no se la pasan (menor). | Respuestas |
| O22 | El panel de revisar dice «1 de 2 por aprobar» y la pantalla completa dice «2 de 8», y sus flechas recorren todas las respuestas. | Respuestas |
| O23 | La pantalla de enviada decía «Por aprobar» y la respuesta quedó «Por resolver», porque la regla falló. | Carga y reglas |
| O24 | Después de importar en un catálogo que propuso la IA, la cabecera pasó a «Dado de baja» hasta recargar. | IA y catálogos |
| O26 | Al llegar al tope de caracteres, un campo deja de aceptar letras sin avisar, sin contador ni mensaje (menor). | Toda la app |
| O29 | En el Flow, la ayuda de un número pide la coma para los decimales (r15), pero Meta la rechaza («no coincide con el formato requerido») en el celular, en WhatsApp Web y en la vista oficial, aunque el patrón que se compila la admite; con punto pasa. En español, la ayuda pide justo lo que Meta no deja (importante). | WhatsApp |
| O30 | Cuando un número es ambiguo, el Flow vuelve con lo cargado y el aviso, pero la pregunta no queda marcada y el mensaje no la nombra (menor). | WhatsApp |

### Para decidir

- **O4.** «Editar usuario» no deja corregir el correo sin verificar que cargó un administrador: si se escribió mal, hay que
  borrar la cuenta y crearla otra vez. ¿Es lo que se buscó con N11?
- **O28.** El usuario pidió que «correo ya en uso» salga al escribir el correo, antes de los códigos. Hoy sale después de un
  código correcto, a propósito: la regla de identidad es que agregar un correo o vincular un número no revele si el destino
  es de otra cuenta. Se decide después de la prueba.

Además, el registro tiene una veintena de menores sin número (textos, decimales que se ven distintos según la pantalla, un
«código para reportar» en un conflicto esperado). Van con su sección a la lista de después.

## 5. Lo que enseñó

- **El contrato es lo que se declara** (O25). Una propiedad calculada de un modelo de respuesta también se serializa. El test
  de cada GET exige las claves exactas del contrato, y el esquema del front es estricto a propósito: así la falla aparece en
  el test y no en la pantalla.
- **Si no llega un webhook, primero la suscripción** (O27). Con una app de Meta compartida, la URL de la suscripción es una
  sola: se mira con las herramientas de desarrollador de Meta antes de buscar en el túnel o en la Api.
- **Lo que pide una ayuda se prueba en el cliente real** (O29). Que el patrón propio admita algo no dice que Meta lo deje
  escribir.
- **La cuenta en vivo usa la misma lectura que la validación** (O19): mientras la validación marque un número como ambiguo,
  el cálculo no muestra resultado.
- **Lo que el servidor rechaza, la pantalla lo frena antes** (O10, O13, O16): apagado con su motivo (MA1-A), o con el error
  en el campo que falta y sólo en ése.
- **Los límites de pedidos por minuto se notan en desarrollo,** donde cada lectura sale dos veces (StrictMode de React):
  «Hiciste demasiadas solicitudes» al abrir varios editores en pestañas, o al pedir dos veces la vista previa de una
  importación. No es de uso normal.

## 6. Lo que falta, para el traspaso 17

- **WhatsApp:** el resto de la sección 4, que necesita un formulario de WhatsApp con «elegir primero», cálculos, condiciones
  y ubicación, y los pasos de WhatsApp de las secciones 5, 6, 7 y 9. Al terminar, la suscripción de Meta vuelve a su
  dirección.
- **A mano:** el micrófono, un celular por USB, el lector de pantalla, el Ctrl+V del sistema, algunas recargas y teclas.
- **Con el usuario:** el pedido del certificado de ARCA con su CUIT (169) y el correo con un secreto mal puesto (180).
- **El cierre:** una corrida completa en verde con la app apagada, la de las carpetas, aplicar la pieza única al front, la
  auditoría documental y el traspaso 17, con lo que se decida de O4, O28 y O29 y lo que se arregle de la lista de después.

## 7. Para el multitenant

- **Salvo lo que cuentan el 01 al 04, nada de esto es una decisión.** Anotá las observaciones como riesgos a mirar en tus
  specs, sin armar etapas.
- **O29 deja en suspenso la pregunta 14 del 15** (la coma o el punto decimal en WhatsApp) y lo que el T13/03 §4 y §5 contaba
  de la coma de r15. Hasta que acá se decida (traspaso 17), no adoptes la coma en el Flow.
- **O28 ya está resuelto allá:** tu ADR 0043 dice que, en «Mi cuenta», agregar un correo que ya es de otra cuenta lo dice y
  no manda código. Es lo que pidió el usuario acá. No cambies nada; si acá se decide distinto, lo trae el 17.
- **O4:** allá tampoco hay forma de que un administrador corrija el correo de otra cuenta. Si acá se decide, lo trae el 17.
- **O6:** tu `Email:AppName` por defecto dice «Arquitectura Base». Revisá cómo se leen el asunto y la primera línea de cada
  correo con ese nombre.
- **O19:** tu E20 deja el cálculo en vivo para después de validar al enviar. Cuando lo armes, que lea los números con tu
  `parseDecimal` y la cultura de la cuenta.
- **O25:** tus DTO de respuesta y los esquemas estrictos de tu front tienen el mismo riesgo: sumá el test de las claves
  exactas en cada GET nuevo.
- **O27:** si tu app de Meta sirve a varios ambientes, la URL de la suscripción decide a cuál le llega todo.
- **Tu propia prueba integral** puede tomar esta forma: un recorrido único al final, sólo se arregla lo que bloquea y un
  registro con observaciones numeradas.
