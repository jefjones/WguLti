namespace WguLti.Core;

public sealed class LtiPlatformException : Exception
{
    public LtiPlatformException(int statusCode, string error, string description)
        : base(description)
    {
        StatusCode = statusCode;
        Error = error;
    }

    public int StatusCode { get; }
    public string Error { get; }
}
