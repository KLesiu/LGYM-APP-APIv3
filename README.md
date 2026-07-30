# LGYM API (.NET)

Backend API for the LGYM application built on .NET 10, EF Core, and PostgreSQL.
The project exposes endpoints used by clients for user auth, training plans, workouts, exercises,
gyms, measurements, records, scores, and application configuration.

## What this project does

- Serves REST API endpoints for core LGYM domain modules.
- Handles authentication and user context with JWT.
- Persists application data in PostgreSQL through EF Core.
- Preserves legacy client contract shape (including `_id`, `msg`, and route conventions).

## Solution structure

The solution has 18 projects and 90 direct project references. The authoritative graph, dependency-first order, and forbidden complement are in `docs/modular-monolith/issue-380-project-reference-graph.md`.

- `LgymApi.Api` is the HTTP host and composition root.
- `LgymApi.Application` contains the remaining Reporting, Workout & Progress, Coaching, and Nutrition use cases.
- `LgymApi.Platform`, `LgymApi.Identity`, `LgymApi.TrainingPlanning`, and `LgymApi.Notifications` are stable module assemblies with small public facades and internal implementations.
- `LgymApi.Domain` contains entities, enums, IDs, and security constants.
- `LgymApi.Infrastructure` owns the shared EF runtime: one `AppDbContext`, one migration stream, the Unit of Work, Hangfire persistence, and module persistence bridges.
- `LgymApi.BackgroundWorker` executes durable jobs and selects no-op or Hangfire schedulers. `LgymApi.BackgroundWorker.Common` is the closed persisted-job and email-wire seam.
- `LgymApi.Resources` and `LgymApi.Resources.Generator` provide localized resources and their analyzer.
- `LgymApi.DataSeeder`, `LgymApi.DataSeeder.Tests`, `LgymApi.UnitTests`, `LgymApi.IntegrationTests`, `LgymApi.ArchitectureTests`, and `LgymApi.TestUtils` provide bootstrap and verification support.
- Project-level docs live next to each `.csproj` as `<ProjectName>.md`.

For contributor workflow, use the [module contribution guide](docs/MODULE_CONTRIBUTION_GUIDE.md) and [architecture overview](docs/ARCHITECTURE.md). [New modules usage notes](docs/NEW_MODULES_USAGE.md) cover feature and API usage, not contribution workflow.

## Requirements

- .NET SDK 10.x
- PostgreSQL

## Configuration

Configure via `appsettings.json` files or environment variables:

- API: `LgymApi.Api/appsettings.json`

Common environment variable overrides:

- `ConnectionStrings__Postgres`
- `Jwt__SigningKey`
- `PhotoStorage__Provider`
- `PhotoStorage__AccessKeyId`
- `PhotoStorage__SecretAccessKey`
- `PushNotifications__SendEnabled`
- `PushNotifications__StaleTokenCleanupEnabled`
- `PushNotifications__Fcm__ProjectId`
- `PushNotifications__Fcm__CredentialsPath`
- `PushNotifications__Fcm__CredentialsJson`

## Container runtime contract

The container image expects environment-specific config to be mounted from outside the image.
Do not bake secrets or site-specific values into the image.

- Mount the runtime config at `/run/config/appsettings.container.json`
- Set `LGYM_APP_CONFIG_PATH=/run/config/appsettings.container.json`
- Use `ASPNETCORE_ENVIRONMENT=Development` for local dev and `ASPNETCORE_ENVIRONMENT=Production` for production
- Publish the API on container port `8080` and map that port to a host port
- Set `ConnectionStrings__Postgres` from the runtime environment
- Set `Jwt__SigningKey` only if the mounted config does not already provide it
- Keep PostgreSQL outside this image; for a database running on the Docker host, use `host.docker.internal` from inside the container

The process runs from `/app` inside the container, so the mounted config and any relative paths must assume `/app` as the content root.
Avoid launch profile assumptions when testing the image.

## Push rollout and credentials

Push registration and push delivery are separated on purpose.

- `PushNotifications:SendEnabled=false` disables outbound push sends while keeping installation registration and token refresh active.
- `PushNotifications:StaleTokenCleanupEnabled=true` keeps the recurring stale-token tombstoning job active even when send delivery is disabled.
- Stale cleanup marks inactive rows with `DisabledAt` and `DisabledReason=InactiveStale`; it does not delete installation rows or historical push message audit data.

### Credentials by environment

Development:

- Keep `PushNotifications:SendEnabled=false` until local Firebase credentials are present.
- Prefer .NET user secrets or untracked environment variables for `PushNotifications__Fcm__CredentialsJson` or `PushNotifications__Fcm__CredentialsPath`.
- If you need end-to-end push locally, point `PushNotifications__Fcm__CredentialsPath` at an untracked service-account JSON file outside the repo.

Staging:

- Use a dedicated Firebase project and service account separate from production.
- Mount the service-account JSON from the host or secret store and set `PushNotifications__Fcm__CredentialsPath` to that mounted path.
- Keep `PushNotifications__SendEnabled=false` until test devices register successfully and the admin test-event path is verified.

Production:

- Use a production-only Firebase project/service account with least-privilege access for messaging.
- Prefer mounted secret files or a secret manager over inline JSON in committed config.
- Rotate credentials outside the repo and restart the API after secret replacement if your host does not support live secret reload.

Do not commit:

- Firebase service-account JSON files
- raw `PushNotifications__Fcm__CredentialsJson` values
- APNs `.p8` files, Apple key IDs, or Apple team IDs
- any environment-specific credentials in tracked `appsettings.*.json`

### Mobile iOS / APNs requirement

LGYM uses FCM tokens on both Android and iOS. iOS delivery still depends on APNs being configured in Firebase.

- Create a separate Firebase iOS app for each dev/staging/prod bundle setup you actually ship.
- Upload the matching APNs auth key to Firebase Project Settings -> Cloud Messaging.
- Keep APNs credentials in Apple/Firebase secret storage only; never in this repository.

### Operator runbook

Rollout:

1. Deploy config with `PushNotifications:SendEnabled=false` and stale cleanup enabled.
2. Confirm mobile native builds register installations successfully in the target environment.
3. Verify `/api/internal/push/test-event` creates queued/sent rows for a controlled test user.
4. Turn `PushNotifications:SendEnabled=true` in staging, validate delivery, then repeat in production.
5. Watch logs for `eventId`, `category`, and `provider status` fields plus disabled-installation counts from the stale cleanup job.

Troubleshooting:

1. Registration works but no push is sent: check `PushNotifications:SendEnabled`, Firebase project ID, and service-account secret mount.
2. iOS tokens register but devices never receive pushes: verify APNs auth key upload in Firebase and the shipped iOS bundle identifier.
3. Rows stay `Failed` with `InvalidToken`: the token was tombstoned immediately; wait for the device to re-register on next app open/token refresh.
4. Old devices should stop receiving pushes: confirm the `push-stale-installation-cleanup` recurring job is present and `StaleTokenCleanupEnabled=true`.
5. Investigating delivery issues: use `PushNotificationMessages` plus logs, not raw payload dumps; logs intentionally omit sensitive push payload content.

### Build the image

```bash
docker build -t lgym-api:test .
```

### Publish the image from GitHub Actions

The `.github/workflows/api-image.yml` workflow publishes the image only through manual `workflow_dispatch` from the repository default branch.
Each successful run automatically bumps the next patch semver image version based on existing Git tags matching `v*.*.*`.
If no matching tag exists yet, the first published version is `v0.1.0`.

Configure these GitHub repository variables:

- `DOCKERHUB_NAMESPACE` - Docker Hub namespace, usually your username or organization
- `DOCKERHUB_IMAGE_NAME` - API image name, for example `lgym-api`; optional because the workflow defaults to `lgym-api`
- `DOCKERHUB_USERNAME` - Docker Hub login username; optional if it is the same as `DOCKERHUB_NAMESPACE`

Configure this GitHub repository secret:

- `DOCKERHUB_TOKEN` - Docker Hub access token or password used by the workflow login step

Published tags include:

- the auto-generated semver tag, for example `v1.2.4`
- `latest`
- `sha-<short-sha>` for traceability

After publishing the image, the workflow also creates and pushes the matching Git tag so the next run can derive the next version deterministically.

Example image reference:

```bash
docker pull docker.io/<DOCKERHUB_NAMESPACE>/<DOCKERHUB_IMAGE_NAME>:latest
```

### Publish the logpanel image from GitHub Actions

The `.github/workflows/logpanel-image.yml` workflow publishes the logpanel image with the same manual `workflow_dispatch`, default-branch, Docker Hub push, summary output, and release-tag creation behavior as the API workflow.
It uses its own logpanel-only Git tag namespace (`logpanel-v*.*.*`) so logpanel releases do not share version history with the API image.
The API workflow still uses `DOCKERHUB_IMAGE_NAME`; the logpanel workflow uses `DOCKERHUB_LOGPANEL_IMAGE_NAME` so the two image names do not collide.

Configure these GitHub repository variables:

- `DOCKERHUB_NAMESPACE` - Docker Hub namespace, usually your username or organization
- `DOCKERHUB_LOGPANEL_IMAGE_NAME` - logpanel image name, for example `lgym-logpanel`; optional because the workflow defaults to `lgym-logpanel`
- `DOCKERHUB_USERNAME` - Docker Hub login username; optional if it is the same as `DOCKERHUB_NAMESPACE`

Configure this GitHub repository secret:

- `DOCKERHUB_TOKEN` - Docker Hub access token or password used by the workflow login step

Published logpanel tags include:

- the auto-generated semver tag in the `logpanel-v1.2.4` format
- `latest`
- `sha-<short-sha>` for traceability

After publishing the image, the workflow also creates and pushes the matching `logpanel-v*.*.*` Git tag so the next run can derive the next version deterministically without affecting API tags.

### Logpanel runtime URL

Set `LOGPANEL_PUBLIC_BASE_URL` when the logpanel image needs a public Kibana URL at runtime.
If the variable is set, the container writes `server.publicBaseUrl: "<value>"` into Kibana config on startup.
If it is unset or empty, the setting is omitted entirely.

Example:

```bash
LOGPANEL_PUBLIC_BASE_URL=https://log.lgym.ovh
```

### Logging stack SSO runbook

- Primary browser login is Google OIDC at `https://log.lgym.ovh/login` with Kibana provider `oidc.google_oidc` and the label `Continue with Google`.
- Break-glass recovery is `https://log.lgym.ovh/admin-login`, which uses nginx HTTP Basic auth and then deep-links to Kibana's native fallback provider `basic.basic1`.
- The tracked callback and logout endpoints are `https://log.lgym.ovh/api/security/oidc/callback` and `https://log.lgym.ovh/security/logged_out`.
- Secret injection points are operator-managed, not git-tracked:
  - `LOGGING_GOOGLE_OIDC_CLIENT_ID` and `LOGGING_GOOGLE_OIDC_GROUPS_CLAIM` are provided to `docker-compose.logging.yml`.
  - The Google OIDC client secret is stored in the Elasticsearch keystore outside git.
  - Break-glass htpasswd files are mounted on the proxy host (`/etc/nginx/.htpasswd-log.lgym.ovh` in production, `/demo/.htpasswd` in local demo).
  - TLS materials are mounted into nginx and the Elastic/Kibana cert paths from runtime secret storage.
- Rollback is the same recovery path in reverse: restore the last known good tracked `docker-compose.logging.yml`, `kibana.yml`, and `deploy/nginx/*.conf`, keep `/admin-login` available, restart the stack, and confirm `/login` renders Kibana before re-testing Google OIDC.
- `LOGPANEL_PUBLIC_BASE_URL` only sets Kibana `server.publicBaseUrl`; it does not replace the proxy basic-auth gate.

#### Kibana-to-Elasticsearch credentials

The secured logging stack now bootstraps credentials so Kibana can authenticate to Elasticsearch:

- `docker-compose.logging.yml` reads `LOGGING_ES_PASSWORD` and sets the Elasticsearch `elastic` superuser password (`ELASTIC_PASSWORD`).
- Kibana reads `LOGGING_ES_KIBANA_USERNAME` (default `elastic`) and `LOGGING_ES_KIBANA_PASSWORD` (default falls back to `LOGGING_ES_PASSWORD`) to authenticate to the secured cluster (`ELASTICSEARCH_USERNAME` / `ELASTICSEARCH_PASSWORD`).
- For production, prefer a dedicated `kibana_system` password or a Kibana service-account token over the `elastic` superuser; set `LOGGING_ES_KIBANA_USERNAME` and `LOGGING_ES_KIBANA_PASSWORD` accordingly.
- The all-in-one `docker/logpanel` image reads `LOGPANEL_ES_USERNAME` (default `kibana_system`) and `LOGPANEL_ES_PASSWORD` from its `kibana.yml` (`elasticsearch.username` / `elasticsearch.password`). At runtime the image requires `LOGPANEL_ES_PASSWORD` (and the matching ES-side password, e.g. `LOGGING_ES_PASSWORD` on the compose stack) to be supplied.

#### Required role-mapping step (operator-applied)

Viewer authorization is applied by an operator via the tracked script `deploy/es/apply-role-mapping.sh`, run after the stack is up. This is intentional: group claims are operator-managed and are not baked into git. The script maps the `google_oidc` realm AND the approved viewer group to the Elastic built-in `viewer` role (read-only Kibana access).

Exact command:

```bash
LOGGING_ES_PASSWORD=... bash deploy/es/apply-role-mapping.sh
```

Optional overrides: `LOGGING_ES_URL` (default `https://localhost:9200`), `LOGGING_ES_USER` (default `elastic`), `LOGGING_GOOGLE_OIDC_VIEWER_GROUP` (default `kibana-viewers@lgym.ovh`). The script fails closed if `LOGGING_ES_PASSWORD` is unset.

#### Local demo requirements

`docker-compose.logging.local-demo.yml` now REQUIRES `DEMO_BASIC_AUTH_PASSWORD` (the insecure `admin12345` default was removed). `DEMO_BASIC_AUTH_USER` still defaults to `admin`. The local demo boots Elasticsearch with a placeholder OIDC client_id (`__OIDC_DISABLED__`), so it does NOT perform real Google OIDC — it is for testing the nginx proxy and the break-glass path only.

#### Browser validation limitation

Full end-to-end Google OIDC browser validation requires operator-provided Google test accounts and break-glass credentials, which were not available in the automated review environment. The recorded evidence reflects that blocker honestly; the static config and runbook alignment are complete, but live `/login` and `/admin-login` browser QA could not be signed off here.

### Local development with an external PostgreSQL database

For a local PostgreSQL instance that is not running in Docker, copy the example environment file and update the database password/port/name:

```bash
cp .env.example .env
# edit .env
```

Then start only the API container:

```bash
docker compose -f docker-compose.external-db.yml up --build -d
```

The compose file does not start PostgreSQL. It connects the API container to the host machine through `host.docker.internal`, with a Linux-compatible `host-gateway` mapping.

Smoke check:

```bash
curl --fail http://localhost:18080/health/live
```

### Manual local run

Use the same image for local runs and point it at an external config file:

```bash
docker run -d --rm --name lgym-api-dev \
  -p 18080:8080 \
  --add-host=host.docker.internal:host-gateway \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e LGYM_APP_CONFIG_PATH=/run/config/appsettings.container.json \
  -e ConnectionStrings__Postgres='Host=host.docker.internal;Port=5432;Database=LGYM-APP;Username=postgres;Password=REPLACE_ME;TimeZone=Europe/Warsaw' \
  -e Jwt__SigningKey='REPLACE_ME_MIN_32_CHARS' \
  -v "$(pwd)/appsettings.container.example.json:/run/config/appsettings.container.json:ro" \
  lgym-api:test
```

Smoke check:

```bash
curl --fail http://localhost:18080/health/live
```

### Production

Use the same image and mount the production config from the host or secret store:

```bash
docker run -d --name lgym-api \
  -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e LGYM_APP_CONFIG_PATH=/run/config/appsettings.container.json \
  -e ConnectionStrings__Postgres='Host=PROD_DB_HOST;Port=5432;Database=LGYM-APP;Username=PROD_USER;Password=REPLACE_ME;TimeZone=Europe/Warsaw' \
  -e Jwt__SigningKey='REPLACE_ME_OR_SET_IN_CONFIG' \
  -v /etc/lgym-api/appsettings.container.json:/run/config/appsettings.container.json:ro \
  lgym-api:test
```

Smoke check:

```bash
curl --fail http://localhost:8080/health/live
```

## Quick start

Run the API:

```bash
dotnet run --project LgymApi.Api
```

## Report photo storage in development

LGYM supports two development storage modes for report photos:

- `PhotoStorage__Provider=Local` - default local dev storage written under `dev-photo-storage`.
- `PhotoStorage__Provider=CloudflareR2` - private Cloudflare R2 direct-upload flow for realistic end-to-end testing.

### Local storage

Use local storage when you want the API to serve development upload/read endpoints without cloud credentials.

```env
PhotoStorage__Provider=Local
```

### Cloudflare R2 development storage

Use Cloudflare R2 when you want the backend to generate signed PUT/GET URLs against the private development bucket.

Required environment variables or user-secrets:

```env
PhotoStorage__Provider=CloudflareR2
PhotoStorage__BucketName=YOUR_BUCKET_NAME
PhotoStorage__AccountId=YOUR_ACCOUNT_ID
PhotoStorage__Endpoint=https://YOUR_ACCOUNT_ID.r2.cloudflarestorage.com
PhotoStorage__AccessKeyId=<ACCESS_KEY_ID>
PhotoStorage__SecretAccessKey=<SECRET_ACCESS_KEY>
PhotoStorage__SignedUploadExpirationMinutes=10
PhotoStorage__SignedReadExpirationMinutes=15
PhotoStorage__MaxFileSizeBytes=5242880
PhotoStorage__AllowedMimeTypes__0=image/jpeg
PhotoStorage__AllowedMimeTypes__1=image/png
PhotoStorage__AllowedMimeTypes__2=image/heic
PhotoStorage__DevMaxTotalBytes=8589934592
PhotoStorage__DevMaxUploadsPerDay=200
PhotoStorage__DevMaxUploadInitPerUserPerHour=50
```

Do not commit:

- `PhotoStorage__AccessKeyId`
- `PhotoStorage__SecretAccessKey`
- any local secrets file containing those values

Development bucket notes:

- bucket stays private
- Public Development URL must remain disabled
- lifecycle rule deletes `photos/` objects after 7 days

### Manual smoke test

1. Start the backend with `PhotoStorage__Provider=CloudflareR2`.
2. Call `POST /api/trainee/reporting/photos/upload-init` or the trainer equivalent.
3. Confirm the response contains a signed PUT URL and backend-generated `storageKey` under `photos/`.
4. Upload a JPEG/PNG/HEIC file directly to the signed URL with HTTP `PUT`.
5. Confirm the object appears in Cloudflare R2 under the expected prefix.
6. Call `POST /api/trainee/reporting/photos/complete-upload`.
7. Confirm the photo metadata row appears in PostgreSQL.
8. Call the history or signed preview endpoint and open the signed GET URL.
9. Confirm the image loads from the private bucket.
10. Confirm an unauthorized user cannot get a signed read URL for that photo.

Important limitation:

- Cloudflare R2 presigned `PUT` does **not** guarantee max file size enforcement at storage level.
- LGYM validates declared size in `upload-init`, then verifies the real stored object size in `complete-upload` before saving metadata to PostgreSQL.
- If object metadata verification fails, backend rejects finalization and removes the invalid object.

## Persistence conventions (Unit of Work)

- Repositories stage changes (`Add`, `Update`, `Remove`) and do not call `SaveChangesAsync`.
- Application services define commit timing (`IUnitOfWork.SaveChangesAsync`) at use-case boundaries.
- Multi-step write flows use explicit `IUnitOfWork` transactions in services.
- Read-only queries should prefer `AsNoTracking()` unless tracking is required.
- The production application has one `AppDbContext`, one PostgreSQL database, and one migration stream. Each persisted entity still has one module owner. See `docs/modular-monolith/issue-376-ownership-map.md`; the executable source is `LgymApi.ArchitectureTests/PersistedEntityOwnershipCatalog.cs`.
- Known internal entity IDs use `Id<T>` and PostgreSQL `uuid` columns. API JSON UUID values remain strings. Only `PushNotificationMessage.EntityId` and `PushEventPayload.EntityId` are polymorphic string ID exceptions.

### PostgreSQL integration runner

The runner needs Docker, or an admin connection supplied through `-ConnectionString`. It scopes `LGYM_TEST_POSTGRES` to the test process, checks non-empty TRX counters, removes its Docker container, and the test fixture drops its leased `lgym_it_*` database.

```powershell
pwsh -File scripts/run-postgresql-integration-tests.ps1
```

## Notes

- Password verification uses legacy `passport-local-mongoose` PBKDF2 settings (sha256, 25000 iterations, keylen 512, hex).
- Known internal entity IDs use `Id<T>`; EF Core stores their provider values as PostgreSQL `uuid`, while API responses transport UUID values such as `_id` as strings. The only polymorphic string ID exceptions are `PushNotificationMessage.EntityId` and `PushEventPayload.EntityId`.
