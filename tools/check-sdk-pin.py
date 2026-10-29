"""Fails when global.json and mise.toml pin different .NET SDK versions.

Both files have to exist: global.json is what the dotnet CLI and actions/setup-dotnet read, and it
carries the Microsoft.Testing.Platform runner setting; mise.toml is what mise users and the
pre-commit hooks resolve the SDK through. Neither tool reads the other's file, so this keeps them
from drifting apart.
"""

import json
import pathlib
import re
import sys

root = pathlib.Path(__file__).resolve().parent.parent

global_json = json.loads((root / "global.json").read_text(encoding="utf-8"))["sdk"]["version"]

match = re.search(r'^\s*dotnet\s*=\s*"([^"]+)"', (root / "mise.toml").read_text(encoding="utf-8"), re.MULTILINE)
mise = match.group(1) if match else None

if global_json != mise:
    print(f"SDK pin mismatch: global.json says {global_json}, mise.toml says {mise}", file=sys.stderr)
    sys.exit(1)

print(f"SDK pin OK: {global_json}")
