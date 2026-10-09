# Revisión del diseño y maqueta de formularios dinámicos

Fecha: 2026-10-07. Alcance: [propuesta](../plans/2026-10-07-formularios-dinamicos-y-whatsapp-flows.md) y prototipo aislado. Sin implementación productiva.

**Procedencia sustituida el 2026-10-08:** el usuario eligió
[Formularios por WhatsApp](../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md)
y sus 12 capturas como fuente de pantallas, navegación, textos y etapas.
Esta revisión conserva evidencia histórica del prototipo del 7/8 de octubre; no es una referencia
vigente de UI y no autoriza promoverlo ni seguir mejorándolo. La arquitectura propuesta sigue
en el plan actualizado; [la revisión del cambio de referencia](2026-10-08-formularios-whatsapp-plan.md)
distingue diseño elegido e implementación pendiente.

## Qué cambió

Se agregó un plan de arquitectura, persistencia, contratos, carpetas, pantallas y etapas; se enlazó en el índice de pendientes. La maqueta está en la carpeta de esta conversación, fuera de los dos repositorios productivos. Se construyó usando AppLayout/Page/Surface/DataTable/Pagination/FormField/Dialog/ConfirmDialog y recursos es/en reales del front, con Campo/Public Sans.

No se agregaron entidades, migraciones, rutas, permisos ni componentes productivos. El repositorio ya contenía cambios de otros trabajos; no se atribuyen a esta entrega ni se revertieron. No hubo commit, rama o push.

## Arquitectura revisada

- Una instalación por negocio; sin TenantId ni conexión por usuario.
- Módulos opcionales independientes con contratos neutrales; ninguna dependencia BusinessForms ↔ WhatsApp ↔ IA propuesta.
- MVC/servicios/puertos/repositorios, mapeo manual y un único límite por método público de escritura. Proveedores y puentes entre módulos fuera del límite del llamador.
- Versiones selladas, identidades estables, registros confirmados inmutables y correcciones explícitas. JSON canónico, proyección tipada reconstruible y vínculos relacionales canónicos, sin tablas físicas por plantilla.
- Consultas SQL antes de contar/paginar; operadores tipados, mismo RowId para condiciones de detalle, facetas separadas y cuerpos privados para filtros sensibles.
- Permisos actuales, vínculo de actor, recibos, fencing/leases, reconciliación de efectos inciertos y snapshots de documentos.
- Result/ProblemDetails, resx/i18next, TimeProvider/UTC/DateOnly, Redis único, logs privados y guías de migración/retirada/verificación preservados como requisitos de implementación.

Esta es una revisión de la propuesta: no demuestra que esas reglas estén ejecutándose en el producto.

## Comprobaciones automáticas del prototipo

- Vite 8.3.0 compiló el bundle aislado usando las dependencias ya instaladas del front; última compilación sin errores reportados.
- TypeScript parser: 0 diagnósticos sintácticos en la revisión del prototipo. No equivale a typecheck/lint completo del front.
- Fragmento final: 695479 bytes, debajo del límite de 1 MB; estilos y fuente incluidos. Sin instalación de paquetes.
- `git diff --check` focalizado sobre los documentos editados, sin errores reportados antes del cierre documental; documentos nuevos revisados de forma directa.

## Comportamiento observado en Chrome

Pruebas manuales con herramientas de navegador, sin servicios de negocio:

| Recorrido | Resultado observado |
| --- | --- |
| Campo incompleto | FormField mostró error inline al intentar guardar sin etiquetas |
| Plantilla desde cero | Registrar mantenimiento con Equipo/equipment conservó borrador y v1 propios; volver a Registrar visita mantuvo sus 3 campos. Después de activar/capturar, el payload incluyó sólo equipment="Compresor", links=[] y rows=[] |
| Decimal requerido | Se agregó Importe/Amount con clave amount, se guardó el borrador y se selló v2 con 4 campos; v1 conservó 3 |
| Activación de prueba | v2 se habilitó sólo para la simulación web, con aviso explícito de que no publica/sincroniza Meta |
| Captura | La revisión mostró v2 fijada; Observaciones e Importe vacíos dieron errores separados; al completar se creó VIS-000024 con amount="125.50" |
| JSON y relación | Detalle mostró versión, valores y link al RecordId ficticio del cliente; no guardó el nombre del cliente como clave relacional |
| Histórico/documento | Después de preparar el documento y renombrar Importe a Monto en el borrador, el detalle/documento siguió mostrando Importe y 125.50 |
| Filtro decimal | Importe >= 100 devolvió únicamente VIS-000024; facetas conservaron el filtro; el valor privado no apareció en el ejemplo de URL |
| Orden y paginación | Orden descendente por número; tamaño 20 y página 2 de 2 mostraron 21–23 de 23 en inglés. Se corrigieron los bindings a sort/onSortChange y hasPrevious/hasNext reales |
| Inglés | Chrome mostró navegación, controles, estados y paginación en inglés; el contenido escrito por usuarios siguió en su idioma |
| Conflicto/incertidumbre | Se pudo releer un conflicto conservando el borrador y consultar el comprobante del resultado incierto; no se hizo una escritura externa |
| Permiso | Se mostró ForbiddenPage compartido con su traducción en inglés |
| Remito | Se abrió el ejemplo de esquema y se agregó una tercera línea; grupo/cantidades/unidades quedaron visibles como simulación web |
| Dossier técnico | Se recorrieron modelo, carpetas y premisas separados de las pantallas de negocio |
| 320/390/1024 px | Sin overflow horizontal del documento en los tamaños observados; editor apilado y controles dentro del ancho. Tablas conservan desplazamiento horizontal interno del componente compartido |
| Preview Meta | El iframe oficial cargó el Flow real Registrar visita en diálogo amplio: Cliente/Fecha/Observaciones y confirmación. No se modificó ni publicó el Flow |

La vista integrada del chat usa una captura real del ejemplo Meta y el enlace oficial: su política de recursos no permite cargar el iframe de Facebook ahí. El iframe se probó en la vista standalone local. El sitio desplegado todavía requiere CSP/frame-src, comprobación de URL/vencimiento y permisos reales. La vista oficial no sigue los cambios locales del prototipo.

Captura del recorrido histórico probado:

![Registro v2 con documento conservado](C:/Users/ezequ/.codex/visualizations/2026/10/05/01a10cf6-23c4-7641-b084-a486091df430/registro-versionado-verificado.png)

![Payload de plantilla creada desde cero](C:/Users/ezequ/.codex/visualizations/2026/10/05/01a10cf6-23c4-7641-b084-a486091df430/plantilla-generica-verificada.png)

## Pendientes y límites

No se ejecutaron dotnet build/test ni las suites npm del producto: esta entrega no implementó código productivo. TDD, tests de arquitectura/integración con PostgreSQL/Redis reales, pruebas de rendimiento, piloto Meta/LLM y pruebas de Azure quedan obligatorias para las etapas del plan.

El prototipo sólo ejecuta en memoria ejemplos de campos/filtros/reglas; no certifica cualquier esquema anidado, todos los operadores, permisos por definición ni stock. Documento es una previsualización, no un PDF emitido. Los escenarios operativos del selector de prueba ilustran estados; no acreditan respuestas HTTP ni reintentos reales.

No se levantó Aspire. El servidor local de revisión se detiene al entregar; la maqueta integrada no depende de él. Quedan abiertas elección del diseño, calificación del detalle de remito en Meta, almacenamiento, grants si corresponden, retención y volumen objetivo. Se mantienen como propuesta en plans; no se promueven a premisas implementadas.

## Aclaración de alcance y maqueta — 2026-10-08

El usuario confirmó una empresa única: administrador del sistema que diseña los formularios y usuarios empleados que los usan según permisos. Se explicitó en el plan, sin cambiar las reglas canónicas ni implementar autorización nueva. Clientes representa datos comerciales opcionales, no empresas usuarias ni particiones por usuario.

La maqueta muestra Administrador en la cabecera, describe esa distribución de tareas en Configuración y llama al filtro Cliente de la visita. Se retiró el bloque redundante de cuenta/número de WhatsApp; la asociación formulario/versión/Flow reutiliza la conexión del módulo existente.

Verificación del prototipo: bundle Vite sin errores (fragmento final 702341 bytes y lectura de la referencia Windows correcta); Chrome confirmó que ya no existen la tarjeta de conexión ni el campo de número, conservó Registrar visita v1 en la asociación, mostró el alcance en español e inglés y no registró errores de consola. Son comprobaciones de presentación, no pruebas de permisos reales, conexión productiva ni captura persistida. No se modificó ni publicó el Flow de Meta. No se ejecutaron suites del producto porque no se implementó código productivo.

## Segunda plantilla editable — 2026-10-08

Se agregó Registrar mantenimiento con cinco campos, reglas, documento y versión propios; doce respuestas ficticias. El catálogo conserva Registrar visita y permite alternar sus borradores. Los filtros rápidos y sus conteos provienen de los campos de opciones/referencias presentes; el buscador usa un campo de texto del esquema y el filtro avanzado selecciona operadores por tipo. Las columnas y filtros del borrador se pueden previsualizar desde Ver cambios en tabla sin activarlo ni modificar registros históricos. Este modo es una demostración local, no una escritura productiva.

Comprobación manual en Chrome:

- Equipo=Compresor y Costo>=50000 devolvieron MNT-000004, MNT-000007 y MNT-000010, con 1–3 de 3. El valor privado no se incluyó en el ejemplo de URL.
- Renombrar Equipo a Máquina cambió columna y filtro; ocultar Costo quitó la columna y conservó el filtro.
- Agregar Sector creó columna y filtro en el borrador. Sin dato devolvió las doce respuestas y Con dato devolvió cero; la vista activa v1 y el detalle histórico conservaron Equipo/Costo y excluyeron Sector.
- La simulación de captura creó MNT-000013 con Generador, Correctivo, fecha 2026-10-08 y costo 19000. Cambiar a visitas conservó sus 23 respuestas y tres campos; volver mostró los 13 mantenimientos de esa sesión.
- En inglés se mostraron Equipment/Date/Work type/Cost/Notes. A 1024 px no hubo overflow del documento: la tabla de 802 px conservó scroll interno en su contenedor de 687 px. Sin errores de consola durante el recorrido.

Vite compiló sin errores; fragmento 722580 bytes, con referencia Windows leída correctamente. No se llamó a Meta, LLM ni PostgreSQL; la plantilla de mantenimiento no está creada en Meta. Las pruebas completas de producto, persistencia, autorización y rendimiento siguen pendientes de implementación.
