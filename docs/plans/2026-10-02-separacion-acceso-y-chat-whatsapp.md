# Separar el acceso al sistema del chat de WhatsApp

Fecha: **2026-10-02**. Estado: **implementado localmente; prueba con teléfono real pendiente**.
Alcance: `ArquitecturaBase` y `../ArquitecturaBaseFront`.

El usuario aprobó separar el ingreso por WhatsApp de la atención del negocio y pidió un plan detallado antes de programar. Este documento conserva el alcance aprobado y su trazabilidad. Las decisiones implementadas están consolidadas en el ADR 0011 y las fuentes funcionales; no acredita despliegue ni prueba con teléfono real. Las reglas actuales siguen en [WhatsApp](../features/whatsapp.md) e [identidad](../features/identidad.md); la arquitectura vigente es [backend.md](../architecture/backend.md).

## Estado de implementación

- Bot anterior retirado, códigos y registro explícitos conservados, invitación con orientación pública.
- Propósito/evento y migración Manual implementados; historia, lecturas, receipts y operaciones conservados.
- Presentación integrada en la maqueta existente por indicación del usuario: conserva MessageBubble,
  ChatTimeline y los tokens que ajusta otra tarea. No hay una segunda implementación del historial.
- El refactor concurrente ya había separado ChatTimeline y lib/timeline: se amplió ese adaptador;
  no se duplicaron ChatMessage/MessageDelivery. Pruebas de eventos viven en ChatTimeline.test.tsx.
- Tests del bot sustituidos por clasificación y procesamiento; enlaces históricos preparados por
  TestLoginLinks y fixtures explícitos. El arnés de lock de cuenta del bot se retiró.
- Prueba con teléfono y payload externo pendientes en verificacion-y-produccion.md.
- Este plan estaba sin versionar y no tiene copia en Git. Se conserva hasta que haya procedencia
  recuperable; la guía documental prohíbe eliminar un archivo así. Sin commits autorizados no se
  inventa un commit anterior a la limpieza.

## 1. Resultado buscado

Las secciones 1 a 11 conservan el plan aprobado antes de implementar, con nombres y tareas
propuestos. Su estado actual es el resumen anterior y la [validación registrada](../reviews/2026-10-02-separacion-acceso-whatsapp-validacion.md).
Las fuentes canónicas prevalecen; el tiempo futuro del plan no reabre el desarrollo ya realizado.

Una instalación representa un negocio y tiene un número compartido de WhatsApp. Ese número puede transportar mensajes de atención y mensajes de acceso, pero son casos de uso distintos.

| Recorrido | Quién lo inicia | Resultado |
| --- | --- | --- |
| Atención del negocio | Una persona escribe un mensaje común | El operador lo ve y puede contestar. No se busca una cuenta para decidir una respuesta de ingreso. |
| Ingreso por WhatsApp | La persona pide un código desde `/login` | Recibe el código y lo verifica en la web. La sesión se abre allí después de comprobar identidad. |
| Registro | La persona usa `/registro` y confirma el código | Se aplican las reglas vigentes de registro abierto/invitación. El chat no crea cuentas. |
| Verificar teléfono | Una persona autenticada lo solicita desde Mi perfil | Se envía y verifica un código con propósito de vinculación, independiente del de ingreso. |
| Invitación | Un administrador invita una cuenta | Se conserva la invitación. Su botón conduce al recorrido explícito de acceso descrito abajo. |
| IA asistida, futura | Un operador pide una propuesta | Se usa contexto del negocio y se devuelve un borrador. La generación no envía WhatsApp. |

**Contacto no equivale a usuario del sistema.** El contacto identifica a quien escribe. La cuenta tiene credenciales, roles y permisos. Un contacto puede existir sin cuenta; el vínculo existente con una cuenta se conserva y solo cambia mediante los recorridos autorizados del perfil/administración.

Los permisos actuales se conservan: `whatsapp.read`, `whatsapp.send` y `whatsapp.manage`. Pedir un código desde el login sigue siendo un recorrido público protegido por sus controles propios, no una acción que requiere permisos de operador.

## 2. Diagnóstico comprobado del código actual

| Pieza actual | Responsabilidad y problema |
| --- | --- |
| `WhatsAppReplyPolicy.DecideAsync` | Busca la cuenta del número. Si está activa, emite un enlace ante cualquier mensaje, incluido «Hola», una foto o un audio. |
| `WhatsAppLinkIssuer` | Además de emitir enlaces, vincula contactos, puede confirmar teléfonos y puede crear cuentas desde el chat. No alcanza con cambiar el texto de una respuesta. |
| `WhatsAppInboundService` | Marca entradas procesadas, consulta Bot/Manual y coordina el bot de ingreso. Manual actualmente también detiene respuestas de acceso por botones. |
| `WhatsAppInvitationChannel` | Encola la invitación con el payload `WANT_TO_ENTER`. El recorrido actual espera que el bot le devuelva un enlace de un solo uso. |
| `WhatsAppCodeIssuer` | Ya distingue ingreso y verificación en los códigos del núcleo, pero usa el mismo tipo de mensaje de transporte para ambos. |
| `WhatsAppDeliveryService` | Guarda resúmenes seguros de salidas: los códigos y las URL secretas no se guardan. El historial todavía no tiene propósito explícito. |
| `WhatsAppContact` y su configuración EF | Las conversaciones nuevas comienzan en Bot. Cambiar solo la fábrica no modifica los chats existentes ni el default de la base. |
| `ChatThread.tsx` | Presenta el modo como «Bot activo». Hoy ese botón activa el bot de ingreso, aunque la pantalla se presenta como atención del negocio. |

La recepción real fue confirmada por el usuario. Sus capturas muestran también una respuesta manual con estado Entregado. Esto no acredita todavía una prueba completa de la separación propuesta ni reemplaza las pruebas de regresión.

## 3. Decisiones propuestas para el refactor

### 3.1. El acceso se solicita explícitamente

El recorrido principal reutiliza el login por código que ya existe:

```text
/login → elegir WhatsApp → ingresar teléfono → pedir código
        → recibirlo en WhatsApp → ingresarlo en la web
        → verificar código/cuenta → abrir sesión después del commit
```

No se modifica el algoritmo de códigos, los límites, el propósito de verificación, OIDC/PKCE, cookies, roles ni la política de registro. El webhook y el procesador de entrantes no crean cuentas, no confirman teléfonos, no emiten enlaces secretos y no abren sesiones.

### 3.2. Las invitaciones conservan un recorrido completo

Para conservar el botón de la plantilla existente sin depender del bot anterior:

```text
Admin envía invitación → persona toca «Quiero entrar»
→ respuesta de acceso con botón a la URL pública /login
→ la persona pide y verifica el código en la web
```

El botón sigue usando `WANT_TO_ENTER`. Se reconoce el identificador estructurado de un botón recibido, no su título ni una frase de texto libre. La URL pública no contiene un token ni autentica a nadie. No se necesita consultar la cuenta para entregar esa orientación; la web comprueba identidad y estado de cuenta.

Es una modificación deliberada de experiencia: la invitación deja de producir acceso por enlace de un solo uso y pasa a usar el mismo login por código. Agrega ese paso de verificación y elimina un segundo mecanismo de ingreso disparado desde la conversación.

También se contemplan botones enviados antes del cambio:

| Payload recibido en un botón | Tratamiento propuesto |
| --- | --- |
| `WANT_TO_ENTER` | Orientación a `/login`. |
| `HAVE_ACCOUNT` | Orientación a `/login`. |
| `CREATE_ACCOUNT` | Orientación a `/registro`; la web decide si el registro está permitido. No crea una cuenta desde WhatsApp. |
| `DID_NOT_REQUEST_CODE` | Registrar el evento de acceso y procesarlo sin responder. Se conserva el vencimiento y los controles actuales del código. |
| Identificador desconocido | Mensaje del negocio; no entra al recorrido de autenticación. |
| Texto libre «Quiero entrar» o `WANT_TO_ENTER` | Mensaje del negocio. No se infiere una solicitud de ingreso a partir del cuerpo. |

Las respuestas públicas usan el idioma general configurado de la instalación; los códigos e invitaciones mantienen el idioma que ya resuelve su recorrido. No se agrega una búsqueda de cuentas solo para elegir el idioma de una orientación.

Se conserva la capacidad del núcleo de previsualizar/canjear enlaces ya emitidos, con sus controles y vencimiento. No se borran sus tablas o endpoints en este refactor. El retiro de esa capacidad, si se solicita, sería otro desarrollo después de relevar sus consumidores.

### 3.3. El modo de atención pertenece al negocio

- Los chats nuevos empiezan en Manual tanto en Domain como en EF.
- La migración convierte a Manual los chats existentes en Bot e incrementa su revisión. La respuesta de ingreso deja de existir también para esos contactos.
- El botón «Activar bot» deja de ofrecerse mientras esta instalación no tenga automatización del negocio implementada. Las sugerencias futuras del LLM no requieren activar un bot.
- Se conserva por compatibilidad el enum HTTP `Bot`/`Manual` y la ruta de cambio de modo. `Bot` ya no dispara ninguna acción de acceso.
- Si un cliente anterior o un pedido externo deja un contacto en Bot, no se ejecuta ninguna respuesta automática de negocio en esta entrega. La UI lo indica como «Automatización sin configurar» y permite volver a Manual con el permiso existente. No muestra «Bot activo» ni habilita envíos manuales saltándose el modo.
- La respuesta de orientación a un botón de acceso y el envío de códigos/invitaciones funcionan en Manual. No pasan por el control de automatización del negocio.

Esta entrega no introduce un motor ficticio de IA, un proveedor de automatización vacío ni un interruptor que silenciosamente habilite el bot anterior. Una automatización real se diseñará cuando exista su caso de uso.

### 3.4. El propósito del mensaje queda registrado

Se agrega metadata explícita al mensaje, sin crear otro chat ni duplicar contactos:

| Campo | Valores propuestos | Uso |
| --- | --- | --- |
| `Purpose` | `Business`, `Access`, `Unclassified` | Distinguir atención, acceso e historia cuyo origen no puede demostrarse. |
| `AccessEventKind` | `SignInCode`, `VerifyPhoneCode`, `Invitation`, `AccessRequested`, `AccessInstructions` | Mostrar un evento traducido de acceso sin exponer la credencial. Solo se completa cuando el propósito es Access. |

La regla de Domain impide combinar Business con un evento de acceso o crear un evento Access sin subtipo. Los tipos de salida indican su propósito al construirse; no se deduce buscando palabras como «código» en el cuerpo. Los mensajes de sistema y los históricos sin evidencia suficiente quedan Unclassified.

`WhatsAppInboundPolicy` clasifica la entrada usando tipo de mensaje e identificador de botón. El webhook guarda esa clasificación; el procesador usa la misma política para seleccionar las acciones pendientes. Una entrada del negocio se guarda y procesa sin llamar a ningún servicio de identidad.

Los mensajes nuevos que responde un operador son Business. Los códigos, verificaciones, invitaciones y orientaciones de acceso son Access, con su subtipo correspondiente.

La lectura por operador conserva la secuencia de **todas** las entradas, incluidos eventos de acceso. En este refactor no se redefine el contador de sin leer ni se modifica la ventana de envío. Los eventos de acceso no se convierten en propuestas comerciales del LLM.

### 3.5. El historial muestra la diferencia

Ejemplo del mismo contacto:

```text
Cliente: «Hola, ¿tienen stock?»                         [Business]
Operador: «Sí, ¿qué producto buscás?»                   [Business]
Sistema: «Se envió un código de ingreso»                [Access]
Cliente: «Quiero entrar» [botón de una invitación]       [Access]
Sistema: «Se enviaron instrucciones para ingresar»      [Access]
```

Los eventos Access se muestran con un tratamiento visual de evento del sistema y textos es/en, junto a su fecha y estado de entrega. El listado también muestra un resumen seguro traducido, mediante metadata de la vista previa. Los mensajes comunes conservan su presentación y autor.

No se devuelve el código, token o enlace secreto en estos eventos. Para salidas Access, los DTOs exponen metadata y el resumen permitido, nunca la credencial. La protección actual de `SafeSummary` se conserva y se prueba. Esto no promete detectar cualquier secreto que una persona pegue por iniciativa propia en un texto común.

Para la futura IA, la regla de selección será **solo Business**, excluyendo Access y Unclassified. La conexión con el LLM no se implementa en este desarrollo.

## 4. Árbol previsto del backend

Leyenda: `[N]` nuevo; `[M]` modificado; `[C]` conservado, probado como regresión. Árbol acotado al módulo y sus consumidores; las carpetas restantes continúan como están. No se crean carpetas o interfaces por simetría: las responsabilidades se distinguen con servicios y políticas concretos dentro de las carpetas canónicas existentes.

```text
ArquitecturaBase/
├─ src/
│  ├─ ArquitecturaBase.Domain/
│  │  ├─ Authentication/                              [C] códigos/enlaces y reglas de identidad
│  │  └─ Modules/WhatsApp/
│  │     ├─ WhatsAppContact.cs                        [M] default Manual
│  │     ├─ WhatsAppConversationMode.cs               [C] contrato Bot/Manual
│  │     ├─ WhatsAppMessage.cs                        [M] propósito y evento; invariantes
│  │     ├─ WhatsAppMessagePurpose.cs                 [N] Business/Access/Unclassified
│  │     ├─ WhatsAppAccessEventKind.cs                [N] tipos de evento de acceso
│  │     ├─ WhatsAppOutgoingMessage.cs                [C] salida manual durable e idempotencia
│  │     ├─ WhatsAppReadState.cs                      [C] lectura por operador
│  │     ├─ WhatsAppDeliveryReceipt.cs                [C] estados tempranos de Meta
│  │     └─ WhatsAppPermissions.cs                    [C] permisos actuales
│  ├─ ArquitecturaBase.Application/
│  │  ├─ Services/Auth/                               [C] ingreso, registro, canje y sesión
│  │  ├─ Services/Users/UserInvitationIssuer.cs       [M] corregir comentario del recorrido; lógica conservada
│  │  └─ Modules/WhatsApp/
│  │     ├─ WhatsAppApplicationRegistration.cs        [M] retirar DI del bot anterior; registrar nueva política
│  │     ├─ Channels/
│  │     │  ├─ WhatsAppInvitationChannel.cs           [M] payload de acceso, no del bot del negocio
│  │     │  ├─ WhatsAppPhoneChannel.cs                [C] disponibilidad del login telefónico
│  │     │  ├─ WhatsAppPhoneLinkParticipant.cs        [C] vínculo y desvinculación autorizados
│  │     │  ├─ WhatsAppInvitationDeliveryStatusSource.cs [C] seguimiento de invitaciones
│  │     │  └─ WhatsAppPermissionCatalogSource.cs     [C] permisos del módulo
│  │     ├─ Interfaces/
│  │     │  ├─ Services/IWhatsAppInboundService.cs    [C] contrato del procesador
│  │     │  ├─ Services/IWhatsAppChatDispatchService.cs [M] quitar ShouldSendBotReplyAsync
│  │     │  ├─ Services/IWhatsAppChatService.cs       [C] contratos del chat
│  │     │  ├─ Services/IWhatsAppLoginCodeService.cs  [C] pedido explícito de código
│  │     │  └─ Persistence/                          [C] contratos existentes de lectores/repositorios
│  │     ├─ Models/
│  │     │  ├─ WhatsAppAccessActions.cs              [N] IDs actuales de botones de acceso, sin cambiar valores
│  │     │  ├─ WhatsAppOutboundMessage.cs            [M] metadata de acceso; quitar metadata del bot retirado
│  │     │  ├─ WhatsAppChatRow.cs                    [M] propósito/evento y metadata de Preview
│  │     │  ├─ WhatsAppChatResponse.cs               [M] metadata aditiva en mensajes/vista previa
│  │     │  └─ WhatsAppWebhookBatch.cs               [C] entrada técnica ya contiene tipo y ReplyId
│  │     ├─ Services/
│  │     │  ├─ WhatsAppInboundPolicy.cs              [N] clasificación determinista y selección de acciones
│  │     │  ├─ WhatsAppAccessReplyPolicy.cs          [N] orientación pública a login/registro; no autentica
│  │     │  ├─ WhatsAppWebhookPersistence.cs         [M] guardar propósito al recibir; misma idempotencia
│  │     │  ├─ WhatsAppInboundService.cs             [M] procesar entradas; acceso explícito separado del modo
│  │     │  ├─ WhatsAppCodeIssuer.cs                 [M] trasladar SignIn/VerifyDestination a metadata de salida
│  │     │  ├─ WhatsAppDeliveryService.cs            [M] persistir metadata y mantener resumen seguro
│  │     │  ├─ WhatsAppChatService.cs                [M] mapear metadata a respuestas; envío/permisos conservados
│  │     │  ├─ WhatsAppChatDispatchService.cs        [M] salida manual Business; retirar chequeo del bot de ingreso
│  │     │  ├─ WhatsAppLoginCodeService.cs           [C] límite transaccional del pedido de código
│  │     │  ├─ ProfileWhatsAppService.cs             [C] código de vinculación y confirmación
│  │     │  ├─ WhatsAppContactLinker.cs              [C] vínculo de contacto con cuenta
│  │     │  └─ WhatsAppCodeQuotaGuard.cs             [C] cuota de códigos
│  │     └─ Resources/
│  │        ├─ Access.resx / Access.en.resx           [N] textos de orientación, separados del negocio
│  │        ├─ AccessTexts.cs                        [N] acceso tipado al recurso
│  │        └─ Permissions.resx / Permissions.en.resx [C] traducciones de permisos
│  ├─ ArquitecturaBase.Infrastructure/
│  │  ├─ Modules/WhatsApp/
│  │  │  ├─ WhatsAppInfrastructureRegistration.cs    [M] corregir dependencia/documentación del procesador y origen
│  │  │  ├─ WhatsAppInboundProcessor.cs             [M] comentarios/logs de procesamiento; coordinación conservada
│  │  │  ├─ WhatsAppSenderBackgroundService.cs       [M] retirar control del bot anterior; transporte conservado
│  │  │  ├─ WhatsAppOutgoingBackgroundService.cs     [C] worker de salidas manuales
│  │  │  ├─ WhatsAppDispatchCoordinator.cs           [C] exclusión por destino entre workers
│  │  │  ├─ WhatsAppCloudClient.cs                   [C] integración Meta y plantillas existentes
│  │  │  ├─ WhatsAppWebhookReader.cs                 [C] parsear botones y texto sin inferir intención
│  │  │  ├─ WhatsAppSignatureValidator.cs            [C] firma del webhook
│  │  │  ├─ WhatsAppOptions.cs / WhatsAppOptionsValidator.cs [C] opciones y validación actuales
│  │  │  └─ Persistence/
│  │  │     ├─ Configurations/WhatsAppContactConfiguration.cs [M] default Manual en EF
│  │  │     ├─ Configurations/WhatsAppMessageConfiguration.cs [M] persistir enums y nulabilidad coherente
│  │  │     ├─ Readers/WhatsAppChatReader.cs          [M] proyecciones de propósito/evento y vista previa
│  │  │     └─ Repositories/                        [C] locks, recepción, estados y retención existentes
│  │  └─ Persistence/Migrations/
│  │     ├─ <fecha>_SeparateWhatsAppAccessAndBusiness.cs [N] datos y defaults; fecha real al generar
│  │     ├─ <fecha>_SeparateWhatsAppAccessAndBusiness.Designer.cs [N] modelo de la migración
│  │     └─ ApplicationDbContextModelSnapshot.cs     [M] snapshot generado con todos los cambios previos
│  └─ ArquitecturaBase.Api/
│     ├─ Controllers/LoginCodeController.cs          [C] verificación de códigos del núcleo
│     ├─ Controllers/LoginLinkController.cs          [C] preview y canje de enlaces pendientes
│     ├─ Controllers/ConnectController.cs            [C] OIDC, autorización y tokens
│     └─ Modules/WhatsApp/
│        ├─ Controllers/WhatsAppWebhookController.cs [C] webhook, firma y respuestas técnicas
│        ├─ Controllers/WhatsAppLoginCodeController.cs [C] solicitud pública de código
│        ├─ Controllers/WhatsAppChatsController.cs   [C] rutas, permisos y rate limits del chat
│        ├─ Controllers/MeWhatsAppController.cs     [C] confirmación explícita del teléfono
│        ├─ Contracts/                             [C] pedidos HTTP actuales
│        └─ WhatsAppApiRegistration.cs              [C] composición del módulo
└─ docs/                                           decisiones e instrucciones: sección 9
```

Las etiquetas C son importantes para las regresiones, no una promesa de que ningún comentario o doble de test pueda requerir ajuste al compilar. La implementación debe revisar referencias de cada símbolo retirado y ampliar el inventario si aparece otro consumidor real.

### Código que se retira, con reemplazo identificado

| Archivo actual | Destino de su responsabilidad |
| --- | --- |
| `Services/WhatsAppReplyPolicy.cs` | Se elimina la búsqueda de cuenta/ingreso implícito. La orientación explícita queda en `WhatsAppAccessReplyPolicy`. |
| `Services/WhatsAppLinkIssuer.cs` | Se retiran creación, confirmación y emisión desde el chat. Registro/verificación continúan en los recorridos existentes del núcleo y perfil. |
| `Services/BotReply.cs` | Se retiran saludos/alta/enlaces automáticos. Los pocos textos de orientación quedan en Access y su política. |
| `Models/BotButtons.cs` | Sustituido por `WhatsAppAccessActions.cs`, manteniendo los IDs externos existentes. |
| `Resources/BotTexts.cs`, `Bot.resx`, `Bot.en.resx` | Sustituidos por recursos de acceso que solo contienen textos usados por el recorrido nuevo. |

No se dejan el bot anterior registrado, una bandera para ocultarlo ni una segunda política equivalente. Se borran sus consumidores y dependencias después de cubrir los comportamientos válidos de reemplazo.

## 5. Árbol previsto del front

```text
ArquitecturaBaseFront/
├─ src/
│  ├─ features/
│  │  ├─ auth/
│  │  │  ├─ pages/LoginPage.tsx                       [C] ingreso/registro explícitos
│  │  │  ├─ pages/LoginCodePage.tsx                   [C] verificación del código
│  │  │  ├─ pages/LoginLinkPage.tsx                   [C] enlaces ya emitidos y controles actuales
│  │  │  ├─ components/WhatsAppCodeForm.tsx           [C] pedir código por teléfono
│  │  │  ├─ api/loginCode.ts / api/loginLink.ts       [C] transporte de acceso existente
│  │  │  └─ AGENTS.md / CLAUDE.md                    [M] recorrido de invitaciones y separación
│  │  └─ whatsapp/
│  │     ├─ api/chat.ts                              [M] tipos de Purpose/AccessEventKind y Preview
│  │     ├─ components/ChatThread.tsx                 [M] atención manual y eventos; no activar bot de ingreso
│  │     ├─ components/ChatMessage.tsx                [N] presentación de mensaje común o evento de acceso
│  │     ├─ components/ChatComposer.tsx               [C] idempotencia, borrador y restricciones existentes
│  │     ├─ hooks/useChatThread.ts                    [C] polling, historial y lectura por operador
│  │     ├─ lib/history.ts                           [C] merge de páginas y estados
│  │     ├─ lib/messagePresentation.ts               [N] resolver resumen traducido para hilo y listado
│  │     ├─ pages/WhatsAppChatPage.tsx                [M] vista previa de acceso segura y traducida
│  │     └─ AGENTS.md / CLAUDE.md                    [M] atención y acceso como responsabilidades distintas
│  ├─ locales/
│  │  ├─ es/whatsapp.json                            [M] eventos y estado sin automatización
│  │  └─ en/whatsapp.json                            [M] equivalentes en inglés
│  └─ test/visual/
│     ├─ fixtures.ts                                [M] conversación mixta Business/Access/Unclassified
│     └─ whatsapp.spec.ts                           [M] desktop/móvil, eventos y controles disponibles
└─ docs/
   ├─ design/visual-baseline.md                       [M] aprobar y documentar presentación de eventos
   └─ mapa-de-instrucciones.md                        [M] responsabilidad actualizada de la feature
```

`ChatMessage.tsx` concentra fecha, autor, estado y tratamiento del evento. `messagePresentation.ts` comparte únicamente la interpretación de metadata entre el listado y el hilo; no conecta con auth ni resuelve permisos o envío. Auth y WhatsApp siguen siendo features independientes.

Antes de programar esos ajustes se presenta la variante visual con las piezas existentes del sistema y sus estados de error/vacío/carga, siguiendo [visual-baseline](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md). No se agrega una pantalla o sección de configuración nueva. Se preservan borradores, posición del historial, teclado, móvil y mensajes del sistema común.

## 6. Persistencia, historia y compatibilidad

La migración **es nueva**. No se reescribe `WhatsAppOperatorInbox` ni ninguna migración que ya pudo aplicarse. Se genera desde el snapshot real presente al desarrollar, incluyendo migraciones de otros cambios de la plantilla.

| Cambio | Tratamiento |
| --- | --- |
| Default de modo | Manual en Domain y base. |
| Contactos Bot existentes | UPDATE a Manual con aumento de ModeRevision, sin modificar vínculo, perfil, fecha, secuencia o lectura. |
| Columnas de mensaje | Agregar Purpose y AccessEventKind sin borrar cuerpos, IDs, estados ni autor. |
| Historia con operador identificado | Las salidas con OperatorUserId pueden clasificarse como Business por evidencia estructurada. |
| Historia restante | Unclassified. No se busca «Entrar»/«código» en textos para decidir origen; se conserva sin incluirla automáticamente en contexto futuro del LLM. |
| Pendientes de procesamiento previos | Clasificar por metadata estructurada al procesar. Texto común: sin respuesta automática; botón de acceso conocido: orientación pública. |
| Salidas manuales Pending/Sending/Uncertain | Se conservan estados, requestId, autor y texto. No se reenvían por ejecutar la migración. |
| Enlaces de ingreso ya emitidos | Mantener verificación y expiración del núcleo; no invalidarlos arbitrariamente ni emitir otros desde el chat. |
| Salidas Access sin contacto | Conservar el caso existente: pedir un código no exige haber escrito antes al número. No crear chats artificialmente desde el login. |

El hilo y su cursor siguen paginando el historial completo; no se filtran Access/Unclassified en esa consulta. Los nuevos metadatos son aditivos en JSON. El front trata una respuesta sin metadata durante una transición como Unclassified, sin inferir secretos ni propósito.

Se conservan verbos, rutas, enums actuales, permisos y códigos de estado. La eliminación de una ruta/valor aceptado o cambio de contrato obligatorio no está autorizada por este plan: si aparece necesaria, se documenta y aplica la decisión de versionado del [ADR 0005](../decisions/0005-sin-versionado-de-api-por-ahora.md) antes de ejecutarla.

La migración de datos a Manual no tiene una reversión automática fiel a los modos anteriores sin un respaldo. Un Down de esquema no debe poner todos los chats en Bot. El despliegue y cualquier vuelta atrás tienen que considerar tanto los datos como el código.

## 7. Transacciones, colas y concurrencia que se conservan

- Webhook: firma y parseo técnico; persistencia del lote en el límite actual; sin llamadas a Meta ni a identidad al recibir.
- Procesamiento: lock del contacto, selección de entradas pendientes y un solo `ExecuteInTransactionAsync`. Las políticas no abren otro límite ni guardan. Los mensajes del negocio se marcan procesados aunque nadie los haya leído; procesado y leído son conceptos distintos.
- Acceso explícito por botones: como máximo una orientación seleccionada por contacto/lote procesado, elegida entre acciones de acceso vigentes; un mensaje comercial más nuevo no la reemplaza. Un botón repetido con el mismo wamid no origina otro envío. Una acción de acceso vieja conserva los controles de ventana actuales.
- Fallo al encolar la orientación: no marcarla definitivamente procesada sin su respuesta; rollback del límite y recuperación por el procesador. Se mantiene la semántica existente de esa cola, sin afirmar persistencia durable.
- Códigos e invitaciones: se conservan su cola en memoria y sus garantías actuales. Este plan no convierte esa cola en un outbox; tampoco presenta un 202 como entrega confirmada.
- Manual: se conserva la salida durable, reserva, comprobación actual de permisos, llamada externa fuera de transacción, resultado incierto y revisión humana. No hay reenvío automático nuevo.
- Ambos workers siguen coordinándose por destino mediante la lease Redis existente. La orientación de acceso no saltea la exclusión frente a una respuesta manual.
- Estados de Meta, receipts tempranos, historial y retención mantienen su mecanismo. La metadata no provoca una segunda fila por mensaje.
- Retirar la dependencia de cuenta del procesador no habilita locks en otro orden. Perfil, administración y códigos mantienen su coordinación actual.

Antes de instalar la versión nueva se detienen los procesos anteriores de la aplicación de forma controlada: una cola vieja puede contener respuestas del bot y un POST ya iniciado no puede deshacerse. No se borran colas/datos manuales persistentes. No se propone ejecutar el bot viejo y el nuevo procesador simultáneamente durante la transición.

## 8. Archivos y matriz de pruebas

### Backend

Rutas de pruebas relativas a cada proyecto; todo lo específico sigue dentro de `Modules/WhatsApp`.

| Proyecto / archivo | Trabajo y comprobación |
| --- | --- |
| Domain.UnitTests / `WhatsAppMessageTests.cs` | Propósito/subtipo coherentes, metadatos al construir y estados de entrega conservados. |
| Domain.UnitTests / `WhatsAppContactTests.cs`, `WhatsAppOperatorChatTests.cs` | Default Manual y revisión/lectura/idempotencia existentes. |
| Application.UnitTests / Services / `WhatsAppInboundPolicyTests.cs` **nuevo** | Texto, audio, foto y botón desconocido son negocio; acciones estructuradas conocidas son acceso; un texto igual al payload no lo es. |
| Application.UnitTests / Services / `WhatsAppAccessReplyPolicyTests.cs` **nuevo** | URL pública permitida, login/registro, textos es/en y ausencia de credenciales/operaciones de identidad. |
| Application.UnitTests / Services / `WhatsAppInboundServiceTests.cs` | Reescribir los casos del bot: negocio no responde; acceso funciona en Manual; lote mixto, duplicados, ventana y fallo de cola. |
| Application.UnitTests / Services / `WhatsAppWebhookPersistenceTests.cs` | Propósito persistido sin duplicar filas/secuencias. |
| Application.UnitTests / Services / `WhatsAppChatServiceTests.cs` | Mapeos de metadata, idempotencia, permisos y borradores del envío manual conservados. |
| Application.UnitTests / Services / `WhatsAppDeliveryServiceTests.cs`, `RecordOutboundWhatsAppMessageTests.cs`, `RecordUnsentWhatsAppMessageTests.cs` | Guardado seguro de Access/Business y estados tempranos. |
| Application.UnitTests / Services / `RequestWhatsAppLoginCodeServiceTests.cs`, `ProfileWhatsAppServiceTests.cs` | Códigos SignIn/VerifyDestination correctamente etiquetados; login no depende de modo del contacto. |
| Application.UnitTests / Channels / `WhatsAppInvitationChannelTests.cs` | Payload externo conservado, metadata de invitación y consentimiento. |
| Application.UnitTests / Resources / `ResourceParityTests.WhatsApp.cs` | Recursos Access es/en, sin claves del bot retirado. |
| Application.UnitTests / Services / `BotReplyTests.cs` | Retirar después de reemplazar controles de idioma y límites por los de acceso explícito. |
| Api.IntegrationTests / `WhatsAppAccessAndChatTests.cs` **nuevo** | Webhook firmado → propósito → historial; login en Manual; mensajes de cliente con/sin cuenta no crean, vinculan ni confirman cuentas. |
| Api.IntegrationTests / `WhatsAppAccessAndChatMigrationTests.cs` **nuevo** | Base anterior con historial, Bot y salidas pendientes → nueva migración; conservación de datos y defaults. |
| Api.IntegrationTests / `WhatsAppBotTests.cs` | Reemplazar por `WhatsAppInboundProcessingTests.cs`: conservar duplicados/concurrencia/locks y comprobar procesamiento sin bot de ingreso. Revisar uso de `HeldLoginLinkRepository.cs` antes de retirarlo. |
| Api.IntegrationTests / `WhatsAppChatTests.cs`, `WhatsAppChatPersistenceTests.cs` | Chat manual, recepción, estados, consulta de envío, lectura propia y cursores existentes. |
| Api.IntegrationTests / `WhatsAppLoginCodeTests.cs`, `WhatsAppRegistrationTests.cs`, `UserInvitationEndpointsTests.WhatsApp.cs` | Ingreso/registro/invitación completos sin emisión implícita desde el chat. |
| Api.IntegrationTests / `WhatsAppSenderBackgroundServiceTests.cs`, `WhatsAppLogPrivacyTests.cs`, `WhatsAppWebhookLogPrivacyTests.cs` | Transporte y logs sin código, URL secreta ni teléfono entero; acceso/manual comparten coordinación. |
| Api.IntegrationTests / `ApiFactory.WhatsApp.cs` y dobles del módulo | Nuevos mensajes/metadata; no introducir lógica de producción en los dobles. |
| ArchitectureTests / `Modules/WhatsApp/IdentityBoundaryTests.WhatsApp.cs` | Ya no exigir que el bot consulte bloqueo. Prohibir creación de cuenta, confirmación de teléfono y emisión de enlace desde el procesamiento entrante; conservar un caso de control que demuestre que el detector funciona. |
| ArchitectureTests / reglas de helpers, dependencias, transacciones, rutas y módulos | Mantener todas las convenciones; ampliar reglas cuando haga falta, no relajarlas para acomodar código. |

Se revisan además todos los constructores/call sites de `WhatsAppMessage`, `WhatsAppOutboundMessage` y botones en los tests del módulo. No se considera terminada la lista solo porque compiló un subconjunto.

### Front

| Archivo | Comprobación |
| --- | --- |
| `features/whatsapp/lib/messagePresentation.test.ts` **nuevo** | Metadata de acceso → texto traducido seguro; historia sin metadata → Unclassified. |
| `features/whatsapp/components/ChatMessage.test.tsx` **nuevo** | Evento separado de mensaje común, fecha/estado, sin credenciales ni URL secreta renderizadas. |
| `features/whatsapp/pages/WhatsAppChatPage.test.tsx` | No ofrece activar el bot anterior; modo legado permite tomar Manual; preview seguro; draft y envío incierto conservados. |
| `features/auth/pages/LoginPage.test.tsx`, `LoginCodePage.test.tsx`, `AccessFlow.test.tsx` | Código explícito y llegada desde URL pública de invitación sin perder OIDC/returnUrl o intención de registro. |
| `features/auth/pages/LoginLinkPage.test.tsx` | Enlaces pendientes mantienen preview/Continuar, limpieza del fragmento y vencimiento. |
| `test/visual/fixtures.ts`, `whatsapp.spec.ts` | Historial mixto y modos, capturas desktop/390/320, teclado y ausencia de desbordamiento. |

### Casos obligatorios de aceptación

1. Una persona sin cuenta manda «Hola»: llega una vez al chat y no recibe una respuesta de acceso ni se crea una cuenta.
2. Una persona con cuenta manda «Hola»: mismo comportamiento, sin verificarle el teléfono o emitirle un enlace.
3. Un operador responde desde Manual: envío durable, entrega/lectura según los estados reales y sin pérdida del borrador.
4. El operador deja el chat en Manual y pide un código desde `/login`: lo recibe y puede ingresar.
5. Registro permitido/prohibido y cuenta deshabilitada conservan sus decisiones del servidor.
6. Desde Mi perfil, verificar/vincular/desvincular teléfono funciona y no se confunde con ingreso.
7. Una invitación nueva o su botón histórico abre el recorrido por orientación pública y luego código; no autentica al tocar el botón.
8. Botones históricos de alta nunca crean la cuenta desde el webhook.
9. Dos operadores conservan sus propios sin leer y las restricciones de permiso.
10. Dos hosts y un acceso simultáneo al envío manual conservan coordinación; replay de webhook no duplica mensajes.
11. Migrar datos reales de ejemplo no pierde historia, secuencias, estados, lectura, vínculos ni operaciones manuales pendientes.
12. Eventos Access no exponen credenciales y la historia no se clasifica mediante palabras del cuerpo.

Los tests de backend usan PostgreSQL y Redis reales y Meta simulado. Después se hace una prueba controlada con teléfono real de recepción, respuesta manual, código en Manual e invitación. Cada evidencia se registra por separado; ver Entregado en una prueba no acredita todo el recorrido.

## 9. Documentación e instrucciones a actualizar al implementar

| Destino | Contenido |
| --- | --- |
| `docs/decisions/0011-separacion-acceso-y-atencion-whatsapp.md` **previsto** | Decisión, costo de cambiar invitaciones, alternativas y separación por propósito. Revalidar el siguiente número disponible antes de crearlo. |
| `docs/decisions/README.md` | Registrar el ADR con su estado real. |
| `docs/features/whatsapp.md` | Sustituir tabla del bot por los recorridos de negocio/acceso, modos y migración. |
| `docs/features/identidad.md` | Corregir que el emisor de enlaces era el bot retirado; conservar código, verificación y canje de enlaces existentes. |
| `docs/features/administracion.md` | Invitación WhatsApp pasa a login explícito; permisos/consentimiento conservados. |
| `docs/architecture/backend.md` | Ajustar ejemplos/comentarios de bot y pruebas del módulo; arquitectura MVC y transacciones conservadas. |
| `docs/architecture/premisas-de-la-plantilla.md` | Consolidar separación solo cuando esté implementada y comprobada. |
| `docs/architecture/mapa-de-instrucciones.md` | Actualizar responsabilidades, sin inventar ámbitos nuevos por archivo. |
| AGENTS del módulo Domain/Application/Infrastructure/Api y de sus tests | Retirar responsabilidades del bot anterior y registrar las nuevas. Sus CLAUDE mantienen `@AGENTS.md`. |
| `Application/Services/Users/AGENTS.md`, `Application/Services/Auth/AGENTS.md` | Revisar referencias a invitaciones/enlaces; modificar solo si contradicen el recorrido nuevo. |
| Front `features/auth/AGENTS.md`, `features/whatsapp/AGENTS.md` y mapa | Acceso explícito independiente del modo; eventos del historial. |
| `docs/guides/whatsapp-en-local.md`, `docs/guides/quitar-whatsapp.md` | Nueva prueba manual y nuevos archivos retirables del módulo; historia de migraciones preservada. |
| `docs/postman/` y material de operación que referencie el bot | Actualizar recorridos y assertions afectados, sin cambiar contratos por documentación. |
| `docs/plans/2026-10-01-bandeja-whatsapp.md` | Este refactor es requisito previo de las entregas restantes, sobre todo selección de contexto del LLM. |

Los specs históricos conservan su fecha y carácter de referencia; se corrigen sus referencias de vigencia sin reescribir la historia. Al cerrar, se consolidan decisiones y se retira este plan según [la guía documental](../guides/auditar-documentacion.md), después de rescatar pendientes.

## 10. Orden de ejecución y puntos de revisión

| Etapa | Entrega concreta | Criterio para avanzar |
| --- | --- | --- |
| 0. Revisar este plan | Flujos, tratamiento de invitaciones y contactos actuales | Recorridos entendidos y decisiones elegidas; comprobar consumidores/contratos reales antes de editar. |
| 1. Diseño de los ajustes del chat | Eventos de acceso, Manual y estado legado sin automatización | Elección visual siguiendo el sistema actual, antes de programar la presentación. |
| 2. Pruebas en rojo | Ingreso en Manual, «Hola» sin acceso, sin alta/confirmación y acciones conocidas | Fallan por el acoplamiento actual, no por un test mal armado. |
| 3. Refactor de Application/Domain | Políticas explícitas y retiro completo del bot anterior | Tests unitarios y reglas de arquitectura en verde; no convivir con dos políticas activas. |
| 4. Persistencia y migración | Metadata y datos/default Manual | Base anterior migra sin pérdida; modelo sin cambios pendientes; replay/concurrencia comprobados. |
| 5. Front | Historial mixto y controles coherentes | Tests de flujo y visuales, mobile/teclado/es/en y borradores conservados. |
| 6. Regresión completa | Backend y front comprobados | `dotnet build` sin advertencias, `dotnet test` completo con Docker; build/lint/test/visual del front. |
| 7. Prueba real y cierre | Teléfono, login en Manual e invitación; documentación consolidada | Evidencia externa y automática diferenciadas, pendientes explícitos, aplicación de prueba apagada si la levantó el agente. |

Antes de editar se lee la cadena de AGENTS de cada ámbito afectado y se revisa el estado de ambos repositorios. Se preservan cambios de otras tareas, migraciones ya existentes y configuración/secretos. Se trabaja en main, sin ramas/push y sin commits no solicitados.

La instalación del refactor requiere reinicio coordinado de la aplicación, migración y front compatible. En producción se mantiene el procedimiento bundle → imagen. No se publica ni cambia una plantilla en Meta como parte de la planificación. Antes de la prueba real se comprueba el payload que devuelve la plantilla de invitación configurada: el código actual usa `WANT_TO_ENTER`, pero este relevamiento local no acredita por sí solo la configuración externa de Meta.

## 11. Fuera de esta entrega y riesgos identificados

- Conectar el LLM, administrarle una key, fuentes del negocio, contactos CRUD, gestión de plantillas, Embedded Signup y multitenancy siguen en sus entregas propias.
- La IA asistida no es un bot autónomo. Su entrega reutilizará mensajes Business y exigirá revisión humana para enviar.
- Cambiar la invitación a orientación + código agrega un paso. Se eligió como propuesta concreta para conservar la plantilla y unificar el ingreso; debe revisarse antes de desarrollar, no ocultarse como si fuera el mismo recorrido anterior.
- La historia Unclassified se conserva, pero no alimentará automáticamente al LLM. Recuperar su propósito requeriría una decisión y evidencia adicionales.
- La cola de códigos/invitaciones sigue teniendo sus límites actuales en memoria. Convertirla en durable merece un desarrollo explícito, no una promesa añadida a este refactor.
- Un POST a Meta ya iniciado antes del apagado puede completar. La migración no puede retirarlo del teléfono del destinatario.
- No se afirma riesgo cero: el cierre exige casos de regresión, migración, concurrencia y pruebas reales. Si un consumidor descubierto obliga a ampliar el alcance, se actualiza este inventario antes de implementar ese cambio.
