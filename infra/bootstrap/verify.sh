#!/usr/bin/env bash
# Reads Azure and GitHub directly and compares what infra/bootstrap must leave there, as exact
# sets, with design §6 (C15, C26, C27, C52). Exits 1 naming every difference.
#
# Needs: az logged in as the owner, gh authenticated, TF_VAR_admin_email and
# TF_VAR_budget_extra_emails set (as for terraform plan).
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
failures=0

fail() {
  echo "FAIL: $*"
  failures=$((failures + 1))
}

# az on Windows ends lines with CR.
az() { command az "$@" | tr -d '\r'; }

tfvar() {
  sed -nE "s/^[[:space:]]*$1[[:space:]]*=[[:space:]]*\"([^\"]*)\".*/\1/p" "$here/terraform.tfvars"
}

# compare <what> <expected lines> <actual lines>: case-insensitive exact set comparison.
compare() {
  local what="$1" missing extra
  missing="$(comm -23 <(printf '%s\n' "$2" | sed '/^$/d' | tr 'A-Z' 'a-z' | sort -u) \
                      <(printf '%s\n' "$3" | sed '/^$/d' | tr 'A-Z' 'a-z' | sort -u))"
  extra="$(comm -13 <(printf '%s\n' "$2" | sed '/^$/d' | tr 'A-Z' 'a-z' | sort -u) \
                    <(printf '%s\n' "$3" | sed '/^$/d' | tr 'A-Z' 'a-z' | sort -u))"
  [[ -z "$missing" ]] || while IFS= read -r line; do fail "$what: missing $line"; done <<<"$missing"
  [[ -z "$extra" ]] || while IFS= read -r line; do fail "$what: unexpected $line"; done <<<"$extra"
}

: "${TF_VAR_admin_email:?TF_VAR_admin_email must be set}"
: "${TF_VAR_budget_extra_emails:?TF_VAR_budget_extra_emails must be set}"

repo="$(tfvar github_repo)"
owner_id="$(tfvar owner_object_id)"
admin_id="$(tfvar admin_object_id)"
github_owner="${repo%%/*}"

sub="/subscriptions/$(az account show --query id -o tsv)"
rg="$sub/resourceGroups"
tfstate_rg="rg-climbon-tfstate"

# --- State storage -------------------------------------------------------------------------
accounts="$(az storage account list -g "$tfstate_rg" --query "[].name" -o tsv 2>/dev/null)"
if [[ "$(printf '%s\n' "$accounts" | sed '/^$/d' | wc -l)" -eq 1 ]]; then
  account="$accounts"
  shared_key="$(az storage account show -g "$tfstate_rg" -n "$account" --query allowSharedKeyAccess -o tsv)"
  [[ "$shared_key" == "false" ]] || fail "$account: allowSharedKeyAccess is '$shared_key', expected false"
  compare "state containers" \
    "$(printf '%s\n' tfstate-bootstrap tfstate-shared tfstate-staging tfstate-production tfstate-email)" \
    "$(az storage container-rm list -g "$tfstate_rg" --storage-account "$account" --query "[].name" -o tsv)"
else
  fail "expected exactly one storage account in $tfstate_rg, found: ${accounts:-none}"
  account="<state account>"
fi
sa="$rg/$tfstate_rg/providers/Microsoft.Storage/storageAccounts/$account"
containers="$sa/blobServices/default/containers"

# --- Resource providers ----------------------------------------------------------------------
for ns in Microsoft.App Microsoft.DBforPostgreSQL Microsoft.KeyVault Microsoft.Storage \
          Microsoft.OperationalInsights Microsoft.Insights Microsoft.ManagedIdentity Microsoft.Consumption; do
  state="$(az provider show -n "$ns" --query registrationState -o tsv)"
  [[ "$state" == "Registered" ]] || fail "provider $ns is '$state', expected Registered"
done

# --- Budget ----------------------------------------------------------------------------------
budget="$sub/providers/Microsoft.Consumption/budgets/climbon-monthly?api-version=2023-05-01"
amount="$(az rest --method get --url "$budget" --query "properties.amount" -o tsv 2>/dev/null)"
[[ "$amount" == "55" || "$amount" == "55.0" ]] || fail "budget climbon-monthly: amount is '${amount:-missing}', expected 55"
# Addresses are compared but never printed (C32).
expected_emails="$(printf '%s\n' "$TF_VAR_admin_email"; printf '%s' "$TF_VAR_budget_extra_emails" | tr -d '[]" ' | tr ',' '\n')"
actual_emails="$(az rest --method get --url "$budget" --query "properties.notifications.*.contactEmails[]" -o tsv 2>/dev/null)"
email_diff="$(comm -3 <(printf '%s\n' "$expected_emails" | sed '/^$/d' | tr 'A-Z' 'a-z' | sort -u) \
                      <(printf '%s\n' "$actual_emails" | sed '/^$/d' | tr 'A-Z' 'a-z' | sort -u) | wc -l)"
[[ "$email_diff" -eq 0 ]] || fail "budget climbon-monthly: contact emails differ from TF_VAR_admin_email + TF_VAR_budget_extra_emails ($email_diff address(es))"

# --- CI identities ---------------------------------------------------------------------------
compare "identities in $tfstate_rg" "$(printf '%s\n' ci-plan ci-staging ci-production)" \
  "$(az identity list -g "$tfstate_rg" --query "[].name" -o tsv)"

declare -A subjects=(
  [ci-plan]="pull_request environment:drift"
  [ci-staging]="environment:staging"
  [ci-production]="environment:production"
)
declare -A principal
for identity in ci-plan ci-staging ci-production; do
  principal[$identity]="$(az identity show -g "$tfstate_rg" -n "$identity" --query principalId -o tsv 2>/dev/null)"
  if [[ -z "${principal[$identity]}" ]]; then
    fail "identity $identity not found"
    continue
  fi
  expected=""
  for s in ${subjects[$identity]}; do
    expected+="https://token.actions.githubusercontent.com|repo:$repo:$s|api://AzureADTokenExchange"$'\n'
  done
  compare "$identity federated credentials" "$expected" \
    "$(az identity federated-credential list -g "$tfstate_rg" --identity-name "$identity" \
         --query "[].join('|', [issuer, subject, join(',', audiences)])" -o tsv)"

  directory_roles="$(az rest --method get \
    --url "https://graph.microsoft.com/v1.0/roleManagement/directory/roleAssignments?\$filter=principalId%20eq%20'${principal[$identity]}'" \
    --query "value[].roleDefinitionId" -o tsv)"
  [[ -z "$directory_roles" ]] || fail "$identity has Entra directory role(s): $directory_roles"
done

# --- Role assignments (exact set per principal) ----------------------------------------------
assignments() {
  az role assignment list --assignee "$1" --all --include-inherited \
    --query "[].join('|', [roleDefinitionName, scope])" -o tsv
}

check_assignments() {
  local who="$1" id="$2" expected="$3"
  [[ -n "$id" ]] || return 0
  compare "$who role assignments" "$expected" "$(assignments "$id")"
}

check_assignments ci-plan "${principal[ci-plan]:-}" "$(printf '%s\n' \
  "climbon-plan|$sub" \
  "Storage Blob Data Reader|$containers/tfstate-bootstrap" \
  "Storage Blob Data Reader|$containers/tfstate-shared" \
  "Storage Blob Data Reader|$containers/tfstate-staging" \
  "Storage Blob Data Reader|$containers/tfstate-production")"

check_assignments ci-staging "${principal[ci-staging]:-}" "$(printf '%s\n' \
  "Contributor|$rg/rg-climbon-staging" \
  "Role Based Access Control Administrator|$rg/rg-climbon-staging" \
  "Key Vault Crypto Officer|$rg/rg-climbon-staging" \
  "Storage Blob Data Contributor|$rg/rg-climbon-staging" \
  "Storage Blob Data Contributor|$containers/tfstate-staging")"

check_assignments ci-production "${principal[ci-production]:-}" "$(printf '%s\n' \
  "Contributor|$rg/rg-climbon-production" \
  "Role Based Access Control Administrator|$rg/rg-climbon-production" \
  "Contributor|$rg/rg-climbon-shared" \
  "Role Based Access Control Administrator|$rg/rg-climbon-shared" \
  "Contributor|$rg/rg-climbon-staging" \
  "Role Based Access Control Administrator|$rg/rg-climbon-staging" \
  "Storage Blob Data Contributor|$containers/tfstate-shared" \
  "Storage Blob Data Contributor|$containers/tfstate-production" \
  "Storage Blob Data Contributor|$containers/tfstate-staging" \
  "Key Vault Crypto Officer|$rg/rg-climbon-production" \
  "Storage Blob Data Contributor|$rg/rg-climbon-production")"

check_assignments admin "$admin_id" "$(printf '%s\n' \
  "Key Vault Secrets Officer|$rg/rg-climbon-staging" \
  "Key Vault Secrets Officer|$rg/rg-climbon-production")"

# The owner's subscription Owner role is the account's own, not granted by bootstrap.
check_assignments owner "$owner_id" "$(printf '%s\n' \
  "Owner|$sub" \
  "Storage Blob Data Owner|$sa")"

# --- climbon-plan grants no blob reads -------------------------------------------------------
definitions="$(az role definition list --custom-role-only true --name climbon-plan --query "length(@)" -o tsv)"
[[ "$definitions" == "1" ]] || fail "expected one climbon-plan role definition, found ${definitions:-0}"
while IFS= read -r action; do
  [[ -n "$action" ]] || continue
  lower="$(tr 'A-Z' 'a-z' <<<"$action")"
  if [[ "$lower" == "*" || "$lower" == *blobs/read* || "$lower" == *blobs/\** ||
        ( "$lower" == microsoft.storage/* && "$lower" == *\** ) ]]; then
    fail "climbon-plan data action grants blob reads: $action"
  fi
done <<<"$(az role definition list --custom-role-only true --name climbon-plan \
             --query "[].permissions[].dataActions[]" -o tsv)"

# --- GitHub environments ---------------------------------------------------------------------
for env in staging production drift; do
  if ! policy="$(gh api "repos/$repo/environments/$env" \
      --jq '[.deployment_branch_policy.protected_branches, .deployment_branch_policy.custom_branch_policies] | map(tostring) | join(" ")' 2>/dev/null)"; then
    fail "GitHub environment $env not found"
    continue
  fi
  [[ "$policy" == "false true" ]] || fail "GitHub environment $env: branch policy is '$policy', expected custom branches only"
  if [[ "$policy" == "false true" ]]; then
    compare "GitHub environment $env deployment branches" "branch:main" \
      "$(gh api "repos/$repo/environments/$env/deployment-branch-policies" \
           --jq '.branch_policies[] | "\(.type // "branch"):\(.name)"')"
  fi
  if [[ "$env" == "production" ]]; then
    compare "GitHub environment production required reviewers" "User:$github_owner" \
      "$(gh api "repos/$repo/environments/production" \
           --jq '.protection_rules[] | select(.type == "required_reviewers") | .reviewers[] | "\(.type):\(.reviewer.login // .reviewer.slug)"')"
  fi
done

if [[ "$failures" -gt 0 ]]; then
  echo "$failures difference(s)"
  exit 1
fi
echo "bootstrap matches design §6"
