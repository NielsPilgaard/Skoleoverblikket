import { test, expect, type APIRequestContext, type Browser, type Page } from '@playwright/test'

// The read-only schedule grids: each lesson shows once, under its own weekday and time row.
// The parent is a real account created through the parent invitation email in Mailpit.

const MAILPIT_API = 'http://localhost:8025/api/v1'

type Kc = { token: string; authenticated?: boolean; updateToken: (n: number) => Promise<boolean> }

async function api<T>(page: Page, method: string, path: string, body?: unknown): Promise<T> {
  // Right after a navigation keycloak-js may still be initialising, with no refresh token yet.
  await page.waitForFunction(() => (window as unknown as { __keycloak?: Kc }).__keycloak?.authenticated === true)
  const result = await page.evaluate(
    async ({ method, path, body }) => {
      const kc = (window as unknown as { __keycloak: Kc }).__keycloak
      await kc.updateToken(30)
      const res = await fetch(`/api/v1${path}`, {
        method,
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${kc.token}` },
        body: body === undefined ? undefined : JSON.stringify(body),
      })
      const text = await res.text()
      return { ok: res.ok, status: res.status, json: text ? JSON.parse(text) : null }
    },
    { method, path, body }
  )
  expect(result.ok, `${method} ${path} failed with ${result.status}`).toBeTruthy()
  return result.json as T
}

async function findMail(request: APIRequestContext, to: string): Promise<string> {
  for (let i = 0; i < 20; i++) {
    const res = await request.get(`${MAILPIT_API}/messages`)
    const body = (await res.json()) as { messages?: { ID: string; To: { Address: string }[] }[] }
    const msg = (body.messages ?? []).find((m) => m.To.some((t) => t.Address === to))
    if (msg) {
      const detail = (await (await request.get(`${MAILPIT_API}/message/${msg.ID}`)).json()) as {
        HTML?: string
        Text?: string
      }
      return `${detail.Text ?? ''}\n${detail.HTML ?? ''}`
    }
    await new Promise((r) => setTimeout(r, 500))
  }
  throw new Error(`No email to ${to}`)
}

/** Invites a parent for the student and logs them in through the invitation. Returns their page. */
async function parentSession(
  admin: Page,
  browser: Browser,
  request: APIRequestContext,
  studentId: string
): Promise<Page> {
  const email = `foraelder-skema-${Date.now()}@testskole.dk`
  await api(admin, 'POST', '/parents/invite', { name: 'E2E Forælder', email, studentIds: [studentId] })

  const mail = await findMail(request, email)
  const link = mail.match(/http[^"'\s<>]+\/parent-invitation\/[^"'\s<>]+/)?.[0].replace(/&amp;/g, '&')
  expect(link, 'No parent invitation link').toBeDefined()

  // The parent chooses their own password on the invitation page and is logged in right away.
  const context = await browser.newContext({ storageState: { cookies: [], origins: [] } })
  const page = await context.newPage()
  await page.goto(link!)
  await page.waitForURL((url) => url.port === '5173', { timeout: 30_000 })
  await page.getByTestId('invitation-password').fill('NewPass456!')
  await page.getByTestId('invitation-password-submit').click()

  await page.waitForURL(/\/parent-invitation\/.*accept=1/, { timeout: 20_000 })
  await page.getByRole('button', { name: /spring over/i }).click()
  await page.waitForURL(/\/foraeldrevisning\//, { timeout: 20_000 })
  return page
}

/** Creates a class whose active schema has one time slot, with the given lessons in it. */
async function createClassWithLessons(
  admin: Page,
  lesson: { name: string; startTime: string; endTime: string; weekdays: string[]; courseId: string; teacherId: string }
) {
  const klass = await api<{ id: string }>(admin, 'POST', '/classes', { name: lesson.name, gradeLevel: 3 })
  const schema = await api<{ id: string }>(admin, 'POST', `/classes/${klass.id}/schemas`, { name: 'Skema' })

  const year = new Date().getFullYear()
  await api(admin, 'PUT', `/classes/${klass.id}/schemas/${schema.id}/daterange`, {
    startDate: `${year - 1}-01-01`,
    endDate: `${year + 1}-12-31`,
  })
  const [timeSlot] = await api<{ id: string }[]>(admin, 'PUT', `/classes/${klass.id}/schemas/${schema.id}/time-slots`, [
    { sortOrder: 1, startTime: lesson.startTime, endTime: lesson.endTime, label: '1.' },
  ])
  for (const weekday of lesson.weekdays) {
    await api(admin, 'PUT', `/classes/${klass.id}/schemas/${schema.id}/slots`, {
      timeSlotId: timeSlot.id,
      weekday,
      courseId: lesson.courseId,
      teacherId: lesson.teacherId,
    })
  }
  return klass.id
}

test.describe.serial('Skemavisninger', () => {
  test('parent sees lessons under the right weekday', async ({ page, browser, request }) => {
    test.setTimeout(180_000)
    await page.goto('/dashboard')

    const stamp = Date.now()
    const course = await api<{ id: string }>(page, 'POST', '/courses', { name: `Dansk ${stamp}` })
    const teacher = await api<{ id: string }>(page, 'POST', '/staff', {
      name: `E2E Lærer ${stamp}`,
      email: `laerer-skema-${stamp}@testskole.dk`,
      role: 'Teacher',
    })
    const classId = await createClassWithLessons(page, {
      name: `E2E skema ${stamp}`,
      startTime: '08:00',
      endTime: '08:45',
      weekdays: ['Monday', 'Friday'],
      courseId: course.id,
      teacherId: teacher.id,
    })
    const student = await api<{ id: string }>(page, 'POST', '/students', {
      name: `Elev ${stamp}`,
      classId,
      isEnrolledInSfo: false,
    })

    const parent = await parentSession(page, browser, request, student.id)
    await parent.goto('/foraeldrevisning/skema')

    const cell = (weekday: string) => parent.getByTestId(`parent-schedule-cell-08:00-08:45-${weekday}`)
    await expect(cell('Monday')).toContainText(`Dansk ${stamp}`, { timeout: 15_000 })
    await expect(cell('Friday')).toContainText(`Dansk ${stamp}`)
    await expect(cell('Tuesday')).toBeEmpty()
    await parent.context().close()
  })

  test('a lesson shows once when another class has the same start but a later end', async ({ page }) => {
    await page.goto('/dashboard')

    const stamp = Date.now()
    const course = await api<{ id: string }>(page, 'POST', '/courses', { name: `Matematik ${stamp}` })
    const teacher = await api<{ id: string }>(page, 'POST', '/staff', {
      name: `E2E Lærer ${stamp}`,
      email: `laerer-raekker-${stamp}@testskole.dk`,
      role: 'Teacher',
    })
    await createClassWithLessons(page, {
      name: `E2E kort ${stamp}`,
      startTime: '08:00',
      endTime: '08:45',
      weekdays: ['Monday'],
      courseId: course.id,
      teacherId: teacher.id,
    })
    await createClassWithLessons(page, {
      name: `E2E lang ${stamp}`,
      startTime: '08:00',
      endTime: '09:30',
      weekdays: ['Tuesday'],
      courseId: course.id,
      teacherId: teacher.id,
    })

    await page.goto(`/medarbejdere/${teacher.id}/skema`)

    const cell = (row: string, weekday: string) => page.getByTestId(`staff-schedule-cell-${row}-${weekday}`)
    await expect(cell('08:00-08:45', 'Monday')).toContainText(`E2E kort ${stamp}`, { timeout: 15_000 })
    await expect(cell('08:00-09:30', 'Tuesday')).toContainText(`E2E lang ${stamp}`)
    await expect(cell('08:00-08:45', 'Tuesday')).toBeEmpty()
    await expect(cell('08:00-09:30', 'Monday')).toBeEmpty()
  })
})
