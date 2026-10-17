using System;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace TaskbarGames.Shared
{
    // 버전이 있는 단일 프로필. 게임 창을 닫아도 기록과 설정을 유지한다.
    public sealed class GameData
    {
        public int Version = 1;
        public string MonitorDevice = "";
        public int RunnerBest;
        public int BaseballBestDistance;
        public long BaseballScore;
        public int BaseballAttempts, BaseballHits;
        public int FortressHits, FortressShots, FortressBestCombo;
        public int TankColor = 0x5DB86A;
        public double PetFullness = 75, PetMood = 75;
        public int PetMeals;

        internal void Normalize()
        {
            RunnerBest = Math.Max(0, RunnerBest);
            BaseballBestDistance = Math.Max(0, Math.Min(160, BaseballBestDistance));
            BaseballScore = Math.Max(0, BaseballScore);
            BaseballAttempts = Math.Max(0, BaseballAttempts);
            BaseballHits = Math.Max(0, Math.Min(BaseballAttempts, BaseballHits));
            FortressShots = Math.Max(0, FortressShots);
            FortressHits = Math.Max(0, Math.Min(FortressShots, FortressHits));
            FortressBestCombo = Math.Max(0, Math.Min(FortressHits, FortressBestCombo));
            if (TankColor < 0 || TankColor > 0xFFFFFF) TankColor = 0x5DB86A;
            PetFullness = Clamp(PetFullness); PetMood = Clamp(PetMood);
            PetMeals = Math.Max(0, PetMeals);
            MonitorDevice = MonitorDevice ?? "";
        }
        static double Clamp(double value) { return Double.IsNaN(value) ? 75 : Math.Max(0, Math.Min(100, value)); }
    }

    public sealed class GameStore
    {
        readonly string path;
        bool readOnly, recovered;
        public GameData Data { get; private set; }
        public string Error { get; private set; }
        public GameStore() { Data = new GameData(); Error = ""; } // 메모리 전용: 테스트도 실제 사용자 파일을 건드리지 않는다.
        public GameStore(string filePath) : this() { path = Path.GetFullPath(filePath); }

        public void Load()
        {
            if (path == null) return;
            readOnly = recovered = false;
            Error = "";
            if (!File.Exists(path) && !File.Exists(path + ".bak")) return;
            try { Data = Read(path); return; }
            catch (NotSupportedException) { readOnly = true; Error = "새 버전의 저장 파일입니다. 덮어쓰지 않습니다."; return; }
            catch (Exception ex) { if (!IsStorageError(ex)) throw; }
            try
            {
                Data = Read(path + ".bak"); recovered = true;
                Error = "백업에서 기록을 복구했습니다.";
            }
            catch (Exception ex)
            {
                if (!IsStorageError(ex) && !(ex is NotSupportedException)) throw;
                readOnly = true; Error = "기록을 읽지 못했습니다. 기존 파일을 보존합니다.";
            }
        }

        static GameData Read(string file)
        {
            XmlReaderSettings settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (XmlReader reader = XmlReader.Create(file, settings))
            {
                GameData result = (GameData)new XmlSerializer(typeof(GameData)).Deserialize(reader);
                if (result == null) throw new InvalidOperationException("Empty profile.");
                if (result.Version != 1) throw new NotSupportedException();
                result.Normalize(); return result;
            }
        }

        public bool Save()
        {
            if (readOnly) return false;
            if (path == null) return true;
            try
            {
                Data.Normalize();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporary = path + ".tmp";
                using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    new XmlSerializer(typeof(GameData)).Serialize(stream, Data);
                    stream.Flush(true);
                }
                // 복구 직후에는 손상된 원본이 정상 백업을 덮지 않게 한다.
                if (File.Exists(path)) File.Replace(temporary, path, recovered ? null : path + ".bak");
                else File.Move(temporary, path);
                recovered = false; Error = ""; return true;
            }
            catch (Exception ex)
            {
                if (!IsStorageError(ex)) throw;
                Error = "기록 저장 실패 · 폴더 접근 권한과 디스크 공간을 확인해 주세요.";
                return false;
            }
        }

        // 최초 프로필에만 이전한다. 원본은 보존하므로 이전 실패나 구버전 실행에도 복구할 수 있다.
        public void ImportLegacy(string petPath, Func<int?> readTankColor, string colorPath)
        {
            if (path == null || File.Exists(path) || File.Exists(path + ".bak") || readOnly) return;
            try
            {
                if (File.Exists(petPath))
                {
                    string[] values = File.ReadAllLines(petPath);
                    double fullness, mood; int meals;
                    if (values.Length == 3 && Double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out fullness)
                        && Double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out mood)
                        && Int32.TryParse(values[2], out meals))
                    { Data.PetFullness = fullness; Data.PetMood = mood; Data.PetMeals = meals; }
                }
                int? color = readTankColor == null ? null : readTankColor();
                int legacy;
                if (!color.HasValue && File.Exists(colorPath) && Int32.TryParse(File.ReadAllText(colorPath).Trim(), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out legacy)) color = legacy;
                if (color.HasValue) Data.TankColor = color.Value;
                Data.Normalize(); Save();
            }
            catch (Exception ex)
            {
                if (!IsStorageError(ex)) throw;
                // 이전이 불완전하면 새 파일을 만들지 않는다. 다음 실행에서 다시 시도할 수 있다.
                readOnly = true; Error = "기존 설정을 가져오지 못했습니다. 원본을 보존합니다.";
            }
        }

        static bool IsStorageError(Exception ex)
        { return ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is XmlException || ex is System.Security.SecurityException; }

        public static GameStore OpenDefault()
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Taskbar_Games");
            GameStore store = new GameStore(Path.Combine(directory, "profile.xml"));
            store.Load();
            store.ImportLegacy(Path.Combine(directory, "squirrel.txt"), delegate {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\TaskbarFortress"))
                {
                    object value = key == null ? null : key.GetValue("TankColor");
                    return value is int && (int)value >= 0 && (int)value <= 0xFFFFFF ? (int?)value : null;
                }
            }, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tank-color.txt"));
            return store;
        }
    }
}
