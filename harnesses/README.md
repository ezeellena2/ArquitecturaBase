# Harnesses: quién los llama y cómo funcionan

**Los iniciás vos desde una terminal.** Un arnés es un programa que prepara una tarea para
un agente, espera su respuesta y verifica el resultado. No se inicia por existir esta carpeta
ni por tener archivos Markdown. Tampoco hace falta que esta conversación siga abierta.

```text
Vos
 └─ PowerShell: run.ps1
     └─ Node: runner.mjs
         ├─ prepara inventario y tarea
         ├─ inicia codex exec o claude -p
         ├─ recibe una propuesta JSON
         ├─ verifica la propuesta
         └─ aplica documentación e informa, según el modo
```

El proceso de Codex o Claude es hijo del runner: el script lo crea y espera a que termine.
Las CLIs necesitan estar instaladas y autenticadas por su procedimiento habitual. El arnés
no incluye claves, no inicia sesión por vos y no instala herramientas. Usar un agente consume
el acceso/cupo que corresponda a su CLI; `-DryRun` y `-Engine none` no llaman a un modelo.

## Primera prueba, sin agente

Desde la raíz del backend, en PowerShell:

```powershell
.\harnesses\documentation\run.ps1 -DryRun
```

Prepara `task.md`, inventario e informe en `.test-artifacts/harnesses/documentation/`.
No actualiza documentación ni ejecuta un agente. Abrí esos archivos para ver exactamente
qué recibiría el motor y qué controles se aplican.

## Elegir quién analiza

```powershell
# Codex analiza; propone cambios, sin aplicarlos.
.\harnesses\documentation\run.ps1 -Engine codex -Mode audit

# Claude analiza y el runner aplica la propuesta validada.
.\harnesses\documentation\run.ps1 -Engine claude -Mode update
```

Podés intercambiar `codex` y `claude` en cualquiera de los modos. El workflow y el contrato
de salida son los mismos; cambian la invocación y la normalización de la respuesta.
El motor no escribe directamente: trabaja en lectura y devuelve cambios propuestos.

## Carpetas y responsabilidad

```text
harnesses/
├─ AGENTS.md y CLAUDE.md       reglas comunes de estas herramientas
├─ README.md                  esta explicación
├─ shared/
│  ├─ core.mjs                controles y aplicación documental
│  └─ engines/
│     ├─ codex.mjs            cómo llamar a Codex
│     └─ claude.mjs           cómo llamar a Claude
└─ documentation/
   ├─ run.ps1                lo que ejecutás vos
   ├─ runner.mjs             quien coordina los pasos
   ├─ workflow.md            qué tiene que hacer el agente
   ├─ config.json            rutas y checks
   ├─ report.schema.json     formato de su respuesta
   ├─ README.md              comandos y límites
   └─ tests/                 pruebas del arnés
```

| Pieza | Qué hace |
| --- | --- |
| [documentation/run.ps1](documentation/run.ps1) | Entrada manual: lee opciones e inicia Node. |
| [documentation/runner.mjs](documentation/runner.mjs) | Coordina inventario, motor, aplicación y validación; guarda resultados y maneja errores. |
| [documentation/workflow.md](documentation/workflow.md) | Tarea común de ambos agentes; enlaza el procedimiento canónico sin copiarlo entero. |
| [documentation/config.json](documentation/config.json) | Rutas de repositorios, timeout y comandos de checks controlados por el runner. |
| [documentation/report.schema.json](documentation/report.schema.json) | Forma de la propuesta del agente. El runner agrega su evidencia de ejecución. |
| [shared/core.mjs](shared/core.mjs) | Inventario Git, enlaces/anclas, instrucciones, formato, rutas y concurrencia. |
| [shared/engines/codex.mjs](shared/engines/codex.mjs) | Construye `codex exec` en lectura y obtiene su respuesta JSON. |
| [shared/engines/claude.mjs](shared/engines/claude.mjs) | Construye `claude -p` con herramientas de lectura y extrae `structured_output`. |
| [documentation/tests](documentation/tests/) | Repositorios temporales y respuestas simuladas para probar el código del arnés sin modelos. |

La coordinación está en JavaScript para compartir inventario, parsers, procesos y tests con
Node ya instalado por el front. PowerShell conserva una entrada breve que podés ejecutar
y leer primero. No hay una copia del procedimiento por motor.

## Qué leer para aprender

Empezá por `run.ps1`: recibe opciones y llama a otro programa. Después, leé la función
`run` de `runner.mjs`: sus mensajes numerados muestran las cuatro etapas. Seguí con el
workflow y el schema, y finalmente con los adaptadores y tests.

Los Markdown contienen instrucciones; JSON contiene parámetros/contratos; JavaScript
ejecuta decisiones concretas. Una instrucción de texto no es una restricción del sistema:
por eso las rutas y los cambios se verifican con código antes de aplicarlos.

## Otros posibles llamadores

Más adelante, el mismo comando puede invocarlo una tarea programada, un pipeline de CI
o un agente al que se lo pidas. En ese caso, ese sistema inicia el script en tu lugar.
Esta versión es manual: **no instala horarios, hooks ni tareas de fondo**.

## Extender

Otro arnés tendría su carpeta con entrada, procedimiento, configuración y tests. Reutilizá
los adaptadores cuando sus permisos/contrato sean compatibles. No crees carpetas vacías
para tareas todavía no implementadas.

Reglas y detalle del primer arnés: [auditor documental](documentation/README.md).

Los protocolos de ejecución se pueden contrastar con las guías de
[Codex sin interacción](https://learn.chatgpt.com/docs/non-interactive-mode) y
[Claude Code programático](https://code.claude.com/docs/en/headless).
Para opciones de la versión instalada, consultá `codex exec --help` y `claude --help`.
