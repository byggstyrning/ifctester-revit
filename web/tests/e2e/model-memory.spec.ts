import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { type Page, type Route, expect, test } from '@playwright/test';

const FIX = join(import.meta.dirname, '..', 'fixtures');
const read = (name: string) => readFileSync(join(FIX, name), 'utf8');

const REVIT = 'http://localhost:48881';
const CORS = {
    'access-control-allow-origin': '*',
    'access-control-allow-methods': 'GET, POST, OPTIONS',
    'access-control-allow-headers': 'Content-Type',
};

const CONFIGURATION = 'IFC4 Reference View';
const DEFAULT_FOLDER = '\\\\bim-byggp1.hogerklick.bim\\H29\\BIM-tools\\pset';
const IDS_PATH = '\\\\bim-byggp1.hogerklick.bim\\H29\\BIM-tools\\ids\\specs-ifc4.ids';
const PSET_PATH = `${DEFAULT_FOLDER}\\Byggpartner H29.txt`;
const MODEL = { key: '\\\\srv\\H29\\K-20-V-100-6300-000.rvt', title: 'K-20-V-100-6300-000_vilhe', workshared: true };

type File = { path: string; name: string; exists: boolean; error: string | null };
const file = (path: string, exists = true): File => ({ path, name: path.split('\\').pop() ?? path, exists, error: null });

type Memory = {
    model: typeof MODEL | null;
    message: string | null;
    remembered: { updated: string | null; configuration: string | null; idsFile: File | null; psetFile: File | null; parameterMappingFile: File | null } | null;
};

type Seen = { paths: string[]; idsReads: string[]; memoryPosts: Record<string, unknown>[]; exports: number };

// Same package-download escape hatch as audit.spec.ts, for sandboxes whose browser cannot reach PyPI.
test.beforeEach(async ({ page }) => {
    if (!process.env.E2E_ROUTE_NETWORK) return;
    await page.route(/^https:\/\/(pypi\.org|files\.pythonhosted\.org|cdn\.jsdelivr\.net)\//, async (route) => {
        const r = await fetch(route.request().url());
        const headers = Object.fromEntries(
            [...r.headers].filter(([k]) => !/^(content-encoding|content-length|transfer-encoding)$/i.test(k)),
        );
        await route.fulfill({ status: r.status, headers: { ...headers, 'access-control-allow-origin': '*' }, body: Buffer.from(await r.arrayBuffer()) });
    });
});

/** The add-in with a model open; `memory` is what it remembers for it. */
async function mockRevit(page: Page, capabilities: string[], memory: Memory): Promise<Seen> {
    const seen: Seen = { paths: [], idsReads: [], memoryPosts: [], exports: 0 };
    const json = (route: Route, body: unknown, status = 200) =>
        route.fulfill({ status, headers: CORS, contentType: 'application/json', body: JSON.stringify(body) });

    await page.route(`${REVIT}/**`, async (route) => {
        const request = route.request();
        const url = new URL(request.url());
        const path = url.pathname;
        seen.paths.push(`${request.method()} ${path}`);

        if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS });
        if (path === '/status') return json(route, { status: 'ok', connected: true, configsReady: true, version: '1.4.0', capabilities });
        if (path === '/ifc-configurations') return json(route, { configurations: ['Other setup', CONFIGURATION] });
        if (path === '/ifc-configuration-files') {
            return json(route, {
                psetFile: null,
                psetFileExists: false,
                psetFileIsOverride: false,
                parameterMappingFile: null,
                parameterMappingFileExists: false,
                parameterMappingFileIsOverride: false,
                warning: null,
            });
        }
        if (path === '/pset-files') {
            const dir = url.searchParams.get('dir') ?? '';
            return json(route, { files: [{ name: 'Byggpartner H29.txt', path: `${dir}\\Byggpartner H29.txt`, modified: '2026-09-30T12:00:00Z' }] });
        }
        if (path === '/model-memory' && request.method() === 'GET') return json(route, memory);
        if (path === '/model-memory' && request.method() === 'POST') {
            const body = request.postDataJSON() as { idsFile: string };
            seen.memoryPosts.push(body);
            memory.remembered = { updated: '2026-10-05T12:00:00Z', configuration: null, psetFile: null, parameterMappingFile: null, ...memory.remembered, idsFile: file(body.idsFile) };
            return json(route, memory);
        }
        if (path === '/ids-files/read') {
            const idsPath = url.searchParams.get('path') ?? '';
            seen.idsReads.push(idsPath);
            return json(route, { cancelled: false, path: idsPath, name: idsPath.split('\\').pop(), content: read('specs-ifc4.ids') });
        }
        if (path === '/ids-files/pick') return json(route, { cancelled: false, path: IDS_PATH, name: 'specs-ifc4.ids', content: read('specs-ifc4.ids') });
        if (path === '/export-ifc') {
            seen.exports++;
            return json(route, { error: 'not in this test' }, 500);
        }
        return json(route, { error: `Not mocked: ${path}` }, 404);
    });
    return seen;
}

const rememberedAll = (): Memory => ({
    model: MODEL,
    message: null,
    remembered: { updated: '2026-10-05T10:00:00Z', configuration: CONFIGURATION, idsFile: file(IDS_PATH), psetFile: file(PSET_PATH), parameterMappingFile: null },
});

test.describe('choices remembered per Revit model', () => {
    test('the IDS, export setup and pset file last used are preselected, nothing runs', async ({ page }) => {
        const revit = await mockRevit(page, ['writeback', 'export-overrides', 'model-memory'], rememberedAll());
        await page.goto('/?source=revit');

        // The IDS is read again from its path and opened
        await expect(page.getByRole('heading', { name: 'IFC4 regression' })).toBeVisible();
        expect(revit.idsReads).toEqual([IDS_PATH]);

        const memory = page.getByLabel('Model memory');
        await expect(memory).toContainText(`Last used for this model: specs-ifc4.ids, ${CONFIGURATION}, Byggpartner H29.txt`);
        await expect(memory.getByRole('button', { name: 'change' })).toBeVisible();

        await expect(page.locator('select.config-select')).toHaveValue(CONFIGURATION);
        await expect(page.getByLabel('Property set file', { exact: true })).toHaveValue(PSET_PATH);
        await expect(page.getByLabel('Property set file path')).toHaveValue(PSET_PATH);

        // Only preselected: no export, no audit
        await page.waitForTimeout(1000);
        expect(revit.exports).toBe(0);
        expect(revit.paths.filter((p) => p.startsWith('POST'))).toEqual([]);
        await expect(page.getByText('Audit Reports')).toHaveCount(0);
    });

    test('a remembered file that is gone is reported and left to choose by hand', async ({ page }) => {
        const memory = rememberedAll();
        memory.remembered = {
            ...memory.remembered!,
            idsFile: file(IDS_PATH, false),
            psetFile: { ...file(PSET_PATH, false), error: 'The folder did not answer in time.' },
        };
        const revit = await mockRevit(page, ['writeback', 'export-overrides', 'model-memory'], memory);
        await page.goto('/?source=revit');

        await expect(page.getByText(`The IDS last used for this model was not found: ${IDS_PATH}. Choose one manually.`)).toBeVisible();
        await expect(page.getByText(`The property set file last used for this model could not be checked (The folder did not answer in time.): ${PSET_PATH}. Choose one manually.`)).toBeVisible();
        expect(revit.idsReads).toEqual([]);

        // No IDS opened, no override set; the setup, which still exists, is preselected
        await expect(page.getByRole('heading', { name: 'Get Started' })).toBeVisible();
        await expect(page.locator('select.config-select')).toHaveValue(CONFIGURATION);
        await expect(page.getByLabel('Property set file path')).toHaveValue('');
    });

    test('a setup that no longer exists is reported, not selected', async ({ page }) => {
        const memory = rememberedAll();
        memory.remembered = { ...memory.remembered!, configuration: 'Deleted setup', idsFile: null, psetFile: null };
        await mockRevit(page, ['writeback', 'export-overrides', 'model-memory'], memory);
        await page.goto('/?source=revit');

        await expect(page.getByText('The export setup last used for this model, Deleted setup, is not among this model\'s setups. Choose one.')).toBeVisible();
        await expect(page.locator('select.config-select')).toHaveValue('');
    });

    test('Open IDS goes through Revit\'s dialog and is remembered for the model', async ({ page }) => {
        const revit = await mockRevit(page, ['writeback', 'export-overrides', 'model-memory'], { model: MODEL, message: null, remembered: null });
        await page.goto('/?source=revit');

        const memory = page.getByLabel('Model memory');
        await expect(memory).toContainText('Nothing remembered for K-20-V-100-6300-000_vilhe yet.');

        // The page's own Open IDS button asks the add-in, not the browser's file picker
        let browserPicker = false;
        page.on('filechooser', () => {
            browserPicker = true;
        });
        await page.getByRole('button', { name: 'Open IDS', exact: true }).click();
        await expect(page.getByRole('heading', { name: 'IFC4 regression' })).toBeVisible();
        expect(browserPicker).toBe(false);
        expect(revit.paths).toContain('POST /ids-files/pick');

        await expect.poll(() => revit.memoryPosts).toEqual([{ idsFile: IDS_PATH }]);
        await expect(memory).toContainText('Last used for this model: specs-ifc4.ids');
    });

    test('an unsaved model remembers nothing and says so', async ({ page }) => {
        await mockRevit(page, ['writeback', 'export-overrides', 'model-memory'], {
            model: null,
            message: 'No saved model is open in Revit, so IfcTester remembers nothing for it.',
            remembered: null,
        });
        await page.goto('/?source=revit');
        await expect(page.getByText('No saved model is open in Revit, so IfcTester remembers nothing for it.')).toBeVisible();
        await expect(page.locator('select.config-select')).toHaveValue('');
    });

    test('an older add-in without the capability: no memory, browser file picker as before', async ({ page }) => {
        const revit = await mockRevit(page, ['writeback', 'export-overrides'], rememberedAll());
        await page.goto('/?source=revit');
        await expect(page.locator('select.config-select')).toBeVisible();

        await expect(page.getByRole('heading', { name: 'This Model' })).toHaveCount(0);
        await expect(page.locator('select.config-select')).toHaveValue('');
        const chooser = page.waitForEvent('filechooser');
        await page.getByRole('button', { name: 'Open IDS', exact: true }).click();
        await chooser;
        expect(revit.paths.filter((p) => p.includes('/model-memory') || p.includes('/ids-files'))).toEqual([]);
    });
});
