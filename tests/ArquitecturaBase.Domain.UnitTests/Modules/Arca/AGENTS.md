# ARCA: pruebas de dominio

Reglas puras del módulo con fechas explícitas, una carpeta por familia: la CUIT, el código de servicio, el ciclo de la conexión, el certificado y la reserva del ticket en `Connection`; los datos del padrón en `Registry`; el punto de venta, el desglose del IVA de la factura A y B, la clase y el código del comprobante, los importes de la C, la ventana de la fecha, la espera entre intentos, el comprobante de la cola y su secuencia en `Invoicing`. Sin EF, base ni proveedores. El lado del robot de delegación, con su reserva, sus pasos y el límite de avisos, también va en `Connection`.

Hereda las instrucciones del proyecto. Las decisiones del módulo son el [ADR 0023](../../../../docs/decisions/0023-arca-como-modulo-opcional.md) y el [ADR 0025](../../../../docs/decisions/0025-familias-de-servicios-arca.md). No importar otros módulos.
