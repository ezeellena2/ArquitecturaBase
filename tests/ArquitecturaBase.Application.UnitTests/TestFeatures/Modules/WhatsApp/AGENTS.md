# WhatsApp: dobles de pruebas

Sólo contratos propios/Result y datos ficticios. No proveedores externos; no registrar estos dobles en src. Retirar junto con el módulo.

WhatsAppChatModeFixture implementa puertos de cuentas, permisos, conversación y disponibilidad neutral para observar sólo configuración de modo; Submit/Get del motor no se invocan.

WhatsAppAudioFixture aporta cuentas reales en memoria, Read por operador, receptor explícito, reloj, objeto privado y puertos de adquisición/repositorio. Verifica contacto antes de mensaje y que la llamada de medios ocurra fuera del límite; no implementa un motor IA alternativo.

Los fixtures Assistant usan Actor/Subject distintos, permisos vigentes y el reloj del negocio. La operación neutral y la consulta de cuenta afirman que se ejecutan fuera de la UoW; la señal afirma postcommit. La confirmación construye la sesión real y sus dos sellos; el dispatch usa ese mismo contenido, sin fabricar valores en el test.
