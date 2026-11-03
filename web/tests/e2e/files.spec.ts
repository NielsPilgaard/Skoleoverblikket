import { test, expect, type Page } from '@playwright/test'

test.describe('FilesPage — folders', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/filer')
    await expect(page.getByRole('heading', { name: 'Filer' })).toBeVisible({ timeout: 15_000 })
  })

  test('create folder button is visible for admin', async ({ page }) => {
    await expect(page.getByTestId('create-folder-btn')).toBeVisible()
  })

  test('opening create folder modal and cancelling', async ({ page }) => {
    await page.getByTestId('create-folder-btn').click()
    await expect(page.getByRole('heading', { name: 'Opret mappe' })).toBeVisible()
    await page.getByRole('button', { name: 'Annuller' }).click()
    await expect(page.getByRole('heading', { name: 'Opret mappe' })).not.toBeVisible()
  })

  test('create folder, navigate into it, and see empty state', async ({ page }) => {
    const folderName = `E2E-Mappe-${Date.now()}`

    await page.getByTestId('create-folder-btn').click()
    await expect(page.getByRole('heading', { name: 'Opret mappe' })).toBeVisible()
    await page.locator('input[placeholder*="Matematik"]').fill(folderName)
    await page.getByRole('button', { name: 'Opret' }).click()

    await expect(page.getByRole('cell', { name: folderName })).toBeVisible({ timeout: 10_000 })

    const folderRow = page.locator('tr', { hasText: folderName })
    await folderRow.click()

    await expect(page.getByText(folderName)).toBeVisible()
    await expect(page.getByText('Mappen er tom')).toBeVisible()

    await page.getByTestId('breadcrumb-root').click()
    await expect(page.getByText('Mappen er tom')).not.toBeVisible()
  })

  test('rename folder via "Omdøb"', async ({ page }) => {
    const originalName = `E2E-Omdøb-${Date.now()}`
    const renamedName = `${originalName}-RENAMED`

    await page.getByTestId('create-folder-btn').click()
    await page.locator('input[placeholder*="Matematik"]').fill(originalName)
    await page.getByRole('button', { name: 'Opret' }).click()
    await expect(page.getByRole('cell', { name: originalName })).toBeVisible({ timeout: 10_000 })

    const folderRow = page.locator('tr', { hasText: originalName })
    await folderRow.locator('[data-testid^="rename-folder-"]').click()

    // After clicking rename, the span is replaced by an inline input.
    // folderRow no longer matches by text, so locate the input globally.
    const input = page.locator('input[type="text"]').first()
    await input.fill(renamedName)
    await input.press('Enter')

    await expect(page.getByRole('cell', { name: renamedName, exact: true })).toBeVisible({ timeout: 10_000 })
    await expect(page.getByRole('cell', { name: originalName, exact: true })).not.toBeVisible({ timeout: 5_000 })
  })

  test('delete folder with confirmation', async ({ page }) => {
    const folderName = `E2E-Slet-${Date.now()}`

    await page.getByTestId('create-folder-btn').click()
    await page.locator('input[placeholder*="Matematik"]').fill(folderName)
    await page.getByRole('button', { name: 'Opret' }).click()
    await expect(page.getByRole('cell', { name: folderName })).toBeVisible({ timeout: 10_000 })

    page.once('dialog', (dialog) => dialog.accept())

    const folderRow = page.locator('tr', { hasText: folderName })
    await folderRow.locator('[data-testid^="delete-folder-"]').click()

    await expect(page.getByRole('cell', { name: folderName })).not.toBeVisible({ timeout: 10_000 })
  })
})

test.describe('FilesPage — upload modal rename', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/filer')
    await expect(page.getByRole('heading', { name: 'Filer' })).toBeVisible({ timeout: 15_000 })
  })

  test('filename input appears after selecting a file and is pre-filled without extension', async ({ page }) => {
    await page.getByRole('button', { name: 'Upload fil' }).click()
    await expect(page.getByRole('heading', { name: 'Upload fil' })).toBeVisible()

    await expect(page.getByLabel('Filnavn')).not.toBeVisible()

    const [fileChooser] = await Promise.all([
      page.waitForEvent('filechooser'),
      page.locator('input[type="file"]').evaluate((el: HTMLInputElement) => el.click()),
    ])
    await fileChooser.setFiles({
      name: 'rapport_Q1.pdf',
      mimeType: 'application/pdf',
      buffer: Buffer.from('PDF test content'),
    })

    const nameInput = page.getByLabel('Filnavn')
    await expect(nameInput).toBeVisible()
    await expect(nameInput).toHaveValue('rapport_Q1')
  })

  test('editing filename changes the name used for presign', async ({ page }) => {
    await page.getByRole('button', { name: 'Upload fil' }).click()

    const [fileChooser] = await Promise.all([
      page.waitForEvent('filechooser'),
      page.locator('input[type="file"]').evaluate((el: HTMLInputElement) => el.click()),
    ])
    await fileChooser.setFiles({
      name: 'IMG_20240301_142233.jpg',
      mimeType: 'image/jpeg',
      buffer: Buffer.from('fake image'),
    })

    const nameInput = page.getByLabel('Filnavn')
    await expect(nameInput).toHaveValue('IMG_20240301_142233')

    await nameInput.fill('Klassefoto forår 2024')
    await expect(nameInput).toHaveValue('Klassefoto forår 2024')
  })
})

test.describe('FilesPage — file preview modal', () => {
  // Upload a tiny PNG before each test so preview modal always has an image file to open.
  // PDF files bypass the modal (open in new tab), so we need a known image file.
  async function uploadTestImage(page: import('@playwright/test').Page) {
    await page.goto('/filer')
    await expect(page.getByRole('heading', { name: 'Filer' })).toBeVisible({ timeout: 15_000 })

    await page.getByRole('button', { name: 'Upload fil' }).click()
    await expect(page.getByRole('heading', { name: 'Upload fil' })).toBeVisible()

    const [fileChooser] = await Promise.all([
      page.waitForEvent('filechooser'),
      page.locator('input[type="file"]').evaluate((el: HTMLInputElement) => el.click()),
    ])
    // 1×1 white PNG (smallest valid PNG)
    const pngBytes = Buffer.from(
      'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==',
      'base64',
    )
    await fileChooser.setFiles({ name: 'e2e-preview-test.png', mimeType: 'image/png', buffer: pngBytes })
    await expect(page.getByLabel('Filnavn')).toHaveValue('e2e-preview-test')

    await page.getByRole('button', { name: 'Upload', exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Upload fil' })).not.toBeVisible({ timeout: 15_000 })
  }

  test('clicking "Vis" on a file opens preview modal', async ({ page }) => {
    await uploadTestImage(page)
    const fileRow = page.locator('[data-testid^="file-row-"]').filter({ hasText: 'e2e-preview-test' }).first()
    await fileRow.locator('[data-testid^="preview-"]').click()
    await expect(page.locator('button[title="Luk"]')).toBeVisible({ timeout: 5_000 })
  })

  test('clicking file name opens preview modal', async ({ page }) => {
    await uploadTestImage(page)
    const fileRow = page.locator('[data-testid^="file-row-"]').filter({ hasText: 'e2e-preview-test' }).first()
    await fileRow.locator('button').first().click()
    await expect(page.locator('button[title="Luk"]')).toBeVisible({ timeout: 5_000 })
  })

  test('preview modal closes when clicking close button', async ({ page }) => {
    await uploadTestImage(page)
    const fileRow = page.locator('[data-testid^="file-row-"]').filter({ hasText: 'e2e-preview-test' }).first()
    await fileRow.locator('[data-testid^="preview-"]').click()
    const closeBtn = page.locator('button[title="Luk"]')
    await expect(closeBtn).toBeVisible({ timeout: 5_000 })
    await closeBtn.click()
    await expect(closeBtn).not.toBeVisible()
  })

  test('preview modal closes on Escape', async ({ page }) => {
    await uploadTestImage(page)
    const fileRow = page.locator('[data-testid^="file-row-"]').filter({ hasText: 'e2e-preview-test' }).first()
    await fileRow.locator('[data-testid^="preview-"]').click()
    await expect(page.locator('button[title="Luk"]')).toBeVisible({ timeout: 5_000 })
    await page.keyboard.press('Escape')
    await expect(page.locator('button[title="Luk"]')).not.toBeVisible()
  })

  test('preview modal closes on backdrop click', async ({ page }) => {
    await uploadTestImage(page)
    const fileRow = page.locator('[data-testid^="file-row-"]').filter({ hasText: 'e2e-preview-test' }).first()
    await fileRow.locator('[data-testid^="preview-"]').click()
    await expect(page.locator('button[title="Luk"]')).toBeVisible({ timeout: 5_000 })
    // Click the backdrop overlay directly (the fixed inset-0 div that wraps the modal)
    const backdrop = page.locator('.fixed.inset-0.bg-black\\/60')
    await backdrop.click({ position: { x: 640, y: 5 }, force: true })
    await expect(page.locator('button[title="Luk"]')).not.toBeVisible({ timeout: 3_000 })
  })
})

test.describe('FilesPage — order', () => {
  async function createFolder(page: Page, name: string) {
    await page.getByTestId('create-folder-btn').click()
    await page.getByTestId('folder-name-input').fill(name)
    await page.getByTestId('folder-create-submit').click()
    await expect(page.getByTestId('folder-name-input')).toBeHidden()
  }

  // In name order the file comes before both folders, and it is the newest, so without pinning
  // it would lead in ascending name and descending date.
  test('folders stay on top in every sort and direction, on laptop and phone', async ({ page }) => {
    test.setTimeout(120_000)
    const parent = `E2E-Rækkefølge-${Date.now()}`

    await page.goto('/filer')
    await expect(page.getByTestId('create-folder-btn')).toBeVisible({ timeout: 15_000 })
    await createFolder(page, parent)
    const list = page.getByTestId('file-list')
    const parentRow = list.locator('[data-testid^="folder-row-"]', { hasText: parent })
    await parentRow.click()
    await expect(page.getByTestId('breadcrumb-root')).toBeVisible()

    await createFolder(page, 'B-mappe')
    await createFolder(page, 'Å-mappe')
    await page.getByTestId('upload-file-btn').click()
    await page
      .getByTestId('upload-file-input')
      .setInputFiles({ name: 'A-fil.txt', mimeType: 'text/plain', buffer: Buffer.from('hej') })
    await page.getByTestId('upload-file-submit').click()

    const rows = list.locator('[data-testid^="folder-row-"], [data-testid^="file-row-"]')
    const ascending = [/B-mappe/, /Å-mappe/, /A-fil/]
    const descending = [/Å-mappe/, /B-mappe/, /A-fil/]
    await expect(rows).toHaveText(ascending, { timeout: 30_000 })

    // Laptop: column headers. The first click on the sorted column flips it, others start ascending.
    await page.getByTestId('list-sort-name').click()
    await expect(rows).toHaveText(descending)
    await page.getByTestId('list-sort-date').click()
    await expect(rows).toHaveText(ascending)
    await page.getByTestId('list-sort-date').click()
    await expect(rows).toHaveText(descending)
    // Every row has the same (no) course, so only the file's place says anything here.
    await page.getByTestId('list-sort-course').click()
    await expect(rows.last()).toHaveText(/A-fil/)
    await page.getByTestId('list-sort-course').click()
    await expect(rows.last()).toHaveText(/A-fil/)

    // Phone: the sort select and the direction button.
    await page.setViewportSize({ width: 375, height: 800 })
    await page.getByTestId('list-sort').selectOption('name')
    const nameFirst = await rows.first().textContent()
    await page.getByTestId('list-sort-direction').click()
    await expect(rows.first()).not.toHaveText(nameFirst ?? '')
    await expect(rows.last()).toHaveText(/A-fil/)
    await page.getByTestId('list-sort').selectOption('date')
    await expect(rows.last()).toHaveText(/A-fil/)
    await page.getByTestId('list-sort-direction').click()
    await expect(rows.last()).toHaveText(/A-fil/)

    await page.setViewportSize({ width: 1280, height: 720 })
    await page.getByTestId('breadcrumb-root').click()
    page.once('dialog', (dialog) => dialog.accept())
    await parentRow.locator('[data-testid^="delete-folder-"]').click()
    await expect(parentRow).toBeHidden({ timeout: 10_000 })
  })
})
