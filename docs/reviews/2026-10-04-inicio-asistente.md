# Inicio autenticado con assistant-ui — revisión de implementación

Fecha: 2026-10-04. Estado: implementación autorizada y verificación local completas; piloto pendiente.
Trabajo directo en `main`, sin commits, ramas ni push. Los cambios previos de ambos árboles se
conservaron. El plan original aún sin versionar conserva procedencia y los pendientes del piloto.

## Alcance implementado

`app/AuthenticatedHome` compone el asistente desde la feature `artificial-intelligence`.
`@assistant-ui/react` usa ExternalStoreRuntime, primitivas compartidas y estado confirmado por
el servidor. Preguntas explícitas, respuesta completa con fuentes, historial privado, repreguntas,
cancelación y recuperación por UUID. Enter agrega línea; Ctrl/Cmd+Enter envía fuera de IME.
Campo/Public Sans, es/en, formateadores del perfil y notificaciones comunes. El timer compartido
conserva su API y calcula por deadline monotónico, con recuperación de pestaña oculta.

MVC → servicios Application → puertos → Infrastructure. Las cinco herramientas son users.summary,
roles.list, roles.permissions, business.information y system.help, con EF detrás de lectores y un scope
nuevo por herramienta. El coordinador abre scopes separados para cada unidad; no mantiene UoW
durante HTTP. PostgreSQL conserva aceptación/claim/fence/cancelación/resultado, cuotas y recibos.
Los adaptadores OpenAI Responses/Anthropic Messages usan la conexión guardada, sin reintentos
facturables. No hay streaming, escrituras de negocio ni MCP externo.

Historial/pasos/publicación comprueban propietario y permisos actuales. Fuentes verificables e
IDs terminales observados, sin SQL o URL arbitraria. Texto/evidencia cifrados con propósitos
independientes del anillo externo. Operaciones perdidas no se regeneran al leer o reiniciar.
Retención, reconciliación, fencing, límites y métricas quedan en el módulo opcional independiente.

La modificación de esta entrega se delimita a las nuevas entidades/contratos/servicios `AiAssistant*`,
conversaciones y protección del contenido, clientes de conversación y workers, `AddAiAssistant` y
sus pruebas. En el núcleo agrega capacidades/fuentes/ejecutor neutrales, dos proyecciones de lectores,
la composición pública de capacidades y recursos. En el front agrega Inicio desde `app`, el asistente
en la feature IA y primitivas compartidas; mejora el timer y los guards/diálogos compartidos conservando
sus consumidores. Documentación, instrucciones y evidencia acompañan esas piezas. Los cambios previos
de conexión, negocio/Places, WhatsApp, apariencia, despliegue y auditoría no se atribuyen a este desarrollo.

## Ajustes respecto del diseño inicial

- El ejecutor que crea scopes es un proveedor técnico en `Interfaces/Integrations/SystemQueries`,
  y las fuentes aportantes son canales neutrales. El núcleo no nombra IA ni WhatsApp.
- Se conserva la unión de permisos de fuentes ofrecidas antes del HTTP. Es más conservadora
  que registrar sólo fuentes usadas y protege historia si el proceso cae antes de persistirlas.
- IDs funcionales con puntos se traducen a nombres de protocolo compatibles; colisiones se
  rechazan. Las continuaciones completas permanecen privadas, acotadas y sólo en memoria.
- Helpers con responsabilidades de lectura/aceptación/contexto/reserva respetan ocho dependencias.
  No se relajaron las reglas de arquitectura. `IAiConversationProtector` concreta el puerto de
  protección de contenido del plan.
- CancelRequestedAtUtc se publica como metadata confirmada para recuperar un pedido de cancelación
  tras recarga. Uso conocido se conserva al fallar o bloquear publicación; desconocido permanece así.

## Evidencia automatizada

| Verificación | Resultado registrado |
| --- | --- |
| Build completo backend | `dotnet build ArquitecturaBase.slnx`, estado definitivo: 0 advertencias/0 errores, 6,70 s |
| Suite completa backend con Docker | `dotnet test`, estado definitivo: 2334/2334 correctas, ninguna omitida, 10 min 57,933 s; Domain, Application, Architecture e Integration |
| Integración durable del asistente | Incluida en la suite completa: pertenencia, cuotas/capacidad/claim entre hosts, cancelación/fence, purga de contenido y física, recuperación sólo de commit y migración Up/Down |
| Herramientas sin evidencia | Regresión unitaria en Application: registra resultado válido sin inventar un ID de evidencia, con deduplicación de CallId; incluida en la suite completa |
| Contratos HTTP de proveedores | 42/42 con handlers falsos: continuidad, límites, headers/orígenes, tools/terminal, cancelación, incertidumbre y ausencia de retry con ServiceDefaults; incomplete/refusal conserva usage informado |
| Ejecución de las cinco consultas con ambos proveedores | 21/21 (E2E 10 + endpoints 11): datos reales de Testcontainers, HTTP simulado, pregunta/repregunta y transacciones del coordinador ausentes durante HTTP |
| Timer y consumidores | 173 pruebas de los siete archivos seleccionados correctas; pruebas nuevas tuvieron rojo antes del cambio |
| Front build/lint | Estado definitivo: build correcto sin advertencias; lint y 48 reglas de arquitectura correctos |
| Front completo y visual | Unitarios definitivos: 1003/1003 en 95 archivos con `--maxWorkers=1`, 297,92 s; `test:visual`: 185/185 con un worker, 3,0 min, salida 0 |
| Retirada backend en copia aislada | Build sin advertencias y modelo sin cambios pendientes; 2078 pruebas con una única aserción de presentación antigua, luego Settings 33/33 con aserción corregida y smoke 404/installedFeatures[]; migración física 1/1: 8→0→8→0 tablas y rol del núcleo preservado |
| Retirada front en copia aislada | Build, lint (48 arquitectura) y 104 pruebas base correctos; Inicio base, layouts, ajustes y timer sin IA |

Las pruebas nuevas usaron TDD en lógica de estado, deduplicación/validación, autorizaciones,
evidencia, contadores, fuentes, reloj y privacidad de respuestas tardías. Pruebas de migración
preservan conexión y negocio existentes. Las suites no usan claves reales ni llamadas facturables.
La corrida completa definitiva incluye las correcciones de DI/capacidad y contrato de presentación
detectadas en la primera corrida, y las regresiones de retención, herramientas y uso de proveedores.
Logs backend: `artifacts/assistant-validation/build-final-confirmed.log` y
`artifacts/assistant-validation/test-final-confirmed.log` (artefactos locales ignorados).
El [registro del front](../../../ArquitecturaBaseFront/docs/verification/inicio-asistente-2026-10-04.md)
detalla arnés y repeticiones: la visual completa final corrió sola para evitar interferencia de
transformación/HMR; las fallas intermitentes anteriores no se presentan como corridas aprobadas.

## Revisión manual real y simulaciones

Se levantó un AppHost temporal con código real de API/front y PostgreSQL/Redis de sesión separados
de la instalación del usuario. API y recursos Healthy; health 200, SPA 200, presentación mediante
proxy 200 JSON y bootstrap sin sesión 401 ProblemDetails. La portada pública se renderizó en el
navegador real. Ese recorrido detectó la capacidad `assistant` ausente en presentación y originó
su corrección con regresión. El smoke posterior sobre el código final volvió a comprobar health/SPA
200, `installedFeatures: ["assistant"]` mediante el proxy y bootstrap 401. Una repetición detached
mostró recursos Healthy con endpoints ausentes; se detuvo y se repitió en primer plano, donde
las cuatro comprobaciones HTTP pasaron. Se ejecutó `aspire stop` para ese AppHost y `aspire ps` confirmó que
no quedaba ninguno activo. No se abrió túnel ni se llamó a un proveedor.

Se inspeccionaron capturas productivas answer-es en 1440/320 y blocked-en en 320: fuentes legibles,
texto seguro, estado bloqueado sin contenido y sin desborde horizontal. Sus datos/auth/proveedores
son fixtures visuales; no acreditan un recorrido autenticado con cuentas/modelos externos reales.

## Pendientes de piloto y operación desplegada

1. Aprobar/calibrar cuotas, concurrencia y retención iniciales propuestos. No se los convierte
   en premisas operativas por tener pruebas verdes.
2. Calificar un modelo exacto de cada proveedor con credenciales del producto y presupuesto
   controlado: cinco consultas, repreguntas, salida terminal, latencia/contexto y calidad de texto.
   No hay perfiles habilitados por defecto; catálogo y prueba de conexión no los acreditan.
3. Validar egress/retención externa, restauración de backups/anillo y alertas/exportación de
   telemetría en el despliegue. Tests criptográficos con claves inventadas no acreditan restore operativo.
4. Sincronizar tablero/evidencia con el Artifact externo cuando esté accesible.

Fuentes consolidadas: [regla funcional](../features/inteligencia-artificial.md#inicio-autenticado-con-consultas--2026-10-04),
[configuración/runbook](../guides/configurar-ia.md#habilitar-el-asistente),
[retirada](../guides/quitar-ia.md) y [ADR 0017](../decisions/0017-inicio-asistente-y-operaciones-durables.md).
