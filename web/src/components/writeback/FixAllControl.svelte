<script lang="ts">
    import * as Writeback from "$src/modules/api/writeback.svelte";
    import type { WritebackTarget } from "$src/modules/api/writeback.svelte";
    import type { AuditRequirement } from "$src/types/report";

    type Props = {
        auditId: string;
        specIndex: number;
        reqIndex: number;
        target: WritebackTarget;
        requirement: AuditRequirement;
    };

    let { auditId, specIndex, reqIndex, target, requirement }: Props = $props();

    // Every failed element in the report, not only the rows the table shows
    const entities = $derived(
        (requirement.failed_entities ?? []).filter((entity) => entity.global_id && entity.global_id !== '-')
    );
    const prefill = $derived(target.supported && target.allowedValues.length === 1 ? target.allowedValues[0] : '');
    const pending = $derived(Writeback.requirementPendingCount(specIndex, reqIndex));
    const listId = $derived(`wb-values-${specIndex}-${reqIndex}-all`);

    let draft = $state<string | null>(null);
    const value = $derived(draft ?? prefill);

    function setAll(event: SubmitEvent) {
        event.preventDefault();
        if (value.trim() === '') return;
        Writeback.setChanges({ auditId, specIndex, reqIndex, target }, entities, value);
    }

    function clearAll() {
        Writeback.removeRequirementChanges(specIndex, reqIndex);
    }
</script>

{#if target.supported && entities.length > 1}
    <form class="fix-all" onsubmit={setAll}>
        <label for="{listId}-input">Set all {entities.length} failed to</label>
        <input
            id="{listId}-input"
            type="text"
            class="fix-all-input"
            list={target.allowedValues.length > 0 ? listId : undefined}
            placeholder="New value"
            {value}
            oninput={(event) => draft = event.currentTarget.value}
        />
        {#if target.allowedValues.length > 0}
            <datalist id={listId}>
                {#each target.allowedValues as allowed}
                    <option value={allowed}></option>
                {/each}
            </datalist>
        {/if}
        <button type="submit" class="fix-all-btn primary" disabled={value.trim() === ''}>Set all</button>
        {#if pending > 0}
            <button type="button" class="fix-all-btn" onclick={clearAll}>Clear {pending} pending</button>
        {/if}
        {#if requirement.has_omitted_failures}
            <span class="fix-all-note">{requirement.total_omitted_failures} more failures are not in the report and are not included</span>
        {/if}
    </form>
{/if}

<style>
    .fix-all {
        display: flex;
        align-items: center;
        flex-wrap: wrap;
        gap: 8px;
        margin-bottom: 8px;
        font-size: 12px;
        color: #b0b0b0;
    }

    .fix-all-input {
        width: 160px;
        height: 26px;
        padding: 0 8px;
        background: #ffffff0a;
        border: 1px solid #e5e7eb24;
        border-radius: 0.25rem;
        color: #e0e0e0;
        font-size: 12px;
        transition: all 0.2s;
    }

    .fix-all-input:hover {
        border-color: #e5e7eb40;
    }

    .fix-all-input:focus {
        outline: none;
        border-color: #10b981;
    }

    .fix-all-btn {
        height: 26px;
        padding: 0 10px;
        background: #ffffff12;
        color: #ffffff;
        border: 1px solid #ffffff24;
        border-radius: 0.25rem;
        font-size: 12px;
        font-weight: 500;
        cursor: pointer;
        transition: all 0.2s;
    }

    .fix-all-btn:hover:not(:disabled) {
        background: #ffffff1a;
        border-color: #ffffff40;
    }

    .fix-all-btn.primary {
        background: #12613d;
        border-color: transparent;
    }

    .fix-all-btn.primary:hover:not(:disabled) {
        background: #197148;
    }

    .fix-all-btn:disabled {
        background: #ffffff0a;
        color: #6b7280;
        cursor: not-allowed;
    }

    .fix-all-note {
        color: #9ca3af;
        font-style: italic;
    }
</style>
