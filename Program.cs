using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using static System.StringComparison;

namespace SosfmtMsgBin {
    internal static class Program {
        private const string InFmt = @"main{
    int firstOff 0;
    $value count firstOff/4;
    int[count] -> str[] entries 0,0;
}

str {
    cstring16 text 0;
}";

        private const string Usage = "Usage: <extract|pack>";
        private const string UsageExtract = "Usage: extract <in/inDir(.bin)> <outDir>";
        private const string UsagePack = "Usage: pack <in/inDir(.xml)> <outDir>";

        private static int CfgFail(int code, string usage, string message = null) {
            if (message == null)
                Console.WriteLine(usage);
            else
                Console.WriteLine($"{message}\n{usage}");
            return code;
        }

        private static int Main(string[] args) {
            if (args.Length == 0)
                return CfgFail(1, Usage);

            switch (args[0].ToLowerInvariant()) {
                case "extract":
                    return Extract(new ArraySegment<string>(args, 1, args.Length - 1));
                case "pack":
                    return Pack(new ArraySegment<string>(args, 1, args.Length - 1));
                default:
                    return CfgFail(1, Usage, $"Unknown verb {args[0]}");
            }
        }

        private static int Extract(IReadOnlyList<string> args) {
            if (args.Count != 2) return CfgFail(1, UsageExtract);
            if (!Directory.Exists(args[1])) Directory.CreateDirectory(args[1]);
            if (File.Exists(args[0])) return ExtractFile(args[0], args[1]);

            var files = Directory.GetFiles(args[0]);
            foreach (var file in files.Where(x => x.EndsWith(".bin", InvariantCultureIgnoreCase))) {
                var ec = ExtractFile(file, args[1]);
                if (ec != 0) return ec;
            }

            return 0;
        }

        private static int ExtractFile(string file, string outDir) {
            try {
                var arr = File.ReadAllBytes(file);
                var fn = Path.GetFileNameWithoutExtension(file);
                Console.WriteLine($"{fn}...");
                var count = BinaryPrimitives.ReadInt32LittleEndian(arr) / 4;
                var settings = new XmlWriterSettings {NewLineChars = "\n", Indent = true};

                using (var writer = XmlWriter.Create(File.CreateText(Path.Combine(outDir, $"{fn}.xml")), settings)) {
                    writer.WriteStartElement("Entries");
                    writer.WriteAttributeString("Count", count.ToString());

                    for (var i = 0; i < count; i++) {
                        writer.WriteStartElement("Entry");
                        var offset = BinaryPrimitives.ReadInt32LittleEndian(arr.AsSpan(i * 4, 4));
                        var next = i + 1 == count
                            ? arr.Length
                            : BinaryPrimitives.ReadInt32LittleEndian(arr.AsSpan((i + 1) * 4, 4));
                        var text = Encoding.Unicode.GetString(arr, offset, next - offset - 2);
                        writer.WriteAttributeString("Index", i.ToString());
                        writer.WriteString(text);
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                }

                return 0;
            } catch (Exception ex) {
                return CfgFail(1, UsageExtract, $"Error extracting from {file}: {ex.Message}");
            }
        }

        private static int Pack(IReadOnlyList<string> args) {
            if (args.Count != 2) return CfgFail(1, UsagePack);
            if (!Directory.Exists(args[1])) Directory.CreateDirectory(args[1]);
            if (File.Exists(args[0])) return PackFile(args[0], args[1]);

            var files = Directory.GetFiles(args[0]);
            foreach (var file in files.Where(x => x.EndsWith(".xml", InvariantCultureIgnoreCase))) {
                var ec = PackFile(file, args[1]);
                if (ec != 0) return ec;
            }

            return 0;
        }

        private static int PackFile(string file, string outDir) {
            try {
                var xml = new XmlDocument();
                xml.Load(file);
                var fn = Path.GetFileNameWithoutExtension(file);
                Console.WriteLine($"{fn}...");
                var root = xml["Entries"];
                var count = int.Parse(root.GetAttribute("Count"));

                using (var fs = File.Create(Path.Combine(outDir, $"{fn}.bin"))) {
                    var main = new byte[count * 4];
                    var loc = count * 4;
                    fs.Position = loc;
                    var null2 = new byte[2];

                    foreach (var element in root) {
                        var entry = element as XmlElement;
                        if (entry == null || entry.Name != "Entry") continue;
                        var i = int.Parse(entry.GetAttribute("Index"));
                        BinaryPrimitives.WriteInt32LittleEndian(main.AsSpan(i * 4, 4), loc);
                        var body = entry.InnerText;
                        var bodyArr = Encoding.Unicode.GetBytes(body);
                        fs.Write(bodyArr, 0, bodyArr.Length);
                        fs.Write(null2, 0, 2);
                        loc = (int) fs.Position;
                    }

                    fs.Position = 0;
                    fs.Write(main, 0, main.Length);
                }

                return 0;
            } catch (Exception ex) {
                return CfgFail(1, UsagePack, $"Error packing from {file}: {ex.Message}");
            }
        }
    }
}
