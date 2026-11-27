---
name: publicar-en-azure
description: Publicar ArquitecturaBase en Azure o cambiar algo de una instalación. Usalo cuando el titular pida subir, publicar o desplegar (en dev o en un cliente), dar de alta o de baja un cliente, cambiar una clave, un secreto, el tamaño o el dominio de una instalación, prender WhatsApp o el asistente en Azure, o revisar qué quedó configurado y cuánto cuesta.
---

# Publicar en Azure

La fuente es [`docs/deploy/README.md`](../../../docs/deploy/README.md): leela entera antes de empezar. Tiene cómo está
armado, qué se configura en cada lugar, de qué user-secret sale cada valor, los procedimientos y las trampas. Este skill
agrega sólo cómo hacerlo desde la PC del titular.

## Con el titular

- **Los valores están en sus user-secrets: leelos vos.** No le preguntes dónde está cada clave. Pasalas al script como
  variables de entorno del mismo proceso, sin escribirlas en el chat, en archivos del repositorio ni en la salida.
- **Confirmá antes de lo que cuesta o publica:** crear una instalación nueva (unos USD 35 a 60 por mes), agrandar una, y
  cada push a `main` (publica en dev). La publicación en un cliente la aprueba él en GitHub («Review deployments»).
- **Lo que tipea él:** claves que no están en sus user-secrets (la de OpenAI va en la pantalla de cada instalación; una
  clave nueva de Google, con un `Read-Host -AsSecureString` que le das en un bloque de PowerShell), contraseñas y códigos
  por SMS. Vos nunca escribís una clave ni una contraseña en una página.
- **Su Chrome:** las consolas (Azure, Google Cloud, Meta, GitHub) y la app se manejan con Claude in Chrome, en su perfil
  de Gmail, nunca en el de LC Agro. Si hay varios navegadores conectados, mirá sus pestañas antes de actuar.

## Herramientas en esta PC

- Azure CLI está en `C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin`. En Git Bash:
  `export PATH="/c/Program Files/Microsoft SDKs/Azure/CLI2/wbin:$PATH"`. Desde PowerShell, `& $az` con la ruta de
  `az.cmd`. Un `--query` de JMESPath con corchetes se rompe al pasar por `az.cmd`: pedí `-o json` y filtrá con
  `ConvertFrom-Json`.
- `gh` está autenticado con su cuenta; `az login` ya está hecho (si vence, lo hace él en su navegador).
- El script tarda 15 a 20 minutos y una corrida de `deploy.yml` 20 a 30: corrélos en segundo plano.

## Correr provision-azure.sh con sus user-secrets

Desde la raíz del repo, en Bash. Lee los user-secrets, arma las variables y el archivo de perfiles en el scratchpad, y
los borra al terminar. Agregá las variables propias de la instalación (`INSTALLATION`, `CUSTOM_DOMAIN`,
`EMAIL_DAILY_LIMIT`, `SEED_ADMIN_EMAIL` si el administrador no es el titular, y para dev `PG` y `ACA_ENV`).

```bash
export PATH="/c/Program Files/Microsoft SDKs/Azure/CLI2/wbin:$PATH"
S="<scratchpad>"
dotnet user-secrets list --json --project src/ArquitecturaBase.Api > "$S/us.json"
python -I - "$S/us.json" "$S/ai-settings.txt" "$S/env.sh" <<'PY'
import sys, json, shlex
raw = open(sys.argv[1], encoding="utf-8").read()
d = json.loads(raw[raw.index("{"): raw.rindex("}") + 1])
with open(sys.argv[2], "w", encoding="utf-8", newline="\n") as f:
    for k, v in sorted(d.items()):
        if k.startswith("ArtificialIntelligence:Assistant:"):
            f.write(k.replace(":", "__") + "=" + v + "\n")
names = {"SMTP_USERNAME": "Email:Smtp:UserName", "SMTP_PASSWORD": "Email:Smtp:Password",
         "SMTP_FROM_ADDRESS": "Email:Smtp:FromAddress", "SEED_ADMIN_EMAIL": "Seed:AdminEmail",
         "ALERT_EMAIL": "Seed:AdminEmail", "GOOGLE_CLIENT_ID": "Authentication:Google:ClientId",
         "GOOGLE_CLIENT_SECRET": "Authentication:Google:ClientSecret",
         "GOOGLE_PLACES_API_KEY": "ArtificialIntelligence:GooglePlaces:ApiKey"}
with open(sys.argv[3], "w", encoding="utf-8", newline="\n") as f:
    for env, key in names.items():
        if d.get(key):
            f.write(f"export {env}={shlex.quote(d[key])}\n")
PY
rm -f "$S/us.json"; source "$S/env.sh"; rm -f "$S/env.sh"
INSTALLATION=<instalación> LOCATION=centralus EXTRA_SETTINGS_FILE="$S/ai-settings.txt" \
AZURE_SUBSCRIPTION_ID=59608b9d-bdb8-466a-a5a3-149fe20a8e2b GITHUB_REPOSITORY=ezeellena2/ArquitecturaBase \
bash docs/deploy/provision-azure.sh 2>&1 | tr '\r' '\n' | grep -v "Running \.\.\|^WARNING\|^$"
rm -f "$S/ai-settings.txt"
```

WhatsApp sólo para la instalación dueña del número: sumá al diccionario `WHATSAPP_PHONE_NUMBER_ID` ← `WhatsApp:PhoneNumberId`,
y lo mismo con `ACCESS_TOKEN`, `APP_SECRET`, `VERIFY_TOKEN`, `WABA_ID`, `DISPLAY_PHONE_NUMBER` y
`SEND_ARGENTINE_MOBILES_WITHOUT_NINE`. El número de sus user-secrets es el de prueba: dev ya lo tiene (con su webhook
propio hacia el túnel de la PC) y un cliente lleva siempre su propio número, nunca ése.

## Después de crear o publicar

1. Comprobar con los pasos de «Comprobar» del README: revisión sana, `/health`, ingreso en su Chrome.
2. En la pantalla de la instalación: él pega la clave de OpenAI; vos elegís `gpt-6.1-sol`, marcás «Disponible»,
   «Probar conexión» y «Guardar». La información del negocio la podés cargar vos con lo que él te pase o con su sitio.
3. Una instalación o un cambio de configuración nuevo va a la [tabla de clientes](../../../docs/deploy/produccion-por-cliente.md#clientes)
   o a las notas de [Azure dev](../../../docs/deploy/azure-dev.md), y a la memoria `produccion-por-cliente`. Commit en
   español; el push, sólo con su sí.
