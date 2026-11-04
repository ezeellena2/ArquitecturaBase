# ARCA: pruebas de arquitectura

`ArcaFamilyBoundaryTests` lee el fuente del módulo, como `ModuleBoundaryTests`, y verifica tres cosas: que una familia use sólo `Connection` y la raíz, que `Connection` no nombre a ninguna familia y que toda carpeta de familia esté declarada en `Families`. Una familia nueva se suma a esa lista. Los casos de control prueban que el detector ve un using y un nombre completo de otra familia.

Hereda las instrucciones del proyecto. Su decisión es el [ADR 0025](../../../../docs/decisions/0025-familias-de-servicios-arca.md). No importar otros módulos ni relajar la regla para hacer pasar código.
