# WhatsApp después del chat: entregas pendientes

Estado: **propuesta**. Alcance compartido: `ArquitecturaBase` y `../ArquitecturaBaseFront`.

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
