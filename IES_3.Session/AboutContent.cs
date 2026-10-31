namespace IES_2.Session
{
    /// <summary>
    /// Credits and license shown by every edition. Carries both this fork's own copyright and the
    /// original upstream project's (required by its BSD license), plus a link back to that original
    /// project: neither may be dropped in favour of the other.
    /// </summary>
    public sealed class AboutContent
    {
        public const string ForkRepoUrl = "https://github.com/intilinux-eng/iaw-scan-3";
        public const string OriginalProjectUrl = "http://iaw-scan2.sourceforge.net";

        public string ForkCopyright { get; private init; } = "";
        public string OriginalProjectLine { get; private init; } = "";
        public string Description { get; private init; } = "";
        public string LicenseText { get; private init; } = "";

        private const string EnglishLicense =
            "Copyright (c) 2011, Tomasz Orczyk\n" +
            "All rights reserved.\n\n" +
            "Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:\n" +
            "  * Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.\n" +
            "  * Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.\n" +
            "  * Neither the name of the copyright owner nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission.\n\n" +
            "THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS \"AS IS\" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. " +
            "IN NO EVENT SHALL COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) " +
            "HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.";

        private const string ItalianLicense =
            "Copyright (c) 2011, Tomasz Orczyk\n" +
            "Tutti i diritti riservati.\n\n" +
            "È consentita la ridistribuzione e l'uso in forma sorgente e binaria, con o senza modifiche, a condizione che siano rispettate le seguenti condizioni:\n" +
            "  * le ridistribuzioni del codice sorgente devono mantenere il suddetto avviso di copyright, questo elenco di condizioni e la seguente esclusione di responsabilità;\n" +
            "  * le ridistribuzioni in forma binaria devono riprodurre il suddetto avviso di copyright, questo elenco di condizioni e la seguente esclusione di responsabilità nella documentazione e/o in altri materiali forniti con la distribuzione;\n" +
            "  * né il nome del titolare del copyright né i nomi dei suoi contributori possono essere usati per promuovere prodotti derivati da questo software senza specifica autorizzazione scritta preventiva.\n\n" +
            "QUESTO SOFTWARE VIENE FORNITO DAI TITOLARI DEL COPYRIGHT E DAI CONTRIBUTORI \"COSÌ COM'È\" E SENZA GARANZIE DI ALCUN TIPO, ESPRESSE O IMPLICITE, IVI COMPRESE, MA NON A TITOLO ESAUSTIVO, LE GARANZIE IMPLICITE DI COMMERCIABILITÀ E IDONEITÀ PER UNO SCOPO PARTICOLARE. " +
            "IN NESSUN CASO IL TITOLARE DEL COPYRIGHT O I CONTRIBUTORI SARANNO RESPONSABILI PER DANNI DIRETTI, INDIRETTI, INCIDENTALI, SPECIALI, ESEMPLARI O CONSEQUENZIALI DERIVANTI IN QUALSIASI MODO DALL'USO DI QUESTO SOFTWARE, ANCHE SE AVVISATI DELLA POSSIBILITÀ DI TALI DANNI.\n\n" +
            "Fa fede il testo originale inglese (license.txt).";

        public static AboutContent For(string language) => language == "it" ? Italian : English;

        public static readonly AboutContent English = new()
        {
            ForkCopyright = "Copyright © 2026 IAW Scan 3 project",
            OriginalProjectLine = "Based on IAW Scan 2 v0.85 by Tomasz Orczyk (\"TzOk\"): the ECU communication and decoding logic comes unchanged from that project.",
            Description =
                "Diagnostic software for Fiat/Lancia/Alfa Romeo IAW engine control units and the FIAT CODE system.\n\n" +
                "The author and contributors accept no liability for any damage caused by the use of this program.\n\n" +
                "Thanks (original project): T5, Nailed Barnacle, Woj76, Yani, Pete, Gerrelt and everyone else who actively contributed to the development and testing of IAW Scan 2.",
            LicenseText = EnglishLicense,
        };

        public static readonly AboutContent Italian = new()
        {
            ForkCopyright = "Copyright © 2026 Progetto IAW Scan 3",
            OriginalProjectLine = "Basato su IAW Scan 2 v0.85 di Tomasz Orczyk (\"TzOk\"): la logica di comunicazione e decodifica delle centraline proviene invariata da quel progetto.",
            Description =
                "Software di diagnosi per centraline motore Fiat/Lancia/Alfa Romeo IAW e sistema FIAT CODE.\n\n" +
                "L'autore e i contributori non si assumono alcuna responsabilità per eventuali danni causati dall'uso di questo programma.\n\n" +
                "Ringraziamenti (progetto originale): T5, Nailed Barnacle, Woj76, Yani, Pete, Gerrelt e tutti gli altri che hanno contribuito attivamente allo sviluppo e ai test di IAW Scan 2.",
            LicenseText = ItalianLicense,
        };
    }
}
