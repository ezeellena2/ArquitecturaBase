# ADR 0019: SQL de configuración acotado a catálogos

## Estado

**Aceptada**, 2026-10-11, por decisión explícita del usuario. No habilita SQL configurable ni la implementación de Query/K5 por ahora.

## Contexto

La Etapa 2 necesita catálogos compartidos. `Manual` usa ítems mantenidos por la aplicación y `Filters` compila condiciones sobre vistas controladas; `Query` propone una consulta de lectura que administra una persona. La arquitectura no permite que SQL escrito por usuarios o por un LLM llegue a ejecutarse. La propuesta tiene que separar esa configuración de SQL arbitrario y conservar los límites entre Application e Infrastructure.

## Decisión

**Por ahora no se admite SQL configurable.** La Etapa 2 entrega `Manual` y `Filters`, la pregunta de catálogo (K6) y su resolución al enviar y responder (K7). K2 sólo muestra los dos orígenes disponibles. Query/K5 se retoma cuando una lista necesaria no se pueda expresar con `Filters`; aceptar este ADR no habilita su implementación ahora.

Al retomarlo, las consultas configuradas estarán limitadas a `Catalogs.Query`, aprobadas por una persona autorizada y sobre vistas inventariadas bajo el esquema `catalogos`, mediante un puerto de lectura del módulo y una credencial separada y restringida. `Filters` conserva la gramática común compilada. Se mantienen como requisitos todas las condiciones siguientes:

- El único parámetro de contexto permitido es `:usuario_actual`, resuelto en el servidor; la zona no existe como dato y no se inventa ni se admite como parámetro.
- Un catálogo inválido no frena el despliegue: queda **«No se puede usar»**, con su motivo seguro y sin ejecutarse, como en K1.
- La vista previa usa una persona de ejemplo elegida explícitamente por quien está autorizado, como en K5. Calcula ese contexto sin suplantar su identidad ni sus permisos.
- Query tiene un permiso propio, distinto de administrar Manual/Filters; su código se define al retomarlo con la guía de permisos del repositorio.

La consulta ejecutable debe validarse como una sola lectura mediante parser y AST con listas permitidas de vistas, columnas, funciones y operadores; no alcanza una expresión regular ni comprobar que el texto empiece con `SELECT`. Se rechazan escritura, DDL, `COPY`, locks, comandos múltiples y funciones con efectos. Los valores variables se enlazan como parámetros; el preview y la ejecución aplican cancelación, timeout, presupuesto y topes de filas y bytes. La conexión no usa el login de EF ni privilegios de owner/superusuario; un `search_path` fijo y la revisión de permisos sobre funciones y vistas complementan el modo de sólo lectura.

El guardado requiere validar, ejecutar un preview acotado con esa credencial y aprobar una revisión identificada por hash. Después de cada migración, las consultas guardadas se revalidan; una inválida queda indisponible con un motivo estable y no se ejecuta por omisión. El SQL, los parámetros sensibles y los resultados no se registran ni se devuelven en errores. Un LLM podrá proponer configuración en una etapa posterior, pero nunca ejecutarla ni aprobarla.

## Consecuencias

Manual y Filters avanzan sin parser ni rutas, permisos o editor de SQL configurable. Las siguientes responsabilidades son requisitos futuros de K5, no infraestructura a crear por anticipado: Application mantiene el caso de uso y el contrato del puerto sin EF, Npgsql ni acceso directo a la base; Infrastructure contiene el parser, la conexión restringida y el lector. K5 agrega dependencia de PostgreSQL, provisión y rotación de credenciales, mantenimiento del inventario de vistas y revisión de seguridad del parser. Antes de implementarlo habrá que elegir parser, lista concreta de funciones/operadores, código del permiso propio y provisión del rol. El parámetro, el contexto de vista previa y la continuidad del despliegue ya están decididos arriba.

Al retomar Query, las pruebas deberán usar el rol restringido real y cubrir accesos ajenos, funciones y escrituras, múltiples sentencias, parámetros enlazados/inyección, rechazo de parámetros distintos de `:usuario_actual`, cancelación y timeout, límites y propuestas no aprobadas. Una migración que invalida un catálogo debe permitir completar el despliegue y dejarlo «No se puede usar» con motivo seguro. También se prueban el permiso propio y la vista previa con persona explícita sin suplantación. La revisión de arquitectura debe asegurar que ningún otro servicio obtenga una ruta directa a tablas o SQL configurable.

## Alternativas descartadas

- **Implementar Query en la Etapa 2:** se descarta por ahora; Manual y Filters cubren los orígenes actuales. Sólo una lista que no pueda expresarse con Filters justifica retomarlo.
- **Catálogos tipados por proveedor:** reduce la superficie de consulta, pero ata cada origen nuevo a cambios de código y no ofrece la expresividad acordada para Query.
- **SQL libre con un login de sólo lectura:** se descarta porque `SELECT` y el modo de sólo lectura por sí solos no restringen vistas, funciones, catálogos internos ni consumo de recursos.
