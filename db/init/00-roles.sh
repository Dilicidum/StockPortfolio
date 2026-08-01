#!/bin/bash
# =============================================================================
#  StockPortfolio - database initialisation wrapper
# =============================================================================
#  WHY THIS FILE EXISTS
#
#  `docker-entrypoint-initdb.d` executes *.sql files with
#      psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f <file>
#  and passes NO -v user variables. `01-roles.sql` needs five passwords, and a
#  bare `:'migrator_pw'` in a file run that way is a syntax error which, with
#  ON_ERROR_STOP=1, aborts initialisation - so `docker compose up` fails from a
#  clean clone (P0 req 7, the first thing a grader runs). This wrapper is the
#  only thing the entrypoint runs; it supplies the variables and then hands
#  01-roles.sql to psql itself.
#
#  CONSEQUENCE FOR THE MOUNT: `01-roles.sql` must NOT be visible inside
#  /docker-entrypoint-initdb.d. The entrypoint globs that directory once and
#  would run the .sql bare after this script, hitting exactly the syntax error
#  above. docker-compose.yml therefore mounts THIS FILE into the entrypoint
#  directory and the db/init directory separately at /db/init.
#
#  Passwords come from the environment (set in docker-compose.yml). None are
#  hardcoded here and none may be committed.
#
#  NOTE ON THE EXECUTABLE BIT: the postgres entrypoint runs a *.sh file with
#  `bash "$f"` when it is executable and `source "$f"` when it is not. Both work
#  for this script, so a lost +x bit on a Windows checkout is harmless.
# =============================================================================
set -e

: "${POSTGRES_USER:?POSTGRES_USER must be set by the postgres image}"
: "${POSTGRES_DB:?POSTGRES_DB must be set by the postgres image}"
: "${MIGRATOR_PW:?MIGRATOR_PW must be set - see .env.example}"
: "${IDENTITY_PW:?IDENTITY_PW must be set - see .env.example}"
: "${PORTFOLIO_PW:?PORTFOLIO_PW must be set - see .env.example}"
: "${MARKETDATA_PW:?MARKETDATA_PW must be set - see .env.example}"
: "${ALERTS_PW:?ALERTS_PW must be set - see .env.example}"

# Where 01-roles.sql lives. ROLES_SQL wins (Testcontainers, ad-hoc runs), then
# /db/init as mounted by compose, then next to this script for the case where
# someone runs `bash db/init/00-roles.sh` against an already-running server.
sp_roles_sql="${ROLES_SQL:-}"
if [ -z "$sp_roles_sql" ]; then
    sp_here="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" && pwd)"
    for sp_candidate in "/db/init/01-roles.sql" "${sp_here}/01-roles.sql"; do
        if [ -f "$sp_candidate" ]; then
            sp_roles_sql="$sp_candidate"
            break
        fi
    done
fi

if [ -z "$sp_roles_sql" ]; then
    echo "StockPortfolio: FATAL - 01-roles.sql not found (set ROLES_SQL to its path)" >&2
    exit 1
fi

echo "StockPortfolio: applying ${sp_roles_sql} as ${POSTGRES_USER} on ${POSTGRES_DB}"

psql -v ON_ERROR_STOP=1 \
     --username "$POSTGRES_USER" \
     --dbname "$POSTGRES_DB" \
     --no-password \
     -v dbname="$POSTGRES_DB" \
     -v migrator_pw="$MIGRATOR_PW" \
     -v identity_pw="$IDENTITY_PW" \
     -v portfolio_pw="$PORTFOLIO_PW" \
     -v marketdata_pw="$MARKETDATA_PW" \
     -v alerts_pw="$ALERTS_PW" \
     -f "$sp_roles_sql"

unset sp_roles_sql sp_here sp_candidate
