# Traspaso 15 · 03 · Las preguntas para el usuario

Todas las preguntas que los traspasos 4 a 14 dejaron para el usuario del multitenant, juntas y sin repetir, en el orden de
las tandas del [01](01-orden-de-lectura.md). Son 68: 67 salen de los traspasos y una (la 30) la suma este orden. Las siglas
de los traspasos están en el [01](01-orden-de-lectura.md#abreviaturas).

**Cómo hacerlas.** De a una, al llegar a su tanda, con las opciones y la recomendada primero. Lo de ArquitecturaBase es lo
más reciente que eligió el usuario: es la recomendada, salvo que tu modelo lo impida, y en ese caso decí por qué. «Acá» dice
lo que eligió ArquitecturaBase cuando cabe en una línea; si no, está en el contexto. Lo que decida va como decisión nueva,
con el número que siga a tu 108.

## Tanda 1 · Plataforma

**Revisiones y forma de trabajo**
1. ¿Se adopta la guía de revisión de arquitectura de 30 criterios para tus módulos, con un bloque más sobre el aislamiento
   entre organizaciones? Recomendada: sí, módulo por módulo, antes de abrirlo a varias organizaciones. Contexto: T14/01.
2. ¿La regla de versiones (la versión vieja convive con la nueva al desplegar) y «nadie da lo que no tiene» van como reglas
   de plataforma desde el primer despliegue? Recomendada: sí. Contexto: T14/01.
3. ¿Qué parte de «Varias copias» pasa a «Hoy», si allá varias organizaciones comparten copias desde el principio?
   Recomendada: las leases y las reservas de los workers (N28, I18, H23, H24 y H26). Contexto: T14/01 y 02 §4.
4. ¿La corrida completa (Docker y los controles del front) va una sola vez, al cerrar el desarrollo? Acá: sí. Contexto:
   T13/06 §3 y §7.

**Diseño para toda la app**
5. La espera de un botón: ¿el aro con el gerundio («Guardando…») sin atenuarse (LC1-A) o grisado con su texto, como tu 43?
   Contexto: T4 §7.
6. La ⓘ: ¿un tooltip, como acá, o un aviso (`ImportantNotice`)? Contexto: T4 §7.
7. Los filtros: ¿un desplegable simple, sin la cantidad por opción (PL2-A y FO1-A), o con la cantidad, como pide la regla
   general de tu front? Contexto: T5 §6 y §13.
8. La planilla: ¿chips con «Limpiar todo» y cada cambio aplicado al momento (PL1-A), o un panel con borrador hasta aplicar?
   Contexto: T5 §6 y §13.
9. Lo elegido: ¿nunca sólo un tinte, con el segmentado relleno (OE1-A), o el segmentado blanco con sombra de tu `tema.md`?
   Contexto: T5 §5 y §13.
10. Las zonas plegables: ¿se admiten fuera de permisos, en el editor de una pregunta de catálogo (ZP1-A) y en el alta de la
    conexión con ARCA, o siguen sólo en permisos y en tus excepciones escritas? Contexto: T5 §9 y §13, T6 §5 y T13/04 §7.
11. Los mensajes con datos: ¿«Agregar dato» con etiquetas y la identidad de la pregunta guardada (MD1-A), o `{pregunta}`
    escrita a mano? Contexto: T5 §8 y §13.
12. Las fechas: ¿la hora después de una coma y nunca el mes en letras, contra tu `formatos.md`? Contexto: T13/05 §2 y §5.
13. Un dato por columna: ¿vale la excepción del texto secundario en gris? Contexto: T13/05 §5.
14. El separador decimal en WhatsApp: ¿coma en español, como el Flow `r15`, o punto, como dice tu hoja de ruta? Contexto:
    T13/03 §4 y §5.

**El núcleo**
15. ¿«Nadie da lo que no tiene» se evalúa dentro de cada organización? Recomendada: sí. Contexto: T14/02 §1.
16. ¿Los topes de correo son de la plataforma o de cada organización? Recomendada: globales de la plataforma, bajo el
    cupo. Contexto: T14/02 §3.
17. ¿La retención va con los mismos plazos: 30 días, 12 meses y 14 días? Recomendada: sí. Contexto: T14/02 §5.
18. ¿Con Redis caído la app sigue, leyendo los permisos de la base? Recomendada: sí, como acá (ADR 0032). Contexto:
    T14/02 §4.
19. ¿La historia administrativa queda sin pantalla hasta dibujarla? Recomendada: sí, como acá. Contexto: T14/02 §2.
20. ¿Dónde se ve el estado del chat de WhatsApp y hasta cuándo llegan los avisos: en «Mi cuenta» (PF1-A) o en «Mensajes»?
    Contexto: T9 §4 y §7.

## Tanda 2 · Catálogos y formularios (E12 a E17)

21. Las opciones de un catálogo: ¿el modal «lista y opción» (CO1-B), que coincide con tu ficha del front, o la tabla con ⋮
    de tu E12? Contexto: T4 §3 y §7.
22. Los campos de un catálogo: ¿el modal «Campos de…» o la tabla en el lugar de tu E13? Contexto: T4 §3 y §7.
23. Exportar las opciones: ¿el Excel legible, que se reimporta sin diferencias, o el texto canónico de tu C10? Contexto:
    T4 §3 y §7.
24. Las pestañas de la planilla: ¿«Por aprobar (N)» y «Todas» (PL1-A), y ahora «Por resolver (N)» (F2), contra tu 79 y la
    prohibición de pestañas para filtrar un listado? Contexto: T5 §6 y §13, y 02 §3.
25. El filtro de una pregunta con opciones: ¿un combo de una opción, con «Todas» y buscador (PL2-A), contra la §3 de tu
    hoja de ruta («alguna de» y el criterio de «sin respuesta»)? Contexto: T5 §6 y §13.
26. «Sólo en la app»: ¿un ajuste explícito del formulario, como acá, o tu regla automática de la decisión 98? Contexto:
    T13/01 §5 y §11.
27. El Excel: ¿una fila por ítem, con un tope de 100.000 filas? Contexto: T13/01 §6 y §11.
28. La pregunta «Ubicación»: ¿en qué etapa entra? Contexto: T13/01 §7 y §11.
29. El aviso de opciones por persona: ¿sólo cuando no entran o cuando da cero (NI3-B), contra tu E16? Contexto: T9 §2 y §7.
30. Las carpetas de formularios y catálogos (CP1-A a CP5-A): ¿en qué etapa entran? No tienen etapa allá. Contexto:
    T13/01 §4.

## Tanda 3 · Lo que se carga y lo que se decide (E18 a E21)

31. Renglones: ¿como acá (RG1-A a RG13-A: una pantalla por renglón en WhatsApp, mínimo y máximo, y reglas y filtros sobre
    renglones), contra tu 54? Contexto: T12 §2 y §7; el choque está en T5 §13.
32. El redondeo de los cálculos: ¿half-even, como acá, o `AwayFromZero`, como tu E19? Contexto: T13/01 §1 y §11.
33. Las operaciones de un cálculo: ¿tres fijas, como acá, o las cuatro de tu E19? Contexto: T13/01 §1 y §11.
34. «Mantener las respuestas»: ¿también en la página de carga (AR2-A), contra tu 84? Contexto: T5 §10 y §13.
35. Quien sólo carga: ¿ve «Mis cargas» y «Mi última carga» en «Formularios», contra tu 83? Contexto: T7 §3 y §5.
36. Reintentar un aviso: ¿por persona (RT1-A) o por ejecución, como tu E21? Contexto: T9 §3 y §7.
37. ¿Las horas del PDF van en la zona de la cuenta de quien cargó o en la de la empresa? Recomendada: la de quien cargó.
    Contexto: T14/06 §10.
38. El aviso a quien cargó cuando su regla se frena: ¿por el canal durable, con «No aplica» si la ventana está cerrada, o con
    una plantilla aprobada por Meta? Recomendada: el canal durable. Contexto: T14/06 §3 y §10.
39. ¿Quien corrigió una respuesta o la pasó desde un documento queda afuera de su aprobación, o alcanza con excluir al
    autor? Recomendada: queda afuera, como acá (F4). Contexto: T14/06 §2.

## Tanda 4 · Biblioteca, asistente, documentos e IA (E22 a E25)

40. La biblioteca: ¿sólo plantillas de la plataforma, o también estándares que aportan los módulos (BI1-A)? Contexto:
    T13/01 §2, §3 y §11.
41. ¿Un tema nuevo de «Formularios que faltan» se revisa antes de ofrecerse, o se ofrece de entrada? Recomendada: se
    revisa, como acá (F7 y F14). Contexto: T14/06 §2.
42. ¿Quién califica el modelo que redacta en el asistente? Contexto: T13/03 §3 y §5.
43. La IA que arma: ¿crea con un botón de la persona (IA1-A, ADR 0024), o el chat crea al pedirlo, como tu E25? Contexto:
    T8 §5.
44. ¿Se arma también por WhatsApp (IA3-A), con «pregunta y espera» cuando el pedido es ambiguo? Contexto: T8 §5 y T11 §2.
45. ¿Va el audio en el chat del Inicio? Contexto: T8 §5.
46. Documentos: ¿la carga automática de tu 71? Contexto: T13/02 §9.
47. Documentos: ¿se leen los documentos de clientes sin cuenta? Contexto: T13/02 §9.
48. Documentos: ¿quién crea el borrador? Contexto: T13/02 §9.
49. ¿Un documento propuesto sin revisar vence a los 90 días, o se queda hasta la decisión de retención? Recomendada: vence
    a los 90 días (F9). Contexto: T14/06 §5.
50. El cupo de IA: ¿cuándo se devuelve? Recomendada: sólo lo que venció sin intentarse; la otra opción es nunca. Contexto:
    T14/05 §1.
51. El cupo de IA: ¿cuándo se renueva? Recomendada: al cambiar el día UTC (las 21 de la Argentina), con la hora local en el
    error; la otra opción es la zona de cada organización. Contexto: T14/05 §1.
52. ¿Qué pasa con las lecturas al borrar una conversación? Recomendada: se borran con ella, y lo huérfano vence a las
    24 horas. Contexto: T14/05 §3.
53. ¿La clave del proveedor de IA es de la instalación o de cada organización? Recomendada: de la instalación, con un
    anillo de claves. Contexto: T14/05 §8 y §11.
54. ¿Cuánto espera un apagado a una consulta en curso? Recomendada: el paso en curso, con el plazo del host; lo demás
    vuelve a pendiente. Contexto: T14/05 §6.

## Tanda 5 · Fuera del bloque: WhatsApp y ARCA

**WhatsApp**
55. El cifrado del contenido de los chats: ¿una clave de la instalación con el propósito atado a la organización, o una
    clave por organización? Recomendada: una de la instalación con el propósito atado a la organización; la clave por
    organización se suma si un cliente la exige. Contexto: T14/03 §1.
56. ¿Los chats reales guardados en claro se convierten o se vacían al migrar? Recomendada: convertirlos antes de migrar;
    vaciarlos sólo si son datos de prueba, como acá. Contexto: T14/03 §1.
57. ¿Se cifra todo el contenido y quedan en claro el nombre y el número? Recomendada: sí, como acá: el nombre y el número
    son lo que la bandeja muestra y busca. Contexto: T14/03 §1.
58. ¿Cuándo vence el contenido de un chat? Recomendada: a los 90 días, como acá, con el texto y la ubicación del envío
    vaciados al cerrar la captura; otro plazo por organización es configuración. Contexto: T14/03 §1.

**ARCA**
59. ¿La conexión es de cada empresa o de la organización? Recomendada: de cada empresa, porque la secuencia y la CUIT son
    suyas. Contexto: T6 §5, T13/04 §7 y T14/04 §11.
60. ¿ARCA es un módulo propio o va dentro de tu `Integrations` (decisión 76)? Contexto: T6 §5 y T13/04 §7.
61. ¿Una cuenta de plataforma única, con un ticket que sirva a todas las organizaciones (ADR 0027)? Contexto: T13/04 §7.
62. ¿Qué se hace si se bloquea la clave de la plataforma, que frena a todas las organizaciones (pausar el robot, ADR 0026)?
    Contexto: T13/04 §7.
63. ¿Quién vuelve a probar la clave rechazada de la plataforma? Recomendada: alguien con un permiso de plataforma, porque
    la clave es una para todas las organizaciones. Contexto: T14/04 §5 (H5).
64. ¿El robot corre en un host aparte o dentro de la Api? Acá se descartó el host aparte porque la clave viajaría por la red
    interna. Contexto: T13/04 §5 y §7.
65. ¿Se puede revocar desde la app? Acá quedó afuera. Contexto: T13/04 §7.
66. ¿Otro sistema en el mismo punto de venta obliga a decidir factura por factura (H3)? Recomendada: sí, con un punto de
    venta propio. Contexto: T14/04 §3.
67. ¿Poner a emitir pide `formrules.manage` y `arca.issue` (H6)? Recomendada: sí, y cargar la «Factura» sigue sin pedir
    `arca.issue`. Contexto: T14/04 §3.
68. El cliente: ¿se elige por «Inicial», con un tope de 200 por inicial, o se escribe la CUIT por WhatsApp? Recomendada: la
    inicial, que no pide un mecanismo nuevo. Contexto: T14/04 §6.
