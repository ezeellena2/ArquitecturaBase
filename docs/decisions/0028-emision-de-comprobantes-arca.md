# ADR 0028: emisión de comprobantes en ARCA: cola, numeración y reconciliación

## Estado

**Propuesta**, 2026-11-09, con el [diseño de la etapa 3 de ARCA](../plans/2026-11-09-arca-etapa-3-factura-c.md). Pasa a
aceptada con el visto bueno del usuario a ese diseño. Complementa el [ADR 0023](0023-arca-como-modulo-opcional.md) (módulo y
credenciales) y el [ADR 0025](0025-familias-de-servicios-arca.md) (familias), y aplica el
[ADR 0018](0018-robustez-proporcional-al-efecto.md) a un efecto con valor legal.

## Contexto

Emitir una factura en ARCA no se puede deshacer: un comprobante autorizado sólo se compensa con una nota de crédito. WSFEv1
exige que cada número sea el último autorizado más 1 para la misma CUIT, punto de venta y tipo (validación 10016), y su
manual (4.7) trata la respuesta que no llega con `FECompConsultar`: reenviar un comprobante ya autorizado da un error de
consecutividad, y el campo `Reproceso` «no es operativo». Hay errores de infraestructura (500, 501, 502 «Transacción
activa») que no dicen si se emitió.

El motor de acciones de formularios ya reserva con lease y fence, ejecuta el efecto fuera de la transacción y marca
`Uncertain` lo que no se confirmó. Pero cada run es independiente: no ordena runs entre sí, no tiene espera creciente y un
`Uncertain` no vuelve solo. La numeración, en cambio, es un estado compartido por todas las emisiones de una secuencia, que
tiene que sobrevivir a reinicios, a varios hosts y a una persona que anula o corrige mientras ARCA no responde. Además, la
nota de crédito de «Anular» no sale de una regla.

## Decisión

1. **La emisión es una cola de la familia `Invoicing`.** Cada pedido es un comprobante (`ArcaVoucher`) con su clave de pedido
   única, los datos fijados, el número, el hash de lo que se manda y su estado (`Queued`, `Sending`, `Authorized`,
   `Rejected`, `Uncertain`, `NeedsReview`, `Cancelled`). La procesa un worker del módulo, no la acción de formularios.
2. **Formularios la usa por un puerto neutral,** `IStructuredRecordIssuer` en `Application/Interfaces/Channels/`: encolar con
   el run como clave, consultar (también por lote), revisar, cancelar mientras no hay número y una vista previa del tipo y el
   total. Es una colección opcional, como el canal de acciones: sin emisor, la acción no se ofrece. La colección vacía hace de
   versión apagada, en lugar de un adaptador apagado aparte (el ADR 0023 pedía uno). El puerto no transporta tipos de ARCA.
3. **Un candado por secuencia** (CUIT, punto de venta y tipo): una fila con una reserva corta (dueño, vencimiento y fence) y el
   comprobante pendiente, el que tiene número sin resolver. El worker toma una secuencia por vez y procesa su comprobante más
   viejo sin resolver; nadie toma otro número mientras haya un pendiente. La reserva se toma y se confirma en transacciones
   cortas; los pedidos a ARCA van fuera de toda transacción, como el ticket del ADR 0023.
4. **El número y el hash se guardan antes de `FECAESolicitar`,** en una transacción que exige el fence propio. El número
   funciona como clave de idempotencia natural.
5. **Reconciliar antes de reintentar.** Ante un 10016, un corte, una respuesta que no llega, un 500, un 501 o un 502 se consulta
   `FECompConsultar`: si existe y coincide, queda autorizado con el CAE consultado; si no existe y el último autorizado es
   menor, se reintenta con el mismo número; cualquier otro caso (existe con otros datos, o falta y el último es igual o
   mayor) va a revisión humana y no se reintenta solo. Nunca se reemite con otro número sin una persona.
6. **Espera creciente en la cola,** sin tope de intentos: el límite es la ventana de `CbteFch` (5 días con productos, 10 con
   servicios, en hora de la Argentina). Una emisión que ya no entra en la ventana va a revisión humana.
7. **El servidor recalcula los importes** con half-even (`MidpointRounding.ToEven`) y decide la clase del comprobante con una
   tabla de datos (emisor y receptor → clase, clase y tipo → código). Una diferencia con lo que calculó el formulario no se
   encola.
8. **El PDF fiscal es del módulo:** diseño fijo con QR, generado al tener el CAE, guardado una vez en el almacén privado con su
   prefijo y su fuente de retención. Formularios lo adjunta por su clave, como el PDF de sus acciones. Una falla del almacén
   deja el comprobante autorizado con el PDF pendiente: nunca provoca otra emisión.

## Consecuencias

- **Varios hosts:** la reserva de la secuencia y el fence evitan dos emisiones de la misma secuencia a la vez; el pendiente
  evita que un host tome el número de una emisión dudosa de otro que se cayó. Se prueba con un ARCA simulado y dos workers en
  paralelo.
- **Una secuencia frenada:** un pendiente en revisión humana frena las emisiones de esa secuencia (las demás siguen). Es el
  precio de no saltear ni duplicar números; la revisión es visible en el detalle de la respuesta.
- **El motor de formularios** suma un tipo de acción que espera a otro (`Running` mirando el emisor), con la espera que el
  emisor sugiere en lugar de los 5 s fijos.
- **Etapa 4:** A y B usan la misma cola y la misma tabla de clases; sólo cambian los importes (IVA por alícuota) y la tabla.
- **Delegación (etapas 5 y 6):** la CUIT de la secuencia es la representada; la cola no cambia.
- **Multitenant:** la cola y las secuencias pasan a ser por organización.
- **Quitar ARCA o la familia `Invoicing`** borra la cola. Con comprobantes autorizados, la guía avisa que los PDF fiscales
  quedan en el almacén y que la base de ARCA sigue siendo la fuente.

## Alternativas descartadas

- **El efecto dentro de la acción de formularios,** con el run como única operación durable: el run no puede ordenar ni
  frenar a los demás runs de la misma secuencia, no tiene espera creciente y la nota de crédito de «Anular» no es una regla.
- **Reservar el número con `FECompUltimoAutorizado` sin guardar el pendiente:** si la emisión anterior no llegó a ARCA, otra
  toma el mismo número y la reconciliación encuentra un conflicto que no existía.
- **Un lease en Redis para la numeración:** se puede perder, y el pendiente tiene que sobrevivir a reinicios y a la pérdida
  del caché ([ADR 0009](0009-redis-como-unico-cache-de-datos.md)).
- **Lotes de varios comprobantes por pedido:** un rechazo deja sin procesar a los siguientes por correlatividad; uno por
  pedido es más simple de reconciliar.
- **CAEA:** desde la RG 5782 es un régimen de contingencia y en WSFEv1 sirve sólo para A y B.
- **Reintentar a ciegas con el mismo pedido:** `Reproceso` no es operativo; un reenvío de algo ya emitido sólo da 10016.
