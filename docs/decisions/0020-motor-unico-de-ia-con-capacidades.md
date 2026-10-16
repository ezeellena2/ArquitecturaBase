# ADR 0020: Motor único de IA con capacidades por módulo

## Estado

**Aceptada**, 2026-10-16 (propuesta el 2026-10-11). Rige desde la Etapa 5 de formularios.

## Contexto

El asistente de Inicio ([ADR 0017](0017-inicio-asistente-y-operaciones-durables.md)) ya tiene un motor durable en el módulo `ArtificialIntelligence`:
- operación con recibo, fence y cuotas, y contenido cifrado;
- herramientas tipadas de lectura aportadas por `ISystemQuerySource`;
- una salida terminal validada;
- `ILlmConversationClient` como transporte.

La Etapa 5 de formularios suma más usos del modelo:
- interpretar lo que escribe o dice un empleado y ofrecerle un formulario prellenado;
- sugerirle al operador;
- proponer catálogos desde el chat administrativo;
- transcribir audios;
- registrar pedidos de formularios que no existen.

El plan los describía con puertos propios (`IStructuredIntentInference`, `IStructuredSuggestionSource`, `IStructuredCatalogProposalSource`). Construidos por separado, darían dos o más motores, cada uno con su ejecución, sus permisos, sus cuotas y su auditoría. Se espera que el asistente sume muchas capacidades más.

## Decisión

Todo uso de un modelo pasa por un único motor del módulo `ArtificialIntelligence`, el de Inicio generalizado. Ese motor ofrece capacidades tipadas que aportan los módulos mediante puertos neutrales. El modelo solo lee o propone; lo que se hace con una propuesta lo valida y ejecuta el módulo dueño.

1. **Capacidades.** Cada una declara:
   - un id estable con versión y una descripción para el modelo;
   - el permiso que exige;
   - su tipo: lectura o propuesta;
   - los esquemas de entrada y salida;
   - su presupuesto.

   Las aportan el núcleo y los módulos, como hoy `ISystemQuerySource`. `ArtificialIntelligence` no nombra formularios ni WhatsApp.
2. **Solo leer o proponer.** Ninguna capacidad guarda datos ni produce efectos externos.
   - Una propuesta es un DTO tipado. El módulo dueño la valida y, si corresponde, la ejecuta con su servicio, una sola UoW, sus permisos y, si hay efecto externo, su cola durable.
   - Lo que afecta a personas o a terceros lo confirma una persona o una regla declarada.
   - El modo Asistente puede mandar un formulario solo, porque mandarlo no guarda nada.
   - Una capacidad que escriba necesita un ADR nuevo.
3. **Canales.** Cada punto de entrada define actor, cultura, capacidades ofrecidas y salidas terminales admitidas. Los puntos de entrada son Inicio, la conversación de WhatsApp en modo Asistente, la sugerencia al operador en Atención manual y el chat administrativo. Sumar una función es sumar una capacidad y el canal que la ofrece.
4. **Entrada normalizada.** Todo llega al motor como texto antes de interpretarse.
   - El audio llega transcripto, con un perfil de transcripción calificado dentro del módulo.
   - Otros formatos, como imágenes, siguen el mismo camino cuando tengan perfil calificado.
   - El archivo original conserva la retención de su módulo.
5. **Ejecución y registro comunes.** Todos los canales usan la operación durable, las cuotas, el cifrado y el uso informado de ADR 0017. Cada resultado conserva canal, capacidad y versión, perfil de modelo, versión de las instrucciones y evidencia, para poder explicar por qué se propuso algo.
6. **Evaluación.** Cada capacidad que decide sobre lenguaje tiene casos de ejemplo (entrada y resultado esperado, con datos ficticios). Esos casos se corren antes de habilitar un modelo, un perfil o instrucciones nuevas. Los tests con dobles verifican el contrato, no la calidad de la decisión.

## Consecuencias

- **Puertos del plan.** Los puertos de la Etapa 5 se reformulan como capacidades aportadas por BusinessForms y WhatsApp. El plan los ajusta al empezar esa etapa.
- **Motor de Inicio.** Se generaliza antes de la Etapa 5 con la noción de canal, conservando su comportamiento y sus pruebas.
- **Pruebas de arquitectura.** Hay que agregar las que verifiquen que:
  - ningún módulo llama al transporte del modelo fuera del motor;
  - las capacidades no reciben `IUnitOfWork` ni servicios de escritura.
- **Transcripción.** Necesita elegir y calificar un proveedor con su clave. Se verifica al implementarla si el proveedor de conversación admite audio.
- **Costo de mantenimiento.** Habrá que mantener el catálogo de capacidades y los casos de evaluación. El costo de una función nueva baja: no repite ejecución, permisos ni auditoría.

## Alternativas descartadas

- **Un cliente de IA por funcionalidad**, como describía el plan original: duplica la ejecución durable, las cuotas, la auditoría y los permisos, y cada función nueva diverge.
- **Herramientas que escriben o actúan directamente:** rompen la única forma de guardar, la revisión de permisos del módulo dueño y la confirmación humana.
- **Un framework de agentes o MCP externo:** suma dependencia y superficie sin resolver la autorización; ADR 0017 ya los dejó afuera.
