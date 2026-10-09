# Panel interno de conversaciones: implementación y validación

Fecha: 2026-10-05. Alcance aprobado: promover el panel refinado con primitivas de assistant-ui,
abrir Inicio con chat nuevo y recuperar conversaciones anteriores por elección explícita.
Instalación independiente por empresa. [Plan y árbol real](../plans/2026-10-04-panel-conversaciones-inicio.md).

## Resultado implementado

- Panel interno plegable de 256 px; menú general y composición de chat elegida conservados.
  Filas de una línea, Hoy/Ayer/Anteriores por zona del perfil, menú More y borrado confirmado.
  En móvil: Dialog compartido inicialmente cerrado, blancos de 44 px, Escape y retorno del foco.
- ThreadList/Item/More son las primitivas de assistant-ui 0.15.23 instalada. La API experimental
  de threadList queda aislada en AssistantRuntimeProvider. El runtime representa el listado;
  los callbacks pasan por las decisiones de AiAssistantPage y el backend conserva la autoridad.
- Una sola instancia del provider al aceptar/cambiar conversación. No remonta el editor para
  cambiar un ID ni crea conversaciones desde el runtime. `/` queda vacío y `conversacion` restaura
  sólo por GET. Borrador, operación activa e incertidumbre conservan los guards existentes.
- Listado privado con 20 filas por defecto, página en URL, idioma/revisión de permisos en la clave,
  sin polling de toda la lista. Aceptación, cierre y borrado confirmado invalidan el listado.
  Borrado confirmado retira título y turnos del caché antes de volver a consultar.
- Título derivado del primer turno cifrado: permiso actual y vigencia antes de descifrar con su
  contexto original. Espacios normalizados, máximo 80 grafemas. Bloqueo/purga/vencimiento usan el
  genérico localizado; corrupción mantiene ContentUnavailable. Modelos con ToString redactado.
- Reader EF: LEFT JOIN Ordinal=1 y pertenencia compuesta sobre el índice único existente.
  Dos comandos SQL por página, sin respuestas, evidencias, tools ni descifrado por consulta individual.
  Sin columna de título, llamada adicional al LLM, ruta, paquete, tabla o migración nuevos.

## Validación

| Comprobación | Resultado |
| --- | --- |
| `dotnet build ArquitecturaBase.slnx` | Correcto; 0 advertencias y 0 errores |
| `dotnet test` completo, Docker real | 2.340 correctas, 0 fallidas, 0 omitidas; cuatro proyectos |
| Front `npm run build` | TypeScript y Vite correctos |
| Front `npm run lint` | oxlint limpio; 48 pruebas de arquitectura correctas |
| Front Vitest completo | 96 archivos; 1.014 pruebas correctas |
| Playwright del front | 197 recorridos verificados: 193 correctos en suite completa y 4 correctos tras corregir selectores de tests |
| Nuevos recorridos del panel | 6 correctos: es/en × 1440/390/320 |
| `git diff --check` de ambos repositorios | Correcto |

Los cuatro casos móviles fallaban por un selector de tests que buscaba cualquier Dialog cuando
drawer y confirmación estaban abiertos. Se acotó por nombre accesible y se cierra el drawer antes
de consultar el editor subyacente. La repetición de esos cuatro casos fue correcta; no se relajaron
timeouts, tolerancias de imágenes ni comportamiento del producto para resolverlos.

Las referencias de los 66 estados del Inicio se actualizaron por el cambio visual aprobado;
las seis del panel largo se agregaron. Se revisaron escritorio, móvil, drawer, bienvenida,
respuesta/fuentes, contenido bloqueado, configuración ausente, fallo e incertidumbre.
No se actualizaron referencias de pantallas ajenas. También pasaron las regresiones de WhatsApp.
Los GET/escrituras del panel se instrumentan en el fetch ficticio: plegar, abrir y paginar no generan.

Las pruebas nuevas de backend cubren Unicode, expiración, ausencia de descifrado sin acceso,
contexto de cifrado, corrupción, propietario distinto, borrado, desempate estable por Id ascendente
y proyección acotada instrumentada.
El reloj de expiración de ese test es local; no se retrocede el reloj compartido del factory.

El wrapper npm global de la sesión no funciona. Se invocó el CLI instalado con
`node 'C:/Program Files/nodejs/node_modules/npm/bin/npm-cli.js' run build` y `run lint`;
Vitest/Playwright se invocaron desde sus ejecutables locales equivalentes.
El cierre de Vite de Playwright en Windows requirió detener sólo los PID lanzados por cada run
después de completar sus casos. No se levantó Aspire ni se detuvieron las instancias del usuario.
Los logs de esta ejecución se conservan en `.test-artifacts/panel-conversaciones-2026-10-05/`
de cada repositorio, fuera del producto y de archivos a versionar.

## Fuentes y pendientes

Decisiones consolidadas en la regla funcional de IA, premisas, arquitectura y fundamento visual
del front, instrucciones locales y mapas. La propuesta original conserva evidencia única como
archivo inactivo en `ArquitecturaBaseFront/design-lab/history/home-assistant-conversations-2026-10-05`;
su HTML se preserva como `index.html.reference`, fuera de entradas activas/build.

El plan se conserva porque todavía contiene trabajo independiente pendiente: retención completa
de metadatos, medición de capacidad por instalación, piloto de proveedor y Artifact privado.
Las consultas SQL instrumentadas no son un benchmark ni acreditan soporte ilimitado de clientes.
El desarrollo del panel no modifica cuotas, retención ni WhatsApp.
