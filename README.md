# Arquitectura Base

Plantilla base para aplicaciones web con .NET 10, Aspire, React y PostgreSQL, organizada en Clean Architecture.

- Diseño: [docs/specs/2026-09-18-arquitectura-base-design.md](docs/specs/2026-09-18-arquitectura-base-design.md).
- Este repo es el backend. El front está en `../ArquitecturaBaseFront`.

## Requisitos

- .NET SDK 10.0.400 o superior (lo fija `global.json`).
- Docker Desktop encendido. Lo usan el Postgres del AppHost y los tests de integración.
- Aspire CLI 13.5.4:

  ```bash
  dotnet tool install -g Aspire.Cli --version 13.5.4
  ```

- Certificado HTTPS de desarrollo confiable:

  ```bash
  dotnet dev-certs https --trust
  ```

## Contraseña de Postgres

La contraseña del contenedor es fija y sale del parámetro `Parameters:postgres-password` del AppHost.

- **Por ahora (pruebas en local):** está en `src/ArquitecturaBase.AppHost/appsettings.Development.json` con el valor `postgres`.
- **Más adelante:** se saca del repo y se carga en los user-secrets del AppHost:

  ```bash
  dotnet user-secrets set "Parameters:postgres-password" "tu-contraseña" --project src/ArquitecturaBase.AppHost
  ```

Postgres toma la contraseña solo cuando crea el volumen. Para cambiarla:

1. Detené el AppHost.
2. Borrá el contenedor de Postgres y después el volumen: `docker volume rm arquitecturabase-pgdata`.
3. Cargá la nueva contraseña y volvé a levantar.

## Levantar el proyecto

Desde la raíz del repo:

```bash
aspire run
```

La consola muestra la URL del dashboard de Aspire. Se levantan:

- **postgres:** contenedor persistente en `localhost:5433` con la base `appdb`. Sigue vivo al cerrar el AppHost.
- **api:** espera a que la base esté lista y, en desarrollo, aplica las migraciones al iniciar. También en desarrollo:
  - OpenAPI en `/openapi/v1.json`;
  - Swagger UI en `/swagger` (en el dashboard de Aspire, el link "Swagger UI" de la fila `api`);
  - health checks en `/health` y `/alive`.

También funciona con `dotnet run --project src/ArquitecturaBase.AppHost` o con F5 sobre el AppHost en Visual Studio.

## Conectarse con DBeaver

| Campo | Valor |
|---|---|
| Host | `localhost` |
| Puerto | `5433` |
| Base de datos | `appdb` |
| Usuario | `postgres` |
| Contraseña | la de `Parameters:postgres-password` (`postgres` mientras esté en `appsettings.Development.json`) |

El puerto 5432 queda libre para el PostgreSQL local de la máquina.

Si DBeaver responde `FATAL: invalid value for parameter "TimeZone": "America/Buenos_Aires"`, es porque Java manda el nombre viejo de la zona horaria y la imagen de Postgres 18 ya no lo trae. Cerrá DBeaver, agregá esta línea al final de `dbeaver.ini` (debajo de `-vmargs`; en Windows está en `C:\Program Files\DBeaver\dbeaver.ini` y se edita como administrador) y volvé a abrirlo:

```
-Duser.timezone=UTC
```

Con UTC ves las fechas tal como están guardadas. Para verlas en hora local, usá `-Duser.timezone=America/Argentina/Buenos_Aires`.

## Tests

```bash
dotnet test
```

Los tests de integración levantan su propio Postgres con Testcontainers, así que necesitan Docker encendido.

## Estructura

```
src/
  ArquitecturaBase.Domain            reglas de negocio (Result, Error, Entity, ...)
  ArquitecturaBase.Application       casos de uso, validación, paginado, textos (resx)
  ArquitecturaBase.Infrastructure    EF Core + PostgreSQL, interceptores, paginado
  ArquitecturaBase.Api               endpoints, ProblemDetails, localización, OpenAPI
  ArquitecturaBase.AppHost           orquestación con Aspire
  ArquitecturaBase.ServiceDefaults   OpenTelemetry, health checks, resiliencia
tests/
  ArquitecturaBase.Domain.UnitTests
  ArquitecturaBase.Application.UnitTests
  ArquitecturaBase.Api.IntegrationTests
  ArquitecturaBase.ArchitectureTests
```

Las convenciones de código están en [CLAUDE.md](CLAUDE.md).
