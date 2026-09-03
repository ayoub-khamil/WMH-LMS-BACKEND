# WMH LMS — Demo Backend

ASP.NET Core (.NET 8) + EF Core + SQLite. Serves the demo frontend.
Three tiers: `WmhLms.Api` → `WmhLms.Core` → `WmhLms.Data`.

## Fresh-machine run

Prereqs: [.NET 8 SDK](https://dotnet.microsoft.com/download) (8.0.x).

```bash
cd "antigravity backend demo"
dotnet run --project WmhLms.Api
```

First boot creates `WmhLms.Api/app.db`, applies the schema, and seeds
demo content (users + courses). The API listens on
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

## Useful knobs (`WmhLms.Api/appsettings.json`)

- `Quiz:CooldownMinutes` (default `5`) — lower for testing retake locks.
- `Seed:DemoContent` — `false` disables seeding.

## Dev helpers (Development only)

- `POST /api/dev/reset` — wipe everything back to just the root user.
- `DELETE /api/dev/quiz-lock?agent_id=&item_id=` — clear one quiz lock.

## Fresh database

Stop the backend and delete `WmhLms.Api/app.db` (`-shm`/`-wal` go with
it). Next boot re-seeds from scratch. Never commit `*.db` files.
