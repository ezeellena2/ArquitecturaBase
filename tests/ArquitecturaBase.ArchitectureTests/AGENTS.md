# ArchitectureTests: reglas ejecutables

Verifica capas, paquetes, ubicación, llamadas, nombres y límites de la arquitectura. Algunas reglas leen ensamblados y otras el código fuente; `Support` contiene mecanismos de inspección.

## Al modificar

- Una funcionalidad nueva conserva las reglas existentes. Un cambio estructural requiere ADR y actualización coherente de arquitectura e instrucciones.
- No agregar exclusiones amplias para un archivo que incumple. Las excepciones deben tener alcance y motivo explícitos.
- Las piezas opcionales tienen sus reglas en `Modules/<Módulo>`; el núcleo no referencia clases concretas del módulo.
- Al modificar una regla, probá que detecte una infracción y permita el patrón autorizado; revisá inventarios que fijan casos conocidos.

## Referencias

- [Arquitectura canónica](../../docs/architecture/backend.md).
- [Decisiones vigentes](../../docs/decisions/README.md).
- [Inspección de llamadas](Support/CallSites.cs).
