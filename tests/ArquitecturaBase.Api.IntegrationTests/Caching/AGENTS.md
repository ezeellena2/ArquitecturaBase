# Caching tests: garantías de Redis

Prueba lectura, expiración, payloads, invalidación, leases, concurrencia, fallas y seed. Los hosts independientes comparten una instancia Redis real.

## Al modificar

- Una garantía entre réplicas necesita al menos dos hosts con el mismo prefijo y conexión; el aislamiento entre escenarios usa prefijos propios.
- Coordiná fábricas y escrituras con señales para demostrar carreras; no depender solo de una demora.
- Verificá que una invalidación o lease vencido impida publicar el resultado anterior y que un contexto no confirmado no contamine el caché.
- Una falla posterior al commit se observa tanto en HTTP como en la base. Salud y reconexión se verifican con la dependencia real.

## Referencias

- [Casos requeridos al agregar caché](../../../docs/guides/agregar-cache.md).
- [Adaptador Redis](../../../src/ArquitecturaBase.Infrastructure/Caching/AGENTS.md).
- [Arnés de hosts independientes](../Support/CacheTestHost.cs).
