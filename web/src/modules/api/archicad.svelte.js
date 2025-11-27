import { IFCModels, loadIfc, auditIfc, runAudit as runBrowserAudit } from './api.svelte.js';
import * as IDS from './ids.svelte.js';
import { error, success } from '../utils/toast.svelte.js';
import hyperid from 'hyperid';

// Archicad connection state
export let Archicad = $state({
    enabled: false,
    apiUrl: null,
    connected: false,
    auditing: false,
    loading: false
});

const id = hyperid();
const pendingAudits = new Map();

// Check for Archicad API URL in URL parameters
const urlParams = new URLSearchParams(window.location.search);
const apiUrl = urlParams.get('api');

if (apiUrl) {
    Archicad.enabled = true;
    Archicad.apiUrl = apiUrl;
} else {
    // Default to localhost:48882 (Archicad API server) if no API URL provided
    const isLocalhost = window.location.hostname === 'localhost' || 
                       window.location.hostname === '127.0.0.1' ||
                       window.location.hostname === '0.0.0.0';
    
    Archicad.enabled = true;
    if (isLocalhost) {
        Archicad.apiUrl = 'http://localhost:48882';
    } else {
        // For remote access, user must provide IP via ?api= parameter
        Archicad.enabled = false;
        console.warn('Archicad integration requires ?api= parameter when accessing from remote host');
    }
}

/**
 * Test connection to Archicad API server
 */
export const connect = async () => {
    if (!Archicad.apiUrl) {
        error('No Archicad API URL provided');
        return false;
    }
    
    try {
        Archicad.loading = true;
        
        const statusUrl = `${Archicad.apiUrl}/status`;
        
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 5000);
        
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
                Archicad.connected = true;
                success('Connected to Archicad');
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
        Archicad.connected = false;
        
        if (err.message.includes('Mixed Content') || err.message.includes('blocked')) {
            error('Mixed content blocked. Use Cloudflare Tunnel proxy or access via HTTP.');
            console.error('To fix: Configure Cloudflare Tunnel to proxy /archicad-api/* to your local Archicad API');
        } else if (err.message.includes('timeout')) {
            error('Connection timeout. Archicad API server may be down. Please reload the add-in in Archicad.');
        } else {
            error(`Failed to connect to Archicad: ${err.message}`);
        }
        return false;
    } finally {
        Archicad.loading = false;
    }
};

/**
 * Disconnect from Archicad
 */
export const disconnect = () => {
    Archicad.connected = false;
    success('Disconnected from Archicad');
};

/**
 * Run audit using current IDS document against Archicad's IFC model
 * Validation happens in the browser using WebAssembly/Pyodide
 * Archicad only provides the IFC file and element selection
 * @returns {Promise<string|null>} Returns audit ID when completed, null if failed
 */
export const runAudit = async () => {
    if (!Archicad.connected || !IDS.Module.activeDocument) {
        return null;
    }
    
    try {
        Archicad.auditing = true;
        
        if (IFCModels.models.length === 0) {
            throw new Error('No IFC model loaded. Please load an IFC file first.');
        }
        
        const auditReport = await runBrowserAudit();
        
        Archicad.auditing = false;
        success('Audit completed (Archicad)');
        
        return auditReport?.id || null;
        
    } catch (err) {
        Archicad.auditing = false;
        error(`Failed to run Archicad audit: ${err.message}`);
        return null;
    }
};

/**
 * Select element in Archicad by IfcGUID/GlobalId
 * @param {string} globalId - IFC GlobalId (from entity.global_id)
 * @returns {Promise<boolean>} Returns true if request was sent (assumes success)
 */
export const selectElement = async (globalId) => {
    if (!Archicad.apiUrl || !Archicad.connected) {
        error('Not connected to Archicad');
        return false;
    }
    
    if (!globalId || typeof globalId !== 'string' || globalId.trim() === '' || globalId === '-') {
        error(`Invalid GlobalId: ${globalId}`);
        return false;
    }
    
    const encodedGuid = encodeURIComponent(globalId.trim());
    const selectUrl = `${Archicad.apiUrl}/select-by-guid/${encodedGuid}`;
    
    return new Promise((resolve) => {
        try {
            const img = new Image();
            let requestCompleted = false;
            let timeoutId = null;
            
            img.onload = () => {
                if (!requestCompleted) {
                    requestCompleted = true;
                    if (timeoutId) clearTimeout(timeoutId);
                    console.log('Archicad API successful for GUID:', globalId);
                    success(`Element with GUID ${globalId} selected in Archicad`);
                    resolve(true);
                }
            };
            
            img.onerror = (event) => {
                if (!requestCompleted) {
                    requestCompleted = true;
                    if (timeoutId) clearTimeout(timeoutId);
                    
                    if (!Archicad.connected) {
                        error('Archicad API is not responding. Please reload the add-in in Archicad.');
                        resolve(false);
                        return;
                    }
                    
                    console.log('Archicad API request sent for GUID:', globalId, 
                        '(CORS error expected, but action likely worked in Archicad)');
                    
                    success(`Element with GUID ${globalId} selected in Archicad`);
                    resolve(true);
                }
            };
            
            timeoutId = setTimeout(() => {
                if (!requestCompleted) {
                    requestCompleted = true;
                    error('Request timed out. Archicad API may be down. Please reload the add-in in Archicad.');
                    resolve(false);
                }
            }, 10000);
            
            img.src = selectUrl;
            
        } catch (err) {
            console.error('Error setting up switchback request:', err);
            error(`Failed to select element: ${err.message}. Please reload the add-in in Archicad.`);
            resolve(false);
        }
    });
};

/**
 * Get available IFC export configurations from Archicad
 * @returns {Promise<string[]>} Returns array of configuration names
 */
export const getIfcConfigurations = async () => {
    if (!Archicad.apiUrl || !Archicad.connected) {
        error('Not connected to Archicad');
        return [];
    }
    
    try {
        const configUrl = `${Archicad.apiUrl}/ifc-configurations`;
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
 * Export active view as IFC from Archicad
 * @param {string} configurationName - Name of the IFC export configuration to use
 * @returns {Promise<File|null>} Returns the exported IFC file, or null if failed
 */
export const exportIfc = async (configurationName) => {
    if (!Archicad.apiUrl || !Archicad.connected) {
        error('Not connected to Archicad');
        return null;
    }
    
    if (!configurationName || typeof configurationName !== 'string') {
        error('Invalid IFC configuration name');
        return null;
    }
    
    try {
        const exportUrl = `${Archicad.apiUrl}/export-ifc`;
        
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
        
        const blob = await response.blob();
        
        const contentDisposition = response.headers.get('Content-Disposition');
        let fileName = `Export_${new Date().toISOString().replace(/[:.]/g, '-')}.ifc`;
        if (contentDisposition) {
            const fileNameMatch = contentDisposition.match(/filename="?([^"]+)"?/);
            if (fileNameMatch) {
                fileName = fileNameMatch[1];
            }
        }
        
        const file = new File([blob], fileName, { type: 'application/octet-stream' });
        
        success(`IFC exported successfully: ${fileName}`);
        return file;
    } catch (err) {
        console.error('Failed to export IFC:', err);
        error(`Failed to export IFC: ${err.message}`);
        return null;
    }
};
