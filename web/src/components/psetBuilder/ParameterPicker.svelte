<script lang="ts">
    // A Revit parameter field for the pset builder: free text (a name, or BuiltInParameter.X),
    // with a dropdown of every parameter the model has once those are loaded. Typing filters the
    // list; the chevron shows all of it. The list is a popover so the table's scrolling box does
    // not clip it.
    import type { ModelParameter } from "$src/modules/psetBuilder/psetFile";

    type Props = {
        value: string;
        parameters: ModelParameter[] | null;
        label: string;
        pickLabel: string;
        placeholder: string;
        onchange: (value: string) => void;
    };
    let { value, parameters, label, pickLabel, placeholder, onchange }: Props = $props();

    /** Rendering thousands of rows makes the list slow to open; typing narrows it. */
    const SHOWN = 200;
    const listId = $props.id();

    let input = $state<HTMLInputElement>();
    let list = $state<HTMLUListElement>();
    let open = $state(false);
    // What was typed since the list opened; empty shows every parameter
    let query = $state("");
    let active = $state(-1);

    const matches = $derived.by(() => {
        if (!parameters) return [];
        const needle = query.trim().toLowerCase();
        const hit = (p: ModelParameter) => !needle || p.name.toLowerCase().includes(needle) || !!p.builtInParameter?.toLowerCase().includes(needle);
        const starts = (p: ModelParameter) => (needle && p.name.toLowerCase().startsWith(needle) ? 0 : 1);
        return parameters.filter(hit).sort((a, b) => starts(a) - starts(b) || a.name.localeCompare(b.name));
    });
    const shown = $derived(matches.slice(0, SHOWN));

    const categories = (p: ModelParameter) => (p.categories.length > 3 ? `${p.categories.slice(0, 3).join(", ")} +${p.categories.length - 3}` : p.categories.join(", "));
    const describe = (p: ModelParameter) =>
        [p.scope, p.origin, p.builtInParameter ? `BuiltInParameter.${p.builtInParameter}` : "", p.elementCount > 0 ? `${p.elementCount} elements` : "bound, on no element yet", categories(p), p.readOnly ? "read-only" : ""]
            .filter(Boolean)
            .join(" · ");

    function show(filter: string) {
        if (!parameters || !input || !list) return;
        query = filter;
        active = -1;
        const box = input.getBoundingClientRect();
        const below = window.innerHeight - box.bottom;
        list.style.left = `${box.left}px`;
        list.style.minWidth = `${Math.max(box.width, 280)}px`;
        // Open upwards when there is little room under the field
        if (below < 220 && box.top > below) {
            list.style.top = "";
            list.style.bottom = `${window.innerHeight - box.top + 2}px`;
            list.style.maxHeight = `${Math.min(320, box.top - 8)}px`;
        } else {
            list.style.bottom = "";
            list.style.top = `${box.bottom + 2}px`;
            list.style.maxHeight = `${Math.min(320, below - 8)}px`;
        }
        if (!open) list.showPopover();
        open = true;
    }

    function hide() {
        if (!open) return;
        open = false;
        list?.hidePopover();
    }

    function pick(p: ModelParameter) {
        if (input) input.value = p.name;
        hide();
        onchange(p.name);
    }

    function keydown(e: KeyboardEvent) {
        if (!parameters) return;
        if (e.key === "ArrowDown" || e.key === "ArrowUp") {
            e.preventDefault();
            if (!open) return show("");
            const step = e.key === "ArrowDown" ? 1 : -1;
            active = Math.max(0, Math.min(shown.length - 1, active + step));
        } else if (e.key === "Enter" && open && shown[active]) {
            e.preventDefault();
            pick(shown[active]);
        } else if (e.key === "Escape" && open) {
            e.preventDefault();
            hide();
        }
    }

    $effect(() => {
        if (open && active >= 0) list?.children[active]?.scrollIntoView({ block: "nearest" });
    });

    // The list is placed for where the field was: close it when anything else scrolls or resizes
    $effect(() => {
        if (!open) return;
        const close = (e: Event) => {
            if (e.target !== list) hide();
        };
        window.addEventListener("scroll", close, true);
        window.addEventListener("resize", close);
        return () => {
            window.removeEventListener("scroll", close, true);
            window.removeEventListener("resize", close);
        };
    });
</script>

<div class="picker">
    <input
        bind:this={input}
        role={parameters ? "combobox" : undefined}
        aria-label={label}
        aria-expanded={parameters ? open : undefined}
        aria-controls={parameters ? listId : undefined}
        aria-autocomplete={parameters ? "list" : undefined}
        aria-activedescendant={open && active >= 0 ? `${listId}-${active}` : undefined}
        autocomplete="off"
        spellcheck="false"
        {placeholder}
        {value}
        oninput={(e) => show(e.currentTarget.value)}
        onkeydown={keydown}
        onblur={hide}
        onchange={(e) => onchange(e.currentTarget.value)}
    />
    {#if parameters}
        <button
            class="chevron"
            tabindex="-1"
            aria-label={pickLabel}
            onmousedown={(e) => e.preventDefault()}
            onclick={() => {
                if (open) return hide();
                input?.focus();
                show("");
            }}>▾</button>
        <ul bind:this={list} id={listId} role="listbox" popover="manual" class="list scrollbar" aria-label={pickLabel}>
            <!-- Not keyed: a model can hold two shared parameters with one name (different GUIDs) -->
            {#each shown as p, i}
                <!-- The keyboard stays in the input, which moves the active option (aria-activedescendant) -->
                <!-- svelte-ignore a11y_click_events_have_key_events -->
                <li
                    id="{listId}-{i}"
                    role="option"
                    aria-selected={i === active}
                    class:active={i === active}
                    class:current={p.name === value}
                    onmousedown={(e) => e.preventDefault()}
                    onclick={() => pick(p)}
                    onmouseenter={() => (active = i)}
                >
                    <span class="name">{p.name}</span>
                    <span class="meta">{describe(p)}</span>
                </li>
            {:else}
                <li class="empty">{parameters?.length ? `No model parameter matches "${query}"` : "The model has no parameters to list"}</li>
            {/each}
            {#if matches.length > shown.length}
                <li class="empty">{matches.length - shown.length} more: type to narrow the list</li>
            {/if}
        </ul>
    {/if}
</div>

<style>
    .picker {
        position: relative;
        display: flex;
        flex: 1;
        min-width: 0;
    }

    input {
        flex: 1;
        min-width: 0;
        padding: 0.25rem 1.5rem 0.25rem 0.375rem;
        background: #ffffff0a;
        border: 1px solid var(--picker-border, #e5e7eb24);
        border-radius: 0.25rem;
        color: #ffffffd9;
        font-size: 0.75rem;
    }

    .chevron {
        position: absolute;
        right: 0;
        top: 0;
        bottom: 0;
        width: 1.375rem;
        background: none;
        border: none;
        color: #ffffffb3;
        cursor: pointer;
        font-size: 0.75rem;
    }

    .chevron:hover {
        color: #ffffff;
    }

    .list {
        position: fixed;
        inset: auto;
        margin: 0;
        padding: 0.25rem 0;
        overflow-y: auto;
        list-style: none;
        background: #1f2937;
        border: 1px solid #e5e7eb33;
        border-radius: 0.375rem;
        box-shadow: 0 8px 24px #00000080;
        color: #ffffffd9;
        font-size: 0.75rem;
    }

    li {
        display: flex;
        flex-direction: column;
        padding: 0.25rem 0.5rem;
        cursor: pointer;
    }

    li.active {
        background: #3b82f640;
    }

    li.current .name {
        color: #93c5fd;
    }

    .meta {
        color: #9ca3af;
        font-size: 0.6875rem;
    }

    li.empty {
        color: #9ca3af;
        cursor: default;
    }
</style>
