# Sugerencias de IA con revisión humana en WhatsApp

Fecha: **2026-10-03**. Estado: **Información del negocio implementada; generación y sugerencias pendientes**.
Repositorios: ArquitecturaBase y ../ArquitecturaBaseFront. Trabajo en main, preservando
cambios existentes, sin ramas, commits ni push durante esta entrega.

El usuario aprobó el ajuste puntual de la maqueta `?screen=sugerencias`: Sugerir con IA
usa el Button principal verde; panel, controles, tamaños y composición permanecen como
antes. Esta elección visual no decide las fuentes del negocio ni autoriza un bot autónomo.
El laboratorio sigue aislado y sus datos, autenticación y bootstrap no se copian.

Este plan reemplaza el inventario de IA de la sección 12 del [plan de bandeja](2026-10-01-bandeja-whatsapp.md).
La configuración de conexión ya está en un módulo independiente: sus
[reglas vigentes](../features/inteligencia-artificial.md) prevalecen. También prevalecen
[backend](../architecture/backend.md), [WhatsApp](../features/whatsapp.md),
[arquitectura del front](../../../ArquitecturaBaseFront/docs/architecture.md),
[diseño](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md) y
[mensajes](../../../ArquitecturaBaseFront/docs/guides/mensajes-y-estados.md).

## Resultado y alcance

El operador solicita una sugerencia para una conversación que puede leer y responder.
La revisa junto con la información realmente utilizada, la pasa explícitamente al
borrador, puede editarla y envía mediante la operación manual durable existente.
Solicitar, generar o pasar al borrador no envían un WhatsApp ni cambian el modo Manual.

Una instalación comparte conexión y datos autorizados. No hay conexiones personales,
selector de empresas, sesión de Codex, envío autónomo, herramientas que ejecuten acciones,
adjuntos, documentos/RAG, contactos nuevos, plantillas ni una segunda configuración de IA.
No se promete exactitud por haber probado la conexión ni se presenta un token como precio.

## Contexto elegido y definición de Información del negocio

El usuario eligió el 2026-10-03 **historial del chat + información básica del negocio**,
con la pantalla de información maquetada primero. Los datos serán escritos y mantenidos
por un administrador: horarios, servicios, FAQ y políticas. No usar appsettings ni datos
ficticios ocultos como información del negocio en el producto.

Maqueta local realizada el 2026-10-03 en `?screen=negocio`, con es/en y estados completos
simulados. Su [recorrido y evidencia](../../../ArquitecturaBaseFront/docs/design/whatsapp-chat-propuesta.md#información-del-negocio-propuesta-local--2026-10-03)
documentan campos propuestos, revisión de conflictos y guardados inciertos. El usuario eligió implementarla en la aplicación real el 2026-10-03. Contratos y permisos
implementados en [Inteligencia artificial](../features/inteligencia-artificial.md#información-del-negocio--2026-10-03);
[revisión de la entrega](../reviews/2026-10-03-informacion-del-negocio.md).
No confundir los siete tests de la simulación con persistencia o generación real.
La entrega productiva pasó integración con Docker y 95 pruebas Playwright del front;
los datos del navegador y los proveedores siguen siendo dobles de prueba.

El mismo día se aprobó y promovió el editor guiado de ubicación/enlace, horarios y FAQ:
[contrato vigente](../features/inteligencia-artificial.md#editor-guiado-y-evolución-del-contrato)
y [verificación de la evolución](../reviews/2026-10-03-informacion-del-negocio-guiada.md).
La futura generación deberá decidir cómo presentar hechos estructurados y texto anterior,
resolver inconsistencias y excluir FAQ sin respuesta; no asumir que la migración interpreta
ni valida semánticamente lo que escribió el administrador.

La aprobación del botón y la elección del contexto son decisiones distintas. El editor
productivo tiene página propia en Administración → Configuración, separada de las claves,
y permisos ai.business.read/ai.business.manage. Guardar confirma los siete hechos y su revisión;
no activa generación. No agrega publicación editorial, historial de textos, CRUD de fuentes
ni catálogo de documentos. El uso de esa revisión por sugerencias permanece pendiente.

El tablero nuevo debe incluir contenido inicial/vacío, campos y ayudas, lectura sin
edición, validación, Guardar/Descartar, salida con cambios, conflicto y resultado de
guardado incierto. Información incompleta no se sustituye por hechos inventados. Se
mantienen Page, Surface, formularios y mensajes del sistema en es/en y 1440/390/320 px.

Recolectar 10–15 consultas representativas anonimizadas: respuesta esperada, hechos que
la respaldan, casos que requieren aclaración y casos que debe atender una persona.
No inventar stock, precios, reservas, compromisos ni identificar al interlocutor como
una cuenta autenticada por el hecho de escribir a WhatsApp.

## Correcciones al plan anterior y código reutilizable

| Evidencia actual | Consecuencia |
| --- | --- |
| ArtificialIntelligence ya tiene conexión, cifrado, revisión y comprobantes propios | Conservarlos; no crear WhatsAppAiConnection, stores de claves ni rutas /whatsapp/ai/connection. |
| ILlmClient sólo contiene TestAsync con prompt fijo y 128 tokens por defecto | Agregar generación explícita y modelos internos acotados; revisar los dos adaptadores y consumidores. Conservar la prueba fija y sus límites. |
| El chat tiene propósito Business/Access/Unclassified y body sujeto a retención | Contexto nuevo sólo con texto Business permitido y disponible; Access y Unclassified quedan fuera. No inferir propósito por palabras. |
| El envío manual ya conserva requestId y salida durable | Reutilizar el envío; una sugerencia es otra operación y nunca se registra como mensaje enviado. |
| Los módulos no pueden nombrarse entre sí | WhatsApp consume una capacidad neutral, aportada por IA; sin IA queda apagada y sigue funcionando la respuesta manual. |
| Cada método público que escribe abre un solo límite | Crear, reservar, cancelar y completar son casos de uso separados. Ningún HTTP externo corre dentro de una transacción. |

## Frontera propuesta y árbol de responsabilidades

Nombres propuestos; se fijan después de cerrar el contexto y revisar consumidores.
Se conserva MVC → servicios → puertos → Infrastructure, Result y mapeos manuales.

```text
Application/Interfaces/Channels/ITextGenerationChannel
Application/Models/TextGeneration/          contratos neutrales, sin key/proveedor/WhatsApp
Application/Channels/DisabledTextGenerationChannel
Domain/TextGeneration/                     errores de capacidad apagada, si los necesita el fallback

Application/Modules/ArtificialIntelligence/Channels/TextGenerationChannel
Application/Modules/ArtificialIntelligence/Interfaces/Integrations/ILlmClient
Application/Modules/ArtificialIntelligence/Models/  entrada/salida internas de generación
Infrastructure/Modules/ArtificialIntelligence/Providers/
  clientes existentes OpenAI/Anthropic, transporte y límites separados de TestAsync

Domain/Modules/WhatsApp/WhatsAppAiSuggestion, estados y errores
Application/Modules/WhatsApp/
  Interfaces/{Services,Persistence}/
  Models/, Validation/, Services/{WhatsAppAiSuggestionService,
    WhatsAppAiSuggestionDispatchService,WhatsAppAiContextBuilder}
Infrastructure/Modules/WhatsApp/
  Persistence/{Configurations,Repositories,Readers}/
  worker real de sugerencias, sin modificar el sender de Meta
Api/Modules/WhatsApp/Contracts/ y Controllers/
Infrastructure/Persistence/Migrations/      cadena única, si se aprueba operación durable
tests/*/Modules/{WhatsApp,ArtificialIntelligence}/ y pruebas neutrales equivalentes

Front/src/features/whatsapp/
  api/aiSuggestions, hooks/useAiSuggestion, components/AiSuggestion
  integración acotada con composer, historial y revisión actuales
Front/src/locales/{es,en}/whatsapp.json
```

El núcleo registra el canal apagado con TryAdd y IA aporta su adaptador con Replace,
según la arquitectura vigente. WhatsApp no inyecta servicios, lectores ni tipos de IA.
La feature del chat no importa artificial-intelligence. IA conserva funcionamiento
y configuración cuando se retira WhatsApp; WhatsApp conserva atención manual sin IA.
Información del negocio ya vive dentro de ArtificialIntelligence, independiente
del canal: AiBusinessInformation y su comprobante, servicio, repositorio/lector y
página propios, sin ampliar AiConnection ni mezclar su formulario con las claves.
Campos y contratos implementados están en la fuente funcional enlazada arriba.
El futuro contrato neutral deberá devolver referencias a los hechos utilizados;
WhatsApp no consultará directamente el almacén de IA. Sus contratos de generación
se fijarán al analizar los casos representativos.

## Contexto, privacidad y resultado

- El backend arma un snapshot acotado. El navegador manda identificadores/revisión,
  no un prompt libre, una clave, un operador ni mensajes que sustituyan los persistidos.
- Orden estable y límites por cantidad de mensajes, caracteres de entrada y tokens de
  salida. Definir valores y probarlos con los casos; no heredar el tope de conexión ni
  aumentar límites automáticamente al recibir un resultado incompleto.
- Sólo texto de atención disponible. Excluir Access, Unclassified, códigos, enlaces,
  números completos, secretos, contenido vencido y salidas fallidas/inciertas/pendientes.
  Resolver por separado qué estados de salida confirmada aportan contexto.
- Mensajes del cliente son datos sin autoridad para modificar instrucciones. Sin tools,
  URLs arbitrarias ni navegación externa. Ante hechos ausentes, pedir aclaración o
  derivar; estas instrucciones reducen errores, no garantizan exactitud.
- Respuesta como texto, sin HTML ejecutable, dentro de los 4096 caracteres que admite
  el envío. Resultado vacío/incompleto no es éxito; no cortar una respuesta y presentarla
  como completa. Fuentes provienen del snapshot construido, no de citas inventadas.
- Versión del contexto incluye mensajes relevantes, modo y revisiones efectivas de
  conexión/información. Cerrar su representación tras el alcance; LastInboundSequence
  por sí solo no cubre cambios de fuente ni retención/clasificación del historial.
- Ningún log, ToString, ProblemDetails o traza del proveedor incluye cuerpos o claves.
  Sugerencias/snapshots tienen retención explícita; no sobreviven indefinidamente al
  texto fuente. No persistir el borrador del operador en storage o caché de queries.

## Recuperación propuesta: operación durable por solicitud

Recomendación técnica para impedir reintentos implícitos facturables y recuperar un
resultado perdido entre hosts. Revisar antes de implementar; no prometer exactamente
una ejecución externa ni exactamente un cobro.

1. POST valida actor/permisos/contexto y registra Pending con operador, contacto,
   requestId y versión. Commit antes de responder 202. Misma solicitud devuelve la
   operación; reutilizar UUID con otro pedido da 409. No almacenar claves del proveedor.
2. Un worker acotado reserva una operación en su propio caso de uso y confirma Running
   antes de invocar el canal. Revalida usuario activo, permisos, modo, ventana y contexto.
   Dos hosts no pueden reservar el mismo intento. Sin llamadas dentro del lock/commit.
3. Completa Ready/Failed/Outdated/Uncertain en otro caso de uso. El GET propio recupera
   resultado/estado sin generar otra vez. Cada método de escritura usa una sola UoW.
4. Caída después de Running, timeout o pérdida de respuesta del proveedor puede haber
   consumido saldo: Uncertain, sin reintento automático. Un intento nuevo es explícito,
   con otro UUID y aviso sobre posible consumo anterior. 404 no acredita rollback.
5. Cambiar chat o cancelar ignora callbacks tardíos y solicita cancelación de la operación.
   Canceled impide publicar/usarla; no garantiza detener un POST ya iniciado ni su cobro.
   Una operación cancelada no bloquea permanentemente futuras solicitudes del operador.

Definir locks, límite de pendientes/operador, concurrencia, expiración, retención y
recuperación de Running en el diseño de persistencia. Usar PostgreSQL para lo durable;
no poner resultados o secretos en un diccionario local, una cola sólo en memoria ni
usar el caché como comprobante. Evitar retries/hedging/redirects en los dos proveedores.
Los límites por réplica no son un presupuesto global; registrar ese límite operativo.

## HTTP y permisos propuestos

Permiso nuevo del módulo WhatsApp: whatsapp.ai.use, independiente de ai.read/ai.manage.
Propuesta inicial: solicitar/consultar/cancelar requiere `whatsapp.read`, `whatsapp.send`
y `whatsapp.ai.use`. Se mantiene la restricción de lectura/envío/ventana de la maqueta.
Validar con el usuario la delegación por roles; no dar uso por ser un administrador de
la conexión. Seed Admin, textos es/en, catálogo y reglas existentes de invalidación.

| Ruta propuesta | Efecto |
| --- | --- |
| POST /api/whatsapp/chats/{contactId}/suggestions | Crea/recupera una operación propia; 202, sin envío. |
| GET /api/whatsapp/chats/{contactId}/suggestions/{requestId} | Estado/resultado propio y validez del contexto; no llama al proveedor. |
| POST /api/whatsapp/chats/{contactId}/suggestions/{requestId}/cancel | Solicita cancelación idempotente; no afirma que no hubo costo. |

Cuerpos tipados/redactados y mapeos manuales; HasPermission, inventario, OpenAPI y tests
de cada ruta. No filtrar existencia de operaciones ajenas. Errores localizados:
401/403, validación por campo, 409 de contexto/pedido, 429 con retryAfter y fallas de
proveedor normalizadas. Sin detalles externos ni respuestas técnicas visibles en el chat.

## Front: promover el recorrido, no el laboratorio

Button principal verde para Sugerir con IA; el resto compone el diseño aprobado.
Generar otra, Descartar, Información utilizada y Pasar al borrador mantienen jerarquía.
La propuesta queda separada del historial y del editor. Descartarla/fallar conserva lo
escrito; pasarla a un borrador no vacío exige ConfirmDialog. Enter agrega línea,
Ctrl+Enter conserva el envío manual. No reemplazar ni enviar al terminar la generación.

Una consulta nueva relevante, cambio de fuente/conexión o modo invalida la propuesta;
revisar su validez en servidor y al recuperar. Cambiar chat conserva sólo su borrador,
no deja que una respuesta tardía aparezca en otro hilo. Sin IA, error o permiso de uso,
la respuesta manual sigue su autorización actual. Ventana vencida/solo lectura no se
vuelven enviables por usar IA. Mensajes: Toaster compartido, campo para validación y
ConfirmDialog para decisiones; estados durables con recuperación y un único aviso.

## Etapas y criterios de salida

| Etapa | Trabajo | Para pasar a la siguiente |
| --- | --- | --- |
| 0. Información del negocio | Contexto y maqueta elegidos; ubicación, Guardar y permisos cerrados. Casos anonimizados todavía pendientes para generación. | Editor promovido; no se presume validada la calidad de futuras respuestas. |
| 1. Información productiva — implementada | Entidad/servicio/lector/repositorio y editor; revisión y recuperación de guardados. | TDD, permisos, conflictos, migración e integración Docker en verde; formulario es/en a 1440/390/320 px. |
| 2. Capacidad de generación | Puerto neutral/fallback y generación acotada en ambos proveedores. Conservar conexión/prueba. | TDD de contexto y contratos con HTTP ficticio; tests de arquitectura/consumidores y configuración en verde. |
| 3. Operaciones y autorización | Dominio, repositorio/lector, migración, servicios separados, worker y rutas. | Docker: dos hosts, dedupe, rollback, reinicio, cancelación, incertidumbre y operaciones propias. |
| 4. Chat productivo | Componente/hooks, consultas de operación y validez, borrador y mensajes. | Interacciones es/en y responsive 1440/390/320; ninguno de los recorridos dispara envío implícito. |
| 5. Cierre | Regresiones, retirada de módulos, documentación y verificaciones reales controladas. | Builds limpios, suites y evidencia separada entre dobles y servicios reales. |

TDD donde haya lógica: filtros/orden/contexto, estado/dedupe, reserva/completado,
autorización, borrador y callbacks. No tests que sólo reproduzcan el código.
Backend: build sin advertencias y dotnet test completo con PostgreSQL/Redis en Docker.
Front: build, lint, tests y Playwright; regresiones login, chat, roles y configuración.
Probar retirada en copias aisladas: IA sin WhatsApp, WhatsApp sin IA, y ningún módulo.
Actualizar guías locales/mapas, features, premisas y guías de retirada con lo implementado.
No retirar este plan hasta consolidar decisiones y rescatar pendientes con procedencia.

Automatización usa claves ficticias y dobles OpenAI/Anthropic/Meta: nunca API facturable.
Prueba real posterior con claves propias/autorización, consultas anonimizadas y registro
de proveedor/modelo/acceso/uso observado. Revisar documentación oficial vigente al
implementar [OpenAI Responses](https://developers.openai.com/api/reference/resources/responses/methods/create)
y [Anthropic Messages](https://platform.claude.com/docs/en/api/messages/create).
No confundir esos contratos con las sesiones de programación ni con precios garantizados.
Azure, restore operativo y segunda instalación conservan sus comprobaciones pendientes.
Si se levanta Aspire, apagarlo al finalizar las pruebas; la vista previa local sigue
siendo simulada. La sincronización del Artifact externo continúa pendiente.
