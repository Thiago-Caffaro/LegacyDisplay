"""Exercise the real JVM tablet server with the real Windows Agent. No third-party Python packages."""
import argparse
import json
from pathlib import Path
import queue
import subprocess
import tempfile
import threading
import time
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--java", default="java")
    args = parser.parse_args()
    dotnet = str(Path(args.dotnet).resolve()) if Path(args.dotnet).exists() else args.dotnet
    classpath = (ROOT / "android-client/app/build/poc-classpath.txt").read_text(encoding="utf-8")
    agent_dll = ROOT / "windows-agent/LegacyDisplay.Agent/bin/Debug/net8.0-windows/LegacyDisplay.Agent.dll"
    studio_dll = ROOT / "windows-studio/LegacyDisplay.Studio/bin/Debug/net8.0-windows/LegacyDisplay.Studio.dll"
    tablet = subprocess.Popen([args.java, "-Dstdout.encoding=UTF-8", "-Dstderr.encoding=UTF-8", "-Dfile.encoding=UTF-8", "-cp", classpath, "org.legacydisplay.client.PocTablet"],
                              cwd=ROOT / "android-client", stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                              stderr=subprocess.PIPE, text=True, encoding="utf-8")
    replies = queue.Queue()
    def read_replies():
        for line in tablet.stdout:
            replies.put(line)
    threading.Thread(target=read_replies, daemon=True).start()
    agent = None
    action_calls = queue.Queue()
    class ActionApi(BaseHTTPRequestHandler):
        def do_POST(self):
            body = self.rfile.read(int(self.headers.get('Content-Length', '0'))).decode('utf-8')
            action_calls.put((self.command, self.path, self.headers.get('Authorization'), body, self.headers.get('X-LegacyDisplay-Test')))
            self.send_response(204)
            self.send_header('Content-Length', '0')
            self.end_headers()
        def log_message(self, *args):
            pass
    api = ThreadingHTTPServer(('127.0.0.1', 0), ActionApi)
    threading.Thread(target=api.serve_forever, daemon=True).start()
    try:
        ready = json.loads(replies.get(timeout=20))
        device = f'http://127.0.0.1:{ready["port"]}'

        def command(value):
            tablet.stdin.write(value + "\n")
            tablet.stdin.flush()
            return json.loads(replies.get(timeout=10))

        def wait_for(predicate, seconds=12):
            deadline = time.monotonic() + seconds
            while time.monotonic() < deadline:
                state = command("state")
                if predicate(state):
                    return state
                time.sleep(0.15)
            raise AssertionError(f"Tablet state did not converge: {state}")

        def http_status(path, payload=None):
            data = None if payload is None else json.dumps(payload).encode()
            request = urllib.request.Request(device + path, data=data, headers={"Content-Type": "application/json"})
            try:
                with urllib.request.urlopen(request, timeout=5) as response:
                    return response.status
            except urllib.error.HTTPError as error:
                return error.code

        assert http_status("/api/v1/status") == 200
        assert http_status("/api/v1/layout") == 401
        assert http_status("/api/v1/pair", {"code": "wrong"}) == 400

        with tempfile.TemporaryDirectory(prefix="legacydisplay-interop-") as temporary:
            credentials = Path(temporary) / "device.json"

            def agent_command(*arguments):
                result = subprocess.run([dotnet, str(agent_dll), *arguments, "--credentials", str(credentials)],
                                        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", timeout=25)
                assert result.returncode == 0, result.stderr

            agent_command("pair", "--device", device, "--code", ready["code"])
            assert ready["code"] not in credentials.read_text(encoding="utf-8")
            assert http_status("/api/v1/pair", {"code": ready["code"]}) == 409
            reset = command("reset")
            studio_result = Path(temporary) / "studio-result.txt"
            action_url = f'http://127.0.0.1:{api.server_port}/action'
            gui = subprocess.run([dotnet, str(studio_dll), "--connection-actions-smoke-test", device, reset["code"], str(credentials), action_url, str(studio_result)],
                                 cwd=ROOT, capture_output=True, text=True, encoding="utf-8", timeout=60)
            error_file = Path(str(studio_result) + ".error.txt")
            assert gui.returncode == 0, error_file.read_text(encoding="utf-8") if error_file.exists() else "Studio connection test failed"
            print(studio_result.read_text(encoding="utf-8"))
            # Execute the action catalog created by the WPF controls through the actual tablet touch path.
            actions_path = Path(temporary) / 'actions.json'
            assert 'LOCAL_TEST_TOKEN' not in actions_path.read_text(encoding='utf-8')
            with open(Path(temporary) / 'custom-agent.log', 'w', encoding='utf-8') as custom_log:
                agent = subprocess.Popen([dotnet, str(agent_dll), 'run', '--interval', '100', '--credentials', str(credentials), '--actions', str(actions_path)], cwd=ROOT, stdout=custom_log, stderr=custom_log)
                try:
                    wait_for(lambda s: s['connected'] and 'pc.cpu.usage' in s['values'])
                    command('touch')
                    wait_for(lambda s: s['values'].get('action.lastResult') == 'Confirmação e API · sequência concluída')
                    method, path, auth, body, header = action_calls.get(timeout=5)
                    assert method == 'POST' and path == '/action' and auth == 'Bearer LOCAL_TEST_TOKEN'
                    assert json.loads(body) == {'enabled': True} and header == 'local'
                    assert action_calls.empty(), 'One touch must execute exactly one HTTP step'
                    print('PASS: WPF action editor -> encrypted catalog -> button chooser/deploy -> tablet touch -> Agent sequence -> HTTP method/body/auth -> tablet result.')
                finally:
                    agent.terminate(); agent.wait(timeout=10); agent = None
            # The distributed GPU example must pass both C# and Kotlin validators.
            agent_command("deploy", "--layout", str(ROOT / "protocol/examples/dashboard-gpu.json"))
            # A hello larger than the C# receive buffer also exercises fragmented reads.
            layout = json.loads((ROOT / "protocol/examples/dashboard.json").read_text(encoding="utf-8"))
            layout["widgets"].append({"id": "deny", "type": "button", "x": 40, "y": 1080, "width": 720, "height": 80,
                                       "text": "Unsupported action", "action": "unknown.action"})
            for index in range(40):
                layout["widgets"].append({"id": f"fixture-{index}", "type": "text", "x": 0, "y": 0, "width": 100, "height": 60, "text": "x" * 100})
            large_layout = Path(temporary) / "large-layout.json"
            large_layout.write_text(json.dumps(layout), encoding="utf-8")
            assert large_layout.stat().st_size > 4096
            agent_command("deploy", "--layout", str(large_layout))
            log = open(Path(temporary) / "agent.log", "w", encoding="utf-8")
            agent = subprocess.Popen([dotnet, str(agent_dll), "run", "--interval", "100", "--credentials", str(credentials), "--actions", str(actions_path)],
                                     cwd=ROOT, stdout=log, stderr=log, text=True)
            try:
                state = wait_for(lambda s: s["connected"] and "pc.cpu.usage" in s["values"])
                assert 0 <= state["values"]["pc.cpu.usage"] <= 100
                assert 0 <= state["values"]["pc.memory.usage"] <= 100
                assert "demo.gpu.temperature" not in state["values"]
                assert "pc.gpu.temperature" in state["values"] and "pc.gpu.powerWatts" in state["values"]
                assert len(state["values"]) <= 256
                command("touch")
                wait_for(lambda s: s["values"].get("action.lastResult") == "PC recebeu o toque!")
                command("touch-denied")
                wait_for(lambda s: s["values"].get("action.lastResult") == "Ação não permitida")
                # Deploy while connected exercises C# live layout.changed handling.
                agent_command("deploy", "--layout", str(ROOT / "protocol/examples/dashboard.json"))
                command("disconnect")
                wait_for(lambda s: s["connected"])
                # A changed marker proves that the second response is fresh after reconnect.
                previous_notice = command("state")["notice"]
                assert previous_notice == "PC conectado"
                command("touch")
                wait_for(lambda s: s["notice"] == "PC recebeu o toque!")
                assert agent.poll() is None
                command("reset")
                assert agent.wait(timeout=12) == 1, "Revoked credential must stop Agent with a useful error"
                print("PASS: auth, pairing, deploy (including GPU example), fragmented reads, live CPU/RAM/GPU sources, action allowlist/round-trip, live deploy, reconnect, token revocation.")
            finally:
                if agent.poll() is None:
                    agent.terminate()
                    agent.wait(timeout=10)
                log.close()
    finally:
        api.shutdown(); api.server_close()
        if agent is not None and agent.poll() is None:
            agent.terminate()
            agent.wait(timeout=10)
        if tablet.poll() is None:
            tablet.stdin.write("stop\n")
            tablet.stdin.flush()
            try:
                tablet.wait(timeout=10)
            except subprocess.TimeoutExpired:
                tablet.terminate()
                tablet.wait(timeout=10)


if __name__ == "__main__":
    main()
