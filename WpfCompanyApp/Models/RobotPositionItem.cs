using CommunityToolkit.Mvvm.ComponentModel;

namespace WpfCompanyApp.Models
{
    public partial class RobotPositionItem : ObservableObject
    {
        [ObservableProperty]
        private int positionId;

        [ObservableProperty]
        private string positionName = "";
    }
    public class TriggerPosItem
    {
        public int Id { get; set; }
        public PosMoveL PosMoveL { get; set; }
        public bool IsStatus { get; set; }  // false = chưa save, true = đã save
    }
}
