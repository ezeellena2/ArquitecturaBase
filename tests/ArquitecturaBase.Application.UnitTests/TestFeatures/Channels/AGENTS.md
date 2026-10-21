# Channels: dobles exclusivos de pruebas
- StubStructuredFormChannel comprueba identificadores duplicados/vacíos del consumidor; no ejecuta compilación ni simula disponibilidad.
- Sólo BCL, contratos de Application y Result. Ninguna implementación de prueba va en src.
- El compilador real se verifica en Modules/WhatsApp, sin conexión a un proveedor.

RecordingAssistantOperationCompletionSignal conserva sólo identificadores de canal ficticios y permite observar el estado al avisar; no conoce motores ni módulos.
