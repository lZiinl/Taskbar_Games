using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using TaskbarGames.Shared;
using TaskbarGames.Games.Baseball;
using TaskbarGames.Games.Fortress;
using TaskbarGames.Games.Runner;

namespace TaskbarGames.Launcher
{
    // 설정·랭킹은 이 창에서만 표시한다. 작은 기존 런처/게임 화면은 확장하지 않는다.
    public sealed class OnlineForm : Form
    {
        readonly OnlineSettings settings = OnlineSettings.Load();
        readonly TextBox endpoint = new TextBox(), nickname = new TextBox(), invite = new TextBox();
        readonly ComboBox games = new ComboBox();
        readonly ListBox ranks = new ListBox();
        readonly Label status = new Label(), mine = new Label();
        readonly Button download, retry;
        ServerStatus policy;
        OnlineRun pending;
        bool busy;
        GameKind Game { get { return (GameKind)Math.Max(0, games.SelectedIndex); } }
        public OnlineForm()
        {
            GameUi.Setup(this, "온라인 · v" + ReleaseInfo.Version, new Size(310, 392));
            Label title = new Label { Text = "FRIENDS  ·  v" + ReleaseInfo.Version, Bounds = new Rectangle(10, 8, 260, 20) };
            Controls.Add(title);
            AddField("서버", endpoint, 34); AddField("닉네임", nickname, 61); AddField("초대/복구", invite, 88);
            endpoint.Text = settings.Endpoint; nickname.Text = settings.Nickname; invite.UseSystemPasswordChar = true;
            Button connect = Button("연결 / 확인", 10, 118, 92, () => Check());
            Button join = Button("참가", 108, 118, 92, () => Join(false));
            Button recover = Button("계정 복구", 206, 118, 94, () => Join(true));
            games.Bounds = new Rectangle(10, 151, 104, 24); games.DropDownStyle = ComboBoxStyle.DropDownList;
            games.Items.AddRange(new object[] { "포트리스", "야구", "공룡 러너" }); games.SelectedIndex = 0; Controls.Add(games);
            Button("순위", 120, 151, 60, () => RefreshRanks());
            Button("온라인 도전", 186, 151, 114, () => Play());
            ranks.Bounds = new Rectangle(10, 181, 290, 102); ranks.BackColor = ArcadeTheme.Panel; ranks.ForeColor = ArcadeTheme.Neon;
            Controls.Add(ranks);
            mine.Bounds = new Rectangle(10, 286, 290, 20); Controls.Add(mine);
            status.Bounds = new Rectangle(10, 310, 290, 42); status.Text = "서버 주소를 입력해 연결하세요."; Controls.Add(status);
            retry = Button("재등록", 10, 358, 70, () => SubmitPending()); retry.Enabled = false;
            download = Button("업데이트", 86, 358, 102, () => {
                Uri uri;
                if (policy == null || !Uri.TryCreate(policy.download_url, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0)
                    throw new InvalidOperationException("배포 주소가 아직 등록되지 않았습니다.");
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                return Task.FromResult(0);
            });
            download.Enabled = false;
            Button close = new Button { Text = "닫기", Bounds = new Rectangle(194, 358, 106, 25) };
            GameUi.Style(close); close.Click += delegate { if (!busy) Close(); }; Controls.Add(close);
            CancelButton = close;
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
        }
        void AddField(string label, TextBox box, int y)
        {
            Controls.Add(new Label { Text = label, Bounds = new Rectangle(10, y + 3, 64, 21) });
            box.Bounds = new Rectangle(76, y, 224, 23); box.BackColor = ArcadeTheme.Panel; box.ForeColor = Color.WhiteSmoke; Controls.Add(box);
        }
        Button Button(string text, int x, int y, int width, Func<Task> action)
        {
            Button button = new Button { Text = text, Bounds = new Rectangle(x, y, width, 25) };
            GameUi.Style(button);
            button.Click += async delegate {
                if (busy) return;
                busy = true; SetInputs(false);
                try { await action(); }
                catch (Exception ex) { status.Text = ex.Message; }
                finally { busy = false; SetInputs(true); }
            };
            Controls.Add(button); return button;
        }
        void SetInputs(bool enabled)
        {
            foreach (Control control in Controls)
                if (control is Button || control is TextBox || control is ComboBox) control.Enabled = enabled;
            download.Enabled = enabled && policy != null && !String.IsNullOrEmpty(policy.download_url);
            retry.Enabled = enabled && pending != null && pending.Result != null && !pending.Cancelled;
        }
        OnlineClient Client()
        {
            string address = OnlineSettings.ValidateEndpoint(endpoint.Text);
            if (address != settings.Endpoint)
            {
                if (pending != null) throw new InvalidOperationException("재등록할 기록이 있습니다. 창을 닫은 뒤 서버를 변경하세요.");
                settings.Endpoint = address; settings.Token = ""; policy = null;
            }
            settings.Save(); return new OnlineClient(settings);
        }
        async Task Check()
        {
            policy = await Client().Status();
            Version latest;
            if (!Version.TryParse(policy.latest_version, out latest)) throw new InvalidOperationException("버전 정보가 올바르지 않습니다.");
            status.Text = latest > new Version(ReleaseInfo.Version) ? "새 버전 " + policy.latest_version + " · 업데이트 가능" : "현재 버전 " + ReleaseInfo.Version;
            if (!ReleaseInfo.Compatible(policy, Game)) status.Text += "\n온라인 도전에 업데이트가 필요합니다.";
        }
        async Task Join(bool recovery)
        {
            OnlineClient client = Client();
            if (recovery)
            {
                string previous = settings.Token;
                settings.Token = invite.Text.Trim();
                try { await new OnlineClient(settings).Rankings(Game); }
                catch { settings.Token = previous; throw; }
                settings.Nickname = nickname.Text.Trim();
            }
            else
            {
                if (settings.Token.Length > 0) throw new InvalidOperationException("이미 참가한 계정입니다. 순위를 확인해 주세요.");
                Registration account = await client.Register(nickname.Text.Trim(), invite.Text);
                settings.Token = account.token; settings.Nickname = account.nickname;
            }
            settings.Save(); invite.Clear(); status.Text = "참가 완료 · " + settings.Nickname;
            await RefreshRanks();
        }
        async Task RefreshRanks()
        {
            Ranking ranking = await Client().Rankings(Game);
            ranks.Items.Clear();
            ranks.Items.Add("시즌 " + ranking.season);
            foreach (RankRow row in ranking.rows) ranks.Items.Add(Row(row));
            mine.Text = ranking.mine == null ? "내 기록 없음" : "내 순위 · " + Row(ranking.mine);
        }
        string Row(RankRow row)
        { return row.rank + "위  " + row.nickname + "  " + (Game == GameKind.Runner ? (row.metric / 1000.0).ToString("0.000") + "초" : row.metric + "점"); }
        async Task Play()
        {
            if (pending != null) throw new InvalidOperationException("이전 기록을 먼저 재등록하세요. 포기하려면 창을 닫고 다시 열어 주세요.");
            OnlineClient client = Client();
            policy = await client.Status();
            if (!ReleaseInfo.Compatible(policy, Game)) throw new InvalidOperationException("온라인 도전에 업데이트가 필요합니다.");
            int requiredWidth = Game == GameKind.Fortress ? 800 : 480;
            if (GameEnvironment.Display.WorkingArea.Width < requiredWidth)
                throw new InvalidOperationException("온라인 경기에 필요한 화면 너비가 부족합니다.");
            pending = await client.Start(Game);
            OnlineSession.Current = pending;
            Hide();
            try
            {
                using (Form game = Game == GameKind.Fortress ? (Form)new FortressGame(GameMode.Challenge)
                    : Game == GameKind.Baseball ? (Form)new BaseballGame(GameMode.Challenge) : new RunnerGame(GameMode.Challenge))
                    game.ShowDialog(this);
            }
            finally { OnlineSession.Current = null; Show(); Activate(); }
            if (pending.Cancelled || pending.Result == null)
            {
                status.Text = pending.Cancelled ? "화면 변경으로 온라인 기록 등록이 취소되었습니다." : "중단한 경기는 등록하지 않습니다.";
                pending = null; return;
            }
            await SubmitPending();
        }
        async Task SubmitPending()
        {
            if (pending == null) return;
            ServerMessage result = await pending.Client.Submit(pending);
            pending = null; status.Text = result.message;
            await RefreshRanks();
        }
    }
}
