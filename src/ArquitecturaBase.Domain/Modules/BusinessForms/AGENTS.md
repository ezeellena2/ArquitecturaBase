# BusinessForms: valores puros del borrador

Leer [regla funcional](../../../../docs/features/formularios.md) antes de modificar. Sólo revisión e identidad/tipo de preguntas; no hay entidades ni publicación implementadas.

- Domain depende sólo de BCL. No introducir Meta, JSON de canal, persistencia o referencias a WhatsApp.
- Revisión esperada vieja falla sin avanzar. Mismo tipo conserva FieldId; cambio y copia generan UUID v7. Sólo los seis tipos manuales están habilitados; Group/desconocidos fallan.
- ChoiceId, ownership y guardado canónico siguen pendientes; no inferirlos de estas fábricas.
- Mantener TDD en [FormDraftModelTests](../../../../tests/ArquitecturaBase.Domain.UnitTests/Modules/BusinessForms/FormDraftModelTests.cs). Las propuestas viven en [contrato](../../../../docs/plans/formularios-etapa-1-contrato.md).

Preguntas/opciones/snapshots puros localizados: copian mapas es/en y colecciones readOnly/Frozen. Preservan identidades y ledger retirado al renombrar, traducir o reordenar; no generan UUID ni tocan persistencia.
