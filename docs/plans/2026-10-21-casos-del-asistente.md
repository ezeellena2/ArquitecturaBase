# Casos del asistente

**Estado:** decidido el 2026-10-21 (AS1-A y AS2–AS6), programado en back y front del 2026-11-11 al 2026-11-14, calificado y
probado en la prueba integral; condensado el 2026-11-26. Las reglas están en
[WhatsApp](../features/whatsapp.md#casos-del-asistente--2026-11-11), las lecturas precargadas en
[IA](../features/inteligencia-artificial.md#lecturas-precargadas-y-varias-propuestas--casos-del-asistente-2026-11-12), la
decisión en el [ADR 0021](../decisions/0021-respuestas-redactadas-por-el-asistente.md) y las pantallas en el
[diseño del front](../../../ArquitecturaBaseFront/docs/design/asistente-casos.md). Lo programado se recupera desde la
[procedencia](../history/README.md#cierre-del-2026-11-26). Acá queda lo que sigue abierto.

## Para decidir: un formulario junto con otras acciones

Lo decidido el 2026-10-21 admitía «un texto y un formulario, o dos formularios», y AS3 muestra una ráfaga que responde con un
texto y dos formularios. Lo programado deja ir solo un formulario, o la pregunta de cuál: mezclado con otras acciones no sale
nada y deriva (`replyNotAllowed`, en `WhatsAppAssistantReplyPolicy.PrepareAsync`). El usuario acepta la diferencia o pide que
se programe la mezcla ([W-1](../reviews/2026-11-18-barrido-documental.md#w-1-un-formulario-junto-con-otras-acciones-en-una-respuesta-del-agente)).

## Lo que queda de fotos y documentos

Documentos por WhatsApp entregó la copia, la lectura y el prellenado ([formularios](../features/formularios.md#la-bandeja-documentos-recibidos--doc5-a-2026-11-12)).
Quedan dos cosas, que se deciden junto con ese circuito:

- **Casos por tipo de entrada foto o documento** («cuando manda una foto de un ticket»): hoy el tipo de entrada es Cualquiera,
  Texto o Audio ([W-15](../reviews/2026-11-18-barrido-documental.md#w-15-casos-con-tipo-de-entrada-foto-o-documento)).
- **Probar el asistente con archivos:** «Probar» acepta sólo texto.

## Tercera etapa: agentes

**Qué cambia.** El asistente ya se modela como el **agente principal**, una fila con su perfil, conocimiento, casos, límites y acciones permitidas, así esta etapa agrega más filas y su ruteo sin rehacer nada. Un agente es un asistente especializado: perfil e instrucciones propias, solo el conocimiento que le corresponde, sus casos, sus acciones permitidas y su tono. En la primera etapa hay uno solo, el principal. En esta, el negocio crea varios desde un menú «Agentes». Por ejemplo:
- ventas: catálogo, precios y pedidos;
- soporte: reclamos y el formulario de visita;
- personal interno: licencias y recibos.

**Qué se construye:**
1. **Crear agentes** con su prompt de «qué hace», más casos, conocimiento, acciones y límites, y «Probar el agente».
2. **Ruteo:** a qué agente va una conversación, de una de tres formas:
   - por número de WhatsApp o canal;
   - por tipo de contacto (empleados o clientes, según su cuenta o su rol);
   - un clasificador que lee el primer mensaje y elige el agente. El clasificador es lo último y lleva su propia calificación.
3. **Pases:** un agente pasa la conversación a otro o a una persona, con el motivo visible en el chat.
4. **Un solo motor:** cada agente es una configuración de canal con su conjunto de capacidades, sin otro motor ([ADR 0020][0020]).

**Cuándo conviene.** Cuando el público o el propósito cambian mucho (empleados frente a clientes, o números distintos), o cuando un solo agente junta tantos casos que empieza a confundirse. Mientras tanto, alcanza un agente con casos.

[0020]: ../decisions/0020-motor-unico-de-ia-con-capacidades.md
