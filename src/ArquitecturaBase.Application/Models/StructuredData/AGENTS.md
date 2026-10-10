# StructuredData: contratos neutrales de formularios
Leer [formularios](../../../../docs/features/formularios.md) y los módulos opcionales del backend.
- Primer consumidor real: chequeo de publicación de BusinessForms y adaptador de canal instalado.
- DTOs tipados y cerrados: preguntas canónicas, presentación, culturas, compatibilidad y problemas. Sin JsonElement, tipos de módulos, HTTP, EF, credenciales ni URLs del cliente.
- Perfil y huella de compilación son opacos para el consumidor; el canal conserva JSON/mapping/codec. No exponer hash/JSON técnico en HTTP ni usarlos como autoridad enviada por navegador.
- Sólo ampliar el puerto al existir consumidores reales; envío y captura permanecen fuera de 2b.
2b agrega consumidores reales de Accept/GetPublication/GetPreview. Requests incluyen operación/versión opacas, actor/permisos, snapshot/perfil/huella; responses tipados de progreso/issues y preview oficial con vencimiento exacto. Inputs y URL/huellas privados redactados. No envío/captura/endpoint_data anticipado.
