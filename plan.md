# Project fix and CRUD completion plan

Reviewed and implementation updated: 2026-10-05

This document tracks implementation of the review fixes and the missing CRUD operations. Checked items have implementation and local verification evidence; the Azure rollout section remains unchecked until it is performed on the target server.

Agreed decisions: use a fresh SQLite database; host the application and database together on Azure; skip SMTP. The prepared deployment uses one Azure Linux VM, a persistent local SQLite file, local uploads, and persistent authentication keys. Production email confirmation/recovery is disabled; development pickup files support local account-flow tests.

The original review was static. A .NET 10 SDK is now installed, the solution has an integration test project using real SQLite databases, and the published application has been exercised in Chromium. See `README.md`, `OnlineDisscussionForum.Tests/CrudTests.cs`, and `scripts/browser-check.mjs` for repeatable verification. Live Azure infrastructure has not been supplied or deployed.

## Source map

Paths below are relative to `OnlineDisscussionForum FYP/`:

- Web application: `OnlineDisscussionForum/`
- Service implementations: `OnlineDisscussionForum.Service/`
- Entities, service interfaces, database context, and seeder: `OnlineDisscussionForum.Data/`

## CRUD completion audit

| Entity | Create | Read | Update | Delete |
| --- | --- | --- | --- | --- |
| Forum | Admin form/action/service, with validation and local image upload | Database-paged forum listing and topic view | Admin edit form/action/service for title, description, optional image; version check | Admin confirmation/POST/service; only unchanged empty forums may be deleted |
| Post | Member form/action/service; content and +1 rating committed atomically | Detail, forum/search pages, latest 10 on home | Author/admin edit form/action/service for title and content; version check | Author/admin confirmation/POST/service; replies cascade with parent deletion |
| Reply | Member form/action/service; content and +3 rating committed atomically | Replies displayed in the parent post; direct lookup for management | Author/admin edit form/action/service; version check | Author/admin confirmation/POST/service with return to parent post |
| User/account | Identity registration and idempotent configured admin initialization | Own settings, profile detail, admin-paged user listing | Username/email/phone/password/authentication settings/image; identity updates and refreshed sign-in | Own-account reauthentication and confirmation; personal data removed and discussions retained with an inactive anonymized author |
| Images | Validated local image upload with unique server-generated names | Only validated uploads subtree publicly served | Safe replacement saves the new reference before deleting the old image | Owned-file cleanup on replacement/forum/account deletion; defaults retained and failures logged |

The original `ForumService.UpdateForumTitle`, `UpdateForumDescription`, `PostService.EditPostContent`, and `PostService.Delete` stubs are implemented. `IForum`, `IPost`, and their services include the missing operations; matching controller actions and views now exist. Reply lookup is in `IPost`; a separate reply index/service is unnecessary. Separate admin user-deactivation controls are intentionally excluded; own-account deletion and inactive-account login/session enforcement are implemented.

## Phase 0 — Migrate SQL Server to SQLite and prepare single-server deployment

### Provider, configuration, and schema

- [x] Establish a runnable baseline and coordinate the .NET/EF Core upgrade with the database migration. Choose mutually compatible framework, Identity, and SQLite-provider versions before generating the final migration set.
- [x] Add the compatible `Microsoft.EntityFrameworkCore.Sqlite` package to the project that configures the database. Replace `UseSqlServer` in `OnlineDisscussionForum/Startup.cs:29` with `UseSqlite`.
- [x] Replace the SQL Server/LocalDB connection configuration in `appsettings.Development.json` with a SQLite connection string. Configure the production database through an environment-specific absolute file path; document `ConnectionStrings__DefaultConnection` for deployment.
- [x] Establish one migration assembly. Preserve the existing SQL Server migrations as migration history/reference and generate a separate SQLite migration set from the intended model; do not apply SQL Server migrations directly to SQLite.
- [x] Check provider-specific column types, generated IDs, indexes, defaults, foreign keys, cascade behavior, dates, and query translation. Check Identity username/email normalization and uniqueness as well as search case sensitivity. Rewrite any SQL Server-specific SQL or assumptions, including `SQLQuery1.sql` if it is needed for setup.
- [x] Apply the SQLite migrations to an empty database and verify the full Identity, forum, post, and reply schema. Ensure foreign-key enforcement and required relationships behave as intended.
- [x] Run administrator initialization only after database migrations complete. Run migrations as a controlled deployment step before the application starts accepting requests.

### Existing data transfer — not applicable

The user explicitly selected a fresh SQLite database. No SQL Server data import, legacy-account migration, or production SQL Server cutover is required. Original migrations and the old manual SQL script are archived as noncompiled reference files under `legacy/`. Fresh installation, role/admin setup, database integrity, and new-record generation are covered by local verification.

### Persistent storage and deployment

- [x] Store the SQLite file in a persistent data directory outside `wwwroot` and outside replaceable build/release directories. Exclude database files and their journal/WAL sidecars from Git and published application artifacts.
- [x] Restrict the persistent directory to mode `0700` on Unix and provide a dedicated service account/`UMask=0077` in the systemd template. Verify the database is inaccessible through HTTP. VM deployment uses persistent disk storage; the target-server ownership check is tracked below.
- [x] Prepare single-instance deployment, short atomic write transactions, a 30-second SQLite busy timeout, and WAL mode. Separate-scoped concurrent write tests preserve every record and rating point. Actual Azure deployment is tracked below.
- [x] Add consistent SQLite backups and a tested restore procedure. Use a SQLite-aware backup method or stop the application for a coordinated backup; do not assume copying only the main file while writes are active is sufficient.
- [x] Prepare systemd, Nginx HTTPS, production environment, logging, and database-health-check configuration in `deployment/`. Persist Data Protection keys outside release storage and verify authentication survives a process/release-directory change. Enabling the service/domain/certificate on Azure remains a rollout task.
- [x] Verify deployments and service restarts retain the database, uploaded assets, and encryption keys. Document data paths, migration commands, backup/restore commands, permissions, and recovery steps in `README.md`.
- [x] Replace Azure Blob Storage with `IUpload`/`LocalUploadService`, persistent local PNG files, restricted URL serving, safe replacement, and cleanup. SQL Server and Azure Blob SDKs are no longer runtime dependencies.

Local acceptance verified: the published application uses SQLite, Identity and CRUD work against a fresh schema, overlapping writes are handled, and release-directory changes/restarts/backup/restore preserve application data. Azure acceptance must be verified during rollout.

## Phase 1 — Fix security and request handling

- [x] **Prevent stored XSS.** Replace `Html.Raw(Model.PostContent)` and `Html.Raw(reply.ReplyContent)` in `OnlineDisscussionForum/Views/Post/Index.cshtml:31,73` with encoded output. Preserve line breaks with CSS. If HTML formatting is required, define and enforce an allowlist sanitizer on create and update, and sanitize existing stored content before rendering it.
- [x] **Validate antiforgery tokens.** Protect `AddPost`, `AddReply`, `AddForum`, and `UploadProfileImage`, preferably with a global MVC antiforgery filter in `Startup.cs`. Apply the same protection to all new update/delete actions. Keep destructive operations on POST; GET may display a confirmation only.
- [x] **Repair administrator seeding.** In `OnlineDisscussionForum.Data/DataSeeder.cs`, use `UserManager` and `RoleManager` for normalized names, password validation, role creation, and membership. Remove the hardcoded password, load initial credentials from secure configuration, await every operation, check results, and make repeated execution idempotent. Complete initialization before accepting requests; also fix the unawaited startup call in `Startup.cs:61`.
- [x] **Fix dependency lifetime and asynchronous calls.** Make `PostController._userManager` an instance `readonly` field instead of `static` (`PostController.cs:22`). Replace `.Result` in post/profile actions and author-role checks with awaited calls. Avoid repeated role lookups per reply where practical.
- [x] **Validate submissions server-side.** Add required fields and sensible length limits to forum/post/reply input models. Reject whitespace-only content, check `ModelState`, and redisplay forms with their context and errors. Validate that the selected forum/post exists before saving. Do not accept ownership, rating, role, or creation date from submitted fields.
- [x] **Handle missing records.** Replace throwing lookups such as `PostService.GetById(...).First()` with safe lookups. Return `404` for missing detail/edit/delete targets and validation errors for invalid parent IDs. Handle missing users and empty uploads without null-reference exceptions.
- [x] **Define authorization for new CRUD actions.** Proposed baseline: admins manage forums; post/reply authors and admins may edit/delete their content; users update only their own account. Check ownership on the server for both form-loading and POST actions. Hiding buttons alone is insufficient.

Acceptance: submitted scripts render safely, mutations reject missing/invalid tokens, invalid inputs do not create records or award ratings, concurrent requests do not share request-scoped services, and administrator setup succeeds on first and repeated startup.

## Phase 2 — Complete forum, post, and reply CRUD

### Forums

- [x] Implement forum updates in `IForum` and `ForumService`. Prefer one validated update operation for title, description, and optional image so an edit saves consistently; replace or implement the existing title/description stubs.
- [x] Add admin-only GET/POST edit actions to `ForumController`, an edit input model, and `Views/Forum/Edit.cshtml`. Add an edit control to forum management pages.
- [x] Connect the existing forum delete service to admin-only confirmation and POST delete actions, with clear feedback and a suitable redirect.
- [x] Define nonempty-forum deletion before wiring it to the UI. Recommended initial policy: reject deletion while posts exist and explain why. If deletion of an entire discussion is desired, explicitly remove replies and posts transactionally and show the affected counts.
- [x] Fix the default forum image path in `ForumController.cs:69`: it references `/images/users/default.png`, while the supplied default forum asset is `/images/forum/default.png`.

### Posts

- [x] Implement post update and delete in `IPost` and `PostService`; include title and content in update support.
- [x] Add GET/POST edit actions and edit views to `PostController`, plus an author/admin edit control on post detail.
- [x] Add confirmation and POST delete actions with server-side ownership checks and redirect back to the parent forum after success.
- [x] Delete a post's replies together with the post in a database transaction, or implement an explicit archive policy. Do not leave replies with missing parent posts.

### Replies

- [x] Extend `IPost`/`PostService` with reply lookup, update, and delete operations, or introduce a dedicated `IReply`/`ReplyService` registered in `Startup.cs`.
- [x] Add author/admin edit actions, input model, and edit view to `ReplyController`.
- [x] Add confirmation and POST delete actions; return to the parent post after successful update/delete.
- [x] Add edit/delete controls to each permitted reply in `Views/Post/Index.cshtml`.

### Data integrity shared by CRUD

- [x] Make forum/post/author relationships explicit in `ApplicationDbContext` and migrations. Require author/parent links and version tokens in the active SQLite schema, restrict forum/user deletion, and cascade reply deletion with a post. Fresh installation requires no legacy-row conversion.
- [x] Save content creation and its rating change atomically in one SQLite transaction. Editing should not award new points. Define whether deletion reverses earned points and implement the rule consistently without repeated deductions.
- [x] Detect an already deleted or concurrently modified target and return a useful result rather than an unhandled exception.

Acceptance: forum/post/reply operations work through the UI and direct requests, unauthorized users cannot change another user's content, all four service stubs are resolved, and deletion respects the chosen relationship and rating rules.

## Phase 3 — Repair uploads and account workflows

- [x] **Prevent blob collisions.** In profile and forum uploads, generate server-controlled unique blob names, with a user-specific prefix for profile images. Do not use the submitted filename as the shared storage identifier.
- [x] **Validate images.** Check empty files, file size, permitted image formats, and decoded content. Set the correct content type and dispose streams. Return form errors for invalid uploads.
- [x] **Await forum uploads.** Replace the old unawaited Azure helper with awaited local image saving. Save the URL only after upload completion and clean up a new image if database saving fails.
- [x] **Configure storage explicitly.** Use `DataDirectory` for persistent database/uploads/keys; document absolute production paths and environment configuration. Legacy Azure settings are unused. Storage failures return form feedback while preserving existing image references.
- [x] **Implement safe image replacement and cleanup.** Add upload-service operations to delete application-owned blobs. Persist the replacement URL before scheduling old-blob cleanup; avoid deleting shared/default assets. Handle storage/database failures without leaving a profile pointing at a failed upload.
- [x] **Apply the user's SMTP exclusion.** Remove SMTP packages/configuration. Disable production email confirmation/recovery and their submission paths; registration/login remain available without confirmed email. Development-only private pickup files allow local confirmation/reset tests and are never publicly served.
- [x] **Review login protection.** The original login action used `lockoutOnFailure: false`; it now counts failed attempts. Configure failed-attempt protection and verify it without locking out legitimate recovery flows.
- [x] **Complete account-management scope.** Existing email, phone, password, authentication, and image updates should be preserved and tested. Add username editing only if it is intended to be editable; use Identity APIs for uniqueness, normalization, and refreshing sign-in state.
- [x] **Plan account deletion/deactivation.** Define content retention before implementing an own-account deletion flow or admin deactivation. Recommended approach: retain discussion content with an anonymized author and remove private account data according to the chosen policy. Require reauthentication for own-account deletion. `ApplicationUser.IsActive` now defaults to true and is enforced during login and existing-session validation.

Acceptance verified: identical filenames cannot alter other users' images, failed uploads preserve existing references, production email is explicitly disabled, development confirmation/reset flows work, and account deletion preserves valid discussion authors.

## Phase 4 — Improve reads, setup, and maintainability

- [x] Keep filtering, sorting, pagination, and projections in database queries before materialization. `PostService.GetAll` now returns `IQueryable`, and listing controllers project/filter/page in SQL. Avoid loading every post and reply to display a small result set.
- [x] Normalize empty search input; Null/empty input now returns the paged list. Fresh SQLite titles/content are required.
- [x] Fix `ForumService.GetActiveUsers`'s condition (`posts != null || !posts.Any()`), handle missing forums safely, and calculate counts/recent activity with focused queries instead of repeatedly loading each forum's full discussion graph.
- [x] Fix `GetPostsByForum` to query posts by forum ID directly or explicitly load the collection; it now queries posts by forum ID directly.
- [x] Document SDK/framework prerequisites, SQLite setup and persistent data paths, connection configuration, migration/import commands, single-server deployment, backup/restore, administrator initialization, storage, and email in `README.md`.
- [x] Verify migration ownership: migrations/snapshots exist in both the web and data projects. Establish one intended migration assembly and validate setup against a fresh database before removing or consolidating files.
- [x] Complete the migration from `netcoreapp2.0` to the supported .NET target selected in Phase 0. Verify current support and dependency compatibility at implementation time, align package versions across projects, and retest Identity, EF, SQLite, Razor, and storage integration.
- [x] Add repository ignore rules for generated `bin/` and `obj/` artifacts and local secrets; review tracked generated files before removing them.

## Verification and completion checklist

- [x] Restore and build the solution in a prepared .NET environment; record required configuration and any migration blockers.
- [x] Test meaningful security cases: stored script content, missing antiforgery tokens, anonymous mutations, and another user's record IDs.
- [x] Test forum/post/reply create, read, edit, and delete against a real temporary SQLite database, including missing records, invalid input, nonempty forum deletion, transactions, and post/reply relationship behavior. Use SQL Server only when validating legacy data export/import.
- [x] Test empty-database installation and, where applicable, SQL Server-to-SQLite import with preserved users, roles, IDs, and password hashes.
- [x] Test simultaneous writes, database lock handling, persistent file permissions, and database access being blocked through HTTP.
- [x] Test production service restarts and application updates without data loss, and restore a backup into a separate environment to confirm recovery.
- [x] Test first/repeated administrator initialization and overlapping requests using separate dependency scopes.
- [x] Test same-filename uploads by different users, invalid/empty/oversized uploads, storage failure, and image replacement cleanup.
- [x] Test development account confirmation/password reset using private pickup files; verify production email workflows are disabled as requested.
- [x] Test null/empty search and populated forum/home pages, and inspect query volume to verify pagination and filtering occur in SQL.
- [x] Exercise rendered forms, errors, confirmation pages, permissions, redirects, and images in Chromium, and visually inspect desktop/mobile screenshots.
- [x] Confirm there are no reachable CRUD stubs and document intentionally excluded account-management features.

## Verification evidence

- Target: .NET 10, EF Core/Identity/SQLite 10.0.12; package versions aligned and pinned. `global.json` and `.config/dotnet-tools.json` document the SDK/tool requirements.
- Solution integration suite: 15 passing tests against isolated real SQLite files, actual cookies, and antiforgery tokens. Includes all content-mutation token checks, direct cross-author edit/delete denial, foreign-key/required-author/version enforcement and rollback without rating changes, last-admin protection, login lockout, image validation/replacement/storage failure, account flows, pagination, and concurrent writes, including profile/Identity updates that preserve concurrent ratings and images.
- Published production build: Chromium completed 8 checks for actual forms, encoded content, desktop/mobile navigation/layout, nonempty forum deletion feedback, disabled email, local uploads/private directory permissions, release/process restart persistence, and SQLite backup/restore.
- Repeatable commands: `dotnet test 'OnlineDisscussionForum FYP/OnlineDisscussionForum.sln'` and `npm run test:browser` (after the browser prerequisites in README). Browser reports/screenshots are generated under ignored `artifacts/`.
- Repository hygiene: generated `bin/` and `obj/` artifacts removed from version control, local copies retained, and data/secrets/build/browser-output exclusions added to `.gitignore`.

## Azure rollout — pending target details and execution

Azure hosting is selected; no subscription, VM, domain, TLS certificate, or connection details have been supplied. The implementation and deployment templates are ready, and their application/persistence behavior is verified locally. These operational steps are not claimed as completed:

- [ ] Select/provide the Azure Linux VM, access method, domain, and persistent disk path. If a different Azure hosting product is chosen, validate its persistent local-storage and single-instance support before using SQLite.
- [ ] Install the runtime and proxy; configure the `forum` service account, production permissions/environment, persistent mount, firewall/NSG, domain, and TLS certificate.
- [ ] Publish/copy the release, run controlled migration/admin initialization, enable the systemd/Nginx configuration, and verify public HTTPS `/health`, sign-in, CRUD, and uploads.
- [ ] Schedule backups and retention; test restore and service/release restarts on the Azure target. Verify users, content, images, cookies, and keys survive and database/key paths remain inaccessible through HTTP.

No Azure resources have been created and no external email has been sent. Existing SQL Server import and SMTP are excluded by the user's instructions. Complete the remaining Azure checks when the target is available.
