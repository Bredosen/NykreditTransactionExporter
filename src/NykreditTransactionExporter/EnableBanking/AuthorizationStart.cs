namespace NykreditTransactionExporter.EnableBanking;

internal sealed class AuthorizationStart
{
    #region Properties
    public string Url { get; }

    public string AuthorizationId { get; }
    #endregion

    #region Create authorization start
    public AuthorizationStart(string url, string authorizationId)
    {
        Url = url;
        AuthorizationId = authorizationId;
    }
    #endregion
}
