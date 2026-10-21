using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml;
using System.Xml.Serialization;

namespace TaskbarGames.Shared
{
    // 배포 버전은 UI 수정마다 바뀔 수 있다. Rule은 점수·판정·난수·경기장 변경 시 올린다.
    public static class ReleaseInfo
    {
        public const string Version = "0.3.0";
        public const int Protocol = 1;
        public static int Rule(GameKind game) { return 1; }
        public static bool Compatible(ServerStatus status, GameKind game)
        {
            Version minimum;
            RulePolicy rule;
            return status != null && status.protocol == Protocol
                && System.Version.TryParse(status.minimum_version, out minimum)
                && new Version(Version) >= minimum && status.games != null
                && status.games.TryGetValue(game.ToString(), out rule) && rule.rule == Rule(game);
        }
    }
    public sealed class RulePolicy { public int rule; public string season; public int seed; }
    public sealed class ServerStatus
    {
        public string latest_version, minimum_version, download_url;
        public int protocol;
        public Dictionary<string, RulePolicy> games;
    }
    public sealed class RankRow { public int rank; public string nickname; public long metric; public double achieved; }
    public sealed class Ranking { public string season; public RankRow[] rows; public RankRow mine; }
    public sealed class Registration { public string token, nickname; }
    public sealed class ServerMessage { public string message; }

    // 온라인 설정과 계정은 게임 기록과 분리한다. 토큰은 현재 Windows 사용자 키로 암호화한다.
    // 설치 폴더를 바꾸거나 EXE를 교체해도 유지되며 다른 PC로는 복구 토큰을 사용한다.
    public sealed class OnlineSettings
    {
        public string Endpoint = "", Nickname = "", ProtectedToken = "";
        public static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Taskbar_Games", "online.xml"); } }
        [XmlIgnore]
        public string Token
        {
            get
            {
                try { return String.IsNullOrEmpty(ProtectedToken) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedToken), null, DataProtectionScope.CurrentUser)); }
                catch (CryptographicException) { return ""; }
                catch (FormatException) { return ""; }
            }
            set { ProtectedToken = String.IsNullOrEmpty(value) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser)); }
        }
        public static OnlineSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new OnlineSettings();
                using (XmlReader reader = XmlReader.Create(FilePath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                    return (OnlineSettings)new XmlSerializer(typeof(OnlineSettings)).Deserialize(reader);
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is XmlException)) throw;
                return new OnlineSettings();
            }
        }
        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temporary = FilePath + ".tmp";
            using (FileStream stream = new FileStream(temporary, FileMode.Create))
            { new XmlSerializer(typeof(OnlineSettings)).Serialize(stream, this); stream.Flush(true); }
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak");
            else File.Move(temporary, FilePath);
        }
        public static string ValidateEndpoint(string value)
        {
            Uri uri;
            if (!Uri.TryCreate((value ?? "").Trim(), UriKind.Absolute, out uri) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0
                || uri.AbsolutePath != "/" || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
                throw new InvalidOperationException("서버 주소는 HTTPS를 사용하세요. 로컬 검사는 http://127.0.0.1:8765를 사용할 수 있습니다.");
            return uri.GetLeftPart(UriPartial.Authority);
        }
    }
    public sealed class OnlineRun
    {
        public string id, game, season;
        public int rule, seed;
        [ScriptIgnore] public OnlineClient Client;
        [ScriptIgnore] public RunStats Result;
        [ScriptIgnore] public bool Cancelled;
        // 완료 기록만 메모리에 보관한다. 서버 응답 유실 시 같은 경기 번호로 안전하게 재전송한다.
        public void Capture(RunStats stats)
        {
            if (Cancelled || !stats.Ended || Result != null) return;
            Result = new RunStats { Score = stats.Score, Attempts = stats.Attempts, Hits = stats.Hits,
                HomeRuns = stats.HomeRuns, Seconds = stats.Seconds, Cleared = stats.Cleared, Ended = true };
        }
    }
    public static class OnlineSession
    {
        public static OnlineRun Current;
        public static Random RandomFor(GameKind game)
        { return Current != null && Current.game == game.ToString() ? new Random(Current.seed) : new Random(); }
        public static void CancelForLayout()
        { if (Current != null) Current.Cancelled = true; }
    }
    public sealed class OnlineClient
    {
        readonly string endpoint, token;
        public OnlineClient(OnlineSettings settings)
        { endpoint = OnlineSettings.ValidateEndpoint(settings.Endpoint); token = settings.Token; }
        // 요청은 작업 스레드에서 수행한다. 리디렉션을 금지하여 인증 토큰을 다른 호스트에 전달하지 않는다.
        T Request<T>(string path, object body)
        {
            JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 65536 };
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(endpoint + "/v1/" + path);
            request.Timeout = request.ReadWriteTimeout = 5000; request.AllowAutoRedirect = false;
            request.Method = body == null ? "GET" : "POST";
            if (!String.IsNullOrEmpty(token)) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
            request.Accept = "application/json";
            if (body != null)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json.Serialize(body));
                request.ContentType = "application/json"; request.ContentLength = bytes.Length;
                using (Stream stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            }
            try
            {
                using (WebResponse response = request.GetResponse()) return json.Deserialize<T>(Read(response));
            }
            catch (WebException ex)
            {
                if (ex.Response != null)
                    using (WebResponse response = ex.Response)
                    {
                        try
                        {
                            Dictionary<string, object> error = json.Deserialize<Dictionary<string, object>>(Read(response));
                            if (error.ContainsKey("error")) throw new InvalidOperationException(Convert.ToString(error["error"]));
                        }
                        catch (ArgumentException) { }
                    }
                throw new IOException("서버에 연결하지 못했습니다. 오프라인 게임은 계속 이용할 수 있습니다.");
            }
        }
        static string Read(WebResponse response)
        {
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                char[] buffer = new char[65537]; int count = 0, read;
                while (count < buffer.Length && (read = reader.Read(buffer, count, buffer.Length - count)) > 0) count += read;
                if (count > 65536) throw new IOException("서버 응답이 너무 큽니다.");
                return new string(buffer, 0, count);
            }
        }
        public Task<ServerStatus> Status() { return Task.Run(() => Request<ServerStatus>("status", null)); }
        public Task<Registration> Register(string nickname, string invite)
        { return Task.Run(() => Request<Registration>("register", new { nickname = nickname, invite = invite })); }
        public Task<Ranking> Rankings(GameKind game)
        { return Task.Run(() => Request<Ranking>("leaderboard?game=" + game, null)); }
        public Task<OnlineRun> Start(GameKind game)
        {
            return Task.Run(() => {
                OnlineRun run = Request<OnlineRun>("start", new { game = game.ToString(), version = ReleaseInfo.Version, protocol = ReleaseInfo.Protocol, rule = ReleaseInfo.Rule(game) });
                if (run.game != game.ToString() || run.rule != ReleaseInfo.Rule(game) || String.IsNullOrEmpty(run.id))
                    throw new InvalidOperationException("경기 정보가 올바르지 않습니다.");
                run.Client = this; return run;
            });
        }
        public Task<ServerMessage> Submit(OnlineRun run)
        {
            if (run.Cancelled || run.Result == null) throw new InvalidOperationException("완료한 경기만 등록할 수 있습니다.");
            RunStats s = run.Result;
            return Task.Run(() => Request<ServerMessage>("submit", new {
                id = run.id, game = run.game, rule = run.rule, protocol = ReleaseInfo.Protocol, version = ReleaseInfo.Version,
                stats = new { score = s.Score, attempts = s.Attempts, hits = s.Hits, home_runs = s.HomeRuns, seconds = s.Seconds, cleared = s.Cleared }
            }));
        }
    }
}
