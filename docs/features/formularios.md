# Formularios: borradores localizados y chequeo local

**Decisión humana D5, 2026-10-10:** BusinessForms no fue commiteado ni desplegado; no hay borradores/comprobantes reales que preservar. Se cambia directamente el contrato a name mapa es/en, sin Asp.Versioning/ADR, nameByCulture ni una variante legada ficticia. Se regenera la inicial AddBusinessFormDrafts no commiteada, sin migración extra ni update de bases locales. [Contrato de etapa1](../plans/formularios-etapa-1-contrato.md) fijan ejemplos y verificaciones. La propuesta técnica previa v1/v2 está reemplazada.

BusinessForms es módulo opcional independiente de IA y WhatsApp. Implementa crear completo, editar, lista/facetas y obtener/recuperar intento propio. Publicación y preview oficial corresponden al siguiente paso. Catálogos de etapa 2, envío y captura permanecen en sus etapas posteriores; la calificación real con Meta está pendiente.

## Persistencia y responsabilidad

BusinessFormDefinition y BusinessFormDraftChange heredan Entity e IAuditable; sólo interceptores completan auditoría. AddBusinessFormDrafts agrega FormDefinitions y FormDraftChanges. NameJson guarda el mapa de nombre autoritativo; Name deriva de su idioma original en el mismo commit y nunca es otro nombre editable. DisplayNameEs/En son columnas calculadas de ese mapa, con Name original como referencia de autoría si falta la traducción; no son fuentes de contenido ni caché.

Cultura, nombre y botón son metadata de la definición, fuera de SchemaJson. SchemaJson contiene format=1 y fields de preguntas únicamente; ledger de identidades retiradas separado. QuestionCount se deriva del snapshot en el mismo commit para SQL. No tablas de traducciones. Fields y versiones se crean al aceptar publicación; no hay registros de captura.

El borrador usa fields; el schema sellado de versión usa questions. Decimal/date/boolean son tipos de pregunta, sin captura. La futura representación decimal es string exacto .NET y la fecha DateOnly.

## Identidad y autoría por idioma

Sólo servidor genera FieldId/ChoiceId v7; key es field_ + FieldId formato N y permanece con la identidad, nunca label. UUID recibido se busca en el borrador de la definición y su padre. LocalId correlaciona intento/mapa protegido, sin entrar en SchemaJson.

Renombrar, traducir y reordenar conserva IDs. Cambio de tipo exige replacesFieldId y otra identidad/configuración. Omitir retira campo/opción; ledger impide resurrección. Opciones no se mueven de padre. Helpers puros de Domain conservan mapas copiados/Frozen y colecciones readOnly; no son codec de persistencia ni autoridad alternativa del borrador productivo.

Name, labels, opciones y submitLabel son mapas es/en en HTTP/Application/persistencia/recibos. Original draft.culture es inmutable y válido; sus textos siempre no vacíos. Secundarios pueden faltar o ser "" al guardar; whitespace secundario se canoniza a "". Nombre se recorta en ambos idiomas. Se preservan vacíos explícitos/ausencias sin inventar traducciones. "" retira deliberadamente una secundaria; omitir una traducción no vacía existente falla TranslationsWouldBeLost. Una clave ya vacía puede omitirse. Sin merge de concurrencia ni editedCulture como autoridad.

Botón omitido al crear tiene default Enviar/Submit sólo original; no rellena la traducción de un botón personalizado. Help input admite strings nullable por cultura: vacío/null explícito retira esa entrada conservando las demás. Canonical help es null si no queda texto, o mapa de strings no vacíos parcial/completo. Omisión accidental de ayuda secundaria existente falla. No inventar ayuda.

Guardar exige original válido y admite cero preguntas. Choice exige ≥1 opción e IDs compartidos; duplicados se rechazan sólo en el original por NFC+Trim+OrdinalIgnoreCase, señalando la segunda fila. No nueva restricción secundaria por inferencia. Claves ajenas es/en, null en nombre/labels/opciones/botón y configuraciones desconocidas se rechazan.

Budgets de autoría revisables e independientes de Meta: cada nombre hasta120 UTF-16; prefijo ASCII normalizado 1..12 y único; labels/opciones/botón hasta160 elementos Unicode y ayuda hasta1000; 40 preguntas/50 opciones; cuerpo512KiB y resultado protegido1MiB. Short/long maxLength default80/600, rango1..10000. No truncado.

## Listado, búsqueda y facetas coherentes

Respuesta name es mapa completo. El SPA muestra la cultura UI; si falta su nombre, usa original como referencia de autoría y puede indicar traducción pendiente. Búsqueda, orden name y facetas usan **esa misma proyección en SQL**, según cultura UI resuelta por Accept-Language o DefaultCulture. No orden/paginación en memoria, N+1 ni búsqueda en otro texto oculto. Prefix sigue searchable; ApplySort desempata por Id y ToPagedResultAsync pagina. Nombre original no es identidad ni permiso; originales en/es funcionan simétricamente.

Esta referencia de autoría del listado/preview no permite fallback de texto en Flow publicado. La cultura futura del empleado procede de cuenta vinculada activa, no UI del operador; envío aún no implementado.

## Un guardado y recuperación exacta

Create/Update validan permisos/entrada y abren una UoW OnSuccess. Dentro: lock actor/UUID, intento previo, lock global de definiciones/prefijos, revisión/identidades/prefijo, definición/ledger y resultado protegido. Helpers no guardan; retención tiene caso de uso técnico propio. Medir contención antes de dividir lock global.

Mismo actor/UUID/cuerpo canónico devuelve snapshot/mapa de su commit con201/200, sin avanzar revisión. Otro kind/definición/cuerpo da RequestConflict. Huella cubre todos los nombres/textos por cultura, vaciados explícitos, IDs/localIds/orden/revisión y defaults; mapas ordenados por clave. Recuperación devuelve el resultado original aunque currentRevision sea mayor; committedAtUtc viene de auditoría confirmada. Exacto se refiere al contenido del snapshot, no bytes HTTP/escape Unicode. 404 en vuelo no demuestra rollback; conservar UUID/cuerpo.

DataProtection usa ArquitecturaBase.BusinessForms.DraftChanges.v1, ligado a fila/actor/UUID/definición/kind; codec cerrado format=1 ahora contiene el contrato localizado directamente. Sin v1/v2 artificial ni reescritura de datos reales. SHA256 normalizado vive sólo en payload protegido; ningún hash privado/cuerpo/resultado en columna/log en claro. Restore incluye anillo protegido y certificados de la plantilla, sin credenciales IA/Meta.

Resultado recuperable30d; luego GET/replay410 antes de abrir payload. Purga seguida100 al guardar y cada15min; tombstone conserva actor/UUID/kind/definición/revisiones/expiración/auditoría contra reutilización. Sin ExecuteUpdate/Delete ni retención nueva por D5.

## HTTP, permisos y composición

Las seis rutas MVC de autoría bajo /api/forms son: GET definitions/facets/detail, POST definitions, PUT definitions/{id}/draft y GET draft-changes/{requestId}. Controllers sólo IFormDraftService; contratos propios mapeados a mano, cerrados y redactados. name es mapa también en fila/list/detail/result; no propiedad nameByCulture. ProblemDetails traducido code/traceId/errors; paths name.es/name.en; 409 conflictos y410 expiración.

Read siempre; Manage adicional al escribir. Recuperación Read + actor propio, sin Manage. Catálogo opcional aporta publish y formrecords.create/read para recorridos, sin nuevos grants/ACL por formulario. Seed Admin/composición/Application/Infrastructure/Api/Program existentes. No versionado ni endpoints nuevos por D5.
