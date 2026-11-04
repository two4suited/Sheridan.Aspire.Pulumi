namespace Sheridan.Aspire.Pulumi.Azure.Foundry;

/// <summary>
/// Describes a model that can be deployed to Microsoft Foundry (the catalog of the official <c>Aspire.Hosting.Foundry.FoundryModel</c>,
/// without the Foundry Local-only models). Pass one to <c>AddDeployment(name, FoundryModel)</c>.
/// </summary>
public sealed class FoundryModel
{
    /// <summary>The model name.</summary>
    public required string Name { get; init; }
    /// <summary>The model version.</summary>
    public required string Version { get; init; }
    /// <summary>The model format / publisher (e.g. <c>OpenAI</c>).</summary>
    public required string Format { get; init; }

    /// <summary>Anthropic models.</summary>
    public static class Anthropic
    {
        public static readonly FoundryModel ClaudeFable5 = new() { Name = "claude-fable-5", Version = "1", Format = "Anthropic" };
        public static readonly FoundryModel ClaudeFable51 = new() { Name = "claude-fable-5-1", Version = "1", Format = "Anthropic" };
        public static readonly FoundryModel ClaudeHaiku45 = new() { Name = "claude-haiku-4-5", Version = "2", Format = "Anthropic" };
        public static readonly FoundryModel ClaudeMythos5 = new() { Name = "claude-mythos-5", Version = "1", Format = "Anthropic" };
        public static readonly FoundryModel ClaudeMythos51 = new() { Name = "claude-mythos-5-1", Version = "1", Format = "Anthropic" };
        public static readonly FoundryModel ClaudeMythosPreview = new() { Name = "claude-mythos-preview", Version = "1", Format = "Anthropic" };
        public static readonly FoundryModel ClaudeOpus45 = new() { Name = "claude-opus-4-5", Version = "20251101", Format = "Anthropic" };
        public static readonly FoundryModel ClaudeOpus46 = new() { Name = "claude-opus-4-6", Version = "1", Format = "Anthropic" };
        public static readonly FoundryModel ClaudeOpus47 = new() { Name = "claude-opus-4-7", Version = "1", Format = "Anthropic" };
        public static readonly FoundryModel ClaudeOpus48 = new() { Name = "claude-opus-4-8", Version = "2", Format = "Anthropic" };
        public static readonly FoundryModel ClaudeOpus5 = new() { Name = "claude-opus-5", Version = "2", Format = "Anthropic" };
        public static readonly FoundryModel ClaudeSonnet45 = new() { Name = "claude-sonnet-4-5", Version = "20250929", Format = "Anthropic" };
        public static readonly FoundryModel ClaudeSonnet46 = new() { Name = "claude-sonnet-4-6", Version = "1", Format = "Anthropic" };
        public static readonly FoundryModel ClaudeSonnet5 = new() { Name = "claude-sonnet-5", Version = "2", Format = "Anthropic" };
    }

    /// <summary>BlackForestLabs models.</summary>
    public static class BlackForestLabs
    {
        public static readonly FoundryModel Flux11Pro = new() { Name = "FLUX-1.1-pro", Version = "1", Format = "Black Forest Labs" };
        public static readonly FoundryModel Flux1KontextPro = new() { Name = "FLUX.1-Kontext-pro", Version = "1", Format = "Black Forest Labs" };
        public static readonly FoundryModel Flux2Flex = new() { Name = "FLUX.2-flex", Version = "1", Format = "Black Forest Labs" };
        public static readonly FoundryModel Flux2Pro = new() { Name = "FLUX.2-pro", Version = "1", Format = "Black Forest Labs" };
    }

    /// <summary>Cohere models.</summary>
    public static class Cohere
    {
        public static readonly FoundryModel CohereCommandA = new() { Name = "cohere-command-a", Version = "4", Format = "Cohere" };
        public static readonly FoundryModel CohereCommandAPlus052026 = new() { Name = "Cohere-command-a-plus-05-2026", Version = "1", Format = "Cohere" };
        public static readonly FoundryModel CohereEmbedV3English = new() { Name = "Cohere-embed-v3-english", Version = "1", Format = "Cohere" };
        public static readonly FoundryModel CohereEmbedV3Multilingual = new() { Name = "Cohere-embed-v3-multilingual", Version = "1", Format = "Cohere" };
        public static readonly FoundryModel CohereParseV5 = new() { Name = "Cohere-parse-v5", Version = "1", Format = "Cohere" };
        public static readonly FoundryModel CohereRerankV40Fast = new() { Name = "Cohere-rerank-v4.0-fast", Version = "2", Format = "Cohere" };
        public static readonly FoundryModel CohereRerankV40Pro = new() { Name = "Cohere-rerank-v4.0-pro", Version = "1", Format = "Cohere" };
        public static readonly FoundryModel EmbedV40 = new() { Name = "embed-v-4-0", Version = "6", Format = "Cohere" };
    }

    /// <summary>DeepSeek models.</summary>
    public static class DeepSeek
    {
        public static readonly FoundryModel DeepSeekV32 = new() { Name = "DeepSeek-V3.2", Version = "1", Format = "DeepSeek" };
        public static readonly FoundryModel DeepSeekV32Speciale = new() { Name = "DeepSeek-V3.2-Speciale", Version = "1", Format = "DeepSeek" };
        public static readonly FoundryModel DeepSeekV4Flash = new() { Name = "DeepSeek-V4-Flash", Version = "2026-04-23", Format = "DeepSeek" };
        public static readonly FoundryModel DeepSeekV4Flash0731 = new() { Name = "DeepSeek-V4-Flash-0731", Version = "2026-07-31", Format = "DeepSeek" };
        public static readonly FoundryModel DeepSeekV4Pro = new() { Name = "DeepSeek-V4-Pro", Version = "2026-04-23", Format = "DeepSeek" };
    }

    /// <summary>Meta models.</summary>
    public static class Meta
    {
        public static readonly FoundryModel Llama3211BVisionInstruct = new() { Name = "Llama-3.2-11B-Vision-Instruct", Version = "6", Format = "Meta" };
        public static readonly FoundryModel Llama3290BVisionInstruct = new() { Name = "Llama-3.2-90B-Vision-Instruct", Version = "5", Format = "Meta" };
        public static readonly FoundryModel Llama3370BInstruct = new() { Name = "Llama-3.3-70B-Instruct", Version = "10", Format = "Meta" };
        public static readonly FoundryModel Llama4Maverick17B128EInstructFP8 = new() { Name = "Llama-4-Maverick-17B-128E-Instruct-FP8", Version = "5", Format = "Meta" };
        public static readonly FoundryModel Llama4Scout17B16EInstruct = new() { Name = "Llama-4-Scout-17B-16E-Instruct", Version = "4", Format = "Meta" };
        public static readonly FoundryModel MetaLlama31405BInstruct = new() { Name = "Meta-Llama-3.1-405B-Instruct", Version = "1", Format = "Meta" };
        public static readonly FoundryModel MetaLlama318BInstruct = new() { Name = "Meta-Llama-3.1-8B-Instruct", Version = "6", Format = "Meta" };
        public static readonly FoundryModel MuseSpark = new() { Name = "Muse-Spark", Version = "1", Format = "Meta" };
        public static readonly FoundryModel MuseSpark13 = new() { Name = "Muse-Spark-1.3", Version = "1", Format = "Meta" };
    }

    /// <summary>Microsoft models.</summary>
    public static class Microsoft
    {
        public static readonly FoundryModel AzureAIContentSafety = new() { Name = "Azure-AI-Content-Safety", Version = "1", Format = "Microsoft" };
        public static readonly FoundryModel AzureAIContentUnderstanding = new() { Name = "Azure-AI-Content-Understanding", Version = "1", Format = "Microsoft" };
        public static readonly FoundryModel AzureAIDocumentIntelligence = new() { Name = "Azure-AI-Document-Intelligence", Version = "1", Format = "Microsoft" };
        public static readonly FoundryModel AzureAIVision = new() { Name = "Azure-AI-Vision", Version = "1", Format = "Microsoft" };
        public static readonly FoundryModel AzureContentUnderstandingLayout = new() { Name = "Azure-Content-Understanding-Layout", Version = "1", Format = "Microsoft" };
        public static readonly FoundryModel AzureContentUnderstandingRead = new() { Name = "Azure-Content-Understanding-Read", Version = "1", Format = "Microsoft" };
        public static readonly FoundryModel AzureLanguageConversationalPiiRedaction = new() { Name = "Azure-Language-Conversational-PII-redaction", Version = "1", Format = "Microsoft" };
        public static readonly FoundryModel AzureLanguageDocumentPiiRedaction = new() { Name = "Azure-Language-Document-PII-redaction", Version = "1", Format = "Microsoft" };
        public static readonly FoundryModel AzureLanguageLanguageDetection = new() { Name = "Azure-Language-Language-detection", Version = "1", Format = "Microsoft" };
        public static readonly FoundryModel AzureLanguageTextAnalyticsForHealth = new() { Name = "Azure-Language-Text-Analytics-for-Health", Version = "1", Format = "Microsoft" };
        public static readonly FoundryModel AzureLanguageTextPiiRedaction = new() { Name = "Azure-Language-Text-PII-redaction", Version = "1", Format = "Microsoft" };
        public static readonly FoundryModel AzureSpeechSpeechToText = new() { Name = "Azure-Speech-Speech-to-text", Version = "1", Format = "Microsoft" };
        public static readonly FoundryModel AzureSpeechSpeechTranslation = new() { Name = "Azure-Speech-Speech-Translation", Version = "1", Format = "Microsoft" };
        public static readonly FoundryModel AzureSpeechTextToSpeech = new() { Name = "Azure-Speech-Text-to-speech", Version = "1", Format = "Microsoft" };
        public static readonly FoundryModel AzureSpeechTextToSpeechAvatar = new() { Name = "Azure-Speech-Text-to-speech-Avatar", Version = "1", Format = "Microsoft" };
        public static readonly FoundryModel AzureSpeechVoiceLive = new() { Name = "Azure-Speech-Voice-Live", Version = "1", Format = "Microsoft" };
        public static readonly FoundryModel AzureTranslatorDocumentTranslation = new() { Name = "Azure-Translator-Document-translation", Version = "1", Format = "Microsoft" };
        public static readonly FoundryModel AzureTranslatorTextTranslation = new() { Name = "Azure-Translator-Text-translation", Version = "1", Format = "Microsoft" };
        public static readonly FoundryModel MaiTranscribe1 = new() { Name = "MAI-Transcribe-1", Version = "2026-01-23", Format = "Microsoft" };
        public static readonly FoundryModel MaiTranscribe15 = new() { Name = "MAI-Transcribe-1.5", Version = "2026-06-02", Format = "Microsoft" };
        public static readonly FoundryModel MaiTranscribe2 = new() { Name = "MAI-Transcribe-2", Version = "2026-09-03", Format = "Microsoft" };
        public static readonly FoundryModel MaiVoice1 = new() { Name = "MAI-Voice-1", Version = "2025-12-18", Format = "Microsoft" };
        public static readonly FoundryModel MaiVoice2 = new() { Name = "MAI-Voice-2", Version = "2026-06-02", Format = "Microsoft" };
        public static readonly FoundryModel MaiVoice2Flash = new() { Name = "MAI-Voice-2-Flash", Version = "2026-07-22", Format = "Microsoft" };
        public static readonly FoundryModel ModelRouter = new() { Name = "model-router", Version = "2025-11-18", Format = "Microsoft" };
        public static readonly FoundryModel Phi3Small8kInstruct = new() { Name = "Phi-3-small-8k-instruct", Version = "6", Format = "Microsoft" };
        public static readonly FoundryModel Phi3Vision128kInstruct = new() { Name = "Phi-3-vision-128k-instruct", Version = "2", Format = "Microsoft" };
        public static readonly FoundryModel Phi4 = new() { Name = "Phi-4", Version = "7", Format = "Microsoft" };
        public static readonly FoundryModel Phi4MiniInstruct = new() { Name = "Phi-4-mini-instruct", Version = "1", Format = "Microsoft" };
        public static readonly FoundryModel Phi4MiniReasoning = new() { Name = "Phi-4-mini-reasoning", Version = "1", Format = "Microsoft" };
        public static readonly FoundryModel Phi4MultimodalInstruct = new() { Name = "Phi-4-multimodal-instruct", Version = "2", Format = "Microsoft" };
        public static readonly FoundryModel Phi4Reasoning = new() { Name = "Phi-4-reasoning", Version = "1", Format = "Microsoft" };
    }

    /// <summary>MistralAI models.</summary>
    public static class MistralAI
    {
        public static readonly FoundryModel Codestral2501 = new() { Name = "Codestral-2501", Version = "2", Format = "Mistral AI" };
        public static readonly FoundryModel Ministral3B = new() { Name = "Ministral-3B", Version = "1", Format = "Mistral AI" };
        public static readonly FoundryModel MistralDocumentAi2512 = new() { Name = "mistral-document-ai-2512", Version = "1", Format = "Mistral AI" };
        public static readonly FoundryModel MistralLarge3 = new() { Name = "Mistral-Large-3", Version = "1", Format = "Mistral AI" };
        public static readonly FoundryModel MistralMedium2505 = new() { Name = "mistral-medium-2505", Version = "1", Format = "Mistral AI" };
        public static readonly FoundryModel MistralMedium35 = new() { Name = "mistral-medium-3-5", Version = "1", Format = "Mistral AI" };
        public static readonly FoundryModel MistralOcr40 = new() { Name = "mistral-ocr-4-0", Version = "1", Format = "Mistral AI" };
        public static readonly FoundryModel MistralSmall2503 = new() { Name = "mistral-small-2503", Version = "1", Format = "Mistral AI" };
    }

    /// <summary>OpenAI models.</summary>
    public static class OpenAI
    {
        public static readonly FoundryModel CodexMini = new() { Name = "codex-mini", Version = "2025-05-16", Format = "OpenAI" };
        public static readonly FoundryModel ComputerUsePreview = new() { Name = "computer-use-preview", Version = "2025-03-11", Format = "OpenAI" };
        public static readonly FoundryModel Gpt41 = new() { Name = "gpt-4.1", Version = "2025-04-14", Format = "OpenAI" };
        public static readonly FoundryModel Gpt41Mini = new() { Name = "gpt-4.1-mini", Version = "2025-04-14", Format = "OpenAI" };
        public static readonly FoundryModel Gpt41Nano = new() { Name = "gpt-4.1-nano", Version = "2025-04-14", Format = "OpenAI" };
        public static readonly FoundryModel Gpt4o = new() { Name = "gpt-4o", Version = "2024-11-20", Format = "OpenAI" };
        public static readonly FoundryModel Gpt4oMini = new() { Name = "gpt-4o-mini", Version = "2024-07-18", Format = "OpenAI" };
        public static readonly FoundryModel Gpt4oMiniTranscribe = new() { Name = "gpt-4o-mini-transcribe", Version = "2025-12-15", Format = "OpenAI" };
        public static readonly FoundryModel Gpt4oMiniTts = new() { Name = "gpt-4o-mini-tts", Version = "2025-12-15", Format = "OpenAI" };
        public static readonly FoundryModel Gpt4oTranscribe = new() { Name = "gpt-4o-transcribe", Version = "2025-03-20", Format = "OpenAI" };
        public static readonly FoundryModel Gpt4oTranscribeDiarize = new() { Name = "gpt-4o-transcribe-diarize", Version = "2025-10-15", Format = "OpenAI" };
        public static readonly FoundryModel Gpt5 = new() { Name = "gpt-5", Version = "2025-08-07", Format = "OpenAI" };
        public static readonly FoundryModel Gpt5Codex = new() { Name = "gpt-5-codex", Version = "2025-09-15", Format = "OpenAI" };
        public static readonly FoundryModel Gpt5Mini = new() { Name = "gpt-5-mini", Version = "2025-08-07", Format = "OpenAI" };
        public static readonly FoundryModel Gpt5Nano = new() { Name = "gpt-5-nano", Version = "2025-08-07", Format = "OpenAI" };
        public static readonly FoundryModel Gpt5Pro = new() { Name = "gpt-5-pro", Version = "2025-10-06", Format = "OpenAI" };
        public static readonly FoundryModel Gpt51 = new() { Name = "gpt-5.1", Version = "2025-11-13", Format = "OpenAI" };
        public static readonly FoundryModel Gpt51Codex = new() { Name = "gpt-5.1-codex", Version = "2025-11-13", Format = "OpenAI" };
        public static readonly FoundryModel Gpt51CodexMax = new() { Name = "gpt-5.1-codex-max", Version = "2025-12-04", Format = "OpenAI" };
        public static readonly FoundryModel Gpt51CodexMini = new() { Name = "gpt-5.1-codex-mini", Version = "2025-11-13", Format = "OpenAI" };
        public static readonly FoundryModel Gpt52 = new() { Name = "gpt-5.2", Version = "2025-12-11", Format = "OpenAI" };
        public static readonly FoundryModel Gpt52Codex = new() { Name = "gpt-5.2-codex", Version = "2026-01-14", Format = "OpenAI" };
        public static readonly FoundryModel Gpt53Codex = new() { Name = "gpt-5.3-codex", Version = "2026-02-24", Format = "OpenAI" };
        public static readonly FoundryModel Gpt54 = new() { Name = "gpt-5.4", Version = "2026-03-05", Format = "OpenAI" };
        public static readonly FoundryModel Gpt54Mini = new() { Name = "gpt-5.4-mini", Version = "2026-03-17", Format = "OpenAI" };
        public static readonly FoundryModel Gpt54Nano = new() { Name = "gpt-5.4-nano", Version = "2026-03-17", Format = "OpenAI" };
        public static readonly FoundryModel Gpt54Pro = new() { Name = "gpt-5.4-pro", Version = "2026-03-05", Format = "OpenAI" };
        public static readonly FoundryModel Gpt55 = new() { Name = "gpt-5.5", Version = "2026-04-24", Format = "OpenAI" };
        public static readonly FoundryModel Gpt56Luna = new() { Name = "gpt-5.6-luna", Version = "2026-07-09", Format = "OpenAI" };
        public static readonly FoundryModel Gpt56Sol = new() { Name = "gpt-5.6-sol", Version = "2026-07-09", Format = "OpenAI" };
        public static readonly FoundryModel Gpt56Terra = new() { Name = "gpt-5.6-terra", Version = "2026-07-09", Format = "OpenAI" };
        public static readonly FoundryModel Gpt6Astra = new() { Name = "gpt-6-astra", Version = "2026-09-03", Format = "OpenAI" };
        public static readonly FoundryModel GptAudio = new() { Name = "gpt-audio", Version = "2025-08-28", Format = "OpenAI" };
        public static readonly FoundryModel GptAudio15 = new() { Name = "gpt-audio-1.5", Version = "2026-02-23", Format = "OpenAI" };
        public static readonly FoundryModel GptAudioMini = new() { Name = "gpt-audio-mini", Version = "2025-10-06", Format = "OpenAI" };
        public static readonly FoundryModel GptChatLatest = new() { Name = "gpt-chat-latest", Version = "2026-08-06", Format = "OpenAI" };
        public static readonly FoundryModel GptImage1Mini = new() { Name = "gpt-image-1-mini", Version = "2025-10-06", Format = "OpenAI" };
        public static readonly FoundryModel GptImage15 = new() { Name = "gpt-image-1.5", Version = "2025-12-16", Format = "OpenAI" };
        public static readonly FoundryModel GptImage2 = new() { Name = "gpt-image-2", Version = "2026-04-21", Format = "OpenAI" };
        public static readonly FoundryModel GptImage25Flare = new() { Name = "gpt-image-2.5-flare", Version = "2026-09-08", Format = "OpenAI" };
        public static readonly FoundryModel GptImage25Sunburst = new() { Name = "gpt-image-2.5-sunburst", Version = "2026-09-08", Format = "OpenAI" };
        public static readonly FoundryModel GptLive1 = new() { Name = "gpt-live-1", Version = "2026-09-10", Format = "OpenAI" };
        public static readonly FoundryModel GptLiveTranscribe = new() { Name = "gpt-live-transcribe", Version = "2026-07-28", Format = "OpenAI" };
        public static readonly FoundryModel GptOss120b = new() { Name = "gpt-oss-120b", Version = "4", Format = "OpenAI" };
        public static readonly FoundryModel GptOss20b = new() { Name = "gpt-oss-20b", Version = "11", Format = "OpenAI" };
        public static readonly FoundryModel GptRealtime = new() { Name = "gpt-realtime", Version = "2025-08-28", Format = "OpenAI" };
        public static readonly FoundryModel GptRealtime15 = new() { Name = "gpt-realtime-1.5", Version = "2026-02-23", Format = "OpenAI" };
        public static readonly FoundryModel GptRealtime2 = new() { Name = "gpt-realtime-2", Version = "2026-05-07", Format = "OpenAI" };
        public static readonly FoundryModel GptRealtime21 = new() { Name = "gpt-realtime-2.1", Version = "2026-07-07", Format = "OpenAI" };
        public static readonly FoundryModel GptRealtime21Mini = new() { Name = "gpt-realtime-2.1-mini", Version = "2026-07-07", Format = "OpenAI" };
        public static readonly FoundryModel GptRealtimeMini = new() { Name = "gpt-realtime-mini", Version = "2025-12-15", Format = "OpenAI" };
        public static readonly FoundryModel GptRealtimeTranslate = new() { Name = "gpt-realtime-translate", Version = "2026-05-07", Format = "OpenAI" };
        public static readonly FoundryModel GptRealtimeWhisper = new() { Name = "gpt-realtime-whisper", Version = "2026-05-07", Format = "OpenAI" };
        public static readonly FoundryModel GptTranscribe = new() { Name = "gpt-transcribe", Version = "2026-07-28", Format = "OpenAI" };
        public static readonly FoundryModel O1 = new() { Name = "o1", Version = "2024-12-17", Format = "OpenAI" };
        public static readonly FoundryModel O3 = new() { Name = "o3", Version = "2025-04-16", Format = "OpenAI" };
        public static readonly FoundryModel O3DeepResearch = new() { Name = "o3-deep-research", Version = "2025-06-26", Format = "OpenAI" };
        public static readonly FoundryModel O3Mini = new() { Name = "o3-mini", Version = "2025-01-31", Format = "OpenAI" };
        public static readonly FoundryModel O3Pro = new() { Name = "o3-pro", Version = "2025-06-10", Format = "OpenAI" };
        public static readonly FoundryModel O4Mini = new() { Name = "o4-mini", Version = "2025-04-16", Format = "OpenAI" };
        public static readonly FoundryModel TextEmbedding3Large = new() { Name = "text-embedding-3-large", Version = "1", Format = "OpenAI" };
        public static readonly FoundryModel TextEmbedding3Small = new() { Name = "text-embedding-3-small", Version = "1", Format = "OpenAI" };
        public static readonly FoundryModel TextEmbeddingAda002 = new() { Name = "text-embedding-ada-002", Version = "2", Format = "OpenAI" };
        public static readonly FoundryModel Tts = new() { Name = "tts", Version = "001", Format = "OpenAI" };
        public static readonly FoundryModel TtsHd = new() { Name = "tts-hd", Version = "001", Format = "OpenAI" };
        public static readonly FoundryModel Whisper = new() { Name = "whisper", Version = "001", Format = "OpenAI" };
    }

    /// <summary>StabilityAI models.</summary>
    public static class StabilityAI
    {
        public static readonly FoundryModel StableDiffusion35Large = new() { Name = "Stable-Diffusion-3.5-Large", Version = "1", Format = "StabilityAI" };
        public static readonly FoundryModel StableImageCore = new() { Name = "Stable-Image-Core", Version = "1", Format = "StabilityAI" };
        public static readonly FoundryModel StableImageUltra = new() { Name = "Stable-Image-Ultra", Version = "1", Format = "StabilityAI" };
    }

    /// <summary>XAI models.</summary>
    public static class XAI
    {
        public static readonly FoundryModel Grok4 = new() { Name = "grok-4", Version = "1", Format = "xAI" };
        public static readonly FoundryModel Grok41FastNonReasoning = new() { Name = "grok-4-1-fast-non-reasoning", Version = "1", Format = "xAI" };
        public static readonly FoundryModel Grok41FastReasoning = new() { Name = "grok-4-1-fast-reasoning", Version = "1", Format = "xAI" };
        public static readonly FoundryModel Grok420NonReasoning = new() { Name = "grok-4-20-non-reasoning", Version = "1", Format = "xAI" };
        public static readonly FoundryModel Grok420Reasoning = new() { Name = "grok-4-20-reasoning", Version = "1", Format = "xAI" };
        public static readonly FoundryModel Grok43 = new() { Name = "grok-4.3", Version = "1", Format = "xAI" };
        public static readonly FoundryModel Grok46 = new() { Name = "grok-4.6", Version = "1", Format = "xAI" };
        public static readonly FoundryModel GrokCodeFast1 = new() { Name = "grok-code-fast-1", Version = "1", Format = "xAI" };
    }
}
