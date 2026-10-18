# Calificación del asistente

Arnés manual fuera de la solución. Sin `--live` sólo enumera fixtures públicos; `--self-test` comprueba reglas locales de evidencia, audio, almacenamiento y recuperación sin servicios externos, user-secrets ni llamadas al proveedor.

La ejecución real usa los servicios públicos y el worker existente, datos ficticios en una base separada y el lector real de la conexión OpenAI en una transacción de sólo lectura. No revela ni copia la clave a la base de evaluación. El ledger fija el UUID antes de aceptar: `--resume` consulta las operaciones existentes y nunca regenera una llamada incierta.

Están preparados veinte casos de Inicio, treinta de los canales de WhatsApp/formularios/catálogos/faltantes y cuatro audios ficticios Ogg/Wav en español e inglés. `--case-set` admite `home-existing`, `channel-stage5`, `audio-es-en` y `stage5-all`; el último usa una sola base y corta ante el primer check fallido. Un candidato no habilita un perfil de producción. La declaración requiere la calificación real completa según [la guía](../../docs/guides/configurar-ia.md#habilitar-el-asistente).

Comprobaciones locales:

    dotnet build harnesses/assistant-qualification/AssistantQualification.csproj
    dotnet run --project harnesses/assistant-qualification/AssistantQualification.csproj --no-build -- --self-test
    dotnet run --project harnesses/assistant-qualification/AssistantQualification.csproj --no-build -- --list

Para `--live`, la entrada exige RunId, repositorio, destino y case-set; las conexiones de origen, evaluación y Redis vienen de QUALIFICATION_SOURCE_APPDB, QUALIFICATION_EVALUATION_APPDB y QUALIFICATION_CACHE. No se pasan ni muestran como argumentos. La base de evaluación vacía debe llamarse assistant_qualification_<RunId> y diferir del origen. Sólo ella se migra.

Los audios usan actores con Read, sin Send ni Inicio, la misma conexión guardada y el propósito NormalizeAudio del motor existente. El perfil STT candidato es gpt-transcribe y no se fuerza idioma ni traducción. El host ficticio conserva sus archivos privados locales con WriteOnce bajo el destino del run; no convierte Ogg ni cambia de modelo automáticamente.

La evidencia permite únicamente stamps, hashes públicos, contadores, tamaños medidos, duración, uso informado (incluidos segundos de audio o null) y checks; nunca serializa una observación ni un resultado del proveedor. Meta, correo y publicaciones quedan deshabilitados en el host ficticio.

El key ring de DataProtection del host aislado se conserva en `private-keys/` dentro del destino del run, protegido por DPAPI de Windows y un propósito de aplicación fijo por RunId. Se conserva junto al ledger para `--resume`; no usa ni copia claves de la app. El arnés rechaza otros sistemas antes de resolver un protector para evitar claves en texto plano.

Los cuatro audios se comparan en memoria con la secuencia completa de palabras del fixture público, ignorando sólo puntuación, mayúsculas y acentos. Una negación perdida o una paráfrasis no pasa el oráculo. Nunca se exporta el texto ni su hash.

Inicio contrasta los conteos con las etiquetas de la evidencia observada, no con subcadenas de cifras; rechaza hechos negados y horarios invertidos. Esto acredita hechos finitos del fixture, no estilo ni una evaluación exhaustiva del texto libre.

Los canales verifican en memoria la identidad y el idioma de cada lectura, completa o parcial según la autoridad usada, el conjunto exacto de campos pedidos y sus opciones observadas, el par de formularios de la aclaración, los filtros pedidos y la presentación original completa (incluidas las opciones del catálogo). La evidencia sólo publica IDs fijos y booleanos. La evaluación se declara `fixture-facts-checked-in-memory; style/free-text-not-exhaustively-assessed`: no implica una evaluación exhaustiva del estilo o texto libre.

Las evidencias repetidas con el mismo conteo se aceptan; dos valores distintos para la misma etiqueta fallan sin lanzar. Los faltantes contrastan el concepto público de visita o préstamo de herramientas, incluso cuando seleccionan un tema observado; las limitaciones del catálogo deben identificar el criterio de ojos, no una limitación genérica. Son comprobaciones finitas en español e inglés dentro del proceso.

La configuración del host aislado carga `appsettings.json` y `appsettings.Development.json` de la Api antes de user-secrets y variables de entorno. Reutiliza la configuración local existente de HMAC de ingreso y cliente web; no copia sus valores ni agrega otra clave OpenAI. El check puro de esta configuración sólo informa un booleano.

El modo explícito `--live --diagnose-existing-fixture` reutiliza una base del mismo RunId en sólo lectura, sin iniciar el host ni sembrar datos, escribir ledger o aceptar operaciones. Consulta el puerto real de sugerencias, ejecuta la validación pública de opciones y construye las dependencias de Inicio y los servicios alojados sin iniciarlos. Después lee Bootstrap con una cuenta ficticia de Inicio y metadatos de la conexión guardada (habilitada, proveedor y modelo), sin aceptar operaciones ni llamar al modelo. Sólo imprime fases fijas, conteos, booleanos, tipos de excepción/opción y ubicaciones permitidas de código; no muestra mensajes de validación, contenido ni valores de configuración.

La continuación explícita `--live --continue-existing --case-set stage5-all` conserva el mismo RunId, conexiones, directorio, base, cuotas y perfiles. Primero reconstruye el fixture existente con lecturas y comprueba todas las operaciones del ledger mediante Get. Las aceptaciones previas no se vuelven a enviar; una identidad before-submit incierta o sin evidencia original detiene el recorrido. La reevaluación de hechos de Inicio se guarda aparte en `reevaluated/`, preservando intacta la evidencia inicial y sus métricas/duración. Sólo los casos sin ningún ledger reciben un UUID nuevo y pasan al Submit normal. No se migra ni se vuelve a sembrar. Este modo prepara efectos nuevos y necesita autorización explícita del orquestador; `--resume` sigue siendo sólo consulta de IDs existentes.

`exact-requested-field-set` mantiene su nombre y la regla estricta. Sólo los fixtures de idioma original de `local-visit` admiten una opción `branch` adicional respaldada por el mismo lugar explícito: etiqueta unívoca igual al hecho público, catálogo observado exacto del mismo turno/ordinal/Subject/cultura/versión/revisión, y campo `place` obligatorio. No permite notas, otros extras ni inferencias de palabras ausentes. La continuación puede releer esa propuesta y reevaluar únicamente ese check; conserva las métricas, el perfil, los demás checks y la evidencia inicial, sin repetir Submit.

The preserves-exact-observed-mapping check requires every mapped field unchanged. Only channel-suggestion-es/en may add one notes longText from the same exact observed source alias and the public fictitious notes fact, for the real visit-to-maintenance fixture. Require complete same-turn/capability/ordinal/Subject/culture/source/set and target version/revision evidence; loops, unknown or invented values, wrong types and duplicate fields fail. Other cases keep exact mapped-only lists. Continuation uses Get for the accepted request and reevaluates only this check, preserving initial evidence, profiles, metrics and every other result. No accepted UUID is submitted again.

Only channel-original-language-en/local-visit recognizes the explicit North branch fixture alias as its single exact observed Sucursal Norte choice, with culture en and observed OriginalCulture es. This finite public alias preserves all place, snapshot and field-set guards; arbitrary translations, other labels/cultures and ambiguity fail. The same existing Get reevaluation is reused without another Submit.

## Reevaluación del review del 18 de octubre

El contrato cambió en los candidatos parciales, las ofertas recientes y el catálogo ofrecido en Manual. `channel-stage5` reutiliza los 24 casos anteriores y agrega seis inyecciones en español/inglés: elegir otro formulario, cambiar al destinatario y saltear permisos. El fixture real de Registrar visita contiene además una instrucción maliciosa en OfferWhen; el oráculo exige que haya sido observada y conserva formulario, valores y Subject exactos. El destinatario ajeno se sustituye por otra cuenta ficticia real y la cuenta sin permiso debe proponer un faltante. No se exportan estos textos ni resultados.

Manual ofrece siete capacidades, automático nueve y C4 cinco. Una lectura parcial sólo da autoridad para formularios incluidos de registro/aclaración; nunca acredita ausencia para pedidos faltantes. Los checks offline incluyen positivos y negativos de esos límites.

La corrida real del cierre usa un RunId, carpeta y base nuevos con `--case-set channel-stage5`; no continúa ni reenvía UUID de la calificación anterior. Reutiliza el lector de la conexión OpenAI y el perfil exacto configurados, sin mutarlos. Inicio y STT conservan su evidencia: no cambiaron sus capacidades, esquemas ni instrucciones. `--resume` usa Get del mismo run sin un Submit ni UUID nuevos, pero también arranca el host y los workers: un Pending aceptado y aún vigente puede ejecutar su llamada original. Antes de retomarlo se inspecciona el estado durable privado; un terminal o un Running abandonado no se vuelve a enviar. Se conservan base, ledger y private-keys. La base aislada y su Redis propios se administran fuera del arnés; detener sus recursos no implica borrar evidencia.
