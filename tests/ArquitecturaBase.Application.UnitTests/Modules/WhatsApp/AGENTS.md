# WhatsApp: tests de Application.UnitTests

Prueba clasificación entrante, orientación pública, códigos, perfil y canales con dobles de los puertos del módulo.

## Al modificar

- Conservar IDs de botones históricos, historial sin credenciales y acceso en Manual. La migración preserva filas, relaciones, lecturas, receipts y envíos; los tests usan Meta simulado.

- Probá cuotas, consentimiento, emisión y verificación sin invocar Meta ni otros proveedores.
- Conservá el reparto entre el límite del servicio y los helpers que no guardan.

## Referencias

- [Reglas funcionales del módulo](../../../../docs/features/whatsapp.md).
- [Independencia del núcleo](../../../../docs/guides/quitar-whatsapp.md).

- Compilador: controles, dropdown manual y `source.catalog` dinámico con datos de pantalla declarados, bytes/hash/fixtures sintéticos, orden/culturas/textos completos sin fallback, límites UTF-16 y presupuestos, required/ausencia/false y decimal exacto (overflow/escala/gramática).
- Canal: registro idempotente, cancelación y evidencia externa Pending separada de compatibilidad local. No simular que un fixture fue aceptado por Meta.
- FlowReplyService: mapping sellado y codec de seis tipos antes del sink; replay exacto, respuesta alterada rechazada y datos inválidos nunca enviados como valores tipados.

G6 prueba idempotencia por ejecución/destinatario, contacto fijado, número confirmado, ventana, revocación antes del efecto, fences, preparación recuperable y efecto incierto. También verifica que la lista no contenga valores y que revocar lectura de registros o recargar oculte cuerpos enviados y pendientes.

El cambio de modo se prueba aparte: permisos mínimos por destino, cuenta activa, disponibilidad local de IA, revisión y toma Manual con Pending/Sending/Uncertain. El doble no llama al proveedor ni interpreta lenguaje.

El hilo prueba los cinco estados de disponibilidad sin Submit/Get privados ni efectos, y la lista/noexistente no los consulta. Preserve las pruebas FormAction con y sin lectura de registros al ampliar el DTO.
