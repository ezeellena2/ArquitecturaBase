# Configuración general de LLM como módulo opcional

Fecha: **2026-10-02**. Estado: **implementación local revisada; comprobaciones externas pendientes**.
Repositorios: `ArquitecturaBase` y `../ArquitecturaBaseFront`.

Este texto conserva la propuesta original sin versionar; no es la arquitectura vigente.
La revisión crítica corrigió abstracción, arnés multi-módulo y transporte facturable.
Decisiones finales en [la feature](../features/inteligencia-artificial.md),
[operación](../guides/configurar-ia.md), ADR 0013/0014 y [evidencia](../reviews/2026-10-02-configuracion-llm.md).
No se retira el archivo sin procedencia recuperable. Pendientes: API real de ambos proveedores,
restauración operativa de backups, despliegue y sincronización del Artifact externo.

El usuario aceptó la dirección de la maqueta local y pidió este plan antes de programar.
La pantalla estará en **Administración → Configuración → Inteligencia artificial**, ruta
SPA `/configuracion/ia`. El laboratorio `design-lab/whatsapp-chat/?screen=ia` todavía
simula las operaciones. Su ubicación física histórica no determina la del producto.
La sincronización del Artifact externo sigue pendiente; conservar esa procedencia y
documentar la elección antes de promover la pantalla, según el contrato visual.

## 1. Resultado de esta entrega

Una conexión de IA compartida por instalación, administrada inicialmente por el propietario:

- Elegir proveedor y modelo de texto.
- Cargar, reemplazar o quitar su clave de API.
- Probar credencial y modelo mediante una llamada real, breve y acotada.
- Guardar o descartar; dejar la conexión Disponible o Pausada.
- Recuperar errores, conflictos de edición y guardados de resultado incierto.
- Autorizar lectura y administración con permisos del módulo, usando roles existentes.

Cada copia del código tendrá su base, administrador, credenciales y claves de cifrado.
No se incorporan empresas a la base ni credenciales por operador. El consumidor paga
en la cuenta de API configurada en esa instalación; no se implementa facturación propia.

**Quedan para otra entrega:** sugerencias en el chat, respuestas automáticas, información
del negocio, instrucciones configurables, RAG, herramientas/acciones, análisis de archivos,
contactos, plantillas de Meta y contabilidad de consumo. Configurar una conexión no activa
un bot ni modifica el login. Una futura sugerencia seguirá necesitando revisión humana.

## 2. Proveedores: Claude, OpenAI y Codex

Primera implementación propuesta: **Anthropic y OpenAI**, probados por separado bajo un
contrato común. Google y otros proveedores se suman después con sus adaptadores y pruebas;
el selector productivo no ofrecerá adaptadores todavía inexistentes.

| Elección | Conexión del servidor | Credencial |
| --- | --- | --- |
| Claude | Anthropic Messages API, `POST https://api.anthropic.com/v1/messages` | Clave de API del workspace de esa instalación. |
| Modelo de OpenAI | Responses API, `POST https://api.openai.com/v1/responses` | Clave de API del proyecto de esa instalación. |
| Codex como agente de programación | No forma parte de esta conexión de texto | Su sesión de escritorio/CLI no se extrae ni se guarda en el sistema. |

Claude es una familia de modelos de Anthropic. Para generar texto en este producto se
utiliza su API, no una sesión de Claude Code. OpenAI es el proveedor que corresponde al
otro adaptador. Codex tiene recorridos de autenticación propios; para llamadas generales
a la API, la documentación indica usar claves de Platform. Este plan no integra ejecución
de agentes sobre repositorios ni convierte una suscripción de escritorio en credencial
del backend. Fuentes: [OpenAI API](https://developers.openai.com/api/reference/overview),
[autenticación de Codex](https://learn.chatgpt.com/docs/auth),
[Anthropic Messages](https://platform.claude.com/docs/en/api/messages/create).

Se utilizarán claves acotadas a un proyecto/workspace. Anthropic permite también claves
que actúan en varios workspaces y requieren `anthropic-workspace-id`; ese caso se deja
fuera de la primera versión, con orientación explícita para crear una clave acotada.
Si luego se necesita, el ID del workspace será un campo tipado y visible, con validación
y pruebas, no un header oculto. [Autenticación Anthropic](https://platform.claude.com/docs/en/manage-claude/authentication).

El modelo se guarda por su **identificador real**, no por el nombre ilustrativo de la
maqueta. Primera versión: campo de texto con ayuda por proveedor. No se inventa un catálogo
de todos los modelos; los adaptadores documentan las capacidades de texto admitidas y la
prueba confirma el acceso de la cuenta al modelo elegido. Antes de programar cada
adaptador se revisan otra vez parámetros, modelos compatibles y documentación vigente.
Una clave o un modelo sintácticamente válido todavía puede ser rechazado por el proveedor.

## 3. Fronteras de arquitectura

Se conserva MVC → servicio → puertos → Infrastructure, Result, FluentValidation,
transacción explícita, PostgreSQL, permisos y recursos bilingües.
El módulo se llama **`ArtificialIntelligence`**, bajo `Modules/ArtificialIntelligence`
en las cuatro capas existentes; no se crean proyectos ni un framework nuevo.

Hay dos fronteras diferentes:

1. **Contra el proveedor:** `ILlmClient`, puerto de Application del módulo. Recibe un
   pedido neutral de generación de texto y devuelve texto, uso informado y resultado
   normalizado. `LlmClient` selecciona el adaptador técnico; solo Infrastructure conoce
   HTTP, autenticación, esquemas y parámetros de Anthropic/OpenAI. El test de conexión
   utiliza este mismo recorrido con un texto fijo y breve.
2. **Contra los demás módulos:** ahora no hay consumidor de IA fuera de su configuración.
   No se crea un puerto del núcleo vacío. Al integrar la primera función real se agrega
   `ITextGenerationChannel` en `Application/Interfaces/Channels`, modelos neutrales y
   una implementación apagada del núcleo. El módulo aporta su adaptador con el patrón
   TryAdd/Replace existente. WhatsApp o Facturitas consumirán ese puerto, sin nombrar
   `ArtificialIntelligence`, sus secretos, HTTP o un SDK externo.

Ningún módulo nombra a otro. El núcleo solo compone el módulo desde `Program.cs`;
`ApplicationDbContext` no incorpora `DbSet` de IA. EF usa `Set<T>()`, configuraciones
del módulo y la cadena única de migraciones. Se agrega una guía para retirar el módulo
y una prueba real de compilación/tests sin él. No alcanza con decir que es opcional.
Referencias: [módulos opcionales](../architecture/backend.md#módulos-opcionales),
[permisos de módulos](../guides/permiso-nuevo.md), [agregar un área](../guides/agregar-un-area.md).

Los adaptadores iniciales usarán `HttpClient` tipado y contratos internos de serialización.
No hace falta un SDK de proveedor en Domain/Application ni introducir un orquestador de
agentes. Si más adelante conviene un SDK oficial, se sustituye dentro de Infrastructure,
manteniendo los puertos y sus pruebas de contrato.

## 4. Árbol propuesto del backend

Todos los siguientes archivos son **por crear**, salvo los marcados en el inventario de
modificaciones. No se crearán carpetas/clases vacías para etapas futuras. Los sufijos de
helpers, lectores, servicios y contratos siguen las reglas canónicas.

```text
src/
├─ ArquitecturaBase.Domain/Modules/ArtificialIntelligence/
│  ├─ AGENTS.md / CLAUDE.md                  # Reglas locales y enlace a la fuente funcional.
│  ├─ AiConnection.cs                       # Conexión única: proveedor, modelo, ciphertext, disponibilidad y revisión.
│  ├─ AiProvider.cs                         # Enum de proveedores implementados; sin URLs ni SDKs.
│  ├─ AiCredentialChange.cs                 # Keep / Replace / Remove, sin ambigüedad por null.
│  ├─ AiConnectionChange.cs                 # Comprobante durable de un guardado, sin credencial ni payload original.
│  ├─ AiConnectionErrors.cs                 # Errores de configuración, revisión, permisos y disponibilidad.
│  ├─ LlmErrors.cs                          # Errores normalizados del proveedor, incluida prueba incierta.
│  └─ ArtificialIntelligencePermissions.cs  # ai.read y ai.manage, más All del módulo.
│
├─ ArquitecturaBase.Application/Modules/ArtificialIntelligence/
│  ├─ AGENTS.md / CLAUDE.md
│  ├─ ArtificialIntelligenceApplicationRegistration.cs # Servicios, validadores y fuente de permisos.
│  ├─ Channels/
│  │  └─ AiPermissionCatalogSource.cs       # Extiende IPermissionCatalogSource; el seed existente compone Admin.
│  ├─ Interfaces/
│  │  ├─ Services/
│  │  │  ├─ IAiConnectionService.cs         # Leer y guardar configuración; consultar un comprobante propio.
│  │  │  └─ IAiConnectionTestService.cs     # Probar el borrador sin guardarlo.
│  │  ├─ Persistence/
│  │  │  ├─ IAiConnectionRepository.cs      # Lock/Get de la conexión y escrituras seguidas.
│  │  │  └─ IAiConnectionReader.cs          # Find de metadata/snapshot y comprobantes; sin IQueryable.
│  │  └─ Integrations/
│  │     ├─ ILlmClient.cs                   # Generación de texto neutral usada por la prueba.
│  │     ├─ IAiSecretProtector.cs           # Proteger/desproteger, sin tipos criptográficos en Application.
│  │     └─ IAiChangeFingerprint.cs         # HMAC para deduplicar payloads de guardado sin conservarlos.
│  ├─ Models/
│  │  ├─ AiConnectionResponse.cs            # Estado público, revisión y proveedores admitidos; nunca la key.
│  │  ├─ UpdateAiConnectionRequest.cs       # Cambios, expectedRevision, requestId y operación sobre la key.
│  │  ├─ AiConnectionChangeResponse.cs      # Guardado confirmado y revisión resultante de ese requestId.
│  │  ├─ TestAiConnectionRequest.cs         # Proveedor/modelo/key candidata o uso explícito de la guardada.
│  │  ├─ AiConnectionTestResponse.cs        # Prueba concluida, uso informado y contexto de revisión, sin secreto.
│  │  ├─ AiConnectionSnapshot.cs            # Proyección interna consistente; puede contener ciphertext, no sale por HTTP.
│  │  ├─ LlmTextRequest.cs                  # Entrada neutral; secreto transitorio con ToString redactado.
│  │  └─ LlmTextResponse.cs                 # Texto y uso informado por el proveedor; sin JSON externo.
│  ├─ Services/
│  │  ├─ AiConnectionService.cs             # Validar, serializar guardados, proteger key y confirmar cambios.
│  │  ├─ AiConnectionTestService.cs         # Resolver candidato, llamar al LLM y clasificar el resultado.
│  │  └─ AiConnectionCandidatePolicy.cs     # Resolver Keep/Replace/Remove y cambio de proveedor para ambos usos.
│  ├─ Validation/
│  │  ├─ UpdateAiConnectionRequestValidator.cs # Forma, longitudes, revisión y combinación de campos.
│  │  └─ TestAiConnectionRequestValidator.cs   # Pedido de prueba válido; nunca un prompt libre del navegador.
│  └─ Resources/
│     ├─ Permissions.resx                  # Etiquetas/descripciones del catálogo del módulo.
│     └─ Permissions.en.resx
│
├─ ArquitecturaBase.Infrastructure/Modules/ArtificialIntelligence/
│  ├─ AGENTS.md / CLAUDE.md
│  ├─ ArtificialIntelligenceInfrastructureRegistration.cs # Repositorio, lector, cifrado, clientes y límites.
│  ├─ AiTransportOptions.cs                # Timeout, tamaño máximo de respuesta y concurrencia por réplica.
│  ├─ Providers/
│  │  ├─ LlmClient.cs                       # Implementa ILlmClient; selecciona un adaptador registrado.
│  │  ├─ ILlmProviderClient.cs              # Contrato interno técnico de adaptadores, sin salir de Infrastructure.
│  │  ├─ LlmRequestLimiter.cs               # Cupo acotado por proceso; cancelación y rechazo sin cola ilimitada.
│  │  ├─ OpenAI/
│  │  │  ├─ OpenAiResponsesClient.cs        # POST Responses; autenticación por petición y normalización.
│  │  │  └─ OpenAiContracts.cs              # JSON interno de pedido, salida, uso y error del proveedor.
│  │  └─ Anthropic/
│  │     ├─ AnthropicMessagesClient.cs      # POST Messages; versión, autenticación y normalización.
│  │     └─ AnthropicContracts.cs           # JSON interno; no expone tipos de Anthropic al resto.
│  ├─ Security/
│  │  ├─ AiSecretProtectionOptions.cs       # ActiveKeyId y key ring secreto, externos a PostgreSQL.
│  │  ├─ AesGcmAiSecretProtector.cs         # AES-GCM autenticado, nonce aleatorio y sobre versionado.
│  │  └─ HmacAiChangeFingerprint.cs         # Fingerprint canónico con clave derivada y propósito separado.
│  └─ Persistence/
│     ├─ AiAdvisoryLockKeys.cs              # Clave estable del lock de conexión, incluida en tests de transacción.
│     ├─ Configurations/
│     │  ├─ AiConnectionConfiguration.cs    # Unicidad de conexión, revisión y longitudes.
│     │  └─ AiConnectionChangeConfiguration.cs # Índice único de requestId y comprobante sin key.
│     ├─ Repositories/
│     │  └─ AiConnectionRepository.cs       # EF seguido y locks; nunca SaveChanges fuera del UnitOfWork.
│     └─ Readers/
│        └─ AiConnectionReader.cs           # Proyecciones AsNoTracking y búsqueda del comprobante propio.
│
├─ ArquitecturaBase.Infrastructure/Persistence/Migrations/
│  ├─ <timestamp>_AiConnection.cs           # Tablas del módulo, en la cadena única.
│  ├─ <timestamp>_AiConnection.Designer.cs
│  └─ ApplicationDbContextModelSnapshot.cs  # Existente: snapshot actualizado por EF.
│
└─ ArquitecturaBase.Api/Modules/ArtificialIntelligence/
   ├─ AGENTS.md / CLAUDE.md
   ├─ ArtificialIntelligenceApiRegistration.cs # Políticas de rate limit y configuración HTTP del módulo.
   ├─ AiRateLimitOptions.cs                 # Límites configurables de administración/prueba por operador.
   ├─ Contracts/
   │  ├─ UpdateAiConnectionHttpRequest.cs    # Entrada HTTP con ToString redactado y mapeo manual.
   │  └─ TestAiConnectionHttpRequest.cs      # Entrada HTTP de prueba con ToString redactado.
   └─ Controllers/
      └─ AiConnectionController.cs          # Servicios + HasPermission + conversión de Result/OpenAPI.
```

Los contratos secretos de Application sobrescribirán también `ToString()` como defensa
adicional, aunque la obligación MVC sea la del contrato HTTP. No usar records con una
representación automática del secreto sin corregirla. Ciphertext y fingerprints también
se excluyen de respuestas, diagnósticos y logging de entidades.

## 5. Árbol propuesto del front

IA es una **feature propia**, no una carpeta de WhatsApp ni una ampliación del formulario
de `SystemSettings`. Compartir ubicación en el menú no mezcla entidades ni servicios.
Se conservan AppLayout, Page, Surface, Campo, Public Sans, controles compartidos y el
contrato de notificaciones. La feature no importa `settings` ni `whatsapp`.

```text
ArquitecturaBaseFront/src/
├─ features/artificial-intelligence/
│  ├─ AGENTS.md / CLAUDE.md                 # Alcance, permisos, privacidad y referencias canónicas.
│  ├─ model.ts                             # Borrador tipado, operación de key y estados de edición/prueba.
│  ├─ model.test.ts                        # Transiciones, dirty, reemplazo, quita y cambio de proveedor.
│  ├─ errors.ts                            # Códigos propios; delega fallbacks técnicos en shared.
│  ├─ api/
│  │  ├─ aiConnection.ts                    # httpClient: leer, guardar, probar y consultar comprobante.
│  │  └─ aiConnection.test.ts              # Contratos y revisión; ninguna respuesta contiene key.
│  ├─ hooks/
│  │  ├─ useAiConnection.ts                # TanStack Query: metadata fresca para iniciar el editor.
│  │  ├─ useAiConnectionEditor.ts          # Borrador, guardado/reconciliación y protección al salir.
│  │  ├─ useAiConnectionEditor.test.tsx    # Conflictos, incertidumbre y conservación del borrador.
│  │  ├─ useAiConnectionTest.ts            # Una prueba explícita; invalida resultado al cambiar el candidato.
│  │  └─ useAiConnectionTest.test.tsx      # Cancelación, resultado viejo, doble clic y error del proveedor.
│  ├─ components/
│  │  ├─ AiConnectionForm.tsx              # Tres Surface: proveedor/modelo, key/prueba, disponibilidad.
│  │  ├─ AiCredentialField.tsx             # Key solo transitoria; configurar/reemplazar/cancelar/quitar.
│  │  └─ AiConnectionReview.tsx            # Comparación de metadata y recuperación de conflicto; no muestra secretos.
│  └─ pages/
│     ├─ AiSettingsPage.tsx                # Page, permisos, carga, Guardar/Descartar y coordinación.
│     └─ AiSettingsPage.test.tsx           # Permisos, validación, mensajes únicos y recorridos completos.
├─ locales/
│  ├─ es/artificialIntelligence.json       # Todo texto de la pantalla en español rioplatense.
│  └─ en/artificialIntelligence.json       # Mismas claves en inglés.
└─ test/visual/
   ├─ ai-settings.spec.ts                  # Página real: geometría/estados en es/en a 1440, 390 y 320 px.
   ├─ ai-settings-flow.spec.ts             # Guardar/probar/quitar/conflicto/resultado incierto.
   └─ screenshots/ai-settings-*.png        # Evidencia revisada del diseño elegido.
```

La prueba exitosa es un estado temporal de la pantalla, no un dato editable ni una promesa
de disponibilidad permanente. No se guardan la key ni su borrador en localStorage,
sessionStorage, URL, TanStack Query o herramientas de analytics. TanStack Query conserva
solo metadata; las mutaciones que transporten credenciales no retienen variables/resultados
después de concluir. Evitar persistir o inspeccionar esas mutaciones desde devtools y
limpiar estado al guardar, descartar, cambiar proveedor, perder permiso o cerrar sesión.
Las trazas de pruebas/snapshots usan únicamente secretos ficticios.

## 6. Archivos existentes a modificar al implementar

| Archivo o conjunto | Cambio acotado |
| --- | --- |
| `Api/Program.cs` | Tres using y bloque de registros del módulo, como WhatsApp. No lógica de IA en el composition root. |
| `Application/Resources/Errors{.en}.resx` | Traducciones de códigos del módulo con el resolvedor actual, sin inventar otro resolvedor. |
| `Application/Resources/Validation{.en}.resx` y `ValidationMessages.cs` | Mensajes de validación nuevos y claves tipadas cuando corresponda. |
| `Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` | Solo la generación de EF; no editar manualmente. |
| `Api/appsettings.json` | Valores técnicos públicos de timeout/rate limit; ninguna key. |
| `ArquitecturaBaseFront/src/app/routes.tsx` | Ruta lazy `/configuracion/ia` bajo sesión + `ai.read`, independiente de `settings.manage`. |
| `ArquitecturaBaseFront/src/layouts/navigation.ts` y su test | Enlace en Configuración con `ai.read`; sin mutar navegación desde una feature. El grupo aparece si algún hijo es visible. |
| `ArquitecturaBaseFront/src/locales/{es,en}/common.json` | Etiqueta del menú/migas. |
| `ArquitecturaBaseFront/src/test/mocks/handlers.ts` | Dobles HTTP del nuevo contrato; no respuestas falsas en src productivo. |
| `ArquitecturaBaseFront/src/test/visual/{fixtures.ts,preview.tsx}` | Registrar página real y escenarios aislados; ampliar el arnés existente. |
| Tests con ganchos partial | Añadir partes del módulo, nunca referencias del núcleo al módulo. Inventario, permisos, DI, helpers y locks. |
| Documentación, mapas y pares AGENTS/CLAUDE | Responsabilidades del módulo y feature, operación, retirada y evidencia. |

No se modifican `SystemSettings`, su tabla ni su servicio para guardar secretos de IA.
No se cambian contratos del chat, webhook, acceso o envío a Meta en esta entrega. Si se
detecta una pieza técnica realmente compartida necesaria, se refactoriza en su capa dueña
con tests de sus consumidores; no se copia código de una feature dentro de otra.

## 7. Persistencia, secretos y despliegue

**PostgreSQL:** `AiConnection` mantiene una fila lógica única, con ID de Entity y restricción
única del ámbito de instalación; no se agrega TenantId. Al no existir fila, GET devuelve
estado vacío y revisión inicial, sin sembrar una key. Guardar por primera vez y guardar
después toman el mismo lock. La fila tiene Provider, Model, ProtectedApiKey, Enabled y
Revision. Los interceptores completan la auditoría, nunca a mano. Cada cambio confirmado
avanza Revision. No se requieren cachés de configuración/credenciales para esta entrega.

**Cifrado propuesto:** AES-GCM mediante la BCL de .NET, detrás de `IAiSecretProtector`;
no un algoritmo casero. Un sobre versionado incluye keyId, nonce, tag y ciphertext;
nonce criptográficamente aleatorio, tag de 128 bits, clave de 256 bits y datos asociados
que atan formato/propósito/instalación. Los fingerprints usan una subclave derivada con
HKDF y propósito distinto; el comprobante conserva su keyId para verificar un reintento
tras rotar la clave activa. [AesGcm en .NET](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm?view=net-10.0).

La clave de cifrado estará **fuera de la base de datos**. Ya existe Data Protection con
key ring persistido en la misma base y sin protección externa configurada; reutilizarlo
sin resolver esa protección no separaría la key del LLM de un backup completo de la base.
La propuesta no modifica Data Protection del login.

| Dato | Dónde se configura |
| --- | --- |
| Proveedor, modelo, key de API y Disponible/Pausada | Pantalla del administrador. El servidor guarda metadata + ciphertext. |
| `ArtificialIntelligence:SecretProtection:InstallationId` | Identificador estable del despliegue, público; distinto entre instalaciones, igual entre réplicas. |
| `ArtificialIntelligence:SecretProtection:ActiveKeyId` | Configuración técnica del host; identifica la clave activa. |
| `ArtificialIntelligence:SecretProtection:Keys:<id>` | user-secrets de Api en local; secretos del orquestador/secret manager en producción. Nunca appsettings del repo. |
| Timeout, cupo de concurrencia y rate limit | Opciones técnicas del host, con valores acotados y validados. |

Mientras el módulo esté registrado, el despliegue debe provisionar primero su clave
de cifrado; la validación de arranque rechaza un key ring ausente/mal formado. No hace
falta una key de Anthropic/OpenAI para iniciar: la conexión funcional comienza vacía.
Al quitar el módulo y sus registros, ese requisito técnico también desaparece.

Varias réplicas de la misma instalación comparten base, InstallationId y key ring externo.
Los cambios son visibles en nuevos pedidos sin reiniciar ni quedar ligados al HttpClient
del arranque. Rotar la key de API se hace en la pantalla. Rotar el cifrado exige conservar
claves anteriores para leer ciphertext/comprobantes; no se retirarán hasta completar su
reprotección y verificar backups. Esta entrega documenta el procedimiento; no crea una
consola de rotación ni un worker vacío. Se guarda una decisión de seguridad con el siguiente
ADR disponible al implementar.

Restaurar una instalación necesita su base y el key ring correspondiente. Clonar el código
para otra empresa usa datos y secretos nuevos; no copiar una base productiva como plantilla.
El cifrado protege el almacenamiento; el proceso servidor debe poder descifrar para enviar
la llamada y sigue siendo un ámbito de confianza.

## 8. Contratos HTTP y permisos

| Ruta | Permiso | Resultado |
| --- | --- | --- |
| `GET /api/ai/connection` | `ai.read` | Metadata, revisión, estado vacío/configurado/pausado y proveedores admitidos. Sin key, ciphertext ni fingerprint. |
| `PUT /api/ai/connection` | `ai.manage` | Cambio atómico con expectedRevision y requestId. Devuelve metadata + comprobante. |
| `GET /api/ai/connection/changes/{requestId}` | `ai.manage` | Comprobante del operador autenticado, para recuperar un guardado incierto; 404 si no existe o no es suyo. |
| `POST /api/ai/connection/test` | `ai.manage` | Prueba real del candidato sin guardar sus cambios. Texto fijo del servidor y respuesta de diagnóstico normalizada. |

Admin obtiene ambos permisos por el catálogo extensible/seed existente. User no los
obtiene. Editar requiere también poder leer la pantalla: la experiencia exige ambos,
y el servidor exige `ai.read` además de `ai.manage` en acciones administrativas.
Leer no permite editar/probar, y `settings.manage` no concede IA automáticamente.
No se agrega `ai.use` hasta que exista un consumidor funcional que necesite autorización.

`credentialChange` es Keep, Replace o Remove. Replace exige `apiKey`; Keep no recibe
una key devuelta por GET. Cambiar proveedor requiere Replace o Remove: nunca se manda
una credencial de Anthropic a OpenAI. Remove fuerza Enabled=false. Disponible requiere
proveedor, modelo y key configurada; se puede guardar sin prueba previa, pero eso no se
presenta como conexión verificada. La prueba puede ejecutarse estando Pausada porque
es una acción administrativa explícita.

Cada ruta se inventaría y prueba; errores en ProblemDetails con code, traceId y
validaciones camelCase, en es/en. 401/403/409/429 conservan el borde común. Un 401 del
proveedor se normaliza como credencial del LLM inválida y no como sesión de nuestra app
vencida. Un 429 externo no se confunde con el límite interno; ambos dan recuperación
clara con retryAfter cuando se dispone de él. Nunca se reenvía un body de error externo
sin sanear, que podría contener datos o secretos.

## 9. Recorridos y concurrencia

### Abrir y editar

Se espera una lectura fresca de metadata antes de habilitar el editor. Un refetch no pisa
un borrador iniciado. Se distingue key guardada de nueva key transitoria. Cambiar proveedor
y quitar key requieren ConfirmDialog; solo se aplican al Guardar. Cancelar reemplazo
conserva la key anterior. Descartar limpia la key candidata y vuelve al estado confirmado.

### Guardar y recuperar una respuesta perdida

1. Validar forma y armar un fingerprint protegido del pedido con requestId propio.
2. `AiConnectionService.UpdateAsync` abre **un** UnitOfWork/OnSuccess.
3. Tomar lock de conexión; buscar comprobante por operador/requestId. Un reintento idéntico
   devuelve el comprobante; reutilizar ID con otro payload responde conflicto.
4. Comprobar expectedRevision, resolver la política del candidato, cifrar según corresponda,
   escribir conexión y comprobante en la misma transacción. No llamar al proveedor.
5. Después del commit, responder e invalidar la consulta de metadata en el cliente.

Si se pierde la respuesta, la UI conserva borrador/requestId y ofrece **Consultar guardado**;
consulta el comprobante y metadata actual. No compara ni vuelve a pedir el valor de la key.
Un comprobante confirma esa escritura, pero no garantiza que otro operador no haya cambiado
la conexión después: se compara su revisión con la actual antes de mostrar el estado.
Un 404 del comprobante no prueba rollback mientras el pedido original podría seguir en
curso; un reintento explícito reutiliza el mismo requestId y payload, serializado por el lock.
No reintentar automáticamente ni generar otro ID ante un timeout. Con conflicto se conserva
el borrador y se revisan los cambios ajenos antes de intentar guardar otra revisión.

`AiConnectionChange` conserva requestId, operador, fingerprint/keyId, revisión resultante
y metadata necesaria para el comprobante, sin key ni borrador. Se define retención operativa
antes del cierre; al purgar, el expectedRevision sigue impidiendo reaplicar silenciosamente
una edición vieja. La garantía de consulta/deduplicación se documenta con ese plazo, no eterna.

### Probar

La UI manda proveedor/modelo del borrador y Replace con key candidata, o Keep con la
revisión guardada. El servidor valida, lee un snapshot consistente, resuelve/descifra
solo en memoria y ejecuta un pedido breve con entrada fija y salida acotada. La prueba
no modifica AiConnection ni abre una transacción que abarque HTTP. Su éxito pertenece
al candidato probado; cambiar proveedor/modelo/key o recibir una revisión nueva invalida
la indicación. Respuestas tardías no acreditan un borrador distinto.

Probar comprueba autenticación y capacidad de generar texto con el modelo, no solo listar
modelos. Puede consumir uso facturable del proveedor; se avisa junto al botón. No se manda
historial de WhatsApp ni información de negocio. La respuesta del proveedor se inspecciona
para comprobar una generación válida, pero no se publica texto arbitrario en el diagnóstico.
No se promete gratuidad ni se usa como prueba de disponibilidad permanente.

### Fallas externas y límites

- Timeout total propuesto de 15 s, salida de prueba pequeña y respuesta HTTP con tamaño
  máximo; valores finales se verifican con los modelos compatibles, incluido reasoning.
- Cancelación propagada, sin cola ilimitada; cupo inicial propuesto de 2 llamadas concurrentes
  **por réplica**, ajustable. La política HTTP limita frecuencia por operador.
- HttpClient sin loggers de headers/body y sin retries/hedging de POST, también frente a
  defaults globales de ServiceDefaults. No repetir una generación que quizá ya se cobró.
- Una prueba que vence puede haber sido procesada/cobrada; se informa resultado incierto.
  Volver a probar es una nueva llamada explícita. Esta entrega no promete exactly-once
  para el proveedor ni un presupuesto mensual distribuido.
- Autenticación inválida, modelo inaccesible/incompatible, falta de cuota, saturación,
  respuesta ilegible, respuesta vacía/truncada y timeout tienen clasificación distinta.
- No hay fallback a otro proveedor, key ajena o modelo sin decisión del administrador.

Estos límites acotan recursos en el despliegue inicial. Al crecer, el cupo total crece con
el número de réplicas: no se presenta el limiter local como control global de gastos.
Una entrega de consumo/producción masiva necesitará cuotas durables por instalación y
coordinación compartida; si incorpora caché, usa solo el adaptador Redis canónico.

La IA no se convierte en condición de salud del login/chat: no hacer llamadas facturables
en health checks ni bloquear la app por caída del proveedor. Métricas: proveedor/modelo,
duración, categoría de resultado y tokens informados, sin key, prompt, texto, teléfono ni
userId como etiqueta de alta cardinalidad. Logs LoggerMessage y traceId/request ID saneado.

## 10. Pruebas y documentación a crear

```text
tests/
├─ ArquitecturaBase.Domain.UnitTests/Modules/ArtificialIntelligence/
│  ├─ AiConnectionTests.cs                 # Reglas, revisión, quita y unicidad lógica.
│  ├─ AiConnectionChangeTests.cs           # Comprobantes sin secreto e invariantes.
│  └─ ArtificialIntelligencePermissionsTests.cs # Catálogo y formato.
├─ ArquitecturaBase.Application.UnitTests/Modules/ArtificialIntelligence/
│  ├─ Services/AiConnectionServiceTests.cs  # UnitOfWork, política, permisos contextuales y deduplicación.
│  ├─ Services/AiConnectionTestServiceTests.cs # Candidato/revisión; HTTP fuera de locks; prueba no guarda.
│  ├─ Validation/AiConnectionValidatorsTests.cs # Campos y combinaciones; no llega al proveedor si falla.
│  ├─ Resources/ResourceParityTests.ArtificialIntelligence.cs # Permisos es/en.
│  └─ TestDoubles/AiFakes.cs                # Repositorios, clock, cifrado y proveedor ficticios.
├─ ArquitecturaBase.Api.IntegrationTests/Modules/ArtificialIntelligence/
│  ├─ ApiFactory.ArtificialIntelligence.cs # Gancho del arnés; proveedor HTTP simulado, PG/Redis reales.
│  ├─ ExplicitRouteInventoryTests.ArtificialIntelligence.cs # Cuatro rutas y autorización.
│  ├─ AiConnectionEndpointsTests.cs        # 401/403, body, validación, códigos, culturas y OpenAPI.
│  ├─ AiConnectionConcurrencyTests.cs      # Dos hosts, revisión, primer guardado y requestId repetido.
│  ├─ AiConnectionPersistenceTests.cs      # Rollback atómico, reinicio, lectura en otra réplica y cifrado.
│  ├─ AiProviderContractTests.cs           # HTTP real contra handler simulado; JSON/auth/normalización de ambos.
│  ├─ AiSecretProtectionTests.cs           # Alteración, propósito, pérdida/rotación de keys y lectura cruzada.
│  └─ AiPrivacyTests.cs                    # Logs estructurados, ProblemDetails y respuestas sin secretos.
└─ ArquitecturaBase.ArchitectureTests/Modules/ArtificialIntelligence/
   ├─ DependencyInjectionTests.ArtificialIntelligence.cs # Registro completo y repetible.
   ├─ ApplicationHelpersTests.ArtificialIntelligence.cs  # Convenciones de helpers.
   └─ TransactionBoundaryTests.ArtificialIntelligence.cs # Locks/escrituras y prefijo propio.

docs/                                      # Se agregan con la implementación, no como hechos hoy.
├─ features/inteligencia-artificial.md      # Fuente funcional: alcance, secretos, concurrencia y errores.
├─ guides/configurar-ia.md                  # Cuenta API, modelo/key, prueba, operación y recuperación.
├─ guides/quitar-ia.md                      # Retirar módulo/front, generar migración y probar el núcleo.
└─ decisions/<siguiente>-credenciales-ia.md # Decisión de seguridad, operaciones y restauración.
```

Cada carpeta de módulo en src/tests recibe AGENTS/CLAUDE según el mapa, sin duplicar la
guía entera. Actualizar backend/frontend mapas, premisas y fuentes vigentes **al implementar**.
Los tests existentes de capas, ModuleBoundaryTests, ErrorDeclarationTests, permisos,
recursos, rutas, locks y ubicación de persistencia deben seguir pasando.

Casos de cierre: ninguna credencial en GET/log/errores/QueryCache; dos operadores no pisan
ediciones; doble guardado con el mismo ID no repite la escritura; reintento con payload
distinto es rechazado; retirar el módulo deja identidad/chat operativos; reemplazar key
surte efecto en otro host sin reiniciar; test no guarda; prueba fallida no elimina la key
actual; caída del proveedor no afecta el ingreso. Para contratos HTTP de proveedor se
usan dobles sin conexión externa; eso no acredita una key ni un modelo de cuenta real.

## 11. Orden de implementación y aceptación

1. **Contrato y preparación:** confirmar este alcance, elegir un modelo inicial compatible
   de cada proveedor al implementar, completar mensajes/casos de maqueta y registrar la
   elección visual. Revisar todas las instrucciones locales; provisionar cifrado de desarrollo.
2. **Dominio y permisos:** tests en rojo, entidades, errores, catálogo y recursos bilingües.
3. **Configuración durable:** puertos, servicio, repositorio/lector, comprobantes, cifrado,
   migración y endpoints. Probar concurrencia y resultado incierto antes de HTTP externo.
4. **Conexión externa:** adaptadores OpenAI/Anthropic detrás de ILlmClient, normalización,
   cupos/timeouts, logs saneados y prueba de conexión; tests contractuales de ambos.
5. **Pantalla productiva:** feature propia, ruta/permisos reales, formulario aprobado,
   estado transitorio de key, consulta de comprobantes y Toaster compartido. Sustituir
   simulaciones, no copiar el bootstrap que muta navegación en el laboratorio.
6. **Verificación y operación:** build sin warnings + dotnet test completo con Docker;
   front build/lint/tests + Playwright es/en responsive; regresión de Configuración,
   roles, login y chat; arranque sin key de proveedor, reinicio/segunda réplica y prueba
   de retirar IA. `aspire stop` si se levantó Aspire. Conservar evidencia fechada.
7. **Prueba real acompañada:** configurar una key de API desde la pantalla en su instalación
   y probar el modelo elegido. No pedir que se pegue la key en el chat. Registrar solo
   resultado/uso sin secreto. Repetir en una segunda instalación con secretos propios
   para acreditar el caso de clonación; no declararlo probado por tests simulados.
8. **Cierre documental:** consolidar reglas implementadas, rescatar pendientes y retirar
   este plan según la guía de auditoría. Si falta integración con Docker o prueba real,
   informar exactamente qué está pendiente; no dar toda la entrega por cerrada.

Cada etapa se puede revisar antes de avanzar. Las decisiones de arquitectura existentes
prevalecen; una desviación exige su ADR, no un parche que esquive tests.

## 12. Próxima entrega: consumir IA desde el chat (solo previsión)

Al abordar sugerencias se añadirá el puerto neutral del núcleo y su adaptador del módulo,
con los permisos y pruebas del consumidor. Se diseñarán selección de contexto autorizado,
instrucciones/fuentes del negocio, protección frente a instrucciones en mensajes recibidos,
evaluaciones y persistencia/origen de sugerencias. La salida generada se revisa y luego
se incorpora al borrador; el envío sigue el flujo durable de WhatsApp ya existente.

No se crea hoy ese árbol ni se habilita `POST /generate` libre para el navegador. El
primer entregable termina cuando se puede configurar y probar la conexión con seguridad,
manteniendo login y chat como funcionan actualmente.
