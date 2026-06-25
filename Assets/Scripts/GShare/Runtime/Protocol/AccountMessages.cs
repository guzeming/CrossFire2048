using System;

namespace CrossFire2048.Shared.Protocol
{
    /// <summary>
    /// 账户相关消息的统一结果码，客户端可据此显示提示。
    /// </summary>
    public enum AuthResultCode
    {
        Ok = 0,
        UnknownError = 1,
        InvalidFormat = 2,
        AccountAlreadyExists = 3,
        AccountNotFound = 4,
        WrongPassword = 5,
        AlreadyLoggedIn = 6,
    }

    [Serializable]
    public class RegisterRequest
    {
        public string Username;
        public string Password;

        public RegisterRequest()
        {
            Username = string.Empty;
            Password = string.Empty;
        }
    }

    [Serializable]
    public class RegisterResponse
    {
        public AuthResultCode Code;
        public string Message;

        public RegisterResponse()
        {
            Code = AuthResultCode.UnknownError;
            Message = string.Empty;
        }
    }

    [Serializable]
    public class LoginRequest
    {
        public string Username;
        public string Password;

        public LoginRequest()
        {
            Username = string.Empty;
            Password = string.Empty;
        }
    }

    [Serializable]
    public class LoginResponse
    {
        public AuthResultCode Code;
        public string Message;
        public string UserId;
        public string SessionToken;

        public LoginResponse()
        {
            Code = AuthResultCode.UnknownError;
            Message = string.Empty;
            UserId = string.Empty;
            SessionToken = string.Empty;
        }
    }

    [Serializable]
    public class ErrorResponse
    {
        public string Message;

        public ErrorResponse()
        {
            Message = string.Empty;
        }
    }

    [Serializable]
    public class Heartbeat
    {
        public long ClientTimeMs;

        public Heartbeat()
        {
            ClientTimeMs = 0;
        }
    }
}
