<script>
    import * as IDS from "$src/modules/api/ids.svelte.js";
    import wasm from "$src/modules/wasm";
    import { error, success } from "$src/modules/utils/toast.svelte.js";
    
    let activeDocument = $derived(IDS.Module.activeDocument ? IDS.Module.documents[IDS.Module.activeDocument] : null);
    let validationResult = $state(null);
    let showPreview = $state(false);
    let isValidating = $state(false);

    const getProp = (prop) => {
        return activeDocument?.info[prop] ?? "";
    };
    
    const setProp = (prop, value) => {
        activeDocument.info[prop] = value;
    };
    
    async function handleValidate() {
        if (!activeDocument) {
            error('No document to validate');
            return;
        }
        
        try {
            isValidating = true;
            const doc = $state.snapshot(activeDocument);
            const result = await wasm.validateIDS(doc);
            validationResult = result;
            showPreview = true;
            
            if (result.valid) {
                success('IDS document is valid');
            } else {
                error('IDS document validation failed');
            }
        } catch (err) {
            console.error('Validation error:', err);
            error(`Validation failed: ${err.message}`);
            validationResult = {
                valid: false,
                html: `<div style="padding: 20px; color: #ef4444;"><h2>✗ Validation Error</h2><p>${err.message}</p></div>`
            };
            showPreview = true;
        } finally {
            isValidating = false;
        }
    }
    
    function closePreview() {
        showPreview = false;
    }
</script>

<div class="ids-info">
    <div class="ids-md-header">
        <h2>IDS Information</h2>
        <button class="validate-btn" onclick={handleValidate} disabled={isValidating || !activeDocument}>
            {#if isValidating}
                <svg class="spinner" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M21 12a9 9 0 11-6.219-8.56"/>
                </svg>
                Validating...
            {:else}
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M9 11l3 3L22 4"/>
                    <path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11"/>
                </svg>
                Validate
            {/if}
        </button>
    </div>
    <div class="form-grid">
        <div class="form-group">
            <label>Title</label>
            <input class="form-input" type="text" bind:value={() => getProp("title"), (v) => setProp("title", v)} placeholder="Enter IDS title">
        </div>
        <div class="form-group">
            <label>Author</label>
            <input class="form-input" type="email" bind:value={() => getProp("author"), (v) => setProp("author", v)} placeholder="Enter author">
        </div>
        <div class="form-group">
            <label>Version</label>
            <input class="form-input" type="text" bind:value={() => getProp("version"), (v) => setProp("version", v)} placeholder="Enter version">
        </div>
        <div class="form-group">
            <label>Date</label>
            <input class="form-input" type="date" bind:value={() => getProp("date"), (v) => setProp("date", v)}>
        </div>
        <div class="form-group full-width">
            <label>Description</label>
            <textarea class="form-input" bind:value={() => getProp("description"), (v) => setProp("description", v)} placeholder="Enter description" rows="3"></textarea>
        </div>
        <div class="form-group">
            <label>Purpose</label>
            <input class="form-input" type="text" bind:value={() => getProp("purpose"), (v) => setProp("purpose", v)} placeholder="Enter purpose">
        </div>
        <div class="form-group">
            <label>Milestone</label>
            <input class="form-input" type="text" bind:value={() => getProp("milestone"), (v) => setProp("milestone", v)} placeholder="Enter milestone">
        </div>
        <div class="form-group full-width">
            <label>Copyright</label>
            <input class="form-input" type="text" bind:value={() => getProp("copyright"), (v) => setProp("copyright", v)} placeholder="Enter copyright">
        </div>
    </div>
    
    {#if showPreview && validationResult}
        <div class="validation-preview">
            <div class="preview-header">
                <h3>Validation Results</h3>
                <button class="close-btn" onclick={closePreview} aria-label="Close preview">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <line x1="18" y1="6" x2="6" y2="18"/>
                        <line x1="6" y1="6" x2="18" y2="18"/>
                    </svg>
                </button>
            </div>
            <div class="preview-content">
                {@html validationResult.html}
            </div>
        </div>
    {/if}
</div>