# WhatsApp: dobles de pruebas

Sólo contratos propios/Result y datos ficticios. No proveedores externos; no registrar estos dobles en src. Retirar junto con el módulo.

WhatsAppChatModeFixture implementa puertos de cuentas, permisos, conversación y disponibilidad neutral para observar sólo configuración de modo; Submit/Get del motor no se invocan.

WhatsAppAudioFixture aporta cuentas reales en memoria, Read por operador, receptor explícito, reloj, objeto privado y puertos de adquisición/repositorio. Verifica contacto antes de mensaje y que la llamada de medios ocurra fuera del límite; no implementa un motor IA alternativo.
