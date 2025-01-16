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
            "C'est certain.",
            "C'est décidément le cas.",
            "Sans aucun doute.",
            "Oui définitivement.",
            "Tu peux compter dessus.",
            "Comme je le vois, oui.",
            "Probablement.",
            "Les perspectives sont bonnes.",
            "Oui.",
            "Les signes indiquent oui.",
            "Réponse floue, essayez à nouveau.",
            "Repose la question plus tard.",
            "Mieux vaut ne pas te le dire maintenant.",
            "Impossible de prédire maintenant.",
            "Concentre-toi et redemande.",
            "Ne compte pas dessus.",
            "Ma réponse est non.",
            "Mes sources disent que non.",
            "Les perspectives ne sont pas très bonnes.",
            "Très peu probable.",
        ];


        [FunctionName("MagicBallAsk")]
        public static async Task<IActionResult> MagicBallAsk([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req, ILogger log)
        {
         

            string speechSubscriptionKey = Environment.GetEnvironmentVariable("SPEECH_KEY");
            string openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            string openAiApiEndpoint = Environment.GetEnvironmentVariable("OPENAI_API_ENDPOINT");
            string aiInstructions= Environment.GetEnvironmentVariable("AI_INSTRUCTIONS");
            string serviceRegion = "francecentral";
            log.LogInformation("Testing Env Variables.");
            if (string.IsNullOrEmpty(speechSubscriptionKey) || string.IsNullOrEmpty(openAiApiKey) 
                                                            || string.IsNullOrEmpty(openAiApiEndpoint) || string.IsNullOrEmpty(aiInstructions))
            {
                log.LogError("Missing required API keys.");
                return new NoContentResult();
            }
            log.LogInformation("Getting audiofile.");
            Stream l_stream = req.Body;
            
            if (l_stream == null || l_stream.Length == 0)
            {
                log.LogError("Missing AudioFile");
                return new BadRequestObjectResult("Missing AudioFile");
            }
            try
            {
                log.LogInformation("Saving audio file int temp path.");
                var tempFilePath = Path.Combine(Path.GetTempPath(), "audioFile.wav");
                await using (var fileStream = new FileStream(tempFilePath, FileMode.Create))
                {
                    await l_stream.CopyToAsync(fileStream);
                }

                var speechConfig = SpeechConfig.FromSubscription(speechSubscriptionKey, serviceRegion);
                speechConfig.SpeechRecognitionLanguage = "fr-FR";
                speechConfig.SpeechSynthesisVoiceName = "en-US-OnyxTurboMultilingualNeural";


                bool skipOpenAi = false;
                string aiResponse = "Voix non reconnue.";
                log.LogInformation("Getting voice text.");
           
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
                    aiResponse = GetAIResponse(recognizedText, aiInstructions, openAiApiKey, openAiApiEndpoint, log);
                }

                log.LogInformation($"Ai reponse :{aiResponse}");
                // Convert AI response to speech

                
                return new OkObjectResult(aiResponse);
               

                // Return the audio file
             
            }
            catch (Exception ex)
            {
                log.LogError($"Exception : {ex}");
                return new BadRequestObjectResult("Error processing request.");
            }
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

        [FunctionName("MagicBallSpeech")]
        public static async Task<IActionResult> MagicBallSpeech([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req, ILogger log)
        {
            string speechSubscriptionKey = Environment.GetEnvironmentVariable("SPEECH_KEY");
            string serviceRegion = "francecentral";
            if (string.IsNullOrEmpty(speechSubscriptionKey))
            {
                log.LogError("Missing required API keys.");
                return new NoContentResult();
            }

            string textToSpeak = await new StreamReader(req.Body).ReadToEndAsync();;
            
            log.LogInformation($"Getting text : {textToSpeak}.");
            if (string.IsNullOrEmpty(textToSpeak))
            {
                log.LogError("Missing text for TTS.");
                return new BadRequestObjectResult("Missing text for TTS.");
            }
            
            try
            {
                var speechConfig = SpeechConfig.FromSubscription(speechSubscriptionKey, serviceRegion);
                speechConfig.SpeechSynthesisVoiceName = "en-US-OnyxTurboMultilingualNeural";
                using var synthesizer = new SpeechSynthesizer(speechConfig);

                using var audioStream = AudioOutputStream.CreatePullStream();
                using var audioConfig = AudioConfig.FromStreamOutput(audioStream);
                var synthesisTask  =  synthesizer.SpeakTextAsync(textToSpeak);
                
                // if (synthesisTask.AudioData == null)
                // {
                //     return new BadRequestObjectResult("Error generating audio from AI response.");
                // }
                //
                // return new FileContentResult(synthesisTask.AudioData, "audio/wav")
                // {
                //     FileDownloadName = "response.wav"
                // };

                // var response = req.HttpContext.Response;
                // response.ContentType = "audio/wav";
                // response.Headers["Transfer-Encoding"] = "chunked";
                // byte[] buffer = new byte[4096];
                // uint bytesRead;
                // Stream audio to the response as it is generated
                // response.StatusCode = 200;
                // log.LogInformation($"Before Buffer while");
                // while ((bytesRead = audioStream.Read(buffer)) > 0)
                // {
                //     log.LogInformation($"Read {bytesRead} bytes.");
                //     await response.Body.WriteAsync(buffer, 0, (int)bytesRead);
                //     await response.Body.FlushAsync();
                // }
                
                log.LogInformation($"After buffer while");
                // Stream audio to the response
                await synthesisTask;

                return new FileContentResult(synthesisTask.Result.AudioData, "audio/wav")
                {
                    FileDownloadName = "response.wav"
                };
                return new OkResult();
            }
            catch (Exception ex)
            {
  
                log.LogError($"Exception: {ex}");
                return new BadRequestObjectResult("Error processing request.");
            }
            
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