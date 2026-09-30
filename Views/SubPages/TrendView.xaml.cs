using System.Windows.Controls;

namespace WpfTest.Views.SubPages
{
    /// <summary>
    /// TrendView.xaml 的交互逻辑
    /// 说明：趋势页数据直接绑定主视图模型(MainViewModel)的共享数据源，
    /// 无需在此创建视图模型实例。
    /// </summary>
    public partial class TrendView : UserControl
    {
        public TrendView()
        {
            InitializeComponent();
        }
    }
}
