[CmdletBinding()]
param([ValidateSet('Development', 'Homologation')][string]$Environment = 'Homologation')

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common-local.ps1')
Assert-DotNet
if (-not $env:ConnectionStrings__Agro360) { throw 'Defina ConnectionStrings__Agro360 no ambiente ou secret manager local.' }

function Read-PlainSecret([string]$Name, [string]$Prompt) {
    $current = [Environment]::GetEnvironmentVariable($Name)
    if ($current) { return $current }
    $secure = Read-Host $Prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

$keysPath = if ($env:AGRO360_DATA_PROTECTION_KEYS_PATH) { $env:AGRO360_DATA_PROTECTION_KEYS_PATH } else { Join-Path $HOME '.agro360/data-protection-keys' }
[IO.Directory]::CreateDirectory($keysPath) | Out-Null
$env:AGRO360_DATA_PROTECTION_KEYS_PATH = $keysPath
$env:DOTNET_ENVIRONMENT = $Environment
$env:AGRO360_PROVISION_SUPERADMIN_PASSWORD = Read-PlainSecret 'AGRO360_PROVISION_SUPERADMIN_PASSWORD' 'Senha temporária do SuperAdmin'
$env:AGRO360_PROVISION_SANTA_CLARA_PASSWORD = Read-PlainSecret 'AGRO360_PROVISION_SANTA_CLARA_PASSWORD' 'Senha temporária do administrador Santa Clara'

if (-not $env:AGRO360_PROVISION_SUPERADMIN_TOTP_SECRET) {
    $alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
    $bytes = [byte[]]::new(20)
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $bits = -join ($bytes | ForEach-Object { [Convert]::ToString($_, 2).PadLeft(8, '0') })
    $env:AGRO360_PROVISION_SUPERADMIN_TOTP_SECRET = -join (0..31 | ForEach-Object { $alphabet[[Convert]::ToInt32($bits.Substring($_ * 5, 5), 2)] })
}

Write-Host 'Cadastre localmente o segredo abaixo no autenticador. Ele será exibido uma única vez e não deve ser salvo em logs ou no Git.'
Write-Host "otpauth://totp/Agro360:superadmin%40mnsoft.com.br?secret=$($env:AGRO360_PROVISION_SUPERADMIN_TOTP_SECRET)&issuer=Agro360&algorithm=SHA256&digits=6&period=30"
$env:AGRO360_PROVISION_SUPERADMIN_TOTP_CODE = Read-PlainSecret 'AGRO360_PROVISION_SUPERADMIN_TOTP_CODE' 'Código atual do autenticador'

try { dotnet run --project src/Hosts/Agro360.Migrator -- provision-homologation --environment $Environment; if ($LASTEXITCODE) { throw 'Provisionamento falhou.' } }
finally {
    'AGRO360_PROVISION_SUPERADMIN_PASSWORD','AGRO360_PROVISION_SANTA_CLARA_PASSWORD','AGRO360_PROVISION_SUPERADMIN_TOTP_SECRET','AGRO360_PROVISION_SUPERADMIN_TOTP_CODE' |
        ForEach-Object { Remove-Item "Env:$_" -ErrorAction SilentlyContinue }
}
