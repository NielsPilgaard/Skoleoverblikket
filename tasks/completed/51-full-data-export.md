---
title: 'Full data export before deletion'
purpose: 'Let a school download all its data in one go before it leaves, as the deletion warning email suggests.'
description: >-
  Follow-up from task 06. Canceled schools get a warning 7 days before all
  data is deleted, with a link to /eksporter. That page only exports hours
  reports and the schema as CSV. Add one "download everything" ZIP.
status: 'Proposed'
---

# Full data export before deletion

## TL;DR

`/eksporter` today: Timer pr. medarbejder, Timer pr. fag, Komplet skema, UVM minimumstimetal. Missing: classes, students, parents, staff, calendar, ugeplaner, fravær, beskeder and uploaded files. Add one ZIP with a CSV per table plus the files, so a leaving school (and GDPR art. 20 portability) is covered in one click.

## Context

Surfaced by [06-data-retention](06-data-retention.md): the deletion warning promises "log ind og eksporter data", but there is not much to export yet.

## Decisions

1. **Streamed directly**, not async with an emailed link. `GET /api/v1/exports/school.zip` writes the ZIP to the response as it is built: no job, no stored copy, no extra infra.
2. **Message content is included**: beskeder, kontaktbog and klassechat. The school is data controller, and the export is admin-only.
3. **Generic table dump**: one semicolon CSV per table from the EF model (like `SchoolDeletionService.DeletionOrder`), archived rows included. A new table is exported automatically; one without `TenantId` fails the tests. Invitation tokens are left out, since they are live credentials.
4. **Files under readable names**: `filer/` and `bestyrelse/` keep their folder trees, `klassechat/<klasse>/`, and everything else under the school's storage prefixes (avatars, logo, backups) goes in `andre-filer/`.
