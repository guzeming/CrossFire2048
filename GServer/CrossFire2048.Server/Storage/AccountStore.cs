using System.Text.Json;

namespace CrossFire2048.Server.Storage
{
    /// <summary>
    /// 账户的本地文件存储。第一阶段用 JSON 文件保存，
    /// 后续可以无痛替换为数据库实现，只要保持读写接口一致。
    /// 所有公开方法都使用内部锁，保证多连接并发访问安全。
    /// </summary>
    public sealed class AccountStore
    {
        private readonly string _filePath;
        private readonly object _gate = new object();

        // 用户名（小写）-> 账户记录，用于快速查重和登录查询。
        private readonly Dictionary<string, AccountRecord> _accountsByUsername =
            new Dictionary<string, AccountRecord>();

        public AccountStore(string filePath)
        {
            _filePath = filePath;
        }

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _accountsByUsername.Count;
                }
            }
        }

        public void Load()
        {
            lock (_gate)
            {
                _accountsByUsername.Clear();

                if (!File.Exists(_filePath))
                {
                    return;
                }

                string json = File.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                List<AccountRecord>? records = JsonSerializer.Deserialize<List<AccountRecord>>(json);
                if (records == null)
                {
                    return;
                }

                foreach (AccountRecord record in records)
                {
                    if (string.IsNullOrEmpty(record.Username))
                    {
                        continue;
                    }

                    _accountsByUsername[record.Username.ToLowerInvariant()] = record;
                }
            }
        }

        public void Save()
        {
            lock (_gate)
            {
                string? directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                List<AccountRecord> records = _accountsByUsername.Values.ToList();
                string json = JsonSerializer.Serialize(records, new JsonSerializerOptions
                {
                    WriteIndented = true,
                });

                File.WriteAllText(_filePath, json);
            }
        }

        public bool Exists(string username)
        {
            lock (_gate)
            {
                return _accountsByUsername.ContainsKey(username.ToLowerInvariant());
            }
        }

        public AccountRecord? Find(string username)
        {
            lock (_gate)
            {
                _accountsByUsername.TryGetValue(username.ToLowerInvariant(), out AccountRecord? record);
                return record;
            }
        }

        /// <summary>
        /// 添加账户并立即落盘。若用户名已存在则返回 false。
        /// </summary>
        public bool Add(AccountRecord record)
        {
            lock (_gate)
            {
                string key = record.Username.ToLowerInvariant();
                if (_accountsByUsername.ContainsKey(key))
                {
                    return false;
                }

                _accountsByUsername[key] = record;
                SaveNoLock();
                return true;
            }
        }

        private void SaveNoLock()
        {
            string? directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            List<AccountRecord> records = _accountsByUsername.Values.ToList();
            string json = JsonSerializer.Serialize(records, new JsonSerializerOptions
            {
                WriteIndented = true,
            });

            File.WriteAllText(_filePath, json);
        }
    }
}
