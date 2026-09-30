<script lang="ts">
    import * as Dialog from "$lib/components/ui/dialog";
    import * as Writeback from "$src/modules/api/writeback.svelte";
    import type { ParameterCandidate, PendingChange } from "$src/modules/api/writeback.svelte";
    import { Revit, exportAndAudit, getIfcConfigurations } from "$src/modules/api/revit.svelte.js";
    import { error, success } from "$src/modules/utils/toast.svelte";

    const SOURCE_LABELS: Record<string, string> = {
        "pset-mapping-file": "property set mapping file",
        "name-match": "parameter name",
        "ifc-override": "IFC override parameter"
    };

    // The re-export replaces the audit; the dialog has to outlive that to show its progress
    let { hasAudit }: { hasAudit: boolean } = $props();

    let open = $state(false);
    let applyError = $state<string | null>(null);
    let reexporting = $state(false);
    let configurations = $state<string[]>([]);
    let selectedConfiguration = $state('');

    const changes = $derived(Writeback.pendingChanges());
    const count = $derived(changes.length);
    const applied = $derived(Writeback.Writeback.applied);
    const sendable = $derived(Writeback.sendableChanges().length);
    const busy = $derived(Writeback.Writeback.resolving || Writeback.Writeback.applying || reexporting);

    const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? '' : 's'}`;

    async function review() {
        applyError = null;
        open = true;
        // Always ask again: the current values may have changed in Revit since the last look
        await Writeback.resolvePending();
    }

    async function apply() {
        applyError = null;
        try {
            const outcome = await Writeback.applyPending();
            if (outcome.applied > 0) {
                success(`${plural(outcome.applied, 'change')} applied to Revit`);
                loadConfigurations();
            }
            if (outcome.failed > 0) {
                error(`${plural(outcome.failed, 'change')} could not be applied`);
            }
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            if (err instanceof Writeback.RevitHttpError && err.status !== 500) {
                // Revit refused the request as a whole (busy, or a malformed request): nothing was written
                applyError = `${message}. Nothing was applied.`;
            } else {
                // Revit may have applied the batch even though the answer did not arrive
                applyError = `${message}. Check the Revit model before applying again.`;
            }
            error('Failed to apply changes to Revit');
        }
    }

    async function loadConfigurations() {
        selectedConfiguration = Revit.exportConfiguration;
        if (configurations.length > 0) return;
        configurations = (await getIfcConfigurations()) as string[];
    }

    async function reexport() {
        if (!selectedConfiguration) return;
        reexporting = true;
        try {
            // Replaces the audit, which drops whatever is still pending
            if (await exportAndAudit(selectedConfiguration)) {
                open = false;
            }
        } finally {
            reexporting = false;
        }
    }

    function targetLabel(change: PendingChange) {
        return change.facet === 'property' ? `${change.propertySet}.${change.name}` : `Attribute ${change.name}`;
    }

    function typeScopeNote(change: PendingChange) {
        const count = change.resolved?.typeInstanceCount;
        const typeName = change.resolved?.typeName;
        if (count == null) return 'changes every instance of this type';
        return `changes all ${plural(count, 'instance')} of ${typeName ? `type ${typeName}` : 'this type'}`;
    }

    function candidateLabel(candidate: ParameterCandidate) {
        const note = candidate.readOnly ? ', read-only' : candidate.storageType === 'elementid' ? ', element reference' : '';
        return `${candidate.parameter} (${candidate.scope}${note})`;
    }
</script>

{#if hasAudit}
<div class="pending-bar" class:active={count > 0}>
    <img src="/images/revit-icon.svg" alt="" width="18" height="18" />
    {#if count > 0}
        <span class="pending-text"><strong>{plural(count, 'pending change')}</strong> for Revit</span>
        <button type="button" class="bar-btn primary" onclick={review}>Review changes</button>
        <button type="button" class="bar-btn" onclick={Writeback.discardPending}>Discard</button>
    {:else if applied.length > 0}
        <span class="pending-text">{plural(applied.length, 'change')} applied to Revit. Re-export to confirm the fix.</span>
        <button type="button" class="bar-btn primary" onclick={() => { loadConfigurations(); review(); }}>Show</button>
    {:else}
        <span class="pending-text muted">To fix a failed element in Revit, expand a failed requirement and type the correct value next to the element.</span>
    {/if}
</div>
{/if}

<Dialog.Root bind:open>
    <Dialog.Content class="sm:max-w-[960px]">
        <Dialog.Header>
            <Dialog.Title>Pending changes ({count})</Dialog.Title>
            <Dialog.Description>
                Nothing is written until you click Apply to Revit. All changes land in one Revit undo step (Ctrl+Z).
            </Dialog.Description>
        </Dialog.Header>

        <div class="dialog-body">
            {#if Writeback.Writeback.resolving}
                <p class="state-line">
                    <svg class="spinner" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M21 12a9 9 0 11-6.219-8.56"/>
                    </svg>
                    Looking up the parameters in Revit...
                </p>
            {:else if Writeback.Writeback.resolveError}
                <p class="state-line problem">Could not look up the parameters in Revit: {Writeback.Writeback.resolveError}</p>
                <button type="button" class="bar-btn" onclick={review}>Try again</button>
            {:else if count > 0}
                <div class="changes-container">
                    <table class="changes-table">
                        <thead>
                            <tr>
                                <th>Element</th>
                                <th>Requirement</th>
                                <th>Revit parameter</th>
                                <th>Current value</th>
                                <th>New value</th>
                                <th></th>
                            </tr>
                        </thead>
                        <tbody>
                            {#each changes as change (change.key)}
                                {@const status = Writeback.getChangeStatus(change)}
                                {@const resolved = change.resolved}
                                {@const candidate = status.candidate}
                                <tr class:blocked={!status.sendable} class:failed={!!change.error}>
                                    <td>
                                        <div class="cell-main">{resolved?.elementName || change.entityName || change.entityClass || '-'}</div>
                                        <div class="cell-sub">
                                            {#if resolved?.found}
                                                {resolved.category || change.entityClass} · id {resolved.elementId}
                                            {:else}
                                                {change.entityClass} · {change.globalId}
                                            {/if}
                                        </div>
                                    </td>
                                    <td>
                                        <div class="cell-main">{targetLabel(change)}</div>
                                    </td>
                                    <td>
                                        {#if resolved && resolved.candidates.length > 1}
                                            <select
                                                class="candidate-select"
                                                aria-label="Revit parameter for {resolved.elementName || change.entityName}"
                                                value={change.candidateIndex}
                                                onchange={(event) => Writeback.selectCandidate(change.key, Number(event.currentTarget.value))}
                                            >
                                                {#each resolved.candidates as option, optionIndex}
                                                    <option value={optionIndex} disabled={!Writeback.isCandidateWritable(option)}>{candidateLabel(option)}</option>
                                                {/each}
                                            </select>
                                        {:else if resolved && resolved.candidates.length === 1}
                                            <div class="cell-main">{resolved.candidates[0].parameter}</div>
                                        {/if}
                                        {#if candidate}
                                            <div class="cell-sub">
                                                {#if candidate.scope === 'type'}
                                                    <span class="scope-badge type">Type</span> {typeScopeNote(change)}
                                                {:else}
                                                    <span class="scope-badge">Instance</span>
                                                {/if}
                                                {#if SOURCE_LABELS[candidate.source]}
                                                    · found by {SOURCE_LABELS[candidate.source]}
                                                {/if}
                                            </div>
                                        {/if}
                                        {#if status.problem}
                                            <div class="cell-problem">Not applicable: {status.problem}</div>
                                        {/if}
                                        {#if change.error}
                                            <div class="cell-problem">Revit could not apply this: {change.error}</div>
                                        {/if}
                                    </td>
                                    <td>
                                        {#if candidate}
                                            <div class="cell-main" class:empty={!candidate.hasValue}>{candidate.hasValue ? candidate.value : '(empty)'}</div>
                                        {:else}
                                            -
                                        {/if}
                                    </td>
                                    <td>
                                        <div class="cell-main new-value">{change.value}</div>
                                        {#if candidate?.storageType === 'double'}
                                            <div class="cell-sub">read in the project's display units</div>
                                        {/if}
                                    </td>
                                    <td>
                                        <button type="button" class="remove-btn" onclick={() => Writeback.removeChange(change.key)} disabled={busy} title="Remove this pending change" aria-label="Remove pending change">
                                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                                <path d="M18 6L6 18M6 6l12 12"/>
                                            </svg>
                                        </button>
                                    </td>
                                </tr>
                            {/each}
                        </tbody>
                    </table>
                </div>
                {#if sendable < count}
                    <p class="state-line">{plural(count - sendable, 'change')} cannot be applied and will stay pending.</p>
                {/if}
                {#if Writeback.Writeback.resolveNote}
                    <p class="state-line">{Writeback.Writeback.resolveNote}</p>
                {/if}
            {:else if applied.length === 0 && !reexporting}
                <p class="state-line">No pending changes.</p>
            {/if}

            {#if applyError}
                <p class="state-line problem">{applyError}</p>
            {/if}

            {#if applied.length > 0}
                <div class="applied">
                    <h4>Applied to Revit ({applied.length})</h4>
                    <ul>
                        {#each applied as item (item.key)}
                            <li>
                                <span class="applied-mark">✓</span>
                                <span>
                                    {item.elementName}: {item.parameter} = <strong>{item.newValue}</strong>
                                    {#if item.scope === 'type'}<span class="scope-badge type">Type</span>{/if}
                                    {#if item.message}<span class="applied-note">{item.message}</span>{/if}
                                </span>
                            </li>
                        {/each}
                    </ul>
                </div>
            {/if}

            {#if applied.length > 0 || reexporting}
                <div class="reexport">
                    <p class="state-line">Re-export the model from Revit and run the audit again to confirm the fix.</p>
                    <div class="reexport-controls">
                        <select class="candidate-select" aria-label="IFC export configuration" bind:value={selectedConfiguration} disabled={reexporting}>
                            <option value="" disabled>Select your IFC Export Configuration</option>
                            {#each configurations as configuration}
                                <option value={configuration}>{configuration}</option>
                            {/each}
                            {#if selectedConfiguration && !configurations.includes(selectedConfiguration)}
                                <option value={selectedConfiguration}>{selectedConfiguration}</option>
                            {/if}
                        </select>
                        <button type="button" class="bar-btn primary" onclick={reexport} disabled={busy || !selectedConfiguration}>
                            {#if reexporting}
                                <svg class="spinner" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                    <path d="M21 12a9 9 0 11-6.219-8.56"/>
                                </svg>
                                Exporting and auditing...
                            {:else}
                                Re-export and re-audit
                            {/if}
                        </button>
                    </div>
                </div>
            {/if}
        </div>

        <Dialog.Footer>
            <button type="button" class="bar-btn" onclick={() => open = false}>Close</button>
            <button type="button" class="bar-btn primary" onclick={apply} disabled={busy || sendable === 0 || !!Writeback.Writeback.resolveError}>
                {#if Writeback.Writeback.applying}
                    <svg class="spinner" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M21 12a9 9 0 11-6.219-8.56"/>
                    </svg>
                    Applying...
                {:else}
                    Apply to Revit{sendable > 0 ? ` (${sendable})` : ''}
                {/if}
            </button>
        </Dialog.Footer>
    </Dialog.Content>
</Dialog.Root>

<style>
    .pending-bar {
        display: flex;
        align-items: center;
        gap: 10px;
        margin-top: 16px;
        padding: 8px 12px;
        border: 1px solid #5555556e;
        border-radius: 8px;
        background: #ffffff05;
        font-size: 13px;
        color: #e0e0e0;
    }

    .pending-bar.active {
        border-color: #10b98155;
        background: #10b98110;
    }

    .pending-text {
        flex: 1;
    }

    .pending-text.muted {
        color: #9ca3af;
    }

    .bar-btn {
        display: inline-flex;
        align-items: center;
        gap: 0.5rem;
        padding: 6px 12px;
        background: #ffffff12;
        color: #ffffff;
        border: 1px solid #ffffff24;
        border-radius: 6px;
        font-size: 13px;
        font-weight: 500;
        white-space: nowrap;
        cursor: pointer;
        transition: all 0.2s;
    }

    .bar-btn:hover:not(:disabled) {
        background: #ffffff1a;
        border-color: #ffffff40;
    }

    .bar-btn.primary {
        background: #12613d;
        border-color: transparent;
    }

    .bar-btn.primary:hover:not(:disabled) {
        background: #197148;
    }

    .bar-btn:disabled {
        background: #ffffff0a;
        color: #6b7280;
        cursor: not-allowed;
    }

    .dialog-body {
        display: flex;
        flex-direction: column;
        gap: 12px;
        min-width: 0;
        max-height: 65vh;
        overflow-y: auto;
        color: #e0e0e0;
    }

    .state-line {
        display: flex;
        align-items: center;
        gap: 8px;
        margin: 0;
        font-size: 13px;
        color: #b0b0b0;
    }

    .state-line.problem, .cell-problem {
        color: #ff8282;
    }

    .changes-container {
        border: 1px solid #5555556e;
        border-radius: 8px;
        overflow-x: auto;
        background: #ffffff02;
    }

    .changes-table {
        width: 100%;
        border-collapse: collapse;
        font-size: 12px;
    }

    .changes-table th {
        background: #ffffff12;
        color: #b0b0b0;
        padding: 8px 12px;
        text-align: left;
        font-weight: 500;
        border-bottom: 1px solid #5555556e;
    }

    .changes-table td {
        padding: 8px 12px;
        border-bottom: 1px solid #55555530;
        vertical-align: top;
    }

    .changes-table tr.blocked .new-value {
        color: #9ca3af;
        text-decoration: line-through;
    }

    .changes-table tr.failed {
        background: #ef444410;
    }

    .cell-sub {
        margin-top: 2px;
        font-size: 11px;
        color: #9ca3af;
    }

    .cell-problem {
        margin-top: 4px;
        font-size: 11px;
    }

    .cell-main.empty {
        color: #9ca3af;
        font-style: italic;
    }

    .new-value {
        color: #79ecb7;
        font-weight: 500;
    }

    .scope-badge {
        display: inline-block;
        padding: 0 6px;
        border: 1px solid #5555556e;
        border-radius: 12px;
        font-size: 10px;
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.5px;
        color: #b0b0b0;
    }

    .scope-badge.type {
        border-color: #ff8c0066;
        background: #ff8c0022;
        color: #ffb454;
    }

    .candidate-select {
        max-width: 260px;
        padding: 4px 8px;
        background: #ffffff0a;
        border: 1px solid #e5e7eb24;
        border-radius: 0.375rem;
        color: #ffffffd9;
        font-size: 12px;
        cursor: pointer;
    }

    .candidate-select option {
        background: #1f2937;
        color: #ffffffd9;
    }

    .candidate-select option:disabled {
        color: #6b7280;
    }

    .remove-btn {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 24px;
        height: 24px;
        background: none;
        color: #6b7280;
        border: none;
        border-radius: 0.25rem;
        cursor: pointer;
        transition: all 0.2s;
    }

    .remove-btn:hover:not(:disabled) {
        color: #ff7171;
    }

    .applied h4 {
        margin: 0 0 6px 0;
        font-size: 13px;
        font-weight: 600;
        color: #10b981;
    }

    .applied ul {
        display: flex;
        flex-direction: column;
        gap: 4px;
        margin: 0;
        padding: 0;
        list-style: none;
        font-size: 12px;
    }

    .applied li {
        display: flex;
        gap: 8px;
    }

    .applied-mark {
        color: #10b981;
    }

    .applied-note {
        margin-left: 6px;
        color: #ffb454;
    }

    .reexport {
        display: flex;
        flex-direction: column;
        gap: 8px;
        padding-top: 12px;
        border-top: 1px solid #5555556e;
    }

    .reexport-controls {
        display: flex;
        align-items: center;
        gap: 8px;
    }

    .spinner {
        animation: spin 1s linear infinite;
    }

    @keyframes spin {
        from { transform: rotate(0deg); }
        to { transform: rotate(360deg); }
    }
</style>
