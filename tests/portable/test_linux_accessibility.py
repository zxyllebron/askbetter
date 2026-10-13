"""Regression checks for Chromium rich editors and duplicate Electron frames."""
from pathlib import Path
import sys
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from linux.accessibility import DesktopAccess, Target
from crossplatform.geometry import Rect

class Link:
 def __init__(self,child): self.child=child
 def getObject(self,index): return self.child
class Node:
 def __init__(self,text='',links=None,attrs=None):
  self.value=text;self.characterCount=len(text);self.links=links or {};self.attrs=attrs or {}
 def queryText(self): return self
 def getText(self,start,end): return self.value[start:end]
 def get_attributes(self): return self.attrs
 def queryHypertext(self): return self
 def getLinkIndex(self,offset): return offset if offset in self.links else -1
 def getLink(self,index): return Link(self.links[index])
class Frame:
 def __init__(self,w,h,name='ChatGPT'):
  self.name=name;self.rect=Rect(0,0,w,h)
 def getRoleName(self): return 'frame'
class App:
 def __init__(self,frames): self.frames=frames;self.childCount=len(frames)
 def getChildAtIndex(self,i): return self.frames[i]
class Access(DesktopAccess):
 def __init__(self,frames):
  super().__init__(api=object()); self.app=App(frames);self.selected=None
 def _app(self,context): return self.app
 def _visible(self,node): return True
 def _rectangle(self,node): return node.rect
 def _nodes(self,node): self.selected=node;return iter(())

class RichTextTests(unittest.TestCase):
 def setUp(self): self.access=DesktopAccess(api=object())
 def read(self,node): return self.access.read(Target(None,None,node,None,{}))
 def root(self,*paragraphs):
  return Node('\ufffc'*len(paragraphs),dict(enumerate(paragraphs)),{'class':'ProseMirror'})
 def test_plain_text_unchanged(self):
  self.assertEqual(self.read(Node('原稿\n\n')), '原稿\n\n')
 def test_embedded_paragraph_is_expanded(self):
  self.assertEqual(self.read(self.root(Node('排查 Python 报错'))),'排查 Python 报错')
 def test_multiple_paragraphs_preserve_blank_lines(self):
  self.assertEqual(self.read(self.root(Node('背景'),Node(''),Node('要求'),Node(''))),'背景\n\n要求\n')
 def test_css_placeholder_is_not_a_draft(self):
  placeholder=Node('Work with Codex',attrs={'tag':'::before'})
  self.assertEqual(self.read(self.root(Node('\ufffc',{0:placeholder}))),'')
 def test_link_text_preserved(self):
  paragraph=Node('访问 \ufffc。',{3:Node('https://github.com/zxyllebron/askbetter')})
  self.assertEqual(self.read(self.root(paragraph)),'访问 https://github.com/zxyllebron/askbetter。')
 def test_unknown_object_is_rejected(self):
  with self.assertRaisesRegex(RuntimeError,'Unreadable embedded object'):
   self.read(self.root(Node('说明 \ufffc\n')))
 def test_cycle_is_rejected(self):
  node=Node('\ufffc');node.links[0]=node
  with self.assertRaisesRegex(RuntimeError,'safely read'):
   self.read(node)

class FrameTests(unittest.TestCase):
 def context(self,w=860,h=651):
  return {'pid':4379,'windowId':'5','title':'ChatGPT','buffer':{'x':121,'y':87,'width':w,'height':h}}
 def test_unique_foreground_size_selects_main(self):
  frames=[Frame(724,651),Frame(860,651),Frame(1132,1705)];access=Access(frames)
  with self.assertRaisesRegex(RuntimeError,'input box/model selector'): access.discover(self.context())
  self.assertIs(access.selected,frames[1])
 def test_same_size_duplicate_is_rejected(self):
  access=Access([Frame(860,651),Frame(860,651)])
  with self.assertRaisesRegex(RuntimeError,'uniquely identify'): access.discover(self.context())
  self.assertIsNone(access.selected)
 def test_no_size_match_is_rejected(self):
  access=Access([Frame(724,651),Frame(1132,1705)])
  with self.assertRaisesRegex(RuntimeError,'uniquely identify'): access.discover(self.context())
 def test_client_dimensions_match_decorated_window(self):
  frames=[Frame(860,651),Frame(860,800)];access=Access(frames)
  ctx=self.context(h=689);ctx['client']={'x':121,'y':125,'width':860,'height':651}
  with self.assertRaisesRegex(RuntimeError,'input box/model selector'): access.discover(ctx)
  self.assertIs(access.selected,frames[0])

if __name__=='__main__': unittest.main()
