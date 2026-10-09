# Revisión e implementación de configuración LLM

Fecha del desarrollo: 2026-10-02 (algunos timestamps UTC/migraciones son del 03).
Ambos repositorios permanecen en main, sin ramas, commits ni push. Se conservaron los
cambios anteriores, incluidos chat/acceso WhatsApp, Campo, apariencia y despliegue.
El plan se revisó contra código, AGENTS y fuentes canónicas antes de editar; los hallazgos
y el árbol se presentaron en la conversación antes de iniciar la implementación.

## Hallazgos y correcciones al plan

| Revisión | Decisión implementada |
| --- | --- |
| Independencia del núcleo/WhatsApp | Modules/ArtificialIntelligence en cuatro capas; sólo Program los compone. No usar nombre de IA en el núcleo ni puerto sin consumidor. Catálogo de permisos extensible existente. |
| Proveedores/retirada | ILlmClient prueba una conexión; dos adaptadores privados. Sin interfaz genérica de generación, capabilities, registry extensible ni futuro canal del chat. Retirada compilada/probada en copia aislada. |
| Credenciales | AES-GCM externo a PostgreSQL: Data Protection actual guardaba el anillo en la misma base. Envelope/AAD autentica instalación, propósito y proveedor; cambiar la metadata no permite descifrar para otro proveedor. Claves históricas, HMAC derivado para comprobantes. Reemplazar recifra; Keep conserva. Restore requiere base + ID/anillo. |
| Concurrencia/guardado incierto | Lock de instalación, revisión y comprobante en un commit. Dedupe por operador/UUID/HMAC, 30 días. GET404 nunca confirma rollback; reintento explícito idéntico. Conflicto obliga a revisar. |
| Complejidad | Sin worker vacío, caché de claves, default remoto de modelos, toolkit genérico ni futuro consumidor. Prueba candidata no cifra/descifra ni guarda. Operaciones secretas del editor sin MutationCache. |
| Permisos/HTTP/logs | ai.read + ai.manage; contratos redacted, errores normalizados, sin cuerpos de proveedor. Rate limiter después de autenticación para partición real por operador; límites por réplica, no presupuesto distribuido. |
| Transporte facturable | ServiceDefaults agregaba resiliencia a todos los HttpClient. IA elimina retries/hedging/loggers, redirects y URL arbitraria. Timeout total, límite de bytes/tokens y concurrencia sin cola. OpenAI store:false. |
| Sistema visual | La fuente visual decía forma pendiente mientras el plan decía maqueta aceptada. La instrucción actual de implementar desde esa maqueta autoriza promover sus tres Surface con UI existente. Artifact externo sigue pendiente. Bootstrap del laboratorio excluido del producto. |
| Arnés de tests | Partial void ya ocupado por WhatsApp no admite segunda implementación. Refactor a contribuciones descubiertas, sin nombres de módulos en el núcleo del arnés. ADR 0013; se verifican consumidores existentes y retirada real. |

Las decisiones de producto se conservan: negocio/instalación independiente, conexión
compartida, proveedor/modelo/clave de administrador, sólo configuración y prueba.
No hay uso de sesión Codex, sugerencias, automatización, conocimiento, contactos o plantillas.

## Árbol definitivo de responsabilidades

```text
Backend/src/ArquitecturaBase.Domain/Modules/ArtificialIntelligence/
  AiConnection, AiConnectionChange, AiProvider, AiCredentialChange, errores y permisos
Backend/src/ArquitecturaBase.Application/Modules/ArtificialIntelligence/
  Interfaces/{Services,Persistence,Integrations}/
  Models/, Validation/, Resources/, Channels/AiPermissionCatalogSource
  Services/{AiConnectionService,AiConnectionTestService,CandidatePolicy,ChangeRecorder}
  ArtificialIntelligenceApplicationRegistration
Backend/src/ArquitecturaBase.Infrastructure/Modules/ArtificialIntelligence/
  Persistence/{Configurations,Repositories,Readers}/ y claves del lock
  Security/{AiKeyRing,AesGcmAiSecretProtector,HmacAiChangeFingerprint,opciones}
  Providers/{OpenAI,Anthropic}/, LlmClient, LlmHttpTransport y opciones
  ArtificialIntelligenceInfrastructureRegistration
Backend/src/ArquitecturaBase.Api/Modules/ArtificialIntelligence/
  Contracts/, Controllers/AiConnectionController, opciones y registro HTTP
Backend/src/ArquitecturaBase.Infrastructure/Persistence/Migrations/
  AiConnection y snapshot de la cadena única
Backend/tests/*/Modules/ArtificialIntelligence/ y tests/Shared/
Front/src/features/artificial-intelligence/
  api/aiConnection, model, errors, hooks/{useAiConnectionEditor,useAiConnectionTest}
  pages/AiSettingsPage y pruebas
Front/src/locales/{es,en}/artificialIntelligence.json
Front/src/test/visual/artificial-intelligence.spec.ts y screenshots/ai-*.png
```

## Evidencia ejecutada

Resultados finales: build backend **0 advertencias/0 errores** y `dotnet test` **2099/2099**,
**0 omitidas**, incluida integración con PostgreSQL/Redis en Docker. Front `npm run build`
y `npm run lint` limpios (38 reglas de arquitectura), `npm run test` **845/845** en 80 archivos.
Playwright completo **80/80** (incluye login/cambio de cuenta, roles, settings, chat e IA).
`dotnet ef migrations has-pending-model-changes` confirmó modelo sin diferencias y se
generó el bundle EF local Windows con conexiones ficticias, sin aplicar a una base. Esto
acredita empaquetado local; no acredita la imagen/bundle Linux del workflow ni Azure.
La pantalla IA se verifica en español/inglés a 1440, 390 y 320 px; seis capturas nuevas
revisadas visualmente y sin overflow horizontal. Tras los últimos cambios, IA se repitió
sin actualizar referencias: **8/8**. Se comprobaron conflictos de proveedor, eliminación,
cooldown, permisos independientes, comprobantes propios, límite por operador, respuestas
malformadas/incompletas, timeout, semáforo y ausencia de retries aun con ServiceDefaults.

Retirada real en copia temporal del código de trabajo: se borraron carpetas AI, composición
Program y contribuciones del front; se generó RemoveArtificialIntelligence sólo en la copia.
Backend 2060/2060 con Docker; front build/lint y 830/830, sin IA. La migración de retirada
se revisó: Up borra sólo las dos tablas, Down las reconstruye. Playwright sin IA **72/72**.
La copia reutilizó node_modules mediante junction: al principio Vite bloqueó la fuente externa
y fallaron capturas. Se permitió únicamente ese directorio en la configuración temporal;
las capturas originales pasaron sin regenerarse. No se modificó Vite productivo para este
ensayo. No se cambió ni eliminó código en los originales al simular la retirada.

TDD observado en reglas Domain, servicios, editor/modelo, contratos HTTP de proveedores,
respuesta malformed y prueba candidata sin cifrado: fallaron primero y pasaron después.
El arnés completo protege regresiones; no se escribieron pruebas que llamen a API facturable.

## Qué fue real, qué fue simulado y qué falta

- **Real local:** EF/PostgreSQL, Redis, migraciones, commit/rollback, auditoría, locks entre
  dos hosts, permisos/catálogo/seed, AES-GCM/HMAC/rotación con bytes inventados, browser
  responsive y rutas/components productivos. Anillo local provisionado en user-secrets,
  sin mostrar claves y preservando los secretos anteriores.
- **Simulado:** OpenAI/Anthropic HTTP y tokens, correo/Meta/OIDC del navegador, tiempos y
  pérdida de respuesta del editor. TestServer hace login PKCE real del arnés; para compartir
  un operador entre hosts usa el header de autenticación de pruebas, porque sus claves de
  firma efímeras son distintas. Eso no acredita autenticación real del proveedor externo.
- **Pendiente externo:** pruebas pagas autorizadas con claves propias de ambos proveedores,
  acceso al modelo/crédito, restore de backup operativo, instalación de un segundo negocio,
  despliegue Azure (incluido vault/ring y latencia), sincronización del Artifact privado.
  Documentación oficial vigente verificada y enlazada en [configurar IA](../guides/configurar-ia.md).
- **Operación:** no se levantó Aspire. Playwright administra/detiene su Vite; no hay servicio
  del producto iniciado por esta tarea. El código no depende de abrir el laboratorio.

El plan y las maquetas previos estaban sin versionar. Se conserva su procedencia en vez
de borrar archivos sin historial recuperable; se marca la conexión promovida y el resto
pendiente. Fuentes vigentes: [feature](../features/inteligencia-artificial.md),
[ADR 0013](../decisions/0013-composicion-de-modulos-en-tests.md),
[ADR 0014](../decisions/0014-credenciales-de-ia-fuera-de-la-base.md),
[operación](../guides/configurar-ia.md) y [retirada](../guides/quitar-ia.md).
