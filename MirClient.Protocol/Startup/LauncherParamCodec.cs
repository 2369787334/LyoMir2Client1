using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace MirClient.Protocol.Startup;





public static class LauncherParamCodec
{
    
    public const string DefaultSourceKey =
        "E3894C0D-F0DA-4DA6-828B-4E17FAB36B87-5A96ADC0-E444-4D5F-A3B7-8D424727BC56";

    private const int BlowfishBlockSizeBytes = 8; 
    private const int IvSeedSizeBytes = 8; 

    public static byte[] DecodeSourceData(string base64) => DecodeData(base64, DefaultSourceKey);

    public static byte[] DecodeData(string base64, string key)
    {
        if (string.IsNullOrWhiteSpace(base64))
            return Array.Empty<byte>();

        byte[] cipherText = Convert.FromBase64String(base64);
        return DecodeData(cipherText, key);
    }

    public static byte[] DecodeData(ReadOnlySpan<byte> cipherText, string key)
    {
        if (cipherText.Length < IvSeedSizeBytes)
            throw new FormatException("Ciphertext too short.");

        ReadOnlySpan<byte> iv = cipherText[..IvSeedSizeBytes];
        ReadOnlySpan<byte> payload = cipherText[IvSeedSizeBytes..];
        if (payload.IsEmpty)
            return Array.Empty<byte>();

        byte[] keyBytes = DeriveLockBox3BlowfishKey(key);
        var decryptor = new BlowfishEngine();
        decryptor.Init(false, new KeyParameter(keyBytes));

        if (payload.Length < BlowfishBlockSizeBytes)
        {
            var encryptor = new BlowfishEngine();
            encryptor.Init(true, new KeyParameter(keyBytes));
            return DecryptCfb8(encryptor, iv, payload);
        }

        int remainder = payload.Length % BlowfishBlockSizeBytes;
        if (remainder == 0)
            return DecryptCbc(decryptor, iv, payload);

        if (payload.Length <= BlowfishBlockSizeBytes)
            throw new FormatException("Ciphertext stealing requires more than one block.");

        return DecryptCbcCiphertextStealing(decryptor, iv, payload, remainder);
    }

    public static byte[] EncodeData(ReadOnlySpan<byte> plainText, string key)
    {
        Span<byte> iv = stackalloc byte[IvSeedSizeBytes];
        RandomNumberGenerator.Fill(iv);
        return EncodeData(plainText, key, iv);
    }

    public static byte[] EncodeData(ReadOnlySpan<byte> plainText, string key, ReadOnlySpan<byte> iv)
    {
        if (iv.Length != IvSeedSizeBytes)
            throw new ArgumentException("IV must be exactly 8 bytes.", nameof(iv));

        byte[] keyBytes = DeriveLockBox3BlowfishKey(key);
        var encryptor = new BlowfishEngine();
        encryptor.Init(true, new KeyParameter(keyBytes));

        if (plainText.IsEmpty)
            return iv.ToArray();

        if (plainText.Length < BlowfishBlockSizeBytes)
        {
            byte[] payload = EncryptCfb8(encryptor, iv, plainText);
            return ConcatIv(iv, payload);
        }

        int remainder = plainText.Length % BlowfishBlockSizeBytes;
        if (remainder == 0)
        {
            byte[] payload = EncryptCbc(encryptor, iv, plainText);
            return ConcatIv(iv, payload);
        }

        if (plainText.Length <= BlowfishBlockSizeBytes)
        {
            byte[] payload = EncryptCfb8(encryptor, iv, plainText);
            return ConcatIv(iv, payload);
        }

        byte[] csPayload = EncryptCbcCiphertextStealing(encryptor, iv, plainText, remainder);
        return ConcatIv(iv, csPayload);
    }

    internal static byte[] DeriveLockBox3BlowfishKey(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password is required.", nameof(password));

        
        
        int inputSeedSize = password.Length * 2;
        int outputSeedSize = Math.Clamp(inputSeedSize, 1, 56);

        if (inputSeedSize == outputSeedSize)
        {
            
            byte[] seed = Encoding.Unicode.GetBytes(password);
            if (seed.Length != outputSeedSize)
                Array.Resize(ref seed, outputSeedSize);
            return seed;
        }

        
        
        byte[] hash = SHA1.HashData(Encoding.ASCII.GetBytes(password));

        byte[] result = new byte[outputSeedSize];
        for (int i = 0; i < result.Length; i++)
            result[i] = hash[i % hash.Length];

        return result;
    }

    private static byte[] DecryptCbc(BlowfishEngine decryptor, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> cipherText)
    {
        if (cipherText.Length % BlowfishBlockSizeBytes != 0)
            throw new ArgumentException("CBC ciphertext must be a whole number of blocks.", nameof(cipherText));
        if (iv.Length != BlowfishBlockSizeBytes)
            throw new ArgumentException("IV must be exactly one block.", nameof(iv));

        byte[] plaintext = new byte[cipherText.Length];
        Span<byte> prev = stackalloc byte[BlowfishBlockSizeBytes];
        iv.CopyTo(prev);

        var inBlock = new byte[BlowfishBlockSizeBytes];
        var outBlock = new byte[BlowfishBlockSizeBytes];

        for (int offset = 0; offset < cipherText.Length; offset += BlowfishBlockSizeBytes)
        {
            cipherText.Slice(offset, BlowfishBlockSizeBytes).CopyTo(inBlock);
            decryptor.ProcessBlock(inBlock, 0, outBlock, 0);

            for (int i = 0; i < BlowfishBlockSizeBytes; i++)
                plaintext[offset + i] = (byte)(outBlock[i] ^ prev[i]);

            cipherText.Slice(offset, BlowfishBlockSizeBytes).CopyTo(prev);
        }

        return plaintext;
    }

    private static byte[] EncryptCbc(BlowfishEngine encryptor, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> plainText)
    {
        if (plainText.Length % BlowfishBlockSizeBytes != 0)
            throw new ArgumentException("CBC plaintext must be a whole number of blocks.", nameof(plainText));
        if (iv.Length != BlowfishBlockSizeBytes)
            throw new ArgumentException("IV must be exactly one block.", nameof(iv));

        byte[] cipherText = new byte[plainText.Length];
        Span<byte> prev = stackalloc byte[BlowfishBlockSizeBytes];
        iv.CopyTo(prev);

        var inBlock = new byte[BlowfishBlockSizeBytes];
        var outBlock = new byte[BlowfishBlockSizeBytes];

        for (int offset = 0; offset < plainText.Length; offset += BlowfishBlockSizeBytes)
        {
            plainText.Slice(offset, BlowfishBlockSizeBytes).CopyTo(inBlock);
            for (int i = 0; i < BlowfishBlockSizeBytes; i++)
                inBlock[i] ^= prev[i];

            encryptor.ProcessBlock(inBlock, 0, outBlock, 0);
            outBlock.CopyTo(cipherText.AsSpan(offset, BlowfishBlockSizeBytes));
            outBlock.CopyTo(prev);
        }

        return cipherText;
    }

    private static byte[] EncryptCbcCiphertextStealing(
        BlowfishEngine encryptor,
        ReadOnlySpan<byte> iv,
        ReadOnlySpan<byte> plainText,
        int remainder)
    {
        
        
        int fullBlocks = plainText.Length / BlowfishBlockSizeBytes;
        int prefixLen = (fullBlocks - 1) * BlowfishBlockSizeBytes;

        ReadOnlySpan<byte> prefixPlain = plainText[..prefixLen];
        ReadOnlySpan<byte> pNMinus1 = plainText.Slice(prefixLen, BlowfishBlockSizeBytes);
        ReadOnlySpan<byte> pNTrunc = plainText.Slice(prefixLen + BlowfishBlockSizeBytes);

        byte[] prefixCipher = prefixLen > 0 ? EncryptCbc(encryptor, iv, prefixPlain) : Array.Empty<byte>();
        ReadOnlySpan<byte> prev = prefixLen > 0 ? prefixCipher.AsSpan(prefixLen - BlowfishBlockSizeBytes, BlowfishBlockSizeBytes) : iv;

        Span<byte> cNMinus1Full = stackalloc byte[BlowfishBlockSizeBytes];
        EncryptBlockXor(encryptor, pNMinus1, prev, cNMinus1Full);

        Span<byte> tempPlain = stackalloc byte[BlowfishBlockSizeBytes];
        pNTrunc.CopyTo(tempPlain);
        cNMinus1Full.Slice(remainder).CopyTo(tempPlain.Slice(remainder));

        Span<byte> cN = stackalloc byte[BlowfishBlockSizeBytes];
        EncryptBlockXor(encryptor, tempPlain, prev, cN);

        byte[] payload = new byte[prefixCipher.Length + BlowfishBlockSizeBytes + remainder];
        int dst = 0;
        if (prefixCipher.Length > 0)
        {
            Buffer.BlockCopy(prefixCipher, 0, payload, 0, prefixCipher.Length);
            dst += prefixCipher.Length;
        }

        cN.CopyTo(payload.AsSpan(dst, BlowfishBlockSizeBytes));
        dst += BlowfishBlockSizeBytes;
        cNMinus1Full.Slice(0, remainder).CopyTo(payload.AsSpan(dst, remainder));
        return payload;
    }

    private static byte[] DecryptCbcCiphertextStealing(
        BlowfishEngine decryptor,
        ReadOnlySpan<byte> iv,
        ReadOnlySpan<byte> cipherText,
        int remainder)
    {
        
        
        int fullBlocks = cipherText.Length / BlowfishBlockSizeBytes; 
        int prefixLen = (fullBlocks - 1) * BlowfishBlockSizeBytes;

        ReadOnlySpan<byte> prefix = cipherText[..prefixLen];
        ReadOnlySpan<byte> cN = cipherText.Slice(prefixLen, BlowfishBlockSizeBytes);
        ReadOnlySpan<byte> cNMinus1Trunc = cipherText.Slice(prefixLen + BlowfishBlockSizeBytes);

        byte[] prefixPlain = prefixLen > 0 ? DecryptCbc(decryptor, iv, prefix) : Array.Empty<byte>();
        ReadOnlySpan<byte> prev = prefixLen > 0 ? prefix.Slice(prefixLen - BlowfishBlockSizeBytes, BlowfishBlockSizeBytes) : iv;

        Span<byte> tempPlain = stackalloc byte[BlowfishBlockSizeBytes];
        DecryptBlock(decryptor, cN, tempPlain);
        XorInPlace(tempPlain, prev);

        var cNMinus1Full = new byte[BlowfishBlockSizeBytes];
        cNMinus1Trunc.CopyTo(cNMinus1Full);
        tempPlain.Slice(remainder).CopyTo(cNMinus1Full.AsSpan(remainder));

        Span<byte> pNMinus1 = stackalloc byte[BlowfishBlockSizeBytes];
        DecryptBlock(decryptor, cNMinus1Full, pNMinus1);
        XorInPlace(pNMinus1, prev);

        byte[] plaintext = new byte[prefixPlain.Length + BlowfishBlockSizeBytes + remainder];
        int dst = 0;
        if (prefixPlain.Length > 0)
        {
            Buffer.BlockCopy(prefixPlain, 0, plaintext, 0, prefixPlain.Length);
            dst += prefixPlain.Length;
        }

        pNMinus1.CopyTo(plaintext.AsSpan(dst));
        dst += BlowfishBlockSizeBytes;
        tempPlain.Slice(0, remainder).CopyTo(plaintext.AsSpan(dst));
        return plaintext;
    }

    private static byte[] DecryptCfb8(BlowfishEngine encryptor, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> cipherText)
    {
        if (iv.Length != BlowfishBlockSizeBytes)
            throw new ArgumentException("IV must be exactly one block.", nameof(iv));

        byte[] plaintext = new byte[cipherText.Length];
        var shiftRegister = new byte[BlowfishBlockSizeBytes];
        iv.CopyTo(shiftRegister);

        var outBlock = new byte[BlowfishBlockSizeBytes];

        for (int i = 0; i < cipherText.Length; i++)
        {
            encryptor.ProcessBlock(shiftRegister, 0, outBlock, 0);
            byte c = cipherText[i];
            plaintext[i] = (byte)(c ^ outBlock[0]);

            Buffer.BlockCopy(shiftRegister, 1, shiftRegister, 0, BlowfishBlockSizeBytes - 1);
            shiftRegister[^1] = c;
        }

        return plaintext;
    }

    private static byte[] EncryptCfb8(BlowfishEngine encryptor, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> plainText)
    {
        if (iv.Length != BlowfishBlockSizeBytes)
            throw new ArgumentException("IV must be exactly one block.", nameof(iv));

        byte[] cipherText = new byte[plainText.Length];
        var shiftRegister = new byte[BlowfishBlockSizeBytes];
        iv.CopyTo(shiftRegister);

        var outBlock = new byte[BlowfishBlockSizeBytes];

        for (int i = 0; i < plainText.Length; i++)
        {
            encryptor.ProcessBlock(shiftRegister, 0, outBlock, 0);
            byte p = plainText[i];
            byte c = (byte)(p ^ outBlock[0]);
            cipherText[i] = c;

            Buffer.BlockCopy(shiftRegister, 1, shiftRegister, 0, BlowfishBlockSizeBytes - 1);
            shiftRegister[^1] = c;
        }

        return cipherText;
    }

    private static void DecryptBlock(BlowfishEngine decryptor, ReadOnlySpan<byte> cipherBlock, Span<byte> plainBlock)
    {
        if (cipherBlock.Length != BlowfishBlockSizeBytes)
            throw new ArgumentException("Input must be one block.", nameof(cipherBlock));
        if (plainBlock.Length != BlowfishBlockSizeBytes)
            throw new ArgumentException("Output must be one block.", nameof(plainBlock));

        var inBlock = new byte[BlowfishBlockSizeBytes];
        var outBlock = new byte[BlowfishBlockSizeBytes];
        cipherBlock.CopyTo(inBlock);
        decryptor.ProcessBlock(inBlock, 0, outBlock, 0);
        outBlock.CopyTo(plainBlock);
    }

    private static void EncryptBlockXor(BlowfishEngine encryptor, ReadOnlySpan<byte> plainBlock, ReadOnlySpan<byte> xor, Span<byte> cipherBlock)
    {
        if (plainBlock.Length != BlowfishBlockSizeBytes)
            throw new ArgumentException("Input must be one block.", nameof(plainBlock));
        if (xor.Length != BlowfishBlockSizeBytes)
            throw new ArgumentException("XOR must be one block.", nameof(xor));
        if (cipherBlock.Length != BlowfishBlockSizeBytes)
            throw new ArgumentException("Output must be one block.", nameof(cipherBlock));

        var inBlock = new byte[BlowfishBlockSizeBytes];
        var outBlock = new byte[BlowfishBlockSizeBytes];
        plainBlock.CopyTo(inBlock);
        for (int i = 0; i < BlowfishBlockSizeBytes; i++)
            inBlock[i] ^= xor[i];

        encryptor.ProcessBlock(inBlock, 0, outBlock, 0);
        outBlock.CopyTo(cipherBlock);
    }

    private static byte[] ConcatIv(ReadOnlySpan<byte> iv, ReadOnlySpan<byte> payload)
    {
        byte[] result = new byte[IvSeedSizeBytes + payload.Length];
        iv.CopyTo(result);
        payload.CopyTo(result.AsSpan(IvSeedSizeBytes));
        return result;
    }

    private static void XorInPlace(Span<byte> buffer, ReadOnlySpan<byte> value)
    {
        if (buffer.Length != value.Length)
            throw new ArgumentException("XOR spans must have the same length.");

        for (int i = 0; i < buffer.Length; i++)
            buffer[i] ^= value[i];
    }
}
