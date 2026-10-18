using System;
using System.Drawing;
using System.Windows.Forms;
using TaskbarGames.Shared;

namespace TaskbarGames.Launcher
{
    public sealed class ModeSelectionForm : Form
    {
        readonly ArcadeHints hints = new ArcadeHints();
        public GameMode SelectedMode { get; private set; }
        public ModeSelectionForm(GameKind game)
        {
            Text = GameUi.Name(game) + " · 모드 선택"; Icon = ArcadeTheme.AppIcon;
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            MinimumSize = MaximumSize = ClientSize = GameEnvironment.SelectionSize;
            BackColor = ArcadeTheme.Black; TopMost = true; StartPosition = FormStartPosition.Manual;
            Location = GameEnvironment.SelectionLocation(Size); DoubleBuffered = true;
            Add("CHAL", "도전 모드", game == GameKind.Runner ? "최고 속도 560px/s에서 120초 생존" : game == GameKind.Baseball ? "10구 총점 도전 · 한 판 기준 칭호 획득" : "10발 총점 도전 · 멀리 맞힐수록 높은 점수", 3, 20,
                delegate { SelectedMode = GameMode.Challenge; DialogResult = DialogResult.OK; Close(); });
            Add("PRACT", "연습 모드", "횟수 제한 없이 연습 · 도전 칭호는 획득하지 않음", 26, 18,
                delegate { SelectedMode = GameMode.Practice; DialogResult = DialogResult.OK; Close(); });
            Add("RECORD", "기록과 칭호", "모드별 기록 확인 · 획득한 칭호 장착", 47, 10,
                delegate { using (RecordsForm records = new RecordsForm(game)) records.ShowDialog(this); });
            Add("BACK", "게임 선택으로 돌아가기", "돌아가기 (Esc)", 59, 12,
                delegate { DialogResult = DialogResult.Cancel; Close(); });
        }
        void Add(string label, string accessible, string hint, int top, int height, EventHandler click)
        {
            Button button = new PixelMenuButton(label) { Bounds = new Rectangle(3, top, 52, height), AccessibleName = accessible };
            button.Click += click; hints.SetToolTip(button, hint); Controls.Add(button);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        { if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; } return base.ProcessCmdKey(ref msg, keyData); }
        protected override void OnPaint(PaintEventArgs e)
        { base.OnPaint(e); using (Pen p = new Pen(ArcadeTheme.Neon)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); }
        protected override void Dispose(bool disposing) { if (disposing) hints.Dispose(); base.Dispose(disposing); }
    }

    internal sealed class PixelMenuButton : Button
    {
        readonly string label;
        bool hovering;
        public Color Accent { get; private set; }
        public PixelMenuButton(string text)
        {
            label = Text = text; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
            Accent = text == "EXIT" || text == "BACK" || text == "X" ? ArcadeTheme.Pink
                : text == "CHAL" ? Color.FromArgb(255, 233, 77) : text == "PRACT" ? ArcadeTheme.Neon : ArcadeTheme.Cyan;
            MouseEnter += delegate { hovering = true; Invalidate(); }; MouseLeave += delegate { hovering = false; Invalidate(); };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(hovering || Focused ? Color.FromArgb(24, 49, 43) : ArcadeTheme.Panel);
            using (Pen p = new Pen(Focused || hovering ? Accent : Color.FromArgb(36, 79, 64))) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            PixelArt.Text(e.Graphics, label, Accent, (Width - PixelArt.Width(label)) / 2, Height == 12 ? 3 : (Height - 7) / 2);
        }
    }

    public sealed class RecordsForm : Form
    {
        readonly Button games = new CompactTextButton();
        readonly ArcadeList titles = new ArcadeList();
        readonly Label summary = new Label();
        readonly Button equip = new CompactTextButton();
        readonly ArcadeHints hints = new ArcadeHints();
        readonly RecordInfoPopup info = new RecordInfoPopup();
        Control infoSource;
        string summaryDetail = "";
        GameKind selectedGame;
        GameKind Game { get { return selectedGame; } }
        public RecordsForm(GameKind game)
        {
            GameUi.Setup(this, "기록실 · 칭호", new Size(174, 148));
            games.Bounds = new Rectangle(23, 3, 96, 20); GameUi.Style(games);
            Button previous = new CompactTextButton { Text = "‹", Bounds = new Rectangle(4, 3, 18, 20), AccessibleName = "이전 게임" };
            Button next = new CompactTextButton { Text = "›", Bounds = new Rectangle(120, 3, 18, 20), AccessibleName = "다음 게임" };
            hints.SetToolTip(previous, "이전 게임 (←)"); hints.SetToolTip(next, "다음 게임 (→)");
            previous.Click += delegate { ChangeGame(-1); }; next.Click += delegate { ChangeGame(1); };
            summary.Bounds = new Rectangle(4, 27, 166, 27);
            titles.Bounds = new Rectangle(4, 56, 166, 69); titles.BackColor = ArcadeTheme.Panel; titles.ForeColor = ArcadeTheme.Neon;
            titles.ItemColor = item => ((TitleItem)item).Color;
            titles.MouseMove += delegate(object sender, MouseEventArgs e) {
                int index = titles.IndexFromPoint(e.Location);
                ShowTitle(index < 0 ? null : (TitleItem)titles.Items[index]);
            };
            titles.MouseLeave += delegate { HideInfo(titles); };
            titles.Scrolled += delegate { info.Hide(); };
            summary.MouseEnter += delegate { info.Present(this, "RECORD", summaryDetail, ArcadeTheme.Cyan); infoSource = summary; };
            summary.MouseLeave += delegate { HideInfo(summary); };
            equip.MouseEnter += delegate { ShowTitle(titles.SelectedItem as TitleItem, equip); };
            equip.MouseLeave += delegate { HideInfo(equip); };
            equip.Text = "착용 / 해제"; equip.Bounds = new Rectangle(4, 129, 166, 16);
            Button close = new PixelMenuButton("X") { Bounds = new Rectangle(142, 3, 28, 20), DialogResult = DialogResult.Cancel, AccessibleName = "기록실 닫기" };
            GameUi.Style(equip); GameUi.Style(close); CancelButton = close;
            equip.Padding = Padding.Empty; equip.FlatAppearance.BorderSize = 0;
            Controls.AddRange(new Control[] { games, summary, titles, equip, close, previous, next });
            games.Click += delegate { ChangeGame(1); };
            titles.SelectedIndexChanged += delegate {
                TitleItem item = titles.SelectedItem as TitleItem;
                equip.Enabled = item != null && item.Unlocked;
                if (titles.Focused) ShowTitle(item);
            };
            equip.Click += delegate {
                TitleItem item = titles.SelectedItem as TitleItem;
                if (item != null && item.Unlocked && Achievements.Equip(GameEnvironment.Store.Data, Game, item.Equipped ? "" : item.Id)) { GameEnvironment.Store.Save(); RefreshRecords(); }
            };
            selectedGame = game; RefreshRecords();
            VisibleChanged += delegate { if (!Visible) info.Hide(); };
            LocationChanged += delegate { info.Hide(); };
        }
        void ChangeGame(int direction) { selectedGame = (GameKind)(((int)selectedGame + direction + 3) % 3); RefreshRecords(); }
        void HideInfo(Control source) { if (infoSource == source) { info.Hide(); infoSource = null; } }
        void ShowTitle(TitleItem item, Control source = null)
        { if (item == null) HideInfo(source ?? titles); else { info.Present(this, "TITLE", item.Description, item.Color); infoSource = source ?? titles; } }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Left || keyData == Keys.Right) { ChangeGame(keyData == Keys.Left ? -1 : 1); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        void RefreshRecords()
        {
            GameData data = GameEnvironment.Store.Data;
            games.Text = GameUi.Name(Game);
            GameRecord practice = Achievements.Record(data, Game, GameMode.Practice), challenge = Achievements.Record(data, Game, GameMode.Challenge);
            string fullSummary = "달성 일시: PC 현지 시간\n" + RecordDetails("연습", practice) + "\n" + RecordDetails("도전", challenge)
                + String.Format("\n도전 완료 {0}회", challenge.FinishedRuns);
            fullSummary += Game == GameKind.Fortress ? String.Format("\n전체 누적 {0}/{1}명중 · 최고 {2}콤보\n  달성: {3}", data.FortressHits, data.FortressShots, data.FortressBestCombo, RecordTime.Format(data.FortressBestComboAtUtc))
                : Game == GameKind.Baseball ? String.Format("\n전체 최고 {0}m · 누적 {1:N0}점\n  최고 비거리 달성: {2}", data.BaseballBestDistance, data.BaseballScore, RecordTime.Format(data.BaseballBestDistanceAtUtc))
                : String.Format(" · 성공 {0}회\n전체 최고 {1:N0}점 (이전 버전 포함)\n  달성: {2}", challenge.Clears, data.RunnerBest, RecordTime.Format(data.RunnerBestAtUtc));
            summary.Text = Game == GameKind.Runner ? String.Format("연습 {0:N0}점\n도전 {1:0.00} / 120초", practice.BestScore, challenge.BestSeconds)
                : String.Format("연습 {0:N0}점\n도전 {1:N0}점", practice.BestScore, challenge.BestScore);
            string error = GameEnvironment.Store.Error;
            if (error.Length > 0) summary.Text = "저장 오류 (마우스 올리기)";
            summaryDetail = GameUi.Name(Game) + "\n" + fullSummary + (error.Length == 0 ? "" : "\n" + error);
            string equipped = Achievements.Equipped(data, Game);
            titles.Items.Clear(); titles.Items.Add(new TitleItem(null, true, equipped == ""));
            foreach (Achievement a in Achievements.All)
                if (a.Game == Game) titles.Items.Add(new TitleItem(a, data.UnlockedTitles.Contains(a.Id), a.Id == equipped, RecordTime.TitleEarned(data, a.Id)));
            titles.SelectedIndex = 0;
            for (int i = 1; i < titles.Items.Count; i++) if (((TitleItem)titles.Items[i]).Equipped) titles.SelectedIndex = i;
            info.Hide();
        }
        string RecordDetails(string mode, GameRecord record)
        {
            string result = String.Format("{0} 최고 {1:N0}점 · {2}", mode, record.BestScore, RecordTime.Format(record.BestScoreAtUtc));
            if (Game == GameKind.Runner) return result + String.Format("\n  최장 {0:0.00}초 · {1}", record.BestSeconds, RecordTime.Format(record.BestSecondsAtUtc));
            result += String.Format("\n  최고 {0}히트 · {1}", record.BestHits, RecordTime.Format(record.BestHitsAtUtc));
            if (Game == GameKind.Baseball) result += String.Format("\n  최고 {0}홈런 · {1}", record.BestHomeRuns, RecordTime.Format(record.BestHomeRunsAtUtc));
            return result;
        }
        sealed class TitleItem
        {
            public readonly Achievement Achievement;
            public readonly bool Unlocked;
            public readonly bool Equipped;
            readonly DateTime? earnedAtUtc;
            public Color Color { get { return Equipped && Achievement != null ? Color.Yellow : Unlocked ? ArcadeTheme.Neon : Color.White; } }
            public string Description { get { return Achievement == null ? "칭호를 표시하지 않습니다." : Achievement.Name + "\n" + Achievement.Description + "\n" + (Equipped ? "착용 중 · 선택 후 버튼을 누르면 해제" : Unlocked ? "해금됨 · 선택 후 버튼을 누르면 착용" : "미해금") + (Unlocked ? "\n획득 일시: " + RecordTime.Format(earnedAtUtc) + " (PC 현지 시간)" : ""); } }
            public string Id { get { return Achievement == null ? "" : Achievement.Id; } }
            public TitleItem(Achievement a, bool unlocked, bool active, DateTime? earned = null) { Achievement = a; Unlocked = unlocked; Equipped = active; earnedAtUtc = earned; }
            public override string ToString() { return (Equipped ? "● " : Unlocked ? "· " : "○ ") + (Achievement == null ? "칭호 없음" : Achievement.Name); }
        }
        protected override void Dispose(bool disposing) { if (disposing) { hints.Dispose(); info.Dispose(); } base.Dispose(disposing); }
    }

    public sealed class ChallengeResultForm : Form
    {
        readonly ArcadeHints hints = new ArcadeHints();
        public ChallengeResultForm(GameKind game, RunStats stats, ProgressTracker progress)
        {
            GameUi.Setup(this, GameUi.Name(game) + " · 도전 결과", GameEnvironment.SelectionSize);
            string detail = game == GameKind.Runner
                ? String.Format("{0}\n생존 {1:0.00} / 120초", stats.Cleared ? "도전 성공!" : "도전 종료", stats.Seconds)
                : String.Format("{0:N0}점 · {1}/{2}히트{3}", stats.Score, stats.Hits, stats.Attempts, game == GameKind.Baseball ? "\n홈런 " + stats.HomeRuns + "개" : "");
            detail += "\n새 칭호: " + (progress.NewTitles.Count == 0 ? "없음" : String.Join(", ", new System.Collections.Generic.List<string>(progress.NewTitles).ToArray()));
            string[] lines = game == GameKind.Runner
                ? new[] { stats.Cleared ? "CLEAR" : "END", stats.Seconds.ToString("0.00") + "S", "120S GOAL", "NEW " + progress.NewTitles.Count }
                : new[] { "SCORE", stats.Score.ToString(), "HIT" + stats.Hits + "/" + stats.Attempts, game == GameKind.Baseball ? "HR " + stats.HomeRuns : "NEW " + progress.NewTitles.Count };
            for (int i = 0; i < lines.Length; i++)
            {
                PixelResultLabel label = new PixelResultLabel(lines[i]) { Bounds = new Rectangle(3, 4 + i * 10, 52, 9), AccessibleName = detail };
                hints.SetToolTip(label, detail); Controls.Add(label);
            }
            Button retry = new PixelMenuButton("RETRY") { Bounds = new Rectangle(3, 47, 52, 11), DialogResult = DialogResult.Retry, AccessibleName = "다시 도전" };
            Button back = new PixelMenuButton("BACK") { Bounds = new Rectangle(3, 59, 52, 12), DialogResult = DialogResult.Cancel, AccessibleName = "게임 선택" };
            hints.SetToolTip(retry, "다시 도전"); hints.SetToolTip(back, "게임 선택으로 돌아가기 (Esc)");
            CancelButton = back; Controls.AddRange(new Control[] { retry, back });
        }
        protected override void Dispose(bool disposing) { if (disposing) hints.Dispose(); base.Dispose(disposing); }
    }

    internal sealed class PixelResultLabel : Control
    {
        public PixelResultLabel(string text) { Text = text; }
        protected override void OnPaint(PaintEventArgs e)
        { e.Graphics.Clear(ArcadeTheme.Black); PixelArt.Text(e.Graphics, Text, Color.Silver, Math.Max(0, (Width - PixelArt.Width(Text)) / 2), 1); }
    }

    internal sealed class CompactTextButton : Button
    {
        public CompactTextButton() { FlatStyle = FlatStyle.Flat; Cursor = Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Focused ? Color.FromArgb(35, 42, 48) : ArcadeTheme.Panel);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ArcadeTheme.Neon : Color.Gray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(1, 1, Width - 2, Height - 2));
        }
    }

    internal static class GameUi
    {
        public static string Name(GameKind game) { return game == GameKind.Fortress ? "포트리스" : game == GameKind.Baseball ? "야구" : "다이노 러너"; }
        public static void Setup(Form form, string title, Size size)
        {
            form.Text = title; form.Icon = ArcadeTheme.AppIcon; form.AutoScaleMode = AutoScaleMode.None;
            form.FormBorderStyle = FormBorderStyle.None; form.MinimumSize = form.MaximumSize = form.ClientSize = size;
            form.MaximizeBox = form.MinimizeBox = false;
            form.BackColor = ArcadeTheme.Black; form.ForeColor = Color.WhiteSmoke;
            form.StartPosition = FormStartPosition.Manual; form.TopMost = true; form.ShowInTaskbar = false;
            form.Location = GameEnvironment.SelectionLocation(form.Size);
        }
        public static void Style(Button button)
        { button.FlatStyle = FlatStyle.Flat; button.BackColor = ArcadeTheme.Panel; button.ForeColor = ArcadeTheme.Neon; }
        public static void DrawBadge(Graphics graphics, GameStore store, GameKind game, ProgressTracker progress, int x, int y)
        {
            Achievement equipped = Achievements.Find(Achievements.Equipped(store.Data, game));
            string text = equipped == null ? "" : equipped.Name;
            if (text.Length == 0) return;
            TextRenderer.DrawText(graphics, text, SystemFonts.MessageBoxFont, new Point(x, y), Color.Gray, ArcadeTheme.Black,
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }
}
