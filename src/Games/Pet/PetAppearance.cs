using System;
using System.Drawing;

namespace TaskbarGames.Games.Pet
{
    public enum PetAnimal { Squirrel, Dog, Cat, Panda }

    public static class PetAppearance
    {
        public static PetAnimal Parse(string id)
        {
            PetAnimal value;
            return Enum.TryParse(id, true, out value) && Enum.IsDefined(typeof(PetAnimal), value) ? value : PetAnimal.Squirrel;
        }
        public static string Name(PetAnimal animal)
        { return animal == PetAnimal.Dog ? "강아지" : animal == PetAnimal.Cat ? "고양이" : animal == PetAnimal.Panda ? "판다" : "다람쥐"; }
        static void Box(Graphics g, Color color, int x, int y, int w, int h)
        { using (Brush brush = new SolidBrush(color)) g.FillRectangle(brush, x, y, w, h); }
        static void Round(Graphics g, Color color, int x, int y, int w, int h)
        { Box(g, color, x + 2, y, w - 4, h); Box(g, color, x, y + 2, w, h - 4); }
        static readonly Color Ink = Color.FromArgb(48, 39, 43), Cream = Color.FromArgb(255, 239, 209), Pink = Color.FromArgb(244, 153, 166);
        public static void Draw(Graphics g, PetAnimal animal, int left, int top, bool right, string pose, double time, int scale)
        {
            if (animal == PetAnimal.Squirrel) PetGame.DrawSquirrel(g, left, top, right, pose, time, scale);
            System.Drawing.Drawing2D.GraphicsState state = g.Save();
            g.TranslateTransform(left + (right ? 0 : 32 * scale), top);
            g.ScaleTransform(right ? scale : -scale, scale);
            int frame = (int)(time * 4) % 2;
            if (animal != PetAnimal.Squirrel)
            {
                bool panda = animal == PetAnimal.Panda, cat = animal == PetAnimal.Cat, sleeping = pose == "sleep";
                Color fur = panda ? Color.FromArgb(245, 243, 231) : cat ? Color.FromArgb(183, 192, 205) : Color.FromArgb(214, 158, 98);
                Color dark = panda ? Ink : cat ? Color.FromArgb(96, 108, 132) : Color.FromArgb(126, 78, 50);
                if (pose == "walk") g.TranslateTransform(0, frame);
                // 꼬리·귀·눈 무늬로 작은 크기에서도 종을 구분한다.
                if (!panda)
                {
                    Box(g, dark, 3, 14 - frame, 4, 10); Box(g, fur, 5, 20, 10, 4);
                    if (cat) { Box(g, dark, 3, 10 - frame, 7, 4); Box(g, dark, 8, 8 - frame, 3, 5); }
                }
                Round(g, Ink, 10, 16, 19, 12); Round(g, fur, 11, 17, 17, 10);
                Round(g, panda ? Ink : Cream, 16, 20, 10, 7);
                int step = pose == "walk" ? frame * 2 : 0;
                Box(g, dark, 11 - step, 26, 7, 2); Box(g, dark, 23 + step, 26, 7, 2);
                if (sleeping)
                {
                    Round(g, Ink, 18, 16, 14, 11); Round(g, fur, 19, 17, 12, 9);
                    Round(g, dark, 18, 15, 5, 5); Box(g, Ink, 25, 21, 4, 1);
                }
                else
                {
                    if (cat)
                    {
                        Box(g, dark, 12, 2, 3, 8); Box(g, dark, 15, 4, 4, 6);
                        Box(g, dark, 27, 2, 3, 8); Box(g, dark, 24, 4, 4, 6);
                        Box(g, Pink, 14, 5, 2, 4); Box(g, Pink, 27, 5, 2, 4);
                    }
                    else
                    {
                        Round(g, dark, 11, 3, 8, panda ? 8 : 14);
                        Round(g, dark, 26, 3, 7, panda ? 8 : 14);
                    }
                    Round(g, Ink, 11, 7, 22, 17); Round(g, fur, 12, 8, 20, 15);
                    if (panda) { Round(g, dark, 14, 11, 7, 8); Round(g, dark, 25, 11, 7, 8); }
                    if (cat) { Box(g, dark, 20, 8, 2, 4); Box(g, dark, 24, 8, 2, 3); }
                    bool closed = pose == "love" || time % 4.8 > 4.6;
                    Color eye = panda ? Color.WhiteSmoke : Ink;
                    Box(g, eye, 16, closed ? 14 : 12, 3, closed ? 1 : 4);
                    Box(g, eye, 27, closed ? 14 : 12, 3, closed ? 1 : 4);
                    Round(g, Cream, 19, 17, 12, 6); Box(g, Ink, 23, 17, 3, 2); Box(g, Ink, 24, 19, 1, 2);
                    Box(g, Pink, 14, 18, 3, 1);
                    if (cat) { Box(g, Ink, 11, 18, 6, 1); Box(g, Ink, 29, 18, 5, 1); }
                    if (pose == "eat")
                    {
                        PetFood.Draw(g, animal, 20, 17 + frame);
                        Box(g, Cream, 18, 23 + frame, 3, 2); Box(g, Cream, 31, 22 + frame, 2, 2);
                    }
                    else { Box(g, fur, 15, 22, 3, 2); Box(g, fur, 27, 22, 3, 2); }
                }
            }
            DrawActivity(g, pose, time);
            g.Restore(state);
        }
        static void DrawActivity(Graphics g, string pose, double time)
        {
            int frame = (int)(time * 3) % 2;
            Color red = Color.FromArgb(225, 58, 65), white = Color.FromArgb(255, 246, 223), blue = Color.FromArgb(76, 193, 207);
            if (pose == "cola")
            {
                int y = 20 - frame * 3;
                Box(g, Ink, 20, y, 10, 8); Box(g, red, 21, y + 1, 8, 6);
                Box(g, white, 21, y + 3, 8, 2); Box(g, white, 20, y - 1, 10, 2);
                Box(g, white, 26, 17, 1, Math.Max(1, y - 16)); Box(g, white, 24, 16, 3, 1);
                Box(g, Cream, 18, y + 3, 3, 3); Box(g, Cream, 29, y + 2, 3, 3);
            }
            else if (pose == "popcorn")
            {
                Box(g, Ink, 18, 20, 14, 8); Box(g, white, 19, 21, 12, 6);
                for (int x = 19; x < 31; x += 4) Box(g, red, x, 22, 2, 5);
                for (int x = 19; x < 31; x += 3) { Box(g, Cream, x, 18 + x % 2, 3, 3); Box(g, Color.Goldenrod, x + 1, 20, 1, 1); }
                Box(g, Cream, 29, 16 + frame * 5, 3, 3); Box(g, white, 28, 15 + frame * 5, 2, 2);
            }
            else if (pose == "read")
            {
                Box(g, Ink, 13, 18, 22, 10); Box(g, blue, 14, 19, 20, 8);
                Box(g, white, 15, 19, 8, 7); Box(g, white, 25, 19, 8, 7);
                Box(g, Ink, 23, 20, 1, 8);
                for (int y = 21; y < 26; y += 2) { Box(g, blue, 16, y, 5, 1); Box(g, blue, 26, y, 5, 1); }
                if ((int)(time * 2) % 6 == 0) { Box(g, white, 22, 16, 3, 9); Box(g, blue, 22, 16, 1, 9); }
                Box(g, Cream, 12, 23, 3, 3); Box(g, Cream, 33, 23, 2, 3);
            }
            else if (pose == "watchtv")
            {
                Box(g, Ink, 35, 10, 18, 15); Box(g, Color.FromArgb(95, 109, 133), 36, 11, 16, 13);
                Box(g, Color.FromArgb(29, 57, 80), 37, 12, 12, 10);
                Box(g, blue, 38 + frame * 3, 14, 5, 3); Box(g, Color.FromArgb(120, 205, 112), 37, 19, 12, 3);
                Box(g, white, 44 - frame * 4, 17, 2, 2); Box(g, red, 50, 14, 1, 2);
                Box(g, Ink, 38, 25, 3, 3); Box(g, Ink, 48, 25, 3, 3);
                Box(g, Ink, 43, 7, 2, 3); Box(g, Ink, 40, 6, 3, 1); Box(g, Ink, 45, 6, 3, 1);
                Box(g, Ink, 28, 22, 6, 3); Box(g, Cream, 26, 22, 3, 2); Box(g, red, 32, 22, 1, 1);
            }
        }
        public static Bitmap Icon(PetAnimal animal)
        {
            Bitmap image = new Bitmap(36, 30);
            using (Graphics g = Graphics.FromImage(image)) Draw(g, animal, 1, 1, true, "idle", 0, 1);
            return image;
        }
    }
}
