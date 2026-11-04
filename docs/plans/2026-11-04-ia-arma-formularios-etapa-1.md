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
   al crear. Un dato inválido devuelve la propuesta rechazada con sus motivos, para que el modelo la corrija en el mismo turno;
   nunca se recorta. La salida es la propuesta normalizada que muestra la tarjeta.
2. **Tope de definiciones: de 12 a 16.** Inicio queda en 12 (cinco consultas, las cuatro capacidades, las dos de H4 y la
   terminal), pero el asistente automático de WhatsApp tiene nueve más la terminal y con las cuatro llega a 14 para una
   cuenta con todos los permisos. El ADR 0024 prevé «agruparlas o revisar ese tope»; agrupar mezclaría permisos. Siguen los seis
   llamados y los cuatro pasos por turno.
3. **Inicio cierra con `assistant_answer@2`.** Una sola terminal: `kind` suma `proposal` y `proposalId` es obligatorio en ese
   caso y vacío en los demás. El texto de una propuesta no exige evidencias. Las instrucciones pasan a `home-system@2`; los
   turnos con la versión 1 se siguen leyendo. La historia de Inicio suma la última propuesta (su contenido normalizado) para
   poder ajustarla.
4. **Tope de salida: `OutputTokens` de 1500 a 4096.** Un formulario entero viaja como argumentos de la propuesta; con 1500 un
   formulario mediano corta la respuesta.
5. **Crear lo pide la persona en la web por BusinessForms.** `POST /api/forms/definitions/assistant-proposals` y
   `POST /api/forms/catalogs/assistant-proposals` reciben el `requestId` del turno y el `proposalId`; cada uno lee la propuesta
   por un método nuevo del puerto del motor, `IAssistantOperations.FindProposalAsync`, que comprueba dueño, vigencia y permisos
   del turno y dice si sigue siendo la última de su conversación. Crear es idempotente por propuesta: el formulario guarda
   `ProposalId` y su procedencia como ya lo hace el catálogo de H4. `GET …/assistant-proposals/{proposalId}` dice si ya se creó,
   para la tarjeta plegada. Un nombre o un prefijo en uso no crea nada.
6. **Un catálogo Manual propuesto** nace inactivo con la propuesta pendiente, igual que H4, y se aprueba o descarta en su
   página. Aprobar un Manual sólo activa y guarda la decisión; no prueba filtros.
7. **El front conecta la tarjeta sin que las features se importen:** un contexto en `src/shared/hooks` que provee `src/app`
   con componentes de `business-forms` cargados con lazy, como `UserDataSectionContext`. También aporta las sugerencias de
   Inicio vacío según los permisos. «Reemplazada» lo calcula el front con los turnos de la conversación; el servidor lo
   impide al crear.
8. **WhatsApp arma el resumen con un puerto neutral nuevo** (`IStructuredAuthoringProposalChannel`), que BusinessForms
   implementa: resume una propuesta (título, hasta 10 líneas y cuántas quedan) y la crea. H4 pasa por el mismo puerto. «Cambiar
   algo» deja la conversación esperando el cambio, y el mensaje siguiente se interpreta con la propuesta anterior: el motor
   acepta una operación anterior (`PreviousRequestId`) del mismo actor, canal y ámbito y la suma a la historia.
9. **Audio en Inicio:** el navegador sube la grabación (WebM u MP4, hasta 1.000.000 bytes como WhatsApp) como copia privada; el
   motor la normaliza con `NormalizeAudio` en un canal propio y la transcripción se manda como pregunta del turno, que guarda la
   referencia al audio para escucharlo después. `NormalizeAudio` suma WebM y MP4, verificados por su contenido.

## Tareas

Cada tarea sigue [validar un cambio](../guides/validar-un-cambio.md): TDD en las políticas, build y tests rápidos del área en
cada corte. Las del backend exportan los contratos al cerrar.

1. **Lecturas de lo que existe.** `forms.authoring.forms` y `forms.authoring.catalogs`, con lector propio, presupuesto de
   16 KiB y lectura parcial marcada, codec y pruebas.
2. **Propuestas de formulario y de catálogo.** Esquemas cerrados, validación con `SaveFormDraftRequestValidator` y las reglas
   de catálogos, catálogos existentes y activos, propuesta normalizada y rechazo con motivos. Pruebas unitarias.
3. **Puerto para leer una propuesta de Inicio.** `FindProposalAsync` en `IAssistantOperations`, con el adaptador apagado y el
   del motor, y pruebas de dueño, vigencia, permisos y «sigue siendo la última».
4. **Crear desde una propuesta.** Columnas de propuesta en el formulario y propuestas Manual en el catálogo (migración),
   servicios con una UoW, idempotencia y nombre o prefijo en uso; endpoints de crear y de estado; aprobar y descartar un Manual.
   Integración con PostgreSQL, inventario de rutas, OpenAPI y ejemplos HTTP.
5. **Canal Inicio.** Catálogo con las capacidades nuevas y las de H4 según permisos, `assistant_answer@2`, `home-system@2`
   es/en, tope de 16, `OutputTokens`, historia con la última propuesta, `proposal` en el turno HTTP (Id, capacidad, versión y
   contenido) y H4 con el contexto de Inicio. Pruebas unitarias, de integración y la captura del contrato.
6. **Tarjeta en el hilo.** Pieza de `shared/ui/assistant` con sus estados (abierta, «Reemplazada», «Borrador creado»), el
   contexto de extensión, la tarjeta de formulario, de catálogo y de H4 en `business-forms`, «Ver cómo queda» con
   `FormPreview`, crear y navegar, y la banda de propuesta en un catálogo Manual. Tests, contratos y visuales es/en.
7. **Entradas y sugerencias.** «Armar con la IA» en Diseñar formularios, Catálogos y Formularios que faltan, con el pedido
   empezado en el estado de la navegación, y la sugerencia de Inicio vacío. Tests y visuales.
8. **WhatsApp.** El puerto del resumen y crear, las capacidades en el asistente automático, el resumen con «Crear borrador» o
   «Crear catálogo» y «Cambiar algo», el Id de los botones, sólo la última propuesta, el enlace, «Cambiar algo» con
   `PreviousRequestId`, H4 con el botón y las propuestas fuera del cupo de ofertas. Unitarias e integración.
9. **Audio.** WebM y MP4 en `NormalizeAudio`; en Inicio, subir, normalizar, escuchar y la referencia en el turno; en el front,
   grabar, detener, descartar, escuchar y la transcripción en gris. Tests y visuales.
10. **Casos de evaluación y cierre.** Casos ficticios del arnés de calificación (pedidos escritos y dictados, ajustes, nombres
    en uso, sin permiso, ambiguos e instrucciones escondidas) y sus comprobaciones locales; la corrida con el proveedor real
    espera el visto bueno del usuario. Reglas a `docs/features`, diferencias con el dibujo en el front, suites completas.

## Estado

Por empezar.
