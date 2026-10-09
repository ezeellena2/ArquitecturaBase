# Validación de acceso y atención por WhatsApp — 2026-10-02

Implementación local en `main` de ambos repositorios, sin ramas, commits ni push.
Se conservaron los cambios previos y concurrentes. Las decisiones vigentes están en
[ADR 0011](../decisions/0011-separacion-acceso-y-atencion-whatsapp.md),
[WhatsApp](../features/whatsapp.md) e [identidad](../features/identidad.md).

## Evidencia automática

| Comprobación | Resultado |
| --- | --- |
| Backend: `dotnet build ArquitecturaBase.slnx --artifacts-path .test-artifacts/full` | 0 advertencias, 0 errores. |
| Backend: `dotnet test --artifacts-path .test-artifacts/full` | 2057 correctos, 0 fallidos, 0 omitidos. Incluye todos los proyectos. |
| Front: `npm run build` | Correcto. |
| Front: `npm run lint` | Correcto; 38 pruebas de arquitectura. |
| Front: `npm run test` | 829 correctos en 78 archivos. |
| Front: Playwright, suite visual completa | 57 correctos. |
| Ambos repositorios: `git diff --check` | Sin errores. |

La integración usó Docker con PostgreSQL 18.3 y Redis 8.6 reales; Meta fue simulado.
Los artefactos del backend se aislaron para evitar los DLL de una instancia ajena.
Los scripts de npm se ejecutaron mediante su CLI de Node porque el wrapper de la máquina
fallaba. Playwright usó una configuración temporal equivalente sobre Vite en el puerto
5193, sin modificar la configuración habitual. Se retiró esa configuración y se apagó
el Vite propio. No se levantó Aspire ni se cerraron procesos ajenos.

Los logs locales están en `.test-artifacts/access-*.log` y, en el front,
`test-results/access-*.log`; son artefactos ignorados por Git.

## Regresiones comprobadas

- TDD: primero fallaron los casos de mensaje común sin efectos de identidad, botón de
  acceso en Manual, default Manual, conversión de chats existentes y ocultación de
  secretos/acción de activar el bot en el front; pasaron después del refactor.
- Los mensajes comunes no crean cuentas, confirman teléfonos ni emiten enlaces de acceso.
  Solo los identificadores estructurados históricos disparan orientación pública.
- Login explícito en Manual, códigos separados de verificación del teléfono e invitación
  con `WANT_TO_ENTER`; no se infiere autenticación desde texto libre.
- Migración desde la anterior: conserva mensajes, vínculos, revisiones, lecturas, receipts,
  enlaces pendientes y operaciones Pending/Sending/Uncertain. Default y chats existentes
  quedan en Manual; el modelo no tiene cambios pendientes de migración.
- Idempotencia, permisos, resultados inciertos, receipts tempranos, paginado y locks;
  acceso y atención comparten la exclusión Redis por destinatario entre dos hosts.
- Historia y previews de acceso sin códigos ni URL secreta. La presentación reutiliza
  el chat existente, con estados y hora; pruebas es/en a 1440, 390 y 320 px, además de
  borradores y posición de lectura durante paginado/polling.

## Verificación pendiente y límites

No se realizó una prueba con teléfono real, no se verificó la configuración externa de
Meta ni se acredita despliegue. La [guía local](../guides/whatsapp-en-local.md) describe
los pasos para ejecutarla con el titular; el pendiente está en
[verificación y producción](../plans/verificacion-y-produccion.md).

La cola de acceso existente sigue en memoria; este refactor no la convierte en una
bandeja durable. La atención manual conserva su persistencia y tratamiento de resultados
inciertos. Los mensajes históricos sin evidencia estructurada conservan `Unclassified`.

El plan aprobado estaba sin versionar y sin copia en Git. Se conserva con estado actualizado:
la [guía documental](../guides/auditar-documentacion.md) dispone que «Un archivo sin versionar
y sin otra copia no se elimina por asumir que está terminado». Sus decisiones ya quedaron
consolidadas en las fuentes canónicas.
