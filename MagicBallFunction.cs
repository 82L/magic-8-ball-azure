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

namespace MagicBall.Function
{
    public class MagicBallFunction
    {


        [FunctionName("MagicBallFunction")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", "put", Route = null)] HttpRequest req, ILogger log)
        {
            log.LogInformation("Traitement de la requête Speech to Text.");

            var exceptions = new List<Exception>();
            string speechSubscriptionKey = Environment.GetEnvironmentVariable("SPEECH_KEY");
            if (speechSubscriptionKey == null)
            {
                log.LogInformation("No Speech Key");
                return new NoContentResult();
            }
            string serviceRegion = "francecentral";
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

            using AudioConfig audioConfig = AudioConfig.FromWavFileInput(tempFilePath);
            var speechConfig = SpeechConfig.FromSubscription(speechSubscriptionKey, serviceRegion);
            speechConfig.SpeechRecognitionLanguage = "fr-FR";

            // Créer un objet SpeechRecognizer
            using (var recognizer = new SpeechRecognizer(speechConfig, audioConfig))
            {
                var result = await recognizer.RecognizeOnceAsync();

                if (result.Reason == ResultReason.RecognizedSpeech)
                {
                    return new OkObjectResult(result.Text);
                }
                else if (result.Reason == ResultReason.NoMatch)
                {
                    return new BadRequestObjectResult("Aucune parole reconnue.");
                }
                else
                {
                    return new BadRequestObjectResult($"Erreur de reconnaissance : {result.Reason}");
                }
            }

        }
    }
}
