# WhatsApp: aporte de arquitectura

Leer [WhatsApp](../../../../docs/features/whatsapp.md). Mantener acá las reglas ejecutables propias del módulo y verificar sus límites sin nombrarlo desde las pruebas de otras áreas.

Los métodos públicos de dispatch usan `OperationLog`; el inicio durable del efecto requiere lock, lease y fence vigentes. No relajar límites de capas, módulo ni transacción para acomodar una implementación.

WhatsAppTransactionContribution aporta el Type exacto IWhatsAppAssistantReader mediante CapabilityReadPorts, además de sus locks y la excepción de retención existente. El lector se retira con la carpeta del módulo; las pruebas de otras áreas consumen el aporte neutral sin nombrarlo.

ModuleBoundaryTests.WhatsApp verifica los IDs privados de contexto del canal mediante literales IL exactos: sólo WhatsApp puede interpretarlos. Conserva controles de dueño, módulo ajeno y núcleo, y permite capacidades neutrales o textos distintos; ambos IDs deben aparecer en su dueño para evitar un detector vacío.
