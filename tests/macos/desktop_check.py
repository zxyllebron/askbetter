"""Probe real macOS Accessibility against a separate native editor fixture."""
import json
from pathlib import Path
import plistlib
import subprocess
import sys
import tempfile
import time

ROOT=Path(__file__).resolve().parents[2]
app=Path(sys.argv[1]).resolve()
with tempfile.TemporaryDirectory() as temporary:
    folder=Path(temporary)
    fixture=folder/'Codex.app'
    (fixture/'Contents/MacOS').mkdir(parents=True)
    (fixture/'Contents/Info.plist').write_bytes(plistlib.dumps({'CFBundleName':'Codex','CFBundleExecutable':'Codex',
        'CFBundleIdentifier':'io.github.zxyllebron.askbetter.fixture','CFBundlePackageType':'APPL'}))
    subprocess.run(['swiftc',str(ROOT/'tests/macos/Fixture.swift'),'-o',str(fixture/'Contents/MacOS/Codex')],check=True)
    child=subprocess.Popen([str(fixture/'Contents/MacOS/Codex'),str(folder)])
    try:
        deadline=time.monotonic()+15
        while not (folder/'ready.json').exists():
            if child.poll() is not None or time.monotonic()>deadline:
                raise AssertionError('Native fixture did not launch')
            time.sleep(0.1)
        pid=json.loads((folder/'ready.json').read_text())['pid']
        subprocess.run([str(app/'Contents/MacOS/AskBetter'),'--desktop-probe',str(pid),str(folder)],check=True,timeout=30)
        result=json.loads((folder/'probe.json').read_text())
        assert result['read'] and result['replacement'] and result['changedDraftRejected'] and result['titleChangeRejected'],result
        assert not (folder/'sent.txt').exists(),'The fixture Send button was clicked'
        result['neverSent']=True
        result['fixture']='Separate native AppKit editor; not a signed-in Codex client'
        result['realCodex']=False
        output=ROOT/'artifacts/tests/macos-accessibility.json'
        output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
        print(json.dumps(result,ensure_ascii=False))
    finally:
        child.terminate()
        child.wait(timeout=5)
