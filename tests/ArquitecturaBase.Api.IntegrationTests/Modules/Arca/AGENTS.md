# ARCA: pruebas de integración

`ArcaTestModule` aporta el anillo de prueba y deja los clientes de ARCA sin red: ningún test llama a ARCA. Las opciones y la composición se prueban en la raíz; cada familia, en su carpeta. Los dobles de ARCA van en `TestFeatures/Modules/Arca`. Docker es obligatorio, como en el resto del proyecto.

Hereda las instrucciones del proyecto. Las decisiones del módulo son el [ADR 0023](../../../../docs/decisions/0023-arca-como-modulo-opcional.md) y el [ADR 0025](../../../../docs/decisions/0025-familias-de-servicios-arca.md); la configuración, [configurar ARCA](../../../../docs/guides/configurar-arca.md). No importar otros módulos.
