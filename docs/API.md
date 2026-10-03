# RedeStore API — Referência completa

Documentação de todos os endpoints da API RedeStore: o que cada um faz, como chamar,
o que enviar e o que volta. A especificação OpenAPI equivalente (máquina-legível) está em
[`docs/openapi.json`](openapi.json).

## Sumário

- [Visão geral](#visão-geral)
- [Convenções](#convenções)
  - [Autenticação (JWT)](#autenticação-jwt)
  - [Papéis e permissões](#papéis-e-permissões)
  - [Formato de erros](#formato-de-erros)
  - [Tipos e valores fixos](#tipos-e-valores-fixos)
- [Health](#health)
- [Auth](#auth)
- [Usuários](#usuários)
- [Produtos](#produtos)
- [Eventos](#eventos)
- [Inscrições](#inscrições)
- [Pedidos](#pedidos)
- [Schemas de resposta](#schemas-de-resposta)
- [Fluxos típicos](#fluxos-típicos)
- [OpenAPI e Scalar](#openapi-e-scalar)

## Visão geral

| Item | Valor |
|---|---|
| Stack | ASP.NET Core 10 (Minimal APIs) + EF Core + PostgreSQL |
| URL local | `http://localhost:5052` (perfil `http`) / `https://localhost:7182` (perfil `https`) |
| URL no container | porta `8080` |
| Formato | JSON (`Content-Type: application/json`), propriedades em **camelCase** |
| Autenticação | `Authorization: Bearer <token>` |
| CORS | Origem única liberada via `Cors:AllowedOrigin` (padrão `http://localhost:4200`) |

A API é organizada em camadas:

```
RedeStore.Api            → endpoints (Minimal APIs), filtro de validação, handler global de erros
RedeStore.Application    → serviços (regras de negócio), DTOs e validadores (FluentValidation)
RedeStore.Domain         → entidades, enums e exceções de domínio (cada uma com código + status HTTP)
RedeStore.Infrastructure → EF Core/PostgreSQL, repositórios, JWT, hash de senha, e-mail (Resend)
```

Fluxo de uma requisição: **endpoint** → (autorização) → **ValidationFilter** (valida o corpo com
FluentValidation; se inválido devolve 400) → **serviço** → repositório. Exceções de domínio são
convertidas em respostas de erro pelo `GlobalExceptionHandler`.

### Mapa de endpoints

| Método | Rota | Acesso | Descrição |
|---|---|---|---|
| GET | `/health` | Público | Status da API e do banco |
| POST | `/auth/cadastro` | Público | Cria conta e devolve token |
| POST | `/auth/login` | Público | Login, devolve token |
| GET | `/auth/me` | Autenticado | Dados do usuário logado |
| PATCH | `/auth/perfil` | Autenticado | Atualiza nome/e-mail/telefone |
| POST | `/auth/recuperar-senha` | Público | Envia e-mail com link de redefinição |
| POST | `/auth/redefinir-senha` | Público | Troca a senha usando o token do e-mail |
| GET | `/usuarios/{id}` | Autenticado (dono ou admin) | Consulta usuário por id |
| GET | `/produtos` | Público | Lista produtos (filtros: `categoria`, `busca`) |
| GET | `/produtos/destaques` | Público | Lista produtos em destaque |
| GET | `/produtos/{id}` | Público | Detalhe do produto |
| POST | `/produtos` | Admin | Cria produto |
| PATCH | `/produtos/{id}` | Admin | Atualiza produto |
| DELETE | `/produtos/{id}` | Admin | Remove produto |
| GET | `/eventos` | Público | Lista eventos (filtro: `apenasFuturos`) |
| GET | `/eventos/{id}` | Público | Detalhe do evento |
| GET | `/eventos/{id}/vagas-restantes` | Público | Vagas restantes |
| POST | `/eventos` | Admin | Cria evento |
| PATCH | `/eventos/{id}` | Admin | Atualiza evento |
| DELETE | `/eventos/{id}` | Admin | Remove evento |
| POST | `/eventos/{eventoId}/inscricoes` | Autenticado | Inscreve o usuário logado no evento |
| GET | `/eventos/{eventoId}/inscricoes` | Admin | Inscrições de um evento |
| GET | `/usuarios/me/inscricoes` | Autenticado | Minhas inscrições |
| PATCH | `/inscricoes/{id}/cancelar` | Autenticado (dono ou admin) | Cancela inscrição |
| POST | `/pedidos` | Autenticado | Cria pedido (checkout) |
| GET | `/pedidos` | Admin | Todos os pedidos |
| PATCH | `/pedidos/{id}/avancar-status` | Admin | Avança status do pedido |
| GET | `/usuarios/me/pedidos` | Autenticado | Meus pedidos |

## Convenções

### Autenticação (JWT)

1. Obtenha um token em `POST /auth/login` ou `POST /auth/cadastro` (campo `token` da resposta).
2. Envie-o em toda rota protegida:

```http
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

- Algoritmo: HMAC-SHA256. Validade: `Jwt:ExpirationHours` (padrão **8 horas**). Não há refresh token:
  quando expirar, faça login de novo.
- Claims: `sub` (id do usuário), `email` e a claim de papel (`jovem` ou `admin`).
- O papel fica **gravado no token**: se o papel de um usuário mudar no banco, ele precisa fazer login
  novamente para o token refletir isso.
- Token ausente, inválido ou expirado → `401` (sem corpo).

### Papéis e permissões

| Papel | Como obter | O que pode |
|---|---|---|
| `jovem` | Padrão de todo cadastro | Rotas públicas + rotas "Autenticado" sobre os **próprios** dados |
| `admin` | Alterado direto no banco (não há endpoint para promover) | Tudo, incluindo rotas "Admin" e dados de outros usuários |

- Rota "Admin" chamada por um `jovem` → `403` (sem corpo).
- Rota "dono ou admin" (`/usuarios/{id}`, `/inscricoes/{id}/cancelar`) acessando recurso de outro
  usuário → `403` com corpo `ACESSO_NEGADO`.

### Formato de erros

Todos os erros de negócio usam **ProblemDetails** (`Content-Type: application/problem+json`):

```json
{
  "status": 404,
  "title": "EVENTO_NAO_ENCONTRADO",
  "detail": "Evento '3fa85f64-5717-4562-b3fc-2c963f66afa6' não encontrado.",
  "traceId": "0HN6..."
}
```

- `title` é o **código estável** do erro — use-o no frontend para decidir o que mostrar.
- `detail` é uma mensagem legível (pode mudar; não compare strings).
- `traceId` ajuda a localizar a requisição nos logs.

**Erros de validação (400)** vêm no formato `ValidationProblem`, com os campos inválidos em `errors`
(chaves no nome da propriedade do C#, em PascalCase; itens de lista com índice). As mensagens são as
padrão do FluentValidation, no idioma da cultura do servidor — use as **chaves** para associar o erro
ao campo do formulário:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Email": ["'Email' é um endereço de email inválido."],
    "Itens[0].Quantidade": ["'Quantidade' deve ser superior a '0'."]
  }
}
```

**Tabela completa de códigos de erro:**

| Código (`title`) | HTTP | Quando acontece |
|---|---|---|
| `CREDENCIAIS_INVALIDAS` | 401 | Login com e-mail inexistente ou senha errada |
| `ACESSO_NEGADO` | 403 | Acessar usuário/inscrição de outra pessoa sem ser admin |
| `TOKEN_INVALIDO` | 400 | Token de redefinição de senha inválido, expirado ou já usado |
| `PRODUTO_NAO_ENCONTRADO` | 404 | Produto não existe (detalhe, edição, remoção, item de pedido) |
| `VARIACAO_NAO_ENCONTRADA` | 404 | Item de pedido com tamanho/cor que o produto não tem |
| `EVENTO_NAO_ENCONTRADO` | 404 | Evento não existe |
| `INSCRICAO_NAO_ENCONTRADA` | 404 | Inscrição não existe |
| `PEDIDO_NAO_ENCONTRADO` | 404 | Pedido não existe |
| `EMAIL_EM_USO` | 409 | Cadastro ou troca de e-mail para um e-mail já cadastrado |
| `ESTOQUE_INSUFICIENTE` | 409 | Pedido pede mais unidades do que há em estoque |
| `EVENTO_COM_INSCRICOES_CONFIRMADAS` | 409 | Remover evento que tem inscrições confirmadas |
| `EVENTO_VAGAS_TOTAIS_INSUFICIENTES` | 409 | Reduzir `vagasTotais` abaixo das inscrições confirmadas |
| `PEDIDO_EM_ESTADO_FINAL` | 409 | Avançar status de pedido já `retirado`/`entregue` |
| `ERRO_INTERNO` | 500 | Erro inesperado (detalhes só nos logs do servidor) |

Outros status sem corpo de domínio:

- `401` sem corpo: token ausente/inválido/expirado.
- `403` sem corpo: rota exige papel `admin`.
- `404` sem corpo: rota inexistente ou `{id}` que não é um GUID válido (as rotas usam a restrição `:guid`).

### Tipos e valores fixos

| Campo | Tipo | Formato / valores |
|---|---|---|
| ids (`id`, `produtoId`, `eventoId`...) | string | GUID, ex.: `3fa85f64-5717-4562-b3fc-2c963f66afa6` |
| datas (`dataHora`, `criadoEm`) | string | ISO 8601. `criadoEm` é sempre UTC (`...Z`). Envie `dataHora` em UTC (`2026-12-20T19:00:00Z`) — o filtro `apenasFuturos` compara com o horário UTC atual |
| valores monetários (`preco`, `valorTotal`...) | number | decimal, ex.: `79.90` |
| `papel` | string | `jovem` \| `admin` |
| `categoria` (produto) | string | `camisetas` \| `camisas` \| `polos` \| `regatas` \| `moletons` \| `jaquetas` \| `calcas` \| `bermudas` \| `saias` \| `vestidos` \| `calcados` \| `acessorios` |
| `formaEntrega` | string | `retirada` \| `entrega` |
| `status` (pedido) | string | `pago` \| `em_preparo` \| `retirado` \| `entregue` |
| `status` (inscrição) | string | `confirmada` \| `cancelada` |
| `resultado` (inscrição) | string | `criada` \| `ja_inscrito` \| `esgotado` |

Os valores de enum são **case-sensitive** (use exatamente em minúsculas).

---

## Health

### `GET /health`

Verifica se a API está no ar e consegue conectar no PostgreSQL. Útil para health check de deploy.

**Acesso:** público

```bash
curl http://localhost:5052/health
```

**200 OK**

```json
{ "status": "healthy", "database": "connected" }
```

**503 Service Unavailable** — banco inacessível (ProblemDetails com `detail: "Não foi possível conectar ao banco de dados."`).

---

## Auth

### `POST /auth/cadastro`

Cria uma conta nova com papel `jovem` e já devolve o token (o usuário sai logado).

**Acesso:** público

**Body**

| Campo | Tipo | Obrigatório | Regras |
|---|---|---|---|
| `nome` | string | sim | mínimo 2 caracteres |
| `email` | string | sim | e-mail válido, único (sem diferenciar maiúsculas/minúsculas) |
| `senha` | string | sim | mínimo 8 caracteres |

O e-mail é gravado sem espaços nas pontas e em minúsculas: `" Maria@Exemplo.com"` vira `maria@exemplo.com`
e conflita com uma conta já existente em `maria@exemplo.com`. Login, recuperação de senha e troca de e-mail
no perfil seguem a mesma regra. Nomes iguais são permitidos (homônimos); o que identifica a conta é o e-mail.

```bash
curl -X POST http://localhost:5052/auth/cadastro \
  -H "Content-Type: application/json" \
  -d '{ "nome": "Maria Silva", "email": "maria@exemplo.com", "senha": "senhaSegura123" }'
```

**200 OK** — [`AuthResponse`](#authresponse)

```json
{
  "usuario": {
    "id": "b1d7c0a2-5f3e-4c1a-9d2b-7e8f9a0b1c2d",
    "nome": "Maria Silva",
    "email": "maria@exemplo.com",
    "telefone": null,
    "papel": "jovem"
  },
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

**Erros:** `400` validação · `409 EMAIL_EM_USO`

### `POST /auth/login`

Autentica com e-mail e senha.

**Acesso:** público

**Body**

| Campo | Tipo | Obrigatório | Regras |
|---|---|---|---|
| `email` | string | sim | e-mail válido |
| `senha` | string | sim | não vazio |

```bash
curl -X POST http://localhost:5052/auth/login \
  -H "Content-Type: application/json" \
  -d '{ "email": "maria@exemplo.com", "senha": "senhaSegura123" }'
```

**200 OK** — [`AuthResponse`](#authresponse) (mesmo formato do cadastro).

**Erros:** `400` validação · `401 CREDENCIAIS_INVALIDAS` (mesma resposta para e-mail inexistente e senha errada, de propósito)

### `GET /auth/me`

Retorna os dados do usuário dono do token.

**Acesso:** autenticado

```bash
curl http://localhost:5052/auth/me -H "Authorization: Bearer $TOKEN"
```

**200 OK** — [`UsuarioDto`](#usuariodto)

```json
{
  "id": "b1d7c0a2-5f3e-4c1a-9d2b-7e8f9a0b1c2d",
  "nome": "Maria Silva",
  "email": "maria@exemplo.com",
  "telefone": "(11) 99999-0000",
  "papel": "jovem"
}
```

**Erros:** `401`

### `PATCH /auth/perfil`

Atualiza parcialmente o perfil do usuário logado. **Todos os campos são opcionais**: só os enviados
(e não `null`) são alterados. Não há como "limpar" um campo enviando `null`.

**Acesso:** autenticado

**Body**

| Campo | Tipo | Obrigatório | Regras |
|---|---|---|---|
| `nome` | string | não | mínimo 2 caracteres |
| `email` | string | não | e-mail válido, não pode estar em uso por outra conta |
| `telefone` | string | não | livre (sem validação de formato) |

```bash
curl -X PATCH http://localhost:5052/auth/perfil \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{ "telefone": "(11) 99999-0000" }'
```

**200 OK** — [`UsuarioDto`](#usuariodto) já atualizado.

> Trocar o e-mail **não** invalida o token atual (a claim `email` dele fica com o valor antigo até o
> próximo login; a API usa apenas o `sub` para identificar o usuário).

**Erros:** `400` validação · `401` · `409 EMAIL_EM_USO`

### `POST /auth/recuperar-senha`

Inicia a recuperação de senha: gera um token de uso único (válido por **1 hora**) e envia por e-mail
(via Resend) um link `{Frontend:ResetPasswordUrl}?token=<token>`.

**Acesso:** público

**Body**

| Campo | Tipo | Obrigatório | Regras |
|---|---|---|---|
| `email` | string | sim | e-mail válido |

```bash
curl -X POST http://localhost:5052/auth/recuperar-senha \
  -H "Content-Type: application/json" \
  -d '{ "email": "maria@exemplo.com" }'
```

**204 No Content** — sempre, **exista ou não** o e-mail (para não revelar quais e-mails estão
cadastrados).

**Erros:** `400` validação

### `POST /auth/redefinir-senha`

Define uma nova senha usando o token recebido no link do e-mail. O frontend deve ler o `token` da
query string da página de redefinição e enviá-lo aqui.

**Acesso:** público

**Body**

| Campo | Tipo | Obrigatório | Regras |
|---|---|---|---|
| `token` | string | sim | o valor do `?token=` do link |
| `novaSenha` | string | sim | mínimo 8 caracteres |

```bash
curl -X POST http://localhost:5052/auth/redefinir-senha \
  -H "Content-Type: application/json" \
  -d '{ "token": "Xk3...q9", "novaSenha": "novaSenhaSegura456" }'
```

**204 No Content** — senha trocada; o token é marcado como usado.

**Erros:** `400` validação · `400 TOKEN_INVALIDO` (inexistente, expirado ou já usado)

> Tokens JWT emitidos antes da troca de senha continuam válidos até expirarem.

---

## Usuários

### `GET /usuarios/{id}`

Retorna um usuário pelo id. Um usuário comum só pode consultar **a si mesmo**; admin consulta qualquer um.

**Acesso:** autenticado (dono ou admin)

| Parâmetro | Onde | Tipo | Descrição |
|---|---|---|---|
| `id` | path | GUID | id do usuário |

```bash
curl http://localhost:5052/usuarios/b1d7c0a2-5f3e-4c1a-9d2b-7e8f9a0b1c2d \
  -H "Authorization: Bearer $TOKEN"
```

**200 OK** — [`UsuarioDto`](#usuariodto)

**Erros:** `401` · `403 ACESSO_NEGADO`

---

## Produtos

Cada produto tem uma lista de **variações** (combinação tamanho + cor) e cada variação tem seu
próprio **estoque**. Os campos `tamanhos` e `cores` da resposta são calculados a partir das variações
(valores distintos) — não são enviados na criação.

### `GET /produtos`

Lista produtos, com filtros opcionais combináveis.

**Acesso:** público

| Parâmetro | Onde | Tipo | Descrição |
|---|---|---|---|
| `categoria` | query | string | `camisetas` \| `camisas` \| `polos` \| `regatas` \| `moletons` \| `jaquetas` \| `calcas` \| `bermudas` \| `saias` \| `vestidos` \| `calcados` \| `acessorios`. Valor desconhecido é **ignorado** (não filtra) |
| `busca` | query | string | trecho do nome do produto, sem diferenciar maiúsculas/minúsculas |

```bash
curl "http://localhost:5052/produtos?categoria=camisetas&busca=rede"
```

**200 OK** — lista de [`ProdutoDto`](#produtodto) (pode ser `[]`). Sem paginação e sem ordenação garantida.

### `GET /produtos/destaques`

Lista os produtos com `destaque = true` (vitrine da home).

**Acesso:** público

```bash
curl http://localhost:5052/produtos/destaques
```

**200 OK** — lista de [`ProdutoDto`](#produtodto).

### `GET /produtos/{id}`

**Acesso:** público

```bash
curl http://localhost:5052/produtos/7c9e6679-7425-40de-944b-e07fc1f90ae7
```

**200 OK** — [`ProdutoDto`](#produtodto)

```json
{
  "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "nome": "Camiseta Rede Oversized",
  "categoria": "camisetas",
  "preco": 79.90,
  "descricao": "Camiseta 100% algodão.",
  "fotos": ["https://cdn.exemplo.com/camiseta-1.jpg"],
  "tamanhos": ["P", "M"],
  "cores": ["preto", "branco"],
  "destaque": true,
  "variacoes": [
    { "id": "0b5c...", "tamanho": "P", "cor": "preto", "estoque": 10 },
    { "id": "1c6d...", "tamanho": "M", "cor": "preto", "estoque": 4 },
    { "id": "2d7e...", "tamanho": "M", "cor": "branco", "estoque": 0 }
  ]
}
```

**Erros:** `404 PRODUTO_NAO_ENCONTRADO`

### `POST /produtos`

Cria um produto.

**Acesso:** admin

**Body**

| Campo | Tipo | Obrigatório | Regras |
|---|---|---|---|
| `nome` | string | sim | não vazio |
| `categoria` | string | sim | `camisetas` \| `camisas` \| `polos` \| `regatas` \| `moletons` \| `jaquetas` \| `calcas` \| `bermudas` \| `saias` \| `vestidos` \| `calcados` \| `acessorios` |
| `preco` | number | sim | `> 0` |
| `descricao` | string | sim | não vazio |
| `fotos` | string[] | não | URLs das fotos; ausente/`null` vira `[]`. A primeira é usada como foto do item no pedido |
| `destaque` | boolean | não | padrão `false` |
| `variacoes` | [`VariacaoRequest`](#variacaorequest)[] | sim | ao menos 1 item |

<a id="variacaorequest"></a>**VariacaoRequest**

| Campo | Tipo | Obrigatório | Regras |
|---|---|---|---|
| `tamanho` | string | sim | não vazio (ex.: `P`, `M`, `G`, `Único`) |
| `cor` | string | sim | não vazio |
| `estoque` | integer | sim | `>= 0` |

```bash
curl -X POST http://localhost:5052/produtos \
  -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" \
  -d '{
    "nome": "Camiseta Rede Oversized",
    "categoria": "camisetas",
    "preco": 79.90,
    "descricao": "Camiseta 100% algodão.",
    "fotos": ["https://cdn.exemplo.com/camiseta-1.jpg"],
    "destaque": true,
    "variacoes": [
      { "tamanho": "P", "cor": "preto", "estoque": 10 },
      { "tamanho": "M", "cor": "preto", "estoque": 4 }
    ]
  }'
```

**200 OK** — [`ProdutoDto`](#produtodto) criado (com `id` e ids das variações gerados).

**Erros:** `400` validação · `401` · `403`

### `PATCH /produtos/{id}`

Atualiza parcialmente um produto. Só os campos enviados (não `null`) mudam.

**Acesso:** admin

**Body** — todos opcionais, mesmas regras do `POST` quando enviados:
`nome`, `categoria`, `preco`, `descricao`, `fotos`, `destaque`, `variacoes`.

> **Atenção:** enviar `variacoes` **substitui todas** as variações do produto (as antigas são
> removidas e novas são criadas com **novos ids**). Para alterar só o estoque de uma variação,
> envie a lista completa com o valor atualizado. `fotos`, se enviado, também substitui a lista inteira.

```bash
curl -X PATCH http://localhost:5052/produtos/7c9e6679-7425-40de-944b-e07fc1f90ae7 \
  -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" \
  -d '{ "preco": 69.90, "destaque": false }'
```

**200 OK** — [`ProdutoDto`](#produtodto) atualizado.

**Erros:** `400` validação · `401` · `403` · `404 PRODUTO_NAO_ENCONTRADO`

### `DELETE /produtos/{id}`

Remove o produto (e suas variações). Pedidos antigos não são afetados: eles guardam uma cópia
(snapshot) de nome, preço e foto do produto.

**Acesso:** admin

```bash
curl -X DELETE http://localhost:5052/produtos/7c9e6679-7425-40de-944b-e07fc1f90ae7 \
  -H "Authorization: Bearer $TOKEN_ADMIN"
```

**204 No Content**

**Erros:** `401` · `403` · `404 PRODUTO_NAO_ENCONTRADO`

---

## Eventos

### `GET /eventos`

Lista eventos.

**Acesso:** público

| Parâmetro | Onde | Tipo | Descrição |
|---|---|---|---|
| `apenasFuturos` | query | boolean | `true` → só eventos com `dataHora >= agora (UTC)`. Padrão `false` (todos) |

```bash
curl "http://localhost:5052/eventos?apenasFuturos=true"
```

**200 OK** — lista de [`EventoDto`](#eventodto). Sem paginação e sem ordenação garantida.

### `GET /eventos/{id}`

**Acesso:** público

```bash
curl http://localhost:5052/eventos/9b2f1e3a-1c4d-4e5f-8a9b-0c1d2e3f4a5b
```

**200 OK** — [`EventoDto`](#eventodto)

```json
{
  "id": "9b2f1e3a-1c4d-4e5f-8a9b-0c1d2e3f4a5b",
  "titulo": "Acampamento de Verão",
  "descricao": "Três dias de programação.",
  "dataHora": "2026-12-20T19:00:00Z",
  "local": "Sítio Esperança",
  "preco": 150.00,
  "vagasTotais": 100,
  "vagasRestantes": 37,
  "foto": "https://cdn.exemplo.com/acampamento.jpg"
}
```

**Erros:** `404 EVENTO_NAO_ENCONTRADO`

### `GET /eventos/{id}/vagas-restantes`

Retorna só o número de vagas restantes (útil para atualizar o contador sem recarregar o evento todo).
`vagasRestantes = vagasTotais − inscrições com status confirmada`.

**Acesso:** público

```bash
curl http://localhost:5052/eventos/9b2f1e3a-1c4d-4e5f-8a9b-0c1d2e3f4a5b/vagas-restantes
```

**200 OK**

```json
{ "vagasRestantes": 37 }
```

**Erros:** `404 EVENTO_NAO_ENCONTRADO`

### `POST /eventos`

Cria um evento.

**Acesso:** admin

**Body**

| Campo | Tipo | Obrigatório | Regras |
|---|---|---|---|
| `titulo` | string | sim | não vazio |
| `descricao` | string | sim | não vazio |
| `dataHora` | string (ISO 8601) | sim | data/hora do evento (envie em UTC) |
| `local` | string | sim | não vazio |
| `preco` | number | sim | `>= 0` (0 = gratuito) |
| `vagasTotais` | integer | sim | `>= 1` |
| `foto` | string | sim | URL da imagem, não vazio |

```bash
curl -X POST http://localhost:5052/eventos \
  -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" \
  -d '{
    "titulo": "Acampamento de Verão",
    "descricao": "Três dias de programação.",
    "dataHora": "2026-12-20T19:00:00Z",
    "local": "Sítio Esperança",
    "preco": 150.00,
    "vagasTotais": 100,
    "foto": "https://cdn.exemplo.com/acampamento.jpg"
  }'
```

**200 OK** — [`EventoDto`](#eventodto) criado (`vagasRestantes` = `vagasTotais`).

**Erros:** `400` validação · `401` · `403`

### `PATCH /eventos/{id}`

Atualiza parcialmente um evento. Só os campos enviados (não `null`) mudam; mesmas regras do `POST`.

**Acesso:** admin

**Body** — todos opcionais: `titulo`, `descricao`, `dataHora`, `local`, `preco`, `vagasTotais`, `foto`.

- `vagasTotais` não pode ficar menor que o número de inscrições **confirmadas** →
  `409 EVENTO_VAGAS_TOTAIS_INSUFICIENTES`.
- Mudar `preco` não altera o `valorPago` de inscrições já feitas.

```bash
curl -X PATCH http://localhost:5052/eventos/9b2f1e3a-1c4d-4e5f-8a9b-0c1d2e3f4a5b \
  -H "Authorization: Bearer $TOKEN_ADMIN" -H "Content-Type: application/json" \
  -d '{ "vagasTotais": 120 }'
```

**200 OK** — [`EventoDto`](#eventodto) atualizado.

**Erros:** `400` validação · `401` · `403` · `404 EVENTO_NAO_ENCONTRADO` · `409 EVENTO_VAGAS_TOTAIS_INSUFICIENTES`

### `DELETE /eventos/{id}`

Remove um evento. Só é permitido se **não houver inscrições confirmadas** (cancele-as antes).

**Acesso:** admin

```bash
curl -X DELETE http://localhost:5052/eventos/9b2f1e3a-1c4d-4e5f-8a9b-0c1d2e3f4a5b \
  -H "Authorization: Bearer $TOKEN_ADMIN"
```

**204 No Content**

**Erros:** `401` · `403` · `404 EVENTO_NAO_ENCONTRADO` · `409 EVENTO_COM_INSCRICOES_CONFIRMADAS`

---

## Inscrições

### `POST /eventos/{eventoId}/inscricoes`

Inscreve o usuário logado no evento. **Não tem corpo.** A operação é segura contra concorrência
(trava o evento durante a contagem de vagas, então não há overbooking) e é **idempotente**: chamar de
novo não cria uma segunda inscrição.

**Acesso:** autenticado

```bash
curl -X POST http://localhost:5052/eventos/9b2f1e3a-1c4d-4e5f-8a9b-0c1d2e3f4a5b/inscricoes \
  -H "Authorization: Bearer $TOKEN"
```

**200 OK** — [`ResultadoInscricaoDto`](#resultadoinscricaodto). Sempre `200` quando o evento existe;
o desfecho vem no campo `resultado`:

| `resultado` | Significado | `inscricao` |
|---|---|---|
| `criada` | Inscrição nova, status `confirmada` | a inscrição criada |
| `ja_inscrito` | O usuário já tinha inscrição confirmada | a inscrição existente |
| `esgotado` | Não há vagas | `null` |

```json
{
  "resultado": "criada",
  "inscricao": {
    "id": "e4f5a6b7-c8d9-4e0f-a1b2-c3d4e5f6a7b8",
    "eventoId": "9b2f1e3a-1c4d-4e5f-8a9b-0c1d2e3f4a5b",
    "usuarioId": "b1d7c0a2-5f3e-4c1a-9d2b-7e8f9a0b1c2d",
    "status": "confirmada",
    "valorPago": 150.00,
    "criadoEm": "2026-10-01T14:32:10.123Z"
  }
}
```

```json
{ "resultado": "esgotado", "inscricao": null }
```

- `valorPago` é o preço do evento **no momento** da inscrição. Não há integração de pagamento.
- Um usuário que cancelou pode se inscrever de novo (gera uma nova inscrição).

**Erros:** `401` · `404 EVENTO_NAO_ENCONTRADO`

### `GET /usuarios/me/inscricoes`

Lista as inscrições do usuário logado (confirmadas e canceladas), mais recentes primeiro.

**Acesso:** autenticado

```bash
curl http://localhost:5052/usuarios/me/inscricoes -H "Authorization: Bearer $TOKEN"
```

**200 OK** — lista de [`InscricaoDto`](#inscricaodto). Para exibir dados do evento, use o `eventoId`
com `GET /eventos/{id}`.

**Erros:** `401`

### `GET /eventos/{eventoId}/inscricoes`

Lista todas as inscrições de um evento (confirmadas e canceladas), mais recentes primeiro.

**Acesso:** admin

```bash
curl http://localhost:5052/eventos/9b2f1e3a-1c4d-4e5f-8a9b-0c1d2e3f4a5b/inscricoes \
  -H "Authorization: Bearer $TOKEN_ADMIN"
```

**200 OK** — lista de [`InscricaoDto`](#inscricaodto). Evento inexistente devolve `[]` (não 404).

**Erros:** `401` · `403`

### `PATCH /inscricoes/{id}/cancelar`

Cancela uma inscrição (status vira `cancelada` e a vaga é liberada). **Não tem corpo.**
Só o dono da inscrição ou um admin podem cancelar. Cancelar uma inscrição já cancelada apenas devolve
ela de novo. Não há estorno automático.

**Acesso:** autenticado (dono ou admin)

```bash
curl -X PATCH http://localhost:5052/inscricoes/e4f5a6b7-c8d9-4e0f-a1b2-c3d4e5f6a7b8/cancelar \
  -H "Authorization: Bearer $TOKEN"
```

**200 OK** — [`InscricaoDto`](#inscricaodto) com `status: "cancelada"`.

**Erros:** `401` · `403 ACESSO_NEGADO` · `404 INSCRICAO_NAO_ENCONTRADA`

---

## Pedidos

### `POST /pedidos`

Faz o checkout do carrinho do usuário logado. Em uma única transação: valida produtos e variações,
**debita o estoque** de cada variação (de forma atômica — se faltar estoque em qualquer item, nada é
debitado) e grava o pedido com status `pago`. Não há integração com meio de pagamento.

O preço é sempre o **do servidor** (o cliente não envia preço), e o pedido guarda um snapshot de nome,
preço e primeira foto de cada produto.

**Acesso:** autenticado

**Body**

| Campo | Tipo | Obrigatório | Regras |
|---|---|---|---|
| `itens` | [`ItemPedidoRequest`](#itempedidorequest)[] | sim | ao menos 1 item |
| `formaEntrega` | string | sim | `retirada` \| `entrega` |
| `endereco` | [`EnderecoDto`](#enderecodto) | condicional | **obrigatório** quando `formaEntrega = "entrega"`; ignorado em `retirada` |

<a id="itempedidorequest"></a>**ItemPedidoRequest**

| Campo | Tipo | Obrigatório | Regras |
|---|---|---|---|
| `produtoId` | GUID | sim | produto existente |
| `tamanho` | string | sim | deve existir variação com esse tamanho **e** cor (comparação exata) |
| `cor` | string | sim | idem |
| `quantidade` | integer | sim | `> 0` |

Endereço (quando enviado): `rua`, `numero`, `bairro`, `cidade`, `cep` obrigatórios; `complemento` opcional.

```bash
curl -X POST http://localhost:5052/pedidos \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{
    "itens": [
      { "produtoId": "7c9e6679-7425-40de-944b-e07fc1f90ae7", "tamanho": "M", "cor": "preto", "quantidade": 2 }
    ],
    "formaEntrega": "entrega",
    "endereco": {
      "rua": "Rua das Flores",
      "numero": "123",
      "complemento": "Apto 4",
      "bairro": "Centro",
      "cidade": "São Paulo",
      "cep": "01000-000"
    }
  }'
```

**200 OK** — [`PedidoDto`](#pedidodto)

```json
{
  "id": "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d",
  "usuarioId": "b1d7c0a2-5f3e-4c1a-9d2b-7e8f9a0b1c2d",
  "itens": [
    {
      "produtoId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "nome": "Camiseta Rede Oversized",
      "precoUnitario": 79.90,
      "fotoUrl": "https://cdn.exemplo.com/camiseta-1.jpg",
      "tamanho": "M",
      "cor": "preto",
      "quantidade": 2
    }
  ],
  "formaEntrega": "entrega",
  "endereco": {
    "rua": "Rua das Flores",
    "numero": "123",
    "complemento": "Apto 4",
    "bairro": "Centro",
    "cidade": "São Paulo",
    "cep": "01000-000"
  },
  "valorTotal": 159.80,
  "status": "pago",
  "criadoEm": "2026-10-01T14:40:00.000Z"
}
```

**Erros:** `400` validação · `401` · `404 PRODUTO_NAO_ENCONTRADO` · `404 VARIACAO_NAO_ENCONTRADA` · `409 ESTOQUE_INSUFICIENTE`

### `GET /usuarios/me/pedidos`

Lista os pedidos do usuário logado, mais recentes primeiro.

**Acesso:** autenticado

```bash
curl http://localhost:5052/usuarios/me/pedidos -H "Authorization: Bearer $TOKEN"
```

**200 OK** — lista de [`PedidoDto`](#pedidodto).

**Erros:** `401`

### `GET /pedidos`

Lista todos os pedidos de todos os usuários, mais recentes primeiro (painel administrativo).

**Acesso:** admin

```bash
curl http://localhost:5052/pedidos -H "Authorization: Bearer $TOKEN_ADMIN"
```

**200 OK** — lista de [`PedidoDto`](#pedidodto).

**Erros:** `401` · `403`

### `PATCH /pedidos/{id}/avancar-status`

Avança o pedido para a próxima etapa. **Não tem corpo** — o próximo status é calculado pelo servidor:

```
pago ──► em_preparo ──► retirado   (formaEntrega = "retirada")
                    └─► entregue   (formaEntrega = "entrega")
```

`retirado` e `entregue` são estados finais. Não é possível voltar status nem cancelar pedido.

**Acesso:** admin

```bash
curl -X PATCH http://localhost:5052/pedidos/a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d/avancar-status \
  -H "Authorization: Bearer $TOKEN_ADMIN"
```

**200 OK** — [`PedidoDto`](#pedidodto) com o novo `status`.

**Erros:** `401` · `403` · `404 PEDIDO_NAO_ENCONTRADO` · `409 PEDIDO_EM_ESTADO_FINAL`

---

## Schemas de resposta

Todos os campos abaixo estão sempre presentes na resposta; os marcados com `?` podem ser `null`.

### AuthResponse

| Campo | Tipo |
|---|---|
| `usuario` | [`UsuarioDto`](#usuariodto) |
| `token` | string (JWT) |

### UsuarioDto

| Campo | Tipo |
|---|---|
| `id` | GUID |
| `nome` | string |
| `email` | string |
| `telefone?` | string \| null |
| `papel` | `jovem` \| `admin` |

### ProdutoDto

| Campo | Tipo |
|---|---|
| `id` | GUID |
| `nome` | string |
| `categoria` | `camisetas` \| `camisas` \| `polos` \| `regatas` \| `moletons` \| `jaquetas` \| `calcas` \| `bermudas` \| `saias` \| `vestidos` \| `calcados` \| `acessorios` |
| `preco` | number |
| `descricao` | string |
| `fotos` | string[] |
| `tamanhos` | string[] (distintos, derivados das variações) |
| `cores` | string[] (distintos, derivados das variações) |
| `destaque` | boolean |
| `variacoes` | [`VariacaoDto`](#variacaodto)[] |

### VariacaoDto

| Campo | Tipo |
|---|---|
| `id` | GUID |
| `tamanho` | string |
| `cor` | string |
| `estoque` | integer |

### EventoDto

| Campo | Tipo |
|---|---|
| `id` | GUID |
| `titulo` | string |
| `descricao` | string |
| `dataHora` | string (ISO 8601) |
| `local` | string |
| `preco` | number |
| `vagasTotais` | integer |
| `vagasRestantes` | integer (calculado) |
| `foto` | string |

### VagasRestantesDto

| Campo | Tipo |
|---|---|
| `vagasRestantes` | integer |

### InscricaoDto

| Campo | Tipo |
|---|---|
| `id` | GUID |
| `eventoId` | GUID |
| `usuarioId` | GUID |
| `status` | `confirmada` \| `cancelada` |
| `valorPago` | number |
| `criadoEm` | string (ISO 8601, UTC) |

### ResultadoInscricaoDto

| Campo | Tipo |
|---|---|
| `resultado` | `criada` \| `ja_inscrito` \| `esgotado` |
| `inscricao?` | [`InscricaoDto`](#inscricaodto) \| null (null quando `esgotado`) |

### PedidoDto

| Campo | Tipo |
|---|---|
| `id` | GUID |
| `usuarioId` | GUID |
| `itens` | [`ItemPedidoDto`](#itempedidodto)[] (na mesma ordem enviada no checkout) |
| `formaEntrega` | `retirada` \| `entrega` |
| `endereco?` | [`EnderecoDto`](#enderecodto) \| null (null em `retirada`) |
| `valorTotal` | number (soma de `precoUnitario × quantidade`) |
| `status` | `pago` \| `em_preparo` \| `retirado` \| `entregue` |
| `criadoEm` | string (ISO 8601, UTC) |

### ItemPedidoDto

Snapshot do produto no momento da compra (não muda se o produto for editado/removido depois).

| Campo | Tipo |
|---|---|
| `produtoId` | GUID |
| `nome` | string |
| `precoUnitario` | number |
| `fotoUrl` | string (primeira foto do produto, ou `""` se não havia) |
| `tamanho` | string |
| `cor` | string |
| `quantidade` | integer |

### EnderecoDto

| Campo | Tipo |
|---|---|
| `rua` | string |
| `numero` | string |
| `complemento?` | string \| null |
| `bairro` | string |
| `cidade` | string |
| `cep` | string |

---

## Fluxos típicos

**Loja (usuário):**

1. `GET /produtos` ou `GET /produtos/destaques` → vitrine.
2. `GET /produtos/{id}` → escolher variação com `estoque > 0`.
3. `POST /auth/login` (ou `/auth/cadastro`) → guardar `token`.
4. `POST /pedidos` com os itens do carrinho → tratar `409 ESTOQUE_INSUFICIENTE`.
5. `GET /usuarios/me/pedidos` → acompanhar `status`.

**Eventos (usuário):**

1. `GET /eventos?apenasFuturos=true` → lista.
2. `POST /eventos/{id}/inscricoes` → tratar `resultado` (`criada`, `ja_inscrito`, `esgotado`).
3. `GET /usuarios/me/inscricoes` → minhas inscrições; `PATCH /inscricoes/{id}/cancelar` para desistir.

**Admin:**

- Catálogo: `POST/PATCH/DELETE /produtos`.
- Eventos: `POST/PATCH/DELETE /eventos`, `GET /eventos/{id}/inscricoes`.
- Pedidos: `GET /pedidos` e `PATCH /pedidos/{id}/avancar-status`.

**Recuperação de senha:**

1. Tela "esqueci a senha" → `POST /auth/recuperar-senha`.
2. Usuário clica no link do e-mail → frontend abre `Frontend:ResetPasswordUrl?token=...`.
3. Frontend envia `POST /auth/redefinir-senha` com o `token` e a `novaSenha`.

---

## OpenAPI e Scalar

- **Arquivo estático:** [`docs/openapi.json`](openapi.json) (OpenAPI 3.1), versionado no repositório.
  Pode ser importado no Postman/Insomnia ou usado para gerar clientes (ex.: `openapi-generator`
  para o frontend Angular).
- **Em tempo de execução** (apenas com `ASPNETCORE_ENVIRONMENT=Development`):
  - `GET /openapi/v1.json` — documento gerado ao vivo.
  - `GET /scalar` — interface interativa para testar os endpoints (use o botão de autenticação
    para colar o token Bearer).

Os resumos, descrições, tipos de resposta e requisitos de segurança vêm dos metadados declarados em
cada endpoint (`src/RedeStore.Api/Endpoints/*.cs`) e da configuração em
`src/RedeStore.Api/OpenApi/OpenApiConfiguration.cs`.

**Regenerar o `docs/openapi.json`** depois de alterar endpoints ou DTOs:

```powershell
./scripts/gerar-openapi.ps1
```

O script compila a API com `-p:OpenApiGenerateDocuments=true` usando configurações fictícias
(nenhum banco ou serviço externo é acessado). No build normal a geração fica desligada.
