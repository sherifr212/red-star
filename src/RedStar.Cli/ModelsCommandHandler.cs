using RedStar.Base;

namespace RedStar.Cli;

internal static class ModelsCommandHandler
{
    public static async Task<int> RunAsync(RedStarOptions options, CancellationToken cancellationToken)
    {
        try
        {
            using var modelsClient = new ModelsClient(options);
            var models = await modelsClient.ListAsync(cancellationToken);
            if (models.Count == 0)
            {
                Console.WriteLine("No models available. Load one in Unsloth Studio first.");
                return 0;
            }

            foreach (var model in models)
            {
                Console.WriteLine(model.Loaded ? $"* {model.Id} (loaded)" : $"  {model.Id}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error listing models: {ex.Message}");
            return 1;
        }
    }
}
