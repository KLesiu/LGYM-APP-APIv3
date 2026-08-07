\set ON_ERROR_STOP on

\if :{?database_name}
\else
  \echo 'database_name is required'
  \quit 3
\endif
\if :{?target_environment}
\else
  \echo 'target_environment is required'
  \quit 3
\endif
\if :{?maintenance_role}
\else
  \echo 'maintenance_role is required'
  \quit 3
\endif
\if :{?runtime_role}
\else
  \echo 'runtime_role is required'
  \quit 3
\endif

BEGIN;
SELECT pg_advisory_xact_lock(hashtextextended('lgym.tutorial-row-security.rollout', 0));

SELECT current_database() = :'database_name' AS target_database_matches \gset
\if :target_database_matches
\else
  \echo 'Connected database does not match database_name.'
  \quit 4
\endif

SELECT lower(:'target_environment') IN ('development', 'staging', 'production') AS target_environment_is_known \gset
\if :target_environment_is_known
\else
  \echo 'target_environment must be Development, Staging, or Production.'
  \quit 4
\endif

SELECT current_user = :'maintenance_role' AS maintenance_connection_matches \gset
\if :maintenance_connection_matches
\else
  \echo 'Deactivation must run through the configured maintenance role.'
  \quit 4
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
  \quit 4
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
  \quit 4
\endif

ALTER TABLE public."UserTutorialStepProgresses" NO FORCE ROW LEVEL SECURITY;
ALTER TABLE public."UserTutorialStepProgresses" DISABLE ROW LEVEL SECURITY;
ALTER TABLE public."UserTutorialProgresses" NO FORCE ROW LEVEL SECURITY;
ALTER TABLE public."UserTutorialProgresses" DISABLE ROW LEVEL SECURITY;

COMMIT;
