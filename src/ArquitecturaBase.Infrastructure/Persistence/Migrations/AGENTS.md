# Migrations: evolución versionada del esquema

Contiene migraciones EF, sus metadatos y el snapshot. Representa la secuencia que debe poder aplicarse tanto en una base nueva como en un despliegue.

## Al modificar

- Seguí la guía de migración para generar, revisar y comprobar cambios pendientes. Revisá pérdidas de datos, índices, filtros y `Up`/`Down`.
- No cambiar una migración ya aplicada para resolver un cambio nuevo; creá la siguiente. Un ajuste manual del SQL generado debe quedar coherente con el modelo y el snapshot.
- EF construye el host de diseño: necesita configuración de `appdb` y `cache`; crear el bundle no requiere conectarse a Redis.
- Production aplica el bundle antes de la imagen y la Api valida el estado; no habilitar migraciones automáticas en producción.

La migración de sugerencias y audio conserva Conversation y bytea vacío como defaults para el intervalo bundle→imagen. Su Down toma locks de las tablas del canal y rechaza hechos Assistant o QuickReplies antes de cualquier borrado; no inventa operadores ni elimina respuestas confirmadas. Sólo el audio representable vuelve a Media sin conservar su transcripción como cuerpo original. Las pruebas de migración verifican datos e historia antes/después, no sólo el esquema vacío.

## Referencias

- [Comandos y revisión obligatoria](../../../../docs/guides/migracion.md).
- [Bundle antes de imagen](../../../../docs/guides/despliegue.md).
- [Pruebas de migraciones](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Persistence/MigrationsTests.cs).
