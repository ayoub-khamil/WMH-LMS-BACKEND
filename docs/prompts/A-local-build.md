# Phase A: production build running on your PC

Three sessions, then your own smoke test. Prerequisites in `README.md` first.

---

## A1. Database: Postgres, root bootstrap, root protection

Model: Fable 5.1. Backend only. The longest session in Phase A.

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8, WmhLms.sln) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` in this folder first; it is the specification and section 14 is the ordered plan. "Step N" means section 14.

Rules: do only the task below; do not start the next step; do not refactor, rename or improve anything the task does not name; add no packages the task does not name. Anti-goals apply (no MediatR, CQRS, Redis, AutoMapper, FluentValidation, TypeScript, react-query, new UI libraries). Backend conventions are section 6: one class per file, Guard for validation, ApiException for expected failures, async with CancellationToken. Keep the app runnable at every commit. Before reporting done, run `dotnet build` and `dotnet test` and paste the last ten lines of each. Commit inside the repo you changed, one commit per logical change, short imperative subject. Do not push. If the document and the code disagree, or a decision is missing from section 16, stop and ask me. Windows machine, Git Bash, Docker Desktop running; folder names contain spaces, quote paths.

Task: steps 2, 3 and 4 of section 14, in order, all in `antigravity backend demo`.

Before anything: run `docker ps --format "{{.Names}} {{.Ports}}"`. If host port 5432 or 8080 is already used by another container, use 5433 and/or 8081 instead everywhere (docker-compose.yml host side, appsettings.Development.json, and the commands you print at the end) and tell me which ports you chose.

Step 2 (sections 8.1, 8.2, 10.3): replace Microsoft.EntityFrameworkCore.Sqlite in WmhLms.Data with Npgsql.EntityFrameworkCore.PostgreSQL 8.0.*; move Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore from WmhLms.Api to WmhLms.Data; WmhLms.Tests keeps Sqlite. Rename the connection string key from Sqlite to Default everywhere and switch UseSqlite to UseNpgsql. Delete the NOCASE collation in AppDbContext. Guard.Email returns the lower-cased email; every email comparison in the services becomes a direct equality on the normalised value. Replace docker-compose.yml with the two-service file from section 10.3 (db, and api under the prod profile). Add `.env.*.local` to .gitignore and add the tracked `.env.production.example` from section 10.3. Delete the three files in WmhLms.Data/Migrations. Start Postgres with `docker compose up -d db`. Create the migration with `dotnet ef migrations add Initial --project WmhLms.Data --startup-project WmhLms.Api`. Open the generated migration and confirm the column types are Postgres types (bigint, text, character varying, timestamp with time zone, boolean).

Step 3 (sections 8.4, 10.1): add RootManagerOptions and RootManagerBootstrap exactly as specified; it runs on every boot after MigrateAsync and the app refuses to start if Email or Password is missing. Strip the root user and every explicit `Id =` from DemoSeed; the demo agent becomes agent@wmh.local / dev-agent-password. appsettings.Development.json gets ConnectionStrings:Default for the Docker Postgres, RootManager values root@wmh.local / dev-root-password / Root / Manager, and loses the Seed:Manager* and Seed:Agent* keys. Update WmhLms.Api.http to the new dev accounts.

Step 4 (section 8.5): append the trigger and single-root partial index SQL to the Initial migration's Up, and the drops to Down.

Finish: `docker compose down -v`, `docker compose up -d db`, run the API, confirm the log shows the migration applied and "Root manager root@wmh.local created from configuration.", stop the API. Run `dotnet test`; all 32 must pass. Three commits, one per step. At the end print the exact commands I should run to start Postgres and the API, with the ports you chose.
```

**Check** (Git Bash, in `antigravity backend demo`; use the ports the agent chose):

```bash
grep -rn "UseSqlite\|NOCASE\|Sqlite" WmhLms.Api WmhLms.Core WmhLms.Data --include=*.cs --include=*.csproj --include=*.json
```

Expect no output.

```bash
dotnet test --nologo -v q 2>&1 | tail -2
```

Expect `Passed!` with 32 or more tests.

```bash
docker exec wmhlms-db psql -U wmhlms -d wmhlms -c "select tgname from pg_trigger where tgname='trg_protect_root_user';" -c "select \"Email\",\"IsRoot\" from \"Users\";"
```

Expect one trigger row and a user row `root@wmh.local | t`.

Start the API in a second terminal (`dotnet run --project WmhLms.Api`), then:

```bash
curl -s -X POST http://localhost:5000/api/auth/login -H "Content-Type: application/json" -d "{\"email\":\"root@wmh.local\",\"password\":\"dev-root-password\"}" | head -c 120
```

Expect a body starting with `{"token":"`. Copy the token; A2's check needs it. Stop the API (Ctrl+C) before the next session.

---

## A2. API contract and frontend hygiene

Model: Opus 5. Both repos.

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8, WmhLms.sln) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` in this folder first; it is the specification and section 14 is the ordered plan. "Step N" means section 14.

Rules: do only the task below; do not start the next step; do not refactor, rename or improve anything the task does not name; add no packages the task does not name. Anti-goals apply (no MediatR, CQRS, Redis, AutoMapper, FluentValidation, TypeScript, react-query, new UI libraries). Backend conventions are section 6; frontend conventions are section 7 (names carry meaning, comments explain why never what, no inline style={{}} except dynamic values). Keep both apps runnable at every commit. Before reporting done, run `dotnet build` and `dotnet test` in the backend and `npm run build` in the frontend and paste the last ten lines of each. Commit inside the repo you changed, one commit per logical change, short imperative subject. Do not push. If the document and the code disagree, or a decision is missing from section 16, stop and ask me. Windows machine, Git Bash; folder names contain spaces, quote paths.

Task: steps 5, 6 and 8 of section 14.

Step 5 (section 6.2 and the "Answer key" row of 9.2): in WmhLms.Core add learn-facing DTOs LearnOptionDto(Id, Text), LearnQuestionDto, LearnItemDto, LearnSectionDto and a LearnMapper. Use them in LearnService.GetCourseTreeAsync and GetCoursesAsync so no learn endpoint ever serialises is_correct. The manager endpoints under /api/courses keep OptionDto with IsCorrect. Frontend: delete the ?autofill flag, handleAutofillCorrect, handleResetTimer and the "reset timer" button from QuizPlayer.jsx; delete resetQuizLock from learn.api.js and api.js. Backend: delete DevController's quiz-lock action, keep its reset action.

Step 6 (sections 9.1, 7.3, 7.4): the CORS policy uses WithHeaders("Authorization","Content-Type","X-Correlation-Id"), WithMethods("GET","POST","PUT","PATCH","DELETE") and WithExposedHeaders("X-Correlation-Id","Retry-After"). Frontend: add src/services/ApiError.js as in 7.3; httpClient.js throws ApiError with status, payload and correlationId read from the response header. Add src/components/common/ErrorBanner.jsx (current folder layout; the restructure is a later step) showing the message, a "Reference: <id>" line when present, and an optional Retry button. Replace the nine copied red error-banner blocks in AgentDashboard, CourseViewer, QuizPlayer, CoursesList, CourseEditor, ItemEditor, QuizQuestionEditor, AssignmentsManager and UserManagement with it. Keep the Tailwind classes inside ErrorBanner for now.

Step 8 (section 15.1, first paragraph only): in certificate.js delete the signature block, the certificate ID and the Score line. Keep company name, title, agent name, course title and issue date. No redesign in this session.

Commit per step in the repo each change belongs to.
```

**Check** (start the API first: `dotnet run --project WmhLms.Api` in `antigravity backend demo`; replace TOKEN with the token from A1, or log in again):

```bash
curl -s "http://localhost:5000/api/learn/courses/101?agent_id=1" -H "Authorization: Bearer TOKEN" | grep -c is_correct; curl -s http://localhost:5000/api/courses/101 -H "Authorization: Bearer TOKEN" | grep -c is_correct
```

Expect `0` then a number greater than `0`. (Course 101 is the demo seed; if it is missing, the API was started outside Development. Use `dotnet run --project WmhLms.Api`, not the Docker image.)

```bash
curl -si -H "Origin: http://localhost:5173" http://localhost:5000/health/live | grep -i "expose-headers\|x-correlation-id"
```

Expect an `Access-Control-Expose-Headers` line containing `X-Correlation-Id` and an `X-Correlation-Id` header.

In `antigravity frontend demo`:

```bash
grep -rn "autofill\|resetQuizLock\|Sarah\|certId\|Full Mastery" src | wc -l; grep -rln "ErrorBanner" src/components | wc -l
```

Expect `0` then `10`. Stop the API before the next session.

---

## A3. Dockerfile and the local production run

Model: Opus 5. Both repos. This session gives you the production build on your PC.

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8, WmhLms.sln) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` in this folder first; it is the specification and section 14 is the ordered plan. "Step N" means section 14.

Rules: do only the task below; do not start the next step; do not refactor, rename or improve anything the task does not name; add no packages the task does not name. Keep both apps runnable at every commit. Before reporting done, run `dotnet build`, `dotnet test` and `npm run build` and paste the last ten lines of each. Commit inside the repo you changed, one commit per logical change, short imperative subject. Do not push. Never create a file whose name ends in `.local`; I create those by hand because they hold secrets. If the document and the code disagree, stop and ask me. Windows machine, Git Bash, Docker Desktop running; folder names contain spaces, quote paths.

Task: steps 7 and 8a of section 14 (sections 11.2, 11.6, 10.3).

Step 7: replace Dockerfile with the one in section 11.2 exactly. Update .dockerignore so it excludes bin, obj, .git and .env*.local and no longer mentions SQLite. Run `docker build -t wmhlms-api .` and confirm it succeeds. Update the backend CI workflow's docker job if needed (the Dockerfile no longer runs tests; the test job already does).

Step 8a: in Program.cs, gate AddHsts, UseHsts and UseHttpsRedirection behind a `Security:RequireHttps` setting that defaults to true, replacing the `!isDevelopment` condition. Confirm docker-compose.yml has the api service under the prod profile from section 10.3, with the host ports matching what A1 chose, and that `.env.production.example` exists and lists every key from section 10.3. In the frontend, add a comment line to .env.example explaining `.env.production.local` (VITE_API_BASE_URL for the local production run) and confirm .gitignore ignores `.env.*.local`.

Do not run the prod profile yourself; I will after creating the .local files. Finish by printing, in order, the exact commands from section 11.6 I need to run, with the correct ports.
```

**Check** (first create the two `.env.production.local` files as described in `README.md`, then in `antigravity backend demo`):

```bash
docker compose --profile prod up --build -d && sleep 25 && curl -si http://localhost:8080/health/ready | head -1 && docker compose logs api 2>&1 | grep -i "root manager\|listening"
```

Expect `HTTP/1.1 200 OK`, a "Root manager ... created" line, and "Now listening on".

```bash
curl -si http://localhost:8080/health/live | grep -ic "strict-transport\|^location:"
```

Expect `0`.

In `antigravity frontend demo`:

```bash
npm run build 2>&1 | tail -2 && grep -o "localhost:8080/api" dist/assets/*.js | head -1
```

Expect a successful build and one match. Then run `npm run preview`, open `http://localhost:4173`, and log in with the root email and the password from your backend `.env.production.local`.

---

## A4. Local smoke test (you, not the agent)

Follow `PRODUCTION-REVIEW.md` section 13.4, steps 1 to 8, against `http://localhost:4173`. About an hour. It covers user creation, course authoring, enrolment, the agent flow with a failed and a passed quiz, the certificate, and cohort progress.

**Milestone: LOCAL-PROD-READY.** Test as long as you like. Phases B and D need no hosting either.

To stop the local production stack:

```bash
docker compose --profile prod down -v
```

---

## Fix-up prompt (only if the smoke test finds a defect)

One defect per session. Fill the three angle-bracket parts.

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` first. Rules: fix only the defect below, change nothing else, add a unit test if the defect is in a backend service, run `dotnet test` and/or `npm run build`, paste the last ten lines, commit in the right repo with a short imperative subject, do not push.

Defect found in the local smoke test.
Symptom: <what I saw>
Steps to reproduce: <steps>
Expected (per section 13.4): <what should happen>
Find the cause, explain it in two sentences, fix it.
```
