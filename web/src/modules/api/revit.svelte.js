import { IFCModels, loadIfc, auditIfc, runAudit as runBrowserAudit } from './api.svelte.js';
import * as IDS from './ids.svelte.js';
import { error, success } from '../utils/toast.svelte.js';
import hyperid from 'hyperid';

// Revit connection state
export let Revit = $state({
    enabled: false,
    apiUrl: null,
    connected: false,
    auditing: false,
    loading: false
});

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
 * Export active view as IFC from Revit
 * @param {string} configurationName - Name of the IFC export configuration to use
 * @returns {Promise<File|null>} Returns the exported IFC file, or null if failed
 */
export const exportIfc = async (configurationName) => {
    if (!Revit.apiUrl || !Revit.connected) {
        error('Not connected to Revit');
        return null;
    }
    
    if (!configurationName || typeof configurationName !== 'string') {
        error('Invalid IFC configuration name');
        return null;
    }
    
    try {
        const exportUrl = `${Revit.apiUrl}/export-ifc`;
        
        const response = await fetch(exportUrl, {
            method: 'POST',
            mode: 'cors',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ configuration: configurationName })
        });
        
        if (!response.ok) {
            const errorData = await response.json().catch(() => ({ error: 'Unknown error' }));
            throw new Error(errorData.error || `HTTP ${response.status}: ${response.statusText}`);
        }
        
        // Get the file blob
        const blob = await response.blob();
        
        // Extract filename from Content-Disposition header if available
        const contentDisposition = response.headers.get('Content-Disposition');
        let fileName = `Export_${new Date().toISOString().replace(/[:.]/g, '-')}.ifc`;
        if (contentDisposition) {
            const fileNameMatch = contentDisposition.match(/filename="?([^"]+)"?/);
            if (fileNameMatch) {
                fileName = fileNameMatch[1];
            }
        }
        
        // Create a File object from the blob
        const file = new File([blob], fileName, { type: 'application/octet-stream' });
        
        success(`IFC exported successfully: ${fileName}`);
        return file;
    } catch (err) {
        console.error('Failed to export IFC:', err);
        error(`Failed to export IFC: ${err.message}`);
        return null;
    }
};


