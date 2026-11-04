# La IA arma formularios y catálogos · Etapa 1: texto y audio, proponer y crear

Plan de implementación del 2026-11-04 para la Etapa 1 del [plan](2026-11-04-ia-arma-formularios-y-catalogos.md), con el
[ADR 0024](../decisions/0024-ia-arma-formularios-y-catalogos.md) y el diseño congelado en el front
([la IA arma formularios y catálogos](../../../ArquitecturaBaseFront/docs/design/ia-arma-formularios.md)). Se trabaja en modo
liviano: este diseño con un revisor, diez tareas, una revisión por tarea y la suite completa al cerrar.

Alcance: pedir un formulario o un catálogo Manual sin planilla con un texto o un audio, en Inicio (IA1-A) y por WhatsApp
(IA3-A); ver la propuesta, ajustarla conversando y crearla con un botón (IA2); H4 pasa al mismo recorrido. Las planillas
(Etapa 2) y los documentos (Etapa 3) quedan afuera: en esta etapa no hay clip.

## Decisiones técnicas

Lo que el plan no fijaba, resuelto con la opción más simple que cumple el plan y las reglas:

1. **Cuatro capacidades de BusinessForms, versión 1.** Dos lecturas con permisos separados, para que la reserva de permisos
   del motor proteja lo leído, y dos propuestas:

   | Id | Tipo | Permisos del Actor | Qué hace |
   | --- | --- | --- | --- |
   | `forms.authoring.forms` | Read | `formtemplates.read` | nombre, prefijo, estado y preguntas (etiqueta y tipo) de los formularios que existen; sin respuestas |
   | `forms.authoring.catalogs` | Read | `formcatalogs.read` | nombre, tipo, tipo de valor, activo y campos (nombre y tipo) de los catálogos; sin opciones ni personas |
   | `forms.authoring.propose-form` | Proposal | `formtemplates.read`, `formtemplates.manage` | un formulario entero, validado con las reglas de la autoría |
   | `forms.authoring.propose-catalog` | Proposal | `formcatalogs.read`, `formcatalogs.manage` | un catálogo Manual sin opciones: nombre, descripción, tipo de valor, qué es el código, qué es la opción y campos |

   Las propuestas no exigen una lectura observada: los catálogos que usa un formulario se comprueban en la base al proponer y
   al crear. Un dato inválido vuelve como `rejected`, con la ruta de cada argumento a corregir; el motor no le da Id, así que
   nunca se elige, y el modelo la corrige en el mismo turno. Nunca se recorta. La salida es la propuesta normalizada que
   muestra la tarjeta. Las lecturas van en 8 KiB cada una, para dejar lugar a la historia y a una corrección.
2. **Tope de definiciones: de 12 a 16.** Inicio queda en 12 (cinco consultas, las cuatro capacidades, las dos de H4 y la
   terminal), pero el asistente automático de WhatsApp tiene nueve más la terminal y con las cuatro llega a 14 para una
   cuenta con todos los permisos. El ADR 0024 prevé «agruparlas o revisar ese tope»; agrupar mezclaría permisos. Siguen los seis
   llamados y los cuatro pasos por turno.
3. **Inicio cierra con `assistant_answer@2`.** Una sola terminal: `kind` suma `proposal` y `proposalId` la nombra en ese caso
   y es null en los demás. El texto de una propuesta no exige evidencias. Las instrucciones pasan a `home-system@2`, genéricas:
   el motor no nombra formularios. Inicio suma sólo las capacidades que su dueño marca `OfferedInHome`, y sus sugerencias
   siguen siendo las de las cinco consultas. Los turnos con la versión 1 se siguen leyendo. La historia de Inicio suma el
   contenido de la última propuesta para ajustarla, y el listado de turnos carga los resultados de herramientas por página.
   Con el módulo dueño quitado, la explicación se lee sin la tarjeta.
4. **Tope de salida: `OutputTokens` de 1500 a 4096.** Un formulario entero viaja como argumentos de la propuesta; con 1500 un
   formulario mediano corta la respuesta.
5. **Crear lo pide la persona en la web por BusinessForms.** `POST /api/forms/definitions/assistant-proposals` y
   `POST /api/forms/catalogs/assistant-proposals` reciben el `requestId` del turno y el `proposalId`; cada uno lee la propuesta
   por un puerto nuevo del motor, `IAssistantProposalLookup`, sólo para turnos de Inicio del mismo actor: comprueba vigencia y
   permisos del turno y dice si una propuesta posterior de la misma capacidad la reemplazó (pedir un catálogo después de un
   formulario no reemplaza el formulario). La procedencia usa la hora del turno. Crear busca primero lo ya creado por esa
   propuesta, así que un segundo toque devuelve lo mismo aunque la propuesta haya vencido. Crear es idempotente por propuesta: el formulario guarda
   `ProposalId` y su procedencia como ya lo hace el catálogo de H4. `GET …/assistant-proposals/{proposalId}` dice si ya se creó,
   para la tarjeta plegada. Un nombre o un prefijo en uso no crea nada.
6. **Un catálogo Manual propuesto** nace inactivo con la propuesta pendiente, igual que H4, y se aprueba o descarta en su
   página. Aprobar un Manual sólo activa y guarda la decisión; no prueba filtros.
7. **El front conecta la tarjeta sin que las features se importen:** un contexto en `src/shared/hooks` que provee `src/app`
   con componentes de `business-forms` cargados con lazy, como `UserDataSectionContext`. También aporta las sugerencias de
   Inicio vacío según los permisos. «Reemplazada» lo calcula el front con los turnos de la conversación (una propuesta
   posterior de la misma capacidad); el servidor lo impide al crear.
8. **WhatsApp arma el resumen con un puerto neutral nuevo** (`IStructuredAuthoringProposalChannel`), que BusinessForms
   implementa, con su versión apagada en `Application/Channels`: resume una propuesta (título, hasta 10 líneas y cuántas
   quedan) y la crea. H4 pasa por el mismo puerto. «Cambiar algo» deja la conversación esperando el cambio, y el mensaje
   siguiente se interpreta con la propuesta anterior: el motor recibe la operación anterior y agrega el turno nuevo a la
   conversación del motor de esa operación, como hace Inicio, así que la reserva de permisos, la historia vencida y la
   retención ya aplican; la huella suma esa referencia sólo cuando existe. Los casos que hoy no tienen texto (sin permiso,
   uno que ya existe, un archivo que no se lee) llegan como una propuesta de «no se puede» con su motivo, que el canal
   traduce a su texto fijo.
9. **Audio en Inicio:** el navegador sube la grabación (WebM u MP4, hasta 1.000.000 bytes como WhatsApp) como copia privada; el
   motor la normaliza con `NormalizeAudio` en un canal propio. El turno recibe el `RequestId` de esa normalización y el servidor
   toma el texto de la operación completada (mismo dueño, vigente), nunca del cliente; la referencia al audio entra en la
   huella y una fuente de retención del módulo conserva el archivo mientras dure el contenido del turno. El bootstrap dice si
   hay audio (proveedor, perfil de transcripción para lo que graba el navegador y almacenamiento). `NormalizeAudio` suma WebM y
   MP4, verificados por su contenido, con un perfil de transcripción nuevo que se califica.

## Tareas

Cada tarea sigue [validar un cambio](../guides/validar-un-cambio.md): TDD en las políticas, build y tests rápidos del área en
cada corte. Las del backend exportan los contratos al cerrar. El orden cambió con la revisión: el canal Inicio va antes de
leer y crear una propuesta, y el cambio del motor para «Cambiar algo» tiene su propia tarea.

1. **Lecturas de lo que existe.** Hecha (6b113b2e).
2. **Propuestas de formulario y de catálogo.** Hecha (6b113b2e), con el rechazo con motivos en bd75ea73.
3. **Canal Inicio.** Hecha en el motor (bd75ea73): capacidades `OfferedInHome`, `assistant_answer@2`, `home-system@2`, tope de 16,
   `OutputTokens`, historia y listado con la propuesta, `proposal` en el turno HTTP y H4 en Inicio. Falta la captura del
   contrato HTTP y la integración.
4. **Leer una propuesta de Inicio.** Hecha (cb172643): `IAssistantProposalLookup` con su versión apagada y la del motor: sólo
   Inicio, dueño, vigencia, permisos y reemplazo por capacidad.
5. **Crear desde una propuesta.** Hecha (da624007 y 50d6f354): `ProposalId` y evidencia en el formulario y propuestas Manual en
   el catálogo (migración), lo ya creado antes de leer la propuesta, idempotencia, nombre o prefijo en uso, endpoints de crear y
   de estado, aprobar un Manual y la prueba de arquitectura. La integración con PostgreSQL y la cohorte de ejemplos HTTP
   (`forms-assistant-proposals-http`, 14 cuerpos) están escritas y corren en el cierre conjunto.
6. **Tarjeta en el hilo.** Hecha (front 5ad868b0 y b2fc0422; backend 72196b2d y a39d5bf2): `AssistantProposalCard` en
   `shared/ui/assistant` con sus estados, el contexto de extensión que compone app, la tarjeta de formulario, de catálogo y de
   H4 en `business-forms`, «Ver cómo queda» con `FormPreview`, crear y navegar, y la banda de propuesta en un catálogo Manual.
   Las escenas visuales es/en están escritas y se capturan al cierre, con los contratos de la cohorte exportada.
7. **Entradas y sugerencias.** Hecha (front b0eaa847 y b66026ed): «Armar con la IA» en Diseñar formularios, Catálogos y
   Formularios que faltan, con el pedido empezado en el estado de la navegación, y la sugerencia y el texto del editor de Inicio
   vacío. Tests y la escena `assistantEntries`, sin golden.
8. **Continuar una operación en el motor.** Hecha (36923c7b): `PreviousRequestId` en la operación neutral suma el turno a la
   conversación de la última operación terminada y vigente de las mismas personas y conversación del canal, con la reserva de
   permisos; la historia de un canal lleva la última propuesta con su contenido y la huella suma la referencia sólo cuando
   existe. Unitarias y huella; la persistencia usa los métodos existentes del repositorio y se recorre con PostgreSQL en la 9.
9. **WhatsApp.** El puerto del resumen y crear, las capacidades en el asistente automático, el resumen con «Crear borrador» o
   «Crear catálogo» y «Cambiar algo», el Id de los botones, sólo la última propuesta, el enlace, los textos fijos de «no se
   puede», H4 con el botón y las propuestas fuera del cupo de ofertas. Unitarias e integración.
10. **Audio.** WebM y MP4 en `NormalizeAudio` con perfil nuevo; en Inicio, subir, normalizar, escuchar, retener y la referencia en
    el turno; en el front, grabar, detener, descartar, escuchar y la transcripción en gris. Tests y visuales.
11. **Casos de evaluación y cierre.** Casos ficticios del arnés de calificación para lo nuevo (pedidos escritos y dictados,
    ajustes, rechazos corregidos, nombres en uso, sin permiso, ambiguos e instrucciones escondidas) y volver a correr los de
    Inicio y de WhatsApp, que cambian; la corrida con el proveedor real espera el visto bueno del usuario. Reglas a
    `docs/features`, diferencias con el dibujo en el front, suites completas.

Riesgos a medir en la calificación: el presupuesto de contexto en un ajuste con historia, dos lecturas, una corrección y la
terminal (131072 bytes, con la continuación contada aparte), y que una salida larga entre en los 30 segundos del proveedor.

## Estado

2026-11-04: tareas 1 a 3 hechas en el backend (6b113b2e y bd75ea73), con unitarias, de arquitectura y de dominio en verde. La
revisión del plan (15 hallazgos) cambió el rechazo de propuestas, el mecanismo de Inicio sin nombrar formularios, el
reemplazo por capacidad, la continuación de WhatsApp en el motor y el audio de Inicio (retención y transcripción del
servidor). Sigue la tarea 4.

2026-11-04: tareas 4 y 5 hechas en el backend, con unitarias, de dominio y de arquitectura en verde. Lo que el plan no fijaba,
con la opción más simple: el nombre en uso se compara sin mayúsculas con el nombre visible en el idioma de la propuesta (también
para H4 desde Inicio); el estado da el Id creado o null, nunca 404; la evidencia dice `requestedIn: "app"` para la banda «en el
chat de Inicio»; los catálogos que usa un formulario tienen que seguir activos y legibles al crear; aprobar un Manual sólo lo
activa y la edición común no activa una propuesta pendiente. Sigue la tarea 6, la tarjeta en el front.

2026-11-04: tareas 6 y 7 hechas en el front, con tsc, lint y las tandas de las áreas tocadas en verde. Lo que el plan no fijaba:
la sugerencia «Armame un catálogo con una planilla» llega con el clip en la Etapa 2, porque sin planilla no lleva a nada; en esta
etapa Inicio vacío suma sólo «Armame un formulario para…», con el permiso de diseñar formularios. De paso se arregló lo que
encontró la sesión de las listas dependientes: los dobles de Inicio (85bbcd69), el conteo del OpenAPI (90c264ad) y la historia
de la migración de valores del catálogo (e2ec8fbd). Sigue la tarea 8.

2026-11-04: tarea 8 hecha en el motor, con las unitarias de Application, arquitectura y la huella en verde. Lo que el plan no
fijaba: la operación anterior tiene que ser la última de su conversación (si no, revisión cambiada, como la revisión esperada
de Inicio), terminada y vigente; el ámbito compara personas y conversación del canal, no lo ofrecido, que puede cambiar entre
mensajes. Sigue la tarea 9, WhatsApp.
