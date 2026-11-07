# 02 · En curso, por decidir y pruebas pendientes (al 2026-11-07)

Este archivo es para que el multitenant sepa cómo viene cada cosa en ArquitecturaBase. Lo que está en curso tendrá su
traspaso cuando cierre: no armes etapas con eso.

## Desarrollos

| Desarrollo | Estado | Dónde mirar |
| --- | --- | --- |
| **Renglones · Etapa 1, «Armar y publicar»** | Programada y verificada con Meta y en el celular, en tres variantes. Falta la respuesta de unos 40 KB, programar RG10-D («Agregar bloque») junto con RG12-A («ítem»), y el cierre: documentación, contratos `rows/*` exportados y la verificación completa. | `docs/plans/2026-11-04-renglones-etapa-1-implementacion.md` |
| **Renglones · Etapa 2, «Cargar renglones»** | Plan con el visto bueno del usuario, sin programar: guardar los ítems, capturarlos desde la app y por WhatsApp, y la tabla para cargarlos. Suma RG13-A (publicar frena con un mínimo mayor que 10) y LD3-A. | `docs/plans/2026-11-07-renglones-etapa-2-implementacion.md` |
| **Renglones · Etapa 3, «Ver los renglones»** | Plan con el visto bueno del usuario. Espera el backend de la Etapa 2. | `docs/plans/2026-11-07-renglones-etapa-3-implementacion.md` |
| **Renglones · Etapa 4, «Las reglas»** | Decidida (RG5-A: «algún ítem con…»), sin plan de implementación. | `docs/plans/2026-11-03-renglones-en-formularios.md` |
| **Elegir primero por un campo** (LD1-A a LD3-A) | Decidido y probado con Meta. Se estaba programando al escribir esto. | `docs/plans/2026-11-07-elegir-primero-por-un-campo.md` |
| **Carpetas para formularios y catálogos** (CP1-A a CP5-A) | Decidido, sin programar: cuatro cortes, con el turno a acordar con renglones. | `docs/plans/2026-11-03-carpetas-de-formularios-y-catalogos.md` |
| **La IA arma formularios, etapas 2 y 3** | Sin programar. Falta decidir qué hace WhatsApp ante un pedido ambiguo. La corrida con el proveedor real de la etapa 1 espera que el usuario cargue una conexión de IA. | traspaso 8 y `docs/plans/2026-11-04-ia-arma-formularios-y-catalogos.md` |
| **ARCA** | La etapa 1 está programada (traspaso 6) y le falta la homologación real con la clave fiscal del usuario. Las etapas 2 a 6 siguen en la hoja de ruta, sin diseño en detalle. | `docs/plans/2026-10-31-arca-formularios-estandar.md` |
| **Documentos por WhatsApp y casos del asistente** | Decididos y sin empezar. | traspaso 2, anexo F |
| **Rendimiento** | Lo aplicado está en el 01. Esperan que el usuario elija: las dependencias sin usar del front, el plazo del correo y el tope total de la vista oficial, y los re-renders del chat. Para producción: trazas, región y red privada, y medir a los usuarios reales. | 01, §4 |
| **Sin decidir** | Conectar bases de datos externas en autoservicio (propuesta del 2026-11-02), copiar formularios y la retención de las respuestas. | `docs/plans/README.md` |

## Lo que tiene que decidir el usuario en ArquitecturaBase

- Rendimiento: cuáles aplicar de las tres que esperan.
- IA: qué hace WhatsApp ante un pedido ambiguo.
- Roles: qué hacen Re Pág, Av Pág, Espacio y las flechas cuando el foco está fuera del contenido.
- Azure: cuándo salir a la nube. Sin eso no hay ambiente dev ni Container App donde cargar los probes.
- La plantilla sin el módulo de WhatsApp: «Desvincular» en el perfil da 404. Hay que decidir si la ruta pasa al núcleo o si
  se esconde el botón.

## Pruebas manuales pendientes en ArquitecturaBase

Sirven al multitenant para saber qué no está probado a mano todavía.

- **Primero, porque destraban algo:**
  - renglones: la respuesta de unos 40 KB;
  - la carga en un celular real (cámara, red lenta, sin conexión), con la guía del celular;
  - cargar la conexión de IA, que destraba las pruebas de IA;
  - Mi perfil con el chat de WhatsApp vinculado, que destraba reintentar el aviso a una persona y la última tanda del caso
    de prueba completo.
- **Pantallas ya programadas, sin Meta:**
  - títulos de sección;
  - lo propio de quien sólo completa;
  - corregir y anular;
  - listas dependientes;
  - el armado de la carga;
  - zonas plegadas del editor;
  - publicar por zonas;
  - el idioma sólo cuando es el otro;
  - el estado una vez (EP1-A);
  - el mensaje con datos y las reglas en pasos;
  - filtros, detalle y comparaciones;
  - importar y exportar opciones;
  - la opción del catálogo en un modal;
  - lo elegido relleno y las esperas;
  - el menú sin saltos y los esqueletos de carga.
- **Con WhatsApp e IA:**
  - la IA en Inicio y por WhatsApp;
  - el acceso por WhatsApp;
  - el piloto con Meta de octubre, con algunos textos ya cambiados.
- **Más adelante:**
  - la homologación de ARCA;
  - el ingreso con Google real;
  - las salidas, el responsive y el teclado de Roles;
  - el arranque con una base nueva;
  - el primer despliegue a Azure con sus probes;
  - la restauración aislada y una segunda instalación.
