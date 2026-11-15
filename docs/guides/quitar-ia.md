# Quitar el módulo de inteligencia artificial

Para un derivado que no usa IA. No apagar opciones para simular una retirada: quitar
código/configuración/rutas y generar la migración del modelo nuevo. Conservá backups
y el anillo externo para restaurar datos históricos; esta operación elimina las ocho tablas del módulo.

## Antes de quitarlo

En una instalación con datos, con el módulo todavía instalado, cerrá lo que sin él quedaría a medio hacer.
Si WhatsApp o Formularios no están instalados, salteá su paso.

1. **WhatsApp:** sin el módulo, un chat en modo Asistente sigue así y cada mensaje se deriva a una persona. Pasá a
   Manual cada uno: en el chat, «Tomar atención manual» (pide el permiso «Responder chats» y cancela las sugerencias
   pendientes del asistente). La bandeja no filtra por modo; para encontrarlos, una consulta de sólo lectura, y cada
   uno se abre en `/whatsapp/chat?conversacion=<Id>`:

   ```sql
   SELECT "Id" FROM "WhatsAppContacts" WHERE "Mode" = 'Assistant';
   ```

2. **IA:** en Configuración › Inteligencia artificial, pasá la conexión a Pausada y guardá. Desde ahí no se acepta
   trabajo nuevo, y lo pendiente o en curso falla en su paso siguiente, sin otra llamada al proveedor.
3. **Formularios:** sin el módulo, un documento que se está leyendo para otro formulario («Leyendo los datos para…»)
   queda así para siempre: la consulta contesta «no disponible» y el pedido no vence. Con la conexión pausada, cada
   lectura en curso falla, y el documento vuelve a como estaba, sin el pedido, cuando alguien la consulta: el servidor
   consulta solo las relecturas de «Armar formulario con la IA», en segundos; las demás, el panel del documento, cuando
   alguien con «Revisar documentos» lo abre en «Documentos recibidos». Comprobá que no quede ninguno y abrí los que
   queden:

   ```sql
   SELECT "Id" FROM "FormReceivedDocuments" WHERE "ExtractionRequestId" IS NOT NULL;
   ```

   Las relecturas que todavía no empezaron no traban nada (sus documentos siguen «Sin clasificar», para revisarlos a
   mano), pero sin la IA el servidor las reintenta cada minuto. Cada una se va cuando su documento deja de estar sin
   clasificar, por ejemplo al descartarlo:

   ```sql
   SELECT "Id", cardinality("PendingRereadIds") FROM "FormDocumentProposals" WHERE cardinality("PendingRereadIds") > 0;
   ```

## Backend

1. Leé [módulos opcionales](../architecture/backend.md#módulos-opcionales), la
   [regla de IA](../features/inteligencia-artificial.md) y [migración](migracion.md).
2. Quitá `Modules/ArtificialIntelligence` de los cuatro proyectos de src y de los cuatro
   proyectos de tests, y `TestFeatures/Modules/ArtificialIntelligence` de
   Application.UnitTests y Api.IntegrationTests. La carpeta de ArchitectureTests aporta
   el lock; las otras aportan reglas, unitarios y dobles HTTP. Verificá las rutas absolutas
   dentro de la copia del proyecto antes de borrar carpetas recursivamente. No cambies
   los módulos presentes, salvo las dos pruebas de WhatsApp que componen el motor real y no pasan sin él:
   `WhatsAppAssistantIsolationIntegrationTests` y `WhatsAppAssistantLatencyIntegrationTests`
   (Api.IntegrationTests/Modules/WhatsApp), con sus escenarios `WhatsAppAssistantIsolationScenario` y
   `WhatsAppAssistantLatencyScenario` de `TestFeatures/Modules/WhatsApp`, que configuran IA y llaman a sus rutas.
3. Quitá el bloque de IA de Program y sus tres using. Los registros generales y el contexto
   no nombran IA; los arneses descubren sólo contribuciones presentes (ADR 0013). Sin su
   registro, el núcleo vuelve a las versiones apagadas de los tres puertos que reemplazaba
   (`Application/Channels`): con `DisabledAssistantOperations` la disponibilidad es `notInstalled`, así que
   WhatsApp no deja pasar un chat a Asistente y Formularios no pide lecturas; con `UnavailableBusinessKnowledgeSource`
   no hay hechos del negocio; y con `DisabledAssistantProposalLookup` no hay propuesta que leer:
   la importación desde la copia de una planilla y crear desde una propuesta que todavía no
   creó nada dan `ProposalUnavailable` (404), y ningún catálogo tiene importación pendiente.
4. Quitá configuración externa `ArtificialIntelligence:*` del host. Guardá una copia segura
   del anillo/ID para backups históricos, sin incluirla en Git. Quitá también lo que nombra al módulo fuera de sus
   carpetas:
   - en `.editorconfig`, las dos secciones de `src/ArquitecturaBase.Infrastructure/Modules/ArtificialIntelligence/…`
     (`ArtificialIntelligenceInfrastructureRegistration.cs` y `AiAssistantProvidersRegistration.cs`), que apagan
     `EXTEXP0001` para archivos que ya no existen;
   - `harnesses/assistant-qualification`, el arnés manual de calificación: está fuera de la solución y el build no lo
     ve, pero usa el módulo y deja de compilar. Borralo con su mención en el
     [mapa de instrucciones](../architecture/mapa-de-instrucciones.md);
   - la cohorte `ai-assistant` de `TestFeatures/Modules/BusinessForms/export-responses-http.ps1` y su comprobación en
     `test-export-responses-http.ps1`, que nombran fuentes del módulo.
5. Retirá las claves `Ai.*` de `Errors.resx` y `Errors.en.resx`: sin el módulo, ninguna clase
   `*Errors` las declara, y `ErrorCodeTests.Every_error_key_belongs_to_a_code_declared_in_domain`
   falla y nombra cada una que quede. Retirá también `AiProviderInvalid`, `AiModelInvalid`, `AiRevisionInvalid`,
   `AiCredentialInvalid` y `AiPlaceSearchInvalid` de `ValidationMessages` y de `Validation.resx`/`.en.resx`:
   ningún test los reclama, pero quedarían sin uso. Los demás textos del módulo viven en su carpeta
   (`Modules/ArtificialIntelligence/Resources`) y se van con ella.
6. **Conservá todas las migraciones anteriores**. Generá `RemoveArtificialIntelligence`:

```powershell
dotnet ef migrations add RemoveArtificialIntelligence --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api --output-dir Persistence/Migrations -- --environment Development
dotnet build ArquitecturaBase.slnx
dotnet test
```

Para diseño fuera de Aspire, declarás conexiones ficticias de `appdb`/`cache` como en la
guía de migraciones; generar no conecta a esas direcciones. Revisá que Up borre sólo
AiConnections/AiConnectionChanges/AiBusinessInformation/AiBusinessInformationChanges,
AiConversations/AiAssistantTurns/AiAssistantToolExecutions/AiAssistantRequestReceipts e índices,
y Down los reconstruya. Las únicas claves foráneas del módulo son internas (herramientas → turno y
turno → conversación, en cascada): ninguna tabla del núcleo ni de otro módulo apunta a las suyas.
Lo que sumaron migraciones posteriores se va con su tabla: las columnas de la planilla de
`AiAssistantTurns`, con su índice filtrado, e `IsDocument` de los recibos, con su CHECK y su índice;
`AddAssistantTurnAttachment`, `AddAssistantTurnAttachmentHashAndSize` y `AddAssistantDocumentReading` se
quedan, como las demás migraciones. Aplicá con el bundle
antes del nuevo código, después de detener o retirar tráfico de rutas IA de la versión vieja.
El drop es destructivo: respaldar precede a publicar. Las copias privadas de Inicio (las planillas y los Word en
`ai/assistant/attachments/…`, los audios en `ai/assistant/audio/…` y las fotos y los PDF en
`ai/assistant/documents/…`) se quedan sin sus tres fuentes de retención, y la limpieza de archivos privados del
núcleo, que corre cada hora, borra las que tienen más de un día. Claims IA antiguos quedan inefectivos
porque el catálogo ya no contiene esos permisos; no hay que hardcodearlos en el núcleo.

## Front

Quitá `src/features/artificial-intelligence`, sus namespaces es/en, la rama guard
`ai.read` de `/configuracion/ia` y `ai.business.read` de `/configuracion/negocio`,
los enlaces `navigation.ai`/`navigation.businessInformation` y sus textos de common.
Quitá `artificial-intelligence.spec.ts`, `business-information.spec.ts` y `business-guided.spec.ts`, sus capturas
`ai-*.png`/`business-information-*.png` y las contribuciones AI/business del
fixture visual (import, escenario/permisos, metadatos/comprobantes y bloque HTTP).
El buscador `BusinessPlaceSearch` y su transporte se retiran con la feature; `Autocomplete`
es UI genérica compartida y puede conservarse. La configuración externa de Google Places
se retira con `ArtificialIntelligence:*`; no deja clientes o rutas en el núcleo.
No borres WhatsApp ni settings; una feature no importa otra. Conservá la evidencia histórica
de diseño y el resto del laboratorio, cuyos otros alcances siguen pendientes.

Quitá también los namespaces `assistant` es/en, tests/fixtures/capturas propios
y la composición del asistente en `app/AuthenticatedHome`. Home conserva su portada/base autenticada;
retirá la consulta/condición de la capacidad `assistant`, sin introducir esa dependencia
en home ni en settings. Los ejemplos exportados `test/fixtures/ai-assistant-http` y su cohorte `aiAssistant` de
`test/httpCohort.ts` se van con la cohorte del backend. Si no queda otro consumidor, desinstalá `@assistant-ui/react` del
package.json/lock. Conservá el timer compartido mejorado y sus pruebas de consumidores.
Los puertos neutrales de capacidades/consultas y fuentes Users/Roles/Help del núcleo pueden
permanecer: no nombran IA. Sin módulo, presentación publica `installedFeatures: []`.

```powershell
npm run build
npm run lint
npm run test
npm run test:visual
```

Revisá que navegación/login/roles/settings/chat funcionen sin permisos ni rutas IA. Una
URL retirada cae en la pantalla 404 habitual. El prefijo `/api` y el fallback del SPA no cambian.

## Comprobaciones registradas

Se usa una copia temporal del código actual, sin tocar las carpetas de trabajo ni hacer
checkout/reset. Se quita el módulo y se genera una migración nueva en esa copia. Los resultados
exactos de la conexión original, incluidos Docker y front, están en la
[revisión del 2026-10-02](../reviews/2026-10-02-configuracion-llm.md).
El 2026-10-04 se ejecutó la retirada ampliada de conexión, negocio/Places e Inicio en copias
aisladas de ambos repositorios. Backend: build sin advertencias, modelo sin cambios pendientes,
migración física sobre PostgreSQL de Testcontainers que comprueba 8→0→8→0 tablas y conserva
un rol del núcleo. La suite de esa copia entregó 2077/2078 correctas por una aserción histórica
de presentación; después de corregirla, Settings pasó 33/33 e incluyó 404 de rutas retiradas y
`installedFeatures: []`. No se atribuye a esa copia una segunda corrida completa verde.
Front: build, lint con 48 reglas de arquitectura y 104 pruebas base correctos, sin el módulo
ni su composición. La [revisión de Inicio](../reviews/2026-10-04-inicio-asistente.md) registra
la evidencia del módulo presente y de estas copias.
El 2026-11-15 la guía se comparó con el código (tablas, claves foráneas, registros, rutas, fuentes de retención y
lo que nombra al módulo afuera de sus carpetas), sin repetir la retirada: el paso previo, las lecturas de documentos,
las pruebas de WhatsApp, el arnés y la cohorte son posteriores a esas copias.
La receta no acredita haber eliminado IA de una instalación real ni restaurado un backup del negocio.
