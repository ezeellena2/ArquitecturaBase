# Delivery: reserva y finalización

Cada método que escribe abre un límite IUnitOfWork. La reserva y limpieza vencida usan el repositorio; no se llama al proveedor externo dentro del límite. `EmailDeliveryService` anota en la invitación lo que el worker de correo hizo con su correo, bajo el lock de invitaciones de la cuenta (N5 y N8); un correo sin invitación no abre nada.

Referencia: [colas transaccionales](../../../../docs/architecture/backend.md#colas-transaccionales).
