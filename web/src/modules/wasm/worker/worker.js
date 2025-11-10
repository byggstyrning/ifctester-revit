/**
 * WASM worker
*/

import { MessageType } from '../index';
import config from '../../../config.json';
import * as IDS from './ids.js';
import * as API from './api';

let pyodide = null;
let ready = false;

self.addEventListener('message', async (event) => {
    console.log("[worker] Received message:", event.data);
    const { type, payload, id } = event.data;

    try {
        switch (type) {
            case MessageType.INIT:
                await initEnvironment();
                self.postMessage({
                    type: MessageType.READY,
                    payload: { success: true },
                    id
                });
                break;

            case MessageType.API_CALL:
                if (!ready) {
                    throw new Error('[worker] Pyodide not initialized');
                }
                const result = await handleApiCall(payload);
                self.postMessage({
                    type: MessageType.API_RESPONSE,
                    payload: result,
                    id
                });
                break;

            default:
                throw new Error(`[worker] Unknown message type: ${type}`);
        }
    } catch (error) {
        self.postMessage({
            type: MessageType.ERROR,
            payload: {
                message: error.message,
                stack: error.stack
            },
            id
        });
    }
});

async function initEnvironment() {
    if (ready) return;

    // Get the origin (works with both localhost and app.localhost)
    const origin = self.location.origin;
    
    // Helper function to resolve URLs - use absolute URLs for wheel files
    const resolveUrl = (path) => {
        // If path is already absolute (starts with http:// or https://), use as-is
        if (path.startsWith('http://') || path.startsWith('https://')) {
            return path;
        }
        // Otherwise, make it absolute using the current origin
        return `${origin}${path.startsWith('/') ? path : '/' + path}`;
    };

    // Load Pyodide
    const scriptUrl = new URL('/pyodide/pyodide.mjs', import.meta.url);
    const { loadPyodide } = await import(scriptUrl.href);
    pyodide = await loadPyodide({
        convertNullToNone: true
    });

    // Load required packages
    await pyodide.loadPackage('micropip');
    await pyodide.loadPackage('numpy');

    const micropip = pyodide.pyimport('micropip');

    // Install IfcOpenShell wheel (local) - use absolute URL
    try {
        const ifcopenshellUrl = resolveUrl(config.wasm.wheel_url);
        console.log(`[worker] Installing IfcOpenShell from: ${ifcopenshellUrl}`);
        
        // Check if file exists first by trying a HEAD request
        try {
            const headResponse = await fetch(ifcopenshellUrl, { method: 'HEAD', mode: 'cors' });
            if (!headResponse.ok) {
                throw new Error(`File not found (${headResponse.status})`);
            }
            console.log(`[worker] IfcOpenShell file exists, proceeding with install...`);
        } catch (headError) {
            console.warn(`[worker] HEAD request failed, trying install anyway: ${headError.message}`);
        }
        
        await micropip.install(ifcopenshellUrl);
        console.log("[worker] IfcOpenShell installed successfully");
    } catch (error) {
        console.error(`[worker] Failed to install IfcOpenShell from local file: ${error.message}`);
        console.error(`[worker] Error details:`, error);
        // Try to install from PyPI as fallback (may not work for emscripten builds)
        try {
            console.log("[worker] Attempting to install ifcopenshell from PyPI...");
            await micropip.install('ifcopenshell');
            console.log("[worker] IfcOpenShell installed from PyPI");
        } catch (pypiError) {
            console.error(`[worker] Failed to install IfcOpenShell from PyPI: ${pypiError.message}`);
            throw new Error(`Failed to install IfcOpenShell. Please ensure the wheel file is available at ${config.wasm.wheel_url} or internet access is available. Error: ${error.message}`);
        }
    }

    // Install IfcTester dependencies (local) - use absolute URL
    try {
        const odfpyUrl = resolveUrl(config.wasm.odfpy_url);
        console.log(`[worker] Installing odfpy from: ${odfpyUrl}`);
        await micropip.install(odfpyUrl);
        console.log("[worker] odfpy installed successfully");
    } catch (error) {
        console.error(`[worker] Failed to install odfpy from local file: ${error.message}`);
        // Try to install from PyPI as fallback
        try {
            console.log("[worker] Attempting to install odfpy from PyPI...");
            await micropip.install('odfpy');
            console.log("[worker] odfpy installed from PyPI");
        } catch (pypiError) {
            console.error(`[worker] Failed to install odfpy from PyPI: ${pypiError.message}`);
            throw new Error(`Failed to install odfpy. Please ensure the wheel file is available at ${config.wasm.odfpy_url} or internet access is available.`);
        }
    }
    
    await pyodide.loadPackage("shapely");

    // Install IfcTester - try local first, fallback to PyPI
    try {
        if (config.wasm.ifctester_url) {
            // Install from local wheel file - use absolute URL
            const ifctesterUrl = resolveUrl(config.wasm.ifctester_url);
            console.log(`[worker] Installing ifctester from local file: ${ifctesterUrl}`);
            await micropip.install(ifctesterUrl);
            console.log("[worker] Installed ifctester from local file");
        } else {
            // Fallback: try to install from PyPI (may fail in local deployment)
            console.log("[worker] Installing ifctester from PyPI...");
            await micropip.install('ifctester');
            console.log("[worker] Installed ifctester from PyPI");
        }
    } catch (error) {
        console.error("[worker] Failed to install ifctester:", error.message);
        // Try alternative: install from PyPI with explicit URL (latest version)
        try {
            const pypiUrl = 'https://files.pythonhosted.org/packages/8c/98/98afa5fa347361b8d0f421b1c5059ef960a455f89b8235e6ceed33c0e796/ifctester-0.8.3-py3-none-any.whl';
            console.log(`[worker] Trying PyPI fallback: ${pypiUrl}`);
            await micropip.install(pypiUrl);
            console.log("[worker] Installed ifctester from PyPI URL");
        } catch (fallbackError) {
            console.error("[worker] Failed to install ifctester from PyPI:", fallbackError.message);
            const localUrl = config.wasm.ifctester_url ? resolveUrl(config.wasm.ifctester_url) : 'not configured';
            throw new Error(`Failed to install ifctester: ${error.message}. Make sure ifctester wheel is available locally at ${localUrl} or internet access is available.`);
        }
    }

    // Initialize IDS and API
    await API.init(pyodide);
    await IDS.init(pyodide);

    console.log("[worker] Environment initialized");

    ready = true;
}

async function cleanupEnvironment() {
    ready = false;
    pyodide = null;
    console.log("[worker] Closed environment");
}

async function handleApiCall({ method, args = [] }) {
    if (method === 'internal.cleanup') {
        await cleanupEnvironment();
        return true;
    }

    if (method in API.API) {
        return await API.API[method](...args);
    } else if (method in IDS.API) {
        return await IDS.API[method](...args);
    } else {
        throw new Error(`[worker] Unknown API method: ${method}`);
    }
}