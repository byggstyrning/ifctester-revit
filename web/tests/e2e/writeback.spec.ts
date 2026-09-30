import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { type Page, type Route, expect, test } from '@playwright/test';

const FIX = join(import.meta.dirname, '..', 'fixtures');
const read = (name: string) => readFileSync(join(FIX, name), 'utf8');

// The web app talks to the Revit add-in on this address when it is served from localhost.
const REVIT = 'http://localhost:48881';
const CORS = {
    'access-control-allow-origin': '*',
    'access-control-allow-methods': 'GET, POST, OPTIONS',
    'access-control-allow-headers': 'Content-Type',
};

// W2 is the wall that fails "wall name attribute" and "wall fire rating property" in the IFC4 fixture.
const W2 = '3M0lsbuhnETg9eDScRBzRN';
const CONFIGURATION = 'IFC4 Reference View';

type ResolveItem = { key: string; globalId: string; facet: string; propertySet?: string; name: string };

type MockRevit = {
    resolveBodies: { items: ResolveItem[] }[];
    applyBodies: unknown[];
    exportBodies: unknown[];
};

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

/** Serves the write-back contract of tasks/writeback.md in place of the Revit add-in. */
async function mockRevit(page: Page, options: { capabilities?: string[]; exportedIfc?: string; busy?: boolean }): Promise<MockRevit> {
    const seen: MockRevit = { resolveBodies: [], applyBodies: [], exportBodies: [] };
    const json = (route: Route, body: unknown, status = 200) =>
        route.fulfill({ status, headers: CORS, contentType: 'application/json', body: JSON.stringify(body) });

    await page.route(`${REVIT}/**`, async (route) => {
        const request = route.request();
        const path = new URL(request.url()).pathname;

        if (request.method() === 'OPTIONS') {
            return route.fulfill({ status: 200, headers: CORS });
        }
        if (path === '/status') {
            const status: Record<string, unknown> = { status: 'ok', connected: true, configsReady: true, version: '1.4.0' };
            if (options.capabilities) status.capabilities = options.capabilities;
            return json(route, status);
        }
        if (path === '/ifc-configurations') {
            return json(route, { configurations: [CONFIGURATION] });
        }
        if (path === '/resolve-parameters') {
            const body = request.postDataJSON() as { items: ResolveItem[] };
            seen.resolveBodies.push(body);
            return json(route, {
                items: body.items.map((item) => ({
                    key: item.key,
                    found: true,
                    elementId: 123456,
                    elementName: 'Basic Wall: Generic - 200mm',
                    category: 'Walls',
                    typeName: 'Generic - 200mm',
                    typeInstanceCount: 3,
                    candidates:
                        item.facet === 'property'
                            ? [
                                  { parameter: 'FireRating', scope: 'instance', storageType: 'string', value: '', hasValue: false, readOnly: false, source: 'name-match' },
                                  { parameter: 'Fire Rating', scope: 'type', storageType: 'string', value: 'EI30', hasValue: true, readOnly: false, source: 'pset-mapping-file' },
                              ]
                            : [{ parameter: `Ifc${item.name}`, scope: 'instance', storageType: 'string', value: item.name === 'Name' ? 'W2' : '', hasValue: item.name === 'Name', readOnly: false, source: 'ifc-override' }],
                    message: null,
                })),
            });
        }
        if (path === '/apply-changes') {
            const body = request.postDataJSON() as { changes: { key: string; parameter: string; value: string }[] };
            seen.applyBodies.push(body);
            if (options.busy) {
                return json(route, { error: 'Revit is busy: close the open dialog and try again' }, 503);
            }
            // The type parameter is written; the IfcName change is refused
            const results = body.changes.map((change) =>
                change.parameter === 'IfcName'
                    ? { key: change.key, ok: false, message: 'The element is owned by another user', newValue: null }
                    : { key: change.key, ok: true, message: 'Type parameter: 3 instances changed', newValue: change.value },
            );
            const applied = results.filter((result) => result.ok).length;
            return json(route, { applied, failed: results.length - applied, results });
        }
        if (path === '/export-ifc') {
            seen.exportBodies.push(request.postDataJSON());
            return json(route, { jobId: 'job-1', status: 'running' });
        }
        if (path === '/export-status/job-1') {
            return json(route, { jobId: 'job-1', status: 'complete' });
        }
        if (path === '/export-file/job-1') {
            return route.fulfill({
                status: 200,
                headers: { ...CORS, 'content-disposition': 'attachment; filename="fixed.ifc"' },
                contentType: 'application/octet-stream',
                body: options.exportedIfc ?? '',
            });
        }
        return json(route, { error: `Not mocked: ${path}` }, 404);
    });

    return seen;
}

/** Opens the app as the Revit add-in serves it, then loads the IFC4 fixture and audits it against its IDS. */
async function openAuditedFixture(page: Page) {
    await page.goto('/?source=revit');
    await page.evaluate(
        async ({ ifc, ids }) => {
            const wasm = (await import('/src/modules/wasm/index.ts')).default;
            const IDS = await import('/src/modules/api/ids.svelte.ts');
            const API = await import('/src/modules/api/api.svelte.ts');

            await wasm.init();
            const docId = 'e2e-writeback';
            IDS.Module.documents[docId] = (await wasm.openIDS(ids)) as never;
            IDS.setDocumentState(docId, { viewMode: 'viewer' });
            IDS.Module.activeDocument = docId;

            await API.loadIfc(new File([ifc], 'model.ifc'));
            await API.runAudit();
        },
        { ifc: read('model-ifc4.ifc'), ids: read('specs-ifc4.ids') },
    );
    await expect(page.getByRole('heading', { name: 'IFC4 regression' })).toBeVisible();
}

const specCard = (page: Page, name: string) =>
    page.locator('.specification-card').filter({ has: page.getByRole('heading', { name, exact: true }) });

/** Expands a specification and its only requirement, so the failed-elements table shows. */
async function expandRequirement(page: Page, name: string) {
    const card = specCard(page, name);
    await card.getByRole('heading', { name, exact: true }).click();
    await card.locator('button.facet-header').click();
    return card;
}

test.describe('write-back of IDS fixes to Revit', () => {
    test('a fix is previewed, applied and confirmed by a re-export', async ({ page }) => {
        // The model Revit exports after the fix: W2 now carries Pset_WallCommon.FireRating = EI60
        const fixedIfc = read('model-ifc4.ifc').replace("$,$,(#12),#16);", "$,$,(#12,#13),#16);");
        expect(fixedIfc).not.toEqual(read('model-ifc4.ifc'));

        const revit = await mockRevit(page, { capabilities: ['writeback'], exportedIfc: fixedIfc });
        await openAuditedFixture(page);

        // Out-of-scope facets say why instead of offering an input
        const material = await expandRequirement(page, 'wall material');
        await expect(material.locator('th', { hasText: 'Fix in Revit' })).toBeVisible();
        await expect(material.getByText('Materials cannot be written to Revit from here')).toBeVisible();
        await expect(material.locator('input')).toHaveCount(0);

        // A property requirement with a single allowed value prefills it
        const fireRating = await expandRequirement(page, 'wall fire rating property');
        const fireRatingFix = fireRating.getByLabel('Fix value for W2');
        await expect(fireRatingFix).toHaveValue('EI60');
        await expect(page.getByText('pending change')).toHaveCount(0);
        await fireRating.getByRole('button', { name: 'Add fix for W2' }).click();
        await expect(page.getByText('1 pending change', { exact: true })).toBeVisible();

        // An attribute requirement, with a typed value
        const wallName = await expandRequirement(page, 'wall name attribute');
        const wallNameFix = wallName.getByLabel('Fix value for W2');
        await wallNameFix.fill('');
        await wallNameFix.pressSequentially('W1');
        await wallNameFix.press('Enter');
        await expect(page.getByText('2 pending changes', { exact: true })).toBeVisible();

        // The export configuration selected in the toolbar is the one the re-export will use
        await page.locator('select.config-select').selectOption(CONFIGURATION);

        // Review: Revit resolves the targets, nothing is written yet
        await page.getByRole('button', { name: 'Review changes' }).click();
        const dialog = page.getByRole('dialog');
        await expect(dialog.getByRole('heading', { name: 'Pending changes (2)' })).toBeVisible();
        await expect(dialog.getByText('Basic Wall: Generic - 200mm')).toHaveCount(2);
        expect(revit.resolveBodies).toEqual([
            {
                items: [
                    { key: `${W2}|1:0`, globalId: W2, facet: 'property', propertySet: 'Pset_WallCommon', name: 'FireRating' },
                    { key: `${W2}|0:0`, globalId: W2, facet: 'attribute', name: 'Name' },
                ],
                configuration: CONFIGURATION,
            },
        ]);
        expect(revit.applyBodies).toEqual([]);

        const fireRatingRow = dialog.getByRole('row').filter({ hasText: 'Pset_WallCommon.FireRating' });
        const wallNameRow = dialog.getByRole('row').filter({ hasText: 'Attribute Name' });
        await expect(wallNameRow.getByText('IfcName')).toBeVisible();
        await expect(wallNameRow.getByText('found by IFC override parameter')).toBeVisible();
        await expect(wallNameRow.getByRole('cell', { name: 'W2', exact: true })).toBeVisible();
        await expect(wallNameRow.getByRole('cell', { name: 'W1', exact: true })).toBeVisible();

        // Two candidates: the best one is preselected, the user picks the type parameter instead
        const candidates = fireRatingRow.getByRole('combobox');
        await expect(candidates.getByRole('option')).toHaveText(['FireRating (instance)', 'Fire Rating (type)']);
        await expect(fireRatingRow.getByText('(empty)')).toBeVisible();
        await candidates.selectOption({ label: 'Fire Rating (type)' });
        await expect(fireRatingRow.getByText('changes all 3 instances of type Generic - 200mm')).toBeVisible();
        await expect(fireRatingRow.getByRole('cell', { name: 'EI30', exact: true })).toBeVisible();

        await dialog.getByRole('button', { name: 'Apply to Revit (2)' }).click();

        await expect(dialog.getByRole('heading', { name: 'Applied to Revit (1)' })).toBeVisible();
        expect(revit.applyBodies).toEqual([
            {
                changes: [
                    { key: `${W2}|1:0`, globalId: W2, parameter: 'Fire Rating', scope: 'type', value: 'EI60' },
                    { key: `${W2}|0:0`, globalId: W2, parameter: 'IfcName', scope: 'instance', value: 'W1' },
                ],
            },
        ]);

        // The applied change is reported with Revit's note; the refused one stays pending with its message
        await expect(dialog.getByText('Type parameter: 3 instances changed')).toBeVisible();
        await expect(dialog.getByRole('heading', { name: 'Pending changes (1)' })).toBeVisible();
        await expect(dialog.getByText('Revit could not apply this: The element is owned by another user')).toBeVisible();

        // Re-export with the toolbar's configuration and audit again
        await expect(specCard(page, 'wall fire rating property')).toHaveClass(/spec-fail/);
        await expect(dialog.getByRole('combobox', { name: 'IFC export configuration' })).toHaveValue(CONFIGURATION);
        await dialog.getByRole('button', { name: 'Re-export and re-audit' }).click();
        await expect(dialog.getByRole('button', { name: 'Exporting and auditing...' })).toBeVisible();

        await expect(dialog).toBeHidden({ timeout: 120_000 });
        expect(revit.exportBodies).toEqual([{ configuration: CONFIGURATION }]);
        await expect(specCard(page, 'wall fire rating property')).toHaveClass(/spec-pass/);
        // The new audit replaces the old one, which drops what was still pending
        await expect(page.getByText('pending change')).toHaveCount(0);
        expect(revit.applyBodies).toHaveLength(1);
    });

    test('one value is set for every failed element of a requirement', async ({ page }) => {
        // Both walls lack a Description, so the requirement has two failures
        const ids = read('specs-ifc4.ids').replace(
            '<name><simpleValue>Name</simpleValue></name><value><simpleValue>W1</simpleValue></value>',
            '<name><simpleValue>Description</simpleValue></name>',
        );
        expect(ids).not.toEqual(read('specs-ifc4.ids'));

        const revit = await mockRevit(page, { capabilities: ['writeback'] });
        await page.goto('/?source=revit');
        await page.evaluate(
            async ({ ifc, ids }) => {
                const wasm = (await import('/src/modules/wasm/index.ts')).default;
                const IDS = await import('/src/modules/api/ids.svelte.ts');
                const API = await import('/src/modules/api/api.svelte.ts');

                await wasm.init();
                IDS.Module.documents.bulk = (await wasm.openIDS(ids)) as never;
                IDS.setDocumentState('bulk', { viewMode: 'viewer' });
                IDS.Module.activeDocument = 'bulk';
                await API.loadIfc(new File([ifc], 'model.ifc'));
                await API.runAudit();
            },
            { ifc: read('model-ifc4.ifc'), ids },
        );

        const card = await expandRequirement(page, 'wall name attribute');
        await card.getByLabel('Set all 2 failed to').fill('Load-bearing wall');
        await card.getByRole('button', { name: 'Set all' }).click();
        await expect(page.getByText('2 pending changes', { exact: true })).toBeVisible();
        await expect(card.getByLabel('Fix value for W1')).toHaveValue('Load-bearing wall');
        await expect(card.getByLabel('Fix value for W2')).toHaveValue('Load-bearing wall');

        await page.getByRole('button', { name: 'Review changes' }).click();
        await page.getByRole('dialog').getByRole('button', { name: 'Apply to Revit (2)' }).click();
        await expect.poll(() => revit.applyBodies.length).toBe(1);
        expect(revit.applyBodies[0]).toEqual({
            changes: [
                { key: '18Si1AzJn4lg5Rv6P1rLRf|0:0', globalId: '18Si1AzJn4lg5Rv6P1rLRf', parameter: 'IfcDescription', scope: 'instance', value: 'Load-bearing wall' },
                { key: `${W2}|0:0`, globalId: W2, parameter: 'IfcDescription', scope: 'instance', value: 'Load-bearing wall' },
            ],
        });
    });

    test('a request Revit refuses as a whole leaves the changes pending', async ({ page }) => {
        const revit = await mockRevit(page, { capabilities: ['writeback'], busy: true });
        await openAuditedFixture(page);

        const fireRating = await expandRequirement(page, 'wall fire rating property');
        await fireRating.getByRole('button', { name: 'Add fix for W2' }).click();
        await page.getByRole('button', { name: 'Review changes' }).click();
        const dialog = page.getByRole('dialog');
        await dialog.getByRole('button', { name: 'Apply to Revit (1)' }).click();

        await expect(dialog.getByText('Revit is busy: close the open dialog and try again. Nothing was applied.')).toBeVisible();
        await expect(dialog.getByRole('heading', { name: 'Pending changes (1)' })).toBeVisible();
        await expect(dialog.getByRole('button', { name: 'Apply to Revit (1)' })).toBeEnabled();
        expect(revit.applyBodies).toHaveLength(1);
    });

    test('an add-in without the writeback capability gets no fix controls', async ({ page }) => {
        await mockRevit(page, {});
        await openAuditedFixture(page);

        const fireRating = await expandRequirement(page, 'wall fire rating property');
        await expect(fireRating.locator('th', { hasText: 'GlobalId' })).toHaveCount(2);
        await expect(fireRating.locator('th', { hasText: 'Fix in Revit' })).toHaveCount(0);
        await expect(fireRating.locator('input')).toHaveCount(0);
        await expect(page.getByRole('button', { name: 'Review changes' })).toHaveCount(0);
    });
});
