using CommunityToolkit.Mvvm.ComponentModel;

namespace WpfCompanyApp.Models
{
    public partial class PickOffsetSetting : ObservableObject
    {
        public int Basket { get; set; }
        public int Tool { get; set; }
        public bool IsGreaterThanOrEqualPickX { get; set; }

        public string ToolName => $"Tool{Tool}";
        public string XCondition => IsGreaterThanOrEqualPickX
            ? "X >= PickProductPose.X"
            : "X < PickProductPose.X";

        [ObservableProperty] private float deltaX;
        [ObservableProperty] private float deltaY;

        public string SettingKey =>
            $"PickOffset_Basket{Basket}_Tool{Tool}_{(IsGreaterThanOrEqualPickX ? "XGePick" : "XLtPick")}";
    }
}
