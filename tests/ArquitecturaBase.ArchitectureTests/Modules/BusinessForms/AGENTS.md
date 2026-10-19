# BusinessForms: aporte de arquitectura

Leer [formularios](../../../../docs/features/formularios.md). El aporte de locks declara dueño y prefijo precisos con IModuleArchitectureContribution.
No relajar fronteras, transacciones ni composición. Registrar las capas reales y retirarlas con el módulo.

FormsTransactionContribution aporta también los Type exactos de sus lectores de selección, pedidos faltantes, respuestas, sugerencias, catálogos, filtros, reglas y diseño PDF mediante CapabilityReadPorts. La preparación de cultura común sólo lee reglas/diseño; no agrega permiso para escribir ni transporte. El descubrimiento retira estos puertos al borrar el módulo, sin que otra carpeta los nombre.
