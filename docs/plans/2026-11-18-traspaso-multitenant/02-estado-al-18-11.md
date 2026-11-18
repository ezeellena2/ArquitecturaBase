# Traspaso 15 · 02 · Cómo quedó todo al 18/11

Reemplaza las secciones de estado de los traspasos 4 a 14 («Pendiente acá», «Lo que falta acá», «En curso o sólo
planeado», el 02 del 10, el 07 del 13 y los §1 y §2 del 07 del 14). Las siglas de los traspasos están en el
[01](01-orden-de-lectura.md#abreviaturas).

## 1. Dónde está ArquitecturaBase

- **La tanda que arrancó el 11/11 terminó.** El usuario pidió que cada desarrollo terminara su backend y su front antes de
  una sola prueba integral de punta a punta. El 15/11 congeló la lista: los arreglos de las revisiones del núcleo, de IA y
  de Formularios, y nada más. El 17/11 quedó todo programado.
- **El cierre del 17/11, en verde:**
  - la revisión contra el código de lo commiteado, para ver que ningún commit hubiera deshecho lo de otro: sólo O8 estaba
    deshecho y se repuso (§3);
  - el backend, 11.656 de 11.656 tests con Docker sobre `02c60e91`, con la exportación de los contratos y las 28 cohortes
    del front (`844a5c13`);
  - el front, tipos, lint, build y 6.022 tests sobre `93eb8eb0`;
  - la CLI de Aspire, de 13.5.4 a 13.6.5, y una prueba de humo: 43 migraciones aplicadas en la base local, y la Api, el
    front y el túnel sanos.
- **Lo que sigue:** la prueba integral con el usuario, de 181 pasos en nueve secciones (ingreso y cuenta; usuarios y
  roles; formularios, de los catálogos a la carga; WhatsApp; respuestas, planilla, aprobación, reglas y avisos; IA y Casos;
  documentos; ARCA sin emitir; configuración, salud y toda la app). Lo que encuentre se arregla sólo si bloquea; lo demás va
  a la lista de después. Detrás vienen la auditoría documental, que lleva las decisiones a las premisas y retira los planes
  terminados, y el traspaso 16 con lo que haya cambiado.

## 2. Cada desarrollo

Todo lo de esta tabla está programado y entró en el cierre en verde del 17/11. «Falta» dice lo que queda además de la
prueba integral.

| Desarrollo | Falta | Dónde leer |
| --- | --- | --- |
| Leyendas y carga, catálogos, publicar, adjuntar el PDF y aprobar | — | T4 |
| Tablas, lecturas, lo elegido, filtros, el detalle, reglas en pasos y la carga en dos anchos | — | T5 |
| La carga desde la app, etapas 1 a 5 | — | T3 (ya incorporado) y T7 |
| El idioma, reintentar un aviso, Mi perfil y el menú sin saltos | — | T9 |
| Vista oficial, el estado una vez, el nombre del Flow, el mensaje y rendimiento | — | T10/01 |
| Renglones (RG1-A a RG13-A) | Nada: se probó de punta a punta con Meta el 9/11 y quedó aprobado. Quedan detalles en «verificación y producción» | T12 |
| Elegir primero por un campo (LD1-A a LD3-A) | La prueba en el celular | T10/01 §3 y T13/01 §8 |
| El motor para documentos, la biblioteca, las carpetas, «sólo en la app», el Excel en una hoja y «Ordenar por…» | — | T13/01 |
| La pregunta «Ubicación» (UB1-A a UB4-A, W1-A) | La prueba real con Meta | T13/01 §7 |
| Documentos por WhatsApp (DOC1 a DOC7-A, UC1-A, VC1-A) | — (calificado con el proveedor real el 14/11) | T13/02 y T14/06 §5 |
| La IA que arma formularios, etapas 1 a 3 | — (calificada con el proveedor real) | T8, T11, T13/02 §7 y T14/05 |
| Casos del asistente (AS1-A a AS5) | — (el modelo que redacta, calificado) | T13/03 §3 |
| Reglas de diseño para toda la app y Roles (RO1-A) | — | T13/05 |
| Fechas y números para una persona | — | T13/05 §2 y T14/06 §1 |
| La revisión de WhatsApp y sus 22 arreglos | — | T13/03 §1 y §2, T14/03 |
| ARCA, etapas 1 a 6, y los 34 arreglos de su revisión | Las homologaciones con CUIT reales y la corrida real del robot, después de la prueba | T6, T13/04 y T14/04 |
| Los 31 arreglos del núcleo | — | T14/02 |
| Los 29 arreglos de IA | — | T14/05 |
| Los 41 arreglos de Formularios | — (la marca de la planilla y la prueba de quitar Formularios se terminaron después del 14: §3) | T14/06 y §3 |

## 3. Lo que se hizo después del traspaso 14

Backend `72510c01..5356b282` (18 commits) y front `ecc0f561..cf247979` (9 commits).

### La marca de la planilla (F2, 2026-11-17)

Backend `06ac5482`, front `0b3a75c6`, con el diseño B del lienzo «Acciones por resolver».

- **Qué cuenta.** Una respuesta tiene acciones por resolver cuando alguna quedó «Fallida», o «En duda» esperando a una
  persona. No cuentan el «¿La emito?», que espera a quien cargó, la consulta que la app todavía hace sola, ni lo que espera,
  terminó o no aplica.
- **Para todos los que ven la planilla:** la columna «Reglas», después de «Estado», dice «Por resolver» o «—». Aparece
  cuando alguna fila de la página tiene la marca.
- **Para quien administra reglas y ve todas las respuestas:** la pestaña «Por resolver (N)», después de «Por aprobar», con
  la misma planilla y el filtro puesto. Aparece con la cuenta mayor que cero y nunca es la vista por defecto. El Excel que
  se pide desde ahí exporta lo que se ve, y su diálogo lo dice.
- **Hasta cuándo.** Una acción fallida que no se reintenta sola sigue por resolver hasta que se anula o se corrige la
  respuesta. Al corregirla, la corrección empieza sin acciones por resolver (decisión del usuario del 17/11).
- **Para el multitenant:** cae en tu E15 y tu E21. Es una pestaña más para filtrar la planilla, como las de «Por aprobar»
  (PL1-A), así que va en la misma pregunta que tu decisión 79 (03, tanda 2).
- **La regla completa:** `docs/features/formularios.md`, «Acciones por resolver».

### La prueba de quitar Formularios, repetida (2026-11-17)

Sobre `72510c01`, en una copia (`04df45ef`). Pidió corregir la guía:

- el `Up` de `RemoveBusinessForms` tiene que soltar la clave de la regla activa, que se creó diferida por SQL a mano
  (`DeferActiveRuleSetKey`), y el `Down` tiene que volver a crearla diferida;
- EF genera tres claves foráneas, no cuatro;
- seis tests de WhatsApp y de IA daban por instalado el módulo.

**Para el multitenant:** un módulo opcional se prueba quitándolo de verdad, y lo que se crea por SQL a mano en una migración
EF no lo ve: hay que soltarlo y recrearlo a mano en la migración que quita el módulo.

### Dos arreglos que dejan una regla

- **La ubicación de una carga por planilla completa la primera fila** (`9cbf0ad0`, W1-A y VC1-A). Las cargas de una
  planilla abren sus envíos en la misma unidad de trabajo, con el mismo `CreatedAtUtc`, y el desempate por `Id` no servía:
  un Guid v7 no tiene orden dentro de un mismo milisegundo. Ahora se ordena por la creación del envío, después por la hora
  del pedido de ubicación (que la carga fija en el orden de la planilla) y recién al final por el `Id`. **No repetir:** un
  Guid v7 no ordena lo creado en el mismo milisegundo.
- **La purga de documentos recibidos registra sólo el tipo de la excepción** (`02c60e91`, F18 y O8). Un commit hecho desde
  una copia anterior había devuelto el log a la excepción entera, y lo encontró la revisión contra el código del cierre.
  **No repetir:** con varias sesiones commiteando en el mismo `main`, el cierre revisa contra el código que ningún commit
  haya deshecho lo de otro.

### Pruebas

- Dos escenarios de prueba sembraban datos distintos de los que guarda el editor (`911a89c5`, `c82ec50d`). **No repetir:**
  el dato de prueba se siembra como lo guarda la pantalla real.
- En el front, los tests del editor de catálogos, del de títulos, de la ruta de sugerencias y el primero de respuestas
  tienen 20 s, porque con la PC cargada se cortaban (`b618d58b`, `925ab958`, `1e877620`, `f027d83f`).

### Una propuesta sin decidir: recorridos de punta a punta

`docs/plans/2026-11-17-recorridos-de-punta-a-punta.md` (`9f800771`): 44 recorridos, uno por caso de uso, en un navegador
real contra la API, la base y el ingreso reales, con video y traza de cada uno. Tiene ocho decisiones recomendadas que el
usuario todavía no confirmó, y no se programó. Tomalo como información: no armes nada con eso.

## 4. Para después de la prueba integral

- **Los horizontes que no se pagaron.** Lo de «Varias copias» y «Gran escala» de las cinco revisiones queda anotado en cada
  una. Antes de pasar de una réplica: la lease de la limpieza de archivos (N28), I18 de IA, y H23, H24 y H26 de WhatsApp.
  En tu modelo, donde varias organizaciones comparten copias desde el principio, conviene tenerlos desde ya (03, pregunta 3).
- **ARCA con ARCA real:** homologar la factura A y la B con la CUIT del usuario, la corrida real del robot y medir memoria y
  CPU de una pasada del robot en la imagen publicada (H22 de ARCA).
- **Formularios:** los menores de la segunda revisión adversarial, de una línea cada uno, en «Qué falta al 2026-11-16» del
  plan `2026-11-15-arreglos-de-formularios.md`.
- **Azure dev:** desplegar para ver la compresión, la caché y los probes.
- **Costos que aceptó el usuario:** la invitación sale en el idioma general (N24); la historia administrativa (N13) queda
  sin pantalla hasta dibujarla; la cuota diaria de IA se renueva a las 21 de la Argentina y la pantalla muestra la hora; el
  gasto en IA lo limita la cuenta del proveedor, con el máximo diario escrito en la guía.

## 5. Planeado y sin programar

- **Tickets en Jira desde formularios** (sección G de `docs/plans/integraciones/estudio-2026-10-22.md`, del 12/11):
  decidido con el usuario (JT1 a JT7) y revisado, sin programar. El formulario es el ticket y sus preguntas son los campos
  del proyecto; se agrega desde la biblioteca y lo dispara otro formulario, una persona, un error en pantalla o una falla
  del sistema. Su ADR decide entre un módulo propio y uno de integraciones: **toca tu decisión 76**.
- **Los agentes del asistente** (varios agentes con ruteo): la tercera etapa de los casos, sin programar. Es tu E24.
- **WhatsApp con volumen real:** H27 a H34 de su revisión, y borrar o anonimizar a una persona (H14), anotado en
  `docs/plans/verificacion-y-produccion.md`.
- **Propuestas sin decidir:** conectar bases de datos externas en autoservicio (la recomendación fue no construir el
  conector por ahora), copiar formularios, los tableros que cruzan formularios y la retención de las respuestas, que se
  decide antes de producción.
- **Los recorridos de punta a punta** (§3).
