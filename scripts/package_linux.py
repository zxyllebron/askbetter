"""Build a source-based Ubuntu package without copying settings or account data."""
import argparse
from pathlib import Path
import shutil
import tarfile
import tempfile
import zipfile

ROOT=Path(__file__).resolve().parent.parent
UUID="askbetter@zxyllebron.github.io"


def build(output):
    output=Path(output).resolve()
    output.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory() as temporary:
        package=Path(temporary)/"AskBetter-Ubuntu-24.04"
        extension=package/"extension"
        extension.mkdir(parents=True)
        for name in ("metadata.json","extension.js","wand.svg"):
            shutil.copy2(ROOT/"linux/gnome-extension"/name,extension/name)
        shutil.copytree(ROOT/"linux/gnome-extension/schemas",extension/"schemas")
        for directory,names in (("crossplatform",("__init__.py","core.py","geometry.py")),
                                ("linux",("__init__.py","accessibility.py","service.py"))):
            (extension/directory).mkdir()
            for name in names:
                shutil.copy2(ROOT/directory/name,extension/directory/name)
        (extension/"prompts").mkdir()
        for path in (ROOT/"askbetter").glob("optimizer-prompt*.txt"):
            shutil.copy2(path,extension/"prompts"/path.name)
        for source,target in (("linux/install.sh","install.sh"),("linux/run.sh","run.sh"),
                              ("linux/README.md","README.md"),("LICENSE","LICENSE")):
            shutil.copy2(ROOT/source,package/target)
        for path in (package/"install.sh",package/"run.sh"):
            path.chmod(0o755)
        for path in package.rglob("*"):
            if path.is_dir():
                path.chmod(0o755)
            elif path.suffix not in (".sh",):
                path.chmod(0o644)
        archive=output/"AskBetter-Ubuntu-24.04-GNOME.tar.gz"
        with tarfile.open(archive,"w:gz") as stream:
            stream.add(package,arcname=package.name)
        print(archive)
        return archive


if __name__ == "__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("--output",default=str(ROOT/"artifacts/linux"))
    build(parser.parse_args().output)
