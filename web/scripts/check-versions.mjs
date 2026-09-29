// Fails when the pinned Pyodide/wheel versions drift apart between config.json, the worker,
// the download scripts and the Windows build script. Run: node scripts/check-versions.mjs
import { existsSync, readFileSync } from 'node:fs';
import { basename, join } from 'node:path';

const root = join(import.meta.dirname, '..');
const repo = join(root, '..');
const read = (p) => readFileSync(p, 'utf8');
const errors = [];
const check = (ok, message) => { if (!ok) errors.push(message); };

const { wasm } = JSON.parse(read(join(root, 'src/config.json')));
const ifcopenshellWheel = basename(wasm.wheel_url);
const ifctesterWheel = basename(wasm.ifctester_url);
const ifctesterVersion = /^ifctester-(\d+\.\d+\.\d+)-/.exec(ifctesterWheel)?.[1];
check(ifctesterVersion, `cannot parse ifctester version from ${ifctesterWheel}`);

const files = {
    'src/modules/wasm/worker/worker.ts': read(join(root, 'src/modules/wasm/worker/worker.ts')),
    'scripts/download-packages.sh': read(join(root, 'scripts/download-packages.sh')),
    'scripts/download-packages.ps1': read(join(root, 'scripts/download-packages.ps1')),
    'scripts/web/build.ps1': read(join(repo, 'scripts/web/build.ps1')),
};

// ifcopenshell wasm wheel: same filename everywhere it is referenced
for (const [name, text] of Object.entries(files)) {
    check(text.includes(ifcopenshellWheel), `${name} does not reference ${ifcopenshellWheel} (config.json wheel_url)`);
}
// ifctester: pinned version in the download scripts and the worker's PyPI fallback URL
check(files['scripts/download-packages.sh'].includes(`IFCTESTER_VERSION="${ifctesterVersion}"`), `download-packages.sh must pin ifctester ${ifctesterVersion}`);
check(files['scripts/download-packages.ps1'].includes(`"${ifctesterVersion}"`), `download-packages.ps1 must pin ifctester ${ifctesterVersion}`);
check(files['src/modules/wasm/worker/worker.ts'].includes(`ifctester-${ifctesterVersion}-py3-none-any.whl`), `worker.ts PyPI fallback URL must use ifctester ${ifctesterVersion}`);
// native reference tests pin the same ifctester version
const reqs = read(join(root, 'tests/python/requirements.txt'));
check(new RegExp(`^ifctester==${ifctesterVersion.replaceAll('.', '\\.')}$`, 'm').test(reqs), `tests/python/requirements.txt must pin ifctester==${ifctesterVersion}`);
// odfpy wheel
check(files['scripts/download-packages.sh'].includes(basename(wasm.odfpy_url)), 'download-packages.sh does not fetch the odfpy wheel from config.json');

// Pyodide: CDN version in config.json matches the committed runtime, and sqlite3 (needed by ifctester >= 0.9) is present
const pyodideVersion = /pyodide\/v([\d.]+)\//.exec(wasm.pyodide_url)?.[1];
const lock = JSON.parse(read(join(root, 'public/pyodide/pyodide-lock.json')));
check(lock.info?.version?.startsWith(pyodideVersion ?? '?'), `pyodide_url uses ${pyodideVersion} but public/pyodide is ${lock.info?.version}`);
const sqlite = lock.packages?.sqlite3?.file_name;
check(sqlite && existsSync(join(root, 'public/pyodide', sqlite)), 'public/pyodide is missing the sqlite3 wheel (ifctester >= 0.9 imports sqlite3)');
check(files['src/modules/wasm/worker/worker.ts'].includes('loadPackage("sqlite3")'), 'worker.ts must load the sqlite3 package');
// wheel ABI tag must match the Pyodide runtime (e.g. pyodide_2025_0 <-> Pyodide 0.28)
check(ifcopenshellWheel.includes(`pyodide_${lock.info?.abi_version}_wasm32`), `ifcopenshell wheel ${ifcopenshellWheel} does not match Pyodide ABI ${lock.info?.abi_version}`);

if (errors.length) {
    console.error(`Version consistency check failed:\n - ${errors.join('\n - ')}`);
    process.exit(1);
}
console.log(`OK: ifctester ${ifctesterVersion}, ${ifcopenshellWheel}, Pyodide ${pyodideVersion}`);
