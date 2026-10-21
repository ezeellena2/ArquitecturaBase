# Code review: dar de baja, reactivar y eliminar formularios — 2026-10-21

Revisión de lectura de backend `3156d486..b7a7188e` y front `5db98e8..65ed60a`, contra las decisiones del plan retirado (`git show 3156d486:docs/plans/formularios/dar-de-baja-formularios.md`) y las [reglas implementadas](../features/formularios.md#dar-de-baja-reactivar-y-eliminar--2026-10-21).

**Lo que está bien:** no hay huecos de seguridad, y lo central está completo y con test.
- **Oferta:** un solo filtro (`FormOfferableVersionQuery`) para el selector, el asistente, las sugerencias y su preparación. Un envío abierto se responde y se captura igual.
- **Baja y reactivación:** la baja conserva `ActiveVersionId` y sube la revisión. Reactivar la limpia recién al activar la versión nueva. Con una publicación abierta, baja y eliminación responden 409.
- **Eliminar:**
  - es un borrado lógico;
  - el prefijo queda reservado;
  - las respuestas sin capturar y los envíos encolados bloquean la eliminación;
  - un guardado previo se recupera sin revivir la definición.
- **Deprecación:**
  - lease y fence propios;
  - orden de locks sin ciclos;
  - un efecto en curso pasa a «Sin confirmar» y se consulta antes de repetir;
  - incluye las versiones reemplazadas, y una publicación en curso la suspende;
  - contrato real de Meta: POST `/{id}/deprecate`.
- **Front:**
  - contratos zod estrictos iguales a los ejemplos exportados;
  - el borrador sin guardar se conserva;
  - acciones según permiso, sin un menú «⋯» vacío;
  - BA6 con solo Quitar y Guardar apagado;
  - es/en con las mismas claves.

Se corrige en un solo chat, backend y front juntos, cada arreglo con su test y commits chicos. Las decisiones no cambian. Después va el [piloto con Meta](../plans/verificacion-y-produccion.md#formularios-baja-reactivación-y-eliminación).

## Arreglar ahora

**Backend**

1. **Un rechazo definitivo de la deprecación se repite para siempre y no se ve.**
   - **Dónde:**
     - `WhatsAppFlowClient.EffectAsync` (`WhatsAppFlowClient.cs:77`) devuelve un 4xx como «sin efecto y sin incertidumbre»;
     - `WhatsAppFlowBinding.RecordDeprecationEffect` (`WhatsAppFlowBinding.Deprecation.cs:50-54`) lo vuelve a `Pending`;
     - `WhatsAppFlowDeprecationService` (`:57` y `:79-86`) ignora `FailureCode`.
   - **Qué pasa:** el worker repite el POST, o la verificación de cuenta, cada pocos segundos, sin fin y por cada Flow. Pasa con un Flow que alguien ya deprecó a mano en WhatsApp Manager, con la cuenta cambiada (`AccountMismatch`), con `AccountUnavailable` o con un manifiesto ilegible. La pantalla sigue diciendo «Se deprecan cuando venzan los envíos abiertos» aunque no haya ninguno.
   - **Arreglo:**
     - ante un 4xx, pasar a Verificar: consultar el estado con GET antes de cualquier otro POST. Si Meta dice DEPRECATED, queda confirmado;
     - los errores permanentes (cuenta distinta, manifiesto ilegible, rechazo después de verificar que sigue publicado) pasan a un estado visible «Requiere atención», con espera creciente;
     - el puerto, el contrato y el front muestran ese estado (BA2 ya prevé «no se pudo confirmar»).
   - **Tests:** 4xx, «ya deprecado», `AccountMismatch` y manifiesto ilegible.
2. **`FindPublishedAsync` puede lanzar una excepción con una definición eliminada** (`FormSelectionReader.cs:38-43`, usado desde `WhatsAppOutgoingSendGuard.cs:65`).
   - **Qué pasa:** el asistente encola respuestas rápidas con un formulario que se elimina antes del despacho. Las subconsultas `.Single()` aplican el filtro de borrado y devuelven NULL sobre un `long`.
   - **Arreglo:** exigir la definición con el set filtrado, para que devuelva null y el mensaje se rechace.
   - **Test:** `FindPublishedAsync` y `FindAcceptedSessionAsync` con la definición eliminada.
3. **El uso de un catálogo cuenta formularios eliminados** (`FormCatalogReader.cs:21-28`, SQL directo sin el filtro global).
   - **Arreglo:** `AND NOT definition."IsDeleted"`, con test.
3b. **El prellenado repite el perfil vigente como texto** (`WhatsAppFlowPrefillPolicy.cs:10`, con `"flow-static-7.3-r8"`).
   - **Qué pasa:** si el perfil sube, el prellenado va a rechazar los valores de las publicaciones nuevas. Lo encontró la revisión de las guías.
   - **Arreglo:** usar `WhatsAppFlowProfile.Revision`, o el criterio «desde r8», con un test.

**Front**

4. **Eliminar no actualiza el panel de Respuestas** (`FormsPage.tsx:85`, `FormEditorPage.tsx:271`).
   - **Qué pasa:** el enlace del formulario eliminado sigue en el panel y abre un 404.
   - **Arreglo:** invalidar también `["form-responses", owner]` al eliminar y al dar de baja.
5. **BA3 muestra textos que no aplican sin envíos abiertos** (`FormDeactivationDialog.tsx:70`).
   - **Qué pasa:** con Flows publicados y sin envíos abiertos (el caso común) dice «Los envíos abiertos se pueden responder hasta que vencen» y «Deprecación pendiente».
   - **Arreglo:**
     - el aviso de envíos, solo con vencimiento;
     - sin envíos abiertos, el texto del tablero: se deprecan ahora;
     - documentar en el puerto que `Pending` también lo informa un formulario activo.
6. **Un error de red o 5xx al dar de baja o eliminar** (`FormDeactivationDialog.tsx:56`, `FormDeletionDialog.tsx:41`).
   - **Qué pasa:**
     - dice «No pudimos confirmar el guardado. Volvé a guardar.», pero en el diálogo no hay nada para guardar;
     - si la baja sí se aplicó, en el editor aparece «Cambió la revisión… Revisar», y en el listado la fila queda en Activos.
   - **Arreglo:**
     - usar el texto de revisar;
     - al releer, si viene `deactivatedAtUtc`, o la definición ya no existe al eliminar, tratarlo como hecho y refrescar.
7. **Una baja confirmada puede no refrescar el editor** (`FormEditorPage.tsx:216`).
   - **Qué pasa:** si `confirmRevision` da false, sale antes de actualizar.
   - **Arreglo:** invalidar igual.
8. **El menú ofrece Reactivar sin `canPublish`,** mientras la cabecera lo oculta.
   - **Arreglo:** usar la misma condición en los dos lugares.
9. **Eliminar no sigue BA4 en un formulario publicado.**
   - **Qué falta:** el aviso «En WhatsApp no se deshace» y el prefijo que queda reservado, con su nombre.
   - **Arreglo:** distinguir borrador de publicado como en el tablero.
10. **Regresión en `PublicationCheckDialog`** (`:79-105`).
    - **Qué pasa:** el envoltorio nuevo quita el espacio entre el resultado y «Esperá…» en todas las publicaciones.
    - **Arreglo:** envolver solo en la reactivación.
11. **Accesibilidad.**
    - Los ítems apagados con motivo de `FormLifecycleMenu.tsx:15` no se alcanzan con el teclado. Que el motivo se pueda leer, como en `RowActions`.
    - `FormSuggestionsSection.tsx:115` usa `role="alert"` sobre un texto que ya está al cargar; va como texto común.

**Tests que faltan:**
- dar de baja desde la fila: diálogo, POST, refresco de lista y facetas, y foco;
- el 409 que lleva a «Revisar»;
- dar de baja o eliminar con un error de red;
- BA3 sin envíos abiertos;
- el refresco del panel de Respuestas.

## Después, sin bloquear

- **El selector de formularios del chat** puede ofrecer durante 30 s un formulario recién dado de baja: su caché es de otra feature. El backend lo rechaza igual, y ya pasaba con las publicaciones. Pide un aviso común en `shared`.
- **Revisiones periódicas:** cada Flow de una versión activa se revisa cada 5 min tomando el lock global de definiciones. Con pocos formularios no se nota; si crecen, filtrar antes de tomar el lock.
- **BLOCKED o THROTTLED** en Meta se leen como desconocido: un POST incierto sobre ese Flow queda «Sin confirmar». Con el arreglo 1 se ve.
- **Desvíos menores del diseño, aceptados:**
  - el error de dar de baja va en el aviso común y no dentro del diálogo;
  - el aviso del editor no lleva la cantidad de respuestas;
  - una reactivación fallida no se ve en el listado: queda «Dado de baja · versión N» y el detalle está en el editor.
- **«No se puede eliminar porque tiene respuestas»** se le muestra a quien solo administra: lo fija el plan.
