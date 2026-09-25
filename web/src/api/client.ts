import keycloak from '../auth/keycloak'
import { client } from './generated/client.gen'

export const API_BASE = '/api/v1'

async function getToken(): Promise<string> {
  await keycloak.updateToken(30).catch(() => {
    keycloak.login()
    throw new Error('Not authenticated')
  })
  if (!keycloak.token) {
    keycloak.login()
    throw new Error('Not authenticated')
  }
  return keycloak.token
}

// Configure the generated client with Keycloak bearer auth and the correct base URL.
client.setConfig({
  baseUrl: '',
  auth: getToken,
})

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string
  ) {
    super(message)
  }
}

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const token = await getToken()

  const res = await fetch(`${API_BASE}${path}`, {
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    },
    ...options,
  })
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText)
    throw new ApiError(res.status, text)
  }
  if (res.status === 204) return undefined as T
  return res.json()
}

async function requestForm<T>(path: string, body: FormData): Promise<T> {
  const token = await getToken()

  const res = await fetch(`${API_BASE}${path}`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${token}` },
    body,
  })
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText)
    throw new ApiError(res.status, text)
  }
  if (res.status === 204) return undefined as T
  return res.json()
}

export const api = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body: unknown) =>
    request<T>(path, { method: 'POST', body: JSON.stringify(body) }),
  postForm: <T>(path: string, body: FormData) => requestForm<T>(path, body),
  put: <T>(path: string, body: unknown) =>
    request<T>(path, { method: 'PUT', body: JSON.stringify(body) }),
  patch: <T>(path: string, body: unknown) =>
    request<T>(path, { method: 'PATCH', body: JSON.stringify(body) }),
  delete: (path: string) => request<void>(path, { method: 'DELETE' }),
}

// Re-export all generated types — stable (non-prefixed) names kept as aliases
export type {
  CalendarEntryType,
  ConflictInfo,
  ConflictType,
  DayOfWeek,
  StaffRole,
  AbsenceStatus,
  SubscriptionStatus,
  BillingInterval,
  NotificationType,
  SubjectCategory,
} from './generated/types.gen'

export type {
  TimeSlotsControllerBreakDto as BreakDto,
  CalendarControllerCalendarEntryDto as CalendarEntryDto,
  CalendarControllerDefaultHolidayDto as DefaultHolidayDto,
  ClassesControllerClassDto as ClassDto,
  ClassesControllerYearRollCreateEntry as YearRollCreateEntry,
  ClassesControllerYearRollRenameEntry as YearRollRenameEntry,
  ClassPermissionsControllerClassPermissionDto as ClassPermissionDto,
  CoursesControllerCourseDto as CourseDto,
  FilesControllerFileDto as FileDto,
  FilesControllerFolderDto as FolderDto,
  RoomsControllerRoomDto as RoomDto,
  SchemasControllerSchemaDetailDto as SchemaDetailDto,
  SchemasControllerSchemaDto as SchemaDto,
  SchemasControllerSlotDto as SlotDto,
  SchemasControllerSlotsAndConflictsDto as SlotsAndConflictsDto,
  SchedulesControllerScheduleSlotDto as ScheduleSlotDto,
  SchoolsControllerOnboardingStatusDto as OnboardingStatusDto,
  SchoolsControllerSchoolSettingsDto as SchoolSettingsDto,
  SfoControllerSfoShiftDto as SfoShiftDto,
  SfoWeekPlanControllerSfoWeekPlanDto as SfoWeekPlanDto,
  SfoWeekPlanControllerSfoWeekPlanShiftDto as SfoWeekPlanShiftDto,
  StaffControllerStaffDto as StaffDto,
  StaffInvitationsControllerInvitationDto as InvitationDto,
  StatsControllerDashboardStats as DashboardStats,
  StudentsControllerStudentDto as StudentDto,
  ParentsControllerParentDto as ParentDto,
  ParentsControllerParentSummaryDto as ParentSummaryDto,
  ParentMeControllerParentMeDto as ParentMeDto,
  TimeSlotsControllerTemplateDto as TemplateDto,
  TimeSlotsControllerTimeSlotDto as TimeSlotDto,
  WeekPlanControllerWeekPlanDto as WeekPlanDto,
  WeekPlanControllerWeekPlanSlotDto as WeekPlanSlotDto,
  WeekPlanControllerWeekPlanSlotFileDto as WeekPlanSlotFileDto,
  BillingControllerSubscriptionDto as SubscriptionDto,
} from './generated/types.gen'
