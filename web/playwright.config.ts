import { defineConfig } from '@playwright/test';

// Browser tests for the in-browser (Pyodide) IDS audit pipeline. Not part of `npm run check`.
// Needs public/worker/bin/{ifcopenshell,odfpy,ifctester}*.whl (run scripts/download-packages.sh first).
export default defineConfig({
    testDir: 'tests/e2e',
    timeout: 300_000,
    expect: { timeout: 30_000 },
    fullyParallel: false,
    workers: 1,
    retries: process.env.CI ? 1 : 0,
    reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : 'list',
    use: {
        baseURL: 'http://localhost:5199',
        trace: 'retain-on-failure',
        launchOptions: process.env.PW_CHROMIUM_PATH ? { executablePath: process.env.PW_CHROMIUM_PATH, args: ['--no-sandbox'] } : {},
    },
    webServer: {
        command: 'npx vite --port 5199 --strictPort',
        url: 'http://localhost:5199',
        reuseExistingServer: !process.env.CI,
        timeout: 120_000,
    },
});
