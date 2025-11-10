# Mixed Content Solution for Revit Integration

## Problem
When accessing `https://validate.byggstyrning.se` (HTTPS), browsers block requests to `http://10.13.42.120:48884` (HTTP) due to mixed content security policy.

## Solutions

### Option 1: HTTP Direct Access (Recommended - Works for Everyone)

**Simplest solution:** Use HTTP for the hosted page instead of HTTPS.

**Pros:**
- No proxy setup needed
- Works for all users immediately
- No Cloudflare Tunnel configuration required

**Cons:**
- Page is served over HTTP (less secure, but acceptable for internal tools)

**Implementation:**
1. Set `USE_HTTPS = False` in the Revit script
2. Host the app on HTTP (or use Vite dev server)
3. Use direct HTTP API endpoint: `http://10.13.42.120:48884/ifc-validator`

**Current:** This is the default setting in `script.py`

### Option 2: Cloudflare Tunnel Proxy (Per-User Setup)

**Requires:** Each user sets up their own Cloudflare Tunnel

**Pros:**
- Full HTTPS
- Secure end-to-end

**Cons:**
- Each user must configure Cloudflare Tunnel
- Complex setup

**Setup:**
1. Configure Cloudflare Tunnel to proxy `/revit-api/*` to local Revit API
2. Set `USE_HTTPS = True` in Revit script
3. Use proxied URL: `https://validate.byggstyrning.se/revit-api/ifc-validator`

### Option 3: HTTPS Proxy Server (Future)

Create a small HTTPS proxy server that runs alongside Revit. This would require:
- Self-signed certificate generation
- Browser certificate acceptance
- More complex setup

**Not recommended** - Option 1 is simpler.

## Current Implementation

The Revit script now defaults to **Option 1 (HTTP Direct Access)**, which:
- ✅ Works immediately for all users
- ✅ No proxy configuration needed
- ✅ Uses Vite dev server or HTTP hosting
- ✅ Direct connection to Revit API

To switch to HTTPS proxy:
1. Set `USE_HTTPS = True` in `script.py`
2. Configure Cloudflare Tunnel proxy
3. Update hosted page URL to HTTPS

