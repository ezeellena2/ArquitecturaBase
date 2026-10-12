# BusinessForms: garantías reales

Leer [formularios](../../../../docs/features/formularios.md). Usar ApiFactory, PostgreSQL/Redis reales y scopes propios.
- Verificar seis rutas/inventario, permisos y actor, snapshot anterior, retired IDs/traducciones y transacción única.
- Carreras coordinadas con lock PostgreSQL y dos hosts; comprobar espera, ganador/replay y cantidad de recibos.
- FakeTimeProvider para expiración. Restaurar anillo cifrado de PostgreSQL con certificado de test; propósito/actor equivocados no abren payload.
- Medir 40x50 y tombstone indicando qué costos excluye. Código sólo para pruebas en TestFeatures, sin proveedores externos.

- Chequeo: sesión/Read+Publish sin Manage, revisión e identidad guardadas, cuerpo cerrado, errores traducidos, ausencia real del adaptador y ausencia de escrituras. Mantener recorridos de seis rutas; D5 actualiza name a mapa y admite secundarios pendientes, sin contratos legados ficticios.
- Usar actores propios por escenario para no acumular el rate limit real entre pruebas. No subir cuotas ni quitar middleware para lograr verde. OpenAPI/inventario incluyen la consulta y aceptación/status.

D5: mapas en todas las respuestas/comprobantes, vaciados explícitos/replay/IDs, búsqueda/orden/facetas por idioma UI, migración inicial sobre esquema anterior/base vacía y Down. No usar la base AppHost ni inventar datos v1 históricos.

2b mínimo: aceptación/replay tras edición/expiración, rollback técnico sin Fields/Version/Operation/prefix parciales, actor/token/permisos, dos hosts y una operación abierta. El canal ReadyStructuredFormChannel sólo vive en TestFeatures: no acredita Meta real.

2b prueba activación preservando borrador, CAS/fences/replay, otro host y ambos workers reales con proveedor de TestFeatures: es/en, upload con errores, caída después de Create y revocación. URLs de preview/estado sin secretos; inventario/OpenAPI diez rutas. Sin red Meta; workers automáticos apagados y ejecución controlada por operación.

Mínimo2: recarga propia/ajena y revocación Publish; última operación sin borrar activa anterior/listado/facetas. Preview es/en independiente de Accept-Language, cultura ausente/ajena400, expiración sin URL en canal real y guard neutral, renovación sólo por GET nuevo.

La captura de mínimo 3 usa PostgreSQL/Redis reales para verificar valores tipados, recibo idempotente, conflicto de replay y pertenencia compuesta del FieldId. No usa el sink/source ficticio de los tests HTTP de WhatsApp.

Catálogo Manual: pruebas HTTP con PostgreSQL real cubren alta positionless, rechazo de Position enviado por cliente, secuencias de movimientos entre páginas, unicidad final y auditoría; un movimiento fuera del límite revierte metadatos/orden.
