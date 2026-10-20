"""Desktop checks against the separate Winicode test host; no personal apps are edited."""
import ctypes,json,os,pathlib,sys,time
from ctypes import wintypes as W

ROOT=pathlib.Path(__file__).resolve().parents[2]
sys.coinit_flags=0
deps=os.environ.get('WINICODE_PYTHON_DEPS',str(ROOT/'.tools/python-deps'))
sys.path.insert(0,deps)
import comtypes.client
from PIL import ImageGrab

u=ctypes.windll.user32
u.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
u.FindWindowW.restype=W.HWND;u.GetForegroundWindow.restype=W.HWND
m=comtypes.client.GetModule('UIAutomationCore.dll')
a=comtypes.client.CreateObject(m.CUIAutomation8,interface=m.IUIAutomation)
OUT=ROOT/'artifacts/desktop';OUT.mkdir(parents=True,exist_ok=True)
def wait(fn,seconds=5):
    end=time.monotonic()+seconds
    while time.monotonic()<end:
        result=fn()
        if result:return result
        time.sleep(.05)
    raise RuntimeError('Timed out waiting for test control')
def byid(root,id):return root.FindFirst(4,a.CreatePropertyCondition(30011,id))
def invoke(element):element.GetCurrentPattern(10000).QueryInterface(m.IUIAutomationInvokePattern).Invoke()
def value(element):return element.GetCurrentPattern(10002).QueryInterface(m.IUIAutomationValuePattern).CurrentValue
def setvalue(element,text):element.GetCurrentPattern(10002).QueryInterface(m.IUIAutomationValuePattern).SetValue(text)
def key(v):u.keybd_event(v,0,0,0);u.keybd_event(v,0,2,0)
def screenshot(hwnd,name):
    r=W.RECT();u.GetWindowRect(hwnd,ctypes.byref(r))
    ImageGrab.grab(bbox=(r.left,r.top,r.right,r.bottom)).save(OUT/name)
target=u.FindWindowW(None,'Winicode verification target')
if not target:raise RuntimeError('Launch tests/Winicode.TestHost first')
u.ShowWindow(target,9);u.SetForegroundWindow(target)
host=a.ElementFromHandle(target);edit=byid(host,'TestText');other=byid(host,'TestOther')
prepare=host.FindFirst(4,a.CreatePropertyCondition(30005,'开始拼音测试'))
def open_word(word,backspace=False):
    edit.SetFocus();key(27);invoke(prepare);time.sleep(.15);key(27);time.sleep(.25)
    for ch in word.upper():key(ord(ch));time.sleep(.04)
    if backspace:key(8)
    wait(lambda:(h:=u.FindWindowW(None,'Winicode U+')) and u.IsWindowVisible(h))
    time.sleep(.1);key(0x30)
    p=wait(lambda:(h:=u.FindWindowW(None,'Winicode 字符搜索')) and u.IsWindowVisible(h) and h)
    root=a.ElementFromHandle(p);query=byid(root,'UnicodeQuery')
    expected=word[:-1] if backspace else word
    assert value(query)==expected,('prefill',expected,value(query))
    return p,root,query
report={}
for text in ['ni','hello']:
    p,r,q=open_word(text);report[text]=value(q);key(27);time.sleep(.2)
p,r,q=open_word('alphax',True);time.sleep(.2);screenshot(p,'selector-light.png')
items=byid(r,'UnicodeResults').FindAll(2,a.CreatePropertyCondition(30003,50007))
assert items.Length==5,items.Length
key(0x31);wait(lambda:not u.IsWindowVisible(p));time.sleep(.2)
assert value(edit).endswith('α'),value(edit);report['numberSelection']=True
p,r,q=open_word('alpha');setvalue(q,'U+1D6FC');q.SetFocus();key(13)
wait(lambda:not u.IsWindowVisible(p));time.sleep(.25)
assert value(edit).endswith(chr(0x1d6fc)),value(edit);report['supplementaryScalar']=True
p,r,q=open_word('alpha');other.SetFocus();time.sleep(.25)
assert not u.IsWindowVisible(p);assert value(other)=='';report['focusChangeCancels']=True
edit.SetFocus();key(27);time.sleep(.4);before=value(edit);key(0x30);time.sleep(.2)
assert value(edit)==before+'0';report['ordinaryZero']=True
(OUT/'results.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False),flush=True)
