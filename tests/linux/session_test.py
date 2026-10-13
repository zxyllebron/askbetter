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
print('Enable driver',extensions.EnableExtension('askbetter-tests@local'),flush=True)
wait(lambda:bus.name_has_owner('org.askbetter.Tests'),'GNOME test extension enabled')
driver=dbus.Interface(bus.get_object('org.askbetter.Tests','/org/askbetter/Tests'),'org.askbetter.Tests')
print('Initial driver state:',driver.State(),flush=True)
extensions.EnableExtension('askbetter@zxyllebron.github.io')
try:
    wait(lambda:bus.name_has_owner('org.askbetter.Desktop'),'helper service started')
except Exception:
    print('AskBetter extension:',extensions.GetExtensionInfo('askbetter@zxyllebron.github.io'),flush=True)
    print('AskBetter errors:',extensions.GetExtensionErrors('askbetter@zxyllebron.github.io'),flush=True)
    print('Driver state:',driver.State(),flush=True)
    raise
helper=dbus.Interface(bus.get_object('org.askbetter.Desktop','/org/askbetter/Desktop'),'org.askbetter.Desktop')
kind=os.environ.get('ASKBETTER_FIXTURE','gtk')
command_line=['/usr/bin/python3',str(ROOT/'tests/linux/fixture.py')]
fixture_environment={**os.environ,'PYTHONIOENCODING':'utf-8'}
if kind.startswith('electron'):
    binary=str(ROOT/'artifacts/electron/node_modules/.bin/electron')
    command_line=[binary,str(ROOT/'tests/linux/electron-fixture.cjs'),'--class=Codex']
    if os.environ.get('ASKBETTER_TEST_NO_SANDBOX') == '1':
        command_line.append('--no-sandbox')  # Local generated fixture only.
    if kind in ('electron-xwayland', 'electron-rich'):
        shell_environment=json.loads(str(driver.State()))
        fixture_environment['DISPLAY']=shell_environment['xwaylandDisplay']
        fixture_environment['XAUTHORITY']=shell_environment['xwaylandAuthority']
        command_line.append('--ozone-platform=x11')
    elif os.environ['XDG_SESSION_TYPE']=='wayland':
        command_line.append('--ozone-platform=wayland')
fixture=subprocess.Popen(command_line,stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,text=True,encoding='utf-8',env=fixture_environment)
replies=queue.Queue()
def reader():
    for line in fixture.stdout:
        try:
            replies.put(json.loads(line))
        except ValueError:
            continue
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
            return reply.get('geometry') if operation=='geometry' else reply.get('text')
    raise AssertionError('Fixture command timed out')


def state():
    return json.loads(str(driver.State()))


checks=[]
try:
    ready=replies.get(timeout=15)
    assert ready.get('ready'),ready
    fixture_pid=ready['pid']
    driver.Action('desktop')
    wait(lambda:driver.Action('focus1') or state().get('focusedTitle')=='AskBetter fixture 1','fixture receives foreground focus')
    current=wait(lambda:(value if value.get('visible') and value.get('context',{}).get('pid')==fixture_pid else None)
                 if (value:=state()) else None,'circle appears on the focused fixture')
    assert current['size'] == 26, current
    assert current['menu'] == ['简短','适中','详细','单独对话框模式','更改模型'], current
    checks.append('circle size and five menu options')
    from linux.accessibility import DesktopAccess
    access=DesktopAccess()
    target=access.discover(current['context'])
    expected=access.position(target)
    assert abs(current['x']-expected.x)<1 and abs(current['y']-expected.y)<1,current
    measured=command('geometry')
    frame=current['context']['buffer']
    measured['y'] += frame['height']-measured['outerHeight']
    measured['outerHeight'] = frame['height']
    sx,sy=frame['width']/measured['outerWidth'],frame['height']/measured['outerHeight']
    independent_x=frame['x']+measured['x']*sx-34
    independent_y=frame['y']+measured['y']*sy+(measured['height']*sy-26)/2
    assert abs(current['x']-independent_x)<2 and abs(current['y']-independent_y)<2,{'button':current,'fixtureGeometry':measured,'expected':(independent_x,independent_y)}
    checks.append('window-relative AT-SPI geometry maps to GNOME circle position')
    driver.Action('ctrl-down')
    try:
        time.sleep(0.5)
        assert state().get('visible'),'Ctrl alone hid the circular button'
    finally:
        driver.Action('ctrl-up')
    checks.append('Ctrl alone keeps the circular button visible')
    initial_clipboard_formats=state().get('clipboardFormats') or []

    if os.environ.get('ASKBETTER_RICH_EDITOR') == '1':
        draft='背景：Python 项目报错\n先检查日志和相关代码\n最后说明如何验证'
        command('set',text=draft)
        target=access.discover(state()['context'],fresh=True)
        assert access.read(target)==draft,repr(access.read(target))
        driver.Action('click')
        wait(lambda:command('get')=='优化：'+draft,'multi-paragraph rich-editor replacement')
        wait(lambda:not json.loads(str(helper.Poll(json.dumps(state()['context']))))['busy'],'rich-editor replacement verified')
        driver.Action('undo')
        wait(lambda:command('get')==draft,'multi-paragraph rich-editor undo')
        wait(lambda:not json.loads(str(helper.Poll(json.dumps(state()['context']))))['busy'],'rich-editor undo verified')
        checks.append('paragraph objects expand to actual text; multiline replacement and undo')

        # These are different chats in one native window; PID/title/window ID stay
        # unchanged, while editor identity, model label and composer layout vary.
        for label,compact in (('Ask ChatGPT',True),('Work with ChatGPT',False),('Work with Codex',False)):
            draft='切换聊天：'+label
            command('chat',text=draft,compact=compact,label=label)
            wait(lambda:access.read(access.discover(state()['context'],fresh=True))==draft,'new chat accessible draft loaded')
            wait(lambda:state().get('visible'),'circle follows '+label)
            target=access.discover(state()['context'],fresh=True)
            assert target.editor.name==label,target.editor.name
            if compact:
                position=access.position(target)
                editor=access._rectangle(target.editor)
                client=state()['context']['client']
                assert position.y+position.height<=client['y']+editor.y,'circle overlaps compact editor'
            driver.Action('click')
            wait(lambda:command('get')=='优化：'+draft,'replacement in '+label)
            wait(lambda:not json.loads(str(helper.Poll(json.dumps(state()['context']))))['busy'],'replacement verified in '+label)
            driver.Action('undo')
            wait(lambda:command('get')==draft,'undo in '+label)
            wait(lambda:not json.loads(str(helper.Poll(json.dumps(state()['context']))))['busy'],'undo verified in '+label)
            checks.append('same-window '+label+' circle -> replacement -> undo')

        draft='相同草稿，切换同一窗口的聊天'
        command('set',text=draft)
        driver.Action('click')
        wait(lambda:json.loads(str(helper.Poll(json.dumps(state()['context']))))['busy'],'optimization starts before chat switch')
        command('chat',text=draft,compact=True,label='Ask ChatGPT')
        wait(lambda:not json.loads(str(helper.Poll(json.dumps(state()['context']))))['busy'],'same-window chat switch rejected')
        assert command('get')==draft,'result overwrote a different chat'
        checks.append('same text in another chat of the same window is not overwritten')
        command('chat',text=draft,compact=False,label='Work with Codex')
        wait(lambda:access.discover(state()['context'],fresh=True).editor.name=='Work with Codex','expanded editor accessible after chat switch')
        wait(lambda:state().get('visible'),'circle returns to expanded composer')
        time.sleep(1.2)  # Allow the helper's previous per-frame cache to expire.

    for style in ('brief','moderate','detailed'):
        driver.Action(style)
        wait(lambda:json.loads(str(helper.Poll(json.dumps(state()['context']))))['settings']['lengthStyle']==style,'mode changed')
        draft='IMU、UDP 和机械臂：'+style
        command('set',text=draft)
        driver.Action('click')
        wait(lambda:command('get')=='优化：'+draft,'one-click draft replacement')
        wait(lambda:not json.loads(str(helper.Poll(json.dumps(state()['context']))))['busy'],'replacement verification completes')
        driver.Action('undo')
        wait(lambda:command('get')==draft,'undo replacement')
        wait(lambda:not json.loads(str(helper.Poll(json.dumps(state()['context']))))['busy'],'undo verification completes')
        checks.append(style+' button -> fake Codex -> draft replacement -> undo')
    if kind.startswith('electron') and not initial_clipboard_formats:
        wait(lambda:str(driver.ClipboardText())=='','empty clipboard restored safely')
        checks.append('empty clipboard restored without a zero-byte GNOME memory source')
    if kind.startswith('electron'):
        driver.Action('clipboard-text')
        command('set',text='剪贴板保留测试')
        driver.Action('click')
        wait(lambda:command('get')=='优化：剪贴板保留测试','replacement with a populated clipboard')
        wait(lambda:not json.loads(str(helper.Poll(json.dumps(state()['context']))))['busy'],'populated clipboard replacement verified')
        wait(lambda:str(driver.ClipboardText())=='原剪贴板：IMU / UDP','original Unicode clipboard restored')
        driver.Action('undo')
        wait(lambda:command('get')=='剪贴板保留测试','populated clipboard undo')
        wait(lambda:not json.loads(str(helper.Poll(json.dumps(state()['context']))))['busy'],'populated clipboard undo verified')
        checks.append('original Unicode clipboard survives replacement and undo')

    command('set',text='优化前的草稿')
    driver.Action('click')
    wait(lambda:json.loads(str(helper.Poll(json.dumps(state()['context']))))['busy'],'optimization captured the original draft')
    command('set',text='优化期间的新编辑')
    wait(lambda:'未覆盖' in (state().get('caption') or '') or 'preserved' in (state().get('caption') or ''),'changed draft rejected')
    assert command('get')=='优化期间的新编辑'
    checks.append('edits made while waiting are preserved')

    command('set',text='相同草稿，不同窗口')
    driver.Action('click')
    wait(lambda:json.loads(str(helper.Poll(json.dumps(state()['context']))))['busy'],'optimization started before changing windows')
    command('new',text='相同草稿，不同窗口')
    wait(lambda:driver.Action('focus2') or state().get('focusedTitle')=='AskBetter fixture 2','second fixture receives focus')
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
            'checks':checks,'realCodex':False,'fixture':kind+' editor + fake app-server'}
    (output/('linux-'+os.environ['XDG_SESSION_TYPE']+'.json')).write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(report,ensure_ascii=False,indent=2))
finally:
    if sys.exc_info()[0] is not None:
        try:
            debug=state()
            print('Failure state:',json.dumps(debug,ensure_ascii=False),flush=True)
            print('Failure service state:',helper.Poll(json.dumps(debug.get('context',{}))),flush=True)
            try:
                target=access.discover(debug['context'],fresh=True)
                print('Failure current editor:',target.editor.name,'draft:',repr(access.read(target)),flush=True)
            except Exception as error:
                print('Failure editor diagnostic:',str(error),flush=True)
        except Exception as error:
            print('State diagnostic failed:',str(error),flush=True)
    try:
        command('quit')
        fixture.wait(timeout=5)
    except Exception:
        fixture.terminate()
