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
    const rich=process.env.ASKBETTER_RICH_EDITOR === '1';
    const markup=rich ? escaped.split('\n').map(text=>`<p>${text || '<br class="ProseMirror-trailingBreak">'}</p>`).join('') : escaped;
    return `<!doctype html><html><head><title>${title}</title><style>
      body {font:14px sans-serif;background:#191919;color:#eee;margin:20px;height:calc(100vh - 40px);display:flex;flex-direction:column;gap:12px}
      #editor {background:#343434;flex:1;white-space:pre-wrap;padding:12px;border:1px solid #555;overflow:auto}
      #toolbar {display:flex;gap:12px;justify-content:flex-end} button {padding:6px 12px}
      </style></head><body><div>AskBetter CI — browser-backed editor fixture</div>
      <div id="editor" class="${rich ? 'ProseMirror' : ''}" contenteditable="${rich ? 'true' : 'plaintext-only'}" role="textbox" aria-label="Work with Codex" aria-multiline="true">${markup}</div>
      <div id="toolbar"><button id="model">GPT-6 Luna</button><button>Microphone</button><button id="send">Send</button></div>
      <script>window.__askbetterSent=false;
      document.getElementById('send').onclick=()=>window.__askbetterSent=true;
      document.getElementById('editor').onkeydown=e=>{if(e.key==='Enter'&&!e.shiftKey&&!e.isComposing){window.__askbetterSent=true;e.preventDefault();}};
      </script></body></html>`;
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
        if(request.op==='set') {
            if(process.env.ASKBETTER_RICH_EDITOR === '1')
                await window.webContents.executeJavaScript(`document.getElementById('editor').replaceChildren(...${JSON.stringify(request.text)}.split('\\n').map(text=>{const p=document.createElement('p');if(text)p.textContent=text;else{const br=document.createElement('br');br.className='ProseMirror-trailingBreak';p.append(br);}return p;}));`);
            else
                await window.webContents.executeJavaScript(`document.getElementById('editor').textContent=${JSON.stringify(request.text)};`);
        }
        else if(request.op==='new')
            await create('AskBetter fixture 2',request.text);
        else if(request.op==='focus')
            window.focus();
        else if(request.op==='quit') {
            console.log(JSON.stringify({id:request.id,text:''}));
            app.quit();
            break;
        }
        const text=await window.webContents.executeJavaScript(process.env.ASKBETTER_RICH_EDITOR === '1'
            ? "(()=>{const e=document.getElementById('editor');if([...e.children].every(n=>n.tagName==='P')&&e.children.length)return [...e.children].map(n=>n.textContent).join('\\n');return e.innerText;})()"
            : "document.getElementById('editor').innerText");
        const reply={id:request.id,text,sent:await window.webContents.executeJavaScript('window.__askbetterSent')};
        if(request.op==='geometry')
            reply.geometry=await window.webContents.executeJavaScript(`(()=>{const r=document.getElementById('model').getBoundingClientRect(); const border=(outerWidth-innerWidth)/2; return {x:r.x+border,y:r.y+(outerHeight-innerHeight)-border,width:r.width,height:r.height,outerWidth,outerHeight};})()`);
        console.log(JSON.stringify(reply));
    }
});
app.on('window-all-closed',()=>app.quit());
