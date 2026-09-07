# Phase D: structure (after launch)

Fifteen sessions. Every block below is one session. D2 has six blocks and D5 has seven; run them in the order printed, one per session, check between each. Start each session with any running API or dev server stopped and `git status --short` empty in both repos.

Common preamble used by every block (already included in each; shown here only so you know what it says):

> Work in `demo/`, two repos, read `PRODUCTION-REVIEW.md` first, do only the task, no unrelated refactors, no new packages beyond those named, anti-goals apply, conventions in sections 6 and 7, keep the app runnable, paste build and test output, commit per logical change, do not push, ask if the document and code disagree.

---

## D1. Backend split, part 1: moves only

Model: Opus 5. Backend only.

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8, WmhLms.sln) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` first; sections 5.1 and 6 are the target. Rules: do only the task below; pure moves and splits, no behaviour change; add no packages; anti-goals apply. Build and test after each numbered part and commit after each. Paste the last ten lines of `dotnet build` and `dotnet test` when done. Do not push. If a move would change the database schema, stop and ask me. Windows machine, Git Bash; folder names contain spaces, quote paths.

Task: step 17 of section 14, in `antigravity backend demo`.
1. Move every entity from WmhLms.Data/Entities to WmhLms.Core/Domain, one class per file, namespace WmhLms.Core.Domain. Add the constants classes UserRole, UserStatus, CourseStatus, ItemType, QuestionType, AssignmentStatus and replace the string literals in services and controllers with them.
2. Split Dtos.cs into one record per file under the feature folders named in section 5.1. Split UserServices.cs, Support.cs, CatalogControllers.cs, AuthController.cs, SecurityHeadersMiddleware.cs and Assignment.cs into one class per file. Move RecordViewRequest out of LearnController.cs. Rename Base.cs to ApiController.cs and the class to ApiController.
3. Split Program.cs into the Startup/ extension classes listed in section 5.1; Program.cs must end under 60 lines.
4. Run `dotnet ef migrations add SchemaCheck --project WmhLms.Data --startup-project WmhLms.Api`. If the generated Up method is empty, remove that migration again with `dotnet ef migrations remove`. If it is not empty, stop and show me the migration.
```

**Check** (in `antigravity backend demo`):

```bash
dotnet test --nologo -v q 2>&1 | tail -1; wc -l < WmhLms.Api/Program.cs; ls WmhLms.Core/Domain | wc -l; ls WmhLms.Data/Entities 2>/dev/null | wc -l; ls WmhLms.Data/Migrations | wc -l
```

Expect `Passed!`, a number under 60, `15`, `0`, `3`.

---

## D2. Backend split, part 2: one aggregate per session

Model: Fable 5.1. Backend only. Six sessions, in this order.

### D2.1 Users and Auth

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8, WmhLms.sln) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` first; sections 4.3, 5.1, 6 and 6.5 are the target. Rules: do only the task below for ONE aggregate; other aggregates keep using AppDbContext directly until their own session; no unrelated refactors; no new packages; anti-goals apply; no generic repository, no specification pattern, no IQueryable leaving Data. Existing tests must keep passing using the real Data repositories over the SQLite TestDb; do not mock repositories. Build, test, paste the last ten lines of each, commit. Do not push. Ask if the document and code disagree. Windows machine, Git Bash; quote paths.

Task: step 18 of section 14 for the Users and Auth aggregate.
1. Add IUserRepository (section 6.5) and IUnitOfWork to WmhLms.Core/Abstractions. Add UnitOfWork from section 6.5 to WmhLms.Data (it maps Postgres unique violations, SqlState 23505, to ApiException.Conflict).
2. Implement UserRepository in WmhLms.Data/Repositories as internal sealed. Create WmhLms.Data/DependencyInjection.cs with AddDataAccess(IServiceCollection, string connectionString) that registers AppDbContext, UnitOfWork and UserRepository; the Startup code calls it.
3. Rewrite UserService and AuthService to depend on IUserRepository, IUnitOfWork, IPasswordHasher (rename IPasswordService), ITokenService and TimeProvider. Move Guard-based validation into a Validated() method on CreateUserRequest and UpdateUserRequest as in 6.5. Replace the Dictionary return of GetAssignmentsAsync with UserAssignmentDto and the Login Dictionary with LoginResponse.
4. Add IAccountStatusReader to Core, implement it in Data, and make the JWT OnTokenValidated handler (Auth/AccountStatusValidator.cs) use it instead of AppDbContext. Delete DevController.
5. Update UserServiceTests to construct the service through the Data repositories over TestDb.
```

### D2.2 Audit

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8, WmhLms.sln) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` first; sections 4.3, 5.1, 6 and 6.5 are the target. Rules: do only the task below for ONE aggregate; other aggregates keep their current data access until their own session; no unrelated refactors; no new packages; anti-goals apply; no generic repository, no IQueryable leaving Data. Existing tests must keep passing using the real Data repositories; do not mock. Build, test, paste the last ten lines of each, commit. Do not push. Ask if the document and code disagree. Windows machine, Git Bash; quote paths.

Task: step 18 of section 14 for the Audit aggregate. Add IAuditRepository (Add, RecentAsync) to Core, AuditRepository to Data, register it in AddDataAccess. AuditService depends on IAuditRepository and TimeProvider; UserService now calls audit through the repository as in section 6.5. AuditController returns AuditEntryDto (new) instead of a Dictionary; the mapping lives in an AuditMapper in Core. Move AuditActions into its own file if not already.
```

### D2.3 Courses

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8, WmhLms.sln) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` first; sections 4.3, 5.1, 6 and 6.5 are the target. Rules: do only the task below for ONE aggregate; other aggregates keep their current data access until their own session; no unrelated refactors; no new packages; anti-goals apply; no generic repository, no IQueryable leaving Data. Build, test, paste the last ten lines of each, commit. Do not push. Ask if the document and code disagree. Windows machine, Git Bash; quote paths.

Task: step 18 of section 14 for the Courses aggregate. Add ICourseRepository to Core with intention-revealing methods derived from the queries CourseService runs today (paged list with status and search, GetTreeAsync, FindAsync, FindSectionAsync, FindItemWithQuestionsAsync, FindQuestionWithOptionsAsync, NextSectionOrderAsync, NextItemOrderAsync, Add/Remove for each level, and the quiz-lock cleanups it performs on delete via IQuizLockRepository, which you add now with only the methods Courses needs). Implement CourseRepository and QuizLockRepository in Data, register them. Split CourseService into CourseService, SectionService, ItemService and QuestionService as section 5.1 lists; the option-shape rules from BuildOptions live in QuestionService. Split CoursesController's siblings accordingly (they already are separate controllers; wire each to its service). Mapping stays in CourseMapper.
```

### D2.4 Assignments

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8, WmhLms.sln) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` first; sections 4.3, 5.1, 6 and 6.5 are the target. Rules: do only the task below for ONE aggregate; the Learn aggregate keeps its current data access until its own session; no unrelated refactors; no new packages; anti-goals apply; no generic repository, no IQueryable leaving Data. Build, test, paste the last ten lines of each, commit. Do not push. Ask if the document and code disagree. Windows machine, Git Bash; quote paths.

Task: step 18 of section 14 for the Assignments aggregate. Add IAssignmentRepository to Core (ListByCourseAsync, ListByAgentAsync, FindAsync(courseId, agentId), ExistingAgentIdsAsync(courseId, agentIds), AddRange, RemoveRange) and extend IQuizLockRepository with the unassign cleanup. Implement in Data, register. AssignmentService depends on the repositories, IUserRepository (for known agent ids and names) and IUnitOfWork; GetCourseAssignmentsAsync returns CourseAssignmentDto instead of a Dictionary.
```

### D2.5 Learn

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8, WmhLms.sln) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` first; sections 4.3, 5.1, 6 and 6.5 are the target. Rules: do only the task below for ONE aggregate; no unrelated refactors; no new packages; anti-goals apply; no generic repository, no IQueryable leaving Data. LearnServiceTests must keep passing using the real Data repositories over TestDb; do not mock. Build, test, paste the last ten lines of each, commit. Do not push. Ask if the document and code disagree. Windows machine, Git Bash; quote paths.

Task: step 18 of section 14 for the Learn aggregate. Split LearnService into LearnDashboardService, CourseProgressService and QuizService, plus a pure QuizGrader (questions + answers to incorrect ids, no I/O) and ProgressReconciler (today's Reconcile and ApplyCompletion). They depend on ICourseRepository (GetTreeAsync, published trees by ids), IAssignmentRepository, IQuizLockRepository, IUnitOfWork, TimeProvider and IOptions<QuizOptions> (new, bound from the Quiz section; no IConfiguration in Core). Replace every object or Dictionary return with the DTOs named in section 5.1 (ResumeDto, CompleteItemResultDto, QuizLockStatusDto). Split LearnServiceTests into QuizServiceTests and CourseProgressServiceTests with the same assertions.
```

### D2.6 Flip the references, add the architecture test

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8, WmhLms.sln) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` first; sections 4.3 and 13.3 are the target. Rules: do only the task below; no unrelated refactors; no new packages; build, test, paste the last ten lines of each, commit. Do not push. Windows machine, Git Bash; quote paths.

Task: finish step 18. In `antigravity backend demo`: remove the WmhLms.Data project reference from WmhLms.Core.csproj and add a WmhLms.Core reference to WmhLms.Data.csproj. Remove every `using Microsoft.EntityFrameworkCore` and `using WmhLms.Data` from WmhLms.Api and WmhLms.Core; the only Data symbol the Api may use is AddDataAccess. Move the health-check registration and its package into AddDataAccess. Add IDatabaseInitializer (Core) and DatabaseInitializer (Data) so Startup/DatabaseStartup.cs migrates without touching AppDbContext. Add WmhLms.Tests/Architecture/LayeringTests.cs from section 13.3 and move the existing test classes under Unit/. Confirm `dotnet build` shows no warnings about unused references.
```

**Check** (after every D2 session, in `antigravity backend demo`):

```bash
dotnet test --nologo -v q 2>&1 | tail -1; grep -rln "AppDbContext\|Microsoft.EntityFrameworkCore" WmhLms.Api WmhLms.Core --include=*.cs | wc -l
```

Expect `Passed!`. The second number must go down each session and be `0` after D2.6. After D2.6 also run the API once against Docker Postgres and log in.

---

## D3. Integration tests

Model: Opus 5. Backend only.

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8, WmhLms.sln) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` first; section 13.2 is the target. Rules: do only the task below; no unrelated refactors; no packages beyond Microsoft.AspNetCore.Mvc.Testing which is already referenced. Build, test, paste the last ten lines, commit. Do not push. Windows machine, Git Bash, Docker Desktop running; quote paths.

Task: step 19 of section 14. Add WmhLms.Tests/Integration/ApiFactory.cs (WebApplicationFactory<Program> pointed at a wmhlms_test database on the local Docker Postgres via ConnectionStrings__Default, RootManager__* test values, Security__RequireHttps=false, Cors__Origins__0 set; EnsureDeleted then Migrate once per test class through the test project's own reference to WmhLms.Data). Add the seven tests from the 13.2 table as AuthTests.cs and RoleEnforcementTests.cs, using HttpClient only. Skip the integration tests with a clear reason when the database is unreachable so unit tests still run without Docker. Add a `services: postgres:17-alpine` block to .github/workflows/ci.yml with POSTGRES_DB wmhlms_test and health options, and set ConnectionStrings__Default in the test step.
```

**Check** (in `antigravity backend demo`):

```bash
docker compose up -d db && dotnet test --nologo -v q 2>&1 | tail -1
```

Expect `Passed!` with at least 42 tests and `Skipped: 0`.

---

## D4. Frontend restructure

Model: Opus 5. Frontend only.

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` first; sections 5.2 and 7 are the target. Rules: do only the task below; moves and extractions with no behaviour change; the only new packages are eslint, eslint-plugin-react and eslint-plugin-react-hooks as dev dependencies; anti-goals apply; Tailwind classes stay exactly as they are in this session. Build and lint after each numbered part and commit after each. Paste the last ten lines of `npm run lint` and `npm run build` when done. Do not push. Windows machine, Git Bash; quote paths.

Task: step 20 of section 14, in `antigravity frontend demo`.
1. Create app/, pages/, features/{auth,learn,courses,assignments,users}/, shared/{api,components,hooks,layout,theme,styles,logging}/ and move every existing file to the location the 5.2 tree gives it, updating imports. Split App.jsx into App, AppRoutes, AppLayout, HomeRedirect, ManagerOnly and paths.js (from appRoutes.js). Split Sidebar.jsx into CurriculumSidebar (features/learn) and ManagerSidebar (shared/layout). Delete services/api.js; components import their feature's *Api.js directly.
2. Extract UserControls (name + sign out + theme toggle, currently pasted in AppLayout, TopNav and AgentDashboard), LoadingState, and the sub-components the tree names: CourseCard, QuizQuestion, QuizLockBanner, SectionCard, QuestionForm, CohortSummary, EnrollAgentsModal, UserTable, UserForm, PasswordFields, UserAssignmentsModal, youtubeEmbed.js (one copy of getEmbedUrl).
3. Add shared/hooks/useAsyncData.js from section 7.4 and use it in every screen that has its own loading/error/reload state. Lift the course tree load into pages/agent/CoursePage.jsx and pass it to both CourseViewer and CurriculumSidebar.
4. Add eslint.config.js (flat config, react and react-hooks plugins, exhaustive-deps as error), an `npm run lint` script, and a lint step in .github/workflows/ci.yml before the build. Fix what lint reports.
```

**Check** (in `antigravity frontend demo`):

```bash
npm run lint 2>&1 | tail -3; npm run build 2>&1 | tail -1; ls src/components src/services src/context 2>/dev/null | wc -l; ls src/features | wc -l
```

Expect no lint errors, a successful build, `0`, `5`. Then `npm run dev` and click through dashboard, course, editor, users: everything renders as before.

---

## D5. CSS Modules, one target per session

Model: Opus 5. Frontend only. Seven sessions, in this order. Each block is identical except for the target line and, in the last one, the Tailwind removal.

### D5.1 shared/components (creates the token file)

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` first; section 7.6 is the target. Rules: do only the task below for ONE target folder; components outside it keep their Tailwind classes untouched; no new packages; no raw hex values in any module (tokens only); inline style only for progress widths and the sidebar width; dark mode through the tokens under html.dark. Run the dev server and check every converted component in light and dark mode, and describe what you checked. Lint, build, paste the last ten lines, commit per component. Do not push. Windows machine, Git Bash; quote paths.

Task: step 21 of section 14 for the target `src/shared/components` (Button, Badge, Modal, Select, EmptyState, ErrorBanner, LoadingState). First create src/shared/styles/tokens.css and global.css from section 7.6 (global.css takes over what index.css does today) and import them in main.jsx. Then convert each component in the target to a co-located .module.css following the migration recipe in 7.6.
```

### D5.2 features/auth

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` first; section 7.6 is the target. Rules: do only the task below for ONE target folder; components outside it keep their Tailwind classes untouched; no new packages; no raw hex values in any module (tokens only); inline style only for progress widths and the sidebar width; dark mode through the tokens. Run the dev server and check every converted component in light and dark mode, and describe what you checked. Lint, build, paste the last ten lines, commit per component. Do not push. Windows machine, Git Bash; quote paths.

Task: step 21 of section 14 for the target `src/features/auth`. The login background image moves from an inline style to background-image in LoginForm.module.css.
```

### D5.3 features/learn

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` first; section 7.6 is the target. Rules: do only the task below for ONE target folder; components outside it keep their Tailwind classes untouched; no new packages; no raw hex values in any module (tokens only); inline style only for progress widths and the sidebar width; dark mode through the tokens. Run the dev server and check every converted component in light and dark mode, and describe what you checked. Lint, build, paste the last ten lines, commit per component. Do not push. Windows machine, Git Bash; quote paths.

Task: step 21 of section 14 for the target `src/features/learn`. Use the CourseCard example in section 7.6 as the model.
```

### D5.4 features/courses

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` first; section 7.6 is the target. Rules: do only the task below for ONE target folder; components outside it keep their Tailwind classes untouched; no new packages; no raw hex values in any module (tokens only); inline style only for progress widths and the sidebar width; dark mode through the tokens. Run the dev server and check every converted component in light and dark mode, and describe what you checked. Lint, build, paste the last ten lines, commit per component. Do not push. Windows machine, Git Bash; quote paths.

Task: step 21 of section 14 for the target `src/features/courses`.
```

### D5.5 features/assignments

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` first; section 7.6 is the target. Rules: do only the task below for ONE target folder; components outside it keep their Tailwind classes untouched; no new packages; no raw hex values in any module (tokens only); inline style only for progress widths and the sidebar width; dark mode through the tokens. Run the dev server and check every converted component in light and dark mode, and describe what you checked. Lint, build, paste the last ten lines, commit per component. Do not push. Windows machine, Git Bash; quote paths.

Task: step 21 of section 14 for the target `src/features/assignments`.
```

### D5.6 features/users

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` first; section 7.6 is the target. Rules: do only the task below for ONE target folder; components outside it keep their Tailwind classes untouched; no new packages; no raw hex values in any module (tokens only); inline style only for progress widths and the sidebar width; dark mode through the tokens. Run the dev server and check every converted component in light and dark mode, and describe what you checked. Lint, build, paste the last ten lines, commit per component. Do not push. Windows machine, Git Bash; quote paths.

Task: step 21 of section 14 for the target `src/features/users`. The FIELD_CLASS, SECTION_LABEL_CLASS and FIELD_LABEL_CLASS constants become classes in the module.
```

### D5.7 app, shared/layout, and remove Tailwind

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` first; section 7.6 is the target. Rules: do only the task below; no new packages; no raw hex values in any module (tokens only); inline style only for progress widths and the sidebar width; dark mode through the tokens. Run the dev server and check every screen in light and dark mode, and describe what you checked. Lint, build, paste the last ten lines, commit per component and one final commit for the removal. Do not push. Windows machine, Git Bash; quote paths.

Task: step 21 of section 14 for the targets `src/app` and `src/shared/layout`, then remove Tailwind: delete tailwind.config.js, postcss.config.js, the three @tailwind directives, the `class` attributes on <body> in index.html, and the tailwindcss, postcss and autoprefixer dev dependencies (run npm install so the lockfile updates). Then search the whole of src for any remaining utility-class string (patterns like `bg-`, `text-zinc`, `dark:`, `flex `, `px-`) and convert what is left. The final `npm run build` must succeed with no Tailwind in package.json.
```

**Check** (after each D5 session, in `antigravity frontend demo`; replace TARGET with the folder just converted):

```bash
grep -rn "#[0-9a-fA-F]\{6\}" src/TARGET --include=*.css | grep -v tokens.css | wc -l; grep -rln "bg-\[\|text-zinc\|dark:" src/TARGET | wc -l; npm run build 2>&1 | tail -1
```

Expect `0`, `0`, successful build. Open the converted screens in both themes yourself. After D5.7 additionally:

```bash
grep -rn "tailwind" package.json src index.html | wc -l
```

Expect `0`.

---

## D6. Audit trail screen

Model: Opus 5. Both repos.

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8, WmhLms.sln) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` first. Rules: do only the task below; conventions in sections 6 and 7 (CSS Modules with tokens for the new screen); no new packages; build, test, lint, paste the last ten lines, commit per logical change. Do not push. Windows machine, Git Bash; quote paths.

Task: step 22 of section 14 (W15). Backend: record course.created, course.updated, course.published, course.unpublished, course.deleted, section.created/updated/deleted/reordered, item.created/updated/deleted/reordered, question.created/updated/deleted, assignment.assigned and assignment.unassigned through AuditService in the corresponding services, with target labels a manager would recognise (titles, agent emails). Controllers pass the caller id into these service methods where they do not already. Add a unit test per service asserting an entry is written for one action. Frontend: a root-only page at /manager/audit under features/audit with a paged, read-only table (actor, action, target, detail, time) using the existing GET /api/audit, and a sidebar link shown only when user.is_root.
```

**Check:** as root, delete a test course, open Manager Console, Audit: a `course.deleted` row with the course title. As a non-root manager: no link, and

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5000/api/audit -H "Authorization: Bearer MANAGER_TOKEN"
```

Expect `403`.

---

## D7. Course import and export

Model: Opus 5. Both repos.

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8, WmhLms.sln) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` first; section 15.2 is the specification. Rules: do only the task below; conventions in sections 6 and 7; no new packages; no server-side file storage (the JSON is the request body); build, test, lint, paste the last ten lines, commit per logical change. Do not push. Windows machine, Git Bash; quote paths.

Task: step 23 of section 14, exactly as section 15.2 specifies: the import request records (one file each), CourseImportService, CourseExportMapper, ICourseRepository.AddTree, POST /api/courses/import and GET /api/courses/{id}/export, the "Import course" file picker on the courses list (reads the .json with FileReader and posts it), the "Export" action on the course editor (downloads the JSON), and the two tests. The import must reuse the option-shape validation the question endpoints use and create the course as draft in one SaveChangesAsync.
```

**Check:** export the demo course to a file, delete the course, import the file, open it in the editor: same sections, items, questions and correct answers.

```bash
dotnet test --nologo -v q 2>&1 | tail -1
```

Expect `Passed!`.

---

## D8. Remaining small items

Model: Sonnet 5. Both repos.

```
You are working in the folder `demo/`. It contains two git repositories: `antigravity backend demo` (ASP.NET Core 8, WmhLms.sln) and `antigravity frontend demo` (React 18 + Vite). Read `PRODUCTION-REVIEW.md` first. Rules: do only the four items below, one commit each; no new packages; build, test, lint, paste the last ten lines. Do not push. Windows machine, Git Bash; quote paths.

Task: step 24 of section 14.
1. Frontend: switch HashRouter to BrowserRouter in main.jsx and add public/_redirects containing `/* /index.html 200`.
2. Frontend: move the three images from /assets into src/assets with no spaces in the filenames and update the imports.
3. Backend and frontend: trim the learn dashboard payload (section 15, item L4): LearnBucketsDto courses carry total_items and completed_item_ids but no sections; update AgentDashboard and CurriculumSidebar if they read sections from that payload.
4. Backend: document Supabase CA pinning (section 8.3 and L8) as a commented COPY line in the Dockerfile and a short note in PRODUCTION-REVIEW.md section 8.3, without enabling it.
```

**Check:** `npm run build`, `npm run preview`, open `http://localhost:4173/learn` directly (no `#`): the dashboard loads; logos render in both themes.

```bash
curl -s "http://localhost:5000/api/learn/courses?agent_id=2" -H "Authorization: Bearer TOKEN" | grep -c '"sections"'
```

Expect `0`.

**Everything in the brief is now done.**
