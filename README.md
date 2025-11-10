Welcome to the Web AEC Tech Workshop! This repository contains code samples and exercises to help you get started building web-powered plugins for architecture, engineering, and construction (AEC) applications like Rhino and Revit using WebView2.

Make sure to check out the [PREREQUISITES.md](PREREQUISITES.md) file to set up your development environment before diving into the exercises.

# 1. Intro to WebView2 in AEC Plugins

_Estimated time: 20 minutes_

WebView2 is the bridge that lets us ship web-grade user experiences inside desktop design tools that architects and engineers already rely on. By embedding a Chromium-based surface inside Revit and Rhino, we can reuse our React component library, move faster on UI iteration, and keep feature parity across desktop and browser workflows.

## What is WebView2?

WebView2 is a Microsoft Edge (Chromium) control that hosts HTML, CSS, and JavaScript inside Windows applications. It supports .NET Framework, .NET 6+, Win32, and WinUI hosts, giving plugin developers an evergreen browser engine with familiar tooling.

Why it fits our scenario:

- We can load any SPA that Vite produces, including the shared frontend in this repository.
- Evergreen runtime updates independently from our add-ins, keeping the browser engine secure and modern.
- The API surface (`CoreWebView2`) exposes navigation events, scripting, and message passing so native code stays in sync with the web layer.
- Edge DevTools attach directly to the embedded view, which simplifies debugging complex UI flows without leaving the host application.

## Why modern plugins want web user interfaces?

- **Shared design system**: Designers and front-end engineers can iterate once and deploy to web, Rhino, and Revit without rewriting controls for WinForms or WPF.
- **Faster iteration**: Hot module reload from Vite lets us see UI changes instantly while the plugin runs, cutting feedback cycles down to seconds.
- **Richer interactions**: Browser technologies (WebGL, Canvas, WebAssembly) unlock visualizations and geometry inspectors that are time-consuming to build natively.
- **Easier distribution**: Shipping static assets with the plugin keeps deployment lightweight while still enabling runtime feature flags or remote content when needed.
- **Future portability**: Investing in web tech now sets us up to target additional hosts (Blender, standalone Electron, MAUI) with minimal refactoring.

## How do we implement web views into Rhino and Revit?

### Rhino

1. Instantiate a hosting window (`WebAecRhinoView.cs`) that embeds a `WebView2` control using Rhino 8's .NET 7 support.
2. During development, point the control to the Vite dev server so UI changes appear immediately.
3. For packaged builds, bundle the compiled `frontend/dist` assets under `EmbeddedResources` and serve them from disk to keep the plugin offline-friendly.
4. Use commands defined in `WebAecRhinoCommand.cs` to open the view and register handlers that react to messages from the web layer (e.g., geometry selection requests).

### Revit

1. Launch a WPF window (`WebAecRevitView.cs`) from `StartupCommand.cs`, adding a `WebView2` element to host our React app.
2. Verify that the Evergreen WebView2 Runtime is installed; if not, prompt the user before continuing since Revit cannot bootstrap it automatically.
3. Map a virtual host (`app.localhost`) to the add-in's asset folder with `SetVirtualHostNameToFolderMapping` so the SPA loads from clean URLs without hitting the filesystem directly.
4. Keep long-running Revit operations on the native side while streaming progress updates to the web UI to avoid blocking the main thread.

## How do we communicate between our host and webview applications?

- **Native to web**: Call `CoreWebView2.PostWebMessageAsJson` or `ExecuteScriptAsync` to send payloads (selected element IDs, geometry summaries) into the React app.
- **Web to native**: From JavaScript, use `window.chrome.webview.postMessage` to publish JSON messages. The host listens to `WebMessageReceived` and routes commands to Revit or Rhino services.
- **Shared contracts**: Define DTOs/interfaces that live alongside both the React code and the .NET projects so both sides compile against the same schema.
- **Diagnostics**: Mirror important events to the browser console and native logs; attach Edge DevTools for live inspection during workshop exercises.

# 2. Building a Hello WebView2 panel

_Estimated time: 30 minutes_

- [ ] Run the frontend web app
  ```powershell
  cd frontend
  npm install
  npm run dev
  ```
- [ ] Run C# projects (Revit/Rhino)
- [ ] Initialize WebView2 pointing to local development

By the end we should see our webapplication inside Revit or Rhino.

# 3. Host → UI Messaging

_Estimated time: 25 min_

### Host

- [ ] Select objects
- [ ] Extract properties
- [ ] Send data to WebView2

### UI

- [ ] Render properties inside Web UI

Selecting something in the model should update the UI.

# 4. UI → Host Messaging

_Estimated time: 25 min_

Let's implement a button that triggers a command in the host application. The command must edit data.

_BREAK 10 min_

# 5. Let's build a data visualization panel

_Estimated time: 45 min_

- [ ] Extract data from the host application
- [ ] Send data to WebView2
- [ ] Render data visualization using charting library
- [ ] Add interactivity that triggers host commands
