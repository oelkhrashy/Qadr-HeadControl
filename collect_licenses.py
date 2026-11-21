"""Copy available dependency notices into a native distribution; no network use."""
from pathlib import Path
from importlib import metadata
import argparse
import shutil
import sys

p = argparse.ArgumentParser()
p.add_argument("destination", type=Path)
p.add_argument("--nuget", type=Path)
args = p.parse_args()
dest = args.destination / "licenses"
dest.mkdir(parents=True, exist_ok=True)
inventory = ["# Dependency inventory", "", "Built with Python " + sys.version.split()[0], "", "Installed worker build dependencies are listed, including build-only tools.", "This collection does not replace a redistribution/license audit.", ""]
for dist in sorted(metadata.distributions(), key=lambda d: d.metadata["Name"].lower()):
    name = dist.metadata["Name"]
    inventory.append(f"- {name} {dist.version}")
    for entry in dist.files or []:
        parts = Path(str(entry)).parts
        if any(part.startswith("..") for part in parts):
            continue
        if any(word in Path(str(entry)).name.lower() for word in ("license", "copying", "notice", "copyright")):
            src = Path(dist.locate_file(entry))
            if src.is_file():
                target = dest / "python-packages" / name / Path(str(entry))
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(src, target)
project = Path(__file__).resolve().parent
shutil.copy2(project.parent / "licenses" / "srt-LICENSE", dest / "srt-LICENSE")
runtime_license = Path(sys.base_prefix) / "LICENSE.txt"
if runtime_license.exists():
    shutil.copy2(runtime_license, dest / "Python-LICENSE.txt")
if args.nuget:
    for package in args.nuget.iterdir():
        if not package.is_dir():
            continue
        for version in package.iterdir():
            if not version.is_dir():
                continue
            for entry in version.iterdir():
                if entry.is_file() and any(word in entry.name.lower() for word in ("license", "notice", "thirdparty")):
                    target = dest / "dotnet-packages" / package.name / version.name / entry.name
                    target.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(entry, target)
(dest / "DEPENDENCIES.md").write_text("\n".join(inventory) + "\n", encoding="utf-8")
