. (Join-Path $PSScriptRoot 'common-local.ps1')
if (-not $env:AGRO360_TEST_CONNECTION_STRING -or $env:AGRO360_TEST_CONNECTION_STRING -notmatch '(?i)test|teste') { throw 'Defina AGRO360_TEST_CONNECTION_STRING para banco exclusivo contendo test/teste no nome.' }
$env:ConnectionStrings__Agro360=$env:AGRO360_TEST_CONNECTION_STRING
& (Join-Path $PSScriptRoot 'setup-local.ps1'); & (Join-Path $PSScriptRoot 'migrate-local.ps1')
dotnet test MNSOFT.Agro360.sln --configuration Release --no-build; if ($LASTEXITCODE) { throw 'Testes falharam.' }
