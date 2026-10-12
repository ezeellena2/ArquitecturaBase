# Sugerencias de IA con revisión humana en WhatsApp

Estado: **propuesta, sin implementar**. Depende del [ADR 0020](../decisions/0020-motor-unico-de-ia-con-capacidades.md)
(propuesta aprobada en dirección) y del motor con canales que generaliza la
[Etapa 5 de formularios](formularios/etapa-5-asistente.md). Lo ya construido que reutiliza tiene su
fuente en [inteligencia artificial](../features/inteligencia-artificial.md) y [WhatsApp](../features/whatsapp.md#chat-de-atención--2026-10-02).

## Qué se busca

En Atención manual, el operador pide una **respuesta de texto sugerida** para un chat que puede leer y
responder. La revisa junto con la información utilizada, la pasa al borrador, la edita y la envía con el
envío manual durable que ya existe. Pedir, generar o pasar al borrador nunca envía un WhatsApp ni cambia el modo.

No es lo mismo que la Etapa 5: allí el asistente sugiere o manda **formularios** (H1/H2) y no contesta
consultas generales. Acá se sugiere texto libre y siempre lo envía una persona.

## Punto de partida comprobado

- No hay nada de sugerencias en producción: ni rutas, ni permiso, ni entidad, ni componente en
  `features/whatsapp`. El tablero `?screen=sugerencias` del laboratorio es la única pieza, con datos ficticios.
- Se reutilizan: la conexión compartida y su cifrado; Información del negocio (siete campos y `details`,
  permisos `ai.business.*`, `/configuracion/negocio`); la fuente tipada `business.information` y la operación
  durable de Inicio ([fuentes](../features/inteligencia-artificial.md#fuentes-y-autorización),
  [ADR 0017](../decisions/0017-inicio-asistente-y-operaciones-durables.md)); el envío manual con `requestId`
  y tope de 4096 caracteres, y el propósito Business/Access/Unclassified de cada mensaje
  ([WhatsApp](../features/whatsapp.md#propósito-migración-y-recorridos--2026-10-02)).
- El recorrido de pantalla está aprobado: Sugerir con IA con el Button principal verde; Generar otra,
  Descartar, Información utilizada y Pasar al borrador; ConfirmDialog si ya hay borrador; Enter agrega
  línea y Ctrl+Enter envía; la propuesta queda desactualizada si llega otro mensaje
  ([tablero y estados](../../../ArquitecturaBaseFront/docs/design/whatsapp-chat-propuesta.md#sugerencias-con-revisión-humana-nueva-propuesta--2026-10-02)).

## Decisiones tomadas

- **Contexto:** historial del chat + información básica del negocio. Los datos los mantiene un
  administrador en su editor; nunca appsettings ni datos ficticios ocultos.
- **Una conexión por instalación.** Sin conexiones personales, selector de empresas, sesión de Codex,
  herramientas que actúen, adjuntos, documentos/RAG, contactos ni plantillas.
- **Arquitectura (ADR 0020):** la sugerencia es un canal del motor único. Ofrece dos capacidades de
  lectura: el historial del chat, que aporta WhatsApp, y `business.information`, que ya aporta IA. La
  salida terminal es el texto propuesto con los IDs de evidencia del turno. No se crea un puerto de
  generación propio, ni una entidad de sugerencias de WhatsApp, ni otro worker: la recuperación,
  la incertidumbre y las cuotas son las de la operación del motor ([ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md):
  una llamada facturable sí justifica esa robustez).
- **Independencia:** WhatsApp no nombra IA ni lee su almacén; sin IA, la atención manual no cambia. La
  feature del chat en el front no importa `artificial-intelligence`.
- **Entrada:** el navegador manda identificadores y revisiones; nunca un prompt libre, una clave, el
  operador ni mensajes que reemplacen los persistidos.

## Reglas de contexto y resultado

- Solo texto Business disponible. Quedan afuera Access, Unclassified, códigos, enlaces, números
  completos, secretos, texto ya borrado por retención y salidas fallidas, inciertas o pendientes. No
  se infiere el propósito por palabras.
- Los mensajes del cliente son datos sin autoridad sobre las instrucciones. Sin URLs ni navegación.
  Si faltan hechos, la sugerencia pide aclaración o deriva: no inventa stock, precios, reservas ni
  compromisos, y escribir por WhatsApp no identifica una cuenta.
- Texto plano de hasta 4096 caracteres. Vacío o incompleto no es éxito, y no se recorta para
  presentarlo como completo. «Información utilizada» sale de la evidencia, no de citas del modelo.
- La validez cubre mensajes relevantes, modo y revisiones de conexión e información: cualquiera que
  cambie la desactualiza. La última secuencia entrante sola no alcanza.
- Una respuesta tardía nunca aparece en otro chat. Cancelar no garantiza que no hubo costo.
- Sugerencias y contexto no sobreviven al texto que les dio origen (90 días de WhatsApp). Ni logs ni
  `ToString` con cuerpos; el borrador no va a storage ni a la caché de consultas.

## Pendientes y criterio de cierre

| Pendiente | Se cierra cuando |
| --- | --- |
| Motor con canales y ADR 0020 aceptado. Revisar por canal la unicidad de una operación activa por actor de Inicio, que hoy impediría pedir una sugerencia con una consulta de Inicio en curso. | ADR aceptado y canal «sugerencia al operador» disponible con sus pruebas de la Etapa 5. |
| Casos de evaluación: 10 a 15 consultas anonimizadas con respuesta esperada, hechos que la respaldan, casos de aclaración y casos para una persona. | Casos versionados y corridos antes de habilitar perfil o instrucciones (ADR 0020, punto 6). |
| Presentación de hechos: combinar los textos anteriores con `details`, resolver inconsistencias y excluir FAQ sin respuesta. Hoy `business.information` entrega también preguntas con respuesta vacía. | Decisión registrada en la feature y prueba de la fuente. |
| Permiso de uso: propuesta `whatsapp.ai.use`, junto con `whatsapp.read` y `whatsapp.send`, independiente de `ai.read`/`ai.manage`; o el mismo criterio que H2. Delegación por roles a confirmar. | Decisión del usuario y alta según [permiso nuevo](../guides/permiso-nuevo.md). |
| Presupuesto del canal: cantidad de mensajes, caracteres de entrada y tokens de salida; sin heredar el tope de la prueba de conexión. | Valores calibrados con los casos de evaluación. |
| Tablero: la sugerencia de texto convive con la de formulario (H2) arriba del compositor. | Tablero dibujado y elegido antes de programar ([tableros de la Etapa 5](../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#los-tableros)). |
| Prueba real con claves propias y consultas anonimizadas; registrar proveedor, modelo y uso sin secretos. | Evidencia separada de los dobles; los pendientes de cuentas reales siguen en [verificación y producción](verificacion-y-produccion.md). |

## Fuera de alcance

Envío autónomo de texto generado o un bot de respuestas (el modo Asistente de formularios es de la
Etapa 5), capacidades que escriben (piden un ADR nuevo), adjuntos e imágenes, RAG, contactos y
plantillas de Meta.

## Cómo se cierra

Checks por corte y al cerrar, según ADR 0018 y [validar un cambio](../guides/validar-un-cambio.md).
Docker: recuperación por la operación del motor, respuesta tardía después de cambiar de chat, permiso
revocado, contexto desactualizado e IA no disponible. Front en es/en a 1440/390/320 px, sin envío
implícito en ningún recorrido. Retirada: WhatsApp sin IA e IA sin WhatsApp siguen funcionando.
