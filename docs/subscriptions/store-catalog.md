# Store Catalog

## Status, Authority, and Scope

This document is the human-readable V0 direct-store catalog for issue #444. Its current state is approved precreation: the commercial intent and immutable identifiers are approved, but provider objects, generated identifiers, and store read-back evidence do not exist yet. Configured means a later restricted-test read-back has proved every required object and populated sanitized evidence. Neither state enables a production sale.

Source precedence is strict. The checked-in canonical JSON V0 fixture is first, the public #444 approval receipt is second, and this Markdown rendering is third. If they disagree, fail closed and correct this document rather than interpreting the difference. The receipt was approved by the catalog owner at the recorded UTC time.

| Catalog field | Approved value |
| --- | --- |
| `catalog.data.schema-version` | `1` |
| `catalog.data.catalog-id` | `lgym-direct-store-v1` |
| `catalog.data.state` | `approved-precreation` |
| `catalog.data.approver` | `@withelm` |
| `catalog.data.approval-url` | `https://github.com/withelm/LGYM-APP-APIv3/issues/444#issuecomment-5296486807` |
| `catalog.data.approved-at` | `2026-08-14T17:52:55Z` |

This catalog is configuration documentation only. It does not prove or add runtime subscription code, provider calls, purchase verification, paid capability enforcement, implemented paid benefits, production readiness, or production availability. Free access remains outside the paid catalog and is not a fourth paid profile.

## Revision

| Revision ID | Previous ID | Approved at UTC | Production enabled |
| --- | --- | --- | --- |
| `catalog.data.revision.v0-restricted-test` | `absent` | `2026-08-14T17:52:55Z` | `false` |

Revisions are append-only and Git-reviewed. V0 must never be rewritten to fit provider state. A price, territory, period, identifier, or production change requires a newly approved revision linked to its predecessor.

## Paid Profiles and Localization

| Profile row | Profile ID | Rank | English locale | English name | Polish locale | Polish name |
| --- | --- | --- | --- | --- | --- | --- |
| `catalog.data.profile.tier-1` | `tier_1` | `1` | `en` | `Basic` | `pl` | `Podstawowy` |
| `catalog.data.profile.tier-2` | `tier_2` | `2` | `en` | `Plus` | `pl` | `Plus` |
| `catalog.data.profile.tier-3` | `tier_3` | `3` | `en` | `Pro` | `pl` | `Pro` |

| Benefit row | Status ID | English locale | English copy | Polish locale | Polish copy |
| --- | --- | --- | --- | --- | --- |
| `catalog.data.benefit` | `pending_paid_benefits` | `en` | `Paid benefits pending approval; unavailable for production sale.` | `pl` | `Płatne korzyści oczekują na zatwierdzenie; sprzedaż produkcyjna jest niedostępna.` |

The localized benefit text is deliberately transparent: paid benefits remain pending and unavailable for production sale. It is not a promise that any paid capability exists.

## Common Period

| Period row | Period ID | Apple period | Google period |
| --- | --- | --- | --- |
| `catalog.data.period.monthly` | `monthly` | `one-month` | `P1M` |

V0 has one common monthly period and no annual or other cadence.

## Provider Applications and Environments

Each provider targets the same mobile identity. Usable means eligible for the later restricted-test setup only; production-enabled remains false in every environment.

| Application row | Provider | Application key | Application identity | Environments |
| --- | --- | --- | --- | --- |
| `catalog.data.application.apple` | `apple` | `6753204527` | `com.lesiuuu.lgymappmobile` | `sandbox:true:false;testflight:true:false;production:false:false` |
| `catalog.data.application.google` | `google` | `com.lesiuuu.lgymappmobile` | `com.lesiuuu.lgymappmobile` | `license-test:true:false;internal:true:false;production:false:false` |

Apple restricted testing is Sandbox/TestFlight. Google restricted testing is License Test/Internal. A usable restricted channel does not authorize customer distribution or a production purchase path.

## Poland V0 Prices

| Price row | Revision ID | Profile ID | Territory | Currency | Gross amount | VAT display |
| --- | --- | --- | --- | --- | --- | --- |
| `catalog.data.price.tier-1` | `v0-restricted-test` | `tier_1` | `POL` | `PLN` | `4.99` | `vat-inclusive` |
| `catalog.data.price.tier-2` | `v0-restricted-test` | `tier_2` | `POL` | `PLN` | `9.99` | `vat-inclusive` |
| `catalog.data.price.tier-3` | `v0-restricted-test` | `tier_3` | `POL` | `PLN` | `19.99` | `vat-inclusive` |

These are the approved configured V0 restricted-test customer-facing gross prices for Poland, inclusive of displayed VAT. They are not Billing Lab prices, tester overrides, tax-exclusive console inputs, or nearest-price substitutions. Production prices require a new appended and approved catalog revision. The store-observed renewal price and price-change state remain authoritative for an individual subscriber; this catalog never decides a charge.

## Apple Catalog

The subscription-group reference is a human-selected console reference. The English and Polish group names are customer-facing display localizations. The generated group identifier is a different, opaque provider-created fact that is absent before creation and must be read back after creation. Neither a reference nor a display name may be copied into the generated-ID field.

| Apple group field | Approved value |
| --- | --- |
| `catalog.data.apple.group.reference` | `LGYM Paid Profiles V1` |
| `catalog.data.apple.group.name.1` | `en:LGYM Subscriptions` |
| `catalog.data.apple.group.name.2` | `pl:Subskrypcje LGYM` |
| `catalog.data.apple.group.generated-id` | `<apple-generated-group-id after creation>` |
| `catalog.data.apple.group.sandbox-testable` | `false` |
| `catalog.data.apple.group.evidence` | `<evidence-url after read-back>` |

| Apple product row | Product ID | Profile ID | Period ID | Level | Sandbox testable | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| `catalog.data.apple.product.tier-1` | `lgym.subscription.tier_1.monthly` | `tier_1` | `monthly` | `3` | `false` | `<evidence-url after read-back>` |
| `catalog.data.apple.product.tier-2` | `lgym.subscription.tier_2.monthly` | `tier_2` | `monthly` | `2` | `false` | `<evidence-url after read-back>` |
| `catalog.data.apple.product.tier-3` | `lgym.subscription.tier_3.monthly` | `tier_3` | `monthly` | `1` | `false` | `<evidence-url after read-back>` |

Apple levels are intentionally inverse to LGYM rank: Pro is level 1, Plus level 2, and Basic level 3. Precreation false means no sandbox-testable provider object is claimed yet.

## Google Catalog

Primary is an LGYM catalog role, not a native Google subscription or base-plan field. The immutable base-plan identifier is separately fixed as primary-monthly. Each product has exactly one auto-renewing monthly base plan, and precreation inactive means the provider object is not claimed as configured.

| Google product row | Product ID | Profile ID | LGYM role | Base-plan ID | Period ID | Auto renewing | Active | Product evidence | Plan evidence |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `catalog.data.google.product.tier-1` | `lgym.subscription.tier_1.monthly` | `tier_1` | `primary` | `primary-monthly` | `monthly` | `true` | `false` | `<evidence-url after read-back>` | `<evidence-url after read-back>` |
| `catalog.data.google.product.tier-2` | `lgym.subscription.tier_2.monthly` | `tier_2` | `primary` | `primary-monthly` | `monthly` | `true` | `false` | `<evidence-url after read-back>` | `<evidence-url after read-back>` |
| `catalog.data.google.product.tier-3` | `lgym.subscription.tier_3.monthly` | `tier_3` | `primary` | `primary-monthly` | `monthly` | `true` | `false` | `<evidence-url after read-back>` | `<evidence-url after read-back>` |

## Transitions

There is no same-rank V1 crossgrade. Higher-rank moves are immediate; lower-rank moves take effect at the next renewal. The Google mode is a client-selected replacement policy, not a universal Console field.

| Transition row | From profile | To profile | Timing | Google mode |
| --- | --- | --- | --- | --- |
| `catalog.data.transition.1` | `tier_1` | `tier_2` | `immediate` | `CHARGE_PRORATED_PRICE` |
| `catalog.data.transition.2` | `tier_1` | `tier_3` | `immediate` | `CHARGE_PRORATED_PRICE` |
| `catalog.data.transition.3` | `tier_2` | `tier_3` | `immediate` | `CHARGE_PRORATED_PRICE` |
| `catalog.data.transition.4` | `tier_3` | `tier_2` | `next-renewal` | `DEFERRED` |
| `catalog.data.transition.5` | `tier_3` | `tier_1` | `next-renewal` | `DEFERRED` |
| `catalog.data.transition.6` | `tier_2` | `tier_1` | `next-renewal` | `DEFERRED` |

## V1 Exclusions

Every excluded path remains disabled or absent. No alternative commercial path may be added under V0.

| Exclusion ID | Enabled |
| --- | --- |
| `catalog.data.exclusion.trials` | `false` |
| `catalog.data.exclusion.offers` | `false` |
| `catalog.data.exclusion.coupons` | `false` |
| `catalog.data.exclusion.family-sharing` | `false` |
| `catalog.data.exclusion.promoted-iap` | `false` |
| `catalog.data.exclusion.offer-codes` | `false` |
| `catalog.data.exclusion.win-back` | `false` |
| `catalog.data.exclusion.play-resubscribe` | `false` |
| `catalog.data.exclusion.outside-app-acceptance` | `false` |

The exclusions cover trials, offers, coupons, Apple Family Sharing, promoted in-app purchases, offer codes, win-back, Google Play resubscribe, and outside-app acceptance. They also imply no extra product, base plan, period, or territory.

## Governance, Aliases, and Price Preservation

Aliases identify non-PII test-account roles only; they are never account emails or credentials. The owner approves catalog revisions, while an authenticated least-privilege operator performs provider setup without recording account identity, team identity, login, MFA, key material, or provider payloads.

| Governance field | Approved value |
| --- | --- |
| `catalog.data.governance.owner` | `@withelm` |
| `catalog.data.governance.alias.1` | `apple-sandbox-v1-primary` |
| `catalog.data.governance.alias.2` | `google-license-v1-primary` |
| `catalog.data.governance.revision-policy` | `append-only-git-reviewed` |
| `catalog.data.governance.apple-preservation` | `preserve-existing-subscriber-price-on-increase` |
| `catalog.data.governance.google-preservation` | `retain-legacy-cohort-opt-in-migration` |
| `catalog.data.governance.evidence-manifest` | `docs/subscriptions/evidence/issue-444-store-console-evidence.json` |

Apple price increases preserve the existing subscriber price by default. Google price changes retain the legacy cohort by default and require an approved opt-in migration. These preservation defaults remember agreements; verified store state remains the renewal-price authority.

## Machine-Checked Operating Contract

The following decision codes are stable parser input. They summarize source precedence, the state legend, provider distinctions, price authority, fail-closed execution, and rollout gates without turning narrative wording into a prose snapshot.

| Contract ID | Decision |
| --- | --- |
| `catalog.contract.source.fixture` | `1:canonical-json-v0` |
| `catalog.contract.source.approval` | `2:public-approval-receipt` |
| `catalog.contract.source.markdown` | `3:human-rendering` |
| `catalog.contract.legend.precreation` | `approved-intent:no-provider-objects-or-evidence` |
| `catalog.contract.legend.configured` | `restricted-test-read-back:evidence-required` |
| `catalog.contract.apple.identifiers` | `reference-and-display-human:generated-id-post-create` |
| `catalog.contract.google.role` | `primary-is-lgym-role-not-native-field:base-plan-id-is-primary-monthly` |
| `catalog.contract.price.v0` | `configured-restricted-test:not-billing-lab-override` |
| `catalog.contract.price.production` | `new-appended-approved-revision-required` |
| `catalog.contract.price.authority` | `store-observed-renewal-state` |
| `catalog.contract.benefits` | `pending:not-implemented-or-production-sale` |
| `catalog.contract.free` | `outside-paid-catalog` |
| `catalog.contract.preflight.apple` | `auth-iam-agreement-app-collision-price:fail-closed` |
| `catalog.contract.preflight.google` | `auth-iam-package-license-internal-collision-price:fail-closed` |
| `catalog.contract.iam` | `agent-operated:least-privilege:no-account-identity-or-credentials-in-evidence` |
| `catalog.contract.retry` | `reconcile-matching-partial-objects:never-delete-or-reuse-id` |
| `catalog.contract.rollback` | `preserve-restricted-drafts:disable-rollout:no-production-mutation` |
| `catalog.contract.evidence` | `metadata-only:sanitized-object-bound-read-back` |
| `catalog.contract.rollout.approval` | `gate-1:passed` |
| `catalog.contract.rollout.parity` | `gate-2:required` |
| `catalog.contract.rollout.apple` | `gate-3:preflight-and-read-back-required` |
| `catalog.contract.rollout.google` | `gate-4:preflight-and-read-back-required` |
| `catalog.contract.rollout.production` | `gate-5:blocked-new-revision-and-review-required` |

## Console Preflight, Retry, and Rollback

Before any irreversible Apple operation, authenticate without recording session data and verify IAM, the paid-app agreement required for testing, the exact app identity, collisions, and exact price availability. Before any Google operation, verify IAM, the exact package, license testing, the restricted internal track, collisions, and exact price availability. Authentication, MFA, IAM, wrong app/package, agreement, collision, unavailable exact price, or redaction failure stops all mutation.

If matching partial objects already exist, read and reconcile immutable fields before continuing. A mismatch blocks execution. A retry must preserve matching restricted drafts and immutable identifiers; it must never delete, archive, reuse, repurpose, or replace an identifier to simulate a clean run. Rollback disables rollout and preserves the restricted drafts for diagnosis. It does not mutate production, invent a substitute price, or claim success.

## Evidence Schema

Precreation placeholders are allowed only for the provider-generated Apple group identifier and sanitized evidence fields. Configured state replaces every placeholder with object-bound read-back facts, HTTPS references, UTC observation time, and a redaction-reviewed artifact hash. Each provider-object evidence record must compare expected and actual group/product/base-plan identity, period, level where applicable, territory, displayed gross price, and restricted-test state. Evidence is metadata-only and must not contain credentials, login/MFA material, account or team identifiers, emails, personal data, raw receipts, signed payloads, purchase artifacts, service-account material, provider bodies, or unredacted screenshots.

| Evidence field | Precreation placeholder |
| --- | --- |
| `catalog.evidence.provider` | `apple-or-google` |
| `catalog.evidence.logical-id` | `catalog-stable-id` |
| `catalog.evidence.application` | `catalog-application-identity` |
| `catalog.evidence.expected` | `catalog-approved-value` |
| `catalog.evidence.actual` | `<evidence-read-back-value after creation>` |
| `catalog.evidence.read-back-id` | `<evidence-read-back-id after creation>` |
| `catalog.evidence.reference` | `<evidence-url after read-back>` |
| `catalog.evidence.observed-at` | `<observed-at-utc after read-back>` |
| `catalog.evidence.sha256` | `<sha256 after redaction review>` |
| `catalog.evidence.state` | `restricted-test-configured` |
| `catalog.evidence.redaction` | `approved` |
| `catalog.evidence.source-task` | `task-8-or-task-9` |

## Rollout Gates

The public V0 approval is the first completed gate. Markdown/JSON parity must pass before console work. Apple and Google each require their own fail-closed preflight and complete restricted-test read-back before the catalog can move to configured state. Production sale remains blocked until a new revision, provider evidence convergence, downstream agreement-history work, and explicit release review are complete. No gate in this document enables runtime code, provider calls, paid benefits, customer distribution, or production sale.

## Official Sources

| Official source ID | HTTPS URL |
| --- | --- |
| `catalog.source.apple.offer` | `https://developer.apple.com/help/app-store-connect/manage-subscriptions/offer-auto-renewable-subscriptions/` |
| `catalog.source.apple.pricing` | `https://developer.apple.com/help/app-store-connect/manage-subscriptions/manage-pricing-for-auto-renewable-subscriptions/` |
| `catalog.source.apple.sandbox` | `https://developer.apple.com/help/app-store-connect/test-in-app-purchases/overview-of-testing-in-sandbox/` |
| `catalog.source.apple.group-api` | `https://developer.apple.com/documentation/appstoreconnectapi/post-v1-subscriptiongroups` |
| `catalog.source.google.catalog` | `https://support.google.com/googleplay/android-developer/answer/140504` |
| `catalog.source.google.replacements` | `https://developer.android.com/google/play/billing/subscriptions` |
| `catalog.source.google.pricing` | `https://developer.android.com/google/play/billing/price-changes` |
