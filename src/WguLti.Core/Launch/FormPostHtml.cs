using System.Net;

namespace WguLti.Core.Launch;

public static class FormPostHtml
{
    public static string Build(string action, IReadOnlyDictionary<string, string> fields)
    {
        var inputs = string.Concat(fields.Select(pair =>
            $"<input type=\"hidden\" name=\"{Encode(pair.Key)}\" value=\"{Encode(pair.Value)}\"/>"));
        return "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>LTI launch</title></head><body>"
            + $"<form method=\"post\" action=\"{Encode(action)}\">{inputs}"
            + "<noscript><p><button type=\"submit\">Continue</button></p></noscript></form>"
            + "<script>document.forms[0].submit();</script></body></html>";
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
