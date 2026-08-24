using CommunityToolkit.Mvvm.ComponentModel;

namespace WpfCompanyApp.Models
{
    public partial class PickOffsetSetting : ObservableObject
    {
        public const float MinDelta = -10f;
        public const float MaxDelta = 10f;

        private float _deltaX;
        private float _deltaY;

        public int Basket { get; set; }
        public int Tool { get; set; }
        public bool IsGreaterThanOrEqualPickX { get; set; }

        public string ToolName => $"Tool{Tool}";
        public string XCondition => IsGreaterThanOrEqualPickX
            ? "X >= PickProductPose.X"
            : "X < PickProductPose.X";

        public float DeltaX
        {
            get => _deltaX;
            set => SetProperty(ref _deltaX, ClampDelta(value));
        }

        public float DeltaY
        {
            get => _deltaY;
            set => SetProperty(ref _deltaY, ClampDelta(value));
        }

        private static float ClampDelta(float value)
        {
            if (float.IsNaN(value))
                return 0f;

            if (value < MinDelta)
                return MinDelta;

            return value > MaxDelta ? MaxDelta : value;
        }

        public string SettingKey =>
            $"PickOffset_Basket{Basket}_Tool{Tool}_{(IsGreaterThanOrEqualPickX ? "XGePick" : "XLtPick")}";
    }
}
