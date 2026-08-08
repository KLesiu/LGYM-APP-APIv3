\set ON_ERROR_STOP on

\if :{?database_name}
\else
  \echo 'database_name is required'
  SELECT 1 / 0;
\endif
\if :{?target_environment}
\else
  \echo 'target_environment is required'
  SELECT 1 / 0;
\endif
\if :{?maintenance_role}
\else
  \echo 'maintenance_role is required'
  SELECT 1 / 0;
\endif
\if :{?runtime_role}
\else
  \echo 'runtime_role is required'
  SELECT 1 / 0;
\endif

BEGIN;
SELECT pg_advisory_xact_lock(hashtextextended('lgym.tutorial-row-security.rollout', 0));

SELECT current_database() = :'database_name' AS target_database_matches \gset
\if :target_database_matches
\else
  \echo 'Connected database does not match database_name.'
  SELECT 1 / 0;
\endif

SELECT lower(:'target_environment') IN ('development', 'staging', 'production') AS target_environment_is_known \gset
\if :target_environment_is_known
\else
  \echo 'target_environment must be Development, Staging, or Production.'
  SELECT 1 / 0;
\endif

WITH database_setting AS (
    SELECT split_part(setting.value, '=', 2) AS environment
    FROM pg_db_role_setting configured
    CROSS JOIN LATERAL unnest(configured.setconfig) AS setting(value)
    WHERE configured.setdatabase = (SELECT oid FROM pg_database WHERE datname = current_database())
      AND configured.setrole = 0
      AND split_part(setting.value, '=', 1) = 'lgym.deployment_environment'
)
SELECT COUNT(*) = 1
   AND lower(MAX(environment)) = lower(:'target_environment') AS database_environment_matches
FROM database_setting \gset
\if :database_environment_matches
\else
  \echo 'Stored database environment does not match target_environment.'
  SELECT 1 / 0;
\endif

SELECT current_user = :'maintenance_role' AS maintenance_connection_matches \gset
\if :maintenance_connection_matches
\else
  \echo 'Deactivation must run through the configured maintenance role.'
  SELECT 1 / 0;
\endif

SELECT EXISTS (
    SELECT 1
    FROM pg_roles
    WHERE rolname = :'maintenance_role'
      AND NOT rolsuper
      AND rolbypassrls
) AND EXISTS (
    SELECT 1
    FROM pg_roles
    WHERE rolname = :'runtime_role'
      AND NOT rolsuper
      AND NOT rolbypassrls
) AS role_configuration_matches \gset
\if :role_configuration_matches
\else
  \echo 'Maintenance/runtime role configuration is unsafe for tutorial RLS deactivation.'
  SELECT 1 / 0;
\endif

SELECT COUNT(*) = 2 AS protected_tables_owned_by_maintenance
FROM pg_class relation
JOIN pg_namespace namespace ON namespace.oid = relation.relnamespace
JOIN pg_roles owner ON owner.oid = relation.relowner
WHERE namespace.nspname = 'public'
  AND relation.relkind = 'r'
  AND relation.relname IN ('UserTutorialProgresses', 'UserTutorialStepProgresses')
  AND owner.rolname = :'maintenance_role' \gset
\if :protected_tables_owned_by_maintenance
\else
  \echo 'Both tutorial tables must exist and be owned by the configured maintenance role.'
  SELECT 1 / 0;
\endif

ALTER TABLE public."UserTutorialStepProgresses" NO FORCE ROW LEVEL SECURITY;
ALTER TABLE public."UserTutorialStepProgresses" DISABLE ROW LEVEL SECURITY;
ALTER TABLE public."UserTutorialProgresses" NO FORCE ROW LEVEL SECURITY;
ALTER TABLE public."UserTutorialProgresses" DISABLE ROW LEVEL SECURITY;

COMMIT;
