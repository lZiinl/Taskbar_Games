using System.Drawing;

namespace TaskbarGames.Games.Pet
{
    // 같은 먹이 그림을 메뉴, 배치 미리보기, 바닥, 먹는 모션에서 공유한다.
    public static class PetFood
    {
        public static string Name(PetAnimal animal)
        { return animal == PetAnimal.Dog ? "뼈 간식" : animal == PetAnimal.Cat ? "생선" : animal == PetAnimal.Panda ? "대나무" : "도토리"; }
        public static string Label(PetAnimal animal)
        { return animal == PetAnimal.Dog ? "BONE" : animal == PetAnimal.Cat ? "FISH" : animal == PetAnimal.Panda ? "BAMBOO" : "ACORN"; }
        static void Box(Graphics g, Color color, int x, int y, int w, int h)
        { using (Brush brush = new SolidBrush(color)) g.FillRectangle(brush, x, y, w, h); }
        // 12×10 픽셀 안에 맞춰 모든 동물이 기존 배치 창을 그대로 사용한다.
        public static void Draw(Graphics g, PetAnimal animal, int x, int y)
        {
            if (animal == PetAnimal.Dog)
            {
                Color edge = Color.FromArgb(152, 116, 81), snack = Color.FromArgb(255, 230, 180);
                Box(g, edge, x, y + 1, 4, 8); Box(g, edge, x + 8, y + 1, 4, 8); Box(g, edge, x + 3, y + 3, 6, 4);
                Box(g, snack, x + 1, y + 2, 2, 6); Box(g, snack, x + 9, y + 2, 2, 6); Box(g, snack, x + 3, y + 4, 6, 2);
            }
            else if (animal == PetAnimal.Cat)
            {
                Color edge = Color.FromArgb(45, 102, 145), fish = Color.FromArgb(125, 211, 227);
                Box(g, edge, x + 3, y + 2, 7, 6); Box(g, edge, x + 5, y + 1, 3, 8);
                Box(g, edge, x, y + 1, 2, 8); Box(g, edge, x + 2, y + 3, 2, 4); Box(g, edge, x + 10, y + 3, 2, 4);
                Box(g, fish, x + 4, y + 3, 6, 4); Box(g, Color.WhiteSmoke, x + 6, y + 3, 3, 1);
                Box(g, Color.FromArgb(25, 32, 45), x + 9, y + 4, 1, 1);
            }
            else if (animal == PetAnimal.Panda)
            {
                Color edge = Color.FromArgb(48, 110, 55), bamboo = Color.FromArgb(125, 196, 78);
                Box(g, edge, x + 4, y, 4, 10); Box(g, bamboo, x + 5, y, 2, 10);
                Box(g, Color.FromArgb(212, 225, 126), x + 4, y + 3, 4, 1); Box(g, Color.FromArgb(212, 225, 126), x + 4, y + 7, 4, 1);
                Box(g, edge, x + 1, y + 2, 3, 2); Box(g, bamboo, x, y + 1, 3, 2);
                Box(g, edge, x + 8, y + 5, 3, 2); Box(g, bamboo, x + 9, y + 4, 3, 2);
            }
            else
            {
                Color cap = Color.FromArgb(119, 72, 46), nut = Color.FromArgb(245, 184, 115);
                Box(g, cap, x + 4, y, 2, 2); Box(g, cap, x + 1, y + 2, 9, 3);
                Box(g, nut, x + 2, y + 5, 7, 4); Box(g, nut, x + 3, y + 9, 5, 1); Box(g, Color.Bisque, x + 3, y + 5, 2, 2);
            }
        }
        public static Bitmap Icon(PetAnimal animal)
        {
            Bitmap image = new Bitmap(18, 18);
            using (Graphics g = Graphics.FromImage(image)) Draw(g, animal, 3, 4);
            return image;
        }
    }
}
