namespace Clawbot.Agents.Core.Skills.Nlp;

public sealed record IntentResult(string Label, float Confidence);

public interface IIntentClassifier : ISkill
{
    Task<IntentResult> ClassifyAsync(string text, string? locale, CancellationToken ct);
}

// Source: https://huggingface.co/vinai/phobert-base-v2
// Inference: HF Inference Endpoint OR ONNX local. Fine-tune dataset = local sale logs.
internal sealed class PhoBertIntentClassifier : IIntentClassifier
{
    public string Name => "intent-classification";

    public Task<IntentResult> ClassifyAsync(string text, string? locale, CancellationToken ct) =>
        throw new NotImplementedException("TODO: wire vinai/phobert-base-v2 via HF Inference Endpoint or ONNX.");
}
