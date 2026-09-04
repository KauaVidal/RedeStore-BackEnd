# Guia de Deploy — RedeStore Backend

Este documento descreve como construir e rodar a imagem Docker de produção
da API, e quais variáveis de ambiente ela exige. Nenhum provedor de
hospedagem específico está amarrado aqui — a imagem roda em qualquer
ambiente que aceite um container Docker padrão escutando na porta 8080.

## Build

```bash
docker build -t redestore-api:latest .
```

## Variáveis de ambiente

Todas usam a convenção de dupla-underscore do ASP.NET Core para seções
aninhadas (`Secao:Chave` no `appsettings.json` vira `Secao__Chave` como
variável de ambiente). Nenhuma delas deve ir para um `appsettings.json`
commitado — em produção, todas vêm de variáveis de ambiente (ou do
mecanismo de segredos do seu provedor de hospedagem).

| Variável | Obrigatória | Formato / exemplo | Descrição |
|---|---|---|---|
| `ConnectionStrings__Default` | Sim | `Host=<host>;Port=5432;Database=redestore;Username=<user>;Password=<senha>` | Connection string do PostgreSQL. A aplicação aplica as migrations pendentes automaticamente na inicialização — não é necessário rodar `dotnet ef database update` manualmente antes do deploy. |
| `Jwt__SigningKey` | Sim | string com pelo menos 32 bytes | Chave HMAC-SHA256 usada para assinar e validar os access tokens JWT. A aplicação falha ao iniciar (`ValidateOnStart`) se estiver ausente ou curta demais. |
| `Jwt__Issuer` | Sim | ex: `https://api.redestore.com.br` | Emissor esperado nos tokens JWT. |
| `Jwt__Audience` | Sim | ex: `redestore-frontend` | Audiência esperada nos tokens JWT. |
| `Jwt__ExpirationHours` | Não (padrão: `8`) | inteiro | Validade do access token em horas. |
| `Resend__ApiKey` | Sim | chave de API do Resend | Usada para enviar o e-mail de recuperação de senha. A aplicação falha ao iniciar se estiver ausente. |
| `Resend__FromEmail` | Sim | ex: `nao-responda@redestore.com.br` | Endereço remetente dos e-mails enviados via Resend. |
| `Frontend__ResetPasswordUrl` | Sim | ex: `https://app.redestore.com.br/redefinir-senha` | URL do frontend Angular para onde o link de redefinição de senha aponta. |
| `Cors__AllowedOrigin` | Não (padrão: `http://localhost:4200`) | ex: `https://app.redestore.com.br` | Origem liberada pela policy de CORS. Em produção, deve apontar para o domínio real do frontend, não o padrão de desenvolvimento. |
| `ASPNETCORE_ENVIRONMENT` | Não (padrão: `Production` em um container) | `Production` \| `Development` | Controla exposição do Scalar/OpenAPI (`/scalar`, `/openapi`) — só ficam disponíveis em `Development`. Deixe sem definir (ou `Production`) em produção. |

## Rodando localmente para testar a imagem

```bash
docker compose up -d db   # sobe só o Postgres, via docker-compose.yml já existente

docker run --rm -p 8080:8080 \
  -e ConnectionStrings__Default="Host=host.docker.internal;Port=5432;Database=redestore;Username=postgres;Password=postgres" \
  -e Jwt__SigningKey="troque-por-uma-chave-de-pelo-menos-32-caracteres" \
  -e Jwt__Issuer="RedeStore.Local" \
  -e Jwt__Audience="RedeStore.Local.Clients" \
  -e Resend__ApiKey="sua-chave-resend" \
  -e Resend__FromEmail="nao-responda@exemplo.com" \
  -e Frontend__ResetPasswordUrl="http://localhost:4200/redefinir-senha" \
  redestore-api:latest
```

`GET /health` deve responder `{"status":"healthy","database":"connected"}`.

## Migrations

A imagem aplica automaticamente qualquer migration pendente do EF Core na
inicialização (`RedeStoreDbContext.Database.MigrateAsync()`, chamado antes
do `app.Run()`). Não existe um artefato separado de "migration runner" —
o próprio container, ao subir, garante que o schema do banco está
atualizado antes de aceitar requisições.
