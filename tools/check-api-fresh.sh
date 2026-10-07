#!/usr/bin/env bash
# Fails when openapi/ or the generated client differs from what the code generates (C48).
# Never writes to the working tree: it copies the files git would commit (tracked, plus untracked
# and not ignored) to a temporary directory, runs tools/regenerate-api.sh there, and compares.
#
#   check-api-fresh.sh              exit 0 fresh, 1 stale, 2 could not check
#   check-api-fresh.sh --self-test  exit 0 when the check catches every planted staleness
set -euo pipefail

generated=(openapi src/ClimbOn.Client.Api/Generated)

script="tools/check-api-fresh.sh"

could_not_check() {
    echo "api-diff: $1, so freshness could not be checked" >&2
    exit 2
}

root="$(git -C "$(dirname "$0")" rev-parse --show-toplevel)" || could_not_check "no git repository"

scratch="$(mktemp -d)" || could_not_check "no temporary directory"
trap 'rm -rf "$scratch" 2>/dev/null || true' EXIT

# Copies the files git would commit from $1 to the new directory $2.
copy_tree() {
    mkdir -p "$2" \
        && (cd "$1" && git ls-files -z -co --exclude-standard \
            | while IFS= read -r -d '' file; do [ -e "$file" ] && printf '%s\0' "$file"; done \
            | tar --null -T - -cf -) \
        | tar -xf - -C "$2"
}

check() {
    copy_tree "$root" "$scratch/original" || could_not_check "copying the repository failed"
    copy_tree "$root" "$scratch/work" || could_not_check "copying the repository failed"

    if ! (cd "$scratch/work" && bash tools/regenerate-api.sh) >"$scratch/regenerate.log" 2>&1; then
        cat "$scratch/regenerate.log"
        could_not_check "regeneration failed"
    fi

    local stale=0 dir status
    for dir in "${generated[@]}"; do
        mkdir -p "$scratch/original/$dir" "$scratch/work/$dir" || could_not_check "copying the repository failed"
        status=0
        diff -r --strip-trailing-cr "$scratch/original/$dir" "$scratch/work/$dir" || status=$?
        case "$status" in
            0) ;;
            1) stale=1 ;;
            *) could_not_check "comparing $dir failed" ;;
        esac
    done

    if [ "$stale" -ne 0 ]; then
        echo "api-diff: generated API files are stale; run tools/regenerate-api.sh and commit the result" >&2
        return 1
    fi
    echo "api-diff: openapi/ and the generated client are fresh"
}

self_test() {
    local failures=0

    # A copy of the repository with the files git would commit, as a repository of its own.
    new_case() {
        local dir
        dir="$(mktemp -d "$scratch/case.XXXXXX")"
        copy_tree "$root" "$dir"
        git -C "$dir" init -q
        git -C "$dir" -c core.autocrlf=false -c core.safecrlf=false add -A
        printf '%s' "$dir"
    }

    # Runs the copy's own check, with any further arguments as environment assignments, and
    # compares its exit with the expected one.
    expect() {
        local expected="$1" description="$2" dir="$3" actual=0
        (cd "$dir" && env "${@:4}" bash "$script") >"$scratch/case.log" 2>&1 || actual=$?
        if [ "$actual" -eq "$expected" ]; then
            echo "self-test pass: $description (exit $actual)"
        else
            cat "$scratch/case.log"
            echo "self-test FAIL: $description: expected exit $expected, got $actual" >&2
            failures=$((failures + 1))
        fi
    }

    # Adds $2 to the copy $1's Program.cs just before app.Run().
    add_to_program() {
        sed -i "s|^app\\.Run();\$|$2\\napp.Run();|" "$1/src/ClimbOn.Api/Program.cs"
        grep -qF "$2" "$1/src/ClimbOn.Api/Program.cs"
    }

    local before after dir
    before="$(git -C "$root" status --porcelain)"
    expect 0 "the repository as it is" "$root"
    after="$(git -C "$root" status --porcelain)"
    if [ "$before" = "$after" ]; then
        echo "self-test pass: git status of the repository is unchanged by a run"
    else
        echo "self-test FAIL: a run changed git status of the repository" >&2
        diff <(printf '%s\n' "$before") <(printf '%s\n' "$after") || true
        failures=$((failures + 1))
    fi

    dir="$(new_case)"
    printf 'junk\n' >>"$dir/openapi/climber.json"
    expect 1 "junk appended to openapi/climber.json" "$dir"

    dir="$(new_case)"
    printf '// junk\n' >>"$dir/src/ClimbOn.Client.Api/Generated/ClimberApiClient.cs"
    expect 1 "junk appended to a generated client file" "$dir"

    dir="$(new_case)"
    if add_to_program "$dir" 'app.MapGet("/api/climber/self-test-probe", () => "probe");'; then
        expect 1 "an endpoint added without regenerating" "$dir"
    else
        echo "self-test FAIL: could not add the probe endpoint to Program.cs" >&2
        failures=$((failures + 1))
    fi

    # Generation runs in DocumentGeneration.EnvironmentName: an endpoint mapped only there shows up.
    dir="$(new_case)"
    if add_to_program "$dir" 'if (app.Environment.IsEnvironment(DocumentGeneration.EnvironmentName)) app.MapGet("/api/climber/self-test-generation", () => "probe");'; then
        expect 1 "an endpoint mapped only in the generation environment" "$dir"
    else
        echo "self-test FAIL: could not add the generation-environment probe to Program.cs" >&2
        failures=$((failures + 1))
    fi

    expect 2 "no temporary directory" "$root" TMPDIR="$scratch/missing"

    if [ "$failures" -ne 0 ]; then
        echo "api-diff self-test: $failures case(s) failed" >&2
        return 1
    fi
    echo "api-diff self-test: all cases passed"
}

case "${1:-}" in
    "") check ;;
    --self-test) self_test ;;
    *) echo "usage: $script [--self-test]" >&2; exit 2 ;;
esac
