# ARCA: módulo opcional

Anillo externo propio (`Arca:SecretProtection`, el mismo diseño que el de IA) y opciones `Arca` con las direcciones de cada ambiente por código de servicio WSN, validadas al arrancar. Los clientes HTTP tienen un nombre por familia y van sin redirecciones, loggers ni la resiliencia global (EXTEXP0001 se suprime sólo en el registro): un pedido a ARCA no se repite a ciegas. `Connection/` reúne seguridad, WSAA y el sobre SOAP común; cada familia tiene su carpeta, y la persistencia va en `Persistence/{Configurations,Repositories,Readers}/<Familia>`. El registro tiene una línea por familia.

Hereda las instrucciones del proyecto. Sus decisiones son el [ADR 0023](../../../../docs/decisions/0023-arca-como-modulo-opcional.md) y el [ADR 0025](../../../../docs/decisions/0025-familias-de-servicios-arca.md); la configuración, [configurar ARCA](../../../../docs/guides/configurar-arca.md). No importar otros módulos. Actualizar mapa y fuentes con un cambio de responsabilidad.
