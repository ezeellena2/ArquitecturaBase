# Delivery: persistencia técnica y consumo

DurableMessageStore es scoped y exige la transacción del llamador: protege y agrega, nunca guarda. La fuente confirma la reserva antes de descifrar y ceder al worker. Una reserva interrumpida no se repite; los codecs del módulo quedan en su carpeta.

Referencia: [colas transaccionales](../../../docs/architecture/backend.md#colas-transaccionales).
