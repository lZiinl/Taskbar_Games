"""인터넷·실제 계정·게임 창 없이 임시 DB와 루프백 HTTP로 검증한다."""
import json
import subprocess
import tempfile
import threading
import time
import os
import sys
import sqlite3
import unittest
from pathlib import Path
from server import Service, ApiError, make_server


class ServerTests(unittest.TestCase):
    def setUp(self):
        self.test_root = (Path(__file__).resolve().parent.parent / "artifacts" / "tests").resolve()
        self.test_root.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(prefix="online-", dir=self.test_root)
        self.root = Path(self.temp.name)
        self.policy = json.loads(Path(__file__).with_name("policy.example.json").read_text())
        self.policy_path = self.root / "policy.json"
        self.write_policy()
        self.service = Service(self.root / "data.sqlite", self.policy_path, "integration-invite-code")
        self.token = self.service.register({"nickname": "tester", "invite": "integration-invite-code"})["token"]

    def tearDown(self):
        # 재귀 정리는 방금 생성한 테스트 하위 폴더에만 한정한다.
        self.assertEqual(self.root.resolve().parent, self.test_root)
        self.temp.cleanup()

    def write_policy(self):
        self.policy_path.write_text(json.dumps(self.policy), encoding="utf-8")

    def start(self, game="Baseball"):
        return self.service.start(self.token, {"game": game, "version": "0.3.0", "protocol": 1, "rule": 1})

    def result(self, run, score=1000):
        with self.service.connect() as db:
            db.execute("UPDATE runs SET started=? WHERE id=?", (time.time() - 200, run["id"]))
        return {**run, "version": "0.3.0", "protocol": 1,
                "stats": {"score": score, "attempts": 10, "hits": 1, "home_runs": 0, "seconds": 0, "cleared": False}}

    def test_register_and_auth(self):
        with self.assertRaises(ApiError):
            self.service.register({"nickname": "other", "invite": "wrong"})
        with self.assertRaises(ApiError):
            self.service.register({"nickname": "tester", "invite": "integration-invite-code"})
        with self.assertRaises(ApiError):
            self.service.start("forged", {"game": "Baseball", "version": "0.3.0", "protocol": 1, "rule": 1})
        with self.service.connect() as db:
            self.assertNotEqual(db.execute("SELECT token_hash FROM players").fetchone()[0], self.token)

    def test_versions_rules_and_seed(self):
        first = self.start()
        self.assertEqual(self.start()["seed"], first["seed"])
        for change in ({"version": "0.2.0"}, {"rule": 2}, {"protocol": 2}):
            with self.assertRaises(ApiError):
                self.service.start(self.token, {"game": "Baseball", "version": "0.3.0", "protocol": 1, "rule": 1, **change})

    def test_submission_best_retry_and_season(self):
        body = self.result(self.start())
        self.service.submit(self.token, body)
        self.service.submit(self.token, body)
        self.assertEqual(self.service.leaderboard(self.token, "Baseball")["mine"]["metric"], 1000)
        body["stats"]["score"] = 1100
        with self.assertRaises(ApiError):
            self.service.submit(self.token, body)
        self.service.submit(self.token, self.result(self.start(), 500))
        self.assertEqual(self.service.leaderboard(self.token, "Baseball")["mine"]["metric"], 1000)
        old = self.result(self.start())
        self.policy["games"]["Baseball"]["season"] = "friends-002"
        self.write_policy()
        with self.assertRaises(ApiError):
            self.service.submit(self.token, old)
        self.assertIsNone(self.service.leaderboard(self.token, "Baseball")["mine"])
        with self.service.connect() as db:
            self.assertEqual(db.execute("SELECT COUNT(*) FROM scores").fetchone()[0], 1)

    def test_ties_and_invalid_result(self):
        self.service.submit(self.token, self.result(self.start()))
        other = self.service.register({"nickname": "friend", "invite": "integration-invite-code"})["token"]
        self.token = other
        self.service.submit(self.token, self.result(self.start()))
        self.assertEqual([r["rank"] for r in self.service.leaderboard(self.token, "Baseball")["rows"]], [1, 1])
        bad = self.result(self.start(), 20000)
        with self.assertRaises(ApiError):
            self.service.submit(self.token, bad)
        bad["stats"]["score"] = 1000
        bad["stats"]["seconds"] = float("nan")
        with self.assertRaises(ApiError):
            self.service.submit(self.token, bad)

    def test_expiry_and_ownership(self):
        run = self.start()
        body = self.result(run)
        other = self.service.register({"nickname": "other", "invite": "integration-invite-code"})["token"]
        with self.assertRaises(ApiError):
            self.service.submit(other, body)
        with self.service.connect() as db:
            db.execute("UPDATE runs SET started=? WHERE id=?", (time.time() - 90000, run["id"]))
        with self.assertRaises(ApiError):
            self.service.submit(self.token, body)

    def test_backup_and_recovery(self):
        self.service.submit(self.token, self.result(self.start()))
        backup = self.root / "backup.sqlite"
        command = [sys.executable, str(Path(__file__).with_name("server.py")), "--db", self.service.database,
                   "--policy", str(self.policy_path)]
        env = dict(os.environ, TASKBAR_INVITE="integration-invite-code")
        result = subprocess.run(command + ["--backup", str(backup)], env=env, capture_output=True,
                                timeout=10, creationflags=subprocess.CREATE_NO_WINDOW)
        self.assertEqual(result.returncode, 0, result.stderr)
        db = sqlite3.connect(backup)
        try:
            self.assertEqual(db.execute("SELECT metric FROM scores").fetchone()[0], 1000)
        finally:
            db.close()
        result = subprocess.run(command + ["--recover", "tester"], env=env, capture_output=True,
                                timeout=10, creationflags=subprocess.CREATE_NO_WINDOW)
        self.assertEqual(result.returncode, 0, result.stderr)
        with self.assertRaises(ApiError):
            self.service.leaderboard(self.token, "Baseball")
        # 출력 인코딩과 무관하게 ASCII 토큰만 분리하며 비밀 값을 로그에 출력하지 않는다.
        replacement = result.stdout.strip().split(b": ")[-1].decode("ascii")
        self.assertEqual(self.service.leaderboard(replacement, "Baseball")["mine"]["metric"], 1000)

    def test_csharp_http_client(self):
        server = make_server(self.service, port=0)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            root = Path(__file__).resolve().parent.parent
            result = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass",
                                     "-File", str(root / "scripts/test-online-client.ps1"),
                                     "-Endpoint", "http://127.0.0.1:" + str(server.server_port)],
                                    capture_output=True, text=True, timeout=40,
                                    creationflags=subprocess.CREATE_NO_WINDOW)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn("PASS:", result.stdout)
        finally:
            server.shutdown()
            server.server_close()
            thread.join()


if __name__ == "__main__":
    unittest.main()
