# ARCA: dobles de Application

`ArcaApplicationHost` arma los servicios reales del módulo con todo lo demás en memoria: `FakeArcaStore` (conexión, certificados y configuración de Facturación, con las escrituras y los locks sólo dentro de `FakeUnitOfWork`), las credenciales, los tickets, el cliente de WSFEv1 y dos familias de prueba (`FakeArcaDefinition`): el padrón obligatorio y la facturación opcional, con su resultado, su completitud y lo que pasa mientras prueban programables. Sin red, base ni proveedores.

Hereda [las pruebas de ARCA](../../../Modules/Arca/AGENTS.md). No crear puertos productivos para estos dobles ni importar otros módulos.
