using System;
using System.Text;
using MpqNameBreaker.Mpq;
using CommandLine;

namespace MpqNameBreaker
{
    [Verb("MpqStringHash", HelpText = "Gets the MPQ hash associated with a name.")]
    public class GetMpqStringHashCommand
    {
        [Value(0, HelpText = "Name to hash.", Required = true)]
        public string String { get; set; }

        [Value(1, HelpText = "Hash type to use, either MpqHashNameA or MpqHashNameB.", Required = true)]
        public HashType Type { get; set; }

        public static int ProcessRecord(GetMpqStringHashCommand opts)
        {
            string strUpper;
            byte[] strBytes;
            uint hash;

            // Initialize hash calculator
            HashCalculator hashCalculator = new HashCalculator();

            // Convert string to uppercase
            strUpper = opts.String.ToUpper();
            // Get ASCII chars
            strBytes = Encoding.ASCII.GetBytes(strUpper);
            // Compute hash
            hash = hashCalculator.HashString(strBytes, opts.Type);

            // Output hash to console
            Console.WriteLine(hash);

            return 0;
        }
    }
}
