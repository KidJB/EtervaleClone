namespace PristonTale.Contracts.Sessions;

// Values are the LoginResult statuses on the wire. Unknown accounts report a bad password so the reply does not reveal
// which accounts exist.
public enum LoginFailure
{
    None = 0,
    BadCredentials = 1,
    LoginDenied = 2,
    AlreadyLoggedIn = 3,
    Busy = 4,
    OutdatedClient = 5
}

// How a login proves the account: its password, its password asking for a "remember me" token, or such a token.
public enum LoginMode : byte
{
    Password = 0,
    Remember = 1,
    Token = 2
}

public sealed record LoginOutcome(bool Ok, string SessionToken, string Message)
{
    public LoginFailure Failure { get; init; }

    // The "remember me" token a Remember login was given, else empty.
    public string RememberToken { get; init; } = string.Empty;

    public static LoginOutcome Denied(LoginFailure failure) => new(false, string.Empty, MessageFor(failure)) { Failure = failure };

    public static LoginOutcome Success(string sessionToken, string message) =>
        new(true, sessionToken, message);

    public static string MessageFor(LoginFailure failure) => failure switch
    {
        LoginFailure.None => "",
        LoginFailure.BadCredentials => "invalid account or password",
        LoginFailure.LoginDenied => "account is blocked",
        LoginFailure.AlreadyLoggedIn => "already logged in on this connection",
        LoginFailure.Busy => "the server is busy, try again later",
        LoginFailure.OutdatedClient => "client version is older than the server requires",
        _ => "login failed"
    };
}
