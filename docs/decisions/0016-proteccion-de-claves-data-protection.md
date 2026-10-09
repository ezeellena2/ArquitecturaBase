# 0016 — Protección de claves Data Protection

**Estado:** Aceptada, 2026-10-03.

## Contexto

La persistencia del key ring en PostgreSQL permitía compartir cookies entre réplicas, pero una copia de `DataProtectionKeys` incluía secretos en texto plano.

## Decisión

Cifrar las claves con un PFX externo común a las réplicas; exigirlo fuera de Development Windows y Testing. En Windows local se admite DPAPI del usuario. El seed cifra las claves antiguas conservando sus identificadores.

## Consecuencias

Restaurar exige base y material criptográfico respaldados por separado. Rotar conserva certificados anteriores mientras las claves o respaldos los necesiten. Las copias antiguas sin cifrar no se vuelven seguras retroactivamente. El bundle puede construirse sin secretos; el host debe tenerlos antes de arrancar.

## Alternativas descartadas

Confiar sólo en acceso a PostgreSQL; claves efímeras en producción; incluir un certificado privado en Git o en la imagen.

Detalle operativo: [claves de Data Protection](../guides/despliegue.md#data-protection).
