# ADR 0033: los procesos se arman con motores configurables, a mano o con la IA

## Estado

**Propuesta**, 2026-11-19. Sale de [procesos del campo](../plans/2026-11-19-procesos-del-campo.md#cómo-se-construye): el usuario
decidió que no hay desarrollos por cliente y que la plataforma se vuelve más dinámica, para que cada empresa arme sus procesos a
mano o con la IA interna. No se programa nada hasta que el usuario la acepte, y la lista sigue congelada hasta la prueba integral.
Su punto 4 sigue al [ADR 0034](0034-acciones-del-asistente.md), aceptado el mismo día.

## Contexto

- Las empresas del agro cargan a mano decenas de procesos: órdenes de trabajo, movimientos de stock y de hacienda, lluvias,
  cosecha, cupos y cargas. Hoy los llevan en papel, en planillas, en un bot de audio, en un sistema de gestión agrícola o
  retipeando en el ERP. Cada uno se podría resolver con pantallas a medida, como ya se hizo para un cliente; eso no escala y deja
  el código repartido.
- Formularios ya funciona de otra manera. Un formulario, un catálogo o una regla son definiciones guardadas como datos, con
  revisiones, que interpretan servicios fijos. La IA los propone con capacidades tipadas y una persona los crea
  ([ADR 0020](0020-motor-unico-de-ia-con-capacidades.md), [ADR 0024](0024-ia-arma-formularios-y-catalogos.md)). La biblioteca trae
  estándares como datos ([ADR 0029](0029-biblioteca-de-estandares.md)).
- Para cubrir esos procesos faltan conceptos: saldos, flujos con estados, agenda, relaciones entre respuestas y consultas. También
  falta una regla que diga cómo se suman sin que cada pedido termine en código propio, ni en una llamada al modelo por cada
  cálculo.

## Decisión

El código implementa **conceptos**; los procesos de cada cliente son **definiciones**. Cada concepto es un motor con la forma de
abajo, y ningún pedido de un cliente ni de un rubro se resuelve con código para él.

1. **Una definición con esquema cerrado y versionado.** Un motor ejecuta una definición en JSON con su propio esquema, sin campos
   libres. Se guarda en el módulo dueño con revisión inmutable, revisión esperada y 409, como las reglas. Lo cargado queda atado a
   la revisión con que llegó, y un cambio de esquema es una versión nueva con su codec: lo guardado no se reescribe.
2. **Un solo validador por motor.** La pantalla, la IA y la biblioteca producen la misma definición y pasan por el mismo
   validador, que devuelve la ruta de cada error.
3. **La ejecución es determinista.** El motor interpreta la definición con código fijo: guardar, sumar, cambiar de estado,
   disparar por tiempo o consultar no llama al modelo. Los cálculos se hacen en SQL acotado y con índices, como el
   [tablero por formulario](../features/formularios.md#tablero-por-formulario--doc7-a), y no recorriendo respuestas en memoria.
4. **La IA sólo traduce.** Cada motor le aporta al motor único sus lecturas (un resumen acotado) y sus propuestas. Armar o
   cambiar una definición es un DTO tipado por `IAssistantCapabilitySource`; una carga, o lo que su pantalla crea, edita o
   elimina, es una acción declarada que confirma la pantalla de siempre ([ADR 0034](0034-acciones-del-asistente.md)).
   - La IA no guarda, no ejecuta y no escribe SQL ni código ([ADR 0019](0019-sql-de-configuracion-acotado-a-catalogos.md)).
   - Crear o cambiar una definición lo confirma una persona. Una carga la confirma quien la hace o una regla declarada
     (ADR 0020).
   - Una pregunta sobre los datos es una consulta tipada, que el motor de consultas ejecuta con los permisos de quien pregunta. Al
     modelo vuelve, como mucho, un resumen, y la respuesta puede armarla un texto fijo.
5. **No hay código por pregunta.** El backend no interpreta texto: el modelo elige una capacidad o una acción y la completa. Lo
   que ninguna capacidad ni acción puede expresar se contesta «todavía no» y queda registrado como un faltante. Se resuelve con un concepto nuevo, si se
   decide, y nunca con un caso particular.
6. **Los tokens tienen presupuesto.** Cada capacidad declara su presupuesto de entrada, salida, ítems y tiempo, y su costo se mide
   antes de habilitarla. Los resúmenes tienen tope y, para armar, no llevan respuestas ni datos de personas. Lo que no necesita
   entender lenguaje, como confirmar o elegir de una lista, no usa el modelo.
7. **El rubro va en los datos.** Lo propio de un rubro llega en paquetes de la biblioteca (formularios, catálogos y definiciones de
   cada motor) y en catálogos de referencia que mantiene la plataforma. Ningún motor nombra al agro.
8. **Cada motor va en el lugar de siempre.** Es un área del módulo dueño (hoy Formularios), con sus capas, sus permisos, su editor,
   sus capacidades, sus acciones y sus tests. Los tests del motor corren sin IA; las capacidades tienen casos de ejemplo que se califican contra
   el modelo real (punto 6 del ADR 0020). Se prende y se apaga con su módulo (MO1-B) y, en el multitenant, va por organización.

Procesos del campo pide cinco motores nuevos: saldos, flujos, agenda, relaciones y consultas. Cada uno se diseña con su esquema y
sus tableros antes de programarse. El [diseño de referencia](../specs/2026-11-19-motores-configurables-design.md) baja estas reglas
a archivos, tablas, tecnología y pruebas.

## Consecuencias

- El código crece con los conceptos, no con los clientes, los procesos ni las preguntas. Cada motor se lee, se prueba y se
  refactoriza dentro de su área.
- La pantalla y la IA hacen lo mismo: lo que se arma a mano se puede pedir por chat, y al revés.
- El costo de la IA depende de los mensajes de la gente, no del volumen de datos.
- Diseñar un motor cuesta más que una pantalla a medida: su esquema tiene que servirle a cualquier cliente y crecer sin romper lo
  guardado.
- Hay un techo deliberado: lo que ningún motor expresa no se puede armar hasta que exista ese concepto.
- Un motor no se vuelve un lenguaje de programación: los conceptos son cerrados y tipados, sin fórmulas libres ni scripts.
- No hace falta el ADR que el 0020 pide para una capacidad que escriba, porque estas capacidades y acciones sólo leen y proponen.

## Alternativas descartadas

- **Pantallas y código por cliente o por proceso:** es más rápido la primera vez, pero no escala y deja el código repartido.
- **Un módulo por rubro con entidades fijas** (campos, lotes, órdenes): sirve para un solo rubro y duplica lo que Formularios ya
  hace.
- **Que la IA genere SQL o código en el momento:** es impredecible, inseguro y caro, y contradice el ADR 0019.
- **Que la IA conteste con texto que el backend interpreta:** es frágil y no hay esquema contra el cual validar.
- **Un motor de procesos o un lenguaje de reglas de terceros:** es potente, pero difícil para el usuario final y para la IA, y los
  flujos que piden los clientes son sencillos.
- **Datos sin tipo (entidad, atributo y valor) para todo:** se pierden las validaciones, los índices y lo que hoy garantizan las
  respuestas tipadas.
