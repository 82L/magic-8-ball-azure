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
            using (var audioStream = new MemoryStream())
            {
                await req.Body.CopyToAsync(audioStream);
                audioStream.Position = 0;

                // Créer une configuration de parole
                var speechConfig = SpeechConfig.FromSubscription(speechSubscriptionKey, serviceRegion);
                speechConfig.SpeechRecognitionLanguage = "en-US";
                // Créer un flux d'entrée audio à partir du stream
                var pushStream = AudioInputStream.CreatePushStream();
                var audioConfig = AudioConfig.FromStreamInput(pushStream);

                // Pousser le contenu du stream dans le PushAudioInputStream
                pushStream.Write(audioStream.ToArray());
                pushStream.Close();

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
}
