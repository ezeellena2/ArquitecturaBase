# Reglas fijas a Configuración: lo que queda

Inventario del 2026-11-22, condensado el 2026-11-26. Lo decidido y programado (TO1-A y RC1-A a RC7-B, en `main` el 22 y el
23/11) está en la [regla general](../architecture/backend.md#reglas-de-negocio-en-configuración-2026-11-22) y en cada feature;
el inventario completo y las decisiones se recuperan desde la [procedencia](../history/README.md#cierre-del-2026-11-26).

## Para decidir

- **Los cupos de la IA por persona, en pantalla** (RC5, O35): consultas y lecturas por persona y día (10 y 10; por
  instalación, 100 y 50, `AiAssistantLimits`) siguen en `appsettings`. Propuesta: Configuración › IA con el valor de
  `appsettings` como techo, porque el negocio paga su propia clave.

## Propuestas sin decidir

- **Duraciones fijas en el código, al menos a `appsettings`:** los rate limits de Formularios
  (`BusinessFormsApiRegistration.cs`), la cookie de sesión y el refresh token (30 días, `IdentityRegistration.cs` y
  `AuthServerDefaults.cs`), el código de autorización y el access token (5 y 15 minutos, `OpenIddictRegistration.cs`) y un
  minuto entre dos invitaciones (`UserInvitation.cs`).
- **La moneda de los comprobantes de ARCA** (RC7-B): hoy siempre pesos (`ArcaVoucherRequestPolicy.Pesos`); espera a que un
  cliente la pida, porque necesita la cotización de ARCA y el PDF.
