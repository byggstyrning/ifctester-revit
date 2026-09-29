import json
from pathlib import Path

import ifcopenshell
import ifctester.ids
import ifctester.reporter

from normalize import summarize


def audit(ifc_path: Path, ids_path: Path) -> list[dict]:
    ifc = ifcopenshell.open(str(ifc_path))
    ids = ifctester.ids.open(str(ids_path))
    ids.validate(ifc)
    reporter = ifctester.reporter.Json(ids)
    reporter.report()
    return summarize(json.loads(reporter.to_string()))
