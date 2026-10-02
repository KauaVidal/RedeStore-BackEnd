# Guia de Deploy — RedeStore Backend

A API é um container Docker (ASP.NET Core / .NET 10) que escuta em HTTP. Ela roda em qualquer
plataforma que aceite containers. Este guia cobre a **Vercel** (Functions com container, usando o
`Dockerfile.vercel`) e serve para Render, Railway, Fly.io ou Azure App Service com o `Dockerfile`.

## Arquitetura recomendada

```
Navegador ──► Vercel: projeto do frontend (Angular estático)  ex.: https://redestore.vercel.app
    │
    └──────► Vercel: projeto do backend (container .NET)       ex.: https://redestore-api.vercel.app
                 │
                 └──► PostgreSQL gerenciado (Neon via Vercel Marketplace, Supabase, ...)
```

Frontend e backend ficam em **projetos separados** na Vercel (um por repositório), cada um com o seu
domínio. O frontend chama a API pela URL pública dela; o CORS da API libera só o domínio do frontend.

## Deploy na Vercel

1. **Banco:** no projeto do backend, em *Storage → Create Database*, crie um Postgres (Neon) na
   mesma região da função (ex.: `gru1` / São Paulo). A integração injeta `DATABASE_URL`
   (formato `postgresql://...`), que a API já entende — não é preciso convertê-la.
2. **Projeto:** importe o repositório `RedeStore-BackEnd`. A Vercel detecta o `Dockerfile.vercel`
   na raiz e builda a imagem. A API escuta na porta informada em `PORT`.
3. **Variáveis de ambiente:** cadastre as da tabela abaixo em *Settings → Environment Variables*
   (marque como *Sensitive* as secretas). Use valores diferentes para *Production* e *Preview*.
4. **Deploy** e confira `GET https://<api>/health` → `{"status":"healthy","database":"connected"}`.
5. **Primeiro admin:** cadastre-se pelo site e promova o usuário direto no banco:

   ```sql
   UPDATE "Usuarios" SET "Papel" = 'Admin' WHERE "Email" = 'seu-email@exemplo.com';
   ```

   Faça login de novo depois disso — o papel vai dentro do JWT.

## Variáveis de ambiente

Seções aninhadas usam dupla-underscore (`Secao:Chave` vira `Secao__Chave`). Nenhum segredo vai para
`appsettings.json` commitado.

| Variável | Obrigatória | Formato / exemplo | Descrição |
|---|---|---|---|
| `DATABASE_URL` **ou** `ConnectionStrings__Default` | Sim | `postgresql://user:senha@host/redestore?sslmode=require` ou `Host=...;Database=...;Username=...;Password=...;SSL Mode=Require` | Conexão com o PostgreSQL. Aceita formato URI ou chave=valor; `ConnectionStrings__Default` tem prioridade. Em banco gerenciado, sempre com SSL (`sslmode=require`). |
| `Jwt__SigningKey` | Sim | 64+ caracteres aleatórios (`openssl rand -base64 48`) | Chave HMAC-SHA256 dos tokens. Mínimo 32 bytes (a API não sobe sem isso). **Nunca reutilize a de desenvolvimento**; trocar a chave invalida todas as sessões. |
| `Jwt__Issuer` | Sim | `https://redestore-api.vercel.app` | Emissor esperado nos tokens. |
| `Jwt__Audience` | Sim | `redestore-frontend` | Audiência esperada nos tokens. |
| `Jwt__ExpirationHours` | Não (padrão `8`) | inteiro | Validade do token. |
| `Resend__ApiKey` | Sim | `re_...` | Envio do e-mail de recuperação de senha. A API não sobe sem ela. |
| `Resend__FromEmail` | Sim | `nao-responda@seudominio.com.br` | Remetente. O domínio precisa estar **verificado no Resend**, senão o envio falha. |
| `Frontend__ResetPasswordUrl` | Sim | `https://redestore.vercel.app/redefinir-senha` | URL absoluta da tela de redefinição no frontend (vai no link do e-mail). |
| `Cors__AllowedOrigin` | Sim fora de Development | `https://redestore.vercel.app,https://www.seudominio.com.br` | Origens liberadas no CORS, separadas por vírgula, sem barra no final. Fora de `Development` a API não sobe sem ela. |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | Sim atrás de proxy (já definida no `Dockerfile.vercel`) | `true` | Faz a API usar o IP real do cliente (`X-Forwarded-For`), necessário para o rate limiting por IP. Só ative atrás de um proxy que sobrescreve esse cabeçalho (Vercel, Render, Railway fazem isso). |
| `RateLimiting__AuthPorMinuto` | Não (padrão `10`) | inteiro | Requisições por minuto, por IP, em `/auth/cadastro`, `/auth/login`, `/auth/recuperar-senha` e `/auth/redefinir-senha`. Acima disso: `429`. |
| `Database__MigrarNaInicializacao` | Não (padrão `true`) | `true` \| `false` | Aplica migrations pendentes ao subir. Veja "Migrations". |
| `ASPNETCORE_ENVIRONMENT` | Não (padrão `Production`) | `Production` | Em `Development` o Scalar/OpenAPI (`/scalar`, `/openapi`) fica exposto. **Nunca use `Development` em produção.** |

## Migrations

Por padrão a API aplica as migrations pendentes ao iniciar (`Database.MigrateAsync()`), com lock do
EF Core para que instâncias simultâneas não colidam. Na Vercel, cada *cold start* faz essa checagem
(uma ida ao banco). Se preferir controlar o momento das migrations:

1. Defina `Database__MigrarNaInicializacao=false` no projeto.
2. A cada deploy que trouxer migration nova, aplique-a uma vez, de preferência com a connection
   string **direta (sem pooler)** — no Neon, `DATABASE_URL_UNPOOLED`:

   ```bash
   dotnet tool install --global dotnet-ef
   dotnet ef database update \
     --project src/RedeStore.Infrastructure --startup-project src/RedeStore.Api \
     --connection "Host=...;Database=...;Username=...;Password=...;SSL Mode=Require"
   ```

Antes de migrations destrutivas, faça backup (no Neon: crie um *branch* do banco antes).

## Rodando a imagem localmente

```bash
docker build -t redestore-api:latest .
docker compose up -d db   # sobe só o Postgres

docker run --rm -p 8080:8080 \
  -e DATABASE_URL="postgresql://postgres:postgres@host.docker.internal:5432/redestore" \
  -e Jwt__SigningKey="troque-por-uma-chave-de-pelo-menos-32-caracteres" \
  -e Jwt__Issuer="RedeStore.Local" \
  -e Jwt__Audience="RedeStore.Local.Clients" \
  -e Resend__ApiKey="sua-chave-resend" \
  -e Resend__FromEmail="nao-responda@exemplo.com" \
  -e Frontend__ResetPasswordUrl="http://localhost:4200/redefinir-senha" \
  -e Cors__AllowedOrigin="http://localhost:4200" \
  redestore-api:latest
```

`GET http://localhost:8080/health` deve responder `{"status":"healthy","database":"connected"}`.

## Checklist de segurança antes de abrir para o público

- [ ] `Jwt__SigningKey` novo, aleatório, só em variável de ambiente (Production ≠ Preview).
- [ ] `ASPNETCORE_ENVIRONMENT` não é `Development` (o `/scalar` deve responder 404).
- [ ] `Cors__AllowedOrigin` contém só os domínios do frontend.
- [ ] Banco com SSL obrigatório, senha forte, e acesso só pela API (sem `postgres` padrão).
- [ ] Domínio do remetente verificado no Resend.
- [ ] Backups/point-in-time recovery do banco habilitados.
- [ ] Usuário admin criado e testado; nenhum usuário de seed/teste em produção.
