# infra/email

The Scaleway side of email (design §6), applied locally by the owner. It holds two Scaleway
projects, `climbon-staging` and `climbon-production`, each with a Transactional Email (TEM)
domain in `fr-par` (`mail-staging.<domain>`, `mail.<domain>`), that domain's SPF, DKIM, MX and
DMARC records in the registered domain's DNS zone, and an IAM application whose one policy rule
allows SMTP sending (`TransactionalEmailEmailSmtpCreate`) in its own project only.

Scaleway has no GitHub OIDC federation, so no workflow plans or applies this root: it is outside
the PR plan and the drift check (the C26 exception), and `verify.sh` is its check. State lives in
`tfstate-email`, which no CI identity can read.

**No API key is ever created by Terraform** (an API key resource would put the secret in
state, C50). Sending keys are created in the Scaleway console and written to the environment's
Key Vault by `tools/set-secret.sh` (task 31).

## Prerequisites

- [Terraform](https://developer.hashicorp.com/terraform/install) 1.16.x, the Azure CLI, Python 3
  and Git Bash. All commands below run from `infra/email` in Git Bash.
- `az login` as the owner: the backend is reached through the owner's Storage Blob Data Owner on
  the state account (`infra/bootstrap`).
- The owner key (step 3) in the shell, never in a file in the repository, a variable or a tfvars:

  ```bash
  export SCW_ACCESS_KEY='<owner key access key>'
  export SCW_SECRET_KEY='<owner key secret key>'
  ```

The owner key is an operator credential, like the owner's Azure CLI login. It never enters the
repository, Terraform state or the application, and it expires within 90 days of its creation.

## Step 1: pre-apply checks (design §7)

Each check is recorded below as `| <id> | PASS | <YYYY-MM-DD> | <source> |` **before the first
apply**, except `tem-validation-stable` and `smtp-send-scoped-key`, which are dated right after it
(step 4). If a check fails, stop: the design is revised through `/harness:spec`, not the code.
`verify.sh --readme` fails until every row is a dated `PASS`.

The `dpa-accepted` row is dated the day the owner accepted Scaleway's DPA online, and its source
is `version <DPA version>; evidence <where the screenshot or console export is kept>`. The
evidence itself is kept **outside this repository** (it is public); this table names only where
it is. **This row is re-recorded whenever Scaleway publishes a new DPA version.**

| Check | Result | Date | Source |
|---|---|---|---|
| eea-processing | PASS | 2026-10-16 | Scaleway DPA (version of June 1st, 2024) art. 11.1 and 11.2.2; TEM FAQ, Privacy and security: "All data is hosted and processed entirely within the European Union" |
| dpa-accepted | PASS | 2026-10-08 | version June 1st, 2024 (accepted online by scrolling to the end, Scaleway offers no signed copy); evidence owner's machine, Downloads/Scaleway_Data Processing Agreement_Thu Oct 24 2024.pdf |
| subprocessors-eea | PASS | 2026-10-16 | TEM FAQ, Privacy and security: no non-EU sub-processors, "the entire TEM technical stack is fully managed by Scaleway, within the EU"; scaleway.com/en/subprocessorlist (last reviewed July 2025) lists no TEM sub-processor |
| smtp-587-starttls | PASS | 2026-10-16 | Scaleway docs, TEM Setting up SMTP (validated 2025-11-18): smtp.tem.scaleway.com, port 587 with STARTTLS, encryption mandatory, user name = Project ID, password = IAM API secret key; TEM capabilities and limits lists STARTTLS |
| iam-project-scope | PASS | 2026-10-16 | Scaleway docs, IAM Permission sets: TransactionalEmailEmailSmtpCreate ("Permission to create emails via SMTP") is listed under Scoped by Project |
| free-tier-scope | PASS | 2026-10-16 | per organisation: Scaleway pricing (scaleway.com/en/pricing/managed-services), TEM Essential "300 per organization (Free tier)" |
| billing-alert | PASS | 2026-10-08 | Scaleway console, Billing alerts: €1/month alert emailing the C66 admin address, set by the owner; not read through the API (the owner key has no billing permission, CHECK_BILLING_ALERT=no) |
| owner-permission-sets | PASS | 2026-10-16 | Scaleway console, IAM overview, policy climbon-owner: DomainsDNSFullAccess, IAMApplicationManager, IAMGroupReadOnly, IAMPolicyManager, ProjectManager, TransactionalEmailFullAccess (equals owner_permission_sets in verify.sh); IAMGroupReadOnly added 2026-10-16 to read IAM groups for verify's no-group check |
| provider-no-secret-in-state | PASS | 2026-10-16 | terraform providers schema -json for scaleway/scaleway 2.84.0: no sensitive or secret attribute on scaleway_account_project, scaleway_domain_record, scaleway_iam_application, scaleway_iam_policy, scaleway_tem_domain, scaleway_tem_domain_validation; no API-key resource in this root |
| sending-quota | PASS | 2026-10-16 | Scaleway docs, TEM capabilities and limits (validated 2025-10-29): default 10,000 emails/month, stricter initial limits possible for new users; M1 sends a handful of S49 and S87 emails on staging |
| outbound-587 | PASS | 2026-10-16 | Microsoft Learn, Troubleshoot outbound SMTP connectivity in Azure (updated 2026-07-16): authenticated SMTP relay on port 587 "isn't restricted in Azure, regardless of the subscription type"; only port 25 is blocked |
| tem-validation-stable | TODO | | |
| smtp-send-scoped-key | TODO | | |

What each check establishes:

- `eea-processing`: TEM in `fr-par` stores and processes email data within the EEA (C13).
- `dpa-accepted`: Scaleway's DPA, accepted online, is in effect for this account (C14).
- `subprocessors-eea`: TEM's sub-processors are all within the EEA (C13).
- `smtp-587-starttls`: TEM accepts SMTP on port 587 with STARTTLS, the project ID as user name
  and an IAM key as password.
- `iam-project-scope`: an IAM policy can be limited to TEM sending in one project.
- `free-tier-scope`: whether the free 300 emails/month apply per project or per organisation.
  Either passes; record which in the source (design §6 'Cost', restated 2026-10-16, says per
  organisation, shared across both environments).
- `billing-alert`: a Scaleway billing alert can email the C66 admin address at €1, and whether
  the API exposes it. The owner key has no billing permission, so `verify.sh` commits
  `CHECK_BILLING_ALERT=no`.
- `owner-permission-sets`: the owner application's organisation-level permission sets are enough
  to apply this root and run `verify.sh`, and nothing broader is needed. The source names them
  exactly; they must equal `owner_permission_sets` in `verify.sh`, which full mode compares with
  the live policy. `verify.sh` reads projects, IAM applications, policies, rules and API keys,
  TEM domains and emails, and the domain registration (auto-renewal).
- `provider-no-secret-in-state`: the pinned Scaleway provider (2.84.0) manages the TEM domain, its
  DNS records and the IAM applications and policies without putting any secret in state.
- `sending-quota`: a new account's TEM sending quota is enough for M1 (S49 and S87 emails on
  staging).
- `outbound-587`: outbound port 587 is open from Container Apps (documentation here; task 31's
  walkthrough is the live proof).
- `tem-validation-stable`: before the first apply, the pinned provider's documentation and
  changelog show no perpetual diff on `scaleway_tem_domain_validation` or `scaleway_tem_domain`;
  dated with the empty plan right after the first apply (step 4).
- `smtp-send-scoped-key`: an SMTP send succeeds with a key whose policy grants only
  `TransactionalEmailEmailSmtpCreate` in one project; dated in step 4.

## Owner records

`verify.sh --readme` requires each row with a valid date. Replace the owner key rows whenever
the owner key is recreated.

| Record | Date |
|---|---|
| owner-key-created | 2026-10-08 |
| owner-key-expires | 2026-11-08 |
| billing-alert-set | 2026-10-08 |

## Steps 2 and 3: domain and owner key (done 2026-10-08)

2. The domain in `email.tfvars` is registered at Scaleway Domains with auto-renewal on. Full mode
   fails if auto-renewal is off.
3. The owner IAM application `climbon-owner` has an organisation-level policy granting only the
   permission sets recorded in `owner-permission-sets`, and one API key expiring within 90 days,
   held only on the owner's machine. Its dates are in the owner records above.

## Step 4: apply

```bash
terraform init -input=false
terraform plan -input=false -var-file=email.tfvars -out=email.tfplan
terraform apply email.tfplan
```

The apply waits (up to an hour per domain) until TEM has found the DNS records and marked each
domain verified.

Right after the first apply:

1. **Empty plan.** `terraform plan -input=false -var-file=email.tfvars -detailed-exitcode` must
   exit 0. Record `tem-validation-stable` with that date. If it shows a change although nothing
   changed, **stop and report it**: the verify's own plan could then never pass. Do not add
   `ignore_changes` or drop the validation resource.
2. **Scoped SMTP send.** In the console, create a temporary API key on `climbon-staging-sender`
   (its policy grants only `TransactionalEmailEmailSmtpCreate` in `climbon-staging`) with an
   expiry of **29 days or less**, so a key left behind still fails full mode's 30-day rule even if
   Scaleway rounds the expiry up. Send one email over SMTP, port 587 with STARTTLS, from
   `noreply@mail-staging.<domain>` to the admin address, with the `climbon-staging` project ID as
   user name and that key as password. Confirm it is accepted, **delete the key**, and record
   `smtp-send-scoped-key` with the date of the send.

The outputs (`terraform output email`) are what task 30 copies into `infra/env/<env>.tfvars`:
`email_smtp_host`, `email_smtp_port`, `email_smtp_username` (the project ID) and
`email_sender_address`.

## Step 5: billing alert (done 2026-10-08)

A Scaleway billing alert at €1/month to the admin address was set by hand in the console (C15;
C25 covers Azure only). It is expected to fire in the domain's renewal month. `verify.sh` does not
read it (`CHECK_BILLING_ALERT=no`).

## Steps 6 and 7: verify

```bash
bash verify.sh --self-test   # stubs only; no key needed
bash verify.sh --readme      # this README's tables; no key needed
bash verify.sh               # full check against the Scaleway API
```

Full mode needs the owner key. It checks: both TEM domains verified and in their own project; the
projects named `climbon-*` are exactly the two; each sender application belongs to no IAM group
and has exactly one policy with exactly one rule, its own project only, permission sets exactly
`TransactionalEmailEmailSmtpCreate`; the owner key expires at most 90 days after creation and its
application belongs to no IAM group, with permission sets equal to `owner_permission_sets`;
auto-renewal is on; every API key on
both sender applications has an expiry at most a year after creation and more than 30 days away
(no key is fine here); and each `infra/env/<env>.tfvars` that exists carries this root's outputs.

Two more modes are run by task 31: `--env <env>` (that tfvars matches and its sender application
has a key) and `--delivered <env> <n> --since <RFC 3339>` (TEM lists at least `n` emails sent
from that sender since then; it reads status, sender and time only, C32).

The task's full verify, with `az login` and the owner key in the shell, from the repository root:

```bash
terraform fmt -check -recursive infra/email && terraform -chdir=infra/email init -input=false \
  && terraform -chdir=infra/email validate \
  && terraform -chdir=infra/email plan -input=false -lock=false -var-file=email.tfvars -detailed-exitcode \
  && bash infra/email/verify.sh --self-test && bash infra/email/verify.sh --readme && bash infra/email/verify.sh
```

## Owner key renewal

When the owner key expires, `verify.sh` says so before any other check. Create a new key for
`climbon-owner` in the console (expiry 90 days or less, held only on the owner's machine), delete
the expired one, replace the two owner key rows above, and re-run the verify.

## Sending-key rotation

Each sending key expires after one year; full mode fails once one is within 30 days of expiry.

1. Create the new key on `climbon-<env>-sender` in the console, with a one-year expiry.
2. `tools/set-secret.sh set <env> email-smtp-password` with its secret key.
3. Wait 5 minutes (the app caches the password for at most 5 minutes, task 10).
4. Delete the old key.
