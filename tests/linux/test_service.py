import sys
from pathlib import Path
from unittest.mock import patch
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from linux.service import wake_chromium_accessibility

class Status:
 def __init__(self,value): self.value=value;self.writes=[]
 def Get(self,*args): return self.value
 def Set(self,interface,key,value): self.value=bool(value);self.writes.append(bool(value))
class Bus:
 def get_object(self,*args): return object()
class AccessibilityWakeTests(unittest.TestCase):
 def test_wake_restores_original_flag(self):
  status=Status(False);scheduled=[]
  with patch('linux.service.dbus.Interface',return_value=status):
   restore=wake_chromium_accessibility(Bus(),lambda ms,fn:scheduled.append((ms,fn)))
   self.assertEqual(status.writes,[True]);self.assertEqual(scheduled[0][0],1500)
   scheduled[0][1]();restore()
  self.assertEqual(status.writes,[True,False]);self.assertFalse(status.value)
 def test_existing_screen_reader_is_preserved(self):
  status=Status(True)
  with patch('linux.service.dbus.Interface',return_value=status):
   self.assertIsNone(wake_chromium_accessibility(Bus(),lambda *_:self.fail('should not schedule')))
  self.assertEqual(status.writes,[]);self.assertTrue(status.value)
 def test_scheduler_failure_restores_flag(self):
  status=Status(False)
  def fail(*_): raise RuntimeError('scheduler unavailable')
  with patch('linux.service.dbus.Interface',return_value=status):
   with self.assertRaises(RuntimeError): wake_chromium_accessibility(Bus(),fail)
  self.assertFalse(status.value)
if __name__=='__main__': unittest.main()
