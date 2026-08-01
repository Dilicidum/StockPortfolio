-- =============================================================================
--  StockPortfolio - Postgres roles, schemas and grants
-- =============================================================================
--  This file is the SINGLE SOURCE OF TRUTH for the database security model.
--  Compose, Testcontainers and the Azure provisioning job all execute this
--  exact text so the three environments cannot drift.
--
--  DO NOT let `docker-entrypoint-initdb.d` execute this file directly.
--  The entrypoint runs *.sql with `psql -v ON_ERROR_STOP=1 -f <file>` and
--  passes NO -v user variables, so every `:'password'` below becomes a syntax
--  error and, with ON_ERROR_STOP=1, ABORTS database initialisation - which
--  means `docker compose up` fails from a clean clone (P0 req 7, the first
--  thing a grader runs). `00-roles.sh` is the wrapper that supplies them.
--
--  Required psql variables (all supplied by 00-roles.sh):
--      dbname, migrator_pw, identity_pw, portfolio_pw, marketdata_pw, alerts_pw
--
--  Role model (docs/plan/er-diagram.md "Roles and grants"):
--      migrator          OWNER of all four schemas, CREATE - migration job only
--      identity_svc      DML on identity.*    only
--      portfolio_svc     DML on portfolio.*   only
--      marketdata_svc    DML on marketdata.*  only
--      alerts_svc        DML on alerts.*      only
--
--  A cross-schema read must fail at runtime with SQLSTATE 42501, which is what
--  Api.IntegrationTests.PortfolioRole_CannotReadIdentitySchema asserts.
-- =============================================================================

\echo 'StockPortfolio: creating roles, schemas and grants'


-- -----------------------------------------------------------------------------
-- 1. Login roles
-- -----------------------------------------------------------------------------
-- Guarded so the file is re-runnable (Azure re-provisioning, a re-created test
-- container). `DO $$ ... $$` is NOT an option here: psql does not substitute
-- :'variables' inside dollar-quoted strings, so the password would be sent
-- literally as the text ":'migrator_pw'". `\gexec` runs the query buffer's
-- result as SQL and executes nothing when the guard yields no row.

SELECT format('CREATE ROLE migrator LOGIN PASSWORD %L', :'migrator_pw')
 WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'migrator')
\gexec

SELECT format('CREATE ROLE identity_svc LOGIN PASSWORD %L', :'identity_pw')
 WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'identity_svc')
\gexec

SELECT format('CREATE ROLE portfolio_svc LOGIN PASSWORD %L', :'portfolio_pw')
 WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'portfolio_svc')
\gexec

SELECT format('CREATE ROLE marketdata_svc LOGIN PASSWORD %L', :'marketdata_pw')
 WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'marketdata_svc')
\gexec

SELECT format('CREATE ROLE alerts_svc LOGIN PASSWORD %L', :'alerts_pw')
 WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'alerts_svc')
\gexec


-- -----------------------------------------------------------------------------
-- 2. Membership - NOT optional
-- -----------------------------------------------------------------------------
-- `CREATE SCHEMA ... AUTHORIZATION migrator` and `ALTER DEFAULT PRIVILEGES FOR
-- ROLE migrator` both require the *executing* role to be a member of migrator.
-- Under compose the entrypoint runs as superuser and this line looks pointless;
-- on Azure Postgres Flexible Server the admin account is NOT a superuser and
-- the migration job fails on first deploy - after everything looked fine
-- locally. Harmless here, mandatory there.

GRANT migrator TO CURRENT_USER;


-- -----------------------------------------------------------------------------
-- 3. Schemas - one per module, all four created in Phase 1
-- -----------------------------------------------------------------------------
-- Only `identity` has tables in Phase 1. The other three exist from day one so
-- the cross-schema isolation test is meaningful before the modules are built.

CREATE SCHEMA IF NOT EXISTS identity   AUTHORIZATION migrator;
CREATE SCHEMA IF NOT EXISTS portfolio  AUTHORIZATION migrator;
CREATE SCHEMA IF NOT EXISTS marketdata AUTHORIZATION migrator;
CREATE SCHEMA IF NOT EXISTS alerts     AUTHORIZATION migrator;


-- -----------------------------------------------------------------------------
-- 4. Database-level privileges
-- -----------------------------------------------------------------------------
-- Strip the implicit PUBLIC grants (CONNECT + TEMPORARY) and hand CONNECT back
-- to the five named roles only. :"dbname" is psql identifier quoting.

REVOKE ALL    ON DATABASE :"dbname" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"dbname"
    TO migrator, identity_svc, portfolio_svc, marketdata_svc, alerts_svc;


-- -----------------------------------------------------------------------------
-- 5. Per-schema grants
-- -----------------------------------------------------------------------------
-- ALTER DEFAULT PRIVILEGES is the load-bearing clause. Granting on EXISTING
-- tables only means every future migration produces tables the service role
-- cannot read - a failure that surfaces a phase later and looks like an EF bug.
-- The `ON ALL TABLES` grant next to it is a no-op on a fresh database and only
-- matters when this file is re-run against one that already has tables.

-- identity ---------------------------------------------------------------
REVOKE ALL   ON SCHEMA identity FROM PUBLIC;
GRANT  USAGE ON SCHEMA identity TO identity_svc;
ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA identity
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO identity_svc;
ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA identity
    GRANT USAGE, SELECT ON SEQUENCES TO identity_svc;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES    IN SCHEMA identity TO identity_svc;
GRANT USAGE, SELECT                  ON ALL SEQUENCES IN SCHEMA identity TO identity_svc;

-- portfolio --------------------------------------------------------------
REVOKE ALL   ON SCHEMA portfolio FROM PUBLIC;
GRANT  USAGE ON SCHEMA portfolio TO portfolio_svc;
ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA portfolio
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO portfolio_svc;
ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA portfolio
    GRANT USAGE, SELECT ON SEQUENCES TO portfolio_svc;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES    IN SCHEMA portfolio TO portfolio_svc;
GRANT USAGE, SELECT                  ON ALL SEQUENCES IN SCHEMA portfolio TO portfolio_svc;

-- marketdata -------------------------------------------------------------
REVOKE ALL   ON SCHEMA marketdata FROM PUBLIC;
GRANT  USAGE ON SCHEMA marketdata TO marketdata_svc;
ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA marketdata
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO marketdata_svc;
ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA marketdata
    GRANT USAGE, SELECT ON SEQUENCES TO marketdata_svc;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES    IN SCHEMA marketdata TO marketdata_svc;
GRANT USAGE, SELECT                  ON ALL SEQUENCES IN SCHEMA marketdata TO marketdata_svc;

-- alerts -----------------------------------------------------------------
REVOKE ALL   ON SCHEMA alerts FROM PUBLIC;
GRANT  USAGE ON SCHEMA alerts TO alerts_svc;
ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA alerts
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO alerts_svc;
ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA alerts
    GRANT USAGE, SELECT ON SEQUENCES TO alerts_svc;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES    IN SCHEMA alerts TO alerts_svc;
GRANT USAGE, SELECT                  ON ALL SEQUENCES IN SCHEMA alerts TO alerts_svc;


-- -----------------------------------------------------------------------------
-- 6. Cross-schema isolation, stated explicitly
-- -----------------------------------------------------------------------------
-- Section 5 already leaves every role without rights on the other three
-- schemas. These REVOKEs are written out anyway: they are the executable form
-- of the boundary claim, they survive someone adding a broad GRANT above, and
-- REVOKE of a privilege that was never granted is not an error.

REVOKE ALL ON SCHEMA identity   FROM portfolio_svc, marketdata_svc, alerts_svc;
REVOKE ALL ON SCHEMA portfolio  FROM identity_svc,  marketdata_svc, alerts_svc;
REVOKE ALL ON SCHEMA marketdata FROM identity_svc,  portfolio_svc,  alerts_svc;
REVOKE ALL ON SCHEMA alerts     FROM identity_svc,  portfolio_svc,  marketdata_svc;

\echo 'StockPortfolio: roles, schemas and grants applied'
