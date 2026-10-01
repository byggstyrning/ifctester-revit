<script lang="ts">
    // Property set file and parameter mapping table for "Export Active View as IFC": shows the
    // setup's own files and lets the user replace them for the next export. The add-in applies
    // an override to a temporary copy of the setup only; the saved setup never changes.
    import { onMount } from "svelte";
    import { Revit, fileNameOf, getIfcConfigurationFiles, listPsetFiles } from "$src/modules/api/revit.svelte.js";

    type FolderFile = { name: string; path: string; modified: string };
    type ExportFiles = {
        psetFile: string | null;
        psetFileExists: boolean;
        parameterMappingFile: string | null;
        parameterMappingFileExists: boolean;
        warning: string | null;
    };
    type Kind = "psetFile" | "parameterMappingFile";

    let { configuration, disabled = false }: { configuration: string; disabled?: boolean } = $props();

    const DEFAULT_FOLDER = "\\\\bim-byggp1.hogerklick.bim\\H29\\BIM-tools\\pset";
    const FOLDER_KEY = "ifctester.revit.psetFolder";
    const TYPED = "__typed__";

    function storedFolder() {
        try {
            return localStorage.getItem(FOLDER_KEY) || DEFAULT_FOLDER;
        } catch {
            return DEFAULT_FOLDER;
        }
    }

    const message = (err: unknown) => (err instanceof Error ? err.message : String(err));

    let folder = $state(storedFolder());
    let files = $state<FolderFile[]>([]);
    let listing = $state(false);
    let listError = $state<string | null>(null);
    let setupFiles = $state<ExportFiles | null>(null);
    let setupError = $state<string | null>(null);

    async function loadFolder() {
        const dir = folder.trim();
        if (!dir) return;
        try {
            localStorage.setItem(FOLDER_KEY, dir);
        } catch {
            // Storage may be blocked; the folder then lasts for this page only
        }
        listing = true;
        listError = null;
        try {
            files = await listPsetFiles(dir);
        } catch (err) {
            files = [];
            listError = message(err);
        } finally {
            listing = false;
        }
    }

    onMount(loadFolder);

    // The setup's own files, to show as the default
    $effect(() => {
        const name = configuration;
        setupFiles = null;
        setupError = null;
        if (!name) return;
        let current = true;
        getIfcConfigurationFiles(name)
            .then((result: ExportFiles) => {
                if (current) setupFiles = result;
            })
            .catch((err: unknown) => {
                if (current) setupError = message(err);
            });
        return () => {
            current = false;
        };
    });

    function choice(kind: Kind) {
        const value = Revit.exportOverrides[kind];
        if (!value) return "";
        return files.some((file) => file.path === value) ? value : TYPED;
    }

    function pick(kind: Kind, value: string) {
        if (value !== TYPED) Revit.exportOverrides[kind] = value;
    }

    // The status of the last export with this setup: which file it read, or why it read none
    const lastExport = $derived(Revit.lastExport?.configuration === configuration ? Revit.lastExport : null);
    const exportedPset = $derived(lastExport?.files?.psetFile ?? null);
</script>

{#snippet setupDefault(path: string | null, exists: boolean, kindLabel: string)}
    {#if setupError}
        <p class="line muted">Setup's {kindLabel}: unknown ({setupError})</p>
    {:else if !setupFiles}
        <p class="line muted">Setup's {kindLabel}: reading...</p>
    {:else if !path}
        <p class="line muted">The setup names no {kindLabel}.</p>
    {:else}
        <p class="line" class:missing={!exists} title={path}>
            Setup's {kindLabel}: {fileNameOf(path)}{#if !exists}<span class="badge">not found</span>{/if}
        </p>
    {/if}
{/snippet}

{#snippet chooser(kind: Kind, label: string)}
    <select
        class="file-select"
        aria-label={label}
        value={choice(kind)}
        onchange={(e) => pick(kind, e.currentTarget.value)}
        {disabled}
    >
        <option value="">Use the setup's own</option>
        {#each files as file (file.path)}
            <option value={file.path}>{file.name}</option>
        {/each}
        {#if choice(kind) === TYPED}
            <option value={TYPED}>Path typed below</option>
        {/if}
    </select>
    <input
        class="path-input"
        type="text"
        spellcheck="false"
        placeholder="or type a full path"
        aria-label="{label} path"
        bind:value={Revit.exportOverrides[kind]}
        {disabled}
    />
{/snippet}

<div class="overrides">
    <div class="group">
        <span class="title">Property set file</span>
        {@render setupDefault(setupFiles?.psetFile ?? null, setupFiles?.psetFileExists ?? false, "property set file")}
        {@render chooser("psetFile", "Property set file")}
    </div>

    <details class="group">
        <summary class="title">
            Parameter mapping table{#if Revit.exportOverrides.parameterMappingFile}: {fileNameOf(Revit.exportOverrides.parameterMappingFile)}{/if}
        </summary>
        {@render setupDefault(setupFiles?.parameterMappingFile ?? null, setupFiles?.parameterMappingFileExists ?? false, "mapping table")}
        {@render chooser("parameterMappingFile", "Parameter mapping table")}
    </details>

    <details class="group">
        <summary class="title">Folder</summary>
        <div class="folder-row">
            <input
                class="path-input"
                type="text"
                spellcheck="false"
                aria-label="Property set folder"
                bind:value={folder}
                onkeydown={(e) => e.key === "Enter" && loadFolder()}
                {disabled}
            />
            <button type="button" class="list-btn" onclick={loadFolder} disabled={disabled || listing}>
                {listing ? "..." : "List"}
            </button>
        </div>
        {#if listError}
            <p class="line missing">{listError}</p>
        {:else if !listing}
            <p class="line muted">{files.length} .txt file{files.length === 1 ? "" : "s"} in the folder</p>
        {/if}
    </details>

    {#if lastExport && !Revit.exporting}
        {#if lastExport.warning}
            <p class="status missing" role="status">{lastExport.warning}</p>
        {:else if exportedPset}
            <p class="status" role="status" title={exportedPset}>
                Exported with {lastExport.files?.psetFileIsOverride ? "override" : "the setup's"} property set file {fileNameOf(exportedPset)}
            </p>
        {:else if lastExport.files}
            <p class="status" role="status">Exported without user-defined property sets</p>
        {/if}
    {/if}
</div>

<style>
    .overrides {
        display: flex;
        flex-direction: column;
        gap: 0.5rem;
        font-size: 0.75rem;
    }

    .group {
        display: flex;
        flex-direction: column;
        gap: 0.25rem;
    }

    .title {
        color: #ffffffb3;
        font-weight: 500;
        cursor: default;
    }

    summary.title {
        cursor: pointer;
    }

    details.group[open] > :global(*:not(summary)) {
        margin-top: 0.25rem;
    }

    .line,
    .status {
        margin: 0;
        color: #ffffffa6;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .status {
        white-space: normal;
    }

    .muted {
        color: #6b7280;
    }

    .missing {
        color: #f87171;
    }

    .badge {
        margin-left: 0.375rem;
        padding: 0 0.375rem;
        border-radius: 0.25rem;
        background: #ef444433;
        color: #fca5a5;
    }

    .file-select,
    .path-input {
        width: 100%;
        padding: 0.375rem 0.5rem;
        background: #ffffff0a;
        border: 1px solid #e5e7eb24;
        border-radius: 0.375rem;
        color: #ffffffd9;
        font-size: 0.75rem;
    }

    .file-select option {
        background: #1f2937;
        color: #ffffffd9;
    }

    .file-select:disabled,
    .path-input:disabled {
        opacity: 0.5;
    }

    .folder-row {
        display: flex;
        gap: 0.25rem;
    }

    .list-btn {
        padding: 0 0.625rem;
        background: #ffffff12;
        border: 1px solid #e5e7eb24;
        border-radius: 0.375rem;
        color: #ffffffd9;
        cursor: pointer;
    }

    .list-btn:disabled {
        opacity: 0.5;
        cursor: not-allowed;
    }
</style>
