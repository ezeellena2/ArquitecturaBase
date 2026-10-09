# Información del negocio guiada — 2026-10-03

Entrega autorizada después de revisar y elegir la maqueta. Trabajado en main de ambos
repositorios, preservando cambios anteriores, sin ramas, commits ni push.

## Revisión y correcciones antes de implementar

El modelo productivo anterior tenía siete textos y ya protegía la escritura con revisión,
advisory lock, HMAC y comprobante durable. No correspondía serializar objetos nuevos como
texto ni crear otra operación de guardado. Se agregó un único detalle JSONB nullable a la
misma entidad y al contrato existente; conserva los siete textos para revisión manual.

La búsqueda de la maqueta era ficticia. Se reemplazó por un mapa real Leaflet/OpenStreetMap
con navegación/clic y coordenadas manuales, sin búsqueda ni API de Google. El servidor genera
el enlace a partir del punto validado. No acepta URL arbitraria, no hace geocodificación y no
solicita la ubicación del dispositivo. Confirmar sólo cambia el borrador; Guardar persiste.

Horarios y FAQ conservan su alcance elegido: opciones por día, cruces explícitos de medianoche,
fechas especiales y preguntas editables sin respuestas precargadas. Las FAQ sin respuesta no
cuentan como hechos. No se agregó generación, envío del enlace por WhatsApp ni un bot.

## Árbol implementado

```text
Domain/Modules/ArtificialIntelligence/
  AiBusinessDetails.cs: ubicación, semana/franjas/excepciones y FAQ; reglas/normalización
  AiBusinessInformation.cs, AiBusinessInformationErrors.cs
Application/Modules/ArtificialIntelligence/
  Models/, Validation/, Services/: contratos, validación y caso de uso existente
Api/Modules/ArtificialIntelligence/
  Contracts/: HTTP anidados propios; Controllers/: mapeo manual, misma autorización
Infrastructure/Modules/ArtificialIntelligence/
  Persistence/Configurations, Readers/: JSONB y proyección
  Security/: extensión versionada de HMAC compatible con recibos anteriores
Infrastructure/Persistence/Migrations/
  20261003215957_AddGuidedAiBusinessInformation + designer/snapshot
Front/src/features/artificial-intelligence/
  api/businessInformation, businessDetailsModel, businessInformationModel
  hooks/useBusinessInformationEditor, pages/BusinessInformationPage
  components/Business{Location,Hours,Faq}Editor
Front/src/shared/ui/LocationMap.tsx y LocationMap.css: selector genérico lazy
Front/src/locales/{es,en}/businessInformation.json
tests/*/Modules/ArtificialIntelligence y Front/src/test/visual/
```

Fuentes vigentes: [regla funcional](../features/inteligencia-artificial.md#editor-guiado-y-evolución-del-contrato)
y [decisión visual](../../../ArquitecturaBaseFront/docs/design/informacion-del-negocio-guiada.md).
El laboratorio no aporta bootstrap al producto. Su entrada guiada se retiró como propuesta
activa y redirige al catálogo real; sus archivos sin versionar se conservaron como procedencia.

## Garantías y comprobaciones

- Migración aditiva probada contra PostgreSQL real en Docker: conserva ID, los siete textos,
  revisión, auditoría y comprobantes. No se migró una base del usuario durante las pruebas.
- Contratos HTTP es/en, errores por grupo sin eco del contenido, dos hosts, reintento idéntico,
  mismo UUID con otro punto rechazado y PUT anterior que omite details y los conserva.
- TDD de coordenadas finitas/en rango, orden y cruce de franjas, superposición entre días,
  excepciones y fechas duplicadas, zona IANA, FAQ incompleta/duplicada y copias normalizadas.
- Front: cambio de dirección elimina punto; URL derivada fuera del cuerpo; un solo PUT con
  revisión/UUID; validación impide guardar; incertidumbre congela todo el cuerpo para reintento;
  conflicto combina grupos atómicos. MSW usa datos ficticios, no una API del usuario.
- Backend Release: build con **0 advertencias/errores**, suite completa **2155/2155**, sin
  omitidos, con PostgreSQL/Redis en Docker. `has-pending-model-changes`: ningún cambio pendiente.
- Front: build productivo limpio, lint + 39 reglas de arquitectura, **911/911** Vitest.
  Playwright completo: **103/103**, con regresiones de login/OIDC, chat, roles y configuración
  y recorrido guiado es/en a 1440/390/320. Se agregó después un caso de caída del mapa, probado
  por separado: **1/1**, aviso único, alternativa manual y retorno de foco. Las 104 pruebas
  actuales quedaron ejecutadas en esas dos corridas; no se afirma una corrida única de 104.
  Tras el ajuste final del ancho de los estados del horario se repitieron las **20/20** pruebas
  de Información del negocio, incluido el caso de caída del mapa, y el build/lint final.
  Se revisaron las seis referencias nuevas antes de actualizarlas. Se conservan warnings
  preexistentes del arnés Vite sobre HydrateFallback/NO_COLOR; no son advertencias del build.

Navegador manual: cargaron teselas públicas OSM reales, con atribución, y un clic preparó el
punto/enlace del borrador. Guardado y sesión de esa vista son dobles del catálogo. Evidencia:
[mapa real](../../../ArquitecturaBaseFront/design-lab/whatsapp-chat/evidence/business-guided-real-map-es.png)
y [editor productivo con datos ficticios](../../../ArquitecturaBaseFront/design-lab/whatsapp-chat/evidence/business-guided-product-es.png).
Las pruebas automatizadas interceptan teselas y API; no acreditan disponibilidad del proveedor.
Ningún test llama OpenAI, Anthropic, Google Places o Meta ni usa claves reales.

## Despliegue y pendientes

Ejecutar bundle antes de activar nueva imagen/front en Production; Development aplica la
migración al arrancar. No se modificaron las migraciones anteriores. Leaflet y sus tipos
tienen versiones explícitas; el mapa queda en un chunk separado, cargado al abrir el diálogo.
OSM público no tiene SLA; permite ingreso manual ante falla. Cumple atribución y caché normal,
sin precarga/offline. La plantilla pública VITE permite ajustar host; otro proveedor requiere
revisar atribución/licencia/topes y prueba en el entorno elegido, sin secretos en VITE.
Fuentes oficiales revisadas: [Leaflet](https://leafletjs.com/reference.html),
[política OSM](https://operations.osmfoundation.org/policies/tiles/) y
[Maps URLs](https://developers.google.com/maps/documentation/urls/get-started).

Pendientes: validación de despliegue productivo y red corporativa/proveedor elegido; futura
generación que reconcilie texto anterior y hechos tipados; envío del enlace o ubicación nativa
desde WhatsApp; sincronización del Artifact privado. La migración no decide cuál texto antiguo
es verdadero. No se ejecutaron pruebas facturables. Aspire no se inició en esta entrega.
