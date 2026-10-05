<script lang="ts">
    import { tick } from "svelte";
    import * as Inspector from "$src/modules/api/inspector.svelte";
    import type { InspectorRow, MappingLine, RevitParameter, RowStatus } from "$src/modules/api/inspector.svelte";
    import * as Draft from "$src/modules/api/psetDraft.svelte";
    import { DATA_TYPES } from "$src/modules/psetBuilder/psetEdit";
    import { Revit, fileNameOf, selectElement } from "$src/modules/api/revit.svelte.js";
    import { error, success } from "$src/modules/utils/toast.svelte";

    const view = Inspector.Inspector;
    const draft = Draft.PsetDraft;

    let filter = $state("");
    let problemsOnly = $state(false);
    let mappedOnly = $state(false);

    const rows = $derived(Inspector.buildRows(view.ifc, view.revit, view.tree));
    const usage = $derived(Inspector.parameterUsage(rows));
    const ifcElement = $derived(view.ifc?.found ? view.ifc : null);
    const revitElement = $derived(view.revit?.found ? view.revit : null);

    const STATUS_LABEL: Record<RowStatus, string> = {
        exported: "Exported",
        differs: "Differs",
        unmapped: "Not in pset file",
        "no-parameter": "No parameter in Revit",
        empty: "Empty in Revit",
        "not-exported": "Not exported",
        "wrong-case": "Entity case"
    };
    const PROBLEMS: RowStatus[] = ["differs", "no-parameter", "empty", "not-exported", "wrong-case"];

    const paramKey = (scope: string, name: string) => `${scope}|${name}`;

    /** The Revit parameters an IFC row reads, as parameter keys. */
    const rowParams = (row: InspectorRow) => row.lines.flatMap((l) => l.candidates).map((c) => paramKey(c.scope, c.parameter));

    /** Parameter key to the keys of the IFC rows it feeds. */
    const paramRows = $derived.by(() => {
        const result = new Map<string, string[]>();
        for (const row of rows) {
            for (const key of rowParams(row)) result.set(key, [...(result.get(key) ?? []), row.key]);
        }
        return result;
    });

    const needle = $derived(filter.trim().toLowerCase());

    function rowMatches(row: InspectorRow): boolean {
        if (problemsOnly && !PROBLEMS.includes(row.status)) return false;
        if (!needle) return true;
        const parameters = row.lines.flatMap((l) => [l.parameterName ?? "", ...l.candidates.map((c) => c.parameter)]);
        return [row.propertySet, row.property, Inspector.formatValue(row.occurrenceValue), Inspector.formatValue(row.typeValue), ...parameters]
            .some((text) => text.toLowerCase().includes(needle));
    }

    const groups = $derived.by(() => {
        const bySet = new Map<string, InspectorRow[]>();
        for (const row of rows) {
            if (!rowMatches(row)) continue;
            bySet.set(row.propertySet, [...(bySet.get(row.propertySet) ?? []), row]);
        }
        return [...bySet.entries()];
    });

    const counts = $derived({
        exported: rows.filter((r) => r.status === "exported").length,
        problems: rows.filter((r) => PROBLEMS.includes(r.status)).length,
        unmapped: rows.filter((r) => r.status === "unmapped").length
    });

    const parameterGroups = $derived.by(() => {
        const result: { title: string; parameters: RevitParameter[] }[] = [];
        for (const scope of ["instance", "type"] as const) {
            const byGroup = new Map<string, RevitParameter[]>();
            for (const parameter of revitElement?.parameters ?? []) {
                if (parameter.scope !== scope) continue;
                if (needle && ![parameter.name, parameter.value, parameter.group].some((t) => t.toLowerCase().includes(needle))) continue;
                if (mappedOnly && !usage.has(paramKey(scope, parameter.name))) continue;
                byGroup.set(parameter.group, [...(byGroup.get(parameter.group) ?? []), parameter]);
            }
            for (const [group, parameters] of [...byGroup.entries()].sort((a, b) => a[0].localeCompare(b[0]))) {
                result.push({ title: `${scope === "instance" ? "Instance" : "Type"} · ${group}`, parameters });
            }
        }
        return result;
    });

    // Hovering one side lights up its partners on the other
    let hoveredRow = $state<InspectorRow | null>(null);
    let hoveredParam = $state<string | null>(null);
    const litParams = $derived(new Set(hoveredRow ? rowParams(hoveredRow) : []));
    const litRows = $derived(new Set(hoveredParam ? (paramRows.get(hoveredParam) ?? []) : []));

    const focusKey = $derived(
        view.focus ? `${(view.focus.propertySet ?? Inspector.ATTRIBUTES).toLowerCase()}|${view.focus.name.toLowerCase()}` : null
    );

    let ifcColumn: HTMLElement | undefined = $state();
    let revitColumn: HTMLElement | undefined = $state();

    function reveal(column: HTMLElement | undefined, selector: string) {
        const target = column?.querySelector<HTMLElement>(selector);
        if (!target) return;
        target.scrollIntoView({ block: "center", behavior: "smooth" });
        target.classList.remove("flash");
        void target.offsetWidth;
        target.classList.add("flash");
    }

    // Bring the property of the requirement the element was opened from into view once both sides are in,
    // and the Revit parameter it reads
    $effect(() => {
        if (!focusKey || view.ifcLoading || view.revitLoading) return;
        tick().then(() => {
            ifcColumn?.querySelector(`[data-row="${CSS.escape(focusKey)}"]`)?.scrollIntoView({ block: "center" });
            const row = rows.find((r) => r.key === focusKey);
            const param = row ? rowParams(row)[0] : undefined;
            if (param) revitColumn?.querySelector(`[data-param="${CSS.escape(param)}"]`)?.scrollIntoView({ block: "center" });
        });
    });

    function showParam(key: string) {
        if (mappedOnly && !usage.has(key)) mappedOnly = false;
        tick().then(() => reveal(revitColumn, `[data-param="${CSS.escape(key)}"]`));
    }

    function showRow(key: string) {
        problemsOnly = false;
        tick().then(() => reveal(ifcColumn, `[data-row="${CSS.escape(key)}"]`));
    }

    function lineSource(line: MappingLine): string {
        if (line.origin === "mapping-table") return "Mapping table";
        return `Pset file · ${line.entities.join(", ")}`;
    }

    function wanted(line: MappingLine): string {
        return line.builtInParameter ? `BuiltInParameter.${line.builtInParameter}` : line.parameterName ?? line.propertyName;
    }

    async function select() {
        if (!view.globalId) return;
        try {
            await selectElement(view.globalId);
        } catch (err) {
            error(`Failed to select element: ${err instanceof Error ? err.message : String(err)}`);
        }
    }

    // ---- Linking: pick an IFC property and a Revit parameter, then rewrite the pset file line ----

    let selectedRowKey = $state<string | null>(null);
    let selectedParamKey = $state<string | null>(null);
    const selectedRow = $derived(rows.find((r) => r.key === selectedRowKey) ?? null);
    const selectedParam = $derived(revitElement?.parameters.find((p) => paramKey(p.scope, p.name) === selectedParamKey) ?? null);
    const plan = $derived(
        selectedRow && selectedParam && revitElement ? Inspector.planLink(selectedRow, selectedParam, revitElement, view.ifc, view.tree) : null
    );
    let dataType = $state("Label");
    // A set that also covers other classes is split by default, so a link changes only this element's class
    let linkScope = $state<"class" | "set">("class");
    let linking = $state(false);

    // A new line's data type follows the picked parameter until changed
    $effect(() => {
        if (plan?.ok && plan.dataType) dataType = plan.dataType;
    });

    // Another element starts without a selection
    $effect(() => {
        void view.globalId;
        selectedRowKey = null;
        selectedParamKey = null;
    });

    const toggleRow = (key: string) => (selectedRowKey = selectedRowKey === key ? null : key);
    const toggleParam = (key: string) => (selectedParamKey = selectedParamKey === key ? null : key);

    function pick(event: MouseEvent | KeyboardEvent, toggle: () => void) {
        if ((event.target as HTMLElement).closest("button, input, select, a")) return;
        if (event instanceof KeyboardEvent) {
            if (event.key !== "Enter" && event.key !== " ") return;
            event.preventDefault();
        }
        toggle();
    }

    const failure = (err: unknown) => (err instanceof Error ? err.message : String(err));

    async function doLink() {
        if (!selectedRow || !selectedParam) return;
        linking = true;
        try {
            const change = await Inspector.link(selectedRow, selectedParam, dataType, linkScope);
            success(`Line ${change.line}: ${change.label}`);
            selectedParamKey = null;
        } catch (err) {
            error(failure(err));
        } finally {
            linking = false;
        }
    }

    async function doSplitAll() {
        linking = true;
        try {
            const result = await Inspector.splitSetsPerClass();
            success(`${result.split} sets split into ${result.split + result.added}, one per class. Review and save in the bar above.`);
        } catch (err) {
            error(failure(err));
        } finally {
            linking = false;
        }
    }

    async function doFixCase(row: InspectorRow) {
        linking = true;
        try {
            const change = await Inspector.fixEntityCase(row);
            success(`Line ${change.line}: ${change.label}`);
        } catch (err) {
            error(failure(err));
        } finally {
            linking = false;
        }
    }

    // ---- The draft of the pset file: review, undo, save ----

    let reviewOpen = $state(false);
    let savePath = $state("");
    let useForNextExport = $state(true);
    let confirmOverwrite = $state(false);

    $effect(() => {
        savePath = draft.file ? Draft.suggestedPath() : "";
        confirmOverwrite = false;
    });

    async function doUndo() {
        Draft.undo();
        await Inspector.reloadRevit();
    }

    async function doCloseDraft() {
        Draft.close();
        reviewOpen = false;
        await Inspector.reloadRevit();
    }

    async function doSave(overwrite: boolean) {
        const outcome = await Draft.save(savePath.trim(), { overwrite, thenUse: useForNextExport });
        if (outcome.saved) {
            confirmOverwrite = false;
            reviewOpen = false;
            success(`Saved ${fileNameOf(outcome.path)}${useForNextExport ? ". The next Export IFC uses it." : "."}`);
        } else if (outcome.exists) {
            confirmOverwrite = true;
        } else {
            error(outcome.message);
        }
    }

    // ---- Width: dragged on the left edge, remembered in this browser ----
    const WIDTH_KEY = "ifctester.inspector.width";
    const readWidth = () => {
        try {
            return Number(localStorage.getItem(WIDTH_KEY)) || 900;
        } catch {
            return 900;
        }
    };
    let width = $state(readWidth());

    function startResize(event: PointerEvent) {
        const handle = event.currentTarget as HTMLElement;
        const startX = event.clientX;
        const startWidth = width;
        handle.setPointerCapture(event.pointerId);
        const move = (e: PointerEvent) => {
            width = Math.max(480, Math.min(window.innerWidth * 0.8, startWidth + startX - e.clientX));
        };
        const up = () => {
            handle.removeEventListener("pointermove", move);
            handle.removeEventListener("pointerup", up);
            try {
                localStorage.setItem(WIDTH_KEY, String(Math.round(width)));
            } catch {
                // Width is only a convenience
            }
        };
        handle.addEventListener("pointermove", move);
        handle.addEventListener("pointerup", up);
    }

    function resizeByKey(event: KeyboardEvent) {
        if (event.key === "ArrowLeft") width = Math.min(window.innerWidth * 0.8, width + 40);
        else if (event.key === "ArrowRight") width = Math.max(480, width - 40);
    }
</script>

{#if view.open}
    <aside class="inspector" aria-label="Element inspector" style:width="{width}px">
        <button
            class="resizer"
            aria-label="Resize inspector"
            title="Drag to resize"
            onpointerdown={startResize}
            onkeydown={resizeByKey}
        ></button>

        <header class="inspector-header">
            <div class="title-row">
                <h2 title={view.entity?.name ?? ""}>{view.entity?.name || view.globalId}</h2>
                <button class="icon-btn" onclick={Inspector.reloadRevit} disabled={view.revitLoading || !Inspector.isRevitAvailable()} title="Ask Revit again" aria-label="Ask Revit again">
                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class:spinning={view.revitLoading}><path d="M21 12a9 9 0 1 1-3-6.7L21 8" /><path d="M21 3v5h-5" /></svg>
                </button>
                <button class="icon-btn" onclick={Inspector.close} aria-label="Close inspector" title="Close">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 6 6 18M6 6l12 12" /></svg>
                </button>
            </div>

            <div class="meta">
                <span class="mono">{view.globalId}</span>
                {#if revitElement}
                    <span class="muted">·</span>
                    {#if revitElement.mappingFiles.length > 0}
                        {#each revitElement.mappingFiles as file}
                            <span class="file" title={file}>{fileNameOf(file)}</span>
                        {/each}
                    {:else}
                        <span class="muted">No property set file read</span>
                    {/if}
                    {#if revitElement.configuration}<span class="muted">setup {revitElement.configuration}</span>{/if}
                    {#if revitElement.psetFile}
                        <button class="text-btn split-all" onclick={doSplitAll} disabled={linking || view.revitLoading || draft.loading} title="Rewrite the file so every set lists one class; a link then changes that class only">
                            One set per class
                        </button>
                    {/if}
                {/if}
            </div>

            {#if revitElement?.mappingNote}
                <p class="note warn">{revitElement.mappingNote}</p>
            {/if}
            {#if view.revit?.found && view.revit.message}
                <p class="note warn">{view.revit.message}</p>
            {/if}
            {#if view.ifcError}
                <p class="note warn">{view.ifcError}</p>
            {/if}

            <input class="filter" type="search" placeholder="Filter both sides: property, parameter or value" bind:value={filter} />

            {#if draft.text !== null}
                {@const count = draft.history.length}
                <div class="draft" class:dirty={count > 0} role="region" aria-label="Pset file edits">
                    <div class="draft-top">
                        <span class="draft-title">
                            {#if count > 0}
                                <b>{count}</b> unsaved {count === 1 ? "change" : "changes"} to <span class="file" title={draft.file}>{fileNameOf(draft.file)}</span>
                            {:else if draft.savedPath}
                                Saved as <span class="file" title={draft.savedPath}>{fileNameOf(draft.savedPath)}</span>
                                {#if Revit.exportOverrides.psetFile === draft.savedPath}<span class="muted">· the next Export IFC uses it</span>{/if}
                            {:else}
                                Editing <span class="file" title={draft.file}>{fileNameOf(draft.file)}</span>
                            {/if}
                        </span>
                        {#if count > 0}
                            <button class="text-btn" onclick={() => (reviewOpen = !reviewOpen)}>{reviewOpen ? "Hide changes" : "Review"}</button>
                            <button class="text-btn" onclick={doUndo} disabled={view.revitLoading}>Undo</button>
                        {/if}
                        <button class="text-btn" onclick={doCloseDraft} title={count > 0 ? "Throw the unsaved changes away" : "Stop showing the edited file"}>
                            {count > 0 ? "Discard" : "Close"}
                        </button>
                    </div>

                    {#if reviewOpen && count > 0}
                        <ol class="changes">
                            {#each draft.history as entry}
                                <li>
                                    <span class="muted">Line {entry.change.line}</span> {entry.change.label}
                                    {#if entry.change.before !== null}<code class="before">{entry.change.before.trim()}</code>{/if}
                                    <code class="after">{entry.change.after.trim()}</code>
                                </li>
                            {/each}
                        </ol>
                    {/if}

                    {#if count > 0}
                        <div class="save-row">
                            <input class="path" bind:value={savePath} aria-label="Save as" title="Type the original's path to overwrite it" />
                            <label><input type="checkbox" bind:checked={useForNextExport} /> Use for next export</label>
                            {#if confirmOverwrite}
                                <button class="primary danger" onclick={() => doSave(true)} disabled={draft.saving}>Replace existing file</button>
                            {:else}
                                <button class="primary" onclick={() => doSave(false)} disabled={draft.saving || !savePath.trim()}>Save</button>
                            {/if}
                        </div>
                        {#if confirmOverwrite}<p class="note warn">{fileNameOf(savePath)} exists. Replace it?</p>{/if}
                    {/if}
                    {#if draft.error}<p class="note warn">{draft.error}</p>{/if}
                </div>
            {/if}
        </header>

        <div class="columns">
            <section class="column ifc" aria-label="IFC properties">
                <div class="column-head">
                    <div class="column-title">
                        <span class="side-label">IFC</span>
                        <span class="entity">
                            {ifcElement?.ifcClass ?? view.entity?.class ?? ""}
                            {#if ifcElement?.type}<span class="muted">· {ifcElement.type.ifcClass}</span>{/if}
                        </span>
                    </div>
                    <div class="column-sub">
                        {#if revitElement}
                            <span><b class="ok">{counts.exported}</b> exported</span>
                            <span><b class:bad={counts.problems > 0}>{counts.problems}</b> problems</span>
                            <span><b>{counts.unmapped}</b> not in pset file</span>
                        {:else}
                            <span>{rows.length} properties</span>
                        {/if}
                        <label><input type="checkbox" bind:checked={problemsOnly} /> Problems only</label>
                    </div>
                </div>

                <div class="column-body" bind:this={ifcColumn}>
                    {#if view.ifcLoading}
                        <p class="muted pad">Reading the IFC…</p>
                    {:else}
                        {#each groups as [setName, setRows] (setName)}
                            <section class="set">
                                <h3>{setName}</h3>
                                <div role="listbox" aria-label={setName}>
                                {#each setRows as row (row.key)}
                                    {@const candidates = row.lines.flatMap((l) => l.candidates)}
                                    <div
                                        class="row status-{row.status}"
                                        class:focused={row.key === focusKey}
                                        class:lit={litRows.has(row.key)}
                                        class:selected={row.key === selectedRowKey}
                                        data-row={row.key}
                                        role="option"
                                        aria-selected={row.key === selectedRowKey}
                                        tabindex="0"
                                        onclick={(e) => pick(e, () => toggleRow(row.key))}
                                        onkeydown={(e) => pick(e, () => toggleRow(row.key))}
                                        onmouseenter={() => (hoveredRow = row)}
                                        onmouseleave={() => (hoveredRow = null)}
                                    >
                                        <div class="row-top">
                                            <span class="prop">{row.property}</span>
                                            {#if revitElement}
                                                <span class="status">{STATUS_LABEL[row.status]}</span>
                                            {/if}
                                        </div>
                                        {#if row.inIfc}
                                            {#if row.occurrenceValue !== undefined}
                                                <span class="value">{Inspector.formatValue(row.occurrenceValue) || "(empty)"}</span>
                                            {/if}
                                            {#if row.typeValue !== undefined}
                                                <span class="value"><span class="badge">type</span>{Inspector.formatValue(row.typeValue) || "(empty)"}</span>
                                            {/if}
                                        {:else}
                                            <span class="value absent">not in the IFC</span>
                                        {/if}

                                        {#if row.lines.length > 0}
                                            <div class="from" title={row.lines.map(lineSource).join("\n")}>
                                                <span class="arrow">←</span>
                                                {#if candidates.length > 0}
                                                    {#each candidates as candidate, i}
                                                        <button class="param-link" class:fallback={i > 0} onclick={() => showParam(paramKey(candidate.scope, candidate.parameter))}>
                                                            {candidate.parameter}<span class="badge">{candidate.scope === "type" ? "T" : "I"}</span>
                                                        </button>
                                                    {/each}
                                                {:else}
                                                    <span class="missing-param">{wanted(row.lines[0])}</span>
                                                {/if}
                                            </div>
                                        {:else if row.wrongCase.length > 0}
                                            <div class="from"><span class="arrow">←</span><span class="missing-param">{wanted(row.wrongCase[0].line)}</span></div>
                                        {/if}

                                        {#if row.note}
                                            <p class="row-note">{row.note}</p>
                                        {/if}
                                        {#if row.status === "wrong-case" && revitElement?.psetFile}
                                            <button class="text-btn fix" onclick={() => doFixCase(row)} disabled={linking || view.revitLoading}>
                                                Fix case: {row.wrongCase[0].entity} → {row.wrongCase[0].canonical}
                                            </button>
                                        {/if}
                                    </div>
                                {/each}
                                </div>
                            </section>
                        {:else}
                            <p class="muted pad">Nothing matches.</p>
                        {/each}
                    {/if}
                </div>
            </section>

            <section class="column revit" aria-label="Revit parameters">
                <div class="column-head">
                    <div class="column-title">
                        <span class="side-label">Revit</span>
                        <span class="entity">
                            {#if revitElement}
                                {revitElement.category ?? ""} · id {revitElement.elementId}
                            {:else if view.revitLoading}
                                <span class="muted">Asking Revit…</span>
                            {/if}
                        </span>
                        {#if revitElement}<button class="link-btn" onclick={select}>Select</button>{/if}
                    </div>
                    <div class="column-sub">
                        {#if revitElement}
                            {#if revitElement.typeName}<span class="type-name" title={revitElement.typeName}>{revitElement.typeName}</span>{/if}
                            <span><b>{revitElement.parameters.length}</b> parameters</span>
                        {/if}
                        <label><input type="checkbox" bind:checked={mappedOnly} disabled={!revitElement} /> Mapped only</label>
                    </div>
                </div>

                <div class="column-body" bind:this={revitColumn}>
                    {#if view.revitError}
                        <p class="note pad-note">{view.revitError}</p>
                    {:else if view.revitLoading && !revitElement}
                        <p class="muted pad">Asking Revit…</p>
                    {:else if revitElement}
                        {#each parameterGroups as group (group.title)}
                            <section class="set">
                                <h3>{group.title}</h3>
                                <div role="listbox" aria-label={group.title}>
                                {#each group.parameters as parameter (parameter.scope + parameter.name + parameter.builtIn)}
                                    {@const key = paramKey(parameter.scope, parameter.name)}
                                    {@const used = usage.get(key)}
                                    <div
                                        class="param"
                                        class:used={!!used}
                                        class:lit={litParams.has(key)}
                                        class:selected={key === selectedParamKey}
                                        data-param={key}
                                        role="option"
                                        aria-selected={key === selectedParamKey}
                                        tabindex="0"
                                        onclick={(e) => pick(e, () => toggleParam(key))}
                                        onkeydown={(e) => pick(e, () => toggleParam(key))}
                                        onmouseenter={() => (hoveredParam = key)}
                                        onmouseleave={() => (hoveredParam = null)}
                                    >
                                        <span class="prop">
                                            {parameter.name}
                                            {#if parameter.shared}<span class="badge">shared</span>{/if}
                                            {#if parameter.readOnly}<span class="badge">read-only</span>{/if}
                                        </span>
                                        <span class="value">{parameter.value || "(empty)"}</span>
                                        {#if used}
                                            <button class="exports" onclick={() => showRow((paramRows.get(key) ?? [])[0])}>→ {used.join(", ")}</button>
                                        {/if}
                                    </div>
                                {/each}
                                </div>
                            </section>
                        {:else}
                            <p class="muted pad">Nothing matches.</p>
                        {/each}
                    {/if}
                </div>
            </section>
        </div>

        {#if revitElement && (selectedRow || selectedParam)}
            <div class="linkbar" role="region" aria-label="Link">
                <div class="link-ends">
                    <span class="end ifc-end">
                        {#if selectedRow}
                            {#if selectedRow.propertySet !== Inspector.ATTRIBUTES}<span class="muted">{selectedRow.propertySet}.</span>{/if}<b>{selectedRow.property}</b>
                        {:else}
                            <span class="muted">Pick an IFC property on the left</span>
                        {/if}
                    </span>
                    <span class="arrow">←</span>
                    <span class="end revit-end">
                        {#if selectedParam}
                            <b>{selectedParam.name}</b><span class="badge">{selectedParam.scope === "type" ? "T" : "I"}</span>
                        {:else}
                            <span class="muted">Pick a Revit parameter on the right</span>
                        {/if}
                    </span>
                    {#if plan?.ok && plan.kind === "add"}
                        <select bind:value={dataType} aria-label="Data type">
                            {#each DATA_TYPES as type}<option value={type}>{type}</option>{/each}
                        </select>
                    {/if}
                    <button class="primary" onclick={doLink} disabled={!plan?.ok || linking || view.revitLoading || draft.loading}>Link</button>
                    <button class="text-btn" onclick={() => { selectedRowKey = null; selectedParamKey = null; }}>Cancel</button>
                </div>
                {#if plan}
                    {#if plan.ok}
                        <p class="plan">
                            {#if plan.kind === "remap"}
                                Line {plan.line} of {fileNameOf(revitElement.psetFile)}: <code>{plan.previous}</code> → <code>{plan.parameterRef}</code>
                            {:else}
                                New line {plan.line} in set "{plan.setName}" of {fileNameOf(revitElement.psetFile)}: <code>{plan.property}</code> ← <code>{plan.parameterRef}</code>
                            {/if}
                        </p>
                        {#if plan.splitEntity}
                            <div class="scope" role="radiogroup" aria-label="Which classes">
                                <label><input type="radio" bind:group={linkScope} value="class" /> Only {plan.splitEntity}</label>
                                <label><input type="radio" bind:group={linkScope} value="set" /> All {plan.entities.length} classes in the set</label>
                            </div>
                            {#if linkScope === "class"}
                                <p class="plan">
                                    The set "{plan.setName}" is split: {plan.splitEntity} leaves its header and gets its own copy of the set ({plan.blockLines.length + (plan.kind === "add" ? 1 : 0)} lines) with this change. The other {plan.entities.length - 1} classes keep the set as it is.
                                </p>
                            {:else}
                                <p class="plan warn">Every element of {plan.entities.join(", ")} reads the new parameter, not only this one.</p>
                            {/if}
                        {:else if revitElement && ifcElement && (plan.entities.length > 1 || plan.entities[0] !== ifcElement.ifcClass)}
                            <p class="plan warn">The set applies to {plan.entities.join(", ")}: every element of {plan.entities.length > 1 ? "these classes" : "that class"} reads the new parameter, not only this one.</p>
                            {#if plan.splitBlocked}<p class="plan">{plan.splitBlocked}</p>{/if}
                        {/if}
                        {#each plan.warnings as warning}<p class="plan warn">{warning}</p>{/each}
                    {:else}
                        <p class="plan bad">{plan.reason}</p>
                    {/if}
                {/if}
            </div>
        {/if}
    </aside>
{/if}

<style>
    .inspector {
        --ifc-bg: #161d27;
        --ifc-head: #1b2532;
        --ifc-accent: #60a5fa;
        --revit-bg: #221c15;
        --revit-head: #2a2219;
        --revit-accent: #f0a646;

        position: relative;
        min-width: 480px;
        max-width: 80%;
        flex-shrink: 0;
        display: flex;
        flex-direction: column;
        border-left: 1px solid #2f2f2f;
        background: #1c1c1c;
        color: #e0e0e0;
        font-size: 12px;
    }

    .resizer {
        padding: 0;
        border: none;
        background: transparent;
        position: absolute;
        top: 0;
        bottom: 0;
        left: -3px;
        width: 6px;
        z-index: 3;
        cursor: col-resize;
    }

    .resizer:hover,
    .resizer:focus-visible {
        background: #3b82f6;
        outline: none;
    }

    .inspector-header {
        padding: 12px 16px 10px;
        border-bottom: 1px solid #2f2f2f;
    }

    .title-row {
        display: flex;
        align-items: center;
        gap: 4px;
    }

    .title-row h2 {
        flex: 1;
        margin: 0;
        font-size: 15px;
        font-weight: 600;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .meta {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: 6px;
        margin: 6px 0 8px;
    }

    .mono {
        font-family: ui-monospace, Consolas, monospace;
    }

    .muted {
        color: #8b8b8b;
    }

    .pad {
        padding: 12px 14px;
        margin: 0;
    }

    .file {
        padding: 0 6px;
        border-radius: 4px;
        background: #2a2a2a;
    }

    .note {
        margin: 6px 0;
        padding: 6px 8px;
        border-radius: 4px;
        background: #262626;
        color: #c8c8c8;
    }

    .note.warn {
        background: #3a2f12;
        color: #f5d38a;
    }

    .pad-note {
        margin: 12px 14px;
    }

    .filter {
        width: 100%;
        padding: 6px 8px;
        border: 1px solid #3a3a3a;
        border-radius: 4px;
        background: #232323;
        color: inherit;
        font: inherit;
    }

    .icon-btn {
        display: inline-flex;
        padding: 4px;
        border: none;
        border-radius: 4px;
        background: transparent;
        color: #b5b5b5;
        cursor: pointer;
    }

    .icon-btn:hover:not(:disabled) {
        background: #2f2f2f;
        color: #fff;
    }

    .icon-btn:disabled {
        opacity: 0.4;
        cursor: default;
    }

    .link-btn {
        margin-left: auto;
        padding: 0;
        border: none;
        background: none;
        color: var(--revit-accent);
        cursor: pointer;
        font: inherit;
    }

    .spinning {
        animation: spin 1s linear infinite;
    }

    /* ---- The two sides ---- */

    .columns {
        flex: 1;
        min-height: 0;
        display: grid;
        grid-template-columns: 1fr 1fr;
    }

    .column {
        display: flex;
        flex-direction: column;
        min-width: 0;
        min-height: 0;
    }

    .column.ifc {
        background: var(--ifc-bg);
        --accent: var(--ifc-accent);
        --head: var(--ifc-head);
        border-right: 1px solid #2f2f2f;
    }

    .column.revit {
        background: var(--revit-bg);
        --accent: var(--revit-accent);
        --head: var(--revit-head);
    }

    .column-head {
        padding: 8px 14px;
        background: var(--head);
        border-top: 2px solid var(--accent);
        border-bottom: 1px solid #00000055;
    }

    .column-title {
        display: flex;
        align-items: baseline;
        gap: 8px;
        min-width: 0;
    }

    .side-label {
        color: var(--accent);
        font-size: 11px;
        font-weight: 700;
        letter-spacing: 0.08em;
        text-transform: uppercase;
    }

    .entity {
        min-width: 0;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        font-weight: 500;
    }

    .column-sub {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: 4px 12px;
        margin-top: 4px;
        color: #9a9a9a;
    }

    .column-sub b {
        color: #d4d4d4;
    }

    .column-sub b.ok {
        color: #86efac;
    }

    .column-sub b.bad {
        color: #fca5a5;
    }

    .column-sub label {
        display: flex;
        align-items: center;
        gap: 4px;
        margin-left: auto;
        white-space: nowrap;
    }

    .type-name {
        max-width: 100%;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .column-body {
        flex: 1;
        min-height: 0;
        overflow-y: auto;
        padding-bottom: 24px;
    }

    .set h3 {
        position: sticky;
        top: 0;
        z-index: 1;
        margin: 0;
        padding: 8px 14px 4px;
        background: inherit;
        font-size: 12px;
        font-weight: 600;
        color: var(--accent);
    }

    .column.ifc .set h3 {
        background: var(--ifc-bg);
    }

    .column.revit .set h3 {
        background: var(--revit-bg);
    }

    .row,
    .param {
        padding: 5px 14px 5px 11px;
        border-left: 3px solid transparent;
        transition: background 0.12s;
    }

    .row {
        display: flex;
        flex-direction: column;
        gap: 1px;
    }

    .row-top {
        display: flex;
        align-items: start;
        gap: 8px;
    }

    .row-top .prop {
        flex: 1;
    }

    .prop {
        font-weight: 500;
        overflow-wrap: anywhere;
    }

    .value {
        color: #b9b9b9;
        overflow-wrap: anywhere;
    }

    .absent {
        color: #777;
        font-style: italic;
    }

    .from {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: 2px 6px;
        margin-top: 1px;
        font-size: 11px;
    }

    .arrow {
        color: var(--revit-accent);
    }

    .param-link {
        padding: 0;
        border: none;
        background: none;
        color: var(--revit-accent);
        cursor: pointer;
        font: inherit;
        text-align: left;
    }

    .param-link:hover {
        text-decoration: underline;
    }

    .fallback {
        opacity: 0.6;
    }

    .missing-param {
        color: #fca5a5;
        text-decoration: line-through;
    }

    .badge {
        display: inline-block;
        margin: 0 4px;
        padding: 0 4px;
        border-radius: 3px;
        background: #ffffff14;
        color: #aaa;
        font-size: 10px;
        font-weight: 400;
    }

    .status {
        flex-shrink: 0;
        padding: 1px 6px;
        border-radius: 8px;
        font-size: 10px;
        white-space: nowrap;
        background: #ffffff10;
        color: #9a9a9a;
    }

    .status-exported .status {
        background: #173a24;
        color: #86efac;
    }

    .status-not-exported,
    .status-differs {
        border-left-color: #b7791f;
    }

    .status-not-exported .status,
    .status-differs .status {
        background: #3a2f12;
        color: #f5d38a;
    }

    .status-no-parameter,
    .status-empty,
    .status-wrong-case {
        border-left-color: #b91c1c;
    }

    .status-no-parameter .status,
    .status-empty .status,
    .status-wrong-case .status {
        background: #4a1d1d;
        color: #fca5a5;
    }

    .row.focused {
        background: #ffffff0d;
        box-shadow: inset 0 0 0 1px #60a5fa55;
    }

    .row-note {
        margin: 3px 0 0;
        color: #a3a3a3;
        font-size: 11px;
    }

    .param {
        display: grid;
        grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
        gap: 2px 8px;
    }

    .param.used {
        border-left-color: var(--revit-accent);
    }

    .exports {
        grid-column: 1 / -1;
        padding: 0;
        border: none;
        background: none;
        color: var(--ifc-accent);
        cursor: pointer;
        font: inherit;
        font-size: 11px;
        text-align: left;
    }

    .exports:hover {
        text-decoration: underline;
    }

    /* Partners of the hovered row on the other side */
    .row.lit,
    .param.lit,
    .row:hover,
    .param:hover {
        background: #ffffff12;
    }

    .row.lit {
        box-shadow: inset 3px 0 0 var(--ifc-accent);
    }

    .param.lit {
        box-shadow: inset 3px 0 0 var(--revit-accent);
    }

    /* ---- Linking ---- */

    .row,
    .param {
        cursor: pointer;
    }

    .row:focus-visible,
    .param:focus-visible {
        outline: 1px solid #ffffff55;
        outline-offset: -1px;
    }

    .row.selected {
        background: #1e3a5f;
        box-shadow: inset 3px 0 0 var(--ifc-accent);
    }

    .param.selected {
        background: #4a3416;
        box-shadow: inset 3px 0 0 var(--revit-accent);
    }

    .text-btn {
        padding: 2px 6px;
        border: none;
        border-radius: 4px;
        background: transparent;
        color: #b5b5b5;
        cursor: pointer;
        font: inherit;
    }

    .text-btn:hover:not(:disabled) {
        background: #ffffff14;
        color: #fff;
    }

    .text-btn:disabled,
    .primary:disabled {
        opacity: 0.45;
        cursor: default;
    }

    .text-btn.fix {
        align-self: flex-start;
        margin-top: 3px;
        background: #4a1d1d;
        color: #fca5a5;
    }

    .primary {
        padding: 4px 14px;
        border: none;
        border-radius: 4px;
        background: #2563eb;
        color: #fff;
        cursor: pointer;
        font: inherit;
        font-weight: 600;
    }

    .primary:hover:not(:disabled) {
        background: #1d4ed8;
    }

    .primary.danger {
        background: #b91c1c;
    }

    .linkbar {
        padding: 10px 16px;
        border-top: 1px solid #3a3a3a;
        background: #202020;
        box-shadow: 0 -6px 12px #00000040;
    }

    .link-ends {
        display: flex;
        align-items: center;
        gap: 8px;
    }

    .end {
        min-width: 0;
        flex: 1;
        padding: 4px 8px;
        border-radius: 4px;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .ifc-end {
        background: var(--ifc-head);
        border-left: 3px solid var(--ifc-accent);
    }

    .revit-end {
        background: var(--revit-head);
        border-left: 3px solid var(--revit-accent);
    }

    .linkbar select {
        padding: 3px 6px;
        border: 1px solid #3a3a3a;
        border-radius: 4px;
        background: #232323;
        color: inherit;
        font: inherit;
    }

    .split-all {
        margin-left: auto;
        border: 1px solid #3a3a3a;
    }

    .scope {
        display: flex;
        gap: 16px;
        margin-top: 6px;
    }

    .scope label {
        display: flex;
        align-items: center;
        gap: 5px;
        cursor: pointer;
    }

    .plan {
        margin: 6px 0 0;
        color: #c8c8c8;
    }

    .plan.warn {
        color: #f5d38a;
    }

    .plan.bad {
        color: #fca5a5;
    }

    code {
        padding: 0 4px;
        border-radius: 3px;
        background: #ffffff12;
        font-family: ui-monospace, Consolas, monospace;
        font-size: 11px;
    }

    .draft {
        margin-top: 8px;
        padding: 8px 10px;
        border-radius: 6px;
        border: 1px solid #3a3a3a;
        background: #222;
    }

    .draft.dirty {
        border-color: #b7791f;
        background: #2a2416;
    }

    .draft-top {
        display: flex;
        align-items: center;
        gap: 6px;
    }

    .draft-title {
        flex: 1;
        min-width: 0;
    }

    .changes {
        margin: 8px 0 0;
        padding-left: 20px;
        display: flex;
        flex-direction: column;
        gap: 4px;
    }

    .changes code {
        display: block;
        margin-top: 2px;
        white-space: pre-wrap;
        overflow-wrap: anywhere;
    }

    .changes .before {
        color: #fca5a5;
        text-decoration: line-through;
    }

    .changes .after {
        color: #86efac;
    }

    .save-row {
        display: flex;
        align-items: center;
        gap: 8px;
        margin-top: 8px;
    }

    .save-row .path {
        flex: 1;
        min-width: 0;
        padding: 4px 8px;
        border: 1px solid #3a3a3a;
        border-radius: 4px;
        background: #191919;
        color: inherit;
        font: inherit;
    }

    .save-row label {
        display: flex;
        align-items: center;
        gap: 4px;
        white-space: nowrap;
    }

    :global(.inspector .flash) {
        animation: -global-inspector-flash 1.2s ease-out;
    }

    @keyframes -global-inspector-flash {
        from {
            background: #ffffff33;
        }
        to {
            background: transparent;
        }
    }
</style>
