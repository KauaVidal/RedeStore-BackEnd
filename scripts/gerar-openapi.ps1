# Regenera docs/openapi.json a partir dos metadados dos endpoints.
# Os valores abaixo são fictícios: só servem para o Program.cs subir durante a
# geração (nenhuma conexão com banco ou Resend é feita).
$ErrorActionPreference = 'Stop'

$env:ConnectionStrings__Default = 'Host=localhost;Database=geracao-openapi'
$env:Jwt__SigningKey = 'chave-ficticia-somente-para-gerar-o-openapi-json'
$env:Jwt__Issuer = 'RedeStore.OpenApi'
$env:Jwt__Audience = 'RedeStore.OpenApi'
$env:Resend__ApiKey = 'chave-ficticia'
$env:Resend__FromEmail = 'nao-responda@exemplo.com'
$env:Frontend__ResetPasswordUrl = 'http://localhost:4200/redefinir-senha'
$env:Cors__AllowedOrigin = 'http://localhost:4200'

dotnet build "$PSScriptRoot/../src/RedeStore.Api/RedeStore.Api.csproj" -p:OpenApiGenerateDocuments=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "docs/openapi.json atualizado."
