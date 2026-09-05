# WatermelonHub LMS — API

**Repo:** `WMH-LMS-BACKEND`

REST API for WatermelonHub LMS, a role-based BPO training platform. ASP.NET
Core 8 + EF Core + SQLite. Pairs with [`WMH-LMS-FRONTEND`](https://github.com/ayoub-khamil/WMH-LMS-FRONTEND),
which is a pure client — this service owns all identity, content and progress.

---

## Contents

- [Architecture](#architecture)
- [Running it](#running-it)
- [Demo data](#demo-data)
- [Configuration](#configuration)
- [API surface](#api-surface)
- [What the server enforces](#what-the-server-enforces)
- [Data model](#data-model)
- [Testing](#testing)
- [Deployment](#deployment)
- [Known limitations](#known-limitations)

---

## Architecture

Three projects plus tests, referenced in one direction only:

```
WmhLms.Api    HTTP surface: controllers, middleware, JWT issuing, DI wiring
   ↓
WmhLms.Core   Business rules: services, DTOs, validation guards, demo seed
   ↓
WmhLms.Data   Persistence: entities, DbContext, EF Core migrations

WmhLms.Tests  xUnit, references all three
```

`WmhLms.Core` holds the rules and never touches `HttpContext`; controllers are
thin and mostly resolve identity, then delegate. Validation lives in a shared
`Guard` class (`Core/Services/Support.cs`) so the same input rules apply
wherever a field is written.

| Concern | Where |
|---|---|
| Request pipeline, auth, CORS, rate limiting | `Api/Program.cs` |
| Identity resolution from the bearer token | `Api/Controllers/Base.cs` |
| Error → HTTP response | `Api/Middleware/ExceptionHandlingMiddleware.cs` |
| Response headers, correlation ids | `Api/Middleware/SecurityHeadersMiddleware.cs` |
| Learner progress, grading, quiz locks | `Core/Services/LearnService.cs` |
| Users, roles, root guards | `Core/Services/UserServices.cs` |
| Course tree authoring | `Core/Services/CourseService.cs` |
| Enrolment | `Core/Services/AssignmentService.cs` |
| Admin audit trail | `Core/Services/AuditService.cs` |
| Schema | `Data/AppDbContext.cs` + `Data/Migrations/` |

JSON is snake_case in both directions (`JsonNamingPolicy.SnakeCaseLower`),
with one exception: `pagination.totalPages`.

---

## Running it

**Prereq:** [.NET 8 SDK](https://dotnet.microsoft.com/download). Newer SDKs
work — the projects target `net8.0` and roll forward.

```bash
dotnet run --project WmhLms.Api
```

Listens on `http://localhost:5000`. First boot creates `WmhLms.Api/app.db`,
applies every migration, and seeds demo content.

Check it is up:

```bash
curl http://localhost:5000/api/health
```

**Pairing the frontend.** Create `WMH-LMS-FRONTEND/.env` (gitignored):

```ini
VITE_API_BASE_URL=http://localhost:5000/api
```

Then `npm run dev` in that repo. Vite's `:5173` and `127.0.0.1:5173` are
already in the default CORS allowlist.

**Starting over.** Stop the API and delete `WmhLms.Api/app.db` along with its
`-shm` and `-wal` siblings. The next boot rebuilds and re-seeds. Never commit
`*.db` files — they are gitignored.

---

## Demo data

Seeding runs only when `Seed:DemoContent` is true. It is **false** in
`appsettings.json` and **true** in `appsettings.Development.json`, so a plain
`dotnet run` seeds and a production build does not.

Credentials come from `appsettings.Development.json`:

| Role | Email | Password |
|---|---|---|
| Manager (root) | `ayoub.khamil@watermelon-hub.com` | `ayoub1234` |
| Agent | `khalid.khamil@watermelon-hub.com` | `khalid1234` |

The manager is the root account: undeletable, and the only account that may
administer other managers.

Outside Development these four `Seed:` values have no defaults — the seed
throws rather than inventing a password for a signable account. Set them
explicitly or leave `Seed:DemoContent=false`.

**Seeded catalogue** (users and content seed independently):

| Id | Course | Status |
|---|---|---|
| 101 | BPO Tier 1 Customer Operations & Escalation Mastery | published |
| 102 | PCI-DSS Data Security & Privacy Compliance 2026 | published |
| 103 | Omnichannel Live Chat & Tone Calibration | draft |

Course 101 carries two sections, video/text/quiz items and all three question
types. **No assignments are seeded** — sign in as the manager and enrol the
agent before their dashboard shows anything. Course 103 stays invisible to
learners until published.

---

## Configuration

Standard ASP.NET configuration: `appsettings.json`, then
`appsettings.{Environment}.json`, then environment variables (`__` replaces
`:` — `Jwt:Key` is `JWT__KEY`).

| Key | Default | Notes |
|---|---|---|
| `ConnectionStrings:Sqlite` | `Data Source=app.db` | |
| `Jwt:Key` | *(none)* | ≥32 bytes. Required outside Development; startup fails fast if missing or short. In Development an ephemeral per-run key is generated, so restarting invalidates dev tokens. |
| `Jwt:Issuer` / `Jwt:Audience` | `wmh-lms` / `wmh-lms-frontend` | Validated on every request. |
| `Jwt:ExpiryMinutes` | `1440` | |
| `Seed:DemoContent` | `false` (`true` in Development) | Logs a warning if enabled outside Development. |
| `Seed:ManagerEmail` / `ManagerPassword` / `AgentEmail` / `AgentPassword` | Development only | |
| `Quiz:CooldownMinutes` | `5` | Lower it to test retake locks. |
| `Cors:Origins` | `localhost:5173`, `127.0.0.1:5173` | Required outside Development; startup throws if empty. |
| `RateLimit:LoginAttempts` | `10` (`200` in Development) | Per client IP. |
| `RateLimit:WindowMinutes` | `1` | |

No signing key is ever committed. `Jwt:Key` is absent from both tracked
appsettings files by design.

---

## API surface

Everything is under `/api`. Every endpoint requires a bearer token unless
marked otherwise — a fallback authorization policy makes authentication the
default, so a controller that forgets `[Authorize]` is still protected.

### Auth

| Method | Path | Access |
|---|---|---|
| `POST` | `/api/auth/login` | anonymous, rate-limited |
| `POST` | `/api/auth/logout` | any signed-in user |
| `GET` | `/api/me` | any signed-in user |
| `GET` | `/api/health` | anonymous |

`login` takes `{email, password}` and returns `{token, user}`.

### Users — manager only

| Method | Path |
|---|---|
| `GET` | `/api/users?role=&status=&search=&page=&limit=` |
| `POST` | `/api/users` |
| `PATCH` | `/api/users/{id}` |
| `PATCH` | `/api/users/{id}/status` |
| `DELETE` | `/api/users/{id}` |
| `GET` | `/api/users/{id}/assignments` |

### Courses and curriculum — manager only

| Method | Path |
|---|---|
| `GET` | `/api/courses?status=&search=&page=&limit=` |
| `POST` | `/api/courses` |
| `GET` `PATCH` `DELETE` | `/api/courses/{id}` |
| `POST` | `/api/courses/{id}/sections` |
| `PUT` | `/api/courses/{id}/sections/order` |
| `PATCH` `DELETE` | `/api/sections/{id}` |
| `POST` | `/api/sections/{id}/items` |
| `PUT` | `/api/sections/{id}/items/order` |
| `PATCH` `DELETE` | `/api/items/{id}` |
| `POST` | `/api/items/{id}/questions` |
| `PATCH` `DELETE` | `/api/questions/{id}` |

### Enrolment — manager only

| Method | Path |
|---|---|
| `POST` `DELETE` | `/api/assignments/bulk` |
| `GET` | `/api/courses/{id}/assignments` |

### Learning — any signed-in user

| Method | Path |
|---|---|
| `GET` | `/api/learn/courses?agent_id=` |
| `GET` | `/api/learn/courses/{id}?agent_id=` |
| `GET` | `/api/learn/courses/{id}/resume?agent_id=` |
| `POST` | `/api/learn/complete` |
| `POST` | `/api/learn/quiz/submit` |
| `POST` | `/api/learn/views` |
| `GET` | `/api/learn/quiz/lock?agent_id=&item_id=` |

### Audit — root only

`GET /api/audit?limit=` returns the administrative trail, newest first.

### Development-only helpers

Both 404 outside Development.

| Method | Path | Notes |
|---|---|---|
| `POST` | `/api/dev/reset` | Wipes courses, assignments, locks and every non-root user. Restart to re-seed the catalogue. |
| `DELETE` | `/api/dev/quiz-lock?agent_id=&item_id=` | Clears one cooldown. Agents may only clear their own. |

### Health probes

`GET /health/live` answers "is the process up". `GET /health/ready` also
proves the database is reachable — gate orchestration on that one. Both are
anonymous and sit outside `/api`.

### Errors

Always `{ "message": "..." }`:

| Status | Meaning |
|---|---|
| `400` | Validation failure |
| `401` | Missing, expired or unusable token; bad credentials |
| `403` | Authenticated but not permitted (wrong role, not enrolled, root-only action) |
| `404` | Not found |
| `409` | Duplicate email, or a lost write race caught by a unique index |
| `423` | Quiz retake locked — carries a `Retry-After` header in seconds |
| `429` | Login rate limit — carries `Retry-After: 60` |
| `500` | Unexpected. Internal detail is included only in Development. |

Every request gets an `X-Correlation-Id` response header (echoed back if you
send one), and that id is attached to the log scope.

---

## What the server enforces

The API does not trust the client for identity or progress. Worth knowing
before changing the frontend.

**Identity comes from the token.** The `agent_id` that learn endpoints accept
is a hint, never an authority. An agent may only ever act as themselves;
managers may read on an agent's behalf for preview and support. Mismatches
return `403`.

**Tokens are re-checked on every request.** A signed token is not enough — the
account behind it must still exist, still be active, and still hold the role
baked into the token. Disabling, deleting or re-roling a user invalidates
their outstanding tokens immediately instead of waiting for expiry.

**Enrolment is required.** Reading a course tree, completing an item and
submitting a quiz all need an existing assignment. Learners cannot enrol
themselves.

**Items must belong to the course** they are submitted against, and stored
progress is reconciled against the live course tree on every read and write.
An item that was deleted, or that never belonged to the course, can never
count toward completion — which is what makes "done ≥ total" trustworthy.

**Quiz grading is server-side and strict.** Every question is graded by set
equality between selected and correct options, so partially answering a
multiple-answer question is wrong. All questions must be answered. Passing
requires every question correct.

**The cooldown and the review gate are both server-side.** A failed attempt
writes a `QuizLock` row that survives page reloads. Retaking before the
cooldown expires returns `423` with the remaining seconds; retaking after it
expires but without having viewed another item in the course also returns
`423`. The lock is checked *before* answers are validated, so a locked learner
always gets a countdown rather than a confusing `400`.

**Only root administers managers.** Creating, editing, disabling, deleting a
manager, and changing any role, are root-only — otherwise managers, being
peers, could reset each other's passwords. Nobody may disable or delete
themselves, and the root account can do neither.

**Passwords** are hashed with ASP.NET Core's `PasswordHasher`, minimum eight
characters, never logged and never returned by any endpoint. Login returns an
identical `401` for an unknown email and a wrong password, so the API cannot
be used to enumerate accounts. Login is rate-limited per client IP.

**Administrative actions are audited.** User create, update, password change,
role change, status change and delete each append an `AuditEntry` in the same
transaction as the change itself, so an action and its record either both land
or neither does. The trail deliberately has no foreign key to `Users` — it
must outlive the account it describes.

---

## Data model

```
User ──┬─< Assignment >─┬── Course ──< Section ──< Item ──< Question ──< Option
       └─< QuizLock >───┘                                    ↑
                          QuizLock also references the quiz Item

AuditEntry   (standalone — no FK, survives actor deletion)
```

Real foreign keys with cascade delete throughout. Two unique indexes carry
weight: `(course_id, agent_id)` on `Assignment` closes the check-then-insert
enrolment race, and `(agent_id, quiz_item_id)` on `QuizLock` does the same for
cooldowns. A lost race surfaces as a `409`, not a duplicate row.

Cascades are *also* performed explicitly at the call site (deleting a user
clears their assignments and locks; deleting a course or item clears theirs)
so behaviour is identical on any provider and visible where it happens.

Two denormalisations, both deliberate:

- `Assignment.CompletedItemIdsJson` is a JSON array rather than a join table.
  It is always reconciled against the live tree before use, so stale ids are
  harmless.
- `AuditEntry.ActorEmail` is copied at write time so the trail stays readable
  after the actor is deleted.

`User.Email` is unique and uses SQLite's `NOCASE` collation, so `A@b.com` and
`a@b.com` cannot coexist. On Postgres, swap that for `citext`.

### Migrations

The schema is versioned in `WmhLms.Data/Migrations` and applied on boot with
`MigrateAsync`. After changing an entity:

```bash
dotnet ef migrations add <Name> --project WmhLms.Data --startup-project WmhLms.Api
```

A database created by the older `EnsureCreated` path fails startup with an
explicit message telling you to delete `app.db` and rebuild.

---

## Testing

```bash
dotnet test
```

32 xUnit tests across two suites:

- `LearnServiceTests` — enrolment gates, cross-course item rejection, progress
  reconciliation, completion arithmetic, all three question types, the
  cooldown, the review gate, and unpublished-course filtering.
- `UserServiceTests` — root immutability, manager-vs-manager guards, self
  disable/delete guards, role-change permissions.

Tests run against a real SQLite database held in memory, not the EF in-memory
provider — that provider enforces neither foreign keys, unique indexes nor
cascades, which are exactly the behaviours under test. Time is injected via
`TimeProvider`, so cooldown tests advance a fake clock instead of sleeping.

---

## Deployment

**Docker.** Multi-stage build; the test suite runs inside the build, so a
failing test fails the image. Runs as a non-root user, stores the database on
a `/data` volume, listens on `:8080`, and ships a `HEALTHCHECK` against
`/health/ready`.

```bash
docker build -t wmh-lms-api .
```

**Compose.** Requires a signing key in the environment:

```bash
JWT_KEY=$(openssl rand -base64 48) docker compose up
```

`FRONTEND_ORIGIN` sets the CORS allowlist (defaults to
`http://localhost:5173`).

**Outside Development** the app additionally enables HSTS (365 days) and HTTPS
redirection, and honours `X-Forwarded-For` / `X-Forwarded-Proto` so that
scheme detection and IP-keyed rate limiting see the real caller rather than
the proxy. Known networks and proxies are cleared — tighten that if you are
not terminating TLS at a trusted ingress.

Responses carry `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`,
`Referrer-Policy: no-referrer`, `Cache-Control: no-store`, a restrictive
`Permissions-Policy`, and `Content-Security-Policy: default-src 'none';
frame-ancestors 'none'` — nothing here should ever render as a document.

**CI** (`.github/workflows/ci.yml`) restores, builds Release, runs the tests,
uploads the `.trx` results, then builds the Docker image.

---

## Known limitations

Current state, honestly. None of these are blocked on anything.

**Quiz answers are sent to learners.** `OptionDto` serialises `is_correct`,
and the learn endpoints embed the full question tree. Any learner can read the
answer key out of the network response, so the strict-100% gate is advisory in
practice. Fixing this means splitting the learner-facing option shape from the
manager-facing one, and it changes a payload the frontend reads.

**Item order is not enforced on completion.** `POST /api/learn/complete`
checks that the item belongs to the course and that the caller is enrolled,
but not that earlier items are done. The sequential gating a learner sees is
implemented in the client only, so the API will accept completions in any
order.

**Certificates are generated client-side.** The API supplies inputs but issues
nothing and verifies nothing, and certificate ids are throwaway. A server-side
issuer would need a `Certificates` table and an endpoint that refuses unless
the assignment is genuinely `completed`.

**Bulk enrolment does not filter by role or status.** `AssignBulkAsync`
resolves agent ids without checking `role = 'agent'` or `status = 'active'`,
so a manager or a disabled account can be enrolled through the API. The UI
only ever offers active agents.

**The audit trail has no reader.** `GET /api/audit` works and is root-gated,
but nothing in the frontend calls it. Only user actions are recorded — course
and enrolment changes are not yet audited.

**Unpublished courses vanish mid-training.** The dashboard filters to
`published`, so unpublishing a course silently removes it from the dashboards
of learners partway through. An explicit "unavailable" state would be kinder,
and needs a matching UI change.

**`text_content` is not sanitised server-side.** The client renders it as
plain text, so this is defence in depth rather than a live hole, but the API
will store whatever it is given.

**Migrations are not covered by tests.** The suite builds its schema with
`EnsureCreated` from the model snapshot, so drift between the model and the
migration files would not be caught.

**Auth hardening left for a real build:** JWTs are bearer tokens held in
client `localStorage` rather than httpOnly cookies; there is no refresh-token
rotation, no password reset and no invite flow.
