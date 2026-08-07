using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LgymApi.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260807160000_AddTutorialRowSecurityPolicies")]
    public partial class AddTutorialRowSecurityPolicies : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public."UserTutorialProgresses" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE public."UserTutorialProgresses" DISABLE ROW LEVEL SECURITY;
                ALTER TABLE public."UserTutorialStepProgresses" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE public."UserTutorialStepProgresses" DISABLE ROW LEVEL SECURITY;

                CREATE POLICY "user_tutorial_progresses_actor_select"
                    ON public."UserTutorialProgresses"
                    FOR SELECT TO PUBLIC
                    USING (
                        "UserId" = CASE
                            WHEN current_setting('lgym.account_id', true) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                                THEN current_setting('lgym.account_id', true)::uuid
                            ELSE NULL
                        END
                    );

                CREATE POLICY "user_tutorial_progresses_actor_insert"
                    ON public."UserTutorialProgresses"
                    FOR INSERT TO PUBLIC
                    WITH CHECK (
                        "UserId" = CASE
                            WHEN current_setting('lgym.account_id', true) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                                THEN current_setting('lgym.account_id', true)::uuid
                            ELSE NULL
                        END
                    );

                CREATE POLICY "user_tutorial_progresses_actor_update"
                    ON public."UserTutorialProgresses"
                    FOR UPDATE TO PUBLIC
                    USING (
                        "UserId" = CASE
                            WHEN current_setting('lgym.account_id', true) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                                THEN current_setting('lgym.account_id', true)::uuid
                            ELSE NULL
                        END
                    )
                    WITH CHECK (
                        "UserId" = CASE
                            WHEN current_setting('lgym.account_id', true) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                                THEN current_setting('lgym.account_id', true)::uuid
                            ELSE NULL
                        END
                    );

                CREATE POLICY "user_tutorial_progresses_actor_delete"
                    ON public."UserTutorialProgresses"
                    FOR DELETE TO PUBLIC
                    USING (
                        "UserId" = CASE
                            WHEN current_setting('lgym.account_id', true) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                                THEN current_setting('lgym.account_id', true)::uuid
                            ELSE NULL
                        END
                    );

                CREATE POLICY "user_tutorial_step_progresses_actor_select"
                    ON public."UserTutorialStepProgresses"
                    FOR SELECT TO PUBLIC
                    USING (
                        EXISTS (
                            SELECT 1
                            FROM public."UserTutorialProgresses" AS progress
                            WHERE progress."Id" = "UserTutorialProgressId"
                              AND progress."UserId" = CASE
                                  WHEN current_setting('lgym.account_id', true) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                                      THEN current_setting('lgym.account_id', true)::uuid
                                  ELSE NULL
                              END
                        )
                    );

                CREATE POLICY "user_tutorial_step_progresses_actor_insert"
                    ON public."UserTutorialStepProgresses"
                    FOR INSERT TO PUBLIC
                    WITH CHECK (
                        EXISTS (
                            SELECT 1
                            FROM public."UserTutorialProgresses" AS progress
                            WHERE progress."Id" = "UserTutorialProgressId"
                              AND progress."UserId" = CASE
                                  WHEN current_setting('lgym.account_id', true) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                                      THEN current_setting('lgym.account_id', true)::uuid
                                  ELSE NULL
                              END
                        )
                    );

                CREATE POLICY "user_tutorial_step_progresses_actor_update"
                    ON public."UserTutorialStepProgresses"
                    FOR UPDATE TO PUBLIC
                    USING (
                        EXISTS (
                            SELECT 1
                            FROM public."UserTutorialProgresses" AS progress
                            WHERE progress."Id" = "UserTutorialProgressId"
                              AND progress."UserId" = CASE
                                  WHEN current_setting('lgym.account_id', true) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                                      THEN current_setting('lgym.account_id', true)::uuid
                                  ELSE NULL
                              END
                        )
                    )
                    WITH CHECK (
                        EXISTS (
                            SELECT 1
                            FROM public."UserTutorialProgresses" AS progress
                            WHERE progress."Id" = "UserTutorialProgressId"
                              AND progress."UserId" = CASE
                                  WHEN current_setting('lgym.account_id', true) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                                      THEN current_setting('lgym.account_id', true)::uuid
                                  ELSE NULL
                              END
                        )
                    );

                CREATE POLICY "user_tutorial_step_progresses_actor_delete"
                    ON public."UserTutorialStepProgresses"
                    FOR DELETE TO PUBLIC
                    USING (
                        EXISTS (
                            SELECT 1
                            FROM public."UserTutorialProgresses" AS progress
                            WHERE progress."Id" = "UserTutorialProgressId"
                              AND progress."UserId" = CASE
                                  WHEN current_setting('lgym.account_id', true) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                                      THEN current_setting('lgym.account_id', true)::uuid
                                  ELSE NULL
                              END
                        )
                    );
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public."UserTutorialStepProgresses" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE public."UserTutorialStepProgresses" DISABLE ROW LEVEL SECURITY;
                ALTER TABLE public."UserTutorialProgresses" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE public."UserTutorialProgresses" DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS "user_tutorial_step_progresses_actor_delete" ON public."UserTutorialStepProgresses";
                DROP POLICY IF EXISTS "user_tutorial_step_progresses_actor_update" ON public."UserTutorialStepProgresses";
                DROP POLICY IF EXISTS "user_tutorial_step_progresses_actor_insert" ON public."UserTutorialStepProgresses";
                DROP POLICY IF EXISTS "user_tutorial_step_progresses_actor_select" ON public."UserTutorialStepProgresses";
                DROP POLICY IF EXISTS "user_tutorial_progresses_actor_delete" ON public."UserTutorialProgresses";
                DROP POLICY IF EXISTS "user_tutorial_progresses_actor_update" ON public."UserTutorialProgresses";
                DROP POLICY IF EXISTS "user_tutorial_progresses_actor_insert" ON public."UserTutorialProgresses";
                DROP POLICY IF EXISTS "user_tutorial_progresses_actor_select" ON public."UserTutorialProgresses";
                """);
        }
    }
}
