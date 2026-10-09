# Desarrollo de la bandeja de WhatsApp

Fecha: **2026-10-01**, actualizado **2026-10-02**. Estado: **chat de atención implementado; IA, contactos, plantillas y segunda instalación pendientes**.
Alcance compartido: `ArquitecturaBase` y `../ArquitecturaBaseFront`.
La configuración general de LLM ya está implementada localmente fuera de WhatsApp:
ver [reglas del módulo IA](../features/inteligencia-artificial.md) y
[evidencia/pendientes](../reviews/2026-10-02-configuracion-llm.md). Administración →
Configuración → Inteligencia artificial conserva una conexión compartida por instalación.
El siguiente bloque se propone en [Sugerencias de IA con revisión humana](2026-10-03-sugerencias-ia-whatsapp.md):
ajuste visual elegido, historial + información básica del negocio confirmados y generación
todavía sin implementar. La pantalla de información se maqueta primero.
Ese plan reemplaza el inventario histórico de IA de la sección 12; no crear otra conexión
ni acoplar WhatsApp con el módulo ArtificialIntelligence.
Este documento conserva las propuestas pendientes de ambos repositorios. Para el chat, las decisiones y archivos vigentes son el código y [WhatsApp: chat de atención](../features/whatsapp.md#chat-de-atención--2026-10-02), que prevalecen sobre la estructura propuesta abajo. El chat promovido incluye API, permisos, lectura propia, modo bot/manual, salida durable y revisión de resultados inciertos. Las pruebas automatizadas usan Meta simulado; la comprobación con el teléfono real sigue siendo el próximo paso con el usuario.

El 2026-10-02 el usuario confirmó recepción real y mostró respuesta manual con estado Entregado. Detectó además el acoplamiento con el bot de ingreso. El [refactor de separación de acceso y atención](2026-10-02-separacion-acceso-y-chat-whatsapp.md) ya está implementado localmente: ingreso explícito, invitaciones, modo Manual y propósito del historial, con [validación automática](../reviews/2026-10-02-separacion-acceso-whatsapp-validacion.md). La observación previa de recepción/entrega no acredita la prueba completa de acceso/invitación después del refactor, que sigue pendiente.

Las secciones 1 y 2 se reconciliaron con la implementación. Las secciones 3 a 11 conservan
el inventario y orden de desarrollo originales del chat, incluidos nombres propuestos y
comportamientos reemplazados: no son una lista de archivos por crear ni otro contrato vigente.
El documento estaba sin versionar y se conserva por su información única y procedencia.
Para nuevas entregas, usar las fuentes canónicas y las secciones 12 y 13; IA, Contactos,
Plantillas, configuración del canal en pantalla y segunda instalación siguen abiertas.

Actualización de alcance: **2026-10-02**, plantilla reutilizable con instalaciones independientes.

## Decisión de producto: una plantilla, una instalación por negocio

El usuario confirmó que desarrollará su servicio propio (por ejemplo, Facturitas)
a partir de esta base. Después clonará el código y hará una instalación independiente
para La Clementina, donde seguirá desarrollando las funciones de ese negocio;
podrá repetir el proceso con otras empresas. **Cada instalación atiende un negocio**
y tiene su administrador, datos, WhatsApp, configuración y conexión de IA propios.
La decisión reemplaza la propuesta anterior de una plataforma central con varios
negocios: multitenancy, superadministración global y Embedded Signup no son etapas
obligatorias de este desarrollo.

En su primera instalación, **solo su cuenta administra la configuración**. Los demás
pueden atender según los permisos que él les asigne; no conectan números propios ni
aportan claves personales. En una copia se configura su administrador inicial propio,
sin dejar una cuenta del desarrollador ni credenciales heredadas como acceso permanente.
El proveedor puede ayudar a instalar y mantener cada copia con una cuenta autorizada.

| Primera etapa acordada | Regla |
| --- | --- |
| Negocio/canal | Una instalación, un negocio atendido y un WhatsApp compartido. |
| Configuración | Administrador inicial configura el canal y la conexión de IA compartida. No se concede esa capacidad al registrarse. |
| Atención | Roles editables con permisos separados para ver chats, responder, cambiar bot/manual y consultar contactos. |
| Lectura y modo | Sin leer por operador; modo bot/manual compartido por conversación. |
| IA | Una configuración del negocio; sugerencias revisadas y enviadas por una persona, confirmado el 2026-10-02. Sin perfiles personales. Falta elegir proveedor/modelo y fuentes. |
| Plantillas | Crear las necesarias para el negocio; la gestión por API y su pantalla tienen una entrega propia después del chat. |
| Otros negocios | Clonar código y desplegar otra instalación con datos/secretos nuevos. La segunda empresa no se agrega como usuario de la primera. |

El rol Admin actual administra toda la instalación. En esta etapa se asigna solo a
la cuenta del propietario que administra el despliegue; las cuentas de atención usan
roles con permisos acotados. El servidor autoriza por permisos, sin comparar emails
en controllers ni exigir un nombre de rol. Dar Admin a otra persona también le daría
configuración; no se debe usar ese rol para habilitar solamente el chat.

**Situación verificada:** la base tiene Identity, roles y permisos globales; no
empresas, membresías ni autorización por empresa. `WhatsAppOptions` contiene un
`PhoneNumberId` y un token leídos al arrancar; `WhatsAppCloudClient` usa esos valores.
Los contactos tienen índices únicos globales por identificador/cuenta y no incluyen
empresa/canal. No hay alta de negocios con Embedded Signup ni un superadministrador
de plataforma independiente. El alcance canónico excluye `ArquitecturaBaseMultitenant`.
Por tanto, el modelo actual de la base coincide con la decisión: una instalación
administrada por negocio. Chat y permisos de atención están implementados; IA y las demás
entregas siguen pendientes. Vender/clonar la plantilla no acredita una segunda instalación probada.

El usuario pidió maqueta → estructura y explicación de archivos → plan → desarrollo,
trabajando por partes. La [maqueta local](../../../ArquitecturaBaseFront/docs/design/whatsapp-chat-propuesta.md)
ya permite revisar Chats, Contactos, Plantillas, Inteligencia artificial y Conexión de
WhatsApp, además de Roles y permisos en Administración. La ronda del 2026-10-02 incluye
modo bot/manual y propuestas de IA separadas del borrador: agregar una propuesta no
envía un mensaje. Todos los datos, pruebas de conexión, permisos y envíos son simulados;
el chat fue elegido y promovido a producto. Contactos, IA, Plantillas y diagnóstico del canal
conservan sus entregas posteriores y datos simulados en el laboratorio. El inventario original
se conserva como antecedente; no se crean clases, componentes ni carpetas productivas vacías.

## Ampliación en estudio: respuestas con LLM

El usuario pidió incorporar el estudio de respuestas con LLM y trabajar el diseño
conversándolo **antes de desarrollar**, para fortalecer este plan. La propuesta de
atención anterior sigue como base; la integración de IA tiene responsabilidades
propuestas en sección 12 y sigue sin implementar. No se eligieron proveedor ni modelo; se confirmó primero
sugerencia con revisión humana, sin envío autónomo generado por el LLM.

Primero identificar consultas reales, fuentes de información, datos autorizados,
acciones permitidas y criterios de derivación a una persona. Comparar dos recorridos:
LLM que propone un borrador al operador y LLM que responde automáticamente dentro
de un alcance acordado. La elección de bot/manual no autoriza por sí sola envío
autónomo de texto generado ni acceso del modelo a datos de todas las cuentas.

Separar tres responsabilidades: reglas actuales de ingreso/códigos/vínculos;
generación de respuestas de atención; y transporte por WhatsApp, con sus plantillas
aprobadas y ventana. Una respuesta generada no constituye una plantilla de Meta.
El LLM recibe contexto seleccionado por el backend; no se le entregan códigos,
tokens o enlaces de ingreso ni se le permite abrir sesiones. Las consultas/acciones
de producto se ejecutan por servicios autorizados, con alcance de la persona del
chat, sin delegar autorización en el texto generado.

Antes de extender el árbol de archivos: preparar conversaciones de ejemplo y un
conjunto de evaluación con respuestas esperadas, información ausente, contradicciones,
mensajes que intenten cambiar instrucciones, derivación humana y falla del proveedor.
Revisar calidad, costo y latencia con el alcance concreto; versionar instrucciones
y registrar origen de sugerencias/respuestas sin exponer contenido sensible en logs.
Referencia conceptual: [workflows, herramientas y evaluación, Anthropic](https://www.anthropic.com/engineering/building-effective-agents).

### Configuración, credenciales y quién paga: en discusión

Maqueta solicitada por el usuario: [configuración de la conexión](../../../ArquitecturaBaseFront/docs/design/whatsapp-chat-propuesta.md#conexión-al-llm-maqueta-solicitada-el-2026-10-01),
entrada local `?screen=ia` en el laboratorio. Guardar/prueba/quita y sus errores son
simulados. Es evidencia para discutir alcance y responsables, no una integración
implementada ni una decisión tomada sobre uso de claves personales.

Corrección del 2026-10-02: la separación visual «Del negocio / Mi conexión» fue
una propuesta del agente que confundió roles con alcance de credenciales. Se retiró
de la maqueta, junto con límites y tarjetas de enrutamiento/pago. Ahora se muestra
un formulario común; poder consultarlo o editarlo depende del permiso. El modelo
de negocio se aclaró después en la decisión de producto anterior: conexión compartida,
administrada por el propietario de la instalación, sin clave por operador.

Aplicación de la decisión (ejemplos, no funciones de producto implementadas):

| Caso | Quién configura y utiliza la IA |
| --- | --- |
| Servicio propio / Facturitas | Su instalación, administrador y configuración; funciones específicas desarrolladas en el proyecto derivado. |
| La Clementina | Copia del código, despliegue y datos propios; WhatsApp y conocimiento de La Clementina; mismo formulario y permisos comunes. |
| Otra empresa | Otra instalación independiente del mismo punto de partida, con configuración y funciones propias. |

Si el objetivo fuera un asistente personal para cada cliente final, podría tener
sentido una conexión propia por persona. Sería otro alcance, a elegir expresamente.

La alternativa de una clave por usuario se pospone según el pedido del 2026-10-02.
La primera versión guarda/reemplaza la configuración compartida del negocio mediante
la cuenta administradora. El consumo corresponde a la cuenta del proveedor asociada
a esa clave; vender el software no obliga a usar la cuenta de IA del desarrollador.
El contrato comercial puede acordar quién aporta y paga esa cuenta. No se autoriza
usar credenciales personales de operadores en trabajos automáticos.

Un perfil de conexión separa proveedor, modelo y credencial. Reemplazar la clave
rota la credencial; cambiar de modelo o proveedor es otra operación que requiere
validar compatibilidad. Propuesta de pantalla: seleccionar proveedor/modelo, cargar
o reemplazar clave, comprobar conexión y activar/desactivar. Uso/límites quedan para
discutir luego de definir quién paga. El
resultado de una prueba debe aclarar si implicó una llamada facturable. La clave es
de escritura: luego la UI muestra estado/máscara, nunca devuelve el secreto guardado.
Los secretos se protegen en el backend con un almacén de secretos o cifrado cuya
clave de protección vive fuera de la base/repo, con acceso por dueño y permiso.
Sin secretos en localStorage, logs ni configuración versionada. Referencia:
[autenticación y claves, documentación oficial de OpenAI](https://developers.openai.com/api/reference/overview#authentication).

Alternativas históricas; solo negocio/instalación integra el alcance actual:

| Dueño del perfil | Uso posible | Qué falta definir |
| --- | --- | --- |
| Negocio/instalación | Conexión compartida administrada centralmente | Confirmado. Falta elegir alcance de respuestas y cuenta que aporta la clave. |
| Operador | Conexión personal para sus sugerencias | Pospuesto; no se ofrece en esta entrega. |
| Persona que escribe por WhatsApp | Asistente personal con perfil propio | Fuera del producto actual; ser contacto no implica ser administrador ni operador. |

El mismo número de WhatsApp puede transportar respuestas generadas con distintos
perfiles: el backend selecciona **un único perfil por trabajo**, conserva esa
asociación y el historial por contacto. No se dispara una respuesta por cada perfil.
No se selecciona la clave por quién tiene la pantalla abierta ni por quién leyó primero.
Para automático, el canal necesita una regla estable de dueño/presupuesto. Un perfil
personal para sugerencias es una alternativa todavía no elegida. Cambio de perfil no mezcla
historiales ni modifica permisos/datos del negocio; el contexto se reconstruye de
fuentes autorizadas y no depende de memoria compartida del proveedor.

Traer clave propia traslada el consumo a la cuenta/proyecto al que pertenece, según
la facturación del proveedor. No cubre por sí mismo hosting ni cargos de WhatsApp.
Para OpenAI, el uso de API se factura por separado del uso de una suscripción ChatGPT:
[documentación oficial de precios](https://learn.chatgpt.com/docs/pricing).
No se supone acceso gratuito a la API por tener una suscripción de chat.

Cada copia tiene una instalación y un canal compartido entre sus operadores. El
aislamiento entre negocios depende de separar sus recursos y configuración según
sección 13; clonar un checkout conservando la misma base no crea otra instalación.

**Próxima decisión:** definir qué preguntas del primer servicio debe resolver la IA,
qué información confiable puede consultar y qué respuestas debe derivar a una persona.
Luego revisar fuentes y herramientas, experiencia de la pantalla y contratos. El
inventario inicial de responsabilidades está en sección 12 y se concreta después
de esas decisiones. La definición no se da por resuelta con agregar un cliente HTTP
al proveedor.

### Plantillas de Meta: posibilidad investigada el 2026-10-02

Meta permite crear plantillas con `POST /{WABA_ID}/message_templates` en Graph API,
además de consultarlas, editarlas y eliminarlas. Crear inicia el proceso de revisión;
no equivale a aprobar ni enviar. Referencias primarias: [colección oficial de Meta](https://www.postman.com/meta/whatsapp-business-platform/folder/lczy75a/templates)
y [ejemplo de creación y respuesta PENDING](https://www.postman.com/meta/whatsapp-business-platform/request/4z2awhk/create-catalog-template).
Una opción «WhatsApp → Plantillas» podría permitir redactar, enviar a revisión y
consultar el estado desde la aplicación. Es una entrega posterior acordada, con
responsabilidades en sección 12 y sin pantalla ni código productivo todavía. Distinguir plantillas aprobadas por
Meta, respuestas rápidas internas y textos generados por el LLM.

## 1. Qué vamos a construir

Un menú **WhatsApp**, con su panel como Administración. Se entrega por partes:

| Destino | Primera versión |
| --- | --- |
| Chats | Bandeja, búsqueda, Todos/Sin leer con conteos, historial, respuesta de texto, ficha del contacto y modo bot/manual. |
| Contactos | Listado de contactos que ya llegaron por el webhook, búsqueda, vínculo con una cuenta cuando esté autorizado y acceso a su chat. Solo lectura. Maqueta local dibujada; elección visual e integración pendientes. |
| Inteligencia artificial | Configuración compartida exclusiva del administrador y uso según permisos; inventario adicional y decisiones pendientes en la ampliación al final. |
| Plantillas | Entrega posterior: creación, revisión y consulta del estado de Meta. No habilitar el enlace productivo hasta tener su pantalla y contratos. |

**Decisiones vigentes:** sin leer por operador y modo compartido por conversación, sin
asignación ni bloqueo exclusivo de un operador. Manual es el default. Bot permanece en el
contrato como automatización sin configurar; la UI solo permite tomar Manual. La decisión
del ADR 0011 reemplaza el bot de acceso del diseño original.

Comportamiento implementado del chat:

- Un chat nuevo comienza en Manual. Abrir o leer no cambia el modo; responder exige Manual.
- El bot anterior no responde mensajes comunes ni se puede activar desde la UI. Los botones
  estructurados históricos orientan a login/registro públicos en cualquier modo, sin efectos
  de identidad. Pedir y verificar un código desde la web es independiente de la atención.
- Dos operadores pueden leer y responder; se muestran los autores. La idempotencia evita
  duplicar un pedido, pero no evita que dos personas escriban dos respuestas distintas.
- Marcar leído confirma únicamente los entrantes que llegaron a mostrarse con el hilo
  visible. Un entrante nuevo o tardío conserva su condición de sin leer.
- La primera versión responde a conversaciones existentes. No crea contactos a mano,
  no vincula cuentas desde esta pantalla ni inicia conversaciones con números nuevos.
- Borradores por contacto en memoria de la sesión; se conservan al cambiar de hilo o
  abrir el panel. No se guardan cuerpos de mensajes en URL ni localStorage. Se limpian
  junto con la sesión; al recargar se pierden, con guarda de salida cuando corresponda.
- Español e inglés, escritorio y móvil desde 320 px, con las piezas visuales actuales.

Quedan para otras entregas: adjuntos, audios, respuestas rápidas, configuración del
canal desde una pantalla, asignaciones, etiquetas y automatizaciones adicionales.
Inicialmente el canal se configura con el procedimiento técnico actual, asistido
por el administrador; no se promete edición en caliente de `WhatsAppOptions`.
No se muestran opciones sin funcionalidad en el menú productivo.

## 2. Qué existe y qué reutilizamos

Relevamiento del código local, no de una implementación supuesta de Multitenant.

| Pieza existente | Uso en este desarrollo |
| --- | --- |
| `Domain/Modules/WhatsApp/WhatsAppContact.cs` | Identidad del contacto, nombre, número opcional, cuenta vinculada y último entrante. Se amplía; no se crea otra entidad Contact. |
| `Domain/Modules/WhatsApp/WhatsAppMessage.cs` | Historial confirmado, dirección, cuerpo opcional por retención y estados de Meta. Se amplía con origen/autor y secuencia. |
| `WhatsAppWebhookController`, `WhatsAppWebhookService`, `WhatsAppWebhookPersistence`, parser y repositorios | Recibir y deduplicar entrantes/estados. Se incorporan secuencias y correlación de envíos manuales. |
| `WhatsAppInboundService`, `WhatsAppInboundPolicy`, `WhatsAppAccessReplyPolicy` | Refactor implementado: clasifica entradas y orienta acciones estructuradas sin modificar identidad; acceso independiente de Manual. |
| `IWhatsAppCloudClient`, `WhatsAppCloudClient`, `WhatsAppMessagePayload` | Envío a Meta. Se reutilizan y se distingue rechazo confirmado de resultado incierto. |
| `WhatsAppSendQueue`, `WhatsAppSenderBackgroundService`, `WhatsAppDeliveryService` | Códigos, invitaciones y orientación pública por cola en memoria. Los manuales usan `WhatsAppOutgoingMessage`, servicios de despacho y worker propios; ambos transports comparten coordinación Redis. |
| `WhatsAppContactLinker` y los participantes del número | Vínculos con cuentas; no se duplican desde Contactos. |
| Retención de mensajes, disponibilidad, salud y registros DI del módulo | Se amplían para las nuevas tablas y estados sin exponer secretos. |
| Front: `AppLayout`, `Sidebar`, `Topbar`, `Page`, `Surface`, campos, diálogos, permisos, i18n, QueryClient y `httpClient` | Composición del menú y pantalla con controles comunes. El layout temporal del laboratorio no se copia a producción. |

`IWhatsAppMessageReader` consulta estados de entrega por id de Meta. El chat implementado
agrega `IWhatsAppChatReader`, `WhatsAppChatService`, despacho durable y siete rutas en
`WhatsAppChatsController`; el inventario vivo está en `ExplicitRouteInventoryTests.WhatsApp`.
Los nombres propuestos del inventario siguiente no sustituyen esos archivos reales.

## 3. Cómo se conectan las partes

```text
Pantalla React
  → API de la feature (httpClient + TanStack Query)
  → Controller MVC (sesión, permiso, contrato HTTP)
  → Servicio de Application (validación, reglas, transacción)
  → Lector / repositorio del módulo
  → PostgreSQL

Respuesta manual:
  POST → guardar operación pendiente → 202 con su identificador
  Worker → reservar operación → llamar a Meta fuera de la transacción
         → confirmar resultado e historial en otra transacción
  Webhook → actualizar entrega
  Pantalla → consultar bandeja/hilo/operación y mostrar su estado real
```

El front no llama a Meta ni decide si la ventana está abierta. Controllers no usan EF.
Los servicios públicos que escriben tienen un único `ExecuteInTransactionAsync` por
operación; los helpers no guardan. El worker separa reservar, HTTP y confirmar.
Todas las piezas propias del canal permanecen en `Modules/WhatsApp`.

## 4. Estructura del front y función de cada archivo

Raíz: `../ArquitecturaBaseFront`. **N** nuevo; **M** existente que se modifica.
Los árboles omiten lo reutilizado sin cambios. Los nombres son destino propuesto,
no una obligación de mantener divisiones artificiales si una pieza resulta trivial.

```text
src/features/whatsapp/                          N: área de producto
├─ AGENTS.md                                   reglas del área y enlaces canónicos
├─ CLAUDE.md                                   importa @AGENTS.md
├─ api/
│  ├─ contracts.ts                             tipos HTTP: filas, detalle, estados y pedidos
│  ├─ chats.ts                                 leer bandeja/hilo, marcar leído y cambiar modo
│  ├─ contacts.ts                              leer listado/ficha de contactos
│  ├─ messages.ts                              aceptar respuesta y consultar operación
│  └─ queryKeys.ts                             claves compartidas de caché de esta feature
├─ pages/
│  ├─ WhatsAppChatsPage.tsx                     coordina URL, selección y consultas del chat
│  └─ WhatsAppContactsPage.tsx                  coordina listado/ficha y acceso a conversación
├─ components/
│  ├─ ChatFilterBar.tsx                         búsqueda y Todos/Sin leer con conteos
│  ├─ ConversationList.tsx                      filas, selección, paginado y recuperación
│  ├─ ConversationListItem.tsx                  nombre, vista previa, hora y sin leer
│  ├─ ConversationHeader.tsx                    contacto, ventana y acceso a su ficha
│  ├─ ConversationModeControl.tsx               Bot activo/Atención manual y confirmación
│  ├─ MessageThread.tsx                         historial, cargar anteriores y scroll
│  ├─ MessageBubble.tsx                         contenido/origen/autor sin renderizar HTML
│  ├─ MessageDeliveryStatus.tsx                 texto e icono para estados de envío/entrega
│  ├─ MessageComposer.tsx                       texto, validación, Enter y envío explícito
│  ├─ ContactDetailsDialog.tsx                  ficha reutilizada dentro de la feature
│  └─ ContactsFilterBar.tsx                     filtros del listado de contactos
├─ hooks/
│  ├─ useWhatsAppChatQueries.ts                 consultas, refresco y mutaciones del chat
│  ├─ useWhatsAppContactQueries.ts              consultas de contactos y detalle
│  ├─ useConversationDrafts.ts                  borradores por contacto en memoria
│  └─ useManualMessageSend.ts                   id del pedido, bloqueo y revisión de inciertos
├─ model/
│  ├─ chatSearchParams.ts                      parsea/normaliza el estado compartible de URL
│  └─ messagePresentation.ts                   decide presentación sin inventar estados
├─ contactColumns.tsx                          columnas y Abrir chat del listado de contactos
└─ errors.ts                                   interpreta códigos con recuperación de negocio

src/layouts/
├─ navigation.ts                              M: panel WhatsApp, permisos, enlaces y migas
├─ components/Sidebar.tsx                      M: un panel activo; escritorio y cajón móvil
└─ components/NavigationPanel.tsx              N: presentación común de paneles extraída del Sidebar
src/app/routes.tsx                            M: páginas lazy y guards
src/shared/i18n/index.ts                       M: namespace whatsapp
src/locales/{es,en}/whatsapp.json              N: todos los textos del área en ambos idiomas
src/locales/{es,en}/common.json                M: nombres del menú y panel
src/test/mocks/whatsappHandlers.ts             N: respuestas MSW del área para tests
src/test/mocks/handlers.ts                     M: incorpora esos handlers
src/test/visual/whatsapp.spec.ts               N: chat/contactos/menú, móvil y teclado
```

Las páginas usan Page; listados, FilterBar/Surface/Pagination y tabla compartida; el
composer usa campos y botones comunes. No se agrega una biblioteca visual de WhatsApp
ni una copia de `PanelProposalLayout.tsx`. Si se necesita un token de composición del
hilo, se incorpora deliberadamente a `src/index.css`, con documentación y test visual.

**Tests junto a la pieza que tiene comportamiento**, archivos nuevos:

| Archivo (ruta dentro de `src/features/whatsapp`) | Qué demuestra |
| --- | --- |
| `pages/WhatsAppChatsPage.test.tsx` | URL, selección, permisos, carga/error, conteos y conservación del hilo. |
| `pages/WhatsAppContactsPage.test.tsx` | Filtros, ficha, paginado y Abrir chat. |
| `components/MessageThread.test.tsx` | Historial anterior, deduplicación, scroll y entrada nueva sin saltar la lectura. |
| `components/MessageComposer.test.tsx` | Enter/Shift+Enter, texto inválido, ventana cerrada y permiso de envío. |
| `components/ConversationModeControl.test.tsx` | Confirmación, conflicto de revisión y conservación del borrador. |
| `hooks/useConversationDrafts.test.tsx` | Borradores separados y limpieza de sesión. |
| `hooks/useManualMessageSend.test.tsx` | Doble clic, respuesta perdida, revisión y misma identidad del pedido. |
| `model/chatSearchParams.test.ts` | Parámetros válidos, defaults y limpieza sin perder selección/filtros. |
| `model/messagePresentation.test.ts` | Estados verdaderos, cuerpo retenido, origen histórico desconocido. |
| `errors.test.ts` | Códigos, recuperación y traducciones sin comparar textos del backend. |

Se amplían los tests existentes de Sidebar, navegación, rutas/i18n y arquitectura del
front. Los avisos usan `shared/ui/notifications.ts` y el QueryClient común: un dueño
por error, validación en campo, resultado en Toaster y borrador conservado ante fallo.

## 5. Estructura del backend y función de cada archivo

Raíz: `ArquitecturaBase/src`. Dentro de cada proyecto se respeta la misma arquitectura.
**N** nuevo, **M** modificación de una pieza ya existente.

### Domain: datos y reglas puras

```text
ArquitecturaBase.Domain/Modules/WhatsApp/
├─ WhatsAppContact.cs                          M: modo, revisión y secuencias del contacto
├─ WhatsAppMessage.cs                          M: secuencia, origen y operador autor opcional
├─ WhatsAppConversationMode.cs                 N: Bot / Manual
├─ WhatsAppMessageOrigin.cs                    N: Operator / Bot / System / Unknown
├─ WhatsAppReadState.cs                        N: avance leído de un operador en un contacto
├─ WhatsAppOutgoingMessage.cs                  N: operación manual durable y sus transiciones
├─ WhatsAppOutgoingMessageState.cs             N: Pending / Sending / Accepted / Failed / Uncertain
├─ WhatsAppConversationErrors.cs               N: contacto ausente, modo/revisión y lectura inválida
├─ WhatsAppOutgoingMessageErrors.cs            N: ventana, destino, idempotencia y envío
└─ WhatsAppPermissions.cs                      N: permisos de atención, contactos, IA y administración por entrega
```

`WhatsAppMessage` sigue representando un mensaje confirmado por el proveedor. La
operación pendiente vive en `WhatsAppOutgoingMessage`, porque todavía no tiene wamid.
Al confirmarse, se vinculan operación y mensaje en un commit; la lectura evita mostrar
ambos como dos mensajes. `Unknown` describe salientes anteriores cuyo autor no se
guardó: la migración no inventa nombres ni afirma que todos eran respuestas del bot.

### Application: casos de uso, contratos y validación

Todo este árbol está dentro de `ArquitecturaBase.Application/Modules/WhatsApp/`:

```text
Interfaces/Services/
├─ IWhatsAppChatService.cs                     N: bandeja, hilo, contactos, lectura y modo
├─ IWhatsAppMessageService.cs                  N: aceptar texto y consultar su operación
└─ IWhatsAppOutgoingDispatchService.cs         N: reservar y confirmar envíos del worker
Interfaces/Persistence/
├─ IWhatsAppChatReader.cs                      N: proyecciones de bandeja/hilo/conteos
├─ IWhatsAppContactReader.cs                   N: listado/ficha autorizada de contactos
├─ IWhatsAppConversationRepository.cs          N: locks, modo y avance leído del operador
├─ IWhatsAppOutgoingMessageRepository.cs       N: operación, unicidad, reserva y resultado
└─ IWhatsAppMessageRepository.cs               M: registrar historia/secuencia/correlación
Services/
├─ WhatsAppChatService.cs                      N: coordina lecturas y mutaciones de conversación
├─ WhatsAppMessageService.cs                   N: valida y guarda una respuesta pendiente
├─ WhatsAppSendGuard.cs                        N: destino, modo, ventana y autorización vigente
├─ WhatsAppOutgoingDispatchService.cs          N: ciclo durable; cada escritura tiene su límite
├─ WhatsAppInboundService.cs                   M: manual procesa entrantes sin responder
├─ WhatsAppWebhookPersistence.cs               M: secuencias y estados manuales correlacionados
└─ WhatsAppDeliveryService.cs                  M: origen y secuencia de salientes existentes
Models/
├─ ListWhatsAppChatsRequest.cs                 N: búsqueda, sin leer, página/tamaño/orden
├─ WhatsAppChatListRow.cs                      N: fila interna proyectada por lector de bandeja
├─ WhatsAppChatDetailRow.cs                    N: detalle interno de conversación
├─ WhatsAppMessageRow.cs                       N: fila interna del historial y operación vinculada
├─ WhatsAppContactListRow.cs                   N: fila interna del listado de contactos
├─ WhatsAppContactDetailRow.cs                 N: ficha interna, separada del contrato HTTP
├─ WhatsAppOutgoingMessageRow.cs               N: proyección interna del estado durable
├─ WhatsAppChatListItemResponse.cs             N: contacto, último mensaje y sin leer
├─ WhatsAppChatFilterCounts.cs                 N: conteos con contrato de filtros coherente
├─ WhatsAppChatResponse.cs                     N: cabecera, modo/revisión y capacidad de responder
├─ ListWhatsAppMessagesRequest.cs              N: contacto y cursor para historia anterior
├─ WhatsAppMessagePageResponse.cs              N: mensajes y cursor/avance visible
├─ WhatsAppMessageResponse.cs                  N: cuerpo opcional, origen/autor y estados reales
├─ MarkWhatsAppChatReadRequest.cs              N: contacto y último entrante visible
├─ ChangeWhatsAppConversationModeRequest.cs    N: modo deseado y expectedRevision
├─ ListWhatsAppContactsRequest.cs              N: paginado, búsqueda y vínculo con cuenta
├─ WhatsAppContactListItemResponse.cs          N: proyección de fila de contactos
├─ WhatsAppContactResponse.cs                  N: ficha y cuenta vinculada permitida
├─ SendWhatsAppTextRequest.cs                  N: contacto, operationId y texto
├─ WhatsAppOutgoingMessageResponse.cs          N: identidad y estado de la operación
├─ WhatsAppDispatchResponse.cs                 N: reserva y datos mínimos para envío técnico
├─ CompleteWhatsAppDispatchRequest.cs          N: reserva y resultado confirmado/incierto
└─ WhatsAppOutboundMessage.cs                  M: metadatos de origen/correlación sin filtrarlos en logs
Validation/
├─ ListWhatsAppChatsRequestValidator.cs        N: filtros, tamaño y sortable fields
├─ ListWhatsAppMessagesRequestValidator.cs     N: cursor válido y límite de historia
├─ ListWhatsAppContactsRequestValidator.cs     N: filtros y paginado del listado
├─ MarkWhatsAppChatReadRequestValidator.cs     N: avance válido; servicio verifica pertenencia
├─ ChangeWhatsAppConversationModeRequestValidator.cs N: modo y revisión
└─ SendWhatsAppTextRequestValidator.cs         N: no vacío, límite y texto almacenable
Channels/WhatsAppPermissionCatalogSource.cs    N: aporta códigos y textos por el puerto genérico
Resources/Permissions.resx                    N: etiquetas de permisos del módulo
Resources/Permissions.en.resx                 N: traducción inglesa de esos permisos
Resources/WhatsAppPermissionTexts.cs          N: acceso a los recursos de permisos
WhatsAppApplicationRegistration.cs           M: registra servicios/lectores de catálogo/validadores
```

Los errores y validaciones visibles se agregan a los pares existentes
`Application/Resources/Errors{,.en}.resx` y `Validation{,.en}.resx`, con sus helpers
cuando corresponda. Las declaraciones de Error permanecen en Domain del módulo.
No se crea un helper por método ni se supera el límite de ocho dependencias por clase.
Si una responsabilidad lo exige, se separa por tarea y se actualiza este inventario.
Lectores devuelven Row y servicios los mapean a Response a mano. El conteo agregado
termina en Counts; su excepción al sufijo de salida se registra explícitamente en
ServiceOutputNamingTests sin debilitar el detector para otros tipos.

### Infrastructure: EF, proveedor y worker

Todo lo siguiente vive en `ArquitecturaBase.Infrastructure/Modules/WhatsApp/`:

```text
Persistence/Readers/
├─ WhatsAppChatReader.cs                       N: SQL/EF de bandeja, historia y conteos
└─ WhatsAppContactReader.cs                    N: SQL/EF de contactos y ficha
Persistence/Repositories/
├─ WhatsAppConversationRepository.cs           N: seguimiento, locks y marcas de lectura
├─ WhatsAppOutgoingMessageRepository.cs        N: outbox manual, reservas y resultados
├─ WhatsAppMessageRepository.cs               M: historia/secuencia y estado
└─ WhatsAppMessageRetentionRepository.cs       M: retención también del cuerpo de operaciones
Persistence/Configurations/
├─ WhatsAppContactConfiguration.cs            M: columnas/índices de modo y actividad
├─ WhatsAppMessageConfiguration.cs            M: autor/origen/secuencia y relaciones
├─ WhatsAppReadStateConfiguration.cs          N: índice único operador+contacto
└─ WhatsAppOutgoingMessageConfiguration.cs    N: operación única, lease, estados e índices
Persistence/WhatsAppLockKeys.cs               M: claves y orden canónico de nuevos locks
WhatsAppOutgoingBackgroundService.cs          N: scheduler/scopes, llama servicio y Meta
WhatsAppDispatchCoordinator.cs                N: exclusión temporal por destinatario entre hosts
WhatsAppCloudClient.cs                        M: rechazos vs inciertos y HTTP sin retry oculto
IWhatsAppCloudClient.cs                       M: contrato técnico de envío/correlación si hace falta
WhatsAppSendResult.cs                         M: resultado técnico inequívoco/incierto
WhatsAppMessagePayload.cs                     M: correlación del envío, si la API vigente la soporta
WhatsAppWebhookReader.cs                      M: parsea correlación de estados sin cuerpos crudos
WhatsAppSenderBackgroundService.cs            M: comparte coordinación y clasificación de reintentos
WhatsAppOptions.cs                            M: habilitación/poll/reserva del worker manual
WhatsAppOptionsValidator.cs                   M: límites y opciones coherentes
WhatsAppInfrastructureRegistration.cs         M: repositorios, lectores, worker y cliente técnico
```

`WhatsAppDispatchCoordinator` requiere una pequeña API de leases en el adaptador
Redis existente: hoy sus leases solo coordinan el llenado del caché y no están
expuestos para despacho. Se modifican `Infrastructure/Caching/RedisCache.cs` y
`RedisCacheScripts.cs`, y se agrega `RedisLease.cs` como reserva técnica con token,
vencimiento y liberación/renovación segura. No se usa una fábrica de caché para enviar
mensajes ni se reutiliza la clave de llenado. Estos archivos genéricos no nombran
WhatsApp; no se crea otro caché ni se escribe con otro cliente. Todos los emisores
(manual, bot, códigos, invitaciones) respetan la misma exclusión por destinatario.
PostgreSQL guarda reservas, intentos y resultado; Redis no sustituye la outbox.
La reserva tiene dueño/token y vencimiento: un worker viejo no puede confirmar la
reserva de otro. Recuperar un Sending tras caída se trata conservadoramente como
incierto; no se presume que Meta no recibió el pedido.
El lease no autoriza a prometer exclusión eterna: se renueva durante el HTTP y, si se
pierde, no se inicia otro intento automático. La outbox y su estado incierto siguen
siendo la protección durable. La responsabilidad del adaptador y sus tests de frontera
se documentan junto con esta extensión.

Las migraciones van en la cadena única existente:

```text
ArquitecturaBase.Infrastructure/Persistence/Migrations/
├─ <timestamp>_WhatsAppOperatorInbox.cs           N: esquema + backfill preservando mensajes
├─ <timestamp>_WhatsAppOperatorInbox.Designer.cs  N: generado por EF
└─ ApplicationDbContextModelSnapshot.cs          M: modelo generado
```

Se mantiene `ApplicationDbContext.Set<T>()`; no se agregan DbSets del módulo al núcleo.
Fecha/nombre definitivo se generan con EF al implementar, no se inventa una migración vacía.

### Api: rutas y contratos HTTP

Todo este árbol vive en `ArquitecturaBase.Api/Modules/WhatsApp/`:

```text
Controllers/
├─ WhatsAppChatsController.cs                  N: bandeja, detalle/hilo, lectura, modo y envío
└─ WhatsAppContactsController.cs               N: listado y ficha de contactos
Contracts/
├─ ListWhatsAppChatsQuery.cs                   N: entrada HTTP del listado
├─ ListWhatsAppMessagesQuery.cs                N: entrada HTTP del cursor
├─ ListWhatsAppContactsQuery.cs                N: entrada HTTP de contactos
├─ MarkWhatsAppChatReadHttpRequest.cs          N: entrada HTTP de avance visible
├─ ChangeWhatsAppConversationModeHttpRequest.cs N: modo y revisión
└─ SendWhatsAppTextHttpRequest.cs              N: texto e identificador idempotente
RateLimiting/WhatsAppChatRateLimitOptions.cs   N: límites por operador para lectura/escritura
Routing/ConditionalWhatsAppRouteConvention.cs M: omite las nuevas rutas con canal apagado
WhatsAppApiRegistration.cs                    M: límites y convenciones del módulo
```

Los contratos sensibles sobrescriben ToString. No hay nuevos prefijos fuera de `/api`.
Inventario/OpenAPI documentan permisos, 202, errores y cuerpos. Cuando el canal está
apagado se conserva el criterio de omitir rutas; el front usa la disponibilidad ya
publicada en el perfil para explicar el estado sin consultar endpoints inexistentes.

## 6. Permisos sin acoplar el núcleo al módulo

Este punto requiere **un ADR nuevo antes de cambiar el catálogo**. La guía actual pide
`Permissions.All`, pero el núcleo no puede nombrar WhatsApp. Agregar `Permissions.WhatsApp`
en el archivo del núcleo violaría esa frontera. Propuesta: mantener `Permissions.All`
como catálogo base y componerlo con fuentes de módulos registradas en DI.

| Archivo nuevo del núcleo | Responsabilidad genérica |
| --- | --- |
| `Application/Interfaces/Integrations/Identity/IPermissionCatalog.cs` | Catálogo efectivo disponible para validar, listar y sembrar; sin conocimiento de módulos concretos. |
| `Application/Interfaces/Channels/IPermissionCatalogSource.cs` | Puerto por el que un módulo aporta permisos y textos, como participante registrado con TryAddEnumerable. |
| `Application/Models/Authorization/PermissionDefinition.cs` | Código y datos de presentación del permiso/grupo. |
| `Application/Authorization/PermissionCatalog.cs` | Compone base y aportes, preserva orden y detecta códigos duplicados. |

Se adaptan `RoleService`, validadores de Roles y la regla compartida de permisos
(`Common/Validation/ValidationRules.cs`), `RoleSeeder`, DI y tests que hoy consultan
directamente `Permissions.All`. Seed sigue dando todo lo habilitado a Admin, avanzando
revisión e invalidando después del commit. Fuentes ausentes no dejan permisos del
módulo en el catálogo efectivo; probar también claims antiguos y quitar el módulo.

Permisos propuestos del módulo:

| Código | Habilita |
| --- | --- |
| `whatsapp.read` | Bandeja, historia, ficha básica del interlocutor y marca de lectura propia. |
| `whatsapp.send` | Responder en manual, combinado con read. |
| `whatsapp.manage` | Cambiar bot/manual, combinado con read. |
| `whatsapp.contacts.read` | Listado y ficha de contactos; separado de atender un hilo. |
| `whatsapp.ai.use` | Solicitar una sugerencia en un chat autorizado. Inicialmente solo Admin hasta delegación explícita. |
| `whatsapp.ai.configure` | Consultar/cambiar proveedor, modelo, credencial y reglas compartidas. Solo Admin en esta etapa. |
| `whatsapp.configure` | Reservado para una entrega de configuración del canal desde la aplicación; no crea un endpoint ni sustituye secretos técnicos por ahora. |
| `whatsapp.templates.manage` | Entrega posterior de gestión de plantillas de Meta. |
| `users.read` (existente) | Datos adicionales de la cuenta vinculada. Sin él, el servidor no proyecta esos datos. |

Rutas de envío/modo exigen ambos permisos de su combinación. El actor sale de la
sesión, nunca del cuerpo. El permiso de atención no abre sesiones ni otorga gestión
de usuarios. El ADR y la guía de permiso nuevo se actualizan con la implementación,
sin presentar esta extensión propuesta como una regla ya vigente.

Combinaciones iniciales a crear con la pantalla de roles existente (nombres sugeridos,
no nuevos roles de sistema obligatorios):

| Perfil | Chats | Responder | Bot/manual | Contactos | IA | Configuración |
| --- | --- | --- | --- | --- | --- | --- |
| Administrador inicial (vos) | Sí | Sí | Sí | Sí | Usar y configurar | Sí |
| Atención | Sí | Sí | Sí | Si se concede | Solo si se concede | No |
| Consulta | Sí | No | No | Si se concede | No | No |
| Usuario registrado / contacto | No por defecto | No | No | No | No | No |

El registro o el vínculo con el contacto no conceden atención. Configurar IA y usarla
son permisos distintos. Solo tener send no permite enviar a un hilo que no se puede
leer. Los permisos del menú nunca sustituyen la comprobación del servidor.

## 7. Contrato HTTP propuesto

Todas las rutas de atención están autenticadas. Status de éxito de la tabla; fallos
con ProblemDetails traducido (400/401/403/404/409/429 según regla y convención existente).

| Verbo y ruta | Para qué | Permiso | Éxito |
| --- | --- | --- | --- |
| GET `/api/whatsapp/chats` | Bandeja paginada con búsqueda y filtro sin leer | read | 200 |
| GET `/api/whatsapp/chats/filter-counts` | Todos/Sin leer respetando búsqueda y otros filtros | read | 200 |
| GET `/api/whatsapp/chats/{contactId}` | Cabecera, modo/revisión, ventana y capacidad | read | 200 |
| GET `/api/whatsapp/chats/{contactId}/messages` | Últimos mensajes o anteriores con cursor | read | 200 |
| PUT `/api/whatsapp/chats/{contactId}/read` | Avance propio monotónico hasta lo visible | read | 204 |
| PUT `/api/whatsapp/chats/{contactId}/mode` | Cambiar modo si coincide expectedRevision | read + manage | 200 |
| POST `/api/whatsapp/chats/{contactId}/messages` | Guardar respuesta con operationId; no espera entrega | read + send | 202 |
| GET `/api/whatsapp/chats/{contactId}/outgoing/{operationId}` | Revisar estado tras aceptación o respuesta perdida | read | 200 |
| GET `/api/whatsapp/contacts` | Listado de contactos recibidos | contacts.read | 200 |
| GET `/api/whatsapp/contacts/{contactId}` | Ficha autorizada | contacts.read | 200 |

`SendWhatsAppTextHttpRequest = { operationId, text }`.
`ChangeWhatsAppConversationModeHttpRequest = { mode, expectedRevision }`.
`MarkWhatsAppChatReadHttpRequest = { lastVisibleInboundSequence }`.
Las respuestas incluyen fechas UTC ISO con Z; el front formatea en la zona del perfil.

URL del front: `/whatsapp/chats?contact=<id>&search=...&unread=true&page=...&pageSize=...`
y `/whatsapp/contactos?search=...&linked=...&page=...&pageSize=...`.
Usar nombres definitivos coherentes con el arnés compartido al implementar. Conversación,
filtros y paginado se comparten por URL; el texto escrito y la apertura del diálogo no.

## 8. Reglas que deben quedar resueltas y probadas

### Sin leer e historia

- Secuencia persistida por contacto, asignada bajo el mismo lock al incorporar historia.
  Duplicados del webhook no avanzan secuencia ni inflan conteos. Un entrante tardío se
  incorpora con avance nuevo aunque su timestamp de Meta sea anterior.
- `WhatsAppReadState`: un registro por operador/contacto; avance solo hacia adelante,
  validado contra el contacto y entrantes existentes. El cliente no puede mandar un
  futuro arbitrario que oculte mensajes por llegar. Sin marca, los entrantes históricos
  cuentan como sin leer; confirmar ese comportamiento en la revisión funcional.
- Bandeja ordenada por última actividad y desempate Id; filtros paginados con
  PagedRequest/ApplySort/ToPagedResultAsync. Hilo usa cursor estable y acotado, no
  descarga completa. La secuencia permite mantener historia consistente ante tardíos.
- La primera consulta devuelve últimos mensajes; cargar anteriores preserva scroll.
  Si el operador está leyendo arriba, un entrante no lo lleva al final ni se marca
  leído automáticamente. Se ofrece Ir al último mensaje.
- Botones o multimedia ya guardados se muestran por su tipo/resumen; no se descargan
  adjuntos. Un cuerpo null no implica siempre retención: distinguir el tipo de mensaje.

### Bot/manual

- Cambio de modo y procesado del bot toman el lock canónico del contacto; conflicto
  de revisión devuelve 409 y pide revisar el estado. El bot no responde mientras el
  modo persistido sea manual. Los entrantes quedan en historial y se marcan procesados.
- Abrir un chat no toma atención exclusiva. Enviar exige manual y no cambia modo
  silenciosamente. Worker vuelve a validar modo/ventana/cuenta/permisos antes del HTTP.
- Una respuesta del bot ya encolada debe revalidar modo antes de salir; si cambió,
  se descarta de forma controlada. Para eso el envío automático lleva contacto/origen.
  Códigos e invitaciones explícitos no son respuestas automáticas del hilo.
- Definir el punto de orden entre envío en curso y cambio de modo: un HTTP que ya
  comenzó no se puede retirar. Coordinar el despacho por contacto, revisar nuevamente
  tras obtener exclusión y explicar que manual detiene respuestas futuras.

### Envío durable y resultados inciertos

- Guardar la operación y texto en PostgreSQL antes del 202. `operationId` único y
  ligado a actor/contacto/contenido: repetir el mismo pedido devuelve la misma operación;
  reutilizar su id con otro contenido es conflicto. Un doble clic no manda dos HTTP a Meta.
- Meta no participa de la transacción local: **no se promete entrega exactamente una vez**.
  Idempotencia de API y reserva durable no eliminan la caída entre HTTP y commit.
- Rechazo inequívoco retryable permite espera e intento acotado. Timeout, desconexión,
  respuesta aceptada sin wamid o caída luego de empezar envío quedan Uncertain; no
  reenviar automáticamente. Auditar también la resiliencia HttpClient registrada:
  ningún retry de POST oculto puede multiplicar el envío.
  El registro actual ya usa DisableForUnsafeHttpMethods: se conserva y se prueba.
- Investigar correlación de estados con la versión de Graph configurada usando la
  documentación de Meta. Si existe identificador opaco, enviar solo el operationId,
  sin cuerpo/datos personales, y persistir estados que lleguen antes del historial.
  Sin correlación confiable, no inferir por texto/hora ni habilitar reenvío automático;
  algunos inciertos requerirán revisión operativa explícita. Una consulta sin resultado
  nuevo no convierte Uncertain en Failed.
- Pending es aceptado por nuestra API; Accepted indica aceptación de Meta. Sent,
  Delivered, Read y Failed del historial requieren el proveedor. UI no marca entregado
  porque el POST respondió 202 ni porque pasaron unos segundos.
- El texto libre se habilita solo dentro de la ventana calculada por servidor a partir
  del último entrante. Revalidar al reservar y al despachar; fuera de ventana se explica
  la necesidad de una plantilla, sin ofrecer un selector aún inexistente. Regla verificada
  en la [política oficial de WhatsApp](https://whatsappbusiness.com/policy/), sección 2.
- No incluir cuerpos, códigos, URLs de ingreso ni números completos en logs/ToString.
  Extender retención al cuerpo duplicado de la operación; borrar su copia tras confirmación
  cuando ya está en historial. Fallidas/inciertas tienen caducidad de cuerpo también.

### Refresco inicial propuesto

TanStack Query consulta cada **5 segundos** mientras la pantalla esté visible, con
refresco al volver al foco y después de mutaciones. Consultar bandeja/conteos y el hilo
activo, no todos los hilos; mantener borradores y evitar avisos repetidos por polling.
Pausar en segundo plano y aplicar backoff ante fallas. Como PostgreSQL es compartido,
funciona entre hosts sin un canal SignalR local ni nuevas dependencias en Application.
Los 5 segundos son una decisión del producto propuesta, no un SLA de entrega de Meta.
Si el volumen exige push, medir y planificar ese transporte aparte.

## 9. Tests del backend: archivos y alcance

Raíz `tests/`; cada nuevo test del canal vive en `Modules/WhatsApp` de su proyecto.
Se escribe el caso rojo antes de la lógica correspondiente.

| Proyecto / archivo nuevo | Qué verifica |
| --- | --- |
| Domain.UnitTests / `WhatsAppReadStateTests.cs` | Marca propia y avance monotónico. |
| Domain.UnitTests / `WhatsAppConversationModeTests.cs` | Transiciones, revisión y modo inicial. |
| Domain.UnitTests / `WhatsAppOutgoingMessageTests.cs` | Máquina de estados, reservas, caducidad e incertidumbre. |
| Application.UnitTests / Services / `WhatsAppChatServiceTests.cs` | Lecturas, modo, marca visible, validación y un límite por escritura. |
| Application.UnitTests / Services / `WhatsAppMessageServiceTests.cs` | Ventana/modo, idempotencia, autorización y aceptación durable. |
| Application.UnitTests / Services / `WhatsAppSendGuardTests.cs` | Criterios de envío y revocación antes del despacho. |
| Application.UnitTests / Services / `WhatsAppOutgoingDispatchServiceTests.cs` | Reservar/confirmar, rechazo, reserva vieja y resultado incierto. |
| Application.UnitTests / Validation / `WhatsAppChatValidationTests.cs` | Entradas de chat/contactos, cuerpo, revisión y cursor. |
| Api.IntegrationTests / `WhatsAppChatsEndpointsTests.cs` | Todos los contratos, 401/403, 409, 202 y errores traducidos. |
| Api.IntegrationTests / `WhatsAppContactsEndpointsTests.cs` | Listado/ficha, paginado y datos de cuenta protegidos. |
| Api.IntegrationTests / `WhatsAppChatReaderTests.cs` | SQL real, conteos, orden, cursor, nulls y sin leer por operador. |
| Api.IntegrationTests / `WhatsAppOutgoingPersistenceTests.cs` | Unicidad concurrente, rollback y recuperación después de reinicio. |
| Api.IntegrationTests / `WhatsAppOutgoingBackgroundServiceTests.cs` | Dos hosts, una reserva, HTTP fuera de transacción y caída de confirmación. |
| Api.IntegrationTests / `WhatsAppConversationModeConcurrencyTests.cs` | Carrera bot/manual y entrada simultánea a marca leída. |
| Api.IntegrationTests / `WhatsAppDispatchCoordinatorTests.cs` | Redis real, exclusión por destinatario y pérdida/vencimiento de lease. |

Nuevos tests genéricos, fuera del módulo: `PermissionCatalogTests.cs` y
`PermissionCatalogIntegrationTests.cs` en Application.UnitTests y Api.IntegrationTests,
respectivamente. Usan aportes de control en TestFeatures; verifican catálogo base,
fuentes opcionales, duplicados, traducción, seed y rol con claims antiguos.

Se amplían los tests existentes de Message/Contact, bot/entregas, webhook/retención,
cliente/sender, inventario de rutas, OpenAPI, privacidad y registro apagado; Architecture
conserva detección de permisos válidos, módulos, recursos, capas y transacciones.
Las reglas nuevas de locks se agregan en los parciales del módulo, no relajando tests.
Migración: prueba con historia previa y estados existentes, sin pérdida de datos.
Se agrega `Caching/RedisLeaseTests.cs` en Api.IntegrationTests para la extensión genérica
del adaptador: dos hosts, renovación, dueño obsoleto y liberación que no borra otra reserva.

## 10. Orden de desarrollo: entregas pequeñas y revisables

| Etapa | Trabajo y evidencia para darla por cumplida |
| --- | --- |
| 0. Diseño y acuerdos | Trabajar el alcance y recorrido de LLM con ejemplos reales según la ampliación en estudio. Revisar y elegir los recorridos ya maquetados: chat, bot/manual, anteriores/entrantes, Contactos, propuesta de IA, configuración, plantillas y permisos. Elección explícita antes de producir esas pantallas. Acordar comportamiento de históricos sin leer y refresco propuesto. |
| 1. Contratos y catálogo | ADR del catálogo extensible; tests rojos/verdes de fuentes, seed y traducción. Fijar DTOs, permisos, estados y rutas. Verificar correlación y resiliencia de Meta antes de programar reintentos. |
| 2. Modelo y persistencia | TDD de modo, secuencias, leído y operación durable. Configuraciones EF, migración/backfill y lectores/repositorios con PostgreSQL real. |
| 3. Lectura completa | Controllers de lectura; bandeja, conteos, hilo y ficha reales. Verificar contrato/inventario/OpenAPI, privacidad, paginado y permisos. |
| 4. Atención y envío | Marca leída, modo y bot coordinados; POST durable, worker, reservas y entrega. Pruebas de reinicio, carreras y red incierta con PostgreSQL/Redis reales y Meta simulado. |
| 5. Front de Chats | Generalizar paneles sin cambiar Administración; routes lazy, consultas, borradores, composer, modo y revisión de inciertos. Tests y revisión visual sobre contrato simulado, luego API real. |
| 6. Contactos | Promover el diseño elegido a listado/ficha de lectura; filtros, permiso de cuenta y Abrir chat conservando navegación. |
| 7. IA compartida | Después de elegir proveedor y fuentes: configuración protegida del administrador, prueba y sugerencias revisadas por una persona (recorrido confirmado). No activar envío autónomo. Inventario en sección 12. |
| 8. Cierre de atención | Pruebas completas de ambos proyectos, móvil/teclado/es/en, dos operadores y dos hosts. Prueba controlada con el número de Meta disponible para comprobar recepción y entrega; registrar qué se verificó realmente. Consolidar lo implementado y rescatar las entregas pendientes. |
| 9. Plantillas del negocio | Dibujar listado/editor y revisión; contratos y cliente de gestión de Meta, creación y sincronización del estado. Elegir plantillas necesarias, sin suponer aprobación inmediata. |
| 10. Preparar proyectos derivados | Documentar instalación nueva, secretos/recursos aislados, administrador inicial, fuentes del bot y proceso para trasladar mejoras de la base. Probar una segunda instalación independiente; no agrega gestión central de empresas. Sección 13. |

No se crean todos los archivos de una vez. Cada entrega agrega contratos, implementación
y pruebas que usa. Al comenzar se releva `git status` de ambos repositorios, porque hay
cambios ajenos en curso; no se sobrescriben ni se incluyen como parte de este desarrollo.

## 11. Documentación y cierre

Durante implementación, actualizar los `AGENTS.md`/`CLAUDE.md` existentes del módulo
y tests; crear el par de la feature del front y actualizar ambos mapas. Consolidar:

- `docs/features/whatsapp.md`: reglas efectivamente implementadas de atención/lectura/envío.
- `docs/architecture/backend.md`, guía de permiso nuevo y ADR: catálogo compuesto aprobado.
- `docs/guides/quitar-whatsapp.md`: nuevas tablas, permisos y eliminación de la feature.
- `docs/guides/whatsapp-en-local.md` y Postman: comprobar chat real y rutas nuevas.
- Front `docs/architecture.md`, premisas y `docs/design/visual-baseline.md`: navegación,
  estados, URL y evidencias elegidas; no declarar una propuesta como implementación.
- `docs/architecture/premisas-de-la-plantilla.md`: enlace a las decisiones consolidadas.

Checks requeridos al cerrar el desarrollo (no se ejecutan para afirmar un plan):

```text
Backend: dotnet build ArquitecturaBase.slnx (sin advertencias) y dotnet test
Front:   npm run build, npm run lint, npm run test y npm run test:visual
Si se levantó Aspire: aspire stop al terminar
```

Sin Docker no se declara integración terminada. No se hacen llamadas reales a destinatarios
para probar contratos: los tests usan Meta simulado; el recorrido real se hace de forma
controlada con el número de prueba disponible. Registrar bloqueos de verificación y
fallos ajenos separadamente, sin borrar tests ni actualizar capturas para ocultarlos.

Este documento sigue pendiente hasta implementar y comprobar la entrega correspondiente.
Se consolida lo vigente y se retira con la guía de auditoría documental; Git conserva
el plan y se rescatan pendientes reales. Plantillas y preparación de proyectos
derivados tienen entregas separadas; no bloquean el cierre de la primera entrega de
atención. No se declara capacidad operativa comprobada por escribir esta documentación.

## 12. Inventario adicional: IA compartida y plantillas

Son responsabilidades propuestas; elegir proveedor y concretar el recorrido de
sugerencias confirmado antes de fijar todos los nombres y agregar código. No crear implementaciones vacías para proveedores que
no se usan. El inventario de chat anterior permanece vigente; esto lo amplía.

### IA: front

Raíz `../ArquitecturaBaseFront/src/features/whatsapp/`:

| Archivo propuesto | Para qué |
| --- | --- |
| `pages/AiSettingsPage.tsx` | Formulario de la conexión compartida, exclusivo de configuración: consultar, reemplazar clave, probar y guardar. |
| `components/AiConnectionForm.tsx` | Campos proveedor/modelo, activación y acciones sobre la credencial; no muestra el secreto guardado. |
| `components/AiSuggestion.tsx` | Generación pendiente, propuesta, error y acción explícita para incorporarla al borrador sin borrar texto escrito. |
| `api/ai.ts` | Contratos HTTP tipados de configuración, prueba y sugerencia. |
| `hooks/useAiSettings.ts` | Consulta/mutación por TanStack Query, revisión y recuperación de guardado incierto. |
| `hooks/useAiSuggestion.ts` | Solicitud acotada por chat, cancelación, conservación del borrador y resultado ligado al contexto que la originó. |
| `schemas/aiConnection.ts` | Validación del formulario sin duplicar reglas de autorización del servidor. |
| `pages/AiSettingsPage.test.tsx`, `components/AiSuggestion.test.tsx` | Permisos, secreto oculto, guardas, fallos, revisión y borrador conservado. |

Se amplían rutas lazy, navegación, `locales/{es,en}/whatsapp.json`, composer y pruebas
visuales del área. Usuarios sin configure no acceden al formulario ni reciben los
metadatos sensibles por API; use no entrega credenciales. Primero elegir el diseño
del recorrido de sugerencias y sus estados antes de implementarlo.

### IA: backend

Cada archivo pertenece a `Modules/WhatsApp` en su capa. Mantener dependencia MVC →
servicio → puertos, sin integrar el SDK del proveedor a Application.

| Capa / archivo propuesto | Para qué |
| --- | --- |
| Domain / `WhatsAppAiConnection.cs` | Perfil compartido: proveedor, modelo, activación, revisión y referencia de credencial; sin clave en claro. |
| Domain / `WhatsAppAiErrors.cs` | Errores estables de configuración, proveedor, capacidad y contexto cambiado. |
| Application / `Interfaces/Services/IWhatsAppAiConfigurationService.cs` | Consultar/cambiar configuración y solicitar prueba autorizada. |
| Application / `Interfaces/Services/IWhatsAppAiSuggestionService.cs` | Generar una propuesta para un hilo autorizado; no envía mensajes. |
| Application / `Services/WhatsAppAiConfigurationService.cs` | Coordina validación, rotación y persistencia con revisión. |
| Application / `Services/WhatsAppAiSuggestionService.cs` | Verifica permisos, recoge contexto permitido, llama al puerto del modelo y devuelve la sugerencia. |
| Application / `Services/WhatsAppAiContextBuilder.cs` | Selecciona historia y fuentes del negocio; excluye códigos, secretos y enlaces de ingreso. |
| Application / `Interfaces/Persistence/IWhatsAppAiConnectionRepository.cs` | Recupera perfil seguido y prepara escrituras dentro del único límite del servicio. |
| Application / `Interfaces/Persistence/IWhatsAppAiConnectionReader.cs` | Proyección de estado/configuración que nunca incluye el secreto. |
| Application / `Interfaces/Integrations/AI/IWhatsAppAiClient.cs` | Puerto de generación/prueba; implementación técnica resuelve credencial por referencia. |
| Application / `Interfaces/Integrations/AI/IWhatsAppAiCredentialStore.cs` | Protección y rotación de la credencial; no confundirla con persistencia de conversaciones. |
| Application / `Models/AI/WhatsAppAiConnectionResponse.cs` | Metadatos del perfil, estado y revisión; sin credencial ni texto de conversaciones. |
| Application / `Models/AI/UpdateWhatsAppAiConnectionRequest.cs` | Proveedor/modelo, activación, cambio explícito de clave y revisión esperada; representación redactada. |
| Application / `Models/AI/WhatsAppAiTestRequest.cs`, `WhatsAppAiTestResponse.cs` | Qué perfil/borrador probar y resultado operativo, sin secretos devueltos. |
| Application / `Models/AI/WhatsAppAiSuggestionRequest.cs`, `WhatsAppAiSuggestionResponse.cs` | Contacto/contexto origen e id de solicitud; texto propuesto, generación/origen y revisión, sin enviar. |
| Application / `Validation/AI/UpdateWhatsAppAiConnectionRequestValidator.cs` | Configuración admitida, revisión y operación sobre credencial. |
| Application / `Validation/AI/WhatsAppAiTestRequestValidator.cs`, `WhatsAppAiSuggestionRequestValidator.cs` | Pedidos acotados; permisos se verifican en borde/servicio y no se confían al navegador. |
| Infrastructure / `AI/<Provider>WhatsAppAiClient.cs` | Único adaptador inicial del proveedor elegido, con timeout, catálogo admitido y tratamiento de respuestas/fallas. |
| Infrastructure / `AI/WhatsAppAiCredentialStore.cs` | Almacén protegido, referencias y rotación; clave de protección fuera del repo/base, compartida correctamente entre hosts. |
| Infrastructure / `Persistence/Repositories/WhatsAppAiConnectionRepository.cs` | Escritura EF del perfil. |
| Infrastructure / `Persistence/Readers/WhatsAppAiConnectionReader.cs` | Lectura de metadatos sin exponer credenciales. |
| Infrastructure / `Persistence/Configurations/WhatsAppAiConnectionConfiguration.cs` | Restricción del perfil único inicial y control de revisión; migración sin afectar historia. |
| Api / `Controllers/WhatsAppAiController.cs` | Borde HTTP de configuración y prueba; inyecta el servicio. |
| Api / `Contracts/AI/UpdateWhatsAppAiConnectionHttpRequest.cs`, `TestWhatsAppAiConnectionHttpRequest.cs` | Entrada HTTP de edición/prueba; contratos con clave sobrescriben ToString y nunca la devuelven. |
| Api / `Contracts/AI/GenerateWhatsAppAiSuggestionHttpRequest.cs` | Entrada de generación; actor sale de sesión y contacto de ruta, sin autorizar un negocio desde el body. |
| Api / `Controllers/WhatsAppChatsController.cs` | Añade acción de sugerencia, con read + ai.use, separada del envío manual. |

Se amplían registro DI del módulo, recursos es/en de permisos/errores/validación,
inventario/OpenAPI y tests de los cuatro proyectos. Si la protección elegida requiere
una decisión estructural nueva, crear su ADR antes de implementarla; no dejar una
clave maestra de prueba como protección de producción.

Contrato orientativo (pendiente de fijar DTOs):

| Acción | Permiso | Efecto |
| --- | --- | --- |
| GET / PUT `/api/whatsapp/ai/connection` | ai.configure | Consultar metadatos / cambiar perfil compartido con revisión. |
| POST `/api/whatsapp/ai/connection/test` | ai.configure | Probar explícitamente; puede consumir saldo, no envía WhatsApp. |
| POST `/api/whatsapp/chats/{contactId}/suggestions` | read + ai.use | Generar sugerencia, sin enviar ni reemplazar automáticamente el borrador. |

Para la primera entrega, **sugerencia con revisión humana confirmada el 2026-10-02**.
El modo bot/manual gobierna el bot existente; no activa envío autónomo del LLM.
Envío sigue en la operación manual durable, con read + send,
ventana válida y confirmación del operador. Recopilar preguntas/respuestas reales de
el primer negocio antes de ofrecer generación; definir las fuentes autorizadas y la
respuesta cuando faltan datos. Nunca inventar precios, disponibilidad o reservas.

Pruebas requeridas: permisos de consulta/configuración/uso separados; nueva cuenta
sin acceso; metadatos sin clave; redacción de ToString/logs; revisiones concurrentes;
rotación y recuperación de credenciales; dos hosts; timeout y rate limit; contexto
permitido y ausencia de datos de identidad; proveedor falla y borrador se conserva;
un resultado antiguo no reemplaza un borrador de otro chat. Desconexión o configuración
ausente dejan disponible la atención manual. Definir el estado final de la rotación
y compensación si falla el almacén antes/después del commit: no es una transacción
distribuida con el proveedor de secretos.

Archivos de prueba adicionales, dentro de `Modules/WhatsApp` de cada proyecto:
Domain.UnitTests / `WhatsAppAiConnectionTests.cs` (perfil, revisiones y transiciones);
Application.UnitTests / `Services/WhatsAppAiConfigurationServiceTests.cs`,
`Services/WhatsAppAiSuggestionServiceTests.cs`, `Services/WhatsAppAiContextBuilderTests.cs`
y `Validation/WhatsAppAiValidationTests.cs` (coordinación, contexto, fallos y pedidos);
Api.IntegrationTests / `WhatsAppAiEndpointsTests.cs`, `WhatsAppAiConnectionPersistenceTests.cs`
y `WhatsAppAiCredentialStoreTests.cs` (autorización real, concurrencia, contratos y protección).
ArchitectureTests amplía las reglas del módulo para proveedor solo en Infrastructure,
controllers solo hacia servicios y persistencia detrás de puertos. Ningún test llama
a un proveedor facturable; la comprobación real se hace luego con una cuenta configurada
por el administrador y registra explícitamente qué se verificó.

Si se elige envío autónomo, agregar una entrega con trabajo durable de generación,
deduplicación por entrante, reglas de derivación, versionado de instrucciones y
revalidación de bot/manual antes de generar y antes de despachar. No realizar llamadas
al LLM dentro de una transacción EF ni incorporar generación sin límite a los workers
actuales. Evaluar intentos, consumo y cambios de configuración en trabajos en curso.

### Plantillas: entrega posterior

Front: `pages/TemplatesPage.tsx`, `pages/TemplateEditorPage.tsx`,
`components/TemplatePreview.tsx`, `api/templates.ts`, `hooks/useTemplates.ts` y
`schemas/template.ts`: listado/estado, editor con variables y ejemplos, vista previa,
contratos/consultas y validación; sus tests y tableros antes de producción.

Backend dentro del módulo: `IWhatsAppTemplateService` y `WhatsAppTemplateService`
(Application), modelos/validadores, `IWhatsAppTemplateManagementClient` (puerto) y
`WhatsAppTemplateManagementClient` (Infrastructure), `WhatsAppTemplatesController`
y sus contratos (Api). Usar la WABA y credencial del negocio, no el id del contacto
ni la clave del LLM. Consultar el estado de Meta y manejar timeout de creación sin
crear duplicados por un reintento ciego. Solo añadir persistencia local si el caso de
uso requiere borradores o sincronización; la aprobación la administra Meta.

Los casos iniciales a preparar son las plantillas de ingreso/invitación ya existentes
cuando corresponda a esa instalación y los mensajes de atención que realmente necesite
su negocio. Confirmar contenido y categoría con Meta. Gestión no implica campañas
masivas; envío de una plantilla desde el chat requiere su recorrido y autorización.

## 13. Reutilizar la plantilla: instalaciones independientes

**Modelo confirmado el 2026-10-02:** el mismo código base origina proyectos derivados
para el servicio del desarrollador, La Clementina u otra empresa. Cada despliegue
administra su negocio y sus operadores. No se necesita un superusuario central,
selección de empresa ni TenantId para atender varias instalaciones separadas.
Se mantiene la arquitectura canónica existente y el carácter opcional de WhatsApp.

### Qué se comparte y qué se personaliza

| Parte | Responsabilidad y ubicación |
| --- | --- |
| Plantilla común | Identidad, usuarios/roles/permisos, configuración, idioma, tema, UI, auditoría y pruebas. Conserva las reglas de los repositorios. |
| Módulo WhatsApp común | Webhook, contactos, chat, lectura, modo, operaciones de envío, plantillas y cliente de Meta. En `Modules/WhatsApp` de cada capa; el núcleo no lo nombra. |
| IA de atención común | Conexión compartida, permisos, generación de sugerencias y armado de contexto. Proveedor detrás del puerto del módulo; sin reglas de un cliente concreto en el código común. |
| Información del negocio | Nombre, horarios, políticas y contenido autorizado, configurados por instalación; no fijar «La Clementina» ni precios de un cliente en el bot base. |
| Funciones específicas | En áreas o módulos del proyecto derivado siguiendo la receta canónica. Ejemplos de ubicación: `Application/Services/<ÁreaDelProducto>` y `src/features/<producto>`; no agregar funciones particulares a identidad ni al transporte de WhatsApp. |

La base ofrece el mecanismo para consultar información autorizada; cada derivado
aporta la información y acciones de su producto. Una copia puede usar el chat sin IA.
Las acciones que modifican datos siguen servicios, permisos y transacciones propios:
el texto del LLM no ejecuta funciones particulares ni decide autorizaciones.

Punto de extensión previsto para el contexto: `IWhatsAppAiKnowledgeSource` en
`Application/Modules/WhatsApp/Interfaces/Integrations/AI`, con modelos de contexto
del módulo. `WhatsAppAiContextBuilder` consume los aportes registrados en DI; un
adaptador del proyecto derivado, dentro de `Application/Modules/WhatsApp/AI/KnowledgeAdapters`,
consulta sus servicios/lectores autorizados y entrega
solo información necesaria. El módulo común no referencia clases de Facturitas ni
de La Clementina. Si no existe información confiable, deriva al operador. Concretar
este puerto y sus modelos con los casos reales; no crear un framework de herramientas
ni capas adicionales sin consumidores.

El bot de ingreso actual y las sugerencias comerciales son recorridos distintos.
Elegir qué recorrido ofrece cada instalación; no convertir a un contacto externo en
operador ni reemplazar autenticación determinística con respuestas del LLM. La primera
integración de atención sigue siendo sugerencias con revisión y envío humanos.

### Preparación de una copia

| Recurso | Qué cambia en cada instalación |
| --- | --- |
| Código y Git | Nuevo proyecto derivado desde una versión identificada de la base, con su repositorio y documentación de funciones específicas. Registrar versión de origen y cambios comunes adoptados. |
| Datos y almacenamiento | PostgreSQL propio, credenciales de acceso propias, migraciones desde base vacía y backups/retención propios. No copiar registros de producción de otra empresa. |
| Redis | Instancia o aislamiento técnico efectivo propio, incluidos caché, claves de locks y cuotas. La opción inicial es instancia propia; compartirla sin diseño mezcla estado entre copias. |
| Identidad y despliegue | Administrador inicial, dominio/issuer, certificados y secretos de firma/cifrado propios. Registro y permisos definidos para ese servicio. No heredar una cuenta de soporte oculta. |
| WhatsApp y Meta | Número/phone_number_id del negocio, WABA, token autorizado y plantillas disponibles en esa cuenta. Definir app de Meta y callback para ese despliegue; no reutilizar el destino del webhook anterior por copiar opciones. Configuración asistida inicial. |
| IA | Proveedor/modelo/credencial y fuentes del negocio; perfiles nuevos sin la clave ni contexto de otra instalación. |
| Presentación y operación | Nombre/apariencia, correo, idioma/zona, notificaciones, origen público, observabilidad y políticas del negocio configurados para el despliegue. |

Clonar archivos no copia automáticamente user-secrets ni las variables del servidor;
preparar los valores de cada instalación sin incorporarlos a Git. Mantener fuera del
checkout bases, mensajes, credenciales y archivos privados. Si se decide compartir
algún servicio técnico entre despliegues, documentar y probar el aislamiento antes
de hacerlo; no asumir que puertos u otras URLs separan sus datos.

### Mejoras comunes y cambios particulares

Las correcciones de chat, permisos, transporte y componentes genéricos se realizan
en la base y se trasladan de forma controlada a los derivados, con sus migraciones
y pruebas. Las funciones específicas se desarrollan en el derivado. Clonar no crea
una actualización automática: registrar versiones, revisar diffs y conflictos y
verificar cada instalación antes de desplegar una mejora común. Elegir automatización
de actualizaciones o paquetes solo cuando exista una necesidad concreta.

Entregable de preparación: `docs/guides/crear-proyecto-desde-la-base.md`, con pasos
para obtener la versión, configurar recursos/secretos/admin, aplicar migraciones,
levantar el servicio y comprobar login/webhook/chat/IA. Aún no existe esta receta;
se escribe y verifica con la segunda instalación. Ampliar la guía de despliegue y
el registro de producción con las comprobaciones reales; no afirmar un clon probado
por documentar estos pasos.

### Comprobación con dos instalaciones

Levantar dos despliegues de prueba con bases/Redis/configuración separados: usuarios,
roles, contactos repetidos y mensajes de uno no aparecen en el otro; un cambio de
clave/configuración/permiso no modifica el otro; los webhooks y envíos usan el número
correcto; migraciones y seed funcionan desde cero sin datos ni credenciales heredados.
Usar Meta y LLM simulados en integración; verificar cada conexión real de manera
controlada con sus credenciales. La prueba del chat no requiere que la API externa
se contacte desde la suite.

Una plataforma compartida con múltiples empresas podría evaluarse como otro producto
si vuelve a solicitarse. No integra este plan ni condiciona la instalación de La
Clementina por separado.

## Dependencia resuelta: separación de acceso y atención — 2026-10-02

El refactor de acceso está implementado localmente; las reglas vigentes están en
[WhatsApp](../features/whatsapp.md#propósito-migración-y-recorridos--2026-10-02) y ADR 0011.
La futura selección de contexto del LLM usará solo Business; Unclassified no se reclasifica
por texto y Access no alimenta contexto del negocio. IA, contactos y gestión de plantillas
siguen fuera de esa entrega. La prueba con teléfono real permanece pendiente por separado.
