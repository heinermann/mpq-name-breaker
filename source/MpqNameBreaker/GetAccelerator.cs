using CommandLine;
using ILGPU;

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

            return 0;
        }
    }
}
