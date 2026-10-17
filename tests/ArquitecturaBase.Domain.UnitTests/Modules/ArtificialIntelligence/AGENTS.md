# Inteligencia artificial: pruebas

Reglas puras, sin proveedores ni claves reales; primero el test rojo para invariantes nuevas.

Hereda las instrucciones del proyecto. Su fuente funcional es [inteligencia artificial](../../../../docs/features/inteligencia-artificial.md); operación y seguridad, [configurar IA](../../../../docs/guides/configurar-ia.md); independencia, [quitar IA](../../../../docs/guides/quitar-ia.md). No importar otros módulos. Actualizar mapa y fuentes con un cambio de responsabilidad.

Turnos del asistente prueban cancelación contra publicación tardía, fence/deadline, caída después
de reservar HTTP y uso acumulado parcialmente desconocido. Conversaciones mantienen unión de
permisos y retirada idempotente. No confundir los estados durables con la disponibilidad del proveedor.

El sello del perfil verifica el límite en bytes UTF-8, vacío inválido, reemplazo rechazado y captura sólo en Pending. Un replay idéntico conserva perfil y revisión también después del claim.

AudioUsage prueba duración informada, cero explícito, acumulación y conservación después de vencimiento/purga sin inventar tokens.
