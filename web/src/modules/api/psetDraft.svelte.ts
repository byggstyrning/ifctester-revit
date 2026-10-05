import { Revit, fileNameOf, readPsetFile, savePsetFile } from "./revit.svelte.js";
import { joinText, splitText, type PsetText } from "$src/modules/psetBuilder/psetEdit";

// An edited copy of the export setup's property set file, made from the element inspector. It lives
// on the page until it is saved on the Revit machine; the inspector asks Revit to read it in place of
// the file, so the effect of an edit shows before anything is saved.

export type DraftChange = {
    /** 1-based line in the draft after the change. */
    line: number;
    /** The line before the change; null for an added line. */
    before: string | null;
    after: string;
    label: string;
};

type DraftState = {
    /** The file the draft was made from. */
    file: string | null;
    bom: boolean;
    text: string | null;
    history: { text: string; change: DraftChange }[];
    loading: boolean;
    saving: boolean;
    error: string | null;
    /** Where the draft was last saved; cleared by the next edit. */
    savedPath: string | null;
};

export const PsetDraft: DraftState = $state({
    file: null,
    bom: false,
    text: null,
    history: [],
    loading: false,
    saving: false,
    error: null,
    savedPath: null
});

export const hasChanges = () => PsetDraft.history.length > 0;

/** The content Revit should read in place of the file, when there is a draft of it. */
export function contentFor(): { psetFileContent: string; psetFileContentFor: string } | Record<string, never> {
    if (PsetDraft.text === null || PsetDraft.file === null) return {};
    return { psetFileContent: PsetDraft.text, psetFileContentFor: PsetDraft.file };
}

const message = (err: unknown) => (err instanceof Error ? err.message : String(err));

/** Starts a draft of the file, unless one of it is open. Throws when another file has unsaved edits. */
export async function open(file: string): Promise<void> {
    if (PsetDraft.file === file && PsetDraft.text !== null) return;
    if (hasChanges()) {
        throw new Error(`Save or discard the edits to ${fileNameOf(PsetDraft.file)} first.`);
    }
    PsetDraft.loading = true;
    PsetDraft.error = null;
    try {
        const read = await readPsetFile(file);
        Object.assign(PsetDraft, { file, bom: read.bom, text: read.content, history: [], savedPath: null });
    } catch (err) {
        PsetDraft.error = message(err);
        throw err;
    } finally {
        PsetDraft.loading = false;
    }
}

/** Applies one line edit to the draft. */
export function edit(change: (text: PsetText) => DraftChange): DraftChange {
    if (PsetDraft.text === null) throw new Error("No property set file is open for editing");
    const text = splitText(PsetDraft.text);
    const done = change(text);
    PsetDraft.history = [...PsetDraft.history, { text: PsetDraft.text, change: done }];
    PsetDraft.text = joinText(text);
    PsetDraft.savedPath = null;
    return done;
}

export function undo() {
    const last = PsetDraft.history.at(-1);
    if (!last) return;
    PsetDraft.history = PsetDraft.history.slice(0, -1);
    PsetDraft.text = last.text;
    PsetDraft.savedPath = null;
}

export function close() {
    Object.assign(PsetDraft, { file: null, bom: false, text: null, history: [], error: null, savedPath: null });
}

/** Next to the original, with "-edited" added to its name. */
export function suggestedPath(): string {
    const file = PsetDraft.file ?? "";
    const match = file.match(/^(.*[\\/])?([^\\/]*?)(\.txt)?$/i);
    const folder = match?.[1] ?? "";
    const base = (match?.[2] ?? "pset").replace(/-edited$/i, "");
    return `${folder}${base}-edited.txt`;
}

export type SaveOutcome = { saved: true; path: string } | { saved: false; exists: boolean; message: string };

/**
 * Writes the draft on the Revit machine (UTF-8, with a byte order mark only if the original had one).
 * With thenUse, the next export from the toolbar reads it.
 */
export async function save(path: string, options: { overwrite?: boolean; thenUse?: boolean } = {}): Promise<SaveOutcome> {
    if (PsetDraft.text === null) return { saved: false, exists: false, message: "Nothing to save" };
    PsetDraft.saving = true;
    PsetDraft.error = null;
    try {
        const result = await savePsetFile({
            path,
            content: (PsetDraft.bom ? "﻿" : "") + PsetDraft.text,
            overwrite: options.overwrite === true
        });
        if (options.thenUse) Revit.exportOverrides.psetFile = result.path;
        // The saved text is the new starting point; Revit keeps reading it in place of the original
        PsetDraft.history = [];
        PsetDraft.savedPath = result.path;
        return { saved: true, path: result.path };
    } catch (err) {
        const exists = (err as { status?: number }).status === 409;
        if (!exists) PsetDraft.error = message(err);
        return { saved: false, exists, message: message(err) };
    } finally {
        PsetDraft.saving = false;
    }
}
