# Costos de Azure dev — comparación del 2026-10-02

Estado: comparación de referencia, sin recursos creados. El titular descartó el costo mensual de USD 50 y pide un ambiente gratuito. La configuración paga 24/7 de abajo no se adopta. Se compararon East US y Brazil South; la elegibilidad de servicios gratuitos y la disponibilidad en esta suscripción se verifican después de iniciar sesión.

## Restricción vigente: ambiente gratuito

Se busca publicar el producto para pruebas personales sin un gasto fijo mensual de USD 50. No crear recursos de la propuesta paga ni activar su deploy. Los workflows preparados conservan la validación de ambos repositorios, pero su publicación actual a Container Apps/ACR/PostgreSQL requiere adaptar el destino si elegimos App Service F1.

| Opción | Beneficio verificado | Límite relevante para este producto |
|---|---|---|
| App Service F1 | Hosting gratuito; 60 minutos de CPU por día y 1 GB de almacenamiento. | No equivale a una hora de disponibilidad: se mide CPU. Sin Always On, puede dormir; no garantiza workers de correo/WhatsApp continuos. No incluye PostgreSQL ni Redis. |
| Container Apps Consumption | Franquicia mensual permanente de CPU/RAM y requests. | Es consumo gratuito hasta una cuota; puede facturar excedentes. Dos contenedores encendidos todo el mes no entran automáticamente en ella. |
| PostgreSQL de Azure | Oferta de cuenta gratuita: hasta 750 horas de Burstable y 32 GB por mes durante 12 meses. | Confirmar beneficio en esta suscripción; no es un PostgreSQL gratuito permanente para cualquier cuenta. |
| Blob de Azure | Oferta gratuita de 12 meses: 5 GB Hot/LRS, 20.000 lecturas y 10.000 escrituras. | Confirmar elegibilidad; excedentes y fin de oferta pueden generar cargos. |

Fuentes: [cuotas de App Service](https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/azure-subscription-service-limits#azure-app-service-limits), [Container Apps](https://azure.microsoft.com/en-us/pricing/details/container-apps/), [oferta PostgreSQL](https://azure.microsoft.com/en-us/products/postgresql/) y [servicios gratuitos](https://azure.microsoft.com/en-us/pricing/free-services/).

Primer candidato: React/API juntos en App Service F1. Para los datos, revisar beneficios de Azure; si no están disponibles, evaluar PostgreSQL Neon Free y Redis Upstash Free. Neon publica 0,5 GB y 100 CU-horas por proyecto; Upstash Free publica 256 MB y 500.000 comandos/mes. Hay que comprobar conexión, migraciones, TLS, scripts Redis y consumo real antes de adoptar estos proveedores. [Neon](https://neon.com/blog/building-patterns-unlocked-by-scale-to-zero), [Upstash](https://upstash.com/pricing/redis).

El Azure Blob real se conserva como proveedor elegido; sin beneficio vigente no se promete gratuito. Azurite local es una alternativa sin facturación para desarrollar el adaptador, pero no verifica acceso al servicio real de Azure.

Publicar solo React manteniendo el backend local no satisface por sí solo el ambiente independiente: la nube no puede conectarse al localhost de la computadora sin preparar un enlace de red. Esa opción no se adopta como atajo.

En el portal: Subscriptions → la suscripción → revisar servicios gratuitos y fecha de la oferta. La tabla de uso puede tardar en aparecer si aún no se consumió nada; consultar también el tipo de oferta. La CLI todavía requiere az login, así que los beneficios no se verificaron. [Comprobación oficial de beneficios](https://learn.microsoft.com/en-us/azure/cost-management-billing/manage/check-free-service-usage).

## Escenario pago anterior, conservado como comparación

Usar East US para dev, Container Apps Consumption, ACR Basic, PostgreSQL Flexible Server Burstable B1ms con 32 GB y Blob GPv2 Hot/LRS privado.

Redis tiene dos candidatos: una Container App interna con Redis, o Azure Managed Redis B0 sin alta disponibilidad para dev. La opción interna conserva el mismo Redis 8.6 usado en local, sin publicar su puerto en Internet. No elegir por la idea de que un contenedor es gratis: Managed Redis B0 tiene una tarifa pequeña y puede competir en precio. Hay que completar red, nodos y disponibilidad antes de decidir.

Para comparar la opción interna usamos dos apps, cada una con 0,25 vCPU y 0,5 GiB, una réplica mínima y máxima. Son tamaños de prueba: el arranque, consumo de memoria y workers reales deben medirse; si la API necesita más memoria se ajusta el presupuesto. Redis requiere límite de memoria por debajo de la asignada, autenticación y política que no elimine leases de coordinación por presión de memoria.

Una sola Container App pública contiene React y la API. Redis es otra app, accesible solo dentro del mismo environment por TCP. No se incluye un CDN ni un segundo hosting del front. [Ingress interno de Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview).

## Comparación mensual

USD de lista, sin descuentos ni impuestos, consultados con la [API pública de precios de Azure](https://learn.microsoft.com/en-us/rest/api/cost-management/retail-prices/azure-retail-prices). Se usa 730 horas (30,42 días) y 10 GB de blobs. No es una cotización de la suscripción.

| Componente | East US | Brazil South | Cálculo |
|---|---:|---:|---|
| PostgreSQL B1ms, cómputo 24/7 | 12,41 | 25,55 | 730 × 0,017 / 0,035 USD/h |
| PostgreSQL, 32 GB de disco estándar | 3,68 | 6,99 | 32 × 0,115 / 0,2185 USD/GB-mes |
| ACR Basic | 5,07 | 5,07 | 730/24 × 0,1666 USD/día |
| Blob Hot/LRS, 10 GB | 0,21 | 0,33 | 10 × 0,0208 / 0,0326 USD/GB-mes |
| **Subtotal sin API/Redis** | **21,37** | **37,94** | Redondeado |
| API + Redis internos, escenario todo activo 24/7 | 34,02 | 34,02 | Dos apps de 0,25 vCPU y 0,5 GiB, descontando franquicia |
| **Total de esos componentes, escenario activo** | **55,39** | **71,96** | No incluye los adicionales de abajo |

La diferencia de región ronda USD 16,57/mes para esos recursos. La latencia desde Argentina puede ser mayor en East US; para este dev priorizamos costo y luego medimos la experiencia real.

**No incluye:** logs/Log Analytics, operaciones Blob, transferencia a Internet, backups por encima de la franquicia incluida, almacenamiento extra de ACR, SMTP u otros servicios externos, impuestos, Private Endpoints, NAT Gateway ni opciones premium. Una alerta de USD 70 sería un punto de partida para esta propuesta; no es un tope de facturación ni garantiza que todos esos adicionales entren.

El escenario todo activo es conservador para el cómputo del tamaño elegido, no un máximo de la factura. El uso idle puede bajar el costo; más réplicas, RAM/CPU, otras apps o cuotas gratuitas ya consumidas lo pueden subir.

## Tarifas consultadas y cómo reproducir

Endpoint: https://prices.azure.com/api/retail/prices, currencyCode USD. Filtros: priceType eq 'Consumption', armRegionName eastus/brazilsouth; serviceName y producto según la tabla. Se revisó que las respuestas usadas no tuvieran NextPageLink pendiente.

| Servicio / producto | SKU / meter | East US | Brazil South | Unidad |
|---|---|---:|---:|---|
| Azure Database for PostgreSQL / Flexible Server Burstable BS Series Compute | B1MS / B1MS | 0,017 | 0,035 | hora |
| Azure Database for PostgreSQL / Flex Server Storage | Storage / Storage Data Stored | 0,115 | 0,2185 | GB/mes |
| Container Registry | Basic / Basic Registry Unit | 0,1666 | 0,1666 | día |
| Storage / General Block Blob v2 | Hot LRS / Hot LRS Data Stored, tierMinimumUnits=0 | 0,0208 | 0,0326 | GB/mes |
| Azure Container Apps | Standard / Standard vCPU Active Usage | 0,000024 | 0,000024 | vCPU-segundo |
| Azure Container Apps | Standard / Standard vCPU Idle Usage | 0,000003 | 0,000003 | vCPU-segundo |
| Azure Container Apps | Standard / Standard Memory Active/Idle Usage | 0,000003 | 0,000003 | GiB-segundo |
| Azure Managed Redis - Balanced | B0 / B0 Cache Instance | 0,016 | No cotizado | hora, revisar nodos y HA |

Ejemplo de consulta desde PowerShell:

```powershell
$priceFilter = "serviceName eq 'Azure Database for PostgreSQL' and armRegionName eq 'eastus' and priceType eq 'Consumption'"
$priceUri = 'https://prices.azure.com/api/retail/prices?$filter=' + [uri]::EscapeDataString($priceFilter)
$priceResult = Invoke-RestMethod -Uri $priceUri
$priceResult.Items | Where-Object skuName -eq B1MS |
    Select-Object productName, skuName, meterName, retailPrice, unitOfMeasure
```

Para consumo de ambas apps:

```text
segundos = 730 × 3600 = 2.628.000
CPU total = 0,5 × segundos = 1.314.000 vCPU-segundos
RAM total = 1 × segundos = 2.628.000 GiB-segundos
CPU facturable = (1.314.000 - 180.000) × 0,000024 = USD 27,216
RAM facturable = (2.628.000 - 360.000) × 0,000003 = USD 6,804
total activo = USD 34,02
```

La franquicia de 180.000 vCPU-segundos, 360.000 GiB-segundos y dos millones de requests es por suscripción/mes, compartida entre apps. No se aplica una franquicia completa a cada contenedor. La elegibilidad idle depende de réplicas, tráfico y CPU; un worker puede mantener la app activa aun sin visitas. [Facturación de Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/billing).

## Redis interno frente a administrado

| Opción | A favor | Lo que falta contemplar |
|---|---|---|
| Redis 8.6 en Container App interna | Mismo servidor usado en local; red interna, sin otro endpoint público; control de versión. | Lo operamos nosotros; consume CPU/RAM de Container Apps; reinicios pierden caché y coordinación si no hay persistencia. |
| Managed Redis B0 para dev | Operación administrada; tarifa publicada 0,016 USD/h equivale a 11,68 USD por 730 horas de ese meter. | Confirmar precio con HA deshabilitada/nodos y red; Private Link agrega costos; habilitar TLS/autenticación y probar comandos/scripts del proyecto. |

Una única API de 0,25 vCPU/0,5 GiB todo activo costaría USD 14,31 descontando la franquicia completa. Si la cotización efectiva de B0 de un nodo confirma USD 11,68 sin extras, API+B0 serían USD 25,99 frente a USD 34,02 de ambas apps activas. Esto **no compara aún una configuración de red equivalente**: Managed Redis privado requiere preparar y cotizar la red. Por eso no se da la opción interna como automáticamente más barata.

Azure ofrece configuraciones sin HA para dev/test; la disponibilidad y pérdida de datos pueden ser peores que con HA. La página de precios distingue uno y dos nodos. [Precios de Managed Redis](https://azure.microsoft.com/en-us/pricing/details/managed-redis/) y [opciones de red](https://learn.microsoft.com/en-us/azure/redis/private-link).

Antes de elegir el tamaño hay que validar si la API alcanza con 0,5 GiB; no se probó todavía el arranque del producto bajo ese límite.

## Cómo bajar consumo sin romper las pruebas

Mantener una réplica mientras se prueban los workers de correo/WhatsApp y coordinación. Escalar automáticamente a cero no ejecuta esos procesos cuando no hay réplicas; una request entrante puede despertar la web, pero no garantiza entrega de trabajos pendientes durante la pausa.

En una pausa de dev, coordinar la detención de consumidores con la base. PostgreSQL detenido deja de cobrar cómputo, pero conserva cargos de disco/backups y vuelve a iniciarse automáticamente después de siete días. No dejar la API reiniciándose contra una base apagada. [Detener PostgreSQL](https://learn.microsoft.com/en-us/azure/postgresql/configure-maintain/how-to-stop-server) y [optimización de costos](https://learn.microsoft.com/en-us/azure/postgresql/configure-maintain/how-to-cost-optimization).

No se crearon tareas programadas de encendido/apagado. Podremos diseñarlas después de tener el primer ambiente funcionando y medir consumo.

No contratar reservas anuales para estas pruebas. Elegir Consumption, no un workload Dedicated; no activar Private Endpoint del environment o mantenimiento premium sin cotizarlos, porque pueden agregar cargos de administración. Los logs se acotan por volumen y retención; no se supone que sean gratis.

## Próxima comprobación

Iniciar sesión en Azure, revisar créditos/cuotas y disponibilidad B1ms/Container Apps/B0 en East US. Después cerrar red y elección de Redis, confirmar el resumen de precios en la suscripción y recién entonces crear el grupo dev. Los workflows siguen deshabilitados hasta completar infraestructura y secrets.
