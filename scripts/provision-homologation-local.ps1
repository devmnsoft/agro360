param(
    [ValidateSet('Development', 'Homologation')]
    [string]$Environment = $(if ($env:ASPNETCORE_ENVIRONMENT) { $env:ASPNETCORE_ENVIRONMENT } else { 'Homologation' })
)

$ErrorActionPreference = 'Stop'

if (-not $env:ConnectionStrings__Agro360) {
    throw 'Defina ConnectionStrings__Agro360 no ambiente ou secret manager local; a senha não será exibida.'
}

if (-not $env:AGRO360_DATA_PROTECTION_KEYS_PATH) {
    $env:AGRO360_DATA_PROTECTION_KEYS_PATH = Join-Path $HOME '.agro360\data-protection-keys'
}

New-Item -ItemType Directory -Force -Path $env:AGRO360_DATA_PROTECTION_KEYS_PATH | Out-Null

function Read-PlainSecret([string]$Prompt) {
    $secret = Read-Host -Prompt $Prompt -AsSecureString
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    try {
        [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
    }
}

function New-TotpSecret {
    $bytes = [byte[]]::new(20)
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
    $bits = 0
    $buffer = 0
    $chars = New-Object System.Text.StringBuilder
    foreach ($byte in $bytes) {
        $buffer = ($buffer -shl 8) -bor $byte
        $bits += 8
        while ($bits -ge 5) {
            $index = ($buffer -shr ($bits - 5)) -band 31
            [void]$chars.Append($alphabet[$index])
            $bits -= 5
        }
    }
    if ($bits -gt 0) {
        $index = ($buffer -shl (5 - $bits)) -band 31
        [void]$chars.Append($alphabet[$index])
    }
    $chars.ToString()
}

try {
    if (-not $env:AGRO360_PROVISION_SUPERADMIN_PASSWORD) {
        $env:AGRO360_PROVISION_SUPERADMIN_PASSWORD = Read-PlainSecret 'Senha temporária do SuperAdmin'
    }
    if (-not $env:AGRO360_PROVISION_SANTA_CLARA_PASSWORD) {
        $env:AGRO360_PROVISION_SANTA_CLARA_PASSWORD = Read-PlainSecret 'Senha temporária do administrador Santa Clara'
    }
    if (-not $env:AGRO360_PROVISION_SUPERADMIN_TOTP_SECRET) {
        $env:AGRO360_PROVISION_SUPERADMIN_TOTP_SECRET = New-TotpSecret
    }

    Write-Host 'Cadastre agora no autenticador (exibição local única; não copie para logs/Git):' -ForegroundColor Yellow
    Write-Host ('otpauth://totp/Agro360:superadmin%40mnsoft.com.br?secret={0}&issuer=Agro360&algorithm=SHA256&digits=6&period=30' -f $env:AGRO360_PROVISION_SUPERADMIN_TOTP_SECRET)

    if (-not $env:AGRO360_PROVISION_SUPERADMIN_TOTP_CODE) {
        $env:AGRO360_PROVISION_SUPERADMIN_TOTP_CODE = Read-PlainSecret 'Código atual do autenticador'
    }

    dotnet run --project src/Hosts/Agro360.Migrator -- provision-homologation --environment $Environment
    if ($LASTEXITCODE) {
        throw "Provisionamento falhou com código $LASTEXITCODE."
    }
}
finally {
    Remove-Item Env:\AGRO360_PROVISION_SUPERADMIN_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:\AGRO360_PROVISION_SANTA_CLARA_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:\AGRO360_PROVISION_SUPERADMIN_TOTP_SECRET -ErrorAction SilentlyContinue
    Remove-Item Env:\AGRO360_PROVISION_SUPERADMIN_TOTP_CODE -ErrorAction SilentlyContinue
}
