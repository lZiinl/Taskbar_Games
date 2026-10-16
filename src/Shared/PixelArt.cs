// 외부 폰트·이미지 파일 없이 픽셀 글자와 문자 배열 스프라이트를 그리는 공통 도구.
// Graphics의 현재 변환을 그대로 따른다. 작은 글자는 호출 측에서 정수 화면 좌표로 그려야 획이 보존된다.

using System;
using System.Collections.Generic;
using System.Drawing;

namespace TaskbarGames.Shared
{
    internal static class PixelArt
    {
        // 5×7 글꼴 데이터. 글자마다 14자리 16진수 문자열을 저장한다.
        // 두 자리씩 잘라 한 행의 비트 마스크로 읽고, 하위 5비트를 왼쪽부터 출력한다.
        static readonly Dictionary<char, string> Glyphs = new Dictionary<char, string> {
            {'0', "0E11131519110E"}, {'1', "040C040404040E"}, {'2', "0E11010204081F"},
            {'3', "1E01010E01011E"}, {'4', "02060A121F0202"}, {'5', "1F10101E01011E"},
            {'6', "0E10101E11110E"}, {'7', "1F010204080808"}, {'8', "0E11110E11110E"},
            {'9', "0E11110F01010E"}, {'A', "0E11111F111111"}, {'B', "1E11111E11111E"},
            {'C', "0E11101010110E"}, {'D', "1E11111111111E"}, {'E', "1F10101E10101F"},
            {'F', "1F10101E101010"}, {'G', "0E11101711110E"}, {'H', "1111111F111111"},
            {'I', "0E04040404040E"}, {'J', "0702020212120C"}, {'K', "11121418141211"},
            {'L', "1010101010101F"}, {'M', "111B1515111111"}, {'N', "11191915131311"},
            {'O', "0E11111111110E"}, {'P', "1E11111E101010"}, {'Q', "0E11111115120D"},
            {'R', "1E11111E141211"}, {'S', "0F10100E01011E"}, {'T', "1F040404040404"},
            {'U', "1111111111110E"}, {'V', "11111111110A04"}, {'W', "11111115151B11"},
            {'X', "11110A040A1111"}, {'Y', "11110A04040404"}, {'Z', "1F01020408101F"},
            {'+', "0004041F040400"}, {'-', "0000001F000000"}, {'/', "01010204081010"},
            {':', "00040400040400"}, {'.', "00000000000C0C"}, {'|', "04040404040404"}
        };

        // 글자 너비 5픽셀 + 글자 사이 1픽셀. 마지막 글자 뒤 여백은 제외한다.
        // 빈 문자열은 음수가 되지 않도록 0을 반환한다.
        public static int Width(string text) { return Math.Max(0, text.Length * 6 - 1); }

        // 지정 위치부터 글자별 비트 마스크를 채운다. 미등록 문자는 공백처럼 자리만 차지한다.
        // Brush는 한 문자열 안에서 재사용하고 using으로 GDI 리소스를 반환한다.
        public static void Text(Graphics g, string text, Color color, int x, int y)
        {
            using (SolidBrush brush = new SolidBrush(color))
                foreach (char ch in text)
                {
                    string glyph;
                    if (Glyphs.TryGetValue(ch, out glyph))
                        for (int row = 0; row < 7; row++)
                        {
                            int bits = Convert.ToInt32(glyph.Substring(row * 2, 2), 16);
                            for (int col = 0; col < 5; col++)
                                if ((bits & (1 << (4 - col))) != 0) g.FillRectangle(brush, x + col, y + row, 1, 1);
                        }
                    x += 6;
                }
        }

        // 문자 하나가 논리 픽셀 하나다. pixel은 화면에 그릴 정사각형 변 길이이다.
        // D는 윤곽, B는 전달한 기본색, L/C는 기본색의 명암이며 나머지는 고정 색상이다.
        // 알 수 없는 문자와 점은 그리지 않으므로 배경이 그대로 보인다.
        public static void Sprite(Graphics g, string[] rows, int x, int y, Color uniform, int pixel = 2)
        {
            for (int row = 0; row < rows.Length; row++)
                for (int col = 0; col < rows[row].Length; col++)
                {
                    Color color;
                    switch (rows[row][col])
                    {
                        case 'D': color = Color.FromArgb(25, 32, 45); break;
                        case 'B': color = uniform; break;
                        case 'L': color = Color.FromArgb((uniform.R + 255) / 2, (uniform.G + 255) / 2, (uniform.B + 255) / 2); break;
                        case 'C': color = Color.FromArgb(uniform.R * 2 / 3, uniform.G * 2 / 3, uniform.B * 2 / 3); break;
                        case 'R': color = Color.FromArgb(226, 93, 105); break;
                        case 'T': color = Color.FromArgb(193, 134, 94); break;
                        case 'S': color = Color.FromArgb(246, 194, 144); break;
                        case 'W': color = Color.FromArgb(233, 239, 247); break;
                        default: continue;
                    }
                    using (SolidBrush brush = new SolidBrush(color)) g.FillRectangle(brush, x + col * pixel, y + row * pixel, pixel, pixel);
                }
        }
    }
}
