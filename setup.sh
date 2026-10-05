#!/usr/bin/env bash
# Local development bootstrap. Run from any directory: /path/to/repo/setup.sh
set -Eeuo pipefail
umask 077

fail() { printf 'Setup error: %s\n' "$*" >&2; exit 1; }
trap 'printf "Setup failed at line %s. Fix the error above and rerun ./setup.sh.\n" "$LINENO" >&2' ERR
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
web_project="$repo_root/OnlineDisscussionForum FYP/OnlineDisscussionForum"
port="${FORUM_PORT:-5080}"
start_app=true
for argument in "$@"; do
    case "$argument" in
        --no-start) start_app=false ;;
        --help|-h)
            cat <<'HELP'
Usage: ./setup.sh [--no-start]
Installs .NET 10 if needed, builds the forum, initializes SQLite, and starts it.
Supported: Debian/Ubuntu Linux and macOS; other Linux systems need .NET 10 installed.
First run prompts for an administrator username, email, and password.
Automation: set Admin__UserName, Admin__Email, and Admin__Password.
Options: --no-start prepares the application without starting the web server.
Environment: FORUM_PORT (5080), DataDirectory (repository/.forum-data),
             DOTNET_COMMAND (SDK executable), FORUM_DOTNET_DIR (local SDK location).
Existing discussions and account passwords are preserved on subsequent runs.
HELP
            exit 0 ;;
        *) fail "Unknown argument: $argument. Use --help." ;;
    esac
done
[[ "$port" =~ ^[0-9]+$ && ${#port} -le 5 ]] || fail 'FORUM_PORT must be an integer between 1 and 65535.'
port=$((10#$port))
((port >= 1 && port <= 65535)) || fail 'FORUM_PORT must be between 1 and 65535.'
if "$start_app" && (exec 3<>"/dev/tcp/127.0.0.1/$port") 2>/dev/null; then
    fail "Port $port is already in use. Open http://127.0.0.1:$port or choose FORUM_PORT=5081 ./setup.sh."
fi
cd "$repo_root"

usable_sdk() {
    [[ -n "$1" ]] && "$1" --version 2>/dev/null | { read -r version; [[ "$version" == 10.* ]]; }
}
dotnet_command="${DOTNET_COMMAND:-}"
local_sdk="${FORUM_DOTNET_DIR:-$HOME/.local/share/forum-dotnet}"
if [[ -n "$dotnet_command" ]]; then
    usable_sdk "$dotnet_command" || fail 'DOTNET_COMMAND must point to a working .NET 10 SDK.'
elif usable_sdk "$(command -v dotnet || true)"; then
    dotnet_command="$(command -v dotnet)"
elif usable_sdk "$local_sdk/dotnet"; then
    dotnet_command="$local_sdk/dotnet"
else
    printf 'Installing .NET 10 SDK into %s ...\n' "$local_sdk"
    case "$(uname -s)" in
        Linux)
            if command -v apt-get >/dev/null 2>&1; then
                elevate=()
                if ((EUID != 0)); then
                    command -v sudo >/dev/null 2>&1 || fail 'sudo is needed to install Linux prerequisites. Ask an administrator to install them or install .NET 10 first.'
                    elevate=(sudo)
                fi
                "${elevate[@]}" apt-get update
                "${elevate[@]}" apt-get install -y ca-certificates curl libicu-dev libssl-dev zlib1g libkrb5-3 libgcc-s1 libstdc++6
            else
                fail 'Install the .NET 10 SDK and runtime dependencies for your Linux distribution, then rerun this script. Automatic prerequisites support Debian/Ubuntu.'
            fi ;;
        Darwin) command -v curl >/dev/null 2>&1 || fail 'curl is required to download the SDK.' ;;
        *) fail 'Run this script on Linux, macOS, or an Ubuntu WSL environment.' ;;
    esac
    installer="$(mktemp "${TMPDIR:-/tmp}/forum-dotnet-install.XXXXXX")"
    trap 'rm -f -- "${installer:-}"' EXIT
    curl --fail --silent --show-error --location --retry 3 https://dot.net/v1/dotnet-install.sh -o "$installer"
    bash "$installer" --channel 10.0 --install-dir "$local_sdk" --no-path
    rm -f -- "$installer"
    trap - EXIT
    dotnet_command="$local_sdk/dotnet"
    usable_sdk "$dotnet_command" || fail 'The SDK could not start. Check the installer output and Linux runtime dependencies.'
fi
if [[ "$dotnet_command" == "$local_sdk/dotnet" ]]; then
    export DOTNET_ROOT="$local_sdk"
fi
export PATH="$(dirname -- "$dotnet_command"):$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export ASPNETCORE_ENVIRONMENT=Development
export Email__Mode=Disabled
[[ -z "${ConnectionStrings__DefaultConnection:-}" ]] || fail 'Unset ConnectionStrings__DefaultConnection so setup manages the database inside DataDirectory.'
mkdir -p -- "${DataDirectory:-$repo_root/.forum-data}"
export DataDirectory="$(cd -- "${DataDirectory:-$repo_root/.forum-data}" && pwd -P)"
case "$DataDirectory/" in
    "$web_project/wwwroot/"*) fail 'DataDirectory must be outside wwwroot.' ;;
esac
chmod 700 "$DataDirectory"

initialize_admin=false
if [[ ! -f "$DataDirectory/.setup-complete" || ! -f "$DataDirectory/forum.db" || -n "${Admin__UserName:-}" ]]; then
    initialize_admin=true
    if [[ -z "${Admin__UserName:-}" ]]; then
        [[ -t 0 ]] || fail 'First setup needs a terminal, or Admin__UserName, Admin__Email and Admin__Password environment variables.'
        read -r -p 'Administrator username [ForumAdmin]: ' admin_name
        export Admin__UserName="${admin_name:-ForumAdmin}"
    fi
    if [[ -z "${Admin__Email:-}" ]]; then
        [[ -t 0 ]] || fail 'Set Admin__Email for unattended first setup.'
        read -r -p 'Administrator email: ' admin_email
        export Admin__Email="$admin_email"
    fi
    if [[ -z "${Admin__Password:-}" ]]; then
        [[ -t 0 ]] || fail 'Set Admin__Password for unattended first setup.'
        printf 'Choose a password with uppercase, lowercase, a number, and a symbol (at least 6 characters).\n'
        read -r -s -p 'Administrator password: ' admin_password
        printf '\n'
        read -r -s -p 'Confirm password: ' admin_confirmation
        printf '\n'
        [[ "$admin_password" == "$admin_confirmation" ]] || fail 'Passwords do not match.'
        export Admin__Password="$admin_password"
        unset admin_password admin_confirmation
    fi
    [[ -n "$Admin__Email" && -n "$Admin__Password" ]] || fail 'Administrator email and password cannot be empty.'
fi
printf 'Using .NET %s\nData directory: %s\n' "$("$dotnet_command" --version)" "$DataDirectory"
"$dotnet_command" restore "$web_project/OnlineDisscussionForum.csproj"
"$dotnet_command" build "$web_project/OnlineDisscussionForum.csproj" --no-restore
cd "$web_project"
"$dotnet_command" run --no-build --no-launch-profile -- --migrate
if "$initialize_admin"; then
    printf 'Administrator ready: %s (%s). Sign in using the username.\n' "$Admin__UserName" "$Admin__Email"
    printf 'An existing account keeps its existing password.\n'
fi
touch "$DataDirectory/.setup-complete"
unset Admin__Password Admin__UserName Admin__Email admin_name admin_email
printf '\nSetup complete. Database: %s/forum.db\n' "$DataDirectory"
if "$start_app"; then
    printf 'Open http://127.0.0.1:%s/Account/Login\nPress Ctrl+C to stop. Rerun ./setup.sh to start again.\n' "$port"
    exec "$dotnet_command" run --no-build --no-launch-profile --urls "http://127.0.0.1:$port"
fi
