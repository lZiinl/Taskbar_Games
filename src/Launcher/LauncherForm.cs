// 게임 선택과 창 수명을 관리하는 주 창. 게임은 동시에 하나만 열린다.
// 선택창은 화면 오른쪽 아래에 배치되며 게임 4개와 별도 종료 버튼을 제공한다.

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
        // 현재 런처가 소유한 게임 창. null일 때만 새 게임을 생성한다.
        // shuttingDown은 종료 도중 게임의 FormClosed가 런처를 다시 표시하지 않게 하는 보호 플래그다.
        Form activeGame;
        bool shuttingDown;
        readonly Timer displayTimer = new Timer();
        readonly ArcadeHints hints = new ArcadeHints();
        readonly ContextMenuStrip menu = new ContextMenuStrip();

        // 네이티브 최소 창 너비의 자동 확장을 막도록 최소·최대 크기를 동일하게 고정한다.
        // 각 버튼은 공통 스타일을 쓰고, 생성 함수만 전달하여 선택한 게임을 필요할 때 만든다.
        public LauncherForm()
        {
            Text = "Taskbar_Games";
            Icon = ArcadeTheme.AppIcon;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            MinimumSize = MaximumSize = GameEnvironment.SelectionSize;
            ClientSize = GameEnvironment.SelectionSize;
            BackColor = ArcadeTheme.Black;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            KeyPreview = true;
            GameCard fortress = new GameCard(false);
            fortress.Bounds = new Rectangle(3, 3, 24, 24);
            fortress.Click += delegate { SelectGame(GameKind.Fortress); };
            GameCard baseball = new GameCard(true);
            baseball.Bounds = new Rectangle(31, 3, 24, 24);
            baseball.Click += delegate { SelectGame(GameKind.Baseball); };
            Controls.Add(fortress);
            Controls.Add(baseball);
            GameCard pet = new GameCard(false, true);
            pet.Bounds = new Rectangle(3, 31, 24, 24);
            pet.Click += delegate { OpenGame(delegate { return new PetGame(); }); };
            Controls.Add(pet);
            GameCard runner = new GameCard(false, false, false, true);
            runner.Bounds = new Rectangle(31, 31, 24, 24);
            runner.Click += delegate { SelectGame(GameKind.Runner); };
            Controls.Add(runner);
            hints.SetToolTip(runner, "공룡 러너");
            Button exit = new PixelMenuButton("EXIT");
            exit.AccessibleName = "종료"; exit.AccessibleDescription = "프로그램 종료 (Esc)";
            exit.Name = "ExitButton";
            exit.Bounds = new Rectangle(3, 59, 52, 12);
            exit.Click += delegate { Close(); };
            Controls.Add(exit);
            hints.SetToolTip(exit, "종료 (Esc)");
            hints.SetToolTip(pet, "동물 친구");
            hints.SetToolTip(fortress, "포트리스");
            hints.SetToolTip(baseball, "야구");
            ArcadeTheme.Style(menu);
            menu.Items.Add("모니터 선택", null, delegate {
                Hide();
                using (MonitorSelectionForm picker = new MonitorSelectionForm()) picker.ShowDialog();
                Show(); Activate();
            });
            menu.Items.Add("기록실 / 칭호", null, delegate { using (RecordsForm records = new RecordsForm(GameKind.Fortress)) records.ShowDialog(this); });
            menu.Items.Add("종료 (Esc)", null, delegate { Close(); });
            ContextMenuStrip = fortress.ContextMenuStrip = baseball.ContextMenuStrip = pet.ContextMenuStrip = runner.ContextMenuStrip = menu;
            PositionAboveTaskbar();
            displayTimer.Interval = 500;
            displayTimer.Tick += delegate { if (Visible) PositionAboveTaskbar(); };
            displayTimer.Start();
        }

        // 자식 버튼이 포커스를 가져도 Esc를 먼저 처리한다.
        // 단순 KeyDown보다 앞단에서 처리하므로 버튼의 대화상자 키 처리에 가로막히지 않는다.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Handle Escape before the focused game button processes dialog keys.
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void PositionAboveTaskbar()
        {
            Location = GameEnvironment.SelectionLocation(Size);
        }

        // 게임 종료 후 런처가 다시 보일 때 현재 작업 영역 기준으로 위치를 보정한다.
        protected override void OnVisibleChanged(EventArgs e)
        {
            if (Visible) PositionAboveTaskbar();
            base.OnVisibleChanged(e);
        }

        // 중복 호출과 종료 중 호출을 무시한다. 게임을 만든 뒤 닫힘 이벤트를 구독하고 런처를 숨긴다.
        // 표시 도중 예외가 나면 이벤트·참조·게임 자원을 정리하고 런처를 복원한 뒤 예외를 다시 전달한다.
        bool selectingMode;
        void SelectGame(GameKind kind)
        {
            if (selectingMode || activeGame != null || shuttingDown) return;
            selectingMode = true; Hide();
            try
            {
                using (ModeSelectionForm picker = new ModeSelectionForm(kind))
                    if (picker.ShowDialog() == DialogResult.OK && !IsDisposed && !shuttingDown)
                    {
                        GameMode mode = picker.SelectedMode;
                        OpenGame(delegate {
                            if (kind == GameKind.Fortress) return new FortressGame(mode);
                            if (kind == GameKind.Baseball) return new BaseballGame(mode);
                            return new RunnerGame(mode);
                        });
                    }
            }
            finally { selectingMode = false; if (!IsDisposed && !shuttingDown && activeGame == null) { Show(); Activate(); } }
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

        // 현재 소유한 게임에서 온 이벤트만 처리한다. 다른 창이나 이전 창의 이벤트는 무시한다.
        // 참조와 이벤트를 해제한 뒤, 프로그램 종료 중이 아니면 선택창을 다시 활성화한다.
        void GameClosed(object sender, FormClosedEventArgs e)
        {
            if (!Object.ReferenceEquals(sender, activeGame)) return;
            activeGame.FormClosed -= GameClosed;
            activeGame = null;
            if (shuttingDown || IsDisposed) return;
            Show();
            Activate();
        }

        // 기본 처리에서 종료가 취소되면 아무것도 닫지 않는다.
        // 종료가 확정되면 플래그를 먼저 설정하고 게임을 닫아 재표시 경쟁을 막는다.
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;
            shuttingDown = true;
            if (activeGame != null) activeGame.Close();
        }

        // 클라이언트 경계 안쪽에 1픽셀 테두리만 그린다. 버튼 내부는 각 GameCard가 담당한다.
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen border = new Pen(ArcadeTheme.Neon))
                e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        // 타이머를 가진 게임과 툴팁·메뉴 등 관리형 UI 자원을 명시적으로 반환한다.
        // Dispose는 닫힘 이벤트와 별도로 호출될 수 있으므로 여기서도 종료 플래그를 설정한다.
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                shuttingDown = true;
                if (activeGame != null) { activeGame.Dispose(); activeGame = null; }
                displayTimer.Dispose();
                hints.Dispose();
                menu.Dispose();
            }
            base.Dispose(disposing);
        }

        // 표준 Button의 클릭·키보드·접근성 동작을 유지하면서 외형만 픽셀 그림으로 바꾼다.
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

            // 생성 시 전달한 종류에 맞춰 접근성 이름과 설명을 지정한다.
            // 마우스 진입·이탈 시에만 다시 그리도록 요청하며, 게임 실행 자체는 런처의 Click 구독자가 처리한다.
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

            // 호버/포커스 배경과 테두리를 그린 뒤 버튼 종류에 맞는 그림을 출력한다.
            // 다람쥐 축소 변환은 Save/Restore로 격리하여 다른 버튼 그리기에 영향을 주지 않게 한다.
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
