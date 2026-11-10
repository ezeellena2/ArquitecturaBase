# ADR 0029: la biblioteca de estándares, aportada por los módulos como datos

## Estado

**Aceptada**, 2026-11-09, con la decisión BI1-A del usuario en la [etapa 2 de ARCA](../plans/2026-11-09-arca-etapa-2-motor.md)
y la del [plan de documentos](../plans/2026-10-22-documentos-por-whatsapp.md) (la biblioteca son datos de inicio y «Agregar»
crea una copia editable que recuerda de qué estándar viene). Programada en e2cb8606.

## Contexto

Un estándar como la factura lo necesita un módulo (ARCA emite con sus datos), pero el formulario es de formularios: se edita,
se publica y se responde como cualquier otro. Los módulos opcionales no se nombran entre sí, y una copia tiene que seguir
sirviendo al módulo aunque la empresa la edite: si borra el total o lo cambia de tipo, la emisión no tiene de dónde sacarlo.

## Decisión

1. **Cada módulo aporta sus estándares como datos** por un puerto neutral del núcleo, `IStructuredFormStandardSource`
   (colección opcional, `TryAddEnumerable`): clave estable, versión, nombre y descripción por idioma, prefijo sugerido, el
   formulario en el formato del borrador (con identificadores locales UUID para que cálculos, condiciones y filtros se nombren
   entre sí), qué pregunta guarda cada dato que el módulo necesita y los catálogos por su `Key`. No hay código por estándar:
   uno nuevo es otra entrada de datos. Sin fuentes, la biblioteca está vacía y no se muestra.
2. **«Agregar» crea un borrador propio por el guardado de siempre** (`FormDraftService`), con el pedido del cliente como
   comprobante, y la definición sella una vez el estándar, su versión y `dato → FieldId`. Se puede agregar más de una vez.
3. **Las preguntas atadas** (las que guardan un dato) conservan todo menos su texto, su ayuda y su lugar: el guardado del
   borrador rechaza quitarlas o cambiar su tipo, su bloque, su catálogo, sus opciones, su cálculo o si son obligatorias
   (`Forms.Draft.BoundQuestionChanged`). El módulo lee la respuesta por los `FieldId` sellados.
4. **Un catálogo se nombra por su `Key`**: si falta o está inactivo, la copia no se crea (`Forms.Library.CatalogUnavailable`).
   Un estándar puede traer catálogos propios que la gente edita (como «Clientes»): se crean vacíos con su `Key` y sus campos en
   la misma transacción que la copia, si todavía no existen.

## Consecuencias

- La etapa 3 aporta «Factura» como datos desde el módulo ARCA y lee sus respuestas por los datos sellados, sin conocer cómo
  la empresa renombró o reordenó las preguntas.
- Una versión nueva de un estándar no cambia las copias existentes: actualizar una copia a otra versión queda fuera hasta que
  haya un caso.
- Quitar el módulo que aportó un estándar deja las copias como formularios comunes; sus preguntas siguen atadas, porque el
  sello está en la definición.
- **Multitenant:** la biblioteca es la misma para todas las organizaciones; las copias son de cada una.

## Alternativas descartadas

- **Estándares en código, uno por clase:** cada estándar nuevo sería un cambio de código, contra «genérico» del plan de
  documentos.
- **Tablas de estándares en la base, sembradas:** duplica lo que ya traen los módulos instalados y exige migrar datos al
  cambiar un estándar.
- **Copias que no se pueden editar:** la empresa no podría adaptar los textos ni sumar preguntas propias, que BI1-A permite.
