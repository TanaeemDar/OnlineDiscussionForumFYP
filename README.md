# Online Discussion Forum

ASP.NET Core MVC forum with accounts, administrator-managed forums, posts, replies, and profile images. The application now targets .NET 10 and EF Core 10, uses SQLite, and stores uploads and authentication keys locally. SQL Server and Azure Blob Storage are not required.

## Requirements

- .NET 10 SDK for development; ASP.NET Core 10 runtime on the server.
- Python 3 for the SQLite backup/restore helper.
- Node.js 20.11+ and Chromium/Playwright for the optional browser verification.
- One application instance with persistent local storage. The intended Azure deployment is a Linux VM with the application and SQLite on the same server.

The old manual SQL script is archived as `legacy/SqlServerQueries.txt` and is not used for setup. The old SQL Server migration sources are preserved as noncompiled reference files under `OnlineDisscussionForum FYP/legacy/SqlServerMigrations/`. The active migrations are SQLite-only in the web project. This installation starts fresh; existing SQL Server data import is outside the agreed scope.

## Run locally

From the repository root:

```bash
dotnet restore 'OnlineDisscussionForum FYP/OnlineDisscussionForum.sln'
dotnet build 'OnlineDisscussionForum FYP/OnlineDisscussionForum.sln'
dotnet test 'OnlineDisscussionForum FYP/OnlineDisscussionForum.sln'
cd 'OnlineDisscussionForum FYP/OnlineDisscussionForum'
export ASPNETCORE_ENVIRONMENT=Development
export DataDirectory="$(realpath ../../.forum-data)"
export Admin__UserName=ForumAdmin
export Admin__Email=admin@example.com
read -rs -p 'Initial admin password: ' forum_admin_password
export Admin__Password="$forum_admin_password"
dotnet run --no-launch-profile -- --migrate
unset Admin__Password forum_admin_password
dotnet run --no-launch-profile --urls http://127.0.0.1:5080
```

Use a strong unique administrator password satisfying Identity's defaults (at least six characters, with upper/lowercase, digit, and a symbol). No default password is embedded. Administrator initialization is idempotent; supplying credentials again does not reset an existing password. Without `Admin__UserName`, migration creates the admin role but does not create an administrator.

Run `--migrate` before starting a fresh installation and before starting an updated release with pending schema changes. Normal startup refuses to run with pending migrations. `DataDirectory` contains `forum.db`, `uploads/`, `keys/`, and development-only `mail/`. Set it to an absolute path in production; it must be outside `wwwroot` and release directories. An optional `ConnectionStrings__DefaultConnection` overrides the database filename; its directory must also be outside `wwwroot`. Foreign keys and a 30-second busy timeout are enabled. Migration configures WAL mode.

Development mail is written as private JSON pickup files under `mail/` for testing confirmation/reset links. Production email is disabled by design, as requested: there is no SMTP dependency, sign-up/login do not require confirmed email, and email-based recovery/confirmation submission is unavailable. Keep administrator credentials in your own secure password manager.

## CRUD and data policies

- Admins create, edit, and delete forums. A forum containing posts cannot be deleted.
- Members create posts/replies. Authors and admins can edit/delete them. Ownership is enforced on the server.
- Deleting a post deletes its replies atomically. Editing never awards points. Deletion retains historical contribution points; creating a post earns 1 and a reply earns 3, in the same transaction as content creation.
- Edits/deletes use version tokens and return `409` if a record changed. Reload the page before retrying. Missing targets return `404`.
- Members can update username, email, phone, password, authentication settings, and profile image.
- Account deletion requires password reauthentication and explicit confirmation. Personal account data and Identity claims/logins/tokens/roles are removed; an inactive anonymized author record remains so discussions keep valid authors. The last active admin cannot delete their account. Separate admin user-deactivation controls are intentionally excluded.
- Posts/replies are plain text, including historical HTML, and are encoded when displayed.
- Image uploads accept decoded PNG/JPEG/WebP, at most 5 MB, 4096 pixels per dimension, and 16 million pixels. Images are reencoded as single-frame PNG with metadata removed and unique server-generated names. Replaced/deleted owned images are cleaned up; defaults are retained. Cleanup failures are logged for later retry.
- Forum, search, and admin-user listings use 20-item database pages. Home shows the latest 10 posts. SQLite text search uses its provider's `Contains` semantics (case-sensitive); username uniqueness uses Identity normalization.

## Azure deployment (one Linux VM)

Live Azure provisioning/deployment remains a future step: no subscription, VM, domain, or access details have been supplied. The checked-in deployment files prepare the application for that step. Do not place this SQLite deployment on an ephemeral disk or scale it to multiple application instances.

1. Provision an Azure Linux VM, install the ASP.NET Core 10 runtime, Python 3, and Nginx, and allow public HTTP/HTTPS through its network security group. Keep the application port bound to loopback.
2. Use a persistent OS/managed disk for `/var/lib/forum`. If attaching a disk, mount it before starting the service and configure the mount in `/etc/fstab`.
3. Create the dedicated `forum` service account. Create `/var/lib/forum` owned by that account with mode `0700`. Place releases under `/opt/forum/releases/` and use `/opt/forum/current` as a symlink to the selected release. Release files should be readable but not writable by the service account.
4. Publish with `dotnet publish 'OnlineDisscussionForum FYP/OnlineDisscussionForum/OnlineDisscussionForum.csproj' -c Release -o /tmp/forum-release`, then transfer the output into a new release directory on the VM.
5. Copy `deployment/forum.env.example` to `/etc/forum/forum.env`, replace the domain and initial admin values, and restrict its permissions to `0600`. Do not commit real credentials. `Email__Mode=Disabled` is the production setting.
6. Stop the existing service, take a backup, and run the new release with `--migrate` as the `forum` user using the production environment file. Remove initial admin credentials from the file once initialization succeeds. Switch the release symlink and start the service only after migration succeeds.
7. Install `deployment/forum.service` in `/etc/systemd/system/`. Run `systemctl daemon-reload` and `systemctl enable --now forum`. The unit sets restart behavior, a restrictive umask, and write access limited to the persistent directory.
8. Configure a domain and TLS certificate, adapt `deployment/nginx.conf`, run `nginx -t`, and enable the site. Forwarded scheme/address headers are trusted only from the loopback proxy. If the proxy is moved off this VM, configure its exact trusted address rather than trusting arbitrary forwarded headers.
9. Check HTTPS sign-in, CRUD, uploads, and `/health`. `/health` verifies the database schema can be queried and returns no database details. Inspect `journalctl -u forum`. Restart the service and verify users, content, images, and cookies survive.

Back up `/var/lib/forum/keys` with restricted access: those keys protect authentication cookies and reset tokens. Uploads, keys, and the database must remain outside release directories so upgrades preserve them. Never publish the database or keys through Nginx or static-file configuration. The application only serves its validated `uploads/` subtree.

## Backup and restore

The helper uses SQLite's online backup API, so committed WAL content is included. It verifies database integrity and foreign keys and creates a mode-`0600` backup. Use unique destination filenames; it refuses to overwrite an existing destination.

```bash
python3 scripts/sqlite-backup.py backup /var/lib/forum/forum.db /secure/backups/forum-2026-10-05.db
```

Schedule this command with a systemd timer/cron under an account that can read the database. Also back up uploads and keys. For a coordinated snapshot of all application assets, briefly stop the service while backing up the complete data set. Apply retention and store a protected copy outside the VM.

Restore into a new path with the service stopped:

```bash
sudo systemctl stop forum
python3 scripts/sqlite-backup.py restore /secure/backups/forum-2026-10-05.db /var/lib/forum/restored.db --service-stopped
```

Check ownership, point the connection string at the restored file, restore matching uploads/keys if needed, and restart. Never combine a restored main file with old WAL/SHM sidecars. Test restore in a separate environment before relying on it for production recovery. For release rollback after a schema change, restore a compatible backup; preserve/reconcile any post-cutover writes before rolling back.

## Tests and implementation status

`OnlineDisscussionForum.Tests` uses isolated real SQLite files, real authentication cookies and antiforgery tokens, and a development mailbox. It exercises administrator initialization, CRUD permissions, stale edits, cascades, validation, uploads, account flows, and concurrent writes. Run the complete solution tests before publishing.

For repeatable browser verification of a published production build:

```bash
npm ci
npx playwright install chromium
npm run test:browser
```

The browser check publishes into a temporary release, migrates a fresh private database, tests desktop/mobile forms and navigation, checks disabled production email, restarts from a different release directory, verifies persisted cookies/data/images/keys, and backs up/restores SQLite. It removes temporary app data afterward. Reports and screenshots are written to the ignored `artifacts/` directory. Set `DOTNET_COMMAND` or `CHROME_PATH` if using a nonstandard SDK/browser installation. Browser verification sends no email and provisions no Azure resources.

For future schema edits, use `dotnet tool restore` and `dotnet ef migrations add NAME --project 'OnlineDisscussionForum FYP/OnlineDisscussionForum/OnlineDisscussionForum.csproj'`. Inspect the generated migration, back up the database, and apply it through the application's `--migrate` command before serving requests.

See [plan.md](plan.md) for the requirement-by-requirement completion status and remaining Azure operational checks.
