#!/usr/bin/env python3
"""Fails if any tracked XML file (manifests, project files, XAML, run settings) is not well-formed.

A malformed app.manifest still builds but Windows refuses to start the exe ("side-by-side configuration is
incorrect"), so this runs on every CI build.
"""
import subprocess
import sys
import xml.dom.minidom

EXTENSIONS = (".manifest", ".csproj", ".props", ".targets", ".xaml", ".runsettings", ".xml")
files = [f for f in subprocess.check_output(["git", "ls-files"], text=True).splitlines() if f.endswith(EXTENSIONS)]
bad = []
for f in files:
    try:
        xml.dom.minidom.parse(f)
    except Exception as e:  # noqa: BLE001 - report every parse failure
        bad.append(f"{f}: {e}")
print("\n".join(bad) if bad else f"OK: {len(files)} XML files are well-formed.")
sys.exit(1 if bad else 0)
