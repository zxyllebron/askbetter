"""Deterministic fake app-server; never contacts a model or reads account files."""
import argparse
import json
from pathlib import Path
import sys
import time

parser = argparse.ArgumentParser()
parser.add_argument("--scenario", default="success")
parser.add_argument("--record")
parser.add_argument("--delay", type=float, default=0)
arguments = parser.parse_args()
threads = 0
draft = ""
turn_id = ""
thread_id = ""


def send(value):
    print(json.dumps(value, ensure_ascii=False), flush=True)


def reply(request, value):
    send({"id": request["id"], "result": value})


def finish():
    if arguments.delay:
        time.sleep(arguments.delay)
    if arguments.scenario == "timeout":
        time.sleep(10)
        return
    text = json.dumps({"prompt": "优化："+draft}, ensure_ascii=False)
    if arguments.scenario == "empty":
        text = '{"prompt":""}'
    if arguments.scenario == "invalid":
        text = "unexpected text"
    # A stale result must never be selected for the current draft.
    send({"method":"item/completed","params":{"threadId":"old-thread","item":{"type":"agentMessage","text":"stale"}}})
    send({"method":"item/completed","params":{"threadId":thread_id,"turnId":"old-turn","item":{"type":"agentMessage","text":"stale-turn"}}})
    send({"method":"item/completed","params":{"threadId":thread_id,"turnId":turn_id,"item":{"type":"agentMessage","phase":"commentary","text":"not final"}}})
    send({"method":"item/completed","params":{"threadId":thread_id,"turnId":turn_id,"item":{"type":"agentMessage","text":text}}})
    send({"method":"turn/completed","params":{"threadId":thread_id,"turn":{"id":turn_id,"status":"completed"}}})


for line in sys.stdin:
    request = json.loads(line)
    if arguments.record:
        with Path(arguments.record).open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(request, ensure_ascii=False)+"\n")
    method, params = request.get("method"), request.get("params", {})
    if method == "initialize":
        reply(request, {})
    elif method == "thread/start":
        if arguments.scenario == "crash":
            sys.exit(0)
        if arguments.scenario == "error":
            send({"id":request["id"],"error":{"message":"model unavailable"}})
            continue
        threads += 1
        thread_id, turn_id = "thread-"+str(threads), "turn-"+str(threads)
        reply(request, {"thread":{"id":thread_id}, "model":"other-model" if arguments.scenario == "wrongmodel" else params["model"],
                        "serviceTier":"standard" if arguments.scenario == "wrongtier" else "priority"})
    elif method == "turn/start":
        draft = json.loads(params["input"][0]["text"])["draft"]
        if arguments.scenario == "early":
            # Item notification before the RPC response is legal.
            send({"method":"item/completed","params":{"threadId":thread_id,"turnId":turn_id,"item":{"type":"agentMessage","text":json.dumps({"prompt":"优化："+draft},ensure_ascii=False)}}})
            send({"method":"turn/completed","params":{"threadId":thread_id,"turn":{"id":turn_id,"status":"completed"}}})
            reply(request, {"turn":{"id":turn_id}})
        else:
            reply(request, {"turn":{"id":turn_id}})
            if arguments.scenario == "tool":
                send({"id":900,"method":"item/tool/request","params":{}})
            else:
                finish()
    elif "error" in request and request.get("id") == 900:
        finish()
    elif method == "model/list":
        if params.get("cursor"):
            reply(request,{"data":[{"id":"gpt-6.1-sol"},{"id":"bad model"}],"nextCursor":None})
        else:
            reply(request,{"data":[{"model":"gpt-6-luna"}],"nextCursor":"page2"})
    elif method == "thread/unsubscribe":
        reply(request,{})
