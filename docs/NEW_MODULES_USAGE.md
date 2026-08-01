# Feature and API usage notes

This document covers feature and product API usage. For the contributor workflow, see the [module contribution guide](MODULE_CONTRIBUTION_GUIDE.md). Its examples do not authorize creating a project, context, database, schema, migration, or project-reference edge.

## 1. Report photo module

### Purpose

The report photo module lets trainees and trainers attach progress photos to report requests without streaming the binary through PostgreSQL. The backend owns authorization, signed URLs, upload session tracking, and photo metadata persistence.

### Main building blocks

- `IPhotoStorageProvider` - storage abstraction for signed upload/read URLs and metadata lookup.
- `ReportingService.Photos.cs` - business flow for upload-init, complete-upload, signed reads, and history.
- `CloudflareR2PhotoStorageProvider` - signed direct-upload provider for realistic development and production.
- `LocalPhotoStorageProvider` - local-development provider backed by files under `dev-photo-storage`.
- `LocalPhotoDevelopmentEndpoints` - development-only endpoints behind `/dev/photos/*` used by the local provider.

### Configuration

`PhotoStorage` section example:

```json
{
  "PhotoStorage": {
    "Provider": "Local",
    "BucketName": "YOUR_BUCKET_NAME",
    "Endpoint": "https://YOUR_ACCOUNT_ID.r2.cloudflarestorage.com",
    "SignedUploadExpirationMinutes": 10,
    "SignedReadExpirationMinutes": 15,
    "MaxFileSizeBytes": 5242880,
    "AllowedMimeTypes": ["image/jpeg", "image/png", "image/heic"]
  }
}
```

Never commit:

- `PhotoStorage:AccessKeyId`
- `PhotoStorage:SecretAccessKey`
- local config overrides that contain real credentials

### User flow

#### Step 1 - initialize upload

The trainee or trainer calls:

- `POST /api/trainee/reporting/photos/upload-init`
- `POST /api/trainer/reporting/photos/upload-init`

Example request body:

```json
{
  "reportRequestId": "REPORT_REQUEST_ID",
  "viewType": "Front",
  "mimeType": "image/jpeg",
  "sizeBytes": 2451200
}
```

The backend then:

- verifies access to the report request,
- validates MIME type and declared size,
- generates a `storageKey`,
- persists the pending upload-init session,
- returns `uploadUrl`, `storageKey`, and `expiresAt`.

#### Step 2 - upload the binary

- for `CloudflareR2`, the client performs a direct `PUT` to the signed URL,
- for `Local`, the client uploads through `PUT /dev/photos/upload/{storageKey}`.

#### Step 3 - complete upload

Call one of:

- `POST /api/trainee/reporting/photos/complete-upload`
- `POST /api/trainer/reporting/photos/complete-upload`

Example request body:

```json
{
  "storageKey": "photos/.../Front/...jpg",
  "mimeType": "image/jpeg",
  "sizeBytes": 2451200,
  "checksum": "OPTIONAL_CLIENT_CHECKSUM",
  "reportRequestId": "REPORT_REQUEST_ID",
  "viewType": "Front"
}
```

The backend:

- verifies the request against upload-init,
- reads object metadata from storage,
- rejects mismatched or suspicious files,
- persists the `Photo` entity,
- soft-deletes the previous photo for the same `viewType` if it gets replaced.

#### Step 4 - history and preview

- `GET /api/trainee/reporting/photos/history?requestId=...`
- `GET /api/trainer/reporting/photos/history?traineeId=...&requestId=...`
- `GET /api/trainer/reporting/photos/{photoId}/signed-url`

### Security rules

- `Local` provider must only run in Development or Testing.
- Photo access is always authorized by the backend.
- Clients must not treat `storageKey` as proof of ownership.
- Signed URLs are short-lived.
- Completion validates real object metadata, not just client-declared values.

---

## 2. Extended reporting module

### What was added

- `moduleConfig` on template fields,
- module-aware field types, including `Photos` and `Measurements`,
- trainer feedback per submission and per field,
- extra in-app notifications for report requests and feedback.

### Creating a template with module configuration

Endpoint:

- `POST /api/trainer/report-templates`

Example photo field:

```json
{
  "key": "progressPhotos",
  "label": "Progress photos",
  "type": "Photos",
  "isRequired": true,
  "order": 3,
  "moduleConfig": {
    "requiredViews": ["Front", "Back", "Side"]
  }
}
```

Important details:

- `moduleConfig` is stored and returned to the client,
- submission validation ensures required photo views really exist,
- trainer field comments must reference existing template keys only.

Example measurements field:

```json
{
  "key": "checkInMeasurements",
  "label": "Check-in measurements",
  "type": "Measurements",
  "isRequired": true,
  "order": 4,
  "moduleConfig": {
    "measurementTypes": ["BodyWeight", "Waist", "Chest"]
  }
}
```

For `Measurements`, the validator expects a JSON object with a `measurementTypes` array.

### Trainer feedback

Endpoint:

- `POST /api/trainer/trainees/{traineeId}/report-submissions/{submissionId}/feedback`

Example payload:

```json
{
  "trainerOverallComment": "Great work, but tighten the progression pacing.",
  "trainerFieldComments": {
    "weight": "Add context about hydration.",
    "progressPhotos": "Retake the next series in more even lighting."
  }
}
```

### Operational behavior

- creating a report request sends a trainee notification,
- adding trainer feedback sends a trainee notification,
- templates with required `Photos` modules enforce complete uploads before submission.

---

## 3. In-app notifications for reporting and invitations

### New events

- trainer invitation for an existing user,
- report request creation,
- feedback added to a submission.

### Flow

1. the application service enqueues a command,
2. `CommandEnvelope` is persisted inside the same unit-of-work boundary,
3. dispatch runs after commit,
4. the handler creates an `InAppNotification`,
5. the publisher may push the event through SignalR.

### Why it matters

This removes the earlier risk of double-scheduling the same envelope. There is now a single durable dispatch path after the transaction commits.

---

## 4. Measurements module - bulk add and trends

### New capabilities

- add many measurements in one request,
- calculate a single body-part trend,
- calculate a trend list across measurement groups,
- convert units automatically on read.

### Endpoints

- `POST /api/measurements/add`
- `POST /api/measurements/add-bulk`
- `GET /api/measurements/{id}/getHistory`
- `GET /api/measurements/{id}/list`
- `GET /api/measurements/{id}/trend`
- `GET /api/measurements/{id}/trends`

### Bulk add example

```json
{
  "measurements": [
    { "bodyPart": "BodyWeight", "unit": "Kilograms", "value": 82.4 },
    { "bodyPart": "Waist", "unit": "Centimeters", "value": 86.0 }
  ]
}
```

### Validation rules

- the unit must match the body part,
- values must be positive,
- trends are returned only after authorization succeeds,
- if there is not enough data, the trend returns `insufficient_data` instead of a misleading result.

---

## 5. Trainee self-service

### New endpoints

- `GET /api/trainee/trainer` - returns the current trainer profile,
- `GET /api/trainee/plan/active` - returns the active trainer-assigned plan.

### Why it helps

- mobile clients can immediately show who the trainee is linked to,
- mobile clients can fetch the active plan without navigating other trainer routes.

---

## 6. Google auth fallback

### What changed

- Google auth and account-linking endpoints now also accept `accessToken`,
- when the ID token is valid but lacks profile or email data, the backend falls back to Google `userinfo`.

### Why

Some Google flows return a valid subject with incomplete claims. The fallback keeps sign-in and linking working as long as `userinfo` confirms the same `sub`.

### Important note

- the fallback does not bypass security validation,
- email is still required for the current registration and deduplication flow.
