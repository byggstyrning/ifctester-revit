import { toast } from "svelte-sonner";

/**
 * Parse IDS validation error to extract useful information
 * @param {Error|string} error - The error object or message
 * @returns {Object|null} - Parsed error info or null if not an IDS validation error
 */
function parseIdsValidationError(error) {
    const errorMessage = error?.message || error?.toString() || String(error);
    const errorStack = error?.stack || '';
    const fullError = errorMessage + '\n' + errorStack;

    // Check if it's an IDS validation error
    if (!fullError.includes('XMLSchema') && !fullError.includes('xmlschema') && !fullError.includes('IDS')) {
        return null;
    }

    // Extract the main error reason
    let reason = '';
    const reasonMatch = fullError.match(/Reason:\s*(.+?)(?:\n|$)/i);
    if (reasonMatch) {
        reason = reasonMatch[1].trim();
    }

    // Extract element/attribute information (more flexible pattern)
    let elementInfo = '';
    const elementMatch = fullError.match(/Element\s+['"]\{[^'"]+\}([^'"]+)['"]/i) || 
                         fullError.match(/Element\s+['"]([^'"]+)['"]/i);
    if (elementMatch) {
        elementInfo = elementMatch[1].trim();
    }

    // Extract expected vs actual
    let expected = '';
    const expectedMatch = fullError.match(/Tag\s+\(([^)]+)\)\s+expected/i);
    if (expectedMatch) {
        expected = expectedMatch[1].trim();
    }

    // Extract property/attribute details from XML instance
    let propertyDetails = '';
    const propertyMatch = fullError.match(/<property[^>]*>[\s\S]*?<baseName>[\s\S]*?<simpleValue>([^<]+)<\/simpleValue>/i);
    if (propertyMatch) {
        propertyDetails = propertyMatch[1].trim();
    }

    // Extract position information
    let position = '';
    const positionMatch = fullError.match(/at position\s+(\d+)/i);
    if (positionMatch) {
        position = positionMatch[1];
    }

    // Build a user-friendly message
    let userMessage = 'IDS Validation Error: The IDS document is not valid.';
    let details = [];

    // Generic IDS validation error
    if (reason) {
        details.push(`Reason: ${reason}`);
    }
    if (elementInfo) {
        details.push(`Element: ${elementInfo}`);
    }
    if (expected) {
        details.push(`Expected: ${expected}`);
    }
    if (propertyDetails) {
        details.push(`Property: ${propertyDetails}`);
    }
    if (position) {
        details.push(`Position: ${position}`);
    }

    return {
        title: userMessage,
        details: details.length > 0 ? details.join('\n') : 'Please check your IDS document structure.',
        fullError: fullError
    };
}

/**
 * Show an IDS validation error toast with orange/warning styling
 * @param {Error|string} error - The error object or message
 */
export function idsValidationError(error) {
    const parsed = parseIdsValidationError(error);
    
    if (!parsed) {
        // Fallback to regular error if not an IDS validation error
        return toast.error(String(error));
    }

    // Use custom toast with orange/warning styling
    // Using toast.warning with className and custom styling
    toast.warning(parsed.title, {
        description: parsed.details,
        className: 'ids-validation-error-toast',
        duration: 10000, // Show for 10 seconds to give user time to read
        style: {
            backgroundColor: '#ff8c00',
            borderColor: '#ff7a00',
            color: '#ffffff'
        }
    });
}

/**
 * Show an error toast notification
 * @param {string} message - The error message to display
 */
export function error(message) {
    toast.error(message);
}

/**
 * Show a success toast notification
 * @param {string} message - The success message to display
 */
export function success(message) {
    toast.success(message);
}

/**
 * Show an info toast notification
 * @param {string} message - The info message to display
 */
export function info(message) {
    toast.info(message);
}

/**
 * Show a warning toast notification
 * @param {string} message - The warning message to display
 */
export function warning(message) {
    toast.warning(message);
}

/**
 * Show a loading toast notification
 * @param {string} message - The loading message to display
 * @returns {string} - Toast ID for dismissing later
 */
export function loading(message) {
    return toast.loading(message);
}

/**
 * Dismiss a specific toast
 * @param {string} toastId - The toast ID to dismiss
 */
export function dismiss(toastId) {
    toast.dismiss(toastId);
}

/**
 * Show a promise-based toast that updates based on promise state
 * @param {Promise} promise - The promise to track
 * @param {Object} messages - Messages for different states
 * @param {string} messages.loading - Loading message
 * @param {string} messages.success - Success message
 * @param {string} messages.error - Error message
 */
export function promise(promiseToTrack, messages) {
    return toast.promise(promiseToTrack, {
        loading: messages.loading,
        success: messages.success,
        error: messages.error,
    });
}