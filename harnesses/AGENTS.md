# Harnesses: tareas repetibles con agentes

Herramientas de desarrollo fuera de `src` y de la solución .NET. Cada arnés tiene entrada,
procedimiento compartido, configuración, controles y resultados. El usuario lo inicia;
un archivo Markdown por sí solo no ejecuta una tarea.

## Al modificar

- El procedimiento y el contrato de salida son comunes a Claude Code y Codex CLI. Los
  adaptadores contienen únicamente invocación y normalización propias de cada motor.
- El auditor propone cambios en modo de lectura; el runner valida y aplica solamente
  documentación permitida. No permite que una propuesta cambie código ni el propio arnés.
- Conservar cambios existentes y detener la aplicación si cambió el inventario durante
  el análisis. No usar `git reset`, crear ramas, hacer commits ni push.
- TDD de reglas, rutas y adaptación de resultados: `node --test harnesses/documentation/tests/*.test.mjs`.
- Los tests usan repositorios temporales y respuestas simuladas; no llaman a modelos ni
  necesitan credenciales. Una prueba simulada no acredita autenticación del motor real.
- Registrar este ámbito en el mapa. `CLAUDE.md` importa esta misma fuente.

## Referencias

- [Uso y arquitectura](README.md).
- [Auditor documental](documentation/README.md).
- [Rutina canónica](../docs/guides/auditar-documentacion.md).
