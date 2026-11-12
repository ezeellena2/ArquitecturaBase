# WhatsApp: dobles de pruebas

Sólo contratos propios/Result y datos ficticios. No proveedores externos; no registrar estos dobles en src. Retirar junto con el módulo.

WhatsAppChatModeFixture implementa puertos de cuentas, permisos, conversación y disponibilidad neutral para observar sólo configuración de modo; Submit/Get del motor no se invocan.

WhatsAppAudioFixture aporta cuentas reales en memoria, Read por operador, receptor explícito, reloj, objeto privado y puertos de adquisición/repositorio. Verifica contacto antes de mensaje y que la llamada de medios ocurra fuera del límite; no implementa un motor IA alternativo.

Los fixtures Assistant usan Actor/Subject distintos, permisos vigentes y el reloj del negocio. La operación neutral y la consulta de cuenta afirman que se ejecutan fuera de la UoW; la señal afirma postcommit. La confirmación construye la sesión real y sus dos sellos; el dispatch usa ese mismo contenido, sin fabricar valores en el test.

WhatsAppAssistantTerminalFixture captura sólo las peticiones neutrales H4/H7 y devuelve recibos ficticios tipados. Permite comprobar el acuse estático, el contexto exacto y la fecha original, sin ejecutar modelo, Meta ni persistencia. Desde IA3-A su dueño de autoría sólo resume (crear desde un terminal lanza) y H7 sigue con su sink. Con las planillas trae la versión de la capacidad y un lector falso cuyas referencias, por pedido, arman la cadena de operaciones de una conversación.

WhatsAppAssistantPresentationFixture conserva el guard, un lector readonly y contenido ficticio sellado; se comparte con los fixtures de aceptación para comprobar recibos e historial sin duplicar un motor.

WhatsAppFlowDeprecationFixture conserva operación/binding publicados y manifiesto real del contrato, reloj falso y puertos neutrales de retiro. Afirma una UoW y Definitions antes de Flow, sin importar BusinessForms ni simular su implementación.

WhatsAppFlowPreviewFixture es un Flow publicado en un idioma con un Meta falso que puede dejar de contestar en una llamada de la vista oficial; no simula red ni la respuesta real de Meta.

RecordingWhatsAppInboundSignal cuenta avisos sin interpretar operaciones neutrales ni ejecutar el procesador.

WhatsAppWebhookSignalFixture usa dos UoW ficticias para persistencia y retry, y registra el commit exitoso sólo después del await. Las señales comprueban que ambos límites están cerrados; los casos de firma y commit fallidos no avisan. No acredita concurrencia de PostgreSQL ni efectos externos.

WhatsAppSpreadsheetFixture monta una planilla sobre el fixture de la conversación del asistente: Meta, el lector del dueño y la copia afirman correr fuera de la UoW, y la respuesta pasa por la política automática real.

FakeWhatsAppAssistantAgentSources dobla el agente guardado (o ninguno), las cuentas de Probar y un historial ficticio con las respuestas del día y el inicio de la ráfaga; `WhatsAppAssistantAcceptanceFixture.CreateReply` arma la política de respuestas con ellos y la derivación real.

WhatsAppReceivedDocumentFixture monta un documento recibido sobre el mismo fixture: Meta, el lector del dueño, la copia y el puerto de la bandeja afirman correr fuera de la UoW; los rechazos y la derivación pasan por las políticas reales.
