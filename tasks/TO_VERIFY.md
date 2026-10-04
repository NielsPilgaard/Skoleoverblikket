---
description: Manual UI acceptance checklist for fravær, staff absence, vikardækning, notifications, public site, and PR #24 (databehandleraftale + data deletion).
---

# Manual UI acceptance checklist

- `[x]` = confirmed in the running app. Brackets name the screenshot: plain numbers are in `test-results/ui-verify/` (run 1, 3 Oct); `v2-NN` are in `test-results/ui-verify-2/` (run 2, 4 Oct).
- `[x] 🧪` = covered by an API integration test only, not clicked through.
- ⚠ = still a problem in run 2.
- `[ ]` = not verified.

## Your turn: what's left

Everything else is ticked. The rest needs you, or production. With Aspire running and logged in as `admin@debugskolen.dk`:

1. **Lawyer review (blocker).** Send them `test-results/ui-verify-2/databehandleraftale-v1.0.pdf` and `test-results/ui-verify-2/underdatabehandlere.pdf`, both printed from the local pages. They're gitignored, so they exist only on this machine. The live pages are http://localhost:5173/databehandleraftale and http://localhost:5173/underdatabehandlere. The text lives in `web/src/pages/DataProcessingAgreementPage.tsx` and `web/src/content/dataProcessing.ts`.
2. **Read the two new emails.** Backoffice now previews them:
   - Deletion warning, 7 days before: http://localhost:5173/backoffice/emails?type=deletion-warning
   - Sub-processor notice: http://localhost:5173/backoffice/emails?type=sub-processor-notice
3. **After the first production deploy**, check the `CanceledAt` backfill in the prod DB. The query should return `0`:
   ```sql
   select count(*) from "Subscriptions" where "Status" = 3 and "CanceledAt" is null;
   ```
4. **Look at the flags yourself (optional).** Open http://localhost:5173/fravaer?fane=statistik and press ‹ to go to 3. kvartal 2026. Anders Haarby shows ▲ 15 % and Christian Søndergaard shows ● 10 %. That data was seeded with SQL. Remove it with:
   ```sql
   delete from "AbsenceReports" where "Reason" = 'UI-verify flag test';
   ```

## Admin / secretary (Hanne)

### 1. Fravær → Registrering (AttendancePage)

- [x] Pick a class and mark each child present, sick or absent. [14] Attendance offers Ulovligt + Syg. Ekstraordinær frihed comes from approved leave requests. [17]
- [x] Save, reload and check it persists. [15]
- [x] Non-school day is blocked: banner shows and taps are ignored. [11, 12]
- [x] The Fremmøde overview no longer shows "Morgen mangler" on a weekend. [v2-51]
- [x] The save bar sits above the "Vis som" toolbar. [v2-62]

### 2. Fravær → parent reports

- [x] Parent sick reports and leave requests show up for the office. [24, 60-register]
- [x] Approve and reject a leave request. Check the follow-up status. [25, 26]
- [x] A rejected request shows "Fjern" for the parent instead of "Annuller". [v2-71]
- [x] Leave badges are coloured by decision: godkendt is green, afvist red, afventer amber. This holds on both the admin and the parent page. [v2-92, v2-96]

### 3. Fravær stats

- [x] Quarterly numbers are right, checked by hand on Q3 (37 school days). Anders: 6 ulovligt days, 6/37 = 16,2 %. Christian: 4 days, 4/37 = 10,8 %. 3.a total: 10 days / (10 students × 37) = 2,7 %. [v2-90, v2-91]
- [x] Ulovligt flags show at 10% and 15%: Anders ▲ "15 % ulovligt fravær", Christian ● "10 % ulovligt fravær", and the 3.a row shows Flag 2. This used SQL-seeded Q3 data, because closed quarters are read-only in the UI (cleanup is under "Your turn"). The API side is covered by 🧪 `AbsenceStatsTests`. [v2-91]
- [x] Retention notice shows ("Skoleåret 2025/26 slettes automatisk 1. august 2027"). [17-Anmodninger]

### 4. Staff absence (StaffAbsencePage)

- [x] Report a teacher absent for a full day, on the teacher's behalf. [03, 05]
- [x] Report a teacher absent for part of a day: "Kun en del af dagen" adds Fra kl./Til kl. [v2-57]
- [x] Edit an absence ("Ret datoer eller tidsrum"). [v2-55]
- [x] Cancel an absence ("Slet fravær", with a confirm dialog). [06, 08, run 2]
- [x] Reporting the same person twice for an overlapping period is refused: "Medarbejderen er allerede meldt fraværende i den periode." [v2-58]

### 5. Vikardækning (SubstituteCoverPage and detail)

- [x] Each affected lektion is listed. [06, v2-53]
- [x] Free candidates are ranked sensibly: vikarer come first, then free staff A–Z. Absent and busy staff are left out. [v2-53]
- [x] Assigning a vikar in one click works. [07, run 2]
- [x] Reassigning works. [36, 60-vikar-detail]
- [x] Unassigning ("Fjern") works and survives a reload. [run 2]
- [x] Double-booked staff are not offered. Vibeke Stub is hidden on Jørgen Bak's lektioner at the three times she already covers. [v2-56]
- [x] A vikar who is reported absent is released from their bookings, and those lektioner show as uncovered again. This only applies to absences saved after the fix. Allan's 5. okt absence predated it and needed a re-save.

### 6. Dashboard

- [x] Uncovered lektioner show up: "Kræver din opmærksomhed · Lektioner uden vikar · 11 lektioner de næste 7 dage". [v2-50]
- [x] Stats numbers still add up. [01, 09, v2-50]

## Teacher / staff

### 7. Self-report absence

- [x] Report absence from the staff view. [32, 33]
- [x] The teacher only sees their own report. Allan's "Mit fravær" lists only his 5. okt absence. [v2-81] The API side is covered by 🧪 `StaffAbsenceTests`.

### 8. Staff dashboard

- [x] The assigned vikar lektion shows where and when. [31, v2-80]
- [x] Check it on a phone width. [31, v2-80]

### 9. Ugeplan

- [x] The vikar name shows on the affected slot in WeekPlanList. [36]
- [x] The parent ugeplan shows the same. [38]

### 10. Permissions

- [x] Restricted-mode teachers cannot take attendance for a class they don't have. [34] The error text is unchanged and still vague (`AttendancePage.tsx:161`).
- [x] 🧪 Parents cannot see other children's absence (`AbsenceTests.ParentSeesOnlyOwnChildren`, `ParentReport_ForSomeoneElsesChild_Returns403`).

## Parent

### 11. ParentAbsencePage (rewritten)

- [x] Report sick for one child. [21]
- [x] Weekend sick reports are blocked. On a Sunday the form defaults to the next school day. Picking a Saturday disables "Meld syg". The server also returns 400 (🧪 `ParentReport_OnlyWeekendDays_Returns400`). [run 2]
- [x] Request leave. [22, 23]
- [x] History list shows the right status. [26, v2-71]
- [x] Check it on a phone. [19–26, v2-71]
  - On phones the badge now sits on its own row under the date, with Annuller/Fjern to its right. [v2-96]
- [x] Check a parent with two children. [19, v2-71]

## Notifications

### 12. Bell and preferences

- [x] The bell shows an entry when leave is decided. [27, run 2]
- [x] The bell shows "Du er vikar i 3.a (Dansk) tirsdag 6. oktober kl. 08:00" when a vikar is assigned. [run 2]
- [x] At 375px the bell dropdown opens leftwards and stays on screen (x 39–359). The page doesn't scroll sideways. [v2-97]
- [x] The new types appear in NotificationPreferencesPage (admin and parent). [28, 29]
- [x] Opt-out suppresses them. The parent has "Fri godkendt" off in both columns, and the leave approved after that (9. okt "Optout-test") has no bell entry, while earlier approvals do. [run 2]
- [x] The email arrives ("Du er vikar i 3.a (Dansk)…" to allan.drost@ in Mailpit). [run 2]

## Public site

### 13. Landing, /nyheder, About, Contact, Privacy, Footer

- [x] /nyheder renders. [40, 41, v2-21]
- [x] PublicNav works on mobile: "Log ind" stays on one line on /nyheder, /kontakt and the legal pages. [v2-21-*]
- [x] Feature cards include the new features (Vikardækning, Fravær). [40-landing]
- [x] Privacy page text about absence data is correct (§2, §6). [v2-20-privatlivspolitik]
- [x] Footer links work. Footer: Om, Nyheder, Privatlivspolitik, Databehandleraftale, Kontakt, Log ind. [41, run 2]
- [x] OG image looks right. [41-og-image]
- [x] Sitemap lists /, /signup, /om, /kontakt, /nyheder, /privatlivspolitik, /databehandleraftale and /underdatabehandlere.

## PR #24: databehandleraftale + data deletion

### 14. Databehandleraftale (task 48)

- [ ] **Blocker:** a lawyer has reviewed the DPA text (`DataProcessingAgreementPage.tsx`, v1.0). The PDFs are under "Your turn".
- [x] /databehandleraftale renders on desktop and at 375px. [v2-20, v2-21]
- [x] /underdatabehandlere lists OVHcloud, Scaleway, Stripe and elmah.io, each with purpose, data and location. [v2-20]
  - At 375px the legal page headings use a smaller size and fit on screen. There's no sideways scroll. [v2-95]
- [x] Both pages are in the sitemap. The privacy policy links to both. The footer links to /databehandleraftale only, and /underdatabehandlere is reached from there.
- [x] Privacy policy §5/§6 shows the shared sub-processor list, 90 days, the 7-day warning and 14-day backups. The old "endnu ikke implementeret" note is gone. [v2-20]
- [x] Signup can't be submitted without the DPA checkbox: the button is disabled and no request is sent. [v2-30]
- [x] Signup with an email that already has an account returns 409 "Der findes allerede en bruger med den e-mail…" under the field. admin@debugskolen.dk still logs in afterwards. [v2-31]
- [x] An admin of an existing school (Isolationsskolen) sees the amber banner. "Acceptér på vegne af skolen" hides it, and it stays hidden after reload. [v2-40, v2-41]
- [x] Non-admin staff and parents never see the banner. Verified by code (`enabled = isAdmin && !isSuperAdmin`); the uiverify parent and Allan had no banner.
- [x] The banner works at 375px. [v2-40]
- [x] Backoffice → Underdatabehandlere refuses less than 30 days' notice. The date input has `min`, and the server returns 400 when `min` is bypassed.
- [x] Backoffice → Underdatabehandlere with 30+ days sends a Bcc email to every school's admins, in batches of 50, To kontakt@. It shows "Sendt til 138 modtagere på 161 skoler". [v2-12]
  - The text field now clears after a successful send, which disables the button, so a second click can't resend. Checked by code only. I didn't send a third notice to every school just to test it.

### 15. Data deletion after cancellation (task 06)

- [x] 🧪 Cancelling sets `CanceledAt`, and resubscribing clears it (`SchoolRetentionTests.CancelWebhook_StartsClock_AndResubscribing_StopsIt`). It can't be clicked through locally because `stripe-listen` isn't running.
- [x] The day-83 warning email is in Danish and links to /eksporter and kontakt@skoleoverblikket.dk. Seen in the new backoffice preview (under "Your turn"). Timing is covered by 🧪 `Warns7DaysAhead_…` and `NeverWarned_…`. [v2-98-preview-deletion-warning]
- [x] 🧪 After deletion, the school's files, Keycloak logins and rows are gone, another school is untouched, and a failed login deletion keeps the data (`SchoolRetentionTests`).
- [ ] Schools that were already canceled get `CanceledAt` backfilled from `UpdatedAt`. The SQL looks right (`WHERE "Status" = 3`), but the dev DB has no canceled school. Check it in prod after deploy (query under "Your turn").
- [x] 🧪 elmah.io scrubbing (`ElmahIoScrubberTests`). There's no elmah.io key locally, so it can't be checked end to end.

## Cross-cutting

- [x] Tenant isolation: logged in as a second school (Isolationsskolen) and none of this data leaks. [50-*]
- [x] Migration: all three new migrations (`AbsenceRegisterAndStaffAbsence`, `SchoolRetentionAndDataProcessingAgreement`, `Add_StaffAbsence_TimeWindow`) are applied on the dev DB, which already had data.
- [x] Mobile: run every page at about 375px wide. [60-*, 41-*, v2-21-*, v2-6x, v2-7x] The ⚠ items above still apply.
- [x] Danish text: months are now lowercase ("Mandag 5. oktober", "Fredag 2. oktober", "Søndag 4. oktober"). [v2-53, v2-62, v2-80]

## Other issues

- [x] The Fravær tab bar no longer shows native scroll arrows on desktop or a scrollbar on mobile. [v2-51, v2-63]
- [x] Long class names ("Børnehaveklassen") no longer overlap "Morgen mangler" in the Fremmøde overview. [v2-93]
- [x] On mobile the Fravær date range wraps as one unit ("04/09/2026 – 04/10/2026" on one line). [v2-94]
- [x] An unknown `?fane=` on /fravaer, or `fri` for non-admins, falls back to Fremmøde instead of an empty page.
