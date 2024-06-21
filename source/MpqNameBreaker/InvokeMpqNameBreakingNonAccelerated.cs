using System;
using MpqNameBreaker.NameGenerator;
using MpqNameBreaker.Mpq;
using CommandLine;

namespace MpqNameBreaker
{
    [Verb("MpqNameBreakingNonAccelerated", HelpText = "Runs non-accelerated (CPU) namebreaking.")]
    public class InvokeMpqNameBreakingNonAcceleratedCommand
    {
        [Option(Required = true)]
        public string HashA { get; set; }

        [Option(Required = true)]
        public string HashB { get; set; }

        [Option(Default = "")]
        public string Prefix { get; set; } = "";

        [Option(Default = "")]
        public string Suffix { get; set; } = "";

        // Fields
        private static BruteForce _bruteForce;
        private static HashCalculator _hashCalculator;

        // This method will be called for each input received from the pipeline to this cmdlet; if no input is received, this method is not called
        public static int ProcessRecord(InvokeMpqNameBreakingNonAcceleratedCommand opts)
        {
            uint prefixSeed1A, prefixSeed2A, prefixSeed1B, prefixSeed2B, currentHashA, currentHashB;
            DateTime start = DateTime.Now;

            // Initialize brute force name generator
            _bruteForce = new BruteForce(opts.Prefix, opts.Suffix);
            _bruteForce.Initialize();

            // Initialize hash calculator
            _hashCalculator = new HashCalculator();
            // Prepare prefix seeds to speed up calculation
            (prefixSeed1A, prefixSeed2A) = _hashCalculator.HashStringOptimizedCalculateSeeds(_bruteForce.PrefixBytes, HashType.MpqHashNameA);
            (prefixSeed1B, prefixSeed2B) = _hashCalculator.HashStringOptimizedCalculateSeeds(_bruteForce.PrefixBytes, HashType.MpqHashNameB);

            Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff"));

            uint hashValueA = Convert.ToUInt32(opts.HashA, 16);
            uint hashValueB = Convert.ToUInt32(opts.HashB, 16);

            long count = 0;
            // 38^8 = 4_347_792_138_496
            while (_bruteForce.NextName() && count < 4_347_792_138_496)
            {
                //currentHash = _hashCalculator.HashString( _bruteForce.NameBytes, Type );
                currentHashA = _hashCalculator.HashStringOptimized(_bruteForce.NameBytes, HashType.MpqHashNameA, _bruteForce.Prefix.Length, prefixSeed1A, prefixSeed2A);

                if (hashValueA == currentHashA)
                {
                    currentHashB = _hashCalculator.HashStringOptimized(_bruteForce.NameBytes, HashType.MpqHashNameB, _bruteForce.Prefix.Length, prefixSeed1B, prefixSeed2B);

                    // Detect collisions
                    if (hashValueB == currentHashB)
                    {
                        Console.WriteLine("Name found: " + _bruteForce.Name);
                        Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff"));
                        break;
                    }
                }

                if (count % 1_000_000_000 == 0)
                {
                    TimeSpan elapsed = DateTime.Now - start;
                    Console.WriteLine(string.Format("Time: {0} - Name: {1} - Count : {2:N0} billion", elapsed.ToString(), _bruteForce.Name, count / 1_000_000_000));
                }

                count++;
            }

            return 0;
        }
    }
}
