# Phase B: hardening while waiting for hosting

Two sessions. Both need only the Docker Postgres from A1 (`docker compose up -d db`). Stop any running API before starting a session.

---

## B1. Backend hardening

Model: Opus 5. Backend only.

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8, WmhLms.sln) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` in this folder first; it is the specification. Rules: do only the four items below, one commit each, in `antigravity backend demo`; do not refactor or rename anything else; add no packages. Backend conventions are section 6. Before reporting done, run `dotnet build` and `dotnet test` and paste the last ten lines of each. Do not push. If the document and the code disagree, stop and ask me. Windows machine, Git Bash; folder names contain spaces, quote paths.

1. Section 12.3: replace the default console logger with AddJsonConsole (IncludeScopes true, UTC timestamps) in Program.cs (or the Startup logging class if one exists). Confirm the CorrelationIdMiddleware scope values appear inside each JSON log line.
2. Section 9.2, row "Sequential gating server-side" (W9): in LearnService.CompleteItemAsync and SubmitQuizAsync, throw ApiException.BadRequest("Complete the previous modules first.") when any item earlier in Flat(course) is not in the reconciled done list. Add two tests to LearnServiceTests: completing item 2 before item 1 is refused with 400; completing item 1 then item 2 succeeds.
3. Section 9.2, row "Rate limiting on auth": add a singleton FailedLoginTracker (ConcurrentDictionary keyed by lower-cased email; 10 failures within a rolling 15 minutes) used by AuthService.LoginAsync: record a failure on a wrong password, clear on success, throw ApiException with status 429 and RetryAfterSeconds when the limit is hit. Inject TimeProvider so it is testable. Add a test.
4. W14: delete the GET /api/health action from MeController; /health/live and /health/ready remain. Update WmhLms.Api.http.

Do not touch Sentry; there is no DSN yet.
```

**Check** (in `antigravity backend demo`):

```bash
dotnet test --nologo -v q 2>&1 | tail -1
```

Expect `Passed!` with at least 35 tests.

Start the API in a second terminal (`dotnet run --project WmhLms.Api`), then:

```bash
for i in $(seq 1 11); do curl -s -o /dev/null -w "%{http_code} " -X POST http://localhost:5000/api/auth/login -H "Content-Type: application/json" -d "{\"email\":\"root@wmh.local\",\"password\":\"wrong\"}"; done; echo
```

Expect ten `401` then one `429`. Look at the API terminal: the log lines start with `{`. Stop the API.

---

## B2. Product tweaks the owner asked for

Model: Opus 5. Both repos.

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8, WmhLms.sln) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` in this folder first; it is the specification. Rules: do only the three items below, one commit each, in the repo each belongs to; do not refactor or rename anything else; add no packages. Conventions are sections 6 and 7. Before reporting done, run `dotnet build`, `dotnet test` and `npm run build` and paste the last ten lines of each. Do not push. If the document and the code disagree, stop and ask me. Windows machine, Git Bash; folder names contain spaces, quote paths.

1. W16 (section 7.5, remembered email), frontend: LoginScreen.jsx stores the email in localStorage under wmh_last_email after a successful login, pre-fills it on mount, and moves autoFocus to the password field when an email was pre-filled. The password is never stored by the app.
2. W18 (section 16.2, Q4), backend and frontend: in UserService.UpdateAsync allow a root target only when the caller is root, and apply only FirstName, LastName and Password for a root target; ignore Role and Email. Rename the test The_root_account_cannot_be_edited to Only_root_can_edit_root_and_only_name_and_password and add a test that root changing its own password succeeds. In UserManagement.jsx, blockedReason lets root edit the root row; the edit dialog disables the role and email fields for that row.
3. W17 (section 15.1), frontend: redesign certificate.js as specified: page fill #F7F8ED, green rules, the light logo via addImage, Helvetica hierarchy, company-name footer, no signer, no ID, no score, splitTextToSize for long titles. The function becomes async; update its three callers to await it.
```

**Check** (in `antigravity frontend demo`):

```bash
grep -c "wmh_last_email" src/components/auth/LoginScreen.jsx; grep -n "Sarah\|certId\|addImage" src/services/certificate.js
```

Expect `2` or more, then exactly one line containing `addImage`.

In `antigravity backend demo`:

```bash
dotnet test --nologo -v q 2>&1 | tail -1
```

Expect `Passed!`. Then run the app (`docker compose up -d db`, `dotnet run --project WmhLms.Api`, `npm run dev` in the frontend), log in as `root@wmh.local`, open Users, edit the root row, change the first name, save, and confirm the new name shows top right. Log out: the email field is pre-filled on the login page.
