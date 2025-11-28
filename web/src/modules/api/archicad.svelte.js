import { IFCModels, loadIfc, auditIfc, runAudit as runBrowserAudit } from './api.svelte.js';
import * as IDS from './ids.svelte.js';
import { error, success } from '../utils/toast.svelte.js';
import hyperid from 'hyperid';

// ArchiCAD connection state
export let ArchiCAD = $state({
    enabled: false,
    apiUrl: null,
    connected: false,
    auditing: false,
    loading: false
});

const id = hyperid();

// Check for ArchiCAD API URL in URL parameters
const urlParams = new URLSearchParams(window.location.search);
const apiUrl = urlParams.get('api');

if (apiUrl) {
    ArchiCAD.enabled = true;
    ArchiCAD.apiUrl = apiUrl;
} else {
    // Default ArchiCAD API port (from ArchiCADApiServer)
    const isLocalhost = window.location.hostname === 'localhost' || 
                       window.location.hostname === '127.0.0.1' ||
                       window.location.hostname === '0.0.0.0';
    
    if (isLocalhost) {
        ArchiCAD.enabled = true;
        ArchiCAD.apiUrl = 'http://localhost:48882'; // ArchiCAD API server port
    } else {
        ArchiCAD.enabled = false;
        console.warn('ArchiCAD integration requires ?api= parameter when accessing from remote host');
    }
}

/**
 * Test connection to ArchiCAD API server
 */
export const connect = async () => {
    if (!ArchiCAD.apiUrl) {
        error('No ArchiCAD API URL provided');
        return false;
    }
    
    try {
        ArchiCAD.loading = true;
        
        const statusUrl = `${ArchiCAD.apiUrl}/status`;
        console.log(`[ArchiCAD] Attempting to connect to: ${statusUrl}`);
        
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
            
            console.log(`[ArchiCAD] Response status: ${response.status} ${response.statusText}`);
            
            if (response.ok) {
                const data = await response.json().catch(() => ({}));
                console.log('[ArchiCAD] Connection successful:', data);
                ArchiCAD.connected = true;
                success('Connected to ArchiCAD');
                return true;
            } else {
                const errorText = await response.text().catch(() => response.statusText);
                console.error(`[ArchiCAD] HTTP error: ${response.status}`, errorText);
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }
        } catch (fetchErr) {
            clearTimeout(timeoutId);
            
            console.error('[ArchiCAD] Fetch error:', fetchErr);
            
            if (fetchErr.name === 'AbortError') {
                throw new Error('Connection timeout - ArchiCAD API server may not be running');
            } else if (fetchErr.message.includes('Failed to fetch') || fetchErr.message.includes('NetworkError')) {
                throw new Error('Network error - Cannot reach ArchiCAD API server. Check if add-on is loaded.');
            } else if (fetchErr.message.includes('CORS') || fetchErr.message.includes('cross-origin')) {
                throw new Error('CORS error - API server may not be configured correctly');
            }
            throw fetchErr;
        }
    } catch (err) {
        ArchiCAD.connected = false;
        
        console.error('[ArchiCAD] Connection failed:', err);
        
        if (err.message.includes('timeout')) {
            error('Connection timeout. Make sure IfcTester add-on is loaded in ArchiCAD.');
        } else if (err.message.includes('Network error')) {
            error('Cannot connect to ArchiCAD API server. Verify the add-on is loaded and port 48882 is available.');
        } else {
            error(`Failed to connect to ArchiCAD: ${err.message}`);
        }
        return false;
    } finally {
        ArchiCAD.loading = false;
    }
};

/**
 * Disconnect from ArchiCAD
 */
export const disconnect = () => {
    ArchiCAD.connected = false;
    success('Disconnected from ArchiCAD');
};

/**
 * Run audit using current IDS document against ArchiCAD's IFC model
 * @returns {Promise<string|null>} Returns audit ID when completed, null if failed
 */
export const runAudit = async () => {
    if (!ArchiCAD.connected || !IDS.Module.activeDocument) {
        return null;
    }
    
    try {
        ArchiCAD.auditing = true;
        
        if (IFCModels.models.length === 0) {
            throw new Error('No IFC model loaded. Please export from ArchiCAD first.');
        }
        
        const auditReport = await runBrowserAudit();
        
        ArchiCAD.auditing = false;
        success('Audit completed (ArchiCAD)');
        
        return auditReport?.id || null;
        
    } catch (err) {
        ArchiCAD.auditing = false;
        error(`Failed to run ArchiCAD audit: ${err.message}`);
        return null;
    }
};

/**
 * Select element in ArchiCAD by IfcGUID/GlobalId
 * @param {string} globalId - IFC GlobalId
 * @returns {Promise<boolean>} Returns true if successful
 */
export const selectElement = async (globalId) => {
    if (!globalId || typeof globalId !== 'string' || globalId.trim() === '' || globalId === '-') {
        error(`Invalid GlobalId: ${globalId}`);
        return false;
    }
    
    const trimmedGuid = globalId.trim();
    
    // Prefer window.ACAPI (runs on main thread via RegisterAsynchJSObject)
    // This is available when the browser palette is loaded in ArchiCAD
    if (window.ACAPI?.SelectElementByGUID) {
        try {
            const result = await window.ACAPI.SelectElementByGUID(trimmedGuid);
            if (result) {
                success('Element selected in ArchiCAD');
                return true;
            } else {
                error('Element not found in ArchiCAD');
                return false;
            }
        } catch (err) {
            error(`Failed to select element: ${err.message}`);
            return false;
        }
    }
    
    // Fallback to HTTP API (for external access or when ACAPI is not available)
    if (!ArchiCAD.apiUrl || !ArchiCAD.connected) {
        error('Not connected to ArchiCAD. window.ACAPI not available and HTTP API not configured.');
        return false;
    }
    
    const encodedGuid = encodeURIComponent(trimmedGuid);
    const selectUrl = `${ArchiCAD.apiUrl}/select-by-guid/${encodedGuid}`;
    
    try {
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 10000);
        
        const response = await fetch(selectUrl, {
            method: 'GET',
            mode: 'cors',
            signal: controller.signal
        });
        
        clearTimeout(timeoutId);
        
        if (response.ok) {
            const data = await response.json();
            if (data.success) {
                success(`Element selected in ArchiCAD`);
                return true;
            } else {
                error(data.message || 'Element not found in ArchiCAD');
                return false;
            }
        } else {
            throw new Error(`HTTP ${response.status}`);
        }
    } catch (err) {
        if (err.name === 'AbortError') {
            error('Request timed out');
        } else {
            error(`Failed to select element: ${err.message}`);
        }
        return false;
    }
};

/**
 * Get available IFC export configurations from ArchiCAD
 * @returns {Promise<Array>} Returns array of configuration objects
 */
export const getIfcConfigurations = async () => {
    if (!ArchiCAD.apiUrl || !ArchiCAD.connected) {
        error('Not connected to ArchiCAD');
        return [];
    }
    
    try {
        const configUrl = `${ArchiCAD.apiUrl}/ifc-configurations`;
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
 * Export IFC from ArchiCAD
 * @param {string} configurationName - Name of the IFC export configuration to use
 * @returns {Promise<File|null>} Returns the exported IFC file, or null if failed
 */
export const exportIfc = async (configurationName) => {
    if (!ArchiCAD.apiUrl || !ArchiCAD.connected) {
        error('Not connected to ArchiCAD');
        return null;
    }
    
    if (!configurationName || typeof configurationName !== 'string') {
        error('Invalid IFC configuration name');
        return null;
    }
    
    try {
        const exportUrl = `${ArchiCAD.apiUrl}/export-ifc`;
        
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
        let fileName = `ArchiCAD_Export_${new Date().toISOString().replace(/[:.]/g, '-')}.ifc`;
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

