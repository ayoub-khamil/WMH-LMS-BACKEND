# To Be Implemented

Queued backend work. One section per feature, newest at the bottom.

---

## 1. Server-generated completion certificates

**Why:** certificates are currently built client-side with jsPDF
(`frontend: src/services/certificate.js`). The backend only supplies
inputs, so completion is never verified and IDs are throwaway
(`Date.now()` suffixes).

**Scope:**

1. New `Certificates` table: `id` (stable public ID, e.g. `WMH-{courseId}-{suffix}`),
   `agent_id`, `course_id`, `issued_at`. One row per completion; re-downloads
   return the same document.
2. New endpoint `GET /api/learn/courses/:id/certificate?agent_id=`
   (auth required). Must reject unless that agent's assignment for the
   course has `status = completed` — never trust the client's modal.
3. Generate landscape-A4 PDF with QuestPDF, replicating the current
   layout: borders, `WATERMELONHUB LMS — BPO OPERATIONS ACADEMY` header,
   agent name, course title, issue date (from `completed_at`),
   certificate ID, `Score: 100%`, signature block.
4. Return `application/pdf` with content-disposition attachment.
5. Frontend swap: `downloadCertificatePDF` becomes an authenticated fetch
   + blob download. Buttons and UX stay identical.
6. Demo data: none needed. Existing completions issue certificates lazily
   on first download.

---

## 2. Plain-text content model (decided)

**Decision:** text items are plain text, rendered verbatim
(`whitespace-pre-wrap`, no HTML parsing). The manager-side live render
preview is removed; the textarea placeholder states that what is typed
is what agents see.

**Remaining work:**

- Backend: strip/reject HTML tags in `text_content` on item create/update
  (defense in depth; the editor no longer suggests formatting exists).
- Seed data already converted to plain text (`DemoSeed`). Any future seed
  content must follow suit. Existing databases with HTML-tagged rows keep
  rendering the tags literally until edited.

## 3. Agent dashboard upgrades

- Search/sort on the course list (the All tab becomes a wall as courses grow).
- Header row responsive rework for small screens (logo + tabs + user controls crowd).
- Expandable long course descriptions (currently hard-truncated at two lines).
- Honest "unavailable" state for courses unpublished mid-training
  (backend currently filters to published, so in-progress courses vanish
  silently). Needs backend + UI.

## 4. Learning-flow verification

- Execute the quiz *pass* path and all authoring write paths
  (user/course/section/item/question CRUD) end-to-end against the API.
  They compile and follow tested patterns but have never been run.

## 5. Data and contract hardening

- Trim `sections` from the `learn/courses` dashboard payload (dead weight
  the UI discards). Backend change + rebuild/restart.
- Stop sending `is_correct` per quiz option to clients (currently leaks
  the answer key; the `?autofill` test helper depends on it, so replace
  the helper first).
- `course_id` deep-links from the user-assignments modal rows.

## 6. Auth and security (real build, not demo)

- httpOnly cookies vs localStorage JWT, refresh-token rotation,
  password reset and invite flows.
- Backend role-enforcement audit (client-side guards are courtesy only).
- PasswordHasher is correctly in place; keep it and never log plaintext.

## 7. Quality

- Test coverage: unit tests for grading/progress math, browser journeys
  for auth, CRUD, and quiz-lock flows (including reload mid-lock).
