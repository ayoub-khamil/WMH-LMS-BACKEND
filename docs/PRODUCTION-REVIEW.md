# WMH LMS: Production Review and Target Architecture

Review date: 2026-09-07. Target launch: Monday 2026-09-14.
Scope: the two repositories in this folder, `antigravity backend demo` (GitHub `ayoub-khamil/WMH-LMS-BACKEND`) and `antigravity frontend demo` (GitHub `ayoub-khamil/WMH-LMS-FRONTEND`). Every file in both was read. Every claim below about the current code points at a file.

---

## Ground truth: read this first

These are the findings that change the plan. Details are in section 2.

1. **The backend is .NET 8, not .NET Framework.** All four project files declare `<TargetFramework>net8.0</TargetFramework>`: `WmhLms.Api/WmhLms.Api.csproj`, `WmhLms.Core/WmhLms.Core.csproj`, `WmhLms.Data/WmhLms.Data.csproj`, `WmhLms.Tests/WmhLms.Tests.csproj`. Linux container hosting on Railway is valid. A working multi-stage `Dockerfile` on `mcr.microsoft.com/dotnet/sdk:8.0` / `aspnet:8.0` already exists. No decision is needed here.

2. **The database is SQLite, not PostgreSQL. The brief is wrong on this point and the code wins.** `WmhLms.Data.csproj` references only `Microsoft.EntityFrameworkCore.Sqlite`. `Program.cs:49-50` calls `UseSqlite`. Both migrations under `WmhLms.Data/Migrations/` are SQLite-typed (`INTEGER`, `TEXT`, `Sqlite:Autoincrement`). `AppDbContext.cs:32` uses the SQLite-only `NOCASE` collation. The `Dockerfile` mounts a `/data` volume for the SQLite file. `docker-compose.yml` has no database service. Nothing references Npgsql. Moving to Postgres is real work, not a config change, and it is the first BLOCKING item.

3. **Production would start with zero users.** The root manager is only created by `DemoSeed`, and seeding is off outside Development (`appsettings.json` sets `Seed:DemoContent=false`; `Program.cs:256-266` skips the seed when false). Deploying as-is produces an empty `Users` table and nobody can log in. A separate, always-on, idempotent root-manager bootstrap is BLOCKING.

4. **The login rate limiter will lock out a training cohort.** `Program.cs:137-149` allows `RateLimit:LoginAttempts` (10) logins per minute **per client IP**. A BPO office sits behind one NAT address. Thirty agents signing in at 9:00 means twenty of them get HTTP 429. One environment variable fixes it; it must be set before launch.

5. **The quiz answer key is sent to agents.** `OptionDto` (`WmhLms.Core/Dtos/Dtos.cs:27`) carries `IsCorrect`, and `LearnService.GetCourseTreeAsync` (`LearnService.cs:111-121`) maps options through `Mapping.ToDto`, so `GET /api/learn/courses/{id}` returns `is_correct` for every option. The frontend's `?autofill` test helper (`QuizPlayer.jsx:26-38`) depends on it. `to-be-implemented.md` already records this as open. It is a one-hour fix and it is the integrity of the one control this app exists to enforce, so it is BLOCKING.

6. **The frontend lockfile is git-ignored.** `antigravity frontend demo/.gitignore` lists `package-lock.json`, and `git ls-files` confirms it is untracked. The frontend `ci.yml` runs `npm ci`, which fails without a lockfile, and Cloudflare Pages builds would resolve dependencies fresh on every deploy. BLOCKING, ten-minute fix.

7. **Features the recommended stack assumes do not exist.** There is no email sending anywhere in either repo (no password reset, no invites, no notifications), so no email provider is recommended. There are no file uploads (media is YouTube embeds via `YouTubePlayer.jsx`; text items are plain text), so no object storage is recommended. Both are omitted from this document except in the open-questions section.

8. **What does work.** The backend test suite passes: 32 tests, 0 failures (`dotnet test`, run during this review). The frontend production build succeeds (`npm run build`, run during this review; the largest chunk is jsPDF's html2canvas dependency, pulled in by the certificate feature). Authorization is enforced server-side on every endpoint with a fallback policy. Migrations, not `EnsureCreated`, are used at runtime. CORS is origin-locked and refuses to start in production without an origin list. Error responses hide internals outside Development. The hardening described in `to-be-implemented.md` section 8 is genuinely present in the code.

---

## Table of contents

1. [Purpose and how to use this document](#1-purpose-and-how-to-use-this-document)
2. [Current state](#2-current-state)
3. [Gap list](#3-gap-list)
4. [Target architecture](#4-target-architecture)
5. [Target directory structure](#5-target-directory-structure)
6. [Backend conventions](#6-backend-conventions)
7. [Frontend conventions](#7-frontend-conventions)
8. [Data layer](#8-data-layer)
9. [Security checklist](#9-security-checklist)
10. [Configuration and environments](#10-configuration-and-environments)
11. [Deployment](#11-deployment)
12. [Operations runbook](#12-operations-runbook)
13. [Testing](#13-testing)
14. [Ordered execution plan](#14-ordered-execution-plan)
15. [Post-launch backlog](#15-post-launch-backlog)
16. [Assumptions and open questions](#16-assumptions-and-open-questions)

---

## 1. Purpose and how to use this document

This is the single reference for taking the WMH LMS from a working local demo to a production deployment that one person can run alone, and for the structural clean-up that follows launch. It replaces the two deleted README files and `to-be-implemented.md`.

How to use it:

- **If an AI coding agent is doing the work:** the session-by-session prompts are in the `prompts/` folder next to this file, one file per phase (`A-local-build.md`, `B-hardening.md`, `C-deploy.md`, `D-structure.md`) plus a `README.md` with prerequisites and the routine. Every prompt is one self-contained block to copy and paste, followed by a check to run by hand. They follow section 14 and refer back here for every detail.
- **This week (before Monday):** work through section 14 in order up to the line marked **LAUNCH-SAFE**. Everything above that line is tagged `BLOCKING` in section 3. Do not start anything tagged `WEEK-2` or `LATER` before that line is reached.
- **When something breaks in production:** go straight to section 12, the operations runbook. It assumes no prior operations experience.
- **When adding a feature later:** sections 6 and 7 are the conventions. Section 5 shows where files go. Follow the vertical slice examples rather than the existing code where the two disagree; the existing code is being migrated toward the examples.
- **When you need to remember why something was decided:** section 16 lists every assumption with the condition that would reverse it, and records the owner's answers to the fifteen decision questions (given on 2026-09-07). Only the domain and its DNS host remain open.

The document is prescriptive. Where it says "do X", that is the recommendation. Where an alternative exists, it is stated second, with the one condition that would make it the better choice.

---

## 2. Current state

### 2.1 Solution inventory (backend)

Solution file: `WmhLms.sln`. Four projects, all `net8.0`, `Nullable` and `ImplicitUsings` enabled.

| Project | SDK | References | Packages |
|---|---|---|---|
| `WmhLms.Api` | `Microsoft.NET.Sdk.Web` | `WmhLms.Core`, `WmhLms.Data` | `Microsoft.AspNetCore.Authentication.JwtBearer 8.0.*`, `Microsoft.EntityFrameworkCore.Design 8.0.*`, `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore 8.0.*` |
| `WmhLms.Core` | `Microsoft.NET.Sdk` | `WmhLms.Data` | `Microsoft.EntityFrameworkCore 8.0.*`, `Microsoft.Extensions.Identity.Core 8.0.*` |
| `WmhLms.Data` | `Microsoft.NET.Sdk` | none | `Microsoft.EntityFrameworkCore.Sqlite 8.0.*` |
| `WmhLms.Tests` | `Microsoft.NET.Sdk` | all three | xunit 2.9.2, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.Extensions.TimeProvider.Testing`, coverlet |

Dependency direction today: `Api -> Core -> Data` and `Api -> Data`. This is the reverse of the required direction for the Core/Data pair (Application should not depend on the data access implementation). Section 4 fixes it.

**Layering violations found (each is a concrete refactor target):**

- `WmhLms.Api/Program.cs:102` resolves `AppDbContext` inside `OnTokenValidated` and queries `Users` directly.
- `WmhLms.Api/Controllers/DevController.cs:17` injects `AppDbContext` and manipulates `DbSet`s in a controller.
- `WmhLms.Api/Controllers/AuditController.cs:20-30` builds the response shape (a `Dictionary<string, object?>`) in the controller.
- `WmhLms.Api/Controllers/LearnController.cs:50` declares a request type (`RecordViewRequest`) in the API project instead of with the other DTOs.
- Every service in `WmhLms.Core/Services/` takes `AppDbContext` directly. There is no repository abstraction.
- `WmhLms.Core/Services/LearnService.cs:27` takes raw `IConfiguration` instead of a typed options object.
- Several service methods return `object` or `Dictionary<string, object?>` instead of a DTO: `LearnService.ResumeAsync`, `LearnService.CompleteItemAsync`, `LearnService.GetLockStatusAsync`, `UserService.GetAssignmentsAsync`, `AssignmentService.GetCourseAssignmentsAsync`, `AuthController.Login`.

**Single Responsibility violations (multi-class files):**

| File | Contains |
|---|---|
| `WmhLms.Core/Services/UserServices.cs` | `Mapping`, `AuthService`, `UserService` |
| `WmhLms.Core/Services/Support.cs` | `JsonIds`, `Progress`, `Guard`, `ApiException` |
| `WmhLms.Core/Dtos/Dtos.cs` | all 30 DTO records |
| `WmhLms.Data/Entities/Course.cs` | `Course`, `Section`, `Item`, `Question`, `Option` |
| `WmhLms.Data/Entities/Assignment.cs` | `Assignment`, `QuizLock` |
| `WmhLms.Api/Controllers/CatalogControllers.cs` | `CoursesController`, `SectionsController`, `ItemsController`, `QuestionsController`, `AssignmentsController` |
| `WmhLms.Api/Controllers/AuthController.cs` | `AuthController`, `MeController` |
| `WmhLms.Api/Middleware/SecurityHeadersMiddleware.cs` | `SecurityHeadersMiddleware`, `CorrelationIdMiddleware` |
| `WmhLms.Api/Program.cs` | host setup, JWT setup, rate limiting, CORS, health checks, DB migration, seeding (275 lines) |

### 2.2 Data model (backend)

Entities in `WmhLms.Data/Entities/`, configured in `WmhLms.Data/AppDbContext.cs`:

- `User` (`Id`, `FirstName`, `LastName`, `Email`, `PasswordHash`, `Role` = `manager|agent`, `Status` = `active|disabled`, `IsRoot`, `CreatedAt`). Unique index on `Email` with SQLite `NOCASE` collation.
- `Course` > `Section` > `Item` > `Question` > `Option`. Cascade deletes down the tree. `Item.Type` = `video|text|quiz|audio`; `Item.ContentUrl` holds a YouTube URL for video and audio; `Item.TextContent` holds plain text. `Course.Status` = `draft|published`.
- `Assignment` (`CourseId`, `AgentId`, `CompletedItemIdsJson` as a JSON array in a text column, `Status` = `not_started|in_progress|completed`, `AssignedAt`, `CompletedAt`). Unique on (`CourseId`, `AgentId`). FKs cascade from `Course` and `User`.
- `QuizLock` (`AgentId`, `QuizItemId`, `CourseId`, `LockedUntilUtc`, `ReviewedItemIdsJson`, `LastResultJson`). Unique on (`AgentId`, `QuizItemId`).
- `AuditEntry` (`ActorId?`, `ActorEmail`, `Action`, `TargetType`, `TargetId?`, `TargetLabel`, `Detail`, `CreatedAt`). Deliberately no FK to `User`.

Migrations: `20260904162202_InitialSchema` and `20260904225515_AuditTrail`, applied at boot by `Program.cs:246` (`MigrateAsync`). `EnsureCreated` is used only in `WmhLms.Tests/TestDb.cs:22` for the in-memory SQLite test database.

Indexes that exist (from `20260904162202_InitialSchema.cs:203-254`): `IX_Assignments_AgentId`, `IX_Assignments_CourseId_AgentId` (unique), `IX_Items_SectionId`, `IX_Options_QuestionId`, `IX_Questions_ItemId`, `IX_QuizLocks_AgentId_QuizItemId` (unique), `IX_QuizLocks_CourseId`, `IX_QuizLocks_QuizItemId`, `IX_Sections_CourseId`, `IX_Users_Email` (unique), plus `IX_AuditEntries_CreatedAt` and `IX_AuditEntries_TargetType_TargetId` from the second migration.

### 2.3 Feature map (what exists in code)

Every feature below traces to files that were read. Nothing else exists.

| Feature | Backend | Frontend | State |
|---|---|---|---|
| Login with email + password, JWT bearer (HS256, 24h) | `AuthController.cs`, `AuthService` in `UserServices.cs:24-46`, `Api/Services/TokenService.cs`, `PasswordService.cs` (ASP.NET Identity `PasswordHasher`) | `LoginScreen.jsx`, `AuthContext.jsx`, `auth.api.js`, `httpClient.js` (token in `localStorage` key `wmh_token`) | Complete |
| Session restore via `GET /api/me`; auto-logout on 401 | `MeController` in `AuthController.cs:24-33` | `AuthContext.jsx:31-58, 70-77` | Complete |
| Per-request account revalidation (disabled/deleted/role-changed accounts rejected even with a valid token) | `Program.cs:98-123` | n/a | Complete |
| Login rate limiting per IP | `Program.cs:137-158`, `AuthController.cs:13` | n/a | Complete, misconfigured for NAT (see gap list) |
| User management: paged list with role/status/search filters, create, edit, change password, toggle status, delete | `UsersController.cs`, `UserService` in `UserServices.cs:48-222` | `UserManagement.jsx` | Complete |
| Root manager rules: root cannot be edited/disabled/deleted; only root creates or administers managers or changes roles; nobody disables or deletes themselves | `UserServices.cs:87-91, 100, 128, 134, 164-166, 178-180` | `UserManagement.jsx:186-195` (mirrors rules to disable buttons) | Complete, enforced in service layer only (section 8 adds DB-level enforcement) |
| Audit trail of user-admin actions | `AuditService.cs`, `AuditEntry.cs`, `AuditController.cs` (`GET /api/audit`, root only) | **none** (`grep -ri audit src` finds nothing) | Half-built: backend writes and exposes it; no screen reads it. Only user actions are audited, not course edits or assignments |
| Course catalogue: paged list with status/search, create, edit title/description, publish/unpublish, delete | `CoursesController` in `CatalogControllers.cs:8-41`, `CourseService.cs:19-73` | `CoursesList.jsx`, `CourseEditor.jsx` | Complete |
| Sections: add, rename, delete, reorder (move up/down) | `CatalogControllers.cs:34-58`, `CourseService.cs:77-121` | `CourseEditor.jsx` | Complete |
| Items: add (video/text/quiz/audio), edit, delete, reorder | `CatalogControllers.cs:60-84`, `CourseService.cs:125-177` | `CourseEditor.jsx`, `ItemEditor.jsx` | Complete |
| Quiz questions: add/edit/delete; types `multiple_choice`, `true_false`, `multiple_answer`; server validates option shape | `CatalogControllers.cs:86-104`, `CourseService.cs:181-245` | `QuizQuestionEditor.jsx` | Complete |
| Bulk assign/unassign agents to a course; cohort progress table | `AssignmentsController` in `CatalogControllers.cs:106-126`, `AssignmentService.cs` | `AssignmentsManager.jsx` | Complete |
| Per-user assignments view | `UsersController.cs:35-37`, `UserServices.cs:191-222` | `UserManagement.jsx` (modal) | Complete |
| Agent dashboard: in-progress / not-started / completed / all tabs; only published courses shown | `LearnController.cs:16-18`, `LearnService.cs:79-105` | `AgentDashboard.jsx`, `Sidebar.jsx` | Complete |
| Course player: YouTube embed (`youtube-nocookie.com`), plain-text reader, quiz; resume at first incomplete item; complete-and-continue | `LearnController.cs:20-30`, `LearnService.cs:111-152` | `CourseViewer.jsx`, `YouTubePlayer.jsx`, `TextItemViewer.jsx` | Complete |
| Sequential gating (item N locked until items 1..N-1 complete) | **none**: `LearnService.CompleteItemAsync` accepts any item in the course | `Sidebar.jsx:134-140`, `CourseViewer.jsx:98-106` | Half-built: UI only. A direct API call completes items in any order |
| Quiz: 100% required to pass; failed attempt starts a server-side cooldown (`Quiz:CooldownMinutes`, default 5) plus a review gate (must view another item before retry) | `LearnService.cs:154-274`, `QuizLock` entity | `QuizPlayer.jsx`, `quizCooldownStore.js` (sessionStorage mirror, server authoritative) | Complete |
| Certificate of completion (PDF generated in the browser) | none | `certificate.js` (jsPDF), called from `AgentDashboard.jsx:55`, `CourseCompleteModal.jsx:11`, `Sidebar.jsx:142` | Complete but with placeholder content: signer is hard-coded as "Sarah Jenkins, Director of Operations & Compliance" (`certificate.js:95-99`); certificate ID is `WMH-{courseId}-{last 6 digits of Date.now()}` and is stored nowhere, so it cannot be verified |
| Dark/light theme, persisted in `localStorage` | n/a | `ThemeContext.jsx` | Complete |
| Resizable sidebar width, persisted in `localStorage` | n/a | `Sidebar.jsx:151-187` | Complete |
| Health checks | `/health/live`, `/health/ready` (`Program.cs:198-205`), plus a duplicate `GET /api/health` (`AuthController.cs:31-32`) | n/a | Complete |
| Dev helpers: `POST /api/dev/reset`, `DELETE /api/dev/quiz-lock` (404 outside Development) | `DevController.cs` | `QuizPlayer.jsx:26-51` (`?autofill` URL flag, "reset timer" button), `learn.api.js:54-56` | Dev only; frontend helpers ship to production and must be removed |
| Demo seed: root manager + demo agent + three courses | `WmhLms.Core/Seed/DemoSeed.cs` | n/a | Development only |

**Not present anywhere:** email of any kind, password reset, invitations, refresh tokens, file uploads, SCORM, discussions, gamification, bulk import, SSO, multi-language, notifications, reporting/export, course-edit auditing.

### 2.4 Frontend inventory

- **Stack:** React 18.3, Vite 5.4, `react-router-dom` 7.18, Tailwind CSS 3.4 with `darkMode: 'class'`, `jspdf` 2.5. Plain JSX, no TypeScript. No ESLint or Prettier configuration (one `eslint-disable` comment exists in `CourseViewer.jsx:106`, so the intent was there). No frontend tests.
- **Layout of `src/`:** `App.jsx` (318 lines: route table, layout shell, six route-page wrapper components, breadcrumb logic), `appRoutes.js` (path builders, role checks), `useListQuery.js` (URL-backed list filters), `main.jsx`, `index.css`, `context/` (`AuthContext`, `ThemeContext`), `services/` (`httpClient`, `api` barrel, one `*.api.js` per resource, `certificate`, `logger`, `quizCooldownStore`), `components/{agent,auth,common,layout,manager}/`.
- **Routing:** `HashRouter` (`main.jsx:11`). Routes are declared in `App.jsx:278-306`. Role guard is the `ManagerOnly` wrapper (`App.jsx:39-43`); unauthenticated state renders `LoginScreen` instead of the route tree (`App.jsx:266-275`). Hash routing means the static host needs no SPA rewrite rule.
- **State:** two React contexts (auth, theme) plus local `useState` in every screen. No global store, no query cache.
- **Data fetching:** `fetch` wrapped in `services/httpClient.js`; each screen calls the API in a `useEffect` and keeps its own `loading` / `error` / data triple. The same load-error-retry pattern is hand-written in `AgentDashboard`, `CourseViewer`, `CoursesList`, `CourseEditor`, `AssignmentsManager`, `UserManagement`, `Sidebar`. The same red error-banner class string appears verbatim in nine files.
- **Forms:** controlled inputs with `useState`; validation in handlers. `UserManagement.jsx:33-62` has the only reusable validation helper (`describePasswordPair`).
- **Styling:** Tailwind utility classes in `className` strings (720 `className` occurrences across `src/`). `index.css` holds a small global layer (scrollbars, focus ring, a `.flat-card` class). Inline `style={{...}}` is used exactly four times, all for genuinely dynamic values: progress-bar width (`AgentDashboard.jsx:230`, `AssignmentsManager.jsx:273`, `Sidebar.jsx:243`), sidebar width (`Sidebar.jsx:192`), login background image (`LoginScreen.jsx:60`). The literal colour `#F7F8ED` appears as an arbitrary Tailwind value 77 times; `bg-[#4ADE80]`, `bg-[#16a34a]`, `bg-[#f87171]`, `bg-[#dc2626]`, `bg-[#FC1E1D]` and others are also inlined. There is no token file. Tailwind's config (`tailwind.config.js`) defines the `watermelon`, `zinc`, `brand`, and `wmh` palettes and disables box-shadow globally.
- **Assets:** logos and the login background live in `/assets/` at the repo root and are imported with absolute paths (`LoginScreen.jsx:13-15`). One filename contains spaces. Vite bundles them correctly; it is untidy, not broken.
- **Config:** `VITE_API_BASE_URL` read in `httpClient.js:13`, defaulting to `/api`. The untracked `.env` sets it to `http://localhost:5000/api`. `vite.config.js` sets `sourcemap: 'hidden'` and splits React into its own chunk.
- **Error reporting hook:** `services/logger.js` exposes `installErrorReporter(fn)`; nothing calls it. `ErrorBoundary.jsx:21` has a comment where a reporter should go.
- **CI:** `.github/workflows/ci.yml` runs `npm ci`, `npm audit --audit-level=high --omit=dev`, `npm run build`. It cannot currently pass because the lockfile is untracked.
- **Build check during this review:** `npm run build` succeeds in about 20 seconds. Output is route-split as intended (`react`, `CourseViewer`, `CourseEditor`, `UserManagement`, `AssignmentsManager` chunks). The 200 kB `html2canvas` chunk and 150 kB `index.es` chunk come from `jspdf`; they load only when a certificate is generated.

### 2.5 Authentication and authorization today

- **Token issue:** `POST /api/auth/login` calls `AuthService.LoginAsync`, which verifies with `PasswordHasher<User>` and returns `{ token, user }`. Token claims: `sub` (user id), `role`, `email`, `jti`. Signed HS256 with `Jwt:Key`; expiry `Jwt:ExpiryMinutes` (1440). Key is validated at startup to be at least 32 bytes and is mandatory outside Development (`Program.cs:216-235`).
- **Token transport:** `Authorization: Bearer` header, token persisted in `localStorage` (`httpClient.js:15-32`).
- **Server enforcement:** `FallbackPolicy = RequireAuthenticatedUser` (`Program.cs:126-133`), so every endpoint is authenticated unless it opts out with `[AllowAnonymous]` (login, the two health endpoints, `GET /api/health`, `POST /api/dev/reset`). Manager-only controllers carry `[Authorize(Roles = "manager")]`. Root-only rules live in `UserService` and are tested. Agent identity for learn endpoints always comes from the token via `BaseController.ResolveAgentId` (`Base.cs:31-38`); a client-supplied `agent_id` can only widen access for managers.
- **Client enforcement (UI only):** `ManagerOnly` route wrapper, `isAllowedPath`, and `UserManagement.blockedReason`. None of these are relied on for security.
- **No client-supplied role is trusted anywhere.** Role comes from the token claim, which is re-checked against the database on every request.

### 2.6 Things that will not survive the move off localhost

| Item | Where | Effect in production | Fix |
|---|---|---|---|
| SQLite file database | `Program.cs:49-50`, `Dockerfile` (`ConnectionStrings__Sqlite`, `VOLUME /data`) | Railway's filesystem is ephemeral; every deploy wipes the database | Postgres via Npgsql (section 8) |
| SQLite-typed migrations and `NOCASE` collation | `WmhLms.Data/Migrations/*`, `AppDbContext.cs:32` | Migration fails on Postgres | Regenerate migrations; drop the collation |
| Explicit primary keys in seed data | `DemoSeed.cs:46, 53, 75, 84, ...` | On Postgres identity columns the sequence does not advance past inserted values; the next insert collides | Seed without explicit ids |
| No root manager outside Development | `Program.cs:256`, `appsettings.json` (`Seed:DemoContent=false`) | Empty user table; nobody can log in | Always-on root bootstrap from env vars |
| Login limiter keyed on IP | `Program.cs:140-149`, `appsettings.json` (`LoginAttempts: 10`) | Office NAT shares one IP; cohorts get 429 | Set `RateLimit__LoginAttempts` high enough for the office |
| Frontend API URL | `.env` (`http://localhost:5000/api`) | Production build points at localhost | Set `VITE_API_BASE_URL` in the Cloudflare Pages build environment |
| Untracked lockfile | frontend `.gitignore` | `npm ci` fails; non-reproducible builds | Track `package-lock.json` |
| Dockerfile `HEALTHCHECK` uses `wget` | `Dockerfile` | `aspnet:8.0` (Debian slim) ships neither `wget` nor `curl`; the check would fail every time. Railway ignores Dockerfile health checks anyway | Delete the line; configure Railway's healthcheck path |
| Dockerfile runs the test suite during image build | `Dockerfile` (`RUN dotnet test`) | A flaky or slow test blocks a 9pm hotfix deploy | Tests run in GitHub Actions; the image only publishes |
| Dev-only helpers in the shipped bundle | `QuizPlayer.jsx:26-51`, `learn.api.js:54-56` | Harmless (server 404s) but exposes the `is_correct` leak in the UI | Remove with the answer-key fix |
| Committed demo passwords | `appsettings.Development.json`, `WmhLms.Api.http` | Dev-only, but the root email matches a real person | Use obviously fake dev credentials; production password only ever in env |
| Two untracked README deletions | both repos (`git status`) | Confusion for future you | Commit the deletions; this document replaces them |

Things that were checked and are **fine**: CORS uses `WithOrigins` from config and throws outside Development if empty (`Program.cs:160-171`); HTTPS redirection and HSTS are on outside Development behind `UseForwardedHeaders` (`Program.cs:65-70, 181-186`); there is no `DeveloperExceptionPage`; the exception middleware hides details outside Development (`ExceptionHandlingMiddleware.cs:43-45`); no code writes to local disk; `sourcemap: 'hidden'` keeps maps out of the served bundle; no `dangerouslySetInnerHTML` anywhere in the frontend, so text items cannot inject markup.

---

## 3. Gap list

Tags: `BLOCKING` = must be done before Monday; `WEEK-2` = safe to defer past launch but should not slip further; `LATER` = backlog. The blocking list is deliberately short. Everything on it is either a launch-stopper or a fix under two hours.

### BLOCKING

| # | Gap | Evidence | Fix (detail in section) |
|---|---|---|---|
| B1 | Database is SQLite; Railway disk is ephemeral | `WmhLms.Data.csproj`, `Program.cs:49`, `Dockerfile` | Switch to Postgres via Npgsql, regenerate migrations, local Docker Postgres (8) |
| B2 | No root manager is created outside Development | `Program.cs:256`, `DemoSeed.cs:31` | Always-on idempotent root bootstrap from `RootManager__Email` / `RootManager__Password` (8.4) |
| B3 | Seed inserts explicit primary keys | `DemoSeed.cs:46, 53, 75...` | Remove explicit ids; demo seed stays Development-only (8.4) |
| B4 | `NOCASE` collation is SQLite-only | `AppDbContext.cs:32` | Normalise emails to lower case on write; plain unique index (8.2) |
| B5 | Login rate limit per IP will block the office | `Program.cs:140`, `appsettings.json` | `RateLimit__LoginAttempts=100` in Railway variables (10) |
| B6 | Quiz answer key sent to agents; dev helpers in shipped UI | `Dtos.cs:27`, `LearnService.cs:118`, `QuizPlayer.jsx:26-51`, `learn.api.js:54` | Learn-facing option DTO without `IsCorrect`; delete `?autofill`, "reset timer", `resetQuizLock` (9) |
| B7 | `package-lock.json` is git-ignored | frontend `.gitignore` | Remove the ignore line, commit the lockfile |
| B8 | Frontend API URL points at localhost | frontend `.env` | `VITE_API_BASE_URL` in Cloudflare Pages build settings (10, 11) |
| B9 | Dockerfile: SQLite env, `wget` health check, tests in build | `Dockerfile` | Replace with the Dockerfile in section 11 |
| B10 | No production deployment exists | n/a | Railway + Supabase + Cloudflare Pages first deploy (11) |
| B11 | No backups: the Free plan (owner's choice) has none | Supabase plan facts (12.5) | The `pg_dump` GitHub Actions workflow, run twice daily, is the only backup; one restore rehearsal before launch (12) |
| B12 | Certificate carries a fake signer, a fake ID and a score line | `certificate.js:78-99` | Owner decision: the certificate is generic. Delete the signature block, the ID line and the score line; keep company name, title, agent name, course title, completion date. Design refresh is W17 |
| B13 | Root protection only in the service layer | `UserServices.cs:128, 164, 178` | Postgres trigger and single-root partial index in the initial migration (8.5). Included in BLOCKING only because it rides along in the migration being written anyway; costs 20 minutes |

### WEEK-2

| # | Gap | Evidence | Fix |
|---|---|---|---|
| W1 | Api project references `AppDbContext` | `Program.cs:102`, `DevController.cs:17` | Composition-root extension method in Data; account check via an Application interface (4, 6) |
| W2 | Application depends on Data; no repositories | every service in `WmhLms.Core/Services/` | Entities move to Core; repository interfaces in Core, implementations in Data (4, 5, 6) |
| W3 | Multi-class files | table in 2.1 | One class per file (5) |
| W4 | Services return `object` / dictionaries | 2.1 list | Named DTOs (6) |
| W5 | `LearnService` reads `IConfiguration` | `LearnService.cs:27-30` | `IOptions<QuizOptions>` (6) |
| W6 | No error tracking | `logger.js:16` unused, no backend Sentry | Sentry free tier both sides; wire `installErrorReporter` and `ErrorBoundary.componentDidCatch` (12) |
| W7 | Correlation id not shown to users | `httpClient.js` ignores `X-Correlation-Id` | Attach to thrown errors and show in error banners (12) |
| W8 | Console logs are plain text | `Program.cs` default logging | `AddJsonConsole()` so Railway log search works on fields (12) |
| W9 | Sequential gating is client-only | `LearnService.CompleteItemAsync` | Require all earlier items complete before accepting a completion (9) |
| W10 | No HTTP-level tests of auth and role enforcement | `WmhLms.Tests/` covers services only | Six `WebApplicationFactory` tests against Docker Postgres (13) |
| W11 | Frontend has no lint | no config | ESLint with `react-hooks` plugin; CI runs it (7) |
| W12 | Route-page components live in `App.jsx` | `App.jsx:45-112` | `src/pages/` (5, 7) |
| W13 | Committed dev credentials match real emails | `appsettings.Development.json` | Fake dev accounts (`root@wmh.local`) |
| W14 | Duplicate `GET /api/health` | `AuthController.cs:31` | Delete; keep `/health/live` and `/health/ready` |
| W15 | `AuditController` has no UI; audit covers users only | `AuditController.cs` | Owner decision: build a root-only read-only screen and record course, section, item, question and assignment actions too |
| W16 | Login form does not remember the agent's email | `LoginScreen.jsx:21` | Store the last successful email in `localStorage`, pre-fill it, focus the password field. Password autofill is left to the browser; the form already has `autocomplete="email"` and `autocomplete="current-password"` (`LoginScreen.jsx:118, 138`) |
| W17 | Certificate design does not match the app | `certificate.js` | Generic company certificate in the app's theme: page tint, green accent rule, logo, no signer (spec in 15) |
| W18 | Root cannot change its own name or password | `UserServices.cs:128` | Owner decision: root may edit its own first name, last name and password; role, status and `IsRoot` stay locked (the trigger in 8.5 already allows exactly this) |

### LATER

| # | Gap | Fix |
|---|---|---|
| L1 | Styling is Tailwind utility strings; no token file; `#F7F8ED` inlined 77 times | Token file + CSS Modules, migrated screen by screen (7.6). Largest item in the backlog |
| L2 | Load/error/retry pattern duplicated in seven screens | `useAsyncData` hook + `ErrorBanner` / `LoadingState` shared components (7) |
| L3 | `Assignment.CompletedItemIdsJson` is a JSON string column | Normalise into an `ItemCompletions` table when reporting needs per-item timestamps (8.6) |
| L4 | Dashboard payload includes full section trees with questions | Trim to counts (`to-be-implemented.md` section 5) |
| L5 | `HashRouter` URLs contain `#/` | Switch to `BrowserRouter` with a Cloudflare Pages `_redirects` rule once nothing else is in flight |
| L6 | Assets at repo root with a space in one filename | Move to `src/assets/`, rename |
| L7 | No way to add a course other than typing it in | Course import/export endpoint using the existing course-tree JSON shape (spec in 15). Owner wants this as the first extension after launch |
| L8 | Supabase TLS uses `Trust Server Certificate=true` | Pin the Supabase CA and use `SSL Mode=VerifyFull` (8.3) |

---

## 4. Target architecture

### 4.1 System diagram

```
                       https://app.<domain>                      https://api.<domain>
                    +----------------------+                  +------------------------+
  Agents (50-100)   |  Cloudflare Pages    |   JSON over      |  Railway               |
  Managers (few)    |  static React SPA    |   HTTPS + CORS   |  one Docker container  |
  --------------->  |  (Vite build output) | ---------------> |  WmhLms.Api (.NET 8)   |
   browser          |                      |  Bearer JWT      |  Kestrel on :8080      |
                    +----------+-----------+                  +-----------+------------+
                               | <iframe>                                 | Npgsql, TLS
                               v                                          v
                    +----------------------+                  +------------------------+
                    |  YouTube (unlisted)  |                  |  Supabase Postgres     |
                    |  youtube-nocookie    |                  |  via session pooler    |
                    +----------------------+                  |  :5432                 |
                                                              +-----------+------------+
                                                                          | nightly pg_dump
                                                                          v
                                                              +------------------------+
                                                              | GitHub Actions cron    |
                                                              | encrypted artifact     |
                                                              +------------------------+

  GitHub (2 repos) --push to main--> GitHub Actions (build + test gate)
                   --push to main--> Railway auto-deploy (backend image)
                   --push to main--> Cloudflare Pages auto-deploy (frontend)
  Sentry (free): browser SDK in the SPA, .NET SDK in the API          [WEEK-2]
```

Two artifacts, one managed database, no other moving parts. There is no queue, cache, worker, or second service, and none is planned.

### 4.2 The three backend tiers

| Tier | Project | Responsibility in one sentence |
|---|---|---|
| Presentation | `WmhLms.Api` | Turn HTTP into calls on Application services and turn their results or exceptions into HTTP responses, and nothing else. |
| Application | `WmhLms.Core` | Own every business rule (who may do what, what counts as complete, how a quiz is graded), expressed against entities and repository interfaces it defines itself. |
| Data access | `WmhLms.Data` | Persist and retrieve Core's entities with EF Core and Npgsql, implementing Core's repository interfaces, with no knowledge of HTTP, DTOs, or callers. |

Project names stay as they are. `WmhLms.Core` is the Application tier; renaming it to `WmhLms.Application` would cost a morning of churn and buy nothing.

### 4.3 Dependency direction rules

```
   WmhLms.Api -------> WmhLms.Core <------- WmhLms.Data
        |                                        ^
        +----------------------------------------+
          (composition root only: one call to AddDataAccess(...))
```

1. `WmhLms.Core` references **no** other project. It contains entities, repository interfaces, service classes, DTOs, validation, and the `ApiException` type.
2. `WmhLms.Data` references `WmhLms.Core` and implements its interfaces. It contains `AppDbContext`, entity configurations, migrations, repositories, and one `AddDataAccess` extension method.
3. `WmhLms.Api` references both, but the **only** symbol it may use from `WmhLms.Data` is `AddDataAccess`. No controller, middleware, or startup code may name `AppDbContext`, `DbSet`, or `Microsoft.EntityFrameworkCore`. A test (section 13.3) enforces this so it cannot regress silently.
4. `WmhLms.Data` must not reference `Microsoft.AspNetCore.*`. It has no reason to, and the project file must not gain one.
5. Controllers never contain a rule. If a controller has an `if` that is not about HTTP shape, the rule belongs in a service.
6. Services never see `HttpContext`, headers, or claims. The controller resolves the caller's id and role from the token and passes them as plain arguments, exactly as `BaseController.CurrentUserId` and `ResolveAgentId` do today.
7. Entities never leave the Application tier. Controllers receive and return DTOs only.

Why this shape and not a stricter one: repositories here are thin, aggregate-scoped classes with intention-revealing methods, not a generic repository or specification pattern. That is enough to keep EF out of the service layer and the service layer testable, and it is small enough for one person to keep consistent.

### 4.4 How new features and integrations fit

The owner's stated reason for the structure is extensibility, with course import as the first concrete example. In this architecture a new capability is always the same four files: a request/response DTO in `WmhLms.Core/<Feature>/`, a method on the feature's service, a repository method if a new query is needed, and a controller action. Nothing else changes. Course import is the worked example in section 15.

External tools integrate through the same JSON API with the same bearer token; a manager's token is enough for a script that bulk-creates users or courses. If a permanent machine credential is ever needed, it is a `Role = "integration"` user with a long-lived token, not a second auth system.

---

## 5. Target directory structure

Both trees are prescriptive. One class per file, file name equals class name. Folders inside `WmhLms.Core` are named after the feature they serve (`Users`, `Courses`, `Learn`), not after the kind of class they hold, so everything about one feature sits together.

### 5.1 Backend solution

```
WMH-LMS-BACKEND/
+-- WmhLms.sln
+-- Dockerfile                          (section 11.2)
+-- docker-compose.yml                  (local Postgres only, section 10.3)
+-- .dockerignore
+-- .github/workflows/
|   +-- ci.yml                          build + test on every push and PR
|   +-- backup.yml                      nightly pg_dump (section 12.5)
|
+-- WmhLms.Api/                         PRESENTATION
|   +-- Program.cs                      about 40 lines: build, call the Startup extensions, run
|   +-- Startup/                        one static class per startup concern, replaces the 275-line Program.cs
|   |   +-- JwtAuthenticationSetup.cs   AddJwtAuthentication(IServiceCollection, IConfiguration)
|   |   +-- AuthorizationSetup.cs       fallback policy
|   |   +-- CorsSetup.cs
|   |   +-- RateLimitingSetup.cs        login policy name lives here, not in Program
|   |   +-- LoggingSetup.cs             AddJsonConsole
|   |   +-- ApplicationServicesSetup.cs registers Core services and IOptions bindings
|   |   +-- PipelineSetup.cs            UseApiPipeline(WebApplication): middleware order in one place
|   |   +-- DatabaseStartup.cs          MigrateAndBootstrapAsync via IDatabaseInitializer + RootManagerBootstrap
|   +-- Auth/
|   |   +-- JwtOptions.cs               (existing)
|   |   +-- JwtTokenService.cs          implements Core.Abstractions.ITokenService (existing TokenService, renamed)
|   |   +-- AccountStatusValidator.cs   OnTokenValidated body, calls Core IAccountStatusReader; no DbContext
|   +-- Controllers/
|   |   +-- ApiController.cs            (existing Base.cs, renamed to match the class)
|   |   +-- AuthController.cs           login, logout
|   |   +-- MeController.cs
|   |   +-- UsersController.cs
|   |   +-- CoursesController.cs
|   |   +-- SectionsController.cs
|   |   +-- ItemsController.cs
|   |   +-- QuestionsController.cs
|   |   +-- AssignmentsController.cs
|   |   +-- LearnController.cs
|   |   +-- AuditController.cs
|   +-- Middleware/
|   |   +-- ExceptionHandlingMiddleware.cs
|   |   +-- SecurityHeadersMiddleware.cs
|   |   +-- CorrelationIdMiddleware.cs
|   +-- appsettings.json
|   +-- appsettings.Development.json
|   +-- Properties/launchSettings.json
|
+-- WmhLms.Core/                        APPLICATION (no project references)
|   +-- Abstractions/                   interfaces implemented outside Core
|   |   +-- IUserRepository.cs
|   |   +-- ICourseRepository.cs
|   |   +-- IAssignmentRepository.cs
|   |   +-- IQuizLockRepository.cs
|   |   +-- IAuditRepository.cs
|   |   +-- IUnitOfWork.cs
|   |   +-- IPasswordHasher.cs          (existing IPasswordService, renamed)
|   |   +-- ITokenService.cs
|   |   +-- IAccountStatusReader.cs     used by the JWT validator
|   |   +-- IDatabaseInitializer.cs     MigrateAsync
|   +-- Common/
|   |   +-- ApiException.cs
|   |   +-- Guard.cs
|   |   +-- JsonIds.cs
|   |   +-- Progress.cs
|   |   +-- PagedResult.cs
|   |   +-- PaginationDto.cs
|   +-- Options/
|   |   +-- QuizOptions.cs              CooldownMinutes
|   |   +-- RootManagerOptions.cs       Email, Password, FirstName, LastName
|   +-- Domain/                         entities (moved from WmhLms.Data/Entities)
|   |   +-- User.cs
|   |   +-- UserRole.cs                 const strings "manager", "agent"
|   |   +-- UserStatus.cs
|   |   +-- Course.cs
|   |   +-- CourseStatus.cs
|   |   +-- Section.cs
|   |   +-- Item.cs
|   |   +-- ItemType.cs
|   |   +-- Question.cs
|   |   +-- QuestionType.cs
|   |   +-- Option.cs
|   |   +-- Assignment.cs
|   |   +-- AssignmentStatus.cs
|   |   +-- QuizLock.cs
|   |   +-- AuditEntry.cs
|   +-- Auth/
|   |   +-- AuthService.cs
|   |   +-- LoginRequest.cs
|   |   +-- LoginResponse.cs            replaces the Dictionary in AuthController
|   +-- Users/                          <- the full vertical slice shown in section 6
|   |   +-- UserService.cs
|   |   +-- UserMapper.cs
|   |   +-- UserDto.cs
|   |   +-- UserListFilter.cs
|   |   +-- CreateUserRequest.cs
|   |   +-- UpdateUserRequest.cs
|   |   +-- UpdateStatusRequest.cs
|   |   +-- UserAssignmentDto.cs        replaces the Dictionary in GetAssignmentsAsync
|   +-- Audit/
|   |   +-- AuditService.cs
|   |   +-- AuditActions.cs
|   |   +-- AuditEntryDto.cs
|   +-- Courses/
|   |   +-- CourseService.cs            list, get, create, update, delete
|   |   +-- SectionService.cs
|   |   +-- ItemService.cs
|   |   +-- QuestionService.cs          includes the option-shape rules from BuildOptions
|   |   +-- CourseMapper.cs
|   |   +-- CourseDto.cs, SectionDto.cs, ItemDto.cs, QuestionDto.cs, OptionDto.cs
|   |   +-- CreateCourseRequest.cs, UpdateCourseRequest.cs, SectionTitleRequest.cs,
|   |       ReorderSectionsRequest.cs, CreateItemRequest.cs, UpdateItemRequest.cs,
|   |       ReorderItemsRequest.cs, QuestionRequest.cs, OptionRequest.cs
|   +-- Assignments/
|   |   +-- AssignmentService.cs
|   |   +-- BulkAssignmentRequest.cs
|   |   +-- CourseAssignmentDto.cs      replaces the Dictionary in GetCourseAssignmentsAsync
|   +-- Learn/
|   |   +-- LearnDashboardService.cs    GetCoursesAsync (the three buckets)
|   |   +-- CourseProgressService.cs    tree, resume, complete item
|   |   +-- QuizService.cs              submit, lock status, record view
|   |   +-- QuizGrader.cs               pure function: questions + answers -> incorrect ids
|   |   +-- ProgressReconciler.cs       Reconcile / ApplyCompletion (existing private statics)
|   |   +-- LearnMapper.cs              learn-facing DTOs: no IsCorrect
|   |   +-- LearnOptionDto.cs, LearnQuestionDto.cs, LearnItemDto.cs, LearnSectionDto.cs
|   |   +-- CourseWithProgressDto.cs, LearnBucketsDto.cs, CourseTreeDto.cs, ResumeDto.cs
|   |   +-- CompleteItemRequest.cs, CompleteItemResultDto.cs
|   |   +-- SubmitQuizRequest.cs, QuizAnswerRequest.cs, QuizResultDto.cs
|   |   +-- RecordViewRequest.cs        (moved out of LearnController.cs)
|   |   +-- QuizLockStatusDto.cs
|   +-- Seeding/
|       +-- RootManagerBootstrap.cs     always runs, idempotent
|       +-- DemoSeed.cs                 Development only
|
+-- WmhLms.Data/                        DATA ACCESS (references WmhLms.Core)
|   +-- DependencyInjection.cs          AddDataAccess(IServiceCollection, string connectionString)
|   +-- AppDbContext.cs                 DbSets + ApplyConfigurationsFromAssembly, nothing else
|   +-- DatabaseInitializer.cs
|   +-- UnitOfWork.cs                   SaveChangesAsync; maps unique violations to ApiException.Conflict
|   +-- Configurations/                 one IEntityTypeConfiguration<T> per entity
|   |   +-- UserConfiguration.cs
|   |   +-- CourseConfiguration.cs
|   |   +-- SectionConfiguration.cs
|   |   +-- ItemConfiguration.cs
|   |   +-- QuestionConfiguration.cs
|   |   +-- OptionConfiguration.cs
|   |   +-- AssignmentConfiguration.cs
|   |   +-- QuizLockConfiguration.cs
|   |   +-- AuditEntryConfiguration.cs
|   +-- Repositories/
|   |   +-- UserRepository.cs
|   |   +-- CourseRepository.cs
|   |   +-- AssignmentRepository.cs
|   |   +-- QuizLockRepository.cs
|   |   +-- AuditRepository.cs
|   +-- Migrations/
|       +-- 2026MMDDHHMMSS_Initial.cs (+ .Designer.cs)
|       +-- AppDbContextModelSnapshot.cs
|
+-- WmhLms.Tests/
    +-- TestDb.cs                       SQLite in-memory for unit tests (unchanged)
    +-- Unit/
    |   +-- UserServiceTests.cs         (existing)
    |   +-- QuizServiceTests.cs         (existing LearnServiceTests, split with the service)
    |   +-- CourseProgressServiceTests.cs
    +-- Integration/
    |   +-- ApiFactory.cs               WebApplicationFactory against Docker Postgres
    |   +-- AuthTests.cs
    |   +-- RoleEnforcementTests.cs
    +-- Architecture/
        +-- LayeringTests.cs            reflection checks on project references (section 13.3)
```

### 5.2 Frontend `src/`

```
WMH-LMS-FRONTEND/
+-- index.html
+-- package.json, package-lock.json     (lockfile tracked)
+-- vite.config.js
+-- eslint.config.js                    (WEEK-2)
+-- public/
|   +-- _headers                        Cloudflare Pages response headers (section 11.3)
+-- src/
    +-- main.jsx                        createRoot, StrictMode, ErrorBoundary, HashRouter, <App/>
    +-- app/
    |   +-- App.jsx                     providers + <AppRoutes/>; nothing else
    |   +-- AppRoutes.jsx               the route table (from App.jsx:278-306)
    |   +-- AppLayout.jsx               shell: sidebar + top nav + <Outlet/> (from App.jsx:114-239)
    |   +-- AppLayout.module.css
    |   +-- HomeRedirect.jsx
    |   +-- ManagerOnly.jsx
    |   +-- paths.js                    (from appRoutes.js)
    +-- pages/                          one file per route; a page only composes feature components
    |   +-- LoginPage.jsx
    |   +-- agent/
    |   |   +-- DashboardPage.jsx
    |   |   +-- CoursePage.jsx
    |   +-- manager/
    |       +-- CoursesPage.jsx
    |       +-- CourseEditorPage.jsx
    |       +-- AssignmentsPage.jsx
    |       +-- UsersPage.jsx
    +-- features/                       everything about one domain area in one folder
    |   +-- auth/
    |   |   +-- AuthProvider.jsx        (from context/AuthContext.jsx)
    |   |   +-- useAuth.js
    |   |   +-- authApi.js              (from services/auth.api.js)
    |   |   +-- tokenStorage.js         getToken / setToken (from httpClient.js)
    |   |   +-- LoginForm.jsx
    |   |   +-- LoginForm.module.css
    |   +-- learn/                      <- the full vertical slice shown in section 7
    |   |   +-- learnApi.js
    |   |   +-- useAgentCourses.js      dashboard buckets loader
    |   |   +-- useCourseTree.js        tree + completed ids loader
    |   |   +-- useQuizLock.js          server lock + sessionStorage mirror (from QuizPlayer.jsx:53-112)
    |   |   +-- quizCooldownStore.js
    |   |   +-- certificate.js
    |   |   +-- AgentDashboard.jsx
    |   |   +-- AgentDashboard.module.css
    |   |   +-- CourseCard.jsx          one card (from AgentDashboard.jsx:161-272)
    |   |   +-- CourseCard.module.css
    |   |   +-- CourseViewer.jsx
    |   |   +-- CourseViewer.module.css
    |   |   +-- CurriculumSidebar.jsx   agent half of Sidebar.jsx
    |   |   +-- CurriculumSidebar.module.css
    |   |   +-- QuizPlayer.jsx
    |   |   +-- QuizPlayer.module.css
    |   |   +-- QuizQuestion.jsx        one fieldset (from QuizPlayer.jsx:316-396)
    |   |   +-- QuizQuestion.module.css
    |   |   +-- QuizLockBanner.jsx      (from QuizPlayer.jsx:238-296)
    |   |   +-- YouTubePlayer.jsx
    |   |   +-- YouTubePlayer.module.css
    |   |   +-- TextItemViewer.jsx
    |   |   +-- CourseCompleteModal.jsx
    |   +-- courses/
    |   |   +-- coursesApi.js
    |   |   +-- CoursesList.jsx (+ .module.css)
    |   |   +-- CourseRow.jsx
    |   |   +-- CourseEditor.jsx (+ .module.css)
    |   |   +-- SectionCard.jsx         (from CourseEditor.jsx:334-486)
    |   |   +-- ItemEditor.jsx (+ .module.css)
    |   |   +-- QuizQuestionEditor.jsx (+ .module.css)
    |   |   +-- QuestionForm.jsx        the modal body (from QuizQuestionEditor.jsx:250-355)
    |   |   +-- youtubeEmbed.js         getEmbedUrl, currently duplicated in YouTubePlayer.jsx and ItemEditor.jsx
    |   +-- assignments/
    |   |   +-- assignmentsApi.js
    |   |   +-- AssignmentsManager.jsx (+ .module.css)
    |   |   +-- CohortSummary.jsx       (from AssignmentsManager.jsx:164-216)
    |   |   +-- EnrollAgentsModal.jsx   (from AssignmentsManager.jsx:305-395)
    |   +-- users/
    |       +-- usersApi.js
    |       +-- UserManagement.jsx (+ .module.css)
    |       +-- UserTable.jsx
    |       +-- UserForm.jsx            create and edit share it
    |       +-- PasswordFields.jsx      (from UserManagement.jsx:72-163)
    |       +-- passwordRules.js        describePasswordPair, MIN_PASSWORD_LENGTH
    |       +-- UserAssignmentsModal.jsx
    +-- shared/
        +-- api/
        |   +-- httpClient.js           request(), attaches token, throws ApiError with status + correlationId
        |   +-- ApiError.js
        +-- components/
        |   +-- Button.jsx, Button.module.css
        |   +-- Badge.jsx, Badge.module.css
        |   +-- Modal.jsx, Modal.module.css
        |   +-- Select.jsx, Select.module.css
        |   +-- EmptyState.jsx, EmptyState.module.css
        |   +-- ErrorBanner.jsx, ErrorBanner.module.css     (the nine copies become one)
        |   +-- LoadingState.jsx
        |   +-- ErrorBoundary.jsx
        |   +-- icons/Icons.jsx
        +-- hooks/
        |   +-- useAsyncData.js         loading / error / data / reload
        |   +-- useListQuery.js         (from src/useListQuery.js)
        +-- layout/
        |   +-- ManagerSidebar.jsx      manager half of Sidebar.jsx
        |   +-- TopNav.jsx
        |   +-- UserControls.jsx        name + sign out + theme toggle (currently pasted in three places)
        +-- theme/
        |   +-- ThemeProvider.jsx
        |   +-- useTheme.js
        +-- styles/
        |   +-- tokens.css              CSS custom properties: colours, spacing, radius, type
        |   +-- global.css              reset, body, scrollbars, focus ring (from index.css)
        +-- logging/
            +-- logger.js
```

Rules that follow from the tree:

- `pages/` files are thin. `DashboardPage.jsx` is `useNavigate` plus `<AgentDashboard onLaunchCourse=... />`, as `AgentDashboardPage` is today in `App.jsx:45-54`.
- A feature may import from `shared/` and from its own folder. A feature must not import from another feature. If two features need the same thing, it moves to `shared/`.
- `shared/` must not import from `features/` or `pages/`.
- The `services/api.js` barrel is deleted. Components import their feature's `*Api.js` directly. The barrel exists to preserve an old mock-layer call shape (`api.js:7-11`) and now only adds a hop.

---

## 6. Backend conventions

### 6.1 Naming and responsibilities

| Thing | Convention | Responsibility |
|---|---|---|
| Project | `WmhLms.Api`, `WmhLms.Core`, `WmhLms.Data`, `WmhLms.Tests` | see 4.2 |
| Controller | `<Resource>Controller`, `sealed`, inherits `ApiController`, one per URL family | Bind route/body, resolve caller from token, call one service method, return `Ok(dto)` or `NoContent()`. No `try/catch`, no rules, no mapping. |
| Service | `<Feature>Service`, `sealed`, in `WmhLms.Core/<Feature>/` | All rules. Validates input via `Guard`, checks permissions, loads through repositories, mutates entities, adds audit entries, calls `IUnitOfWork.SaveChangesAsync` once. Throws `ApiException` for expected failures. |
| Repository interface | `I<Aggregate>Repository` in `WmhLms.Core/Abstractions/` | Intention-revealing methods (`GetTreeAsync`, `EmailExistsAsync`). Returns entities or `PagedResult<T>`. Never exposes `IQueryable`. `Add`/`Remove` are synchronous (they only track). |
| Repository | `<Aggregate>Repository`, `internal sealed`, in `WmhLms.Data/Repositories/` | Translate one interface method into EF calls. No rules. `internal` so nothing outside Data can construct one. |
| Unit of work | `IUnitOfWork.SaveChangesAsync(ct)` | One commit per service method. Maps `DbUpdateException` unique-index violations to `ApiException.Conflict`, so the API never references EF. |
| Entity | plain class in `WmhLms.Core/Domain/` | State plus small invariant helpers (`User.Create(...)`). No EF attributes; configuration lives in Data. |
| DTO | `record` in the feature folder; `<Name>Request` for input, `<Name>Dto` for output | Immutable shapes. Requests carry a `Validated()` method (6.5). |
| Mapper | `static class <Feature>Mapper` | Entity to DTO. Called by services only. No AutoMapper: the mappings are ten lines each and a library would hide them. |
| Options | `<Name>Options` in `WmhLms.Core/Options/`, bound with `services.Configure<T>(config.GetSection(...))` | Typed configuration. Services take `IOptions<T>`, never `IConfiguration`. |
| Constants | `static class UserRole { public const string Manager = "manager"; ... }` | Replace the string literals scattered through services and controllers. |

Rules of thumb: a controller file over 60 lines, a service file over 250 lines, or a method over 40 lines is a signal to split by responsibility. `CourseService.cs` (246 lines, four entity types) and `LearnService.cs` (290 lines, three concerns) are the two current cases and section 5 shows the split.

### 6.2 DTOs versus entities, and where mapping happens

- Controllers see DTOs only. They never `using WmhLms.Core.Domain`.
- Services receive request DTOs, work on entities, and return response DTOs. Mapping happens at the end of the service method through the feature's mapper.
- Learn-facing DTOs are a separate family (`LearnOptionDto` without `IsCorrect`) from manager-facing ones (`OptionDto` with it). This is the fix for B6, and the type system then makes the leak impossible to reintroduce by accident.
- Responses use `snake_case` because `Program.cs:46` sets `JsonNamingPolicy.SnakeCaseLower` globally, and the frontend contract depends on it. The one exception is `PaginationDto.TotalPages`, which is pinned to `totalPages` with `JsonPropertyName` because `CoursesList.jsx:37` and `UserManagement.jsx:266` read it that way. Keep the exception until the frontend reads `total_pages`; then delete the attribute.

### 6.3 Validation strategy

Keep `Guard` (`Support.cs:22-71`). It is small, tested through `UserServiceTests`, and throws the same `ApiException.BadRequest` the middleware already handles. Do not add DataAnnotations or FluentValidation; two validation systems is worse than one plain one.

Where it runs: at the top of the service method, before any I/O, via the request record's `Validated()` method (example in 6.5). A controller never validates. `[ApiController]` model-binding failures (malformed JSON) still produce the framework's 400, which is fine.

### 6.4 Async, cancellation, dependency injection

- Every I/O method is `async Task<T>` with the `Async` suffix and a trailing `CancellationToken ct` parameter. Controllers declare `CancellationToken ct` as the last action parameter; ASP.NET binds it to the request's abort token automatically. Pass it through to EF. Never `.Result`, `.Wait()`, or `async void`.
- Lifetimes: `AppDbContext`, repositories, unit of work, and services are `Scoped`. `JwtOptions`, `TimeProvider.System`, and `IPasswordHasher` are `Singleton`. This matches `Program.cs:52-60` today.
- Registration lives in two extension methods: `AddDataAccess` (Data) and `AddApplicationServices` (Api, `Startup/ApplicationServicesSetup.cs`). `Program.cs` calls each once. Nothing else calls `services.Add*` for application types.
- Constructors use C# 12 primary constructors, as the code already does.

### 6.5 Example stubs: the Users vertical slice

All five compile against the target structure and are consistent with each other.

**Request DTO with validation** (`WmhLms.Core/Users/CreateUserRequest.cs`):

```csharp
using WmhLms.Core.Common;
using WmhLms.Core.Domain;

namespace WmhLms.Core.Users;

public sealed record CreateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string? Password,
    string? Role)
{
    /// <summary>
    /// Throws ApiException(400) on the first invalid field. Returns a trimmed,
    /// lower-cased-email copy so the service never touches raw input.
    /// </summary>
    public CreateUserRequest Validated() => this with
    {
        FirstName = Guard.RequiredText(FirstName, "First name", 100),
        LastName = Guard.RequiredText(LastName, "Last name", 100),
        Email = Guard.Email(Email),
        Password = Guard.Password(Password),
        Role = Guard.OneOf(string.IsNullOrWhiteSpace(Role) ? UserRole.Agent : Role, UserRole.All, "role")
    };
}
```

`Guard.Email` gains one line versus today: it returns `email.ToLowerInvariant()`. That is what makes the plain unique index in section 8.2 case-insensitive without a collation.

**Repository interface and method** (`WmhLms.Core/Abstractions/IUserRepository.cs`, `WmhLms.Data/Repositories/UserRepository.cs`):

```csharp
namespace WmhLms.Core.Abstractions;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(long id, CancellationToken ct);
    Task<User?> GetByEmailAsync(string normalisedEmail, CancellationToken ct);
    Task<bool> EmailExistsAsync(string normalisedEmail, long? exceptUserId, CancellationToken ct);
    Task<PagedResult<User>> ListAsync(UserListFilter filter, CancellationToken ct);
    void Add(User user);
    void Remove(User user);
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using WmhLms.Core.Abstractions;
using WmhLms.Core.Common;
using WmhLms.Core.Domain;
using WmhLms.Core.Users;

namespace WmhLms.Data.Repositories;

internal sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(long id, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByEmailAsync(string normalisedEmail, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Email == normalisedEmail, ct);

    public Task<bool> EmailExistsAsync(string normalisedEmail, long? exceptUserId, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Email == normalisedEmail && (exceptUserId == null || u.Id != exceptUserId), ct);

    public async Task<PagedResult<User>> ListAsync(UserListFilter filter, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking().AsQueryable();
        if (filter.Role is not null) query = query.Where(u => u.Role == filter.Role);
        if (filter.Status is not null) query = query.Where(u => u.Status == filter.Status);
        if (filter.Search is not null)
        {
            var s = filter.Search.ToLowerInvariant();
            query = query.Where(u => (u.FirstName + " " + u.LastName).ToLower().Contains(s) || u.Email.Contains(s));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(u => u.Id)
            .Skip((filter.Page - 1) * filter.Limit).Take(filter.Limit).ToListAsync(ct);
        return PagedResult<User>.Of(items, filter.Page, filter.Limit, total);
    }

    public void Add(User user) => db.Users.Add(user);
    public void Remove(User user) => db.Users.Remove(user);
}
```

**Service method** (`WmhLms.Core/Users/UserService.cs`, the create path; the other methods follow the same shape as today's `UserServices.cs:124-189`):

```csharp
using WmhLms.Core.Abstractions;
using WmhLms.Core.Audit;
using WmhLms.Core.Common;
using WmhLms.Core.Domain;

namespace WmhLms.Core.Users;

public sealed class UserService(
    IUserRepository users,
    IAuditRepository audit,
    IPasswordHasher passwords,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<UserDto> CreateAsync(long callerId, CreateUserRequest request, CancellationToken ct)
    {
        var caller = await users.GetByIdAsync(callerId, ct)
            ?? throw ApiException.Unauthorized("Session expired.");
        var input = request.Validated();

        if (input.Role == UserRole.Manager && !caller.IsRoot)
            throw ApiException.Forbidden("Only the root account can create managers.");
        if (await users.EmailExistsAsync(input.Email, exceptUserId: null, ct))
            throw ApiException.Conflict("User with this email already exists.");

        var now = clock.GetUtcNow().UtcDateTime;
        var user = User.Create(input.FirstName, input.LastName, input.Email,
            passwords.Hash(input.Password!), input.Role!, now);

        users.Add(user);
        audit.Add(AuditEntry.Create(caller, AuditActions.UserCreated, "user", null,
            user.Email, $"role={user.Role}", now));
        await unitOfWork.SaveChangesAsync(ct);

        return UserMapper.ToDto(user);
    }
}
```

The two `!` are the price of keeping the request record's optional fields optional at the HTTP boundary; `Validated()` guarantees both are non-null before this line runs.

**Controller action** (`WmhLms.Api/Controllers/UsersController.cs`):

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WmhLms.Core.Domain;
using WmhLms.Core.Users;

namespace WmhLms.Api.Controllers;

[Route("api/users"), Authorize(Roles = UserRole.Manager)]
public sealed class UsersController(UserService users) : ApiController
{
    [HttpPost]
    public async Task<ActionResult<UserDto>> Create([FromBody] CreateUserRequest request, CancellationToken ct) =>
        Ok(await users.CreateAsync(CurrentUserId, request, ct));
}
```

`ApiController` is today's `BaseController` (`Base.cs`) renamed to match its file. `CurrentUserId` and `ResolveAgentId` stay exactly as they are.

**Global exception-handling middleware** (`WmhLms.Api/Middleware/ExceptionHandlingMiddleware.cs`). This is the existing middleware minus the `DbUpdateException` branch, which moves into `UnitOfWork` so the API project drops its EF reference:

```csharp
using WmhLms.Core.Common;

namespace WmhLms.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IHostEnvironment env)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (ApiException ex)
        {
            logger.LogWarning("{Status} on {Method} {Path}: {Message}",
                ex.StatusCode, ctx.Request.Method, ctx.Request.Path, ex.Message);
            await WriteAsync(ctx, ex.StatusCode, ex.Message, ex.RetryAfterSeconds);
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is nobody to answer.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
            var message = env.IsDevelopment()
                ? $"Unexpected server error: {ex.Message}"
                : "An unexpected error occurred. Please try again.";
            await WriteAsync(ctx, StatusCodes.Status500InternalServerError, message);
        }
    }

    private static async Task WriteAsync(HttpContext ctx, int status, string message, int? retryAfterSeconds = null)
    {
        if (ctx.Response.HasStarted) return;
        ctx.Response.Clear();
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        if (retryAfterSeconds.HasValue)
            ctx.Response.Headers.RetryAfter = retryAfterSeconds.Value.ToString();
        await ctx.Response.WriteAsJsonAsync(new { message });
    }
}
```

And the matching unit of work (`WmhLms.Data/UnitOfWork.cs`):

```csharp
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WmhLms.Core.Abstractions;
using WmhLms.Core.Common;

namespace WmhLms.Data;

internal sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Unique index violation: a lost race on (course, agent) or (agent, quiz item).
            throw ApiException.Conflict("That change conflicts with the current state. Please retry.");
        }
    }
}
```

`23505` is Postgres's `unique_violation` code. The `{ message }` response body shape is unchanged, so `httpClient.js:74-75` keeps working.

### 6.6 Exception policy

- `ApiException` (400, 401, 403, 404, 409, 423) is the only exception a service throws on purpose. It carries the user-facing message.
- Anything else reaching the middleware is a bug and is logged at Error with the stack; the client gets a generic 500.
- Never catch and swallow in services. Never catch `Exception` in a controller.
- Messages in `ApiException` are shown to users verbatim by the frontend (`httpClient.js:74`). Write them for a manager, not a developer.

---

## 7. Frontend conventions

### 7.1 Folder taxonomy

Three kinds of folder, defined in section 5.2:

- **`pages/`**: one component per route. It reads route params, wires navigation callbacks, and renders one feature component. No data fetching, no markup beyond a wrapper.
- **`features/<name>/`**: the screens, components, hooks, API module, and CSS Modules for one domain area. A feature owns its API calls (`learnApi.js`) and its loaders (`useCourseTree.js`).
- **`shared/`**: things used by two or more features. Primitive components, the HTTP client, hooks, layout chrome, theme, tokens.

The current `components/{agent,manager}` split by role maps onto features as: `agent/*` becomes `features/learn`, `manager/Courses*`, `ItemEditor`, `QuizQuestionEditor` become `features/courses`, `manager/AssignmentsManager` becomes `features/assignments`, `manager/UserManagement` becomes `features/users`.

### 7.2 Routing

Keep `HashRouter` for launch. It works on any static host with no rewrite rule, and it is what the code has been tested with. Route table moves verbatim from `App.jsx:278-306` into `app/AppRoutes.jsx`. `paths.js` stays the single source of URL strings; no component builds a path by string concatenation. Switching to `BrowserRouter` is item L5 and needs only a `public/_redirects` file containing `/* /index.html 200`.

Role guard stays `ManagerOnly` (`App.jsx:39-43`). It is a UX convenience; the API enforces roles.

### 7.3 API client layer

`shared/api/httpClient.js` is today's `services/httpClient.js` with two additions: it reads the `X-Correlation-Id` response header and throws an `ApiError` (`shared/api/ApiError.js`) instead of a bare `Error` with ad-hoc properties. Token storage moves to `features/auth/tokenStorage.js` and `httpClient` takes a `getToken` function at module load so `shared/` does not import from `features/`.

```js
// shared/api/ApiError.js
export class ApiError extends Error {
  constructor(message, { status, payload, correlationId }) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.payload = payload;
    this.correlationId = correlationId;
  }
}
```

```js
// shared/api/httpClient.js (the changed part of request())
const correlationId = res.headers.get('X-Correlation-Id') || null;
if (!res.ok) {
  const message = payload?.message || payload?.error || `Request failed (${res.status})`;
  throw new ApiError(message, { status: res.status, payload, correlationId });
}
```

Feature API modules are plain objects of functions, one per endpoint, exactly like `courses.api.js` today. They contain no state and no React.

```js
// features/learn/learnApi.js
import { http } from '../../shared/api/httpClient';

export const learnApi = {
  getCourses: (agentId) => http.get('/learn/courses', { params: { agent_id: agentId } }),
  getCourseTree: (courseId, agentId) => http.get(`/learn/courses/${courseId}`, { params: { agent_id: agentId } }),
  resumeCourse: (courseId, agentId) => http.get(`/learn/courses/${courseId}/resume`, { params: { agent_id: agentId } }),
  completeItem: (itemId, courseId, agentId) => http.post('/learn/complete', { item_id: itemId, course_id: courseId, agent_id: agentId }),
  submitQuiz: (itemId, courseId, agentId, answers) => http.post('/learn/quiz/submit', { item_id: itemId, course_id: courseId, agent_id: agentId, answers }),
  recordView: (agentId, courseId, viewedItemId) => http.post('/learn/views', { agent_id: agentId, course_id: courseId, viewed_item_id: viewedItemId }),
  getQuizLock: (agentId, itemId) => http.get('/learn/quiz/lock', { params: { agent_id: agentId, item_id: itemId } })
};
```

`resetQuizLock` is gone (B6).

### 7.4 Data fetching, caching, loading and error states

No query library. At 100 users and screens that each make one or two calls, the hand-rolled pattern is correct; what is wrong is that it is hand-rolled seven times. One hook replaces all of them:

```js
// shared/hooks/useAsyncData.js
import { useCallback, useEffect, useState } from 'react';
import { reportError } from '../logging/logger';

export function useAsyncData(load, deps) {
  const [state, setState] = useState({ data: null, error: null, loading: true });
  const [attempt, setAttempt] = useState(0);
  const reload = useCallback(() => setAttempt((n) => n + 1), []);

  useEffect(() => {
    let cancelled = false;
    setState((s) => ({ ...s, loading: true, error: null }));
    load()
      .then((data) => { if (!cancelled) setState({ data, error: null, loading: false }); })
      .catch((error) => {
        reportError(error);
        if (!cancelled) setState((s) => ({ ...s, error, loading: false }));
      });
    return () => { cancelled = true; };
  }, [...deps, attempt]); // eslint-disable-line react-hooks/exhaustive-deps

  return { ...state, reload };
}
```

Rendering the three states is done by two shared components, not by inline ternaries in every screen:

```jsx
// shared/components/ErrorBanner.jsx
import styles from './ErrorBanner.module.css';

export function ErrorBanner({ error, onRetry }) {
  if (!error) return null;
  return (
    <div role="alert" className={styles.banner}>
      <p className={styles.message}>{error.message}</p>
      {error.correlationId && (
        <p className={styles.reference}>Reference: {error.correlationId}</p>
      )}
      {onRetry && <button type="button" className={styles.retry} onClick={onRetry}>Retry</button>}
    </div>
  );
}
```

The reference line is the piece that makes section 12 work: a user reads it out, and the maintainer searches Railway logs for it.

Caching: none. The one place a cache would matter (the sidebar and the course viewer both fetching the same tree, `Sidebar.jsx:110-123` and `CourseViewer.jsx:44`) is solved by lifting `useCourseTree` into `CoursePage.jsx` and passing the result down, not by adding a cache.

### 7.5 Forms, auth state, route guards

- Forms are controlled inputs with `useState`, submitted through an `async` handler that sets a `submitting` flag and a form-local error, exactly as `UserManagement.jsx` does. Errors raised by a modal render inside the modal (`FormError` in `UserManagement.jsx:167-177`). No form library.
- Validation rules that the server also enforces (password length, question option shape) live in one plain module per feature (`passwordRules.js`) so the UI and the error message agree.
- Auth state is `features/auth/AuthProvider.jsx`, today's `AuthContext.jsx` unchanged in behaviour: restore session from the stored token via `GET /api/me`, listen for the `auth:unauthorized` event, expose `user`, `isManager`, `login`, `logout`.
- Remembered email (W16): on a successful login `LoginForm` writes the email to `localStorage` under `wmh_last_email`; on mount it reads it, pre-fills the field, and focuses the password field instead. The password is never stored by the app; the browser's own password manager fills it because the inputs already declare `autocomplete="email"` and `autocomplete="current-password"`. On a shared office PC the browser prompt "save password?" is the agent's choice, not the app's.
- Route guards: `ManagerOnly` for manager routes. Agents deep-linking into `/manager/*` are redirected to `/learn` (`App.jsx:41`).

### 7.6 Styling: CSS Modules plus a token file

This is the hard requirement that the current code does not meet in spirit. The prescription:

1. **Tokens.** `shared/styles/tokens.css` declares every colour, spacing step, radius, and font as a CSS custom property on `:root`, with dark-mode overrides under `html.dark` (the class `ThemeProvider` already toggles, `ThemeContext.jsx:12-20`). Values come from `tailwind.config.js` and from the inlined literals.
2. **CSS Modules.** Each component has a co-located `<Component>.module.css`. Class names are semantic (`.card`, `.cardTitle`, `.progressFill`), not descriptive of appearance. Every declaration uses a token; a raw hex value in a module is a review failure.
3. **Dynamic values only inline.** `style={{ width: \`${pct}%\` }}` for the progress bar and `style={{ width: sidebarWidth }}` for the sidebar are the only permitted inline styles. The login background image becomes a CSS `background-image: url(...)` in `LoginForm.module.css`, importing the asset via the module.
4. **Tailwind is removed at the end.** During the migration both coexist. When the last `className="..."` utility string is gone, delete `tailwind.config.js`, `postcss.config.js`, the three `@tailwind` directives, and the `tailwindcss` / `autoprefixer` / `postcss` dev dependencies.

**Token file** (`shared/styles/tokens.css`):

```css
:root {
  /* surfaces and text */
  --color-page: #f7f8ed;            /* the literal inlined 77 times today */
  --color-surface: #ffffff;
  --color-surface-muted: #f4f4f5;   /* zinc-100 */
  --color-border: #e4e4e7;          /* zinc-200 */
  --color-border-strong: #d4d4d8;   /* zinc-300 */
  --color-text: #18181b;            /* zinc-900 */
  --color-text-muted: #71717a;      /* zinc-500 */
  --color-text-faint: #a1a1aa;      /* zinc-400 */

  /* brand */
  --color-brand: #3ed676;           /* wmh.green */
  --color-accent: #4ade80;          /* watermelon.green.400 */
  --color-accent-strong: #22c55e;   /* watermelon.green.500 */
  --color-accent-soft: #dcfce7;     /* watermelon.green.100 */
  --color-danger: #f43f5e;          /* watermelon.red.500 */
  --color-danger-soft: #ffe4e6;     /* watermelon.red.100 */
  --color-danger-text: #9f1239;     /* watermelon.red.800 */
  --color-back-button: #fc1e1d;     /* Sidebar.jsx:216 */

  /* status pills (Badge.jsx) */
  --color-status-done: #4ade80;
  --color-status-active: #34d399;
  --color-status-off: #f87171;

  /* spacing (Tailwind scale, 4px base) */
  --space-1: 0.25rem;  --space-2: 0.5rem;  --space-3: 0.75rem;  --space-4: 1rem;
  --space-5: 1.25rem;  --space-6: 1.5rem;  --space-8: 2rem;     --space-10: 2.5rem;

  /* radius (tailwind.config.js borderRadius) */
  --radius-sm: 4px;  --radius-md: 6px;  --radius-lg: 8px;  --radius-xl: 10px;  --radius-full: 9999px;

  /* type */
  --font-sans: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
  --text-xs: 0.75rem;  --text-sm: 0.875rem;  --text-base: 1rem;  --text-lg: 1.125rem;
  --text-xl: 1.25rem;  --text-2xl: 1.5rem;
  --weight-medium: 500;  --weight-bold: 700;  --weight-black: 900;

  --sidebar-width-default: 320px;
}

html.dark {
  --color-page: #2f3136;            /* zinc-950 in tailwind.config.js */
  --color-surface: #36393f;         /* zinc-900 */
  --color-surface-muted: #3f4248;   /* zinc-800 */
  --color-border: #4e5058;          /* zinc-700 */
  --color-border-strong: #52525b;
  --color-text: #f4f4f5;
  --color-text-muted: #a1a1aa;
  --color-text-faint: #71717a;
  --color-accent-soft: #052e16;
  --color-danger-soft: #4c0519;
  --color-danger-text: #fecdd3;
  --color-status-done: #16a34a;
  --color-status-active: #059669;
  --color-status-off: #dc2626;
}
```

**Migration recipe, per component** (this is how existing styles move):

1. Create `<Component>.module.css` next to the component and `import styles from './<Component>.module.css'`.
2. For each element with a utility string, pick a semantic class name and write its declarations in the module using tokens. Translate mechanically: `bg-[#F7F8ED]` becomes `background: var(--color-page)`; `text-zinc-500 dark:text-zinc-400` becomes `color: var(--color-text-muted)` (the dark value is handled by the token); `rounded-lg` becomes `border-radius: var(--radius-lg)`; `px-8 pt-5 pb-6` becomes `padding: var(--space-5) var(--space-8) var(--space-6)`.
3. Conditional classes use a tiny join: `className={[styles.card, isDone && styles.cardDone].filter(Boolean).join(' ')}`. No `clsx` dependency needed.
4. Hover, focus, and responsive rules go in the module (`.card:hover`, `@media (min-width: 1024px)`), replacing `hover:` and `lg:` prefixes.
5. Run the screen in both themes. Delete the utility strings. Commit that one component.

Order: `shared/components` first (Button, Badge, Modal, Select, EmptyState, ErrorBanner), because every screen uses them; then `LoginForm`; then one feature at a time, starting with whichever is next to be touched for another reason. Estimate: half a day for shared components, one day per feature, three to five days total. This is item L1 and it starts after launch.

**Example: a feature component with its co-located module** (the course card from `AgentDashboard.jsx:169-270`, reduced to its shape):

```jsx
// features/learn/CourseCard.jsx
import { Badge } from '../../shared/components/Badge';
import { Button } from '../../shared/components/Button';
import styles from './CourseCard.module.css';

const STATE_LABEL = { completed: 'COMPLETED', in_progress: 'IN PROGRESS', not_started: 'NOT STARTED' };

export function CourseCard({ course, onLaunch, onDownloadCertificate }) {
  const state = course.assignment_status || 'not_started';
  const done = course.completed_item_ids?.length || 0;
  const total = course.total_items || 0;
  const pct = course.progress || 0;

  return (
    <article className={[styles.card, styles[state]].join(' ')}>
      <Badge variant={state}>{STATE_LABEL[state]}</Badge>
      <div className={styles.body}>
        <div className={styles.summary}>
          <h3 className={styles.title}>{course.title}</h3>
          <p className={styles.description}>{course.description}</p>
        </div>
        <div className={styles.progress}>
          <div className={styles.progressHeader}>
            <span>Progress</span>
            <span className={styles.progressValue}>{pct}%</span>
          </div>
          <div className={styles.progressTrack} role="progressbar" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100}>
            <div className={styles.progressFill} style={{ width: `${pct}%` }} />
          </div>
          <p className={styles.progressCaption}>{done} of {total} modules</p>
        </div>
        <div className={styles.actions}>
          <Button variant="secondary" onClick={() => onLaunch(course.id)}>
            {state === 'completed' ? 'Review' : state === 'not_started' ? 'Start Course' : 'Resume'}
          </Button>
          {state === 'completed' && (
            <Button variant="secondary" onClick={() => onDownloadCertificate(course)}>Certificate</Button>
          )}
        </div>
      </div>
    </article>
  );
}
```

```css
/* features/learn/CourseCard.module.css */
.card {
  background: var(--color-page);
  border: 1.5px solid var(--color-border);
  border-top: 4px solid var(--color-border-strong);
  border-radius: var(--radius-xl);
  padding: var(--space-5) var(--space-8) var(--space-6);
  display: grid;
  gap: var(--space-4);
}
.card.in_progress { border-top-color: var(--color-accent); }
.card.completed   { border-top-color: var(--color-accent-strong); }

.body { display: grid; gap: var(--space-6); align-items: center; }
@media (min-width: 1024px) { .body { grid-template-columns: 6fr 3fr 3fr; } }

.title { font-size: var(--text-xl); font-weight: var(--weight-black); color: var(--color-text); line-height: 1.375; }
.description {
  font-size: var(--text-sm); color: var(--color-text-muted); line-height: 1.625;
  display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden;
}

.progressHeader {
  display: flex; justify-content: space-between;
  font-size: var(--text-xs); font-weight: var(--weight-bold); text-transform: uppercase; color: var(--color-text-faint);
}
.progressValue { color: var(--color-accent-strong); font-size: var(--text-sm); font-variant-numeric: tabular-nums; }
.progressTrack { height: 0.625rem; background: var(--color-surface-muted); border-radius: var(--radius-full); overflow: hidden; }
.progressFill  { height: 100%; background: var(--color-accent-strong); transition: width 500ms; }
.progressCaption { font-size: var(--text-xs); color: var(--color-text-faint); }

.actions { display: flex; flex-direction: column; gap: var(--space-2); align-items: center; }
@media (min-width: 1024px) { .actions { align-items: flex-end; } }
```

**Example: a page** (`pages/agent/DashboardPage.jsx`):

```jsx
import { useNavigate, useOutletContext } from 'react-router-dom';
import { AgentDashboard } from '../../features/learn/AgentDashboard';
import { paths } from '../../app/paths';

export function DashboardPage() {
  const navigate = useNavigate();
  const { progressRefreshKey } = useOutletContext();
  return (
    <AgentDashboard
      onLaunchCourse={(courseId) => navigate(paths.course(courseId))}
      refreshTrigger={progressRefreshKey}
    />
  );
}
```

### 7.7 Readability rules

- Names carry the meaning; comments say why, never what. `CourseViewer.jsx:119-122` ("The server owns the quiz review gate, so the local mirror is only credited once the server has actually recorded the view") is the model. `// Swap` above a swap (`CourseEditor.jsx:140`) is the anti-model and gets deleted.
- A component over 250 lines is split by extracting the sub-tree with the most local state (the quiz question fieldset, the enrol-agents modal, the section card). Section 5.2 names the extractions.
- Derived values are computed once with a name (`const isDone = state === 'completed'`), not inline in JSX three times.
- Handlers are named `handle<Event>`; props that accept handlers are named `on<Event>`. The code already does this.
- ESLint (WEEK-2): `eslint`, `eslint-plugin-react`, `eslint-plugin-react-hooks`, flat config, with `react-hooks/exhaustive-deps` as an error. It will flag the effects in `CourseViewer.jsx:71-73` and `CoursesList.jsx:46-48` that list a subset of their dependencies; `useAsyncData` resolves those.

---

## 8. Data layer

### 8.1 Provider switch: SQLite to PostgreSQL

Concrete steps (they are also items 2-4 of the execution plan):

1. In `WmhLms.Data.csproj` replace `Microsoft.EntityFrameworkCore.Sqlite` with `Npgsql.EntityFrameworkCore.PostgreSQL` version `8.0.*`. Move `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` from the Api project to Data. Keep the Sqlite package in `WmhLms.Tests` only.
2. Rename the connection string key from `Sqlite` to `Default` everywhere: `appsettings.json`, `appsettings.Development.json`, `Dockerfile`, `Program.cs:49`.
3. Replace `o.UseSqlite(conn)` with `o.UseNpgsql(conn)` inside `AddDataAccess`.
4. Delete `AppDbContext.cs:32` (`UseCollation("NOCASE")`). See 8.2.
5. Delete the three files in `WmhLms.Data/Migrations/`. Nothing needs to survive (brief), and SQLite migrations cannot be replayed on Postgres.
6. Start local Postgres (`docker compose up -d db`, section 10.3) and create the fresh migration:

```bash
dotnet ef migrations add Initial --project WmhLms.Data --startup-project WmhLms.Api
```

7. Open the generated `Initial.cs`, confirm the column types are `bigint` / `text` / `character varying(n)` / `timestamp with time zone` / `boolean`, then append the raw SQL from 8.5 at the end of `Up` and its inverse in `Down`.
8. Run the API. `MigrateAsync` applies it. Log in with the bootstrap root account (8.4).

Two Npgsql behaviours to know:

- **`DateTime` must be UTC.** Npgsql maps `DateTime` to `timestamp with time zone` and throws on a value whose `Kind` is `Local` or `Unspecified`. The code is already clean: every write uses `DateTime.UtcNow` or `time.GetUtcNow().UtcDateTime` (`LearnService.cs:144, 156`, `UserServices.cs:115`, `AssignmentService.cs:26`), and the seed uses `DateTimeKind.Utc`. Keep it that way; a `new DateTime(...)` without a kind in a future service is the classic first Postgres bug.
- **Serialised timestamps gain a `Z` suffix.** SQLite stored them as text without a zone; the frontend's `new Date(...)` calls handle both. No change needed.

### 8.2 Case-insensitive email without a collation

Replace the SQLite collation with normalisation on write: `Guard.Email` returns `email.ToLowerInvariant()`, so `Users.Email` always holds lower case and the existing plain unique index (`IX_Users_Email`) is effectively case-insensitive. Look-ups then compare directly (`u.Email == normalisedEmail`) instead of `ToLower()` on both sides (`UserServices.cs:29-30, 102, 144`). One line why: no Postgres extension to enable, no `citext` mapping, and the SQLite unit tests keep working unchanged. The alternative, `citext`, would be right only if emails had to be displayed with the case the user typed; nothing in the UI needs that.

The `RootManagerBootstrap` and `DemoSeed` lower-case their emails through the same `Guard.Email` call.

### 8.3 Supabase connection: session pooler, and why

Supabase exposes three ways in. Use the **Supavisor session-mode pooler on port 5432** through the pooler hostname.

| Option | Host | Port | Verdict |
|---|---|---|---|
| Direct | `db.<ref>.supabase.co` | 5432 | IPv6-only unless the IPv4 add-on is bought. Railway egress is not reliably IPv6. Do not use. |
| Session pooler | `aws-0-<region>.pooler.supabase.com` | 5432 | IPv4. Each client connection holds one server connection for its lifetime. Full Postgres semantics: prepared statements, advisory locks, `SET`, migrations all work. **Use this.** |
| Transaction pooler | same host | 6543 | IPv4. Server connection is borrowed per transaction. Breaks prepared statements and session state. Only worth it for thousands of short-lived clients (serverless). Not this app. |

Why session mode is the right one here: there is exactly one long-lived process (one Railway container) with its own Npgsql pool. Session mode makes Supavisor almost transparent, and the failure mode the brief warns about (intermittent errors under load from prepared statements in transaction mode) cannot occur.

**Connection string shape** (this is the value of `ConnectionStrings__Default` on Railway):

```
Host=aws-0-<region>.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<project-ref>;Password=<db-password>;SSL Mode=Require;Trust Server Certificate=true;Maximum Pool Size=10;Minimum Pool Size=1;Timeout=15;Command Timeout=30;Keepalive=30
```

The office is in Morocco, so the project is created in Supabase's **West EU (Paris)** region, `eu-west-3`, and the host is `aws-0-eu-west-3.pooler.supabase.com`. Railway's matching region is **EU West (Amsterdam)**. Both are one hop from Morocco; Frankfurt would also be fine.

Each setting, and why:

- `Username=postgres.<project-ref>`: the pooler needs the project reference suffix; the direct connection uses plain `postgres`. Supabase shows the exact string under Project Settings, Database, Connection string, "Session pooler".
- `SSL Mode=Require;Trust Server Certificate=true`: encrypts the connection without validating Supabase's certificate chain. Npgsql 6+ validates the certificate under `Require` unless told not to. Item L8 replaces this with `SSL Mode=VerifyFull;Root Certificate=/app/supabase-ca.crt` after downloading the CA from the Supabase dashboard and copying it into the image.
- `Maximum Pool Size=10`: Supabase's default pooler `pool_size` is 15 per database user. Session mode means every open Npgsql connection is a held server connection, so the app's pool must stay under 15 with headroom for the Supabase dashboard and a `psql` session. Ten connections serve far more than 60 concurrent users whose requests take tens of milliseconds.
- `Timeout=15`: fail a connection attempt after 15 seconds instead of hanging a request.
- `Keepalive=30`: sends a TCP keepalive so idle pooled connections are not silently dropped by the pooler.

**If transaction mode (port 6543) is ever forced**, the string must add `No Reset On Close=true` and keep `Max Auto Prepare=0` (the default), and migrations must be run from a laptop against the session pooler rather than at boot. That is the "exact Npgsql setting" the brief asks about. Do not go there without a reason.

Local development uses the Docker Postgres from section 10.3: `Host=localhost;Port=5432;Database=wmhlms;Username=wmhlms;Password=wmhlms`.

### 8.4 Seeding: root manager (always) and demo content (Development only)

Two seeds with different lifecycles, both in `WmhLms.Core/Seeding/`:

**`RootManagerBootstrap`** runs on every boot in every environment, after migrations. It is idempotent and uses no explicit ids.

```csharp
public sealed class RootManagerBootstrap(
    IUserRepository users, IPasswordHasher passwords, IUnitOfWork unitOfWork,
    IOptions<RootManagerOptions> options, TimeProvider clock, ILogger<RootManagerBootstrap> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Email) || string.IsNullOrWhiteSpace(o.Password))
            throw new InvalidOperationException(
                "RootManager:Email and RootManager:Password must be configured.");

        var email = Guard.Email(o.Email);
        if (await users.GetByEmailAsync(email, ct) is not null) return;

        users.Add(User.Create(o.FirstName, o.LastName, email,
            passwords.Hash(Guard.Password(o.Password)), UserRole.Manager,
            clock.GetUtcNow().UtcDateTime, isRoot: true));
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogWarning("Root manager {Email} created from configuration.", email);
    }
}
```

The password is only used when the account does not exist yet. Changing `RootManager__Password` later does nothing; the root user changes their password through the normal UI (which today is impossible because root cannot be edited, see open question Q4). `Program.cs` calls `IDatabaseInitializer.MigrateAsync()` then `RootManagerBootstrap.RunAsync()`, and refuses to start if either throws.

**`DemoSeed`** keeps its current content minus the root user block and minus every explicit `Id =`. It runs only when `Seed:DemoContent` is true, which stays `false` in `appsettings.json` and `true` in `appsettings.Development.json`. Its demo agent gets a fake address (`agent@wmh.local`) rather than a real employee's.

Reference data: there is none. Roles, statuses, item types, and question types are string constants in code, not lookup tables. This matches how the entities are configured today (plain `string` columns with `HasMaxLength`), so the brief's "seeded reference data" does not exist and nothing needs seeding beyond the root manager.

### 8.5 Root protection at the database level

Service-layer checks exist and are tested (`UserServiceTests.cs:54-65`). The database adds a second layer so no future code path, direct SQL, or migration can remove or demote the root account. Append this to the `Up` method of the initial migration:

```csharp
migrationBuilder.Sql("""
    CREATE UNIQUE INDEX "UX_Users_SingleRoot" ON "Users" ("IsRoot") WHERE "IsRoot";

    CREATE OR REPLACE FUNCTION protect_root_user() RETURNS trigger AS $$
    BEGIN
        IF TG_OP = 'DELETE' THEN
            IF OLD."IsRoot" THEN
                RAISE EXCEPTION 'The root manager account cannot be deleted.';
            END IF;
            RETURN OLD;
        END IF;
        IF OLD."IsRoot" AND (NOT NEW."IsRoot" OR NEW."Role" <> 'manager' OR NEW."Status" <> 'active') THEN
            RAISE EXCEPTION 'The root manager account cannot be demoted, disabled, or un-rooted.';
        END IF;
        RETURN NEW;
    END;
    $$ LANGUAGE plpgsql;

    CREATE TRIGGER trg_protect_root_user
        BEFORE UPDATE OR DELETE ON "Users"
        FOR EACH ROW EXECUTE FUNCTION protect_root_user();
    """);
```

And in `Down`:

```csharp
migrationBuilder.Sql("""
    DROP TRIGGER IF EXISTS trg_protect_root_user ON "Users";
    DROP FUNCTION IF EXISTS protect_root_user();
    DROP INDEX IF EXISTS "UX_Users_SingleRoot";
    """);
```

The partial unique index guarantees there is never more than one root. The trigger guarantees the one root stays a manager, stays active, and cannot be deleted. `UserService` still checks first so the user sees a friendly 403 instead of a 500, and `UnitOfWork` does not need to translate this exception because the service never lets the write reach the database.

The SQLite unit-test database (`TestDb.cs`) does not run migrations, so it does not get the trigger; that is fine, because the unit tests cover the service checks and the integration tests (13.2) run against real Postgres.

### 8.6 Entity configuration and schema decisions

- One `IEntityTypeConfiguration<T>` per entity in `WmhLms.Data/Configurations/`; `AppDbContext.OnModelCreating` becomes `b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly)`. Content is today's `OnModelCreating` split by entity; the FK, cascade, and index declarations do not change.
- `Assignment.CompletedItemIdsJson` stays a `text` column holding a JSON array for launch. The reconciliation logic in `LearnService.Reconcile` makes it safe, and it is tested. Item L3 replaces it with an `ItemCompletions(AssignmentId, ItemId, CompletedAt)` table if per-item completion dates are ever needed for reporting. Do not do this before launch; it changes the one piece of logic with the most tests behind it.
- `QuizLock.LastResultJson` and `ReviewedItemIdsJson` likewise stay `text`.
- Column types: let Npgsql choose. `string` with `HasMaxLength` becomes `character varying(n)`; `long` becomes `bigint` identity; `DateTime` becomes `timestamp with time zone`. Do not add `HasColumnType` calls; they would break the SQLite test database.

### 8.7 Indexes the current queries need

The index set in the existing migration (listed in 2.2) already covers every query in the services:

| Query | Index used |
|---|---|
| `Users` by email (login, uniqueness) | `IX_Users_Email` |
| `Users` list with role/status/search | table scan; at 100 rows an index would not be chosen anyway |
| `Assignments` by `AgentId` (dashboard, user assignments, delete user) | `IX_Assignments_AgentId` |
| `Assignments` by `CourseId` (cohort view, delete course) and by `(CourseId, AgentId)` | `IX_Assignments_CourseId_AgentId` (unique; serves `CourseId` alone as a prefix) |
| `QuizLocks` by `(AgentId, QuizItemId)` | `IX_QuizLocks_AgentId_QuizItemId` (unique) |
| `QuizLocks` by `(AgentId, CourseId)` (record view) | `IX_QuizLocks_AgentId_QuizItemId` prefix on `AgentId`, then filter; a handful of rows per agent |
| `QuizLocks` by `QuizItemId` / `CourseId` (deletes) | `IX_QuizLocks_QuizItemId`, `IX_QuizLocks_CourseId` |
| course tree loads | `IX_Sections_CourseId`, `IX_Items_SectionId`, `IX_Questions_ItemId`, `IX_Options_QuestionId` |
| `AuditEntries` recent | primary key order |

No new indexes are needed. The only addition is the single-root partial index from 8.5, which is a constraint, not a performance index.

### 8.8 Migrations workflow

**Create.** After changing an entity or configuration:

```bash
dotnet ef migrations add <DescriptiveName> --project WmhLms.Data --startup-project WmhLms.Api
```

**Review.** Open the generated `<timestamp>_<Name>.cs` and read `Up`. Check for: a `DropColumn` or `DropTable` you did not intend (EF generates these when a property is renamed rather than altered); a new non-nullable column without a default on a table with rows (fails on apply); any `AlterColumn` that changes a type. If the migration is not what you meant, run `dotnet ef migrations remove` and fix the model.

**Apply locally.** Run the API; `MigrateAsync` at boot applies pending migrations. Or explicitly:

```bash
dotnet ef database update --project WmhLms.Data --startup-project WmhLms.Api
```

**Apply in production.** Commit the migration with the code that needs it. Railway builds and starts the new container; `MigrateAsync` runs before the app accepts traffic. This is safe because there is exactly one instance; never scale the Railway service above one replica while migrations run at boot.

**Roll back.** Schema rolls forward only. If a deploy is bad, roll back the code (section 11.5) and, if the bad migration must be undone, write a new migration that reverses it. `dotnet ef database update <PreviousMigration>` runs `Down` and is fine locally, but do not run it against production from a laptop unless the app is stopped.

**Never** edit a migration that has already run in production. Add a new one.

---

## 9. Security checklist

### 9.1 Auth mechanism: decision

**Keep the JWT bearer token in the `Authorization` header, stored in `localStorage`, with a fixed lifetime and no refresh token.** No cookie is set by the API, so no cookie attributes apply.

Why this and not httpOnly cookies: it is what exists, it is tested, and its one real weakness (a script running in the page could read the token) is mitigated by the facts that the app renders no user-supplied HTML (no `dangerouslySetInnerHTML`, text items are rendered as text by React) and that every request re-checks the account against the database (`Program.cs:98-123`), so a disabled or deleted account is locked out within one request even while its token is unexpired. A cookie design would require rewriting `AuthContext`, `httpClient`, the JWT setup, and adding CSRF thinking, for a gain this app does not need. The one condition that would flip the decision: a written company policy that tokens must not be readable by page scripts.

Lifetime: set `Jwt__ExpiryMinutes=720` (12 hours) in production. It covers a shift; an agent whose token expires mid-course is returned to the login screen with all progress already saved server-side.

**CORS configuration required** (`Startup/CorsSetup.cs`, replacing `Program.cs:160-171`):

```csharp
services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(origins)                                   // ["https://app.<domain>"]
    .WithHeaders("Authorization", "Content-Type", "X-Correlation-Id")
    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
    .WithExposedHeaders("X-Correlation-Id", "Retry-After")));
```

`WithExposedHeaders` is the line that is missing today: without it the browser hides `X-Correlation-Id` from `httpClient.js`, and section 7.4's error reference never appears. `AllowCredentials()` is deliberately absent; there are no cookies. `origins` continues to come from `Cors:Origins` and the startup still throws outside Development when it is empty.

### 9.2 Checklist

| Control | Status today | Action |
|---|---|---|
| Password hashing | ASP.NET Identity `PasswordHasher<User>` (PBKDF2, per-user salt), `PasswordService.cs` | None. Never log or return `PasswordHash`; `UserDto` already omits it |
| Password policy | Minimum 8, maximum 256, enforced server-side (`Guard.Password`) | None for launch |
| Authorization on every endpoint | Fallback policy requires authentication; manager controllers carry `[Authorize(Roles="manager")]`; root rules in `UserService` | None. Keep the fallback policy; it is the reason a forgotten attribute fails closed |
| Client-supplied role never trusted | Role from token claim, re-checked per request against `Users.Role` | Confirmed. `CreateUserRequest.Role` is validated against the caller's rights, not trusted |
| Client-supplied identity never trusted | `ResolveAgentId` (`Base.cs:31-38`), 403 on mismatch | Confirmed |
| Enrolment required for learn reads/writes | `LearnService.RequireAssignmentAsync` | Confirmed |
| Answer key not exposed to learners | **Leaks** via `OptionDto.IsCorrect` | **B6:** `LearnMapper` emits `LearnOptionDto(Id, Text)`; `GetCourseTreeAsync` and `GetCoursesAsync` use it. Delete `?autofill`, `handleResetTimer`, `resetQuizLock`. Integration test 13.2 (7) guards the regression |
| Sequential gating server-side | UI only | **W9:** in `CourseProgressService.CompleteItemAsync`, reject with 400 if any earlier item in `Flat(course)` is not in the reconciled done list. Quiz submit gets the same check |
| CORS locked to the real origin | Config-driven, throws if empty in production | Set `Cors__Origins__0=https://app.<domain>`; add exposed headers (9.1) |
| Secrets via environment variables | `Jwt:Key` required from env in production; DB string from config | Full inventory in section 10. No secret is in a tracked file today except the dev seed passwords, which W13 replaces |
| HTTPS enforcement | `UseHttpsRedirection` + HSTS (365 days) outside Development, behind `UseForwardedHeaders` | Gate both behind `Security:RequireHttps` (default `true`) instead of `!isDevelopment`, so the local production run (11.6) can switch them off. Railway terminates TLS and sets `X-Forwarded-Proto`. `KnownNetworks.Clear()` (`Program.cs:68-69`) trusts any proxy, which is acceptable because only Railway's edge can reach the container |
| Rate limiting on auth | Fixed window per IP, 10/min | **B5:** `RateLimit__LoginAttempts=100`. **WEEK-2:** add a per-account failed-login counter (`FailedLoginTracker`, singleton `ConcurrentDictionary<string,(int count, DateTime windowStart)>`, 10 failures per email per 15 minutes returns 429) so one shared office IP does not weaken protection of individual accounts |
| Security headers on API responses | `SecurityHeadersMiddleware` | None |
| Error responses hide internals | `ExceptionHandlingMiddleware` | None |
| Dev endpoints in production | `DevController` returns 404 outside Development | Fine, but delete `DevController` in W1 since it is the last controller touching `AppDbContext`; recreate the reset as a `dotnet run --reset-demo` switch if it is ever missed |
| File upload validation | **No uploads exist** | Nothing to do. If uploads are ever added, this row becomes real |
| Dependency vulnerabilities | Frontend CI runs `npm audit --audit-level=high --omit=dev` | Add `dotnet list package --vulnerable` to backend CI (one line) |
| Root account protection | Service layer | **B13:** database trigger and single-root index (8.5) |
| Account revalidation cost | One `Users` query per request | Fine at this scale; do not cache it, the immediacy is the point |
| Logging of credentials | None found; `AuditEntry.Detail` never carries passwords | Keep it that way |
| Source maps | `sourcemap: 'hidden'`: generated, not referenced | Upload to Sentry in W6 or leave them out of the deploy |

---

## 10. Configuration and environments

There are exactly two environments: **local** and **production**. There is no staging. A solo maintainer cannot keep three environments honest, and Cloudflare Pages preview deployments cover "does this branch build".

### 10.1 Backend configuration keys

Configuration is read by ASP.NET's default providers: `appsettings.json`, then `appsettings.{Environment}.json`, then environment variables (double underscore `__` maps to the `:` separator; matching is case-insensitive).

| Key | Local (where) | Production (Railway variable) | Required in prod | Notes |
|---|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Development` (`launchSettings.json`) | `Production` (set in Dockerfile) | yes | Controls seeding, error detail, HTTPS redirect, dev endpoints |
| `ASPNETCORE_URLS` | `http://localhost:5000` (Program default) | `http://+:8080` (Dockerfile) | yes | |
| `PORT` | n/a | `8080` | yes | Tells Railway's proxy which container port to route to |
| `ConnectionStrings__Default` | `appsettings.Development.json` (Docker Postgres) | Supabase session pooler string (8.3) | yes | |
| `Jwt__Key` | unset; ephemeral random key per run (`Program.cs:234`) | `openssl rand -base64 48` output | yes | At least 32 bytes; startup refuses shorter |
| `Jwt__Issuer` | `wmh-lms` (`appsettings.json`) | leave default | no | |
| `Jwt__Audience` | `wmh-lms-frontend` (`appsettings.json`) | leave default | no | |
| `Jwt__ExpiryMinutes` | `1440` (`appsettings.json`) | `720` | no | 9.1 |
| `Cors__Origins__0` | `http://localhost:5173` (`appsettings.json`) | `https://app.<domain>` | yes | Startup throws without it |
| `Security__RequireHttps` | unset (`true`); the local production compose sets `false` | leave unset (`true`) | no | When `false`, HSTS and HTTPS redirection are skipped so the Production image can run on a laptop over plain http (11.6). Never `false` on Railway |
| `RootManager__Email` | `root@wmh.local` (`appsettings.Development.json`) | `ayoub.khamil@watermelon-hub.com` | yes | 8.4, Q14 |
| `RootManager__Password` | `dev-root-password` (`appsettings.Development.json`) | strong, generated once, stored in a password manager; **must not** be the `ayoub1234` from the committed dev config | yes | Only used on first creation |
| `RootManager__FirstName`, `RootManager__LastName` | dev values | real values | yes | |
| `Seed__DemoContent` | `true` (`appsettings.Development.json`) | unset (defaults to `false`) | no | Never `true` in production |
| `RateLimit__LoginAttempts` | `200` (`appsettings.Development.json`) | `100` | yes | B5 |
| `RateLimit__WindowMinutes` | `1` | leave default | no | |
| `Quiz__CooldownMinutes` | `5` (`appsettings.json`) | leave default unless the owner decides otherwise (Q11) | no | |
| `Sentry__Dsn` | unset | Sentry project DSN | no | W6 |
| `Logging__LogLevel__Default` | `Information` | `Information` | no | Drop to `Warning` only if log volume ever becomes a cost |

Retired keys: `ConnectionStrings:Sqlite`, `Seed:ManagerEmail`, `Seed:ManagerPassword`, `Seed:AgentEmail`, `Seed:AgentPassword`.

### 10.2 Frontend configuration

Vite bakes `VITE_*` variables into the bundle at build time. They are not secrets and cannot hold secrets.

| Variable | Local (`.env`, untracked) | Production (Cloudflare Pages, Settings, Environment variables, Production) |
|---|---|---|
| `VITE_API_BASE_URL` | `http://localhost:5000/api` | `https://api.<domain>/api` |
| `NODE_VERSION` | n/a (local Node 24 works) | `20` |
| `VITE_SENTRY_DSN` | unset | Sentry browser DSN (W6) |

Commit a `.env.example` containing the local value so the next machine set-up is copy-and-rename.

### 10.3 Local Docker Postgres

`docker-compose.yml` in the backend repo has two services. `db` is used every day; the API runs from `dotnet run` (or the IDE) against it, because containerising the API for normal development only slows the edit-run loop. `api` sits behind the `prod` profile and is only started for the local production run described in 11.6.

```yaml
services:
  db:
    image: postgres:17-alpine
    container_name: wmhlms-db
    environment:
      POSTGRES_DB: wmhlms
      POSTGRES_USER: wmhlms
      POSTGRES_PASSWORD: wmhlms
    ports:
      - "5432:5432"
    volumes:
      - wmhlms-pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U wmhlms -d wmhlms"]
      interval: 5s
      timeout: 3s
      retries: 10

  api:
    profiles: ["prod"]
    build: .
    container_name: wmhlms-api
    depends_on:
      db:
        condition: service_healthy
    env_file: .env.production.local
    environment:
      ConnectionStrings__Default: Host=db;Port=5432;Database=wmhlms;Username=wmhlms;Password=wmhlms
      Cors__Origins__0: http://localhost:4173
      Security__RequireHttps: "false"
      PORT: "8080"
    ports:
      - "8080:8080"

volumes:
  wmhlms-pgdata:
```

Match the major version to the Supabase project (Supabase shows it under Project Settings, Infrastructure; new projects are on 17). Commands:

```bash
docker compose up -d db
```

```bash
docker compose down -v
```

The second one wipes the local database; the next `dotnet run` migrates and re-seeds. `appsettings.Development.json` gets `"ConnectionStrings": { "Default": "Host=localhost;Port=5432;Database=wmhlms;Username=wmhlms;Password=wmhlms" }`.

The backend `.gitignore` gains `.env.*.local`. A tracked `.env.production.example` documents the file the `api` service reads:

```
Jwt__Key=replace-with-openssl-rand-base64-48-output
Jwt__ExpiryMinutes=720
RootManager__Email=ayoub.khamil@watermelon-hub.com
RootManager__Password=replace-me
RootManager__FirstName=Ayoub
RootManager__LastName=Khamil
RateLimit__LoginAttempts=100
```

### 10.4 GitHub Actions secrets

| Secret | Repo | Used by |
|---|---|---|
| `BACKUP_DATABASE_URL` | backend | `backup.yml`; the session-pooler URL in `postgresql://` form |
| `BACKUP_PASSPHRASE` | backend | `backup.yml`; encrypts the dump |

Nothing else in CI needs a secret. Railway and Cloudflare Pages pull from GitHub with their own integrations.

---

## 11. Deployment

### 11.0 Domain: decide by Thursday, or launch without one

The domain is not settled (Q1). Two layouts are on the table, and both work identically with this design because auth is a bearer token, not a cookie, so same-site is not a constraint:

| Layout | SPA origin | API origin | `Cors__Origins__0` | `VITE_API_BASE_URL` |
|---|---|---|---|---|
| Subdomain of the existing company domain | `https://learn.watermelon-hub.com` | `https://api-learn.watermelon-hub.com` | `https://learn.watermelon-hub.com` | `https://api-learn.watermelon-hub.com/api` |
| New domain | `https://app.<newdomain>` | `https://api.<newdomain>` | `https://app.<newdomain>` | `https://api.<newdomain>/api` |

Everywhere this document writes `app.<domain>` and `api.<domain>`, substitute the chosen pair.

**If no decision exists by Thursday, launch on the platform hostnames** and add the domain later. Railway gives `<service>.up.railway.app`; Cloudflare Pages gives `<project>.pages.dev`. Set `Cors__Origins__0=https://<project>.pages.dev` and `VITE_API_BASE_URL=https://<service>.up.railway.app/api`. Adding the real domain afterwards is two DNS records, one Railway variable change, one Pages variable change, and a Pages rebuild: about 30 minutes, no code change. Do not let the domain discussion delay Monday.

### 11.1 Order of first deploy

1. Supabase project (database exists before the API needs it).
2. Railway backend (API exists before the frontend is built with its URL).
3. Cloudflare Pages frontend.
4. DNS for both (or the platform hostnames, 11.0), then set `Cors__Origins__0` on Railway to the final SPA origin and redeploy.

### 11.2 Backend: Dockerfile and Railway

Replace the current `Dockerfile` with this one. Changes from today: no SQLite volume or env, no test run in the image, no `HEALTHCHECK` (the runtime image has no `wget`; Railway uses its own check), no `WmhLms.Tests` copy.

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY WmhLms.Api/WmhLms.Api.csproj WmhLms.Api/
COPY WmhLms.Core/WmhLms.Core.csproj WmhLms.Core/
COPY WmhLms.Data/WmhLms.Data.csproj WmhLms.Data/
RUN dotnet restore WmhLms.Api/WmhLms.Api.csproj
COPY WmhLms.Api/ WmhLms.Api/
COPY WmhLms.Core/ WmhLms.Core/
COPY WmhLms.Data/ WmhLms.Data/
RUN dotnet publish WmhLms.Api/WmhLms.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN adduser --system --uid 1001 --group wmh
USER wmh
COPY --from=build --chown=wmh:wmh /app .
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_gcServer=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "WmhLms.Api.dll"]
```

`DOTNET_gcServer=0` selects workstation GC, which uses far less memory in a small container; server GC is for multi-core boxes this app will never run on.

**Railway steps (first time):**

1. Railway dashboard, New Project, Deploy from GitHub repo, pick `WMH-LMS-BACKEND`, branch `main`. Railway detects the Dockerfile. Service, Settings, Region: **EU West (Amsterdam)**, the closest Railway region to both Morocco and the Paris Supabase project.
2. Service, Variables: add every "yes" row from 10.1. Use the Raw Editor and paste `KEY=value` lines. For `ConnectionStrings__Default`, paste the Supabase session-pooler string (8.3) with the real password.
3. Service, Settings, Networking: Generate Domain (gives `<name>.up.railway.app`). Then Custom Domain: `api.<domain>`. Railway shows the CNAME target to create (11.4).
4. Service, Settings, Deploy: Healthcheck Path `/health/ready`, Healthcheck Timeout `300` (migrations run before the first healthy response). Restart Policy: On Failure, max 10 retries. Replicas: 1. Leave it at 1 forever unless migrations move off boot.
5. Deploy. Watch Deployments, View Logs. The first successful boot logs `Root manager <email> created from configuration.` and then Kestrel's `Now listening on: http://[::]:8080`.
6. Verify from a terminal:

```bash
curl -i https://api.<domain>/health/ready
```

Expect `200` with body `Healthy`. Then log in:

```bash
curl -s -X POST https://api.<domain>/api/auth/login -H "Content-Type: application/json" -d "{\"email\":\"<root email>\",\"password\":\"<root password>\"}"
```

Expect a JSON body with `token` and `user`.

Subsequent deploys: push to `main`. Railway builds and swaps when the health check passes.

### 11.3 Frontend: Cloudflare Pages

1. Cloudflare dashboard, Workers & Pages, Create, Pages, Connect to Git, pick `WMH-LMS-FRONTEND`.
2. Build settings: Framework preset `Vite`; Build command `npm run build`; Build output directory `dist`; Root directory `/`; Production branch `main`.
3. Environment variables (Production): `VITE_API_BASE_URL=https://api.<domain>/api`, `NODE_VERSION=20`.
4. Save and Deploy. First build takes two to three minutes.
5. Custom domains: add `app.<domain>`. If the domain's DNS is on Cloudflare, the CNAME is created for you.
6. Add `public/_headers` to the repo so every response carries the same baseline headers the API sends:

```
/*
  X-Content-Type-Options: nosniff
  X-Frame-Options: DENY
  Referrer-Policy: strict-origin-when-cross-origin
  Permissions-Policy: geolocation=(), microphone=(), camera=()
```

No `Content-Security-Policy` yet: the YouTube iframe, the inline `style` attributes, and the token bake-in make a first CSP easy to get wrong on launch week. It is listed in the backlog.

Preview deployments (every non-`main` branch) get a random `*.pages.dev` origin that is not in the API's CORS list, so previews can render but cannot log in. That is acceptable; they exist to check the build.

### 11.4 DNS records

The DNS host is not yet known (Q1). Whoever bought the domain has the login; the records are the same at any provider. If DNS is at Cloudflare, the `app` record is created automatically when the custom domain is added to Pages; if it is elsewhere, Pages shows a CNAME target and a one-time TXT verification record to create by hand.

| Name (new-domain layout) | Name (subdomain layout) | Type | Target | Cloudflare proxy, if applicable |
|---|---|---|---|---|
| `app` | `learn` | CNAME | `<project>.pages.dev` | Proxied (Pages manages it) |
| `api` | `api-learn` | CNAME | `<service>.up.railway.app` (from Railway's Custom Domain dialog) | **DNS only** (grey cloud) |

`api` is DNS-only so that TLS terminates at Railway with the certificate Railway issues; putting Cloudflare's proxy in front as well works only with the SSL mode set to Full (strict) and adds a second place to debug when a request fails.

Propagation is usually minutes. Verify with:

```bash
nslookup api.<domain>
```

### 11.5 Rolling back

**Backend:** Railway, service, Deployments, find the last green deployment, three-dot menu, Redeploy. The previous image comes back in about a minute. Schema is not rolled back (8.8); the previous code must tolerate the newer schema, which is true when a migration only adds things. If a migration dropped or renamed something, roll forward with a fix instead.

**Frontend:** Cloudflare, Pages project, Deployments, previous production deployment, three-dot menu, Rollback to this deployment. Instant.

**Variables:** changing a Railway variable triggers a redeploy of the same image. Reverting the variable is the rollback.

Write the deploy time and the commit hash in the deploy's Railway notes field or in a one-line commit message. Future you at 9pm wants to know what changed last.

### 11.6 Running the production build locally (before any hosting exists)

Hosting accounts arrive at the end of the week. Until then the exact production artifacts run on a laptop: the same Docker image, `ASPNETCORE_ENVIRONMENT=Production`, real Postgres, the frontend's real `vite build` output. The only difference from Railway is that `Security__RequireHttps=false`, because there is no TLS proxy in front.

**Backend:**

1. Copy `.env.production.example` to `.env.production.local` and fill in a real `Jwt__Key` (`openssl rand -base64 48`, or any 48 random characters) and a `RootManager__Password`.
2. Build and start database plus API:

```bash
docker compose --profile prod up --build -d
```

3. Confirm:

```bash
curl -i http://localhost:8080/health/ready
```

Expect `200 Healthy`. Logs: `docker compose logs -f api`. The first boot prints the migration and `Root manager ... created from configuration.`

**Frontend:**

1. Create `.env.production.local` in the frontend repo containing `VITE_API_BASE_URL=http://localhost:8080/api`. Vite reads it for `vite build` and it is gitignored.
2. Build and serve the built output:

```bash
npm run build
```

```bash
npm run preview
```

3. Open `http://localhost:4173`. That origin is the one the compose file allows in CORS.

**Stop and reset:**

```bash
docker compose --profile prod down -v
```

This is the environment the smoke test in 13.4 runs against first (steps 1 to 8; steps 9 and 10 need the real deployment). Everything that passes here passes on Railway, except the three things a laptop cannot prove: TLS, the Supabase pooler, and the office IP.

---

## 12. Operations runbook

Written for someone who has never operated a service. Follow it top to bottom.

### 12.1 Health endpoints

| URL | Meaning | Healthy response |
|---|---|---|
| `https://api.<domain>/health/live` | The process is up | `200 Healthy` |
| `https://api.<domain>/health/ready` | The process is up **and** can query the database | `200 Healthy`; `503 Unhealthy` if the database check fails |

Railway calls `/health/ready` and restarts the container if it fails repeatedly. You can call both from any browser.

### 12.2 When a user reports a problem

Work down this list; stop at the first step that finds the cause.

1. **"The site does not load."** Open `https://app.<domain>` yourself. If it fails for you: Cloudflare dashboard, Pages project, Deployments. A red build is the cause; roll back (11.5). If the site loads for you, ask the user to hard-refresh (Ctrl+F5) and to confirm the URL.
2. **"I cannot log in: Network error."** Open `https://api.<domain>/health/ready`. If it does not answer or returns 503: Railway dashboard, service, Deployments. Read the latest deploy's logs (12.3). Common causes: a bad variable (startup throws and logs the reason, such as `Cors:Origins must list...` or `Jwt:Key is not configured`), or the database being unreachable (12.4).
3. **"I cannot log in: Too many attempts."** The office hit the login limiter. Railway, Variables, raise `RateLimit__LoginAttempts` (each change redeploys, about a minute). Then reconsider the per-account limiter in 9.2.
4. **"I cannot log in: Invalid email or password."** That is the app working. Verify the account exists and is active in Manager Console, Users. Reset the password from the edit dialog.
5. **"It says something went wrong / an unexpected error occurred."** Ask for the **Reference** value shown in the error banner (7.4). Search the logs for it (12.3). The log line at Error level with that correlation id contains the stack trace.
6. **A course or quiz behaves wrongly.** Reproduce with a manager account, which can open any course tree through the same endpoints. Check `Quiz__CooldownMinutes` and the lock status endpoint `GET /api/learn/quiz/lock?agent_id=<id>&item_id=<id>` with a manager token.
7. **Everything is slow.** Railway, service, Metrics: CPU and memory. Supabase, Reports, Database. At this scale a slow app is almost always the database being paused, restarted, or at connection limit (12.4).

### 12.3 Reading logs

Railway, service, Logs (or the Logs tab inside a deployment). With `AddJsonConsole()` (W8, `Startup/LoggingSetup.cs`):

```csharp
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o =>
{
    o.IncludeScopes = true;
    o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    o.UseUtcTimestamp = true;
});
```

every line is one JSON object with `Timestamp`, `LogLevel`, `Category`, `Message`, and a `Scopes` array that carries `CorrelationId` and `RequestPath` from `CorrelationIdMiddleware`. In the Railway filter box, paste the correlation id from the user's error banner. Warning-level lines are expected failures (a 403, a 423); Error-level lines are bugs.

Until W8 lands, the default console logger prints the same information as indented text; the correlation id search still works because the scope is printed under each line.

Why not Serilog: the code already uses `ILogger<T>` with scopes, and the built-in JSON console formatter needs no package and no configuration section. Serilog becomes worth it only if logs must go to a file or a second sink, which Railway's log capture makes unnecessary.

### 12.4 Database problems

- **Supabase dashboard, project Home** shows status. The project is on the **Free** plan (owner's decision, Q2). Free projects are **paused after seven days without any database activity**. Daily use keeps it alive; a two-week office closure will pause it, and the first Monday back the API's `/health/ready` returns 503 and every login fails with "Network error". Fix: Supabase dashboard, the paused project, Restore project; it takes a few minutes. Put a reminder in the calendar for the morning after any closure longer than five days. Free limits (500 MB database, 2 projects, shared compute) are far above this app's needs; the second project slot is used for the restore rehearsal in 12.6.
- **"remaining connection slots" / "MaxClientsInSessionMode"** in the logs means the pooler is out of connections. The app holds at most 10 (8.3). Check what else is connected (Supabase, Database, Roles, or a forgotten `psql`). Restarting the Railway service releases the app's ten.
- **Password rotated** (Supabase, Settings, Database, Reset database password): update `ConnectionStrings__Default` on Railway. The redeploy happens automatically.
- **Connecting yourself:** install `psql` and use the same session-pooler string in URL form. Read-only look-arounds are fine; never `DELETE` or `UPDATE` by hand in production.

### 12.5 Backups

**What Supabase provides:** on the **Free** plan, which is the plan chosen (Q2), **nothing**. There is no daily backup and no restore button. The workflow below is therefore the only copy of the data, which is why it runs twice a day (the most that can be lost is half a day of progress records) and why the restore rehearsal in 12.6 is on the blocking list. If the company ever upgrades to Pro (daily backups retained 7 days, no pausing), keep the workflow anyway; two independent copies are better than one.

**Twice-daily `pg_dump`** via `.github/workflows/backup.yml` in the backend repo:

```yaml
name: Database backup

on:
  schedule:
    - cron: "15 2 * * *"    # 02:15 UTC, before the office day
    - cron: "15 13 * * *"   # 13:15 UTC, mid-day
  workflow_dispatch:         # lets you run it by hand from the Actions tab

jobs:
  dump:
    runs-on: ubuntu-latest
    steps:
      - name: Install a pg_dump that matches the server version
        run: |
          sudo sh -c 'echo "deb https://apt.postgresql.org/pub/repos/apt $(lsb_release -cs)-pgdg main" > /etc/apt/sources.list.d/pgdg.list'
          curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc | sudo gpg --dearmor -o /etc/apt/trusted.gpg.d/pgdg.gpg
          sudo apt-get update -q
          sudo apt-get install -y postgresql-client-17
      - name: Dump
        env:
          DATABASE_URL: ${{ secrets.BACKUP_DATABASE_URL }}
        run: |
          pg_dump --format=custom --no-owner --no-privileges --file=wmhlms.dump "$DATABASE_URL"
      - name: Encrypt
        env:
          PASSPHRASE: ${{ secrets.BACKUP_PASSPHRASE }}
        run: |
          gpg --batch --yes --symmetric --cipher-algo AES256 --passphrase "$PASSPHRASE" wmhlms.dump
          rm wmhlms.dump
      - uses: actions/upload-artifact@v4
        with:
          name: wmhlms-${{ github.run_id }}
          path: wmhlms.dump.gpg
          retention-days: 30
```

`BACKUP_DATABASE_URL` is the session-pooler string in URL form: `postgresql://postgres.<ref>:<password>@aws-0-<region>.pooler.supabase.com:5432/postgres?sslmode=require`. The `pg_dump` major version must be at least the server's; hence the explicit client 17 install. The dump is encrypted because it contains employee names, emails, and password hashes; the passphrase lives in the repo's secrets and in your password manager.

Check the Actions tab once a week for a green run. GitHub emails you when a scheduled workflow fails.

### 12.6 Restore procedure (rehearse it once before launch)

Rehearse against a **second, throw-away Supabase project** so production is never touched by a test. The Free plan allows two projects, so this costs nothing; delete the rehearsal project afterwards so the slot stays free for a real recovery.

1. Download the latest `wmhlms-<run>` artifact from the Actions tab and unzip it.
2. Decrypt:

```bash
gpg --batch --decrypt --passphrase "<passphrase>" --output wmhlms.dump wmhlms.dump.gpg
```

3. Restore into the target database (the throw-away project's session-pooler URL, or production's when it is real):

```bash
pg_restore --clean --if-exists --no-owner --no-privileges --dbname "<target URL>" wmhlms.dump
```

`--clean --if-exists` drops and recreates each object, so this also works on a database that already has the schema. Warnings about `plpgsql` already existing are harmless.

4. Verify:

```bash
psql "<target URL>" -c "select count(*) as users from \"Users\"; select count(*) as assignments from \"Assignments\";"
```

Counts should match what the manager console showed when the backup ran.

5. To fail production over to the restored project: change `ConnectionStrings__Default` on Railway to the restored project's string. One redeploy later the app is on the restored data.

Write down how long steps 1 to 4 took. That number is your recovery time, and it is the honest answer when someone asks.

### 12.7 Error tracking (W6)

Sentry free tier, one organisation, two projects.

- **Backend:** package `Sentry.AspNetCore`; in `Program.cs`, `builder.WebHost.UseSentry(o => { o.Dsn = ...; o.Environment = env.EnvironmentName; o.TracesSampleRate = 0; })` reading `Sentry:Dsn`. Unhandled exceptions are captured before `ExceptionHandlingMiddleware` turns them into a 500. `ApiException` is expected and is **not** sent: add `o.SetBeforeSend((e, hint) => e.Exception is ApiException ? null : e)`.
- **Frontend:** package `@sentry/react`; in `main.jsx`, `Sentry.init({ dsn: import.meta.env.VITE_SENTRY_DSN, tracesSampleRate: 0 })`, then `installErrorReporter((error, context) => Sentry.captureException(error, { extra: context }))` from `logger.js:16`, and `Sentry.captureException(error)` inside `ErrorBoundary.componentDidCatch` where the comment sits today (`ErrorBoundary.jsx:21`).
- Turn on email alerts for new issues in both projects. That is the whole monitoring setup; a free uptime pinger on `/health/ready` (for example UptimeRobot, five-minute interval) is an optional extra that costs five minutes to set up.

### 12.8 Routine tasks

| When | What |
|---|---|
| Weekly | Glance at Railway metrics, the Actions tab (backup green), Sentry inbox |
| Monthly | `dotnet list package --outdated` and `npm outdated`; update patch versions; run tests; deploy |
| When a manager leaves | Disable the account (not delete: the audit trail and assignments stay readable) |
| Every 6 months | Rotate `Jwt__Key` (everyone logs in again) and the Supabase database password (update the Railway variable and the backup secret) |
| Before a large cohort | Log in with a test agent from the office network at the start time; watch the logs for 429s |

---

## 13. Testing

### 13.1 Keep what exists

`WmhLms.Tests` has 32 passing tests in two classes. `UserServiceTests` pins every root, peer-manager, self-protection, role, and input rule. `LearnServiceTests` pins enrolment checks, item membership, grading of every question type, cooldown, review gate, and the unpublished-course filter. They run against a real SQLite in-memory database with foreign keys and unique indexes enforced (`TestDb.cs`), which is why they are worth keeping exactly as they are through the provider switch. The model stays provider-neutral (8.6) so they keep working. When `LearnService` is split (section 5), the tests split with it into `QuizServiceTests` and `CourseProgressServiceTests`; the assertions do not change.

### 13.2 Add: six HTTP-level tests (WEEK-2)

These cover what the unit tests cannot: that the attributes, the fallback policy, the token validator, and the middleware are wired. `WmhLms.Tests/Integration/ApiFactory.cs` extends `WebApplicationFactory<Program>`, sets `ConnectionStrings__Default` to a `wmhlms_test` database on the local Docker Postgres, sets `RootManager__*` to test values, and before the test class runs calls `EnsureDeleted` then `Migrate` through the test project's own reference to `WmhLms.Data`. The tests use `HttpClient` only.

| # | Test | Asserts |
|---|---|---|
| 1 | Login with wrong password | `401`, body `{ "message": "Invalid email or password." }` |
| 2 | `GET /api/me` with no token | `401` (the fallback policy) |
| 3 | Agent token calling `GET /api/users` | `403` (role attribute) |
| 4 | Agent token calling `GET /api/learn/courses?agent_id=<other agent>` | `403` (`ResolveAgentId`) |
| 5 | Non-root manager `POST /api/users` with `role=manager` | `403`; root doing the same | `200` |
| 6 | Disable a user with the root token, then use the disabled user's still-valid token on `GET /api/me` | `401` (per-request revalidation) |
| 7 | Agent `GET /api/learn/courses/{id}` for an assigned course | response JSON contains no `is_correct` key anywhere (guards B6) |

Backend `ci.yml` gains a `services: postgres: image: postgres:17-alpine` block with `POSTGRES_DB: wmhlms_test` and health options; the test step sets `ConnectionStrings__Default` to `Host=localhost;...` accordingly. Unit tests keep running without Docker.

### 13.3 Add: one architecture test

Two assertions in `WmhLms.Tests/Architecture/LayeringTests.cs` that make the dependency rules from 4.3 fail loudly:

```csharp
[Fact]
public void Api_does_not_reference_EntityFrameworkCore()
{
    var referenced = typeof(WmhLms.Api.Program).Assembly.GetReferencedAssemblies().Select(a => a.Name);
    Assert.DoesNotContain("Microsoft.EntityFrameworkCore", referenced);
    Assert.DoesNotContain("Npgsql", referenced);
}

[Fact]
public void Core_references_no_project_and_no_EntityFramework()
{
    var referenced = typeof(WmhLms.Core.Common.ApiException).Assembly.GetReferencedAssemblies().Select(a => a.Name);
    Assert.DoesNotContain("WmhLms.Data", referenced);
    Assert.DoesNotContain("Microsoft.EntityFrameworkCore", referenced);
}
```

They pass only once W1 and W2 are done, so add them at the end of that work, not before.

### 13.4 Pre-launch manual smoke test (BLOCKING, run against production)

Do this on Friday with the real root account and one test agent account you create for the purpose.

1. Open `https://app.<domain>`; confirm the login page and the theme toggle.
2. Log in as root. Users: create a manager, create an agent, edit the agent's name, toggle the agent disabled and back.
3. Log in as the new manager in a private window: confirm you cannot edit the root row or the other manager row, and can edit the agent.
4. Courses: create a course, add a section, add a video item with a real unlisted YouTube URL, add a text item, add a quiz item with one question of each type. Publish.
5. Assignments: enrol the test agent. Confirm the cohort table shows 0%.
6. Log in as the test agent: the course appears under Not Started. Open it; the video plays; complete the text item; fail the quiz on purpose; confirm the cooldown and review gate; open a previous item; wait out the cooldown (set `Quiz__CooldownMinutes=1` for the rehearsal if you like, then set it back); pass the quiz; download the certificate and confirm it shows only the company name, the agent name, the course title and the date (no signer, no ID, no score).
7. Back as manager: cohort table shows 100% and a completion date.
8. Delete the test course and the test agent. Confirm the manager console still loads.
9. From a phone on mobile data (a different IP), log in as the test agent once, to prove the API is reachable outside the office network.
10. Run the backup workflow by hand (Actions, Nightly database backup, Run workflow) and confirm a green run with an artifact.

### 13.5 Not worth testing before launch

- Frontend unit or component tests. The screens are thin over a tested API; the smoke test above exercises them end to end.
- Browser automation (Playwright and the like). Valuable later for the quiz-lock reload flow, expensive to set up this week.
- Controllers in isolation, mappers, DTO shapes, CSS.
- Load testing. Sixty users making one request every few seconds is a few requests per second; Kestrel and Postgres will not notice. The only load-shaped risk is the login limiter, and step 9 plus the variable in B5 covers it.
- Coverage numbers of any kind.

---

## 14. Ordered execution plan

Each step leaves both apps runnable. Estimates are working hours for one person who knows the codebase. Front-loaded structural moves are the ones that touch many files; everything after LAUNCH-SAFE can be done in any order.

| # | Task | Est. | Leaves app runnable because |
|---|---|---|---|
| 1 | Frontend: remove `package-lock.json` from `.gitignore`, run `npm install`, commit the lockfile; commit the two README deletions; add `.env.example` | 0.5 h | Tooling only |
| 2 | Backend: switch `WmhLms.Data` to `Npgsql.EntityFrameworkCore.PostgreSQL`; rename connection key to `Default`; remove `NOCASE`; `Guard.Email` lower-cases; look-ups compare directly; new `docker-compose.yml` (10.3); delete SQLite migrations; add `Initial` migration; boot against Docker Postgres | 3 h | Local run works end to end on Postgres. Tests still pass on SQLite |
| 3 | Backend: `RootManagerOptions` + `RootManagerBootstrap` always-on; strip root user and explicit ids from `DemoSeed`; dev credentials become `root@wmh.local` / `agent@wmh.local`; update `appsettings.Development.json` | 1.5 h | Fresh local DB boots with a root login |
| 4 | Backend: append the trigger and single-root index SQL (8.5) to the `Initial` migration `Up`/`Down`; `docker compose down -v`, reboot, confirm the trigger exists with `psql` | 0.5 h | Migration re-applies cleanly |
| 5 | Backend + frontend: `LearnOptionDto` without `IsCorrect`, `LearnMapper`, used by tree and dashboard; delete `?autofill`, `handleAutofillCorrect`, `handleResetTimer`, `resetQuizLock`; `curl` the tree as an agent and grep for `is_correct` | 1.5 h | Quiz still grades server-side; UI unchanged for real users |
| 6 | Backend: CORS `WithHeaders`/`WithMethods`/`WithExposedHeaders` (9.1); frontend `httpClient` reads `X-Correlation-Id` into `ApiError`; `ErrorBanner` shows it (7.4) | 1.5 h | Additive |
| 7 | Backend: replace `Dockerfile` (11.2); `docker build .` locally to prove it | 0.5 h | Image only |
| 8 | Certificate: delete the signature block (`certificate.js:87-99`), the ID line and the score line; keep company name, title, agent name, course title, completion date | 0.5 h | Content only; the W17 redesign comes later |
| 8a | Local production run (11.6): `Security:RequireHttps` flag in `Program.cs`; `api` service under the `prod` profile in `docker-compose.yml`; `.env.production.example`; `.env.*.local` ignored; frontend `.env.production.local`; run the smoke test 13.4 steps 1 to 8 against `localhost:4173` | 1.5 h | The Production image boots on the laptop |
| | **LOCAL-PROD-READY. Everything above needs no hosting account. Steps 9 to 13 need Supabase, Railway and Cloudflare, expected at the end of the week.** | | |
| 9 | Supabase: create the project on the Free plan in West EU (Paris), note the session-pooler string; Railway: create service in EU West (Amsterdam), variables (10.1), health check, domain or platform hostname (11.0); first deploy; `curl` health and login | 2.5 h | Production API exists |
| 10 | Cloudflare Pages: connect repo, build settings, `VITE_API_BASE_URL`, `_headers`, custom domain if decided; DNS records (11.4); set `Cors__Origins__0` on Railway | 1.5 h | Production app exists |
| 11 | `RateLimit__LoginAttempts=100`, `Jwt__ExpiryMinutes=720` on Railway | 0.1 h | Variables |
| 12 | Backup workflow (12.5, twice daily) with the two secrets; run it by hand; restore rehearsal into the second Free project (12.6); record the timing; delete the rehearsal project | 2 h | Nothing in the app changes |
| 13 | Manual smoke test on production (13.4) | 1.5 h | Verification |
| | **LAUNCH-SAFE. Total above: about 17 hours, two and a half working days. Real course content is entered by you or a manager after this point (Q13).** | | |
| 14 | Sentry both sides (12.7); `AddJsonConsole` (12.3) | 2 h | Additive |
| 15 | Per-account failed-login tracker (9.2) | 1.5 h | Additive |
| 16 | Server-side sequential gating in `CompleteItemAsync` and `SubmitQuizAsync` (W9, owner confirmed it is a business rule) | 1 h | Tightens a rule the UI already enforces |
| 16a | Remembered email on the login form (W16) | 0.5 h | Additive |
| 16b | Root may edit its own name and password (W18): in `UserService.UpdateAsync`, a root target is allowed only when caller is root, and only name and password fields are applied | 1 h | Tested rule change; add two unit tests |
| 16c | Certificate redesign in the app theme (W17, spec in 15.1) | 0.5 day | Frontend only |
| 17 | Backend split, part 1: move entities to `WmhLms.Core/Domain`; constants classes; one file per DTO and per entity; `Program.cs` into `Startup/*` extension classes | 4 h | Pure moves; build passes after each file |
| 18 | Backend split, part 2, one aggregate at a time in this order: Users and Auth (repository, unit of work, `IAccountStatusReader` for the JWT validator, delete `DevController`), Audit, Courses (four services), Assignments, Learn (three services + `QuizGrader` + `ProgressReconciler`); flip `WmhLms.Core` to reference nothing and `WmhLms.Data` to reference Core; `AddDataAccess`; move health-check package to Data; add the architecture test | 2 days | Each aggregate is converted and its tests re-run before the next starts; Api compiles at every commit because the old and new paths coexist until the flip |
| 19 | Integration tests (13.2) with the Postgres service container in CI | 1 day | Additive |
| 20 | Frontend restructure: `app/`, `pages/`, `features/`, `shared/` moves (5.2); `useAsyncData`, `ErrorBanner`, `LoadingState`, `UserControls` extractions; delete the `api.js` barrel; ESLint | 1.5 days | Moves and extractions, no behaviour change; build after each folder |
| 21 | `tokens.css` and `global.css`; CSS Modules for `shared/components`; then `LoginForm`; then one feature per pass; delete Tailwind at the end (7.6) | 3 to 5 days | Each component converted and checked in both themes independently |
| 22 | Audit trail: root-only screen; record course, section, item, question and assignment actions (W15, owner confirmed) | 1 day | Additive |
| 23 | Course import and export (L7, spec in 15.2). First extension after launch, per the owner | 1 day | Additive; reuses the existing validation |
| 24 | `BrowserRouter` + `_redirects`; assets into `src/assets`; trim dashboard payload; Supabase CA pinning (L8) | 1 day | Independent small items |

The order inside the LAUNCH-SAFE block matters: 2 to 5 change the database and the API contract and are done locally first; 9 to 11 need the code from 2 to 8 to be on `main`; 12 and 13 need 9 to 11.

---

## 15. Post-launch backlog

Deferred on purpose, so it is not forgotten. Each line names the section that specifies it.

- Sentry on both sides and JSON console logging (12.7, 12.3).
- Per-account failed-login limiter (9.2).
- Server-side sequential gating (9.2, W9).
- Backend three-tier refactor: entities to Core, repositories, unit of work, `Program.cs` split, `DevController` removal, architecture test (4, 5, 6, 13.3).
- Named DTOs replacing every `object` / `Dictionary` return (2.1 list, 6.2).
- `IOptions<QuizOptions>` replacing `IConfiguration` in the learn services (6.1).
- Integration tests with a Postgres service container (13.2).
- Frontend restructure into `app/pages/features/shared`; `useAsyncData`; shared `ErrorBanner`, `LoadingState`, `UserControls`; barrel removal; ESLint (5.2, 7).
- Token file and CSS Modules, screen by screen; Tailwind removal (7.6). The largest item.
- Audit trail screen plus course, section, item, question and assignment coverage (W15).
- Root account self-service for name and password (W18).
- Remembered email on the login form (W16).
- Certificate redesign (W17, 15.1) and course import/export (L7, 15.2), specified below.
- `ItemCompletions` table replacing the JSON column if reporting needs per-item dates (L3).
- Dashboard payload trim (L4), `BrowserRouter` (L5), asset tidy (L6), Supabase CA pinning (L8), a first `Content-Security-Policy` on the SPA (11.3).
- `dotnet list package --vulnerable` in backend CI (9.2).
- Duplicate `GET /api/health` removal (W14). Fake dev credentials (W13).
- Optional uptime pinger (12.7).

### 15.1 Certificate redesign (W17)

Owner decision: the certificate is a generic company document. No signer, no certificate ID, no score line. It must look like the app.

- Stays client-side in `features/learn/certificate.js` with jsPDF; no server involvement (Q6: nobody verifies certificates).
- Landscape A4. Page fill `#F7F8ED` (the app's page colour). A 6 mm rule in `#22c55e` (accent-strong) across the top edge and a 1 mm rule in `#3ed676` (brand) above the footer.
- The light logo (`assets/newTransparentLogo.png`) centred near the top, about 40 mm wide. Load it once: `import logoUrl from '../../assets/newTransparentLogo.png'`, draw it into an `Image`, then `doc.addImage(img, 'PNG', x, y, w, h)`. Wrap the download in `async` and await the image load.
- Text, all Helvetica (built into jsPDF, no font embedding): "WATERMELONHUB" in 11 pt letter-spaced grey; "Certificate of Completion" in 28 pt black; "This certifies that" in 12 pt grey; the agent's name in 24 pt bold; "has completed" in 12 pt grey; the course title in 16 pt bold, wrapped with `doc.splitTextToSize` so long titles do not overflow; the completion date in 11 pt grey.
- Footer: the company name only, centred, 10 pt.
- Everything that was in the signature block (`certificate.js:87-99`), the `certId` (`certificate.js:78, 84`) and the score line (`certificate.js:85`) is gone.
- File name unchanged: `Certificate_<course>_<agent>.pdf`.

### 15.2 Course import and export (L7)

Owner intent: content will be authored by the owner or a manager, and an import route is wanted so courses can be bulk-created or copied rather than typed item by item. This is the first feature to build on the section 6 conventions, and it needs no new infrastructure: the JSON is posted as the request body, so there is still no file storage.

- **Format:** the existing course-tree JSON shape minus server-owned fields. `CourseImportRequest(Title, Description, Sections[])`, `SectionImport(Title, Items[])`, `ItemImport(Title, Type, ContentUrl, TextContent, Questions[])`, `QuestionImport(Type, Prompt, Options[])`, `OptionImport(Text, IsCorrect)`. Order is array order. No ids, no status, no dates.
- **Endpoints:** `POST /api/courses/import` (manager) creates the whole tree as a `draft` course in one `SaveChangesAsync`, validating every node with the same `Guard` rules and the same option-shape rules the item-by-item endpoints use (`QuestionService.BuildOptions`), and returns the created `CourseDto`. `GET /api/courses/{id}/export` (manager) returns the same shape for an existing course, so a course can be copied, edited as a file, and re-imported.
- **Files:** `WmhLms.Core/Courses/CourseImportRequest.cs` (and the four nested records, one file each), `CourseImportService.cs`, `CourseExportMapper.cs`, `ICourseRepository.AddTree(Course)`, two actions on `CoursesController`.
- **UI:** an "Import course" button on the courses list that opens a file picker for `.json`, reads it with `FileReader`, posts it, and navigates to the editor of the created course; an "Export" action on the course editor that downloads the JSON. Both in `features/courses/`.
- **Spreadsheet authoring:** if managers prefer a spreadsheet, a small script (outside the app) converts CSV to this JSON. The API stays JSON-only.
- **Tests:** one unit test that an import with a single-answer question and two correct options is rejected with 400; one that a valid import round-trips through export.

---

## 16. Assumptions and open questions

### 16.1 Assumptions made in this document

| # | Assumption | If wrong |
|---|---|---|
| A1 | The office reaches the internet through one shared public IP (confirmed by the owner, Q12) | If agents later work remotely, the limit can drop back toward 10 |
| A2 | Railway's outbound traffic is IPv4 and the Supabase direct host is IPv6-only | The session pooler works either way; the assumption only rules out the direct host |
| A3 | Supabase's pooler `pool_size` is the default 15 for the `postgres` role | Read the value in Supabase, Database, Connection pooling; keep `Maximum Pool Size` at least 3 below it |
| A4 | The Supabase project runs Postgres 17 | Match the local Docker image and the `postgresql-client-N` in `backup.yml` to whatever the dashboard shows |
| A5 | The domain's DNS is hosted at Cloudflare | Same records at any registrar; Pages custom-domain verification adds one TXT record |
| A6 | The API runs as a single Railway replica | Migrations at boot and the in-memory login limiter both assume one process |
| A7 | `mcr.microsoft.com/dotnet/aspnet:8.0` ships without `wget`/`curl`, and Railway ignores Docker `HEALTHCHECK` | Either way the Railway health-check path setting is the mechanism that matters |
| A8 | Supabase Free has no automated backups, pauses projects after seven idle days, and allows two projects | Re-read the pricing page when creating the project; the `pg_dump` workflow stands regardless |
| A9 | Nothing in the local SQLite database needs to be migrated (brief: all data is mock) | If any authored course content must survive, export it through the manager UI by re-creating it; there is no import feature |
| A10 | The two GitHub repositories stay separate and both deploy from `main` | A single repo would need Railway and Pages root-directory settings; nothing else changes |
| A11 | The company name printed on the certificate is "WatermelonHub", as in `certificate.js:36` and the page title | Change one string in `certificate.js` |

### 16.2 Decisions: answered by the project owner on 2026-09-07

Every question was put to the owner and answered. The answers are recorded here and applied throughout the document; the two still-open items are marked.

| # | Question | Answer | Applied in |
|---|---|---|---|
| Q1 | Domain and DNS host | **Still open.** Either a subdomain of the company domain (for example `learn.watermelon-hub.com`) or a new domain; DNS host unknown. To be settled right before deployment. If not settled by Thursday, launch on the platform hostnames | 11.0, 11.4 |
| Q2 | Supabase plan | **Free.** No automated backups, project pauses after seven idle days. The twice-daily `pg_dump` is the only backup | B11, 12.4, 12.5, 12.6 |
| Q3 | Region | Office is in Morocco. Supabase West EU (Paris, `eu-west-3`); Railway EU West (Amsterdam) | 8.3, 11.2, step 9 |
| Q4 | Root self-edit | Root may change its own name and password; role, status and `IsRoot` stay locked | W18, step 16b |
| Q5 | Certificate signer | None. The certificate is a generic company document: no signer, no ID, no score, company name only, redesigned in the app's theme | B12, W17, 15.1, step 8, step 16c |
| Q6 | Certificate verification | Not needed; stays a client-side PDF | 15.1 |
| Q7 | Audit trail | Build the root-only screen and extend recording to course, section, item, question and assignment actions | W15, step 22 |
| Q8 | Sequential item order | Business rule; enforce server-side | W9, step 16 |
| Q9 | Token lifetime | 12 hours. Additionally, the agent's email is remembered and pre-filled; the password is filled by the browser's password manager, never stored by the app | 9.1, W16, 7.5, step 16a |
| Q10 | Password reset | Manager-performed reset is sufficient; no email provider | 9.2 |
| Q11 | Quiz rules | Keep 100% pass, 5-minute cooldown, review gate | 10.1 |
| Q12 | Office IPs | One shared public IP | B5, A1 |
| Q13 | Content entry | The owner or a manager types content into the console after launch-safe. A course import feature is wanted as the first extension | 15.2, step 23 |
| Q14 | Root email | `ayoub.khamil@watermelon-hub.com`. Because this address is also in the committed dev seed with a known password, the production `RootManager__Password` must be different, and W13 replaces the dev seed address | 10.1, W13 |
| Q15 | Backup retention | **Not asked.** Assumed 30 days; the dumps contain employee names and emails, so do not raise it without a reason | 12.5 |

### 16.3 Contradictions between the brief and the code

- Brief: "Database: PostgreSQL (Docker locally, Supabase in production)". Code: SQLite everywhere, no Postgres anywhere. Resolved in favour of the code as the starting point and Postgres as the target (B1).
- Brief: ".NET Framework" (per the owner's description quoted in the review instructions). Code: `net8.0`. Resolved: .NET 8, container hosting is fine.
- Brief: "The database can start empty apart from seeded reference data and the root manager." Code: there is no reference data to seed, and the root manager is not seeded outside Development. Resolved: root bootstrap added; no reference tables exist or are needed.
- Brief's recommended stack lists email and file storage. Code: neither capability exists. Resolved: both omitted.
- Brief: "Media: all video and audio lives on YouTube". Code agrees: `Item.ContentUrl` holds a YouTube URL for both `video` and `audio` types, rendered by the same `YouTubePlayer`. No contradiction, confirmed.
