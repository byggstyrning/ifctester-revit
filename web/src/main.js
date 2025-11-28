import { mount } from 'svelte';
import './css/app.css';
import App from './App.svelte';

// Log browser/environment information for debugging
console.log('[IfcTester] Browser Info:', {
    userAgent: navigator.userAgent,
    platform: navigator.platform,
    vendor: navigator.vendor,
    chromeVersion: navigator.userAgent.match(/Chrome\/(\d+)/)?.[1] || 'unknown',
    isCEF: navigator.userAgent.includes('CEF') || navigator.userAgent.includes('Chromium Embedded'),
    location: window.location.href,
    protocol: window.location.protocol
});

const app = mount(App, {
    target: document.getElementById('root'),
});

export default app;