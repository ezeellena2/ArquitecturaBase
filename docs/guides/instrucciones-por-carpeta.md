# Instrucciones por carpeta para agentes

## Convención de la plantilla

Usamos `AGENTS.md`, en plural, como fuente de reglas compartida. Cada carpeta con instrucciones tiene un `CLAUDE.md` que importa el archivo vecino con `@AGENTS.md`. La raíz de Claude puede sumar reglas propias de la herramienta; no copia las reglas generales.

Codex descubre instrucciones desde la raíz hasta su directorio de trabajo y las combina en ese orden, con un límite predeterminado de 32 KiB. No presupongas que un chat iniciado en la raíz ya leyó todos los descendientes: antes de editar, buscá y leé las guías que contienen los archivos afectados. El nombre singular `AGENT.md` no es el nombre predeterminado. [Documentación oficial de descubrimiento de instrucciones](https://developers.openai.com/codex/guides/agents-md).

El [mapa del backend](../architecture/mapa-de-instrucciones.md) y el [mapa del front](../../../ArquitecturaBaseFront/docs/mapa-de-instrucciones.md) enumeran los ámbitos. Las fuentes completas siguen siendo [backend.md](../architecture/backend.md), los documentos de [features](../features/) y la [arquitectura del front](../../../ArquitecturaBaseFront/docs/architecture.md).

## Cuándo crear un ámbito

Creá una guía cuando haya una responsabilidad diferenciada, una frontera de dependencias, un contrato compartido, un riesgo de seguridad o concurrencia, o una forma de prueba específica. Las capas, servicios de un área, adaptadores, persistencia y arneses de tests cumplen este criterio.

Una carpeta pequeña puede quedar cubierta por el padre si sus reglas son las mismas. No crear archivos vacíos ni repetir la raíz en cada nivel. Tampoco agregarlos a outputs, dependencias, laboratorios temporales o cada carpeta de una migración.

## Qué explica cada archivo

Una guía permite responder estas preguntas antes de cambiar código:

1. ¿Para qué sirve la carpeta y qué contienen sus subcarpetas principales?
2. ¿Qué corresponde programar acá y qué va a otra capa o área?
3. ¿Qué invariantes y trampas hay que conservar?
4. ¿Qué código real sirve como referencia y qué test observa el comportamiento?
5. ¿Dónde está la regla canónica que completa el resumen?

Apuntá a unos pocos párrafos y reglas concretas. Las guías de áreas existentes conservan su lectura funcional obligatoria; el detalle completo se enlaza. No listar cada método ni incorporar pendientes personales, status de un chat o secretos.

## Plantilla

```markdown
# <Carpeta>: <responsabilidad>

<Para qué sirve y qué contiene. Diferencia con carpetas vecinas.>

## Al modificar

- <Frontera de dependencias o ubicación.>
- <Invariante funcional o técnica que se conserva.>
- <Forma correcta de extensión y trampa comprobada.>

## Referencias

- <Enlace relativo a la arquitectura o regla del área.>
- <Enlace relativo a un ejemplo real y a sus tests.>
```

El `CLAUDE.md` de ese ámbito contiene:

```markdown
@AGENTS.md
```

Las reglas generales viven en la raíz; las de una capa, en su proyecto; las funcionales, cerca de la implementación del área. Las interfaces y adaptadores agregan sus propios límites. Un módulo opcional contiene sus guías y no obliga al núcleo a conocer su código.

## Cómo anclarlo a un desarrollo

1. Leé raíz, guías de ancestros y guías locales de todos los archivos afectados. Para una pantalla, leé también las premisas y el contrato visual del front.
2. Implementá en las ubicaciones fijadas por la arquitectura. Una guía local orienta dentro de esa estructura; no autoriza una dependencia prohibida.
3. Al agregar un área, adaptador o regla nueva, creá o actualizá su `AGENTS.md`, mantené la importación de Claude y agregá el ámbito al mapa de su repo.
4. Si cambia una decisión estructural, agregá ADR y actualizá la arquitectura. Si cambia una regla funcional, actualizá `features`; si cambia una receta, `guides`. Las instrucciones solo resumen y enlazan.
5. Verificá que los archivos citados existan, los enlaces resuelvan, el par no forme un ciclo y las instrucciones de ancestros no se contradigan. Revisá el tamaño combinado de las guías hasta el ámbito más profundo.
6. Corré los checks propios del desarrollo y los de cierre del repo. Un cambio solo documental no necesita pruebas nuevas que repliquen su texto.

El mapa registra carpetas con guía propia y las cubiertas por el padre. Se mantiene con la funcionalidad: una carpeta renombrada mueve también sus instrucciones y corrige los enlaces; un módulo eliminado borra sus pares y sus entradas del mapa.

## Relevamiento aplicado el 2026-09-30

El backend pasó de 18 a 67 ámbitos: se enriquecieron las 17 guías locales existentes y se agregaron 49 para capas, puertos, persistencia, Redis, borde HTTP, tests y operación. El front pasó de una raíz a 22 ámbitos, con 21 guías locales.

Las reglas generales que estaban en el `CLAUDE.md` del front se conservaron en `docs/architecture.md`. Su `AGENTS.md` exige leer esa fuente y conserva las premisas y el diseño vigentes. Así ambos agentes parten de las mismas reglas y las cadenas de instrucciones quedan breves.
