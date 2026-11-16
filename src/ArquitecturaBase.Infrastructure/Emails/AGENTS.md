# Emails: render, cola y entrega

Implementa templates localizados, cola y worker de correo y adaptadores de entrega. Las reglas de emisión e invitación pertenecen a Application.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/identidad.md`](../../../docs/features/identidad.md).

## Al modificar

- Los templates y sus recursos se mantienen en los dos idiomas; escapá los datos insertados en HTML.
- La cola productiva persiste cifrada en la transacción del llamador. El registro usa el store scoped; el constructor en memoria sólo sirve para tests aislados. Nunca enviar antes del commit ni repetir una reserva incierta.
- Un adjunto (`EmailAttachment`, como el PDF de la copia de una acción de formularios) viaja con sus bytes dentro del contenido cifrado de la cola; `MimeMessageFactory` lo agrega con su nombre y su tipo. Los correos sin adjuntos conservan su contenido.
- El worker resuelve dependencias scoped por operación; los secretos SMTP vienen de configuración externa.
- Un intento por reserva (N5 y N8). `IEmailSender` devuelve el resultado clasificado, no una excepción. El plazo propio, de 12 s, cuenta desde que conecta (MailKit espera 2 min). Conectar y autenticarse va antes del cuerpo, donde el apagado corta (`Stopped`, el worker lo devuelve con `GiveBackCurrent`) y toda falla se reintenta (la conexión, un 535, un 4xx); desde el primer comando del mensaje sólo cuenta el plazo propio: un 530 se reintenta como las credenciales, un 4xx también (incluso después del punto final, que dice que el servidor no lo tomó, RFC 5321), un 5xx no (al remitente, como el cupo diario de Gmail, es `SenderRejected`, que la salud cuenta) y cualquier otro corte queda incierto y no se repite. Lo que se reintenta vuelve a la cola a esperar (`RetryCurrentLaterAsync`, que distingue devuelto, vencido y no devuelto), sin esperas en memoria. Anotar y completar comparten un token de 10 s (`SettleTimeout`, `CompleteCurrentAsync`): con el envío, caben en los 25 s del apagado.
- Una invitación anota cómo le fue (`IEmailDeliveryService`, con su propio plazo): el Message-ID al salir, fallida si venció o la rechazaron y sin confirmar si se cortó. `EmailHealth` y su chequeo `email` (Degraded, nunca Unhealthy) y `EmailMetrics` (de `IMeterFactory`) siguen al servidor; los logs llevan el id del correo, nunca la dirección.
- Probá qué ocurre si no se encola o falla el envío sin filtrar dirección, código o enlace en logs.

## Verificación y ejemplos

- [Colas transaccionales](../../../docs/architecture/backend.md).
- [Pruebas de correo](../../../tests/ArquitecturaBase.Api.IntegrationTests/Emails).
