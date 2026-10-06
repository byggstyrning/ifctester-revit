<script lang="ts">
    // Pset builder: one row per property the IDS requires, mapped to a Revit parameter, written
    // as a Revit user-defined property set file. Works without Revit for drafting; suggestions,
    // the parameter list and saving next to Revit need an add-in that reports "pset-builder".
    import * as IDS from "$src/modules/api/ids.svelte";
    import {
        type BuilderState,
        PsetBuilder,
        canUseRevit,
        download,
        fileBaseName,
        generate,
        loadModelParameters,
        openBuilder,
        openPsetFile,
        persist,
        reloadFromIds,
        reset,
        save,
        schemaOf,
        setSchema,
        suggestFromModel
    } from "$src/modules/api/psetBuilder.svelte";
    import { Revit, fileNameOf } from "$src/modules/api/revit.svelte.js";
    import ParameterPicker from "$src/components/psetBuilder/ParameterPicker.svelte";
    import {
        EXPORTER_DATA_TYPES,
        type PsetRow,
        SCHEMAS,
        type SchemaName,
        entityIssues,
        exporterDataType,
        rowWarnings,
        switchScope
    } from "$src/modules/psetBuilder/psetFile";
    import { error as toastError, success } from "$src/modules/utils/toast.svelte";

    const docId = $derived(IDS.Module.activeDocument);
    const builder = $derived<BuilderState | null>(docId ? (PsetBuilder.builders[docId] ?? null) : null);
    const schema = $derived(builder ? schemaOf(builder) : null);
    const revit = $derived(canUseRevit());
    const preview = $derived(builder?.ready ? generate(builder) : "");

    $effect(() => {
        if (docId && !PsetBuilder.builders[docId]) openBuilder(docId);
    });

    // Keep the work per IDS as it changes
    $effect(() => {
        if (!builder?.ready) return;
        // Reading everything that is kept makes the effect run again when any of it changes
        const kept = JSON.stringify([builder.rows, builder.schemaName, builder.savePath]);
        if (kept) persist(builder);
    });

    // The parameter dropdown lists the model's parameters: read them once per IDS when Revit is there
    const parametersTried = new Set<string>();
    let parametersLoading = $state(false);
    $effect(() => {
        if (!revit || !docId || !builder?.ready || builder.modelParameters !== null || parametersTried.has(docId)) return;
        parametersTried.add(docId);
        refreshParameters();
    });

    const groups = $derived.by(() => {
        const map = new Map<string, PsetRow[]>();
        for (const row of builder?.rows ?? []) {
            const list = map.get(row.propertySet) ?? [];
            list.push(row);
            map.set(row.propertySet, list);
        }
        return [...map.entries()];
    });

    const counts = $derived.by(() => {
        const rows = (builder?.rows ?? []).filter((row) => !row.excluded);
        return { total: rows.length, unmapped: rows.filter((row) => row.parameters.every((p) => !p.trim())).length };
    });

    const schemaNotInIds = $derived(
        builder && builder.ifcVersions.length > 0 && !builder.ifcVersions.some((v) => v.toUpperCase() === builder.schemaName)
    );

    function edited(row: PsetRow) {
        row.edited = true;
    }

    function setParameter(row: PsetRow, index: number, value: string) {
        row.parameters[index] = value;
        edited(row);
    }

    function addFallback(row: PsetRow) {
        row.parameters.push("");
        edited(row);
    }

    function removeParameter(row: PsetRow, index: number) {
        row.parameters.splice(index, 1);
        edited(row);
    }

    function toggleScope(row: PsetRow) {
        switchScope(row, row.scope === "instance" ? "type" : "instance", schema);
        edited(row);
    }

    function setEntities(row: PsetRow, text: string) {
        row.entities = [...new Set(text.split(/[,;\s]+/).map((e) => e.trim()).filter(Boolean))];
        edited(row);
    }

    function setDataType(row: PsetRow, value: string) {
        row.dataType = exporterDataType(value) ?? value;
        row.dataTypeNote = null;
    }

    function coverage(row: PsetRow) {
        const s = row.suggestion;
        if (!s || !s.parameter || !row.parameters[0] || s.parameter !== row.parameters[0]) return "";
        return `${s.elementsWithParameter}/${s.elementsScanned}${s.elementsWithValue < s.elementsWithParameter ? ` (${s.elementsWithValue} with value)` : ""}`;
    }

    async function suggest() {
        if (!builder) return;
        await suggestFromModel(builder);
        if (!builder.error) success(`Suggestions for ${counts.total} properties; ${counts.unmapped} unmapped`);
    }

    async function refreshParameters() {
        if (!builder || parametersLoading) return;
        parametersLoading = true;
        try {
            await loadModelParameters(builder);
        } catch (err) {
            builder.error = `The model's parameters could not be read: ${err instanceof Error ? err.message : String(err)}`;
        } finally {
            parametersLoading = false;
        }
    }

    async function saveFile(thenUse: boolean, overwrite = false) {
        if (!builder) return;
        const path = await save(builder, overwrite ? { overwrite, thenUse } : { thenUse });
        if (!path) return;
        success(thenUse ? `Saved ${fileNameOf(path)}; the next Export IFC uses it` : `Saved ${path}`);
    }

    async function openFile() {
        if (!builder) return;
        const result = await openPsetFile(builder);
        if (result) success(`Pset file read: ${result.updated} rows updated, ${result.added} added`);
        else if (builder.error) toastError(builder.error);
    }

    async function changeSchema(name: string) {
        if (builder) await setSchema(builder, name as SchemaName);
    }
</script>

{#if !builder || !builder.ready}
    <p class="muted pad">Reading the IDS...</p>
{:else}
    <div class="pset-builder">
        <div class="bar">
            <h2>Pset builder</h2>
            <label class="field">
                <span>IFC schema</span>
                <select aria-label="IFC schema" value={builder.schemaName} onchange={(e) => changeSchema(e.currentTarget.value)}>
                    {#each SCHEMAS as name (name)}
                        <option value={name}>{name}</option>
                    {/each}
                </select>
            </label>
            <span class="muted small">
                IDS: {builder.ifcVersions.length ? builder.ifcVersions.join(", ") : "no ifcVersion"}
                {#if schemaNotInIds}<span class="warn">· the IDS does not name {builder.schemaName}</span>{/if}
            </span>
            <span class="spacer"></span>
            {#if revit}
                <button class="btn" onclick={suggest} disabled={builder.busy !== ""}>{builder.busy === "suggest" ? "Suggesting..." : "Suggest from model"}</button>
            {/if}
            <button class="btn" onclick={openFile}>Open pset file...</button>
            <button class="btn" onclick={() => builder && reloadFromIds(builder)} title="Read the IDS again after editing it; mappings are kept">Reload IDS</button>
            <button class="btn subtle" onclick={() => builder && confirm("Forget the mappings for this IDS and start again?") && reset(builder)}>Reset</button>
        </div>

        <p class="status small">
            {counts.total} properties, <span class:warn={counts.unmapped > 0}>{counts.unmapped} unmapped</span>.
            {#if revit}
                {#if builder.suggestedAt}Suggested from the model at {builder.suggestedAt}.{/if}
                {#if builder.modelSummary}<span class="muted">{builder.modelSummary}</span>{/if}
            {:else}
                <span class="muted">Drafting: suggestions and saving next to Revit need the IfcTester add-in with the pset builder, connected.</span>
            {/if}
        </p>
        {#if builder.scopeMethod}<p class="muted small">{builder.scopeMethod}</p>{/if}
        {#if builder.error}<p class="error small" role="alert">{builder.error}</p>{/if}

        {#if builder.notCovered.length > 0}
            <details class="not-covered">
                <summary>Not covered ({builder.notCovered.length})</summary>
                <ul>
                    {#each builder.notCovered as item, i (i)}
                        <li><b>{item.spec}</b> · {item.what}: <span class="muted">{item.reason}</span></li>
                    {/each}
                </ul>
            </details>
        {/if}

        <datalist id="pset-builder-datatypes">
            {#each EXPORTER_DATA_TYPES as t (t)}<option value={t}></option>{/each}
        </datalist>

        <div class="table-wrap scrollbar">
            <table class="rows">
                <thead>
                    <tr>
                        <th title="Write this property">Use</th>
                        <th>Property</th>
                        <th>Data type</th>
                        <th title="Instance or type: decides the entity list">I/T</th>
                        <th>Entities</th>
                        <th>Revit parameter (fallbacks below)</th>
                        <th>Coverage</th>
                        <th></th>
                    </tr>
                </thead>
                {#each groups as [pset, rows] (pset)}
                    <tbody>
                        <tr class="group"><td colspan="8">{pset} <span class="muted">({rows.length})</span></td></tr>
                        {#each rows as row (row.key)}
                            {@const issues = entityIssues(row, schema)}
                            {@const warnings = rowWarnings(row, builder.modelParameters)}
                            {@const unmapped = !row.excluded && row.parameters.every((p) => !p.trim())}
                            <tr class:excluded={row.excluded} class:unmapped data-row={row.key}>
                                <td><input type="checkbox" aria-label="Use {row.name}" checked={!row.excluded} onchange={(e) => (row.excluded = !e.currentTarget.checked)} /></td>
                                <td class="prop" title={row.specs.length ? `Asked for by: ${row.specs.join(", ")}` : "From an opened pset file"}>
                                    {row.name}
                                    {#if row.specs.length === 0}<span class="tag">file</span>{/if}
                                </td>
                                <td>
                                    <input
                                        class="dt"
                                        class:flag={row.dataTypeNote || !exporterDataType(row.dataType)}
                                        list="pset-builder-datatypes"
                                        aria-label="Data type of {row.name}"
                                        value={row.dataType}
                                        title={row.dataTypeNote ?? (exporterDataType(row.dataType) ? "" : "The exporter does not know this data type and writes Text")}
                                        onchange={(e) => setDataType(row, e.currentTarget.value)}
                                    />
                                </td>
                                <td>
                                    <button class="scope" aria-label="Scope of {row.name}" title={row.scope === "type" ? "Type parameter: set on type entities" : "Instance parameter: set on occurrence entities"} onclick={() => toggleScope(row)}>
                                        {row.scope === "type" ? "T" : "I"}
                                    </button>
                                </td>
                                <td>
                                    <input
                                        class="entities"
                                        class:flag={issues.length > 0}
                                        aria-label="Entities of {row.name}"
                                        value={row.entities.join(", ")}
                                        title={issues.join("\n")}
                                        onchange={(e) => setEntities(row, e.currentTarget.value)}
                                    />
                                </td>
                                <td class="params">
                                    {#each row.parameters.length ? row.parameters : [""] as parameter, index (index)}
                                        <div class="param">
                                            <ParameterPicker
                                                parameters={builder.modelParameters}
                                                label={index === 0 ? `Revit parameter of ${row.name}` : `Fallback ${index} of ${row.name}`}
                                                pickLabel={index === 0 ? `Model parameters for ${row.name}` : `Model parameters for ${row.name}, fallback ${index}`}
                                                placeholder={index === 0
                                                    ? builder.modelParameters ? "unmapped: pick or type a name" : "unmapped: type a name or BuiltInParameter.X"
                                                    : "fallback"}
                                                value={parameter}
                                                onchange={(value) => setParameter(row, index, value)}
                                            />
                                            {#if row.parameters.length > 1 || (index === 0 && parameter)}
                                                <button class="icon" aria-label="Remove {parameter || 'parameter'}" onclick={() => removeParameter(row, index)}>×</button>
                                            {/if}
                                        </div>
                                    {/each}
                                    {#if row.parameters.some((p) => p.trim())}
                                        <button class="link" onclick={() => addFallback(row)}>+ fallback</button>
                                    {/if}
                                </td>
                                <td class="small" title={row.suggestion ? [row.suggestion.scanScope === "all" ? "Scanned every model element" : "Scanned the row's entities", row.suggestion.note, row.suggestion.alternatives.length ? `Also: ${row.suggestion.alternatives.join("; ")}` : ""].filter(Boolean).join("\n") : ""}>
                                    {coverage(row)}
                                    {#if row.suggestion && !row.suggestion.parameter}<span class="muted">no match</span>{/if}
                                </td>
                                <td>
                                    {#if warnings.length > 0}
                                        <span class="warn-icon" role="img" aria-label="Warnings for {row.name}" title={warnings.join("\n")}>⚠</span>
                                    {/if}
                                </td>
                            </tr>
                        {/each}
                    </tbody>
                {/each}
            </table>
            {#if builder.rows.length === 0}
                <p class="muted pad">The IDS requires no property with a single property set and name. Open a pset file to edit one.</p>
            {/if}
        </div>

        <div class="output">
            <div class="save-row">
                {#if revit}
                    <input
                        class="path"
                        aria-label="Save path"
                        spellcheck="false"
                        placeholder={`%LOCALAPPDATA%\\IfcTesterRevit\\psets\\${fileBaseName(builder)}`}
                        bind:value={builder.savePath}
                    />
                    <button class="btn" onclick={() => saveFile(false)} disabled={builder.busy !== ""}>Save</button>
                    <button class="btn" onclick={() => saveFile(true)} disabled={builder.busy !== ""} title="Save, then use it as the property set file of the next Export IFC">Use for next export</button>
                {/if}
                <button class="btn" onclick={() => builder && download(builder)}>Download</button>
                {#if Revit.exportOverrides.psetFile && Revit.exportOverrides.psetFile === builder.lastSavedPath}
                    <span class="ok small">Next export uses {fileNameOf(builder.lastSavedPath)}</span>
                {:else if builder.lastSavedPath}
                    <span class="muted small" title={builder.lastSavedPath}>Saved {builder.lastSavedPath}</span>
                {/if}
            </div>
            {#if builder.overwritePending}
                <p class="warn small" role="alert">
                    {builder.overwritePending.path} already exists.
                    <button class="btn" onclick={() => builder?.overwritePending && saveFile(builder.overwritePending.thenUse, true)}>Overwrite</button>
                    <button class="btn subtle" onclick={() => builder && (builder.overwritePending = null)}>Cancel</button>
                </p>
            {/if}
            {#if revit}
                <button class="link small" onclick={refreshParameters} disabled={parametersLoading} title="Read the parameter list again after changing the model">
                    {parametersLoading ? "Reading the model's parameters..." : builder.modelParameters ? "Reload the model's parameters" : "Load the model's parameters for the dropdown"}
                </button>
            {/if}
            <details open>
                <summary>Preview</summary>
                <pre class="preview scrollbar" aria-label="Pset file preview">{preview}</pre>
            </details>
        </div>
    </div>
{/if}

<style>
    .pset-builder {
        display: flex;
        flex-direction: column;
        gap: 0.5rem;
        font-size: 0.8125rem;
    }

    .bar {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: 0.5rem;
    }

    .bar h2 {
        margin: 0 0.5rem 0 0;
        font-size: 1.125rem;
    }

    .spacer {
        flex: 1;
    }

    .field {
        display: flex;
        align-items: center;
        gap: 0.375rem;
    }

    .pad {
        padding: 1rem;
    }

    .small {
        font-size: 0.75rem;
    }

    .muted {
        color: #9ca3af;
    }

    .warn {
        color: #fbbf24;
    }

    .error {
        color: #f87171;
    }

    .ok {
        color: #34d399;
    }

    .status {
        margin: 0;
    }

    p {
        margin: 0;
    }

    select,
    input {
        padding: 0.25rem 0.375rem;
        background: #ffffff0a;
        border: 1px solid #e5e7eb24;
        border-radius: 0.25rem;
        color: #ffffffd9;
        font-size: 0.75rem;
    }

    select option {
        background: #1f2937;
    }

    input.flag {
        border-color: #f87171;
    }

    .btn.subtle {
        opacity: 0.7;
    }

    .not-covered ul {
        margin: 0.25rem 0 0;
        padding-left: 1.25rem;
        font-size: 0.75rem;
    }

    .table-wrap {
        overflow: auto;
        max-height: 55vh;
        border: 1px solid #e5e7eb1f;
        border-radius: 0.375rem;
    }

    table.rows {
        width: 100%;
        border-collapse: collapse;
    }

    th {
        position: sticky;
        top: 0;
        z-index: 1;
        background: #2b2b2b;
        text-align: left;
        font-weight: 500;
        color: #ffffffb3;
        padding: 0.375rem;
        font-size: 0.75rem;
    }

    td {
        padding: 0.25rem 0.375rem;
        border-top: 1px solid #e5e7eb14;
        vertical-align: top;
    }

    tr.group td {
        background: #ffffff0d;
        font-weight: 600;
    }

    tr.unmapped td {
        background: #ef44441f;
    }

    tr.unmapped .params {
        --picker-border: #f87171;
    }

    tr.excluded td {
        opacity: 0.45;
    }

    .prop {
        white-space: nowrap;
    }

    .tag {
        margin-left: 0.25rem;
        padding: 0 0.25rem;
        border-radius: 0.25rem;
        background: #3b82f633;
        font-size: 0.6875rem;
    }

    .dt {
        width: 8.5rem;
    }

    .entities {
        width: 16rem;
    }

    .params {
        min-width: 15rem;
    }

    .param {
        display: flex;
        gap: 0.25rem;
        margin-bottom: 0.125rem;
    }


    .scope {
        width: 1.75rem;
        padding: 0.125rem 0;
        background: #ffffff12;
        border: 1px solid #e5e7eb24;
        border-radius: 0.25rem;
        color: #ffffffd9;
        cursor: pointer;
    }

    .icon,
    .link {
        background: none;
        border: none;
        color: #93c5fd;
        cursor: pointer;
        padding: 0 0.25rem;
    }

    .warn-icon {
        color: #fbbf24;
        cursor: help;
    }

    .output {
        display: flex;
        flex-direction: column;
        gap: 0.375rem;
    }

    .save-row {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: 0.375rem;
    }

    .path {
        flex: 1;
        min-width: 16rem;
    }

    .preview {
        margin: 0.25rem 0 0;
        max-height: 40vh;
        overflow: auto;
        padding: 0.5rem;
        background: #00000040;
        border-radius: 0.375rem;
        font-size: 0.75rem;
        tab-size: 12;
        white-space: pre;
    }
</style>
