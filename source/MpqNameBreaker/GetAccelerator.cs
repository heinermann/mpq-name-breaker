using CommandLine;
using ILGPU;
using MpqNameBreaker.Mpq;

namespace MpqNameBreaker
{
    [Verb("Accelerator", HelpText = "Show supported accelerators.")]
    public class GetAcceleratorCommand
    {
        // This method will be called for each input received from the pipeline to this cmdlet; if no input is received, this method is not called
        public static int ProcessRecord(GetAcceleratorCommand opts)
        {
            var context = Context.Create(builder =>
            {
                builder.AllAccelerators();
            });

            // For each available accelerator...
            context.Devices.ToList().ForEach(Console.WriteLine);

            /* Use to create the static crypt table
            Console.WriteLine();
            uint n = 1;
            HashCalculator hashCalculator = new HashCalculator();
            foreach (uint hash in hashCalculator.CryptTable)
            {
                Console.Write("0x{0:X8}, ", hash);
                if (n % 16 == 0) Console.WriteLine();
                if (n % 256 == 0) Console.WriteLine();
                n++;
            }
            Console.WriteLine();
            */
            return 0;
        }
    }
}
