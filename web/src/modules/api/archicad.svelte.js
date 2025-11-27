import { IFCModels, runAudit as runBrowserAudit } from "$src/modules/api/api.svelte.js";
import * as IDS from "$src/modules/api/ids.svelte.js";
import { error, success } from "$src/modules/utils/toast.svelte.js";

const hasBridge = typeof window !== "undefined" && typeof window.ACAPI !== "undefined";

export let Archicad = $state({
    enabled: hasBridge,
    connected: false,
    loading: false,
    auditing: false,
    selection: [],
    metadata: null,
    lastUpdated: null
});

function getBridge() {
    if (!Archicad.enabled || typeof window === "undefined" || typeof window.ACAPI === "undefined") {
        throw new Error("Archicad bridge not available");
    }
    return window.ACAPI;
}

function normalizeSelection(rawItems = []) {
    return (rawItems || []).map((entry) => ({
        guid: entry?.[0] || "",
        ifcGuid: entry?.[1] || "",
        typeName: entry?.[2] || "",
        elementId: entry?.[3] || ""
    }));
}

export async function connect() {
    if (!Archicad.enabled) {
        return false;
    }
    Archicad.loading = true;
    try {
        const bridge = getBridge();
        const info = await bridge.GetHostInfo?.();
        Archicad.metadata = {
            platform: info?.[0] || "Archicad",
            webAppUrl: info?.[1] || "",
            devServer: Boolean(info?.[2])
        };
        Archicad.connected = true;
        success("Connected to Archicad");
        await refreshSelection();
        return true;
    } catch (err) {
        Archicad.connected = false;
        error(`Failed to connect to Archicad: ${err.message || err}`);
        return false;
    } finally {
        Archicad.loading = false;
    }
}

export function disconnect() {
    Archicad.connected = false;
    Archicad.selection = [];
    success("Disconnected from Archicad");
}

export async function refreshSelection() {
    if (!Archicad.enabled || !Archicad.connected) {
        return [];
    }
    try {
        const bridge = getBridge();
        const items = await bridge.GetSelectedElements?.();
        Archicad.selection = normalizeSelection(items);
        Archicad.lastUpdated = new Date().toISOString();
        return Archicad.selection;
    } catch (err) {
        error(`Failed to read Archicad selection: ${err.message || err}`);
        return [];
    }
}

export async function selectElement(globalId) {
    if (!Archicad.enabled || !Archicad.connected) {
        throw new Error("Archicad bridge not connected");
    }
    if (!globalId || globalId === "-") {
        return false;
    }
    try {
        const bridge = getBridge();
        const result = await bridge.SelectElementByGuid?.(globalId);
        if (!result) {
            throw new Error("The Archicad API could not highlight the element");
        }
        success("Element highlighted in Archicad");
        return true;
    } catch (err) {
        error(`Failed to highlight element in Archicad: ${err.message || err}`);
        return false;
    }
}

export async function runAudit() {
    if (!Archicad.connected) {
        throw new Error("Connect to Archicad first");
    }
    if (!IDS.Module.activeDocument) {
        throw new Error("Create or open an IDS document first");
    }
    try {
        Archicad.auditing = true;
        const report = await runBrowserAudit();
        success("Audit completed using Archicad data");
        return report;
    } catch (err) {
        error(`Audit failed: ${err.message || err}`);
        return null;
    } finally {
        Archicad.auditing = false;
    }
}

function installHostCallbacks() {
    if (typeof window === "undefined") {
        return;
    }
    window.__IfcTesterArchicad__ = {
        onHostReady: () => {
            if (!Archicad.connected) {
                connect();
            } else {
                refreshSelection();
            }
        },
        selectionChanged: () => {
            refreshSelection();
        }
    };
}

installHostCallbacks();
