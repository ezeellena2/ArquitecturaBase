# Delivery: persistencia técnica y consumo

DurableMessageStore es scoped y exige la transacción del llamador: protege y agrega, nunca guarda. La fuente confirma la reserva antes de descifrar y ceder al worker. Una reserva interrumpida no se repite: sólo vuelve al canal la que el worker devuelve porque esperaba su turno o el reintento que ya iba a hacer (`GiveBackCurrent`, H8). Completar y devolver usan un token propio. Los codecs del módulo quedan en su carpeta.

Referencia: [colas transaccionales](../../../docs/architecture/backend.md#colas-transaccionales).
