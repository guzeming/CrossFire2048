namespace CrossFire2048.Shared.Protocol
{
    /// <summary>
    /// 账户字段的校验规则，客户端与服务端共用，保证两端判断一致。
    /// 客户端用于即时提示，服务端用于最终权威校验。
    /// </summary>
    public static class AccountRules
    {
        public const int UsernameMinLength = 3;
        public const int UsernameMaxLength = 16;
        public const int PasswordMinLength = 6;
        public const int PasswordMaxLength = 32;

        public static bool IsUsernameValid(string username)
        {
            if (string.IsNullOrEmpty(username))
            {
                return false;
            }

            if (username.Length < UsernameMinLength || username.Length > UsernameMaxLength)
            {
                return false;
            }

            foreach (char c in username)
            {
                bool isLetter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                bool isDigit = c >= '0' && c <= '9';
                bool isUnderscore = c == '_';
                if (!isLetter && !isDigit && !isUnderscore)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsPasswordValid(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return false;
            }

            return password.Length >= PasswordMinLength && password.Length <= PasswordMaxLength;
        }
    }
}
