using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WpfTest.ViewModels
{
    /// <summary>
    /// 报警页 ViewModel：报警列表展示 + 报警操作
    /// AlarmList 和 AlarmCount 保留在 MainViewModel（监控页和报警页共用）
    /// </summary>
    public partial class AlarmViewModel : ObservableObject
    {
        private readonly MainViewModel _main;

        public AlarmViewModel(MainViewModel main)
        {
            _main = main;
        }

        /// <summary>
        /// 清空已修复的报警记录（保留未修复的）
        /// </summary>
        [RelayCommand]
        private async Task ClearResolvedAlarmsAsync()
        {
            await _main.ClearResolvedAlarmsAsync();
        }
    }
}
