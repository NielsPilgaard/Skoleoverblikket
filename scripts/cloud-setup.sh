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
has_sdk10() { "$1" --list-sdks 2>/dev/null | grep -q '^10\.'; }
if ! { command -v dotnet >/dev/null 2>&1 && has_sdk10 dotnet; } &&
	! { [ -x "$dotnet_dir/dotnet" ] && has_sdk10 "$dotnet_dir/dotnet"; }; then
	# Pinned to a dotnet/install-scripts commit and checked before running. To update: pick a
	# newer commit and replace both values with its sha and the script's sha256sum.
	installer_commit=e5cf1dd2d1540ed05ac84f8eb8c5cdec2807621e
	installer_sha256=082f7685e156738a1b2e2ed8381a621870d4ce8e8c59278034556f05c186eb2e
	installer=$(mktemp)
	curl -fsSL "https://raw.githubusercontent.com/dotnet/install-scripts/$installer_commit/src/dotnet-install.sh" -o "$installer"
	echo "$installer_sha256  $installer" | sha256sum -c --quiet -
	bash "$installer" --channel 10.0 --install-dir "$dotnet_dir" >/dev/null
	rm -f "$installer"
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
