# ADR 0031: la fecha que ARCA rechaza y el número que ARCA tiene adelantado van a una persona

## Estado

**Aceptada**, 2026-11-14, por decisión del usuario sobre la
[revisión de arquitectura de ARCA](../reviews/2026-11-14-arquitectura-arca.md#decisiones-del-2026-11-14) (decisiones 2 y 4).
Reemplaza la decisión 5 del [ADR 0028](0028-emision-de-comprobantes-arca.md) en lo que dice del 10016; el resto del ADR 0028
sigue vigente. La decisión 1 rige desde el 2026-11-14; la 2, desde la tarea 13 de los
[arreglos de la revisión](../plans/2026-10-31-arca-formularios-estandar.md#arreglos-de-la-revisión-de-arquitectura).

## Contexto

El ADR 0028 trata el 10016 como un número equivocado: si ARCA no tiene el número y el último autorizado es menor, se reenvía
con el mismo número. Pero WSFEv1 responde el mismo 10016 cuando la fecha es anterior a la del último comprobante del mismo tipo
y punto de venta, o cuando está fuera de la ventana. Una venta cargada con una fecha atrasada, algo común al ponerse al día con
el fin de semana, volvía con 10016 y se reenviaba igual para siempre. Además frenaba la secuencia, sin revisión ni salida desde
la app.

La cola, además, numera con el último autorizado de ARCA más 1 sin compararlo con lo que conoce la base. Después de restaurar la
base, o con otro sistema en el mismo punto de venta, puede facturar dos veces una venta.

## Decisión

1. **Una fecha que ARCA no puede aceptar va a revisión sin el número.** Cuando la consulta dice que ARCA no tiene el número y
   que el último autorizado es menor, antes de reenviarlo se consulta a ARCA la fecha de ese último. Si la nuestra es anterior,
   o salió de la ventana, el comprobante va a revisión sin el número, que queda libre para el siguiente. Si ARCA la acepta, se
   reenvía con el mismo número, como hasta ahora.
   - **El motivo:** «anterior a la del último comprobante», o «fuera de la ventana» si la fecha ya salió de ella.
   - **La revisión:** ofrece «Emitir con la fecha de hoy» o «No emitir». Emitir toma la fecha de hoy o, si el último
     comprobante que conoce la base tiene una fecha posterior, esa. Un vencimiento del pago anterior a esa fecha se corre a
     ella.
   - **Antes de encolar:** «¿La emito?» y «Emitir» avisan si la fecha es anterior a la del último comprobante que conoce la
     base, y no encolan.
2. **Lo que ARCA tiene y la base no conoce va a revisión.** Al numerar, si el último autorizado de ARCA es mayor que el último
   número que la base conoce de la secuencia, el comprobante va a revisión con su motivo. La excepción es una base que no conoce
   ningún número de la secuencia. La revisión guarda el último autorizado que vio, y emitir vale para ése. Lo mismo vale para
   «Emitir con el número siguiente» de un número tomado o que falta.

## Consecuencias

- **La secuencia sigue:** una fecha rechazada ya no la frena, porque el número queda libre y las ventas siguientes salen.
- **El dato legal lo cambia una persona:** corregir la fecha pide su decisión.
- **Una consulta más:** antes de reenviar un número que ARCA no tiene, la cola pide la fecha del último comprobante con
  `FECompConsultar`. Sólo pasa después de un incierto.
- **Sin columnas nuevas:** el aviso antes de encolar y «Emitir con la fecha de hoy» usan la fecha del último comprobante
  autorizado que tiene la base. Si lo emitió otro sistema, la base no lo conoce: no hay aviso, y lo resuelve la revisión.
- **Una columna para la decisión 2:** el comprobante guarda el último autorizado de ARCA que vio su revisión
  (`ReviewedLastAuthorized`).
- **Otro sistema en el mismo punto de venta:** con la decisión 2, cada factura pide una decisión. Por eso el robot da de alta un
  punto de venta propio, y las guías de la conexión piden uno propio en los modos 1 y 2.
- **Las pruebas:** el ARCA simulado rechaza con 10016 una fecha anterior a la del último comprobante.

## Alternativas descartadas

- **Deducir del 10016 que fue por la fecha:** ARCA responde el mismo 10016 cuando el número es nuestro, autorizado tarde y
  todavía sin mostrarse en la consulta. Soltar el número ahí podría facturar dos veces la misma venta.
- **Corregir la fecha sola,** a la de hoy o a la del último: cambia un dato legal sin que lo vea una persona.
- **Reenviar con espera hasta que la fecha salga de la ventana:** frena la secuencia varios días y no se arregla sola.
- **Para la decisión 2, sólo la guía de restauración y la retención de los PDF:** no cubre otro sistema en el mismo punto de
  venta, ni una restauración hecha sin la guía.
