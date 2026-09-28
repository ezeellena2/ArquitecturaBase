@AGENTS.md

# ArquitecturaBase: lo propio de Claude Code

Las reglas del proyecto están en `AGENTS.md`, que se importa arriba y vale igual para cualquier agente. Acá va solo lo que depende de la herramienta.

## Forma de trabajo

- No crear worktrees (tampoco temporales ni en modo detached): el trabajo va en el checkout principal, aunque un skill o una instrucción genérica recomiende "branch first" o una worktree aislada. Lo mismo vale para `../ArquitecturaBaseFront`.
- Los planes de cada etapa del [plan maestro](docs/plans/2026-09-26-plantilla-estandar-por-etapas.md) se escriben con el skill `superpowers:writing-plans` al arrancar la etapa.

## Documentación por área

- Las carpetas de código de WhatsApp, identidad y administración tienen un `AGENTS.md` corto que remite a su documento de `docs/features/`, y un `CLAUDE.md` que lo importa (`@AGENTS.md`). Claude Code lo carga al leer un archivo de esa carpeta: ese documento se lee antes de cambiar nada ahí.
