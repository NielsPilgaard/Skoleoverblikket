---
title: 'Task 35: Class group chat'
status: 'Completed'
description: >-
  Schools today run a Facebook group per klasse as an auxiliary tool to
  Skoleoverblikket, for Q&A, discussion, and sharing documents. Build a
  per-klasse group chat with file uploads directly into the platform so
  schools no longer need Facebook.
purpose: >-
  Implementation spec for a per-klasse group discussion thread (parents +
  assigned staff), with file attachments, automatic membership, and
  notification integration.
---

# Task 35: Class group chat

## TL;DR

One persistent group chat thread per `Class`, auto-membership (all parents of
enrolled students + staff currently teaching that klasse via `SchemaSlot`),
plain-text messages with file attachments (reuse avatar's presign+confirm
OVHCloud pattern), own-message delete + admin/staff moderation delete,
poll/refresh-based (no SignalR), notifications via existing
`NotificationPreference` pattern. No existing staff↔klasse roster exists in
the codebase — this task must build that resolution query from scratch.

## Context

Distinct from existing messaging features — do not conflate:

- **Kontaktbog** (`ContactThreadsController.cs`, `ContactMessage.cs`) — private
  per-child parent↔teacher thread. Notifies *all* tenant staff regardless of
  klasse (`ContactThreadsController.cs:387`), no klasse-scoping exists there.
- **Beskeder** (`MessagesController.cs`, `Message.cs`) — flat 1:1/broadcast
  inbox across all tenant users. `GroupMessage.BroadcastAudience` supports
  `ClassParents` (klasse-scoped parent broadcast,
  `MessagesController.cs:535-545`) but has no `ClassStaff` equivalent —
  staff broadcasts are always `AllStaff` or `StaffByRole`
  (`MessagesController.cs:557,565`), never klasse-scoped.
- **This task** — an ongoing multi-party discussion thread scoped to one
  klasse, closer to a Facebook group than either of the above.

**No staff↔klasse membership table exists anywhere.** Confirmed by repo
search: no `ClassTeacher`/`KlasseStaff` junction. The only staff↔klasse
association is derived live from `SchemaSlot.TeacherId`/`AideId` where
`Schema.ClassId == X` (`api/Skoleoverblikket.Api/Models/SchemaSlot.cs:11-50`,
`Schema.cs:16`), currently duplicated ad hoc per-controller (e.g.
`SchedulesController.cs:89`, scoped the other direction — "slots for this
staff member"). `ClassPermission` (`ClassPermissionsController.cs`) is
**not** reusable for this — it's an opt-in admin write-permission grant list
where an empty table means "unrestricted," not "no staff for this class"
(see doc comment at `ClassPermission.cs:9`).

## Scope decisions (locked)

- **Thread granularity**: one thread per `Class`. No custom/admin-defined
  cross-class groups in this task.
- **Membership — parents**: automatic, all parents of students currently
  enrolled in the klasse. No opt-in/consent step (trusted-forum framing,
  matches "already in the class" reality).
- **Membership — staff**: automatic, staff with a `SchemaSlot` for that
  klasse (`TeacherId` or `AideId`) in the active schema. **Substitutes/vikarer
  excluded** — standing teachers/aides only, not day-specific
  `WeekPlanSlot.SubstituteTeacherId/SubstituteAideId` assignments (too
  transient for persistent thread membership).
- **Identity display**: sender name + avatar always shown on every message.
  `ShareContactInfo` consent (gates the separate Kontakt directory lookup)
  does **not** apply here — posting in a group forum inherently reveals
  identity, same as the Facebook group it replaces. **Needs explicit sign-off
  before implementation**: this exposes name+avatar to every parent/staff in
  the class thread with no consent gate and no way to leave — before
  building, either write the privacy notice shown at first use, define what
  happens when a member wants their history removed, and confirm disclosure
  handling, or get explicit approval to skip that and ship as-is.
- **Content**: plain text only, links auto-detected/linkified. Linkification
  must allowlist `http`/`https` schemes only — `javascript:`, `data:`, and any
  other scheme render as plain text, never as a clickable link. No rich text
  editor, no reactions/emoji in this task.
- **File attachments**: documents + images, reusing the existing
  presign+confirm upload pattern to OVHCloud used for avatars. Apply a
  per-file size cap (e.g. 10–25MB, match/confirm against existing avatar
  cap if one exists). No attachment-type allowlist beyond documents/images
  (no arbitrary/unrestricted file types).
- **Moderation**: sender can delete their own message. Staff/admin assigned
  to that klasse (or any tenant admin) can delete any message in that
  klasse's thread. No edit — delete only.
- **Real-time**: none. Poll/refresh on open, consistent with rest of app
  (no SignalR/websocket infra exists yet — do not introduce it here).
- **Notifications**: new `NotificationType` (e.g. `ClassChatMessage`), reusing
  existing `NotificationPreference` opt-out pattern
  (`NotificationsController.cs`). In-app notification always fires on new
  message. Email defaults **off**; when a user opts in, email fires
  immediately per-message (no digest/batching infra in this task).
- **Multi-class parents/staff**: chat presented as a list of threads (one row
  per klasse the user has access to), user switches between them — same
  pattern as Kontaktbog's per-child thread list. No merged/combined feed.
- **Lifecycle**: thread lifecycle follows the `Class` entity as-is — no new
  year-end archival/rollover logic. Confirm current `Class` year-transition
  behavior before implementation if it's not already load-bearing knowledge.
- **Retention**: no message cap, no auto-deletion, no storage quota logic —
  matches Beskeder/Kontaktbog precedent (neither has a retention policy).
  Storage cost from attachments is a cross-cutting concern, not handled here.

## Proposed scope

1. **Data model**:
   - `ClassChatMessage` (or similar): `TenantId`, `ClassId` (FK), `SenderId`
     (Staff or Parent — check how Kontaktbog models mixed sender identity in
     `ContactMessage.cs` and follow that pattern), `Body` (text), `CreatedAt`,
     `DeletedAt`/`IsDeleted` (soft delete for moderation).
   - `ClassChatAttachment`: `MessageId` (FK), OVHCloud object key, filename,
     content type, size — mirror the avatar presign+confirm entity shape.
   - New EF Core migration via `/add-migration`. Never edit existing
     migrations.
2. **Backend — staff roster resolution** (new, reusable):
   - Add a service/query method (e.g. on a new or existing service) —
     "given `ClassId`, return distinct `Staff` from active `SchemaSlot`
     `TeacherId`/`AideId`" — since no such helper exists today. Use this for
     both membership checks and notification fan-out.
3. **Backend — controller** (new `ClassChatController.cs` or similar):
   - `GET /api/v1/class-chats` — list threads visible to caller (parent: via
     their children's classes; staff: via `SchemaSlot` roster; admin: all).
   - `GET /api/v1/class-chats/{classId}/messages` — paginated message list,
     authorize caller is a member (parent of enrolled student, or in staff
     roster, or admin).
   - `POST /api/v1/class-chats/{classId}/messages` — send message, optional
     attachments (post-confirm upload).
   - `DELETE /api/v1/class-chats/{classId}/messages/{messageId}` — soft
     delete; authorize sender OR admin/staff-in-roster.
   - Presign/confirm endpoints for attachment upload, mirroring avatar upload
     pattern.
   - All queries scoped via `ITenantContext` / global query filter — never
     bypass tenant scoping.
   - `ProblemDetails` for all error responses, JWT auth required throughout.
4. **Backend — notifications**: new `NotificationType`, wire into
   `NotificationsController`/`NotificationPreference` opt-out flow, fan out
   to thread members (parents of enrolled students + roster staff) on new
   message, excluding the sender. Deduplicate recipient IDs before fan-out —
   a parent with multiple enrolled students in the class, or staff reachable
   via more than one roster path, must be notified once, not once per match.
5. **Frontend**:
   - New top-level nav item (phone + laptop both first-class — parents
     primarily on mobile).
   - Thread list view (one row per klasse the user belongs to) →
     conversation view (message list + composer + attachment picker).
   - Reuse generated typed API client after `/codegen`. Tailwind only,
     functional components + hooks.
6. **Codegen**: `/codegen` after controller/DTO changes — never hand-edit
   `web/src/api/generated/*`.

## Open questions / needs confirmation before implementation

- Exact per-file and per-message attachment size caps — check whether an
  existing avatar upload constant should be reused or a new one is needed. (Dev: ~5MB per file, ~4000 chars per message)
- `ContactMessage.cs`'s sender-identity pattern (mixed Staff/Parent sender)
  needs to be read in full before modeling `ClassChatMessage.SenderId` —
  referenced above but not yet confirmed field-for-field.
- Current `Class` year-rollover behavior (new row per year vs. same row
  continuing) — confirm before assuming "no special handling" is safe.

## Testing

- API integration tests (tUnit + Testcontainers): membership resolution
  (parent sees only own children's classes, staff sees only roster classes,
  admin sees all), tenant isolation, send/delete authorization (own message
  vs. admin override), notification fan-out excludes sender, module/consent
  independence (`ShareContactInfo` does not gate visibility).
- Playwright e2e: parent opens class chat, sends message with attachment,
  sees it appear; staff member deletes another user's message (moderation);
  parent with two children sees two separate threads.

## Out of scope

- Custom/admin-defined cross-class groups (only auto per-klasse threads)
- Substitute/vikar thread membership
- Rich text formatting, emoji reactions
- Real-time push (SignalR/websockets)
- Email digest/batching for notifications
- Message retention limits, storage quota enforcement
- Birthday list feature, event scheduling (Calendar module already covers
  scheduling; birthday lists are a separate feature if wanted later)
