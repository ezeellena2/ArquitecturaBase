# Calificación del asistente

Arnés manual fuera de la solución. Sin `--live` sólo enumera fixtures públicos; `--self-test` comprueba treinta reglas locales de evidencia, audio, almacenamiento y recuperación sin servicios ni claves.

La ejecución real usa los servicios públicos y el worker existente, datos ficticios en una base separada y el lector real de la conexión OpenAI en una transacción de sólo lectura. No revela ni copia la clave a la base de evaluación. El ledger fija el UUID antes de aceptar: `--resume` consulta las operaciones existentes y nunca regenera una llamada incierta.

Están preparados veinte casos de Inicio, veinticuatro de los canales de WhatsApp/formularios/catálogos/faltantes y cuatro audios ficticios Ogg/Wav en español e inglés. `--case-set` admite `home-existing`, `channel-stage5`, `audio-es-en` y `stage5-all`; el último usa una sola base y corta ante el primer check fallido. Un candidato no habilita un perfil de producción. La declaración requiere la calificación real completa según [la guía](../../docs/guides/configurar-ia.md#habilitar-el-asistente).

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

Los canales verifican en memoria la identidad y el idioma de cada lectura completa, el conjunto exacto de campos pedidos y sus opciones observadas, el par de formularios de la aclaración, los filtros pedidos y la presentación original completa (incluidas las opciones del catálogo). La evidencia sólo publica IDs fijos y booleanos. La evaluación se declara `fixture-facts-checked-in-memory; style/free-text-not-exhaustively-assessed`: no implica una evaluación exhaustiva del estilo o texto libre.
