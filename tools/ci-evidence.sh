#!/usr/bin/env bash
# Has there ever been a completed run of <workflow-file> in which every named job (or job:step) succeeded?
#
#   ci-evidence.sh <workflow-file> <event|any> <branch|any> <job>[:<step>]...
#   ci-evidence.sh --self-test
#
# Exit 0: such a run exists (its id is printed). Exit 1: none of the last 100 runs qualifies. Exit 2: usage error.
# A skipped, cancelled or missing job or step never counts as success.
set -euo pipefail

usage() {
    echo "usage: $0 <workflow-file> <event|any> <branch|any> <job>[:<step>]..." >&2
    echo "       $0 --self-test" >&2
    exit 2
}

find_python() {
    local candidate
    for candidate in python3 python; do
        if command -v "$candidate" >/dev/null 2>&1 && "$candidate" -c 'import json' >/dev/null 2>&1; then
            echo "$candidate"
            return 0
        fi
    done
    echo "error: python3 or python is required" >&2
    exit 2
}

# Reads `gh run list --json databaseId,status` from stdin; prints the ids of completed runs, newest first.
completed_run_ids() {
    "$PYTHON" -c '
import json, sys
for run in json.load(sys.stdin):
    if run.get("status") == "completed":
        print(run["databaseId"])
'
}

# Reads `gh run view --json jobs` from stdin; exits 0 iff every target succeeded.
all_targets_succeeded() {
    "$PYTHON" -c '
import json, sys
jobs = json.load(sys.stdin).get("jobs") or []
for target in sys.argv[1:]:
    job_name, _, step_name = target.partition(":")
    matching = [j for j in jobs if j.get("name") == job_name]
    if not matching:
        sys.exit(1)
    for job in matching:
        if not step_name:
            ok = job.get("conclusion") == "success"
        else:
            steps = [s for s in job.get("steps") or [] if s.get("name") == step_name]
            ok = bool(steps) and all(s.get("conclusion") == "success" for s in steps)
        if not ok:
            sys.exit(1)
' "$@"
}

evidence() {
    [ $# -ge 4 ] || usage
    local workflow=$1 event=$2 branch=$3
    shift 3

    local list_args=(run list --workflow "$workflow" --limit 100 --json databaseId,status)
    [ "$event" = any ] || list_args+=(--event "$event")
    [ "$branch" = any ] || list_args+=(--branch "$branch")

    local runs ids id
    runs=$(gh "${list_args[@]}")
    ids=$(printf '%s' "$runs" | completed_run_ids | tr -d '\r')
    for id in $ids; do
        if gh run view "$id" --json jobs | all_targets_succeeded "$@"; then
            echo "run $id: $* succeeded"
            return 0
        fi
    done

    echo "no completed $workflow run (event $event, branch $branch) in which $* all succeeded" >&2
    return 1
}

self_test() {
    local dir failures=0
    dir=$(mktemp -d)
    trap 'rm -rf "$dir"' RETURN

    mkdir -p "$dir/bin"
    cat >"$dir/bin/gh" <<'STUB'
#!/usr/bin/env bash
# Stub gh: `run list` prints $FIXTURE/list.json and records its arguments; `run view <id>` prints $FIXTURE/view-<id>.json.
set -euo pipefail
case "$1 $2" in
    "run list") printf '%s\n' "$*" >"$FIXTURE/list-args"; cat "$FIXTURE/list.json" ;;
    "run view") cat "$FIXTURE/view-$3.json" ;;
    *) echo "stub gh: unexpected: $*" >&2; exit 99 ;;
esac
STUB
    chmod +x "$dir/bin/gh"

    local ok='{"name":"ci","conclusion":"success","steps":[{"name":"spec-trace","conclusion":"success"},{"name":"secret-scan","conclusion":"success"}]}'
    local lint='{"name":"lint","conclusion":"success","steps":[]}'
    local skipped='{"name":"lint","conclusion":"skipped","steps":[]}'
    local step_failed='{"name":"ci","conclusion":"success","steps":[{"name":"spec-trace","conclusion":"failure"},{"name":"secret-scan","conclusion":"success"}]}'

    # case <name> <expected exit> <list.json> [<id> <jobs json>]...
    run_case() {
        local name=$1 expected=$2 list=$3 actual=0
        shift 3
        export FIXTURE="$dir/$name"
        mkdir -p "$FIXTURE"
        printf '%s' "$list" >"$FIXTURE/list.json"
        while [ $# -gt 0 ]; do
            printf '{"jobs":[%s]}' "$2" >"$FIXTURE/view-$1.json"
            shift 2
        done
        PATH="$dir/bin:$PATH" evidence ci.yml push main ci:spec-trace ci:secret-scan lint >/dev/null 2>&1 || actual=$?
        if [ "$actual" -eq "$expected" ]; then
            echo "ok   $name (exit $actual)"
        else
            echo "FAIL $name: expected exit $expected, got $actual"
            failures=$((failures + 1))
        fi
    }

    run_case all-success 0 '[{"databaseId":1,"status":"completed"}]' 1 "$ok,$lint"
    run_case job-skipped 1 '[{"databaseId":1,"status":"completed"}]' 1 "$ok,$skipped"
    run_case job-missing 1 '[{"databaseId":1,"status":"completed"}]' 1 "$ok"
    run_case step-failed 1 '[{"databaseId":1,"status":"completed"}]' 1 "$step_failed,$lint"
    run_case older-run-success 0 '[{"databaseId":2,"status":"completed"},{"databaseId":1,"status":"completed"}]' \
        2 "$step_failed,$lint" 1 "$ok,$lint"
    run_case in-progress-ignored 1 '[{"databaseId":1,"status":"in_progress"}]' 1 "$ok,$lint"
    run_case no-runs 1 '[]'

    if ! grep -q -- '--workflow ci.yml .*--event push --branch main' "$dir/older-run-success/list-args"; then
        echo "FAIL filters: gh run list was called with: $(cat "$dir/older-run-success/list-args")"
        failures=$((failures + 1))
    else
        echo "ok   filters passed to gh run list"
    fi

    if [ "$failures" -ne 0 ]; then
        echo "self-test: $failures failure(s)" >&2
        return 1
    fi
    echo "self-test: passed"
}

PYTHON=$(find_python)

if [ "${1:-}" = --self-test ]; then
    [ $# -eq 1 ] || usage
    self_test
else
    evidence "$@"
fi
