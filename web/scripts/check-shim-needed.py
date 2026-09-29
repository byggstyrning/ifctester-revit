"""Watchdog for the ifcopenshell compat shim in public/worker/api.py.

ifctester >= 0.9 needs `entity_instance.get_attribute_category()` etc. If the wasm ifcopenshell wheel
configured in src/config.json already provides them, the shim can be deleted. Emits a GitHub Actions
warning (never fails). Usage: python scripts/check-shim-needed.py [path/to/wheel]
"""
import json
import sys
import urllib.parse
import urllib.request
import zipfile
from io import BytesIO
from pathlib import Path

root = Path(__file__).resolve().parent.parent
wheel_name = Path(json.loads((root / "src/config.json").read_text())["wasm"]["wheel_url"]).name
local = Path(sys.argv[1]) if len(sys.argv) > 1 else root / "public/worker/bin" / wheel_name
if local.exists():
    data = local.read_bytes()
else:
    url = "https://s3.amazonaws.com/ifcopenshell-builds/" + urllib.parse.quote(wheel_name)
    data = urllib.request.urlopen(url, timeout=120).read()

with zipfile.ZipFile(BytesIO(data)) as z:
    source = z.read("ifcopenshell/entity_instance.py").decode()

native = "def get_attribute_category" in source
if native:
    print(f"::warning title=ifcopenshell compat shim no longer needed::{wheel_name} provides entity_instance.get_attribute_category natively; remove install_ifcopenshell_compat() from web/public/worker/api.py")
else:
    print(f"shim still required for {wheel_name}")
