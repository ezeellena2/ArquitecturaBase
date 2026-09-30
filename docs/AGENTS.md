# Docs: fuentes de verdad y recetas

`architecture` fija estructura; `features`, reglas funcionales; `guides`, procedimientos; `decisions`, ADR; `plans`, trabajo en curso; `history`, trabajo cerrado; `specs`, diseños; `deploy` y `postman`, materiales de operación.

## Al modificar

- La arquitectura canónica prevalece sobre planes y diseños históricos. Una regla de área cambia en `features`; una decisión estructural, con ADR.
- Las instrucciones por carpeta resumen responsabilidad y trampas, y enlazan estas fuentes; no copiar la guía completa a cada carpeta.
- Una actualización se hace con el cambio funcional que la vuelve necesaria. Revisá enlaces, ejemplos, comandos y los consumidores del front.
- Un plan cerrado va a `history` marcado HISTÓRICO; los pendientes y relatos de implementación no son reglas permanentes.
- Al crear un área o cambiar su ubicación, actualizá el mapa de instrucciones y los pares `AGENTS.md`/`CLAUDE.md`.

## Referencias

- [Arquitectura canónica](architecture/backend.md).
- [Mapa de carpetas e instrucciones](architecture/mapa-de-instrucciones.md).
- [Cómo escribir y mantener instrucciones](guides/instrucciones-por-carpeta.md).
- [Índice de ADR](decisions/README.md).
