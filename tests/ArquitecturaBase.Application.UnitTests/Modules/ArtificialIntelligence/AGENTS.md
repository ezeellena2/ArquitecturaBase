# Inteligencia artificial: pruebas

Coordinación con puertos doblados y FakeUnitOfWork. Probar límite/rollback/revisión, deduplicación, cambios de proveedor y catálogo/prueba sin guardar. Catálogo acepta candidata sin modelo y rechaza revisión vieja/clave de otro proveedor antes de llamar al puerto. Recursos bilingües incluidos en la paridad.

Hereda las instrucciones del proyecto. Su fuente funcional es [inteligencia artificial](../../../../docs/features/inteligencia-artificial.md); operación y seguridad, [configurar IA](../../../../docs/guides/configurar-ia.md); independencia, [quitar IA](../../../../docs/guides/quitar-ia.md). No importar otros módulos. Actualizar mapa y fuentes con un cambio de responsabilidad.

El asistente prueba validación antes de UoW (longitud UTF-16 y Unicode), herramientas desconocidas
o argumentos extras sin consultar fuentes, permisos revocados, evidencia ajena, contrato terminal
estricto y deadline con FakeTimeProvider. Dobles con datos ficticios; no HTTP facturable. Las carreras,
cuotas compartidas, scopes, cifrado, rollback y retención reales pertenecen a integración con Docker.
ContextPolicy verifica es/en con cultura ambiente distinta de la capturada y corta historia
anterior a un hueco vencido sin descifrarla. Dispatch comprueba consumo conocido ante bloqueo
de publicación y la reserva conservadora de permisos. Retention verifica cuenta eliminada,
cancelación durable y purga física posterior a la ventana; el FK independiente se prueba en Docker.

ConversationTitleTests comprueba grafemas Unicode, normalización, contexto original de cifrado
y ausencia de descifrado ante bloqueo/vencimiento con FakeTimeProvider. ToString no expone títulos.
