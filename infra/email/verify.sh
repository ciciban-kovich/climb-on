#!/usr/bin/env bash
# Reads the Scaleway API and compares what infra/email must leave there, as exact sets, with
# design §6 (C13, C14, C26 exception, C50). Exits 1 naming every difference.
#
# Modes:
#   verify.sh                                         full check
#   verify.sh --env <env>                             <env>.tfvars matches, sending key exists
#   verify.sh --delivered <env> <n> --since <RFC3339> TEM sent >= n emails from <env> since then
#   verify.sh --readme                                README records every step 1 check
#   verify.sh --self-test                             every mode against testdata/ stubs
#
# The API modes need the owner key in SCW_ACCESS_KEY and SCW_SECRET_KEY; the modes that read
# Terraform outputs also need the owner's Azure CLI login (backend). JSON is read with Python.
set -uo pipefail
export LC_ALL=C PYTHONUTF8=1 PYTHONIOENCODING=utf-8

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# Before the temporary directory below: exec would skip its EXIT trap.
[[ "${1:-}" == --self-test ]] && exec bash "$here/testdata/self-test.sh" "$here"
api_base="https://api.scaleway.com"
region="fr-par"
envs=(staging production)

# Step 1 `owner-permission-sets`: the owner IAM application's permission sets, exactly.
owner_permission_sets=(DomainsDNSFullAccess IAMApplicationManager IAMPolicyManager ProjectManager TransactionalEmailFullAccess)
sender_permission_set="TransactionalEmailEmailSmtpCreate"
# Step 1 `billing-alert`: the owner key has no billing permission.
CHECK_BILLING_ALERT=no

step1_ids=(eea-processing dpa-accepted subprocessors-eea smtp-587-starttls iam-project-scope
  free-tier-scope billing-alert owner-permission-sets provider-no-secret-in-state sending-quota
  outbound-587 tem-validation-stable smtp-send-scoped-key)
owner_record_ids=(owner-key-created owner-key-expires billing-alert-set)
image_exts="png|jpg|jpeg|gif|webp|bmp|tif|tiff|heic|heif|avif|svg|pdf"

failures=0
fail() {
  echo "FAIL: $*"
  failures=$((failures + 1))
}
finish() {
  if ((failures)); then
    echo "$failures difference(s)"
    exit 1
  fi
  echo "${1:-infra/email matches design §6}"
  exit 0
}

python_bin="$(command -v python3 || command -v python)" || { echo "FAIL: python not found"; exit 1; }
py() { "$python_bin" "$@" | tr -d '\r'; }

# jget <expr>: evaluates a Python expression over the JSON on stdin (as d); a list prints one
# item per line.
jget() {
  py -c '
import json, sys
d = json.load(sys.stdin)
v = eval(sys.argv[1], {"d": d})
for x in (v if isinstance(v, list) else [v]):
    print("" if x is None else x)
' "$1"
}

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

tfvar() { # tfvar <file> <name>
  sed -nE "s/^[[:space:]]*$2[[:space:]]*=[[:space:]]*\"?([^\"]*)\"?[[:space:]]*$/\1/p" "$1" | tr -d '\r'
}
domain="$(tfvar "$here/email.tfvars" domain)"
org="$(tfvar "$here/email.tfvars" organization_id)"
subdomain() { case "$1" in staging) echo mail-staging ;; production) echo mail ;; esac; }

valid_env() {
  [[ " ${envs[*]} " == *" $1 "* ]] || { echo "FAIL: unknown environment '$1' (expected one of: ${envs[*]})"; exit 1; }
}

# get <path> <name>: GET into $tmp/<name>.json; on a non-2xx answer, fails and returns 1.
get() {
  local code
  code="$(curl -sS -o "$tmp/$2.json" -w '%{http_code}' -H "X-Auth-Token: ${SCW_SECRET_KEY:-}" "$api_base$1")" || code="000"
  if [[ "$code" != 2* ]]; then
    fail "GET ${1%%\?*}: HTTP $code"
    return 1
  fi
}
# list <path> <name> <key>: as get, for a list of at most 100 items (one page).
list() {
  get "$1" "$2" || return 1
  if [[ "$(jget "len(d['$3']) >= d['total_count']" <"$tmp/$2.json")" != True ]]; then
    fail "GET ${1%%\?*}: more than one page of $3; this script reads one page only"
    return 1
  fi
}

# --- Credentials ---------------------------------------------------------------------------
owner_app=""
credentials() {
  if [[ -z "${SCW_ACCESS_KEY:-}" || -z "${SCW_SECRET_KEY:-}" ]]; then
    echo "FAIL: SCW_ACCESS_KEY and SCW_SECRET_KEY must hold the owner key (README, Prerequisites)"
    exit 1
  fi
  local code
  code="$(curl -sS -o "$tmp/owner-key.json" -w '%{http_code}' -H "X-Auth-Token: $SCW_SECRET_KEY" \
    "$api_base/iam/v1alpha1/api-keys/$SCW_ACCESS_KEY")" || code="000"
  if [[ "$code" == 401 || "$code" == 403 ]]; then
    echo "FAIL: Scaleway rejected the key in SCW_ACCESS_KEY/SCW_SECRET_KEY (HTTP $code): invalid, revoked or expired; create a new owner key (README, Owner key)"
    exit 1
  elif [[ "$code" != 2* ]]; then
    echo "FAIL: could not read the owner key named by SCW_ACCESS_KEY (HTTP $code)"
    exit 1
  fi
  local problems
  problems="$(py -c '
import json, sys, datetime as dt
k = json.load(sys.stdin)
now = dt.datetime.now(dt.timezone.utc)
p = lambda s: dt.datetime.fromisoformat(s.replace("Z", "+00:00"))
if k.get("expires_at") and p(k["expires_at"]) <= now:
    print("expired")
' <"$tmp/owner-key.json")"
  if [[ "$problems" == expired ]]; then
    echo "FAIL: the owner key in SCW_ACCESS_KEY/SCW_SECRET_KEY has expired; create a new owner key (README, Owner key)"
    exit 1
  fi
  owner_app="$(jget "d.get('application_id') or ''" <"$tmp/owner-key.json")"
}

# --- Terraform outputs and tfvars ----------------------------------------------------------
outputs_read=""
outputs() {
  [[ -n "$outputs_read" ]] && return 0
  if ! (cd "$here" && terraform output -json email) >"$tmp/outputs.json" 2>"$tmp/outputs.err"; then
    fail "terraform output -json email failed in infra/email (terraform init with the owner's Azure CLI login): $(tr -d '\r' <"$tmp/outputs.err" | head -3)"
    return 1
  fi
  outputs_read=yes
}
output() { jget "d['$1']['$2']" <"$tmp/outputs.json"; } # output <env> <key>

compare_tfvars() { # compare_tfvars <env> <file>
  outputs || return 0
  local key want got
  for key in email_smtp_host email_smtp_port email_smtp_username email_sender_address; do
    want="$(output "$1" "$key")"
    got="$(tfvar "$2" "$key")"
    [[ "$got" == "$want" ]] || fail "infra/env/$1.tfvars: $key is '${got:-<missing>}', infra/email outputs '$want'"
  done
}

# --- Sender applications -------------------------------------------------------------------
declare -A project_id app_id
applications_listed=""
sender_app() { # sender_app <env>: sets app_id[<env>], or fails
  if [[ -z "$applications_listed" ]]; then
    list "/iam/v1alpha1/applications?organization_id=$org&page_size=100" applications applications || return 1
    applications_listed=yes
  fi
  local name="climbon-$1-sender" ids
  ids="$(jget "[a['id'] for a in d['applications'] if a['name'] == '$name']" <"$tmp/applications.json")"
  if [[ "$(printf '%s' "$ids" | grep -c .)" -ne 1 ]]; then
    fail "expected exactly one IAM application $name, found $(printf '%s' "$ids" | grep -c .)"
    return 1
  fi
  app_id[$1]="$ids"
}

sender_keys() { # sender_keys <env>: lists that application's API keys into $tmp/keys-<env>.json
  list "/iam/v1alpha1/api-keys?organization_id=$org&application_id=${app_id[$1]}&page_size=100" "keys-$1" api_keys
}

check_sender_policy() { # check_sender_policy <env>
  local name="climbon-$1-sender" policies rules
  list "/iam/v1alpha1/policies?organization_id=$org&application_ids=${app_id[$1]}&page_size=100" "policies-$1" policies || return
  policies="$(jget "[p['id'] for p in d['policies']]" <"$tmp/policies-$1.json")"
  if [[ "$(printf '%s' "$policies" | grep -c .)" -ne 1 ]]; then
    fail "$name: expected exactly one policy, found $(printf '%s' "$policies" | grep -c .)"
    return
  fi
  list "/iam/v1alpha1/rules?policy_id=$policies&page_size=100" "rules-$1" rules || return
  rules="$(jget "len(d['rules'])" <"$tmp/rules-$1.json")"
  if [[ "$rules" -ne 1 ]]; then
    fail "$name: expected exactly one policy rule, found $rules"
    return
  fi
  local scope sets
  scope="$(jget "sorted(d['rules'][0].get('project_ids') or []) + (['organization ' + d['rules'][0]['organization_id']] if d['rules'][0].get('organization_id') else [])" <"$tmp/rules-$1.json")"
  [[ "$scope" == "${project_id[$1]:-<climbon-$1 missing>}" ]] \
    || fail "$name: rule scope is '$(echo $scope)', expected only project climbon-$1 (${project_id[$1]:-missing})"
  sets="$(jget "sorted(d['rules'][0].get('permission_set_names') or [])" <"$tmp/rules-$1.json")"
  [[ "$sets" == "$sender_permission_set" ]] \
    || fail "$name: rule permission sets are '$(echo $sets)', expected exactly $sender_permission_set"
}

check_sender_keys() { # check_sender_keys <env>: expiry rules for every key (design §6)
  local problem
  while IFS= read -r problem; do
    [[ -n "$problem" ]] && fail "climbon-$1-sender: $problem"
  done < <(py -c '
import json, sys, datetime as dt
d = json.load(sys.stdin)
now = dt.datetime.now(dt.timezone.utc)
p = lambda s: dt.datetime.fromisoformat(s.replace("Z", "+00:00"))
def plus_year(t):
    try:
        return t.replace(year=t.year + 1)
    except ValueError:  # 29 February
        return t.replace(year=t.year + 1, month=3, day=1)
for k in d["api_keys"]:
    a, c, e = k["access_key"], p(k["created_at"]), k.get("expires_at")
    if not e:
        print(f"key {a} has no expiry")
        continue
    e = p(e)
    if e > plus_year(c):
        print(f"key {a} expires {e:%Y-%m-%d}, more than one year after its creation {c:%Y-%m-%d}")
    if e <= now + dt.timedelta(days=30):
        print(f"key {a} expires {e:%Y-%m-%d}, within 30 days")
' <"$tmp/keys-$1.json")
}

# --- Modes ---------------------------------------------------------------------------------
full() {
  credentials

  # Projects named climbon-*: exactly the two.
  if list "/account/v3/projects?organization_id=$org&page_size=100" projects projects; then
    local names env
    names="$(jget "sorted(p['name'] for p in d['projects'] if p['name'].startswith('climbon-'))" <"$tmp/projects.json")"
    [[ "$(echo $names)" == "climbon-production climbon-staging" ]] \
      || fail "projects named climbon-* are '$(echo $names)', expected exactly climbon-production climbon-staging"
    for env in "${envs[@]}"; do
      project_id[$env]="$(jget "[p['id'] for p in d['projects'] if p['name'] == 'climbon-$env'][:1]" <"$tmp/projects.json")"
    done
  fi

  # TEM domains: verified, in their own project.
  if list "/transactional-email/v1alpha1/regions/$region/domains?organization_id=$org&page_size=100" tem-domains domains; then
    local env name rows
    for env in "${envs[@]}"; do
      name="$(subdomain "$env").$domain"
      rows="$(jget "[x['status'] + ' ' + x['project_id'] for x in d['domains'] if x['name'] == '$name' and x['status'] != 'revoked']" <"$tmp/tem-domains.json")"
      if [[ "$(printf '%s' "$rows" | grep -c .)" -ne 1 ]]; then
        fail "TEM domain $name: expected exactly one, found $(printf '%s' "$rows" | grep -c .)"
        continue
      fi
      [[ "${rows%% *}" == checked ]] || fail "TEM domain $name is '${rows%% *}', expected checked (verified)"
      [[ "${rows#* }" == "${project_id[$env]:-}" ]] || fail "TEM domain $name is in project ${rows#* }, expected climbon-$env"
    done
  fi

  # Sender applications: one policy, one rule, own project, SMTP sending only; key expiry.
  local env
  for env in "${envs[@]}"; do
    sender_app "$env" || continue
    check_sender_policy "$env"
    sender_keys "$env" && check_sender_keys "$env"
  done

  # The owner key: at most 90 days, and its application's permission sets exactly step 1's.
  local problem
  problem="$(py -c '
import json, sys, datetime as dt
k = json.load(sys.stdin)
p = lambda s: dt.datetime.fromisoformat(s.replace("Z", "+00:00"))
if not k.get("expires_at"):
    print("has no expiry")
else:
    c, e = p(k["created_at"]), p(k["expires_at"])
    if e > c + dt.timedelta(days=90):
        print(f"expires {e:%Y-%m-%d}, more than 90 days after its creation {c:%Y-%m-%d}")
' <"$tmp/owner-key.json")"
  [[ -z "$problem" ]] || fail "owner key $SCW_ACCESS_KEY $problem"
  if [[ -z "$owner_app" ]]; then
    fail "owner key $SCW_ACCESS_KEY belongs to no IAM application; use the owner IAM application's key"
  elif list "/iam/v1alpha1/policies?organization_id=$org&application_ids=$owner_app&page_size=100" owner-policies policies; then
    local policy sets=""
    for policy in $(jget "[p['id'] for p in d['policies']]" <"$tmp/owner-policies.json"); do
      list "/iam/v1alpha1/rules?policy_id=$policy&page_size=100" "owner-rules" rules || continue
      sets+="$(jget "[s for r in d['rules'] for s in (r.get('permission_set_names') or [])]" <"$tmp/owner-rules.json")"$'\n'
    done
    sets="$(printf '%s' "$sets" | sed '/^$/d' | sort -u | tr '\n' ' ')"
    [[ "$sets" == "$(printf '%s\n' "${owner_permission_sets[@]}" | sort -u | tr '\n' ' ')" ]] \
      || fail "owner application permission sets are '${sets% }', expected exactly '${owner_permission_sets[*]}' (step 1 owner-permission-sets)"
  fi

  # The registered domain renews itself.
  if get "/domain/v2beta1/domains/$domain" registrar-domain; then
    local renew
    renew="$(jget "d.get('auto_renew_status')" <"$tmp/registrar-domain.json")"
    [[ "$renew" == enabled ]] || fail "domain $domain: auto-renewal is '$renew', expected enabled"
  fi

  if [[ "$CHECK_BILLING_ALERT" == yes ]]; then
    fail "CHECK_BILLING_ALERT=yes, but no billing alert read is implemented; record step 1 billing-alert again"
  fi

  # Each environment's tfvars that exists carries this root's outputs.
  for env in "${envs[@]}"; do
    [[ -f "$here/../env/$env.tfvars" ]] && compare_tfvars "$env" "$here/../env/$env.tfvars"
  done
  finish
}

env_mode() {
  valid_env "$1"
  credentials
  local file="$here/../env/$1.tfvars"
  if [[ -f "$file" ]]; then
    compare_tfvars "$1" "$file"
  else
    fail "infra/env/$1.tfvars does not exist"
  fi
  if sender_app "$1" && sender_keys "$1"; then
    [[ "$(jget "len(d['api_keys'])" <"$tmp/keys-$1.json")" -ge 1 ]] \
      || fail "climbon-$1-sender has no API key (create the sending key, README step 7 and task 31)"
  fi
  finish "infra/email matches infra/env/$1.tfvars and climbon-$1-sender has a key"
}

delivered() { # delivered <env> <n> <since>
  valid_env "$1"
  [[ "$2" =~ ^[0-9]+$ ]] || { echo "FAIL: --delivered: <n> is '$2', expected a whole number"; exit 1; }
  if [[ -z "$3" ]] || ! py -c '
import re, sys, datetime as dt
s = sys.argv[1]
if not re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})", s):
    sys.exit(1)
dt.datetime.fromisoformat(s.replace("Z", "+00:00"))
' "$3" 2>/dev/null; then
    echo "FAIL: --since is '$3', expected an RFC 3339 time such as 2026-10-12T15:04:05Z"
    exit 1
  fi
  credentials
  outputs || finish
  local sender project since_q count
  sender="$(output "$1" email_sender_address)"
  project="$(output "$1" email_smtp_username)"
  since_q="$(py -c 'import sys, urllib.parse as u; print(u.quote(sys.argv[1], safe=""))' "$3")"
  list "/transactional-email/v1alpha1/regions/$region/emails?project_id=$project&statuses=sent&since=$since_q&page_size=100" emails emails || finish
  # Status, sender and time only (C32): subjects and recipients are never read.
  count="$(py -c '
import json, sys, datetime as dt
d = json.load(sys.stdin)
p = lambda s: dt.datetime.fromisoformat(s.replace("Z", "+00:00"))
since, sender = p(sys.argv[1]), sys.argv[2].lower()
print(sum(1 for e in d["emails"]
          if e.get("status") == "sent"
          and (e.get("mail_from") or "").lower().split("<")[-1].rstrip(">").strip() == sender
          and p(e["created_at"]) >= since))
' "$3" "$sender" <"$tmp/emails.json")"
  [[ "$count" -ge "$2" ]] || fail "TEM lists $count email(s) sent from $sender since $3, expected at least $2"
  finish "TEM lists $count email(s) sent from $sender since $3"
}

readme() {
  local top
  if ! top="$(git rev-parse --show-toplevel 2>&1)"; then
    fail "git rev-parse --show-toplevel failed: $(printf '%s' "$top" | tr -d '\r' | head -1)"
    finish
  fi
  top="${top%$'\r'}"
  local file="$top/infra/email/README.md"
  if [[ ! -f "$file" ]]; then
    fail "$file does not exist"
    finish
  fi
  tr -d '\r' <"$file" >"$tmp/readme.md"

  local id rows n res date source
  for id in "${step1_ids[@]}"; do
    rows="$(grep -E "^\|[[:space:]]*$id[[:space:]]*\|" "$tmp/readme.md")"
    n="$(printf '%s' "$rows" | grep -c .)"
    if [[ "$n" -ne 1 ]]; then
      fail "README: expected one step 1 row for $id, found $n"
      continue
    fi
    if [[ ! "$rows" =~ ^\|[[:space:]]*$id[[:space:]]*\|[[:space:]]*([^|]*[^|[:space:]])?[[:space:]]*\|[[:space:]]*([^|]*[^|[:space:]])?[[:space:]]*\|[[:space:]]*(.*[^[:space:]])?[[:space:]]*\|[[:space:]]*$ ]]; then
      fail "README: step 1 row for $id is not '| $id | PASS | <YYYY-MM-DD> | <source> |'"
      continue
    fi
    res="${BASH_REMATCH[1]}" date="${BASH_REMATCH[2]}" source="${BASH_REMATCH[3]}"
    [[ "$res" == PASS ]] || fail "README: $id result is '$res', expected PASS"
    valid_date "$date" || fail "README: $id date '$date' is not a valid YYYY-MM-DD date"
    [[ -n "$source" ]] || fail "README: $id has no source"
    if [[ "$id" == dpa-accepted && -n "$source" ]] \
      && [[ ! "$source" =~ ^version[[:space:]]+([^;]*[^;[:space:]])[[:space:]]*\;[[:space:]]*evidence[[:space:]]+(.*[^[:space:]])[[:space:]]*$ ]]; then
      fail "README: dpa-accepted source is '$source', expected 'version <DPA version>; evidence <where the evidence is kept>'"
    fi
  done

  for id in "${owner_record_ids[@]}"; do
    rows="$(grep -E "^\|[[:space:]]*$id[[:space:]]*\|" "$tmp/readme.md")"
    n="$(printf '%s' "$rows" | grep -c .)"
    if [[ "$n" -ne 1 ]]; then
      fail "README: expected one owner record row for $id, found $n"
    elif [[ ! "$rows" =~ ^\|[[:space:]]*$id[[:space:]]*\|[[:space:]]*([^|]*[^|[:space:]])?[[:space:]]*\|[[:space:]]*$ ]] \
      || ! valid_date "${BASH_REMATCH[1]}"; then
      fail "README: owner record $id is not '| $id | <YYYY-MM-DD> |' with a valid date"
    fi
  done

  # No DPA evidence in the repository: no tracked path with the word dpa, no image or PDF
  # under infra/email.
  local listing="$tmp/ls-files" path lower tracked=0
  if ! git -C "$top" ls-files -z >"$listing" 2>"$tmp/ls-files.err"; then
    fail "git -C $top ls-files -z failed: $(tr -d '\r' <"$tmp/ls-files.err" | head -1)"
    finish
  fi
  while IFS= read -r -d '' path; do
    tracked=$((tracked + 1))
    lower="${path,,}"
    [[ "$lower" =~ (^|[^a-z])dpa([^a-z]|$) ]] \
      && fail "tracked file '$path' has the word dpa in its path; DPA evidence is kept outside the repository"
    [[ "$lower" == infra/email/* && "$lower" =~ \.($image_exts)$ ]] \
      && fail "tracked image or PDF under infra/email: '$path'; DPA evidence is kept outside the repository"
  done <"$listing"
  ((tracked)) || fail "git -C $top ls-files -z listed nothing; refusing to treat an empty listing as clean"
  finish "README records every step 1 check and owner record; no DPA evidence is tracked"
}

valid_date() {
  [[ "$1" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]] && [[ "$(date -u -d "$1" +%Y-%m-%d 2>/dev/null)" == "$1" ]]
}

case "${1:-}" in
  "") full ;;
  --env)
    [[ $# -eq 2 ]] || { echo "usage: verify.sh --env <env>"; exit 1; }
    env_mode "$2" ;;
  --delivered)
    [[ $# -eq 5 && "$4" == --since ]] || { echo "FAIL: usage: verify.sh --delivered <env> <n> --since <RFC 3339>"; exit 1; }
    delivered "$2" "$3" "$5" ;;
  --readme) readme ;;
  *) echo "usage: verify.sh [--env <env> | --delivered <env> <n> --since <t> | --readme | --self-test]"; exit 1 ;;
esac
