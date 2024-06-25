using CommandLine;
using MpqNameBreaker.Mpq;
using System.Text;

namespace MpqNameBreaker
{
    [Verb("hash", HelpText = "Gets the MPQ hash associated with a name.")]
    public class GetMpqStringHashCommand
    {
        [Value(0, HelpText = "Name to hash.", Required = true)]
        public string String { get; set; }

        public static int ProcessRecord(GetMpqStringHashCommand opts)
        {
            // Initialize hash calculator
            HashCalculator hashCalculator = new HashCalculator();

            // Get ASCII chars
            var strBytes = Encoding.ASCII.GetBytes(opts.String.ToUpper());

            // Hash string
            var hashA = hashCalculator.HashString(strBytes, HashType.MpqHashNameA);
            var hashB = hashCalculator.HashString(strBytes, HashType.MpqHashNameB);

            // Output hash to console
            Console.WriteLine($"{hashA:X8}");
            Console.WriteLine($"{hashB:X8}");

            return 0;
        }
    }
}
