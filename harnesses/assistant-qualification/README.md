# Calificación del asistente

Arnés manual fuera de la solución. Sin `--live` sólo enumera fixtures públicos; `--self-test` comprueba el recolector de evidencia sin servicios ni claves.

La ejecución real usa los servicios públicos y el worker existente, datos ficticios en una base separada y el lector real de la conexión OpenAI en una transacción de sólo lectura. No revela ni copia la clave a la base de evaluación. El ledger fija el UUID antes de aceptar: `--resume` consulta las operaciones existentes y nunca regenera una llamada incierta.

Actualmente están rebasados los veinte casos de Inicio y sus cinco capacidades; los casos de WhatsApp, catálogos, faltantes y audio se incorporan al cerrar sus consumidores. Un candidato no habilita un perfil de producción. La declaración requiere la calificación real completa según [la guía](../../docs/guides/configurar-ia.md#habilitar-el-asistente).

Comprobaciones locales:

    dotnet build harnesses/assistant-qualification/AssistantQualification.csproj
    dotnet run --project harnesses/assistant-qualification/AssistantQualification.csproj --no-build -- --self-test
    dotnet run --project harnesses/assistant-qualification/AssistantQualification.csproj --no-build -- --list

Para `--live`, la entrada exige RunId, repositorio, destino y case-set; las conexiones de origen, evaluación y Redis vienen de QUALIFICATION_SOURCE_APPDB, QUALIFICATION_EVALUATION_APPDB y QUALIFICATION_CACHE. No se pasan ni muestran como argumentos. La base de evaluación vacía debe llamarse assistant_qualification_<RunId> y diferir del origen. Sólo ella se migra.

La evidencia permite únicamente stamps, hashes públicos, contadores, tamaños medidos, duración, uso informado y checks; nunca serializa una observación ni un resultado del proveedor. Meta, correo y publicaciones quedan deshabilitados en el host ficticio.
