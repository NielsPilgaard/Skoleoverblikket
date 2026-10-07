#!/usr/bin/env bash
# SessionStart hook: prepares a Claude Code cloud session (claude.ai/code, Claude app).
# Does nothing on a local machine. Idempotent: a resumed session skips what is already there.
# Installs the .NET 10 SDK (not in the cloud image) and web dependencies, which also
# enables the .githooks pre-commit formatter. Docker is not assumed: integration tests
# and e2e run in PR CI, see "Cloud sessions" in AGENTS.md.

[ "$CLAUDE_CODE_REMOTE" = "true" ] || exit 0

set -euo pipefail
cd "$CLAUDE_PROJECT_DIR"

dotnet_dir="$HOME/.dotnet"
if ! { command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks | grep -q '^10\.'; } &&
	! [ -x "$dotnet_dir/dotnet" ]; then
	curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir "$dotnet_dir" >/dev/null
fi

if [ -x "$dotnet_dir/dotnet" ]; then
	export DOTNET_ROOT="$dotnet_dir"
	export PATH="$dotnet_dir:$PATH"
	if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
		{
			echo "export DOTNET_ROOT=\"$dotnet_dir\""
			echo "export PATH=\"$dotnet_dir:\$PATH\""
			echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
			echo "export DOTNET_NOLOGO=1"
		} >>"$CLAUDE_ENV_FILE"
	fi
fi

[ -d web/node_modules ] || npm ci --prefix web --no-audit --no-fund >/dev/null

dotnet restore api/Skoleoverblikket.Api/Skoleoverblikket.Api.csproj -v q >/dev/null
