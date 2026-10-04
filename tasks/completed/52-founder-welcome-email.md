---
title: 'Personal founder welcome email at signup'
purpose: 'Spec for a plain, personal-looking welcome email from the founder that is sent automatically a day after a school signs up, and openly says so.'
description: >-
  A day after a school completes self-serve signup, send the new admin a short email
  written by Niels in his own voice: plain text look, sent from his name, replies
  go straight to his inbox. The email openly says it was sent automatically but
  written by hand. Modelled on the onboarding emails from PingPuffin and Alunta.
status: 'Proposed'
---

# Personal founder welcome email at signup

## TL;DR

About 24 hours after `SchoolSignupService.CreateAsync` succeeds, send one email to the new admin. It looks like a personal email, not a newsletter: no logo, no branded card, no button. From "Niels Pilgaard", `Reply-To` `niels@skoleoverblikket.dk`. First paragraph admits it is sent automatically. It ends with an open question so people reply. A failed send must never fail signup.

## Context

Signing up for PingPuffin and Alunta both produce an email from a founder right after signup. They read as personal, they are honest about being automated ("Full disclosure; this email is automatically sent to you, but handwritten nonetheless"), and they invite a reply that lands in the founder's inbox.

That fits us well. Our buyer is Hanne or a principal at a small friskole. A real person they can write to lowers the bar for asking questions in the trial, and every reply is direct feedback from a customer. Today signup sends no email at all.

## Scope

### In scope

1. **Trigger**: send once, about 24 hours after signup. `SchoolSignupService.CreateAsync` sets `School.WelcomeEmailDueAt` to signup + 24h when the school is saved, and the hourly `WelcomeEmailJob` sends due emails and clears the field. Not on any failure path (`EmailTaken`, `AccountFailed`, `SaveFailed`), since no school is saved. Existing schools have no due time and are never emailed.
2. **Recipient**: `req.AdminEmail`, greeted by `req.AdminFirstName`.
3. **Sender**:
   - Display name `Niels Pilgaard`.
   - From address on the verified Scaleway TEM domain (`skoleoverblikket.dk`), so deliverability is unchanged.
   - `Reply-To`: `niels@skoleoverblikket.dk`.
   - No phone number in the signature.
4. **Look**: plain-text body plus a minimal HTML body (paragraphs and line breaks, default font, no `EmailTemplate.Wrap`, no logo, no images, no tracking pixels). It should look like it was typed in a mail client.
5. **Honesty**: the email says, in the first or second paragraph, that it was sent automatically and that Niels wrote it himself and reads replies.
6. **Failure handling**: signup never sends, so it cannot fail on email. A failed send is logged and retried every hour, and given up after 3 days.

### Out of scope

- Drip sequences, follow-ups during the trial, or any marketing automation.
- Welcome emails for invited staff, parents or board members. They already get invitation emails.
- Unsubscribe handling. This is a one-off transactional email tied to account creation.

## Email copy (draft, Danish)

Niels should rewrite this in his own words before shipping. The structure is what matters: who I am, honest disclosure, why I built it, open question, reply invitation.

**Subject**: `Velkommen til Skoleoverblikket, {Fornavn}`

```text
Hej {Fornavn},

Jeg hedder Niels, og det er mig, der har bygget Skoleoverblikket.

For at være ærlig: Den her mail bliver sendt automatisk dagen efter, at en skole er blevet oprettet. Men jeg har skrevet den selv, og hvis du svarer, lander dit svar direkte i min indbakke. Jeg læser og svarer på alle mails personligt, oftest samme dag.

Tak, fordi I har oprettet {Skolenavn}. Jeg har lavet Skoleoverblikket, fordi skoler bruger alt for meget tid og alt for mange penge på administration. Den tid skulle hellere bruges på eleverne.

Hvad skal der til, for at Skoleoverblikket bliver en rigtig god hjælp hos jer?

Hvis noget er svært at finde ud af, eller hvis I mangler noget, så skriv bare tilbage. Ingen spørgsmål er for små.

Venlig hilsen
Niels
Skoleoverblikket
```

`{Fornavn}` and `{Skolenavn}` are HTML-encoded in the HTML body.

## Implementation notes

- **`EmailMessage`** ([IEmailSender.cs](../../api/Skoleoverblikket.Api/Email/IEmailSender.cs)): add optional `FromName` and `ReplyTo`. `MailKitEmailSender` uses `FromName ?? _options.FromName` and sets `mime.ReplyTo` when given. Existing callers stay unchanged.
- **`WelcomeEmail.cs`** in `api/Skoleoverblikket.Api/Email/`, next to `StaffInvitationEmail.cs` and `ParentInvitationEmail.cs`. A static builder returning an `EmailMessage` with both bodies. Sender name and reply-to address are constants here, not config. They are not tenant-specific and there is one founder.
- **`SchoolSignupService`**: inject `IEmailSender` and `ILogger`. Send after the save, following the service rule "load, check, change, save once, then trigger side effects". Wrap in try/catch so an SMTP outage cannot turn a created school into a signup error.
- **Migration** for `School.WelcomeEmailDueAt`. No new config. The email is in the backoffice email preview (`/api/v1/admin/email-preview/welcome`).
- **Landing page / changelog**: not a user-facing feature card. Use a `feat:` commit subject that reads well in `/nyheder` anyway, or `chore:` if it shouldn't appear there.

## Tests

API integration tests through HTTP, using `RecordingEmailSender` ([RecordingEmailSender.cs](../../api/tests/Skoleoverblikket.Api.IntegrationTests/Infrastructure/RecordingEmailSender.cs)). Put them in the existing signup test file if there is one, otherwise a new `SchoolSignupWelcomeEmailTests.cs`.

- Successful signup records no email at once and none before 24 hours, then exactly one email to the admin address (never twice), with the founder `ReplyTo`, the first name in the body, and no `EmailTemplate` markup.
- Signup with an email that is already taken records no welcome email.
- The body contains the automated-sending disclosure. One assertion on a stable phrase is enough, so copy edits don't break the test.

No Playwright test. Hanne isn't blocked if this email breaks.

## Acceptance criteria

- [ ] New school signup sends one personal-looking welcome email to the admin.
- [ ] The email says it is sent automatically, written by Niels, and that replies reach him.
- [ ] Replying in Gmail/Outlook addresses `niels@skoleoverblikket.dk`, not `kontakt@`.
- [ ] Email renders as plain text in Gmail, Outlook and Apple Mail (no card, logo or button).
- [ ] The email goes out about 24 hours after signup, not at signup.
- [ ] An SMTP failure is logged and retried; signup never fails on email.
- [ ] Integration tests above pass. `/verify` passes.

## Decisions

1. **Reply-to address**: `niels@skoleoverblikket.dk`. It must exist and be monitored before this ships.
2. **Sender display name**: `Niels Pilgaard`.
3. **Signature**: no phone number. Niels can only take calls after 15:00 on weekdays and at weekends, so email is the channel.
4. **Response time**: say "oftest samme dag" (usually the same day). It reassures without guaranteeing. Never promise "same day" outright.
