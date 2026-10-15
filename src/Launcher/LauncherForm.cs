using System;
using System.Drawing;
using System.Windows.Forms;
using TaskbarGames.Games.Baseball;
using TaskbarGames.Games.Fortress;
using TaskbarGames.Shared;

namespace TaskbarGames.Launcher
{
    public class LauncherForm : Form
    {
        Form activeGame;
        bool shuttingDown;
        readonly ToolTip hints = new ToolTip();
        readonly ContextMenuStrip menu = new ContextMenuStrip();

        public LauncherForm()
        {
            Text = "Taskbar_Games";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            MinimumSize = MaximumSize = new Size(68, 36);
            ClientSize = new Size(68, 36);
            BackColor = Color.FromArgb(22, 30, 45);
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            KeyPreview = true;
            GameCard fortress = new GameCard(false);
            fortress.Bounds = new Rectangle(4, 4, 28, 28);
            fortress.Click += delegate { OpenGame(delegate { return new FortressGame(); }); };
            GameCard baseball = new GameCard(true);
            baseball.Bounds = new Rectangle(36, 4, 28, 28);
            baseball.Click += delegate { OpenGame(delegate { return new BaseballGame(); }); };
            Controls.Add(fortress);
            Controls.Add(baseball);
            hints.SetToolTip(fortress, "포트리스 · SPACE 길게 눌러 거리 조절");
            hints.SetToolTip(baseball, "야구 · SPACE로 스윙");
            menu.Items.Add("종료 (Esc)", null, delegate { Close(); });
            ContextMenuStrip = fortress.ContextMenuStrip = baseball.ContextMenuStrip = menu;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
            PositionAboveTaskbar();
        }

        void PositionAboveTaskbar()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 8, area.Bottom - Height - 8);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            if (Visible) PositionAboveTaskbar();
            base.OnVisibleChanged(e);
        }

        internal void OpenGame(Func<Form> createGame)
        {
            if (shuttingDown || activeGame != null) return;
            Form game = createGame();
            activeGame = game;
            game.FormClosed += GameClosed;
            Hide();
            try { game.Show(); game.Activate(); }
            catch
            {
                game.FormClosed -= GameClosed;
                activeGame = null;
                game.Dispose();
                Show();
                throw;
            }
        }

        void GameClosed(object sender, FormClosedEventArgs e)
        {
            if (!Object.ReferenceEquals(sender, activeGame)) return;
            activeGame.FormClosed -= GameClosed;
            activeGame = null;
            if (shuttingDown || IsDisposed) return;
            Show();
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;
            shuttingDown = true;
            if (activeGame != null) activeGame.Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen border = new Pen(Color.FromArgb(72, 90, 112)))
                e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                shuttingDown = true;
                if (activeGame != null) { activeGame.Dispose(); activeGame = null; }
                hints.Dispose();
                menu.Dispose();
            }
            base.Dispose(disposing);
        }

        sealed class GameCard : Button
        {
            readonly bool baseball;
            bool hovering;
            static readonly string[] Tank = {
                "....................", ".................DDD", ".......DDDD...DDDLLD",
                "......DLLLLDDDLLDDD.", "......DBBBBBDDDD....", "...DDDDBBBBBDDDDD...",
                "..DLLLLLLLLLLLLLBD..", "..DBBBBBBBBBBBBBBD..", ".DDCCCCCCCCCCCCCCDD.",
                "DWWDWWDWWDWWDWWDWWDD", ".DDDDDDDDDDDDDDDDDD."
            };
            static readonly string[] Ball = {
                "....DDDD....", "..DDWWWWDD..", ".DWWRWWWWWD.", ".DWWWRWWWWD.",
                "DWWWWRWWWWWD", "DWWWWRWWWWWD", "DWWWRWWWWWWD", "DWWRWWWWWWWD",
                ".DRWWWWWWWD.", ".DWWWWWWWWD.", "..DDWWWWDD..", "....DDDD...."
            };

            public GameCard(bool isBaseball)
            {
                baseball = isBaseball;
                AccessibleName = isBaseball ? "야구" : "포트리스";
                AccessibleDescription = isBaseball ? "스페이스바로 스윙" : "스페이스바를 길게 눌러 거리 조절";
                Cursor = Cursors.Hand;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                MouseEnter += delegate { hovering = true; Invalidate(); };
                MouseLeave += delegate { hovering = false; Invalidate(); };
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(hovering || Focused ? Color.FromArgb(43, 57, 76) : Color.FromArgb(30, 40, 55));
                Color accent = baseball ? Color.FromArgb(255, 202, 117) : Color.FromArgb(102, 235, 177);
                using (Pen border = new Pen(Focused || hovering ? accent : Color.FromArgb(49, 64, 82)))
                    g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
                PixelArt.Sprite(g, baseball ? Ball : Tank, baseball ? 8 : 4, baseball ? 8 : 9, accent, 1);
            }
        }
    }
}
