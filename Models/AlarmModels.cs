using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace WpfTest.Models
{
    /// <summary>
    /// 报警记录模型：
    /// MsgTime 为报警触发时的实时时间，RemoveTime 为修复（取消报警）时的实时时间，
    /// RemoveTime 为 null 表示该报警仍在处理中。
    /// </summary>
    public partial class AlarmModels : ObservableObject
    {
        /// <summary>数据库自增 ID（未入库时为 null）</summary>
        public int? DbId { get; set; }

        /// <summary>序号（全局递增）</summary>
        public int Index { get; set; }

        /// <summary>设备编号</summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>报警项：压力 / 温度 / 湿度 / 流量</summary>
        public string ItemName { get; set; } = string.Empty;

        /// <summary>报警提示，如：压力值过高（当前 1.72MPa）</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>报警级别：紧急 / 一般</summary>
        public string MesType { get; set; } = string.Empty;

        /// <summary>报警触发实时时间</summary>
        public DateTime MsgTime { get; set; }

        /// <summary>修复（取消报警）实时时间；null 表示处理中</summary>
        private DateTime? _removeTime;
        public DateTime? RemoveTime
        {
            get => _removeTime;
            set => SetProperty(ref _removeTime, value);
        }
    }
}
