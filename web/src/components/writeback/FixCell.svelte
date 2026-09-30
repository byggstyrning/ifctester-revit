<script lang="ts">
    import * as Writeback from "$src/modules/api/writeback.svelte";
    import type { WritebackTarget } from "$src/modules/api/writeback.svelte";
    import type { AuditReportEntity } from "$src/types/report";

    type Props = {
        auditId: string;
        specIndex: number;
        reqIndex: number;
        rowIndex: number;
        target: WritebackTarget;
        entity: AuditReportEntity;
    };

    let { auditId, specIndex, reqIndex, rowIndex, target, entity }: Props = $props();

    const globalId = $derived(entity.global_id && entity.global_id !== '-' ? entity.global_id : null);
    const change = $derived(globalId ? Writeback.getChange(globalId, specIndex, reqIndex) : undefined);
    // A requirement with exactly one allowed value prefills it; the user still has to add it
    const prefill = $derived(target.supported && target.allowedValues.length === 1 ? target.allowedValues[0] : '');
    const listId = $derived(`wb-values-${specIndex}-${reqIndex}-${rowIndex}`);

    // What the user typed before it became a pending change
    let draft = $state<string | null>(null);
    const value = $derived(change ? change.value : (draft ?? prefill));

    const source = $derived({ auditId, specIndex, reqIndex, target });

    function handleInput(event: Event) {
        const typed = (event.currentTarget as HTMLInputElement).value;
        if (change) {
            // Emptying the field removes the change; keep the field empty rather than prefilled
            draft = typed.trim() === '' ? '' : null;
            Writeback.setChange(source, entity, typed);
        } else {
            draft = typed;
        }
    }

    function commit() {
        if (change || value.trim() === '') return;
        Writeback.setChange(source, entity, value);
        draft = null;
    }

    // Typed values become pending on Enter or when the field loses focus; a prefilled value needs the Add button.
    // Not the change event: the field is controlled, and a browser does not fire change for a value set by script.
    function commitTyped() {
        if (draft !== null) commit();
    }

    function handleKeydown(event: KeyboardEvent) {
        if (event.key === 'Enter') {
            event.preventDefault();
            commitTyped();
        }
    }

    function remove() {
        if (change) Writeback.removeChange(change.key);
        draft = null;
    }
</script>

{#if !target.supported}
    <span class="fix-reason" title={target.reason}>{target.reason}</span>
{:else if !globalId}
    <span class="fix-reason" title="Revit finds elements by GlobalId">No GlobalId</span>
{:else}
    <div class="fix-cell" class:pending={!!change} class:failed={!!change?.error}>
        <input
            type="text"
            class="fix-input"
            list={target.allowedValues.length > 0 ? listId : undefined}
            aria-label="Fix value for {entity.name || globalId}"
            placeholder="New value"
            {value}
            oninput={handleInput}
            onkeydown={handleKeydown}
            onblur={commitTyped}
        />
        {#if target.allowedValues.length > 0}
            <datalist id={listId}>
                {#each target.allowedValues as allowed}
                    <option value={allowed}></option>
                {/each}
            </datalist>
        {/if}
        {#if change}
            <button type="button" class="fix-btn" onclick={remove} title="Remove this pending change" aria-label="Remove pending change for {entity.name || globalId}">
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round">
                    <path d="M18 6L6 18M6 6l12 12"/>
                </svg>
            </button>
        {:else}
            <button type="button" class="fix-btn add" onclick={commit} disabled={value.trim() === ''} title="Add to pending changes" aria-label="Add fix for {entity.name || globalId}">
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round">
                    <path d="M12 5v14M5 12h14"/>
                </svg>
            </button>
        {/if}
    </div>
    {#if change?.error}
        <div class="fix-error" title={change.error}>{change.error}</div>
    {/if}
{/if}

<style>
    .fix-reason {
        display: block;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        max-width: 190px;
        color: #9ca3af;
        font-style: italic;
    }

    .fix-cell {
        display: flex;
        align-items: center;
        gap: 4px;
    }

    .fix-input {
        width: 130px;
        height: 24px;
        padding: 0 6px;
        background: #ffffff0a;
        border: 1px solid #e5e7eb24;
        border-radius: 0.25rem;
        color: #e0e0e0;
        font-size: 12px;
        transition: all 0.2s;
    }

    .fix-input:hover {
        border-color: #e5e7eb40;
    }

    .fix-input:focus {
        outline: none;
        border-color: #10b981;
    }

    .fix-cell.pending .fix-input {
        border-color: #10b981;
        background: #10b98115;
        color: #79ecb7;
    }

    .fix-cell.failed .fix-input {
        border-color: #ff7171;
        background: #ef444415;
        color: #ffb4b4;
    }

    .fix-btn {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 24px;
        height: 24px;
        flex-shrink: 0;
        background: #ffffff12;
        color: #b0b0b0;
        border: none;
        border-radius: 0.25rem;
        cursor: pointer;
        transition: all 0.2s;
    }

    .fix-btn:hover:not(:disabled) {
        background: #ffffff1f;
        color: #ff7171;
    }

    .fix-btn.add {
        background: #12613d;
        color: white;
    }

    .fix-btn.add:hover:not(:disabled) {
        background: #197148;
        color: white;
    }

    .fix-btn:disabled {
        background: #ffffff0a;
        color: #6b7280;
        cursor: not-allowed;
    }

    .fix-error {
        margin-top: 2px;
        max-width: 190px;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        color: #ff8282;
        font-size: 11px;
    }
</style>
