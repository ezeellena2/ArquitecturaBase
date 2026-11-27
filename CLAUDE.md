@AGENTS.md

# ArquitecturaBase: lo propio de Claude Code

Las reglas del proyecto están en `AGENTS.md`, que se importa arriba y vale igual para cualquier agente. Acá va solo lo que depende de la herramienta.

## Forma de trabajo

- No crear worktrees (tampoco temporales ni en modo detached): el trabajo va en el checkout principal, aunque un skill o una instrucción genérica recomiende "branch first" o una worktree aislada. Lo mismo vale para `../ArquitecturaBaseFront`.
- Al auditar documentación o cerrar un plan, seguí la [rutina compartida](docs/guides/auditar-documentacion.md). Las etapas terminadas ya tienen sus decisiones en las fuentes de las [premisas](docs/architecture/premisas-de-la-plantilla.md).
- Las tareas se siguen en el [tablero de Jira del espacio AB](https://ezequielellena0003.atlassian.net/jira/software/projects/AB/boards/2); KAN es del multitenant. Al empezar, terminar o descubrir un trabajo, mové, cerrá o creá su ticket con el conector de Atlassian, además de actualizar `docs/plans`. Un ticket nuevo lleva su épica (AB-1 a AB-9, por área), sus etiquetas y el enlace a su fuente en `docs/`. El conector no crea espacios de Jira.

## Documentación por área

- Las capas, áreas, adaptadores y tests tienen un `AGENTS.md` que explica su responsabilidad y remite a reglas canónicas, ejemplos y pruebas. Cada `CLAUDE.md` local importa `@AGENTS.md`. Antes de cambiar archivos, leé las guías de sus ancestros y del ámbito local; el [mapa](docs/architecture/mapa-de-instrucciones.md) enumera los puntos de entrada y la [receta](docs/guides/instrucciones-por-carpeta.md) explica cómo mantenerlos.
