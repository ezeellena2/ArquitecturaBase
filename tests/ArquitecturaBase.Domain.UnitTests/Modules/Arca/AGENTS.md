# ARCA: pruebas de dominio

Reglas puras del módulo con fechas explícitas, una carpeta por familia: la CUIT, el código de servicio, el ciclo de la conexión, el certificado y la reserva del ticket en `Connection`; los datos del padrón en `Registry`; el punto de venta y el desglose del IVA de la factura A y B en `Invoicing`. Sin EF, base ni proveedores.

Hereda las instrucciones del proyecto. Las decisiones del módulo son el [ADR 0023](../../../../docs/decisions/0023-arca-como-modulo-opcional.md) y el [ADR 0025](../../../../docs/decisions/0025-familias-de-servicios-arca.md). No importar otros módulos.
