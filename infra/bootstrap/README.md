# infra/bootstrap

Account setup, applied locally by the owner with subscription scope (design §6). It creates the
Terraform state storage, the resource-provider registrations, the three resource groups, the
budget and the three CI identities with their roles. CI never applies it; the drift check plans it
as `ci-plan` (C26).

## Pre-apply checks (design §7)

| Check | Result | Date |
|---|---|---|
| PostgreSQL `Standard_B1ms` (free offer) in Norway East | PASS: offered (`az postgres flexible-server list-skus`) | 2026-10-05 |
| Container Apps in Norway East | PASS: `Microsoft.App/managedEnvironments` lists Norway East | 2026-10-05 |
| azurerm `create_mode = "PointInTimeRestore"` | PASS: azurerm 5.8.0 `azurerm_postgresql_flexible_server` supports it (with `source_server_id`, `point_in_time_restore_time_in_utc`) | 2026-10-05 |
| azurerm Entra-only auth with a managed-identity admin | PASS: `authentication { active_directory_auth_enabled = true, password_auth_enabled = false }`; `azurerm_postgresql_flexible_server_active_directory_administrator` takes `principal_type = "ServicePrincipal"` | 2026-10-05 |
| Billing currency is NOK | PASS: checked in Cost Management + Billing | 2026-10-05 |

Region: **Norway East**. The Scaleway checks belong to `infra/email` and run before it is first
applied.

**The subscription is a Free Trial.** Upgrade it to pay-as-you-go (portal: Subscriptions →
Upgrade) before the 30-day credit ends, around **2026-11-04**; otherwise the 12-month free
services are lost (C15).

## Prerequisites

- [Terraform](https://developer.hashicorp.com/terraform/install) 1.16.x, the
  [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) and the
  [GitHub CLI](https://cli.github.com/).
- `az login` as the owner (the account in `owner_object_id`), on the climb-on subscription
  (`az account show`).
- `gh auth login` as the repository owner.
- In the shell that runs Terraform or `verify.sh`, the two values that are never committed:

  ```bash
  export TF_VAR_admin_email='<C66 admin address>'
  export TF_VAR_budget_extra_emails='["<owner address>"]'
  ```

All commands below run from `infra/bootstrap` in Git Bash.

## First apply, with local state

The backend in `versions.tf` points at the storage this root creates, so the first apply uses
local state through an override file (`*_override.tf` is gitignored):

```bash
printf 'terraform {\n  backend "local" {}\n}\n' > local_override.tf
terraform init -input=false
terraform plan -input=false -out=bootstrap.tfplan
terraform apply bootstrap.tfplan
```

The plan imports the providers that were already registered (`providers.tf`) and creates
everything else. Azure accepts a past budget start date only within the current month, so if the
first apply is not in October 2026, set `start_date` in `budget.tf` to the first day of the
current month before planning; once the budget exists, leave it alone (changing it replaces the
budget). If `stclimbontfstate` is taken, choose another name and change it in both
`state.tf` and the backend block in `versions.tf`.

## Move the state into `tfstate-bootstrap`

Wait about five minutes for the owner's Storage Blob Data Owner role to take effect, then:

```bash
rm local_override.tf
terraform init -migrate-state -force-copy -input=false
terraform plan -input=false -detailed-exitcode   # expect exit 0: no changes
rm -f terraform.tfstate terraform.tfstate.backup bootstrap.tfplan
```

From then on the state is read and written only through Entra ID (`use_azuread_auth`); the
account has no usable key.

## GitHub environments

`staging`, `production` and `drift`, each deployable from `main` only; `production` requires the
owner's approval (C28):

```bash
repo=ciciban-kovich/climb-on
owner_id=$(gh api users/${repo%%/*} --jq .id)
for env in staging production drift; do
  if [ "$env" = production ]; then
    reviewers="[{\"type\":\"User\",\"id\":$owner_id}]"
  else
    reviewers="[]"
  fi
  gh api -X PUT "repos/$repo/environments/$env" --input - <<EOF
{"reviewers": $reviewers,
 "deployment_branch_policy": {"protected_branches": false, "custom_branch_policies": true}}
EOF
  gh api -X POST "repos/$repo/environments/$env/deployment-branch-policies" -f name=main -f type=branch
done
```

## Repository variables

All are **repository** variables (not environment variables), so the PR plan, which runs in no
environment, can read them. None is a secret.

```bash
gh variable set AZURE_TENANT_ID            --body "$(az account show --query tenantId -o tsv)"
gh variable set AZURE_SUBSCRIPTION_ID      --body "$(az account show --query id -o tsv)"
gh variable set TFSTATE_ACCOUNT            --body "$(terraform output -raw state_account_name)"
gh variable set AZURE_CLIENT_ID_PLAN       --body "$(terraform output -json ci_client_ids | python -c 'import json,sys; print(json.load(sys.stdin)["ci-plan"])')"
gh variable set AZURE_CLIENT_ID_STAGING    --body "$(terraform output -json ci_client_ids | python -c 'import json,sys; print(json.load(sys.stdin)["ci-staging"])')"
gh variable set AZURE_CLIENT_ID_PRODUCTION --body "$(terraform output -json ci_client_ids | python -c 'import json,sys; print(json.load(sys.stdin)["ci-production"])')"
gh variable set ADMIN_EMAIL                --body "$TF_VAR_admin_email"
gh variable set BUDGET_ALERT_EMAILS        --body "$TF_VAR_budget_extra_emails"
```

## Verify

```bash
terraform init -input=false
terraform plan -input=false -lock=false -detailed-exitcode   # 0 = applied, no drift
bash verify.sh
```

`verify.sh` reads Azure and GitHub directly and compares exact sets: role assignments per
principal, federated credential subjects, state containers, provider registrations, the budget
and its recipients, the `climbon-plan` data actions, and the GitHub environments. It names every
difference and exits 1 if there is any. The owner's own `Owner` role on the subscription is
expected; bootstrap does not grant it.

## Changing bootstrap

Edit, then `terraform plan` and `terraform apply` from the owner's machine. Using a new Azure
service needs its provider added to `providers.tf` and an apply here, because no other root may
register providers. A data-plane action refused to `ci-staging` or `ci-production` is fixed by a
grant added here, never by the refused root.
