# Formularios por WhatsApp: incorporación de acuerdos — 2026-10-08

**Alcance:** reformulación documental, sin programación, ramas, commits ni push. Complementa la [revisión anterior](2026-10-08-formularios-whatsapp-plan.md), cuya evidencia y propuesta anterior conservan su fecha; no modifica el modelo demo que mantiene Claude ni el diseño del front.

## Fuentes y prioridad

Se leyeron completos los [acuerdos](../plans/formularios-modelo-demo/acuerdos-2026-10-08.md), el [README demo](../plans/formularios-modelo-demo/README.md), [01-esquema.sql](../plans/formularios-modelo-demo/01-esquema.sql) y [05-recorrido-crear-formulario.sql](../plans/formularios-modelo-demo/05-recorrido-crear-formulario.sql). Se contrastó [diseño del front](../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md) y se inspeccionaron las cuatro imágenes nuevas s1–s4. Las doce elegidas estaban revisadas en el registro anterior; las nuevas siguen **en revisión**, sin promoción.

AGENTS/docs, arquitectura backend, reglas de WhatsApp/IA y ADR vigentes prevalecen; después acuerdos; después plan. Demo es apoyo de escrituras, no EF/producto. Sus catálogos anteriores quedan reemplazados por acuerdos §5; no se ejecutaron scripts destructivos ni se alteraron tablas de esa base. Contenido/acuerdos elegidos no se convierten en premisas implementadas.

## Qué cambia en el plan

- Versiones sólo contienen preguntas/validaciones; IDs server dentro de borrador, Fields al publicar, DraftRevision y contador LastRecordNumber. Prefijo sugerido/editable hasta primera publicación, único/no reciclable; elimina F000001.
- RuleSets con revisiones/guardado independiente; un motor ActionRuns con dependencia e idempotencia; Tasks de un nivel y documentos inmutables. Sin emisión especial ni motor PDF aparte.
- Catálogos separados Manual/Filters/Query, primera vista Usuarios, conexión de lectura restringida y lista inicial dentro del mensaje Flow; RecordReferences captura key/revisión/texto histórico. Una gramática de condiciones.
- Sugerencias/orígenes de respuesta y modo Asistente, Origin/Kind/operador opcional en la cola actual. FormAction necesita esa extensión desde Etapa 4, antes del asistente; no se promete un segundo outbox.
- Puertos/consumidores, permisos/rutas/inventario, árbol por etapa, recorridos y tableros pendientes actualizados. Se conserva UI principal/editor sin pestañas/menú Formularios.
- Orden de acuerdos §7 **propuesto para confirmación**: editor/chat → catálogos → planilla/detalle → reglas/ejecuciones/aprobación/PDF/avisos → asistente/sugerencias. Correcciones/intercambio/grupos después.

## Revisión de encaje y decisiones abiertas

[Plan §11](../plans/2026-10-07-formularios-dinamicos-y-whatsapp-flows.md#11-pantallas-elegidas-estados-y-decisiones-pendientes) registra conflictos sin resolverlos por cuenta propia. En particular: excepción SQL requiere ADR propuesto (sin crearlo); nombres SQL españoles/dueño de vista Usuarios y semántica de roles/activo/zona; seguridad/funciones/PUBLIC y snapshots/revalidación de catálogos; momento de bloqueo de prefijo; IDs server al agregar frente a guardado explícito; revisión de reglas en decisión tardía y espera tras pedir aprobación; deduplicación/autoridad de revisión con operador null; toma Manual ante cola pendiente; Always/oferta y botones Meta; historia de aceptación/layout PDF/captura web.

Se revisó cola vigente: unique(ContactId,OperatorUserId,RequestId), operador obligatorio; Bot sigue enum/HTTP sin automatización y cola bloquea cambio de modo. Las propuestas no se presentan como migraciones aprobadas. SQL de configuración no se confunde con una herramienta SQL del asistente ni con consultas libres de respuestas. La lectura PostgreSQL de privilegios/vistas y la consulta oficial Meta quedaron registradas en §5.3/13, con límites de evidencia y piloto pendiente.

## Verificación de esta revisión

Checks del **producto existente**; no verifican implementación de formularios ni validan SQL/protocolo Meta propuestos:

- Backend build --no-restore: correcto, cero advertencias/errores.
- Backend `dotnet test --no-build --no-restore` con Docker PostgreSQL 18.3 y Redis 8.6: 2.340 correctas, cero errores/omitidas; 11 min 25 s total. Domain, Application, Architecture e integración completas.
- Front build: correcto; aviso informativo PLUGIN_TIMINGS, sin error de compilación.
- Front lint: correcto, 48 controles de arquitectura.
- Front test: 96 archivos, 1.014 pruebas correctas.
- Revisión documental de enlaces y del diff contra copia previa del plan (archivo sin versionar): realizada; los nombres reemplazados sólo se conservan para explicar qué se retiró. Whitespace se comprueba directamente además del diff de Git.
- Auditor estructural `-Engine none -Checks structural` (sin agente): no introduce enlaces/anclas rotos; estado general **incomplete** por los mismos cuatro hallazgos ajenos ya registrados en la revisión anterior (dos AGENTS fuera del mapa y dos anclas históricas de Inicio). Informe: `.test-artifacts/harnesses/documentation/2026-10-08T23-39-12.412Z-211c1ecd/report.md`; 349 documentos inventariados. `git diff --check` correcto en ambos repositorios. No acredita auditoría semántica global.

No se implementó ninguna pantalla ni cambiaron estilos: no corresponde actualizar capturas ni ejecutar tests visuales de una UI nueva. No se levantó Aspire; tests usan su arnés Docker. Se verificó el cierre de Testcontainers: sólo quedaron los dos Postgres preexistentes, incluido formularios-demo, sin detenerlos ni modificarlos. No se ejecutaron los scripts de la demo. Pilotos Meta/modelos/SQL restringido, migraciones y TDD específicos de las etapas siguen pendientes.
