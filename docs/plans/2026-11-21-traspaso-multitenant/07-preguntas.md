# Traspaso 16 · 07 · Las preguntas para el usuario

Las preguntas que suma este traspaso. Siguen la numeración de las 68 del [03 del 15](../2026-11-18-traspaso-multitenant/03-preguntas.md),
sin repetirlas, y van en la tanda del 15 que dice cada una.

**Cómo hacerlas.** Como las del 15: de a una, al llegar a su tanda, con las opciones y la recomendada primero. Lo de
ArquitecturaBase es lo más reciente que eligió el usuario: es la recomendada, salvo que tu modelo lo impida, y en ese caso
decí por qué. Lo que decida va como decisión nueva, con el número que siga a la última de tu hoja de ruta.

## Las del 15 que cambian

- **La 7 y la 25** (el filtro como desplegable simple y el combo de una opción con «Todas» y buscador) siguen, con un cambio
  de la pieza única: en un filtro con muchas opciones se busca adentro de la lista, no escribiendo en el campo. Contexto:
  [03](03-diseno-para-toda-la-app.md) §2.
- **La 14** (la coma o el punto decimal en WhatsApp) queda en suspenso: en la prueba, Meta rechazó la coma en el Flow (O29).
  No la hagas hasta que lo traiga el traspaso 17. Contexto: [05](05-prueba-integral.md) §4.
- **La 15** (nadie da lo que no tiene, dentro de cada organización) sigue; si se adopta, compara con los permisos efectivos,
  con lo incluido (69).
- **La 30** (en qué etapa entran las carpetas): la reemplaza la 72.
- **La 43** (crear con un botón o que el chat cree al pedirlo): la amplía la 73, que vale para crear, editar o eliminar
  cualquier cosa.

## Tanda 1 · Plataforma

69. Roles: ¿se suma «un permiso incluye a otro»? El permiso que no sirve sin el «Ver» de su misma área lo incluye, dentro de
    cada catálogo y nunca entre áreas ni entre catálogos, y la pantalla lo muestra marcado y apagado con su motivo.
    Recomendada: sí, como acá, revisando permiso por permiso contra tus pantallas. Contexto: [01](01-roles-y-permisos.md) §3
    y §6.
70. Las acciones de una fila: ¿íconos a la vista con su nombre en el tooltip, como acá, o el ⋮ de tu regla? En los dos casos,
    nunca un botón con texto en una fila, así que el «Cargar» de tu 83 deja de serlo. Recomendada: los íconos a la vista,
    como acá. Contexto: [03](03-diseno-para-toda-la-app.md) §1 y §3.
71. Elegir de una lista: ¿la pieza única (DS1-A, un botón que abre la lista con el buscador adentro cuando son más de diez, y
    DS2-A, varias elegidas por su nombre con «y N más») entra ya como regla de tus pantallas y de tus tableros nuevos, aunque
    acá todavía no esté aplicada? Recomendada: sí, como regla; las medidas finas, con el traspaso 17. La otra opción es
    esperar al 17. Contexto: [03](03-diseno-para-toda-la-app.md) §2 y §3.

## Tanda 2 · Catálogos y formularios (E12 a E17)

72. Las carpetas (reemplaza la 30 del 15): ¿las carpetas de formularios y catálogos son menús del menú lateral de cada
    organización, con dos niveles, como acá (CM1 a CM6), y en qué etapa entran? Recomendada: sí, como acá; las carpetas y su
    menú con la E14 («Formularios», que suma el filtro «Carpeta»), y lo que abre cada formulario del menú se completa con la
    E15 (la planilla) y la E20 (la carga). Contexto: [02](02-carpetas-como-menus.md) §2 y §7.

## Tanda 4 · Biblioteca, asistente, documentos e IA (E22 a E25)

73. La IA que crea, edita o elimina: ¿va como regla el ADR 0034? La IA prepara una acción declarada, la confirma la pantalla
    de siempre, de a una cosa, y lo que nace en borrador lo crea una tarjeta, sin que la IA guarde nada. Recomendada: sí,
    como la forma de la v2 de tu ADR 0048, sin etapa hasta que acá se programe. Contexto:
    [04](04-ia-y-procesos-del-campo.md) §1 y §4.
