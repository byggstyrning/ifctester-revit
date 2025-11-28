import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import path from "path";

export default defineConfig({
    // Use relative paths for file:// compatibility (ArchiCAD embedded browser)
    base: './',
    plugins: [
        tailwindcss(), 
        svelte({
            compilerOptions: {
                // Suppress accessibility warnings in production builds
                accessors: true,
            },
            onwarn: (warning, handler) => {
                // Suppress accessibility warnings during build
                if (warning.code?.startsWith('a11y-')) {
                    return;
                }
                handler(warning);
            }
        })
    ],
    resolve: {
        alias: {
            $lib: path.resolve("./src/lib"),
            $src: path.resolve("./src"),
        },
    },
    server: {
        host: '0.0.0.0', // Listen on all network interfaces
        port: 5173,
        strictPort: false, // Allow port to be changed if 5173 is taken
        allowedHosts: [
            'validate.byggstyrning.se',
            'localhost',
            '.local',
        ],
    },
    build: {
        // Suppress warnings in build output
        logLevel: 'error',
    },
});