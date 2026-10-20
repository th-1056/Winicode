"""Run on a QA --data-dir profile; changes settings/aliases only in that profile."""
import ctypes,json,os,pathlib,sys,time
from ctypes import wintypes as W
ROOT=pathlib.Path(__file__).resolve().parents[2]
sys.coinit_flags=0;sys.path.insert(0,os.environ.get('WINICODE_PYTHON_DEPS',str(ROOT/'.tools/python-deps')))
import comtypes.client
m=comtypes.client.GetModule('UIAutomationCore.dll');a=comtypes.client.CreateObject(m.CUIAutomation8,interface=m.IUIAutomation)
u=ctypes.windll.user32;u.FindWindowW.restype=W.HWND
def key(v):u.keybd_event(v,0,0,0);u.keybd_event(v,0,2,0)
def choose_theme(combo,index):
 combo.GetCurrentPattern(10005).QueryInterface(m.IUIAutomationExpandCollapsePattern).Collapse()
 combo.SetFocus();key(0x24)
 for _ in range(index):key(0x28)
 key(13);time.sleep(.12)
def wait(fn,seconds=5):
 end=time.monotonic()+seconds
 while time.monotonic()<end:
  result=fn()
  if result:return result
  time.sleep(.05)
 raise RuntimeError('Control/state did not become ready')
def byid(root,id):return root.FindFirst(4,a.CreatePropertyCondition(30011,id))
def invoke(e):e.GetCurrentPattern(10000).QueryInterface(m.IUIAutomationInvokePattern).Invoke()
def select(e):e.GetCurrentPattern(10010).QueryInterface(m.IUIAutomationSelectionItemPattern).Select()
def setvalue(e,text):e.GetCurrentPattern(10002).QueryInterface(m.IUIAutomationValuePattern).SetValue(text)
def element_window(id):
 r=a.GetRootElement().FindFirst(2,a.CreatePropertyCondition(30011,id))
 return r if r else None
first=element_window('FirstRunWindow')
if first:invoke(byid(first,'CompleteFirstRunAndSettings'))
root=wait(lambda:element_window('SettingsWindow'))
select(byid(root,'InputDisplayTab'));time.sleep(.15)
combo=byid(root,'Theme')
choose_theme(combo,2);invoke(byid(root,'ApplySettings'));time.sleep(.25)
profile=pathlib.Path(sys.argv[1]);settings=json.loads((profile/'settings.json').read_text(encoding='utf-8-sig'))
assert settings['theme']=='dark',settings
select(byid(root,'AliasesTab'));time.sleep(.1)
setvalue(byid(root,'AliasCharacter'),'U+03B1');setvalue(byid(root,'AliasKeyword'),'qa-custom-alpha');setvalue(byid(root,'AliasDescription'),'QA 自定义阿尔法')
invoke(byid(root,'AddAlias'));time.sleep(.2)
aliases=json.loads((profile/'aliases.json').read_text(encoding='utf-8-sig'))
assert any(x['keyword']=='qa-custom-alpha' and x['codePoint']==0x3b1 for x in aliases),aliases
select(byid(root,'AboutTab'));invoke(byid(root,'PreviewDiagnostics'));time.sleep(.15)
preview=byid(root,'DiagnosticPreview').GetCurrentPattern(10002).QueryInterface(m.IUIAutomationValuePattern).CurrentValue
assert 'qa-custom-alpha' not in preview and str(profile) not in preview
before=set(profile.glob('Winicode-diagnostics-*.txt'));invoke(byid(root,'ExportDiagnostics'));time.sleep(.2)
assert set(profile.glob('Winicode-diagnostics-*.txt'))-before
select(byid(root,'InputDisplayTab'))
combo=byid(root,'Theme');choose_theme(combo,1);invoke(byid(root,'ApplySettings'));time.sleep(.15)
(ROOT/'artifacts/settings-results.json').write_text(json.dumps({'themePersistence':True,'aliasCrud':True,'diagnosticPreviewExport':True},indent=2),encoding='utf-8')
print('PASS: theme persisted, alias saved, diagnostic preview/export contain no query or local path.')
