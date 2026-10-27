using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// The connector as Pipeline commands. Pipeline owns the listener, the bearer token and CORS.
    /// </summary>
    static class FigmaPipelineCommands
    {
        const string k_Tag = "figma";

        // A staged fill is one rasterized Figma layer; anything larger is a mistake or an attempt to fill
        // the disk, and Pipeline's own body cap is well above this.
        const int k_MaxStagedImageBytes = 16 * 1024 * 1024;

        static bool IsPng(byte[] b)
        {
            return b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
                && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A;
        }

        [CliCommand("figma_status", "Identify this Editor to the Figma plugin: project name and a stable project id.",
            Tags = new[] { k_Tag })]
        public static object Status()
        {
            var identity = FigmaConnectorService.Identity(FigmaPipelineConnector.ProjectRoot());
            return new
            {
                figmaConnector = true,
                version = FigmaPipelineConnector.Version,
                // The scalar keeps working for a plugin that predates the set.
                wireSchema = FigmaImportPipeline.ReadableSchemas[0],
                wireSchemas = FigmaImportPipeline.ReadableSchemas,
                project = identity.name,
                projectId = identity.id
            };
        }

        [CliCommand("figma_import", "Import a Figma wire payload as UXML/USS under Assets/Figma.",
            Tags = new[] { k_Tag })]
        public static object Import(
            [CliArg("payload", "The wire payload, as a JSON object.", Required = true)] JObject payload)
        {
            if (payload == null)
                throw new ArgumentException("payload is required");

            // JSON in and out, never a string: escaping it again in the request body would roughly double
            // it against Pipeline's size cap, and nest escaped JSON inside `result` on the way back.
            var json = payload.ToString(Formatting.None);
            FigmaConnectorReplay.Capture(json);
            return JToken.Parse(FigmaConnectorService.Import(json));
        }

        [CliCommand("figma_stage_image", "Stage a rasterized Figma fill as PNG; the next import promotes it into the project.",
            // Plain file IO, no Unity API: off the main thread so large uploads do not queue behind it.
            MainThreadRequired = false, Tags = new[] { k_Tag })]
        public static object StageImage(
            [CliArg("image_key", "The key to stage the fill under; the plugin derives it from the image bytes, so identical artwork shares one.", Required = true)] string imageKey,
            [CliArg("png_base64", "The PNG bytes, base64 encoded.", Required = true)] string pngBase64)
        {
            if (string.IsNullOrEmpty(imageKey))
                throw new ArgumentException("image_key is required");

            // Bounded before decoding, so an oversized payload costs a length check rather than the
            // allocation. Base64 inflates by about a third, hence the 4/3.
            if ((pngBase64 != null ? pngBase64.Length : 0) > k_MaxStagedImageBytes * 4L / 3L + 4)
                throw new ArgumentException("png_base64 exceeds " + k_MaxStagedImageBytes + " bytes decoded");

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(pngBase64 ?? string.Empty);
            }
            catch (FormatException e)
            {
                throw new ArgumentException("png_base64 is not valid base64: " + e.Message);
            }
            if (bytes.Length == 0)
                throw new ArgumentException("png_base64 is empty");
            if (bytes.Length > k_MaxStagedImageBytes)
                throw new ArgumentException("png_base64 exceeds " + k_MaxStagedImageBytes + " bytes decoded");
            if (!IsPng(bytes))
                throw new ArgumentException("png_base64 is not a PNG");

            return new { ok = FigmaConnectorService.StageImage(imageKey, bytes), bytes = bytes.Length };
        }

        [CliCommand("figma_commands", "Drain the write-back queues: token and property edits made in Unity, plus the tracked screen roots.",
            Tags = new[] { k_Tag })]
        public static object Commands()
        {
            return FigmaConnectorService.Drain();
        }

        [CliCommand("figma_ack", "Acknowledge applied write-back changes. Unacked changes stay queued and are retried.",
            Tags = new[] { k_Tag })]
        public static object Ack(
            [CliArg("token_keys", "Keys of the applied token changes.")] string[] tokenKeys = null,
            [CliArg("prop_keys", "Keys of the applied property changes.")] string[] propKeys = null)
        {
            FigmaConnectorService.Ack(tokenKeys, propKeys);
            return new { ok = true };
        }
    }
}
