\set ON_ERROR_STOP on

\if :{?database_name}
\else
  \echo 'database_name is required'
  SELECT 1 / 0;
\endif
\if :{?database_environment}
\else
  \echo 'database_environment is required'
  SELECT 1 / 0;
\endif
\if :{?maintenance_role}
\else
  \echo 'maintenance_role is required'
  SELECT 1 / 0;
\endif

SELECT lower(:'database_environment') IN ('development', 'staging', 'production') AS database_environment_is_known \gset
\if :database_environment_is_known
\else
  \echo 'database_environment must be Development, Staging, or Production.'
  SELECT 1 / 0;
\endif
\if :{?runtime_role}
\else
  \echo 'runtime_role is required'
  SELECT 1 / 0;
\endif

SELECT format('CREATE ROLE %I LOGIN NOSUPERUSER BYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION', :'maintenance_role')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'maintenance_role')
\gexec
SELECT format('CREATE ROLE %I LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT', :'runtime_role')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'runtime_role')
\gexec

ALTER ROLE :"maintenance_role" NOSUPERUSER BYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
ALTER ROLE :"runtime_role" NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT;
REVOKE :"maintenance_role" FROM :"runtime_role";
ALTER DATABASE :"database_name" OWNER TO :"maintenance_role";
SELECT format(
    'ALTER DATABASE %I SET lgym.deployment_environment TO %L',
    :'database_name',
    lower(:'database_environment'))
\gexec
CREATE SCHEMA IF NOT EXISTS hangfire AUTHORIZATION :"maintenance_role";
SELECT format('ALTER TABLE %I.%I OWNER TO %I', schemaname, tablename, :'maintenance_role')
FROM pg_tables
WHERE schemaname = 'public'
  AND tablename IN ('UserTutorialProgresses', 'UserTutorialStepProgresses')
\gexec

GRANT CONNECT ON DATABASE :"database_name" TO :"runtime_role";
GRANT USAGE ON SCHEMA public, hangfire TO :"runtime_role";
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public, hangfire TO :"runtime_role";
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public, hangfire TO :"runtime_role";
ALTER DEFAULT PRIVILEGES FOR ROLE :"maintenance_role" IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"runtime_role";
ALTER DEFAULT PRIVILEGES FOR ROLE :"maintenance_role" IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO :"runtime_role";
ALTER DEFAULT PRIVILEGES FOR ROLE :"maintenance_role" IN SCHEMA hangfire GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"runtime_role";
ALTER DEFAULT PRIVILEGES FOR ROLE :"maintenance_role" IN SCHEMA hangfire GRANT USAGE, SELECT ON SEQUENCES TO :"runtime_role";

WITH RECURSIVE memberships(role_id) AS (
    SELECT roleid FROM pg_auth_members WHERE member = (SELECT oid FROM pg_roles WHERE rolname = :'runtime_role')
    UNION
    SELECT membership.roleid FROM pg_auth_members membership JOIN memberships ON membership.member = memberships.role_id
)
SELECT EXISTS (SELECT 1 FROM memberships WHERE role_id = (SELECT oid FROM pg_roles WHERE rolname = :'maintenance_role')) AS runtime_can_set_maintenance
\gset
\if :runtime_can_set_maintenance
  \echo 'Runtime role has a membership path to the maintenance role.'
  SELECT 1 / 0;
\endif
