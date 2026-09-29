"""Shared normalisation of an ifctester Json report into a small, stable summary."""


def summarize(report: dict) -> list[dict]:
    out = []
    for spec in report["specifications"]:
        out.append(
            {
                "name": spec["name"],
                "status": spec["status"],
                "is_skipped": spec.get("is_skipped", False),
                "applicable": spec["total_applicable"],
                "applicable_fail": spec["total_applicable_fail"],
                "requirements": [[r["facet_type"], r["status"], r["total_fail"]] for r in spec["requirements"]],
            }
        )
    return out
