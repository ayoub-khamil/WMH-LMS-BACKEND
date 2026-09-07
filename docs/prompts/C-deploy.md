# Phase C: deployment (end of the week)

Needs accounts on Supabase, Railway and Cloudflare, and a decision on the domain (or the no-domain fallback in `PRODUCTION-REVIEW.md` section 11.0). Most of this phase is you in dashboards following sections 11.0 to 11.5. The agent has two small jobs.

Order for the day: C1 (agent) → Supabase project (you) → Railway service (you, C2 helps) → Cloudflare Pages (you) → DNS (you) → C3 (you).

---

## C1. Backup workflow, CI vulnerability check, SPA headers

Model: Sonnet 5. Both repos. Do this before deploy day.

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` in this folder first. Rules: do only the three items below, one commit each; change nothing else; do not push. Windows machine, Git Bash; folder names contain spaces, quote paths.

1. Backend: add .github/workflows/backup.yml exactly as in section 12.5 (two cron entries, PostgreSQL 17 client install, pg_dump custom format, gpg symmetric encryption, upload-artifact with 30-day retention, workflow_dispatch).
2. Backend: add a step to .github/workflows/ci.yml after restore that runs `dotnet list package --vulnerable --include-transitive` and fails the job if its output contains "has the following vulnerable packages".
3. Frontend: add public/_headers with the four headers from section 11.3.

Print the two GitHub secret names the backup workflow needs and the section that explains their values.
```

**Check:** push both repos. In the backend repo on GitHub, Settings, Secrets and variables, Actions: add `BACKUP_DATABASE_URL` and `BACKUP_PASSPHRASE` (section 10.4; the URL needs the Supabase project, so this check completes on deploy day). Then Actions, "Database backup", Run workflow. Expect a green run with a `wmhlms-<run id>` artifact.

---

## C2. Deploy-day helper

Model: Sonnet 5. No files change. Use it once you have the Supabase pooler string and know the two origins (real domain or the platform hostnames from section 11.0).

Replace the four angle-bracket values. Replace the database password inside the pooler string with `XXXX` before pasting; keep the real one in your password manager.

```
You are working in the folder `demo/`. Read `PRODUCTION-REVIEW.md` sections 10.1, 10.2, 11.2 and 11.3. Do not change any files.

I am deploying today. Facts:
- SPA origin: <https://... or https://<project>.pages.dev>
- API origin: <https://... or https://<service>.up.railway.app>
- Supabase session-pooler connection string with the password replaced by XXXX: <Host=...;Port=5432;...>
- Root manager email: ayoub.khamil@watermelon-hub.com

Print, as plain KEY=value lines I can paste into Railway's Raw Editor, every variable from section 10.1 that is required in production, with these facts filled in, `Jwt__Key` shown as GENERATE-48-RANDOM-CHARS, `RootManager__Password` as CHOOSE-A-STRONG-PASSWORD, and the pooler password left as XXXX. Then print the Cloudflare Pages production variables from section 10.2. Then print the two curl commands from section 11.2 step 6 with the real API origin. Then list, in order, the Railway settings from 11.2 steps 3 and 4 and the Pages settings from 11.3 step 2, as a checklist.
```

**Check** (after Railway and Pages are up):

```bash
curl -si https://<api origin>/health/ready | head -1
```

Expect `200`. Open the SPA origin, log in as root. Then section 13.4 step 9: log in from a phone on mobile data.

---

## C3. Restore rehearsal and production smoke test (you)

1. Section 12.6 against a second, throw-away Supabase project. Write down how long steps 1 to 4 took, then delete the rehearsal project.
2. Section 13.4, all ten steps, against production.
3. Enter the real course content (you or a manager).

**Milestone: LAUNCH-SAFE.**

If anything fails in step 2, use the fix-up prompt at the end of `A-local-build.md`, then push; Railway and Pages redeploy from `main` automatically.
