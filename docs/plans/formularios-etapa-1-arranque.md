# Formularios por WhatsApp: coordinación de la Etapa 1

El chat original coordina el plan y revisa; backend y front conservan sus repositorios, tests y commits separados. El usuario autorizó la coordinación entre ambos chats. Main, commits chicos en español, sin ramas ni push. ADR 0018: tests del área por corte, suite completa al cerrar cada paso; documentos sólo en el cierre, contrato en código/OpenAPI y ejemplos HTTP exportados.

La fuente de pantallas sigue siendo el diseño elegido de Formularios por WhatsApp, lienzo versión 65. D4 publica sólo lo guardado; D5 guarda es/en incompleto y exige ambos completos al publicar. CTA de apertura «Abrir formulario» distinto del botón final; prefijo fijo desde la primera aceptación durable; IDs de preguntas/opciones del servidor; valores del chat requieren `formrecords.read`.

El detalle futuro y los pendientes únicos permanecen en [el plan principal](2026-10-07-formularios-dinamicos-y-whatsapp-flows.md); reglas implementadas y evidencia breve en [formularios](../features/formularios.md#verificación-de-cierre--2026-10-11) y [WhatsApp](../features/whatsapp.md). Se retira la bitácora por corte, sin inventar contratos nuevos ni repetir verificaciones antiguas como actuales.

## Estado al terminar el piloto — 2026-10-11

Pasos 1–4 implementados; envío real Entregado y respuesta PRB1010-000001 guardada y conservada tras recarga. XML/notas de cortes retirados con procedencia local recuperable. Etapa 1 cerrada por instrucción humana: backend 2685/2685 con Docker, front 1455/1455 y corrida visual final 292/294, exit 1, tres workers. Sus dos esperas iniciales de Nombre tienen diagnóstico pendiente y no se presentan como verde. R4 sólo afecta publicaciones futuras; r3 conserva envío, estado y sesiones abiertas, probado en SQL. Los tres menores R3/decimal físico/Roles pasan a Etapa 2 junto con el seguimiento del arnés. No se inició Etapa 2. Evidencia y límites de lo probado en la fuente funcional enlazada; pendientes únicos en el plan principal.
