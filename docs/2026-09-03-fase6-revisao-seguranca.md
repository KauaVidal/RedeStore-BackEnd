# Fase 6 — Revisão de Segurança e Autorização

Auditoria rota-a-rota e de exposição de dados sobre o backend RedeStore, feita por leitura direta do código de mapeamento de rotas e dos serviços de aplicação (não apenas execução de testes). Nenhuma mudança arquitetural foi feita; esta é uma auditoria, com correção mínima aplicada apenas se um gap real fosse encontrado.

## 1. Auditoria rota-a-rota

| Rota | Política esperada | Verdito | Evidência |
|---|---|---|---|
| `POST /auth/cadastro` | pública | ✅ | `src/RedeStore.Api/Endpoints/AuthEndpoints.cs:15-19` — sem `.RequireAuthorization()` |
| `POST /auth/login` | pública | ✅ | `AuthEndpoints.cs:21-25` — sem `.RequireAuthorization()` |
| `GET /auth/me` | `.RequireAuthorization()` | ✅ | `AuthEndpoints.cs:27-32` |
| `PATCH /auth/perfil` | `.RequireAuthorization()`, self-only (id do JWT `sub`, nunca do corpo) | ✅ | `AuthEndpoints.cs:34-39` — `id` vem de `user.FindFirst(JwtRegisteredClaimNames.Sub)`, nunca do `request`. `AtualizarPerfilRequest` (`src/RedeStore.Application/Auth/Dtos/AtualizarPerfilRequest.cs:3`) é `record(string? Nome, string? Email, string? Telefone)` — nenhum campo `id`/`usuarioId` capaz de mirar outra conta. |
| `POST /auth/recuperar-senha` | pública, sempre `204` | ✅ | `AuthEndpoints.cs:41-45` — `Results.NoContent()` incondicional, independente do retorno de `RecuperarSenhaAsync` |
| `POST /auth/redefinir-senha` | pública | ✅ | `AuthEndpoints.cs:47-51` — sem `.RequireAuthorization()` |
| `GET /usuarios/{id}` | `.RequireAuthorization()`, self-or-admin em `AuthService` | ✅ | `src/RedeStore.Api/Endpoints/UsuariosEndpoints.cs:11-17` chama `ObterComAutorizacaoAsync`. `src/RedeStore.Application/Auth/AuthService.cs:78-85`: `if (idSolicitado != idUsuarioLogado && !ehAdmin) throw new AcessoNegadoException(...)` |
| `GET /produtos`, `/produtos/destaques`, `/produtos/{id}` | pública | ✅ | `src/RedeStore.Api/Endpoints/ProdutosEndpoints.cs:13-29` — nenhum dos três tem `.RequireAuthorization()` |
| `POST /produtos`, `PATCH /produtos/{id}`, `DELETE /produtos/{id}` | `.RequireAuthorization("Admin")` | ✅ | `ProdutosEndpoints.cs:35,41,47` |
| `GET /eventos`, `/eventos/{id}`, `/eventos/{id}/vagas-restantes` | pública | ✅ | `src/RedeStore.Api/Endpoints/EventosEndpoints.cs:13-29` |
| `POST /eventos`, `PATCH /eventos/{id}`, `DELETE /eventos/{id}` | `.RequireAuthorization("Admin")` | ✅ | `EventosEndpoints.cs:35,41,47` |
| `POST /eventos/{id}/inscricoes` | `.RequireAuthorization()` | ✅ | `src/RedeStore.Api/Endpoints/InscricoesEndpoints.cs:11-16` |
| `GET /usuarios/me/inscricoes` | `.RequireAuthorization()` | ✅ | `InscricoesEndpoints.cs:18-23` |
| `GET /eventos/{id}/inscricoes` | `.RequireAuthorization("Admin")` | ✅ | `InscricoesEndpoints.cs:25-29` |
| `PATCH /inscricoes/{id}/cancelar` | `.RequireAuthorization()`, dono-or-admin em `InscricaoService` | ✅ | `InscricoesEndpoints.cs:31-37`. `src/RedeStore.Application/Inscricoes/InscricaoService.cs:68-81`: `if (inscricao.UsuarioId != idUsuarioLogado && !ehAdmin) throw new AcessoNegadoException(...)` at lines 73-76 |
| `POST /pedidos` | `.RequireAuthorization()` | ✅ | `src/RedeStore.Api/Endpoints/PedidosEndpoints.cs:15-20` |
| `GET /usuarios/me/pedidos` | `.RequireAuthorization()` | ✅ | `PedidosEndpoints.cs:34-39` |
| `GET /pedidos` | `.RequireAuthorization("Admin")` | ✅ | `PedidosEndpoints.cs:22-26` |
| `PATCH /pedidos/{id}/avancar-status` | `.RequireAuthorization("Admin")` | ✅ | `PedidosEndpoints.cs:28-32` |
| `GET /health` | pública | ✅ | `src/RedeStore.Api/Program.cs:131-137` — sem `.RequireAuthorization()` |

19/19 rotas conferem exatamente com a tabela de Global Constraints do plano. Todos os três checks de autorização mais profunda (self-only em `/auth/perfil`, self-or-admin em `/usuarios/{id}`, dono-or-admin em `/inscricoes/{id}/cancelar`) foram re-lidos diretamente no código-fonte (não apenas re-afirmados a partir do plano) e todos lançam `AcessoNegadoException` (`src/RedeStore.Domain/Exceptions/AcessoNegadoException.cs`, mapeada para 403 pelo `GlobalExceptionHandler` via `DomainException.StatusCode`) no caso de descasamento.

## 2. Auditoria de exposição de dados

1. **`UsuarioDto` nunca inclui `SenhaHash`** — ✅. `src/RedeStore.Application/Auth/Dtos/UsuarioDto.cs:3`: `record(Guid Id, string Nome, string Email, string? Telefone, string Papel)` — nenhum campo de senha. Todos os cinco endpoints que retornam um usuário passam por este DTO, nunca pela entidade `Usuario`: `CadastrarAsync`/`LoginAsync` retornam `AuthResponse(UsuarioDto, string)` (`AuthService.cs:56,68`, DTO montado em `MapearParaDto` — `AuthService.cs:164-165`); `GET /auth/me` chama `ObterPorIdAsync` (`AuthEndpoints.cs:30`, retorna `UsuarioDto`); `PATCH /auth/perfil` chama `AtualizarPerfilAsync` (`AuthEndpoints.cs:37`, retorna `UsuarioDto` via `MapearParaDto` na linha 113); `GET /usuarios/{id}` chama `ObterComAutorizacaoAsync` → `ObterPorIdAsync` (`AuthService.cs:78-85`, `UsuarioDto`).

2. **`POST /auth/recuperar-senha` retorna `204` incondicionalmente** — ✅, com uma ressalva Menor. `AuthEndpoints.cs:41-45` chama `RecuperarSenhaAsync` e sempre devolve `Results.NoContent()`, independente do valor de retorno (o método é `Task`, sem retorno usado na branch). `AuthService.cs:116-122`: se o e-mail não existe, retorna imediatamente após uma única consulta (`BuscarPorEmailAsync`) — nenhum corpo de resposta ou status code diferente é produzido em nenhum dos dois caminhos.
   - **Achado Menor (não corrigido, fora de escopo por instrução explícita do brief):** o caminho "e-mail existe" (`AuthService.cs:124-141`) faz trabalho assíncrono significativamente maior que o caminho "e-mail não existe" — gera 32 bytes aleatórios, calcula um hash SHA-256, insere um `PasswordResetToken` no banco (`_passwordResetTokenRepository.AdicionarAsync`) e faz uma chamada HTTP de saída para a API do Resend (`_emailSender.EnviarAsync`) — enquanto o caminho "não existe" faz apenas a consulta inicial. Isso é um canal lateral de timing teoricamente observável (um atacante medindo a latência da resposta poderia inferir se um e-mail está cadastrado). Documentado aqui apenas; construir um caminho de tempo constante está fora do escopo desta tarefa por instrução explícita do brief.

3. **`PasswordResetToken.TokenHash` — token bruto nunca persistido/logado em texto plano** — ✅. `AuthService.cs:124-126`: o token bruto (`tokenBruto`) é gerado e imediatamente hasheado com `SHA256.HashData` antes de qualquer persistência; apenas `tokenHash` é gravado no `PasswordResetToken.TokenHash` (linha 132). O token bruto só é usado para montar a URL do e-mail (linha 136) e é passado ao `IEmailSender.EnviarAsync` (linha 137-141) — nunca a um logger. Uma busca por chamadas de logger em `AuthService.cs` não encontrou nenhuma (`AuthService` não injeta `ILogger`).

4. **`GlobalExceptionHandler` nunca vaza mensagem de exceção crua ou stack trace para falhas não-`DomainException`** — ✅. `src/RedeStore.Api/Middleware/GlobalExceptionHandler.cs:15-19`: o `switch` expression mapeia qualquer exceção que não seja `DomainException` para a tupla fixa `(StatusCodes.Status500InternalServerError, "ERRO_INTERNO", "Ocorreu um erro inesperado.")` — nunca `exception.Message` ou `exception.ToString()` nesse ramo. Linhas 21-24: o detalhe completo da exceção só vai para `logger.LogError(exception, ...)` (lado servidor); o `ProblemDetails` escrito na resposta (linhas 26-31) usa apenas a `mensagem` fixa da tupla.

5. **CORS restrito a uma única origem configurável, sem wildcard** — ✅. `src/RedeStore.Api/Program.cs:106-114`: `policy.WithOrigins(builder.Configuration["Cors:AllowedOrigin"] ?? "http://localhost:4200")` — nenhum `AllowAnyOrigin()` em nenhum lugar do arquivo.

6. **Validação de JWT cobre issuer, audience, lifetime e chave de assinatura** — ✅. `src/RedeStore.Infrastructure/Auth/JwtTokenValidationParametersFactory.cs:9-20`: `ValidateIssuer = true`, `ValidateAudience = true`, `ValidateIssuerSigningKey = true`, `ValidateLifetime = true`, `ClockSkew = TimeSpan.FromMinutes(1)`. Um token expirado ou com issuer/audience/assinatura incorretos é genuinamente rejeitado, não apenas verificado por assinatura.
   - Cobertura de teste: nenhum teste pré-existente cobria token expirado antes desta fase. `tests/RedeStore.IntegrationTests/Auth/AuthEndpointsSmokeTests.cs:53-74` (`GetMe_ComTokenExpirado_Retorna401`) foi encontrado já presente no working tree, não commitado, de uma sessão anterior interrompida. Ele foi verificado linha a linha nesta auditoria: usa exatamente a mesma `SigningKey`/`Issuer`/`Audience` configurados pelo `ApiFactory` para o ambiente de testes (`tests/RedeStore.IntegrationTests/ApiFactory.cs:28-30` vs. `AuthEndpointsSmokeTests.cs:57-63` — strings idênticas), constrói um `JwtSecurityToken` real com `expires: DateTime.UtcNow.AddHours(-1)` (bem além do `ClockSkew` de 1 minuto) e assinado com `HmacSha256`, e o envia como `Bearer` real contra `GET /auth/me` através do pipeline de middleware real (não um stub). O teste foi confirmado correto, mantido como está, e incorporado ao commit desta tarefa (ver seção "Achados" abaixo).

## 3. Achados

Nenhum gap de autorização ou de exposição de dados foi encontrado no código de produção — nenhuma rota precisou de correção e nenhuma das seis checagens de exposição de dados revelou comportamento incorreto. **19 rotas e as 6 checagens de exposição de dados foram verificadas, nenhum gap de produção encontrado.**

O único item de ação foi de **cobertura de teste**, não de comportamento: o item 2.6 pedia um teste de token expirado, e um teste (`GetMe_ComTokenExpirado_Retorna401`) já existia sem commit no working tree, provavelmente de uma tentativa anterior interrompida desta mesma tarefa. Ele foi auditado (chave/issuer/audience batem com o `ApiFactory`; exercita validação real de JWT, não um fluke) e considerado correto e completo. Ele foi mantido sem alterações e commitado nesta tarefa em `6e8cc97` — "Add integration test proving expired JWT is rejected with 401" — comprovando que a validação de lifetime do JWT (`ValidateLifetime = true`, `JwtTokenValidationParametersFactory.cs:17`) realmente rejeita tokens expirados com `401`.

Um achado Menor (não uma falha, documentado por instrução explícita do brief) foi registrado no item 2 acima: uma diferença de timing teoricamente observável entre os caminhos "e-mail existe" e "e-mail não existe" em `POST /auth/recuperar-senha`. Não corrigido — fora de escopo desta tarefa.
