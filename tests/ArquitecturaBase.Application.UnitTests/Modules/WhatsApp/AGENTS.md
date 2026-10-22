# WhatsApp: tests de Application.UnitTests

Prueba clasificación entrante, orientación pública, códigos, perfil y canales con dobles de los puertos del módulo.

## Al modificar

- Conservar IDs de botones históricos, historial sin credenciales y acceso en Manual. La migración preserva filas, relaciones, lecturas, receipts y envíos; los tests usan Meta simulado.

- Probá cuotas, consentimiento, emisión y verificación sin invocar Meta ni otros proveedores.
- Conservá el reparto entre el límite del servicio y los helpers que no guardan.

## Referencias

- [Reglas funcionales del módulo](../../../../docs/features/whatsapp.md).
- [Independencia del núcleo](../../../../docs/guides/quitar-whatsapp.md).

- Compilador: controles, dropdown manual y `source.catalog` dinámico con datos de pantalla declarados, bytes/hash/fixtures sintéticos, orden y textos completos sin fallback por cada cultura solicitada, incluidos objetivos monolingües; límites UTF-16 y presupuestos, required/ausencia/false y decimal exacto (overflow/escala/gramática).
- Canal: registro idempotente, cancelación y evidencia externa Pending separada de compatibilidad local. No simular que un fixture fue aceptado por Meta.
- FlowReplyService: mapping y oferta sellados antes del sink; replay exacto, respuesta alterada rechazada y datos inválidos nunca enviados como valores tipados. MultiChoice conserva GUID D distintos, orden y rango, con [] explícito diferente de omisión; una lista inválida corta antes de archivos o captura.

G6 prueba idempotencia por ejecución/destinatario, contacto fijado, número confirmado, ventana, revocación antes del efecto, fences, preparación recuperable y efecto incierto. También verifica que la lista no contenga valores y que revocar lectura de registros o recargar oculte cuerpos enviados y pendientes.

El cambio de modo se prueba aparte: permisos mínimos por destino, cuenta activa, disponibilidad local de IA, revisión y toma Manual con Pending/Sending/Uncertain. El doble no llama al proveedor ni interpreta lenguaje.

El hilo prueba los cinco estados de disponibilidad sin Submit/Get privados ni efectos, y la lista/noexistente no los consulta. Preserve las pruebas FormAction con y sin lectura de registros al ampliar el DTO.

El perfil vigente r9 agrega CheckboxGroup propio y dinámico con rango y mapping sellados; 33 casos de compilación y dos de aceptación cubren ese borde. El prellenado conserva r8/r9 y excluye multiChoice y archivos; los fixtures r3–r8 permanecen intactos. El codec y la respuesta clasifican arrays por mapping antes de media: veinte elecciones y una foto producen una sola descarga. El contador previo cuenta sólo arrays de objetos; [] conserva el presupuesto histórico de 64 KiB, pero sin archivos no marca descarga. Estas pruebas no acreditan aceptación ni entrega física por Meta.

H6 prueba por separado almacenamiento privado WriteOnce/SHA/Ogg y recuperación de objeto previo al commit, permisos manuales Read sin Send/Home, Subject automático explícito, revocación/vínculo, la única UoW de Prepare/Complete y el límite entre I/O y transacción. El hilo cubre texto sólo confirmado, retención viva y preview genérico. AudioDispatch cubre copia inicial sin cuenta vinculada activa, pérdida de vínculo durante descarga y reintento de adquisición, sin trabajo IA; una solicitud ya aceptada con acceso revocado conserva cero I/O. Los dobles no acreditan calificación del modelo ni entrega física.

C3 prueba replay compartido de dos operadores, revocación, vencimiento fuente, descarte durable, IDs incluidos y cultura efectiva completa. El dispatch usa una sesión creada por la confirmación real; incluye false como string, exclusión de campo, origen automático explícito y control de sesión anterior. El codec H5 valida forma cerrada y preserva únicamente IDs/revisión capturados.

TerminalPolicy verifica delegación exacta a los sinks H4/H7, source/ref/context cruzados, idiomas, acuse fijo sin eco del modelo, fecha original, replay y silencio Manual. No inventa otro ejecutor ni abre una UoW; los dueños conservan validación y persistencia.

Presentation cubre razón y valores capturados sin valores canónicos, vencimiento y permiso; ChatQuery cubre el retiro al recargar, preview genérico y títulos sin IDs. No se llama al motor desde GET.

Captura de origen: el fixture de FlowReplyService conserva los 22 casos previos y agrega 20 sobre cabecera explícita, formatos, origen privado, propósito, retención, cultura y replay. No simula que un mensaje distinto tenga un recibo completado después de purgar el contenido; conserva el registro previo y no infiere Assistant de un operador null.

C3 automático cubre replay antes del cupo, dos ofertas por día del negocio, interpretación del rechazo con el cupo ya agotado y su persistencia al día siguiente; una propuesta nueva no encola una tercera oferta. Conserva retiro de permisos/destino, toma Manual tardía, salidas pendientes y charla sin lectura. H5 prueba pregunta→elección→Flow con un solo cupo y UUID distintos, elección inválida sin bloquear una posterior válida y ninguna nueva aceptación IA al tocar un botón. H4/H7 prueban sinks fuera de UoW, acuse fijo automático y silencio Manual, incluso con cupo agotado.

C4 prueba creación desde FlowReply Completed, primer POST antes del worker, Always sin ninguna consulta a Operations aun con IA pausada, replay por registro con otro recibo, Actor real para la interpretación Manual, cupo/bucle automáticos y confirmación humana. Una lectura temporalmente no disponible conserva la fuente pendiente; el idioma de conversación no hereda el fallback del formulario capturado.

FlowDeprecationServiceTests verifica orden Definitions→Flow, versión no usada y trabajo pendiente al reservar y marcar, lease/fence entre dueños, recuperación por GET tras incertidumbre o rechazo, y fallas de cuenta/manifiesto con NeedsAttention, espera creciente y sin perder evidencia de un POST; todo con puertos falsos, sin red ni PostgreSQL.
