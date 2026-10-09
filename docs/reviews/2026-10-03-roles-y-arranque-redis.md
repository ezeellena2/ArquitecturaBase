# Roles y arranque de Redis: verificación del 2026-10-03

El usuario pidió diferenciar con color los encabezados de área (Usuarios, Roles,
Configuración, WhatsApp e Inteligencia artificial) manteniendo blancos los permisos
individuales. También mostró una RedisConnectionException al levantar la API.

## Cambio visual

TableRow incorpora tone="section", compuesto desde PermissionPicker sin estilos propios
de la feature. Los tokens table-section/table-section-foreground presentan una banda Campo
suave y se derivan de la paleta guardada en Apariencia. El estado expandido no cambia el
tono y TableBody striped=false conserva permisos blancos. El encabezado principal sigue
más marcado. No se cambiaron medidas, navegación, permisos ni lógica de selección.

TDD de contraste: primero fallaron seis casos sin los nuevos tokens; después pasaron los
ocho casos de Apariencia y los 30 del selector. TDD de navegador: es/en fallaron primero
porque los encabezados eran blancos; pasaron después del cambio, conservando blancos los
permisos y un color distinto para el encabezado principal.

Build, lint (39 reglas/pruebas de arquitectura) y los **886 tests de front** pasaron. La
primera suite Playwright tuvo 94/95: una diferencia de color en la referencia del editor
a 320 px. Se revisó imagen y diff y se regeneraron únicamente editor-1440.png/editor-320.png
para la elección autorizada. La ronda final pasó **95/95 Playwright**, incluyendo
responsive es/en y regresiones de login, chat, roles y configuración.
No se modificaron las referencias de otras pantallas.

[Captura de los componentes productivos con datos de prueba](../../../ArquitecturaBaseFront/design-lab/whatsapp-chat/evidence/roles-area-headings-es.png).
El bootstrap del catálogo y los proveedores permanecen simulados.

## Excepción observada y alcance del diagnóstico

La captura muestra `No connection is active/available` al ejecutar EVAL en RemoveAsync,
con `UnableToConnect`, estado `Connecting`, `Initializing/NotStarted`, `last: NONE` y
`last-heartbeat: never`. Es una conexión de Redis que aún no se había establecido; no
demuestra corrupción de PostgreSQL ni de Información del negocio. Durante el arranque,
DatabaseSeeder confirma sus cambios y después invalida roles/ajustes en Redis. Esa
invalidación requiere Redis; un fallo posterior al commit no revierte lo confirmado.

AppHost ya referencia `cache` y usa WaitFor(cache). CachingRegistration conserva
AbortOnConnectFail=false, ConnectTimeout/AsyncTimeout/SyncTimeout de 3000 ms y BacklogPolicy.FailFast.
Un multiplexer desconectado puede ser creado y el primer comando fallar; no equivale a
que el servidor estuviera listo para ese cliente. No se alteraron esas opciones, no se
agregaron demoras, catches que oculten el fallo ni un caché local.

Al inspeccionar después de la captura, Redis/AppHost ya no estaban ejecutándose. Se hizo
**un arranque propio** desde AppHost con el túnel público desactivado sólo mediante el
argumento de ejecución DevTunnel:Enabled=false, sin modificar secretos/configuración.
Redis 8.6.6 anunció que aceptaba conexiones tcp/tls. La API arrancó y respondió
`https://localhost:7180/health` con **HTTP 200 y Healthy**. Buscar RedisConnectionException
en sus logs no encontró resultados. La excepción original **no se reprodujo**, por lo
que su causa anterior exacta (endpoint, disponibilidad, transporte o reconexión) queda
sin confirmar. El mensaje del dashboard sólo acredita pérdida de su conexión con el host;
no permite adjudicar por sí solo la falla a la API.

Las **14 pruebas existentes de Caching** pasaron con PostgreSQL/Redis reales en Docker,
incluyendo seed, invalidación, fallas y concurrencia entre hosts. No se llamó a proveedores
facturables ni se cambió código backend de Redis para presentar una reparación no demostrada.

Se ejecutó aspire stop sobre la instancia propia; la CLI confirmó el apagado. Quedó un
Redis efímero residual: se verificaron las etiquetas de su proceso creador, fecha y
persistent=false antes de detenerlo por su nombre. La comprobación final dejó sólo
PostgreSQL persistente en Docker. No se detuvo una sesión de Visual Studio ajena ni se
eliminó una base o volumen. Cambios existentes preservados, main, sin commits ni push.

Si se repite en Visual Studio, conservar la excepción interna y logs del mismo arranque
de cache/api antes de detenerlo. Arrancar el AppHost con Docker activo y referencias
inyectadas; no fijar en configuración el puerto aleatorio de un arranque previo ni
suprimir la invalidación de seguridad. El botón Running del contenedor no acredita
conectividad del cliente .NET. No se verificó una segunda reproducción bajo el depurador.

Documentación oficial consultada: [configuración de StackExchange.Redis](https://seredis.dev/Configuration.html)
y [conexión Redis de Aspire](https://aspire.dev/integrations/caching/redis/redis-connect/).
La comprobación directa usa las opciones y versión del repositorio; las guías no
sustituyen los logs del intento que falló.
