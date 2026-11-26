# Traspaso 19 · 05 · Las preguntas para el usuario

Las preguntas que suma este traspaso. Siguen la numeración del 18, desde la 85, y van en la tanda del 15 que dice cada una.

**Cómo hacerlas.** Como las del 16, el 17 y el 18: de a una, al llegar a su tanda, con las opciones y la recomendada primero.
Lo de ArquitecturaBase es lo más reciente que eligió el usuario: es la recomendada, salvo que tu modelo lo impida, y en ese
caso decí por qué. Lo que decida va como decisión nueva, con el número que siga a la última de tu hoja de ruta (hoy, la 259).

## Las de antes que cambian

- **La 83 del 18** (la fecha en un Flow, O48) **cambia de recomendada.** El 25/11 el usuario decidió conservar el selector de
  WhatsApp ([02](02-cierre-de-la-prueba-integral.md) §3), y lo del 22/11 no se programó. Queda así:
  - a) Conservar el calendario de WhatsApp como excepción escrita a la regla de fechas sólo con números; lo que la app escribe de
    esa fecha (avisos, planilla, PDF) sigue con números. Recomendada: es lo que quedó acá, y tu Flow ya usa `CalendarPicker`.
  - b) Pasar la pregunta a un texto con números (dd/mm/aaaa; mm/dd/aaaa en inglés), con su validación y sin el calendario.

  Con la a), tu ficha de formatos igual deja la fecha larga con el mes en letras fuera del Flow: la regla de fechas sólo con
  números sigue valiendo en todo lo demás.
- **La 74 del 17** (la pieza única) no cambia. Si el usuario la elige, sumá la regla del foco (IM3-A) y las excepciones escritas: la
  vista previa que imita WhatsApp, el buscador de lugares e «Ingresar con», que va con subrayado ([01](01-lo-que-termino-de-programarse.md) §2).
- **La 76 del 17** (ordenar las tablas) no cambia: el front quedó completo y te sirve como referencia entera
  ([01](01-lo-que-termino-de-programarse.md) §1).
- **La 77 del 17** (la respuesta como el formulario) no cambia. Si el usuario la elige, sumá O52 (la corrección que encuentra la
  respuesta cambiada vuelve a la lectura y «Ver la actual» abre la que la reemplazó) y O56 (lo que se hizo al recibirla nunca va
  antes de ella) ([01](01-lo-que-termino-de-programarse.md) §3).

## Tanda 1 · Plataforma y diseño

85. **La falta de permiso** (SP1-B): ¿una pantalla sin permiso se muestra en su propia dirección y dentro de la app, con su
    título y el vacío estándar con «Volver al inicio», y un 403 al guardar hace que la pantalla vuelva a leer lo que está a la
    vista (queda en sólo lectura o sin permiso)?
    - a) Sí, y se va la ruta `/sin-permiso`. Recomendada.
    - b) Conservar `/sin-permiso`.

    En tu modelo, un recurso de otra organización o de una empresa ajena sigue siendo un 404 que no dice si existe (tu E9): la
    pieza es sólo para lo propio sin el permiso. Contexto: [01](01-lo-que-termino-de-programarse.md) §6.
86. **Los datos de etiqueta y valor y las tarjetas del chat** (TC1-B a TC5-A): ¿los pares etiqueta y valor van en su recuadro,
    como una tabla de dos columnas con el dato teñido, y de borde a borde cuando son el cuerpo de una tarjeta o de una zona con
    banda; y las tarjetas del chat siguen TC1-B a TC4-A (la muestra de un catálogo con sus columnas nombradas, la propuesta sin
    resumen gris y con pie, las preguntas en una tabla y cada fuente en su tarjeta)?
    - a) Sí, en toda la app y en el chat de tu asistente de la web y de tu E23. Recomendada.
    - b) Sólo los datos de etiqueta y valor (TC5-A), sin cambiar las tarjetas del chat.
    - c) Como tus tableros.

    Contexto: [01](01-lo-que-termino-de-programarse.md) §7.

## Tanda 2 · Formularios (E18)

87. **Una respuesta de Flow que Meta no entrega** (O49): cuando llega un estado `failed` 131052 de un mensaje entrante, ¿la app
    guarda el recibo y le reenvía a la persona cada formulario abierto que pide un archivo, del mismo remitente, con «No me llegó
    tu respuesta al formulario «X». Completalo de nuevo, por favor.»?
    - a) Sí, en la misma transacción del webhook, sólo si el remitente todavía puede mandarlo y con la ventana de 24 horas
      abierta. Recomendada: es lo único que le devuelve la respuesta al negocio.
    - b) Sólo guardar el recibo y mostrarle la pérdida al operador en el chat.
    - c) Como hoy: nada.

    En tu modelo, la persona se busca en la organización dueña del número que recibió el mensaje. Contexto:
    [02](02-cierre-de-la-prueba-integral.md) §2.

## Tanda 4 · Asistente y documentos (E23 y E25)

88. **Un importe con signo en un comprobante** (O47): un débito que muestra «Importe -4.800,00», ¿se lee y se guarda negativo,
    como lo muestra, y quien lo ve lo corrige a mano?
    - a) Sí. Recomendada: es lo que decidió el usuario acá el 25/11.
    - b) En un comprobante de transferencia, leer siempre el valor sin signo.

    Contexto: [02](02-cierre-de-la-prueba-integral.md) §3.
89. **Pedir por WhatsApp un formulario «Solo en la app»** (O55): tu decisión 258 dice que el asistente no lo ofrece. Si la
    persona lo pide por su nombre, ¿el asistente le contesta que existe y le manda el enlace a su carga, sin registrarlo en
    Formularios que faltan?
    - a) Sí, cuando el nombre coincide con un único formulario «Solo en la app» activo de la organización; con ninguno o con
      varios, el pedido sigue como siempre. Recomendada.
    - b) No: el pedido queda como un formulario que falta.

    Contexto: [02](02-cierre-de-la-prueba-integral.md) §1.
