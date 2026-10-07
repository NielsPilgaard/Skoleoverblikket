# Features

Built features beyond the core schema planner, with the controllers and services that own them. The product is the full admin backbone for a small school, not just a schema grid.

- SFO week plan (`SfoWeekPlanController`, `SfoController`) — weekly SFO schedule with print view
- Ugeplan / weekplan (`WeekPlanController`) — per-class weekly plan with file attachments per slot, shown to parents
- Vikar overview (`SubstituteController`) — free/busy staff lookup per time slot and one-click substitute assignment when a teacher or aide is out
- Staff absence + vikardækning (`StaffAbsenceController`, `StaffAbsenceService`, `SubstituteService`) — staff report themselves absent (or the office does it for them); admin sees each affected lektion with ranked free candidates and assigns a vikar per lektion (stored on `WeekPlanSlot`, shown in ugeplan and on the staff dashboard)
- Parent module (`ParentsController`, `ParentMeController`, `ParentInvitationsController`) — parent portal with schema/calendar/ugeplan views
- Fravær / student absence register (`AbsenceController`, `AttendanceController`, `AbsenceService`, `AbsenceStatsService`) — daily fremmøde per class, three legal categories, parent sick reports and leave requests, quarterly stats with 10%/15% ulovligt flags, retention of current + previous school year (`AbsenceRetentionJob`)
- Kontakt directory (`ContactDirectoryController`) — role-filtered parent directory with `ShareContactInfo` consent
- Kontaktbog (`ContactThreadsController`) — per-child parent↔teacher message threads
- Klassechat (`ClassChatController`, `ClassChatAttachmentSweeper`) — one group thread per klasse for its parents, schema staff and admins, with file attachments; membership is derived via `ClassMembershipService`, never stored
- Beskeder (`MessagesController`) — flat inbox for all tenant users with consent rules
- Notifications (`NotificationsController`) — in-app + email, per-type opt-out via `NotificationPreference`
- Calendar with recurrence (`CalendarController`) — school calendar events with recurrence and excluded dates
- Class permissions (`ClassPermissionsController`) — per-class edit grants (superadmin vs. restricted mode)
- File explorer (`FilesController`) — upload files, link to courses, browse by course, OVHCloud object storage
- Bestyrelse / board module (`BoardMembersController`, `BoardInvitationsController`, `BoardFilesController`) — board member invitations and a board-only file space, separate from staff/parent files
- Stå mål med / compliance publishing (`ComplianceCoverageController`) — lets friskoler publish teaching goals and plans per course/grade to satisfy Friskoleloven §1a public-disclosure requirements
- Stats dashboard (`StatsController`) — school-wide overview numbers (classes, staff, schema completeness) for the admin dashboard
- Reports (`ReportsController`) — Excel export of teacher/staff hours and UVM timetal comparisons
- CSV import (`ImportsController`) — bulk import of parents/students onto existing classes, admin-only, with per-row warnings
- Demo requests (`DemoRequestController`) — public "book a demo" form on the marketing site, emailed to sales
- Module billing (`SubscriptionModulesController`) — parent module gated behind Stripe subscription
- Backoffice (`SuperAdminTenantsController`, `SuperAdminEmailPreviewController`) — isSuperAdmin role, view-as mode
- Avatar uploads — presign+confirm pattern for Parent, Staff, Student avatars stored in OVHCloud
- Data retention (`SchoolDeletionService`, `SchoolRetentionJob`) — 90 days after Stripe cancellation, admins are warned 7 days ahead and then all school data (rows, files, Keycloak logins) is permanently deleted
- Databehandleraftale (`DataProcessingAgreementController`, `SuperAdminSubProcessorNoticeController`) — GDPR art. 28 agreement accepted at signup or via an admin banner, public `/databehandleraftale` and `/underdatabehandlere` pages, 30-day sub-processor change notice from the backoffice
- Vacation registration / ferieindmelding (`VacationRegistrationController`) — admin creates registration windows with granularity (weeks/days) and deadlines; parents submit vacation requests via `ParentVacationRegistrationPage`; admin reviews all entries and manages windows via `VacationRegistrationPage` / `VacationRegistrationDetailPage`; full CRUD on windows with open/closed toggle and CSV export of responses
