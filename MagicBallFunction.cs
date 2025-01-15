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

namespace MagicBall.Function
{
    public class MagicBallFunction
    {


        [FunctionName("MagicBallFunction")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", "put", Route = null)] HttpRequest req, ILogger log)
        {
            var timeStamp = DateTime.UtcNow;
            log.LogInformation(timeStamp + ": Processing Speech-to-Text request.");

            string speechSubscriptionKey = Environment.GetEnvironmentVariable("SPEECH_KEY");
            string openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            string serviceRegion = "francecentral";

            if (string.IsNullOrEmpty(speechSubscriptionKey) || string.IsNullOrEmpty(openAiApiKey))
            {
                log.LogError(timeStamp + ": Missing required API keys.");
                return new NoContentResult();
            }


            var file = req.Form.Files.GetFile("audioFile");
            if (file == null || file.Length == 0)
            {
                return new BadRequestObjectResult(timeStamp + ": Missing AudioFile");
            }

            var tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetFileName(file.FileName));
            using (var fileStream = new FileStream(tempFilePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            using AudioConfig audioConfig = AudioConfig.FromWavFileInput(tempFilePath);
            var speechConfig = SpeechConfig.FromSubscription(speechSubscriptionKey, serviceRegion);
            speechConfig.SpeechRecognitionLanguage = "fr-FR";
            // speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm);

            using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);
            var recognitionResult = await recognizer.RecognizeOnceAsync();

            // Créer un objet SpeechRecognizer

            bool skipOpenAi = false;
            string aiResponse = "Voix non reconnue.";

            if (recognitionResult.Reason != ResultReason.RecognizedSpeech)
            {
                log.LogError(timeStamp + ": Voix non reconnue.");
                aiResponse = "Voix non reconnue.";
                skipOpenAi = true;
            }


            string recognizedText = recognitionResult.Text;
            log.LogInformation(timeStamp + $": Recognized Text: {recognizedText}");
            if (!skipOpenAi)
            {


                // Send recognized text to OpenAI API
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", openAiApiKey);

                var openAiRequest = new
                {
                    model = "gpt-3.5-turbo",
                    messages = new[]
                    {
                            new { role = "system", content = "You are a seer able to see the future, with an existential dread about it because of the climate change and the incoming apocalypse caused by it. You must speak in french." },
                            new { role = "user", content = recognizedText }
                        },
                    temperature = 0.7
                };

                var openAiResponse = await httpClient.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", openAiRequest);
                if (!openAiResponse.IsSuccessStatusCode)
                {
                    log.LogError(timeStamp + ": Open AI ne fonctionne pas");
                    aiResponse = "La boule magique est cassée";
                }
                else
                {
                    var openAiResponseContent = await openAiResponse.Content.ReadAsStringAsync();
                    var openAiResult = JsonSerializer.Deserialize<JsonElement>(openAiResponseContent);
                    aiResponse = openAiResult.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                    log.LogInformation(timeStamp + $": AI Response: {aiResponse}");

                }


            }
            // Convert AI response to speech
            using var synthesizer = new SpeechSynthesizer(speechConfig);
            using var stream = AudioOutputStream.CreatePullStream();
            speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm);

            var synthesisResult = await synthesizer.SpeakTextAsync(aiResponse);
            if (synthesisResult.Reason != ResultReason.SynthesizingAudioCompleted)
            {
                return new BadRequestObjectResult(timeStamp + ": Error generating audio from AI response.");
            }
            var audioBytes = synthesisResult.AudioData;

            // Return the audio file
            return new FileContentResult(audioBytes, "audio/wav")
            {
                FileDownloadName = "response.wav"
            };
        }
    }
}
