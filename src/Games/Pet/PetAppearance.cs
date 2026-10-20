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
            if (animal == PetAnimal.Squirrel) PetGame.DrawSquirrel(g, left, top, right,
                pose == "groomtail" ? "sleep" : pose, time, scale);
            System.Drawing.Drawing2D.GraphicsState state = g.Save();
            g.TranslateTransform(left + (right ? 0 : 32 * scale), top);
            g.ScaleTransform(right ? scale : -scale, scale);
            int frame = (int)(time * 4) % 2;
            // 종마다 별도 실루엣을 사용한다. 모든 몸체는 34×28 안에 들어가며
            // 먹이·취미 소품은 기존 공통 좌표를 사용해 동작 연결을 유지한다.
            if (animal != PetAnimal.Squirrel)
            {
                if (pose == "walk") g.TranslateTransform(0, frame);
                bool sleeping = pose == "sleep", closed = pose == "love" || time % 4.8 > 4.6;
                if (animal == PetAnimal.Dog) DrawDog(g, sleeping, closed, frame, pose);
                else if (animal == PetAnimal.Cat) DrawCat(g, sleeping, closed, frame, pose);
                else DrawPanda(g, sleeping, closed, frame, pose);
                if (pose == "eat")
                {
                    PetFood.Draw(g, animal, 20, 17 + frame);
                    Box(g, Cream, 18, 23 + frame, 3, 2);
                    Box(g, Cream, 30, 22 + frame, 3, 2);
                }
            }
            DrawSignature(g, pose, time);
            DrawActivity(g, pose, time);
            g.Restore(state);
        }
        // 강아지: 낮고 긴 몸, 축 처진 귀와 튀어나온 주둥이, 짧은 꼬리.
        static void DrawDog(Graphics g, bool sleep, bool closed, int frame, string pose)
        {
            Color fur = Color.FromArgb(222, 169, 102), ear = Color.FromArgb(125, 75, 46);
            Box(g, ear, 2, 14 - frame, 4, 7); Box(g, fur, 4, 16, 5, 5);
            Round(g, Ink, 5, 16, 24, 11); Round(g, fur, 6, 17, 22, 9);
            Box(g, Cream, 9, 21, 17, 5);
            int step = pose == "walk" ? frame : 0;
            Box(g, ear, 7 + step, 25, 5, 3); Box(g, ear, 24 - step, 25, 5, 3);
            if (sleep)
            {
                Round(g, ear, 16, 17, 10, 10); Round(g, fur, 23, 19, 10, 8);
                Box(g, Ink, 27, 22, 3, 1); Box(g, Ink, 31, 24, 3, 2); return;
            }
            Round(g, Ink, 15, 6, 17, 17); Round(g, fur, 16, 7, 15, 15);
            Round(g, ear, 12, 7, 7, 15); Round(g, ear, 28, 7, 6, 11);
            Box(g, Cream, 21, 7, 3, 9);
            Eye(g, 19, 12, closed, Ink); Eye(g, 27, 12, closed, Ink);
            Round(g, Cream, 21, 17, 13, 7); Box(g, Ink, 29, 17, 4, 3);
            if (pose == "love" || pose == "idle") Box(g, Pink, 27, 22, 3, 3);
            Box(g, Color.FromArgb(80, 151, 170), 17, 23, 12, 2);
        }
        // 고양이: 가느다란 몸, 높은 삼각 귀, 길게 말린 꼬리와 흰 양말.
        static void DrawCat(Graphics g, bool sleep, bool closed, int frame, string pose)
        {
            Color fur = Color.FromArgb(166, 178, 201), stripe = Color.FromArgb(77, 88, 120);
            // 식빵 자세는 발을 감춘 낮고 넓은 몸과 감긴 눈으로 구분한다.
            if (pose == "loaf")
            {
                Round(g, Ink, 5, 17, 28, 11); Round(g, fur, 6, 18, 26, 9);
                Box(g, stripe, 7, 24, 14, 3);
                Box(g, stripe, 17, 10, 4, 9); Box(g, stripe, 28, 10, 4, 9);
                Round(g, fur, 16, 16, 17, 9);
                Box(g, Ink, 19, 20, 3, 1); Box(g, Ink, 28, 20, 3, 1);
                Box(g, Pink, 25, 22, 2, 1); return;
            }
            Box(g, stripe, 2, 7 - frame, 3, 17); Box(g, stripe, 4, 5 - frame, 5, 3);
            Box(g, fur, 3, 10, 1, 11); Box(g, stripe, 4, 22, 9, 4);
            Round(g, Ink, 10, 17, 19, 10); Round(g, fur, 11, 18, 17, 8);
            Box(g, stripe, 12, 18, 3, 5); Box(g, stripe, 17, 18, 2, 3);
            int step = pose == "walk" ? frame : 0;
            Box(g, Cream, 12 + step, 25, 4, 3); Box(g, Cream, 25 - step, 25, 4, 3);
            if (sleep)
            {
                Round(g, fur, 18, 18, 15, 9); Box(g, stripe, 19, 15, 4, 6);
                Box(g, Ink, 26, 22, 4, 1); Box(g, Pink, 31, 24, 2, 1);
                Box(g, stripe, 7, 24, 16, 3); return;
            }
            Box(g, Ink, 13, 1, 2, 10); Box(g, Ink, 15, 3, 3, 8); Box(g, Ink, 18, 6, 3, 5);
            Box(g, Ink, 29, 1, 2, 10); Box(g, Ink, 26, 3, 3, 8); Box(g, Ink, 23, 6, 3, 5);
            Box(g, Pink, 15, 5, 2, 5); Box(g, Pink, 27, 5, 2, 5);
            Round(g, Ink, 12, 9, 21, 13); Round(g, fur, 13, 10, 19, 11);
            Box(g, stripe, 20, 10, 2, 4); Box(g, stripe, 24, 10, 2, 3);
            Eye(g, 16, 14, closed, Color.FromArgb(53, 93, 60)); Eye(g, 27, 14, closed, Color.FromArgb(53, 93, 60));
            Box(g, Cream, 20, 18, 8, 3); Box(g, Pink, 23, 17, 2, 2);
            Box(g, Ink, 10, 18, 6, 1); Box(g, Ink, 29, 18, 5, 1);
            Box(g, Ink, 11, 20, 4, 1); Box(g, Ink, 30, 20, 4, 1);
        }
        // 판다: 넓고 둥근 정면 몸통, 둥근 귀, 검은 눈 무늬와 큰 발.
        static void DrawPanda(Graphics g, bool sleep, bool closed, int frame, string pose)
        {
            Color white = Color.FromArgb(244, 244, 230);
            Round(g, Ink, 7, 14, 25, 14); Round(g, white, 11, 16, 17, 11);
            Round(g, Ink, 6, 17, 6, 9); Round(g, Ink, 28, 17, 6, 9);
            int step = pose == "walk" ? frame : 0;
            Round(g, Ink, 8 + step, 23, 9, 5); Round(g, Ink, 24 - step, 23, 9, 5);
            if (sleep)
            {
                Round(g, Ink, 10, 14, 7, 7); Round(g, white, 13, 17, 19, 9);
                Round(g, Ink, 22, 19, 7, 5); Box(g, white, 24, 21, 3, 1);
                Box(g, Ink, 30, 24, 3, 2); return;
            }
            Round(g, Ink, 6, 2, 9, 9); Round(g, Ink, 25, 2, 9, 9);
            Round(g, Ink, 7, 6, 27, 17); Round(g, white, 8, 7, 25, 15);
            Round(g, Ink, 11, 10, 8, 8); Round(g, Ink, 24, 10, 8, 8);
            Eye(g, 14, 12, closed, white); Eye(g, 26, 12, closed, white);
            Box(g, Ink, 20, 17, 4, 2); Box(g, Ink, 21, 19, 2, 2);
            Box(g, Pink, 10, 19, 4, 1); Box(g, Pink, 28, 19, 4, 1);
        }
        static void Eye(Graphics g, int x, int y, bool closed, Color color)
        {
            Box(g, color, x, closed ? y + 2 : y, 2, closed ? 1 : 4);
        }
        // 작은 소품과 앞발의 두 프레임만 움직인다. 전용 동작도 창 크기를 늘리지 않는다.
        // 모든 좌표는 기존 76×56 창의 로컬 여백 안이며 좌우 반전도 상위 변환을 따른다.
        static void DrawSignature(Graphics g, string pose, double time)
        {
            int frame = (int)(time * 2) % 2;
            Color brown = Color.FromArgb(150, 98, 62), green = Color.FromArgb(111, 159, 74);
            if (pose == "dig")
            {
                Box(g, brown, 27, 26, 15, 2); Box(g, Ink, 31, 26, 7, 1);
                Box(g, Cream, 27 + frame * 3, 22 + frame * 2, 5, 3);
                Box(g, brown, 36 + frame * 3, 22 - frame * 2, 2, 2);
                Box(g, brown, 42 - frame * 2, 24 - frame, 2, 1);
            }
            else if (pose == "groomtail")
            {
                Box(g, Cream, 12 + frame * 3, 19 - frame, 5, 3);
                Box(g, brown, 13 + frame * 3, 22 - frame, 3, 1);
            }
            else if (pose == "wag")
            {
                // 두 방향으로 흔드는 꼬리와 작은 하트로 반가운 자세를 표현한다.
                Box(g, brown, frame == 0 ? 0 : 4, 10, 3, 10);
                Box(g, Cream, frame == 0 ? 0 : 4, 8, 3, 3);
                Box(g, Pink, 36, 7 + frame, 2, 4); Box(g, Pink, 40, 7 + frame, 2, 4);
                Box(g, Pink, 37, 10 + frame, 4, 3); Box(g, Pink, 38, 13 + frame, 2, 1);
            }
            else if (pose == "playball")
            {
                int x = 36 + frame * 5;
                Round(g, Ink, x, 20, 8, 8); Round(g, Color.FromArgb(115, 173, 191), x + 1, 21, 6, 6);
                Box(g, Cream, x + 2, 22, 2, 2);
                Box(g, Cream, 29 + frame * 3, 24, 5, 3);
            }
            else if (pose == "wash")
            {
                Box(g, Ink, 16, 16, 3, 1); // 얼굴을 문지르는 앞발과 감긴 눈.
                Box(g, Cream, 27, 13 + frame * 5, 5, 5);
                Box(g, Pink, 28, 14 + frame * 5, 2, 2);
            }
            else if (pose == "bamboo")
            {
                Box(g, green, 29, 11, 3, 17);
                for (int y = 14; y < 27; y += 5) Box(g, Cream, 29, y, 3, 1);
                Box(g, green, 32, 12, 6, 2); Box(g, green, 34, 10, 4, 2);
                Box(g, green, 24, 16, 5, 2); Box(g, Ink, 27, 20 + frame * 2, 6, 4);
                Box(g, green, 23, 18 + frame, 5, 1);
            }
            else if (pose == "bellypat")
            {
                Round(g, Cream, 14, 20, 13, 7);
                Box(g, Ink, 12 + frame * 3, 21, 5, 4);
                Box(g, Ink, 25 - frame * 3, 23 - frame, 5, 4);
            }
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
