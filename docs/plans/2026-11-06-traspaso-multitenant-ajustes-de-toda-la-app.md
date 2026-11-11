# Ajustes de toda la app y de la plantilla: traspaso 9 al multitenant (2026-11-06)

Autor: el chat guía de ArquitecturaBase. Destino: ArquitecturaBaseMutitenant, para las reglas de pantallas de su front
(`docs/rules/`, `docs/architecture/tema.md` y los tableros «Criterio»), sus reglas del backend (`docs/rules/` y los tests de
arquitectura) y sus etapas **E16 · Datos de las personas** y **E21 · Reglas y aprobación**.

Del 2026-11-03 al 2026-11-06, además de las etapas grandes (la carga desde la app en el [traspaso 7](2026-11-06-traspaso-multitenant-carga-etapas-2-a-5.md),
la IA que arma formularios en el [traspaso 8](2026-11-06-traspaso-multitenant-ia-arma-formularios.md) y ARCA en el
[traspaso 6](2026-11-06-traspaso-multitenant-arca-conexion.md)), se terminaron desarrollos más chicos: casi todos salieron de
la prueba completa y valen para toda la app. Este traspaso cuenta cada uno, con lo que tiene que evaluar el multitenant, y
cierra con el estado de lo que sigue en curso o sólo planeado (§9), que no es para incorporar todavía.

**Como los anteriores, no es código para copiar** (decisión 18 de su hoja de ruta) ni hace falta para dibujar (decisión
108). **No depende de los traspasos 4 a 8.**

## 1. En pocas palabras

- **El idioma, sólo cuando es el otro (NI1-A, NI2-C y NI3-B, §2):** un texto nombra su idioma sólo cuando no es el del menú, y
  publicar avisa las opciones por persona sólo cuando no entran o nadie ve ninguna.
- **Reintentar el aviso a una persona de un rol (RT1-A, §3):** «Reintentar» en el renglón de la persona, sólo con la falla
  confirmada.
- **Mi perfil (PF1-A, §4):** tres zonas como Configuración y el estado del chat de WhatsApp, aparte de «Verificado».
- **El menú carga sin saltos (§5):** lo que carga no mueve lo que ya se ve, y un bloque de carga se distingue de su
  superficie.
- **La plantilla (§6):** cuatro controles de arquitectura nuevos, la guía de quitar formularios probada, los logs de
  desarrollo con el depurador y una advertencia sobre el agente de IA de Meta.
- **Lo que decide el usuario allá** (§7): el aviso de opciones por persona, dónde va «Reintentar» y dónde se ve el estado del
  chat de WhatsApp.
- **Parte de los commits no está en GitHub.** Desde esta PC se leen en `C:\Users\ezequ\source\repos\ArquitecturaBase` y
  `C:\Users\ezequ\source\repos\ArquitecturaBaseFront` (§10).

## 2. El idioma, sólo cuando es el otro (NI1-A, NI2-C y NI3-B, 2026-11-03)

Publicando un formulario escrito sólo en español, con el menú en español, el usuario vio «· Español» en la etiqueta de una
pregunta y en cada problema de «Publicar en WhatsApp», y preguntó por qué, si eso sólo aplica cuando se cambia de idioma. Se
buscaron en toda la app los textos que nombran el idioma del menú, y eligió:

- **NI1-A, en toda la app:** un texto nombra su idioma sólo cuando no es el del menú. Cambiaron la etiqueta de la pregunta, los
  problemas al publicar y los de una publicación no aceptada, la revisión de los cambios de otra persona, el largo de las
  opciones en «En WhatsApp», la vista previa del PDF, la vista previa del formulario, los PDF de una respuesta y «Le llega en
  …» al mandar un formulario desde el chat. Descartó NI1-B, que cambiaba sólo lo de editar.
- **NI2-C, el renglón «Idiomas» al publicar:** un formulario escrito en el idioma del menú, sin ningún texto en el otro, se
  publica sin hablar de idiomas: sin el renglón «Idiomas» y con «Publicamos la versión 3.». El renglón vuelve con una
  traducción empezada, con los dos idiomas o con el menú en el otro idioma. Descartó «Español» solo (NI2-B) y dejarlo como
  estaba (NI2-A).
- **NI3-B, el aviso de opciones por persona:** el servidor avisa sólo cuando el máximo pasa el límite de WhatsApp (200 en una
  elección, 20 en varias) o da cero, como ya hacía el editor de la pregunta. Con cero dice «Con estos filtros nadie ve
  ninguna opción.». Descartó NI3-A, que avisaba siempre que la pregunta filtra por persona.

La regla quedó en los principios del diseño del front, para las pantallas nuevas. Contratos: ninguna ruta, permiso ni
migración; cambia lo que devuelve el chequeo de publicación (NI3-B). Commits: backend `65a7f13f`, `918e80b4` y `d558c421`;
front `e5b704e7`, `f0a745c9`, `c6792b63`, `f3a3caf4`, `eecbafa5` y `3a87b109`, con el contrato en `b5abebde`.

## 3. Reintentar el aviso a una persona de un rol (RT1-A, 2026-11-03)

En la prueba completa, el aviso de una regla a un rol no le llegó a la única persona del rol: su WhatsApp no estaba
vinculado. El usuario lo arregló y buscó «Reintentar» en la actividad, pero no estaba: el aviso a un rol terminaba «Hecho»
cuando todas las personas lo recibían o fallaban. Eligió **RT1-A** y descartó un botón «Reintentar a N personas» en el renglón
del aviso (RT1-B) y dejarlo como estaba (RT0).

- **En el renglón de la persona:** «Reintentar» va en Acciones, como «Resolver envío», con una (i) que dice que manda el aviso
  sólo a esa persona y qué revisar antes (el WhatsApp vinculado y la ventana de 24 horas).
- **Sólo con la falla confirmada:** WhatsApp lo rechazó o alguien resolvió «No salió». Un envío sin confirmar sigue con
  «Resolver envío», y uno que salió no tiene botón. Sin permiso para administrar reglas se ve la falla, sin el botón.
- **Reabre el aviso sólo para esa persona:** el aviso vuelve a «En curso» y al terminar, a «Hecho». No se repite a quienes lo
  recibieron ni vuelve a correr la acción siguiente de la regla.
- **Si cambió:** el pedido lleva la revisión del aviso, y otra persona que ya lo reintentó o lo resolvió da 409.

Cambió la regla «sin reintento parcial del aviso a un rol»; no hizo falta un ADR, porque sólo se reintenta una falla
confirmada. Contratos: `POST /api/forms/records/{recordId}/actions/{runId}/recipients/{recipientUserId}/retry`, con
`formrules.manage` y la revisión esperada, y `canRetry` en cada destinatario de la actividad. Migración:
`AddFormActionRecipientRetry`. Commits: backend `11c56613`, `c203d072`, `52f30373` y `4c662b39`; front `d3de7dff`, `d6a27818` y
`48a5ef9a`, con el contrato en `bf754a26`.

## 4. Mi perfil y el chat de WhatsApp (PF1-A, 2026-11-03)

En la prueba completa, la cuenta del usuario tenía el número «Verificado», pero el chat de WhatsApp sin vincular, y los avisos
de las reglas no le llegaban: el perfil no mostraba esa diferencia. El número se había confirmado antes de que existiera su
chat; el primer mensaje creó el chat suelto, y un mensaje nunca vincula (esa regla de identidad no cambió). Eligió **PF1-A**:
tres zonas, una debajo de la otra, como Configuración. Descartó dos columnas con el chat dentro de la fila de WhatsApp (PF1-B)
y una lectura que se cambia en su renglón (PF1-C). También aprobó «Desvincular» en rojo en «Editar usuario».

- **Datos del perfil:** nombre, idioma y zona horaria en un renglón, con «Guardar» apagado sin cambios.
- **Medios de ingreso:** correo, WhatsApp, Google y el último ingreso, en renglones de columnas fijas: el dato, el valor, su
  estado y su acción. Lo que cargó un administrador dice «Sin verificar», con el porqué en el tooltip.
- **Chat de WhatsApp,** con el módulo prendido y el número verificado: el vínculo («Vinculado», «Sin vincular» con
  «Vincular», o «Sin chat todavía» con «Abrir el chat») y los avisos («Llegan hasta el …», «Pausados» o «No llegan»). Vincular
  confirma el mismo número con un código, como antes.
- **«Editar usuario»** usa los mismos renglones.

Las reglas generales, para toda pantalla:
- una pantalla de ajustes se arma como Configuración: zonas a todo el ancho, una debajo de la otra, y «Guardar» en la banda,
  apagado sin cambios;
- lo que se lee de una cuenta va en renglones de columnas fijas, la misma pieza en el perfil y en la administración;
- **un dato que sirve para dos cosas dice el estado de cada una:** «Verificado» es entrar con el número, y el chat y los
  avisos van aparte, con la acción que los arregla;
- quitar va en rojo antes de confirmar, también en los medios de ingreso, y las altas dicen «Agregar …».

Contratos: `GET /api/me/whatsapp`, del módulo de WhatsApp, devuelve el estado del chat del número confirmado (vinculado, suelto
o sin chat) y hasta cuándo pueden salir los avisos, 24 horas después del último mensaje de la persona, con las mismas
condiciones que un aviso. Con WhatsApp apagado responde 405, porque la ruta sigue para confirmar el número. Sin permisos ni
migraciones. Commits: backend `cab46fc5`, `6cfccb40` y `471542ce`; front `8c3bec01`, `1e2cc683`, `3a805e30`, `bd16deb5`,
`ecf989c8`, `acbe6a85`, `e115019d` y `fcf3e8f2`.

## 5. El menú carga sin saltos (2026-11-05)

El usuario mandó un video: al recargar, quedaba un espacio debajo de «Formularios» en el panel del menú y después se
acomodaba. Ese día se arregló eso y todo lo del mismo tipo que apareció en el caparazón:
- **los bloques de carga miden lo que reemplazan:** el número de «Formularios» carga dentro de su renglón, y los bloques de la
  barra expandida y del panel miden el renglón;
- **en el panel claro se ven:** el panel redefine su color de acento como un tinte translúcido del color del texto, así el
  bloque se distingue sobre la superficie y sobre el renglón señalado;
- **la barra contraída** usa bloques de 30 px, y los botones de los paneles quedan centrados, como el de Administración;
- **el avatar de la barra superior** reserva el lugar de su flecha y lleva el mismo tinte. El tinte va sólo en ese bloque y no
  en toda la barra, porque el encabezado público comparte su clase.

Quedaron dos reglas para toda pantalla:
- **lo que carga no mueve lo que ya se ve:** un bloque de carga ocupa el lugar y la medida de lo que reemplaza, y un dato que
  se suma a algo visible carga en su mismo renglón, sin agregar uno debajo que desaparezca al llegar el dato;
- **un bloque de carga se distingue de la superficie donde está,** con un tinte del texto sobre lo que tenga detrás.

Una escena visual retiene el perfil y el número mientras carga, con la barra expandida y contraída, y compara la geometría,
el centrado y el contraste. Sin cambios en el backend. Commits: front `934c783c`, `45ec8151` y `5e5111a3`.

## 6. La plantilla

### Cuatro controles de arquitectura nuevos (2026-11-03)

Una revisión de premisas encontró tres desvíos, y el usuario pidió arreglarlos por el camino mínimo. Con eso y una tanda de
tests de las reglas de convención quedaron:
1. **Cada texto de error tiene su código y cada código, sus dos textos.** `ErrorCodeTests` lee del IL los códigos de cada clase
   de errores de Domain y exige que cada clave de `Errors.resx` tenga un código declarado: al quitar un módulo, sus textos no
   quedan huérfanos sin aviso. También exige texto en los dos idiomas para cada código, incluidos los que se arman dentro de
   una fábrica.
2. **Lo que devuelve un lector:** `Row`, `Response` o `Counts` (`ReaderOutputNamingTests`). Tres tipos se renombraron, y las
   excepciones de cada módulo van con su motivo en el aporte del módulo.
3. **Un solo `ExecuteInTransactionAsync` por método público:** `TransactionBoundaryTests` cuenta, leyendo el IL, los límites
   del recorrido de cada método público (los métodos propios que llama, sus lambdas y sus máquinas async), con un caso de
   control.
4. **Un contrato con datos privados escribe su `ToString()`:** `ControllerInputContractTests` lo exige a todo contrato del
   núcleo y de los módulos con una propiedad que termina en Email, Phone, Code, Token, ApiKey o DisplayName, o que se llama
   Number. Los modelos de Application con datos privados también lo ocultan, y la arquitectura y el ADR 0002 lo dicen.

Commits: `bb8b5873`, `ba7f9d88`, `bfdee75b`, `a67d31e7`, `692647cc`, `7b006963` y `a5c3b084`.

### La guía de quitar formularios, probada (2026-11-03 y 2026-11-04)

`docs/guides/quitar-formularios.md` es nueva, como las de WhatsApp, IA y ARCA. Se siguió en copias del backend y del front, y
después con la integración con Docker. Lo que encontró:
- la vuelta atrás de la migración de retiro necesita a mano las dos claves diferibles de las opciones;
- 44 composiciones de tests de WhatsApp, el inventario de rutas de WhatsApp y los tests de migraciones del núcleo daban por
  instalado el módulo. Se arregló en la plantilla: se omiten solos sin el módulo, y los controles de formularios se van con él;
- en el front faltaban en la guía los tests, el enrutador de escenas y las capturas HTTP, y sus patrones borraban escenas de
  WhatsApp.

Commits: `7f4e91ec`, `18730d4c`, `ed0f2d06`, `a7ae921b`, `12ee7aab` y `20f878ed`.

### Los logs de desarrollo con el depurador (2026-11-06)

Con la Api levantada desde Visual Studio con el depurador, el proveedor de log Debug manda cada línea a la ventana Salida, y eso
frena a todo el proceso. Medido con un GET anónimo y diez pedidos en paralelo: unos 11 pedidos por segundo con el log
original y unos 690 apagando sólo ese proveedor. El editor de formularios tardaba unos 4 segundos en abrir algunas secciones.
`Logging:Debug:LogLevel:Default` quedó en Warning en el `appsettings.Development.json` de la Api, explicado en el README junto
a F5; la consola y la telemetría no cambian. Commit: `4ff931b0`.

### Una advertencia: el agente de IA de Meta (2026-11-05)

WhatsApp Manager ofrece un agente de IA de Meta para responder solo. Con ese agente como primer respondedor, lo que escribe el
cliente llega por el campo `standby` del webhook, no por `messages`, y la app lee sólo `messages`: no vería esas charlas, ni en
la atención ni en el asistente. Además, un mensaje que manda la app le saca la conversación al agente hasta que la app la
libere, cosa que no hace. Se le recomendó al usuario no prenderlo en el número de la app; no decidió nada ni se cambió código.
Que convivan sería otro desarrollo: leer `standby` y los traspasos de conversación, y tomarla y liberarla.

## 7. Adaptación al multitenant

Leído en el multitenant en `c339b58d` (front `c5172e65`), con la hoja de ruta hasta la decisión 108.

| Tema | Su hoja de ruta y sus reglas hoy | En ArquitecturaBase | Qué evaluar |
| --- | --- | --- | --- |
| **El idioma** (NI1-A y NI2-C) | El contenido no tiene idioma (35): sin selector, pares, vista previa en dos idiomas ni avisos de traducción. Los textos del sistema van en el idioma de la cuenta de quien recibe (42). El tablero `Criterio-Decisiones`, en la fila «WhatsApp · mandar formulario», dibuja «Le llega en …». | §2. | NI2-C no aplica. De NI1-A queda la regla para los textos del sistema que nombran un idioma, como «Le llega en …»: sólo cuando no es el del menú. Sumarla a su regla de textos y a ese tablero. |
| **El aviso de opciones por persona** (NI3-B) | Decisión 37 y E16 «Al publicar»: avisa el máximo por combinación contra 200 o 20 (`MaxOptionsPerRecipient`), y el ejemplo de `FRM-Publicar-16` es «Como mucho 18 opciones por persona», aunque entre. Que nadie vea ninguna opción no se avisa al publicar: frena al mandar, si la pregunta es obligatoria (`NoOptions`). | Avisa sólo cuando pasa el límite o da cero. El usuario descartó avisar siempre. | Choca con su E16 y con `FRM-Publicar-16`: **lo decide el usuario.** Con NI3-B, el aviso de cero al publicar es nuevo allá. |
| **Reintentar un aviso a un rol** (RT1-A) | E21 «Un solo motor de acciones»: «Reintentar» retoma sólo lo que no salió, por ejecución (`POST …/executions/{executionId}/retry`, con `formrules.manage`); `Forms.Execution.NotRetryable` para lo que no quedó en «No se pudo». Las fallas por destinatario se ven en Actividad, y la campana avisa a quien administra las reglas. | Por persona, en su renglón, sólo con la falla confirmada, con la revisión del aviso y 409. Acá el usuario descartó un botón para todos (RT1-B), lo más parecido a lo suyo. | Las dos formas reintentan sólo lo que falló; cambia dónde va el botón y si se puede reintentar a una sola persona después de arreglar lo suyo. **Lo decide el usuario.** |
| **Mi perfil** (PF1-A) | «Mi cuenta» (`/cuenta`): «Tus datos» y los métodos de ingreso (correos, WhatsApp y Google, sin contraseña), con «Borrar mi cuenta». El vínculo de una conversación con la cuenta lo ofrece el bot de cada organización y se lee en Perfil personal › «Mensajes» (ficha de WhatsApp, «Vínculo con la cuenta y «Mensajes»»), sin el estado del chat del número. Un aviso de una regla puede fallar por `NoConversation` o `WindowClosed` (E21). «Editar usuario» muestra roles, empresas y estado, sin los métodos de ingreso. | §4. | Lo que pasó acá (un número verificado que no recibe avisos porque su chat no está vinculado o su ventana venció) puede pasar allá con esas dos fallas. **Lo decide el usuario:** mostrar el estado del chat y hasta cuándo llegan los avisos en «Mi cuenta» o en «Mensajes». Las reglas generales del §4 van a sus reglas de pantallas. |
| **Estados de carga** (el menú sin saltos) | No hay regla sobre bloques de carga. El componente de esqueleto de su front no se usa, y `Criterio-Tablas` dibuja «Cargando» con barras. | §5. | Sumar las dos reglas a `docs/rules/pantallas-y-ui.md` de su front, con una escena visual que compare la geometría mientras carga. Los tokens de acá no existen allá: van con los suyos. |
| **Controles de arquitectura** | `ErrorCodeTests` controla que cada código tenga su clave, no la vuelta. `TransactionBoundaryTests` controla quién recibe el `IUnitOfWork`, pero no cuenta los límites por método. `SensitiveToStringLoggingTests` cubre los `*HttpRequest` de `Api.Contracts`. La regla de `Row` no tiene test. | §6. | Sumar los que faltan: las claves huérfanas de `Errors.resx`, un límite por método público, `ToString` en todo contrato con datos privados (también en los módulos y con ApiKey, DisplayName y Number) y lo que devuelven los lectores. Son técnicos: los decide quien programa. |
| **Quitar formularios** | `quitar-formularios.md` queda para su E14 (fila «Retiro»), y el retiro se prueba una vez, en la etapa del módulo. | La guía probada (§6). | Al escribirla, sumar lo que encontró la prueba de acá: la vuelta atrás de la migración de retiro y los tests de otros módulos, de rutas y de migraciones que dan por instalado el módulo. |
| **Logs en desarrollo** | El `appsettings.Development.json` de la Api no tiene sección `Logging`; la base va en Information. | Debug en Warning. | Si allá se levanta la Api con el depurador de Visual Studio, aplicar lo mismo, después de medir con un GET simple y diez pedidos en paralelo. |
| **El agente de IA de Meta** | Sus webhooks de WhatsApp leen los mensajes como acá. | §6. | Vale para el número de cada organización: sumarlo a su guía de operación de WhatsApp. |

**Lo que tiene que decidir el usuario allá:**
- el aviso de opciones por persona sólo cuando no entran o da cero (NI3-B), contra su E16;
- reintentar por persona (RT1-A) o por ejecución, como dice su E21;
- dónde se ve el estado del chat de WhatsApp y hasta cuándo llegan los avisos: en «Mi cuenta» (PF1-A) o en «Mensajes».

## 8. Lo que falta acá

- **La prueba manual del idioma**, con la Api reiniciada para el aviso de NI3-B.
- **La prueba manual de RT1-A:** reintentar desde su renglón un aviso que falló.
- **La prueba manual de Mi perfil:** con la Api reiniciada por la ruta nueva, vincular desde el perfil y escribirle al número
  del negocio para que vuelvan los avisos.
- **La prueba manual del menú:** recargar con la barra expandida y contraída.

## 9. En curso o sólo planeado

Esta sección es para que el multitenant sepa cómo vienen. No trae prompt de implementación: cada desarrollo tendrá su
traspaso cuando cierre.

| Desarrollo | Estado al 2026-11-06 | Dónde mirar |
| --- | --- | --- |
| **Renglones en los formularios** | Decididos RG1-A a RG8-A (traspaso 5) y **RG9-A** (2026-11-04): «Renglones» es un tipo más en «Tipo de respuesta», apagado con su motivo después de «·» («Renglones · Ya hay un bloque de renglones»), y en una pregunta del bloque la foto y el documento dicen «· No se carga varias veces». Desde el 2026-11-06 (**RG10-D**), el bloque se agrega con «Agregar bloque», al lado de «Agregar pregunta», como una pregunta más, y «Tipo de respuesta» ya no ofrece «Renglones». Desde el 2026-11-07 (**RG12-A**), los textos que ve el usuario dicen «ítem» en lugar de «renglón», en toda la app: «Bloque · de 1 a 10 ítems · 3 preguntas» (con mínimo y máximo iguales, «1 ítem»; con mínimo 0, «hasta 10 ítems»), «Preguntas de cada ítem», «Pasan al bloque», en WhatsApp «Artículos · ítem 2» y «¿Agregás otro ítem?», y en las etapas que siguen «Agregar ítem», «Ítem 2» y «Algún ítem con…»; la lista completa, con el inglés («item»), está en el diseño del front. De la etapa 1 (editor y publicación) están ocho de sus diez tareas: el bloque en el borrador y su contrato, el chequeo al publicar con «Pasan a renglones», el Flow con una pantalla por renglón, el webhook y el editor con su vista previa. Faltan la verificación con Meta, en curso desde el 2026-11-06 (Meta aceptó el primer formulario con bloque), programar RG10-D y RG12-A, y el cierre. Por decidir: un mínimo mayor que 10 (WhatsApp carga hasta 10 renglones) y una lista dependiente que filtre por una pregunta del bloque. Las etapas 2 a 4 (carga, lecturas y reglas), sin programar. | [traspaso 12](2026-11-11-traspaso-multitenant-renglones.md), [etapa 1](../features/formularios.md#el-bloque-de-renglones-al-armar-y-publicar--rg1-a-rg3-a-y-rg6-a-2026-11-04) y el diseño del front, `docs/design/renglones-en-formularios.md` |
| **Carpetas para formularios y catálogos** | CP1-A a CP3-A (traspaso 5) y **CP5-A** (2026-11-04): cada carpeta elige en su «…» si va al menú lateral. Puede quedar fuera, que es lo que viene por defecto; ser un menú propio, con un ícono entre doce; o ir adentro de «Gestión de formularios» o de «Administración». Muestra dos niveles y sólo formularios activos, que abren su planilla (o la carga, para quien sólo carga). Descartó una pantalla «Menú» en Configuración (CP5-B) y las carpetas del primer nivel como menús (CP5-C). Sin programar: cuatro cortes, con el turno a acordar con renglones. | [plan](2026-11-03-carpetas-de-formularios-y-catalogos.md), decisión 7 |
| **La IA arma formularios, etapas 2 y 3** | Planillas y documentos, sin programar; la 3 espera la lectura de documentos del ADR 0022. Falta decidir qué hace WhatsApp ante un pedido ambiguo. | traspaso 8 |
| **ARCA, etapas 2 a 6** | La etapa 1 está en el traspaso 6; las demás, en la hoja de ruta, sin diseño en detalle. | traspaso 6 y la [hoja de ruta de ARCA](2026-10-31-arca-formularios-estandar.md) |
| **La carga desde la app, después del plan** | Las listas dependientes por WhatsApp, con el intercambio cifrado con Meta, y cargar en nombre de otra persona, si aparece el caso. | traspaso 7 |
| **Documentos por WhatsApp y casos del asistente** | Decididos y sin empezar, sin cambios desde el anexo F del traspaso 2. | [traspaso 2](2026-10-23-traspaso-multitenant/anexo-f-planes-pendientes.md) |
| **Sin decidir** | Traer datos de bases de clientes para catálogos y tableros (propuesta del 2026-11-02), copiar formularios, la retención de las respuestas y las integraciones de las partes 2 y 3. | [planes pendientes](README.md) |

## 10. Dónde está cada cosa en ArquitecturaBase

- **Reglas implementadas:** [formularios](../features/formularios.md), en el idioma y el aviso de opciones (NI1-A a NI3-B) y en
  el motor de acciones (RT1-A); [WhatsApp](../features/whatsapp.md), en «El perfil dice cómo está el chat».
- **Diseño elegido,** en `ArquitecturaBaseFront/docs/design/`:
  - `visual-baseline.md`: los principios («El idioma, sólo cuando es el otro», «Un dato que sirve para dos cosas dice el
    estado de cada una», «Lo que carga no mueve lo que ya se ve» y «Un bloque de carga se distingue de la superficie donde
    está»), «El idioma, sólo cuando es el otro — 2026-11-03» y «Mi perfil y el chat de WhatsApp — 2026-11-03»;
  - `formularios-whatsapp.md`: NI1-A, NI2-C y NI3-B, y RT1-A.
- **La plantilla:** [arquitectura](../architecture/backend.md), [validar un cambio](../guides/validar-un-cambio.md),
  [quitar formularios](../guides/quitar-formularios.md) y el README, junto a F5.
- **Tableros:** el lienzo [Formularios desde la app](https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M), filas NI, RT y «Mi
  perfil» (PF0 a PF3). El menú no tuvo tablero: se arregló sobre el video del usuario.
- **Commits:** en cada sección.
- **Código de referencia:**
  - backend: `FormPublicationGuard` (NI3-B); `BusinessFormActionRun.RequestRecipientRetry` y `FormWorkflowService` (RT1-A);
    `ProfileWhatsAppChatService` y `MeWhatsAppController` (PF1-A); `ErrorCodeTests`, `ReaderOutputNamingTests`,
    `TransactionBoundaryTests` y `ControllerInputContractTests` (la plantilla);
  - front: `PublicationCheckDialog` (NI1-A y NI2-C); el renglón de la persona en la actividad de una respuesta (RT1-A);
    `ProfilePage`, `LoginMethodsCard`, `WhatsAppChatCard`, `AccountRows` y `UserEditDialog` (PF1-A); el menú lateral y
    `UserMenu` (el menú sin saltos).

## 11. Prompt para el chat guía del multitenant

```text
Leé el traspaso 9 de ArquitecturaBase, en
C:\Users\ezequ\source\repos\ArquitecturaBase\docs\plans\2026-11-06-traspaso-multitenant-ajustes-de-toda-la-app.md.
Cuenta lo que allá se decidió y se programó del 3/11 al 6/11 fuera de las etapas grandes: cuándo se nombra el idioma de
un texto y cuándo se avisan las opciones por persona, reintentar el aviso a una persona de un rol, Mi perfil con el
estado del chat de WhatsApp, el menú que carga sin saltos, cuatro controles de arquitectura, la guía de quitar
formularios probada, los logs de desarrollo y una advertencia sobre el agente de IA de Meta. Al final dice cómo vienen
los desarrollos en curso o sólo planeados. No depende de los traspasos 4 a 8. Parte de los commits no está en GitHub:
leelos en C:\Users\ezequ\source\repos\ArquitecturaBase y ...\ArquitecturaBaseFront.

1. Evaluá del §2 al §6 contra tus reglas y tus etapas, con la tabla del §7. No es código para copiar (tu decisión 18)
   ni hace falta para dibujar (tu decisión 108).
2. Las reglas de pantallas del §4 y el §5 valen para toda la app: proponé dónde fijarlas (docs/rules del front, tu
   decisión 43 o los tableros «Criterio») y revisá los textos de diseño que hoy piden otra cosa.
3. Los controles de arquitectura, la guía de quitar formularios, los logs y la advertencia de Meta del §6 son técnicos:
   anotalos en tus reglas, en tu E14 o en tu guía de operación de WhatsApp, sin programarlos.
4. Los tres puntos del final del §7 los decide el usuario: preguntáselos de a una pregunta, con las opciones y la
   recomendada marcada. Lo mismo para cualquier otra adaptación que tenga que decidir él.
5. El §9 es sólo para que sepas cómo vienen los desarrollos en curso: no armes etapas ni specs con eso. Cada uno tendrá
   su traspaso cuando cierre.
6. Decile al usuario qué tableros cambian. Como ejemplo para adaptar, tiene las filas NI, RT y «Mi perfil» en el lienzo
   de ArquitecturaBase (https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M).
7. No programes nada. Commits chicos en español, en main, sin push.

Al terminar, contame qué cambió en tus specs y tus reglas, qué decisiones quedaron pendientes y qué tableros hay que
ajustar.
```

Este traspaso se retira cuando el multitenant lo haya incorporado a sus reglas y etapas.
