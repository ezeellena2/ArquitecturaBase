# Traspaso 14 · 04 · ARCA

Cubre lo programado desde el traspaso 13 (backend `23797fff..HEAD`, front `7b95095b..HEAD`): los arreglos de la revisión de arquitectura de ARCA. El 14/11 se revisó el
módulo con 30 criterios (hallazgos B1 a B5 y H1 a H37) y el usuario tomó sus ocho decisiones (`e7373fc4`). Del 14 al 15/11 se programaron 34 tareas en ocho etapas.
Quedaron afuera la medición de memoria y CPU de H22 y el resto de gran escala (más puntos de venta o lotes en H33, H35 a H37), que espera volumen real. H11 no se programó
acá: lo resolvió Formularios (F2, ver el 06). Todo está commiteado, pero **no pasó la prueba integral, la homologación con ARCA real ni la corrida real del robot**. Lo que
ARCA devuelva de verdad puede corregir lo que hoy sale del manual.

Fuentes: el plan [`2026-10-31-arca-formularios-estandar.md`](../2026-10-31-arca-formularios-estandar.md#arreglos-de-la-revisión-de-arquitectura) (sección «Arreglos de la
revisión de arquitectura»); la [revisión](../../reviews/2026-11-14-arquitectura-arca.md) y sus
[decisiones](../../reviews/2026-11-14-arquitectura-arca.md#decisiones-del-2026-11-14); el [ADR 0031](../../decisions/0031-fecha-rechazada-y-numero-adelantado-arca.md)
(nuevo) y los cambios al 0028; la regla en `docs/features/arca.md` y las guías `configurar-arca.md`, `robot-arca.md` y `quitar-arca.md`. Las tareas se citan como «tarea
N» del plan. Las reglas de las etapas 3 a 6 del traspaso 13 siguen vigentes salvo donde se indica.

## 1. Lo que traba la emisión (etapa 1; ADR 0031)

- **B1. La factura A o B con precios sin IVA se emite.** El total que manda el formulario es la suma de los importes de los renglones, en cualquier clase; no se compara
  con neto más IVA. Sin «El precio incluye IVA», el total de la factura es mayor que el del formulario y sale en el resultado. Antes esas facturas no se encolaban nunca.
  Commit `829ed447` (tarea 1).
- **H1. Una fecha que ARCA no acepta va a revisión sin el número.** ARCA responde el mismo 10016 cuando el número ya existe y cuando la fecha es anterior a la del último
  comprobante o está fuera de la ventana. Antes se reenviaba para siempre y la secuencia quedaba frenada. Ahora, si ARCA no tiene el número y el último autorizado es
  menor, se consulta la fecha de ese último. Si la nuestra es anterior o salió de la ventana, el comprobante va a revisión, **libera el número** y la secuencia sigue. Un
  10016 solo no alcanza para soltar el número: puede ser nuestro y estar autorizado tarde, y soltarlo así facturaría dos veces. Decisión 2 del usuario; reemplaza la
  decisión 5 del ADR 0028 (tarea 2).
- **B2. «Emitir con la fecha de hoy» corre el vencimiento.** La revisión ofrece esa opción (o «No emitir») con la mayor entre hoy y la última fecha autorizada que la base
  conoce de la secuencia. Al elegirla se reescriben en el pedido guardado la fecha y un vencimiento del pago ya pasado, y el texto de la opción lo dice. Nunca se corrige
  la fecha sola: es un dato legal y lo decide una persona. Los motivos son «anterior a la del último comprobante» y «fuera de la ventana».

## 2. Lo trivial (etapa 2)

- **H10.** Si el padrón rechaza al tocar «Emitir», la acción queda fallida con «Reintentar», porque no se encoló nada. Un pedido mal armado sigue sin reintento.
- **H12.** Un corte a mitad de la respuesta de ARCA es «ARCA no respondió», no un error propio.
- **H16.** Anular mientras la cola espera no la frena: suelta sólo la reserva de la secuencia.
- **H33.** La cola no hace su pausa de 2 s cuando la vuelta emitió algo (pidió un CAE o guardó un PDF). Con una revisión abierta sigue pausando.
- **H23 y H32. Configuración validada y documentada.** Al arrancar se validan las reservas contra los plazos: la del ticket mayor que un pedido, la de la cola cubre una
  vuelta que concilia (la de fábrica pasó a **3 minutos**) y la del robot, al menos cuatro `StepTimeout`. Las guías escriben las cuentas, la rotación del anillo de
  claves, y que los límites de pedidos y el cupo del padrón son **de cada copia** de la Api.
- **H3 y H24. Los PDF fiscales no se borran solos.** La retención guarda todo `arca-vouchers/` sin mirar la base. La guía de restauración suma un apartado de ARCA:
  levantar la copia con la cola y las acciones apagadas, comparar el último autorizado de cada punto de venta con la base y recuperar del servidor original lo que falte.
  «Quitar ARCA» pide copiar `arca-vouchers/` antes de desplegar y corrige «siete tablas» por ocho.
- **Commit:** `ba9dba84` (tareas 3 a 8).

## 3. Quién pone a emitir y qué se emite (etapa 3)

- **H6. Poner a emitir pide dos permisos** (decisión 3). Agregar de la biblioteca un estándar con reglas que emiten pide `formrules.manage` y `arca.issue`; y todo
  guardado de reglas que deje una acción «Emitir» suma el permiso del emisor: en una copia de «Factura», cambiar cualquier regla pide `arca.issue`. Formularios no nombra
  a ARCA: el permiso lo declara el emisor. Cargar la «Factura» sigue sin pedir `arca.issue`, así la carga por WhatsApp no cambia, y sacar la regla que emite no pide el
  permiso. El 403 usa un código propio (`Forms.Rule.IssuerPermissionRequired`): el editor de reglas toma cualquier otro 403 como «perdiste el permiso de reglas» y se
  cerraba sin lo editado. Commits `78dc2114` (front `018caf37`) (tarea 9).
- **H2. La cola manda lo que se fijó al encolar.** Al encolar se fija el pedido ya armado (importes, alícuotas, fechas, códigos) con una versión y una huella; el worker
  sólo pone el número. Si la huella no coincide, va a revisión con el motivo «datos cambiados» y **conserva el número**, porque ARCA puede tenerlo. Una versión que el
  worker no entiende espera (otra versión de la app); la respuesta se muestra sin receptor, y revisar o anular responden conflicto. La nota de crédito fija su pedido
  desde el de la factura. Commit `e6974cf3` (tarea 10).
- **H9. Los importes tienen tope** antes de encolar: 9.999.999.999.999,99 para el total y cada renglón, y también para cantidad y precio, con un código propio, en la
  vista previa y al encolar (tarea 11).
- **H27 y H34. Dos índices:** una sola nota de crédito activa por factura, y los autorizados sin PDF (tarea 12). Commit `960d1c48`.

## 4. Restauraciones, esperas y PDF (etapa 4)

- **H3. Lo que ARCA tiene y la base no conoce va a revisión** (decisión 4; ADR 0031, decisión 2). Si el último autorizado de ARCA es mayor que el último número que la
  base conoce de la secuencia, el comprobante va a revisión con un motivo propio y un texto que pide mirar en ARCA si esa venta ya se facturó. Excepción: la base no
  conoce ningún número de la secuencia (base nueva). La revisión guarda el último autorizado que vio y emitir vale para ése; si ARCA avanza otra vez, vuelve a revisión.
  Cubre una restauración y otro sistema que factura en el mismo punto de venta. **Costo aceptado:** con otro sistema en ese punto de venta, cada factura pide una
  decisión; en los modos 1 y 2 el punto de venta lo elige la persona y la guía pide uno propio. Commit `6ee20368` (tarea 13).
- **H7. La espera dice de quién es.** Se separan ARCA, la conexión y otra versión de la app, cada una con su aviso. Con la facturación instalada, la Api no arranca sin
  almacén de archivos. Una factura autorizada cuyo PDF falló se informa **emitida**, con número y CAE y sin documento, y se puede anular; antes decía «ARCA no responde».
  Commit `27469add` (tarea 14).
- **H17.** Un PDF que no se puede armar (por ejemplo, una alícuota desconocida) se pospone con su propio código y no frena a los demás. Commit `8ac6588b` (tarea 15).

## 5. La conexión y el ticket (etapa 5)

- **B4. El estado sigue al certificado y a los servicios** (decisión 5). Un certificado vencido pasa a «Requiere atención» (antes volvía a «Sin verificar», contra la
  regla del alta) y la conexión corta con `CertificateExpired` antes de llamar a ARCA. **Sumar un servicio a una conexión activa la pasa a «Requiere atención»** hasta que
  una prueba dé verde; no prueba sola, porque antes hay que autorizar el servicio en ARCA. Cambia la frase «la pantalla vuelve a probar». Commit `adaf6d6b` (front
  `d518f018`, tarea 16).
- **H4.** Las tablas de ARCA (catálogos) no piden un ticket que no sirve: sin Facturación elegida o con la conexión inactiva, el worker no llama al WSAA. Sumar un
  servicio frena la lectura hasta otra prueba en verde. Commit `0b42d899`.
- **H8.** Cortar el pedido de un ticket no lo pierde: el pedido usa su propio plazo, el guardado no se cancela, y el ticket guardado se entrega mientras no venza, también
  si la renovación no respondió. Commit `945513cf`.
- **H15. Los guardados confirman la conexión que se leyó.** Una pantalla vieja no escribe sobre una conexión empezada después. La prueba y el punto de venta dan 409 si
  cambió la conexión o sus servicios, y el estado se recalcula en el mismo guardado. Commit `5537d08b`.
- **H30. Cambiar CUIT, ambiente o modo es un solo pedido** que reemplaza la conexión entera en una transacción (antes eran tres y, si fallaba el segundo, quedaba la
  conexión vieja). La zona de puntos de venta muestra el detalle de un error `Arca.*`. Commit `e9a2e280` (front `80281db9`, tareas 17 a 20).

## 6. El padrón y los clientes (etapa 6)

- **B3. Una constancia con errores no decide la clase.** Si el grupo que decide la condición frente al IVA trae errores, no se infiere: responde un código propio y pide
  revisar, y esa condición no llega a «Clientes». Commit `e283a29f`.
- **H13 y H14. El padrón dice por qué falló.** «No existe» se reconoce por el texto de la falla; las demás son «ARCA rechazó el pedido» o «ARCA no respondió». Un corte de
  la espera de la caché también es «ARCA no respondió». Commit `66ee7c63`.
- **H25 y H31. Datos personales y «Clientes»** (decisión 6). Los comprobantes, su pedido y su PDF se conservan durante el plazo fiscal, que fija el negocio con su
  contador, y un pedido de borrado no los alcanza dentro de ese plazo; la purga al vencer queda para después. **El cliente escrito por su CUIT se suma a «Clientes» recién
  con la factura emitida**, sin domicilio si es persona humana, también si la acción se retoma tras un corte. Commit `fc24b0d3`.
- **B5. «Factura» por WhatsApp con más de 200 clientes.** La pregunta Cliente elige primero por **«Inicial»** (LD1-A, que WhatsApp manda desde r11): un campo de texto
  nuevo de «Clientes» con la primera letra del nombre, sin tilde salvo la Ñ. Lo completan el emisor y «Completar con ARCA», se corrige a mano, y «Consumidor final» y
  «Otro» llevan la suya. **El tope es 200 clientes por inicial.** Va con la versión 2 del estándar y cuesta el prellenado del cliente por WhatsApp. Un «Clientes» creado
  por la versión 1 no tiene «Inicial» y frena «Agregar» (cambio de la biblioteca de formularios); una copia vieja sigue andando. Se descartó escribir la CUIT por
  WhatsApp, que pediría un mecanismo nuevo en los Flows. Commit `936b3b0c`.

## 7. El robot (etapa 7)

- **H5. La clave rechazada de la plataforma no se vuelve a probar sola** (cumple la decisión 3 del ADR 0026). Un lado de la plataforma frenado por ingreso rechazado no se
  rearma al tocar «Delegar con el robot», ni al terminar a mano, ni al arrancar: sólo lo rearma quien tiene `arca.manage`, después de actualizar la clave, con el botón
  «Volver a probar la clave de la plataforma». La clave es una sola para todas las instalaciones: un reintento automático en cada una podría bloquearla. Commit `237ea6df`
  (front `ba0761e4`).
- **H18. El robot distingue «ARCA no responde» de una pantalla cambiada.** Un plazo vencido de navegación, la red caída o un 5xx son «ARCA no responde»; «pantalla
  cambiada» es sólo una página cargada a la que le falta lo esperado. Commit `3c0b4d3a`.
- **H19. La plataforma avisa cuando se rinde** (decisión 8). Cuando su lado queda frenado para siempre (agotados los intentos, por ARCA sin responder o una pasada
  interrumpida) sale el correo de RB4-A; también avisa el certificado que falta. La pantalla promete el aviso sólo cuando salió. Commit `f4e5a372` (front `341cea32`).
- **H20. La reserva acompaña al paso:** un latido la renueva mientras corre un paso, y la del cliente al sacarlo de la cola, así otro host no toma un paso largo. Commit
  `8749f436`.
- **H21. La clave fiscal vive lo menos posible.** Se entrega una sola vez y el contenedor queda vacío; la Api no arranca con un `DEBUG` que tenga `*` o `pw`. Commit
  `51bfc96a`.
- **H22.** El driver de Playwright que se cae vuelve en la pasada siguiente, y el chequeo de arranque abre y cierra Chromium. La memoria y la CPU **quedan sin medir**
  (sección 10). Commit `e322d9b5`.
- **O10. El robot da de alta un punto de venta propio** (decisión 4) y deja de usar «el que ya hay»; guarda en la corrida el número antes de confirmarlo, así repetir la
  corrida no da de alta otro. Espera la tabla después del clic (O9). Commit `02029be2`.

## 8. Lo que se ve y lo que se sigue (etapa 8)

- **H26. Una emisión se sigue de punta a punta.** Los logs de la cola llevan el id del comprobante, su clave de pedido y el código de la falla, también donde antes
  posponía callada. El certificado y la configuración de Facturación son auditables, y desconectar deja un log con quién lo hizo. Hay una métrica de la cola: pendientes,
  los que esperan revisión y la antigüedad del más viejo. Commits `5a9ffb1f`, `3ac32991`, `3cd27c01` y `c6890b3f`.
- **H28. Los nombres fiscales van en español en todos lados** (decisión 7): comprobante, documento y condición, también en la vista previa. Commit `74d86581`.
- **H29. El total de la factura es un importe con dos decimales fijos,** con la región de cada idioma, en el front y en `{result:arca.invoice.total}`. Commits `e3e582c4`
  (front `57021f72`).
- **H11** (avisar a quien cargó si la emisión falla): resuelto en Formularios como F2, `63edb23e` y `f4041e4e`; vale para toda acción y no sólo ARCA.

## 9. Contratos HTTP que cambiaron

El front del multitenant los consume. Todos son aditivos salvo el reemplazo de la conexión.

- **`PUT api/arca/connection`** (`arca.manage`): recibe los datos del alta y la revisión leída, y reemplaza la conexión con todo lo suyo en un guardado. Sin conexión
  responde `Arca.Connection.NotConnected`; con una revisión vieja, 409. Sustituye el encadenado desconectar, pedir certificado y elegir servicios.
- **`POST api/arca/robot-runs/current/platform-rearm`** (`arca.manage`): responde 202 con la corrida; error propio `RobotPlatformNotRejected` si la plataforma no está
  frenada por una clave rechazada.
- **Corrida del robot:** cada lado trae `alerted`, que dice si el aviso salió. La pantalla no promete el correo sin él.
- **Total del resultado:** el tipo del dato es `amount` y ya no `number`. Los resultados guardados con `number` se leen igual, y el PDF y el Excel de la respuesta también
  lo conocen.
- **Resultado de la emisión:** una factura autorizada sin PDF viene emitida, con número y CAE y sin documento.
- **Errores nuevos** con código estable: `Forms.Rule.IssuerPermissionRequired` (403 al guardar reglas), `Forms.Action.IssuerUnavailable`, el tope de importes,
  `CertificateExpired`, el de la constancia con errores y los de espera por almacén o conexión.
- **Catálogo «Clientes»:** campo de texto nuevo «Inicial» y estándar «Factura» en su versión 2.

## 10. Lo que falta

- **La prueba integral y la corrida completa,** una sola vez, al cerrar los desarrollos en curso.
- **Homologar con ARCA real** (CUIT del usuario). Confirma o corrige lo que hoy sale del manual: el 10016 por fecha, los errores de la constancia y la ventana del WSAA.
- **La corrida real del robot** y la medición de memoria y CPU de una pasada en la imagen publicada (H22): si los dos lados juntos no entran en el despliegue, corre un
  lado por vez por host.
- **Límites conocidos:** tras un corte largo, la espera por un 10016 sigue la espera acumulada (hasta una hora) con la secuencia frenada; un servicio con fecha futura en
  la misma secuencia que productos puede dejar una venta sin fecha posible hasta que pase el tiempo; lo encolado antes de H2 no tiene versión y espera.

## 11. Para el multitenant

- **Dónde cae.**
  - La **cola, las secuencias y la numeración** son por CUIT emisora, punto de venta y tipo: en tu modelo, por **empresa**, no por organización. Un H3 (último autorizado
    de ARCA contra la base) se evalúa por empresa.
  - **La conexión** (certificado, ticket, servicios, punto de venta, estado) es por empresa si cada una tiene CUIT; si no, por organización. El reemplazo en un pedido
    (H30) y la regla «sumar un servicio pasa a Requiere atención» valen por conexión.
  - **De plataforma:** la clave fiscal del robot y su rearme (H5), el correo de aviso y la cuenta única de plataforma (ADR 0027). Rearmar la clave de la plataforma
    debería pedir un permiso de plataforma, no `arca.manage` de una organización.
  - **Comprobantes y secuencias** tienen valor legal y plazo fiscal: no los borra un pedido de borrado ni quitar el módulo.
- **Qué adaptar.**
  - La huella, la versión del pedido fijado y las revisiones por fecha o por número adelantado se tienen que aislar por RLS con la empresa.
  - El latido y la reserva del robot, y las validaciones de arranque (almacén, Chromium, `DEBUG`), son de la instalación.
  - Los límites de pedidos y el cupo del padrón son de cada copia: con varias réplicas hace falta un límite compartido.
  - «Inicial», el tope de 200 por inicial y H6 viven en tu biblioteca de estándares y en tu modelo de permisos.
  - La métrica de la cola se etiqueta por empresa.
- **Preguntas para el usuario de allá** (con la opción de ArquitecturaBase recomendada):
  1. ¿La conexión es por empresa o por organización? Recomendada: por empresa, porque la secuencia y el CUIT son suyos.
  2. ¿Quién rearma la clave rechazada de la plataforma? Recomendada: un permiso de plataforma, porque la clave es una sola para todas las organizaciones.
  3. ¿Otro sistema en el mismo punto de venta pide una decisión por factura (H3)? Recomendada: sí, con punto de venta propio.
  4. ¿Poner a emitir pide los dos permisos (H6)? Recomendada: sí, y cargar la «Factura» sigue sin pedir `arca.issue`.
  5. ¿Elegir cliente por «Inicial» con tope de 200 por inicial, o escribir la CUIT por WhatsApp? Recomendada: la inicial, que no pide un mecanismo nuevo.


