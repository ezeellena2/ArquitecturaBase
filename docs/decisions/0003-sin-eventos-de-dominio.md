# 0003. Sin eventos de dominio

**Estado:** Aceptada, 2026-09-26.

**Origen:** decisión D3 del [plan maestro](../plans/2026-09-26-plantilla-estandar-por-etapas.md), que sale del code review del 2026-09-26. Corresponde a la tarea 1 de la Etapa 7 y se aplicó en el commit `cbff737`.

## Contexto

Domain tenía `AggregateRoot`, que acumulaba eventos de dominio, e `IDomainEvent`. Cinco entidades heredaban de `AggregateRoot` (`LoginCode`, `LoginLink`, `UserInvitation`, `WhatsAppContact` y `WhatsAppMessage`), pero ninguna levantaba eventos y nada los despachaba al guardar. La pieza prometía un comportamiento que no existía, y eso confunde a una persona o a una IA que lee el código para copiarlo.

Despacharlos pediría handlers, y el usuario los rechazó para la arquitectura: la [arquitectura canónica](../architecture/backend.md) es controllers → servicios → repositorios y lectores, sin handlers.

## Decisión

**Se quitan `AggregateRoot` e `IDomainEvent`.** Las entidades heredan de `Entity`.

## Consecuencias

- Las cinco entidades, y el `Widget` de los tests, heredan de `Entity`. El modelo de EF no cambia: los eventos se exponían con métodos y nunca se mapearon.
- Lo que un caso de uso tiene que provocar en otra área lo coordina su servicio de forma explícita, como el resto de la arquitectura.
- Si un proyecto derivado necesita eventos de dominio, los agrega con su despachador y sus tests, y escribe un ADR que reemplace a este.

## Alternativas descartadas

- **Dejarlos sin uso.** Siguen prometiendo un mecanismo que no existe.
- **Despacharlos al guardar.** Pide handlers, que el usuario rechazó para la arquitectura.
