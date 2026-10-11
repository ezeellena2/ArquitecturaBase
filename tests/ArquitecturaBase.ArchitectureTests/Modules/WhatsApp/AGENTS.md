# WhatsApp: aporte de arquitectura

Leer [WhatsApp](../../../../docs/features/whatsapp.md). Mantener acá las reglas ejecutables propias del módulo y verificar sus límites sin nombrarlo desde las pruebas de otras áreas.

Los métodos públicos de dispatch usan `OperationLog`; el inicio durable del efecto requiere lock, lease y fence vigentes. No relajar límites de capas, módulo ni transacción para acomodar una implementación.
