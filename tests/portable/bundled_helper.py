"""Exercise the packaged macOS helper and its actual bundled Python runtime."""
import json
from pathlib import Path
import shlex
import subprocess
import sys
import tempfile

ROOT=Path(__file__).resolve().parents[2]
resources=Path(sys.argv[1]).resolve()/'Contents/Resources'
with tempfile.TemporaryDirectory() as temporary:
    folder=Path(temporary)
    fake=folder/'codex'
    fake.write_text('#!/bin/sh\nexec '+shlex.quote(sys.executable)+' '+shlex.quote(str(ROOT/'tests/portable/fake_codex.py'))+'\n')
    fake.chmod(0o755)
    requests=[{'id':index,'op':'optimize','draft':'IMU、UDP 与机械臂 '+mode,
               'settings':{'codexPath':str(fake),'lengthStyle':mode}} for index,mode in enumerate(('brief','moderate','detailed'))]
    result=subprocess.run([str(resources/'askbetter-core'),'--prompts',str(resources/'prompts'),'--config',str(folder/'settings.json')],
        input=''.join(json.dumps(request,ensure_ascii=False)+'\n' for request in requests),text=True,encoding='utf-8',capture_output=True,timeout=30)
    assert result.returncode == 0,result.stderr
    replies=[json.loads(line) for line in result.stdout.splitlines()]
    assert len(replies) == len(requests),replies
    for before,after in zip(requests,replies):
        assert after['id'] == before['id'] and after['ok'] and after['prompt']=='优化：'+before['draft'],after
    print('Bundled macOS helper: all three modes and UTF-8 replies passed')
