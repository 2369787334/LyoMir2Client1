namespace MirClient.Audio;

public static class SoundUtil
{
    
    
    
    
    public static void GainPanVolume(int srcX, int srcY, int listenerX, int listenerY, out float volume, out float pan)
    {
        float dx = srcX - listenerX;
        float dy = srcY - listenerY;
        float dist = MathF.Sqrt((dx * dx) + (dy * dy));

        if (dist <= 0.0001f)
        {
            volume = 1f;
            pan = 0f;
            return;
        }

        float dir = dx > 0 ? 1f : dx < 0 ? -1f : 0f;

        if (dist >= 16f)
        {
            volume = 0f;
            pan = 0f;
            return;
        }

        volume = Math.Clamp(MathF.Cos(MathF.PI * (dist / 32f)), 0f, 1f);
        pan = Math.Clamp(dir * MathF.Sin(MathF.PI * (dist / 28f)), -1f, 1f);
    }
}
