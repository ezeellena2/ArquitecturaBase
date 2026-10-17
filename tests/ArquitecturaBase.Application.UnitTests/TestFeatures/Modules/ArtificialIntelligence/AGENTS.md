# ArtificialIntelligence: dobles tipados del motor

Capacidades ficticias y payloads tipados para probar catálogo, permisos y propuestas sin proveedores ni UoW. No crear puertos productivos sólo para estos dobles. Hereda las reglas de tests y del módulo.

AssistantOperationsFixture usa la política de aceptación real con puertos en memoria y FakeUnitOfWork: observa un límite por aceptación, replay antes de cuota y procedencia sellada. Su protector cuenta descifrados para probar rechazos por contexto o permisos antes de leer contenido. No ejecuta proveedor ni demuestra restricciones PostgreSQL.

AssistantAudioExecutionFixture usa Context/Dispatch/Get reales y descriptores ficticios cifrados para verificar el propósito técnico sin herramientas ni permisos prestados. No demuestra almacenamiento privado real, HTTP ni restricciones PostgreSQL.
