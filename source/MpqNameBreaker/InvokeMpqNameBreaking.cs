using MpqNameBreaker.NameGenerator;
using MpqNameBreaker.Mpq;
using CommandLine;

namespace MpqNameBreaker
{
    [Verb("MpqNameBreaking", HelpText = "Runs accelerated (GPU) namebreaking.")]
    public class InvokeMpqNameBreakingCommand
    {
        [Option(Required = true)]
        public string HashA { get; set; }

        [Option(Required = true)]
        public string HashB { get; set; }

        [Option(Default = "")]
        public string Prefix { get; set; } = "";

        [Option(Default = "")]
        public string Suffix { get; set; } = "";

        [Option(Default = "")]
        public string AdditionalChars { get; set; } = "";

        [Option(Default = BruteForceBatches.DefaultCharset)]
        public string Charset { get; set; } = BruteForceBatches.DefaultCharset;

        [Option(Default = -1)]
        public int BatchSize { get; set; }

        [Option(Default = -1)]
        public int BatchCharCount { get; set; }

        // Fields
        private static BruteForce _bruteForce;
        private static BruteForceBatches _bruteForceBatches;
        private static HashCalculator _hashCalculator;
        private static HashCalculatorAccelerated _hashCalculatorAccelerated;
        private static volatile bool nameFound = false;
        private static string resultName;

        private static void PrintDeviceInfo(HashCalculatorAccelerated hashCalc)
        {
            Console.WriteLine("Devices:");
            foreach (var device in hashCalc.GPUContext)
            {
                Console.WriteLine($"\t{device}");
            }
        }

        private static object logLock = new object();
        private static List<string> verboseLogBuffer = new List<string>();
        private static void WriteLogAsync(string text)
        {
            lock(logLock)
            {
                verboseLogBuffer.Add(text);
            }
        }

        private static object nameFoundLock = new object();
        private static void NameFoundAsync(string name)
        {
            lock (nameFoundLock)
            {
                resultName = name;
            }
            nameFound = true;
        }

        // This method will be called for each input received from the pipeline to this cmdlet; if no input is received, this method is not called
        public static int ProcessRecord(InvokeMpqNameBreakingCommand opts)
        {
            uint prefixSeed1A, prefixSeed2A, prefixSeed1B, prefixSeed2B;

            // Initialize brute force name generator
            _bruteForce = new BruteForce(opts.Prefix, opts.Suffix);
            _bruteForce.Initialize();

            // Initialize classic CPU hash calculator
            _hashCalculator = new HashCalculator();

            // Pre-calculate prefix seeds
            (prefixSeed1A, prefixSeed2A, prefixSeed1B, prefixSeed2B) = PreCalculatePrefixSeeds(opts.Prefix.Length);

            // Initialize accelerated hash calculator
            _hashCalculatorAccelerated = new HashCalculatorAccelerated();
            PrintDeviceInfo(_hashCalculatorAccelerated);

            // Define the batch size to MaxNumThreads of the accelerator if no custom value has been provided
            if (opts.BatchSize == -1)
            {
                opts.BatchSize = _hashCalculatorAccelerated.GetBestDevice().MaxNumThreads;
            }

            if (opts.BatchCharCount == -1)
            {
                if (_hashCalculatorAccelerated.GetBestDevice().MaxNumThreads < 1024)
                    opts.BatchCharCount = 3;
                else
                    opts.BatchCharCount = 4;
            }

            // Initialize brute force batches name generator
            _bruteForceBatches = new BruteForceBatches(opts.BatchSize, opts.BatchCharCount, opts.AdditionalChars, opts.Charset);

            _bruteForceBatches.Initialize();

            DateTime start = DateTime.Now;
            Console.WriteLine($"Start: {start:HH:mm:ss.fff}");

            var batches = new List<BatchJob>();

            foreach (var device in _hashCalculatorAccelerated.GPUContext)
            {
                // TODO: Temporary - only use the best device until we can utilize multiple devices with different batch sizes...
                if (device != _hashCalculatorAccelerated.GetBestDevice()) continue;

                var job = new BatchJob(_hashCalculatorAccelerated.GPUContext, device, _bruteForceBatches, _hashCalculatorAccelerated) {
                    Prefix = opts.Prefix,
                    Suffix = opts.Suffix,
                    HashA = Convert.ToUInt32(opts.HashA, 16),
                    HashB = Convert.ToUInt32(opts.HashB, 16),
                    PrefixSeed1A = prefixSeed1A,
                    PrefixSeed1B = prefixSeed1B,
                    PrefixSeed2A = prefixSeed2A,
                    PrefixSeed2B = prefixSeed2B
                };
                job.SetLoggerCallback(WriteLogAsync);
                job.SetNameFoundCallback(NameFoundAsync);
                batches.Add(job);
                job.Run();
            }

            while (!nameFound)
            {
                lock(logLock)
                {
                    foreach(string text in verboseLogBuffer)
                    {
                        Console.WriteLine(text);
                    }
                    verboseLogBuffer.Clear();
                }
                Thread.Sleep(100);
            }

            foreach(BatchJob job in batches)
            {
                job.Stop();
            }

            Console.WriteLine($"End: {DateTime.Now:HH:mm:ss.fff}");
            TimeSpan elapsed = DateTime.Now - start;
            Console.WriteLine($"Elapsed: {elapsed}");
            Console.WriteLine(resultName);

            return 0;
        }

        private static (uint, uint, uint, uint) PreCalculatePrefixSeeds(int prefixLength)
        {
            uint prefixSeed1A, prefixSeed2A, prefixSeed1B, prefixSeed2B;

            // Pre-calculate prefix seeds
            if (prefixLength > 0)
            {
                (prefixSeed1A, prefixSeed2A) = _hashCalculator.HashStringOptimizedCalculateSeeds(_bruteForce.PrefixBytes, HashType.MpqHashNameA);
                (prefixSeed1B, prefixSeed2B) = _hashCalculator.HashStringOptimizedCalculateSeeds(_bruteForce.PrefixBytes, HashType.MpqHashNameB);
            }
            else
            {
                prefixSeed1A = HashCalculatorAccelerated.HashSeed1;
                prefixSeed2A = HashCalculatorAccelerated.HashSeed2;
                prefixSeed1B = HashCalculatorAccelerated.HashSeed1;
                prefixSeed2B = HashCalculatorAccelerated.HashSeed2;
            }

            return (prefixSeed1A, prefixSeed2A, prefixSeed1B, prefixSeed2B);
        }
    }
}
