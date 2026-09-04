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

**Note:** the `status = completed` check in step 2 is now trustworthy —
completion can no longer be forged from the client (see section 8).

---

## 2. Plain-text content model (decided)

**Decision:** text items are plain text, rendered verbatim
(`whitespace-pre-wrap`, no HTML parsing). The manager-side live render
preview is removed; the textarea placeholder states that what is typed
is what agents see.

**Remaining work:**

- Backend: strip/reject HTML tags in `text_content` on item create/update
  (defense in depth; the editor no longer suggests formatting exists).
  Deliberately left alone in the hardening pass — it changes what a
  manager can save, so it should land together with the editor copy.
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

## 4. Learning-flow verification — DONE

Executed end-to-end against the running API: the quiz *pass* path, the
fail → cooldown → review → retry path, and every authoring write path
(user/course/section/item/question CRUD, both reorder endpoints). All
green. The harness lives outside the repo; converting it into an xUnit
integration project is section 7.

## 5. Data and contract hardening

- Trim `sections` from the `learn/courses` dashboard payload (dead weight
  the UI discards). Backend change + rebuild/restart.
- Stop sending `is_correct` per quiz option to clients (currently leaks
  the answer key; the `?autofill` test helper depends on it, so replace
  the helper first).
- `course_id` deep-links from the user-assignments modal rows.

Both backend items are still open on purpose: each one changes a payload
the frontend reads, so they belong with the matching frontend change.

## 6. Auth and security (real build, not demo) — PARTLY DONE

- **Done:** backend role-enforcement audit. Identity is taken from the
  bearer token, never from a client-supplied `agent_id`; enrolment is
  required for every learn read and write; item/course membership is
  verified; the quiz cooldown and review gate are enforced server-side;
  only the root account administers managers; endpoints are authenticated
  by default via a fallback authorization policy.
- **Still open:** httpOnly cookies vs localStorage JWT, refresh-token
  rotation, password reset and invite flows.
- PasswordHasher is correctly in place; keep it and never log plaintext.

## 7. Quality

- Unit tests for grading/progress math (`QuizGrading`, `Progress.Percent`,
  the completion reconciliation in `LearnService`).
- An xUnit + `WebApplicationFactory` integration project covering the
  journeys already verified manually in section 4.
- Browser journeys for auth, CRUD, and quiz-lock flows (including reload
  mid-lock).

## 8. Backend hardening pass — DONE

Closed against the existing feature set and API contract:

- Client-supplied `agent_id` no longer widens access (403 on mismatch).
- No self-enrolment; no completion of items that belong to another course.
- Stored progress is reconciled against the live course tree on every read
  and write, so deleted or foreign item ids cannot inflate completion.
- Quiz retake: lock is checked before answer validation; the review gate
  is enforced server-side rather than by the client alone.
- Only root may edit, disable or delete a manager; nobody may disable or
  delete themselves.
- Real foreign keys with cascade, plus unique indexes on
  `(course_id, agent_id)` and `(agent_id, quiz_item_id)`. Quiz locks are
  cleaned up on unassign, item delete, course delete and user delete.
- EF Core migrations replace `EnsureCreated`; schema changes are now
  versioned and applied on boot.
- Global exception handling with logging; unhandled errors no longer leak
  internals outside Development.
- Fallback authorization policy, paging clamps, input validation via a
  shared `Guard`, JWT key validated at startup, N+1 queries removed from
  the agent dashboard and bulk assignment.
