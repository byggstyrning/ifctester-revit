import wasm from "$src/modules/wasm";
import { IFCModels } from "./api.svelte";
import { Revit, ELEMENT_INSPECTOR, getElementParameters } from "./revit.svelte.js";
import { resolveSettings, type ParameterCandidate } from "./writeback.svelte";
import type { AuditReportEntity } from "$src/types/report";
import * as PsetDraftModule from "./psetDraft.svelte";
import * as PsetEdit from "$src/modules/psetBuilder/psetEdit";

// Element inspector: one element of an audited IFC next to the same element in Revit, joined by
// the export setup's property set file. HTTP contract: POST /element-parameters, and
// "element-inspector" in the capabilities of GET /status.

export type IfcObject = {
    ifcClass: string;
    globalId: string | null;
    attributes: Record<string, unknown>;
    psets: Record<string, Record<string, unknown>>;
    qtos: Record<string, Record<string, unknown>>;
};

export type IfcElement =
    | ({ found: true; schema: string; type: IfcObject | null } & IfcObject)
    | { found: false; schema: string };

export type RevitParameter = {
    name: string;
    scope: "instance" | "type";
    group: string;
    storageType: string;
    value: string;
    hasValue: boolean;
    readOnly: boolean;
    shared: boolean;
    builtIn: string | null;
};

export type MappingLine = {
    propertySet: string;
    propertyName: string;
    parameterName: string | null;
    builtInParameter: string | null;
    entities: string[];
    origin: "pset-file" | "mapping-table";
    onInstance: boolean;
    onType: boolean;
    /** 1-based line in its file and its set's PropertySet: header; 0 when unknown. */
    lineNumber: number;
    headerLineNumber: number;
    dataType: string;
    candidates: ParameterCandidate[];
};

export type RevitElement = {
    found: boolean;
    message: string | null;
    elementId: number | null;
    elementName: string | null;
    category: string | null;
    typeId: number | null;
    typeName: string | null;
    parameters: RevitParameter[];
    mappings: MappingLine[];
    configuration: string | null;
    mappingFiles: string[];
    mappingNote: string | null;
    useTypePropertiesInInstancePsets: boolean;
    /** The property set file the mappings come from (the file a draft replaces). */
    psetFile: string | null;
};

/** [supertype, is a type object, is abstract] by canonical entity name. */
export type EntityTree = Record<string, [string | null, boolean, boolean]>;

export type InspectorFocus = { propertySet?: string; name: string };

type InspectorState = {
    open: boolean;
    globalId: string | null;
    modelId: string | null;
    entity: AuditReportEntity | null;
    focus: InspectorFocus | null;
    ifc: IfcElement | null;
    ifcLoading: boolean;
    ifcError: string | null;
    revit: RevitElement | null;
    revitLoading: boolean;
    revitError: string | null;
    tree: EntityTree | null;
};

export const Inspector: InspectorState = $state({
    open: false,
    globalId: null,
    modelId: null,
    entity: null,
    focus: null,
    ifc: null,
    ifcLoading: false,
    ifcError: null,
    revit: null,
    revitLoading: false,
    revitError: null,
    tree: null
});

/** The attributes of the entity are shown as a set of this name; "Attribute Mapping" lines map to it. */
export const ATTRIBUTES = "Attributes";
const ATTRIBUTE_MAPPING = "Attribute Mapping";

export function isRevitAvailable(): boolean {
    return Revit.enabled && Revit.connected && Revit.capabilities.includes(ELEMENT_INSPECTOR);
}

function revitUnavailableReason(): string {
    if (!Revit.enabled || !Revit.connected) return "Connect to Revit to see the element's parameters and the property set mapping.";
    return "The connected IfcTester add-in cannot list element parameters. Update the add-in.";
}

const trees = new Map<string, Promise<EntityTree | null>>();

function entityTree(schema: string): Promise<EntityTree | null> {
    let tree = trees.get(schema);
    if (!tree) {
        // Without the tree only an exact class name matches a set's entity list
        tree = wasm.getEntityTree(schema).catch(() => null);
        trees.set(schema, tree);
    }
    return tree;
}

const message = (err: unknown) => (err instanceof Error ? err.message : String(err));

// Answers to an earlier click that arrive after a later one are dropped
let request = 0;

export async function inspect(entity: AuditReportEntity, modelId: string, focus: InspectorFocus | null = null) {
    const globalId = entity.global_id;
    if (!globalId || globalId === "-") return;

    const current = ++request;
    Object.assign(Inspector, {
        open: true,
        globalId,
        modelId,
        entity,
        focus,
        ifc: null,
        ifcError: null,
        ifcLoading: true,
        revit: null,
        revitError: null
    });

    const ifc = (async () => {
        try {
            const element = (await wasm.getElementProperties(modelId, globalId)) as IfcElement;
            const tree = await entityTree(element.schema);
            if (current !== request) return;
            Inspector.ifc = element;
            Inspector.tree = tree;
            if (!element.found) Inspector.ifcError = `No entity with GlobalId ${globalId} in the loaded IFC.`;
        } catch (err) {
            if (current === request) Inspector.ifcError = `The IFC could not be read: ${message(err)}`;
        } finally {
            if (current === request) Inspector.ifcLoading = false;
        }
    })();

    await Promise.all([ifc, loadRevit(current)]);
}

/** Asks Revit again, e.g. after the model or the property set file changed. */
export async function reloadRevit() {
    if (!Inspector.open || !Inspector.globalId) return;
    await loadRevit(request);
}

async function loadRevit(current: number) {
    if (!isRevitAvailable()) {
        Inspector.revitError = revitUnavailableReason();
        Inspector.revitLoading = false;
        return;
    }

    const tag = String(Inspector.entity?.tag ?? "").trim();
    Inspector.revitLoading = true;
    Inspector.revitError = null;
    try {
        const element = (await getElementParameters({
            globalId: Inspector.globalId as string,
            // Revit's exporter writes the element id as the Tag; it spares Revit a scan of the model
            ...(/^\d+$/.test(tag) ? { elementId: Number(tag) } : {}),
            ...resolveSettings(),
            // An unsaved edit of the property set file is read in place of the file
            ...PsetDraftModule.contentFor()
        })) as RevitElement;
        if (current !== request) return;
        Inspector.revit = element;
        if (!element.found) Inspector.revitError = element.message || "The element was not found in the Revit model.";
    } catch (err) {
        if (current === request) Inspector.revitError = `Revit could not list the parameters: ${message(err)}`;
    } finally {
        if (current === request) Inspector.revitLoading = false;
    }
}

export function close() {
    request++;
    Object.assign(Inspector, {
        open: false,
        globalId: null,
        modelId: null,
        entity: null,
        focus: null,
        ifc: null,
        ifcError: null,
        ifcLoading: false,
        revit: null,
        revitError: null,
        revitLoading: false
    });
}

// ---- Joining the two sides ----

/** The class and its supertypes, canonical names, the class itself first. */
export function lineage(ifcClass: string, tree: EntityTree | null): string[] {
    const chain: string[] = [];
    const canonical = tree ? Object.keys(tree).find((name) => name.toLowerCase() === ifcClass.toLowerCase()) : undefined;
    let current: string | null = canonical ?? ifcClass;
    while (current && !chain.includes(current)) {
        chain.push(current);
        current = tree?.[current]?.[0] ?? null;
    }
    return chain;
}

/** A set's entity name that matches a class of the lineage only ignoring case, and that class. */
export type CaseMismatch = { entity: string; canonical: string };

export type LineMatch = { applies: boolean; wrongCase: CaseMismatch | null };

/**
 * Whether a mapping line reaches an entity of this lineage. Revit's exporter matches the set's
 * entity names case-sensitively and skips a set silently on a case mismatch, so a name that only
 * matches ignoring case is reported instead of applied.
 */
export function matchLine(line: MappingLine, chain: string[]): LineMatch {
    // A mapping table line renames the parameter behind a property of any entity's common set
    if (line.origin === "mapping-table" || line.entities.length === 0) return { applies: true, wrongCase: null };

    let wrongCase: CaseMismatch | null = null;
    for (const entity of line.entities) {
        if (chain.includes(entity)) return { applies: true, wrongCase: null };
        const canonical = chain.find((name) => name.toLowerCase() === entity.toLowerCase());
        if (!wrongCase && canonical) wrongCase = { entity, canonical };
    }
    return { applies: false, wrongCase };
}

/**
 * Where a property stands, named by its cause on the Revit side: "no-parameter" (the mapped parameter
 * does not exist), "empty" (it exists without a value), "not-exported" (Revit has a value the IFC lacks).
 */
export type RowStatus = "exported" | "differs" | "unmapped" | "no-parameter" | "empty" | "not-exported" | "wrong-case";

export type InspectorRow = {
    key: string;
    propertySet: string;
    property: string;
    kind: "attribute" | "property" | "quantity";
    /** Values in the IFC, on the occurrence and on its type; undefined where the property is absent. */
    occurrenceValue: unknown;
    typeValue: unknown;
    inIfc: boolean;
    /** Mapping lines that reach this element, in file order: a repeated property is a fallback chain. */
    lines: MappingLine[];
    /** Lines that would reach it but for the case of an entity name. */
    wrongCase: ({ line: MappingLine } & CaseMismatch)[];
    status: RowStatus;
    note: string | null;
};

export function formatValue(value: unknown): string {
    if (value === undefined || value === null) return "";
    if (typeof value === "boolean") return value ? "TRUE" : "FALSE";
    if (Array.isArray(value)) return value.map(formatValue).join(", ");
    if (typeof value === "object") return JSON.stringify(value);
    return String(value);
}

const rowKey = (propertySet: string, property: string) => `${propertySet.toLowerCase()}|${property.toLowerCase()}`;

function statusOf(row: InspectorRow): { status: RowStatus; note: string | null } {
    const line = row.lines[0];
    const candidate = row.lines.flatMap((l) => l.candidates)[0];

    if (row.inIfc) {
        if (!line) return { status: "unmapped", note: null };
        const ifcValue = formatValue(row.occurrenceValue !== undefined ? row.occurrenceValue : row.typeValue).trim();
        if (candidate && candidate.storageType === "string" && candidate.value.trim() !== ifcValue) {
            return { status: "differs", note: "Revit holds another value than the IFC: the model has changed since this export, or another line or parameter won." };
        }
        return { status: "exported", note: null };
    }

    if (!line) {
        const mismatch = row.wrongCase[0];
        return {
            status: "wrong-case",
            note: `The set lists "${mismatch?.entity}". Revit's exporter matches entity names case-sensitively and skips the set silently; write it "${mismatch?.canonical}".`
        };
    }

    const wanted = line.builtInParameter ? `BuiltInParameter.${line.builtInParameter}` : line.parameterName ?? line.propertyName;
    if (!candidate) {
        return {
            status: "no-parameter",
            note: `The pset file reads "${wanted}", but the element and its type have no such parameter. Add it in Revit, or point the pset file at the parameter that holds the value.`
        };
    }
    if (row.lines.flatMap((l) => l.candidates).every((c) => !c.hasValue)) {
        return { status: "empty", note: `"${candidate.parameter}" (${candidate.scope}) has no value in Revit. Fill it in.` };
    }
    return {
        status: "not-exported",
        note: `"${candidate.parameter}" (${candidate.scope}) has a value in Revit but the IFC lacks the property: the IFC is older than the model, or it was exported with another property set file.`
    };
}

/**
 * One row per IFC property of the element and its type, and per mapping line that reaches the
 * element without a property in the IFC.
 */
export function buildRows(ifc: IfcElement | null, revit: RevitElement | null, tree: EntityTree | null): InspectorRow[] {
    const rows = new Map<string, InspectorRow>();

    const row = (propertySet: string, property: string, kind: InspectorRow["kind"]) => {
        const key = rowKey(propertySet, property);
        let existing = rows.get(key);
        if (!existing) {
            existing = {
                key,
                propertySet,
                property,
                kind,
                occurrenceValue: undefined,
                typeValue: undefined,
                inIfc: false,
                lines: [],
                wrongCase: [],
                status: "unmapped",
                note: null
            };
            rows.set(key, existing);
        }
        return existing;
    };

    const addSets = (sets: Record<string, Record<string, unknown>>, kind: InspectorRow["kind"], onType: boolean) => {
        for (const [setName, properties] of Object.entries(sets)) {
            for (const [name, value] of Object.entries(properties)) {
                const target = row(setName, name, kind);
                target.inIfc = true;
                if (onType) target.typeValue = value;
                else target.occurrenceValue = value;
            }
        }
    };

    if (ifc?.found) {
        for (const [name, value] of Object.entries(ifc.attributes)) {
            const target = row(ATTRIBUTES, name, "attribute");
            target.inIfc = true;
            target.occurrenceValue = value;
        }
        addSets(ifc.psets, "property", false);
        addSets(ifc.qtos, "quantity", false);
        if (ifc.type) {
            addSets(ifc.type.psets, "property", true);
            addSets(ifc.type.qtos, "quantity", true);
        }
    }

    if (revit?.found && ifc?.found) {
        const occurrence = lineage(ifc.ifcClass, tree);
        const type = ifc.type ? lineage(ifc.type.ifcClass, tree) : [];

        for (const line of revit.mappings) {
            const isAttribute = line.propertySet === ATTRIBUTE_MAPPING;
            const onOccurrence = matchLine(line, occurrence);
            const onType = type.length > 0 ? matchLine(line, type) : { applies: false, wrongCase: null };
            if (!onOccurrence.applies && !onType.applies && !onOccurrence.wrongCase && !onType.wrongCase) continue;

            const target = row(isAttribute ? ATTRIBUTES : line.propertySet, line.propertyName, isAttribute ? "attribute" : "property");
            if (onOccurrence.applies || onType.applies) target.lines.push(line);
            else target.wrongCase.push({ line, ...((onOccurrence.wrongCase ?? onType.wrongCase) as CaseMismatch) });
        }
    }

    const result = [...rows.values()];
    if (revit?.found) {
        for (const item of result) Object.assign(item, statusOf(item));
    }
    return result.sort(
        (a, b) =>
            Number(b.propertySet === ATTRIBUTES) - Number(a.propertySet === ATTRIBUTES) ||
            a.propertySet.localeCompare(b.propertySet) ||
            a.property.localeCompare(b.property)
    );
}

/** "scope|parameter" to the IFC properties whose first mapped candidate it is. */
export function parameterUsage(rows: InspectorRow[]): Map<string, string[]> {
    const usage = new Map<string, string[]>();
    for (const item of rows) {
        const candidate = item.lines.flatMap((l) => l.candidates)[0];
        if (!candidate) continue;
        const key = `${candidate.scope}|${candidate.parameter}`;
        const label = item.propertySet === ATTRIBUTES ? item.property : `${item.propertySet}.${item.property}`;
        usage.set(key, [...(usage.get(key) ?? []), label]);
    }
    return usage;
}

// ---- Linking: an IFC property to a Revit parameter, by editing the property set file ----

export type LinkPlan =
    | {
          ok: true;
          /** "remap" changes the parameter column of an existing line; "add" adds a line to a set. */
          kind: "remap" | "add";
          /** The line changed, or the line the new one gets; 1-based in the current draft or file. */
          line: number;
          header: number;
          setName: string;
          property: string;
          parameterRef: string;
          /** The parameter the line read before, for a remap. */
          previous: string | null;
          entities: string[];
          /** The data type of an added line. */
          dataType: string | null;
          /** The property lines of the set, 1-based: what a split copies. */
          blockLines: number[];
          /**
           * The element's own class (or its type's) as the set's header lists it, when the set lists
           * other classes too: the class that can get its own copy of the set.
           */
          splitEntity: string | null;
          /** Why the change cannot be kept to this element's class, when the set covers others. */
          splitBlocked: string | null;
          warnings: string[];
      }
    | { ok: false; reason: string };

const isTypeEntity = (entity: string) => /(Type|Style)$/i.test(entity) || /^IfcType/i.test(entity);

/** What linking the row to the parameter would change in the property set file, or why it cannot. */
export function planLink(row: InspectorRow, parameter: RevitParameter, revit: RevitElement, ifc: IfcElement | null, tree: EntityTree | null): LinkPlan {
    if (!revit.psetFile) return { ok: false, reason: "The export setup reads no property set file, so there is nothing to edit." };
    if (row.lines.length === 0 && row.wrongCase.length > 0) {
        return { ok: false, reason: "Fix the entity case of the set first; until then the exporter skips the whole set." };
    }

    const parameterRef = PsetEdit.parameterReference(parameter);
    const warnings: string[] = [];
    const setName = row.propertySet === ATTRIBUTES ? ATTRIBUTE_MAPPING : row.propertySet;
    let plan: Extract<LinkPlan, { ok: true }>;

    const line = row.lines[0];
    if (line) {
        if (line.origin !== "pset-file") {
            return { ok: false, reason: "This property is mapped in the parameter mapping table, which is not edited from here." };
        }
        if (!line.lineNumber) return { ok: false, reason: "The connected add-in does not report line numbers. Update the add-in." };
        const previous = line.builtInParameter ? `BuiltInParameter.${line.builtInParameter}` : line.parameterName;
        if (previous === parameterRef) return { ok: false, reason: `Line ${line.lineNumber} already reads ${parameterRef}.` };
        if (row.lines.length > 1) {
            warnings.push(`The file has ${row.lines.length} lines for this property (a fallback chain); the first, line ${line.lineNumber}, is changed.`);
        }
        plan = {
            ok: true,
            kind: "remap",
            line: line.lineNumber,
            header: line.headerLineNumber,
            setName,
            property: row.property,
            parameterRef,
            previous,
            entities: line.entities,
            dataType: null,
            blockLines: [],
            splitEntity: null,
            splitBlocked: null,
            warnings
        };
    } else {
        if (!ifc?.found) return { ok: false, reason: "The IFC entity is not loaded." };
        // A new line goes into a set of the same name that already reaches the element
        const occurrence = lineage(ifc.ifcClass, tree);
        const type = ifc.type ? lineage(ifc.type.ifcClass, tree) : [];
        const sets = revit.mappings.filter(
            (m) =>
                m.origin === "pset-file" &&
                m.propertySet === setName &&
                m.headerLineNumber > 0 &&
                (matchLine(m, occurrence).applies || (type.length > 0 && matchLine(m, type).applies))
        );
        if (sets.length === 0) {
            return { ok: false, reason: `The pset file has no set "${setName}" that reaches ${ifc.ifcClass}, so there is no set to add the line to.` };
        }
        const header = sets[0].headerLineNumber;
        const last = Math.max(...sets.filter((m) => m.headerLineNumber === header).map((m) => m.lineNumber));
        plan = {
            ok: true,
            kind: "add",
            line: last + 1,
            header,
            setName,
            property: row.property,
            parameterRef,
            previous: null,
            entities: sets[0].entities,
            dataType: PsetEdit.defaultDataType(parameter.storageType),
            blockLines: [],
            splitEntity: null,
            splitBlocked: null,
            warnings
        };
    }

    plan.blockLines = revit.mappings
        .filter((m) => m.origin === "pset-file" && m.headerLineNumber === plan.header && m.lineNumber > 0)
        .map((m) => m.lineNumber)
        .sort((a, b) => a - b);

    // A set that also covers other classes can give this element's class a copy of its own, but only
    // when the header names the class itself: a supertype (IfcElement) cannot leave one subclass out
    if (ifc?.found && (plan.entities.length > 1 || !plan.entities.includes(ifc.ifcClass))) {
        const own = [ifc.ifcClass, ifc.type?.ifcClass].filter((c): c is string => !!c);
        plan.splitEntity = plan.entities.length > 1 ? (plan.entities.find((e) => own.includes(e)) ?? null) : null;
        if (!plan.splitEntity) {
            const chains = [lineage(ifc.ifcClass, tree), ifc.type ? lineage(ifc.type.ifcClass, tree) : []];
            const via = plan.entities.find((e) => chains.some((chain) => chain.includes(e)));
            plan.splitBlocked = via
                ? `${ifc.ifcClass} is in the set through ${via}, which a set cannot leave one class out of, so the change applies to all of ${plan.entities.join(", ")}.`
                : null;
        }
    }

    if (parameter.scope === "type" && !plan.entities.some(isTypeEntity) && !revit.useTypePropertiesInInstancePsets) {
        warnings.push(
            'A type parameter in a set without type entities: the exporter reads it there only with the setup\'s "Use type properties in instance property sets" option, which is off.'
        );
    }
    if (!parameter.hasValue) warnings.push(`"${parameter.name}" has no value on this element.`);
    return plan;
}

/**
 * Links the row to the parameter in a draft of the property set file, then asks Revit again. With
 * scope "class" and a set that covers other classes too, the element's class leaves the set's
 * header and gets a copy of the whole set, with the change, right after it.
 */
export async function link(
    row: InspectorRow,
    parameter: RevitParameter,
    dataType?: string,
    scope: "class" | "set" = "set"
): Promise<PsetDraftModule.DraftChange> {
    const revit = Inspector.revit;
    if (!revit?.found) throw new Error("Revit has not answered for this element");
    const plan = planLink(row, parameter, revit, Inspector.ifc, Inspector.tree);
    if (!plan.ok) throw new Error(plan.reason);

    await PsetDraftModule.open(revit.psetFile as string);
    const label = `${plan.setName}.${plan.property} ← ${plan.parameterRef}`;
    const changed = "The pset file changed since Revit read it. Ask Revit again.";
    const change = PsetDraftModule.edit((text) => {
        // The line numbers came from Revit's reading of this same text; refuse if they no longer fit
        const header = text.lines[plan.header - 1] ?? "";
        if (!header.trimStart().startsWith("PropertySet:")) throw new Error(changed);
        const firstColumn = (n: number) => (text.lines[n - 1] ?? "").split("\t").find((c) => c.trim() !== "")?.trim();
        if (plan.kind === "remap" && firstColumn(plan.line) !== plan.property) throw new Error(changed);

        if (scope === "class" && plan.splitEntity) {
            const entity = plan.splitEntity;
            const copy = plan.blockLines.map((n) =>
                plan.kind === "remap" && n === plan.line ? PsetEdit.setParameterColumn(text.lines[n - 1], plan.parameterRef) : text.lines[n - 1]
            );
            if (plan.kind === "add") copy.push(PsetEdit.propertyLine(plan.property, dataType ?? plan.dataType ?? "Label", plan.parameterRef));
            const block = [
                "",
                `# ${plan.setName} for ${entity} only, split from the set on line ${plan.header} by IfcTester`,
                PsetEdit.withEntities(header, [entity]),
                ...copy
            ];
            const end = Math.max(plan.header, ...plan.blockLines);
            text.lines[plan.header - 1] = PsetEdit.removeEntity(header, entity);
            text.lines.splice(end, 0, ...block);
            return {
                line: end + 3,
                before: header,
                after: [text.lines[plan.header - 1], ...block.slice(1)].join("\n"),
                label: `${label}, only for ${entity} (the set is split)`
            };
        }

        if (plan.kind === "remap") {
            const before = text.lines[plan.line - 1] ?? "";
            const after = PsetEdit.setParameterColumn(before, plan.parameterRef);
            text.lines[plan.line - 1] = after;
            return { line: plan.line, before, after, label };
        }
        const after = PsetEdit.propertyLine(plan.property, dataType ?? plan.dataType ?? "Label", plan.parameterRef);
        text.lines.splice(plan.line - 1, 0, after);
        return { line: plan.line, before: null, after, label: `${label} (new line)` };
    });
    await reloadRevit();
    return change;
}

/** Rewrites the draft as one set per class, so each later link changes one class only. */
export async function splitSetsPerClass(): Promise<{ split: number; added: number }> {
    const revit = Inspector.revit;
    if (!revit?.found || !revit.psetFile) throw new Error("The export setup reads no property set file");

    await PsetDraftModule.open(revit.psetFile);
    let result = { split: 0, added: 0 };
    PsetDraftModule.edit((text) => {
        result = PsetEdit.oneSetPerEntity(text);
        if (result.split === 0) throw new Error("Every set already lists one class.");
        return {
            line: 1,
            before: null,
            after: `${result.split} sets that listed several classes are now ${result.split + result.added} sets, one per class.`,
            label: "One set per class"
        };
    });
    await reloadRevit();
    return result;
}

/** Writes the entity of a set header in the case the exporter needs (IFCWALL as IfcWall). */
export async function fixEntityCase(row: InspectorRow): Promise<PsetDraftModule.DraftChange> {
    const revit = Inspector.revit;
    const mismatch = row.wrongCase[0];
    if (!revit?.found || !revit.psetFile || !mismatch) throw new Error("Nothing to fix");
    if (!mismatch.line.headerLineNumber) throw new Error("The connected add-in does not report line numbers. Update the add-in.");

    await PsetDraftModule.open(revit.psetFile);
    const change = PsetDraftModule.edit((text) => {
        const line = mismatch.line.headerLineNumber;
        const before = text.lines[line - 1] ?? "";
        const after = PsetEdit.replaceEntity(before, mismatch.entity, mismatch.canonical);
        text.lines[line - 1] = after;
        return { line, before, after, label: `${mismatch.line.propertySet}: ${mismatch.entity} → ${mismatch.canonical}` };
    });
    await reloadRevit();
    return change;
}

// The inspector closes when the model it shows is unloaded
$effect.root(() => {
    $effect(() => {
        const modelId = Inspector.modelId;
        if (modelId !== null && !IFCModels.models.some((model) => model.id === modelId)) {
            close();
        }
    });
});
