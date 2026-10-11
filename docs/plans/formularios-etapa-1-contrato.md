# Formularios, etapa 1: contrato de crear y guardar

**Decisión humana vigente D5, 2026-10-10:** BusinessForms no fue commiteado ni desplegado y no hay borradores/comprobantes reales que preservar. Se adapta directamente el mismo contrato: name mapa es/en, igual que los demás textos; guardar incompleto, publicar ambos completos sin fallback. **Sin Asp.Versioning/ADR, nameByCulture ni compatibilidad legada ficticia.** Se regenera AddBusinessFormDrafts inicial no commiteada; no otra migración ni update de bases locales. [Contrato mínimo D5 para coordinación](../reviews/2026-10-10-formularios-d5-contrato.md). Implementación autorizada en curso; resultados de build/tests se registran al cierre.

Estado: contrato K1–K9 implementado en backend desde 2026-10-09 y adaptado por D5 el 2026-10-10. Mismos recursos, seis rutas de borrador más chequeo local; mismas identidades, permisos, límites, privacidad, UoW y recuperación30d. [Fuente funcional](../features/formularios.md). La aprobación anterior de fallback y la propuesta técnica v1/v2 quedaron reemplazadas por esta decisión; no gobiernan la implementación actual.

Se continúa el [plan original](2026-10-07-formularios-dinamicos-y-whatsapp-flows.md) y el [arranque](formularios-etapa-1-arranque.md). Reutilizar Flows físicos queda **diferido**: esta revisión no cambia cardinalidad, binding ni compiler del plan. Publicación, envío y captura quedan en sus incrementos; no se amplían sus APIs acá. No se modifica plan, arranque, demo ni frontend.

## Resoluciones de esta ronda

| Revisión | Resolución y estado |
| --- | --- |
| K1 | Crear recibe nombre, prefijo y todas las preguntas/opciones en un Guardar y un commit. No hay alta vacía obligatoria previa. POST devuelve snapshot y mapa, sin GET obligatorio. Confirmado por el encargo. |
| K2 | Comprobante aprobado e implementado por actor/requestId, snapshot/mapa protegidos en el mismo commit; resultado 30 días y tombstone posterior contra reutilización. |
| K3 | Identidades explícitas, reemplazo en entrada, localId de padre y ejemplos completos abajo. Retiradas no resucitan; una opción no se mueve a otra pregunta. |
| K4 | D5 vigente: name/label/help/opciones/botón por es/en; guardar pendientes secundarios, publicar completos sin fallback. Ayuda opcional si no existe en ambos, parcial guardable. Budgets de autoría continúan; compilación estricta perfil r2. |
| K5 | Read + Manage para escribir; Read para lista/detail y recuperación propia. Capability del módulo sólo se entrega con recorrido real. Aprobados e implementados formtemplates.read/manage/publish y formrecords.create/read; forms sólo con recorrido de borrador real. |
| K6 | Query/facetas/sort/filas completos; último cambio efectivo y autor autorizado, sin contador de registros ni orden de columna ausente en el primer recorte. Propuesta para congelar. |
| K7 | Paths indexados al cuerpo congelado, ProblemDetails de campos y conflictos diferenciados. Sin merge automático. |
| K8 | Descartar local vuelve al baseline; releer servidor es otra acción. Guardar permanece en editor con notificación compartida y Volver al listado; comparación/relectura conserva intención sin merge automático. |
| K9 | Primer recorte sólo crear/listar/facetas/obtener/guardar y consulta del intento necesaria para recuperación. Copiar/Eliminar/Publicar no se habilitan ni figuran como capabilities disponibles. |

Confirmaciones conservadas: IDs definitivos asignados por servidor al guardar; prefijo congelado en commit de primera publicación durable incluso si luego falla; apertura «Abrir formulario» / «Open form», distinta de submitLabel; valores futuros requieren lectura de registros además de WhatsApp. Entity/IAuditable e interceptores para entidades futuras, sin AuditLog nuevo ni caché de formularios. e1-1 ya tiene prefijo: faltan bloqueo/errores, no un control nuevo.

## Formato 1 y autoría

name es mapa de strings es/en en todos los pedidos, definition, fila de listado y comprobante/recuperación. draft.culture identifica el idioma original inmutable, distinto de UI/Accept-Language. Label, help, opciones y submitLabel mantienen sus mapas existentes. No nameByCulture ni nombre escalar en HTTP; Name interno es proyección derivada del original, y NameJson guarda el mapa completo fuera del SchemaJson de preguntas.

Original de name/label/opciones/botón siempre no vacío. Secundario ausente o "" admite Guardar; whitespace secundario se canoniza a "". Nombre se recorta en ambos idiomas; null de nombre/label/opción/botón y claves fuera es/en son inválidos. Respuestas y comprobantes preservan vacíos explícitos y claves ausentes sin inventar traducción. Retirar explícitamente una secundaria con "" la deja pendiente; omitir una traducción no vacía existente conserva TranslationsWouldBeLost. Omitir una entrada previamente vacía no pierde contenido. Snapshot/revisión completos, sin merge automático.

Ayuda es opcional: entrada mapa de strings nullable por cultura; null/vacío explícito retira esa entrada, conservando las otras. Canonical help es null si no queda ningún texto, o mapa de strings no vacíos parcial/completo. Omitir traducción secundaria existente de ayuda sigue siendo pérdida accidental y se rechaza. Publicación exige ambas ayudas si existe cualquiera; no pide inventar ayuda donde no existe.

Publicar/chequear exige name, labels, opciones y submitLabel es/en completos, ayuda completa si está presente. No fallback al original ni a cultura de instalación. E puede mostrar original en cursiva y «Falta en inglés» sólo como orientación de autoría. Draft preview no demuestra cómo se compila el Flow publicado.

### Contrato mínimo D5 confirmado para frontend

POST definitions lleva requestId/name/prefix/draft; PUT añade expectedRevision y las identidades existentes. Ejemplo mínimo guardable:

~~~json
{
  "requestId": "019a0000-0000-7000-8000-000000000064",
  "name": { "es": "Formulario", "en": "" },
  "prefix": "FORM",
  "draft": { "format": 1, "culture": "es", "fields": [] }
}
~~~

Al omitir submitLabel, default Enviar sólo original (Submit si original en). Resultado201/200 conserva envelope requestId/definition/identityMap/recovery; definition.name devuelve el mismo mapa normalizado, name.en="", draft.submitLabel sólo es, fields vacío. Detail conserva {definition,capabilities,audit}. La fila del listado responde name mapa; el front muestra el nombre de su idioma UI, o el original como referencia cuando falte; orden/búsqueda/facetas name usan en SQL el nombre de la cultura UI resuelta por Accept-Language/configuración, con original sólo como referencia de autoría si falta esa traducción. GET draft-changes conserva {requestId,kind,status,committedAtUtc,currentRevision,result}; result contiene el snapshot de mapas aceptado, aun tras editar después. Todos siguen DTOs cerrados y ToString redactado.

~~~json
{
  "expectedRevision": 1,
  "name": { "es": "Formulario", "en": "" },
  "prefix": "FORM",
  "draft": { "format": 1, "culture": "es", "submitLabel": { "es": "Enviar" }, "fields": [] }
}
~~~

POST definitions/{id}/publication-checks usa ese cuerpo, sin requestId; exige el borrador guardado con IDs presentes. Response200 mantiene definitionId/draftRevision/compatibility/canAccept/acceptanceAvailability/channels/issues/diff. Este ejemplo da Blocked e issues name.en y draft.submitLabel.en, code Forms.Publication.MissingTranslation, culture en, channelId/fieldId/choiceId null. En una pregunta/opción, paths draft.fields[i].label.en/help.en/options[j].label.en incorporan IDs cuando corresponden. Campos inválidos del guardado usan ProblemDetails.errors con paths name.es/name.en; chequeo de traducción distinta sin guardar da409.

Ejemplo completo sin preguntas: name {"es":"Formulario","en":"Form"}, submitLabel {"es":"Enviar","en":"Submit"}. Completitud permite evaluar el canal; compatibility LocallyValidated del perfil flow-static-7.3-r2 no acredita Meta y no instala aceptación: canAccept=false/acceptanceAvailability=Unavailable, externalEvidence=Pending. Nombre largo/help de Dropdown siguen bloqueos de representación por cultura. No versiones/bindings/workers/envíos nuevos.

| Discriminador | Configuración del primer editor | Valor futuro normalizado |
| --- | --- | --- |
| shortText | label/help/required, maxLength positivo | string |
| longText | label/help/required, maxLength positivo | string |
| decimal | label/help/required, sin precision/scale/min/max editables | decimal como string, sin pasar por Number de JavaScript |
| date | label/help/required, sin rangos editables | DateOnly YYYY-MM-DD, sin zona/UTC |
| boolean | label/help/required | bool; false no es ausencia |
| choice | label/help/required, opciones manuales en orden | ChoiceId |

`group` y `source.catalog` se reservan y rechazan al guardar. Sin grupos, catálogos, condiciones, reglas ni selección múltiple. Number del enum de Domain se mapeará explícitamente a decimal JSON. Key técnica sólo en respuesta, asignada por servidor; nunca nombre/label como columna o identidad.

**Presupuestos técnicos de autoría aprobados para este incremento, revisables e independientes de Meta, con FV/HTTP implementados:** cada texto de name 1..120 UTF-16 (original requerido; secundario puede faltar/estar vacío), prefijo 1..12 ASCII `[A-Z][A-Z0-9]{0,11}` normalizado con Trim/UpperInvariant y unicidad incluyendo retirados; labels/opciones/botón 1..160, help hasta 1000; hasta 40 preguntas/50 opciones por pregunta; cuerpo 512 KiB y resultado recuperable hasta 1 MiB. Labels/opciones/botón/ayuda se cuentan en elementos de texto Unicode (StringInfo); nombre usa la cota técnica de 120 unidades UTF-16 compatible con su columna. Defaults maxLength 80/600 para corto/largo; rango propuesto 1..10000. Null de maxLength en entrada se normaliza al default y la respuesta devuelve valor explícito. Required omitido se normaliza false. Defaults de nuevo botón Enviar/Submit en cultura base, sin traducir luego lo personalizado. No cortar textos silenciosamente.

Guardar permite cero preguntas. Una choice incluida debe tener al menos una opción no vacía; no se inventa mínimo dos. Duplicados de labels de opciones: rechazar equivalencia por NFC + Trim + OrdinalIgnoreCase y señalar la segunda fila; no deduplicar automáticamente ni cambiar el texto almacenado. Estas reglas finales y máximos quedan sujetos a revisión; el modelo puro sólo protege estructura/identidad, no todos estos presupuestos. Datos numéricos futuros respetan decimal exacto de .NET; admisión/calificación de Meta se comprueba después sin alterar el editor por inferencia. Campos no soportados/format desconocido fallan; no se eliminan settings/traducciones silenciosamente.

**D5 directa vigente:** ejemplos actualizados al mapa name. La propuesta previa de name escalar + nameByCulture/versionado fue reemplazada por la decisión humana sin datos desplegados. Las seis rutas y comprobantes conservan sus recorridos, con el mismo contrato actualizado.

## Identidad y forma de los pedidos

Cada field tiene localId único en todo el pedido (token `[A-Za-z0-9_-]{1,64}`, generado por cliente y sin datos personales); cada opción tiene localId único dentro de su padre, pudiendo repetirse en otra lista. El servidor no adopta UUIDs del cliente para nuevas identidades. Al guardar crea FieldId/ChoiceId v7 y key. localId no entra en SchemaJson ni en registros; **sí se conserva en el mapa protegido del comprobante**, por su ventana de recuperación.

| Intención | fieldId / replacesFieldId de entrada | Opciones |
| --- | --- | --- |
| Pregunta nueva | null / null | choiceId null para cada nueva opción |
| Existente: editar/renombrar/reordenar | ID vigente / null; type debe coincidir | ID vigente conserva identidad al renombrar/reordenar; nuevas llevan null |
| Cambiar tipo | null / ID vigente que reemplaza; type distinto | Ninguna identidad del campo anterior; opciones nuevas si el tipo pasa a choice |
| Quitar | Omitir pregunta/opción del conjunto completo | Retirar identidad, conservar historia |
| Quitar y reagregar / volver al tipo anterior | Identidad nueva null; reemplazar sólo al ID actualmente vigente cuando corresponde | No usar IDs retirados ni opciones del campo reemplazado |

key sólo responde: mandarla como entrada se rechaza como propiedad no admitida, no se usa para buscar identidad. fieldId y replacesFieldId son excluyentes; reemplazo no puede apuntar a la misma pregunta dos veces ni a una retirada/ajena. El mapa devuelve todas las entradas (existentes y nuevas), no sólo nuevas. choices incluye fieldLocalId además de localId y los IDs definitivos, por lo que dos listas nuevas con option localId=opt-1 no colisionan. Pertenencia se verifica con definición/borrador autorizado y opciones del padre, no sólo por formato UUID. No resucitar IDs tras 409; mantener intención local no equivale a reusar identidad retirada por otro actor.

## Crear completo y obtener

`POST /api/forms/definitions`: requestId nuevo por intento lógico, nombre/prefijo/draft completos. Sin expectedRevision ni definitionId suministrado; el servidor crea definición y revisión 1 junto con todo su contenido y comprobante en un commit. Un guardado del editor completo no se divide en POST vacío + PUT. Crear explícitamente sin preguntas sigue siendo válido, sin imponerlo como paso de UI.

201 devuelve FormDraftSaveResponse completa y Location de definición. Puede usarse sin GET posterior. Si se conoce el id y sólo falla una lectura posterior, repetir GET, nunca crear otra definición. La ruta GET de definición devuelve `{definition,capabilities:{canSave},audit}`; definition es exactamente la forma canónica mostrada en el resultado siguiente (en una lectura posterior contendrá revisión/contenido actuales). En este recorte draft siempre está presente y activo/publicación no se incluyen. No bloquear este contrato por draft=null de una etapa posterior; esa ampliación necesita su propio DTO de editableBase.

### Ejemplo POST: seis tipos y dos listas nuevas
```json
{
  "requestId": "019a0000-0000-7000-8000-000000000064",
  "name": { "es": "Pedido" },
  "prefix": "PED",
  "draft": {
    "format": 1,
    "culture": "es",
    "submitLabel": {
      "es": "Enviar pedido"
    },
    "fields": [
      {
        "localId": "q-ref",
        "fieldId": null,
        "replacesFieldId": null,
        "type": "shortText",
        "label": {
          "es": "Referencia"
        },
        "help": null,
        "required": true,
        "maxLength": 80
      },
      {
        "localId": "q-notes",
        "fieldId": null,
        "replacesFieldId": null,
        "type": "longText",
        "label": {
          "es": "Observaciones"
        },
        "help": null,
        "required": false,
        "maxLength": 2000
      },
      {
        "localId": "q-quantity",
        "fieldId": null,
        "replacesFieldId": null,
        "type": "decimal",
        "label": {
          "es": "Cantidad"
        },
        "help": {
          "es": "Ingresá la cantidad con decimales."
        },
        "required": true
      },
      {
        "localId": "q-date",
        "fieldId": null,
        "replacesFieldId": null,
        "type": "date",
        "label": {
          "es": "Fecha"
        },
        "help": null,
        "required": true
      },
      {
        "localId": "q-pickup",
        "fieldId": null,
        "replacesFieldId": null,
        "type": "boolean",
        "label": {
          "es": "Retira personalmente"
        },
        "help": null,
        "required": true
      },
      {
        "localId": "q-shift",
        "fieldId": null,
        "replacesFieldId": null,
        "type": "choice",
        "label": {
          "es": "Turno"
        },
        "help": null,
        "required": true,
        "options": [
          {
            "localId": "opt-1",
            "choiceId": null,
            "label": {
              "es": "Mañana"
            }
          },
          {
            "localId": "opt-2",
            "choiceId": null,
            "label": {
              "es": "Tarde"
            }
          }
        ]
      },
      {
        "localId": "q-delivery",
        "fieldId": null,
        "replacesFieldId": null,
        "type": "choice",
        "label": {
          "es": "Entrega"
        },
        "help": null,
        "required": true,
        "options": [
          {
            "localId": "opt-1",
            "choiceId": null,
            "label": {
              "es": "Retiro"
            }
          },
          {
            "localId": "opt-2",
            "choiceId": null,
            "label": {
              "es": "Envío"
            }
          }
        ]
      }
    ]
  }
}
```

### Ejemplo 201 completo (identidades ilustrativas de servidor)

```json
{
  "requestId": "019a0000-0000-7000-8000-000000000064",
  "definition": {
    "id": "019a0000-0000-7000-8000-00000000000a",
    "name": { "es": "Pedido" },
    "prefix": "PED",
    "prefixLocked": false,
    "revision": 1,
    "draft": {
      "format": 1,
      "culture": "es",
      "submitLabel": {
        "es": "Enviar pedido"
      },
      "fields": [
        {
          "fieldId": "019a0000-0000-7000-8000-000000000001",
          "key": "field_019a0000000070008000000000000001",
          "type": "shortText",
          "label": {
            "es": "Referencia"
          },
          "help": null,
          "required": true,
          "maxLength": 80
        },
        {
          "fieldId": "019a0000-0000-7000-8000-000000000002",
          "key": "field_019a0000000070008000000000000002",
          "type": "longText",
          "label": {
            "es": "Observaciones"
          },
          "help": null,
          "required": false,
          "maxLength": 2000
        },
        {
          "fieldId": "019a0000-0000-7000-8000-000000000003",
          "key": "field_019a0000000070008000000000000003",
          "type": "decimal",
          "label": {
            "es": "Cantidad"
          },
          "help": {
            "es": "Ingresá la cantidad con decimales."
          },
          "required": true
        },
        {
          "fieldId": "019a0000-0000-7000-8000-000000000004",
          "key": "field_019a0000000070008000000000000004",
          "type": "date",
          "label": {
            "es": "Fecha"
          },
          "help": null,
          "required": true
        },
        {
          "fieldId": "019a0000-0000-7000-8000-000000000005",
          "key": "field_019a0000000070008000000000000005",
          "type": "boolean",
          "label": {
            "es": "Retira personalmente"
          },
          "help": null,
          "required": true
        },
        {
          "fieldId": "019a0000-0000-7000-8000-000000000006",
          "key": "field_019a0000000070008000000000000006",
          "type": "choice",
          "label": {
            "es": "Turno"
          },
          "help": null,
          "required": true,
          "options": [
            {
              "choiceId": "019a0000-0000-7000-8000-00000000001e",
              "label": {
                "es": "Mañana"
              }
            },
            {
              "choiceId": "019a0000-0000-7000-8000-00000000001f",
              "label": {
                "es": "Tarde"
              }
            }
          ]
        },
        {
          "fieldId": "019a0000-0000-7000-8000-000000000007",
          "key": "field_019a0000000070008000000000000007",
          "type": "choice",
          "label": {
            "es": "Entrega"
          },
          "help": null,
          "required": true,
          "options": [
            {
              "choiceId": "019a0000-0000-7000-8000-000000000020",
              "label": {
                "es": "Retiro"
              }
            },
            {
              "choiceId": "019a0000-0000-7000-8000-000000000021",
              "label": {
                "es": "Envío"
              }
            }
          ]
        }
      ]
    }
  },
  "identityMap": {
    "fields": [
      {
        "localId": "q-ref",
        "fieldId": "019a0000-0000-7000-8000-000000000001",
        "key": "field_019a0000000070008000000000000001",
        "replacesFieldId": null
      },
      {
        "localId": "q-notes",
        "fieldId": "019a0000-0000-7000-8000-000000000002",
        "key": "field_019a0000000070008000000000000002",
        "replacesFieldId": null
      },
      {
        "localId": "q-quantity",
        "fieldId": "019a0000-0000-7000-8000-000000000003",
        "key": "field_019a0000000070008000000000000003",
        "replacesFieldId": null
      },
      {
        "localId": "q-date",
        "fieldId": "019a0000-0000-7000-8000-000000000004",
        "key": "field_019a0000000070008000000000000004",
        "replacesFieldId": null
      },
      {
        "localId": "q-pickup",
        "fieldId": "019a0000-0000-7000-8000-000000000005",
        "key": "field_019a0000000070008000000000000005",
        "replacesFieldId": null
      },
      {
        "localId": "q-shift",
        "fieldId": "019a0000-0000-7000-8000-000000000006",
        "key": "field_019a0000000070008000000000000006",
        "replacesFieldId": null
      },
      {
        "localId": "q-delivery",
        "fieldId": "019a0000-0000-7000-8000-000000000007",
        "key": "field_019a0000000070008000000000000007",
        "replacesFieldId": null
      }
    ],
    "choices": [
      {
        "fieldLocalId": "q-shift",
        "localId": "opt-1",
        "fieldId": "019a0000-0000-7000-8000-000000000006",
        "choiceId": "019a0000-0000-7000-8000-00000000001e"
      },
      {
        "fieldLocalId": "q-shift",
        "localId": "opt-2",
        "fieldId": "019a0000-0000-7000-8000-000000000006",
        "choiceId": "019a0000-0000-7000-8000-00000000001f"
      },
      {
        "fieldLocalId": "q-delivery",
        "localId": "opt-1",
        "fieldId": "019a0000-0000-7000-8000-000000000007",
        "choiceId": "019a0000-0000-7000-8000-000000000020"
      },
      {
        "fieldLocalId": "q-delivery",
        "localId": "opt-2",
        "fieldId": "019a0000-0000-7000-8000-000000000007",
        "choiceId": "019a0000-0000-7000-8000-000000000021"
      }
    ]
  },
  "recovery": {
    "path": "/api/forms/draft-changes/019a0000-0000-7000-8000-000000000064",
    "resultExpiresAtUtc": "2026-11-08T12:00:00Z"
  }
}
```

## Guardar conjunto completo

PUT /api/forms/definitions/019a0000-0000-7000-8000-00000000000a/draft usa requestId nuevo, expectedRevision=1 y el conjunto completo. revision/expectedRevision se proponen como enteros JSON 1..9007199254740991 (seguros en JavaScript); el escritor deberá rechazar el avance que exceda ese máximo. Es restricción de interoperabilidad a revisar, implementada al validar el pedido y avanzar la revisión persistida. Este ejemplo renombra Notas/Fecha, reordena y renombra opciones existentes de Turno, agrega Noche, retira Envío y agrega Entrega coordinada. Además reemplaza Referencia por longText y crea Empresa. Mantiene identidades de los cambios del mismo tipo y no reaprovecha ninguna del campo reemplazado. Los dos opt-3 nuevos son distintos por fieldLocalId.

### Ejemplo PUT completo
```json
{
  "requestId": "019a0000-0000-7000-8000-000000000065",
  "name": { "es": "Pedido" },
  "prefix": "PED",
  "draft": {
    "format": 1,
    "culture": "es",
    "submitLabel": {
      "es": "Enviar pedido"
    },
    "fields": [
      {
        "localId": "q-ref",
        "fieldId": null,
        "replacesFieldId": "019a0000-0000-7000-8000-000000000001",
        "type": "longText",
        "label": {
          "es": "Detalle de referencia"
        },
        "help": null,
        "required": true,
        "maxLength": 2000
      },
      {
        "localId": "q-notes",
        "fieldId": "019a0000-0000-7000-8000-000000000002",
        "replacesFieldId": null,
        "type": "longText",
        "label": {
          "es": "Notas"
        },
        "help": null,
        "required": false,
        "maxLength": 2000
      },
      {
        "localId": "q-quantity",
        "fieldId": "019a0000-0000-7000-8000-000000000003",
        "replacesFieldId": null,
        "type": "decimal",
        "label": {
          "es": "Cantidad"
        },
        "help": {
          "es": "Ingresá la cantidad con decimales."
        },
        "required": true
      },
      {
        "localId": "q-date",
        "fieldId": "019a0000-0000-7000-8000-000000000004",
        "replacesFieldId": null,
        "type": "date",
        "label": {
          "es": "Fecha preferida"
        },
        "help": null,
        "required": true
      },
      {
        "localId": "q-pickup",
        "fieldId": "019a0000-0000-7000-8000-000000000005",
        "replacesFieldId": null,
        "type": "boolean",
        "label": {
          "es": "Retira personalmente"
        },
        "help": null,
        "required": true
      },
      {
        "localId": "q-shift",
        "fieldId": "019a0000-0000-7000-8000-000000000006",
        "replacesFieldId": null,
        "type": "choice",
        "label": {
          "es": "Turno"
        },
        "help": null,
        "required": true,
        "options": [
          {
            "localId": "opt-2",
            "choiceId": "019a0000-0000-7000-8000-00000000001f",
            "label": {
              "es": "Tarde"
            }
          },
          {
            "localId": "opt-1",
            "choiceId": "019a0000-0000-7000-8000-00000000001e",
            "label": {
              "es": "Primer turno"
            }
          },
          {
            "localId": "opt-3",
            "choiceId": null,
            "label": {
              "es": "Noche"
            }
          }
        ]
      },
      {
        "localId": "q-delivery",
        "fieldId": "019a0000-0000-7000-8000-000000000007",
        "replacesFieldId": null,
        "type": "choice",
        "label": {
          "es": "Entrega"
        },
        "help": null,
        "required": true,
        "options": [
          {
            "localId": "opt-1",
            "choiceId": "019a0000-0000-7000-8000-000000000020",
            "label": {
              "es": "Retiro"
            }
          },
          {
            "localId": "opt-3",
            "choiceId": null,
            "label": {
              "es": "Entrega coordinada"
            }
          }
        ]
      },
      {
        "localId": "q-company",
        "fieldId": null,
        "replacesFieldId": null,
        "type": "shortText",
        "label": {
          "es": "Empresa"
        },
        "help": null,
        "required": false,
        "maxLength": 80
      }
    ]
  },
  "expectedRevision": 1
}
```

### Ejemplo 200 completo

```json
{
  "requestId": "019a0000-0000-7000-8000-000000000065",
  "definition": {
    "id": "019a0000-0000-7000-8000-00000000000a",
    "name": { "es": "Pedido" },
    "prefix": "PED",
    "prefixLocked": false,
    "revision": 2,
    "draft": {
      "format": 1,
      "culture": "es",
      "submitLabel": {
        "es": "Enviar pedido"
      },
      "fields": [
        {
          "fieldId": "019a0000-0000-7000-8000-000000000008",
          "key": "field_019a0000000070008000000000000008",
          "type": "longText",
          "label": {
            "es": "Detalle de referencia"
          },
          "help": null,
          "required": true,
          "maxLength": 2000
        },
        {
          "fieldId": "019a0000-0000-7000-8000-000000000002",
          "key": "field_019a0000000070008000000000000002",
          "type": "longText",
          "label": {
            "es": "Notas"
          },
          "help": null,
          "required": false,
          "maxLength": 2000
        },
        {
          "fieldId": "019a0000-0000-7000-8000-000000000003",
          "key": "field_019a0000000070008000000000000003",
          "type": "decimal",
          "label": {
            "es": "Cantidad"
          },
          "help": {
            "es": "Ingresá la cantidad con decimales."
          },
          "required": true
        },
        {
          "fieldId": "019a0000-0000-7000-8000-000000000004",
          "key": "field_019a0000000070008000000000000004",
          "type": "date",
          "label": {
            "es": "Fecha preferida"
          },
          "help": null,
          "required": true
        },
        {
          "fieldId": "019a0000-0000-7000-8000-000000000005",
          "key": "field_019a0000000070008000000000000005",
          "type": "boolean",
          "label": {
            "es": "Retira personalmente"
          },
          "help": null,
          "required": true
        },
        {
          "fieldId": "019a0000-0000-7000-8000-000000000006",
          "key": "field_019a0000000070008000000000000006",
          "type": "choice",
          "label": {
            "es": "Turno"
          },
          "help": null,
          "required": true,
          "options": [
            {
              "choiceId": "019a0000-0000-7000-8000-00000000001f",
              "label": {
                "es": "Tarde"
              }
            },
            {
              "choiceId": "019a0000-0000-7000-8000-00000000001e",
              "label": {
                "es": "Primer turno"
              }
            },
            {
              "choiceId": "019a0000-0000-7000-8000-000000000028",
              "label": {
                "es": "Noche"
              }
            }
          ]
        },
        {
          "fieldId": "019a0000-0000-7000-8000-000000000007",
          "key": "field_019a0000000070008000000000000007",
          "type": "choice",
          "label": {
            "es": "Entrega"
          },
          "help": null,
          "required": true,
          "options": [
            {
              "choiceId": "019a0000-0000-7000-8000-000000000020",
              "label": {
                "es": "Retiro"
              }
            },
            {
              "choiceId": "019a0000-0000-7000-8000-000000000029",
              "label": {
                "es": "Entrega coordinada"
              }
            }
          ]
        },
        {
          "fieldId": "019a0000-0000-7000-8000-000000000009",
          "key": "field_019a0000000070008000000000000009",
          "type": "shortText",
          "label": {
            "es": "Empresa"
          },
          "help": null,
          "required": false,
          "maxLength": 80
        }
      ]
    }
  },
  "identityMap": {
    "fields": [
      {
        "localId": "q-ref",
        "fieldId": "019a0000-0000-7000-8000-000000000008",
        "key": "field_019a0000000070008000000000000008",
        "replacesFieldId": "019a0000-0000-7000-8000-000000000001"
      },
      {
        "localId": "q-notes",
        "fieldId": "019a0000-0000-7000-8000-000000000002",
        "key": "field_019a0000000070008000000000000002",
        "replacesFieldId": null
      },
      {
        "localId": "q-quantity",
        "fieldId": "019a0000-0000-7000-8000-000000000003",
        "key": "field_019a0000000070008000000000000003",
        "replacesFieldId": null
      },
      {
        "localId": "q-date",
        "fieldId": "019a0000-0000-7000-8000-000000000004",
        "key": "field_019a0000000070008000000000000004",
        "replacesFieldId": null
      },
      {
        "localId": "q-pickup",
        "fieldId": "019a0000-0000-7000-8000-000000000005",
        "key": "field_019a0000000070008000000000000005",
        "replacesFieldId": null
      },
      {
        "localId": "q-shift",
        "fieldId": "019a0000-0000-7000-8000-000000000006",
        "key": "field_019a0000000070008000000000000006",
        "replacesFieldId": null
      },
      {
        "localId": "q-delivery",
        "fieldId": "019a0000-0000-7000-8000-000000000007",
        "key": "field_019a0000000070008000000000000007",
        "replacesFieldId": null
      },
      {
        "localId": "q-company",
        "fieldId": "019a0000-0000-7000-8000-000000000009",
        "key": "field_019a0000000070008000000000000009",
        "replacesFieldId": null
      }
    ],
    "choices": [
      {
        "fieldLocalId": "q-shift",
        "localId": "opt-2",
        "fieldId": "019a0000-0000-7000-8000-000000000006",
        "choiceId": "019a0000-0000-7000-8000-00000000001f"
      },
      {
        "fieldLocalId": "q-shift",
        "localId": "opt-1",
        "fieldId": "019a0000-0000-7000-8000-000000000006",
        "choiceId": "019a0000-0000-7000-8000-00000000001e"
      },
      {
        "fieldLocalId": "q-shift",
        "localId": "opt-3",
        "fieldId": "019a0000-0000-7000-8000-000000000006",
        "choiceId": "019a0000-0000-7000-8000-000000000028"
      },
      {
        "fieldLocalId": "q-delivery",
        "localId": "opt-1",
        "fieldId": "019a0000-0000-7000-8000-000000000007",
        "choiceId": "019a0000-0000-7000-8000-000000000020"
      },
      {
        "fieldLocalId": "q-delivery",
        "localId": "opt-3",
        "fieldId": "019a0000-0000-7000-8000-000000000007",
        "choiceId": "019a0000-0000-7000-8000-000000000029"
      }
    ]
  },
  "recovery": {
    "path": "/api/forms/draft-changes/019a0000-0000-7000-8000-000000000065",
    "resultExpiresAtUtc": "2026-11-08T13:00:00Z"
  }
}
```

## Recuperación durable aprobada e implementada (K1/K2)

**Implementada y aprobada, con migración incremental AddBusinessFormDrafts.** El resultado canónico que debe conservarse es definition (id/name/prefix/prefixLocked/revision/draft) e identityMap del intento, junto con requestId y ventana de recuperación. Audit y capabilities no forman parte de ese snapshot: GET/lista los proyectan desde los interceptores/directorio/permisos. Esto permite escribir el comprobante completo antes del SaveChanges sin setear auditoría a mano ni abrir un segundo commit; el interceptor todavía no completó auditoría dentro del callback de UoW. Recovery expone committedAtUtc desde la auditoría del comprobante ya confirmado. Nunca reconstruir el contenido del intento desde la definición actual.

### Mecanismos contrastados

| Mecanismo real | Lo que se reutiliza / lo que falta |
| --- | --- |
| [AiBusinessInformationService](../../src/ArquitecturaBase.Application/Modules/ArtificialIntelligence/Services/AiBusinessInformationService.cs), [Change](../../src/ArquitecturaBase.Domain/Modules/ArtificialIntelligence/AiBusinessInformationChange.cs), [Repository](../../src/ArquitecturaBase.Infrastructure/Modules/ArtificialIntelligence/Persistence/Repositories/AiBusinessInformationRepository.cs), [Reader](../../src/ArquitecturaBase.Infrastructure/Modules/ArtificialIntelligence/Persistence/Readers/AiBusinessInformationReader.cs) | Lock, unique OperatorId+RequestId, huella, revisión y comprobante en mismo OnSuccess; relectura propia y corte de 30 días. Su receipt devuelve sólo revisión: no recupera snapshot ni mapa del editor. No depender del módulo IA ni reutilizar sus tablas/keys. |
| AiConnectionChange / AiAssistantRequestReceipt | Referencia de huella, actor, resultado/retención y recuperación tras pérdida; su propósito y ciclo son distintos. No extenderlas para formularios. |
| WhatsAppChatService y salida durable | Dedup de UUID/cuerpo y consulta existente para envío externo; estado incierto necesario allí. Guardar local no necesita outbox, Pending/Sending ni worker de envío. |
| [DataProtectionRegistration](../../src/ArquitecturaBase.Infrastructure/Identity/DataProtectionRegistration.cs) y [DurableMessageStore](../../src/ArquitecturaBase.Infrastructure/Delivery/DurableMessageStore.cs) | Protección autenticada/anillo persistido y protegido existente; WhatsAppQueuedMessageCodec sólo serializa formato cerrado, el store protege el payload; propósito independiente por módulo. No crear credencial IA/Meta para guardar un borrador. |
| Guardar último requestId/mapa en BusinessFormDefinitions | Menos filas, pero otro actor sobrescribe el resultado que K2 necesita; insuficiente. Guardar toda historia en esa fila vuelve a implementar un registro de intentos. |
| PublicationOperation / captureReceipt | No son guardados de editor. No mutar ni crear por simetría; se conservan para sus incrementos originales. |

### Una tabla adicional por necesidad real

Propuesta `BusinessFormDraftChanges`, aparte de las tablas del plan original: Id v7 (Entity); OperatorUserId y RequestId, unique conjunto para POST/PUT del actor; Kind Create/Update; DefinitionId; ExpectedRevision nullable; ConfirmedRevision; ResultFormat=1; ProtectedResult bytea nullable; ResultExpiresAtUtc; auditoría IAuditable (CreatedAtUtc/CreatedBy/ModifiedAtUtc/ModifiedBy), sólo interceptores. Índice de ResultExpiresAtUtc para purga; FK de definición no elimina en cascada la evidencia de un intento. Retirada/borrado futuro no autoriza otra creación con el mismo UUID. No tabla genérica de operaciones ni cambios a núcleo.

ProtectedResult contiene huella SHA-256 del pedido tipado normalizado **dentro del sobre protegido**, más resultado canónico/mapa. No digest de contenido privado en claro ni body original duplicado. Data Protection con propósito versionado propio y contexto vinculado a Id/OperatorUserId/RequestId/Kind/DefinitionId; protección autenticada impide trasplantes. Comparar huella descifrada con FixedTimeEquals. Persistir ResultFormat permite replays compatibles tras una evolución aditiva. Rotación/restauración dependen del anillo/certificados históricos ya gestionados (ADR 0016); prueba de dos hosts/restauración obligatoria. La pérdida de llave devuelve falla técnica, no recrea el formulario.

Huella incluye Kind/ruta lógica, definición (PUT), expectedRevision, nombre/prefijo normalizados y todos los campos del pedido admitido, incluidos localIds/reemplazos, orden de preguntas/opciones, traducciones y defaults resueltos. Mapas de idioma se ordenan por clave; orden de preguntas/opciones sí es semántico. Actor sale de sesión y se vincula en contexto de protección. La normalización/versionado se fija antes de implementar; no usar hash del JSON recibido sin validar ni comparar por etiquetas/revisión/orden.

### Límite de guardado y replay

Un solo IUnitOfWork.OnSuccess por Create/Save. Validar contrato antes; dentro, locks con orden definido (actor/requestId primero, luego definición/prefijo), buscar recibo propio **antes** del conflicto de revisión. Mismo UUID/cuerpo devuelve snapshot/mapa previos y el mismo 201/200 sin volver a asignar IDs ni avanzar revisión; UUID usado en otro Kind/definición/cuerpo devuelve RequestConflict. Para intento nuevo, revalidar acceso y expectedRevision/pertenencia/prefijo, construir contenido canónico y retiro de identidades, agregar resultado protegido y guardar todo en el commit único. Unique index protege carrera entre hosts; probar rollback y resolver colisión sin inventar 500 como éxito. Ningún callback guarda por su cuenta ni abre UoW anidada.

GET implementado `/api/forms/draft-changes/{requestId}` es lectura propia, no provoca escritura ni crea/reenvía. Revalida TemplateRead y actor; un UUID ajeno/ausente responde 404 genérico. Respuesta Committed incluye result original, committedAtUtc y currentRevision informativa, que puede ser mayor. Nunca sustituye result por GET actual. Si la definición fue retirada o se perdió permiso, no devuelve contenido privado; replay tampoco evita autorización actual.

404 de consulta NO prueba rollback: el primer commit puede estar en vuelo. Mantener UUID/cuerpo/localIds congelados; consulta y eventual repetición explícita con el mismo UUID/cuerpo bajo dedup son la forma de resolver, sin retry automático ni POST nuevo. Guardado tardío sólo actualiza el editor/actor correspondiente. Si currentRevision supera la confirmada, recuperar el mapa no autoriza sobrescribir ni reutilizar IDs retirados; releer y comparar con el baseline y la intención local. Recarga que perdió UUID/cuerpo de memoria no se correlaciona por contenido: persistencia/resume de metadata del intento en cliente necesita decisión de interacción, no se promete recuperación ficticia por GET de definición.

### Retención, costo y pruebas a aprobar

Payload recuperable aprobado de 30 días desde aceptación, resultExpiresAtUtc devuelto. Tras expirar, lectura/replay propio da 410 Forms.Draft.ChangeExpired, sin crear otro formulario. Purga borra ProtectedResult mediante entidades seguidas/UoW, conserva tombstone (actor/UUID/Kind/DefinitionId/revisión/expiración) para impedir reutilización; no ExecuteUpdate con IAuditable. Tombstones aprobados como metadata retenida, sin purga automática de UUIDs en este incremento; la retirada definitiva requiere su protocolo explícito, sin afirmar que 30 días de payload garantiza replay eterno. Aun antes de purga física, expiración impide exponer resultado.

Costo: una fila e índices por guardado, copia protegida del schema+mapa O(preguntas+opciones), más la definición vigente; máximo de resultado 1 MiB; lote 40x50 medido en la evidencia, sin afirmar rendimiento de producción. Tras purga queda metadata pequeña, pero cantidad de tombstones crece. Medir pg_column_size, tiempos/locks, plan del índice de vencimiento y lote con 40x50/autores concurrentes; no declarar cifras de rendimiento sin piloto. Alternativa de retención finita de toda la fila reduce costo pero pierde garantía contra POST viejo repetido: requeriría protocolo adicional de expiración/nonce; no se elige implícitamente.

Pruebas requeridas: POST con commit y respuesta perdida recupera mismo id/mapa; PUT perdido recupera mapa aun tras otro actor guardar; dos hosts/mismo UUID sólo un commit; UUID/cuerpo o Kind distintos 409; corte antes del commit no deja definición/recibo parcial; fallo de protección impide commit; revisión vieja no muta; 404 mientras primer intento está en vuelo no habilita duplicado; pérdida/revocación de permiso y actor ajeno no leen; expiración/purga con FakeTimeProvider produce 410 y bloquea replay; keys históricas/dos hosts/restauración; orden/localIds incluidos en huella y compatibilidad de ResultFormat. Ninguna está ejecutada: no existe aún el mecanismo.

### GET de definición completo tras PUT

Audit usa CreatedAtUtc/CreatedBy y ModifiedAtUtc/ModifiedBy de interceptores; lastChangedAtUtc = ModifiedAtUtc ?? CreatedAtUtc, siempre no null. lastChangedBy refleja actor de ese cambio, con displayName de proyección autorizada actual; si cuenta no está disponible, summary null y UI muestra un guion, nunca autor inventado. No mostrar correo/teléfono. Lectura por lote de autores detrás del puerto/lector correspondiente, sin N+1. La visibilidad de ese nombre de empleado es contexto de TemplateRead, no acceso general a sus datos de cuenta.
```json
{
  "definition": {
    "id": "019a0000-0000-7000-8000-00000000000a",
    "name": { "es": "Pedido" },
    "prefix": "PED",
    "prefixLocked": false,
    "revision": 2,
    "draft": {
      "format": 1,
      "culture": "es",
      "submitLabel": {
        "es": "Enviar pedido"
      },
      "fields": [
        {
          "fieldId": "019a0000-0000-7000-8000-000000000008",
          "key": "field_019a0000000070008000000000000008",
          "type": "longText",
          "label": {
            "es": "Detalle de referencia"
          },
          "help": null,
          "required": true,
          "maxLength": 2000
        },
        {
          "fieldId": "019a0000-0000-7000-8000-000000000002",
          "key": "field_019a0000000070008000000000000002",
          "type": "longText",
          "label": {
            "es": "Notas"
          },
          "help": null,
          "required": false,
          "maxLength": 2000
        },
        {
          "fieldId": "019a0000-0000-7000-8000-000000000003",
          "key": "field_019a0000000070008000000000000003",
          "type": "decimal",
          "label": {
            "es": "Cantidad"
          },
          "help": {
            "es": "Ingresá la cantidad con decimales."
          },
          "required": true
        },
        {
          "fieldId": "019a0000-0000-7000-8000-000000000004",
          "key": "field_019a0000000070008000000000000004",
          "type": "date",
          "label": {
            "es": "Fecha preferida"
          },
          "help": null,
          "required": true
        },
        {
          "fieldId": "019a0000-0000-7000-8000-000000000005",
          "key": "field_019a0000000070008000000000000005",
          "type": "boolean",
          "label": {
            "es": "Retira personalmente"
          },
          "help": null,
          "required": true
        },
        {
          "fieldId": "019a0000-0000-7000-8000-000000000006",
          "key": "field_019a0000000070008000000000000006",
          "type": "choice",
          "label": {
            "es": "Turno"
          },
          "help": null,
          "required": true,
          "options": [
            {
              "choiceId": "019a0000-0000-7000-8000-00000000001f",
              "label": {
                "es": "Tarde"
              }
            },
            {
              "choiceId": "019a0000-0000-7000-8000-00000000001e",
              "label": {
                "es": "Primer turno"
              }
            },
            {
              "choiceId": "019a0000-0000-7000-8000-000000000028",
              "label": {
                "es": "Noche"
              }
            }
          ]
        },
        {
          "fieldId": "019a0000-0000-7000-8000-000000000007",
          "key": "field_019a0000000070008000000000000007",
          "type": "choice",
          "label": {
            "es": "Entrega"
          },
          "help": null,
          "required": true,
          "options": [
            {
              "choiceId": "019a0000-0000-7000-8000-000000000020",
              "label": {
                "es": "Retiro"
              }
            },
            {
              "choiceId": "019a0000-0000-7000-8000-000000000029",
              "label": {
                "es": "Entrega coordinada"
              }
            }
          ]
        },
        {
          "fieldId": "019a0000-0000-7000-8000-000000000009",
          "key": "field_019a0000000070008000000000000009",
          "type": "shortText",
          "label": {
            "es": "Empresa"
          },
          "help": null,
          "required": false,
          "maxLength": 80
        }
      ]
    }
  },
  "capabilities": {
    "canSave": true
  },
  "audit": {
    "createdAtUtc": "2026-10-09T12:00:00Z",
    "modifiedAtUtc": "2026-10-09T13:00:00Z",
    "lastChangedAtUtc": "2026-10-09T13:00:00Z",
    "lastChangedBy": {
      "userId": "019a0000-0000-7000-8000-0000000000c8",
      "displayName": "Operador de ejemplo"
    }
  }
}
```

### Recuperar el POST perdido después de otro guardado

GET /api/forms/draft-changes/019a0000-0000-7000-8000-000000000064 devuelve revisión 1/mapa original y currentRevision=2. No actualizar el editor a revisión 2 con este mapa; GET actual/comparación son otra acción.

```json
{
  "requestId": "019a0000-0000-7000-8000-000000000064",
  "kind": "Create",
  "status": "Committed",
  "committedAtUtc": "2026-10-09T12:00:00Z",
  "currentRevision": 2,
  "result": {
    "requestId": "019a0000-0000-7000-8000-000000000064",
    "definition": {
      "id": "019a0000-0000-7000-8000-00000000000a",
      "name": { "es": "Pedido" },
      "prefix": "PED",
      "prefixLocked": false,
      "revision": 1,
      "draft": {
        "format": 1,
        "culture": "es",
        "submitLabel": {
          "es": "Enviar pedido"
        },
        "fields": [
          {
            "fieldId": "019a0000-0000-7000-8000-000000000001",
            "key": "field_019a0000000070008000000000000001",
            "type": "shortText",
            "label": {
              "es": "Referencia"
            },
            "help": null,
            "required": true,
            "maxLength": 80
          },
          {
            "fieldId": "019a0000-0000-7000-8000-000000000002",
            "key": "field_019a0000000070008000000000000002",
            "type": "longText",
            "label": {
              "es": "Observaciones"
            },
            "help": null,
            "required": false,
            "maxLength": 2000
          },
          {
            "fieldId": "019a0000-0000-7000-8000-000000000003",
            "key": "field_019a0000000070008000000000000003",
            "type": "decimal",
            "label": {
              "es": "Cantidad"
            },
            "help": {
              "es": "Ingresá la cantidad con decimales."
            },
            "required": true
          },
          {
            "fieldId": "019a0000-0000-7000-8000-000000000004",
            "key": "field_019a0000000070008000000000000004",
            "type": "date",
            "label": {
              "es": "Fecha"
            },
            "help": null,
            "required": true
          },
          {
            "fieldId": "019a0000-0000-7000-8000-000000000005",
            "key": "field_019a0000000070008000000000000005",
            "type": "boolean",
            "label": {
              "es": "Retira personalmente"
            },
            "help": null,
            "required": true
          },
          {
            "fieldId": "019a0000-0000-7000-8000-000000000006",
            "key": "field_019a0000000070008000000000000006",
            "type": "choice",
            "label": {
              "es": "Turno"
            },
            "help": null,
            "required": true,
            "options": [
              {
                "choiceId": "019a0000-0000-7000-8000-00000000001e",
                "label": {
                  "es": "Mañana"
                }
              },
              {
                "choiceId": "019a0000-0000-7000-8000-00000000001f",
                "label": {
                  "es": "Tarde"
                }
              }
            ]
          },
          {
            "fieldId": "019a0000-0000-7000-8000-000000000007",
            "key": "field_019a0000000070008000000000000007",
            "type": "choice",
            "label": {
              "es": "Entrega"
            },
            "help": null,
            "required": true,
            "options": [
              {
                "choiceId": "019a0000-0000-7000-8000-000000000020",
                "label": {
                  "es": "Retiro"
                }
              },
              {
                "choiceId": "019a0000-0000-7000-8000-000000000021",
                "label": {
                  "es": "Envío"
                }
              }
            ]
          }
        ]
      }
    },
    "identityMap": {
      "fields": [
        {
          "localId": "q-ref",
          "fieldId": "019a0000-0000-7000-8000-000000000001",
          "key": "field_019a0000000070008000000000000001",
          "replacesFieldId": null
        },
        {
          "localId": "q-notes",
          "fieldId": "019a0000-0000-7000-8000-000000000002",
          "key": "field_019a0000000070008000000000000002",
          "replacesFieldId": null
        },
        {
          "localId": "q-quantity",
          "fieldId": "019a0000-0000-7000-8000-000000000003",
          "key": "field_019a0000000070008000000000000003",
          "replacesFieldId": null
        },
        {
          "localId": "q-date",
          "fieldId": "019a0000-0000-7000-8000-000000000004",
          "key": "field_019a0000000070008000000000000004",
          "replacesFieldId": null
        },
        {
          "localId": "q-pickup",
          "fieldId": "019a0000-0000-7000-8000-000000000005",
          "key": "field_019a0000000070008000000000000005",
          "replacesFieldId": null
        },
        {
          "localId": "q-shift",
          "fieldId": "019a0000-0000-7000-8000-000000000006",
          "key": "field_019a0000000070008000000000000006",
          "replacesFieldId": null
        },
        {
          "localId": "q-delivery",
          "fieldId": "019a0000-0000-7000-8000-000000000007",
          "key": "field_019a0000000070008000000000000007",
          "replacesFieldId": null
        }
      ],
      "choices": [
        {
          "fieldLocalId": "q-shift",
          "localId": "opt-1",
          "fieldId": "019a0000-0000-7000-8000-000000000006",
          "choiceId": "019a0000-0000-7000-8000-00000000001e"
        },
        {
          "fieldLocalId": "q-shift",
          "localId": "opt-2",
          "fieldId": "019a0000-0000-7000-8000-000000000006",
          "choiceId": "019a0000-0000-7000-8000-00000000001f"
        },
        {
          "fieldLocalId": "q-delivery",
          "localId": "opt-1",
          "fieldId": "019a0000-0000-7000-8000-000000000007",
          "choiceId": "019a0000-0000-7000-8000-000000000020"
        },
        {
          "fieldLocalId": "q-delivery",
          "localId": "opt-2",
          "fieldId": "019a0000-0000-7000-8000-000000000007",
          "choiceId": "019a0000-0000-7000-8000-000000000021"
        }
      ]
    },
    "recovery": {
      "path": "/api/forms/draft-changes/019a0000-0000-7000-8000-000000000064",
      "resultExpiresAtUtc": "2026-11-08T12:00:00Z"
    }
  }
}
```

## Permisos, capacidad y primer inventario (K5/K9)

TemplateRead/TemplateManage son alias de formtemplates.read/manage. Los nombres formtemplates.read/manage/publish y formrecords.create/read fueron aprobados por el usuario e implementados en BusinessFormsPermissions, catálogo opcional y recursos es/en. El seed instalado los compone para Admin; publish y records no habilitan rutas futuras en este recorte. El formato de dos segmentos conserva la guía y tests existentes.

| Operación propuesta | Autorización | Respuesta |
| --- | --- | --- |
| GET /api/forms/definitions | TemplateRead | PagedResult directo |
| GET /api/forms/definitions/facets | TemplateRead | Conteos de la dimensión |
| GET /api/forms/definitions/{id} | TemplateRead | definition + capabilities + audit |
| POST /api/forms/definitions | TemplateRead + TemplateManage | 201 snapshot/mapa/comprobante |
| PUT /api/forms/definitions/{id}/draft | TemplateRead + TemplateManage | 200 snapshot/mapa/comprobante |
| GET /api/forms/draft-changes/{requestId} | TemplateRead + propiedad del intento | 200 resultado propio; 404/410 según evidencia |

Sólo lectura: panel/lista/editor de consulta; sin Guardar/crear. Manage sin Read: no panel/alta/editor ni guardado; no interpretar Manage como Read. Revocación de Manage conserva recuperación propia si sigue Read, pero impide otro guardado. Sin sesión 401; permiso insuficiente 403; entidad/recibo ajeno o inexistente 404 sin datos privados.

Capability propuesta `forms` en InstalledFeatures de SystemPresentationResponse, aportada por IClientCapabilitySource y registro del módulo **cuando el recorrido entregado realmente exista**. No aportar capability por el mero enum/clases de Domain. Fuente real: SystemSettingsService.GetPresentationAsync compone las fuentes fuera del cache de ajustes, como assistant. Ausencia de módulo: no capability, no panel/rutas consumidoras; URLs API inexistentes siguen borde 404, no una feature ficticia. Actor/permisos/cultura de bootstrap delimitan query keys; perder sesión/acceso limpia snapshots/mapas y callbacks tardíos no mutan otro editor.

GET capabilities.canSave requiere ambas facultades y recorrido disponible. No canDelete/canCopy/canPublish=true en este recorte ni acciones habilitadas para operaciones no entregadas. Publicación/envío/captura conservan inventario del plan para después. Sólo /api, sin BackendPrefixes nuevo; al implementar estas seis combinaciones se agregan inventario explícito, OpenAPI y tests.

## Lista y facetas congelables (K6)

Query propuesta `page=1&pageSize=20&search=Pedido&sort=name&publicationFilter=All`. PagedRequest: page 1..1000000, pageSize 1..100, search <=100. Filtro enum All/Published/Drafts. Sort whitelist name, questionCount, lastChangedAtUtc; prefijo '-' indica descendente. Default name ascendente, Id ascendente como desempate único. No sort por recordCount ni columna ausente. lastChangedAtUtc no nullable: coalesce servidor, nunca fecha ficticia en cliente.

questionCount = preguntas vigentes del draft (sin IDs retirados). En este primer bloque siempre hay draft; fallback de snapshot sellado pertenece al contrato de publicación posterior. All reúne visibles; Published exige versión activa; Drafts no tiene activa. Versión activa anterior y falla posterior sigue Published, independientemente del displayState Failed: Badge no define filtro. Mientras sólo haya borradores, Published=0 y displayState=Draft; no fingir publicación. Sin contador/planilla/Ver respuestas en estas filas.

Búsqueda literal case-insensitive por nombre, con escape de %/_. Filtrar autorización/soft delete antes de conteos/paginado. Facetas con búsqueda y demás filtros, sin su propia dimensión publicationFilter; page/pageSize/sort no afectan conteos. Infrastructure ApplySort/ToPagedResultAsync, sin IQueryable a Application. Campos del autor se proyectan en lote como audit anterior; ausencia no inventa nombre.

Ejemplos de `GET /api/forms/definitions` y `/facets?search=Pedido`:

```json
{
  "items": [
    {
      "id": "019a0000-0000-7000-8000-00000000000a",
      "name": { "es": "Pedido" },
      "prefix": "PED",
      "questionCount": 8,
      "activeVersionNumber": null,
      "displayState": "Draft",
      "lastChangedAtUtc": "2026-10-09T13:00:00Z",
      "lastChangedBy": {
        "userId": "019a0000-0000-7000-8000-0000000000c8",
        "displayName": "Operador de ejemplo"
      },
      "canEdit": true
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1,
  "hasPrevious": false,
  "hasNext": false
}
```

```json
{
  "all": 1,
  "published": 0,
  "drafts": 1
}
```

## Errores de controles y conflictos (K7)

Todos son ProblemDetails localizados (title/detail/code/traceId; errors sólo validación). Textos de los ejemplos son ilustrativos es; recursos es/en serán fuente. Paths exactos en camelCase sobre el **pedido congelado**: índices no identifican negocio, sólo apuntan al field.localId y option.localId de ese mismo intento. Front expande/enfoca usando ese snapshot y sus localIds; jamás aplica el índice a otro orden o editor posterior. Path desconocido/actor-editor cambiado va al feedback general, no a un control supuesto. No eco de labels/IDs ajenos ni body en logs/ToString.

| Control | Path |
| --- | --- |
| Nombre / prefijo | name / prefix |
| Botón final | draft.submitLabel.es |
| Etiqueta / ayuda de pregunta | draft.fields[1].label.es / draft.fields[1].help.es |
| Cota de texto | draft.fields[0].maxLength |
| Opción vacía/duplicada | draft.fields[5].options[0].label.es / draft.fields[5].options[2].label.es |
| Grupo de opciones vacío | draft.fields[5].options |
| Tipo reservado | draft.fields[4].type |
| Identidad/pertenencia inválida | draft.fields[6].fieldId o draft.fields[6].options[1].choiceId |

Ejemplo 400 de varias validaciones del cuerpo (variante inválida del pedido, no respuesta al ejemplo válido):

```json
{
  "type": "about:blank",
  "title": "Datos no válidos",
  "status": 400,
  "detail": "Revisá los campos indicados.",
  "code": "Validation.Failed",
  "traceId": "00-11111111111111111111111111111111-2222222222222222-01",
  "errors": {
    "name": [
      "Ingresá un nombre."
    ],
    "prefix": [
      "Usá de 1 a 12 letras o números, empezando con una letra."
    ],
    "draft.fields[1].help.es": [
      "Usá hasta 1000 caracteres."
    ],
    "draft.fields[0].maxLength": [
      "Ingresá un máximo entre 1 y 10000."
    ],
    "draft.fields[5].options[0].label.es": [
      "Escribí el texto de la opción."
    ],
    "draft.fields[5].options[2].label.es": [
      "Esta opción está repetida."
    ],
    "draft.fields[6].options": [
      "Agregá las opciones de esta pregunta."
    ],
    "draft.fields[4].type": [
      "Este tipo de pregunta no está disponible en esta etapa."
    ]
  }
}
```

PrefixTaken e IdentityInvalid, si se atribuyen a un campo, siguen FieldErrors.On (ValidationError, 400 con code específico y errors); no inventar Error fuera de Domain. IdentityRetired, PrefixLocked, RevisionConflict y RequestConflict son **409 distintos**, sin convertirlos al mismo conflicto ni envolverlos en FieldErrors.On (eso cambiaría su tipo/status). Cada error se declara en Domain y se traduce en recursos antes de HTTP.

```json
{
  "type": "about:blank",
  "title": "Conflicto",
  "status": 409,
  "detail": "El borrador cambió. Volvé a cargarlo antes de guardar.",
  "code": "Forms.Draft.RevisionConflict",
  "traceId": "00-11111111111111111111111111111111-2222222222222222-01"
}
```

RevisionConflict no adjunta una revisión adivinada: Revisar ejecuta GET de definición, que devuelve revisión y contenido actuales; se compara con baseline confirmado y Mis cambios por FieldId/ChoiceId. No merge automático por texto/índice ni sobrescribir con expectedRevision recién leído. Conservar mis cambios mantiene intención local; retiradas/reemplazos se resuelven explícitamente antes de nuevo intento. PrefixLocked relee indicador y muestra prefijo readOnly; RequestConflict obliga resolver el intento original, no generar UUID para saltar dedup; IdentityRetired requiere reconstruir como identidad nueva después de decisión visible, sin resurrección silenciosa.

Errores implementados en FormDraftErrors/FormDefinitionErrors, traducidos es/en: identidad/configuración/revisión, PrefixTaken/PrefixLocked/NotFound, TranslationsWouldBeLost, RequestConflict, ChangeNotFound/ChangeExpired y ContentInvalid/ResultTooLarge. Format desconocido usa Validation.Failed con path draft.format; no se inventa FormatUnsupported. Request.Invalid ilegible conserva ausencia de errors. Rate limits reales y pipeline compartido cubren los errores HTTP; expiración del resultado agrega Gone/410.

## Baseline, descarte y alcance del editor (K8)

Baseline = snapshot confirmado con su revisión y mapa. Descartar local restaura ese baseline en memoria; no hace GET, DELETE ni afecta cambios de otro actor. En alta todavía sin commit restaura estado inicial local, sin dejar definición vacía persistida. Recargar servidor es acción separada: si hay cambios locales pide decisión y no pisa intención. Guardado confirmado actualiza baseline/IDs juntos para el editor y actor del intento; resultado tardío de otra pantalla/cuenta no reabre ni modifica ese editor.

Durante Saving/Unconfirmed se congelan UUID/cuerpo/provisionales y se bloquea otro Guardar. Salir no cancela un posible commit y no autoriza creación repetida. Diseño D/E debe concretar guarda/salida/consulta sin pérdida; persistencia del UUID no secreto para resume también necesita elección, sin guardar contenido privado en URL/storage/MutationCache. **Destino aprobado tras Guardar**: permanece en editor, notificación compartida y Volver al listado explícito. Borrador sin activa/preview orientativa no hace llamadas Meta.

## Propuesta de cierre técnico por bloques

Los prerrequisitos fueron aprobados e implementados: entidades auditables, servicio/puertos/validador/canonización, repositorio/lector/protección, migración y registros útiles de las tres capas, controllers/DTOs/inventario. El fallo de composición inicial quedó resuelto sin registros vacíos ni cambios al detector; arquitectura 139/139. El cierre completo se registra con sus resultados reales en la evidencia enlazada arriba. No se inició Aspire.

## Implementado en esta ronda y evidencia

La [ronda 2](../reviews/2026-10-09-formularios-borrador-ronda-2.md) conserva evidencia histórica del modelo puro (28 tests nuevos) y su falla de composición entonces pendiente. El incremento durable implementa ahora las responsabilidades antes ausentes. El desarrollo del front, demo y planes principales pertenece a sus autores; este contrato no declara su validación visual.

## Estado implementado tras aprobación humana

La fuente funcional y la evidencia enlazadas arriba reemplazan las referencias históricas a ausencia de guardado/composición en el bloque de modelo puro. FormDefinitions/FormDraftChanges, ledger separado, codec/protección propia, catálogo/capacidad/registros reales y seis rutas están implementados. QuestionCount es una proyección persistida para listado/sort SQL. No existen Fields/versions/records del futuro. SchemaJson conserva fields del borrador únicamente; questions del futuro esquema de versión sigue en su etapa.

GET de intento devuelve result original; 404 en vuelo conserva incertidumbre. Retención física: lotes de 100 al guardar y cada 15 minutos; corte de lectura a 30 días inmediato, tombstones sin caducidad automática. Propósito DataProtection vinculado a fila/actor/UUID/kind/definición y anillo protegido compartido; no dependencia IA. Key técnica actual field_ + UUID N, sin contador ni etiqueta. La decisión de UX Guardar/Volver y los cinco permisos ya no están abiertos.

### Mínimo2 implementado después del contrato de autoría

El detalle amplía el envelope de paso1 con publication y capabilities.canPublish/canPreview. Forma, permisos y errores actuales en [la fuente funcional](../features/formularios.md#recarga-de-publicación-y-cultura-de-preview--mínimo-2): activa y última operación separadas, UUID/ruta sólo del actor con Publish, listado/facetas conservando activa anterior y preview culture=es|en explícita. Las formas de guardado/recuperación de paso1 permanecen iguales; no agregar metadata de publicación al snapshot del comprobante.
