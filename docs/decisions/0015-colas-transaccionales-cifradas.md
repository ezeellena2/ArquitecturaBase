# 0015 — Colas transaccionales cifradas

**Estado:** Aceptada, 2026-10-03.

## Contexto

Las colas en memoria perdían pendientes al reiniciar y podían enviar códigos antes de un commit fallido. Persistir el cuerpo sin protección expondría credenciales y datos personales en una copia de PostgreSQL.

## Decisión

Persistir contenido cifrado por canal y tipo en la transacción del caso de uso. Reservar y confirmar antes de llamar al proveedor, con exclusión entre réplicas; una reserva interrumpida no se repite automáticamente.

## Consecuencias

La capacidad se comparte por canal y encolar requiere una transacción. Los pendientes sobreviven al reinicio y se revierten con el caso de uso. El plazo limita la retención; completar elimina el payload. Un fallo durante la llamada externa puede dejar una entrega incierta: no se promete entrega exactamente una vez. La salida manual de un módulo conserva su circuito específico de revisión.

## Alternativas descartadas

Mantener memoria y aceptar pérdida/precommit; guardar códigos legibles; repetir reservas automáticamente sin idempotencia del proveedor. Redis conserva su responsabilidad de caché y coordinación, no contiene esta cola.

Detalle: [arquitectura](../architecture/backend.md#colas-transaccionales) y [despliegue](../guides/despliegue.md#colas-transaccionales).
