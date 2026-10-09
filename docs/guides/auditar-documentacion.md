# Auditar la documentación

Esta rutina revisa backend y `../ArquitecturaBaseFront`: ajusta documentación al comportamiento
comprobado, conserva decisiones como [premisas](../architecture/premisas-de-la-plantilla.md) y
retira planes terminados que ya no aportan información única. Se ejecuta cuando se pide una
auditoría o al cerrar un desarrollo; no programa ejecuciones futuras.

La [entrada ejecutable](../../harnesses/documentation/README.md) permite iniciar esta rutina
manualmente con Codex o Claude. `run.ps1` llama al runner, el runner inicia el motor y valida
sus propuestas antes de aplicar documentación. `-DryRun` prepara la tarea sin agente;
`-Engine none` revisa estructura. Estos dos modos no acreditan una auditoría semántica.

## Criterio del auditor

El nombre, la fecha y las casillas de un plan no prueban su implementación. Para cada afirmación,
distinguí la decisión aprobada, el código existente, las pruebas automáticas y la verificación
manual o externa. Un build verde no demuestra que una pantalla respete una nueva política.

| Estado encontrado | Acción |
| --- | --- |
| Propuesta sin implementar | Conservar como propuesta en `plans` o `specs`; no promoverla a premisa. |
| Implementación parcial | Registrar lo hecho con evidencia y conservar el alcance pendiente. |
| Implementado, con verificación pendiente | Consolidar las reglas comprobadas; trasladar la comprobación pendiente con su criterio de cierre. |
| Implementado y verificado | Guardar reglas reutilizables en su fuente canónica y retirar el plan cuando ya no tenga información única. |
| Obsoleto o duplicado | Corregir consumidores y retirar, conservando procedencia recuperable. |
| Contradicción entre documentación y código | Identificar decisión y evidencia. Corregir el texto desactualizado; si el código viola una regla vigente, registrar el desvío sin cambiar esa regla para hacerlo pasar. |

## Recorrido

1. **Preparar.** Leer `AGENTS.md` de ambos repositorios, los ámbitos de documentación y sus
   fuentes canónicas. Registrar `git status --short` y el commit de cada repo; conservar cambios
   de otras sesiones. Trabajar en `main`, sin crear ramas ni hacer push.
2. **Inventariar.** Revisar README, instrucciones, arquitectura, features, guías, ADR, diseños,
   planes y materiales operativos. Usar `git ls-files` para incluir documentación versionada
   aunque `rg` la ignore; excluir outputs y dependencias. Incluir los archivos Markdown nuevos
   del desarrollo y buscar referencias entre repositorios.
3. **Contrastar.** Para cada plan o contradicción, buscar el servicio/controller, adaptador,
   contrato y test que observan el comportamiento. Leer el cuerpo del test; su nombre solo
   orienta. Contrastar comandos con scripts/configuración locales. No declarar actualizados
   una biblioteca, un entorno desplegado o una política externa que no se inspeccionaron.
4. **Consolidar.** Extraer decisiones reutilizables y su motivo. Poner el detalle en su fuente
   completa, según la [tabla de destinos](#dónde-guardar-las-premisas); actualizar el índice de
   premisas y las instrucciones que lo resumen. Corregir enlaces de ADR sin cambiar una
   decisión aceptada; si cambia la decisión, escribir el ADR correspondiente.
5. **Rescatar pendientes.** Conservar propuestas, adopción parcial, verificaciones manuales,
   dependencias externas y decisiones todavía abiertas. Cada pendiente tiene evidencia actual
   o se marca como heredado por confirmar, y un criterio para cerrarlo. No convertir una
   mejora opcional en requisito ni un pendiente en premisa. Evitar duplicarlo entre planes.
6. **Retirar.** Eliminar el plan completado o duplicado después de consolidar su información
   única y corregir todas sus referencias. Registrar en [history](../history/README.md) el
   commit anterior a la limpieza y dónde quedó cada grupo de decisiones. Git conserva el
   relato, el código de ejemplo y la secuencia de tareas; no copiar todo a otro archivo.
   Un archivo sin versionar y sin otra copia no se elimina por asumir que está terminado.
7. **Verificar.** Comprobar destinos y anclas de enlaces locales, referencias en texto a archivos
   retirados, archivos canónicos no vacíos, pares `AGENTS.md`/`CLAUDE.md`, mapas y ausencia de
   contradicciones nuevas. Los ejemplos dentro de bloques de código se revisan como ejemplos,
   sin exigir que una ruta de plantilla exista. `git diff --check` queda limpio en ambos repos.
8. **Cerrar.** Ejecutar los checks exigidos abajo y revisar el diff. Informar qué se consolidó,
   qué se retiró, qué quedó pendiente y qué validación realmente se hizo. Si un check falla por
   el entorno, registrar la limitación; no dar por cerrada la integración sin Docker.

## Dónde guardar las premisas

| Información | Destino |
| --- | --- |
| Capas, contratos técnicos, transacciones, caché, hosting | [Arquitectura del backend](../architecture/backend.md). |
| Regla funcional de un área | [Features](../features/). |
| Decisión estructural y alternativas | [ADR](../decisions/README.md). |
| Procedimiento repetible | `docs/guides/`. |
| Estructura del SPA y convenciones | [Arquitectura del front](../../../ArquitecturaBaseFront/docs/architecture.md). |
| Interacción compartida | [Premisas de pantallas](../../../ArquitecturaBaseFront/docs/guides/premisas-de-desarrollo.md) o [contrato de mensajes](../../../ArquitecturaBaseFront/docs/guides/mensajes-y-estados.md). |
| Elección visual y evidencia | [Fundamento visual](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md), tokens y componentes. |
| Resumen para agentes | `AGENTS.md` del ámbito y su mapa; `CLAUDE.md` importa la misma fuente. |
| Trabajo todavía abierto | [Planes pendientes](../plans/README.md) o el plan del front dueño del desarrollo. |

## Checks de cierre

Desde el backend:

```powershell
dotnet build ArquitecturaBase.slnx
dotnet test
git diff --check
```

Desde `../ArquitecturaBaseFront`:

```powershell
npm run build
npm run lint
npm run test
git diff --check
```

Si se levantó la app, terminar con `aspire stop`. Una auditoría documental no necesita pruebas
nuevas que repitan el texto ni levantar la app para dar por cumplidos antiguos smokes manuales.

## Pedido reutilizable

> Auditá la documentación del backend y del front siguiendo esta guía. Contrastá planes y
> afirmaciones con código y pruebas. Consolidá lo implementado como premisas en sus fuentes
> canónicas, conservá propuestas y pendientes comprobables, retirá planes redundantes después
> de rescatar su información única y corregí referencias e índices. Validá ambos repositorios y
> reportá alcance, evidencia y limitaciones. No hagas push ni cambies comportamiento del producto
> para ajustar la documentación.
