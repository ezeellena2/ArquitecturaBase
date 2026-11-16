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
- **Tiene que convivir con la versión anterior.** Entre el bundle y la imagen nueva, la imagen vieja corre contra el esquema nuevo: primero se agrega, y lo que se borra sale en otro despliegue ([despliegue](despliegue.md#el-orden-bundle-después-imagen)). Las reglas están en [Convivencia de versiones](#convivencia-de-versiones).

## Convivencia de versiones

Con varias copias y un despliegue gradual, o al volver a la imagen anterior, dos versiones comparten la base. Lo que escribe la nueva lo tiene que poder leer y escribir la vieja. Se resuelve en dos despliegues: el primero deja que todos toleren el cambio y el siguiente lo empieza a usar.

- **Un CHECK nuevo sobre una tabla que ya existía va en el despliegue siguiente.** Un CHECK que ata una columna nueva a una que la versión vieja escribe (por ejemplo `"NotSendable" = ("NotSentReason" IS NOT NULL)`) rechaza lo que la vieja guarda, porque ella no conoce la columna nueva. Lo mismo vale para un CHECK que sólo mira columnas viejas. Sólo se agrega en la misma migración un CHECK sobre una tabla nueva o que mencione únicamente columnas nuevas.
- **Una columna que pasa a aceptar nulos** se tolera primero: una versión que lee el nulo sin romper, y después la que lo escribe.
- **Un borrado no viaja con su reemplazo.** Se agrega lo nuevo en un despliegue y se borra lo viejo (o se renombra) en el siguiente; un renombre es siempre un agregado y un borrado.
- **Un valor nuevo de una enumeración guardada como texto** se lee en un despliegue y se escribe en el siguiente; la vieja lanzaría al leerlo. Las de Formularios están listadas en [`FormStoredEnumTests`](../../tests/ArquitecturaBase.ArchitectureTests/Modules/BusinessForms/FormStoredEnumTests.cs): sumar un valor obliga a editar esa lista, y ese es el momento de aplicar la regla. Excepción conocida: `FormActionRunState.NotApplicable` (estado de las acciones, 2026-11-15) se empezó a escribir en la misma versión que lo lee; mientras existan filas con él, volver a la imagen anterior rompe la lectura de esas acciones.
- **Un dato sellado o serializado que la propia app vuelve a abrir** se abre sin rechazar lo desconocido: el comprobante de guardado de Formularios ignora los miembros que no conoce, de modo que una versión que le suma un dato lo puede leer la anterior.

Lo comprueba [`MigrationCoexistenceTests`](../../tests/ArquitecturaBase.ArchitectureTests/MigrationCoexistenceTests.cs) en cada migración posterior a `20261116130834` (las anteriores ya están aplicadas): un CHECK nuevo sobre una tabla existente que mencione una columna que no se agrega en la misma migración, un CHECK en SQL crudo, una columna que pasa a aceptar nulos, un borrado de columna junto con un agregado en la misma tabla y un renombre. Si una migración lo necesita de verdad porque la versión anterior ya lo tolera, se anota en `Acknowledged` con el motivo.

## Lo verifica

- Sin Docker: `has-pending-model-changes` (paso 5), [`MigrationCoexistenceTests`](../../tests/ArquitecturaBase.ArchitectureTests/MigrationCoexistenceTests.cs) y [`EntityConfigurationTests`](../../tests/ArquitecturaBase.ArchitectureTests/EntityConfigurationTests.cs), que exige una configuración por entidad.
- Con Docker: [`MigrationsTests`](../../tests/ArquitecturaBase.Api.IntegrationTests/Persistence/MigrationsTests.cs), `Model_has_no_pending_changes` (falla si el modelo cambió y falta la migración) y `Migrations_create_the_schema_on_an_empty_database`.
