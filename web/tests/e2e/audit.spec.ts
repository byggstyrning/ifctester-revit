import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test } from '@playwright/test';

const FIX = join(import.meta.dirname, '..', 'fixtures');
const read = (name: string) => readFileSync(join(FIX, name), 'utf8');

// Sandbox-only escape hatch: route package downloads through Node when the browser cannot
// verify the network proxy's CA. Not needed on GitHub runners.
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

type Summary = {
    name: string;
    status: boolean;
    is_skipped: boolean;
    applicable: number;
    applicable_fail: number;
    requirements: [string, boolean, number][];
};

async function runAudit(page: import('@playwright/test').Page, ifc: string, ids: string) {
    return page.evaluate(
        async ({ ifc, ids }) => {
            const wasm = (await import('/src/modules/wasm/index.ts')).default;
            const IDS = await import('/src/modules/api/ids.svelte.ts');
            const API = await import('/src/modules/api/api.svelte.ts');

            await API.clearAllModels();
            const doc = await wasm.openIDS(ids);
            const docId = `e2e-${Math.random().toString(36).slice(2)}`;
            IDS.Module.documents[docId] = doc as never;
            IDS.Module.states[docId] = {} as never;
            IDS.Module.activeDocument = docId;

            await API.loadIfc(new File([ifc], 'model.ifc'));
            await API.runAudit();
            const report = API.IFCModels.audits[0];
            const data = JSON.parse(JSON.stringify(report.data));
            type RawReq = { facet_type: string; status: boolean; total_fail: number };
            type RawSpec = {
                name: string;
                status: boolean;
                is_skipped?: boolean;
                total_applicable: number;
                total_applicable_fail: number;
                requirements: RawReq[];
            };
            const summary = (data.specifications as RawSpec[]).map((s) => ({
                name: s.name,
                status: s.status,
                is_skipped: s.is_skipped ?? false,
                applicable: s.total_applicable,
                applicable_fail: s.total_applicable_fail,
                requirements: s.requirements.map((r) => [r.facet_type, r.status, r.total_fail]),
            }));
            return { summary, htmlReport: report.htmlReport ?? '' };
        },
        { ifc, ids },
    ) as Promise<{ summary: Summary[]; htmlReport: string }>;
}

test.describe('in-browser IDS audit (Pyodide + ifctester)', () => {
    test.beforeEach(async ({ page }) => {
        const errors: string[] = [];
        page.on('pageerror', (e) => errors.push(String(e)));
        await page.goto('/');
        await page.evaluate(async () => {
            const wasm = (await import('/src/modules/wasm/index.ts')).default;
            await wasm.init();
        });
        expect(errors, 'no uncaught page errors while initialising the worker').toEqual([]);
    });

    for (const schema of ['ifc4', 'ifc2x3']) {
        test(`${schema}: audit results match the native ifctester reference`, async ({ page }) => {
            const expected = JSON.parse(read(`expected-${schema}.json`)) as Summary[];
            const { summary, htmlReport } = await runAudit(page, read(`model-${schema}.ifc`), read(`specs-${schema}.ids`));
            expect(summary).toEqual(expected);
            for (const spec of expected) {
                expect(htmlReport, `HTML report mentions "${spec.name}"`).toContain(spec.name);
            }
        });
    }

    test('invalid IDS is rejected with a readable error', async ({ page }) => {
        const message = await page.evaluate(async () => {
            const wasm = (await import('/src/modules/wasm/index.ts')).default;
            try {
                await wasm.openIDS('<ids xmlns="http://standards.buildingsmart.org/IDS"><oops/></ids>', true);
                return null;
            } catch (e) {
                return String(e);
            }
        });
        expect(message).not.toBeNull();
    });

    test('generated IDS XML round-trips through validation', async ({ page }) => {
        const result = await page.evaluate(async (ids) => {
            const wasm = (await import('/src/modules/wasm/index.ts')).default;
            const doc = await wasm.openIDS(ids);
            return (await wasm.validateIDS(doc)) as { valid: boolean };
        }, read('specs-ifc4.ids'));
        expect(result.valid).toBe(true);
    });
});
