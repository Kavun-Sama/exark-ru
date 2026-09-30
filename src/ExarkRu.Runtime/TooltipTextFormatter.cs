namespace ExarkRu.Runtime
{
    internal sealed class TooltipTextFormatter
    {
        private const string TriggeredSprites =
            "<sprite name=\"triggered0\"><sprite name=\"triggered1\"><sprite name=\"triggered2\">: ";
        private const string PassiveSprites =
            "<sprite name=\"passive0\"><sprite name=\"passive1\"><sprite name=\"passive2\">: ";

        private readonly string triggeredLabel;
        private readonly string passiveLabel;

        public TooltipTextFormatter(string triggeredLabel, string passiveLabel)
        {
            this.triggeredLabel = triggeredLabel;
            this.passiveLabel = passiveLabel;
        }

        public string Format(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source;
            }

            return source.Replace(TriggeredSprites, triggeredLabel)
                .Replace(PassiveSprites, passiveLabel);
        }
    }
}
