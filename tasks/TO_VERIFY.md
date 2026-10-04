---
description: Manual UI acceptance checklist for fravær, staff absence, vikardækning, notifications, public site, and PR #24 (databehandleraftale + data deletion).
---

# Manual UI acceptance checklist

`[x]` = confirmed by a screenshot in `test-results/ui-verify/` (file numbers in brackets).
⚠ = problem found. `[ ]` with no note = not covered yet.

## Admin / secretary (Hanne)

### 1. Fravær → Registrering (AttendancePage)

- [x] Pick a class and mark each child present, sick or absent. [14] Attendance offers Ulovligt + Syg. Ekstraordinær frihed only comes from approved leave requests. [17]
- [x] Save, reload and check it persists. [15]
- [x] Non-school day is blocked: banner shows and taps are ignored. [11, 12]
  - ⚠ The Fremmøde overview still shows "Morgen mangler" for every class on a weekend. [10]

### 2. Fravær → parent reports

- [x] Parent sick reports and leave requests show up for the office. [24, 60-register]
- [x] Approve and reject a leave request. Check the follow-up status. [25, 26]
  - ⚠ The "afvist" badge is green, the same as "godkendt". [25, 26]
  - ⚠ The parent still sees "Annuller" on a rejected request. [26]

### 3. Fravær stats

- [ ] Quarterly numbers are right. Numbers render [17-Statistik, 18] but haven't been checked by hand.
- [ ] Ulovligt flags show at 10% and 15%. Test data never reached the thresholds.
- [x] Retention notice shows ("Skoleåret 2025/26 slettes automatisk 1. august 2027"). [17-Anmodninger]

### 4. Staff absence (StaffAbsencePage)

- [x] Report a teacher absent for a full day, on the teacher's behalf. [03, 05]
- [ ] ⚠ Report a teacher absent for a partial day. The form only has dates, no times. [03]
- [ ] Edit an absence.
- [x] Cancel an absence ("Slet fravær"). [06, 08]
- ⚠ The same staff member can be reported twice for the same day (Bent Holm, 3. okt ×2). [02]

### 5. Vikardækning (SubstituteCoverPage and detail)

- [x] Each affected lektion is listed. [06]
- [ ] Free candidates are ranked sensibly. Dropdown never opened in screenshots.
- [x] Assigning a vikar in one click works. [07]
- [x] Reassigning works (Dansk: Allan Drost → Vibeke Stub). [36, 60-vikar-detail]
- [ ] Unassigning ("Fjern") works. The link exists [07] but no screenshot confirms it.
- [ ] ⚠ Double-booked staff are not offered. Allan Drost is reported absent on 5. okt but stays assigned as vikar for two lektioner that day. Nothing flags it. [60-vikar, 31]

### 6. Dashboard

- [ ] ⚠ Uncovered lektioner show up. The dashboard has no such widget. [09]
- [x] Stats numbers still add up. [01, 09]

## Teacher / staff

### 7. Self-report absence

- [x] Report absence from the staff view. [32, 33]
- [ ] The teacher can only see their own report.

### 8. Staff dashboard

- [x] The assigned vikar lektion shows where and when. [31]
- [x] Check it on a phone width. [31]

### 9. Ugeplan

- [x] The vikar name shows on the affected slot in WeekPlanList. [36]
- [x] The parent ugeplan shows the same. [38]

### 10. Permissions

- [x] Restricted-mode teachers cannot take attendance for a class they don't have. [34] The error text is vague: "Fremmødet kunne ikke hentes. Du har måske ikke adgang til klassen."
- [ ] Parents cannot see other children's absence.

## Parent

### 11. ParentAbsencePage (rewritten)

- [x] Report sick for one child. [21]
  - ⚠ A sick report for a Saturday is accepted. [21]
- [x] Request leave. [22, 23]
- [x] History list shows the right status. [26]
- [x] Check it on a phone. [19–26]
  - ⚠ The pending-leave badge squeezes the card text into a narrow column. [23]
- [x] Check a parent with two children. [19]

## Notifications

### 12. Bell and preferences

- [x] The bell shows an entry when leave is decided. [27]
  - ⚠ At 375px the bell dropdown runs off the right edge of the screen. [27]
  - [ ] The bell entry for "Du er sat på som vikar" hasn't been opened.
- [x] The new types appear in NotificationPreferencesPage (admin and parent). [28, 29]
- [ ] Opt-out suppresses them. Toggling off and saving works [30], but suppression wasn't tested.
- [ ] The email arrives.

## Public site

### 13. Landing, /nyheder, About, Contact, Privacy, Footer

- [x] /nyheder renders. [40, 41] There's no fravær/vikar entry yet. That's expected until the weekly changelog run.
- [ ] ⚠ PublicNav works on mobile. On /nyheder and /kontakt, "Log ind" wraps onto two lines and crowds the logo. [41-nyheder-375, 41-footer-kontakt-click] The landing nav has Nyheder and Book demo, but the other pages don't.
- [x] Feature cards include the new features (Vikardækning, Fravær). [40-landing]
- [x] Privacy page text about absence data is correct (§6). [40-privacy]
- [x] Footer links work. [41-footer-kontakt-click]
- [x] OG image looks right. [41-og-image]
- [ ] Sitemap looks right.

## PR #24: databehandleraftale + data deletion

### 14. Databehandleraftale (task 48)

- [ ] **Blocker:** a lawyer has reviewed the DPA text (`DataProcessingAgreementPage.tsx`, v1.0).
- [ ] /databehandleraftale renders on desktop and at 375px.
- [ ] /underdatabehandlere lists OVHcloud, Scaleway, Stripe and elmah.io.
- [ ] Both pages are linked from the footer, the sitemap and the privacy policy.
- [ ] Privacy policy §5/§6 shows the shared sub-processor list, 90 days, a 7-day warning and 14-day backups. The old "endnu ikke implementeret" note is gone.
- [ ] Signup can't be submitted without the DPA checkbox.
- [ ] Signup with an email that already has an account shows the Danish 409 message. The existing account still logs in to its old school.
- [ ] Admin of an existing school sees the amber banner. "Acceptér på vegne af skolen" hides it, and it stays hidden after reload.
- [ ] Non-admin staff and parents never see the banner.
- [ ] The banner works at 375px.
- [ ] Backoffice → Underdatabehandlere refuses less than 30 days' notice.
- [ ] Backoffice → Underdatabehandlere with 30+ days sends one Bcc email to every school's admins.

### 15. Data deletion after cancellation (task 06)

- [ ] Cancelling in Stripe test mode sets `CanceledAt`. Resubscribing clears it.
- [ ] The day-83 warning email arrives in Danish and links to /eksporter and kontakt@skoleoverblikket.dk.
- [ ] After deletion, the school's files, Keycloak logins and rows are gone, and another school is untouched.
- [ ] Schools that were already canceled get `CanceledAt` backfilled from `UpdatedAt`.
- [ ] elmah.io: trigger an error and confirm the entry has no emails, phone/CPR numbers, auth headers or cookies.

## Cross-cutting

- [x] Tenant isolation: logged in as a second school (Isolationsskolen) and none of this data leaks. [50-*]
- [ ] Migration: apply both migrations (fravær + PR #24) to a DB that already has data, not only a fresh one.
- [x] Mobile: run every page at about 375px wide. [60-*, 41-*] The ⚠ items above still apply.
- [ ] ⚠ Danish text: months are capitalized ("Mandag 5. Oktober", "Fredag 2. Oktober") and the header says "I Dag". Danish writes these in lowercase. [06, 11, 31–33]

## Other issues found during ui-verify

- ⚠ The Fravær tab bar shows native scroll arrows on desktop and a horizontal scrollbar on mobile. [10, 17, 60-*]
- ⚠ "Børnehaveklassen" overlaps "Morgen mangler" in the Fremmøde overview. [10, 60-fravaer]
- ⚠ The "Vis som" superadmin toolbar covers the attendance save bar at the bottom of the page. [13, 14, 16]
- ⚠ On mobile, the Fravær date range wraps and leaves a lone "–" behind. [60-register]
