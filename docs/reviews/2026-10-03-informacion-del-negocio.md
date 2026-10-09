# Información del negocio: implementación y verificación

Fecha: **2026-10-03**. El usuario eligió implementar la pantalla en la aplicación real,
después de revisar la maqueta. También pidió ver menú/permisos y quitar el color de las
filas de permisos del rol. La entrega implementa esas tres cosas; sugerencias y generación
de respuestas continúan pendientes en el [plan](../plans/2026-10-03-sugerencias-ia-whatsapp.md).

## Revisión y decisiones implementadas

La arquitectura vigente exige MVC → servicios → puertos, una transacción explícita por
escritura y módulos opcionales independientes. Información del negocio pertenece a
ArtificialIntelligence, con entidad, servicio y editor propios: no amplía AiConnection,
no depende de WhatsApp y no agrega un puerto de generación sin consumidor. El núcleo
no contiene referencias al módulo. [Reglas funcionales](../features/inteligencia-artificial.md#información-del-negocio--2026-10-03).

El formulario conserva los siete campos aprobados: nombre, descripción, horario, ubicación,
productos/servicios, políticas/condiciones y preguntas/respuestas. Nombre y al menos un hecho
son obligatorios. Guardar persiste la revisión actual; no publica, genera ni envía mensajes.
No hay información ficticia inicial ni historial de textos por simetría.

Permisos ai.business.read/ai.business.manage, independientes de ai.read/ai.manage. El catálogo
compuesto incorpora ambos a Admin al arrancar; el seed conserva invalidación de roles. La
página `/configuracion/negocio` aparece en Administración → Configuración sólo con lectura.
El servidor autoriza cada ruta; sólo lectura muestra hechos sin acciones de guardado.

Una instalación conserva una fila auditable Scope=1. PUT valida, toma su propio advisory lock,
deduplica operador/UUID, compara revisión y confirma hechos/comprobante juntos. El comprobante
conserva revisión y huella HMAC, sin copiar textos. Ventana de recuperación de 30 días; el
siguiente guardado elimina vencidos. La huella usa el anillo externo con propósito HKDF propio;
la refactorización conserva el propósito existente de la conexión y sus consumidores.

El navegador congela cuerpo/UUID ante una respuesta incierta. Consultar el comprobante no
repite la escritura. Un 404 permite consultar otra vez o repetir explícitamente el mismo
pedido; no demuestra rollback. Conflictos requieren leer y elegir cómo revisar los valores.
No hay reintentos automáticos ni borradores en storage, URLs o cachés de mutations/queries.
Los hechos confirmados usan QueryCache de la sesión y su limpieza compartida.

La tabla de permisos usa variantes comunes de TableBody/TableRow para filas uniformes,
incluidas las secciones expandidas. Conserva el color del encabezado y los estados de
hover/foco. El valor por defecto de esos componentes mantiene las tablas alternadas de
los otros consumidores; no se agregaron selectores CSS exclusivos para Roles.

## Árbol definitivo

```text
Domain/Modules/ArtificialIntelligence/
  AiBusinessInformation, AiBusinessInformationChange, AiBusinessInformationErrors
  ArtificialIntelligencePermissions
Application/Modules/ArtificialIntelligence/
  Interfaces/{Services,Persistence,Integrations}/
  Models/, Validation/, Resources/
  Services/AiBusinessInformationService
Infrastructure/Modules/ArtificialIntelligence/
  Persistence/{Configurations,Repositories,Readers}/
  Security/HmacAiBusinessInformationFingerprint, AiKeyRing
Api/Modules/ArtificialIntelligence/
  Contracts/UpdateAiBusinessInformationHttpRequest
  Controllers/AiBusinessInformationController
Infrastructure/Persistence/Migrations/20261003134826_AddAiBusinessInformation
tests/*/Modules/ArtificialIntelligence/

Front/src/features/artificial-intelligence/
  api/businessInformation, businessInformationModel
  hooks/useBusinessInformationEditor, pages/BusinessInformationPage
Front/src/{app/routes,layouts/navigation,locales/{es,en}/businessInformation.json}
Front/src/shared/ui/table + PermissionPicker
Front/src/test/visual/business-information.spec + fixtures y seis capturas
```

## Verificaciones ejecutadas

| Verificación | Resultado |
| --- | --- |
| Backend `dotnet build ArquitecturaBase.slnx -c Release` | Sin advertencias ni errores. |
| Backend `dotnet test -c Release` | **2137 pasados**, 0 fallidos/omitidos; incluye PostgreSQL 18.3 y Redis 8.6 mediante Docker. |
| Integración nueva de Información del negocio | **11 pasados**; también incluidos en la suite completa. |
| Refactorización del anillo y pruebas adicionales de huellas | **3 pasados** en AiSecretProtectionTests, con claves ficticias y assertions de rotación, propósito e instalación. |
| `dotnet ef migrations has-pending-model-changes --configuration Release --no-build` | Modelo sin cambios pendientes respecto de la última migración. |
| Front `npm run build` | TypeScript y Vite correctos. |
| Front `npm run lint` | Correcto; incluye 39 reglas/pruebas de arquitectura visual. |
| Front `npm test` | **886 pasados**, 84 archivos. |
| Front `npm run test:visual` | **95 pasados**, incluidas regresiones de login, chat, roles y configuración. |
| Nuevas pruebas Playwright | **13 pasados**; es/en, 1440/390/320 px, guardado, FAQ al final del formulario, permisos, incertidumbre, revisión y filas sin color. |

Se aplicó TDD a reglas del dominio, coordinación del servicio y lógica del editor. La
integración verifica autorización de las tres rutas, errores es/en sin eco de datos,
auditoría, reinicio del host, caducidad y operaciones propias; dos hosts verifican deduplicación
y conflicto de revisión. Una falla de commit prueba rollback de hechos y comprobante.

La migración Up agrega sólo AiBusinessInformation/AiBusinessInformationChanges y sus índices;
Down elimina esas dos tablas. La cadena previa se conserva. Las pruebas aplicaron migraciones
en PostgreSQL de Testcontainers. No se actualizó directamente la base del usuario.

Backend se verificó en Release porque la API de Debug está abierta desde Visual Studio y
mantiene sus DLL bloqueados. No se detuvo esa sesión. Para ver la funcionalidad local, reiniciar
esa API aplica el arranque de Development y su seed, y recargar el front renueva la sesión/
catálogo. En Production se aplica el bundle antes de publicar el nuevo código.

No se crearon ramas, commits ni push. Se preservaron cambios existentes de ambos repositorios.
No se levantó Aspire durante esta entrega; no quedó una instancia nueva que apagar.

## Dobles y pendientes reales

PostgreSQL/Redis de integración, transacciones, criptografía y migraciones son reales. Las
claves criptográficas son ficticias. Proveedores OpenAI/Anthropic/Meta, OIDC/correos y HTTP del
catálogo Playwright son dobles. Las capturas usan componentes productivos con datos simulados;
no prueban el navegador contra la API que el usuario mantiene abierta en Visual Studio.
Ningún test usa claves reales ni llama a servicios facturables.

Capturas de los componentes productivos con datos de prueba: [menú y permisos del rol](../../../ArquitecturaBaseFront/design-lab/whatsapp-chat/evidence/business-product-role-permissions-es.png)
y [formulario](../../../ArquitecturaBaseFront/design-lab/whatsapp-chat/evidence/business-product-form-es.png).

No se implementó generación, consumo del historial del chat ni envío autónomo. Quedan pendientes
reinicio/recorrido de la instalación local del usuario, llamadas reales de conexión, despliegue
Azure, restauración operativa, segunda instalación y sincronización del Artifact externo.
La [retirada](../guides/quitar-ia.md) ahora contempla cuatro tablas y ambas pantallas; esta
ampliación todavía no se ejecutó en una copia aislada. La retirada de la conexión original
tiene evidencia independiente del 2026-10-02 y no acredita esta comprobación nueva.

Se volvió a consultar documentación oficial el 2026-10-03: [Anthropic Messages](https://platform.claude.com/docs/en/api/messages/create),
[OpenAI Responses](https://developers.openai.com/api/docs/guides/migrate-to-responses) y
[estado de conversaciones](https://developers.openai.com/api/docs/guides/conversation-state).
La referencia completa de Responses excedió el límite de lectura de la herramienta; se usaron
sus guías oficiales. Esta entrega no cambió el transporte de los proveedores ni acredita
acceso, crédito, precio o latencia de una cuenta real.
