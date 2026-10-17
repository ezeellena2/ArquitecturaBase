# Assistant: contratos neutrales del motor

Contexto de servidor con Actor/Subject explícitos, requisitos separados sin herencia, capacidades Read/Proposal versionadas y codec/payload tipado del dueño. Los requisitos efectivos se sellan y se revalidan para cada identidad; el modelo no elige identidad, permisos, cultura, SQL o URL. Sin efectos/UoW. No referencia módulos opcionales. Todo contenido privado redacta ToString.

Operations lleva el vencimiento del contenido original y devuelve procedencia readonly reconstruida del sello (perfil/instrucciones/versiones/instante original). El motor acota contenido a min(retenciónIA, vencimiento del dueño); recibos metadata conservan su retención independiente. Nunca reconstruir procedencia desde configuración vigente ni exponer prompts, snapshots o IDs de continuación por HTTP.
