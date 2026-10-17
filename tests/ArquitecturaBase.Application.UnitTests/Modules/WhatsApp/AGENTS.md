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

C2 verifica init-values r8 por pantalla y tipos wire reales, ausencia distinta de cero/false, mapas inmutables, exclusión de archivos y primer screen sólo de archivos. Los fixtures r8 se exportan desde el compilador real y comparan bytes exactos; los fixtures r7 permanecen intactos. No acreditan aceptación ni entrega física por Meta.

H6 prueba por separado almacenamiento privado WriteOnce/SHA/Ogg y recuperación de objeto previo al commit, permisos manuales Read sin Send/Home, Subject automático explícito, revocación/vínculo, la única UoW de Prepare/Complete y el límite entre I/O y transacción. El hilo cubre texto sólo confirmado, retención viva y preview genérico. Los dobles no acreditan calificación del modelo ni entrega física.

C3 prueba replay compartido de dos operadores, revocación, vencimiento fuente, descarte durable, IDs incluidos y cultura efectiva completa. El dispatch usa una sesión creada por la confirmación real; incluye false como string, exclusión de campo, origen automático explícito y control de sesión anterior. El codec H5 valida forma cerrada y preserva únicamente IDs/revisión capturados.

TerminalPolicy verifica delegación exacta a los sinks H4/H7, source/ref/context cruzados, idiomas, acuse fijo sin eco del modelo, fecha original, replay y silencio Manual. No inventa otro ejecutor ni abre una UoW; los dueños conservan validación y persistencia.

Presentation cubre razón y valores capturados sin valores canónicos, vencimiento y permiso; ChatQuery cubre el retiro al recargar, preview genérico y títulos sin IDs. No se llama al motor desde GET.
