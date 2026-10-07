#!/usr/bin/env bash
# Dev only: the real app schema (migration_script.sql, as the app role, so its GRANT to
# backup_agent runs) plus a few fake schools and a stand-in for Keycloak's tables, so drills and
# restores have something to check. Fake names only.
set -euo pipefail

export PGPASSWORD=dev-app
psql -q -v ON_ERROR_STOP=1 -h /var/run/postgresql -U skoleoverblikket -d skoleoverblikket -f /dev-migrations/migration_script.sql
psql -v ON_ERROR_STOP=1 -h /var/run/postgresql -U skoleoverblikket -d skoleoverblikket <<'SQL'
INSERT INTO "Schools" ("Id", "Name") VALUES
  ('11111111-1111-1111-1111-111111111111', 'Dev Friskole'),
  ('22222222-2222-2222-2222-222222222222', 'Prøve Privatskole'),
  ('33333333-3333-3333-3333-333333333333', 'Opsagt Friskole')
ON CONFLICT DO NOTHING;
INSERT INTO "Subscriptions" ("Id", "SchoolId", "Status", "TrialEnd", "CanceledAt", "DeletionWarningSentAt") VALUES
  (gen_random_uuid(), '11111111-1111-1111-1111-111111111111', 1, now(), NULL, NULL),
  (gen_random_uuid(), '22222222-2222-2222-2222-222222222222', 0, now() + interval '30 days', NULL, NULL),
  (gen_random_uuid(), '33333333-3333-3333-3333-333333333333', 3, now() - interval '200 days', now() - interval '100 days', NULL)
ON CONFLICT DO NOTHING;
INSERT INTO "Rooms" ("Id", "TenantId", "Name", "CreatedAt")
  SELECT gen_random_uuid(), s."Id", 'Lokale ' || g, now() FROM "Schools" s, generate_series(1, 25) g;
SQL

export PGPASSWORD=dev-keycloak
psql -v ON_ERROR_STOP=1 -h /var/run/postgresql -U keycloak -d keycloak <<'SQL'
CREATE TABLE IF NOT EXISTS realm (id text PRIMARY KEY, name text NOT NULL);
CREATE TABLE IF NOT EXISTS user_entity (id text PRIMARY KEY, realm_id text NOT NULL);
CREATE TABLE IF NOT EXISTS credential (id text PRIMARY KEY, user_id text NOT NULL);
INSERT INTO realm VALUES ('r1', 'Skoleoverblikket') ON CONFLICT DO NOTHING;
INSERT INTO user_entity SELECT 'u' || g, 'r1' FROM generate_series(1, 40) g ON CONFLICT DO NOTHING;
INSERT INTO credential SELECT 'c' || g, 'u' || g FROM generate_series(1, 40) g ON CONFLICT DO NOTHING;
ANALYZE;
SQL

export PGPASSWORD=dev-app
psql -v ON_ERROR_STOP=1 -h /var/run/postgresql -U skoleoverblikket -d skoleoverblikket -c 'ANALYZE;'
echo "Seeded."
