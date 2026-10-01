import { IFCModels } from "./api.svelte";
import { Revit } from "./revit.svelte.js";
import type { AuditReportEntity, AuditRequirement } from "$src/types/report";

// Write-back: fix failed IDS requirements from the audit results and apply them to the open Revit model.
// HTTP contract (shared with revit/Writeback): POST /resolve-parameters, POST /apply-changes,
// and "capabilities": ["writeback"] in GET /status.

export type WritebackFacet = "property" | "attribute";

export type WritebackTarget =
    | {
          supported: true;
          facet: WritebackFacet;
          propertySet?: string;
          name: string;
          /** Values the requirement allows: one for a simple value, several for an enumeration, none otherwise. */
          allowedValues: string[];
      }
    | { supported: false; reason: string };

export type ParameterScope = "instance" | "type";
export type ParameterStorageType = "string" | "integer" | "double" | "yesno" | "elementid";

export type ParameterCandidate = {
    parameter: string;
    scope: ParameterScope;
    storageType: ParameterStorageType;
    value: string;
    hasValue: boolean;
    readOnly: boolean;
    source: string;
};

export type ResolvedTarget = {
    found: boolean;
    elementId: number | null;
    elementName: string | null;
    category: string | null;
    /** The element's type and how many elements share it: what a type-scope change touches. */
    typeName: string | null;
    typeInstanceCount: number | null;
    candidates: ParameterCandidate[];
    message: string | null;
};

export type PendingChange = {
    key: string;
    globalId: string;
    specIndex: number;
    reqIndex: number;
    facet: WritebackFacet;
    propertySet?: string;
    name: string;
    /** Name and class from the audit report, shown until Revit has resolved the element. */
    entityName: string;
    entityClass: string;
    value: string;
    resolved: ResolvedTarget | null;
    candidateIndex: number;
    /** Message from the last /apply-changes that could not apply this change. */
    error: string | null;
};

export type AppliedChange = {
    key: string;
    elementName: string;
    parameter: string;
    scope: ParameterScope;
    newValue: string;
    message: string | null;
};

export type ChangeStatus = {
    candidate: ParameterCandidate | null;
    sendable: boolean;
    problem: string | null;
};

type WritebackState = {
    auditId: string | null;
    changes: Record<string, PendingChange>;
    applied: AppliedChange[];
    resolving: boolean;
    applying: boolean;
    resolveError: string | null;
    /** Set by Revit when a mapping file of the export setup could not be read. */
    resolveNote: string | null;
};

export const Writeback: WritebackState = $state({
    auditId: null,
    changes: {},
    applied: [],
    resolving: false,
    applying: false,
    resolveError: null,
    resolveNote: null
});

const WRITABLE_ATTRIBUTES = ["Name", "Description", "ObjectType", "LongName"];

const UNSUPPORTED_FACETS: Record<string, string> = {
    entity: "The IFC class cannot be changed from here",
    classification: "Classifications cannot be written to Revit from here",
    material: "Materials cannot be written to Revit from here",
    partof: "Relationships cannot be written to Revit from here"
};

const RESOLVE_TIMEOUT = 120_000;

export function isAvailable(): boolean {
    return Revit.enabled && Revit.connected && Revit.capabilities.includes("writeback");
}

// A facet value in AuditRequirement.metadata is {simpleValue} or a restriction. The report carries the
// restriction as ifctester exports it ("xs:restriction": [{"xs:enumeration": [...]}]); the IDS document
// in the editor carries the normalized form ({restriction: {enumeration: [...]}}). Accept both.
function simpleValue(value: unknown): string | null {
    if (typeof value === "string") return value;
    if (typeof value !== "object" || value === null) return null;
    const simple = (value as Record<string, unknown>).simpleValue;
    return typeof simple === "string" ? simple : null;
}

function enumerationValues(value: unknown): string[] {
    if (typeof value !== "object" || value === null) return [];
    const record = value as Record<string, unknown>;
    const raw = record["xs:restriction"] ?? record.restriction;
    const restriction = (Array.isArray(raw) ? raw[0] : raw) as Record<string, unknown> | undefined;
    if (!restriction || typeof restriction !== "object") return [];
    const enumeration = restriction["xs:enumeration"] ?? restriction.enumeration;
    if (!Array.isArray(enumeration)) return [];
    return enumeration
        .map((item) => (item as Record<string, unknown> | null)?.["@value"])
        .filter((item): item is string | number => typeof item === "string" || typeof item === "number")
        .map(String);
}

/** Works out from a report requirement whether its failures can be fixed by writing a Revit parameter. */
export function getWritebackTarget(requirement: AuditRequirement): WritebackTarget {
    const facetType = String(requirement.facet_type ?? "").toLowerCase();
    const metadata = requirement.metadata ?? {};

    if (facetType in UNSUPPORTED_FACETS) {
        return { supported: false, reason: UNSUPPORTED_FACETS[facetType] };
    }
    if (facetType !== "property" && facetType !== "attribute") {
        return { supported: false, reason: "This requirement cannot be written to Revit from here" };
    }
    if (metadata["@cardinality"] === "prohibited") {
        return { supported: false, reason: "Prohibited value: remove it in Revit" };
    }

    const simple = simpleValue(metadata.value);
    const allowedValues = simple !== null ? [simple] : enumerationValues(metadata.value);

    if (facetType === "attribute") {
        const name = simpleValue(metadata.name);
        if (name === null) {
            return { supported: false, reason: "The requirement does not name a single attribute" };
        }
        if (!WRITABLE_ATTRIBUTES.includes(name)) {
            return {
                supported: false,
                reason: `Attribute ${name} cannot be written to Revit (only ${WRITABLE_ATTRIBUTES.join(", ")})`
            };
        }
        return { supported: true, facet: "attribute", name, allowedValues };
    }

    const name = simpleValue(metadata.baseName);
    const propertySet = simpleValue(metadata.propertySet);
    if (name === null) {
        return { supported: false, reason: "The requirement does not name a single property" };
    }
    if (propertySet === null) {
        return { supported: false, reason: "The requirement does not name a single property set" };
    }
    return { supported: true, facet: "property", propertySet, name, allowedValues };
}

export function changeKey(globalId: string, specIndex: number, reqIndex: number): string {
    return `${globalId}|${specIndex}:${reqIndex}`;
}

export function getChange(globalId: string, specIndex: number, reqIndex: number): PendingChange | undefined {
    return Writeback.changes[changeKey(globalId, specIndex, reqIndex)];
}

export function pendingChanges(): PendingChange[] {
    return Object.values(Writeback.changes);
}

export function requirementPendingCount(specIndex: number, reqIndex: number): number {
    return pendingChanges().filter((change) => change.specIndex === specIndex && change.reqIndex === reqIndex).length;
}

export function clear() {
    Writeback.auditId = null;
    Writeback.changes = {};
    Writeback.applied = [];
    Writeback.resolveError = null;
    Writeback.resolveNote = null;
}

export function discardPending() {
    Writeback.changes = {};
    Writeback.resolveError = null;
    Writeback.resolveNote = null;
}

export function removeChange(key: string) {
    delete Writeback.changes[key];
}

export function removeRequirementChanges(specIndex: number, reqIndex: number) {
    for (const change of pendingChanges()) {
        if (change.specIndex === specIndex && change.reqIndex === reqIndex) {
            delete Writeback.changes[change.key];
        }
    }
}

type ChangeSource = {
    auditId: string;
    specIndex: number;
    reqIndex: number;
    target: WritebackTarget;
};

/** Sets the fix value for one failed entity of one requirement. An empty value removes the pending change. */
export function setChange(source: ChangeSource, entity: AuditReportEntity, value: string) {
    const { auditId, specIndex, reqIndex, target } = source;
    const globalId = entity.global_id;
    if (!target.supported || !globalId || globalId === "-") return;

    // Pending changes belong to one audit; an edit against another audit starts over
    if (Writeback.auditId !== auditId) {
        clear();
        Writeback.auditId = auditId;
    }

    const key = changeKey(globalId, specIndex, reqIndex);
    const trimmed = value.trim();
    if (trimmed === "") {
        delete Writeback.changes[key];
        return;
    }

    const existing = Writeback.changes[key];
    if (existing) {
        existing.value = trimmed;
        existing.error = null;
        return;
    }

    Writeback.changes[key] = {
        key,
        globalId,
        specIndex,
        reqIndex,
        facet: target.facet,
        propertySet: target.propertySet,
        name: target.name,
        entityName: entity.name || "",
        entityClass: entity.class || "",
        value: trimmed,
        resolved: null,
        candidateIndex: -1,
        error: null
    };
}

export function setChanges(source: ChangeSource, entities: AuditReportEntity[], value: string) {
    for (const entity of entities) {
        setChange(source, entity, value);
    }
}

export function isCandidateWritable(candidate: ParameterCandidate): boolean {
    return !candidate.readOnly && candidate.storageType !== "elementid";
}

export function selectCandidate(key: string, candidateIndex: number) {
    const change = Writeback.changes[key];
    const candidate = change?.resolved?.candidates[candidateIndex];
    if (!change || !candidate || !isCandidateWritable(candidate)) return;
    change.candidateIndex = candidateIndex;
    change.error = null;
}

function validateValue(value: string, storageType: ParameterStorageType): string | null {
    if (storageType === "integer" && !/^[+-]?\d+$/.test(value)) {
        return `"${value}" is not a whole number`;
    }
    if (storageType === "yesno" && !/^(true|false|yes|no|1|0)$/i.test(value)) {
        return `"${value}" is not a yes/no value (use true, false, yes, no, 1 or 0)`;
    }
    return null;
}

function targetId(change: PendingChange, candidate: ParameterCandidate): string {
    return `${change.resolved?.elementId ?? change.globalId}|${candidate.scope}|${candidate.parameter}`;
}

/** Says whether a pending change can be sent to Revit, and if not, why. */
export function getChangeStatus(change: PendingChange): ChangeStatus {
    const resolved = change.resolved;
    if (!resolved) {
        return { candidate: null, sendable: false, problem: "Not resolved in Revit yet" };
    }
    if (!resolved.found) {
        return { candidate: null, sendable: false, problem: resolved.message || "Element not found in the Revit model" };
    }
    if (resolved.candidates.length === 0) {
        return { candidate: null, sendable: false, problem: resolved.message || "No matching Revit parameter" };
    }

    const candidate = resolved.candidates[change.candidateIndex] ?? null;
    if (!candidate) {
        const readOnly = resolved.candidates.every((c) => c.readOnly);
        return {
            candidate: null,
            sendable: false,
            problem: readOnly ? "The Revit parameter is read-only" : "The Revit parameter holds an element reference and cannot be set from here"
        };
    }

    const invalid = validateValue(change.value, candidate.storageType);
    if (invalid) {
        return { candidate, sendable: false, problem: invalid };
    }

    // Two requirements can ask for different values in the same Revit parameter
    const id = targetId(change, candidate);
    const conflict = pendingChanges().some((other) => {
        if (other.key === change.key || other.value === change.value) return false;
        const otherCandidate = other.resolved?.candidates[other.candidateIndex];
        return !!otherCandidate && targetId(other, otherCandidate) === id;
    });
    if (conflict) {
        return { candidate, sendable: false, problem: "Another pending change sets a different value in the same parameter" };
    }

    return { candidate, sendable: true, problem: null };
}

export function sendableChanges(): PendingChange[] {
    return pendingChanges().filter((change) => getChangeStatus(change).sendable);
}

/** Revit answered, but with an error status. 400 and 503 mean that nothing was done. */
export class RevitHttpError extends Error {
    status: number;

    constructor(status: number, message: string) {
        super(message);
        this.name = "RevitHttpError";
        this.status = status;
    }
}

// Without a timeout the request waits for Revit as long as it takes
async function postJson(path: string, body: unknown, timeout?: number): Promise<Record<string, unknown>> {
    if (!Revit.apiUrl || !Revit.connected) {
        throw new Error("Not connected to Revit");
    }

    const controller = new AbortController();
    const timeoutId = timeout ? setTimeout(() => controller.abort(), timeout) : undefined;
    try {
        const response = await fetch(`${Revit.apiUrl}${path}`, {
            method: "POST",
            mode: "cors",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body),
            signal: controller.signal
        });
        const data = (await response.json().catch(() => null)) as Record<string, unknown> | null;
        if (!response.ok) {
            throw new RevitHttpError(response.status, typeof data?.error === "string" ? data.error : `HTTP ${response.status}: ${response.statusText}`);
        }
        if (!data) {
            throw new Error("Revit returned a response that is not JSON");
        }
        return data;
    } catch (err) {
        if (err instanceof Error && err.name === "AbortError") {
            throw new Error("Revit did not answer in time");
        }
        throw err;
    } finally {
        clearTimeout(timeoutId);
    }
}

function parseCandidate(raw: Record<string, unknown>): ParameterCandidate {
    return {
        parameter: String(raw.parameter ?? ""),
        scope: raw.scope === "type" ? "type" : "instance",
        storageType: String(raw.storageType ?? "string").toLowerCase() as ParameterStorageType,
        value: raw.value == null ? "" : String(raw.value),
        hasValue: raw.hasValue === true,
        readOnly: raw.readOnly === true,
        source: String(raw.source ?? "")
    };
}

/**
 * The setup and file overrides the audited export was made with, so resolve reads the same
 * mapping files the export did. Falls back to the setup selected in the toolbar.
 */
export function resolveSettings(): { configuration?: string; psetFile?: string; parameterMappingFile?: string } {
    const last = Revit.lastExport;
    if (last?.fileName && IFCModels.models.some((model) => model.fileName === last.fileName)) {
        return {
            configuration: last.configuration,
            ...(last.psetFile ? { psetFile: last.psetFile } : {}),
            ...(last.parameterMappingFile ? { parameterMappingFile: last.parameterMappingFile } : {})
        };
    }
    return Revit.exportConfiguration ? { configuration: Revit.exportConfiguration } : {};
}

/** Asks Revit which parameter on which element each pending change would write to. Writes nothing. */
export async function resolvePending(): Promise<boolean> {
    const changes = pendingChanges();
    Writeback.resolveError = null;
    Writeback.resolveNote = null;
    if (changes.length === 0) return true;

    Writeback.resolving = true;
    try {
        const data = await postJson(
            "/resolve-parameters",
            {
                items: changes.map((change) =>
                    change.facet === "property"
                        ? { key: change.key, globalId: change.globalId, facet: change.facet, propertySet: change.propertySet, name: change.name }
                        : { key: change.key, globalId: change.globalId, facet: change.facet, name: change.name }
                ),
                // Tells Revit whose property set mapping files to read; without it, it uses its last export
                ...resolveSettings()
            },
            RESOLVE_TIMEOUT
        );
        Writeback.resolveNote = typeof data.mappingNote === "string" && data.mappingNote ? data.mappingNote : null;

        const items = new Map<string, Record<string, unknown>>();
        for (const item of Array.isArray(data.items) ? (data.items as Record<string, unknown>[]) : []) {
            items.set(String(item.key), item);
        }

        for (const sent of changes) {
            // The change may have been removed while Revit was answering
            const change = Writeback.changes[sent.key];
            if (!change) continue;

            const item = items.get(change.key);
            if (!item) {
                change.resolved = {
                    found: false,
                    elementId: null,
                    elementName: null,
                    category: null,
                    typeName: null,
                    typeInstanceCount: null,
                    candidates: [],
                    message: "Revit returned no answer for this element"
                };
                change.candidateIndex = -1;
                continue;
            }

            const previous = change.resolved?.candidates[change.candidateIndex];
            const candidates = (Array.isArray(item.candidates) ? (item.candidates as Record<string, unknown>[]) : []).map(parseCandidate);
            change.resolved = {
                found: item.found === true,
                elementId: typeof item.elementId === "number" ? item.elementId : null,
                elementName: typeof item.elementName === "string" ? item.elementName : null,
                category: typeof item.category === "string" ? item.category : null,
                typeName: typeof item.typeName === "string" ? item.typeName : null,
                typeInstanceCount: typeof item.typeInstanceCount === "number" ? item.typeInstanceCount : null,
                candidates,
                message: typeof item.message === "string" ? item.message : null
            };

            // Keep the user's pick across a re-resolve; otherwise take the best writable candidate
            const kept = previous
                ? candidates.findIndex((c) => c.parameter === previous.parameter && c.scope === previous.scope && isCandidateWritable(c))
                : -1;
            change.candidateIndex = kept >= 0 ? kept : candidates.findIndex(isCandidateWritable);
        }
        return true;
    } catch (err) {
        Writeback.resolveError = err instanceof Error ? err.message : String(err);
        return false;
    } finally {
        Writeback.resolving = false;
    }
}

export type ApplyOutcome = { applied: number; failed: number };

/**
 * Sends the resolved pending changes to Revit. Applied changes leave the pending list; the ones Revit
 * could not apply stay pending with its message.
 */
export async function applyPending(): Promise<ApplyOutcome> {
    const sending = sendableChanges().map((change) => {
        const candidate = getChangeStatus(change).candidate as ParameterCandidate;
        return { change, candidate };
    });
    if (sending.length === 0) return { applied: 0, failed: 0 };

    Writeback.applying = true;
    try {
        const data = await postJson(
            "/apply-changes",
            {
                changes: sending.map(({ change, candidate }) => ({
                    key: change.key,
                    globalId: change.globalId,
                    parameter: candidate.parameter,
                    scope: candidate.scope,
                    value: change.value
                }))
            }
            // No timeout: Revit may show its own warning dialog when the transaction commits
        );

        const results = new Map<string, Record<string, unknown>>();
        for (const result of Array.isArray(data.results) ? (data.results as Record<string, unknown>[]) : []) {
            results.set(String(result.key), result);
        }

        let applied = 0;
        let failed = 0;
        for (const { change, candidate } of sending) {
            const result = results.get(change.key);
            const message = typeof result?.message === "string" ? result.message : null;
            const pending = Writeback.changes[change.key];

            if (result?.ok === true) {
                applied++;
                Writeback.applied.push({
                    key: change.key,
                    elementName: change.resolved?.elementName || change.entityName || change.globalId,
                    parameter: candidate.parameter,
                    scope: candidate.scope,
                    newValue: result.newValue == null ? change.value : String(result.newValue),
                    message
                });
                delete Writeback.changes[change.key];
            } else {
                failed++;
                if (pending) {
                    pending.error = message || (result ? "Revit could not apply this change" : "Revit returned no result for this change");
                }
            }
        }
        return { applied, failed };
    } finally {
        Writeback.applying = false;
    }
}

// Pending changes are dropped when the audit they came from is replaced (re-audit, re-export, models cleared)
$effect.root(() => {
    $effect(() => {
        const auditId = Writeback.auditId;
        if (auditId !== null && !IFCModels.audits.some((audit) => audit.id === auditId)) {
            clear();
        }
    });
});
