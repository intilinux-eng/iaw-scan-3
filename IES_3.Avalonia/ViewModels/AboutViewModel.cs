using System;
using System.Reflection;

namespace IES_2.Avalonia.ViewModels
{
    /// <summary>
    /// About dialog content, replaces AboutBox1. Carries both this fork's own copyright and the
    /// original upstream project's (required by its BSD license), plus a link back to that
    /// original project - none of the two may be dropped in favour of the other.
    /// </summary>
    public class AboutViewModel
    {
        public string ProductName { get; }
        public string VersionText { get; }

        public string ForkCopyright { get; } = "Copyright © 2026 Progetto IAW Scan 3";
        public string ForkRepoUrl { get; } = "https://github.com/intilinux-eng/iaw-scan-3";

        public string OriginalProjectLine { get; } =
            "Basato su IAW Scan 2 v0.85 di Tomasz Orczyk (\"TzOk\") - la logica di comunicazione e decodifica delle centraline proviene invariata da quel progetto.";
        public string OriginalProjectUrl { get; } = "http://iaw-scan2.sourceforge.net";

        public string Description { get; }
        public string LicenseText { get; }

        public AboutViewModel()
        {
            var asm = Assembly.GetExecutingAssembly();
            ProductName = asm.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "IAW Scan 3";
            VersionText = "Versione " + (asm.GetName().Version?.ToString() ?? "3.0");

            Description =
                "Software di diagnosi per centraline motore Fiat/Lancia/Alfa Romeo IAW e sistema FIAT CODE." + Environment.NewLine + Environment.NewLine +
                "L'autore e i contributori non si assumono alcuna responsabilità per eventuali danni causati dall'uso di questo programma." + Environment.NewLine + Environment.NewLine +
                "Ringraziamenti (progetto originale): T5, Nailed Barnacle, Woj76, Yani, Pete, Gerrelt e tutti gli altri che hanno contribuito attivamente allo sviluppo e ai test di IAW Scan 2.";

            LicenseText =
                "Copyright (c) 2011-2015, Tomasz Orczyk (\"TzOk\")" + Environment.NewLine +
                "Tutti i diritti riservati." + Environment.NewLine + Environment.NewLine +
                "È consentita la ridistribuzione e l'uso in forma sorgente e binaria, con o senza modifiche, a condizione che siano rispettate le seguenti condizioni:" + Environment.NewLine +
                "  - le ridistribuzioni del codice sorgente devono mantenere il suddetto avviso di copyright, questo elenco di condizioni e la seguente esclusione di responsabilità;" + Environment.NewLine +
                "  - le ridistribuzioni in forma binaria devono riprodurre il suddetto avviso di copyright, questo elenco di condizioni e la seguente esclusione di responsabilità nella documentazione e/o in altri materiali forniti con la distribuzione;" + Environment.NewLine +
                "  - né il nome del titolare del copyright né i nomi dei suoi contributori possono essere usati per promuovere prodotti derivati da questo software senza specifica autorizzazione scritta preventiva." + Environment.NewLine + Environment.NewLine +
                "QUESTO SOFTWARE VIENE FORNITO DAI TITOLARI DEL COPYRIGHT E DAI CONTRIBUTORI \"COSÌ COM'È\" E SENZA GARANZIE DI ALCUN TIPO, ESPRESSE O IMPLICITE, IVI COMPRESE, MA NON A TITOLO ESAUSTIVO, LE GARANZIE IMPLICITE DI COMMERCIABILITÀ E IDONEITÀ PER UNO SCOPO PARTICOLARE.";
        }
    }
}
