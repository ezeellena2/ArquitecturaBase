# Premisas de la plantilla

El chat de atención del 2026-10-02 conserva una instalación por negocio, lectura por operador, modo compartido y salida manual durable con revisión de resultados inciertos. Su fuente es [WhatsApp](../features/whatsapp.md#chat-de-atención--2026-10-02). El [ADR 0010](../decisions/0010-catalogo-de-permisos-extensible.md) permite extender permisos sin acoplar el núcleo al módulo. La [conexión, prueba e información del negocio de IA](../features/inteligencia-artificial.md) es un módulo independiente; sugerencias de IA siguen en su [plan](../plans/2026-10-03-sugerencias-ia-whatsapp.md); contactos, plantillas y preparación de una segunda instalación, en el [plan de WhatsApp](../plans/2026-10-01-bandeja-whatsapp.md).

Estado: **vigente**. Consolida las decisiones implementadas hasta el 2026-10-16 en el backend
y el front. Cada premisa remite a su fuente completa; este índice no crea otra arquitectura
ni reemplaza los contratos de las áreas. Las propuestas y la adopción incompleta siguen en
[planes pendientes](../plans/README.md).

## Fuentes y precedencia

- [Backend](backend.md): capas, recorrido HTTP, persistencia, transacciones y contratos técnicos.
- [Features](../features/): reglas funcionales de identidad, administración, formularios, WhatsApp e IA.
- [ADR](../decisions/README.md): decisión, motivo, alternativas y enmiendas aceptadas.
- [Front](../../../ArquitecturaBaseFront/docs/architecture.md),
  [premisas de pantallas](../../../ArquitecturaBaseFront/docs/guides/premisas-de-desarrollo.md) y
  [contrato de mensajes](../../../ArquitecturaBaseFront/docs/guides/mensajes-y-estados.md): estructura e interacción.
- [Fundamento visual](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md): elecciones
  aprobadas, evidencia y sincronización externa pendiente.
- `AGENTS.md` resume y enlaza esas fuentes; `guides` explica cómo aplicarlas.
  Un diseño anterior sirve como referencia mientras no contradiga una regla vigente.

## Decisiones que se conservan al desarrollar

| Premisa | Fuente completa y evidencia que la protege |
| --- | --- |
| Las respuestas de formularios se consultan por FieldId y versión, con filtros privados en POST, SQL antes de paginar y facetas históricas. Excel usa permiso propio y un límite sin cola; archivos siguen privados por API. | [Planilla, detalle y Excel](../features/formularios.md#planilla-detalle-y-excel--etapa-3), `FormDraftEndpointsTests` (FormResponseHttpTests.cs), `FormResponseServiceTests` y pruebas del front contra cuerpos HTTP exportados. |
| Reglas y diseño PDF tienen revisión propia. Cada respuesta fija sus reglas y acciones; la aprobación vigente excluye al autor. Los efectos se reservan y confirman con fence; lo incierto se consulta antes de repetir. El PDF conserva la primera copia y los avisos de la revisión y su PDF usan un idioma completo, con respaldo íntegro al original. | [Reglas, motor y PDF](../features/formularios.md#reglas-con-revisiones-propias--etapa-4-g1), [canal de avisos](../features/whatsapp.md#avisos-de-reglas-de-formularios--etapa-4-g6), pruebas rápidas por corte y [verificación local completa](../features/formularios.md#verificación-de-la-etapa-4--2026-10-16). El [piloto físico](../plans/verificacion-y-produccion.md#formularios-piloto-de-aprobación-y-pdf) se acredita por separado. |
| Archivos privados por puertos neutrales; Azure sólo en Infrastructure, metadata auditada y acceso por API autorizado, sin URL públicas. Subida fuera de UoW, primera copia condicional y limpieza que respeta referencias durables pendientes; Azurite no acredita Azure real. | [Archivos privados](backend.md#archivos-privados), [guía Azure](../guides/azure-blob-en-local.md) y piloto real foto/PDF del 2026-10-15, `AzureBlobFileStoreIntegrationTests` y `WhatsAppFormSendingTests.Real_files_flow_is_atomic_private_reloadable_and_independent_of_message_retention`. |
| Cada cambio reutiliza patrones existentes, justifica piezas nuevas y revisa arquitectura además de comportamiento. La entrega distingue pruebas, revisión y pendientes; las reglas verificables tienen controles con casos positivos y negativos. | [Revisión del backend](backend.md#revisión-y-validación-de-cada-cambio), [forma de trabajo del front](../../../ArquitecturaBaseFront/docs/architecture.md#forma-de-trabajo) y [rutina compartida](../guides/validar-un-cambio.md). |
| Colas de entrega cifradas y transaccionales, claves Data Protection protegidas; reservas inciertas no se repiten al reiniciar. | [Colas](backend.md#colas-transaccionales), [ADR 0015](../decisions/0015-colas-transaccionales-cifradas.md), [ADR 0016](../decisions/0016-proteccion-de-claves-data-protection.md), `TransactionalEmailQueueTests` y `DataProtectionAtRestTests`. |
| Trabajar en `main`, con cambios acotados; ramas y push requieren pedido explícito. Leer las instrucciones de todos los ámbitos afectados. | [AGENTS.md](../../AGENTS.md#forma-de-trabajo) y [mapa de instrucciones](mapa-de-instrucciones.md). |
| Las tareas repetibles viven en `harnesses/`, fuera de la aplicación. El auditor documental se inicia a mano, comparte procedimiento entre Codex y Claude y valida las propuestas antes de aplicar Markdown. | [Arquitectura del arnés](../../harnesses/README.md), [uso del auditor](../../harnesses/documentation/README.md) y [rutina canónica](../guides/auditar-documentacion.md). |
| Un caso de uso recorre controller MVC → interfaz de servicio → servicio → puerto → Infrastructure. Application y Domain conservan sus límites. | [Recorrido HTTP](backend.md#recorrido-obligatorio-de-un-caso-de-uso-http), `LayerDependencyTests`, `ControllerServiceRepositoryTests` y `MinimalApiRoutesTests`. |
| Entrada HTTP propia en Api, mapeo manual, respuestas de Application y errores ProblemDetails traducidos. Las rutas piden permisos y forman parte del inventario vivo. | [Borde HTTP](backend.md#borde-http), [ADR 0002](../decisions/0002-contratos-http.md), `ControllerInputContractTests`, `PermissionAuthorizationTests`, `OpenApiTests` y `ExplicitRouteInventoryTests`. |
| Cada método público que escribe abre un solo límite explícito. La política decide si también se persisten los errores; helpers y repositorios no guardan por su cuenta. | [Una sola forma de guardar](backend.md#una-sola-forma-de-guardar), [ADR 0001](../decisions/0001-transaccion-explicita-por-caso-de-uso.md) y `TransactionBoundaryTests`. |
| Identity resuelve sesión y bloqueo; los datos de cuentas van por repositorios/lectores. La cookie sale después del commit. | [Identidad](../features/identidad.md), `IdentityBoundaryTests` y pruebas de ingreso. |
| Validación y logging operativo tienen componentes comunes; servicios y helpers conservan responsabilidades y un constructor de hasta ocho dependencias. | [Validación](backend.md#validación-guardado-y-errores), [helpers](backend.md#convención-de-sufijos-de-los-helpers), `RequestValidatorTests`, `OperationLoggingTests`, `ApplicationHelpersTests` y `ServiceDependencyLimitTests`. |
| Repositorios, lectores y modelos se nombran por su responsabilidad y salida. `IQueryable` queda en Infrastructure; solo los lectores usan `AsNoTracking`. | [Persistencia](backend.md#nombres-de-repositorios-y-lectores), [modelos](backend.md#modelos-response-y-row), [ADR 0008](../decisions/0008-nombres-de-repositorios-y-lectores.md), `PersistenceNamingTests` y `ServiceOutputNamingTests`. |
| Roles es el área real de referencia. Una funcionalidad nueva sigue la receta y amplía pruebas e inventario. | [ADR 0004](../decisions/0004-roles-como-area-de-referencia.md) y [agregar un área](../guides/agregar-un-area.md). |
| Domain contiene reglas puras, errores y entidades con Guid v7, sin eventos de dominio sin consumidor. Auditoría y borrado lógico se completan en interceptores. | [AGENTS.md](../../AGENTS.md#guardar-errores-y-persistencia), [ADR 0003](../decisions/0003-sin-eventos-de-dominio.md), `EntityConfigurationTests` y pruebas de persistencia. |
| Redis directo es el único caché compartido de datos; fábrica en scope propio, clave/TTL definidos e invalidación después del commit. | [Caché](backend.md#caché-compartido-en-redis), [ADR 0009](../decisions/0009-redis-como-unico-cache-de-datos.md), [agregar caché](../guides/agregar-cache.md), `CacheBoundaryTests` y pruebas con dos hosts y Redis real. |
| Todo uso de modelos comparte el motor durable de IA y un contexto de canal creado por servidor: Actor/Subject conservan permisos propios, capacidades sólo leen o proponen y el dueño ejecuta. Perfiles/instrucciones se fijan por Id/versión; audio normaliza con la misma conexión y una calificación STT independiente, sin otra clave ni worker. | [Motor por canales y audio](../features/inteligencia-artificial.md#motor-único-y-propuestas-por-canal--etapa-5), [ADR 0020](../decisions/0020-motor-unico-de-ia-con-capacidades.md), `AiAssistantChannelAccessTests`, `AiAssistantProviderContractTests` y `AiAssistantAudioOperationsTests`. La [calificación real pendiente](../guides/configurar-ia.md#evidencia-pendiente-para-habilitar-perfiles) no se acredita con dobles ni con una conexión Disponible. |
| Un módulo opcional vive en `Modules/<M>` y se compone por capa. El núcleo consume puertos y se puede compilar y probar después de quitar el módulo. | [Módulos](backend.md#módulos-opcionales), [ADR 0007](../decisions/0007-whatsapp-como-modulo-opcional.md), [quitar WhatsApp](../guides/quitar-whatsapp.md) y `ModuleBoundaryTests`. |
| El catálogo de permisos compone el núcleo y las fuentes de módulos presentes; seed, edición y autorización consumen el mismo catálogo. Quitar un módulo no obliga al núcleo a nombrarlo. | [ADR 0010](../decisions/0010-catalogo-de-permisos-extensible.md), [permiso nuevo](../guides/permiso-nuevo.md) y `PermissionCatalogTests`. |
| Migraciones y seed tienen un procedimiento único. Fuera de Development el bundle precede a la imagen; el seed es atómico e idempotente. | [ADR 0006](../decisions/0006-migraciones-y-seed-fuera-de-development.md), [migración](../guides/migracion.md), [despliegue](../guides/despliegue.md), `MigrationsTests`, `DatabaseSeederTests` y `ProductionStartupTests`. |
| El despliegue dev verifica ambos repositorios y empaqueta React/API juntos; bundle antes de revisión, OIDC y activación por variable. La validación real de Azure sigue pendiente. | [Azure dev](../deploy/azure-dev.md), [despliegue](../guides/despliegue.md) y workflows de ambos repositorios. |
| El navegador usa un solo origen. Los prefijos de backend quedan fuera del fallback del SPA; la confianza del proxy se configura explícitamente. | [Hosting](backend.md#front-y-hosting-del-spa), [prefijo de backend](../guides/prefijo-de-backend.md), [proxy](../guides/despliegue.md#proxy-y-encabezados-reenviados), `SpaHostingTests` y `ForwardedHeadersTests`. |
| Instantes almacenados y transmitidos en UTC, reloj inyectado y fechas con offset; idioma y zona generales dan valores iniciales, sin pisar las preferencias de una cuenta existente. | [Fechas](../../AGENTS.md#otras-reglas-fijas), [configuración general](../features/administracion.md#configuración-general-2026-09-30), `BannedSymbols.txt` y pruebas de configuración/localización. |
| El front comparte transporte, sesión, componentes, tokens e i18n. Filtros y paginado viven en la URL; una feature no importa otra. La renovación por 401 se coordina y se intenta una sola vez por petición. | [Arquitectura del front](../../../ArquitecturaBaseFront/docs/architecture.md), `httpClient.test.ts`, `authConfig.test.ts` y pruebas de hooks compartidos. |
| Una pantalla nueva se dibuja y se elige antes de implementarse. Las decisiones comunes terminan en tokens y UI compartida; mensajes y accesibilidad siguen sus contratos transversales. | [Premisas del front](../../../ArquitecturaBaseFront/docs/guides/premisas-de-desarrollo.md) y [fundamento visual](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md). La [unificación de mensajes](../../../ArquitecturaBaseFront/docs/plans/2026-09-30-unificacion-mensajes.md) todavía tiene adopción pendiente. |
| El cierre requiere build limpio y tests completos de ambos repositorios; integración con Docker. Si se inició Aspire, se detiene al terminar. | [Checks del backend](../../AGENTS.md#forma-de-trabajo) y [checks del front](../../../ArquitecturaBaseFront/AGENTS.md#reglas-generales). Una suite verde no acredita una prueba manual ni una sincronización externa. |

## Mantenimiento

Inicio autenticado del 2026-10-04 adapta assistant-ui al estado confirmado en PostgreSQL.
Las consultas tipadas autorizadas producen evidencia, el historial es privado y una recarga/GET
no genera. Scopes técnicos separados, reserva antes de HTTP, fencing y revisión de conexión
protegen recuperación/cancelación entre réplicas; nunca se abre una transacción durante HTTP.
La composición de capacidades ocurre fuera del caché de ajustes y no acopla núcleo/WhatsApp
al módulo IA. Fuentes: [regla funcional](../features/inteligencia-artificial.md#inicio-autenticado-con-consultas--2026-10-04),
[ADR 0017](../decisions/0017-inicio-asistente-y-operaciones-durables.md) y
[evidencia local](../reviews/2026-10-04-inicio-asistente.md). La calibración de cuotas y retención sigue en su [plan](../plans/2026-10-04-panel-conversaciones-inicio.md); la
calificación de modelos reales y la comprobación desplegada, en [verificación y producción](../plans/verificacion-y-produccion.md#inteligencia-artificial).

La búsqueda de lugares del 2026-10-04 usa Google Places desde un puerto del módulo IA,
con credencial de servidor externa, permisos de administración y HTTP acotado sin retries.
Sólo persiste Place ID; las etiquetas son temporales y el enlace se deriva con destino fijo.
La evolución aditiva conserva JSON anterior y huellas de comprobantes ya emitidos.
Fuentes: [regla funcional](../features/inteligencia-artificial.md#búsqueda-de-lugares--2026-10-04),
[configuración](../guides/configurar-google-places.md) y [evidencia](../reviews/2026-10-04-google-places.md).

Información del negocio (2026-10-03) agrega datos compartidos independientes de la
conexión, permisos propios y guardado con revisión/comprobante; no genera respuestas.
Su editor guiado conserva texto anterior y agrega ubicación/enlace, horarios locales con
fechas especiales y FAQ sin respuestas precargadas, mediante contrato/migración aditivos.
Su [fuente funcional](../features/inteligencia-artificial.md#información-del-negocio--2026-10-03)
consolida la etapa elegida. El selector de permisos de Roles usa cuerpo uniforme y
color sólo en encabezado por elección explícita; los otros listados conservan alternado.

El catálogo de modelos de IA del 2026-10-04 consulta OpenAI/Anthropic con la clave candidata
o guardada, sin persistir ni enviar una guardada a otro proveedor. El selector no acredita
compatibilidad de generación; conserva entrada manual y prueba separada. Límites, revisión,
cancelación y metadatos sólo en el editor: [fuente funcional](../features/inteligencia-artificial.md#catálogo-de-modelos--2026-10-04).

IA conserva una conexión por instalación, revisión/lock y comprobante atómico propio para
guardados inciertos, sin acoplar núcleo/WhatsApp. Cifrado autenticado con anillo externo y
rotación/restauración explícitas (ADR 0014); HTTP de prueba acotado, sin reintentos facturables.
El arnés compone contribuciones de múltiples módulos (ADR 0013). La feature administra
secretos sólo en el editor y conserva el contrato visual/de mensajes. Fuente:
[inteligencia artificial](../features/inteligencia-artificial.md); [evidencia](../reviews/2026-10-02-configuracion-llm.md).

La elección del 2026-10-01 unifica Configuración general en tres superficies y un guardado.
El PATCH valida y confirma el conjunto de ajustes con una revisión y un commit, conservando
concurrencia, relectura y borrador ante errores. La [regla funcional](../features/administracion.md#configuración-general-2026-09-30)
y el [fundamento visual](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md) registran la decisión implementada.

Cuando cambia una premisa, actualizá su fuente completa y los resúmenes que la enlazan en el
mismo desarrollo. Un cambio estructural requiere ADR; una regla funcional va a `features`;
una regla común de pantalla va a las premisas o al contrato transversal del front.
Para revisar documentación o retirar un plan, seguí [auditar documentación](../guides/auditar-documentacion.md).
La procedencia de los planes retirados se conserva en [history](../history/README.md) y Git.

La Apariencia aprobada el 2026-10-01 es configuración global persistida y publicada al
SPA, con paleta completa, revisión y descarte posterior al commit. La vista previa no
aplica el borrador fuera de su contexto y no altera colores semánticos. Fuente: [administración](../features/administracion.md#apariencia-global-2026-10-01).

El 2026-10-02 se eligió Campo únicamente como paleta: componentes, tipografía, medidas y
distribución conservan su diseño. La migración `FieldAppearanceDefaults` actualiza el conjunto
Jade original y los defaults de instalaciones nuevas; una paleta personalizada se conserva
completa. Revisión y auditoría acompañan la actualización técnica, con reversión probada.
Fuentes: [apariencia global](../features/administracion.md#apariencia-global-2026-10-01) y
[elección visual](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md#paleta-campo-aplicada-solo-colores--2026-10-02).

El refactor del chat del 2026-10-02 conserva separación de datos/acciones/viewport y presenta
mensajes mediante piezas compartidas, sin importar la maqueta. La UI no infiere entrega de
una cola ni de una revisión; adapta estados confirmados, agrupa por día del perfil y conserva
lectura/borrador. Recuperar un texto fallido nunca envía. Fuentes: [reglas de WhatsApp](../features/whatsapp.md#chat-de-atención--2026-10-02),
[premisas del front](../../../ArquitecturaBaseFront/docs/guides/premisas-de-desarrollo.md#historial-de-mensajes-2026-10-02)
y [evidencia visual](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md#contraste-de-mensajes-y-refactor-del-chat--2026-10-02).

La atención por WhatsApp es Manual por defecto desde el refactor del 2026-10-02. El acceso
usa códigos explícitos de la web y no depende del modo. Mensajes comunes no modifican
identidad; botones estructurados conocidos reciben orientación pública. La historia conserva
propósito/evento sin credenciales y una migración nueva preserva datos al convertir Bot a
Manual. Fuentes: [WhatsApp](../features/whatsapp.md#propósito-migración-y-recorridos--2026-10-02)
y [ADR 0011](../decisions/0011-separacion-acceso-y-atencion-whatsapp.md). La prueba real con
el teléfono y el payload de Meta permanece en el registro de verificación.

El panel interno de Inicio elegido el 2026-10-05 abre chat nuevo por defecto y conserva historial privado paginado con primitivas nativas de assistant-ui. El título se deriva bajo autorización y vigencia desde la primera pregunta cifrada, sin llamada LLM, columna ni migración. [Regla funcional](../features/inteligencia-artificial.md#panel-interno-e-identificación-del-historial--2026-10-05). La capacidad por instalación y el ciclo de vida completo de metadata permanecen pendientes, sin garantía de carga implícita.

El módulo opcional BusinessForms implementa borradores durables con seis rutas MVC, permisos formtemplates.read/manage y catálogo publish/records, IDs canónicos/ledger retirado, definición y comprobante en una única OnSuccess. DataProtection de propósito propio conserva resultado/mapa 30 días y purga seguida deja tombstone contra UUID repetido. Cultura/botón y ledger quedan fuera del SchemaJson de preguntas; Versiones sólo se crean al aceptar publicación. La captura de Flows agrega registros/valores tipados/recibos idempotentes mediante puertos neutrales, sin dependencias a WhatsApp. Fuente: [formularios](../features/formularios.md), los DTO/OpenAPI y ejemplos HTTP exportados y [evidencia](../features/formularios.md#verificación-de-cierre--2026-10-11). Presupuestos revisables y lock global de escrituras no acreditan calificación Meta ni rendimiento de producción.

El incremento 2a de formularios (2026-10-10) implementa chequeo lector de revisión/contenido ya guardados mediante IStructuredFormChannel y modelos neutrales; el canal es dueño del compilador/codec puro. Se conservan los seis contratos de borrador. Compatibilidad local, aceptación no instalada y evidencia externa pendiente tienen salidas separadas. En ese corte no hubo aceptación/worker/bindings ni efectos Meta; 2b se consolida abajo. Reglas y decisiones de controles/culturas en [formularios](../features/formularios.md#chequeo-local-de-publicación--incremento-2a-2026-10-10) y [WhatsApp](../features/whatsapp.md#perfil-local-de-formularios--2026-10-10); [evidencia](../features/formularios.md#verificación-de-cierre--2026-10-11). Las propuestas técnicas 2b fueron adoptadas para su implementación.

D5 de formularios (2026-10-10) consolida nombres/textos por es/en, guardado con traducciones secundarias pendientes y chequeo/compilación estrictos sin fallback. La decisión se tomó antes de los primeros commits del módulo: contrato e inicial se adaptaron directamente, sin Asp.Versioning/ADR ni convivencia ficticia. NameJson es autoridad; SQL generado para la cultura UI mantiene búsqueda/orden/facetas coherentes con el nombre de autoría mostrado. [Regla y evidencia](../features/formularios.md); D5 no agregó versiones/publicación; 2b se consolida abajo. Envío/captura están implementados; catálogos de etapa 2 siguen pendientes.

El 2b autorizado de formularios implementa aceptación sellada e idempotente, versión jsonb de preguntas y manifiesto protegido, commits separados del canal y workers con lease/fence. Activación CAS por versión base conserva borrador posterior y activa sólo tras verificar es/en; falla/revocación conserva recursos/activa anterior. WABA opcional de la misma conexión, pertenencia antes de efectos; Create/Publish inciertos sólo se consultan, nunca se repiten a ciegas ni se adoptan por nombre. Preview de versión publicada con Read, cultura UI y URL/vencimiento oficiales/no-store. [Reglas de formularios](../features/formularios.md#publicación-y-preview--implementación-2b-2026-10-10) y [canal](../features/whatsapp.md#publicación-durable-de-flows--2b-2026-10-10). La recepción local siguiente captura mediante mapping sellado y puertos neutrales; el piloto real confirmó publicación y round-trip en el alcance informado; catálogo etapa 2 y etapas posteriores permanecen pendientes.

El detalle de formularios recupera el agregado de publicación, con UUID/ruta privados del actor autorizado. Última operación y activa son proyecciones distintas; filtro/facetas Published conserva la activa anterior. Preview exige cultura explícita es/en y no-store, sin cambiar idioma UI ni cuentas. Fuente: [formularios](../features/formularios.md#recarga-de-publicación-y-cultura-de-preview--mínimo-2).

El perfil r3 de WhatsApp Flows emitió los límites como números JSON; r4 agrega la ayuda decimal aprobada sólo a publicaciones futuras. Un cambio de bytes requiere perfil nuevo y conserva las proyecciones y sesiones aceptadas. Los diagnósticos admiten el formato acotado de rutas observado en Meta y descartan texto del proveedor. El rechazo real r2, el piloto r3 y el alcance de r4 están en [WhatsApp](../features/whatsapp.md#perfil-local-de-formularios--2026-10-10).

Una versión de formulario conserva su presentación aceptada aparte del esquema/hash de preguntas; los selectores deben usar esa proyección SQL y no un borrador posterior. El backfill legacy usa el manifiesto protegido en el seed técnico existente, sin transporte externo ni otro límite. Los participantes opcionales de seed se componen mediante una interfaz técnica del núcleo. LinkRevision distingue cambios de vínculo, incluso regresar a la misma cuenta. Alcance en [formularios](../features/formularios.md) y [WhatsApp](../features/whatsapp.md).

- La selección opcional de formulario usa puerto neutral reemplazable, permiso del destinatario y presentación sellada de versión activa; busca/ordena/pagina en SQL. Revisiones de destinatario deterministas detectan cambios y ABA sin conceder autorización; un filtro sin coincidencias no invalida el contexto. El envío fija una sesión durable por versión, cifra el token y el payload y revalida WABA, vínculo y permisos antes de despachar. La recepción protege la respuesta, verifica sesión/LinkRevision, decodifica con mapping sellado y captura idempotentemente mediante puertos neutrales; `formrecords.read` gobierna los valores proyectados. El piloto físico del 2026-10-11 confirmó entrega, captura numerada y recarga; calificaciones específicas y hallazgos de cierre permanecen en el plan de formularios.
