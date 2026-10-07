#!/usr/bin/env bash
# Regenerates openapi/climber.json and the Kiota client in src/ClimbOn.Client.Api/Generated in place
# (C47). Run it after changing an endpoint, and commit what it writes; tools/check-api-fresh.sh fails
# until you do (C48).
set -euo pipefail

cd "$(dirname "$0")/.."

export DOTNET_NOLOGO=true DOTNET_CLI_TELEMETRY_OPTOUT=true KIOTA_TUTORIAL_ENABLED=false

dotnet tool restore >/dev/null

# --no-incremental: an up-to-date build would skip document generation.
dotnet build src/ClimbOn.Api/ClimbOn.Api.csproj -c Release --no-incremental -p:RegenerateApiClients=true

# Kiota exits 0 after logging an OpenAPI error, even when it has deleted the client, so its
# output decides.
kiota_output="$(mktemp)"
trap 'rm -f "$kiota_output"' EXIT
dotnet kiota generate --language CSharp \
    --openapi openapi/climber.json \
    --output src/ClimbOn.Client.Api/Generated \
    --class-name ClimberApiClient \
    --namespace-name ClimbOn.Client.Api \
    --exclude-backward-compatible \
    --clean-output | tee "$kiota_output"
if grep -Eq '^(fail|crit):' "$kiota_output"; then
    echo "regenerate-api: kiota reported errors; the generated client is incomplete" >&2
    exit 1
fi

# Kiota's run log is not part of the client.
rm -f src/ClimbOn.Client.Api/Generated/.kiota.log
