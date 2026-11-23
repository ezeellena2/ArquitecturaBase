# Traspaso 18 · 05 · El estado al 23/11

Reemplaza al [06 del 16](../2026-11-21-traspaso-multitenant/06-estado-al-21-11.md) y a la tabla «Qué está programado y qué
no» del [README del 17](../2026-11-22-traspaso-multitenant/README.md). Está tomado de lo commiteado hasta la mañana del 23/11:
backend `208d1ad0` y front `4e6d9a5c`.

## 1. Cada desarrollo

| Desarrollo | Estado | Archivo |
| --- | --- | --- |
| Reglas a Configuración | Todo en `main`, back y front: TO1-A y RC1-A a RC7-B. La tanda de WhatsApp (RC2-B, RC3-A y su retención) entró el 23/11. Falta la prueba a mano. | [01](01-reglas-en-configuracion.md) |
| Ordenar las tablas (OT1-C) | El backend completo: las diez listas paginadas ordenan por todas sus columnas con la regla común. El front (la flecha, «Ordenar por», las listas enteras) va después de la prueba. | [02](02-lo-programado-del-diseno.md) §1 |
| La pieza única para elegir | En el `main` del front con su extensión del 22/11. Falta la pasada final: las guías viejas, las capturas, la regla del foco y la corrida completa. | [02](02-lo-programado-del-diseno.md) §2 |
| La respuesta como el formulario (VF1-A y VF2-A) | Casi entera en la app: el contrato del detalle, las dos pantallas con la forma de la carga, corregir en el lugar, la actividad fila por fila y el barrido de «nada debajo de otro dato» (VF3). Faltan la exportación de los ejemplos HTTP, la verificación y el cierre. | [02](02-lo-programado-del-diseno.md) §3 |
| Información del negocio (IN1-C, IN2 e IN3) | Decidida el 22/11; el plan vive en el front. Sin programar. | [02](02-lo-programado-del-diseno.md) §4 |
| Formularios en una sola entrada (FU1-B y FU2-A) | Elegida el 23/11; el diseño vive en el front. Sin programar. | [02](02-lo-programado-del-diseno.md) §5 |
| Roles (RO3-D) y carpetas como menús (CM1 a CM6) | En la app desde el 19 y el 20/11. Faltan la corrida completa, la prueba manual de roles y el segundo despliegue de la migración de carpetas. | Traspaso 16 |
| Las acciones del asistente (ADR 0034) y los procesos del campo (ADR 0033) | Sin programar hasta después de la prueba. | Traspaso 16, 04 |
| Tickets en Jira y recorridos con Playwright | Planes escritos, sin programar. | Traspaso 15, 02 |

## 2. Lo que sigue

1. **La reconstrucción final de la app,** por el carril ([04](04-forma-de-trabajo.md) §1), con todo lo que ya está en `main`, y
   después la pasada final de la pieza única.
2. **Los pasos a mano** de la prueba integral y los dos que hace el usuario ([03](03-prueba-integral.md) §5).
3. **Una corrida completa en verde,** con la máquina quieta.
4. **El cierre:** la auditoría documental, que lleva las decisiones a las premisas y retira los planes terminados, y el traspaso
   19, con el cierre de la prueba, el front del orden por columnas y lo que se programe de O48.

## 3. Dónde está cada cosa

- **En GitHub** está el backend con este paquete desde el 2026-11-23, y el front hasta `fbd5538a`, del 22/11: los seis
  commits del front que siguen (`fbd5538a..4e6d9a5c`) están sólo en la PC del usuario.
- **Los planes vivos:** en el backend, `docs/plans/2026-11-22-reglas-a-configuracion.md`,
  `docs/plans/2026-11-22-ordenar-tablas-por-columnas.md` y `docs/plans/2026-11-22-respuesta-como-formulario.md`; en el front,
  `docs/plans/2026-11-19-una-sola-pieza-para-elegir.md` y `docs/plans/2026-11-22-informacion-del-negocio-reordenar.md`.
- **Las reglas implementadas:** `docs/features/` (formularios, administración, identidad, IA, WhatsApp y ARCA) y, en el front,
  `docs/design/visual-baseline.md`.

## 4. Para el multitenant

Tu `main` sigue en `c339b58d` y tu front en `c5172e65`, del 1/11, igual en GitHub. Si incorporaste los traspasos sin subirlos,
mirá este estado contra lo que ya tomaste: lo que acá figura «en programación» o «sin programar» puede cambiar en el 19.
