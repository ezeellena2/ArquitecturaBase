# ARCA: pruebas de Application

Permisos y paridad de recursos del módulo en la raíz; las pruebas de cada familia van en su carpeta (`Connection`, `Registry`, `Invoicing`), y sus dobles en `TestFeatures/Modules/Arca`. Los servicios se prueban con `ArcaApplicationHost`: revisión y 409, certificado inválido sin cambios, alta, renovación sin corte, prueba fuera de la transacción y reevaluación por la completitud de las familias. Sin proveedores externos.

Hereda las instrucciones del proyecto. Las decisiones del módulo son el [ADR 0023](../../../../docs/decisions/0023-arca-como-modulo-opcional.md) y el [ADR 0025](../../../../docs/decisions/0025-familias-de-servicios-arca.md). No importar otros módulos.
