# WhatsApp: proveedores, workers y persistencia

Implementa Meta, firma, parsing del webhook, cola de acceso transaccional cifrada, workers, historial de entregas y persistencia del módulo. El registro reúne sus adaptadores.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/whatsapp.md`](../../../../docs/features/whatsapp.md).

## Al modificar

- Conservar IDs de botones históricos, historial sin credenciales y acceso en Manual. La migración preserva filas, relaciones, lecturas, receipts y envíos; los tests usan Meta simulado.

- Validá firma e idempotencia antes de aplicar efectos; no registrar cuerpos sensibles, números completos ni logs de HttpClient con secretos.
- `WhatsAppSendQueue` persiste códigos, invitaciones y orientación en la transacción del llamador; sólo el consumidor durable llama a Meta después del commit. El chat manual usa una salida durable separada; no repetir automáticamente Sending/Uncertain. Ambos workers comparten lease Redis por destino, detrás de RedisCache. Los scopes de reservar, llamar a Meta y completar son separados; la llamada externa no mantiene una transacción abierta. Los receipts preservan estados que llegan antes del registro del saliente.
- Las consultas EF de negocio usan lectores o repositorios propios. La persistencia se registra en `WhatsAppInfrastructureRegistration`.
- Probá reintentos, fallas, carreras y privacidad con el nivel de logs usado por los tests.

## Verificación y ejemplos

- [Pruebas técnicas](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp).
- [Configuración local](../../../../docs/guides/whatsapp-en-local.md).
- [Eliminación del módulo](../../../../docs/guides/quitar-whatsapp.md).

Flows 2b: WabaId opcional de la misma conexión; pertenencia antes de efectos, sin nuevo requisito de arranque de texto. Operación/bindings propios, lock whatsapp-flow y FK compuesta interna; protector de manifiesto/proyección por propósito e identidad. Worker verifica la cuenta, marca el efecto en su propio límite inmediatamente antes del proveedor, acota la llamada al lease/deadline y completa con un token independiente y acotado; un rechazo preenvío seguro limpia InFlight, mientras una cancelación posterior conserva incertidumbre. No reintentar POST ni Create/Publish inciertos. Sin ID consulta candidatos y conserva revisión, sin adoptar por nombre. Cliente sin logs/bearer en CDN/redirects, respuestas limitadas; preview oficial HTTPS y vencimiento explícito. Pruebas sólo Meta simulado.

Los diagnósticos de Flows reconocen índices y propiedades permitidas tanto con puntos como con corchetes de Meta. No transportar mensajes brutos ni deducir IDs desde texto; las identidades vienen del mapping sellado.

Verify lee health_status de Flow y del teléfono exacto por separado, requiere AVAILABLE en ambos y conserva owner/perfil/hash. Assets sólo HTTPS443 sin userinfo desde *.fbcdn.net o mmg.whatsapp.net exacto; sin bearer, redirects ni más de 1 MiB. Bloqueos de cuenta no se eluden con cambios de flags.

El webhook protege response_json de nfm_reply por mensaje/contacto y el worker de captura corre independiente del procesador de texto. Antes de cada Create/Upload/Publish verifica pertenencia paginada de PhoneNumberId al mismo WabaId; los tests cubren un mismatch confirmado antes del efecto con cero llamadas externas. La recepción no llama a Meta; sólo usa puertos de captura neutrales.

Medios: Graph consulta con PhoneNumberId; sólo HTTPS443 de lookaside.fbsbx.com o mmg.whatsapp.net recibe el bearer de descarga, sin redirecciones ni logs HTTP. Valida hash/tipo/tamaño de metadata y límite de lectura de 25 MiB; el contenido real se valida en el consumidor. No persiste URLs de descarga ni crea recuperación durable de uploads.
