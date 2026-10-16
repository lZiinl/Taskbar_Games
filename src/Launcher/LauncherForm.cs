using System;
using System.Drawing;
using System.Windows.Forms;
using TaskbarGames.Games.Baseball;
using TaskbarGames.Games.Fortress;
using TaskbarGames.Shared;
using TaskbarGames.Games.Pet;
using TaskbarGames.Games.Runner;

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
            Icon = ArcadeTheme.AppIcon;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            MinimumSize = MaximumSize = new Size(58, 74);
            ClientSize = new Size(58, 74);
            BackColor = ArcadeTheme.Black;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            KeyPreview = true;
            GameCard fortress = new GameCard(false);
            fortress.Bounds = new Rectangle(3, 3, 24, 24);
            fortress.Click += delegate { OpenGame(delegate { return new FortressGame(); }); };
            GameCard baseball = new GameCard(true);
            baseball.Bounds = new Rectangle(31, 3, 24, 24);
            baseball.Click += delegate { OpenGame(delegate { return new BaseballGame(); }); };
            Controls.Add(fortress);
            Controls.Add(baseball);
            GameCard pet = new GameCard(false, true);
            pet.Bounds = new Rectangle(3, 31, 24, 24);
            pet.Click += delegate { OpenGame(delegate { return new PetGame(); }); };
            Controls.Add(pet);
            GameCard runner = new GameCard(false, false, false, true);
            runner.Bounds = new Rectangle(31, 31, 24, 24);
            runner.Click += delegate { OpenGame(delegate { return new RunnerGame(); }); };
            Controls.Add(runner);
            hints.SetToolTip(runner, "공룡 러너 · SPACE로 점프 · 짧게 누르면 낮게 점프");
            GameCard exit = new GameCard(false, false, true);
            exit.Name = "ExitButton";
            exit.Bounds = new Rectangle(3, 59, 52, 12);
            exit.Click += delegate { Close(); };
            Controls.Add(exit);
            hints.SetToolTip(exit, "종료 (Esc)");
            hints.SetToolTip(pet, "다람쥐 · 클릭해서 쓰다듬기 · 우클릭으로 먹이 주기");
            hints.SetToolTip(fortress, "포트리스 · SPACE 길게 눌러 거리 조절");
            hints.SetToolTip(baseball, "야구 · SPACE로 스윙");
            ArcadeTheme.Style(menu);
            menu.Items.Add("종료 (Esc)", null, delegate { Close(); });
            ContextMenuStrip = fortress.ContextMenuStrip = baseball.ContextMenuStrip = pet.ContextMenuStrip = runner.ContextMenuStrip = menu;
            PositionAboveTaskbar();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Handle Escape before the focused game button processes dialog keys.
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
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
            using (Pen border = new Pen(ArcadeTheme.Neon))
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
            readonly bool pet;
            readonly bool exit;
            readonly bool runner;
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

            public GameCard(bool isBaseball, bool isPet = false, bool isExit = false, bool isRunner = false)
            {
                baseball = isBaseball;
                pet = isPet;
                exit = isExit;
                runner = isRunner;
                AccessibleName = exit ? "종료" : runner ? "공룡 러너" : pet ? "다람쥐" : isBaseball ? "야구" : "포트리스";
                AccessibleDescription = exit ? "프로그램 종료 (Esc)" : runner ? "스페이스바로 점프" : pet ? "클릭해서 쓰다듬기, 우클릭으로 먹이 주기" : isBaseball ? "스페이스바로 스윙" : "스페이스바를 길게 눌러 거리 조절";
                Cursor = Cursors.Hand;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                MouseEnter += delegate { hovering = true; Invalidate(); };
                MouseLeave += delegate { hovering = false; Invalidate(); };
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(hovering || Focused ? Color.FromArgb(24, 49, 43) : ArcadeTheme.Panel);
                Color accent = exit ? ArcadeTheme.Pink : baseball ? ArcadeTheme.Cyan : ArcadeTheme.Neon;
                using (Pen border = new Pen(Focused || hovering ? accent : Color.FromArgb(36, 79, 64)))
                    g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
                if (exit)
                {
                    PixelArt.Text(g, "EXIT", accent, 14, 3);
                }
                else if (runner) PixelArt.Sprite(g, RunnerGame.Dino, 2, 3, ArcadeTheme.Neon, 1);
                else if (pet)
                {
                    System.Drawing.Drawing2D.GraphicsState state = g.Save();
                    g.TranslateTransform(2, 3);
                    g.ScaleTransform(0.625f, 0.625f);
                    PetGame.DrawSquirrel(g, 0, 0, true, "idle", 0, 1);
                    g.Restore(state);
                }
                else PixelArt.Sprite(g, baseball ? Ball : Tank, baseball ? 6 : 2, baseball ? 6 : 7, accent, 1);
            }
        }
    }
}
