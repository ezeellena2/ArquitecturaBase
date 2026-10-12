# Base de demostración: modelo de formularios

Una base PostgreSQL con el modelo propuesto para [formularios por WhatsApp](../formularios/README.md)
y datos de ejemplo coherentes, para mirarla en DBeaver y hacerle consultas. Es material de estudio de una propuesta:
**no es la migración de EF** ni código del producto. Las tablas usan los nombres que tendrían en el sistema, con los
ajustes recomendados en la página «Modelo de formularios» (versión sin reglas, Reglas con revisiones propias,
un solo motor de Ejecuciones, Tareas, catálogos como formularios).

Las decisiones posteriores (reglas y sugerencias aparte, prefijo, catálogos con vistas, modo Asistente) están en
los [resúmenes por etapa](../formularios/README.md). Donde difieren, prevalecen los resúmenes: en particular, esta base
todavía modela los catálogos como formularios de tipo catálogo.

## Levantarla

Necesita Docker. Desde esta carpeta, en PowerShell:

```powershell
.\levantar.ps1            # la primera vez la crea y carga; después solo la arranca
.\levantar.ps1 -Recargar  # vuelve a cargar todo (pierde lo que hayas cambiado)
.\levantar.ps1 -Borrar    # elimina el contenedor
```

Corre en un contenedor propio, `formularios-demo`, en el puerto **5440** y solo para esta máquina. No toca la
base de Aspire (5433).

## Conectarse desde DBeaver

Nueva conexión → PostgreSQL:

| Campo | Valor |
| --- | --- |
| Host | `localhost` |
| Port | `5440` |
| Database | `formularios_demo` |
| Usuario | `postgres` |
| Contraseña | vacía: el contenedor solo acepta conexiones locales y no la pide |

## Qué hay adentro

| Esquema | Para qué |
| --- | --- |
| `vistas` | Para empezar: planillas y listados en castellano (`planilla_registrar_visita`, `trabajos`, `historial`, `tareas`…) |
| `app` | Las tablas reales: `BusinessForm*` (formularios), `WhatsApp*` (canal) y versiones reducidas de `AspNetUsers`/`AspNetRoles` |
| `demo` | `reconstruir_valores()`: arma la tabla de valores desde el JSON de cada respuesta |

Cada tabla y columna importante tiene un comentario que explica para qué sirve; en DBeaver se ve en la pestaña
Propiedades. En la pestaña «Diagrama ER» del esquema `app` se ven las relaciones.

Los datos cuentan la misma historia que los tableros: «Registrar visita» con tres versiones y 24 respuestas,
«Registrar mantenimiento» con 13, el catálogo «Clientes», «Pedido de materiales» que elige clientes del catálogo y
«Remito de entrega» sin publicar. Para la demo, «ahora» es el 8 de octubre de 2026 a las 15:04: VIS-000024 acaba
de llegar y tiene trabajos pendientes y en curso; VIS-000021 tiene un PDF sin confirmar; MNT-000010 tiene un aviso
que falló.

## Archivos

| Archivo | Qué hace |
| --- | --- |
| `01-esquema.sql` | Crea las tablas, restricciones, índices y comentarios. Solo corre en la base `formularios_demo` |
| `02-datos.sql` | Carga los datos de ejemplo. Lo generó un script; los IDs son UUID v7 fijos |
| `03-vistas.sql` | Crea las vistas en castellano |
| `04-consultas.sql` | Diecinueve consultas guiadas, de lo simple a lo profundo. Abrilo en DBeaver y corré una por una con Ctrl+Enter |
| `05-recorrido-crear-formulario.sql` | Recorrido paso a paso: el administrador crea y publica «Registrar entrega». Cada paso es lo que escribiría el sistema, con sus consultas para mirar el resultado. `levantar.ps1` lo deja cargado; el paso 0 lo borra para repetirlo |
