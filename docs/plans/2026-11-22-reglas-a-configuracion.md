# Reglas fijas que pasan a Configuración: inventario

Fecha: 2026-11-22. Estado: **propuesta sin decidir, sin programar.**

El 2026-11-21 el usuario pidió que ninguna regla de negocio quede fija en el código: «toda configuración o regla tenemos que
tratar de colocarla en la zona de configuraciones del administrador». El primer caso ya está hecho: el tope de ofrecimientos de
formularios del asistente pasó a WhatsApp › Asistente › Límites (TO1-A, backend `a62fc296`, front `3e8c9cc4`, con O36 y O39).
Este inventario junta lo que queda, para que el usuario elija qué pasa, a dónde y en qué orden.

Se armó con tres relevamientos de solo lectura (WhatsApp; Formularios e IA; el núcleo y ARCA), y las candidatas se
verificaron contra el código el mismo día.

## Criterio

- **Pasa a Configuración:** una regla de negocio que un administrador querría ajustar en su instalación (un tope, un plazo,
  un cupo, una espera, un valor por defecto).
- **Queda en el código:** lo que fija un proveedor o una norma (Meta, ARCA, OpenAI, el correo) y lo técnico (leases, lotes,
  polling, timeouts, presupuestos de bytes o del motor, reintentos con espera).
- **Seguridad, costo o abuso:** queda en `appsettings` (cupos de códigos, intentos, rate limits, tokens). Algunos podrían
  mostrarse en pantalla con el valor de `appsettings` como techo, si el usuario lo decide.

## Candidatas

Los números de la columna «Hoy» son los valores vigentes; cada uno quedaría como valor por defecto. El núcleo no nombra los
módulos opcionales, así que cada módulo guarda lo suyo en su propia configuración.

### 1. Retención y privacidad

| Regla | Hoy | Dónde está | Dónde iría |
| --- | --- | --- | --- |
| Borrar el texto de los mensajes de WhatsApp y el contenido del asistente (audios, planillas, documentos, lo compartido) | 90 días (de 1 a 3650) | `WhatsApp:MessageRetentionDays`, `WhatsAppMessageRetentionOptions.cs:20` | Configuración › WhatsApp (zona nueva), «Retención» |
| Quitar la IP y el navegador de un ingreso | 12 meses | `IdentityRetention:LoginAuditMonths`, `IdentityRetentionOptions.cs:22` | Configuración general, zona nueva «Privacidad y retención» |
| Borrar los códigos de ingreso | 30 días (mínimo 1) | `IdentityRetention:LoginCodeDays`, `IdentityRetentionOptions.cs:18` | la misma zona |
| Borrar las invitaciones viejas de una cuenta | 30 días (mínimo 7) | `IdentityRetention:InvitationDays`, `IdentityRetentionOptions.cs:31` | la misma zona |
| Borrar las conversaciones del asistente de Inicio | 30 días (techo de 30 en `appsettings`) | `AiAssistantLimits.RetentionDays`, `AiAssistantLimits.cs:35` | Configuración › IA, «Privacidad» |
| Vencer un documento recibido propuesto que nadie revisó, y borrar uno descartado | 90 días los dos (una sola constante) | `FormReceivedDocument.RetentionDays`, `FormReceivedDocument.cs:35` | Configuración › Formularios (zona nueva), «Documentos recibidos», con dos valores |

La política de privacidad promete estos plazos: tiene que leer los mismos valores. La retención de WhatsApp corre aunque el
módulo esté apagado. Hoy las de identidad y WhatsApp se leen al arrancar (`IOptions`): pasarían a leerse de la base en cada
corrida.

### 2. Asistente de WhatsApp (Límites)

| Regla | Hoy | Dónde está | Dónde iría |
| --- | --- | --- | --- |
| Esperar a que la persona termine de escribir (ráfaga) | 4 s sin mensajes nuevos, como mucho 15 s | `WhatsAppAssistantReplyPolicy.cs:36-37` | WhatsApp › Asistente › Límites, zona «Esperas» |
| Formularios que manda por una planilla (el resto pasa a una persona) | 3 | `WhatsAppAssistantRequest.MaxLoads`, `WhatsAppAssistantRequest.Spreadsheet.cs:11` | Límites, zona «Planillas y documentos» |
| Páginas de un PDF que lee (con más, contesta que no lo lee) | 10 | `WhatsAppMessage.MaxDocumentPages`, `WhatsAppMessage.Document.cs:34` | la misma zona (pesa en el costo de la IA) |
| Tiempo para tomar un archivo y su texto o audio como un solo pedido | 10 min | `WhatsAppAssistantSourcePolicy.PairingWindow`, `WhatsAppAssistantSourcePolicy.cs:17` | Límites, «Esperas» (prioridad baja) |

Para decidir aparte, porque hoy no son una constante: «no repetir un formulario ya enviado hoy salvo que lo pida», que está
escrita en las instrucciones del modelo (`WhatsAppAssistantAgentCapabilitySource.cs:51`), y que un formulario rechazado no se
vuelva a ofrecer nunca (se podría volver a ofrecer después de N días). El texto de las páginas («más de 10 páginas», en
`AssistantTerminalTexts.resx:49` y su `.en.resx`) tendría que llevar el valor. Una ráfaga de más de 15 s exige revisar cuánto
mira adelante el procesador.

### 3. WhatsApp en general

| Regla | Hoy | Dónde está | Dónde iría |
| --- | --- | --- | --- |
| Cuánto tiempo puede la persona contestar un formulario enviado | 24 h (de 1 a 168) | `WhatsApp:FlowSessionLifetimeHours`, `WhatsAppOptions.cs:30` (y otro 24 h fijo en `DisabledWhatsAppFlowEnvironment.cs:11`) | Configuración › WhatsApp, «Formularios» |
| Países que reciben códigos y de los que se aceptan números | Argentina | `WhatsApp:AllowedCountries`, `WhatsAppLoginOptions.cs` | Configuración › WhatsApp, «Ingreso» (cada país cuesta por código: también es costo) |

### 4. Formularios

| Regla | Hoy | Dónde está | Dónde iría |
| --- | --- | --- | --- |
| Tamaño máximo de una foto o un documento al responder | 25 MiB (el techo de 25 es de WhatsApp) | `BusinessForms:MaxFileSizeMiB`, `BusinessFormsOptions.cs:7` | Configuración › Formularios, «Archivos» |
| Relecturas de documentos por persona y día, después de «Armar formulario con la IA» | 5 (de 1 a 100) | `BusinessForms:DocumentReread:DailyPerAsker`, `FormDocumentRereadOptions.cs:16` | Configuración › Formularios, «Documentos recibidos» |
| Desde cuántos documentos parecidos la bandeja sugiere armar un formulario | 3 (hasta 5 grupos) | `FormReceivedDocumentReader.cs:30-31` | la misma zona (prioridad baja) |
| Quien cargó o corrigió una respuesta no puede aprobarla ni rechazarla | prohibido | `BusinessFormTask.cs:31` | en cada formulario, acción «Pedir aprobación»: «Puede decidir quien cargó» (por defecto no) |
| Rechazar exige un motivo | obligatorio | `BusinessFormTask.cs:33` | la misma acción: «Exigir motivo al rechazar» (por defecto sí) |

Con un solo aprobador en el rol, la primera regla traba la aprobación de lo que carga esa persona.

### 5. IA

| Regla | Hoy | Dónde está | Dónde iría |
| --- | --- | --- | --- |
| El día de los cupos se cortaba a medianoche UTC (las 21 h de Argentina) | **hecho el 2026-11-22**: el día del negocio | `BusinessDay` en `Application/Common/Time` | sin pantalla: la zona horaria de Configuración general, como WhatsApp |
| Consultas y lecturas de documentos por persona y día | 10 y 10 (por instalación, 100 y 50) | `AiAssistantLimits.cs:31-34` | a decidir: Configuración › IA con el valor de `appsettings` como techo, porque el negocio paga su propia clave |

### 6. Núcleo

| Regla | Hoy | Dónde está | Dónde iría |
| --- | --- | --- | --- |
| Invitaciones por correo por administrador y día | 100 | `Email:DailyInvitationLimitPerAdministrator`, `EmailQuotaOptions.cs:46` | Configuración general, zona nueva «Registro e invitaciones» |
| Lugares del cupo diario de correo reservados para invitaciones | 50 | `Email:DailyInvitationReserve`, `EmailQuotaOptions.cs:30` | la misma zona; se valida contra `Email:DailyLimit`, que sigue en `appsettings` |
| Nombre de la aplicación en correos, invitaciones y PDF | «Acceso» | `Email:AppName`, `EmailOptions.cs:13` | Configuración general › Apariencia |
| Logo de los correos | vacío | `Email:LogoUrl`, `EmailOptions.cs:17` | Apariencia |
| Nombre del remitente | «Acceso» | `Email:Smtp:FromName`, `SmtpOptions.cs:29` | Apariencia (la cuenta y la dirección siguen en `appsettings`) |

Para decidir: una instalación nueva arranca con el registro abierto, porque `appsettings.json` pone `Registration:Mode` en
`Open` aunque el valor de la clase (`RegistrationOptions`) sea `InviteOnly`; sólo lo usa el seeder.

### 7. ARCA: funciones que faltan

No son constantes: hoy no existen. El aviso al dueño antes de que venza el certificado (`ArcaCertificate.ExpiresWithin` sólo
lo usan los tests), con «Avisar N días antes» en Configuración › ARCA (30 por defecto), y la moneda de los comprobantes, hoy
siempre pesos (`ArcaVoucherRequestPolicy.cs:19`), que necesita la cotización de ARCA y el PDF.

## Lo que queda donde está

- **Proveedor o norma.** De Meta: la ventana de 24 h, 6 s entre mensajes a la misma persona, los largos (1024 y 4096), tres
  botones de 20 caracteres, listas de 10 filas y 10 renglones por Flow. De ARCA: la ventana de fechas del comprobante (5 y 10
  días) y los importes. De OpenAI: 25 MB de audio y sus formatos. Del correo: `Email:DailyLimit` (400, por el cupo de la cuenta).
- **Técnico.** Leases, polling, lotes, timeouts, reintentos con espera, presupuestos de bytes y del motor, y los topes de
  capacidad de un formulario o un catálogo (40 preguntas, 10.000 filas, 20 campos).
- **Seguridad, costo o abuso** (en `appsettings`). El código de ingreso (10 minutos, 5 intentos, 60 s entre reenvíos, bloqueo de
  15 minutos), los cupos de códigos por WhatsApp (100, 10 y 20 por día) y por correo, los rate limits y el filtro de datos
  inventados en las respuestas redactadas.

Encontrado de paso, sin ser de Configuración: los rate limits de Formularios (`BusinessFormsApiRegistration.cs`), la cookie de
sesión y los tokens (30 días, 15 minutos y 5 minutos, en `IdentityRegistration.cs`, `AuthServerDefaults.cs` y
`OpenIddictRegistration.cs`) y la espera de un minuto entre dos invitaciones (`UserInvitation.cs:19`) están fijos en el código;
convendría pasarlos al menos a `appsettings`.

## Cómo se haría

Como TO1-A, por grupo y con su tablero antes de tocar una pantalla:

- cada valor en la configuración de su dueño, con su valor por defecto, revisión y 409 ([ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md));
- se lee de la base en cada uso, no al arrancar;
- lo guardado antes del cambio lee el valor por defecto, también lo que escriba la versión anterior mientras conviven;
- un texto que nombra el número lo recibe como parámetro;
- la política de privacidad lee los mismos plazos.

## Decisiones para el usuario

- **RC1.** Retención y privacidad: ¿pasan a Configuración, cada módulo en la suya?
- **RC2.** Asistente de WhatsApp: ¿la ráfaga, las cargas por planilla, las páginas y el emparejado van a Límites? ¿Y las dos
  reglas que hoy no son constantes?
- **RC3.** WhatsApp en general: ¿una Configuración › WhatsApp con la vida del formulario y los países?
- **RC4.** Formularios: ¿una Configuración › Formularios con el tamaño de archivo, las relecturas y los grupos, y las dos
  reglas de aprobación en cada formulario?
- **RC5.** IA: el día de los cupos ya usa la zona horaria del negocio (decidido y programado el 2026-11-22). Queda: ¿los cupos
  por persona en pantalla, con techo?
- **RC6.** Núcleo: ¿invitaciones, nombre, logo y remitente en Configuración general? ¿Con qué modo de registro arranca una
  instalación nueva?
- **RC7.** ARCA: ¿el aviso de certificado por vencer y la moneda, como funciones nuevas?

Orden sugerido: RC1 (la política de privacidad promete esos plazos),
RC2 (mismo patrón que TO1-A), RC6, RC3, RC4 y RC7. Desde el 2026-11-15 la lista está congelada hasta la prueba integral: lo
que el usuario no pida antes va después.
