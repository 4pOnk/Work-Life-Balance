namespace WorkLifeBalance.Domain;

public static class SiteRules
{
    // Rules are hosts, never paths or URL substrings. IDNs use their ASCII representation.
    public static string Normalize(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 2048 || value.Any(char.IsWhiteSpace) || value.Contains('\\'))
            throw new ArgumentException("Укажите домен сайта или HTTP/HTTPS-адрес.");
        if (!Uri.TryCreate(value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
            throw new ArgumentException("Укажите домен сайта или HTTP/HTTPS-адрес без логина и пароля.");
        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        var type = Uri.CheckHostName(host);
        if (host.Length > 253 || type is not (UriHostNameType.Dns or UriHostNameType.IPv4) ||
            (type == UriHostNameType.Dns && !host.Contains('.')))
            throw new ArgumentException("Укажите полный домен, например docs.unity3d.com, или IPv4-адрес.");
        return host;
    }

    public static bool Matches(string? domain, IReadOnlyList<string> rules)
    {
        if (string.IsNullOrEmpty(domain)) return false;
        string host;
        try { host = Normalize(domain); }
        catch (ArgumentException) { return false; }
        return rules.Any(rule => host.Equals(rule, StringComparison.OrdinalIgnoreCase) ||
            (Uri.CheckHostName(rule) == UriHostNameType.Dns && host.EndsWith("." + rule, StringComparison.OrdinalIgnoreCase)));
    }
}
