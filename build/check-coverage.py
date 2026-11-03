#!/usr/bin/env python3
"""Fail when line coverage of an assembly drops below its threshold.

Reads a merged Cobertura report (from ReportGenerator) and compares the line
coverage of each <package> (one per assembly) with a minimum percentage.

    python3 build/check-coverage.py coverage/Cobertura.xml Timeglass.Core=90 Timeglass.Storage=80

An assembly that is missing from the report has no coverable lines yet (for
example a project that only holds a marker type). It is reported and skipped,
not failed. Assemblies without a threshold are not checked.

When GITHUB_STEP_SUMMARY is set, a results table is appended to the job summary.
"""

from __future__ import annotations

import os
import sys
import xml.etree.ElementTree as ET


def parse_thresholds(args: list[str]) -> dict[str, float]:
    thresholds: dict[str, float] = {}
    for arg in args:
        name, sep, value = arg.partition("=")
        if not sep or not name:
            raise SystemExit(f"Invalid threshold '{arg}', expected Assembly=percent")
        thresholds[name] = float(value)
    return thresholds


def read_line_coverage(report: str) -> dict[str, float]:
    root = ET.parse(report).getroot()
    return {
        package.attrib["name"]: float(package.attrib["line-rate"]) * 100
        for package in root.iter("package")
    }


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print(__doc__, file=sys.stderr)
        return 2

    coverage = read_line_coverage(argv[0])
    thresholds = parse_thresholds(argv[1:])

    rows: list[str] = []
    failures = 0
    for name, minimum in thresholds.items():
        actual = coverage.get(name)
        if actual is None:
            status = "skipped (no coverable lines)"
            print(f"::notice::{name}: no coverable lines, threshold {minimum:g}% not checked")
            rows.append(f"| {name} | n/a | {minimum:g}% | :white_circle: {status} |")
            continue

        if actual + 1e-9 < minimum:
            failures += 1
            print(f"::error::{name}: line coverage {actual:.1f}% is below the {minimum:g}% threshold")
            rows.append(f"| {name} | {actual:.1f}% | {minimum:g}% | :x: below threshold |")
        else:
            print(f"{name}: line coverage {actual:.1f}% (threshold {minimum:g}%)")
            rows.append(f"| {name} | {actual:.1f}% | {minimum:g}% | :white_check_mark: ok |")

    summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary_path:
        with open(summary_path, "a", encoding="utf-8") as summary:
            summary.write("### Coverage thresholds\n\n")
            summary.write("| Assembly | Line coverage | Threshold | Result |\n")
            summary.write("| --- | ---: | ---: | --- |\n")
            summary.write("\n".join(rows) + "\n\n")

    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
