-- Migration: Add owner_id to home_games
-- Date: 2026-04-02
-- Description: Adds owner_id column to home_games table to track who created/owns each home game

-- Step 1: Add the column as nullable first
ALTER TABLE home_games ADD COLUMN IF NOT EXISTS owner_id UUID;

-- Step 2: Update existing home games to set owner_id to the first admin user
-- (This is a fallback - in production, you should set the correct owner)
UPDATE home_games 
SET owner_id = (SELECT id FROM users WHERE role = 'Admin' LIMIT 1)
WHERE owner_id IS NULL;

-- Step 3: Make the column NOT NULL after data is populated
ALTER TABLE home_games ALTER COLUMN owner_id SET NOT NULL;

-- Step 4: Add foreign key constraint
ALTER TABLE home_games 
ADD CONSTRAINT fk_home_games_owner 
FOREIGN KEY (owner_id) REFERENCES users(id) ON DELETE RESTRICT;

-- Step 5: Create index for performance
CREATE INDEX IF NOT EXISTS idx_home_games_owner ON home_games (owner_id);
