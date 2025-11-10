# Cloudflare Tunnel Configuration for Mixed Content Fix

To fix the mixed content error (HTTPS page trying to access HTTP Revit API), you need to proxy the Revit API through Cloudflare Tunnel.

## Updated Cloudflare Tunnel Config

Edit `%USERPROFILE%\.cloudflared\config.yml`:

```yaml
tunnel: ifc-validator  # or your tunnel name
credentials-file: C:\Users\JonatanJacobsson\.cloudflared\<UUID>.json

ingress:
  # Route validate.byggstyrning.se to Vite dev server
  - hostname: validate.byggstyrning.se
    service: http://10.13.42.120:5173
  
  # Proxy Revit API requests through tunnel (HTTPS)
  # This allows HTTPS page to access Revit API via HTTPS
  - hostname: validate.byggstyrning.se
    path: /revit-api/*
    service: http://10.13.42.120:48884
    originRequest:
      noTLSVerify: true
  
  # Catch-all rule (required)
  - service: http_status:404
```

## Update Revit Script

The Revit script should now pass a proxied URL:

```python
HOSTED_PAGE_URL = "https://validate.byggstyrning.se"  # Use HTTPS!

# Proxy Revit API through Cloudflare Tunnel
api_endpoint = "https://validate.byggstyrning.se/revit-api/ifc-validator"
url = "{}?api={}".format(HOSTED_PAGE_URL, quote(api_endpoint, safe=''))
```

## Update Revit Integration Code

The revit.svelte.js will automatically use the proxied URL if it's provided via the `?api=` parameter.

## Restart Tunnel

After updating config, restart the tunnel:
```bash
cloudflared tunnel run ifc-validator
```

Now all requests go through HTTPS, avoiding mixed content errors!

