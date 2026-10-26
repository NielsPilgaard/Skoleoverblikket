import { defineConfig, devices } from '@playwright/test'

// Read-only smoke test against production, run by the CD watch job after each deploy (task 44).
// Run locally with SMOKE_USER and SMOKE_PASSWORD set: npx playwright test -c playwright.smoke.config.ts
export default defineConfig({
  testDir: './tests/smoke',
  workers: 1,
  // One automatic retry, so a single network blip doesn't roll back a deploy.
  retries: 1,
  timeout: 60_000,
  reporter: 'line',
  use: {
    baseURL: process.env.SMOKE_BASE_URL ?? 'https://skoleoverblikket.dk',
    // The repo and its Actions logs are public: traces, videos and screenshots would expose the
    // smoke user's session.
    trace: 'off',
    video: 'off',
    screenshot: 'off',
  },
  projects: [
    {
      name: 'smoke',
      // CI uses the Chrome preinstalled on GitHub's runners, so the CD job skips the browser install.
      use: { ...devices['Desktop Chrome'], channel: process.env.CI ? 'chrome' : undefined },
    },
  ],
})
