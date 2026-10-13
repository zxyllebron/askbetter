"""End-to-end GNOME/AT-SPI/button/backend/replacement checks with a real GTK fixture."""
import json
import os
from pathlib import Path
import queue
import subprocess
import sys
import threading
import time
import dbus

ROOT=Path(__file__).resolve().parents[2]
bus=dbus.SessionBus()


def wait(predicate, description, seconds=30):
    deadline=time.monotonic()+seconds
    latest=None
    while time.monotonic()<deadline:
        try:
            latest=predicate()
            if latest:
                return latest
        except Exception as error:
            latest=str(error)
        time.sleep(0.15)
    raise AssertionError(f"Timed out: {description}; last result: {latest}")


wait(lambda:bus.name_has_owner('org.gnome.Shell'),'GNOME Shell session started')
extensions=dbus.Interface(bus.get_object('org.gnome.Shell','/org/gnome/Shell'),'org.gnome.Shell.Extensions')
inventory=wait(lambda:extensions.ListExtensions(),'desktop extensions registered')
print('Desktop extension inventory:',inventory,flush=True)
for extension_uuid in ('askbetter@zxyllebron.github.io','askbetter-tests@local'):
    print('Enable',extension_uuid,extensions.EnableExtension(extension_uuid),flush=True)
wait(lambda:bus.name_has_owner('org.askbetter.Tests'),'GNOME test extension enabled')
driver=dbus.Interface(bus.get_object('org.askbetter.Tests','/org/askbetter/Tests'),'org.askbetter.Tests')
wait(lambda:bus.name_has_owner('org.askbetter.Desktop'),'helper service started')
helper=dbus.Interface(bus.get_object('org.askbetter.Desktop','/org/askbetter/Desktop'),'org.askbetter.Desktop')
fixture=subprocess.Popen(['/usr/bin/python3',str(ROOT/'tests/linux/fixture.py')],stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,text=True,encoding='utf-8',env={**os.environ,'PYTHONIOENCODING':'utf-8'})
replies=queue.Queue()
def reader():
    for line in fixture.stdout:
        replies.put(json.loads(line))
threading.Thread(target=reader,daemon=True).start()
sent=False
counter=0


def command(operation, **values):
    global counter,sent
    counter+=1
    fixture.stdin.write(json.dumps({'id':counter,'op':operation,**values},ensure_ascii=False)+'\n')
    fixture.stdin.flush()
    deadline=time.monotonic()+10
    while time.monotonic()<deadline:
        reply=replies.get(timeout=10)
        if reply.get('sent'):
            sent=True
        if reply.get('id') == counter:
            return reply.get('text')
    raise AssertionError('Fixture command timed out')


def state():
    return json.loads(str(driver.State()))


checks=[]
try:
    wait(lambda:state().get('visible'),'circle appears on the fixture')
    current=state()
    assert current['size'] == 26, current
    assert current['menu'] == ['简短','适中','详细','单独对话框模式','更改模型'], current
    checks.append('circle size and five menu options')
    from linux.accessibility import DesktopAccess
    access=DesktopAccess()
    target=access.discover(current['context'])
    expected=access.position(target)
    assert abs(current['x']-expected.x)<1 and abs(current['y']-expected.y)<1,current
    checks.append('window-relative AT-SPI geometry maps to GNOME circle position')

    for style in ('brief','moderate','detailed'):
        driver.Action(style)
        wait(lambda:json.loads(str(helper.Poll(json.dumps(state()['context']))))['settings']['lengthStyle']==style,'mode changed')
        draft='IMU、UDP 和机械臂：'+style
        command('set',text=draft)
        driver.Action('click')
        wait(lambda:command('get')=='优化：'+draft,'one-click draft replacement')
        driver.Action('undo')
        wait(lambda:command('get')==draft,'undo replacement')
        checks.append(style+' button -> fake Codex -> draft replacement -> undo')

    command('set',text='优化前的草稿')
    driver.Action('click')
    command('set',text='优化期间的新编辑')
    wait(lambda:'未覆盖' in (state().get('caption') or '') or 'preserved' in (state().get('caption') or ''),'changed draft rejected')
    assert command('get')=='优化期间的新编辑'
    checks.append('edits made while waiting are preserved')

    command('set',text='相同草稿，不同窗口')
    driver.Action('click')
    command('new',text='相同草稿，不同窗口')
    wait(lambda:state().get('focusedTitle')=='AskBetter fixture 2','new foreground window')
    wait(lambda:'preserved' in (state().get('caption') or '') or '未覆盖' in (state().get('caption') or ''),'different window rejected')
    assert command('get',window=0)=='相同草稿，不同窗口'
    assert command('get',window=1)=='相同草稿，不同窗口'
    checks.append('same text in a different window is not overwritten')

    wait(lambda:state().get('visible'),'circle follows new window')
    driver.Action('resize')
    wait(lambda:not state().get('visible'),'circle hides during resize',seconds=5)
    wait(lambda:state().get('visible'),'circle returns after resize settles')
    checks.append('resize hides and restores the circular button')
    assert not sent,'The Send button was clicked'
    checks.append('draft replacement never sends the prompt')

    output=ROOT/'artifacts/tests'
    output.mkdir(parents=True,exist_ok=True)
    report={'session':os.environ.get('XDG_SESSION_TYPE'),'toolkit':os.environ.get('GDK_BACKEND'),
            'checks':checks,'realCodex':False,'fixture':'GTK editor + fake app-server'}
    (output/('linux-'+os.environ['XDG_SESSION_TYPE']+'.json')).write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(report,ensure_ascii=False,indent=2))
finally:
    try:
        command('quit')
        fixture.wait(timeout=5)
    except Exception:
        fixture.terminate()
