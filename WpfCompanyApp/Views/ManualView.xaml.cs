using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using WpfCompanyApp.Services;
using WpfCompanyApp.ViewModels;

namespace WpfCompanyApp.Views
{
    public partial class ManualView : UserControl
    {
        // ✅ Constructor không tham số – XAML sẽ gọi cái này
        //  public ManualView() : this(App.Services.GetRequiredService<ManualViewModel>())
        //  {
        //  }

        ////  Constructor thật dùng DI
        //  public ManualView(ManualViewModel vm)
        //  {
        //      InitializeComponent();
        //      DataContext = vm;
        //  }
        // LẤY ĐÚNG INSTANCE AppDataService đã đăng ký Singleton
        public ManualView()
        {
            InitializeComponent();

            //// LẤY ĐÚNG INSTANCE AppDataService đã đăng ký Singleton
            //var data = App.Services.GetRequiredService<AppDataService>();
            //DataContext = data;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {

        }
    }
    
}
