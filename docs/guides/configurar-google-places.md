# Configurar la búsqueda de lugares

Implementada el 2026-10-04 en Información del negocio. El servidor consulta **Places API
(New)**; el navegador busca nombre/ciudad, elige una sugerencia y abre la ficha completa en
Google Maps. Elegir prepara el borrador; Guardar confirma el Place ID con revisión/comprobante.

## Credencial

1. En el proyecto de Google Cloud de esta instalación, habilitar facturación y **Places API (New)**.
2. Crear una clave independiente, por ejemplo `ArquitecturaBase Places Servidor`.
3. Restringir sus APIs **sólo a Places API (New)**.
4. Restricción de aplicación: **Direcciones IP**, con la IP pública de salida del servidor.
   En local es la IP pública de tu conexión, no localhost, 127.0.0.1 ni una dirección privada.
   Si cambia, actualizarla. Production usa su propia clave y salida estable.
5. Guardar y esperar la propagación de restricciones antes de probar.

La clave previa de **Sitios web** corresponde al SDK JavaScript del navegador; no sirve
para este REST de servidor. Se conserva sin debilitar sus restricciones. Esta integración
no necesita Maps JavaScript API. No copiar claves al front, VITE_*, Git, capturas o chat.

Desde la raíz del backend, cargar mediante stdin sin escribir su valor en el historial:

```powershell
$placesSecret = Read-Host "Clave de Google Places para servidor" -AsSecureString
$placesCredential = [System.Management.Automation.PSCredential]::new("places", $placesSecret)
@{ "ArtificialIntelligence:GooglePlaces:ApiKey" = $placesCredential.GetNetworkCredential().Password } |
    ConvertTo-Json -Compress |
    dotnet user-secrets set --project src/ArquitecturaBase.Api/ArquitecturaBase.Api.csproj
Remove-Variable placesSecret, placesCredential
```

Reiniciar la API. Opcional: `ArtificialIntelligence:GooglePlaces:RegionCode`, por ejemplo AR,
orienta resultados a esa región; no limita el país. Cada clon define la región de su negocio.
Sin valor se conserva el sesgo predeterminado de Google. Agregar ciudad a la búsqueda evita
depender de la ubicación de la IP del servidor.

Production carga `ArtificialIntelligence__GooglePlaces__ApiKey` desde el gestor de secretos
o entorno del host. La clave no se guarda en PostgreSQL ni en la administración. Para rotar,
reemplazar configuración y reiniciar réplicas; los lugares guardados no cambian. Restaurar
la base no restaura la credencial externa.

## Costos y condiciones

Cada búsqueda puede ser facturable: 400 ms de espera, 3–200 caracteres, sin retries ni Place
Details. No se envía sessionToken ni se presume un descuento de sesión sin completarla con
Details/Address Validation. Hay 60 pedidos/minuto por operador, cuatro llamadas simultáneas
por proceso sin cola, cinco segundos de plazo y 32 KiB por respuesta. Configurar cuotas del
proyecto para varias réplicas y alertas; un presupuesto avisa, no corta consumo.

La aplicación debe publicar Términos de uso y Política de privacidad compatibles con Places,
con enlaces a términos/privacidad de Google. **Esta plantilla todavía no tiene esas páginas
públicas: prepararlas por instalación es pendiente antes de publicar el buscador.**
Atribución Google Maps junto a sugerencias; no combinar sus resultados con un mapa OSM.

Sólo se persiste Place ID, permitido por la excepción de almacenamiento de Places. Etiquetas
quedan en memoria del editor, sin QueryCache, MutationCache, storage, logs ni caché HTTP
(no-store). Dirección/referencia son ingresadas por el administrador. El enlace se deriva
en un destino fijo con query_place_id; no se acepta una URL arbitraria. Los puntos anteriores
siguen siendo legibles. No requiere migración: PlaceId es opcional en el JSONB existente y
los comprobantes anteriores conservan exactamente su huella.

## Prueba real pendiente

Con la clave configurada y ambos permisos ai.business.read/ai.business.manage, buscar un
lugar por nombre/ciudad, abrir su ficha, guardar y recargar. Revisar restricciones, cuotas y
rotación de la clave. Esta prueba puede tener costo y se hace manualmente con la cuenta real.
Sin clave, el resto del módulo funciona; buscar devuelve error localizado. Los 429 conservan
espera y requieren una nueva acción; cancelar no demuestra que Google no haya procesado/cobrado.

Suites automatizadas: claves inventadas y dobles HTTP; Docker para PostgreSQL/Redis y
persistencia; fixtures de navegador para diseño. Ninguna suite llama a Google facturable.

Fuentes oficiales revisadas el 2026-10-04: [Autocomplete](https://developers.google.com/maps/documentation/places/web-service/place-autocomplete),
[seguridad](https://developers.google.com/maps/api-security-best-practices),
[políticas](https://developers.google.com/maps/documentation/places/web-service/policies),
[Maps URLs](https://developers.google.com/maps/documentation/urls/get-started) y
[sesiones](https://developers.google.com/maps/documentation/places/web-service/session-pricing).
