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

SELECT lower(:'target_environment') = 'staging' AS target_environment_is_staging \gset
\if :target_environment_is_staging
\else
  \echo 'Tutorial RLS activation is staging-only until the Task 18 production go/no-go.'
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
  \echo 'Activation must run through the configured maintenance role.'
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
  \echo 'Maintenance/runtime role configuration is unsafe for tutorial RLS activation.'
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

WITH expressions(parent_expression, child_expression) AS (
    VALUES (
        $policy$("UserId" =
        CASE
            WHEN (current_setting('lgym.account_id'::text, true) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'::text) THEN (current_setting('lgym.account_id'::text, true))::uuid
            ELSE NULL::uuid
        END)$policy$,
        $policy$(EXISTS ( SELECT 1
           FROM "UserTutorialProgresses" progress
          WHERE ((progress."Id" = "UserTutorialStepProgresses"."UserTutorialProgressId") AND (progress."UserId" =
                CASE
                    WHEN (current_setting('lgym.account_id'::text, true) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'::text) THEN (current_setting('lgym.account_id'::text, true))::uuid
                    ELSE NULL::uuid
                END))))$policy$
    )
), normalized(parent_expression, child_expression) AS (
    SELECT regexp_replace(parent_expression, '[[:space:]]+', '', 'g'),
           regexp_replace(child_expression, '[[:space:]]+', '', 'g')
    FROM expressions
), expected(table_name, policy_name, policy_command, policy_roles, is_permissive, using_expression, check_expression) AS (
    SELECT 'UserTutorialProgresses', 'user_tutorial_progresses_actor_select', 'r', ARRAY['PUBLIC']::text[], true, parent_expression, NULL FROM normalized
    UNION ALL SELECT 'UserTutorialProgresses', 'user_tutorial_progresses_actor_insert', 'a', ARRAY['PUBLIC']::text[], true, NULL, parent_expression FROM normalized
    UNION ALL SELECT 'UserTutorialProgresses', 'user_tutorial_progresses_actor_update', 'w', ARRAY['PUBLIC']::text[], true, parent_expression, parent_expression FROM normalized
    UNION ALL SELECT 'UserTutorialProgresses', 'user_tutorial_progresses_actor_delete', 'd', ARRAY['PUBLIC']::text[], true, parent_expression, NULL FROM normalized
    UNION ALL SELECT 'UserTutorialStepProgresses', 'user_tutorial_step_progresses_actor_select', 'r', ARRAY['PUBLIC']::text[], true, child_expression, NULL FROM normalized
    UNION ALL SELECT 'UserTutorialStepProgresses', 'user_tutorial_step_progresses_actor_insert', 'a', ARRAY['PUBLIC']::text[], true, NULL, child_expression FROM normalized
    UNION ALL SELECT 'UserTutorialStepProgresses', 'user_tutorial_step_progresses_actor_update', 'w', ARRAY['PUBLIC']::text[], true, child_expression, child_expression FROM normalized
    UNION ALL SELECT 'UserTutorialStepProgresses', 'user_tutorial_step_progresses_actor_delete', 'd', ARRAY['PUBLIC']::text[], true, child_expression, NULL FROM normalized
), actual(table_name, policy_name, policy_command, policy_roles, is_permissive, using_expression, check_expression) AS (
    SELECT relation.relname,
           policy.polname,
           policy.polcmd::text,
           ARRAY(
               SELECT CASE WHEN policy_role.role_oid = 0 THEN 'PUBLIC' ELSE role.rolname END
               FROM unnest(policy.polroles) AS policy_role(role_oid)
               LEFT JOIN pg_roles role ON role.oid = policy_role.role_oid
               ORDER BY 1),
           policy.polpermissive,
           regexp_replace(pg_get_expr(policy.polqual, policy.polrelid), '[[:space:]]+', '', 'g'),
           regexp_replace(pg_get_expr(policy.polwithcheck, policy.polrelid), '[[:space:]]+', '', 'g')
    FROM pg_policy policy
    JOIN pg_class relation ON relation.oid = policy.polrelid
    JOIN pg_namespace namespace ON namespace.oid = relation.relnamespace
    WHERE namespace.nspname = 'public'
      AND relation.relname IN ('UserTutorialProgresses', 'UserTutorialStepProgresses')
)
SELECT NOT EXISTS (
    (SELECT * FROM expected EXCEPT SELECT * FROM actual)
    UNION ALL
    (SELECT * FROM actual EXCEPT SELECT * FROM expected)
) AS policy_contract_matches \gset
\if :policy_contract_matches
\else
  \echo 'Tutorial RLS policy contract does not match the dormant migration.'
  SELECT 1 / 0;
\endif

ALTER TABLE public."UserTutorialProgresses" ENABLE ROW LEVEL SECURITY;
ALTER TABLE public."UserTutorialProgresses" FORCE ROW LEVEL SECURITY;
ALTER TABLE public."UserTutorialStepProgresses" ENABLE ROW LEVEL SECURITY;
ALTER TABLE public."UserTutorialStepProgresses" FORCE ROW LEVEL SECURITY;

COMMIT;
