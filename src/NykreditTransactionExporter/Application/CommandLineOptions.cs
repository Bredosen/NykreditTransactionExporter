namespace NykreditTransactionExporter.Application;

internal sealed class CommandLineOptions
{
    #region Properties
    public string Command { get; private set; } = "help";

    public string? Month { get; private set; }

    public string? OutputPath { get; private set; }

    public string? AccountUid { get; private set; }

    public bool Once { get; private set; }
    #endregion

    #region Parse command line
    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();
        if (args.Length == 0)
        {
            return options;
        }

        options.Command = args[0].Trim().ToLowerInvariant();
        for (int index = 1; index < args.Length; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "--month":
                    options.Month = ReadValue(args, ref index, argument);
                    break;
                case "--output":
                    options.OutputPath = ReadValue(args, ref index, argument);
                    break;
                case "--account":
                    options.AccountUid = ReadValue(args, ref index, argument);
                    break;
                case "--once":
                    options.Once = true;
                    break;
                case "--help":
                case "-h":
                    options.Command = "help";
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {argument}");
            }
        }

        return options;
    }
    #endregion

    #region Read option value
    private static string ReadValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{optionName} requires a value.");
        }

        index++;
        return args[index];
    }
    #endregion
}
