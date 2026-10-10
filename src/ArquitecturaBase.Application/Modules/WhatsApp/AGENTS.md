# WhatsApp: coordinación funcional

Agrupa casos de uso, helpers, contratos, modelos, validadores, recursos y adaptadores de canales propios del módulo.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/whatsapp.md`](../../../../docs/features/whatsapp.md).
`WhatsAppInboundService` toma el contacto y el único límite; `WhatsAppInboundPolicy` clasifica metadata y selecciona acciones estructuradas; `WhatsAppAccessReplyPolicy` produce orientación pública sin leer identidad. `WhatsAppAccessActions` conserva IDs externos, y los textos viven en Access.resx/es/en. Perfil y registro conservan recorridos explícitos: `WhatsAppCodeIssuer` usa LoginCodeIssuer y la cuota común, con SignInCode o VerifyPhoneCode. Manual no interviene en acceso. El núcleo conserva el canje de enlaces pendientes.

## Al modificar

- ChatService coordina permisos HTTP, lectura propia y salida durable; DispatchService abre un límite separado para reservar/completar/recuperar. La llamada a Meta queda afuera de las transacciones. Las reglas están en «Chat de atención» de la fuente funcional.
- `WhatsAppPermissionCatalogSource` aporta códigos y textos del módulo al puerto genérico; el núcleo no los nombra.

- El procesamiento no crea cuentas, confirma teléfonos, vincula contactos ni emite enlaces secretos. No inferir acceso por frases. Policies y helpers no guardan.
- Los códigos usan el emisor común y una cuota propia por número; conservá propósito y límites aunque el pedido provenga de otro flujo.
- Los adaptadores de `Channels` implementan puertos del núcleo sin mover la implementación al núcleo.
- El módulo se registra en su bloque; no introducir referencias del núcleo a sus clases.

## Verificación y ejemplos

- [Pruebas de coordinación](../../../../tests/ArquitecturaBase.Application.UnitTests/Modules/WhatsApp).
- [Independencia del núcleo](../../../../docs/guides/quitar-whatsapp.md).

- WhatsAppStructuredFormChannel aporta IStructuredFormChannel con ChannelId neutral phone. Consulta pura y disponible sin transporte habilitado; no acredita cuenta ni habilita aceptación.
- WhatsAppFlowCompiler es dueño del JSON 7.3, presupuesto, mapping y hash wire; perfil flow-static-7.3-r3 y codec versionado. ValueCodec preserva decimal exacto/string, DateOnly y ausencia distinta de false; sin captura productiva.
- Boolean usa RadioButtonsGroup Sí/No sin inicial; decimal TextInput text + gramática; fecha CalendarPicker single/required. es/en resuelve cada texto en su cultura exacta; nombre y ayudas presentes también completos, sin fallback ni traducción automática.
- Excesos se bloquean por identidad/ruta; no truncar ni dividir pantallas. Perfil conservador UTF-16, ayuda de Dropdown bloqueada sin variante calificada. Validación local no es upload/publicación/round-trip.

2b: canal también consume servicios propios Accept/Status/Preview. Accept verifica cuenta fuera de su UoW y sella proyecciones por es/en idempotentes. Dispatch reserva/completa con UoW propios, permisos actuales y fences; nunca mantiene transporte dentro del límite. Sólo puertos neutrales hacia BusinessForms, IDs opacos sin tipos del otro módulo. Status/preview verifican owner/asset/perfil/health; diagnósticos por mapping, sin mensaje bruto. Revocación conserva IDs/Uncertain existentes.

WhatsAppFormService resuelve sólo vínculo explícito/cuenta/idioma actuales y consulta IStructuredFormSource neutral; no hereda permisos del operador. Revisión determinista de destinatario incluye actor/LinkRevision/estado/idioma/stamps con roles ordenados; no es autorización ni permite identidad/cultura cliente. Filtro sin resultados no altera elegibilidad general. Sin UoW para selección; sesión/salida todavía pendientes.
