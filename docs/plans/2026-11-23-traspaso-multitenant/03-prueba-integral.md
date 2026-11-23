# Traspaso 18 · 03 · La prueba integral, del 21/11 al 23/11

Sigue al [05 del 16](../2026-11-21-traspaso-multitenant/05-prueba-integral.md), que la dejó el 21/11 a las 12:30. Esto es
su resumen al 23/11 a la mañana. La forma de hacerla no cambió (un recorrido único de 181 pasos, sólo se arregla lo que
bloquea, un registro fuera de Git con observaciones numeradas). Lo nuevo: WhatsApp quedó probado entero y aparecieron las
observaciones O31 a O49.

## 1. Dónde va

| Sección | Hecho desde el 16 | Falta |
| --- | --- | --- |
| 1. Ingreso y cuenta | — | El 17, que es opcional |
| 2. Usuarios y roles | — | Recargar la página en el 36, a mano |
| 3. Formularios, de los catálogos a la carga | El 71 con datos (cada ítem usa su propio rubro) | El 76, con un celular real |
| 4. WhatsApp | Toda: del 82 al 89 con tres formularios de prueba armados para eso, el 80 y el 81 con el perfil r16 (O29) | Ver la coma en el celular |
| 5. Respuestas, planilla, aprobación, reglas y avisos | El 102, el 103, el 106, el 107, el 110 y el 117; el 122, por los tests | El 123, opcional; el 124 quedó a medias (O41) |
| 6. IA y Casos | Lo de WhatsApp, del 138 al 144 y del 150 al 157, también el audio | El micrófono (126, 134, 136 y 137) |
| 7. Documentos | Toda, del 159 al 166 | — |
| 8. ARCA, sin emitir | — | El 169, con la CUIT del usuario |
| 9. Configuración, salud y toda la app | El 176, el 178 y el 179; el 175 y el 181 a medias | El 180, con un secreto; la primera carga de los esqueletos (175) y una recarga sin la Api (181), a mano |

## 2. Lo que cambió por la prueba

Decisiones del usuario que entraron, o quedaron tomadas, mientras se probaba:

| Qué | Cuándo y cómo quedó | Dónde |
| --- | --- | --- |
| El tope de ofrecimientos del asistente, configurable (TO1-A, con O36 y O39) | 22/11, en la app | [01](01-reglas-en-configuracion.md) §2 |
| Ninguna regla de negocio fija en el código: RC1-A a RC7-B | 22/11; casi todo en `main`, la tanda de WhatsApp lista sin commitear | [01](01-reglas-en-configuracion.md) |
| Ordenar las tablas por sus columnas (OT1-C) | 22/11; el backend completo, el front después de la prueba | [02](02-lo-programado-del-diseno.md) §1 |
| La pieza única para elegir, con su extensión del 22/11 | 22/11, en el `main` del front | [02](02-lo-programado-del-diseno.md) §2 |
| La respuesta como el formulario (VF1-A), la actividad fila por fila (VF2-A) y «nada debajo de otro dato» | 22/11, en programación | [02](02-lo-programado-del-diseno.md) §3 |
| La fecha de un Flow sin el calendario de WhatsApp (O48) | 22/11, para después de la prueba | §3 |

Y cinco arreglos, todos con el test en rojo primero: O31 (front `bbbe9a60`), O38 (`bd6ddd2b`), O29 (`9c24b597`, contado en
el 17), O43 (`b250242e`) y O44 (`0b79ad30`).

## 3. Las observaciones

Siguen la numeración del 16. «Para después» es la lista de después de la prueba: nada de eso está decidido ni programado.

### Resueltas

- **O31 (bloqueaba el 82).** «Enviar formulario» con un formulario que elige primero por un campo (LD1-A) y 331 opciones en
  total decía «The catalog options could not be loaded.»: el front leía la cantidad con un máximo de 200. El tope de 200 de
  Meta vale por valor del campo y para la lista de valores (LD2-A), no para el total. Se quitó el máximo (front `bbbe9a60`).
- **O36.** Con las dos ofertas automáticas del día ya usadas, el formulario que el asistente quería mandar terminaba
  bloqueado sin respuesta ni derivación: la persona no recibía nada, aunque hubiera tocado un botón que le ofreció el propio
  asistente. Ahora deriva a una persona con el motivo «Llegó al tope de ofrecimientos del día», y el tope se configura
  (TO1-A). La revisión adversarial encontró que elegir una opción de una pregunta ya ofrecida no tiene que gastar otra oferta
  (`6bbf37d6`).
- **O38.** Al confirmar el formulario de un comprobante, el documento que el Flow ya adjuntaba no vuelve en la respuesta de
  Meta, y la regla aplicaba el mínimo de la pregunta (uno) antes de sumar la copia propia: la respuesta quedaba rechazada y
  a la persona no le llegaba nada. Ahora esa pregunta pide mínimo cero cuando la copia está (`bd6ddd2b`).
- **O39.** Con el tope lleno, una nota de voz no se transcribía y quedaba «No disponible» sin respuesta, aunque pidiera algo
  que contesta un caso. El corte venía de cuando el audio sólo servía para ofrecer formularios. Ahora todo audio se
  transcribe y sigue como un texto.
- **O43.** El catálogo que crea un estándar de la biblioteca numeraba sus opciones desde 0; todo catálogo numera desde 1 y el
  front rechaza la posición 0, así que «Bancos y billeteras» no mostraba ninguna opción (`b250242e`). Una instalación que ya
  agregó el estándar necesita renumerar sus opciones: acá se hizo a mano, porque la guía pide no renumerar en una migración.
- **O44.** De un comprobante se leyó como «Cuenta de origen» el número interno de la cuenta de débito, que no es CBU, CVU ni
  alias, y quedó marcado como inválido: el Flow abría con el error y sin salida clara. Decisión del usuario (opción A): lo que
  no tiene la forma de su validación (22 dígitos o un alias para una cuenta, once dígitos para una CUIT) queda sin leer, y lo
  que la tiene y no da sigue marcado. Vale para documentos y para filas de planillas (`0b79ad30`).

### Para después

| | Qué se vio | Área |
| --- | --- | --- |
| O32 | Una sugerencia armada antes de tomar la conversación, mandada después, la rechaza el servidor con un 409 claro, pero la pantalla la presenta como un resultado incierto («No se pudo confirmar si el pedido quedó guardado») (menor). | WhatsApp |
| O33 | En desarrollo, abrir el enlace de una importación que propuso la IA dice «Hiciste demasiadas solicitudes»: StrictMode monta dos veces y choca con el cupo de exportar. En producción no pasa (menor). | IA y catálogos |
| O34 | Cuando el código y la opción salen de la misma columna, el resumen lo dice dos veces («el código es X y cada opción X») (texto). | IA |
| O37 y O46 | «Este comprobante ya se cargó» aunque el primero sólo se propuso y nadie lo confirmó. Convendría «Ya me lo mandaste el {fecha}: completá el formulario que te mandé.» (texto). | Documentos |
| O40 | Con la Api caída, recargar deja «Cargando…» más de 30 s, sin aviso ni «Reintentar» (menor). | Toda la app |
| O41 | Guardar una regla que nombra una opción borrada se rechaza bien, pero el aviso no dice cuál ni marca la condición, aunque la Api lo dice en el campo (menor). | Formularios |
| O42 | En el Flow, «1.500,5» (con separador de miles) no se puede mandar: Meta lo frena con su propio mensaje en inglés. Es coherente con la ayuda («sin separador de miles»); puede que sólo el paso de la prueba pidiera de más. | WhatsApp |

### Para decidir

- **O35.** Una persona agotó su cupo diario de IA (diez interpretaciones por persona, canal y día) en una conversación corta,
  y el asistente derivó con «El asistente no pudo responder», sin que la persona ni el operador vieran que fue el cupo. RC5
  pasó el corte al día del negocio ([01](01-reglas-en-configuracion.md) §3); sigue abierto mostrar y configurar en pantalla
  los cupos por persona, con el techo de la instalación.
- **Un audio que el proveedor devuelve vacío.** El chat dice «No pudimos pasar este audio a texto», a la persona no le llega
  nada y no se deriva. Falta decidir si tiene que contestar algo.
- **Un formulario no se combina con otra acción** (154). Ante «quiero cargar el registro de lluvia y además decime a qué hora
  abren», el motor propone sólo el formulario. Así está documentado; falta que el usuario lo acepte.
- **O45.** «Banco de origen» quedó sin leer en dos comprobantes que muestran el banco en el logo y el formato (menor).
- **O47.** Un comprobante de débito muestra el importe con signo menos y «Monto» llegó negativo. En un comprobante de
  transferencia el monto es siempre positivo, y un negativo resta en el tablero del formulario (importante).
- **O48.** La fecha de un Flow la dibuja el selector nativo de WhatsApp («22 Nov. 2026» en el formulario, «Nov 22 2026» en
  el resumen), cuyo formato Meta no deja elegir, y viola la regla de fechas sólo con números. **Decidido el 22/11:** después
  de la prueba, la fecha de un Flow pasa a texto con números (dd/mm/aaaa; mm/dd/aaaa en inglés), sin el calendario, y la
  regla de fechas suma esa excepción de WhatsApp para que no se repita.
- **O49 (importante).** Una respuesta de Flow, con el documento adjunto, nunca llegó a la app: su sesión de Flow quedó
  pendiente, a la persona le figura «Respuesta enviada» y nadie se entera. El webhook andaba (un mensaje posterior entró). Para
  investigar: qué pasó con esa respuesta, y guardar del webhook algo que dure más que el panel de desarrollo, para poder
  diagnosticar una pérdida así.
- **O28** sigue como la dejó el 16.

## 4. Lo que enseñó

- **Un tope que corta tiene que contestar o derivar, y decir por qué** (O35, O36, O39). Un formulario bloqueado sin aviso, un
  audio sin transcribir o un cupo agotado en silencio dejan a la persona sin respuesta y al operador sin pista. Y si el tope es
  una regla de negocio, se configura: de ahí salió todo el [01](01-reglas-en-configuracion.md).
- **Un dato sin la forma esperada es «sin leer», no «inválido»** (O44). La validación fija marca lo que tiene la forma y no da;
  lo que ni siquiera tiene la forma lo dejó la lectura y no sirve como error para la persona. Se prueba con comprobantes
  reales, que traen formatos que los de prueba no (O45, O47).
- **Lo que dibuja WhatsApp no lo controla la app** (O48, O42). El calendario y el formato de un número los fija Meta: si violan
  una regla de la app, se reemplazan por un texto con su propia validación, y la excepción se escribe en la regla.
- **Una respuesta perdida no la ve nadie** (O49). Para la app es igual a una que nadie mandó: la persona ve «Respuesta
  enviada» y la sesión de Flow sigue pendiente. Lo mínimo es guardar del webhook lo necesario para diagnosticar una pérdida;
  cómo detectarla está por decidir.
- **Lo que se crea solo respeta las invariantes de lo que se crea a mano** (O43): un catálogo de la biblioteca numera como
  cualquier otro.
- **El campo nuevo de un contrato y su lector entran juntos** (22/11). El backend de VF1-A sumó la forma de la versión al
  detalle de una respuesta, y la app se reconstruyó antes de que su lector llegara al front: el esquema estricto del front no
  conocía el campo y el detalle de las respuestas no abrió hasta el arreglo del front (`29405a01`). El esquema estricto es a
  propósito (O25, en el 16): lo que se coordina es el orden.

## 5. Lo que falta

- **A mano:** el micrófono, un celular real (el 76 y la coma de O29), el lector de pantalla, algunas recargas y teclas, y la
  primera carga de los esqueletos.
- **Con el usuario:** el pedido del certificado de ARCA con su CUIT (169) y el correo con un secreto mal puesto (180).
- **El cierre:** la reconstrucción final de la app, con lo que falta de «Reglas a Configuración» y la pasada final de la pieza
  única ([05](05-estado-al-23-11.md) §2); una corrida completa en verde con la máquina quieta; la auditoría documental; y el
  traspaso 19, con lo que se decida de O28, O35, O45, O47 y O49 y lo que se programe de O48.
- **Después de la prueba,** en su lista: las homologaciones de ARCA y la corrida real del robot, el primer despliegue en Azure,
  quitar módulos en una copia y los cierres de Casos y Documentos.

## 6. Para el multitenant

- **Salvo lo que cuentan el 01 y el 02, y O48, nada de esto es una decisión.** Anotá las observaciones como riesgos a mirar en
  tus specs, sin armar etapas.
- **Leído en tu `main`** (`c339b58d`), lo que toca tus specs:
  - **O38 responde tu mayor riesgo técnico de la E25.** Tu `docs/plans/formularios/25-documentos.md` manda probar con Meta si
    un Flow acepta un archivo precargado. Acá se precarga, y Meta no lo devuelve en la respuesta: la copia guardada se cuenta
    antes de exigir el mínimo de la pregunta (en tu E18, una obligatoria pide mínimo uno).
  - **O44:** tu E25 dice que lo que no se leyó queda vacío, lo dudoso marcado y que un dato que no pasa una validación no se
    propone como válido, pero no decide qué pasa con un valor que ni siquiera tiene la forma. Acá queda sin leer. El alias, el
    signo del monto (O47) y el banco de origen (O45) no aparecen en tu E25: anotalos como riesgos.
  - **O48:** tu Flow pide la fecha con `CalendarPicker` (`14-parte-archivos-tests-y-meta.md`) y tu ficha de formatos todavía
    admite la fecha larga, con el mes en letras. Es la pregunta 83 del [06](06-preguntas.md).
  - **O49:** tu webhook no guarda el cuerpo original (`docs/features/whatsapp.md`) y «Venció sin respuesta» se calcula al leer:
    una respuesta perdida se ve igual que una que nadie mandó. Anotalo como riesgo; acá todavía se investiga.
  - **O35:** tu asistente de la web ya cumple lo que faltó acá (el cupo por persona, lo que queda y un aviso claro al llegar,
    ADR 0048 y 0049). En WhatsApp (E23) y en la lectura de documentos (E25) no dice qué ve la persona ni el operador cuando se
    agota. Va con la pregunta 80 del [06](06-preguntas.md), la del día de los cupos.
  - **O31:** tus listas dependientes van sólo en la web por ahora (tu decisión 94). Cuando lleguen al Flow, el tope de 200 es
    por valor y para la lista de valores, nunca para el total del catálogo.
  - **O43 coincide:** tu biblioteca crea los catálogos con posiciones de 1 a n (`22-parte-datos-y-contrato.md`).
- **Tu lista de errores ya pagados** de la E18 (`18-fotos-y-documentos.md`) es buen lugar para O38 y O44.
- **Tu propia prueba integral** puede sumar lo que enseñó esta: probar los topes hasta que corten y leer documentos reales.
