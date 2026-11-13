# Revisar la arquitectura de un desarrollo

Esta rutina toma un desarrollo entero (WhatsApp, el asistente, una parte de formularios, ARCA…) y lo
pasa por los mismos 30 criterios de calidad. Pregunta si se mezclan los datos o los pedidos de dos
personas, si aguanta varias copias de la app y mucho volumen, qué pasa cuando falla un tercero, y cómo
resuelve la seguridad, los datos, el idioma, las fechas, los errores y la operación. En la jerga son los
*atributos de calidad* de la norma ISO/IEC 25010, o una *production readiness review*. Es de solo
lectura: no cambia código ni reglas.

No reemplaza la [revisión de cada cambio](validar-un-cambio.md), que mira un diff, ni la
[auditoría documental](auditar-documentacion.md). Las convenciones siguen en las
[premisas](../architecture/premisas-de-la-plantilla.md): esta revisión comprueba que el desarrollo
las cumple y mira lo que ninguna convención cubre, que es cómo se comporta con carga, con varias
copias, con fallas y con entradas hostiles.

## Horizontes

Cada criterio se contesta en tres horizontes. El veredicto dice hasta dónde aguanta el desarrollo; no
obliga a construir para el horizonte más alto ([ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md)).
Qué horizonte se paga lo elige el usuario.

| Horizonte | Qué supone |
| --- | --- |
| **Hoy** | Una copia de la Api con sus workers, PostgreSQL y Redis; una instalación por negocio, con el volumen de un negocio chico o mediano. |
| **Varias copias** | Dos o más copias de la Api detrás de un balanceador (más réplicas de la Container App, o Kubernetes) con la misma base y el mismo Redis. |
| **Gran escala** | Millones de filas en las tablas principales, miles de personas activas en la misma hora y cientos de pedidos o mensajes por segundo en los picos. |

En cualquier horizonte, cada despliegue hace convivir un rato las dos versiones: la migración corre
antes, y la versión vieja atiende hasta que la nueva pasa readiness. Con varias copias, un despliegue
gradual alarga esa convivencia.

## Veredictos

| Veredicto | Cuándo |
| --- | --- |
| Cumple | Hay evidencia: el código que lo garantiza y, si existe, el test que lo prueba. |
| Con reservas | Se cumple en lo principal, con un hueco acotado (poco frecuente o de efecto chico), o falta evidencia para afirmarlo. |
| No cumple | Hay un camino concreto, con archivo y línea, que falla en ese horizonte en la operación normal o con un efecto serio. |
| No aplica | El desarrollo no tiene esa pieza; se dice por qué. |

- Sin evidencia no hay «Cumple». El nombre de un test sólo orienta: se lee su cuerpo.
- Cada «No cumple» y «Con reservas» lleva un escenario concreto: qué hace alguien, con qué datos o
  qué carga, y qué pasa. Se escribe para que lo entienda quien no programa.
- Cada «No cumple» y «Con reservas» pasa, antes de registrarse, por la verificación adversarial del
  [recorrido](#recorrido).
- Si el código muestra el camino que falla (un recorrido de toda la tabla, un lock global, una fila
  que atiende de a uno), es «No cumple», aunque cuándo empieza a fallar sólo lo diga una prueba real,
  que se nombra. Queda «Con reservas» lo que el código no alcanza a mostrar: cómo responde el proveedor
  de verdad o qué configura el despliegue.
- Lo que acepta una decisión documentada se registra como su costo, citando la fuente; cambiarla lo
  decide el usuario.

## La ficha del desarrollo

Delimita qué se revisa. La arma un agente leyendo el código; la primera vez la confirma el usuario.

- **Entradas:** rutas HTTP, webhooks, workers y tareas de fondo, pantallas del front.
- **Datos:** entidades y tablas, claves de Redis, archivos.
- **Terceros:** proveedores externos, otros módulos y qué se les manda.
- **Reglas:** secciones de `docs/features`, ADR y diseños que lo rigen.
- **Pruebas:** carpetas de tests de cada nivel.

Un desarrollo grande se parte en piezas con su propia ficha; formularios, por ejemplo, en
publicación, carga, respuestas, reglas y documentos.

## Los 30 criterios

Cada criterio tiene su pregunta y dónde mirar en este repositorio. «Ya lo cuida» nombra la regla o el
test que cubre parte del criterio en toda la plantilla; hay que confirmar que alcanza al desarrollo y
que éste no lo esquiva.

### 1. Que no se mezclen

1. **Estado por pedido.** ¿Queda algún dato de un usuario o de un pedido en memoria compartida,
   donde otro pedido lo vea o lo pise? Mirar campos `static` que cambian, singletons con estado,
   diccionarios en memoria y singletons que capturan servicios scoped. Ya lo cuida: la regla de
   [Infrastructure](../../src/ArquitecturaBase.Infrastructure/AGENTS.md) (un singleton no captura
   scoped y cada operación de fondo abre su scope).
2. **Ediciones simultáneas.** Si dos personas guardan lo mismo a la vez, ¿una pisa a la otra sin
   enterarse? Mirar el control de revisión con 409, los [locks](../architecture/backend.md#locks)
   y las escrituras que leen y guardan sin ninguno de los dos.
3. **Repeticiones (idempotencia).** Si el proveedor reenvía un webhook, alguien hace doble clic o un
   worker reintenta, ¿el efecto ocurre una sola vez? Mirar la deduplicación por el id del proveedor,
   los UUID de operación, los recibos idempotentes y los únicos de la base que los respaldan.
4. **Orden.** Si llegan juntos dos eventos de la misma persona o del mismo recurso, ¿se procesan en
   orden y sin pisarse el estado? Mirar los locks por contacto o por recurso, `SKIP LOCKED` y las
   colas FIFO.
5. **Todo o nada.** Si el proceso se corta a la mitad, ¿queda algo a medias? Mirar que cada método
   que escribe abra un solo límite ([ADR 0001](../decisions/0001-transaccion-explicita-por-caso-de-uso.md)),
   que no haya HTTP dentro de una transacción y que cada efecto externo se reserve antes y se
   confirme después. Ya lo cuida: `TransactionBoundaryTests` (a lo sumo un límite por método).
6. **Cada uno lo suyo.** ¿Se controla el permiso de la ruta y también que el recurso sea de quien lo
   pide? Cambiar un id en la URL no tiene que mostrar lo de otro, y las claves de caché incluyen a su
   dueño. Ya lo cuidan: `[HasPermission]` y `PermissionAuthorizationTests` (el permiso existe y se
   pide bien); la pertenencia del recurso se revisa a mano.

### 2. Varias copias de la app

7. **Sin estado propio.** ¿Sesiones, caché, archivos temporales, colas y claves de Data Protection
   viven fuera de la memoria y del disco de una copia? Mirar `Channel<T>` y otras colas en memoria,
   `Path.GetTempPath`, cachés en proceso y lo que se escribe en disco local. Ya lo cuidan: Redis
   como único caché ([ADR 0009](../decisions/0009-redis-como-unico-cache-de-datos.md)), los
   archivos privados en Azure Blob ([ADR 0012](../decisions/0012-azure-blob-como-primer-proveedor-de-archivos.md))
   y las claves de [ADR 0016](../decisions/0016-proteccion-de-claves-data-protection.md).
8. **Trabajos de fondo.** Con tres copias, ¿cada trabajo corre una sola vez y se retoma si una copia
   se cae? Mirar cada `BackgroundService`: lease y fence, `SKIP LOCKED`, reservas que vencen y
   avisos que sólo despiertan a su propia copia.
9. **Arranque, apagado y configuración.** ¿Avisa si está viva y lista (`/alive` y `/health`), valida
   sus opciones al arrancar, se apaga sin perder lo que estaba haciendo y toma de variables lo que
   cambia por entorno? Mirar `ServiceDefaults`, `ValidateOnStart`, el uso del token de apagado y
   las opciones con valores de una máquina (rutas locales, `localhost`).
10. **Despliegue sin corte.** ¿La base migrada funciona con la versión vieja y la nueva corriendo a
    la vez? Mirar las migraciones del desarrollo: columnas nuevas opcionales, nada renombrado ni
    borrado en el mismo paso, JSON que agrega sin romper lo anterior, valores de enum que la versión
    vieja no conoce y un `Down` que no pierde datos
    ([migración](migracion.md), [ADR 0006](../decisions/0006-migraciones-y-seed-fuera-de-development.md)).

### 3. Volumen

11. **Consultas.** ¿Cada filtro y cada orden tienen índice, sin una consulta por fila y sin traer
    tablas enteras? Mirar `HasIndex` en las configuraciones, los bucles que llaman a repositorios,
    los `ToListAsync` sin tope y las proyecciones de los lectores
    ([ADR 0008](../decisions/0008-nombres-de-repositorios-y-lectores.md)).
12. **Paginado y exportaciones.** ¿Todo listado tiene tope y orden estable, y una exportación grande
    se arma sin cargarla entera en memoria? Mirar `PagedRequest`, `ToPagedResultAsync`, el desempate
    único de `ApplySort` y los topes de Excel y PDF.
13. **Filtros y búsqueda.** ¿Se filtra sólo por campos permitidos, en SQL y antes del tope, y la
    búsqueda de texto usa un índice? Mirar `SortableFields`, `PagedRequestValidator<T>` y los
    `ILIKE '%…%'` o `Contains` sobre columnas sin índice de texto.
14. **Caché.** ¿Qué se guarda, cuánto dura, cómo se invalida y qué pasa si Redis no responde? Mirar
    clave, TTL e invalidación después del commit ([agregar caché](agregar-cache.md)). Ya lo cuida:
    `CacheBoundaryTests`.
15. **Trabajo pesado fuera del pedido.** ¿IA, PDF, Excel, archivos y llamadas a proveedores van a una
    cola o a una operación durable con estado consultable, sin dejar el pedido colgado? Mirar las
    rutas que esperan a un proveedor y sus plazos
    ([ADR 0017](../decisions/0017-inicio-asistente-y-operaciones-durables.md)).
16. **Filas únicas.** ¿Hay algo que atienda de a uno para todos (un worker que procesa en serie, un
    lock global, una espera dentro de un ciclo) y que con mucha gente haga cola? Mirar los ciclos de
    los workers, los `Task.Delay` y los locks que no dependen del recurso.

### 4. Terceros

17. **Plazos y reintentos.** ¿Cada llamada externa tiene timeout, reintenta sólo lo que es seguro
    repetir, con espera creciente, y deja de insistir cuando el proveedor está caído? Mirar los
    `HttpClient` tipados, sus handlers de resiliencia y qué métodos reintentan.
18. **Estado incierto.** Si el proceso muere justo después de llamar, ¿se sabe si el efecto ocurrió,
    sin repetirlo a ciegas? Mirar el recorrido reserva → efecto → confirmación, qué se hace con lo
    incierto (consultar antes de repetir) y quién puede resolverlo
    ([robustez proporcional](../architecture/backend.md#robustez-proporcional-al-efecto)).
19. **Degradación.** Si un proveedor o Redis se cae, ¿el resto sigue andando y el usuario entiende
    qué pasa? Mirar las versiones `Disabled*`, los chequeos de salud y los mensajes que ve el usuario.
20. **Límites y costo.** ¿Hay topes de pedidos por usuario y de gasto (tokens de IA, mensajes del
    proveedor), medidos antes de pagar? Mirar las políticas de
    [rate limiting](../../src/ArquitecturaBase.Api/RateLimiting/RateLimitingExtensions.cs), los
    topes de tamaño y las cuotas del módulo.

### 5. Seguridad

21. **Acceso.** ¿Las sesiones vencen y se revocan, los códigos sirven una sola vez, y los permisos se
    revalidan también en webhooks, workers y en lo que propone la IA? Mirar quién actúa en cada
    trabajo de fondo y con qué permisos ([ADR 0020](../decisions/0020-motor-unico-de-ia-con-capacidades.md)).
22. **Entradas hostiles.** ¿Se valida todo lo que entra? Mirar los validadores, la firma y el tamaño
    de los webhooks, los archivos subidos (tipo real y tamaño), el SQL armado a mano, las fórmulas en
    exportaciones a Excel (celdas que empiezan con `=`, `+`, `-` o `@`) y los textos de usuarios que
    llegan a un modelo de IA (*prompt injection*).
23. **Datos sensibles.** ¿Los logs están limpios y los secretos van cifrados? Mirar los
    `[LoggerMessage]` (sin teléfonos enteros, códigos de ingreso, tokens ni cuerpos), el `ToString()`
    de los contratos y los protectores de Data Protection con propósito propio
    ([ADR 0014](../decisions/0014-credenciales-de-ia-fuera-de-la-base.md),
    [ADR 0015](../decisions/0015-colas-transaccionales-cifradas.md)). Ya lo cuida: la regla de logs
    de [`AGENTS.md`](../../AGENTS.md#otras-reglas-fijas); el contenido de cada log se revisa a mano.

### 6. Datos

24. **Integridad en la base.** ¿Claves foráneas, únicos y checks respaldan las reglas, también con el
    borrado lógico? Mirar las configuraciones EF y las migraciones; un único que ignore las filas
    borradas necesita su filtro. Ya lo cuida: `EntityConfigurationTests` exige la configuración de
    cada entidad; las restricciones se revisan a mano.
25. **Privacidad y respaldo.** ¿Cuánto se guardan teléfonos, mensajes, audios y documentos, cómo se
    borran, y el backup se restaura de verdad? Mirar los workers de retención, las purgas, los datos
    personales en tablas y en archivos, y el [despliegue](despliegue.md).

### 7. Lo que ve el usuario

26. **Idioma.** ¿Cada texto sale de un `.resx` con su `.en.resx`, y quien recibe lo lee en su idioma
    también por WhatsApp, correo, PDF y Excel? Las claves ya las cuidan `ResourceParityTests`, que recorre
    todos los `.resx` de Application, y `EmailTemplateRendererTests`, los de los correos; un `.resx` en otra
    capa necesita el suyo.
27. **Fechas, zonas horarias y números.** ¿Se guarda en UTC con el reloj inyectado, y «hoy», «vence» o
    «abre» se calculan en la zona del negocio? Calculado en UTC, en Argentina el día cambia a las 21.
    ¿Las fechas le llegan al usuario sólo con números, y los números con la región de su idioma? Mirar
    `TimeZoneInfo`, `DateOnly`, los cortes de día y los textos que piden un formato. Ya lo cuidan:
    `BannedSymbols.txt` (sin leer el reloj del sistema) y `NumberFormattingTests`.
28. **Errores.** ¿Cada error tiene un código estable y un mensaje que le sirve a quien lo ve, sin
    detalles internos, igual en la web y en el canal? Ya lo cuidan: `ErrorDeclarationTests` y
    `ErrorCodeTests`; lo que llega por el canal se revisa a mano.

### 8. Operación y evolución

29. **Observabilidad.** ¿Se puede seguir un mismo pedido por la web, el worker y el webhook, y saber
    quién hizo qué? Mirar las trazas de OpenTelemetry (`ServiceDefaults`), qué lleva cada log y la
    auditoría (`IAuditable`).
30. **Separabilidad.** Si mañana el módulo fuera un servicio aparte, ¿qué se rompería? Mirar las
    tablas ajenas, las transacciones compartidas y las llamadas que no pasan por un puerto
    ([módulos](../architecture/backend.md#módulos-opcionales)). Ya lo cuida, entre módulos:
    `ModuleBoundaryTests`. Las lecturas de tablas del núcleo y los guardados compartidos con él están
    permitidos y se inventarían a mano; la guía de quitar el módulo se contrasta con sus migraciones.

## Recorrido

1. **Preparar.** Leer los `AGENTS.md` del desarrollo y su regla en `docs/features`. Anotar el commit de
   cada repositorio (backend y front) y su `git status --short`: la revisión vale para esos commits. Si
   otras sesiones tienen cambios sin commitear en el área, la ficha y los revisores leen una copia de
   cada commit (`git archive` extraído en una carpeta temporal fuera del repositorio; en el front alcanza
   `git show <commit>:<ruta>`), para que los conteos, el archivo y la línea coincidan con lo revisado.
2. **Ficha.** Armarla y, la primera vez, confirmarla con el usuario.
3. **Revisar por bloque.** Un revisor por bloque, con la ficha y esta guía; en un desarrollo grande,
   los bloques largos se reparten entre dos (WhatsApp, con unos 370 archivos, llevó nueve revisores).
   Sólo leen: no editan, no compilan, no corren tests ni levantan la app, para no pisar a otras
   sesiones ni cargar la máquina. Citan archivo y línea, y nombran un test sólo después de leer su
   cuerpo.
4. **Verificar.** Cada «No cumple» y «Con reservas» pasa por un verificador que recibe el escenario y
   el código, no la conclusión, e intenta refutarlo. Si lo refuta, el veredicto se corrige. Se verifica
   en tandas, a medida que llegan los informes, sin repetir un hallazgo que aparece en dos bloques
   (WhatsApp llevó siete verificadores). Lo que un verificador encuentra fuera de los criterios, como
   un bug funcional, se registra aparte.
5. **Consolidar.** La matriz de los 30 criterios en los tres horizontes; los hallazgos, cada uno con
   su escenario, evidencia, arreglo, esfuerzo y el horizonte que destraba; lo que está bien, con su
   evidencia; y lo que se puede verificar con código, propuesto como test de arquitectura. Un hallazgo
   que toca varios criterios se escribe una vez y se nombra en cada fila de la matriz.
6. **Registrar.** En `docs/reviews/AAAA-MM-DD-arquitectura-<desarrollo>.md`, con los commits revisados.
   Los arreglos que se elijan se planifican aparte en `docs/plans`; si de ahí sale una regla nueva, va
   a su fuente canónica en ese desarrollo.
7. **Repetir.** Cuando el desarrollo cambia mucho, o antes de subir de horizonte, se repite y se
   compara con la revisión anterior.

## Pedido reutilizable

> Revisá la arquitectura de <desarrollo> siguiendo `docs/guides/revisar-arquitectura.md`. Armá la
> ficha (la primera vez, confirmala conmigo), contestá los 30 criterios en los tres horizontes con
> evidencia de archivo y línea, verificá cada «No cumple» y «Con reservas» con un verificador que
> intente refutarlo, y registrá la matriz y los arreglos en `docs/reviews`. Es de solo lectura: no
> cambies código ni reglas.
