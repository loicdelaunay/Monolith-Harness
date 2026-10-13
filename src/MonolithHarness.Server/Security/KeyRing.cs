using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MonolithHarness.Server.Security;

/// <summary>
/// The Data Protection key ring that encrypts the provider keys. On Windows the keys are encrypted with DPAPI; by default only the account that
/// created them can read them, so a service running under another account would lose every provider key. Re-protecting them for the machine
/// (readable by any account of this computer, still unreadable elsewhere) lets the same data be served by the console and by the service.
/// </summary>
public static class KeyRing
{
    static readonly XNamespace Ns = "http://schemas.asp.net/2015/03/dataProtection";

    /// <summary>
    /// Re-encrypts the DPAPI-protected keys of <paramref name="keysFolder"/> for the machine, after copying the folder to <paramref name="backupFolder"/>.
    /// All keys are read before any file is written: if one cannot be read, nothing changes. Returns the number of keys re-protected.
    /// </summary>
    public static int ProtectToMachine(string keysFolder, string backupFolder)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(keysFolder)) return 0;
        var decryptor = new DpapiXmlDecryptor(new ServiceCollection().BuildServiceProvider());
        var encryptor = new DpapiXmlEncryptor(true, NullLoggerFactory.Instance);
        var updates = new List<(string File, XDocument Document)>();
        foreach (var file in Directory.EnumerateFiles(keysFolder, "*.xml"))
        {
            var document = XDocument.Load(file, LoadOptions.PreserveWhitespace);
            var changed = false;
            foreach (var secret in document.Descendants(Ns + "encryptedSecret").ToList())
            {
                // The attribute is called decryptorType by current versions of Data Protection (decryptor by older ones).
                var kind = secret.Attribute("decryptorType") ?? secret.Attribute("decryptor");
                if (kind?.Value.StartsWith("Microsoft.AspNetCore.DataProtection.XmlEncryption.DpapiXmlDecryptor", StringComparison.Ordinal) != true) continue;
                XElement plain;
                try { plain = decryptor.Decrypt(secret.Elements().First()); }
                catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or InvalidOperationException)
                {
                    throw new InvalidOperationException("Les clés de chiffrement du serveur ne sont lisibles que par le compte qui les a créées. Lancez cette opération depuis ce compte.", ex);
                }
                var info = encryptor.Encrypt(plain);
                secret.ReplaceWith(new XElement(Ns + "encryptedSecret", new XAttribute(kind.Name, info.DecryptorType.AssemblyQualifiedName!), info.EncryptedElement));
                changed = true;
            }
            if (changed) updates.Add((file, document));
        }
        if (updates.Count == 0) return 0;
        Directory.CreateDirectory(backupFolder);
        foreach (var file in Directory.EnumerateFiles(keysFolder)) File.Copy(file, Path.Combine(backupFolder, Path.GetFileName(file)), true);
        foreach (var (file, document) in updates)
        {
            var temporary = file + ".tmp";
            document.Save(temporary);
            File.Move(temporary, file, true);
        }
        return updates.Count;
    }
}
