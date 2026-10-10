using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using MonolithHarness.Core;
using System.Text;
namespace MonolithHarness.Core.Mobile;

/// <summary>API keys are encrypted with a non-exportable AES key in Android Keystore.</summary>
public sealed class AndroidSecretVault : ISecretVault
{
    const string Alias="monolith.mobile.apikey.v1";
    readonly object gate=new();
    IKey Key()
    {
        using var store=KeyStore.GetInstance("AndroidKeyStore")!; store.Load(null);
        if(store.ContainsAlias(Alias)) return store.GetKey(Alias,null)!;
        using var generator=KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes,"AndroidKeyStore")!;
        using var spec=new KeyGenParameterSpec.Builder(Alias,KeyStorePurpose.Encrypt|KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm!).SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone!).Build();
        generator.Init(spec); return generator.GenerateKey()!;
    }
    public byte[] Protect(string secret)
    {
        lock(gate) {
            using var cipher=Cipher.GetInstance("AES/GCM/NoPadding")!; using var key=Key(); cipher.Init(CipherMode.EncryptMode,key);
            var iv=cipher.GetIV()!; var encrypted=cipher.DoFinal(Encoding.UTF8.GetBytes(secret))!;
            return new byte[]{(byte)iv.Length}.Concat(iv).Concat(encrypted).ToArray();
        }
    }
    public string Unprotect(byte[] protectedSecret)
    {
        if(protectedSecret.Length==0) return "";
        int length=protectedSecret[0]; if(length!=12 || protectedSecret.Length<=length+1) throw new InvalidOperationException("Clé API enregistrée invalide. Enregistrez-la à nouveau.");
        lock(gate) {
            using var cipher=Cipher.GetInstance("AES/GCM/NoPadding")!; using var key=Key(); using var spec=new GCMParameterSpec(128,protectedSecret[1..(length+1)]);
            cipher.Init(CipherMode.DecryptMode,key,spec); return Encoding.UTF8.GetString(cipher.DoFinal(protectedSecret[(length+1)..])!);
        }
    }
}
