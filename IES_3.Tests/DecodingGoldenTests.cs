using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using IES_2.ECU;
using Xunit;

namespace IES_2.Tests
{
    /// <summary>
    /// Pins what the diagnostic engine decodes: every parameter and fault of every ECU, over a set of
    /// raw byte patterns, against a file generated from the untouched upstream formulas. The project
    /// promises they never change silently, so any difference here must fail the build. To regenerate
    /// after a deliberate, documented formula fix, set UPDATE_GOLDEN to the source Golden folder.
    /// </summary>
    [Collection(EcuStaticState.Name)]
    public class DecodingGoldenTests
    {
        private const string FileName = "decode.golden.txt";

        private static readonly (string Name, Func<int, byte> Fill)[] Patterns =
        {
            ("zero", _ => 0x00),
            ("one", _ => 0x01),
            ("mid", _ => 0x80),
            ("0x55", _ => 0x55),
            ("0xAA", _ => 0xAA),
            ("full", _ => 0xFF),
            ("ascending", i => (byte)i),
            ("descending", i => (byte)(255 - i)),
            ("mixed", i => (byte)(i * 7 + 3)),
        };

        private static IEnumerable<ecu> AllEcus()
        {
            var port = new SerialPort();
            yield return new iaw16f(ref port);
            yield return new iaw18f(ref port);
            yield return new iaw18fd(ref port);
            yield return new iaw8f_68(ref port);
            yield return new iaw04k(ref port);
            yield return new code(ref port);
        }

        private static string Render()
        {
            var previousUi = CultureInfo.CurrentUICulture;
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                var text = new StringBuilder();
                foreach (var unit in AllEcus())
                {
                    var name = unit.GetType().Name;
                    foreach (var (pattern, fill) in Patterns)
                    {
                        for (int i = 0; i < ecu.Buffer.Length; i++) ecu.Buffer[i] = fill(i);
                        for (int i = 0; i < unit.engineData.Length; i++)
                        {
                            var index = i;
                            text.Append(name).Append('|').Append(pattern).Append("|data|").Append(i).Append('|')
                                .Append(Try(() => unit.engineData[index].Value.ToString(CultureInfo.InvariantCulture))).Append('\n');
                        }
                        AppendErrors(text, name, pattern, "engine", unit.engineErrors);
                        AppendErrors(text, name, pattern, "immo", unit.immoErrors);
                    }
                }
                return text.ToString();
            }
            finally
            {
                CultureInfo.CurrentUICulture = previousUi;
                CultureInfo.CurrentCulture = previous;
            }
        }

        private static void AppendErrors(StringBuilder text, string name, string pattern, string kind, errorElement[] errors)
        {
            for (int i = 0; i < errors.Length; i++)
            {
                var index = i;
                text.Append(name).Append('|').Append(pattern).Append('|').Append(kind).Append('|').Append(i).Append('|')
                    .Append(Try(() =>
                    {
                        errors[index].Decode();
                        var e = errors[index];
                        return $"{e.isActive},{e.isVerified},{e.isStored},{e.Reason}";
                    })).Append('\n');
            }
        }

        private static string Try(Func<string> read)
        {
            try { return read(); }
            catch (Exception ex) { return "throws " + ex.GetType().Name; }
        }

        [Fact]
        public void DecodedValuesMatchTheUpstreamFormulas()
        {
            var actual = Render();

            var update = Environment.GetEnvironmentVariable("UPDATE_GOLDEN");
            if (!string.IsNullOrEmpty(update))
            {
                File.WriteAllText(Path.Combine(update, FileName), actual, new UTF8Encoding(false));
                return;
            }

            var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", FileName));
            var expectedLines = expected.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            var actualLines = actual.Split('\n');
            Assert.Equal(expectedLines.Length, actualLines.Length);
            for (int i = 0; i < expectedLines.Length; i++)
                Assert.Equal(expectedLines[i], actualLines[i]);
        }

        [Fact]
        public void GoldenFileCoversEveryEcu()
        {
            var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", FileName));
            foreach (var unit in AllEcus())
                Assert.Contains(unit.GetType().Name + "|", expected);
        }
    }

    /// <summary>The engine keeps its buffers in statics: tests touching them must not run in parallel.</summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public static class EcuStaticState
    {
        public const string Name = "ECU static state";
    }
}
