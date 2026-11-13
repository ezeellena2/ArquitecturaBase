# Revisar y validar un cambio

Esta rutina vale para la plantilla, sus proyectos derivados y ambos repositorios. Complementa
la [arquitectura del backend](../architecture/backend.md), la
[del front](../../../ArquitecturaBaseFront/docs/architecture.md) y las reglas del área afectada.
Una suite verde respalda las garantías que comprueba; la coherencia y la necesidad del diseño
también requieren revisar el código. Para revisar un desarrollo entero contra los 30 criterios de
calidad (que no se mezclen los pedidos, varias copias, volumen, terceros, seguridad, datos, lo que ve
el usuario y operación), seguí [revisar la arquitectura de un desarrollo](revisar-arquitectura.md).

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
cuenta a lo sumo un límite por método público, pero no comprueba que cada método que escribe abra
uno ni ve uno repetido en un bucle. Los tests visuales con HTTP simulado no acreditan persistencia
ni proveedores reales.

### Antes de cerrar: lo que más se repite en las revisiones

Estos errores aparecen una y otra vez en [`docs/reviews/`](../reviews/). Contestá las preguntas que
apliquen al cambio:

1. **Tablero:** ¿comparé la pantalla con su tablero elemento por elemento (textos, estados, vacíos,
   320 px) y anoté cada diferencia aceptada?
2. **Textos:** ¿cada texto que toqué sigue siendo cierto sin datos, con un filtro sin resultados, sin
   permiso y después de un error, y nombra la causa y la salida reales?
3. **Errores en pantalla:** ¿400, 403, 409, red o 5xx y 429 tienen un solo dueño y un solo aviso,
   conservan lo escrito y sólo ofrecen reintentar lo que se puede reintentar? ¿El foco queda en un
   control visible al cerrar, apagar o sacar uno?
4. **Datos al día:** después de cada escritura, ¿se invalidan todas las consultas que muestran el dato,
   también en otras páginas?
5. **Ofertas:** ¿cada botón, ítem u oferta usa la misma condición con la que el servidor la acepta
   (permiso, elegibilidad, tipo)?
6. **Fallas:** ¿cada falla nueva tiene su clase y su test? Una permanente no se reintenta para siempre,
   una transitoria espera, una anterior al envío no queda incierta y una esperable es 4xx, no 500.
7. **Topes y costo:** ¿cada tope sale de una sola constante que respetan todas las capas y se mide
   antes de cargar o de pagarle al proveedor? Con el máximo permitido, ¿cada lectura está acotada en
   SQL, sin una consulta por fila?
8. **Uno malo no frena al resto:** si un elemento es inválido, enorme o falla siempre, ¿se excluye
   sólo ese, con su motivo?
9. **Lo que ya existe:** ¿anda con los datos que ya hay (sellados, dados de baja, eliminados, de otro
   tipo)? ¿Un blanco viaja como ausente o null sin borrar lo guardado, y guardar sin cambios no
   escribe ni sube la revisión?
10. **Tests que ven la falla:** ¿cada test falla si saco el arreglo? ¿Cubre 403, 409, red y el
    máximo, con datos de forma real (y PostgreSQL si hay SQL), y crea y limpia sus propios datos?
11. **El mismo error en otro camino:** ¿lo busqué en los demás caminos y llamadores, y corrí los tests
    de todos los consumidores de lo que cambié?
12. **Contrato y documentos:** ¿cada cuerpo nuevo, también los 4xx, está exportado y el guard estricto
    del front lo acepta tal cual? ¿Cada guía o regla que toqué dice lo que hace el código final? Si
    hay migración, ¿el `Down` no pierde datos y el snapshot quedó sin `\r`?

## Verificación y cierre

En cada corte, alcanza con el build y los tests rápidos del área afectada (en backend, unitarios y de
arquitectura con `--filter-class`, sin los de integración con Docker; en front, `tsc`, `lint` y los tests
de la feature, sin `test:visual`). Al terminar una tarea o una etapa: el build sin advertencias, los
proyectos unitarios y `ArchitectureTests` enteros (hay controles que recorren todos los módulos) y los
tests de integración de lo que cambió, con `--filter-class`; en front, `tsc`, `lint`, los tests del área
y, si cambió una pantalla, sus pruebas visuales.

La corrida completa va una sola vez, al cerrar el desarrollo
([ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md)): build sin advertencias y `dotnet test`
completo para backend, con integración Docker; `npm run build`, `npm run lint` y `npm run test` para
front. Los cambios visuales también requieren navegador, `npm run test:visual` y revisión de capturas.
Cuando varios chats trabajan a la vez, no se corre una por chat ni por etapa: la corre una sola sesión,
con la PC quieta, cuando terminan todos. Lo que se usa desde el celular (cámara, toque, fotos sobre una
red lenta) se prueba además una vez en un teléfono real, con [probar en el celular](probar-en-el-celular.md).
Si levantaste Aspire, terminá con `aspire stop`; los servidores de pruebas también deben cerrarse.

**Si la corrida final falla:** se corrige y se repite solo lo que falló, con `--filter-class` o el
test del front; después, una corrida completa más. Vale la última corrida completa en verde. Las
corridas intermedias no se registran, no se nombran («adicional», «única») ni se discuten entre chats.
Un test que falla solo junto con los demás es un problema de aislamiento de datos del test, no de la app.

Un aviso inesperado del arnés se investiga. No silenciar consolas, quitar handlers estrictos,
aumentar tolerancias ni actualizar capturas solo para obtener verde. Si no se resuelve en el momento,
queda una línea en la deuda del plan.

La entrega explica, en proporción al cambio:

- **Cambio y encaje:** comportamiento resultante, responsabilidad y patrón existente utilizado;
  motivo de cualquier pieza o dependencia nueva.
- **Pruebas:** qué se corrió y su resultado final, y qué se probó a mano o con servicios reales.
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
