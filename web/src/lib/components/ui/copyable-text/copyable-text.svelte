<script>
    import * as Tooltip from "$src/lib/components/ui/tooltip";
    import { toast } from "svelte-sonner";

    let { 
        value = '',
        truncate = true,
        showToast = true,
        successMessage = 'Copied to clipboard'
    } = $props();

    let copied = $state(false);

    async function copyToClipboard() {
        if (!value || value === '-') return;
        
        try {
            await navigator.clipboard.writeText(value);
            copied = true;
            if (showToast) {
                toast.success(successMessage, {
                    description: value
                });
            }
            
            setTimeout(() => {
                copied = false;
            }, 1000);
        } catch (err) {
            console.error('Failed to copy:', err);
        }
    }
</script>

<Tooltip.Root>
    <Tooltip.Trigger>
        <button 
            class="copyable-text" 
            class:truncated={truncate}
            class:copied={copied}
            onclick={copyToClipboard}
            type="button"
        >
            {value || '-'}
        </button>
    </Tooltip.Trigger>
    <Tooltip.Content>
        <p>{copied ? '✓ Copied!' : value || '-'}</p>
    </Tooltip.Content>
</Tooltip.Root>

<style>
    .copyable-text {
        width: 100%;
        background: none;
        border: none;
        color: inherit;
        padding: 0;
        font: inherit;
        text-align: left;
        cursor: pointer;
        transition: color 0.15s ease;
        position: relative;
    }

    .copyable-text.truncated {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        display: block;
    }

    .copyable-text:hover {
        color: #79ecb7;
    }

    .copyable-text.copied {
        color: #10b981;
        animation: pulse 0.3s ease;
    }

    @keyframes pulse {
        0%, 100% {
            transform: scale(1);
        }
        50% {
            transform: scale(1.02);
        }
    }
</style>

