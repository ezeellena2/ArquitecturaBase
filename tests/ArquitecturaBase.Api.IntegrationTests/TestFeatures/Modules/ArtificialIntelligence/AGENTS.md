# Asistente: escenarios exclusivos de integración

Estos helpers crean cuentas y conexiones ficticias mediante contratos existentes del arnés.
El proveedor de coordinación consulta PostgreSQL real y verifica reserva confirmada y ausencia
de transacción durante el envío; nunca abre conexiones a proveedores externos.

La falla de confirmación se provoca dentro de la UoW real, una vez después de guardar para
comprobar rollback y recuperación en un scope nuevo. No usar este comportamiento en src.
Hereda [TestFeatures](../../AGENTS.md) y [las pruebas IA](../../../Modules/ArtificialIntelligence/AGENTS.md).

AiAssistantExecutionScenario permite abrir un segundo host con la misma composición y base para verificar reservas entre réplicas. AiAssistantExecutionScenario y AiAssistantTestScenario crean turnos por el contrato HTTP de aceptación real; no inyectan ni reconstruyen perfiles de ejecución. Un seed que no va a ejecutar el motor (retención o coordinación doblada) no recibe un sello ficticio sólo para parecer una aceptación real.

AiAssistantContractCapture escribe bytes HTTP intactos del asistente de Inicio sólo con AI_ASSISTANT_HTTP_OUTPUT; no completa ni reescribe los cuerpos.

AudioTranscriptionHandler y AudioNormalizationFiles/Client son dobles técnicos sin red ni facturación; sus bytes ficticios prueban límites y preservación, nunca compatibilidad del codec real. El fixture común del coordinador permite Proposal o NormalizedAudio y sólo acredita coordinación, no persistencia PostgreSQL.
