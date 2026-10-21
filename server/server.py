"""지인용 기록 서버. Python 표준 라이브러리만 사용한다.

기본은 루프백 전용이다. 인터넷 운영 시 HTTPS 역방향 프록시 뒤에 배치한다.
서버는 계정/시즌/중복/점수 범위를 검증하지만 클라이언트 실행을 증명하지 않는다.
실시간 대전이나 입력 재현 기반 치트 방지 서버가 아니다.
"""
import argparse
import hashlib
import hmac
import json
import math
import os
import re
import secrets
import sqlite3
import threading
import time
from contextlib import contextmanager, closing
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse, parse_qs


class ApiError(Exception):
    def __init__(self, status, message):
        self.status, self.message = status, message


def digest(value):
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def version(value):
    if not isinstance(value, str) or not re.fullmatch(r"\d{1,5}\.\d{1,5}\.\d{1,5}", value):
        raise ApiError(400, "앱 버전 형식이 올바르지 않습니다.")
    return tuple(map(int, value.split(".")))


class Service:
    def __init__(self, database, policy, invite):
        if len(invite) < 12:
            raise ValueError("초대 코드는 12자 이상으로 설정하세요.")
        self.database, self.policy_path, self.invite = str(database), Path(policy), invite
        Path(self.database).parent.mkdir(parents=True, exist_ok=True)
        self.policy()  # 잘못된 정책은 시작 시 즉시 거부한다.
        with self.connect() as db:
            db.executescript("""
                CREATE TABLE IF NOT EXISTS players(
                    id TEXT PRIMARY KEY, nickname TEXT NOT NULL COLLATE NOCASE UNIQUE,
                    token_hash TEXT NOT NULL UNIQUE);
                CREATE TABLE IF NOT EXISTS runs(
                    id TEXT PRIMARY KEY, player TEXT NOT NULL, game TEXT NOT NULL,
                    season TEXT NOT NULL, rule INTEGER NOT NULL, seed INTEGER NOT NULL,
                    started REAL NOT NULL, payload_hash TEXT, metric INTEGER);
                CREATE TABLE IF NOT EXISTS scores(
                    player TEXT NOT NULL, game TEXT NOT NULL, season TEXT NOT NULL,
                    rule INTEGER NOT NULL, metric INTEGER NOT NULL, achieved REAL NOT NULL,
                    PRIMARY KEY(player, game, season, rule));
            """)

    @contextmanager
    def connect(self):
        db = sqlite3.connect(self.database, timeout=10)
        db.row_factory = sqlite3.Row
        try:
            with db:
                yield db
        finally:
            db.close()

    def policy(self):
        data = json.loads(self.policy_path.read_text(encoding="utf-8-sig"))
        if version(data["minimum_version"]) > version(data["latest_version"]):
            raise ValueError("minimum_version exceeds latest_version")
        if data["protocol"] != 1:
            raise ValueError("This server supports protocol 1 only")
        link = data.get("download_url", "")
        if link and (urlparse(link).scheme != "https" or not urlparse(link).netloc):
            raise ValueError("download_url must use HTTPS")
        for game in ("Baseball", "Fortress", "Runner"):
            rule = data["games"][game]
            if not isinstance(rule["rule"], int) or rule["rule"] < 1:
                raise ValueError("Invalid rule")
            if not re.fullmatch(r"[a-zA-Z0-9_-]{1,40}", rule["season"]):
                raise ValueError("Invalid season")
            if not isinstance(rule["seed"], int) or not 0 <= rule["seed"] < 2147483647:
                raise ValueError("Invalid seed")
        return data

    def compatible(self, body, policy):
        if body.get("protocol") != policy["protocol"] or version(body.get("version")) < version(policy["minimum_version"]):
            raise ApiError(409, "온라인 도전을 이용하려면 업데이트해 주세요.")
        game = body.get("game")
        rule = policy["games"].get(game)
        if rule is None or body.get("rule") != rule["rule"]:
            raise ApiError(409, "경기 규칙이 변경되었습니다. 업데이트 후 다시 시작해 주세요.")
        return game, rule

    def player(self, db, token):
        row = db.execute("SELECT * FROM players WHERE token_hash=?", (digest(token),)).fetchone()
        if row is None:
            raise ApiError(401, "참가 등록이 필요합니다.")
        return row

    def register(self, body):
        if not isinstance(body.get("invite"), str) or not hmac.compare_digest(body["invite"].encode(), self.invite.encode()):
            raise ApiError(403, "초대 코드가 올바르지 않습니다.")
        nickname = body.get("nickname", "")
        if not isinstance(nickname, str) or not 2 <= len(nickname.strip()) <= 16 or any(ord(c) < 32 for c in nickname):
            raise ApiError(400, "닉네임은 2~16자로 입력해 주세요.")
        token, player_id = secrets.token_urlsafe(32), secrets.token_hex(16)
        try:
            with self.connect() as db:
                db.execute("INSERT INTO players VALUES(?,?,?)", (player_id, nickname.strip(), digest(token)))
        except sqlite3.IntegrityError:
            raise ApiError(409, "이미 사용 중인 닉네임입니다. 기존 계정은 운영자에게 복구를 요청하세요.")
        return {"token": token, "nickname": nickname.strip()}

    def start(self, token, body):
        game, rule = self.compatible(body, self.policy())
        with self.connect() as db:
            player = self.player(db, token)
            now = time.time()
            # 미완료 티켓은 사용자당 하나만 유지한다. 오래된 미완료 행은 정리한다.
            db.execute("DELETE FROM runs WHERE payload_hash IS NULL AND (player=? OR started<?)",
                       (player["id"], now - 86400))
            run = secrets.token_hex(24)
            db.execute("INSERT INTO runs VALUES(?,?,?,?,?,?,?,NULL,NULL)",
                       (run, player["id"], game, rule["season"], rule["rule"], rule["seed"], now))
        return {"id": run, "game": game, "season": rule["season"], "rule": rule["rule"], "seed": rule["seed"]}

    @staticmethod
    def metric(game, stats):
        # False/True도 Python에서는 int이므로 type을 정확하게 검사한다.
        for field in ("score", "attempts", "hits", "home_runs"):
            if type(stats.get(field)) is not int or not 0 <= stats[field] <= 20000:
                raise ApiError(400, "기록 숫자가 올바르지 않습니다.")
        seconds = stats.get("seconds")
        if type(seconds) not in (int, float) or not math.isfinite(seconds) or not 0 <= seconds <= 120:
            raise ApiError(400, "생존 시간이 올바르지 않습니다.")
        if type(stats.get("cleared")) is not bool:
            raise ApiError(400, "완주 값이 올바르지 않습니다.")
        if game == "Runner":
            if stats["attempts"] or stats["hits"] or stats["home_runs"]:
                raise ApiError(400, "러너 기록 형식이 올바르지 않습니다.")
            if abs(stats["score"] - seconds * 56) > 2 or stats["cleared"] != (seconds == 120):
                raise ApiError(400, "러너 점수와 시간이 일치하지 않습니다.")
            return round(seconds * 1000)
        if stats["attempts"] != 10 or not 0 <= stats["home_runs"] <= stats["hits"] <= 10 or seconds != 0 or stats["cleared"]:
            raise ApiError(400, "완료한 10회 도전만 등록할 수 있습니다.")
        if stats["score"] % 10 or stats["score"] > stats["hits"] * (1600 if game == "Baseball" else 2000):
            raise ApiError(400, "점수가 허용 범위를 벗어났습니다.")
        if game == "Fortress" and stats["home_runs"]:
            raise ApiError(400, "포트리스 기록 형식이 올바르지 않습니다.")
        return stats["score"]

    def submit(self, token, body):
        game, rule = self.compatible(body, self.policy())
        stats = body.get("stats")
        if not isinstance(stats, dict):
            raise ApiError(400, "기록이 없습니다.")
        metric = self.metric(game, stats)
        payload_hash = digest(json.dumps(stats, sort_keys=True))
        with self.connect() as db:
            db.execute("BEGIN IMMEDIATE")
            player = self.player(db, token)
            run = db.execute("SELECT * FROM runs WHERE id=? AND player=?", (body.get("id"), player["id"])).fetchone()
            if run is None or run["game"] != game:
                raise ApiError(404, "경기를 찾을 수 없습니다.")
            if run["season"] != rule["season"] or run["rule"] != rule["rule"] or run["seed"] != rule["seed"]:
                raise ApiError(409, "시즌이 변경되어 이 경기는 등록할 수 없습니다.")
            if run["payload_hash"]:
                if run["payload_hash"] != payload_hash:
                    raise ApiError(409, "이미 다른 결과로 등록된 경기입니다.")
                return {"message": "이미 등록된 기록입니다."}
            age = time.time() - run["started"]
            minimum = stats["seconds"] if game == "Runner" else 5
            if age < minimum - 0.5 or age > 86400:
                raise ApiError(409, "경기 시간이 올바르지 않거나 등록 기한이 지났습니다.")
            db.execute("UPDATE runs SET payload_hash=?,metric=? WHERE id=?", (payload_hash, metric, run["id"]))
            old = db.execute("SELECT metric FROM scores WHERE player=? AND game=? AND season=? AND rule=?",
                             (player["id"], game, rule["season"], rule["rule"])).fetchone()
            if old is None or metric > old["metric"]:
                db.execute("INSERT OR REPLACE INTO scores VALUES(?,?,?,?,?,?)",
                           (player["id"], game, rule["season"], rule["rule"], metric, time.time()))
        return {"message": "기록 등록 완료"}

    def leaderboard(self, token, game):
        policy = self.policy()
        if game not in policy["games"]:
            raise ApiError(400, "게임을 선택해 주세요.")
        rule = policy["games"][game]
        with self.connect() as db:
            player = self.player(db, token)
            # 동점은 공동 순위. 같은 점수의 표시 순서는 최초 달성 시각으로 안정화한다.
            rows = [dict(row) for row in db.execute("""
                SELECT p.id, p.nickname, s.metric, s.achieved,
                RANK() OVER(ORDER BY s.metric DESC) AS rank
                FROM scores s JOIN players p ON p.id=s.player
                WHERE game=? AND season=? AND rule=?
                ORDER BY s.metric DESC, s.achieved ASC, p.id ASC
            """, (game, rule["season"], rule["rule"]))]
        mine = next((r for r in rows if r["id"] == player["id"]), None)
        for row in rows:
            del row["id"]
        return {"season": rule["season"], "rows": rows[:10], "mine": mine}


class Handler(BaseHTTPRequestHandler):
    # 로그에 초대 코드, 계정 토큰, 닉네임, 요청 본문을 남기지 않는다.
    def log_message(self, *_):
        pass

    def do_GET(self):
        self.handle_api()

    def do_POST(self):
        self.handle_api()

    def handle_api(self):
        try:
            with self.server.rate_lock:
                now, ip = time.monotonic(), self.client_address[0]
                # 프록시 헤더는 신뢰하지 않는다. 프록시 운영 시 별도 요청 제한도 권장한다.
                self.server.rates = {k: v for k, v in self.server.rates.items() if now - v[0] < 60}
                start, count = self.server.rates.get(ip, (now, 0))
                if count >= 120:
                    raise ApiError(429, "요청이 많습니다. 잠시 후 다시 시도해 주세요.")
                self.server.rates[ip] = (start, count + 1)
            self.connection.settimeout(5)
            length = int(self.headers.get("Content-Length", "0"))
            if not 0 <= length <= 16384:
                raise ApiError(413, "요청이 너무 큽니다.")
            body = json.loads(self.rfile.read(length)) if length else {}
            if not isinstance(body, dict):
                raise ApiError(400, "요청 형식이 올바르지 않습니다.")
            token = self.headers.get("Authorization", "").removeprefix("Bearer ")
            route = urlparse(self.path)
            service = self.server.service
            if self.command == "GET" and route.path == "/v1/status":
                result = service.policy()
            elif self.command == "GET" and route.path == "/v1/leaderboard":
                result = service.leaderboard(token, parse_qs(route.query).get("game", [""])[0])
            elif self.command == "POST" and route.path == "/v1/register":
                result = service.register(body)
            elif self.command == "POST" and route.path == "/v1/start":
                result = service.start(token, body)
            elif self.command == "POST" and route.path == "/v1/submit":
                result = service.submit(token, body)
            else:
                raise ApiError(404, "요청 경로가 없습니다.")
            self.reply(200, result)
        except ApiError as error:
            self.reply(error.status, {"error": error.message})
        except (ValueError, TypeError):
            self.reply(400, {"error": "요청 형식이 올바르지 않습니다."})
        except Exception:
            self.reply(503, {"error": "서버를 사용할 수 없습니다. 나중에 다시 시도해 주세요."})

    def reply(self, status, body):
        raw = json.dumps(body, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(raw)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(raw)


def make_server(service, host="127.0.0.1", port=8765):
    server = ThreadingHTTPServer((host, port), Handler)
    server.service, server.rates, server.rate_lock = service, {}, threading.Lock()
    server.daemon_threads = True
    return server


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--db", default="data/friends.sqlite")
    parser.add_argument("--policy", default="policy.json")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--backup", help="서버 실행 대신 SQLite 온라인 백업 생성")
    parser.add_argument("--recover", help="닉네임의 로그인 토큰 재발급 (이전 토큰 폐기)")
    args = parser.parse_args()
    service = Service(args.db, args.policy, os.environ.get("TASKBAR_INVITE", ""))
    if args.backup:
        destination = Path(args.backup)
        if destination.exists() or destination.resolve() == Path(args.db).resolve():
            raise ValueError("백업은 기존 파일을 덮어쓸 수 없습니다.")
        destination.parent.mkdir(parents=True, exist_ok=True)
        with service.connect() as source, closing(sqlite3.connect(destination)) as target:
            source.backup(target)
        print("백업 완료")
    elif args.recover:
        token = secrets.token_urlsafe(32)
        with service.connect() as db:
            cursor = db.execute("UPDATE players SET token_hash=? WHERE nickname=?", (digest(token), args.recover))
            if cursor.rowcount != 1:
                raise ValueError("닉네임을 찾을 수 없습니다.")
        print("복구 토큰 (해당 지인에게만 전달): " + token)
    else:
        server = make_server(service, args.host, args.port)
        print("Taskbar friends server listening on " + str(server.server_address))
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass
        finally:
            server.server_close()


if __name__ == "__main__":
    main()
