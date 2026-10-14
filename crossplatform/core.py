"""Portable Codex app-server client. No desktop access or third-party dependencies."""
from __future__ import annotations

import atexit
import copy
import json
import os
from pathlib import Path
import queue
import re
import shutil
import signal
import subprocess
import threading
import time

VERSION = "1.1.0"
MODES = {"brief": "optimizer-prompt-brief.txt", "moderate": "optimizer-prompt.txt",
         "detailed": "optimizer-prompt-detailed.txt"}
DEFAULTS = {"model": "gpt-6-luna", "effort": "low", "serviceTier": "fast",
            "lengthStyle": "moderate", "uiMode": "initial", "codexPath": "",
            "timeoutSeconds": 90, "buttonSize": 26, "anchorGap": 8}
MODEL_ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}$")


def validate_settings(value):
    result = {**DEFAULTS, **value}
    if not MODEL_ID.fullmatch(result["model"]):
        raise ValueError("Invalid model identifier")
    if result["lengthStyle"] not in MODES or result["uiMode"] not in ("initial", "standalone"):
        raise ValueError("Invalid editing or interface mode")
    if result["effort"] != "low" or result["serviceTier"] != "fast":
        raise ValueError("This version requires Low reasoning and Fast mode")
    for key, low, high in (("timeoutSeconds", 1, 300), ("buttonSize", 20, 48), ("anchorGap", 0, 32)):
        if not isinstance(result[key], (int, float)) or not low <= result[key] <= high:
            raise ValueError("Invalid " + key)
    return result


class Settings:
    def __init__(self, path):
        self.path = Path(path)
        self.value = validate_settings(json.loads(self.path.read_text(encoding="utf-8-sig"))
                                       if self.path.exists() else {})
        if not self.path.exists():
            self.update()

    def update(self, **changes):
        value = validate_settings({**self.value, **changes})
        self.path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
        temp = self.path.with_suffix(".tmp")
        with temp.open("w", encoding="utf-8") as stream:
            json.dump(value, stream, ensure_ascii=False, indent=2)
        if os.name != "nt":
            temp.chmod(0o600)
        temp.replace(self.path)
        self.value = value
        return copy.deepcopy(value)


def locate_codex(configured=""):
    if configured:
        path = Path(configured).expanduser()
        if path.is_file() and (os.name == "nt" or os.access(path, os.X_OK)):
            return str(path)
        raise RuntimeError("codexPath does not point to an executable Codex CLI")
    found = shutil.which("codex")
    if found:
        return found
    candidates = [Path.home() / ".local/bin/codex", Path.home() / ".npm-global/bin/codex",
                  Path("/opt/homebrew/bin/codex"), Path("/usr/local/bin/codex"),
                  Path("/usr/lib/chatgpt/resources/codex"), Path("/usr/lib/codex/resources/codex"),
                  Path("/opt/chatgpt/resources/codex"), Path("/opt/Codex/resources/codex")]
    for app in ("Codex", "ChatGPT"):
        for parent in (Path("/Applications"), Path.home() / "Applications"):
            candidates += [parent / (app + ".app") / "Contents/Resources" / name
                           for name in ("codex", "bin/codex", "codex-cli/codex")]
    for path in candidates:
        if path.is_file() and os.access(path, os.X_OK):
            return str(path)
    raise RuntimeError("Codex CLI not found. Install/sign in to Codex or set codexPath in settings.json.")


class Backend:
    def __init__(self, prompts, workspace, command=None):
        self.prompts, self.workspace = Path(prompts), Path(workspace)
        self.command = command
        self.process = None
        self.messages = queue.Queue()
        self.sequence = 0
        self.operations = threading.Lock()
        atexit.register(self.close)

    def _send(self, value):
        if not self.process or self.process.poll() is not None:
            raise RuntimeError("Codex background process exited")
        self.process.stdin.write(json.dumps(value, ensure_ascii=False) + "\n")
        self.process.stdin.flush()

    def _receive(self, deadline):
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise TimeoutError("Optimization timed out; the original draft was preserved")
        try:
            message = self.messages.get(timeout=remaining)
        except queue.Empty:
            raise TimeoutError("Optimization timed out; the original draft was preserved") from None
        if message is None:
            raise RuntimeError("Codex background process exited. Check CLI installation and login.")
        if "id" in message and "method" in message:
            self._send({"id": message["id"], "error": {"code": -32601,
                        "message": "AskBetter does not execute tools or accept approval requests"}})
            return self._receive(deadline)
        return message

    def _rpc(self, method, params, deadline, notifications=None):
        self.sequence += 1
        request_id = self.sequence
        self._send({"id": request_id, "method": method, "params": params})
        while True:
            message = self._receive(deadline)
            if message.get("id") == request_id:
                if "error" in message:
                    raise RuntimeError("Codex: " + str(message["error"].get("message", "request failed")))
                return message["result"]
            if notifications is not None and "method" in message:
                notifications.append(message)

    def _start(self, settings, deadline):
        if self.process and self.process.poll() is None:
            return
        command = self.command or [locate_codex(settings["codexPath"]), "app-server"]
        messages = self.messages = queue.Queue()
        self.workspace.mkdir(parents=True, exist_ok=True, mode=0o700)
        self.process = process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL, text=True, encoding="utf-8", bufsize=1,
            cwd=self.workspace, start_new_session=(os.name != "nt"),
            env={**os.environ, "PYTHONIOENCODING": "utf-8"})

        def read():
            try:
                for line in process.stdout:
                    try:
                        message = json.loads(line)
                    except (ValueError, TypeError):
                        continue
                    if isinstance(message, dict):
                        messages.put(message)
            except (OSError, ValueError):
                pass
            finally:
                messages.put(None)
        threading.Thread(target=read, daemon=True).start()
        self._rpc("initialize", {"clientInfo": {"name": "askbetter", "title": "AskBetter", "version": VERSION},
                  "capabilities": {"experimentalApi": True}}, deadline)
        self._send({"method": "initialized"})

    def models(self, settings):
        with self.operations:
            settings = validate_settings(settings)
            deadline = time.monotonic() + settings["timeoutSeconds"]
            try:
                self._start(settings, deadline)
                models, cursor = [], None
                for _ in range(20):
                    result = self._rpc("model/list", {"limit": 100, "cursor": cursor, "includeHidden": False}, deadline)
                    models.extend(item.get("model") or item.get("id") for item in result.get("data", []))
                    cursor = result.get("nextCursor")
                    if not cursor:
                        break
                return sorted({value for value in models if isinstance(value, str) and MODEL_ID.fullmatch(value)})
            except Exception:
                self.close()
                raise

    def optimize(self, draft, settings):
        if not isinstance(draft, str) or not draft.strip():
            raise ValueError("Enter a prompt to optimize")
        if "\ufffc" in draft:
            raise ValueError("Draft contains an unreadable object; original preserved")
        if len(draft) > 100000:
            raise ValueError("Draft exceeds 100,000 characters")
        settings = validate_settings(copy.deepcopy(settings))
        instructions = (self.prompts / MODES[settings["lengthStyle"]]).read_text(encoding="utf-8-sig").strip()
        if not instructions:
            raise ValueError("The optimization instruction file is empty")
        with self.operations:
            deadline = time.monotonic() + settings["timeoutSeconds"]
            try:
                self._start(settings, deadline)
                result = self._rpc("thread/start", {"model": settings["model"], "serviceTier": settings["serviceTier"],
                    "cwd": str(self.workspace.resolve()), "approvalPolicy": "never", "sandbox": "read-only",
                    "baseInstructions": instructions, "developerInstructions": instructions, "ephemeral": True,
                    "config": {"model_reasoning_effort": settings["effort"], "web_search": "disabled",
                               "features.shell_tool": False}}, deadline)
                if result.get("model") != settings["model"] or result.get("serviceTier") not in ("fast", "priority"):
                    raise RuntimeError("Codex did not accept the configured model/Fast mode; draft preserved")
                thread_id = result["thread"]["id"]
                early = []
                turn = self._rpc("turn/start", {"threadId": thread_id, "model": settings["model"],
                    "effort": settings["effort"], "serviceTier": settings["serviceTier"],
                    "input": [{"type": "text", "text": json.dumps({"draft": draft}, ensure_ascii=False), "text_elements": []}],
                    "outputSchema": {"type": "object", "properties": {"prompt": {"type": "string"}},
                                     "required": ["prompt"], "additionalProperties": False}}, deadline, early)
                turn_id = turn.get("turn", {}).get("id")
                final = None
                completed = False
                while not completed:
                    message = early.pop(0) if early else self._receive(deadline)
                    params = message.get("params", {})
                    if params.get("threadId") != thread_id:
                        continue
                    if turn_id and params.get("turnId") and params["turnId"] != turn_id:
                        continue
                    if message.get("method") == "item/completed":
                        item = params.get("item", {})
                        if item.get("type") == "agentMessage" and item.get("phase") != "commentary":
                            final = item.get("text")
                    elif message.get("method") == "turn/completed":
                        item = params.get("turn", {})
                        if turn_id and item.get("id") and item["id"] != turn_id:
                            continue
                        if item.get("status") != "completed":
                            raise RuntimeError("Optimization failed; the original draft was preserved")
                        completed = True
                try:
                    output = json.loads(final)["prompt"]
                except (ValueError, TypeError, KeyError):
                    raise RuntimeError("Codex returned an invalid optimization result; draft preserved") from None
                if not isinstance(output, str) or not output.strip():
                    raise RuntimeError("Codex returned an empty optimization result; draft preserved")
                if "\ufffc" in output:
                    raise RuntimeError("Codex returned an object placeholder; draft preserved")
                # This notification ends the subscription without delaying the completed result.
                self.sequence += 1
                self._send({"id": self.sequence, "method": "thread/unsubscribe", "params": {"threadId": thread_id}})
                return output
            except Exception:
                self.close()
                raise

    def close(self):
        process, self.process = self.process, None
        if process is None:
            return
        try:
            if process.poll() is None:
                if os.name != "nt":
                    os.killpg(process.pid, signal.SIGTERM)
                else:
                    process.terminate()
                try:
                    process.wait(timeout=2)
                except subprocess.TimeoutExpired:
                    if os.name != "nt":
                        os.killpg(process.pid, signal.SIGKILL)
                    else:
                        process.kill()
                    process.wait(timeout=2)
        except (OSError, subprocess.TimeoutExpired):
            pass
        finally:
            for stream in (process.stdin, process.stdout):
                if stream:
                    stream.close()


def serve(prompts, config):
    settings = Settings(config)
    backend = Backend(prompts, Path(config).parent / "empty-workspace")
    if os.name != "nt":
        def stop(*_):
            backend.close()
            raise SystemExit(0)
        signal.signal(signal.SIGTERM, stop)
    try:
        for line in __import__("sys").stdin:
            request = {}
            try:
                request = json.loads(line)
                operation = request.get("op")
                options = validate_settings({**settings.value, **request.get("settings", {})})
                started = time.monotonic()
                if operation == "optimize":
                    value = {"prompt": backend.optimize(request.get("draft"), options)}
                elif operation == "models":
                    value = {"models": backend.models(options)}
                else:
                    raise ValueError("Unknown operation")
                reply = {"id": request.get("id"), "ok": True, "elapsedMs": round((time.monotonic()-started)*1000), **value}
            except Exception as error:
                reply = {"id": request.get("id") if isinstance(request, dict) else None,
                         "ok": False, "error": str(error)}
            print(json.dumps(reply, ensure_ascii=False), flush=True)
    finally:
        backend.close()


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--prompts", required=True)
    parser.add_argument("--config", required=True)
    arguments = parser.parse_args()
    serve(arguments.prompts, arguments.config)
