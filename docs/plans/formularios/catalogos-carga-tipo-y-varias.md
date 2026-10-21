# Catálogos: carga de una vez, tipo de valor y elegir varias

**Estado:** decidida y con tableros elegidos el 2026-10-21 (CA1-A y CA2–CA6), sin empezar. Es la próxima etapa: arranca después del cierre de [Menú y detalle](../../features/formularios.md#gestión-de-formularios-y-detalle-completo--2026-10-21). Lo que viene después de esta etapa está en la [hoja de ruta de catálogos](../2026-10-21-catalogos-con-origenes-externos.md).

## Para qué

Hoy un catálogo «Escritas a mano» se carga de a una opción, todas son texto y una pregunta de catálogo deja elegir una sola. El primer cliente, OCHA S.A., tiene listas de cientos o miles de filas en Macrosistemas. Esta etapa permite:
- cargarlas desde Excel o pegándolas;
- declarar el tipo de valor de las opciones;
- que una pregunta deje elegir varias.

**La carga de a una se conserva tal cual:** para un catálogo de 3 o 4 opciones sigue siendo lo más cómodo.

## Qué se construye

Las partes van en este orden. Cada una con su code review.

### 0 · Arreglos previos, sin cambio visible

- **La unicidad del código pasa a diferida**, como ya lo es la de la posición. Hoy intercambiar dos códigos en un mismo guardado termina en un error 500.
- **El error de código repetido dice en qué fila está** (`items[i].code`).
- **El tope de opciones por catálogo pasa de 500 a 10.000.** Es fijo en el código y no se configura. Se revisan las lecturas que hoy leen hasta 500 y cortarían sin avisar (por ejemplo `FormConditionChoicePolicy`), con una prueba de rendimiento con 10.000.
- **El prellenado acepta un conjunto de perfiles de compilación,** no uno solo. Si no, subir a r9 en la parte C lo apaga en los formularios r8.
- **Las respuestas de WhatsApp en forma de lista se clasifican por su mapeo:** objetos son archivos y textos son opciones. Hoy toda lista se toma como archivos.
- **Front:**
  - un solo editor de condiciones para reglas y sugerencias, que hoy está duplicado;
  - una forma única de leer las opciones elegidas de una celda;
  - mínimo y máximo reutilizables, sacados de las preguntas de archivo;
  - en el catálogo, la tabla separada del editor de la fila.

### B · Tipo de valor (CA3)

- **Al crear un catálogo «Escritas a mano»** se elige el tipo: texto (por defecto), entero, decimal o fecha. No se cambia después. Los de Filtros siempre son texto.
- **En un catálogo tipado**, cada opción suma un **Valor**, validado según el tipo, en su propia columna de la tabla editable. El **código** sigue siendo la identidad de la empresa y el **texto**, lo que se ve en WhatsApp.
- **La respuesta sella el valor y su tipo junto con el texto,** para que editar el catálogo no cambie lo histórico. Los campos nuevos de las copias selladas llevan `JsonIgnore(WhenWritingNull)`, así no cambian las huellas de lo ya publicado.
- **Excel:** la pregunta suma la columna «<pregunta> · valor», como número o fecha. **PDF:** muestra «texto · valor», por ejemplo «GBA · 1.500,00».
- **No cambian:** WhatsApp, los filtros de la planilla, las reglas ni el asistente, porque siguen trabajando por opción.
- **En el editor de formularios,** al elegir el catálogo se ve su tipo.

### A · Carga de una vez (CA1-A y CA2)

**Todo a la vista en la sección Opciones:** Agregar opción (como hoy), Pegar desde Excel, Importar y Exportar. El menú «⋯» tiene bajar la plantilla, ordenar A–Z y generar los códigos que faltan.

**La tabla:**
- se edita en el lugar en pantallas anchas, con el lápiz como hoy; en el celular sigue el editor por opción;
- búsqueda y paginado, porque puede haber miles de opciones;
- casilla por fila y la barra «N seleccionadas de esta página · Dar de baja · Reactivar». Las acciones masivas son **de la página**; los cambios grandes se hacen exportando e importando.

**El desplegable de WhatsApp,** fijo al costado, se actualiza mientras se carga y avisa que una pregunta muestra hasta 200.

**Importar y pegar** (CA2), el mismo recorrido para los dos:
1. archivo (con la plantilla) o pegar;
2. revisar: errores por fila y columna, y altas, cambios y bajas;
3. aplicar.

**Reglas de la carga:**
- **Pegar es importar:** el front interpreta lo pegado y lo manda por la misma vista previa del servidor. Las miles de filas nunca entran al borrador en memoria.
- **El archivo:**
  - XLSX o CSV, hasta 10.000 filas;
  - se lee en el servidor, con topes de tamaño, filas y columnas, sin ejecutar fórmulas ni macros;
  - la vista previa no guarda el archivo.
- **Solo sobre un catálogo guardado y sin cambios pendientes,** con la revisión esperada. En un catálogo nuevo, o con cambios sin guardar, pegar e importar aparecen apagados con su motivo: «Guardá o descartá los cambios para pegar o importar». Un catálogo nuevo se puede guardar vacío.
- **El archivo reemplaza lo que hay:** lo que no viene se inactiva, nunca se borra, y lo que vuelve se reactiva. Pegar **agrega**.
- **Protecciones:**
  - dar de baja una parte grande pide marcar «Sí, dar de baja las N»;
  - una planilla ya aplicada avisa «Esta planilla ya se aplicó el …»;
  - el 409 ofrece «Revisar de nuevo».
- **Validación:**
  - un texto de más de 30 caracteres o un código repetido no dejan aplicar, y dicen la fila;
  - un texto repetido solo avisa.
- **Ordenar A–Z** es una marca del guardado, no una serie de movimientos.
- **Permisos:**
  - exportar y bajar la plantilla piden «Ver catálogos»;
  - lo demás pide «Administrar catálogos».
- **«Usado en N formularios»** va en la cabecera, solo con el número.
- **Back:**
  - una vista previa de importación y una exportación, que también sirve de plantilla;
  - aplicar usa el guardado que ya existe, con revisión;
  - un puerto lector de planillas en Application, implementado en Infrastructure;
  - para escribir, se reutiliza el escritor de Excel.

### C · Elegir varias (CA4 y CA5)

- **Un tipo de pregunta nuevo,** «elegir varias», tanto con opciones propias como de un catálogo. Se ve como «Permitir elegir varias», con mínimo y máximo:
  - el mínimo es 1 si es obligatoria;
  - el máximo, hasta 20.
- **No es una marca sobre la elección de hoy:** así, lo que hoy asume una sola opción falla cerrado en lugar de leer mal. Activarlo en una pregunta ya guardada crea una pregunta nueva, con el aviso «Crear la pregunta nueva», como cambiar de catálogo.
- **WhatsApp:**
  - usa el componente de casillas (`CheckboxGroup`), que admite **hasta 20 opciones**;
  - la comprobación de publicación frena un catálogo más grande en una pregunta de varias y explica qué hacer;
  - hace falta el perfil de compilación **r9**.
- **Cómo se guarda:**
  - varias filas por pregunta en `BusinessFormRecordValues`, con su posición;
  - la clave única pasa a respuesta, pregunta y posición;
  - las referencias al catálogo, a una por opción elegida.
- **Lo que muestra:**
  - **Planilla:** «Frío, Ruido» en la celda y el filtro «alguna de» con su chip. La columna no se ordena.
  - **Detalle y chat:** la lista de opciones elegidas.
  - **Excel:** «Frío; Ruido». **PDF:** la lista.
- **Reglas:** la condición sobre una pregunta de varias es «eligió alguna de».
- **En esta etapa,** la pregunta de varias no tiene prellenado del asistente ni mapeo de sugerencias: quedan excluidos solos por su tipo.
- **Va en cuatro cortes:**
  1. base y captura;
  2. lecturas (planilla, detalle, Excel, PDF y reglas);
  3. WhatsApp r9, codec y webhook;
  4. front.

## Decisiones tomadas

No se vuelven a preguntar.

- La carga de a una se conserva; pegar, importar y la tabla editable se suman.
- Tableros: **CA1-A** (todo a la vista) y CA2–CA6, en la versión 102 del lienzo ([diseño del front][tableros]). CA1-B queda como alternativa no elegida.
- Hasta **10.000 opciones por catálogo**, fijo. Lo que WhatsApp muestra por pregunta es otra cosa y lo fija Meta: 200 en el desplegable y 20 en el de varias. Si una persona recibiría más, el envío se bloquea con un aviso, nunca se corta la lista.
- Lo fijado al dibujar y confirmado al elegir: el archivo, los apagados con motivo, la validación, las protecciones, los permisos, la columna de valor en el Excel y el mínimo y máximo de elegir varias, tal como figuran arriba.
- Nada fijo en el código: los tipos son un conjunto cerrado; los catálogos y sus opciones son configuración.

## Fuera de esta etapa

Están en la [hoja de ruta](../2026-10-21-catalogos-con-origenes-externos.md):
- campos extra del catálogo y **filtrar por persona** («Proveedores de mi zona», que el prototipo muestra pero no se eligió);
- búsqueda en vivo con Meta para más de 200 opciones;
- el asistente que arma catálogos y mapea un Excel;
- orígenes externos y el conector a Macrosistemas;
- al elegir una opción, completar otras preguntas con sus datos.

## Cómo se cierra

- **Checks:** por corte y al cerrar, según [validar un cambio](../../guides/validar-un-cambio.md), con TDD donde hay lógica.
- **Integración con Docker:**
  - **arreglos previos:** intercambio de códigos en un guardado; 10.000 opciones;
  - **parte B:**
    - migración sobre datos existentes;
    - error en `items[i].value`;
    - el valor sellado sobrevive a editar el catálogo;
    - Excel tipado;
    - una sesión sellada antes del cambio conserva su huella;
  - **parte A:**
    - vista previa de XLSX y CSV con errores por fila y columna, fórmulas y topes;
    - aplicar dos veces da 409;
    - exportar y volver a importar da cero diferencias;
    - permisos, rutas y OpenAPI;
  - **parte C:**
    - Up y Down con datos;
    - captura con mínimo y máximo, repetidos, opción no ofrecida e ítem dado de baja;
    - replay;
    - «alguna de» en filtros, facetas y reglas;
    - Excel, PDF, detalle y chat;
    - casillas y archivos en el mismo formulario;
    - una sesión r8 abierta que se captura después de publicar en r9;
    - más de 20 opciones bloqueado.
- **Front:** contratos con los ejemplos exportados, visuales y los tableros en el navegador, es/en, a 390 y 320 px. El [prototipo navegable](https://claude.ai/artifact/2noVifPc2MX33UnNvT2jhY) sirve de guía de los recorridos.
- **Piloto real con Meta:**
  - pegar y después importar un catálogo de proveedores de OCHA;
  - un catálogo decimal;
  - mandar un formulario con una pregunta de varias y confirmar en el celular cómo llega la respuesta: el formato del `nfm_reply` y el caso vacío;
  - verlo en la planilla, el Excel y el PDF.

[tableros]: ../../../../ArquitecturaBaseFront/docs/design/catalogos-mejoras.md#los-tableros
