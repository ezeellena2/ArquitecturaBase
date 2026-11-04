# ARCA: piezas exclusivas de integración

`ArcaPersistenceScenario` arma la conexión de la instalación y sus certificados como un caso de uso, dentro de un límite real (`ApiFactory.InTransactionAsync`). Hay una sola conexión por base: cada test la borra al terminar, y la cascada se lleva certificados, tickets y lo que guardó cada familia. `FakeArcaCertificateAuthority` hace de ARCA al emitir certificados: una CA autofirmada por ambiente, con el CN de la configuración, que firma el pedido tal como llega. Los dobles de ARCA de las tareas siguientes (`FakeArca`) van acá y nunca abren red.

Hereda [TestFeatures](../../AGENTS.md) y [las pruebas de ARCA](../../../Modules/Arca/AGENTS.md). No importar otros módulos.
