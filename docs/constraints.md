# Constraints — climbing partner app, v1

IDs are stable: never renumbered, never reused.
Rationale lives in decisions.md, not here.

## Scope and data location

- C1. v1 serves climbers located in Norway only.
- C2. Postcode reference data comes from Kartverket's "Postnummerområder" dataset (CC BY 4.0).
- C3. All timestamps are stored and compared in UTC; dates are UTC dates.
- C4. Application code reads the current time only through a single injectable clock, so tests can fix time. Applies to every time-dependent rule.
- C5. The privacy policy states: the retention periods in S38, S40 and S41; that erased data can remain in database backups for up to 7 days after erasure; the dormancy period and the automatic deletion timeline (S107, S109).
- C13. All personal data is stored and processed within the EEA, including backups, logs and third-party processors. Exception: delivery metadata of web push notifications, which pass through the browser vendors' push services. Payloads are end-to-end encrypted.
- C14. The email provider processes data within the EEA and has a signed data processing agreement.

## Architecture and stack

- C6. The climber client is a progressive web app; there is no native iOS or Android app. The client is Blazor WebAssembly, standalone, served as static files from within the EEA (C13).
- C7. All client functionality goes through a documented HTTP API. Every rule in specs.md is enforced server-side; the client contains no business rules.
- C8. Backend: C# on .NET 10 with ASP.NET Core.
- C9. The backend is deployed as one unit (modular monolith).
- C10. Database: PostgreSQL, accessed through EF Core.
- C12. Hosting: Azure Container Apps.
- C15. Monthly running-cost ceiling: [pending, budget check]. Behaviour at the ceiling: [pending, same question].
- C47. Each API (climber, admin) publishes an OpenAPI description generated from its code. Each client's API code is generated from the corresponding description.
- C48. CI regenerates both OpenAPI descriptions and both generated clients, and fails if any differs from the committed version.
- C49. The service worker's push handling is hand-written JavaScript. It only receives and displays notifications and opens the app when one is clicked. It contains no business rules (C7).

## Testing

- C11. Integration tests run against a real PostgreSQL instance in a container (Testcontainers). In-memory database substitutes are not used in tests.
- C16. Every S-ID in specs.md, top-level points and sub-points alike, is referenced by at least one automated test through a machine-readable test attribute (e.g. [Trait("Spec", "S31")]). Lines marked "(retired" are excluded.
- C17. CI computes the set difference between the S-IDs in /docs/specs.md and the S-IDs referenced by tests, and fails if it is non-empty.
- C18. Mutation testing (Stryker.NET) runs on the domain module. CI fails if the mutation score is below 80%.
- C19. The domain module has no dependency on the database, HTTP, email, push or the system clock. An architecture test in CI enforces this.
- C20. S10 (including after blocks, S24.1), the total order of S60, and S13.4 are tested with property-based tests (FsCheck) over randomly generated profiles.
- C21. Integration tests call the HTTP API (C7), run against PostgreSQL per C11, and fix time through the clock in C4.
- C22. In tests, email and push are replaced at the system boundary by fakes that record every notification that would have been sent. Tests assert on that record.
- C23. CI runs on GitHub Actions for every pull request. Merging to main is blocked unless all steps pass.
- C24. Where specs.md says that a climber sees or is shown something, it means the API returns it. How it is presented is the client's concern and is not covered by S-points.
- C44. Tests use a fixed fixture postcode table, never the live dataset.
- C45. Every error response contains a stable, language-independent error code in addition to the translated message. Client logic and tests act on the code; tests never assert on translated text, except tests of the translations themselves.
- C46. CI fails if any text key is missing in either supported language.

## Infrastructure and operations

- C25. All Azure resources are defined in Terraform in the repository. No resource is created or changed manually. Exception: secret values. Terraform defines the Key Vault and its access rules, but never secret values, so no secret ever enters Terraform state. Secret values are set with a documented script run by the admin.
- C26. A scheduled CI job runs terraform plan against production and alerts the admin if the plan is not empty (drift).
- C27. Terraform state is stored in Azure Storage within the EEA.
- C28. Environments: staging and production. A merge to main deploys to staging automatically. Production deploys the same build artifact after manual approval (GitHub environment protection). Deployments run only from CI.
- C29. EF Core migrations run as a separate step before the new app version starts. Every migration is compatible with the previous app version.
- C30. CI verifies C29 by running the previous release's integration tests against the schema after the new migrations.
- C31. Traces, metrics and logs are sent through OpenTelemetry to Application Insights within the EEA. Logs are structured. Retention is 30 days.
- C32. No telemetry (logs, traces, metrics) contains personal data other than account IDs. This includes request URLs, query strings and recorded SQL.
- C33. Integration tests capture all log and trace output and fail if any personal value from the test fixtures appears in it.
- C34. An availability test checks a health endpoint and emails the admin on failure.

## Backup and replay

- C35. Database backups: Azure automated backups with point-in-time restore, retained for 7 days, within the EEA.
- C36. Every action listed here is appended to a replay log stored outside the database: erasures (S38, S40, S43, S46, S56), creation and expiry of identifier hashes (S41), blocks (S24), suspensions and lifted suspensions (S26, S77), display-name resets (S59), and gym creation, changes and deactivation (S17, S21).
- C37. An entry contains only what is needed to re-apply the action: event type, affected ID(s), timestamp; plus the hash for S41, the generated placeholder for S59, and the gym's data for gym events. No other personal data. The log is append-only. Entries older than 14 days are deleted.
- C38. After any database restore, replay-log entries newer than the restore point are re-applied in timestamp order, each with its full effects (e.g. a replayed block ends the connection, S24.3). Re-applying an entry is idempotent. The API accepts no traffic until the replay has finished.
- C39. Before launch and at least quarterly, a restore-and-replay drill runs on staging. It fails if any action recorded in the log is not in effect after the replay.
- C40. An action listed in C36 is not confirmed to the client until its replay-log entry has been written.
- C41. Integration tests fail if any personal value from the test fixtures appears in the replay log.

## Security and secrets

- C42. Passwords are stored only as salted, slow hashes (ASP.NET Core Identity password hasher). Plain-text passwords never appear in the database, telemetry (C32) or the replay log (C37).
- C50. All secrets are stored in Azure Key Vault within the EEA. The backend reads them through its managed identity. No secret appears in the repository, configuration files, container images, Terraform state, telemetry (C32) or the client.
- C51. The backend authenticates to PostgreSQL with Microsoft Entra authentication through its managed identity. Password authentication is disabled on the database server.
- C52. CI authenticates to Azure through OpenID Connect federation. GitHub stores no long-lived Azure credential.
- C53. GitHub push protection is enabled, and CI runs a secret scan (e.g. gitleaks) that fails the build on any finding.
- C54. A scheduled job regenerates the Sign in with Apple client secret monthly from the private key in Key Vault, each time valid for 6 months. The admin is alerted if regeneration fails.
- C55. The web push (VAPID) key pair is rotated only if the private key is compromised. On rotation, all stored push subscriptions are deleted; clients re-subscribe when next opened. Email notifications (S70) are unaffected.

## Admin

- C63. Admin login is through Microsoft Entra ID with multi-factor authentication required.
- C64. The admin client is a separate Blazor WebAssembly app on its own hostname. The climber client's bundle contains no admin code.
- C65. Admin API integration tests use tokens signed by a local test key. Token validation accepts that key only in the test environment. A test asserts that the production configuration accepts tokens from the Entra tenant only.
- C66. Messages addressed to the admin (S19, S101.3, C26, C34, C54) go to an admin email address set in configuration, outside the database.

## Repository

- C56. The repository is public on GitHub and carries no open-source licence.
- C57. The repository contains no harness files: no role, workflow or critic files.
- C58. specs.md, constraints.md, non-goals.md, decisions.md, later.md and accessibility-checklist.md are stored in /docs in the repository.

## Client presentation and accessibility

- C43. The client displays attribution for Kartverket data on an about page, worded per Kartverket's terms of use.
- C59. The climber client meets WCAG 2.1 level AA. The admin client is not in WCAG scope.
- C60. CI runs an axe-core scan of every page in the core flow (registration, profile, suggestion list, invites, chat, settings), in both supported languages, against the app running with fixture data (C44). Any violation fails the build.
- C61. Before launch, a keyboard-only pass and a screen-reader pass through the core flow are carried out, following /docs/accessibility-checklist.md.
- C62. The client sets the page language attribute from the climber's stored language (S104).
- C67. The climber client displays message text as plain text and never interprets it as HTML.
  - C67.1 URLs beginning with http:// or https:// are displayed as links whose visible text is the full URL.
  - C67.2 Any other scheme (e.g. javascript:, data:) is displayed as plain text.
  - C67.3 Links open in a new tab with rel="noopener noreferrer".
  - C67.4 This behaviour is covered by component tests (bUnit), which N7 does not exclude.
