# Revisar y validar un cambio

Esta rutina vale para la plantilla, sus proyectos derivados y ambos repositorios. Complementa
la [arquitectura del backend](../architecture/backend.md), la
[del front](../../../ArquitecturaBaseFront/docs/architecture.md) y las reglas del área afectada.
Una suite verde respalda las garantías que comprueba; la coherencia y la necesidad del diseño
también requieren revisar el código.

## Antes de programar

1. Leé los `AGENTS.md` de todos los ámbitos afectados y sus fuentes canónicas. Revisá el estado
   de Git y delimitá el pedido sin sobrescribir cambios anteriores o de otras sesiones.
2. Buscá el recorrido y las piezas existentes que resuelven una necesidad equivalente. Roles
   es la referencia del backend; `shared`, los layouts y los tokens lo son para las piezas comunes
   del front. Identificá qué se reutiliza y dónde pertenece cada responsabilidad nueva.
3. Una abstracción, dependencia o mecanismo nuevo se justifica por una necesidad actual y un
   consumidor real. No crear infraestructura futura, capas por simetría, copias locales de una
   pieza común ni contratos, permisos o estados del producto por suposición. Las decisiones
   nuevas se explicitan; un cambio de arquitectura requiere ADR y fuentes actualizadas.

## Durante el desarrollo

- Aplicá TDD donde hay lógica: primero un test que falle por el comportamiento faltante,
  después la implementación. Elegí el nivel que observe la garantía, sin reproducir el algoritmo.
- Conservá contratos reales, límites de capas y reglas de permisos, transacciones, concurrencia,
  errores, caché y datos sensibles que correspondan al cambio.
- Cuando una regla común pueda comprobarse automáticamente, agregá o ampliá su control con
  casos permitidos, infracciones deliberadas y revisión de los consumidores productivos. No
  relajar una regla ni agregar una exclusión amplia para hacer pasar una implementación.
- Una pantalla compone las piezas aprobadas y conserva sus estados, idioma y accesibilidad.
  Las pantallas nuevas mantienen su tablero y elección previos; esta rutina no los reemplaza.

## Revisión del cambio

Revisá el diff contra el pedido y las fuentes vigentes. Cada pieza debe tener una responsabilidad
clara en su ubicación y depender sólo de lo permitido. Buscá duplicación, caminos alternativos de
guardado o transporte, complejidad sin consumidor y código que sólo oculta una falla. Conservá los
cambios ajenos al desarrollo. Las decisiones implementadas se consolidan en su fuente y las
instrucciones y el mapa se actualizan cuando cambia una responsabilidad.

La revisión también cubre lo que los tests no demuestran. Por ejemplo, `TransactionBoundaryTests`
controla quién recibe y usa la unidad de trabajo; no cuenta un único límite por cada método de
escritura. Los tests visuales con HTTP simulado no acreditan persistencia ni proveedores reales.

## Verificación y cierre

Ejecutá los checks exigidos por los repositorios afectados: build sin advertencias y `dotnet test`
completo para backend, con integración Docker; `npm run build`, `npm run lint` y `npm run test`
para front. Los cambios visuales también requieren navegador, `npm run test:visual` y revisión de
capturas. Si levantaste Aspire, terminá con `aspire stop`; los servidores de pruebas también deben
cerrarse. Una prueba no ejecutada conserva su estado pendiente.

Los avisos inesperados del arnés se investigan y registran con evidencia. No silenciar consolas,
quitar handlers estrictos, aumentar tolerancias ni actualizar capturas sólo para obtener verde.
Un aviso conocido puede conservar un pendiente con alcance y criterio de cierre; no se describe
esa ejecución como limpia de avisos.

La entrega explica, en proporción al cambio:

- **Cambio y encaje:** comportamiento resultante, responsabilidad y patrón existente utilizado;
  motivo de cualquier pieza o dependencia nueva.
- **Evidencia:** pruebas ejecutadas y resultados, revisión del diff y verificaciones manuales o
  externas realmente realizadas. Distinguir automatizado, revisado y pendiente.
- **Pendientes:** alcance sin verificar y hallazgos conocidos, con fuente y criterio de cierre.

## Clasificar un hallazgo

| Tipo | Cómo reconocerlo | Cómo cerrarlo |
| --- | --- | --- |
| Bug funcional | Un comportamiento o efecto contradice el contrato del producto. | Reproducirlo, agregar la regresión y comprobar la corrección. |
| Desvío de arquitectura | El código viola una frontera o ubicación vigente, aunque funcione. | Corregir su responsabilidad/dependencia y agregar un control cuando sea verificable. |
| Problema del arnés o entorno | Avisos, simulaciones incompletas o cierre de procesos de prueba; la causa debe confirmarse. | Diagnosticar sin alterar producto para compensar el entorno; repetir el escenario y verificar limpieza. |
| Organización del desarrollo | Cambios acumulados o alcance difícil de atribuir. | Revisar por funcionalidad y consolidar en cambios/commits chicos, conservando trabajo previo. |

La clase del hallazgo no indica por sí sola su gravedad ni el esfuerzo. Un aviso de tests no prueba
un bug del producto, y un desvío estructural puede afectar mantenimiento o seguridad según su caso.
Los pendientes se registran en `docs/plans`, separados de las reglas permanentes.
