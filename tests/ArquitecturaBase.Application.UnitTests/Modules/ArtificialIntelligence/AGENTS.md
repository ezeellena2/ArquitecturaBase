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

C0 conserva paridad de las cinco consultas de Inicio y prueba catálogo exacto, identidades sin herencia, perfiles/instrucciones/capacidades/terminales sellados y presupuestos por capacidad. Los casos de observación usan lecturas tipadas previas, rechazan otro turno y el mismo batch y recorren Advance→RecordProgress→Complete→codec nuevo con referencias ordinales protegidas. Inicio tampoco puede cancelar/retirar canales ajenos. Ninguna propuesta ni snapshot sale en su DTO HTTP.

AudioOperations/Execution prueban el mismo Submit/Prepare/Dispatch/Complete/Get con perfil STT separado, actor Read sin Send/Home, cifrado y resultado técnico; modelo textual no calificado no bloquea la normalización. Rechazos antes de efecto y cierre tardío preservan consumo sin contenido.

ChannelQuota verifica Inicio y canales separados, interpretación/audio independientes, replay antes de cuota y persistencia del cupo sin contenido; la capacidad global y el límite de instalación conservan sus pruebas.

Dispatch y AudioCompletionSignal comprueban el aviso neutral con ChannelId ficticio opaco después del commit, incluidos cierres terminales por guards. Un rollback o Result fallido no avisa; un cierre conocido puede reiterar el aviso coalescente y los turnos activos conservan la recuperación periódica.

ConversationReservation comprueba Manual con dos ámbitos del mismo actor, con/sin Subject, exclusión del mismo ámbito, Get/Cancel sellados, replay antes de cuota y cuota compartida entre ámbitos. El fallback null conserva la reserva previa; NormalizeAudio mantiene sus controles existentes.
