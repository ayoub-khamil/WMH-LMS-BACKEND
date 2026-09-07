# Read me first (2 minutes)

## The files

| File | What it gets you | Sessions |
|---|---|---|
| `A-local-build.md` | The finished app running on your PC | 3 + one hour of clicking by you |
| `B-hardening.md` | Extra safety and the small tweaks you asked for | 2 |
| `C-deploy.md` | The app live on the internet | 2 + you in the hosting dashboards |
| `D-structure.md` | Clean, tidy code for the long term | 15 |

## Before you start

- Docker Desktop is open. (Done.)
- Ports 5432 and 8080 are free. (Done.)
- Before **A3 only**: you create two small secret files by hand. The A3 section tells you exactly what to put in them. Never let Claude create them.

## Every session, same 5 moves

1. **Open a new Claude Code session** on the `demo` folder. Close the old one.
2. **Copy one grey block** from the phase file. Paste it into Claude. Send.
3. **Wait.** When Claude says done, it should show build and test output. If it does not, reply: `Paste the last ten lines of dotnet test and npm run build before I accept this.`
4. **Run the Check** written under that block, in Git Bash. It tells you what you should see.
5. **Green?** Run `git push origin main` in the repo that changed. Close the session. Next block.
   **Red?** Stay in the same session and paste the failing output after this line: `This check failed. Fix it without touching anything else, then tell me what was wrong.` Run the check again.
   **Red twice?** Close the session, run `git checkout -- .` in that repo, and start the same block again fresh.

## Rules of thumb

- Never paste a password, key, or connection string into Claude.
- One block per session. Do not add "and also".
- If Claude asks a question, the answer is probably in `PRODUCTION-REVIEW.md` section 16. If it is not, decide, answer, and write your answer into section 16 so it is not asked again.
- Stop any running app (Ctrl+C in its terminal) before starting the next session.
- Which model: Fable 5.1 for A1 and all of D2. Opus 5 for everything else. Sonnet 5 is fine for C1, C2, D1, D8.

That is it. Open `A-local-build.md`, copy the A1 block, go.
