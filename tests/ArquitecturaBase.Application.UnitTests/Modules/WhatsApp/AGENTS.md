# WhatsApp: tests de Application.UnitTests

Prueba clasificación entrante, orientación pública, códigos, perfil y canales con dobles de los puertos del módulo.

## Al modificar

- Conservar IDs de botones históricos, historial sin credenciales y acceso en Manual. La migración preserva filas, relaciones, lecturas, receipts y envíos; los tests usan Meta simulado.

- Probá cuotas, consentimiento, emisión y verificación sin invocar Meta ni otros proveedores.
- Conservá el reparto entre el límite del servicio y los helpers que no guardan.

## Referencias

- [Reglas funcionales del módulo](../../../../docs/features/whatsapp.md).
- [Independencia del núcleo](../../../../docs/guides/quitar-whatsapp.md).

- Compilador: seis controles, bytes/hash/fixtures sintéticos, orden/culturas/textos completos sin fallback, límites UTF-16 y presupuestos, required/ausencia/false y decimal exacto (overflow/escala/gramática).
- Canal: registro idempotente, cancelación y evidencia externa Pending separada de compatibilidad local. No simular que un fixture fue aceptado por Meta.
