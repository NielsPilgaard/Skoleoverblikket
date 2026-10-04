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

Surfaced by [06-data-retention](completed/06-data-retention.md): the deletion warning promises "log ind og eksporter data", but there is not much to export yet.

## Open questions

- Async (email a link when ready) or streamed directly? Files can be large.
- Include message bodies and kontaktbog threads, or only structured data?
