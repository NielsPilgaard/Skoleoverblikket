#!/usr/bin/env bash
# Runs once, when the data volume is empty (docker-entrypoint-initdb.d).
# One cluster, two databases with their own roles (task 54 D2), and the backup agent role
# with only what pg_receivewal and pgBackRest need (task 60 D1).
set -euo pipefail

: "${APP_DB_PASSWORD:?APP_DB_PASSWORD must be set}"
: "${KEYCLOAK_DB_PASSWORD:?KEYCLOAK_DB_PASSWORD must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
	-v app_password="$APP_DB_PASSWORD" \
	-v keycloak_password="$KEYCLOAK_DB_PASSWORD" <<-'SQL'
	CREATE ROLE skoleoverblikket LOGIN PASSWORD :'app_password';
	CREATE ROLE keycloak LOGIN PASSWORD :'keycloak_password';
	CREATE DATABASE skoleoverblikket OWNER skoleoverblikket;
	CREATE DATABASE keycloak OWNER keycloak;

	-- Socket only (pg_hba peer map), no password. Reads no table data: row counts come
	-- from pg_stat_user_tables, and the app grants SELECT on the two tables it needs
	-- (SchoolDeletionRecords, __EFMigrationsHistory) in a migration.
	CREATE ROLE backup_agent LOGIN REPLICATION;
	GRANT pg_read_all_settings, pg_checkpoint TO backup_agent;
	GRANT EXECUTE ON FUNCTION pg_backup_start(text, boolean) TO backup_agent;
	GRANT EXECUTE ON FUNCTION pg_backup_stop(boolean) TO backup_agent;
	GRANT EXECUTE ON FUNCTION pg_switch_wal() TO backup_agent;
	GRANT EXECUTE ON FUNCTION pg_create_restore_point(text) TO backup_agent;
	GRANT EXECUTE ON FUNCTION pg_control_checkpoint() TO backup_agent;
	GRANT EXECUTE ON FUNCTION pg_control_system() TO backup_agent;
	GRANT EXECUTE ON FUNCTION pg_ls_dir(text) TO backup_agent;

	-- The agent commits this row every 5 minutes before switching WAL. That gives every WAL
	-- segment a commit timestamp, so a point-in-time target is always reachable even on a quiet
	-- night, and a restore can read how far it got. It's the only table the agent writes.
	CREATE TABLE backup_agent_heartbeat (id int PRIMARY KEY, at timestamptz NOT NULL);
	ALTER TABLE backup_agent_heartbeat OWNER TO backup_agent;
SQL
