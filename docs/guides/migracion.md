# Migración

Cómo agregar una migración de EF Core después de cambiar el modelo (una entidad, su configuración o un índice) y cómo comprobarla sin Docker. Lo general de las migraciones (qué paquete las habilita, que el comando pase la cadena de conexión como argumento de la aplicación y cómo se aplican en cada ambiente) está en [backend.md, "Migraciones"](../architecture/backend.md#migraciones); cómo salen a producción, en la [guía de despliegue](despliegue.md#el-orden-bundle-después-imagen). Si la migración es parte de un área nueva, el [paso 3 de la receta](agregar-un-area.md#3-migración) dice qué revisar en la generada.

Todo se corre desde la raíz del repo. Ni `migrations add` ni `has-pending-model-changes` necesitan PostgreSQL, Redis ni Docker. Las dos conexiones se declaran para construir los servicios; estos comandos no se conectan.

## Los pasos

1. **Instalá `dotnet ef`, una sola vez.** No viene con el repo (no hay manifiesto `.config/dotnet-tools.json`): es una herramienta global, con la misma versión que `Microsoft.EntityFrameworkCore.Design` en [`Directory.Packages.props`](../../Directory.Packages.props) (hoy, 10.0.12):

   ```
   dotnet tool install --global dotnet-ef --version 10.0.12
   export PATH="$PATH:$HOME/.dotnet/tools"
   dotnet ef --version
   ```

   En Linux y macOS, `~/.dotnet/tools` tiene que estar en el `PATH` (el `export` vale para esa terminal); en Windows el instalador ya lo suma. Si la versión del paquete cambia, actualizá la herramienta con `dotnet tool update --global dotnet-ef --version <la nueva>`. El paso "Instalar dotnet-ef" de [`deploy.yml`](../../.github/workflows/deploy.yml) fija la misma versión (`--version 10.0.12`): cambiala ahí también, para que el bundle salga de la misma herramienta que usás vos.

2. **Cambiá el modelo** (la entidad en `src/ArquitecturaBase.Domain/<Área>/`, su configuración en `src/ArquitecturaBase.Infrastructure/Persistence/Configurations/`) y compilá.

3. **Generá la migración**, con un nombre que diga qué cambia (`AddProducts`, `AddUserPhoneIndex`):

   ```
   dotnet ef migrations add <Nombre> --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api --output-dir Persistence/Migrations -- --environment Development --ConnectionStrings:appdb "Host=localhost;Port=5433;Database=appdb;Username=postgres;Password=postgres" --ConnectionStrings:cache "localhost:6379"
   ```

   Deja tres cambios en `src/ArquitecturaBase.Infrastructure/Persistence/Migrations/`: `<fecha>_<Nombre>.cs`, su `.Designer.cs` y `ApplicationDbContextModelSnapshot.cs` actualizado. Los tres van en el mismo commit.

4. **Revisá lo generado.** Que `Up` y `Down` hagan solo lo que cambiaste: una columna, un índice o una tabla de más o de menos es un cambio del modelo que no buscabas. Un índice único sobre un nombre va sobre la columna normalizada y, si la entidad es `ISoftDeletable`, con su `filter` ([Nombre único](agregar-un-area.md#nombre-único-sin-distinguir-mayúsculas)).

5. **Comprobá que no quedó nada afuera**, con los mismos argumentos:

   ```
   dotnet ef migrations has-pending-model-changes --project src/ArquitecturaBase.Infrastructure --startup-project src/ArquitecturaBase.Api -- --environment Development --ConnectionStrings:appdb "Host=localhost;Port=5433;Database=appdb;Username=postgres;Password=postgres" --ConnectionStrings:cache "localhost:6379"
   ```

   Tiene que responder `No changes have been made to the model since the last migration.`

6. **Aplicarla no es un paso aparte.** En Development la Api aplica las migraciones pendientes al arrancar (`aspire run`). Fuera de Development las aplica el bundle del pipeline antes de desplegar la imagen, y la Api no arranca si falta alguna ([ADR 0006](../decisions/0006-migraciones-y-seed-fuera-de-development.md)).

## Trampas

- **Los argumentos después de `--` son de la Api, no de `dotnet ef`.** Declarar tanto `--ConnectionStrings:appdb` como `--ConnectionStrings:cache`: la Api las recibe de Aspire en ejecución, y sin ellas falla al construir el host o el contexto. En estos comandos solo deben existir; no se conectan. El bundle del pipeline usa conexiones de mentira para generarse y declara la conexión de Redis también al ejecutarse, sin necesitar ese servidor.
- **No corras `dotnet ef database update`** contra la base del AppHost: la aplica la Api al arrancar.
- **Una migración que se generó mal se borra**, no se edita: borrá sus dos archivos, volvé `ApplicationDbContextModelSnapshot.cs` a lo que estaba (`git checkout` del archivo) y generala de nuevo. `dotnet ef migrations remove` también sirve, pero consulta la base para saber si ya se aplicó.
- **Las migraciones son código generado:** `.editorconfig` las excluye del estilo (`[**/Migrations/*.cs]`, `generated_code = true`). No las edites a mano salvo para corregir lo generado (un `DropColumn` y `AddColumn` donde iba un `RenameColumn`) o para escribir una migración de datos adentro de una generada vacía, con un comentario y un test que migre filas guardadas ([backend.md, "Migraciones"](../architecture/backend.md#migraciones)). Hay un tercer caso: partir en dos despliegues una migración que borra (una tabla o una columna que la versión anterior todavía usa). En el primero, se guardan aparte el `Up` y el `Down` tal como los generó EF, y se sacan de la generada el borrado del `Up` y su inverso del `Down` (si el `Down` queda, volver atrás crea lo que nunca se borró), con un comentario que diga por qué; en el siguiente, se escriben el borrado y su `Down` en otra generada vacía ([despliegue](despliegue.md#el-orden-bundle-después-imagen)).
- **Tiene que convivir con la versión anterior.** Entre el bundle y la imagen nueva, la imagen vieja corre contra el esquema nuevo: primero se agrega, y lo que se borra sale en otro despliegue ([despliegue](despliegue.md#el-orden-bundle-después-imagen)).

## Lo verifica

- Sin Docker: `has-pending-model-changes` (paso 5) y [`EntityConfigurationTests`](../../tests/ArquitecturaBase.ArchitectureTests/EntityConfigurationTests.cs), que exige una configuración por entidad.
- Con Docker: [`MigrationsTests`](../../tests/ArquitecturaBase.Api.IntegrationTests/Persistence/MigrationsTests.cs), `Model_has_no_pending_changes` (falla si el modelo cambió y falta la migración) y `Migrations_create_the_schema_on_an_empty_database`.
