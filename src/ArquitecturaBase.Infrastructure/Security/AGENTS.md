# Security: generación y protección de secretos

Implementa generación segura de tokens y códigos, y su hash con opciones de configuración. El ciclo funcional de emisión y consumo está en Application.

## Al modificar

- Usá generación criptográfica y el hash definido por el puerto. No sustituir por aleatoriedad de simulación ni guardar códigos en claro.
- Las claves se configuran por secretos o entorno; mantené validación al arrancar. La excepción local HMAC está documentada en la raíz.
- Nunca registrar códigos, tokens, claves ni enlaces. Los consumidores deben verificar propósito, destino, vencimiento e intentos en su área.
- Probá el comportamiento del adaptador y las garantías funcionales con las pruebas de identidad.

## Referencias

- [Códigos y seguridad](../../../docs/features/identidad.md).
- [Pruebas de acceso](../../../tests/ArquitecturaBase.Api.IntegrationTests/Auth).
- [Coordinación de emisión y verificación](../../../tests/ArquitecturaBase.Application.UnitTests/Services/Auth).
