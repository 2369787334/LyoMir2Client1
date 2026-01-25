namespace MirClient.Assets.Palettes;

internal static class Mir2MainPalette
{
    private const string ColorArrayBase64 =
        "AAAAAAAAgAAAgAAAAICAAIAAAACAAIAAgIAAAMDAwACXgFUAyLmdAHNzewApKS0AUlJaAFpaYwA5OUIAGBgdABAQGAAYGCkACAgQAHF58gBfZ+EAWlr/ADEx/wBSWtYAABCUABgplAAACDkAABBzAAAYtQBSY70AEBhCAJmq/wAAEFoAKTlzADFKpQBze5QAMVK9ABAhUgAYMXsAEBgtADFKjAAAKZQAADG9AFJzxgAYMWsAQmvGAABKzgA5Y6UAGDFaAAAQKgAACBUAABg6AAAACAAAACkAAABKAAAAnQAAANwAAADeAAAA+wBSc5wASmuUAClKcwAYMVIAGEqMABFEiAAAIUoAEBghAFqU1gAha8YAAGvvAAB3/wCElKUAITFCAAgQGAAIGCkAABAhABgpOQA5Y4wAEClCABhCawAYSnsAAEqUAHuEjABaY2sAOUJKABghKQApOUYAlKW1AFprewCUsc4Ac4ylAFpzjABzlLUAc6XWAEql7wCMxu8AQmN7ADlWawBalL0AADljAK3G1gApQlIAGGOUAK3W7wBjjKUASlpjAHulvQAYQloAMYy9ACkxNQBjhJQASmt7AFqMpQApSloAOXucABAxQgAhre8AABAYAAAhKQAAa5wAWoSUABhCUgApWmsAIWN7ACF7nAAApd4AOVJaABApMQB7vc4AOVpjAEqElAAppcYAGJwQAEqMQgBCjDEAKZQQABAYCAAYGAgAECkIAClCGACttaUAc3NrACkpGABKQhgASkIxAN7GYwD/3UQA79aMADlrcwA53vcAjO/3AADn9wBaa2sApYxaAO+1OQDOnEoAtYQxAGtSMQDW3t4Atb29AISMjADe9/cAGAgAADkYCAApEAgAABgIAAApCAClUgAA3nsAAEopEABrORAAjFIQAKVaIQBaMRAAhEIQAIRSMQAxIRgAe1pKAKVrUgBjOSkA3koQACEpKQA5SkoAGCkpAClKSgBCe3sASpycAClaWgAUQkIAADk5AABZWQAsNcoAIXNrAAAxKQAQOTEAGDkxAABKQgAYY1IAKXNaABhKMQAAIRgAADEYABA5GABKhGMASr1rAEq1YwBKvWMASpxaADmMSgBKxmMAStZjAEqEUgApczEAWsZjAEq9UgAA/xAAGCkYAEqISgBK50oAAFoAAACIAAAAlAAAAN4AAADuAAAA+wAAlFpKALVzYwDWjHsA1ntrAP+IdwDOxsYAnJSUAMaUnAA5MTEAhBgpAIQAGABSQkoAe0JSAHNaYwD3tc4AnHuMAMwidwD/qt0AKrTwAJ8A3wCzF+MA8Pv/AKSgoACAgIAAAAD/AAD/AAAA//8A/wAAAP8A/wD//wAA////AA==";

    private static readonly Lazy<uint[]> PaletteLazy = new(BuildPalette, isThreadSafe: true);

    public static ReadOnlySpan<uint> Colors => PaletteLazy.Value;

    private static uint[] BuildPalette()
    {
        byte[] colorArray = Convert.FromBase64String(ColorArrayBase64);

        if (colorArray.Length != 1024)
            throw new InvalidOperationException($"Palette byte array must be 1024 bytes, got {colorArray.Length}.");

        var palette = new uint[256];
        for (int i = 0; i < palette.Length; i++)
        {
            int offset = i * 4;
            byte b = colorArray[offset + 0];
            byte g = colorArray[offset + 1];
            byte r = colorArray[offset + 2];
            palette[i] = (uint)(b | (g << 8) | (r << 16) | (255u << 24));
        }

        return palette;
    }
}

