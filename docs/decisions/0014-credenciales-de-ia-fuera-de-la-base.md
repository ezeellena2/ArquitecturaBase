# ADR 0014: credenciales de IA con anillo externo

Fecha: 2026-10-02. Estado: aceptada e implementada en esta entrega.

## Contexto

Una clave de API compartida puede generar consumo facturable. Data Protection de esta
plantilla guarda sus claves en PostgreSQL: usarlo sin otra protección haría que el backup
de la base incluyera todo lo necesario para descifrar la clave del proveedor.

## Decisión

El módulo usa AES-256-GCM con envelope versionado, keyId, nonce aleatorio y autenticación
de instalación/propósito/proveedor. El anillo externo se provisiona por user-secrets en Development
y gestor de secretos/variables protegidas al desplegar. HMAC-SHA256 de comprobantes usa
una clave derivada por HKDF y otro propósito. No se guarda clave en claro, digest simple,
anillo ni plaintext en la base, logs o respuestas. Fallas de descifrado son explícitas.

## Consecuencias

Rotar cambia ActiveKeyId conservando claves históricas. Backups de datos y anillo deben
restaurarse conjuntamente; clones independientes usan instalación/anillo nuevos.
Reemplazar la credencial la cifra con la clave activa. No hay recifrado automático ni
worker sin consumidor. Una clave se retira sólo cuando no la necesitan filas, comprobantes
vigentes ni backups retenidos. No protege contra un atacante con acceso al proceso y
su gestor de secretos. User-secrets es almacenamiento local de desarrollo, no un vault.

## Alternativas descartadas

- Data Protection actual o anillo en la base: no separa el riesgo del backup.
- Clave cifradora hardcodeada o regenerada al arrancar: filtra secretos o pierde restauración.
- Cifrar sin autenticación o usar hash de la clave para deduplicar: permite alteración
  o deja un verificador susceptible a ataques fuera de línea.
