# 0007. WhatsApp como módulo opcional

**Estado:** Aceptada, 2026-09-26; implementada el 2026-09-29.

**Origen:** decisión D7 del [plan maestro](../plans/2026-09-26-plantilla-estandar-por-etapas.md), que sale del code review del 2026-09-26. Se implementó en la Etapa 6, cerrada el 2026-09-29, con su [plan detallado](../history/plans/2026-09-28-etapa-6-whatsapp-modulo.md) en diez tandas: los tests de arquitectura que reconocen `Modules/<M>` y la frontera (`fa2a760`); la mudanza a `Modules/WhatsApp` (`0e57894`); un registro por capa y los controllers del módulo (`5e17eef`, `8afff8e`); los cuatro puertos del núcleo en `Application/Interfaces/Channels`, que son `IPhoneChannel` (`6b51793`), `IInvitationChannel` e `IInvitationDeliveryStatusSource` (`acbc8e8`) e `IPhoneLinkParticipant` (`c7f68c8`); los códigos por teléfono y su tope diario al módulo (`c19b642`); `LoginCodeChannel.Phone` y `ProviderMessageId`, con sus migraciones (`77522e9`); los tests del núcleo sin el módulo (`f869d74`, `5f63e09`); y la [guía para quitarlo](../guides/quitar-whatsapp.md), probada con la prueba de fuego (`59ad558`, `424a371` y `e539284`). La frontera la verifica `ModuleBoundaryTests`. El contexto que sigue describe el estado anterior.

## Contexto

La plantilla tiene que servir también para proyectos que no usan WhatsApp, y hoy no se lo puede quitar sin tocar unas 40 piezas. El code review lo encontró entrelazado con Auth y Users:

- los códigos de ingreso (`LoginCode`) conocen el canal de WhatsApp;
- el perfil y la administración, al cambiar un número, sueltan el contacto de WhatsApp e invalidan sus enlaces;
- una invitación puede salir por WhatsApp;
- Infrastructure registra servicios de Application del webhook (`WhatsAppRegistration.cs`).

## Decisión

**WhatsApp es un módulo opcional dentro del mismo repo**, con un registro propio, puertos hacia el núcleo y una guía para quitarlo.

El diseño concreto se escribe como spec al arrancar la Etapa 6, porque tiene decisiones abiertas. El plan maestro trae un borrador de enfoque: los puertos que el núcleo expondría, las carpetas del módulo, un registro por capa y qué pasa con las migraciones.

## Consecuencias

- Un proyecto sin WhatsApp lo quita borrando carpetas y una línea de registro (lo que de verdad hay que borrar está en las Enmiendas), con el build y los tests en verde. La Etapa 6 lo comprueba con una prueba de fuego en una copia descartable.
- El núcleo no puede referenciar el módulo, y un test de arquitectura lo va a verificar.
- Es un cambio de estructura: las reglas funcionales de WhatsApp (en [`docs/features/whatsapp.md`](../features/whatsapp.md) y en el [spec de WhatsApp](../specs/2026-09-22-ingreso-whatsapp-design.md)) siguen vigentes.
- Es la etapa más grande y de riesgo alto. Necesita que antes estén las interfaces por responsabilidad (Etapa 3), y conviene hacerla después de la documentación en capas (Etapa 5), para que su documentación ya tenga dónde vivir.

## Alternativas descartadas

- **Sacarlo a otro repositorio.** Es prematuro: lo que falta es poder quitarlo, no separarlo.
- **Dejarlo como está.** No se puede quitar sin tocar unas 40 piezas.

## Enmiendas

**Enmienda (2026-09-29, al cerrar la Etapa 6):** el diseño no se escribió como spec: lo fijó el [plan detallado de la Etapa 6](../history/plans/2026-09-28-etapa-6-whatsapp-modulo.md), con las nueve decisiones del usuario del 2026-09-28 (siete en su sección 2.1 y dos en la 9.2), y su sección 2.2 corrigió el borrador del plan maestro. Lo que cambió respecto de lo que dice este ADR:
- **Los puertos** son cuatro y no los tres del borrador (`IInvitationChannel`, `ILoginCodeChannel`, `IPhoneLinkObserver`), que dejaban afuera el país permitido, el tope diario, el estado de entrega y el orden de los locks (un observador no puede tomar locks antes que el núcleo): `IPhoneChannel`, `IPhoneLinkParticipant`, `IInvitationChannel` e `IInvitationDeliveryStatusSource`. El pedido de códigos por teléfono y su tope diario pasaron enteros al módulo.
- **El registro** son tres métodos con nombre propio, uno por capa (`AddWhatsAppApplication()`, `AddWhatsAppInfrastructure(configuration)` y `AddWhatsAppApi()`), en un bloque de `Program.cs`; así Infrastructure dejó de registrar servicios de Application (lo hacía `WhatsAppRegistration.cs`).
- **Las carpetas** son `Modules/WhatsApp` en los cuatro proyectos, también en Domain, para que un solo patrón (`*.Modules.WhatsApp`) cubra la frontera. `ApplicationDbContext` ya tomaba todas las configuraciones del ensamblado: al borrar la carpeta, las entidades salen del modelo solas.
- **Las migraciones:** las tablas del módulo no estaban en la migración inicial. Las crea `20260923174423_WhatsAppMessages`, y `20260923214525_WhatsAppInboundProcessing` les agrega un índice filtrado; las dos nombran las entidades con cadenas, así que compilan sin el módulo y quedan en la cadena. Quitarlo genera una migración propia, `RemoveWhatsApp`, que la plantilla no trae. Además se migraron dos valores guardados: `LoginCodes.Channel` pasó de `'WhatsApp'` a `'Phone'` (`LoginCodePhoneChannel`) y `UserInvitations.WaMessageId` a `ProviderMessageId` (`UserInvitationProviderMessageId`).
- **Quitarlo no es "borrar carpetas y una línea de registro"** (Consecuencias): son las ocho carpetas `Modules/WhatsApp` (cuatro de `src` y cuatro de `tests`), el bloque de `Program.cs` con sus tres `using` marcados, la sección `WhatsApp` y dos claves de `RateLimiting` de los `appsettings`, la documentación del módulo y la migración `RemoveWhatsApp`, que en una base desplegada puede pedir dos despliegues. Lo dice la [guía](../guides/quitar-whatsapp.md), y la prueba de fuego lo comprobó siguiéndola.
- **El test de la frontera** (`ModuleBoundaryTests`) lee el código fuente y no el IL, porque una constante, un `cref` o un `nameof` no dejan rastro en el IL y rompen la compilación al borrar el módulo; deja afuera lo que genera EF en las migraciones.
- **Queda en el núcleo, a propósito:** el enlace de ingreso (`LoginLink`), `UserInvitationChannel.WhatsApp`, `LoginMethod.WhatsAppCode` y `WhatsAppLink`, `DELETE /api/users/{id}/whatsapp` y, desde el 2026-09-30, `DELETE /api/me/whatsapp`, los campos `whatsapp*` de `login-methods` y seis claves de los `.resx`: son valores guardados o del contrato con el front, que no cambió.

**Enmienda (2026-09-30):** la desvinculación del número propio pasa al núcleo (`MeController` → `IProfilePhoneService` → `ProfilePhoneService`), sin cambiar su contrato HTTP, los locks ni las sesiones. El participante del módulo sigue soltando el contacto del chat; sin el módulo, se quita el número y se anulan los enlaces igual. El pedido de código (`POST /api/me/whatsapp/code`) y la confirmación (`PUT /api/me/whatsapp`) siguen en el módulo. Así queda exacta la decisión 9 de la Etapa 6: las cuentas que ya tienen número lo muestran y lo pueden desvincular también desde el perfil.
