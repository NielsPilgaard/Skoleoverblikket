CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE TABLE "Classes" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Description" character varying(8000),
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Classes" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE TABLE "Courses" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Description" character varying(8000),
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_Courses" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE TABLE "Rooms" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Description" character varying(8000),
        "Capacity" integer,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Rooms" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE TABLE "Schools" (
        "Id" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Slug" character varying(128) NOT NULL,
        "ContactEmail" character varying(500),
        "ContactPhone" character varying(50),
        "LogoUrl" character varying(500),
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_Schools" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE TABLE "Staff" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Email" character varying(500),
        "Phone" character varying(50),
        "Role" integer NOT NULL,
        "KeycloakSubject" character varying(500),
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Staff" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE TABLE "TimeSlotTemplates" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "LessonDurationMinutes" integer NOT NULL,
        "DayStartTime" time without time zone NOT NULL,
        "DayEndTime" time without time zone NOT NULL,
        "ActiveDays" text NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_TimeSlotTemplates" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE TABLE "Schemas" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ClassId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Status" integer NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Schemas" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Schemas_Classes_ClassId" FOREIGN KEY ("ClassId") REFERENCES "Classes" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE TABLE "TimeSlots" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ClassId" uuid,
        "SortOrder" integer NOT NULL,
        "StartTime" time without time zone NOT NULL,
        "EndTime" time without time zone NOT NULL,
        "Label" character varying(500),
        CONSTRAINT "PK_TimeSlots" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TimeSlots_Classes_ClassId" FOREIGN KEY ("ClassId") REFERENCES "Classes" ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE TABLE "StaffInvitations" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "StaffId" uuid NOT NULL,
        "Email" character varying(500) NOT NULL,
        "Token" character varying(128) NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "AcceptedAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "RowVersion" bytea,
        CONSTRAINT "PK_StaffInvitations" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_StaffInvitations_Staff_StaffId" FOREIGN KEY ("StaffId") REFERENCES "Staff" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE TABLE "TimeSlotTemplateBreaks" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "TimeSlotTemplateId" uuid NOT NULL,
        "StartTime" time without time zone NOT NULL,
        "DurationMinutes" integer NOT NULL,
        CONSTRAINT "PK_TimeSlotTemplateBreaks" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_TimeSlotTemplateBreaks_TimeSlotTemplates_TimeSlotTemplateId" FOREIGN KEY ("TimeSlotTemplateId") REFERENCES "TimeSlotTemplates" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE TABLE "SchemaSlots" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "SchemaId" uuid NOT NULL,
        "TimeSlotId" uuid NOT NULL,
        "Weekday" integer NOT NULL,
        "CourseId" uuid NOT NULL,
        "TeacherId" uuid NOT NULL,
        "RoomId" uuid,
        "AideId" uuid,
        CONSTRAINT "PK_SchemaSlots" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SchemaSlots_Courses_CourseId" FOREIGN KEY ("CourseId") REFERENCES "Courses" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_SchemaSlots_Rooms_RoomId" FOREIGN KEY ("RoomId") REFERENCES "Rooms" ("Id"),
        CONSTRAINT "FK_SchemaSlots_Schemas_SchemaId" FOREIGN KEY ("SchemaId") REFERENCES "Schemas" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_SchemaSlots_Staff_AideId" FOREIGN KEY ("AideId") REFERENCES "Staff" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_SchemaSlots_Staff_TeacherId" FOREIGN KEY ("TeacherId") REFERENCES "Staff" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_SchemaSlots_TimeSlots_TimeSlotId" FOREIGN KEY ("TimeSlotId") REFERENCES "TimeSlots" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE INDEX "IX_Schemas_ClassId" ON "Schemas" ("ClassId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE INDEX "IX_SchemaSlots_AideId" ON "SchemaSlots" ("AideId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE INDEX "IX_SchemaSlots_CourseId" ON "SchemaSlots" ("CourseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE INDEX "IX_SchemaSlots_RoomId" ON "SchemaSlots" ("RoomId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE INDEX "IX_SchemaSlots_SchemaId" ON "SchemaSlots" ("SchemaId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE INDEX "IX_SchemaSlots_TeacherId" ON "SchemaSlots" ("TeacherId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE INDEX "IX_SchemaSlots_TimeSlotId" ON "SchemaSlots" ("TimeSlotId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE UNIQUE INDEX "IX_Schools_Slug" ON "Schools" ("Slug");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE INDEX "IX_StaffInvitations_StaffId" ON "StaffInvitations" ("StaffId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE UNIQUE INDEX "IX_StaffInvitations_Token" ON "StaffInvitations" ("Token");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE INDEX "IX_TimeSlots_ClassId" ON "TimeSlots" ("ClassId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    CREATE INDEX "IX_TimeSlotTemplateBreaks_TimeSlotTemplateId" ON "TimeSlotTemplateBreaks" ("TimeSlotTemplateId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403183219_Initial') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260403183219_Initial', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403205521_Remove_Slugs') THEN
    DROP INDEX "IX_Schools_Slug";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403205521_Remove_Slugs') THEN
    ALTER TABLE "Schools" DROP COLUMN "Slug";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260403205521_Remove_Slugs') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260403205521_Remove_Slugs', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260404051806_Add_SchoolFile') THEN
    CREATE TABLE "SchoolFiles" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "FileName" character varying(500) NOT NULL,
        "ContentType" character varying(200) NOT NULL,
        "SizeBytes" bigint NOT NULL,
        "StorageKey" character varying(1000) NOT NULL,
        "Url" character varying(2000) NOT NULL,
        "CourseId" uuid,
        "UploadedBy" character varying(200) NOT NULL,
        "UploadedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_SchoolFiles" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SchoolFiles_Courses_CourseId" FOREIGN KEY ("CourseId") REFERENCES "Courses" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260404051806_Add_SchoolFile') THEN
    CREATE INDEX "IX_SchoolFiles_CourseId" ON "SchoolFiles" ("CourseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260404051806_Add_SchoolFile') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260404051806_Add_SchoolFile', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260404060432_Add_Subscription') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260404060432_Add_Subscription', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260404065008_Transactions') THEN
    CREATE TABLE "Subscriptions" (
        "Id" uuid NOT NULL,
        "SchoolId" uuid NOT NULL,
        "Status" integer NOT NULL,
        "StripeCustomerId" text,
        "StripeSubscriptionId" text,
        "CurrentPeriodEnd" timestamp with time zone,
        "TrialEnd" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        "UpdatedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_Subscriptions" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260404065008_Transactions') THEN
    CREATE UNIQUE INDEX "IX_Subscriptions_SchoolId" ON "Subscriptions" ("SchoolId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260404065008_Transactions') THEN
    CREATE INDEX "IX_Subscriptions_StripeCustomerId" ON "Subscriptions" ("StripeCustomerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260404065008_Transactions') THEN
    CREATE INDEX "IX_Subscriptions_StripeSubscriptionId" ON "Subscriptions" ("StripeSubscriptionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260404065008_Transactions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260404065008_Transactions', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260404223250_TimeSlot_IsBreak') THEN
    ALTER TABLE "TimeSlots" ADD "IsBreak" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260404223250_TimeSlot_IsBreak') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260404223250_TimeSlot_IsBreak', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260405115552_AddCalendarEntry') THEN
    CREATE TABLE "CalendarEntries" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Type" integer NOT NULL,
        "Title" character varying(200) NOT NULL,
        "StartDate" date NOT NULL,
        "EndDate" date NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_CalendarEntries" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260405115552_AddCalendarEntry') THEN
    CREATE INDEX "IX_CalendarEntries_TenantId_StartDate_EndDate" ON "CalendarEntries" ("TenantId", "StartDate", "EndDate");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260405115552_AddCalendarEntry') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260405115552_AddCalendarEntry', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260405124740_Add_WeekPlan') THEN
    CREATE TABLE "WeekPlans" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ClassId" uuid NOT NULL,
        "IsoYear" integer NOT NULL,
        "IsoWeek" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_WeekPlans" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_WeekPlans_Classes_ClassId" FOREIGN KEY ("ClassId") REFERENCES "Classes" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260405124740_Add_WeekPlan') THEN
    CREATE TABLE "WeekPlanSlots" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "WeekPlanId" uuid NOT NULL,
        "SchemaSlotId" uuid NOT NULL,
        "Beskrivelse" character varying(8000),
        "Lektier" character varying(8000),
        "FagSwapCourseId" uuid,
        "UpdatedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_WeekPlanSlots" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_WeekPlanSlots_Courses_FagSwapCourseId" FOREIGN KEY ("FagSwapCourseId") REFERENCES "Courses" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_WeekPlanSlots_SchemaSlots_SchemaSlotId" FOREIGN KEY ("SchemaSlotId") REFERENCES "SchemaSlots" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_WeekPlanSlots_WeekPlans_WeekPlanId" FOREIGN KEY ("WeekPlanId") REFERENCES "WeekPlans" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260405124740_Add_WeekPlan') THEN
    CREATE TABLE "WeekPlanSlotFiles" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "WeekPlanSlotId" uuid NOT NULL,
        "SchoolFileId" uuid NOT NULL,
        CONSTRAINT "PK_WeekPlanSlotFiles" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_WeekPlanSlotFiles_SchoolFiles_SchoolFileId" FOREIGN KEY ("SchoolFileId") REFERENCES "SchoolFiles" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_WeekPlanSlotFiles_WeekPlanSlots_WeekPlanSlotId" FOREIGN KEY ("WeekPlanSlotId") REFERENCES "WeekPlanSlots" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260405124740_Add_WeekPlan') THEN
    CREATE INDEX "IX_WeekPlans_ClassId" ON "WeekPlans" ("ClassId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260405124740_Add_WeekPlan') THEN
    CREATE UNIQUE INDEX "IX_WeekPlans_TenantId_ClassId_IsoYear_IsoWeek" ON "WeekPlans" ("TenantId", "ClassId", "IsoYear", "IsoWeek");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260405124740_Add_WeekPlan') THEN
    CREATE INDEX "IX_WeekPlanSlotFiles_SchoolFileId" ON "WeekPlanSlotFiles" ("SchoolFileId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260405124740_Add_WeekPlan') THEN
    CREATE UNIQUE INDEX "IX_WeekPlanSlotFiles_WeekPlanSlotId_SchoolFileId" ON "WeekPlanSlotFiles" ("WeekPlanSlotId", "SchoolFileId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260405124740_Add_WeekPlan') THEN
    CREATE INDEX "IX_WeekPlanSlots_FagSwapCourseId" ON "WeekPlanSlots" ("FagSwapCourseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260405124740_Add_WeekPlan') THEN
    CREATE INDEX "IX_WeekPlanSlots_SchemaSlotId" ON "WeekPlanSlots" ("SchemaSlotId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260405124740_Add_WeekPlan') THEN
    CREATE UNIQUE INDEX "IX_WeekPlanSlots_WeekPlanId_SchemaSlotId" ON "WeekPlanSlots" ("WeekPlanId", "SchemaSlotId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260405124740_Add_WeekPlan') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260405124740_Add_WeekPlan', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260405204220_Add_TimeSlot_SchemaId') THEN
    ALTER TABLE "TimeSlots" ADD "SchemaId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260405204220_Add_TimeSlot_SchemaId') THEN
    CREATE INDEX "IX_TimeSlots_SchemaId" ON "TimeSlots" ("SchemaId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260405204220_Add_TimeSlot_SchemaId') THEN
    ALTER TABLE "TimeSlots" ADD CONSTRAINT "FK_TimeSlots_Schemas_SchemaId" FOREIGN KEY ("SchemaId") REFERENCES "Schemas" ("Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260405204220_Add_TimeSlot_SchemaId') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260405204220_Add_TimeSlot_SchemaId', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260412083208_Add_Course_Color') THEN
    ALTER TABLE "Courses" ADD "Color" character varying(7);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260412083208_Add_Course_Color') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260412083208_Add_Course_Color', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260412192147_AddSchemaDateRange') THEN
    ALTER TABLE "Schemas" DROP COLUMN "IsActive";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260412192147_AddSchemaDateRange') THEN
    ALTER TABLE "Schemas" DROP COLUMN "Status";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260412192147_AddSchemaDateRange') THEN
    ALTER TABLE "Schemas" ADD "EndDate" date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260412192147_AddSchemaDateRange') THEN
    ALTER TABLE "Schemas" ADD "StartDate" date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260412192147_AddSchemaDateRange') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260412192147_AddSchemaDateRange', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260419074357_Add_Staff_IsAdmin') THEN
    ALTER TABLE "Staff" ADD "IsAdmin" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260419074357_Add_Staff_IsAdmin') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260419074357_Add_Staff_IsAdmin', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260425064641_Add_SchoolFileFolder_And_Presign') THEN
    ALTER TABLE "SchoolFiles" ADD "FolderId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260425064641_Add_SchoolFileFolder_And_Presign') THEN
    CREATE TABLE "SchoolFileFolders" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "ParentId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_SchoolFileFolders" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SchoolFileFolders_SchoolFileFolders_ParentId" FOREIGN KEY ("ParentId") REFERENCES "SchoolFileFolders" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260425064641_Add_SchoolFileFolder_And_Presign') THEN
    CREATE INDEX "IX_SchoolFiles_FolderId" ON "SchoolFiles" ("FolderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260425064641_Add_SchoolFileFolder_And_Presign') THEN
    CREATE INDEX "IX_SchoolFileFolders_ParentId" ON "SchoolFileFolders" ("ParentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260425064641_Add_SchoolFileFolder_And_Presign') THEN
    ALTER TABLE "SchoolFiles" ADD CONSTRAINT "FK_SchoolFiles_SchoolFileFolders_FolderId" FOREIGN KEY ("FolderId") REFERENCES "SchoolFileFolders" ("Id") ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260425064641_Add_SchoolFileFolder_And_Presign') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260425064641_Add_SchoolFileFolder_And_Presign', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502101634_CalendarRecurrence') THEN
    ALTER TABLE "CalendarEntries" ADD "RecurrenceEnd" date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502101634_CalendarRecurrence') THEN
    ALTER TABLE "CalendarEntries" ADD "RecurrenceRule" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260502101634_CalendarRecurrence') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260502101634_CalendarRecurrence', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260504203750_AddCalendarEntryExcludedDates') THEN
    ALTER TABLE "CalendarEntries" ADD "ExcludedDates" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260504203750_AddCalendarEntryExcludedDates') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260504203750_AddCalendarEntryExcludedDates', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260510060354_Add_Course_Category_And_Class_GradeLevel') THEN
    ALTER TABLE "Courses" ADD "Category" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260510060354_Add_Course_Category_And_Class_GradeLevel') THEN
    ALTER TABLE "Classes" ADD "GradeLevel" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260510060354_Add_Course_Category_And_Class_GradeLevel') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260510060354_Add_Course_Category_And_Class_GradeLevel', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260510203252_AddClassPermissions') THEN
    CREATE TABLE "ClassPermissions" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ClassId" uuid NOT NULL,
        "StaffId" uuid NOT NULL,
        "GrantedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_ClassPermissions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ClassPermissions_Classes_ClassId" FOREIGN KEY ("ClassId") REFERENCES "Classes" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_ClassPermissions_Staff_StaffId" FOREIGN KEY ("StaffId") REFERENCES "Staff" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260510203252_AddClassPermissions') THEN
    CREATE INDEX "IX_ClassPermissions_ClassId" ON "ClassPermissions" ("ClassId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260510203252_AddClassPermissions') THEN
    CREATE INDEX "IX_ClassPermissions_StaffId" ON "ClassPermissions" ("StaffId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260510203252_AddClassPermissions') THEN
    CREATE UNIQUE INDEX "IX_ClassPermissions_TenantId_ClassId_StaffId" ON "ClassPermissions" ("TenantId", "ClassId", "StaffId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260510203252_AddClassPermissions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260510203252_AddClassPermissions', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260518034929_Add_CourseId_To_SchoolFileFolder') THEN
    ALTER TABLE "SchoolFileFolders" ADD "CourseId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260518034929_Add_CourseId_To_SchoolFileFolder') THEN
    CREATE INDEX "IX_SchoolFileFolders_CourseId" ON "SchoolFileFolders" ("CourseId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260518034929_Add_CourseId_To_SchoolFileFolder') THEN
    ALTER TABLE "SchoolFileFolders" ADD CONSTRAINT "FK_SchoolFileFolders_Courses_CourseId" FOREIGN KEY ("CourseId") REFERENCES "Courses" ("Id") ON DELETE SET NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260518034929_Add_CourseId_To_SchoolFileFolder') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260518034929_Add_CourseId_To_SchoolFileFolder', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260520135716_AddArchivedAtToClass') THEN
    ALTER TABLE "Classes" ADD "ArchivedAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260520135716_AddArchivedAtToClass') THEN
    CREATE TABLE "Parents" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Email" character varying(500) NOT NULL,
        "Phone" character varying(50),
        "Address" character varying(500),
        "PostalCode" character varying(10),
        "City" character varying(100),
        "ShareContactInfo" boolean NOT NULL,
        "KeycloakSubject" character varying(128),
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Parents" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260520135716_AddArchivedAtToClass') THEN
    CREATE TABLE "SfoShifts" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "DayOfWeek" integer NOT NULL,
        "StartTime" time without time zone NOT NULL,
        "EndTime" time without time zone NOT NULL,
        "Label" character varying(200),
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_SfoShifts" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260520135716_AddArchivedAtToClass') THEN
    CREATE TABLE "Students" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "ClassId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Students" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Students_Classes_ClassId" FOREIGN KEY ("ClassId") REFERENCES "Classes" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260520135716_AddArchivedAtToClass') THEN
    CREATE TABLE "ParentInvitations" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ParentId" uuid NOT NULL,
        "Email" character varying(500) NOT NULL,
        "Token" character varying(128) NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "AcceptedAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "RowVersion" bytea,
        CONSTRAINT "PK_ParentInvitations" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ParentInvitations_Parents_ParentId" FOREIGN KEY ("ParentId") REFERENCES "Parents" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260520135716_AddArchivedAtToClass') THEN
    CREATE TABLE "SfoShiftStaff" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ShiftId" uuid NOT NULL,
        "StaffId" uuid NOT NULL,
        CONSTRAINT "PK_SfoShiftStaff" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SfoShiftStaff_SfoShifts_ShiftId" FOREIGN KEY ("ShiftId") REFERENCES "SfoShifts" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_SfoShiftStaff_Staff_StaffId" FOREIGN KEY ("StaffId") REFERENCES "Staff" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260520135716_AddArchivedAtToClass') THEN
    CREATE TABLE "ParentStudents" (
        "ParentId" uuid NOT NULL,
        "StudentId" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        CONSTRAINT "PK_ParentStudents" PRIMARY KEY ("ParentId", "StudentId"),
        CONSTRAINT "FK_ParentStudents_Parents_ParentId" FOREIGN KEY ("ParentId") REFERENCES "Parents" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_ParentStudents_Students_StudentId" FOREIGN KEY ("StudentId") REFERENCES "Students" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260520135716_AddArchivedAtToClass') THEN
    CREATE INDEX "IX_ParentInvitations_ParentId" ON "ParentInvitations" ("ParentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260520135716_AddArchivedAtToClass') THEN
    CREATE UNIQUE INDEX "IX_ParentInvitations_Token" ON "ParentInvitations" ("Token");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260520135716_AddArchivedAtToClass') THEN
    CREATE INDEX "IX_ParentStudents_StudentId" ON "ParentStudents" ("StudentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260520135716_AddArchivedAtToClass') THEN
    CREATE UNIQUE INDEX "IX_ParentStudents_TenantId_ParentId_StudentId" ON "ParentStudents" ("TenantId", "ParentId", "StudentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260520135716_AddArchivedAtToClass') THEN
    CREATE INDEX "IX_SfoShiftStaff_ShiftId" ON "SfoShiftStaff" ("ShiftId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260520135716_AddArchivedAtToClass') THEN
    CREATE INDEX "IX_SfoShiftStaff_StaffId" ON "SfoShiftStaff" ("StaffId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260520135716_AddArchivedAtToClass') THEN
    CREATE INDEX "IX_Students_ClassId" ON "Students" ("ClassId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260520135716_AddArchivedAtToClass') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260520135716_AddArchivedAtToClass', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260523103235_Add_SubscriptionModuleItems') THEN
    CREATE TABLE "SubscriptionModuleItems" (
        "Id" uuid NOT NULL,
        "SubscriptionId" uuid NOT NULL,
        "Module" integer NOT NULL,
        "StripeSubscriptionItemId" text,
        "IsAdminOverride" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_SubscriptionModuleItems" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SubscriptionModuleItems_Subscriptions_SubscriptionId" FOREIGN KEY ("SubscriptionId") REFERENCES "Subscriptions" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260523103235_Add_SubscriptionModuleItems') THEN
    CREATE UNIQUE INDEX "IX_SubscriptionModuleItems_SubscriptionId_Module" ON "SubscriptionModuleItems" ("SubscriptionId", "Module");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260523103235_Add_SubscriptionModuleItems') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260523103235_Add_SubscriptionModuleItems', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525061406_Add_SfoWeekPlan') THEN
    CREATE TABLE "SfoWeekPlans" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "IsoYear" integer NOT NULL,
        "IsoWeek" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_SfoWeekPlans" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525061406_Add_SfoWeekPlan') THEN
    CREATE TABLE "SfoWeekPlanShifts" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "SfoWeekPlanId" uuid NOT NULL,
        "SfoShiftId" uuid NOT NULL,
        "Beskrivelse" character varying(4000),
        "UpdatedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_SfoWeekPlanShifts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SfoWeekPlanShifts_SfoShifts_SfoShiftId" FOREIGN KEY ("SfoShiftId") REFERENCES "SfoShifts" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_SfoWeekPlanShifts_SfoWeekPlans_SfoWeekPlanId" FOREIGN KEY ("SfoWeekPlanId") REFERENCES "SfoWeekPlans" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525061406_Add_SfoWeekPlan') THEN
    CREATE UNIQUE INDEX "IX_SfoWeekPlans_TenantId_IsoYear_IsoWeek" ON "SfoWeekPlans" ("TenantId", "IsoYear", "IsoWeek");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525061406_Add_SfoWeekPlan') THEN
    CREATE INDEX "IX_SfoWeekPlanShifts_SfoShiftId" ON "SfoWeekPlanShifts" ("SfoShiftId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525061406_Add_SfoWeekPlan') THEN
    CREATE UNIQUE INDEX "IX_SfoWeekPlanShifts_SfoWeekPlanId_SfoShiftId" ON "SfoWeekPlanShifts" ("SfoWeekPlanId", "SfoShiftId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525061406_Add_SfoWeekPlan') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260525061406_Add_SfoWeekPlan', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525194729_SfoWeekPlanShift_UpdatedAt_ValueGeneratedNever') THEN
    ALTER TABLE "SfoWeekPlanShifts" ALTER COLUMN "UpdatedAt" DROP DEFAULT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260525194729_SfoWeekPlanShift_UpdatedAt_ValueGeneratedNever') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260525194729_SfoWeekPlanShift_UpdatedAt_ValueGeneratedNever', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526041548_AddAvatarUrls') THEN
    ALTER TABLE "Students" ADD "AvatarUrl" character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526041548_AddAvatarUrls') THEN
    ALTER TABLE "Staff" ADD "AvatarUrl" character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526041548_AddAvatarUrls') THEN
    ALTER TABLE "Parents" ADD "AvatarUrl" character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526041548_AddAvatarUrls') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260526041548_AddAvatarUrls', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526042355_AddAbsenceReport') THEN
    CREATE TABLE "AbsenceReports" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "StudentId" uuid NOT NULL,
        "ReportedByParentId" uuid NOT NULL,
        "Date" date NOT NULL,
        "EndDate" date,
        "Reason" character varying(500),
        "Status" integer NOT NULL,
        "ConfirmedByStaffId" uuid,
        "ConfirmedAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_AbsenceReports" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AbsenceReports_Parents_ReportedByParentId" FOREIGN KEY ("ReportedByParentId") REFERENCES "Parents" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_AbsenceReports_Staff_ConfirmedByStaffId" FOREIGN KEY ("ConfirmedByStaffId") REFERENCES "Staff" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_AbsenceReports_Students_StudentId" FOREIGN KEY ("StudentId") REFERENCES "Students" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526042355_AddAbsenceReport') THEN
    CREATE INDEX "IX_AbsenceReports_ConfirmedByStaffId" ON "AbsenceReports" ("ConfirmedByStaffId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526042355_AddAbsenceReport') THEN
    CREATE INDEX "IX_AbsenceReports_ReportedByParentId" ON "AbsenceReports" ("ReportedByParentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526042355_AddAbsenceReport') THEN
    CREATE INDEX "IX_AbsenceReports_StudentId" ON "AbsenceReports" ("StudentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526042355_AddAbsenceReport') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260526042355_AddAbsenceReport', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526043140_AddNotifications') THEN
    CREATE TABLE "NotificationPreferences" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "UserType" integer NOT NULL,
        "Type" integer NOT NULL,
        "InApp" boolean NOT NULL,
        "Email" boolean NOT NULL,
        CONSTRAINT "PK_NotificationPreferences" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526043140_AddNotifications') THEN
    CREATE TABLE "Notifications" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "RecipientId" uuid NOT NULL,
        "RecipientType" integer NOT NULL,
        "Type" integer NOT NULL,
        "ReferenceId" uuid,
        "Body" character varying(300) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "ReadAt" timestamp with time zone,
        CONSTRAINT "PK_Notifications" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526043140_AddNotifications') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260526043140_AddNotifications', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526044856_AddContactBook') THEN
    CREATE TABLE "ContactThreads" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "StudentId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_ContactThreads" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ContactThreads_Students_StudentId" FOREIGN KEY ("StudentId") REFERENCES "Students" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526044856_AddContactBook') THEN
    CREATE TABLE "ContactMessages" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "ThreadId" uuid NOT NULL,
        "SenderType" integer NOT NULL,
        "SenderId" uuid NOT NULL,
        "Body" character varying(4000) NOT NULL,
        "SentAt" timestamp with time zone NOT NULL,
        "ReadAt" timestamp with time zone,
        CONSTRAINT "PK_ContactMessages" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ContactMessages_ContactThreads_ThreadId" FOREIGN KEY ("ThreadId") REFERENCES "ContactThreads" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526044856_AddContactBook') THEN
    CREATE INDEX "IX_ContactMessages_ThreadId" ON "ContactMessages" ("ThreadId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526044856_AddContactBook') THEN
    CREATE INDEX "IX_ContactThreads_StudentId" ON "ContactThreads" ("StudentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526044856_AddContactBook') THEN
    CREATE UNIQUE INDEX "IX_ContactThreads_TenantId_StudentId" ON "ContactThreads" ("TenantId", "StudentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526044856_AddContactBook') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260526044856_AddContactBook', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526045737_AddIndexesForAbsenceAndNotifications') THEN
    CREATE INDEX "IX_Notifications_TenantId_RecipientId_CreatedAt" ON "Notifications" ("TenantId", "RecipientId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526045737_AddIndexesForAbsenceAndNotifications') THEN
    CREATE INDEX "IX_Notifications_TenantId_RecipientId_ReadAt" ON "Notifications" ("TenantId", "RecipientId", "ReadAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526045737_AddIndexesForAbsenceAndNotifications') THEN
    CREATE UNIQUE INDEX "IX_NotificationPreferences_TenantId_UserId_UserType_Type" ON "NotificationPreferences" ("TenantId", "UserId", "UserType", "Type");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526045737_AddIndexesForAbsenceAndNotifications') THEN
    CREATE INDEX "IX_AbsenceReports_TenantId_Date" ON "AbsenceReports" ("TenantId", "Date");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526045737_AddIndexesForAbsenceAndNotifications') THEN
    CREATE INDEX "IX_AbsenceReports_TenantId_Status" ON "AbsenceReports" ("TenantId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526045737_AddIndexesForAbsenceAndNotifications') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260526045737_AddIndexesForAbsenceAndNotifications', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526050203_AddMessages') THEN
    CREATE TABLE "Messages" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "SenderId" uuid NOT NULL,
        "SenderType" integer NOT NULL,
        "RecipientId" uuid NOT NULL,
        "RecipientType" integer NOT NULL,
        "Subject" character varying(200) NOT NULL,
        "Body" character varying(10000) NOT NULL,
        "SentAt" timestamp with time zone NOT NULL,
        "ReadAt" timestamp with time zone,
        CONSTRAINT "PK_Messages" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260526050203_AddMessages') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260526050203_AddMessages', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260528073412_AddVacationRegistration') THEN
    CREATE TABLE "VacationRegistrationWindows" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Title" character varying(200) NOT NULL,
        "RegistrationDeadline" date NOT NULL,
        "CareStartDate" date NOT NULL,
        "CareEndDate" date NOT NULL,
        "Granularity" integer NOT NULL,
        "IsOpen" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_VacationRegistrationWindows" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260528073412_AddVacationRegistration') THEN
    CREATE TABLE "VacationRegistrationEntries" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "WindowId" uuid NOT NULL,
        "StudentId" uuid NOT NULL,
        "SubmittedByParentId" uuid NOT NULL,
        "SelectedDates" character varying(4000) NOT NULL,
        "Note" character varying(500),
        "SubmittedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_VacationRegistrationEntries" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_VacationRegistrationEntries_Parents_SubmittedByParentId" FOREIGN KEY ("SubmittedByParentId") REFERENCES "Parents" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_VacationRegistrationEntries_Students_StudentId" FOREIGN KEY ("StudentId") REFERENCES "Students" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_VacationRegistrationEntries_VacationRegistrationWindows_Win~" FOREIGN KEY ("WindowId") REFERENCES "VacationRegistrationWindows" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260528073412_AddVacationRegistration') THEN
    CREATE INDEX "IX_VacationRegistrationEntries_StudentId" ON "VacationRegistrationEntries" ("StudentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260528073412_AddVacationRegistration') THEN
    CREATE INDEX "IX_VacationRegistrationEntries_SubmittedByParentId" ON "VacationRegistrationEntries" ("SubmittedByParentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260528073412_AddVacationRegistration') THEN
    CREATE UNIQUE INDEX "IX_VacationRegistrationEntries_TenantId_WindowId_StudentId" ON "VacationRegistrationEntries" ("TenantId", "WindowId", "StudentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260528073412_AddVacationRegistration') THEN
    CREATE INDEX "IX_VacationRegistrationEntries_WindowId" ON "VacationRegistrationEntries" ("WindowId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260528073412_AddVacationRegistration') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260528073412_AddVacationRegistration', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260528185332_AddBroadcastEmail') THEN
    CREATE TABLE "BroadcastEmails" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "SenderStaffId" uuid NOT NULL,
        "SenderName" character varying(200) NOT NULL,
        "ClassId" uuid,
        "Subject" character varying(200) NOT NULL,
        "Body" character varying(10000) NOT NULL,
        "RecipientCount" integer NOT NULL,
        "SentAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_BroadcastEmails" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260528185332_AddBroadcastEmail') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260528185332_AddBroadcastEmail', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260530040112_AddAdresseBeskyttelse') THEN
    ALTER TABLE "Parents" ADD "AdresseBeskyttet" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260530040112_AddAdresseBeskyttelse') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260530040112_AddAdresseBeskyttelse', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260605045206_AddSubstituteStaffToWeekPlanSlot') THEN
    ALTER TABLE "WeekPlanSlots" ADD "SubstituteAideId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260605045206_AddSubstituteStaffToWeekPlanSlot') THEN
    ALTER TABLE "WeekPlanSlots" ADD "SubstituteTeacherId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260605045206_AddSubstituteStaffToWeekPlanSlot') THEN
    CREATE INDEX "IX_WeekPlanSlots_SubstituteAideId" ON "WeekPlanSlots" ("SubstituteAideId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260605045206_AddSubstituteStaffToWeekPlanSlot') THEN
    CREATE INDEX "IX_WeekPlanSlots_SubstituteTeacherId" ON "WeekPlanSlots" ("SubstituteTeacherId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260605045206_AddSubstituteStaffToWeekPlanSlot') THEN
    ALTER TABLE "WeekPlanSlots" ADD CONSTRAINT "FK_WeekPlanSlots_Staff_SubstituteAideId" FOREIGN KEY ("SubstituteAideId") REFERENCES "Staff" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260605045206_AddSubstituteStaffToWeekPlanSlot') THEN
    ALTER TABLE "WeekPlanSlots" ADD CONSTRAINT "FK_WeekPlanSlots_Staff_SubstituteTeacherId" FOREIGN KEY ("SubstituteTeacherId") REFERENCES "Staff" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260605045206_AddSubstituteStaffToWeekPlanSlot') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260605045206_AddSubstituteStaffToWeekPlanSlot', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260606071047_AddIsEnrolledInSfoToStudent') THEN
    DROP TABLE "BroadcastEmails";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260606071047_AddIsEnrolledInSfoToStudent') THEN
    ALTER TABLE "Students" ADD "IsEnrolledInSfo" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260606071047_AddIsEnrolledInSfoToStudent') THEN
    ALTER TABLE "Messages" ADD "GroupMessageId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260606071047_AddIsEnrolledInSfoToStudent') THEN
    CREATE TABLE "GroupMessages" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "SenderStaffId" uuid,
        "SenderParentId" uuid,
        "SenderName" character varying(200) NOT NULL,
        "Audience" integer NOT NULL,
        "ClassId" uuid,
        "StaffRole" integer,
        "Subject" character varying(200) NOT NULL,
        "Body" character varying(10000) NOT NULL,
        "RecipientCount" integer NOT NULL,
        "SentAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_GroupMessages" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260606071047_AddIsEnrolledInSfoToStudent') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260606071047_AddIsEnrolledInSfoToStudent', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260611033342_Add_GroupMessage_Index_And_Sender_Constraint') THEN
    CREATE INDEX "IX_Messages_GroupMessageId" ON "Messages" ("GroupMessageId") WHERE "GroupMessageId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260611033342_Add_GroupMessage_Index_And_Sender_Constraint') THEN
    ALTER TABLE "GroupMessages" ADD CONSTRAINT "CK_GroupMessages_Sender" CHECK ("SenderStaffId" IS NOT NULL OR "SenderParentId" IS NOT NULL);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260611033342_Add_GroupMessage_Index_And_Sender_Constraint') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260611033342_Add_GroupMessage_Index_And_Sender_Constraint', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260611051652_AddBoardModule') THEN
    CREATE TABLE "BoardFileFolders" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "ParentId" uuid,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_BoardFileFolders" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_BoardFileFolders_BoardFileFolders_ParentId" FOREIGN KEY ("ParentId") REFERENCES "BoardFileFolders" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260611051652_AddBoardModule') THEN
    CREATE TABLE "BoardMembers" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Email" character varying(500) NOT NULL,
        "KeycloakSubject" character varying(500),
        "CanAccessTeacherData" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_BoardMembers" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260611051652_AddBoardModule') THEN
    CREATE TABLE "BoardFiles" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "FileName" character varying(500) NOT NULL,
        "ContentType" character varying(200) NOT NULL,
        "SizeBytes" bigint NOT NULL,
        "StorageKey" character varying(1000) NOT NULL,
        "Url" character varying(2000) NOT NULL,
        "FolderId" uuid,
        "UploadedBy" character varying(200) NOT NULL,
        "UploadedAt" timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_BoardFiles" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_BoardFiles_BoardFileFolders_FolderId" FOREIGN KEY ("FolderId") REFERENCES "BoardFileFolders" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260611051652_AddBoardModule') THEN
    CREATE TABLE "BoardMemberInvitations" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "BoardMemberId" uuid NOT NULL,
        "Email" character varying(500) NOT NULL,
        "Token" character varying(128) NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        "AcceptedAt" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "RowVersion" bytea,
        CONSTRAINT "PK_BoardMemberInvitations" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_BoardMemberInvitations_BoardMembers_BoardMemberId" FOREIGN KEY ("BoardMemberId") REFERENCES "BoardMembers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260611051652_AddBoardModule') THEN
    CREATE INDEX "IX_BoardFileFolders_ParentId" ON "BoardFileFolders" ("ParentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260611051652_AddBoardModule') THEN
    CREATE INDEX "IX_BoardFiles_FolderId" ON "BoardFiles" ("FolderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260611051652_AddBoardModule') THEN
    CREATE INDEX "IX_BoardMemberInvitations_BoardMemberId" ON "BoardMemberInvitations" ("BoardMemberId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260611051652_AddBoardModule') THEN
    CREATE UNIQUE INDEX "IX_BoardMemberInvitations_Token" ON "BoardMemberInvitations" ("Token");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260611051652_AddBoardModule') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260611051652_AddBoardModule', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260613041459_AddBoardMemberUniqueIndexes') THEN
    CREATE UNIQUE INDEX "IX_BoardMembers_TenantId_Email" ON "BoardMembers" ("TenantId", "Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260613041459_AddBoardMemberUniqueIndexes') THEN
    CREATE UNIQUE INDEX "IX_BoardMembers_TenantId_KeycloakSubject" ON "BoardMembers" ("TenantId", "KeycloakSubject") WHERE "KeycloakSubject" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260613041459_AddBoardMemberUniqueIndexes') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260613041459_AddBoardMemberUniqueIndexes', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260620075050_AddBoardMemberInvitation_CreatedAt_Default') THEN
    ALTER TABLE "BoardMemberInvitations" ALTER COLUMN "CreatedAt" SET DEFAULT (now());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260620075050_AddBoardMemberInvitation_CreatedAt_Default') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260620075050_AddBoardMemberInvitation_CreatedAt_Default', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812195345_Add_Message_InReplyToId') THEN
    ALTER TABLE "Messages" ADD "InReplyToId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812195345_Add_Message_InReplyToId') THEN
    CREATE INDEX "IX_Messages_InReplyToId" ON "Messages" ("InReplyToId") WHERE "InReplyToId" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812195345_Add_Message_InReplyToId') THEN
    ALTER TABLE "Messages" ADD CONSTRAINT "FK_Messages_Messages_InReplyToId" FOREIGN KEY ("InReplyToId") REFERENCES "Messages" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812195345_Add_Message_InReplyToId') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260812195345_Add_Message_InReplyToId', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260815055403_AddSubscriptionInterval') THEN
    ALTER TABLE "Subscriptions" ADD "Interval" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260815055403_AddSubscriptionInterval') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260815055403_AddSubscriptionInterval', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825152626_AddStaaMaalMedSnapshot') THEN
    CREATE TABLE "StaaMaalMedSnapshots" (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "SchoolYear" character varying(20) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CreatedByStaffId" uuid NOT NULL,
        "Reason" character varying(500),
        "DataVersion" integer NOT NULL,
        "Data" jsonb NOT NULL,
        CONSTRAINT "PK_StaaMaalMedSnapshots" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_StaaMaalMedSnapshots_Staff_CreatedByStaffId" FOREIGN KEY ("CreatedByStaffId") REFERENCES "Staff" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825152626_AddStaaMaalMedSnapshot') THEN
    CREATE INDEX "IX_StaaMaalMedSnapshots_CreatedByStaffId" ON "StaaMaalMedSnapshots" ("CreatedByStaffId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825152626_AddStaaMaalMedSnapshot') THEN
    CREATE INDEX "IX_StaaMaalMedSnapshots_TenantId_CreatedAt" ON "StaaMaalMedSnapshots" ("TenantId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825152626_AddStaaMaalMedSnapshot') THEN
    CREATE INDEX "IX_StaaMaalMedSnapshots_TenantId_SchoolYear" ON "StaaMaalMedSnapshots" ("TenantId", "SchoolYear");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825152626_AddStaaMaalMedSnapshot') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260825152626_AddStaaMaalMedSnapshot', '10.0.7');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902141356_AddWeekPlanGenerelt') THEN
    ALTER TABLE "WeekPlans" ADD "Generelt" character varying(8000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902141356_AddWeekPlanGenerelt') THEN
    ALTER TABLE "SfoWeekPlans" ADD "Generelt" character varying(8000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902141356_AddWeekPlanGenerelt') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260902141356_AddWeekPlanGenerelt', '10.0.7');
    END IF;
END $EF$;
COMMIT;

