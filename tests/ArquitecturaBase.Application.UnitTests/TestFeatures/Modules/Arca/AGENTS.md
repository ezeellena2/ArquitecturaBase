# ARCA: dobles de Application

`ArcaApplicationHost` arma los servicios reales del módulo con todo lo demás en memoria (con `withPlatform`, la CUIT de la plataforma configurada para el modo delegación): `FakeArcaStore` (conexión, certificados y configuración de Facturación, con las escrituras y los locks sólo dentro de `FakeUnitOfWork`), las credenciales, los tickets, el cliente de WSFEv1 y dos familias de prueba (`FakeArcaDefinition`): el padrón obligatorio y la facturación opcional, con su resultado, su completitud y lo que pasa mientras prueban programables. Sin red, base ni proveedores. Con `withRobot`, también el robot prendido con su aviso, `FakeArcaRobotRuns` (los dos lados en memoria), la cola del host, los correos y el operador.

Hereda [las pruebas de ARCA](../../../Modules/Arca/AGENTS.md). No crear puertos productivos para estos dobles ni importar otros módulos.
