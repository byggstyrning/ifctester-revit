import wasm from "$src/modules/wasm";
import * as IDS from "./ids.svelte";
import {
    PSET_BUILDER,
    Revit,
    getIfcConfigurationFiles,
    getModelParameters,
    getPsetSuggestions,
    savePsetFile
} from "./revit.svelte.js";
import {
    type EntitySchema,
    type ModelParameter,
    type NotCovered,
    type PsetRow,
    type SchemaName,
    type SuggestionResult,
    applyParsed,
    applySuggestion,
    canonicalEntity,
    generatePsetFile,
    makeSchema,
    occurrenceEntityOf,
    parsePsetFile,
    rowsFromIds,
    typeEntityOf
} from "$src/modules/psetBuilder/psetFile";
import type { IdsDocument } from "$src/types/ids";

// Pset builder: the IDS's required properties as rows, mapped to Revit parameters, written as a
// Revit user-defined property set file. State per IDS document; the rows are kept in
// localStorage per IDS title and version so a designer can come back to them.
// HTTP contract (revit/Writeback): GET /model-parameters, POST /pset-suggestions,
// POST /pset-files/save, and "pset-builder" in GET /status capabilities.

export type BuilderState = {
    docId: string;
    storageKey: string;
    ready: boolean;
    schemaName: SchemaName;
    rows: PsetRow[];
    notCovered: NotCovered[];
    ifcVersions: string[];
    /** Where Save writes; empty: the add-in's default folder. */
    savePath: string;
    lastSavedPath: string | null;
    /** Set when a save found the file already there, so the page can offer to overwrite it. */
    overwritePending: { path: string; thenUse: boolean } | null;
    busy: "" | "suggest" | "save" | "open";
    scopeMethod: string | null;
    suggestedAt: string | null;
    modelParameters: ModelParameter[] | null;
    modelSummary: string | null;
    error: string | null;
};

type Stored = { v: 1; schema: SchemaName; savePath: string; rows: PsetRow[] };

export const PsetBuilder: { builders: Record<string, BuilderState>; schemas: Record<string, EntitySchema> } = $state({
    builders: {},
    schemas: {}
});

const message = (err: unknown) => (err instanceof Error ? err.message : String(err));

/** The add-in answers the builder's endpoints (it reports "pset-builder"). Without it the builder drafts only. */
export function canUseRevit(): boolean {
    return Revit.connected && Revit.capabilities.includes(PSET_BUILDER);
}

export function schemaOf(builder: BuilderState): EntitySchema | null {
    return PsetBuilder.schemas[builder.schemaName] ?? null;
}

function documentOf(docId: string): IdsDocument | null {
    return (IDS.Module.documents[docId] as IdsDocument | undefined) ?? null;
}

function storageKeyOf(doc: IdsDocument): string {
    return `ifctester.psetBuilder.${doc.info?.title || "untitled"}|${doc.info?.version || ""}`;
}

function readStored(key: string): Stored | null {
    try {
        const raw = localStorage.getItem(key);
        if (!raw) return null;
        const stored = JSON.parse(raw) as Stored;
        return stored?.v === 1 && Array.isArray(stored.rows) ? stored : null;
    } catch {
        return null;
    }
}

/** Keeps the builder's work for this IDS; storage may be blocked, then it lasts for this page. */
export function persist(builder: BuilderState): void {
    try {
        const stored: Stored = { v: 1, schema: builder.schemaName, savePath: builder.savePath, rows: $state.snapshot(builder.rows) as PsetRow[] };
        localStorage.setItem(builder.storageKey, JSON.stringify(stored));
    } catch {
        // Storage blocked or full
    }
}

export async function ensureSchema(name: SchemaName): Promise<EntitySchema> {
    const loaded = PsetBuilder.schemas[name];
    if (loaded) return loaded;
    const tree = await wasm.getEntityTree(name);
    const schema = makeSchema(name, tree);
    PsetBuilder.schemas[name] = schema;
    return PsetBuilder.schemas[name];
}

/** The selected export setup's schema when the add-in can tell, otherwise IFC2X3. */
async function defaultSchema(): Promise<SchemaName> {
    if (!Revit.connected || !Revit.exportConfiguration) return "IFC2X3";
    try {
        const files = await getIfcConfigurationFiles(Revit.exportConfiguration);
        const version = String(files?.ifcVersion ?? "");
        if (/^IFC4/i.test(version)) return "IFC4";
    } catch {
        // An older add-in or no such setup: the default stands
    }
    return "IFC2X3";
}

/** Fresh rows from the IDS with the user's work laid over them: the stored row wins where the key matches. */
function mergeRows(fresh: PsetRow[], previous: PsetRow[]): PsetRow[] {
    const byKey = new Map(previous.map((row) => [row.key, row]));
    const merged = fresh.map((row) => {
        const old = byKey.get(row.key);
        if (!old) return row;
        byKey.delete(row.key);
        return { ...old, specs: row.specs, dataTypeNote: old.dataTypeNote ?? row.dataTypeNote };
    });
    // Rows from an opened file, or from an earlier version of the IDS that still carry a mapping
    for (const row of byKey.values()) {
        if (row.specs.length === 0 || row.edited) merged.push({ ...row, specs: [] });
    }
    return merged;
}

/** Opens the builder for an IDS document, restoring earlier work for the same IDS title and version. */
export async function openBuilder(docId: string): Promise<BuilderState | null> {
    const existing = PsetBuilder.builders[docId];
    if (existing) return existing;
    const doc = documentOf(docId);
    if (!doc) return null;

    const storageKey = storageKeyOf(doc);
    const stored = readStored(storageKey);
    PsetBuilder.builders[docId] = {
        docId,
        storageKey,
        ready: false,
        schemaName: stored?.schema ?? "IFC2X3",
        rows: [],
        notCovered: [],
        ifcVersions: [],
        savePath: stored?.savePath ?? "",
        lastSavedPath: null,
        overwritePending: null,
        busy: "",
        scopeMethod: null,
        suggestedAt: null,
        modelParameters: null,
        modelSummary: null,
        error: null
    };
    const builder = PsetBuilder.builders[docId];
    try {
        if (!stored) builder.schemaName = await defaultSchema();
        const schema = await ensureSchema(builder.schemaName);
        const fromIds = rowsFromIds($state.snapshot(doc) as IdsDocument, schema);
        builder.rows = mergeRows(fromIds.rows, stored?.rows ?? []);
        builder.notCovered = fromIds.notCovered;
        builder.ifcVersions = fromIds.ifcVersions;
    } catch (err) {
        builder.error = `The builder could not read the IDS: ${message(err)}`;
    }
    builder.ready = true;
    return builder;
}

/** Reads the IDS again after it was edited, keeping the mappings made so far. */
export async function reloadFromIds(builder: BuilderState): Promise<void> {
    const doc = documentOf(builder.docId);
    if (!doc) return;
    const schema = await ensureSchema(builder.schemaName);
    const fromIds = rowsFromIds($state.snapshot(doc) as IdsDocument, schema);
    builder.rows = mergeRows(fromIds.rows, $state.snapshot(builder.rows) as PsetRow[]);
    builder.notCovered = fromIds.notCovered;
    builder.ifcVersions = fromIds.ifcVersions;
}

/** Changes the schema; entity names are taken again from it (IfcDoorStyle in IFC2X3 becomes IfcDoorType in IFC4). */
export async function setSchema(builder: BuilderState, name: SchemaName): Promise<void> {
    if (builder.schemaName === name) return;
    const from = await ensureSchema(builder.schemaName);
    const to = await ensureSchema(name);
    builder.schemaName = name;
    for (const row of builder.rows) {
        row.entities = row.entities.map((entity) => {
            if (row.scope === "type") {
                const occurrence = occurrenceEntityOf(entity, from);
                return (occurrence && typeEntityOf(occurrence, to)) ?? canonicalEntity(entity, to) ?? entity;
            }
            return canonicalEntity(entity, to) ?? entity;
        });
    }
}

/** GET /model-parameters, for the parameter picker and the warnings. */
export async function loadModelParameters(builder: BuilderState): Promise<void> {
    const result = await getModelParameters();
    builder.modelParameters = result?.parameters ?? [];
    builder.modelSummary = result?.message
        ? result.message
        : `${builder.modelParameters.length} parameters on ${result.elementCount} elements and ${result.typeCount} types of ${result.document ?? "the model"} (scan ${result.elapsedMs} ms)`;
}

/** "Suggest from model": the best parameter for every row the user has not edited. */
export async function suggestFromModel(builder: BuilderState): Promise<void> {
    if (builder.busy) return;
    builder.busy = "suggest";
    builder.error = null;
    try {
        const rows = builder.rows.filter((row) => !row.excluded);
        const [result] = await Promise.all([
            getPsetSuggestions(rows.map((row) => ({ key: row.key, propertySet: row.propertySet, name: row.name, entities: [...row.entities] }))),
            builder.modelParameters ? Promise.resolve() : loadModelParameters(builder)
        ]);
        if (result?.message) throw new Error(result.message);
        const schema = schemaOf(builder);
        const byKey = new Map((result?.items ?? []).map((item: SuggestionResult) => [item.key, item]));
        for (const row of builder.rows) {
            const item = byKey.get(row.key);
            if (item) applySuggestion(row, item, schema);
        }
        builder.scopeMethod = result?.scopeMethod ?? null;
        builder.suggestedAt = new Date().toLocaleTimeString();
    } catch (err) {
        builder.error = `Suggestions failed: ${message(err)}`;
    } finally {
        builder.busy = "";
    }
}

/** The file name the add-in uses when no path is given: the IDS title. */
export function fileBaseName(builder: BuilderState): string {
    const doc = documentOf(builder.docId);
    const title = (doc?.info?.title || "untitled").replace(/[\\/:*?"<>|]+/g, "_").trim();
    return `${title} pset.txt`;
}

export function generate(builder: BuilderState, date = new Date()): string {
    const doc = documentOf(builder.docId);
    return generatePsetFile(builder.rows, {
        title: doc?.info?.title || "untitled IDS",
        version: doc?.info?.version || undefined,
        date: date.toISOString().slice(0, 10),
        schema: schemaOf(builder)
    });
}

/**
 * Saves through the add-in. A file that is already there is only replaced with `overwrite`;
 * otherwise the builder notes it, so the page can ask. Returns the saved path or null.
 */
export async function save(builder: BuilderState, options: { overwrite?: boolean; thenUse?: boolean } = {}): Promise<string | null> {
    if (builder.busy) return null;
    builder.busy = "save";
    builder.error = null;
    builder.overwritePending = null;
    try {
        // Saving again to the file this builder wrote replaces it without asking
        const path = builder.savePath.trim() || builder.lastSavedPath || "";
        const result = await savePsetFile({
            ...(path ? { path } : { name: fileBaseName(builder) }),
            content: generate(builder),
            overwrite: options.overwrite ?? (path !== "" && path === builder.lastSavedPath)
        });
        builder.lastSavedPath = result.path;
        if (options.thenUse) Revit.exportOverrides.psetFile = result.path;
        return result.path;
    } catch (err) {
        const status = (err as { status?: number }).status;
        const existing = (err as { data?: { path?: string } }).data?.path;
        if (status === 409) {
            builder.overwritePending = { path: existing ?? targetPath(builder), thenUse: options.thenUse ?? false };
        } else {
            builder.error = `Save failed: ${message(err)}`;
        }
        return null;
    } finally {
        builder.busy = "";
    }
}

function targetPath(builder: BuilderState): string {
    return builder.savePath.trim() || fileBaseName(builder);
}

/** Saves the file in the browser's downloads; works without Revit. */
export function download(builder: BuilderState): void {
    const blob = new Blob([generate(builder)], { type: "text/plain;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = fileBaseName(builder);
    a.click();
    URL.revokeObjectURL(url);
}

/** Reads a pset file into the table: its mappings replace the matching rows, other properties are added. */
export function loadPsetFileText(builder: BuilderState, text: string): { updated: number; added: number } {
    const parsed = parsePsetFile(text);
    return applyParsed(builder.rows, parsed, schemaOf(builder));
}

/** "Open pset file…": a local file picked in the browser. */
export function openPsetFile(builder: BuilderState): Promise<{ updated: number; added: number } | null> {
    return new Promise((resolve) => {
        const input = document.createElement("input");
        input.type = "file";
        input.accept = ".txt";
        input.onchange = async () => {
            const file = input.files?.[0];
            if (!file) return resolve(null);
            try {
                resolve(loadPsetFileText(builder, await file.text()));
            } catch (err) {
                builder.error = `The file could not be read: ${message(err)}`;
                resolve(null);
            }
        };
        input.click();
    });
}

/** Forgets the builder's work for this IDS and starts again from the IDS alone. */
export async function reset(builder: BuilderState): Promise<void> {
    try {
        localStorage.removeItem(builder.storageKey);
    } catch {
        // Storage blocked
    }
    const doc = documentOf(builder.docId);
    if (!doc) return;
    const fromIds = rowsFromIds($state.snapshot(doc) as IdsDocument, await ensureSchema(builder.schemaName));
    builder.rows = fromIds.rows;
    builder.notCovered = fromIds.notCovered;
    builder.lastSavedPath = null;
    builder.overwritePending = null;
}
