using CommandLine;

namespace MpqNameBreaker
{
    static class MpqNameBreaker
    {
        static int Main(string[] args)
        {
#if DEBUG
            Console.WriteLine("Attach debugger and press any key to continue...");
            Console.ReadKey();
#endif
            return Parser.Default.ParseArguments<GetAcceleratorCommand, GetMpqStringHashCommand, InvokeMpqNameBreakingCommand, InvokeMpqNameBreakingNonAcceleratedCommand>(args)
                .MapResult(
                    (GetAcceleratorCommand opts) => GetAcceleratorCommand.ProcessRecord(opts),
                    (GetMpqStringHashCommand opts) => GetMpqStringHashCommand.ProcessRecord(opts),
                    (InvokeMpqNameBreakingCommand opts) => InvokeMpqNameBreakingCommand.ProcessRecord(opts),
                    (InvokeMpqNameBreakingNonAcceleratedCommand opts) => InvokeMpqNameBreakingNonAcceleratedCommand.ProcessRecord(opts),
                    errs => 1
                );
        }
    }
}
