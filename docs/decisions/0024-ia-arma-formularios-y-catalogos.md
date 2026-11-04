# ADR 0024: La IA arma formularios y catálogos a pedido

## Estado

**Aceptada**, 2026-11-04, por decisión del usuario. Recoge el diseño que eligió en [la IA arma formularios y catálogos](../plans/2026-11-04-ia-arma-formularios-y-catalogos.md)
(IA1-A e IA3-A). Amplía el [ADR 0017](0017-inicio-asistente-y-operaciones-durables.md) en lo que hace Inicio, el
[ADR 0020](0020-motor-unico-de-ia-con-capacidades.md) con capacidades que proponen configuración en varios canales y el
[ADR 0022](0022-lectura-de-documentos-con-ia.md) a leer documentos para armar. Rige desde la etapa 1 de ese plan.

## Contexto

El usuario quiere pedirle a la IA, en el chat de Inicio o por WhatsApp, con un texto, un archivo o un audio, que arme un
formulario o un catálogo, verlo y seguir en el editor con todo cargado.

Hoy:
- Inicio solo consulta. El ADR 0017 dejó afuera las escrituras de negocio, y su diseño no tiene adjuntos ni audio.
- El ADR 0020 admite capacidades que proponen. La única que propone configuración es el catálogo de personas con filtros
  (H4), que por WhatsApp se crea inactivo en cuanto el asistente lo propone, sin un paso de confirmación.
- El ADR 0022 lee los documentos que llegan por WhatsApp para cargarlos en un formulario, y al proveedor le manda solo el
  documento y las preguntas del formulario candidato. Armar un formulario desde un documento (DOC6) está decidido, pero
  solo desde la bandeja.

Armar configuración a pedido toca a los tres: un canal que solo consultaba propone configuración, recibe archivos y audios,
y le manda al proveedor otra cosa que la que fija el ADR 0022.

## Decisión

1. **Inicio también propone.** Además de consultar, el chat de Inicio propone formularios y catálogos a quien tiene el
   permiso de diseñarlos o de administrarlos, con capacidades de BusinessForms en el motor único. Inicio sigue sin escribir:
   la propuesta es un DTO, y crear es una acción de la persona («Crear borrador» o «Crear catálogo») que ejecuta
   BusinessForms con su servicio, una sola UoW y los permisos actuales.
2. **Crear siempre lo pide una persona, en todos los canales.** La IA nunca crea sola, ni publica, activa, importa o cambia
   lo que existe:
   - un formulario se crea en borrador, sin publicar; un catálogo, inactivo y con su propuesta a la vista, como en H4;
   - por WhatsApp, crear es un botón de la última propuesta, y H4 deja de crear al proponer y pasa a ese botón;
   - crear es idempotente por propuesta, y una propuesta reemplazada o vencida no se crea.
3. **Una sola propuesta para todos los canales.** BusinessForms la valida con las reglas de la autoría y la presenta; Inicio,
   WhatsApp y Documentos recibidos (DOC6) la muestran a su manera, sin interpretar un formulario.
4. **Qué se le manda al proveedor para armar:**
   - el pedido, con el audio ya transcripto;
   - el archivo preparado como en el ADR 0022 y, de una planilla, solo los encabezados y unas filas de muestra;
   - los nombres y los campos de los formularios y catálogos que la persona puede ver.

   Nunca respuestas, opciones cargadas ni datos de personas. Lo leído es un dato, nunca una instrucción. Rigen la falta de
   retención en el proveedor y el registro de hash y tamaño del ADR 0022.
5. **Archivos y audios en Inicio.** Inicio acepta un archivo por mensaje, con los tipos y los topes del ADR 0022, más CSV
   para una planilla, y audios grabados en el navegador, que se transcriben en el motor antes de interpretarse (punto 4 del
   ADR 0020). El archivo se guarda como copia privada y vence con el contenido de la conversación; la importación de un
   catálogo lee esa copia y compara su huella.
6. **Evaluación.** Antes de habilitar el perfil, las capacidades se califican con casos ficticios: pedidos escritos y
   dictados, planillas en papel en PDF y en fotos (también movidas), Excel con y sin encabezados, pedidos ambiguos, nombres
   que ya existen e instrucciones escondidas en el archivo.

## Consecuencias

- **Inicio:** su catálogo pasa de seis definiciones a once o doce, según los permisos, en el tope de doce por turno. Sumarle
  otra capacidad obliga a agruparlas o a revisar ese tope.
- **Front:** Inicio muestra la tarjeta de una propuesta por un punto de extensión, sin nombrar el módulo, y el clip aparece
  solo con el permiso.
- **Audio de la web:** el perfil de transcripción tiene que admitir lo que graba el navegador (WebM o MP4), o el servidor
  convertirlo, y volver a calificarse.
- **H4 cambia:** por WhatsApp, el catálogo de personas se crea con el botón y no al proponerlo; cambian sus pruebas y su
  texto.
- **Pruebas de arquitectura:** siguen las del ADR 0020 (ningún módulo llama al transporte fuera del motor y las capacidades
  no reciben `IUnitOfWork`), y se suma que solo el servicio dueño crea desde una propuesta.
- **Costos:** armar usa las cuotas del motor por actor, canal y propósito; leer archivos, los topes del ADR 0022.

## Alternativas descartadas

- **Que la IA cree el borrador al proponer,** como hace H4: lo que entendió se ve recién en la app y cada pedido deja un
  borrador aunque no sirva. El usuario la descartó (IA3-B).
- **Un editor sin guardar con lo propuesto,** que se guarda recién con «Guardar borrador»: contradice DOC6, y desde WhatsApp
  el enlace dependería de que la propuesta no venza.
- **Un chat aparte para armar, o un panel de IA en el editor:** duplica la conversación y el motor que unificó el ADR 0020.
- **Mandarle al modelo el Excel entero o las opciones de los catálogos:** suma datos sin necesidad; para proponer alcanzan
  los encabezados, unas filas y los nombres.
- **Que la IA cambie formularios o catálogos que existen:** necesita comparar versiones y cuidar lo publicado; queda para
  una decisión propia.
