# Inteligencia artificial: módulo opcional

Conexión e información del negocio con entidades, permisos y errores puros. Scope=1 y revisión son invariantes; los comprobantes no contienen credenciales. AiBusinessDetails normaliza y copia ubicación, horarios locales/fechas especiales y FAQ; no interpreta textos anteriores ni llama al mapa externo. PlaceId es opcional, excluye coordenadas y deriva un enlace a la ficha en un destino fijo. No agregar dependencias, reloj global, eventos ni reglas de proveedores HTTP.

Hereda las instrucciones del proyecto. Su fuente funcional es [inteligencia artificial](../../../../docs/features/inteligencia-artificial.md); operación y seguridad, [configurar IA](../../../../docs/guides/configurar-ia.md); independencia, [quitar IA](../../../../docs/guides/quitar-ia.md). No importar otros módulos. Actualizar mapa y fuentes con un cambio de responsabilidad.

El asistente agrega conversaciones privadas, turnos, consultas y recibos durables. Las transiciones
controlan estado/fence, cancelación confirmada y deadline explícito; un Running vencido con un paso
reservado queda Uncertain y nunca vuelve a Pending. Removed es retirada funcional (no ISoftDeletable)
para purgar contenido protegido después. Los recibos independientes conservan cuota/deduplicación
tras la retirada. Uso desconocido no equivale a cero; conservar contadores conocidos por paso.

El perfil de ejecución se sella una sola vez en Pending antes del claim, con máximo de 16384 bytes UTF-8. Repetir el mismo sello no muta la revisión; cambiarlo o intentar una primera captura en otro estado da conflicto. El canal de la conversación distingue Inicio de los canales aportados por sus dueños.
