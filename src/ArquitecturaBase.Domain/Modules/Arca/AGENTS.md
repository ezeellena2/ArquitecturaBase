# ARCA: módulo opcional

Permisos, entidades y errores puros del módulo ARCA. Hay una carpeta por familia de servicios: `Connection` (conexión, certificado y ticket; en delegación, la conexión guarda la CUIT de la plataforma, que firma) y cada familia (`Registry`, `Invoicing` y las que lleguen). Las clases `*Errors` de todas van en la raíz, porque ahí las exige la regla de errores. Una familia usa sólo `Connection` y la raíz; `Connection` no nombra a ninguna. Sin dependencias, reloj global ni eventos.

Hereda las instrucciones del proyecto. Sus decisiones son el [ADR 0023](../../../../docs/decisions/0023-arca-como-modulo-opcional.md), el [ADR 0025](../../../../docs/decisions/0025-familias-de-servicios-arca.md) y el [ADR 0027](../../../../docs/decisions/0027-delegacion-guiada-arca.md); el diseño vigente, [ARCA · Etapa 1](../../../../docs/plans/2026-11-03-arca-etapa-1-conexion.md). No importar otros módulos. Actualizar mapa y fuentes con un cambio de responsabilidad.
