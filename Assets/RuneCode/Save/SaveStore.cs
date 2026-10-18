using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using UnityEngine;

namespace RuneCode
{
    public static class SaveStore
    {
        private const int SLOT_COUNT = 3;
        private const int DEFAULT_SLOT = 1;
        private const string SLOT_FILE_FORMAT = "runecode.slot.{0}.v4.json";
        private const string LEGACY_SAVE_V4_FILE = "runecode.save.v4.json";
        private const string LEGACY_SAVE_V3_FILE = "runecode.save.v3.json";
        private const string LEGACY_SAVE_V2_FILE = "runecode.save.v2.json";
        private const string LEGACY_SAVE_V1_FILE = "runecode.save.v1.json";
        private const string ACTIVE_SLOT_KEY = "runecode.save.active-slot";
        private const string AUTO_SAVE_KEY = "runecode.save.auto-save";
        private const string SCREEN_SHAKE_KEY = "runecode.settings.screen-shake";
        private const string HIT_STOP_KEY = "runecode.settings.hit-stop";
        private const string MASTER_VOLUME_KEY = "runecode.settings.master-volume";
        private const string LEGACY_MIGRATED_KEY = "runecode.save.legacy-migrated";
        private const string ENCRYPTED_SAVE_PREFIX = "RC-AES-CBC-1:";
        private const string SAVE_KEY_BASE64 = "Fn18bSp50G37Q6RkCeWQIoCjDt1zqeoAbviQE4oXn00=";
        private const int AES_IV_BYTES = 16;

        private static string _lastWarning;

        public static string LastWarning => _lastWarning;
        public static int ActiveSlot => Mathf.Clamp(PlayerPrefs.GetInt(ACTIVE_SLOT_KEY, DEFAULT_SLOT), 1, SLOT_COUNT);
        public static bool AutoSaveEnabled => PlayerPrefs.GetInt(AUTO_SAVE_KEY, 1) != 0;
        public static bool ScreenShake => PlayerPrefs.GetInt(SCREEN_SHAKE_KEY, 1) != 0;
        public static bool HitStop => PlayerPrefs.GetInt(HIT_STOP_KEY, 1) != 0;
        public static float MasterVolume => Mathf.Clamp01(PlayerPrefs.GetFloat(MASTER_VOLUME_KEY, 1f));
        public static string SavePath => GetSlotPath(ActiveSlot);

        /// <summary>선택된 슬롯을 1부터 3까지의 범위 안에 저장하고 다른 실행에도 적용한다.</summary>
        public static void SetActiveSlot(int slot)
        {
            if (!IsValidSlot(slot)) return;
            PlayerPrefs.SetInt(ACTIVE_SLOT_KEY, slot);
            PlayerPrefs.Save();
        }

        /// <summary>자동 저장 설정을 사용자 환경설정에 기록한다.</summary>
        public static void SetAutoSaveEnabled(bool isEnabled)
        {
            PlayerPrefs.SetInt(AUTO_SAVE_KEY, isEnabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>확정한 마스터 음량을 0부터 1로 제한해 기기 공통 환경설정에 저장한다.</summary>
        public static void SetMasterVolume(float volume)
        {
            PlayerPrefs.SetFloat(MASTER_VOLUME_KEY, Mathf.Clamp01(volume));
            PlayerPrefs.Save();
        }

        /// <summary>기기 공통 피드백 설정이 없을 때 현재 일반 저장의 기존 설정을 최초 한 번 이전한다.</summary>
        public static void InitializeFeedbackSettings(PlayerSave save)
        {
            bool isChanged = false;
            if (!PlayerPrefs.HasKey(SCREEN_SHAKE_KEY))
            {
                PlayerPrefs.SetInt(SCREEN_SHAKE_KEY, save.ScreenShake ? 1 : 0);
                isChanged = true;
            }
            if (!PlayerPrefs.HasKey(HIT_STOP_KEY))
            {
                PlayerPrefs.SetInt(HIT_STOP_KEY, save.HitStop ? 1 : 0);
                isChanged = true;
            }
            if (isChanged) PlayerPrefs.Save();
        }

        /// <summary>화면 흔들림 또는 히트스톱을 기기 공통 환경설정에 기록하고 세이브 파일은 변경하지 않는다.</summary>
        public static void SetFeedbackSetting(string key, bool isEnabled)
        {
            if (key != "screenShake" && key != "hitStop") return;
            PlayerPrefs.SetInt(key == "screenShake" ? SCREEN_SHAKE_KEY : HIT_STOP_KEY, isEnabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>지정 슬롯이나 이전 단일 저장 파일에 저장 데이터가 있는지 반환한다.</summary>
        public static bool HasSlot(int slot)
        {
            if (!IsValidSlot(slot)) return false;
            if (File.Exists(GetSlotPath(slot))) return true;
            return slot == DEFAULT_SLOT && PlayerPrefs.GetInt(LEGACY_MIGRATED_KEY, 0) == 0 && FindLegacySavePath() != null;
        }

        /// <summary>현재 선택된 슬롯의 데이터를 읽고, 없으면 새 진행을 반환한다.</summary>
        public static PlayerSave Load()
        {
            return TryLoadSlot(ActiveSlot, out PlayerSave save) ? save : PlayerSave.CreateNew();
        }

        /// <summary>지정 슬롯의 진행을 이전·검증해 읽고, 없거나 유효하지 않으면 false를 반환한다.</summary>
        public static bool TryLoadSlot(int slot, out PlayerSave save)
        {
            save = null;
            _lastWarning = null;
            if (!IsValidSlot(slot)) return false;

            string sourcePath = GetSlotPath(slot);
            bool isLegacySource = false;
            if (!File.Exists(sourcePath))
            {
                if (slot != DEFAULT_SLOT || PlayerPrefs.GetInt(LEGACY_MIGRATED_KEY, 0) != 0) return false;
                sourcePath = FindLegacySavePath();
                if (sourcePath == null) return false;
                isLegacySource = true;
            }

            try
            {
                string raw = DecryptSave(File.ReadAllText(sourcePath));
                PlayerSave loaded = Migrate(JsonUtility.FromJson<PlayerSave>(raw));
                if (loaded != null && loaded.Validate(out _))
                {
                    save = loaded;
                    if (isLegacySource && WriteToSlot(slot, loaded)) ArchiveLegacySource(sourcePath);
                    return true;
                }

                _lastWarning = GameData.L("save.recovered");
                File.Copy(sourcePath, sourcePath + ".invalid-backup", true);
            }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is UnauthorizedAccessException || exception is FormatException || exception is CryptographicException)
            {
                _lastWarning = GameData.L("save.unavailable");
                if (exception is FormatException || exception is ArgumentException || exception is CryptographicException)
                {
                    try { File.Copy(sourcePath, sourcePath + ".invalid-backup", true); }
                    catch (Exception copyException) when (copyException is IOException || copyException is UnauthorizedAccessException) { }
                }
            }

            return false;
        }

        /// <summary>빈 슬롯에 새 진행을 만들고 기록하며 성공 여부와 생성한 저장을 반환한다.</summary>
        public static bool CreateSlot(int slot, out PlayerSave save)
        {
            save = null;
            if (!IsValidSlot(slot) || HasSlot(slot)) return false;
            PlayerSave created = PlayerSave.CreateNew();
            if (!WriteToSlot(slot, created)) return false;
            SetActiveSlot(slot);
            save = created;
            return true;
        }

        /// <summary>현재 슬롯의 진행을 기록하고 직전 정상 파일을 백업한다.</summary>
        public static bool Write(PlayerSave save)
        {
            return WriteToSlot(ActiveSlot, save);
        }

        /// <summary>지정 슬롯에 진행을 임시 파일로 기록한 뒤 원자적으로 교체한다.</summary>
        public static bool WriteToSlot(int slot, PlayerSave save)
        {
            if (!IsValidSlot(slot) || save == null) return false;
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                string path = GetSlotPath(slot);
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, EncryptSave(JsonUtility.ToJson(save, true)));
                if (File.Exists(path)) File.Replace(temporary, path, path + ".backup");
                else File.Move(temporary, path);
                _lastWarning = null;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is CryptographicException)
            {
                _lastWarning = GameData.L("save.unavailable");
                return false;
            }
        }

        /// <summary>지정 슬롯 파일과 임시·백업 파일을 정리하고 삭제 결과를 반환한다.</summary>
        public static bool DeleteSlot(int slot)
        {
            if (!IsValidSlot(slot)) return false;
            try
            {
                string path = GetSlotPath(slot);
                DeleteIfExists(path);
                DeleteIfExists(path + ".tmp");
                DeleteIfExists(path + ".backup");
                DeleteIfExists(path + ".invalid-backup");
                if (slot == DEFAULT_SLOT)
                {
                    ArchiveLegacyFiles(".deleted-backup");
                    PlayerPrefs.SetInt(LEGACY_MIGRATED_KEY, 1);
                    PlayerPrefs.Save();
                }
                _lastWarning = null;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                _lastWarning = GameData.L("save.unavailable");
                return false;
            }
        }

        /// <summary>JSON을 AES-256-CBC와 PKCS7 패딩으로 암호화하고 매 저장마다 생성한 IV와 암호문을 버전 표식 뒤에 반환한다.</summary>
        private static string EncryptSave(string json)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = Convert.FromBase64String(SAVE_KEY_BASE64);
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();
                byte[] plain = Encoding.UTF8.GetBytes(json);
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    byte[] encrypted = encryptor.TransformFinalBlock(plain, 0, plain.Length);
                    byte[] payload = new byte[AES_IV_BYTES + encrypted.Length];
                    Buffer.BlockCopy(aes.IV, 0, payload, 0, AES_IV_BYTES);
                    Buffer.BlockCopy(encrypted, 0, payload, AES_IV_BYTES, encrypted.Length);
                    return ENCRYPTED_SAVE_PREFIX + Convert.ToBase64String(payload);
                }
            }
        }

        /// <summary>표식이 있는 저장의 IV와 암호문을 검증해 JSON으로 복호화한다. 기존 평문은 그대로 반환하고 손상된 암호문은 예외로 알린다.</summary>
        private static string DecryptSave(string raw)
        {
            if (!raw.StartsWith(ENCRYPTED_SAVE_PREFIX, StringComparison.Ordinal)) return raw;
            byte[] payload = Convert.FromBase64String(raw.Substring(ENCRYPTED_SAVE_PREFIX.Length));
            int encryptedLength = payload.Length - AES_IV_BYTES;
            if (encryptedLength <= 0 || encryptedLength % AES_IV_BYTES != 0)
                throw new FormatException("암호화 저장 파일의 길이가 올바르지 않습니다.");
            using (Aes aes = Aes.Create())
            {
                aes.Key = Convert.FromBase64String(SAVE_KEY_BASE64);
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                byte[] iv = new byte[AES_IV_BYTES];
                Buffer.BlockCopy(payload, 0, iv, 0, AES_IV_BYTES);
                aes.IV = iv;
                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                {
                    byte[] plain = decryptor.TransformFinalBlock(payload, AES_IV_BYTES, encryptedLength);
                    return new UTF8Encoding(false, true).GetString(plain);
                }
            }
        }

        /// <summary>이전 진행을 순차 이전하고 그래프를 문법 블록으로 변환한다. 알 수 없는 버전은 원본을 보존한다.</summary>
        private static PlayerSave Migrate(PlayerSave save)
        {
            if (save == null) return null;
            if (save.Version == 1) save.MigrateToIncremental();
            if (save.Version == 2) save.MigrateToHardwareTree();
            if (save.Version == 3) save.MigrateToUpgradeTreeV4();
            if (save.Version != 4) throw new FormatException("지원하지 않는 룬 코드 저장 버전입니다.");
            save.MigrateSpellGrammar();
            return save;
        }

        /// <summary>저장 슬롯 번호가 지원 범위에 있는지 반환한다.</summary>
        private static bool IsValidSlot(int slot)
        {
            return slot >= 1 && slot <= SLOT_COUNT;
        }

        /// <summary>지정 슬롯의 저장 파일 경로를 반환한다.</summary>
        private static string GetSlotPath(int slot)
        {
            return Path.Combine(Application.persistentDataPath, string.Format(SLOT_FILE_FORMAT, slot));
        }

        /// <summary>이전 단일 저장 파일 중 우선순위가 가장 높은 경로를 반환한다.</summary>
        private static string FindLegacySavePath()
        {
            string[] legacyPaths = GetLegacyPaths();
            foreach (string path in legacyPaths)
                if (File.Exists(path)) return path;
            return null;
        }

        /// <summary>기존 버전의 단일 저장 파일 경로를 현재 버전 우선순위로 반환한다.</summary>
        private static string[] GetLegacyPaths()
        {
            return new[]
            {
                Path.Combine(Application.persistentDataPath, LEGACY_SAVE_V4_FILE),
                Path.Combine(Application.persistentDataPath, LEGACY_SAVE_V3_FILE),
                Path.Combine(Application.persistentDataPath, LEGACY_SAVE_V2_FILE),
                Path.Combine(Application.persistentDataPath, LEGACY_SAVE_V1_FILE)
            };
        }

        /// <summary>이전 저장을 마이그레이션 백업으로 보존하고 재탐색을 막는다.</summary>
        private static void ArchiveLegacySource(string sourcePath)
        {
            try
            {
                string backupPath = sourcePath + ".migrated-backup";
                File.Copy(sourcePath, backupPath, true);
                File.Delete(sourcePath);
                PlayerPrefs.SetInt(LEGACY_MIGRATED_KEY, 1);
                PlayerPrefs.Save();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                PlayerPrefs.SetInt(LEGACY_MIGRATED_KEY, 1);
                PlayerPrefs.Save();
            }
        }

        /// <summary>이전 저장 파일을 삭제용 백업으로 보존한다.</summary>
        private static void ArchiveLegacyFiles(string suffix)
        {
            foreach (string path in GetLegacyPaths())
            {
                if (!File.Exists(path)) continue;
                File.Copy(path, path + suffix, true);
                File.Delete(path);
            }
        }

        /// <summary>지정 경로가 있으면 파일을 제거한다.</summary>
        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
