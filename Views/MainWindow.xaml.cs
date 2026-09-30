using System.Windows;
using WpfTest.ViewModels;

namespace WpfTest.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel viewModel;

        public MainWindow()
        {
            InitializeComponent();
            viewModel = new MainViewModel();
            this.DataContext = viewModel;

            //窗口关闭时释放 Modbus 串口资源
            this.Closed += (s, e) => viewModel.Cleanup();
        }
    }
}
