# WhatsApp después del chat: entregas pendientes

Estado: **propuesta**, salvo los [arreglos de la revisión de arquitectura](#arreglos-de-la-revisión-de-arquitectura),
decididos el 2026-11-13. Alcance compartido: `ArquitecturaBase` y `../ArquitecturaBaseFront`.

El chat de atención, sus permisos y la separación entre acceso y atención están implementados:
las reglas están en [WhatsApp](../features/whatsapp.md#chat-de-atención--2026-10-02) y en el
[ADR 0011](../decisions/0011-separacion-acceso-y-atencion-whatsapp.md). La IA asistida tiene su
propio plan: [sugerencias con revisión humana](2026-10-03-sugerencias-ia-whatsapp.md). Este plan
conserva solo lo que todavía no existe en el código. El inventario original se recupera desde Git
([procedencia](../history/README.md)).

## Condiciones comunes

- Una instalación atiende un negocio, con su administrador, datos, número de WhatsApp y conexión
  de IA. No hay multitenancy, Embedded Signup, superadministración ni claves por operador.
- Cada pantalla nueva arranca de su tablero elegido. Las maquetas de `design-lab/whatsapp-chat`
  (`?screen=contactos`, `plantillas`, `conexion`) son evidencia para discutir, no contrato
  ([ronda final](../../../ArquitecturaBaseFront/docs/design/whatsapp-chat-propuesta.md#ronda-final-para-revisar-2026-10-02)).
- Cada permiso nuevo va en `WhatsAppPermissions` y se compone con `IPermissionCatalogSource`
  ([ADR 0010](../decisions/0010-catalogo-de-permisos-extensible.md), [permiso nuevo](../guides/permiso-nuevo.md)).
- Todo vive en `Modules/WhatsApp` de cada capa; el núcleo no lo nombra y se puede quitar.

## Contactos

Propuesta, sin implementar. Listado **de solo lectura** de los contactos que llegaron por el
webhook: búsqueda, filtro por cuenta vinculada con conteos, ficha y «Abrir chat».

- No crea contactos ni cambia vínculos: vincular y soltar siguen en perfil y administración
  (`PhoneNumberLinker`, `WhatsAppContactLinker`).
- Permiso propuesto `whatsapp.contacts.read`, separado de atender un chat. Los datos de la cuenta
  vinculada se proyectan solo con `users.read`.
- Rutas propuestas: `GET /api/whatsapp/contacts` (paginado con `PagedRequest`, `ApplySort` y
  conteos por opción) y `GET /api/whatsapp/contacts/{id}`. En el front, listado con
  `usePagination`/`useFilters`.
- Cierre: tablero elegido, rutas en el inventario y en OpenAPI con sus 401/403, integración con
  Docker y front es/en a 1440/390/320 px.

## Plantillas de Meta

Propuesta, sin implementar. Redactar, mandar a revisión y consultar el estado desde la aplicación.

- Graph API crea con `POST /{WABA_ID}/message_templates`; también lista, edita y elimina. Crear
  deja la plantilla en revisión (PENDING): no la aprueba ni la envía. Referencias:
  [colección de Meta](https://www.postman.com/meta/whatsapp-business-platform/folder/lczy75a/templates).
- Usa `WabaId` y `AccessToken` de la conexión actual (hoy `WabaId` existe solo para Flows), nunca
  la clave del LLM ni el id del contacto. Un timeout al crear se consulta antes de reintentar: es
  un efecto externo ([ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md)).
- Persistencia local solo si hacen falta borradores o sincronización; la aprobación es de Meta.
- Distinguí plantillas aprobadas, respuestas rápidas internas y texto sugerido por IA.
- Casos iniciales: `codigo_ingreso` e `invitacion_acceso` si la instalación las necesita, y las de
  atención que use el negocio; contenido y categoría se confirman con Meta.
- Gestionar plantillas no habilita campañas masivas. Mandar una plantilla desde el chat, fuera de
  la ventana de 24 horas, es otro recorrido con su autorización; hoy el chat solo avisa que la
  ventana se cerró.
- Permiso propuesto `whatsapp.templates.manage`. Cierre: tablero, contratos y cliente de gestión
  con Meta simulado, y prueba controlada en la cuenta real registrando qué estados se vieron.

## Conexión del canal en pantalla

Propuesta. Hoy `WhatsAppOptions` se lee al arrancar y los secretos van en user-secrets o variables
de entorno ([configuración](../features/whatsapp.md#configuración)). Una pantalla de diagnóstico
mostraría número, estado y configuración asistida sin devolver secretos; no se promete edición en
caliente. Permiso reservado `whatsapp.configure`, sin endpoint hasta definir la entrega.

## Desvío registrado: estado del listado del chat

`WhatsAppChatPage` guarda conversación, búsqueda, «Sin leer» y página en `useState`, con `Input`.
Las [premisas del front](../../../ArquitecturaBaseFront/docs/guides/premisas-de-desarrollo.md#filtros-y-búsqueda)
piden búsqueda, página y filtros en la URL con `usePagination`/`useFilters` y `SearchInput`
([listados](../../../ArquitecturaBaseFront/docs/architecture.md#listados)). Decidí si el chat
adopta esos hooks o se documenta como excepción; el borrador sigue en memoria en ambos casos.
Cierre: decisión registrada en la feature del front y test que la cubra.

## Instalaciones independientes y proyectos derivados

Ya vigente: otra empresa usa otra instalación con datos, recursos, secretos y administrador
inicial propios ([seed](../guides/despliegue.md#qué-hace-el-seed-en-cada-arranque)); la
separación de user-secrets, base, prefijo Redis, anillo e `InstallationId` de IA está en
[configurar IA](../guides/configurar-ia.md#backups-restauración-y-clones). Falta:

1. **Guía `docs/guides/crear-proyecto-desde-la-base.md`.** Partir de una versión identificada de
   la base y registrarla; `UserSecretsId`, base, Redis y prefijo, puertos, origen e issuer,
   certificados, Data Protection, anillo de IA e `InstallationId` propios. Administrador inicial
   propio, sin cuentas de soporte ni credenciales heredadas. Meta propio: número y
   `PhoneNumberId`, WABA, token, plantillas, app y callback del webhook (no reutilizar el destino
   anterior por copiar opciones). Marca, correo, idioma, zona, observabilidad y política de
   privacidad del negocio. Migraciones desde base vacía y comprobación de login, webhook, chat e IA.
   Las funciones del negocio van en áreas o módulos del derivado; las reglas de un cliente no
   entran en identidad, en el transporte de WhatsApp ni en la IA común.
2. **Traslado de mejoras comunes.** Las correcciones de chat, permisos, transporte y componentes
   genéricos se hacen en la base y se llevan a cada derivado revisando diff, conflictos,
   migraciones y pruebas, con la versión adoptada registrada. Automatizarlo solo ante una
   necesidad concreta.
3. **Prueba con dos instalaciones.** Dos despliegues con base, Redis y configuración separados:
   usuarios, roles, contactos repetidos y mensajes de uno no aparecen en el otro; cambiar una
   clave, configuración o permiso no afecta al otro; webhooks y envíos usan el número correcto;
   migraciones y seed funcionan desde cero sin datos ni credenciales heredados. Integración con
   Meta y LLM simulados; cada conexión real se comprueba aparte, de forma controlada. Esta prueba
   cubre también la segunda instalación pendiente de la conexión de IA.

Cierre: la guía escrita y ejecutada en una segunda instalación, con evidencia fechada de qué se
comprobó con servicios reales y qué con dobles. Clonar el código no acredita una instalación.

## Arreglos de la revisión de arquitectura

Decididos el 2026-11-13 y en programación, etapa por etapa (lo hecho, al pie de cada etapa). Salen de la
[revisión de arquitectura de WhatsApp](../reviews/2026-11-12-arquitectura-whatsapp.md), que tiene el
escenario, la evidencia (archivo y línea en `9c0eb048`) y el esfuerzo de cada hallazgo, y de las
[decisiones del usuario](../reviews/2026-11-12-arquitectura-whatsapp.md#decisiones-del-2026-11-13),
que no se vuelven a preguntar.

- **Alcance:** los hallazgos de «Hoy» (H2 a H22), menos H14 y el ensayo de H15, que ya están en
  [verificación y producción](verificacion-y-produccion.md); además H25, H33 y la prioridad de H31.
- **Fuera:** H23, H24 y H26 van antes de pasar de una réplica; el resto de gran escala, cuando haya
  volumen real que medir.
- **Al día:** el 2026-11-13 se contrastó lo que cambió desde `9c0eb048` y ningún hallazgo quedó
  arreglado. W3 ya cifra y hace vencer lo que la persona comparte, pero no la ubicación del envío ni
  su texto (H13). Cada tarea vuelve a leer sus archivos antes de empezar.

**Cómo se trabaja.** Cada tarea sigue [validar un cambio](../guides/validar-un-cambio.md): el test en
rojo primero y las suites rápidas del área; si toca datos, permisos, la cola de salida o Meta, una
revisión adversarial antes del commit. Cada etapa cierra con los unitarios y `ArchitectureTests`
enteros y la integración de lo que cambió; la suite completa con Docker la corre la coordinadora una
sola vez, al final de la tanda (decidido el 2026-11-13). Además:

- una regla escrita que cambia se corrige con su arreglo, no antes, en [WhatsApp](../features/whatsapp.md),
  en sus [servicios](../features/whatsapp-servicios.md) y en los `AGENTS.md` que la repiten. La cadena
  de guías de `Infrastructure/Modules/WhatsApp/Persistence` está casi en el tope de
  `InstructionChainSizeTests`: lo que se cambie en esa cadena no puede crecer;
- un error nuevo se declara en Domain, con su código estable y su texto en `Errors.resx` y `.en.resx`;
- un cambio de un contrato publicado es aditivo, un campo o una ruta nuevos, y se revisa con el front
  ([ADR 0005](../decisions/0005-sin-versionado-de-api-por-ahora.md));
- los logs van con `[LoggerMessage]`, sin cuerpos, códigos de ingreso, tokens ni teléfonos enteros.

Las etapas van en orden. Dentro de una etapa, las tareas pueden ir en cualquier orden, salvo las
dependencias que se nombran.

### Etapa 1: lo trivial y la documentación

1. **H17. La paridad de todos los `.resx`.** Un test que recorra cada `.resx` de Application con su
   `.en.resx`, en lugar de las listas a mano que hoy dejan afuera `AssistantTexts`,
   `AssistantTerminalTexts` y `AssistantAgentValidation`. Trivial.
2. **H22. La guía para quitar WhatsApp.** Sumar `WhatsAppAssistantAgents` y unificar la cuenta en
   diez tablas: hoy dice «ocho» en dos lugares y «nueve» en otros dos. La exportación también guarda
   la configuración del asistente. Trivial.
3. **H15. El plazo del respaldo.** Sólo documentación en [desplegar a Azure](../deploy/azure-setup.md):
   fijar el plazo de restauración a un momento dado (7 días por defecto, hasta 35) y la redundancia al
   crear el servidor, y levantar una copia restaurada con `WhatsApp:ProcessOutgoingInBackground=false`
   y `WhatsApp:ProcessFlowsInBackground=false` hasta revisar lo pendiente. La guía avisa que la cola de
   acceso no tiene interruptor y manda igual lo que quedó pendiente.
4. **H18. El idioma y la región de quien recibe.** La ayuda decimal en español (`DecimalHelp` de
   `FlowTexts.resx`) pide punto, cuando en es-AR va coma y el formulario ya la acepta. Como el texto
   queda sellado en el JSON de cada Flow, el nuevo va en una revisión nueva del perfil de Flows, con su
   fixture, y los Flows ya publicados conservan el de hoy. Además, el id del botón de las invitaciones
   nuevas lleva el idioma, para que el texto con el enlace salga en el de la invitación sin leer cuentas
   ([ADR 0011](../decisions/0011-separacion-acceso-y-atencion-whatsapp.md)); los botones ya enviados se
   siguen aceptando. Chico.

**Hecha el 2026-11-13:** H17 en 64d878a0, H22 en ac5403bc, H15 en 54b213a4 y H18 en 5372b7b4 (con c0caf0ad). H18 pide en
la ayuda decimal, además, no separar los miles en los dos idiomas, porque el codec toma cualquiera de las dos marcas como la
decimal. Queda para la prueba integral comprobar con Meta que el celular y la vista oficial aceptan la coma con r15. La suite
completa con Docker sobre una copia de c0caf0ad dio 10.569 de 10.582, con el build sin advertencias; las 13 fallas son ajenas: 12
ya fallaban en be294cdb (`FormSpreadsheetPhysicalLimitsTests`, cuyo fixture no siguió a c9db1571, y una de planillas de la IA) y
una de cargas por WhatsApp pasa sola.

### Etapa 2: acceso y abuso

5. **H4. «Probar» con los permisos de quien prueba.** El contexto de «Probar» exige también
   `whatsapp.read` para leer el historial real; sin lectura de registros, la prueba no devuelve motivo
   ni campos, y el correo se busca y se muestra sólo con `users.read`. El front muestra la prueba sin
   esos datos. Test: buscar por correo con un rol sin `users.read`. Chico.
6. **H20. «Crear» revisa el vínculo antes de crear.** Antes de crear el borrador se comparan el modo, la
   cuenta y la revisión del vínculo, y se repite en la transacción, como en los botones de los casos.
   Test: un toque en «Crear» después de desvincular el número. Chico.
7. **H10. El cupo de códigos.** Un tope diario por cuenta para los códigos del perfil (las filas ya
   guardan quién los pidió) y, con el registro abierto, un tope diario por IP en el pedido anónimo.
   El pedido que cruza el 80 % del cupo diario deja un aviso en el log. Chico.

**Hecha el 2026-11-13:** H20 en c7798d5d, H4 en 61de6935 (front b2ea62b7) y H10 en ac5b5ca8. La suite completa con Docker
sobre una copia de ac5b5ca8 dio 10.613 de 10.626, con el build sin advertencias; las 13 fallas son ajenas: las 12 de la etapa
1 y una de formularios con condiciones que pasa sola (un timeout de Redis al sembrar, con la máquina cargada). En el front,
tsc y oxlint sin errores y vitest del área de WhatsApp en verde (925 tests en 62 archivos, en tandas).

### Etapa 3: el chat no se traba y sale en orden

8. **H2. Revisa cualquiera con `whatsapp.send`.** Una ruta nueva revisa un envío incierto por el id de
   la salida, porque el `requestId` es de cada operador. Va con su test y en
   `ExplicitRouteInventoryTests`, y la de hoy, que recibe el `requestId` de quien envió, se queda. Quién
   y cuándo se guardan en `DeliveryReviewedByUserId` y `DeliveryReviewedAtUtc`, como en la revisión de
   los avisos; hoy `Review()` no los llena. Revisar no reenvía ni afirma que llegó, y la consulta por
   `requestId` sigue siendo de quien envió. El front ofrece «Revisar» en los inciertos de cualquier
   operador y muestra quién lo revisó. Test: dos operadores con un envío incierto en el mismo chat.
   Chico.
9. **H6. Las acciones de una respuesta salen en orden.** Cada acción suma su lugar, en milisegundos, a
   `RequestedAtUtc`, como ya hace `SkipRequestedAt`. Test con PostgreSQL: una respuesta de tres
   acciones sale en orden. Con el mismo andamiaje va una prueba pendiente de B1, fuera de los
   hallazgos: despachar de punta a punta una pregunta de caso de un botón y otra de tres. Chico.
10. **H12 y la prioridad de H31: cómo se elige el próximo chat.** Van juntos porque cambian la misma
    consulta (`GetNextPendingAsync`):
    - se saltean los contactos con una salida en curso o en su espera de 6 s, con la hora del último
      envío guardada en PostgreSQL. Es la misma condición que pide H23: queda programada acá, y su
      prueba con dos hosts va con varias copias;
    - entre los chats que pueden salir, primero los que tienen una respuesta pendiente (del operador,
      del asistente o la confirmación de un toque) y después los avisos de las reglas. Avisos y
      confirmaciones comparten el origen `FormAction`, así que la prioridad lleva su propia marca en la
      fila, con su índice;
    - dentro de un chat se mantiene el orden.

    Tests con PostgreSQL: con un aviso a un rol grande en la fila, la respuesta de un operador a otro
    chat sale antes; dos mensajes a una misma persona no frenan a las demás. La FIFO única figura dos
    veces en [WhatsApp](../features/whatsapp.md) y en los `AGENTS.md` del módulo en Application e
    Infrastructure. Mediano.
11. **H8. Apagar sin cortar envíos.** Con el apagado pedido, no se toma trabajo nuevo, la llamada en
    curso termina con su plazo y se confirma con un token acotado en todos los orígenes, como ya hacen
    los Flows. Lo que esperaba su turno para escribirle a esa persona vuelve a pendiente sin llamar a
    Meta: para el chat manual es una transición nueva del dominio, que después usa la tarea 13. Un
    código que esperaba su reintento no queda tomado por la copia que se apaga. Test: apagar la copia
    en medio de un envío manual. Chico.

**Hecha el 2026-11-13:** H2 en 3e750848 (front a202c056), H6 en 7bf19995, H12 y H31 en b3c2fb5e y H8 en 79925e34. La suite
completa con Docker sobre una copia de 79925e34 dio 10.643 de 10.645, con el build sin advertencias: las dos fallas eran tests
de migración que la migración de H12 rompía (después de un downgrade rechazado leían columnas posteriores), arreglados en
4d7f1e9b y corridos solos en verde. Desde acá no hay suite completa por etapa: la corre la coordinadora una vez, al final de la tanda. Quedan como avisos los
mensajes de BusinessForms que contestan por el canal neutral (ver H31 en la revisión).

### Etapa 4: Meta y Redis fallan sin perder mensajes

12. **H7. «No salió» o «no se sabe».** El cliente de mensajes separa los dos casos:
    - «No salió»: 131056, 130429, circuito abierto o límite, error de conexión, DNS o TLS, y un 5xx con
      un código que Meta manda reintentar (131000, 131016). Se reintenta con espera.
    - «No se sabe»: timeout, respuesta cortada o 5xx sin código. Queda incierto.

    En la cola de acceso, un timeout ya no se manda hasta tres veces, y una invitación incierta deja de
    figurar como fallida: es un estado nuevo, revisado con el front, que el administrador ve antes de
    reenviar. En la salida durable, una columna de próximo intento, que entra en la consulta de la
    tarea 10: un chat cuya primera salida espera su próximo intento no sale, para no romper el orden.
    Cambian los tests que fijan lo de hoy (`Network_failures_and_timeouts_are_transient` y
    `Transient_failures_wait_the_configured_delay_before_retrying`). Chico en la cola de acceso;
    mediano en la salida durable.
13. **H3. Con Redis caído no se pierden mensajes.** Si no se llamó al proveedor, la salida vuelve a
    pendiente con una espera, con la columna de la tarea 12. El dominio ya sabe devolver la reserva de
    avisos y asistente; la del chat manual la suma la tarea 11. La cola de acceso reintenta el turno en
    vez de descartar el mensaje. Test: apagar Redis con mensajes en cola. Mediano; depende de las tareas
    11 y 12.
14. **H11. Lo recibido con archivos aguanta una caída larga.** La ventana de reintentos pasa de unos 15
    minutos a un día en los cuatro casos: respuestas de Flow, audios, planillas y documentos recibidos.
    Después de la escala de hoy, la espera sigue creciendo hasta una hora entre intentos, para no volver
    a Meta en cada vuelta del procesador; la respuesta cifrada ya queda guardada. La regla de WhatsApp
    pasa a decir un día. El cliente de medios separa 401 y 403; el aviso a `/health` es de la tarea 16.
    Chico.
15. **H33. «Enviar» un formulario no consulta a Meta.** La consulta de los números de la cuenta sale del
    pedido y de la propuesta (`WhatsAppFormSendPolicy`), que comparan el manifiesto con la cuenta
    configurada sin llamar a Meta; queda sólo la del worker, que está documentada. Chico.
16. **H9. Un envío fallido dice por qué.**
    - Un código estable por tipo de rechazo, declarado en Domain y traducido, con su campo en el DTO y
      su texto en el front, que hoy no usa el `errorCode` de los estados.
    - El log lleva el id del envío, el código de error de Meta y la excepción, también cuando algo
      explota antes de llamar.
    - Todos los clientes de Meta avisan a `/health` (`WhatsAppHealth`) cuando el token falla, y una
      llamada que vuelve a andar lo limpia; hoy sólo lo limpia un envío aceptado.
    - Métricas de pendientes, inciertos y fallidos con su `AddMeter`, como la IA.

    Chico a mediano. Va después de las tareas 12 y 14: los códigos dependen de qué se reintenta, y el
    aviso de medios, de la separación de 401 y 403.

**Hecha el 2026-11-13:** H7 en 83e53357 (front 85fa7666), H11 en 4f2df684, H33 en 2232dfe7, H3 en 8303e7d4 y H9 en 0097643b
(front 9cb1f971). Sin suite completa por etapa (la corre la coordinadora al final): el build sin advertencias,
Domain, Application y ArchitectureTests enteros y la integración de cada cambio; en el front, tsc, oxlint y vitest del área.
Quedan para el reporte final: `WhatsAppAssistantSpreadsheetLoadTests.The_flow_that_follows_the_location_request_of_a_load_carries_its_footer`
falla a veces bajo carga (cargas a 1 µs y «Seguir sin ubicación» a 1 ms), y con `MessageRetentionDays` en 1 un archivo que
reintenta sale de la retención antes de abandonarse.

### Etapa 5: el asistente y los toques

17. **H5. Una falla del motor deriva.** Cuando un pedido automático termina bloqueado, porque el motor
    falló o venció sin respuesta, se deriva con `WhatsAppAssistantHandoffPolicy` en la misma unidad, con
    un motivo y un código estable nuevos («El asistente no pudo responder»). Sale el mensaje fijo de
    derivar si el agente lo tiene y la ventana de 24 horas sigue abierta. Lo pendiente o reintentable no
    deriva. Test: el motor falla en modo Asistente y el chat queda en Atención manual, con la marca y el
    mensaje. Chico a mediano.
18. **H21. El mismo toque, la misma respuesta.** El puerto `IStructuredRecordActionReplySink` recibe el
    id del toque. El dueño lo guarda con su decisión y, ante el mismo toque, contesta Applied con el
    mismo texto; un segundo toque, con otro id, se sigue ignorando. Cada confirmación se encola apenas
    vuelve su dueño, sin esperar a los otros toques de la vuelta. Test: un corte entre la decisión del
    dueño y la confirmación. Chico.

**Hecha el 2026-11-14:** H5 en 8c357060 (front 7b95095b) y H21 en 852857fa, cada una con su revisión adversarial
aplicada: H5 también deriva el pedido que vence la ventana esperando al motor, y H21 corta la vuelta en el toque que falla.
Se corrió el build sin advertencias, Domain, Application y ArchitectureTests enteros y la integración del asistente y de los
toques; en el front, tsc, oxlint y vitest de WhatsApp. Quedan para el reporte final un sticker después del pedido, que evita
derivar, el `NotFound` de `Submit` en una continuación y el botón viejo después de «Volver a probar».

### Etapa 6: datos y privacidad

19. **H13. La ubicación y el texto del envío vencen.** La ubicación que guarda el envío y su texto
    (`FlowBody`) se vacían al capturar o al vencer, por lotes dentro de una unidad (la sesión es
    auditable y no admite `ExecuteUpdate`), como ya hace W3 con lo compartido. Al vencer se espera a que
    no quede una respuesta pendiente (el predicado de `WhatsAppFlowSessionQuery`), porque la captura lee
    esa ubicación. Se conserva el estado de la ubicación. Chico.
20. **H16. El contenido del chat se cifra, con su ADR.**
    - **El ADR:** la amenaza es una copia de la base, como en el
      [ADR 0015](../decisions/0015-colas-transaccionales-cifradas.md). Las claves de Data Protection
      viven en la base, pero un despliegue exige protegerlas con un certificado externo; por eso una
      copia sola no alcanza para leer. El ADR enumera las columnas.
    - **Qué se cifra:** todo el contenido, en todas las tablas del módulo. Por ejemplo, los cuerpos de
      mensajes y salidas (textos y transcripciones), las planillas, las ubicaciones, los contactos
      compartidos, lo leído de documentos, los títulos de los botones, lo que guarda el asistente y el
      texto del envío. El nombre y el número de cada chat quedan en claro para la bandeja.
    - **Lo que cambia:** las consultas que preguntan si el cuerpo está vacío pasan a guardar `null` en
      lugar de vacío. La migración cambia el tipo de esas columnas y deja vacío lo que ya hay, como la
      retención a los 90 días: los chats y sus mensajes siguen, sin el texto. Hoy sólo hay datos de
      desarrollo (decidido el 2026-11-13), y el traspaso al multitenant avisa que una instalación con
      chats reales necesita convertirlos.
    - **Tests:** lo guardado no se lee en SQL, el chat se lee igual, y la retención y la búsqueda siguen
      andando.

    Mediano. Va después de la tarea 19, que define cuándo se vacían la ubicación y el texto del envío.
21. **H19. Quién cambió el modo, y una traza.** Se guarda quién cambió el modo y cuándo, también al
    derivar; no sirve `IAuditable`, porque cada mensaje entrante modifica el contacto. Además, la fila
    de cada mensaje y de cada salida guarda el contexto de traza, y un `ActivitySource` del módulo une
    el webhook, el worker y Meta. Chico la auditoría; mediano la traza.

### Etapa 7: despliegues sin romper

22. **H25. La versión vieja deja lo que no entiende.** Una copia deja para otra, con un tope, lo que su
    versión no sabe leer:
    - una respuesta de Flow de un formulario que no entiende queda pendiente, sin cerrar la sesión;
    - un formulario que otra versión dejó listo con otro perfil de Flows no se publica ni queda
      fallido: espera a una copia que lo sepa compilar;
    - una salida de un tipo desconocido vuelve a pendiente, en lugar de salir como texto o quedar
      fallida;
    - un mensaje de un tipo nuevo no rompe el historial;
    - una entrega de acceso que no se decodifica no se borra.

    Además, la [guía de despliegue](../guides/despliegue.md) y la de [migraciones](../guides/migracion.md)
    piden escribir cada campo o valor nuevo un despliegue después del que ya sabe leerlo. Test: una fila
    con un tipo o un campo que la versión no conoce. Chico a mediano.

**Cierre.** Cada regla que cambió queda en [WhatsApp](../features/whatsapp.md), la de H16 también en su
ADR, y la revisión anota qué commit arregló cada hallazgo. Las decisiones van a las fuentes de las
[premisas](../architecture/premisas-de-la-plantilla.md) y la sección se retira con la
[auditoría documental](../guides/auditar-documentacion.md). Lo programado entra en el próximo traspaso
al multitenant, que avisa el paso de datos de H16.
