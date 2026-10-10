using System.Diagnostics;
using System.Text;

namespace MonolithHarness.Core;

/// <summary>Reads only bounded GGUF metadata, without loading tensors or allocating the vocabulary.</summary>
public static class GgufContext
{
    public static int? Read(string path, CancellationToken ct)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);
            var watch = Stopwatch.StartNew();
            long budget = Math.Min(file.Length, 64L * 1024 * 1024);
            void Check()
            {
                ct.ThrowIfCancellationRequested();
                if (file.Position > budget || watch.Elapsed > TimeSpan.FromSeconds(2)) throw new InvalidDataException();
            }
            void Skip(ulong bytes)
            {
                Check();
                if (bytes > (ulong)Math.Max(0, budget - file.Position)) throw new InvalidDataException();
                file.Seek((long)bytes, SeekOrigin.Current);
            }
            string Text()
            {
                Check(); var size = reader.ReadUInt64();
                if (size > 1024 || size > (ulong)Math.Max(0, budget - file.Position)) throw new InvalidDataException();
                var bytes = reader.ReadBytes((int)size);
                if (bytes.Length != (int)size) throw new EndOfStreamException();
                return Encoding.UTF8.GetString(bytes);
            }
            int Size(uint type) => type switch { 0 or 1 or 7 => 1, 2 or 3 => 2, 4 or 5 or 6 => 4, 10 or 11 or 12 => 8, _ => 0 };
            void SkipValue(uint type, int depth = 0)
            {
                Check(); if (depth > 8) throw new InvalidDataException();
                if (Size(type) is > 0 and var size) { Skip((ulong)size); return; }
                if (type == 8) { Skip(reader.ReadUInt64()); return; }
                if (type != 9) throw new InvalidDataException();
                var element = reader.ReadUInt32(); var length = reader.ReadUInt64();
                if (length > 1_000_000) throw new InvalidDataException();
                if (Size(element) is > 0 and var width) { Skip(checked(length * (ulong)width)); return; }
                for (ulong i = 0; i < length; i++) SkipValue(element, depth + 1);
            }
            if (reader.ReadUInt32() != 0x46554747 || reader.ReadUInt32() is not (2 or 3)) return null;
            reader.ReadUInt64(); // Tensor count; tensor descriptors and data are never read.
            var count = reader.ReadUInt64(); if (count > 10000) return null;
            string architecture = "";
            var limits = new Dictionary<string, int>(StringComparer.Ordinal);
            for (ulong i = 0; i < count; i++)
            {
                Check(); var key = Text(); var type = reader.ReadUInt32();
                if (key == "general.architecture" && type == 8) architecture = Text();
                else if (key.EndsWith(".context_length", StringComparison.Ordinal) && type is 4 or 5 or 10 or 11)
                {
                    long value = type switch { 4 => reader.ReadUInt32(), 5 => reader.ReadInt32(), 10 => checked((long)reader.ReadUInt64()), _ => reader.ReadInt64() };
                    if (value is >= 1024 and <= 10_000_000) limits[key] = (int)value;
                }
                else SkipValue(type);
                if (architecture.Length > 0 && limits.TryGetValue(architecture + ".context_length", out var context)) return context;
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException) { return null; }
    }
}
