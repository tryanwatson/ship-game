namespace ShipGame.Net;

/// <summary>A server to join, as players type it: <c>host</c>, <c>host:port</c>, <c>[v6]:port</c>, or a bare IPv6 address.</summary>
public readonly record struct ServerAddress(string Host, int Port)
{
    public static bool TryParse(string? text, out ServerAddress address)
    {
        address = default;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
            return false;

        string host;
        var port = Protocol.DefaultPort;
        if (text.StartsWith('['))
        {
            // [::1] or [::1]:7777
            var close = text.IndexOf(']');
            if (close < 2)
                return false;
            host = text[1..close];
            var rest = text[(close + 1)..];
            if (rest.Length > 0 && !(rest.StartsWith(':') && TryParsePort(rest[1..], out port)))
                return false;
        }
        else if (text.Count(c => c == ':') == 1)
        {
            var colon = text.IndexOf(':');
            host = text[..colon];
            if (!TryParsePort(text[(colon + 1)..], out port))
                return false;
        }
        else
        {
            host = text; // no port, or a bare IPv6 address (which can't carry one without brackets)
        }

        if (host.Length == 0 || host.Any(char.IsWhiteSpace))
            return false;
        address = new ServerAddress(host, port);
        return true;
    }

    /// <summary>As players would type it; the default port is left off.</summary>
    public override string ToString()
    {
        var host = Host.Contains(':') ? $"[{Host}]" : Host;
        return Port == Protocol.DefaultPort ? host : $"{host}:{Port}";
    }

    private static bool TryParsePort(string text, out int port) =>
        int.TryParse(text, out port) && port is > 0 and <= 65535;
}
