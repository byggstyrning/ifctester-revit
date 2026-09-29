"""Regenerates fixtures/expected-*.json from NATIVE ifcopenshell + ifctester.

Run: python web/tests/python/generate_expected.py   (needs `pip install ifcopenshell ifctester`)
Review the diff before committing: these files define the audit results the app must reproduce.
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from run_audit import audit  # noqa: E402

FIX = Path(__file__).parent.parent / "fixtures"
for name in ("ifc4", "ifc2x3"):
    summary = audit(FIX / f"model-{name}.ifc", FIX / f"specs-{name}.ids")
    (FIX / f"expected-{name}.json").write_text(json.dumps(summary, indent=2) + "\n")
    print(name, len(summary), "specs")
