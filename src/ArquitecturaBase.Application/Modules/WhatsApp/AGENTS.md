# WhatsApp: casos de uso del canal

Coordina en Application el módulo opcional WhatsApp: acceso con códigos, webhook y entrada, chat de atención, Flows de formularios, avisos de reglas, asistente y audios. Meta, persistencia y workers están en [Infrastructure](../../../ArquitecturaBase.Infrastructure/Modules/WhatsApp/AGENTS.md); las rutas, en [Api](../../../ArquitecturaBase.Api/Modules/WhatsApp/AGENTS.md).

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/whatsapp.md`](../../../../docs/features/whatsapp.md); para formularios, Flows o el asistente, también [`docs/features/formularios.md`](../../../../docs/features/formularios.md).

## Carpetas

- `Channels`: adaptadores de los puertos neutrales del núcleo ([guía](Channels/AGENTS.md)).
- `Configuration`: opciones del ingreso, retención y perfil técnico de Flows (`WhatsAppFlowProfile`).
- `Interfaces/Integrations`: puertos hacia Meta, protectores de datos sellados, cola de acceso, webhook y señal entrante.
- `Interfaces/Persistence`: repositorios y lectores del módulo.
- `Interfaces/Services`: contratos de los casos de uso que consumen Api, Infrastructure y `Channels`.
- `Models`: requests, respuestas, filas y payloads sellados; `WhatsAppAccessActions` guarda los IDs de botón de las plantillas de Meta.
- `Resources`: textos de acceso, Flows, asistente y permisos, cada uno con su `.en.resx`.
- `Services`: casos de uso y helpers ([guía](Services/AGENTS.md)).
- `Validation`: validadores de los requests del módulo.

## Al modificar

- El núcleo no nombra clases del módulo y el módulo no referencia BusinessForms ni IA: usa puertos de `Application/Interfaces/Channels` con IDs opacos. `WhatsAppApplicationRegistration` registra todo en su bloque, aun con WhatsApp apagado; las rutas condicionales cortan lo que exige transporte.
- Un mensaje entrante no crea cuentas, no confirma teléfonos, no vincula contactos ni emite enlaces. El acceso sale solo de botones reconocidos, nunca de frases.
- Códigos, invitaciones y orientación se encolan con `IWhatsAppSendQueue` en la transacción del llamador. Texto manual, Flows, avisos y asistente comparten una única salida durable FIFO. No agregues otra cola ni prioridad.
- Responder sin plantilla exige la ventana de 24 h (`WhatsAppChatService.IsReplyWindowOpen`) al aceptar y otra vez al despachar.
- Un método público que escribe abre una sola UoW. Policies, guards, issuers y codecs no guardan. Meta, medios, almacenamiento, IA y las escrituras de otro dueño (capturas, propuestas) van fuera del límite.
- Los límites de reserva, efecto y cierre usan `OperationLog`; los logs usan `[LoggerMessage]` y nunca llevan teléfonos enteros, BSUID, códigos, tokens, enlaces, cuerpos ni valores de respuestas.

## Referencias

- [Pruebas unitarias](../../../../tests/ArquitecturaBase.Application.UnitTests/Modules/WhatsApp) y [HTTP con PostgreSQL](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp).
- [Módulos opcionales](../../../../docs/architecture/backend.md#módulos-opcionales), [colas transaccionales](../../../../docs/architecture/backend.md#colas-transaccionales) y [quitar WhatsApp](../../../../docs/guides/quitar-whatsapp.md).
