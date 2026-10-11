# ADR 0018: Robustez proporcional al efecto y verificación por paso

## Estado

Aceptada, 2026-10-11, por decisión explícita del usuario.

## Contexto

La Etapa 1 de formularios por WhatsApp llevó cuatro días de las cinco etapas previstas. El código escrito no fue la
causa: unas 6.000 líneas de backend y otras tantas de tests; las 15.000 restantes son migraciones generadas. El costo
vino de tres prácticas que ninguna fuente prohibía:

- **Robustez de efecto externo para guardados internos.** El contrato del borrador exigió un comprobante por intento,
  cifrado y recuperable 30 días, con pruebas de respuesta perdida y de dos hosts con el mismo pedido. Eso se justifica
  cuando hay un efecto fuera del sistema que no se puede repetir (publicar en Meta, mandar un mensaje, cobrar una
  llamada facturable); para guardar un borrador alcanza con no pisar lo que otro guardó.
- **La suite completa en cada corte.** «Antes de dar algo por terminado» se leyó como cada corte: 2.605 tests con
  Docker, unos 22 minutos por corrida, más recorridos de navegador con capturas.
- **Contratos y bitácoras en Markdown.** Un contrato de 1.381 líneas antes del código, revisiones escritas por cada
  corte y un arranque que registraba cada commit. Se escriben, se revisan y se coordinan, y después se desactualizan
  frente al código.

## Decisión

1. **La robustez es proporcional al efecto.** Comprobantes recuperables por `requestId`, estado incierto, leases y
   fences, reconciliación y pruebas de varios hosts se usan solo para operaciones con efecto fuera del sistema o que
   cuestan al repetirse: proveedores externos (Meta, correo, almacenamiento de archivos, LLM), mensajes a personas y
   efectos que una regla dispara. Guardar configuración interna usa la forma común: un límite por método
   ([ADR 0001](0001-transaccion-explicita-por-caso-de-uso.md)), control de revisión y 409 si otro guardó antes.
2. **Verificación por paso.** En cada corte: build y los tests del área afectada (`--filter-class`, o los del proyecto
   tocado; en el front, `tsc`, `lint` y los tests de la feature). Al cerrar un paso o un desarrollo: la suite completa
   con Docker, `npm run build`, `lint` y `test` completos y, si hay cambios visuales, el recorrido en el navegador.
3. **El contrato es el código.** Entre backend y front, el contrato son los DTO, el OpenAPI y los ejemplos de respuesta
   que exporta el backend para el front; no se escriben contratos en Markdown antes del código. Planes, arranques y
   revisiones se actualizan al cerrar cada paso, con un resumen corto: qué quedó, cómo se probó y qué falta.

## Consecuencias

- Lo ya implementado se conserva: la durabilidad del borrador de formularios queda como está; no se retira trabajo
  hecho para cumplir este ADR.
- Un desarrollo que quiera más robustez para algo interno la justifica en el cambio con el riesgo concreto (qué se
  pierde o se duplica y para quién). Sin esa justificación, la revisión la objeta.
- Un corte puede quedar en verde con tests focalizados y romper otra área: lo detecta la suite completa del cierre del
  paso, que sigue siendo obligatoria. Un paso no se da por cerrado sin ella.
- Las decisiones de diseño siguen en sus fuentes canónicas (arquitectura, features, ADR); lo que cambia es cuándo se
  escriben los resúmenes, no si se consolidan.

## Alternativas descartadas

- **Robustez uniforme para toda escritura:** cuesta diseño, tests y correcciones en lugares donde un 409 alcanza.
- **Suite completa en cada corte:** 22 minutos por corrida multiplicados por cada corrección; la seguridad extra frente a
  la suite del cierre del paso no compensa.
- **Contratos en Markdown como fuente:** duplican el código, se desactualizan y no se pueden verificar; los ejemplos
  exportados sí.
