# Traspaso 18 · 06 · Las preguntas para el usuario

Las preguntas que suma este traspaso. Siguen la numeración del 17, desde la 79, y van en la tanda del 15 que dice cada una.

**Cómo hacerlas.** Como las del 15, el 16 y el 17: de a una, al llegar a su tanda, con las opciones y la recomendada primero.
Lo de ArquitecturaBase es lo más reciente que eligió el usuario: es la recomendada, salvo que tu modelo lo impida, y en ese
caso decí por qué. Lo que decida va como decisión nueva, con el número que siga a la última de tu hoja de ruta.

## Las de antes que cambian

- **La 76 del 17** (ordenar las tablas) no cambia. Si el usuario la elige, sumá también la regla de la base: lo vacío al final
  y un estado en el orden de su circuito, en tu `ApplySort` ([02](02-lo-programado-del-diseno.md) §5).
- **La 77 del 17** (la respuesta como el formulario) no cambia. Si el usuario la elige, el detalle de tu E15 tiene que traer
  la forma de carga de la versión, no sólo sus preguntas ([02](02-lo-programado-del-diseno.md) §3 y §5).

## Tanda 1 · Plataforma

79. **Ninguna regla de negocio fija:** ¿todo tope, plazo, cupo, espera o valor por defecto de una regla de negocio se ajusta
    en pantalla, con su valor por defecto, su mínimo y su máximo, revisión y 409, leído de la base en cada uso, y sólo lo que
    fija un proveedor, lo técnico y lo de seguridad quedan en el código o en `appsettings`? En tu modelo, ¿en qué nivel?
    - a) Cada organización ajusta lo suyo dentro de un máximo que la plataforma fija en su configuración, como tu decisión
      66. Recomendada.
    - b) Sólo la plataforma, igual para todas las organizaciones.
    - c) Como hoy, en `appsettings`.

    Contexto: [01](01-reglas-en-configuracion.md) §1 y §5.
80. **El día de los cupos de la IA** (RC5): acá se renuevan a la medianoche de la zona horaria del negocio, no a la de UTC. Tu
    ADR 0049 los corta en UTC, y con la clave de la plataforma el cupo es de la persona en todas sus organizaciones.
    - a) Con clave propia, a la medianoche de la organización; con la de la plataforma, en UTC, porque cruza organizaciones.
      Recomendada: es RC5 donde tu modelo lo permite.
    - b) Todo en UTC, como hoy.
    - c) Todo por organización y a su medianoche, también con la clave de la plataforma: una persona en dos organizaciones
      tendría dos cupos.

    Contexto: [01](01-reglas-en-configuracion.md) §3 y §5, y O35 en el [03](03-prueba-integral.md) §3.
81. **La retención** (RC1-A): acá cada módulo tiene sus plazos en su configuración, con mínimo y máximo, y la política de
    privacidad promete los mismos. ¿Quién los elige en tu modelo?
    - a) La plataforma fija los de sus datos (los ingresos, los códigos, las invitaciones y las conversaciones de Inicio), y
      cada organización elige los de los suyos (sus mensajes de WhatsApp y sus documentos recibidos), dentro de los límites
      de la plataforma. Recomendada.
    - b) La plataforma los fija todos.
    - c) Como hoy.

    Ojo: con la a), los chats de una organización pasan a tener plazo (90 días, como acá); hoy no tienen y se borran con el
    contacto, que tu decisión 107 no deja borrar. Contexto: [01](01-reglas-en-configuracion.md) §3 y §5.
82. **El nombre y el remitente de los correos** (RC6-B): acá el administrador los pone en Apariencia, con el logo de los
    correos. Tu `Email:AppName` («Arquitectura Base») y el logo están en `appsettings`, y por organización sólo tenés el logo
    de la página pública.
    - a) La plataforma pone el nombre de la app, el remitente y el logo en su configuración, y cada correo nombra a la
      organización con su logo. Recomendada.
    - b) Cada organización pone su nombre y su remitente.
    - c) Como hoy, en `appsettings`.

    Contexto: [01](01-reglas-en-configuracion.md) §3 y §5.

## Tanda 2 · Formularios (E14)

83. **La fecha en un Flow** (O48): el calendario de WhatsApp muestra «22 Nov. 2026», y Meta no deja elegir el formato. ¿La
    pregunta de fecha de un Flow pasa a un texto con números (dd/mm/aaaa; mm/dd/aaaa en inglés), con su validación y sin el
    calendario?
    - a) Sí, como se decidió acá el 22/11. Recomendada.
    - b) Conservar el calendario, como excepción escrita a la regla de fechas.

    Con la a), tu ficha de formatos también deja la fecha larga con el mes en letras: la regla de fechas sólo con números viene
    del T13/01 §10. Contexto: [03](03-prueba-integral.md) §3 y §6.

## Tanda 3 · Reglas y aprobación (E21)

84. **«Pedir aprobación»** (RC4-A): ¿la acción suma «Puede decidir quien cargó» (sin marcar) y «Exigir motivo al rechazar»
    (marcada), y cada respuesta las sella al llegar? Tu E21 dice que nunca decide quien cargó la respuesta ni quien la corrigió,
    y que rechazar exige un motivo.
    - a) Las dos casillas en cada acción, con lo de hoy por defecto. Recomendada: con un solo aprobador en el rol, la regla
      fija traba todo lo que carga esa persona.
    - b) Dejarlo fijo, como dice tu E21.

    Contexto: [01](01-reglas-en-configuracion.md) §3.
