// Pset builder, the part without UI or network: IDS requirements to rows, rows to a Revit
// user-defined property set file, and that file back to rows. The format and the exporter's
// behaviour are in claude-kit library/formats/revit/pset.md:
//   PropertySet:<TAB><Pset><TAB>I|T<TAB><entities, comma-separated>
//   <TAB><Property><TAB><Data type><TAB><Revit parameter>
// Entity names must be exact CamelCase (the exporter's Enum.TryParse is case-sensitive and
// silently drops the set), so they always come from the IFC schema. The I/T column is ignored
// by the exporter: the entity list decides, so a type parameter goes in a set on type entities.
// A property repeated inside one set is a fallback chain.

import type { Facet, FacetValue, IdsDocument, Specification } from "$src/types/ids";

export type Scope = "instance" | "type";
export type SchemaName = "IFC2X3" | "IFC4";
export const SCHEMAS: SchemaName[] = ["IFC2X3", "IFC4"];

/** [supertype, is a type object, is abstract] per canonical entity name, from the worker's getEntityTree. */
export type EntityTree = Record<string, [string | null | undefined, boolean, boolean]>;

export type EntitySchema = {
    name: SchemaName;
    /** Upper-case name to canonical name, supertype and kind. */
    byUpper: Map<string, { name: string; supertype: string | null; isType: boolean; abstract: boolean }>;
};

/** What /pset-suggestions said about the parameter a row was given. */
export type RowSuggestion = {
    parameter: string | null;
    scope: Scope | null;
    origin: string | null;
    builtInParameter: string | null;
    storageType: string | null;
    readOnly: boolean;
    elementsWithParameter: number;
    elementsWithValue: number;
    elementsScanned: number;
    /** "entities" when only elements of the row's entities were scanned, "all" otherwise. */
    scanScope: string;
    note: string | null;
    /** The other matches, best first, as "Name (type)". */
    alternatives: string[];
};

export type PsetRow = {
    /** Stable identity: pset and property, plus a counter for a second mapping of the same property. */
    key: string;
    propertySet: string;
    name: string;
    dataType: string;
    /** Why the data type is not the IDS's own (none given, or not one the exporter knows). Null when it is. */
    dataTypeNote: string | null;
    /** The entity list written into the file, canonical names. */
    entities: string[];
    scope: Scope;
    /** Revit parameter names in fallback order; "BuiltInParameter.X" is allowed. Empty: unmapped. */
    parameters: string[];
    excluded: boolean;
    /** Names of the specifications that ask for it; empty for a row from an opened file. */
    specs: string[];
    /** The user changed the mapping, so "Suggest from model" leaves it alone. */
    edited: boolean;
    suggestion: RowSuggestion | null;
};

export type NotCovered = { spec: string; what: string; reason: string };

export type IdsRows = { rows: PsetRow[]; notCovered: NotCovered[]; ifcVersions: string[] };

/**
 * The data types the Revit exporter accepts in column 2 (Revit.IFC.Export PropertyType, read
 * from the 2025 exporter, 25.5.0.57). It parses them ignoring case and turns anything else into
 * Text without a word.
 */
export const EXPORTER_DATA_TYPES = [
    "Label", "Text", "Boolean", "Integer", "Real", "PositiveLength", "PositiveRatio", "PlaneAngle", "Area", "Identifier",
    "Count", "ThermodynamicTemperature", "Length", "Ratio", "ThermalTransmittance", "VolumetricFlowRate", "Logical",
    "Power", "ClassificationReference", "Frequency", "PositivePlaneAngle", "ElectricCurrent", "ElectricVoltage", "Volume",
    "LuminousFlux", "Force", "Pressure", "ColorTemperature", "Currency", "ElectricalEfficacy", "LuminousIntensity",
    "Illuminance", "NormalisedRatio", "LinearVelocity", "MassDensity", "Torque", "Mass", "SoundPower", "Time",
    "LocalTime", "Energy", "LinearForce", "PlanarForce", "Monetary", "ThermalConductivity", "RotationalFrequency",
    "AreaDensity", "Date", "MassFlowRate", "ElectricResistance", "MassPerLength", "SpecificHeatCapacity",
    "MolecularWeight", "HeatingValue", "IsothermalMoistureCapacity", "VaporPermeability", "MoistureDiffusivity",
    "DynamicViscosity", "ModulusOfElasticity", "ThermalExpansionCoefficient", "IonConcentration", "PH", "DateTime",
    "NonNegativeLength", "MomentOfInertia", "WarpingConstant", "SectionModulus", "Duration", "ElectricConductance",
    "TemperatureRateOfChange", "RadioActivity", "SoundPressure", "HeatFluxDensity", "ComplexNumber",
    "ThermalResistance", "Numeric", "ElectricCapacitance", "URIReference", "Acceleration", "SoundPowerLevel",
    "IntegerCountRate", "ElectricCharge", "Inductance", "AngularVelocity", "FrictionLoss", "LinearMoment",
    "LinearStiffness", "Luminance", "ThermalMass", "Efficacy"
] as const;

const DATA_TYPES_BY_UPPER = new Map(EXPORTER_DATA_TYPES.map((t) => [t.toUpperCase(), t as string]));

/** The exporter's spelling of a data type typed or read from a file, or null when it has none. */
export function exporterDataType(value: string): string | null {
    return DATA_TYPES_BY_UPPER.get(value.trim().toUpperCase()) ?? null;
}

/**
 * The exporter data type for an IDS dataType: IFCLABEL gives Label, IFCPOSITIVELENGTHMEASURE
 * PositiveLength. Without one, or with one the exporter does not know, Label and a note.
 */
export function dataTypeFromIds(idsType: unknown): { dataType: string; note: string | null } {
    const raw = typeof idsType === "string" ? idsType.trim().toUpperCase() : "";
    if (!raw) return { dataType: "Label", note: "The IDS gives no data type; Label assumed" };
    const bare = raw.replace(/^IFC/, "");
    const found = DATA_TYPES_BY_UPPER.get(bare) ?? DATA_TYPES_BY_UPPER.get(bare.replace(/MEASURE$/, ""));
    if (found) return { dataType: found, note: null };
    return { dataType: "Label", note: `The exporter has no data type for ${raw}; Label assumed` };
}

export function makeSchema(name: SchemaName, tree: EntityTree): EntitySchema {
    const byUpper: EntitySchema["byUpper"] = new Map();
    for (const [entity, [supertype, isType, abstract]] of Object.entries(tree)) {
        byUpper.set(entity.toUpperCase(), { name: entity, supertype: supertype ?? null, isType: Boolean(isType), abstract: Boolean(abstract) });
    }
    return { name, byUpper };
}

/** The canonical schema name of an entity, or null when the schema does not have it. */
export function canonicalEntity(entity: string, schema: EntitySchema | null): string | null {
    return schema?.byUpper.get(entity.trim().toUpperCase())?.name ?? null;
}

/** Without a schema the name ending tells: IfcWallType, IfcDoorStyle, IfcTypeObject. */
export function isTypeEntity(entity: string, schema: EntitySchema | null): boolean {
    const known = schema?.byUpper.get(entity.trim().toUpperCase());
    if (known) return known.isType;
    return /(type|style)$/i.test(entity.trim()) || /^ifctype/i.test(entity.trim());
}

/**
 * The type entity Revit writes a type's parameters on for an occurrence entity: IfcWall gives
 * IfcWallType, IfcDoor IfcDoorStyle in IFC2X3, IfcWallStandardCase IfcWallType. Null when the
 * schema has none (IfcBuildingStorey).
 */
export function typeEntityOf(entity: string, schema: EntitySchema | null): string | null {
    if (!schema) return null;
    const known = schema.byUpper.get(entity.trim().toUpperCase());
    if (!known) return null;
    if (known.isType) return known.name;
    const base = known.name.replace(/(StandardCase|ElementedCase)$/, "");
    for (const candidate of [`${base}Type`, `${base}Style`]) {
        const found = schema.byUpper.get(candidate.toUpperCase());
        if (found?.isType) return found.name;
    }
    return null;
}

/** The occurrence entity for a type entity: IfcWallType gives IfcWall, IfcDoorStyle IfcDoor. */
export function occurrenceEntityOf(entity: string, schema: EntitySchema | null): string | null {
    if (!schema) return null;
    const known = schema.byUpper.get(entity.trim().toUpperCase());
    if (!known) return null;
    if (!known.isType) return known.name;
    const base = known.name.replace(/(Type|Style)$/, "");
    const found = schema.byUpper.get(base.toUpperCase());
    return found && !found.isType ? found.name : null;
}

/** Moves a row between instance and type: the entity list follows (IfcWall and IfcWallType). */
export function switchScope(row: PsetRow, scope: Scope, schema: EntitySchema | null): void {
    if (row.scope === scope) return;
    row.scope = scope;
    row.entities = dedupe(row.entities.map((e) => (scope === "type" ? typeEntityOf(e, schema) : occurrenceEntityOf(e, schema)) ?? e));
}

/** What is wrong with a row's entity list in the schema, one line each. */
export function entityIssues(row: PsetRow, schema: EntitySchema | null): string[] {
    if (row.entities.length === 0) return ["No entities: the set would apply to nothing"];
    if (!schema) return [];
    const issues: string[] = [];
    for (const entity of row.entities) {
        const known = schema.byUpper.get(entity.trim().toUpperCase());
        if (!known) {
            issues.push(`${entity} is not an entity of ${schema.name}`);
        } else if (row.scope === "type" && !known.isType) {
            const type = typeEntityOf(entity, schema);
            issues.push(type ? `${known.name} is an occurrence: a type parameter needs ${type}` : `${known.name} is an occurrence and has no type entity`);
        } else if (row.scope === "instance" && known.isType) {
            issues.push(`${known.name} is a type entity: an instance parameter needs an occurrence entity`);
        }
    }
    return issues;
}

// ---- IDS to rows ---------------------------------------------------------------------------

function simpleValue(value: unknown): string | null {
    if (typeof value === "string") return value;
    if (typeof value === "number") return String(value);
    if (typeof value !== "object" || value === null) return null;
    const simple = (value as Record<string, unknown>).simpleValue;
    return typeof simple === "string" || typeof simple === "number" ? String(simple) : null;
}

function enumerationValues(value: unknown): string[] | null {
    if (typeof value !== "object" || value === null) return null;
    const record = value as Record<string, unknown>;
    const raw = record["xs:restriction"] ?? record.restriction;
    const restriction = (Array.isArray(raw) ? raw[0] : raw) as Record<string, unknown> | undefined;
    if (!restriction || typeof restriction !== "object") return null;
    const keys = Object.keys(restriction).filter((k) => !k.startsWith("@")).map((k) => k.replace(/^xs:/, ""));
    if (keys.some((k) => k !== "enumeration")) return null;
    const enumeration = restriction["xs:enumeration"] ?? restriction.enumeration;
    if (!Array.isArray(enumeration)) return null;
    return enumeration
        .map((item) => (item as Record<string, unknown> | null)?.["@value"])
        .filter((item): item is string | number => typeof item === "string" || typeof item === "number")
        .map(String);
}

function facets(clause: unknown, type: string): Facet[] {
    const list = (clause as Record<string, unknown> | undefined)?.[type];
    return Array.isArray(list) ? (list as Facet[]) : [];
}

function describeValue(value: unknown): string {
    const simple = simpleValue(value);
    if (simple !== null) return simple;
    const values = enumerationValues(value);
    if (values) return values.join(" | ");
    return "(restriction)";
}

const dedupe = (values: string[]) => [...new Set(values.filter((v) => v.trim() !== ""))];

export const rowKey = (propertySet: string, name: string) => `${propertySet}|${name}`;

const REQUIREMENT_REASONS: Record<string, string> = {
    entity: "The IFC class comes from Revit's category mapping or IfcExportAs, not from the pset file",
    attribute: "An attribute, not a property; set it with the IfcName / IfcDescription / IfcObjectType parameters or an Attribute Mapping set",
    classification: "A classification comes from the export's classification settings, not the pset file",
    material: "A material comes from the element's materials, not the pset file",
    partOf: "A relation comes from the model's structure, not the pset file"
};

/** The specification's ifcVersion list ("IFC2X3 IFC4" or an array). */
function specVersions(spec: Specification): string[] {
    const raw = spec["@ifcVersion"] as unknown;
    if (Array.isArray(raw)) return raw.map(String);
    if (typeof raw === "string") return raw.split(/\s+/).filter(Boolean);
    return [];
}

/**
 * One row per required property with a single property set and name, merged across
 * specifications; everything else goes to the "Not covered" list with the reason.
 */
export function rowsFromIds(doc: IdsDocument, schema: EntitySchema | null): IdsRows {
    const rows = new Map<string, PsetRow>();
    const notCovered: NotCovered[] = [];
    const versions = new Set<string>();

    (doc.specifications?.specification ?? []).forEach((spec, index) => {
        const specName = spec["@name"] || `Specification ${index + 1}`;
        for (const version of specVersions(spec)) versions.add(version);

        // A specification that forbids its elements asks for no properties on them
        const applicability = spec.applicability as Record<string, unknown> | undefined;
        if (applicability && Number(applicability["@maxOccurs"]) === 0) return;

        const entities: string[] = [];
        for (const facet of facets(applicability, "entity")) {
            const names = simpleValue(facet.name) !== null ? [simpleValue(facet.name) as string] : enumerationValues(facet.name);
            if (!names) {
                notCovered.push({ spec: specName, what: `Applicability: entity ${describeValue(facet.name)}`, reason: "A pattern on the entity name cannot become an entity list; add the entities to the rows by hand" });
                continue;
            }
            for (const name of names) entities.push(canonicalEntity(name, schema) ?? name);
            if (facet.predefinedType) {
                notCovered.push({
                    spec: specName,
                    what: `Applicability: predefined type ${describeValue(facet.predefinedType)}`,
                    reason: `The pset file cannot filter by predefined type; the set will apply to all ${names.map((n) => canonicalEntity(n, schema) ?? n).join(", ")}`
                });
            }
        }
        const otherApplicability = ["attribute", "property", "classification", "material", "partOf"].filter((t) => facets(applicability, t).length > 0);
        if (otherApplicability.length > 0) {
            notCovered.push({
                spec: specName,
                what: `Applicability: ${otherApplicability.join(", ")}`,
                reason: `The pset file can only select by entity; the set will apply to all elements of ${entities.length ? dedupe(entities).join(", ") : "the entities"}`
            });
        }

        const requirements = spec.requirements as Record<string, unknown> | undefined;
        for (const type of Object.keys(REQUIREMENT_REASONS)) {
            for (const facet of facets(requirements, type)) {
                const label = type === "attribute" ? `attribute ${describeValue(facet.name)}` : type === "material" ? "material" : type === "partOf" ? `part of ${describeValue((facet.entity as Facet | undefined)?.name ?? facet.relation)}` : type;
                notCovered.push({ spec: specName, what: `Requirement: ${label}`, reason: REQUIREMENT_REASONS[type] });
            }
        }

        const properties = facets(requirements, "property");
        if (properties.length > 0 && entities.length === 0) {
            notCovered.push({ spec: specName, what: "Applicability without an entity", reason: "Its properties got rows without entities; add the entities by hand" });
        }
        for (const facet of properties) {
            const pset = simpleValue(facet.propertySet);
            const name = simpleValue(facet.baseName);
            const label = `property ${describeValue(facet.propertySet)}.${describeValue(facet.baseName)}`;
            if (pset === null || name === null) {
                notCovered.push({ spec: specName, what: `Requirement: ${label}`, reason: "A restriction or pattern on the property set or property name cannot become one property line" });
                continue;
            }
            if (facet["@cardinality"] === "prohibited") {
                notCovered.push({ spec: specName, what: `Requirement: ${label}`, reason: "The property is prohibited: there is nothing to export" });
                continue;
            }
            const { dataType, note } = dataTypeFromIds(facet["@dataType"]);
            const key = rowKey(pset, name);
            const row = rows.get(key);
            if (!row) {
                rows.set(key, {
                    key,
                    propertySet: pset,
                    name,
                    dataType,
                    dataTypeNote: note,
                    entities: dedupe(entities),
                    scope: "instance",
                    parameters: [],
                    excluded: false,
                    specs: [specName],
                    edited: false,
                    suggestion: null
                });
                continue;
            }
            row.entities = dedupe([...row.entities, ...entities]);
            if (!row.specs.includes(specName)) row.specs.push(specName);
            if (row.dataTypeNote && !note) {
                row.dataType = dataType;
                row.dataTypeNote = null;
            } else if (!note && dataType !== row.dataType) {
                row.dataTypeNote = `The specifications disagree on the data type (${row.dataType}, ${dataType}); the first is used`;
            }
        }
    });

    const sorted = [...rows.values()].sort((a, b) => a.propertySet.localeCompare(b.propertySet) || 0);
    return { rows: sorted, notCovered, ifcVersions: [...versions] };
}

// ---- rows to file --------------------------------------------------------------------------

export type GenerateOptions = {
    title: string;
    version?: string;
    /** YYYY-MM-DD */
    date: string;
    schema: EntitySchema | null;
};

/** The canonical entity list a row is written with, deduplicated. */
export function fileEntities(row: PsetRow, schema: EntitySchema | null): string[] {
    return dedupe(row.entities.map((e) => canonicalEntity(e, schema) ?? e.trim()));
}

/**
 * The pset file text, CRLF line ends: one PropertySet block per (pset, instance/type, entity
 * list), rows in table order. An unmapped row is written as a comment so the gap shows in the
 * file; a row without entities cannot be written at all.
 */
export function generatePsetFile(rows: PsetRow[], options: GenerateOptions): string {
    const lines: string[] = [
        "# User Defined PropertySet Definition File",
        `# Generated from ${options.title || "untitled IDS"}${options.version ? ` (version ${options.version})` : ""} on ${options.date} by ifc-tester (pset builder), ${options.schema?.name ?? "IFC2X3"} entity names.`,
        "#",
        "# PropertySet:<TAB><Pset name><TAB>I|T<TAB><entities, comma-separated>",
        "#   <TAB><Property name><TAB><Data type><TAB><Revit parameter>",
        "# The entity list decides instance or type (IfcWall: the element's parameters, IfcWallType: its type's).",
        "# A property repeated inside a set is a fallback: the next line is read where the parameter is missing.",
        ""
    ];

    const blocks = new Map<string, { pset: string; scope: Scope; entities: string[]; rows: PsetRow[] }>();
    const skipped: PsetRow[] = [];
    for (const row of rows) {
        if (row.excluded) continue;
        const entities = fileEntities(row, options.schema);
        if (entities.length === 0) {
            skipped.push(row);
            continue;
        }
        const key = `${row.propertySet}\u0000${row.scope}\u0000${entities.join(",")}`;
        let block = blocks.get(key);
        if (!block) {
            block = { pset: row.propertySet, scope: row.scope, entities, rows: [] };
            blocks.set(key, block);
        }
        block.rows.push(row);
    }

    for (const block of blocks.values()) {
        lines.push(`PropertySet:\t${block.pset}\t${block.scope === "type" ? "T" : "I"}\t${block.entities.join(", ")}`);
        for (const row of block.rows) {
            const parameters = row.parameters.map((p) => p.trim()).filter(Boolean);
            if (parameters.length === 0) {
                lines.push(`#\t${row.name}\t${row.dataType}\t${UNMAPPED}`);
                continue;
            }
            for (const parameter of parameters) lines.push(`\t${row.name}\t${row.dataType}\t${parameter}`);
        }
        lines.push("");
    }

    if (skipped.length > 0) {
        lines.push("# Not written, no entities:");
        for (const row of skipped) lines.push(`#\t${row.propertySet}.${row.name}`);
        lines.push("");
    }
    return lines.join("\r\n");
}

/** Marks an unmapped property in a generated file, so opening the file brings the row back. */
export const UNMAPPED = "(no Revit parameter)";

// ---- file to rows --------------------------------------------------------------------------

export type ParsedProperty = {
    propertySet: string;
    name: string;
    dataType: string;
    entities: string[];
    /** Fallback chain in file order; empty for an unmapped marker line. */
    parameters: string[];
};

/**
 * Reads a user-defined property set file the way the exporter does (PropertyMap.LoadUserDefinedPset):
 * lines trimmed of leading spaces and tabs, '#' lines skipped, split on tabs with empty columns
 * dropped; a "PropertySet:" line needs four columns. Repeated property lines in a set become
 * one property with a fallback chain. Without a third column the parameter is the property name.
 */
export function parsePsetFile(text: string): ParsedProperty[] {
    const out: ParsedProperty[] = [];
    let block: { pset: string; entities: string[]; byName: Map<string, ParsedProperty> } | null = null;
    const unmapped = new RegExp(`^#\\t([^\\t]+)\\t([^\\t]+)\\t${UNMAPPED.replace(/[()]/g, "\\$&")}\\s*$`);

    for (const rawLine of text.split(/\r?\n/)) {
        const marker = rawLine.match(unmapped);
        if (marker && block) {
            const name = marker[1].trim();
            if (!block.byName.has(name.toUpperCase())) {
                const property = { propertySet: block.pset, name, dataType: marker[2].trim(), entities: block.entities, parameters: [] };
                block.byName.set(name.toUpperCase(), property);
                out.push(property);
            }
            continue;
        }
        const line = rawLine.replace(/^[ \t]+/, "");
        if (line.length === 0 || line[0] === "#") continue;
        const parts = line.split("\t").filter((p) => p.length > 0);
        if (parts.length >= 4 && parts[0].toLowerCase() === "propertyset:") {
            block = { pset: parts[1].trim(), entities: parts[3].split(/[,; ]+/).filter(Boolean), byName: new Map() };
            continue;
        }
        if (parts.length < 2 || !block) continue;
        const name = parts[0].trim();
        const parameter = parts.length >= 3 ? parts[2].trim() : name;
        const existing = block.byName.get(name.toUpperCase());
        if (existing) {
            existing.parameters.push(parameter || name);
            continue;
        }
        const property = { propertySet: block.pset, name, dataType: parts[1].trim(), entities: block.entities, parameters: [parameter || name] };
        block.byName.set(name.toUpperCase(), property);
        out.push(property);
    }
    return out;
}

/**
 * Puts an opened file's mappings into the rows: a row for the same pset and property takes the
 * file's parameters, entities and scope; a property the rows lack becomes a new row. A second
 * set mapping the same property (another entity list) becomes a row of its own.
 */
export function applyParsed(rows: PsetRow[], parsed: ParsedProperty[], schema: EntitySchema | null): { updated: number; added: number } {
    let updated = 0;
    let added = 0;
    const used = new Set<string>();
    for (const property of parsed) {
        const entities = dedupe(property.entities.map((e) => canonicalEntity(e, schema) ?? e));
        const scope: Scope = entities.length > 0 && entities.every((e) => isTypeEntity(e, schema)) ? "type" : "instance";
        const dataType = exporterDataType(property.dataType);
        const same = (row: PsetRow) =>
            row.propertySet.toLowerCase() === property.propertySet.toLowerCase() && row.name.toLowerCase() === property.name.toLowerCase();
        const target = rows.find((row) => same(row) && !used.has(row.key));
        if (target) {
            used.add(target.key);
            target.parameters = [...property.parameters];
            target.entities = entities;
            target.scope = scope;
            target.excluded = false;
            target.edited = true;
            target.suggestion = null;
            if (dataType) {
                target.dataType = dataType;
                target.dataTypeNote = null;
            }
            updated++;
            continue;
        }
        let key = rowKey(property.propertySet, property.name);
        for (let n = 2; rows.some((row) => row.key === key); n++) key = `${rowKey(property.propertySet, property.name)}|${n}`;
        used.add(key);
        rows.push({
            key,
            propertySet: property.propertySet,
            name: property.name,
            dataType: dataType ?? "Label",
            dataTypeNote: dataType ? null : `The file's data type ${property.dataType} is not one the exporter knows; Label assumed`,
            entities,
            scope,
            parameters: [...property.parameters],
            excluded: false,
            specs: [],
            edited: true,
            suggestion: null
        });
        added++;
    }
    return { updated, added };
}

// ---- suggestions and warnings --------------------------------------------------------------

export type SuggestionResult = {
    key?: string | null;
    scope: string;
    elementsScanned: number;
    note?: string | null;
    suggestions: {
        parameter: string;
        scope: Scope;
        rule: string;
        origin: string;
        builtInParameter?: string | null;
        storageType: string;
        readOnly: boolean;
        elementsWithParameter: number;
        elementsWithValue: number;
        elementsScanned: number;
    }[];
};

/** Takes the best suggestion into a row that the user has not edited; an empty answer unmaps it. */
export function applySuggestion(row: PsetRow, result: SuggestionResult, schema: EntitySchema | null): void {
    const [best, ...rest] = result.suggestions;
    row.suggestion = {
        parameter: best?.parameter ?? null,
        scope: best?.scope ?? null,
        origin: best?.origin ?? null,
        builtInParameter: best?.builtInParameter ?? null,
        storageType: best?.storageType ?? null,
        readOnly: best?.readOnly ?? false,
        elementsWithParameter: best?.elementsWithParameter ?? 0,
        elementsWithValue: best?.elementsWithValue ?? 0,
        elementsScanned: best?.elementsScanned ?? result.elementsScanned,
        scanScope: result.scope,
        note: result.note ?? null,
        alternatives: rest.map((s) => `${s.parameter} (${s.scope}, ${s.elementsWithParameter}/${s.elementsScanned})`)
    };
    if (row.edited) return;
    row.parameters = best ? [best.parameter] : [];
    if (best) switchScope(row, best.scope, schema);
}

/** A model parameter as GET /model-parameters lists it. */
export type ModelParameter = {
    name: string;
    scope: "instance" | "type" | "both";
    origin: string;
    builtInParameter?: string | null;
    storageType: string;
    dataType?: string;
    readOnly: boolean;
    instanceCount: number;
    typeCount: number;
    elementCount: number;
    withValueCount: number;
    categories: string[];
};

/** A parameter name as the exporter compares it: no spaces, any case. */
export const parameterKey = (name: string) => name.replace(/ /g, "").toUpperCase();

/** Below this share of scanned elements a suggestion is flagged. */
export const LOW_COVERAGE = 0.5;

/**
 * Why a row's mapping may not give the property in the IFC, one line each. A project parameter
 * is flagged but not called broken: the old rule was that they never export, yet Revit 2025
 * (25.5.0.57) and 2026 (26.5.0.55) exports wrote one from a user-defined set; older versions were
 * not tried.
 */
export function rowWarnings(row: PsetRow, modelParameters: ModelParameter[] | null): string[] {
    if (row.excluded) return [];
    const warnings: string[] = [];
    const parameters = row.parameters.map((p) => p.trim()).filter(Boolean);
    if (parameters.length === 0) return warnings;

    const suggested = row.suggestion?.parameter && parameterKey(row.suggestion.parameter) === parameterKey(parameters[0]) ? row.suggestion : null;
    for (const parameter of parameters) {
        if (/^BuiltInParameter\./i.test(parameter)) continue;
        const known = modelParameters?.filter((p) => parameterKey(p.name) === parameterKey(parameter)) ?? [];
        const origin = suggested && parameter === parameters[0] ? suggested.origin : known[0]?.origin;
        const readOnly = suggested && parameter === parameters[0] ? suggested.readOnly : known.length > 0 && known.every((p) => p.readOnly);
        if (origin === "project") warnings.push(`${parameter} is a project parameter: Revit 2025 and 2026 export these (checked); older versions were not checked, so verify the IFC there`);
        if (readOnly) warnings.push(`${parameter} is read-only in Revit: write-back cannot fix its values`);
        if (modelParameters && known.length === 0 && !suggested) warnings.push(`${parameter} is not a parameter of any model element`);
        if (known.length > 0 && !suggested) {
            const onType = known.some((p) => p.scope !== "instance");
            const onInstance = known.some((p) => p.scope !== "type");
            if (row.scope === "instance" && !onInstance) warnings.push(`${parameter} is a type parameter: switch the row to type (T)`);
            if (row.scope === "type" && !onType) warnings.push(`${parameter} is an instance parameter: switch the row to instance (I)`);
        }
    }
    if (suggested && suggested.elementsScanned > 0) {
        const share = suggested.elementsWithParameter / suggested.elementsScanned;
        if (share < LOW_COVERAGE) warnings.push(`Low coverage: ${suggested.elementsWithParameter} of ${suggested.elementsScanned} scanned elements have ${suggested.parameter}`);
        else if (suggested.elementsWithValue < suggested.elementsWithParameter) warnings.push(`${suggested.elementsWithParameter - suggested.elementsWithValue} of ${suggested.elementsWithParameter} elements have no value in ${suggested.parameter}`);
    }
    return warnings;
}

/** Text for a value used in descriptions; exported for the view. */
export function describeFacetValue(value: FacetValue | undefined): string {
    return describeValue(value);
}
