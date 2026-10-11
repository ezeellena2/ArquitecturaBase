# ADR 0019: SQL de configuración acotado a catálogos

## Estado

**Propuesta**, 2026-10-11. No habilita la implementación de Query/K5.

## Contexto

La Etapa 2 necesita catálogos compartidos. `Manual` usa ítems mantenidos por la aplicación y `Filters` compila condiciones sobre vistas controladas; `Query` propone una consulta de lectura que administra una persona. La arquitectura no permite que SQL escrito por usuarios o por un LLM llegue a ejecutarse. La propuesta tiene que separar esa configuración de SQL arbitrario y conservar los límites entre Application e Infrastructure.

## Decisión

Se propone permitir consultas configuradas únicamente para `Catalogs.Query`, aprobadas por una persona autorizada y limitadas a vistas inventariadas bajo el esquema `catalogos`, mediante un puerto de lectura del módulo y una credencial separada y restringida; `Filters` seguirá usando la gramática común compilada. Hasta aceptar este ADR y resolver las decisiones de implementación indicadas abajo, Query/K5 permanece bloqueado; Manual, Filters, la pregunta de catálogo y su resolución pueden avanzar sin SQL configurable.

La consulta ejecutable debe validarse como una sola lectura mediante parser y AST con listas permitidas de vistas, columnas, funciones y operadores; no alcanza una expresión regular ni comprobar que el texto empiece con `SELECT`. Se rechazan escritura, DDL, `COPY`, locks, comandos múltiples y funciones con efectos. Los valores variables se enlazan como parámetros; el preview y la ejecución aplican cancelación, timeout, presupuesto y topes de filas y bytes. La conexión no usa el login de EF ni privilegios de owner/superusuario; un `search_path` fijo y la revisión de permisos sobre funciones y vistas complementan el modo de sólo lectura.

El guardado requiere validar, ejecutar un preview acotado con esa credencial y aprobar una revisión identificada por hash. Después de cada migración, las consultas guardadas se revalidan; una inválida queda indisponible con un motivo estable y no se ejecuta por omisión. El SQL, los parámetros sensibles y los resultados no se registran ni se devuelven en errores. Un LLM podrá proponer configuración en una etapa posterior, pero nunca ejecutarla ni aprobarla.

## Consecuencias

Application mantiene el caso de uso y el contrato del puerto sin EF, Npgsql ni acceso directo a la base; Infrastructure contiene el parser, la conexión restringida y el lector. K5 agrega dependencia de PostgreSQL, provisión y rotación de credenciales, mantenimiento del inventario de vistas y revisión de seguridad del parser. Antes de implementarlo habrá que elegir parser, lista concreta de funciones/operadores, permiso específico, política de contexto para preview, provisión del rol y bloqueo del despliegue ante catálogos inválidos.

Las pruebas deberán usar el rol restringido real y cubrir accesos ajenos, funciones y escrituras, múltiples sentencias, parámetros enlazados/inyección, cancelación y timeout, límites, migraciones que rompen una vista y propuestas no aprobadas. La revisión de arquitectura debe asegurar que ningún otro servicio obtenga una ruta directa a tablas o SQL configurable.

## Alternativas descartadas

- **Postergar Query y entregar sólo Manual/Filters:** es el límite más simple y seguro, pero no cubre el origen `Query` acordado; sirve como secuencia mientras este ADR siga propuesto.
- **Catálogos tipados por proveedor:** reduce la superficie de consulta, pero ata cada origen nuevo a cambios de código y no ofrece la expresividad acordada para Query.
- **SQL libre con un login de sólo lectura:** se descarta porque `SELECT` y el modo de sólo lectura por sí solos no restringen vistas, funciones, catálogos internos ni consumo de recursos.
