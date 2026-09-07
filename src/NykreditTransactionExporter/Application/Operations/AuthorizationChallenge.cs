namespace NykreditTransactionExporter.Application.Operations;

internal sealed class AuthorizationChallenge
{
    #region Properties
    public string Url { get; }

    public string State { get; }

    public string RedirectUrl { get; }

    public DateTimeOffset ValidUntil { get; }
    #endregion

    #region Create authorization challenge
    public AuthorizationChallenge(string url, string state, string redirectUrl, DateTimeOffset validUntil)
    {
        Url = url;
        State = state;
        RedirectUrl = redirectUrl;
        ValidUntil = validUntil;
    }
    #endregion
}
