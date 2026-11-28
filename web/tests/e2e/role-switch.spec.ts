import { test, expect } from '@playwright/test'
import { api, parentInvitationToken } from './parents'

// One login, several roles (task 61): the seeded admin is also invited as a parent. The API links the
// invitation to the existing login, so "Vis som" appears, and switching shows the parent schedule.

const ADMIN_EMAIL = 'admin@debugskolen.dk'

test('a teacher who is also a parent switches with "Vis som" and back', async ({ page }) => {
  test.setTimeout(90_000)
  await page.goto('/dashboard')

  const klass = await api<{ id: string }>(page, 'POST', '/classes', { name: `E2E Vis som ${Date.now()}`, gradeLevel: 2 })
  const student = await api<{ id: string }>(page, 'POST', '/students', {
    name: 'Elev Vis Som',
    classId: klass.id,
    isEnrolledInSfo: false,
  })
  const parent = await api<{ id: string }>(page, 'POST', '/parents/invite', {
    name: 'Debug Admin',
    email: ADMIN_EMAIL,
    studentIds: [student.id],
  })

  try {
    await page.reload()
    await expect(page.getByTestId('role-switcher')).toBeVisible({ timeout: 15_000 })
    await expect(page.getByTestId('role-switcher-staff').getByRole('radio')).toBeChecked()

    await page.getByTestId('role-switcher-parent').click()
    await page.waitForURL(/\/foraeldrevisning\/skema/, { timeout: 15_000 })
    await expect(page.getByTestId('parent-schema-page')).toBeVisible({ timeout: 15_000 })
    await expect(page.getByTestId('role-switcher-parent').getByRole('radio')).toBeChecked()

    await page.getByTestId('role-switcher-staff').click()
    await page.waitForURL(/\/dashboard/, { timeout: 15_000 })
    await expect(page.getByTestId('dashboard-quick-actions')).toBeVisible({ timeout: 15_000 })
  } finally {
    // The admin's login is shared with their staff row, so removing the parent keeps it.
    await api(page, 'DELETE', `/parents/${parent.id}`)
  }

  await page.reload()
  await expect(page.getByTestId('dashboard-quick-actions')).toBeVisible({ timeout: 15_000 })
  await expect(page.getByTestId('role-switcher')).toBeHidden()
})

test('a teacher who accepts a parent invitation lands in the parent view', async ({ page }) => {
  test.setTimeout(90_000)
  await page.goto('/dashboard')

  const klass = await api<{ id: string }>(page, 'POST', '/classes', { name: `E2E Accept ${Date.now()}`, gradeLevel: 3 })
  const student = await api<{ id: string }>(page, 'POST', '/students', {
    name: 'Elev Accept',
    classId: klass.id,
    isEnrolledInSfo: false,
  })
  const invitedAt = Date.now() - 1_000
  const parent = await api<{ id: string }>(page, 'POST', '/parents/invite', {
    name: 'Debug Admin',
    email: ADMIN_EMAIL,
    studentIds: [student.id],
  })

  try {
    const token = await parentInvitationToken(page, ADMIN_EMAIL, invitedAt)
    // ?accept=1 is where Keycloak returns after login; the admin is already logged in.
    await page.goto(`/parent-invitation/${token}?accept=1`)

    // The contact step calls a parent-only endpoint, so it fails unless the page acts as the parent.
    await page.getByTestId('invitation-contact-submit').click({ timeout: 15_000 })
    await page.waitForURL(/\/foraeldrevisning\/skema/, { timeout: 15_000 })
    await expect(page.getByTestId('parent-schema-page')).toBeVisible({ timeout: 15_000 })
    await expect(page.getByTestId('role-switcher-parent').getByRole('radio')).toBeChecked()
  } finally {
    await api(page, 'DELETE', `/parents/${parent.id}`)
  }
})
