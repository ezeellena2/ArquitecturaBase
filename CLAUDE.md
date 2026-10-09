@AGENTS.md

# ArquitecturaBase: lo propio de Claude Code

Las reglas del proyecto están en `AGENTS.md`, que se importa arriba y vale igual para cualquier agente. Acá va solo lo que depende de la herramienta.

## Forma de trabajo

- No crear worktrees (tampoco temporales ni en modo detached): el trabajo va en el checkout principal, aunque un skill o una instrucción genérica recomiende "branch first" o una worktree aislada. Lo mismo vale para `../ArquitecturaBaseFront`.
- Al auditar documentación o cerrar un plan, seguí la [rutina compartida](docs/guides/auditar-documentacion.md). Las etapas terminadas ya tienen sus decisiones en las fuentes de las [premisas](docs/architecture/premisas-de-la-plantilla.md).

## Documentación por área

- Las capas, áreas, adaptadores y tests tienen un `AGENTS.md` que explica su responsabilidad y remite a reglas canónicas, ejemplos y pruebas. Cada `CLAUDE.md` local importa `@AGENTS.md`. Antes de cambiar archivos, leé las guías de sus ancestros y del ámbito local; el [mapa](docs/architecture/mapa-de-instrucciones.md) enumera los puntos de entrada y la [receta](docs/guides/instrucciones-por-carpeta.md) explica cómo mantenerlos.
