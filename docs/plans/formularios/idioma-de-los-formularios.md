# Idioma de los formularios: el original obligatorio y la traducción opcional

Estado: decidido el 2026-10-16; tableros I1–I3 elegidos el 2026-10-18; sin empezar. Arranca cuando termine la corrección del [code review de la Etapa 5](../../reviews/2026-10-18-code-review-etapa-5.md). Su punto 24 resuelve una cultura común para el formulario, la apertura, los avisos y el PDF, y esto se apoya en eso.

## Qué se construye

Tableros: I1–I3 en [Formularios por WhatsApp][tableros]. Reemplazan lo de E y P1 sobre los dos idiomas.

- **Regla:** cada formulario tiene un idioma original (`draft.culture`, ya inmutable), que es el único obligatorio para guardar y publicar. La traducción es opcional.
- **Publicar (I2, I3).**
  - Backend: `FormPublicationPolicy` deja de exigir es/en completos. Exige el original completo y, por cada idioma secundario, informa si está completo o qué le falta, sin bloquear.
  - **Meta:** se compila y se publica un Flow solo por cada idioma completo. Una versión nueva puede sumar el inglés o perderlo, y el chequeo lo avisa.
  - Front: la comprobación dice en qué idiomas va a WhatsApp («En WhatsApp, en español»), avisa «El inglés no se publica» con lo que falta y ofrece «Completar el inglés». La confirmación cubre los dos casos y la versión que suma o pierde un idioma.
- **Editor (I1).**
  - «Textos en» marca el original como obligatorio y el inglés como opcional.
  - Lo que falta se cuenta en gris («3 sin traducir») y cada texto dice «Sin traducir», sin alerta.
  - La vista previa muestra lo que recibe una cuenta en inglés: entera en español mientras falte algo, entera en inglés al completarlo, nunca mezclada.
- **Envío.** El idioma de la sesión es el de la cuenta de quien recibe, si la versión tiene ese idioma publicado y completo en todo lo que usa el envío: formulario, avisos de la revisión de reglas y diseño del PDF. Si no, todo sale en el original. Formulario, apertura, avisos y PDF comparten esa misma cultura, fijada al aceptar el envío.
- **Catálogos.** Sigue la excepción decidida en la Etapa 2: un ítem sin inglés muestra su texto original, porque casi siempre son nombres. Las demás reglas del catálogo no cambian.

## Decisiones tomadas

No se vuelven a preguntar.

- El idioma del sistema (pantallas, mensajes y errores) no cambia.
- No hay traducción automática.
- Las versiones ya publicadas con los dos idiomas siguen igual. Las sesiones y lo sellado (perfiles, presentaciones y copias fijas) no se recompilan.
- Un formulario publicado solo en el original se manda en el original a todas las cuentas.
- Sumar el inglés después es una versión nueva.
- Lo nuevo de las Etapas 4 y 5 ya sigue esta regla: no se toca.

## Fuera de esta etapa

- Más idiomas y varios países.
- Traducción asistida por IA.

## Cómo se cierra

- **Checks:** por corte y al cerrar, según [ADR 0018][0018] y [validar un cambio][validar].
- **TDD:**
  - la política de publicación con traducción completa, incompleta y ausente;
  - el compilador, que publica solo los idiomas completos;
  - la resolución de la cultura del envío, con un faltante en el formulario, en las reglas o en el PDF;
  - un ítem de catálogo sin inglés.
- **Integración con Docker:** publicar con el original solo, sumar el inglés en una versión nueva y perderlo en otra; el envío y la captura en cada caso; los avisos y el PDF con la misma cultura del formulario.
- **Front:** I1–I3 en el navegador, es/en, a 390 y 320 px, con sus pruebas visuales.
- **Piloto real con Meta:**
  - publicar solo en español; una cuenta en inglés lo recibe entero en español, incluidos los avisos y el PDF;
  - completar el inglés y publicar la versión siguiente; la misma cuenta lo recibe entero en inglés.
  - con el inglés completo en el formulario pero faltante solo en un aviso o en el diseño del PDF, la misma cuenta recibe todo en español: formulario, apertura, avisos y PDF. Este caso es el punto 24 del review de la Etapa 5, que quedó corregido sin piloto y se prueba acá.

[tableros]: ../../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#los-tableros
[0018]: ../../decisions/0018-robustez-proporcional-al-efecto.md
[validar]: ../../guides/validar-un-cambio.md
