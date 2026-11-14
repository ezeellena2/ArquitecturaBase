# 07 · En curso, pendiente y lo que hay que preguntarle al usuario (al 2026-11-14)

Para que el multitenant sepa cómo viene cada cosa en ArquitecturaBase. Lo que está en curso tendrá su traspaso cuando
cierre: no armes etapas con eso.

## 1. Dónde está ArquitecturaBase

- **La tanda del 11/11.** El usuario pidió que cada chat terminara todo lo que se le había pedido, back y front, para
  hacer después **una sola prueba integral** de punta a punta. Terminaron su código ARCA (etapas 1 a 6), el motor, la
  biblioteca, las carpetas, «sólo en la app», el Excel con sus diálogos, «Ordenar por…», la ubicación (también por
  WhatsApp y por el asistente), la etapa 2 de la IA, los casos del asistente y Roles.
- **Siguen programando:** documentos, que tiene pendiente la calificación con el proveedor real (02, §8), y los arreglos
  de WhatsApp, en la etapa 6 (H13, H16 y H19) y la 7 (H25) (03, §2).
- **Después, la sesión que coordina la tanda:** la exportación conjunta de los contratos del front (corre la suite completa
  del backend con Docker), los consumidores del front que esperan esas capturas, la verificación completa del front (lint,
  build, vitest y las pruebas visuales, con la PC quieta) y la lista final de la prueba integral.
- **En la prueba integral, con el usuario:** las homologaciones de ARCA (etapa 1, factura C y factura A y B), la corrida
  real del robot, la prueba con Meta del pedido de ubicación y de la ventana de 24 horas, la vista oficial y el celular con
  el Flow `r15` (lo leído de un documento y la coma decimal), los 13 pasos de los casos del asistente y los pasos nuevos de
  cada desarrollo.
- **Después de la prueba:** el cierre de cada plan con la auditoría documental, y un traspaso con lo que haya cambiado.
- **Revisiones de arquitectura:** desde el 14/11 corren las de ARCA y del núcleo con la guía de los 30 criterios; IA y
  Formularios van después.

## 2. Cada desarrollo

| Desarrollo | Estado | Dónde mirar |
| --- | --- | --- |
| Motor para documentos (CA1-A, CO1-A, RE1-A) | Programado; falta la prueba integral y el cierre | 01, §1; `docs/plans/2026-11-09-arca-etapa-2-motor.md` |
| Biblioteca de estándares (BI1-A) y catálogos de un módulo | Programados; falta la prueba integral | 01, §2 y §3; ADR 0029 |
| Carpetas (CP1-A a CP5-A) | Programadas; falta la prueba integral | 01, §4; `docs/plans/2026-11-03-carpetas-de-formularios-y-catalogos.md` |
| Formularios sólo en la app (SA1-A a SA5) | Programado; faltan la exportación conjunta, la prueba a mano y el cierre | 01, §5; `docs/plans/2026-11-09-formularios-solo-en-la-app.md` |
| El Excel en una hoja y los diálogos (XL1-A, EX1-A, EX2-A, DL) | Programados; faltan las capturas `.xlsx` y la prueba a mano | 01, §6; 05, §3 |
| La pregunta «Ubicación» (UB1-A a UB4-A, W1-A) | Programada; falta la prueba real con Meta | 01, §7 |
| Elegir primero por un campo (LD1-A a LD3-A) | Programado entero; falta la prueba en el celular | 01, §8 |
| «Ordenar por…» (OR1-A) | Programado; falta la prueba a mano | 01, §9 |
| Documentos por WhatsApp (DOC1 a DOC7-A, UC1-A, VC1-A) | Tareas 1 a 30 en `main`; falta la calificación real (tarea 17) y el cierre | 02; `docs/plans/2026-10-22-documentos-por-whatsapp.md` |
| La IA arma formularios, etapas 2 y 3 | Programadas y calificadas; faltan los contratos tras la exportación | 02, §7 |
| Casos del asistente (AS1-A a AS5) | Programados; faltan la prueba a mano y el cierre del plan | 03, §3; `docs/plans/2026-10-21-casos-del-asistente.md` |
| Arreglos de la revisión de WhatsApp | Etapas 1 a 5 hechas; la 6 y la 7, en curso | 03, §2; `docs/plans/2026-10-01-bandeja-whatsapp.md` |
| ARCA, etapas 3 a 6 y agregados de la 1 | Programadas; faltan las homologaciones, la corrida real del robot y el cierre | 04 |
| Reglas de diseño (lista de chequeo, UD, MA1-A, AV1-A, IA1-A, AC1-A) y Roles (RO1-A) | Programadas; falta la prueba a mano | 05 |
| Fechas y números para una persona | Programados en back y front | 05, §2 |

## 3. Planeado y sin programar

- **Tickets en Jira desde formularios** (plan del 12/11, sección G de `docs/plans/integraciones/estudio-2026-10-22.md`):
  decidido con el usuario (JT1 a JT7) y revisado, sin programar. El formulario es el ticket (sus preguntas son los campos
  del proyecto), se agrega desde la biblioteca y lo dispara otro formulario, una persona, un error en pantalla o una falla
  del sistema; la ida y vuelta llega a quien reportó. Extiende «Qué sugerir después» con «Se carga sola», «Preguntar
  siempre» también en la app y fotos y documentos en «Datos que pasan». Su ADR decide entre un módulo propio y uno de
  integraciones: **toca tu decisión 76**.
- **Agentes del asistente** (varios agentes con ruteo): la tercera etapa de los casos, sin programar; es tu E24.
- **WhatsApp a más escala:** H23, H24 y H26 antes de pasar de una copia de la Api; H27 a H34 con volumen real; borrar o
  anonimizar a una persona (H14).
- **Propuestas sin decidir:** conectar bases de datos externas en autoservicio (propuesta del 2/11; la recomendación fue no
  construir el conector por ahora), copiar formularios, los tableros que cruzan formularios y la retención de las
  respuestas, que se decide antes de producción.
- **Azure dev,** después de la prueba integral.

## 4. Lo que el chat del multitenant tiene que preguntarle al usuario

Juntadas de las secciones «Para el multitenant». De a una, con las opciones y la recomendada primero. Lo de
ArquitecturaBase es lo más reciente que eligió el usuario: proponelo como recomendado, salvo que tu modelo lo impida, y
decí por qué.

1. El redondeo de los cálculos: half-even, como acá, o `AwayFromZero`, como tu E19 (01, §11).
2. Las operaciones de un cálculo: tres fijas o las cuatro de tu E19 (01, §11).
3. La biblioteca: sólo plantillas de la plataforma o también estándares que aportan los módulos (01, §11).
4. «Sólo en la app»: un ajuste explícito del formulario o tu regla automática de la decisión 98 (01, §11).
5. El Excel: una fila por ítem con un tope de 100.000 filas (01, §11).
6. La pregunta «Ubicación»: en qué etapa entra (01, §11).
7. Documentos: la carga automática de tu 71, si se leen los documentos de clientes sin cuenta y quién crea el borrador
   (02, §9).
8. El asistente: quién califica el modelo que redacta (03, §5).
9. El separador decimal en WhatsApp: coma en español, como `r15`, o punto, como dice tu hoja de ruta (03, §5).
10. Las fechas: la hora después de una coma y nunca el mes en letras, contra tu `formatos.md` (05, §5).
11. Un dato por columna: si vale la excepción del texto secundario en gris (05, §5).
12. ARCA: las siete preguntas del 04, §7.
13. La verificación: la corrida completa una sola vez, al cerrar el desarrollo (06, §7).
