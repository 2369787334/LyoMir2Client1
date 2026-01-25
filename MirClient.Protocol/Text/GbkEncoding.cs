using System.Text;

namespace MirClient.Protocol.Text;

public static class GbkEncoding
{
    private static readonly Lazy<Encoding> LazyEncoding = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936);
    });

    public static Encoding Instance => LazyEncoding.Value;
}

