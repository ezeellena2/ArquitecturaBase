# Delivery: reserva y finalización

Cada método que escribe abre un límite IUnitOfWork. La reserva y limpieza vencida usan el repositorio; no se llama al proveedor externo dentro del límite.

Referencia: [colas transaccionales](../../../../docs/architecture/backend.md#colas-transaccionales).
