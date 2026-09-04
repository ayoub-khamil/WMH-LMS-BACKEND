# WMH LMS — Demo Backend

ASP.NET Core (.NET 8) + EF Core + SQLite. Serves the demo frontend.
Three tiers: `WmhLms.Api` → `WmhLms.Core` → `WmhLms.Data`.

## Fresh-machine run

Prereqs: [.NET 8 SDK](https://dotnet.microsoft.com/download) (8.0.x).

```bash
cd "antigravity backend demo"
dotnet run --project WmhLms.Api
```

First boot creates `WmhLms.Api/app.db`, applies the EF migrations, and
seeds demo content (users + courses). The API listens on
`http://localhost:5000` — health check: `GET /api/health`.

## Pairing the frontend

In `antigravity frontend demo/.env` (gitignored, create it):

```ini
VITE_API_BASE_URL=http://localhost:5000/api
```

Then `npm run dev` in the frontend folder (Vite serves on `:5173`,
already allowed by CORS).

## Demo accounts (seeded)

| Role    | Email                              | Password   |
|---------|------------------------------------|------------|
| Manager | ayoub.khamil@watermelon-hub.com    | ayoub1234  |
| Agent   | khalid.khamil@watermelon-hub.com   | khalid1234 |

The manager is the undeletable root account. Seed credentials live in
`WmhLms.Api/appsettings.json` (`Seed:` section) and can be changed there
before first boot.

No assignments are seeded: sign in as the manager and assign a course
before the agent dashboard shows anything.

## What the server enforces

The API does not trust the client for identity or progress. Worth knowing
before changing the frontend:

- **Identity comes from the bearer token.** The `agent_id` the learn
  endpoints accept is a hint. An agent may only ever act as themselves;
  managers may read on an agent's behalf. Mismatches return `403`.
- **Enrolment is required.** Reading a course tree, completing an item or
  submitting a quiz all need an existing assignment. Learners cannot
  enrol themselves by calling the API.
- **Items must belong to the course** they are submitted against, and
  stored progress is reconciled against the live course tree, so a
  deleted or foreign item can never count toward completion.
- **Quiz cooldown and the review gate are both server-side.** Retaking
  before the cooldown expires, or without viewing another item in the
  course, returns `423` with a `Retry-After` header.
- **Only the root account administers managers** (create, edit, disable,
  delete). Managers administer agents.

Errors are always `{ "message": "..." }` with a meaningful status code.

## Useful knobs (`WmhLms.Api/appsettings.json`)

- `Quiz:CooldownMinutes` (default `5`) — lower for testing retake locks.
- `Seed:DemoContent` — `false` disables seeding.
- `Jwt:Key` — must be at least 32 bytes; startup fails fast otherwise.

## Dev helpers (Development only)

- `POST /api/dev/reset` — wipe everything back to just the root user.
  Restart the API afterwards to re-seed the demo catalogue.
- `DELETE /api/dev/quiz-lock?agent_id=&item_id=` — clear one quiz lock
  (authenticated; agents may only clear their own).

## Schema changes

The schema is managed by EF Core migrations in `WmhLms.Data/Migrations`,
applied automatically on boot. After changing an entity:

```bash
dotnet ef migrations add <Name> --project WmhLms.Data --startup-project WmhLms.Api
```

## Fresh database

Stop the backend and delete `WmhLms.Api/app.db` (`-shm`/`-wal` go with
it). Next boot rebuilds and re-seeds from scratch. Never commit `*.db`
files.
