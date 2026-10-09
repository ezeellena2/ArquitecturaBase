# Asistente: escenarios exclusivos de integración

Estos helpers crean cuentas y conexiones ficticias mediante contratos existentes del arnés.
El proveedor de coordinación consulta PostgreSQL real y verifica reserva confirmada y ausencia
de transacción durante el envío; nunca abre conexiones a proveedores externos.

La falla de confirmación se provoca dentro de la UoW real, una vez después de guardar para
comprobar rollback y recuperación en un scope nuevo. No usar este comportamiento en src.
Hereda [TestFeatures](../../AGENTS.md) y [las pruebas IA](../../../Modules/ArtificialIntelligence/AGENTS.md).
