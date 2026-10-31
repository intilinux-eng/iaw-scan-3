using System;
using System.Collections.Generic;
using System.Linq;
using IES_2.ECU;
using IES_2.Session;

namespace IES_2.Coupe
{
    /// <summary>
    /// The only ECUs this edition offers: the Fiat Coupé's engine ECU (Weber-Marelli IAW-04K.P8,
    /// 2.0 16V and 2.0 16V Turbo) and its FIAT CODE immobiliser, each limited to the Coupé rows
    /// of the upstream vehicle tables. The tables themselves are read, never edited.
    /// </summary>
    public static class CoupeCatalog
    {
        /// <summary>Simulation-mode ISO: the Coupé 2.0 16V, present in both the IAW-04K.P8 and FIAT CODE tables.</summary>
        public const string DemoIso = "55CE858394BF";

        public static IReadOnlyList<CoupeEcu> Ecus { get; } = new[]
        {
            new CoupeEcu(new EcuOption(iaw04k.name, iaw04k.longName, CoupeRows(iaw04k.cars)), "Centralina motore"),
            new CoupeEcu(new EcuOption(code.name, code.longName, CoupeRows(code.cars)), "Immobilizer"),
        };

        private static readonly HashSet<string> CoupeIsos = new(
            CoupeIsoCodes(iaw04k.cars).Concat(CoupeIsoCodes(code.cars)), StringComparer.OrdinalIgnoreCase);

        public static CoupeEcu? Find(string? id) => Ecus.FirstOrDefault(e => e.Option.Id == id);

        /// <summary>True when the ISO code read from the ECU is one of the known Fiat Coupé ones.</summary>
        public static bool IsCoupeIso(string? iso) => iso != null && CoupeIsos.Contains(iso);

        private static IEnumerable<int> CoupeRowIndexes(string[,] cars)
        {
            for (int i = 0; i < cars.GetLength(0); i++)
                if (cars[i, 1].Contains("coupe", StringComparison.OrdinalIgnoreCase))
                    yield return i;
        }

        private static string CoupeRows(string[,] cars) =>
            string.Join(Environment.NewLine, CoupeRowIndexes(cars).Select(i => cars[i, 1]));

        private static IEnumerable<string> CoupeIsoCodes(string[,] cars) => CoupeRowIndexes(cars).Select(i => cars[i, 0]);
    }

    public sealed record CoupeEcu(EcuOption Option, string Role);
}
