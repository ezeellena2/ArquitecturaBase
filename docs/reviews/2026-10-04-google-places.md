# Google Places: integración del buscador de lugares

Fecha: **2026-10-04**. Pedido explícito de integrar el autocompletado de Google en
Información del negocio y configurar la credencial externa. Esta entrega conecta la
pantalla real al backend; no conecta todavía una cuenta real ni genera mensajes de WhatsApp.

## Revisión contra la arquitectura y decisiones

La búsqueda pertenece a ArtificialIntelligence, dueño actual de Información del negocio.
MVC → servicio → puerto → adaptador conserva los límites; no introduce dependencias del
núcleo o WhatsApp. Se explicó el árbol antes de editar. No requiere un ADR estructural.
La búsqueda es una consulta sin transacción ni persistencia: no incorpora otro mecanismo
de guardado. Elegir un lugar modifica sólo el borrador y el PUT existente confirma Place ID
con su revisión, lock y comprobante. El módulo continúa siendo retirable por carpeta.

La clave de sitios web configurada previamente corresponde al SDK del navegador. El recorrido
elegido usa REST desde servidor: requiere otra clave restringida a Places API (New) y a la IP
pública de salida del host. No se debilitaron restricciones, leyeron claves ni guardaron valores
reales. [Guía de configuración, rotación y restauración](../guides/configurar-google-places.md).

Sólo se conserva Place ID, bajo la excepción de almacenamiento de Places. Etiquetas en memoria
del editor, sin cachés, storage o logs; dirección y referencia ingresadas por el administrador.
Maps URLs deriva un destino fijo con query_place_id: no acepta URLs arbitrarias. Los puntos
anteriores siguen siendo legibles. No se muestran resultados de Google sobre mapas de otro
proveedor. Se retiró el modal de coordenadas de este consumidor; LocationMap sigue siendo UI
genérica disponible, sin mezclarla con las sugerencias.

El campo opcional PlaceId en JSONB no necesita una migración. Se detectó que agregar una
propiedad nula a la serialización cambiaría las huellas de comprobantes ya emitidos: se preservó
la proyección histórica exacta de details:v1 y se agrega place:v1 sólo cuando hay ID.
La prueba compara la huella anterior y distingue dos selecciones con el mismo UUID.

## Árbol implementado

```text
Domain/Modules/ArtificialIntelligence/
  AiBusinessDetails (BusinessLocation.PlaceId), BusinessPlacesErrors
Application/Modules/ArtificialIntelligence/
  Models/{SearchBusinessPlacesRequest,BusinessPlacesResponse}
  Interfaces/{Services/IBusinessPlacesService,Integrations/IBusinessPlacesClient}
  Services/BusinessPlacesService, Validation/SearchBusinessPlacesRequestValidator
Infrastructure/Modules/ArtificialIntelligence/
  Places/{GooglePlacesClient,GooglePlacesOptions}
  Security/HmacAiBusinessInformationFingerprint
Api/Modules/ArtificialIntelligence/
  Contracts/SearchBusinessPlacesHttpRequest
  Controllers/BusinessPlacesController
tests/*/Modules/ArtificialIntelligence/

Front/src/features/artificial-intelligence/
  api/businessPlaces, components/BusinessPlaceSearch, components/BusinessLocationEditor
  businessDetailsModel, api/businessInformation
Front/src/shared/ui/Autocomplete + pruebas
Front/src/{locales/{es,en}/businessInformation.json,test/visual/business-guided.spec}
```

## HTTP, credenciales y límites

POST /api/ai/business-information/places/search exige ai.business.read y ai.business.manage;
sin sesión 401, sin ambos permisos 403. Request propio con ToString redactado; validación
3–200 caracteres y 4 KiB de cuerpo. Respuestas no-store y ProblemDetails localizado.
Rate limit por operador/IP de 60/minuto, cuatro búsquedas simultáneas por proceso sin cola.
HTTP fijo, clave en header, field mask mínimo, cinco segundos y 32 KiB. Sin redirects,
loggers del cliente ni resiliencia que repita búsquedas potencialmente facturables.
Los errores de Google nunca se devuelven literalmente. Sin credencial no hay petición externa.

Frontend usa el transporte central, espera 400 ms y cancela respuestas obsoletas. Un 429
conserva cooldown y exige otra acción al vencerlo. Selección con teclado/puntero, foco
conservado, atribución visible dentro del desplegable y fuera al cerrarlo. Campo/Public Sans,
Page/Surface y Toaster único se mantienen; no se copió el bootstrap del laboratorio.

## Verificaciones ejecutadas

- TDD de reglas de ubicación, cliente Google, servicio y controles. El test de comprobante
  histórico falló antes de preservar su proyección; el de atribución falló antes de agregar
  el footer no seleccionable. La prueba del formulario usa pegado para evitar renderizar
  toda la pantalla por cada carácter; una corrida bajo carga había excedido sus cinco segundos.
- Backend: dotnet build ArquitecturaBase.slnx sin advertencias/errores; dotnet test completo
  **2.199 aprobadas, cero fallidas/omitidas**, con PostgreSQL/Redis de Testcontainers y Docker.
  Después se ejecutaron **12 pruebas de GooglePlacesContractTests** y **18 de
  AiBusinessInformationEndpointsTests**, incluyendo timeout, cancelación, capacidad, tamaño,
  no-store, permisos separados y persistencia/deduplicación entre dos hosts.
- Front: build aprobado; lint sin advertencias y **48 controles de arquitectura** aprobados;
  suite completa **957 pruebas, 90 archivos**, aprobada.
- Playwright completo: **116 pruebas aprobadas**. Incluye es/en y 1440/390/320, buscador,
  enlace, horarios/FAQ, guardados inciertos, login/sesión, chat, roles y configuración.
  El fixture de portada se completó con el rechazo OIDC silencioso simulado que esperaba
  el nuevo recuperador de sesión; no se modificó autenticación productiva.
  Se usó temporalmente el puerto 5181 porque 5178 estaba ocupado; configuración retirada
  al finalizar. Advertencias del arnés: NO_COLOR/FORCE_COLOR y HydrateFallback del router.
- Captura inspeccionada del desplegable con datos ficticios:
  [evidencia](../../../ArquitecturaBaseFront/design-lab/whatsapp-chat/evidence/google-places-autocomplete-es.png).
  Una dependencia omitida del efecto de iniciales en el laboratorio existente se completó
  para que lint quedara limpio; no cambia el chat productivo.

Todas las respuestas de proveedores fueron dobles con claves inventadas; el cliente nombrado
se bloquea contra red en el arnés. No se llamó a Google, OpenAI o Anthropic facturable.
No se inició Aspire; no se detuvieron servicios del usuario. Trabajo en main, sin commits/push.

## Pendientes explícitos

- Crear/configurar clave de servidor por instalación y cargarla en user-secrets o gestor del
  host. Prueba real de buscar un lugar existente, abrir ficha, guardar, recargar y rotar.
- Configurar cuotas/alertas del proyecto; las alertas de presupuesto no cortan consumo.
- Antes de publicar, completar Términos de uso y Política de privacidad públicos requeridos
  por Places. La plantilla todavía no tiene esas páginas. La suite verde no acredita despliegue
  real ni cumplimiento de esas condiciones por una instalación.
- Retirada ampliada del módulo en copia aislada y restauración de backup real siguen sin
  ejecutarse. La [receta](../guides/quitar-ia.md) se actualizó, sin declarar esas pruebas completas.

Fuentes oficiales verificadas: [Autocomplete (New)](https://developers.google.com/maps/documentation/places/web-service/place-autocomplete),
[seguridad](https://developers.google.com/maps/api-security-best-practices),
[políticas de contenido](https://developers.google.com/maps/documentation/places/web-service/policies),
[Maps URLs](https://developers.google.com/maps/documentation/urls/get-started) y
[facturación de sesiones](https://developers.google.com/maps/documentation/places/web-service/session-pricing).
