# Report Photos Storage - Architecture and Production Deployment

This document describes how LGYM should store report photos in production, how the current development flow works, which cloud provider is recommended, and how to deploy the feature safely.

## Objective

Provide a production-ready storage layer for report photos used by:

- trainee report requests,
- trainer report review,
- photo history,
- signed upload URLs,
- signed read URLs.

The target design must work for:

- mobile native clients,
- `mobile` running via `npm run web`,
- backend-hosted signed URL generation,
- secure private photo access.

---

## Current State

The photo flow in the API already exists conceptually:

1. client calls `POST /api/trainee/reporting/photos/upload-init`,
2. API validates access and file metadata,
3. API generates a signed upload URL,
4. client uploads binary directly to storage,
5. client calls `POST /api/trainee/reporting/photos/complete-upload`,
6. photo metadata is saved in the database,
7. read URLs are generated later for preview/history.

### Important note about the current local mode

In development, LGYM currently uses `LocalPhotoStorageProvider`.

This provider is **development-only** and must **not** be used in production.

For local work we now support API-hosted dev storage endpoints:

- `PUT /dev/photos/upload/{storageKey}`
- `GET /dev/photos/read/{storageKey}`

Those endpoints write files to a local folder so the flow can work without an external storage server.

---

## Recommendation

## Recommended cloud: **Cloudflare R2**

Cloudflare R2 is the recommended default for LGYM production photo storage.

### Why R2

R2 fits this use case well because:

- it supports S3-compatible APIs,
- it works naturally with signed URLs,
- it is good for private object storage,
- egress pricing is usually better than classic S3-heavy setups,
- it is simple for mobile/web direct upload patterns,
- it scales without adding a custom file server.

### Good alternatives

If R2 is not acceptable, the next best options are:

1. **AWS S3** - safest mainstream option, strongest ecosystem.
2. **Supabase Storage** - good if the rest of the platform already standardizes on Supabase.
3. **Azure Blob Storage** - good only if the platform is already Azure-first.

### Recommendation summary

- **Default**: Cloudflare R2
- **Enterprise fallback**: AWS S3
- **Platform-coupled fallback**: Supabase Storage

---

## Recommended Production Architecture

```text
Mobile / Web Client
    |
    | 1. POST /api/trainee/reporting/photos/upload-init
    v
LGYM API
    |
    | 2. Generate signed PUT URL
    v
Cloud Storage (R2 / S3)
    ^
    | 3. PUT binary directly to storage
    |
Client
    |
    | 4. POST /api/trainee/reporting/photos/complete-upload
    v
LGYM API + PostgreSQL
    |
    | 5. GET history / preview
    v
Signed GET URL from storage
```

### Core rule

The API should store **metadata only** in PostgreSQL.

PostgreSQL should contain:

- `StorageKey`
- `MimeType`
- `SizeBytes`
- `Checksum`
- `ViewType`
- `ReportRequestId`
- `UploaderUserId`
- `OwnerUserId`
- optional `ThumbnailStorageKey`

Binary photo files should live only in object storage.

---

## Privacy and Access Model

Report photos are sensitive progress data and should be treated as private objects.

### Production rules

- bucket/container must be **private**,
- no public object URLs,
- read access only through short-lived signed URLs,
- upload access only through short-lived signed PUT URLs,
- API must authorize every `upload-init`, `complete-upload`, and `history` request,
- `storageKey` from client must never be treated as proof of ownership.

### Authorization rules

Allowed viewers:

- trainee who owns the photo,
- assigned trainer linked to that trainee,
- optionally admin only if explicitly approved later.

---

## Bucket / Object Layout

Recommended object key format:

```text
photos/{traineeId}/{reportRequestId}/{viewType}/{timestamp}-{uuid}.{ext}
```

This matches the current backend pattern.

### Example

```text
photos/39ffc8b2-1a7e-49da-8903-fd09a9a38b56/2320b3c8-4459-489b-b477-c07b3a07a97a/Front/2026-06-19T12-40-10Z-14b52d559ebf48b79d15828a1ec3cd3a.jpg
```

### Thumbnails

Recommended thumbnail key format:

```text
photos-thumbnails/{traineeId}/{reportRequestId}/{viewType}/{timestamp}-{uuid}.jpg
```

Thumbnails should be generated asynchronously after upload completion.

---

## Production Backend Changes Required

## 1. Add a real cloud storage provider

Create a new provider, for example:

- `LgymApi.Infrastructure/Services/CloudflareR2PhotoStorageProvider.cs`

This provider must implement `IPhotoStorageProvider`:

- `GenerateSignedUploadUrlAsync(...)`
- `GenerateSignedReadUrlAsync(...)`
- `DeleteAsync(...)`
- `GetMetadataAsync(...)`

## 2. Replace dev-only DI registration

Current registration in `LgymApi.Infrastructure/ServiceCollectionExtensions.cs` points to `LocalPhotoStorageProvider`.

Production should select provider by config, for example:

- `LocalPhotoStorageProvider` in Development,
- `CloudflareR2PhotoStorageProvider` in Production.

Recommended strategy:

```text
PhotoStorage:Provider = Local | CloudflareR2 | S3 | Supabase
```

Then register implementation conditionally.

`Local` provider should be allowed only in:

- `Development`,
- optionally `Testing`.

It should be treated as invalid for production startup.

## 3. Keep local provider for Development only

Do not remove the local provider.

It is useful for:

- local API development,
- `mobile` web mode,
- smoke testing without cloud credentials,
- CI scenarios if needed.

---

## Recommended Configuration Model

Add a config section like this:

```json
{
  "PhotoStorage": {
    "Provider": "CloudflareR2",
    "BucketName": "lgym-report-photos",
    "PublicBaseUrl": "",
    "AccountId": "YOUR_R2_ACCOUNT_ID",
    "AccessKeyId": "YOUR_R2_ACCESS_KEY",
    "SecretAccessKey": "YOUR_R2_SECRET_KEY",
    "Endpoint": "https://YOUR_ACCOUNT_ID.r2.cloudflarestorage.com",
    "SignedUploadExpirationMinutes": 15,
    "SignedReadExpirationMinutes": 60,
    "MaxFileSizeBytes": 10485760,
    "AllowedMimeTypes": ["image/jpeg", "image/png", "image/heic"]
  }
}
```

### Environment variable equivalents

```text
PhotoStorage__Provider=CloudflareR2
PhotoStorage__BucketName=lgym-report-photos
PhotoStorage__AccountId=...
PhotoStorage__AccessKeyId=...
PhotoStorage__SecretAccessKey=...
PhotoStorage__Endpoint=https://...
PhotoStorage__SignedUploadExpirationMinutes=15
PhotoStorage__SignedReadExpirationMinutes=60
PhotoStorage__MaxFileSizeBytes=10485760
```

### Secret handling

Never commit these values into the repo:

- `AccessKeyId`
- `SecretAccessKey`
- any signing secret or bucket admin key

Use:

- mounted config in container runtime,
- secret manager,
- CI/CD secret variables,
- Kubernetes secrets / Docker secrets / cloud secret store.

---

## Cloudflare R2 Deployment Plan

## Step 1 - Create bucket

Create a private bucket, e.g.:

```text
lgym-report-photos
```

Recommended settings:

- private bucket,
- versioning optional,
- lifecycle rules enabled,
- no public listing,
- no public read.

## Step 2 - Create service credentials

Create R2 API credentials with access limited to the photo bucket.

Grant only what is needed:

- object read,
- object write,
- object delete.

Avoid broad account-wide permissions if bucket-scoped permissions are available.

## Step 3 - Implement provider

Implement `CloudflareR2PhotoStorageProvider` with S3-compatible signing.

Responsibilities:

- generate signed PUT URLs,
- generate signed GET URLs,
- resolve metadata,
- delete objects,
- support mandatory finalize-time metadata verification.

## Step 4 - Register provider conditionally

In the infrastructure composition layer, keep this branch inside the reporting module helper or the shared platform helper that owns photo storage wiring:

- `Development` -> local provider,
- `Production` -> R2 provider.

## Step 5 - Configure client CORS on bucket

R2/S3 storage must allow browser uploads from your clients.

Allow origins such as:

- production mobile-web host,
- production web app host,
- local development hosts if needed.

Typical allowed methods:

- `PUT`
- `GET`
- `HEAD`

Typical allowed headers:

- `Content-Type`

If the final upload implementation adds provider-specific signed headers, the bucket CORS policy must explicitly allow those exact headers too.

Only allow the minimum necessary origins.

## Step 6 - Production smoke test

Test full flow:

1. `upload-init` returns signed PUT URL,
2. browser/mobile can upload binary,
3. `complete-upload` persists DB record,
4. `history` returns signed read URL,
5. preview image loads successfully.

---

## Should the API Proxy Binary Uploads?

### Recommended answer: **No**

Do not proxy the full binary upload through LGYM API in production.

### Why not

If API proxies binaries:

- API bandwidth cost increases,
- API CPU/memory usage increases,
- request time increases,
- horizontal scaling becomes more expensive,
- large upload retries hit the API directly.

### Better design

Keep API responsible for:

- auth,
- signed URL creation,
- metadata validation,
- DB finalize step.

Keep cloud storage responsible for:

- actual file transfer,
- object persistence,
- read delivery.

---

## Recommended Rollout Strategy

## Phase 1 - Development complete

- local dev storage works through API-hosted `/dev/photos/...` endpoints,
- mobile app can test flow without real cloud storage.

## Phase 2 - Staging with real bucket

- deploy staging API with `PhotoStorage__Provider=CloudflareR2`,
- use a dedicated staging bucket,
- validate signed URL uploads from web and mobile.

## Phase 3 - Production rollout

- enable production bucket,
- rotate fresh production credentials,
- validate upload from one internal test account,
- then open to all users.

## Phase 4 - Thumbnail and retention automation

After core production flow works:

- generate thumbnails asynchronously,
- add retention/archive policy,
- add moderation or virus-scanning if required later.

---

## Operational Checklist

Before production launch, verify all of the following.

### Backend

- [ ] real storage provider implemented,
- [ ] provider selected by config,
- [ ] local provider disabled outside Development,
- [ ] `complete-upload` validates object ownership expectations,
- [ ] max file size enforced,
- [ ] allowed MIME types enforced,
- [ ] read URL expiration configured,
- [ ] upload URL expiration configured.

### Cloud bucket

- [ ] bucket is private,
- [ ] credentials are bucket-scoped,
- [ ] CORS configured for correct origins,
- [ ] CORS allows the exact methods/headers used by the signed upload flow,
- [ ] lifecycle policy defined,
- [ ] monitoring/logging enabled if available.

### Security

- [ ] no public object URLs,
- [ ] no storage credentials shipped to clients,
- [ ] signed URLs expire quickly,
- [ ] API authorization checked for every history/read/finalize flow,
- [ ] secrets stored outside repo.

### Client apps

- [ ] mobile native upload works,
- [ ] `npm run web` upload works,
- [ ] photo preview works,
- [ ] required views validation works,
- [ ] large image failure path shows user-friendly error.

---

## Security Notes

## Signed URL lifetime

Recommended defaults:

- upload URL: **15 minutes**
- read URL: **60 minutes**

Do not make them long-lived.

## Content validation

Client-side validation is not enough.

Server must validate:

- MIME type,
- max file size,
- report ownership,
- trainer-trainee relationship,
- expected photo view type.

## Finalize-time verification is mandatory

Production `complete-upload` must not trust client-provided metadata blindly.

Before persisting a photo record in PostgreSQL, the backend should call storage metadata lookup and verify at least:

- object exists,
- actual file size is within allowed limit,
- actual content type is acceptable,
- storage key matches the server-authorized pattern,
- checksum or ETag is validated where feasible.

At minimum, the backend must ensure the `StorageKey` being finalized belongs to the expected:

- trainee,
- report request,
- photo view type.

Recommended key rule:

```text
photos/{traineeId}/{reportRequestId}/{viewType}/...
```

Without finalize-time verification, a client could attempt to finalize an arbitrary or malformed object reference.

## Signed PUT limitations

Do not assume that signed PUT alone fully enforces business rules.

Depending on provider and signing strategy, signed URLs may not reliably guarantee by themselves:

- file size enforcement,
- MIME type correctness,
- checksum integrity,
- object ownership semantics.

Because of that, prevalidation in `upload-init` is necessary but not sufficient. Finalize-time storage verification is required.

## EXIF / metadata

For progress photos, consider stripping EXIF metadata in the future.

This avoids leaking:

- GPS coordinates,
- device metadata,
- creation timestamps beyond what LGYM controls.

## Malware scanning

Not mandatory for v1 if only images are allowed and size is small, but it is a good follow-up for enterprise-grade production.

---

## Retention Policy Recommendation

Default recommendation:

- keep original photos while the report/history feature needs them,
- soft-delete database records immediately when business deletion occurs,
- hard-delete cloud objects asynchronously after a retention window,
- optionally archive old photos after 12-24 months depending on product policy.

This part should be aligned with product/legal requirements.

---

## Observability Recommendation

Track at least:

- upload-init success/failure count,
- complete-upload success/failure count,
- history/read failures,
- average upload size,
- top MIME types,
- orphaned initiated uploads,
- cloud storage error rates.

Useful alerts:

- spike in `upload-init` failures,
- spike in `complete-upload` failures,
- storage auth/signing failures,
- unusually high upload volume.

---

## Minimal Implementation Checklist for Engineering

If the team wants the shortest path to production, do this:

1. keep current DB model,
2. implement `CloudflareR2PhotoStorageProvider`,
3. add `PhotoStorage` config section,
4. register provider by environment/config,
5. implement mandatory finalize-time metadata verification,
6. configure R2 bucket CORS,
7. deploy staging,
8. test mobile + web upload,
9. deploy production.

That is enough to get to a correct v1.

---

## Final Recommendation

For LGYM production photo storage, use:

- **Cloudflare R2** as the primary object store,
- **private bucket** only,
- **short-lived signed PUT/GET URLs**,
- **direct-to-storage upload** from client,
- **API-only metadata persistence and authorization**, 
- **local API-hosted dev storage** only for Development.

Do **not** keep the current local provider as the production implementation.

It is intentionally only a development adapter.
