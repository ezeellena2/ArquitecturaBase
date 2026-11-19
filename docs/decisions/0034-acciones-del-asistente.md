# ADR 0034: la IA prepara y la pantalla de siempre confirma

## Estado

**Aceptada**, 2026-11-19, por decisión del usuario sobre los tableros de
[acciones del asistente](../plans/2026-11-19-procesos-del-campo.md#acciones-del-asistente) («Sí, quedan así»). Rige desde
la etapa 1 de esa sección (el motor, sin ofrecer), que se programa después de la prueba integral, cuando el usuario lo decida.
Enmienda dos frases del [ADR 0020](0020-motor-unico-de-ia-con-capacidades.md), que se dicen en las consecuencias; el resto
sigue vigente. El punto 4 del [ADR 0033](0033-motores-configurables-para-procesos.md), todavía en propuesta, se ajusta a esta
decisión.

## Contexto

- El asistente de Inicio lee y propone con capacidades ([ADR 0020](0020-motor-unico-de-ia-con-capacidades.md)). Hoy propone,
  entre otras cosas, formularios, catálogos y una importación, cada uno con su DTO tipado y su tarjeta. A un pedido de alta,
  edición o baja de otra cosa contesta que no puede.
- En la prueba integral, el usuario pidió que el asistente tome un pedido escrito o dictado y abra el alta de usuario con los
  datos cargados. Pidió también que sea una regla general para crear, editar o eliminar cualquier cosa, y que una pantalla
  nueva, como una de transportistas, quede disponible sin programarla otra vez para la IA.
- Con el ADR 0020 tal cual, cada pantalla sumaría una capacidad con su DTO, su tarjeta y sus casos. Las herramientas crecerían
  con las pantallas, y el transporte admite dieciséis definiciones por pedido: Inicio usa hasta trece, y el asistente
  automático de WhatsApp, quince.

## Decisión

Crear, editar o eliminar con el asistente es una **acción declarada**. El modelo la propone con piezas comunes a todas las
pantallas, y la confirma la pantalla de siempre, que guarda como siempre: la IA no tiene un camino propio para guardar.

1. **Acciones declaradas.** Cada acción tiene un id estable con versión y sellado (`users.create@1`). Declara su tipo (crear,
   editar, eliminar u otro), sus campos y de dónde salen sus opciones, que forman su esquema cerrado, su permiso, su
   validación, dónde se confirma, si tiene un efecto fuera del sistema, sus casos de ejemplo y su presupuesto. Las de una
   pantalla se programan una vez. Las de lo armado como dato (una opción de un catálogo, una respuesta de un formulario, los
   datos de una persona) son una por tipo de definición, no por registro, y sus campos salen de la definición que nombra el
   modelo.
2. **Un registro en el núcleo.** Un puerto del núcleo junta las acciones que cada módulo aporta por su propia fuente, como hoy
   las capacidades, y el núcleo no nombra a ningún módulo. Se filtran por los permisos de quien pide y por los módulos
   prendidos.
3. **Tres piezas para todas las pantallas.** Una lectura precargada con los tipos de acción que puede hacer esa persona, una
   lectura con los campos y las opciones acotadas de una acción y una sola herramienta de proponer con forma cerrada: la
   acción y una lista de campo y valor.
4. **Validar sin guardar.** La propuesta se valida con el esquema de su acción y con el validador de su pantalla: formatos,
   obligatorios, opciones que existen y lo que quien pide puede dar (nadie da un permiso que no tiene). Lo rechazado vuelve al
   modelo con la ruta del error. No se miran otros registros: los repetidos y a quién se refiere quedan para la tarjeta y la
   pantalla.
5. **La pantalla de siempre confirma**, como lo hace hoy la app:
   1. lo que se guarda en un diálogo, en ese diálogo sobre Inicio, con lo propuesto y «Propuesta del asistente»;
   2. lo que se guarda en una página, en esa página con lo propuesto y sin guardar; si guardar dispara efectos fuera del
      sistema, la página lo dice antes de «Guardar»;
   3. lo que nace en borrador o inactivo lo crea la tarjeta, como hoy los formularios y los catálogos: nada queda vigente
      hasta que alguien lo publica o lo activa;
   4. editar parte de lo guardado al abrir y muestra sólo lo que cambia; si otro guarda en el medio, vale el 409 de siempre;
   5. eliminar, dar de baja o desactivar abre la confirmación de esa pantalla, de a una cosa;
   6. por WhatsApp, un resumen con «Revisar en la app» que abre lo mismo; crear, guardar o borrar desde el chat es una
      excepción que se decide aparte;
   7. el asistente nunca propone claves ni conexiones, no tilda consentimientos ni declaraciones de la persona, no dispara un
      efecto fuera del sistema sin su confirmación propia y no hace varias cosas a la vez. Sin pantalla donde confirmar,
      contesta «todavía no».
6. **Al proveedor, lo mínimo.** Le llega lo que dijo la persona, los tipos de acción y los nombres de las definiciones
   (catálogos, formularios, roles), nunca datos de otros registros. A quién se refiere un editar o un eliminar lo busca la
   tarjeta al dibujarse, con las lecturas de su pantalla y los permisos de quien pide; eso no vuelve al modelo ni queda en la
   propuesta.
7. **Se ofrece calificada.** Una acción se ofrece cuando sus casos pasan la calificación contra el modelo real y su costo está
   medido (punto 6 del ADR 0020). Lo armado como dato se califica por tipo, con definiciones ficticias. Cambiar los campos de
   una acción es una versión nueva, que se escribe recién un despliegue después del que ya la lee
   ([I4](../features/inteligencia-artificial.md#motor-único-y-propuestas-por-canal--etapa-5)).

## Consecuencias

- **Lo que cambia del ADR 0020.** Para crear, editar o eliminar, «una propuesta es un DTO tipado» (punto 2) pasa a ser la
  forma común validada con el esquema de la acción, y «sumar una función es sumar una capacidad» (punto 3) pasa a ser declarar
  una acción. Sigue lo demás: el motor único, sólo leer o proponer, los canales, la entrada normalizada, la ejecución y el
  registro comunes y la evaluación. Como ninguna acción guarda, no hace falta el ADR que el 0020 pide para una capacidad que
  escriba.
- **Armar una definición entera sigue como está.** Un formulario, un catálogo o la definición de un motor del ADR 0033 es un
  árbol que una lista de campo y valor no expresa bien: sigue con su capacidad y su DTO, y se confirma por la regla 3 o la 4.
  Las propuestas que ya existen no cambian.
- **Una pantalla nueva suma dos pasos** a la [receta de un área](../guides/agregar-un-area.md): declarar sus acciones con sus
  casos de ejemplo y prestar su diálogo, su página o su confirmación con lo propuesto. No pide código de IA ni tarjeta propios,
  y lo armado como dato no pide nada.
- **Las herramientas no crecen con las pantallas.** Inicio suma dos definiciones y una lectura precargada, y queda dentro de
  los topes. El asistente automático de WhatsApp no tiene lugar: la regla 6 espera a que suba el tope o a que las propuestas
  de los módulos pasen a ser acciones.
- **Cada pantalla acepta una propuesta:** se abre con valores iniciales, la marca de lo propuesto y «Revisá» en lo dudoso, y
  su validación sin guardar es la de su servicio, así que un cambio de campos en la pantalla es una versión nueva de su
  acción. La tarjeta y la apertura se programan una sola vez en el front.
- **Pruebas de arquitectura:** el núcleo no nombra a los módulos, y las fuentes de acciones no reciben `IUnitOfWork` ni
  servicios de escritura, como las de capacidades.

## Alternativas descartadas

- **Una capacidad con su DTO por pantalla**, el ADR 0020 tal cual: cada pantalla repite código de IA, tarjeta y casos, y las
  herramientas chocan con el tope de dieciséis.
- **Un formulario genérico en el chat, dibujado desde el esquema:** duplica la validación y el aspecto de cada pantalla (los
  roles que no se pueden dar, el teléfono con su país, los consentimientos), y lo mismo se crearía de dos maneras.
- **Confirmar en la tarjeta sin abrir la pantalla:** la persona no ve el formulario entero (lo que queda por defecto, lo que
  declara) y sería otro camino para guardar. Sólo lo que nace en borrador se crea desde la tarjeta, porque no queda vigente.
- **Que la IA guarde, o que busque a quién se refiere:** guardar rompe la única forma de guardar y la confirmación de una
  persona (ADR 0020); buscar le manda al proveedor datos de otros registros.
- **Varias acciones por turno, o un agente que encadena pasos:** cuesta confirmarlas de a una y un error se arrastra. Es una
  por turno y el asistente no sigue solo.
