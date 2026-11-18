# BusinessForms: borde HTTP

Leer [formularios](../../../../docs/features/formularios.md) y el [resumen de la etapa en curso](../../../../docs/plans/formularios/README.md).

Controllers MVC y contratos de entrada del módulo, bajo `api/forms`. Los casos de uso están en Application; acá se adaptan rutas, permisos, límites y respuestas. `BusinessFormsApiRegistration.cs` registra sus políticas de rate limit: `forms-read`, `forms-write`, `forms-files-read`, `forms-meta-read` (vista oficial, F17) y `forms-export`, con dos a la vez por copia (F11).

- `Controllers/`: un controller por familia, con sus rutas, permisos, políticas y respuestas ([guía](Controllers/AGENTS.md): borrador, publicación, vista previa, baja, eliminación, catálogos y el flujo de una respuesta).
- `Contracts/`: cuerpos `*HttpRequest`, queries `*Query`, respuestas HTTP y los mapeos que comparten varias rutas ([guía](Contracts/AGENTS.md), con la vista previa de destinatarios y la carga desde la app).
- Las demás familias están en el [detalle por tema](../../../../docs/features/formularios-borde-http.md): respuestas, archivos y corrección; reglas, efectos, aprobaciones y PDF; sugerencias, pedidos faltantes, datos de personas y el asistente.

## Al modificar

- Seis rutas de borrador MVC; esos controllers sólo IFormDraftService. Contratos HTTP propios mapeados a mano y ToString redactado.
- Una familia puede tener reglas en las dos guías y en el detalle por tema: leelas antes de tocarla y actualizá cada regla donde está.

## Referencias

- [Borde HTTP](../../../../docs/architecture/backend.md#borde-http) y [módulos opcionales](../../../../docs/architecture/backend.md#módulos-opcionales).
- [Pruebas de integración del módulo](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/AGENTS.md).
