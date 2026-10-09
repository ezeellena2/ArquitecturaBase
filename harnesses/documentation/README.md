# Auditor documental ejecutable

El usuario inicia [run.ps1](run.ps1); [runner.mjs](runner.mjs) llama a Codex o Claude y controla
el resultado. La [guía canónica](../../docs/guides/auditar-documentacion.md) define qué conservar,
consolidar y retirar; [workflow.md](workflow.md) traduce esa tarea al contrato del arnés.

## Comandos desde la raíz del backend

```powershell
# Aprender el recorrido sin gastar una ejecución de agente.
.\harnesses\documentation\run.ps1 -DryRun

# Revisar solamente enlaces, anclas, pares y mapas, sin agente.
.\harnesses\documentation\run.ps1 -Engine none -Checks structural

# Análisis semántico con propuesta; no aplica cambios.
.\harnesses\documentation\run.ps1 -Engine codex -Mode audit

# Actualización con Claude, controles y checks completos.
.\harnesses\documentation\run.ps1 -Engine claude -Mode update
```

En cualquier modo con agente podés elegir `codex` o `claude`. La CLI correspondiente debe
estar instalada y autenticada; una falta de acceso queda como ejecución incompleta y logs.
Se respetan los defaults de modelo de la CLI en esta ejecución aislada, sin fijar un modelo
en el código. No se cargan personalizaciones/hooks de Claude (`--safe-mode`) ni configuración
personal de Codex (`--ignore-user-config`); las instrucciones del repo se leen explícitamente.
Los servidores MCP declarados como tablas simples en la configuración de proyecto de Codex
se deshabilitan para esta invocación, sin editar esa configuración.

## Opciones

| Opción | Valores | Efecto |
| --- | --- | --- |
| `-Engine` | `codex` (default), `claude`, `none` | Quién analiza; `none` hace controles estructurales y no sirve para update. |
| `-Mode` | `audit` (default), `update` | Guardar propuestas o aplicar las que superan los controles. |
| `-Checks` | `full` (default), `structural` | Build/tests de ambos repositorios o solo controles documentales y diff. |
| `-DryRun` | switch | Preparar tarea e inventario sin agente ni build/tests; sí revisar estructura y diff. |

`structural` permite una pasada rápida, pero no acredita build ni integración. `full` usa
los comandos de [config.json](config.json): build/test backend con Docker y build/lint/test
front. Los logs quedan en el directorio de ejecución. El runner no inicia Aspire.

## Etapas reales

1. **Inventariar:** lee archivos versionados y nuevos no ignorados, con hashes, branch y
   cambios locales. Requiere ambos repositorios en `main`. Revisa enlaces e instrucciones.
2. **Analizar:** entrega el mismo workflow e inventario al motor. El agente lee código/docs
   y devuelve el [schema común](report.schema.json). Se registran documentos efectivamente
   revisados declarados por el agente; recibir el inventario no acredita revisarlo entero.
3. **Aplicar:** valida todas las propuestas y el árbol documental resultante también en audit.
   En update escribe y guarda originales y registro de cambios aplicados para recuperación.
4. **Verificar:** controles estructurales y `git diff --check`; con full, checks del producto.
   Guarda informe, propuestas, logs y estado. Un check fallido devuelve exit code 1.

El timeout limita cada proceso, con 20 minutos iniciales configurables. Un lock evita dos
ejecuciones del auditor a la vez; no coordina otros chats o editores. Si un proceso muere,
verificá que terminó antes de retirar su lock. No publiques ni ejecutes esta carpeta como
contenido de la API o del SPA: es una herramienta de desarrollo.

## Límites comprobados por código

- Solo aplicar Markdown de `docs`, README/instrucciones raíz y pares AGENTS/CLAUDE locales
  de `src`, `tests`, `design-lab` o `.github`. No cambiar producto ni el propio arnés.
- Rechazar rutas absolutas, traversal, aliases reservados y escapes por enlaces/junctions.
- Motivo y evidencia obligatorios; no permitir dos cambios sobre el mismo archivo.
- Retiros solo de planes/specs/diseños con original en Git y sin texto local nuevo que se
  perdería. ADR, evidencia, índices e instrucciones se conservan.
- Verificar referencias, anclas, pares y mapas del árbol propuesto antes de escribir.
- Comparar el inventario antes de aplicar: un cambio concurrente aborta esa aplicación.

No hay transacción global entre dos repositorios. Si falla una escritura intermedia o un
check posterior, los cambios aplicados y sus originales quedan en `recovery/`; el informe
marca ejecución incompleta. No se ejecuta un reset automático que pueda borrar trabajo ajeno.
No hay commit, push, ramas, despliegue ni contactos con proveedores externos.

## Resultados

`.test-artifacts/harnesses/documentation/<ejecución>/` contiene:

- `task.md`, `inventory.json` e `initial-checks.json`: entrada y estado local inicial.
- `proposal.json`: respuesta común del agente; en audit no se aplica.
- `proposal-checks.json`: controles sobre el árbol que produciría esa propuesta.
- `engine.*.log` y `check-*.log`: salida de los procesos.
- `recovery/originals.json` y `recovery/applied.json`: originales locales y cambios aplicados.
- `report.json` y `report.md`: resultado del runner, cobertura declarada, pendientes y checks.

Los estados `*-partial` identifican documentos sin revisión semántica declarada. `prepared`
y `structural-only` no son una auditoría semántica. Una forma JSON válida y links correctos
no prueban la exactitud de una afirmación: su evidencia requiere revisión del agente/persona.

## Probar el arnés

```powershell
node --test harnesses/documentation/tests/*.test.mjs
```

Los tests crean repositorios aislados con documentos simples, código y cambios locales.
Ejercen errores, aplicación, retiros, concurrencia y ambas respuestas de motores. No requieren
modelos ni secretos. Las pruebas con respuestas simuladas no acreditan login o un análisis
real de las CLIs; `audit` permite hacer esa prueba sin aplicar documentación.

Para aprender la arquitectura general: [quién inicia los arneses](../README.md).
