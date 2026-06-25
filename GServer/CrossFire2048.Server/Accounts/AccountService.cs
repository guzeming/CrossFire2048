using System.Security.Cryptography;
using CrossFire2048.Server.Storage;
using CrossFire2048.Shared.Protocol;

namespace CrossFire2048.Server.Accounts
{
    /// <summary>
    /// 注册与登录的业务逻辑。负责格式校验、查重、密码哈希与校验。
    /// 这里是服务端权威校验，客户端的校验只是提前提示，不可信任。
    /// </summary>
    public sealed class AccountService
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 100_000;

        private readonly AccountStore _store;

        public AccountService(AccountStore store)
        {
            _store = store;
        }

        public int AccountCount => _store.Count;

        public RegisterResult Register(string username, string password)
        {
            if (!AccountRules.IsUsernameValid(username) || !AccountRules.IsPasswordValid(password))
            {
                return new RegisterResult(AuthResultCode.InvalidFormat, "用户名或密码格式不合法");
            }

            if (_store.Exists(username))
            {
                return new RegisterResult(AuthResultCode.AccountAlreadyExists, "该用户名已被注册");
            }

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] hash = HashPassword(password, salt);

            AccountRecord record = new AccountRecord
            {
                UserId = Guid.NewGuid().ToString("N"),
                Username = username,
                PasswordSalt = Convert.ToBase64String(salt),
                PasswordHash = Convert.ToBase64String(hash),
                CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };

            if (!_store.Add(record))
            {
                return new RegisterResult(AuthResultCode.AccountAlreadyExists, "该用户名已被注册");
            }

            return new RegisterResult(AuthResultCode.Ok, "注册成功");
        }

        public LoginResult Login(string username, string password)
        {
            if (!AccountRules.IsUsernameValid(username) || !AccountRules.IsPasswordValid(password))
            {
                return new LoginResult(AuthResultCode.InvalidFormat, "用户名或密码格式不合法", null);
            }

            AccountRecord? record = _store.Find(username);
            if (record == null)
            {
                return new LoginResult(AuthResultCode.AccountNotFound, "账号不存在", null);
            }

            byte[] salt = Convert.FromBase64String(record.PasswordSalt);
            byte[] expected = Convert.FromBase64String(record.PasswordHash);
            byte[] actual = HashPassword(password, salt);

            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                return new LoginResult(AuthResultCode.WrongPassword, "密码错误", null);
            }

            return new LoginResult(AuthResultCode.Ok, "登录成功", record);
        }

        private static byte[] HashPassword(string password, byte[] salt)
        {
            using Rfc2898DeriveBytes pbkdf2 = new Rfc2898DeriveBytes(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256);

            return pbkdf2.GetBytes(HashSize);
        }
    }

    public readonly struct RegisterResult
    {
        public readonly AuthResultCode Code;
        public readonly string Message;

        public RegisterResult(AuthResultCode code, string message)
        {
            Code = code;
            Message = message;
        }
    }

    public readonly struct LoginResult
    {
        public readonly AuthResultCode Code;
        public readonly string Message;
        public readonly AccountRecord? Account;

        public LoginResult(AuthResultCode code, string message, AccountRecord? account)
        {
            Code = code;
            Message = message;
            Account = account;
        }
    }
}
