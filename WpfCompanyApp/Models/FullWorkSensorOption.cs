namespace WpfCompanyApp.Models
{
    public sealed class FullWorkSensorOption
    {
        public FullWorkSensorOption(string key, string displayName)
        {
            Key = key;
            DisplayName = displayName;
        }

        public string Key { get; }
        public string DisplayName { get; }
    }
}
