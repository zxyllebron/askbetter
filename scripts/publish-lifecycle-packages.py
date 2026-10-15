"""Publish the tested lifecycle-fix artifacts without a local download/upload hop."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile

source=Path(sys.argv[1])
output=Path('artifacts/publication')
sha=os.environ['SOURCE_SHA']
repo=os.environ['GH_REPO']
run=os.environ['BUILD_RUN']
base='https://github.com/'+repo
expected=('AskBetter-Windows.zip','AskBetter-macOS-arm64.zip',
          'AskBetter-macOS-x86_64.zip','AskBetter-Ubuntu-24.04-GNOME.tar.gz')
for filename in expected:
    found=list(source.rglob(filename))
    assert len(found)==1,(filename,len(found))
    if filename.endswith('.zip'):
        with zipfile.ZipFile(found[0]) as archive:
            assert archive.testzip() is None,filename

reports=output/'Platform-Test-Reports.zip'
output.mkdir(parents=True,exist_ok=True)
with zipfile.ZipFile(reports,'w',compression=zipfile.ZIP_DEFLATED) as archive:
    for path in sorted(source.rglob('*')):
        if path.is_file() and path.suffix in ('.json','.png') and '-reports' in str(path.relative_to(source)):
            info=zipfile.ZipInfo(path.relative_to(source).as_posix(),(1980,1,1,0,0,0))
            info.compress_type=zipfile.ZIP_DEFLATED
            archive.writestr(info,path.read_bytes())
    archive.writestr(zipfile.ZipInfo('SOURCE.txt',(1980,1,1,0,0,0)),
        f'Source commit: {sha}\nCI: {base}/actions/runs/{run}\nNative editor fixtures are not signed-in Codex clients.\n')

for tag,filenames,title,notes_file,preview in (
    ('v1.0.1',expected[:1],'AskBetter v1.0.1 — Windows window lifecycle fixes',
     '.github/releases/windows-v1.0.1.md',False),
    ('v1.1.0-preview.5',expected,'AskBetter Preview 5 — Window lifecycle cleanup',
     '.github/releases/native-preview5.md',True)):
    folder=output/tag
    folder.mkdir(exist_ok=True)
    for filename in filenames:
        shutil.copy2(next(source.rglob(filename)),folder/filename)
    shutil.copy2(reports,folder/reports.name)
    files=sorted(folder.iterdir())
    digests={path.name:hashlib.sha256(path.read_bytes()).hexdigest() for path in files}
    checksum=folder/'SHA256SUMS.txt'
    checksum.write_text(''.join(digests[path.name]+'  '+path.name+'\n' for path in files),encoding='ascii')
    files.append(checksum)
    notes=Path(notes_file).read_text(encoding='utf-8')
    notes+=f'\n\n[Verified CI run]({base}/actions/runs/{run}) · Source `{sha}`\n'
    notes_path=folder/'release-notes.md'
    notes_path.write_text(notes,encoding='utf-8')
    existing=subprocess.run(['gh','release','view',tag,'--json','isDraft,targetCommitish,assets'],
                            capture_output=True,text=True)
    if existing.returncode==0:
        value=json.loads(existing.stdout)
        assert value['targetCommitish']==sha,'Existing release has a different source commit'
        if not value['isDraft']:
            actual={item['name']:item.get('digest') for item in value['assets']}
            assert all(actual.get(path.name)=='sha256:'+hashlib.sha256(path.read_bytes()).hexdigest() for path in files)
            print('Already published and verified: '+tag,flush=True)
            continue
        subprocess.run(['gh','release','upload',tag,'--clobber',*[str(path) for path in files]],check=True)
    else:
        command=['gh','release','create',tag,'--draft','--target',sha,'--title',title,'--notes-file',str(notes_path)]
        if preview:
            command.append('--prerelease')
        subprocess.run(command+[str(path) for path in files],check=True)
    subprocess.run(['gh','release','edit',tag,'--draft=false'],check=True)
    print('Published: '+base+'/releases/tag/'+tag,flush=True)
