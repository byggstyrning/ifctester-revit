# Proxy Solution for Revit API (Mixed Content Fix)

Since browsers block mixed content (HTTPS page accessing HTTP API), we need a proxy solution. Here are the options:

## Option 1: Each User Runs Cloudflare Tunnel (Current)

**Pros:**
- Works immediately
- Secure (HTTPS all the way)

**Cons:**
- Each user must set up their own tunnel
- Requires tunnel configuration

**Setup:** Each user runs their own Cloudflare Tunnel that proxies `/revit-api/*` to their local Revit API.

## Option 2: Server-Side Proxy (Better for Multiple Users)

Create a proxy endpoint in the ifctester-next app that forwards requests to the user's local API.

**Implementation:**
1. Add proxy endpoint: `/api/proxy-revit`
2. User provides their local IP in URL: `?api=http://10.13.42.120:48884/ifc-validator`
3. Proxy endpoint forwards requests to that IP
4. But Cloudflare Pages can't connect to user's localhost...

**Problem:** Cloudflare Pages (serverless) can't connect to user's localhost.

## Option 3: Browser Extension or Local Proxy Server (Most Practical)

**Create a small local proxy server** that:
- Runs on user's machine (like pyRevit Routes)
- Listens on HTTPS locally
- Proxies to Revit API

**Or:** Use a browser extension that can bypass mixed content for localhost.

## Option 4: Use WebSocket or WebRTC (Complex)

Use WebSocket connection to bypass mixed content restrictions.

## Recommended: Option 1 (Cloudflare Tunnel per User)

For now, the simplest solution is for each user to:
1. Install cloudflared
2. Configure tunnel to proxy `/revit-api/*` to their local Revit API
3. Use the proxied URL

This is similar to how the Bonsai integration works - each user runs their own server.

## Alternative: Local HTTPS Proxy

Create a small Python script that runs alongside Revit and:
- Generates self-signed SSL certificate
- Runs HTTPS proxy on localhost:8443
- Proxies to Revit API
- Browsers accept it because it's same-origin-ish

But this is complex...

## Current Solution

The current implementation uses Cloudflare Tunnel proxy. Each user needs to:
1. Set up Cloudflare Tunnel (one-time setup)
2. Configure `/revit-api/*` route to their local Revit API
3. Use the proxied URL

This is similar to how many developer tools work (ngrok, localtunnel, etc.).

