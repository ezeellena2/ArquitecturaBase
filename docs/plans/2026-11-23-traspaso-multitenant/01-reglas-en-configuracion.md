# Traspaso 18 · 01 · Ninguna regla de negocio fija en el código

El 21/11, durante la prueba integral, el usuario pidió: «toda configuración o regla tenemos que tratar de colocarla en la zona
de configuraciones del administrador». El primer caso fue el tope de ofrecimientos del asistente (TO1-A); el 22/11 se
inventariaron las demás reglas fijas y el usuario decidió dónde va cada una (RC1-A a RC7-B). El inventario y las decisiones
están en `docs/plans/2026-11-22-reglas-a-configuracion.md`.

## 1. El criterio y la forma

- **Pasa a Configuración** una regla de negocio que un administrador querría ajustar en su instalación: un tope, un plazo, un
  cupo, una espera, un valor por defecto.
- **Queda en el código** lo que fija un proveedor o una norma (Meta, ARCA, OpenAI, el servidor de correo) y lo técnico
  (leases, lotes, polling, timeouts, reintentos, los topes de capacidad de un formulario o un catálogo).
- **Queda en `appsettings`** lo de seguridad, costo o abuso: el código de ingreso, los cupos de códigos, los límites de pedidos
  por minuto.
- **Cada valor** va en la configuración de su dueño (el núcleo no nombra los módulos opcionales: cada uno guarda lo suyo), con
  su valor por defecto, su mínimo y su máximo, revisión y 409, como cualquier configuración interna (ADR 0018).
- **Se lee de la base en cada uso,** no al arrancar. Lo guardado antes del cambio, y lo que escriba la versión anterior
  mientras conviven, lee el valor por defecto.
- **Un texto que nombra el número lo recibe como parámetro,** y la política de privacidad promete los mismos plazos. Acá es un
  sitio aparte: la (i) de cada zona de retención avisa que tiene que prometer lo mismo (P1-A).

## 2. El tope de ofrecimientos del asistente (TO1-A, con O36 y O39)

- **Antes:** el asistente de WhatsApp ofrecía hasta dos formularios automáticos por conversación y día del negocio, fijo en el
  código.
- **Ahora:** WhatsApp › Asistente › Límites suma «Tope de ofrecimientos de formularios», de 1 a 50 y 2 por defecto. Sin agente
  guardado vale 2, y un agente guardado antes del cambio también lee 2.
- **Con el tope lleno** (O36), el pedido deriva a una persona con el motivo «Llegó al tope de ofrecimientos del día», a la
  vista en la franja del chat y en el porqué. Elegir una opción de una pregunta ya ofrecida no gasta otra oferta.
- **Una nota de voz se transcribe siempre** (O39) y sigue como un texto: si la contesta un caso, la contesta.
- **Las cargas desde una planilla no gastan los ofrecimientos del día;** los textos ya no nombran «dos».
- Commits: back `a62fc296` y `6bbf37d6`, front `3e8c9cc4` y `81fa13f6`.

## 3. Las decisiones RC1-A a RC7-B

| | Qué | Dónde y con qué valores | Estado |
| --- | --- | --- | --- |
| RC1-A | La retención, cada módulo en la suya | Configuración general › «Privacidad y retención»: la IP y el navegador de un ingreso (12 meses, de 1 a 120), los códigos y enlaces (30 días, de 1 a 3650) y las invitaciones viejas (30 días, de 7 a 3650). Configuración › IA: las conversaciones de Inicio (30 días, hasta el techo de la instalación, también 30). Configuración › Formularios: vencer los documentos propuestos sin revisar y borrar los descartados (90 días cada uno, de 1 a 3650). Configuración › WhatsApp: los mensajes y el contenido del asistente (90 días, de 1 a 3650) | En `main` (`587dcadb`, `1b110f62` y `dfc48cde`) |
| RC2-B | Límites del asistente | Formularios por planilla (3, de 1 a 10), páginas de un PDF (10, de 1 a 10: la lectura de documentos no lee más) y «Volver a ofrecer un formulario rechazado» (nunca, o a los 1 a 365 días). La ráfaga, el emparejado y «no repetir un formulario ya enviado hoy» quedan como están | En `main` (back `dfc48cde`, front `2c570593`) |
| RC3-A | Configuración › WhatsApp, pantalla nueva | El tiempo para contestar un formulario enviado (24 h, de 1 a 168), los países que reciben códigos (Argentina, con un aviso de costo al sumar uno) y la retención de RC1. Sus claves salen de `appsettings` | En `main` (back `dfc48cde`, front `2c570593`) |
| RC4-A | «Pedir aprobación» y Configuración › Formularios | En la acción de cada formulario: «Puede decidir quien cargó» (no, por defecto) y «Exigir motivo al rechazar» (sí, por defecto); cada respuesta las sella al llegar, así lo que ya espera aprobación no cambia. En la pantalla nueva: el tamaño máximo por archivo (de 1 a 25 MiB, 25, sellado en la versión al publicar), las relecturas por persona y día (de 1 a 100, 5), desde cuántos documentos parecidos se sugiere armar un formulario (de 2 a 20, 3) y los dos plazos de RC1 | En `main` (back `e24400bc` y `1b110f62`, front `22e704c7`) |
| RC5 | El día de los cupos de la IA | Los cupos diarios y las relecturas se renuevan a la medianoche de la zona horaria de Configuración, no a la de UTC (las 21 en Argentina). Sin pantalla: el día del negocio es una pieza común del núcleo, que WhatsApp también usa | En `main` (`1b48cf9d`) |
| RC6-B | El nombre y los correos | Configuración › Apariencia › «Nombre y correos»: el nombre de la aplicación y el del remitente («Acceso», hasta 60 caracteres cada uno) y el logo de los correos (vacío, una dirección `https://`). Salen de `appsettings`; la pantalla muestra «Así llega» con el correo de un código. Una instalación nueva arranca «sólo por invitación». Los cupos de invitaciones siguen en `appsettings` | En `main` (back `587dcadb`, front `799b5864`) |
| RC7-B | El aviso de vencimiento del certificado de ARCA | En la zona «Certificado» de la conexión, «Avisar N días antes» (de 1 a 365, 30). Un proceso que corre cada hora manda un correo por certificado a quienes administran la conexión, en su idioma y con la fecha sólo con números, y la pantalla muestra el aviso desde ese día. La moneda de los comprobantes espera a que un cliente la pida | En `main` (back `e3fc1662`, front `a958191e`) |

Las reglas implementadas están en `docs/features/`: `administracion.md` (RC6-B y la retención de la identidad),
`identidad.md`, `inteligencia-artificial.md` (RC5 y la de la IA), `formularios.md` (RC4-A), `whatsapp.md` (TO1-A) y
`arca.md` (RC7-B).

## 4. Lo que se aprendió

- **Una regla fija suele estar en varios lugares:** el tope de dos ofrecimientos estaba en la política del asistente, en los
  textos de la pantalla, que nombraban «dos», y en la documentación, también en traspasos anteriores. Moverla es buscarlos
  todos.
- **El techo del proveedor manda sobre el tablero:** las páginas de un PDF se dibujaron de 1 a 50, pero la lectura de
  documentos no lee más de 10, y quedó de 1 a 10.
- **Una regla que traba sola merece una casilla:** con un solo aprobador en el rol, «quien cargó no decide» trababa la
  aprobación de todo lo que cargaba esa persona. De ahí «Puede decidir quien cargó».
- **Sacar una clave de `appsettings` y reconstruir van juntos** ([04](04-forma-de-trabajo.md) §2).

## 5. Para el multitenant

Leído en tu `main` (`c339b58d`). En tu modelo cada valor tiene dos niveles posibles: la plataforma, para todas las
organizaciones, y cada organización. Acá hay uno solo: la instalación.

- **El principio no está escrito allá.** Lo más cercano: «lo particular es configuración de cada organización» (tu hoja de
  ruta, «Producto genérico»), tu decisión 66 (el tope de ofrecimientos lo elige cada organización, dentro de un máximo de la
  plataforma) y tus cuotas compartidas, que viven en la configuración de Plataforma con versión y motivo
  (`docs/rules/cuotas.md`). Pero varios de estos valores siguen en `appsettings` de la plataforma. La pregunta 79 decide el
  principio y sus niveles.
- **Ya tenés dónde ponerlos:** la configuración de cada organización, la de Plataforma (`/plataforma/configuracion`, con
  `platform.settings.manage`) y la del asistente, en los dos niveles.

| Regla | Allá hoy | Qué hacer |
| --- | --- | --- |
| Tope de ofrecimientos (TO1-A) | Coincide: tu decisión 66 lo deja en cada organización, con un máximo de la plataforma, y el contador va por chat y día en la zona de la organización (`docs/plans/formularios/23-asistente.md`) | Sumá a tu E23, sin pregunta: el rango, derivar con el motivo cuando se llena (O36) y que una nota de voz se transcriba siempre (O39) |
| Límites del asistente (RC2-B) | Tu E23 ya tiene límites por bandeja, con un máximo de la plataforma | Sumá formularios por planilla, páginas de un PDF y volver a ofrecer un formulario rechazado |
| Retención (RC1-A) | Choca en WhatsApp: los chats de una organización no tienen plazo y se borran con el contacto, que tu decisión 107 no deja borrar; los 90 días son sólo de la copia global de la entrada, fijos en el código. Falta en el ingreso (no guardás la IP y el navegador de cada ingreso) y en los códigos e invitaciones (no se purgan). Coincide en el asistente de Inicio (30 días, de la plataforma, leídos en cada pasada) y en el valor de los documentos (90, fijo, sin «propuestos sin revisar») | Pregunta 81. Tu política de privacidad de ejemplo promete sólo los 30 días del asistente |
| WhatsApp (RC3-A) | Los valores coinciden (24 h, de 1 a 168; Argentina), pero en `appsettings` de la plataforma | Con la 79: la vida de un formulario, de la organización; los países, de la plataforma, porque cada código cuesta |
| Formularios (RC4-A) | El archivo (de 1 a 25 MiB) y las lecturas por persona y día en `appsettings`, y «tres o más» documentos parecidos fijo. La aprobación: «nunca quien cargó la respuesta, ni quien la corrigió» y el motivo obligatorio (`21-reglas-y-aprobacion.md`) | Con la 79, de la organización dentro del máximo; las dos casillas, pregunta 84 |
| Cupos de la IA (RC5) | Tu ADR 0049 corta el día en UTC: con la clave de la plataforma, el cupo es de la persona en todas sus organizaciones; con clave propia, de la persona en esa organización. Tu presupuesto mensual ya usa la zona de la organización | Pregunta 80 |
| Nombre y correos (RC6-B) | `Email:AppName` («Arquitectura Base») y el logo, en `appsettings`. Por organización sólo está el logo de la página pública, que va a tomar el PDF según tu decisión 82, y los correos nombran a la organización sólo en la invitación | Pregunta 82 de este traspaso |
| Aviso del certificado (RC7-B) | ARCA es tu decisión 76, con un módulo `Integrations` y sus decisiones abiertas; el certificado y su vencimiento no aparecen | Sumalo como requisito para cuando lo diseñes: avisar N días antes, con un correo por certificado a quienes administran la conexión |
