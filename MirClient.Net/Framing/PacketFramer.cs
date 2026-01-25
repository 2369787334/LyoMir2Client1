namespace MirClient.Net.Framing;





public sealed class PacketFramer
{
    private string _buffer = string.Empty;

    public IEnumerable<string> Push(string chunk)
    {
        if (!string.IsNullOrEmpty(chunk))
            _buffer += chunk;

        while (_buffer.Length >= 2 && _buffer.Contains('!'))
        {
            _buffer = ArrestStringEx(_buffer, '#', '!', out string packet);
            if (string.IsNullOrEmpty(packet))
                yield break;

            yield return packet;
        }
    }

    public void Clear() => _buffer = string.Empty;

    internal static string ArrestStringEx(string source, char searchAfter, char arrestBefore, out string arrestStr)
    {
        arrestStr = string.Empty;
        if (string.IsNullOrEmpty(source))
            return string.Empty;

        int srcLen = source.Length;
        int i = source.IndexOf(searchAfter);
        if (i >= 0)
        {
            int n = source.IndexOf(arrestBefore, i + 1);
            if (n > i)
            {
                arrestStr = source.Substring(i + 1, n - i - 1);
                return source.Substring(n + 1, srcLen - n - 1);
            }

            arrestStr = source.Substring(i + 1, srcLen - i - 1);
            return string.Empty;
        }

        int bang = source.IndexOf(arrestBefore);
        if (bang >= 0)
        {
            arrestStr = source.Substring(0, bang);
            return source.Substring(bang + 1, srcLen - bang - 1);
        }

        arrestStr = source;
        return string.Empty;
    }
}

