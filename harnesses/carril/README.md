# Carril: lo pesado, de a uno

Con varias sesiones a la vez, cada una compilaba y corría suites por su cuenta y la PC quedaba al 100 %:
los tests vencían por tiempo, se repetían y no avanzaba nadie. El carril es un turno para toda la PC:
una corrida pesada por vez. Las demás esperan y entran solas cuando se libera (decisión del 2026-11-22).

## Qué pasa por el carril

- Compilar la solución o un proyecto con sus dependencias: `dotnet build`, también en una copia con
  `--artifacts-path`, y `aspire resource api rebuild`.
- `dotnet test` de un proyecto entero o de la solución. La integración (Testcontainers) va siempre por el carril.
- En el front: `npm run build`, `npm run test` o `vitest run` sin archivo, `npm run test:visual` y `playwright test`.
- Las corridas de arneses (`assistant-qualification`, el auditor con `-Checks full`) y las bisecciones.

Lo liviano va libre: una clase de unitarios o de arquitectura con `--filter-class` sobre lo ya compilado,
un archivo de tests del front, `tsc`, `oxlint`, leer, editar y git.

## Uso

```powershell
pwsh -NoProfile -File harnesses/carril/carril.ps1 -- dotnet test --project tests/ArquitecturaBase.ArchitectureTests/ArquitecturaBase.ArchitectureTests.csproj
pwsh -NoProfile -File harnesses/carril/carril.ps1 -- dotnet test --project tests/X/X.csproj -- --filter-class "Namespace.Clase"
pwsh -NoProfile -File harnesses/carril/carril.ps1 -Status
```

Desde el front, con la ruta del backend: `pwsh -NoProfile -File ../ArquitecturaBase/harnesses/carril/carril.ps1 -- npm run test`.

- Todo lo que va después del primer `--` es el comando tal cual, también otro `--`. Tiene que ser un
  ejecutable (`dotnet`, `npm`, `node`, `pwsh`…). Un script de PowerShell va con `-- pwsh -NoProfile -File script.ps1`,
  y varios comandos en un mismo turno, con `-- pwsh -NoProfile -Command "npm run build; npm run test"`.
- El comando no recibe entrada: una pregunta, como la de `npx` para instalar un paquete, falla en lugar de trabar el carril.
- Si el comando queda quieto, sin usar el procesador, 30 minutos (`-StallMinutes`), o pasa de 4 horas
  (`-MaxRunMinutes`), el carril lo corta con todos sus hijos y devuelve 125. Si no lo puede arrancar, devuelve 127.
- Lo pesado va en segundo plano: la espera es parte de la corrida, y el plazo corto de una herramienta la cortaría.
- Mientras espera, avisa quién lo tiene (proceso, desde cuándo, carpeta y comando) y lo repite cada 5 minutos.
  `-MaxWaitMinutes` (240 por defecto) corta la espera con el código 124.
- Devuelve el código de salida del comando.
- Si el dueño muere sin soltarlo, Windows libera el turno y entra el siguiente.
- Un comando que ya corre dentro del carril y lo vuelve a llamar no se espera a sí mismo.

## Cómo funciona

Un mutex con nombre de Windows (`ArquitecturaBase.Carril`), que comparten todas las sesiones del usuario,
y un archivo en `.artifacts/carril/` que dice quién lo tiene. No instala servicios ni procesos de fondo.

Tests: `node --test harnesses/carril/tests/*.test.mjs`. Cada test usa su propio carril
(`AB_CARRIL_NAME` y `AB_CARRIL_STATE`), así no espera a las corridas reales de la PC.
