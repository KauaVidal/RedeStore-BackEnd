# Requisitos de Backend — REDE (Primeira Igreja Batista de Vila Maria)

Documento gerado a partir da varredura completa do frontend atual (`main`,
pós-merge dos 4 subprojetos: Fundação, Loja, Eventos, Admin). Hoje toda a
"persistência" é um mock em memória no próprio Angular (`*-mock-store.ts`),
com `localStorage` só para sessão de login e carrinho. Este doc especifica
o que um backend real precisa oferecer para substituir esse mock sem
mudar os contratos que as telas já consomem.

Cada seção de endpoint lista: método/rota sugerida, quem pode chamar,
entrada, saída, e as regras de negócio observadas (ou necessárias) no
frontend. Nomes de campos seguem o padrão em português já usado no
frontend (`nome`, `preco`, `dataHora` etc.) para minimizar tradução na
integração.

---

## 1. Entidades

### 1.1 Usuario

```
Usuario {
  id: string
  nome: string
  email: string          // único
  telefone?: string
  papel: 'jovem' | 'admin'
  senhaHash: string       // NUNCA exposto na API
}
```

- Duas contas seed hoje no mock: `admin@rede.com` / `admin123` (papel
  `admin`) e `jovem@rede.com` / `jovem123` (papel `jovem`). São só dados
  de desenvolvimento — o backend real não deve subir com senhas em texto
  puro nem replicar isso em produção.
- `papel` só tem dois valores hoje. Todo usuário criado via cadastro
  público nasce como `jovem` — não existe fluxo de auto-promoção a admin
  no frontend (promoção a admin precisa ser uma ação manual/administrativa
  fora do escopo do app, ex: direto no banco ou um endpoint futuro
  restrito).

### 1.2 Produto

```
Produto {
  id: string
  nome: string
  categoria: 'camisetas' | 'moletons' | 'acessorios'
  preco: number           // > 0
  descricao: string
  fotos: string[]         // URLs
  tamanhos: string[]      // derivado das variações, mas persistido também
  cores: string[]         // idem
  variacoes: Variacao[]
  destaque: boolean
}

Variacao {
  tamanho: string
  cor: string
  estoque: number         // >= 0
}
```

- `tamanhos`/`cores` no frontend são hoje derivados de `variacoes` no
  momento do save (`[...new Set(variacoes.map(v => v.tamanho))]`), não
  campos digitados à parte. O backend pode escolher persistir os três
  campos como estão (compatibilidade 1:1 com o contrato atual) ou
  recalculá-los sempre a partir de `variacoes` — mas a resposta da API
  precisa continuar trazendo os três, pois a listagem/filtro da loja lê
  `produto.tamanhos`/`produto.cores` diretamente.
- Categoria é um enum fechado de 3 valores — sem tela de gestão de
  categorias no frontend.

### 1.3 Evento

```
Evento {
  id: string
  titulo: string
  descricao: string
  dataHora: string        // ISO 8601
  local: string
  preco: number           // >= 0, 0 = gratuito
  vagasTotais: number     // >= 1
  foto: string            // URL
}
```

### 1.4 Inscricao

```
Inscricao {
  id: string
  eventoId: string
  usuarioId: string
  status: 'confirmada' | 'cancelada'
  valorPago: number
  criadoEm: string        // ISO 8601
}
```

### 1.5 Pedido

```
Pedido {
  id: string
  usuarioId: string
  itens: ItemPedido[]
  formaEntrega: 'retirada' | 'entrega'
  endereco?: Endereco     // obrigatório se formaEntrega === 'entrega'
  valorTotal: number      // calculado no backend, nunca recebido do client
  status: 'pago' | 'em_preparo' | 'retirado' | 'entregue'
  criadoEm: string
}

ItemPedido {
  produtoId: string
  nome: string            // snapshot do nome no momento da compra
  precoUnitario: number   // snapshot do preço no momento da compra
  fotoUrl: string
  tamanho: string
  cor: string
  quantidade: number
}

Endereco {
  rua: string
  numero: string
  complemento?: string
  bairro: string
  cidade: string
  cep: string
}
```

- Os itens do pedido são um **snapshot** (nome/preço/foto), não uma
  referência viva ao produto — se o produto mudar de preço depois, o
  pedido antigo não deve refletir isso. Precisa copiar os valores no
  momento da criação do pedido, não fazer join com a tabela de produtos.

---

## 2. Autenticação e Autorização

O frontend não usa JWT nem cookies — a "sessão" hoje é só o objeto
`Usuario` salvo em `localStorage` (`rede_sessao_usuario`), sem token. Um
backend real precisa de um mecanismo de sessão de verdade; a recomendação
é JWT (access token) devolvido no login/cadastro e enviado em
`Authorization: Bearer` nas chamadas seguintes — o frontend precisará de
um interceptor HTTP para isso (fora do escopo deste doc, é trabalho de
frontend).

Dois guards de rota existem hoje e mapeiam direto para autorização de API:

- **`authGuard`** — exige usuário autenticado (qualquer papel). Protege
  `/perfil` no frontend → no backend, protege endpoints de "meus
  pedidos", "minhas inscrições", atualizar perfil, criar pedido, se
  inscrever em evento.
- **`adminGuard`** — exige `papel === 'admin'`. Protege `/admin/**` no
  frontend → no backend, protege todo CRUD de produtos/eventos, listagem
  de todos os pedidos, avanço de status, listagem/cancelamento de
  inscrições por evento.

### Endpoints

| Método | Rota | Auth | Descrição |
|---|---|---|---|
| POST | `/auth/login` | pública | `{ email, senha }` → `{ usuario, token }`. Erro `CREDENCIAIS_INVALIDAS` se não bater. |
| POST | `/auth/cadastro` | pública | `{ nome, email, senha }` → `{ usuario, token }`. Papel sempre `jovem`. Erro `EMAIL_EM_USO` se e-mail já existe. |
| POST | `/auth/recuperar-senha` | pública | `{ email }` → `204`. Hoje o frontend só mostra uma mensagem de sucesso sem checar se o e-mail existe (evita enumeração de contas) — manter esse comportamento: sempre `204`, mesmo se o e-mail não existir. **Precisa de envio real de e-mail** (fora do escopo do mock atual, que só simula latência). |
| GET | `/auth/me` | autenticado | Retorna o usuário da sessão atual (substitui o `usuarioAtual` lido do localStorage). |
| PATCH | `/auth/perfil` | autenticado | `{ nome?, email?, telefone? }` → `Usuario` atualizado. Usuário só edita o próprio perfil. |
| GET | `/usuarios/:id` | autenticado (uso interno) | Usado para o Admin resolver nome/e-mail do cliente em Pedidos e Inscrições (`AuthService.buscarPorId`). Não precisa ser exposto a não-admins além do próprio `id`. |

### Validações de campo (herdadas dos forms do frontend)

- `nome`: obrigatório, mínimo 2 caracteres (cadastro).
- `email`: obrigatório, formato de e-mail válido.
- `senha`: obrigatório, mínimo 8 caracteres. **O backend precisa fazer
  hash (bcrypt/argon2)** — o mock guarda em texto puro, isso não pode ir
  para produção.
- Confirmação de senha é validação só de UI (compara dois campos do
  form) — não precisa existir no payload da API.

---

## 3. Produtos (Loja + Admin)

| Método | Rota | Auth | Descrição |
|---|---|---|---|
| GET | `/produtos?categoria=&busca=` | pública | Lista com filtro opcional por categoria (enum) e busca por nome (case-insensitive, `contains`). |
| GET | `/produtos/destaques` | pública | Lista só os `destaque: true` (usado na Home). |
| GET | `/produtos/:id` | pública | Detalhe de um produto. 404 se não existir. |
| POST | `/produtos` | admin | Cria produto. Body = `Produto` sem `id`. |
| PATCH | `/produtos/:id` | admin | Atualização parcial. 404 se não existir. |
| DELETE | `/produtos/:id` | admin | Remove produto. **Decisão de negócio pendente:** o mock deleta mesmo se houver pedidos existentes que referenciam esse produto (via snapshot em `ItemPedido`, então não quebra pedidos antigos) — ok manter hard delete, já que o pedido não depende de o produto ainda existir. |

### Regras de negócio

- Preço deve ser > 0 (validado no form: `Validators.min(0.01)`).
- Precisa de ao menos 1 variação (tamanho+cor+estoque) para o produto ser
  salvo — um produto sem nenhuma variação não pode ser vendido.
- `fotos` é uma lista de URLs — o frontend hoje aceita **URLs diretas
  digitadas pelo admin**, não há upload de arquivo. Se o backend real
  quiser oferecer upload de imagem, isso é uma funcionalidade nova sem
  equivalente no frontend atual (ver seção 7).
- **Controle de estoque na compra (funcionalidade que falta até no mock,
  precisa existir de verdade no backend):** hoje `OrderService.criar` no
  frontend NÃO decrementa `variacoes[].estoque` ao fechar um pedido — é
  uma lacuna conhecida do protótipo. O backend real **precisa**:
  1. Validar, no momento de criar o pedido, que cada item ainda tem
     estoque suficiente na variação (tamanho+cor) pedida.
  2. Decrementar o estoque atomicamente (transação/lock) para evitar
     overselling com dois checkouts concorrentes no mesmo produto.
  3. Rejeitar a criação do pedido (ou o item) se o estoque não for mais
     suficiente entre a tela de carrinho e a confirmação do checkout.

---

## 4. Eventos (Agenda pública + Admin)

| Método | Rota | Auth | Descrição |
|---|---|---|---|
| GET | `/eventos` | pública | Lista todos os eventos. A tela de Agenda filtra client-side por `dataHora >= agora`; o backend pode oferecer isso como filtro (`?apenasFuturos=true`) ou deixar o filtro no client — ambos os comportamentos já existem hoje (Admin lista todos, Agenda só futuros). |
| GET | `/eventos/:id` | pública | Detalhe do evento. 404 se não existir. |
| POST | `/eventos` | admin | Cria evento. Body = `Evento` sem `id`. |
| PATCH | `/eventos/:id` | admin | Atualização parcial. |
| DELETE | `/eventos/:id` | admin | Remove evento. Mesma observação de Produtos: inscrições antigas não fazem join vivo com o evento além do `eventoId` — decisão de produto sobre o que mostrar em "Minhas inscrições" se o evento for removido (hoje o frontend mostraria `evento: undefined` e provavelmente quebraria a exibição do título; vale o backend impedir DELETE de evento com inscrições confirmadas, ou o frontend tratar `evento` ausente com um fallback — recomendo a primeira opção). |

### Validações de campo

- `titulo`, `descricao`, `local`, `foto`: obrigatórios.
- `dataHora`: obrigatório, formato ISO 8601.
- `preco`: obrigatório, `>= 0` (0 = gratuito, exibido como "Gratuito" no
  frontend em vez de "R$ 0").
- `vagasTotais`: obrigatório, `>= 1`.

---

## 5. Inscrições em eventos

| Método | Rota | Auth | Descrição |
|---|---|---|---|
| POST | `/eventos/:id/inscricoes` | autenticado | Inscreve o usuário logado no evento. Ver regras abaixo. |
| GET | `/usuarios/me/inscricoes` | autenticado | Lista as inscrições do usuário logado (todas, incluindo canceladas — a tela "Minhas inscrições" mostra o status de cada uma). |
| GET | `/eventos/:id/inscricoes` | admin | Lista todas as inscrições de um evento (tela Admin → Inscrições). |
| PATCH | `/inscricoes/:id/cancelar` | autenticado (dono) OU admin | Marca a inscrição como `cancelada`. O dono cancela a própria (fluxo "Minhas inscrições"); o admin cancela qualquer uma (fluxo Admin → Inscrições). |
| GET | `/eventos/:id/vagas-restantes` | pública | `vagasTotais - contagem de inscrições confirmadas`. Usado em 3 telas (Agenda, Detalhes do evento, Admin → Eventos) — vale a pena já vir embutido na resposta de `GET /eventos` e `GET /eventos/:id` como um campo calculado (`vagasRestantes`), em vez de forçar N+1 requisições. |

### Regras de negócio (já implementadas no mock, precisam ser mantidas)

- **Idempotência:** se o usuário já tem uma inscrição `confirmada` para
  aquele evento, `POST /eventos/:id/inscricoes` não cria uma segunda —
  retorna a existente com um indicador de que já estava inscrito
  (`resultado: 'ja_inscrito'`). Isso cobre o caso de o usuário
  recarregar/voltar na tela de confirmação.
- **Controle de vagas:** se `vagasConfirmadas >= vagasTotais` no momento
  da inscrição, retorna esgotado (`resultado: 'esgotado'`) sem criar
  registro. **Isso precisa de lock/transação no backend real** — dois
  usuários inscrevendo-se na última vaga ao mesmo tempo é exatamente o
  tipo de corrida que o mock (single-threaded, em memória) não expõe mas
  um backend com banco compartilhado e requisições concorrentes expõe.
- Resposta da API de inscrição deve distinguir os 3 casos
  (`criada` / `ja_inscrito` / `esgotado`) — o frontend já tem 3 telas de
  resultado diferentes para isso.
- `valorPago` é um snapshot do `preco` do evento no momento da inscrição
  (mesma lógica de snapshot dos itens de pedido).

---

## 6. Pedidos (Checkout, Meus Pedidos, Admin)

| Método | Rota | Auth | Descrição |
|---|---|---|---|
| POST | `/pedidos` | autenticado | Cria o pedido a partir do carrinho enviado. Ver regras abaixo. |
| GET | `/usuarios/me/pedidos` | autenticado | Lista os pedidos do usuário logado, mais recentes primeiro. |
| GET | `/pedidos` | admin | Lista todos os pedidos de todos os usuários, mais recentes primeiro (Admin → Pedidos). |
| PATCH | `/pedidos/:id/avancar-status` | admin | Avança para o **próximo** status válido — não aceita um status arbitrário no body, só "avança um passo". Ver máquina de estados abaixo. |

### Payload de criação (`POST /pedidos`)

```
{
  itens: [{ produtoId, tamanho, cor, quantidade }],  // sem preço/nome — o backend busca do produto atual
  formaEntrega: 'retirada' | 'entrega',
  endereco?: Endereco   // obrigatório se formaEntrega === 'entrega'
}
```

**Importante:** o frontend hoje monta `itens` com preço/nome/foto vindos
do carrinho local (que por sua vez veio da tela de produto quando
adicionado). Um backend real **não deve confiar nesses valores vindos do
client** — precisa buscar `produtoId` no banco, pegar `preco`/`nome`/foto
atuais do produto para montar o snapshot do `ItemPedido`, e calcular
`valorTotal` ele mesmo. Aceitar preço vindo do client é uma
vulnerabilidade óbvia (o usuário poderia mandar `precoUnitario: 0.01`).

### Regras de negócio

- `endereco` é obrigatório apenas quando `formaEntrega === 'entrega'`;
  ausente/ignorado quando `'retirada'`.
- `status` inicial de todo pedido novo é sempre `'pago'` — não existe no
  frontend um fluxo de pagamento pendente/gateway; o checkout assume
  pagamento já efetivado no momento da criação (não há integração com
  gateway de pagamento no protótipo — se isso for necessário no backend
  real, é uma funcionalidade nova, ver seção 7).
- **Máquina de estados do pedido** (função `proximoStatus` já existente
  no frontend, replicar exatamente):
  - `pago` → `em_preparo`
  - `em_preparo` → `retirado` (se `formaEntrega === 'retirada'`) **ou**
    `entregue` (se `formaEntrega === 'entrega'`)
  - `retirado` e `entregue` são estados finais — não têm próximo status;
    o botão "Avançar" desaparece da tela Admin nesses casos.
  - O endpoint de avançar status deve rejeitar a chamada se o pedido já
    estiver em um estado final.
- Ver decrementação de estoque na seção 3 — é responsabilidade deste
  fluxo (criação de pedido), não de produtos.
- Depois de criar o pedido com sucesso, o carrinho do usuário é limpo —
  isso é hoje uma ação client-side (`CartService.limpar()`); se o
  carrinho passar a ser persistido no backend (ver seção 7), essa limpeza
  precisa acontecer lá também.

---

## 7. Coisas que existem no frontend mas não precisam de backend (ou que são decisões em aberto)

- **Carrinho de compras:** hoje é 100% `localStorage`, por dispositivo,
  sem sincronização entre aparelhos. Pode continuar assim (mais simples,
  sem necessidade de endpoint) ou virar uma entidade persistida por
  usuário se o produto quiser carrinho sincronizado entre dispositivos —
  não há isso no frontend atual, é uma decisão a tomar, não uma lacuna.
- **Upload de imagens:** produtos e eventos usam URLs diretas digitadas
  pelo admin (sem input de arquivo). Se o backend real quiser oferecer
  upload (S3/Cloudinary/etc.), é uma funcionalidade adicional sem
  contrato equivalente hoje no frontend — o formulário precisaria mudar
  junto.
- **Pagamento:** não há gateway de pagamento integrado — o "pagamento" é
  simulado (pedido nasce com status `pago`). Uma integração real de
  pagamento (Pix, cartão) é fora do escopo do que o frontend atual
  suporta; entraria como uma mudança de contrato em `POST /pedidos`
  (provavelmente um passo intermediário de status `aguardando_pagamento`
  antes de `pago`, com webhook do gateway).
- **Recuperação de senha:** o frontend só dispara a ação e mostra uma
  mensagem de sucesso — não há tela de "criar nova senha" com token. O
  fluxo completo (gerar token, enviar e-mail, tela de redefinição) é
  necessário no backend mas o frontend hoje não tem a segunda metade
  desse fluxo implementada — precisa ser adicionada em conjunto.
- **Paginação:** nenhuma lista do frontend pagina hoje (produtos, eventos,
  pedidos, inscrições são todos carregados por completo). Para volume de
  dados real isso deve ser adicionado nas rotas GET de listagem
  (`?pagina=&tamanho=`), mas é uma extensão, não algo que o frontend já
  espera — as telas atuais não têm paginação nem infinite scroll.

---

## 8. Resumo de autorização por papel

| Ação | Pública | Jovem (autenticado) | Admin |
|---|---|---|---|
| Ver produtos/eventos | ✅ | ✅ | ✅ |
| Login/cadastro/recuperar senha | ✅ | — | — |
| Ver/editar próprio perfil | ❌ | ✅ | ✅ |
| Criar pedido, ver próprios pedidos | ❌ | ✅ | ✅ |
| Inscrever-se/cancelar própria inscrição | ❌ | ✅ | ✅ |
| CRUD de produtos | ❌ | ❌ | ✅ |
| CRUD de eventos | ❌ | ❌ | ✅ |
| Ver todos os pedidos, avançar status | ❌ | ❌ | ✅ |
| Ver/cancelar inscrições de qualquer usuário | ❌ | ❌ | ✅ |

Não existe hoje nenhum papel intermediário (ex.: "atendente" que só vê
Pedidos mas não Produtos) — o Admin do frontend trata `admin` como
tudo-ou-nada.
