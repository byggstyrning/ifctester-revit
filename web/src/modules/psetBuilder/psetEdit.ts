// Line edits of an existing Revit user-defined property set file. Every edit touches one line and
// leaves the rest of the file byte for byte as it was, so a project's pset file keeps its layout,
// comments and line endings. No UI, no Revit.

export type PsetText = {
    lines: string[];
    eol: "\r\n" | "\n";
    /** The text ended with a line break. */
    finalNewline: boolean;
};

export function splitText(text: string): PsetText {
    const eol = text.includes("\r\n") ? "\r\n" : "\n";
    const finalNewline = text.endsWith("\n");
    const lines = text.split(/\r?\n/);
    if (finalNewline) lines.pop();
    return { lines, eol, finalNewline };
}

export function joinText(text: PsetText): string {
    return text.lines.join(text.eol) + (text.finalNewline ? text.eol : "");
}

/** How a Revit parameter is named in a pset file: by its BuiltInParameter, which does not depend on Revit's language, else by name. */
export function parameterReference(parameter: { name: string; builtIn: string | null }): string {
    return parameter.builtIn ? `BuiltInParameter.${parameter.builtIn}` : parameter.name;
}

/** The text columns of a line and the tab runs around them: "\tA\t\tB" gives ["", "\t", "A", "\t\t", "B"]. */
const tokens = (line: string) => line.split(/(\t+)/);

/**
 * The property line with its Revit parameter column (the third column) replaced, or added when the
 * line has only a property name and a data type. The tabs between columns are kept.
 */
export function setParameterColumn(line: string, parameter: string): string {
    const parts = tokens(line);
    let column = 0;
    for (let i = 0; i < parts.length; i++) {
        if (i % 2 === 1 || parts[i].trim() === "") continue;
        column++;
        if (column === 3) {
            // Keep spaces or a stray character after the name, as the exporter trims them anyway
            const trailing = parts[i].match(/\s*$/)?.[0] ?? "";
            parts[i] = parameter + trailing;
            return parts.join("");
        }
    }
    if (column < 2) throw new Error(`Line is not a property line: ${JSON.stringify(line)}`);
    return `${line.replace(/\s*$/, "")}\t${parameter}`;
}

/** The text of the n-th (1-based) column of a line and its index in tokens(), or null. */
function column(parts: string[], n: number): number | null {
    let count = 0;
    for (let i = 0; i < parts.length; i++) {
        if (i % 2 === 1 || parts[i].trim() === "") continue;
        count++;
        if (count === n) return i;
    }
    return null;
}

/** The entity names a PropertySet: header lists (its fourth column), as written. */
export function headerEntities(header: string): string[] {
    const parts = tokens(header);
    const index = column(parts, 4);
    return index === null ? [] : parts[index].split(/[,; ]+/).filter(Boolean);
}

/** The header with its entity list replaced, keeping the list's separator style ("A, B" or "A,B"). */
export function withEntities(header: string, entities: string[]): string {
    const parts = tokens(header);
    const index = column(parts, 4);
    if (index === null || entities.length === 0) throw new Error(`Not a PropertySet: header with entities: ${JSON.stringify(header)}`);
    const trailing = parts[index].match(/\s*$/)?.[0] ?? "";
    const separator = /,\s/.test(parts[index]) || !parts[index].includes(",") ? ", " : ",";
    parts[index] = entities.join(separator) + trailing;
    return parts.join("");
}

/** The header without one of its entities. A header cannot lose its last entity. */
export function removeEntity(header: string, entity: string): string {
    const entities = headerEntities(header);
    if (!entities.includes(entity)) throw new Error(`"${entity}" is not in the header`);
    return withEntities(header, entities.filter((e) => e !== entity));
}

const isHeader = (line: string) => line.trimStart().startsWith("PropertySet:") && headerEntities(line).length > 0;

/**
 * Rewrites the file so that every set listing several entities becomes one set per entity, each
 * with a copy of the set's lines (comments included). An edit to one class's set then reaches no
 * other class. Lines before the first set and sets with one entity stay as they are.
 */
export function oneSetPerEntity(text: PsetText): { split: number; added: number } {
    const out: string[] = [];
    let split = 0;
    let added = 0;
    let i = 0;
    while (i < text.lines.length) {
        if (!isHeader(text.lines[i])) {
            out.push(text.lines[i++]);
            continue;
        }
        const header = text.lines[i++];
        const body: string[] = [];
        while (i < text.lines.length && !isHeader(text.lines[i])) body.push(text.lines[i++]);

        const entities = headerEntities(header);
        if (entities.length < 2) {
            out.push(header, ...body);
            continue;
        }
        // Blank lines before the next set stay after the last copy only
        let end = body.length;
        while (end > 0 && body[end - 1].trim() === "") end--;
        const lines = body.slice(0, end);
        const gap = body.slice(end);
        entities.forEach((entity, n) => {
            if (n > 0) out.push("");
            out.push(withEntities(header, [entity]), ...lines);
        });
        out.push(...gap);
        split++;
        added += entities.length - 1;
    }
    text.lines.splice(0, text.lines.length, ...out);
    return { split, added };
}

/** A new property line in the file's own layout: "\t<Property>\t<Data type>\t<Parameter>". */
export function propertyLine(property: string, dataType: string, parameter: string): string {
    return `\t${property}\t${dataType}\t${parameter}`;
}

/** The PropertySet: header with one entity name replaced, e.g. IFCWALL by IfcWall. */
export function replaceEntity(header: string, entity: string, replacement: string): string {
    const escaped = entity.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    const pattern = new RegExp(`(^|[\\t,; ])${escaped}(?=$|[\\t,; ])`);
    if (!pattern.test(header)) throw new Error(`"${entity}" is not in the header`);
    return header.replace(pattern, `$1${replacement}`);
}

/** The data type a new line gets for a Revit parameter of this storage type. */
export function defaultDataType(storageType: string): string {
    switch (storageType) {
        case "integer":
            return "Integer";
        case "double":
            return "Real";
        case "yesno":
            return "Boolean";
        default:
            return "Label";
    }
}

/** Data types offered for a new line; the exporter knows more, which can be typed in the file. */
export const DATA_TYPES = ["Label", "Text", "Identifier", "Integer", "Real", "Boolean", "Length", "PositiveLength", "Area", "Volume"];
