# 0007. WhatsApp como módulo opcional

**Estado:** Aceptada, 2026-09-26.

**Origen:** decisión D7 del [plan maestro](../plans/2026-09-26-plantilla-estandar-por-etapas.md), que sale del code review del 2026-09-26. Se implementa en la Etapa 6.

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

- Un proyecto sin WhatsApp lo quita borrando carpetas y una línea de registro, con el build y los tests en verde. La Etapa 6 lo comprueba con una prueba de fuego en una copia descartable.
- El núcleo no puede referenciar el módulo, y un test de arquitectura lo va a verificar.
- Es un cambio de estructura: las reglas funcionales de WhatsApp (en `CLAUDE.md` y en el [spec de WhatsApp](../specs/2026-09-22-ingreso-whatsapp-design.md)) siguen vigentes.
- Es la etapa más grande y de riesgo alto. Necesita que antes estén las interfaces por responsabilidad (Etapa 3), y conviene hacerla después de la documentación en capas (Etapa 5), para que su documentación ya tenga dónde vivir.

## Alternativas descartadas

- **Sacarlo a otro repositorio.** Es prematuro: lo que falta es poder quitarlo, no separarlo.
- **Dejarlo como está.** No se puede quitar sin tocar unas 40 piezas.
