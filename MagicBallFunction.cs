using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using System.Linq;
using System.Security.Cryptography;

namespace MagicBall.Function
{
    public class MagicBallFunction
    {
        
        private static readonly string[] MagicBallAnswers = [
            "It is certain.",
            "It is decidedly so.",
            "Without a doubt.",
            "Yes definitely.",
            "You may rely on it.",
            "As I see it, yes.",
            "Most likely.",
            "Outlook good.",
            "Yes",
            "Signs point to yes.",
            "Reply hazy, try again.",
            "Ask again later.",
            "Better not tell you now.",
            "Cannot predict now.",
            "Concentrate and ask again.",
            "Don't count on it.",
            "My reply is no.",
            "My sources say no.",
            "Outlook not so good.",
            "Very doubtful.",
        ];


        [FunctionName("MagicBallFunction")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", "put", Route = null)] HttpRequest req, ILogger log)
        {
            log.LogInformation("Processing Speech-to-Text request.");

            string speechSubscriptionKey = Environment.GetEnvironmentVariable("SPEECH_KEY");
            string openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            string openAiApiEndpoint = Environment.GetEnvironmentVariable("OPENAI_API_ENDPOINT");
            string aiInstructions= Environment.GetEnvironmentVariable("AI_INSTRUCTIONS");
            string serviceRegion = "francecentral";

            if (string.IsNullOrEmpty(speechSubscriptionKey) || string.IsNullOrEmpty(openAiApiKey) 
            || string.IsNullOrEmpty(openAiApiEndpoint) || string.IsNullOrEmpty(aiInstructions))
            {
                log.LogError("Missing required API keys.");
                return new NoContentResult();
            }


            var file = req.Form.Files.GetFile("audioFile");
            if (file == null || file.Length == 0)
            {
                return new BadRequestObjectResult("Missing AudioFile");
            }

            var tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetFileName(file.FileName));
            using (var fileStream = new FileStream(tempFilePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            var speechConfig = SpeechConfig.FromSubscription(speechSubscriptionKey, serviceRegion);
            speechConfig.SpeechRecognitionLanguage = "fr-FR";
            speechConfig.SpeechSynthesisVoiceName = "en-US-OnyxTurboMultilingualNeural";


            bool skipOpenAi = false;
            string aiResponse = "Voix non reconnue.";

            string recognizedText = await SpeechToText(tempFilePath, speechConfig);
            if (string.IsNullOrEmpty(recognizedText))
            {
                aiResponse = "Voix non reconnue.";
                log.LogInformation("Voix non reconnue !");
                skipOpenAi = true;
            }

            log.LogInformation($"Recognized Text: {recognizedText}");
            if (!skipOpenAi)
            {
                log.LogInformation("Trying to get Ai reponse");
                aiResponse = GetAIResponse(recognizedText,aiInstructions, openAiApiKey, openAiApiEndpoint, log);
            }

            log.LogInformation($"Ai reponse :{aiResponse}");
            // Convert AI response to speech


            var audioBytes = await TextToSpeech(aiResponse, speechConfig);

            if (audioBytes == null)
            {
                return new BadRequestObjectResult("Error generating audio from AI response.");
            }

            // Return the audio file
            return new FileContentResult(audioBytes, "audio/wav")
            {
                FileDownloadName = "response.wav"
            };
        }
        private static async Task<string> SpeechToText(string tempFilePath, SpeechConfig speechConfig)
        {
            using AudioConfig audioConfig = AudioConfig.FromWavFileInput(tempFilePath);

            using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);
            var recognitionResult = await recognizer.RecognizeOnceAsync();
            if (recognitionResult.Reason != ResultReason.RecognizedSpeech)
            {
                return string.Empty;
            }
            return recognitionResult.Text;
        }

        private static async Task<byte[]> TextToSpeech(string text, SpeechConfig speechConfig)
        {
            using var synthesizer = new SpeechSynthesizer(speechConfig);
            using var stream = AudioOutputStream.CreatePullStream();
            speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm);
            var synthesisResult = await synthesizer.SpeakTextAsync(text);

            if (synthesisResult.Reason != ResultReason.SynthesizingAudioCompleted)
            {
                return null;
            }
            return synthesisResult.AudioData;

        }

        private static string GetAIResponse(string prompt, string aiInstructions, string key, string endpoint, ILogger log)
        {
            int reponseNumber = RandomNumberGenerator.GetInt32(0, MagicBallAnswers.Length);
            string aiInstructionsText = aiInstructions.Replace("<answer>", MagicBallAnswers[reponseNumber]);
            log.LogInformation("Creating Key");
            AzureKeyCredential credential = new(key);
            log.LogInformation($"Creating Client {endpoint}" );
            AzureOpenAIClient azureClient = new(new Uri(endpoint), credential);
            log.LogInformation("Getting Client");
            ChatClient chatClient = azureClient.GetChatClient("gpt-35-turbo-16k");
            log.LogInformation($"Created client {prompt}, {endpoint}");
            ChatCompletion completion = chatClient.CompleteChat(
                new ChatMessage[]
                {
                new SystemChatMessage(
                    aiInstructionsText ),
                new UserChatMessage(prompt)
                }
            );
            log.LogInformation("messageSent");
            return completion.Content.Last().Text;
        }
    }
}
