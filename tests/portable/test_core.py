import json
from pathlib import Path
import sys
import tempfile
import time
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
from crossplatform.core import Backend, DEFAULTS, Settings, MODES
from crossplatform.geometry import Rect, anchor, near_model, to_shell, LayoutGate, same_target


class BackendTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.record = self.root/"requests.jsonl"

    def client(self, scenario="success"):
        value = Backend(ROOT/"askbetter", self.root/"empty", [sys.executable, str(ROOT/"tests/portable/fake_codex.py"),
            "--scenario", scenario, "--record", str(self.record)])
        self.addCleanup(value.close)
        return value

    def records(self):
        return [json.loads(line) for line in self.record.read_text(encoding="utf-8").splitlines()]

    def test_modes_and_request_isolation(self):
        client = self.client()
        for mode in MODES:
            draft = '相机 IMU：UDP → 机械臂。路径 C:\\示例\\数据；300 字，不用表格。'+mode
            self.assertEqual(client.optimize(draft,{**DEFAULTS,"lengthStyle":mode}),"优化："+draft)
        requests = self.records()
        threads = [value["params"] for value in requests if value.get("method") == "thread/start"]
        self.assertEqual(len(threads),3)
        for mode, thread in zip(MODES,threads):
            self.assertTrue(thread["ephemeral"])
            self.assertEqual(thread["sandbox"],"read-only")
            self.assertEqual(thread["baseInstructions"],(ROOT/"askbetter"/MODES[mode]).read_text(encoding="utf-8-sig").strip())
            self.assertEqual(thread["model"],"gpt-6-luna")
            self.assertEqual(thread["serviceTier"],"fast")

    def test_paginated_models(self):
        self.assertEqual(self.client().models(DEFAULTS),["gpt-6-luna","gpt-6.1-sol"])

    def test_early_notifications(self):
        self.assertEqual(self.client("early").optimize("保留原意",DEFAULTS),"优化：保留原意")

    def test_tool_requests_are_rejected(self):
        self.assertEqual(self.client("tool").optimize("不执行任务",DEFAULTS),"优化：不执行任务")
        self.assertTrue(any(value.get("id") == 900 and "error" in value for value in self.records()))

    def test_failures_preserve_no_result(self):
        for scenario in ("empty","invalid","object","error","crash","wrongmodel","wrongtier"):
            with self.subTest(scenario=scenario):
                with self.assertRaises(RuntimeError):
                    self.client(scenario).optimize("原稿",DEFAULTS)

    def test_timeout_and_recovery(self):
        client = self.client("timeout")
        start = time.monotonic()
        with self.assertRaises(TimeoutError):
            client.optimize("原稿",{**DEFAULTS,"timeoutSeconds":1})
        self.assertLess(time.monotonic()-start,4)
        self.assertIsNone(client.process)
        client.command=[sys.executable,str(ROOT/"tests/portable/fake_codex.py")]
        self.assertEqual(client.optimize("重试",DEFAULTS),"优化：重试")

    def test_invalid_input(self):
        client=self.client()
        for value in ("", "   ", None, "x"*100001, "\ufffc", "Some text \ufffc"):
            with self.assertRaises(ValueError):
                client.optimize(value,DEFAULTS)
        self.assertIsNone(client.process)

    def test_settings_persist_and_reject_invalid_changes(self):
        path=self.root/"settings.json"
        settings=Settings(path)
        self.assertTrue(path.exists())
        settings.update(lengthStyle="detailed",model="gpt-6.1-sol")
        self.assertEqual(Settings(path).value["lengthStyle"],"detailed")
        with self.assertRaises(ValueError):
            settings.update(model="invalid model")
        self.assertEqual(Settings(path).value["model"],"gpt-6.1-sol")


class GeometryTests(unittest.TestCase):
    def test_anchor_and_multimonitor_scaling(self):
        editor=Rect(10,200,800,180); model=Rect(500,350,200,30)
        self.assertTrue(near_model(editor,model))
        self.assertEqual(anchor(model),Rect(466,352,26,26))
        self.assertEqual(to_shell(Rect(100,200,400,60),Rect(0,0,1600,1200),Rect(-800,100,800,600)),Rect(-750,200,200,30))
        self.assertFalse(near_model(editor,Rect(500,10,200,30)))

    def test_layout_settles_without_ctrl_side_effect(self):
        gate=LayoutGate()
        self.assertFalse(gate.update(("window",26),0))
        self.assertTrue(gate.update(("window",26),0.31))
        self.assertFalse(gate.update(("window",27),0.4))
        self.assertFalse(gate.update(("window",27),0.8,moving=True))
        self.assertTrue(gate.update(("window",27),1.11))

    def test_draft_and_target_guards(self):
        before=(12,"chat1","editor")
        self.assertTrue(same_target(before,before,"原稿","原稿"))
        self.assertFalse(same_target(before,before,"原稿","新编辑"))
        self.assertFalse(same_target(before,(12,"chat2","editor"),"原稿","原稿"))


if __name__ == "__main__":
    unittest.main()
