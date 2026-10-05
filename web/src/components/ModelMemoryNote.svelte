<script lang="ts">
    // What was last used for the model open in Revit, as the add-in remembers it per model: the IDS,
    // the export setup and the property set file overrides. They are preselected when the page
    // connects (see applyModelMemory); this shows them, says which could not be, and lets the user
    // pick another IDS through Revit's file dialog so that one is remembered instead.
    import { Revit, fileNameOf, openIdsThroughRevit } from "$src/modules/api/revit.svelte.js";
    import { error } from "$src/modules/utils/toast.svelte";

    let picking = $state(false);

    const memory = $derived(Revit.memory);
    const remembered = $derived(memory?.remembered ?? null);

    // The remembered choices, in the order the user meets them
    const parts = $derived(
        remembered
            ? [
                  remembered.idsFile ? { label: remembered.idsFile.name, title: remembered.idsFile.path, missing: !remembered.idsFile.exists } : null,
                  remembered.configuration ? { label: remembered.configuration, title: "IFC export setup", missing: false } : null,
                  remembered.psetFile
                      ? { label: remembered.psetFile.name, title: remembered.psetFile.path, missing: !remembered.psetFile.exists }
                      : remembered.configuration
                        ? { label: "the setup's own property set file", title: "No property set file override", missing: false }
                        : null,
                  remembered.parameterMappingFile
                      ? { label: remembered.parameterMappingFile.name, title: remembered.parameterMappingFile.path, missing: !remembered.parameterMappingFile.exists }
                      : null
              ].filter((part) => part !== null)
            : []
    );

    async function change() {
        picking = true;
        try {
            await openIdsThroughRevit();
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            if (message !== "File selection cancelled") error(`Error opening file: ${message}`);
        } finally {
            picking = false;
        }
    }
</script>

{#if memory}
    <div class="memory" aria-label="Model memory">
        {#if memory.loading}
            <p class="line muted">Checking what was last used for this model...</p>
        {:else if !memory.model}
            <p class="line muted">{memory.message ?? "No saved model is open in Revit, so nothing is remembered for it."}</p>
        {:else}
            <p class="line">
                {#if parts.length > 0}
                    <span class="muted">Last used for this model:</span>
                    {#each parts as part, i}
                        <span class="part" class:missing={part.missing} title={part.title}>{part.label}</span>{i < parts.length - 1 ? ", " : ""}
                    {/each}
                {:else}
                    <span class="muted">Nothing remembered for {memory.model.title ?? fileNameOf(memory.model.key)} yet. The IDS you open and the export you run are preselected next time.</span>
                {/if}
                <button type="button" class="change" onclick={change} disabled={picking} title="Open an IDS through Revit's file dialog; it is remembered for this model">
                    {picking ? "choose in Revit..." : remembered?.idsFile ? "change" : "open IDS"}
                </button>
            </p>
            {#each memory.notes as note}
                <p class="line missing" role="status">{note}</p>
            {/each}
        {/if}
    </div>
{/if}

<style>
    .memory {
        display: flex;
        flex-direction: column;
        gap: 0.25rem;
        font-size: 0.75rem;
    }

    .line {
        margin: 0;
        color: #ffffffd9;
        overflow-wrap: anywhere;
    }

    .muted {
        color: #9ca3af;
    }

    .part {
        font-weight: 500;
    }

    .missing {
        color: #f87171;
    }

    .change {
        margin-left: 0.375rem;
        padding: 0;
        background: none;
        border: none;
        color: #60a5fa;
        font-size: 0.75rem;
        cursor: pointer;
    }

    .change:hover:not(:disabled) {
        text-decoration: underline;
    }

    .change:disabled {
        color: #6b7280;
        cursor: wait;
    }
</style>
