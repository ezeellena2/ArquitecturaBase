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

## Contraste y corrección — 2026-10-21

Alcance confirmado por el usuario: todos los puntos 1–15, incluido 3b. Los hallazgos se contrastaron con sus consumidores y patrones existentes. El 3b es una duplicación real, sin fallo con el perfil r8 vigente; el 9 es parcial porque ya existían los textos genéricos de reserva y deprecación. En el 11, el motivo ya estaba en el DOM, pero no era alcanzable con las flechas del menú. La deuda menor y «Después, sin bloquear» conservan su alcance pendiente.

| Punto | Corrección | Regresión y estado |
| --- | --- | --- |
| 1 | Backend `432070c4`: rechazo exige GET antes de repetir; cuenta/manifiesto/rechazo persistente muestran NeedsAttention con espera durable creciente. Contrato/exportador `a8e5a90f`; presentación front `f27c540` con zod estricto y texto localizado en listado, editor y diálogo. | Rojo→verde y áreas Domain 187, Application 665, recursos 88, cliente 20, SQL 3 y arquitectura pertinente 150. Migración aditiva y build 0 advertencias; PostgreSQL aprobado en el cierre conjunto. |
| 2 | Backend `dc8af3f8`: la selección publicada exige una definición no eliminada; conserva versiones históricas y dadas de baja. | SQL 5/5 tras rojo; regresión de lector y fuente aceptada antes/después de DELETE aprobada con PostgreSQL en el cierre conjunto. |
| 3 | Backend `48c66359`: el SQL de uso excluye eliminados y conserva COUNT DISTINCT y dados de baja. | SQL rojo→verde; HTTP con dos definiciones y dos preguntas por catálogo comprueba detalle/lista tras borrar y dar de baja, aprobado en el cierre integrado. |
| 3b | Backend `8be1da5c`: la política y el fixture usan el perfil del compilador/fuente vigente. | Refactor preventivo: ambos valores r8 ya coincidían; 6/6 pruebas de prellenado, con controles del perfil anterior. |
| 4 | Front `02c5076`: baja y eliminación invalidan también la raíz de Respuestas del dueño actual. | Pruebas de fila y editor observan la nueva lectura del panel; tests rápidos, TypeScript y lint aprobados. |
| 5 | Front `08fffcf`: BA3 muestra el plazo sólo cuando hay envíos abiertos; sin ellos explica el retiro inmediato tras confirmar la baja. | Regresiones de ambos casos; 16 tests del área, TypeScript y lint aprobados. |
| 6 | Front `2bedb9f`: una baja o eliminación sin resultado confirmado consulta primero; reconoce la baja observada o el 404 tipado sin repetir el efecto ni perder el borrador. | 40 tests del área, TypeScript, lint y revisión aprobados; GET fallido o ajeno no se trata como éxito. |
| 7 | Front `55a7b1d`: el editor invalida metadata y navegación aunque rechace adoptar otra revisión; conserva cambios locales. | Regresión con hook real y borrador modificado; TypeScript, lint y revisión aprobados. |
| 8 | Front `58f1922`: menú y cabecera comparten la capacidad de publicar/reactivar, conservando los permisos de administrar y eliminar. | Rojo→verde y 18 pruebas del área, TypeScript, lint y revisión aprobados. |
| 9 | Front `77f0066`: el diálogo nombra el prefijo reservado y agrega la advertencia irreversible sólo al eliminar un publicado. | Cuatro regresiones nuevas, 22 pruebas del área, TypeScript, lint y revisión aprobados; escenas BA4 aprobadas en el cierre visual y recorrido móvil. |
| 10 | Front `11dd92e`: el envoltorio se limita a reactivación y conserva el espaciado de publicación normal. | 19 pruebas del componente, TypeScript, lint y revisión aprobados; seis casos geométricos 429 es/en a 1440/390/320 aprobados en el cierre visual, con sus capturas inspeccionadas. |
| 11 | Front `00c3f93`: los motivos no accionables se alcanzan por teclado con aria-disabled y selección protegida; BA6 no anuncia como alerta su estado inicial. | Tres regresiones nuevas, 32 pruebas del área, TypeScript, lint y revisión aprobados. |
| 12 | Backend `2a9f6d82`: edición Manual/Filters devuelve el conteo real, oculto sin Read y sin consultar al fallar. | Rojo→verde 6 casos; área 987 y arquitectura 160 verdes, build 0 advertencias; se preservaron los rojos de P13 hasta su corrección. Dos casos HTTP aprobados con PostgreSQL en el cierre conjunto. |
| 13 | Backend `6314f6f5`, `dc709706` y `07a16c23`: modo neutral capturado y revalidado, ID opaco fuera del dueño, alcance y huella ligados al modo; el arnés conserva el modo original de su ledger. | Rojo→verde; Application 2438, huella 20, arquitectura 161, controles offline 117 y build sin advertencias. La frontera IL detecta los literales privados; no cambiaron capacidades, esquemas ni instrucciones del modelo y no se repitió la evaluación real. |
| 14 | Front `fc306b7`: envío privado local al componente, sin cuerpo ni resultado en MutationCache. | Cinco regresiones nuevas; 78/78 pruebas de chat/formularios, TypeScript, lint y revisión aprobados. |
| 15 | Backend `68369657`: el cliente Cloud desactiva redirecciones como Flows y medios. | Configuración real de los cuatro transportes: rojo sólo en Cloud y 4/4 en verde. Build de solución 0 advertencias y 0 errores. |

Los cortes del review quedaron cerrados con sus pruebas rápidas. El cierre conjunto con la mejora de latencia aprobó **5236/5236 pruebas backend**, con PostgreSQL y Redis en Docker, y build de solución con **0 advertencias y 0 errores**. Fuente `49e14c21`, run `20261021-141839-b590895e7fee4a4c8bbd4aeecd648066`: **20 cohortes, 356 capturas HTTP y 63 fuentes únicas**, con 30 capturas de ciclo de vida, consumidas sin alterar sus bytes ni SHA256.

Front: **2511/2511 tests en 237 archivos**, build, lint y arquitectura **49/49** aprobados; visual **585/585**, con las 127 referencias intactas y sin cambios de tolerancia. El recorrido en IAB con datos sintéticos comprobó BA1-A, BA2, BA3, BA4 y BA6 en es/en a 390/320, incluidos teclado, tabla contenida, diálogos y NeedsAttention. Los seis casos 429 se inspeccionaron en la visual. La [evidencia de diseño](../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#cierre-local-de-baja-reactivación-y-eliminación--2026-10-21) conserva las capturas; la [espera del asistente](../features/whatsapp.md#espera-del-asistente-y-avisos-locales--2026-10-21) registra la comparación local antes/después con proveedores simulados. Se ejecutó `aspire stop`, sin AppHost en ejecución. No se ejecutó el piloto con Meta ni se repitió la evaluación del modelo real: sus capacidades, esquemas e instrucciones no cambiaron.
