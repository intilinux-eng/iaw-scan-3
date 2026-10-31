using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace IES_2.Tests
{
    /// <summary>Checks over the sources: no text may be missing from a language or from the dictionaries.</summary>
    public class LocalizationTests
    {
        private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

        private static string Source(params string[] parts)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "IES_3.sln")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            return Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray());
        }

        private static Dictionary<string, string> Strings(string language)
        {
            var document = XDocument.Load(Source("IES_3.Avalonia", "Localization", $"Strings.{language}.axaml"));
            return document.Root!.Elements(X + "String").ToDictionary(e => e.Attribute(X + "Key")!.Value, e => e.Value);
        }

        [Fact]
        public void BothLanguagesHaveTheSameTexts()
        {
            var english = Strings("en").Keys.OrderBy(k => k).ToList();
            var italian = Strings("it").Keys.OrderBy(k => k).ToList();
            Assert.Equal(english, italian);
        }

        [Fact]
        public void TranslationsKeepTheirPlaceholders()
        {
            var italian = Strings("it");
            foreach (var (key, text) in Strings("en"))
            {
                var expected = Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).OrderBy(v => v);
                var actual = Regex.Matches(italian[key], @"\{\d+\}").Select(m => m.Value).OrderBy(v => v);
                Assert.True(expected.SequenceEqual(actual), $"Placeholders differ in {key}");
            }
        }

        [Fact]
        public void NoTextIsEmpty()
        {
            foreach (var language in new[] { "en", "it" })
                foreach (var (key, text) in Strings(language))
                    Assert.False(string.IsNullOrWhiteSpace(text), $"{key} is empty in {language}");
        }

        [Fact]
        public void EveryTextTheCodeAsksForExists()
        {
            var known = Strings("en").Keys.ToHashSet();
            var root = Source("IES_3.Avalonia");
            var prefix = @"(?:App|Nav|Status|Banner|Dialog|Connect|Dashboard|Parameters|Faults|Tests|Graph|Adjust|Sessions|Settings|Outcome|Event)_\w+";
            var patterns = new[]
            {
                new Regex(@"DynamicResource\s+(" + prefix + ")"),
                new Regex(@"Loc\.[TF]\(""(" + prefix + @")"""),
                new Regex(@"""(" + prefix + @")"""),
            };

            var missing = new List<string>();
            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                    || file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                    || file.Contains("Localization"))
                    continue;
                if (!file.EndsWith(".cs") && !file.EndsWith(".axaml")) continue;

                var text = File.ReadAllText(file);
                foreach (var pattern in patterns)
                    foreach (Match match in pattern.Matches(text))
                        if (!known.Contains(match.Groups[1].Value))
                            missing.Add($"{Path.GetFileName(file)}: {match.Groups[1].Value}");
            }
            Assert.Empty(missing);
        }

        [Fact]
        public void EveryTextIsUsedSomewhere()
        {
            var root = Source("IES_3.Avalonia");
            var sources = string.Concat(Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml"))
                    && !f.Contains("Localization")
                    && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                    && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                .Select(File.ReadAllText));

            var unused = Strings("en").Keys.Where(k => !sources.Contains(k)).ToList();
            Assert.Empty(unused);
        }

        [Fact]
        public void TheInterfaceHasNoHardCodedSentences()
        {
            // Anything a user reads goes through the dictionaries: a Text="..." with words in a view is a bug.
            var views = Source("IES_3.Avalonia", "Views");
            var literal = new Regex(@"(?:Text|Content|Header|PlaceholderText|ToolTip\.Tip)=""([^""{]*[A-Za-zÀ-ÿ]{3,}[^""{]*)""");
            var allowed = new HashSet<string> { "IAW Scan 3", "MIL", "min", "·  ISO" };

            var found = new List<string>();
            foreach (var file in Directory.EnumerateFiles(views, "*.axaml"))
                foreach (Match match in literal.Matches(File.ReadAllText(file)))
                {
                    var text = match.Groups[1].Value.Trim();
                    if (!allowed.Contains(text) && !Regex.IsMatch(text, @"^\d+ (s|min)$"))
                        found.Add($"{Path.GetFileName(file)}: {text}");
                }
            Assert.Empty(found);
        }
    }
}
