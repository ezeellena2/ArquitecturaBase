# Traspaso 14 · 07 · En curso y pendiente

Cómo quedó todo al 16 de noviembre a la noche, qué quedó para después y las preguntas que el chat del multitenant tiene que
hacerle al usuario.

## 1. Lo que falta para cerrar

- **Formularios, dos tareas:**
  - la marca «Con acciones por resolver» de la planilla (F2), que primero se dibuja en el tablero y elige el usuario;
  - repetir la prueba de quitar Formularios en una copia, que estaba corriendo.
- **El cierre.** Lo hace otra cuenta de Claude, porque la de ArquitecturaBase se quedó sin cupo semanal:
  - revisar contra el código lo commiteado, sobre todo que ningún commit haya deshecho lo de otro;
  - la exportación de los contratos y las cohortes del front;
  - la corrida completa: `dotnet test` con Docker y los controles del front;
  - la coherencia de las migraciones del 15 y el 16;
  - la actualización de la CLI de Aspire (13.5.4 a 13.6.5);
  - levantar la app y una prueba de humo.
- **La prueba integral**, guiada con el usuario. Lo que encuentre se arregla sólo si bloquea; lo demás va a la lista de después.
- **Después de la prueba:** la auditoría documental, que lleva las decisiones a las fuentes de las premisas y retira los planes
  terminados, y el traspaso de lo que cambie.

## 2. Lo que quedó para después

- **Los horizontes que no se pagaron.** Lo de «Varias copias» y «Gran escala» de las cinco revisiones queda anotado en cada una.
  Algunos puntos se nombraron para antes de pasar de una réplica: la lease de la limpieza de archivos (N28), I18 de IA, y H23,
  H24 y H26 de WhatsApp.
- **ARCA con ARCA real:**
  - homologar la factura A y la B, que pide la CUIT del usuario;
  - la corrida real del robot;
  - medir memoria y CPU de una pasada del robot en la imagen publicada (H22).
- **Formularios:** los menores de la segunda revisión adversarial, de una línea cada uno, en «Qué falta al 2026-11-16» del
  [plan](../2026-11-15-arreglos-de-formularios.md).
- **Costos aceptados por el usuario:**
  - la invitación sale en el idioma general (N24);
  - la historia administrativa (N13) queda sin pantalla hasta dibujarla;
  - la cuota diaria de IA se renueva a las 21 de la Argentina, y la pantalla muestra la hora;
  - el gasto en IA lo limita la cuenta del proveedor, con el máximo diario escrito en la guía.

## 3. Preguntas para el usuario

Cada pregunta tiene su contexto y la opción recomendada al final del archivo de su área. Acá van juntas, en el orden en que
conviene hacerlas: primero las de plataforma, que cambian todo lo demás.

**Plataforma (01)**
1. ¿Se adopta la guía de revisión de arquitectura para los módulos del multitenant, con un bloque más sobre el aislamiento entre
   organizaciones? Recomendada: sí, módulo por módulo, antes de abrirlo a varias organizaciones.
2. ¿La regla de versiones y «nadie da lo que no tiene» van como reglas de plataforma, desde el primer despliegue? Recomendada: sí.
3. ¿Qué parte de «Varias copias» pasa a «Hoy» allá, donde varias organizaciones comparten copias desde el principio?
   Recomendada: las leases y las reservas de los workers (N28, I18, H23, H24 y H26).

**Núcleo (02)**
4. ¿«Nadie da lo que no tiene» se evalúa dentro de cada organización?
5. ¿Los topes de correo son de la plataforma o de cada organización?
6. ¿La retención va con los mismos plazos: 30 días, 12 meses y 14 días?
7. ¿Con Redis caído la app sigue, leyendo los permisos de la base?
8. ¿La historia administrativa queda sin pantalla hasta dibujarla?

**WhatsApp (03)**
9. ¿Una clave de cifrado de la instalación con el propósito atado a la organización, o una clave por organización?
10. ¿Qué pasa con los chats reales guardados en claro al migrar: se convierten o se vacían?
11. ¿Se cifra todo el contenido y quedan en claro el nombre y el número?
12. ¿Cuándo vence el contenido de un chat?

**ARCA (04)**
13. ¿La conexión es por empresa o por organización?
14. ¿Quién vuelve a probar la clave rechazada de la plataforma?
15. ¿Otro sistema en el mismo punto de venta obliga a decidir factura por factura (H3)?
16. ¿Poner a emitir pide `formrules.manage` y `arca.issue` (H6)?
17. ¿El cliente se elige por «Inicial», con un tope de 200 por inicial, o se escribe la CUIT por WhatsApp?

**IA (05)**
18. ¿Cuándo se devuelve el cupo?
19. ¿Cuándo se renueva el cupo?
20. ¿Qué pasa con las lecturas al borrar una conversación?
21. ¿La clave del proveedor es de la instalación o de cada organización?
22. ¿Cuánto espera un apagado a una consulta en curso?

**Formularios (06)**
23. ¿Las horas del PDF van en la zona de quien cargó o en la de la empresa?
24. El aviso a quien cargó, cuando su regla se frena: ¿va por el canal durable, con «No aplica» si la ventana está cerrada, o con
    una plantilla aprobada por Meta?
25. ¿Un tema nuevo de «Formularios que faltan» se revisa antes de ofrecerse?
26. ¿Un documento propuesto sin revisar vence a los 90 días?
27. ¿Quien corrigió o pasó una respuesta queda afuera de su aprobación?

## 4. Lo que todavía no conviene convertir en etapas

Todo lo de este traspaso está programado, pero sin la prueba integral. El multitenant puede ajustar sus specs, sus reglas y sus
tableros, pero conviene que espere al próximo traspaso para armar etapas de lo que la prueba todavía puede cambiar: la marca de
F2, las fallas que aparezcan en la corrida completa y lo que encuentre el usuario al probar.
