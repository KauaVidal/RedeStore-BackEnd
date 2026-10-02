# Popula o PostgreSQL LOCAL (serviço "db" do docker-compose.yml) com dados de exemplo.
# Somente para desenvolvimento. Pré-requisitos: `docker compose up -d db`, a API já ter rodado
# uma vez (as migrations criam as tabelas) e a conta admin@rede.com já cadastrada.
$ErrorActionPreference = 'Stop'

$raiz = Resolve-Path "$PSScriptRoot/.."
$sql = Get-Content -Raw -Encoding UTF8 "$PSScriptRoot/seed-dev.sql"

# Sem isso o Windows PowerShell envia o texto ao processo nativo em ASCII e os acentos viram "?".
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$sql | docker compose -f "$raiz/docker-compose.yml" exec -T db psql -U postgres -d redestore -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Seed aplicado. Se admin@rede.com estava logado, saia e entre de novo (o papel fica no token)."
