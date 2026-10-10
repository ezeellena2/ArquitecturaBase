# BusinessForms: garantías reales

Leer [formularios](../../../../docs/features/formularios.md). Usar ApiFactory, PostgreSQL/Redis reales y scopes propios.
- Verificar seis rutas/inventario, permisos y actor, snapshot anterior, retired IDs/traducciones y transacción única.
- Carreras coordinadas con lock PostgreSQL y dos hosts; comprobar espera, ganador/replay y cantidad de recibos.
- FakeTimeProvider para expiración. Restaurar anillo cifrado de PostgreSQL con certificado de test; propósito/actor equivocados no abren payload.
- Medir 40x50 y tombstone indicando qué costos excluye. Código sólo para pruebas en TestFeatures, sin proveedores externos.

-
