#!/usr/bin/env bash
# verify.sh --self-test: runs every mode of a copy of verify.sh against the stub curl, terraform
# and git in stubs/, serving the fixtures in this directory. Each case states the exit code and a
# phrase the output must contain, so a case cannot pass by failing for another reason.
set -uo pipefail
export LC_ALL=C PYTHONUTF8=1 PYTHONIOENCODING=utf-8

root="$1"                     # infra/email
td="$root/testdata"
python_bin="$(command -v python3 || command -v python)"
staging_app=20000000-0000-4000-8000-000000000001
production_app=20000000-0000-4000-8000-000000000002
staging_policy=30000000-0000-4000-8000-000000000001

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
repo="$tmp/repo"                # a copy laid out as the repository: infra/email, infra/env
mkdir -p "$repo/infra/email"
cp "$root/verify.sh" "$repo/infra/email/verify.sh"
cp "$td/email.tfvars" "$repo/infra/email/email.tfvars"

mismatches=0
cases=0

# prep: fresh API fixtures, both tfvars, the all-rows README and the base listing.
prep() {
  rm -rf "$tmp/api" "$repo/infra/env" "$tmp/top"
  cp -r "$td/api" "$tmp/api"
  mkdir -p "$repo/infra/env" "$tmp/top/infra/email"
  cp "$td/staging.tfvars" "$td/production.tfvars" "$repo/infra/env/"
  cp "$td/readme-all-rows.md" "$tmp/top/infra/email/README.md"
  cp "$td/ls-files.txt" "$tmp/ls-files.txt"
  git_fail=""
}

# jedit <api fixture name> <python statement over d>
jedit() {
  "$python_bin" -c 'import json, sys
d = json.load(sys.stdin)
exec(sys.argv[1])
json.dump(d, sys.stdout)' "$2" <"$tmp/api/$1.json" >"$tmp/api/$1.new" && mv "$tmp/api/$1.new" "$tmp/api/$1.json"
}

# readme_edit <sed expression>
readme_edit() { sed -i -E "$1" "$tmp/top/infra/email/README.md"; }

# expect <exit> <phrase> <description> <key|nokey> -- <verify.sh arguments>
expect() {
  local want="$1" phrase="$2" desc="$3" key="$4" out code
  shift 5
  local creds=()
  [[ "$key" == key ]] && creds=(SCW_ACCESS_KEY=SCWFIXOWNER SCW_SECRET_KEY=fixture-secret)
  out="$(env -u SCW_ACCESS_KEY -u SCW_SECRET_KEY "${creds[@]}" \
    PATH="$td/stubs:$PATH" STUB_API_DIR="$tmp/api" STUB_TF_OUTPUT="$td/terraform-output.json" \
    STUB_GIT_TOP="$tmp/top" STUB_GIT_LS="$tmp/ls-files.txt" STUB_GIT_FAIL="$git_fail" \
    bash "$repo/infra/email/verify.sh" "$@" 2>&1)"
  code=$?
  report "$want" "$phrase" "$desc" "$code" "$out"
}

report() { # report <want> <phrase> <description> <code> <output>
  cases=$((cases + 1))
  if [[ "$4" == "$1" ]] && grep -qF -- "$2" <<<"$5"; then
    echo "ok    $3"
  else
    mismatches=$((mismatches + 1))
    echo "FAIL  $3: expected exit $1 and output containing '$2', got exit $4:"
    sed 's/^/        /' <<<"$5"
  fi
}

since="$(date -u -d '-2 days' +%Y-%m-%dT%H:%M:%SZ)"

# --- Full mode -----------------------------------------------------------------------------
prep
expect 0 "infra/email matches design" "full: all good" key --
prep; jedit tem-domains 'd["domains"][0]["status"] = "pending"'
expect 1 "mail-staging.example.net is 'pending'" "full: a domain not verified" key --
prep; jedit projects 'd["projects"].append({"id": "10000000-0000-4000-8000-000000000009", "name": "climbon-test"}); d["total_count"] = 4'
expect 1 "projects named climbon-* are" "full: an extra climbon-* project" key --
prep; jedit "rules-$staging_policy" 'd["rules"][0]["project_ids"].append("10000000-0000-4000-8000-000000000003")'
expect 1 "climbon-staging-sender: rule scope is" "full: a rule naming a second project" key --
prep; jedit "rules-$staging_policy" 'd["rules"][0]["permission_set_names"].append("TransactionalEmailEmailApiCreate")'
expect 1 "climbon-staging-sender: rule permission sets are" "full: a rule naming a second permission set" key --
prep; jedit "rules-$staging_policy" 'd["rules"][0]["permission_set_names"] = ["TransactionalEmailEmailApiCreate"]'
expect 1 "rule permission sets are 'TransactionalEmailEmailApiCreate'" "full: a sender policy with only TransactionalEmailEmailApiCreate" key --
prep; jedit "keys-$staging_app" 'd["api_keys"][0]["expires_at"] = "@DAYS:29@"'
expect 1 "SCWFIXSTAGING expires" "full: a sending key expiring in 29 days" key --
prep; jedit "keys-$staging_app" 'd["api_keys"][0]["expires_at"] = None'
expect 1 "key SCWFIXSTAGING has no expiry" "full: a sending key without expiry" key --
prep; jedit "keys-$staging_app" 'd["api_keys"][0]["expires_at"] = "@DAYS:400@"'
expect 1 "more than one year after its creation" "full: a sending key expiring more than a year after creation" key --
prep; rm "$repo/infra/env/production.tfvars"; jedit "keys-$production_app" 'd["api_keys"][0]["expires_at"] = None'
expect 1 "climbon-production-sender: key SCWFIXPRODUCTION has no expiry" "full: a production key without expiry, no production.tfvars" key --
prep; jedit registrar-domain 'd["auto_renew_status"] = "disabled"'
expect 1 "auto-renewal is 'disabled'" "full: auto-renewal off" key --
prep; sed -i 's/noreply@mail-staging/sender@mail-staging/' "$repo/infra/env/staging.tfvars"
expect 1 "infra/env/staging.tfvars: email_sender_address is" "full: a tfvars email value differing from the output" key --
prep; jedit "keys-$staging_app" 'd["api_keys"] = []; d["total_count"] = 0'
expect 0 "infra/email matches design" "full: staging.tfvars present, no staging key" key --
expect 1 "climbon-staging-sender has no API key" "--env staging: staging.tfvars present, no staging key" key -- --env staging
prep; jedit owner-key 'd["created_at"] = "@DAYS:-1@"; d["expires_at"] = "@DAYS:90@"'
expect 1 "more than 90 days after its creation" "full: the owner key expiring 91 days after creation" key --

# --- Credentials ---------------------------------------------------------------------------
prep
expect 1 "SCW_ACCESS_KEY and SCW_SECRET_KEY must hold the owner key" "full: SCW_* unset" nokey --
expect 1 "SCW_ACCESS_KEY and SCW_SECRET_KEY must hold the owner key" "--env: SCW_* unset" nokey -- --env staging
prep; echo 401 >"$tmp/api/owner-key.status"
expect 1 "Scaleway rejected the key in SCW_ACCESS_KEY/SCW_SECRET_KEY" "full: the API rejects the key" key --
prep; jedit owner-key 'd["created_at"] = "@DAYS:-31@"; d["expires_at"] = "@DAYS:-1@"'
expect 1 "SCW_ACCESS_KEY/SCW_SECRET_KEY has expired" "full: the owner key has expired" key --

# --- --env ---------------------------------------------------------------------------------
prep
expect 0 "climbon-staging-sender has a key" "--env staging: all good" key -- --env staging
prep; rm "$repo/infra/env/staging.tfvars"
expect 1 "infra/env/staging.tfvars does not exist" "--env staging: tfvars missing" key -- --env staging

# --- --delivered ---------------------------------------------------------------------------
prep
expect 0 "TEM lists 2 email(s) sent from noreply@mail-staging.example.net" "--delivered staging 2: two sent since t" key -- --delivered staging 2 --since "$since"
prep; jedit emails 'del d["emails"][1]; d["total_count"] = 3'
expect 1 "TEM lists 1 email(s) sent" "--delivered staging 2: one sent" key -- --delivered staging 2 --since "$since"
prep; jedit emails 'd["emails"][0]["created_at"] = "@DAYS:-4@"; d["emails"][1]["created_at"] = "@DAYS:-3@"; d["emails"].append(dict(d["emails"][1], created_at="@DAYS:0@")); d["total_count"] = 5'
expect 1 "TEM lists 1 email(s) sent" "--delivered staging 2: two sent before t, one after" key -- --delivered staging 2 --since "$since"
prep
expect 1 "--since is ''" "--delivered: --since empty" key -- --delivered staging 2 --since ""
expect 1 "--since is 'yesterday'" "--delivered: --since yesterday" key -- --delivered staging 2 --since yesterday

# --- --readme (SCW_* unset throughout) -----------------------------------------------------
prep
expect 0 "README records every step 1 check" "--readme: all rows" nokey -- --readme
prep; readme_edit '/^\| dpa-accepted /d'
expect 1 "expected one step 1 row for dpa-accepted, found 0" "--readme: dpa-accepted missing" nokey -- --readme
prep; readme_edit 's/version 2024-05; //'
expect 1 "dpa-accepted source is" "--readme: dpa-accepted without a version" nokey -- --readme
prep; readme_edit 's/; evidence [^|]*/ /'
expect 1 "dpa-accepted source is" "--readme: dpa-accepted without an evidence location" nokey -- --readme
prep; { echo "docs/Scaleway-DPA-2026.png"; cat "$td/ls-files.txt"; } >"$tmp/ls-files.txt"
expect 1 "tracked file 'docs/Scaleway-DPA-2026.png' has the word dpa" "--readme: a tracked file named DPA" nokey -- --readme
prep; echo "infra/email/acceptance.png" >>"$tmp/ls-files.txt"
expect 1 "tracked image or PDF under infra/email: 'infra/email/acceptance.png'" "--readme: a tracked image under infra/email" nokey -- --readme
prep; echo "infra/email/Scan.AVIF" >>"$tmp/ls-files.txt"
expect 1 "tracked image or PDF under infra/email: 'infra/email/Scan.AVIF'" "--readme: an upper-case .AVIF under infra/email" nokey -- --readme
prep; echo "src/ClimbOn.Infrastructure/IdpAdapter.cs" >>"$tmp/ls-files.txt"
expect 0 "README records every step 1 check" "--readme: a tracked IdpAdapter.cs, all rows" nokey -- --readme
prep; git_fail=ls-files
expect 1 "ls-files -z failed: fatal: index file corrupt" "--readme: git ls-files exits non-zero" nokey -- --readme
prep; git_fail=rev-parse
expect 1 "git rev-parse --show-toplevel failed" "--readme: git rev-parse exits non-zero" nokey -- --readme
prep; readme_edit '/^\| smtp-send-scoped-key /d'
expect 1 "expected one step 1 row for smtp-send-scoped-key, found 0" "--readme: smtp-send-scoped-key missing" nokey -- --readme
prep; readme_edit 's/^\| eea-processing \| PASS/| eea-processing | FAIL/'
expect 1 "eea-processing result is 'FAIL'" "--readme: a FAIL row" nokey -- --readme
prep; readme_edit 's/^(\| subprocessors-eea \| PASS \|) 2026-10-08 /\1 /'
expect 1 "subprocessors-eea date '' is not a valid" "--readme: a row without a date" nokey -- --readme
for id in owner-key-created owner-key-expires billing-alert-set; do
  prep; readme_edit "/^\\| $id /d"
  expect 1 "expected one owner record row for $id, found 0" "--readme: $id missing" nokey -- --readme
done

# A real repository: real git, -C <toplevel>, -z and a non-ASCII name.
real="$tmp/real"
mkdir -p "$real/infra/email"
cp "$td/readme-all-rows.md" "$real/infra/email/README.md"
printf 'not an image\n' >"$real/infra/email/skjermbilde-æ.png"
export GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_NOSYSTEM=1
git -C "$real" init -q && git -C "$real" add infra/email/README.md "infra/email/skjermbilde-æ.png"
out="$(cd "$real/infra/email" && env -u SCW_ACCESS_KEY -u SCW_SECRET_KEY bash "$repo/infra/email/verify.sh" --readme 2>&1)"
report 1 "tracked image or PDF under infra/email: 'infra/email/skjermbilde-æ.png'" \
  "--readme: a real repository with infra/email/skjermbilde-æ.png, run from a subdirectory" "$?" "$out"

echo
if ((mismatches)); then
  echo "self-test: $mismatches of $cases cases did not behave as expected"
  exit 1
fi
echo "self-test: all $cases cases behaved as expected"
