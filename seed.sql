-- Seed inicial do sistema de poker

-- 1. Person admin
INSERT INTO persons (full_name, nickname, email, whatsapp, is_active, created_at, updated_at)
VALUES ('Administrador', 'Admin', 'admin@poker.com', NULL, true, NOW(), NOW())
ON CONFLICT DO NOTHING;

-- 2. User admin (senha: admin123, hash SHA256+Base64)
INSERT INTO users (person_id, email, password_hash, role, is_active, created_at, updated_at)
SELECT id, 'admin@poker.com', 'JAvlGPq9JyTdtvBO6x2llnRI1+gxwIyPqCKAn3THIKk=', 'Admin', true, NOW(), NOW()
FROM persons WHERE email = 'admin@poker.com'
ON CONFLICT DO NOTHING;

-- 3. Estrutura de blinds padrao
INSERT INTO blind_structures (name, description, is_active, created_at, updated_at)
VALUES ('Padrao 20min', 'Estrutura padrao com niveis de 20 minutos', true, NOW(), NOW())
ON CONFLICT DO NOTHING;

-- 4. Niveis de blind
INSERT INTO blind_levels (blind_structure_id, level_number, small_blind, big_blind, ante, big_blind_ante, duration_minutes, is_break, created_at, updated_at)
SELECT bs.id, v.level_number, v.sb, v.bb, v.ante, 0, v.duration, v.is_break, NOW(), NOW()
FROM blind_structures bs,
(VALUES
  (1,  25,   50,   0, 20, false),
  (2,  50,   100,  0, 20, false),
  (3,  75,   150,  0, 20, false),
  (4,  100,  200,  0, 20, false),
  (5,  0,    0,    0, 10, true),
  (6,  150,  300,  50, 20, false),
  (7,  200,  400,  50, 20, false),
  (8,  300,  600,  75, 20, false),
  (9,  400,  800,  100, 20, false),
  (10, 0,    0,    0, 10, true),
  (11, 500,  1000, 100, 20, false),
  (12, 600,  1200, 200, 20, false),
  (13, 800,  1600, 200, 20, false),
  (14, 1000, 2000, 300, 20, false),
  (15, 0,    0,    0, 10, true),
  (16, 1500, 3000, 400, 20, false),
  (17, 2000, 4000, 500, 20, false),
  (18, 3000, 6000, 1000, 20, false),
  (19, 4000, 8000, 1000, 20, false),
  (20, 5000, 10000, 2000, 20, false)
) AS v(level_number, sb, bb, ante, duration, is_break)
WHERE bs.name = 'Padrao 20min'
ON CONFLICT DO NOTHING;

-- 5. Regra de pontuacao padrao
INSERT INTO scoring_rules (name, description, points_config, is_active, created_at, updated_at)
VALUES (
  'Padrao Liga',
  'Pontuacao padrao para rankings de liga',
  '{"positions":{"1":100,"2":70,"3":50,"4":40,"5":32,"6":26,"7":22,"8":19,"9":16,"10":14},"participationBonus":10,"playerCountMultiplier":false,"eliminationBonus":0}',
  true, NOW(), NOW()
)
ON CONFLICT DO NOTHING;

-- 6. Jogadores de exemplo
INSERT INTO persons (full_name, nickname, email, whatsapp, is_active, created_at, updated_at) VALUES
('Joao Silva', 'Joaozinho', 'joao@email.com', '+5511999990001', true, NOW(), NOW()),
('Carlos Mendes', 'Shark', 'carlos@email.com', '+5511999990002', true, NOW(), NOW()),
('Ana Paula', 'Ana', 'ana@email.com', '+5511999990003', true, NOW(), NOW()),
('Pedro Santos', 'Pedrinho', 'pedro@email.com', '+5511999990004', true, NOW(), NOW()),
('Fernanda Costa', 'Fer', 'fernanda@email.com', '+5511999990005', true, NOW(), NOW())
ON CONFLICT DO NOTHING;

-- 7. Home game de exemplo
INSERT INTO home_games (name, description, location, pix_key, pix_beneficiary, timezone, default_buy_in, default_rebuy, default_addon, is_active, created_at, updated_at)
VALUES ('Poker Night SP', 'Home game semanal de poker', 'Rua das Cartas, 123 - SP', '11999999999', 'Poker Night SP', 'America/Sao_Paulo', 100.00, 100.00, 100.00, true, NOW(), NOW())
ON CONFLICT DO NOTHING;
