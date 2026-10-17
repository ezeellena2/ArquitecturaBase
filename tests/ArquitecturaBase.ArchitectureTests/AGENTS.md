# ArchitectureTests: reglas ejecutables

Verifica capas, paquetes, ubicación, llamadas, nombres y límites de la arquitectura. Algunas reglas leen ensamblados y otras el código fuente; `Support` contiene mecanismos de inspección.

## Al modificar

- Los locks, excepciones y puntos de entrada de puertos productivos neutrales opcionales se componen con `IModuleArchitectureContribution`; los registros Application con `tests/Shared/ModuleApplicationRegistration`. La reflexión sólo existe en tests. Conservar la retirada de cada contribución al borrar su carpeta (ADR 0013).

- Una funcionalidad nueva conserva las reglas existentes. Un cambio estructural requiere ADR y actualización coherente de arquitectura e instrucciones.
- No agregar exclusiones amplias para un archivo que incumple. Las excepciones deben tener alcance y motivo explícitos.
- Las piezas opcionales tienen sus reglas en `Modules/<Módulo>`; el núcleo no referencia clases concretas del módulo.
- Al modificar una regla, probá que detecte una infracción y permita el patrón autorizado; revisá inventarios que fijan casos conocidos.

## Referencias

- [Arquitectura canónica](../../docs/architecture/backend.md).
- [Decisiones vigentes](../../docs/decisions/README.md).
- [Inspección de llamadas](Support/CallSites.cs).
