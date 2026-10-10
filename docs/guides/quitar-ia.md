# Quitar el módulo de inteligencia artificial

Para un derivado que no usa IA. No apagar opciones para simular una retirada: quitar
código/configuración/rutas y generar la migración del modelo nuevo. Conservá backups
y el anillo externo para restaurar datos históricos; esta operación elimina las ocho tablas del módulo.

## Backend

1. Leé [módulos opcionales](../architecture/backend.md#módulos-opcionales), la
   [regla de IA](../features/inteligencia-artificial.md) y [migración](migracion.md).
2. Quitá `Modules/ArtificialIntelligence` de los cuatro proyectos de src y de los cuatro
   proyectos de tests, y `TestFeatures/Modules/ArtificialIntelligence` de
   Application.UnitTests y Api.IntegrationTests. La carpeta de ArchitectureTests aporta
   el lock; las otras aportan reglas, unitarios y dobles HTTP. Verificá las rutas absolutas
   dentro de la copia del proyecto antes de borrar carpetas recursivamente. No cambies
   los módulos presentes.
3. Quitá el bloque de IA de Program y sus tres using. Los registros generales y el contexto
   no nombran IA; los arneses descubren sólo contribuciones presentes (ADR 0013).
4. Quitá configuración externa `ArtificialIntelligence:*` del host. Guardá una copia segura
   del anillo/ID para backups históricos, sin incluirla en Git.
5. Opcionalmente retirás textos `Ai.*` de Errors y los `Ai*` de ValidationMessages/
   Validation, siempre en ambos idiomas. Dejarlos no crea referencias ni rutas.
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
y Down los reconstruya. Aplicá con el bundle
antes del nuevo código, después de detener o retirar tráfico de rutas IA de la versión vieja.
El drop es destructivo: respaldar precede a publicar. Claims IA antiguos quedan inefectivos
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
en home ni en settings. Si no queda otro consumidor, desinstalá `@assistant-ui/react` del
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
La receta no acredita haber eliminado IA de una instalación real ni restaurado un backup del negocio.
