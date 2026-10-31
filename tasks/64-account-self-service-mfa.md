---
title: 'Account self-service: password change, two-factor and sessions'
purpose: 'Plan the "Min konto" page where every user changes their own password, turns on two-factor login and logs out lost devices, built on Keycloak application-initiated actions.'
description: >-
  Adds a Danish "Min konto" page in the app for every role (staff, parents,
  board). Password change, authenticator-app two-factor (TOTP) with recovery
  codes, passkeys and "log out everywhere" all run as Keycloak
  application-initiated actions on themed Keycloak pages, so we never handle
  passwords or OTP secrets in our API. Two-factor is opt-in per user; admins can
  reset a lost phone; the superadmin login must use two-factor. Prod realm
  config has to be applied by hand or by a config tool, because --import-realm
  skips the existing realm.
status: 'Ready'
---

# Account self-service: password change, two-factor and sessions

## TL;DR

A new page **Min konto** (`/min-konto`, linked from the sidebar footer next to the user's name) shows the user's email, whether they have a password, two-factor status and a "Log ud på alle enheder" button. Every button that changes a credential sends the browser to Keycloak with `keycloak.login({ action: ... })` (application-initiated action, AIA), and Keycloak sends them back to `/min-konto` when done. Keycloak asks for the password again first if the login is old. Our API only reads credential metadata and does the actions Keycloak can't trigger from the browser (log out everywhere, admin reset of a lost phone). Phase 1: password change, authenticator app, recovery codes, log out everywhere, security emails, superadmin must use two-factor. Phase 2: passkeys. Phase 3: a school setting that requires two-factor for its admins.

## Why

- No user can change their own password today. The only route is "Glemt adgangskode" on the login page, which Hanne won't think of as "change my password".
- Admins see CPR-adjacent data (fravær, parent contacts, files). Two-factor is a reasonable thing for a school's DPO to ask about, and "yes, optional per user, required for our own operator login" is a better answer than "no".
- A lost or shared phone currently stays logged in for up to 30 days (`ssoSessionMaxLifespan` 2592000). Users need a way to end that themselves.
- The superadmin login (back office) can read every school's data. It has no second factor today. That's the biggest single risk this task removes.

## Context

- Keycloak 26.2 ([Dockerfile](../infrastructure/keycloak/Dockerfile)), custom Danish login theme with only `login.ftl`, `login-reset-password.ftl` and `login-update-password.ftl`. Every other Keycloak page (OTP setup, OTP prompt, passkeys, recovery codes, delete credential) falls back to the stock theme, so it looks foreign and parts are English.
- Realm ([Skoleoverblikket-realm.json](../infrastructure/keycloak/realms/Skoleoverblikket-realm.json)): no `passwordPolicy` (the app enforces 8 characters in the invitation pages only), no `requiredActions` listed (defaults apply), no custom flows, `accountTheme` unset, brute-force protection on.
- **Prod ignores realm JSON changes.** `--import-realm` skips a realm that already exists ([54](54-move-vps.md)). Every realm setting in this task must also be applied to prod some other way, see D5.
- The API already talks to the Keycloak admin API through `IKeycloakAdminApi` / `KeycloakAdminService` (Refit, service account `skoleoverblikket-admin`), and tests replace it with `FakeKeycloak`.
- A login can belong to several schools and roles ([AUTHORIZATION.md](../docs/AUTHORIZATION.md), `SchoolLoginService`). Anything an admin does to a login affects the person in every school.
- Tabs logged in from signup or an invitation use seeded password-grant tokens with no Keycloak SSO cookie (`web/src/auth/keycloak.ts`). An AIA from such a tab makes Keycloak ask for the password first. Acceptable, just don't treat it as a bug.
- [59 Microsoft and Google login](59-microsoft-google-login.md) will add logins that have no Keycloak password. Their second factor belongs to Microsoft/Google, so the page must hide password and two-factor for them.

## Decisions

D3 and D5 agreed by Niels 2026-10-31. The rest are recommendations an agent may follow.

### D1. Own page in the app, not the Keycloak Account Console

**Chosen:** a React page in the app that only shows status and buttons; the actual credential screens are Keycloak AIA pages in our login theme.

- The Account Console (v3) is a separate React app with its own look, English leftovers, and menus Hanne doesn't need (applications, linked accounts, editable email that would drift from `Staff.Email`). Theming it properly means a Keycloakify build. Rejected.
- Building password/OTP forms ourselves through the admin API means the API sees passwords and OTP secrets and has to redo re-authentication. Rejected.
- AIA keeps secrets inside Keycloak, gives re-authentication for free (`max_auth_age` on the required action), and the screens are the same login-theme pages we already style.

### D2. Which second factors

**Chosen:** authenticator app (TOTP) + recovery codes in Phase 1, passkeys in Phase 2. No SMS (costs money per message, weakest option), no email codes (not built into Keycloak 26.2, and email is the reset channel anyway).

- TOTP: works on every phone, any app (Microsoft Authenticator, Google Authenticator, Authy). Show app names on the setup page, since Hanne knows "the app" rather than "TOTP".
- Recovery codes: without them every lost phone becomes a support case. Keycloak's `CONFIGURE_RECOVERY_AUTHN_CODES` action shows 12 one-time codes with a print/download button. Check during Phase 1 whether it is still a preview feature in 26.2 (`--features=recovery-codes` in the Dockerfile) or already default.
- Passkeys: Face ID / fingerprint is the nicest second factor for Thomas and Mikkel (personas: "biometric if possible"). Phase 2 because the login flow and the stock templates need more work, and phone/browser behaviour needs testing on real devices.

### D3. Opt-in per user, with two exceptions

**Decided (2026-10-31):**

- Everyone: opt-in. Forcing two-factor on parents and substitutes would generate support, not security.
- Superadmin: required, from Phase 1. A conditional sub-flow in the browser flow (`Condition - user role` = `superadmin` → OTP required, `CONFIGURE_TOTP` if missing).
- School admins: Phase 3 adds a school setting "Kræv tofaktor for administratorer" (off by default). Enforced in the API, not in Keycloak, because Keycloak doesn't know which school a login is acting in: the API rejects admin requests (403 ProblemDetails with a "slå tofaktor til" code) when the school requires it and the token's `amr` / `acr` doesn't show a second factor; the web app then sends the user to `/min-konto`. Spec details in Phase 3 once 1 and 2 have shipped.

### D4. Lost phone

**Recommended:** recovery codes first. If those are lost too:

- A school admin can click "Nulstil tofaktor" on the staff member, board member or parent row. The API deletes the login's `otp` and `webauthn` credentials through the admin API, logs out all its sessions and emails the user ("Tofaktor er slået fra på din konto af <admin> på <skole>").
- Only if the login belongs to this school alone (same rule as `SchoolLoginService` uses for deleting logins). A login shared with another school is reset by the superadmin from the back office, otherwise an admin at school A could weaken a user's login at school B.
- An admin can't reset their own two-factor that way (they use recovery codes or another admin).

### D5. Getting realm changes into prod

**Decided (2026-10-31): A, manual checklist.**

The realm JSON drives dev, e2e and Staging. Prod needs one of:

- **A. Manual checklist (chosen).** Each phase lists the exact console changes, Niels applies them in prod, ticks them off here. Five to ten settings, done once. Same approach as earlier realm changes.
- **B. keycloak-config-cli** as a one-shot container in the prod compose on every deploy. Real config-as-code, and tasks [57](57-migration-cli.md) and [59](59-microsoft-google-login.md) also need realm changes. Needs a separate prod realm file without dev users, mailpit SMTP and dev secrets, and a wrong file can lock everyone out. Worth its own task when the third realm change lands.
- Not C (API startup job that writes realm settings): needs `manage-realm` on the API's service account, which widens what a leaked API secret can do.

### D6. Password policy

**Recommended:** realm `passwordPolicy` = `length(8) and notEmail and notUsername and maxLength(128)`. Matches `MIN_PASSWORD_LENGTH = 8` in the invitation pages, so the two can't disagree. No complexity rules (they make Hanne write it on a post-it), no forced expiry (NIST 800-63B). Existing passwords keep working; the policy applies at the next change.

### D7. Email and name stay with the school

Email is the login name and is matched against `Staff`/`Parent`/`BoardMember` rows in several schools. Changing it from "Min konto" would need verification and a sync across schools. Out of scope; the page says "Kontakt skolens kontor for at ændre e-mail". Name is already edited by the school.

## Phase 1: password, authenticator app, recovery codes, log out everywhere

### Keycloak

- [ ] Realm JSON: `passwordPolicy` (D6), required actions `UPDATE_PASSWORD`, `CONFIGURE_TOTP`, `CONFIGURE_RECOVERY_AUTHN_CODES`, `delete_credential` enabled with `max_auth_age` 300 s; `otpPolicy*` = TOTP, SHA1, 6 digits, 30 s (what every authenticator app supports), `otpSupportedApplications` listing Microsoft Authenticator, Google Authenticator; `eventsListeners` += `email`, `eventsEnabled` true with a short expiry.
- [ ] Browser flow: copy the built-in `browser` flow to `skoleoverblikket browser`. Two-factor sub-flow: OTP Form and Recovery Authentication Code Form as alternatives, conditional on "user has a second factor configured". Superadmin sub-flow per D3. Set as realm `browserFlow`. Check what Keycloak 26.2's default flow already contains before copying.
- [ ] Login theme, Danish, same `card-pf` layout as `login-update-password.ftl`, mobile first: `login-config-totp.ftl` (QR code, "kan du ikke scanne?" manual key, app names), `login-otp.ftl` (one big numeric input, `autocomplete="one-time-code"`, `inputmode="numeric"`, link to "Brug en gendannelseskode"), `login-recovery-authn-code-config.ftl` (codes, print, "Jeg har gemt koderne" checkbox), `login-recovery-authn-code-input.ftl`, `delete-credential.ftl`. Danish strings in `messages_da.properties`.
- [ ] Email theme, Danish, html + text: `event-update_password.ftl`, `event-update_totp.ftl`, `event-remove_totp.ftl` ("Hvis det ikke var dig, så kontakt skolens kontor"). Add to `BackofficeEmailPreviewPage` if it can render Keycloak templates; otherwise preview in mailpit.
- [ ] Prod (D5 A), Niels: apply every setting above in the prod console, then log in as superadmin and set up two-factor before logging out.

### API

New `AccountService` (`Services/`) and `AccountController` at `/api/v1/account`, every role, no tenant data:

- [ ] `GET /api/v1/account/security` → `{ email, hasPassword, otp: { configured, createdAt }, recoveryCodes: { configured, remaining? }, passkeys: [] , externalLogin: string? }` for `User.GetKeycloakSubject()`. Reads `GET /users/{id}/credentials` and `GET /users/{id}/federated-identity` through `KeycloakAdminService`. Returns credential ids so the web app can pass them to `delete_credential:<id>`.
- [ ] `POST /api/v1/account/logout-everywhere` → admin API `POST /users/{id}/logout`. 204.
- [ ] Extend `IKeycloakAdminApi` (credentials with id/createdDate/userLabel, delete credential, logout user, federated identities) and `FakeKeycloak` to match.

Admin reset (D4), in the owning services (`StaffService` or equivalent, plus parent and board):

- [ ] `POST /api/v1/staff/{id}/reset-two-factor` (and parent/board equivalents), admin only. Service returns an enum: `Reset`, `NotFound`, `SharedLogin`, `Self`, `NoTwoFactor`. Side effects after the change: logout user, send the email through `IEmailSender`.
- [ ] Back office: same reset for any login, superadmin only.
- [ ] `/codegen`.

### Web

- [ ] `web/src/pages/AccountPage.tsx` at `/min-konto`, all roles. Sections, each a card with one sentence and one button:
  - **Adgangskode**: "Skift adgangskode" → `keycloak.login({ action: 'UPDATE_PASSWORD', redirectUri: <origin>/min-konto?done=password })`.
  - **Tofaktor-login**: off → "Slå tofaktor til" (→ `CONFIGURE_TOTP`, then `CONFIGURE_RECOVERY_AUTHN_CODES` if none). On → "Til siden <date>", "Lav nye gendannelseskoder", "Slå tofaktor fra" (→ `delete_credential:<id>`).
  - **Enheder**: "Log ud på alle enheder" with a confirm dialog, then log out this tab too.
  - Login via Microsoft/Google (59): replace the first two sections with "Du logger ind med din Microsoft-konto".
  - Toast from `?done=` on return; Keycloak returns `kc_action_status=success|cancelled|error` on the redirect, show the matching Danish message.
- [ ] Sidebar footer: make the user-name row a link to `/min-konto` ("Min konto"), keep "Notifikationsindstillinger" and the parent "Min profil" as they are. Map `/min-konto` → `null` in `sidebarRouteFeatures`.
- [ ] Admin reset button in the staff, parent and board member detail views, with a confirm dialog that says what happens ("<navn> kan logge ind med kun adgangskode, og bliver logget ud alle steder").
- [ ] Changelog `feat:` entry and a landing-page feature card update (security bullet), per AGENTS.md.

### Tests

- [ ] Integration (`AccountTests.cs`): security summary for own login only; logout-everywhere calls Keycloak for own subject; admin reset works for a single-school staff member, returns 409 for a shared login, 403 for non-admin, 404 for another school's staff (tenant isolation), refuses self; email sent on reset.
- [ ] Playwright (`account.spec.ts`), against the real Keycloak in the e2e stack: change password and log in with the new one; turn on TOTP (read the secret from the manual-key text, compute the code in the test with a small TOTP helper), log out, log in with the code, turn it off. `data-testid` on every element, including in the `.ftl` templates.
- [ ] Manual on a real iPhone and Samsung: QR scan, numeric keypad, autofill of the code from the authenticator app. Add to [TO_VERIFY.md](TO_VERIFY.md).

### Docs

- [ ] [AUTHORIZATION.md](../docs/AUTHORIZATION.md): account endpoints, admin reset rule (single-school logins only), superadmin requires two-factor.
- [ ] [FEATURES.md](../docs/FEATURES.md): Min konto → `AccountController` / `AccountService`.
- [ ] DPA / security page copy (`DataProcessingAgreementPage.tsx`): mention optional two-factor and that operator access requires it.

## Phase 2: passkeys

- [ ] Realm: `webauthn-register` required action, `webAuthnPolicyRpEntityName` "Skoleoverblikket", user verification "preferred", WebAuthn Authenticator as a third alternative in the two-factor sub-flow.
- [ ] Theme: `webauthn-register.ftl`, `webauthn-authenticate.ftl`, `select-authenticator.ftl` in Danish.
- [ ] Min konto: "Tilføj adgangsnøgle (Face ID / fingeraftryk)", list with label and date, remove each.
- [ ] Decide after real-device testing whether passkeys may also replace the password (passwordless). Not before: Hanne on a shared office PC is the edge case.

## Phase 3: school can require two-factor for admins

Spec when 1 and 2 have shipped and a school asks for it. Outline in D3. Open questions: grace period after turning it on, what the admin sees who just enabled the setting without having two-factor themselves (block turning it on until they do), and whether it should cover all staff.

## Out of scope

- Changing email or name from Min konto (D7).
- SMS codes, email codes.
- Per-session list with device names. "Log ud på alle enheder" covers the lost-phone case; revisit if users ask.
- Login history / audit log for the user.
- Account deletion by the user. Logins are owned by the school's rows (`SchoolLoginService`).
