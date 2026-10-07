# Prod Postgres (task 54). No backup tooling in here: WAL streaming and pgBackRest live in
# the backup-agent image (task 60), so a broken backup setup can't take Postgres down.
# PG_MAJOR must match the backup-agent image, because physical restores need the same major.
ARG PG_MAJOR=17
FROM postgres:${PG_MAJOR}

COPY postgresql.conf pg_hba.conf pg_ident.conf /etc/postgresql/
COPY initdb/ /docker-entrypoint-initdb.d/
RUN chmod 0755 /docker-entrypoint-initdb.d/*.sh

CMD ["postgres", "-c", "config_file=/etc/postgresql/postgresql.conf"]
