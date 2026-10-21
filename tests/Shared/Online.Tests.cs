using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using TaskbarGames.Shared;

public static class OnlineRegressionTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        ServerStatus policy = new ServerStatus { protocol = 1, minimum_version = "0.3.0",
            games = new Dictionary<string, RulePolicy> { { "Baseball", new RulePolicy { rule = 1, season = "test" } } } };
        Assert(ReleaseInfo.Compatible(policy, GameKind.Baseball), "Compatible version rejected.");
        policy.minimum_version = "0.3.1";
        Assert(!ReleaseInfo.Compatible(policy, GameKind.Baseball), "Old app accepted.");
        policy.minimum_version = "0.3.0"; policy.games["Baseball"].rule = 2;
        Assert(!ReleaseInfo.Compatible(policy, GameKind.Baseball), "Different rules accepted.");
        policy.games["Baseball"].rule = 1; policy.protocol = 2;
        Assert(!ReleaseInfo.Compatible(policy, GameKind.Baseball), "Different protocol accepted.");
        Assert(OnlineSettings.ValidateEndpoint("http://127.0.0.1:8765") == "http://127.0.0.1:8765", "Local testing endpoint rejected.");
        foreach (string bad in new[] { "http://example.com", "file:///test", "https://user:password@example.com", "https://example.com/path" })
        {
            bool failed = false;
            try { OnlineSettings.ValidateEndpoint(bad); } catch (InvalidOperationException) { failed = true; }
            Assert(failed, "Unsafe endpoint accepted.");
        }
        OnlineSettings settings = new OnlineSettings(); settings.Token = "test-only-secret-value";
        using (StringWriter writer = new StringWriter())
        {
            new XmlSerializer(typeof(OnlineSettings)).Serialize(writer, settings);
            Assert(!writer.ToString().Contains("test-only-secret-value") && !writer.ToString().Contains("<Token>"), "Token saved in plaintext.");
            using (StringReader reader = new StringReader(writer.ToString()))
                Assert(((OnlineSettings)new XmlSerializer(typeof(OnlineSettings)).Deserialize(reader)).Token == settings.Token, "Protected token round trip failed.");
        }
        OnlineRun run = new OnlineRun();
        run.Capture(new RunStats { Score = 10 }); Assert(run.Result == null, "Unfinished game queued.");
        run.Capture(new RunStats { Score = 20, Ended = true });
        run.Capture(new RunStats { Score = 40, Ended = true }); Assert(run.Result.Score == 20, "Result replaced after completion.");
        run = new OnlineRun { Cancelled = true };
        run.Capture(new RunStats { Ended = true }); Assert(run.Result == null, "Cancelled layout game queued.");
        Console.WriteLine("PASS: online version/rule gates, endpoint policy, encrypted credentials and completed-run capture.");
    }

    // Python 테스트의 임시 로컬 HTTP 서버에 실제 C# 클라이언트로 연결한다.
    // 임시 계정만 만들고 앱 창과 실제 사용자 설정 파일은 사용하지 않는다.
    public static void Integration(string endpoint)
    {
        OnlineSettings settings = new OnlineSettings { Endpoint = endpoint };
        OnlineClient client = new OnlineClient(settings);
        ServerStatus status = client.Status().Result;
        Assert(ReleaseInfo.Compatible(status, GameKind.Baseball), "Server policy mismatch.");
        Registration registration = client.Register("client-test", "integration-invite-code").Result;
        settings.Token = registration.token; client = new OnlineClient(settings);
        OnlineRun run = client.Start(GameKind.Runner).Result;
        Assert(run.game == "Runner" && run.seed == 20261022, "Run ticket mismatch.");
        // 0초 즉시 충돌 기록은 왕복·중복 요청 검증용이다.
        run.Capture(new RunStats { Ended = true });
        Assert(client.Submit(run).Result.message.Length > 0, "Submission failed.");
        Assert(client.Submit(run).Result.message.Length > 0, "Idempotent retry failed.");
        Ranking ranks = client.Rankings(GameKind.Runner).Result;
        Assert(ranks.mine != null && ranks.mine.nickname == "client-test", "Own rank missing.");
        Console.WriteLine("PASS: C# client / HTTP server registration, policy, run, submission, retry and ranking.");
    }
}
