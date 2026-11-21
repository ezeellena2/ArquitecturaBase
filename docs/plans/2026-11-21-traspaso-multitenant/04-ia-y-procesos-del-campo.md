# Traspaso 16 · 04 · La IA prepara y la pantalla confirma, y los procesos del campo

Nada de esto está programado: es una decisión aceptada (el ADR 0034), una propuesta (los procesos del campo, con el ADR 0033)
y una decisión sin documento (MO1-B). Cae en la tanda 4 del 15 (E23 a E25) y, lo de los procesos, en la 5 (fuera del
bloque). Anotalo sin armar etapas.

## 1. Las acciones del asistente (ADR 0034, aceptado el 19/11)

`docs/decisions/0034-acciones-del-asistente.md` (`47feb444`) y la sección «Acciones del asistente» de
`docs/plans/2026-11-19-procesos-del-campo.md`, con su lienzo de tableros. Se programa después de la prueba integral, cuando lo
decida el usuario.

- **De dónde sale.** Armando cuentas de prueba a mano, el usuario preguntó si el asistente de Inicio podía tomar un pedido
  escrito o dictado y abrirle el alta de usuario con los datos cargados. Pidió que fuera una regla general para crear, editar
  o eliminar cualquier cosa, y que una pantalla nueva quedara disponible para la IA sin programarla otra vez.
- **La decisión.** Crear, editar o eliminar con el asistente es una **acción declarada**. El modelo la propone con piezas
  comunes a todas las pantallas y la confirma la pantalla de siempre, que guarda como siempre: la IA no tiene un camino
  propio para guardar.
  1. **Acciones declaradas.** Cada una tiene un id estable con versión (`users.create@1`) y declara su tipo, sus campos y de
     dónde salen sus opciones, su permiso, su validación, dónde se confirma, si tiene un efecto fuera del sistema, sus casos
     de ejemplo y su presupuesto. Lo armado como dato (una opción de un catálogo, una respuesta de un formulario, los datos de
     una persona) es una acción por tipo de definición, no por registro.
  2. **Un registro en el núcleo** junta las acciones que aporta cada módulo y las filtra por los permisos de quien pide y por
     los módulos prendidos. El núcleo no nombra a ningún módulo.
  3. **Tres piezas para todas las pantallas:** una lectura precargada con los tipos de acción que esa persona puede hacer,
     una lectura con los campos y las opciones acotadas de una acción y una sola herramienta de proponer, con la acción y una
     lista de campo y valor.
  4. **Validar sin guardar,** con el esquema de la acción y el validador de su pantalla. Lo rechazado vuelve al modelo con la
     ruta del error.
  5. **La pantalla de siempre confirma:** lo que se guarda en un diálogo, en ese diálogo sobre Inicio, con «Propuesta del
     asistente»; lo que se guarda en una página, en esa página con lo propuesto y sin guardar, avisando antes de «Guardar» si
     dispara efectos fuera del sistema; lo que nace en borrador o inactivo lo crea la tarjeta, como hoy los formularios y los
     catálogos; editar muestra sólo lo que cambia, con el 409 de siempre; eliminar abre la confirmación de esa pantalla, de a
     una cosa; por WhatsApp, un resumen con «Revisar en la app». Nunca claves ni conexiones, consentimientos tildados,
     efectos fuera del sistema sin su confirmación ni varias cosas a la vez. Sin pantalla donde confirmar, contesta «todavía
     no».
  6. **Al proveedor, lo mínimo:** lo que dijo la persona, los tipos de acción y los nombres de las definiciones, nunca datos
     de otros registros. A quién se refiere un editar o un eliminar lo busca la tarjeta, con los permisos de quien pide.
  7. **Se ofrece calificada:** una acción se ofrece cuando sus casos pasan la calificación contra el modelo real y su costo
     está medido.
- **Enmienda dos frases del ADR 0020:** para crear, editar o eliminar, la propuesta deja de ser un DTO tipado por capacidad
  y pasa a ser la forma común, validada con el esquema de la acción. Armar una definición entera (un formulario, un catálogo)
  sigue con su capacidad y su DTO, y las propuestas que ya existen no cambian.
- **Por qué así:** con una capacidad por pantalla, las herramientas crecerían con las pantallas, y el transporte admite
  dieciséis por pedido: Inicio usa hasta trece y el asistente automático de WhatsApp, quince.
- **El primer ejemplo** es el alta de una persona en «Nuevo usuario», sobre Inicio (AU1-A), con sólo los roles que quien
  pide puede dar.
- **Etapas recomendadas:** el motor, sin ofrecer; usuarios y roles; lo armado como dato; WhatsApp, cuando el asistente
  automático tenga lugar para dos herramientas más; la documentación.

## 2. Los procesos del campo y el ADR 0033 (propuestas del 19/11)

`docs/plans/2026-11-19-procesos-del-campo.md`, `docs/decisions/0033-motores-configurables-para-procesos.md` y el diseño de
referencia `docs/specs/2026-11-19-motores-configurables-design.md` (`e888d035`).

- **Lo que decidió el usuario,** que no se vuelve a preguntar: nada por cliente, ni formularios ni desarrollos para una
  empresa; si falta algo, se mejora la dinámica de la plataforma; todo lo que hacen los otros sistemas del rubro importa,
  porque la idea es que los clientes se pasen; cada proceso que hoy se carga a mano se automatiza; y todo se puede hacer a
  mano o con la IA interna.
- **El mapa:** lo que las empresas del agro cargan a mano (labores, cosecha y logística, insumos y stock, hacienda, lo
  comercial y la administración), lo que ya se arma con formularios y 17 capacidades que faltan (C1 a C17), en cuatro
  etapas.
- **El ADR 0033, en propuesta:** el código implementa conceptos y los procesos de cada cliente son definiciones. Cada
  concepto es un motor determinista que interpreta una definición en JSON con esquema cerrado y versionado, y tiene un solo
  validador para la pantalla, la IA y la biblioteca. La IA sólo traduce: no guarda, no ejecuta y no escribe SQL ni código. Lo
  que ningún motor expresa se contesta «todavía no» y queda como faltante. Lo propio de un rubro llega en paquetes de la
  biblioteca. Pide cinco motores nuevos: saldos, flujos, agenda, relaciones y consultas.
- **Pendiente acá:** que el usuario acepte el 0033 y decida PC1 a PC5 (el orden de las etapas, cómo empieza quien sólo tiene
  el teléfono, confirmar en el chat, el paquete del rubro, y los mapas y la carga sin conexión).

## 3. Los módulos que prende y apaga el administrador (MO1-B)

Decidido el 17/11 en una charla de diseño del CRM, sin documento todavía (Pr-4 del barrido del 18/11): el administrador del
cliente prende y apaga cada módulo (WhatsApp, Formularios, IA, ARCA y el CRM) desde la app, al instante. Falta escribirlo,
dibujarlo y decidir el resto: los datos y los procesos en segundo plano de un módulo apagado, los permisos, el menú y las
rutas. El ADR que lo fije tomaría la forma de tu `[FeatureGate]`. Del CRM todavía no hay nada decidido.

## 4. Para el multitenant

- **El ADR 0034 es una forma de tu v2.** Tu ADR 0048 deja el asistente de la web con herramientas de sólo lectura en la v1,
  y su D8 deja «las acciones con confirmación» para la v2. Anotalo en tu hoja de ruta como la regla para cuando el asistente
  cree, edite o elimine, sin etapa todavía.
- **La pregunta 43 del 15 se amplía.** Tu decisión 75 (E25) crea el formulario en borrador y los catálogos por aprobar, y
  nada se publica solo. El 0034 dice lo mismo para lo que nace en borrador (lo crea la tarjeta) y suma lo demás: lo vigente
  se confirma en su pantalla, de a una cosa. Si allá se elige que el chat cree sin tarjeta, choca con el 0034. Es la pregunta
  73 del [07](07-preguntas.md).
- **En tu modelo:**
  - el registro de acciones filtra también por la organización y por los módulos prendidos para ella;
  - las opciones de una acción salen con los permisos y el «Vale en» de quien pide;
  - al proveedor nunca van datos de otra organización;
  - por WhatsApp, las acciones son para quien trabaja en la organización y con sus permisos: a un cliente que escribe (tu
    decisión 50) no se le ofrece ninguna.
- **El ADR 0033 y los procesos del campo** son información, sin etapas, porque están en propuesta. Si se acepta, su punto 8
  ya dice que en el multitenant cada motor va por organización, y los paquetes y los catálogos de referencia los comparte la
  plataforma. Se cruza con tu decisión 76 (las integraciones, primero después del bloque) y con los orígenes externos de los
  catálogos.
- **MO1-B:** allá, prender y apagar los módulos de una organización es de la plataforma (`platform.tenants.manage`). Si acá
  se escribe como está, choca con eso, porque lo haría el administrador de cada organización. Esperá el documento.
