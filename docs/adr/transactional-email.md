---
title: 'ADR: Transactional email provider — Scaleway TEM via SMTP'
status: 'Accepted'
date: '2025-01-01'
authors: 'Niels Pilgaard Grøndahl'
tags: ['infrastructure', 'email']
supersedes: ''
superseded_by: ''
description: >-
  Scaleway Transactional Email over SMTP (via MailKit) in all environments —
  EU-hosted, near-zero cost at expected volume, no provider lock-in since
  switching is a config change.
---

# ADR: Transactional email provider — Scaleway TEM via SMTP

## TL;DR

Scaleway Transactional Email (SMTP relay, port 587 STARTTLS) via MailKit, used in dev/staging/production alike. EU-hosted, ~€1.18/month at 5,000 emails, 300 free/month covers development. Chosen over Brevo, MailerSend, self-hosted Postal, and Postmark/Resend (the last two disqualified outright by GDPR/US-hosting).

## Status

**Accepted**

## Context

Skoleoverblikket sends transactional emails on behalf of the platform: staff invitation emails, password resets, trial expiry reminders, billing notifications. These emails contain personal data, so the provider must be EU-hosted. Volume at launch is low — likely well under 1,000 emails/month for the first schools, growing to ~5,000/month at scale. Cost-efficiency and operational simplicity are the primary drivers.

## Decision

Use Scaleway Transactional Email (SMTP relay) in all environments. Integrate via MailKit over SMTP (port 587, STARTTLS) — no provider SDK required, switching providers later is a config change.

```csharp
using var smtp = new SmtpClient();
await smtp.ConnectAsync("smtp.provider.com", 587, SecureSocketOptions.StartTls);
await smtp.AuthenticateAsync(username, apiKeyOrPassword);
await smtp.SendAsync(message);
await smtp.DisconnectAsync(true);
```

## Consequences

### Positive

- **POS-001**: No provider lock-in — switching is a config change (connection string swap), no application code changes.
- **POS-002**: Near-zero email cost for the first year of production (~€1.18/month at 5,000 emails, no monthly minimum).
- **POS-003**: EU data residency (French cloud provider, Iliad group) — data never leaves the EU, satisfying the personal-data constraint.
- **POS-004**: No branding on outgoing emails on any plan.

### Negative

- **NEG-001**: New Scaleway accounts have a default rate limit of ~300 emails/hour, raiseable via support request if the platform scales faster than that request can be processed.
- **NEG-002**: MailKit adds one NuGet dependency.

## Alternatives Considered

### Brevo

- **ALT-001**: **Description**: Considered for dev-only use due to a generous free tier.
- **ALT-002**: **Rejection Reason**: having two providers (Brevo for dev, Scaleway for prod) adds config complexity for negligible cost saving — Scaleway's 300 free emails/month already covers development.

### MailerSend

- **ALT-003**: **Description**: Previously considered; had a generous free tier historically.
- **ALT-004**: **Rejection Reason**: as of October 2025, MailerSend eliminated its useful free tier for new accounts (caps at 500/month with mandatory branding); first paid tier is $7/month minimum — more expensive than Scaleway's pay-as-you-go at the same volume, with less EU clarity.

### Postal (self-hosted)

- **ALT-005**: **Description**: Self-hosted open-source transactional email server.
- **ALT-006**: **Rejection Reason**: zero licensing cost but significant deliverability risk (cold OVH VPS IP, no managed sender reputation) and ops overhead (MariaDB, RabbitMQ, Redis) — not appropriate for a solo developer.

### Postmark / Resend

- **ALT-007**: **Description**: Popular developer-friendly transactional email providers.
- **ALT-008**: **Rejection Reason**: US-only infrastructure — a hard disqualifier under GDPR given the personal data these emails contain.

## Implementation Notes

- **IMP-001**: Sends are abstracted behind `IEmailSender` — one implementation, provider injected via config.
- **IMP-002**: SMTP credentials live in user secrets locally, environment variables in production — see [docs/DEPLOYMENT.md](../DEPLOYMENT.md).
- **IMP-003**: SPF, DKIM, and DMARC must be configured on the sending domain before any production send.
- **IMP-004**: All email templates carry Skoleoverblikket branding.
- **IMP-005**: Do not route bulk/marketing sends through this integration — transactional only.

## Related Decisions

- [tech-stack](tech-stack.md) — the EU-residency constraint this provider choice satisfies
- [ai-data-boundary](ai-data-boundary.md) — Scaleway is also used for feedback speech-to-text under the same EU-first rule
