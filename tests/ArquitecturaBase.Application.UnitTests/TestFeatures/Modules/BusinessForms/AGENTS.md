# BusinessForms: dobles propios de pruebas

Los dobles que consumen tipos o errores BusinessForms pertenecen a este módulo; los canales neutrales permanecen en `TestFeatures/Channels`.
Sólo se usan en tests de BusinessForms, sin referencias a WhatsApp ni implementaciones en `src`.
La regla funcional está en [formularios](../../../../../docs/features/formularios.md).

H7: FormMissingRequestFixture implementa únicamente persistencia de temas/pedidos en memoria y exige la UoW real simulada para locks/altas. Usa cuentas, permisos, validadores y TimeProvider ya compartidos; los casos prueban recibo estable, reapertura, revisión, permisos y metadata privada sin otro motor.
