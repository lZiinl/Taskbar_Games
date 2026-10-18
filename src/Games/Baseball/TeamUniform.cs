using System;
using System.Drawing;
using TaskbarGames.Shared;

namespace TaskbarGames.Games.Baseball
{
    // 구단의 대표 배색과 줄무늬를 작은 도트 유니폼으로 표현한다.
    public sealed class TeamUniform
    {
        public readonly string Id, Name, Mark;
        public readonly Color Cap, Jersey, Accent;
        public readonly bool Pinstripes;
        TeamUniform(string id, string name, string mark, int cap, int jersey, int accent, bool stripes = false)
        { Id = id; Name = name; Mark = mark; Cap = Rgb(cap); Jersey = Rgb(jersey); Accent = Rgb(accent); Pinstripes = stripes; }
        static Color Rgb(int rgb) { return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }
        public static readonly TeamUniform DefaultBatter = new TeamUniform("", "기본", "", 0x5C9FEB, 0x5C9FEB, 0xB4DAFA);
        public static readonly TeamUniform DefaultPitcher = new TeamUniform("", "기본", "", 0xE3656E, 0xE3656E, 0xFFB8BA);
        public static readonly TeamUniform[] All = {
            new TeamUniform("lg", "LG 트윈스", "L", 0x20212B, 0xF1F1EE, 0xC41249, true),
            new TeamUniform("hanwha", "한화 이글스", "E", 0x24272B, 0xF47725, 0xF6EEE2),
            new TeamUniform("ssg", "SSG 랜더스", "S", 0xC82032, 0xC82032, 0xF5D5A0),
            new TeamUniform("samsung", "삼성 라이온즈", "S", 0x1462B7, 0x1462B7, 0xF3F4F7),
            new TeamUniform("nc", "NC 다이노스", "N", 0x193456, 0x315A89, 0xC6AB7A),
            new TeamUniform("kt", "KT 위즈", "W", 0x25262C, 0x32333A, 0xED2939),
            new TeamUniform("lotte", "롯데 자이언츠", "G", 0x17314C, 0x89C9E4, 0xD9293D),
            new TeamUniform("kia", "KIA 타이거즈", "T", 0xD9293D, 0xD9293D, 0xF7F2E9),
            new TeamUniform("doosan", "두산 베어스", "B", 0x202844, 0xEFF0ED, 0xD42642),
            new TeamUniform("kiwoom", "키움 히어로즈", "H", 0x741B3B, 0x741B3B, 0xF3E4D4)
        };
        public static TeamUniform Find(string id, bool batter)
        {
            foreach (TeamUniform team in All) if (team.Id == id) return team;
            return batter ? DefaultBatter : DefaultPitcher;
        }
        public void DrawSprite(Graphics g, string[] rows, int x, int y, bool headOnly = false)
        {
            for (int row = 0; row < rows.Length; row++)
                for (int col = 0; col < rows[row].Length; col++)
                {
                    Color basis = row < 4 ? Cap : Jersey, color;
                    switch (rows[row][col])
                    {
                        case 'D': color = Color.FromArgb(25, 32, 45); break;
                        case 'B': color = !headOnly && row >= 8 && row <= 13 && Pinstripes && col % 3 == 0 ? Cap : basis; break;
                        case 'L': color = row < 4 ? Accent : ControlLight(basis); break;
                        case 'C': color = Color.FromArgb(basis.R * 2 / 3, basis.G * 2 / 3, basis.B * 2 / 3); break;
                        case 'T': color = Color.FromArgb(193, 134, 94); break;
                        case 'S': color = Color.FromArgb(246, 194, 144); break;
                        case 'W': color = Color.FromArgb(233, 239, 247); break;
                        default: continue;
                    }
                    using (Brush brush = new SolidBrush(color)) g.FillRectangle(brush, x + col * 2, y + row * 2, 2, 2);
                }
            if (Mark.Length > 0)
            {
                PixelArt.Text(g, Mark, Accent, x + (headOnly ? 7 : 16), y + 2);
                if (!headOnly) PixelArt.Text(g, Mark, Accent, x + 16, y + 20);
            }
        }
        static Color ControlLight(Color color) { return Color.FromArgb((color.R + 255) / 2, (color.G + 255) / 2, (color.B + 255) / 2); }
        public Bitmap Icon()
        {
            Bitmap image = new Bitmap(28, 24);
            using (System.IO.Stream stream = typeof(TeamUniform).Assembly.GetManifestResourceStream("Teams." + Id + ".png"))
            using (Image logo = Image.FromStream(stream))
            using (Graphics g = Graphics.FromImage(image))
            {
                float ratio = Math.Min(28f / logo.Width, 24f / logo.Height);
                int width = Math.Max(1, (int)Math.Round(logo.Width * ratio)), height = Math.Max(1, (int)Math.Round(logo.Height * ratio));
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(logo, new Rectangle((28 - width) / 2, (24 - height) / 2, width, height));
            }
            return image;
        }
    }
}
