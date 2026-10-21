# WhatsApp: dobles de pruebas

Sólo contratos propios/Result y datos ficticios. No proveedores externos; no registrar estos dobles en src. Retirar junto con el módulo.

WhatsAppChatModeFixture implementa puertos de cuentas, permisos, conversación y disponibilidad neutral para observar sólo configuración de modo; Submit/Get del motor no se invocan.

WhatsAppAudioFixture aporta cuentas reales en memoria, Read por operador, receptor explícito, reloj, objeto privado y puertos de adquisición/repositorio. Verifica contacto antes de mensaje y que la llamada de medios ocurra fuera del límite; no implementa un motor IA alternativo.

Los fixtures Assistant usan Actor/Subject distintos, permisos vigentes y el reloj del negocio. La operación neutral y la consulta de cuenta afirman que se ejecutan fuera de la UoW; la señal afirma postcommit. La confirmación construye la sesión real y sus dos sellos; el dispatch usa ese mismo contenido, sin fabricar valores en el test.

WhatsAppAssistantTerminalFixture captura sólo las peticiones neutrales H4/H7 y devuelve recibos ficticios tipados. Permite comprobar el acuse estático, el contexto exacto y la fecha original, sin ejecutar modelo, Meta ni persistencia.

WhatsAppAssistantPresentationFixture conserva el guard, un lector readonly y contenido ficticio sellado; se comparte con los fixtures de aceptación para comprobar recibos e historial sin duplicar un motor.

WhatsAppFlowDeprecationFixture conserva operación/binding publicados y manifiesto real del contrato, reloj falso y puertos neutrales de retiro. Afirma una UoW y Definitions antes de Flow, sin importar BusinessForms ni simular su implementación.

RecordingWhatsAppInboundSignal cuenta avisos sin interpretar operaciones neutrales ni ejecutar el procesador.
