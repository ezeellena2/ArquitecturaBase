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

## Sumado: encontrado al reorganizar las guías

Estos puntos no vienen de esta etapa: aparecieron al releer el código para dividir las guías (`ff861297` y `1fab63b`). Van en la misma tanda, cada uno con su test.

12. **Editar un catálogo devuelve «usado por 0»** aunque lo usen formularios (`FormCatalogService.cs:183`, `FormCatalogFiltersService.cs:116`). El detalle y la lista sí cuentan bien.
    - **Arreglo:** devolver el conteo real, como `FormCatalogProposalService.MapAsync`.
13. **BusinessForms compara IDs de canal de WhatsApp escritos a mano:** `"whatsapp-assistant"` y `"whatsapp-manual"` aparecen en `BusinessFormSuggestionAssistantCapabilitySource.cs:31`, `FormCatalogProposalService.cs:131` y `FormMissingRequestService.cs:110-111`.
    - **Por qué importa:** la frontera entre módulos se rompe sin que `ModuleBoundaryTests` lo detecte.
    - **Arreglo:** que el contexto neutral diga si es Asistente o Manual, sin el ID del canal, y que el test de frontera busque también estos literales.
14. **El texto enviado desde el chat queda en la caché de mutaciones** (`ChatComposer.tsx:46-48`, con `{requestId, body}`). Va contra la regla de editores privados de `docs/architecture.md`. Los formularios y las sugerencias ya la cumplen.
    - **Arreglo:** el mismo patrón que ellos.
15. **Redirecciones del cliente de mensajes de WhatsApp:** los clientes de Flows y de medios desactivan las redirecciones automáticas (`WhatsAppInfrastructureRegistration.cs:90,119,146`), pero el de mensajes (`WhatsAppCloudClient`) no.
    - **Arreglo:** desactivarlas igual, con un test.

**Deuda menor, se arregla al tocar esos archivos:**
- `FormDocumentRepository.Add` y `FormPdfDesignRepository.Add` no exigen la transacción, a diferencia del resto.
- Varios lectores de formularios devuelven `*Response` en lugar de `Row` (`backend.md#modelos-response-y-row`).
- Los filtros de la bandeja del chat viven en el estado de la página y no en la URL.
- El guard de catálogos acepta `filters` no nulo en uno Manual.
- Un comentario mal codificado en `WhatsAppMessageRetentionRepository.cs`.

## Después, sin bloquear

- **El selector de formularios del chat** puede ofrecer durante 30 s un formulario recién dado de baja: su caché es de otra feature. El backend lo rechaza igual, y ya pasaba con las publicaciones. Pide un aviso común en `shared`.
- **Revisiones periódicas:** cada Flow de una versión activa se revisa cada 5 min tomando el lock global de definiciones. Con pocos formularios no se nota; si crecen, filtrar antes de tomar el lock.
- **BLOCKED o THROTTLED** en Meta se leen como desconocido: un POST incierto sobre ese Flow queda «Sin confirmar». Con el arreglo 1 se ve.
- **Desvíos menores del diseño, aceptados:**
  - el error de dar de baja va en el aviso común y no dentro del diálogo;
  - el aviso del editor no lleva la cantidad de respuestas;
  - una reactivación fallida no se ve en el listado: queda «Dado de baja · versión N» y el detalle está en el editor.
- **«No se puede eliminar porque tiene respuestas»** se le muestra a quien solo administra: lo fija el plan.
