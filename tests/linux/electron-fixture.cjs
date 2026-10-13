// Browser-backed editor fixture; no external pages, accounts, or model calls.
const {app, BrowserWindow} = require('electron');
const readline = require('node:readline');
app.setName('Codex');
app.setDesktopName('Codex.desktop');
app.disableHardwareAcceleration();
app.commandLine.appendSwitch('class', 'Codex');
app.commandLine.appendSwitch('force-renderer-accessibility');
const windows=[];
function html(title,draft) {
    const escaped=draft.replace(/[&<>]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]));
    return `<!doctype html><html><head><title>${title}</title><style>
      body {font:14px sans-serif;background:#191919;color:#eee;margin:20px;height:calc(100vh - 40px);display:flex;flex-direction:column;gap:12px}
      #editor {background:#343434;flex:1;white-space:pre-wrap;padding:12px;border:1px solid #555;overflow:auto}
      #toolbar {display:flex;gap:12px;justify-content:flex-end} button {padding:6px 12px}
      </style></head><body><div>AskBetter CI — browser-backed editor fixture</div>
      <div id="editor" contenteditable="plaintext-only" role="textbox" aria-label="Work with Codex" aria-multiline="true">${escaped}</div>
      <div id="toolbar"><button id="model">GPT-6 Luna</button><button>Microphone</button><button id="send">Send</button></div>
      </body></html>`;
}
async function create(title,draft) {
    const window=new BrowserWindow({width:900,height:650,title,
        webPreferences:{contextIsolation:true,nodeIntegration:false,sandbox:true,backgroundThrottling:false}});
    // The fixture renders a generated local document only.
    await window.loadURL('data:text/html;charset=utf-8,'+encodeURIComponent(html(title,draft)));
    window.setTitle(title); window.show(); window.focus();
    windows.push(window);
    return window;
}
app.whenReady().then(async()=>{
    app.setAccessibilitySupportEnabled(true);
    await create('AskBetter fixture 1','原始 IMU 草稿，保留 UDP 和机械臂限制。');
    console.log(JSON.stringify({ready:true,pid:process.pid}));
    const incoming=readline.createInterface({input:process.stdin});
    for await (const line of incoming) {
        const request=JSON.parse(line), index=request.window ?? windows.length-1;
        const window=windows[index];
        if(request.op==='set')
            await window.webContents.executeJavaScript(`document.getElementById('editor').textContent=${JSON.stringify(request.text)};`);
        else if(request.op==='new')
            await create('AskBetter fixture 2',request.text);
        else if(request.op==='focus')
            window.focus();
        else if(request.op==='quit')
            app.quit();
        const text=await window.webContents.executeJavaScript("document.getElementById('editor').innerText");
        console.log(JSON.stringify({id:request.id,text}));
    }
});
app.on('window-all-closed',()=>app.quit());
