#!/usr/bin/env bash
set -euo pipefail

environment="${ASPNETCORE_ENVIRONMENT:-Homologation}"
case "${environment,,}" in
  development|homologation) ;;
  *) echo "ERRO: este comando só aceita Development ou Homologation." >&2; exit 2 ;;
esac

: "${ConnectionStrings__Agro360:?Defina ConnectionStrings__Agro360 no ambiente ou secret manager local.}"
keys_path="${AGRO360_DATA_PROTECTION_KEYS_PATH:-${HOME}/.agro360/data-protection-keys}"
mkdir -p "$keys_path"
chmod 700 "$keys_path"

read_secret() {
  local variable="$1" prompt="$2" value
  if [[ -n "${!variable:-}" ]]; then return; fi
  read -r -s -p "$prompt: " value; echo >&2
  [[ -n "$value" ]] || { echo "ERRO: valor obrigatório." >&2; exit 2; }
  printf -v "$variable" '%s' "$value"
  export "$variable"
}

read_secret AGRO360_PROVISION_SUPERADMIN_PASSWORD "Senha temporária do SuperAdmin"
read_secret AGRO360_PROVISION_SANTA_CLARA_PASSWORD "Senha temporária do administrador Santa Clara"

if [[ -z "${AGRO360_PROVISION_SUPERADMIN_TOTP_SECRET:-}" ]]; then
  AGRO360_PROVISION_SUPERADMIN_TOTP_SECRET="$(python3 - <<'PY'
import base64, secrets
print(base64.b32encode(secrets.token_bytes(20)).decode().rstrip('='))
PY
)"
  export AGRO360_PROVISION_SUPERADMIN_TOTP_SECRET
fi

echo "Cadastre agora no autenticador (exibição local única; não copie para logs/Git):" >&2
printf 'otpauth://totp/Agro360:superadmin%%40mnsoft.com.br?secret=%s&issuer=Agro360&algorithm=SHA256&digits=6&period=30\n' "$AGRO360_PROVISION_SUPERADMIN_TOTP_SECRET" >&2
read_secret AGRO360_PROVISION_SUPERADMIN_TOTP_CODE "Código atual do autenticador"
export AGRO360_DATA_PROTECTION_KEYS_PATH="$keys_path"

dotnet run --project src/Hosts/Agro360.Migrator -- provision-homologation --environment "$environment"
unset AGRO360_PROVISION_SUPERADMIN_PASSWORD AGRO360_PROVISION_SANTA_CLARA_PASSWORD \
  AGRO360_PROVISION_SUPERADMIN_TOTP_SECRET AGRO360_PROVISION_SUPERADMIN_TOTP_CODE
