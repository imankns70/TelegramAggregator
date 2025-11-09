namespace TelegramAggregator.Core
{
    public class PipelineRegistry
    {
        private readonly Dictionary<string, IEnumerable<IPipelineStep>> _pipelines = new();

        public void RegisterPipeline(string name, IEnumerable<IPipelineStep> steps)
        {
            _pipelines[name] = steps;
        }

        public IEnumerable<IPipelineStep> GetPipeline(string name)
        {
            if (!_pipelines.TryGetValue(name, out var steps))
                throw new KeyNotFoundException($"Pipeline '{name}' not found.");

            return steps;
        }
    }
}
