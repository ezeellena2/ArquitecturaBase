# Contratos de fuentes locales

Definiciones versionadas y argumentos tipados. Actor, cultura, zona y alcance los fija el servidor. Resultados contienen filas de datos verificables y evidencia con ID del servidor, instante UTC, revisión y destino lógico. Nunca exponer IQueryable, entidades seguidas, SQL o URLs arbitrarias. ToString redacta contenido sensible. Las herramientas de un consumidor validan argumentos adicionales antes de llamar una fuente. Hereda Application y la arquitectura canónica.
