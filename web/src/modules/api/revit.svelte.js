import { IFCModels, loadIfc, auditIfc, clearAllModels, runAudit as runBrowserAudit } from './api.svelte.js';
import * as IDS from './ids.svelte.js';
import { error, success, idsValidationError } from '../utils/toast.svelte.js';
import hyperid from 'hyperid';

/**
 * @typedef {Object} RevitState
 * @property {boolean} enabled
 * @property {string | null} apiUrl
 * @property {boolean} connected
 * @property {boolean} auditing
 * @property {boolean} loading
 * @property {string[]} capabilities - Optional features the connected add-in reports in /status (e.g. "writeback")
 * @property {string} exportConfiguration - IFC export configuration last selected in the toolbar
 * @property {ExportOverrides} exportOverrides - Files chosen on the page that replace the setup's own for the next export
 * @property {LastExport | null} lastExport - What the last export from this page read, as the add-in reported it
 * @property {boolean} exporting
 */

/**
 * @typedef {Object} ExportOverrides
 * @property {string} psetFile - User-defined property set file; empty uses the setup's own
 * @property {string} parameterMappingFile - Parameter mapping table; empty uses the setup's own
 */

/**
 * The files an export reads: GET /export-status and GET /ifc-configuration-files.
 * @typedef {Object} ExportFiles
 * @property {string | null} psetFile
 * @property {boolean} psetFileExists
 * @property {boolean} psetFileIsOverride
 * @property {string | null} parameterMappingFile
 * @property {boolean} parameterMappingFileExists
 * @property {boolean} parameterMappingFileIsOverride
 * @property {string | null} warning
 * @property {string | null} [ifcVersion] - The setup's IFC version (IFC2x3CV2, IFC4, ...); only GET /ifc-configuration-files gives it
 */

/**
 * @typedef {Object} LastExport
 * @property {string} configuration
 * @property {string | null} fileName - The exported IFC as loaded on the page
 * @property {string} psetFile - The property set file override sent with the export, or ''
 * @property {string} parameterMappingFile - The mapping table override sent with the export, or ''
 * @property {ExportFiles | null} files - What the add-in reports it read; null from add-ins without "export-overrides"
 * @property {string | null} warning
 */

/** The add-in can take property set file overrides with an export (GET /status capabilities). */
export const EXPORT_OVERRIDES = 'export-overrides';

/** The add-in can scan the model's parameters, suggest pset mappings and save pset files (GET /status capabilities). */
export const PSET_BUILDER = 'pset-builder';

// Revit connection state
/** @type {RevitState} */
export const Revit = $state({
    enabled: false,
    apiUrl: null,
    connected: false,
    auditing: false,
    loading: false,
    capabilities: [],
    exportConfiguration: '',
    exportOverrides: { psetFile: '', parameterMappingFile: '' },
    lastExport: null,
    exporting: false
});

/**
 * The base name of a Windows or UNC path.
 * @param {string | null | undefined} path
 */
export const fileNameOf = (path) => (path ? String(path).split(/[\\/]/).pop() || String(path) : '');

/**
 * "Pset override: <file>" when the loaded model came from an export whose property set file or
 * mapping table was replaced on the page, so nobody mistakes it for the delivery export.
 * Empty when no loaded model is such an export.
 * @param {{ fileName: string }[]} models - The loaded models
 * @returns {string}
 */
export const overrideLabel = (models) => {
    const last = Revit.lastExport;
    if (!last?.fileName || !models.some((model) => model.fileName === last.fileName)) return '';
    return [
        last.psetFile ? `Pset override: ${fileNameOf(last.psetFile)}` : '',
        last.parameterMappingFile ? `Mapping table override: ${fileNameOf(last.parameterMappingFile)}` : ''
    ].filter(Boolean).join(' · ');
};

const id = hyperid();
const pendingAudits = new Map();

// Check for Revit API URL in URL parameters
const urlParams = new URLSearchParams(window.location.search);
const apiUrl = urlParams.get('api');

if (apiUrl) {
    Revit.enabled = true;
    Revit.apiUrl = apiUrl;
} else {
    // Default to 0.0.0.0:48880 (SelectServer) if no API URL provided
    // Note: When accessing from remote (like validate.byggstyrning.se), 
    // browsers can't connect to localhost or 0.0.0.0 - need actual IP address
    // So we detect if we're on localhost vs remote and adjust accordingly
    const isLocalhost = window.location.hostname === 'localhost' || 
                       window.location.hostname === '127.0.0.1' ||
                       window.location.hostname === '0.0.0.0';
    
    Revit.enabled = true;
    if (isLocalhost) {
        Revit.apiUrl = 'http://localhost:48881';
    } else {
        // For remote access, user must provide IP via ?api= parameter
        // Default won't work because browser can't know the Revit machine's IP
        Revit.enabled = false; // Disable until user provides API URL
        console.warn('Revit integration requires ?api= parameter when accessing from remote host');
    }
}

/**
 * Test connection to Revit SelectServer API
 */
export const connect = async () => {
    if (!Revit.apiUrl) {
        error('No Revit API URL provided');
        return false;
    }
    
    try {
        Revit.loading = true;
        
        // Use SelectServer status endpoint (port 48880)
        // Status endpoint: http://<host>:48880/status
        const statusUrl = `${Revit.apiUrl}/status`;
        
        // Add timeout to prevent hanging requests that could cause server issues
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 5000); // 5 second timeout
        
        try {
            const response = await fetch(statusUrl, {
                method: 'GET',
                mode: 'cors',
                headers: {
                    'Content-Type': 'application/json'
                },
                signal: controller.signal
            });
            
            clearTimeout(timeoutId);
            
            if (response.ok) {
                // Add-ins older than the write-back release report no capabilities
                const status = await response.json().catch(() => null);
                Revit.capabilities = Array.isArray(status?.capabilities) ? status.capabilities : [];
                Revit.connected = true;
                success('Connected to Revit');
                return true;
            } else {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }
        } catch (fetchErr) {
            clearTimeout(timeoutId);
            if (fetchErr.name === 'AbortError') {
                throw new Error('Connection timeout - server may be down or unresponsive');
            }
            throw fetchErr;
        }
    } catch (err) {
        Revit.connected = false;
        
        // Provide helpful error message for mixed content issues
        if (err.message.includes('Mixed Content') || err.message.includes('blocked')) {
            error('Mixed content blocked. Use Cloudflare Tunnel proxy or access via HTTP.');
            console.error('To fix: Configure Cloudflare Tunnel to proxy /revit-api/* to your local Revit API');
        } else if (err.message.includes('timeout')) {
            error('Connection timeout. Revit API server may be down. Please reload pyRevit in Revit.');
        } else {
            error(`Failed to connect to Revit: ${err.message}`);
        }
        return false;
    } finally {
        Revit.loading = false;
    }
};

/**
 * Disconnect from Revit
 */
export const disconnect = () => {
    Revit.connected = false;
    Revit.capabilities = [];
    success('Disconnected from Revit');
};

/**
 * Run audit using current IDS document against Revit's IFC model
 * Validation happens in the browser using WebAssembly/Pyodide
 * Revit only provides the IFC file and element selection
 * @returns {Promise<string|null>} Returns audit ID when completed, null if failed
 */
export const runAudit = async () => {
    if (!Revit.connected || !IDS.Module.activeDocument) {
        return null;
    }
    
    try {
        Revit.auditing = true;
        
        // Check if we have loaded IFC models
        if (IFCModels.models.length === 0) {
            throw new Error('No IFC model loaded. Please load an IFC file first.');
        }
        
        // Use the existing browser-based validation (from api.svelte.js)
        // This validates in the browser using WebAssembly/Pyodide
        const auditReport = await runBrowserAudit();
        
        Revit.auditing = false;
        success('Audit completed (Revit)');
        
        return auditReport?.id || null;
        
    } catch (err) {
        Revit.auditing = false;
        error(`Failed to run Revit audit: ${err.message}`);
        return null;
    }
};

/**
 * Select element in Revit by IfcGUID/GlobalId using switchback API
 * Uses Image() workaround to bypass CORS restrictions for simple GET requests
 * @param {string} globalId - IFC GlobalId (from entity.global_id)
 * @returns {Promise<boolean>} Returns true if request was sent (assumes success)
 */
export const selectElement = async (globalId) => {
    if (!Revit.apiUrl || !Revit.connected) {
        error('Not connected to Revit');
        return false;
    }
    
    // Validate GUID format (IFC GUIDs are typically 22 characters, but can vary)
    if (!globalId || typeof globalId !== 'string' || globalId.trim() === '' || globalId === '-') {
        error(`Invalid GlobalId: ${globalId}`);
        return false;
    }
    
    // Use SelectServer API endpoint for GUID selection
    // SelectServer API: http://<host>:48881/select-by-guid/<guid>
    const encodedGuid = encodeURIComponent(globalId.trim());
    const selectUrl = `${Revit.apiUrl}/select-by-guid/${encodedGuid}`;
    
    // Use Image() to make the API request - this bypasses CORS
    // for simple GET requests while still triggering the server action
    // Note: We won't get the JSON response, but the action will work in Revit
    return new Promise((resolve) => {
        try {
            const img = new Image();
            let requestCompleted = false;
            let timeoutId = null;
            
            // Set up success handling (rarely triggers due to CORS, but included for completeness)
            img.onload = () => {
                if (!requestCompleted) {
                    requestCompleted = true;
                    if (timeoutId) clearTimeout(timeoutId);
                    console.log('SelectServer successful for GUID:', globalId);
                    success(`Element with GUID ${globalId} selected in Revit`);
                    resolve(true);
                }
            };
            
            // Set up error handling - this will usually trigger due to CORS,
            // even though the request successfully reaches Revit
            // However, if server is down, we'll also get an error here
            img.onerror = (event) => {
                if (!requestCompleted) {
                    requestCompleted = true;
                    if (timeoutId) clearTimeout(timeoutId);
                    
                    // Check if this is a connection refused error by checking the error type
                    // Connection refused errors typically happen immediately
                    // CORS errors also happen immediately, so we can't distinguish perfectly
                    // But we can check if Revit.connected is still true
                    if (!Revit.connected) {
                        error('SelectServer is not responding. Please reload pyRevit in Revit (Extensions → Reload pyRevit).');
                        resolve(false);
                        return;
                    }
                    
                    console.log('SelectServer request sent for GUID:', globalId, 
                        '(CORS error expected, but action likely worked in Revit)');
                    
                    // Assume success because the request typically reaches Revit
                    // despite the browser showing a CORS error
                    success(`Element with GUID ${globalId} selected in Revit`);
                    resolve(true);
                }
            };
            
            // Set a timeout to detect if server is truly down
            // Longer timeout for GUID search as it may take longer
            timeoutId = setTimeout(() => {
                if (!requestCompleted) {
                    requestCompleted = true;
                    error('Request timed out. SelectServer may be down. Please reload pyRevit in Revit (Extensions → Reload pyRevit).');
                    resolve(false);
                }
            }, 10000); // 10 second timeout for GUID search
            
            // Send the request - this triggers the SelectServer endpoint
            // The browser will attempt to load this as an image, which bypasses CORS
            img.src = selectUrl;
            
        } catch (err) {
            console.error('Error setting up switchback request:', err);
            error(`Failed to select element: ${err.message}. Please reload pyRevit in Revit.`);
            resolve(false);
        }
    });
};

/**
 * Get available IFC export configurations from Revit
 * @returns {Promise<string[]>} Returns array of configuration names
 */
export const getIfcConfigurations = async () => {
    if (!Revit.apiUrl || !Revit.connected) {
        error('Not connected to Revit');
        return [];
    }
    
    try {
        const configUrl = `${Revit.apiUrl}/ifc-configurations`;
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 10000);
        
        const response = await fetch(configUrl, {
            method: 'GET',
            mode: 'cors',
            headers: {
                'Content-Type': 'application/json'
            },
            signal: controller.signal
        });
        
        clearTimeout(timeoutId);
        
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }
        
        const data = await response.json();
        return data.configurations || [];
    } catch (err) {
        console.error('Failed to get IFC configurations:', err);
        error(`Failed to get IFC configurations: ${err.message}`);
        return [];
    }
};

/**
 * GET with a timeout; throws the add-in's error message on a non-2xx answer.
 * @param {string} path
 * @param {number} timeout
 */
const getJson = async (path, timeout) => {
    if (!Revit.apiUrl || !Revit.connected) {
        throw new Error('Not connected to Revit');
    }
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), timeout);
    try {
        const response = await fetch(`${Revit.apiUrl}${path}`, { method: 'GET', mode: 'cors', signal: controller.signal });
        const data = await response.json().catch(() => null);
        if (!response.ok) {
            throw new Error(data?.error || `HTTP ${response.status}: ${response.statusText}`);
        }
        return data;
    } catch (err) {
        if (err instanceof Error && err.name === 'AbortError') {
            throw new Error('Revit did not answer in time');
        }
        throw err;
    } finally {
        clearTimeout(timeoutId);
    }
};

/**
 * A JSON request with a timeout. A non-2xx answer throws an Error carrying the add-in's message,
 * the HTTP status as `status` and the parsed body as `data`.
 * @param {string} method
 * @param {string} path
 * @param {unknown} body - Sent as JSON; undefined sends none
 * @param {number} timeout
 */
const requestJson = async (method, path, body, timeout) => {
    if (!Revit.apiUrl || !Revit.connected) {
        throw new Error('Not connected to Revit');
    }
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), timeout);
    try {
        const response = await fetch(`${Revit.apiUrl}${path}`, {
            method,
            mode: 'cors',
            signal: controller.signal,
            ...(body === undefined ? {} : { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
        });
        const data = await response.json().catch(() => null);
        if (!response.ok) {
            const failure = new Error(data?.error || `HTTP ${response.status}: ${response.statusText}`);
            Object.assign(failure, { status: response.status, data });
            throw failure;
        }
        return data;
    } catch (err) {
        if (err instanceof Error && err.name === 'AbortError') {
            throw new Error('Revit did not answer in time');
        }
        throw err;
    } finally {
        clearTimeout(timeoutId);
    }
};

/**
 * Every parameter on the model elements of the active document and on their types (read-only scan).
 * @returns {Promise<{ document: string | null, elementCount: number, typeCount: number, elapsedMs: number, parameters: import('../psetBuilder/psetFile').ModelParameter[], message: string | null }>}
 */
export const getModelParameters = () => requestJson('GET', '/model-parameters', undefined, 180000);

/**
 * Revit parameters the exporter would read each IFC property from, by name, best first.
 * @param {{ key: string, propertySet: string, name: string, entities: string[] }[]} items
 * @returns {Promise<{ elementCount: number, scopeMethod: string, elapsedMs: number, items: import('../psetBuilder/psetFile').SuggestionResult[], message: string | null }>}
 */
export const getPsetSuggestions = (items) => requestJson('POST', '/pset-suggestions', { items }, 180000);

/**
 * Writes a pset file on the Revit machine. Throws with `status` 409 when it exists and overwrite is false.
 * @param {{ path?: string, name?: string, content: string, overwrite?: boolean }} request
 * @returns {Promise<{ path: string, overwritten: boolean, bytes: number }>}
 */
export const savePsetFile = (request) => requestJson('POST', '/pset-files/save', request, 30000);

/**
 * The property set file and mapping table a setup exports with, so the page can show its default.
 * @param {string} configurationName
 * @returns {Promise<ExportFiles>}
 */
export const getIfcConfigurationFiles = (configurationName) =>
    getJson(`/ifc-configuration-files?name=${encodeURIComponent(configurationName)}`, 35000);

/**
 * The *.txt files directly in a folder, as the add-in sees it.
 * @param {string} dir
 * @returns {Promise<{ name: string, path: string, modified: string }[]>}
 */
export const listPsetFiles = async (dir) => {
    const data = await getJson(`/pset-files?dir=${encodeURIComponent(dir)}`, 25000);
    return Array.isArray(data?.files) ? data.files : [];
};

/**
 * Export active view as IFC from Revit using async polling pattern
 * @param {string} configurationName - Name of the IFC export configuration to use
 * @param {Partial<ExportOverrides>} [overrides] - Files that replace the setup's own; empty ones are not sent
 * @returns {Promise<File|null>} Returns the exported IFC file, or null if failed
 */
export const exportIfc = async (configurationName, overrides = {}) => {
    if (!Revit.apiUrl || !Revit.connected) {
        error('Not connected to Revit');
        return null;
    }
    
    if (!configurationName || typeof configurationName !== 'string') {
        error('Invalid IFC configuration name');
        return null;
    }
    
    const psetFile = overrides.psetFile?.trim() ?? '';
    const parameterMappingFile = overrides.parameterMappingFile?.trim() ?? '';
    /** @type {LastExport} */
    const record = { configuration: configurationName, fileName: null, psetFile, parameterMappingFile, files: null, warning: null };
    Revit.lastExport = null;

    try {
        // Step 1: Start the export and get job ID
        const startUrl = `${Revit.apiUrl}/export-ifc`;
        
        const startResponse = await fetch(startUrl, {
            method: 'POST',
            mode: 'cors',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                configuration: configurationName,
                ...(psetFile ? { psetFile } : {}),
                ...(parameterMappingFile ? { parameterMappingFile } : {})
            })
        });
        
        if (!startResponse.ok) {
            const errorData = await startResponse.json().catch(() => ({ error: 'Unknown error' }));
            throw new Error(errorData.error || `HTTP ${startResponse.status}: ${startResponse.statusText}`);
        }
        
        const startData = await startResponse.json();
        const jobId = startData.jobId;
        
        if (!jobId) {
            throw new Error('No job ID returned from export request');
        }
        
        console.log(`Revit export started, job ID: ${jobId}`);
        
        // Step 2: Poll for completion
        const pollInterval = 1500; // 1.5 seconds
        const maxPolls = 400; // ~10 minutes total
        let pollCount = 0;
        
        while (pollCount < maxPolls) {
            await new Promise(resolve => setTimeout(resolve, pollInterval));
            pollCount++;
            
            const statusUrl = `${Revit.apiUrl}/export-status/${jobId}`;
            const statusResponse = await fetch(statusUrl, {
                method: 'GET',
                mode: 'cors'
            });
            
            if (!statusResponse.ok) {
                console.warn(`Status poll ${pollCount} failed: ${statusResponse.status}`);
                continue;
            }
            
            const statusData = await statusResponse.json();
            // Add-ins with "export-overrides" report which property set file the export read
            if (statusData.exportFiles) record.files = statusData.exportFiles;
            if (typeof statusData.warning === 'string' && statusData.warning) record.warning = statusData.warning;
            
            if (statusData.status === 'complete') {
                console.log(`Revit export completed after ${pollCount} polls`);
                break;
            } else if (statusData.status === 'failed') {
                Revit.lastExport = { ...record, warning: statusData.error || record.warning };
                throw new Error(statusData.error || 'Export failed');
            }
            // status === 'running', continue polling
        }
        
        if (pollCount >= maxPolls) {
            throw new Error('Export timed out waiting for completion');
        }
        
        // Step 3: Download the file
        const fileUrl = `${Revit.apiUrl}/export-file/${jobId}`;
        const fileResponse = await fetch(fileUrl, {
            method: 'GET',
            mode: 'cors'
        });
        
        if (!fileResponse.ok) {
            const errorData = await fileResponse.json().catch(() => ({ error: 'Unknown error' }));
            throw new Error(errorData.error || `Failed to download file: HTTP ${fileResponse.status}`);
        }
        
        // Get the file blob
        const blob = await fileResponse.blob();
        
        // Extract filename from Content-Disposition header if available
        const contentDisposition = fileResponse.headers.get('Content-Disposition');
        let fileName = `Export_${new Date().toISOString().replace(/[:.]/g, '-')}.ifc`;
        if (contentDisposition) {
            const fileNameMatch = contentDisposition.match(/filename="?([^"]+)"?/);
            if (fileNameMatch) {
                fileName = fileNameMatch[1];
            }
        }
        
        // Create a File object from the blob
        const file = new File([blob], fileName, { type: 'application/octet-stream' });
        record.fileName = fileName;
        Revit.lastExport = record;
        
        success(`IFC exported successfully: ${fileName}`);
        return file;
    } catch (err) {
        console.error('Failed to export IFC:', err);
        error(`Failed to export IFC: ${err.message}`);
        return null;
    }
};

/**
 * Export the active view from Revit, load the result and audit it against the active IDS document.
 * Replaces the loaded models and their audit reports.
 * @param {string} configurationName - Name of the IFC export configuration to use
 * @returns {Promise<boolean>} Returns true when the export was loaded
 */
export const exportAndAudit = async (configurationName) => {
    if (!configurationName) {
        error('Please select an IFC export configuration');
        return false;
    }

    if (Revit.exporting) {
        return false;
    }

    try {
        Revit.exporting = true;
        Revit.exportConfiguration = configurationName;

        // Clear all existing models before loading the new export
        await clearAllModels();

        // Overrides only go to an add-in that can apply them; an older one would ignore them silently
        const overrides = Revit.capabilities.includes(EXPORT_OVERRIDES) ? Revit.exportOverrides : {};
        const exportedFile = await exportIfc(configurationName, overrides);
        if (!exportedFile) {
            return false;
        }

        // Automatically load the exported IFC file
        await loadIfc(exportedFile);
        success('IFC exported and loaded successfully');

        // Automatically run audit if IDS document is active
        if (IDS.Module.activeDocument) {
            try {
                await runBrowserAudit();
                success('Audit completed successfully');
            } catch (auditErr) {
                console.error('Auto-audit failed: ', auditErr);
                // Check if it's an IDS validation error - show it even for auto-audit
                const message = auditErr instanceof Error ? auditErr.message : String(auditErr);
                if (message.includes('XMLSchema') || message.includes('xmlschema') || message.includes('IDS')) {
                    idsValidationError(auditErr);
                }
                // Otherwise, don't show error toast for auto-audit failure, just log it
            }
        }

        return true;
    } catch (err) {
        error(`Failed to export IFC: ${err instanceof Error ? err.message : String(err)}`);
        return false;
    } finally {
        Revit.exporting = false;
    }
};
