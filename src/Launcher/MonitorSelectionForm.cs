using System;
using System.Drawing;
using System.Windows.Forms;
using TaskbarGames.Shared;

namespace TaskbarGames.Launcher
{
    // 런처와 같은 58×74 창. 네 대씩 표시하고 다섯 대 이상이면 아래 버튼으로 넘긴다.
    public sealed class MonitorSelectionForm : Form
    {
        readonly ToolTip hints = new ToolTip();
        readonly Timer timer = new Timer();
        DisplayInfo[] displays;
        int page;
        string signature = "";
        public MonitorSelectionForm()
        {
            Text = "Taskbar_Games - 모니터 선택"; Icon = ArcadeTheme.AppIcon;
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            MinimumSize = MaximumSize = ClientSize = GameEnvironment.SelectionSize;
            BackColor = ArcadeTheme.Black; TopMost = true; DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;
            Location = GameEnvironment.SelectionLocation(Size);
            RefreshDisplays();
            timer.Interval = 500; timer.Tick += delegate { RefreshDisplays(); }; timer.Start();
        }
        void RefreshDisplays()
        {
            displays = GameEnvironment.Display.Screens;
            string current = String.Join(";", Array.ConvertAll(displays, d => d.DeviceName + d.WorkingArea.ToString() + d.Primary));
            Location = GameEnvironment.SelectionLocation(Size);
            if (current == signature) return;
            if (signature.Length == 0)
                for (int i = 0; i < displays.Length; i++)
                    if (displays[i].DeviceName == GameEnvironment.Display.Current.DeviceName) { page = i / 4; break; }
            signature = current; page = Math.Min(page, Math.Max(0, (displays.Length - 1) / 4));
            RenderButtons();
        }
        void RenderButtons()
        {
            hints.RemoveAll();
            while (Controls.Count > 0) { Control old = Controls[0]; Controls.Remove(old); old.Dispose(); }
            for (int slot = 0; slot < 4 && page * 4 + slot < displays.Length; slot++)
            {
                int index = page * 4 + slot;
                DisplayInfo display = displays[index];
                // DISPLAY1 등 실제 장치 번호를 사용해 Windows의 모니터 식별 번호와 맞춘다.
                string number = System.Text.RegularExpressions.Regex.Match(display.DeviceName, @"\d+$").Value;
                if (number.Length == 0) number = (index + 1).ToString();
                Button button = new MonitorButton(number, display.DeviceName == GameEnvironment.Display.Current.DeviceName);
                button.Bounds = new Rectangle(3 + slot % 2 * 28, 3 + slot / 2 * 28, 24, 24);
                button.AccessibleName = "모니터 " + number;
                hints.SetToolTip(button, button.AccessibleName + (display.Primary ? " · 주 모니터" : "") + "\n" + display.DeviceName
                    + "\n작업 영역 " + display.WorkingArea.Width + " × " + display.WorkingArea.Height + "\n클릭해서 게임 선택");
                button.Click += delegate {
                    // 클릭 직전 분리된 장치는 선택하지 않고 목록을 새로 표시한다.
                    foreach (DisplayInfo available in GameEnvironment.Display.Screens)
                        if (available.DeviceName == display.DeviceName)
                        {
                            GameEnvironment.Display.Select(display.DeviceName);
                            GameEnvironment.Store.Data.MonitorDevice = display.DeviceName;
                            GameEnvironment.Store.Save(); DialogResult = DialogResult.OK; Close(); return;
                        }
                    RefreshDisplays();
                };
                Controls.Add(button);
            }
            Button footer = new MonitorButton(displays.Length > 4 ? "NEXT" : "EXIT", false, false);
            footer.Bounds = new Rectangle(3, 59, 52, 12);
            footer.AccessibleName = displays.Length > 4 ? "다음 모니터 페이지" : "종료";
            hints.SetToolTip(footer, displays.Length > 4 ? "모니터 " + (page + 1) + "/" + ((displays.Length + 3) / 4) + " 페이지 · 다음 보기 · Esc 종료" : "종료 (Esc)");
            footer.Click += delegate {
                if (displays.Length <= 4) { DialogResult = DialogResult.Cancel; Close(); }
                else { page = (page + 1) % ((displays.Length + 3) / 4); RenderButtons(); }
            };
            Controls.Add(footer);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void OnPaint(PaintEventArgs e)
        { base.OnPaint(e); using (Pen p = new Pen(ArcadeTheme.Neon)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); }
        protected override void Dispose(bool disposing)
        { if (disposing) { timer.Dispose(); hints.Dispose(); } base.Dispose(disposing); }

        sealed class MonitorButton : Button
        {
            readonly string label;
            readonly bool selected, monitor;
            public MonitorButton(string text, bool active, bool isMonitor = true)
            {
                label = text; selected = active; monitor = isMonitor;
                FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
                MouseEnter += delegate { Invalidate(); }; MouseLeave += delegate { Invalidate(); };
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics; g.Clear(ArcadeTheme.Panel);
                Color color = selected || Focused || ClientRectangle.Contains(PointToClient(Cursor.Position)) ? ArcadeTheme.Neon : ArcadeTheme.Cyan;
                using (Pen p = new Pen(color))
                {
                    if (monitor) { g.DrawRectangle(p, 2, 2, 19, 16); g.DrawLine(p, 8, 21, 16, 21); g.DrawLine(p, 12, 18, 12, 21); }
                    if (Focused) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                }
                PixelArt.Text(g, label, color, Math.Max(1, (Width - PixelArt.Width(label)) / 2), monitor ? 7 : 3);
            }
        }
    }
}
