# Formularios: contrato propuesto del incremento 2

**Procedencia histórica, anterior al ADR 0018:** este documento conserva la propuesta del incremento 2; sus firmas, estados y esperas describen aquel corte. El contrato vigente está en DTO/OpenAPI y ejemplos HTTP exportados. Reglas y comprobaciones actuales en [formularios](../features/formularios.md#verificación-de-cierre--2026-10-11); el piloto real ya volvió al chat el 2026-10-11. No usar esta propuesta como requisito pendiente de implementación.

## Alcance y consumidores

2a implementa con TDD compilación pura de los seis tipos y chequeo informativo sin efectos externos. 2b autorizado acepta una publicación durable y ejecuta sus etapas. Se excluyen captura, sesiones/envío, records, catálogo, grupos, reglas, IA, pooling/reutilización física, tablas futuras por simetría y cambios de conexión Meta. No reutilizar DraftChanges como outbox.

BusinessForms coordina autorización, revisión de borrador, diff, snapshot de preguntas, prefijo y operación local. WhatsApp compila, mantiene mapping/codec y perfil, administra bindings/operaciones y llama al proveedor. Ningún módulo importa tipos/constantes del otro; IDs cruzados opacos, sin FK, navegación EF ni joins entre módulos.

## Puerto neutral y modelos puros implementados en 2a

`Application/Interfaces/Channels/IStructuredFormChannel` se creó con su primer consumidor real, el chequeo. Los DTOs van en `Application/Models/StructuredData`; sin JsonElement abierto, EF, ASP.NET, credenciales, URLs elegidas por cliente ni nombres de tipos de módulos. Adaptador en `Application/Modules/WhatsApp/Channels`, compilador/helper en el área de servicios propia; cliente HTTP en Infrastructure del mismo módulo.

Firma vigente de 2a: `string ChannelId { get; }` y `Task<Result<StructuredFormCheckResponse>> CheckAsync(StructuredFormCheckRequest request, CancellationToken ct)`. Registro `TryAddEnumerable`, un adaptador por identificador funcional neutral; el consumidor recibe colección. Ausencia devuelve ChannelUnavailable; duplicados fallan configuración, nunca First(). No adaptador apagado ficticio. El primer adaptador aporta phone; no introduce nombres de módulos en el núcleo.

| DTO neutral | Contenido cerrado y consumidor |
| --- | --- |
| StructuredFormCheckRequest | Format=1, lista tipada de preguntas con FieldId/Key/Type/Required/MaxLength/textos/opciones ChoiceId; presentación separada Name/OriginalCulture/SubmitLabel y culturas objetivo resueltas por servidor. Consume compilador/check |
| StructuredFormCheckResponse | Compatibility=Blocked/LocallyValidated, ProfileRevision opaca, culturas evaluadas localmente, issues por FieldId/ChoiceId/ruta/código, presupuesto aplicado y CompilationFingerprint. Evidencia externa separada, sin Flow JSON ni Meta ID en HTTP |
| StructuredFormIssueResponse | Identidad opcional + ruta conocida + código de Domain + argumentos seguros de traducción, nunca texto bruto del proveedor. Falta de perfil admite problema global |

Para 2b se propone ampliar **ese puerto**, al existir sus consumidores, con AcceptAsync(StructuredFormPublicationRequest), GetPublicationAsync(StructuredFormPublicationQuery) y GetPreviewAsync(StructuredFormPreviewQuery), todos Task<Result<...Response>>. Accept recibe OperationId/DefinitionId/VersionId opacos, snapshot de preguntas y presentación/culturas selladas, fingerprint y revisión de perfil; devuelve recibo durable opaco. Status recibe ese recibo y devuelve estado/progreso verificados. Preview recibe un binding/recibo fijado, devuelve URL HTTPS del proveedor y ExpiresAtUtc. No agregar ahora métodos ni clases sin llamadas productivas.

Estos entrypoints futuros abren límites propios donde escriben. Un coordinador técnico de Infrastructure invoca reserva local, puerto del canal y completado local en scopes separados; ningún servicio escritor de Application llama al escritor de otro módulo. El chequeo es puro/lector. No se sigue la excepción de Enqueue de invitaciones que participa en la transacción del llamador. Si la implementación necesita una frontera distinta, requiere ADR previo; este documento no cambia la regla ni el test.

## Chequeo: entrada y autoridad

POST `/api/forms/definitions/{id}/publication-checks`: MVC → IBusinessFormPublicationService → lector autorizado → puerto. HasPermission Read y Publish explícitos; consulta sin UoW/locks, ni llamada Meta, ni asignación de identidades. Response 200 con diff/compatibilidad, aun cuando tenga bloqueos representables. HTTP inválido/pertenencia inválida usa Result y ProblemDetails, sin devolver nombres de formularios ajenos.

**D4 aprobada e implementada en 2a:** publicar solamente un borrador ya guardado. Evita inventar FieldId en una consulta y mantiene Fields sólo al publicar. Chequeo recibe Name/Prefix/Draft suministrados y ExpectedRevision; Draft usa la forma de entrada existente (LocalId, FieldId, sin Key). El servicio compara su normalización con el borrador persistido de esa revisión, verificando pertenencia. Si hay preguntas/opciones nuevas sin ID o contenido distinto, devuelve 409 DraftMustBeSaved, sin guardar ni resolver IDs. El compilador recibe el canónico leído; la ruta no habilita reusar identidades retiradas. La consulta no evalúa un nuevo snapshot sin guardar; no asigna identidades ni crea comprobantes.

El navegador no fija autor, versión siguiente, culturas del destinatario, perfil, hash de confianza ni WABA. Accept-Language sólo traduce mensajes. El servidor fija las culturas objetivo de publicación; **D5 anterior, implementada en r1 el 2026-10-10 y reemplazada después por v56:** es/en con fallback al idioma original guardado. No hay otra cadena ni autotraducción.

La conexión actual conserva PhoneNumberId/GraphApiVersion/AccessToken y agrega WhatsApp:WabaId por decisión humana, sin otra conexión por formulario. Se obtuvo del Administrador de WhatsApp y se verificó con GET phone_numbers usando el token existente antes de guardarlo en user-secrets; los demás valores no cambiaron. El servicio del canal y el worker vuelven a comprobar pertenencia antes de efectos externos. Sin contexto verificado se bloquean. Esta lectura acredita pertenencia y acceso a esa consulta; creación/publicación/preview y calificación de permisos/dispositivos siguen pendientes del piloto real. Una respuesta ya prevista por el plan se aplica y se anota sin volver a consultarla.

Contrato HTTP vigente de 2a, propiedades desconocidas rechazadas:

```json
{
  "expectedRevision": 7,
  "name": { "es": "Visita", "en": "Visit" },
  "prefix": "VIS",
  "draft": {
    "format": 1,
    "culture": "es",
    "submitLabel": { "es": "Confirmar visita", "en": "Confirm visit" },
    "fields": []
  }
}
```

Respuesta 2a implementada: definitionId/draftRevision, Compatibility LocallyValidated/Blocked/Unavailable, CanAccept=false, AcceptanceAvailability=Unavailable, canales con revisión opaca/perfil/culturas/ExternalEvidence=Pending, problemas traducidos por identidad y diff inicial. No contiene checkToken, vencimiento ficticio, JSON/hash técnico ni Meta ID. Al no haber versiones todavía, baseVersionId=null y Added son todas las preguntas guardadas; no pretende comparar historia inexistente.

LocallyValidated acredita reglas locales del perfil elegido, sin exigir publicación/round-trip real. La ausencia de aceptación durable no hace incompatible el formulario. En 2b CanAccept expresará admisión local según el contrato aprobado; carencias conocidas de cuenta/configuración pueden impedirla. Evidencia externa Pending no bloquea automáticamente Compatibility. Issues llevan mensaje traducido/código/ruta/límite/cultura y FieldId/ChoiceId; IDs no dependen del orden.

Las seis lecturas/guardados/recuperación existentes conservan sus DTOs. 2a suma sólo la ruta de consulta con Read+Publish, cuerpo 512 KiB y cuota forms-write 30/min. Las lecturas/estados de publicación de 2b requieren la resolución de compatibilidad en la revisión durable, sin ampliar guards cerrados por inferencia.

## Serialización, identidad, hashes y cultura

Versión inmutable: `SchemaFormatVersion=1`, SchemaJson `{format,questions}` exclusivamente. El borrador actual responde `fields`; mapeo explícito a `questions`, sin cambiar ese contrato ni almacenar presentación en versión. Preguntas conservan orden, FieldId/StableKey/tipo; opciones conservan ChoiceId/orden. Sin autor, botón, prefijo, cultura objetivo, perfil, reglas o JSON Meta dentro de SchemaJson. ContentHash sólo SHA-256 de los bytes canónicos de ese schema.

Serializer de versión propuesto: lista fija de propiedades, UTF-8 sin BOM/indentación, GUID formato D minúsculo, boolean/números JSON invariantes, mapas de cultura con orden ordinal; omitir propiedades opcionales ausentes de manera uniforme, no alternar null/omit por serializer. No renormalizar Unicode o recortar contenido ya guardado. Formato/serializer versionados y vectores de bytes/hash en tests; escapar con un único encoder definido. No depender del orden incidental de Dictionary ni GetHashCode.

PublicationFingerprint separado incluye SchemaFormat/ContentHash, revisión, Name/Prefix/SubmitLabel/Culture, culturas objetivo, ChannelId y revisión del perfil/proyección. CompilationFingerprint y hash del JSON wire son propiedad del canal: cubren bytes exactos/perfil/codec, sin contaminar ContentHash. No aceptar hash enviado como autoridad. SHA-256 no cifra preguntas; no usarlo como credencial, publicarlo como diagnóstico ni registrar contenido que lo originó.

**Política anterior pre-D5/v56, evidencia de r1; retirada de la recomendación 2b:** fallback cultura objetivo exacta → idioma original guardado del formulario. No tercer fallback implícito a SystemSettings ni autotraducción. Si falta texto requerido en ambos, MissingTranslation; ayuda opcional ausente permanece ausente. BusinessForms fija idioma original y conjunto objetivo en su manifiesto; el binding congela resolución/codec del canal. Cambiar Accept-Language, configuración o traducciones del borrador después de aceptar no cambia esa publicación. El idioma de errores HTTP conserva su regla independiente.

**Ownership recomendado para revisión, con presentación bilingüe completa según nueva D5 y sin fallback:** BusinessFormPublicationOperation conserva un manifiesto tipado protegido e inmutable: versión de formato, DefinitionId/VersionId/DraftRevision aceptada, nombre, prefijo, idioma original, botón por cultura, conjunto objetivo y referencias/huellas opacas de preparación de cada canal. No tabla Presentation ni metadata dentro de SchemaJson. Estado/lease/recibos cambian sin reescribir el manifiesto. Lector histórico obtiene el manifiesto por relación interna única VersionId→operación, sin reconstruirlo desde Definition mutable. WhatsApp guarda proyección derivada: texto resuelto, JSON exacto, mapping/codec/perfil/Graph y contexto de cuenta. No se vuelve autoridad del contenido del formulario. Preguntas permanecen en versión; el manifiesto referencia su hash, sin otra copia autoritativa.

Name/Prefix/presentación y usuario/UTC se sellan en operación/binding donde tienen consumidor histórico. DataProtection usa propósito propio para checkToken/metadata sensible y rotación compartida existente. Propuesta de token sin tabla: actor/id/revisión/fingerprints/perfil/culturas/expiración 10 minutos, reloj TimeProvider. No guarda DraftChanges, no autoriza por sí solo y no contiene valores de respuestas. Al aceptar se vuelven a verificar permisos/revisión/compatibilidad. Duración de token es política propuesta, no plazo Meta.

## Aceptación durable autorizada — implementación por cortes

POST `/api/forms/definitions/{id}/publications` recibe RequestId, ExpectedRevision, CheckToken y el mismo Name/Prefix/Draft chequeado. HasPermission Read y Publish; no se presume Manage. RequestId UUID del intento; actor tomado del servidor. DTO HTTP y Application sobrescriben ToString; límite propuesto 512 KiB y 30/min por actor como escrituras del borrador, a confirmar con inventario/presupuesto. Chequeo propone 30/min; status/preview 120/min. No son cuotas oficiales Meta.

Replay se resuelve primero por actor/RequestId y payload normalizado completo: mismo intento devuelve recibo original incluso tras cambios de revisión/perfil/token vencido; cuerpo distinto 409. Actor ajeno nunca recupera el intento. Fingerprint del intento es distinto de ContentHash y queda protegido, no en columna/log en claro. Recibo de aceptación sólo necesita IDs/número/revisión/fecha/ruta, no otro snapshot/mapa como DraftChanges. Recomendación: manifiesto/codec mientras su versión/historia lo necesite y deduplicación mínima no reutilizable; plazo/purga y relación con retiro requieren elección previa a migración. No heredar 30 días de DraftChanges ni borrar deduplicación al purgar contenido.

Intento nuevo: valida token/habilitación y snapshot antes del límite; un único ExecuteInTransactionAsync OnSuccess vuelve a autorizar bajo las garantías del caso de uso, toma locks en orden estable, compara ExpectedRevision/revisión de perfil y snapshot, crea filas Fields todavía inexistentes, versión y operación local. Primera aceptación fija PrefixLocked=true **en ese mismo commit**; fallar después nunca lo desbloquea. Publicar no edita preguntas ni incrementa DraftRevision por sí solo; no activa versión ni llama al canal/provider bajo locks. Un rollback no deja prefix lock ni Field/Version/Operation parciales. Usar orden/locks de definición existentes; sólo registrar un nuevo prefijo de lock si el consumidor lo necesita realmente.

202 sin Location hacia GET `/api/forms/definitions/{id}/publications/{requestId}` sólo después de commit: RequestId/OperationId/VersionId/VersionNumber/State=Accepted/AcceptedAtUtc/RecoveryPath. Número de versión asignado bajo lock de definición y unique(DefinitionId,Number). Una operación activa por definición; otra aceptación nueva en curso devuelve 409 PublicationInProgress. Replay no crea otra versión ni otro Flow. Reintento de etapas opera sobre la misma operación/snapshot, no vuelve a aceptar.

**Locks/CAS recomendados:** conservar `forms:request:{actor}:{uuid}` → `forms:definitions`, en llamadas separadas y ese orden, como guardar hoy. No reemplazarlos por lock de definición que escritores existentes no toman. Recibo de publicación se deduplica por actor/UUID dentro de ese recurso; lock compartido no convierte UUID de DraftChanges en publicación. Retención/reserva/completado sin deduplicación de petición sólo toman global y no adquieren request después. Leer después del lock en READ COMMITTED, validar ExpectedRevision y grabar BaseActiveVersionId/operación en curso. Constraint de una operación abierta complementa el lock. Optimizar global requiere medir y migrar conjuntamente sus escritores.

A continuación puede guardarse borrador nuevo con prefijo fijado. Activación compara DefinitionId/VersionId propios, operación vigente, estado ActivationPending, fence y BaseActiveVersionId, **no** que DraftRevision siga igual a la aceptada. Guardar 7→8 mientras se publica 7 no impide activar el snapshot ni sobrescribe 8. Completado ya confirmado es no-op; CAS rechazado queda registrado/recuperable, nunca se asigna ActiveVersionId sin condiciones. Resultado tardío de otra operación/fence no activa ni desactiva versiones. Permisos se revalidan al reservar; tras efecto conocido se preserva recibo aunque autorización cambie, antes de decidir activación/revisión. No descartar evidencia y recrear por ese rechazo.

## Estado y fases recuperables

GET de operación propia exige Read y Publish actuales. 200 no equivale a Published; 404 ajena/inexistente. Un 404 mientras el POST puede seguir confirmando no prueba rollback: consultar con espera/cooldown antes de repetir **el mismo** UUID/cuerpo. No generar otro UUID como recuperación automática.

| Estado local propuesto | Garantía / transición |
| --- | --- |
| Accepted | Snapshot/prefijo/operación confirmados; aún no hay efecto externo confirmado |
| Preparing | Worker reserva lease/fence de la operación local y luego llama al puerto fuera del UoW |
| Publishing | Recibo durable del canal conocido; fases Create → Upload → Validate → Publish → Verify separadas |
| Uncertain | Resultado externo no demostrable; recuperación consulta, sin POST Create ciego |
| ActivationPending | Canal confirmó PUBLISHED y asset/profile/owner esperados; falta commit local |
| Published | Segundo límite BusinessForms verificó recibo/fence/snapshot y fijó ActiveVersionId perteneciente a la definición |
| Failed | Rechazo definitivo traducido; snapshot/prefijo/historia preservados; versión anterior activa permanece |

Status propuesto devuelve IDs locales, revisión/número, estado/etapa, AcceptedAtUtc/UpdatedAtUtc/NextCheckAtUtc, issues localizados y CanRetry/RequiresReview calculados. No enumera Meta ID, access token, body proveedor, WABA/contactos o wire JSON. Reinicio/dos hosts sólo permite completar al dueño vigente del fence. Resultado tardío nunca activa otra versión. Cada servicio de reserva/completar/reconciliar toma un límite propio; helpers no guardan y llamadas entre módulos no sostienen locks.

WhatsApp Accept es idempotente por OperationId neutral más fingerprint; guarda binding/FlowOperation en su UoW. BF worker que cae después de ese commit vuelve a consultar/aceptar **el mismo** ID y obtiene recibo estable. Falla después de Publish confirmado se recupera por GET y commit local; no se vuelve a crear/upload otro Flow. No modelar una transacción distribuida ni poner el binding de WhatsApp en el commit de BF.

Con es/en completos exigidos por D5/v56, la propuesta de operación local agrupa un binding/recibo por cultura y no activa hasta verificar todos los requeridos. Éxito parcial se conserva por cultura; recuperar no recrea los ya confirmados. Una falla conserva la versión anterior activa y los recursos externos conocidos; no los borra como compensación automática. Estado global y NextCheckAtUtc deben reflejar una incertidumbre pendiente aunque otra cultura haya publicado.

## Reconciliación de Create sin ID

Persistir antes de llamar nombre técnico derivado de operación y fingerprint, WABA autorizado, fase/fence y JSON exacto protegido. Deshabilitar retries de POST en resiliencia HTTP; timeout/cancelación después de iniciar la llamada puede dejar efecto incierto. Metadatos/nombre no se suponen únicos ni idempotentes en Meta.

Con ID durable: consultar ese recurso, dueño/asset/validación/status antes de avanzar. Sin ID: listar todas las páginas con presupuesto, buscar correlación y obtener asset de candidatos. Comparar bytes/hash del payload, WABA y evidencias de operación; jamás adoptar sólo por nombre. Cero coincidencias, listado incompleto o varias coincidencias mantienen Uncertain/RequiresReview, sin repetir Create. Incluso una coincidencia exacta puede ser preexistente: la prueba de pertenencia y la eventual adopción exigen revisión/calificación; no se promete un algoritmo automático seguro por la documentación disponible. Acciones manuales y sus permisos/textos/tablero se definen antes del worker afectado.

Upload success con validation_errors no avanza; errores reconocidos mapean a FieldId/ChoiceId mediante mapping de generación y punteros conocidos. Resto es global. No registrar/reenviar mensajes brutos sensibles ni atribuir a una pregunta error de cuenta/red. Antes de activar, verificar PUBLISHED, hash y pertenencia con lectura confirmada; estado de health/eligibilidad puede bloquear uso. Un Flow físico nuevo por versión es política del producto ratificada; calificar su realización por cultura/contexto sin reabrir pooling ni modificar uno vinculado a historia.

## Preview propuesto

GET `/api/forms/definitions/{id}/versions/{versionId}/preview`, Read vigente, pertenencia de versión y binding publicado. Resuelve cultura autorizada por servidor y llama al puerto sin UoW/locks. Devuelve URL del proveedor HTTPS y ExpiresAtUtc exacto; Cache-Control no-store. El canal valida origen proveedor/configuración, no abre URL arbitraria suministrada por cliente; no agrega una sesión/token de captura a preview. Consultar invalidate(false), sin regeneración revocatoria silenciosa.

Error de expiración/ausencia/perfil/health se traduce por Result; iframe/CSP/fallback Abrir vista oficial requieren dibujo y prueba del front. La URL es portadora: ToString redactado, nada en logs/analítica/localStorage. Preview no acredita el round-trip, publicación previa no acredita entrega. No se devuelve una vista local con rótulo oficial.

## Entidades mínimas y migraciones, sólo en 2b

| Dueño / tabla propuesta | Consumidor concreto que justifica crearla |
| --- | --- |
| BusinessFormFields | Primer commit de publicación registra FieldId/StableKey/tipo por definición; no se crea al guardar/chequear |
| BusinessFormVersions | Snapshot preguntas, Number/ContentHash/autor/UTC, leído por diff/historia/activación |
| BusinessFormPublicationOperations | Aceptación/replay/worker/reserva/estado durable local; payload/fingerprint protegidos según privacidad/retención elegidas |
| WhatsAppFlowBindings | Proyección por versión/cultura/perfil: JSON/mapping/codec/hash, Meta ID, owner/status; status/preview y futuras sesiones |
| WhatsAppFlowOperations | Estado de efectos, recibo OperationId único, fingerprint, lease/fence y recuperación del canal |

Definition obtiene ActiveVersionId sólo al existir activación. Constraints garantizan versión de la misma definición y unicidad campo/clave/versión/operación/cultura; ninguna FK cruza módulos. Cinco tablas propuestas por consumidores, no las once de la etapa completa. Retención y protección de schemas/bindings requieren decisión antes de migrar. Configuración por entidad, cadena EF única generada, Up/Down y actualización/base vacía/retirada independiente probadas. 2a no crea estas tablas ni modifica snapshot EF.

## Errores y verificación de los incrementos futuros

2a declaró Forms.Publication.DraftMustBeSaved/ChannelUnavailable y los errores WhatsApp.Flow propios del canal, con traducciones es/en. Para 2b siguen propuestos, sin declarar: Forms.Publication.RevisionConflict, ProfileNotQualified, CheckExpired, PayloadMismatch, InProgress; Forms.Preview.NotAvailable/Expired. Los nombres abreviados de esa lista comparten Forms.Publication como prefijo; PublicationInProgress del recorrido es Forms.Publication.InProgress. El prefijo de código no obliga carpeta compartida; errores del canal pertenecen a su módulo. Fallas por configuración representable señalan FieldId/ChoiceId/ruta; framework conserva 401/403/404/413/429, validación 400, conflictos 409 y excepción 500 genérica con traceId. No construir Error fuera de Domain.

**Evidencia pre-D5/v56:** 2a cuenta con decisiones anteriores de [perfil](../features/formularios.md#verificación-de-cierre--2026-10-11), D4/D5 y el contrato. Evidencia TDD: bytes/hash deterministas, seis dispatches, required/ausencia/false/cero, identidad estable por reordenado, es/en/fallback, límites por campo/global, ningún truncado/mutación y rechazo group/source.catalog. Check falla cerrado si el perfil/control no fue elegido o no pasa validación local; jamás llama provider ni convierte piloto pendiente en fallo local automático. Ruta nueva añade contrato HTTP/OpenAPI/inventario, permisos vigentes, límites/ToString y casos módulo apagado/quitado/duplicado. Build cero advertencias y dotnet test completo con Docker al haber código.

Antes de 2b: revisión concreta de migraciones/retención/locks/worker, sin efectos externos autorizados por este documento. Tests de rollback atómico de prefijo/Field/Version/Operation, dos actores/hosts/CAS/replay, caída entre commits de módulos, fence vencido, pérdida de ID tras Create y ausencia de POST repetido, upload con validation_errors, Publish confirmado sin activación y revocación de permisos. Meta simulado sólo demuestra decisiones del arnés.

Piloto posterior explícitamente pendiente: Graph/perfil/cuenta/WABA calificados, payload real aceptado, seis controles y ayudas en dispositivos, complete/cierre sin respuesta, decimal exacto sin float, ausencia/false/cero, DateOnly/es/en/opciones largas, preview/CSP y retención. Nunca cerrar el piloto con validación documental ni afirmar que Meta aprobó una publicación por un 202 local.
