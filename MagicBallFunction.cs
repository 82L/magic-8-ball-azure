using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace MagicBall.Function
{
    public class MagicBallFunction
    {
        [FunctionName("MagicBallFunction")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req, ILogger log)
        {
            var timeStamp = DateTime.UtcNow;
            log.LogInformation(timeStamp + ": Processing Speech-to-Text request.");

            string speechSubscriptionKey = Environment.GetEnvironmentVariable("SPEECH_KEY");
            string openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            string openAiEndpoint = Environment.GetEnvironmentVariable("OPENAI_ENDPOINT");
            string deploymentName = Environment.GetEnvironmentVariable("OPENAI_DEPLOYMENT");

            string serviceRegion = "francecentral";

            if (string.IsNullOrEmpty(speechSubscriptionKey) || string.IsNullOrEmpty(openAiKey) || string.IsNullOrEmpty(openAiEndpoint) || string.IsNullOrEmpty(deploymentName))
            {
                log.LogError(timeStamp + ": Missing required API keys.");
                return new StatusCodeResult(503);
            }


            var file = req.Form.Files.GetFile("audioFile");
            if (file == null || file.Length == 0)
            {
                return new BadRequestObjectResult(timeStamp + ": Missing AudioFile");
            }

            string tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetFileName(file.FileName));
            using (var fileStream = new FileStream(tempFilePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            bool skipAi = false;
            string aiResponse = "";
            try
            {
                // Speech-to-Text
                string userText = await ConvertSpeechToText(tempFilePath, speechSubscriptionKey, serviceRegion);
                log.LogInformation($"Recognized text: {userText}");
                if (string.IsNullOrEmpty(userText))
                {
                    aiResponse = "Je ne vous ai pas bien entendu";
                    skipAi = true;
                }

                if (!skipAi)
                {
                    // Generate response using Azure OpenAI
                    aiResponse = await GetChatResponse(openAiKey, openAiEndpoint, deploymentName, userText);
                    if(string.IsNullOrEmpty(aiResponse)){
                        aiResponse = "La boule magique est cassée";
                    }
                    log.LogInformation($"AI Response: {aiResponse}");
                }

                // Text-to-Speech
                string audioResponsePath = await ConvertTextToSpeech(aiResponse, speechSubscriptionKey, serviceRegion);
                log.LogInformation($"Audio response generated: {audioResponsePath}");

                // Return the audio response
                byte[] audioBytes = await File.ReadAllBytesAsync(audioResponsePath);
                return new FileContentResult(audioBytes, "audio/wav")
                {
                    FileDownloadName = "response.wav"
                };
            }
            catch (Exception ex)
            {
                log.LogError($"Error processing request: {ex.Message}");
                return new StatusCodeResult(500);
            }
        }

        private static async Task<string> ConvertSpeechToText(string filePath, string speechKey, string region)
        {
            var speechConfig = SpeechConfig.FromSubscription(speechKey, region);
            speechConfig.SpeechSynthesisLanguage = "fr-FR";
            using var audioConfig = AudioConfig.FromWavFileInput(filePath);
            using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

            var result = await recognizer.RecognizeOnceAsync();
            if (result.Reason == ResultReason.RecognizedSpeech)
            {
                return result.Text;
            }
            else if (result.Reason == ResultReason.NoMatch)
            {
                // throw new Exception("No speech recognized.");
            }
            else if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = CancellationDetails.FromResult(result);
                // throw new Exception($"Speech recognition canceled: {cancellation.ErrorDetails}");
            }

            return string.Empty;
        }

        private static async Task<string> ConvertTextToSpeech(string text, string speechKey, string region)
        {
            var speechConfig = SpeechConfig.FromSubscription(speechKey, region);
            speechConfig.SpeechSynthesisLanguage = "fr-FR";
            speechConfig.SpeechSynthesisVoiceName = "fr-FR-DeniseNeural"; // Specify the voice

            string tempAudioPath = Path.Combine(Path.GetTempPath(), "response.wav");
            using var synthesizer = new SpeechSynthesizer(speechConfig, AudioConfig.FromWavFileOutput(tempAudioPath));
            var result = await synthesizer.SpeakTextAsync(text);

            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                return tempAudioPath;
            }
            else if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                throw new Exception($"Text-to-Speech canceled: {cancellation.ErrorDetails}");
            }

            return string.Empty;
        }

        private static async Task<string> GetChatResponse(string apiKey, string endpoint, string deployment, string prompt)
        {
            AzureKeyCredential credential = new(apiKey);
            AzureOpenAIClient azureClient = new(new Uri(endpoint), credential);
            ChatClient chatClient = azureClient.GetChatClient(deployment);

            ChatCompletion completion = await chatClient.CompleteChatAsync(
                new ChatMessage[]
                {
                new SystemChatMessage(
                    "You are a seer able to see the future, with an existential dread about it because of the climate change and the incoming apocalypse caused by it. You must speak in french. All your responses must point toward the end of the world in dramatic twist."),
                new UserChatMessage(prompt)
                });
        
            return completion.Content.ToString();
        }
    }
}
