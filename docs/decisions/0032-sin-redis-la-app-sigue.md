# ADR 0032: sin Redis la app sigue, y los permisos se leen de la base

## Estado

**Aceptada**, 2026-11-14, por decisión del usuario sobre la
[revisión de arquitectura del núcleo](../reviews/2026-11-14-arquitectura-nucleo.md#decisiones-del-2026-11-14) (N9, que
resuelve también N33 y O12). Rige desde el 2026-11-16, con la tarea 15 de los
arreglos del núcleo ([regla](../architecture/backend.md#caché-compartido-en-redis)). Enmienda el
[ADR 0009](0009-redis-como-unico-cache-de-datos.md) en lo que dice de los permisos y de la caída de Redis, y el
[ADR 0010](0010-catalogo-de-permisos-extensible.md) en lo que dice de invalidar después del seed. El resto de los dos sigue
vigente: Redis directo sigue siendo el único caché de datos compartidos.

## Contexto

El ADR 0009 hizo de Redis una dependencia obligatoria: su caída fallaba `/health` y toda operación que lo usara. Como los
permisos y el modo de registro pasaban por Redis, un corte de más de unos 30 segundos dejaba sin responder toda ruta con
permiso, `/api/me` y el ingreso; `/health` sacaba la copia del tráfico, así que tampoco cargaban la app, la pantalla de
ingreso ni los webhooks de Meta, que no lo necesitan; y una copia que se reiniciaba durante el corte no arrancaba, porque el
seed invalidaba en Redis al terminar.

La caché de permisos, además, no ahorraba consultas: cada pregunta leía en la base los roles de la persona y la versión de
cada rol antes de ir a Redis (1 + R consultas y R viajes, y hay políticas que preguntan tres veces). Adentro de un límite,
leer la versión abría otra conexión mientras la transacción retenía la suya.

## Decisión

1. **Los permisos se leen de la base, sin caché:** una sola consulta indexada por persona, con el contexto del pedido, que
   se memoriza en ese scope. Adentro de un límite corre en su conexión y su transacción. Se retiran
   `IPermissionCacheInvalidator`, la clave `permissions:role:<id>:<revisión>` y su lease.
2. **Sin Redis la app sigue:** su chequeo deja `/health` en Degraded, que responde 200, y la copia sigue en el tráfico; los
   ajustes (modo de registro y presentación) se leen de la base, sin cachear, si Redis no responde; y el seed termina aunque
   no pueda descartar los ajustes en Redis. «No responde» es no tener conexión, un comando vencido, un error del servidor o
   un comando que el cliente cancela al soltar un socket colgado: toda cancelación que no pidió quien llama.

## Consecuencias

- **Un cambio de roles o de permisos vale desde el pedido siguiente,** en todas las copias, sin descartar nada. Tampoco hay
  que avanzar el `ConcurrencyStamp` para que se vea: un cambio de claims fuera de Identity también vale.
- **Una consulta por persona y por pedido,** aunque la política pregunte varias veces: `SELECT DISTINCT` de los claims de
  permiso de los roles de la cuenta, que entra por la clave primaria de `AspNetUserRoles` y el índice de `RoleId` de los
  claims. Antes eran 1 + R consultas por pregunta.
- **Lo memorizado vale lo que dura el scope:** un cambio de roles en la mitad de un pedido no cambia lo que ese pedido ya
  preguntó. Un worker que pregunta por muchas personas memoriza una entrada por persona; abre un scope por operación, como
  ya hace.
- **Con Redis caído o colgado** anda todo lo que no lo necesita para su efecto: el arranque, el ingreso por correo y con
  Google, los permisos, la configuración, la app y los webhooks. Lo que sigue necesitándolo falla como antes: el envío de
  WhatsApp, que coordina las copias con una lease de Redis, y la consulta de ARCA que se cachea. Por eso el código de
  ingreso por WhatsApp no sale hasta que vuelve Redis: espera esa lease. Guardar los ajustes confirma y, si no puede
  descartar el caché, responde 500 genérico como hasta ahora; al volver Redis, lo que conservaba de antes vence a los 60
  segundos.
- **La lectura de los ajustes sin Redis** pega a la base en cada pedido mientras dura el corte. El log lleva un Warning por
  proceso y por minuto, con la excepción; las demás lecturas van a Debug, sin ella.
- **El front** muestra un texto propio para un 5xx sin referencia (el 502 o el 504 del ingress, o una respuesta que no es
  ProblemDetails), en lugar de pedir que se informe el código «?».

## Alternativas descartadas

- **Lo mínimo, sólo el chequeo en Degraded y el texto del front:** deja sin ingreso ni permisos hasta que vuelva Redis.
- **Memorizar los permisos por scope y conservar la caché de Redis:** evita las preguntas repetidas, pero no saca la segunda
  conexión adentro de un límite ni la dependencia de Redis.
- **Un caché local por proceso como respaldo:** el ADR 0009 retiró los niveles locales, y una revocación tardaría en verse en
  cada copia.
