# Files: puerto de contenido privado

`IPrivateFileStore` intercambia streams y claves opacas generadas por servidor, sin SDK, proveedor o HTTP.
El caso de uso consumidor controla permisos, límites, identidad y persistencia de metadatos; no exponer las claves al navegador.

`OpenReadAsync` devuelve null cuando no existe el objeto; el consumidor lo traduce a su error NotFound. Las fallas de infraestructura no se convierten en ausencia.

`WriteOnceAsync` crea sólo si la clave no existe: true significa creación, false conserva el objeto existente, sin comparar ni reemplazar bytes. El consumidor reconcilia la copia real con `OpenReadAsync`; otras fallas se propagan y no equivalen a ausencia. `WriteAsync` mantiene la sobrescritura idempotente de capturas.
