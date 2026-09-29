"""Native (desktop) ifctester must reproduce the committed expected results.

The same expected-*.json files are checked against the in-browser (Pyodide) audit by the
Playwright suite, so any difference between native and wasm behaviour shows up in CI.
"""
import json
from pathlib import Path

import pytest
from run_audit import audit

FIX = Path(__file__).parent.parent / "fixtures"


@pytest.mark.parametrize("name", ["ifc4", "ifc2x3"])
def test_native_audit_matches_expected(name):
    expected = json.loads((FIX / f"expected-{name}.json").read_text())
    actual = audit(FIX / f"model-{name}.ifc", FIX / f"specs-{name}.ids")
    assert actual == expected


def test_prohibited_spec_semantics():
    """ifctester >= 0.9: a prohibited spec fails when applicable elements exist, passes when none do."""
    by_name = {s["name"]: s for s in json.loads((FIX / "expected-ifc4.json").read_text())}
    assert by_name["no doors allowed"]["status"] is False
    assert by_name["no doors allowed"]["requirements"] == []
    assert by_name["no columns allowed"]["status"] is True
