using ILGPU;
using ILGPU.Runtime;
using MpqNameBreaker.Mpq;
using MpqNameBreaker.NameGenerator;
using System.Text;

namespace MpqNameBreaker
{
    /**
     * Class to manage separate/multi accelerator processing threads.
     */
    public class BatchJob
    {
        // Properties
        public Context Context { get; }
        public Accelerator Accelerator { get; }

        public BruteForceBatches Batches { get; }
        public HashCalculatorAccelerated HashCalc { get; }

        public int BatchSize { get; }
        public int BatchCharCount { get; }

        public string Prefix { get; set; }
        public string Suffix { get; set; }
        public string Before { get; set; }
        public string After { get; set; }

        public uint HashA { get; set; }
        public uint HashB { get; set; }
        public uint PrefixSeed1A { get; set; }
        public uint PrefixSeed2A { get; set; }
        public uint PrefixSeed1B { get; set; }
        public uint PrefixSeed2B { get; set; }

        private volatile bool endThread = false;

        // Members
        private Thread thread;
        private Action<string> logFn = (str) => { };
        private Action<string> nameFoundFn = (str) => { };
        private double billionCount = 0;
        private DateTime startTime = DateTime.Now;

        public BatchJob(Context context, Device device, BruteForceBatches batches, HashCalculatorAccelerated hashCalc)
        {
            Context = context;
            Accelerator = device.CreateAccelerator(context);
            Batches = batches;
            HashCalc = hashCalc;

            thread = new Thread(() => JobThread());

            BatchSize = Accelerator.MaxNumThreads;
            if (Accelerator.MaxNumThreads < 1024)
            {
                BatchCharCount = 3;
            }
            else if (Accelerator.MaxNumThreads < 1024 * 32)
            {
                BatchCharCount = 4;
            }
            else
            {
                BatchCharCount = 5;
            }
        }

        public void SetLoggerCallback(Action<string> logger)
        {
            logFn = logger;
        }

        public void SetNameFoundCallback(Action<string> nameFound)
        {
            nameFoundFn = nameFound;
        }

        private void Log(string str)
        {
            // TODO make sure this is threadsafe
            logFn($"[{Accelerator.Name}] {str}");
        }

        private void NameFound(int[] nameData)
        {
            string foundName = "";

            byte[] str = new byte[BruteForceBatches.MaxGeneratedChars];

            for (int i = 0; i < BruteForceBatches.MaxGeneratedChars; ++i)
            {
                int idx = nameData[i];

                if (idx == -1)
                    break;

                foundName += Convert.ToChar(Batches.CharsetBytes[idx]);
            }

            Log("Name found!");
            nameFoundFn(Prefix.ToUpper() + foundName + Suffix.ToUpper());
        }

        private void RunBatchStatistics(BruteForceBatches.Batch batch)
        {
            double oneBatchBillionCount = (Math.Pow(Batches.Charset.Length, BatchCharCount) * BatchSize) / 1_000_000_000;

            billionCount += oneBatchBillionCount;

            TimeSpan elapsed = DateTime.Now - startTime;

            string lastName = "";
            for (int i = 0; i < BruteForceBatches.MaxGeneratedChars; i++)
            {
                int idx = batch.BatchNameSeedCharsetIndexes[BatchSize - 1, i];

                if (idx == -1)
                    break;

                lastName += Convert.ToChar(Batches.CharsetBytes[idx]);
            }

            Log($"Elapsed time: {elapsed} - Name: {Prefix.ToUpper() + lastName + Suffix.ToUpper()} - Name count: {billionCount:N0} billion");
        }

        public void Run()
        {
            startTime = DateTime.Now;
            thread.Start();
        }

        public void Stop()
        {
            endThread = true;
        }

        public bool IsRunning()
        {
            return thread.IsAlive;
        }

        static private byte[] GetStringBytes(string str)
        {
            byte[] result;
            if (str.Length > 0)
            {
                result = Encoding.ASCII.GetBytes(str.ToUpper());
            }
            else
            {
                result = new byte[1];
                result[0] = 0x00;
            }
            return result;
        }

        private int[] GetStringIndicesBefore(string str)
        {
            int[] result;
            if (str.Length > 0)
            {
                result = Encoding.ASCII.GetBytes(str.ToUpper())
                    .Select(b => {
                        int result = Array.FindIndex(Batches.CharsetBytes, 0, chr => chr >= b);
                        if (result == -1) result = Batches.CharsetBytes.Length - 1;
                        return result;
                    })
                    .ToArray();
            }
            else
            {
                result = new int[1];
                result[0] = -1;
            }
            return result;
        }

        private int[] GetStringIndicesAfter(string str)
        {
            int[] result;
            if (str.Length > 0)
            {
                result = Encoding.ASCII.GetBytes(str.ToUpper())
                    .Select(b => {
                        int result = Array.FindLastIndex(Batches.CharsetBytes, 0, chr => chr <= b);
                        if (result == -1) result = 0;
                        return result;
                    })
                    .ToArray();
            }
            else
            {
                result = new int[1];
                result[0] = -1;
            }
            return result;
        }

        private void JobThread()
        {
            // Load kernel (GPU function)
            // This function will calculate HashA for each name of a batch and report matches/collisions
            var kernel = Accelerator.LoadAutoGroupedStreamKernel<
                    Index1D,
                    ArrayView<byte>,                            // 1D array holding the charset bytes
                    ArrayView2D<int, Stride2D.DenseX>,          // 2D array containing the char indexes of one batch string seed (one string per line, hashes will be computed starting from this string)
                    ArrayView<byte>,                            // 1D array holding the indexes of the suffix chars
                    SpecializedValue<AccelConstants>,           // Values that can be optimized
                    SpecializedValue<int>,                      // boolean
                    int,                                        // Name count limit (used as return condition)
                    ArrayView<int>,                             // 1D array containing the found name (if found)
                    ArrayView<uint>,
                    ArrayView<uint>,
                    ArrayView<int>,                             // Hint name that comes before this (for bounding)
                    ArrayView<int>                              // Hint name that comes after this (for bounding)
                >(HashCalculatorAccelerated.HashStringsBatchOptimized);

            var charsetBuffer = Accelerator.Allocate1D(Batches.CharsetBytes);
            var charsetIndexesBuffer = Accelerator.Allocate2DDenseX<int>(new LongIndex2D(BatchSize, BruteForceBatches.MaxGeneratedChars));
            int nameCount = (int)Math.Pow(Batches.Charset.Length, BatchCharCount);
            var cryptTableA = Accelerator.Allocate1D(StaticCryptTable.CryptTableA);
            var cryptTableB = Accelerator.Allocate1D(StaticCryptTable.CryptTableB);

            var suffixBytes = GetStringBytes(Suffix);

            var suffixBytesBuffer = Accelerator.Allocate1D(suffixBytes);
            var beforeIndicesBuffer = Accelerator.Allocate1D(GetStringIndicesBefore(Before));
            var afterIndicesBuffer = Accelerator.Allocate1D(GetStringIndicesAfter(After));

            // fill result array with -1
            int[] foundNameCharsetIndexes = new int[BruteForceBatches.MaxGeneratedChars];
            for (int i = 0; i < BruteForceBatches.MaxGeneratedChars; ++i)
            {
                foundNameCharsetIndexes[i] = -1;
            }

            var foundNameCharsetIndexesBuffer = Accelerator.Allocate1D(foundNameCharsetIndexes);

            var optimizableConstants = new AccelConstants();
            optimizableConstants.hashALookup = HashA;
            optimizableConstants.hashBLookup = HashB;
            optimizableConstants.prefixSeed1a = PrefixSeed1A;
            optimizableConstants.prefixSeed2a = PrefixSeed2A;
            optimizableConstants.prefixSeed1b = PrefixSeed1B;
            optimizableConstants.prefixSeed2b = PrefixSeed2B;
            optimizableConstants.batchCharCount = BatchCharCount;
            optimizableConstants.suffixbytes = Suffix.Length;
            optimizableConstants.beforebytes = Before.Length;
            optimizableConstants.afterbytes = After.Length;
            optimizableConstants.charsetLength = Batches.CharsetBytes.Length;

            if (optimizableConstants.suffixbytes > 0)
            {
                optimizableConstants.hashALookup ^= StaticCryptTable.CryptTableA[suffixBytes.Last()];
                optimizableConstants.hashBLookup ^= StaticCryptTable.CryptTableB[suffixBytes.Last()];
            }

            // MAIN
            Log($"Accelerator: {Accelerator.Name} (threads: {Accelerator.MaxNumThreads})");

            BruteForceBatches.Batch batch;
            while ((batch = Batches.NextBatch()) != null && !endThread)
            {
                charsetIndexesBuffer.CopyFromCPU(batch.BatchNameSeedCharsetIndexes);

                // Call the kernel
                kernel((int)charsetIndexesBuffer.Extent.X,
                       charsetBuffer.View,
                       charsetIndexesBuffer.View,
                       suffixBytesBuffer.View,
                       SpecializedValue.New(optimizableConstants),
                       SpecializedValue.New(batch.FirstBatch ? 1 : 0),
                       nameCount,
                       foundNameCharsetIndexesBuffer.View,
                       cryptTableA.View,
                       cryptTableB.View,
                       beforeIndicesBuffer.View,
                       afterIndicesBuffer.View);

                // Wait for the kernel to complete
                Accelerator.Synchronize();

                if (endThread) return;

                // If name was found
                foundNameCharsetIndexes = foundNameCharsetIndexesBuffer.GetAsArray1D();

                if (foundNameCharsetIndexes[0] != -1)
                {
                    NameFound(foundNameCharsetIndexes);
                    return;
                }
                RunBatchStatistics(batch);
            }
        }
    }
}
