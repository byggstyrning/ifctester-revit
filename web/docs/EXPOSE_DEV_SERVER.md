# Exposing ifctester-next Dev Server via Cloudflare Tunnel

To access `validate.byggstyrning.se` pointing to your local dev server at `http://10.13.42.120:5173`:

## Option 1: Cloudflare Tunnel (Recommended)

### Step 1: Install cloudflared
Download from: https://github.com/cloudflare/cloudflared/releases

### Step 2: Login to Cloudflare
```bash
cloudflared tunnel login
```

### Step 3: Create a tunnel (if you don't have one)
```bash
cloudflared tunnel create ifctester-dev
```

### Step 4: Configure the tunnel
Create/edit `~/.cloudflared/config.yml` (or `%USERPROFILE%\.cloudflared\config.yml` on Windows):

```yaml
tunnel: ifctester-dev
credentials-file: C:\Users\JonatanJacobsson\.cloudflared\<tunnel-id>.json

ingress:
  - hostname: validate.byggstyrning.se
    service: http://10.13.42.120:5173
  - service: http_status:404
```

**Important**: Use `10.13.42.120:5173` (your actual IP) instead of `localhost:5173` so the tunnel can reach your dev server.

### Step 5: Configure DNS
```bash
cloudflared tunnel route dns ifctester-dev validate.byggstyrning.se
```

### Step 6: Start the dev server
```bash
cd C:\code\ifctester-next
npm run dev
```

The dev server should show:
```
  VITE v6.x.x  ready in xxx ms

  ➜  Local:   http://localhost:5173/
  ➜  Network: http://10.13.42.120:5173/
```

### Step 7: Run the tunnel
```bash
cloudflared tunnel run ifctester-dev
```

Now `https://validate.byggstyrning.se` should work!

## Option 2: Port Forwarding (Firewall)

If you're using port forwarding through a router/firewall:

1. **Ensure Vite listens on all interfaces** (already configured in `vite.config.js`)
2. **Open firewall port 5173**:
   ```powershell
   New-NetFirewallRule -DisplayName "Vite Dev Server" -Direction Inbound -LocalPort 5173 -Protocol TCP -Action Allow
   ```
3. **Configure port forwarding** on your router to forward port 5173 to `10.13.42.120:5173`
4. **Access via**: `http://validate.byggstyrning.se:5173` (if domain points to your public IP)

## Troubleshooting

### Dev server not accessible
- Check Vite is listening on `0.0.0.0:5173` (not just localhost)
- Verify firewall allows port 5173
- Test locally: `http://10.13.42.120:5173`

### Cloudflare tunnel not working
- Check tunnel logs: `cloudflared tunnel run ifctester-dev`
- Verify DNS: `nslookup validate.byggstyrning.se`
- Check tunnel status: `cloudflared tunnel info ifctester-dev`

### CORS issues
- The dev server should automatically handle CORS
- Ensure Revit API is accessible from the browser

## Production Deployment

For production, build and deploy:
```bash
npm run build
```

Then deploy `dist/` folder to Cloudflare Pages or your web server.

