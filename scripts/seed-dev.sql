-- Seed de DESENVOLVIMENTO: dados de exemplo (7 produtos com variações e 6 eventos) para testar a API
-- e o front localmente. NUNCA rodar em produção. Use via scripts/seed-dev.ps1 (ver README).
-- Pré-requisito: a conta admin@rede.com já foi criada via POST /auth/cadastro (tela de cadastro do
-- front ou Scalar) — a senha usa o hash do ASP.NET Identity e não pode ser gerada por SQL.
-- Idempotente: promove admin@rede.com a Admin e só insere produtos/eventos se as tabelas estiverem vazias.
-- Atenção: o banco grava enums pelo NOME C# ('Admin', 'Camisetas'), não em minúsculas como o JSON.
-- Se uma migration mudar as tabelas/colunas usadas aqui, atualize este script junto.

BEGIN;

UPDATE "Usuarios" SET "Papel" = 'Admin' WHERE lower("Email") = 'admin@rede.com';

DO $$
DECLARE
  p uuid;
BEGIN
  IF NOT EXISTS (SELECT 1 FROM "Produtos") THEN
    p := gen_random_uuid();
    INSERT INTO "Produtos" ("Id", "Nome", "Categoria", "Preco", "Descricao", "Fotos", "Destaque")
    VALUES (p, 'Camiseta REDE Clássica', 'Camisetas', 79.90, 'Camiseta 100% algodão com a marca REDE estampada no peito.',
            ARRAY['https://picsum.photos/seed/camiseta-classica-rede/480/480'], true);
    INSERT INTO "Variacoes" ("Id", "ProdutoId", "Tamanho", "Cor", "Estoque") VALUES
      (gen_random_uuid(), p, 'P', 'Preto', 12), (gen_random_uuid(), p, 'P', 'Amarelo', 8),
      (gen_random_uuid(), p, 'M', 'Preto', 15), (gen_random_uuid(), p, 'M', 'Amarelo', 10),
      (gen_random_uuid(), p, 'G', 'Preto', 9),  (gen_random_uuid(), p, 'G', 'Amarelo', 6),
      (gen_random_uuid(), p, 'GG', 'Preto', 4), (gen_random_uuid(), p, 'GG', 'Amarelo', 0);

    p := gen_random_uuid();
    INSERT INTO "Produtos" ("Id", "Nome", "Categoria", "Preco", "Descricao", "Fotos", "Destaque")
    VALUES (p, 'Camiseta REDE Minimalista', 'Camisetas', 74.90, 'Estampa discreta, para o dia a dia.',
            ARRAY['https://picsum.photos/seed/camiseta-minimalista-rede/480/480'], false);
    INSERT INTO "Variacoes" ("Id", "ProdutoId", "Tamanho", "Cor", "Estoque") VALUES
      (gen_random_uuid(), p, 'P', 'Preto', 10), (gen_random_uuid(), p, 'P', 'Amarelo', 10),
      (gen_random_uuid(), p, 'M', 'Preto', 10), (gen_random_uuid(), p, 'M', 'Amarelo', 10),
      (gen_random_uuid(), p, 'G', 'Preto', 10), (gen_random_uuid(), p, 'G', 'Amarelo', 10),
      (gen_random_uuid(), p, 'GG', 'Preto', 10), (gen_random_uuid(), p, 'GG', 'Amarelo', 10);

    p := gen_random_uuid();
    INSERT INTO "Produtos" ("Id", "Nome", "Categoria", "Preco", "Descricao", "Fotos", "Destaque")
    VALUES (p, 'Camiseta REDE Edição Retiro', 'Camisetas', 84.90, 'Estampa exclusiva do último retiro da REDE.',
            ARRAY['https://picsum.photos/seed/camiseta-retiro-rede/480/480'], false);
    INSERT INTO "Variacoes" ("Id", "ProdutoId", "Tamanho", "Cor", "Estoque") VALUES
      (gen_random_uuid(), p, 'P', 'Preto', 5), (gen_random_uuid(), p, 'M', 'Preto', 7),
      (gen_random_uuid(), p, 'G', 'Preto', 3);

    p := gen_random_uuid();
    INSERT INTO "Produtos" ("Id", "Nome", "Categoria", "Preco", "Descricao", "Fotos", "Destaque")
    VALUES (p, 'Moletom REDE Essencial', 'Moletons', 139.90, 'Moletom canguru, forro macio.',
            ARRAY['https://picsum.photos/seed/moletom-essencial-rede/480/480'], true);
    INSERT INTO "Variacoes" ("Id", "ProdutoId", "Tamanho", "Cor", "Estoque") VALUES
      (gen_random_uuid(), p, 'P', 'Preto', 5), (gen_random_uuid(), p, 'P', 'Amarelo', 5),
      (gen_random_uuid(), p, 'M', 'Preto', 5), (gen_random_uuid(), p, 'M', 'Amarelo', 5),
      (gen_random_uuid(), p, 'G', 'Preto', 5), (gen_random_uuid(), p, 'G', 'Amarelo', 5),
      (gen_random_uuid(), p, 'GG', 'Preto', 5), (gen_random_uuid(), p, 'GG', 'Amarelo', 5);

    p := gen_random_uuid();
    INSERT INTO "Produtos" ("Id", "Nome", "Categoria", "Preco", "Descricao", "Fotos", "Destaque")
    VALUES (p, 'Moletom REDE Oversized', 'Moletons', 149.90, 'Corte oversized, streetwear.',
            ARRAY['https://picsum.photos/seed/moletom-oversized-rede/480/480'], false);
    INSERT INTO "Variacoes" ("Id", "ProdutoId", "Tamanho", "Cor", "Estoque") VALUES
      (gen_random_uuid(), p, 'M', 'Preto', 6), (gen_random_uuid(), p, 'G', 'Preto', 8),
      (gen_random_uuid(), p, 'GG', 'Preto', 2);

    p := gen_random_uuid();
    INSERT INTO "Produtos" ("Id", "Nome", "Categoria", "Preco", "Descricao", "Fotos", "Destaque")
    VALUES (p, 'Boné REDE', 'Acessorios', 59.90, 'Boné aba curva bordado.',
            ARRAY['https://picsum.photos/seed/bone-rede/480/480'], true);
    INSERT INTO "Variacoes" ("Id", "ProdutoId", "Tamanho", "Cor", "Estoque") VALUES
      (gen_random_uuid(), p, 'Único', 'Preto', 20), (gen_random_uuid(), p, 'Único', 'Amarelo', 15);

    p := gen_random_uuid();
    INSERT INTO "Produtos" ("Id", "Nome", "Categoria", "Preco", "Descricao", "Fotos", "Destaque")
    VALUES (p, 'Squeeze REDE', 'Acessorios', 39.90, 'Squeeze 600ml com o logo da REDE.',
            ARRAY['https://picsum.photos/seed/squeeze-rede/480/480'], false);
    INSERT INTO "Variacoes" ("Id", "ProdutoId", "Tamanho", "Cor", "Estoque") VALUES
      (gen_random_uuid(), p, 'Único', 'Preto', 30);
  END IF;

  IF NOT EXISTS (SELECT 1 FROM "Eventos") THEN
    INSERT INTO "Eventos" ("Id", "Titulo", "Descricao", "DataHora", "Local", "Preco", "VagasTotais", "Foto") VALUES
      (gen_random_uuid(), 'Retiro de Verão REDE', 'Um fim de semana de imersão, comunhão e descanso para os jovens da REDE.',
       date_trunc('hour', now()) + interval '30 days', 'Sítio Vida Nova, Ibiúna', 250.00, 4,
       'https://picsum.photos/seed/retiro-verao-rede/480/480'),
      (gen_random_uuid(), 'Encontro de Jovens', 'Noite de louvor, palavra e comunhão na igreja.',
       date_trunc('hour', now()) + interval '5 days', 'Templo sede, Vila Maria', 0, 100,
       'https://picsum.photos/seed/encontro-jovens-rede/480/480'),
      (gen_random_uuid(), 'Culto Especial de Missões', 'Culto dedicado ao envio e apoio aos missionários da igreja.',
       date_trunc('hour', now()) + interval '12 days', 'Templo sede, Vila Maria', 0, 200,
       'https://picsum.photos/seed/culto-missoes-rede/480/480'),
      (gen_random_uuid(), 'Acampamento de Carnaval', 'Três dias de atividades ao ar livre, esportes e devocionais.',
       date_trunc('hour', now()) + interval '60 days', 'Chácara Monte Sião, Mairiporã', 180.00, 3,
       'https://picsum.photos/seed/acampamento-carnaval-rede/480/480'),
      (gen_random_uuid(), 'Workshop de Louvor', 'Oficina prática de instrumentos e ministério de louvor para iniciantes.',
       date_trunc('hour', now()) + interval '8 days', 'Templo sede, Vila Maria', 40.00, 20,
       'https://picsum.photos/seed/workshop-louvor-rede/480/480'),
      (gen_random_uuid(), 'Vigília de Oração', 'Noite inteira de oração e adoração para encerrar o trimestre.',
       date_trunc('hour', now()) - interval '10 days', 'Templo sede, Vila Maria', 0, 150,
       'https://picsum.photos/seed/vigilia-oracao-rede/480/480');
  END IF;
END $$;

COMMIT;
