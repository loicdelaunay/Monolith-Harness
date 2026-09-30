const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require('playwright');
(async()=>{
 const browser=await chromium.launch({headless:true,...(process.env.EDITOR_TEST_BROWSER?{executablePath:process.env.EDITOR_TEST_BROWSER}:process.platform==='win32'?{channel:'chrome'}:{})});
 try{
  const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.addInitScript(()=>{});
  const html=fs.readFileSync(path.join(__dirname,'../src/MonolithHarness.App/Assets/rich-text-editor.html'),'utf8').replaceAll('__NONCE__','editor-regression');
  await page.setContent(html);await page.evaluate(()=>{window.nativeMessages=[];window.chrome??={};window.chrome.webview={postMessage:m=>nativeMessages.push(m)};omhEditor.configure('Paste here',false);});
  async function paste(html,text=''){
   const start=Date.now();
   await page.evaluate(({html,text})=>{
    const editor=document.getElementById('editor');editor.focus();const range=document.createRange();range.selectNodeContents(editor);range.collapse(false);getSelection().removeAllRanges();getSelection().addRange(range);
    const data=new DataTransfer();data.setData('text/html',html);data.setData('text/plain',text);
    editor.dispatchEvent(new ClipboardEvent('paste',{clipboardData:data,bubbles:true,cancelable:true}));
   },{html,text});
   await page.waitForFunction(()=>document.getElementById('editor').getAttribute('aria-busy')==='false');
   return Date.now()-start;
  }
  const email='<p><strong>Bonjour,</strong> voici <a href="https://example.org">un lien</a>.</p><ul><li>Premier élément</li></ul><table><tr><td>Cellule</td><td style="color:#ff0000">Rouge</td></tr></table>';
  await paste(email,'Bonjour, voici un lien. Premier élément Cellule Rouge');
  let snap=await page.evaluate(()=>JSON.parse(omhEditor.read(-1)));
  assert.ok(snap.Document.Runs.map(r=>r.Text).join('').includes('Cellule'));
  assert.ok(snap.Document.Template.includes('<strong>')&&snap.Document.Template.includes('href="https://example.org"')&&snap.Document.Template.includes('<table>'));
  assert.equal(await page.evaluate(()=>nativeMessages.filter(m=>m.kind==='changed').length),1,'One edit notification per paste');
  await page.evaluate(()=>{window.mutations=[];new MutationObserver(list=>mutations.push(...list)).observe(document.getElementById('editor'),{subtree:true,childList:true,characterData:true,attributes:true});omhEditor.select(0,7);});
  const selected=await page.evaluate(()=>getSelection().toString());
  await page.evaluate(()=>{for(let i=0;i<20;i++)omhEditor.read(-1);});
  await page.waitForTimeout(800);
  assert.equal(await page.evaluate(()=>mutations.length),0,'Idle reads never replace the visible editor');
  assert.equal(await page.evaluate(()=>getSelection().toString()),selected,'Read preserves selection');
  const retained=await page.evaluate(()=>document.getElementById('editor').innerHTML);
  await paste('<div>'.repeat(500)+'Texte'+'</div>'.repeat(500),'Texte');
  assert.equal(await page.evaluate(()=>document.getElementById('editor').innerHTML),retained,'Deep paste rejection preserves current text');
  await paste('', 'x'.repeat(20001));
  assert.equal(await page.evaluate(()=>document.getElementById('editor').innerHTML),retained,'Oversized paste rejection preserves current text');
  assert.ok(await page.evaluate(()=>nativeMessages.filter(m=>m.kind==='error').length>=2));
  await page.evaluate(()=>omhEditor.set(''));
  const rich='<p>'+Array.from({length:3500},(_,i)=>'<span style="font-weight:bold">'+(i%10)+' </span>').join('')+'</p>';
  await page.evaluate(()=>{window.pulses=0;window.pulse=setInterval(()=>pulses++,10);});
  const elapsed=await paste(rich,'1 '.repeat(3500));
  const pulses=await page.evaluate(()=>{clearInterval(pulse);return window.pulses;});
  assert.ok(pulses>3,'Rich paste yields to the browser event loop');
  snap=await page.evaluate(()=>JSON.parse(omhEditor.read(-1)));assert.equal(snap.Document.Runs.filter(r=>!r.Structural).length,3500);
  await page.evaluate(()=>omhEditor.set(''));
  await paste('<custom><b>Important</b></custom><script>throw new Error("unsafe")</script><img src="javascript:alert(1)" alt="image"><a href="javascript:alert(1)">Lien</a>','Important image Lien');
  const sanitized=await page.evaluate(()=>document.getElementById('editor').innerHTML);
  assert.ok(sanitized.includes('<b>Important</b>')&&!sanitized.includes('javascript:')&&!sanitized.includes('<script'));
  assert.deepEqual(errors,[]);
  console.log(JSON.stringify({passed:true,richPasteNodes:3500,richPasteMilliseconds:elapsed,eventLoopPulses:pulses,checks:'formatting, one notification, no idle mutation, caret, complexity and length rejection, responsiveness, sanitized markup'}));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
