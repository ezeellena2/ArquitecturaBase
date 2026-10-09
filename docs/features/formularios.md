# Formularios: modelo puro inicial

Estado implementado al 2026-10-09: sólo valores puros del borrador en `Domain/Modules/BusinessForms`. No hay formulario persistido, endpoints ni integración WhatsApp. El [contrato de etapa 1](../plans/formularios-etapa-1-contrato.md) sigue como propuesta; no convierte capacidades pendientes en reglas productivas.

`FormDraftRevision` parte de 1. Advance exige revisión esperada igual a la actual, devuelve la siguiente y conserva el valor anterior; si difiere, devuelve Forms.Draft.RevisionConflict sin avanzar. No implementa lock ni validación de autorización/persistencia.

`FormQuestionIdentity` crea FieldId UUID v7 para ShortText, LongText, Number (decimal), Date, Boolean y Choice. Group y valores desconocidos devuelven Forms.Draft.QuestionTypeUnavailable. Mantener tipo conserva instancia/identidad; cambiar tipo crea identidad nueva; copiar conserva tipo con identidad nueva. No contiene etiquetas, opciones, JSON ni ChoiceId y no prueba pertenencia al borrador. El servidor deberá usar estas reglas al guardar; esa coordinación todavía no existe.

Errores declarados en Domain y recursos de errores es/en. Sin eventos de dominio, caché, EF ni referencias al módulo WhatsApp. [Pruebas del modelo](../../tests/ArquitecturaBase.Domain.UnitTests/Modules/BusinessForms/FormDraftModelTests.cs) y [evidencia de revisión](../reviews/2026-10-09-formularios-incremento-0.md) delimitan la validación.
